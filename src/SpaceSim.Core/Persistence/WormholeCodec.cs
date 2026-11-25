using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

/// <summary>Instabiles Wurmloch im Spielstand (additiv, v17 bleibt).</summary>
public sealed class UnstableWormholeSave
{
    public int Id { get; set; }
    public int Sector { get; set; }
    public Vec2 Pos { get; set; }
    public int Target { get; set; }
    public Vec2 TargetPos { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
    public bool Traversed { get; set; }
}

/// <summary>Speichern, Laden und Hash der instabilen Wurmlöcher — nur wenn je eines lief (sonst bleibt alles wie vorher).</summary>
public static class WormholeCodec
{
    public static void Save(Universe u, SaveGame s)
    {
        if (u.NextWormholeId != 1) s.NextWormholeId = u.NextWormholeId;
        if (u.UnstableWormholes.Count > 0)
            s.UnstableWormholes = u.UnstableWormholes.Select(w => new UnstableWormholeSave
            {
                Id = w.Id, Sector = w.Sector.Value, Pos = w.Position, Target = w.Target.Value, TargetPos = w.TargetPosition, Start = w.StartTick, End = w.EndTick, Traversed = w.Traversed,
            }).ToList();
    }

    public static void Load(SaveGame s, Universe u)
    {
        u.NextWormholeId = Math.Max(1, s.NextWormholeId);
        if (s.UnstableWormholes is null) return;
        foreach (var w in s.UnstableWormholes)
            u.UnstableWormholes.Add(new UnstableWormhole
            {
                Id = w.Id, Sector = new SectorId(w.Sector), Position = w.Pos, Target = new SectorId(w.Target), TargetPosition = w.TargetPos,
                StartTick = w.Start, EndTick = w.End, Traversed = w.Traversed,
            });
    }

    /// <summary>Hash-Anteil (nur wenn je ein instabiles Wurmloch aufriss).</summary>
    public static void Mix(Universe u, Action<ulong> mix, Action<float> mixF)
    {
        if (u.NextWormholeId == 1 && u.UnstableWormholes.Count == 0) return;
        mix(0x574F0001UL); mix((ulong)u.NextWormholeId);
        foreach (var w in u.UnstableWormholes)
        {
            mix((ulong)w.Id); mix((ulong)w.Sector.Value); mixF(w.Position.X); mixF(w.Position.Y); mix((ulong)w.Target.Value);
            mixF(w.TargetPosition.X); mixF(w.TargetPosition.Y); mix((ulong)w.StartTick); mix((ulong)w.EndTick); mix(w.Traversed ? 1UL : 0UL);
        }
    }
}
