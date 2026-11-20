using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Comms;

/// <summary>
/// Ausbau Punkt 6: Comms-Aktion „feud“ — der Spieler erklärt oder beendet eine Fehde (<see cref="Feuds"/>; erklären nur mit Firmensitz,
/// <see cref="PlayerSeatsOffices.FeudUnlocked"/>). Schlüssel streng: <c>feud:declare:&lt;F&gt;</c>, <c>feud:end:&lt;F&gt;</c>.
/// </summary>
public sealed class FeudAction : ICommsAction
{
    public const string KeyId = "feud";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    static bool Parse(string offer, out string verb, out FactionId f)
    {
        verb = ""; f = default;
        foreach (var v in new[] { "declare", "end" })
            if (CommsActions.TryFaction(offer, $"{KeyId}:{v}", out f)) { verb = v; return true; }
        return false;
    }

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (u.PlayerFaction is not { } me || !u.Rules.PlayerFeuds) return list;
        foreach (var f in u.NpcFactionIds())
            if (Wars.CanWage(u, f)) list.Add(Preview(u, Feuds.InFeud(u, me, f) ? $"{KeyId}:end:{f.Value}" : $"{KeyId}:declare:{f.Value}"));
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!Parse(offer, out var verb, out var f)) return new(offer, "Fehde", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        var r = u.Rules;
        string name = CommsHelp.FactionName(u, f);
        if (verb == "declare")
        {
            string? issue = Feuds.PlayerDeclareIssue(u, f);
            return new(offer, $"Fehde erklären – {name}",
                issue ?? $"Fehde mit {name} für {Missions.MissionRules.Rest(r.FeudTicks)}: ihre Polizei überfällt deine Flottenschiffe in ihrem Gebiet, Sperren gegen dich; "
                         + $"Angriffe auf ihre Schiffe sind kein Verbrechen, ihre Stationen bleiben geschützt. Alle anderen Fraktionen −{r.FeudThirdPartyLoss:0.00}.",
                0f, issue is null, issue);
        }
        string? endIssue = Feuds.PlayerEndIssue(u, f);
        return new(offer, $"Fehde beenden – {name}", endIssue ?? $"Die Fehde mit {name} endet sofort; die Beziehung bleibt, wie sie ist.", 0f, endIssue is null, endIssue);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!Parse(offer, out var verb, out var f)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        string msg;
        if (verb == "declare")
        {
            if (Feuds.PlayerDeclareIssue(u, f) is { } i1) return new ServiceResult(false, 0f, 0f, i1);
            Feuds.PlayerDeclare(u, f);
            msg = $"Fehde mit {CommsHelp.FactionName(u, f)} erklärt.";
        }
        else
        {
            if (Feuds.PlayerEndIssue(u, f) is { } i2) return new ServiceResult(false, 0f, 0f, i2);
            Feuds.End(u, Feuds.Between(u, u.PlayerFaction!.Value, f)!, "ended");
            msg = $"Fehde mit {CommsHelp.FactionName(u, f)} beendet.";
        }
        u.Events.Publish(new CommsActionPerformed(KeyId, f.Value, 0f, msg));
        return new ServiceResult(true, 1f, 0f, msg);
    }
}

/// <summary>
/// Ausbau Punkt 6: Comms-Aktion „mediate“ — der Spieler vermittelt zwischen zwei KI-Fraktionen in Krieg oder Fehde (<see cref="Mediation.PlayerIssue"/>): beide zahlen
/// ihm eine Gebühr (höchstens <see cref="EconomyRules.MediationFee"/>, höchstens die Hälfte ihres verfügbaren Geldes), beide heben ihr Ansehen bei ihm; der Krieg
/// wird schneller müde, die Fehde endet. Schlüssel streng: <c>mediate:&lt;A&gt;:&lt;B&gt;</c>.
/// </summary>
public sealed class MediateAction : ICommsAction
{
    public const string KeyId = "mediate";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    static bool Parse(string offer, out FactionId a, out FactionId b)
    {
        a = default; b = default;
        if (!offer.StartsWith(KeyId + ":", StringComparison.Ordinal)) return false;
        var parts = offer[(KeyId.Length + 1)..].Split(':');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0 || !p.All(c => c is >= '0' and <= '9'))) return false;
        if (!int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y) || x <= 0 || y <= 0 || x == y) return false;
        a = new FactionId(x); b = new FactionId(y);
        return true;
    }

    public static string KeyFor(FactionId a, FactionId b) => a.Value < b.Value ? $"{KeyId}:{a.Value}:{b.Value}" : $"{KeyId}:{b.Value}:{a.Value}";

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (u.PlayerFaction is null || !u.Rules.PlayerMediation) return list;
        foreach (var c in Mediation.Conflicts(u)) list.Add(Preview(u, KeyFor(c.A, c.B)));
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!Parse(offer, out var a, out var b)) return new(offer, "Vermitteln", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        string text = $"Vermitteln – {CommsHelp.FactionName(u, a)} / {CommsHelp.FactionName(u, b)}";
        string? issue = Mediation.PlayerIssue(u, a, b);
        if (issue is not null) return new(offer, text, issue, 0f, false, issue);
        var c = Mediation.Find(u, a, b)!.Value;
        float fee = Mediation.FeeOf(u, a) + Mediation.FeeOf(u, b);
        var r = u.Rules;
        string effect = c.War is not null ? $"der Krieg wird schneller müde (+{r.MediationWearinessBonus:0.00})" : "die Fehde endet";
        return new(offer, text, $"Beide zahlen dir zusammen {fee:0} Cr, beide +{r.MediationRelationGain:0.00} Ansehen bei dir; {effect}.", fee, true, null);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!Parse(offer, out var a, out var b)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        if (Mediation.PlayerIssue(u, a, b) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
        float fee = Mediation.PlayerMediate(u, a, b);
        string msg = $"Vermittelt zwischen {CommsHelp.FactionName(u, a)} und {CommsHelp.FactionName(u, b)} — {fee:0} Cr Gebühr.";
        u.Events.Publish(new CommsActionPerformed(KeyId, a.Value, fee, msg));
        return new ServiceResult(true, 1f, fee, msg);
    }
}
