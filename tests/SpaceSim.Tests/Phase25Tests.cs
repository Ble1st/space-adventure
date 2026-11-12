using SpaceSim.Core.Combat;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using SpaceSim.Core.Weapons;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 25 (KAMPF.md 4.3, 5): Spielerkampf — Echtzeitschüsse über dieselbe Trefferregel, Waffenschächte, räumliche Gefechte im Spielersektor mit
/// Gefechts-Autopilot und gespeicherten Positionen, Übergang Linie ↔ Raum, Angriff als Verbrechen, Kopfgeld, Handelsverweigerung, Verlust des aktiven
/// Schiffs mit Landeregel, Verlust von Flottenschiffen, Dienst „rearm“, Kampfschiffe an der Werft, Überladung in der Stellung „Waffen“.
/// </summary>
public class Phase25Tests
{
    static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    static readonly SectorId Alpha = new(1);
    static readonly SectorId Nebelmark = new(9);   // herrenlos: keine Polizei, keine Stationen

    static Universe Standard(ulong? seed = null) => TestWorlds.Default(seed, fuel: false, research: false);

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    static void Place(Universe u, Ship s, SectorId sector, Vec2 pos)
    {
        if (s.Transit is not null) u.FinishTransit(s);
        s.Order = null; s.Phase = TradePhase.Idle; s.MinePhase = MinePhase.Idle;
        if (s.Sector != sector) u.MoveShip(s, sector);
        s.Position = pos;
        s.DockedAt = default;
    }

    static Ship NewShip(Universe u, string model, ShipRole role, ActorId owner, float credits = 2000f, Controller controller = Controller.Ai)
    {
        var m = u.ShipModels[model];
        var cls = u.ShipClasses[m.ClassKey];
        var yard = u.StationsById().First(s => !s.IsBuildSite);
        var ship = ShipPurchase.Create(u, u.NewShipId(), model, cls.Slots, m.Fit, role, controller, owner, yard, credits);
        u.AddShip(ship);
        return ship;
    }

    /// <summary>Spieler im Jäger „Falke“ in einem Sektor (Blick −Y = Peilung 0), Spielerüberfälle aus.</summary>
    static (Universe u, Ship me) Pilot(SectorId sector, Vec2 pos, string model = "falcon", ulong? seed = null)
    {
        var u = Standard(seed);
        u.Rules.PlayerRaids = false;
        var me = NewShip(u, model, ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        Place(u, me, sector, pos);
        PlayerFleet.Activate(u, me, default);
        return (u, me);
    }

    static Ship Raider(Universe u, SectorId sector, Vec2 pos)
    {
        var r = u.ShipsById().First(ThreatMap.IsRaider);
        Place(u, r, sector, pos);
        return r;
    }

    /// <summary>Nur Gefecht und Schiffssysteme laufen (Rest der Welt steht).</summary>
    static void Fight(Universe u, int ticks, Func<bool>? stop = null)
    {
        var combat = new CombatSystem();
        var systems = new ShipSystemsSystem();
        for (int i = 0; i < ticks; i++)
        {
            u.Tick++;
            combat.Step(u, 1);
            systems.Step(u, 1);
            if (stop?.Invoke() == true) return;
        }
    }

    static int Slot(Ship s, string module) => s.Fit.IndexOf(module);

    // ───────── Spielerschuss ─────────

    [Fact]
    public void Spielerschuss_GleicheTrefferregel_ZweiZuegeJeSchuss_Deterministisch()
    {
        var (u, me) = Pilot(Nebelmark, new Vec2(0f, 0f));
        var raider = Raider(u, Nebelmark, new Vec2(0f, -30f));   // direkt vor dem Bug
        int laser = Slot(me, "weapon_laser_s");
        var w = WeaponProfile.Of(u.Modules["weapon_laser_s"]);
        var tProfile = new TargetProfile(raider.ShieldNow, raider.Stats.Armor, 0f, Gunnery.Signature(u, raider));
        // Erwartung mit derselben Regel: zwei Gefechtseintritte ziehen je zwei Zahlen (Mut, Abwurf), dann zieht der Schuss genau zwei
        var rng = new DeterministicRng(1) { State = u.Rng.State };
        for (int i = 0; i < 4; i++) rng.NextDouble();
        var expected = Hit.Roll(rng, w, tProfile, 30f, 1f, u.Rules);
        var ev = Capture(u);
        var shot = PlayerGunnery.TryFire(u, me, laser, raider.Id, 0f);
        Assert.Equal(GunResult.Fired, shot.Result);
        Assert.Equal(expected, shot.Hit);
        Assert.Equal(rng.State, u.Rng.State);
        var e = u.Fights.Of(me.Id)!;
        Assert.Equal(EncounterCause.Assault, e.Cause);
        Assert.True(e.Spatial);   // im Spielersektor räumlich
        Assert.Equal(1, e.Shots);
        Assert.Single(ev.OfType<ShotFired>(), s => s.Shooter == me.Id && s.Target == raider.Id);
        // kein Verbrechen auf Gesetzlose
        Assert.DoesNotContain(ev, x => x is CrimeReported c && c.Actor == u.PlayerActor);
        // gleiche Welt, gleiche Eingabe → gleicher Hash
        var (u2, me2) = Pilot(Nebelmark, new Vec2(0f, 0f));
        var r2 = Raider(u2, Nebelmark, new Vec2(0f, -30f));
        PlayerGunnery.TryFire(u2, me2, laser, r2.Id, 0f);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(u2));
    }

    [Fact]
    public void Spielerschuss_Nachladen_Winkel_Reichweite()
    {
        var (u, me) = Pilot(Nebelmark, new Vec2(0f, 0f));
        var raider = Raider(u, Nebelmark, new Vec2(0f, -30f));
        int laser = Slot(me, "weapon_laser_s");
        Assert.Equal(BayState.Ready, PlayerGunnery.State(u, me, laser, 0f, raider.Id));
        Assert.Equal(GunResult.Fired, PlayerGunnery.TryFire(u, me, laser, raider.Id, 0f).Result);
        // sofort nochmal: lädt nach, nichts passiert
        ulong rng = u.Rng.State;
        int shots = u.Fights.Of(me.Id)!.Shots;
        Assert.Equal(GunResult.Reloading, PlayerGunnery.TryFire(u, me, laser, raider.Id, 0f).Result);
        Assert.Equal(BayState.Reloading, PlayerGunnery.State(u, me, laser, 0f, raider.Id));
        Assert.Equal(rng, u.Rng.State);
        Assert.Equal(shots, u.Fights.Of(me.Id)!.Shots);
        Assert.True(PlayerGunnery.ReloadProgress(u, me, laser) < 1f);
        // die Uhr läuft im Tick-Takt ab (Nachladezeit 2 s = 8 Ticks)
        Fight(u, 9);
        if (u.Fights.Of(me.Id) is not null) Assert.Equal(0f, PlayerGunnery.ReloadLeft(u, me, laser));
        // Ziel hinter dem Heck: Bugwaffe außerhalb des Winkels; zu weit weg: kein Ziel
        var (u3, me3) = Pilot(Nebelmark, new Vec2(0f, 0f));
        var behind = Raider(u3, Nebelmark, new Vec2(0f, 30f));
        Assert.Equal(BayState.OutsideArc, PlayerGunnery.State(u3, me3, laser, 0f, behind.Id));
        Assert.Equal(GunResult.OutsideArc, PlayerGunnery.TryFire(u3, me3, laser, behind.Id, 0f).Result);
        Assert.Equal(0, u3.Fights.Count);   // ein Schuss, der nicht fällt, eröffnet kein Gefecht
        behind.Position = new Vec2(0f, -500f);
        Assert.Equal(BayState.NoTarget, PlayerGunnery.State(u3, me3, laser, 0f, behind.Id));
        Assert.Equal(GunResult.OutOfRange, PlayerGunnery.TryFire(u3, me3, laser, behind.Id, 0f).Result);
    }

    [Fact]
    public void Spielerschuss_OhneLeistung_OhneMunition_SchweigtOhneZustandsaenderung()
    {
        var (u, me) = Pilot(Nebelmark, new Vec2(0f, 0f));
        var raider = Raider(u, Nebelmark, new Vec2(0f, -30f));
        int gun = Slot(me, "weapon_massdriver_s");
        me.Systems.AmmoMissing = me.Stats.AmmoCapacity;   // Magazin leer
        Assert.Equal(BayState.NoAmmo, PlayerGunnery.State(u, me, gun, 0f, raider.Id));
        ulong rng = u.Rng.State;
        Assert.Equal(GunResult.NoAmmo, PlayerGunnery.TryFire(u, me, gun, raider.Id, 0f).Result);
        Assert.Equal(rng, u.Rng.State);
        Assert.Equal(0, u.Fights.Count);
        // keine Leistung: Dauerlast über der Reaktorleistung (zwei zusätzliche Laser nur für den Test), Bank leer
        me.Slots.Add(new ClassSlot { Type = "weapon", Size = SlotSize.S });
        me.Slots.Add(new ClassSlot { Type = "weapon", Size = SlotSize.S });
        me.Fit.Add("weapon_laser_s"); me.Fit.Add("weapon_laser_s");
        me.RecomputeStats(u);
        Assert.True(ShipSystems.Surplus(me.Stats) <= 0f);
        me.Systems.ChargeMissing = me.Stats.PowerStorage;
        int laser = Slot(me, "weapon_laser_s");
        Assert.Equal(BayState.NoPower, PlayerGunnery.State(u, me, laser, 0f, raider.Id));
        Assert.Equal(GunResult.NoPower, PlayerGunnery.TryFire(u, me, laser, raider.Id, 0f).Result);
        Assert.Equal(rng, u.Rng.State);
        Assert.Equal(0, u.Fights.Count);
    }

    [Fact]
    public void Ueberladung_StellungWaffen_MitSpeicherbank_SchnelleresNachladen()
    {
        var (u, me) = Pilot(Nebelmark, new Vec2(0f, 0f));
        var raider = Raider(u, Nebelmark, new Vec2(0f, -30f));
        int laser = Slot(me, "weapon_laser_s");
        var w = WeaponProfile.Of(u.Modules["weapon_laser_s"]);
        Assert.True(me.Stats.PowerStorage > 0f);   // der Jäger hat eine Speicherbank
        ShipSystems.SetMode(u, me, PowerMode.Weapons);
        float before = me.Charge;
        PlayerGunnery.TryFire(u, me, laser, raider.Id, 0f);
        Assert.Equal(w.Reload * u.Rules.OverchargeReloadFactor, PlayerGunnery.ReloadLeft(u, me, laser), 3);
        Assert.Equal(before - w.PeakDraw * (1f + u.Rules.OverchargeDrawFactor), me.Charge, 3);
        // ausgeglichen: normale Nachladezeit, nur die Spitzenlast aus der Bank
        var (u2, me2) = Pilot(Nebelmark, new Vec2(0f, 0f));
        var r2 = Raider(u2, Nebelmark, new Vec2(0f, -30f));
        float b2 = me2.Charge;
        PlayerGunnery.TryFire(u2, me2, laser, r2.Id, 0f);
        Assert.Equal(w.Reload, PlayerGunnery.ReloadLeft(u2, me2, laser), 3);
        Assert.Equal(b2 - w.PeakDraw, me2.Charge, 3);
        // ohne Bank keine Überladung (NPC-Modelle ohne Speicherbank bleiben wie Phase 24)
        Assert.All(new[] { "warden", "corsair" }, m => Assert.DoesNotContain("power_bank_s", u.ShipModels[m].Fit));
    }

    [Fact]
    public void Waffenschaechte_EinSchachtJeWaffe_LaserNurMitBergbau()
    {
        var (u, me) = Pilot(Nebelmark, new Vec2(0f, 0f));
        var bays = WeaponBays.For(u, me);
        Assert.Contains(bays, b => b.Key == "tractor");
        Assert.DoesNotContain(bays, b => b.Key == "laser");   // der Jäger hat keinen Mining-Laser
        var weapons = bays.Where(b => PlayerGunnery.SlotOf(b.Key) >= 0).ToList();
        Assert.Equal(2, weapons.Count);
        Assert.All(weapons, b => { Assert.Equal(Facing.Bow, b.Facing); Assert.True(b.ArcDegrees < 360f && b.Range > 0f); });
        // ein Schürfer ohne Waffe behält genau die alten Schächte
        var (u2, pros) = Pilot(Nebelmark, new Vec2(0f, 0f), "prospector");
        Assert.Equal(WeaponBays.For(pros.Stats).Select(b => b.Key), WeaponBays.For(u2, pros).Select(b => b.Key));
    }

    // ───────── Gefechts-Autopilot (kinematischer Harness) ─────────

    [Theory]
    [InlineData(0f, 0f, 200f, 0f)]
    [InlineData(50f, 50f, -120f, 80f)]
    [InlineData(0f, 0f, 0f, 300f)]
    public void Autopilot_Anflug_ErreichtAbstandsband_UndHaeltEs(float sx, float sy, float tx, float ty)
    {
        var r = new EconomyRules();
        var pos = new Vec2(sx, sy);
        var dir = new Vec2(0f, -1f);
        var target = new Vec2(tx, ty);
        const float wanted = 40f, speed = 8f, dt = 0.25f;
        int inBand = 0;
        for (int i = 0; i < 600; i++)
        {
            var step = CombatAutopilot.Step(new CombatPilotInput(pos, dir, speed, target, CombatMove.Approach, wanted, 60f, 1f), dt, r);
            Assert.True(Vec2.Distance(step.Pos, pos) <= speed * dt + 1e-3f);   // nie schneller als erlaubt
            float turned = MathF.Abs(FiringArc.Normalize180(FiringArc.Bearing(step.Dir) - FiringArc.Bearing(dir)));
            Assert.True(turned <= r.CombatTurnRate * dt + 0.01f);               // Wendegeschwindigkeit begrenzt
            pos = step.Pos; dir = step.Dir;
            float d = Vec2.Distance(pos, target);
            if (i > 300) { Assert.InRange(d, wanted * r.CombatBandMin - 2f, wanted * r.CombatBandMax + 2f); inBand++; }
        }
        Assert.True(inBand > 0);
        // nach dem Anflug blickt das Schiff zum Ziel (Bugwaffen tragen)
        float off = MathF.Abs(FiringArc.Normalize180(FiringArc.Bearing(target - pos) - FiringArc.Bearing(dir)));
        Assert.True(off < 20f, $"Blick weicht {off:0}° vom Ziel ab");
    }

    [Fact]
    public void Autopilot_FluchtVergroessertAbstand_HaltenBleibtStehen_Deterministisch()
    {
        var r = new EconomyRules();
        var pos = new Vec2(0f, 0f); var dir = new Vec2(1f, 0f); var target = new Vec2(20f, 0f);
        float last = Vec2.Distance(pos, target);
        for (int i = 0; i < 100; i++)
        {
            var s = CombatAutopilot.Step(new CombatPilotInput(pos, dir, 8f, target, CombatMove.Flee, 0f, 60f, 1f), 0.25f, r);
            float d = Vec2.Distance(s.Pos, target);
            Assert.True(d > last - 1e-3f);
            last = d; pos = s.Pos; dir = s.Dir;
        }
        Assert.True(last > 20f + 8f * 0.25f * 90f);
        var hold = CombatAutopilot.Step(new CombatPilotInput(new Vec2(0f, 0f), new Vec2(1f, 0f), 8f, new Vec2(0f, -30f), CombatMove.Hold, 30f, 60f, 1f), 0.25f, r);
        Assert.Equal(new Vec2(0f, 0f), hold.Pos);
        var a = CombatAutopilot.Step(new CombatPilotInput(new Vec2(3f, 4f), new Vec2(0f, -1f), 8f, new Vec2(90f, 10f), CombatMove.Approach, 40f, 60f, -1f), 0.25f, r);
        var b = CombatAutopilot.Step(new CombatPilotInput(new Vec2(3f, 4f), new Vec2(0f, -1f), 8f, new Vec2(90f, 10f), CombatMove.Approach, 40f, 60f, -1f), 0.25f, r);
        Assert.Equal(a, b);
    }

    // ───────── Positionen im Spielersektor ─────────

    [Fact]
    public void RaeumlichesGefecht_PositionenWerdenFortgeschrieben_GespeichertGehasht_RoundtripBitIdentisch()
    {
        var (u, me) = Pilot(Nebelmark, new Vec2(0f, 0f));
        var raider = Raider(u, Nebelmark, new Vec2(0f, -55f));
        var start = raider.Position;
        Assert.Equal(GunResult.Fired, PlayerGunnery.TryFire(u, me, Slot(me, "weapon_laser_s"), raider.Id, 0f).Result);
        var runner = new SimulationRunner();
        for (int i = 0; i < 12 && u.Fights.Of(me.Id) is not null; i++) runner.Step(u);
        var e = u.Fights.Of(me.Id);
        Assert.NotNull(e);
        var rp = e!.Of(raider.Id)!;
        Assert.True(e.Spatial);
        Assert.Equal(rp.Pos, raider.Position);                // die Simulation schreibt die Schiffsposition fort
        Assert.NotEqual(start, raider.Position);              // der Kaperer hat sich bewegt
        // Positionen sind Teil des Hashes
        ulong h = UniverseHash.Compute(u);
        var saved = rp.Pos;
        rp.Pos = rp.Pos + new Vec2(1f, 0f);
        Assert.NotEqual(h, UniverseHash.Compute(u));
        rp.Pos = saved;
        // Speichern mitten im Spielergefecht → Laden → Weiterlaufen bit-identisch
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(h, UniverseHash.Compute(loaded));
        var r2 = new SimulationRunner();
        // der Spieler fliegt währenddessen (die Ansicht schreibt die Position jedes Bild — hier je Tick gleich in beiden Welten)
        var meLoaded = loaded.Ships[me.Id];
        for (int i = 0; i < 300; i++)
        {
            if (u.Ships.ContainsKey(me.Id)) me.Position += new Vec2(0.6f, -0.3f);
            if (loaded.Ships.ContainsKey(me.Id)) meLoaded.Position += new Vec2(0.6f, -0.3f);
            runner.Step(u); r2.Step(loaded);
        }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Uebergang_SpielerBetrittUndVerlaesstSektor_AbstaendeBleiben_EinGefecht()
    {
        var u = Standard();
        u.Rules.PlayerRaids = false;
        var raider = Raider(u, Nebelmark, new Vec2(0f, 0f));
        var victim = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        Place(u, victim, Nebelmark, new Vec2(80f, 40f));
        victim.Cargo[u.FindGood("electronics")!.Id] = 40f;
        Assert.NotEqual(Nebelmark, u.PlayerSector);
        var e = Engage.StartRaid(u, raider, victim, u.Sectors[Nebelmark]);
        e.Of(victim.Id)!.Courage = 0f;
        Assert.False(e.Spatial);
        Fight(u, 6);
        var rp = e.Of(raider.Id)!; var vp = e.Of(victim.Id)!;
        float line = MathF.Abs(rp.X - vp.X);
        // Spieler betritt den Sektor: räumlich, Abstand des Paars bleibt, Schiffe stehen an echten Positionen
        u.PlayerSector = Nebelmark;
        CombatSpace.Sync(u, e);
        Assert.True(e.Spatial && rp.InSpace && vp.InSpace);
        Assert.Equal(line, Vec2.Distance(rp.Pos, vp.Pos), 2);
        Assert.Equal(vp.Pos, victim.Position);
        Assert.Equal(rp.Pos, raider.Position);
        Fight(u, 4);
        float space = Vec2.Distance(rp.Pos, vp.Pos);
        // Spieler verlässt den Sektor: wieder die Abstandszahl, derselbe Abstand
        u.PlayerSector = Alpha;
        CombatSpace.Sync(u, e);
        Assert.False(e.Spatial);
        Assert.Equal(space, MathF.Abs(rp.X - vp.X), 2);
        Assert.Single(u.Fights.Encounters);
    }

    // ───────── Überfall auf das aktive Schiff ─────────

    [Fact]
    public void Spielerueberfall_NachDerWarnungEchtesGefecht_AndockenBeendetEs()
    {
        var (u, me) = Pilot(Nebelmark, new Vec2(0f, 0f));
        u.Rules.PlayerRaids = true;
        var raider = Raider(u, Nebelmark, new Vec2(0f, -200f));
        raider.Order = new PatrolOrder(Nebelmark); raider.DutySector = Nebelmark; raider.NextThinkTick = u.Tick + 100000;   // lauert hier
        u.PendingEncounters.Add(new PendingEncounter { Kind = EncounterKind.Raid, Source = raider.Id, Target = me.Id, Sector = Nebelmark, DueTick = u.Tick + 20 });
        var pirates = new PirateSystem();
        var ev = Capture(u);
        for (int i = 0; i < 4; i++) { u.Tick += 5; pirates.Step(u, 5); }
        Assert.True(Vec2.Distance(raider.Position, me.Position) < 200f);   // er nimmt wirklich Kurs auf den Spieler
        for (int i = 0; i < 2; i++) { u.Tick += 5; pirates.Step(u, 5); }
        var e = u.Fights.Of(me.Id);
        Assert.NotNull(e);
        Assert.Equal(EncounterCause.Raid, e!.Cause);
        Assert.Equal(CombatTactics.Player, e.Of(me.Id)!.Tactic);
        Assert.True(e.Spatial);
        Assert.Equal(u.Rules.CombatStartDistance, Vec2.Distance(e.Of(raider.Id)!.Pos, me.Position), 1);
        // der Spieler „dockt an“ (Ansicht setzt DockedAt) → entkommen, das Gefecht endet
        me.DockedAt = u.StationsById().First().Id;
        Fight(u, 3, () => u.Fights.Count == 0);
        Assert.False(u.Fights.IsEngaged(me.Id));
        Assert.Contains(ev, x => x is RaidResolved rr && rr.Player && rr.Victim == me.Id);
        Assert.True(u.Ships.ContainsKey(me.Id));
    }

    [Fact]
    public void Spielerueberfall_OhneHuellenboden_VerlustLandetAnStation_GeldBleibt()
    {
        var (u, me) = Pilot(Nebelmark, new Vec2(0f, 0f));
        u.WalletOf(me).Credits = 1234f;
        me.Cargo[u.FindGood("electronics")!.Id] = 10f;
        var raider = Raider(u, Nebelmark, new Vec2(0f, -30f));
        var ev = Capture(u);
        var e = Engage.StartRaid(u, raider, me, u.Sectors[Nebelmark]);
        me.Hull = 1f; me.Systems.ShieldMissing = me.Stats.Shield;
        Fight(u, 200, () => !u.Ships.ContainsKey(me.Id));
        Assert.False(u.Ships.ContainsKey(me.Id));
        var lost = Assert.Single(ev.OfType<PlayerShipLost>());
        Assert.Contains(ev, x => x is ShipDeparted d && d.Ship == me.Id && d.Reason == DepartureReason.Destroyed);
        var now = u.Ships[u.PlayerShip];
        Assert.Equal(lost.NewShip, now.Id);
        Assert.True(now.IsPlayer);
        Assert.Equal(lost.Station, now.DockedAt);
        Assert.Equal(1234f, u.WalletOf(now).Credits, 2);   // Geld bleibt (Notfall-Leihschiff: keine Vorauszahlung, oder bezahlte Miete)
        Assert.True(now.Cargo.Count == 0);                  // die Fracht ist weg
    }

    // ───────── Landeregel ─────────

    static Station AnyHangarStation(Universe u, SectorId sector) =>
        u.StationsById().First(s => s.Sector == sector && !s.IsBuildSite && u.StationTypes[s.TypeKey].Services.Contains(HangarService.ServiceKey));

    [Fact]
    public void Landeregel_EigenesSchiff_EigeneStation_Neutral_Gesucht_Feindlich()
    {
        var (u, me) = Pilot(Alpha, new Vec2(0f, 0f));
        var first = ShipLoss.LandingStation(u, Alpha, me.Position)!;
        Assert.NotNull(first);
        Assert.Equal(Alpha, first.Sector);
        // gesucht (Stufe 3) bei der Fraktion der nächsten Station → nicht dort
        var faction = u.FactionOf(first)!.Value;
        Wanted.Report(u, u.PlayerActor, faction, "assault");
        Assert.True(Wanted.LevelOf(u, u.PlayerActor, faction) >= 3);
        var second = ShipLoss.LandingStation(u, Alpha, me.Position)!;
        Assert.True(u.FactionOf(second) != faction || !ShipLoss.IsNeutral(u, second) == false);
        Assert.True(ShipLoss.IsNeutral(u, second));
        Assert.False(ShipLoss.IsNeutral(u, first));
        // eine eigene Station geht vor, auch wenn sie weiter weg liegt
        var far = u.StationsById().Last(s => !s.IsBuildSite && u.StationTypes[s.TypeKey].Services.Contains(HangarService.ServiceKey) && s.Sector != Alpha);
        far.Owner = u.PlayerActor;
        Assert.Equal(far.Id, ShipLoss.LandingStation(u, Alpha, me.Position)!.Id);
        // nie handlungsunfähig: auch wenn überall gesucht, gibt es eine Station
        foreach (var f in u.Factions.Keys) Wanted.Report(u, u.PlayerActor, f, "assault");
        far.Owner = u.Factions.Keys.First();
        Assert.NotNull(ShipLoss.LandingStation(u, Alpha, me.Position));
    }

    [Fact]
    public void Verlust_EigenesSchiffAnDerLandestation_WirdAktiv()
    {
        var (u, me) = Pilot(Alpha, new Vec2(0f, 0f));
        var st = ShipLoss.LandingStation(u, Alpha, me.Position)!;
        var spare = NewShip(u, "pip", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        PlayerFleet.Dock(u, spare, st);
        float money = u.WalletOf(me).Credits;
        var next = ShipLoss.LoseActive(u, me, "test");
        Assert.Equal(spare.Id, next!.Id);
        Assert.Equal(spare.Id, u.PlayerShip);
        Assert.False(u.Ships.ContainsKey(me.Id));
        Assert.Equal(money, u.WalletOf(spare).Credits);
        Assert.False(spare.IsRental);
    }

    [Fact]
    public void Verlust_LeihschiffMitGeld_SonstNotfallLeihschiff()
    {
        static (Universe u, Ship me, Station at) AtStation(Func<Universe, Station> pick, float credits)
        {
            var u = Standard();
            u.Rules.PlayerRaids = false;
            var st = pick(u);
            var me = NewShip(u, "falcon", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
            Place(u, me, st.Sector, st.Position);
            PlayerFleet.Activate(u, me, default);
            // keine anderen eigenen Schiffe an der Station (das Startschiff liegt woanders)
            foreach (var other in PlayerFleet.ParkedAt(u, st).ToList()) Place(u, other, Nebelmark, new Vec2(-300f, -300f));
            u.WalletOf(me).Credits = credits;
            return (u, me, st);
        }
        Func<Universe, Station> yardOf = u => u.StationsById().First(s => s.TypeKey == "shipyard");
        // an einer Werft (Leihdienst) mit Geld: reguläre Miete
        var (u1, me1, yard) = AtStation(yardOf, 5000f);
        Assert.Equal(yard.Id, ShipLoss.LandingStation(u1, yard.Sector, yard.Position)!.Id);
        var ev = Capture(u1);
        var next = ShipLoss.LoseActive(u1, me1, "test")!;
        Assert.True(next.IsRental);
        Assert.Equal(ShipLoss.Rented, ev.OfType<PlayerShipLost>().Single().How);
        Assert.Equal(5000f - Hangar.RentalUpfront(u1.Rules), u1.WalletOf(next).Credits, 2);
        // ohne Geld: Notfall-Leihschiff ohne Gebühr und Pfand, Miete läuft
        var (u2, me2, _) = AtStation(yardOf, 0f);
        var ev2 = Capture(u2);
        var em = ShipLoss.LoseActive(u2, me2, "test")!;
        Assert.Equal(ShipLoss.Emergency, ev2.OfType<PlayerShipLost>().Single().How);
        Assert.True(em.IsRental && em.RentalDeposit == 0f && em.RentedSince == u2.Tick);
        Assert.Equal(0f, u2.WalletOf(em).Credits);
        Assert.Equal(em.Id, u2.PlayerShip);
        // auch an einer Station ohne Leihdienst gibt es ein (Notfall-)Leihschiff — nie handlungsunfähig
        var (u3, me3, noRental) = AtStation(u => u.StationsById().First(s => !s.IsBuildSite && u.StationTypes[s.TypeKey].Services.Contains(HangarService.ServiceKey)
            && !u.StationTypes[s.TypeKey].Services.Contains(RentalService.ServiceKey)), 99999f);
        var em3 = ShipLoss.LoseActive(u3, me3, "test")!;
        Assert.True(em3.IsRental);
        Assert.Equal(noRental.Id, em3.DockedAt);
        Assert.Equal(99999f, u3.WalletOf(em3).Credits);
    }

    [Fact]
    public void Flottenschiff_WirdZerstoert_VerbandFolgtNach()
    {
        var u = Standard();
        u.Rules.PlayerRaids = false;
        var lead = NewShip(u, "unit_trader_100", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        var wing = NewShip(u, "unit_trader_100", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        Place(u, lead, Nebelmark, new Vec2(10f, 0f));
        Place(u, wing, Alpha, new Vec2(300f, 300f));
        var sq = Squadrons.Create(u, lead, "Test", out _)!;
        Assert.True(Squadrons.Join(u, sq, wing, out _));
        lead.Cargo[u.FindGood("electronics")!.Id] = 40f;
        var raider = Raider(u, Nebelmark, new Vec2(0f, 0f));
        var e = Engage.StartRaid(u, raider, lead, u.Sectors[Nebelmark]);
        e.Of(lead.Id)!.Courage = 0f;
        lead.Hull = 1f;
        var ev = Capture(u);
        Fight(u, 200, () => !u.Ships.ContainsKey(lead.Id));
        Assert.False(u.Ships.ContainsKey(lead.Id));   // Phase 25: auch Flottenschiffe gehen verloren (kein Hüllenboden)
        Assert.Contains(ev, x => x is ShipDeparted d && d.Ship == lead.Id && d.Reason == DepartureReason.Destroyed);
        Assert.Equal(wing.Id, sq.Leader);
    }

    // ───────── Angriff, Kopfgeld, Fahndung, Handelsverweigerung ─────────

    [Fact]
    public void Angriff_AufHaendler_IstVerbrechen_HandelWirdVerweigert_HaendlerWirftFrachtAb()
    {
        var (u, me) = Pilot(Alpha, new Vec2(0f, 0f));
        var owner = u.Sectors[Alpha].Owner;
        var trader = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        Place(u, trader, Alpha, new Vec2(0f, -25f));
        trader.Cargo[u.FindGood("electronics")!.Id] = 40f;
        var ev = Capture(u);
        Assert.Equal(GunResult.Fired, PlayerGunnery.TryFire(u, me, Slot(me, "weapon_laser_s"), trader.Id, 0f).Result);
        Assert.Contains(ev, x => x is CrimeReported c && c.Actor == u.PlayerActor && c.Crime is "assault" or "assault_station" && c.Faction == owner);
        Assert.True(Wanted.LevelOf(u, u.PlayerActor, owner) >= u.Rules.TradeRefusalWantedLevel);
        // Handel an einer Station der Fraktion verweigert, mit Grund im Angebot und in der Ausführung
        var st = u.StationsById().First(s => u.FactionOf(s) == owner && s.Market.Count > 0 && !s.IsBuildSite);
        var trade = new TradeService();
        var offers = trade.Offers(u, st, me);
        Assert.All(offers.Where(o => o.Key.StartsWith("buy:") || o.Key.StartsWith("sell:")), o => { Assert.Equal(0f, o.Amount); Assert.Contains("verweigert", o.Note); });
        var g = st.Market.Keys.First();
        var r = trade.Execute(u, st, me, new ServiceRequest($"buy:{g.Value}", default, 1f));
        Assert.False(r.Success);
        Assert.Contains("verweigert", r.Message);
        // eine Station einer anderen Fraktion handelt weiter
        var other = u.StationsById().FirstOrDefault(s => u.FactionOf(s) is { } f && f != owner && s.Market.Count > 0 && !s.IsBuildSite);
        if (other is not null) Assert.Null(TradeActions.Refusal(u, me, other));
        // der Händler gibt auf (Mut sofort unterschritten): Fracht als einsammelbare Brocken, Gefecht endet
        var e = u.Fights.Of(me.Id)!;
        e.Of(trader.Id)!.Courage = 2f;
        Fight(u, 3, () => u.Fights.Count == 0 || e.Done);
        Assert.Contains(ev, x => x is CargoDropped d && d.Ship == trader.Id && d.Units > 0f);
        Assert.Contains(ev, x => x is CargoSurrendered s && s.Ship == trader.Id && s.Taker == me.Id);
    }

    [Fact]
    public void Abschuss_EinesKaperers_BringtKopfgeld_OhneVerbrechen()
    {
        var (u, me) = Pilot(Alpha, new Vec2(0f, 0f));
        var faction = u.Sectors[Alpha].Owner;
        var raider = Raider(u, Alpha, new Vec2(0f, -20f));
        Wanted.Report(u, raider.Owner, faction, "piracy");
        raider.Hull = 0.5f; raider.Systems.ShieldMissing = raider.Stats.Shield;
        var ev = Capture(u);
        int laser = Slot(me, "weapon_laser_s");
        for (int i = 0; i < 80 && u.Ships.ContainsKey(raider.Id); i++)
        {
            PlayerGunnery.TryFire(u, me, laser, raider.Id, FiringArc.Bearing(raider.Position - me.Position));
            Fight(u, 1);
        }
        Assert.False(u.Ships.ContainsKey(raider.Id));
        Fight(u, 3);
        Assert.Contains(ev, x => x is BountyEarned b && b.Hunter == u.PlayerActor && b.Amount > 0f);
        Assert.True(u.Actors[u.PlayerActor].BountyClaims.GetValueOrDefault(faction) > 0f);
        Assert.DoesNotContain(ev, x => x is CrimeReported c && c.Actor == u.PlayerActor);
    }

    // ───────── Dienst rearm, Munition aus der Fracht ─────────

    [Fact]
    public void Rearm_StrikteSchluessel_BuchungWieNpc_UndUmladenAusDerFracht()
    {
        var u = Standard();
        new SimulationRunner().Step(u);   // Marktpreise
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        var me = NewShip(u, "falcon", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        PlayerFleet.Dock(u, me, yard);
        PlayerFleet.Activate(u, me, yard.Id);
        var ammo = Rearm.AmmoGood(u)!;
        yard.Market[ammo.Id].Amount = 20f;
        u.WalletOf(me).Credits = 1000f;
        me.Systems.AmmoMissing = me.Stats.AmmoCapacity;
        var svc = StationServices.Default().ForStation(u, yard).Single(s => s.Key == RearmService.ServiceKey);
        var ev = Capture(u);
        foreach (var bad in new[] { "rearm:5", "rearm:fill2", "rearm:", "rearm:010", "refuel:fill", "rearm:-10" })
        {
            var r0 = svc.Execute(u, yard, me, new ServiceRequest(bad, default, 0f));
            Assert.False(r0.Success);
        }
        Assert.Empty(ev);
        Assert.Equal(1000f, u.WalletOf(me).Credits);
        var offer = svc.Offers(u, yard, me).Single(o => o.Key == "rearm:10");
        Assert.Equal(10f, offer.Amount);
        float price = offer.Price, stationBefore = yard.Credits;
        var r = svc.Execute(u, yard, me, new ServiceRequest("rearm:10", default, 0f));
        Assert.True(r.Success);
        Assert.Equal(10f, me.AmmoLoaded, 3);
        Assert.Equal(10f, yard.Market[ammo.Id].Amount, 3);
        Assert.Equal(1000f - 10f * price, u.WalletOf(me).Credits, 2);
        Assert.Equal(stationBefore + 10f * price, yard.Credits, 2);
        Assert.Contains(ev, x => x is ServicePerformed p && p.Service == RearmService.ServiceKey);
        // Munition als Fracht mitführen und im Flug umladen
        PlayerFleet.Undock(u, me);
        me.Cargo[ammo.Id] = 25f;
        Assert.Null(Rearm.FromCargoIssue(u, me));
        float moved = Rearm.FromCargo(u, me);
        Assert.Equal(MathF.Min(25f, me.Stats.AmmoCapacity - 10f), moved, 3);
        Assert.Equal(10f + moved, me.AmmoLoaded, 3);
        // Dienst an Werft, Polizeiwache und Handelsstation gelistet
        foreach (var t in new[] { "shipyard", "police_station", "trade_station" }) Assert.Contains(RearmService.ServiceKey, u.StationTypes[t].Services);
    }

    // ───────── Werft ─────────

    [Fact]
    public void Werft_VerkauftJaegerUndWaechter_KaperschiffBleibtGesperrt()
    {
        var u = Standard();
        new SimulationRunner().Step(u);
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        var player = u.Ships[u.PlayerShip];
        var dealer = new ShipDealerService();
        var keys = dealer.Offers(u, yard, player).Select(o => o.Key).ToList();
        Assert.Contains("buy:falcon", keys);
        Assert.Contains("buy:warden", keys);
        Assert.DoesNotContain("buy:corsair", keys);
        var cls = u.ShipClasses["fighter"];
        Assert.Equal(SlotSize.S, cls.Size);
        Assert.Null(FitRules.Check(u.SlotTypes, u.Modules, cls, cls.Slots, u.ShipModels["falcon"].Fit));
        Assert.Empty(u.ShipModels["falcon"].Jobs);   // nicht für die KI-Flotte
        // kaufen: die Werft hat Bausatz und alle Module (Speicherbank im Lager)
        PlayerFleet.Dock(u, player, yard);
        u.WalletOf(player).Credits = 50000f;
        var r = dealer.Execute(u, yard, player, new ServiceRequest("buy:falcon", default, 0f));
        Assert.True(r.Success, r.Message);
        var bought = u.Ships[u.PlayerShip];
        Assert.Equal("falcon", bought.ModelKey);
        Assert.True(bought.Stats.PowerStorage > 0f && bought.Stats.WeaponPower > 0f);
        // Startpaket „Jäger“
        var w = SpaceSim.Core.Content.WorldBuilder.Build(TestWorlds.Def(fuel: false, research: false), null, "fighter");
        Assert.Equal("falcon", w.Ships[w.PlayerShip].ModelKey);
    }

    // ───────── Liegeplatz nach dem Gefecht (Prüfung Phase 24) ─────────

    [Fact]
    public void Liegeplatz_NurWasVorDemGefechtAngedocktWar()
    {
        var u = Standard();
        u.Rules.PlayerRaids = false;
        var st = u.StationsById().First(s => !s.IsBuildSite && s.Sector != u.PlayerSector);
        var raider = Raider(u, st.Sector, st.DockPoint(default) + new Vec2(25f, 0f));
        var trader = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        Place(u, trader, st.Sector, st.DockPoint(trader.Id));   // liegt am Andockpunkt, war aber nicht angedockt
        trader.Cargo[u.FindGood("electronics")!.Id] = 10f;
        var e = Engage.StartRaid(u, raider, trader, u.Sectors[st.Sector]);
        e.Of(trader.Id)!.Courage = 0f;
        Assert.Equal(default, e.Of(trader.Id)!.WasDockedAt);
        Engage.Leave(u, e, e.Of(trader.Id)!, CombatExit.Ended);
        Assert.Equal(default, trader.DockedAt);
        // das aktive Spielerschiff: Join merkt sich keinen Liegeplatz, Leave setzt keinen
        var (u2, me) = Pilot(Nebelmark, new Vec2(0f, 0f));
        var r2 = Raider(u2, Nebelmark, new Vec2(0f, -30f));
        PlayerGunnery.TryFire(u2, me, Slot(me, "weapon_laser_s"), r2.Id, 0f);
        var e2 = u2.Fights.Of(me.Id)!;
        Assert.Equal(default, e2.Of(me.Id)!.WasDockedAt);
        Engage.Leave(u2, e2, e2.Of(me.Id)!, CombatExit.Fled);
        Assert.Equal(default, me.DockedAt);
    }

    [Fact]
    public void OfflineAufholen_BeendetDasGefechtDesSpielers()
    {
        var (u, me) = Pilot(Nebelmark, new Vec2(0f, 0f));
        var raider = Raider(u, Nebelmark, new Vec2(0f, -30f));
        PlayerGunnery.TryFire(u, me, Slot(me, "weapon_laser_s"), raider.Id, 0f);
        Assert.True(u.Fights.IsEngaged(me.Id));
        OfflineCatchUp.Run(u, new SimulationRunner(), TimeSpan.FromSeconds(30));
        Assert.False(u.Fights.IsEngaged(me.Id));
        Assert.True(u.Ships.ContainsKey(me.Id));
    }

    [Fact]
    public void NpcWelt_OhneRaeumlicheGefechteUndOhneSpeicherbankRegal_HashWiePhase24()
    {
        // Phase 25 ändert die NPC-Welt nur über (a) räumliche Gefechte im Spielersektor, (b) das neue Werftregal der Speicherbank (Jäger) und (c) den
        // Zusatzschritt „jeder Händler bewaffnet“. Dreht man alle drei zurück, läuft die Standardwelt bit-identisch zum Stand Phase 24 (Balance-Tool, Standard-Seed, 20000 Ticks: Hash 75532A3F54AD3D9A) — neue
        // Klasse, neues Modell, Startpaket, Dienst „rearm“, Spielerschuss, Landeregel und Handelsverweigerung berühren die NPC-Wirtschaft nicht.
        var def = TestWorlds.Def(fuel: false, research: false);
        def.StationTypes.First(t => t.Key == "shipyard").Stocks!.Remove("power_bank_s");
        def.Rules!.SpatialCombat = false;
        // Zusatzschritt „jeder Händler bewaffnet“ zurückdrehen: Handelsmodelle ohne Laser, Klassen ohne angehängten Waffenslot und mit alter Masse,
        // die beiden bewaffneten Varianten von Phase 24 wieder dabei, Bewaffnung zählt ab 0
        def.Rules.ArmedTraderBaseline = 0f;
        def.Rules.RaidRatePerTick = new EconomyRules().RaidRatePerTick;   // Korrektur: Standardwelt 0,0065 (Verlustquote), Phase 24 lief mit dem Code-Wert
        foreach (var (cls, mass) in new[] { ("unit_trader", 10f), ("light_freighter", 16f), ("heavy_freighter", 30f) })
        {
            var c = def.ShipClasses.First(x => x.Key == cls);
            c.BaseMass = mass;
            if (c.Slots[^1].Type == "weapon" && cls != "light_freighter") c.Slots.RemoveAt(c.Slots.Count - 1);
        }
        foreach (var m in def.ShipModels.Where(m => m.Key is "unit_trader_100" or "unit_trader_60" or "behemoth")) m.Fit.RemoveAt(m.Fit.Count - 1);
        foreach (var m in def.ShipModels.Where(m => m.Key is "mule" or "hauler" or "tinker")) m.Fit[3] = null;
        int at = def.ShipModels.FindIndex(m => m.Key == "hauler") + 1;
        def.ShipModels.Insert(at, new SpaceSim.Core.Content.ShipModelDef { Key = "mule_armed", Class = "light_freighter", Name = "Mule (bewaffnet)", Jobs = new() { "trade" },
            Fit = new() { "reactor_mk1", "chem_engine_m", null, "weapon_laser_s", "cargo_universal_60", "cargo_universal_60", null } });
        def.ShipModels.Insert(at + 1, new SpaceSim.Core.Content.ShipModelDef { Key = "hauler_armed", Class = "light_freighter", Name = "Hauler (bewaffnet)", Jobs = new() { "trade" },
            Fit = new() { "reactor_mk1", "chem_engine_s", null, "weapon_massdriver_s", "cargo_universal_100", "cargo_universal_60", "weapon_magazine_s" } });
        var u = SpaceSim.Core.Content.WorldBuilder.Build(def);
        new SimulationRunner().Run(u, 20000);
        Assert.Equal(0x75532A3F54AD3D9AUL, UniverseHash.Compute(u));
    }

    // ───────── Zusatz Phase 25: jeder Händler bewaffnet ─────────

    [Fact]
    public void JederHaendler_JedesHandelsmodellTraegtEineWaffe_FrachtraumUnveraendert()
    {
        var u = Standard();
        var trade = u.ShipModels.Values.Where(m => m.Jobs.Contains("trade")).ToList();
        Assert.NotEmpty(trade);
        foreach (var m in trade)
        {
            var cls = u.ShipClasses[m.ClassKey];
            var stats = ShipStats.Compute(cls, m.Fit.Select(k => k is null ? null : u.Modules[k]).ToList());
            Assert.True(stats.WeaponPower > 0f, $"{m.Key} unbewaffnet");
            Assert.True(stats.Capabilities.HasFlag(Capability.Combat), m.Key);
            Assert.Null(FitRules.Check(u.SlotTypes, u.Modules, cls, cls.Slots, m.Fit));   // Leistungsbilanz stimmt
        }
        // Frachtraum (Kapazitätseinheiten) wie vorher: 100/60/120/160/60/500
        Assert.Equal(100f, ShipStats.Compute(u.ShipClasses["unit_trader"], u.ShipModels["unit_trader_100"].Fit.Select(k => k is null ? null : u.Modules[k]).ToList()).Cargo);
        Assert.Equal(500f, ShipStats.Compute(u.ShipClasses["heavy_freighter"], u.ShipModels["behemoth"].Fit.Select(k => k is null ? null : u.Modules[k]).ToList()).Cargo);
        Assert.False(u.ShipModels.ContainsKey("mule_armed"));
        Assert.False(u.ShipModels.ContainsKey("hauler_armed"));
        // Bergbauschiffe sind nicht pflichtbewaffnet
        Assert.Equal(0f, ShipStats.Compute(u.ShipClasses["unit_miner"], u.ShipModels["unit_miner_100"].Fit.Select(k => k is null ? null : u.Modules[k]).ToList()).WeaponPower);
    }

    [Fact]
    public void JederHaendler_GruenderUndKiNeubautenSindBewaffnet_20000Ticks()
    {
        var u = Standard();
        var traders = u.Ships.Values.Where(s => s.Role == ShipRole.Trader && s.Controller == Controller.Ai).ToList();
        Assert.NotEmpty(traders);
        Assert.All(traders, s => Assert.True(s.Stats.WeaponPower > 0f, $"Gründer-Händler {s.Id.Value} ({s.ModelKey}) unbewaffnet"));
        var built = new List<ShipId>();
        u.Events.Raised += e => { if (e is FleetShipBuilt b) built.Add(b.Ship); else if (e is FleetModelChosen c) built.Add(c.Ship); };
        new SimulationRunner().Run(u, 20000);
        // jedes KI-Handelsschiff am Ende (auch die Neubauten, auch über den Rückfall „nur Bausatz“) trägt eine Waffe
        foreach (var s in u.Ships.Values.Where(s => s.Controller == Controller.Ai && s.Role == ShipRole.Trader && s.HasModel))
            Assert.True(s.Stats.WeaponPower > 0f, $"Händler {s.Id.Value} ({s.ModelKey}) unbewaffnet");
        foreach (var id in built.Distinct())
            if (u.Ships.TryGetValue(id, out var s) && u.ShipModels.TryGetValue(s.ModelKey, out var m) && m.Jobs.Contains("trade") && !m.Jobs.Contains("mine"))
                Assert.True(s.Stats.WeaponPower > 0f, $"Neubau {id.Value} ({s.ModelKey}) unbewaffnet");
    }

    // ───────── Korrektur nach der Prüfung ─────────

    [Fact]
    public void Fahndung_ImHerrenlosenSektor_BeiNachbarnUndOpferfraktion_ImBesetztenBeimBesitzer()
    {
        // herrenlos (Nebelmark): freies Opfer → Fraktionen der angrenzenden Sektoren
        var (u, me) = Pilot(Nebelmark, new Vec2(0f, 0f));
        Assert.False(u.Factions.ContainsKey(u.Sectors[Nebelmark].Owner));
        var trader = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        Place(u, trader, Nebelmark, new Vec2(0f, -25f));
        var neighbours = u.Sectors[Nebelmark].Gates.Select(g => u.Sectors[u.Gates[u.Gates[g].Target].Sector].Owner).Where(f => u.Factions.ContainsKey(f)).Distinct().OrderBy(f => f.Value).ToList();
        Assert.NotEmpty(neighbours);
        Assert.Equal(neighbours, Wanted.AssaultAuthorities(u, Nebelmark, trader.Owner));
        Assert.Equal(GunResult.Fired, PlayerGunnery.TryFire(u, me, Slot(me, "weapon_laser_s"), trader.Id, 0f).Result);
        foreach (var f in neighbours) Assert.True(Wanted.LevelOf(u, u.PlayerActor, f) >= 3, $"keine Fahndung bei {u.Factions[f].Name}");
        // herrenlos, Opfer gehört einer Fraktion → (auch) deren Fahndung
        var police = SecuritySystem.PoliceOf(u, new FactionId(2)).First();
        Assert.Equal(new[] { new FactionId(2) }, Wanted.AssaultAuthorities(u, Nebelmark, police.Owner));
        // besetzter Sektor (Alpha, Union): Besitzer und Opferfraktion
        Assert.Equal(new[] { u.Sectors[Alpha].Owner }, Wanted.AssaultAuthorities(u, Alpha, u.FreeActor));
        Assert.Equal(new[] { u.Sectors[Alpha].Owner, new FactionId(2) }.OrderBy(f => f.Value), Wanted.AssaultAuthorities(u, Alpha, police.Owner));
    }

    [Fact]
    public void Stationsschutz_PolizeiAntwortetSicher_HoehereFahndung()
    {
        var u = Standard();
        u.Rules.PlayerRaids = false;
        var owner = u.Sectors[Alpha].Owner;
        var st = u.StationsById().First(x => x.Sector == Alpha && !x.IsBuildSite && u.FactionOf(x) == owner);
        var me = NewShip(u, "falcon", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        Place(u, me, Alpha, st.Position + new Vec2(0f, 50f));
        PlayerFleet.Activate(u, me, default);
        var trader = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        Place(u, trader, Alpha, st.Position + new Vec2(0f, 25f));   // im Schutzradius (35)
        var cop = SecuritySystem.PoliceOf(u, owner).First();
        Place(u, cop, Alpha, st.Position + new Vec2(60f, 0f));
        ulong rng = u.Rng.State;
        Assert.Equal(GunResult.Fired, PlayerGunnery.TryFire(u, me, Slot(me, "weapon_laser_s"), trader.Id, 0f).Result);
        var e = u.Fights.Of(me.Id)!;
        Assert.NotNull(e.Of(cop.Id));   // sicher dabei
        // gezogen: zwei Eintritte × 2, die Streife × 2, der Schuss × 2 — kein Zug für die Antwortchance
        var check = new DeterministicRng(1) { State = rng };
        for (int i = 0; i < 8; i++) check.NextDouble();
        Assert.Equal(check.State, u.Rng.State);
        Assert.Equal("assault_station", u.Actors[u.PlayerActor].Wanted[owner].LastCrime);
        Assert.True(Wanted.LevelOf(u, u.PlayerActor, owner) >= 4);
    }

    [Fact]
    public void Leihschiff_Verlust_PfandVerfaellt_MieteBezahlt_KeinFalschesNotfallschiff()
    {
        var u = Standard();
        u.Rules.PlayerRaids = false;
        var yard = u.StationsById().First(x => x.TypeKey == "shipyard");
        var player = u.Ships[u.PlayerShip];
        PlayerFleet.Dock(u, player, yard);
        PlayerFleet.Activate(u, player, yard.Id);
        u.WalletOf(player).Credits = 5000f;
        var rented = Hangar.Rent(u, yard, player, out _)!;
        Assert.True(rented.IsRental && !Hangar.IsEmergencyRental(rented));
        Place(u, rented, yard.Sector, yard.Position);
        u.Tick += 3 * u.Rules.DayTicks;   // drei angefangene Tage (4 Tage Miete)
        float wallet = u.WalletOf(rented).Credits, yardCash = yard.Credits;
        float rent = Hangar.RentalDays(u, rented) * u.Rules.RentalPerDay, deposit = rented.RentalDeposit;
        var ev = Capture(u);
        var next = ShipLoss.LoseActive(u, rented, "test")!;
        var lost = Assert.Single(ev.OfType<RentalLost>());
        Assert.Equal(deposit, lost.DepositForfeited, 2);
        Assert.Equal(MathF.Max(0f, rent - deposit), lost.RentPaid, 2);
        Assert.Contains("Pfand", ev.OfType<PlayerShipLost>().Single().Message);
        // das verlorene Leihschiff zählt nicht mehr: mit Geld gibt es an der Werft wieder ein reguläres Leihschiff (sonst stünde hier das alte Hauptschiff)
        if (next.IsRental) Assert.False(Hangar.IsEmergencyRental(next));
        // Geld erklärbar: Kasse − Miete über dem Pfand (− Gebühr und Pfand eines neuen Leihschiffs), die Werft bekommt Pfand + Miete (+ Gebühr)
        float newRental = next.IsRental ? Hangar.RentalUpfront(u.Rules) : 0f;
        Assert.Equal(wallet - lost.RentPaid - newRental, u.WalletOf(next).Credits, 1);
        Assert.Equal(yardCash + deposit + lost.RentPaid + (next.IsRental ? u.Rules.RentalFee : 0f), yard.Credits, 1);
    }

    [Fact]
    public void NotfallLeihschiff_MieteWirdAbgerechnet_NotfallsSchulden()
    {
        var u = Standard();
        u.Rules.PlayerRaids = false;
        var st = u.StationsById().First(s => !s.IsBuildSite && u.StationTypes[s.TypeKey].Services.Contains(HangarService.ServiceKey)
            && !u.StationTypes[s.TypeKey].Services.Contains(RentalService.ServiceKey));
        var me = NewShip(u, "falcon", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        Place(u, me, st.Sector, st.Position);
        PlayerFleet.Activate(u, me, default);
        foreach (var other in PlayerFleet.ParkedAt(u, st).ToList()) Place(u, other, Nebelmark, new Vec2(-300f, -300f));
        u.WalletOf(me).Credits = 0f;
        var em = ShipLoss.LoseActive(u, me, "test")!;
        Assert.True(Hangar.IsEmergencyRental(em));
        u.Tick += 2 * u.Rules.DayTicks;
        float stCash = st.Credits;
        float rent = Hangar.RentalDays(u, em) * u.Rules.RentalPerDay;
        Assert.True(rent > 0f);
        var em2 = ShipLoss.LoseActive(u, em, "test")!;   // auch das Notfall-Leihschiff geht verloren: die Miete bleibt geschuldet
        Assert.Equal(-rent, u.WalletOf(em2).Credits, 1);   // kein Gratis-Kreislauf: Schulden bei der Verleihstation
        Assert.Equal(stCash + rent, st.Credits, 1);
    }

    [Fact]
    public void Landeregel_ErreichbarVorUnerreichbar_GesuchtMitHinweisAufDieStrafe()
    {
        var (u, me) = Pilot(Nebelmark, new Vec2(0f, 0f));
        Assert.True(ShipLoss.Landing(u, Nebelmark, me.Position)!.Reachable);
        foreach (var g in u.Sectors[Nebelmark].Gates) GateAccess.SetState(u, u.Gates[g], GateState.Closed);
        var choice = ShipLoss.Landing(u, Nebelmark, me.Position)!;
        Assert.False(choice.Reachable);   // Nebelmark hat keine Station und ist abgeschnitten: nur der letzte Ausweg
        var ev = Capture(u);
        ShipLoss.LoseActive(u, me, "test");
        Assert.Contains("gesperrte Tore", ev.OfType<PlayerShipLost>().Single().Message);
        // überall gesucht: Landung (nie handlungsunfähig) mit dem Hinweis, wo die Strafe zu zahlen ist
        var (u2, me2) = Pilot(Alpha, new Vec2(0f, 0f));
        foreach (var f in u2.Factions.Keys) Wanted.Report(u2, u2.PlayerActor, f, "assault");
        var ev2 = Capture(u2);
        ShipLoss.LoseActive(u2, me2, "test");
        var msg = ev2.OfType<PlayerShipLost>().Single().Message;
        Assert.Contains("gesucht", msg);
        Assert.Contains("Behörde", msg);
    }

    [Fact]
    public void NachDemGefecht_NichtImHindernis()
    {
        var (u, me) = Pilot(Alpha, new Vec2(0f, 0f));
        var rock = u.Sectors[Alpha].Asteroids.First(a => a.Radius > 2f);
        var raider = Raider(u, Alpha, rock.Position);   // mitten im Felsen
        var cleared = CombatSpace.Clear(u, Alpha, rock.Position);
        Assert.True(Vec2.Distance(cleared, rock.Position) >= rock.Radius + Collision.ShipRadius);
        Assert.Equal(cleared, CombatSpace.Clear(u, Alpha, rock.Position));   // deterministisch
        ulong rng = u.Rng.State;
        CombatSpace.Clear(u, Alpha, rock.Position);
        Assert.Equal(rng, u.Rng.State);   // kein Zufallszug
        PlayerGunnery.TryFire(u, me, Slot(me, "weapon_laser_s"), raider.Id, FiringArc.Bearing(raider.Position - me.Position));
        var e = u.Fights.Of(raider.Id);
        if (e is not null)
        {
            var rp = e.Of(raider.Id)!;
            rp.Pos = rock.Position; raider.Position = rock.Position;
            Engage.Leave(u, e, rp, CombatExit.Ended);
            foreach (var o in u.GetObstacles(Alpha)) Assert.True(Vec2.Distance(raider.Position, o.Position) >= o.Radius + Collision.ShipRadius - 1e-3f);
        }
    }
}
