using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 27 (KAMPF.md Stufe 27 „Verbände kämpfen gemeinsam, Begleitschutz“): Verstärkung laufender Gefechte. Verallgemeinert <see cref="FleetGuards"/> von Phase 12:
/// Wer einem Teilnehmer beisteht, tritt auf dessen Seite bei, sobald er im Sektor ist — Eskorten und Wachen (Spielerflotte wie Polizei-Geleit), Mitglieder seines
/// Verbands (Streife einer Fraktion, Rudel einer Piratengruppe, Verband der Spielerflotte; nur mit Regel „Verteidigen“/„Angriff“). Geprüft beim Beginn und dann
/// alle <see cref="EconomyRules.ReinforceTicks"/> Ticks, in fester Reihenfolge (Teilnehmer, dann Helfer nach Id); jeder Beitritt zieht wie immer genau zwei
/// Zufallszahlen (<see cref="Engage.Join"/>). Ohne <see cref="EconomyRules.FleetCombat"/> passiert nichts.
/// </summary>
public static class Reinforcements
{
    /// <summary>Abstand (Gefechtslinie bzw. Raum), in dem ein Helfer neben dem Teilnehmer beitritt, dem er beisteht.</summary>
    public const float HelperGap = 8f;

    /// <summary>Beginn und Takt: alle Helfer der aktiven Teilnehmer, die im Sektor und frei sind, treten bei.</summary>
    public static void Step(Universe u, Encounter e)
    {
        if (!u.Rules.FleetCombat || e.Done) return;
        foreach (var p in e.Participants.Where(p => p.Active).ToList())
        {
            if (!p.Active || !u.Ships.TryGetValue(p.Ship, out var ship)) continue;
            foreach (var (helper, reason) in Helpers(u, e, ship))
                if (helper.Id != u.PlayerShip && !u.Fights.IsEngaged(helper.Id) && u.Ships.ContainsKey(helper.Id)) JoinSide(u, e, helper, p.Side, reason, p);
        }
    }

    /// <summary>Wer steht diesem Schiff im Gefechtssektor bei (feste Reihenfolge, ohne Doppelte): Eskorten/Wachen, dann Verbandsmitglieder.</summary>
    public static List<(Ship Ship, string Reason)> Helpers(Universe u, Encounter e, Ship ship) => Helpers(u, e.Sector, ship);

    /// <summary>Wer würde diesem Schiff in <paramref name="sector"/> beistehen (dieselbe Regel; Runde 5 auch für die Lageanzeige vor einem Stationsangriff).</summary>
    public static List<(Ship Ship, string Reason)> Helpers(Universe u, SectorId sector, Ship ship)
    {
        var list = new List<(Ship, string)>();
        foreach (var s in FleetGuards.Escorts(u, ship, sector))
            list.Add((s, s.Command is GuardCommand ? "guard" : "escort"));
        if (Squadrons.Get(u, ship) is { } sq)
            foreach (var id in sq.Members)
            {
                if (id == ship.Id || !u.Ships.TryGetValue(id, out var m) || list.Any(x => x.Item1.Id == id)) continue;
                if (!m.Has(Capability.Combat) || m.Emergency || Squadrons.RulesOf(u, m) is not (Engagement.Defend or Engagement.Attack)) continue;
                // Runde 5: ein angeschlagenes Begleitschiff des Spielers, das sich zurückgezogen hat, tritt nicht sofort wieder bei (vorher Beitritt/Rückzug im Takt)
                if (m.IsPlayer && m.Hull < u.Rules.EscortRetreatHull * m.MaxHull) continue;
                // Runde 7: auch Rudel und Streifen — wer sich unter seiner Rückzugsschwelle zurückgezogen hat, tritt nicht im Zehn-Takt-Rhythmus wieder bei
                // (gemessen bis Runde 6: 56 von 86 Rudel-Beitritten in 20000 Ticks waren Wiedereintritte desselben Kaperers in dasselbe Gefecht)
                if (u.Rules.CombatRejoinReuse && m.Hull < RetreatHull(u, m) * m.MaxHull) continue;
                if (ThreatMap.Where(u, m).Sector != sector) continue;
                list.Add((m, ThreatMap.IsRaider(m) ? "pack" : "squadron"));
            }
        return list;
    }

    /// <summary>Runde 7: Rückzugsschwelle (Anteil der Hülle) der Taktik, mit der das Schiff beiträte.</summary>
    public static float RetreatHull(Universe u, Ship s) =>
        ThreatMap.IsRaider(s) ? u.Rules.RaiderFleeHull : ThreatMap.IsPolice(s) ? u.Rules.PoliceRetreatHull : Wars.IsWarship(s) ? u.Rules.WarshipRetreatHull : u.Rules.EscortRetreatHull;

    /// <summary>
    /// Tritt einem laufenden Gefecht auf Seite <paramref name="side"/> bei — neben <paramref name="near"/> (Gefechtslinie: etwas hinter ihm, Raum: aus der Richtung
    /// der echten Position). Taktik nach Rolle (Polizei, Kaperer, sonst Begleiter). Ereignis <see cref="CombatJoined"/>.
    /// </summary>
    public static CombatParticipant JoinSide(Universe u, Encounter e, Ship ship, int side, string reason, CombatParticipant? near)
    {
        // Gerätetest Phase 28 (Runde 5): Helfer auf der Angreiferseite einer Belagerung (Verband des Spielers) greifen mit der Belagerungstaktik an — erst Schiffe
        // in Reichweite, sonst die Station (die Begleittaktik kannte nur Schiffe und hielt ohne Gegnerschiff still)
        string tactic = ThreatMap.IsPolice(ship) ? CombatTactics.Police : ThreatMap.IsRaider(ship) ? CombatTactics.Raider
            : e.Cause == EncounterCause.Siege && side == 0 ? CombatTactics.Siege : CombatTactics.Escort;
        float x = near is null ? (side == 0 ? 0f : u.Rules.CombatStartDistance) : near.X + (near.Side == 0 ? -HelperGap : HelperGap);
        Vec2? pos = e.Spatial ? CombatSpace.Clear(u, e.Sector, CombatSpace.Around(near?.Pos ?? ship.Position, ship.Position, HelperGap)) : null;
        // Runde 6: eigene Schiffe des Spielers treten mit ihrem alten Eintrag wieder bei (kein zweiter Eintrag, den Encounter.Of nicht fände); NPCs wie bisher
        var p = ship.IsPlayer && e.Of(ship.Id) is { Active: false } old && old.Side == side ? Engage.Rejoin(u, e, old, ship) : Engage.Join(u, e, ship, side, tactic, x, pos);
        if (pos is { } rp && p.InSpace) { p.Pos = p.PrevPos = rp; ship.Position = rp; }
        if (ThreatMap.IsPolice(ship) && e.Police.Value == 0 && e.Raider.Value != 0 && e.Of(e.Raider)?.Side != side) e.Police = ship.Id;
        u.Events.Publish(new CombatJoined(e.Id, ship.Id, side, reason));
        return p;
    }
}

/// <summary>
/// Phase 27 (KAMPF.md 4.2 „Flucht gelingt, wenn das Schiff … eine Station erreicht oder durch ein Tor springt“; Lücke aus Phase 25): Zuflucht eines Fliehenden.
/// Beim Eintritt merkt sich jeder NPC-Teilnehmer die nächste erreichbare Zuflucht — eine Station, deren Besitzer ihm nicht feindlich ist (Kaperer: nur das eigene
/// Versteck), oder ein offenes Tor, an dem nicht schon ein Gegner näher steht als das Schiff (versperrt). Auf der Gefechtslinie verkürzt jeder Fluchtschritt den Abstand, im räumlichen Gefecht fliegt der Autopilot dorthin. Erreicht:
/// an der Station angedockt bzw. durch das Tor in den Nachbarsektor gesprungen — das Schiff ist entkommen. Keine Zufallszahl. Ohne
/// <see cref="EconomyRules.FleetCombat"/>/<see cref="EconomyRules.CombatRefuge"/> und für das aktive Spielerschiff (es flieht selbst) keine Zuflucht.
/// </summary>
public static class CombatRefuge
{
    /// <summary>Merkt die nächste Zuflucht des Teilnehmers (von seiner echten Position aus).</summary>
    public static void Assign(Universe u, Encounter e, CombatParticipant p, Ship ship)
    {
        if (!u.Rules.FleetCombat || !u.Rules.CombatRefuge || ship.Id == u.PlayerShip || !u.Sectors.TryGetValue(e.Sector, out var sec)) return;
        var from = e.Spatial ? p.Pos : ship.Position;
        bool raider = ThreatMap.IsRaider(ship);
        // eine Zuflucht ist versperrt, wenn ein Gegner näher an ihr steht als das Schiff selbst (Kaperer lauern an den Toren)
        var enemies = new List<Vec2>();
        foreach (var q in e.Participants)
            if (q.Active && q.Side != p.Side && u.Ships.TryGetValue(q.Ship, out var qs)) enemies.Add(e.Spatial ? q.Pos : qs.Position);
        bool Blocked(Vec2 at, float d) { foreach (var en in enemies) if (Vec2.Distance(en, at) < d) return true; return false; }
        float best = MathF.Max(0f, u.Rules.CombatRefugeMaxDistance); int id = 0; Vec2 at = default;
        if (best <= 0f) best = float.MaxValue;
        foreach (var sid in sec.Stations.OrderBy(i => i.Value))
        {
            if (!u.Stations.TryGetValue(sid, out var st) || st.IsBuildSite) continue;
            bool ok = raider ? st.Owner == ship.Owner && StationProtection.IsHideout(u, st)
                : !StationProtection.IsHideout(u, st) && Relations.Effective(u, ship.Owner, st.Owner) > u.Rules.HostileRelation;
            if (!ok) continue;
            float d = Vec2.Distance(from, st.Position);
            if (d < best && !Blocked(st.Position, d)) { best = d; id = st.Id.Value; at = st.DockPoint(ship.Id); }
        }
        foreach (var gid in sec.Gates.OrderBy(g => g.Value))
        {
            if (!u.Gates.TryGetValue(gid, out var g) || !g.Open || !u.Gates.TryGetValue(g.Target, out var far) || !far.Open) continue;
            float d = Vec2.Distance(from, g.Position);
            if (d < best && !Blocked(g.Position, d)) { best = d; id = -g.Id.Value; at = g.Position; }
        }
        if (id == 0) return;
        p.RefugeId = id;
        p.Refuge = MathF.Max(1f, best);
        p.RefugePos = at;
    }

    /// <summary>Hat der fliehende Teilnehmer seine Zuflucht erreicht (Linie: Abstand 0, Raum: im Radius)?</summary>
    public static bool Reached(Universe u, CombatParticipant p) =>
        p.RefugeId != 0 && p.Move == CombatMove.Flee && (p.InSpace ? Vec2.Distance(p.Pos, p.RefugePos) <= u.Rules.CombatRefugeRadius : p.Refuge <= 0f);

    /// <summary>
    /// Entkommen über die Zuflucht: an der Station andocken (der Liegeplatz gilt nach dem Austritt) bzw. durch das Tor in den Nachbarsektor springen; dann verlässt
    /// das Schiff das Gefecht (geflohen). false = Zuflucht nicht mehr da (Station weg, Tor zu) — die Flucht geht über die Reichweite weiter.
    /// </summary>
    public static bool Escape(Universe u, Encounter e, CombatParticipant p, Ship ship)
    {
        string kind;
        if (p.RefugeId > 0 && u.Stations.TryGetValue(new StationId(p.RefugeId), out var st) && st.Sector == ship.Sector)
        {
            ship.Position = st.DockPoint(ship.Id);
            if (ship.Id != u.PlayerShip) p.WasDockedAt = st.Id;   // Engage.Leave dockt an, weil das Schiff am Andockpunkt liegt
            kind = "station";
        }
        else if (p.RefugeId < 0 && u.Gates.TryGetValue(new GateId(-p.RefugeId), out var g) && g.Open && g.Sector == ship.Sector
                 && u.Gates.TryGetValue(g.Target, out var far) && far.Open && ship.Transit is null)
        {
            u.MoveShip(ship, far.Sector);
            var inward = far.Position.Length > 1e-3f ? far.Position.Normalized() * -1f : new Vec2(1f, 0f);
            ship.Position = far.Position + inward * 15f;
            p.WasDockedAt = default;
            kind = "gate";
        }
        else { p.RefugeId = 0; return false; }
        Engage.Leave(u, e, p, CombatExit.Fled);
        u.Events.Publish(new RefugeReached(e.Id, ship.Id, kind, ship.Sector));
        return true;
    }
}
