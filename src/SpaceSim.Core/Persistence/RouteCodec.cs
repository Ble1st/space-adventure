using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

/// <summary>Mehrpunkt-Route des Spielers ↔ <see cref="RouteSave"/> (additiv, Save bleibt v17).</summary>
public static class RouteCodec
{
    public static RouteSave? ToSave(PlayerRoute? r) => r is null ? null : new RouteSave
    {
        Next = r.Next, Dock = r.DockAtStations,
        Stops = r.Stops.Select(s => new RouteStopSave { Kind = (int)s.Kind, Id = s.Id, Sector = s.Sector.Value, X = s.Position.X, Y = s.Position.Y }).ToList(),
    };

    public static PlayerRoute? FromSave(RouteSave? s)
    {
        if (s is null) return null;
        var r = new PlayerRoute { Next = s.Next, DockAtStations = s.Dock };
        foreach (var x in s.Stops) r.Stops.Add(new RouteStop((RouteStopKind)x.Kind, x.Id, new SectorId(x.Sector), new Vec2(x.X, x.Y)));
        r.Next = Math.Clamp(r.Next, 0, r.Stops.Count);
        return r;
    }

    /// <summary>Hash-Beitrag (Reihenfolge der Halte).</summary>
    public static void Mix(PlayerRoute r, Action<ulong> mix)
    {
        mix(0x2047u); mix((ulong)r.Next); mix(r.DockAtStations ? 1UL : 0UL);
        foreach (var s in r.Stops)
        {
            mix((ulong)s.Kind); mix((ulong)s.Id); mix((ulong)s.Sector.Value);
            mix(BitConverter.SingleToUInt32Bits(s.Position.X)); mix(BitConverter.SingleToUInt32Bits(s.Position.Y));
        }
    }
}
