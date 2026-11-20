using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 28 (KAMPF.md Stufe 28): Raketen (Flug, Punktabwehr, Ausweichen), Stationsgeschütze, Angriff auf Stationen (Schaden, Zerstörung, Eroberung,
/// ausgeschaltete Regierungsstationen, Reparatur als Materialsenke), Kriegsschiffe der Fraktionen und Krieg (Erklärung, Belagerung, Kriegsmüdigkeit, Frieden);
/// dazu die Prüfpunkte aus Phase 27 (Festnahme-Abkühlzeit, Strafe als Schuld beim Ergeben).
/// </summary>
public class Phase28Tests
{
    /// <summary>Dreht Phase 28 in einer Weltdefinition zurück (Schalter aus, neue Inhalte entfernt, neue Regeln auf den Stand von Phase 27).</summary>
    internal static void RevertPhase28(WorldDef def)
    {
        Phase29Tests.RevertPhase29(def);   // Phase 29: Schiffsraster
        def.Rules!.Warfare = false;
        def.Rules.ArrestCooldownTicks = 0;
        def.Rules.SurrenderFineAsDebt = false;
        def.Rules.FenceShare = new EconomyRules().FenceShare;
        def.Rules.RaiderUpkeepPerTick = new EconomyRules().RaiderUpkeepPerTick;   // Nacharbeit: Standardwelt 0,03
        def.Rules.TraderBuyGuard = false;          // Nacharbeit: kein Einkauf mit sicherem Verlust
        def.Rules.BuildMissingProducer = false;    // Nacharbeit: fehlender letzter Hersteller zählt als knapp   // Prüfpunkt e: Standardwelt 0,75, bis Phase 27 der Code-Wert 0,6
        def.Rules.PirateBasePatchHull = 0f;        // Prüfung Phase 28: Notversorgung der Piraten im Versteck
        def.Rules.FleetWorldCapWhenDivided = false; // Prüfung Phase 28: Weltgrenze der Flotte im geteilten Zustand
        def.Rules.TraderPlanGuard = false;          // Prüfung Runde 2: Kaufschutz schon in der Planung
        def.Rules.CombatRejoinReuse = false;        // Runde 7: Wiedereintritt mit altem Gefechtseintrag
        RevertRunde9(def);
        string[] late = { "missile", "missile_launcher_s", "point_defense_s" };
        def.Goods.RemoveAll(g => g.Key == "missile");
        def.Modules.RemoveAll(m => late.Contains(m.Key));
        def.ShipClasses.RemoveAll(c => c.Key == "frigate");
        def.ShipModels.RemoveAll(m => m.Key == "bastion");
        def.Player.StartOptions.RemoveAll(o => o.Model == "bastion");   // Runde 5: Kriegstest im Bollwerk (Startpaket, nicht gewählt — ändert den Lauf nicht)
        foreach (var t in def.StationTypes)
        {
            t.Defense = null;
            if (t.Stocks is not null) foreach (var k in late) t.Stocks.Remove(k);
            t.Recipes.RemoveAll(r => r.Out.ContainsKey("missile"));
        }
    }

    /// <summary>Runde 9 zurückdrehen: Tanker und Kurier wieder nur für Spieler (ohne Laser, alte Grundmasse), Kanisterraum 30, Werft-Regal [1, 3].</summary>
    internal static void RevertRunde9(WorldDef def)
    {
        foreach (var (model, cls, mass) in new[] { ("cistern", "tanker", 16f), ("swift", "courier", 4f) })
        {
            var m = def.ShipModels.First(x => x.Key == model);
            m.Jobs = null;
            m.Fit.RemoveAt(m.Fit.Count - 1);
            var c = def.ShipClasses.First(x => x.Key == cls);
            c.Slots.RemoveAt(c.Slots.Count - 1);
            c.BaseMass = mass;
        }
        def.Modules.First(x => x.Key == "canister_bay_s").Stats["capacity"] = 30f;
        def.StationTypes.First(x => x.Key == "shipyard").Stocks!["canister_bay_s"] = new[] { 1f, 3f };
    }

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    // ───────── Schalter: Phase-27-Verhalten bleibt reproduzierbar ─────────

    [Fact]
    public void SchalterAus_StandardweltLaeuftBitIdentischZuPhase27()
    {
        // Balance-Tool, Standard-Seed, 20000 Ticks auf dem Stand Phase 27 (CLAUDE.md, zweimal gleich): CB2A9FDBF34B15AB
        var def = TestWorlds.Def(fuel: false, research: false);
        RevertPhase28(def);
        var u = WorldBuilder.Build(def);
        new SimulationRunner().Run(u, 20000);
        // Phase 29: der Hash mischt zusätzlich die Akteurskürzel (Actor.Tag) — geprüft: ohne diese Mischung bleibt der bisherige Wert; kein Verhalten geändert (vorher CB2A9FDBF34B15AB)
        Assert.Equal(0x3344626E1AC3A1AEUL, UniverseHash.Compute(u));   // Runde 43: Bergbau-Kontakt/konstanter Felsradius (vorher 0x81DFD311790D8AD1)
        // Runde 11: nur die Namensliste geändert (alte Liste: 0x92842D6D5BA8AE3F)
        // Runde 12: ohne die Kürzel (Namensliste) galt der Wert vor Phase 29 (0xCB2A9FDBF34B15AB); Runde 43 ändert das Verhalten (Felsradius konstant) → neuer Wert
        Assert.Equal(0x42F381B121541C18UL, UniverseHash.Compute(u, includeTags: false));
    }

    // ───────── Hilfen ─────────

    static readonly FactionId Union = new(1), Guild = new(2);

    static Universe Standard(ulong? seed = null, string? start = null) => TestWorlds.Default(seed, fuel: false, research: false, startOption: start);

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

    /// <summary>Alle Streifen und Kriegsschiffe aus einem Sektor schaffen (in den Heimatsektor ihrer Basis bzw. weit weg), damit ein Gefecht kontrolliert bleibt.</summary>
    static void ClearForces(Universe u, SectorId sector)
    {
        var away = u.Sectors.Keys.OrderBy(k => k.Value).First(k => k != sector && k != u.PlayerSector);
        foreach (var s in u.ShipsById().ToList())
            if ((ThreatMap.IsPolice(s) || ThreatMap.IsRaider(s) || Wars.IsWarship(s)) && ThreatMap.Where(u, s).Sector == sector) { Place(u, s, away, new Vec2(300f, 300f)); s.Command = null; }
    }

    static Station GuildEconomicStation(Universe u) => u.StationsById().First(st => !st.IsBuildSite && u.FactionOf(st) == Guild && StationProtection.IsEconomic(u, st)
        && st.Sector != u.PlayerSector && StationCombat.DefenseOf(u, st) is null);

    static void FillYard(Universe u, Station yard, string model)
    {
        foreach (var n in ShipPurchase.Materials(u, u.ShipModels[model])!)
        {
            var e = yard.Stock(n.Good);
            e.Amount = MathF.Max(e.Amount, n.Units * 4f);
            if (e.Price <= 0f) e.Price = u.Goods[n.Good].BasePrice;
        }
    }

    static void Hostile(Universe u, float rel = -0.95f)
    {
        Relations.Set(u, Union, Guild, rel);
        Relations.Set(u, Guild, Union, rel);
        u.Actors[Union].Treasury.Credits = 300_000f;
        u.Actors[Guild].Treasury.Credits = 300_000f;
    }

    // ───────── Inhalte ─────────

    [Fact]
    public void Inhalte_RaketeWerferPunktabwehrFregatteVerteidigung()
    {
        var u = Standard();
        var missile = Missiles.MissileGood(u);
        Assert.NotNull(missile);
        Assert.Equal("missile", missile!.Key);
        Assert.Equal(Missiles.Category, missile.Category);
        var launcher = u.Modules["missile_launcher_s"];
        Assert.True(Missiles.IsMissile(launcher));
        Assert.Equal(DamageType.Explosive, launcher.DamageType);
        Assert.False(Missiles.IsMissile(u.Modules["weapon_laser_s"]));
        Assert.True(u.Modules["point_defense_s"].Stat("pointDefense") > 0f);
        var model = u.ShipModels["bastion"];
        var stats = ShipStats.Compute(u.ShipClasses[model.ClassKey], model.Fit.Select(k => k is null ? null : u.Modules[k]).ToList());
        Assert.Equal(12f, stats.MissileCapacity);
        Assert.True(stats.PointDefense > 0f);
        Assert.True(stats.Speed > 2f, $"Fregatte zu langsam ({stats.Speed})");
        Assert.True(stats.WeaponPower > 15f);
        Assert.Empty(model.Jobs);   // nie ein Kaufkandidat der KI-Handelsflotte
        // Werften verkaufen die Fregatte (nicht gesperrt), Waffenwerk stellt Raketen her, Verteidigung an Regierungsstationen, Wachen und Werften
        var yard = u.StationsById().First(st => st.TypeKey == "shipyard");
        Assert.True(ShipPurchase.SellsModel(u, yard, model));
        Assert.Contains(u.StationTypes["weapons_works"].Recipes, r => r.Outputs.ContainsKey(missile.Id));
        foreach (var t in new[] { "headquarters", "sector_hq", "police_station", "shipyard" })
            Assert.True(u.StationTypes[t].Defense is { Guns.Count: > 0 }, t);
        Assert.Null(u.StationTypes["farm"].Defense);
        Assert.Equal(u.Rules.StationBaseHull * (u.Rules.EconomicStationDefense ? u.Rules.EconomicStationHullFactor : 1f), StationCombat.MaxHull(u, u.StationsById().First(st => st.TypeKey == "farm")));   // Ausgleich: Wirtschaftsstationen haben Hülle × Faktor
        Assert.Contains("missile_launcher_s", u.StationTypes["headquarters"].Defense!.Guns);
        // Hauptquartier nie erobern, nie zerstören (Regierung)
        var hq = u.StationsById().First(st => st.TypeKey == "headquarters");
        Assert.False(StationProtection.IsCapturable(u, hq));
        Assert.True(StationProtection.IsGovernment(u, hq));
    }

    [Theory]
    [InlineData("hull", "0", "defense.hull")]
    [InlineData("guns", "[\"nope\"]", "unbekanntes Geschütz")]
    [InlineData("guns", "[\"shield_mk1\"]", "keine Waffe")]
    [InlineData("pointDefense", "1.5", "pointDefense")]
    public void Verteidigung_UngueltigWirft(string field, string value, string message)
    {
        var json = WorldBuilder.DefaultWorldJson();
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var hq = node["stationTypes"]!.AsArray().First(t => (string?)t!["key"] == "headquarters")!;
        hq["defense"]![field] = System.Text.Json.Nodes.JsonNode.Parse(value);
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(WorldBuilder.Parse(node.ToJsonString())));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Explosivschaden_FaktorenGegenSchildUndPanzerung()
    {
        var r = new EconomyRules();
        var t = new TargetProfile(10f, 100f, 0f, 1f);
        var ex = Hit.Damage(30f, DamageType.Explosive, t, 1f, r);
        Assert.Equal(10f, ex.ShieldDamage, 3);
        // Rest nach dem Schild: 30 − 10/0,8 = 17,5; Panzerung 100 × 0,05 × 1,2 = 6
        Assert.Equal(6f, ex.ArmorAbsorbed, 3);
        Assert.Equal(11.5f, ex.HullDamage, 3);
    }

    // ───────── Raketen ─────────

    /// <summary>Kriegsschiff gegen Kaperschiff auf der Gefechtslinie außerhalb des Spielersektors (Abstand <paramref name="dist"/>).</summary>
    static (Encounter E, Ship Shooter, Ship Raider) MissileDuel(Universe u, float dist)
    {
        var sector = u.Sectors.Values.OrderBy(s => s.Id.Value).First(s => s.Id != u.PlayerSector && s.Owner == Union).Id;
        ClearForces(u, sector);
        var shooter = NewShip(u, "bastion", ShipRole.Warship, Union);
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        raider.Command = null;
        Place(u, shooter, sector, new Vec2(0f, 300f));
        Place(u, raider, sector, new Vec2(dist, 300f));
        var e = Engage.Begin(u, EncounterCause.Intercept, sector);
        e.Raider = raider.Id; e.RaiderOwner = raider.Owner; e.Police = shooter.Id;
        Engage.Join(u, e, shooter, 0, CombatTactics.Siege, 0f);
        Engage.Join(u, e, raider, 1, CombatTactics.Raider, dist);
        return (e, shooter, raider);
    }

    [Fact]
    public void Rakete_FliegtUndTrifftErstAmZiel_VerbrauchtMagazin()
    {
        var u = Standard();
        var (e, shooter, raider) = MissileDuel(u, 40f);   // in Laserreichweite: das fliehende Kaperschiff bleibt während des Flugs im Gefecht
        var ev = Capture(u);
        Fight(u, 1);
        // beide Werfer sind geladen und feuern im ersten Takt je eine Rakete
        var launches = ev.OfType<MissileLaunched>().Where(m => m.Shooter == shooter.Id).ToList();
        Assert.Equal(2, launches.Count);
        var launch = launches[0];
        Assert.Equal(raider.Id, launch.Target);
        Assert.Equal(2, e.Missiles.Count);
        Assert.Equal(2f, shooter.Systems.MissilesMissing);
        Assert.Empty(ev.OfType<MissileResolved>());
        // Flugzeit ≈ Abstand ÷ Tempo (40 ÷ 35 s ≈ 4,6 Ticks); vorher kein Treffer
        float flight = launch.FlightTicks;
        Assert.InRange(flight, 3f, 7f);
        Fight(u, (int)flight - 2);
        Assert.DoesNotContain(ev.OfType<MissileResolved>(), m => m.Shooter == shooter.Id);
        Fight(u, 20);
        Assert.Contains(ev.OfType<MissileResolved>(), m => m.Shooter == shooter.Id && m.Outcome is "hit" or "miss" or "intercepted");
    }

    [Fact]
    public void Rakete_Punktabwehr_FaengtAnteilAb_ZiehtGenauDieZuege()
    {
        var u = Standard();
        var (e, shooter, raider) = MissileDuel(u, 100f);
        // Ziel: die Fregatte selbst (Punktabwehr 0,35), Schütze das Kaperschiff
        var tp = e.Of(shooter.Id)!;
        int intercepted = 0, n = 2000;
        for (int i = 0; i < n; i++)
        {
            shooter.Hull = shooter.MaxHull;
            shooter.Systems.ShieldMissing = 0f;
            ulong before = u.Rng.State;
            e.Missiles.Add(new MissileFlight { Shooter = raider.Id, Owner = raider.Owner, Side = 1, Module = "missile_launcher_s", Target = shooter.Id, Left = 0f, Total = 50f, Speed = 35f });
            var ev = Capture(u);
            Missiles.Step(u, e, 0.25f);
            var res = Assert.Single(ev.OfType<MissileResolved>());
            if (res.Outcome == "intercepted") intercepted++;
            Assert.NotEqual(before, u.Rng.State);
        }
        Assert.InRange(intercepted / (float)n, 0.31f, 0.39f);
        // ohne Punktabwehr nie abgefangen
        shooter.Fit[shooter.Fit.IndexOf("point_defense_s")] = null;
        shooter.RecomputeStats(u);
        int none = 0;
        for (int i = 0; i < 200; i++)
        {
            shooter.Hull = shooter.MaxHull;
            e.Missiles.Add(new MissileFlight { Shooter = raider.Id, Owner = raider.Owner, Side = 1, Module = "missile_launcher_s", Target = shooter.Id, Left = 0f, Total = 50f, Speed = 35f });
            var ev = Capture(u);
            Missiles.Step(u, e, 0.25f);
            if (ev.OfType<MissileResolved>().Single().Outcome == "intercepted") none++;
        }
        Assert.Equal(0, none);
    }

    [Fact]
    public void Rakete_ZielEntkommen_VerfaelltOhneZufallszug()
    {
        var u = Standard();
        var (e, shooter, raider) = MissileDuel(u, 100f);
        e.Missiles.Add(new MissileFlight { Shooter = shooter.Id, Owner = shooter.Owner, Module = "missile_launcher_s", Target = raider.Id, Left = 0f, Total = 50f, Speed = 35f });
        Engage.Leave(u, e, e.Of(raider.Id)!, CombatExit.Fled);
        ulong before = u.Rng.State;
        var ev = Capture(u);
        Missiles.Step(u, e, 0.25f);
        Assert.Equal("lost", Assert.Single(ev.OfType<MissileResolved>()).Outcome);
        Assert.Equal(before, u.Rng.State);
    }

    [Fact]
    public void Rakete_LeeresMagazin_Schweigt_UndNachladenAnDerBasis()
    {
        var u = Standard();
        var (e, shooter, raider) = MissileDuel(u, 100f);
        shooter.Systems.MissilesMissing = shooter.Stats.MissileCapacity;
        var ev = Capture(u);
        Fight(u, 4);
        Assert.DoesNotContain(ev.OfType<MissileLaunched>(), m => m.Shooter == shooter.Id);
        // an der Heimatbasis aus der Kasse nachgeladen
        CombatSystem.Finish(u, e, "ended");
        shooter.HomeBase = SecuritySystem.HomeBaseFor(u, Union, null)!.Id;
        u.Actors[Union].Treasury.Credits = 100_000f;
        float before = u.Actors[Union].Treasury.Credits;
        PirateSystem.ServiceAtBase(u, shooter, "navy");
        Assert.Equal(0f, shooter.Systems.MissilesMissing);
        Assert.True(u.Actors[Union].Treasury.Credits < before);
    }

    // ───────── Stationsgeschütze ─────────

    static (Station St, Ship Raider, Ship Victim) RaidNear(Universe u, float raiderDist)
    {
        var st = u.StationsById().First(x => x.TypeKey == "sector_hq" && x.Sector != u.PlayerSector);
        ClearForces(u, st.Sector);
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        raider.Command = null;
        var victim = u.ShipsById().First(s => s.Role == ShipRole.Trader && s.Controller == Controller.Ai);
        Place(u, victim, st.Sector, st.Position + new Vec2(45f, 0f));
        Place(u, raider, st.Sector, st.Position + new Vec2(raiderDist, 0f));
        victim.Cargo[u.Goods.Values.First(g => g.Key == "food").Id] = 20f;
        return (st, raider, victim);
    }

    [Fact]
    public void Stationsgeschuetz_SchiesstAufKaperer_InReichweite_NieAufDasOpfer()
    {
        var u = Standard();
        var (st, raider, victim) = RaidNear(u, 55f);
        var ev = Capture(u);
        Engage.StartRaid(u, raider, victim, u.Sectors[st.Sector]);
        Fight(u, 60);
        var shots = ev.OfType<StationShot>().ToList();
        Assert.NotEmpty(shots);
        Assert.All(shots, s => { Assert.Equal(st.Id, s.Station); Assert.Equal(raider.Id, s.Target); });
    }

    [Fact]
    public void Stationsgeschuetz_AusserReichweite_UndSchalterAus_SchweigenDieGeschuetze()
    {
        var u = Standard();
        var (st, raider, victim) = RaidNear(u, 140f);
        victim.Position = st.Position + new Vec2(110f, 0f);
        var ev = Capture(u);
        Engage.StartRaid(u, raider, victim, u.Sectors[st.Sector]);
        Fight(u, 30);
        Assert.Empty(ev.OfType<StationShot>());
        var u2 = Standard();
        u2.Rules.Warfare = false;
        var (st2, raider2, victim2) = RaidNear(u2, 55f);
        var ev2 = Capture(u2);
        Engage.StartRaid(u2, raider2, victim2, u2.Sectors[st2.Sector]);
        Fight(u2, 30);
        Assert.Empty(ev2.OfType<StationShot>());
    }

    // ───────── Belagerung, Fall der Station, Reparatur ─────────

    static (Ship Ship, Station St) SiegeSetup(Universe u, Station st)
    {
        Wars.Declare(u, Union, Guild);
        ClearForces(u, st.Sector);
        var ship = NewShip(u, "bastion", ShipRole.Warship, Union);
        Place(u, ship, st.Sector, st.Position + new Vec2(70f, 0f));
        return (ship, st);
    }

    [Fact]
    public void Belagerung_WirtschaftsstationFaellt_UeberDepartures()
    {
        var u = Standard();
        var (ship, st) = SiegeSetup(u, GuildEconomicStation(u));
        st.Condition = new StationCondition { HullMissing = StationCombat.MaxHull(u, st) - 60f };
        var ev = Capture(u);
        var e = Siege.Start(u, ship, st);
        Assert.Equal(EncounterCause.Siege, e.Cause);
        Assert.Equal(st.Id, e.Station);
        Fight(u, 600, () => !u.Stations.ContainsKey(st.Id));
        Assert.False(u.Stations.ContainsKey(st.Id));
        var dep = Assert.Single(ev.OfType<StationDeparted>());
        Assert.Equal(DepartureReason.Destroyed, dep.Reason);
        Assert.Contains(ev.OfType<StationFell>(), f => f.How == "destroyed" && f.Station == st.Id);
        // Phase 29: Geschütz oder Rakete — je nach Zufallspfad fällt die Station unter einem Kanonen- oder einem Raketentreffer
        Assert.True(ev.OfType<ShotAtStation>().Any(s => s.Station == st.Id && s.Hit) || ev.OfType<MissileResolved>().Any(m => m.TargetStation == st.Id && m.Outcome == "hit"));
        Fight(u, 5);
        Assert.False(u.Fights.IsEngaged(ship.Id));
        Assert.True(Wars.Between(u, Union, Guild)!.LossB > 0f);
    }

    [Fact]
    public void Belagerung_RegierungsstationWirdAusgeschaltet_NieZerstoert_UndRepariertSichWieder()
    {
        var u = Standard();
        u.Rules.StationCapitulation = false;   // prüft die Phase-28-Regel (Regierungsstation nur ausgeschaltet); Kapitulation/Zerstörung: KapitulationTests
        var hq = u.StationsById().First(x => x.TypeKey == "sector_hq" && u.FactionOf(x) == Guild && x.Sector != u.PlayerSector);
        var (ship, st) = SiegeSetup(u, hq);
        st.Condition = new StationCondition { HullMissing = StationCombat.MaxHull(u, st) - 40f, ShieldMissing = StationCombat.MaxShield(u, st) };
        var ev = Capture(u);
        Siege.Start(u, ship, st);
        Fight(u, 600, () => StationCombat.IsDown(u, st));
        Assert.True(u.Stations.ContainsKey(st.Id));
        Assert.True(StationCombat.IsDown(u, st));
        Assert.Contains(ev.OfType<StationFell>(), f => f.How == "disabled");
        Assert.Empty(ev.OfType<StationDeparted>());
        Fight(u, 5);
        Assert.False(u.Fights.IsEngaged(ship.Id));   // ausgeschaltet = kein Ziel mehr, die Belagerung endet
        Assert.Null(Wars.PickTarget(u, Union, ship) is { } t && t.Id == st.Id ? t : null);
        // Reparatur: Hüllenplatten aus dem eigenen Lager, Rest aus der Kasse; wieder eingeschaltet ab dem Schwellenanteil
        var plates = u.Goods.Values.First(g => g.Key == "hull_plates").Id;
        st.Stock(plates).Amount = 3f;
        u.Actors[Guild].Treasury.Credits = 100_000f;
        float treasury = u.Actors[Guild].Treasury.Credits;
        var repair = new StationRepairSystem();
        for (int i = 0; i < 40 && StationCombat.IsDown(u, st); i++) { u.Tick += 50; repair.Step(u, 50); }
        Assert.False(StationCombat.IsDown(u, st));
        Assert.True(st.Stock(plates).Amount < 3f, "Hüllenplatten aus dem Lager verbraucht");
        Assert.True(u.Actors[Guild].Treasury.Credits < treasury, "Rest aus der Kasse bezahlt");
        Assert.Contains(ev.OfType<StationRepaired>(), r => r.Station == st.Id && r.Plates > 0f);
        // vollständig repariert → kein Zustand mehr
        for (int i = 0; i < 400 && st.Condition is not null; i++) { u.Tick += 50; repair.Step(u, 50); }
        Assert.Null(st.Condition);
    }

    [Fact]
    public void Eroberung_NurMitRegel_BesitzerWechselt()
    {
        var u = Standard();
        u.Rules.StationCapitulation = false;   // prüft die Phase-28-Regel (Regierungsstation nur ausgeschaltet); Kapitulation/Zerstörung: KapitulationTests
        u.Rules.AllowStationCapture = true;
        var (ship, st) = SiegeSetup(u, GuildEconomicStation(u));
        st.Condition = new StationCondition { HullMissing = StationCombat.MaxHull(u, st) - 60f };
        var ev = Capture(u);
        Siege.Start(u, ship, st);
        Fight(u, 600, () => st.Owner == (ActorId)Union);
        Assert.True(u.Stations.ContainsKey(st.Id));
        Assert.Equal((ActorId)Union, st.Owner);
        Assert.Contains(ev.OfType<StationFell>(), f => f.How == "captured");
        Assert.False(StationCombat.IsDown(u, st));
        Fight(u, 5);
        Assert.False(u.Fights.IsEngaged(ship.Id));
    }

    [Fact]
    public void Stationsschaden_SpeichernLadenBitIdentisch_MitRaketenImFlug()
    {
        var u = Standard();
        var (ship, st) = SiegeSetup(u, GuildEconomicStation(u));
        Siege.Start(u, ship, st);
        var runner = new SimulationRunner();
        for (int i = 0; i < 2000 && ((u.Fights.Of(ship.Id)?.Missiles.Count ?? 0) == 0 || st.Condition is null); i++) runner.Step(u);
        Assert.True(u.Fights.Of(ship.Id) is { Missiles.Count: > 0 }, "keine Rakete im Flug");
        Assert.NotNull(st.Condition);
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (copy, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        var r2 = new SimulationRunner();
        for (int i = 0; i < 300; i++) { runner.Step(u); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
    }

    // ───────── Krieg ─────────

    [Fact]
    public void Krieg_StandardweltBleibtFriedlich_KeinKriegsgrund()
    {
        var u = Standard();
        Assert.Equal("Kein Kriegsgrund.", Wars.DeclareIssue(u, Union, Guild));
        Assert.True(Relations.Effective(u, Union, Guild) > Wars.DeclareThreshold(u, Union));
    }

    [Fact]
    public void Krieg_Erklaerung_NurMitGrundKasseUndStaerke()
    {
        var u = Standard();
        Hostile(u);
        Assert.Null(Wars.DeclareIssue(u, Union, Guild));
        u.Actors[Union].Treasury.Credits = 0f;
        Assert.Equal("Keine Kriegskasse.", Wars.DeclareIssue(u, Union, Guild));
        u.Actors[Union].Treasury.Credits = 300_000f;
        u.Rules.WarMinStrengthRatio = 50f;
        Assert.Equal("Klar unterlegen.", Wars.DeclareIssue(u, Union, Guild));
        u.Rules.WarMinStrengthRatio = 0.6f;
        u.Rules.Warfare = false;
        Assert.NotNull(Wars.DeclareIssue(u, Union, Guild));
        u.Rules.Warfare = true;
        var ev = Capture(u);
        new WarSystem().Step(u, WarSystem.Interval);
        var decl = Assert.Single(ev.OfType<WarDeclared>());
        Assert.True(Wars.AtWar(u, Union, Guild));
        Assert.True(Relations.Effective(u, decl.A, decl.B) <= u.Rules.WarRelation + 1e-3f);
        // höchstens ein Krieg je Paar
        new WarSystem().Step(u, WarSystem.Interval);
        Assert.Single(u.Wars);
    }

    [Fact]
    public void Krieg_EndetDurchMuedigkeit_UndAbkuehlzeit()
    {
        var u = Standard();
        Hostile(u);
        u.Rules.WarWearyTicks = 1000;
        u.Rules.WarMinTicks = 500;
        Wars.Declare(u, Union, Guild);
        var ev = Capture(u);
        u.Tick += 400;
        new WarSystem().Step(u, WarSystem.Interval);
        Assert.Single(u.Wars);
        u.Tick += 700;
        new WarSystem().Step(u, WarSystem.Interval);
        var peace = Assert.Single(ev.OfType<PeaceConcluded>());
        Assert.Equal("weary", peace.Reason);
        Assert.Empty(u.Wars);
        Assert.True(Relations.Effective(u, Union, Guild) >= u.Rules.PeaceRelation - 1e-3f);
        Hostile(u);
        Assert.Equal("Frieden ist zu frisch.", Wars.DeclareIssue(u, Union, Guild));
        u.Tick += u.Rules.PeaceCooldownTicks;
        Assert.Null(Wars.DeclareIssue(u, Union, Guild));
    }

    [Fact]
    public void Krieg_LeereKasse_ErzwingtFrieden_KeineFraktionHandlungsunfaehig()
    {
        var u = Standard();
        Hostile(u);
        Wars.Declare(u, Union, Guild);
        u.Tick = u.Rules.FactionReserveBuildTicks + 100;
        u.Actors[Guild].Treasury.Credits = 0f;
        var ev = Capture(u);
        new WarSystem().Step(u, WarSystem.Interval);
        Assert.Equal("broke", Assert.Single(ev.OfType<PeaceConcluded>()).Reason);
        Assert.Empty(u.Wars);
    }

    [Fact]
    public void Kriegsschiffe_KaufNurImKriegAusVerfuegbaremGeld_AngriffsbefehlUndAbbauNachDemFrieden()
    {
        var u = Standard();
        u.Rules.WarDefense = false; u.Rules.WarEscorts = false;   // Ausbau Punkt 2: Abwehr/Geleit gehen dem Angriffsbefehl vor (eigener Test KiKriegsschiffeTests)
        Hostile(u);
        var navy = new NavySystem();
        foreach (var yard in u.StationsById().Where(st => st.TypeKey == "shipyard")) FillYard(u, yard, "bastion");
        navy.Step(u, NavySystem.Interval);
        Assert.Empty(Wars.WarshipsOf(u, Union));   // Frieden: keine Kriegsschiffe
        Wars.Declare(u, Union, Guild);
        var ev = Capture(u);
        for (int i = 0; i < 4; i++) { u.Tick += NavySystem.Interval; navy.Step(u, NavySystem.Interval); }
        Assert.Equal(u.Rules.WarshipsPerWar, Wars.WarshipsOf(u, Union).Count);
        Assert.Equal(u.Rules.WarshipsPerWar, Wars.WarshipsOf(u, Guild).Count);
        Assert.Contains(ev.OfType<WarshipCommissioned>(), c => c.Faction == Union);
        Assert.All(Wars.WarshipsOf(u, Union), s => Assert.IsType<AssaultCommand>(s.Command));
        // Phase 31: mit Torkampf zuerst die Torblockade (ein Tor mit einem Ende bei der Gilde), sonst eine Gildenstation
        Assert.All(Wars.WarshipsOf(u, Union), s => Assert.True(((AssaultCommand)s.Command!) is var ac && (ac.Gate.Value != 0
            ? GateCombat.DefendersOf(u, u.Gates[ac.Gate]).Contains(Guild) : u.FactionOf(u.Stations[ac.Station]) == Guild)));
        Assert.Contains(ev.OfType<TreasuryUpkeep>(), t => t.Purpose == "navy");
        // Kaufen nur aus dem verfügbaren Geld über der Rücklage
        var f3 = new FactionId(3);
        Assert.Empty(Wars.WarshipsOf(u, f3));
        // Frieden → nach der Frist abgebaut (Grund c), nie während der Frist
        Wars.MakePeace(u, Wars.Between(u, Union, Guild)!, "manual");
        u.Tick += 100; navy.Step(u, NavySystem.Interval);
        Assert.Equal(u.Rules.WarshipsPerWar, Wars.WarshipsOf(u, Union).Count);
        u.Tick += u.Rules.WarshipPeaceTicks;
        for (int i = 0; i < 4; i++) { u.Tick += NavySystem.Interval; navy.Step(u, NavySystem.Interval); }
        Assert.Empty(Wars.WarshipsOf(u, Union));
        Assert.Contains(ev.OfType<ShipDeparted>(), d => d.Reason == DepartureReason.Unprofitable && d.Detail == "decommissioned");
    }

    [Fact]
    public void Kriegsschiffe_KeinKaufUnterDerRuecklage()
    {
        var u = Standard();
        Hostile(u);
        foreach (var yard in u.StationsById().Where(st => st.TypeKey == "shipyard")) FillYard(u, yard, "bastion");
        Wars.Declare(u, Union, Guild);
        u.Actors[Union].Treasury.Credits = FactionViability.Reserve(u, Union) + 100f;
        Assert.Null(NavySystem.Commission(u, Union));
        Assert.True(u.Actors[Union].Treasury.Credits >= FactionViability.Reserve(u, Union));
    }

    // ───────── Spieler ─────────

    [Fact]
    public void Spieler_GreiftStationAn_Belagerung_FahndungUndGegenwehr()
    {
        var u = Standard(start: "fighter");
        var me = u.Ships[u.PlayerShip];
        var st = u.StationsById().First(x => x.Sector == u.PlayerSector && u.FactionOf(x) is not null && !x.IsBuildSite && StationCombat.DefenseOf(u, x) is not null);
        me.DockedAt = default;
        me.Position = st.Position + new Vec2(0f, 40f);
        float heading = FiringArc.Bearing((st.Position - me.Position).Normalized());
        int slot = PlayerGunnery.WeaponSlots(u, me).First();
        var f = u.FactionOf(st)!.Value;
        var ev = Capture(u);
        var shot = PlayerGunnery.TryFireAtStation(u, me, slot, st.Id, heading);
        Assert.Equal(GunResult.Fired, shot.Result);
        var e = u.Fights.Of(me.Id);
        Assert.NotNull(e);
        Assert.Equal(EncounterCause.Siege, e!.Cause);
        Assert.Equal(st.Id, e.Station);
        Assert.True(Wanted.LevelOf(u, me.Owner, f) >= 3, "schwerer Angriff gemeldet");
        Assert.Contains(ev.OfType<ShotAtStation>(), x => x.Station == st.Id);
        // die Station wehrt sich (Geschütze auf den Angreifer)
        Fight(u, 40);
        Assert.Contains(ev.OfType<StationShot>(), x => x.Station == st.Id && x.Target == me.Id);
        // eigene Station, Schalter aus: nicht erlaubt
        u.Rules.Warfare = false;
        Assert.Equal(GunResult.NotAllowed, PlayerGunnery.TryFireAtStation(u, me, slot, st.Id, heading).Result);
    }

    [Fact]
    public void Spieler_Rakete_UeberDieGemeinsameRegel()
    {
        var u = Standard(start: "fighter");
        var me = u.Ships[u.PlayerShip];
        int slot = me.Fit.IndexOf("weapon_massdriver_s");
        me.Fit[slot] = "missile_launcher_s";
        me.RecomputeStats(u);
        Assert.Equal(6f, me.Stats.MissileCapacity);
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        raider.Command = null;
        me.DockedAt = default;
        var spot = me.Position + new Vec2(0f, -200f);
        me.Position = spot;
        Place(u, raider, u.PlayerSector, spot + new Vec2(0f, -50f));
        float heading = FiringArc.Bearing(new Vec2(0f, -1f));
        Assert.Equal(BayState.Ready, PlayerGunnery.State(u, me, slot, heading, raider.Id));
        var ev = Capture(u);
        var shot = PlayerGunnery.TryFire(u, me, slot, raider.Id, heading);
        Assert.Equal(GunResult.Fired, shot.Result);
        Assert.Single(ev.OfType<MissileLaunched>(), m => m.Shooter == me.Id);
        Assert.Equal(1f, me.Systems.MissilesMissing);
        Fight(u, 30);
        Assert.Contains(ev.OfType<MissileResolved>(), m => m.Shooter == me.Id);
        // leeres Magazin → keine Raketen
        me.Systems.MissilesMissing = me.Stats.MissileCapacity;
        Assert.Equal(BayState.NoAmmo, PlayerGunnery.State(u, me, slot, heading, raider.Id));
    }

    /// <summary>Spieler bei der Sektorfraktion gesucht (Stufe ≥ 3), eine Streife im Sektor.</summary>
    static (Ship Me, Sector Sec) WantedPlayer(Universe u)
    {
        var me = u.Ships[u.PlayerShip];
        var sec = u.Sectors[me.Sector];
        var actor = u.Actors[me.Owner];
        Wanted.Report(u, me.Owner, sec.Owner, "assault");
        Wanted.SetHeat(u, actor, sec.Owner, 3.5f, 500f);
        me.DockedAt = default;
        var cop = SecuritySystem.PoliceOf(u, sec.Owner).First();
        cop.Command = null;
        Place(u, cop, sec.Id, me.Position + new Vec2(60f, 0f));
        return (me, sec);
    }

    [Fact]
    public void Festnahme_AbkuehlzeitNachFlucht_KeineSchleife()
    {
        var u = Standard();
        var (me, sec) = WantedPlayer(u);
        var sys = new SecuritySystem();
        sys.Step(u, 5);
        var pending = Assert.Single(u.PendingEncounters, p => p.Target == me.Id);
        // Spieler weicht aus (anderer Sektor) → entzogen, Abkühlzeit
        var away = u.Sectors.Keys.First(k => k != sec.Id);
        u.MoveShip(me, away);
        u.Tick = pending.DueTick;
        sys.Step(u, 5);
        Assert.Empty(u.PendingEncounters);
        Assert.True(u.Actors[me.Owner].ArrestCooldownUntil > u.Tick);
        // zurück: keine neue Warnung während der Abkühlzeit
        u.MoveShip(me, sec.Id);
        u.Tick += 10;
        sys.Step(u, 5);
        Assert.Empty(u.PendingEncounters);
        u.Tick = u.Actors[me.Owner].ArrestCooldownUntil + 1;
        if (!PirateSystem.PoliceIn(u, sec.Id).Any()) Place(u, SecuritySystem.PoliceOf(u, sec.Owner).First(), sec.Id, me.Position + new Vec2(60f, 0f));
        sys.Step(u, 5);
        Assert.Single(u.PendingEncounters, p => p.Target == me.Id);
    }

    [Fact]
    public void Ergeben_LeereKasse_StrafeWirdSchuld_FahndungGeloescht()
    {
        var u = Standard();
        var (me, sec) = WantedPlayer(u);
        var wallet = u.WalletOf(me);
        wallet.Credits = 0f;
        float fine = Wanted.FineOf(u, u.Actors[me.Owner].Wanted[sec.Owner]);
        new SecuritySystem().Step(u, 5);
        Assert.NotNull(u.PendingEncounters.FirstOrDefault(p => p.Target == me.Id));
        var res = PlayerSurrender.Surrender(u);
        Assert.NotNull(res);
        Assert.True(res!.ToPolice);
        Assert.Equal(-fine, wallet.Credits, 1);
        Assert.Equal(0, Wanted.LevelOf(u, me.Owner, sec.Owner));
        Assert.True(u.Actors[me.Owner].ArrestCooldownUntil > u.Tick);
        // Regel aus: wie Phase 27 (nur was die Kasse hat, Fahndung auf „verwarnt“)
        var u2 = Standard();
        u2.Rules.SurrenderFineAsDebt = false;
        var (me2, sec2) = WantedPlayer(u2);
        u2.WalletOf(me2).Credits = 0f;
        new SecuritySystem().Step(u2, 5);
        PlayerSurrender.Surrender(u2);
        Assert.Equal(0f, u2.WalletOf(me2).Credits);
        Assert.True(Wanted.LevelOf(u2, me2.Owner, sec2.Owner) > 0);
    }

    // ───────── Langläufe ─────────

    [Fact]
    public void Standardwelt_40000Ticks_KeinKrieg_KeineKriegsschiffe_Handlungsfaehig()
    {
        var u = Standard();
        var ev = Capture(u);
        var runner = new SimulationRunner();
        for (int i = 0; i < 40000; i++) runner.Step(u);
        Assert.Empty(u.Wars);
        Assert.Empty(ev.OfType<WarDeclared>());
        Assert.DoesNotContain(u.ShipsById().Where(s => !Bandits.IsEstablished(u, s.Owner)), Wars.IsWarship);   // Punkt 17: die Banditenfraktion startet mit Kriegsschiffen
        Assert.Empty(ev.OfType<StationFell>());   // Runde 13b: wieder streng (Piraten bauen keine Stellungen mehr)
        foreach (var f in u.NpcFactionIds().Where(f => !Bandits.IsEstablished(u, f)))   // Runde 38: KI-Fraktionen (die Spielerfraktion hat keine Polizei); Punkt 17: Banditen ohne Polizei und Krieg (Vorgabe Gesetzlos)
        {
            Assert.True(SecuritySystem.PoliceOf(u, f).Count >= u.Rules.PoliceMinPerFaction, $"Polizei {f.Value}");
            Assert.True(Relations.Effective(u, f, u.NpcFactionIds().First(k => k != f && !Bandits.IsEstablished(u, k))) > Wars.DeclareThreshold(u, f));
        }
        float revenue = ev.OfType<TradeCompleted>().Sum(t => t.Revenue);
        float loot = ev.OfType<RaidResolved>().Sum(r => r.LootValue);
        Assert.True(loot < 0.01f * revenue, $"Verlustquote {loot / revenue:P2}");
    }

    [Fact]
    public void Testwelt_KriegLaeuftDurch_ErklaerungKriegsschiffeBelagerungFrieden()
    {
        var u = Standard();
        Hostile(u);
        u.Rules.WarWearyTicks = 20000;
        u.Rules.WarLossScale = 1e9f;
        foreach (var yard in u.StationsById().Where(st => st.TypeKey == "shipyard")) FillYard(u, yard, "bastion");
        var ev = Capture(u);
        var runner = new SimulationRunner();
        for (int i = 0; i < 26000; i++) runner.Step(u);
        Assert.Contains(ev.OfType<WarDeclared>(), w => (w.A == Union && w.B == Guild) || (w.A == Guild && w.B == Union));
        Assert.Contains(ev.OfType<WarshipCommissioned>(), _ => true);
        Assert.Contains(ev.OfType<SiegeStarted>(), _ => true);
        Assert.Contains(ev.OfType<ShotAtStation>(), _ => true);
        // Gerätetest Phase 28: seit Belagerungen am Rand der Waffenreichweite nicht mehr sofort als Flucht enden, kosten sie länger — der Frieden kommt je nach
        // Pfad aus Kriegsmüdigkeit oder leerer Kriegskasse (beides ein regulärer Kriegsausgang)
        Assert.Contains(ev.OfType<PeaceConcluded>(), p => p.Reason is "weary" or "broke" or "tribute");   // Runde 45: auch Tributfrieden (Gasernte verschob den Zufallsweg)
        Assert.Empty(u.Wars);
        // keine Fraktion handlungsunfähig: Mindestpolizei, Sitz, Kasse über null
        foreach (var f in new[] { Union, Guild })
        {
            Assert.True(SecuritySystem.PoliceOf(u, f).Count >= u.Rules.PoliceMinPerFaction, $"Polizei {f.Value}");
            Assert.Contains(u.StationsById(), st => st.Owner == (ActorId)f && st.TypeKey == "headquarters");
            Assert.True(u.Actors[f].Treasury.Credits >= 0f);
        }
        // jeder Stationsverlust über Departures
        foreach (var fell in ev.OfType<StationFell>().Where(x => x.How == "destroyed"))
            Assert.Contains(ev.OfType<StationDeparted>(), d => d.Station == fell.Station && d.Reason == DepartureReason.Destroyed);
    }

    // ───────── Prüfpunkt f: Nachholen in Teilen ─────────

    [Fact]
    public void Nachholen_InTeilen_GleichesErgebnisWieAmStueck_RegelWiederhergestellt()
    {
        var a = Standard();
        var b = Standard();
        var ra = new SimulationRunner();
        var rb = new SimulationRunner();
        var res = OfflineCatchUp.Run(a, ra, TimeSpan.FromSeconds(500));
        var job = OfflineCatchUp.Begin(b, TimeSpan.FromSeconds(500));
        Assert.False(b.Rules.PlayerRaids);   // während des Nachholens keine Überfälle auf den Spieler
        while (!job.Done) job.Step(rb, 137);
        job.Finish();
        Assert.Equal(res.TicksRun, job.Result.TicksRun);
        Assert.Equal(2000, job.Result.TicksRun);
        Assert.True(b.Rules.PlayerRaids);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        // Abbruch vor dem Ende stellt die Regel ebenfalls wieder her
        var c = Standard();
        var j2 = OfflineCatchUp.Begin(c, TimeSpan.FromHours(10));
        Assert.Equal(OfflineCatchUp.DefaultMaxTicks, j2.Total);
        j2.Step(new SimulationRunner(), 50);
        j2.Finish();
        Assert.True(c.Rules.PlayerRaids);
        Assert.Equal(50, j2.Ran);
    }

    // ───────── Befunde des Gerätetests (Phase-27-APK) ─────────

    [Fact]
    public void Ausruester_BeschaedigtesModul_GrundIstErstReparieren_NichtCredits()
    {
        var u = Standard(start: "fighter");
        var me = u.Ships[u.PlayerShip];
        var yard = u.StationsById().First(st => st.TypeKey == "shipyard");
        int engine = me.Fit.FindIndex(k => k is not null && u.Modules[k].Stats.ContainsKey("thrust"));
        SpaceSim.Core.Combat.ModuleDamage.SetCondition(u, me, engine, 0.21f);
        u.WalletOf(me).Credits = 3000f;
        new SimulationRunner().Step(u);   // Preise
        var offers = new SpaceSim.Core.Services.OutfitterService().Offers(u, yard, me);
        var swaps = offers.Where(o => o.Key.StartsWith($"fit:{engine}:", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(swaps);
        Assert.All(swaps, o => { Assert.Equal(0f, o.Amount); Assert.Contains("erst reparieren", o.Note ?? ""); });
        var res = new SpaceSim.Core.Services.OutfitterService().Execute(u, yard, me, new SpaceSim.Core.Services.ServiceRequest(swaps[0].Key, default, 0f));
        Assert.False(res.Success);
        Assert.Contains("erst reparieren", res.Message);
    }

    [Fact]
    public void AngedocktesFremdschiff_KeinZiel_UndKeinGegner()
    {
        var u = Standard(start: "fighter");
        var me = u.Ships[u.PlayerShip];
        me.DockedAt = default;
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        Place(u, raider, me.Sector, me.Position + new Vec2(0f, -30f));
        raider.DockedAt = u.StationsById().First(st => st.Sector == me.Sector).Id;   // liegt angedockt
        int slot = PlayerGunnery.WeaponSlots(u, me).First();
        float heading = FiringArc.Bearing(new Vec2(0f, -1f));
        Assert.Equal(BayState.NoTarget, PlayerGunnery.State(u, me, slot, heading, raider.Id));
        Assert.DoesNotContain(raider.Id, PlayerCombat.Enemies(u, me));
        raider.DockedAt = default;
        Assert.Contains(raider.Id, PlayerCombat.Enemies(u, me));
    }

    [Fact]
    public void Startpaket_Gefechtstest_UeberfallKommtNachSekunden_HaendlerUndStreifenverband()
    {
        var u = Standard(start: "combat");
        var me = u.Ships[u.PlayerShip];
        Assert.Equal("falcon", me.ModelKey);
        Assert.False(ThreatMap.NearStation(u, me.Sector, me.Position));
        // zweiter Gerätetest Phase 28: Anführer vor dem Bug außerhalb der eigenen Waffenreichweite, 40 % Hülle ohne Schild; der zweite Kaperer kommt
        // frühestens 60 s nach dem Überfallbeginn (eigene Ankündigung, kein Rudel)
        var raids = u.PendingEncounters.Where(p => p.Kind == EncounterKind.Raid && p.Target == me.Id).OrderBy(p => p.DueTick).ToList();
        Assert.Equal(2, raids.Count);
        var lead = u.Ships[raids[0].Source];
        float reach = CombatSense.MaxRange(u, me);
        Assert.InRange(Vec2.Distance(lead.Position, me.Position), reach, reach + 80f);
        Assert.InRange(Vec2.Distance(lead.Position, me.Position), CombatSense.MaxRange(u, lead), 200f);   // außerhalb seiner Waffenreichweite
        // vor dem Bug (Blickrichtung −y; die Ansicht richtet die Nase beim Sitzungsbeginn genau auf den angekündigten Kaperer aus)
        Assert.InRange(MathF.Abs(FiringArc.Bearing(lead.Position - me.Position)), 0f, 20f);
        Assert.Equal(lead.MaxHull * 0.4f, lead.Hull, 2);
        Assert.Equal(0f, lead.Stats.Shield);
        Assert.Equal(u.Tick + 60, raids[0].DueTick);   // volle 15 s Warnung
        Assert.True(raids[1].DueTick - raids[0].DueTick >= 240, "Verstärkung frühestens 60 s nach dem Überfallbeginn");
        Assert.True(Vec2.Distance(u.Ships[raids[1].Source].Position, me.Position) > 150f);
        Assert.Null(Squadrons.Get(u, lead));
        Assert.Contains(u.ShipsById(), s => s.Role == ShipRole.Trader && s.Sector == me.Sector && s.DockedAt.Value == 0 && Vec2.Distance(s.Position, me.Position) < 60f);
        var cops = SecuritySystem.PoliceOf(u, u.Sectors[me.Sector].Owner).Where(c => c.Sector == me.Sector).ToList();
        Assert.True(cops.Count >= 2 && cops[0].Squadron.Value != 0 && cops[0].Squadron == cops[1].Squadron, "Streife als Verband");
        // kleiner Frachtraum mit Fracht: „Ergeben“ ist im Gefecht möglich
        Assert.True(me.Stats.Cargo > 0f && me.Cargo.Values.Sum() > 0f);
        var runner = new SimulationRunner();
        var ev = Capture(u);
        for (int i = 0; i < 300 && !u.Fights.IsEngaged(me.Id); i++) runner.Step(u);
        Assert.True(u.Fights.Of(me.Id)?.Cause == EncounterCause.Raid, string.Join(" | ", ev.Where(e => e is EncounterEscaped or EncounterStarted or EncounterEnded).Select(e => e.ToString())));
        Assert.True(u.Tick >= raids[0].DueTick);
        Assert.True(PlayerSurrender.Relevant(u), "Ergeben ist im Überfall-Gefecht ein Thema (Taste sichtbar)");
        Assert.Null(PlayerSurrender.Issue(u));   // mit Fracht: Taste aktiv
    }

    /// <summary>
    /// Zweiter Gerätetest Phase 28: der Gefechtstest ist für einen Menschen bestehbar — nur „Nächster Gegner“ und „Dauerfeuer“ (jede bereite Waffe feuert,
    /// sobald das Ziel im Feuerwinkel liegt; die Nase folgt mit der echten Drehrate der Zielhilfe). Runde 13 (Handy-Test: Falke zerstört): wie in der UI
    /// gebaut (<see cref="WorldBuilder.BuildDefault"/> mit gewürfelten Namen) über mehrere Seeds — „Neues Spiel“ würfelt den Seed, und auf manchen Seeds
    /// überstand die Verstärkung (Schild 40) den angeschlagenen Falken —, mit 4 s Reaktionszeit zu Beginn JEDES Überfalls, bis nach der Verstärkung.
    /// </summary>
    [Theory]
    [InlineData(1ul)] [InlineData(3ul)] [InlineData(12ul)] [InlineData(14ul)] [InlineData(16ul)] [InlineData(18ul)]
    public void Startpaket_Gefechtstest_BestehbarMitDauerfeuer(ulong seed)
    {
        var u = WorldBuilder.BuildDefault(seed, "combat");
        var me = u.Ships[u.PlayerShip];
        var lead = u.PendingEncounters.Where(p => p.Kind == EncounterKind.Raid).OrderBy(p => p.DueTick).First().Source;
        var runner = new SimulationRunner();
        long start = -1, leadDown = -1, react = long.MaxValue;
        bool wasEngaged = false;
        var target = default(ShipId);
        float heading = SpaceSim.Core.Flight.AimAssist.Bearing(me.Position, u.Ships[lead].Position);   // die Ansicht richtet die Nase beim Start auf den Kaperer
        for (int i = 0; i < 4 * 150 && u.PlayerShip == me.Id; i++)
        {
            runner.Step(u);
            bool engaged = u.Fights.IsEngaged(me.Id);
            if (engaged && !wasEngaged) { react = u.Tick + 4 * SimClock.TicksPerSecond; target = default; }   // der Mensch braucht ~4 s, bis er reagiert
            wasEngaged = engaged;
            if (!engaged) continue;
            if (start < 0) start = u.Tick;
            if (!u.Ships.ContainsKey(lead) && leadDown < 0) leadDown = u.Tick;
            if (u.Tick < react) continue;
            if (target.Value == 0 || !u.Ships.ContainsKey(target)) target = PlayerCombat.NextEnemy(u, me, default);
            if (target.Value == 0 || PlayerCombat.PositionOf(u, target) is not { } tp) continue;
            heading = SpaceSim.Core.Flight.AimAssist.Turn(heading, SpaceSim.Core.Flight.AimAssist.Bearing(me.Position, tp.Pos), SpaceSim.Core.Flight.AimAssist.PlayerTurnRate, 1f / SimClock.TicksPerSecond);
            foreach (int slot in PlayerGunnery.WeaponSlots(u, me)) PlayerGunnery.TryFire(u, me, slot, target, heading * 180f / MathF.PI);
        }
        if (!u.Ships.ContainsKey(lead) && leadDown < 0) leadDown = u.Tick;
        Assert.True(start >= 0, "der Überfall begann");
        Assert.True(leadDown > 0, "der erste Kaperer wurde abgeschossen");
        Assert.True(u.PlayerShip == me.Id && u.Ships.ContainsKey(me.Id), "der Falke lebt (auch nach der Verstärkung)");
        Assert.True(u.Tick - start > 120 * SimClock.TicksPerSecond, "länger als 120 s überlebt");
        Assert.True(u.Actors[u.PlayerActor].BountyClaims.Values.Sum() > 0f, "Kopfgeld für den Kaperer");
        // Runde 4: der Anführer hatte Beute an Bord — sein Wrack hat Fracht für den Traktorstrahl
        Assert.Contains(u.Wrecks, w => w.From == lead && w.Cargo.Values.Sum() > 0f);
    }

    /// <summary>
    /// Prüfung Phase 28 (Langläufe: Piraten starben aus): eine Piratengruppe mit leerer Kasse und beschädigten Kaperschiffen im Versteck jagt nach begrenzter
    /// Zeit wieder (Notversorgung im Versteck); ohne die Regel bleibt sie stehen und die Handlungsfähigkeit meldet „kann nicht jagen“.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Piraten_LeereKasse_BeschaedigtImVersteck_JagenWieder(bool patch)
    {
        var u = Standard();
        if (!patch) u.Rules.PirateBasePatchHull = 0f;
        var a = u.Actors.Values.Where(x => x.Kind == ActorKind.Outlaw && !Bandits.IsEstablished(u, x.Id)).OrderBy(x => x.Id.Value).First();   // Punkt 17: nur die kleinen Piratengruppen, nicht die etablierte Fraktion
        var hideout = u.StationsById().First(st => st.Owner == a.Id && StationProtection.IsHideout(u, st));
        a.Treasury.Credits = 0f;
        var raiders = u.ShipsById().Where(r => ThreatMap.IsRaider(r) && r.Owner == a.Id).ToList();
        foreach (var r in raiders)
        {
            if (r.Command is not null) Commands.Cancel(u, r);
            if (r.Squadron.Value != 0 && Squadrons.Get(u, r) is { } sq) Squadrons.Disband(u, sq);
            Place(u, r, hideout.Sector, hideout.DockPoint(r.Id));
            r.HomeBase = hideout.Id;
            r.Hull = r.MaxHull * 0.2f;
            int shield = r.Fit.FindIndex(m => m == "shield_mk1");
            if (shield >= 0) ModuleDamage.SetCondition(u, r, shield, 0.05f);
        }
        var runner = new SimulationRunner();
        var ev = Capture(u);
        bool hunted = false;
        for (int i = 0; i < 6000 && !hunted; i++)
        {
            runner.Step(u);
            a.Treasury.Credits = 0f;   // die Kasse bleibt leer (keine Hehlerei, keine bezahlte Reparatur)
            hunted = raiders.Any(r => u.Ships.ContainsKey(r.Id) && !PirateSystem.AtHome(u, r) && r.Command is LurkCommand);
        }
        if (patch)
        {
            Assert.True(hunted, "die Gruppe jagt wieder");
            Assert.DoesNotContain(FactionViability.Check(u, a.Id), i => i.Kind == ViabilityIssueKind.CannotHunt);
        }
        else
        {
            Assert.False(hunted);
            Assert.Contains(FactionViability.Check(u, a.Id), i => i.Kind == ViabilityIssueKind.CannotHunt);
        }
    }

    [Theory]
    [InlineData(0f, 1f, 0.25f, 0.25f)]        // dreht höchstens Rate × Zeit
    [InlineData(0f, 0.1f, 3.2f, 0.25f)]       // kein Überschwingen
    [InlineData(3f, -3f, 3.2f, 0.25f)]        // über ±π den kurzen Weg
    public void Zielhilfe_DrehtMitDerDrehrateZumZiel(float heading, float bearing, float rate, float dt)
    {
        float next = SpaceSim.Core.Flight.AimAssist.Turn(heading, bearing, rate, dt);
        float before = MathF.Abs(SpaceSim.Core.Flight.AimAssist.Delta(heading, bearing));
        float after = MathF.Abs(SpaceSim.Core.Flight.AimAssist.Delta(next, bearing));
        Assert.True(MathF.Abs(SpaceSim.Core.Flight.AimAssist.Delta(heading, next)) <= rate * dt + 1e-4f);
        Assert.True(after <= before + 1e-5f);
        if (before <= rate * dt) Assert.True(after < 1e-4f);
        // Peilung in derselben Konvention wie die Feuerwinkel (FiringArc.Bearing in Grad)
        var a = new Vec2(10f, 5f); var b = new Vec2(-3f, 40f);
        Assert.Equal(FiringArc.Bearing(b - a), SpaceSim.Core.Flight.AimAssist.Bearing(a, b) * 180f / MathF.PI, 3);
    }

    /// <summary>
    /// Gerätetest Phase 28: kann ein Jäger mit der normalen Drehrate seinen kreisenden Gegner im Bugwinkel (±45°) halten? Größte Winkelgeschwindigkeit des
    /// Gegners um den Falken im Gefechtstest (räumliches Gefecht) gegen die Drehrate des Spielerschiffs.
    /// </summary>
    [Fact]
    public void Zielhilfe_JaegerDrehtSchnellerAlsDerKapererKreist()
    {
        var u = Standard(start: "combat");
        var me = u.Ships[u.PlayerShip];
        var lead = u.PendingEncounters.Where(p => p.Kind == EncounterKind.Raid).OrderBy(p => p.DueTick).First().Source;
        var runner = new SimulationRunner();
        float? last = null, maxRate = 0f;
        for (int i = 0; i < 4 * 60 && u.Ships.ContainsKey(lead); i++)
        {
            runner.Step(u);
            if (!u.Fights.IsEngaged(lead) || PlayerCombat.PositionOf(u, lead) is not { } p) { last = null; continue; }
            float b = SpaceSim.Core.Flight.AimAssist.Bearing(me.Position, p.Pos);
            if (last is { } l) maxRate = MathF.Max(maxRate.Value, MathF.Abs(SpaceSim.Core.Flight.AimAssist.Delta(l, b)) * SimClock.TicksPerSecond);
            last = b;
        }
        Assert.True(maxRate > 0f, "der Kaperer bewegte sich im Gefecht");
        Assert.True(maxRate < SpaceSim.Core.Flight.AimAssist.PlayerTurnRate, $"Winkelgeschwindigkeit des Kaperers {maxRate:0.00} rad/s, Drehrate {SpaceSim.Core.Flight.AimAssist.PlayerTurnRate} rad/s");
    }

    /// <summary>
    /// Gerätetest Phase 28: Kriegstest — eine Rakete vom Rand der Raketenreichweite auf die Station: die Belagerung bleibt bestehen, bis die Rakete ankommt
    /// (vorher galt der Spieler beim Abstand zur Stationsmitte als geflohen, das Gefecht endete im nächsten Takt, die Rakete verschwand).
    /// </summary>
    [Fact]
    public void Belagerung_RaketeAmRandDerReichweite_KommtAn()
    {
        var u = Standard(start: "war");
        var r = new SimulationRunner();
        for (int i = 0; i < 6; i++) r.Step(u);
        var me = u.Ships[u.PlayerShip];
        var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
        var ev = Capture(u);
        var shot = PlayerGunnery.TryFireAtStation(u, me, me.Fit.IndexOf("missile_launcher_s"), hq.Id, FiringArc.Bearing(hq.Position - me.Position));
        Assert.Equal(GunResult.Fired, shot.Result);
        for (int i = 0; i < 40 && !ev.Any(e => e is MissileResolved); i++) r.Step(u);
        Assert.Contains(ev, e => e is MissileResolved);
        Assert.True(Vec2.Distance(me.Position, hq.Position) > StationCombat.GunRange(u, hq) + Station.BodyRadius, "Start außerhalb der Geschützreichweite");
    }

    /// <summary>
    /// Prüfung Runde 2 (Leerfahrten): mit dem Kaufschutz schon in der Planung (erwarteter Einkaufspreis nach den Käufen, die andere Händler dort vorhaben)
    /// enden deutlich weniger Fahrten mit einem verweigerten Verlustkauf.
    /// </summary>
    [Fact]
    public void Haendler_PlanenMitErwartetemEinkaufspreis_WenigerLeerfahrten()
    {
        // Runde 9: über vier Seeds summiert — seit Tanker Flüssigware fahren, gibt es auch ohne Planung weniger verweigerte Käufe (Standard-Seed 150 → 77),
        // das Verhältnis eines einzelnen Pfads schwankt (gemessen mit/ohne: 46/77, 54/122, 47/87, 68/114; vor Runde 9: 52/150, 55/118, 33/98, 64/158)
        int Refused(bool plan, ulong? seed)
        {
            var u = Standard(seed);
            u.Rules.TraderPlanGuard = plan;
            var ev = Capture(u);
            var runner = new SimulationRunner();
            for (int i = 0; i < 8000; i++) runner.Step(u);
            return ev.Count(e => e is TraderBuyRefused);
        }
        var seeds = new ulong?[] { null, 1, 2, 3 };
        int with = seeds.Sum(s => Refused(true, s)), without = seeds.Sum(s => Refused(false, s));
        Assert.True(without > 0);
        Assert.True(with < without * 0.65f, $"verweigert mit Planung {with}, ohne {without}");
    }

    /// <summary>
    /// Prüfung Runde 2: ein fälliger Überfall, während der Spieler schon kämpft — gegen Kaperer tritt der neue Kaperer auf der Kaperer-Seite bei; gegen Polizei
    /// (Festnahme) tritt er nicht bei (er landete sonst auf der Polizeiseite), der Überfall wird verschoben und kommt nach dem Gefecht.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ueberfall_WaehrendGefecht_TrittNurGegenKapererBei(bool againstRaiders)
    {
        var u = Standard();
        u.Rules.InterceptRatePerTick = 0f;   // Runde 12: keine zufällige Streife fängt den späten Kaperer in einem eigenen Gefecht ab (pfadabhängig)
        u.Rules.StationGunsActive = false;   // Runde 12: das Hauptquartier (sechs Geschütze, Raketen 110 m) beendete das erste Gefecht vor dem späten Überfall
        if (againstRaiders) foreach (var cop in SecuritySystem.PoliceOf(u, u.Sectors[u.PlayerSector].Owner)) { if (cop.Transit is not null) u.FinishTransit(cop); if (cop.Command is not null) Commands.Cancel(u, cop); if (Squadrons.Get(u, cop) is { } csq) Squadrons.Disband(u, csq); Place(u, cop, new SectorId(2), new Vec2(0f, 0f)); cop.NextThinkTick = u.Tick + 100_000; }   // Runde 12: die stärkere Streife (dritter Laser) half sofort, der erste Kaperer floh vor dem späten Überfall
        new SimulationRunner().Step(u);
        var me = u.Ships[u.PlayerShip];
        var sector = u.Sectors[me.Sector];
        Place(u, me, me.Sector, CombatSpace.Clear(u, me.Sector, me.Position + new Vec2(0f, -120f)));
        var raiders = u.ShipsById().Where(ThreatMap.IsRaider).Take(2).ToList();
        foreach (var r in raiders) { if (r.Command is not null) Commands.Cancel(u, r); if (Squadrons.Get(u, r) is { } sq) Squadrons.Disband(u, sq); Place(u, r, me.Sector, me.Position + new Vec2(0f, -30f)); r.NextThinkTick = u.Tick + 100_000; }
        Encounter fight;
        if (againstRaiders) fight = Engage.StartRaid(u, raiders[0], me, sector);
        else
        {
            var cops = SecuritySystem.PoliceOf(u, sector.Owner).Take(1).ToList();
            foreach (var c in cops) { if (c.Command is not null) Commands.Cancel(u, c); Place(u, c, me.Sector, me.Position + new Vec2(20f, 0f)); }
            fight = Engage.StartArrest(u, cops, me, sector);
        }
        var late = raiders[1];
        Place(u, late, me.Sector, me.Position + new Vec2(0f, 40f));
        Commands.Give(u, late, new LurkCommand(me.Sector, late.Position, u.Tick + 20_000), out _, ignoreCapacity: true);   // lauert hier (ohne Befehl schickte die Gruppe ihn heim)
        u.PendingEncounters.Add(new PendingEncounter { Kind = EncounterKind.Raid, Source = late.Id, Target = me.Id, Sector = me.Sector, DueTick = u.Tick + 5 });
        var runner = new SimulationRunner();
        var ev = Capture(u);
        for (int i = 0; i < 12; i++) runner.Step(u);
        var mySide = fight.Of(me.Id)!.Side;
        Assert.DoesNotContain(ev, x => x is EncounterEscaped);
        if (againstRaiders)
        {
            Assert.Equal(fight.Id, u.Fights.Of(late.Id)?.Id);
            Assert.NotEqual(mySide, fight.Of(late.Id)!.Side);
        }
        else
        {
            Assert.Null(fight.Of(late.Id));
            Assert.False(u.Fights.IsEngaged(late.Id), "nicht auf der Seite der Polizei");
            Assert.Contains(u.PendingEncounters, p => p.Source == late.Id && p.DueTick > u.Tick);   // verschoben, nicht abstrakt aufgelöst
        }
    }

    /// <summary>
    /// Prüfung Phase 28 (Lauf J: Bergbau nach Wiederöffnung dauerhaft über der Grenze): im geteilten Zustand kauft ein Teil mit freiem Anteil nicht, wenn die
    /// ganze Welt die Grenze schon erreicht hat (Schiffe sitzen über dem Anteil eines kleinen Teils fest); der Notkauf der Fraktionskasse bleibt erlaubt.
    /// </summary>
    [Fact]
    public void Flotte_GeteilteKarte_NieUeberDieWeltgrenze()
    {
        var u = Standard();
        new SimulationRunner().Step(u);
        GateAccess.SetState(u, u.Gates[new GateId(17)], GateState.Closed);   // Aurelia (Sackgasse) fällt ab
        Assert.True(FleetCapacity.Divided(u));
        var job = ShipJobs.Default.First(j => j.Key == "mine");
        var aurelia = u.Gates[u.Gates[new GateId(17)].Target].Sector;
        var miners = u.ShipsById().Where(s => s.Role == ShipRole.Miner && s.Controller == Controller.Ai).Take(6).ToList();
        foreach (var m in miners) Place(u, m, aurelia, u.Sectors[aurelia].Fields.Count > 0 ? u.Sectors[aurelia].Fields[0].Position : default);
        u.Rules.MaxMiners = (int)MathF.Floor(ShipJobs.UsedUnits(u, job));
        int main = FleetCapacity.PartOf(u, u.ShipsById().First(s => s.Role == ShipRole.Miner && s.Sector != aurelia && s.Controller == Controller.Ai));
        Assert.True(FleetCapacity.Used(u, job, main) < FleetCapacity.Limit(u, job, main), "der Hauptteil hätte nach seinem Anteil Platz");
        Assert.False(FleetSystem.CanGrow(u, job, main));
        Assert.True(FleetSystem.CanGrow(u, job, main, worldCap: false));
        u.Rules.FleetWorldCapWhenDivided = false;
        Assert.True(FleetSystem.CanGrow(u, job, main));
    }

    [Fact]
    public void Startpaket_Festnahmetest_WarnungNachSekunden()
    {
        var u = Standard(start: "arrest");
        var me = u.Ships[u.PlayerShip];
        Assert.True(Wanted.LevelOf(u, me.Owner, u.Sectors[me.Sector].Owner) >= 3);
        var runner = new SimulationRunner();
        int t = 0;
        for (; t < 400 && !u.PendingEncounters.Any(p => p.Kind == EncounterKind.Intercept && p.Target == me.Id); t++) runner.Step(u);
        Assert.True(t < 400, "Festnahme-Warnung kam nicht");
    }

    // ───────── Nacharbeit nach der Prüfung ─────────

    [Fact]
    public void Haendler_KauftNichtMitSicheremVerlust()
    {
        foreach (bool guard in new[] { true, false })
        {
            var u = Standard();
            u.Rules.TraderBuyGuard = guard;
            new SimulationRunner().Step(u);
            var trader = u.ShipsById().First(s => s.Role == ShipRole.Trader && s.Controller == Controller.Ai);
            var energy = u.FindGood("energy")!.Id;
            var seller = u.StationsById().First(st => !st.IsBuildSite && st.Produces(energy) && st.Market.ContainsKey(energy));
            var buyer = u.StationsById().First(st => !st.IsBuildSite && st.Consumes(energy) && st.Market.ContainsKey(energy));
            Place(u, trader, seller.Sector, seller.DockPoint(trader.Id) + new Vec2(20f, 0f));
            trader.Cargo.Clear(); trader.Credits = 5000f;
            trader.Order = new TradeOrder(seller.Id, buyer.Id, energy);
            trader.Phase = TradePhase.GoingToBuy;
            buyer.Credits = 50_000f;
            buyer.Market[energy].Amount = 0f;
            seller.Market[energy].Amount = 100f;
            seller.Market[energy].Price = buyer.Market[energy].Price * 1.5f;   // Knappheitspreis beim Hersteller: über dem Verkaufspreis
            Assert.True(Travel.Start(u, trader, seller.Sector, seller.DockPoint(trader.Id)));
            var ev = Capture(u);
            var sys = new TraderSystem();
            for (int i = 0; i < 400 && trader.Transit is not null; i++) { u.Tick++; sys.Step(u, 1); }
            u.Tick++; sys.Step(u, 1);
            if (guard)
            {
                Assert.DoesNotContain(ev, e => e is TraderBought tb && tb.Ship == trader.Id);
                Assert.Equal(5000f, trader.Credits);
            }
            else Assert.Contains(ev, e => e is TraderBought tb && tb.Ship == trader.Id);   // Regel aus: kauft trotzdem (Verhalten bis Phase 28)
        }
    }

    [Fact]
    public void Schulden_KeinKaufOderDienstMitNegativerKasse()
    {
        var u = Standard(start: "fighter");
        var me = u.Ships[u.PlayerShip];
        new SimulationRunner().Step(u);
        u.WalletOf(me).Credits = -500f;
        var yard = u.StationsById().First(st => st.TypeKey == "shipyard");
        var good = yard.Market.Keys.First(g => yard.Market[g].Amount >= 1f && yard.Market[g].Price > 0f);
        Assert.Equal(0f, SpaceSim.Core.Trading.TradeActions.MaxBuyable(u, me, yard, good));
        Assert.Equal(0f, SpaceSim.Core.Trading.TradeActions.Buy(u, me, yard, good, 5f).Units);
        // jedes Angebot, das sich ausführen lässt, darf die Schuld nicht vergrößern (Verkäufe/Tausch mit Erlös tilgen sie; Notreparatur ist ausdrücklich auf Schuld)
        var services = SpaceSim.Core.Services.StationServices.Default();
        foreach (var svc in services.ForStation(u, yard))
            foreach (var o in svc.Offers(u, yard, me).ToList())
            {
                if (o.Amount <= 0f || o.Key.Contains("emergency", StringComparison.Ordinal) || svc.Key is "hangar" or "taxi" or "rental") continue;
                float before = u.WalletOf(me).Credits;
                var res = svc.Execute(u, yard, me, new SpaceSim.Core.Services.ServiceRequest(o.Key, o.Good, 1f));
                Assert.True(!res.Success || u.WalletOf(me).Credits >= before - 0.01f, $"{svc.Key}: {o.Key} vergrößert die Schuld ({before:0} → {u.WalletOf(me).Credits:0})");
                if (u.WalletOf(me).Credits > -500f) u.WalletOf(me).Credits = -500f;
            }
        Assert.False(SpaceSim.Core.Services.ShipDealerService.Quote(u, yard, me, "pip")!.Affordable);
    }

    [Fact]
    public void LetzterHerstellerZerstoert_WirtschaftBautNach()
    {
        var u = Standard();
        new SimulationRunner().Run(u, 2000);
        var cells = u.FindGood("fuel_cells")!.Id;
        var producers = u.StationsById().Where(st => !st.IsBuildSite && st.Produces(cells)).ToList();
        Assert.NotEmpty(producers);
        foreach (var st in producers) StationCombat.Fall(u, st, u.PlayerActor);   // der Spieler zerstört alle Hersteller (wirtschaftliche Stationen: zerstörbar)
        Assert.DoesNotContain(u.StationsById(), st => !st.IsBuildSite && st.Produces(cells));
        var runner = new SimulationRunner();
        for (int i = 0; i < 40000 && !u.StationsById().Any(st => !st.IsBuildSite && st.Produces(cells)); i++) runner.Step(u);
        var scan = StationLifecycleSystem.Scan(u);
        Assert.True(u.StationsById().Any(st => !st.IsBuildSite && st.Produces(cells)),
            $"Verhältnis {scan.Ratios.GetValueOrDefault(cells)}, knappste {(scan.Scarce is { } sc ? u.Goods[sc].Key : "-")}, Hersteller-Typ {StationLifecycleSystem.ProducerFor(u, cells, scan.Ratios)?.Key}, "
            + $"Baustellen {string.Join(",", u.StationsById().Where(x => x.IsBuildSite).Select(x => x.Site!.TypeKey))}, wirtschaftlich {StationProtection.CountEconomic(u)}/{u.Rules.MaxStations}");   // Knappheit → Neubau über den normalen Lebenszyklus
    }

    [Fact]
    public void Startpaket_Kriegstest_RaketenUndStationInReichweite()
    {
        var u = Standard(start: "war");
        var me = u.Ships[u.PlayerShip];
        Assert.True(me.Stats.MissileCapacity > 0f && Missiles.HasMissile(u, me));
        Assert.True(u.WalletOf(me).Credits >= 20000f);
        var st = u.StationsById().First(x => x.Sector == me.Sector && x.TypeKey == "sector_hq");
        int slot = me.Fit.IndexOf("missile_launcher_s");
        float heading = FiringArc.Bearing((st.Position - me.Position).Normalized());
        Assert.Null(PlayerGunnery.StationIssue(u, me, st));
        Assert.Equal(BayState.Ready, PlayerGunnery.StateAtStation(u, me, slot, heading, st.Id));
        Assert.True(Vec2.Distance(me.Position, st.Position) > 60f + Station.BodyRadius, "außerhalb der Stationslaser");
        // Runde 4: dieselbe Messung wie die Anzeige — Abstand zur Mitte, Geschütze ab Mitte, eigene Raketen ab Mitte (Waffenreichweite + Außenhaut)
        float d = Vec2.Distance(me.Position, st.Position);
        Assert.True(d > StationCombat.GunRange(u, st), $"außerhalb der Geschütze ({d:0} m)");
        Assert.True(d <= WarInfo.MissileReachFromCentre(u, me), $"innerhalb der Raketenreichweite ({d:0} m)");
        Assert.StartsWith("außerhalb Geschütze · Raketen ja · eigene ab", WarInfo.RangeStatus(u, st, me));   // Runde 14: dazu, ob die eigenen Geschütze reichen
        Assert.Equal($"4 Geschütze · {StationCombat.GunRange(u, st):0} m", WarInfo.DefenseWarning(u, st, me));   // Runde 12: Sektor-HQ mit vier Geschützen
        me.Position = st.Position + (me.Position - st.Position).Normalized() * (StationCombat.GunRange(u, st) - 1f);
        Assert.StartsWith("IN GESCHÜTZREICHWEITE", WarInfo.RangeStatus(u, st, me));
    }

    [Fact]
    public void Autopilot_KommtVonSechsStartpunktenAnJederStationAn()
    {
        // Gerätetest Phase 28: der Autopilot kreiste vor dem HQ Alpha (Gerade streifte einen Felsen, an dem die Kollision das Schiff festhielt). Kinematisch:
        // Schritt zum nächsten Wegpunkt, Kollision mit den Hindernissen — jede Station der Standardwelt von sechs Startpunkten in 230 Einheiten Abstand
        var u = Standard(start: "fighter");
        var fails = new List<string>();
        foreach (var st in u.StationsById().Where(s => !s.IsBuildSite))
        {
            var obs = u.GetObstacles(st.Sector);
            var nav = Navigator.ForStation(u, st.Id, u.PlayerShip);
            for (int k = 0; k < 6; k++)
            {
                var pos = CombatSpace.Clear(u, st.Sector, st.Position + Vec2.FromAngle(k * 1.05f + 0.4f) * 230f);
                bool arrived = false;
                var follower = new PathFollower();
                for (int i = 0; i < 700 && !arrived; i++)
                {
                    var step = follower.Next(u, st.Sector, pos, nav);
                    if (step.Status == NavStatus.Arrived) { arrived = true; break; }
                    var d = step.Waypoint - pos; float len = d.Length;
                    pos += d * (MathF.Min(2.5f, len) / MathF.Max(0.001f, len));
                    Collision.Resolve(ref pos, Collision.ShipRadius, obs, out _);
                }
                if (!arrived) fails.Add($"{st.Name} Start {k} (Rest {Vec2.Distance(pos, nav.Position):0})");
            }
        }
        Assert.True(fails.Count == 0, string.Join("; ", fails));
    }

    // ───────── Fünfter Gerätetest (Runde 5): Stationsangriff nur mit Fregatte oder Flotte ─────────

    /// <summary>Spieler wie am Handy: auf <paramref name="standoff"/> m an die Station heran, Zielhilfe mit echter Drehrate, alle Waffen auf die Station.</summary>
    static (bool Alive, bool Down, long Seconds, List<SimEvent> Events) AttackStation(Universe u, Station hq, float standoff, int maxSeconds, Action<SimEvent>? watch = null)
    {
        var me = u.Ships[u.PlayerShip];
        var ev = Capture(u);
        if (watch is not null) u.Events.Raised += watch;
        var runner = new SimulationRunner();
        float heading = SpaceSim.Core.Flight.AimAssist.Bearing(me.Position, hq.Position);
        long start = u.Tick;
        for (int i = 0; i < maxSeconds * SimClock.TicksPerSecond && u.PlayerShip == me.Id && !StationCombat.IsDown(u, hq); i++)
        {
            runner.Step(u);
            if (u.PlayerShip != me.Id) break;
            float d = Vec2.Distance(me.Position, hq.Position);
            if (d > standoff) me.Position += (hq.Position - me.Position).Normalized() * MathF.Min(d - standoff, me.Stats.Speed / SimClock.TicksPerSecond);
            heading = SpaceSim.Core.Flight.AimAssist.Turn(heading, SpaceSim.Core.Flight.AimAssist.Bearing(me.Position, hq.Position), SpaceSim.Core.Flight.AimAssist.PlayerTurnRate, 1f / SimClock.TicksPerSecond);
            foreach (int slot in PlayerGunnery.WeaponSlots(u, me)) PlayerGunnery.TryFireAtStation(u, me, slot, hq.Id, heading * 180f / MathF.PI);
        }
        return (u.PlayerShip == me.Id && u.Ships.ContainsKey(me.Id), StationCombat.IsDown(u, hq), (u.Tick - start) / SimClock.TicksPerSecond, ev);
    }

    /// <summary>
    /// Fünfter Gerätetest: der Kriegstest startet im Bollwerk mit einem eigenen Verband (zwei weitere Bollwerke, Regel „Angriff“). Wer wie am Handy angreift
    /// (heranfliegen, Zielhilfe, Dauerfeuer auf die Station), schaltet das Sektor-HQ Beta samt Polizei aus und überlebt; der Verband tritt der Belagerung bei.
    /// </summary>
    [Fact]
    public void Startpaket_Kriegstest_MitVerbandSchaffbar()
    {
        var u = Standard(start: "war");
        u.Rules.StationCapitulation = false;   // prüft die Phase-28-Regel (Regierungsstation nur ausgeschaltet); Kapitulation/Zerstörung: KapitulationTests
        var me = u.Ships[u.PlayerShip];
        Assert.Equal("bastion", me.ModelKey);
        var sq = Squadrons.Get(u, me);
        Assert.NotNull(sq);
        Assert.Equal(me.Id, sq!.Leader);
        Assert.Equal(3, sq.Members.Count);
        Assert.All(sq.Members.Where(id => id != me.Id).Select(id => u.Ships[id]), w =>
        {
            Assert.True(w.IsPlayer && w.Owner == me.Owner && w.Sector == me.Sector && w.DockedAt.Value == 0);
            Assert.Equal(Engagement.Attack, w.Engagement);
            Assert.IsType<FollowCommand>(w.Command);
        });
        var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
        Assert.StartsWith("Überlegen", WarInfo.SiegeOddsText(u, hq, me));
        int policeShots = 0;
        var (alive, down, seconds, ev) = AttackStation(u, hq, 56f, 400, e => { if (e is ShotFired f && u.Ships.TryGetValue(f.Shooter, out var s) && ThreatMap.IsPolice(s)) policeShots++; });
        Assert.True(down, $"das HQ ist ausgeschaltet ({WarInfo.StationShort(u, hq)} nach {seconds} s)");
        Assert.True(alive, "das Bollwerk lebt");
        Assert.True(me.Hull >= 0.3f * me.MaxHull, $"Sieg mit mindestens 30 % Hülle ({me.Hull:0}/{me.MaxHull:0})");   // Runde 15 (Handy-Test: nur 38/600)
        Assert.True(seconds >= 120 && seconds <= 240, $"spürbar, aber in zwei bis vier Minuten ({seconds} s)");   // Runde 6: Richtwert 2–4 min
        Assert.Contains(ev, e => e is CombatJoined j && j.Reason == "squadron" && sq.Members.Contains(j.Ship));
        Assert.True(policeShots > 0, "die Polizei hat verteidigt");
    }

    /// <summary>
    /// Fünfter Gerätetest: allein ist der Angriff aussichtslos — die Lagezeile sagt es vorher; der Verlust nennt den, der den letzten Treffer setzte
    /// (vorher stand bei jeder Belagerung „Stationsgeschütze“, auch wenn die Polizei traf).
    /// </summary>
    [Fact]
    public void Kriegstest_Allein_AussichtslosUndVerlustNenntDenLetztenSchuetzen()
    {
        var u = Standard(start: "war");
        var me = u.Ships[u.PlayerShip];
        foreach (var id in Squadrons.Get(u, me)!.Members.Where(id => id != me.Id).ToList()) Departures.RemoveShip(u, u.Ships[id], DepartureReason.Sold, "test");
        var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
        Assert.Equal("Aussichtslos allein – Fregatte oder Flotte nötig", WarInfo.SiegeOddsText(u, hq, me));
        Assert.True(WarInfo.SiegeHopeless(u, hq, me));
        string? lastHit = null;
        void Watch(SimEvent e)
        {
            switch (e)
            {
                case ShotFired s when s.Target == me.Id && s.Hit && u.Ships.TryGetValue(s.Shooter, out var by): lastHit = CombatSense.Who(u, by); break;
                case StationShot s when s.Target == me.Id && s.Hit: lastHit = $"Stationsgeschütze ({u.Stations[s.Station].Name})"; break;
                case MissileResolved m when m.Target == me.Id && m.Outcome == "hit": lastHit = u.Ships.TryGetValue(m.Shooter, out var mb) ? $"Rakete: {CombatSense.Who(u, mb)}" : null; break;
            }
        }
        var (alive, down, _, ev) = AttackStation(u, hq, 40f, 400, Watch);
        Assert.False(down);
        Assert.False(alive, "allein geht das Bollwerk verloren");
        var lost = ev.OfType<PlayerShipLost>().Single();
        Assert.NotNull(lastHit);
        Assert.Contains($"({lastHit})", lost.Message);
    }

    /// <summary>
    /// Fünfter Gerätetest: Flucht aus einer Belagerung. Die eigenen Waffen halten das Gefecht nur fest, solange der Spieler noch schießt (Nachladen, Rakete
    /// unterwegs); danach entkommt er, sobald kein Gegner und kein Stationsgeschütz mehr reicht — vorher hielt die eigene Raketenreichweite (110 m) jede
    /// verfolgende Streife im Gefecht. Anzeige („Flucht: …“) und Regel sind dieselbe Funktion.
    /// </summary>
    [Fact]
    public void Flucht_AusBelagerung_GelingtAusserReichweite()
    {
        var u = Standard(start: "war");
        var me = u.Ships[u.PlayerShip];
        foreach (var id in Squadrons.Get(u, me)!.Members.Where(id => id != me.Id).ToList()) Departures.RemoveShip(u, u.Ships[id], DepartureReason.Sold, "test");
        var runner = new SimulationRunner();
        runner.Step(u);
        var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
        int launcher = me.Fit.IndexOf("missile_launcher_s");
        Assert.Equal(GunResult.Fired, PlayerGunnery.TryFireAtStation(u, me, launcher, hq.Id, FiringArc.Bearing(hq.Position - me.Position)).Result);
        var e = u.Fights.Of(me.Id)!;
        var p = e.Of(me.Id)!;
        // eine Streife der Station auf 100 m vom Spieler (jenseits ihrer 60 m, diesseits der eigenen Raketen)
        var cop = SecuritySystem.PoliceOf(u, u.FactionOf(hq)!.Value).First();
        if (cop.Command is not null) Commands.Cancel(u, cop);
        if (Squadrons.Get(u, cop) is { } csq) Squadrons.Disband(u, csq);
        me.Position = hq.Position + (me.Position - hq.Position).Normalized() * 211f;
        Place(u, cop, hq.Sector, me.Position + new Vec2(100f, 0f));
        var cp = Reinforcements.JoinSide(u, e, cop, 1, "police", null);
        cp.Pos = cop.Position; p.Pos = me.Position;
        Assert.True(CombatSense.StillShooting(e, p));
        Assert.StartsWith("eigene Waffen feuern noch", CombatSense.EscapeBlock(u, e, p));
        Assert.StartsWith("Flucht: eigene Waffen", PlayerCombat.EscapeStatus(u));
        // Nachladen vorbei, keine Rakete unterwegs: nichts reicht mehr — frei
        for (int i = 0; i < p.Timers.Count; i++) p.Timers[i] = 0f;
        e.Missiles.Clear();
        Assert.StartsWith("eigene Waffen feuern noch", CombatSense.EscapeBlock(u, e, p));   // Runde 6: der letzte Schuss hält noch (zwischen zwei Salven)
        p.LastShotTick = u.Tick - u.Rules.PlayerFireHoldTicks - 1;
        Assert.Null(CombatSense.EscapeBlock(u, e, p));
        // Streife auf 50 m: sie reicht (60 m) — die Anzeige nennt sie
        cp.Pos = me.Position + new Vec2(50f, 0f);
        Assert.Equal("Flucht: Polizei in Reichweite (50 von 60 m)", PlayerCombat.EscapeStatus(u));
        // Stationsgeschütze ab Mitte (wie ihre Schussregel)
        cp.Pos = me.Position + new Vec2(150f, 0f);
        p.Pos = hq.Position + new Vec2(55f, 0f);
        // Phase 30: das Sektor-HQ ist groß (L) — seine Geschütze reichen ab der Außenhaut, ab Mitte 60 + 7
        Assert.StartsWith($"Stationsgeschütze in Reichweite (55 von {StationCombat.GunRange(u, hq):0} m)", CombatSense.EscapeBlock(u, e, p));
        p.Pos = hq.Position + new Vec2(StationCombat.GunRange(u, hq) + 5f, 0f);
        Assert.Null(CombatSense.EscapeBlock(u, e, p));
        // und die Simulation lässt den Spieler gehen (Streife weit weg, Spieler 211 m vor der Station)
        Place(u, cop, hq.Sector, me.Position + new Vec2(300f, 0f)); cp.Pos = cop.Position;
        var ev = Capture(u);
        for (int i = 0; i < 4 && u.Fights.IsEngaged(me.Id); i++) runner.Step(u);
        Assert.False(u.Fights.IsEngaged(me.Id), "geflohen");
        Assert.Contains(ev, x => x is CombatExited c && c.Ship == me.Id && c.Exit == CombatExit.Fled);
    }

    /// <summary>
    /// Sechster Gerätetest (Runde 6, Ursache „Station fällt in 25 s“): Nach einem Rückzug aus der Belagerung stand der Spieler noch als abgelaufener Teilnehmer im
    /// Gefecht; sein nächster Schuss trat nicht neu bei — die Nachladeuhr las den alten Eintrag (0), Dauerfeuer schoss jedes Bild (gemessen: 700 Schüsse in 10 s,
    /// Station 2003 → 260), und Geschütze/Verteidiger sahen ihn nicht. Jetzt: Rückzug beendet die Belagerung, der eigene Verband bricht ab und folgt; der nächste
    /// Schuss beginnt eine neue Belagerung mit gültiger Nachladeuhr.
    /// </summary>
    [Fact]
    public void Belagerung_RueckzugBeendetSie_WiedereinstiegMitNachladeuhr()
    {
        var u = Standard(start: "war");
        var me = u.Ships[u.PlayerShip];
        var wing = Squadrons.Get(u, me)!.Members.Where(id => id != me.Id).ToList();
        var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
        var runner = new SimulationRunner();
        runner.Step(u);
        var ev = Capture(u);
        float Bearing() => FiringArc.Bearing(hq.Position - me.Position);
        foreach (int s in PlayerGunnery.WeaponSlots(u, me)) PlayerGunnery.TryFireAtStation(u, me, s, hq.Id, Bearing());
        for (int i = 0; i < 20; i++) runner.Step(u);
        Assert.All(wing, id => Assert.True(u.Fights.IsEngaged(id), "der Verband greift mit an"));
        // Rückzug: weit weg, nicht mehr schießen
        me.Position = hq.Position + (me.Position - hq.Position).Normalized() * 260f;
        for (int i = 0; i < 80 && u.Fights.IsEngaged(me.Id); i++) runner.Step(u);
        Assert.False(u.Fights.IsEngaged(me.Id), "zurückgezogen");
        var sw = Assert.Single(ev.OfType<SiegeWithdrawn>());
        Assert.Equal(2, sw.Wingmen);
        Assert.StartsWith("Rückzug: Angriff auf Sektor-HQ Beta abgebrochen", sw.Message);
        Assert.Null(Siege.Of(u, hq.Id));
        Assert.Contains(ev, x => x is EncounterEnded { Cause: EncounterCause.Siege });
        Assert.All(wing, id => { Assert.False(u.Fights.IsEngaged(id)); Assert.IsType<FollowCommand>(u.Ships[id].Command); });
        // wieder heran und jedes Bild feuern (60 Bilder/s ≈ 15 Aufrufe je Takt): Schüsse nur im Takt der Nachladezeiten
        me.Position = hq.Position + (me.Position - hq.Position).Normalized() * 50f;
        int shots = 0;
        for (int i = 0; i < 10 * SimClock.TicksPerSecond; i++)
        {
            for (int k = 0; k < 15; k++)
                foreach (int s in PlayerGunnery.WeaponSlots(u, me))
                    if (PlayerGunnery.TryFireAtStation(u, me, s, hq.Id, Bearing()).Result == GunResult.Fired) shots++;
            runner.Step(u);
        }
        Assert.True(shots <= 20, $"höchstens im Nachladetakt: {shots} Schüsse in 10 s");
        Assert.True(Siege.Of(u, hq.Id)?.Of(me.Id) is { Active: true }, "aktiver Teilnehmer der neuen Belagerung");
        // der Fall vom Handy: der Spieler hat die noch laufende Belagerung verlassen (der Verband kämpft weiter) und schießt wieder — derselbe Eintrag wird wieder aktiv
        var e = Siege.Of(u, hq.Id)!;
        for (int i = 0; i < 20; i++) runner.Step(u);   // der Verband ist wieder dabei
        Assert.Contains(e.Participants, q => q.Active && wing.Contains(q.Ship));
        Engage.Leave(u, e, e.Of(me.Id)!, CombatExit.Fled);
        Assert.False(u.Fights.IsEngaged(me.Id));
        shots = 0;
        for (int i = 0; i < 10 * SimClock.TicksPerSecond; i++)
        {
            for (int k = 0; k < 15; k++)
                foreach (int s in PlayerGunnery.WeaponSlots(u, me))
                    if (PlayerGunnery.TryFireAtStation(u, me, s, hq.Id, Bearing()).Result == GunResult.Fired) shots++;
            runner.Step(u);
        }
        Assert.True(shots <= 20, $"nach dem Wiederbeitritt höchstens im Nachladetakt: {shots} Schüsse in 10 s (vorher ≈ 700)");
        Assert.Equal(1, e.Participants.Count(q => q.Ship == me.Id));
        Assert.True(e.Of(me.Id) is { Active: true } && u.Fights.Of(me.Id) == e, "wieder aktiver Teilnehmer derselben Belagerung");
    }

    // ───────── Runde 7: ein Schiff hat in einem Gefecht nie zwei Einträge ─────────

    /// <summary>
    /// Runde 7: Wiedereintritt nimmt den alten Eintrag (Mut, Abwurfanteil, Nachladeuhren bleiben, kein Zufallszug); ohne die Regel (Verhalten bis Runde 6)
    /// entstand ein zweiter Eintrag, <see cref="Encounter.Of"/> fand weiter den alten, abgelaufenen — der Wiedereingetretene war für Schiffsfeuer und Raketen kein
    /// Ziel mehr (beide suchen das Ziel über <c>Of</c> und verlangen einen aktiven Eintrag) und schoss mit frischen Uhren sofort wieder.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Gefecht_Wiedereintritt_KeinZweiterEintrag(bool reuse)
    {
        var u = Standard();
        u.Rules.CombatRejoinReuse = reuse;
        new SimulationRunner().Step(u);
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        var victim = u.ShipsById().First(s => s.Role == ShipRole.Trader && s.Sector != u.PlayerSector && !u.Fights.IsEngaged(s.Id));
        if (raider.Command is not null) Commands.Cancel(u, raider);
        if (Squadrons.Get(u, raider) is { } sq) Squadrons.Disband(u, sq);
        Place(u, raider, victim.Sector, victim.Position + new Vec2(0f, -30f));
        var e = Engage.StartRaid(u, raider, victim, u.Sectors[victim.Sector]);
        var first = e.Of(raider.Id)!;
        while (first.Timers.Count < 3) first.Timers.Add(0f);
        first.Timers[2] = 1.5f;
        float courage = first.Courage;
        Engage.Leave(u, e, first, CombatExit.Fled);
        ulong rng = u.Rng.State;
        var again = Engage.Join(u, e, raider, 0, CombatTactics.Raider, 5f);
        Assert.True(u.Fights.Of(raider.Id) == e);
        if (reuse)
        {
            Assert.Same(first, again);
            Assert.Equal(1, e.Participants.Count(p => p.Ship == raider.Id));
            Assert.True(e.Of(raider.Id) is { Active: true });
            Assert.Equal(courage, again.Courage);
            Assert.Equal(1.5f, again.TimerOf(2));
            Assert.Equal(rng, u.Rng.State);   // kein Zufallszug
        }
        else
        {
            Assert.Equal(2, e.Participants.Count(p => p.Ship == raider.Id));
            Assert.False(e.Of(raider.Id)!.Active);   // der alte Fehler: Of findet den abgelaufenen Eintrag
            Assert.NotEqual(rng, u.Rng.State);
        }
    }

    /// <summary>Runde 7 (Prüfung Runde 6): Spieler mitten in einer Belagerung mit gesetztem LastShotTick — Speichern, Laden, Weiterlauf bit-identisch.</summary>
    [Fact]
    public void Belagerung_LastShotTick_SpeichernLadenBitIdentisch()
    {
        var u = Standard(start: "war");
        var me = u.Ships[u.PlayerShip];
        var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
        var runner = new SimulationRunner();
        runner.Step(u);
        foreach (int s in PlayerGunnery.WeaponSlots(u, me)) PlayerGunnery.TryFireAtStation(u, me, s, hq.Id, FiringArc.Bearing(hq.Position - me.Position));
        for (int i = 0; i < 6; i++) runner.Step(u);
        var p = Siege.Of(u, hq.Id)!.Of(me.Id)!;
        Assert.True(p.Active && p.LastShotTick > 0);
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        Assert.Contains("LastShotTick", json);
        var (copy, _) = SaveGameJson.Read(json);
        Assert.Equal(p.LastShotTick, Siege.Of(copy, hq.Id)!.Of(me.Id)!.LastShotTick);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        var r2 = new SimulationRunner();
        for (int i = 0; i < 300; i++) { runner.Step(u); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
    }

    /// <summary>
    /// Runde 7 (Handy-Test Runde 6): fällt die Station, ist das Verlassen der Belagerung ein Sieg, kein Rückzug (vorher „Rückzug: Angriff … abgebrochen“ im
    /// Moment von „AUS“); eine ausgeschaltete Station lädt ihren Schild nicht, ihre Hülle wird wie bisher repariert (600 je Tag) bis zur Wiedereinschaltung.
    /// </summary>
    [Fact]
    public void Belagerung_StationAus_SiegmeldungUndKeinSchildAusgeschaltet()
    {
        var u = Standard(start: "war");
        u.Rules.StationCapitulation = false;   // prüft die Phase-28-Regel (Regierungsstation nur ausgeschaltet); Kapitulation/Zerstörung: KapitulationTests
        var me = u.Ships[u.PlayerShip];
        var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
        var runner = new SimulationRunner();
        runner.Step(u);
        foreach (int s in PlayerGunnery.WeaponSlots(u, me)) PlayerGunnery.TryFireAtStation(u, me, s, hq.Id, FiringArc.Bearing(hq.Position - me.Position));
        var ev = Capture(u);
        // Station fällt: Hülle auf 0 über die Trefferregel des Spiels (StationCombat.Apply)
        StationCombat.Apply(u, Siege.Of(u, hq.Id), hq, new HitResult(true, 1f, 0f, 0f, StationCombat.MaxHull(u, hq) + 1f), me.Owner);
        Assert.True(StationCombat.IsDown(u, hq));
        for (int i = 0; i < 60 && u.Fights.IsEngaged(me.Id); i++) runner.Step(u);
        var sw = ev.OfType<SiegeWithdrawn>().FirstOrDefault();
        if (sw is not null)
        {
            Assert.True(sw.Won);
            Assert.StartsWith("Sektor-HQ Beta außer Gefecht", sw.Message);
        }
        Assert.DoesNotContain(ev.OfType<SiegeWithdrawn>(), x => x.Message.StartsWith("Rückzug"));
        Assert.Equal("Geschütze schweigen (ausgeschaltet)", WarInfo.RangeStatus(u, hq, me));
        hq.Condition!.ShieldMissing = StationCombat.MaxShield(u, hq);
        hq.Condition.ShieldPauseUntil = 0;
        float hull = StationCombat.Hull(u, hq);
        for (int i = 0; i < 400; i++) runner.Step(u);
        Assert.Equal(0f, StationCombat.Shield(u, hq));
        Assert.True(StationCombat.Hull(u, hq) > hull, "die Hülle wird repariert");
        Assert.True(StationCombat.Hull(u, hq) - hull <= u.Rules.StationRepairPerDay * 400f / u.Rules.DayTicks + 13f, "nicht schneller als 600 je Tag");
    }
}
