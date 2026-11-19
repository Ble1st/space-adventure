using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>Ein Schiff der Spielerflotte greift an (Befehl Angreifen, Verfolgen, Tor blockieren). Nur Ereignisanzeige; nicht gespeichert.</summary>
public sealed record FleetAttackStarted(ShipId Ship, ShipId TargetShip, StationId Station, GateId Gate, bool Crime, string Text) : SimEvent;

/// <summary>
/// Ausbau Punkt 1/1b: Kampfregeln der Spielerflotte. Ein Flottenschiff greift ein Schiff an wie die Person selbst (<see cref="PlayerCombat.Engage"/>):
/// es tritt dem laufenden Gefecht des Ziels auf der Gegenseite bei oder eröffnet ein Gefecht „Angriff“; Stationen und Tore belagert es über die bestehende
/// Belagerung (<see cref="Siege.Start"/>, <see cref="ISiegeTarget"/>). Außerhalb eines Kriegs ist das ein Verbrechen mit derselben Meldung wie ein eigener
/// Schuss (<see cref="Wanted.ReportAssault"/>, „assault_station“/„assault_gate“). Verhaltensregeln gelten: „Passiv“ und „Feuer halten“ greifen nicht an.
/// Unter der Rückzugsschwelle zieht sich das Schiff zur Reparatur zurück (Notfall-Ebene). Nur Spielerschiffe — die NPC-Welt ist unberührt.
/// </summary>
public static class FleetAttack
{
    /// <summary>Abstand (gleicher Sektor), ab dem ein Flottenschiff sein Zielschiff stellt.</summary>
    public const float EngageRange = 60f;
    /// <summary>Frist eines Verfolgungsbefehls der Spielerflotte (Ticks).</summary>
    public const int PursuitTicks = 2400;
    /// <summary>Abstand, in dem ein Schiff vor einem Ziel hält (nicht am Andockpunkt — sonst läge es mit Liegeplatz und zahlte Gebühr).</summary>
    public const float Standoff = 25f;
    /// <summary>Ticks zwischen zwei Neuplanungen der Reise zum Zielschiff.</summary>
    public const int ReplanTicks = 10;

    /// <summary>Greift die Verhaltensregel an? null = ja, sonst der Grund.</summary>
    public static string? RulesIssue(Universe u, Ship ship) => Squadrons.RulesOf(u, ship) switch
    {
        Engagement.Passive => "Verhaltensregel „Passiv“: das Schiff greift nicht an.",
        Engagement.HoldFire => "Verhaltensregel „Feuer halten“: das Schiff greift nicht an.",
        _ => null,
    };

    /// <summary>Wäre ein Angriff auf dieses Ziel ein Verbrechen (kein Gesetzloser, kein Kriegsgegner)?</summary>
    public static bool IsCrime(Universe u, ActorId targetOwner) => !PlayerCombat.IsOutlaw(u, targetOwner) && !Wars.AtWar(u, u.PlayerActor, targetOwner);

    /// <summary>Ziel eines Angriffsbefehls auf eine Station oder ein Tor (null = Schiffsziel oder weg).</summary>
    public static ISiegeTarget? SiegeTargetOf(Universe u, AttackCommand a)
    {
        if (a.Gate.Value != 0) return u.Gates.TryGetValue(a.Gate, out var g) ? new GateTarget(u, g) : null;
        if (a.Station.Value != 0) return u.Stations.TryGetValue(a.Station, out var st) && !st.IsBuildSite ? new StationTarget(u, st) : null;
        return null;
    }

    /// <summary>Kann ein Spielerschiff dieses Ziel angreifen? null = ja, sonst der Grund (ohne Erreichbarkeit).</summary>
    public static string? TargetIssue(Universe u, Ship ship, AttackCommand a)
    {
        if (a.Ship.Value != 0)
        {
            if (!u.Ships.TryGetValue(a.Ship, out var t)) return "Zielschiff unbekannt.";
            if (t.Id == ship.Id) return "Ein Schiff greift sich nicht selbst an.";
            if (t.IsPlayer) return "Eigene Schiffe greift die Flotte nicht an.";
            return null;
        }
        if (a.Gate.Value != 0)
        {
            if (!u.Rules.GateCombat || !u.Gates.TryGetValue(a.Gate, out var g)) return "Tor unbekannt.";
            if (GateCombat.IsDown(u, g)) return "Das Tor ist schon außer Betrieb.";
            if (GateCombat.InGrace(u, g)) return "Das Tor ist im Notbetrieb und nicht angreifbar.";
            return null;
        }
        if (!u.Stations.TryGetValue(a.Station, out var st) || st.IsBuildSite) return "Station unbekannt.";
        if (st.Owner == u.PlayerActor) return "Eigene Stationen greift die Flotte nicht an.";
        if (StationCombat.IsDown(u, st)) return "Die Station ist schon ausgeschaltet.";
        return null;
    }

    /// <summary>Gilt das Belagerungsziel noch (steht, gehört nicht dem Spieler)?</summary>
    public static bool StillTarget(Universe u, ISiegeTarget t) =>
        !t.IsDown && !(t is StationTarget st && st.Station.Owner == u.PlayerActor) && !(t is GateTarget gt && GateCombat.InGrace(u, gt.Gate));

    /// <summary>
    /// Zieht sich ein angeschlagenes Flottenschiff zurück? Unter der Rückzugsschwelle seiner Begleit-Taktik startet die Notfall-Ebene (Reparatur, dann geht
    /// der Befehl weiter) — sonst stürzte es sich nach jeder Flucht sofort wieder ins Gefecht. true = Rückzug eingeleitet.
    /// </summary>
    public static bool Retreat(Universe u, Ship ship)
    {
        if (ship.Emergency || ship.Hull >= u.Rules.EscortRetreatHull * ship.MaxHull) return false;
        OrderMove.StopHere(u, ship);
        ship.Emergency = true;
        ship.CommandStep = 0;
        ship.NextThinkTick = u.Tick;
        u.Events.Publish(new EmergencyStarted(ship.Id, "Rückzug: Hülle schwer beschädigt"));
        return true;
    }

    /// <summary>
    /// Stellt ein Zielschiff: tritt dem laufenden Gefecht des Ziels auf der Gegenseite bei oder eröffnet ein Gefecht „Angriff“ (Flottenschiff Seite 0 mit der
    /// Begleit-Taktik, Ziel Seite 1). Außerhalb eines Kriegs Verbrechen bei Opferfraktion und Sektorbesitzer; eine Streife der Sektorfraktion greift mit der
    /// Antwortchance der Überfälle ein (eine Zufallszahl, nur bei einem Verbrechen). null = nicht möglich.
    /// </summary>
    public static Encounter? EngageShip(Universe u, Ship ship, Ship target)
    {
        if (u.Fights.IsEngaged(ship.Id) || target.IsPlayer || (target.DockedAt.Value != 0 && !u.Fights.IsEngaged(target.Id))) return null;
        var (sid, spos) = ThreatMap.Where(u, ship);
        var (tsid, tpos) = ThreatMap.Where(u, target);
        if (sid != tsid || !u.Sectors.TryGetValue(sid, out var sector)) return null;
        var r = u.Rules;
        bool crime = IsCrime(u, target.Owner);
        Encounter e;
        if (u.Fights.Of(target.Id) is { } theirs && theirs.Of(target.Id) is { Active: true } them)
        {
            // gegen ein Schiff, das schon kämpft: auf der Gegenseite beitreten (z. B. gegen einen Kaperer, der einen Händler überfällt)
            if (theirs.Participants.Any(q => q.Active && q.Side == them.Side && u.Ships.TryGetValue(q.Ship, out var qs) && qs.IsPlayer)) return null;   // kämpft auf Spielerseite
            e = theirs;
            Reinforcements.JoinSide(u, e, ship, 1 - them.Side, "attack", them);
        }
        else
        {
            e = Engage.Begin(u, EncounterCause.Assault, sid);
            e.Victim = target.Id;
            float d = MathF.Max(1f, Vec2.Distance(spos, tpos));
            Engage.Join(u, e, ship, 0, CombatTactics.Escort, 0f);
            var tp = Engage.Join(u, e, target, 1, PlayerCombat.TacticOf(target), d);
            if (crime)
            {
                // wie der eigene Schuss: im Schutzradius einer Station deren Polizei sicher, sonst die Streife der Sektorfraktion mit der Antwortchance
                var police = PirateSystem.PoliceIn(u, sid).Where(p => p.Id != target.Id && !u.Fights.IsEngaged(p.Id)).ToList();
                bool guarded = PlayerCombat.GuardOf(u, sid, tpos) is not null;
                if (police.Count > 0 && (guarded || u.Rng.NextDouble() < MathF.Min(r.PoliceResponseMax, r.PoliceResponseChance * police.Count)))
                {
                    e.Police = police[0].Id;
                    Engage.Join(u, e, police[0], 1, CombatTactics.Police, tp.X + r.CombatHelperDistance,
                        e.Spatial ? CombatSpace.Clear(u, sid, CombatSpace.Around(target.Position, police[0].Position, r.CombatHelperDistance)) : null);
                }
            }
            u.Events.Publish(new EncounterStarted(e.Id, sid, EncounterCause.Assault, ship.Id, target.Id,
                $"Gefecht in {sector.Name}: dein Schiff #{ship.Id.Value} greift {PlayerCombat.Describe(u, target)} an"));
            Reinforcements.Step(u, e);   // Verband des Angreifers, Rudel/Streife des Ziels
        }
        if (crime) Wanted.ReportAssault(u, u.PlayerActor, sid, target.Owner, PlayerCombat.GuardOf(u, sid, tpos) is not null ? "assault_station" : "assault");
        u.Events.Publish(new FleetAttackStarted(ship.Id, target.Id, default, default, crime,
            crime ? $"Schiff #{ship.Id.Value} greift {PlayerCombat.Describe(u, target)} an — Verbrechen (Fahndung)." : $"Schiff #{ship.Id.Value} greift {PlayerCombat.Describe(u, target)} an."));
        return e;
    }

    /// <summary>
    /// Belagert eine Station oder ein Tor: tritt einer laufenden Belagerung auf der Angreiferseite bei, sonst beginnt es sie (<see cref="Siege.Start"/> meldet
    /// das Verbrechen). Beim Beitritt zu einer fremden Belagerung meldet es das Verbrechen selbst (dieselbe Regel).
    /// </summary>
    public static Encounter Besiege(Universe u, Ship ship, ISiegeTarget t)
    {
        Encounter e;
        if (SiegeTargets.SiegeOf(u, t.Ref) is { } running)
        {
            bool playerThere = running.Participants.Any(q => q.Active && q.Side == 0 && u.Ships.TryGetValue(q.Ship, out var qs) && qs.IsPlayer);
            Engage.Join(u, running, ship, 0, CombatTactics.Siege, 0f, running.Spatial ? ship.Position : null);
            if (!playerThere) ReportSiegeCrime(u, t);
            e = running;
        }
        else e = Siege.Start(u, ship, t);
        u.Events.Publish(new FleetAttackStarted(ship.Id, default, t.Ref.Kind == SiegeKind.Station ? t.Ref.Station : default, t.Ref.Kind == SiegeKind.Gate ? t.Ref.Gate : default,
            !SiegeIsWar(u, t), $"Schiff #{ship.Id.Value} greift {t.Name} an."));
        return e;
    }

    static bool SiegeIsWar(Universe u, ISiegeTarget t) =>
        t.Ref.Kind == SiegeKind.Gate ? t.Defenders.Any(d => Wars.AtWar(u, u.PlayerActor, d)) : Wars.AtWar(u, u.PlayerActor, t.Owner);

    static void ReportSiegeCrime(Universe u, ISiegeTarget t)
    {
        if (SiegeIsWar(u, t)) return;
        if (t.Ref.Kind == SiegeKind.Gate && t.Defenders.Count > 0) { foreach (var f in t.Defenders) Wanted.Report(u, u.PlayerActor, f, t.Crime); }
        else Wanted.ReportAssault(u, u.PlayerActor, t.Sector, t.Owner, t.Crime);
    }

    /// <summary>
    /// Bewegung zum Zielschiff: zum Reiseziel des Ziels (bzw. zu seinem Liegeplatz) mit Abstand; neu geplant, wenn sich das Ziel geändert hat. Die gemeinsame
    /// Regel für Angreifen, Verfolgen und Tor blockieren.
    /// </summary>
    public static void Close(Universe u, Ship ship, Ship t)
    {
        OrderMove.StepTransit(u, ship);
        if (u.Tick < ship.NextThinkTick) return;
        ship.NextThinkTick = u.Tick + ReplanTicks;
        var (ds, dp) = t.Transit is { } tt ? (tt.ToSector, tt.ToPosition) : (t.Sector, t.Position);
        var spot = CombatSpace.Clear(u, ds, dp + Vec2.FromAngle(ship.Id.Value * 1.7f) * Standoff);
        if (ship.Transit is { } mine && mine.ToSector == ds && Vec2.Distance(mine.ToPosition, spot) <= 5f) return;
        if (ship.Transit is null && ship.Sector == ds && Vec2.Distance(ship.Position, spot) <= 5f) return;
        if (ship.Transit is not null) OrderMove.StopHere(u, ship);
        if (!OrderMove.Go(u, ship, ds, spot)) ship.NextThinkTick = u.Tick + OrderMove.RetryTicks;
        if (ship.IsPlayer && ship.Transit is null) ship.DockedAt = default;
    }

    /// <summary>Stehen Schiff und Ziel im selben Sektor nah genug beieinander, um anzugreifen?</summary>
    public static bool InReach(Universe u, Ship ship, Ship t)
    {
        var (ms, mp) = ThreatMap.Where(u, ship);
        var (ts, tp) = ThreatMap.Where(u, t);
        return ms == ts && Vec2.Distance(mp, tp) <= EngageRange;
    }

    /// <summary>Nach dem Ende eines Angriffs: ein Verbandsmitglied (nicht Anführer) folgt wieder seinem Anführer. true = neuer Befehl gegeben.</summary>
    public static bool BackToLeader(Universe u, Ship ship)
    {
        if (Squadrons.Get(u, ship) is not { } sq || sq.Leader == ship.Id || !u.Ships.ContainsKey(sq.Leader)) return false;
        return Commands.Give(u, ship, new FollowCommand(sq.Leader), out _, ignoreCapacity: true);
    }

    // ───────── „Verband greift an“ (Zielkarte im Flug) ─────────

    /// <summary>
    /// Wer würde auf „Verband greift an“ angreifen: bewaffnete eigene Schiffe (nicht das aktive) im Verband des aktiven Schiffs und frei im Sektor des
    /// aktiven Schiffs; nicht im Gefecht, nicht im Notfall, Verhaltensregel Verteidigen/Angriff. Feste Reihenfolge (Id).
    /// </summary>
    public static List<Ship> Wing(Universe u)
    {
        var list = new List<Ship>();
        if (!u.Ships.TryGetValue(u.PlayerShip, out var me)) return list;
        var sq = Squadrons.Get(u, me);
        foreach (var s in u.ShipsById())
        {
            if (!s.IsPlayer || s.Id == u.PlayerShip || s.IsRental || !s.Has(Capability.Combat) || !CombatSense.Armed(u, s)) continue;
            if (u.Fights.IsEngaged(s.Id) || s.Emergency || RulesIssue(u, s) is not null) continue;
            bool inSquad = sq is not null && s.Squadron == sq.Id;
            if (!inSquad && ThreatMap.Where(u, s).Sector != u.PlayerSector) continue;
            list.Add(s);
        }
        return list;
    }

    /// <summary>Text der Taste „Verband greift an“ für ein Ziel (null = nicht möglich: kein Verband oder kein gültiges Ziel).</summary>
    public static string? WingLabel(Universe u, AttackCommand target)
    {
        var wing = Wing(u);
        if (wing.Count == 0) return null;
        int ok = wing.Count(s => TargetIssue(u, s, target) is null);
        return ok == 0 ? null : $"{ok} Schiff{(ok == 1 ? "" : "e")}";
    }

    /// <summary>
    /// „Verband greift an“: jedes Schiff des Verbands (<see cref="Wing"/>) bekommt den Angriffsbefehl. Verbandsmitglieder zählen über den Verband, freie Schiffe
    /// gegen die Befehlskapazität (wer keinen Platz bekommt, bleibt). Liefert die Zahl der Angreifer und die Meldung.
    /// </summary>
    public static int OrderWing(Universe u, AttackCommand target, out string message)
    {
        int n = 0;
        string? why = null;
        foreach (var s in Wing(u))
        {
            if (Commands.Give(u, s, target, out var m, ignoreCapacity: s.Squadron.Value != 0)) n++;
            else why ??= m;
        }
        string name = target.Ship.Value != 0 && u.Ships.TryGetValue(target.Ship, out var ts) ? PlayerCombat.Describe(u, ts)
            : SiegeTargetOf(u, target)?.Name ?? "das Ziel";
        bool crime = target.Ship.Value != 0 ? u.Ships.TryGetValue(target.Ship, out var t2) && IsCrime(u, t2.Owner)
            : SiegeTargetOf(u, target) is { } st && !SiegeIsWar(u, st);
        message = n == 0 ? "Verband greift nicht an: " + (why ?? "kein bewaffnetes Schiff frei.")
            : $"Verband ({n}) greift {name} an" + (crime ? " — außerhalb eines Kriegs ein Verbrechen." : ".");
        return n;
    }
}
