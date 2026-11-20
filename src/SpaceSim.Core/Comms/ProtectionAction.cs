using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Comms;

/// <summary>
/// Ausbau Punkt 3: Comms-Aktion „protection“ — Schutzvertrag mit einer KI-Fraktion schließen/kündigen (<see cref="Protection"/>). Schlüssel streng:
/// <c>protection:buy:&lt;FactionId&gt;</c>, <c>protection:end:&lt;FactionId&gt;</c>.
/// </summary>
public sealed class ProtectionAction : ICommsAction
{
    public const string KeyId = "protection";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (!u.Rules.ProtectionContracts || u.PlayerFaction is null) return list;
        foreach (var f in u.NpcFactionIds())
        {
            if (Protection.Of(u, f) is not null) list.Add(Preview(u, $"{KeyId}:end:{f.Value}"));
            else if (Protection.StandingIssue(u, f) is null) list.Add(Preview(u, $"{KeyId}:buy:{f.Value}"));
        }
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        float fee = u.Rules.ProtectionFeePerDay;
        if (CommsActions.TryFaction(offer, $"{KeyId}:buy", out var f))
        {
            string name = ActorNames.Tagged(u, new ActorId(f.Value));
            string? issue = u.Factions.ContainsKey(f) ? Protection.BuyIssue(u, f) : "Fraktion unbekannt.";
            return new(offer, $"Schutzvertrag – {name}",
                issue ?? $"{fee:0} Cr je Tag im Voraus. Ihre Polizei hilft deinen Schiffen in ihrem Gebiet sicher und gibt Geleit; endet bei Krieg, Fehde, Feindschaft oder leerer Kasse.",
                -fee, issue is null, issue);
        }
        if (CommsActions.TryFaction(offer, $"{KeyId}:end", out var g))
        {
            string name = ActorNames.Tagged(u, new ActorId(g.Value));
            string? issue = Protection.Of(u, g) is null ? "Kein Vertrag." : null;
            return new(offer, $"Schutzvertrag kündigen – {name}", issue ?? "Bezahlte Tage verfallen.", 0f, issue is null, issue);
        }
        return new(offer, "Schutzvertrag", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (CommsActions.TryFaction(offer, $"{KeyId}:buy", out var f))
        {
            if (!u.Factions.ContainsKey(f)) return new ServiceResult(false, 0f, 0f, "Fraktion unbekannt.");
            if (Protection.BuyIssue(u, f) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
            float fee = u.Rules.ProtectionFeePerDay;
            Protection.Buy(u, f);
            string msg = $"Schutzvertrag mit {ActorNames.Tagged(u, new ActorId(f.Value))} geschlossen ({fee:0} Cr je Tag).";
            u.Events.Publish(new CommsActionPerformed(KeyId, f.Value, -fee, msg));
            return new ServiceResult(true, 1f, -fee, msg);
        }
        if (CommsActions.TryFaction(offer, $"{KeyId}:end", out var g))
        {
            if (Protection.Of(u, g) is null) return new ServiceResult(false, 0f, 0f, "Kein Vertrag.");
            Protection.End(u, g, "cancelled");
            string msg = $"Schutzvertrag mit {ActorNames.Tagged(u, new ActorId(g.Value))} gekündigt.";
            u.Events.Publish(new CommsActionPerformed(KeyId, g.Value, 0f, msg));
            return new ServiceResult(true, 1f, 0f, msg);
        }
        return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
    }
}
