using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;

namespace SpaceSim.Core.Simulation;

/// <summary>Welle C (1): Vermögensabgabe fremder Zahler (freie Schiffe, fraktionslose Stationen) in Sektoren der Spielerfraktion, einmal je Upkeep-Schritt.</summary>
public sealed record PlayerSectorTaxCollected(float Amount) : SimEvent;

/// <summary>Welle C (2): der Spieler hat eine eigene Behörde ruhen lassen (<paramref name="Dormant"/> true) oder wieder angeschaltet.</summary>
public sealed record PlayerGovernmentRested(StationId Station, string Name, SectorId Sector, bool Dormant) : SimEvent;

/// <summary>
/// Welle C (2): gestaffelter Regierungsunterhalt des Spielers. Statt die Behörden in Listenreihenfolge zu bezahlen (die Kasse fiel auf 0 und alle 22
/// eroberten Behörden wurden im selben Schritt unbezahlt), zahlt der Spieler in fester Rangfolge — Hauptquartier, dann Behörden, deren Ausfall den Sektor
/// kosten würde, dann übrige eigene Sektoren, dann der Rest (Einfluss absteigend, Id). Eine Station wird nur bezahlt, wenn danach die Kasse über der
/// Untergrenze plus dem Vorrat <see cref="EconomyRules.PlayerUpkeepReserveTicks"/> aller wichtigeren Stationen bleibt: bei sinkender Kasse fallen die
/// unwichtigsten zuerst, die wichtigen bleiben. Ruhende Behörden (<see cref="Station.Dormant"/>, vom Spieler gewählt) zahlen nichts und geben keinen Einfluss.
/// Kein Zufall; nur Stationen des Spielers.
/// </summary>
public static class PlayerUpkeepOrder
{
    /// <summary>Gilt die Rangfolge (Regel an, Untergrenze an, Spielerfraktion vorhanden)?</summary>
    public static bool Active(Universe u) => u.Rules.PlayerUpkeepPriority && u.Rules.PlayerUpkeepFloorActive && u.PlayerActor.Value != 0;

    /// <summary>Alle fertigen Regierungsstationen des Spielers (Id-Reihenfolge), auch ruhende.</summary>
    public static List<Station> Governments(Universe u)
    {
        var list = new List<Station>();
        foreach (var st in u.StationsById())
            if (!st.IsBuildSite && st.Owner == u.PlayerActor && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Government != GovernmentKind.None) list.Add(st);
        return list;
    }

    /// <summary>
    /// Rang einer Station (kleiner = wichtiger): 0 Hauptquartier, 1 ohne sie ginge der eigene Sektor verloren, 2 übriger eigener Sektor, 3 Rest. Rein.
    /// </summary>
    public static int Rank(Universe u, Station st, Dictionary<SectorId, InfluenceResult>? cache = null)
    {
        if (!u.StationTypes.TryGetValue(st.TypeKey, out var t)) return 3;
        if (t.Government == GovernmentKind.Headquarters) return 0;
        if (u.PlayerFaction is not { } me || !u.Sectors.TryGetValue(st.Sector, out var sec) || sec.Owner != me) return 3;
        if (cache is null || !cache.TryGetValue(sec.Id, out var r)) { r = SectorControl.Compute(u, sec); cache?.Add(sec.Id, r); }
        float p = r.Shares.GetValueOrDefault(me);
        float s = r.Total > 0f && !PlayerUpkeep.Unpaid(u, st) ? MathF.Max(0f, t.Influence) / r.Total : 0f;
        float best = 0f;
        foreach (var (f, v) in r.Shares) if (f != me) best = MathF.Max(best, v);
        return best > 0f && best >= p - s + u.Rules.OwnerSwitchMargin ? 1 : 2;
    }

    /// <summary>Nicht ruhende Regierungsstationen des Spielers in Zahlungsreihenfolge. Rein.</summary>
    public static List<Station> Order(Universe u)
    {
        var cache = new Dictionary<SectorId, InfluenceResult>();
        return Governments(u).Where(s => !s.Dormant)
            .Select(s => (St: s, Rank: Rank(u, s, cache), W: u.StationTypes.TryGetValue(s.TypeKey, out var t) ? t.Influence : 0f))
            .OrderBy(x => x.Rank).ThenByDescending(x => x.W).ThenBy(x => x.St.Id.Value)
            .Select(x => x.St).ToList();
    }

    /// <summary>
    /// Bucht den Regierungsunterhalt aller Spielerbehörden für <paramref name="dt"/> Ticks in Rangfolge (aus <see cref="UpkeepSystem"/>); liefert die
    /// behandelten Stationen (auch ruhende — sie zahlen nichts und bekommen kein Grundeinkommen), damit die normale Schleife sie überspringt.
    /// </summary>
    public static HashSet<StationId> ChargeAll(Universe u, int dt, SortedDictionary<int, (float paid, float unpaid)> official)
    {
        var handled = new HashSet<StationId>();
        if (!u.Actors.TryGetValue(u.PlayerActor, out var owner)) return handled;
        foreach (var st in Governments(u)) if (st.Dormant) handled.Add(st.Id);
        float reserve = 0f;
        foreach (var st in Order(u))
        {
            var t = u.StationTypes[st.TypeKey];
            handled.Add(st.Id);
            float perTick = PlayerUpkeep.GovernmentPerTick(u, st, t);
            float cost = perTick * dt;
            var cur = official.GetValueOrDefault(st.Owner.Value);
            if (cost > 0f)
            {
                bool ok = PlayerUpkeep.Charge(u, st, owner, cost, reserve);
                official[st.Owner.Value] = ok ? (cur.paid + cost, cur.unpaid) : (cur.paid, cur.unpaid + cost);
            }
            st.Credits += t.IncomePerTick * dt;
            Profitability.Transfer(st.Profit, t.IncomePerTick * dt);
            reserve += perTick * MathF.Max(0, u.Rules.PlayerUpkeepReserveTicks);
        }
        return handled;
    }

    /// <summary>Regierungsunterhalt je Spieltag der bezahlten, nicht ruhenden Behörden (Cr). Rein.</summary>
    public static float DailyUpkeep(Universe u)
    {
        float sum = 0f;
        foreach (var st in Governments(u))
            if (!st.Dormant && !(st.Unpaid && u.Rules.PlayerUpkeepFloorActive))
                sum += PlayerUpkeep.GovernmentPerTick(u, st, u.StationTypes[st.TypeKey]);
        return sum * MathF.Max(1, u.Rules.DayTicks);
    }

    /// <summary>
    /// Vorrat der Behörden: Untergrenze plus <see cref="EconomyRules.PlayerUpkeepReserveTicks"/> Unterhalt aller nicht ruhenden Behörden — darunter greifen
    /// gestaffelte Abgaben (Flottenabgabe) nicht. Rein.
    /// </summary>
    public static float Reserve(Universe u)
    {
        float sum = 0f;
        foreach (var st in Governments(u))
            if (!st.Dormant) sum += PlayerUpkeep.GovernmentPerTick(u, st, u.StationTypes[st.TypeKey]);
        return u.Rules.PlayerUpkeepFloor + sum * MathF.Max(0, u.Rules.PlayerUpkeepReserveTicks);
    }

    /// <summary>Wie viele Spieltage die Kasse den Regierungsunterhalt noch deckt (∞ ohne Unterhalt, 0 bei leerer Kasse). Rein.</summary>
    public static float ReachDays(Universe u)
    {
        float day = DailyUpkeep(u);
        if (day <= 0f) return float.PositiveInfinity;
        return MathF.Max(0f, u.Player.Treasury.Credits) / day;
    }

    /// <summary>Vorwarnung fällig (Regel an, Unterhalt da, Reichweite unter <see cref="EconomyRules.PlayerUpkeepWarnDays"/>)? Rein.</summary>
    public static bool Warn(Universe u) =>
        u.Rules.PlayerUpkeepWarnDays > 0f && Active(u) && DailyUpkeep(u) > 0f && ReachDays(u) < u.Rules.PlayerUpkeepWarnDays;

    /// <summary>Zeile für „Meine Fraktion“: Unterhalt je Tag, Reichweite, ruhende Behörden (leer ohne Behörden).</summary>
    public static string StatusLine(Universe u)
    {
        var gov = Governments(u);
        if (gov.Count == 0 || !Active(u)) return "";
        float day = DailyUpkeep(u);
        float reach = ReachDays(u);
        string r = float.IsInfinity(reach) ? "–" : reach >= 99f ? "über 99 Tage" : $"{Num(reach, 1)} Tage";
        int dorm = gov.Count(s => s.Dormant);
        string line = $"Regierungsunterhalt {day:0} Cr/Tag · Kasse reicht für {r}";
        if (dorm > 0) line += $" · {dorm} ruhend";
        if (Warn(u)) line += $" — WARNUNG: unter {Num(u.Rules.PlayerUpkeepWarnDays, 0)} Tagen, zuerst fallen die unwichtigsten Behörden aus.";
        return line;
    }

    /// <summary>Kurzer Alarmtext der Vorwarnung.</summary>
    public static string WarnText(Universe u) =>
        $"Kasse deckt den Regierungsunterhalt nur noch {Num(ReachDays(u), 1)} Tage ({DailyUpkeep(u):0} Cr/Tag) — dann fallen die unwichtigsten Behörden aus. Ruhen lassen unter „Meine Fraktion“.";

    /// <summary>Warum die Behörde nicht ruhen bzw. wieder anlaufen kann (null = sie kann). Rein.</summary>
    public static string? RestIssue(Universe u, StationId id, bool rest)
    {
        if (!Active(u)) return "Ruhen lassen ist abgeschaltet.";
        if (!u.Stations.TryGetValue(id, out var st) || st.IsBuildSite || st.Owner != u.PlayerActor
            || !u.StationTypes.TryGetValue(st.TypeKey, out var t) || t.Government == GovernmentKind.None) return "Keine eigene Behörde.";
        if (rest && st.Dormant) return "Ruht schon.";
        if (!rest && !st.Dormant) return "Läuft schon.";
        if (rest && Combat.Siege.Of(u, id) is not null) return "Wird belagert.";
        return null;
    }

    /// <summary>Behörde ruhen lassen oder wieder anschalten (ohne Prüfung; <see cref="RestIssue"/> vorher). Sektorkontrolle und Sperrdeckel sofort neu.</summary>
    public static void SetDormant(Universe u, StationId id, bool rest)
    {
        var st = u.Stations[id];
        st.Dormant = rest;
        st.Unpaid = false;   // zurück im Betrieb: die nächste Buchung entscheidet, ob es reicht
        u.Events.Publish(new PlayerGovernmentRested(id, st.Name, st.Sector, rest));
        if (!u.Sectors.TryGetValue(st.Sector, out var sec)) return;
        if (SectorControl.Update(u, sec)) u.TouchAccess();
        if (sec.Owner == u.PlayerFaction && sec.Access.EntryLevel > SectorControl.MaxLevel(u, sec)) FactionPolicySystem.SetLevel(u, sec, SectorControl.MaxLevel(u, sec));
    }

    static string Num(float v, int digits) => v.ToString(digits == 0 ? "0" : "0.0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
}

/// <summary>Welle C: gespeicherter Zustand (weiches Ansehen je KI-Fraktion: Ausgangswert vor dem ersten weichen Verlust und offener Verlust).</summary>
public sealed class WelleCState
{
    public SortedDictionary<int, (float Base, float Debt)> Soft { get; } = new();
    public static WelleCState Of(Universe u) => u.WelleC ??= new WelleCState();
}

/// <summary>
/// Welle C (3): weiche Ansehensverluste (Streitsektor, Fehde — <see cref="EconomyRules.PlayerStandingSoftReasons"/>) haben einen Boden
/// (<see cref="EconomyRules.PlayerStandingFloor"/>) und erholen sich, sobald kein Krieg, keine Fehde und kein Streitsektor mehr besteht, um
/// <see cref="EconomyRules.PlayerStandingRecoveryPerDay"/> je Tag — höchstens bis zum Ausgangswert vor dem ersten weichen Verlust (kein Drift darüber).
/// Krieg und Verbrechen gehen unverändert tiefer. Kein Zufall.
/// </summary>
public static class PlayerSoftStanding
{
    public const string RecoveryReason = "standing_recovery";

    public static bool Active(Universe u) => u.Rules.PlayerStandingFloor < 0f;

    public static bool IsSoft(Universe u, string reason) => Active(u) && u.Rules.PlayerStandingSoftReasons.Contains(reason);

    /// <summary>
    /// Begrenzt einen Verlust <paramref name="delta"/> (&lt; 0) aus <see cref="PlayerStanding.Change"/>: weich → nicht unter den Boden; merkt Ausgangswert und
    /// Verlust. Andere Gründe unverändert.
    /// </summary>
    public static float Limit(Universe u, FactionId f, float delta, string reason)
    {
        if (delta >= 0f || !IsSoft(u, reason)) return delta;
        float from = Relations.Effective(u, f, u.PlayerActor);
        float allowed = MathF.Max(delta, MathF.Min(0f, u.Rules.PlayerStandingFloor - from));
        if (allowed >= 0f) return 0f;
        var s = WelleCState.Of(u);
        (float Base, float Debt) rec = s.Soft.TryGetValue(f.Value, out var r) && Open(u, f, r) > 1e-5f ? r : (from, 0f);
        s.Soft[f.Value] = (rec.Base, rec.Debt - allowed);
        return allowed;
    }

    /// <summary>Noch offener weicher Verlust (höchstens der Abstand zum Ausgangswert). Rein.</summary>
    public static float Open(Universe u, FactionId f, (float Base, float Debt) rec) =>
        MathF.Max(0f, MathF.Min(rec.Debt, rec.Base - Relations.Effective(u, f, u.PlayerActor)));

    /// <summary>Hält gerade etwas die Erholung auf (Krieg, Fehde, Streitsektor)? Rein.</summary>
    public static string? Blocked(Universe u, FactionId f)
    {
        if (u.PlayerFaction is not { } me) return "keine Spielerfraktion";
        if (Wars.AtWar(u, f, me)) return "Krieg";
        if (Feuds.InFeud(u, f, me)) return "Fehde";
        if (FactionPolicySystem.ContestedBetween(u, me, f) > 0) return "Streitsektor";
        return null;
    }

    /// <summary>Erholung (aus <see cref="WelleCSystem"/>), feste Reihenfolge.</summary>
    public static void Recover(Universe u, int dt)
    {
        if (u.WelleC is not { } s || s.Soft.Count == 0) return;
        float rate = u.Rules.PlayerStandingRecoveryPerDay * dt / MathF.Max(1, u.Rules.DayTicks);
        foreach (var key in s.Soft.Keys.ToList())
        {
            var f = new FactionId(key);
            var rec = s.Soft[key];
            if (!u.IsNpcFaction(f)) { s.Soft.Remove(key); continue; }
            float open = Open(u, f, rec);
            if (open <= 1e-5f) { s.Soft.Remove(key); continue; }
            s.Soft[key] = (rec.Base, open);
            if (rate <= 0f || Blocked(u, f) is not null) continue;
            float step = MathF.Min(rate, open);
            PlayerStanding.Change(u, f, step, RecoveryReason);
            float left = open - step;
            if (left <= 1e-5f) s.Soft.Remove(key); else s.Soft[key] = (rec.Base, left);
        }
    }

    /// <summary>Anzeige (Fraktionsdetail): „Ansehen −0,30 durch Streit · erholt sich“ oder null.</summary>
    public static string? Text(Universe u, FactionId f)
    {
        if (u.WelleC is not { } s || !s.Soft.TryGetValue(f.Value, out var rec)) return null;
        float open = Open(u, f, rec);
        if (open <= 0.005f) return null;
        string why = Blocked(u, f) is { } b ? $"Erholung ruht ({b})" : $"erholt sich +{CrimeStanding.Num(u.Rules.PlayerStandingRecoveryPerDay)}/Tag";
        return $"Ansehen −{CrimeStanding.Num(open)} durch Streit · {why}";
    }
}

/// <summary>Welle C (4): Behörde im vom Spieler umkämpften Fremdsektor (Einfluss über <see cref="EconomyRules.ContestedShare"/>, kein Krieg mit dem Besitzer).</summary>
public static class PlayerSeatContest
{
    /// <summary>Darf der Spieler in diesem Sektor einer KI-Fraktion trotzdem gründen? Rein.</summary>
    public static bool Allows(Universe u, Sector sec) =>
        u.Rules.PlayerSeatContested && u.PlayerFaction is { } me && u.IsNpcFaction(sec.Owner)
        && sec.Influence.TryGetValue(me, out var share) && share > u.Rules.ContestedShare && !Wars.AtWar(u, sec.Owner, me);
}

/// <summary>Welle C: Erholung des weichen Ansehens (Politik-Modul, Intervall 100). Ohne Zustand nichts.</summary>
public sealed class WelleCSystem : ISimSystem
{
    public const int Interval = 100;
    public void Step(Universe u, int dt) => PlayerSoftStanding.Recover(u, dt);
}

/// <summary>Welle C: Speichern/Laden/Hash (additiv, v17 bleibt; gehasht nur mit Spielerpolitik und nur wenn vorhanden).</summary>
public sealed class WelleCSave
{
    public List<float[]>? Soft { get; set; }   // [Fraktion, Ausgangswert, Verlust]
}

public static class WelleCCodec
{
    public static void Save(Universe u, SaveGame s)
    {
        if (u.WelleC is not { } c) return;
        s.WelleC = new WelleCSave { Soft = c.Soft.Count > 0 ? c.Soft.Select(kv => new[] { kv.Key, kv.Value.Base, kv.Value.Debt }).ToList() : null };
    }

    public static void Load(SaveGame s, Universe u)
    {
        if (s.WelleC is not { } w) return;
        var c = new WelleCState();
        if (w.Soft is { } list) foreach (var d in list) if (d.Length >= 3) c.Soft[(int)d[0]] = (d[1], d[2]);
        u.WelleC = c;
    }

    public static void Mix(Universe u, Action<ulong> mix, Action<float> mixF)
    {
        if (u.WelleC is not { } c) return;
        mix(0xC0C0u);
        foreach (var (k, v) in c.Soft) { mix((ulong)k); mixF(v.Base); mixF(v.Debt); }
    }
}
