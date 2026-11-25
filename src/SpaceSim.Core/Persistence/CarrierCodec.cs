using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

/// <summary>Träger: eingeschleuste Bordjäger eines Trägers im Spielstand (additiv, v17 bleibt).</summary>
public sealed class HangarSave
{
    public int Carrier { get; set; }
    public List<ShipSave> Ships { get; set; } = new();
}

/// <summary>Speichern und Laden der Hangars (<see cref="Universe.Hangars"/>) — nur wenn welche belegt sind (sonst bleibt alles wie vorher).</summary>
public static class CarrierCodec
{
    public static List<HangarSave>? ToSave(Universe u)
    {
        if (u.Hangars.Count == 0) return null;
        return u.Hangars.Where(kv => kv.Value.Count > 0).OrderBy(kv => kv.Key.Value)
            .Select(kv => new HangarSave { Carrier = kv.Key.Value, Ships = kv.Value.Select(UniverseSerializer.ShipToSave).ToList() }).ToList() is { Count: > 0 } l ? l : null;
    }

    public static void FromSave(Universe u, List<HangarSave>? hangars)
    {
        if (hangars is null) return;
        foreach (var h in hangars)
        {
            var list = h.Ships.Select(sh => UniverseSerializer.ShipFromSave(u, sh)).OrderBy(s => s.Id.Value).ToList();
            if (list.Count > 0) u.Hangars[new ShipId(h.Carrier)] = list;
        }
    }
}
