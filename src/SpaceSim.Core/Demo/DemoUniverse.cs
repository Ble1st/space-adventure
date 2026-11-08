using SpaceSim.Core.Content;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Demo;

/// <summary>Kurzform für die mitgelieferte, datengetriebene Standardwelt (Content/default-world.json).</summary>
public static class DemoUniverse
{
    public static Universe Create(ulong? seed = null) => WorldBuilder.BuildDefault(seed);

    // Häufig benutzte Waren-IDs der Standardwelt (Reihenfolge in der JSON-Datei)
    public static readonly GoodId Ore = new(1);
    public static readonly GoodId Metal = new(2);
    public static readonly GoodId Energy = new(3);
    public static readonly GoodId Food = new(4);
    public static readonly GoodId Parts = new(5);
}
