using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 6 (docs/POLITIK.md „Fehden, Vermittlung, Regierungskrise“): <b>Fehde</b> — Konflikt unterhalb des Kriegs zwischen zwei Fraktionen. Eine KI-Fehde
/// beginnt, wenn die schlechtere Beziehung beider unter <see cref="EconomyRules.FeudRelation"/> liegt und ein Sektor zwischen ihnen umkämpft ist; der Spieler
/// erklärt sie per Comms (nur mit Firmensitz, <see cref="PlayerSeatsOffices.FeudUnlocked"/>). Wirkung: Polizei- und Kriegsschiffe überfallen die Handelsschiffe
/// der Gegenseite in den umkämpften Sektoren und im eigenen Gebiet, Sperren gegeneinander (mindestens
/// <see cref="EconomyRules.FeudLockLevel"/>), keine Belagerung (eine Fehde ist kein Krieg — <see cref="Wars.Hostile(Universe, FactionId, FactionId)"/> bleibt falsch).
/// Ende nach <see cref="EconomyRules.FeudTicks"/>, wenn sich die Beziehung erholt, durch Vermittlung oder — nach den bestehenden Kriegsregeln — im Krieg.
/// Kein Zufall außer den zwei Würfen des Gefechtsbeitritts (<see cref="Engage.Join"/>).
/// </summary>
public static class Feuds
{
    public static (int, int) Key(FactionId a, FactionId b) => a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);

    public static Feud? Between(Universe u, FactionId a, FactionId b)
    {
        if (u.Feuds.Count == 0) return null;
        foreach (var f in u.Feuds) if ((f.A == a && f.B == b) || (f.A == b && f.B == a)) return f;
        return null;
    }

    public static bool InFeud(Universe u, FactionId a, FactionId b) => a != b && Between(u, a, b) is not null;

    /// <summary>Fehdegegner einer Fraktion (Reihenfolge der Fehden).</summary>
    public static IEnumerable<FactionId> Enemies(Universe u, FactionId f)
    {
        foreach (var fd in u.Feuds) if (fd.Involves(f)) yield return fd.Other(f);
    }

    /// <summary>Fehde zwischen dem Spieler und der Fraktion des Akteurs (für „kein Verbrechen gegen den Fehdegegner“)?</summary>
    public static bool PlayerFeudWith(Universe u, ActorId owner) =>
        u.Feuds.Count > 0 && u.PlayerFaction is { } me && u.FactionOf(owner) is { } f && InFeud(u, me, f);

    static float Worse(Universe u, FactionId a, FactionId b) => MathF.Min(Relations.Effective(u, a, b), Relations.Effective(u, b, a));

    static bool Cooling(Universe u, FactionId a, FactionId b) =>
        u.FeudEnded.TryGetValue(Key(a, b), out var t) && u.Tick - t < u.Rules.FeudCooldownTicks;

    /// <summary>Warum <paramref name="a"/> und <paramref name="b"/> jetzt keine KI-Fehde beginnen (null = sie würden). Rein.</summary>
    public static string? StartIssue(Universe u, FactionId a, FactionId b, bool playerContest = false)
    {
        var r = u.Rules;
        if (!r.FeudsActive) return "Fehden sind abgeschaltet.";
        if (a == b || !Wars.CanWage(u, a)) return "Keine Fehdeparteien.";
        bool player = u.IsPlayerFaction(b);
        // Welle B (2b): eine KI, deren Sektor der Spieler mit Stationen umkämpft, beginnt die Fehde auch ohne seinen Firmensitz
        if (player ? !r.PlayerFeuds || (!playerContest && !PlayerSeatsOffices.FeudUnlocked(u)) : !Wars.CanWage(u, b)) return "Keine Fehdeparteien.";
        if (Wars.AtWar(u, a, b)) return "Krieg.";
        if (InFeud(u, a, b)) return "Fehde läuft schon.";
        if (Alliances.Allied(u, a, b)) return "Bündnis.";
        if (Cooling(u, a, b)) return "Fehde zu frisch.";
        if (Enemies(u, a).Any() || (!player && Enemies(u, b).Any())) return "Schon in einer Fehde.";
        if (Worse(u, a, b) >= r.FeudRelation) return "Keine Feindschaft.";
        if (FactionPolicySystem.ContestedBetween(u, a, b) == 0) return "Kein umkämpfter Sektor.";
        return null;
    }

    /// <summary>Fehde beginnen (ohne Prüfung). Ereignis <see cref="FeudStarted"/>; Sperren folgen im nächsten Politik-Takt.</summary>
    public static Feud Start(Universe u, FactionId a, FactionId b, bool byPlayer = false)
    {
        if (Between(u, a, b) is { } running) return running;
        var (x, y) = Key(a, b);
        var fd = new Feud { A = new FactionId(x), B = new FactionId(y), Since = u.Tick, Until = u.Tick + Math.Max(1, u.Rules.FeudTicks), ByPlayer = byPlayer };
        u.Feuds.Add(fd);
        u.TouchAccess();
        u.Events.Publish(new FeudStarted(fd.A, fd.B, fd.Until, byPlayer, $"Fehde: {Name(u, a)} gegen {Name(u, b)}."));
        return fd;
    }

    public static void End(Universe u, Feud fd, string reason)
    {
        if (!u.Feuds.Remove(fd)) return;
        u.FeudEnded[Key(fd.A, fd.B)] = u.Tick;
        u.TouchAccess();
        string why = reason switch { "expired" => "abgelaufen", "relation" => "Beziehung erholt", "war" => "jetzt Krieg", "mediated" => "vermittelt", "ended" => "beendet", _ => "beendet" };
        u.Events.Publish(new FeudEnded(fd.A, fd.B, reason, $"Fehde {Name(u, fd.A)} – {Name(u, fd.B)} vorbei ({why})."));
    }

    public static string Name(Universe u, FactionId f) => u.IsPlayerFaction(f) ? "deine Fraktion" : u.Factions.TryGetValue(f, out var fa) ? fa.Name : "?";

    // ───────── Spieler ─────────

    /// <summary>Warum der Spieler <paramref name="f"/> jetzt keine Fehde erklären kann (null = er kann). Rein.</summary>
    public static string? PlayerDeclareIssue(Universe u, FactionId f)
    {
        var r = u.Rules;
        if (!r.PlayerFeuds) return "Fehden sind abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (!PlayerSeatsOffices.FeudUnlocked(u)) return "Fehden erst mit eigenem Firmensitz.";
        if (!Wars.CanWage(u, f)) return "Mit dieser Fraktion gibt es keine Fehde.";
        if (Wars.AtWar(u, me, f)) return "Schon im Krieg.";
        if (InFeud(u, me, f)) return "Fehde läuft schon.";
        if (Alliances.Allied(u, me, f)) return "Ihr seid verbündet.";
        if (Cooling(u, me, f)) return $"Letzte Fehde zu frisch (noch {Missions.MissionRules.Rest(r.FeudCooldownTicks - (u.Tick - u.FeudEnded[Key(me, f)]))}).";
        return null;
    }

    /// <summary>
    /// Der Spieler erklärt <paramref name="f"/> eine Fehde: Beziehung beider Richtungen höchstens knapp unter <see cref="EconomyRules.FeudRelation"/>, alle anderen
    /// KI-Fraktionen −<see cref="EconomyRules.FeudThirdPartyLoss"/> (leichter Ansehensverlust bei Dritten).
    /// </summary>
    public static Feud PlayerDeclare(Universe u, FactionId f)
    {
        var me = u.PlayerFaction ?? throw new InvalidOperationException("Keine Spielerfraktion");
        float target = u.Rules.FeudRelation - 0.05f, cur = Relations.Effective(u, f, me);
        if (cur > target) PlayerStanding.Change(u, f, target - cur, "feud");
        foreach (var other in u.NpcFactionIds())
            if (other != f && Wars.CanWage(u, other)) PlayerStanding.Change(u, other, -u.Rules.FeudThirdPartyLoss, "feud_declared");
        return Start(u, me, f, byPlayer: true);
    }

    public static string? PlayerEndIssue(Universe u, FactionId f)
    {
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        return InFeud(u, me, f) ? null : "Keine Fehde mit dieser Fraktion.";
    }

    // ───────── Überfälle ─────────

    /// <summary>
    /// Sektoren, in denen <paramref name="attacker"/> in der Fehde Händler von <paramref name="victim"/> überfällt: zuerst die zwischen beiden umkämpften, dann das
    /// eigene Gebiet des Angreifers (dort sind seine Streifen; ein umkämpfter Sektor kann während der Fehde wieder ruhig werden). Feste Reihenfolge.
    /// </summary>
    public static List<Sector> RaidSectors(Universe u, FactionId attacker, FactionId victim)
    {
        var list = new List<Sector>();
        float cs = u.Rules.ContestedShare;
        var sorted = u.Sectors.Values.OrderBy(s => s.Id.Value).ToList();
        foreach (var s in sorted)
            if (s.Contested && s.Influence.GetValueOrDefault(attacker) > cs && s.Influence.GetValueOrDefault(victim) > cs) list.Add(s);
        foreach (var s in sorted)
            if (s.Owner == attacker && !list.Contains(s)) list.Add(s);
        return list;
    }

    /// <summary>Handelsschiff im Dienst der Fraktion: Mitglied, liefert an eine ihrer Stationen — oder (Spieler) ein nicht aktives eigenes Schiff mit Fracht.</summary>
    public static bool ServesFaction(Universe u, Ship s, FactionId f)
    {
        if (u.IsPlayerFaction(f)) return s.IsPlayer && s.Id != u.PlayerShip && s.Owner == u.PlayerActor && u.Rules.FleetRaids;
        if (!s.IsEconomyAi) return false;
        if (u.FactionOf(s) == f) return true;
        return s.Order is TradeOrder o && u.Stations.TryGetValue(o.Sell, out var sell) && u.FactionOf(sell) == f;
    }

    /// <summary>Einsatzbereites Polizei-/Kriegsschiff der Fraktion im Sektor (Id-Reihenfolge, nicht im Gefecht, Hülle über der Rückzugsgrenze).</summary>
    public static Ship? AttackerIn(Universe u, FactionId f, SectorId sector)
    {
        foreach (var id in ShipsIn(u, sector))
        {
            if (!u.Ships.TryGetValue(id, out var s) || !(ThreatMap.IsPolice(s) || Wars.IsWarship(s)) || u.FactionOf(s) != f) continue;
            if (u.Fights.IsEngaged(s.Id) || !s.Has(Capability.Combat) || s.Hull < u.Rules.PoliceRetreatHull * s.MaxHull * 1.5f) continue;
            if (ThreatMap.Where(u, s).Sector != sector) continue;
            return s;
        }
        return null;
    }

    /// <summary>Wertvollstes erreichbares Opfer der Gegenseite im Sektor (nicht an einer Station, Fracht ≥ <see cref="EconomyRules.MinLootValue"/>).</summary>
    public static Ship? VictimIn(Universe u, FactionId victim, SectorId sector)
    {
        Ship? best = null;
        float bestValue = 0f;
        foreach (var id in ShipsIn(u, sector))
        {
            if (!u.Ships.TryGetValue(id, out var s) || ThreatMap.IsPolice(s) || ThreatMap.IsRaider(s) || u.Fights.IsEngaged(s.Id) || !ServesFaction(u, s, victim)) continue;
            var (where, pos) = ThreatMap.Where(u, s);
            if (where != sector || ThreatMap.NearStation(u, sector, pos)) continue;
            float value = ThreatMap.CargoValue(u, s);
            if (value < u.Rules.MinLootValue || value <= bestValue) continue;
            best = s; bestValue = value;
        }
        return best;
    }

    static List<ShipId> ShipsIn(Universe u, SectorId sector)
    {
        var ids = u.Tracker.ParkedIn(sector).Concat(u.Tracker.TransitIn(sector)).Distinct().ToList();
        ids.Sort(static (a, b) => a.Value.CompareTo(b.Value));
        return ids;
    }

    /// <summary>Fehdeüberfall als Gefecht (<see cref="EncounterCause.Feud"/>): Angreifer auf Seite 0 mit der Kaperer-Taktik (das Opfer wirft Fracht ab), Opfer auf Seite 1.</summary>
    public static Encounter StartRaid(Universe u, Ship attacker, Ship victim, Sector sector, FactionId fa, FactionId fv)
    {
        var r = u.Rules;
        var e = Engage.Begin(u, EncounterCause.Feud, sector.Id);
        e.Victim = victim.Id;
        e.Police = attacker.Id;
        e.RaiderOwner = attacker.Owner;
        var vPos = ThreatMap.Where(u, victim).Position;
        Engage.Join(u, e, attacker, 0, CombatTactics.Raider, 0f, e.Spatial ? CombatSpace.Around(vPos, ThreatMap.Where(u, attacker).Position, r.CombatStartDistance) : null);
        Engage.Join(u, e, victim, 1, CombatTactics.Trader, r.CombatStartDistance, e.Spatial ? vPos : null);
        foreach (var esc in FleetGuards.Escorts(u, victim, sector.Id))
            if (!u.Fights.IsEngaged(esc.Id)) Engage.Join(u, e, esc, 1, CombatTactics.Escort, r.CombatStartDistance, e.Spatial ? CombatSpace.Around(vPos, esc.Position, 8f) : null);
        string msg = $"Fehde in {sector.Name}: {Name(u, fa)} stellt ein Schiff von {Name(u, fv)}";
        u.Events.Publish(new EncounterStarted(e.Id, sector.Id, EncounterCause.Feud, attacker.Id, victim.Id, msg));
        u.Events.Publish(new FeudRaid(e.Id, sector.Id, fa, fv, attacker.Id, victim.Id, msg));
        Reinforcements.Step(u, e);
        return e;
    }

    /// <summary>Ein Überfall je Fehde und <see cref="EconomyRules.FeudRaidTicks"/>: erst A gegen B, dann B gegen A (feste Reihenfolge). true = einer begann.</summary>
    public static bool TryRaid(Universe u, Feud fd)
    {
        var r = u.Rules;
        long last = fd.LastRaidTick > 0 ? Math.Max(fd.Since, fd.LastRaidTick) : fd.Since;   // 0 = noch kein Überfall
        if (r.FeudRaidTicks <= 0 || u.Tick - last < r.FeudRaidTicks) return false;
        foreach (var (fa, fv) in new[] { (fd.A, fd.B), (fd.B, fd.A) })
        {
            if (u.IsPlayerFaction(fa)) continue;   // der Spieler überfällt selbst (kein Verbrechen gegen den Fehdegegner)
            foreach (var sec in RaidSectors(u, fa, fv))
            {
                if (AttackerIn(u, fa, sec.Id) is not { } att || VictimIn(u, fv, sec.Id) is not { } vic) continue;
                StartRaid(u, att, vic, sec, fa, fv);
                fd.LastRaidTick = Math.Max(1L, u.Tick);
                fd.Raids++;
                Sour(u, fa, fv, r.FeudRaidRelationLoss);
                return true;
            }
        }
        return false;
    }

    /// <summary>Beziehung beider Richtungen senken (Spieler über <see cref="PlayerStanding"/>).</summary>
    public static void Sour(Universe u, FactionId a, FactionId b, float loss)
    {
        if (loss <= 0f) return;
        if (u.IsPlayerFaction(a)) { PlayerStanding.Change(u, b, -loss, "feud_raid"); return; }
        if (u.IsPlayerFaction(b)) { PlayerStanding.Change(u, a, -loss, "feud_raid"); return; }
        Relations.Set(u, a, b, Math.Clamp(Relations.Effective(u, a, b) - loss, -1f, 1f));
        Relations.Set(u, b, a, Math.Clamp(Relations.Effective(u, b, a) - loss, -1f, 1f));
        u.TouchAccess();
    }

    /// <summary>Sperrgegner aus einer Fehde für einen Sektorbesitzer (erste Fehde in Reihenfolge; null = keine).</summary>
    public static FactionId? LockEnemy(Universe u, FactionId owner)
    {
        if (u.Feuds.Count == 0) return null;
        foreach (var fd in u.Feuds) if (fd.Involves(owner)) return fd.Other(owner);
        return null;
    }

    /// <summary>Anzeige: Fehden und Regierungskrise einer Fraktion (leer = keine).</summary>
    public static string FactionLine(Universe u, FactionId f)
    {
        var parts = new List<string>();
        foreach (var fd in u.Feuds)
            if (fd.Involves(f)) parts.Add($"Fehde mit {(u.IsPlayerFaction(fd.Other(f)) ? "dir" : Name(u, fd.Other(f)))} (noch {Missions.MissionRules.Rest(Math.Max(0, fd.Until - u.Tick))})");
        if (GovernmentCrisis.Active(u, f)) parts.Add($"Regierungskrise (noch {Missions.MissionRules.Rest(u.Crises[f.Value] - u.Tick)})");
        return string.Join(" · ", parts);
    }
}

/// <summary>
/// Ausbau Punkt 6: <b>Vermittlung</b>. Eine dritte KI-Fraktion mit guter Beziehung zu beiden Seiten (beide Richtungen ≥ <see cref="EconomyRules.MediatorRelation"/>,
/// mit keiner im Krieg oder in Fehde) vermittelt in Kriegen und Fehden zwischen KI-Fraktionen, sobald der Konflikt <see cref="EconomyRules.MediationMinTicks"/> läuft:
/// beide heben ihre Beziehung zur Vermittlerin um <see cref="EconomyRules.MediationRelationGain"/>; ein Krieg wird schneller müde
/// (<see cref="War.MediationBonus"/> += <see cref="EconomyRules.MediationWearinessBonus"/>, höchstens eine Vermittlung je <see cref="EconomyRules.MediationCooldownTicks"/>),
/// eine Fehde endet. Der Spieler vermittelt per Comms gegen eine Gebühr beider Seiten und gewinnt Ansehen bei beiden. Kein Zufall.
/// </summary>
public static class Mediation
{
    /// <summary>Ein Konflikt zwischen zwei KI-Fraktionen (Krieg oder Fehde, eines von beiden gesetzt).</summary>
    public readonly record struct Conflict(FactionId A, FactionId B, War? War, Feud? Feud)
    {
        public long Since => War?.Since ?? Feud!.Since;
        public string Kind => War is not null ? "war" : "feud";
    }

    /// <summary>Laufende Konflikte zwischen KI-Fraktionen (erst Kriege, dann Fehden; feste Reihenfolge).</summary>
    public static List<Conflict> Conflicts(Universe u)
    {
        var list = new List<Conflict>();
        foreach (var w in u.Wars) if (u.IsNpcFaction(w.A) && u.IsNpcFaction(w.B)) list.Add(new Conflict(w.A, w.B, w, null));
        foreach (var fd in u.Feuds) if (u.IsNpcFaction(fd.A) && u.IsNpcFaction(fd.B)) list.Add(new Conflict(fd.A, fd.B, null, fd));
        return list;
    }

    public static Conflict? Find(Universe u, FactionId a, FactionId b)
    {
        foreach (var c in Conflicts(u)) if ((c.A == a && c.B == b) || (c.A == b && c.B == a)) return c;
        return null;
    }

    /// <summary>Ist der Konflikt reif für eine Vermittlung (Mindestdauer, im Krieg die Abkühlzeit seit der letzten)? null = ja, sonst Grund.</summary>
    public static string? RipeIssue(Universe u, Conflict c)
    {
        var r = u.Rules;
        if (u.Tick - c.Since < r.MediationMinTicks) return $"Konflikt zu jung (noch {Missions.MissionRules.Rest(r.MediationMinTicks - (u.Tick - c.Since))}).";
        if (c.War is { LastMediationTick: > 0 } w && u.Tick - w.LastMediationTick < r.MediationCooldownTicks)
            return $"Gerade erst vermittelt (noch {Missions.MissionRules.Rest(r.MediationCooldownTicks - (u.Tick - w.LastMediationTick))}).";
        return null;
    }

    static bool GoodWith(Universe u, FactionId m, FactionId x, float min) =>
        Relations.Effective(u, m, x) >= min && Relations.Effective(u, x, m) >= min && !Wars.AtWar(u, m, x) && !Feuds.InFeud(u, m, x);

    /// <summary>Beste KI-Vermittlerin (höchste schlechtere Beziehung zu beiden Seiten, bei Gleichstand kleinere Id); null = keine.</summary>
    public static FactionId? MediatorFor(Universe u, Conflict c)
    {
        float min = u.Rules.MediatorRelation;
        FactionId? best = null;
        float bestScore = float.MinValue;
        foreach (var m in u.NpcFactionIds())
        {
            if (m == c.A || m == c.B || !Wars.CanWage(u, m) || !GoodWith(u, m, c.A, min) || !GoodWith(u, m, c.B, min)) continue;
            float score = MathF.Min(Relations.Effective(u, c.A, m), Relations.Effective(u, c.B, m));
            if (score > bestScore) { best = m; bestScore = score; }
        }
        return best;
    }

    /// <summary>Wirkung einer Vermittlung (KI oder Spieler): Beziehung zur Vermittlerin, Müdigkeit bzw. Ende der Fehde, Ereignis.</summary>
    public static void Apply(Universe u, Conflict c, FactionId mediator, float fee)
    {
        var r = u.Rules;
        foreach (var side in new[] { c.A, c.B })
        {
            if (u.IsPlayerFaction(mediator)) PlayerStanding.Change(u, side, r.MediationRelationGain, "mediation");
            else Relations.Set(u, side, mediator, Math.Clamp(Relations.Effective(u, side, mediator) + r.MediationRelationGain, -1f, 1f));
        }
        string who = Feuds.Name(u, mediator);
        string kind;
        if (c.War is { } w)
        {
            w.MediationBonus += r.MediationWearinessBonus;
            w.LastMediationTick = Math.Max(1L, u.Tick);
            kind = "Krieg";
        }
        else
        {
            Feuds.End(u, c.Feud!, "mediated");
            kind = "Fehde";
        }
        u.TouchAccess();
        string text = u.IsPlayerFaction(mediator)
            ? $"Du vermittelst zwischen {Feuds.Name(u, c.A)} und {Feuds.Name(u, c.B)} ({kind}) — {fee:0} Cr Gebühr."
            : $"{who} vermittelt zwischen {Feuds.Name(u, c.A)} und {Feuds.Name(u, c.B)} ({kind}).";
        u.Events.Publish(new MediationConcluded(mediator, c.A, c.B, c.Kind, fee, text));
    }

    /// <summary>KI-Vermittlungen eines Takts (feste Reihenfolge der Konflikte; je Konflikt höchstens eine).</summary>
    public static void Step(Universe u)
    {
        if (!u.Rules.Mediation) return;
        foreach (var c in Conflicts(u))
        {
            if (RipeIssue(u, c) is not null || MediatorFor(u, c) is not { } m) continue;
            Apply(u, c, m, 0f);
        }
    }

    /// <summary>Gebühr einer Konfliktpartei an den Spieler (höchstens die Hälfte ihres verfügbaren Geldes).</summary>
    public static float FeeOf(Universe u, FactionId f) =>
        u.Actors.TryGetValue(f, out var a) ? MathF.Max(0f, MathF.Min(u.Rules.MediationFee, 0.5f * FactionViability.Spendable(u, a))) : 0f;

    /// <summary>Warum der Spieler zwischen <paramref name="a"/> und <paramref name="b"/> jetzt nicht vermitteln kann (null = er kann). Rein.</summary>
    public static string? PlayerIssue(Universe u, FactionId a, FactionId b)
    {
        var r = u.Rules;
        if (!r.PlayerMediation) return "Vermittlung ist abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (Find(u, a, b) is not { } c) return "Kein Krieg und keine Fehde zwischen beiden.";
        foreach (var x in new[] { a, b })
        {
            if (Wars.AtWar(u, me, x) || Feuds.InFeud(u, me, x)) return $"Du bist selbst im Konflikt mit {Feuds.Name(u, x)}.";
            if (Relations.Effective(u, x, me) < r.PlayerMediatorRelation) return $"{Feuds.Name(u, x)} traut dir nicht (Beziehung unter {r.PlayerMediatorRelation:0.00}).";
        }
        return RipeIssue(u, c);
    }

    /// <summary>Der Spieler vermittelt (ohne Prüfung): Gebühr beider Seiten an ihn, Ansehen bei beiden, Wirkung wie bei der KI. Liefert die Gebühr.</summary>
    public static float PlayerMediate(Universe u, FactionId a, FactionId b)
    {
        var c = Find(u, a, b) ?? throw new InvalidOperationException("Kein Konflikt");
        float fee = 0f;
        foreach (var x in new[] { c.A, c.B })
        {
            float pay = FeeOf(u, x);
            if (pay <= 0f || !u.Actors.TryGetValue(x, out var actor)) continue;
            actor.Treasury.Credits -= pay;
            u.Player.Treasury.Credits += pay;
            u.Events.Publish(new TreasuryUpkeep(x, "mediation", pay, 0f));
            fee += pay;
        }
        Apply(u, c, u.PlayerFaction!.Value, fee);
        return fee;
    }
}

/// <summary>
/// Ausbau Punkt 6: <b>Regierungskrise</b>. Verliert eine KI-Fraktion ihr Hauptquartier (zerstört oder kapituliert; der Not-Sitz aus <see cref="SeatRelocation"/>
/// entsteht trotzdem), ist sie <see cref="EconomyRules.CrisisTicks"/> gelähmt: nur <see cref="EconomyRules.CrisisTaxFactor"/> der Steuern erreicht die Kasse, keine
/// freiwilligen Polizeikäufe (der Ersatz der Mindestpolizei mit Vorrang bleibt — keine Todesspirale), Sperren höchstens <see cref="EconomyRules.CrisisMaxLockLevel"/>,
/// ihre Sektoren wechseln schon mit <see cref="EconomyRules.CrisisOwnerSwitchMargin"/> Vorsprung den Besitzer.
/// </summary>
public static class GovernmentCrisis
{
    public static bool Active(Universe u, FactionId f) => u.Crises.Count > 0 && u.Crises.TryGetValue(f.Value, out var until) && until > u.Tick;

    public static void Start(Universe u, FactionId f)
    {
        if (!u.Rules.GovernmentCrisis || !u.IsNpcFaction(f) || !u.Actors.TryGetValue(f, out var a) || a.Kind == ActorKind.Outlaw) return;
        long until = u.Tick + Math.Max(1, u.Rules.CrisisTicks);
        u.Crises[f.Value] = Math.Max(until, u.Crises.GetValueOrDefault(f.Value));
        u.TouchAccess();
        u.Events.Publish(new GovernmentCrisisChanged(f, true, u.Crises[f.Value], $"Regierungskrise bei {Feuds.Name(u, f)}: Hauptquartier verloren."));
    }

    /// <summary>Beendet abgelaufene Krisen (Ereignis je Fraktion, Id-Reihenfolge).</summary>
    public static void Step(Universe u)
    {
        if (u.Crises.Count == 0) return;
        foreach (var (f, until) in u.Crises.OrderBy(kv => kv.Key).ToList())
        {
            if (until > u.Tick) continue;
            u.Crises.Remove(f);
            u.TouchAccess();
            u.Events.Publish(new GovernmentCrisisChanged(new FactionId(f), false, until, $"Regierungskrise bei {Feuds.Name(u, new FactionId(f))} vorbei."));
        }
    }

    public static float TaxFactor(Universe u, FactionId f) => Active(u, f) ? Math.Clamp(u.Rules.CrisisTaxFactor, 0f, 1f) : 1f;
}

/// <summary>
/// Ausbau Punkt 6: Fehden, Vermittlung und Regierungskrise (Politik-Modul nach Krieg und Bündnissen, Intervall 100). Beendet Fehden (Krieg, Ablauf, erholte
/// Beziehung), beginnt höchstens eine neue KI-Fehde je Takt (Paare in Id-Reihenfolge, gegen den Spieler danach), Überfälle, KI-Vermittlung, Ende der Krisen.
/// </summary>
public sealed class FeudSystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        GovernmentCrisis.Step(u);
        if (u.Feuds.Count > 0)
            foreach (var fd in u.Feuds.ToList())
            {
                if (Wars.AtWar(u, fd.A, fd.B)) { Feuds.End(u, fd, "war"); continue; }
                if (u.Tick >= fd.Until) { Feuds.End(u, fd, "expired"); continue; }
                if (!u.Factions.ContainsKey(fd.A) || !u.Factions.ContainsKey(fd.B)) { Feuds.End(u, fd, "gone"); continue; }
                bool npc = u.IsNpcFaction(fd.A) && u.IsNpcFaction(fd.B);
                if (npc && MathF.Min(Relations.Effective(u, fd.A, fd.B), Relations.Effective(u, fd.B, fd.A)) > r.FeudEndRelation) { Feuds.End(u, fd, "relation"); continue; }
                Feuds.TryRaid(u, fd);
            }
        if (r.FeudsActive) StartOne(u);
        Mediation.Step(u);
    }

    static void StartOne(Universe u)
    {
        var factions = u.NpcFactionIds();
        foreach (var a in factions)
            foreach (var b in factions)
            {
                if (a.Value >= b.Value || Feuds.StartIssue(u, a, b) is not null) continue;
                Feuds.Start(u, a, b);
                return;
            }
        if (u.Rules.PlayerFeuds && u.PlayerFaction is { } me)
            foreach (var a in factions)
            {
                if (Feuds.StartIssue(u, a, me) is not null) continue;
                Feuds.Start(u, a, me);
                return;
            }
    }
}
