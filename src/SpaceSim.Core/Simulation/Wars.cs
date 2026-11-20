using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 28 (POLITIK.md „Krieg: jeder darf, es kostet“, KAMPF.md Stufe 28): Regeln des Kriegs zwischen Fraktionen. Eine Fraktion erklärt nur mit
/// <b>Kriegsgrund</b> (Beziehung höchstens <see cref="EconomyRules.WarDeclareRelation"/>, verschoben nach Aggression), <b>Kriegskasse</b>
/// (<see cref="FactionViability.Spendable"/> ≥ <see cref="EconomyRules.WarChest"/>) und ohne klare Unterlegenheit (<see cref="EconomyRules.WarMinStrengthRatio"/>).
/// Im Krieg bleibt die Beziehung feindlich (Sperren folgen über die Politik von Phase 10), die Fraktionen kaufen Kriegsschiffe (<see cref="NavySystem"/>), die
/// feindliche Stationen belagern. Kriegsmüdigkeit aus Dauer und Verlusten beendet den Krieg (nie vor <see cref="EconomyRules.WarMinTicks"/>); eine Kasse unter
/// der Rücklage beendet ihn sofort — keine Kriegsentscheidung macht eine Fraktion handlungsunfähig. Rein bis auf die benannten Schreibstellen.
/// </summary>
public static class Wars
{
    /// <summary>Darf überhaupt Krieg geführt werden (Hauptschalter)?</summary>
    public static bool Active(Universe u) => u.Rules.Warfare;

    public static War? Between(Universe u, FactionId a, FactionId b)
    {
        foreach (var w in u.Wars) if ((w.A == a && w.B == b) || (w.A == b && w.B == a)) return w;
        return null;
    }

    public static bool AtWar(Universe u, FactionId a, FactionId b) => a != b && u.Wars.Count > 0 && Between(u, a, b) is not null;

    /// <summary>Im Krieg mit den Fraktionen zweier Akteure?</summary>
    public static bool AtWar(Universe u, ActorId a, ActorId b) =>
        u.Wars.Count > 0 && u.FactionOf(a) is { } fa && u.FactionOf(b) is { } fb && AtWar(u, fa, fb);

    /// <summary>Gegner einer Fraktion (Reihenfolge der Kriege).</summary>
    public static IEnumerable<FactionId> Enemies(Universe u, FactionId f)
    {
        foreach (var w in u.Wars) if (w.Involves(f)) yield return w.Other(f);
    }

    /// <summary>
    /// Runde 40: verweigert die Station dem Schiff Handel/Dienste wegen Krieg (null = nicht)? Nur das aktive Spielerschiff gegen die
    /// Stationsfraktion im Krieg (auch im Waffenstillstand — <see cref="AtWar(Universe, FactionId, FactionId)"/>, nicht <see cref="Hostile(Universe, FactionId, FactionId)"/>).
    /// </summary>
    public static string? ServiceRefusal(Universe u, Station st, Ship ship)
    {
        if (!ship.IsPlayer || u.FactionOf(st) is not { } f || !AtWar(u, u.PlayerActor, f)) return null;
        return $"Krieg mit {(u.Factions.TryGetValue(f, out var fa) ? fa.Name : "der Fraktion")}";
    }

    public static int CountOf(Universe u, FactionId f) => u.Wars.Count(w => w.Involves(f));

    static (int, int) Key(FactionId a, FactionId b) => a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);

    /// <summary>Kriegsfähige Fraktion: Staat oder Gilde (keine Gesetzlosen, nicht die Spielerfraktion ohne Akteur).</summary>
    public static bool CanWage(Universe u, FactionId f) =>
        u.Factions.ContainsKey(f) && u.Actors.TryGetValue(f, out var a) && a.Kind is ActorKind.State or ActorKind.Guild;

    /// <summary>Runde 38: Kriegspartei — eine kriegsfähige KI-Fraktion oder die Spielerfraktion.</summary>
    public static bool IsParty(Universe u, FactionId f) => CanWage(u, f) || u.IsPlayerFaction(f);

    /// <summary>
    /// Kriegsstärke: Kampfstärke der Polizei und Kriegsschiffe + verfügbares Geld ÷ <see cref="EconomyRules.WarStrengthMoneyScale"/>. Runde 38: für die
    /// Spielerfraktion die Kampfstärke ihrer bewaffneten Schiffe + Kasse ÷ Maßstab.
    /// </summary>
    public static float Strength(Universe u, FactionId f)
    {
        float s = 0f;
        if (u.IsPlayerFaction(f))
        {
            foreach (var ship in u.ShipsById())
                if (ship.Owner == u.PlayerActor && ship.Has(Capability.Combat)) s += CombatRules.Strength(ship);
            return s + MathF.Max(0f, u.Player.Treasury.Credits) / MathF.Max(1f, u.Rules.WarStrengthMoneyScale);
        }
        foreach (var ship in u.ShipsById())
            if ((ThreatMap.IsPolice(ship) || IsWarship(ship)) && u.FactionOf(ship) == f) s += CombatRules.Strength(ship);
        if (u.Actors.TryGetValue(f, out var a)) s += FactionViability.Spendable(u, a) / MathF.Max(1f, u.Rules.WarStrengthMoneyScale);
        return s;
    }

    /// <summary>Schwelle des Kriegsgrunds für eine Fraktion (Aggression senkt die Hemmschwelle). Rein.</summary>
    public static float DeclareThreshold(Universe u, FactionId f) =>
        u.Rules.WarDeclareRelation + (ActorRules.PersonalityOf(u, f).Aggression - 0.5f) * u.Rules.LockAggressionShift;

    /// <summary>Warum <paramref name="a"/> gegen <paramref name="b"/> jetzt keinen Krieg erklären würde (null = es würde). Rein.</summary>
    public static string? DeclareIssue(Universe u, FactionId a, FactionId b)
    {
        var r = u.Rules;
        if (!Active(u)) return "Krieg ist abgeschaltet.";
        // Runde 38: eine KI-Fraktion erklärt dem Spieler nur mit Regel AiWarOnPlayer den Krieg (sonst gleiche Bedingungen); der Spieler erklärt über PlayerDeclareIssue
        bool vsPlayer = u.IsPlayerFaction(b);
        if (vsPlayer && !r.AiWarOnPlayer) return "Krieg gegen den Spieler ist (noch) abgeschaltet.";
        if (a == b || !CanWage(u, a) || !(CanWage(u, b) || vsPlayer)) return "Keine kriegsfähigen Fraktionen.";
        if (AtWar(u, a, b)) return "Schon im Krieg.";
        if (u.PeaceSince.TryGetValue(Key(a, b), out var peace) && u.Tick - peace < r.PeaceCooldownTicks) return "Frieden ist zu frisch.";
        float rel = Relations.Effective(u, a, b);
        if (rel > DeclareThreshold(u, a)) return "Kein Kriegsgrund.";
        if (FactionViability.Spendable(u, u.Actors[a]) < r.WarChest) return "Keine Kriegskasse.";
        float own = Strength(u, a), enemy = Strength(u, b);
        if (own < r.WarMinStrengthRatio * enemy) return "Klar unterlegen.";
        return null;
    }

    /// <summary>
    /// Runde 38 (Entscheidung des Nutzers: kein Kriegsgrund nötig): warum der Spieler <paramref name="f"/> jetzt nicht den Krieg erklären kann (null = er kann).
    /// Bedingungen: Krieg an, Spielerfraktion vorhanden, <paramref name="f"/> kriegsfähige KI-Fraktion, noch kein Krieg, Abkühlzeit nach dem letzten Frieden
    /// (<see cref="EconomyRules.PeaceCooldownTicks"/>) vorbei, Kriegskasse (Spielerkasse ≥ <see cref="EconomyRules.WarChest"/>). Rein.
    /// </summary>
    public static string? PlayerDeclareIssue(Universe u, FactionId f)
    {
        var r = u.Rules;
        if (!Active(u)) return "Krieg ist abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (!CanWage(u, f)) return "Mit dieser Fraktion kann man keinen Krieg führen.";
        if (AtWar(u, me, f)) return "Schon im Krieg.";
        if (u.PeaceSince.TryGetValue(Key(me, f), out var peace) && u.Tick - peace < r.PeaceCooldownTicks)
            return $"Frieden ist zu frisch (noch {(r.PeaceCooldownTicks - (u.Tick - peace)) / 240} min).";
        if (u.Player.Treasury.Credits < r.WarChest) return $"Kriegskasse fehlt ({r.WarChest:0} Cr nötig).";
        return null;
    }

    /// <summary>
    /// Runde 38: der Spieler erklärt <paramref name="f"/> den Krieg (ohne Prüfung; <see cref="PlayerDeclareIssue"/> vorher). Die Beziehung jeder anderen
    /// KI-Fraktion zu ihm sinkt um <see cref="EconomyRules.PlayerWarRelationLoss"/> (<see cref="PlayerStanding.Change"/>). Die Kriegskasse bleibt beim Spieler
    /// (Bedingung, kein Preis — wie bei KI-Kriegen).
    /// </summary>
    public static War PlayerDeclare(Universe u, FactionId f)
    {
        var me = u.PlayerFaction ?? throw new InvalidOperationException("Keine Spielerfraktion");
        var w = Declare(u, me, f);
        foreach (var other in u.NpcFactionIds())
            if (other != f && CanWage(u, other)) PlayerStanding.Change(u, other, -u.Rules.PlayerWarRelationLoss, "war_declared");
        return w;
    }

    // ───────── Runde 40: Waffenstillstand, Frieden, Tribut (Comms-Diplomatie) ─────────

    /// <summary>Läuft für diesen Krieg gerade ein Waffenstillstand?</summary>
    public static bool CeasefireActive(Universe u, War w) => w.CeasefireUntil > u.Tick;

    /// <summary>
    /// Feindlich im Kampf (rein): Krieg <b>und</b> kein laufender Waffenstillstand. An den Kampfstellen ersetzt das <see cref="AtWar(Universe, FactionId, FactionId)"/>:
    /// während des Waffenstillstands keine Kriegsangriffe, Belagerungen oder Stationsgeschütze gegen Kriegsgegner. Sperren und Handelsverweigerung bleiben
    /// über <see cref="AtWar(Universe, FactionId, FactionId)"/> bestehen (Handel bleibt im Waffenstillstand gesperrt).
    /// </summary>
    public static bool Hostile(Universe u, FactionId a, FactionId b) => a != b && Between(u, a, b) is { } w && !CeasefireActive(u, w);

    /// <summary>Wie oben für zwei Akteure (über ihre Fraktionen).</summary>
    public static bool Hostile(Universe u, ActorId a, ActorId b) =>
        a != b && u.FactionOf(a) is { } fa && u.FactionOf(b) is { } fb && Hostile(u, fa, fb);

    /// <summary>Runde 40: laufender Krieg zwischen Spielerfraktion und <paramref name="f"/> (null = keiner).</summary>
    public static War? PlayerWar(Universe u, FactionId f) => u.PlayerFaction is { } me ? Between(u, me, f) : null;

    /// <summary>Verbleibende Sperrzeit für ein neues Angebot (Waffenstillstand/Frieden) in diesem Krieg (0 = frei).</summary>
    public static long OfferCooldownLeft(Universe u, War w) =>
        w.LastOfferTick > 0 ? Math.Max(0L, u.Rules.OfferCooldownTicks - (u.Tick - w.LastOfferTick)) : 0L;

    /// <summary>Tick eines Angebots eintragen (0 = keines; Tick 0 wird auf 1 gehoben, damit das „0 = nie“-Sentinel nicht kollidiert).</summary>
    public static void MarkOffered(Universe u, War w) => w.LastOfferTick = Math.Max(1L, u.Tick);

    /// <summary>
    /// Runde 40: geforderter Tribut (Cr) der Seite <paramref name="ai"/> für Waffenstillstand/Frieden ohne ausreichende Müdigkeit —
    /// <see cref="EconomyRules.TributeBase"/> × (1 − Müdigkeit) + eigene Verluste × <see cref="EconomyRules.TributeLossShare"/>, gemindert um den bereits
    /// gezahlten Tribut (<see cref="War.TributePaid"/>). Deterministisch, nie &lt; 0.
    /// </summary>
    public static float TributeFor(Universe u, War w, FactionId ai)
    {
        float lossAi = w.A == ai ? w.LossA : w.LossB;
        float raw = MathF.Max(0f, u.Rules.TributeBase * (1f - MathF.Min(1f, w.Weariness))) + lossAi * u.Rules.TributeLossShare;
        return MathF.Max(0f, raw - w.TributePaid);
    }

    /// <summary>Nimmt die Seite <paramref name="ai"/> einen Waffenstillstand ohne Zahlung an (deterministisch: Müdigkeit oder klare Unterlegenheit)?</summary>
    public static bool CeasefireFree(Universe u, War w, FactionId ai)
    {
        if (w.Weariness >= u.Rules.CeasefireWeariness) return true;
        return Strength(u, ai) < u.Rules.CeasefireStrengthRatio * MathF.Max(0.0001f, Strength(u, w.Other(ai)));
    }

    /// <summary>Nimmt die Seite <paramref name="ai"/> Frieden ohne Zahlung an (deterministisch: Mindestdauer + Müdigkeit)?</summary>
    public static bool PeaceFree(Universe u, War w, FactionId ai) =>
        u.Tick - w.Since >= u.Rules.WarMinTicks && w.Weariness >= u.Rules.PeaceWeariness;

    /// <summary>
    /// Runde 40: warum der Spieler <paramref name="f"/> jetzt keinen Waffenstillstand anbieten kann (null = er kann; die KI-Entscheidung ist dann
    /// deterministisch über <see cref="CeasefireFree"/>/<see cref="TributeFor"/>). Bedingungen: Krieg an, laufender Krieg mit <paramref name="f"/>,
    /// kein aktiver Waffenstillstand, Angebots-Abkühlzeit (<see cref="EconomyRules.OfferCooldownTicks"/>), und falls kein kostenloser Annahme:
    /// Tribut in der Kasse.
    /// </summary>
    public static string? CeasefireIssue(Universe u, FactionId f)
    {
        var r = u.Rules;
        if (!Active(u)) return "Krieg ist abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (Between(u, me, f) is not { } w) return "Kein Krieg mit dieser Fraktion.";
        if (CeasefireActive(u, w)) return $"Waffenstillstand läuft schon (noch {(w.CeasefireUntil - u.Tick) / 240} min).";
        if (OfferCooldownLeft(u, w) is > 0 and var left) return $"Angebot erst wieder in {left / 240} min möglich.";
        if (!CeasefireFree(u, w, f))
        {
            float t = TributeFor(u, w, f);
            if (t > 0f && u.Player.Treasury.Credits < t) return $"Die KI verlangt einen Tribut von {t:0} Cr — Kasse reicht nicht.";
        }
        return null;
    }

    /// <summary>
    /// Runde 40: der Spieler bietet <paramref name="f"/> den Waffenstillstand an (ohne Prüfung; <see cref="CeasefireIssue"/> vorher). Ergebnis null bei Sperre,
    /// sonst Krieg und gezahlter Tribut. Setzt <see cref="War.LastOfferTick"/> und <see cref="War.CeasefireUntil"/>; bei nicht kostenloser Annahme zahlt der
    /// Spieler den Tribut an die Fraktionskasse. Deterministisch — dieselbe Entscheidung wie in der Vorschau.
    /// </summary>
    public static (War War, float Tribute)? CeasefireAccept(Universe u, FactionId f)
    {
        if (CeasefireIssue(u, f) is not null) return null;
        var w = Between(u, u.PlayerFaction ?? throw new InvalidOperationException("Keine Spielerfraktion"), f)!;
        MarkOffered(u, w);
        float tribute = CeasefireFree(u, w, f) ? 0f : TributeFor(u, w, f);
        if (tribute > 0f) PayTribute(u, f, tribute, w);
        w.CeasefireUntil = u.Tick + u.Rules.CeasefireTicks;
        DropWarWarnings(u, w, "Waffenstillstand — der Angriff unterbleibt.");   // Runde 41 (A2): Warnung aufgehoben → Meldung entfällt sofort
        u.TouchAccess();
        string na = u.Factions.TryGetValue(w.A, out var fa) ? fa.Name : "?", nb = u.Factions.TryGetValue(w.B, out var fb) ? fb.Name : "?";
        u.Events.Publish(new CeasefireConcluded(w.A, w.B, w.CeasefireUntil, tribute,
            $"Waffenstillstand zwischen {na} und {nb} bis Tick {w.CeasefireUntil}{(tribute > 0f ? $" — Tribut {tribute:0} Cr" : "")}."));
        return (w, tribute);
    }

    /// <summary>
    /// Runde 40: warum der Spieler <paramref name="f"/> jetzt keinen Frieden schließen kann (null = er kann). Wie <see cref="CeasefireIssue"/>, aber die
    /// kostenlose Annahme braucht <see cref="PeaceFree"/> (Mindestdauer + <see cref="EconomyRules.PeaceWeariness"/>), sonst Tribut.
    /// </summary>
    public static string? PeaceIssue(Universe u, FactionId f)
    {
        var r = u.Rules;
        if (!Active(u)) return "Krieg ist abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (Between(u, me, f) is not { } w) return "Kein Krieg mit dieser Fraktion.";
        if (OfferCooldownLeft(u, w) is > 0 and var left) return $"Angebot erst wieder in {left / 240} min möglich.";
        if (!PeaceFree(u, w, f))
        {
            float t = TributeFor(u, w, f);
            if (t > 0f && u.Player.Treasury.Credits < t) return $"Die KI verlangt einen Tribut von {t:0} Cr — Kasse reicht nicht.";
        }
        return null;
    }

    /// <summary>
    /// Runde 40: der Spieler schließt mit <paramref name="f"/> Frieden (ohne Prüfung; <see cref="PeaceIssue"/> vorher). Null bei Sperre, sonst gezahlter Tribut.
    /// Bei nicht kostenloser Annahme zahlt der Spieler den Tribut an die Fraktionskasse, dann <see cref="MakePeace"/> (Grund „negotiated").
    /// </summary>
    public static float? PeaceAccept(Universe u, FactionId f)
    {
        if (PeaceIssue(u, f) is not null) return null;
        var w = Between(u, u.PlayerFaction ?? throw new InvalidOperationException("Keine Spielerfraktion"), f)!;
        MarkOffered(u, w);
        float tribute = PeaceFree(u, w, f) ? 0f : TributeFor(u, w, f);
        if (tribute > 0f) PayTribute(u, f, tribute, w);
        MakePeace(u, w, "negotiated");
        return tribute;
    }

    /// <summary>Runde 40: warum der Spieler <paramref name="f"/> jetzt keinen Tribut von <paramref name="amount"/> Cr zahlen kann (null = er kann).</summary>
    public static string? TributeIssue(Universe u, FactionId f, float amount)
    {
        if (!Active(u)) return "Krieg ist abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (Between(u, me, f) is null) return "Kein Krieg mit dieser Fraktion.";
        if (amount <= 0f) return "Betrag muss positiv sein.";
        if (u.Player.Treasury.Credits < amount) return $"Kasse reicht nicht ({amount:0} Cr nötig).";
        return null;
    }

    /// <summary>
    /// Runde 40: Tribut zahlen (ohne Prüfung; <see cref="TributeIssue"/> vorher): Spielerkasse → Kasse der Fraktion
    /// (<see cref="TreasuryIncome"/> „tribute“), zählt auf <see cref="War.TributePaid"/> und mindert so den geforderten Tribut. Ereignisse
    /// <see cref="TributePaid"/> und <see cref="CommsActionPerformed"/>.
    /// </summary>
    public static void PayTribute(Universe u, FactionId f, float amount, War? w = null)
    {
        w ??= PlayerWar(u, f);
        u.Player.Treasury.Credits -= amount;
        if (u.Actors.TryGetValue(f, out var authority))
        {
            authority.Treasury.Credits += amount;
            u.Events.Publish(new TreasuryIncome(authority.Id, "tribute", amount));
        }
        if (w is not null) w.TributePaid += amount;
        string name = u.Factions.TryGetValue(f, out var fac) ? fac.Name : "?";
        u.Events.Publish(new TributePaid(u.PlayerActor, f, amount, $"Tribut {amount:0} Cr an {name} gezahlt."));
        u.Events.Publish(new CommsActionPerformed("tribute", f.Value, -amount, $"Tribut {amount:0} Cr an {name}."));
    }

    // ───────── Runde 45: Tributfrieden (KI gegen KI) und Tributforderung des Spielers ─────────

    /// <summary>
    /// Sieger eines Kriegs für den Tributfrieden (rein): die stärkere Seite, wenn sie mindestens <see cref="EconomyRules.TributeStrengthRatio"/> mal so stark ist
    /// (<see cref="Strength"/>); sonst keiner.
    /// </summary>
    public static FactionId? TributeWinner(Universe u, War w)
    {
        float sa = Strength(u, w.A), sb = Strength(u, w.B), ratio = u.Rules.TributeStrengthRatio;
        if (sa >= ratio * MathF.Max(0.0001f, sb)) return w.A;
        if (sb >= ratio * MathF.Max(0.0001f, sa)) return w.B;
        return null;
    }

    /// <summary>Was der KI-Verlierer <paramref name="payer"/> höchstens zahlt: <see cref="EconomyRules.TributeMaxSpendableShare"/> seines verfügbaren Geldes (nie die Rücklage).</summary>
    public static float TributeBudget(Universe u, FactionId payer) =>
        u.Actors.TryGetValue(payer, out var a) ? MathF.Max(0f, FactionViability.Spendable(u, a)) * u.Rules.TributeMaxSpendableShare : 0f;

    /// <summary>
    /// Tributfrieden zwischen zwei KI-Fraktionen (rein; Ergebnis = Zahler, Sieger, Betrag, Forderung, oder null): Regel an, Mindestdauer, Müdigkeit ≥
    /// <see cref="EconomyRules.TributePeaceWeariness"/>, kein Angebot innerhalb der Abkühlzeit, ein klarer Sieger, Forderung &gt; 0 (<see cref="TributeFor"/> des
    /// Siegers) und der Verlierer kann mindestens <see cref="EconomyRules.TributeAcceptShare"/> davon aus seinem Tributbudget zahlen.
    /// </summary>
    public static (FactionId Payer, FactionId Winner, float Amount, float Demand)? AiTributeDeal(Universe u, War w)
    {
        var r = u.Rules;
        if (!r.AiTributePeace || !u.IsNpcFaction(w.A) || !u.IsNpcFaction(w.B)) return null;
        if (u.Tick - w.Since < r.WarMinTicks || w.Weariness < r.TributePeaceWeariness || OfferCooldownLeft(u, w) > 0) return null;
        if (TributeWinner(u, w) is not { } winner) return null;
        var loser = w.Other(winner);
        float demand = TributeFor(u, w, winner);
        if (demand <= 0f) return null;
        float pay = MathF.Min(demand, TributeBudget(u, loser));
        if (pay < r.TributeAcceptShare * demand) return null;
        return (loser, winner, pay, demand);
    }

    /// <summary>Zahlung Fraktion → Fraktion (KI-Kasse an KI- oder Spielerkasse): Doppelbuchung (Upkeep/Income „tribute“), zählt auf <see cref="War.TributePaid"/>.</summary>
    static void TransferTribute(Universe u, War w, FactionId payer, FactionId to, float amount)
    {
        var from = u.Actors[payer];
        from.Treasury.Credits -= amount;
        u.Events.Publish(new TreasuryUpkeep(from.Id, "tribute", amount, 0f));
        if (u.Actors.TryGetValue(to, out var dest))
        {
            dest.Treasury.Credits += amount;
            u.Events.Publish(new TreasuryIncome(dest.Id, "tribute", amount));
        }
        w.TributePaid += amount;
        string pn = u.Factions.TryGetValue(payer, out var fp) ? fp.Name : "?", tn = u.Factions.TryGetValue(to, out var ft) ? ft.Name : "?";
        u.Events.Publish(new TributePaid(from.Id, to, amount, $"{pn} zahlt {tn} einen Tribut von {amount:0} Cr."));
    }

    /// <summary>Führt einen Tributfrieden aus (<see cref="AiTributeDeal"/> vorher): Zahlung, Frieden (Grund „tribute“), Ereignis <see cref="TributePeace"/>.</summary>
    public static void ConcludeTributePeace(Universe u, War w, FactionId payer, FactionId winner, float amount, float demand)
    {
        TransferTribute(u, w, payer, winner, amount);
        MakePeace(u, w, "tribute");
        string pn = u.Factions.TryGetValue(payer, out var fp) ? fp.Name : "?", wn = u.Factions.TryGetValue(winner, out var fw) ? fw.Name : "?";
        u.Events.Publish(new TributePeace(payer, winner, amount, demand, $"{pn} kauft sich mit {amount:0} Cr Tribut von {wn} frei."));
    }

    /// <summary>Tribut, den der Spieler von <paramref name="f"/> fordern kann: Forderung des Siegers (<see cref="TributeFor"/> der Spielerseite), gedeckelt durch das Tributbudget.</summary>
    public static float DemandAmount(Universe u, War w, FactionId f) =>
        u.PlayerFaction is { } me ? MathF.Min(TributeFor(u, w, me), TributeBudget(u, f)) : 0f;

    /// <summary>
    /// Runde 45: warum der Spieler von <paramref name="f"/> jetzt keinen Tribut fordern kann (null = die KI zahlt und schließt Frieden). Dieselben Bedingungen
    /// wie der KI-Tributfrieden: Krieg läuft, Mindestdauer, Müdigkeit ≥ <see cref="EconomyRules.TributePeaceWeariness"/>, Angebots-Abkühlzeit, der Spieler ist
    /// der klare Sieger (<see cref="TributeWinner"/>), und die KI kann mindestens <see cref="EconomyRules.TributeAcceptShare"/> der Forderung aus ihrem Budget zahlen.
    /// </summary>
    public static string? DemandIssue(Universe u, FactionId f)
    {
        var r = u.Rules;
        if (!Active(u)) return "Krieg ist abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (Between(u, me, f) is not { } w) return "Kein Krieg mit dieser Fraktion.";
        if (u.Tick - w.Since < r.WarMinTicks) return $"Krieg zu jung (noch {(r.WarMinTicks - (u.Tick - w.Since)) / 240 + 1} min).";
        if (w.Weariness < r.TributePeaceWeariness) return $"Gegner noch nicht kriegsmüde ({w.Weariness:0.00} von {r.TributePeaceWeariness:0.00}).";
        if (OfferCooldownLeft(u, w) is > 0 and var left) return $"Angebot erst wieder in {left / 240 + 1} min möglich.";
        // Ausgleich (Punkt 7, Spielerbot: Tribut ohne jeden Kampf): erst nach echten Verlusten des Gegners (Schiff, Station oder Tor)
        if (r.TributeDemandNeedsLosses && !HasLosses(w, f)) return "Der Gegner hat in diesem Krieg noch nichts verloren — erst kämpfen, dann fordern.";
        if (TributeWinner(u, w) != me) return $"Nicht klar überlegen (Stärke {Strength(u, me):0.0} : {Strength(u, f):0.0}, nötig ×{r.TributeStrengthRatio:0.0}).";
        float demand = TributeFor(u, w, me);
        if (demand <= 0f) return "Keine Forderung offen.";
        if (TributeBudget(u, f) < r.TributeAcceptShare * demand) return $"{(u.Factions.TryGetValue(f, out var fa) ? fa.Name : "Die Fraktion")} kann {demand:0} Cr nicht aufbringen.";
        return null;
    }

    /// <summary>Spieler fordert Tribut (<see cref="DemandIssue"/> vorher): KI-Kasse → Spielerkasse, Frieden „tribute“, alle anderen KI-Fraktionen −<see cref="EconomyRules.PlayerDemandRelationLoss"/>.</summary>
    public static float? Demand(Universe u, FactionId f)
    {
        if (DemandIssue(u, f) is not null) return null;
        var me = u.PlayerFaction!.Value;
        var w = Between(u, me, f)!;
        MarkOffered(u, w);
        float demand = TributeFor(u, w, me);
        float pay = MathF.Min(demand, TributeBudget(u, f));
        ConcludeTributePeace(u, w, f, me, pay, demand);
        foreach (var other in u.NpcFactionIds())
            if (other != f && CanWage(u, other)) PlayerStanding.Change(u, other, -u.Rules.PlayerDemandRelationLoss, "tribute_demand");
        return pay;
    }

    /// <summary>
    /// Krieg erklären (ohne Prüfung; die Bedingungen prüft <see cref="DeclareIssue"/>). Beziehungen werden feindlich, Ereignis. Runde 45: danach ruft
    /// <see cref="Alliances.OnWarDeclared"/> die Verbündeten des Opfers (Beistandspakt) — außer bei einem Beitritt selbst (<paramref name="alliedJoin"/>, Tiefe 1).
    /// </summary>
    public static War Declare(Universe u, FactionId a, FactionId b, bool alliedJoin = false)
    {
        if (Between(u, a, b) is { } running) return running;
        var w = new War { A = a, B = b, Since = u.Tick, Join = alliedJoin };
        u.Wars.Add(w);
        float rel = Relations.Effective(u, a, b);
        PinHostile(u, w);
        u.TouchAccess();
        string na = u.Factions.TryGetValue(a, out var fa) ? fa.Name : "?", nb = u.Factions.TryGetValue(b, out var fb) ? fb.Name : "?";
        u.Events.Publish(new WarDeclared(a, b, rel, $"{na} erklärt {nb} den Krieg."));
        if (!alliedJoin) Alliances.OnWarDeclared(u, w);
        return w;
    }

    /// <summary>Frieden schließen: Krieg entfernt, Beziehung beider Seiten auf <see cref="EconomyRules.PeaceRelation"/>, Abkühlzeit.</summary>
    public static void MakePeace(Universe u, War w, string reason)
    {
        if (!u.Wars.Remove(w)) return;
        // Runde 41 (A2): Kriegswarnungen gegen den Spieler sind mit dem Krieg aufgehoben — Ursache abgelaufen, Meldung entfällt sofort
        DropWarWarnings(u, w, "Frieden — der Angriff unterbleibt.");
        // Runde 38: Spielerbeziehungen driften nicht zurück — nach einem Spielerkrieg mindestens PlayerPeaceRelation (über der ersten Sperrschwelle, kein Dauerzoll)
        float floor = u.IsPlayerFaction(w.A) || u.IsPlayerFaction(w.B) ? u.Rules.PlayerPeaceRelation : u.Rules.PeaceRelation;
        Relations.Set(u, w.A, w.B, MathF.Max(Relations.Effective(u, w.A, w.B), floor));
        Relations.Set(u, w.B, w.A, MathF.Max(Relations.Effective(u, w.B, w.A), floor));
        u.PeaceSince[Key(w.A, w.B)] = u.Tick;
        u.TouchAccess();
        string na = u.Factions.TryGetValue(w.A, out var fa) ? fa.Name : "?", nb = u.Factions.TryGetValue(w.B, out var fb) ? fb.Name : "?";
        string why = reason switch { "weary" => "Kriegsmüdigkeit", "broke" => "leere Kasse", "tribute" => "Tributzahlung", _ => "Vereinbarung" };
        u.Events.Publish(new PeaceConcluded(w.A, w.B, reason, u.Tick - w.Since, $"Frieden zwischen {na} und {nb} ({why})."));
    }

    /// <summary>
    /// Runde 41 (A2): hebt laufende Kriegswarnungen (PendingEncounter <see cref="EncounterKind.War"/>) gegen den Spieler auf, deren Ursache
    /// weg ist (Waffenstillstand/Frieden mit einer Kriegspartei) — veröffentlicht je Warnung <see cref="EncounterEscaped"/>, damit die
    /// Darstellung die Meldung sofort entfernt. Ohne Spieler oder ohne solche Warnungen nichts.
    /// </summary>
    public static void DropWarWarnings(Universe u, War w, string message)
    {
        if (!u.Ships.TryGetValue(u.PlayerShip, out var player)) return;
        foreach (var e in u.PendingEncounters.Where(e => e.Kind == EncounterKind.War && e.Target == player.Id
            && u.Sectors.TryGetValue(e.Sector, out var sec) && (sec.Owner == w.A || sec.Owner == w.B)).ToList())
        {
            u.PendingEncounters.Remove(e);
            u.Events.Publish(new EncounterEscaped(EncounterKind.War, e.Source, e.Target, message));
        }
    }

    /// <summary>Im Krieg bleibt die Beziehung beider Seiten höchstens <see cref="EconomyRules.WarRelation"/>.</summary>
    public static void PinHostile(Universe u, War w)
    {
        float cap = u.Rules.WarRelation;
        if (Relations.Effective(u, w.A, w.B) > cap) Relations.Set(u, w.A, w.B, cap);
        if (Relations.Effective(u, w.B, w.A) > cap) Relations.Set(u, w.B, w.A, cap);
    }

    /// <summary>Verlust (Materialwert) einer Seite im Krieg buchen; ohne Krieg nichts.</summary>
    /// <summary>
    /// Ausgleich (Punkt 7): ein Schiff wurde im Gefecht zerstört — gehört es einer Seite eines Kriegs mit der Spielerfraktion und stand ein Akteur der
    /// Gegenseite im Gefecht gegenüber, zählt es als Kriegsverlust (<see cref="War.ShipsLostA"/>/<see cref="War.ShipsLostB"/>). Nur mit Regel
    /// <see cref="EconomyRules.TributeDemandNeedsLosses"/>; kein Zufall.
    /// </summary>
    public static void OnShipLost(Universe u, Encounter e, CombatParticipant p, Ship ship)
    {
        if (!u.Rules.TributeDemandNeedsLosses || u.Wars.Count == 0 || u.PlayerFaction is not { } me || u.FactionOf(ship) is not { } f) return;
        foreach (var q in e.Participants)
        {
            if (q.Side == p.Side || u.FactionOf(q.Owner) is not { } g || Between(u, f, g) is not { } w || (w.A != me && w.B != me)) continue;
            if (w.A == f) w.ShipsLostA++; else w.ShipsLostB++;
            return;
        }
    }

    /// <summary>Ausgleich (Punkt 7): hat <paramref name="f"/> in diesem Krieg wirklich verloren (Schiff, Station oder Tor)?</summary>
    public static bool HasLosses(War w, FactionId f) => w.A == f ? w.ShipsLostA > 0 || w.LossA > 0f : w.ShipsLostB > 0 || w.LossB > 0f;

    public static void OnLoss(Universe u, ActorId owner, ActorId attacker, float value)
    {
        if (u.Wars.Count == 0 || u.FactionOf(owner) is not { } f || u.FactionOf(attacker) is not { } g || Between(u, f, g) is not { } w) return;
        if (w.A == f) w.LossA += value; else w.LossB += value;
    }

    /// <summary>Kriegsschiff einer Fraktion (Etikett, KI)?</summary>
    public static bool IsWarship(Ship s) => s.Role == ShipRole.Warship && s.Controller == Controller.Ai;

    /// <summary>Kriegsschiffe einer Fraktion (feste Reihenfolge).</summary>
    public static List<Ship> WarshipsOf(Universe u, FactionId f) => u.ShipsById().Where(s => IsWarship(s) && u.FactionOf(s) == f).ToList();

    /// <summary>
    /// Kriegsziel einer Fraktion für ein Schiff: angreifbare Station eines Gegners (fertig, kein Versteck), bevorzugt in umkämpften Sektoren und nahe — nach
    /// Sprüngen von der Position des Schiffs (physisch erreichbar), dann Id. null = keins. Rein.
    /// </summary>
    /// <summary>
    /// Phase 31 (docs/TORE.md 5): Torblockade für ein Kriegsschiff der Fraktion <paramref name="f"/> — ein Tor-Paar mit Kampfwerten, an dem ein Ende einem
    /// Kriegsgegner gehört, dessen Krieg noch Blockaden frei hat (<see cref="EconomyRules.GateBlockadesPerWar"/>; eine laufende Blockade wird fortgesetzt), das die
    /// Karte nicht teilen würde (<see cref="MapParts.SplitsWhenClosed"/>, berücksichtigt bereits ausgefallene Tore) und nur, solange weltweit weniger als
    /// <see cref="EconomyRules.GateMaxDown"/> Tore außer Betrieb sind. Wert = Verkehr des gegnerischen Endsektors; angegriffen wird die Hälfte, die das Schiff
    /// mit den wenigsten Sprüngen erreicht. Piraten greifen nie Tore an (nur Kriegsschiffe bekommen diese Wahl). null = keine Blockade.
    /// </summary>
    public static Gate? PickGateTarget(Universe u, FactionId f, Ship ship)
    {
        var r = u.Rules;
        if (!r.GateCombat || !r.Warfare || u.Wars.Count == 0) return null;
        if (GateCombat.DownCount(u) >= r.GateMaxDown) return null;
        Gate? best = null; float bestValue = -1f; int bestHops = int.MaxValue;
        foreach (var g in u.Gates.Values.OrderBy(x => x.Id.Value))
        {
            if (!u.Gates.TryGetValue(g.Target, out var far) || g.Id.Value > far.Id.Value) continue;   // je Paar einmal (kanonisch)
            if (GateCombat.TypeOf(u, g) is null || GateCombat.IsDown(u, g) || GateCombat.InGrace(u, g) || !MapParts.Passable(g) || !MapParts.Passable(far)) continue;
            War? war = WarOverGate(u, f, g);
            if (war is null) continue;
            if (war.Blockades >= r.GateBlockadesPerWar && !BlockadeRunning(u, f, g)) continue;
            if (MapParts.SplitsWhenClosed(u, g.Id)) continue;   // nie kartenteilend
            float value = 0f;
            foreach (var half in new[] { g, far })
                if (u.Sectors.TryGetValue(half.Sector, out var sec) && Wars.AtWar(u, f, sec.Owner)) value += MathF.Max(0f, sec.Traffic);
            // erreichbare Hälfte mit den wenigsten Sprüngen
            Gate? side = null; int hops = int.MaxValue;
            foreach (var half in new[] { g, far })
            {
                var route = u.FindRoute(ship.Sector, half.Sector);
                if (route is null) continue;
                if (route.Count < hops) { hops = route.Count; side = half; }
            }
            if (side is null) continue;
            if (value > bestValue || (value == bestValue && hops < bestHops)) { best = side; bestValue = value; bestHops = hops; }
        }
        return best;
    }

    /// <summary>Krieg der Fraktion mit einem Besitzer eines Torendes (null = keiner).</summary>
    public static War? WarOverGate(Universe u, FactionId f, Gate g)
    {
        foreach (var d in GateCombat.DefendersOf(u, g)) if (d != f && Between(u, f, d) is { } w) return w;
        return null;
    }

    /// <summary>Läuft schon eine Blockade dieses Paars durch Kriegsschiffe der Fraktion?</summary>
    public static bool BlockadeRunning(Universe u, FactionId f, Gate g)
    {
        foreach (var s in u.Ships.Values)
            if (IsWarship(s) && u.FactionOf(s) == f && s.Command is AssaultCommand ac && ac.Gate.Value != 0 && (ac.Gate == g.Id || ac.Gate == g.Target)) return true;
        return false;
    }

    public static Station? PickTarget(Universe u, FactionId f, Ship ship)
    {
        Station? best = null;
        int bestHops = int.MaxValue;
        foreach (var enemy in Enemies(u, f))
            foreach (var st in u.StationsById())
            {
                if (st.IsBuildSite || st.Owner != (ActorId)enemy || StationProtection.IsHideout(u, st) || StationCombat.IsDown(u, st)) continue;
                var route = u.FindRoute(ship.Sector, st.Sector);
                if (route is null) continue;
                int hops = route.Count;
                if (hops < bestHops || (hops == bestHops && best is not null && st.Id.Value < best.Id.Value)) { best = st; bestHops = hops; }
            }
        return best;
    }
}

/// <summary>
/// Phase 28: Krieg und Frieden (Politik-Modul, Intervall 100). Nur mit <see cref="EconomyRules.Warfare"/>: laufende Kriege halten die Beziehung feindlich und
/// werden müde (Dauer ÷ <see cref="EconomyRules.WarWearyTicks"/> + Verluste ÷ <see cref="EconomyRules.WarLossScale"/>); ab Müdigkeit 1 (nach der Mindestdauer) oder
/// wenn eine Kasse unter ihre Rücklage fällt, ist Frieden. Mit <see cref="EconomyRules.WarDeclarations"/> erklärt eine Fraktion Krieg, wenn
/// <see cref="Wars.DeclareIssue"/> nichts einwendet (Fraktionspaare in Id-Reihenfolge, höchstens ein neuer Krieg je Takt). Kein Zufall.
/// </summary>
public sealed class WarSystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.Warfare) return;
        foreach (var w in u.Wars.ToList())
        {
            Wars.PinHostile(u, w);
            float ticks = u.Tick - w.Since;
            w.Weariness = ticks / MathF.Max(1f, r.WarWearyTicks) + MathF.Max(w.LossA, w.LossB) / MathF.Max(1f, r.WarLossScale) + w.MediationBonus;   // Ausbau Punkt 6: Vermittlung
            bool broke = Broke(u, w.A) || Broke(u, w.B);
            if (broke) { Wars.MakePeace(u, w, "broke"); continue; }
            // Runde 45: KI gegen KI — ein klarer Sieger nimmt Tribut, wenn der Verlierer genug aus seinem Budget zahlen kann (vor dem Müdigkeitsfrieden: der Sieger
            // lässt sich nicht umsonst abspeisen); sonst ein Angebot je Abkühlzeit. Die leere Kasse („broke“) bleibt vorrangig.
            if (r.AiTributePeace && u.IsNpcFaction(w.A) && u.IsNpcFaction(w.B) && ticks >= r.WarMinTicks && w.Weariness >= r.TributePeaceWeariness && Wars.OfferCooldownLeft(u, w) == 0)
            {
                if (Wars.AiTributeDeal(u, w) is { } deal) { Wars.ConcludeTributePeace(u, w, deal.Payer, deal.Winner, deal.Amount, deal.Demand); continue; }
                if (Wars.TributeWinner(u, w) is not null) Wars.MarkOffered(u, w);
            }
            if (ticks >= r.WarMinTicks && w.Weariness >= 1f) { Wars.MakePeace(u, w, "weary"); continue; }
            // Runde 40: ab Müdigkeit AiOfferWeariness bietet die KI dem Spieler Waffenstillstand/Frieden per Comms an (höchstens ein Angebot je OfferCooldownTicks)
            if (u.PlayerFaction is { } pf && w.Involves(pf) && w.Weariness >= r.AiOfferWeariness && Wars.OfferCooldownLeft(u, w) == 0)
            {
                var ai = w.Other(pf);
                Wars.MarkOffered(u, w);
                string name = u.Factions.TryGetValue(ai, out var fa) ? fa.Name : "?";
                if (Wars.PeaceFree(u, w, ai))
                    u.Events.Publish(new PeaceOffered(ai, pf, $"{name} bietet dir Frieden an (Kriegsmüdigkeit {w.Weariness:0.00})."));
                else
                    u.Events.Publish(new CeasefireOffered(ai, pf, $"{name} bietet dir einen Waffenstillstand an (Kriegsmüdigkeit {w.Weariness:0.00})."));
            }
        }
        if (!r.WarDeclarations) return;
        var factions = u.NpcFactionIds();   // Runde 38: die KI erklärt nur KI-Fraktionen den Krieg …
        foreach (var a in factions)
            foreach (var b in factions)
            {
                if (a == b || Wars.DeclareIssue(u, a, b) is not null) continue;
                Wars.Declare(u, a, b);
                return;
            }
        // … und dem Spieler nur mit Regel AiWarOnPlayer (vorbereitet, an mit Runde 40), nach denselben Bedingungen
        if (r.AiWarOnPlayer && u.PlayerFaction is { } me)
            foreach (var a in factions)
            {
                if (Wars.DeclareIssue(u, a, me) is not null) continue;
                Wars.Declare(u, a, me);
                return;
            }
    }

    /// <summary>Kasse unter der Rücklage (nach deren Aufbauzeit) — dann ist Frieden Pflicht.</summary>
    static bool Broke(Universe u, FactionId f) =>
        u.Actors.TryGetValue(f, out var a) && !FactionViability.ReserveBuilding(u, a) && a.Treasury.Credits < FactionViability.Reserve(u, f);
}

/// <summary>
/// Phase 28: Kriegsschiffe der Fraktionen (Sicherheitsmodul, Intervall 100). Nur mit <see cref="EconomyRules.Warfare"/>: Unterhalt aus der Kasse
/// (<see cref="EconomyRules.WarshipUpkeepPerTick"/>, „navy“); im Krieg kauft die Fraktion bis <see cref="EconomyRules.WarshipsPerWar"/> je Krieg (höchstens
/// <see cref="EconomyRules.MaxWarshipsPerFaction"/>) über die gemeinsame Kaufregel (<see cref="ShipPurchase"/>), nur aus dem verfügbaren Geld über der Rücklage;
/// nach dem Frieden (<see cref="EconomyRules.WarshipPeaceTicks"/>) oder wenn die Kasse unter die Rücklage fällt wird abgebaut (Grund c, über
/// <see cref="Departures"/>). Befehle: im Krieg Angriff auf das nächste feindliche Ziel (<see cref="AssaultCommand"/>), zur Versorgung heim, im Frieden an der Basis.
/// </summary>
public sealed class NavySystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.Warfare) return;
        foreach (var f in u.NpcFactionIds().ToList())   // Runde 38: der Spieler kauft keine KI-Kriegsschiffe
        {
            if (!Wars.CanWage(u, f) || !u.Actors.TryGetValue(f, out var actor)) continue;
            var ships = Wars.WarshipsOf(u, f);
            if (ships.Count == 0 && Wars.CountOf(u, f) == 0) continue;
            float upkeep = r.WarshipUpkeepPerTick * ships.Count * dt;
            if (upkeep > 0f)
            {
                float paid = MathF.Min(upkeep, MathF.Max(0f, actor.Treasury.Credits));
                actor.Treasury.Credits -= paid;
                u.Events.Publish(new TreasuryUpkeep(actor.Id, "navy", paid, upkeep - paid));
            }
            int wars = Wars.CountOf(u, f);
            int desired = wars > 0 ? Math.Min(r.MaxWarshipsPerFaction, r.WarshipsPerWar * wars) : 0;
            bool belowReserve = !FactionViability.ReserveBuilding(u, actor) && actor.Treasury.Credits < FactionViability.Reserve(u, f);
            if (ships.Count > 0 && (belowReserve || (ships.Count > desired && PeaceLongEnough(u, f))))
            {
                var gone = ships.Where(s => !u.Fights.IsEngaged(s.Id)).OrderByDescending(s => s.Id.Value).FirstOrDefault();
                if (gone is not null)
                {
                    ships.Remove(gone);
                    Departures.RemoveShip(u, gone, DepartureReason.Unprofitable, "decommissioned");
                    u.Events.Publish(new WarshipDecommissioned(f, gone.Id, belowReserve ? "reserve" : "peace"));
                }
            }
            // Runde 24: eine Startszenario-Sperre (LastForceTick in der Zukunft, ScenarioDef.PoliceHoldTicks) gilt auch für Kriegsschiffe; im normalen Spiel nie in der Zukunft
            else if (ships.Count < desired && u.Tick >= actor.LastForceTick && Commission(u, f) is { } bought) ships.Add(bought);
            foreach (var s in ships) Command(u, f, s);
        }
    }

    /// <summary>Seit dem letzten Frieden der Fraktion ist genug Zeit vergangen (ohne Friedensschluss: sofort).</summary>
    static bool PeaceLongEnough(Universe u, FactionId f)
    {
        if (Wars.CountOf(u, f) > 0) return true;   // Überzahl im Krieg (Obergrenze gesenkt): abbauen
        long last = long.MinValue;
        foreach (var (k, t) in u.PeaceSince) if (k.A == f.Value || k.B == f.Value) last = Math.Max(last, t);
        return last == long.MinValue || u.Tick - last >= u.Rules.WarshipPeaceTicks;
    }

    /// <summary>Muss das Kriegsschiff zur Versorgung heim (Hülle, Munition/Raketen, Modulschaden)?</summary>
    public static bool NeedsBase(Universe u, Ship s) =>
        s.Hull < u.Rules.WarshipRetreatHull * s.MaxHull
        || u.Rules.AmmoActive && s.Stats.MissileCapacity > 0f && s.Systems.MissilesMissing >= s.Stats.MissileCapacity - 0.5f
        || u.Rules.DamageActive && NpcRepair.Crippled(u, s);

    static void Command(Universe u, FactionId f, Ship s)
    {
        if (u.Fights.IsEngaged(s.Id) || s.Emergency) return;
        if (s.HomeBase.Value == 0 || !u.Stations.ContainsKey(s.HomeBase))
            if (SecuritySystem.HomeBaseFor(u, f, s.Sector) is { } h) s.HomeBase = h.Id;
        if (s.Command is ReturnCommand) return;
        // Runde 40: während eines Waffenstillstands im Krieg keine Angriffe (wie Frieden), die Kriegsschiffe bleiben aber stationiert
        bool ceasefire = Wars.CountOf(u, f) > 0 && u.Wars.Any(w => w.Involves(f) && Wars.CeasefireActive(u, w));
        if (NeedsBase(u, s) || Wars.CountOf(u, f) == 0 || ceasefire)
        {
            bool atHome = s.Transit is null && s.DockedAt == s.HomeBase;
            if (!atHome || NeedsBase(u, s)) Commands.Give(u, s, new ReturnCommand(), out _, ignoreCapacity: true);
            else if (s.Command is not null) Commands.Cancel(u, s, "Frieden");
            return;
        }
        // Ausbau Punkt 2: gezielte Abwehr — ein angegriffenes eigenes Ziel (Station, Tor) in Reichweite geht dem eigenen Angriff und dem Geleit vor
        if (Combat.WarNavy.DefenseSectorFor(u, s) is { } defend)
        {
            if (!(s.Command is PatrolCommand dpc && dpc.Sectors.Count == 1 && dpc.Sectors[0] == defend))
            {
                Commands.Give(u, s, new PatrolCommand(new[] { defend }), out _, ignoreCapacity: true);
                u.Events.Publish(new Combat.WarDefenseAssigned(s.Id, f, defend, Combat.WarNavy.DefenseTargetName(u, s) ?? ""));
            }
            return;
        }
        // Wirtschaftsstationen halten stand: ein Alarm einer eigenen/verbündeten Wirtschaftsstation in Reichweite geht dem eigenen Angriff vor
        if (Combat.EconomicHoldout.AlarmSectorFor(u, s) is { } alarm)
        {
            if (!(s.Command is PatrolCommand apc && apc.Sectors.Count == 1 && apc.Sectors[0] == alarm))
                Commands.Give(u, s, new PatrolCommand(new[] { alarm }), out _, ignoreCapacity: true);
            return;
        }
        // Ausbau Punkt 2: Geleitschutz — ein laufendes Geleit bleibt; ein beendetes (Ankunft, Frieden, Höchstdauer) gibt das Schiff frei
        if (s.Command is EscortCommand ec)
        {
            if (Combat.WarNavy.EscortStillValid(u, s, ec)) return;
            Commands.Cancel(u, s, "Geleit beendet");
        }
        if (s.Command is AssaultCommand ac && AssaultExecutor.TargetOf(u, ac) is { } cur && AssaultExecutor.StillTarget(u, s, cur)) return;
        if (Combat.WarNavy.EscortWard(u, f, s) is { } ward && Commands.Give(u, s, new EscortCommand(ward.Id), out _, ignoreCapacity: true))
        {
            u.Events.Publish(new EscortAssigned(s.Id, ward.Id, ThreatMap.Where(u, ward).Sector));
            return;
        }
        // Phase 31: zuerst die Torblockade des Kriegs (höchstens eine je Krieg, nie kartenteilend, höchstens GateMaxDown weltweit), sonst eine Station
        if (Wars.PickGateTarget(u, f, s) is { } gate)
        {
            if (!Wars.BlockadeRunning(u, f, gate) && Wars.WarOverGate(u, f, gate) is { } war) war.Blockades++;
            Commands.Give(u, s, new AssaultCommand(gate.Sector, default, gate.Id), out _, ignoreCapacity: true);
            return;
        }
        if (Wars.PickTarget(u, f, s) is { } target) Commands.Give(u, s, new AssaultCommand(target.Sector, target.Id), out _, ignoreCapacity: true);
    }

    /// <summary>Kauft ein Kriegsschiff (gemeinsame Kaufregel) an einer erreichbaren Werft, nur aus dem verfügbaren Geld über der Rücklage. null = kein Kauf.</summary>
    public static Ship? Commission(Universe u, FactionId f)
    {
        var r = u.Rules;
        if (r.WarshipChoosesModels)
        {
            // Phase 29: Wahl unter allen Kampfmodellen, im Krieg voll auf Kampfstärke gewichtet
            if (!u.Actors.TryGetValue(f, out var a) || SecuritySystem.HomeBaseFor(u, f, null) is not { } h
                || CombatModels.AtYard(u, h.Sector, FactionViability.Spendable(u, a), 1f) is not { } pick) return null;
            var q = ShipQualityRoll.Roll(u, pick.Class, pick.Class.Slots, pick.Model.Fit, pick.Yard, pick.Needs);
            ShipPurchase.Consume(pick.Yard, q.Materials ?? pick.Needs);
            pick.Yard.Credits += pick.Price;
            a.Treasury.Credits -= pick.Price;
            var w = ShipPurchase.Create(u, u.NewShipId(), pick.Model.Key, q.Slots, q.Fit, ShipRole.Warship, Controller.Ai, a.Id, pick.Yard, 0f, q.Grade);
            w.HomeBase = h.Id;
            w.JobSince = u.Tick;
            u.AddShip(w);
            u.Events.Publish(new TreasuryUpkeep(a.Id, "navy", pick.Price, 0f));
            u.Events.Publish(new WarshipCommissioned(f, w.Id, pick.Yard.Id, pick.Price));
            return w;
        }
        if (!u.Actors.TryGetValue(f, out var actor) || !u.ShipModels.TryGetValue(r.WarshipModel, out var model)
            || !u.ShipClasses.TryGetValue(model.ClassKey, out var cls) || ShipPurchase.Materials(u, model) is not { } needs) return null;
        var home = SecuritySystem.HomeBaseFor(u, f, null);
        if (home is null) return null;
        foreach (var yard in u.StationsById().Where(st => !st.IsBuildSite && ShipPurchase.SellsModel(u, st, model)).ToList())
        {
            if (!u.IsReachable(yard.Sector, home.Sector) || ShipPurchase.Missing(yard, needs).Count > 0) continue;
            float price = ShipPurchase.Price(u, yard, needs, ShipPurchase.AssemblyOf(u, yard));
            if (FactionViability.Spendable(u, actor) < price) return null;
            ShipPurchase.Consume(yard, needs);
            yard.Credits += price;
            actor.Treasury.Credits -= price;
            var ship = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, ShipRole.Warship, Controller.Ai, actor.Id, yard, 0f);
            ship.HomeBase = home.Id;
            ship.JobSince = u.Tick;
            u.AddShip(ship);
            u.Events.Publish(new TreasuryUpkeep(actor.Id, "navy", price, 0f));
            u.Events.Publish(new WarshipCommissioned(f, ship.Id, yard.Id, price));
            return ship;
        }
        return null;
    }
}
