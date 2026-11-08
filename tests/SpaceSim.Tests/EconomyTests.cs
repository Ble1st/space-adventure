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
        u.AddShip(ship);
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

public class MaterializerTests
{
    [Fact]
    public void Plan_AcrossGates_HasOneLegPerSector_AndIsContiguous()
    {
        var u = SpaceSim.Core.Demo.DemoUniverse.Create();
        var legs = Travel.Plan(u, 100, new SectorId(1), new Vec2(0, 0), new SectorId(3), new Vec2(0, 0))!;
        Assert.Equal(new[] { 1, 2, 3 }, legs.Select(l => l.Sector.Value).Distinct());
        Assert.Equal(100, legs[0].StartTick);
        for (int i = 1; i < legs.Count; i++) Assert.Equal(legs[i - 1].EndTick, legs[i].StartTick);
    }

    [Fact]
    public void EveryShip_IsVisibleInAtMostOneSector_AtAnyTime()
    {
        var u = SpaceSim.Core.Demo.DemoUniverse.Create();
        var sim = new SimulationRunner();
        for (int i = 0; i < 1500; i++)
        {
            sim.Step(u);
            foreach (double frac in new[] { 0.0, 0.5 })
            {
                var seen = new Dictionary<ShipId, int>();
                foreach (var sector in u.Sectors.Keys)
                    foreach (var snap in Materializer.Snapshot(u, sector, u.Tick + frac))
                        seen[snap.Id] = seen.GetValueOrDefault(snap.Id) + 1;

                foreach (var ship in u.Ships.Values.Where(s => s.Role != ShipRole.Player))
                {
                    int n = seen.GetValueOrDefault(ship.Id);
                    Assert.True(n <= 1, $"Schiff {ship.Id.Value} in {n} Sektoren (Tick {u.Tick})");
                    if (ship.Transit is null) Assert.Equal(1, n);
                }
            }
        }
    }

    [Fact]
    public void ShipInTransit_AppearsAtGate_AndFliesToItsStation()
    {
        var u = SpaceSim.Core.Demo.DemoUniverse.Create();
        var sim = new SimulationRunner();
        bool sawMovingInBeta = false;
        for (int i = 0; i < 1500 && !sawMovingInBeta; i++)
        {
            sim.Step(u);
            sawMovingInBeta = Materializer.Snapshot(u, new SectorId(2), u.Tick).Any(s => s.Moving);
        }
        Assert.True(sawMovingInBeta, "Es sollten fliegende Schiffe im Sektor Beta sichtbar werden");
    }
}


public class CollisionTests
{
    [Fact]
    public void Resolve_PushesShipOutOfObstacle()
    {
        var pos = new Vec2(3, 0);
        bool hit = Collision.Resolve(ref pos, 2.5f, new[] { new Obstacle(new Vec2(0, 0), 5f) }, out var n);
        Assert.True(hit);
        Assert.Equal(7.5f, pos.X, 3);
        Assert.Equal(1f, n.X, 3);
    }

    [Fact]
    public void Resolve_NoOverlap_DoesNothing()
    {
        var pos = new Vec2(20, 0);
        Assert.False(Collision.Resolve(ref pos, 2.5f, new[] { new Obstacle(new Vec2(0, 0), 5f) }, out _));
        Assert.Equal(20f, pos.X);
    }
}

public class AvoidanceTests
{
    [Fact]
    public void Plan_DetoursAroundObstacle_OnTheDirectLine()
    {
        var obs = new[] { new Obstacle(new Vec2(0, 0), 10f) };
        var pts = PathPlanner.Plan(new Vec2(-50, 0), new Vec2(50, 0), obs);
        Assert.True(pts.Count > 2, "Es muss ein Wegpunkt für den Umweg geben");
        AssertClear(pts, obs);
    }

    [Fact]
    public void Plan_StraightLine_WhenNothingInTheWay()
    {
        var pts = PathPlanner.Plan(new Vec2(-50, 0), new Vec2(50, 0), new[] { new Obstacle(new Vec2(0, 40), 10f) });
        Assert.Equal(2, pts.Count);
    }

    [Fact]
    public void DemoUniverse_AllKeyPointPairs_HaveCollisionFreeLegs()
    {
        var u = SpaceSim.Core.Demo.DemoUniverse.Create();
        foreach (var sector in u.Sectors.Values)
        {
            var obs = u.GetObstacles(sector.Id);
            var points = new List<Vec2>();
            points.AddRange(sector.Gates.Select(g => u.Gates[g].Position));
            points.AddRange(sector.Stations.Select(s => u.Stations[s].DockPoint(new ShipId(4))));
            points.AddRange(sector.Fields.Select(f => f.WorkPoint(new ShipId(5))));
            foreach (var a in points)
                foreach (var b in points)
                    if (a != b) AssertClear(PathPlanner.Plan(a, b, obs), obs);
        }
    }

    [Fact]
    public void DemoUniverse_AsteroidsDoNotCoverGatesStationsOrFields()
    {
        var u = SpaceSim.Core.Demo.DemoUniverse.Create();
        foreach (var sector in u.Sectors.Values)
        {
            Assert.NotEmpty(sector.Asteroids);
            foreach (var a in sector.Asteroids)
            {
                foreach (var g in sector.Gates) Assert.True(Vec2.Distance(a.Position, u.Gates[g].Position) > a.Radius + 30f);
                foreach (var st in sector.Stations) Assert.True(Vec2.Distance(a.Position, u.Stations[st].Position) > a.Radius + 40f);
                foreach (var f in sector.Fields) Assert.True(Vec2.Distance(a.Position, f.Position) > a.Radius + 15f);
            }
        }
    }

    [Fact]
    public void Plan_GoesAroundLongWall_OfOverlappingObstacles()
    {
        var obs = new List<Obstacle>();
        for (int y = -60; y <= 60; y += 12) obs.Add(new Obstacle(new Vec2(0, y), 8f));
        var pts = PathPlanner.Plan(new Vec2(-60, 0), new Vec2(60, 0), obs);
        AssertClear(pts, obs);
    }

    [Fact]
    public void Plan_NodeBudget_KeepsPlanningBounded()
    {
        // Dichtes Gitter: darf nicht explodieren und muss einen Pfad bis zum Ziel liefern
        var obs = new List<Obstacle>();
        for (int x = -40; x <= 40; x += 14)
            for (int y = -40; y <= 40; y += 14) obs.Add(new Obstacle(new Vec2(x + 3, y + 3), 5f));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var pts = PathPlanner.Plan(new Vec2(-80, -80), new Vec2(80, 80), obs);
        Assert.True(sw.ElapsedMilliseconds < 200, $"Planung dauerte {sw.ElapsedMilliseconds} ms");
        Assert.Equal(new Vec2(80, 80), pts[^1]);
    }

    static void AssertClear(List<Vec2> pts, IReadOnlyList<Obstacle> obs)
    {
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1]; var b = pts[i];
            var ab = b - a;
            float len2 = ab.Dot(ab);
            foreach (var o in obs)
            {
                float t = len2 > 1e-6f ? Math.Clamp((o.Position - a).Dot(ab) / len2, 0f, 1f) : 0f;
                float d = Vec2.Distance(a + ab * t, o.Position);
                Assert.True(d >= o.Radius, $"Segment {i} schneidet Hindernis bei {o.Position} (Abstand {d:0.0} < {o.Radius})");
            }
        }
    }
}


public class ShipAvoidanceTests
{
    [Fact]
    public void Desired_IsZero_WhenFarAway()
    {
        var d = Avoidance.Desired(new ShipId(1), new Vec2(0, 0), new[] { (new ShipId(2), new Vec2(100, 0)) });
        Assert.Equal(0f, d.Length);
    }

    [Fact]
    public void Desired_PointsAwayFromNeighbour_AndIsClamped()
    {
        var d = Avoidance.Desired(new ShipId(1), new Vec2(0, 0), new[] { (new ShipId(2), new Vec2(5, 0)) });
        Assert.True(d.X < 0 && MathF.Abs(d.Y) < 1e-4f);
        var crowded = Enumerable.Range(2, 10).Select(i => (new ShipId(i), new Vec2(1, 0))).ToList();
        Assert.True(Avoidance.Desired(new ShipId(1), new Vec2(0, 0), crowded).Length <= Avoidance.MaxOffset + 1e-3f);
    }

    [Fact]
    public void Desired_ShipsOnTopOfEachOther_DodgeInOppositeDirections()
    {
        var p = new Vec2(10, 10);
        var a = Avoidance.Desired(new ShipId(1), p, new[] { (new ShipId(2), p) });
        var b = Avoidance.Desired(new ShipId(2), p, new[] { (new ShipId(1), p) });
        Assert.True(a.Dot(b) < 0f);
    }
}

public class DeterminismTests
{
    [Fact]
    public void SameSeed_SameTicks_SameHash()
    {
        ulong Run()
        {
            var u = SpaceSim.Core.Demo.DemoUniverse.Create();
            new SimulationRunner().Run(u, 3_000);
            return SpaceSim.Core.Diagnostics.UniverseHash.Compute(u);
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Tracker_MatchesShipState_AfterLongRun()
    {
        var u = SpaceSim.Core.Demo.DemoUniverse.Create();
        new SimulationRunner().Run(u, 3_000);
        foreach (var ship in u.Ships.Values)
        {
            if (ship.Transit is null) Assert.Contains(ship.Id, u.Tracker.ParkedIn(ship.Sector));
            else foreach (var leg in ship.Transit.Legs) Assert.Contains(ship.Id, u.Tracker.TransitIn(leg.Sector));
        }
        // keine Leichen im Index
        foreach (var sec in u.Sectors.Keys)
            foreach (var id in u.Tracker.ParkedIn(sec)) Assert.Null(u.Ships[id].Transit);
    }
}
