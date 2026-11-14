using System.Globalization;
using System.Text;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

/// <summary>
/// Runde 23: <c>--probe spielerhandel</c> — Margenmessung für einen Spieler-Händler (nur messen). Alle 200 Ticks je Kettenware: bester Einkauf (billigster
/// Hersteller mit Bestand, eine Ladung von 100 Laderaum bzw. was er hat) und der beste ehrliche Abnehmer (<see cref="MarketInfo.BestHonestBuyer"/>: was er jetzt
/// annimmt und bezahlt, abzüglich unterwegs) → Gewinn der Rundreise (Rest, den der Abnehmer nicht nimmt, zählt als Verlust), je Tick Reisezeit
/// (<see cref="Travel.Estimate"/>). Getrennt nach Start in den ersten 5000 Ticks und danach; dazu „Endware → Planetensenke“ und der Verdienst je Tick eines
/// NPC-Einheitsfrachters (Rentabilitätsmesser).
/// </summary>
sealed class PlayerTradeProbe
{
    sealed class Acc { public int N, Win, WinFit; public double Sum, Best = double.MinValue, PerTick, SumFit, BestFit = double.MinValue, PerTickFit, UnitsFit; }
    readonly Dictionary<string, Acc[]> _acc = new(StringComparer.Ordinal);
    readonly List<string> _order = new();
    double _npcRate; int _npcN;
    public const int Every = 200, Early = 5000;
    const float Hold = 100f;

    public void Sample(Universe u)
    {
        int phase = u.Tick < Early ? 0 : 1;
        foreach (var g in u.Goods.Values.Where(g => g.Category is "refined" or "component" or "end" or "kit" or "ammo" or "missile").OrderBy(g => g.Id.Value))
        {
            Measure(u, g, phase, g.Name, sinkOnly: false);
            if (g.Category == "end") Measure(u, g, phase, g.Name + " → Planetensenke", sinkOnly: true);
        }
        if (phase == 1)
            foreach (var s in u.Ships.Values)
                if (s.ModelKey == "unit_trader_100" && s.IsEconomyAi) { _npcRate += s.Profit.Rate; _npcN++; }
    }

    void Measure(Universe u, Good g, int phase, string key, bool sinkOnly)
    {
        if (!_acc.TryGetValue(key, out var a)) { _acc[key] = a = new[] { new Acc(), new Acc() }; _order.Add(key); }
        var acc = a[phase];
        float load = MathF.Floor(Hold / MathF.Max(0.1f, g.VolumePerUnit));
        Station? buy = null; float buyPrice = float.MaxValue;
        foreach (var st in u.StationsTrading(g.Id))
            if (!st.IsBuildSite && st.Produces(g.Id) && st.Market.TryGetValue(g.Id, out var e) && e.Price > 0f && e.Amount >= 1f && e.Price < buyPrice) { buy = st; buyPrice = e.Price; }
        acc.N++;
        if (buy is null) return;
        float units = MathF.Min(load, MathF.Floor(buy.Market[g.Id].Amount));
        var at = buy.DockPoint(default);
        // a) volle Ladung zum Abnehmer mit dem höchsten erwarteten Erlös (was er nicht nimmt, ist verloren)
        // b) mit Geduld: nur kaufen, was ein Abnehmer jetzt nimmt — der Abnehmer mit dem höchsten Gewinn für seine Menge
        BuyerQuote? best = null; double fitProfit = double.MinValue, fitTicks = 1, fitUnits = 0;
        foreach (var q in MarketInfo.HonestBuyers(u, g.Id, units, buy.Sector, at, buy.Id))
        {
            if (sinkOnly && !ExportRules.Buys(u, q.Station, g.Id)) continue;
            best ??= q;
            double p = q.Units * (q.Price - buyPrice);
            if (p > fitProfit) { fitProfit = p; fitTicks = Math.Max(1.0, q.TravelTicks); fitUnits = q.Units; }
        }
        if (best is not { } b) return;
        double profit = b.Revenue - units * buyPrice;
        double ticks = Math.Max(1.0, b.TravelTicks);
        if (profit > 0) acc.Win++;
        acc.Sum += profit;
        acc.Best = Math.Max(acc.Best, profit);
        acc.PerTick += profit / ticks;
        if (fitProfit > 0) acc.WinFit++;
        acc.SumFit += fitProfit; acc.BestFit = Math.Max(acc.BestFit, fitProfit); acc.PerTickFit += fitProfit / fitTicks; acc.UnitsFit += fitUnits;
    }

    public string Report(ulong? seed, int ticks)
    {
        var ci = CultureInfo.GetCultureInfo("de-DE");
        var sb = new StringBuilder();
        sb.AppendLine($"SPIELERHANDEL Seed {seed?.ToString() ?? "Standard"}, {ticks} Ticks, Stichprobe alle {Every} Ticks, Laderaum {Hold:0}");
        sb.AppendLine("a) volle Ladung (100 Laderaum) zum Abnehmer mit dem höchsten erwarteten Erlös; b) mit Geduld: nur so viel, wie ein Abnehmer jetzt nimmt (bester Gewinn)");
        sb.AppendLine($"{"Ware",-30}|{"a früh %",9}{"a spät %",9}{"a Ø spät",10} |{"b früh %",9}{"b Ø früh",9}{"b spät %",9}{"b Ø spät",9}{"b best",8}{"b Ø Menge",10}{"b Ø/Tick",9}");
        foreach (var k in _order)
        {
            var (e, l) = (_acc[k][0], _acc[k][1]);
            static double Pct(int w, int n) => n == 0 ? 0 : 100.0 * w / n;
            static double Avg(double v, int n) => n == 0 ? 0 : v / n;
            sb.AppendLine($"{k,-30}|{Pct(e.Win, e.N),8:0}%{Pct(l.Win, l.N),8:0}%{Avg(l.Sum, l.N).ToString("0", ci),10} |{Pct(e.WinFit, e.N),8:0}%{Avg(e.SumFit, e.N).ToString("0", ci),9}{Pct(l.WinFit, l.N),8:0}%{Avg(l.SumFit, l.N).ToString("0", ci),9}{(l.BestFit == double.MinValue ? 0 : l.BestFit).ToString("0", ci),8}{Avg(l.UnitsFit, l.N).ToString("0.#", ci),10}{Avg(l.PerTickFit, l.N).ToString("0.00", ci),9}");
        }
        sb.AppendLine($"NPC-Einheitsfrachter (100): Ø Verdienst {(_npcN == 0 ? 0 : _npcRate / _npcN).ToString("0.00", ci)} Cr/Tick (Rentabilitätsmesser, nach Tick {Early})");
        return sb.ToString();
    }
}
