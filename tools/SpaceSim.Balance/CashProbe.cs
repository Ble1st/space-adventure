using System.Globalization;
using System.Text;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

/// <summary>
/// Runde 25: <c>--probe kassen</c> — Zahlungsfähigkeit der Abnehmer (nur messen). Alle 200 Ticks ab Tick 5000 je Station mit Zutaten (Rezept-Eingang) und je
/// Zutat: Kasse ÷ Preis = bezahlbare Einheiten. Je Stationstyp (getrennt nach Startstation und neu gebaut): Ø Kasse, Anteil der Stichproben, in denen eine
/// Ladung von 100 Einheiten der teuersten Zutat nicht bezahlbar ist, und unter 10 Einheiten; Ø Betriebskapital (Steuerfreibetrag) zum Vergleich.
/// </summary>
sealed class CashProbe
{
    sealed class Acc { public int N, Under100, Under10; public double Cash, Wc; }
    readonly SortedDictionary<string, Acc> _acc = new(StringComparer.Ordinal);
    public const int Every = 200, From = 5000;

    public void Sample(Universe u)
    {
        if (u.Tick < From) return;
        foreach (var st in u.Stations.Values)
        {
            if (st.IsBuildSite || st.Production.Count == 0) continue;
            float maxPrice = 0f;
            foreach (var (g, e) in st.Market)
                if (st.Consumes(g) && e.Price > 0f) maxPrice = MathF.Max(maxPrice, e.Price);
            if (maxPrice <= 0f) continue;
            string type = u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Name : st.TypeKey;
            string key = $"{type} ({(st.BuiltTick > 0 ? "neu gebaut" : "Start")})";
            if (!_acc.TryGetValue(key, out var a)) _acc[key] = a = new Acc();
            float units = MathF.Max(0f, st.Credits) / maxPrice;
            a.N++; a.Cash += st.Credits; a.Wc += WorkingCapital.Of(u, st);
            if (units < 100f) a.Under100++;
            if (units < 10f) a.Under10++;
        }
    }

    public string Report()
    {
        var ci = CultureInfo.GetCultureInfo("de-DE");
        var sb = new StringBuilder("KASSEN der Verarbeiter ab Tick 5000 (teuerste Zutat): Typ | Ø Kasse | Ø Betriebskapital | < 100 Einh. bezahlbar | < 10 Einh.\n");
        foreach (var (k, a) in _acc.OrderByDescending(kv => (double)kv.Value.Under100 / Math.Max(1, kv.Value.N)))
            sb.AppendLine($"  {k,-44} {(a.Cash / a.N).ToString("0", ci),9} {(a.Wc / a.N).ToString("0", ci),9} {100.0 * a.Under100 / a.N,8:0}% {100.0 * a.Under10 / a.N,8:0}%");
        return sb.ToString();
    }
}
