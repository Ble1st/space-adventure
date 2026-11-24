using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Trading;

/// <summary>
/// Lücke gegenüber Interstellar Pilot 2: Fracht zwischen zwei eigenen Schiffen umladen. Rein (keine Zufallszahl, keine Kosten):
/// beide Schiffe gehören dem Spielerakteur, stehen im selben Sektor, sind nicht unterwegs und nicht im Gefecht, und liegen entweder an derselben Station
/// angedockt oder beide frei im Raum höchstens <see cref="SpaceRange"/> auseinander. Die Menge je Ware begrenzt der freie Platz der Zielform
/// (<see cref="Ship.FreeUnits"/>, versiegelte Auftragsfracht belegt mit); versiegelte Auftragsfracht selbst (<see cref="Ship.MissionCargo"/>) ist keine Ware
/// und wird nie umgeladen. Fehlschlag ändert nichts und meldet nichts; Erfolg meldet <see cref="CargoTransferred"/>.
/// </summary>
public static class CargoTransfer
{
    /// <summary>Höchster Abstand zweier frei im Raum liegender Schiffe (Einheiten).</summary>
    public const float SpaceRange = 40f;

    /// <summary>Warum diese zwei Schiffe nichts tauschen können (null = möglich).</summary>
    public static string? PairIssue(Universe u, Ship from, Ship to)
    {
        if (from.Id == to.Id) return "Gleiches Schiff";
        if (from.Owner != u.PlayerActor || to.Owner != u.PlayerActor) return "Nur zwischen eigenen Schiffen";
        if (from.Sector != to.Sector) return "Nicht im selben Sektor";
        if (from.Transit is not null || to.Transit is not null) return "Ein Schiff ist unterwegs";
        if (u.Fights.IsEngaged(from.Id) || u.Fights.IsEngaged(to.Id)) return "Gefecht";
        if (from.DockedAt.Value != 0 || to.DockedAt.Value != 0)
            return from.DockedAt == to.DockedAt ? null : "Nicht an derselben Station angedockt";
        if (Vec2.Distance(from.Position, to.Position) > SpaceRange) return $"Zu weit entfernt (höchstens {SpaceRange:0} m)";
        return null;
    }

    /// <summary>Eigene Schiffe, mit denen <paramref name="ship"/> gerade Fracht tauschen kann (Id-Reihenfolge).</summary>
    public static List<Ship> Partners(Universe u, Ship ship)
    {
        var list = new List<Ship>();
        if (ship.Owner != u.PlayerActor) return list;
        foreach (var o in u.ShipsById())
            if (o.Id != ship.Id && o.Owner == u.PlayerActor && PairIssue(u, ship, o) is null) list.Add(o);
        return list;
    }

    /// <summary>Freie Einheiten der Ware im Zielschiff (Frachtform-Regel, versiegelte Fracht belegt mit).</summary>
    public static float Room(Universe u, Ship to, GoodId good)
    {
        if (!u.Goods.TryGetValue(good, out var g)) return 0f;
        float vol = MathF.Max(0.0001f, g.VolumePerUnit);
        float total = (to.CargoCapacity - to.UsedVolume(u) - to.MissionCargo) / vol;
        return MathF.Max(0f, MathF.Min(to.FreeUnits(u, good), total));
    }

    /// <summary>Höchstmenge, die jetzt umgeladen werden kann (0 = nichts).</summary>
    public static float MaxUnits(Universe u, Ship from, Ship to, GoodId good)
    {
        if (PairIssue(u, from, to) is not null) return 0f;
        float have = from.Cargo.GetValueOrDefault(good);
        if (have <= 0f) return 0f;
        return MathF.Min(have, Room(u, to, good));
    }

    /// <summary>Warum diese Ware nicht umgeladen werden kann (null = möglich).</summary>
    public static string? Issue(Universe u, Ship from, Ship to, GoodId good)
    {
        if (PairIssue(u, from, to) is { } p) return p;
        if (from.Cargo.GetValueOrDefault(good) <= 0f) return "Nichts an Bord";
        if (Room(u, to, good) < 0.0001f) return "Kein Platz im Zielschiff";
        return null;
    }

    /// <summary>
    /// Lädt bis zu <paramref name="units"/> Einheiten um (≤ 0 = so viel wie möglich), begrenzt auf Bestand und freien Platz; liefert die umgeladene Menge.
    /// Ein Rest unter 0,0001 im Quellschiff wird mitgenommen, wenn er noch passt.
    /// </summary>
    public static float Transfer(Universe u, Ship from, Ship to, GoodId good, float units)
    {
        if (Issue(u, from, to, good) is not null) return 0f;
        float max = MaxUnits(u, from, to, good);
        float n = !float.IsFinite(units) || units <= 0f ? max : MathF.Min(units, max);
        if (n <= 0f) return 0f;
        float have = from.Cargo[good];
        if (have - n <= 0.0001f) { n = have; from.Cargo.Remove(good); }
        else from.Cargo[good] = have - n;
        to.Cargo[good] = to.Cargo.GetValueOrDefault(good) + n;
        u.Events.Publish(new CargoTransferred(from.Id, to.Id, good, n));
        return n;
    }
}
