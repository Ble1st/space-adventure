using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Comms;

/// <summary>
/// Söldner per Comms (<see cref="Mercenaries"/>). Schlüssel streng: <c>merc:hire:&lt;ActorId&gt;:&lt;Tage&gt;</c> (Anbieter Gilde oder Unabhängiger,
/// Tage aus <see cref="EconomyRules.MercenaryDays"/>), <c>merc:end:&lt;ShipId&gt;</c> (Vertrag vorzeitig beenden, bezahlte Tage verfallen).
/// </summary>
public sealed class MercenaryAction : ICommsAction
{
    public const string KeyId = "merc";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (!u.Rules.Mercenaries) return list;
        foreach (var c in u.Mercenaries.Where(c => !c.Ending)) list.Add(Preview(u, $"{KeyId}:end:{c.Ship.Value}"));
        foreach (var a in Mercenaries.Providers(u))
        {
            var f = a.IsFaction(u) ? new FactionId(a.Id.Value) : a.Membership;
            if (f is { } kf && !Intel.Knowledge.KnowsFaction(u, kf)) continue;
            foreach (int d in u.Rules.MercenaryDays) list.Add(Preview(u, $"{KeyId}:hire:{a.Id.Value}:{d}"));
        }
        return list;
    }

    /// <summary>Zerlegt „merc:hire:&lt;Akteur&gt;:&lt;Tage&gt;“ streng.</summary>
    public static bool TryHire(string offer, out ActorId actor, out int days)
    {
        actor = default; days = 0;
        var p = offer.Split(':');
        if (p.Length != 4 || p[0] != KeyId || p[1] != "hire" || !Digits(p[2]) || !Digits(p[3])) return false;
        if (!int.TryParse(p[2], out int a) || !int.TryParse(p[3], out days) || a <= 0 || days <= 0) return false;
        actor = new ActorId(a);
        return true;
    }

    static bool TryEnd(string offer, out ShipId ship)
    {
        ship = default;
        var p = offer.Split(':');
        if (p.Length != 3 || p[0] != KeyId || p[1] != "end" || !Digits(p[2]) || !int.TryParse(p[2], out int s) || s <= 0) return false;
        ship = new ShipId(s);
        return true;
    }

    static bool Digits(string s) => s.Length is > 0 and < 9 && s.All(c => c is >= '0' and <= '9');

    public CommsOffer Preview(Universe u, string offer)
    {
        if (TryHire(offer, out var actor, out int days))
        {
            string name = u.Actors.ContainsKey(actor) ? ActorNames.Tagged(u, actor) : "?";
            float fee = u.Rules.MercenaryFeePerDay;
            string model = u.ShipModels.TryGetValue(u.Rules.MercenaryModel, out var m) ? m.Name : u.Rules.MercenaryModel;
            string? issue = Mercenaries.HireIssue(u, actor, days);
            string where = issue is null && u.Actors.TryGetValue(actor, out var a) && Mercenaries.Spawn(u, a) is { } st ? $" Startet an {StationNames.Short(u, st)}." : "";
            return new(offer, $"Söldner – {name} ({days} Tag{(days == 1 ? "" : "e")})",
                issue ?? $"{model} begleitet dein aktives Schiff und kämpft mit. {fee:0} Cr je Tag im Voraus (gesamt {fee * days:0} Cr); endet ohne Schulden, wenn die Kasse leer ist.{where}",
                -fee, issue is null, issue);
        }
        if (TryEnd(offer, out var ship))
        {
            var c = Mercenaries.Of(u, ship);
            string? issue = c is null || c.Ending ? "Kein laufender Vertrag." : null;
            return new(offer, c is null ? "Söldner entlassen" : $"Söldner entlassen – {Mercenaries.Text(u, c)}", issue ?? "Bezahlte Tage verfallen; das Schiff fliegt heim.", 0f, issue is null, issue);
        }
        return new(offer, "Söldner", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (TryHire(offer, out var actor, out int days))
        {
            if (Mercenaries.Hire(u, actor, days, out var msg) is null) return new ServiceResult(false, 0f, 0f, msg);
            float fee = u.Rules.MercenaryFeePerDay;
            u.Events.Publish(new CommsActionPerformed(KeyId, actor.Value, -fee, msg));
            return new ServiceResult(true, 1f, -fee, msg);
        }
        if (TryEnd(offer, out var ship))
        {
            if (Mercenaries.Of(u, ship) is not { Ending: false } c) return new ServiceResult(false, 0f, 0f, "Kein laufender Vertrag.");
            Mercenaries.End(u, c, "cancelled");
            string msg = $"Söldner #{ship.Value} entlassen – fliegt heim.";
            u.Events.Publish(new CommsActionPerformed(KeyId, c.Actor.Value, 0f, msg));
            return new ServiceResult(true, 1f, 0f, msg);
        }
        return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
    }
}
