using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Services;

/// <summary>Kaufangebot eines Schiffsmodells an einer Station: Werte, Preis, Material und was davon im Lager fehlt.</summary>
public sealed record ShipQuote(ShipModel Model, ShipClass Class, ShipStats Stats, float Price, IReadOnlyList<MaterialNeed> Materials,
    IReadOnlyList<MaterialNeed> Missing, bool Affordable)
{
    /// <summary>Material vollständig im Lager und bezahlbar.</summary>
    public bool Buyable => Missing.Count == 0 && Affordable;
}

/// <summary>
/// Dienst „shipdealer“ (Schiffskauf, Phase 5c): verkauft Schiffsmodelle, deren Klasse der Stationstyp montiert
/// (<see cref="StationType.ShipDealer"/>: Klassen, Höchstgröße, Montagegebühr — die Werft alle Klassen, die Handelsstation nur Skiffs).
/// Der Kauf folgt der gemeinsamen Regel <see cref="ShipPurchase"/>: Bausatz der Klasse plus je Modul des Werks-Fits eine Modul-Ware aus
/// dem Stationslager; Preis = Marktpreis × (1 + Montagegebühr), Zahlung aus der Spielerkasse an die Station. Das neue Schiff gehört dem
/// Spieler (Steuerer Player) und wird sofort das aktive Schiff; das bisherige bleibt als Spielerbesitz an der Station geparkt
/// (<see cref="PlayerFleet.SwitchTo"/>). Angebots-Schlüssel streng „buy:&lt;Modellschlüssel&gt;“.
/// </summary>
public sealed class ShipDealerService : IStationService
{
    public const string ServiceKey = "shipdealer";
    /// <summary>Angebots-Schlüssel Kauf „buy:&lt;Modellschlüssel&gt;“.</summary>
    public const string Buy = "buy";

    public string Key => ServiceKey;

    /// <summary>Verfügbar, wenn der Stationstyp einen Schiffshändler hat und mindestens ein Modell dieses Universums dazu passt.</summary>
    public bool IsAvailable(Universe u, Station station) => Models(u, station).Count > 0;

    /// <summary>Die Modelle, die diese Station verkaufen darf, nach Modellschlüssel (ordinal).</summary>
    public static IReadOnlyList<ShipModel> Models(Universe u, Station station)
    {
        if (!u.StationTypes.TryGetValue(station.TypeKey, out var t) || t.ShipDealer is not { } cfg) return Array.Empty<ShipModel>();
        return u.ShipModels.Values
            .Where(m => u.ShipClasses.TryGetValue(m.ClassKey, out var c) && cfg.Allows(c))
            .OrderBy(m => m.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>Modellschlüssel aus einem Angebots-Schlüssel „buy:&lt;Modell&gt;“ (null, wenn keiner).</summary>
    public static string? ModelKeyOf(string offerKey) =>
        offerKey.StartsWith(Buy + ":", StringComparison.Ordinal) && offerKey.Length > Buy.Length + 1 ? offerKey[(Buy.Length + 1)..] : null;

    /// <summary>Angebot eines Modells an dieser Station (rein); null, wenn die Station es nicht verkauft oder Material nicht auflösbar ist.</summary>
    public static ShipQuote? Quote(Universe u, Station station, Ship buyer, string modelKey)
    {
        if (!Models(u, station).Any(m => m.Key == modelKey)) return null;
        var model = u.ShipModels[modelKey];
        var cls = u.ShipClasses[model.ClassKey];
        var needs = ShipPurchase.Materials(u, model);
        if (needs is null) return null;
        var stats = ShipStats.Compute(cls, model.Fit.Select(k => k is not null && u.Modules.TryGetValue(k, out var m) ? m : null).ToList());
        float price = ShipPurchase.Price(u, station, needs, ShipPurchase.AssemblyOf(u, station));
        return new ShipQuote(model, cls, stats, price, needs, ShipPurchase.Missing(station, needs), u.WalletOf(buyer).Credits >= price);
    }

    /// <summary>
    /// Je verkaufbarem Modell ein Angebot „buy:&lt;Modell&gt;“: Text = Name und Klasse, Price = Kaufpreis, Amount = 1, wenn Material vollständig
    /// vorrätig und bezahlbar (sonst 0; fehlendes Material über <see cref="Quote"/>), Good = Bausatz, Stock = Bausatzbestand, Held = 0.
    /// </summary>
    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>();
        foreach (var model in Models(u, station))
        {
            if (Quote(u, station, ship, model.Key) is not { } q) continue;
            var kit = u.FindGood(ShipPurchase.KitKeyOf(u, q.Class))!.Id;   // Materials hat den Bausatz aufgelöst
            offers.Add(new ServiceOffer($"{Buy}:{model.Key}", $"{model.Name} ({q.Class.Name})", q.Price, q.Buyable ? 1f : 0f,
                kit, station.Market.TryGetValue(kit, out var e) ? e.Amount : 0f, 0f));
        }
        return offers;
    }

    /// <summary>
    /// Kauft das Modell: verbraucht Bausatz und Module aus dem Stationslager, bucht den Preis von der Spielerkasse an die Station, setzt das
    /// neue Spielerschiff an den Andockpunkt und macht es zum aktiven Schiff. Nur Spielerschiffe kaufen hier (die KI kauft über
    /// <see cref="FleetSystem"/> mit derselben Regel). Fehlschlag (unbekannter Schlüssel, Modell hier nicht verkauft, Material fehlt,
    /// zu wenig Credits) ändert nichts und löst kein Event aus. Erfolg: <see cref="ServicePerformed"/>, <see cref="ShipPurchased"/>,
    /// <see cref="ActiveShipChanged"/>.
    /// </summary>
    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        var modelKey = ModelKeyOf(request.Offer);
        if (modelKey is null || !u.ShipModels.ContainsKey(modelKey)) return Fail($"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station)) return Fail($"Dienst '{Key}' ist an dieser Station nicht verfügbar.");
        if (!ship.IsPlayer) return Fail("Nur der Spieler kauft hier Schiffe.");
        if (Quote(u, station, ship, modelKey) is not { } q) return Fail("Dieses Modell wird hier nicht verkauft.");
        if (q.Missing.Count > 0)
            return Fail("Nicht lieferbar, im Lager fehlt: " + string.Join(", ", q.Missing.Select(m => $"{m.Units:0}× {u.Goods[m.Good].Name}")) + ".");
        var wallet = u.WalletOf(ship);
        if (!q.Affordable) return Fail($"Nicht genügend Credits ({q.Price:0} Cr nötig).");

        // Phase 7: Güteklasse würfeln — bessere Module, Bonusmodul und Slot-Material kommen zusätzlich aus dem Werftlager (der Preis bleibt)
        var quality = ShipQualityRoll.Roll(u, q.Class, q.Class.Slots, q.Model.Fit, station, q.Materials);
        ShipPurchase.Consume(station, quality.Materials ?? q.Materials);
        station.Credits += q.Price;
        wallet.Credits -= q.Price;
        var id = u.NewShipId();
        var bought = ShipPurchase.Create(u, id, modelKey, quality.Slots, quality.Fit, ShipRole.Player, Controller.Player, u.PlayerWallet.Faction, station, 0f, quality.Grade);
        u.AddShip(bought);
        u.Events.Publish(new ShipQualityRolled(id, quality.Rolled, quality.Grade));
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Buy, u.FindGood(ShipPurchase.KitKeyOf(u, q.Class))!.Id, 1f, -q.Price));
        u.Events.Publish(new ShipPurchased(id, station.Id, modelKey, q.Price));
        // Das gekaufte Schiff wird sofort aktiv; das bisherige bleibt geparkt an dieser Station
        if (ship.Id == u.PlayerShip) PlayerFleet.SwitchTo(u, station, id, out _);
        string grade = quality.Grade == ShipQuality.Standard ? "" : $" Güteklasse {ShipQualityNames.Name(quality.Grade)}!";
        return new ServiceResult(true, 1f, -q.Price,
            $"{q.Model.Name} gekauft für {q.Price:0} Cr –{grade} du fliegst jetzt Schiff #{id.Value}; #{ship.Id.Value} bleibt hier im Hangar.");
    }

    static ServiceResult Fail(string message) => new(false, 0f, 0f, message);
}
