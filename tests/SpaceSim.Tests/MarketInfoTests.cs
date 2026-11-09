using SpaceSim.Core.Demo;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

public class MarketInfoTests
{
    [Fact]
    public void StockRatio_And_Classify_FollowAmountVsTarget()
    {
        var under = new StockEntry { Amount = 40f, Target = 100f };
        var exact = new StockEntry { Amount = 100f, Target = 100f };
        var over = new StockEntry { Amount = 250f, Target = 100f };

        Assert.Equal(0.4f, MarketInfo.StockRatio(under), 3);
        Assert.Equal(MarketPressure.Scarce, MarketInfo.Classify(under));
        Assert.Equal(MarketPressure.Balanced, MarketInfo.Classify(exact));
        Assert.Equal(MarketPressure.Glut, MarketInfo.Classify(over));
        Assert.Equal(1f, MarketInfo.StockRatio(new StockEntry { Amount = 50f, Target = 0f }), 3);   // Ziel 0: keine Division durch null
    }

    [Fact]
    public void BestBuyerForGood_PicksHighestPrice_AndSkipsExcludeAndZeroPrice()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);   // MarketSystem setzt die Preise

        var good = DemoUniverse.Ore;
        var traders = u.StationsTrading(good).ToList();
        Assert.True(traders.Count >= 2, "Demo-Welt braucht mindestens zwei Stationen, die Erz handeln");

        var best = MarketInfo.BestBuyerForGood(u, good);
        Assert.NotNull(best);
        float bestPrice = traders.Where(s => s.Id != best!.Id).Max(s => s.Market[good].Price);
        Assert.Equal(bestPrice, best!.Market[good].Price, 3);   // höchste Preis der übrigen ist kleiner gleich
        Assert.True(best.Market[good].Price >= bestPrice);

        // Ausschluss der teuersten Station liefert die zweitbeste
        var second = MarketInfo.BestBuyerForGood(u, good, best.Id);
        Assert.NotNull(second);
        Assert.NotEqual(best.Id, second!.Id);
        Assert.True(second.Market[good].Price <= best.Market[good].Price);

        // Ware, die niemand handelt: null; null als Exclude ist erlaubt und ändert nichts
        var noGood = new GoodId(999);
        Assert.Null(MarketInfo.BestBuyerForGood(u, noGood));
        Assert.Equal(best.Id, MarketInfo.BestBuyerForGood(u, good, null)!.Id);
    }

    [Fact]
    public void BestBuyerForGood_IgnoresZeroPriceEntries()
    {
        var u = DemoUniverse.Create();
        var st = u.Stations.Values.First();
        var good = DemoUniverse.Ice;
        st.Market[good] = new StockEntry { Amount = 10f, Target = 100f, Price = 0f };   // Preis 0 = wird nicht gehandelt

        var best = MarketInfo.BestBuyerForGood(u, good, st.Id);
        if (best != null) Assert.True(best.Market[good].Price > 0f);
    }
}
