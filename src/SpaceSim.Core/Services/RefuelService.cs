using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Services;

/// <summary>
/// Dienst „refuel“ (Tankstelle, Phase 4c): verkauft den Treibstoff des Schiffs (<see cref="Refuel.ActiveFuel"/>) zum
/// Marktpreis der Station in den Tank — die Buchung läuft über <see cref="Refuel.BuyFuel"/> (Stationsbestand,
/// Stationskasse, Schiffscredits; der Treibstoff landet im Tank, nicht im Laderaum). Angebote mit strikten Schlüsseln:
/// „refuel:fill“ (volltanken, so viel Platz/Credits/Bestand erlauben) und „refuel:25“/„refuel:50“/„refuel:100“
/// (feste Stufen). Verfügbar an Stationen, die mindestens einen zulässigen Treibstoff (laut Antriebsmodulen) mit
/// Bestand und Preis führen. Der Spieler kauft auf dieser Schiene in Credits (Spielerkasse über Universe.WalletOf); die NPC-Logik (<see cref="Refuel.TankUp"/>)
/// bleibt unverändert.
/// </summary>
public sealed class RefuelService : IStationService
{
    public const string ServiceKey = "refuel";
    /// <summary>Angebots-Schlüssel „refuel:fill“: volltanken.</summary>
    public const string Fill = "fill";
    /// <summary>Feste Stufen der Angebots-Schlüssel „refuel:&lt;Menge&gt;“.</summary>
    public static readonly int[] Tiers = { 25, 50, 100 };

    public string Key => ServiceKey;

    /// <summary>
    /// Verfügbar, wenn die Station mindestens einen Treibstoff führt, den ein Antriebsmodul dieses Universums
    /// zulässt (Modul-Registry), mit Bestand &gt; 0 und Preis &gt; 0.
    /// </summary>
    public bool IsAvailable(Universe u, Station station)
    {
        if (!Refuel.Active(u)) return false;   // Phase 14: Treibstoff abgeschaltet → keine Tankstelle
        foreach (var m in u.Modules.Values)
            foreach (var f in m.FuelTypes)
                if (station.Market.TryGetValue(f.Good, out var e) && e.Amount > 0f && e.Price > 0f)
                    return true;
        return false;
    }

    /// <summary>Höchstmenge, die <see cref="Refuel.BuyFuel"/> jetzt liefern würde (Bestand, Credits, Tankplatz; ganze Einheiten).</summary>
    static float MaxFill(Universe u, Ship ship, StockEntry e) =>
        MathF.Max(0f, MathF.Min(MathF.Min(MathF.Floor(e.Amount), MathF.Floor(u.WalletOf(ship).Credits / e.Price)), MathF.Floor(ship.FuelSpace)));

    /// <summary>
    /// Angebote für den Treibstoff des Schiffs: „refuel:fill“ plus eine Zeile je fester Stufe. Price = Marktpreis je
    /// Einheit, Amount = jetzt lieferbare Menge (bei Stufen gedeckelt auf die Stufe), Stock = Stationsbestand,
    /// Held = Tankfüllstand des Schiffs für diese Ware. Leer, wenn die Station den Treibstoff des Schiffs nicht führt.
    /// </summary>
    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>(Tiers.Length + 1);
        var good = Refuel.ActiveFuel(ship);
        if (!Refuel.Active(u) || good is null || !station.Market.TryGetValue(good.Value, out var e) || e.Price <= 0f) return offers;
        string name = u.Goods[good.Value].Name;
        float held = ship.FuelAmount(good.Value);
        float max = MaxFill(u, ship, e);
        offers.Add(new ServiceOffer($"{ServiceKey}:{Fill}", $"Volltanken ({name})", e.Price, max, good.Value, e.Amount, held));
        foreach (int t in Tiers)
            offers.Add(new ServiceOffer($"{ServiceKey}:{t}", $"{t} {name} tanken", e.Price, MathF.Min(t, max), good.Value, e.Amount, held));
        return offers;
    }

    /// <summary>
    /// Führt ein Angebot über <see cref="Refuel.BuyFuel"/> aus und veröffentlicht <see cref="ServicePerformed"/> nur bei
    /// Erfolg (mindestens eine Einheit getankt). Unbekannte Schlüssel, ein nicht verfügbarer Dienst, voller Tank,
    /// Pleite oder leeres Lager ändern keinen Zustand und lösen kein Event aus.
    /// </summary>
    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (!TryParseOffer(request.Offer, out bool fill, out int tier))
            return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station))
            return new ServiceResult(false, 0f, 0f, $"Dienst '{Key}' ist an dieser Station nicht verfügbar.");
        var good = Refuel.ActiveFuel(ship);
        if (good is null)
            return new ServiceResult(false, 0f, 0f, "Dieses Schiff hat keinen Treibstoffantrieb.");
        if (!station.Market.TryGetValue(good.Value, out var e) || e.Price <= 0f)
            return new ServiceResult(false, 0f, 0f, $"An dieser Station wird {u.Goods[good.Value].Name} nicht verkauft.");
        if (ship.FuelSpace <= 0.0001f)
            return new ServiceResult(false, 0f, 0f, "Tank ist bereits voll.");
        if (e.Amount <= 0f)
            return new ServiceResult(false, 0f, 0f, "Kein Treibstoff mehr auf Lager.");
        if (u.WalletOf(ship).Credits < e.Price)
            return new ServiceResult(false, 0f, 0f, "Nicht genügend Credits für eine Einheit Treibstoff.");

        float want = fill ? ship.FuelSpace : tier;
        float bought = Refuel.BuyFuel(u, ship, station, good.Value, want);   // bucht Bestand, Stationskasse, Kasse des Schiffs (WalletOf), Tank
        if (bought <= 0f)
            return new ServiceResult(false, 0f, 0f, "Tanken fehlgeschlagen.");
        float cost = bought * e.Price;
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, request.Offer, good.Value, bought, -cost));
        return new ServiceResult(true, bought, -cost,
            $"{bought:0} {u.Goods[good.Value].Name} getankt für {cost:0} Cr (Tank {ship.TotalFuel:0.#}/{ship.Stats.FuelCapacity:0}).");
    }

    /// <summary>
    /// Zerlegt einen Angebots-Schlüssel streng: erlaubt sind nur „refuel:fill“ und „refuel:&lt;Stufe&gt;“ mit Stufe aus
    /// <see cref="Tiers"/>. Alles andere (falsche Stufe, leere Zahl, anderer Dienst) gilt als unbekannt.
    /// </summary>
    static bool TryParseOffer(string offer, out bool fill, out int tier)
    {
        fill = false;
        tier = 0;
        if (offer == $"{ServiceKey}:{Fill}") { fill = true; return true; }
        if (!offer.StartsWith($"{ServiceKey}:", StringComparison.Ordinal)) return false;
        if (!int.TryParse(offer[(ServiceKey.Length + 1)..], out int v)) return false;
        foreach (int t in Tiers)
            if (v == t) { tier = v; return true; }
        return false;
    }
}
