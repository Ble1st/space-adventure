using SpaceSim.Core.Model;

namespace SpaceSim.Core.Mining;

/// <summary>
/// Die eine Regel für jeden Laserschuss – Spieler (<see cref="OreChunks"/>) und NPC-Miner (<c>MinerSystem</c>) benutzen sie gleich:
/// Mit der Trefferchance der Asteroidenklasse (multipliziert mit der Laserstufe des Schiffs, siehe <see cref="Roll"/>) löst der
/// Schuss einen Brocken zufälliger Größe (ebenfalls skaliert, höchstens so viel, wie der Asteroid noch enthält) und zufälliger
/// Erzsorte nach den Gewichten der Klasse.
/// </summary>
public static class LaserShot
{
    public readonly record struct Result(float Units, GoodId Good)
    {
        public bool Hit => Units > 0f;
    }

    /// <param name="rng">Zufallsgenerator; die Reihenfolge der Aufrufe ist unabhängig von den Faktoren (Determinismus).</param>
    /// <param name="cls">Asteroidenklasse (Trefferchance, Erzgewichte).</param>
    /// <param name="rock">Angeschossener Asteroid (deckelt die Brockengröße am Restbestand).</param>
    /// <param name="chunkChanceMult">Laserstufe: Multiplikator auf die Brocken-Chance (auf 1 begrenzt); Standard 1.</param>
    /// <param name="chunkSizeMult">Laserstufe: Multiplikator auf die Brockengröße; Standard 1.</param>
    public static Result Roll(DeterministicRng rng, AsteroidClass cls, Asteroid rock, float chunkChanceMult = 1f, float chunkSizeMult = 1f)
    {
        if (rng.NextDouble() >= MathF.Min(1f, cls.ChunkChance * chunkChanceMult)) return default;
        float units = MathF.Min(MiningRules.RollChunkUnits(rng) * chunkSizeMult, rock.Ore);
        return new Result(units, cls.RollGood(rng));
    }
}
