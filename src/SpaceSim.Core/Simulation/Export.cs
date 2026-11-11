using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 14: Regeln der Exportsenke (Handelsstation an einem Planeten). Die Station kauft Exportwaren über den normalen Handel und bezahlt sofort;
/// <see cref="OnDelivered"/> (aufgerufen an denselben Buchungsstellen wie die Baustellen-Lieferung: Händler, Spieler, Verkauf-Alles) macht aus jeder
/// Lieferung eine Charge. Die wartenden Chargen zählen zum Bestand, so sinkt der Ankaufpreis bei viel Zulieferung (die Senke kauft Fabriken und
/// Kolonie nicht leer). <see cref="ExportSystem"/> schickt fällige Chargen an den Planeten.
/// </summary>
public static class ExportRules
{
    /// <summary>Exportsenke der Station (über ihren Typ), sonst null. Baustellen haben keine.</summary>
    public static ExportConfig? ConfigOf(Universe u, Station st) =>
        !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Export : null;

    /// <summary>Kauft die Station diese Ware für den Export an (dann verkauft sie sie nicht zurück)?</summary>
    public static bool Buys(Universe u, Station st, GoodId good) => ConfigOf(u, st) is { } cfg && cfg.Goods.Contains(good);

    /// <summary>
    /// Buchungshaken nach einem Verkauf an die Station: Bei einer Exportware entsteht eine Charge über die gelieferte Menge mit dem tatsächlich
    /// bezahlten Betrag, fällig nach der Verzögerung des Typs (<see cref="ExportQueued"/>).
    /// </summary>
    public static void OnDelivered(Universe u, Station st, GoodId good, float units, float paid)
    {
        if (units <= 0f || ConfigOf(u, st) is not { } cfg || !cfg.Goods.Contains(good)) return;
        var batch = new ExportBatch { Good = good, Amount = units, Paid = MathF.Max(0f, paid), DueTick = u.Tick + cfg.Delay(u.Rules) };
        st.Exports.Add(batch);
        u.Events.Publish(new ExportQueued(st.Id, good, units, batch.Paid, batch.DueTick));
    }

    /// <summary>
    /// Fällige Chargen der Station an den Planeten (Reihenfolge des Ankaufs): Ware verschwindet aus dem Lager, die Station erhält bezahlt × (1 + Aufschlag),
    /// anteilig, falls weniger im Lager liegt als die Charge (z. B. nach einem Content-Wechsel). Liefert die Anzahl exportierter Chargen.
    /// </summary>
    public static int ExportDue(Universe u, Station st)
    {
        if (st.Exports.Count == 0 || ConfigOf(u, st) is not { } cfg) return 0;
        float factor = 1f + cfg.MarkupOf(u.Rules);
        int n = 0;
        for (int i = 0; i < st.Exports.Count; )
        {
            var b = st.Exports[i];
            if (b.DueTick > u.Tick) { i++; continue; }
            st.Exports.RemoveAt(i);
            float have = st.Market.TryGetValue(b.Good, out var e) ? e.Amount : 0f;
            float amount = MathF.Min(b.Amount, MathF.Max(0f, have));
            if (e is not null) e.Amount = MathF.Max(0f, e.Amount - amount);
            float revenue = b.Amount > 0f ? b.Paid * factor * (amount / b.Amount) : 0f;
            st.Credits += revenue;
            u.Events.Publish(new GoodsExported(st.Id, b.Good, amount, b.Paid * (b.Amount > 0f ? amount / b.Amount : 0f), revenue));
            n++;
        }
        return n;
    }

    /// <summary>Höchstes Gebot für Exportwaren: Basispreis × <see cref="EconomyRules.ExportMaxBidFactor"/> (nach dem Markt angewandt).</summary>
    public static void CapBids(Universe u, Station st, ExportConfig cfg)
    {
        foreach (var g in cfg.Goods)
            if (st.Market.TryGetValue(g, out var e))
                e.Price = MathF.Min(e.Price, u.Goods[g].BasePrice * u.Rules.ExportMaxBidFactor);
    }
}

/// <summary>
/// Phase 14: Exportsenke (eigenes Modul direkt nach Produktion/Markt): deckelt die Gebote der Exportstationen auf
/// <see cref="EconomyRules.ExportMaxBidFactor"/> × Basispreis, bevor Händler ihre Ziele wählen, und schickt fällige Chargen an den Planeten.
/// Stationen in Id-Reihenfolge, Chargen in Ankaufsreihenfolge — deterministisch.
/// </summary>
public sealed class ExportSystem : ISimSystem
{
    readonly List<Station> _stations = new();
    int _version = -1;
    Universe? _for;

    public void Step(Universe u, int dt)
    {
        if (!ReferenceEquals(_for, u) || _version != u.StructureVersion)
        {
            _stations.Clear();
            _stations.AddRange(u.Stations.Values.Where(s => u.StationTypes.TryGetValue(s.TypeKey, out var t) && t.Export is not null).OrderBy(s => s.Id.Value));
            _version = u.StructureVersion;
            _for = u;
        }
        foreach (var st in _stations)
        {
            if (ExportRules.ConfigOf(u, st) is not { } cfg) continue;
            ExportRules.ExportDue(u, st);
            ExportRules.CapBids(u, st, cfg);
        }
    }
}

/// <summary>Phase 14: Anzeige der Exportsenke einer Station (reine Funktion): Planet, wartende Chargen, Menge, bezahlter Betrag, Ticks bis zur nächsten.</summary>
public sealed record ExportView(string Planet, int Batches, float Units, float Paid, long? NextInTicks, float Markup, IReadOnlyList<GoodId> Goods)
{
    /// <summary>Zeile für die Stationsinfo: „Export an Korvan: 3 Chargen unterwegs, nächste in 1:30“ bzw. „… keine Charge unterwegs“.</summary>
    public string Line => Batches == 0
        ? $"Export an {Planet}: keine Charge unterwegs"
        : $"Export an {Planet}: {Batches} {(Batches == 1 ? "Charge" : "Chargen")} unterwegs, nächste in {ExportInfo.Duration(NextInTicks ?? 0)}";
}

/// <summary>Phase 14: reine Anzeige-Funktionen zur Exportsenke (Stationsinfo, Handelsbereich).</summary>
public static class ExportInfo
{
    /// <summary>Exportsenke der Station, sonst null. Planet = Planet des Sektors (sonst „Planet“).</summary>
    public static ExportView? ForStation(Universe u, Station st)
    {
        if (ExportRules.ConfigOf(u, st) is not { } cfg) return null;
        string planet = u.Sectors.TryGetValue(st.Sector, out var sec) && sec.Planet is { } p ? p.Name : "Planet";
        long? next = st.Exports.Count == 0 ? null : Math.Max(0, st.Exports.Min(b => b.DueTick) - u.Tick);
        return new ExportView(planet, st.Exports.Count, st.Exports.Sum(b => b.Amount), st.Exports.Sum(b => b.Paid), next, cfg.MarkupOf(u.Rules), cfg.Goods);
    }

    /// <summary>Spielzeit als m:ss (<see cref="SimClock.TicksPerSecond"/> Ticks je Sekunde).</summary>
    public static string Duration(long ticks)
    {
        long s = (ticks + SimClock.TicksPerSecond - 1) / SimClock.TicksPerSecond;
        return $"{s / 60}:{s % 60:00}";
    }
}
