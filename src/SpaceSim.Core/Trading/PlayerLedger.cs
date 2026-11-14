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
    /// Verfall durch Bestandsrückkehr, im Tick (<see cref="Simulation.LedgerSystem"/>) und nach jedem Spielerhandel: ein Kauf-Posten verfällt, sobald der
    /// Bestand den Stand vor dem ersten Kauf wieder erreicht, ein Verkaufs-Posten, sobald er wieder auf dem Stand vor dem ersten Verkauf ist; der Verfall
    /// ist endgültig (ein später wieder sinkender Bestand belebt ihn nicht). Leere Posten und Posten weggefallener Stationen werden gelöscht.
    /// </summary>
    public static void Prune(Universe u)
    {
        if (u.Ledger.Count == 0) return;
        List<(int, int, int)>? gone = null;
        foreach (var (k, p) in u.Ledger)
        {
            float amount = u.Stations.TryGetValue(new StationId(k.Item2), out var st) && st.Market.TryGetValue(new GoodId(k.Item3), out var e) ? e.Amount : float.NaN;
            if (float.IsNaN(amount)) { p.BoughtQty = 0f; p.SoldQty = 0f; }
            else
            {
                // Runde 30 (Sonde): nur Bestandsrückkehr genügt nicht — bei Verbrauchern bestimmt oft die Zahlungsbereitschaft den Preis, nicht der Bestand
                // (Elektronikfabrik Beta: Kupfer 20,6 bei Bestand 170, einen Tick später 50,0 bei 250). Darum zusätzlich: der Markt zahlt nicht mehr als den
                // Kaufpreis bzw. verlangt nicht weniger als den Verkaufspreis — erst dann ist der Deckel ohnehin wirkungslos
                float price = u.Stations[new StationId(k.Item2)].Market[new GoodId(k.Item3)].Price;
                if (p.BoughtQty > 0f && amount >= p.BoughtStockBefore - 0.0001f && price <= p.BoughtPrice + 0.0001f) p.BoughtQty = 0f;
                if (p.SoldQty > 0f && amount <= p.SoldStockBefore + 0.0001f && price >= p.SoldPrice - 0.0001f) p.SoldQty = 0f;
            }
            if (p.Empty) (gone ??= new()).Add(k);
        }
        if (gone is not null) foreach (var k in gone) u.Ledger.Remove(k);
    }

    /// <summary>
    /// Erlös eines Verkaufs von <paramref name="units"/> Einheiten zum Marktpreis <paramref name="price"/>: die ersten bis zu Kauf-Posten-Menge Einheiten zu
    /// min(Markt, Kaufpreis), der Rest zum Markt. <c>Capped</c> = so viele Einheiten zum Rückkaufpreis <c>CapPrice</c>.
    /// </summary>
    public static (float Revenue, float Capped, float CapPrice) SaleValue(Universe u, Ship ship, Station st, GoodId good, float units, float price)
    {
        if (!Applies(u, ship) || Post(u, st, good) is not { BoughtQty: > 0f } p || p.BoughtPrice >= price) return (units * price, 0f, price);
        float c = MathF.Min(units, p.BoughtQty);
        return (c * p.BoughtPrice + (units - c) * price, c, p.BoughtPrice);
    }

    /// <summary>
    /// Kosten eines Kaufs von <paramref name="units"/> Einheiten zum Marktpreis <paramref name="price"/>: die ersten bis zu Verkaufs-Posten-Menge Einheiten zu
    /// max(Markt, Verkaufspreis), der Rest zum Markt.
    /// </summary>
    public static (float Cost, float Floored, float FloorPrice) BuyCost(Universe u, Ship ship, Station st, GoodId good, float units, float price)
    {
        if (!Applies(u, ship) || Post(u, st, good) is not { SoldQty: > 0f } p || p.SoldPrice <= price) return (units * price, 0f, price);
        float c = MathF.Min(units, p.SoldQty);
        return (c * p.SoldPrice + (units - c) * price, c, p.SoldPrice);
    }

    /// <summary>Höchste ganze Menge (≤ <paramref name="units"/>), deren <see cref="BuyCost"/> die Kasse <paramref name="credits"/> bezahlt.</summary>
    public static float Affordable(Universe u, Ship ship, Station st, GoodId good, float units, float price, float credits)
    {
        if (price <= 0f) return units;
        if (!Applies(u, ship) || Post(u, st, good) is not { SoldQty: > 0f } p || p.SoldPrice <= price) return MathF.Min(units, MathF.Floor(credits / price));
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
        if (p is { SoldQty: > 0f }) { float c = MathF.Min(rest, p.SoldQty); p.SoldQty -= c; rest -= c; }
        if (rest > 0.0001f)
        {
            p ??= u.Ledger[Key(u, st, good)] = new LedgerPost();
            float unit = marketPrice;
            // Runde 30 (Sonde): kein gewichteter Schnitt — sonst hob ein älterer, teurer gekaufter Rest den Deckel für neu und billig gekaufte Einheiten;
            // der Deckel gilt zum niedrigsten Kaufpreis der offenen Menge
            if (p.BoughtQty <= 0f) { p.BoughtStockBefore = stockBefore; p.BoughtPrice = unit; p.BoughtQty = rest; }
            else { p.BoughtPrice = MathF.Min(p.BoughtPrice, unit); p.BoughtQty += rest; }
        }
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
        if (p is { BoughtQty: > 0f }) { float c = MathF.Min(rest, p.BoughtQty); p.BoughtQty -= c; rest -= c; }
        if (rest > 0.0001f && !st.IsBuildSite && !Simulation.ExportRules.Buys(u, st, good))   // Baustellen und Exportsenken verkaufen nicht zurück
        {
            p ??= u.Ledger[Key(u, st, good)] = new LedgerPost();
            float unit = marketPrice;
            // Spiegel: der Mindestpreis gilt zum höchsten Verkaufspreis der offenen Menge
            if (p.SoldQty <= 0f) { p.SoldStockBefore = stockBefore; p.SoldPrice = unit; p.SoldQty = rest; }
            else { p.SoldPrice = MathF.Max(p.SoldPrice, unit); p.SoldQty += rest; }
        }
        if (p is not null && p.Empty) u.Ledger.Remove(Key(u, st, good));
    }

    /// <summary>Hinweis für Angebot und Editor: wirksamer Deckel beim Verkauf hier (null = keiner).</summary>
    public static string? SaleNote(Universe u, Ship ship, Station st, GoodId good) =>
        Applies(u, ship) && Post(u, st, good) is { BoughtQty: > 0f } p && st.Market.TryGetValue(good, out var e) && p.BoughtPrice < e.Price
            ? $"Rückkauf: höchstens dein Kaufpreis {p.BoughtPrice:0.0} für {p.BoughtQty:0.#} E." : null;

    /// <summary>Hinweis für Angebot und Editor: wirksamer Mindestpreis beim Kauf hier (null = keiner).</summary>
    public static string? BuyNote(Universe u, Ship ship, Station st, GoodId good) =>
        Applies(u, ship) && Post(u, st, good) is { SoldQty: > 0f } p && st.Market.TryGetValue(good, out var e) && p.SoldPrice > e.Price
            ? $"Rückkauf deiner Lieferung: mindestens {p.SoldPrice:0.0} für {p.SoldQty:0.#} E." : null;
}
