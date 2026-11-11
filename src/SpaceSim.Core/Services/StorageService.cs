using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Services;

/// <summary>
/// Lagerhaus (Phase 8, STATIONEN.md „Lagerhaus / Depot“), Dienst „storage“: Der Spieler lagert Fracht an der Station ein
/// (<see cref="Station.PlayerStorage"/>, gespeichert) und holt sie später wieder ab. Einlagern kostet <see cref="EconomyRules.StorageFeePerUnit"/>
/// je Einheit (an die Station, gebucht über <see cref="Universe.WalletOf(Ship)"/>), Abholen ist frei; je Station höchstens
/// <see cref="EconomyRules.StorageCapacity"/> Einheiten. Angebots-Schlüssel streng: <c>store:&lt;GoodId&gt;</c>, <c>fetch:&lt;GoodId&gt;</c>.
/// Für KI-Bauschiffe ist das Lagerhaus über seine Lagerposten (<c>stocks</c> Baumaterial) ein Lieferant der Baustellen (Baustellenlager).
/// </summary>
public sealed class StorageService : IStationService
{
    public const string ServiceKey = "storage";
    public const string Store = "store";
    public const string Fetch = "fetch";

    public string Key => ServiceKey;

    /// <summary>Verfügbar an jeder Station, deren Typ den Dienst führt (Lagerhaus).</summary>
    public bool IsAvailable(Universe u, Station station) => !station.IsBuildSite;

    /// <summary>Belegtes persönliches Lager an dieser Station (Einheiten).</summary>
    public static float Used(Station st) => st.PlayerStorage.Values.Sum();

    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>();
        float fee = u.Rules.StorageFeePerUnit;
        float room = MathF.Max(0f, u.Rules.StorageCapacity - Used(station));
        float credits = u.WalletOf(ship).Credits;
        foreach (var (good, held) in ship.Cargo.OrderBy(kv => kv.Key.Value))
        {
            if (held <= 0f) continue;
            float n = MathF.Min(held, room);
            if (fee > 0f) n = MathF.Min(n, MathF.Floor(credits / fee));
            offers.Add(new ServiceOffer($"{Store}:{good.Value}", $"Einlagern: {u.Goods[good].Name}", fee, MathF.Max(0f, n), good,
                station.PlayerStorage.GetValueOrDefault(good), held));
        }
        foreach (var (good, stored) in station.PlayerStorage.OrderBy(kv => kv.Key.Value))
        {
            if (stored <= 0f) continue;
            float n = MathF.Min(stored, ship.FreeUnits(u, good));
            offers.Add(new ServiceOffer($"{Fetch}:{good.Value}", $"Abholen: {u.Goods[good].Name}", 0f, n, good, stored, ship.Cargo.GetValueOrDefault(good)));
        }
        return offers;
    }

    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (!TryParse(request.Offer, out var action, out var good) || !u.Goods.ContainsKey(good))
            return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station)) return new ServiceResult(false, 0f, 0f, "Kein Lager an dieser Station.");
        if (!ship.IsPlayer) return new ServiceResult(false, 0f, 0f, "Nur für eigene Schiffe.");
        var offer = Offers(u, station, ship).FirstOrDefault(o => o.Key == request.Offer);
        if (offer.Key is null || offer.Amount <= 0f)
            return new ServiceResult(false, 0f, 0f, action == Store
                ? (ship.Cargo.GetValueOrDefault(good) <= 0f ? "Nichts an Bord" : Used(station) >= u.Rules.StorageCapacity ? "Lager voll" : "Nicht genügend Credits")
                : (station.PlayerStorage.GetValueOrDefault(good) <= 0f ? "Nichts eingelagert" : "Laderaum voll"));
        float units = request.Units > 0f ? MathF.Min(request.Units, offer.Amount) : offer.Amount;
        if (action == Store)
        {
            float cost = units * u.Rules.StorageFeePerUnit;
            u.WalletOf(ship).Credits -= cost;
            station.Credits += cost;
            float left = ship.Cargo[good] - units;
            if (left <= 0.0001f) ship.Cargo.Remove(good); else ship.Cargo[good] = left;
            station.PlayerStorage[good] = station.PlayerStorage.GetValueOrDefault(good) + units;
            u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Store, good, units, -cost));
            return new ServiceResult(true, units, -cost, $"{units:0} {u.Goods[good].Name} eingelagert ({cost:0} Cr Gebühr)");
        }
        float rest = station.PlayerStorage[good] - units;
        if (rest <= 0.0001f) station.PlayerStorage.Remove(good); else station.PlayerStorage[good] = rest;
        ship.Cargo[good] = ship.Cargo.GetValueOrDefault(good) + units;
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Fetch, good, units, 0f));
        return new ServiceResult(true, units, 0f, $"{units:0} {u.Goods[good].Name} abgeholt");
    }

    /// <summary>Strenge Zerlegung: nur "store:&lt;GoodId&gt;" und "fetch:&lt;GoodId&gt;" mit positiver Zahl.</summary>
    static bool TryParse(string offer, out string action, out GoodId good)
    {
        action = ""; good = default;
        int sep = offer.IndexOf(':');
        if (sep < 0) return false;
        string head = offer[..sep], tail = offer[(sep + 1)..];
        if (head != Store && head != Fetch || tail.Length == 0 || tail.Any(c => c is < '0' or > '9')) return false;
        if (!int.TryParse(tail, out int v) || v <= 0) return false;
        action = head; good = new GoodId(v);
        return true;
    }
}
