using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
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
    /// <summary>
    /// Phase 18: Angebots-Schlüssel „Fracht abwerfen“ im Format "drop:&lt;GoodId&gt;" (nur Ware an Bord eines Spielerschiffs; Menge ≤ 0 = alles an Bord):
    /// die Ware verschwindet ohne Erlös (<see cref="CargoJettison"/>). Der Ausweg für einen Rest, den die Station nicht nimmt.
    /// </summary>
    public const string Drop = "drop";

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
        string? refusal = TradeActions.Refusal(u, ship, station);   // Phase 25: gesucht → Handel verweigert (Menge 0, Grund im Angebot)
        foreach (var (good, entry) in station.Market.OrderBy(kv => kv.Key.Value))   // feste Reihenfolge nach GoodId, keine Hash-Reihenfolge
        {
            if (entry.Price <= 0f) continue;
            string name = u.Goods[good].Name;
            float held = ship.Cargo.GetValueOrDefault(good);
            // Phase 16: Annahmestatus (Menge und Grund) — dieselbe Regel wie der Verkauf. Phase 17: Angebotsmenge = was „Verkaufen“ der ganzen Ladung
            // bewegen würde (TradeActions.SaleUnits: alles, wenn die Station alles nimmt, auch einen Bruchteil; sonst ganze Einheiten) — Menge > 0 ⇔ Execute gelingt
            var intake = MarketInfo.Acceptance(u, station, good);
            float sellable = intake.SellableFrom(held);
            // Runde 30: Rückkaufdeckel — der Hinweis nennt den wirksamen Mindest- bzw. Höchstpreis (PlayerLedger), Menge und Ausführung rechnen damit
            // Runde 31: Price = wirksamer Stückpreis für die Angebotsmenge (mit Rückkaufdeckel), nicht nur der Hinweis
            float buyable = TradeActions.MaxBuyable(u, ship, station, good);
            float sellUnits = refusal is null ? sellable : 0f;
            offers.Add(new ServiceOffer($"{Buy}:{good.Value}", ExportRules.Buys(u, station, good) ? $"Nur Ankauf (Export): {name}" : $"Kaufen: {name}",
                PlayerLedger.BuyUnitPrice(u, ship, station, good, buyable, entry.Price), buyable, good, entry.Amount, held,
                refusal ?? PlayerLedger.BuyNote(u, ship, station, good)));
            offers.Add(new ServiceOffer($"{Sell}:{good.Value}", $"Verkaufen: {name}", PlayerLedger.SaleUnitPrice(u, ship, station, good, MathF.Max(sellUnits, held > 0f ? MathF.Min(held, 1f) : 0f), entry.Price), sellUnits, good, entry.Amount, held,
                refusal ?? (held > 0f ? intake.NoteFor(held) : intake.Accepts ? null : intake.Reason) ?? (held > 0f ? PlayerLedger.SaleNote(u, ship, station, good) : null)));
        }
        // Phase 18: je Ware an Bord ein Abwurf-Angebot (Menge = an Bord, Preis 0); Hinweis = warum die Station sie nicht (ganz) nimmt, null wenn doch
        if (ship.IsPlayer)
            foreach (var (good, held) in ship.Cargo.OrderBy(kv => kv.Key.Value))
            {
                if (held <= 0f) continue;
                var intake = MarketInfo.Acceptance(u, station, good);
                string? note = intake.SellableFrom(held) >= held - 0.0001f ? null : intake.NoteFor(held) ?? intake.Reason;
                // Phase 19: Treuhand-Material einer Baustelle ist gesperrt (Menge 0, Grund als Hinweis)
                var blocked = CargoJettison.Issue(u, ship, good);
                offers.Add(new ServiceOffer($"{Drop}:{good.Value}", $"Abwerfen: {u.Goods[good].Name}", 0f, blocked is null ? held : 0f, good,
                    station.Market.TryGetValue(good, out var e) ? e.Amount : 0f, held, blocked ?? note));
            }
        // Phase 17: Menge = was „Alles verkaufen“ bewegen würde (gleiche Regel wie SellAll), nicht die ganze Fracht — Menge > 0 ⇔ Execute gelingt
        var (allUnits, allRevenue) = TradeActions.Sellable(u, ship, station);
        float cargo = ship.Cargo.Values.Sum();
        if (refusal is not null) { allUnits = 0f; allRevenue = 0f; }
        offers.Add(new ServiceOffer(SellAll, "Alles verkaufen", allRevenue, allUnits, default, 0f, 0f,
            refusal ?? (cargo > 0f && allUnits <= 0f ? "Station nimmt nichts von der Fracht an" : null)));
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

        // Phase 25: gesucht → Handel verweigert (Abwerfen bleibt erlaubt)
        if (action != Drop && TradeActions.Refusal(u, ship, station) is { } refused) return new ServiceResult(false, 0f, 0f, refused);
        switch (action)
        {
            case Buy:
            {
                // Menge ≤ 0 = Maximum; ist das Maximum 0, fragt der Dienst eine Einheit an, damit TradeActions den Grund nennt
                float want = request.Units > 0f ? request.Units : MathF.Max(1f, TradeActions.MaxBuyable(u, ship, station, good));
                var r = TradeActions.Buy(u, ship, station, good, want);
                if (r.Units > 0f) u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Buy, good, r.Units, -r.Cost));
                // Runde 26 (Gerätetest: 53 statt angezeigter 56 Cr gebucht — der Preis fällt im Takt der Ticks): die Meldung nennt den gebuchten Betrag;
                // Runde 28: mit Nachkommastelle (101,5 statt gerundet 101 bei 102 Cr weniger in der Kasse)
                // Runde 30: gemischte Abrechnung nennt den Rückkaufteil
                string part = r.Floored > 0f && r.Units > 0f ? $", davon {r.Floored:0.#} zum Rückkaufpreis {r.FloorPrice:0.0}" : "";
                return new ServiceResult(r.Units > 0f, r.Units, -r.Cost, r.Units > 0f ? $"Gekauft {r.Units:0.#} × {r.Cost / r.Units:0.0#} = {r.Cost:0.#} Cr{part}" : OutcomeText(r.Outcome));
            }
            case Sell:
            {
                float want = request.Units > 0f ? request.Units : ship.Cargo.GetValueOrDefault(good);
                var r = TradeActions.Sell(u, ship, station, good, want);
                float units = r.Sold.GetValueOrDefault(good);
                if (units > 0f) u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Sell, good, units, r.Revenue));
                string part = r.Capped > 0f && units > 0f ? $", davon {r.Capped:0.#} zum Rückkaufpreis {r.CapPrice:0.0}" : "";
                return new ServiceResult(units > 0f, units, r.Revenue, units > 0f ? $"Verkauft {units:0.#} × {r.Revenue / units:0.0#} = {r.Revenue:0.#} Cr{part}" : SellFailure(u, station, ship, good));
            }
            case SellAll:
            {
                var r = TradeActions.SellAll(u, ship, station);
                float units = r.Sold.Values.Sum();
                if (units > 0f) u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, SellAll, default, units, r.Revenue));
                // Runde 31 (Gerätetest handy31): die Meldung nennt Menge, Betrag und den Rückkaufteil wie beim Einzelverkauf
                string part = r.Capped > 0f ? $", davon {r.Capped:0.#} zum Rückkaufpreis" : "";
                string msg = units <= 0f ? (ship.Cargo.Values.Sum() > 0f ? "Station nimmt nichts von der Fracht an" : "Nichts zu verkaufen")
                    : $"{(r.Unsold > 0.0001f ? "Verkauft" : "Alles verkauft")}: {units:0.#} Einheiten für {r.Revenue:0.#} Cr{part}{(r.Unsold > 0.0001f ? " – Rest bleibt an Bord" : "")}";
                return new ServiceResult(units > 0f, units, r.Revenue, msg);
            }
            case Drop:
            {
                // Phase 18: Fracht abwerfen — nur Spielerschiffe, nur Ware an Bord; scheitert ohne Zustandsänderung und ohne Ereignis
                if (CargoJettison.Issue(u, ship, good) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
                float units = CargoJettison.Jettison(u, ship, good, request.Units);
                u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Drop, good, units, 0f));
                return new ServiceResult(true, units, 0f, $"{units:0.##} {u.Goods[good].Name} abgeworfen");
            }
            default:
                return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        }
    }

    /// <summary>
    /// Zerlegt einen Angebots-Schlüssel streng: erlaubt sind nur "buy:&lt;GoodId&gt;", "sell:&lt;GoodId&gt;", "drop:&lt;GoodId&gt;" (Phase 18) und "sell_all".
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
        if (head != Buy && head != Sell && head != Drop) return false;

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
    /// <remarks>Phase 16: derselbe Grund wie im Angebot (<see cref="MarketInfo.Acceptance"/>).</remarks>
    static string SellFailure(Universe u, Station station, Ship ship, GoodId good)
    {
        float held = ship.Cargo.GetValueOrDefault(good);
        if (held <= 0f) return "Nichts an Bord";
        var intake = MarketInfo.Acceptance(u, station, good);
        return intake.NoteFor(held) ?? intake.Reason ?? "Station kann nichts mehr abnehmen oder bezahlen";
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
