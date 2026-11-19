using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

/// <summary>Ausbau Punkt 16: Spielerwissen ↔ <see cref="IntelSave"/> (additiv, Save bleibt v17).</summary>
public static class IntelCodec
{
    public static IntelSave? ToSave(PlayerIntel? i) => i is null ? null : new IntelSave
    {
        Sectors = i.Sectors.OrderBy(kv => kv.Key.Value).ToDictionary(kv => kv.Key.Value, kv => (int)kv.Value),
        Stations = i.Stations.Select(s => s.Value).OrderBy(x => x).ToList(),
        Gates = i.Gates.Select(g => g.Value).OrderBy(x => x).ToList(),
        Fields = i.Fields.OrderBy(f => f.Sector).ThenBy(f => f.Index).Select(f => new[] { f.Sector, f.Index }).ToList(),
        GasFields = i.GasFields.OrderBy(f => f.Sector).ThenBy(f => f.Index).Select(f => new[] { f.Sector, f.Index }).ToList(),
        Factions = i.Factions.Select(f => f.Value).OrderBy(x => x).ToList(),
    };

    public static PlayerIntel? FromSave(IntelSave? s)
    {
        if (s is null) return null;
        var i = new PlayerIntel();
        foreach (var (k, v) in s.Sectors) i.Sectors[new SectorId(k)] = (SectorKnowledge)v;
        foreach (var x in s.Stations) i.Stations.Add(new StationId(x));
        foreach (var x in s.Gates) i.Gates.Add(new GateId(x));
        foreach (var f in s.Fields) if (f.Length == 2) i.Fields.Add((f[0], f[1]));
        foreach (var f in s.GasFields) if (f.Length == 2) i.GasFields.Add((f[0], f[1]));
        foreach (var x in s.Factions) i.Factions.Add(new FactionId(x));
        return i;
    }

    /// <summary>Hash-Beitrag (feste Reihenfolge).</summary>
    public static void Mix(PlayerIntel i, Action<ulong> mix)
    {
        mix(0x1E7E1u);
        foreach (var (k, v) in i.Sectors.OrderBy(kv => kv.Key.Value)) { mix((ulong)k.Value); mix((ulong)v); }
        mix(0x1E7E2u); foreach (var x in i.Stations.Select(s => s.Value).OrderBy(x => x)) mix((ulong)x);
        mix(0x1E7E3u); foreach (var x in i.Gates.Select(s => s.Value).OrderBy(x => x)) mix((ulong)x);
        mix(0x1E7E4u); foreach (var f in i.Fields.OrderBy(f => f.Sector).ThenBy(f => f.Index)) { mix((ulong)f.Sector); mix((ulong)f.Index); }
        mix(0x1E7E5u); foreach (var f in i.GasFields.OrderBy(f => f.Sector).ThenBy(f => f.Index)) { mix((ulong)f.Sector); mix((ulong)f.Index); }
        mix(0x1E7E6u); foreach (var x in i.Factions.Select(s => s.Value).OrderBy(x => x)) mix((ulong)x);
    }
}
