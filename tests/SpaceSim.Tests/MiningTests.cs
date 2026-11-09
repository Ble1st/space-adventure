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
        for (float t = 0; t < seconds; t += 0.05f) { c.TryFire(u, ship, sector, a, pos); c.Update(u, ship, pos, tractor: false, 0.05f); }
    }

    [Fact]
    public void Laser_OutOfRange_DoesNothing()
    {
        var (u, ship, sector, rock) = Setup();
        var chunks = new OreChunks();
        Assert.Equal(MineResult.OutOfRange, chunks.TryFire(u, ship, sector, rock, new Vec2(5000, 5000)));
        Assert.Empty(chunks.Chunks);
    }

    [Fact]
    public void Shots_AreLimitedByTheReloadTime_AndOnlySometimesYieldAChunk()
    {
        var (u, ship, sector, rock) = Setup();
        rock.Ore = rock.MaxOre = 100_000f;   // soll nicht leerlaufen
        ship.CargoCapacity = 1_000_000f;     // Laderaum soll das Feuern nicht begrenzen
        var chunks = new OreChunks(seed: 7);
        int shots = 0, hits = 0;
        chunks.Shot += (_, r) => { shots++; if (r.Hit) hits++; };

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
        Assert.Equal(total, ship.UsedVolume(u), 3);
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
            chunks.TryFire(u, ship, sector, rock, InRange(rock));
            chunks.Update(u, ship, InRange(rock), false, 0.05f);
            if (i == 40) Assert.True(rock.Radius < radiusBefore, "schrumpft während des Abbaus");
        }

        Assert.DoesNotContain(rock, sector.Asteroids);
        Assert.Single(events);
        chunks.Update(u, ship, InRange(rock), false, 1f);   // nachladen
        Assert.Equal(MineResult.NotMinable, chunks.TryFire(u, ship, sector, rock, InRange(rock)));
    }

    [Fact]
    public void FullCargo_StopsTheLaser_AndPartialChunksStayInSpace()
    {
        var (u, ship, sector, rock) = Setup();
        rock.Ore = rock.MaxOre = 100_000f;
        var oreGood = u.AsteroidClasses[rock.ClassKey].Yields[0].Good;
        ship.Cargo[oreGood] = ship.CargoCapacity;
        var chunks = new OreChunks();
        Assert.Equal(MineResult.CargoFull, chunks.TryFire(u, ship, sector, rock, InRange(rock)));

        ship.Cargo[oreGood] = ship.CargoCapacity - 1f;   // nur 1 Einheit Platz
        var pos = InRange(rock);
        chunks.Chunks.Add(new OreChunk { Good = oreGood, Units = 5f, Position = pos });
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
        Assert.Equal(MineResult.NotMinable, chunks.TryFire(u, ship, sector, inert, InRange(inert)));
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

public class IndividualFireTests
{
    static (Universe u, Ship ship, Sector sector, Asteroid rock) Setup()
    {
        var u = DemoUniverse.Create();
        var sector = u.Sectors[u.PlayerSector];
        return (u, u.Ships[u.PlayerShip], sector, sector.Asteroids.First(a => a.Minable));
    }

    [Fact]
    public void OneTrigger_IsOneShot_AndTheLaserMustReloadBeforeTheNext()
    {
        var (u, ship, sector, rock) = Setup();
        rock.Ore = rock.MaxOre = 10_000f;
        var pos = rock.Position + new Vec2(1, 0) * (rock.Radius + 20f);
        var chunks = new OreChunks(seed: 1);
        int shots = 0;
        chunks.Shot += (_, _) => shots++;

        Assert.True(chunks.Ready);
        Assert.Equal(MineResult.Fired, chunks.TryFire(u, ship, sector, rock, pos));
        Assert.False(chunks.Ready);
        for (int i = 0; i < 20; i++) Assert.Equal(MineResult.Cooldown, chunks.TryFire(u, ship, sector, rock, pos));   // "Halten" feuert nicht
        Assert.Equal(1, shots);

        chunks.Update(u, ship, pos, false, MiningRules.ShotInterval + 0.01f);
        Assert.True(chunks.Ready);
        Assert.Equal(MineResult.Fired, chunks.TryFire(u, ship, sector, rock, pos));
        Assert.Equal(2, shots);
    }

    [Fact]
    public void Recoil_IsConfigured()
    {
        Assert.True(MiningRules.RecoilImpulse > 0f);
        Assert.True(MiningRules.RecoilImpulse < 14f, "Rückstoß allein darf keinen Aufprallschaden verursachen");
    }

    // ───────── NPC-Miner benutzen dieselbe Schussregel ─────────

    static (Universe u, Ship miner, Sector sector, ResourceField field, List<MinerShot> shots) NpcSetup()
    {
        var u = DemoUniverse.Create();
        var sector = u.Sectors[new SectorId(1)];
        var field = sector.Fields[0];   // Metallasteroid-Gürtel
        var miner = u.Ships.Values.First(s => s.Role == ShipRole.Miner);
        miner.CargoCapacity = 1_000_000f;
        miner.Cargo.Clear();
        miner.Position = field.WorkPoint(miner.Id);
        miner.Order = new MineOrder(sector.Id, 0, new StationId(1));
        miner.MinePhase = MinePhase.Mining;
        miner.Credits = 0;
        var shots = new List<MinerShot>();
        u.Events.Raised += e => { if (e is MinerShot m) shots.Add(m); };
        return (u, miner, sector, field, shots);
    }

    [Fact]
    public void NpcMiner_FiresIndividualShots_AtMostOnePerTick_AtTheReloadRate()
    {
        var (u, miner, _, _, shots) = NpcSetup();
        var system = new MinerSystem();
        int perTickMax = 0, ticks = 400;
        for (int i = 0; i < ticks; i++)
        {
            int before = shots.Count;
            system.Step(u, 1);
            perTickMax = Math.Max(perTickMax, shots.Count - before);
        }
        Assert.Equal(1, perTickMax);
        double seconds = ticks * SimClock.TickInterval;
        Assert.InRange(shots.Count / seconds, 1.0 / MiningRules.ShotInterval * 0.85, 1.0 / MiningRules.ShotInterval * 1.05);
    }

    [Fact]
    public void NpcMiner_UsesTheSameHitChanceAndChunkSizes_AsThePlayer()
    {
        var (u, miner, _, _, shots) = NpcSetup();
        var system = new MinerSystem();
        for (int i = 0; i < 2_500 && shots.Count < 600; i++) system.Step(u, 1);

        double hitRate = shots.Count(x => x.Units > 0) / (double)shots.Count;
        Assert.InRange(hitRate, MiningRules.ChunkChance - 0.08, MiningRules.ChunkChance + 0.08);
        var sizes = shots.Where(x => x.Units > 0).Select(x => x.Units).ToList();
        Assert.True(sizes.Distinct().Count() >= 3, "NPC-Brocken sollen unterschiedlich groß sein");
        Assert.InRange(sizes.Average(), 2.0, 3.4);   // Erwartung ≈ 2,7 (kleine Reste kurz vor leeren Asteroiden drücken etwas)
    }

    [Fact]
    public void NpcMiner_OreIsConserved_BetweenAsteroidsAndCargo()
    {
        var (u, miner, sector, field, shots) = NpcSetup();
        float oreBefore = Belt.OreIn(sector, field);
        var system = new MinerSystem();
        for (int i = 0; i < 600; i++) system.Step(u, 1);

        float mined = miner.UsedVolume(u);
        Assert.True(mined > 50f, $"Es sollte nennenswert Erz abgebaut worden sein ({mined:0})");
        Assert.Equal(oreBefore - mined, Belt.OreIn(sector, field), 2);
        Assert.Equal(shots.Sum(x => x.Units), mined, 2);
    }

    [Fact]
    public void NpcMiner_ShootsTheSameRockUntilItIsEmpty_ThenPicksAnother()
    {
        var (u, miner, sector, field, shots) = NpcSetup();
        var system = new MinerSystem();
        for (int i = 0; i < 1_500; i++) system.Step(u, 1);
        var ids = shots.Select(x => x.Asteroid).Distinct().ToList();
        Assert.True(ids.Count >= 2, "Nach einem leeren Asteroiden wird der nächste gewählt");
        // Bis zum Wechsel feuert er stets auf denselben Felsen
        var runs = new List<int>();
        foreach (var x in shots) if (runs.Count == 0 || runs[^1] != x.Asteroid) runs.Add(x.Asteroid);
        Assert.Equal(runs.Count, runs.Distinct().Count());
    }

    [Fact]
    public void NpcMining_StopsWhenTheCargoIsFull_AndNeverOverfills()
    {
        var (u, miner, _, field, _) = NpcSetup();
        miner.CargoCapacity = 20f;
        var system = new MinerSystem();
        for (int i = 0; i < 2_000; i++) system.Step(u, 1);
        Assert.True(miner.UsedVolume(u) <= 20f + 1e-3f);
        Assert.NotEqual(MinePhase.Mining, miner.MinePhase);   // ist zur Lieferung losgefahren
    }

    [Fact]
    public void NpcMiningState_SurvivesSaveAndLoad()
    {
        var (u, miner, _, _, _) = NpcSetup();
        var system = new MinerSystem();
        for (int i = 0; i < 40; i++) system.Step(u, 1);
        var (loaded, _) = SpaceSim.Core.Persistence.SaveGameJson.Read(SpaceSim.Core.Persistence.SaveGameJson.Write(u, DateTimeOffset.UtcNow));
        var copy = loaded.Ships[miner.Id];
        Assert.Equal(miner.ShotTimer, copy.ShotTimer, 3);
        Assert.Equal(miner.MineAsteroid, copy.MineAsteroid);
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
        int missing = field.TargetAsteroids - Belt.AsteroidsOf(sector, field).Count();   // abgebaute plus evtl. bei der Erzeugung nicht untergebrachte
        Assert.True(missing >= 8);

        var respawn = new AsteroidRespawnSystem();   // nur das Respawn-System, damit keine NPC-Miner dazwischen abbauen
        for (int t = 0; t < field.RespawnTicks * 30; t += 10) { u.Tick = t; respawn.Step(u, 10); }

        Assert.Equal(field.TargetAsteroids, Belt.AsteroidsOf(sector, field).Count());
        Assert.Equal(missing, respawned.Count);
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

    static string AsLegacyJson(Universe u, int version)
    {
        var save = SpaceSim.Core.Persistence.UniverseSerializer.ToSave(u, DateTimeOffset.UtcNow);
        int GoodOf(string cls) => save.AsteroidClasses.First(c => c.Key == cls).Yields[0].Good;   // alte Formate kannten genau eine Sorte je Feld
        save.Version = version;
        foreach (var s in save.Sectors)
        {
            foreach (var f in s.Fields) { f.Good = f.Class != "" ? GoodOf(f.Class) : 0; f.Class = ""; }
            if (version == 1)
            {
                s.Asteroids = s.Rocks.Select(r => new Obstacle(r.Pos, r.BaseRadius)).ToList();
                s.Rocks = new();
                foreach (var f in s.Fields) { f.Amount = 2_000f; f.RegenPerTick = 0.5f; f.Capacity = 3_000f; }
            }
            else foreach (var r in s.Rocks) { r.Good = r.Class != "" ? GoodOf(r.Class) : 0; r.Class = ""; }
        }
        save.AsteroidClasses = new();
        save.NextAsteroidId = version == 1 ? 1 : save.NextAsteroidId;
        foreach (var ship in save.Ships.Where(sh => sh.Order is { Kind: "mine" }))
        {
            // Alte Aufträge nannten die Ware statt des Feldes
            var field = save.Sectors.First(sec => sec.Id == ship.Order!.A).Fields[ship.Order!.B];
            ship.Order!.B = field.Good;
        }
        return System.Text.Json.JsonSerializer.Serialize(save, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }

    [Fact]
    public void FormatV1_IsMigrated_OreMovesFromFieldIntoRocks()
    {
        var u = DemoUniverse.Create();
        var (loaded, _) = SpaceSim.Core.Persistence.SaveGameJson.Read(AsLegacyJson(u, version: 1));

        var alpha = loaded.Sectors[new SectorId(1)];
        float oreTotal = alpha.Fields.Sum(f => Belt.OreIn(alpha, f));
        Assert.InRange(oreTotal, 3_900f, 4_100f);                        // 2 Felder × 2000
        Assert.Contains(alpha.Asteroids, a => !a.Minable);               // Hindernisse ohne Erz bleiben
        Assert.True(loaded.NextAsteroidId > alpha.Asteroids.Max(a => a.Id));
        new SimulationRunner().Run(loaded, 2_000);                       // läuft weiter
    }

    [Fact]
    public void FormatV2_IsMigrated_SingleOreFieldsBecomeLegacyClasses_AndOrdersFollow()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 400);   // damit Miner Aufträge haben
        int miningOrders = u.Ships.Values.Count(s => s.Order is MineOrder);
        float oreBefore = u.Sectors.Values.Sum(sec => sec.Asteroids.Sum(a => a.Ore));

        var (loaded, _) = SpaceSim.Core.Persistence.SaveGameJson.Read(AsLegacyJson(u, version: 2));

        Assert.All(loaded.Sectors.Values.SelectMany(s => s.Fields), f => Assert.StartsWith("legacy_", f.ClassKey));
        Assert.All(loaded.AsteroidClasses.Values, c => Assert.Single(c.Yields));
        Assert.Equal(oreBefore, loaded.Sectors.Values.Sum(sec => sec.Asteroids.Sum(a => a.Ore)), 1);
        Assert.Equal(miningOrders, loaded.Ships.Values.Count(s => s.Order is MineOrder));
        new SimulationRunner().Run(loaded, 3_000);
        Assert.All(loaded.Ships.Values, s => Assert.True(float.IsFinite(s.Credits)));
    }
}

public class TradeActionTests
{
    [Fact]
    public void Buy_MovesGoodsAndMoney_AndRespectsLimits()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        var ship = u.Ships[u.PlayerShip];
        var st = u.Stations.Values.First(s => s.Market.Values.Any(m => m.Amount >= 20f && m.Price > 0f));
        var good = st.Market.First(kv => kv.Value.Amount >= 20f && kv.Value.Price > 0f).Key;
        var e = st.Market[good];
        ship.Cargo.Clear(); ship.Credits = 1_000_000f;
        float stock = e.Amount, price = e.Price, stCredits = st.Credits;

        var r = TradeActions.Buy(u, ship, st, good, 10f);

        Assert.Equal(10f, r.Units);
        Assert.Equal(BuyOutcome.Ok, r.Outcome);
        Assert.Equal(10f * price, r.Cost, 2);
        Assert.Equal(stock - 10f, e.Amount, 3);
        Assert.Equal(stCredits + r.Cost, st.Credits, 2);
        Assert.Equal(10f, ship.Cargo[good]);

        ship.Credits = price * 2.5f;
        var poor = TradeActions.Buy(u, ship, st, good, 10f);
        Assert.Equal(2f, poor.Units);
        Assert.Equal(BuyOutcome.NoCredits, poor.Outcome);

        ship.Credits = 0f;
        Assert.Equal(BuyOutcome.NoCredits, TradeActions.Buy(u, ship, st, good, 1f).Outcome);

        ship.Credits = 1_000_000f; ship.Cargo.Clear(); ship.CargoCapacity = 3f * u.Goods[good].VolumePerUnit;
        var tight = TradeActions.Buy(u, ship, st, good, 10f);
        Assert.Equal(3f, tight.Units);
        Assert.Equal(BuyOutcome.NoSpace, tight.Outcome);
    }

    [Fact]
    public void Buy_UnknownGood_IsRejected_AndSellSingleGoodOnlyTouchesThatGood()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        var ship = u.Ships[u.PlayerShip];
        var mine = u.Stations[new StationId(1)];
        ship.Cargo[DemoUniverse.Ore] = 30f;
        ship.Cargo[new GoodId(999)] = 5f;
        Assert.Equal(BuyOutcome.NotSold, TradeActions.Buy(u, ship, mine, new GoodId(999), 1f).Outcome);

        var sale = TradeActions.Sell(u, ship, mine, DemoUniverse.Ore, 10f);
        Assert.Equal(10f, sale.Sold[DemoUniverse.Ore], 3);
        Assert.Equal(20f, ship.Cargo[DemoUniverse.Ore], 3);
        Assert.Equal(5f, ship.Cargo[new GoodId(999)]);
    }

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
        var plant = u.Stations[new StationId(2)];   // Kupferhütte: kauft Kupfererz, aber kein Eisenerz
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

                if (step.Fire && step.Aim is { } rock) Chunks.TryFire(U, Ship, U.Sectors[U.PlayerSector], rock, Pos);
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
        beta.Fields.Add(new ResourceField { ClassKey = "metal_rock", Position = new Vec2(60, 60) });
        beta.Asteroids.Add(new Asteroid { Id = 9_999, Position = new Vec2(80, 60), BaseRadius = 5f, ClassKey = "metal_rock", Ore = 500, MaxOre = 500 });

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
