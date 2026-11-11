using SpaceSim.Core.Model;

namespace SpaceSim.Core.Diagnostics;

/// <summary>Zerfall der Karte für ein Profil: Anzahl Sektoren, größter gegenseitig erreichbarer Block und die Sektoren außerhalb davon.</summary>
public sealed record MapSplit(string Profile, int Sectors, int Largest, IReadOnlyList<SectorId> Isolated)
{
    public bool IsSplit => Isolated.Count > 0;
}

/// <summary>
/// Prüfung der Karte (Phase 10, POLITIK.md „Dämpfer“ und SCHIFFSSLOTS.md Abschnitt 5 „Sektorabschottung“): Zerfällt der Sektorgraph durch geschlossene
/// Gates oder Sperren? Gemessen wird der größte Block gegenseitig erreichbarer Sektoren (stark zusammenhängende Komponente) je Mobilitätsprofil;
/// alle Sektoren außerhalb sind isoliert. Das Balance-Tool warnt bei einem physischen Zerfall oder wenn freie Händler (die Wirtschaft) Sektoren nicht
/// mehr erreichen; Sperren gegen einzelne Fraktionen sind Politik und werden nur gemeldet.
/// </summary>
public static class MapConnectivity
{
    /// <summary>Zerfall für ein Profil (Routen über <see cref="Universe.FindRoute(SectorId, SectorId, MobilityProfile)"/>).</summary>
    public static MapSplit Check(Universe u, MobilityProfile profile, string label)
    {
        var ids = u.Sectors.Keys.OrderBy(s => s.Value).ToList();
        var reach = new Dictionary<SectorId, HashSet<SectorId>>();
        foreach (var a in ids)
            reach[a] = ids.Where(b => a == b || u.FindRoute(a, b, profile) is not null).ToHashSet();
        List<SectorId> best = new();
        foreach (var a in ids)
        {
            var scc = ids.Where(b => reach[a].Contains(b) && reach[b].Contains(a)).ToList();
            if (scc.Count > best.Count) best = scc;
        }
        var isolated = ids.Where(s => !best.Contains(s)).ToList();
        return new MapSplit(label, ids.Count, best.Count, isolated);
    }

    /// <summary>Physischer Zerfall (geschlossene oder zerstörte Gates).</summary>
    public static MapSplit Physical(Universe u) => Check(u, MobilityProfile.Physical, "physisch");

    /// <summary>Zerfall für die freien Händler (Sammelgruppe „Freie“), leer und beladen — das Profil der Wirtschaft.</summary>
    public static MapSplit Economy(Universe u)
    {
        var empty = Check(u, MobilityProfile.For(u.FreeActor), "freie Händler");
        var laden = Check(u, MobilityProfile.For(u.FreeActor, laden: true), "freie Händler (beladen)");
        return laden.Isolated.Count > empty.Isolated.Count ? laden : empty;
    }

    /// <summary>Zerfall je Fraktion (Profil des Fraktionsakteurs, beladen): politische Sperren gegen diese Fraktion.</summary>
    public static IReadOnlyList<MapSplit> Factions(Universe u) =>
        u.Factions.Values.OrderBy(f => f.Id.Value).Select(f => Check(u, MobilityProfile.For(f.Id, laden: true), f.Name)).ToList();
}
