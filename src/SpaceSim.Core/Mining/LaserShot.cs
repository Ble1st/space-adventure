using SpaceSim.Core.Model;

namespace SpaceSim.Core.Mining;

/// <summary>
/// Die eine Regel für jeden Laserschuss – Spieler (<see cref="OreChunks"/>) und NPC-Miner (<c>MinerSystem</c>) benutzen sie gleich:
/// Mit der Trefferchance der Asteroidenklasse löst der Schuss einen Brocken zufälliger Größe (höchstens so viel, wie der
/// Asteroid noch enthält) und zufälliger Erzsorte nach den Gewichten der Klasse.
/// </summary>
public static class LaserShot
{
    public readonly record struct Result(float Units, GoodId Good)
    {
        public bool Hit => Units > 0f;
    }

    public static Result Roll(DeterministicRng rng, AsteroidClass cls, Asteroid rock)
    {
        if (rng.NextDouble() >= cls.ChunkChance) return default;
        float units = MathF.Min(MiningRules.RollChunkUnits(rng), rock.Ore);
        return new Result(units, cls.RollGood(rng));
    }
}
