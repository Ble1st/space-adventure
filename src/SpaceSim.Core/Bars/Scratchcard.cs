using SpaceSim.Core.Model;

namespace SpaceSim.Core.Bars;

/// <summary>
/// Rubbellos der Bar (rein, docs/BARS.md): neun Felder, drei gleiche Symbole gewinnen die Stufe des Symbols. Der Ausgang wird beim Kauf aus einem eigenen,
/// aus Welt-Seed, Tick, Station und Loszähler abgeleiteten Zufall gezogen (nie <see cref="Universe.Rng"/>) — so bleibt die NPC-Welt unberührt, und das
/// Aufdecken in beliebiger Reihenfolge kann nichts ändern.
/// </summary>
public static class Scratchcard
{
    public const int Fields = 9;

    static readonly string[] Names = { "Komet", "Stern", "Planet", "Ring", "Krone", "Galaxie" };

    /// <summary>Anzeigename eines Symbols.</summary>
    public static string SymbolName(int i) => i >= 0 && i < Names.Length ? Names[i] : $"Symbol {i + 1}";

    /// <summary>Anzahl Symbole = Anzahl Gewinnstufen (mindestens 5, damit eine Niete ohne Dreier und ein Gewinn mit genau einem Dreier legbar sind).</summary>
    public static int SymbolCount(EconomyRules r) => Math.Max(5, r.BarPrizes.Length);

    /// <summary>Eigener Zufall eines Loses (gleicher Seed + Tick + Station + Zähler ⇒ gleiches Los).</summary>
    public static DeterministicRng Rng(Universe u, long tick, StationId station, int counter) =>
        new(u.Seed ^ 0x42415253_43524154UL ^ (0x9E3779B97F4A7C15UL * (ulong)(tick + 1))
            ^ (0xC2B2AE3D27D4EB4FUL * (ulong)(uint)station.Value) ^ (0x165667B19E3779F9UL * (ulong)(uint)(counter + 1)));

    /// <summary>Erwartungswert eines Loses in Credits (ohne Kassendeckel).</summary>
    public static double ExpectedValue(EconomyRules r)
    {
        double ev = 0;
        for (int i = 0; i < r.BarPrizes.Length && i < r.BarPrizeWeights.Length; i++) ev += (double)r.BarPrizes[i] * r.BarPrizeWeights[i];
        return r.BarPrizeWeightTotal > 0 ? ev / r.BarPrizeWeightTotal : 0;
    }

    /// <summary>Gewinnwahrscheinlichkeit (irgendeine Stufe).</summary>
    public static double WinChance(EconomyRules r) =>
        r.BarPrizeWeightTotal > 0 ? (double)r.BarPrizeWeights.Take(r.BarPrizes.Length).Sum() / r.BarPrizeWeightTotal : 0;

    /// <summary>Zieht Stufe und Symbole. Stufe −1 = Niete (kein Symbol dreimal), sonst genau ein Symbol dreimal.</summary>
    public static (int Tier, int Prize, int[] Symbols) Draw(DeterministicRng rng, EconomyRules r)
    {
        int n = SymbolCount(r);
        int roll = rng.NextInt(Math.Max(1, r.BarPrizeWeightTotal));
        int tier = -1, acc = 0;
        for (int i = 0; i < r.BarPrizes.Length && i < r.BarPrizeWeights.Length; i++)
        {
            acc += r.BarPrizeWeights[i];
            if (roll < acc) { tier = i; break; }
        }
        var symbols = new int[Fields];
        if (tier < 0)
        {
            var pool = new List<int>(2 * n);
            for (int s = 0; s < n; s++) { pool.Add(s); pool.Add(s); }
            Shuffle(rng, pool);
            for (int i = 0; i < Fields; i++) symbols[i] = pool[i];
            return (-1, 0, symbols);
        }
        var pos = Enumerable.Range(0, Fields).ToList();
        Shuffle(rng, pos);
        var rest = new List<int>(2 * n);
        for (int s = 0; s < n; s++) if (s != tier) { rest.Add(s); rest.Add(s); }
        Shuffle(rng, rest);
        for (int i = 0; i < Fields; i++) symbols[pos[i]] = i < 3 ? tier : rest[i - 3];
        return (tier, r.BarPrizes[tier], symbols);
    }

    /// <summary>Gewinnstufe aus den Symbolen (die Probe von <see cref="Draw"/>): Symbol, das dreimal vorkommt, sonst −1.</summary>
    public static int TierOf(int[] symbols)
    {
        foreach (var g in symbols.GroupBy(s => s)) if (g.Count() >= 3) return g.Key;
        return -1;
    }

    static void Shuffle(DeterministicRng rng, List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Gewinntabelle als Text („3× Komet 25 Cr · …“).</summary>
    public static string PrizeTable(EconomyRules r) =>
        string.Join(" · ", r.BarPrizes.Select((p, i) => $"3× {SymbolName(i)} {p} Cr"));
}
