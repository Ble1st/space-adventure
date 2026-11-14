using SpaceSim.Core.Model;

namespace SpaceSim.Core.Trading;

/// <summary>
/// Runde 30 — Rückkaufdeckel (Gerätetest Runde 29: Fusionsreaktor für 359 gekauft, sofort für ~538 zurückverkauft; der Prüfer fand die Spiegelrichtung:
/// teuer an einen Verbraucher verkaufen, einen Tick später billiger zurückkaufen). Ursache: ein Handel wird für die ganze Menge zum Preis VOR dem Handel
/// abgerechnet, im nächsten Tick bepreist der Markt den veränderten Bestand neu. Regel nur für Schiffe des Spieler-Akteurs (Spielerschiff und Flotte —
/// am Akteur, nicht am Schiff, sonst umginge ein zweites Schiff den Deckel); NPCs haben keine Posten.
/// <list type="bullet">
/// <item>Kauf: verkauft der Spieler diese Ware später an DIESER Station, bekommt er für bis zu <see cref="LedgerPost.BoughtQty"/> Einheiten höchstens den
/// gezahlten Stückpreis (min(Markt, gezahlt)), darüber den Marktpreis.</item>
/// <item>Verkauf (Spiegel): kauft er sie später an DIESER Station, zahlt er für bis zu <see cref="LedgerPost.SoldQty"/> Einheiten mindestens den erhaltenen
/// Stückpreis (max(Markt, erhalten)).</item>
/// <item>Gegenhandel baut einen Posten zuerst ab (ein Rückverkauf mindert den Kauf-Posten, nur der Rest legt einen Verkaufs-Posten an — und umgekehrt).</item>
/// <item>Verfall ohne Frist: ein Kauf-Posten verfällt, sobald der Bestand der Station wieder den Stand vor dem ersten Kauf erreicht (dann ist der Preis nach
/// der Bestandsformel nicht höher als der gezahlte), ein Verkaufs-Posten, sobald der Bestand wieder auf dem Stand vor dem ersten Verkauf ist. Zeitfenster
/// lecken (Runde 29 gemessen: nach 20 Minuten noch ~90 von ~250 Einträgen über dem Kaufpreis), darum keine Frist; leere Posten werden gelöscht.</item>
/// </list>
/// Reine Regeln über <see cref="Universe.Ledger"/>; ehrlicher Handel (Kauf bei A, Verkauf bei B) wird nie berührt.
/// </summary>
public static class PlayerLedger
{
    /// <summary>Gilt der Deckel für dieses Schiff? Nur Schiffe des Spieler-Akteurs.</summary>
    public static bool Applies(Universe u, Ship ship) => u.Rules.BuybackCap && ship.Owner == u.PlayerActor && u.PlayerActor.Value != 0;

    static (int, int, int) Key(Universe u, Station st, GoodId good) => (u.PlayerActor.Value, st.Id.Value, good.Value);

    /// <summary>Posten oder null (nur lesen — der Verfall läuft im Tick, <see cref="Prune"/>, damit eine Anzeige den Zustand nie ändert).</summary>
    public static LedgerPost? Post(Universe u, Station st, GoodId good) =>
        u.Ledger.Count > 0 && u.Ledger.TryGetValue(Key(u, st, good), out var p) && !p.Empty ? p : null;

    /// <summary>
    /// Verfall durch Bestandsrückkehr, im Tick (<see cref="Simulation.LedgerSystem"/>): ein Kauf-Posten verfällt, sobald der
    /// Bestand den Stand vor dem ersten Kauf wieder erreicht, ein Verkaufs-Posten, sobald er wieder auf dem Stand vor dem ersten Verkauf ist; der Verfall
    /// ist endgültig (ein später wieder sinkender Bestand belebt ihn nicht). Leere Posten und Posten weggefallener Stationen werden gelöscht.
    /// </summary>
    public static void Prune(Universe u)
    {
        if (u.Ledger.Count == 0) return;
        bool v1 = u.Rules.BuybackWholeTrade;
        List<(int, int, int)>? gone = null;
        Dictionary<int, float>? aboard = null;   // je Ware: was die Schiffe des Spieler-Akteurs an Bord haben (einmal je Aufruf)
        foreach (var (k, p) in u.Ledger)
        {
            float amount = u.Stations.TryGetValue(new StationId(k.Item2), out var st) && st.Market.TryGetValue(new GoodId(k.Item3), out var e) ? e.Amount : float.NaN;
            if (float.IsNaN(amount)) { CloseBuy(p); CloseSale(p); }
            // Runde 32b: frühestens im Tick nach der letzten Buchung — der Markt hat den vom Spieler bewegten Bestand dann neu bepreist (die Prüfung fand
            // Gegenbuchungen im selben Tick, die den Posten vorzeitig beendeten)
            else if (!v1 || u.Tick > p.LastTick)
            {
                // Runde 30 (Sonde): nur Bestandsrückkehr genügt nicht — bei Verbrauchern bestimmt oft die Zahlungsbereitschaft den Preis, nicht der Bestand
                // (Elektronikfabrik Beta: Kupfer 20,6 bei Bestand 170, einen Tick später 50,0 bei 250). Darum zusätzlich: der Markt zahlt nicht mehr als den
                // Kaufpreis bzw. verlangt nicht weniger als den Verkaufspreis — erst dann ist der Deckel ohnehin wirkungslos
                float price = st!.Market[new GoodId(k.Item3)].Price;
                bool stale = u.Rules.BuybackStaleTicks > 0 && u.Tick - p.LastTick >= u.Rules.BuybackStaleTicks;   // Messvorschlag, Standard aus
                bool staleStock = !u.Rules.BuybackStaleIgnoresStock;
                if (stale && BuyActive(u, p) && (!staleStock || amount >= p.BoughtStockBefore - 0.0001f))
                {
                    aboard ??= Aboard(u);
                    float heldStale = aboard.GetValueOrDefault(k.Item3) + (st.PlayerStorage.TryGetValue(new GoodId(k.Item3), out var st2) ? st2 : 0f);
                    if (MathF.Min(p.BoughtQty, heldStale) <= 0.0001f) CloseBuy(p);
                }
                if (stale && SaleActive(u, p) && (!staleStock || amount <= p.SoldStockBefore + 0.0001f)) CloseSale(p);
                if (BuyActive(u, p) && amount >= p.BoughtStockBefore - 0.0001f && price <= p.BoughtPrice + 0.0001f)
                {
                    if (!u.Rules.BuybackHeldKeepsPost) CloseBuy(p);
                    else
                    {
                        // Runde 31 (Prüfung: 74 Konsumgüter gekauft, Posten verfiel während des Wartens, 26 nachgekauft → 74 ungedeckelt zu 280, +6 627 Cr):
                        // hält der Spieler von den hier gekauften Einheiten noch etwas (an Bord seiner Schiffe oder im Lager dieser Station), bleibt der Posten
                        // für diese Menge. Runde 32b: nur solange die Postenmenge > 0 ist — wer alles hier zurückverkauft hat, hält nichts mehr „von hier“
                        aboard ??= Aboard(u);
                        float held = aboard.GetValueOrDefault(k.Item3) + (st.PlayerStorage.TryGetValue(new GoodId(k.Item3), out var stored) ? stored : 0f);
                        float keep = MathF.Min(p.BoughtQty, held);
                        if (keep > 0.0001f) p.BoughtQty = keep; else CloseBuy(p);
                    }
                }
                if (SaleActive(u, p) && amount <= p.SoldStockBefore + 0.0001f && price >= p.SoldPrice - 0.0001f) CloseSale(p);
            }
            if (p.Empty) (gone ??= new()).Add(k);
        }
        if (gone is not null) foreach (var k in gone) u.Ledger.Remove(k);
    }

    static void CloseBuy(LedgerPost p) { p.BoughtQty = 0f; p.BoughtOpen = false; }
    static void CloseSale(LedgerPost p) { p.SoldQty = 0f; p.SoldOpen = false; }

    /// <summary>
    /// Runde 32b: wirkt der Kauf- bzw. Verkaufsteil? V1: bis zum Verfall im Tick, auch mit Restmenge 0 (Gegenbuchungen bauen nur die Menge ab); Runde-31-Regel:
    /// solange Menge &gt; 0.
    /// </summary>
    public static bool BuyActive(Universe u, LedgerPost p) => u.Rules.BuybackWholeTrade ? p.BoughtOpen : p.BoughtQty > 0f;
    public static bool SaleActive(Universe u, LedgerPost p) => u.Rules.BuybackWholeTrade ? p.SoldOpen : p.SoldQty > 0f;

    static Dictionary<int, float> Aboard(Universe u)
    {
        var d = new Dictionary<int, float>();
        foreach (var sh in u.Ships.Values)
            if (sh.Owner == u.PlayerActor)
                foreach (var (g, a) in sh.Cargo) if (a > 0f) d[g.Value] = d.GetValueOrDefault(g.Value) + a;
        return d;
    }

    /// <summary>
    /// Erlös eines Verkaufs von <paramref name="units"/> Einheiten zum Marktpreis <paramref name="price"/>: die ersten bis zu Kauf-Posten-Menge Einheiten zu
    /// min(Markt, Kaufpreis), der Rest zum Markt. <c>Capped</c> = so viele Einheiten zum Rückkaufpreis <c>CapPrice</c>.
    /// </summary>
    public static (float Revenue, float Capped, float CapPrice) SaleValue(Universe u, Ship ship, Station st, GoodId good, float units, float price)
    {
        if (Piecewise(u, ship, st, good) is { } e2) return (Curve(u, st, good, e2, units, price, sale: true), 0f, price);
        if (!Applies(u, ship) || Post(u, st, good) is not { } p || !BuyActive(u, p) || p.BoughtPrice >= price) return (units * price, 0f, price);
        // Runde 32 (V1): solange der Kauf-Posten offen ist, jede Einheit — auch mitgebrachte Ware (Prüfung: 50 gekauft, Preis hochgetrieben, 50 + 50 mitgebrachte
        // verkauft: die mitgebrachten liefen zum selbst hochgetriebenen Preis, +4 848 Cr je Fahrt)
        float c = u.Rules.BuybackWholeTrade ? units : MathF.Min(units, p.BoughtQty);
        return (c * p.BoughtPrice + (units - c) * price, c, p.BoughtPrice);
    }

    /// <summary>
    /// Kosten eines Kaufs von <paramref name="units"/> Einheiten zum Marktpreis <paramref name="price"/>: die ersten bis zu Verkaufs-Posten-Menge Einheiten zu
    /// max(Markt, Verkaufspreis), der Rest zum Markt.
    /// </summary>
    public static (float Cost, float Floored, float FloorPrice) BuyCost(Universe u, Ship ship, Station st, GoodId good, float units, float price)
    {
        if (Piecewise(u, ship, st, good) is { } e2) return (Curve(u, st, good, e2, units, price, sale: false), 0f, price);
        if (!Applies(u, ship) || Post(u, st, good) is not { } p || !SaleActive(u, p) || p.SoldPrice <= price) return (units * price, 0f, price);
        // Runde 32 (V1): solange der Verkaufs-Posten offen ist, jede gekaufte Einheit (Prüfung: verkaufen, Preis fällt, mehr als verkauft zurückkaufen)
        float c = u.Rules.BuybackWholeTrade ? units : MathF.Min(units, p.SoldQty);
        return (c * p.SoldPrice + (units - c) * price, c, p.SoldPrice);
    }

    /// <summary>Höchste ganze Menge (≤ <paramref name="units"/>), deren <see cref="BuyCost"/> die Kasse <paramref name="credits"/> bezahlt.</summary>
    public static float Affordable(Universe u, Ship ship, Station st, GoodId good, float units, float price, float credits)
    {
        if (price <= 0f) return units;
        if (Piecewise(u, ship, st, good) is { } e2)
        {
            float n = MathF.Min(units, MathF.Floor(credits / price));   // jede Einheit kostet mindestens den Tickpreis
            while (n > 0f && Curve(u, st, good, e2, n, price, sale: false) > credits) n--;
            return n;
        }
        if (!Applies(u, ship) || Post(u, st, good) is not { } p || !SaleActive(u, p) || p.SoldPrice <= price) return MathF.Min(units, MathF.Floor(credits / price));
        if (u.Rules.BuybackWholeTrade) return MathF.Min(units, MathF.Floor(credits / p.SoldPrice + 0.0001f));
        float c = MathF.Min(units, p.SoldQty);
        float atFloor = MathF.Floor(credits / p.SoldPrice);
        if (atFloor < c) return MathF.Min(units, atFloor);
        float rest = credits - c * p.SoldPrice;
        return MathF.Min(units, c + MathF.Floor(rest / price + 0.0001f));
    }

    /// <summary>Nach einem Kauf: zuerst den Verkaufs-Posten abbauen, der Rest wird ein Kauf-Posten zum Marktpreis dieser Einheiten (gewichteter Stückpreis).</summary>
    /// <param name="marketPrice">Marktpreis beim Kauf (der Preis der nicht gedeckelten Einheiten — sonst trüge der Rest den höheren Mindestpreis mit).</param>
    /// <param name="stockBefore">Bestand der Station vor diesem Kauf.</param>
    public static void OnBought(Universe u, Ship ship, Station st, GoodId good, float units, float marketPrice, float stockBefore)
    {
        if (!Applies(u, ship) || units <= 0f) return;
        var p = Post(u, st, good);
        float rest = units;
        // Runde 32b: der Gegenhandel baut die Menge des Verkaufsteils ab, beendet ihn aber nicht (der Boden gilt bis zum Verfall im Tick)
        if (p is { SoldQty: > 0f }) { float c = MathF.Min(rest, p.SoldQty); p.SoldQty -= c; rest -= c; }
        if (rest > 0.0001f)
        {
            p ??= u.Ledger[Key(u, st, good)] = new LedgerPost();
            float unit = marketPrice;
            // Runde 30 (Sonde): kein gewichteter Schnitt — sonst hob ein älterer, teurer gekaufter Rest den Deckel für neu und billig gekaufte Einheiten;
            // der Deckel gilt zum niedrigsten Kaufpreis der offenen Menge
            if (!BuyActive(u, p)) { p.BoughtStockBefore = stockBefore; p.BoughtPrice = unit; p.BoughtQty = rest; }
            else { p.BoughtPrice = MathF.Min(p.BoughtPrice, unit); p.BoughtQty += rest; }
            p.BoughtOpen = true;
        }
        Touch(u, p);
        if (p is not null && p.Empty) u.Ledger.Remove(Key(u, st, good));
    }

    /// <summary>Nach einem Verkauf: zuerst den Kauf-Posten abbauen, der Rest wird ein Verkaufs-Posten zum Marktpreis dieser Einheiten (gewichteter Stückpreis).</summary>
    /// <param name="marketPrice">Marktpreis beim Verkauf (der Preis der nicht gedeckelten Einheiten).</param>
    /// <param name="stockBefore">Bestand der Station vor diesem Verkauf.</param>
    public static void OnSold(Universe u, Ship ship, Station st, GoodId good, float units, float marketPrice, float stockBefore)
    {
        if (!Applies(u, ship) || units <= 0f) return;
        var p = Post(u, st, good);
        float rest = units;
        // Runde 32b: der Gegenhandel baut die Menge des Kaufteils ab, beendet ihn aber nicht (der Deckel gilt bis zum Verfall im Tick)
        if (p is { BoughtQty: > 0f }) { float c = MathF.Min(rest, p.BoughtQty); p.BoughtQty -= c; rest -= c; }
        if (rest > 0.0001f && !st.IsBuildSite && !Simulation.ExportRules.Buys(u, st, good))   // Baustellen und Exportsenken verkaufen nicht zurück
        {
            p ??= u.Ledger[Key(u, st, good)] = new LedgerPost();
            float unit = marketPrice;
            // Spiegel: der Mindestpreis gilt zum höchsten Verkaufspreis der offenen Menge
            if (!SaleActive(u, p)) { p.SoldStockBefore = stockBefore; p.SoldPrice = unit; p.SoldQty = rest; }
            else { p.SoldPrice = MathF.Max(p.SoldPrice, unit); p.SoldQty += rest; }
            p.SoldOpen = true;
        }
        Touch(u, p);
        if (p is not null && p.Empty) u.Ledger.Remove(Key(u, st, good));
    }

    /// <summary>
    /// Runde 32b: Buchungs-Tick merken (der Verfall prüft erst danach). Mit der Runde-31-Regel spiegeln die Kennzeichen nur die Menge (Messmodus wie damals).
    /// </summary>
    static void Touch(Universe u, LedgerPost? p)
    {
        if (p is null) return;
        p.LastTick = u.Tick;
        if (!u.Rules.BuybackWholeTrade) { p.BoughtOpen = p.BoughtQty > 0.0001f; p.SoldOpen = p.SoldQty > 0.0001f; }
    }

    /// <summary>
    /// Runde 31: EINE Buchung eines Kaufs aus dem Markt einer Station (Bestand, Stationskasse, Zahler, Rückkauf-Posten) — alle Wege, auf denen Ware gegen
    /// Geld aus einem Stationsmarkt kommt (Handel, Flottenhandel, Ausrüster, Munition, Raketen), buchen hierüber, damit keiner den Deckel vergisst.
    /// Wohin die Ware geht (Laderaum, Fit, Magazin), bleibt beim Aufrufer. Für NPC-Schiffe genau dieselbe Rechnung wie vorher (Menge × Preis).
    /// </summary>
    public static Booking BookBuy(Universe u, Ship ship, Station st, GoodId good, float units, ICreditAccount payer, float? price = null)
    {
        var e = st.Market[good];
        float p = price ?? e.Price;
        var (cost, floored, floorPrice) = BuyCost(u, ship, st, good, units, p);
        float before = e.Amount;
        e.Amount -= units;
        st.Credits += cost;
        payer.Credits -= cost;
        if (floored > 0f) u.Events.Publish(new Events.LedgerApplied(ship.Id, st.Id, good, floored, cost - units * p, false));
        OnBought(u, ship, st, good, units, EffectivePrice(u, p, cost, units), before);
        return new Booking(units, cost, floored, floorPrice);
    }

    /// <summary>
    /// Runde 31: EINE Buchung eines Verkaufs an den Markt einer Station; <paramref name="paidUnits"/> = bezahlte Menge, wenn die Station nicht alles zahlen kann
    /// (Flottenhändler liefern dann trotzdem alles ab). Wie <see cref="BookBuy"/> für alle Wege (Handel, Flottenhandel, Ausrüster-Ausbau).
    /// </summary>
    public static Booking BookSale(Universe u, Ship ship, Station st, GoodId good, float units, ICreditAccount payee, float? paidUnits = null, float? price = null)
    {
        var e = st.Market[good];
        float p = price ?? e.Price;
        float paid = paidUnits ?? units;
        var (revenue, capped, capPrice) = SaleValue(u, ship, st, good, paid, p);
        float before = e.Amount;
        e.Amount += units;
        st.Credits -= revenue;
        payee.Credits += revenue;
        if (capped > 0f) u.Events.Publish(new Events.LedgerApplied(ship.Id, st.Id, good, capped, paid * p - revenue, true));
        OnSold(u, ship, st, good, paid, EffectivePrice(u, p, revenue, paid), before);
        return new Booking(paid, revenue, capped, capPrice);
    }

    /// <summary>V2 (Messung): Markteintrag, wenn der stückweise Preis für dieses Schiff gilt (Spieler-Akteur, Regel an), sonst null.</summary>
    static StockEntry? Piecewise(Universe u, Ship ship, Station st, GoodId good) =>
        u.Rules.BuybackPiecewise && ship.Owner == u.PlayerActor && u.PlayerActor.Value != 0 && st.Market.TryGetValue(good, out var e) && !st.IsBuildSite ? e : null;

    /// <summary>
    /// V2 (Messung): Summe über die Einheiten eines Handels, jede zum Tickpreis × Kurvenverhältnis f(Bestand nach den vorigen Einheiten) / f(Bestand jetzt),
    /// f = Bestandsformel des Markts (<see cref="Simulation.MarketSystem.StockPrice"/>); Verkauf: der Bestand steigt (Preis fällt), Kauf: er sinkt (Preis steigt).
    /// Bruchteile werden anteilig gerechnet.
    /// </summary>
    static float Curve(Universe u, Station st, GoodId good, StockEntry e, float units, float price, bool sale)
    {
        if (units <= 0f) return 0f;
        var g = u.GoodOf(good);
        float f0 = Simulation.MarketSystem.StockPrice(g, e, e.Amount);
        if (f0 <= 0f) return units * price;
        float sum = 0f, done = 0f, a = e.Amount;
        while (done < units - 1e-4f)
        {
            float step = MathF.Min(1f, units - done);
            sum += step * price * Simulation.MarketSystem.StockPrice(g, e, a) / f0;
            a += sale ? step : -step;
            done += step;
        }
        return sum;
    }

    /// <summary>
    /// Runde 32: Preis, den ein neuer Posten trägt — bei V1 der tatsächlich gezahlte bzw. erhaltene Stückpreis (ein Verkauf unter offenem Kauf-Posten legt den
    /// Spiegel zum gedeckelten Preis an, nicht zum Marktpreis), sonst wie Runde 30/31 der Marktpreis.
    /// </summary>
    static float EffectivePrice(Universe u, float market, float money, float units) =>
        u.Rules.BuybackWholeTrade && units > 0.0001f ? money / units : market;

    /// <summary>
    /// Runde 32: Mehrpreis für Material, das ein Dienst aus dem Lager der Station verbraucht (Schiffskauf, Upgrade, Reparatur): mit offenem Verkaufs-Posten
    /// kostet es mindestens den erhaltenen Preis (<see cref="BuyCost"/>) — sonst Platten teuer verkaufen, dann zum gefallenen Preis reparieren lassen.
    /// Liefert die Differenz zum Marktpreis (0 für NPCs und ohne Posten).
    /// </summary>
    public static float MaterialSurcharge(Universe u, Ship ship, Station st, GoodId good, float units)
    {
        if (units <= 0f || !Applies(u, ship) || !st.Market.TryGetValue(good, out var e) || e.Price <= 0f) return 0f;
        return BuyCost(u, ship, st, good, units, e.Price).Cost - units * e.Price;
    }

    /// <summary>Runde 32: Stückpreis von Material aus dem Lager dieser Station für dieses Schiff (Marktpreis, mit offenem Verkaufs-Posten mindestens dessen Preis).</summary>
    public static float MaterialPrice(Universe u, Ship? ship, Station st, GoodId good)
    {
        float price = st.Market.TryGetValue(good, out var e) ? e.Price : 0f;
        if (ship is null || price <= 0f || !Applies(u, ship) || Post(u, st, good) is not { } p || !SaleActive(u, p)) return price;
        return MathF.Max(price, p.SoldPrice);
    }

    /// <summary>
    /// Runde 32: Material, das ein Dienst verbraucht hat, zählt als Kauf hier (Kauf-Posten zum gezahlten Stückpreis) — sonst hebt der Verbrauch den Preis und
    /// mitgebrachte Ware ginge dann teurer an dieselbe Station.
    /// </summary>
    public static void OnConsumed(Universe u, Ship ship, Station st, GoodId good, float units, float unitPrice, float stockBefore) =>
        OnBought(u, ship, st, good, units, unitPrice, stockBefore);

    /// <summary>Runde 31: wirksamer Stückpreis eines Kaufs von <paramref name="units"/> Einheiten (für <c>ServiceOffer.Price</c>).</summary>
    public static float BuyUnitPrice(Universe u, Ship ship, Station st, GoodId good, float units, float price) =>
        units > 0f ? BuyCost(u, ship, st, good, units, price).Cost / units : MathF.Max(price, Post(u, st, good) is { } p && SaleActive(u, p) && Applies(u, ship) ? p.SoldPrice : price);

    /// <summary>Runde 31: wirksamer Stückpreis eines Verkaufs von <paramref name="units"/> Einheiten (für <c>ServiceOffer.Price</c>).</summary>
    public static float SaleUnitPrice(Universe u, Ship ship, Station st, GoodId good, float units, float price) =>
        units > 0f ? SaleValue(u, ship, st, good, units, price).Revenue / units : MathF.Min(price, Post(u, st, good) is { } p && BuyActive(u, p) && Applies(u, ship) ? p.BoughtPrice : price);

    /// <summary>
    /// Hinweis für Angebot und Editor: wirksamer Deckel beim Verkauf hier (null = keiner). Runde 32b: ehrlich — er gilt für jede Menge (auch mitgebrachte Ware)
    /// und endet erst, wenn Preis und Bestand der Station zurück sind, nicht durch einen Teilverkauf.
    /// </summary>
    public static string? SaleNote(Universe u, Ship ship, Station st, GoodId good) =>
        CapActive(u, ship, st, good) is { } p
            ? (u.Rules.BuybackWholeTrade ? $"Wegen deines Kaufs hier: höchstens {p.BoughtPrice:0.0} je Einheit, bis Preis und Lager zurück sind"
                : $"Rückkauf: höchstens dein Kaufpreis {p.BoughtPrice:0.0} für {p.BoughtQty:0.#} E.") : null;

    /// <summary>Runde 32b: kurze Fassung des Deckels für enge Zeilen („Deckel 183,0“).</summary>
    public static string? SaleNoteShort(Universe u, Ship ship, Station st, GoodId good) =>
        CapActive(u, ship, st, good) is { } p ? $"Deckel {p.BoughtPrice:0.0}" : null;

    /// <summary>Hinweis für Angebot und Editor: wirksamer Mindestpreis beim Kauf hier (null = keiner). Runde 32b: gilt für jede Menge, bis Preis und Lager zurück sind.</summary>
    public static string? BuyNote(Universe u, Ship ship, Station st, GoodId good) =>
        FloorActive(u, ship, st, good) is { } p
            ? (u.Rules.BuybackWholeTrade ? $"Wegen deines Verkaufs hier: mindestens {p.SoldPrice:0.0} je Einheit, bis Preis und Lager zurück sind"
                : $"Rückkauf deiner Lieferung: mindestens {p.SoldPrice:0.0} für {p.SoldQty:0.#} E.") : null;

    /// <summary>Runde 32b: kurze Fassung des Bodens für enge Zeilen („Boden 66,2“).</summary>
    public static string? BuyNoteShort(Universe u, Ship ship, Station st, GoodId good) =>
        FloorActive(u, ship, st, good) is { } p ? $"Boden {p.SoldPrice:0.0}" : null;

    static LedgerPost? CapActive(Universe u, Ship ship, Station st, GoodId good) =>
        Applies(u, ship) && Post(u, st, good) is { } p && BuyActive(u, p) && st.Market.TryGetValue(good, out var e) && p.BoughtPrice < e.Price ? p : null;
    static LedgerPost? FloorActive(Universe u, Ship ship, Station st, GoodId good) =>
        Applies(u, ship) && Post(u, st, good) is { } p && SaleActive(u, p) && st.Market.TryGetValue(good, out var e) && p.SoldPrice > e.Price ? p : null;
}

/// <summary>Runde 31: Ergebnis einer Buchung mit dem Markt einer Station — Menge, Geld, davon zum Rückkaufpreis gerechnete Einheiten und dieser Preis.</summary>
public readonly record struct Booking(float Units, float Money, float Ledgered, float LedgerPrice);
