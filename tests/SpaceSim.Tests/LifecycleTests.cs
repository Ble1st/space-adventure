using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

public class LifecycleTests
{
    [Fact]
    public void LongRun_StaysBounded()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 30_000);

        Assert.True(u.Stations.Count <= u.Rules.MaxStations);
        Assert.True(u.Ships.Values.Count(s => s.Role == ShipRole.Trader) <= u.Rules.MaxTraders);
        Assert.True(u.Ships.Values.Count(s => s.Role == ShipRole.Miner) <= u.Rules.MaxMiners);
        foreach (var s in u.Ships.Values)
            Assert.True(s.Credits >= 0f && s.Credits < 100_000f && float.IsFinite(s.Credits), $"Schiff {s.Id.Value}: {s.Credits}");
        Assert.True(EconomyStats.Snapshot(u).ShipCredits < 300_000f, "Geld darf sich nicht bei den Schiffen aufstauen");
    }

    [Fact]
    public void BankruptStation_IsRemoved_ButEssentialIsNot()
    {
        var u = DemoUniverse.Create();
        var sector = u.Sectors[new SectorId(1)];
        var type = u.StationTypes["farm"];
        var doomed = StationFactory.Create(u, u.NewStationId(), type, "Pleitefarm", sector.Id, new Vec2(100, 100), sector.Owner);
        doomed.Credits = -50_000f;
        u.AddStation(doomed);
        u.Stations[new StationId(1)].Credits = -50_000f;   // wesentlich

        var removed = new List<StationRemoved>();
        u.Events.Raised += e => { if (e is StationRemoved r) removed.Add(r); };
        new SimulationRunner().Run(u, u.Rules.BankruptcyTicks + 200);

        Assert.DoesNotContain(doomed.Id, u.Stations.Keys);
        Assert.Contains(removed, r => r.Station == doomed.Id && r.Reason == "bankrupt");
        Assert.Contains(new StationId(1), u.Stations.Keys);
    }

    [Fact]
    public void RemovingStation_DoesNotBreakShipsWithOrdersForIt()
    {
        var u = DemoUniverse.Create();
        var runner = new SimulationRunner();
        runner.Run(u, 300);
        foreach (var id in u.Stations.Keys.Take(3).ToList()) u.RemoveStation(id, "test");
        runner.Run(u, 3_000);   // darf nicht mit KeyNotFound abstürzen
        Assert.All(u.Ships.Values, s => Assert.True(float.IsFinite(s.Credits)));
    }

    [Fact]
    public void ScarceGood_TriggersConstructionOfAProducer()
    {
        var u = DemoUniverse.Create();
        foreach (var st in u.Stations.Values)
            foreach (var (g, e) in st.Market) { e.Amount = e.Target; }
        foreach (var st in u.Stations.Values)
            if (st.Market.TryGetValue(DemoUniverse.Food, out var f)) f.Amount = 0f;
        u.Ships.Values.First(s => s.Role == ShipRole.Trader).Credits = 50_000f;

        var built = new List<StationBuilt>();
        u.Events.Raised += e => { if (e is StationBuilt b) built.Add(b); };
        int before = u.Stations.Count;
        new SimulationRunner().Run(u, 120);

        Assert.Equal(before + 1, u.Stations.Count);
        Assert.Equal("farm", built.Single().TypeKey);
    }

    [Fact]
    public void RichTrader_BuysAnotherShip_UpToCap()
    {
        var u = DemoUniverse.Create();
        var rich = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        rich.Credits = 20_000f;
        new FleetSystem().Step(u, 20);
        Assert.Equal(7, u.Ships.Values.Count(s => s.Role == ShipRole.Trader));

        u.Rules.MaxTraders = 7;
        rich.Credits = 20_000f;
        new FleetSystem().Step(u, 20);
        Assert.Equal(7, u.Ships.Values.Count(s => s.Role == ShipRole.Trader));
    }

    [Fact]
    public void BrokeIdleShip_IsAbandoned()
    {
        var u = DemoUniverse.Create();
        var ship = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        ship.Credits = 0f; ship.Order = null; ship.IdleSince = 0;
        u.Tick = u.Rules.ShipBankruptTicks + 10;
        new FleetSystem().Step(u, 20);
        Assert.DoesNotContain(ship.Id, u.Ships.Keys);
        Assert.DoesNotContain(ship.Id, u.Tracker.ParkedIn(ship.Sector));
    }

    [Fact]
    public void Station_PaysOnlyWhatItHas()
    {
        var u = DemoUniverse.Create();
        var smelter = u.Stations[new StationId(3)];
        smelter.Credits = 0f;
        Assert.False(TraderSystem.CanAfford(smelter, 10f, 100f));
        smelter.Credits = 5_000f;
        Assert.True(TraderSystem.CanAfford(smelter, 10f, 100f));
    }

    [Fact]
    public void Events_ReportCompletedTrades()
    {
        var u = DemoUniverse.Create();
        var recorder = new RouteRecorder(u);
        new SimulationRunner().Run(u, 3_000);
        Assert.NotEmpty(recorder.Top(3));
    }
}

public class WorldBuilderTests
{
    [Fact]
    public void DefaultWorld_Builds_WithConsistentReferences()
    {
        var u = WorldBuilder.BuildDefault();
        Assert.Equal(4, u.Sectors.Count);
        Assert.Equal(6, u.Gates.Count);
        foreach (var g in u.Gates.Values) Assert.Equal(g.Id, u.Gates[g.Target].Target);
        foreach (var st in u.Stations.Values) Assert.Contains(st.Id, u.Sectors[st.Sector].Stations);
        Assert.Equal(u.PlayerSector, u.Ships[u.PlayerShip].Sector);
        Assert.Contains(u.PlayerShip, u.Tracker.ParkedIn(u.PlayerSector));
    }

    [Fact]
    public void UnknownGood_IsReportedWithItsName()
    {
        var json = WorldBuilder.DefaultWorldJson().Replace("\"good\": \"ore\"", "\"good\": \"unobtainium\"");
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(WorldBuilder.Parse(json)));
        Assert.Contains("unobtainium", ex.Message);
    }

    [Fact]
    public void InvalidJson_ThrowsWorldFormatException() =>
        Assert.Throws<WorldFormatException>(() => WorldBuilder.Parse("{ not json"));

    [Fact]
    public void SameSeedOverride_ProducesSameWorld_DifferentSeedDiffers()
    {
        ulong Run(ulong seed)
        {
            var u = WorldBuilder.BuildDefault(seed);
            new SimulationRunner().Run(u, 5_000);
            return UniverseHash.Compute(u);
        }
        Assert.Equal(Run(7), Run(7));
    }
}

public class NavigatorTests
{
    /// <summary>Simuliert ein Schiff, das stur dem Wegpunkt folgt und Gates beim Berühren durchfliegt (wie die Darstellung).</summary>
    static (bool arrived, int steps, float minClearance) Fly(Universe u, SectorId sector, Vec2 pos, NavTarget target)
    {
        float minClear = float.MaxValue;
        for (int step = 0; step < 6_000; step++)
        {
            var nav = Navigator.Next(u, sector, pos, target);
            if (nav.Status == NavStatus.Arrived) return (true, step, minClear);
            if (nav.Status == NavStatus.Unreachable) return (false, step, minClear);

            var d = nav.Waypoint - pos;
            pos += d.Normalized() * MathF.Min(2.5f, d.Length);

            foreach (var o in u.GetObstacles(sector))
                minClear = MathF.Min(minClear, Vec2.Distance(pos, o.Position) - o.Radius);

            if (nav.Gate is { } g && Vec2.Distance(pos, u.Gates[g].Position) < 10f)
            {
                var far = u.Gates[u.Gates[g].Target];
                sector = far.Sector;
                pos = far.Position + (new Vec2(0, 0) - far.Position).Normalized() * 22f;
            }
        }
        return (false, 6_000, minClear);
    }

    [Fact]
    public void Autopilot_FlysAcrossSeveralSectors_WithoutTouchingObstacles()
    {
        var u = DemoUniverse.Create();
        var ship = u.PlayerShip;
        var target = Navigator.ForStation(u, new StationId(7), ship);   // Werft Delta: Alpha → Beta → Delta
        var (arrived, steps, clearance) = Fly(u, new SectorId(1), new Vec2(0, 0), target);
        Assert.True(arrived, $"nicht angekommen nach {steps} Schritten");
        Assert.True(clearance > Collision.ShipRadius, $"Mindestabstand zu Hindernissen {clearance:0.0} <= Schiffsradius");
    }

    [Fact]
    public void Autopilot_SameSector_ArrivesAtDockPoint()
    {
        var u = DemoUniverse.Create();
        var target = Navigator.ForStation(u, new StationId(2), u.PlayerShip);
        var (arrived, _, _) = Fly(u, new SectorId(1), new Vec2(-120, 120), target);
        Assert.True(arrived);
    }

    [Fact]
    public void ClosedGate_MakesSectorUnreachable()
    {
        var u = DemoUniverse.Create();
        foreach (var g in u.Gates.Values) g.Open = false;
        var nav = Navigator.Next(u, new SectorId(1), new Vec2(0, 0), Navigator.ForSector(u, new SectorId(3), u.PlayerShip));
        Assert.Equal(NavStatus.Unreachable, nav.Status);
    }

    [Fact]
    public void ForSector_TargetsFirstStationDock()
    {
        var u = DemoUniverse.Create();
        var t = Navigator.ForSector(u, new SectorId(2), u.PlayerShip);
        Assert.Equal(new SectorId(2), t.Sector);
        Assert.Equal("Sektor Beta", t.Label);
    }
}
