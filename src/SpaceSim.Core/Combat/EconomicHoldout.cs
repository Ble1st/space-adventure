using System.Runtime.CompilerServices;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Wirtschaftsstationen halten stand (docs/KAMPF.md, Nutzerwunsch „Wirtschaftsstationen stärken gegen Flotten“): Ein Zerstörer oder ein kleiner Verband nahm
/// eine Wirtschaftsstation trotz Vorgabeverteidigung (<see cref="EconomicDefense"/>) in etwa 3,5 Minuten. Zwei Regeln, beide über eigene Schalter:
/// <list type="bullet">
/// <item><b>Alarm</b> (<see cref="EconomyRules.EconomicStationAlarm"/>): der Beginn einer Belagerung und jeder Treffer halten den Alarm der Station
/// (<see cref="StationCondition.AlarmUntil"/>) aufrecht. Polizei und Kriegsschiffe der Besitzerfraktion und ihrer Verbündeten bis
/// <see cref="EconomyRules.EconomicAlarmJumps"/> Sprünge entfernt fliegen über ihre normalen Befehle (Streife im Sektor der Station) hin; im Sektor nimmt sie
/// die Belagerungsverstärkung (<see cref="Siege.Reinforce"/>) ins Gefecht. Kein Versetzen, kein Zufallszug.</item>
/// <item><b>Notschild</b> (<see cref="EconomyRules.EconomicEmergencyShield"/>): unter <see cref="EconomyRules.EconomicEmergencyShieldHull"/> der Hülle hält die
/// Station für <see cref="EconomyRules.EconomicEmergencyShieldTicks"/> einen Teil des Hüllenschadens ab, danach Abklingzeit.</item>
/// </list>
/// Nur Wirtschaftsstationen (keine Regierungs-, Kleinstationen, Verstecke, Baustellen). In einer Welt ohne Angriffe auf Stationen ändert sich nichts.
/// </summary>
public static class EconomicHoldout
{
    /// <summary>Wirtschaftsstation im Sinn dieser Regeln (fertig, ohne Regierungsart, kein Versteck).</summary>
    public static bool IsEconomic(Universe u, Station st) =>
        !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Government == GovernmentKind.None && !t.Hideout;

    static StationCondition Cond(Station st) => st.Condition ??= new StationCondition();

    /// <summary>Läuft der Alarm der Station?</summary>
    public static bool Alarmed(Universe u, Station st) => u.Rules.EconomicStationAlarm && st.Condition is { } c && c.AlarmUntil > u.Tick;

    /// <summary>Ist der Notschild gerade oben?</summary>
    public static bool ShieldUp(Universe u, Station st) => u.Rules.EconomicEmergencyShield && st.Condition is { } c && c.EmergencyUntil > u.Tick;

    /// <summary>
    /// Die Station wird angegriffen (Beginn der Belagerung, jeder Treffer): Alarm verlängern; ein neuer Alarm meldet <see cref="StationAlarmRaised"/> mit der
    /// Zahl der Verteidiger, die ihm folgen.
    /// </summary>
    public static void OnAttacked(Universe u, Station st)
    {
        var r = u.Rules;
        if (!r.EconomicStationAlarm || !IsEconomic(u, st) || u.FactionOf(st) is null) return;
        var c = Cond(st);
        bool fresh = c.AlarmUntil <= u.Tick;
        c.AlarmUntil = u.Tick + Math.Max(1, r.EconomicAlarmTicks);
        if (!fresh) return;
        Invalidate(u);
        int n = 0;
        foreach (var (_, a) in Assignments(u)) if (a.Station == st.Id) n++;
        string where = u.Sectors.TryGetValue(st.Sector, out var sec) ? sec.Name : "?";
        string msg = n > 0 ? $"Alarm: {st.Name} wird angegriffen — {n} Verteidiger unterwegs" : $"Alarm: {st.Name} wird angegriffen — keine Verteidiger in der Nähe";
        u.Events.Publish(new StationAlarmRaised(st.Id, st.Sector, st.Owner, n, msg + (n > 0 ? "" : $" ({where})")));
    }

    /// <summary>
    /// Hüllenschaden durch den Notschild (<paramref name="hullDamage"/> vor der Anwendung) und Auslösen nach dem Treffer (<see cref="AfterHit"/>). Liefert den
    /// verbleibenden Hüllenschaden.
    /// </summary>
    public static float Damp(Universe u, Station st, float hullDamage) =>
        hullDamage > 0f && ShieldUp(u, st) ? hullDamage * (1f - Math.Clamp(u.Rules.EconomicEmergencyShieldReduction, 0f, 1f)) : hullDamage;

    /// <summary>Nach einem Treffer: unter der Schwelle und ohne Abklingzeit hebt die Station den Notschild (<see cref="StationEmergencyShield"/>).</summary>
    public static void AfterHit(Universe u, Station st, float hullFraction)
    {
        var r = u.Rules;
        if (!r.EconomicEmergencyShield || !IsEconomic(u, st) || hullFraction <= 0f || hullFraction >= r.EconomicEmergencyShieldHull) return;
        var c = Cond(st);
        if (c.EmergencyUntil > u.Tick || c.EmergencyCooldownUntil > u.Tick) return;
        int ticks = Math.Max(1, r.EconomicEmergencyShieldTicks);
        c.EmergencyUntil = u.Tick + ticks;
        c.EmergencyCooldownUntil = u.Tick + ticks + Math.Max(0, r.EconomicEmergencyShieldCooldownTicks);
        u.Events.Publish(new StationEmergencyShield(st.Id, st.Sector, true,
            $"{st.Name}: Notschild hoch — {Math.Clamp(r.EconomicEmergencyShieldReduction, 0f, 1f) * 100f:0} % weniger Hüllenschaden für {ticks / SimClock.TicksPerSecond} s"));
    }

    // ───────── Verteidiger zuteilen (je Takt einmal, feste Reihenfolge) ─────────

    sealed class Cache { public long Tick = -1; public Dictionary<ShipId, (StationId Station, SectorId Sector)> Map = new(); }
    static readonly ConditionalWeakTable<Universe, Cache> Caches = new();

    static void Invalidate(Universe u) { if (Caches.TryGetValue(u, out var c)) c.Tick = -1; }

    /// <summary>Sektor der alarmierten Station, die dieses Polizei-/Kriegsschiff verteidigen soll (null = keine).</summary>
    public static SectorId? AlarmSectorFor(Universe u, Ship s) =>
        u.Rules.EconomicStationAlarm && Assignments(u).TryGetValue(s.Id, out var a) ? a.Sector : null;

    /// <summary>
    /// Zuteilung Verteidiger → alarmierte Station: je NPC-Fraktion (Id-Reihenfolge) ihre freien Polizei- und Kriegsschiffe (nicht im Gefecht, kein Notfall,
    /// nicht auf dem Heimweg oder in Verfolgung, ohne Versorgungsbedarf; Verbandsmitglieder folgen ihrem Anführer) zur nächsten alarmierten Station der eigenen
    /// oder einer verbündeten Fraktion in Reichweite (Sprünge, dann Stations-Id); je Fraktion und Station höchstens <see cref="EconomyRules.EconomicAlarmMaxResponders"/>
    /// (die nächsten nach Sprüngen, dann Id). Rein, ohne Zufall.
    /// </summary>
    public static IReadOnlyDictionary<ShipId, (StationId Station, SectorId Sector)> Assignments(Universe u)
    {
        var cache = Caches.GetValue(u, _ => new Cache());
        if (cache.Tick == u.Tick) return cache.Map;
        cache.Tick = u.Tick;
        cache.Map.Clear();
        var r = u.Rules;
        if (!r.EconomicStationAlarm) return cache.Map;
        List<(Station St, FactionId F)>? alarms = null;
        foreach (var st in u.StationsById())
            if (st.Condition is { } c && c.AlarmUntil > u.Tick && !StationCombat.IsDown(u, st) && u.FactionOf(st) is { } f && IsEconomic(u, st))
                (alarms ??= new()).Add((st, f));
        if (alarms is null) return cache.Map;
        int jumps = Math.Max(0, r.EconomicAlarmJumps);
        var picks = new List<(Ship S, int Hops, Station St)>();
        foreach (var s in u.ShipsById())
        {
            if (s.Controller != Controller.Ai || !(ThreatMap.IsPolice(s) || Wars.IsWarship(s)) || u.FactionOf(s) is not { } sf) continue;
            if (u.Fights.IsEngaged(s.Id) || s.Emergency || s.Command is ReturnCommand or PursueCommand || PoliceCommands.NeedsBase(u, s)) continue;
            if (Squadrons.Get(u, s) is { } sq && sq.Leader != s.Id) continue;
            var from = ThreatMap.Where(u, s).Sector;
            Station? best = null; int bestHops = int.MaxValue;
            foreach (var (st, f) in alarms)
            {
                if (f != sf && !Alliances.Allied(u, sf, f)) continue;
                int hops = from == st.Sector ? 0 : u.CachedRoute(from, st.Sector)?.Count ?? -1;
                if (hops < 0 || hops > jumps) continue;
                if (hops < bestHops || (hops == bestHops && best is not null && st.Id.Value < best.Id.Value)) { best = st; bestHops = hops; }
            }
            if (best is not null) picks.Add((s, bestHops, best));
        }
        int cap = r.EconomicAlarmMaxResponders;
        foreach (var g in picks.GroupBy(p => (Faction: u.FactionOf(p.S)!.Value.Value, Station: p.St.Id.Value)).OrderBy(g => g.Key.Faction).ThenBy(g => g.Key.Station))
        {
            int n = 0;
            foreach (var p in g.OrderBy(p => p.Hops).ThenBy(p => p.S.Id.Value))
            {
                if (cap > 0 && n >= cap) break;
                cache.Map[p.S.Id] = (p.St.Id, p.St.Sector);
                n += Squadrons.Get(u, p.S) is { } sq ? Math.Max(1, sq.Members.Count) : 1;
            }
        }
        return cache.Map;
    }

    /// <summary>Verbündete NPC-Fraktionen des Besitzers, die bei einem Alarm mit verteidigen (leer ohne Alarmregel).</summary>
    public static IEnumerable<FactionId> AlliedDefenders(Universe u, Station st)
    {
        if (!u.Rules.EconomicStationAlarm || !IsEconomic(u, st) || u.FactionOf(st) is not { } f) yield break;
        foreach (var a in Alliances.AlliesOf(u, f)) if (u.IsNpcFaction(a)) yield return a;
    }
}
