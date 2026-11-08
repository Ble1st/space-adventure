namespace SpaceSim.Core.Mining;

/// <summary>Spielwerte des Mining-Lasers und des Traktorstrahls (Spieler und Darstellung teilen sie).</summary>
public static class MiningRules
{
    /// <summary>Maximaler Abstand zwischen Schiff und Asteroidenoberfläche für den Laser.</summary>
    public const float LaserRange = 40f;
    /// <summary>Abgebaute Einheiten pro Sekunde bei dauerhaftem Beschuss.</summary>
    public const float OrePerSecond = 10f;
    /// <summary>Einheiten pro Erzbrocken.</summary>
    public const float ChunkUnits = 2f;
    public const float TractorRange = 60f;
    public const float TractorSpeed = 50f;
    public const float TractorAccel = 140f;
    public const float CollectRadius = 4f;
    public const float ChunkLifetime = 90f;
    /// <summary>Asteroiden bis zu diesem Abstand vom Feldmittelpunkt gehören zum Gürtel des Feldes.</summary>
    public const float BeltRadius = 80f;
}
