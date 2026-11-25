using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Punkt 13 (Rechenzeit): Zwischenspeicher der ausweichenden NPC-Pfade je Sektor (<see cref="Travel.SectorPath"/>). Der Pfad ist eine reine Funktion von
/// Start, Ziel, der Hindernisliste des Sektors (Felsen in Listenreihenfolge, dann Stationen) und der Planungsart (Maßstab &gt; 1 = robust). Je Sektor merkt sich
/// der Speicher einen Abzug der Hindernisse; bei jedem Aufruf wird er exakt gegen den aktuellen Stand geprüft (Anzahl, Position und Radius jedes Felsens
/// und jeder Station, Planungsart) — jede Änderung (abgebauter, nachgewachsener oder gespaltener Fels, neue oder entfernte Station, auch direkte Änderungen
/// in Tests) verwirft die Pfade dieses Sektors. Schlüssel sind die Bitmuster von Start und Ziel, das Ergebnis ist also bitgleich mit einer Neuplanung.
/// Abgeleitet, nie gespeichert und nicht im Hash; nach dem Laden leer.
/// </summary>
public sealed class SectorPathCache
{
    /// <summary>Höchstzahl gemerkter Pfade je Sektor; darüber wird der Sektor geleert (begrenzt den Speicher, ändert keine Ergebnisse).</summary>
    public const int MaxPathsPerSector = 4096;

    sealed class Entry
    {
        public Obstacle[] Snapshot = Array.Empty<Obstacle>();
        public bool Robust;
        public readonly Dictionary<(int, int, int, int), List<Vec2>> Paths = new();
    }

    readonly Dictionary<SectorId, Entry> _sectors = new();

    /// <summary>Treffer und Neuplanungen seit dem Anlegen (nur Diagnose/Tests).</summary>
    public long Hits { get; private set; }
    public long Misses { get; private set; }
    /// <summary>Verworfene Sektorstände (geänderte Hindernisse; nur Diagnose).</summary>
    public long Refreshes { get; private set; }

    /// <summary>Pfad von <paramref name="from"/> nach <paramref name="to"/> im Sektor; die zurückgegebene Liste ist geteilt und darf nicht verändert werden.</summary>
    internal IReadOnlyList<Vec2> Get(Universe u, SectorId sector, Vec2 from, Vec2 to)
    {
        if (!u.Sectors.TryGetValue(sector, out var s) || !u.PerfCaches) { Misses++; return Plan(u, sector, s, from, to, null); }
        bool robust = s.Scale > 1f;
        if (!_sectors.TryGetValue(sector, out var e)) _sectors[sector] = e = new Entry();
        if (!Matches(u, s, e, robust)) Refresh(u, s, e, robust);
        var key = (BitConverter.SingleToInt32Bits(from.X), BitConverter.SingleToInt32Bits(from.Y), BitConverter.SingleToInt32Bits(to.X), BitConverter.SingleToInt32Bits(to.Y));
        if (e.Paths.TryGetValue(key, out var pts)) { Hits++; return pts; }
        Misses++;
        pts = Plan(u, sector, s, from, to, e.Snapshot);
        if (e.Paths.Count >= MaxPathsPerSector) e.Paths.Clear();
        e.Paths[key] = pts;
        return pts;
    }

    /// <summary>Die Planung selbst (wie vor Punkt 13 in <see cref="Travel.SectorPath"/>).</summary>
    internal static List<Vec2> Plan(Universe u, SectorId sector, Sector? s, Vec2 from, Vec2 to, IReadOnlyList<Obstacle>? obs)
    {
        obs ??= u.GetObstacles(sector);
        return s is not null && s.Scale > 1f ? PathPlanner.PlanRobust(from, to, obs, PathPlanner.Margin) : PathPlanner.Plan(from, to, obs);
    }

    static bool Matches(Universe u, Sector s, Entry e, bool robust)
    {
        var snap = e.Snapshot;
        var sun = Suns.AvoidObstacle(u, s);   // Sonnen: letztes Hindernis der Liste (wie Universe.GetObstacles)
        if (e.Robust != robust || snap.Length != s.Asteroids.Count + s.Stations.Count + (sun is null ? 0 : 1)) return false;
        int i = 0;
        foreach (var a in s.Asteroids)
        {
            ref readonly var o = ref snap[i++];
            if (!Same(o, a.Position, a.Radius)) return false;
        }
        foreach (var id in s.Stations)
        {
            ref readonly var o = ref snap[i++];
            if (!u.Stations.TryGetValue(id, out var st) || !Same(o, st.Position, st.Radius)) return false;
        }
        if (sun is { } so && !Same(snap[i], so.Position, so.Radius)) return false;
        return true;
    }

    static bool Same(in Obstacle o, Vec2 p, float r) =>
        BitConverter.SingleToInt32Bits(o.Position.X) == BitConverter.SingleToInt32Bits(p.X)
        && BitConverter.SingleToInt32Bits(o.Position.Y) == BitConverter.SingleToInt32Bits(p.Y)
        && BitConverter.SingleToInt32Bits(o.Radius) == BitConverter.SingleToInt32Bits(r);

    void Refresh(Universe u, Sector s, Entry e, bool robust)
    {
        Refreshes++;
        e.Snapshot = u.GetObstacles(s.Id).ToArray();
        e.Robust = robust;
        e.Paths.Clear();
    }
}
