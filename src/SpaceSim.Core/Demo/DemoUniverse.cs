using SpaceSim.Core.Content;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Demo;

/// <summary>Kurzform für die mitgelieferte, datengetriebene Standardwelt (Content/default-world.json).</summary>
public static class DemoUniverse
{
    public static Universe Create(ulong? seed = null) => WorldBuilder.BuildDefault(seed);

    // Waren-IDs der Standardwelt (Reihenfolge in der JSON-Datei). Ein Test prüft, dass sie zu den Schlüsseln passen.
    public static readonly GoodId IronOre = new(1), Ice = new(2), CopperOre = new(3), Silicon = new(4), TitaniumOre = new(5), PlatinumOre = new(6);
    public static readonly GoodId Energy = new(7), Steel = new(8), Water = new(9), Hydrogen = new(10), Copper = new(11), Wafers = new(12), Titanium = new(13), Platinum = new(14);
    public static readonly GoodId Electronics = new(15), HullPlates = new(16), FuelCells = new(17), LifeSupport = new(18);
    public static readonly GoodId Food = new(19), ConsumerGoods = new(20), LuxuryGoods = new(21), ShipKit = new(22);
    public static readonly GoodId Propellant = new(23);   // Treibstoff (Phase 4), ans Ende der goods-Liste

    /// <summary>Kurzformen für ältere Aufrufer.</summary>
    public static GoodId Ore => IronOre;
    public static GoodId Metal => Steel;
}
