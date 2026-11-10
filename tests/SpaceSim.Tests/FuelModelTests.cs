using System.Text.Json.Nodes;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 4a: Treibstoff-Datenmodell — Ware propellant (liquid, ans Ende der goods-Liste), baseFuel der Klassen,
/// fuel-Liste der Antriebsmodule (Validierung), fuelCapacity-Stat, ShipStats.FuelCapacity/FuelTypes, Ship.Fuel mit
/// vollem Tank bei Neubau, Save v5 mit Migration v4→v5. Noch OHNE Verbrauch: Travel.Start bleibt Credit-Pauschale.
/// </summary>
public class FuelModelTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    static WorldDef DefaultDef() => WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());

    /// <summary>Wirft die erwartete WorldFormatException (mit Namen) für eine Mutation der Default-Welt.</summary>
    static WorldFormatException Invalid(Action<WorldDef> mutate, string enthaelt)
    {
        var def = DefaultDef();
        mutate(def);
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
        Assert.Contains(enthaelt, ex.Message);
        return ex;
    }

    /// <summary>ShipStats eines Modells der geladenen Welt (Klasse + Fit auflösen).</summary>
    static ShipStats StatsOf(Universe u, string modelKey)
    {
        var model = u.ShipModels[modelKey];
        var cls = u.ShipClasses[model.ClassKey];
        var fit = model.Fit.Select(k => k is null ? null : u.Modules[k]).ToList();
        return ShipStats.Compute(cls, fit);
    }

    /// <summary>Demotet einen aktuellen v5-Save auf v4: Version 4, Fuel der Schiffe, propellant-Ware und die neuen Content-Felder (Fuel der Module, BaseFuel der Klassen) entfernt.</summary>
    static string ToV4Json(string v5Json)
    {
        var node = JsonNode.Parse(v5Json)!.AsObject();
        node["Version"] = 4;
        var goods = node["Goods"]!.AsArray();
        goods.RemoveAt(goods.Count - 1);   // propellant war die letzte Ware
        foreach (var m in node["Modules"]!.AsArray()) m!.AsObject().Remove("Fuel");
        foreach (var c in node["ShipClasses"]!.AsArray()) c!.AsObject().Remove("BaseFuel");
        foreach (var sh in node["Ships"]!.AsArray()) sh!.AsObject().Remove("Fuel");
        return node.ToJsonString();
    }

    [Fact]
    public void Propellant_LiquidAnsEndeDerWarenliste_NochOhneMarkt()
    {
        var u = DemoUniverse.Create();
        var p = u.Goods[DemoUniverse.Propellant];
        Assert.Equal("propellant", p.Key);
        Assert.Equal("Treibstoff", p.Name);
        Assert.Equal(GoodForm.Liquid, p.Form);
        Assert.Equal(1f, p.VolumePerUnit);
        Assert.Equal(u.Goods.Keys.Max(k => k.Value), p.Id.Value);   // ans Ende angehängt: GoodIds der übrigen Waren stabil
        Assert.InRange(p.BasePrice, u.Goods[DemoUniverse.Water].BasePrice, u.Goods[DemoUniverse.Hydrogen].BasePrice * 1.2f);
        // noch ohne Erzeuger/Verbraucher und ohne Stationsmarkt (kommt in Phase 4b)
        Assert.DoesNotContain(u.StationTypes.Values.SelectMany(t => t.Recipes).SelectMany(r => r.Inputs.Keys.Concat(r.Outputs.Keys)), g => g == p.Id);
        Assert.DoesNotContain(u.Stations.Values, st => st.Market.ContainsKey(p.Id));
    }

    [Fact]
    public void ChemEngineS_NenntPropellant_EinheitsklassenHabenBaseFuel200()
    {
        var u = DemoUniverse.Create();
        var f = Assert.Single(u.Modules["chem_engine_s"].FuelTypes);
        Assert.Equal(new FuelType(DemoUniverse.Propellant, 1f), f);
        Assert.Equal(200f, u.ShipClasses["unit_trader"].BaseFuel);
        Assert.Equal(200f, u.ShipClasses["unit_miner"].BaseFuel);
    }

    [Fact]
    public void FuelValidierung_UnbekannteWare_WirftMitModulname()
        => Invalid(d => d.Modules[1].Fuel.Add(new FuelEntryDef { Good = "unobtainium", Efficiency = 1f }),
            "Modul 'chem_engine_s': unbekannte Ware 'unobtainium' in fuel");

    [Fact]
    public void FuelValidierung_WareNichtLiquid_WirftMitModulname()
        => Invalid(d => d.Modules[1].Fuel.Add(new FuelEntryDef { Good = "steel", Efficiency = 1f }),
            "Modul 'chem_engine_s': Ware 'steel' in fuel ist nicht flüssig");

    [Fact]
    public void FuelValidierung_EfficiencyNichtPositiv_WirftMitModulname()
        => Invalid(d => d.Modules[1].Fuel.Add(new FuelEntryDef { Good = "water", Efficiency = 0f }),
            "Modul 'chem_engine_s': efficiency 0 für Ware 'water' in fuel muss > 0 sein");

    [Fact]
    public void FuelValidierung_WareDoppelt_WirftMitModulname()
        => Invalid(d => d.Modules[1].Fuel.Add(new FuelEntryDef { Good = "propellant", Efficiency = 2f }),
            "Modul 'chem_engine_s': Ware 'propellant' doppelt in fuel");

    [Fact]
    public void ShipStats_Einheitsmodelle_Tank200_PropellantEfficiency1()
    {
        var u = DemoUniverse.Create();
        foreach (var key in new[] { "unit_trader_100", "unit_trader_60", "unit_miner_100", "unit_miner_60", "unit_player" })
        {
            var st = StatsOf(u, key);
            Assert.Equal(200f, st.FuelCapacity);
            var f = Assert.Single(st.FuelTypes);
            Assert.Equal(new FuelType(DemoUniverse.Propellant, 1f), f);
        }
    }

    [Fact]
    public void ShipStats_FuelCapacityAusTankModul_MehrereAntriebeVereinigenMitHoehererEfficiency()
    {
        var def = DefaultDef();
        def.Modules.Add(new ModuleDef { Key = "tank_s", Slot = "utility", Size = "S", Mass = 1, Stats = new() { ["fuelCapacity"] = 50f } });
        def.Modules.Add(new ModuleDef
        {
            Key = "hybrid_engine_test", Slot = "engine", Size = "S", PowerDraw = 3, Mass = 3,
            Stats = new() { ["thrust"] = 100 },
            Fuel = new() { new FuelEntryDef { Good = "propellant", Efficiency = 2f }, new FuelEntryDef { Good = "water", Efficiency = 0.5f } },
        });
        def.ShipClasses.Add(new ShipClassDef
        {
            Key = "test_hull", Name = "Test", Size = "S", BaseHull = 100, BaseMass = 10, BaseFuel = 200,
            Slots = new()
            {
                new ClassSlotDef { Type = "reactor", Size = "S" }, new ClassSlotDef { Type = "engine", Size = "S" },
                new ClassSlotDef { Type = "engine", Size = "S" }, new ClassSlotDef { Type = "utility", Size = "S" },
            },
        });
        def.ShipModels.Add(new ShipModelDef { Key = "test_ship", Class = "test_hull", Name = "Test", Fit = new() { "reactor_mk1", "chem_engine_s", "hybrid_engine_test", "tank_s" } });
        var u = WorldBuilder.Build(def);

        var st = StatsOf(u, "test_ship");
        Assert.Equal(250f, st.FuelCapacity);   // baseFuel 200 + fuelCapacity 50
        Assert.Equal(2, st.FuelTypes.Count);
        // zwei Antriebe: Vereinigung je Ware, höchste efficiency gewinnt (propellant 1.0 vs 2.0)
        Assert.Equal(new FuelType(DemoUniverse.Propellant, 2f), st.FuelTypes.First(f => f.Good == DemoUniverse.Propellant));
        Assert.Equal(new FuelType(u.FindGood("water")!.Id, 0.5f), st.FuelTypes.First(f => f.Good == u.FindGood("water")!.Id));
    }

    [Fact]
    public void NeueSchiffe_StartenMitVollemTankPropellant()
    {
        var u = DemoUniverse.Create();
        // Startwelt: alle Schiffe haben ein Modell und einen vollen Tank
        Assert.All(u.Ships.Values, s => Assert.NotEmpty(s.ModelKey));
        foreach (var s in u.Ships.Values)
        {
            Assert.Equal(200f, s.Stats.FuelCapacity);
            Assert.Equal(200f, s.FuelAmount(DemoUniverse.Propellant));
            Assert.Equal(200f, s.TotalFuel);
            Assert.Equal(0f, s.FuelSpace);
        }
        // WorldBuilder.NewShip (explizites und automatisch gewähltes Modell)
        var ship = WorldBuilder.NewShip(u, u.NewShipId(), ShipRole.Trader, 1, new SectorId(1), 1000f, 100f, "unit_trader_100");
        Assert.Equal(200f, ship.FuelAmount(DemoUniverse.Propellant));
        var auto = WorldBuilder.NewShip(u, u.NewShipId(), ShipRole.Miner, 1, new SectorId(1), 1000f, 60f);
        Assert.Equal("unit_miner_60", auto.ModelKey);
        Assert.Equal(200f, auto.FuelAmount(DemoUniverse.Propellant));
        // Universe.AddShip füllt einen leeren Tank (Fit wie ein echter Ersteller gesetzt)
        var manual = new Ship { Id = u.NewShipId(), Role = ShipRole.Trader, ModelKey = "unit_trader_100", Fit = new List<string?>(u.ShipModels["unit_trader_100"].Fit) };
        u.AddShip(manual);
        Assert.Equal(200f, manual.FuelAmount(DemoUniverse.Propellant));
        Assert.Equal(200f, manual.Stats.FuelCapacity);
    }

    [Fact]
    public void SchiffOhneModell_HatLeerenTank_Kapazitaet0()
    {
        var def = DefaultDef();
        def.Ships.Add(new ShipGroupDef { Role = "fighter", Faction = 1, Sector = 1, Cargo = 100, Count = 1 });
        var u = WorldBuilder.Build(def);
        var fighter = u.Ships.Values.First(s => s.Role == ShipRole.Fighter);
        Assert.False(fighter.HasModel);
        Assert.Empty(fighter.Fuel);
        Assert.Equal(0f, fighter.Stats.FuelCapacity);
        Assert.Equal(0f, fighter.TotalFuel);
        Assert.Equal(0f, fighter.FuelSpace);
        Assert.Equal(0f, fighter.FuelAmount(DemoUniverse.Propellant));
    }

    [Fact]
    public void FleetSystem_Expand_NeueSchiffeHabenVollenTank()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 20_000);   // Flottenwachstum erzeugt neue Schiffe (Ids ab 1000)
        Assert.Contains(u.Ships.Values, s => s.Id.Value >= 1000);
        Assert.All(u.Ships.Values.Where(s => s.HasModel), s =>
        {
            Assert.Equal(s.Stats.FuelCapacity, s.TotalFuel);
            Assert.Equal(s.Stats.FuelCapacity, s.FuelAmount(DemoUniverse.Propellant));
        });
    }

    [Fact]
    public void TravelStart_BleibtCreditPauschale_TankWirdNichtAngetastet()
    {
        var u = DemoUniverse.Create();
        var ship = u.Ships.Values.First(s => s.Role == ShipRole.Trader && s.Transit is null);
        float fuelBefore = ship.TotalFuel;
        float creditsBefore = ship.Credits;
        var to = u.Sectors.Keys.First(x => x != ship.Sector);
        Travel.Start(u, ship, to, new Vec2(0, 0));
        Assert.NotNull(ship.Transit);
        float distance = ship.Transit.Legs.Sum(l => Vec2.Distance(l.From, l.To));
        float charge = MathF.Min(creditsBefore, distance * u.Rules.FuelCostPerUnit);
        Assert.Equal(creditsBefore - charge, ship.Credits, 3);
        Assert.Equal(fuelBefore, ship.TotalFuel);   // Phase 4a: noch kein Treibstoffverbrauch
    }

    [Fact]
    public void SaveLoad_V5_Roundtrip_IdentischInklHashFuelUndContent()
    {
        Assert.Equal(5, SaveGame.CurrentVersion);
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 2_000);
        var before = UniverseHash.Compute(u);

        var json = SaveGameJson.Write(u, Now);
        Assert.Contains("\"Fuel\":{", json);
        var (loaded, _) = SaveGameJson.Read(json);

        Assert.Equal(before, UniverseHash.Compute(loaded));
        foreach (var s in loaded.Ships.Values)
        {
            var orig = u.Ships[s.Id];
            Assert.Equal(orig.Fuel.Count, s.Fuel.Count);
            foreach (var (g, a) in orig.Fuel) Assert.Equal(a, s.Fuel[g], 3);
            Assert.Equal(200f, s.FuelAmount(DemoUniverse.Propellant));
            Assert.Equal(200f, s.Stats.FuelCapacity);
        }
        // Content liegt im Save: propellant, fuel des Antriebs, baseFuel der Klassen
        var prop = loaded.Goods.Values.Single(g => g.Key == "propellant");
        Assert.Equal(DemoUniverse.Propellant, prop.Id);
        Assert.Equal(GoodForm.Liquid, prop.Form);
        Assert.Equal(new FuelType(prop.Id, 1f), Assert.Single(loaded.Modules["chem_engine_s"].FuelTypes));
        Assert.Equal(200f, loaded.ShipClasses["unit_trader"].BaseFuel);
        Assert.Equal(200f, loaded.ShipClasses["unit_miner"].BaseFuel);
        // Stationen handeln propellant noch nicht
        Assert.DoesNotContain(loaded.Stations.Values, st => st.Market.ContainsKey(prop.Id));
    }

    [Fact]
    public void Migration_V4ToV5_PropellantNachgereicht_SchiffeVollerTank_VerhaltenGleich()
    {
        var u = DemoUniverse.Create();
        var v4Json = ToV4Json(SaveGameJson.Write(u, Now));
        Assert.Contains("\"Version\":4", v4Json);
        Assert.DoesNotContain("\"Fuel\"", v4Json);
        Assert.DoesNotContain("propellant", v4Json);

        var (loaded, _) = SaveGameJson.Read(v4Json);

        // propellant ans Ende der Warenliste, bestehende GoodIds unverschoben
        var prop = loaded.Goods.Values.Single(g => g.Key == "propellant");
        Assert.Equal(23, prop.Id.Value);
        Assert.Equal(GoodForm.Liquid, prop.Form);
        Assert.All(loaded.Goods.Values.Where(g => g.Key != "propellant"), g => Assert.True(g.Id.Value < 23));
        // geänderter Slot-Content nachgereicht
        Assert.Equal(new FuelType(prop.Id, 1f), Assert.Single(loaded.Modules["chem_engine_s"].FuelTypes));
        Assert.Equal(200f, loaded.ShipClasses["unit_trader"].BaseFuel);
        // Stationsmärkte unberührt: propellant hat keinen Markt
        Assert.DoesNotContain(loaded.Stations.Values, st => st.Market.ContainsKey(prop.Id));
        // jedes Schiff mit Modell hat einen vollen Tank propellant
        foreach (var s in loaded.Ships.Values)
        {
            Assert.NotEmpty(s.ModelKey);
            Assert.Equal(200f, s.Stats.FuelCapacity);
            Assert.Equal(200f, s.FuelAmount(prop.Id));
            Assert.Equal(0f, s.FuelSpace);
        }

        // Verhalten gleich: 2000 Ticks ab migriertem Save == 2000 Ticks ab frisch erzeugter Welt gleicher Seed
        var fresh = DemoUniverse.Create();
        var runner = new SimulationRunner();
        runner.Run(loaded, 2_000);
        runner.Run(fresh, 2_000);
        Assert.Equal(UniverseHash.Compute(fresh), UniverseHash.Compute(loaded));

        // Roundtrip des migrierten Save: Hash stabil
        var (again, _) = SaveGameJson.Read(SaveGameJson.Write(loaded, Now));
        Assert.Equal(UniverseHash.Compute(loaded), UniverseHash.Compute(again));
    }

    /// <summary>Kleiner nachgebauter v2-Spielstand (Altformat: Felder/Asteroiden mit einzelner Erzsorte, keine Slot-Sektionen, keine Schiffs-Modelkeys).</summary>
    const string V2Json = """
    {
      "Version": 2, "SavedAtUtc": "2026-11-08T12:00:00+00:00", "Tick": 100, "Seed": 42, "RngState": 123,
      "LastStationBuildTick": -1000000, "NextShipId": 1000, "NextStationId": 1000, "NextAsteroidId": 100,
      "PlayerSector": 1, "PlayerShip": 100,
      "Goods": [ { "Id": 1, "Name": "Erz", "BasePrice": 10, "Volume": 1 }, { "Id": 2, "Name": "Energie", "BasePrice": 8, "Volume": 1 } ],
      "AsteroidClasses": [],
      "StationTypes": [ { "Key": "mine", "Name": "Mine", "BuildCost": 4000, "Upkeep": 0.05, "Income": 0, "Recipes": [ { "Ticks": 5, "In": {}, "Out": { "1": 1 } } ] } ],
      "Factions": [ { "Id": 1, "Name": "Alpha", "Relations": {}, "Territory": [1, 2] } ],
      "Sectors": [
        { "Id": 1, "Name": "Alpha", "Owner": 1, "Map": { "X": 0, "Y": 0 }, "Radius": 500, "Gates": [1], "Stations": [1],
          "Fields": [ { "Good": 1, "Pos": { "X": 60, "Y": 60 }, "TargetAsteroids": 8, "RespawnTicks": 120, "Amount": 500, "RegenPerTick": 0, "Capacity": 500 } ],
          "Rocks": [ { "Id": 1, "Pos": { "X": 50, "Y": 50 }, "BaseRadius": 4, "Good": 1, "Ore": 100, "MaxOre": 100 } ] },
        { "Id": 2, "Name": "Beta", "Owner": 1, "Map": { "X": 400, "Y": 0 }, "Radius": 500, "Gates": [2], "Stations": [], "Fields": [], "Rocks": [] }
      ],
      "Gates": [
        { "Id": 1, "Sector": 1, "Pos": { "X": 90, "Y": -70 }, "Target": 2, "Open": true },
        { "Id": 2, "Sector": 2, "Pos": { "X": -90, "Y": 0 }, "Target": 1, "Open": true }
      ],
      "Stations": [ { "Id": 1, "Type": "mine", "Name": "Mine", "Sector": 1, "Owner": 1, "Pos": { "X": -60, "Y": -40 }, "Credits": 10000, "Essential": true, "BelowLimitSince": -1, "Recipes": [ { "Ticks": 5, "In": {}, "Out": { "1": 1 } } ], "Market": { "1": { "Amount": 50, "Target": 100, "Price": 5 } } } ],
      "Ships": [
        { "Id": 1, "Faction": 1, "Role": "Trader", "CargoCapacity": 100, "Credits": 500, "Hull": 100, "Sector": 1, "Pos": { "X": 0, "Y": 0 }, "Phase": "Idle", "MinePhase": "Idle", "NextThinkTick": 0, "IdleSince": -1, "ShotTimer": 0, "MineAsteroid": 0, "Cargo": {} },
        { "Id": 100, "Faction": 1, "Role": "Player", "CargoCapacity": 100, "Credits": 500, "Hull": 100, "Sector": 1, "Pos": { "X": 0, "Y": 0 }, "Phase": "Idle", "MinePhase": "Idle", "NextThinkTick": 0, "IdleSince": -1, "ShotTimer": 0, "MineAsteroid": 0, "Cargo": {} }
      ]
    }
    """;

    [Fact]
    public void Kettenmigration_V2ToV5_Laedt_undJedesSchiffHatVollenTank()
    {
        var (u, _) = SaveGameJson.Read(V2Json);

        // v2 → v3 → v4 → v5 durchlaufen: Modelle zugewiesen, Slot-Content und propellant nachgereicht
        var prop = u.Goods.Values.Single(g => g.Key == "propellant");
        Assert.Equal(3, prop.Id.Value);   // ans Ende der 2 alten Waren angehängt
        Assert.Equal(GoodForm.Liquid, prop.Form);
        Assert.Equal(new FuelType(prop.Id, 1f), Assert.Single(u.Modules["chem_engine_s"].FuelTypes));
        Assert.Equal(200f, u.ShipClasses["unit_trader"].BaseFuel);
        Assert.All(u.Ships.Values, s => Assert.NotEmpty(s.ModelKey));
        foreach (var s in u.Ships.Values)
        {
            Assert.Equal(200f, s.Stats.FuelCapacity);
            Assert.Equal(200f, s.FuelAmount(prop.Id));
            Assert.Equal(200f, s.TotalFuel);
            Assert.Equal(0f, s.FuelSpace);
        }

        // Kette ist schreibbar: Save → Load → gleicher Hash
        var (again, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(again));
    }
}
