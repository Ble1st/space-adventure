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
    public static Encounter? Of(Universe u, StationId station) => SiegeTargets.SiegeOf(u, SiegeRef.Of(station));

    /// <summary>Phase 31: laufende Belagerung dieser Tor-Hälfte (null = keine).</summary>
    public static Encounter? OfGate(Universe u, GateId gate) => SiegeTargets.SiegeOf(u, SiegeRef.Of(gate));

    /// <summary>Belagerung beginnen: Angreifer tritt bei, Verteidiger kommen dazu, Ereignisse; im Krieg zählt sie für die Kriegsbilanz.</summary>
    public static Encounter Start(Universe u, Ship attacker, Station st) => Start(u, attacker, new StationTarget(u, st));

    /// <summary>
    /// Phase 31: Belagerung eines beliebigen Ziels (Station oder Tor-Hälfte). Greift der Spieler an, ist das ein schweres Verbrechen bei den Verteidigern
    /// („assault_station“ bzw. „assault_gate“ bei beiden Endfraktionen eines Tors).
    /// </summary>
    public static Encounter Start(Universe u, Ship attacker, ISiegeTarget t, bool reportCrime = true)
    {
        var r = u.Rules;
        var e = Engage.Begin(u, EncounterCause.Siege, t.Sector);
        if (t.Ref.Kind == SiegeKind.Gate) e.Gate = t.Ref.Gate; else e.Station = t.Ref.Station;
        e.StationX = r.SiegeStartDistance;
        e.Victim = default;
        Engage.Join(u, e, attacker, 0, attacker.Id == u.PlayerShip ? CombatTactics.Player : CombatTactics.Siege, 0f, e.Spatial ? attacker.Position : null);
        if (u.FactionOf(attacker) is { } fa)
            foreach (var fd in t.Defenders) if (Wars.Between(u, fa, fd) is { } war) { war.Sieges++; break; }
        string where = u.Sectors.TryGetValue(t.Sector, out var sec) ? sec.Name : "?";
        bool gate = t.Ref.Kind == SiegeKind.Gate;
        string msg = attacker.IsPlayer ? (gate ? $"Du greifst {t.Name} an — schweres Verbrechen bei beiden Endfraktionen." : $"Du greifst {t.Name} an — die Station wehrt sich.")
            : $"Gefecht in {where}: {u.ActorName(attacker.Owner)} greift {t.Name} an";
        if (attacker.IsPlayer && sec is not null && reportCrime)
        {
            // Runde 40: Angriffe auf Stationen/Tore des Kriegsgegners sind kein Verbrechen
            bool atWar = gate ? t.Defenders.Any(d => Wars.AtWar(u, u.PlayerActor, d)) : Wars.AtWar(u, u.PlayerActor, t.Owner);
            if (!atWar)
            {
                if (gate && t.Defenders.Count > 0) foreach (var f in t.Defenders) Wanted.Report(u, u.PlayerActor, f, t.Crime);
                else Wanted.ReportAssault(u, u.PlayerActor, sec.Id, t.Owner, t.Crime);
            }
        }
        u.Events.Publish(new EncounterStarted(e.Id, t.Sector, EncounterCause.Siege, attacker.Id, default, msg));
        if (gate) u.Events.Publish(new GateAttacked(e.Id, t.Ref.Gate, t.Sector, attacker.Owner, msg));
        else
        {
            u.Events.Publish(new SiegeStarted(e.Id, t.Ref.Station, t.Sector, attacker.Owner, msg));
            if (u.Stations.TryGetValue(t.Ref.Station, out var ast)) EconomicHoldout.OnAttacked(u, ast);   // Alarm: Verteidiger aus der Nähe
        }
        Reinforce(u, e);
        return e;
    }

    /// <summary>
    /// Phase 30: Abwehrgefecht — eine Waffenstellung eröffnet von sich aus das Feuer auf einen Gegner in Reichweite. Dieselbe Mechanik wie eine Belagerung (die
    /// Stellung verteidigt sich, Polizei/Marine ihrer Fraktion im Sektor helfen), aber ohne Kriegszählung und ohne Verbrechen: das Ziel hat nicht angegriffen.
    /// Das Ziel steht auf der Angreiferseite (Spieler mit seiner Steuerung, NPC mit der Belagerungstaktik — er wehrt sich oder flieht nach seinen Regeln).
    /// </summary>
    public static Encounter StartDefense(Universe u, Station st, Ship target)
    {
        var r = u.Rules;
        var e = Engage.Begin(u, EncounterCause.Siege, st.Sector);
        e.Station = st.Id;
        e.StationX = MathF.Max(0f, Vec2.Distance(target.Position, st.Position));
        e.Victim = default;
        Engage.Join(u, e, target, 0, target.Id == u.PlayerShip ? CombatTactics.Player : CombatTactics.Siege, 0f, e.Spatial ? target.Position : null);
        string where = u.Sectors.TryGetValue(st.Sector, out var sec) ? sec.Name : "?";
        string msg = target.Id == u.PlayerShip ? $"{st.Name} eröffnet das Feuer auf dich!" : $"Gefecht in {where}: {st.Name} eröffnet das Feuer";
        u.Events.Publish(new EncounterStarted(e.Id, st.Sector, EncounterCause.Siege, target.Id, default, msg));
        u.Events.Publish(new OutpostEngaged(e.Id, st.Id, target.Id, msg));
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
        var target = SiegeTargets.Of(u, e);
        string name = target?.Name ?? "Die Station";
        // Runde 7 (Handy-Test): ist die Station ausgeschaltet, gefallen oder weg, war es kein Rückzug, sondern der Sieg (vorher „Rückzug …“ im Moment von „AUS“)
        bool won = !SiegeTargets.Defends(u, e);
        string follow = n > 0 ? $" — dein Verband ({n}) folgt dir." : ".";
        string msg = won ? (target is null ? $"Station zerstört{follow}" : $"{name} außer Gefecht{follow}") : $"Rückzug: Angriff auf {name} abgebrochen{follow}";
        u.Events.Publish(new SiegeWithdrawn(e.Id, e.Station, player.Ship, n, msg, won));
    }

    /// <summary>
    /// Verstärkung: Polizei und Kriegsschiffe der Stationsfraktion im Sektor (nicht im Gefecht) verteidigen; Kriegsschiffe mit Angriffsbefehl auf diese Station
    /// greifen mit an. Feste Reihenfolge (Ids).
    /// </summary>
    public static void Reinforce(Universe u, Encounter e)
    {
        if (SiegeTargets.Of(u, e) is not { } st || st.Defenders.Count == 0) return;
        // Phase 31: Fraktionen der Angreifer verteidigen nicht (ein Tor kann einer Angreiferfraktion gehören — ihr Ende verteidigt sie nicht gegen sich selbst)
        var attackers = new HashSet<int>();
        foreach (var q in e.Participants)   // nur KI-Angreifer (der Spieler ist Mitglied, aber nicht Teil seiner Fraktion im Krieg)
            if (q.Side == 0 && u.Ships.TryGetValue(q.Ship, out var qs) && !qs.IsPlayer && u.FactionOf(q.Owner) is { } af) attackers.Add(af.Value);
        var defenders = st.Defenders.Where(f => !attackers.Contains(f.Value)).ToList();
        // Wirtschaftsstationen halten stand: beim Alarm verteidigen auch Verbündete des Besitzers
        if (st.Ref.Kind == SiegeKind.Station && u.Stations.TryGetValue(st.Ref.Station, out var ownSt))
            foreach (var a in EconomicHoldout.AlliedDefenders(u, ownSt)) if (!attackers.Contains(a.Value) && !defenders.Contains(a)) defenders.Add(a);
        if (defenders.Count == 0 && st.Ref.Kind == SiegeKind.Station) return;
        int n = 0;
        foreach (var id in u.Tracker.ParkedIn(e.Sector).Concat(u.Tracker.TransitIn(e.Sector)).Distinct().OrderBy(i => i.Value).ToList())
        {
            if (!u.Ships.TryGetValue(id, out var s) || u.Fights.IsEngaged(s.Id) || s.Controller != Controller.Ai || ThreatMap.Where(u, s).Sector != e.Sector) continue;
            bool attacker = Wars.IsWarship(s) && s.Command is AssaultCommand ac && (ac.Gate.Value != 0 ? SiegeRef.Of(ac.Gate) : SiegeRef.Of(ac.Station)) == e.Target;
            bool mine = !attacker && (ThreatMap.IsPolice(s) || Wars.IsWarship(s)) && u.FactionOf(s) is { } sf && defenders.Contains(sf);
            if (mine && SiegeTargets.Defends(u, e))
            {
                // Runde 6: ein Verteidiger, der sich mit schwerem Schaden zurückgezogen hat, kommt nicht im Zehn-Takt-Rhythmus wieder (vorher Beitritt, Flucht,
                // Beitritt … mit zwei Zufallszügen und einem neuen Eintrag je Runde); wer wiederkommt, nimmt seinen alten Eintrag
                float retreat = Wars.IsWarship(s) ? u.Rules.WarshipRetreatHull : u.Rules.PoliceRetreatHull;
                if (s.Hull < retreat * s.MaxHull) continue;
                float x = e.StationX - 10f - n * Reinforcements.HelperGap;
                var at = e.Spatial ? CombatSpace.Clear(u, e.Sector, CombatSpace.Around(st.Position, s.Position, st.Radius + 15f)) : (Vec2?)null;
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
