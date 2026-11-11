using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Diagnostics;

/// <summary>Phase 15: Markteintrag, der einen großen Teil der Messzeit am oberen (Bestand leer und Preis am festen Höchstgebot, <see cref="MarketWatch.UpperCap"/>) bzw. unteren Preisdeckel hing.</summary>
public sealed record CapHint(StationId Station, GoodId Good, float UpperShare, float LowerShare, float AvgRatio);
/// <summary>Phase 15: Station, deren Kasse einen großen Teil der Messzeit praktisch leer war (unter <see cref="MarketWatch.EmptyCash"/>).</summary>
public sealed record CashHint(StationId Station, float EmptyShare, float MinCredits, float EndCredits);

/// <summary>
/// Phase 15: ehrlicheres Balance-Urteil. Der Durchschnittspreis je Ware über alle Stationen mittelt Dauerzustände weg (eine Werft mit Reaktor dauerhaft
/// am 4×-Deckel neben einer Fabrik auf Halde ergibt „2×“). Diese Messung tastet jeden Markteintrag und jede Stationskasse in festen Abständen ab und
/// zählt Fahrten je Ware über den Ereignisbus. Ephemer (nicht im Spielstand), keine Wirkung auf die Simulation.
/// </summary>
public sealed class MarketWatch
{
    /// <summary>Kasse darunter gilt als „praktisch leer“ (eine Ladung ist damit nicht bezahlbar).</summary>
    public const float EmptyCash = 100f;
    /// <summary>Knappheitsfaktor (Wunsch/Bestand) ab dem ein Eintrag als „am oberen Deckel“ zählt (Deckel 4, kleine Toleranz).</summary>
    public const float UpperFactor = 3.9f;
    /// <summary>Preis/Basis bis zu dem ein Eintrag als „am unteren Deckel“ zählt (Boden 0,25, kleine Toleranz).</summary>
    public const float LowerRatio = 0.27f;

    readonly Dictionary<(StationId, GoodId), (int N, int Up, int Low, double Sum)> _entries = new();
    readonly Dictionary<StationId, (int N, int Empty, float Min)> _cash = new();
    readonly Dictionary<GoodId, int> _trips = new();
    // Phase 16: Preis/Basis je Ware (Durchschnitt über alle fertigen Stationen mit Markteintrag, wie EconomyStats) je Messpunkt, für das rasterunabhängige Urteil
    readonly List<(long Tick, Dictionary<GoodId, float> Ratios)> _prices = new();
    int _samples;

    public MarketWatch(Universe u) => u.Events.Raised += e =>
    {
        switch (e)
        {
            case TradeCompleted t: _trips[t.Good] = _trips.GetValueOrDefault(t.Good) + 1; break;
            case MineDelivered m: _trips[m.Good] = _trips.GetValueOrDefault(m.Good) + 1; break;
        }
    };

    /// <summary>Anzahl der bisherigen Messpunkte.</summary>
    public int Samples => _samples;

    /// <summary>Fahrten (Händlerlieferungen und Erzverkäufe) einer Ware seit Beginn.</summary>
    public int Trips(GoodId good) => _trips.GetValueOrDefault(good);

    /// <summary>Ein Messpunkt: alle fertigen Stationen mit Markt, je Eintrag Deckelzustand, je Station Kasse.</summary>
    public void Sample(Universe u)
    {
        _samples++;
        var sums = new Dictionary<GoodId, (double Sum, int N)>();
        foreach (var st in u.Stations.Values)
        {
            if (!st.IsBuildSite)
                foreach (var (g, e) in st.Market) { var ps = sums.GetValueOrDefault(g); sums[g] = (ps.Sum + e.Price, ps.N + 1); }
            if (st.IsBuildSite || st.Market.Count == 0) continue;
            foreach (var (g, e) in st.Market)
            {
                float basis = u.Goods[g].BasePrice;
                if (e.Price <= 0f || basis <= 0f) continue;
                float ratio = e.Price / basis;
                float factor = e.Target / MathF.Max(e.Amount, 1f);
                var key = (st.Id, g);
                var cur = _entries.GetValueOrDefault(key);
                bool upper = factor >= UpperFactor && ratio >= 0.98f * UpperCap(u, st, g);
                _entries[key] = (cur.N + 1, cur.Up + (upper ? 1 : 0), cur.Low + (ratio <= LowerRatio || factor <= LowerRatio ? 1 : 0), cur.Sum + ratio);
            }
            if (StationProtection.IsOfficial(u, st)) continue;   // Regierungsstationen zahlen aus der Fraktionskasse, ihre eigene Kasse ist kein Befund
            var c = _cash.TryGetValue(st.Id, out var cc) ? cc : (N: 0, Empty: 0, Min: float.MaxValue);
            _cash[st.Id] = (c.N + 1, c.Empty + (st.Credits < EmptyCash ? 1 : 0), MathF.Min(c.Min, st.Credits));
        }
        var ratios = new Dictionary<GoodId, float>();
        foreach (var (g, (sum, n)) in sums)
            if (n > 0 && u.Goods.TryGetValue(g, out var good) && good.BasePrice > 0f) ratios[g] = (float)(sum / n / good.BasePrice);
        _prices.Add((u.Tick, ratios));
    }

    /// <summary>
    /// Phase 16: mittlerer Preis/Basis einer Ware über alle Messpunkte ab <paramref name="fromTick"/> (je Messpunkt Durchschnitt über die Stationen, die sie
    /// führen; Messpunkte ohne Eintrag zählen nicht). Grundlage des Balance-Urteils: Die Probe ist fest (alle 20 Ticks), nicht das Ausgaberaster des Werkzeugs.
    /// null = keine Messung.
    /// </summary>
    public float? AverageRatio(GoodId good, long fromTick)
    {
        double sum = 0; int n = 0;
        foreach (var (tick, ratios) in _prices)
            if (tick >= fromTick && ratios.TryGetValue(good, out var r)) { sum += r; n++; }
        return n > 0 ? (float)(sum / n) : null;
    }

    /// <summary>Phase 16: Anzahl der Messpunkte ab <paramref name="fromTick"/>.</summary>
    public int SamplesSince(long fromTick) => _prices.Count(p => p.Tick >= fromTick);

    /// <summary>
    /// Höchstes Gebot (Preis/Basis) eines Eintrags aus den festen Deckeln: Exportware <see cref="EconomyRules.ExportMaxBidFactor"/>, Zutat eines
    /// Endverbrauchers <see cref="EconomyRules.ConsumerMaxBidFactor"/>, Lager einer Forschungsstation <see cref="EconomyRules.ResearchMaxBidFactor"/>,
    /// sonst der Knappheitsdeckel 4. Die Zahlungsbereitschaft einer Fabrik (dynamische Obergrenze) zählt nicht als Deckel: Eine Fabrik, deren
    /// Zutat leer ist und die nur bietet, was ihr Produkt trägt, hängt nicht „am Deckel“.
    /// </summary>
    public static float UpperCap(Universe u, Station st, GoodId g)
    {
        if (ExportRules.ConfigOf(u, st) is { } ex && ex.Goods.Contains(g)) return MathF.Min(4f, ex.BidOf(g, u.Rules));   // Phase 17: Gebot je Ware
        if (MarketSystem.IsFinalConsumer(st) && st.Production.Any(r => r.Inputs.ContainsKey(g))) return MathF.Min(4f, u.Rules.ConsumerMaxBidFactor);
        if (u.Rules.ResearchActive && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Research is not null && st.IsStockOnly(g)) return MathF.Min(4f, u.Rules.ResearchMaxBidFactor);
        return 4f;
    }

    /// <summary>Einträge, die mehr als <paramref name="share"/> der Messpunkte am oberen oder unteren Deckel hingen (Station-, dann Waren-Id-Reihenfolge).</summary>
    public IReadOnlyList<CapHint> CapHints(Universe u, float share = 0.5f) =>
        _entries.Where(kv => u.Stations.ContainsKey(kv.Key.Item1) && kv.Value.N > 0 && (kv.Value.Up > share * kv.Value.N || kv.Value.Low > share * kv.Value.N))
            .OrderBy(kv => kv.Key.Item1.Value).ThenBy(kv => kv.Key.Item2.Value)
            .Select(kv => new CapHint(kv.Key.Item1, kv.Key.Item2, (float)kv.Value.Up / kv.Value.N, (float)kv.Value.Low / kv.Value.N, (float)(kv.Value.Sum / kv.Value.N)))
            .ToList();

    /// <summary>
    /// Phase 17: Ist der Deckel-Hinweis eine unbefriedigte Exportnachfrage (Exportsenke mit leerem Lager am Gebotsdeckel)? Das ist kein Fehler der Wirtschaft:
    /// Die Senke nimmt jede Menge, die übrig ist; leer heißt nur, dass gerade nichts übrig ist. Das Werkzeug führt solche Einträge in einer eigenen Rubrik.
    /// </summary>
    public static bool IsExportDemand(Universe u, CapHint h) =>
        h.UpperShare >= h.LowerShare && u.Stations.TryGetValue(h.Station, out var st) && ExportRules.Buys(u, st, h.Good);

    /// <summary>Anteil der Messpunkte am oberen/unteren Deckel für einen Eintrag (0, wenn nie gemessen).</summary>
    public (float Upper, float Lower) Share(StationId station, GoodId good) =>
        _entries.TryGetValue((station, good), out var v) && v.N > 0 ? ((float)v.Up / v.N, (float)v.Low / v.N) : (0f, 0f);

    /// <summary>Stationen mit Markt, deren Kasse mehr als <paramref name="share"/> der Messpunkte praktisch leer war (Id-Reihenfolge).</summary>
    public IReadOnlyList<CashHint> CashHints(Universe u, float share = 0.25f) =>
        _cash.Where(kv => u.Stations.ContainsKey(kv.Key) && kv.Value.N > 0 && kv.Value.Empty > share * kv.Value.N)
            .OrderBy(kv => kv.Key.Value)
            .Select(kv => new CashHint(kv.Key, (float)kv.Value.Empty / kv.Value.N, kv.Value.Min, u.Stations[kv.Key].Credits))
            .ToList();

    /// <summary>Anteil der Messpunkte, an denen die Kasse der Station praktisch leer war (0, wenn nie gemessen).</summary>
    public float EmptyShare(StationId station) => _cash.TryGetValue(station, out var v) && v.N > 0 ? (float)v.Empty / v.N : 0f;

    /// <summary>
    /// Gehandelte Waren ohne eine einzige Fahrt: Es gibt einen Hersteller (Rezeptausgang) oder die Ware ist Erz einer Asteroidenklasse, und eine
    /// andere fertige Station führt sie, ohne sie herzustellen (Abnehmer) — trotzdem hat niemand sie gefahren.
    /// </summary>
    public IReadOnlyList<GoodId> IdleGoods(Universe u)
    {
        var ores = new HashSet<GoodId>(u.AsteroidClasses.Values.SelectMany(c => c.Yields.Select(y => y.Good)));
        var list = new List<GoodId>();
        foreach (var g in u.Goods.Values.OrderBy(g => g.Id.Value))
        {
            if (_trips.GetValueOrDefault(g.Id) > 0) continue;
            var stations = u.Stations.Values.Where(s => !s.IsBuildSite && s.Market.ContainsKey(g.Id)).ToList();
            bool source = ores.Contains(g.Id) || stations.Any(s => s.Produces(g.Id));
            bool sink = stations.Any(s => !s.Produces(g.Id));
            if (source && sink) list.Add(g.Id);
        }
        return list;
    }
}
