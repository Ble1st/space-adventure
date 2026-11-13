using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

public class ContentTests
{
    [Fact]
    public void DemoUniverseConstants_MatchTheDataKeys()
    {
        var u = DemoUniverse.Create();
        Assert.Equal("iron_ore", u.Goods[DemoUniverse.IronOre].Key);
        Assert.Equal("platinum_ore", u.Goods[DemoUniverse.PlatinumOre].Key);
        Assert.Equal("energy", u.Goods[DemoUniverse.Energy].Key);
        Assert.Equal("steel", u.Goods[DemoUniverse.Steel].Key);
        Assert.Equal("food", u.Goods[DemoUniverse.Food].Key);
        Assert.Equal("ship_kit_light", u.Goods[DemoUniverse.ShipKit].Key);   // Phase 5a: früher ship_kit, Id 22 unverändert
        Assert.Equal(22, DemoUniverse.ShipKitLight.Value);
        Assert.Equal("propellant", u.Goods[DemoUniverse.Propellant].Key);
        Assert.Equal("ship_kit_medium", u.Goods[DemoUniverse.ShipKitMedium].Key);
        Assert.Equal("ship_kit_heavy", u.Goods[DemoUniverse.ShipKitHeavy].Key);
        // alle übrigen Konstanten (Phase 5a darf keine Id verschieben)
        var expected = new (GoodId Id, string Key)[]
        {
            (DemoUniverse.IronOre, "iron_ore"), (DemoUniverse.Ice, "ice"), (DemoUniverse.CopperOre, "copper_ore"), (DemoUniverse.Silicon, "silicon"),
            (DemoUniverse.TitaniumOre, "titanium_ore"), (DemoUniverse.PlatinumOre, "platinum_ore"), (DemoUniverse.Energy, "energy"), (DemoUniverse.Steel, "steel"),
            (DemoUniverse.Water, "water"), (DemoUniverse.Hydrogen, "hydrogen"), (DemoUniverse.Copper, "copper"), (DemoUniverse.Wafers, "wafers"),
            (DemoUniverse.Titanium, "titanium"), (DemoUniverse.Platinum, "platinum"), (DemoUniverse.Electronics, "electronics"), (DemoUniverse.HullPlates, "hull_plates"),
            (DemoUniverse.FuelCells, "fuel_cells"), (DemoUniverse.LifeSupport, "life_support"), (DemoUniverse.Food, "food"), (DemoUniverse.ConsumerGoods, "consumer_goods"),
            (DemoUniverse.LuxuryGoods, "luxury_goods"),
        };
        Assert.All(expected, e => Assert.Equal(e.Key, u.Goods[e.Id].Key));
        Assert.Equal(DemoUniverse.FirstModuleGoodId, u.Goods.Values.Where(g => u.Modules.ContainsKey(g.Key)).Min(g => g.Id.Value));
    }

    [Fact]
    public void AllSixOres_AreMinableSomewhere()
    {
        var u = DemoUniverse.Create();
        var mined = u.AsteroidClasses.Values.SelectMany(c => c.Yields).Select(y => u.Goods[y.Good].Key).ToHashSet();
        Assert.Superset(new HashSet<string> { "iron_ore", "ice", "copper_ore", "silicon", "titanium_ore", "platinum_ore" }, mined);
        // und jedes Erz wird von mindestens einer Station verarbeitet
        foreach (var key in mined)
        {
            var good = u.FindGood(key)!;
            Assert.Contains(u.StationTypes.Values, t => t.Recipes.Any(r => r.Inputs.ContainsKey(good.Id)));
        }
    }

    [Fact]
    public void EveryGood_IsProducedAndUsedSomewhere_ExceptEndGoods()
    {
        var u = DemoUniverse.Create();
        var raw = u.AsteroidClasses.Values.SelectMany(c => c.Yields).Select(y => y.Good).ToHashSet();
        foreach (var good in u.Goods.Values)
        {
            bool shipFuel = good.Key == "propellant";   // Treibstoff (Phase 4b): von der Raffinerie erzeugt, von Schiffen verbraucht (kein Rezept-Verbraucher)
            bool produced = raw.Contains(good.Id) || u.StationTypes.Values.Any(t => t.Recipes.Any(r => r.Outputs.ContainsKey(good.Id)));
            Assert.True(produced, $"{good.Key} wird nirgends erzeugt");
            bool used = u.StationTypes.Values.Any(t => t.Recipes.Any(r => r.Inputs.ContainsKey(good.Id)));
            bool kit = good.Key == u.Rules.ShipKitGoodKey || u.ShipClasses.Values.Any(c => c.Kit == good.Key);   // Bausatz wird von der Flottenlogik verbraucht
            // Phase 5a: mittlere/schwere Bausätze (noch keine Klasse) und Modul-Waren haben noch keinen Verbraucher — Abnehmer kommen
            // mit Schiffskauf/Ausrüster (Phase 5c); bis dahin liegen sie in Werft-Lagern (stocks) oder auf Halde der Fabrik
            bool phase5aStock = good.Key.StartsWith("ship_kit_") || u.Modules.ContainsKey(good.Key);
            bool ammo = good.Category is "ammo" or "missile";   // Phase 23: Munition liegt nur als Lagerware (Werft, Wache, Handelsstation), verbraucht wird sie von Waffen; Phase 28: Raketen ebenso
            Assert.True(used || kit || shipFuel || phase5aStock || ammo, $"{good.Key} wird nirgends verbraucht");
        }
    }

    [Fact]
    public void RareOres_AreHarderToGetThanCommonOnes()
    {
        var u = DemoUniverse.Create();
        Assert.True(u.AsteroidClasses["rare_rock"].ChunkChance < u.AsteroidClasses["metal_rock"].ChunkChance);
        Assert.True(u.AsteroidClasses["rare_rock"].OreFactor < 1f);
        Assert.True(u.Goods[DemoUniverse.PlatinumOre].BasePrice > u.Goods[DemoUniverse.TitaniumOre].BasePrice);
        Assert.True(u.Goods[DemoUniverse.TitaniumOre].BasePrice > u.Goods[DemoUniverse.IronOre].BasePrice);
    }

    [Fact]
    public void RecipeOutputs_AreWorthMoreThanTheirInputs()
    {
        var u = DemoUniverse.Create();
        foreach (var t in u.StationTypes.Values)
            foreach (var r in t.Recipes.Where(r => r.Outputs.Count > 0))
            {
                float inV = r.Inputs.Sum(kv => kv.Value * u.Goods[kv.Key].BasePrice);
                float outV = r.Outputs.Sum(kv => kv.Value * u.Goods[kv.Key].BasePrice);
                Assert.True(outV > inV * 1.05f, $"{t.Key}: Wertschöpfung fehlt (Eingang {inV:0}, Ausgang {outV:0})");
            }
    }

    [Fact]
    public void ShipKit_NeedsTheWholeChain_TitaniumPlatinumElectronicsFuelCells()
    {
        var u = DemoUniverse.Create();
        var yard = u.StationTypes["shipyard"].Recipes.Single(r => r.Outputs.ContainsKey(DemoUniverse.ShipKitLight));
        var inputs = yard.Inputs.Keys.Select(g => u.Goods[g].Key).ToHashSet();
        Assert.Superset(new HashSet<string> { "hull_plates", "electronics", "fuel_cells" }, inputs);
        Assert.Contains(u.StationTypes.Values, t => t.Recipes.Any(r => r.Outputs.ContainsKey(DemoUniverse.HullPlates) && r.Inputs.ContainsKey(DemoUniverse.Titanium)));
        Assert.Contains(u.StationTypes.Values, t => t.Recipes.Any(r => r.Outputs.ContainsKey(DemoUniverse.FuelCells) && r.Inputs.ContainsKey(DemoUniverse.Platinum)));
    }

    [Fact]
    public void ProductionPausesWhenTheOutputIsOverstocked()
    {
        var u = DemoUniverse.Create();
        var plant = u.Stations.Values.First(s => s.TypeKey == "steelworks");
        var steel = plant.Stock(DemoUniverse.Steel);
        steel.Amount = steel.Target * u.Rules.OverstockFactor + 10f;
        plant.Stock(DemoUniverse.IronOre).Amount = 1000f; plant.Stock(DemoUniverse.Energy).Amount = 1000f;
        float before = steel.Amount;
        new ProductionSystem().Step(u, 1);   // Tick 0 passt zu jedem Rezeptintervall
        Assert.Equal(before, steel.Amount);

        steel.Amount = 0f;
        new ProductionSystem().Step(u, 1);
        Assert.True(steel.Amount > 0f);
    }
}
