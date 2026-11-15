using SpaceSim.Core.Demo;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

public class ServiceRegistryTests
{
    sealed class Stub : IStationService
    {
        public Stub(string key) => Key = key;
        public string Key { get; }
        public bool IsAvailable(Universe u, Station station) => true;
        public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship) => Array.Empty<ServiceOffer>();
        public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request) => new(true, 0f, 0f, "");
    }

    [Fact]
    public void Register_TryGet_KeysInRegistrationOrder()
    {
        var r = new StationServices();
        Assert.Empty(r.Keys);
        Assert.False(r.TryGet("alpha", out _));

        var a = new Stub("alpha");
        var b = new Stub("beta");
        r.Register(a);
        r.Register(b);

        Assert.True(r.TryGet("alpha", out var fa));
        Assert.Same(a, fa);
        Assert.True(r.TryGet("beta", out var fb));
        Assert.Same(b, fb);
        Assert.False(r.TryGet("gamma", out _));
        Assert.Equal(new[] { "alpha", "beta" }, r.Keys);   // Registrierungsreihenfolge, keine Hash-Reihenfolge
    }

    [Fact]
    public void Register_DuplicateKey_Throws_AndKeepsTheFirst()
    {
        var r = new StationServices();
        r.Register(new Stub("trade"));
        Assert.Throws<ArgumentException>(() => r.Register(new Stub("trade")));
        Assert.Single(r.Keys);
        Assert.True(r.TryGet("trade", out var s));
        Assert.Equal("trade", s.Key);
    }

    [Fact]
    public void Default_RegistersAllServicesInFixedOrder()   // Phase 4c: trade, refuel, repair; Phase 5c: outfitter, shipdealer; Phase 7: upgrade; Phase 8: storage; Phase 11: court, bounty
    {
        var r = StationServices.Default();
        Assert.Equal(new[] { TradeService.ServiceKey, RefuelService.ServiceKey, RepairService.ServiceKey, OutfitterService.ServiceKey, ShipDealerService.ServiceKey, UpgradeService.ServiceKey, StorageService.ServiceKey, CourtService.ServiceKey, BountyService.ServiceKey, HangarService.ServiceKey, TaxiService.ServiceKey, RentalService.ServiceKey, ResearchService.ServiceKey, RearmService.ServiceKey, MissionService.ServiceKey }, r.Keys);   // Phase 12: hangar, taxi, rental; Phase 13: research; Phase 25: rearm; Runde 41: missions
        Assert.IsType<TradeService>(r.TryGet(TradeService.ServiceKey, out var a) ? a : null);
        Assert.IsType<RefuelService>(r.TryGet(RefuelService.ServiceKey, out var b) ? b : null);
        Assert.IsType<RepairService>(r.TryGet(RepairService.ServiceKey, out var c) ? c : null);
    }
}

public class TradeServiceTests
{
    public enum BuyFailure { NoCredits, NoStock, NoSpace }

    /// <summary>Identisch erzeugte Welt (DemoUniverse.Create() ist deterministisch) mit einem handelbaren Gut.</summary>
    static (Universe u, Ship ship, Station st, GoodId good) Setup()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);   // MarketSystem setzt die Preise
        var ship = u.Ships[u.PlayerShip];
        var st = u.Stations.Values.First(s => s.Market.Values.Any(m => m.Amount >= 20f && m.Price > 0f));
        var good = st.Market.First(kv => kv.Value.Amount >= 20f && kv.Value.Price > 0f).Key;
        ship.Cargo.Clear();
        u.WalletOf(ship).Credits = 1_000_000f;
        return (u, ship, st, good);
    }

    /// <summary>Identisch erzeugte Welt mit der Erzmine (kauft Eisenerz).</summary>
    static (Universe u, Ship ship, Station mine) SellSetup()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        var mine = u.Stations[new StationId(1)];
        Assert.True(mine.Market[DemoUniverse.Ore].Price > 0f);
        return (u, u.Ships[u.PlayerShip], mine);
    }

    static IStationService Trade()
    {
        var services = StationServices.Default();
        Assert.True(services.TryGet(TradeService.ServiceKey, out var svc));
        return svc;
    }

    /// <summary>Zeichnet alle Events der Welt auf.</summary>
    static List<SimEvent> Record(Universe u)
    {
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        return events;
    }

    /// <summary>Zustand, den ein fehlgeschlagenes Angebot nicht verändern darf.</summary>
    static (float Credits, Dictionary<GoodId, float> Cargo, float StationCredits, Dictionary<GoodId, float> Stock) Snapshot(Universe u, Ship ship, Station st) =>
        (u.WalletOf(ship).Credits, new Dictionary<GoodId, float>(ship.Cargo), st.Credits, st.Market.ToDictionary(kv => kv.Key, kv => kv.Value.Amount));

    static void AssertUnchanged((float Credits, Dictionary<GoodId, float> Cargo, float StationCredits, Dictionary<GoodId, float> Stock) before, Universe u, Ship ship, Station st)
    {
        Assert.Equal(before.Credits, u.WalletOf(ship).Credits, 3);
        Assert.Equal(before.Cargo.Count, ship.Cargo.Count);
        foreach (var (g, v) in before.Cargo) Assert.Equal(v, ship.Cargo[g], 3);
        Assert.Equal(before.StationCredits, st.Credits, 3);
        Assert.Equal(before.Stock.Count, st.Market.Count);
        foreach (var (g, v) in before.Stock) Assert.Equal(v, st.Market[g].Amount, 3);
    }

    /// <summary>Vergleicht die gesamte betroffenen Weltlage: Credits, Fracht und Stationslager.</summary>
    static void AssertSameState(Ship a, Station sa, Ship b, Station sb)
    {
        Assert.Equal(a.Credits, b.Credits, 3);
        Assert.Equal(a.Cargo.Count, b.Cargo.Count);
        foreach (var (g, v) in a.Cargo) Assert.Equal(v, b.Cargo[g], 3);
        Assert.Equal(sa.Credits, sb.Credits, 3);
        Assert.Equal(sa.Market.Count, sb.Market.Count);
        foreach (var (g, e) in sa.Market) Assert.Equal(e.Amount, sb.Market[g].Amount, 3);
    }

    [Fact]
    public void Trade_Offers_ListBuyAndSellPerMarketGood_PlusSellAll_InStableOrder()
    {
        var (u, ship, st, good) = Setup();
        var svc = Trade();
        Assert.True(svc.IsAvailable(u, st));

        var offers = svc.Offers(u, st, ship);

        var expected = st.Market.Where(kv => kv.Value.Price > 0f).OrderBy(kv => kv.Key.Value)
            .SelectMany(kv => new[] { $"{TradeService.Buy}:{kv.Key.Value}", $"{TradeService.Sell}:{kv.Key.Value}" })
            .Append(TradeService.SellAll).Append(TradeService.Undo).ToList();   // Runde 39: „undo“ am Ende
        Assert.Equal(expected, offers.Select(o => o.Key).ToList());
        Assert.Contains(offers, o => o.Key == $"{TradeService.Buy}:{good.Value}" && o.Price > 0f && o.Amount >= 20f);
        Assert.Contains(offers, o => o.Key == $"{TradeService.Sell}:{good.Value}");
    }

    [Fact]
    public void Trade_Offers_CarryGoodStockAndHeld_AndAmountsMatchTheLimits()
    {
        var (u, ship, st, good) = Setup();
        ship.Cargo[good] = 7f;
        var svc = Trade();
        var entry = st.Market[good];

        var offers = svc.Offers(u, st, ship);
        var buy = offers.Single(o => o.Key == $"{TradeService.Buy}:{good.Value}");
        var sell = offers.Single(o => o.Key == $"{TradeService.Sell}:{good.Value}");

        Assert.Equal(good, buy.Good);
        Assert.Equal(good, sell.Good);
        Assert.Equal(entry.Amount, buy.Stock, 3);
        Assert.Equal(entry.Amount, sell.Stock, 3);
        Assert.Equal(7f, buy.Held, 3);
        Assert.Equal(7f, sell.Held, 3);
        Assert.Equal(entry.Price, buy.Price, 3);
        Assert.Equal(entry.Price, sell.Price, 3);
        Assert.Equal(TradeActions.MaxBuyable(u, ship, st, good), buy.Amount, 3);
        // Phase 17: Angebotsmenge nach der Mengenregel des Verkaufs (alles, wenn die Station alles nimmt, sonst ganze Einheiten)
        Assert.Equal(TradeActions.SaleUnits(7f, 7f, MathF.Min(st.Credits / entry.Price, st.AcceptableUnits(good, u.Rules))), sell.Amount, 3);

        var all = offers.Single(o => o.Key == TradeService.SellAll);
        Assert.Equal(default(GoodId), all.Good);
        Assert.Equal(0f, all.Stock);
        Assert.Equal(0f, all.Held);
        Assert.Equal(TradeActions.Quote(u, ship, st), all.Price, 3);
        Assert.Equal(TradeActions.Sellable(u, ship, st).Units, all.Amount, 3);   // Phase 17: was „Alles verkaufen“ bewegt
    }

    [Fact]
    public void Trade_SellOffer_IsLimitedByStationCredits()
    {
        var (u, ship, st, good) = Setup();
        ship.Cargo[good] = 50f;
        float price = st.Market[good].Price;
        st.Credits = price * 2.5f;   // reicht nur für zweieinhalb Einheiten
        var svc = Trade();

        var sell = svc.Offers(u, st, ship).Single(o => o.Key == $"{TradeService.Sell}:{good.Value}");

        Assert.True(price > 0f);
        // Phase 17: nur ganze Einheiten, wenn die Station nicht alles nimmt (vorher 2,5 — der Verkauf bewegte den Bruchteil, die Anzeige nicht)
        Assert.Equal(2f, sell.Amount, 3);
        Assert.Equal(price * 2f, sell.Price * sell.Amount, 3);
    }

    [Fact]
    public void Trade_SellOffer_IsLimitedByAcceptableUnits()
    {
        var (u, ship, st, good) = Setup();
        ship.Cargo[good] = 50f;
        st.Credits = 1_000_000f;
        st.Market[good].Amount = st.Market[good].Target * u.Rules.OverstockFactor - 10f;   // Lager fast voll
        var svc = Trade();

        var sell = svc.Offers(u, st, ship).Single(o => o.Key == $"{TradeService.Sell}:{good.Value}");

        Assert.Equal(10f, st.AcceptableUnits(good, u.Rules), 3);
        Assert.Equal(10f, sell.Amount, 3);
    }

    [Fact]
    public void Trade_Buy_ViaService_IsIdenticalToTradeActions_AndPublishesServicePerformed()
    {
        var (ua, shipA, stA, good) = Setup();
        var (ub, shipB, stB, _) = Setup();
        var svc = Trade();
        var events = new List<ServicePerformed>();
        ub.Events.Raised += e => { if (e is ServicePerformed sp) events.Add(sp); };

        var direct = TradeActions.Buy(ua, shipA, stA, good, 10f);
        var viaService = svc.Execute(ub, stB, shipB, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));

        Assert.True(viaService.Success);
        Assert.Equal(direct.Units, viaService.Units, 3);
        Assert.Equal(-direct.Cost, viaService.Credits, 3);
        AssertSameState(shipA, stA, shipB, stB);

        var sp = Assert.Single(events);
        Assert.Equal(shipB.Id, sp.Ship);
        Assert.Equal(stB.Id, sp.Station);
        Assert.Equal(TradeService.ServiceKey, sp.Service);
        Assert.Equal(TradeService.Buy, sp.Offer);
        Assert.Equal(good, sp.Good);
        Assert.Equal(direct.Units, sp.Units, 3);
        Assert.Equal(-direct.Cost, sp.Credits, 3);
    }

    [Fact]
    public void Trade_Sell_ViaService_IsIdenticalToTradeActions_AndPublishesServicePerformed()
    {
        var (ua, shipA, mineA) = SellSetup();
        var (ub, shipB, mineB) = SellSetup();
        shipA.Cargo[DemoUniverse.Ore] = 50f;
        shipB.Cargo[DemoUniverse.Ore] = 50f;
        var svc = Trade();
        var events = new List<ServicePerformed>();
        ub.Events.Raised += e => { if (e is ServicePerformed sp) events.Add(sp); };

        var direct = TradeActions.Sell(ua, shipA, mineA, DemoUniverse.Ore, 10f);
        var viaService = svc.Execute(ub, mineB, shipB, new ServiceRequest($"{TradeService.Sell}:{DemoUniverse.Ore.Value}", DemoUniverse.Ore, 10f));

        Assert.True(viaService.Success);
        Assert.Equal(direct.Sold[DemoUniverse.Ore], viaService.Units, 3);
        Assert.Equal(direct.Revenue, viaService.Credits, 3);
        AssertSameState(shipA, mineA, shipB, mineB);

        var sp = Assert.Single(events);
        Assert.Equal(TradeService.Sell, sp.Offer);
        Assert.Equal(DemoUniverse.Ore, sp.Good);
        Assert.Equal(direct.Sold[DemoUniverse.Ore], sp.Units, 3);
        Assert.Equal(direct.Revenue, sp.Credits, 3);
    }

    [Fact]
    public void Trade_SellAll_ViaService_IsIdenticalToTradeActions_AndPublishesServicePerformed()
    {
        var (ua, shipA, mineA) = SellSetup();
        var (ub, shipB, mineB) = SellSetup();
        shipA.Cargo[DemoUniverse.Ore] = 50f;
        shipA.Cargo[DemoUniverse.Ice] = 10f;   // wird von der Mine nicht angekauft und bleibt an Bord
        shipB.Cargo[DemoUniverse.Ore] = 50f;
        shipB.Cargo[DemoUniverse.Ice] = 10f;
        var svc = Trade();
        var events = new List<ServicePerformed>();
        ub.Events.Raised += e => { if (e is ServicePerformed sp) events.Add(sp); };

        var direct = TradeActions.SellAll(ua, shipA, mineA);
        var viaService = svc.Execute(ub, mineB, shipB, new ServiceRequest(TradeService.SellAll, default, 0f));

        Assert.True(viaService.Success);
        Assert.Equal(direct.Sold.Values.Sum(), viaService.Units, 3);
        Assert.Equal(direct.Revenue, viaService.Credits, 3);
        Assert.Equal(direct.Unsold, shipB.Cargo.Values.Sum(), 3);
        AssertSameState(shipA, mineA, shipB, mineB);

        var sp = Assert.Single(events);
        Assert.Equal(TradeService.SellAll, sp.Offer);
        Assert.Equal(default(GoodId), sp.Good);
        Assert.Equal(direct.Sold.Values.Sum(), sp.Units, 3);
        Assert.Equal(direct.Revenue, sp.Credits, 3);
    }

    [Fact]
    public void Trade_SuccessfulSell_PublishesOneServicePerformedAndPlayerSold()
    {
        var (u, ship, mine) = SellSetup();
        ship.Cargo[DemoUniverse.Ore] = 20f;
        var svc = Trade();
        var events = Record(u);

        var r = svc.Execute(u, mine, ship, new ServiceRequest($"{TradeService.Sell}:{DemoUniverse.Ore.Value}", DemoUniverse.Ore, 0f));

        Assert.True(r.Success);
        var sp = Assert.Single(events.OfType<ServicePerformed>());
        Assert.Equal(TradeService.Sell, sp.Offer);
        Assert.Equal(DemoUniverse.Ore, sp.Good);
        Assert.Equal(r.Units, sp.Units, 3);
        Assert.Equal(r.Credits, sp.Credits, 3);

        var sold = Assert.Single(events.OfType<PlayerSold>());
        Assert.Equal(DemoUniverse.Ore, sold.Good);
        Assert.Equal(r.Units, sold.Units, 3);
        Assert.Equal(r.Credits, sold.Revenue, 3);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Trade_UnknownOffer_FailsWithoutSideEffects()
    {
        var (u, ship, st, good) = Setup();
        float credits = u.WalletOf(ship).Credits, stCredits = st.Credits;
        var svc = Trade();

        var r = svc.Execute(u, st, ship, new ServiceRequest("gibts_nicht", good, 1f));

        Assert.False(r.Success);
        Assert.Equal(credits, u.WalletOf(ship).Credits, 3);
        Assert.Equal(stCredits, st.Credits, 3);
        Assert.Empty(ship.Cargo);
    }

    [Theory]
    [InlineData("buy")]
    [InlineData("buy:abc")]
    [InlineData("buy:")]
    [InlineData("sell_all:3")]
    [InlineData("foo:1")]
    [InlineData("")]
    public void Trade_MalformedOffer_FailsWithoutEventOrStateChange(string key)
    {
        var (u, ship, st, good) = Setup();
        ship.Cargo[good] = 5f;   // ein Verkauf hätte etwas zu tun
        var svc = Trade();
        var before = Snapshot(u, ship, st);
        var events = Record(u);

        var r = svc.Execute(u, st, ship, new ServiceRequest(key, good, 0f));

        Assert.False(r.Success);
        Assert.False(string.IsNullOrWhiteSpace(r.Message));
        Assert.Equal(0f, r.Units);
        Assert.Equal(0f, r.Credits);
        Assert.Empty(events);
        AssertUnchanged(before, u, ship, st);
    }

    [Fact]
    public void Trade_BuyKeyDecidesTheGood_RequestGoodIsIgnored()
    {
        var (ua, shipA, stA, good) = Setup();
        var (ub, shipB, stB, _) = Setup();
        var other = ua.Goods.Keys.First(g => g != good);
        var svc = Trade();

        var expected = svc.Execute(ua, stA, shipA, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 5f));
        var actual = svc.Execute(ub, stB, shipB, new ServiceRequest($"{TradeService.Buy}:{good.Value}", other, 5f));

        Assert.True(expected.Success);
        Assert.True(actual.Success);
        Assert.Equal(expected.Units, actual.Units, 3);
        Assert.Equal(expected.Credits, actual.Credits, 3);
        Assert.Equal(5f, shipB.Cargo.GetValueOrDefault(good), 3);
        Assert.False(shipB.Cargo.ContainsKey(other));
        AssertSameState(shipA, stA, shipB, stB);
    }

    [Theory]
    [InlineData(BuyFailure.NoCredits)]
    [InlineData(BuyFailure.NoStock)]
    [InlineData(BuyFailure.NoSpace)]
    public void Trade_BuyFailures_ReturnFailureWithoutEventOrStateChange(BuyFailure failure)
    {
        var (u, ship, st, good) = Setup();
        string expectedMessage;
        switch (failure)
        {
            case BuyFailure.NoCredits:
                u.WalletOf(ship).Credits = 0f;
                expectedMessage = "Nicht genügend Credits";
                break;
            case BuyFailure.NoStock:
                st.Market[good].Amount = 0f;
                expectedMessage = "Nicht genügend auf Lager";
                break;
            default:
                ship.Cargo[u.Goods.Keys.First(g => g != good)] = 100f;   // Laderaum voll
                expectedMessage = "Laderaum voll";
                break;
        }
        var svc = Trade();
        var before = Snapshot(u, ship, st);
        var events = Record(u);

        var r = svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 0f));

        Assert.False(r.Success);
        Assert.Equal(expectedMessage, r.Message);
        Assert.Equal(0f, r.Units);
        Assert.Equal(0f, r.Credits);
        Assert.Empty(events);
        AssertUnchanged(before, u, ship, st);
    }

    [Fact]
    public void Trade_SellFailures_NameTheReason_WithoutEventOrStateChange()
    {
        var (u, ship, st, good) = Setup();
        var svc = Trade();
        string key = $"{TradeService.Sell}:{good.Value}";

        // nichts an Bord
        var events = Record(u);
        var r = svc.Execute(u, st, ship, new ServiceRequest(key, default, 0f));
        Assert.False(r.Success);
        Assert.Equal("Nichts an Bord", r.Message);

        // Station zahlt nicht (Kasse leer)
        ship.Cargo[good] = 10f;
        st.Credits = 0f;
        var before = Snapshot(u, ship, st);
        r = svc.Execute(u, st, ship, new ServiceRequest(key, default, 0f));
        Assert.False(r.Success);
        Assert.Equal("Station kann nicht zahlen", r.Message);   // Phase 16: derselbe Grund wie im Angebot (MarketInfo.Acceptance)
        AssertUnchanged(before, u, ship, st);

        // Ware wird hier nicht geführt
        var foreign = u.Goods.Keys.First(g => !st.Market.ContainsKey(g));
        ship.Cargo[foreign] = 5f;
        r = svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Sell}:{foreign.Value}", default, 0f));
        Assert.False(r.Success);
        Assert.Equal("Handelt diese Ware nicht", r.Message);   // Phase 16: Grund aus MarketInfo.Acceptance
        Assert.Empty(events);
    }

    [Fact]
    public void Trade_UnavailableService_FailsWithoutEventOrStateChange()
    {
        var (u, ship, st, good) = Setup();
        st.Market.Clear();   // kein Markt
        var svc = Trade();
        Assert.False(svc.IsAvailable(u, st));
        var before = Snapshot(u, ship, st);
        var events = Record(u);

        var r = svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 1f));

        Assert.False(r.Success);
        Assert.False(string.IsNullOrWhiteSpace(r.Message));
        Assert.Empty(events);
        AssertUnchanged(before, u, ship, st);
    }

    [Fact]
    public void Trade_ZeroUnits_BuysMaximumAndSellsWholeCargo()
    {
        var (u, ship, st, good) = Setup();
        var svc = Trade();
        float max = TradeActions.MaxBuyable(u, ship, st, good);
        Assert.True(max >= 1f);

        var buy = svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", default, 0f));

        Assert.True(buy.Success);
        Assert.Equal(max, buy.Units, 3);
        Assert.Equal(max, ship.Cargo.GetValueOrDefault(good), 3);

        var (u2, ship2, mine) = SellSetup();
        ship2.Cargo[DemoUniverse.Ore] = 40f;
        Assert.True(mine.Credits / mine.Market[DemoUniverse.Ore].Price >= 40f);          // Kasse reicht
        Assert.True(mine.AcceptableUnits(DemoUniverse.Ore, u2.Rules) >= 40f);            // Lager nimmt alles

        var sell = svc.Execute(u2, mine, ship2, new ServiceRequest($"{TradeService.Sell}:{DemoUniverse.Ore.Value}", default, 0f));

        Assert.True(sell.Success);
        Assert.Equal(40f, sell.Units, 3);
        Assert.False(ship2.Cargo.ContainsKey(DemoUniverse.Ore));
    }
}
