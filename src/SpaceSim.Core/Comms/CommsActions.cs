using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Comms;

/// <summary>Runde 40: Hilfsfunktionen der Comms-Aktionen (Namen, Sperrprüfungen).</summary>
static class CommsHelp
{
    public static string FactionName(Universe u, FactionId f) => u.Factions.TryGetValue(f, out var fac) ? fac.Name : $"Fraktion {f.Value}";

    /// <summary>Läuft gerade eine Festnahmewarnung oder ein Gefecht gegen die Fraktion <paramref name="f"/> (Sperre der Fernzahlung)?</summary>
    public static bool BusyWith(Universe u, FactionId f)
    {
        if (u.Ships.TryGetValue(u.PlayerShip, out var ps))
        {
            if (u.PendingEncounters.Any(x => x.Target == ps.Id && x.Kind is EncounterKind.Intercept or EncounterKind.War
                && u.Ships.TryGetValue(x.Source, out var src) && u.FactionOf(src) == f)) return true;
            if (u.Fights.Of(ps.Id) is { } e)
                foreach (var p in e.Participants)
                    if (p.Active && u.Ships.TryGetValue(p.Ship, out var s) && s.Owner != u.PlayerActor && u.FactionOf(s) == f) return true;
        }
        return false;
    }
}

/// <summary>
/// Runde 40: Comms-Aktion „fine“ — Strafe per Comms zahlen (fern), nur an die betroffene Fraktion selbst (keine Rechtshilfe) mit
/// <see cref="EconomyRules.CommsFineMarkup"/> (10 %) Aufschlag auf <see cref="Wanted.FineOf"/>; Geld an die Fraktionskasse, Eintrag gelöscht
/// (wie das Gericht, <see cref="CourtService"/>). Schlüssel streng: <c>fine:&lt;FactionId&gt;</c>. Gesperrt während eines Gefechts mit der Fraktion
/// oder einer laufenden Festnahmewarnung.
/// </summary>
public sealed class FineAction : ICommsAction
{
    public const string KeyId = "fine";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    /// <summary>Zu zahlender Betrag (Strafe + Fernzahlungsaufschlag, aufgerundet).</summary>
    public static float CostOf(Universe u, WantedRecord w) => MathF.Ceiling(Wanted.FineOf(u, w) * (1f + u.Rules.CommsFineMarkup));

    /// <summary>Warum die Strafe an <paramref name="f"/> jetzt nicht zahlbar ist (null = zahlbar). Rein.</summary>
    public static string? Issue(Universe u, FactionId f)
    {
        if (!u.Actors.TryGetValue(u.PlayerActor, out var me) || !me.Wanted.TryGetValue(f, out var w)) return "Keine Strafe offen.";
        if (CommsHelp.BusyWith(u, f)) return "Im Gefecht oder Festnahmewarnung bei dieser Fraktion — Strafe am Gericht/Ergeben dort.";
        float cost = CostOf(u, w);
        if (u.Player.Treasury.Credits < cost) return $"Kasse reicht nicht ({cost:0} Cr nötig).";
        return null;
    }

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (!u.Actors.TryGetValue(u.PlayerActor, out var me)) return list;
        foreach (var (f, w) in me.Wanted.OrderBy(kv => kv.Key.Value))
        {
            if (!u.IsNpcFaction(f)) continue;
            float cost = CostOf(u, w);
            string? issue = Issue(u, f);
            list.Add(new CommsOffer($"{KeyId}:{f.Value}", $"Strafe zahlen – {CommsHelp.FactionName(u, f)}",
                issue ?? $"{cost:0} Cr an die Kasse von {CommsHelp.FactionName(u, f)} (+10 % Fernzahlung) — Fahndung gelöscht.",
                -cost, issue is null, issue));
        }
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!CommsActions.TryFaction(offer, KeyId, out var f)) return new(offer, "Strafe zahlen", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        if (!u.Actors.TryGetValue(u.PlayerActor, out var me) || !me.Wanted.TryGetValue(f, out var w))
            return new(offer, $"Strafe – {CommsHelp.FactionName(u, f)}", "Keine Strafe offen.", 0f, false, "Keine Strafe offen.");
        float cost = CostOf(u, w);
        string? issue = Issue(u, f);
        int level = Wanted.Level(u.Rules, w.Heat);
        return new(offer, $"Strafe zahlen – {CommsHelp.FactionName(u, f)}",
            issue ?? $"{cost:0} Cr an die Kasse von {CommsHelp.FactionName(u, f)} (+10 % Fernzahlung statt Gericht) — Fahndung „{SecurityNames.WantedLevel(level)}“ gelöscht.",
            -cost, issue is null, issue);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!CommsActions.TryFaction(offer, KeyId, out var f)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        if (Issue(u, f) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
        var me = u.Actors[u.PlayerActor];
        var w = me.Wanted[f];
        float cost = CostOf(u, w);
        u.Player.Treasury.Credits -= cost;
        if (u.Actors.TryGetValue(f, out var authority))
        {
            authority.Treasury.Credits += cost;
            u.Events.Publish(new TreasuryIncome(authority.Id, "fine", cost));
        }
        Wanted.SetHeat(u, me, f, 0f, 0f);
        u.Events.Publish(new FinePaid(u.PlayerActor, f, cost));
        string msg = $"Strafe {cost:0} Cr an {CommsHelp.FactionName(u, f)} gezahlt — Fahndung gelöscht.";
        u.Events.Publish(new CommsActionPerformed(KeyId, f.Value, -cost, msg));
        return new ServiceResult(true, 1f, -cost, msg);
    }
}

/// <summary>
/// Runde 40: Comms-Aktion „war“ — der Spieler erklärt einer KI-Fraktion den Krieg (zwei Taps in der Ansicht). Kein Kriegsgrund nötig;
/// Bedingungen und Folgen siehe <see cref="Wars.PlayerDeclareIssue"/>/<see cref="Wars.PlayerDeclare"/> (Kriegskasse 15 000 Cr als Bedingung,
/// Abkühlzeit nach Frieden). Schlüssel streng: <c>war:&lt;FactionId&gt;</c>.
/// </summary>
public sealed class WarAction : ICommsAction
{
    public const string KeyId = "war";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        foreach (var f in u.NpcFactionIds())
        {
            if (!Wars.CanWage(u, f)) continue;
            string? issue = Wars.PlayerDeclareIssue(u, f);
            list.Add(new CommsOffer($"{KeyId}:{f.Value}", $"Krieg erklären – {CommsHelp.FactionName(u, f)}",
                issue ?? $"Krieg beginnt sofort — Beziehung −0,80, alle anderen KI-Fraktionen −{u.Rules.PlayerWarRelationLoss:0.00}; Sperren und Handelsverweigerung folgen. Kriegskasse {u.Rules.WarChest:0} Cr bleibt als Bedingung.",
                0f, issue is null, issue));
        }
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!CommsActions.TryFaction(offer, KeyId, out var f)) return new(offer, "Krieg erklären", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        string? issue = Wars.PlayerDeclareIssue(u, f);
        return new(offer, $"Krieg erklären – {CommsHelp.FactionName(u, f)}",
            issue ?? $"Krieg mit {CommsHelp.FactionName(u, f)} — Stationen verweigern Handel/Dienste/Andocken, ihre Polizei und Kriegsschiffe greifen deine Schiffe an; deine Angriffe auf sie sind kein Verbrechen.",
            0f, issue is null, issue);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!CommsActions.TryFaction(offer, KeyId, out var f)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        if (Wars.PlayerDeclareIssue(u, f) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
        Wars.PlayerDeclare(u, f);
        string msg = $"Du hast {CommsHelp.FactionName(u, f)} den Krieg erklärt.";
        u.Events.Publish(new CommsActionPerformed(KeyId, f.Value, 0f, msg));
        return new ServiceResult(true, 1f, 0f, msg);
    }
}

/// <summary>
/// Runde 40: Comms-Aktion „ceasefire“ — der Spieler bietet einer Kriegsgegnerin den Waffenstillstand an. Die KI entscheidet deterministisch
/// (<see cref="Wars.CeasefireFree"/>: Müdigkeit ≥ 0,4 oder klare Unterlegenheit → ohne Zahlung, sonst Tribut <see cref="Wars.TributeFor"/>).
/// Dauer <see cref="EconomyRules.CeasefireTicks"/> (12 000 Ticks); höchstens ein Angebot je <see cref="EconomyRules.OfferCooldownTicks"/>.
/// Schlüssel streng: <c>ceasefire:&lt;FactionId&gt;</c>.
/// </summary>
public sealed class CeasefireAction : ICommsAction
{
    public const string KeyId = "ceasefire";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    static IEnumerable<(FactionId F, War W)> PlayerWars(Universe u)
    {
        if (u.PlayerFaction is not { } me) yield break;
        foreach (var w in u.Wars)
            if (w.Involves(me) && u.IsNpcFaction(w.Other(me))) yield return (w.Other(me), w);
    }

    /// <summary>Vorschau-Text für ein Angebot an <paramref name="f"/> (rein).</summary>
    public static string PreviewText(Universe u, FactionId f, War w)
    {
        string name = CommsHelp.FactionName(u, f);
        if (Wars.CeasefireFree(u, w, f))
            return $"Die KI nimmt an — Waffenstillstand für {u.Rules.CeasefireTicks / 240} min ({u.Rules.CeasefireTicks} Ticks): keine Kriegsangriffe, Handel bleibt gesperrt.";
        float t = Wars.TributeFor(u, w, f);
        return t > 0f
            ? $"Die KI verlangt einen Tribut von {t:0} Cr — dann Waffenstillstand für {u.Rules.CeasefireTicks / 240} min."
            : $"Tribut bereits gedeckt — Waffenstillstand für {u.Rules.CeasefireTicks / 240} min ohne weitere Zahlung.";
    }

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        foreach (var (f, w) in PlayerWars(u))
        {
            string? issue = Wars.CeasefireIssue(u, f);
            float cost = Wars.CeasefireFree(u, w, f) ? 0f : Wars.TributeFor(u, w, f);
            list.Add(new CommsOffer($"{KeyId}:{f.Value}", $"Waffenstillstand – {CommsHelp.FactionName(u, f)}",
                issue ?? PreviewText(u, f, w), -cost, issue is null, issue));
        }
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!CommsActions.TryFaction(offer, KeyId, out var f)) return new(offer, "Waffenstillstand", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        if (Wars.PlayerWar(u, f) is not { } w) return new(offer, $"Waffenstillstand – {CommsHelp.FactionName(u, f)}", "Kein Krieg mit dieser Fraktion.", 0f, false, "Kein Krieg mit dieser Fraktion.");
        string? issue = Wars.CeasefireIssue(u, f);
        float cost = Wars.CeasefireFree(u, w, f) ? 0f : Wars.TributeFor(u, w, f);
        return new(offer, $"Waffenstillstand – {CommsHelp.FactionName(u, f)}", issue ?? PreviewText(u, f, w), -cost, issue is null, issue);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!CommsActions.TryFaction(offer, KeyId, out var f)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        if (Wars.CeasefireAccept(u, f) is not { } res) return new ServiceResult(false, 0f, 0f, Wars.CeasefireIssue(u, f) ?? "Nicht möglich.");
        string msg = res.Tribute > 0f
            ? $"Waffenstillstand mit {CommsHelp.FactionName(u, f)} bis Tick {res.War.CeasefireUntil} — Tribut {res.Tribute:0} Cr gezahlt."
            : $"Waffenstillstand mit {CommsHelp.FactionName(u, f)} bis Tick {res.War.CeasefireUntil}.";
        u.Events.Publish(new CommsActionPerformed(KeyId, f.Value, -res.Tribute, msg));
        return new ServiceResult(true, 1f, -res.Tribute, msg);
    }
}

/// <summary>
/// Runde 40: Comms-Aktion „peace“ — der Spieler schließt Frieden mit einer Kriegsgegnerin. Kostenlos bei Mindestdauer +
/// <see cref="EconomyRules.PeaceWeariness"/> (<see cref="Wars.PeaceFree"/>), sonst gegen Tribut (<see cref="Wars.TributeFor"/>), der in die
/// Fraktionskasse fließt. Schlüssel streng: <c>peace:&lt;FactionId&gt;</c>.
/// </summary>
public sealed class PeaceAction : ICommsAction
{
    public const string KeyId = "peace";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (u.PlayerFaction is not { } me) return list;
        foreach (var w in u.Wars)
        {
            if (!w.Involves(me) || !u.IsNpcFaction(w.Other(me))) continue;
            var f = w.Other(me);
            string? issue = Wars.PeaceIssue(u, f);
            float cost = Wars.PeaceFree(u, w, f) ? 0f : Wars.TributeFor(u, w, f);
            string prev = issue ?? (cost > 0f
                ? $"Die KI verlangt einen Tribut von {cost:0} Cr — dann Frieden (Beziehung mindestens {u.Rules.PlayerPeaceRelation:0.00}, Abkühlzeit {u.Rules.PeaceCooldownTicks / 240} min)."
                : $"Die KI nimmt an — Frieden mit {CommsHelp.FactionName(u, f)} (Beziehung mindestens {u.Rules.PlayerPeaceRelation:0.00}).");
            list.Add(new CommsOffer($"{KeyId}:{f.Value}", $"Frieden schließen – {CommsHelp.FactionName(u, f)}", prev, -cost, issue is null, issue));
        }
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!CommsActions.TryFaction(offer, KeyId, out var f)) return new(offer, "Frieden schließen", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        if (Wars.PlayerWar(u, f) is not { } w) return new(offer, $"Frieden – {CommsHelp.FactionName(u, f)}", "Kein Krieg mit dieser Fraktion.", 0f, false, "Kein Krieg mit dieser Fraktion.");
        string? issue = Wars.PeaceIssue(u, f);
        float cost = Wars.PeaceFree(u, w, f) ? 0f : Wars.TributeFor(u, w, f);
        string prev = issue ?? (cost > 0f
            ? $"Tribut {cost:0} Cr an die Kasse von {CommsHelp.FactionName(u, f)} — dann Frieden."
            : $"Frieden mit {CommsHelp.FactionName(u, f)} — Beziehung mindestens {u.Rules.PlayerPeaceRelation:0.00}, Abkühlzeit {u.Rules.PeaceCooldownTicks / 240} min.");
        return new(offer, $"Frieden schließen – {CommsHelp.FactionName(u, f)}", prev, -cost, issue is null, issue);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!CommsActions.TryFaction(offer, KeyId, out var f)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        if (Wars.PeaceAccept(u, f) is not { } tribute) return new ServiceResult(false, 0f, 0f, Wars.PeaceIssue(u, f) ?? "Nicht möglich.");
        string msg = tribute > 0f
            ? $"Frieden mit {CommsHelp.FactionName(u, f)} — Tribut {tribute:0} Cr an ihre Kasse gezahlt."
            : $"Frieden mit {CommsHelp.FactionName(u, f)}.";
        u.Events.Publish(new CommsActionPerformed(KeyId, f.Value, -tribute, msg));
        return new ServiceResult(true, 1f, -tribute, msg);
    }
}

/// <summary>
/// Runde 40: Comms-Aktion „tribute“ — der Spieler zahlt einer Kriegsgegnerin einen Tribut; er fließt in ihre Fraktionskasse
/// (<see cref="TreasuryIncome"/> „tribute“) und mindert den für Waffenstillstand/Frieden geforderten Betrag (<see cref="War.TributePaid"/>).
/// Schlüssel streng: <c>tribute:&lt;FactionId&gt;:&lt;Betrag&gt;</c>.
/// </summary>
public sealed class TributeAction : ICommsAction
{
    public const string KeyId = "tribute";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (u.PlayerFaction is not { } me) return list;
        foreach (var w in u.Wars)
        {
            if (!w.Involves(me) || !u.IsNpcFaction(w.Other(me))) continue;
            var f = w.Other(me);
            float t = Wars.TributeFor(u, w, f);
            if (t <= 0f) continue;
            int amount = (int)MathF.Ceiling(t);
            string? issue = Wars.TributeIssue(u, f, amount);
            list.Add(new CommsOffer($"{KeyId}:{f.Value}:{amount}", $"Tribut zahlen – {CommsHelp.FactionName(u, f)}",
                issue ?? $"{amount:0} Cr in die Kasse von {CommsHelp.FactionName(u, f)} — mindert den für Waffenstillstand/Frieden nötigen Tribut (noch {t:0} Cr offen).",
                -amount, issue is null, issue));
        }
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!CommsActions.TryFactionAmount(offer, KeyId, out var f, out var amount))
            return new(offer, "Tribut zahlen", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        string? issue = Wars.TributeIssue(u, f, amount);
        return new(offer, $"Tribut zahlen – {CommsHelp.FactionName(u, f)}",
            issue ?? $"{amount:0} Cr in die Kasse von {CommsHelp.FactionName(u, f)} — mindert den geforderten Tribut.",
            -amount, issue is null, issue);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!CommsActions.TryFactionAmount(offer, KeyId, out var f, out var amount))
            return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        if (Wars.TributeIssue(u, f, amount) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
        Wars.PayTribute(u, f, amount);
        return new ServiceResult(true, 1f, -amount, $"Tribut {amount:0} Cr an {CommsHelp.FactionName(u, f)} gezahlt.");
    }
}

/// <summary>
/// Runde 40: reservierter Platzhalter für Aufträge (Runde 41) — Schlüssel <c>mission:accept:&lt;id&gt;</c>. Liefert noch keine Angebote und
/// schlägt streng fehl, damit die Registry-Struktur (Schlüsselraum) schon steht.
/// </summary>
public sealed class MissionAcceptAction : ICommsAction
{
    public string Key => "mission";
    public bool Handles(string offer) => offer.StartsWith("mission:accept:", StringComparison.Ordinal);
    public IReadOnlyList<CommsOffer> Offers(Universe u) => Array.Empty<CommsOffer>();
    public CommsOffer Preview(Universe u, string offer) => new(offer, "Auftrag annehmen", "Aufträge kommen mit Runde 41.", 0f, false, "Noch nicht verfügbar (kommt mit den Aufträgen).");
    public ServiceResult Execute(Universe u, string offer) => new(false, 0f, 0f, "Noch nicht verfügbar (kommt mit den Aufträgen).");
}

/// <summary>
/// Runde 40: reservierter Platzhalter für Lieferverträge (Runde 41, PLAN Abschnitt 2.3) — Schlüssel <c>contract:haggle:&lt;id&gt;</c>.
/// </summary>
public sealed class ContractHaggleAction : ICommsAction
{
    public string Key => "contract";
    public bool Handles(string offer) => offer.StartsWith("contract:haggle:", StringComparison.Ordinal);
    public IReadOnlyList<CommsOffer> Offers(Universe u) => Array.Empty<CommsOffer>();
    public CommsOffer Preview(Universe u, string offer) => new(offer, "Vertrag nachverhandeln", "Lieferverträge kommen mit Runde 41.", 0f, false, "Noch nicht verfügbar (kommt mit den Lieferverträgen).");
    public ServiceResult Execute(Universe u, string offer) => new(false, 0f, 0f, "Noch nicht verfügbar (kommt mit den Lieferverträgen).");
}
