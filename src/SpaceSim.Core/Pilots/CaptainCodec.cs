using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;

namespace SpaceSim.Core.Pilots;

/// <summary>Bilanz eines NPC-Kapitäns im Spielstand (additiv, v17 bleibt).</summary>
public sealed class CaptainSave
{
    public int Ship { get; set; }
    public int Kills { get; set; }
    public float Rating { get; set; }
    public bool Fallen { get; set; }
    public int Role { get; set; }
    public int Owner { get; set; }
}

/// <summary>Speichern/Laden der Kapitänsbilanzen. Bewusst nicht im <c>UniverseHash</c>: reine Anzeige/Statistik, die kein System liest.</summary>
public static class CaptainCodec
{
    public static void Save(Universe u, SaveGame s)
    {
        if (u.Captains.Count == 0) return;
        s.Captains = u.Captains.OrderBy(x => x.Key.Value).Select(x => new CaptainSave
        {
            Ship = x.Key.Value, Kills = x.Value.Kills, Rating = x.Value.Rating, Fallen = x.Value.Fallen, Role = (int)x.Value.Role, Owner = x.Value.Owner.Value,
        }).ToList();
    }

    public static void Load(SaveGame s, Universe u)
    {
        if (s.Captains is null) return;
        foreach (var c in s.Captains)
            u.Captains[new ShipId(c.Ship)] = new CaptainRecord { Kills = c.Kills, Rating = c.Rating, Fallen = c.Fallen, Role = (ShipRole)c.Role, Owner = new ActorId(c.Owner) };
    }
}
