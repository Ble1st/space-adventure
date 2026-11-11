using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 14: Planeten, Exportsenke an den Handelsstationen (Ankauf bezahlt sofort, Charge geht nach der Verzögerung mit Aufschlag an den Planeten),
/// Warenkategorien, Händlerregeln (nur beim Hersteller kaufen, nur Bezahlbares liefern), Schalter für Treibstoff und Forschung.
/// </summary>
public class ExportPlanetTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    static readonly StationId TradeBeta = new(30), TradeGamma = new(39), Colony = new(10);

    static GoodId G(Universe u, string key) => u.FindGood(key)!.Id;
    static Station St(Universe u, StationId id) => u.Stations[id];

    /// <summary>Ein Händler ohne laufende Reise und ohne Auftrag (nach dem ersten Tick sind alle unterwegs).</summary>
    static Ship IdleTrader(Universe u)
    {
        var t = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        if (t.Transit is not null) u.FinishTransit(t);
        t.Order = null; t.Phase = TradePhase.Idle; t.Cargo.Clear();
        return t;
    }

    static Universe World()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);   // Preise gesetzt
        return u;
    }

    // ───────── Inhalte und Validierung ─────────

    [Fact]
    public void Content_PlanetenInBetaUndGamma_ExportNurEndwaren()
    {
        var u = DemoUniverse.Create();
        Assert.Equal(new[] { "Korvan", "Thalassa" }, u.Sectors.Values.Where(s => s.Planet is not null).OrderBy(s => s.Id.Value).Select(s => s.Planet!.Name));
        Assert.Null(u.Sectors[new SectorId(1)].Planet);
        Assert.Null(u.Sectors[new SectorId(4)].Planet);
        // beide Handelsstationen stehen an einem Planeten und kaufen genau die Endwaren
        foreach (var id in new[] { TradeBeta, TradeGamma })
        {
            var st = St(u, id);
            Assert.NotNull(u.Sectors[st.Sector].Planet);
            var cfg = ExportRules.ConfigOf(u, st)!;
            Assert.Equal(new[] { "food", "consumer_goods", "luxury_goods" }, cfg.Goods.Select(g => u.Goods[g].Key));
            Assert.All(cfg.Goods, g => Assert.True(st.IsStockOnly(g)));   // reiner Ankauf: KI-Händler kaufen dort nicht ein
        }
        // keine Bausätze, Module, Erze, Zwischenprodukte, kein Treibstoff
        foreach (var key in new[] { "ship_kit_light", "ship_kit_heavy", "reactor_mk1", "iron_ore", "steel", "electronics", "life_support", "propellant" })
            Assert.False(ExportRules.Buys(u, St(u, TradeBeta), G(u, key)), key);
        Assert.Equal("end", u.Goods[DemoUniverse.Food].Category);
        Assert.Equal("module", u.FindGood("reactor_mk1")!.Category);
        Assert.Equal("kit", u.FindGood("ship_kit_light")!.Category);
        Assert.All(u.Goods.Values, g => Assert.NotEqual("", g.Category));
        // Wunschbestand nach Warenwert: Luxusgüter klein, Nahrung groß
        var gamma = St(u, TradeGamma);
        Assert.True(gamma.Market[G(u, "luxury_goods")].Target < gamma.Market[DemoUniverse.Food].Target);
    }

    [Fact]
    public void Validierung_ExportOhnePlanet_PlanetOhneName_UnbekannteKategorie_ExportwareImRezept()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Sectors.First(s => s.Id == 3).Planet = null;
        var e = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
        Assert.Contains("Planet", e.Message);
        Assert.Contains("Handelsstation Gamma", e.Message);

        def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Sectors.First(s => s.Id == 2).Planet!.Name = " ";
        Assert.Contains("Namen", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def)).Message);

        def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.StationTypes.First(t => t.Key == "trade_post").Export!.Categories = new() { "luxus" };
        Assert.Contains("Kategorie 'luxus'", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def)).Message);

        def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.StationTypes.First(t => t.Key == "trade_post").Export!.Goods = new() { "steel" };
        def.StationTypes.First(t => t.Key == "trade_post").Recipes.Add(new RecipeDef { Ticks = 5, In = new() { ["steel"] = 1 }, Out = new() });
        Assert.Contains("Rezept", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def)).Message);

        def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.StationTypes.First(t => t.Key == "trade_post").Export!.Goods = new() { "unobtainium" };
        Assert.Contains("unobtainium", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def)).Message);
    }

    // ───────── Ankauf und Export ─────────

    [Fact]
    public void Ankauf_BezahltSofort_ChargeGehtNachVerzoegerungMitAufschlagAnDenPlaneten()
    {
        var u = World();
        var st = St(u, TradeGamma);
        var player = u.Ships[u.PlayerShip];
        var lux = G(u, "luxury_goods");
        player.Cargo[lux] = 3f;
        float wallet = u.PlayerWallet.Credits, stationBefore = st.Credits, price = st.Market[lux].Price;
        Assert.True(price > 0f && price <= u.Goods[lux].BasePrice * u.Rules.ExportMaxBidFactor + 0.01f);   // Gebotsdeckel
        var queued = new List<ExportQueued>();
        var exported = new List<GoodsExported>();
        u.Events.Raised += e => { if (e is ExportQueued q) queued.Add(q); else if (e is GoodsExported x) exported.Add(x); };

        var r = new TradeService().Execute(u, st, player, new ServiceRequest($"sell:{lux.Value}", lux, 3f));
        Assert.True(r.Success);
        // sofort bezahlt (Spielerkasse ↑, Stationskasse ↓) und eine Charge mit genau dem bezahlten Betrag
        Assert.Equal(wallet + 3f * price, u.PlayerWallet.Credits, 2);
        Assert.Equal(stationBefore - 3f * price, st.Credits, 2);
        var batch = Assert.Single(st.Exports);
        Assert.Equal((lux, 3f), (batch.Good, batch.Amount));
        Assert.Equal(3f * price, batch.Paid, 2);
        Assert.Equal(u.Tick + u.Rules.ExportDelayTicks, batch.DueTick);
        Assert.Single(queued);
        Assert.Equal(3f, st.Market[lux].Amount, 3);   // wartende Charge zählt zum Bestand

        // Rückkauf ist nicht möglich (reiner Ankauf)
        Assert.Equal(BuyOutcome.NotSold, TradeActions.Buy(u, player, st, lux, 1f).Outcome);
        Assert.Equal(0f, TradeActions.MaxBuyable(u, player, st, lux));

        // kurz vor der Fälligkeit: noch nichts; danach: Ware weg, Station erhält bezahlt × 1,18
        var system = new ExportSystem();
        while (u.Tick < batch.DueTick - 1) { u.Tick++; system.Step(u, 1); }
        Assert.Single(st.Exports);
        float before = st.Credits;
        u.Tick++; system.Step(u, 1);
        Assert.Empty(st.Exports);
        var x = Assert.Single(exported);
        Assert.Equal(batch.Paid * 1.18f, x.Revenue, 2);
        Assert.Equal(batch.Paid * 1.18f, st.Credits - before, 2);
        Assert.Equal(0f, st.Market[lux].Amount, 3);
        Assert.Equal(3f, x.Amount, 3);
    }

    [Fact]
    public void Ankauf_KeineBausaetzeModuleZwischenwaren_KeineCharge()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var gamma = St(u, TradeGamma);
        var beta = St(u, TradeBeta);
        // Handelsposten Gamma führt als Exportwaren nur Endwaren: Bausatz und Modul werden dort gar nicht gehandelt
        foreach (var key in new[] { "ship_kit_light", "reactor_mk1" })
        {
            var g = G(u, key);
            player.Cargo.Clear();
            player.Cargo[g] = 1f;
            Assert.False(gamma.Market.ContainsKey(g), key);
            Assert.Equal(0f, TradeActions.Sell(u, player, gamma, g, 1f).Revenue);
        }
        // Phase 15: Stahl und Elektronik nimmt die Station als Instandhaltungsmaterial an (reiner Instandhaltungs-Eintrag) — aber das wird keine Exportcharge
        u.Rules.MaintenanceShare = 0.03f;
        new MaintenanceSystem().Step(u, 50);
        new MarketSystem().Step(u, 1);
        foreach (var key in new[] { "steel", "electronics" })
        {
            var g = G(u, key);
            Assert.True(Maintenance.IsPureEntry(u, gamma, g), key);
            Assert.False(ExportRules.Buys(u, gamma, g), key);
            gamma.Market[g].Amount = 0f;
            player.Cargo.Clear();
            player.Cargo[g] = 1f;
            TradeActions.Sell(u, player, gamma, g, 1f);
        }
        Assert.Empty(gamma.Exports);
        // Handelsstation Beta nimmt Bausatz und Modul in ihr Lager (wie bisher), aber das ist kein Export
        player.Cargo.Clear();
        var kit = G(u, "ship_kit_light");
        player.Cargo[kit] = 1f;
        beta.Market[kit].Amount = 0f;
        Assert.True(TradeActions.Sell(u, player, beta, kit, 1f).Revenue > 0f);
        Assert.Empty(beta.Exports);
    }

    [Fact]
    public void ExportAnzeige_Zeile_PlanetChargenNaechste()
    {
        var u = World();
        var st = St(u, TradeBeta);
        Assert.Equal("Export an Korvan: keine Charge unterwegs", ExportInfo.ForStation(u, st)!.Line);
        Assert.Null(ExportInfo.ForStation(u, St(u, Colony)));
        ExportRules.OnDelivered(u, st, DemoUniverse.Food, 10f, 50f);
        u.Tick += 120;   // 30 s später
        ExportRules.OnDelivered(u, st, DemoUniverse.Food, 5f, 20f);
        var view = ExportInfo.ForStation(u, st)!;
        Assert.Equal((2, 15f, 70f), (view.Batches, view.Units, view.Paid));
        Assert.Equal(u.Rules.ExportDelayTicks - 120, view.NextInTicks);
        Assert.Equal("Export an Korvan: 2 Chargen unterwegs, nächste in 4:30", view.Line);
        Assert.Equal("0:01", ExportInfo.Duration(1));
    }

    // ───────── Langlauf, Händler, Speichern ─────────

    [Fact]
    public void Langlauf_20000Ticks_EndwarenFliessen_KoloniePreisUnterDeckel_SenkeSolvent_HaendlerKaufenNurBeimHersteller()
    {
        var u = DemoUniverse.Create();
        int luxTrips = 0, cgTrips = 0, exportedBatches = 0, fromNonProducer = 0;
        double exportPaid = 0, exportRevenue = 0;
        var lux = G(u, "luxury_goods"); var cg = G(u, "consumer_goods");
        u.Events.Raised += e =>
        {
            switch (e)
            {
                case TradeCompleted t:
                    if (t.Good == lux) luxTrips++;
                    if (t.Good == cg) cgTrips++;
                    if (u.Stations.TryGetValue(t.From, out var from) && !from.Produces(t.Good)) fromNonProducer++;
                    break;
                case GoodsExported x: exportedBatches++; exportPaid += x.Paid; exportRevenue += x.Revenue; break;
            }
        };
        var runner = new SimulationRunner();
        var colonyLux = new List<float>();
        for (int i = 0; i < 20; i++)
        {
            runner.Run(u, 1000);
            colonyLux.Add(St(u, Colony).Market[lux].Price / u.Goods[lux].BasePrice);
        }
        Assert.True(luxTrips > 0, $"Luxusgüter-Fahrten {luxTrips}");
        Assert.True(cgTrips > 0);
        Assert.True(exportedBatches > 0);
        Assert.Equal(exportPaid * 1.18, exportRevenue, 0);
        Assert.Equal(0, fromNonProducer);   // Händler kaufen nur, wo die Ware hergestellt wird (nie an der Kolonie, nie aus Lagern)
        // die Kolonie bietet höchstens 2× (Endverbraucher-Deckel) und liegt nicht dauerhaft am alten 4×-Deckel
        Assert.All(colonyLux, r => Assert.True(r <= u.Rules.ConsumerMaxBidFactor + 0.001f));
        // die Handelsstationen bleiben zahlungsfähig und wachsen nicht ins Unendliche
        foreach (var id in new[] { TradeBeta, TradeGamma })
            Assert.InRange(St(u, id).Credits, 0f, 500_000f);
    }

    [Fact]
    public void SaveRoundtrip_MitOffenenChargenUndPlaneten_BitIdentisch_UndDeterministisch()
    {
        var a = DemoUniverse.Create(); var b = DemoUniverse.Create();
        new SimulationRunner().Run(a, 4000);
        new SimulationRunner().Run(b, 4000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        // eine sicher offene Charge (zusätzlich zu den laufenden)
        ExportRules.OnDelivered(a, St(a, TradeGamma), DemoUniverse.Food, 7f, 21f);
        Assert.NotEmpty(St(a, TradeGamma).Exports);
        var json = SaveGameJson.Write(a, Now);
        Assert.Contains("\"Version\":17", json);
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
        Assert.Equal("Thalassa", loaded.Sectors[new SectorId(3)].Planet!.Name);
        Assert.Equal(a.Sectors[new SectorId(3)].Planet!.Radius, loaded.Sectors[new SectorId(3)].Planet!.Radius);
        Assert.Equal(St(a, TradeGamma).Exports.Select(x => (x.Good, x.Amount, x.Paid, x.DueTick)), St(loaded, TradeGamma).Exports.Select(x => (x.Good, x.Amount, x.Paid, x.DueTick)));
        Assert.Equal(ExportRules.ConfigOf(a, St(a, TradeBeta))!.Goods, ExportRules.ConfigOf(loaded, St(loaded, TradeBeta))!.Goods);
        Assert.Equal(a.Goods.Values.Select(g => g.Category), loaded.Goods.Values.Select(g => g.Category));
        new SimulationRunner().Run(a, 3000);
        new SimulationRunner().Run(loaded, 3000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Haendler_KauftNurWasDerAbnehmerBezahlenKann()
    {
        var u = World();
        var col = St(u, Colony);
        var lux = G(u, "luxury_goods");
        var workshop = u.Stations.Values.First(s => s.Produces(lux));
        workshop.Market[lux].Amount = 50f;
        col.Market[lux].Amount = 0f;
        new MarketSystem().Step(u, 1);
        col.Credits = 2.5f * col.Market[lux].Price;   // reicht für 2 Luxusgüter
        var trader = IdleTrader(u);
        trader.Credits = 1_000_000f;
        u.MoveShip(trader, workshop.Sector);
        trader.Position = workshop.DockPoint(trader.Id);
        trader.Order = new TradeOrder(workshop.Id, col.Id, lux);
        trader.Phase = TradePhase.GoingToBuy;
        u.StartTransit(trader, new Transit { StartTick = u.Tick, ArrivalTick = u.Tick, ToSector = workshop.Sector, ToPosition = trader.Position,
            Legs = new() { new Leg(workshop.Sector, trader.Position, trader.Position, u.Tick, u.Tick) } });
        new TraderSystem().Step(u, 1);
        // Einkauf gedeckelt auf die bezahlbaren 2 Einheiten (keine unbezahlte Restladung mehr)
        Assert.Equal(2f, trader.Cargo.GetValueOrDefault(lux), 3);
    }

    // ───────── Schalter ─────────

    [Fact]
    public void TreibstoffAus_KeinVerbrauch_KeinTankdienst_KeineRaffinerie_NotrufZwecklos()
    {
        var u = World();
        Assert.False(u.Rules.FuelActive);
        Assert.False(u.Stations.ContainsKey(new StationId(25)));   // Treibstoffraffinerien fehlen
        Assert.False(u.Stations.ContainsKey(new StationId(26)));
        Assert.DoesNotContain(u.Stations.Values, s => s.TypeKey == "fuel_refinery");
        Assert.Equal(36 - 2, u.Rules.MaxStations);   // Bau-Spielraum bleibt gleich: −2 weggelassene Wirtschaftsstationen
        var reg = StationServices.Default();
        Assert.All(u.Stations.Values, s => Assert.False(reg.TryGet("refuel", out var r) && r.IsAvailable(u, s)));
        // Reise ohne Verbrauch, Spieler ohne Verbrauch und ohne Notantrieb, Notruf zwecklos, Planung ohne Tankstopp
        var trader = IdleTrader(u);
        float tank = trader.TotalFuel;
        Assert.True(tank > 0f);
        Assert.True(Travel.Start(u, trader, new SectorId(4), new Vec2(0, 0)));
        Assert.Equal(tank, trader.TotalFuel);
        var player = u.Ships[u.PlayerShip];
        PlayerFuel.ConsumeFlown(u, player, 1e9f);
        Assert.Equal(200f, player.TotalFuel);
        player.Fuel.Clear();
        Assert.Equal(1f, PlayerFuel.ThrottleCap(u, player));
        Assert.False(Refuel.PlayerEmergency(u, player).Success);
        Assert.Equal(Refuel.TripPlan.Depart, Refuel.PrepareTrip(u, player, new SectorId(4), new Vec2(0, 0)));
        Assert.Null(OrderSystem.EmergencyReason(u, player));
        // Langlauf: niemand verbraucht, niemand tankt
        int bought = 0;
        u.Events.Raised += e => { if (e is FuelBought or EmergencyRefuel) bought++; };
        var tanks = u.Ships.Values.ToDictionary(s => s.Id, s => s.TotalFuel);
        new SimulationRunner().Run(u, 3000);
        Assert.Equal(0, bought);
        Assert.All(u.Ships.Values.Where(s => tanks.ContainsKey(s.Id) && s.Id != u.PlayerShip), s => Assert.Equal(tanks[s.Id], s.TotalFuel, 3));
    }

    [Fact]
    public void TreibstoffAn_RaffinerienDa_VerbrauchUndTankdienst()
    {
        var u = TestWorlds.WithFuel();
        new SimulationRunner().Run(u, 1);
        Assert.True(u.Rules.FuelActive);
        Assert.True(u.Stations.ContainsKey(new StationId(25)) && u.Stations.ContainsKey(new StationId(26)));
        Assert.Equal(36, u.Rules.MaxStations);
        Assert.True(StationServices.Default().TryGet("refuel", out var refuel) && refuel.IsAvailable(u, u.Stations[new StationId(25)]));
        var trader = IdleTrader(u);
        float tank = trader.TotalFuel;
        Assert.True(Travel.Start(u, trader, new SectorId(4), new Vec2(0, 0)));
        Assert.True(trader.TotalFuel < tank);
    }

    [Fact]
    public void ForschungAus_KeineForschungsstation_AllesFrei_KeinBudget_DienstWeg()
    {
        var u = World();
        Assert.False(u.Rules.ResearchActive);
        Assert.False(u.Stations.ContainsKey(new StationId(37)));
        Assert.False(u.Stations.ContainsKey(new StationId(38)));
        Assert.DoesNotContain(u.Stations.Values, s => s.TypeKey == "research_station");
        Assert.True(u.Research.Count > 0);   // die Projekte stehen im Content, gelten aber nicht
        foreach (var f in u.Factions.Keys)
        {
            Assert.True(Research.IsUnlocked(u, f, UnlockKind.Module, "reactor_mk3"));
            Assert.True(Research.IsUnlocked(u, f, UnlockKind.Model, "excavator"));
        }
        var reg = StationServices.Default();
        Assert.All(u.Stations.Values, s => Assert.False(reg.TryGet("research", out var r) && r.IsAvailable(u, s)));
        // die Werft verkauft den Excavator (Material vorausgesetzt) ohne Forschung
        var yard = u.Stations[new StationId(18)];
        var q = ShipDealerService.Quote(u, yard, u.Ships[u.PlayerShip], "excavator");
        Assert.NotNull(q);
        Assert.Null(q!.Locked);
        int budget = 0, cycles = 0;
        u.Events.Raised += e => { if (e is TreasuryUpkeep { Purpose: "research" }) budget++; else if (e is ResearchCycle) cycles++; };
        new SimulationRunner().Run(u, 2000);
        Assert.Equal((0, 0), (budget, cycles));
        // Ids der übrigen Stationen bleiben wie in der Weltdatei (Hauptquartiere 32/34, Handelsstationen 30/39)
        Assert.Equal("headquarters", u.Stations[new StationId(32)].TypeKey);
        Assert.Equal("trade_post", u.Stations[TradeGamma].TypeKey);
    }

    [Fact]
    public void ForschungAn_ForschungsstationenDa_MoeduleGesperrt()
    {
        var u = TestWorlds.WithResearch();
        Assert.True(u.Stations.ContainsKey(new StationId(37)) && u.Stations.ContainsKey(new StationId(38)));
        Assert.False(Research.IsUnlocked(u, new FactionId(1), UnlockKind.Module, "reactor_mk3"));
        Assert.DoesNotContain(u.Stations.Values, s => s.TypeKey == "fuel_refinery");   // Treibstoff bleibt aus
    }
}
