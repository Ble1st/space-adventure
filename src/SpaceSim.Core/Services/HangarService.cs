using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Services;

/// <summary>
/// Phase 12, Dienst „hangar“ (SCHIFFSSLOTS.md: angedockte Schiffe ansehen, wechseln, Befehle geben, nach Hause schicken, verkaufen; Liegegebühr nur an
/// fremden Stationen). Angebote je eigenem Schiff, das hier angedockt liegt: <c>switch:&lt;ShipId&gt;</c> (übernehmen), <c>home:&lt;ShipId&gt;</c>
/// (Befehl Heimkehr), <c>sell:&lt;ShipId&gt;</c> (Verkauf an die Station). Fremde Schiffe zeigt <see cref="Hangar.ForeignDocked"/> (nur Anzeige).
/// Strenge Schlüssel; <see cref="ServicePerformed"/> nur bei Erfolg.
/// </summary>
public sealed class HangarService : IStationService
{
    public const string ServiceKey = "hangar";
    public const string Switch = "switch";
    public const string Home = "home";
    public const string SellShip = "sell";

    public string Key => ServiceKey;
    public bool IsAvailable(Universe u, Station station) => !station.IsBuildSite;

    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var list = new List<ServiceOffer>();
        foreach (var own in PlayerFleet.ParkedAt(u, station))
        {
            string name = OrderMove.ShipName(u, own.Id);
            list.Add(new ServiceOffer($"{Switch}:{own.Id.Value}", $"In {name} wechseln", 0f, 1f, default, 0f, 0f));
            bool homeOk = ReturnExecutor.Home(u, own) is { } h && h.Id != station.Id && Commands.Check(u, own, new ReturnCommand()) is null;
            list.Add(new ServiceOffer($"{Home}:{own.Id.Value}", $"{name} nach Hause schicken", 0f, homeOk ? 1f : 0f, default, 0f, 0f));
            list.Add(new ServiceOffer($"{SellShip}:{own.Id.Value}", $"{name} verkaufen", Hangar.SellPrice(u, own),
                Hangar.SellIssue(u, station, own) is null ? 1f : 0f, default, 0f, 0f));
        }
        return list;
    }

    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (!TryParse(request.Offer, out var action, out var id)) return Fail($"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station)) return Fail("Kein Hangar an dieser Station.");
        if (!ship.IsPlayer) return Fail("Nur für eigene Schiffe.");
        if (!u.Ships.TryGetValue(id, out var target) || !target.IsPlayer) return Fail("Das Schiff gehört dir nicht.");
        if (!PlayerFleet.ParkedAt(u, station).Contains(target)) return Fail("Das Schiff liegt nicht an dieser Station.");
        switch (action)
        {
            case Switch:
                if (!PlayerFleet.SwitchTo(u, station, id, out var msg)) return Fail(msg);
                u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Switch, default, 1f, 0f));
                return new ServiceResult(true, 1f, 0f, msg);
            case Home:
                if (ReturnExecutor.Home(u, target) is { } h && h.Id == station.Id) return Fail("Das Schiff ist schon zu Hause.");
                if (!Commands.Give(u, target, new ReturnCommand(), out var hm)) return Fail(hm);
                u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Home, default, 1f, 0f));
                return new ServiceResult(true, 1f, 0f, $"{OrderMove.ShipName(u, id)}: {hm}");
            default:
                float price = Hangar.SellPrice(u, target);
                if (!Hangar.Sell(u, station, target, out var sm)) return Fail(sm);
                u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, SellShip, default, 1f, price));
                return new ServiceResult(true, 1f, price, sm);
        }
    }

    static ServiceResult Fail(string m) => new(false, 0f, 0f, m);

    /// <summary>Strenge Zerlegung: "switch|home|sell:&lt;positive Zahl&gt;".</summary>
    internal static bool TryParse(string offer, out string action, out ShipId id)
    {
        bool ok = Keys.TryParse(offer, new[] { Switch, Home, SellShip }, out action, out var v);
        id = new ShipId(v);
        return ok;
    }
}

/// <summary>Gemeinsame strenge Zerlegung der Angebots-Schlüssel "aktion:&lt;positive Ganzzahl&gt;" (Phase-12-Dienste).</summary>
internal static class Keys
{
    public static bool TryParse(string offer, IReadOnlyList<string> actions, out string action, out int value)
    {
        action = ""; value = 0;
        int sep = offer.IndexOf(':');
        if (sep <= 0) return false;
        string head = offer[..sep], tail = offer[(sep + 1)..];
        if (!actions.Contains(head) || tail.Length == 0 || tail.Length > 9 || tail.Any(c => c is < '0' or > '9')) return false;
        if (!int.TryParse(tail, out value) || value <= 0) { value = 0; return false; }
        action = head;
        return true;
    }
}
