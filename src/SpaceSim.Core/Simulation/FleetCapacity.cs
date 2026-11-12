using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 20: Flottenkapazität je erreichbarem Kartenteil (<see cref="MapParts"/>). Solange die Karte zusammenhängt, gilt die Grenze einer Aufgabe wie bisher
/// für die ganze Welt. Zerfällt sie (geschlossenes/zerstörtes Gate), bekommt jeder Teil den Anteil der Grenze, der seinem Anteil an den fertigen
/// Wirtschaftsstationen mit Markt entspricht, und gezählt werden nur die Schiffe in diesem Teil. Vorher zählten eingeschlossene Schiffe weltweit mit: Ein
/// Teil mit der Werft konnte die ganze Grenze füllen, während der andere Teil keine Schiffe mehr bekam. Regel <see cref="EconomyRules.FleetCapacityByPart"/>.
/// </summary>
public static class FleetCapacity
{
    /// <summary>Gilt die Teilung (Regel an und Karte zerfallen)?</summary>
    public static bool Divided(Universe u) => u.Rules.FleetCapacityByPart && MapParts.Of(u).IsSplit;

    /// <summary>Zählt diese Station für den Anteil eines Teils? Fertig, wirtschaftlich (keine Regierungsstation, kein Versteck), mit Markt.</summary>
    static bool Counts(Universe u, Station st) => !st.IsBuildSite && st.Market.Count > 0 && StationProtection.IsEconomic(u, st);

    /// <summary>Anteil des Teils an den fertigen Wirtschaftsstationen (1 ohne Teilung).</summary>
    public static float Share(Universe u, int partKey)
    {
        if (!Divided(u)) return 1f;
        var part = MapParts.Of(u);
        int all = 0, inPart = 0;
        foreach (var st in u.Stations.Values)
        {
            if (!Counts(u, st)) continue;
            all++;
            if (part.KeyOf(st.Sector) == partKey) inPart++;
        }
        return all == 0 ? 0f : (float)inPart / all;
    }

    /// <summary>Grenze der Aufgabe im Teil (ohne Teilung: die Grenze der Welt).</summary>
    public static float Limit(Universe u, IShipJob job, int partKey) => job.Limit(u.Rules) * Share(u, partKey);

    /// <summary>Belegte Kapazität der Aufgabe im Teil (ohne Teilung: <see cref="ShipJobs.UsedUnits"/>).</summary>
    public static float Used(Universe u, IShipJob job, int partKey)
    {
        if (!Divided(u)) return ShipJobs.UsedUnits(u, job);
        float sum = 0f;
        foreach (var s in u.ShipsById())
            if (s.Role == job.Role && s.Controller == Controller.Ai && MapParts.KeyOf(u, s) == partKey) sum += ShipJobs.UnitsOf(u, s, job);
        return sum;
    }

    /// <summary>Teil-Schlüssel eines Schiffs (ohne Teilung immer derselbe Wert, damit Aufrufer einheitlich rechnen).</summary>
    public static int PartOf(Universe u, Ship ship) => Divided(u) ? MapParts.KeyOf(u, ship) : 0;

    /// <summary>Teil-Schlüssel eines Sektors (ohne Teilung 0).</summary>
    public static int PartOf(Universe u, SectorId sector) => Divided(u) ? MapParts.Of(u).KeyOf(sector) : 0;

    /// <summary>Belegte Kapazität und Grenze für ein Schiff in seinem Teil.</summary>
    public static (float Used, float Limit) For(Universe u, IShipJob job, Ship ship)
    {
        int key = PartOf(u, ship);
        return (Used(u, job, key), Limit(u, job, key));
    }
}
