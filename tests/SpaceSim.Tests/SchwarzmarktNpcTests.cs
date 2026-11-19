using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

/// <summary>
/// Schwarzmarkt NPC (docs/SCHWARZMARKT.md „NPCs beim Hehler“): gesetzlose KI-Händler verkaufen unverkäufliche Restfracht an den Hehler statt sie abzuwerfen,
/// gesetzestreue und freie Einzelschiffe nie; seltene Kaufrunde beim Hehler; Geld aus Ereignissen erklärbar; Schalter aus = Hash wie vorher; Speichern/Laden
/// mitten in der Hehlerfahrt bitgleich.
/// </summary>
public class SchwarzmarktNpcTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Welt mit einem Händler, dessen Besitzer die angegebene Gesetzestreue hat (null = freies Einzelschiff), Essen an Bord, das niemand annimmt.</summary>
    static (Universe u, Ship ship, Station fence, GoodId food) Setup(float? lawfulness)
    {
        var u = DemoUniverse.Create();
        u.Rules.SecurityActive = false;   // keine Überfälle unterwegs: der Ablauf soll ungestört sein
        u.Rules.StrandedCargoTicks = 0;   // Frist der Restfracht sofort abgelaufen
        var ship = u.ShipsById().First(s => s.Role == ShipRole.Trader && s.ModelKey == "unit_trader_100" && s.Transit is null && !s.IsPlayer);
        if (lawfulness is { } l)
        {
            ActorRules.Promote(u, ship);
            var a = u.Actors[ship.Owner];
            a.Personality = new Personality(0.5f, 1f, 0.5f, l);   // Gier 1: volle Reichweite
        }
        var food = u.FindGood("food")!.Id;
        // niemand nimmt Essen an: alle Lager weit über der Annahme
        foreach (var st in u.Stations.Values) if (st.Market.TryGetValue(food, out var e)) e.Amount = 1e6f;
        ship.Order = null; ship.Phase = TradePhase.Idle;
        ship.Cargo.Clear(); ship.Cargo[food] = 30f;
        ship.StrandedSince = u.Tick; ship.NextThinkTick = u.Tick;
        var fence = u.StationsById().First(s => BlackMarket.IsAt(u, s));
        foreach (var h in u.StationsById().Where(s => BlackMarket.IsAt(u, s))) BlackMarket.Fence(u, h)!.Treasury.Credits = 50_000f;
        return (u, ship, fence, food);
    }

    [Fact]
    public void Inhalt_StandardweltAn_CodeStandardAus()
    {
        Assert.True(DemoUniverse.Create().Rules.BlackMarketNpc);
        Assert.False(new EconomyRules().BlackMarketNpc);
    }

    [Fact]
    public void Gesetzlos_VerkauftRestAnHehler_StattAbwurf()
    {
        var (u, ship, _, food) = Setup(0.1f);
        float soldCr = 0f, soldUnits = 0f, upkeep = 0f; int discards = 0; StationId at = default;
        u.Events.Raised += e =>
        {
            if (e is BlackMarketNpcTrade t && t.Ship == ship.Id && t.Sold) { soldCr += t.Credits; soldUnits += t.Units; at = t.Station; }
            if (e is TreasuryUpkeep { Purpose: "blackmarket" } tu) upkeep += tu.Amount;
            if (e is CargoDiscarded cd && cd.Ship == ship.Id) discards++;
        };
        new TraderSystem().Step(u, 1);
        Assert.IsType<TradeOrder>(ship.Order);
        var fence = u.Stations[((TradeOrder)ship.Order!).Sell];
        Assert.True(BlackMarket.IsAt(u, fence));
        float stockBefore = fence.BlackStock.GetValueOrDefault(food);
        var runner = new SimulationRunner();
        for (int i = 0; i < 6000 && soldUnits <= 0f; i++) runner.Run(u, 1);
        Assert.Equal(0, discards);
        Assert.Equal(fence.Id, at);
        Assert.Equal(30f, soldUnits, 2);
        Assert.Equal(0f, ship.Cargo.GetValueOrDefault(food), 3);
        Assert.Equal(soldCr, upkeep, 2);   // Geld aus Ereignissen erklärbar: was das Schiff bekam, zahlte die Piratenkasse
        Assert.Equal(30f * BlackMarket.BuyPrice(u, food), soldCr, 0);
        Assert.True(fence.BlackStock.GetValueOrDefault(food) >= MathF.Min(u.Rules.BlackMarketStockMax, stockBefore + 30f) - 0.01f);
    }

    [Theory]
    [InlineData(0.9f)]   // gesetzestreu
    [InlineData(null)]   // freies Einzelschiff (kein eigener Charakter)
    public void Gesetzestreu_OderFrei_WirftAbWieBisher(float? lawfulness)
    {
        var (u, ship, _, food) = Setup(lawfulness);
        int discards = 0, trades = 0;
        u.Events.Raised += e => { if (e is CargoDiscarded cd && cd.Ship == ship.Id) discards++; if (e is BlackMarketNpcTrade) trades++; };
        new TraderSystem().Step(u, 1);
        Assert.Equal(1, discards);
        Assert.Equal(0, trades);
        Assert.Equal(0f, ship.Cargo.GetValueOrDefault(food));
    }

    [Fact]
    public void SchalterAus_WirftAb()
    {
        var (u, ship, _, _) = Setup(0.1f);
        u.Rules.BlackMarketNpc = false;
        int discards = 0;
        u.Events.Raised += e => { if (e is CargoDiscarded cd && cd.Ship == ship.Id) discards++; };
        new TraderSystem().Step(u, 1);
        Assert.Equal(1, discards);
    }

    [Fact]
    public void Verkauf_GeldErklaerbar_UndHehlerkasseNurUeberRuecklage()
    {
        var (u, ship, fence, food) = Setup(0.1f);
        var pirates = BlackMarket.Fence(u, fence)!;
        pirates.Treasury.Credits = u.Rules.BlackMarketReserve + 10f * BlackMarket.BuyPrice(u, food);   // reicht für 10 Einheiten
        float shipBefore = ship.Credits, treasuryBefore = pirates.Treasury.Credits;
        float pay = BlackMarketNpc.SellAll(u, fence, ship);
        Assert.Equal(10f, 30f - ship.Cargo[food], 2);
        Assert.Equal(pay, ship.Credits - shipBefore, 2);
        Assert.Equal(-pay, pirates.Treasury.Credits - treasuryBefore, 2);
        Assert.Equal(u.Rules.BlackMarketReserve, pirates.Treasury.Credits, 1);
    }

    [Fact]
    public void Kaufrunde_HehlerwareFuerEhrlichenAbnehmer_NurMitGewinn()
    {
        var (u, ship, _, food) = Setup(0.1f);
        ship.Cargo.Clear(); ship.Credits = 20_000f;
        var fence = BlackMarketNpc.NearestFence(u, ship, u.Actors[ship.Owner])!;
        fence.BlackStock.Clear();
        fence.BlackStock[food] = 20f;
        float fencePrice = BlackMarket.SellPrice(u, food);
        // ein Abnehmer zahlt deutlich mehr als der Hehlerpreis und nimmt an
        var dest = u.StationsById().First(s => s.Market.TryGetValue(food, out _) && s.Consumes(food) && !s.IsBuildSite);
        var e = dest.Market[food];
        e.Amount = 0f; e.Price = fencePrice * 2f; dest.Credits = 100_000f;
        var cap = BlackMarket.Fence(u, fence)!;
        cap.Treasury.Credits = u.Rules.PirateTreasuryCap;   // Piratenkasse am Deckel: keine Kaufrunde (die Kasse soll nicht über den Deckel wachsen)
        Assert.True(u.Rules.PirateTreasuryCap > 0f);
        Assert.False(BlackMarketNpc.TryBuyRound(u, ship));
        cap.Treasury.Credits = 3000f;
        Assert.True(BlackMarketNpc.TryBuyRound(u, ship));
        var order = Assert.IsType<TradeOrder>(ship.Order);
        Assert.Equal(fence.Id, order.Buy);
        Assert.Equal(food, order.Good);
        // Ankunft: kaufen, aus dem Lager, Geld in die Piratenkasse
        var pirates = BlackMarket.Fence(u, fence)!;
        float tBefore = pirates.Treasury.Credits, sBefore = ship.Credits;
        u.Stations[order.Sell].Market[food].Price = fencePrice * 2f;
        Assert.True(BlackMarketNpc.OnTraderArrive(u, ship, order));
        float bought = ship.Cargo.GetValueOrDefault(food);
        Assert.True(bought >= 1f);
        Assert.Equal(bought * fencePrice, pirates.Treasury.Credits - tBefore, 1);
        Assert.Equal(-bought * fencePrice, ship.Credits - sBefore, 1);
        Assert.Equal(20f - bought, fence.BlackStock.GetValueOrDefault(food), 2);

        // ohne Spanne keine Runde
        ship.Order = null; ship.Cargo.Clear(); ship.Phase = TradePhase.Idle;
        foreach (var st in u.Stations.Values) if (st.Market.TryGetValue(food, out var x)) x.Price = fencePrice * 0.9f;
        Assert.False(BlackMarketNpc.TryBuyRound(u, ship));
    }

    [Fact]
    public void SpeichernLaden_MittenInDerHehlerfahrt_Bitgleich()
    {
        var (u, ship, _, _) = Setup(0.1f);
        new TraderSystem().Step(u, 1);
        Assert.NotNull(ship.Transit);
        var copy = SaveGameJson.Read(SaveGameJson.Write(u, Now)).Universe;
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        new SimulationRunner().Run(u, 800);
        new SimulationRunner().Run(copy, 800);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
    }

    /// <summary>
    /// Schalter aus = Standardwelt bitgleich zum Stand vor dieser Regel (Balance-Hash 20000 Ticks F5F1965838C14B59, ohne Spielerpolitik 4A53E782E7FA5B2D).
    /// </summary>
    [Fact]
    public void SchalterAus_StandardweltHashWieVorher_20000Ticks()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        GasNebulaTests.RevertGas(def);   // Gasernte und Nebel kamen danach
        TestWorlds.RevertFuelResearchOn(def);   // die Referenz stammt von vor „Treibstoff und Forschung dauerhaft an“ (beide Schalter aus, vor dem Weltaufbau)
        def.Rules!.BlackMarketNpc = false;
        var u = WorldBuilder.Build(def);
        new SimulationRunner().Run(u, 20000);
        Assert.Equal(0xF5F1965838C14B59UL, UniverseHash.Compute(u));
    }
}
