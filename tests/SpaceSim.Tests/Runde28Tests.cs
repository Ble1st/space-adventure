using SpaceSim.Core.Content;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 28: der „Grün gebrochen“-Fall des Geräts (Konsumgüter Tethys → Kolonie Gamma) im Core nachgestellt.</summary>
public class Runde28Tests
{
    /// <summary>
    /// Gerät: vor dem Kauf „jetzt 155,3 · zuletzt 121,6–155,3 · GRÜN mind. +223“, direkt danach „jetzt 157,0 · zuletzt 74,0–157,0“, bei Ankunft 84,7–86,3.
    /// Ursache: ein NPC-Händler hatte eben 100 Konsumgüter für die Kolonie geladen — Bestand 89 (Preis 140 × 100 ÷ 89 = 157) plus 100 unterwegs ergibt
    /// 140 × 100 ÷ 189 = 74. Der untere Wert der Zeile war der schlechte Fall (mit dieser Lieferung), nicht der Verlauf. Mit der Lieferung unterwegs ist die
    /// Fahrt nicht grün; ohne sie (vor dem Entschluss des Händlers) kann keine Auskunft sie kennen — dann entscheidet der Verlauf.
    /// </summary>
    [Fact]
    public void Kolonie_LieferungAndererHaendler_NichtGruen()
    {
        var u = WorldBuilder.BuildDefault(11, "trader");
        var run = new SimulationRunner();
        var mem = new PriceMemory();
        for (int i = 0; i < 1300; i++) { run.Step(u); mem.Observe(u); }
        var cg = u.Goods.Values.First(g => g.Key == "consumer_goods");
        var gamma = u.StationsById().First(s => s.TypeKey == "colony" && s.Sector.Value == 3);
        var tethys = u.StationsById().First(s => s.TypeKey == "colony" && s.Id != gamma.Id);
        var e = gamma.Market[cg.Id];
        e.Amount = 40f; e.Target = 100f; e.Price = MarketSystem.StockPrice(cg, e, e.Amount);   // Annahme bis zum Ziel — Platz für die 100 der NPC und 10 des Spielers
        gamma.Credits = 1_000_000f;
        var p = u.Ships[u.PlayerShip];
        var from = tethys.DockPoint(p.Id);
        // ein NPC-Händler bringt 40 Einheiten (die Annahme der Kolonie reicht hier bis zum Ziel)
        var npc = u.ShipsById().First(s => s.IsEconomyAi && s.Role == ShipRole.Trader);
        npc.Cargo.Clear(); npc.Cargo[cg.Id] = 40f; npc.Order = new TradeOrder(tethys.Id, gamma.Id, cg.Id); npc.Phase = TradePhase.GoingToSell;
        var acc = MarketInfo.Acceptance(u, gamma, cg.Id);
        Assert.True(acc.Accepts, $"{acc.Reason} {acc.Units} acc {gamma.AcceptableUnits(cg.Id, u.Rules)} route {u.FindRoute(tethys.Sector, gamma.Sector)?.Count}");
        var q = MarketInfo.QuoteBuyer(u, gamma, cg.Id, 10f, tethys.Sector, from, p.Id, mem)!.Value;
        Assert.True(q.Incoming >= 40f);   // (auch andere Händler sind schon unterwegs)
        Assert.Equal(MarketSystem.StockPrice(cg, e, e.Amount + q.Incoming), q.PriceArrival, 1);
        Assert.True(q.PriceArrival < 0.55f * e.Price);
        Assert.True(q.Price <= q.PriceArrival + 0.001f);
        Assert.NotEqual(TripVerdict.Good, MarketInfo.Verdict(q, 0.95f * q.PriceArrival, 10f));   // Einkauf knapp unter dem Preis nach der Lieferung
        // ohne die Lieferung wäre der schlechte Fall der Verlauf
        npc.Order = null; npc.Phase = TradePhase.Idle;
        var q2 = MarketInfo.QuoteBuyer(u, gamma, cg.Id, 10f, tethys.Sector, from, p.Id, mem)!.Value;
        Assert.Equal(q.Incoming - 40f, q2.Incoming, 1);
        Assert.Equal(MathF.Min(q2.PriceArrival, q2.HistoryLow), q2.Price, 3);
    }
}
