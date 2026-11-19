using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.PlayerBot;

/// <summary>Messbuch je eigener Station: Stichproben (Auslastung, Mangel), Warenflüsse aus Ereignissen, Gewinn = Wertänderung (Kasse + Lager zu Basispreisen) − Einzahlungen.</summary>
public sealed class StationBook
{
    sealed class Rec
    {
        public string Type = "", Name = ""; public SectorId Sector; public Vec2 Pos; public long Since; public double WorthStart; public double Transfers;
        public double ActSum, StarveSum; public int Samples; public double InUnits, OutUnits, OutValue, InValue, Exported;
        public Dictionary<GoodId, float> Stock0 = new(); public float UpkeepTick, IncomeTick; public double ProfitRate;
    }
    readonly Universe U; readonly Dictionary<StationId, Rec> _r = new();
    public StationBook(Universe u)
    {
        U = u;
        u.Events.Raised += e =>
        {
            switch (e)
            {
                case TraderBought b when _r.TryGetValue(b.Station, out var r): r.OutUnits += b.Units; r.OutValue += b.Cost; break;
                case PlayerBought b when _r.TryGetValue(b.Station, out var r): r.OutUnits += b.Units; r.OutValue += b.Cost; break;
                case TradeCompleted t when _r.TryGetValue(t.To, out var r): r.InUnits += t.Amount; r.InValue += t.Revenue; break;
                case MineDelivered m when _r.TryGetValue(m.To, out var r): r.InUnits += m.Amount; r.InValue += m.Revenue; break;
                case PlayerSold s when _r.TryGetValue(s.Station, out var r): r.InUnits += s.Units; r.InValue += s.Revenue; break;
                case GoodsExported g when _r.TryGetValue(g.Station, out var r): r.Exported += g.Amount; break;
            }
        };
    }

    double Worth(Station st)
    {
        double w = st.Credits;
        foreach (var kv in st.Market) w += kv.Value.Amount * U.Goods[kv.Key].BasePrice;
        return w;
    }

    public void Register(Station st)
    {
        if (_r.ContainsKey(st.Id) || st.IsBuildSite) return;
        var t = U.StationTypes[st.TypeKey];
        var r = new Rec { Type = st.TypeKey, Name = st.Name, Sector = st.Sector, Pos = st.Position, Since = U.Tick, WorthStart = Worth(st), UpkeepTick = t.UpkeepPerTick, IncomeTick = MathF.Min(t.IncomePerTick, t.UpkeepPerTick) };
        foreach (var kv in st.Market) r.Stock0[kv.Key] = kv.Value.Amount;
        _r[st.Id] = r;
    }

    public void Transfer(StationId id, double amt) { if (_r.TryGetValue(id, out var r)) r.Transfers += amt; }

    public void Sample()
    {
        foreach (var (id, r) in _r)
            if (U.Stations.TryGetValue(id, out var st)) { r.ActSum += st.Activity; r.StarveSum += st.Starved; r.Samples++; r.ProfitRate = st.Profit.Rate; }
    }

    public int Count => _r.Count;
    public Func<StationId, bool>? Built;

    public string Report()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Herkunft;Typ;Name;Sektor;x;y;seit Tick;Stunden;Aktiv%;Mangel%;Einsatz Einheiten;Abgabe Einheiten;Abgabe Cr;Ausstoß geschätzt;Zyklen geschätzt;Lager;Kasse;Einzahlungen;Wert Start;Wert jetzt;Gewinn gesamt;Gewinn Cr/h;Unterhalt Cr/h;Grundeinkommen Cr/h;Rate(Core) Cr/Tick");
        foreach (var (id, r) in _r.OrderBy(k => k.Value.Since))
        {
            if (!U.Stations.TryGetValue(id, out var st)) { sb.AppendLine($"{(Built?.Invoke(id) == true ? "gebaut" : "erobert")};{r.Type};{r.Name};{U.Sectors[r.Sector].Name};{r.Pos.X:0};{r.Pos.Y:0};{r.Since};(Station nicht mehr vorhanden)"); continue; }
            double h = Math.Max(0.01, (U.Tick - r.Since) / 4.0 / 3600.0);
            double producedUnits = r.OutUnits + r.Exported;
            foreach (var rec in st.Production) foreach (var kv in rec.Outputs) producedUnits += st.Stock(kv.Key).Amount - r.Stock0.GetValueOrDefault(kv.Key);
            double perCycleOut = st.Production.Count == 0 ? 0 : st.Production.Average(x => x.Outputs.Values.Sum());
            double cycles = perCycleOut > 0 ? Math.Max(0, producedUnits) / perCycleOut : 0;
            double worth = Worth(st); double profit = worth - r.WorthStart - r.Transfers;
            string stock = string.Join(" ", st.Market.Where(kv => kv.Value.Amount >= 1).OrderByDescending(kv => kv.Value.Amount * U.Goods[kv.Key].BasePrice).Take(4).Select(kv => $"{U.Goods[kv.Key].Name} {kv.Value.Amount:0}"));
            sb.AppendLine($"{(Built?.Invoke(id) == true ? "gebaut" : "erobert")};{r.Type};{st.Name};{U.Sectors[st.Sector].Name};{r.Pos.X:0};{r.Pos.Y:0};{r.Since};{h:0.0};{(r.Samples > 0 ? r.ActSum / r.Samples * 100 : 0):0};{(r.Samples > 0 ? r.StarveSum / r.Samples * 100 : 0):0};{r.InUnits:0};{r.OutUnits:0};{r.OutValue:0};{Math.Max(0, producedUnits):0};{cycles:0};{stock};{st.Credits:0};{r.Transfers:0};{r.WorthStart:0};{worth:0};{profit:0};{profit / h:0};{r.UpkeepTick * 4 * 3600:0};{r.IncomeTick * 4 * 3600:0};{r.ProfitRate:0.000}");
        }
        return sb.ToString();
    }
}
