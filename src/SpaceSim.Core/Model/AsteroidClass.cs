namespace SpaceSim.Core.Model;

public readonly record struct Yield(GoodId Good, float Weight);

/// <summary>
/// Art eines Asteroiden: welche Erze er in welchem Mischungsverhältnis liefert, wie leicht sich Brocken lösen
/// (<see cref="ChunkChance"/>) und wie reich er ist (<see cref="OreFactor"/>). Jeder Brocken würfelt seine Sorte einzeln.
/// </summary>
public sealed class AsteroidClass
{
    public string Key = "";
    public string Name = "";
    public string Color = "#8a7a6a";
    public float ChunkChance = 0.5f;
    public float OreFactor = 1f;
    public List<Yield> Yields = new();

    public GoodId RollGood(DeterministicRng rng)
    {
        float total = 0f;
        foreach (var y in Yields) total += y.Weight;
        float x = (float)rng.NextDouble() * total;
        foreach (var y in Yields)
        {
            if (x < y.Weight) return y.Good;
            x -= y.Weight;
        }
        return Yields[^1].Good;
    }

    /// <summary>Erwarteter Basispreis einer Einheit der Mischung.</summary>
    public float MeanBasePrice(Universe u)
    {
        float total = Yields.Sum(y => y.Weight);
        return total <= 0f ? 0f : Yields.Sum(y => y.Weight / total * u.Goods[y.Good].BasePrice);
    }
}
