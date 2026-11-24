using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

/// <summary>Universumsereignisse im Spielstand (additiv, v17 bleibt).</summary>
public sealed class UniverseEventSave
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";
    public int Sector { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
    public string Detail { get; set; } = "";
    public List<int>? Asteroids { get; set; }
    public List<int>? Wrecks { get; set; }
}

/// <summary>Speichern, Laden und Hash der Universumsereignisse — nur wenn je eines lief (sonst bleibt alles wie vorher).</summary>
public static class UniverseEventCodec
{
    public static void Save(Universe u, SaveGame s)
    {
        if (u.NextUniverseEventId != 1) s.NextUniverseEventId = u.NextUniverseEventId;
        if (u.UniverseEvents.Count > 0)
            s.UniverseEvents = u.UniverseEvents.Select(e => new UniverseEventSave
            {
                Id = e.Id, Kind = e.Kind, Sector = e.Sector.Value, X = e.Position.X, Y = e.Position.Y, Start = e.StartTick, End = e.EndTick, Detail = e.Detail,
                Asteroids = e.Asteroids.Count == 0 ? null : e.Asteroids.ToList(), Wrecks = e.Wrecks.Count == 0 ? null : e.Wrecks.ToList(),
            }).ToList();
    }

    public static void Load(SaveGame s, Universe u)
    {
        u.NextUniverseEventId = Math.Max(1, s.NextUniverseEventId);
        if (s.UniverseEvents is null) return;
        foreach (var e in s.UniverseEvents)
            u.UniverseEvents.Add(new UniverseEvent
            {
                Id = e.Id, Kind = e.Kind, Sector = new SectorId(e.Sector), Position = new Vec2(e.X, e.Y), StartTick = e.Start, EndTick = e.End, Detail = e.Detail,
                Asteroids = e.Asteroids?.ToList() ?? new(), Wrecks = e.Wrecks?.ToList() ?? new(),
            });
    }

    /// <summary>Hash-Anteil (nur wenn je ein Ereignis lief; Detail ist reine Anzeige und wird nicht gemischt).</summary>
    public static void Mix(Universe u, Action<ulong> mix, Action<float> mixF, Action<string> mixString)
    {
        if (u.NextUniverseEventId == 1 && u.UniverseEvents.Count == 0) return;
        mix(0x0E7E0001UL); mix((ulong)u.NextUniverseEventId);
        foreach (var e in u.UniverseEvents)
        {
            mix((ulong)e.Id); mixString(e.Kind); mix((ulong)e.Sector.Value); mixF(e.Position.X); mixF(e.Position.Y); mix((ulong)e.StartTick); mix((ulong)e.EndTick);
            mix(0x0E7E0002UL); foreach (var a in e.Asteroids) mix((ulong)a);
            mix(0x0E7E0003UL); foreach (var w in e.Wrecks) mix((ulong)w);
        }
    }
}
