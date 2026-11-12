using SpaceSim.Core;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 19: Der Stationsbau überprüft seinen Bestand (Auslastung, Stilllegung/Umwidmung, Bauschiff-Nachkauf), Händler laden nichts über die Annahme ab,
/// Abwerfen bei Flottenschiffen mit laufendem Auftrag.
/// </summary>
public class StationReviewTests
{
    static readonly StationId WaterPlantAlpha = new(3), TitanWorksDelta = new(14);

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    /// <summary>Eine zusätzliche, nicht geschützte Station eines Typs (wie vom Lebenszyklus gebaut).</summary>
    static Station AddBuilt(Universe u, string type, StationId near, Vec2 offset)
    {
        var at = u.Stations[near];
        var st = StationFactory.Create(u, u.NewStationId(), u.StationTypes[type], "Test " + type, at.Sector, at.Position + offset, at.Owner);
        u.AddStation(st);
        return st;
    }

    /// <summary>
    /// Ausgangslage für die Regel: alle Plätze voll, eine nutzlose gebaute Titanhütte (Auslastung 2 %, Titan reichlich), Wasser seit 10000 Ticks knapp
    /// (Startwasserwerk entfernt, alle Wasserlager leer).
    /// </summary>
    static (Universe U, Station Idle) Scenario()
    {
        var u = TestWorlds.Classic();
        new SimulationRunner().Run(u, 1);
        u.Tick = 40_000;
        u.LastStationBuildTick = -1_000_000;
        var idle = AddBuilt(u, "titanium_works", TitanWorksDelta, new Vec2(0, 60));
        idle.Activity = 0.02f;
        idle.BuiltTick = 0;   // lange vor der Messung gebaut
        u.RemoveStation(WaterPlantAlpha, "test");
        foreach (var st in u.Stations.Values)
        {
            if (st.Market.TryGetValue(DemoUniverse.Water, out var w)) w.Amount = 0f;
            if (st.Market.TryGetValue(DemoUniverse.Titanium, out var t)) t.Amount = t.Target * 2f;
            if (st.Market.TryGetValue(DemoUniverse.Ice, out var i)) i.Amount = i.Target;
            if (st.Market.TryGetValue(DemoUniverse.Energy, out var e)) e.Amount = e.Target;
        }
        u.ScarceSince.Clear();
        u.ScarceSince[DemoUniverse.Water] = u.Tick - 10_000;
        u.Rules.MaxStations = StationProtection.CountEconomic(u);
        return (u, idle);
    }

    static void Lifecycle(Universe u) => new StationLifecycleSystem().Step(u, 20);

    // ───────── Auslastung ─────────

    [Fact]
    public void Auslastung_SinktOhneAbnehmer_ZutatenmangelGetrenntGemessen()
    {
        var u = TestWorlds.Classic();
        var steel = u.Stations[new StationId(1)];
        Assert.Equal(1f, steel.Activity);
        Assert.Equal(0, steel.BuiltTick);
        u.Rules.UtilizationWindowTicks = 600;
        // ohne Eisenerz: kein Takt läuft, alle fehlen an Zutaten
        steel.Market[DemoUniverse.IronOre].Amount = 0f;
        var runner = new SimulationRunner();
        for (int i = 0; i < 1200; i++) { steel.Market[DemoUniverse.IronOre].Amount = 0f; runner.Step(u); }
        Assert.True(steel.Activity < 0.2f, $"Auslastung {steel.Activity}");
        Assert.True(steel.Starved > 0.8f, $"Zutaten fehlten {steel.Starved}");
        // mit Erz und Abnehmern läuft das Werk wieder
        for (int i = 0; i < 1200; i++) { steel.Market[DemoUniverse.IronOre].Amount = 200f; steel.Market[DemoUniverse.Steel].Amount = 0f; runner.Step(u); }
        Assert.True(steel.Activity > 0.8f, $"Auslastung {steel.Activity}");
        Assert.True(steel.Starved < 0.2f);
        // neue Station: Fertigstellungs-Tick
        var built = AddBuilt(u, "steelworks", new StationId(1), new Vec2(0, 70));
        Assert.Equal(u.Tick, built.BuiltTick);
    }

    // ───────── Regel ─────────

    [Fact]
    public void Stilllegung_NutzloseStationMachtPlatzFuerAnhaltendeKnappheit()
    {
        var (u, idle) = Scenario();
        var ev = Capture(u);
        Assert.True(StationReview.Useless(u, idle));
        Lifecycle(u);
        var r = Assert.Single(ev.OfType<StationRetired>());
        Assert.Equal(idle.Id, r.Station);
        Assert.Equal(DemoUniverse.Water, r.ForGood);
        Assert.Equal("water_plant", r.ForType);
        Assert.False(u.Stations.ContainsKey(idle.Id));
        Assert.Contains(ev.OfType<StationRemoved>(), x => x.Station == idle.Id && x.Reason == "retired");
        // der frei gewordene Platz geht über den normalen Bauweg an den Bedarf: Baustelle für ein Wasserwerk
        Assert.Contains(u.Stations.Values, s => s.Site is { TypeKey: "water_plant" });
        Assert.Equal(u.Tick, u.LastStationRetireTick);
        // Restwert: Kasse + Lager × Anteil an den Besitzer, als Kasseneinnahme gemeldet
        var salvage = Assert.Single(ev.OfType<TreasuryIncome>(), t => t.Purpose == "salvage");
        Assert.Equal(r.Payout, salvage.Amount, 2);
        Assert.True(r.Payout > 0f);
    }

    [Theory]
    [InlineData("geschuetzt")]
    [InlineData("schonfrist")]
    [InlineData("ausgelastet")]
    [InlineData("produkt_knapp")]
    [InlineData("knappheit_kurz")]
    [InlineData("freier_platz")]
    [InlineData("abklingzeit")]
    [InlineData("kritisch")]
    [InlineData("regel_aus")]
    public void Stilllegung_Ausnahmen(string fall)
    {
        var (u, idle) = Scenario();
        switch (fall)
        {
            case "geschuetzt": idle.BankruptcyProtected = true; break;
            case "schonfrist": idle.BuiltTick = u.Tick - u.Rules.RetireGraceTicks + 100; break;
            case "ausgelastet": idle.Activity = u.Rules.RetireMaxActivity + 0.01f; break;
            case "produkt_knapp":
                foreach (var st in u.Stations.Values) if (st.Market.TryGetValue(DemoUniverse.Titanium, out var t)) t.Amount = 0f;
                break;
            case "knappheit_kurz": u.ScarceSince[DemoUniverse.Water] = u.Tick - u.Rules.RetireScarceTicks + 100; break;
            case "freier_platz": u.Rules.MaxStations += 1; break;
            case "abklingzeit": u.LastStationRetireTick = u.Tick - u.Rules.RetireCooldownTicks + 100; break;
            case "kritisch": u.RemoveStation(TitanWorksDelta, "test"); u.Rules.MaxStations -= 1; break;   // letzte Titanhütte
            case "regel_aus": u.Rules.StationRetireActive = false; break;
        }
        var ev = Capture(u);
        Lifecycle(u);
        Assert.Empty(ev.OfType<StationRetired>());
        Assert.True(u.Stations.ContainsKey(idle.Id));
        if (fall == "freier_platz") Assert.Contains(u.Stations.Values, s => s.Site is { TypeKey: "water_plant" });   // freier Platz: normaler Bau
    }

    [Fact]
    public void Subvention_StuetztDauerhaftNutzloseKritischeStationNicht()
    {
        var (u, idle) = Scenario();
        u.RemoveStation(TitanWorksDelta, "test");   // die Testhütte ist jetzt die letzte: kritisch
        Assert.True(StationCriticality.IsCritical(u, idle));
        Assert.True(StationReview.Useless(u, idle));
        Assert.False(SubsidySystem.Eligible(u, idle));
        idle.Activity = 0.5f;   // arbeitet sie, wird sie gestützt
        Assert.True(SubsidySystem.Eligible(u, idle));
    }

    /// <summary>
    /// Langlauf (konstruiertes Szenario): Die Startwelt verliert ihr Wasserwerk (Wasser wird knapp), und eine überzählige Titanhütte steht ohne Nachfrage —
    /// alle Plätze sind belegt. Erwartet: genau die Titanhütte wird stillgelegt (nach Schonfrist und Messfenster), ein Wasserwerk wird über Baustelle und
    /// Bauschiff gebaut (fehlt eins, kauft die Fraktion nach), danach pendelt nichts.
    /// </summary>
    [Fact]
    public void Langlauf_Umwidmung_TitanhuetteWirdWasserwerk_OhnePendeln()
    {
        var u = TestWorlds.Classic();
        var extra = AddBuilt(u, "titanium_works", TitanWorksDelta, new Vec2(0, 60));
        u.RemoveStation(WaterPlantAlpha, "test");
        u.Rules.MaxStations = StationProtection.CountEconomic(u);
        var ev = Capture(u);
        var runner = new SimulationRunner();
        long waterBuiltAt = -1;
        for (int i = 0; i < 60; i++)
        {
            runner.Run(u, 1000);
            if (waterBuiltAt < 0 && u.Stations.Values.Any(s => s.TypeKey == "water_plant" && !s.IsBuildSite)) waterBuiltAt = u.Tick;
        }
        var retired = ev.OfType<StationRetired>().ToList();
        Assert.NotEmpty(retired);
        Assert.Equal(extra.Id, retired[0].Station);
        Assert.Equal("water_plant", retired[0].ForType);
        Assert.True(waterBuiltAt > 0, "kein Wasserwerk gebaut");
        // kein Pendeln: höchstens eine Stilllegung je Abklingzeit, und das neue Wasserwerk wird nicht wieder stillgelegt
        foreach (var r in retired) Assert.NotEqual("water_plant", r.TypeKey);
        Assert.True(retired.Count <= 1 + 60_000 / u.Rules.RetireCooldownTicks);
        Assert.Contains(ev.OfType<StationBuilt>(), b => b.TypeKey == "water_plant");
        // Wasser ist am Ende nicht mehr dauerhaft knapp
        Assert.True(StationReview.Ratio(u, DemoUniverse.Water) < u.Rules.BuildPriceRatio, $"Wasser {StationReview.Ratio(u, DemoUniverse.Water):0.00}");
    }

    [Fact]
    public void Bauschiff_WirdNachgekauft_WennKeinesMehrDa()
    {
        var u = TestWorlds.Classic();
        new SimulationRunner().Run(u, 1);
        foreach (var b in u.Ships.Values.Where(s => s.Role == ShipRole.Builder).ToList()) u.RemoveShip(b.Id, "test");
        var steelworks = u.Stations[new StationId(1)];
        var faction = u.FactionOf(steelworks)!.Value;
        u.Actors[faction].Treasury.Credits = 200_000f;
        var spot = StationLifecycleSystem.FindFreeSpot(u, u.Sectors[steelworks.Sector])!.Value;
        var site = ConstructionRules.StartSite(u, u.StationTypes["water_plant"], steelworks.Sector, spot, steelworks.Owner, 9000f, steelworks.Id, null);
        var ev = Capture(u);
        new SimulationRunner().Run(u, 200);
        var c = Assert.Single(ev.OfType<BuilderCommissioned>());
        Assert.Equal(faction, c.Faction);
        var ship = u.Ships[c.Ship];
        Assert.Equal(ShipRole.Builder, ship.Role);
        Assert.Equal((ActorId)faction, ship.Owner);
        Assert.True(ship.IsSiteBuilder);
        Assert.Contains(ev.OfType<TreasuryUpkeep>(), t => t.Purpose == "builder" && Math.Abs(t.Amount - c.Cost) < 0.5f);
        // das Bauschiff übernimmt die Baustelle
        Assert.True(new SimulationRunner().RunUntilTest(u, () => site.Site?.Builder == ship.Id, 400));
        // ohne Geld (Rücklage) kein Kauf
        var u2 = TestWorlds.Classic();
        new SimulationRunner().Run(u2, 1);
        foreach (var b in u2.Ships.Values.Where(s => s.Role == ShipRole.Builder).ToList()) u2.RemoveShip(b.Id, "test");
        u2.Actors[faction].Treasury.Credits = u2.Rules.SubsidyReserve;
        var st2 = u2.Stations[new StationId(1)];
        ConstructionRules.StartSite(u2, u2.StationTypes["water_plant"], st2.Sector, StationLifecycleSystem.FindFreeSpot(u2, u2.Sectors[st2.Sector])!.Value, st2.Owner, 9000f, st2.Id, null);
        var ev2 = Capture(u2);
        new SimulationRunner().Run(u2, 200);
        Assert.Empty(ev2.OfType<BuilderCommissioned>());
    }

    [Fact]
    public void SaveRoundtrip_MitAuslastungUndKnappheit_BitIdentisch()
    {
        var (u, idle) = Scenario();
        Lifecycle(u);   // Stilllegung + Baustelle
        new SimulationRunner().Run(u, 300);
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.Equal(u.LastStationRetireTick, v.LastStationRetireTick);
        Assert.Equal(u.ScarceSince.Count, v.ScarceSince.Count);
        Assert.All(u.Stations.Values, s => { var t = v.Stations[s.Id]; Assert.Equal(s.Activity, t.Activity); Assert.Equal(s.Starved, t.Starved); Assert.Equal(s.BuiltTick, t.BuiltTick); });
        new SimulationRunner().Run(u, 2000);
        new SimulationRunner().Run(v, 2000);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    // ───────── Lagerpreise: kein Abladen über die Annahme ─────────

    [Fact]
    public void Haendler_LadenNichtsUeberDieAnnahmeAb_20000Ticks()
    {
        var u = TestWorlds.Classic();
        var ev = Capture(u);
        new SimulationRunner().Run(u, 20_000);
        var dumped = ev.OfType<CargoDumped>().ToList();
        // höchstens die Resttoleranz (unter einer Einheit) über der Annahme, nie ganze Ladungen
        Assert.All(dumped, d => Assert.True(d.Units < u.Rules.SaleRestTolerance + 0.001f, $"{u.Stations[d.Station].Name}: {d.Units} {u.Goods[d.Good].Name}"));
        // keine Geisteraufträge: Händler ohne Auftrag tragen keine Fracht, die eine erreichbare Station annähme
        Assert.All(u.Ships.Values.Where(s => s.Role == ShipRole.Trader && s.Order is TradeOrder), s => Assert.True(s.Transit is not null || s.Phase != TradePhase.Idle));
        Assert.All(u.Ships.Values.Where(s => s.IsEconomyAi && s.Order is TradeOrder o && o.Buy.Value == 0), s => Assert.Equal(TradePhase.GoingToSell, s.Phase));
    }

    [Fact]
    public void Haendler_RestOhneKaeufer_BleibtAnBord_DannImAllAbgeworfen()
    {
        var u = TestWorlds.Classic();
        new SimulationRunner().Run(u, 1);
        var electrolysis = u.Stations[new StationId(7)];   // Wasserstoff-Hersteller
        var tm = u.ShipModels["unit_trader_100"];
        var trader = ShipPurchase.Create(u, u.NewShipId(), tm.Key, u.ShipClasses[tm.ClassKey].Slots, tm.Fit, ShipRole.Trader, Controller.Ai, u.FreeActor, electrolysis, 500f);
        u.AddShip(trader);
        var plant = u.Stations[new StationId(4)];   // Kraftwerk Alpha nimmt Wasserstoff
        var h2 = DemoUniverse.Hydrogen;
        // alle Wasserstoff-Abnehmer voll: der Händler kommt mit 40 Einheiten an
        foreach (var st in u.Stations.Values)
            if (st.Market.TryGetValue(h2, out var e) && !st.Produces(h2)) e.Amount += st.AcceptableUnits(h2, u.Rules);   // Lager bis zur Annahmegrenze voll
        var entry = plant.Market[h2];
        float before = entry.Amount;
        trader.NextThinkTick = long.MaxValue;   // denkt erst nach der Ankunft
        trader.Cargo.Clear();
        trader.Cargo[h2] = 40f;
        trader.Order = new TradeOrder(new StationId(7), plant.Id, h2);
        trader.Phase = TradePhase.GoingToSell;
        Travel.Start(u, trader, plant.Sector, plant.DockPoint(trader.Id));
        var ev = Capture(u);
        Assert.True(new SimulationRunner().RunUntilTest(u, () => trader.Transit is null, 3000));
        Assert.True(entry.Amount <= before + u.Rules.SaleRestTolerance + 0.01f, $"Kraftwerk: {before} → {entry.Amount}");
        Assert.DoesNotContain(ev.OfType<CargoDumped>(), d => d.Units >= 1f);
        // der Rest bleibt zunächst an Bord (Frist StrandedCargoTicks; er versucht ihn bei jedem Nachdenken zu verkaufen)
        Assert.True(trader.Cargo.GetValueOrDefault(h2) > 1f || ev.OfType<TradeCompleted>().Any(t => t.Ship == trader.Id && t.Good == h2));
        Assert.DoesNotContain(ev.OfType<CargoDiscarded>(), d => d.Ship == trader.Id);
        // nimmt ihn bis zum Ablauf der Frist niemand, wirft der KI-Händler ihn im All ab (Ereignis), statt ihn ewig mitzuschleppen
        new SimulationRunner().Run(u, u.Rules.StrandedCargoTicks + 200);
        Assert.True(ev.OfType<CargoDiscarded>().Any(d => d.Ship == trader.Id && d.Good == h2) || ev.OfType<TradeCompleted>().Any(t => t.Ship == trader.Id && t.Good == h2));
        Assert.True(trader.Cargo.GetValueOrDefault(h2) < 40f);
    }

    // ───────── Abwerfen bei Flottenschiffen mit Auftrag ─────────

    static Ship Own(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        s.HomeBase = at.Id;
        u.AddShip(s);
        return s;
    }

    static void AssertNoGhosts(Universe u)
    {
        foreach (var s in u.Ships.Values)
        {
            if (s.Order is TradeOrder o) Assert.True(u.Stations.ContainsKey(o.Sell));
            if (s.Order is BuildOrder b && b.Site.Value != 0) Assert.True(u.Stations.ContainsKey(b.Site));
            if (s.Transit is { } t) Assert.True(t.ArrivalTick >= u.Tick - 5);   // Polizei/Piraten denken alle 5 Ticks
            Assert.All(s.Cargo.Values, n => Assert.True(n >= 0f && float.IsFinite(n)));
        }
    }

    [Fact]
    public void Abwerfen_HandelsauftragUnterwegs_AuftragLaeuftWeiter()
    {
        var u = TestWorlds.Classic();
        new SimulationRunner().Run(u, 1);
        u.PlayerWallet.Credits = 20_000f;
        var nomad = Own(u, "nomad", u.Stations[new StationId(5)]);
        Assert.True(Commands.Give(u, nomad, new TradeCommand(AutoLimits.None), out _));
        var runner = new SimulationRunner();
        Assert.True(runner.RunUntilTest(u, () => nomad.Phase == TradePhase.GoingToSell && nomad.Transit is not null && nomad.Cargo.Count > 0, 3000));
        var good = ((TradeOrder)nomad.Order!).Good;
        var sell = ((TradeOrder)nomad.Order!).Sell;
        Assert.Null(CargoJettison.Issue(u, nomad, good));
        Assert.True(CargoJettison.Jettison(u, nomad, good, 0f) > 0f);
        var ev = Capture(u);
        Assert.True(runner.RunUntilTest(u, () => nomad.Transit is null, 3000));
        runner.Run(u, 5);
        Assert.DoesNotContain(ev.OfType<TradeCompleted>(), t => t.Ship == nomad.Id && t.Good == good && t.To == sell && t.Amount > 0f);   // nichts geliefert, was nicht da ist
        Assert.True(nomad.Command is TradeCommand);   // der Auftrag läuft weiter
        AssertNoGhosts(u);
        // nach der Ankunft ohne Ware sucht er die nächste Runde
        Assert.True(runner.RunUntilTest(u, () => ev.OfType<TradeCompleted>().Any(t => t.Ship == nomad.Id && t.Amount > 0f), 6000));
    }

    [Fact]
    public void Abwerfen_Bauauftrag_TreuhandMaterialGesperrt_RestFrei()
    {
        var u = TestWorlds.Classic();
        new SimulationRunner().Run(u, 1);
        var steelworks = u.Stations[new StationId(1)];
        var foreman = Own(u, "foreman", steelworks);
        var spot = StationLifecycleSystem.FindFreeSpot(u, u.Sectors[steelworks.Sector])!.Value;
        var site = ConstructionRules.StartSite(u, u.StationTypes["water_plant"], steelworks.Sector, spot, steelworks.Owner, 9000f, steelworks.Id, null);
        Assert.True(Commands.Give(u, foreman, new BuildCommand(site.Id), out var msg), msg);
        var runner = new SimulationRunner();
        // unterwegs zur Baustelle mit Material aus dem Treuhandgeld
        Assert.True(runner.RunUntilTest(u, () => foreman.Order is BuildOrder { Site.Value: > 0 } && foreman.Phase == TradePhase.GoingToSell && foreman.Cargo.Count > 0, 4000));
        var material = foreman.Cargo.Keys.First();
        float escrow = site.Credits;
        float held = foreman.Cargo[material];
        var issue = CargoJettison.Issue(u, foreman, material);
        Assert.NotNull(issue);
        Assert.Contains("Treuhand", issue);
        Assert.Equal(0f, CargoJettison.Jettison(u, foreman, material, 0f));
        Assert.Equal(held, foreman.Cargo[material]);
        Assert.Equal(escrow, site.Credits);
        // am Dock: das Abwurf-Angebot des Handelsdienstes ist gesperrt (Menge 0) und nennt den Grund; Ausführen scheitert ohne Änderung
        var drop = new TradeService().Offers(u, steelworks, foreman).Single(o => o.Key == $"drop:{material.Value}");
        Assert.Equal(0f, drop.Amount);
        Assert.Equal(issue, drop.Note);
        Assert.False(new TradeService().Execute(u, steelworks, foreman, new ServiceRequest($"drop:{material.Value}", default, 0f)).Success);
        Assert.Equal(held, foreman.Cargo[material]);
        // Ware, die die Baustelle nicht braucht, geht
        foreman.Cargo[DemoUniverse.Food] = 2f;
        Assert.Null(CargoJettison.Issue(u, foreman, DemoUniverse.Food));
        Assert.Equal(2f, CargoJettison.Jettison(u, foreman, DemoUniverse.Food, 0f));
        // der Auftrag läuft weiter: die Baustelle bekommt ihr Material
        Assert.True(runner.RunUntilTest(u, () => site.Site is null || site.Market[material].Amount >= 1f, 6000));
        AssertNoGhosts(u);
    }

    [Fact]
    public void Abwerfen_SchuerferUndVerbandsfuehrer_KeinAbsturzKeineGeister()
    {
        var u = TestWorlds.Classic();
        new SimulationRunner().Run(u, 1);
        var alpha = u.Stations[new StationId(1)];
        var miner = Own(u, "prospector", alpha);
        Assert.True(Commands.Give(u, miner, new MineCommand(new AutoLimits(alpha.Sector, 1, 0f, 0f)), out _));
        var runner = new SimulationRunner();
        Assert.True(runner.RunUntilTest(u, () => miner.Cargo.Values.Sum() >= 5f, 6000));
        foreach (var g in miner.Cargo.Keys.ToList()) Assert.True(CargoJettison.Jettison(u, miner, g, 0f) > 0f);
        Assert.Empty(miner.Cargo);
        runner.Run(u, 2000);
        Assert.True(miner.Command is MineCommand);
        AssertNoGhosts(u);

        // Verbandsführer mit Fracht auf einem Flug: Abwerfen ändert den Befehl nicht
        var lead = Own(u, "nomad", alpha);
        var member = Own(u, "pip", alpha);
        var sq = Squadrons.Create(u, lead, "Test", out _)!;
        Assert.True(Squadrons.Join(u, sq, member, out _));
        lead.Cargo[DemoUniverse.Steel] = 3f;
        Assert.True(Squadrons.Command(u, sq, new GoToCommand(default, default, u.Stations[new StationId(5)].Id), out _));
        runner.Run(u, 5);
        Assert.True(CargoJettison.Jettison(u, lead, DemoUniverse.Steel, 0f) > 0f);
        Assert.True(runner.RunUntilTest(u, () => lead.Command is null, 4000));
        Assert.Equal(lead.Id, sq.Leader);
        AssertNoGhosts(u);
    }
}

static class RunnerTestExtensions
{
    /// <summary>Läuft Tick für Tick, bis die Bedingung gilt (höchstens <paramref name="max"/> Ticks).</summary>
    public static bool RunUntilTest(this SimulationRunner r, Universe u, Func<bool> done, int max)
    {
        for (int i = 0; i < max; i++) { if (done()) return true; r.Step(u); }
        return done();
    }
}
