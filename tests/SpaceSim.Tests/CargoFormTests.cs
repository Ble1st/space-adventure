using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

/// <summary>Phase 3c: Laserstufe (<see cref="LaserShot.Roll"/> mit chunkChanceMult/chunkSizeMult) und Warenform im Laderaum (Ship.FreeVolumeFor, getrennte solid/liquid/any-Räume).</summary>
public class CargoFormTests
{
    // ───────── Laserstufe ─────────

    static AsteroidClass TestKlasse(float chance = 0.5f) => new()
    {
        Key = "test", Name = "Test", ChunkChance = chance,
        Yields = { new Yield(new GoodId(1), 1f) },
    };

    [Fact]
    public void Roll_Faktor1_IstIdentischZumBisherigenVerhalten()
    {
        var cls = TestKlasse();
        var rock = new Asteroid { Ore = 1_000_000f, MaxOre = 1_000_000f };
        var a = new DeterministicRng(42);
        var b = new DeterministicRng(42);
        for (int i = 0; i < 500; i++)
        {
            var r1 = LaserShot.Roll(a, cls, rock);              // alte Aufrufer: Default 1/1
            var r2 = LaserShot.Roll(b, cls, rock, 1f, 1f);      // explizit 1/1
            Assert.Equal(r1, r2);
        }
        Assert.Equal(a.State, b.State);   // dieselbe Rng-Folge, Determinismus bleibt
    }

    [Fact]
    public void Roll_ChanceFaktor2_ErhoehtDieTrefferrate()
    {
        var cls = TestKlasse(0.5f);
        var rock = new Asteroid { Ore = 1_000_000f, MaxOre = 1_000_000f };
        var r1 = new DeterministicRng(7);
        var r2 = new DeterministicRng(7);
        const int n = 2000;
        int hits1 = 0, hits2 = 0;
        for (int i = 0; i < n; i++)
        {
            if (LaserShot.Roll(r1, cls, rock).Hit) hits1++;
            if (LaserShot.Roll(r2, cls, rock, chunkChanceMult: 2f).Hit) hits2++;
        }
        Assert.InRange(hits1, 850, 1150);   // ≈ 50 % von 2000
        Assert.Equal(n, hits2);             // 0,5 × 2 = 1 (auf 1 begrenzt) → jeder Schuss trifft
        Assert.True(hits2 > hits1 * 1.5, $"Trefferrate soll mit Faktor 2 steigen ({hits1} → {hits2})");
    }

    [Fact]
    public void Roll_GroessenFaktor_SkaliertUndDeckeltAmAsteroiden()
    {
        var cls = TestKlasse(1f);   // jeder Schuss löst einen Brocken
        var rock = new Asteroid { Ore = 1_000_000f, MaxOre = 1_000_000f };
        var r1 = new DeterministicRng(99);
        var r2 = new DeterministicRng(99);
        const int n = 2000;
        double sum1 = 0, sum2 = 0;
        for (int i = 0; i < n; i++)
        {
            sum1 += LaserShot.Roll(r1, cls, rock).Units;
            sum2 += LaserShot.Roll(r2, cls, rock, chunkSizeMult: 2f).Units;
        }
        Assert.True(sum2 > sum1 * 1.8, $"Brocken sollen mit Faktor 2 ≈ doppelt so groß sein ({sum1 / n:0.00} → {sum2 / n:0.00})");

        // Deckel am Restbestand des Asteroiden bleibt wirksam
        var small = new Asteroid { Ore = 2f, MaxOre = 2f };
        var r3 = new DeterministicRng(5);
        for (int i = 0; i < 50; i++)
            Assert.True(LaserShot.Roll(r3, cls, small, chunkSizeMult: 10f).Units <= 2f);
    }

    [Fact]
    public void SpielerLaser_NutztDieLaserstufeDesSchiffs()
    {
        var u = DemoUniverse.Create();
        var ship = u.Ships[u.PlayerShip];
        var sector = u.Sectors[u.PlayerSector];
        var rock = sector.Asteroids.First(a => a.Minable);   // metal_rock: ChunkChance 0,5
        rock.Ore = rock.MaxOre = 1_000_000f;
        ship.CargoCapacity = 1_000_000f;
        var pos = rock.Position + new Vec2(1, 0) * (rock.Radius + 20f);

        (int shots, int hits) Fire(float chanceMult)
        {
            ship.Stats.LaserChunkChanceMult = chanceMult;
            var chunks = new OreChunks(seed: 123);
            int shots = 0, hits = 0;
            chunks.Shot += (_, r) => { shots++; if (r.Hit) hits++; };
            for (float t = 0; t < 30f; t += 0.05f) { chunks.TryFire(u, ship, sector, rock, pos); chunks.Update(u, ship, pos, tractor: false, 0.05f); }
            return (shots, hits);
        }

        var (shots1, hits1) = Fire(1f);
        var (shots2, hits2) = Fire(2f);
        Assert.Equal(shots1, shots2);            // gleiche Schusszahl (0,5 × 2 = 1 → jeder trifft)
        Assert.InRange(hits1, shots1 / 2 - 12, shots1 / 2 + 12);
        Assert.Equal(shots2, hits2);
    }

    [Fact]
    public void NpcMiner_NutztDieLaserstufeDesSchiffs()
    {
        var u = DemoUniverse.Create();
        var sector = u.Sectors[new SectorId(1)];
        var field = sector.Fields[0];   // metal_rock: ChunkChance 0,5
        var miner = u.Ships.Values.First(s => s.Role == ShipRole.Miner);
        MiningTests.ToLaser(u, miner);   // Phase 29: Einheitsschürfer tragen einen Bohrkopf
        miner.CargoCapacity = 1_000_000f;
        miner.Cargo.Clear();
        miner.Position = field.WorkPoint(miner.Id);
        miner.Order = new MineOrder(sector.Id, 0, new StationId(1));
        miner.MinePhase = MinePhase.Mining;
        miner.Stats.LaserChunkChanceMult = 2f;   // 0,5 × 2 = 1 → jeder Schuss trifft
        var shots = new List<MinerShot>();
        u.Events.Raised += e => { if (e is MinerShot m) shots.Add(m); };
        var system = new MinerSystem();
        for (int i = 0; i < 400 && shots.Count < 50; i++) system.Step(u, 1);
        Assert.True(shots.Count >= 50);
        Assert.All(shots, s => Assert.True(s.Units > 0f, "mit Laserstufe 2 trifft jeder Schuss"));
    }

    // ───────── Warenform im Laderaum ─────────

    /// <summary>Default-Welt plus Test-Klasse und drei Test-Modelle (solid 60, liquid 60, solid 30 + any 70); Schiffe der Startwelt bleiben unberührt.</summary>
    static WorldDef DefMitTestModellen()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.ShipClasses.Add(new ShipClassDef
        {
            Key = "test_hull", Name = "Testrumpf", Size = "S", BaseHull = 100, BaseMass = 10,
            Slots = new()
            {
                new ClassSlotDef { Type = "reactor", Size = "S" },
                new ClassSlotDef { Type = "engine", Size = "S" },
                new ClassSlotDef { Type = "cargo", Size = "S" },
                new ClassSlotDef { Type = "cargo", Size = "S" },
            },
        });
        def.ShipModels.Add(new ShipModelDef { Key = "solid_test", Class = "test_hull", Name = "Solid-Test", Fit = new() { "reactor_mk1", "chem_engine_s", "cargo_solid_s", "cargo_solid_s" } });
        def.ShipModels.Add(new ShipModelDef { Key = "tanker_test", Class = "test_hull", Name = "Tanker-Test", Fit = new() { "reactor_mk1", "chem_engine_s", "canister_bay_s", "canister_bay_s" } });
        def.ShipModels.Add(new ShipModelDef { Key = "mixed_test", Class = "test_hull", Name = "Mixed-Test", Fit = new() { "reactor_mk1", "chem_engine_s", "cargo_solid_s", "cargo_universal_70" } });
        return def;
    }

    static (Universe u, Ship ship) SchiffMitModell(string modelKey)
    {
        var u = WorldBuilder.Build(DefMitTestModellen());
        var ship = WorldBuilder.NewShip(u, u.NewShipId(), ShipRole.Trader, 1, new SectorId(1), 1_000_000f, 60f, modelKey);
        u.AddShip(ship);
        return (u, ship);
    }

    [Fact]
    public void ShipStats_SummiiertDieRaumformenNachHolds()
    {
        var u = WorldBuilder.Build(DefMitTestModellen());
        ShipStats StatsOf(string key)
        {
            var m = u.ShipModels[key];
            return ShipStats.Compute(u.ShipClasses[m.ClassKey], m.Fit.Select(k => k is null ? null : u.Modules[k]).ToList());
        }
        var solid = StatsOf("solid_test");
        Assert.Equal(60f, solid.Cargo);
        Assert.Equal(60f, solid.CargoSolid);
        Assert.Equal(0f, solid.CargoLiquid);
        Assert.Equal(0f, solid.CargoAny);
        var tanker = StatsOf("tanker_test");
        Assert.Equal(120f, tanker.Cargo);   // Runde 9: Kanisterraum 60 statt 30
        Assert.Equal(120f, tanker.CargoLiquid);
        Assert.Equal(0f, tanker.CargoSolid);
        var mixed = StatsOf("mixed_test");
        Assert.Equal(100f, mixed.Cargo);
        Assert.Equal(30f, mixed.CargoSolid);
        Assert.Equal(70f, mixed.CargoAny);
        var unit = StatsOf("unit_trader_100");
        Assert.Equal(unit.Cargo, unit.CargoAny);   // Einheitsmodelle: nur any
        Assert.Equal(0f, unit.CargoSolid);
        Assert.Equal(0f, unit.CargoLiquid);
    }

    [Fact]
    public void SchiffOhneModell_EinGesamtladeraumWieBisher()
    {
        var u = DemoUniverse.Create();
        var ship = new Ship { Id = new ShipId(999), Sector = new SectorId(1), CargoCapacity = 100f };
        var iron = u.FindGood("iron_ore")!.Id;
        var water = u.FindGood("water")!.Id;
        Assert.False(ship.HasModel);
        Assert.False(ship.HasSeparatedHolds);
        ship.Cargo[iron] = 40f;
        Assert.Equal(60f, ship.FreeVolumeFor(u, iron));
        Assert.Equal(60f, ship.FreeVolumeFor(u, water));   // Flüssigkeit wie bisher im selben Raum
        Assert.Equal(60f, ship.FreeUnits(u, water));
        Assert.Equal(ship.CargoCapacity - ship.UsedVolume(u), ship.FreeVolumeFor(u, water));
    }

    [Fact]
    public void NurSolidModul_WasserPasstNicht_EisenSchon()
    {
        var (u, ship) = SchiffMitModell("solid_test");
        var iron = u.FindGood("iron_ore")!.Id;
        var water = u.FindGood("water")!.Id;
        Assert.True(ship.HasSeparatedHolds);
        Assert.Equal(60f, ship.CargoCapacity);
        Assert.Equal(60f, ship.CapacityVolumeFor(u, iron));
        Assert.Equal(0f, ship.CapacityVolumeFor(u, water));
        Assert.Equal(60f, ship.FreeVolumeFor(u, iron));
        Assert.Equal(0f, ship.FreeVolumeFor(u, water));
        Assert.Equal(0f, ship.FreeUnits(u, water));

        var st = u.Stations.Values.First();
        st.Stock(water).Amount = 100f; st.Stock(water).Price = 10f;
        st.Stock(iron).Amount = 100f; st.Stock(iron).Price = 10f;
        Assert.Equal(0f, TradeActions.MaxBuyable(u, ship, st, water));
        Assert.Equal(BuyOutcome.NoSpace, TradeActions.Buy(u, ship, st, water, 10f).Outcome);
        Assert.Empty(ship.Cargo);
        Assert.True(TradeActions.MaxBuyable(u, ship, st, iron) > 0f);
        Assert.Equal(BuyOutcome.Ok, TradeActions.Buy(u, ship, st, iron, 10f).Outcome);
        Assert.Equal(10f, ship.Cargo[iron]);
    }

    [Fact]
    public void Tanker_WasserJa_EisenNein()
    {
        var (u, ship) = SchiffMitModell("tanker_test");
        var iron = u.FindGood("iron_ore")!.Id;
        var water = u.FindGood("water")!.Id;
        Assert.True(ship.HasSeparatedHolds);
        Assert.Equal(120f, ship.CapacityVolumeFor(u, water));   // Runde 9: Kanisterraum 60
        Assert.Equal(0f, ship.CapacityVolumeFor(u, iron));
        Assert.Equal(120f, ship.FreeVolumeFor(u, water));
        Assert.Equal(0f, ship.FreeVolumeFor(u, iron));

        var st = u.Stations.Values.First();
        st.Stock(water).Amount = 100f; st.Stock(water).Price = 10f;
        st.Stock(iron).Amount = 100f; st.Stock(iron).Price = 10f;
        Assert.Equal(BuyOutcome.NoSpace, TradeActions.Buy(u, ship, st, iron, 10f).Outcome);
        Assert.Equal(BuyOutcome.Ok, TradeActions.Buy(u, ship, st, water, 10f).Outcome);
        Assert.Equal(10f, ship.Cargo[water]);
        Assert.False(ship.Cargo.ContainsKey(iron));
    }

    [Fact]
    public void Universalraum_TeiltSichZwischenFestUndFluessig()
    {
        var (u, ship) = SchiffMitModell("mixed_test");   // solid 30 + any 70 = 100
        var iron = u.FindGood("iron_ore")!.Id;
        var water = u.FindGood("water")!.Id;
        Assert.Equal(100f, ship.FreeVolumeFor(u, iron));   // fest: solid+any = 100
        Assert.Equal(70f, ship.FreeVolumeFor(u, water));   // flüssig: liquid+any = 70

        ship.Cargo[iron] = 30f;   // solid-Anteil voll, any-Raum noch frei und teilbar
        Assert.Equal(70f, ship.FreeVolumeFor(u, iron));
        Assert.Equal(70f, ship.FreeVolumeFor(u, water));

        ship.Cargo[water] = 40f;   // any wird geteilt: 70−40 flüssig, gesamt 100−70
        Assert.Equal(30f, ship.FreeVolumeFor(u, water));
        Assert.Equal(30f, ship.FreeVolumeFor(u, iron));

        ship.Cargo.Clear();
        ship.Cargo[water] = 70f;   // any voll mit Flüssigem: Feststoffe haben nur noch den solid-Raum, begrenzt durch die Gesamtkapazität
        Assert.Equal(0f, ship.FreeVolumeFor(u, water));
        Assert.Equal(30f, ship.FreeVolumeFor(u, iron));

        ship.Cargo[iron] = 30f;   // Laderaum insgesamt voll
        Assert.Equal(0f, ship.FreeVolumeFor(u, iron));
        Assert.Equal(0f, ship.FreeVolumeFor(u, water));
    }

    [Fact]
    public void NurAnyModul_VerhaeltSichWieEinGesamtladeraum()
    {
        var u = DemoUniverse.Create();
        var ship = u.Ships[u.PlayerShip];   // unit_player: nur any 100
        Assert.True(ship.HasModel);
        Assert.False(ship.HasSeparatedHolds);
        Assert.Equal(100f, ship.Stats.CargoAny);
        Assert.Equal(0f, ship.Stats.CargoSolid);
        Assert.Equal(0f, ship.Stats.CargoLiquid);
        var water = u.FindGood("water")!.Id;
        var iron = u.FindGood("iron_ore")!.Id;
        ship.Cargo[iron] = 40f;
        Assert.Equal(60f, ship.FreeVolumeFor(u, water));
        Assert.Equal(ship.CargoCapacity - ship.UsedVolume(u), ship.FreeVolumeFor(u, water));
    }

    [Fact]
    public void Content_JedeWareHatForm_undJedesEinheitsmodellTraegtAlleWaren()
    {
        var u = DemoUniverse.Create();
        Assert.All(u.Goods.Values, g => Assert.True(g.Form is GoodForm.Solid or GoodForm.Liquid));
        Assert.Equal(3, u.Goods.Values.Count(g => g.Form == GoodForm.Liquid));   // water, hydrogen, propellant
        foreach (var key in new[] { "unit_trader_100", "unit_trader_60", "unit_miner_100", "unit_miner_60" })
        {
            var ship = WorldBuilder.NewShip(u, u.NewShipId(), ShipRole.Trader, 1, new SectorId(1), 0f, 100f, key);
            Assert.False(ship.HasSeparatedHolds, $"{key}: Einheitsmodell = Universalraum");
            foreach (var g in u.Goods.Values)
                Assert.Equal(ship.CargoCapacity, ship.CapacityVolumeFor(u, g.Id));   // jede Ware der Startwelt passt voll
        }
    }

    [Fact]
    public void TankerInDerWelt_PlantNurFluessigkeiten_undKeinSchiffUeberfuellt()
    {
        var def = DefMitTestModellen();
        def.Ships.Add(new ShipGroupDef { Role = "trader", Faction = 1, Sector = 1, Credits = 1500, Cargo = 60, Count = 1, Model = "tanker_test" });
        var u = WorldBuilder.Build(def);
        var tanker = u.Ships.Values.First(s => s.ModelKey == "tanker_test");
        Assert.Equal(120f, tanker.Stats.CargoLiquid);   // Runde 9: Kanisterraum 60
        Assert.Equal(0f, tanker.Stats.CargoSolid);

        var runner = new SimulationRunner();
        for (int i = 0; i < 50; i++)
        {
            runner.Run(u, 100);
            foreach (var s in u.Ships.Values)
            {
                // Frachtform-Invariante: usedSolid ≤ solid+any, usedLiquid ≤ liquid+any, gesamt ≤ solid+liquid+any
                Assert.True(s.UsedVolume(u, GoodForm.Solid) <= s.CapacityVolumeFor(GoodForm.Solid) + 1e-3f,
                    $"Schiff {s.Id}: Feststoff {s.UsedVolume(u, GoodForm.Solid):0.#} > {s.CapacityVolumeFor(GoodForm.Solid):0.#}");
                Assert.True(s.UsedVolume(u, GoodForm.Liquid) <= s.CapacityVolumeFor(GoodForm.Liquid) + 1e-3f,
                    $"Schiff {s.Id}: Flüssig {s.UsedVolume(u, GoodForm.Liquid):0.#} > {s.CapacityVolumeFor(GoodForm.Liquid):0.#}");
                Assert.True(s.UsedVolume(u) <= s.CargoCapacity + 1e-3f, $"Schiff {s.Id}: Laderaum überfüllt");
            }
            if (tanker.Order is TradeOrder to)
                Assert.Equal(GoodForm.Liquid, u.Goods[to.Good].Form);   // Tanker handelt nur Flüssigkeiten
        }
        Assert.All(tanker.Cargo.Keys, g => Assert.Equal(GoodForm.Liquid, u.Goods[g].Form));
    }
}
