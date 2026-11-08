using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

public class EconomyTests
{
    static Universe TwoSectorUniverse()
    {
        var u = new Universe();
        var ore = new Good { Id = new(1), Name = "Erz", BasePrice = 10f };
        u.Goods[ore.Id] = ore;

        var a = new Sector { Id = new(1), Name = "A" };
        var b = new Sector { Id = new(2), Name = "B" };
        u.Sectors[a.Id] = a; u.Sectors[b.Id] = b;

        var ga = new Gate { Id = new(1), Sector = a.Id, Target = new(2) };
        var gb = new Gate { Id = new(2), Sector = b.Id, Target = new(1) };
        u.Gates[ga.Id] = ga; u.Gates[gb.Id] = gb;
        a.Gates.Add(ga.Id); b.Gates.Add(gb.Id);

        var mine = new Station { Id = new(1), Name = "Mine", Sector = a.Id };
        mine.Production.Add(new Recipe { Outputs = { [ore.Id] = 5f }, TicksPerCycle = 5 });
        mine.Stock(ore.Id).Amount = 200;
        var smelter = new Station { Id = new(2), Name = "Schmelze", Sector = b.Id };
        smelter.Stock(ore.Id).Amount = 0;
        smelter.Stock(ore.Id).Target = 100;
        u.Stations[mine.Id] = mine; u.Stations[smelter.Id] = smelter;
        a.Stations.Add(mine.Id); b.Stations.Add(smelter.Id);

        var ship = new Ship { Id = new(1), Role = ShipRole.Trader, Credits = 1000, Sector = a.Id };
        u.Ships[ship.Id] = ship;
        a.Ships.Add(ship.Id);
        return u;
    }

    [Fact]
    public void Hops_BetweenConnectedSectors_IsOne()
    {
        var u = TwoSectorUniverse();
        Assert.Equal(1, u.Hops(new SectorId(1), new SectorId(2)));
        Assert.Equal(0, u.Hops(new SectorId(1), new SectorId(1)));
    }

    [Fact]
    public void Economy_RunsStably_WithoutPlayer()
    {
        var u = TwoSectorUniverse();
        new SimulationRunner().Run(u, 5_000);

        foreach (var st in u.Stations.Values)
            foreach (var e in st.Market.Values)
            {
                Assert.True(e.Amount >= 0, "Bestand darf nie negativ sein");
                Assert.True(float.IsFinite(e.Price) && e.Price > 0, "Preis muss endlich und positiv sein");
            }
        Assert.True(float.IsFinite(u.Ships[new ShipId(1)].Credits));
    }

    [Fact]
    public void Trader_MovesGoodsToStationWithDemand()
    {
        var u = TwoSectorUniverse();
        new SimulationRunner().Run(u, 500);
        Assert.True(u.Stations[new StationId(2)].Stock(new GoodId(1)).Amount > 0);
    }
}

public class DemoUniverseTests
{
    [Fact]
    public void DemoUniverse_RunsStably_For20kTicks()
    {
        var u = SpaceSim.Core.Demo.DemoUniverse.Create();
        new SimulationRunner().Run(u, 20_000);

        foreach (var st in u.Stations.Values)
            foreach (var e in st.Market.Values)
            {
                Assert.True(e.Amount >= 0 && float.IsFinite(e.Amount));
                Assert.True(e.Price > 0 && float.IsFinite(e.Price));
            }
        foreach (var s in u.Ships.Values)
            Assert.True(float.IsFinite(s.Credits));
    }
}

public class MinerTests
{
    [Fact]
    public void Miners_ExtractOreAndEarnCredits()
    {
        var u = SpaceSim.Core.Demo.DemoUniverse.Create();
        float fieldsBefore = u.Sectors[new SectorId(1)].Fields.Sum(f => f.Amount);
        new SimulationRunner().Run(u, 2_000);

        var miners = u.Ships.Values.Where(s => s.Role == ShipRole.Miner).ToList();
        Assert.Equal(2, miners.Count);
        Assert.All(miners, m => Assert.True(m.Credits > 1000f, "Miner sollte Gewinn gemacht haben"));
        Assert.True(u.Sectors[new SectorId(1)].Fields.Sum(f => f.Amount) < fieldsBefore);
    }

    [Fact]
    public void Miners_DoNotMine_WhenPriceTooLow()
    {
        var u = SpaceSim.Core.Demo.DemoUniverse.Create();
        foreach (var st in u.Stations.Values)
        {
            st.Stock(SpaceSim.Core.Demo.DemoUniverse.Ore).Amount = 100_000;   // Überangebot → Preis am Boden
            st.Production.Clear();
        }
        foreach (var s in u.Ships.Values.Where(s => s.Role == ShipRole.Trader).ToList()) u.Ships.Remove(s.Id);
        new SimulationRunner().Run(u, 200);
        Assert.All(u.Ships.Values.Where(s => s.Role == ShipRole.Miner), m => Assert.Null(m.Order));
    }
}
