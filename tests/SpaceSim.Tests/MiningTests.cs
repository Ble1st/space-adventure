using SpaceSim.Core.Demo;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Modules;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

public class MiningTests
{
    static (Universe u, Ship ship, Sector sector, Asteroid rock) Setup()
    {
        var u = DemoUniverse.Create();
        var ship = u.Ships[u.PlayerShip];
        var sector = u.Sectors[u.PlayerSector];
        return (u, ship, sector, sector.Asteroids.First(a => a.Minable));
    }

    /// <summary>Schiffsposition in Laserreichweite des Asteroiden.</summary>
    static Vec2 InRange(Asteroid a, float edge = 20f) => a.Position + new Vec2(1, 0) * (a.Radius + edge);

    /// <summary>Feuert so lange, bis <paramref name="seconds"/> Spielzeit vergangen ist.</summary>
    static void Fire(OreChunks c, Universe u, Ship ship, Sector sector, Asteroid a, Vec2 pos, float seconds)
    {
        for (float t = 0; t < seconds; t += 0.05f) { c.Mine(u, ship, sector, a, pos, 0.05f); c.Update(u, ship, pos, tractor: false, 0.05f); }
    }

    [Fact]
    public void Laser_OutOfRange_DoesNothing()
    {
        var (u, ship, sector, rock) = Setup();
        var chunks = new OreChunks();
        Assert.Equal(MineResult.OutOfRange, chunks.Mine(u, ship, sector, rock, new Vec2(5000, 5000), 1f));
        Assert.Empty(chunks.Chunks);
    }

    [Fact]
    public void Shots_FireAtAFixedRate_AndOnlySometimesYieldAChunk()
    {
        var (u, ship, sector, rock) = Setup();
        rock.Ore = rock.MaxOre = 100_000f;   // soll nicht leerlaufen
        ship.CargoCapacity = 1_000_000f;     // Laderaum soll das Feuern nicht begrenzen
        var chunks = new OreChunks(seed: 7);
        int shots = 0, hits = 0;
        chunks.Shot += (_, units) => { shots++; if (units > 0) hits++; };

        Fire(chunks, u, ship, sector, rock, InRange(rock), seconds: 60f);

        Assert.InRange(shots, 60f / MiningRules.ShotInterval * 0.95, 60f / MiningRules.ShotInterval * 1.05);   // ~150 Schüsse
        double rate = hits / (double)shots;
        Assert.InRange(rate, MiningRules.ChunkChance - 0.12, MiningRules.ChunkChance + 0.12);   // nicht jeder Schuss trifft
        Assert.True(hits < shots);
    }

    [Fact]
    public void ChunkSizes_VaryBetweenChunks_AndFollowTheTable()
    {
        var rng = new DeterministicRng(11);
        var rolls = Enumerable.Range(0, 5_000).Select(_ => MiningRules.RollChunkUnits(rng)).ToList();
        Assert.True(rolls.Distinct().Count() >= 4, "Brocken sollen unterschiedlich groß sein");
        Assert.All(rolls, r => Assert.Contains(r, MiningRules.ChunkTable.Select(t => t.Units)));
        Assert.InRange(rolls.Average(), 2.4, 3.0);
        Assert.True(rolls.Count(r => r == 1f) > rolls.Count(r => r == 8f) * 3, "kleine Brocken sind häufiger als große");
    }

    [Fact]
    public void EveryReleasedChunk_HasItsOwnAmount_TakenFromTheAsteroid()
    {
        var (u, ship, sector, rock) = Setup();
        rock.Ore = rock.MaxOre = 100_000f;
        var chunks = new OreChunks(seed: 3);
        float before = rock.Ore;
        Fire(chunks, u, ship, sector, rock, InRange(rock), seconds: 30f);

        Assert.True(chunks.Chunks.Count > 5);
        Assert.True(chunks.Chunks.Select(c => c.Units).Distinct().Count() > 1);
        float released = chunks.Chunks.Sum(c => c.Units);
        Assert.Equal(before - released, rock.Ore, 2);   // was heraus kam, fehlt im Asteroiden
    }

    [Fact]
    public void Tractor_PullsChunksIn_AndLoadsThemIntoTheCargo()
    {
        var (u, ship, sector, rock) = Setup();
        rock.Ore = rock.MaxOre = 100_000f;
        var chunks = new OreChunks(seed: 5);
        var pos = InRange(rock);
        Fire(chunks, u, ship, sector, rock, pos, 6f);
        float inSpace = chunks.Chunks.Sum(c => c.Units);
        Assert.True(inSpace > 0);

        float total = 0f;
        for (int i = 0; i < 300; i++) total += chunks.Update(u, ship, pos, tractor: true, dt: 0.05f);

        Assert.Equal(inSpace, total, 3);
        Assert.Equal(total, ship.Cargo[rock.Good], 3);
        Assert.Empty(chunks.Chunks);
    }

    [Fact]
    public void WithoutTractor_ChunksAreNotCollected()
    {
        var (u, ship, sector, rock) = Setup();
        var chunks = new OreChunks(seed: 5);
        Fire(chunks, u, ship, sector, rock, InRange(rock, edge: 25f), 8f);
        Assert.Empty(ship.Cargo);
        Assert.NotEmpty(chunks.Chunks);
    }

    [Fact]
    public void UncollectedChunks_Expire_AndTheOreIsLost()
    {
        var (u, ship, sector, rock) = Setup();
        var chunks = new OreChunks(seed: 5);
        Fire(chunks, u, ship, sector, rock, InRange(rock, edge: 25f), 5f);
        Assert.NotEmpty(chunks.Chunks);
        for (float t = 0; t < MiningRules.ChunkLifetime + 5; t += 1f) chunks.Update(u, ship, new Vec2(5000, 5000), false, 1f);
        Assert.Empty(chunks.Chunks);
        Assert.Empty(ship.Cargo);
    }

    [Fact]
    public void AsteroidShrinksWhileMined_AndDisappearsWhenEmpty()
    {
        var (u, ship, sector, rock) = Setup();
        rock.Ore = rock.MaxOre = 30f;
        float radiusBefore = rock.Radius;
        var chunks = new OreChunks(seed: 9);
        var events = new List<AsteroidDepleted>();
        u.Events.Raised += e => { if (e is AsteroidDepleted d) events.Add(d); };

        for (int i = 0; i < 4_000 && sector.Asteroids.Contains(rock); i++)
        {
            ship.Cargo.Clear();   // Laderaum nie voll
            chunks.Mine(u, ship, sector, rock, InRange(rock), 0.05f);
            if (i == 40) Assert.True(rock.Radius < radiusBefore, "schrumpft während des Abbaus");
        }

        Assert.DoesNotContain(rock, sector.Asteroids);
        Assert.Single(events);
        Assert.Equal(MineResult.NotMinable, chunks.Mine(u, ship, sector, rock, InRange(rock), 0.1f));
    }

    [Fact]
    public void FullCargo_StopsTheLaser_AndPartialChunksStayInSpace()
    {
        var (u, ship, sector, rock) = Setup();
        rock.Ore = rock.MaxOre = 100_000f;
        ship.Cargo[rock.Good] = ship.CargoCapacity;
        var chunks = new OreChunks();
        Assert.Equal(MineResult.CargoFull, chunks.Mine(u, ship, sector, rock, InRange(rock), 1f));

        ship.Cargo[rock.Good] = ship.CargoCapacity - 1f;   // nur 1 Einheit Platz
        var pos = InRange(rock);
        chunks.Chunks.Add(new OreChunk { Good = rock.Good, Units = 5f, Position = pos });
        chunks.Update(u, ship, pos, tractor: true, 0.05f);
        Assert.Equal(ship.CargoCapacity, ship.UsedVolume(u), 3);
        Assert.Equal(4f, chunks.Chunks.Single().Units, 3);   // Rest bleibt draußen
    }

    [Fact]
    public void Rock_WithoutOre_IsNotMinable()
    {
        var (u, ship, sector, _) = Setup();
        var inert = sector.Asteroids.First(a => !a.Minable);
        var chunks = new OreChunks();
        Assert.False(chunks.IsMinable(sector, inert));
        Assert.Equal(MineResult.NotMinable, chunks.Mine(u, ship, sector, inert, InRange(inert), 1f));
    }

    [Fact]
    public void Nearest_PicksTheClosestMinableAsteroid()
    {
        var (_, _, sector, _) = Setup();
        var chunks = new OreChunks();
        var pos = new Vec2(0, 0);
        var best = chunks.Nearest(sector, pos)!;
        float d = Vec2.Distance(pos, best.Position) - best.Radius;
        foreach (var a in sector.Asteroids.Where(a => a.Minable))
            Assert.True(Vec2.Distance(pos, a.Position) - a.Radius >= d - 1e-3f);
    }
}

public class RespawnTests
{
    static (Universe u, Sector sector, ResourceField field) Setup()
    {
        var u = DemoUniverse.Create();
        var sector = u.Sectors[new SectorId(1)];
        return (u, sector, sector.Fields[0]);
    }

    [Fact]
    public void InitialBelt_HasTheTargetNumberOfOreRocks_WithIndividualOre()
    {
        var (_, sector, field) = Setup();
        var rocks = Belt.AsteroidsOf(sector, field).ToList();
        Assert.Equal(field.TargetAsteroids, rocks.Count);
        Assert.True(rocks.Select(r => MathF.Round(r.Ore)).Distinct().Count() > rocks.Count / 2, "jeder Asteroid hat eine eigene Erzmenge");
    }

    [Fact]
    public void DepletedAsteroids_RespawnOverTime_UntilTheBeltIsFullAgain()
    {
        var (u, sector, field) = Setup();
        var respawned = new List<AsteroidRespawned>();
        u.Events.Raised += e => { if (e is AsteroidRespawned r) respawned.Add(r); };

        foreach (var rock in Belt.AsteroidsOf(sector, field).Take(8).ToList()) Belt.Remove(u, sector, rock, rock.Ore);
        Assert.Equal(field.TargetAsteroids - 8, Belt.AsteroidsOf(sector, field).Count());

        new SimulationRunner(new ISimModule[] { new ResourceModule() }).Run(u, field.RespawnTicks * 12);

        Assert.Equal(field.TargetAsteroids, Belt.AsteroidsOf(sector, field).Count());
        Assert.Equal(8, respawned.Count);
    }

    [Fact]
    public void Respawn_IsGradual_NotInstant()
    {
        var (u, sector, field) = Setup();
        foreach (var rock in Belt.AsteroidsOf(sector, field).ToList()) Belt.Remove(u, sector, rock, rock.Ore);
        new SimulationRunner(new ISimModule[] { new ResourceModule() }).Run(u, 10);
        Assert.True(Belt.AsteroidsOf(sector, field).Count() < field.TargetAsteroids / 2);
    }

    [Fact]
    public void RespawnedAsteroids_FitTheRules_AndHaveFreshOre()
    {
        var (u, sector, field) = Setup();
        foreach (var rock in Belt.AsteroidsOf(sector, field).ToList()) Belt.Remove(u, sector, rock, rock.Ore);
        new SimulationRunner(new ISimModule[] { new ResourceModule() }).Run(u, field.RespawnTicks * 40);

        foreach (var rock in Belt.AsteroidsOf(sector, field))
        {
            Assert.True(rock.Ore > 0f && rock.Ore <= rock.MaxOre + 1e-3f);
            foreach (var g in sector.Gates) Assert.True(Vec2.Distance(rock.Position, u.Gates[g].Position) > rock.Radius + 30f);
            foreach (var st in sector.Stations) Assert.True(Vec2.Distance(rock.Position, u.Stations[st].Position) > rock.Radius + 40f);
            Assert.True(Vec2.Distance(rock.Position, field.Position) > rock.Radius + 15f);
        }
        var ids = sector.Asteroids.Select(a => a.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Respawn_NeverHappensNextToThePlayer()
    {
        var (u, sector, field) = Setup();
        var player = u.Ships[u.PlayerShip];
        foreach (var rock in Belt.AsteroidsOf(sector, field).ToList()) Belt.Remove(u, sector, rock, rock.Ore);
        player.Position = field.Position;   // mitten im Gürtel
        new SimulationRunner(new ISimModule[] { new ResourceModule() }).Run(u, field.RespawnTicks * 40);
        foreach (var rock in Belt.AsteroidsOf(sector, field))
            Assert.True(Vec2.Distance(rock.Position, player.Position) >= AsteroidRespawnSystem.PlayerSafeRadius);
    }

    [Fact]
    public void NpcMining_DrainsRocks_AndTheEconomyKeepsOreAvailableThroughRespawn()
    {
        var u = DemoUniverse.Create();
        var sector = u.Sectors[new SectorId(1)];
        new SimulationRunner().Run(u, 30_000);
        float ore = sector.Fields.Sum(f => Belt.OreIn(sector, f));
        Assert.True(ore > 100f, $"Gürtel sollte durch Respawn nicht dauerhaft leer sein (Erz: {ore:0})");
        Assert.All(sector.Fields, f => Assert.True(Belt.AsteroidsOf(sector, f).Count() <= f.TargetAsteroids));
    }

    [Fact]
    public void SaveAndLoad_KeepsRocksAndRespawnBehaviour()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 6_000);
        var (loaded, _) = SpaceSim.Core.Persistence.SaveGameJson.Read(SpaceSim.Core.Persistence.SaveGameJson.Write(u, DateTimeOffset.UtcNow));
        Assert.Equal(u.Sectors[new SectorId(1)].Asteroids.Count, loaded.Sectors[new SectorId(1)].Asteroids.Count);
        new SimulationRunner().Run(u, 4_000);
        new SimulationRunner().Run(loaded, 4_000);
        Assert.Equal(SpaceSim.Core.Diagnostics.UniverseHash.Compute(u), SpaceSim.Core.Diagnostics.UniverseHash.Compute(loaded));
    }

    [Fact]
    public void OldSaveFormat_IsMigrated_OreMovesFromFieldIntoRocks()
    {
        var u = DemoUniverse.Create();
        var save = SpaceSim.Core.Persistence.UniverseSerializer.ToSave(u, DateTimeOffset.UtcNow);

        // Format v1 nachbauen: Vorrat im Feld, Asteroiden als bloße Hindernisse
        save.Version = 1;
        foreach (var s in save.Sectors)
        {
            s.Asteroids = s.Rocks.Select(r => new Obstacle(r.Pos, r.BaseRadius)).ToList();
            s.Rocks = new();
            foreach (var f in s.Fields) { f.Amount = 2_000f; f.RegenPerTick = 0.5f; f.Capacity = 3_000f; }
        }
        save.NextAsteroidId = 1;
        var json = System.Text.Json.JsonSerializer.Serialize(save, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });

        var (loaded, _) = SpaceSim.Core.Persistence.SaveGameJson.Read(json);

        var alpha = loaded.Sectors[new SectorId(1)];
        float oreTotal = alpha.Fields.Sum(f => Belt.OreIn(alpha, f));
        Assert.InRange(oreTotal, 3_900f, 4_100f);                        // 2 Felder × 2000
        Assert.Contains(alpha.Asteroids, a => !a.Minable);               // Hindernisse ohne Erz bleiben
        Assert.True(loaded.NextAsteroidId > alpha.Asteroids.Max(a => a.Id));
        new SimulationRunner().Run(loaded, 2_000);                       // läuft weiter
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

                if (step.Fire && step.Aim is { } rock) Chunks.Mine(U, Ship, U.Sectors[U.PlayerSector], rock, Pos, dt);
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
        var alpha = h.U.Sectors[new SectorId(1)];
        float oreBefore = alpha.Asteroids.Sum(a => a.Ore);
        float credits = h.Ship.Credits;
        h.Auto.Start();

        h.Run(900, until: () => h.Sales >= 2);

        Assert.True(h.Sales >= 2, $"Verkäufe: {h.Sales}, Status: {h.Auto.Status}");
        Assert.True(h.Revenue > 0f);
        Assert.True(h.Ship.Credits > credits, "Credits sollten durch Verkäufe gestiegen sein");
        Assert.True(h.Ship.Credits > credits + 100f, "Der Abbau sollte sich lohnen");
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
        foreach (var s in h.U.Sectors.Values) s.Asteroids.RemoveAll(a => a.Minable);
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
        h.U.Sectors[new SectorId(1)].Asteroids.RemoveAll(a => a.Minable);
        var beta = h.U.Sectors[new SectorId(2)];
        beta.Fields.Add(new ResourceField { Good = DemoUniverse.Ore, Position = new Vec2(60, 60) });
        beta.Asteroids.Add(new Asteroid { Id = 9_999, Position = new Vec2(80, 60), BaseRadius = 5f, Good = DemoUniverse.Ore, Ore = 500, MaxOre = 500 });

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
