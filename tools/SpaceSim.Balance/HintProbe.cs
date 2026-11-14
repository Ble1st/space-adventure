using System.Globalization;
using System.Text;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

/// <summary>
/// Runde 25: <c>--probe hinweis</c> — Güte der Abnehmer-Auskunft (nur messen). Alle 200 Ticks je Kettenware: Einkauf beim billigsten Hersteller (eine Ladung von
/// 100 Laderaum), bester Abnehmer nach <see cref="MarketInfo.HonestBuyers"/>; gemerkt werden die Schätzung „heutiger Preis“ (Runde 23) und „Preis bei Ankunft“
/// (Runde 25). Nach <see cref="BuyerQuote.TravelTicks"/> wird gemessen, was ein Verkauf der Menge dort dann wirklich brächte (gleiche Verkaufsregel, ohne zu
/// verkaufen). Fehler = (Schätzung − tatsächlich) ÷ Schätzung; positiv = zu optimistisch.
/// </summary>
sealed class HintProbe
{
    sealed record Pending(long Due, StationId Station, GoodId Good, float Qty, float EstOld, float EstNew, float PNow, float PArr);
    readonly List<Pending> _open = new();
    readonly List<double> _old = new(), _new = new(), _pOld = new(), _pNew = new(), _units = new();
    public const int Every = 200;

    public void Sample(Universe u, bool add)
    {
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            var p = _open[i];
            if (p.Due > u.Tick) continue;
            _open.RemoveAt(i);
            if (!u.Stations.TryGetValue(p.Station, out var st) || !st.Market.TryGetValue(p.Good, out var e) || e.Price <= 0f) continue;
            var (cap, allCap) = TradeActions.SaleCaps(u, st, p.Good, st.Credits);
            float units = TradeActions.SaleUnits(p.Qty, p.Qty, cap, allCap);
            float actual = units * e.Price;
            if (p.EstOld > 1f) _old.Add((p.EstOld - actual) / p.EstOld);
            if (p.EstNew > 1f) _new.Add((p.EstNew - actual) / p.EstNew);
            if (p.EstNew > 1f) { _pOld.Add((p.PNow - e.Price) / p.PNow); _pNew.Add((p.PArr - e.Price) / p.PArr); _units.Add((p.Qty - units) / p.Qty); }
        }
        if (!add || u.Tick < 2000) return;
        foreach (var g in u.Goods.Values.Where(g => g.Category is "refined" or "component" or "end").OrderBy(g => g.Id.Value))
        {
            Station? buy = null; float bp = float.MaxValue;
            foreach (var st in u.StationsTrading(g.Id))
                if (!st.IsBuildSite && st.Produces(g.Id) && st.Market.TryGetValue(g.Id, out var e) && e.Amount >= 1f && e.Price > 0f && e.Price < bp) { buy = st; bp = e.Price; }
            if (buy is null) continue;
            float q = MathF.Min(MathF.Floor(100f / MathF.Max(0.1f, g.VolumePerUnit)), MathF.Floor(buy.Market[g.Id].Amount));
            var list = MarketInfo.HonestBuyers(u, g.Id, q, buy.Sector, buy.DockPoint(default), buy.Id);
            if (list.Count == 0) continue;
            var b = list[0];
            _open.Add(new Pending(u.Tick + (long)MathF.Ceiling(b.TravelTicks), b.Station.Id, g.Id, b.Units, b.Units * b.PriceNow, b.Revenue, b.PriceNow, b.Price));
        }
    }

    static double Q(List<double> l, double p) { if (l.Count == 0) return double.NaN; var s = l.OrderBy(x => x).ToList(); return s[(int)Math.Clamp(Math.Round(p * (s.Count - 1)), 0, s.Count - 1)]; }

    public string Report()
    {
        var ci = CultureInfo.GetCultureInfo("de-DE");
        var sb = new StringBuilder("HINWEISGÜTE (Erlös-Schätzung bei Abfahrt gegen Verkauf bei Ankunft; Fehler = (Schätzung − tatsächlich) ÷ Schätzung, + = zu optimistisch)\n");
        foreach (var (name, l) in new[] { ("heutiger Preis (Runde 23)", _old), ("Preis bei Ankunft (Runde 25)", _new),
                     ("nur Stückpreis, heutiger", _pOld), ("nur Stückpreis, bei Ankunft", _pNew), ("nur Menge (angenommen)", _units) })
            sb.AppendLine($"  {name,-30} n {l.Count,5}  Median {(100 * Q(l, 0.5)).ToString("0.0", ci)} %  90-%-Quantil {(100 * Q(l, 0.9)).ToString("0.0", ci)} %  Anteil zu optimistisch (>5 %) {(l.Count == 0 ? 0 : 100.0 * l.Count(x => x > 0.05) / l.Count).ToString("0", ci)} %");
        return sb.ToString();
    }
}
