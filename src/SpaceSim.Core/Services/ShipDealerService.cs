using System.Linq;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Services;

/// <summary>Kaufangebot eines Schiffsmodells an einer Station: Werte, Preis, Material und was davon im Lager fehlt.</summary>
public sealed record ShipQuote(ShipModel Model, ShipClass Class, ShipStats Stats, float Price, IReadOnlyList<MaterialNeed> Materials,
    IReadOnlyList<MaterialNeed> Missing, bool Affordable, string? Locked = null, ShipBlueprint? Blueprint = null)
{
    /// <summary>Der Fit, der gekauft wird (Werks-Fit des Modells oder der Fit des Bauplans).</summary>
    public IReadOnlyList<string?> Fit => Blueprint?.Fit ?? Model.Fit;
    /// <summary>Erforscht (Phase 13), Material vollständig im Lager und bezahlbar.</summary>
    public bool Buyable => Locked is null && Missing.Count == 0 && Affordable;
}

/// <summary>
/// Dienst „shipdealer“ (Schiffskauf, Phase 5c): verkauft Schiffsmodelle, deren Klasse der Stationstyp montiert
/// (<see cref="StationType.ShipDealer"/>: Klassen, Höchstgröße, Montagegebühr — die Werft alle Klassen, die Handelsstation nur Skiffs).
/// Der Kauf folgt der gemeinsamen Regel <see cref="ShipPurchase"/>: Bausatz der Klasse plus je Modul des Werks-Fits eine Modul-Ware aus
/// dem Stationslager; Preis = Marktpreis × (1 + Montagegebühr), Zahlung aus der Spielerkasse an die Station. Das neue Schiff gehört dem
/// Spieler (Steuerer Player), liegt fertig angedockt am Andockpunkt (<see cref="Ship.DockedAt"/> dieser Station) und wird NICHT automatisch
/// bestiegen — die Person bleibt im bisherigen aktiven Schiff (oder zu Fuß) und übernimmt das gekaufte Schiff über den Hangar
/// (<see cref="PlayerFleet.SwitchTo"/>). Angebots-Schlüssel streng „buy:&lt;Modellschlüssel&gt;“.
/// </summary>
public sealed class ShipDealerService : IStationService
{
    public const string ServiceKey = "shipdealer";
    /// <summary>Angebots-Schlüssel Kauf „buy:&lt;Modellschlüssel&gt;“.</summary>
    public const string Buy = "buy";
    /// <summary>Angebots-Schlüssel Bauplan-Kauf „blueprint:&lt;Id&gt;“ (Baupläne des Spielers, <see cref="Blueprints"/>).</summary>
    public const string BlueprintKey = "blueprint";

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
        return QuoteFit(u, station, buyer, model, u.ShipClasses[model.ClassKey], model.Fit, null);
    }

    /// <summary>Bauplan-Id aus „blueprint:&lt;Id&gt;“ (streng: nur Ziffern, &gt; 0); 0 = keiner.</summary>
    public static int BlueprintIdOf(string offerKey)
    {
        if (!offerKey.StartsWith(BlueprintKey + ":", StringComparison.Ordinal)) return 0;
        var t = offerKey[(BlueprintKey.Length + 1)..];
        return t.Length > 0 && t.Length < 9 && t.All(c => c is >= '0' and <= '9') && int.TryParse(t, out int id) ? id : 0;
    }

    /// <summary>
    /// Angebot eines Bauplans an dieser Station (rein): gleiche Kaufregel wie ein Modell (Bausatz + Module des Bauplans aus dem Lager, Preis, Rückkauf-
    /// Spiegelboden), die Werft muss die Klasse verkaufen; gesperrt, wenn Modell oder ein Modul für die Fraktion der Werft nicht erforscht ist.
    /// null = Bauplan unbekannt, Klasse hier nicht verkauft oder Material nicht auflösbar.
    /// </summary>
    public static ShipQuote? QuoteBlueprint(Universe u, Station station, Ship buyer, int blueprintId)
    {
        if (Blueprints.Find(u, blueprintId) is not { } bp || !u.ShipModels.TryGetValue(bp.ModelKey, out var model)) return null;
        if (!Models(u, station).Any(m => m.Key == model.Key)) return null;
        var cls = u.ShipClasses[model.ClassKey];
        if (bp.Fit.Count != cls.Slots.Count || FitRules.Check(u.SlotTypes, u.Modules, cls, cls.Slots, bp.Fit) is not null) return null;
        return QuoteFit(u, station, buyer, model, cls, bp.Fit, bp);
    }

    static ShipQuote? QuoteFit(Universe u, Station station, Ship buyer, ShipModel model, ShipClass cls, IReadOnlyList<string?> fit, ShipBlueprint? bp)
    {
        var needs = ShipPurchase.Materials(u, cls, fit);
        if (needs is null) return null;
        var stats = ShipStats.Compute(cls, fit.Select(k => k is not null && u.Modules.TryGetValue(k, out var m) ? m : null).ToList());
        float assembly = ShipPurchase.AssemblyOf(u, station);
        float price = ShipPurchase.Price(u, station, needs, assembly);
        // Runde 31 (Prüfung: erst ein Modul teuer an die Werft verkaufen, dann ein Schiff kaufen, das es verbraucht, +455 Cr je Zyklus): Material, das der
        // Spieler hier verkauft hat, zählt beim Schiffskauf mindestens zum erhaltenen Preis (Spiegelposten, PlayerLedger.BuyCost)
        if (u.Rules.PlayerShipPricing && buyer.IsPlayer)
        {
            // Ausgleich (Punkt 6, „Schiffspreise wie im decomp“): Materialpreis bzw. Mindestpreis je Kampfstärke × (1 + Händleraufschlag ± Ansehen), auf 250 gerundet
            float surcharge = 0f;
            foreach (var n in needs) surcharge += PlayerLedger.MaterialSurcharge(u, buyer, station, n.Good, n.Units);
            price = ShipPricing.PlayerPrice(u, station, buyer.Owner, ShipPurchase.Price(u, station, needs, 0f), stats, surcharge);
        }
        else
            foreach (var n in needs) price += PlayerLedger.MaterialSurcharge(u, buyer, station, n.Good, n.Units) * (1f + MathF.Max(0f, assembly));
        // Phase 13: gesperrte Modelle bleiben sichtbar („nicht erforscht“), sind aber nicht kaufbar; Baupläne auch mit gesperrtem Modul
        string? locked = Research.LockText(u, station, UnlockKind.Model, model.Key);
        if (locked is null && bp is not null)
            foreach (var k in fit)
                if (k is not null && Research.LockText(u, station, UnlockKind.Module, k) is { } ml) { locked = ml; break; }
        return new ShipQuote(model, cls, stats, price, needs, ShipPurchase.Missing(station, needs), u.WalletOf(buyer).Credits >= price, locked, bp);
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
        // Baupläne des Spielers (nur Spielerschiffe; KI kauft nie hier)
        if (ship.IsPlayer)
            foreach (var bp in Blueprints.Of(u).OrderBy(b => b.Id))
            {
                if (QuoteBlueprint(u, station, ship, bp.Id) is not { } q) continue;
                var kit = u.FindGood(ShipPurchase.KitKeyOf(u, q.Class))!.Id;
                offers.Add(new ServiceOffer($"{BlueprintKey}:{bp.Id}", $"Bauplan „{bp.Name}“ ({q.Model.Name})", q.Price, q.Buyable ? 1f : 0f,
                    kit, station.Market.TryGetValue(kit, out var e) ? e.Amount : 0f, 0f));
            }
        return offers;
    }

    /// <summary>
    /// Kauft das Modell: verbraucht Bausatz und Module aus dem Stationslager, bucht den Preis von der Spielerkasse an die Station und setzt das
    /// neue Spielerschiff an den Andockpunkt (angedockt, geparkt). Das aktive Schiff wechselt dabei nicht (kein automatisches Einsteigen —
    /// auch nicht zu Fuß), dafür gibt es den Hangar-Dienst. Nur Spielerschiffe kaufen hier (die KI kauft über
    /// <see cref="FleetSystem"/> mit derselben Regel). Fehlschlag (unbekannter Schlüssel, Modell hier nicht verkauft, Material fehlt,
    /// zu wenig Credits) ändert nichts und löst kein Event aus. Erfolg: <see cref="ServicePerformed"/>, <see cref="ShipPurchased"/>.
    /// </summary>
    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (BlueprintIdOf(request.Offer) is int bpId and > 0) return ExecuteBlueprint(u, station, ship, bpId);
        var modelKey = ModelKeyOf(request.Offer);
        if (modelKey is null || !u.ShipModels.ContainsKey(modelKey)) return Fail($"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station)) return Fail($"Dienst '{Key}' ist an dieser Station nicht verfügbar.");
        if (!ship.IsPlayer) return Fail("Nur der Spieler kauft hier Schiffe.");
        if (Quote(u, station, ship, modelKey) is not { } q) return Fail("Dieses Modell wird hier nicht verkauft.");
        if (q.Locked is { } locked) return Fail($"{q.Model.Name}: {locked}.");
        if (q.Missing.Count > 0)
            return Fail("Nicht lieferbar, im Lager fehlt: " + string.Join(", ", q.Missing.Select(m => $"{m.Units:0}× {u.Goods[m.Good].Name}")) + ".");
        var wallet = u.WalletOf(ship);
        if (!q.Affordable) return Fail($"Nicht genügend Credits ({q.Price:0} Cr nötig).");

        // Phase 7: Güteklasse würfeln — bessere Module, Bonusmodul und Slot-Material kommen zusätzlich aus dem Werftlager (der Preis bleibt)
        var quality = ShipQualityRoll.Roll(u, q.Class, q.Class.Slots, q.Model.Fit, station, q.Materials);
        var mats = quality.Materials ?? q.Materials;
        var before = mats.Select(m => (m.Good, Stock: station.Market[m.Good].Amount, Price: station.Market[m.Good].Price)).ToList();
        ShipPurchase.Consume(station, mats);
        // Runde 30: die verbrauchten Module/Bausätze zählen als Kauf hier (Rückkaufdeckel) — sonst würde ein Modul nach dem Schiffskauf beim Ausrüster teurer
        // an dieselbe Werft zurückverkauft (ihr Regal ist durch den Kauf leerer, der Preis höher)
        foreach (var (m, b) in mats.Zip(before)) PlayerLedger.OnBought(u, ship, station, m.Good, m.Units, b.Price, b.Stock);
        station.Credits += q.Price;
        wallet.Credits -= q.Price;
        var id = u.NewShipId();
        var bought = ShipPurchase.Create(u, id, modelKey, quality.Slots, quality.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, station, 0f, quality.Grade);
        bought.HomeBase = station.Id;   // Phase 12: Heimatbasis = Kaufort (Heimkehr, Verband)
        u.AddShip(bought);
        u.Events.Publish(new ShipQualityRolled(id, quality.Rolled, quality.Grade));
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Buy, u.FindGood(ShipPurchase.KitKeyOf(u, q.Class))!.Id, 1f, -q.Price));
        u.Events.Publish(new ShipPurchased(id, station.Id, modelKey, q.Price));
        // Der Kauf steigt nicht selbst ein: Das gekaufte Schiff liegt fertig am Andockpunkt der Station (im Hangar); die Person bleibt, wo sie ist
        // (im bisherigen aktiven Schiff oder zu Fuß in der Station). Übernehmen über den Hangar (Dienst "hangar").
        string grade = quality.Grade == ShipQuality.Standard ? "" : $" Güteklasse {ShipQualityNames.Name(quality.Grade)}!";
        return new ServiceResult(true, 1f, -q.Price,
            $"{q.Model.Name} gekauft für {q.Price:0} Cr –{grade} Schiff #{id.Value} liegt hier im Hangar.");
    }

    /// <summary>
    /// Kauft ein Schiff nach Bauplan: gleiche Kaufregel wie ein Modell (Material aus dem Lager, Preis an die Station, Rückkaufposten), aber ohne
    /// Güteklassen-Wurf (der Bauplan legt die Module fest; Standard). Fehlschlag ändert nichts und publiziert nichts.
    /// </summary>
    ServiceResult ExecuteBlueprint(Universe u, Station station, Ship ship, int bpId)
    {
        if (!IsAvailable(u, station)) return Fail($"Dienst '{Key}' ist an dieser Station nicht verfügbar.");
        if (!ship.IsPlayer) return Fail("Nur der Spieler kauft hier Schiffe.");
        if (Blueprints.Find(u, bpId) is null) return Fail("Bauplan unbekannt.");
        if (QuoteBlueprint(u, station, ship, bpId) is not { } q) return Fail("Diese Werft baut diesen Bauplan nicht (Klasse nicht im Angebot).");
        if (q.Locked is { } locked) return Fail($"{q.Blueprint!.Name}: {locked}.");
        if (q.Missing.Count > 0)
            return Fail("Nicht lieferbar, im Lager fehlt: " + string.Join(", ", q.Missing.Select(m => $"{m.Units:0}× {u.Goods[m.Good].Name}")) + ".");
        if (!q.Affordable) return Fail($"Nicht genügend Credits ({q.Price:0} Cr nötig).");
        var wallet = u.WalletOf(ship);
        var before = q.Materials.Select(m => (Stock: station.Market[m.Good].Amount, Price: station.Market[m.Good].Price)).ToList();
        ShipPurchase.Consume(station, q.Materials);
        foreach (var (m, b) in q.Materials.Zip(before)) PlayerLedger.OnBought(u, ship, station, m.Good, m.Units, b.Price, b.Stock);
        station.Credits += q.Price;
        wallet.Credits -= q.Price;
        var id = u.NewShipId();
        var bought = ShipPurchase.Create(u, id, q.Model.Key, q.Class.Slots, q.Blueprint!.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, station, 0f);
        bought.HomeBase = station.Id;
        u.AddShip(bought);
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, BlueprintKey, u.FindGood(ShipPurchase.KitKeyOf(u, q.Class))!.Id, 1f, -q.Price));
        u.Events.Publish(new ShipPurchased(id, station.Id, q.Model.Key, q.Price));
        u.Events.Publish(new BlueprintPurchased(id, station.Id, bpId, q.Price));
        return new ServiceResult(true, 1f, -q.Price, $"Bauplan „{q.Blueprint.Name}“ gebaut für {q.Price:0} Cr – Schiff #{id.Value} liegt hier im Hangar.");
    }

    static ServiceResult Fail(string message) => new(false, 0f, 0f, message);
}
