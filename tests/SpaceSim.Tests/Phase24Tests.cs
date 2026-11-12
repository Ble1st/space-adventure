using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 24 (KAMPF.md Abschnitte 3.3, 3.4, 4.1, 4.2, 4.2a): Waffendaten, die Trefferregel als reine Funktion, das Gefecht als gespeichertes Objekt, das
/// Rundengefecht mit Taktiken (Kaperer, Polizei, Händler, Begleiter), Flucht und Frachtabwurf, Munitionsverbrauch und Nachladen, bewaffnete Händler.
/// </summary>
public class Phase24Tests
{
    static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
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

    /// <summary>Ein Kaperschiff und ein Opfer mit Fracht allein in Nebelmark (keine Polizei, keine Station), der Rest der Welt unberührt.</summary>
    static (Universe u, Ship raider, Ship victim) Arena(string victimModel = "unit_trader_100", ulong? seed = null)
    {
        var u = Standard(seed);
        u.Rules.PlayerRaids = false;
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        var victim = NewShip(u, victimModel, ShipRole.Trader, u.FreeActor);
        Place(u, raider, Nebelmark, new Vec2(0f, 0f));
        Place(u, victim, Nebelmark, new Vec2(10f, 0f));
        victim.Cargo[u.FindGood("electronics")!.Id] = Math.Min(40f, victim.CargoCapacity);
        return (u, raider, victim);
    }

    /// <summary>Nur das Gefecht und die Schiffssysteme laufen (Rest der Welt steht).</summary>
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

    // ───────── Waffendaten und Trefferregel ─────────

    [Fact]
    public void Waffendaten_LaserUndGeschuetz_AbgeleiteteWaffenstaerke()
    {
        var u = Standard();
        var laser = u.Modules["weapon_laser_s"];
        var gun = u.Modules["weapon_massdriver_s"];
        Assert.True(laser.IsWeapon && gun.IsWeapon);
        Assert.Equal(DamageType.Energy, laser.DamageType);
        Assert.Equal(DamageType.Kinetic, gun.DamageType);
        Assert.Equal(0f, laser.Stat("ammoPerShot"));
        Assert.True(gun.Stat("ammoPerShot") > 0f && gun.Stat("ammoCapacity") > 0f);
        // weaponPower ist abgeleitet: Schaden × Treffsicherheit ÷ Nachladezeit × Faktor (Laser wie bisher ≈ 6)
        Assert.Equal(laser.Stat("damage") * laser.Stat("accuracy") / laser.Stat("reload") * ShipStats.WeaponPowerPerDps, ShipStats.WeaponPowerOf(laser), 3);
        Assert.InRange(ShipStats.WeaponPowerOf(laser), 5.5f, 6.5f);
        // das Geschütz ist eine Modul-Ware mit Fabrikrezept (Waffenwerk) und Werftlager
        var good = u.FindGood("weapon_massdriver_s")!;
        Assert.Contains(u.StationTypes.Values, t => t.Recipes.Any(r => r.Outputs.ContainsKey(good.Id)));
        Assert.Contains(u.StationTypes["shipyard"].Stocks, s => s.Good == good.Id);
        // Polizei und Kaperer tragen Laser + Geschütz + Magazin, die bewaffneten Frachter Waffen und die Aufgabe Handel
        Assert.Contains("weapon_massdriver_s", u.ShipModels["warden"].Fit);
        Assert.Contains("weapon_massdriver_s", u.ShipModels["corsair"].Fit);
        // Phase 25 („jeder Händler bewaffnet“): die Handelsmodelle selbst tragen die Laserkanone; die Varianten mule_armed/hauler_armed entfallen
        Assert.Contains("weapon_laser_s", u.ShipModels["mule"].Fit);
        Assert.Contains("weapon_laser_s", u.ShipModels["hauler"].Fit);
        Assert.Contains("trade", u.ShipModels["mule"].Jobs);
    }

    [Theory]
    [InlineData("damage", -1f, "damage muss > 0")]
    [InlineData("reload", 0f, "reload muss > 0")]
    [InlineData("range", 0f, "range muss > 0")]
    [InlineData("accuracy", 1.5f, "accuracy muss in (0, 1]")]
    [InlineData("tracking", 0f, "tracking muss in (0, 1]")]
    [InlineData("ammoPerShot", -1f, "ammoPerShot muss >= 0")]
    public void Weltdatei_WaffenwerteWerdenGeprueft(string stat, float value, string message)
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Modules.First(m => m.Key == "weapon_laser_s").Stats[stat] = value;
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
        Assert.Contains("weapon_laser_s", ex.Message);
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Weltdatei_SchadensartUndWaffenwerteOhneDamage()
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Modules.First(m => m.Key == "weapon_laser_s").DamageType = "plasma";
        Assert.Contains("damageType", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def)).Message);
        def = TestWorlds.Def(fuel: false, research: false);
        def.Modules.First(m => m.Key == "weapon_laser_s").DamageType = null;
        Assert.Contains("damageType fehlt", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def)).Message);
        def = TestWorlds.Def(fuel: false, research: false);
        def.Modules.First(m => m.Key == "shield_mk1").Stats["range"] = 30f;
        Assert.Contains("ohne damage", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def)).Message);
    }

    [Fact]
    public void Trefferregel_Deterministisch_FesteZugzahl_AbstandUndTempo()
    {
        var r = new EconomyRules();
        var w = new WeaponProfile("laser", 7f, DamageType.Energy, 2f, 60f, 0.85f, 0.8f, 0f, 1f);
        var target = new TargetProfile(0f, 0f, 8f, 1f);
        var a = new DeterministicRng(42);
        var b = new DeterministicRng(42);
        for (int i = 0; i < 200; i++)
        {
            float dist = i % 70;   // auch außerhalb der Reichweite
            var ha = Hit.Roll(a, w, target, dist, 1f, r);
            var hb = Hit.Roll(b, w, target, dist, 1f, r);
            Assert.Equal(ha, hb);
            if (dist > w.Range) Assert.False(ha.Hit);
        }
        // genau zwei Züge je Wurf, unabhängig vom Ausgang (Treffer, Fehlschuss, außer Reichweite)
        var c = new DeterministicRng(7);
        var reference = new DeterministicRng(7);
        Hit.Roll(c, w, target, 10f, 1f, r);
        Hit.Roll(c, w, target, 500f, 1f, r);
        Hit.Roll(c, w, target, 59f, 0f, r);
        for (int i = 0; i < 3 * Hit.DrawsPerShot; i++) reference.NextDouble();
        Assert.Equal(reference.State, c.State);
        // nah trifft öfter als fern, langsam öfter als schnell, großes Ziel öfter als kleines, Feuerwinkel senkt die Chance
        Assert.True(Hit.Chance(w, target, 10f, 1f, r) > Hit.Chance(w, target, 55f, 1f, r));
        Assert.True(Hit.Chance(w, target with { Speed = 2f }, 30f, 1f, r) >= Hit.Chance(w, target with { Speed = 14f }, 30f, 1f, r));
        Assert.True(Hit.Chance(w, target with { Speed = 14f, Signature = 2.2f }, 30f, 1f, r) > Hit.Chance(w, target with { Speed = 14f }, 30f, 1f, r));
        Assert.True(Hit.Chance(w, target, 30f, 0.5f, r) < Hit.Chance(w, target, 30f, 1f, r));
        Assert.Equal(0f, Hit.Chance(w, target, 61f, 1f, r));
        // geringe Nachführung (Geschütz) trifft schnelle kleine Ziele schlechter als der Laser
        var gun = new WeaponProfile("gun", 10f, DamageType.Kinetic, 3f, 45f, 0.85f, 0.3f, 1f, 0.5f);
        Assert.True(Hit.Chance(gun, target with { Speed = 12f }, 20f, 1f, r) < Hit.Chance(w, target with { Speed = 12f }, 20f, 1f, r));
    }

    [Fact]
    public void Trefferregel_SchadensartGegenSchildPanzerungHuelle()
    {
        var r = new EconomyRules();
        // Schild: Energie fängt der Schild stärker ab (mehr Schildschaden), kinetisch geht mehr durch
        var shielded = new TargetProfile(100f, 0f, 0f, 1f);
        var energy = Hit.Damage(10f, DamageType.Energy, shielded, 1f, r);
        var kinetic = Hit.Damage(10f, DamageType.Kinetic, shielded, 1f, r);
        Assert.Equal(10f * r.EnergyVsShield, energy.ShieldDamage, 3);
        Assert.Equal(10f * r.KineticVsShield, kinetic.ShieldDamage, 3);
        Assert.Equal(0f, energy.HullDamage); Assert.Equal(0f, kinetic.HullDamage);
        // fast leerer Schild: der Rest geht (zurückgerechnet) an die Hülle
        var weak = Hit.Damage(10f, DamageType.Energy, new TargetProfile(5f, 0f, 0f, 1f), 1f, r);
        Assert.Equal(5f, weak.ShieldDamage, 3);
        Assert.Equal(10f - 5f / r.EnergyVsShield, weak.HullDamage, 3);
        // Panzerung: zieht je Treffer Panzerwert × Anteil × Artfaktor ab — Energie prallt stärker ab
        var armored = new TargetProfile(0f, 40f, 0f, 1f);
        var eArm = Hit.Damage(10f, DamageType.Energy, armored, 1f, r);
        var kArm = Hit.Damage(10f, DamageType.Kinetic, armored, 1f, r);
        Assert.Equal(40f * r.ArmorPerHitShare * r.EnergyVsArmor, eArm.ArmorAbsorbed, 3);
        Assert.True(kArm.HullDamage > eArm.HullDamage);
        // kleine Waffe prallt an schwerer Panzerung ab
        Assert.Equal(0f, Hit.Damage(1f, DamageType.Energy, new TargetProfile(0f, 200f, 0f, 1f), 1f, r).HullDamage);
        // Reihenfolge Schild → Panzerung → Hülle
        var all = Hit.Damage(30f, DamageType.Kinetic, new TargetProfile(6f, 40f, 0f, 1f), 1f, r);
        Assert.Equal(6f, all.ShieldDamage, 3);
        float rest = 30f - 6f / r.KineticVsShield;
        Assert.Equal(rest - 40f * r.ArmorPerHitShare * r.KineticVsArmor, all.HullDamage, 3);
    }

    // ───────── Gefechtsablauf ─────────

    [Fact]
    public void Ueberfall_UnbewaffneterHaendler_GibtAufWirftFrachtAb_KapererNimmtSieUndBrichtAb()
    {
        var (u, raider, victim) = Arena();
        // Phase 25: jeder Händler trägt eine Laserkanone — für den unbewaffneten Fall wird sie hier ausgebaut
        for (int i = 0; i < victim.Fit.Count; i++) if (victim.Fit[i] is { } k && u.Modules[k].IsWeapon) victim.Fit[i] = null;
        victim.RecomputeStats(u);
        var ev = Capture(u);
        var e = Engage.StartRaid(u, raider, victim, u.Sectors[Nebelmark]);
        e.Of(victim.Id)!.Courage = 0.6f;   // gibt früh auf (sonst gewürfelt)
        float cargo = victim.Cargo.Values.Sum();
        float pirateCash = u.Actors[raider.Owner].Treasury.Credits;
        // gesperrt: kein Auftrag, keine Reise, im Index
        Assert.True(u.Fights.IsEngaged(victim.Id) && u.Fights.IsEngaged(raider.Id));
        Assert.Null(victim.Order); Assert.Null(victim.Transit);
        Fight(u, u.Rules.CombatMaxTicks + 5, () => u.Fights.Count == 0);
        Assert.Equal(0, u.Fights.Count);
        Assert.False(u.Fights.IsEngaged(victim.Id));
        var surrendered = Assert.Single(ev.OfType<CargoSurrendered>());
        Assert.True(surrendered.Value > 0f);
        Assert.True(victim.Cargo.Values.Sum() < cargo && victim.Cargo.Values.Sum() > 0f);   // ein Teil bleibt
        var rr = Assert.Single(ev.OfType<RaidResolved>());
        Assert.True(rr.AttackerWon);
        Assert.Equal(surrendered.Value, rr.LootValue, 2);
        Assert.True(u.Actors[raider.Owner].Treasury.Credits >= pirateCash);   // Hehlerei (gedeckelt)
        Assert.Contains(ev.OfType<CombatExited>(), x => x.Ship == raider.Id && x.Exit == CombatExit.Looted);
        Assert.Contains(ev.OfType<ShotFired>(), s => s.Shooter == raider.Id && s.Target == victim.Id);
        Assert.DoesNotContain(ev.OfType<ShotFired>(), s => s.Shooter == victim.Id);   // unbewaffnet
        Assert.True(victim.Hull < victim.MaxHull);
        Assert.IsType<GoToOrder>(raider.Order);   // mit Beute heim (Hehlerei/Reparatur)
    }

    [Fact]
    public void Flucht_SchnelleresSchiffEntkommt_LangsamesNicht()
    {
        var (u, raider, fast) = Arena("swift");
        var ev = Capture(u);
        Assert.True(Travel.SpeedOf(fast) > Travel.SpeedOf(raider));
        var e = Engage.StartRaid(u, raider, fast, u.Sectors[Nebelmark]);
        e.Of(fast.Id)!.Courage = 0f;   // gibt nie auf
        Fight(u, u.Rules.CombatMaxTicks + 5, () => u.Fights.Count == 0);
        Assert.Contains(ev.OfType<CombatExited>(), x => x.Ship == fast.Id && x.Exit == CombatExit.Fled);
        Assert.False(Assert.Single(ev.OfType<RaidResolved>()).AttackerWon);
        Assert.Empty(ev.OfType<CargoSurrendered>());

        var (u2, raider2, slow) = Arena("hauler");
        var ev2 = Capture(u2);
        Assert.True(Travel.SpeedOf(slow) < Travel.SpeedOf(raider2));
        var e2 = Engage.StartRaid(u2, raider2, slow, u2.Sectors[Nebelmark]);
        e2.Of(slow.Id)!.Courage = 0.5f;
        Fight(u2, u2.Rules.CombatMaxTicks + 5, () => u2.Fights.Count == 0);
        Assert.DoesNotContain(ev2.OfType<CombatExited>(), x => x.Ship == slow.Id && x.Exit == CombatExit.Fled);
        Assert.Single(ev2.OfType<CargoSurrendered>());
    }

    [Fact]
    public void BewaffneterHaendler_WehrtSich_SchiesstZurueck()
    {
        var (u, raider, armed) = Arena("mule");   // Phase 25: jeder Händler trägt eine Waffe
        var ev = Capture(u);
        Assert.True(CombatSense.Armed(u, armed));
        var e = Engage.StartRaid(u, raider, armed, u.Sectors[Nebelmark]);
        e.Of(armed.Id)!.Courage = 0.05f;
        float raiderHull = raider.Hull + raider.ShieldNow;
        Fight(u, u.Rules.CombatMaxTicks + 5, () => u.Fights.Count == 0);
        Assert.Contains(ev.OfType<ShotFired>(), s => s.Shooter == armed.Id && s.Target == raider.Id);
        Assert.Contains(ev.OfType<ShotFired>(), s => s.Shooter == armed.Id && s.Hit);
        Assert.True(!u.Ships.ContainsKey(raider.Id) || raider.Hull + raider.ShieldNow < raiderHull);
        // Phase 25: die Mule ist jetzt immer bewaffnet; unbewaffnet sind nur noch Bergbauschiffe ohne Waffe
        Assert.Equal(1f, u.ShipModels["mule"].Fit.Count(k => k is not null && u.Modules[k].IsWeapon));
        Assert.Equal(0f, u.ShipModels["unit_miner_100"].Fit.Count(k => k is not null && u.Modules[k].IsWeapon));
    }

    [Fact]
    public void Abfangen_PolizeiStelltKaperer_VertriebenOderZerstoert_AbgangNurUeberDepartures()
    {
        var u = Standard();
        var ev = Capture(u);
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        var police = SecuritySystem.PoliceOf(u, new FactionId(1)).First();
        Place(u, raider, Nebelmark, new Vec2(0f, 0f));
        Place(u, police, Nebelmark, new Vec2(30f, 0f));
        Engage.StartIntercept(u, police, raider);
        Fight(u, u.Rules.CombatMaxTicks + 5, () => u.Fights.Count == 0);
        var ri = Assert.Single(ev.OfType<RaiderIntercepted>());
        Assert.True(ri.PoliceWon);
        Assert.Contains(ev.OfType<ShotFired>(), s => s.Shooter == police.Id);
        foreach (var x in ev.OfType<CombatExited>().Where(x => x.Exit == CombatExit.Destroyed))
            Assert.Contains(ev.OfType<ShipDeparted>(), d => d.Ship == x.Ship && d.Reason == DepartureReason.Destroyed && d.Detail == "intercept");
        Assert.Equal(ri.RaiderDestroyed, !u.Ships.ContainsKey(raider.Id));
        Assert.Equal(0, u.Fights.Count);
    }

    [Fact]
    public void Begleiter_DerSpielerflotte_Verteidigt_BefehlLaeuftWeiter_HuellenbodenGilt()
    {
        var (u, raider, victim) = Arena();
        var ev = Capture(u);
        var escort = NewShip(u, "warden", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        Place(u, escort, Nebelmark, new Vec2(12f, 0f));
        escort.Command = new EscortCommand(victim.Id);
        escort.Engagement = Engagement.Defend;
        escort.Hull = 0.3f * escort.MaxHull;   // angeschlagen: der Hüllenboden des Spielerschiffs muss halten
        var e = Engage.StartRaid(u, raider, victim, u.Sectors[Nebelmark]);
        Assert.Equal(CombatTactics.Escort, e.Of(escort.Id)!.Tactic);
        Fight(u, u.Rules.CombatMaxTicks + 5, () => u.Fights.Count == 0);
        Assert.Contains(ev.OfType<ShotFired>(), s => s.Shooter == escort.Id);
        Assert.True(u.Ships.ContainsKey(escort.Id));
        Assert.True(escort.Hull >= u.Rules.PlayerMinHull * escort.MaxHull - 0.01f);
        Assert.IsType<EscortCommand>(escort.Command);   // der Befehl ruhte und läuft weiter
        Assert.False(u.Fights.IsEngaged(escort.Id));
    }

    [Theory]
    [InlineData(Engagement.Passive)]
    [InlineData(Engagement.HoldFire)]
    public void Verhaltensregel_PassivUndFeuerEinstellen_SchiessenNicht(Engagement rules)
    {
        var (u, raider, _) = Arena();
        var ev = Capture(u);
        var own = NewShip(u, "mule", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        Place(u, own, Nebelmark, new Vec2(15f, 0f));
        own.Engagement = rules;
        own.Cargo[u.FindGood("electronics")!.Id] = 30f;
        var e = Engage.StartRaid(u, raider, own, u.Sectors[Nebelmark]);
        Fight(u, 60);
        Assert.DoesNotContain(ev.OfType<ShotFired>(), s => s.Shooter == own.Id);
        var p = e.Of(own.Id)!;
        if (p.Active) Assert.Equal(rules == Engagement.Passive ? CombatMove.Flee : CombatMove.Hold, p.Move);
        Assert.True(own.Hull >= u.Rules.PlayerMinHull * own.MaxHull - 0.01f);   // Flottenschiffe gehen in Phase 24 nicht verloren
    }

    // ───────── Sperre und Freigabe ─────────

    [Fact]
    public void Sperre_ReiseEndetAmOrt_KeinAuftrag_FreigabeOhneGeister_HaendlerVerkauftDanach()
    {
        var u = Standard();
        u.Rules.PlayerRaids = false;
        var runner = new SimulationRunner();
        runner.Run(u, 300);
        var trader = u.ShipsById().First(s => s.Role == ShipRole.Trader && s.Transit is not null && s.Order is TradeOrder);
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        var (sector, pos) = ThreatMap.Where(u, trader);
        if (raider.Transit is not null) u.FinishTransit(raider);
        if (raider.Sector != sector) u.MoveShip(raider, sector);
        raider.Position = pos;
        raider.Order = null;
        var e = Engage.StartRaid(u, raider, trader, u.Sectors[sector]);
        e.Of(trader.Id)!.Courage = 0.6f;
        // die Reise endete an der aktuellen Stelle, der Auftrag ist frei (keine Reservierung)
        Assert.Null(trader.Transit);
        Assert.Null(trader.Order);
        Assert.Equal(sector, trader.Sector);
        Assert.DoesNotContain(u.Tracker.TransitIn(sector), id => id == trader.Id);
        // im Gefecht arbeitet kein KI-System am Schiff
        for (int i = 0; i < 40 && u.Fights.IsEngaged(trader.Id); i++)
        {
            runner.Step(u);
            if (u.Fights.IsEngaged(trader.Id)) { Assert.Null(trader.Order); Assert.Null(trader.Transit); }
        }
        for (int i = 0; i < 400 && u.Fights.IsEngaged(trader.Id); i++) runner.Step(u);
        Assert.False(u.Fights.IsEngaged(trader.Id));
        runner.Run(u, 600);
        // danach: wieder Arbeit, keine Geister (Handelsauftrag immer mit Reise oder an der Station)
        foreach (var s in u.Ships.Values)
        {
            Assert.False(s.Order is GoToOrder && s.Transit is null, $"Geister-GoTo bei {s.Id.Value}");
            if (u.Fights.IsEngaged(s.Id)) { Assert.Null(s.Transit); Assert.True(s.Order is null); }
        }
        Assert.True(!u.Ships.ContainsKey(trader.Id) || trader.Order is not null || trader.Transit is not null || trader.Cargo.Values.Sum() < 1f);
    }

    [Fact]
    public void Entfernen_EinesTeilnehmers_GibtDieGegenseiteSofortFrei()
    {
        var (u, raider, victim) = Arena();
        Engage.StartRaid(u, raider, victim, u.Sectors[Nebelmark]);
        u.RemoveShip(raider.Id, "test");
        Assert.False(u.Fights.IsEngaged(victim.Id));
        var ev = Capture(u);
        Fight(u, 2);
        Assert.Equal(0, u.Fights.Count);
        Assert.Single(ev.OfType<EncounterEnded>());
    }

    [Fact]
    public void Andockplatz_BleibtNachDemGefecht_WennDasSchiffAmAndockpunktLiegt()
    {
        var u = Standard();
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        var police = SecuritySystem.PoliceOf(u, new FactionId(1)).First();
        var home = u.Stations[police.HomeBase];
        u.PlayerSector = Nebelmark;   // Phase 25: im Spielersektor flöge die Streife räumlich vom Andockpunkt weg — hier geht es um die Linie
        Place(u, police, home.Sector, home.DockPoint(police.Id));
        police.DockedAt = home.Id;
        Place(u, raider, home.Sector, home.DockPoint(police.Id) + new Vec2(25f, 0f));
        Engage.StartIntercept(u, police, raider);
        Assert.Equal(default, police.DockedAt);   // im Gefecht nicht angedockt
        Fight(u, u.Rules.CombatMaxTicks + 5, () => u.Fights.Count == 0);
        Assert.Equal(0, u.Fights.Count);
        Assert.Null(police.Transit);
        Assert.Equal(home.Id, police.DockedAt);   // am Andockpunkt geblieben: wieder angedockt
        // Phase 24 (Korrektur): ein Gefechtsteilnehmer im freien Raum bleibt ohne Liegeplatz
        Assert.True(!u.Ships.ContainsKey(raider.Id) || raider.Transit is not null || raider.DockedAt == u.StationAt(raider.Sector, raider.Position));
    }

    // ───────── Munition ─────────

    [Fact]
    public void Munition_VerbrauchJeSchuss_LeeresMagazinSchweigt_SchalterAus()
    {
        var (u, raider, victim) = Arena();
        var ev = Capture(u);
        int gunSlot = raider.Fit.IndexOf("weapon_massdriver_s");
        Assert.True(gunSlot >= 0);
        var e = Engage.StartRaid(u, raider, victim, u.Sectors[Nebelmark]);
        e.Of(victim.Id)!.Courage = 0f;
        Fight(u, 80);
        int gunShots = ev.OfType<ShotFired>().Count(s => s.Shooter == raider.Id && s.Weapon == "weapon_massdriver_s");
        Assert.True(gunShots > 0);
        Assert.Equal(gunShots * u.Modules["weapon_massdriver_s"].Stat("ammoPerShot"), raider.Systems.AmmoMissing, 3);
        // leeres Magazin: das Geschütz schweigt, der Laser feuert weiter
        raider.Systems.AmmoMissing = raider.Stats.AmmoCapacity;
        ev.Clear();
        Fight(u, 60);
        if (u.Fights.IsEngaged(raider.Id) || ev.OfType<ShotFired>().Any())
        {
            Assert.DoesNotContain(ev.OfType<ShotFired>(), s => s.Shooter == raider.Id && s.Weapon == "weapon_massdriver_s");
            Assert.Contains(ev.OfType<ShotFired>(), s => s.Shooter == raider.Id && s.Weapon == "weapon_laser_s");
        }
        // Schalter aus: kein Verbrauch, Magazin gilt als voll
        var (u2, raider2, victim2) = Arena();
        u2.Rules.AmmoActive = false;
        var ev2 = Capture(u2);
        Engage.StartRaid(u2, raider2, victim2, u2.Sectors[Nebelmark]).Of(victim2.Id)!.Courage = 0f;
        Fight(u2, 80);
        Assert.Contains(ev2.OfType<ShotFired>(), s => s.Shooter == raider2.Id && s.Weapon == "weapon_massdriver_s");
        Assert.Equal(0f, raider2.Systems.AmmoMissing);
        Assert.Equal(raider2.Stats.AmmoCapacity, ShipSystems.Ammo(u2, raider2));
    }

    [Fact]
    public void Munition_NachladenMitBuchung_HaendlerAnDerStation_PolizeiUndPiratenAusDerKasse()
    {
        var u = Standard();
        var ev = Capture(u);
        var ammo = Rearm.AmmoGood(u)!;
        // Händler mit Geschütz an einer Station mit Munitionslager: zahlt selbst, die Station bekommt das Geld, das Lager sinkt
        var trader = NewShip(u, "hauler", ShipRole.Trader, u.FreeActor, 1000f);
        // Phase 25: das Geschütz mit Magazin statt der Laserkanone (Varianten-Modell hauler_armed entfiel)
        trader.Fit[trader.Fit.IndexOf("weapon_laser_s")] = "weapon_massdriver_s";
        trader.Fit[^1] = "weapon_magazine_s";
        trader.RecomputeStats(u);
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        var entry = yard.Market[ammo.Id];
        entry.Amount = 50f; entry.Price = ammo.BasePrice;
        trader.Systems.AmmoMissing = 20f;
        float yardCash = yard.Credits, shipCash = trader.Credits;
        Place(u, trader, yard.Sector, yard.DockPoint(trader.Id));
        trader.DockedAt = yard.Id;
        new RearmSystem().Step(u, 10);
        Assert.Equal(0f, trader.Systems.AmmoMissing, 3);
        Assert.Equal(30f, entry.Amount, 3);
        Assert.Equal(yardCash + 20f * ammo.BasePrice, yard.Credits, 2);
        Assert.Equal(shipCash - 20f * ammo.BasePrice, trader.Credits, 2);
        Assert.Contains(ev.OfType<AmmoRearmed>(), a => a.Ship == trader.Id && !a.FromTreasury);
        // Polizei an der Heimatbasis: aus der Fraktionskasse, als erklärbare Ausgabe „ammo“
        var police = SecuritySystem.PoliceOf(u, new FactionId(1)).First();
        police.Systems.AmmoMissing = 25f;
        var union = u.Actors[police.Owner];
        union.Treasury.Credits = 10_000f;   // die Kassen starten leer (Abgaben kommen erst mit der Zeit)
        float before = union.Treasury.Credits;
        PirateSystem.ServiceAtBase(u, police, "police");
        Assert.Equal(0f, police.Systems.AmmoMissing, 3);
        float paid = ev.OfType<TreasuryUpkeep>().Where(t => t.Actor == union.Id && t.Purpose == Rearm.Purpose).Sum(t => t.Amount);
        Assert.True(paid > 0f);
        Assert.Equal(before - paid, union.Treasury.Credits, 1);
        // Piraten im Versteck (ohne Lager): Basispreis × Aufschlag aus ihrer Kasse
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        raider.Systems.AmmoMissing = 10f;
        var pirates = u.Actors[raider.Owner];
        float pc = pirates.Treasury.Credits;
        PirateSystem.ServiceAtBase(u, raider, "pirates");
        Assert.Equal(0f, raider.Systems.AmmoMissing, 3);
        Assert.Equal(pc - 10f * ammo.BasePrice * u.Rules.AmmoBaseMarkup, pirates.Treasury.Credits, 1);
        // Schalter aus: nichts zu laden
        u.Rules.AmmoActive = false;
        raider.Systems.AmmoMissing = 10f;
        PirateSystem.ServiceAtBase(u, raider, "pirates");
        Assert.Equal(10f, raider.Systems.AmmoMissing);
    }

    // ───────── Bewaffnung beim Kauf ─────────

    [Fact]
    public void Kaufwahl_BewaffnungLohntNachRoutenrisiko()
    {
        var u = Standard();
        // Phase 25: jedes Handelsmodell trägt eine Laserkanone; die Regel wertet nur Bewaffnung über der Grundausstattung (armedTraderBaseline) — hier mit
        // Grundausstattung 0 die Phase-24-Semantik: bewaffnete Mule gegen eine unbewaffnete
        u.Rules.ArmedTraderBaseline = 0f;
        var armed = ShipStats.Compute(u.ShipClasses["light_freighter"], u.ShipModels["mule"].Fit.Select(k => k is null ? null : u.Modules[k]).ToList());
        var plain = ShipStats.Compute(u.ShipClasses["light_freighter"], u.ShipModels["mule"].Fit.Select(k => k is null || k == "weapon_laser_s" ? null : u.Modules[k]).ToList());
        foreach (var s in u.Sectors.Values) s.Threat = 0f;
        Assert.Equal(0f, FleetSystem.ArmamentBonus(u, armed, Nebelmark));   // ruhig: die Waffe lohnt nicht
        Assert.Equal(0f, FleetSystem.ArmamentBonus(u, plain, Nebelmark));
        u.Sectors[Nebelmark].Threat = 3f;
        float border = FleetSystem.ArmamentBonus(u, armed, Nebelmark);
        float core = FleetSystem.ArmamentBonus(u, armed, new SectorId(6));   // Vesta liegt weit weg vom Risiko
        Assert.True(border > 0f);
        Assert.True(border > core);
        Assert.Equal(0f, FleetSystem.ArmamentBonus(u, plain, Nebelmark));
        u.Rules.ArmedTraderWeight = 0f;
        Assert.Equal(0f, FleetSystem.ArmamentBonus(u, armed, Nebelmark));
        // Standardwelt: Grundausstattung eine Laserkanone — die zählt nicht mehr, nur mehr Bewaffnung
        u.Rules.ArmedTraderWeight = 40f;
        u.Rules.ArmedTraderBaseline = 6f;
        Assert.Equal(0f, FleetSystem.ArmamentBonus(u, armed, Nebelmark));
    }

    // ───────── Prüfung Phase 23 ─────────

    [Fact]
    public void Erholungsindex_OhneUeberschuss_FaelltHeraus_ModulwechselWecktWieder()
    {
        var u = Standard();
        var p = SecuritySystem.PoliceOf(u, new FactionId(1)).First();
        // Reaktor auf Mk1 (10) und zweiter Laser: Dauerlast ≥ Leistung → kein Überschuss
        int reactor = p.Slots.FindIndex(s => s.Type == "reactor");
        p.Fit[reactor] = "reactor_mk1";
        p.RecomputeStats(u);
        Assert.Equal(0f, ShipSystems.Surplus(p.Stats));
        ShipSystems.TakeShield(u, p, 20f);
        Assert.False(ShipSystems.CanRecover(p.Stats, p.Systems));
        Assert.False(u.Recovering.Contains(p.Id));   // ohne mögliche Erholung gar nicht erst im Index
        // auch wenn er drinsteht (alter Stand), fällt er beim nächsten Schritt heraus — ohne den Zustand zu ändern
        u.Recovering.Add(p.Id);
        float missing = p.Systems.ShieldMissing;
        var sys = new ShipSystemsSystem();
        u.Tick += 40;
        sys.Step(u, 1);
        Assert.False(u.Recovering.Contains(p.Id));
        Assert.Equal(missing, p.Systems.ShieldMissing);
        // Modulwechsel (Mk2-Reaktor) weckt das Schiff wieder
        p.Fit[reactor] = "reactor_mk2";
        p.RecomputeStats(u);
        Assert.True(u.Recovering.Contains(p.Id));
        for (int i = 0; i < 2000 && u.Recovering.Contains(p.Id); i++) { u.Tick++; sys.Step(u, 1); }
        Assert.Equal(0f, p.Systems.ShieldMissing);
    }

    [Fact]
    public void Modulwechsel_KlemmtSchildFehlbetrag()
    {
        var u = Standard();
        var p = SecuritySystem.PoliceOf(u, new FactionId(1)).First();
        int shield = p.Slots.FindIndex(s => s.Type == "shield");
        ShipSystems.TakeShield(u, p, 30f);
        Assert.Equal(30f, p.Systems.ShieldMissing);
        p.Fit[shield] = null;   // Schild ausgebaut: kein Fehlbetrag mehr
        p.RecomputeStats(u);
        Assert.Equal(0f, p.Stats.Shield);
        Assert.Equal(0f, p.Systems.ShieldMissing);
        Assert.Equal(0f, p.ShieldNow);
        p.Fit[shield] = "shield_mk1";   // wieder eingebaut: voll (Fehlbetrag 0, nicht der alte)
        p.RecomputeStats(u);
        Assert.Equal(p.Stats.Shield, p.ShieldNow);
    }

    // ───────── Spielstand ─────────

    [Fact]
    public void Spielstand_MittenImGefecht_BitIdentisch_UndWeiterlaufen()
    {
        var (u, raider, victim) = Arena();
        var e = Engage.StartRaid(u, raider, victim, u.Sectors[Nebelmark]);
        e.Of(victim.Id)!.Courage = 0.1f;
        var runner = new SimulationRunner();
        runner.Run(u, 25);
        Assert.Equal(1, u.Fights.Count);
        Assert.True(u.Fights.Encounters[0].Shots > 0);
        var hash = UniverseHash.Compute(u);
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(hash, UniverseHash.Compute(loaded));
        Assert.True(loaded.Fights.IsEngaged(victim.Id) && loaded.Fights.IsEngaged(raider.Id));
        Assert.Equal(u.Modules["weapon_massdriver_s"].DamageType, loaded.Modules["weapon_massdriver_s"].DamageType);
        var r2 = new SimulationRunner();
        runner.Run(u, 400);
        r2.Run(loaded, 400);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Determinismus_GleicherSeedGleicherHash()
    {
        var a = Standard(5); var b = Standard(5);
        new SimulationRunner().Run(a, 6000);
        new SimulationRunner().Run(b, 6000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }

    // ───────── Langläufe ─────────

    [Fact]
    public void Langlauf_Standardwelt_40000Ticks() => LongRun(Standard(), "Standardwelt");

    [Fact]
    public void Langlauf_Classic_40000Ticks() => LongRun(TestWorlds.Classic(), "Classic");

    static void LongRun(Universe u, string name)
    {
        float loot = 0f, revenue = 0f;
        int fights = 0, ended = 0, shots = 0;
        var destroyedInCombat = new HashSet<ShipId>();
        var departedDestroyed = new HashSet<ShipId>();
        var removed = new List<ShipId>();
        u.Events.Raised += e =>
        {
            switch (e)
            {
                case RaidResolved rr: loot += rr.LootValue; break;
                case TradeCompleted tc: revenue += tc.Revenue; break;
                case MineDelivered md: revenue += md.Revenue; break;
                case EncounterStarted: fights++; break;
                case EncounterEnded ee: ended++; shots += ee.Shots; break;
                case CombatExited { Exit: CombatExit.Destroyed } cx: destroyedInCombat.Add(cx.Ship); break;
                case ShipDeparted { Reason: DepartureReason.Destroyed } sd: departedDestroyed.Add(sd.Ship); break;
            }
        };
        var runner = new SimulationRunner();
        for (int chunk = 0; chunk < 40; chunk++)
        {
            runner.Run(u, 1000);
            foreach (var s in u.Ships.Values)
            {
                Assert.False(s.Order is GoToOrder && s.Transit is null, $"{name}: Geister-GoTo bei {s.Id.Value}");
                if (u.Fights.IsEngaged(s.Id))
                {
                    Assert.Null(s.Transit);
                    Assert.True(s.Order is null, $"{name}: Schiff {s.Id.Value} im Gefecht mit Auftrag");
                }
            }
            foreach (var e in u.Fights.Encounters)
            {
                Assert.True(u.Tick - e.StartTick <= u.Rules.CombatMaxTicks + 1, $"{name}: Gefecht {e.Id} läuft zu lange");
                foreach (var p in e.Participants.Where(p => p.Active)) Assert.True(u.Ships.ContainsKey(p.Ship));
            }
        }
        Assert.True(fights > 10, $"{name}: {fights} Gefechte");
        Assert.True(shots > 0);
        Assert.True(ended >= fights - u.Fights.Count);
        Assert.True(revenue > 0f && loot / revenue < 0.01f, $"{name}: Verlustquote {100 * loot / revenue:0.00} %");
        Assert.Subset(departedDestroyed, destroyedInCombat);   // jeder Gefechtsverlust ging über Departures (Grund a)
        foreach (var f in u.Factions.Keys)
            Assert.DoesNotContain(FactionViability.Check(u, f), i => i.Kind is ViabilityIssueKind.PoliceBelowMinimum or ViabilityIssueKind.NoSeat);
        // die Wirtschaft bleibt: Händler und Schürfer nicht eingebrochen
        var trade = ShipJobs.ByKey("trade")!; var mine = ShipJobs.ByKey("mine")!;
        Assert.True(ShipJobs.UsedUnits(u, trade) >= 0.8f * trade.Limit(u.Rules), $"{name}: Handel {ShipJobs.UsedUnits(u, trade):0.0}");
        Assert.True(ShipJobs.UsedUnits(u, mine) >= 0.8f * mine.Limit(u.Rules), $"{name}: Bergbau {ShipJobs.UsedUnits(u, mine):0.0}");
    }
}
