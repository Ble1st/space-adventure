using System.Runtime.CompilerServices;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 20: Zerlegung der Karte in physisch erreichbare Teile (Zusammenhangskomponenten über offene oder eingeschränkte Gates; geschlossene und zerstörte
/// Gates trennen). Politische Sperren zählen nicht — sie gelten nur für einzelne Akteure. Jeder Teil hat einen Schlüssel = kleinste Sektor-Id des Teils.
/// Ist die Karte nicht zerfallen, gibt es genau einen Teil. Allgemein über beliebig viele Sektoren; nichts nimmt die Startkarte an.
/// </summary>
public sealed class MapPartition
{
    readonly Dictionary<SectorId, int> _partOf;
    /// <summary>Teile in fester Reihenfolge (nach kleinster Sektor-Id); jeder Teil sortiert nach Sektor-Id.</summary>
    public IReadOnlyList<IReadOnlyList<SectorId>> Parts { get; }

    internal MapPartition(Dictionary<SectorId, int> partOf, List<IReadOnlyList<SectorId>> parts) { _partOf = partOf; Parts = parts; }

    /// <summary>Zerfallen = mehr als ein Teil.</summary>
    public bool IsSplit => Parts.Count > 1;

    /// <summary>Schlüssel des Teils, zu dem der Sektor gehört (kleinste Sektor-Id des Teils); unbekannte Sektoren: ihre eigene Id.</summary>
    public int KeyOf(SectorId sector) => _partOf.TryGetValue(sector, out var i) ? Parts[i][0].Value : sector.Value;

    /// <summary>Liegen beide Sektoren im selben Teil?</summary>
    public bool Same(SectorId a, SectorId b) => KeyOf(a) == KeyOf(b);

    /// <summary>Sektoren des Teils mit diesem Schlüssel (leer, wenn es ihn nicht gibt).</summary>
    public IReadOnlyList<SectorId> SectorsOf(int key)
    {
        foreach (var p in Parts) if (p[0].Value == key) return p;
        return Array.Empty<SectorId>();
    }
}

/// <summary>Phase 20: Erzeugt und puffert die <see cref="MapPartition"/> (je Tick, Zugangsversion und Gate-Zuständen neu).</summary>
public static class MapParts
{
    sealed class Cache { public long Tick = -1; public int Version = -1; public int Gates = -1; public int Sectors = -1; public long StateSum = -1; public MapPartition? Value; }
    static readonly ConditionalWeakTable<Universe, Cache> Caches = new();

    /// <summary>Gate physisch passierbar (offen oder eingeschränkt)?</summary>
    public static bool Passable(Gate g) => g.State is GateState.Open or GateState.Restricted;

    /// <summary>Aktuelle Zerlegung der Karte (gepuffert).</summary>
    public static MapPartition Of(Universe u)
    {
        var c = Caches.GetOrCreateValue(u);
        long sum = 0;
        foreach (var g in u.Gates.Values) sum = sum * 31 + (Passable(g) ? 1 : 2) + g.Id.Value;
        if (c.Value is not null && c.Tick == u.Tick && c.Version == u.AccessVersion && c.Gates == u.Gates.Count && c.Sectors == u.Sectors.Count && c.StateSum == sum)
            return c.Value;
        c.Value = Compute(u);
        c.Tick = u.Tick; c.Version = u.AccessVersion; c.Gates = u.Gates.Count; c.Sectors = u.Sectors.Count; c.StateSum = sum;
        return c.Value;
    }

    static MapPartition Compute(Universe u)
    {
        var ids = u.Sectors.Keys.OrderBy(s => s.Value).ToList();
        var partOf = new Dictionary<SectorId, int>();
        var parts = new List<IReadOnlyList<SectorId>>();
        foreach (var start in ids)
        {
            if (partOf.ContainsKey(start)) continue;
            int index = parts.Count;
            var members = new List<SectorId>();
            var queue = new Queue<SectorId>();
            queue.Enqueue(start); partOf[start] = index;
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                members.Add(s);
                foreach (var gid in u.Sectors[s].Gates)
                {
                    if (!u.Gates.TryGetValue(gid, out var g) || !Passable(g) || !u.Gates.TryGetValue(g.Target, out var far) || !Passable(far)) continue;
                    if (!u.Sectors.ContainsKey(far.Sector) || partOf.ContainsKey(far.Sector)) continue;
                    partOf[far.Sector] = index;
                    queue.Enqueue(far.Sector);
                }
            }
            members.Sort((a, b) => a.Value.CompareTo(b.Value));
            parts.Add(members);
        }
        return new MapPartition(partOf, parts);
    }

    /// <summary>Schlüssel des Teils, in dem ein Schiff gerade ist (unterwegs: Zielsektor der Reise).</summary>
    public static int KeyOf(Universe u, Ship ship) => Of(u).KeyOf(ship.Transit?.ToSector ?? ship.Sector);

    /// <summary>
    /// Ein geschlossenes oder zerstörtes Gate am Rand des Teils (kleinste Id) — die Ursache, warum der Teil abgeschnitten ist; null, wenn keins.
    /// </summary>
    public static GateId? CutGate(Universe u, int key)
    {
        var part = Of(u);
        foreach (var g in u.Gates.Values.OrderBy(g => g.Id.Value))
            if (!Passable(g) && part.KeyOf(g.Sector) == key) return g.Id;
        return null;
    }
}
