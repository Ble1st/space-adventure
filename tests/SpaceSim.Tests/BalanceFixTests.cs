using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausgleich nach dem Spielerbot-Test (docs/POLITIK.md „Ausgleich nach dem Bot-Test“): kein Baulimit, Bau in fremden und Piratensektoren, Regierungsunterhalt
/// (Anteil + Untergrenze), Spielersperren ärgern nur ihr Ziel, Wirtschaftsstationen wehren sich, Kapitulationssperre, Größenklasse nach Kampfstärke,
/// Schiffspreise wie IP2, Flottenunterhalt nach Größe, Tribut erst nach Verlusten, eigenes Gericht nimmt jede Strafe, Falke schneller als der Korsar.
/// </summary>
public class BalanceFixTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    static readonly SectorId Alpha = new(1), Nebelmark = new(9), Abgrund = new(11), Delta = new(4);
    static readonly FactionId Union = new(1), Guild = new(2);

    /// <summary>
    /// Alle Regeln dieser Runde zurücknehmen (für die Referenz-Hash-Tests; Teil der Rücknahmekette über <see cref="Runde44ScaleTests.RevertRunde44"/>):
    /// Schalter aus, Baulimits wie vorher (2/10/4), Jäger-Grundmasse 5.
    /// </summary>
    internal static void RevertBalanceFix(WorldDef def)
    {
        SchutzKonvoiTests.RevertSchutz(def);   // Ausbau Punkt 3 kam danach: Grenzverfolgung, Schutzvertrag, Konvois aus
        BanditenTests.RevertBandits(def);   // Ausbau Punkt 17 kam danach (erste Zeile; idempotent — RevertGas ruft es ebenfalls)
        GasNebulaTests.RevertGas(def);   // Gasernte und Nebel (danach): Gas, Ionengas, Gasraffinerie, Nebel, Gassammler zurück
        WirtschaftsabwehrTests.RevertWirtschaftsabwehr(def);   // Wirtschaftsstationen halten stand (danach): Alarm/Notschild aus, Geschütze/Hülle wie vorher
        StationsverschleissTests.RevertWear(def);   // Ausbau Punkt 8 (danach): kein Stationsverschleiß
        FirmensitzTests.RevertSeats(def);   // Ausbau Punkt 9 (danach): keine Firmensitze und Büros, Büro ohne Hangar
        BergungTests.RevertSalvage(def);   // Ausbau Punkt 4 (danach): keine NPC-Bergung, kein Bergeschiff
        SchmugglerTests.RevertSmuggling(def);   // Ausbau Punkte 5/10/14 (danach): Schmuggler, Schmuggel-/Forschungs-/Fahrgastaufträge, Passagierkabinen, Taxi
        FehdenTests.RevertFeuds(def);   // Ausbau Punkt 6 (danach): Fehden, Vermittlung, Regierungskrise aus
        KiKriegsschiffeTests.RevertWarNavy(def);   // Ausbau Punkt 2 (danach): Seeschlacht, Abwehr, Kriegsgeleit aus
        IdleShelfTests.RevertIdleShelf(def);   // Ausbau Punkt 12 (danach): ungenutzte Regale — Schalter aus
        var r = def.Rules ??= new EconomyRules();
        r.PlayerMaxSites = 2; r.PlayerMaxStations = 10; r.PlayerMaxStationsPerSector = 4;
        r.PlayerBuildForeignSectors = false; r.PlayerBuildHideoutSectors = false;
        r.PlayerGovernmentUpkeepShare = 1f; r.PlayerUpkeepFloorActive = false;
        r.PlayerLocksSourOnlyTarget = false;
        r.EconomicStationDefense = false; r.CapitulationCooldownTicks = 0;
        r.PlayerSizeByStrength = false;
        r.PlayerShipPricing = false; r.PlayerFleetUpkeepBySize = new();
        r.TributeDemandNeedsLosses = false;
        r.OwnCourtAcceptsAll = false;
        if (def.ShipClasses.FirstOrDefault(c => c.Key == "fighter") is { } f) f.BaseMass = 5f;
    }

    static Universe World(ulong seed = 1, string? start = null) => WorldBuilder.Build(TestWorlds.Def(fuel: false, research: false), seed, start);

    static FactionId Me(Universe u) => u.PlayerFaction ?? throw new Xunit.Sdk.XunitException("keine Spielerfraktion");

    /// <summary>Spieler reich, aktives Schiff abgedockt im Sektor neben einem freien Bauplatz für <paramref name="type"/>.</summary>
    static Vec2 Ready(Universe u, string type, SectorId sid, float credits = 2_000_000f)
    {
        u.Player.Treasury.Credits = credits;
        var ship = u.Ships[u.PlayerShip];
        if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
        u.MoveShip(ship, sid);
        var sec = u.Sectors[sid];
        var t = u.StationTypes[type];
        float half = StationPlacement.HalfArea(sec);
        for (float x = -half + 40f; x < half - 40f; x += 23f)
            for (float y = -half + 40f; y < half - 40f; y += 23f)
            {
                var p = new Vec2(x, y);
                if (!StationPlacement.Fits(u, sec, p, t.Radius)) continue;
                ship.Position = p + new Vec2(t.Radius + 25f, 0f);
                if (PlayerBuilding.PlaceIssue(u, t, p) is null) return p;
            }
        throw new Xunit.Sdk.XunitException($"kein Bauplatz für {type} in {sec.Name}: {PlayerBuilding.TypeIssue(u, t)}");
    }

    static Station FoundSeat(Universe u)
    {
        u.Player.Treasury.Credits = 400_000f;
        var ship = u.Ships[u.PlayerShip];
        if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
        u.MoveShip(ship, Nebelmark);
        foreach (var cand in new[] { new Vec2(0, 0), new Vec2(200, 200), new Vec2(-200, 200), new Vec2(200, -200), new Vec2(-200, -200) })
        {
            ship.Position = cand;
            if (PlayerSeats.FoundIssue(u, Nebelmark) is null) break;
        }
        var res = CommsActions.Default().Execute(u, $"seat:found:{Nebelmark.Value}");
        Assert.True(res.Success, res.Message);
        return PlayerSeats.Seats(u).Single();
    }

    [Fact]
    public void Schalter_CodeAlteRegel_StandardweltAn()
    {
        var c = new EconomyRules();
        Assert.False(c.PlayerBuildForeignSectors || c.PlayerBuildHideoutSectors || c.PlayerUpkeepFloorActive || c.PlayerLocksSourOnlyTarget
                     || c.EconomicStationDefense || c.PlayerSizeByStrength || c.PlayerShipPricing || c.TributeDemandNeedsLosses || c.OwnCourtAcceptsAll);
        Assert.Equal(1f, c.PlayerGovernmentUpkeepShare);
        Assert.Equal(0, c.CapitulationCooldownTicks);
        Assert.Empty(c.PlayerFleetUpkeepBySize);
        Assert.Equal((2, 10, 4), (c.PlayerMaxSites, c.PlayerMaxStations, c.PlayerMaxStationsPerSector));
        var r = World().Rules;
        Assert.True(r.PlayerBuildForeignSectors && r.PlayerBuildHideoutSectors && r.PlayerUpkeepFloorActive && r.PlayerLocksSourOnlyTarget
                    && r.EconomicStationDefense && r.PlayerSizeByStrength && r.PlayerShipPricing && r.TributeDemandNeedsLosses && r.OwnCourtAcceptsAll);
        Assert.Equal(0.15f, r.PlayerGovernmentUpkeepShare);
        Assert.Equal(480, r.CapitulationCooldownTicks);
        Assert.Equal((0, 0, 0), (r.PlayerMaxSites, r.PlayerMaxStations, r.PlayerMaxStationsPerSector));
        // Rücknahme schaltet alles aus
        var def = TestWorlds.Def(fuel: false, research: false);
        RevertBalanceFix(def);
        var off = WorldBuilder.Build(def, 1).Rules;
        Assert.False(off.PlayerShipPricing || off.EconomicStationDefense || off.PlayerUpkeepFloorActive);
        Assert.Equal(10, off.PlayerMaxStations);
    }

    // ───────── 1: kein Baulimit ─────────

    [Fact]
    public void KeinBaulimit_DritteBaustelleUndViele_EigeneStationen()
    {
        var u = World();
        for (int i = 0; i < 5; i++)
        {
            var p = Ready(u, "solar_plant", Nebelmark);
            PlayerBuilding.Start(u, u.StationTypes["solar_plant"], p, 0f);
        }
        Assert.Equal(5, PlayerBuilding.OwnSites(u).Count);
        Assert.DoesNotContain("Höchstens", PlayerBuilding.TypeIssue(u, u.StationTypes["farm"]) ?? "");
        // mit gesetzter Obergrenze greift die alte Regel weiter
        u.Rules.PlayerMaxStationsPerSector = 4;
        Assert.Contains("je Sektor", PlayerBuilding.TypeIssue(u, u.StationTypes["farm"]));
        u.Rules.PlayerMaxStationsPerSector = 0; u.Rules.PlayerMaxSites = 3;
        Assert.Contains("Baustellen gleichzeitig", PlayerBuilding.TypeIssue(u, u.StationTypes["farm"]));
    }

    // ───────── Bau in fremden Sektoren und im Piratensektor ─────────

    [Fact]
    public void FremderSektor_FreundlichErlaubt_KriegFeindlichSperreVerboten()
    {
        var u = World();
        var p = Ready(u, "farm", Alpha);
        Assert.Null(PlayerBuilding.PlaceIssue(u, u.StationTypes["farm"], p));
        var sec = u.Sectors[Alpha];
        // feindliche Beziehung
        Relations.Set(u, sec.Owner, Me(u), -0.5f);
        Assert.Contains("feindlich", PlayerBuilding.SectorIssue(u, Alpha));
        Relations.Set(u, sec.Owner, Me(u), 0.2f);
        Assert.Null(PlayerBuilding.SectorIssue(u, Alpha));
        // Sperre gegen den Spieler
        sec.Access.EntryLevel = 2; sec.Access.Against = Me(u); sec.Access.Enforced = true;
        Assert.Contains("sperrt", PlayerBuilding.SectorIssue(u, Alpha));
        sec.Access.EntryLevel = 0; sec.Access.Against = null;
        Assert.Null(PlayerBuilding.SectorIssue(u, Alpha));
        // Krieg
        u.Player.Treasury.Credits = 2_000_000f;
        Wars.PlayerDeclare(u, sec.Owner);
        Assert.Contains("Krieg", PlayerBuilding.SectorIssue(u, Alpha));
    }

    [Fact]
    public void Piratensektor_BauErlaubt_LaeuftOhneAbsturz()
    {
        var u = World();
        // Ausbau Punkt 17: Der Abgrund gehört den Banditen (feindlich) — Bauversuch dort greift die Fremdregel. Das Korsaren-Versteck liegt in Delta (Gilde, freundlich).
        Assert.Contains(u.Sectors[Delta].Stations, id => StationProtection.IsHideout(u, u.Stations[id]));
        Assert.Contains("feindlich", PlayerBuilding.SectorIssue(u, Abgrund));
        var p = Ready(u, "solar_plant", Delta);
        var site = PlayerBuilding.Start(u, u.StationTypes["solar_plant"], p, 0f);
        foreach (var id in u.Sectors[Delta].Stations.Where(i => i != site.Id))
            Assert.True(Vec2.Distance(site.Position, u.Stations[id].Position) >= site.Radius + u.Stations[id].Radius);
        var runner = new SimulationRunner();
        for (int i = 0; i < 3000; i++) runner.Step(u);
        Assert.True(u.Stations.ContainsKey(site.Id));
        Assert.Equal(u.PlayerActor, u.Stations[site.Id].Owner);
    }

    [Fact]
    public void Umkaempft_SenktNurDieBeziehungZumBesitzer()
    {
        var u = World();
        var me = Me(u);
        var sec = u.Sectors[Alpha];
        var owner = sec.Owner;
        var third = u.NpcFactionIds().First(f => f != owner);
        float o0 = Relations.Effective(u, owner, u.PlayerActor), t0 = Relations.Effective(u, third, u.PlayerActor), ot0 = Relations.Effective(u, owner, third);
        sec.Contested = true;
        sec.Influence[owner] = 0.5f; sec.Influence[me] = 0.45f;
        FactionPolicySystem.StepRelations(u);
        Assert.Equal(o0 - u.Rules.ContestOpinionLoss, Relations.Effective(u, owner, u.PlayerActor), 4);
        Assert.Equal(t0, Relations.Effective(u, third, u.PlayerActor), 4);
        // KI-Paar Besitzer–Dritter: nur die übliche Drift, kein Umkämpft-Abzug durch den Spieler
        var u2 = World();
        FactionPolicySystem.StepRelations(u2);
        Assert.Equal(Relations.Effective(u2, owner, third), Relations.Effective(u, owner, third), 4);
        Assert.NotEqual(0f, ot0);
    }

    // ───────── 2: Regierungsunterhalt ─────────

    [Fact]
    public void Unterhalt_Anteil_Untergrenze_Unbezahlt_UndWiederInBetrieb()
    {
        var u = World();
        var seat = FoundSeat(u);
        var t = u.StationTypes[seat.TypeKey];
        u.Player.Treasury.Credits = 100_000f;
        new UpkeepSystem().Step(u, 10);
        Assert.Equal(100_000f - t.UpkeepPerTick * 0.15f * 10f, u.Player.Treasury.Credits, 1);
        Assert.False(seat.Unpaid);
        // Kasse leer: keine Schulden, Station ruht, Ereignis, kein Einfluss/Gericht, Sektor verliert die Behörde
        u.Player.Treasury.Credits = 0.5f;
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        new UpkeepSystem().Step(u, 10);
        Assert.Equal(0.5f, u.Player.Treasury.Credits, 3);
        Assert.True(seat.Unpaid);
        Assert.Contains(ev, e => e is PlayerGovernmentUnpaidChanged { Unpaid: true } g && g.Station == seat.Id);
        Assert.False(new CourtService().IsAvailable(u, seat));
        Assert.False(SectorControl.HasAuthority(u, u.Sectors[Nebelmark], Me(u)));
        Assert.Equal(0f, ThreatMap.BaseSecurity(u, u.Sectors[Nebelmark], Me(u)));
        Assert.Contains(seat.Name, PlayerUpkeep.StatusText(u));
        // knapp über der Untergrenze: erst mit dem Puffer wieder in Betrieb (kein Flackern)
        u.Player.Treasury.Credits = 100f;
        new UpkeepSystem().Step(u, 10);
        Assert.True(seat.Unpaid);
        u.Player.Treasury.Credits = 10_000f;
        new UpkeepSystem().Step(u, 10);
        Assert.False(seat.Unpaid);
        Assert.Contains(ev, e => e is PlayerGovernmentUnpaidChanged { Unpaid: false });
        Assert.True(new CourtService().IsAvailable(u, seat));
        Assert.Equal("", PlayerUpkeep.StatusText(u));
        // Mittelfristige Rechnung: HQ + 6 Behörden ≈ 58 000 Cr/h (≈ 13 % einer Flotteneinnahme von 450 000 Cr/h)
        float perHour = (u.StationTypes["headquarters"].UpkeepPerTick + 6f * u.StationTypes["sector_hq"].UpkeepPerTick) * 0.15f * SimClock.TicksPerSecond * 3600f;
        Assert.InRange(perHour, 40_000f, 80_000f);
    }

    // ───────── 3: Sperren des Spielers ─────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SpielerSperre_AergertNurIhrZiel(bool rule)
    {
        var u = World();
        FoundSeat(u);
        u.Rules.PlayerLocksSourOnlyTarget = rule;
        var me = Me(u);
        var sec = u.Sectors[Nebelmark];
        sec.Access.EntryLevel = 2; sec.Access.Against = Union; sec.Access.Enforced = true;
        float union0 = Relations.Effective(u, Union, u.PlayerActor), guild0 = Relations.Effective(u, Guild, u.PlayerActor);
        FactionPolicySystem.StepRelations(u);
        Assert.True(Relations.Effective(u, Union, u.PlayerActor) < union0);
        if (rule) Assert.Equal(guild0, Relations.Effective(u, Guild, u.PlayerActor), 4);
        else Assert.True(Relations.Effective(u, Guild, u.PlayerActor) < guild0);
        Assert.Equal(2, FactionPolicySystem.LocksAimedAt(u, me, Union));
        Assert.Equal(0, FactionPolicySystem.LocksAimedAt(u, me, Guild));
    }

    // ───────── 4: Wirtschaftsstationen wehren sich, Kapitulationssperre ─────────

    [Fact]
    public void Wirtschaftsstationen_HuelleSchildGeschuetze_RegierungUnveraendert()
    {
        var u = World();
        Station Of(string key) => u.StationsById().First(s => s.TypeKey == key);
        var farm = Of("farm");   // M
        // „Wirtschaftsstationen halten stand“ (danach) setzt in der Standardwelt Hüllenfaktor und Geschützzahl höher — hier aus den Regeln gelesen
        float factor = u.Rules.EconomicStationHullFactor;
        Assert.Equal(u.Rules.StationBaseHull * factor, StationCombat.MaxHull(u, farm));
        Assert.Equal(80f, StationCombat.MaxShield(u, farm));
        Assert.Equal(u.Rules.EconomicStationGuns[0], StationCombat.DefenseOf(u, farm)!.Guns.Count);
        Assert.True(StationCombat.SelfDefenseOnly(u, farm));
        var works = Of("drive_works");   // L
        Assert.Equal(u.Rules.EconomicStationGuns[1], StationCombat.DefenseOf(u, works)!.Guns.Count);
        Assert.Equal(1400f * factor, StationCombat.MaxHull(u, works));
        var solar = Of("solar_plant");   // S: unverändert
        Assert.Null(StationCombat.DefenseOf(u, solar));
        var hq = Of("sector_hq");   // Regierung: eigene Werte aus der Weltdatei
        Assert.Same(u.StationTypes["sector_hq"].Defense, StationCombat.DefenseOf(u, hq));
        Assert.False(StationCombat.SelfDefenseOnly(u, hq));
        u.Rules.EconomicStationDefense = false;
        Assert.Null(StationCombat.DefenseOf(u, farm));
        Assert.Equal(u.Rules.StationBaseHull, StationCombat.MaxHull(u, farm));
    }

    [Fact]
    public void Kapitulation_SperreNachBesitzerwechsel()
    {
        var u = World();
        var farm = u.StationsById().First(s => s.TypeKey == "farm" && u.FactionOf(s) is { } f && u.IsNpcFaction(f));
        Assert.NotNull(Capitulation.TakerFor(u, farm, u.PlayerActor));
        var other = u.NpcFactionIds().First(f => u.FactionOf(farm) != f);
        Capitulation.Capitulate(u, farm, (ActorId)other, (ActorId)other);
        Assert.True(Capitulation.Locked(u, farm));
        Assert.Null(Capitulation.TakerFor(u, farm, u.PlayerActor));
        u.Tick += u.Rules.CapitulationCooldownTicks;
        Assert.False(Capitulation.Locked(u, farm));
        Assert.NotNull(Capitulation.TakerFor(u, farm, u.PlayerActor));
    }

    // ───────── 5: Größenklasse nach Kampfstärke ─────────

    [Fact]
    public void Groessenklasse_SchiffeNachKampfstaerke()
    {
        var u = World();
        var r = u.Rules;
        ShipStats StatsOf(string m) { var mm = u.ShipModels[m]; return ShipStats.Compute(u.ShipClasses[mm.ClassKey], mm.Fit.Select(k => k is null ? null : u.Modules[k]).ToList()); }
        Assert.Equal(0.25f, ActorRules.ShipSizeWeight(r, StatsOf("unit_miner_100")), 3);
        Assert.InRange(ActorRules.ShipSizeWeight(r, StatsOf("unit_trader_100")), 0.4f, 0.45f);
        Assert.InRange(ActorRules.ShipSizeWeight(r, StatsOf("destroyer")), 3f, 3.2f);
        Assert.Equal(4f, ActorRules.ShipSizeWeight(r, StatsOf("battlecruiser")), 3);
        // 30 Schürfer machten vorher einen Staat (30 × 1), jetzt 7,5 + Rest → Firma
        var start = u.Ships[u.PlayerShip];
        u.Player.Treasury.Credits = 0f;
        for (int i = 0; i < 29; i++)
        {
            var m = u.ShipModels["unit_miner_100"];
            var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor,
                u.StationsById().First(st => st.Sector == start.Sector), 0f);
            u.AddShip(s);
        }
        var cls = ActorRules.ClassOf(u, ActorRules.Index(u), u.PlayerActor);
        Assert.True(cls < ActorSizeClass.Corporation, cls.ToString());
        u.Rules.PlayerSizeByStrength = false;
        Assert.Equal(ActorSizeClass.State, ActorRules.ClassOf(u, ActorRules.Index(u), u.PlayerActor));
        // KI-Akteure unverändert (Gewicht −1 = je Schiff 1)
        Assert.All(ActorRules.Index(u).Where(kv => kv.Key != u.PlayerActor), kv => Assert.Equal(-1f, kv.Value.ShipWeight));
    }

    // ───────── 6: Schiffspreise und Flottenunterhalt ─────────

    static (Universe u, Station yard, Ship me) AtYard()
    {
        var u = World();
        new SimulationRunner().Run(u, 2);
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        var me = u.Ships[u.PlayerShip];
        u.MoveShip(me, yard.Sector);
        me.Position = yard.DockPoint(me.Id);
        u.Player.Treasury.Credits = 1_000_000f;
        return (u, yard, me);
    }

    [Fact]
    public void Schiffspreis_Aufschlag_Ansehen_MindestpreisJeStaerke_Gerundet()
    {
        var (u, yard, me) = AtYard();
        var r = u.Rules;
        foreach (var key in new[] { "destroyer", "battlecruiser", "unit_trader_100", "corvette", "pickaxe" })
        {
            var q = ShipDealerService.Quote(u, yard, me, key)!;
            Assert.Equal(0f, q.Price % r.PlayerShipPriceRounding, 2);
            float material = ShipPurchase.Price(u, yard, q.Materials, 0f);
            float strength = ShipPricing.Strength(q.Stats);
            float f = ShipPricing.DealerFactor(u, yard, u.PlayerActor);
            Assert.True(q.Price >= MathF.Max(material, strength * r.PlayerShipMinPricePerStrength) * f - 0.01f, key);
            Assert.True(q.Price < MathF.Max(material, strength * r.PlayerShipMinPricePerStrength) * f + r.PlayerShipPriceRounding + 0.01f, key);
            Assert.True(q.Price / strength >= r.PlayerShipMinPricePerStrength, $"{key}: {q.Price / strength:0} Cr je Stärke");
        }
        // Ansehen: freundlich billiger, feindlich teurer
        var yardFaction = u.FactionOf(yard)!.Value;
        Relations.Set(u, yardFaction, Me(u), 0.9f);
        float friendly = ShipPricing.DealerFactor(u, yard, u.PlayerActor);
        Relations.Set(u, yardFaction, Me(u), -0.9f);
        float hostile = ShipPricing.DealerFactor(u, yard, u.PlayerActor);
        Assert.True(friendly < 1.2f && hostile > 1.2f, $"{friendly} {hostile}");
        Assert.True(friendly >= 1f);
        // Kauf bucht genau den Angebotspreis
        Relations.Set(u, yardFaction, Me(u), 0.2f);
        var quote = ShipDealerService.Quote(u, yard, me, "unit_trader_100")!;
        if (quote.Buyable)
        {
            float w0 = u.Player.Treasury.Credits;
            Assert.True(new ShipDealerService().Execute(u, yard, me, new ServiceRequest("buy:unit_trader_100", default, 0f)).Success);
            Assert.Equal(w0 - quote.Price, u.Player.Treasury.Credits, 1);
        }
        // Wiederverkauf bleibt unter dem Kaufpreis
        Assert.True(Hangar.SellPrice(u, u.Ships[u.PlayerShip]) < quote.Price);
        // Regel aus: alter Preis (Material × (1 + Montage))
        u.Rules.PlayerShipPricing = false;
        var old = ShipDealerService.Quote(u, yard, me, "destroyer")!;
        Assert.Equal(ShipPurchase.Price(u, yard, old.Materials, ShipPurchase.AssemblyOf(u, yard)), old.Price, 1);
    }

    [Fact]
    public void Flottenunterhalt_NachKlassengroesse()
    {
        var (u, yard, _) = AtYard();
        Ship Make(string model)
        {
            var m = u.ShipModels[model];
            var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, yard, 0f);
            u.AddShip(s);
            return s;
        }
        var small = Make("unit_trader_100"); var large = Make("destroyer"); var xl = Make("battlecruiser");
        float b = u.Rules.ShipUpkeepPerTick;
        Assert.Equal(b, ShipPricing.FleetUpkeepPerTick(u, small), 5);
        Assert.Equal(b * 2.5f, ShipPricing.FleetUpkeepPerTick(u, large), 5);
        Assert.Equal(b * 4f, ShipPricing.FleetUpkeepPerTick(u, xl), 5);
        foreach (var s in new[] { small, large, xl }) Assert.True(Commands.Give(u, s, new HoldCommand(), out var msg, ignoreCapacity: true), msg);
        Assert.Equal(b * 7.5f, Hangar.FleetUpkeepPerTick(u, u.PlayerActor), 5);
        float w0 = u.Player.Treasury.Credits;
        new HangarSystem().Step(u, 10);
        Assert.True(w0 - u.Player.Treasury.Credits >= b * 7.5f * 10f - 0.01f);
        u.Rules.PlayerFleetUpkeepBySize = new();
        Assert.Equal(b, ShipPricing.FleetUpkeepPerTick(u, xl), 5);
    }

    // ───────── 7: Tribut erst nach Verlusten ─────────

    [Fact]
    public void Tribut_ErstNachEchtenVerlustenDesGegners()
    {
        var u = World();
        u.Player.Treasury.Credits = 2_000_000f;
        u.Actors[Guild].Treasury.Credits = 200_000f;
        var w = Wars.PlayerDeclare(u, Guild);
        w.Since = u.Tick - 13_000; w.Weariness = 0.33f;
        Assert.Contains("nichts verloren", Wars.DemandIssue(u, Guild));
        Assert.False(Wars.HasLosses(w, Guild));
        if (w.A == Guild) w.LossA = 4000f; else w.LossB = 4000f;   // eine Station verloren
        Assert.True(Wars.HasLosses(w, Guild));
        Assert.Null(Wars.DemandIssue(u, Guild));
        if (w.A == Guild) { w.LossA = 0f; w.ShipsLostA = 1; } else { w.LossB = 0f; w.ShipsLostB = 1; }
        Assert.Null(Wars.DemandIssue(u, Guild));
        u.Rules.TributeDemandNeedsLosses = false;
        if (w.A == Guild) w.ShipsLostA = 0; else w.ShipsLostB = 0;
        Assert.Null(Wars.DemandIssue(u, Guild));
    }

    // ───────── 8: eigenes Gericht ─────────

    [Fact]
    public void EigenesGericht_NimmtJedeStrafe_FremdesGerichtUnveraendert()
    {
        var u = World();
        var seat = FoundSeat(u);
        Assert.Contains("court", u.StationTypes[seat.TypeKey].Services);
        var ship = u.Ships[u.PlayerShip];
        Wanted.Report(u, u.PlayerActor, Union, "assault");
        Relations.Set(u, Union, Me(u), -0.8f);
        Assert.True(CourtService.Accepts(u, seat, Union));
        var offer = new CourtService().Offers(u, seat, ship).Single(o => o.Key == $"pay:{Union.Value}");
        u.Player.Treasury.Credits = 1_000_000f;
        float t0 = u.Actors[Union].Treasury.Credits;
        var res = new CourtService().Execute(u, seat, ship, new ServiceRequest(offer.Key, default, 0f));
        Assert.True(res.Success, res.Message);
        Assert.Equal(t0 + offer.Price, u.Actors[Union].Treasury.Credits, 1);   // Geld an die Fraktion, keine Geldschleife
        // fremdes Gericht (Gilde) bei schlechter Beziehung der Union zur Gilde: unverändert nach Rechtshilfe
        var guildHq = u.StationsById().First(s => u.FactionOf(s) == Guild && new CourtService().IsAvailable(u, s));
        Relations.Set(u, Union, Guild, -0.5f);
        Assert.False(CourtService.Accepts(u, guildHq, Union));
        u.Rules.OwnCourtAcceptsAll = false;
        Assert.False(CourtService.Accepts(u, seat, Union));
    }

    // ───────── 9: Falke schneller als der Korsar ─────────

    [Fact]
    public void Falke_SchnellerAlsKorsarAufDerFlucht()
    {
        var u = World();
        float Speed(string m) { var mm = u.ShipModels[m]; return ShipStats.Compute(u.ShipClasses[mm.ClassKey], mm.Fit.Select(k => k is null ? null : u.Modules[k]).ToList()).Speed; }
        float corsair = Speed("corsair"), falcon = Speed("falcon");
        Assert.True(falcon > corsair * (1f + u.Rules.EngineModeSpeedBonus), $"Falke {falcon:0.00}, Korsar {corsair:0.00}");
        var def = TestWorlds.Def(fuel: false, research: false);
        RevertBalanceFix(def);
        var old = WorldBuilder.Build(def, 1);
        var fm = old.ShipModels["falcon"];
        Assert.Equal(corsair, ShipStats.Compute(old.ShipClasses[fm.ClassKey], fm.Fit.Select(k => k is null ? null : old.Modules[k]).ToList()).Speed, 2);
    }

    // ───────── Speichern ─────────

    [Fact]
    public void SpeichernLaden_Bitgleich_MitNeuenZustaenden()
    {
        var u = World();
        var seat = FoundSeat(u);
        u.Player.Treasury.Credits = 0.5f;
        new UpkeepSystem().Step(u, 10);
        Assert.True(seat.Unpaid);
        var farm = u.StationsById().First(s => s.TypeKey == "farm");
        Capitulation.Capitulate(u, farm, u.PlayerActor, u.PlayerActor);
        u.Player.Treasury.Credits = 2_000_000f;
        var w = Wars.PlayerDeclare(u, Guild);
        if (w.A == Guild) w.ShipsLostA = 2; else w.ShipsLostB = 2;
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        Assert.True(copy.Stations[seat.Id].Unpaid);
        Assert.Equal(farm.CapitulationLockUntil, copy.Stations[farm.Id].CapitulationLockUntil);
        Assert.Equal(2, copy.Wars.Single().A == Guild ? copy.Wars.Single().ShipsLostA : copy.Wars.Single().ShipsLostB);
        var r1 = new SimulationRunner(); var r2 = new SimulationRunner();
        for (int i = 0; i < 300; i++) { r1.Step(u); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
    }

    /// <summary>
    /// Bot-Fund: das Treuhandgeld einer Spieler-Baustelle war aufgebraucht, es fehlte nur noch 0,33 Hüllenplatten — der Spieler kann nur ganze Einheiten
    /// verkaufen und die Baustelle blieb für immer unfertig. Seine Lieferung darf den Rest (&lt; 1 Einheit) abschließen; NPC-Lieferungen bleiben exakt.
    /// </summary>
    [Fact]
    public void Baustelle_Bruchteilsrest_SpielerKannAbschliessen()
    {
        var u = World(3);
        var p = Ready(u, "solar_plant", Alpha);
        var site = PlayerBuilding.Start(u, u.StationTypes["solar_plant"], p, 0f);
        var bs = site.Site!;
        var good = bs.Required.Keys.OrderBy(g => g.Value).First();
        foreach (var kv in bs.Required) site.Market[kv.Key].Amount = kv.Value;
        site.Market[good].Amount = bs.Required[good] - 0.33f;
        Assert.InRange(bs.Missing(site, good), 0.3f, 0.36f);
        var ship = u.Ships[u.PlayerShip];
        ship.Cargo[good] = 1f;
        PlayerFleet.Undock(u, ship);
        ship.Position = site.DockPoint(ship.Id);
        PlayerFleet.Dock(u, ship, site);
        var svc = StationServices.Default().ForStation(u, site).First(s => s.Key == "trade");
        var res = svc.Execute(u, site, ship, new ServiceRequest($"sell:{good.Value}", good, 1f));
        Assert.True(res.Success, res.Message);
        Assert.True(bs.Complete(site));
    }

    /// <summary>Bot-Fund: Treuhandgeld aufgebraucht → Nachschuss aus der Spielerkasse ist möglich (Abheben bleibt gebunden), Summe aus Kasse + Treuhand bleibt gleich.</summary>
    [Fact]
    public void Baustelle_Nachschuss_EinzahlenJa_AbhebenNein()
    {
        var u = World(3);
        var p = Ready(u, "solar_plant", Alpha);
        var site = PlayerBuilding.Start(u, u.StationTypes["solar_plant"], p, 0f);
        site.Credits = 0f;
        Assert.Null(PlayerBuilding.TransferIssue(u, site.Id, 2500f));
        float sum = u.Player.Treasury.Credits + site.Credits;
        PlayerBuilding.Transfer(u, site.Id, 2500f);
        Assert.Equal(2500f, site.Credits, 2);
        Assert.Equal(sum, u.Player.Treasury.Credits + site.Credits, 2);
        Assert.NotNull(PlayerBuilding.TransferIssue(u, site.Id, -100f));
    }
}
