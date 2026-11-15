using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Weapons;

/// <summary>
/// HUD-Neu (Entwurf A): eine Gruppe von Schächten für den Daumenbogen — Kampfwaffen nach Waffentyp zusammengefasst (Laser, Geschütz, Rakete, Kanone),
/// Bergbauschächte (Bergbaulaser, Bohrer, Traktor, Spalter, Bergen) je einzeln. <see cref="Members"/> = Indizes in der Schachtliste.
/// </summary>
public sealed record BayGroup(string Key, string Label, IReadOnlyList<int> Members, bool Combat);

/// <summary>
/// HUD-Neu: Gruppierung der Waffenschächte eines Schiffs (reine Rechnung). Kampfwaffen gruppiert nach Waffentyp — Rakete, wenn das Modul eine
/// Raketengeschwindigkeit hat (<see cref="Missiles.IsMissile"/>), sonst nach Schadensart (Energie = Laser, kinetisch = Geschütz, explosiv = Kanone);
/// Reihenfolge Laser, Geschütz, Rakete, Kanone, danach die Bergbauschächte in Schachtreihenfolge. Ein Leviathan (10 Waffen) hat so drei Gruppen.
/// </summary>
public static class BayGroups
{
    static readonly (string Key, string Label)[] CombatKinds = { ("laser", "Laser"), ("gun", "Geschütz"), ("missile", "Rakete"), ("cannon", "Kanone") };

    /// <summary>Waffentyp-Schlüssel eines Kampfschachts (null = kein Kampfschacht oder Modul unbekannt).</summary>
    public static string? KindOf(Universe u, Ship ship, WeaponBay bay)
    {
        int slot = PlayerGunnery.SlotOf(bay.Key);
        if (slot < 0) return null;
        if (slot >= ship.Fit.Count || ship.Fit[slot] is not { } key || !u.Modules.TryGetValue(key, out var m)) return "laser";
        if (Missiles.IsMissile(m)) return "missile";
        return m.DamageType switch { DamageType.Kinetic => "gun", DamageType.Explosive => "cannon", _ => "laser" };
    }

    public static List<BayGroup> Of(Universe u, Ship ship, IReadOnlyList<WeaponBay> bays)
    {
        var byKind = new Dictionary<string, List<int>>();
        var mining = new List<BayGroup>();
        for (int i = 0; i < bays.Count; i++)
        {
            if (KindOf(u, ship, bays[i]) is { } kind)
            {
                if (!byKind.TryGetValue(kind, out var l)) byKind[kind] = l = new List<int>();
                l.Add(i);
            }
            // der Bergbaulaser heißt in der Gruppe „Abbaulaser“ (die Kampfgruppe „Laser“ gibt es auf manchen Schiffen daneben)
            else mining.Add(new BayGroup(bays[i].Key, bays[i].Key == "laser" ? "Abbaulaser" : bays[i].Name, new[] { i }, false));
        }
        var list = new List<BayGroup>();
        foreach (var (key, label) in CombatKinds)
            if (byKind.TryGetValue(key, out var members)) list.Add(new BayGroup(key, label, members, true));
        list.AddRange(mining);
        return list;
    }
}
