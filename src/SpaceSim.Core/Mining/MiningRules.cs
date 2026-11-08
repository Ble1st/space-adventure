using SpaceSim.Core.Model;

namespace SpaceSim.Core.Mining;

/// <summary>Spielwerte des Mining-Lasers und des Traktorstrahls (Spieler und Darstellung teilen sie).</summary>
public static class MiningRules
{
    /// <summary>Maximaler Abstand zwischen Schiff und Asteroidenoberfläche für den Laser.</summary>
    public const float LaserRange = 40f;
    /// <summary>Sekunden zwischen zwei Schüssen.</summary>
    public const float ShotInterval = 0.4f;
    /// <summary>Wahrscheinlichkeit, dass ein Schuss einen Erzbrocken löst.</summary>
    public const float ChunkChance = 0.5f;
    /// <summary>Kleinste mögliche Brockengröße; Laderaum darunter gilt als voll.</summary>
    public const float MinChunkUnits = 1f;
    public const float TractorRange = 60f;
    public const float TractorSpeed = 50f;
    public const float TractorAccel = 140f;
    public const float CollectRadius = 4f;
    public const float ChunkLifetime = 90f;
    /// <summary>Asteroiden bis zu diesem Abstand vom Feldmittelpunkt gehören zum Gürtel des Feldes.</summary>
    public const float BeltRadius = 80f;

    /// <summary>Verteilung der Brockengröße (Einheiten, Gewicht): viele kleine, wenige große. Erwartungswert ≈ 2,7.</summary>
    public static readonly (float Units, float Weight)[] ChunkTable =
        { (1f, 32f), (2f, 28f), (3f, 20f), (5f, 13f), (8f, 7f) };

    public static float RollChunkUnits(DeterministicRng rng)
    {
        float total = 0f;
        foreach (var (_, w) in ChunkTable) total += w;
        float x = (float)rng.NextDouble() * total;
        foreach (var (units, w) in ChunkTable)
        {
            if (x < w) return units;
            x -= w;
        }
        return ChunkTable[^1].Units;
    }
}
