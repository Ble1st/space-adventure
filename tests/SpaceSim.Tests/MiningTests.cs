using SpaceSim.Core.Demo;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

public class MiningTests
{
    static (Universe u, Ship ship, Sector sector, int asteroid, ResourceField field) Setup()
    {
        var u = DemoUniverse.Create();
        var ship = u.Ships[u.PlayerShip];
        var sector = u.Sectors[u.PlayerSector];
        int idx = Enumerable.Range(0, sector.Asteroids.Count).First(i => sector.FieldFor(sector.Asteroids[i].Position) != null);
        return (u, ship, sector, idx, sector.FieldFor(sector.Asteroids[idx].Position)!);
    }

    /// <summary>Schiffsposition in Laserreichweite des Asteroiden.</summary>
    static Vec2 InRange(Sector s, int idx, float edge = 20f) => s.Asteroids[idx].Position + new Vec2(1, 0) * (s.Asteroids[idx].Radius + edge);

    [Fact]
    public void Laser_OutOfRange_DoesNothing()
    {
        var (u, ship, sector, a, _) = Setup();
        var chunks = new OreChunks();
        Assert.Equal(MineResult.OutOfRange, chunks.Mine(u, ship, sector, a, new Vec2(5000, 5000), 1f));
        Assert.Empty(chunks.Chunks);
    }

    [Fact]
    public void Laser_InRange_ReleasesChunks_ButDoesNotConsumeTheFieldYet()
    {
        var (u, ship, sector, a, field) = Setup();
        var chunks = new OreChunks();
        float before = field.Amount;
        Assert.Equal(MineResult.Ok, chunks.Mine(u, ship, sector, a, InRange(sector, a), 1f));
        Assert.True(chunks.Chunks.Count >= 4);
        Assert.Equal(before, field.Amount);   // erst beim Einfangen
        Assert.Empty(ship.Cargo);
    }

    [Fact]
    public void Tractor_PullsChunksIn_AndTransfersOreFromFieldToCargo()
    {
        var (u, ship, sector, a, field) = Setup();
        var chunks = new OreChunks();
        var pos = InRange(sector, a);
        float before = field.Amount;
        chunks.Mine(u, ship, sector, a, pos, 1f);
        int spawned = chunks.Chunks.Count;

        float total = 0f;
        for (int i = 0; i < 200; i++) total += chunks.Update(u, ship, pos, tractor: true, dt: 0.05f);

        Assert.Equal(spawned * MiningRules.ChunkUnits, total, 3);
        Assert.Equal(total, ship.Cargo[field.Good], 3);
        Assert.Equal(before - total, field.Amount, 3);
        Assert.Empty(chunks.Chunks);
    }

    [Fact]
    public void WithoutTractor_ChunksAreNotCollected()
    {
        var (u, ship, sector, a, _) = Setup();
        var chunks = new OreChunks();
        var pos = InRange(sector, a, edge: 25f);
        chunks.Mine(u, ship, sector, a, pos, 1f);
        for (int i = 0; i < 100; i++) chunks.Update(u, ship, pos, tractor: false, dt: 0.05f);
        Assert.Empty(ship.Cargo);
        Assert.NotEmpty(chunks.Chunks);
    }

    [Fact]
    public void ReservedChunks_NeverExceedWhatTheFieldHolds()
    {
        var (u, ship, sector, a, field) = Setup();
        field.Amount = 3f;
        var chunks = new OreChunks();
        for (int i = 0; i < 100; i++) chunks.Mine(u, ship, sector, a, InRange(sector, a), 0.1f);
        Assert.Single(chunks.Chunks);   // 3 Einheiten → ein Brocken à 2, der Rest reicht nicht
        Assert.Equal(MineResult.Depleted, chunks.Mine(u, ship, sector, a, InRange(sector, a), 0.1f));
    }

    [Fact]
    public void FullCargo_StopsTheLaser_AndChunksBounceOff()
    {
        var (u, ship, sector, a, field) = Setup();
        ship.Cargo[field.Good] = ship.CargoCapacity;
        var chunks = new OreChunks();
        Assert.Equal(MineResult.CargoFull, chunks.Mine(u, ship, sector, a, InRange(sector, a), 1f));

        ship.Cargo.Clear();
        ship.Cargo[field.Good] = ship.CargoCapacity - 2f;   // genau ein Brocken passt
        chunks.Mine(u, ship, sector, a, InRange(sector, a), 5f);
        for (int i = 0; i < 400; i++) chunks.Update(u, ship, InRange(sector, a), true, 0.05f);
        Assert.True(ship.UsedVolume(u) <= ship.CargoCapacity + 1e-3f);
    }

    [Fact]
    public void AsteroidOutsideAnyBelt_IsNotMinable()
    {
        var (u, ship, sector, a, _) = Setup();
        sector.Fields.Clear();
        var chunks = new OreChunks();
        Assert.False(chunks.IsMinable(sector, a));
        Assert.Equal(MineResult.NotMinable, chunks.Mine(u, ship, sector, a, InRange(sector, a), 1f));
    }

    [Fact]
    public void Nearest_PicksTheClosestMinableAsteroid()
    {
        var (_, _, sector, _, _) = Setup();
        var chunks = new OreChunks();
        var pos = new Vec2(0, 0);
        int best = chunks.Nearest(sector, pos);
        Assert.True(best >= 0);
        float d = Vec2.Distance(pos, sector.Asteroids[best].Position) - sector.Asteroids[best].Radius;
        for (int i = 0; i < sector.Asteroids.Count; i++)
            if (chunks.IsMinable(sector, i)) Assert.True(Vec2.Distance(pos, sector.Asteroids[i].Position) - sector.Asteroids[i].Radius >= d - 1e-3f);
    }
}

public class TradeActionTests
{
    [Fact]
    public void SellAll_PaysAtStationPrice_AndMovesGoods()
    {
        var u = DemoUniverse.Create();
        var ship = u.Ships[u.PlayerShip];
        var mine = u.Stations[new StationId(1)];
        ship.Cargo[DemoUniverse.Ore] = 50f;
        float credits = ship.Credits, price = mine.Market[DemoUniverse.Ore].Price, stock = mine.Market[DemoUniverse.Ore].Amount;
        if (price <= 0) { new SimulationRunner().Run(u, 1); price = mine.Market[DemoUniverse.Ore].Price; credits = ship.Credits; stock = mine.Market[DemoUniverse.Ore].Amount; }

        var sale = TradeActions.SellAll(u, ship, mine);

        Assert.Equal(50f * price, sale.Revenue, 2);
        Assert.Equal(credits + sale.Revenue, ship.Credits, 2);
        Assert.Equal(stock + 50f, mine.Market[DemoUniverse.Ore].Amount, 2);
        Assert.Empty(ship.Cargo);
    }

    [Fact]
    public void SellAll_StationPaysOnlyWhatItHas_RestStaysOnBoard()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        var ship = u.Ships[u.PlayerShip];
        var mine = u.Stations[new StationId(1)];
        mine.Credits = 100f;
        ship.Cargo[DemoUniverse.Ore] = 500f;

        var sale = TradeActions.SellAll(u, ship, mine);

        Assert.True(sale.Revenue <= 100f + 1e-3f);
        Assert.True(sale.Unsold > 0f);
        Assert.True(mine.Credits >= -1e-3f);
    }

    [Fact]
    public void SellAll_IgnoresGoodsTheStationDoesNotTrade()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        var ship = u.Ships[u.PlayerShip];
        var plant = u.Stations[new StationId(2)];   // Kraftwerk: führt keine Erz-Marktposition
        ship.Cargo[DemoUniverse.Ore] = 10f;
        var sale = TradeActions.SellAll(u, ship, plant);
        Assert.Equal(0f, sale.Revenue);
        Assert.Equal(10f, ship.Cargo[DemoUniverse.Ore]);
    }

    [Fact]
    public void BestBuyer_ChoosesAStationThatTradesTheCargo()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        var ship = u.Ships[u.PlayerShip];
        ship.Cargo[DemoUniverse.Ore] = 60f;
        var buyer = TradeActions.BestBuyer(u, ship, u.PlayerSector, new Vec2(0, 0));
        Assert.NotNull(buyer);
        Assert.True(buyer!.Market.ContainsKey(DemoUniverse.Ore));
    }
}

public class MiningAutopilotTests
{
    /// <summary>Spiegelt, was die Darstellung tut: fliegt Wegpunkte/Nav an, springt durch Gates, dockt, feuert und fängt ein.</summary>
    sealed class Harness
    {
        public Universe U = DemoUniverse.Create();
        public OreChunks Chunks = new();
        public MiningAutopilot Auto = new();
        public Vec2 Pos = new(0, 0);
        public int Sales;
        public float Revenue;
        public Ship Ship => U.Ships[U.PlayerShip];

        public Harness() => U.Events.Raised += e => { if (e is PlayerSold s) { Sales++; Revenue += s.Revenue; } };

        public void Run(float seconds, Func<bool>? until = null)
        {
            const float dt = 0.05f, speed = 30f;
            var sim = new SimulationRunner();
            double acc = 0;
            for (float t = 0; t < seconds; t += dt)
            {
                acc += dt;
                while (acc >= 0.25) { sim.Step(U); acc -= 0.25; }   // Wirtschaft läuft weiter

                var step = Auto.Step(U, Chunks, Ship, Pos);
                if (step.Nav is { } nav)
                {
                    var n = Navigator.Next(U, U.PlayerSector, Pos, nav);
                    if (n.Status == NavStatus.Arrived)
                    {
                        if (step.DockAt is { } sid) Auto.OnDocked(U, Ship, U.Stations[sid]);
                    }
                    else if (n.Status == NavStatus.EnRoute)
                    {
                        Pos += (n.Waypoint - Pos).Normalized() * MathF.Min(speed * dt, Vec2.Distance(n.Waypoint, Pos));
                        if (n.Gate is { } g && Vec2.Distance(Pos, U.Gates[g].Position) < 10f) Jump(g);
                    }
                }
                else if (step.Waypoint is { } wp)
                    Pos += (wp - Pos).Normalized() * MathF.Min(speed * dt * step.Throttle, Vec2.Distance(wp, Pos));

                if (step.Fire && step.Aim >= 0) Chunks.Mine(U, Ship, U.Sectors[U.PlayerSector], step.Aim, Pos, dt);
                Chunks.Update(U, Ship, Pos, step.Tractor, dt);
                if (until?.Invoke() == true) return;
            }
        }

        void Jump(GateId g)
        {
            var far = U.Gates[U.Gates[g].Target];
            U.MoveShip(Ship, far.Sector);
            U.PlayerSector = far.Sector;
            Pos = far.Position + (new Vec2(0, 0) - far.Position).Normalized() * 22f;
            Chunks.Clear();
        }
    }

    [Fact]
    public void FullLoop_MinesUntilFull_SellsAtAStation_AndGoesBackToWork()
    {
        var h = new Harness();
        var field = h.U.Sectors[new SectorId(1)].Fields.Sum(f => f.Amount);
        float credits = h.Ship.Credits;
        h.Auto.Start();

        h.Run(900, until: () => h.Sales >= 2);

        Assert.True(h.Sales >= 2, $"Verkäufe: {h.Sales}, Status: {h.Auto.Status}");
        Assert.True(h.Revenue > 0f);
        Assert.True(h.Ship.Credits > credits, "Credits sollten durch Verkäufe gestiegen sein");
        Assert.True(h.U.Sectors[new SectorId(1)].Fields.Sum(f => f.Amount) < field + 1_000f, "Feldbestand wurde abgebaut");
        Assert.True(h.Auto.Enabled, h.Auto.Status);
    }

    [Fact]
    public void NeverOverfillsTheCargo()
    {
        var h = new Harness();
        h.Auto.Start();
        float max = 0f;
        h.Run(300, until: () => { max = MathF.Max(max, h.Ship.UsedVolume(h.U)); return false; });
        Assert.True(max <= h.Ship.CargoCapacity + 1e-3f);
        Assert.True(max > 50f, "Es sollte tatsächlich abgebaut worden sein");
    }

    [Fact]
    public void NoOreAnywhere_StopsWithAStatus()
    {
        var h = new Harness();
        foreach (var s in h.U.Sectors.Values) foreach (var f in s.Fields) f.Amount = 0f;
        h.Auto.Start();
        var step = h.Auto.Step(h.U, h.Chunks, h.Ship, h.Pos);
        Assert.False(h.Auto.Enabled);
        Assert.Contains("Erz", h.Auto.Status);
        Assert.False(step.Fire);
    }

    [Fact]
    public void EmptySector_SendsTheShipToAnotherSectorWithOre()
    {
        var h = new Harness();
        foreach (var f in h.U.Sectors[new SectorId(1)].Fields) f.Amount = 0f;
        var beta = h.U.Sectors[new SectorId(2)];
        beta.Fields.Add(new ResourceField { Good = DemoUniverse.Ore, Position = new Vec2(60, 60), Amount = 500 });
        beta.Asteroids.Add(new Obstacle(new Vec2(80, 60), 5f));

        h.Auto.Start();
        var step = h.Auto.Step(h.U, h.Chunks, h.Ship, h.Pos);

        Assert.NotNull(step.Nav);
        Assert.Equal(new SectorId(2), step.Nav!.Value.Sector);
    }

    [Fact]
    public void Stop_DisablesAllActions()
    {
        var h = new Harness();
        h.Auto.Start();
        h.Auto.Stop("manuell");
        var step = h.Auto.Step(h.U, h.Chunks, h.Ship, h.Pos);
        Assert.False(step.Fire);
        Assert.Null(step.Nav);
        Assert.Null(step.Waypoint);
    }
}
