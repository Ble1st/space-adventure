using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.PlayerBot;

/// <summary>Welle F (1): Reichsrechnung je Sektor — Momentaufnahme (EmpireBalance) am Anfang und Ende sowie gemessene Ströme der Spielerkasse über eine Spielstunde
/// (nur die Welt läuft, der Bot handelt nicht).</summary>
public static class ProbeEmpire
{
    public static void Run(Universe u, Pilot p, Action<string> P, long ticks)
    {
        void Snap(string label)
        {
            P($"== {label} Tick {u.Tick}, Kasse {u.PlayerWallet.Credits:0}, eigene Stationen {u.Stations.Values.Count(s => s.Owner == u.PlayerActor && !s.IsBuildSite)}");
            float ti = 0, tc = 0;
            foreach (var b in EmpireBalance.All(u)) { P("  " + EmpireBalance.Line(b) + $" | {b.OwnStations} eigene Stationen, netto {b.Net:0}"); ti += b.Income; tc += b.Costs; }
            P($"  Summe: +{ti:0} / −{tc:0} Cr/h, netto {ti - tc:0}");
            float gOut = 0, mOut = 0;
            foreach (var st in u.StationsById().Where(s => s.Owner == u.PlayerActor && !s.IsBuildSite && u.Sectors[s.Sector].Owner != u.PlayerFaction))
            {
                var t = u.StationTypes[st.TypeKey];
                if (t.Government != GovernmentKind.None && !PlayerUpkeep.Unpaid(u, st)) gOut += PlayerUpkeep.GovernmentPerTick(u, st, t) * EmpireBalance.TicksPerHour;
                if (Maintenance.Applies(u, st)) foreach (var (g, d) in Maintenance.NeedPerDay(u, t)) mOut += d * u.Goods[g].BasePrice * EmpireBalance.TicksPerHour / u.Rules.DayTicks;
            }
            P($"  außerhalb eigener Sektoren: Behörden {gOut:0}, Instandhaltung {mOut:0} Cr/h");
            var eco = u.StationsById().Where(s => s.Owner == u.PlayerActor && !s.IsBuildSite && u.StationTypes[s.TypeKey].Government == GovernmentKind.None).ToList();
            P($"  eigene Wirtschaftsstationen {eco.Count}: Σ Profit.Rate {eco.Sum(s => s.Profit?.Rate ?? 0) * EmpireBalance.TicksPerHour:0} Cr/h, Σ Kasse {eco.Sum(s => s.Credits):0}, Aktivität Ø {(eco.Count > 0 ? eco.Average(s => s.Activity) : 0):0.00}");
            foreach (var g in eco.GroupBy(s => s.TypeKey).OrderByDescending(g => g.Count()).Take(14))
                P($"    {g.Key}: {g.Count()}× Rate Ø {g.Average(s => s.Profit?.Rate ?? 0) * EmpireBalance.TicksPerHour:0} Cr/h, Aktivität Ø {g.Average(s => s.Activity):0.00}, Kasse Ø {g.Average(s => s.Credits):0}");
        }
        Snap("Anfang");
        var flows = new SortedDictionary<string, float>();
        void Add(string k, float v) => flows[k] = flows.GetValueOrDefault(k) + v;
        u.Events.Raised += e =>
        {
            switch (e)
            {
                case TreasuryUpkeep tu when tu.Actor == u.PlayerActor: Add("Kosten " + tu.Purpose, -tu.Amount); break;
                case PlayerSectorTaxCollected pt: Add("Steuer fremde Zahler", pt.Amount); break;
                case TaxCollected tc when tc.Treasury == u.PlayerActor: Add("Steuer gesamt (inkl. eigene Stationen)", tc.Amount); break;
                case StationCashMoved cm when u.Stations.TryGetValue(cm.Station, out var s) && s.Owner == u.PlayerActor: Add("Umbuchung Station", -cm.Amount); break;
            }
        };
        float w0 = u.PlayerWallet.Credits;
        for (long i = 0; i < ticks; i++) p.Step();
        P($"== gemessen über {ticks} Ticks ({ticks / EmpireBalance.TicksPerHour:0.00} h): Kasse {w0:0} → {u.PlayerWallet.Credits:0}");
        foreach (var kv in flows) P($"  {kv.Key}: {kv.Value * EmpireBalance.TicksPerHour / ticks:0} Cr/h");
        Snap("Ende");
    }
}
