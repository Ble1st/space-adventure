using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 24 (KAMPF.md 4.2): das Rundengefecht. Läuft jeden Tick nur über die aktiven Gefechte (<see cref="Universe.Fights"/>; ohne Gefecht kostet es nichts):
/// je Teilnehmer entscheidet seine Taktik (Ziel, Bewegung, Leistungsverteilung, Feuer, Aufgabe), dann ändern sich die Abstände nach Tempo und Taktik, jede
/// bereite Waffe in Reichweite feuert über die Trefferregel (<see cref="Hit.Roll"/>; Spitzenlast aus der Speicherbank, sonst im Takt des Überschusses;
/// Munition aus dem Magazin, leeres Magazin = die Waffe schweigt), und wer außer Reichweite aller Gegner flieht, ist entkommen. Zerstörte Schiffe gehen
/// über <see cref="Departures"/> (Grund a). Ein Gefecht endet, wenn eine Seite weg ist (zerstört, geflohen, ergeben), wenn der Kaperer seine Beute hat oder
/// nach <see cref="EconomyRules.CombatMaxTicks"/>. Schild und Bank laden weiter über <see cref="ShipSystemsSystem"/>.
/// </summary>
public sealed class CombatSystem : ISimSystem
{
    readonly CombatTactics _tactics;
    readonly List<TacticDecision> _decisions = new();
    readonly List<float> _moves = new();

    public CombatSystem(CombatTactics? tactics = null) => _tactics = tactics ?? CombatTactics.Default;

    public void Step(Universe u, int dt)
    {
        if (u.Fights.Count == 0) return;
        float seconds = ShipSystems.Seconds(dt);
        foreach (var e in u.Fights.Encounters.ToList()) StepEncounter(u, e, seconds);
        u.Fights.Encounters.RemoveAll(e => e.Participants.TrueForAll(p => !p.Active) && e.Done);
    }

    void StepEncounter(Universe u, Encounter e, float seconds)
    {
        var r = u.Rules;
        // 0) weggefallene Schiffe (anderer Abgang) austragen
        foreach (var p in e.Participants)
            if (p.Active && !u.Ships.ContainsKey(p.Ship)) { p.Exit = CombatExit.Lost; u.Fights.Unmark(p.Ship); }
        if (Ended(e)) { Finish(u, e, Outcome(e)); return; }

        // 1) Entscheidungen (alle auf demselben Stand)
        _decisions.Clear();
        foreach (var p in e.Participants)
            _decisions.Add(p.Active ? _tactics.Get(p.Tactic).Decide(u, e, p, u.Ships[p.Ship]) : default);
        for (int i = 0; i < e.Participants.Count; i++)
        {
            var p = e.Participants[i];
            if (!p.Active) continue;
            var d = _decisions[i];
            p.Target = d.Target;
            p.Move = d.Move;
            var ship = u.Ships[p.Ship];
            if (ship.Systems.Mode != d.Mode) ShipSystems.SetMode(u, ship, d.Mode);
        }

        // 2) Aufgabe: Fracht abwerfen, der Kaperer nimmt sie und bricht ab
        for (int i = 0; i < e.Participants.Count; i++)
        {
            var p = e.Participants[i];
            if (!p.Active || !_decisions[i].Surrender) continue;
            if (Surrender(u, e, p)) { Finish(u, e, "surrendered"); return; }
        }

        // 3) Bewegung (gleichzeitig: Schritte aus dem alten Stand)
        _moves.Clear();
        for (int i = 0; i < e.Participants.Count; i++)
        {
            var p = e.Participants[i];
            _moves.Add(p.Active ? MoveOf(u, e, p, _decisions[i], seconds) : 0f);
        }
        for (int i = 0; i < e.Participants.Count; i++)
        {
            var p = e.Participants[i];
            if (!p.Active) continue;
            p.X += _moves[i];
            p.Speed = seconds > 0f ? MathF.Abs(_moves[i]) / seconds : 0f;
        }

        // 4) Feuer (Teilnehmer und Slots in fester Reihenfolge)
        for (int i = 0; i < e.Participants.Count; i++)
        {
            var p = e.Participants[i];
            if (!p.Active || !u.Ships.TryGetValue(p.Ship, out var ship)) continue;
            Fire(u, e, p, ship, _decisions[i].Fire, seconds);
        }

        // 5) Flucht gelingt, wenn kein Gegner mehr reicht
        foreach (var p in e.Participants)
        {
            if (!p.Active || p.Move != CombatMove.Flee) continue;
            bool clear = true;
            foreach (var q in CombatSense.Enemies(u, e, p))
            {
                float reach = MathF.Max(r.CombatMinEscapeDistance, CombatSense.MaxRange(u, u.Ships[q.Ship]));
                if (CombatSense.Distance(p, q) <= reach) { clear = false; break; }
            }
            if (clear) Engage.Leave(u, e, p, CombatExit.Fled);
        }

        // 6) Ende
        if (Ended(e)) { Finish(u, e, Outcome(e)); return; }
        if (u.Tick - e.StartTick >= r.CombatMaxTicks) Finish(u, e, "timeout");
    }

    static bool Ended(Encounter e)
    {
        bool a = false, d = false;
        foreach (var p in e.Participants) if (p.Active) { if (p.Side == 0) a = true; else d = true; }
        return !a || !d;
    }

    static string Outcome(Encounter e)
    {
        bool destroyed = false, fled = false;
        foreach (var p in e.Participants) { destroyed |= p.Exit == CombatExit.Destroyed; fled |= p.Exit == CombatExit.Fled; }
        return destroyed ? "destroyed" : fled ? "fled" : "ended";
    }

    /// <summary>Schritt auf der Gefechtslinie (Vorzeichen = Richtung) nach Bewegung und Tempo (Stellung „Antrieb“ mit Bonus).</summary>
    static float MoveOf(Universe u, Encounter e, CombatParticipant p, in TacticDecision d, float seconds)
    {
        var ship = u.Ships[p.Ship];
        float speed = Travel.SpeedOf(ship) * ShipSystems.SpeedFactor(u, ship);
        float step = speed * seconds;
        var target = d.Target.Value != 0 ? e.Of(d.Target) : null;
        var reference = target is { Active: true } ? target : CombatSense.Nearest(u, e, p);
        if (reference is null || step <= 0f) return 0f;
        float toward = reference.X > p.X ? 1f : reference.X < p.X ? -1f : (p.Side == 0 ? 1f : -1f);
        float dist = CombatSense.Distance(p, reference);
        switch (d.Move)
        {
            case CombatMove.Flee:
                return -toward * step;
            case CombatMove.Approach:
                return dist > d.Distance ? toward * MathF.Min(step, dist - d.Distance) : 0f;
            default:   // Halten: nur heran, wenn außer eigener Reichweite
                float reach = CombatSense.MaxRange(u, ship);
                return reach > 0f && dist > reach && d.Distance > 0f ? toward * MathF.Min(step, dist - d.Distance) : 0f;
        }
    }

    /// <summary>Waffen des Teilnehmers: Uhren laufen ab; bereite Waffen in Reichweite schießen auf das Ziel.</summary>
    static void Fire(Universe u, Encounter e, CombatParticipant p, Ship ship, bool fire, float seconds)
    {
        var r = u.Rules;
        for (int slot = 0; slot < ship.Fit.Count; slot++)
        {
            if (ship.Fit[slot] is not { } key || !u.Modules.TryGetValue(key, out var m) || !m.IsWeapon) continue;
            while (p.Timers.Count <= slot) p.Timers.Add(0f);
            if (p.Timers[slot] > 0f) p.Timers[slot] = MathF.Max(0f, p.Timers[slot] - seconds);
            if (!fire || p.Timers[slot] > 0f || ship.ConditionOf(slot) < r.ModuleFailCondition) continue;
            if (p.Target.Value == 0 || e.Of(p.Target) is not { Active: true } tp || !u.Ships.TryGetValue(tp.Ship, out var target)) continue;
            var w = WeaponProfile.Of(m);
            float dist = CombatSense.Distance(p, tp);
            if (dist > w.Range) continue;
            if (w.AmmoPerShot > 0f && ShipSystems.Ammo(u, ship) < w.AmmoPerShot) continue;   // leeres Magazin: die Waffe schweigt
            float factor = Hit.ArcFactor(r, slot < ship.Slots.Count ? ship.Slots[slot] : new ClassSlot());
            if (p.Move == CombatMove.Flee && (slot < ship.Slots.Count ? ship.Slots[slot].Arc ?? 360f : 360f) < 180f) factor *= r.CombatFleeFireFactor;
            var tProfile = new TargetProfile(target.ShieldNow, target.Stats.Armor, tp.Speed, Signature(u, target));
            // Munition sparen: Waffen mit Munition schießen erst ab einer Mindest-Trefferchance (Regel, rein — kein Zufallszug)
            if (w.AmmoPerShot > 0f && r.AmmoActive && Hit.Chance(w, tProfile, dist, factor, r) < r.AmmoMinHitChance) continue;
            // Spitzenlast: aus der Bank, sonst im Takt des Überschusses (Waffenanteil)
            float reload = w.Reload;
            if (w.PeakDraw > 0f)
            {
                if (ship.Charge >= w.PeakDraw) ShipSystems.Drain(u, ship, w.PeakDraw);
                else
                {
                    float rate = ShipSystems.Split(ship.Systems.Mode, r.PowerFocusShare).Weapons * ShipSystems.Surplus(ship.Stats);
                    if (rate <= 0f) continue;   // keine Leistung: die Waffe schweigt
                    reload = MathF.Max(reload, w.PeakDraw / rate);
                }
            }
            if (w.AmmoPerShot > 0f && r.AmmoActive) ship.Systems.AmmoMissing = MathF.Min(ship.Stats.AmmoCapacity, ship.Systems.AmmoMissing + w.AmmoPerShot);
            var hit = Hit.Roll(u.Rng, w, tProfile, dist, factor, r);
            p.Timers[slot] = reload;
            e.Shots++;
            float floor = target.IsPlayer ? r.PlayerMinHull * target.MaxHull : 0f;   // Spielerflotte: Hüllenboden bis Phase 25
            float hull = Hit.Apply(u, target, hit, floor);
            if (hit.Hit) e.Hits++;
            u.Events.Publish(new ShotFired(e.Id, e.Sector, ship.Id, target.Id, m.Key, m.DamageType, hit.Hit, hit.ShieldDamage, hull));
            if (target.Hull <= 0f && !target.IsPlayer) Engage.Destroy(u, e, tp, target);
        }
    }

    static float Signature(Universe u, Ship s) =>
        u.ShipModels.TryGetValue(s.ModelKey, out var mdl) && u.ShipClasses.TryGetValue(mdl.ClassKey, out var cls) ? Hit.Signature(u.Rules, cls.Size) : 1f;

    /// <summary>
    /// Aufgabe: Der Teilnehmer wirft seinen Abwurfanteil jeder Ware ab; der Kaperer der Gegenseite nimmt die Beute (Hehlerei in die Piratenkasse wie bisher,
    /// gedeckelt) und bricht ab. false = kein Kaperer da oder nichts abzuwerfen.
    /// </summary>
    static bool Surrender(Universe u, Encounter e, CombatParticipant p)
    {
        var r = u.Rules;
        CombatParticipant? taker = null;
        foreach (var q in CombatSense.Enemies(u, e, p)) if (q.Tactic == CombatTactics.Raider) { taker = q; break; }
        if (taker is null || !u.Ships.TryGetValue(p.Ship, out var ship) || !u.Ships.TryGetValue(taker.Ship, out var raider)) return false;
        float value = 0f;
        foreach (var g in ship.Cargo.Keys.OrderBy(k => k.Value).ToList())
        {
            float take = ship.Cargo[g] * p.DropShare;
            if (take <= 0f) continue;
            float left = ship.Cargo[g] - take;
            if (left <= 0.001f) ship.Cargo.Remove(g); else ship.Cargo[g] = left;
            value += take * u.Goods[g].BasePrice;
        }
        if (value <= 0f) return false;
        float fenced = value * r.FenceShare;
        if (r.PirateTreasuryCap > 0f && u.Actors.TryGetValue(raider.Owner, out var capped))
            fenced = MathF.Min(fenced, MathF.Max(0f, r.PirateTreasuryCap - capped.Treasury.Credits));
        if (fenced > 0f && u.Actors.TryGetValue(raider.Owner, out var pirates))
        {
            pirates.Treasury.Credits += fenced;
            u.Events.Publish(new TreasuryIncome(pirates.Id, "fence", fenced));
        }
        e.Loot += value;
        e.Fenced += fenced;
        u.Events.Publish(new CargoSurrendered(e.Id, ship.Id, raider.Id, value));
        Engage.Leave(u, e, p, CombatExit.Surrendered);
        Engage.Leave(u, e, taker, CombatExit.Looted);
        return true;
    }

    /// <summary>
    /// Ende des Gefechts: alle übrigen Teilnehmer verlassen es (Höchstdauer bzw. Ende), Lagekarte, Kopfgeld und die Ereignisse der Anlässe
    /// (<see cref="RaidResolved"/>, <see cref="RaiderIntercepted"/>) wie bisher; danach arbeiten alle Überlebenden weiter (<see cref="Engage.Resume"/>).
    /// </summary>
    public static void Finish(Universe u, Encounter e, string outcome)
    {
        if (e.Done) return;
        e.Done = true;
        var r = u.Rules;
        foreach (var p in e.Participants) if (p.Active) Engage.Leave(u, e, p, outcome == "timeout" ? CombatExit.Timeout : CombatExit.Ended);
        var raiderP = e.Raider.Value != 0 ? e.Of(e.Raider) : null;
        var victimP = e.Victim.Value != 0 ? e.Of(e.Victim) : null;
        bool raiderDestroyed = raiderP?.Exit == CombatExit.Destroyed;
        bool victimDestroyed = victimP?.Exit == CombatExit.Destroyed;
        var sector = u.Sectors.GetValueOrDefault(e.Sector);
        int destroyed = 0, fled = 0, surrendered = 0;
        foreach (var p in e.Participants)
        {
            if (p.Exit == CombatExit.Destroyed) destroyed++;
            else if (p.Exit == CombatExit.Fled) fled++;
            else if (p.Exit == CombatExit.Surrendered) surrendered++;
        }
        string msg;
        if (e.Cause == EncounterCause.Raid)
        {
            bool won = e.Loot > 0f || victimDestroyed;
            if (sector is not null) ThreatMap.Report(u, sector.Id, won ? r.RaidThreat : r.RepelledThreat);
            msg = won ? $"Überfall auf Schiff {e.Victim.Value}: Beute {e.Loot:0} Cr" + (victimDestroyed ? " (Schiff zerstört)" : "")
                : $"Überfall auf Schiff {e.Victim.Value} abgewehrt" + (raiderDestroyed ? " — Kaperschiff zerstört" : "");
            if (raiderDestroyed && sector is not null) ClaimBounty(u, e, sector.Owner, side: 1);
            u.Events.Publish(new RaidResolved(e.Sector, e.Raider, e.Victim, won, e.Loot, e.Fenced, victimDestroyed, raiderDestroyed, e.Police, false, msg));
        }
        else
        {
            bool policeWon = raiderP is { Exit: CombatExit.Destroyed or CombatExit.Fled };
            if (policeWon && sector is not null) sector.Threat = MathF.Max(0f, sector.Threat - r.RepelledThreat);
            if (raiderDestroyed && sector is not null) ClaimBounty(u, e, sector.Owner, side: 0);
            msg = raiderDestroyed ? "Kaperschiff gestellt und zerstört" : policeWon ? "Kaperschiff vertrieben" : "Kaperschiff entkommen";
            u.Events.Publish(new RaiderIntercepted(e.Sector, e.Police, e.Raider, policeWon, raiderDestroyed));
        }
        u.Events.Publish(new EncounterEnded(e.Id, e.Sector, e.Cause, outcome, u.Tick - e.StartTick, e.Shots, e.Hits, e.Loot, destroyed, fled, surrendered, msg));
        foreach (var p in e.Participants)
            if (p.Exit != CombatExit.Destroyed && p.Exit != CombatExit.Lost && u.Ships.TryGetValue(p.Ship, out var ship)) Engage.Resume(u, ship);
    }

    /// <summary>Kopfgeld für die Spielerflotte, wenn ihr Schiff auf der Seite <paramref name="side"/> am Gefecht teilnahm und das Kaperschiff zerstört ist.</summary>
    static void ClaimBounty(Universe u, Encounter e, FactionId faction, int side)
    {
        foreach (var p in e.Participants)
        {
            if (p.Side != side || !u.Ships.TryGetValue(p.Ship, out var s) || !s.IsPlayer) continue;
            float bounty = Wanted.BountyOn(u, e.RaiderOwner, faction);
            if (bounty <= 0f || !u.Actors.TryGetValue(s.Owner, out var hunter)) return;
            hunter.BountyClaims[faction] = hunter.BountyClaims.GetValueOrDefault(faction) + bounty;
            u.Events.Publish(new BountyEarned(hunter.Id, faction, e.Raider, bounty));
            return;
        }
    }
}
