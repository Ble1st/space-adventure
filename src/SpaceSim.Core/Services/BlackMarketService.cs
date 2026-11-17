using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Services;

/// <summary>
/// Schwarzmarkt (docs/SCHWARZMARKT.md), Dienst „blackmarket“ an Piratenverstecken: der Hehler kauft jede Ware an Bord (auch Schmuggelware, egal wie die
/// Fahndung steht — der Ausweg für Beute und gesuchte Spieler) und verkauft seine Hehlerware. Preise und Lager: <see cref="BlackMarket"/>. Angebots-Schlüssel
/// streng: <c>sell:&lt;GoodId&gt;</c> (Spieler verkauft), <c>buy:&lt;GoodId&gt;</c> (Spieler kauft). Angebotsmenge &gt; 0 ⇔ <see cref="Execute"/> gelingt;
/// <see cref="ServicePerformed"/> nur bei Erfolg. Geld: Ankauf aus der Piratenkasse (<see cref="TreasuryUpkeep"/> „blackmarket“), Verkauf in sie
/// (<see cref="TreasuryIncome"/> „blackmarket“); der Spieler bucht über <see cref="Universe.WalletOf(Ship)"/>.
/// </summary>
public sealed class BlackMarketService : IStationService
{
    public const string ServiceKey = BlackMarket.ServiceKey;
    public const string Sell = "sell";
    public const string Buy = "buy";

    public string Key => ServiceKey;

    public bool IsAvailable(Universe u, Station station) => BlackMarket.IsAt(u, station);

    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>();
        if (!ship.IsPlayer || !BlackMarket.IsAt(u, station)) return offers;
        string? refusal = BlackMarket.Refusal(u, station, ship);
        float purse = BlackMarket.Purse(u, station);
        foreach (var (good, held) in ship.Cargo.OrderBy(kv => kv.Key.Value))
        {
            if (held <= 0f || !u.Goods.ContainsKey(good)) continue;
            float price = BlackMarket.BuyPrice(u, good);
            float n = refusal is not null || price <= 0f ? 0f : held * price <= purse + 0.001f ? held : MathF.Floor(purse / price);
            string? note = refusal ?? (n <= 0f ? "Kasse des Hehlers leer" : n < held ? $"Hehler zahlt nur {n:0}" : null);
            offers.Add(new ServiceOffer($"{Sell}:{good.Value}", $"Verkaufen: {u.Goods[good].Name}", price, n, good, station.BlackStock.GetValueOrDefault(good), held, note));
        }
        float credits = u.WalletOf(ship).Credits;
        foreach (var (good, stock) in station.BlackStock.OrderBy(kv => kv.Key.Value))
        {
            if (stock < 0.01f || !u.Goods.ContainsKey(good)) continue;
            float price = BlackMarket.SellPrice(u, good);
            float free = ship.FreeUnits(u, good);
            float n = MathF.Min(stock, free);
            if (n * price > credits + 0.001f) n = credits > 0f ? MathF.Floor(credits / price) : 0f;
            if (refusal is not null) n = 0f;
            n = MathF.Max(0f, n);
            string? note = refusal ?? (n > 0f ? null : free < 0.01f ? "Kein Platz im Laderaum" : "Nicht genügend Credits");
            offers.Add(new ServiceOffer($"{Buy}:{good.Value}", $"Kaufen: {u.Goods[good].Name}", price, n, good, stock, ship.Cargo.GetValueOrDefault(good), note));
        }
        return offers;
    }

    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (!TryParse(request.Offer, out var action, out var good) || !u.Goods.ContainsKey(good))
            return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        if (BlackMarket.Refusal(u, station, ship) is { } refusal) return new ServiceResult(false, 0f, 0f, refusal);
        var offer = Offers(u, station, ship).FirstOrDefault(o => o.Key == request.Offer);
        if (offer.Key is null)
            return new ServiceResult(false, 0f, 0f, action == Sell ? "Nichts an Bord" : "Diese Ware hat der Hehler nicht");
        if (offer.Amount <= 0f) return new ServiceResult(false, 0f, 0f, offer.Note ?? "Nicht möglich");
        float units = request.Units > 0f ? MathF.Min(request.Units, offer.Amount) : offer.Amount;
        var fence = BlackMarket.Fence(u, station)!;
        string name = u.Goods[good].Name;
        if (action == Sell)
        {
            float pay = units * offer.Price;
            float left = ship.Cargo[good] - units;
            if (left <= 0.0001f) ship.Cargo.Remove(good); else ship.Cargo[good] = left;
            BlackMarket.Add(u, station, good, units);
            fence.Treasury.Credits -= pay;
            u.WalletOf(ship).Credits += pay;
            u.Events.Publish(new TreasuryUpkeep(fence.Id, ServiceKey, pay, 0f));
            u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Sell, good, units, pay));
            return new ServiceResult(true, units, pay, $"An den Hehler verkauft: {units:0.##} {name} × {offer.Price:0.00} = {pay:0.00} Cr");
        }
        float cost = units * offer.Price;
        float rest = station.BlackStock[good] - units;
        if (rest <= 0.0001f) station.BlackStock.Remove(good); else station.BlackStock[good] = rest;
        ship.Cargo[good] = ship.Cargo.GetValueOrDefault(good) + units;
        u.WalletOf(ship).Credits -= cost;
        fence.Treasury.Credits += cost;
        u.Events.Publish(new TreasuryIncome(fence.Id, ServiceKey, cost));
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Buy, good, units, -cost));
        return new ServiceResult(true, units, -cost, $"Hehlerware gekauft: {units:0.##} {name} × {offer.Price:0.00} = {cost:0.00} Cr");
    }

    /// <summary>Strenge Zerlegung: nur "sell:&lt;GoodId&gt;" und "buy:&lt;GoodId&gt;" mit positiver Zahl.</summary>
    static bool TryParse(string offer, out string action, out GoodId good)
    {
        action = ""; good = default;
        int sep = offer.IndexOf(':');
        if (sep < 0) return false;
        string head = offer[..sep], tail = offer[(sep + 1)..];
        if (head != Sell && head != Buy || tail.Length == 0 || tail.Any(c => c is < '0' or > '9')) return false;
        if (!int.TryParse(tail, out int v) || v <= 0) return false;
        action = head; good = new GoodId(v);
        return true;
    }
}
