using SpaceSim.Core.Model;

namespace SpaceSim.Core.Content;

/// <summary>
/// Welle B, Punkt 1: Werftregale großer Module (Größe M und mehr) bekommen einen Wunschbestand von mindestens
/// <see cref="EconomyRules.YardLargeModuleTarget"/> — vor dem Bau der Welt in der Weltbeschreibung (wie <c>GuidanceContent</c>), damit Typ und
/// Stationen dieselben Ziele tragen. Regel 0 = nichts (Welt bitgleich).
/// </summary>
public static class WelleBContent
{
    public static void Apply(WorldDef def)
    {
        int target = def.Rules?.YardLargeModuleTarget ?? 0;
        if (target <= 0) return;
        var keys = def.Rules?.YardLargeModuleKeys;
        var large = keys is { Count: > 0 } ? keys.ToHashSet(StringComparer.Ordinal)
            : def.Modules.Where(m => m.Size is "M" or "L" or "XL").Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var t in def.StationTypes)
        {
            if (t.ShipDealer is null || t.Stocks is null) continue;
            foreach (var (key, v) in t.Stocks)
                if (large.Contains(key) && v.Length >= 2 && v[1] < target) v[1] = target;
        }
    }
}
