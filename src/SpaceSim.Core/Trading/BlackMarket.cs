using SpaceSim.Core.Model;

namespace SpaceSim.Core.Trading;

/// <summary>
/// Schwarzmarkt (docs/SCHWARZMARKT.md): der Hehler im Piratenversteck. Reine Regeln über <see cref="Station.BlackStock"/>, die Piratenkasse und die
/// Spielerkasse; ausgeführt wird nur über den Dienst „blackmarket“ (<see cref="Services.BlackMarketService"/>).
/// <list type="bullet">
/// <item>Ankauf (Spieler verkauft): jede Ware, auch Schmuggelware, ohne Blick auf die Fahndung, zu <see cref="EconomyRules.BlackMarketBuyShare"/> ×
/// min(Basispreis, bestes ehrliches Gebot) — gezahlt aus der Piratenkasse über <see cref="EconomyRules.BlackMarketReserve"/>.</item>
/// <item>Verkauf (Spieler kauft): Hehlerware aus dem Lager zu <see cref="EconomyRules.BlackMarketSellFactor"/> × Basispreis, das Geld geht in die
/// Piratenkasse.</item>
/// <item>Kein Rückkaufkreislauf: der Ankaufspreis ist höchstens Anteil × Basispreis, der Verkaufspreis fest Faktor × Basispreis (Standard 0,5 gegen 1,5) — jeder
/// Kauf→Rückverkauf und Verkauf→Rückkauf am Schwarzmarkt verliert, zu jedem Zeitpunkt. Darum braucht er keine Posten im <see cref="PlayerLedger"/> (die
/// hängen an Markteinträgen, die der Hehler nicht hat).</item>
/// <item>Lager: was der Spieler verkauft und was die Kaperer erbeuten (<see cref="Stash"/>), je Ware höchstens <see cref="EconomyRules.BlackMarketStockMax"/>.</item>
/// </list>
/// </summary>
public static class BlackMarket
{
    public const string ServiceKey = "blackmarket";

    /// <summary>Hat diese Station einen Schwarzmarkt (Regel an, Typ führt den Dienst, keine Baustelle, nicht ausgeschaltet)?</summary>
    public static bool IsAt(Universe u, Station st) =>
        u.Rules.BlackMarket && !st.IsBuildSite && st.Condition is not { Disabled: true }
        && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Services.Contains(ServiceKey);

    /// <summary>Kasse, aus der der Hehler zahlt und in die er einnimmt (Besitzer des Verstecks); null = keine.</summary>
    public static Actor? Fence(Universe u, Station st) => u.Actors.TryGetValue(st.Owner, out var a) ? a : null;

    /// <summary>Geld, das der Hehler jetzt für Ankäufe ausgeben kann (Kasse über der Rücklage).</summary>
    public static float Purse(Universe u, Station st) =>
        Fence(u, st) is { } a ? MathF.Max(0f, a.Treasury.Credits - u.Rules.BlackMarketReserve) : 0f;

    /// <summary>Bestes ehrliches Gebot einer Ware (höchster Preis über fertige Stationen, die sie halten und nicht nur herstellen); 0 = keins.</summary>
    public static float HonestBid(Universe u, GoodId g)
    {
        float best = 0f;
        foreach (var st in u.Stations.Values)
        {
            if (st.IsBuildSite || !st.Market.TryGetValue(g, out var e) || e.Price <= 0f) continue;
            if (st.Produces(g) && !st.Consumes(g)) continue;
            if (e.Price > best) best = e.Price;
        }
        return best;
    }

    /// <summary>Ankaufspreis je Einheit: Anteil × min(Basispreis, bestes ehrliches Gebot), ohne Gebot Anteil × Basispreis (auf Cent gerundet).</summary>
    public static float BuyPrice(Universe u, GoodId g)
    {
        float basePrice = u.GoodOf(g).BasePrice, bid = HonestBid(u, g);
        float reference = bid > 0f ? MathF.Min(basePrice, bid) : basePrice;
        return MathF.Floor(u.Rules.BlackMarketBuyShare * reference * 100f) / 100f;
    }

    /// <summary>Verkaufspreis der Hehlerware je Einheit: Faktor × Basispreis (fest, auf Cent gerundet).</summary>
    public static float SellPrice(Universe u, GoodId g) => MathF.Ceiling(u.Rules.BlackMarketSellFactor * u.GoodOf(g).BasePrice * 100f) / 100f;

    /// <summary>
    /// Beute ins Lager des Hehlers: das erste Versteck (nach Id) des Besitzers mit Schwarzmarkt nimmt die Ware auf, je Ware höchstens bis
    /// <see cref="EconomyRules.BlackMarketStockMax"/>. Ohne Schwarzmarkt passiert nichts (kein Zufall, kein Geld — reine Ablage).
    /// </summary>
    public static void Stash(Universe u, ActorId owner, GoodId good, float units)
    {
        if (!u.Rules.BlackMarket || units <= 0f) return;
        Station? at = null;
        foreach (var st in u.StationsById()) if (st.Owner == owner && IsAt(u, st)) { at = st; break; }
        if (at is not null) Add(u, at, good, units);
    }

    /// <summary>Ware ins Lager legen (gedeckelt); liefert die aufgenommene Menge.</summary>
    public static float Add(Universe u, Station st, GoodId good, float units)
    {
        float have = st.BlackStock.GetValueOrDefault(good);
        float take = MathF.Min(units, MathF.Max(0f, u.Rules.BlackMarketStockMax - have));
        if (take > 0f) st.BlackStock[good] = have + take;
        return take;
    }

    /// <summary>Grund, warum der Hehler mit diesem Schiff gar nicht handelt (null = er handelt).</summary>
    public static string? Refusal(Universe u, Station st, Ship ship)
    {
        if (!IsAt(u, st)) return "Kein Schwarzmarkt hier";
        if (!ship.IsPlayer) return "Nur für eigene Schiffe";
        if (u.Fights.IsEngaged(ship.Id)) return "Nicht während eines Gefechts";
        return null;
    }

    /// <summary>Zeile der Station-Info („Schwarzmarkt: …“); null = kein Schwarzmarkt.</summary>
    public static string? InfoLine(Universe u, Station st)
    {
        if (!IsAt(u, st)) return null;
        int goods = st.BlackStock.Count(kv => kv.Value >= 0.01f);
        float units = st.BlackStock.Values.Sum();
        string stock = goods == 0 ? "keine Hehlerware" : $"{goods} Ware{(goods == 1 ? "" : "n")} ({units:0} E.) zu {100f * u.Rules.BlackMarketSellFactor:0} % des Basispreises";
        return $"Schwarzmarkt: kauft jede Ware zu {100f * u.Rules.BlackMarketBuyShare:0} % (Hehlerkasse {Purse(u, st):0} Cr frei) · {stock}";
    }
}
