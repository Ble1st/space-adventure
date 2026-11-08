using SpaceSim.Core.Model;

namespace SpaceSim.Core.Mining;

/// <summary>
/// Die eine Regel für jeden Laserschuss – Spieler (<see cref="OreChunks"/>) und NPC-Miner (<c>MinerSystem</c>) benutzen sie gleich:
/// Mit Wahrscheinlichkeit <see cref="MiningRules.ChunkChance"/> löst der Schuss einen Brocken zufälliger Größe, höchstens so viel,
/// wie der Asteroid noch enthält.
/// </summary>
public static class LaserShot
{
    /// <returns>Gelöste Einheiten; 0 bei Fehlschuss.</returns>
    public static float Roll(DeterministicRng rng, Asteroid rock)
    {
        if (rng.NextDouble() >= MiningRules.ChunkChance) return 0f;
        return MathF.Min(MiningRules.RollChunkUnits(rng), rock.Ore);
    }
}
