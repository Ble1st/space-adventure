using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Services;

/// <summary>
/// Der Markt einer Station als Dienst "trade": Waren kaufen und verkaufen über <see cref="TradeActions"/> —
/// dasselbe Verhalten wie das heutige Dock-Panel.
/// </summary>
public sealed class TradeService : IStationService
{
    public const string ServiceKey = "trade";
    /// <summary>Angebots-Schlüssel Kauf einer Ware im Format "buy:&lt;GoodId&gt;"; Menge ≤ 0 = so viel wie möglich.</summary>
    public const string Buy = "buy";
    /// <summary>Angebots-Schlüssel Verkauf einer Ware im Format "sell:&lt;GoodId&gt;"; Menge ≤ 0 = gesamter Bestand an Bord.</summary>
    public const string Sell = "sell";
    /// <summary>Angebots-Schlüssel Verkauf der gesamten Fracht (ohne Ware, ohne Menge).</summary>
    public const string SellAll = "sell_all";

    public string Key => ServiceKey;

    /// <summary>Verfügbar, wenn die Station einen Markt führt.</summary>
    public bool IsAvailable(Universe u, Station station) => station.Market.Count > 0;

    /// <summary>
    /// Je Ware des Stationsmarkts ein Kauf- und ein Verkaufsangebot, dazu "alles verkaufen" — wie das Dock-Panel.
    /// Waren ohne Preis (&lt;= 0, z.B. nicht gehandelte Zwischenprodukte) bleiben weggelassen.
    /// </summary>
    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>(station.Market.Count * 2 + 1);
        foreach (var (good, entry) in station.Market.OrderBy(kv => kv.Key.Value))   // feste Reihenfolge nach GoodId, keine Hash-Reihenfolge
        {
            if (entry.Price <= 0f) continue;
            string name = u.Goods[good].Name;
            float held = ship.Cargo.GetValueOrDefault(good);
            float sellable = MathF.Min(MathF.Min(held, MathF.Max(0f, station.Credits) / entry.Price), station.AcceptableUnits(good, u.Rules));
            offers.Add(new ServiceOffer($"{Buy}:{good.Value}", $"Kaufen: {name}", entry.Price, TradeActions.MaxBuyable(u, ship, station, good), good, entry.Amount, held));
            offers.Add(new ServiceOffer($"{Sell}:{good.Value}", $"Verkaufen: {name}", entry.Price, sellable, good, entry.Amount, held));
        }
        float cargo = ship.Cargo.Values.Sum();
        offers.Add(new ServiceOffer(SellAll, "Alles verkaufen", TradeActions.Quote(u, ship, station), cargo, default, 0f, 0f));
        return offers;
    }

    /// <summary>
    /// Delegiert an <see cref="TradeActions.Buy"/>/<see cref="TradeActions.Sell"/>/<see cref="TradeActions.SellAll"/> und veröffentlicht
    /// <see cref="ServicePerformed"/> nur bei Erfolg. Unbekannte Schlüssel und ein nicht verfügbarer Dienst ändern nichts und lösen kein Event aus.
    /// </summary>
    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (!TryParseOffer(request.Offer, out string action, out GoodId good))
            return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station))
            return new ServiceResult(false, 0f, 0f, $"Dienst '{Key}' ist an dieser Station nicht verfügbar.");

        switch (action)
        {
            case Buy:
            {
                // Menge ≤ 0 = Maximum; ist das Maximum 0, fragt der Dienst eine Einheit an, damit TradeActions den Grund nennt
                float want = request.Units > 0f ? request.Units : MathF.Max(1f, TradeActions.MaxBuyable(u, ship, station, good));
                var r = TradeActions.Buy(u, ship, station, good, want);
                if (r.Units > 0f) u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Buy, good, r.Units, -r.Cost));
                return new ServiceResult(r.Units > 0f, r.Units, -r.Cost, OutcomeText(r.Outcome));
            }
            case Sell:
            {
                float want = request.Units > 0f ? request.Units : ship.Cargo.GetValueOrDefault(good);
                var r = TradeActions.Sell(u, ship, station, good, want);
                float units = r.Sold.GetValueOrDefault(good);
                if (units > 0f) u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Sell, good, units, r.Revenue));
                return new ServiceResult(units > 0f, units, r.Revenue, units > 0f ? "Verkauft" : SellFailure(station, ship, good));
            }
            case SellAll:
            {
                var r = TradeActions.SellAll(u, ship, station);
                float units = r.Sold.Values.Sum();
                if (r.Revenue > 0f) u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, SellAll, default, units, r.Revenue));
                return new ServiceResult(r.Revenue > 0f, units, r.Revenue, r.Revenue > 0f ? "Alles verkauft" : "Nichts zu verkaufen");
            }
            default:
                return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        }
    }

    /// <summary>
    /// Zerlegt einen Angebots-Schlüssel streng: erlaubt sind nur "buy:&lt;GoodId&gt;", "sell:&lt;GoodId&gt;" und "sell_all".
    /// Alles andere (ohne Ware, unlesbare Zahl, unbekannte Aktion, "sell_all:...") gilt als unbekannt.
    /// </summary>
    static bool TryParseOffer(string offer, out string action, out GoodId good)
    {
        action = "";
        good = default;
        if (offer == SellAll) { action = SellAll; return true; }

        int sep = offer.IndexOf(':');
        if (sep < 0) return false;
        string head = offer[..sep];
        if (head != Buy && head != Sell) return false;

        string tail = offer[(sep + 1)..];
        if (tail.Length == 0) return false;
        foreach (char c in tail)
            if (c is < '0' or > '9') return false;
        if (!int.TryParse(tail, out int value) || value <= 0) return false;   // 0 = keine Ware

        action = head;
        good = new GoodId(value);
        return true;
    }

    /// <summary>Grund, warum ein Verkauf nichts bewegt hat (nach dem Versuch ermittelt; der Zustand ist unverändert).</summary>
    static string SellFailure(Station station, Ship ship, GoodId good)
    {
        if (ship.Cargo.GetValueOrDefault(good) <= 0f) return "Nichts an Bord";
        if (!station.Market.TryGetValue(good, out var e) || e.Price <= 0f) return "Ware wird hier nicht gehandelt";
        return "Station kann nichts mehr abnehmen oder bezahlen";
    }

    static string OutcomeText(BuyOutcome o) => o switch
    {
        BuyOutcome.Ok => "Kauf erfolgreich",
        BuyOutcome.NotSold => "Ware wird hier nicht gehandelt",
        BuyOutcome.OutOfStock => "Nicht genügend auf Lager",
        BuyOutcome.NoCredits => "Nicht genügend Credits",
        BuyOutcome.NoSpace => "Laderaum voll",
        _ => o.ToString(),
    };
}
