using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
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
/// Runde 41: Comms-Aktion „mission“ — Schlüssel <c>mission:accept:&lt;id&gt;</c>. Nimmt ein Auftragsangebot über Comms an
/// (remote — ohne Andocken; dieselbe Regel wie am Dock über <see cref="MissionRules.Accept"/>: Belohnung reserviert aus
/// <see cref="FactionViability.Spendable"/>, Pfand gehalten). Angebote kommen aus <see cref="MissionBoardSystem"/> (Comms-Kanal =
/// Liefervertrag, Posteingang-Nachricht <c>contract_offer</c>).
/// </summary>
public sealed class MissionAcceptAction : ICommsAction
{
    public const string KeyId = "mission";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(Prefix, StringComparison.Ordinal);
    const string Prefix = KeyId + ":accept:";

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (!u.Rules.MissionsActive || !u.Ships.TryGetValue(u.PlayerShip, out var ship)) return list;
        foreach (var o in u.MissionOffers.Where(o => o.Args.TryGetValue("channel", out var ch) && ch == "comms").OrderBy(o => o.Id))
        {
            string? issue = MissionRules.AcceptIssue(u, o.Id, ship, remote: true);
            var type = MissionRules.Types.Get(o.Type);
            list.Add(new CommsOffer($"{KeyId}:accept:{o.Id}", $"Auftrag annehmen: {type?.DescribeOffer(u, o) ?? o.Type}",
                issue ?? $"Belohnung {o.Reward:0} Cr reserviert · Pfand {o.Deposit:0} Cr · Frist {(type?.DeadlineOf(u, o, ship) ?? MissionRules.DeadlineFrom(u, ship, u.Stations[o.At]) - u.Tick) / 240} min · Ziel {type?.TargetText(u, o)}",
                -o.Deposit, issue is null, issue));
        }
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!CommsActions.TryId(offer, Prefix.TrimEnd(':'), out var id)) return new(offer, "Auftrag annehmen", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        if (!u.Ships.TryGetValue(u.PlayerShip, out var ship)) return new(offer, "Auftrag annehmen", "", 0f, false, "Kein aktives Schiff.");
        var o = u.MissionOffers.FirstOrDefault(x => x.Id == id);
        if (o is null) return new(offer, "Auftrag annehmen", "Angebot unbekannt oder abgelaufen.", 0f, false, "Angebot unbekannt.");
        var type = MissionRules.Types.Get(o.Type);
        string? issue = MissionRules.AcceptIssue(u, id, ship, remote: true);
        return new(offer, $"Auftrag annehmen: {type?.DescribeOffer(u, o) ?? o.Type}",
            issue ?? $"Belohnung {o.Reward:0} Cr · Pfand {o.Deposit:0} Cr · Ziel {type?.TargetText(u, o) ?? u.Stations[o.At].Name}.",
            -o.Deposit, issue is null, issue);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!CommsActions.TryId(offer, Prefix.TrimEnd(':'), out var id)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        if (!u.Ships.TryGetValue(u.PlayerShip, out var ship)) return new ServiceResult(false, 0f, 0f, "Kein aktives Schiff.");
        var (m, issue) = MissionRules.Accept(u, id, ship, remote: true);
        if (m is null) return new ServiceResult(false, 0f, 0f, issue ?? "Annahme nicht möglich.");
        var type = MissionRules.Types.Get(m.Type)!;
        string msg = $"Auftrag angenommen: {type.Describe(u, m)}";
        u.Events.Publish(new CommsActionPerformed(KeyId, m.Client.Value, -(m.Deposit), msg));
        return new ServiceResult(true, 1f, -m.Deposit, msg);
    }
}

/// <summary>
/// Runde 41: Comms-Aktion „mission_abandon“ — Schlüssel <c>mission:abandon:&lt;id&gt;</c>. Bricht einen aktiven Auftrag ab
/// (auch ohne Andocken, z. B. aus dem Reiter „Aufträge“ der Comms): Folge wie Scheitern — Pfand verfällt an die Station,
/// Reservierung zurück in die Auftraggeber-Kasse, Beziehung −, Sperrfrist. Ausführung nur über <see cref="MissionRules.Abandon"/>.
/// </summary>
public sealed class MissionAbandonAction : ICommsAction
{
    public const string KeyId = "mission_abandon";
    const string Prefix = "mission:abandon:";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(Prefix, StringComparison.Ordinal);

    static bool TryParse(string offer, out int id)
    {
        id = 0;
        if (!offer.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        string tail = offer[Prefix.Length..];
        return tail.Length > 0 && tail.All(c => c is >= '0' and <= '9') && int.TryParse(tail, out id) && id > 0;
    }

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (!u.Rules.MissionsActive || !u.Ships.TryGetValue(u.PlayerShip, out var ship)) return list;
        foreach (var m in u.Missions.Where(m => m.State == Model.MissionState.Active).OrderBy(m => m.Id))
            list.Add(PreviewFor(u, ship, m));
        return list;
    }

    CommsOffer PreviewFor(Universe u, Ship ship, Model.Mission m)
    {
        string? issue = ship.Id != u.PlayerShip ? "Nur mit dem aktiven Schiff möglich." : null;   // rein — Abandon selbst nicht ausführen
        return new CommsOffer($"{Prefix}{m.Id}", $"Auftrag abbrechen: #{m.Id}",
            issue ?? $"Pfand {m.Deposit:0} Cr verfällt · Belohnung {m.Reserved:0} Cr zurück in die Kasse · Beziehung −{u.Rules.MissionRelationLoss:0.00} · {u.Rules.MissionBlockTicks / 240} min keine neuen Aufträge.",
            0f, issue is null, issue);
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!TryParse(offer, out var id)) return new(offer, "Auftrag abbrechen", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        if (!u.Ships.TryGetValue(u.PlayerShip, out var ship)) return new(offer, "Auftrag abbrechen", "", 0f, false, "Kein aktives Schiff.");
        var m = u.Missions.FirstOrDefault(x => x.Id == id && x.State == Model.MissionState.Active);
        if (m is null) return new(offer, "Auftrag abbrechen", "Kein laufender Auftrag mit dieser Id.", 0f, false, "Kein laufender Auftrag.");
        return PreviewFor(u, ship, m);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!TryParse(offer, out var id)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        if (!u.Ships.TryGetValue(u.PlayerShip, out var ship)) return new ServiceResult(false, 0f, 0f, "Kein aktives Schiff.");
        var (ok, issue) = MissionRules.Abandon(u, id, ship);
        if (!ok) return new ServiceResult(false, 0f, 0f, issue ?? "Abbruch nicht möglich.");
        var m = u.Missions.FirstOrDefault(x => x.Id == id);
        string msg = $"Auftrag #{id} abgebrochen — Pfand verfällt, Reservierung zurück.";
        u.Events.Publish(new CommsActionPerformed(KeyId, m?.Client.Value ?? 0, 0f, msg));
        return new ServiceResult(true, 1f, 0f, msg);
    }
}

/// <summary>
/// Runde 41: Comms-Aktion „contract“ — Schlüssel <c>contract:haggle:&lt;id&gt;</c>. Liefervertrag einmal nachverhandeln:
/// +<see cref="EconomyRules.ContractHaggleShare"/> auf den Festpreis, nur wenn der heutige Preis das deckt und die Beziehung
/// mindestens <see cref="EconomyRules.ContractHaggleRelation"/> ist (deterministisch, <see cref="MissionRules.Haggle"/>).
/// </summary>
public sealed class ContractHaggleAction : ICommsAction
{
    public const string KeyId = "contract";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(Prefix, StringComparison.Ordinal);
    const string Prefix = KeyId + ":haggle:";

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (!u.Rules.MissionsActive) return list;
        foreach (var o in u.MissionOffers.Where(o => o.Args.TryGetValue("contract", out var c) && c == "1" && !o.Haggled).OrderBy(o => o.Id))
        {
            var (_, issue, _) = MissionRules.Haggle(u, o.Id);
            list.Add(new CommsOffer($"{KeyId}:haggle:{o.Id}", $"Liefervertrag nachverhandeln (#{o.Id})",
                issue ?? $"+{u.Rules.ContractHaggleShare * 100:0} % auf den Festpreis — einmal je Angebot.", 0f, issue is null, issue));
        }
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!CommsActions.TryId(offer, Prefix.TrimEnd(':'), out var id)) return new(offer, "Vertrag nachverhandeln", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        var o = u.MissionOffers.FirstOrDefault(x => x.Id == id);
        if (o is null) return new(offer, "Vertrag nachverhandeln", "Angebot unbekannt.", 0f, false, "Angebot unbekannt.");
        var (ok, issue, newPrice) = MissionRules.Haggle(u, id);
        return new(offer, $"Liefervertrag #{id} nachverhandeln",
            issue ?? $"Neuer Festpreis {newPrice:0.00} Cr (+{u.Rules.ContractHaggleShare * 100:0} %) — gilt bei Annahme.", 0f, ok, issue);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!CommsActions.TryId(offer, Prefix.TrimEnd(':'), out var id)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        var (ok, issue, newPrice) = MissionRules.Haggle(u, id);
        if (!ok) return new ServiceResult(false, 0f, 0f, issue ?? "Nicht möglich.");
        string msg = $"Liefervertrag nachverhandelt: neuer Festpreis {newPrice:0.00} Cr (+{u.Rules.ContractHaggleShare * 100:0} %).";
        u.Events.Publish(new CommsActionPerformed(KeyId, u.MissionOffers.FirstOrDefault(o => o.Id == id)?.Client.Value ?? 0, 0f, msg));
        return new ServiceResult(true, 1f, 0f, msg);
    }
}
