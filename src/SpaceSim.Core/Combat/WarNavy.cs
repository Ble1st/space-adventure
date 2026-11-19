using System.Runtime.CompilerServices;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>Ausbau Punkt 2: eine Seeschlacht ist entbrannt (Anzeige, Balance). Nicht gespeichert.</summary>
public sealed record NavalBattleStarted(int Encounter, SectorId Sector, FactionId A, FactionId B, ShipId First, ShipId Second) : SimEvent;

/// <summary>Ausbau Punkt 2: ein Kriegsschiff wird zur Abwehr eines Angriffs auf ein eigenes Ziel geschickt (Anzeige, Balance). Nicht gespeichert.</summary>
public sealed record WarDefenseAssigned(ShipId Ship, FactionId Faction, SectorId Sector, string Target) : SimEvent;

/// <summary>
/// Ausbau Punkt 2 (docs/KAMPF.md „KI-Kriegsschiffe: Seeschlacht, Abwehr, Geleit“): Seeschlacht. Kriegsschiffe zweier KI-Fraktionen im Krieg (kein
/// Waffenstillstand), die im selben Sektor auf höchstens <see cref="EconomyRules.NavalBattleRange"/> zusammentreffen, beginnen ein Gefecht mit dem Anlass
/// <see cref="EncounterCause.Naval"/>; ein Kriegsschiff kann auch eine feindliche Polizeistreife stellen (Polizei gegen Polizei beginnt keine Schlacht).
/// Danach treten alle einsatzbereiten Kriegs- und Polizeischiffe beider Seiten (und ihrer Verbündeten im selben Krieg) im Sektor in Reichweite bei.
/// Rückzugsregeln wie bisher (Begleit- bzw. Polizeitaktik mit Fokusfeuer). Auslösung ohne Zufallszug; jeder Beitritt zieht wie immer zwei Zahlen
/// (<see cref="Engage.Join"/>). Ohne Regel <see cref="EconomyRules.NavalBattles"/> oder ohne Krieg passiert nichts.
/// </summary>
public static class NavalBattle
{
    /// <summary>Kriegs- oder Polizeischiff einer KI-Fraktion, das kämpfen kann (frei, nicht angedockt, nicht auf dem Heimweg, Hülle über der Rückzugsschwelle).</summary>
    public static bool Ready(Universe u, Ship s)
    {
        if (s.Controller != Controller.Ai || !(Wars.IsWarship(s) || ThreatMap.IsPolice(s))) return false;
        if (u.FactionOf(s) is not { } f || !u.IsNpcFaction(f)) return false;
        if (u.Fights.IsEngaged(s.Id) || s.Emergency || s.DockedAt.Value != 0 || s.Command is ReturnCommand) return false;
        if (s.Hull < Reinforcements.RetreatHull(u, s) * s.MaxHull) return false;
        return Wars.IsWarship(s) ? !NavySystem.NeedsBase(u, s) : !PoliceCommands.NeedsBase(u, s);
    }

    /// <summary>Fraktion einer Gefechtsseite (erster Teilnehmer der Seite, auch ausgeschieden; null = keine).</summary>
    public static FactionId? SideFaction(Universe u, Encounter e, int side)
    {
        foreach (var p in e.Participants) if (p.Side == side && u.FactionOf(p.Owner) is { } f) return f;
        return null;
    }

    /// <summary>Darf ein Schiff der Fraktion <paramref name="g"/> auf der Seite der Fraktion <paramref name="mine"/> gegen <paramref name="enemy"/> kämpfen?</summary>
    static bool Fits(Universe u, FactionId g, FactionId mine, FactionId enemy) =>
        (g == mine || Alliances.Allied(u, g, mine)) && Wars.Hostile(u, g, enemy);

    /// <summary>Takt: neue Seeschlachten auslösen (Kriegsschiffe in Id-Reihenfolge, je eins den nächsten Gegner).</summary>
    public static void Trigger(Universe u)
    {
        var r = u.Rules;
        if (!r.NavalBattles || !r.Warfare || !r.FleetCombat || u.Wars.Count == 0) return;
        float range = MathF.Max(1f, r.NavalBattleRange);
        foreach (var w in u.ShipsById().Where(Wars.IsWarship).ToList())
        {
            if (!Ready(u, w) || u.FactionOf(w) is not { } f) continue;
            var (sec, pos) = ThreatMap.Where(u, w);
            Ship? best = null; float bd = float.MaxValue;
            foreach (var id in u.Tracker.ParkedIn(sec).Concat(u.Tracker.TransitIn(sec)).Distinct().OrderBy(i => i.Value))
            {
                if (id == w.Id || !u.Ships.TryGetValue(id, out var o) || !Ready(u, o) || u.FactionOf(o) is not { } g || !Wars.Hostile(u, f, g)) continue;
                var (os, op) = ThreatMap.Where(u, o);
                if (os != sec) continue;
                float d = Vec2.Distance(pos, op);
                if (d <= range && d < bd - 1e-4f) { bd = d; best = o; }
            }
            if (best is not null) Start(u, w, best, sec, bd);
        }
    }

    /// <summary>Beginnt die Seeschlacht: Auslöser auf Seite 0, Gegner auf Seite 1, dann Verstärkung beider Seiten im Sektor.</summary>
    public static Encounter Start(Universe u, Ship first, Ship second, SectorId sector, float distance)
    {
        var r = u.Rules;
        var e = Engage.Begin(u, EncounterCause.Naval, sector);
        e.Police = second.Id;
        var a = ThreatMap.Where(u, first).Position;
        var b = ThreatMap.Where(u, second).Position;
        Engage.Join(u, e, first, 0, TacticOf(first), 0f, e.Spatial ? a : null);
        Engage.Join(u, e, second, 1, TacticOf(second), MathF.Min(distance, 2f * r.CombatStartDistance), e.Spatial ? b : null);
        var fa = u.FactionOf(first)!.Value;
        var fb = u.FactionOf(second)!.Value;
        string where = u.Sectors.TryGetValue(sector, out var s) ? s.Name : "?";
        u.Events.Publish(new EncounterStarted(e.Id, sector, EncounterCause.Naval, first.Id, second.Id,
            $"Seeschlacht in {where}: {Name(u, fa)} gegen {Name(u, fb)}"));
        u.Events.Publish(new NavalBattleStarted(e.Id, sector, fa, fb, first.Id, second.Id));
        Reinforce(u, e);
        Reinforcements.Step(u, e);
        return e;
    }

    static string Name(Universe u, FactionId f) => u.Factions.TryGetValue(f, out var fac) ? fac.Name : "?";

    static string TacticOf(Ship s) => ThreatMap.IsPolice(s) ? CombatTactics.Police : CombatTactics.Escort;

    /// <summary>
    /// Verstärkung (Beginn und alle <see cref="EconomyRules.ReinforceTicks"/>): einsatzbereite Kriegs- und Polizeischiffe im Sektor, die einem aktiven Teilnehmer
    /// auf höchstens <see cref="EconomyRules.NavalBattleRange"/> nahe sind, treten der Seite ihrer Fraktion (oder eines Verbündeten im selben Krieg) bei.
    /// </summary>
    public static void Reinforce(Universe u, Encounter e)
    {
        var r = u.Rules;
        if (!r.NavalBattles || e.Done || SideFaction(u, e, 0) is not { } a || SideFaction(u, e, 1) is not { } b) return;
        float range = MathF.Max(1f, r.NavalBattleRange);
        foreach (var id in u.Tracker.ParkedIn(e.Sector).Concat(u.Tracker.TransitIn(e.Sector)).Distinct().OrderBy(i => i.Value).ToList())
        {
            if (!u.Ships.TryGetValue(id, out var s) || !Ready(u, s) || u.FactionOf(s) is not { } g) continue;
            int side = Fits(u, g, a, b) ? 0 : Fits(u, g, b, a) ? 1 : -1;
            if (side < 0) continue;
            var (ss, sp) = ThreatMap.Where(u, s);
            if (ss != e.Sector) continue;
            CombatParticipant? near = null; float nd = float.MaxValue;
            foreach (var p in e.Participants)
            {
                if (!p.Active || p.Side != side || !u.Ships.TryGetValue(p.Ship, out var ps)) continue;
                float d = Vec2.Distance(sp, e.Spatial ? p.Pos : ps.Position);
                if (d < nd) { nd = d; near = p; }
            }
            if (near is null || nd > range) continue;
            Reinforcements.JoinSide(u, e, s, side, "naval", near);
        }
    }

    /// <summary>Ein Teilnehmer ist zerstört: sein Materialwert × <see cref="EconomyRules.NavalLossShare"/> zählt als Kriegsverlust seiner Fraktion.</summary>
    public static void OnDestroyed(Universe u, Encounter e, CombatParticipant p, Ship ship)
    {
        var r = u.Rules;
        if (!r.NavalBattles || e.Cause != EncounterCause.Naval || r.NavalLossShare <= 0f) return;
        foreach (var q in e.Participants)
            if (q.Side != p.Side) { Wars.OnLoss(u, ship.Owner, q.Owner, Hangar.MaterialValue(u, ship) * r.NavalLossShare); return; }
    }
}

/// <summary>Ausbau Punkt 2: löst Seeschlachten aus (Combat-Modul, Takt <see cref="Interval"/>). Ohne Regel oder ohne Krieg kostet es nichts.</summary>
public sealed class NavalBattleSystem : ISimSystem
{
    public const int Interval = 5;
    public void Step(Universe u, int dt) => NavalBattle.Trigger(u);
}

/// <summary>
/// Ausbau Punkt 2: gezielte Abwehr und Geleitschutz der Kriegsschiffe im Krieg (aufgerufen aus <see cref="NavySystem"/>). Rein, ohne Zufall, feste Reihenfolge.
/// </summary>
public static class WarNavy
{
    /// <summary>Freies Kriegsschiff (nicht im Gefecht, kein Notfall, nicht auf dem Heimweg, ohne Versorgungsbedarf).</summary>
    static bool Free(Universe u, Ship s) =>
        Wars.IsWarship(s) && !u.Fights.IsEngaged(s.Id) && !s.Emergency && s.Command is not ReturnCommand && !NavySystem.NeedsBase(u, s);

    sealed class Cache { public long Tick = -1; public Dictionary<ShipId, (SectorId Sector, string Name)> Map = new(); }
    static readonly ConditionalWeakTable<Universe, Cache> Caches = new();

    /// <summary>Sektor des angegriffenen eigenen Ziels, das dieses Kriegsschiff verteidigen soll (null = keins).</summary>
    public static SectorId? DefenseSectorFor(Universe u, Ship s) =>
        u.Rules.WarDefense && Assignments(u).TryGetValue(s.Id, out var a) ? a.Sector : null;

    /// <summary>Name des Ziels der Abwehr (Anzeige).</summary>
    public static string? DefenseTargetName(Universe u, Ship s) => Assignments(u).TryGetValue(s.Id, out var a) ? a.Name : null;

    /// <summary>
    /// Angegriffene Ziele: Stationen und Tore, auf die ein Kriegsschiff eines Kriegsgegners einen Angriffsbefehl hat und in deren Sektor es schon steht, und laufende Belagerungen (auch durch den
    /// Spieler im Krieg); je Ziel die KI-Verteidigerfraktionen, die mit einem Angreifer im Krieg stehen (Waffenstillstand zählt nicht). Sortiert nach Ziel.
    /// </summary>
    public static List<(ISiegeTarget Target, List<FactionId> Defenders)> Threats(Universe u)
    {
        var map = new SortedDictionary<(int Kind, int Id), (ISiegeTarget T, SortedSet<int> F)>();
        void Add(ISiegeTarget t, FactionId attacker)
        {
            if (t.IsDown) return;
            foreach (var f in t.Defenders)
            {
                if (!u.IsNpcFaction(f) || !Wars.Hostile(u, f, attacker)) continue;
                var key = ((int)t.Ref.Kind, t.Ref.Id);
                if (!map.TryGetValue(key, out var v)) map[key] = v = (t, new SortedSet<int>());
                v.F.Add(f.Value);
            }
        }
        foreach (var w in u.ShipsById())
            if (Wars.IsWarship(w) && w.Command is AssaultCommand ac && u.FactionOf(w) is { } g && AssaultExecutor.TargetOf(u, ac) is { } t
                && ThreatMap.Where(u, w).Sector == t.Sector) Add(t, g);   // „greift gerade an“: der Angreifer steht schon im Sektor des Ziels
        foreach (var e in u.Fights.Encounters)
        {
            if (e.Done || e.Cause != EncounterCause.Siege || SiegeTargets.Of(u, e) is not { } t || !t.Defends(e)) continue;
            foreach (var p in e.Participants)
                if (p.Active && p.Side == 0 && u.FactionOf(p.Owner) is { } g) Add(t, g);
        }
        return map.Values.Select(v => (v.T, v.F.Select(i => new FactionId(i)).ToList())).ToList();
    }

    /// <summary>
    /// Zuteilung Kriegsschiff → angegriffenes Ziel: je Fraktion (Id-Reihenfolge) ihre freien Kriegsschiffe zu den Zielen in Reichweite
    /// (<see cref="EconomyRules.WarDefenseJumps"/>), die nächsten nach Sprüngen, dann Id, höchstens <see cref="EconomyRules.WarDefenseMaxResponders"/> je Ziel;
    /// Rücklage: bei zwei und mehr Kriegsschiffen bleiben <see cref="EconomyRules.WarDefenseReserve"/> bei ihrem Auftrag (ein einzelnes verteidigt).
    /// Wer schon im Sektor des Ziels steht, zählt mit (die Belagerungsverstärkung nimmt ihn ins Gefecht).
    /// </summary>
    public static IReadOnlyDictionary<ShipId, (SectorId Sector, string Name)> Assignments(Universe u)
    {
        var cache = Caches.GetValue(u, _ => new Cache());
        if (cache.Tick == u.Tick) return cache.Map;
        cache.Tick = u.Tick;
        cache.Map.Clear();
        var r = u.Rules;
        if (!r.WarDefense || !r.Warfare || u.Wars.Count == 0) return cache.Map;
        var threats = Threats(u);
        if (threats.Count == 0) return cache.Map;
        int jumps = Math.Max(0, r.WarDefenseJumps);
        foreach (var f in u.NpcFactionIds())
        {
            var mine = Wars.WarshipsOf(u, f);
            if (mine.Count == 0) continue;
            int allowed = mine.Count >= 2 ? Math.Max(0, mine.Count - Math.Max(0, r.WarDefenseReserve)) : mine.Count;
            var free = mine.Where(s => Free(u, s) && (Squadrons.Get(u, s) is not { } sq || sq.Leader == s.Id)).ToList();
            foreach (var (t, defenders) in threats)
            {
                if (allowed <= 0) break;
                if (!defenders.Contains(f)) continue;
                var cands = new List<(Ship S, int Hops)>();
                foreach (var s in free)
                {
                    if (cache.Map.ContainsKey(s.Id)) continue;
                    var from = ThreatMap.Where(u, s).Sector;
                    int hops = from == t.Sector ? 0 : u.CachedRoute(from, t.Sector)?.Count ?? -1;
                    if (hops >= 0 && hops <= jumps) cands.Add((s, hops));
                }
                int n = 0;
                foreach (var (s, _) in cands.OrderBy(c => c.Hops).ThenBy(c => c.S.Id.Value))
                {
                    if (allowed <= 0 || (r.WarDefenseMaxResponders > 0 && n >= r.WarDefenseMaxResponders)) break;
                    cache.Map[s.Id] = (t.Sector, t.Name);
                    n++; allowed--;
                }
            }
        }
        return cache.Map;
    }

    // ───────── Geleitschutz ─────────

    /// <summary>Läuft das Geleit noch (Regel an, Krieg, Schützling unterwegs oder im Gefecht, höchstens <see cref="EconomyRules.WarEscortMaxTicks"/>)?</summary>
    public static bool EscortStillValid(Universe u, Ship s, EscortCommand ec) =>
        u.Rules.WarEscorts && u.FactionOf(s) is { } f && Wars.CountOf(u, f) > 0 && u.Ships.TryGetValue(ec.Target, out var w)
        && (w.Transit is not null || u.Fights.IsEngaged(w.Id)) && u.Tick - s.CommandSince < Math.Max(1, u.Rules.WarEscortMaxTicks);

    /// <summary>Feindliches oder an feindliches Gebiet grenzendes Gebiet für <paramref name="f"/>?</summary>
    public static bool Risky(Universe u, FactionId f, SectorId sector)
    {
        if (!u.Sectors.TryGetValue(sector, out var sec)) return false;
        bool Enemy(Sector s) => s.Owner.Value != 0 && Wars.Hostile(u, f, s.Owner);
        if (Enemy(sec)) return true;
        foreach (var gid in sec.Gates)
            if (u.Gates.TryGetValue(gid, out var g) && u.Gates.TryGetValue(g.Target, out var far) && u.Sectors.TryGetValue(far.Sector, out var ns) && Enemy(ns)) return true;
        return false;
    }

    /// <summary>
    /// Schützling für ein freies Kriegsschiff der Fraktion <paramref name="f"/> (null = keiner): wenn die Fraktion weniger als
    /// <see cref="EconomyRules.WarEscortsPerFaction"/> Geleite hat — der wertvollste beladene KI-Händler (Fracht ≥ <see cref="EconomyRules.WarEscortMinValue"/>,
    /// unterwegs, noch nicht begleitet) der Fraktion oder ohne Fraktion, dessen restliche Route einen riskanten Sektor (<see cref="Risky"/>) berührt und der
    /// höchstens <see cref="EconomyRules.WarEscortJumps"/> Sprünge entfernt ist. Wert, dann Id.
    /// </summary>
    public static Ship? EscortWard(Universe u, FactionId f, Ship warship)
    {
        var r = u.Rules;
        if (!r.WarEscorts || !r.Warfare || Wars.CountOf(u, f) == 0 || r.WarEscortsPerFaction <= 0 || !Free(u, warship)) return null;
        var mine = Wars.WarshipsOf(u, f);
        int running = mine.Count(s => s.Command is EscortCommand ec && EscortStillValid(u, s, ec));
        if (running >= r.WarEscortsPerFaction) return null;
        var escorted = new HashSet<ShipId>(u.Ships.Values.Where(s => s.Command is EscortCommand).Select(s => ((EscortCommand)s.Command!).Target));
        var from = ThreatMap.Where(u, warship).Sector;
        Ship? best = null; float bv = MathF.Max(0f, r.WarEscortMinValue);
        foreach (var s in u.ShipsById())
        {
            if (!s.IsEconomyAi || s.Role != ShipRole.Trader || s.Transit is not { } tr || u.Fights.IsEngaged(s.Id) || escorted.Contains(s.Id)) continue;
            if (tr.ArrivalTick - u.Tick < 100) continue;
            var own = u.FactionOf(s);
            if (own is { } of && of != f && !Alliances.Allied(u, of, f)) continue;
            float v = ThreatMap.CargoValue(u, s);
            if (v < bv || (best is not null && v <= bv)) continue;
            var here = ThreatMap.Where(u, s).Sector;
            bool risky = false, touchesOwn = own is not null;
            foreach (var leg in tr.Legs)
            {
                if (leg.EndTick < u.Tick) continue;
                if (Risky(u, f, leg.Sector)) risky = true;
                if (u.Sectors.TryGetValue(leg.Sector, out var ls) && ls.Owner == f) touchesOwn = true;
            }
            if (!risky || !touchesOwn) continue;
            int hops = from == here ? 0 : u.CachedRoute(from, here)?.Count ?? -1;
            if (hops < 0 || hops > Math.Max(0, r.WarEscortJumps)) continue;
            best = s; bv = v;
        }
        return best;
    }
}
