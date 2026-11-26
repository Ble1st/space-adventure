using System.Globalization;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Comms;

/// <summary>
/// Welle B (4): Spende an eine KI-Fraktion gegen Ansehen — Schlüssel „donate:&lt;Fraktion&gt;:&lt;Betrag&gt;“ (Betrag aus
/// <see cref="EconomyRules.DonationAmounts"/>). Regeln in <see cref="Donations"/>; Fehlschlag ändert nichts und meldet nichts.
/// </summary>
public sealed class DonationAction : ICommsAction
{
    public const string KeyId = "donate";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    /// <summary>Angebotsschlüssel für Fraktion und Betrag.</summary>
    public static string KeyFor(FactionId f, float amount) => $"{KeyId}:{f.Value}:{amount.ToString("0", CultureInfo.InvariantCulture)}";

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (!u.Rules.Donations || u.PlayerFaction is null) return list;
        foreach (var f in u.NpcFactionIds())
            if (!Bandits.IsEstablished(u, f))   // Punkt 17: Banditen führen keine Diplomatie
                foreach (var amount in u.Rules.DonationAmounts)
                    list.Add(Preview(u, KeyFor(f, amount)));
        return list;
    }

    /// <summary>Strenges Parsen „donate:&lt;Fraktion&gt;:&lt;Betrag&gt;“ (Betrag muss einer der erlaubten sein).</summary>
    static bool TryParse(Universe u, string offer, out FactionId f, out float amount)
    {
        f = default; amount = 0f;
        var parts = offer.Split(':');
        if (parts.Length != 3 || parts[0] != KeyId) return false;
        if (!CommsActions.TryFaction($"{KeyId}:{parts[1]}", KeyId, out f)) return false;
        if (parts[2].Length == 0 || !parts[2].All(char.IsAsciiDigit) || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var a)) return false;
        amount = a;
        float am = amount;
        return u.Rules.DonationAmounts.Any(x => MathF.Abs(x - am) < 0.5f);
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!TryParse(u, offer, out var f, out var amount)) return new(offer, "Spende", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        string name = u.Factions.ContainsKey(f) ? ActorNames.Tagged(u, new ActorId(f.Value)) : "?";
        string? issue = u.Factions.ContainsKey(f) ? Donations.Issue(u, f, amount) : "Fraktion unbekannt.";
        float gain = issue is null ? Donations.GainFor(u, f, amount) : 0f;
        return new(offer, $"Spende {amount:0} Cr – {name}",
            issue ?? $"Ansehen +{gain:0.00} (heute höchstens +{u.Rules.DonationDailyCap:0.00}, bis {u.Rules.DonationMaxRelation:0.00}). Das Geld geht in ihre Kasse.",
            -amount, issue is null, issue);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!TryParse(u, offer, out var f, out var amount)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        if (!u.Factions.ContainsKey(f)) return new ServiceResult(false, 0f, 0f, "Fraktion unbekannt.");
        if (Donations.Issue(u, f, amount) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
        float gain = Donations.Donate(u, f, amount);
        string msg = $"Spende an {ActorNames.Tagged(u, new ActorId(f.Value))}: {amount:0} Cr — Ansehen +{gain:0.00}.";
        u.Events.Publish(new Events.CommsActionPerformed(KeyId, f.Value, -amount, msg));
        return new ServiceResult(true, 1f, -amount, msg);
    }
}
