using SpaceSim.Core.Model;

namespace SpaceSim.Core.Mining;

/// <summary>Spielwerte des Mining-Lasers und des Traktorstrahls (Spieler und Darstellung teilen sie).</summary>
public static class MiningRules
{
    /// <summary>Maximaler Abstand zwischen Schiff und Asteroidenoberfläche für den Laser.</summary>
    public const float LaserRange = 40f;
    /// <summary>Nachladezeit in Sekunden: frühestens so lange nach einem Schuss ist der nächste möglich (Spieler wie NPCs).</summary>
    public const float ShotInterval = 0.4f;
    /// <summary>Rückstoß je Schuss (Geschwindigkeitsänderung, vom Asteroiden weg).</summary>
    public const float RecoilImpulse = 6f;
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

    /// <summary>Mittlere Brockengröße laut <see cref="ChunkTable"/>.</summary>
    public static float MeanChunkUnits
    {
        get
        {
            float total = 0f, sum = 0f;
            foreach (var (u, w) in ChunkTable) { total += w; sum += u * w; }
            return sum / total;
        }
    }

    /// <summary>Erwartetes Erz pro Sekunde bei dauerhaftem Feuern im Takt der Nachladezeit.</summary>
    public static float ExpectedOrePerSecond => ChunkChance * MeanChunkUnits / ShotInterval;

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
