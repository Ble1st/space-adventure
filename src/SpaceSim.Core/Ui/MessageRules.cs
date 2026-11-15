using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Ui;

/// <summary>Runde 38a: Gewicht einer Meldung — Wichtig (betrifft dich: groß), Info (geschieht in deinem Sektor: klein, leise), Aus (nicht zeigen).</summary>
public enum MessageWeight { Off, Info, Important }

/// <summary>
/// Runde 38a (Handy-Abschlusstest z1/z2: „Einheitsschürfer 60 #13 … springt auf der Flucht in diesen Sektor“ groß in der Bildmitte): eine Regel für das Gewicht
/// von Meldungen. Wichtig ist, was dich betrifft — dein aktives Schiff, deine Schiffe, deine Kasse/Fahndung, ein Gefecht, an dem du teilnimmst. Fremdes
/// Geschehen in deinem Sektor ist Info (klein, kurz); fremdes Geschehen anderswo ist aus. Reine Rechnung, keine Zustandsänderung.
/// </summary>
public static class MessageRules
{
    /// <summary>Ist das Schiff deins (aktiv oder Flotte)?</summary>
    public static bool Mine(Universe u, ShipId id) => id.Value != 0 && (id == u.PlayerShip || u.Ships.TryGetValue(id, out var s) && s.IsPlayer);

    /// <summary>Nimmt eines deiner Schiffe an diesem Gefecht teil?</summary>
    public static bool InMyFight(Universe u, int encounter) =>
        u.Fights.Encounters.FirstOrDefault(x => x.Id == encounter) is { } e && e.Participants.Any(p => Mine(u, p.Ship));

    /// <summary>Gewicht der Meldungen zum Flottenkampf (<see cref="FleetInfo.Toast"/>): Verstärkung in deinem Gefecht wichtig, sonst Info.</summary>
    public static MessageWeight FleetWeight(Universe u, SimEvent e) => e switch
    {
        CombatJoined cj when InMyFight(u, cj.Encounter) || Mine(u, cj.Ship) => MessageWeight.Important,
        RefugeReached rr when Mine(u, rr.Ship) || InMyFight(u, rr.Encounter) => MessageWeight.Important,
        PursuitStarted ps when Mine(u, ps.Target) => MessageWeight.Important,
        EscortAssigned ea when Mine(u, ea.Ward) => MessageWeight.Important,
        _ => MessageWeight.Info,
    };

    /// <summary>
    /// Runde 38a (Handy-Abschlusstest k7: der zweite Gegner verschwand ohne Meldung): ein Gegner verlässt dein Gefecht — Text dazu (null = kein Gegner von dir).
    /// Zu jedem Ausgang (zerstört, geflohen, ergeben, abgebrochen) gibt es einen Satz.
    /// </summary>
    public static string? EnemyExit(Universe u, CombatExited x)
    {
        var e = u.Fights.Encounters.FirstOrDefault(f => f.Id == x.Encounter);
        if (e is null) return null;
        var me = e.Participants.FirstOrDefault(p => p.Ship == u.PlayerShip);
        var them = e.Of(x.Ship);
        if (me is null || them is null || them.Side == me.Side || Mine(u, x.Ship)) return null;
        string name = u.Ships.TryGetValue(x.Ship, out var s) ? PlayerCombat.Describe(u, s) : $"Schiff #{x.Ship.Value}";
        return x.Exit switch
        {
            CombatExit.Destroyed => $"Gegner zerstört: {name}",
            CombatExit.Fled => $"Gegner geflohen: {name}",
            CombatExit.Surrendered => $"Gegner ergibt sich: {name}",
            CombatExit.Looted => $"Gegner dreht mit Beute ab: {name}",
            CombatExit.Timeout or CombatExit.Ended => $"Gegner bricht das Gefecht ab: {name}",
            _ => null,
        };
    }
}
