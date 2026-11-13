using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 28 (KAMPF.md Stufe 28): Angriff auf eine Station als Gefecht (<see cref="EncounterCause.Siege"/>). Die Angreifer stehen auf Seite 0, die Station auf der
/// Gefechtslinie bei <see cref="EconomyRules.SiegeStartDistance"/> (im räumlichen Gefecht an ihrer echten Position). Verteidiger: die Station selbst (Geschütze,
/// solange sie steht — <see cref="StationCombat.Defends"/>), Polizei und Kriegsschiffe ihrer Fraktion im Sektor; Verstärkung alle
/// <see cref="EconomyRules.ReinforceTicks"/> (auch weitere Kriegsschiffe mit demselben Angriffsziel). Greift der Spieler an, ist das ein schwerer Angriff
/// („assault_station“) bei der Stationsfraktion.
/// </summary>
public static class Siege
{
    /// <summary>Laufende Belagerung einer Station (null = keine).</summary>
    public static Encounter? Of(Universe u, StationId station)
    {
        foreach (var e in u.Fights.Encounters) if (e.Station == station && !e.Done) return e;
        return null;
    }

    /// <summary>Belagerung beginnen: Angreifer tritt bei, Verteidiger kommen dazu, Ereignisse; im Krieg zählt sie für die Kriegsbilanz.</summary>
    public static Encounter Start(Universe u, Ship attacker, Station st)
    {
        var r = u.Rules;
        var e = Engage.Begin(u, EncounterCause.Siege, st.Sector);
        e.Station = st.Id;
        e.StationX = r.SiegeStartDistance;
        e.Victim = default;
        Engage.Join(u, e, attacker, 0, attacker.Id == u.PlayerShip ? CombatTactics.Player : CombatTactics.Siege, 0f, e.Spatial ? attacker.Position : null);
        if (u.FactionOf(attacker) is { } fa && u.FactionOf(st) is { } fd && Wars.Between(u, fa, fd) is { } war) war.Sieges++;
        string where = u.Sectors.TryGetValue(st.Sector, out var sec) ? sec.Name : "?";
        string msg = attacker.IsPlayer ? $"Du greifst {st.Name} an — die Station wehrt sich."
            : $"Gefecht in {where}: {u.ActorName(attacker.Owner)} greift {st.Name} an";
        if (attacker.IsPlayer && sec is not null) Wanted.ReportAssault(u, u.PlayerActor, sec.Id, st.Owner, "assault_station");
        u.Events.Publish(new EncounterStarted(e.Id, st.Sector, EncounterCause.Siege, attacker.Id, default, msg));
        u.Events.Publish(new SiegeStarted(e.Id, st.Id, st.Sector, attacker.Owner, msg));
        Reinforce(u, e);
        return e;
    }

    /// <summary>
    /// Gerätetest Phase 28 (Runde 6): Rückzug des Spielers aus einer Belagerung (als geflohen gewertet) — seine eigenen Schiffe im Gefecht (Verband, Begleiter)
    /// brechen ab und arbeiten ihren Befehl weiter (Verbandsmitglieder: dem Anführer folgen); bleibt kein Angreifer, endet das Gefecht im selben Takt.
    /// Ereignis <see cref="SiegeWithdrawn"/>. Ohne <see cref="EconomyRules.FleetCombat"/> nichts.
    /// </summary>
    public static void Withdraw(Universe u, Encounter e, CombatParticipant player)
    {
        if (!u.Rules.FleetCombat || e.Cause != EncounterCause.Siege || player.Side != 0 || player.Ship != u.PlayerShip) return;
        int n = 0;
        foreach (var q in e.Participants.ToList())
        {
            if (!q.Active || q.Side != player.Side || q.Ship == player.Ship || !u.Ships.TryGetValue(q.Ship, out var s) || s.Owner != player.Owner) continue;
            Engage.Leave(u, e, q, CombatExit.Fled);
            Engage.Resume(u, s);
            n++;
        }
        string name = u.Stations.TryGetValue(e.Station, out var st) ? st.Name : "Die Station";
        // Runde 7 (Handy-Test): ist die Station ausgeschaltet, gefallen oder weg, war es kein Rückzug, sondern der Sieg (vorher „Rückzug …“ im Moment von „AUS“)
        bool won = !StationCombat.Defends(u, e);
        string follow = n > 0 ? $" — dein Verband ({n}) folgt dir." : ".";
        string msg = won ? (st is null ? $"Station zerstört{follow}" : $"{name} außer Gefecht{follow}") : $"Rückzug: Angriff auf {name} abgebrochen{follow}";
        u.Events.Publish(new SiegeWithdrawn(e.Id, e.Station, player.Ship, n, msg, won));
    }

    /// <summary>
    /// Verstärkung: Polizei und Kriegsschiffe der Stationsfraktion im Sektor (nicht im Gefecht) verteidigen; Kriegsschiffe mit Angriffsbefehl auf diese Station
    /// greifen mit an. Feste Reihenfolge (Ids).
    /// </summary>
    public static void Reinforce(Universe u, Encounter e)
    {
        if (!u.Stations.TryGetValue(e.Station, out var st) || u.FactionOf(st) is not { } defender) return;
        int n = 0;
        foreach (var id in u.Tracker.ParkedIn(e.Sector).Concat(u.Tracker.TransitIn(e.Sector)).Distinct().OrderBy(i => i.Value).ToList())
        {
            if (!u.Ships.TryGetValue(id, out var s) || u.Fights.IsEngaged(s.Id) || s.Controller != Controller.Ai || ThreatMap.Where(u, s).Sector != e.Sector) continue;
            bool mine = (ThreatMap.IsPolice(s) || Wars.IsWarship(s)) && u.FactionOf(s) == defender;
            bool attacker = Wars.IsWarship(s) && s.Command is AssaultCommand ac && ac.Station == e.Station;
            if (mine && StationCombat.Defends(u, e))
            {
                // Runde 6: ein Verteidiger, der sich mit schwerem Schaden zurückgezogen hat, kommt nicht im Zehn-Takt-Rhythmus wieder (vorher Beitritt, Flucht,
                // Beitritt … mit zwei Zufallszügen und einem neuen Eintrag je Runde); wer wiederkommt, nimmt seinen alten Eintrag
                float retreat = Wars.IsWarship(s) ? u.Rules.WarshipRetreatHull : u.Rules.PoliceRetreatHull;
                if (s.Hull < retreat * s.MaxHull) continue;
                float x = e.StationX - 10f - n * Reinforcements.HelperGap;
                var at = e.Spatial ? CombatSpace.Clear(u, e.Sector, CombatSpace.Around(st.Position, s.Position, Station.BodyRadius + 15f)) : (Vec2?)null;
                if (e.Of(s.Id) is { Active: false } old && old.Side == 1)
                {
                    Engage.Rejoin(u, e, old, s);
                    old.X = x;
                    if (at is { } ap) { old.Pos = old.PrevPos = ap; s.Position = ap; }
                }
                else Engage.Join(u, e, s, 1, Wars.IsWarship(s) ? CombatTactics.Siege : CombatTactics.Police, x, at);
                n++;
            }
            else if (attacker)
                Engage.Join(u, e, s, 0, CombatTactics.Siege, 0f, e.Spatial ? s.Position : null);
        }
    }
}
