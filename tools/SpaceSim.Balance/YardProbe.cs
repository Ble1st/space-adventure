using System.Globalization;
using System.Text;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;


/// <summary>
/// Welle D (Punkte 2/3): <c>--probe werft</c> — alle <see cref="Every"/> Ticks je Werft, ob Schatten und Schlachtkreuzer lieferbar sind (alle Bauteile im
/// Regal, Kasse egal), und welche Ware am häufigsten fehlt. Reine Messung, ändert nichts.
/// </summary>
sealed class YardProbe
{
    public const int Every = 1000;
    static readonly string[] Models = { "shadow", "battlecruiser" };
    readonly SortedDictionary<string, (int n, int ok)> _acc = new(StringComparer.Ordinal);
    readonly SortedDictionary<string, int> _missing = new(StringComparer.Ordinal);
    readonly SortedDictionary<string, (int n, double sumTarget, double sumAmount)> _shelf = new(StringComparer.Ordinal);

    public void Sample(Universe u)
    {
        if (!u.Ships.TryGetValue(u.PlayerShip, out var buyer)) return;
        foreach (var yard in u.StationsById())
        {
            if (yard.IsBuildSite || yard.TypeKey != "shipyard") continue;
            foreach (var key in Models)
            {
                if (ShipDealerService.Quote(u, yard, buyer, key) is not { } q) continue;
                string k = $"{yard.Name} {key}";
                var a = _acc.GetValueOrDefault(k);
                bool ok = q.Missing.Count == 0;
                _acc[k] = (a.n + 1, a.ok + (ok ? 1 : 0));
                foreach (var m in q.Missing)
                {
                    string mk = $"{k}: {u.Goods[m.Good].Key}";
                    _missing[mk] = _missing.GetValueOrDefault(mk) + 1;
                }
            }
            foreach (var g in new[] { "weapon_laser_m", "cargo_universal_60", "chem_engine_m", "jump_drive_s", "cloak_s", "reactor_mk2" })
            {
                var good = u.Goods.Values.FirstOrDefault(x => x.Key == g);
                if (good is null || !yard.Market.TryGetValue(good.Id, out var e)) continue;
                string sk = $"{yard.Name} {g}";
                var s = _shelf.GetValueOrDefault(sk);
                _shelf[sk] = (s.n + 1, s.sumTarget + e.Target, s.sumAmount + e.Amount);
            }
        }
    }

    public string Report()
    {
        var ci = CultureInfo.GetCultureInfo("de-DE");
        var sb = new StringBuilder("WERFT-SONDE (Welle D): Anteil der Proben, in denen das Modell lieferbar war (alle Bauteile im Regal)\n");
        foreach (var (k, a) in _acc) sb.AppendLine(string.Create(ci, $"  {k}: {100.0 * a.ok / Math.Max(1, a.n):0.0} % von {a.n}"));
        sb.AppendLine("Häufigste Fehlteile:");
        foreach (var (k, n) in _missing.OrderByDescending(kv => kv.Value).Take(20)) sb.AppendLine($"  {k} ({n}×)");
        sb.AppendLine("Regale (Ø Ziel / Ø Bestand):");
        foreach (var (k, s) in _shelf) sb.AppendLine(string.Create(ci, $"  {k}: Ziel {s.sumTarget / Math.Max(1, s.n):0.00} Bestand {s.sumAmount / Math.Max(1, s.n):0.00}"));
        return sb.ToString();
    }
}
