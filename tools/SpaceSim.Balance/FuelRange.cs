using System.Globalization;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

/// <summary>
/// Treibstoff im Großmaßstab: Reichweite eines Spielertanks. Misst die Strecke innerhalb der Sektoren (Luftlinie über die Route,
/// Gate-Sprünge frei — dieselbe Strecke, nach der <see cref="Refuel.Consumption"/> abrechnet) für jeden Flug zwischen zwei
/// andockbaren Stationen mit genau zwei Sprüngen ("Drei-Sektoren-Flug") und rechnet sie mit dem Verbrauch der Startpaket-Modelle um.
/// Nur Messung, ändert nichts am Spielstand.
/// </summary>
static class FuelRange
{
    static readonly string[] Models = { "unit_miner_100", "prospector", "nomad", "falcon" };

    public static string Describe(Universe u)
    {
        var stations = u.StationsById().Where(s => s.Dockable && s.Site is null).ToList();
        var flights = new List<float>();
        foreach (var a in stations)
            foreach (var b in stations)
            {
                if (a.Id.Value >= b.Id.Value) continue;
                var route = u.FindRoute(a.Sector, b.Sector);
                if (route is null || route.Count != 2) continue;
                flights.Add(Travel.EstimateDistance(u, a.Sector, a.Position, b.Sector, b.Position));
            }
        flights.Sort();
        if (flights.Count == 0) return "keine Drei-Sektoren-Flüge";
        float med = flights[flights.Count / 2], p90 = flights[(int)(0.9 * (flights.Count - 1))];
        var ci = CultureInfo.GetCultureInfo("de-DE");
        var parts = new List<string> { string.Create(ci, $"Drei-Sektoren-Flug Median {med:0} / 90 % {p90:0} Einheiten Strecke") };
        foreach (var key in Models)
        {
            if (!u.ShipModels.TryGetValue(key, out var m) || !u.ShipClasses.TryGetValue(m.ClassKey, out var cls)) continue;
            var fit = m.Fit.Select(k => k is not null && u.Modules.TryGetValue(k, out var mod) ? mod : null).ToList();
            var st = ShipStats.Compute(cls, fit);
            if (st.FuelUsePerUnit <= 0f || st.FuelCapacity <= 0f) continue;
            float perFlight = med * st.FuelUsePerUnit;
            parts.Add(string.Create(ci, $"{key} Tank {st.FuelCapacity:0}: {perFlight:0.0} je Flug, {st.FuelCapacity / perFlight:0} Flüge je Tank"));
        }
        return string.Join("; ", parts);
    }
}
