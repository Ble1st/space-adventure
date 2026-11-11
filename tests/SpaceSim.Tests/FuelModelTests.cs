using System.Text.Json.Nodes;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 4a: Treibstoff-Datenmodell — Ware propellant (liquid, ans Ende der goods-Liste), baseFuel der Klassen,
/// fuel-Liste der Antriebsmodule (Validierung), fuelCapacity-Stat, ShipStats.FuelCapacity/FuelTypes, Ship.Fuel mit
/// vollem Tank bei Neubau, Save v5 mit Migration v4→v5. Verbrauch/Tanken/Notkauf: siehe FuelEconomyTests (Phase 4b).
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

    /// <summary>
    /// Demotet einen aktuellen Save (über <see cref="WalletTests.ToV6Json"/>) auf v5: Version 5, die Waren aus Phase 5a (Bausatzstufen, Modul-Waren; Ids ab 24) entfernt,
    /// ship_kit_light heißt wieder ship_kit, die neuen Content-Felder (Kit der Klassen, Name der Module, Color der Slotarten,
    /// Stocks der Stationstypen) entfernt. Stationen und Märkte bleiben (sie verweisen auf dieselben Ids, die die Migration wieder anhängt).
    /// </summary>
    internal static string ToV5Json(string v6Json)
    {
        var node = JsonNode.Parse(WalletTests.ToV6Json(v6Json))!.AsObject();
        node["Version"] = 5;
        var goods = node["Goods"]!.AsArray();
        for (int i = goods.Count - 1; i >= 0; i--) if ((int)goods[i]!["Id"]! >= DemoUniverse.ShipKitMedium.Value) goods.RemoveAt(i);
        foreach (var g in goods) if ((string?)g!["Key"] == "ship_kit_light") { g["Key"] = "ship_kit"; g["Name"] = "Schiffsbausatz"; }
        node["Rules"]!["ShipKitGoodKey"] = "ship_kit";
        foreach (var c in node["ShipClasses"]!.AsArray()) c!.AsObject().Remove("Kit");
        foreach (var m in node["Modules"]!.AsArray()) m!.AsObject().Remove("Name");
        foreach (var t in node["SlotTypes"]!.AsArray()) t!.AsObject().Remove("Color");
        foreach (var t in node["StationTypes"]!.AsArray()) t!.AsObject().Remove("Stocks");
        return node.ToJsonString();
    }

    /// <summary>Demotet einen aktuellen Save auf v4 (über <see cref="ToV5Json"/>): Version 4, Fuel der Schiffe, propellant-Ware und die neuen Content-Felder (Fuel der Module, BaseFuel der Klassen) entfernt.</summary>
    internal static string ToV4Json(string v6Json)
    {
        var node = JsonNode.Parse(ToV5Json(v6Json))!.AsObject();
        node["Version"] = 4;
        var goods = node["Goods"]!.AsArray();
        goods.RemoveAt(goods.Count - 1);   // propellant war die letzte Ware vor Phase 5a
        foreach (var m in node["Modules"]!.AsArray()) m!.AsObject().Remove("Fuel");
        foreach (var c in node["ShipClasses"]!.AsArray()) c!.AsObject().Remove("BaseFuel");
        foreach (var sh in node["Ships"]!.AsArray()) sh!.AsObject().Remove("Fuel");
        return node.ToJsonString();
    }

    [Fact]
    public void Propellant_LiquidAnsEndeDerWarenliste_RaffinerieErzeugtUndHandeltIhn()
    {
        var u = DemoUniverse.Create();
        var p = u.Goods[DemoUniverse.Propellant];
        Assert.Equal("propellant", p.Key);
        Assert.Equal("Treibstoff", p.Name);
        Assert.Equal(GoodForm.Liquid, p.Form);
        Assert.Equal(1f, p.VolumePerUnit);
        // ans Ende angehängt: GoodIds der übrigen Waren stabil; dahinter folgen nur die Waren aus Phase 5a (Bausatzstufen, Modul-Waren)
        Assert.Equal(23, p.Id.Value);
        Assert.All(u.Goods.Values.Where(g => g.Id.Value > p.Id.Value), g => Assert.True(g.Key.StartsWith("ship_kit_") || u.Modules.ContainsKey(g.Key), g.Key));
        Assert.InRange(p.BasePrice, u.Goods[DemoUniverse.Water].BasePrice, u.Goods[DemoUniverse.Hydrogen].BasePrice * 1.2f);
        // Phase 4b: Treibstoffraffinerie (3 Wasserstoff + 1 Energie → 4 Treibstoff); zwei Startwelt-Raffinerien führen propellant im Markt
        var type = Assert.Single(u.StationTypes.Values, t => t.Key == "fuel_refinery");
        Assert.Equal(2, type.Recipes.Count);
        var r = type.Recipes[0];
        Assert.Equal(3f, r.Inputs[DemoUniverse.Hydrogen]);
        Assert.Equal(1f, r.Inputs[DemoUniverse.Energy]);
        Assert.Equal(4f, r.Outputs[p.Id]);
        // Grundproduktion (Gasförderung) ohne Eingänge: Treibstoff versiegt nicht, wenn die Wasserstoffkette stockt
        var base_ = type.Recipes[1];
        Assert.Empty(base_.Inputs);
        Assert.Equal(1f, base_.Outputs[p.Id]);
        var refineries = u.Stations.Values.Where(st => st.TypeKey == "fuel_refinery").OrderBy(st => st.Id.Value).ToList();
        Assert.Equal(2, refineries.Count);
        Assert.All(refineries, st => Assert.True(st.Market.ContainsKey(p.Id)));
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
        // Phase 8: Bauschiffe (Foreman, Klasse mit eigenem Rumpftank) starten ebenfalls voll, nur mit ihrer eigenen Kapazität
        // Phase 11: Polizei- und Kaperschiffe ebenso (eigene Klassen mit eigenem Rumpftank)
        Assert.All(u.Ships.Values.Where(s => s.Role is ShipRole.Builder or ShipRole.Police or ShipRole.Pirate), s => Assert.Equal(s.Stats.FuelCapacity, s.FuelAmount(DemoUniverse.Propellant)));
        foreach (var s in u.Ships.Values.Where(s => s.Role is not (ShipRole.Builder or ShipRole.Police or ShipRole.Pirate)))
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
    public void FleetSystem_Expand_NeueSchiffeStartenMitVollemTank_TankBleibtInGrenzen()
    {
        var u = DemoUniverse.Create();
        var spawnFuel = new List<float>();
        u.Events.Raised += e =>
        {
            if (e is ShipSpawned sp && u.Ships.TryGetValue(sp.Ship, out var s) && s.HasModel)
                spawnFuel.Add(s.TotalFuel - s.Stats.FuelCapacity);
        };
        new SimulationRunner().Run(u, 20_000);   // Flottenwachstum erzeugt neue Schiffe (Ids ab 1000)
        Assert.Contains(u.Ships.Values, s => s.Id.Value >= 1000);
        Assert.NotEmpty(spawnFuel);
        Assert.All(spawnFuel, f => Assert.Equal(0f, f));   // bei Erzeugung voller Tank (Phase 4a; Phase 6: Klassen mit anderem Tank); danach echter Verbrauch (Phase 4b)
        // nach 20k Ticks: Tank nie negativ und nie über Kapazität, nur zulässige Treibstoffwaren an Bord
        Assert.All(u.Ships.Values.Where(s => s.HasModel), s =>
        {
            Assert.True(s.TotalFuel >= -0.001f && s.TotalFuel <= s.Stats.FuelCapacity + 0.001f,
                $"Tank {s.TotalFuel} außerhalb [0, {s.Stats.FuelCapacity}]");
            Assert.All(s.Fuel.Keys, g => Assert.Contains(s.Stats.FuelTypes, f => f.Good == g));
        });
    }

    [Fact]
    public void SaveLoad_V5_Roundtrip_IdentischInklHashFuelUndContent()
    {
        Assert.Equal(15, SaveGame.CurrentVersion);   // v15: Befehle/Verbände/Hangar/Leihschiff (Phase 12); v14: Polizei/Fahndung/Piraten (Phase 11); v13: Gate-Zugang/Sektorkontrolle (Phase 10); v12: Akteure (Phase 9); v11: Bau/Baustellen/Lagerhaus (Phase 8); v10: Güteklassen/Upgrades (Phase 7); v9: Multirole (Phase 6); v8 (Phase 5c); v7: Spielerkasse (Phase 5b); v6 (Phase 5a); Fuel kam mit v5
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
            Assert.Equal(orig.TotalFuel, s.TotalFuel, 3);   // Phase 4b: Tanks werden geleert, Roundtrip bleibt exakt
            Assert.Equal(orig.Stats.FuelCapacity, s.Stats.FuelCapacity);   // Phase 6: gekaufte Klassen haben andere Tanks (Einheitsmodelle 200)
        }
        // Content liegt im Save: propellant, fuel des Antriebs, baseFuel der Klassen
        var prop = loaded.Goods.Values.Single(g => g.Key == "propellant");
        Assert.Equal(DemoUniverse.Propellant, prop.Id);
        Assert.Equal(GoodForm.Liquid, prop.Form);
        Assert.Equal(new FuelType(prop.Id, 1f), Assert.Single(loaded.Modules["chem_engine_s"].FuelTypes));
        Assert.Equal(200f, loaded.ShipClasses["unit_trader"].BaseFuel);
        Assert.Equal(200f, loaded.ShipClasses["unit_miner"].BaseFuel);
        // die Raffinerien der Startwelt handeln propellant (Phase 4b)
        Assert.All(loaded.Stations.Values.Where(st => st.TypeKey == "fuel_refinery"), st => Assert.True(st.Market.ContainsKey(prop.Id)));
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
        Assert.All(loaded.Goods.Values.Where(g => g.Key != "propellant" && g.Id.Value < DemoUniverse.ShipKitMedium.Value), g => Assert.True(g.Id.Value < 23));
        // Phase 5a (Kettenmigration v5→v6): dahinter nur Bausatzstufen und Modul-Waren
        Assert.All(loaded.Goods.Values.Where(g => g.Id.Value > 23), g => Assert.True(g.Key.StartsWith("ship_kit_") || loaded.Modules.ContainsKey(g.Key), g.Key));
        // geänderter Slot-Content nachgereicht
        Assert.Equal(new FuelType(prop.Id, 1f), Assert.Single(loaded.Modules["chem_engine_s"].FuelTypes));
        Assert.Equal(200f, loaded.ShipClasses["unit_trader"].BaseFuel);
        // Raffinerien handeln propellant (Markt-Einträge liefen mit dem Save; propellant hängt per Migration am Ende der Warenliste)
        Assert.All(loaded.Stations.Values.Where(st => st.TypeKey == "fuel_refinery"), st => Assert.True(st.Market.ContainsKey(prop.Id)));
        // jedes Schiff mit Modell hat einen vollen Tank propellant
        // Phase 8: die Kettenmigration (v10→v11) setzt je Fraktion ein Bauschiff mit vollem eigenen Tank dazu
        Assert.All(loaded.Ships.Values.Where(s => s.Role is ShipRole.Builder or ShipRole.Police or ShipRole.Pirate), s => Assert.Equal(s.Stats.FuelCapacity, s.FuelAmount(prop.Id)));   // Phase 11: Polizei/Piraten eigener Tank
        foreach (var s in loaded.Ships.Values.Where(s => s.Role is not (ShipRole.Builder or ShipRole.Police or ShipRole.Pirate)))
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
        // Phase 8: Bauschiffe (Foreman, Klasse mit eigenem Rumpftank) starten ebenfalls voll, nur mit ihrer eigenen Kapazität
        // Phase 11: Polizei- und Kaperschiffe ebenso (eigene Klassen mit eigenem Rumpftank)
        Assert.All(u.Ships.Values.Where(s => s.Role is ShipRole.Builder or ShipRole.Police or ShipRole.Pirate), s => Assert.Equal(s.Stats.FuelCapacity, s.FuelAmount(prop.Id)));
        foreach (var s in u.Ships.Values.Where(s => s.Role is not (ShipRole.Builder or ShipRole.Police or ShipRole.Pirate)))
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
