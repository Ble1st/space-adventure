using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Runde 45 (docs/POLITIK.md „Runde 45“): Bündnisse zwischen Fraktionen, auch mit der Spielerfraktion. Ein Bündnis ist ein <b>defensiver Beistandspakt</b>:
/// wird ein Mitglied angegriffen, erklären seine KI-Verbündeten dem Angreifer den Krieg (Tiefe 1 — der Beitritt ruft niemanden weiter); der Spieler wird
/// per Comms gerufen und muss binnen <see cref="EconomyRules.AllianceCallTicks"/> beitreten, sonst endet das Bündnis. Verbündete gelten an den Kontrollpunkten,
/// bei der Zuständigkeit der Polizei und beim Sperrgroll als Verbündete (<see cref="TreatAsAlly"/>, zusätzlich zur Beziehungsschwelle
/// <see cref="EconomyRules.AllyRelation"/>). Gründung ab <see cref="EconomyRules.AllianceMinRelation"/> (beide Richtungen, KI zusätzlich Kooperation),
/// Auflösung unter <see cref="EconomyRules.AllianceBreakRelation"/> oder bei Verrat; danach Abkühlzeit. Rein bis auf die benannten Schreibstellen, kein Zufall.
/// </summary>
public static class Alliances
{
    public static bool Active(Universe u) => u.Rules.AlliancesActive;

    static (int, int) Key(FactionId a, FactionId b) => a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);

    public static Alliance? Between(Universe u, FactionId a, FactionId b)
    {
        if (a == b) return null;
        foreach (var al in u.Alliances) if ((al.A == a && al.B == b) || (al.A == b && al.B == a)) return al;
        return null;
    }

    /// <summary>Sind beide Fraktionen verbündet?</summary>
    public static bool Allied(Universe u, FactionId a, FactionId b) => u.Alliances.Count > 0 && Between(u, a, b) is not null;

    /// <summary>Verbündete einer Fraktion (Id-Reihenfolge).</summary>
    public static List<FactionId> AlliesOf(Universe u, FactionId f)
    {
        var list = new List<FactionId>();
        foreach (var al in u.Alliances) if (al.Involves(f)) list.Add(al.Other(f));
        list.Sort((x, y) => x.Value.CompareTo(y.Value));
        return list;
    }

    public static int CountOf(Universe u, FactionId f) => u.Alliances.Count(al => al.Involves(f));

    /// <summary>
    /// Behandelt die Fraktion <paramref name="authority"/> den Akteur <paramref name="actor"/> als Verbündeten (Kontrollpunkt, Zuständigkeit, Sperrgroll)?
    /// Bündnis der Fraktionen oder — wie bisher — Beziehung ≥ <see cref="EconomyRules.AllyRelation"/>. Ohne Bündnis also genau das Verhalten bis Runde 44.
    /// </summary>
    public static bool TreatAsAlly(Universe u, FactionId authority, ActorId actor) =>
        Relations.Effective(u, authority, actor) >= u.Rules.AllyRelation
        || (u.Alliances.Count > 0 && u.FactionOf(actor) is { } f && Allied(u, authority, f));

    /// <summary>Beide Richtungen der Beziehung verschieben (mit der Spielerfraktion über <see cref="PlayerStanding.Change"/>, sonst direkt).</summary>
    public static void ChangeRelation(Universe u, FactionId a, FactionId b, float delta, string reason)
    {
        if (delta == 0f || a == b) return;
        if (u.IsPlayerFaction(a)) { PlayerStanding.Change(u, b, delta, reason); return; }
        if (u.IsPlayerFaction(b)) { PlayerStanding.Change(u, a, delta, reason); return; }
        Relations.Set(u, a, b, Relations.Effective(u, a, b) + delta);
        Relations.Set(u, b, a, Relations.Effective(u, b, a) + delta);
        u.TouchAccess();
    }

    static string Name(Universe u, FactionId f) => u.IsPlayerFaction(f) ? PlayerFaction.DisplayName : u.Factions.TryGetValue(f, out var x) ? x.Name : $"Fraktion {f.Value}";

    /// <summary>
    /// Warum <paramref name="a"/> und <paramref name="b"/> jetzt kein Bündnis schließen würden (null = sie würden). Rein. Bedingungen: Regel an, beide
    /// Kriegsparteien (Staat, Gilde oder Spielerfraktion — keine Gesetzlosen), noch kein Bündnis, je höchstens <see cref="EconomyRules.MaxAlliancesPerFaction"/>,
    /// Abkühlzeit nach dem letzten Bündnisende, keiner von beiden im Krieg (kein Bündnis, um jemanden in einen laufenden Krieg zu ziehen), Beziehung beider
    /// Richtungen ≥ <see cref="EconomyRules.AllianceMinRelation"/>, KI-Seiten mit Kooperation ≥ <see cref="EconomyRules.AllianceMinCooperation"/>.
    /// </summary>
    public static string? FormIssue(Universe u, FactionId a, FactionId b)
    {
        var r = u.Rules;
        if (!Active(u)) return "Bündnisse sind abgeschaltet.";
        if (a == b || !Wars.IsParty(u, a) || !Wars.IsParty(u, b)) return "Mit dieser Fraktion ist kein Bündnis möglich.";
        if (Allied(u, a, b)) return "Schon verbündet.";
        if (CountOf(u, a) >= r.MaxAlliancesPerFaction || CountOf(u, b) >= r.MaxAlliancesPerFaction)
            return $"Höchstens {r.MaxAlliancesPerFaction} Bündnisse je Fraktion.";
        if (u.AllianceEnded.TryGetValue(Key(a, b), out var ended) && u.Tick - ended < r.AllianceCooldownTicks)
            return $"Bündnis erst wieder in {(r.AllianceCooldownTicks - (u.Tick - ended)) / 240 + 1} min möglich.";
        if (Wars.CountOf(u, a) > 0 || Wars.CountOf(u, b) > 0) return "Kein Bündnis während eines Kriegs.";
        float ab = Relations.Effective(u, a, b), ba = Relations.Effective(u, b, a);
        if (MathF.Min(ab, ba) < r.AllianceMinRelation) return $"Beziehung zu niedrig ({MathF.Min(ab, ba):0.00}, nötig {r.AllianceMinRelation:0.00}).";
        foreach (var f in new[] { a, b })
            if (u.IsNpcFaction(f) && ActorRules.PersonalityOf(u, f).Cooperation < r.AllianceMinCooperation)
                return $"{Name(u, f)} ist zu wenig kooperativ für ein Bündnis.";
        return null;
    }

    /// <summary>Bündnis schließen (ohne Prüfung; <see cref="FormIssue"/> vorher). Ereignis <see cref="AllianceFormed"/>.</summary>
    public static Alliance Form(Universe u, FactionId a, FactionId b)
    {
        if (Between(u, a, b) is { } running) return running;
        var (x, y) = a.Value < b.Value ? (a, b) : (b, a);
        var al = new Alliance { A = x, B = y, Since = u.Tick };
        u.Alliances.Add(al);
        u.TouchAccess();
        u.Events.Publish(new AllianceFormed(x, y, $"Bündnis zwischen {Name(u, x)} und {Name(u, y)}."));
        return al;
    }

    /// <summary>Bündnis auflösen: entfernt, Abkühlzeit beginnt, Ereignis <see cref="AllianceDissolved"/>.</summary>
    public static void Dissolve(Universe u, Alliance al, string reason)
    {
        if (!u.Alliances.Remove(al)) return;
        u.AllianceEnded[Key(al.A, al.B)] = u.Tick;
        u.TouchAccess();
        string why = reason switch
        {
            "relation" => "Beziehung zu schlecht",
            "betrayal" => "Angriff auf den Verbündeten",
            "betrayal_ally" => "Angriff auf einen Verbündeten des Partners",
            "no_aid" => "kein Beistand geleistet",
            "left" => "ausgetreten",
            _ => "aufgelöst",
        };
        u.Events.Publish(new AllianceDissolved(al.A, al.B, reason, $"Bündnis zwischen {Name(u, al.A)} und {Name(u, al.B)} beendet ({why})."));
    }

    /// <summary>
    /// Nach einer Kriegserklärung (<paramref name="w"/>.A greift <paramref name="w"/>.B an; nicht für Beitritte selbst — Tiefe 1): (1) ein Bündnis zwischen
    /// beiden zerbricht (Verrat); (2) Bündnisse des Angreifers mit Verbündeten des Opfers zerbrechen, die Verratenen verlieren
    /// <see cref="EconomyRules.AllianceBetrayalLoss"/> zum Angreifer; (3) KI-Verbündete des Opfers erklären dem Angreifer den Krieg (ohne Kriegskasse/-grund,
    /// aber nicht während der Friedens-Abkühlzeit mit ihm). Der Spieler als Verbündeter wird nicht gezwungen — er wird gerufen (Comms) und hat
    /// <see cref="EconomyRules.AllianceCallTicks"/> Zeit (<see cref="AllianceSystem"/>).
    /// </summary>
    public static void OnWarDeclared(Universe u, War w)
    {
        if (!Active(u) || u.Alliances.Count == 0) return;
        var attacker = w.A; var defender = w.B;
        if (Between(u, attacker, defender) is { } own) Dissolve(u, own, "betrayal");
        foreach (var c in AlliesOf(u, attacker))
            if (Allied(u, c, defender) && Between(u, attacker, c) is { } al)
            {
                Dissolve(u, al, "betrayal_ally");
                ChangeRelation(u, c, attacker, -u.Rules.AllianceBetrayalLoss, "betrayal");
            }
        foreach (var c in AlliesOf(u, defender))
        {
            if (c == attacker || u.IsPlayerFaction(c) || !Wars.CanWage(u, c) || Wars.AtWar(u, c, attacker)) continue;
            if (u.PeaceSince.TryGetValue(Key(c, attacker), out var peace) && u.Tick - peace < u.Rules.PeaceCooldownTicks) continue;
            Wars.Declare(u, c, attacker, alliedJoin: true);
            u.Events.Publish(new AllyJoinedWar(c, defender, attacker, $"{Name(u, c)} steht {Name(u, defender)} bei und erklärt {Name(u, attacker)} den Krieg."));
        }
    }

    /// <summary>Wartet ein Beistandsruf an den Spieler (Krieg gegen seinen Verbündeten <paramref name="ally"/> durch <paramref name="attacker"/>)? Liefert den Krieg.</summary>
    public static War? CallFor(Universe u, FactionId ally, FactionId attacker)
    {
        if (u.PlayerFaction is not { } me || !Allied(u, me, ally) || attacker == me) return null;
        var w = Wars.Between(u, attacker, ally);
        return w is not null && !w.Join && w.A == attacker && w.B == ally && u.Tick - w.Since < u.Rules.AllianceCallTicks ? w : null;
    }

    /// <summary>Offene Beistandsrufe an den Spieler (Verbündeter, Angreifer, Krieg).</summary>
    public static IEnumerable<(FactionId Ally, FactionId Attacker, War War)> Calls(Universe u)
    {
        if (u.PlayerFaction is not { } me || u.Alliances.Count == 0) yield break;
        foreach (var ally in AlliesOf(u, me))
            foreach (var w in u.Wars.ToList())
                if (w.B == ally && CallFor(u, ally, w.A) is not null) yield return (ally, w.A, w);
    }

    /// <summary>Warum der Spieler <paramref name="attacker"/> jetzt nicht als Beistand den Krieg erklären kann (null = er kann). Keine Kriegskasse nötig.</summary>
    public static string? AidIssue(Universe u, FactionId attacker)
    {
        if (!Active(u) || !Wars.Active(u)) return "Bündnisse oder Krieg sind abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (!Calls(u).Any(c => c.Attacker == attacker)) return "Kein Beistandsruf gegen diese Fraktion offen.";
        if (Wars.AtWar(u, me, attacker)) return "Schon im Krieg.";
        if (u.PeaceSince.TryGetValue(Key(me, attacker), out var peace) && u.Tick - peace < u.Rules.PeaceCooldownTicks) return "Frieden mit dem Angreifer ist zu frisch.";
        return null;
    }

    /// <summary>Der Spieler leistet Beistand: Krieg gegen den Angreifer (Beitritt — ruft niemanden weiter). Ereignis <see cref="AllyJoinedWar"/>.</summary>
    public static War Aid(Universe u, FactionId attacker)
    {
        var me = u.PlayerFaction ?? throw new InvalidOperationException("Keine Spielerfraktion");
        var ally = Calls(u).First(c => c.Attacker == attacker).Ally;
        var w = Wars.Declare(u, me, attacker, alliedJoin: true);
        u.Events.Publish(new AllyJoinedWar(me, ally, attacker, $"Du stehst {Name(u, ally)} bei und erklärst {Name(u, attacker)} den Krieg."));
        return w;
    }

    /// <summary>Spieler schlägt <paramref name="f"/> ein Bündnis vor — die KI entscheidet sofort und deterministisch (<see cref="FormIssue"/>).</summary>
    public static string? ProposeIssue(Universe u, FactionId f)
    {
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (!u.IsNpcFaction(f)) return "Mit dieser Fraktion ist kein Bündnis möglich.";
        return FormIssue(u, me, f);
    }

    /// <summary>Spieler verlässt das Bündnis mit <paramref name="f"/> (Beziehung −<see cref="EconomyRules.AllianceLeaveLoss"/>, Abkühlzeit).</summary>
    public static string? LeaveIssue(Universe u, FactionId f)
    {
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (!Allied(u, me, f)) return "Kein Bündnis mit dieser Fraktion.";
        return null;
    }

    public static void Leave(Universe u, FactionId f)
    {
        var me = u.PlayerFaction ?? throw new InvalidOperationException("Keine Spielerfraktion");
        if (Between(u, me, f) is not { } al) return;
        Dissolve(u, al, "left");
        ChangeRelation(u, me, f, -u.Rules.AllianceLeaveLoss, "alliance_left");
    }
}

/// <summary>
/// Runde 45: Bündnisse pflegen (Politik-Modul, Intervall 100, nach dem <see cref="WarSystem"/>; kein Zufall). (1) Bündnisse unter
/// <see cref="EconomyRules.AllianceBreakRelation"/> oder mit verschwundenen Partnern zerbrechen; (2) hat der Spieler einem gerufenen Verbündeten nach
/// <see cref="EconomyRules.AllianceCallTicks"/> nicht beigestanden, endet das Bündnis (−<see cref="EconomyRules.AllianceDeclineLoss"/>) — geprüft genau im
/// Takt, in dem die Frist abläuft (zustandslos); (3) höchstens ein neues KI-Bündnis je Takt (Paare in Id-Reihenfolge, <see cref="Alliances.FormIssue"/>).
/// KI-Fraktionen schlagen dem Spieler nichts vor — der Spieler schlägt vor (Comms), der Posteingang meldet, wann eine Fraktion annehmen würde.
/// </summary>
public sealed class AllianceSystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.AlliancesActive) return;
        foreach (var al in u.Alliances.ToList())
        {
            if (!Wars.IsParty(u, al.A) || !Wars.IsParty(u, al.B)) { Alliances.Dissolve(u, al, "gone"); continue; }
            float rel = MathF.Min(Relations.Effective(u, al.A, al.B), Relations.Effective(u, al.B, al.A));
            if (rel < r.AllianceBreakRelation) Alliances.Dissolve(u, al, "relation");
        }
        if (u.PlayerFaction is { } me)
            foreach (var ally in Alliances.AlliesOf(u, me))
                foreach (var w in u.Wars.ToList())
                {
                    if (w.B != ally || w.A == me || w.Join || Wars.AtWar(u, me, w.A)) continue;
                    long age = u.Tick - w.Since;
                    if (age < r.AllianceCallTicks || age >= r.AllianceCallTicks + Interval) continue;
                    if (Alliances.Between(u, me, ally) is not { } al) break;
                    Alliances.Dissolve(u, al, "no_aid");
                    Alliances.ChangeRelation(u, me, ally, -r.AllianceDeclineLoss, "no_aid");
                    break;
                }
        var npc = u.NpcFactionIds();
        foreach (var a in npc)
            foreach (var b in npc)
            {
                if (a.Value >= b.Value || Alliances.FormIssue(u, a, b) is not null) continue;
                Alliances.Form(u, a, b);
                return;
            }
    }
}
