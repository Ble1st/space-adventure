using SpaceSim.Core;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 20: Schwächen aus den Langläufen — Versorgungspanik bei geschlossenen Toren (Kartenteile, Ersatzbau, Notreserve, Überzahl-Abbau), Flottenkapazität je
/// Kartenteil und Nachkauf aus der Fraktionskasse, Polizei ohne Kauf-Abbau-Kreis, Pleite nur ohne Fracht, Ausmusterung nur bei Überbesetzung, Mehrzweckschiffe
/// ohne Flattern, Bauschiffe als Bestand der Fraktionen.
/// </summary>
public class Phase20Tests
{
    static readonly GateId AlphaGate = new(1), GammaGate = new(3), DeltaGate = new(5);

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    static void Set(Universe u, GateId g, GateState s) => GateAccess.SetState(u, u.Gates[g], s);

    static Good GoodOf(Universe u, string key) => u.FindGood(key)!;

    // ───────── Kartenteile ─────────

    [Fact]
    public void Kartenteile_ZerfallSchluesselUndUrsache()
    {
        var u = TestWorlds.Classic();
        var p = MapParts.Of(u);
        Assert.False(p.IsSplit);
        Assert.Single(p.Parts);
        Set(u, AlphaGate, GateState.Closed);
        p = MapParts.Of(u);
        Assert.True(p.IsSplit);
        Assert.Equal(2, p.Parts.Count);
        Assert.Equal(1, p.KeyOf(new SectorId(1)));
        Assert.Equal(2, p.KeyOf(new SectorId(3)));
        Assert.True(p.Same(new SectorId(2), new SectorId(4)));
        Assert.NotNull(MapParts.CutGate(u, 2));
        Set(u, AlphaGate, GateState.Destroyed);   // zerstört trennt wie geschlossen
        Assert.True(MapParts.Of(u).IsSplit);
        Set(u, AlphaGate, GateState.Open);
        Assert.False(MapParts.Of(u).IsSplit);
    }

    [Fact]
    public void Flottenkapazitaet_JeKartenteil_AnteilUndZaehlung()
    {
        var u = TestWorlds.Classic();
        new SimulationRunner().Run(u, 200);
        var trade = ShipJobs.ByKey("trade")!;
        float global = ShipJobs.UsedUnits(u, trade);
        Assert.Equal(global, FleetCapacity.Used(u, trade, 0));
        Assert.Equal(trade.Limit(u.Rules), FleetCapacity.Limit(u, trade, 0));
        Set(u, GammaGate, GateState.Closed);
        Assert.True(FleetCapacity.Divided(u));
        float share = FleetCapacity.Share(u, 1) + FleetCapacity.Share(u, 3);
        Assert.Equal(1f, share, 3);
        Assert.True(FleetCapacity.Share(u, 3) > 0f && FleetCapacity.Share(u, 3) < 0.5f);
        Assert.Equal(global, FleetCapacity.Used(u, trade, 1) + FleetCapacity.Used(u, trade, 3), 3);
        Assert.Equal(trade.Limit(u.Rules) * FleetCapacity.Share(u, 3), FleetCapacity.Limit(u, trade, 3), 3);
    }

    // ───────── Versorgungspanik ─────────

    [Fact]
    public void Panik_AlphaZu_RestBautWasserwerk_UndEsWirdFertig()
    {
        // Phase 26 (Prüfung): pfadunabhängig — ob die Panik oder der normale Lebenszyklus zuerst auf die Wasserknappheit im Restteil antwortet, hängt am
        // Zufallspfad; geprüft wird das Ergebnis (ein fertiges Wasserwerk außerhalb Alphas, keine offene Panik, Farmen laufen) und, falls die Panik
        // ausgelöst wurde, dass sie mit einem Wasserwerk antwortete. Die Panik-Entscheidung selbst prüft der deterministische Test darunter.
        var u = TestWorlds.Classic();
        var ev = Capture(u);
        var runner = new SimulationRunner();
        runner.Run(u, 1000);
        foreach (var extra in u.Stations.Values.Where(s => (s.TypeKey == "water_plant" || s.Site?.TypeKey == "water_plant") && s.Sector.Value != 1).Select(s => s.Id).ToList()) u.RemoveStation(extra, "test");
        Set(u, AlphaGate, GateState.Closed);   // Wasserwerk Alpha ist das einzige Wasserwerk; die Farmen liegen in Beta und Gamma
        runner.Run(u, 30_000);
        var water = GoodOf(u, "water").Id;
        var plant = u.Stations.Values.FirstOrDefault(s => !s.IsBuildSite && s.TypeKey == "water_plant" && s.Sector.Value != 1);
        Assert.NotNull(plant);   // der Restteil hat wieder ein fertiges Wasserwerk
        Assert.False(u.PanicSince.ContainsKey((2, water)));   // keine offene Panik
        var raised = ev.OfType<SupplyPanicRaised>().Where(e => e.Part.Value == 2 && e.Good == water).ToList();
        if (raised.Count > 0)
        {
            Assert.All(raised, r => Assert.Equal("water_plant", r.TypeKey));
            Assert.All(raised, r => Assert.True(r.Gate.Value is 1 or 2));
        }
        foreach (var part in MapParts.Of(u).Parts)
            Assert.True(SupplyPanic.PanicStations(u, part[0].Value) <= u.Rules.PanicReserveStations);
        var farms = u.Stations.Values.Where(s => s.TypeKey == "farm" && s.Sector.Value != 1).ToList();
        Assert.NotEmpty(farms);
        Assert.All(farms, f => Assert.True(f.Activity > 0.3f, $"{f.Name} läuft wieder (Auslastung {f.Activity:0.00})"));
    }

    [Fact]
    public void Panik_Entscheidung_WasserwerkFehltImRestteil_Deterministisch()
    {
        // ohne Zufallspfad: Alpha (einziges Wasserwerk) abgeschnitten → der Restteil vermisst Wasser, die Panik plant ein Wasserwerk und legt es im Restteil an
        var u = TestWorlds.Classic();
        new SimulationRunner().Run(u, 20);
        foreach (var extra in u.Stations.Values.Where(s => (s.TypeKey == "water_plant" || s.Site?.TypeKey == "water_plant") && s.Sector.Value != 1).Select(s => s.Id).ToList()) u.RemoveStation(extra, "test");
        Set(u, AlphaGate, GateState.Closed);
        var water = GoodOf(u, "water").Id;
        Assert.Contains(SupplyPanic.Missing(u, 2), m => m.Good == water);
        var type = SupplyPanic.PlanFor(u, 2, water);
        Assert.Equal("water_plant", type?.Key);
        Assert.True(SupplyPanic.StartBuild(u, 2, type!, water));
        var site = Assert.Single(u.Stations.Values, s => s.Site is { Panic: true } bs && bs.TypeKey == "water_plant");
        Assert.NotEqual(1, site.Sector.Value);
        Assert.True(SupplyPanic.ProducedIn(u, 2, water));   // die laufende Panik-Baustelle zählt als Hersteller
    }

    [Fact]
    public void Panik_NichtHerstellbar_MeldetOhneBau_UndEndetMitOffenemTor()
    {
        var u = TestWorlds.Classic();
        var ev = Capture(u);
        var runner = new SimulationRunner();
        runner.Run(u, 100);
        Set(u, DeltaGate, GateState.Closed);   // Platinerz gibt es nur in Delta
        runner.Run(u, u.Rules.PanicDelayTicks + 200);
        var platinum = GoodOf(u, "platinum").Id;
        var p = Assert.Single(ev.OfType<SupplyPanicRaised>(), e => e.Good == platinum && e.Part.Value == 1);
        Assert.Null(p.TypeKey);
        Assert.Null(SupplyPanic.PlanFor(u, 1, platinum));
        Assert.DoesNotContain(ev.OfType<SupplyPanicBuild>(), b => b.Good == platinum);
        Assert.Contains("nicht herstellbar", SupplyPanic.Describe(u, p));
        // Bausätze: Werft fehlt im Rest — die Phase-18-Regel gilt nur, solange eine Herstellerin erreichbar ist
        var kit = GoodOf(u, "ship_kit_light").Id;
        Assert.False(StationLifecycleSystem.FleetGoodWithProducer(u, kit));
        Set(u, DeltaGate, GateState.Open);
        Assert.True(StationLifecycleSystem.FleetGoodWithProducer(u, kit));
        runner.Run(u, 40);
        Assert.Empty(u.PanicSince);
    }

    [Fact]
    public void Panik_KurzeSchliessung_LoestNichtsAus()
    {
        var u = TestWorlds.Classic();
        var ev = Capture(u);
        var runner = new SimulationRunner();
        runner.Run(u, 100);
        Set(u, AlphaGate, GateState.Closed);
        runner.Run(u, u.Rules.PanicDelayTicks - 100);
        Set(u, AlphaGate, GateState.Open);
        runner.Run(u, 2000);
        Assert.Empty(ev.OfType<SupplyPanicRaised>());
        Assert.Empty(ev.OfType<SupplyPanicBuild>());
    }

    [Fact]
    public void Ueberzahl_NachWiederverbindung_PanikStationWirdStillgelegt()
    {
        var u = TestWorlds.Classic();
        new SimulationRunner().Run(u, 1);
        u.Tick = 100_000;
        var extra = StationFactory.Create(u, u.NewStationId(), u.StationTypes["water_plant"], "Ersatz", new SectorId(2), new Vec2(0, 140), new ActorId(1));
        extra.Panic = true; extra.Activity = 0.01f; extra.BuiltTick = 0;
        // Phase 22: „nutzlos“ heißt länger nicht rentabel (Rentabilitätsmessung statt Auslastung)
        extra.Profit.Since = 0; extra.Profit.Rate = -1f; extra.Profit.LossTicks = u.Rules.UnprofitableTicks;
        u.AddStation(extra);
        foreach (var st in u.Stations.Values)
            if (st.Market.TryGetValue(GoodOf(u, "water").Id, out var w)) w.Amount = w.Target * 2f;   // Wasser reichlich: der Ersatz ist überzählig
        u.Rules.MaxStations = StationProtection.CountEconomic(u) - 1;
        var ev = Capture(u);
        // solange die Karte zerfallen ist, bleibt der Ersatz
        Set(u, AlphaGate, GateState.Closed);
        Assert.False(StationReview.TryRetireSurplus(u, StationLifecycleSystem.Scan(u)));
        Set(u, AlphaGate, GateState.Open);
        Assert.True(StationReview.TryRetireSurplus(u, StationLifecycleSystem.Scan(u)));
        var r = Assert.Single(ev.OfType<StationRetired>());
        Assert.Equal(extra.Id, r.Station);
        Assert.Equal(0, r.ForGood.Value);
        Assert.False(u.Stations.ContainsKey(extra.Id));
        // Abklingzeit: keine zweite Stilllegung gleich danach
        Assert.False(StationReview.TryRetireSurplus(u, StationLifecycleSystem.Scan(u)));
    }

    // ───────── Flotte: Pleite, Ausmusterung, Nachkauf ─────────

    static Ship IdleTrader(Universe u, float credits, long idleSince)
    {
        var ship = u.Ships.Values.Where(s => s.IsEconomyAi && s.Role == ShipRole.Trader).OrderBy(s => s.Id.Value).First();
        if (ship.Transit is not null) u.FinishTransit(ship);
        ship.Order = null; ship.Phase = TradePhase.Idle; ship.Cargo.Clear();
        ship.Credits = credits; ship.IdleSince = idleSince;
        return ship;
    }

    [Fact]
    public void Pleite_NurOhneFracht()
    {
        var u = TestWorlds.Classic();
        u.Tick = 10_000;
        var ship = IdleTrader(u, 0f, 0);
        ship.Cargo[GoodOf(u, "hydrogen").Id] = 20f;
        new FleetSystem().Step(u, 20);
        Assert.True(u.Ships.ContainsKey(ship.Id), "mit Fracht an Bord nicht pleite");
        ship.Cargo.Clear();
        new FleetSystem().Step(u, 20);
        Assert.False(u.Ships.ContainsKey(ship.Id), "ohne Geld und Fracht pleite");
    }

    /// <summary>
    /// Phase 22 (angepasst): Ausgemustert wird nach der Rentabilitätsmessung, nicht mehr nach Leerlauf und voller Aufgabe — ein lange untätiges Schiff, dessen
    /// Messung nicht „länger nicht rentabel“ sagt, bleibt auch bei voller Aufgabe; die Kaufsperre danach gilt weiter.
    /// </summary>
    [Fact]
    public void Ausmusterung_NurBeiUeberbesetzung_DanachKaufsperre()
    {
        var u = TestWorlds.Classic();
        u.Tick = 50_000;
        var trade = ShipJobs.ByKey("trade")!;
        var ship = IdleTrader(u, 2000f, 0);
        u.Rules.MaxTraders = (int)MathF.Floor(ShipJobs.UsedUnits(u, trade));   // Aufgabe voll, Schiff lange untätig — aber nicht als unrentabel gemessen
        new FleetSystem().Step(u, 20);
        Assert.True(u.Ships.ContainsKey(ship.Id));
        ship.Profit.Since = 0; ship.Profit.Rate = -0.2f; ship.Profit.LossTicks = u.Rules.UnprofitableTicks;   // länger nicht rentabel: ausmustern
        var ev = Capture(u);
        new FleetSystem().Step(u, 20);
        Assert.False(u.Ships.ContainsKey(ship.Id));
        Assert.Single(ev.OfType<ShipRetired>());
        Assert.True(u.IdleRetiredTick.ContainsKey(("trade", 0)));
        u.Rules.MaxTraders = 1000;
        Assert.False(FleetSystem.CanGrow(u, trade, 0), "Kaufsperre nach Ausmusterung");
        u.Tick += u.Rules.ExpandAfterRetireTicks;
        foreach (var s in u.Ships.Values) s.IdleSince = -1;
        Assert.True(FleetSystem.CanGrow(u, trade, 0));
    }

    [Fact]
    public void KeinNeukauf_SolangeEinArbeitsfaehigesSchiffUntaetigIst()
    {
        var u = TestWorlds.Classic();
        u.Tick = 20_000;
        var trade = ShipJobs.ByKey("trade")!;
        u.Rules.MaxTraders = 1000;
        foreach (var s in u.Ships.Values) s.IdleSince = -1;
        Assert.True(FleetSystem.CanGrow(u, trade, 0));
        IdleTrader(u, u.Rules.NewShipStartCredits, u.Tick - u.Rules.ExpandIdleTicks);
        Assert.False(FleetSystem.CanGrow(u, trade, 0));
        IdleTrader(u, 10f, u.Tick - u.Rules.ExpandIdleTicks);   // arm: kein Hinweis auf fehlende Arbeit
        Assert.True(FleetSystem.CanGrow(u, trade, 0));
    }

    [Fact]
    public void Flotte_TeilOhneKaeufer_FraktionskasseKauftNach()
    {
        var u = TestWorlds.Classic();
        new SimulationRunner().Run(u, 20);
        Set(u, GammaGate, GateState.Closed);
        foreach (var s in u.Ships.Values.Where(s => s.IsEconomyAi && s.Role == ShipRole.Trader).ToList()) u.RemoveShip(s.Id, "test");
        foreach (var a in u.Actors.Values.Where(a => a.IsFaction(u))) a.Treasury.Credits = 200_000f;
        var ev = Capture(u);
        new FleetSystem().Step(u, 20);
        var built = Assert.Single(ev.OfType<FleetShipBuilt>());
        var ship = u.Ships[built.Ship];
        Assert.Equal(ShipRole.Trader, ship.Role);
        Assert.True(u.Factions.ContainsKey(new FactionId(ship.Owner.Value)), "Eigenbetrieb der Fraktion");
        Assert.Contains(ev.OfType<TreasuryUpkeep>(), t => t.Purpose == "fleet" && t.Actor == ship.Owner);
        Assert.NotEqual(3, MapParts.KeyOf(u, ship));   // die Werft liegt im Rest-Teil
    }

    // ───────── Mehrzweckschiffe ─────────

    [Fact]
    public void Aufgabenwechsel_OhneErtrag_ErstNachLeerlauf()
    {
        var u = TestWorlds.Classic();
        u.Tick = 10_000;
        var ship = u.Ships.Values.First(s => s.IsEconomyAi);
        Assert.True(JobAssignmentSystem.IdleLongEnough(u, ship, 0.5f));
        ship.IdleSince = u.Tick - 100;
        Assert.False(JobAssignmentSystem.IdleLongEnough(u, ship, 0f));
        ship.IdleSince = u.Tick - u.Rules.JobIdleSwitchTicks;
        Assert.True(JobAssignmentSystem.IdleLongEnough(u, ship, 0f));
        ship.IdleSince = -1;
        Assert.False(JobAssignmentSystem.IdleLongEnough(u, ship, 0f));
    }

    [Fact]
    public void Aufgabenwechsel_TorZu_KeinFlattern_40000Ticks()
    {
        var u = TestWorlds.Classic();
        var runner = new SimulationRunner();
        var last = new Dictionary<ShipId, long>();
        long tooSoon = 0, switches = 0;
        u.Events.Raised += e =>
        {
            if (e is not JobChanged jc) return;
            switches++;
            if (last.TryGetValue(jc.Ship, out var t) && u.Tick - t < u.Rules.JobMinTicks) tooSoon++;
            last[jc.Ship] = u.Tick;
        };
        runner.Run(u, 2000);
        Set(u, DeltaGate, GateState.Closed);
        runner.Run(u, 38_000);
        Assert.Equal(0, tooSoon);
        // Vorher (Lauf G, Delta zu): 5459 Wechsel in 1 Mio. Ticks bei 4 Mehrzweckschiffen ≈ 220 je 40000 Ticks
        Assert.True(switches <= 60, $"{switches} Aufgabenwechsel in 40000 Ticks");
    }

    // ───────── Polizei ─────────

    [Fact]
    public void Polizei_Schwellen_MitLuecke()
    {
        var u = TestWorlds.Classic();
        var (buy, sell) = SecuritySystem.Thresholds(u, 2.0f);
        Assert.Equal(2, buy); Assert.Equal(2, sell);
        (buy, _) = SecuritySystem.Thresholds(u, 2.4f);
        Assert.Equal(2, buy);                           // ein kurzer Überfall reicht nicht für ein drittes Schiff
        (buy, _) = SecuritySystem.Thresholds(u, 2.6f);
        Assert.Equal(3, buy);
        (_, sell) = SecuritySystem.Thresholds(u, 2.2f);
        Assert.Equal(3, sell);                          // ein drittes Schiff bleibt, solange der Bedarf über 2,1 liegt
        (_, sell) = SecuritySystem.Thresholds(u, 2.05f);
        Assert.Equal(2, sell);
    }

    [Fact]
    public void Polizei_KauftUndMustertNichtImKreis_60000Ticks()
    {
        var u = TestWorlds.Classic();
        int bought = 0, gone = 0;
        u.Events.Raised += e => { if (e is PoliceCommissioned) bought++; else if (e is PoliceDecommissioned) gone++; };
        new SimulationRunner().Run(u, 60_000);
        // vorher: 51 gekauft und 51 abgebaut in 300000 Ticks (≈ 10 je 60000)
        Assert.True(bought + gone <= 6, $"Polizei gekauft {bought}, abgebaut {gone}");
        Assert.All(u.Factions.Keys, f => Assert.True(u.Actors[f].PoliceNeed > 0f));
    }

    // ───────── Bauschiffe ─────────

    [Fact]
    public void Bauschiffe_BleibenBestandDerFraktion_KasseZahlt()
    {
        var u = TestWorlds.Classic();
        var ev = Capture(u);
        new SimulationRunner().Run(u, 30_000);
        foreach (var f in u.Factions.Keys)
            Assert.True(FactionBuilderSystem.Of(u, f).Count >= u.Rules.MinBuildersPerFaction, $"Fraktion {f.Value} hat ihr Bauschiff");
        Assert.Contains(ev.OfType<TreasuryUpkeep>(), t => t.Purpose == "builder");
        // ein mittelloses, lange untätiges Fraktions-Bauschiff wird nicht als pleite aufgegeben
        var b = FactionBuilderSystem.Of(u, u.Factions.Keys.First()).First();
        if (b.Transit is not null) u.FinishTransit(b);
        b.Order = null; b.Credits = 0f; b.IdleSince = u.Tick - 10_000; b.Cargo.Clear();
        new FleetSystem().Step(u, 20);
        Assert.True(u.Ships.ContainsKey(b.Id));
    }

    [Fact]
    public void Bauschiffe_Mindestbestand_FraktionKauftNach()
    {
        var u = TestWorlds.Classic();
        new SimulationRunner().Run(u, 20);
        var f = u.Factions.Keys.OrderBy(k => k.Value).First();
        foreach (var b in FactionBuilderSystem.Of(u, f)) u.RemoveShip(b.Id, "test");
        u.Actors[f].Treasury.Credits = 200_000f;
        var ev = Capture(u);
        new FactionBuilderSystem().Step(u, FactionBuilderSystem.Interval);
        var c = Assert.Single(ev.OfType<BuilderCommissioned>());
        Assert.Equal(f, c.Faction);
        Assert.Equal(0, c.Site.Value);
        Assert.Single(FactionBuilderSystem.Of(u, f));
    }

    // ───────── Speichern ─────────

    [Fact]
    public void SaveRoundtrip_MitPanikKaufsperreUndPolizeibedarf_BitIdentisch()
    {
        var u = TestWorlds.Classic();
        var runner = new SimulationRunner();
        runner.Run(u, 500);
        Set(u, AlphaGate, GateState.Closed);
        runner.Run(u, u.Rules.PanicDelayTicks + 600);
        u.IdleRetiredTick[("mine", 2)] = u.Tick - 10;
        Assert.NotEmpty(u.PanicSince);
        Assert.Contains(u.Stations.Values, s => s.Site is { Panic: true });
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.Equal(u.PanicSince.Count, v.PanicSince.Count);
        Assert.Equal(u.IdleRetiredTick[("mine", 2)], v.IdleRetiredTick[("mine", 2)]);
        Assert.All(u.Factions.Keys, f => Assert.Equal(u.Actors[f].PoliceNeed, v.Actors[f].PoliceNeed));
        runner.Run(u, 3000);
        new SimulationRunner().Run(v, 3000);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }
}
