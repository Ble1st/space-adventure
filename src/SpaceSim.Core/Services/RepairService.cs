using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Services;

/// <summary>
/// Dienst „repair“ (Phase 4c): stellt die Hülle des Schiffs (<see cref="Ship.MaxHull"/>) wieder her und verbraucht dafür
/// Hüllenplatten (<see cref="PlatesGoodKey"/>) aus dem Stationslager — Bedarf = fehlende Hülle ×
/// <see cref="EconomyRules.RepairPlatesPerHull"/>. Bezahlt wird nur ein Arbeitsaufschlag in Credits:
/// Plattenmarktpreis × Bedarf × <see cref="LaborFactor"/>. Angebote mit strikten Schlüsseln: „repair:full“ und
/// „repair:half“; reicht der Plattenbestand nicht, wird nur so viel repariert, wie die Platten hergeben
/// (Teilreparatur mit entsprechender Meldung). Verfügbar nur an Stationen, die Hüllenplatten im Markt führen und
/// Bestand &gt; 0 haben. In <see cref="ServiceOffer"/>/<see cref="ServiceResult"/> sind Amount/Units reparierte
/// Hüllenpunkte, Price der Preis je Hüllenpunkt.
/// </summary>
public sealed class RepairService : IStationService
{
    public const string ServiceKey = "repair";
    /// <summary>Warenschlüssel der Hüllenplatten (Inhaltsverweis wie <see cref="EconomyRules.ShipKitGoodKey"/>).</summary>
    public const string PlatesGoodKey = "hull_plates";
    /// <summary>Angebots-Schlüssel „repair:full“: Hülle voll wiederherstellen.</summary>
    public const string Full = "full";
    /// <summary>Angebots-Schlüssel „repair:half“: Hälfte der fehlenden Hülle wiederherstellen.</summary>
    public const string Half = "half";
    /// <summary>Arbeitsaufschlag auf den Plattenpreis: Plattenmarktpreis × Bedarf × Faktor.</summary>
    public const float LaborFactor = 1.5f;

    public string Key => ServiceKey;

    /// <summary>Die Hüllenplatten-Ware dieses Universums, sonst null (Welt ohne „hull_plates“ hat keinen Reparaturdienst).</summary>
    public static GoodId? PlatesGood(Universe u) => u.FindGood(PlatesGoodKey)?.Id;

    /// <summary>Verfügbar, wenn die Station Hüllenplatten im Markt führt und Bestand &gt; 0 sowie Preis &gt; 0 hat.</summary>
    public bool IsAvailable(Universe u, Station station)
    {
        var good = PlatesGood(u);
        return good is { } g && station.Market.TryGetValue(g, out var e) && e.Amount > 0f && e.Price > 0f;
    }

    /// <summary>Preis je repariertem Hüllenpunkt: Plattenmarktpreis × Plattenbedarf je Punkt × <see cref="LaborFactor"/>.</summary>
    static float PricePerHull(Universe u, Station station, GoodId good) =>
        station.Market[good].Price * u.Rules.RepairPlatesPerHull * LaborFactor;

    /// <summary>Hüllenpunkte, die mit dem aktuellen Plattenbestand höchstens repariert werden können (gedeckelt auf <paramref name="wantHull"/>).</summary>
    static float ByStock(Universe u, Station station, GoodId good, float wantHull)
    {
        float perHull = u.Rules.RepairPlatesPerHull;
        if (perHull <= 0f) return wantHull;   // Regel 0: keine Platten nötig
        return MathF.Min(wantHull, station.Market[good].Amount / perHull);
    }

    /// <summary>Angebote „repair:full“/„repair:half“; leer, wenn das Schiff unbeschädigt ist oder die Station keine Platten führt.</summary>
    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>(2);
        var good = PlatesGood(u);
        if (good is null || !station.Market.TryGetValue(good.Value, out var e) || e.Price <= 0f) return offers;
        float missing = ship.MaxHull - ship.Hull;
        if (missing <= 0.5f) return offers;   // unbeschädigt: nichts anzubieten
        float pricePerHull = PricePerHull(u, station, good.Value);
        foreach (var (key, label, share) in new[] { (Full, "Hülle voll reparieren", 1f), (Half, "Hülle halb reparieren", 0.5f) })
        {
            float amount = ByStock(u, station, good.Value, missing * share);
            if (pricePerHull > 0f) amount = MathF.Min(amount, u.WalletOf(ship).Credits / pricePerHull);
            offers.Add(new ServiceOffer($"{ServiceKey}:{key}", label, pricePerHull, MathF.Max(0f, amount), good.Value, e.Amount, 0f));
        }
        return offers;
    }

    /// <summary>
    /// Führt ein Angebot aus: verbraucht Platten aus dem Stationslager, zahlt der Station den Arbeitsaufschlag und
    /// stellt die Hülle wieder her; veröffentlicht <see cref="ServicePerformed"/> nur bei Erfolg (mindestens ein
    /// Hüllenpunkt repariert). Unbekannte Schlüssel, nicht verfügbarer Dienst, unbeschädigtes Schiff oder fehlende
    /// Platten/Credits ändern keinen Zustand und lösen kein Event aus.
    /// </summary>
    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        bool full;
        if (request.Offer == $"{ServiceKey}:{Full}") full = true;
        else if (request.Offer == $"{ServiceKey}:{Half}") full = false;
        else return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station))
            return new ServiceResult(false, 0f, 0f, $"Dienst '{Key}' ist an dieser Station nicht verfügbar.");
        var good = PlatesGood(u)!.Value;
        float missing = ship.MaxHull - ship.Hull;
        if (missing <= 0.5f)
            return new ServiceResult(false, 0f, 0f, "Schiff ist unbeschädigt.");

        float perHull = u.Rules.RepairPlatesPerHull;
        float want = missing * (full ? 1f : 0.5f);
        float amount = ByStock(u, station, good, want);
        float pricePerHull = PricePerHull(u, station, good);
        if (pricePerHull > 0f) amount = MathF.Min(amount, u.WalletOf(ship).Credits / pricePerHull);
        if (amount <= 0.0001f)
            return new ServiceResult(false, 0f, 0f,
                station.Market[good].Amount <= 0f ? "Keine Hüllenplatten mehr auf Lager." : "Nicht genügend Credits für die Reparatur.");

        float plates = amount * perHull;
        float cost = plates * station.Market[good].Price * LaborFactor;
        station.Market[good].Amount -= plates;   // Platten gehen aus dem Stationslager ab
        station.Credits += cost;
        u.WalletOf(ship).Credits -= cost;   // Spieler: Spielerkasse, NPC: Schiff
        ship.Hull = MathF.Min(ship.MaxHull, ship.Hull + amount);
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, request.Offer, good, amount, -cost));
        bool partial = amount < want - 0.0001f;
        return new ServiceResult(true, amount, -cost,
            $"Hülle +{amount:0.#} auf {ship.Hull:0.#}/{ship.MaxHull:0} für {cost:0} Cr ({plates:0.#} Platten verbraucht)" +
            (partial ? " – Platten reichten nicht für mehr." : "."));
    }
}
