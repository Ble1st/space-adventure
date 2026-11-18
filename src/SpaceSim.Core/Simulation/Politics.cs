using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Schutz- und Rechte-Eigenschaften einer Station (Phase 10, STATIONEN.md „Essential aufgedröselt“). <see cref="Station.BankruptcyProtected"/>
/// (Phase 8) ist gespeichert, <c>Critical</c> abgeleitet (<see cref="StationCriticality"/>); hier die übrigen: <see cref="IsGovernment"/> aus dem Typ,
/// <see cref="IsCapturable"/> aus Regel und Typ (Regierungsstationen nie), <see cref="IsDestructible"/> aus dem Typ, <see cref="Station.Immortal"/>
/// nur aus der Weltdatei. Eroberung und Zerstörung gibt es noch nicht (Kampf zurückgestellt); die Eigenschaften sind die Prüfstellen dafür.
/// </summary>
public static class StationProtection
{
    /// <summary>Regierungsform der Station (aus dem Typ; unbekannter Typ: keine).</summary>
    public static GovernmentKind KindOf(Universe u, Station st) =>
        !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Government : GovernmentKind.None;

    /// <summary>Hauptquartier, Firmensitz, Sektor-HQ oder Büro: eigene Verlustregeln, nie eroberbar (die Polizeiwache zählt nicht dazu).</summary>
    public static bool IsGovernment(Universe u, Station st) =>
        KindOf(u, st) is GovernmentKind.Headquarters or GovernmentKind.CompanySeat or GovernmentKind.SectorHq or GovernmentKind.Office;

    /// <summary>Regierungsstation oder Polizeiwache (zahlt Unterhalt aus der Kasse, zählt nicht gegen die Bau-Obergrenzen).</summary>
    public static bool IsOfficial(Universe u, Station st) => KindOf(u, st) != GovernmentKind.None;

    /// <summary>Darf die Station erobert werden? Nur wenn die Regel es erlaubt (derzeit aus), der Typ es zulässt und sie weder Regierung noch unsterblich ist.</summary>
    public static bool IsCapturable(Universe u, Station st) =>
        u.Rules.AllowStationCapture && !st.Immortal && !IsGovernment(u, st)
        && (!u.StationTypes.TryGetValue(st.TypeKey, out var t) || t.Capturable);

    /// <summary>Darf die Station zerstört werden? Typ erlaubt es und sie ist nicht unsterblich (erst mit Kampf relevant).</summary>
    public static bool IsDestructible(Universe u, Station st) =>
        !st.Immortal && (!u.StationTypes.TryGetValue(st.TypeKey, out var t) || t.Destructible);

    /// <summary>Phase 11: Piratenversteck (Typ mit <see cref="StationType.Hideout"/>)?</summary>
    public static bool IsHideout(Universe u, Station st) => !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Hideout;

    /// <summary>Zählt die Station gegen die Bau-Obergrenzen? Nicht: Regierungsstationen, Wachen und (Phase 11) Piratenverstecke.</summary>
    public static bool IsEconomic(Universe u, Station st) => !IsOfficial(u, st) && !IsHideout(u, st);

    /// <summary>Stationen, die gegen die Bau-Obergrenzen zählen (ohne Regierungsstationen, Wachen und Verstecke).</summary>
    public static int CountEconomic(Universe u) => u.Stations.Values.Count(s => IsEconomic(u, s));

    /// <summary>Stationen eines Sektors, die gegen die Obergrenze je Sektor zählen.</summary>
    public static int CountEconomic(Universe u, Sector sector) => sector.Stations.Count(id => u.Stations.TryGetValue(id, out var s) && IsEconomic(u, s));
}

/// <summary>Einfluss in einem Sektor: Anteile je Fraktion (0..1) und Anteil ohne Fraktion (freie/unabhängige Besitzer).</summary>
public sealed record InfluenceResult(Dictionary<FactionId, float> Shares, float Unaffiliated, float Total);

/// <summary>
/// Sektorkontrolle (Phase 10, POLITIK.md Abschnitt 8): Einfluss aus tatsächlicher Präsenz — Stationen nach Typ gewichtet
/// (<see cref="StationType.Influence"/>: Hauptquartier, Sektor-HQ, Wache mehr; Baustellen nichts), gezählt für die Fraktion ihres Besitzers.
/// Daraus folgen der abgeleitete <see cref="Sector.Owner"/> (Wechsel nur mit Vorsprung <see cref="EconomyRules.OwnerSwitchMargin"/>; ohne Stationen
/// bleibt der Besitzer), die Kontrollstufe des Besitzers und „umkämpft“. Die Patrouillenabdeckung kommt seit Phase 11 aus der Lagekarte
/// (<see cref="ThreatMap.HasCoverage"/>).
/// </summary>
public static class SectorControl
{
    /// <summary>Einflussanteile eines Sektors aus seinen Stationen (feste Reihenfolge).</summary>
    public static InfluenceResult Compute(Universe u, Sector sector)
    {
        var weights = new SortedDictionary<int, float>();
        float none = 0f, total = 0f;
        foreach (var id in sector.Stations.OrderBy(i => i.Value))
        {
            if (!u.Stations.TryGetValue(id, out var st) || st.IsBuildSite) continue;
            float w = u.StationTypes.TryGetValue(st.TypeKey, out var t) ? MathF.Max(0f, t.Influence) : 1f;
            if (w <= 0f) continue;
            total += w;
            if (u.FactionOf(st) is { } f && u.Factions.ContainsKey(f)) weights[f.Value] = weights.GetValueOrDefault(f.Value) + w;
            else none += w;
        }
        var shares = new Dictionary<FactionId, float>();
        if (total > 0f) foreach (var (f, w) in weights) shares[new FactionId(f)] = w / total;
        return new InfluenceResult(shares, total > 0f ? none / total : 0f, total);
    }

    /// <summary>Hat die Fraktion im Sektor eine Station dieser Regierungsformen?</summary>
    static bool Has(Universe u, Sector sector, FactionId f, params GovernmentKind[] kinds) =>
        sector.Stations.Any(id => u.Stations.TryGetValue(id, out var st) && u.FactionOf(st) == f && kinds.Contains(StationProtection.KindOf(u, st)));

    /// <summary>Sektorbehörde vor Ort (Hauptquartier oder Sektor-HQ der Fraktion).</summary>
    public static bool HasAuthority(Universe u, Sector sector, FactionId f) => Has(u, sector, f, GovernmentKind.Headquarters, GovernmentKind.SectorHq);

    /// <summary>
    /// Patrouillenabdeckung. Phase 11: aus der Lagekarte (<see cref="ThreatMap.HasCoverage"/>: Grundschutz der Wache/des Hauptquartiers plus echte
    /// Streifen des Besitzers) statt der Phase-10-Annahme „Hauptquartier oder Polizeiwache im Sektor“.
    /// </summary>
    public static bool HasPatrol(Universe u, Sector sector, FactionId f) => ThreatMap.HasCoverage(u, sector, f);

    /// <summary>Kontrollstufe einer Fraktion im Sektor nach POLITIK.md Abschnitt 8.</summary>
    public static ControlStage StageOf(Universe u, Sector sector, InfluenceResult inf, FactionId f)
    {
        float share = inf.Shares.GetValueOrDefault(f);
        if (share <= 0f) return ControlStage.None;
        float best = inf.Shares.Count == 0 ? 0f : inf.Shares.Values.Max();
        if (share < best - 1e-6f) return ControlStage.Presence;
        if (share <= u.Rules.DominanceShare) return ControlStage.Influence;
        bool authority = HasAuthority(u, sector, f);
        if (!authority && !HasPatrol(u, sector, f)) return ControlStage.Influence;
        return authority && HasPatrol(u, sector, f) ? ControlStage.Control : ControlStage.Dominance;
    }

    /// <summary>Umkämpft: mindestens zwei Fraktionen über <see cref="EconomyRules.ContestedShare"/>.</summary>
    public static bool IsContested(Universe u, InfluenceResult inf) => inf.Shares.Values.Count(s => s > u.Rules.ContestedShare) >= 2;

    /// <summary>
    /// Berechnet Einfluss, Besitzer, Kontrollstufe und „umkämpft“ eines Sektors neu, veröffentlicht Änderungen und hält
    /// <see cref="Faction.Territory"/> aktuell. Liefert, ob sich etwas am Zugang ändern kann (Besitzer, Stufe, umkämpft).
    /// </summary>
    public static bool Update(Universe u, Sector sector)
    {
        var inf = Compute(u, sector);
        bool changed = false;
        sector.Influence = new Dictionary<FactionId, float>(inf.Shares);
        if (inf.Shares.Count > 0)
        {
            var leader = inf.Shares.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Value).First();
            float cur = inf.Shares.GetValueOrDefault(sector.Owner);
            if (leader.Key != sector.Owner && (cur <= 0f || leader.Value >= cur + u.Rules.OwnerSwitchMargin))
            {
                var old = sector.Owner;
                sector.Owner = leader.Key;
                if (u.Factions.TryGetValue(old, out var fo)) fo.Territory.Remove(sector.Id);
                if (u.Factions.TryGetValue(leader.Key, out var fn)) fn.Territory.Add(sector.Id);
                u.Events.Publish(new SectorOwnerChanged(sector.Id, old, leader.Key));
                changed = true;
            }
        }
        var stage = StageOf(u, sector, inf, sector.Owner);
        bool contested = IsContested(u, inf);
        if (stage != sector.Control || contested != sector.Contested)
        {
            u.Events.Publish(new SectorControlChanged(sector.Id, sector.Control, stage, contested));
            sector.Control = stage;
            sector.Contested = contested;
            changed = true;
        }
        return changed;
    }

    /// <summary>Höchste Sperrstufe, die der Besitzer im Sektor durchsetzen darf (nach Kontrollstufe; umkämpft höchstens Stufe 1).</summary>
    public static int MaxLevel(Universe u, Sector sector)
    {
        var list = u.Rules.StageMaxLevel;
        int max = list.Count > (int)sector.Control ? list[(int)sector.Control] : 0;
        if (sector.Contested) max = Math.Min(max, u.Rules.ContestedMaxLevel);
        return Math.Clamp(max, 0, 5);
    }
}

/// <summary>Berechnet die Sektorkontrolle aller Sektoren (Phase 10). Läuft selten (Intervall), Änderungen lösen eine Zugangsprüfung aus.</summary>
public sealed class SectorControlSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        bool changed = false;
        foreach (var sector in u.Sectors.Values.OrderBy(s => s.Id.Value)) changed |= SectorControl.Update(u, sector);
        if (changed) u.TouchAccess();
    }
}

/// <summary>
/// Fraktionspolitik (Phase 10, POLITIK.md Abschnitte 2–3, SCHIFFSSLOTS.md Abschnitt 5 „Auslöser“): Beziehungen bewegen sich aus Daten und Ereignissen
/// (<see cref="StepRelations"/>), daraus folgen die Sperrstufen der Sektoren mit Mindestdauer, Hysterese, Ablauf und Abkühlzeit; die Kasse der
/// Sektorfraktion zahlt die Durchsetzung. KI-Fraktionen und (später) die Spielerfraktion nutzen dieselben Regeln.
/// </summary>
public sealed class FactionPolicySystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        var rules = u.Rules;
        if (rules.RelationStepTicks > 0 && u.Tick % rules.RelationStepTicks < dt) StepRelations(u);
        bool changed = false;
        foreach (var sector in u.Sectors.Values.OrderBy(s => s.Id.Value)) changed |= Decide(u, sector);
        PayEnforcement(u, dt);
        if (changed)
        {
            GateAccess.SyncRestricted(u);
            u.TouchAccess();
            AccessEnforcement.Revalidate(u);
        }
    }

    /// <summary>
    /// Schlechteste Beziehung der Fraktion zu einer anderen Fraktion und zu wem (feste Reihenfolge; ohne andere Fraktionen 1). Runde 38 (Entscheidung des
    /// Nutzers: Sperren im Krieg mit dem Spieler wie bei KI-Kriegen, für alle): die Spielerfraktion zählt mit. Sie steht in der Id-Reihenfolge hinten, bei
    /// Gleichstand gewinnt die KI-Fraktion; ihre Startbeziehungen (≥ 0,1) liegen über jeder Sperrschwelle, ohne Spielerhandlung ändert sich also nichts.
    /// </summary>
    public static (float Worst, FactionId? Against) WorstRelation(Universe u, FactionId f)
    {
        float worst = 1f;
        FactionId? against = null;
        foreach (var other in u.Factions.Keys.OrderBy(k => k.Value))
        {
            if (other == f) continue;
            float r = Relations.Effective(u, f, other);
            if (r < worst) { worst = r; against = other; }
        }
        return (worst, against);
    }

    /// <summary>Stufe zu einer Beziehung: Anzahl der Schwellen (verschoben nach Aggression und <paramref name="offset"/>), unter denen sie liegt.</summary>
    public static int LevelFor(EconomyRules r, float relation, float aggression, float offset = 0f)
    {
        float shift = (aggression - 0.5f) * r.LockAggressionShift + offset;
        int level = 0;
        for (int i = 0; i < r.LockThresholds.Count && i < 5; i++) if (relation < r.LockThresholds[i] + shift) level = i + 1;
        return level;
    }

    /// <summary>
    /// Gewünschte Sperrstufe eines Sektors für seinen Besitzer (rein, ohne Zeitregeln): aus der schlechtesten Beziehung, mit Hysterese beim Senken,
    /// gedeckelt durch die Kontrollstufe. Ohne Fraktion als Besitzer 0.
    /// </summary>
    public static (int Level, FactionId? Against, bool Forced) Desired(Universe u, Sector sector)
    {
        int cur = sector.Access.EntryLevel;
        if (!u.Factions.ContainsKey(sector.Owner)) return (0, null, cur > 0);
        var (worst, against) = WorstRelation(u, sector.Owner);
        float aggression = ActorRules.PersonalityOf(u, sector.Owner).Aggression;
        int raw = LevelFor(u.Rules, worst, aggression);
        int target = raw >= cur ? raw : Math.Min(cur, LevelFor(u.Rules, worst, aggression, u.Rules.LockHysteresis));
        int cap = SectorControl.MaxLevel(u, sector);
        bool forced = target > cap && cur > cap;
        return (Math.Min(target, cap), target > 0 ? against : null, forced);
    }

    /// <summary>Entscheidet die Stufe eines Sektors (Ablauf, Abkühlzeit, Mindestdauer, Hysterese); true bei Änderung.</summary>
    static bool Decide(Universe u, Sector sector)
    {
        var acc = sector.Access;
        var rules = u.Rules;
        int cur = acc.EntryLevel;
        if (cur > 0 && u.Tick >= acc.ExpiresTick)
        {
            Apply(u, sector, 0, acc.Against, "expired");
            acc.CooldownUntil = u.Tick + rules.LockCooldownTicks;
            return true;
        }
        // Runde 45: Sektoren der Spielerfraktion entscheidet der Spieler (PlayerLocks) — hier nur Ablauf (oben) und die Deckelung durch die Kontrollstufe
        if (u.IsPlayerFaction(sector.Owner))
        {
            int capP = SectorControl.MaxLevel(u, sector);
            if (cur <= capP) return false;
            Apply(u, sector, capP, acc.Against, "stage");
            return true;
        }
        var (target, against, forced) = Desired(u, sector);
        if (target == cur) return false;
        bool minOver = u.Tick - acc.SinceTick >= rules.LockMinTicks;
        if (target > cur)
        {
            if (cur == 0 ? u.Tick < acc.CooldownUntil : !minOver) return false;
        }
        else if (!forced && !minOver) return false;
        Apply(u, sector, target, against, forced ? "stage" : "relation");
        return true;
    }

    static void Apply(Universe u, Sector sector, int level, FactionId? against, string reason)
    {
        var acc = sector.Access;
        int fromEntry = acc.EntryLevel, fromExit = acc.ExitLevel;
        bool wasOpen = acc.IsOpen;
        acc.EntryLevel = level;
        acc.ExitLevel = level >= 5 ? 5 : 0;
        acc.Against = level > 0 ? against : null;
        acc.SinceTick = u.Tick;
        if (level == 0) { acc.ExpiresTick = -1; acc.Enforced = true; }
        else if (wasOpen) acc.ExpiresTick = u.Tick + u.Rules.LockMaxTicks;   // Ablauf zählt ab Beginn der Sperre, nicht ab jeder Stufe
        u.Events.Publish(new SectorAccessChanged(sector.Id, sector.Owner, fromEntry, acc.EntryLevel, fromExit, acc.ExitLevel, acc.Against, reason));
    }

    /// <summary>
    /// Setzt die Sperrstufe eines Sektors von Hand (Spielerfraktion, Tests, Szenarien) nach denselben Regeln wie die KI: höchstens die Kontrollstufe
    /// erlaubt es; Mindestdauer und Ablauf gelten ab jetzt. Liefert die gesetzte Stufe.
    /// </summary>
    public static int SetLevel(Universe u, Sector sector, int level, FactionId? against = null)
    {
        level = Math.Clamp(Math.Min(level, SectorControl.MaxLevel(u, sector)), 0, 5);
        if (level == sector.Access.EntryLevel) return level;
        if (against is null && level > 0 && u.Factions.ContainsKey(sector.Owner)) against = WorstRelation(u, sector.Owner).Against;
        Apply(u, sector, level, against, "manual");
        GateAccess.SyncRestricted(u);
        u.TouchAccess();
        AccessEnforcement.Revalidate(u);
        return level;
    }

    /// <summary>Durchsetzungskosten je Sperrstufe aus der Kasse der Sektorfraktion; reicht sie nicht, gilt die Sperre nur auf dem Papier.</summary>
    static void PayEnforcement(Universe u, int dt)
    {
        bool flip = false;
        foreach (var sector in u.Sectors.Values.OrderBy(s => s.Id.Value))
        {
            var acc = sector.Access;
            if (acc.IsOpen) continue;
            float cost = u.Rules.LockUpkeepPerLevel * acc.EntryLevel * dt;
            bool paid = false;
            // Phase 22: eine Sperre ist freiwillig — bezahlt nur aus dem Teil der Kasse über der Betriebsreserve (sonst Sperre auf dem Papier)
            if (u.Actors.TryGetValue(sector.Owner, out var actor) && FactionViability.Spendable(u, actor) >= cost)
            {
                actor.Treasury.Credits -= cost;
                paid = true;
            }
            if (cost > 0f) u.Events.Publish(new TreasuryUpkeep(sector.Owner, "lock", paid ? cost : 0f, paid ? 0f : cost));
            if (paid != acc.Enforced) { acc.Enforced = paid; flip = true; }
        }
        if (flip) { GateAccess.SyncRestricted(u); u.TouchAccess(); AccessEnforcement.Revalidate(u); }
    }

    /// <summary>
    /// Beziehungsschritt (POLITIK.md Abschnitt 3 und „Beziehungen sparsam speichern“): Jede Fraktion bewegt ihre Hauptbeziehung zu jeder anderen um
    /// <see cref="EconomyRules.RelationDriftRate"/> zum Ruhewert (Kooperation hebt, Aggression senkt, gemeinsamer Handel hebt); jeder umkämpfte Sektor
    /// und jede Sperre der anderen Seite gegen sie senkt sie (Sperre gewichtet mit dem betroffenen Handel). Nur Fraktionspaare, sparsam gespeichert.
    /// </summary>
    public static void StepRelations(Universe u)
    {
        var rules = u.Rules;
        var factions = u.NpcFactionIds();   // Runde 38: Spielerbeziehungen bewegen sich nie durch Drift
        if (factions.Count < 2) return;
        var share = TradeShares(u);
        bool any = false;
        foreach (var a in factions)
        {
            var p = ActorRules.PersonalityOf(u, a);
            foreach (var b in factions)
            {
                if (a == b) continue;
                float cur = Relations.Effective(u, a, b);
                float s = share.GetValueOrDefault(Key(a, b));
                float rest = rules.RelationRestBase + rules.RelationCooperationWeight * (p.Cooperation - 0.5f)
                             - rules.RelationAggressionWeight * (p.Aggression - 0.5f) + rules.RelationTradeWeight * s
                             + (u.Alliances.Count > 0 && Alliances.Allied(u, a, b) ? rules.AllianceRestBonus : 0f);   // Runde 45: Bündnistreue
                float delta = rules.RelationDriftRate * (Math.Clamp(rest, -1f, 1f) - cur)
                              - rules.ContestOpinionLoss * ContestedBetween(u, a, b)
                              - rules.LockOpinionLoss * LocksAgainst(u, b, a) * (0.5f + s);
                if (MathF.Abs(delta) < 1e-5f) continue;
                Relations.Set(u, a, b, cur + delta);
                any = true;
            }
        }
        if (any) u.TouchAccess();
        // Runde 45: Sperren der Spielerfraktion verärgern KI-Fraktionen nach derselben Formel wie KI-Sperren (Groll × Stufen × (0,5 + Handelsanteil 0));
        // die Rückdrift entfällt — Spielerbeziehungen driften nie (Runde 38)
        if (u.PlayerFaction is { } me && u.Sectors.Values.Any(sec => sec.Owner == me && !sec.Access.IsOpen))
            foreach (var a in factions)
            {
                int locks = LocksAgainst(u, me, a);
                if (locks > 0) PlayerStanding.Change(u, a, -rules.LockOpinionLoss * locks * 0.5f, "player_lock");
            }
    }

    static (int, int) Key(FactionId a, FactionId b) => a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);

    /// <summary>
    /// Anteil der laufenden Handelsaufträge zwischen Stationen zweier Fraktionen (in beide Richtungen) an allen Handelsaufträgen zwischen Stationen
    /// mit Fraktion — das Maß der wirtschaftlichen Verflechtung („wer wenig handelt, ärgert sich kaum“).
    /// </summary>
    public static Dictionary<(int, int), float> TradeShares(Universe u)
    {
        var counts = new Dictionary<(int, int), int>();
        int total = 0;
        foreach (var ship in u.Ships.Values)
        {
            if (ship.Order is not TradeOrder o || !u.Stations.TryGetValue(o.Buy, out var buy) || !u.Stations.TryGetValue(o.Sell, out var sell)) continue;
            if (u.FactionOf(buy) is not { } fa || u.FactionOf(sell) is not { } fb) continue;
            total++;
            if (fa == fb) continue;
            var k = Key(fa, fb);
            counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        return counts.ToDictionary(kv => kv.Key, kv => total > 0 ? (float)kv.Value / total : 0f);
    }

    /// <summary>Sektoren, die zwischen beiden Fraktionen umkämpft sind (beide über dem Schwellenanteil).</summary>
    public static int ContestedBetween(Universe u, FactionId a, FactionId b) =>
        u.Sectors.Values.Count(s => s.Contested && s.Influence.GetValueOrDefault(a) > u.Rules.ContestedShare && s.Influence.GetValueOrDefault(b) > u.Rules.ContestedShare);

    /// <summary>Summe der durchgesetzten Sperrstufen in Sektoren von <paramref name="owner"/>, die <paramref name="target"/> treffen (kein Bündnis).</summary>
    public static int LocksAgainst(Universe u, FactionId owner, FactionId target)
    {
        if (Relations.Effective(u, owner, target) >= u.Rules.AllyRelation || Alliances.Allied(u, owner, target)) return 0;   // Runde 45: Bündnis
        int sum = 0;
        foreach (var s in u.Sectors.Values)
        {
            if (s.Owner != owner || !s.Access.Enforced) continue;
            // Runde 38b (Entscheidung des Nutzers „nur Spieler-Sperren ärgern Dritte nicht“): eine Sperre gegen die Spielerfraktion gilt weiter für alle,
            // löst aber bei anderen KI-Fraktionen keinen Groll aus — kein KI-gegen-KI-Folgekrieg aus einem Spielerkrieg
            if (s.Access.Against is { } ag && ag != target && u.IsPlayerFaction(ag)) continue;
            sum += s.Access.EntryLevel;
        }
        return sum;
    }
}
