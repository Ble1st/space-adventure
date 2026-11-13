using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 31 (docs/TORE.md, Variante „mittel“): Tore im Kampf. Ein Tor-Paar hat Hülle, Schild und Panzerung seiner Torklasse (<see cref="GateType"/>); beide
/// Hälften teilen den Vorrat (<see cref="Gate.Condition"/> am Tor mit der kleineren Id). Bei Hülle 0 geht das Paar außer Betrieb — es wird
/// <see cref="GateState.Closed"/> (Routen, Kartenteile und die Umplanung laufender Reisen greifen über <see cref="GateAccess.SetState"/>), zerstört wird es nie.
/// Wieder in Betrieb: durch Reparatur ab <see cref="EconomyRules.GateReenableShare"/> oder ohne Kasse nach der Frist (<see cref="GateRepairSystem"/>).
/// Nur mit <see cref="EconomyRules.GateCombat"/>.
/// </summary>
public static class GateCombat
{
    /// <summary>Körperradius eines Tors für Waffenreichweiten (Angreifer messen bis zur Außenhaut).</summary>
    public const float BodyRadius = 10f;

    /// <summary>Die Hälfte mit der kleineren Id (trägt den gemeinsamen Zustand).</summary>
    public static Gate Canonical(Universe u, Gate g) => u.Gates.TryGetValue(g.Target, out var o) && o.Id.Value < g.Id.Value ? o : g;

    /// <summary>Torklasse (null = Tor ohne Kampfwerte oder Torkampf aus).</summary>
    public static GateType? TypeOf(Universe u, Gate g) => u.Rules.GateCombat && u.GateTypes.TryGetValue(g.TypeKey, out var t) ? t : null;

    public static GateCondition? ConditionOf(Universe u, Gate g) => Canonical(u, g).Condition;
    static GateCondition Cond(Universe u, Gate g) { var c = Canonical(u, g); return c.Condition ??= new GateCondition(); }

    public static float MaxHull(Universe u, Gate g) => TypeOf(u, g)?.Defense.Hull ?? 0f;
    public static float MaxShield(Universe u, Gate g) => TypeOf(u, g)?.Defense.Shield ?? 0f;
    public static float Hull(Universe u, Gate g) => MathF.Max(0f, MaxHull(u, g) - (ConditionOf(u, g)?.HullMissing ?? 0f));
    public static float Shield(Universe u, Gate g) => MathF.Max(0f, MaxShield(u, g) - (ConditionOf(u, g)?.ShieldMissing ?? 0f));

    /// <summary>Runde 14b: in der Schonfrist nach dem Wiederanlauf (unverwundbar, nicht angreifbar)?</summary>
    public static bool InGrace(Universe u, Gate g) => ConditionOf(u, g) is { } c && c.GraceUntil > u.Tick;

    /// <summary>Ticks bis zum Ende der Schonfrist (0 = keine).</summary>
    public static long GraceLeft(Universe u, Gate g) => ConditionOf(u, g) is { } c ? Math.Max(0L, c.GraceUntil - u.Tick) : 0L;

    /// <summary>Außer Betrieb (durch Kampf — eine Schließung von Hand ist es nicht).</summary>
    public static bool IsDown(Universe u, Gate g) => ConditionOf(u, g) is { Disabled: true };

    public static TargetProfile Profile(Universe u, Gate g) => new(Shield(u, g), TypeOf(u, g)?.Defense.Armor ?? 0f, 0f, StationCombat.Signature);

    /// <summary>Größte Reichweite der Torgeschütze ab Mitte (0 = keine; Datenfeld, in der Standardwelt leer).</summary>
    public static float GunRange(Universe u, Gate g)
    {
        if (IsDown(u, g) || TypeOf(u, g) is not { } t) return 0f;
        float r = 0f;
        foreach (var k in t.Defense.Guns) if (u.Modules.TryGetValue(k, out var m)) r = MathF.Max(r, m.Stat("range"));
        return r;
    }

    /// <summary>„Tor Alpha–Beta“ (Kurznamen der beiden Sektoren, die Hälfte zuerst).</summary>
    public static string NameOf(Universe u, Gate g)
    {
        string Short(SectorId s) => u.Sectors.TryGetValue(s, out var sec) ? sec.Name.Split(' ').Last() : "?";
        var far = u.Gates.GetValueOrDefault(g.Target);
        return $"Tor {Short(g.Sector)}–{(far is null ? "?" : Short(far.Sector))}";
    }

    /// <summary>Verteidiger: die Fraktionen, denen die beiden Endsektoren gehören (herrenlos zählt nicht), nach Id.</summary>
    public static IReadOnlyList<FactionId> DefendersOf(Universe u, Gate g)
    {
        var list = new List<FactionId>();
        foreach (var half in new[] { g, u.Gates.GetValueOrDefault(g.Target) })
            if (half is not null && u.Sectors.TryGetValue(half.Sector, out var s) && s.Owner.Value != 0 && u.Factions.ContainsKey(s.Owner) && !list.Contains(s.Owner)) list.Add(s.Owner);
        list.Sort((a, b) => a.Value.CompareTo(b.Value));
        return list;
    }

    /// <summary>Besitzer für die Verlustbuchung: Besitzer des Sektors dieser Hälfte, sonst der Gegenseite (default = herrenlos).</summary>
    public static ActorId OwnerOf(Universe u, Gate g)
    {
        if (u.Sectors.TryGetValue(g.Sector, out var s) && s.Owner.Value != 0) return s.Owner;
        return u.Gates.TryGetValue(g.Target, out var far) && u.Sectors.TryGetValue(far.Sector, out var fs) ? fs.Owner : default;
    }

    /// <summary>Ist das Paar kartenteilend (seine Endsektoren liegen ohne es in verschiedenen Teilen)?</summary>
    public static bool Splits(Universe u, Gate g)
    {
        if (!u.Gates.TryGetValue(g.Target, out var far)) return false;
        if (IsDown(u, g) || !MapParts.Passable(g)) return !MapParts.Of(u).Same(g.Sector, far.Sector);
        return MapParts.SplitsWhenClosed(u, g.Id);
    }

    /// <summary>Frist bis zum Wiederanlauf ohne Kasse (Torklasse, sonst kartenteilend <see cref="EconomyRules.GateSplitOutageTicks"/>, sonst <see cref="EconomyRules.GateMaxOutageTicks"/>).</summary>
    public static long OutageLimit(Universe u, Gate g)
    {
        if (u.GateTypes.TryGetValue(g.TypeKey, out var t) && t.MaxOutageTicks > 0) return Splits(u, g) ? Math.Min(t.MaxOutageTicks, u.Rules.GateSplitOutageTicks) : t.MaxOutageTicks;
        return Splits(u, g) ? u.Rules.GateSplitOutageTicks : u.Rules.GateMaxOutageTicks;
    }

    /// <summary>Ticks bis zum spätesten Wiederanlauf eines ausgefallenen Tors (0 = nicht ausgefallen).</summary>
    public static long RestartIn(Universe u, Gate g) =>
        ConditionOf(u, g) is { Disabled: true } c ? Math.Max(0L, c.DownSince + OutageLimit(u, g) - u.Tick) : 0L;

    /// <summary>Zahl der Tor-Paare außer Betrieb.</summary>
    public static int DownCount(Universe u)
    {
        int n = 0;
        foreach (var g in u.Gates.Values) if (g.Condition is { Disabled: true }) n++;
        return n;
    }

    /// <summary>Wird eine Hälfte des Paars gerade belagert?</summary>
    public static bool UnderSiege(Universe u, Gate g)
    {
        foreach (var e in u.Fights.Encounters) if (!e.Done && (e.Gate == g.Id || e.Gate == g.Target)) return true;
        return false;
    }

    /// <summary>Treffer anwenden: Schild (mit Pause), Hülle; bei 0 außer Betrieb. Liefert den Hüllenschaden.</summary>
    public static float Apply(Universe u, Gate g, in HitResult hit, ActorId attacker)
    {
        if (!hit.Hit || IsDown(u, g) || TypeOf(u, g) is null || InGrace(u, g)) return 0f;   // Runde 14b: Schonfrist
        var c = Cond(u, g);
        c.LastAttacker = attacker;
        if (hit.ShieldDamage > 0f) c.ShieldMissing = MathF.Min(MaxShield(u, g), c.ShieldMissing + hit.ShieldDamage);
        if (MaxShield(u, g) > 0f) c.ShieldPauseUntil = u.Tick + u.Rules.ShieldDelayTicks;
        float before = Hull(u, g);
        c.HullMissing = MathF.Min(MaxHull(u, g), c.HullMissing + hit.HullDamage);
        float dealt = before - Hull(u, g);
        if (Hull(u, g) <= 0f) Disable(u, g, attacker);
        return dealt;
    }

    /// <summary>Das Paar geht außer Betrieb: geschlossen für alle (Umplanung laufender Reisen), Verlust im Krieg, Ereignis.</summary>
    public static void Disable(Universe u, Gate g, ActorId attacker)
    {
        var canon = Canonical(u, g);
        var c = Cond(u, g);
        if (c.Disabled) return;
        c.Disabled = true;
        c.DownSince = u.Tick;
        c.PrevState = canon.State is GateState.Open or GateState.Restricted ? null : canon.State;   // Runde 14b: von Hand geschlossen bleibt geschlossen
        c.HullMissing = MaxHull(u, g);
        Wars.OnLoss(u, OwnerOf(u, g), attacker, MaxHull(u, g) * 2f);
        bool split = !u.Gates.TryGetValue(g.Target, out var far) ? false : MapParts.SplitsWhenClosed(u, g.Id);
        GateAccess.SetState(u, canon, GateState.Closed, restoring: true);
        long limit = OutageLimit(u, g);
        string msg = $"{NameOf(u, g)} ist außer Betrieb — läuft spätestens in {Clock(limit)} wieder an.";
        u.Events.Publish(new GateDisabled(canon.Id, g.Sector, attacker, split, limit, msg));
    }

    /// <summary>Wieder in Betrieb (Reparatur oder Notregel): Paar öffnen, Sperrstufen neu ableiten, Ereignis.</summary>
    public static void Restore(Universe u, Gate g, string reason)
    {
        var canon = Canonical(u, g);
        if (canon.Condition is not { Disabled: true } c) return;
        // Runde 14b (Prüfung: ein Sackgassentor ließ sich mit Dauerfeuer sofort wieder abschießen): Schonfrist ab dem Wiederanlauf — kartenteilend länger
        bool split = Splits(u, g);
        c.Disabled = false;
        float floor = MaxHull(u, g) * Math.Clamp(u.Rules.GateReenableShare, 0f, 1f);
        if (Hull(u, g) < floor) c.HullMissing = MaxHull(u, g) - floor;
        c.ShieldMissing = 0f;
        c.ShieldPauseUntil = 0;
        c.GraceUntil = u.Tick + Math.Max(0L, split ? u.Rules.GateSplitRestoreGraceTicks : u.Rules.GateRestoreGraceTicks);
        // laufende Belagerungen des Paars enden sauber (alle Teilnehmer verlassen das Gefecht; der Gefechtsschritt schließt es ab)
        foreach (var e in u.Fights.Encounters.Where(x => !x.Done && (x.Gate == g.Id || x.Gate == g.Target)).ToList())
            foreach (var p in e.Participants.Where(x => x.Active).ToList())
            {
                Engage.Leave(u, e, p, CombatExit.Fled);
                if (u.Ships.TryGetValue(p.Ship, out var ps) && !ps.IsPlayer) Engage.Resume(u, ps);
            }
        var prev = c.PrevState;
        c.PrevState = null;
        if (prev is null)
        {
            GateAccess.SetState(u, canon, GateState.Open, restoring: true);
            GateAccess.SyncRestricted(u);
        }
        u.TouchAccess();
        string msg = reason == "emergency" ? $"{NameOf(u, g)} läuft wieder (Notbetrieb der Torbehörde)." : $"{NameOf(u, g)} ist repariert und wieder in Betrieb.";
        u.Events.Publish(new GateRestored(canon.Id, reason, msg));
    }

    /// <summary>Tor-Geschütze (Datenfeld der Torklasse; in der Standardwelt leer) — wie Stationsgeschütze, auf Angreifer und Gegner der Verteidiger in Reichweite.</summary>
    public static void Fire(Universe u, Encounter e, float seconds)
    {
        var r = u.Rules;
        if (!r.StationGunsActive || !r.GateCombat || !u.Sectors.TryGetValue(e.Sector, out var sector)) return;
        foreach (var gid in sector.Gates.OrderBy(i => i.Value).ToList())
        {
            if (!u.Gates.TryGetValue(gid, out var g) || TypeOf(u, g) is not { Defense.Guns.Count: > 0 } t || IsDown(u, g)) continue;
            var defenders = DefendersOf(u, g);
            for (int k = 0; k < t.Defense.Guns.Count; k++)
            {
                if (!u.Modules.TryGetValue(t.Defense.Guns[k], out var m)) continue;
                var c = ConditionOf(u, g);
                float timer = c is not null && k < c.Timers.Count ? c.Timers[k] : 0f;
                if (timer > 0f) { Cond(u, g).Timers[k] = MathF.Max(0f, timer - seconds); continue; }
                var w = WeaponProfile.Of(m);
                CombatParticipant? target = null;
                float best = float.MaxValue;
                var self = new GateTarget(u, g);
                foreach (var p in e.Participants)
                {
                    if (!p.Active || !u.Ships.TryGetValue(p.Ship, out var ship) || !Hostile(u, g, defenders, e, p, ship)) continue;
                    float d = SiegeTargets.Distance(u, e, p, self);
                    if (d <= w.Range && d < best) { best = d; target = p; }
                }
                if (target is null) continue;
                var cond = Cond(u, g);
                while (cond.Timers.Count <= k) cond.Timers.Add(0f);
                cond.Timers[k] = w.Reload;
                var tShip = u.Ships[target.Ship];
                if (Missiles.IsMissile(m)) { Missiles.Launch(u, e, null, null, target.Side == 0 ? 1 : 0, m, w, target.Ship, default, best, e.Spatial ? g.Position : default, k); continue; }
                var hit = Hit.Roll(u.Rng, w, new TargetProfile(tShip.ShieldNow, tShip.Stats.Armor, target.Speed, Gunnery.Signature(u, tShip)), best, 1f, r);
                e.Shots++;
                float hull = Hit.Apply(u, tShip, hit);
                if (hull > 0f) ModuleDamage.OnHullHit(u, tShip, hull);
                if (hit.Hit) e.Hits++;
                u.Events.Publish(new GateShot(e.Id, e.Sector, g.Id, tShip.Id, m.Key, m.DamageType, hit.Hit, hit.ShieldDamage, hull, k));
                if (tShip.Hull <= 0f && target.Active) Engage.Destroy(u, e, target, tShip, $"Torgeschütze ({NameOf(u, g)})");
            }
        }
    }

    /// <summary>Feindlich aus Sicht eines Tors: Angreifer des Paars, Kriegsgegner/Feinde der Verteidiger, ein dort gesuchter Spieler.</summary>
    public static bool Hostile(Universe u, Gate g, IReadOnlyList<FactionId> defenders, Encounter e, CombatParticipant p, Ship ship)
    {
        if (defenders.Count == 0) return false;
        foreach (var f in defenders)
        {
            if (u.FactionOf(ship) == f) continue;
            if ((e.Gate == g.Id || e.Gate == g.Target) && p.Side == 0) return true;
            if (Wars.AtWar(u, (ActorId)f, ship.Owner) || Relations.Effective(u, f, ship.Owner) <= u.Rules.HostileRelation) return true;
            if (ship.IsPlayer && Wanted.LevelOf(u, ship.Owner, f) >= u.Rules.TradeRefusalWantedLevel) return true;
        }
        return false;
    }

    /// <summary>Zeit m:ss aus Ticks.</summary>
    public static string Clock(long ticks)
    {
        long s = Math.Max(0L, ticks) / SimClock.TicksPerSecond;
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>
    /// Für Routenplanung/Autopilot: Liegt zwischen <paramref name="from"/> und <paramref name="to"/> nur ein ausgefallenes Tor? Dann „Tor … außer Betrieb – läuft
    /// in m:ss wieder an“ (das früheste unter den nötigen); null = nicht deswegen gesperrt.
    /// </summary>
    public static string? OutageReason(Universe u, SectorId from, SectorId to)
    {
        if (from == to) return null;
        // Breitensuche über offene UND ausgefallene Tore; merkt die ausgefallenen auf dem gefundenen Weg
        var prev = new Dictionary<SectorId, (SectorId From, Gate? Down)>();
        var queue = new Queue<SectorId>();
        queue.Enqueue(from); prev[from] = (from, null);
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            if (s == to) break;
            foreach (var gid in u.Sectors[s].Gates.OrderBy(i => i.Value))
            {
                if (!u.Gates.TryGetValue(gid, out var g) || !u.Gates.TryGetValue(g.Target, out var far)) continue;
                bool down = IsDown(u, g);
                if (!down && (!MapParts.Passable(g) || !MapParts.Passable(far))) continue;
                if (prev.ContainsKey(far.Sector)) continue;
                prev[far.Sector] = (s, down ? g : null);
                queue.Enqueue(far.Sector);
            }
        }
        if (!prev.ContainsKey(to)) return null;
        Gate? worst = null; long wait = 0;
        for (var at = to; at != from; at = prev[at].From)
            if (prev[at].Down is { } d && RestartIn(u, d) >= wait) { worst = d; wait = RestartIn(u, d); }
        return worst is null ? null : $"{NameOf(u, worst)} außer Betrieb – läuft in {Clock(wait)} wieder an";
    }
}

/// <summary>
/// Phase 31: Torreparatur und Notregel (Kampfmodul, Intervall 50; nur Paare mit Zustand). Schild lädt nach der Pause; außerhalb einer Belagerung repariert ein
/// Besitzer der Endsektoren, der nicht selbst angreift (im Krieg nur die Seite, die nicht der Angreifer ist), bis zum Tagestempo der Torklasse
/// (<see cref="EconomyRules.GateRepairPerDay"/>) — Hüllenplatten <see cref="EconomyRules.StationRepairPlatesPerHull"/> je Punkt zum Grundpreis ×
/// <see cref="EconomyRules.StationRepairMarkup"/>, nur aus <see cref="FactionViability.Spendable"/> (nie unter die Rücklage). Ab
/// <see cref="EconomyRules.GateReenableShare"/> läuft das Paar wieder; ohne Kasse nach <see cref="GateCombat.OutageLimit"/> (Notregel, „Torbehörde“) —
/// die Karte zerfällt nie dauerhaft.
/// </summary>
public sealed class GateRepairSystem : ISimSystem
{
    public const int Interval = 50;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        Good? plates = null; bool looked = false;
        foreach (var g in u.Gates.Values.OrderBy(x => x.Id.Value).ToList())
        {
            if (g.Condition is not { } c) continue;
            if (!looked) { plates = u.FindGood("hull_plates"); looked = true; }
            bool sieged = GateCombat.UnderSiege(u, g);
            var t = u.GateTypes.GetValueOrDefault(g.TypeKey);
            float regenSeconds = ShipSystems.Seconds((int)Math.Clamp(u.Tick - c.ShieldPauseUntil, 0L, (long)dt));
            if (c.ShieldMissing > 0f && regenSeconds > 0f && t is not null && !c.Disabled)
                c.ShieldMissing = MathF.Max(0f, c.ShieldMissing - t.Defense.ShieldRegen * regenSeconds);
            if (!sieged) for (int i = 0; i < c.Timers.Count; i++) c.Timers[i] = 0f;
            if (!sieged && c.HullMissing > 0f && t is not null) Repair(u, g, c, t, dt, plates);
            if (c.Disabled)
            {
                float max = GateCombat.MaxHull(u, g);
                if (max > 0f && GateCombat.Hull(u, g) >= r.GateReenableShare * max && !sieged) GateCombat.Restore(u, g, "repaired");
                else if (u.Tick - c.DownSince >= GateCombat.OutageLimit(u, g)) GateCombat.Restore(u, g, "emergency");
                else if (t is null || !r.GateCombat) GateCombat.Restore(u, g, "emergency");   // Torkampf abgeschaltet: nichts bleibt aus
            }
            if (c.GraceUntil != 0 && c.GraceUntil <= u.Tick) c.GraceUntil = 0;   // Runde 14b: Schonfrist abgelaufen
            if (c.IsDefault) g.Condition = null;
        }
    }

    static void Repair(Universe u, Gate g, GateCondition c, GateType t, int dt, Good? good)
    {
        var r = u.Rules;
        float perDay = t.RepairPerDay > 0f ? t.RepairPerDay : r.GateRepairPerDay;
        float points = MathF.Min(c.HullMissing, perDay * dt / MathF.Max(1f, r.DayTicks));
        if (points <= 0f || good is null) return;
        float price = good.BasePrice * MathF.Max(0f, r.StationRepairMarkup) * r.StationRepairPlatesPerHull;   // je Hüllenpunkt
        var attackerFaction = u.FactionOf(c.LastAttacker);
        foreach (var f in GateCombat.DefendersOf(u, g))
        {
            if (attackerFaction == f || !u.Actors.TryGetValue(f, out var a)) continue;
            float spend = FactionViability.Spendable(u, a);
            float affordable = price > 0f ? MathF.Min(points, MathF.Max(0f, spend) / price) : points;
            if (affordable <= 0f) continue;
            float cost = affordable * price;
            a.Treasury.Credits -= cost;
            c.HullMissing = MathF.Max(0f, c.HullMissing - affordable);
            u.Events.Publish(new TreasuryUpkeep(a.Id, "gate_repair", cost, 0f));
            u.Events.Publish(new GateRepaired(GateCombat.Canonical(u, g).Id, affordable, cost, f));
            return;
        }
    }
}

/// <summary>Phase 31: Anzeigetexte für Tore (rein).</summary>
public static class GateInfo
{
    /// <summary>Zustand: „intakt“, „beschädigt“ (≤ 50 % Hülle), „außer Betrieb“.</summary>
    public static string State(Universe u, Gate g)
    {
        if (GateCombat.IsDown(u, g)) return "außer Betrieb";
        float max = GateCombat.MaxHull(u, g);
        return max > 0f && GateCombat.Hull(u, g) <= 0.5f * max ? "beschädigt" : "intakt";
    }

    /// <summary>Kurzzeile für die Zielkarte: Hülle, Schild, Zustand (außer Betrieb mit Wiederanlauf).</summary>
    public static string Short(Universe u, Gate g)
    {
        if (GateCombat.IsDown(u, g)) return $"AUSSER BETRIEB · läuft in {GateCombat.Clock(GateCombat.RestartIn(u, g))} · Hülle {GateCombat.Hull(u, g):0}";
        string s = $"Hülle {GateCombat.Hull(u, g):0}" + (GateCombat.MaxShield(u, g) > 0f ? $" · Schild {GateCombat.Shield(u, g):0}" : "");
        if (GateCombat.InGrace(u, g)) return s + $" · Notbetrieb, unangreifbar {GateCombat.Clock(GateCombat.GraceLeft(u, g))}";   // Runde 14b
        return State(u, g) == "beschädigt" ? s + " · beschädigt" : s;
    }

    /// <summary>Beschriftung am Tor in der 3D-Ansicht (leer, wenn intakt).</summary>
    public static string Label(Universe u, Gate g)
    {
        if (GateCombat.TypeOf(u, g) is null) return "";
        if (GateCombat.IsDown(u, g))
        {
            float max = GateCombat.MaxHull(u, g);
            float need = max * u.Rules.GateReenableShare;
            int pct = need > 0f ? (int)MathF.Min(100f, GateCombat.Hull(u, g) / need * 100f) : 0;
            return $"AUSSER BETRIEB · Reparatur {pct} % · spätestens in {GateCombat.Clock(GateCombat.RestartIn(u, g))}";
        }
        return State(u, g) == "beschädigt" ? $"beschädigt ({GateCombat.Hull(u, g) / MathF.Max(1f, GateCombat.MaxHull(u, g)) * 100f:0} %)" : "";
    }
}
