using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Content;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 32: Rückkaufdeckel V1 — solange ein eigener Kauf-Posten an (Station, Ware) offen ist, bringt JEDER Verkauf dieser Ware dort höchstens den niedrigsten
/// offenen Kaufpreis (auch mitgebrachte Ware); solange ein Verkaufs-Posten offen ist, kostet JEDER Kauf dort mindestens den höchsten offenen Verkaufspreis.
/// Prüfer-Schleifen A (mitgebrachte Ware nach eigenem Kauf teurer verkaufen) und B (nach eigenem Verkauf mehr als verkauft billiger zurückkaufen).
/// </summary>
public class Runde32Tests
{
    static readonly Lazy<string> Saved = new(() =>
    {
        var u = WorldBuilder.BuildDefault(11, "trader");
        new SimulationRunner().Run(u, 2000);
        return SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
    });
    static Universe World(bool wholeTrade = true)
    {
        var u = SaveGameJson.Read(Saved.Value).Universe;
        u.Rules.BuybackWholeTrade = wholeTrade;
        var p = u.Ships[u.PlayerShip]; p.Cargo.Clear(); u.WalletOf(p).Credits = 1_000_000f;
        foreach (var st in u.Stations.Values) st.Credits = MathF.Max(st.Credits, 1_000_000f);
        return u;
    }

    /// <summary>Verbraucherware mit Bestand ≥ 40 und Annahme ≥ 40 (beide Richtungen handelbar).</summary>
    static (Station St, GoodId G) Entry(Universe u) =>
        u.StationsById().Where(s => s.Dockable && !s.IsBuildSite).SelectMany(s => s.Market.Where(kv => kv.Value.Amount >= 40f && kv.Value.Price > 0f
            && !ExportRules.Buys(u, s, kv.Key) && !s.Produces(kv.Key) && TradeActions.SaleCap(u, s, kv.Key) >= 40f).Select(kv => (s, kv.Key))).First();

    static float SellRevenue(Universe u, Ship ship, Station st, GoodId g, float n) => TradeActions.Sell(u, ship, st, g, n).Revenue;

    /// <summary>Schleife A: 10 mitgebracht, 10 gekauft, Preis steigt (Folge des Kaufs), alle 20 verkauft — mit V1 nie über dem Kaufpreis; mit Runde 31 lief die mitgebrachte Ware zum hohen Preis.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SchleifeA_MitgebrachteWare_NieUeberDemEigenenKaufpreis(bool v1)
    {
        var u = World(v1);
        var (st, g) = Entry(u);
        var p = u.Ships[u.PlayerShip];
        var e = st.Market[g];
        p.Cargo[g] = 10f;
        var b = TradeActions.Buy(u, p, st, g, 10f);
        float paid = b.Cost / b.Units;
        e.Price = paid * 1.5f;   // im nächsten Tick: Bestand kleiner → Preis höher
        float rev = SellRevenue(u, p, st, g, 20f);
        if (v1) Assert.True(rev <= 20f * paid + 0.5f, $"V1: Erlös {rev} > 20 × {paid}");
        else Assert.True(rev > 20f * paid + 1f, $"Runde-31-Regel: die Lücke ist messbar ({rev} vs {20f * paid})");
    }

    /// <summary>Schleife B (Spiegel): 10 verkauft, Preis fällt, 30 zurückgekauft — mit V1 jede Einheit mindestens zum eigenen Verkaufspreis.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SchleifeB_MehrZurueckkaufen_NieUnterDemEigenenVerkaufspreis(bool v1)
    {
        var u = World(v1);
        var (st, g) = Entry(u);
        var p = u.Ships[u.PlayerShip];
        var e = st.Market[g];
        p.Cargo[g] = 10f;
        var s = TradeActions.Sell(u, p, st, g, 10f);
        float got = s.Revenue / s.Sold[g];
        e.Price = got * 0.5f;
        var b = TradeActions.Buy(u, p, st, g, 30f);
        Assert.True(b.Units >= 30f - 0.01f);
        if (v1) Assert.True(b.Cost >= 30f * got - 0.5f, $"V1: Kosten {b.Cost} < 30 × {got}");
        else Assert.True(b.Cost < 30f * got - 1f, "Runde-31-Regel: die Lücke ist messbar");
    }

    /// <summary>Mehrstufig: kaufen, Preis steigt, nachkaufen (teurer), Preis steigt, alles + mitgebrachte verkaufen — höchstens der niedrigste offene Kaufpreis.</summary>
    [Fact]
    public void Mehrstufig_DeckelIstDerNiedrigsteOffeneKaufpreis()
    {
        var u = World();
        var (st, g) = Entry(u);
        var p = u.Ships[u.PlayerShip];
        var e = st.Market[g];
        p.Cargo[g] = 10f;
        var b1 = TradeActions.Buy(u, p, st, g, 5f);
        float p1 = b1.Cost / b1.Units;
        e.Price = p1 * 1.3f;
        TradeActions.Buy(u, p, st, g, 5f);
        e.Price = p1 * 1.7f;
        float rev = SellRevenue(u, p, st, g, 20f);
        Assert.True(rev <= 20f * p1 + 0.5f, $"Erlös {rev}, niedrigster Kaufpreis {p1}");
    }

    /// <summary>Zweites eigenes Schiff (auch ein Flottenschiff) verkauft mitgebrachte Ware: der Posten hängt am Akteur — gedeckelt; ebenso die Flottenbuchung.</summary>
    [Fact]
    public void ZweitesSchiffUndFlotte_GedeckeltWieDasSpielerschiff()
    {
        var u = World();
        var (st, g) = Entry(u);
        var p = u.Ships[u.PlayerShip];
        var e = st.Market[g];
        var b = TradeActions.Buy(u, p, st, g, 20f);   // der erste Verkauf baut 10 vom Posten ab, der Rest bleibt für die Flottenbuchung offen
        float paid = b.Cost / b.Units;
        var id = u.NewShipId();
        var second = ShipPurchase.Create(u, id, p.ModelKey, p.Slots, p.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, st, 0f);
        u.AddShip(second);
        second.Cargo[g] = 10f;
        e.Price = paid * 1.6f;
        float rev = SellRevenue(u, second, st, g, 10f);
        Assert.True(rev <= 10f * paid + 0.5f, $"zweites Schiff: {rev} > 10 × {paid}");
        // Flottenbuchung (TraderSystem.Deliver bucht über BookSale) — auch mit bezahlter Teilmenge
        second.Cargo[g] = 10f;
        var bk = PlayerLedger.BookSale(u, second, st, g, 10f, u.WalletOf(second), paidUnits: 6f);
        Assert.True(bk.Money <= 6f * paid + 0.5f);
    }

    /// <summary>Ehrlicher Handel: bei A kaufen, bei B (andere Station) verkaufen — kein Deckel, Erlös = Marktpreis; NPCs nie.</summary>
    [Fact]
    public void EhrlicherHandel_AndereStation_UndNpc_Ungedeckelt()
    {
        var u = World();
        var (st, g) = Entry(u);
        var p = u.Ships[u.PlayerShip];
        TradeActions.Buy(u, p, st, g, 10f);
        var other = u.StationsById().First(s => s.Id != st.Id && s.Dockable && s.Market.TryGetValue(g, out var x) && x.Price > 0f && TradeActions.SaleCap(u, s, g) >= 10f
            && !ExportRules.Buys(u, s, g));
        float price = other.Market[g].Price;
        float rev = SellRevenue(u, p, other, g, 10f);
        Assert.Equal(10f * price, rev, 1);
        var npc = u.Ships.Values.First(s => !s.IsPlayer && s.Owner != u.PlayerActor);
        Assert.Equal(10f * st.Market[g].Price, PlayerLedger.SaleValue(u, npc, st, g, 10f, st.Market[g].Price).Revenue, 2);
    }

    /// <summary>Reparatur: Platten teuer an die Station verkauft, Preis fällt — die Reparatur rechnet die Platten mindestens zum erhaltenen Preis (Spiegelboden).</summary>
    [Fact]
    public void Reparatur_PlattenMitSpiegelboden()
    {
        var u = World();
        var p = u.Ships[u.PlayerShip];
        var plates = u.FindGood("hull_plates")!.Id;
        var st = u.StationsById().First(s => s.Dockable && s.Market.TryGetValue(plates, out var x) && x.Price > 0f && x.Amount > 5f
            && u.StationTypes[s.TypeKey].Services.Contains(RepairService.ServiceKey) && TradeActions.SaleCap(u, s, plates) >= 5f);
        var e = st.Market[plates];
        p.Cargo[plates] = 5f;
        var s1 = TradeActions.Sell(u, p, st, plates, 5f);
        float got = s1.Revenue / s1.Sold[plates];
        e.Price = got * 0.4f;
        float perHull = RepairService.PricePerHull(u, st, p);
        Assert.True(perHull >= got * u.Rules.RepairPlatesPerHull * RepairService.LaborFactor - 0.001f, $"Hüllenpreis {perHull} statt Boden {got}");
        Assert.Equal(e.Price * u.Rules.RepairPlatesPerHull * RepairService.LaborFactor, RepairService.PricePerHull(u, st), 4);   // ohne Schiff (NPC-Weg) Marktpreis
        // Material-Mehrpreis für Upgrades/Schiffskauf über denselben Helfer
        Assert.True(PlayerLedger.MaterialSurcharge(u, p, st, plates, 2f) >= 2f * (got - e.Price) - 0.01f);
    }

    /// <summary>Anzeige: Hinweis nennt die neue Regel; Angebotspreis = wirksamer Preis.</summary>
    [Fact]
    public void Hinweis_UndAngebotspreis_NachV1()
    {
        var u = World();
        var (st, g) = Entry(u);
        var p = u.Ships[u.PlayerShip];
        var b = TradeActions.Buy(u, p, st, g, 5f);
        float paid = b.Cost / b.Units;
        st.Market[g].Price = paid * 2f;
        p.Cargo[g] = 20f;   // mehr als gekauft: auch die übrigen sind gedeckelt
        Assert.StartsWith("Wegen deines Kaufs hier: höchstens", PlayerLedger.SaleNote(u, p, st, g));
        var sell = new TradeService().Offers(u, st, p).First(o => o.Key == $"sell:{g.Value}");
        Assert.True(sell.Price <= paid + 0.001f, $"Angebot {sell.Price}, gezahlt {paid}");
    }

    // ───────── Runde 32b: Mehrfachbuchungen im selben Tick, mit echtem Tick (MarketSystem bepreist) ─────────

    /// <summary>Verbraucherware, deren Preis nach einem Kauf von 10 im nächsten Tick steigt (Bestand unter Wunschbestand, Annahme ≥ 40).</summary>
    /// <remarks>Nur Einträge, deren Preis die reine Bestandsformel ist (kein Zahlungsbereitschafts- oder Verbraucherdeckel), im ungeklemmten Bereich.</remarks>
    static (Station St, GoodId G) Responsive(Universe u) =>
        u.StationsById().Where(s => s.Dockable && !s.IsBuildSite).SelectMany(s => s.Market.Where(kv => kv.Value.Amount >= 30f
            && kv.Value.Amount >= kv.Value.Target * 0.35f && kv.Value.Amount <= kv.Value.Target * 0.85f
            && kv.Value.Price > 0f && MathF.Abs(kv.Value.Price - MarketSystem.StockPrice(u.GoodOf(kv.Key), kv.Value, kv.Value.Amount)) < 0.01f
            && !ExportRules.Buys(u, s, kv.Key) && !s.Produces(kv.Key) && TradeActions.SaleCap(u, s, kv.Key) >= 40f).Select(kv => (s, kv.Key))).First();

    static void Tick(Universe u) => new SimulationRunner().Run(u, 1);

    /// <summary>Prüfer S5c/S5d: Kauf-Posten durch Teilverkauf „leeren“, Rest im selben Tick — der Deckel gilt weiter (vorher lief der Rest zum getriebenen Preis).</summary>
    [Fact]
    public void Teilverkauf_LeertPostenNicht_RestImSelbenTickGedeckelt()
    {
        var u = World();
        var (st, g) = Responsive(u);
        var p = u.Ships[u.PlayerShip];
        p.Cargo[g] = 10f;
        var b = TradeActions.Buy(u, p, st, g, 10f);
        float paid = b.Cost / b.Units;
        Tick(u);
        Assert.True(st.Market[g].Price > paid * 1.01f, $"Voraussetzung: Preis steigt nach dem Kauf ({paid} → {st.Market[g].Price})");
        float r1 = SellRevenue(u, p, st, g, b.Units);   // leert die Postenmenge
        Assert.NotNull(PlayerLedger.Post(u, st, g));
        float r2 = SellRevenue(u, p, st, g, 10f);       // mitgebrachte Ware im selben Tick
        Assert.True(r1 + r2 <= (b.Units + 10f) * paid + 0.5f, $"Erlös {r1 + r2} > {(b.Units + 10f) * paid}");
    }

    /// <summary>Prüfer S7c: Spiegel — verkaufen, echter Tick (Preis fällt), dieselbe Menge zurück, dann mehr im selben Tick: alles mindestens zum Verkaufspreis.</summary>
    [Fact]
    public void Spiegel_ZurueckkaufLeertPostenNicht_MehrImSelbenTickMitBoden()
    {
        var u = World();
        var (st, g) = Responsive(u);
        var p = u.Ships[u.PlayerShip];
        p.Cargo[g] = 10f;
        var s1 = TradeActions.Sell(u, p, st, g, 10f);
        float got = s1.Revenue / s1.Sold[g];
        Tick(u);
        Assert.True(st.Market[g].Price < got * 0.99f, $"Voraussetzung: Preis fällt nach dem Verkauf ({got} → {st.Market[g].Price})");
        var b1 = TradeActions.Buy(u, p, st, g, 10f);
        var b2 = TradeActions.Buy(u, p, st, g, 20f);
        Assert.True(b1.Cost + b2.Cost >= (b1.Units + b2.Units) * got - 0.5f, $"Kosten {b1.Cost + b2.Cost} < {(b1.Units + b2.Units) * got}");
    }

    /// <summary>Zwei Schiffe im selben Tick: A kauft, echter Tick, A verkauft seine Menge zurück, B verkauft mitgebrachte Ware sofort — gedeckelt.</summary>
    [Fact]
    public void ZweiSchiffe_GleicherTick_Gedeckelt()
    {
        var u = World();
        var (st, g) = Responsive(u);
        var p = u.Ships[u.PlayerShip];
        var b = TradeActions.Buy(u, p, st, g, 10f);
        float paid = b.Cost / b.Units;
        var second = ShipPurchase.Create(u, u.NewShipId(), p.ModelKey, p.Slots, p.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, st, 0f);
        u.AddShip(second);
        second.Cargo[g] = 10f;
        Tick(u);
        SellRevenue(u, p, st, g, b.Units);
        float rB = SellRevenue(u, second, st, g, 10f);
        Assert.True(rB <= 10f * paid + 0.5f, $"zweites Schiff {rB} > 10 × {paid}");
    }

    /// <summary>Prüfer S5b: Einzelstücke im selben Tick — kein Stück über dem Kaufpreis.</summary>
    [Fact]
    public void Einzelstuecke_GleicherTick_KeinStueckUeberDemKaufpreis()
    {
        var u = World();
        var (st, g) = Responsive(u);
        var p = u.Ships[u.PlayerShip];
        p.Cargo[g] = 10f;
        var b = TradeActions.Buy(u, p, st, g, 10f);
        float paid = b.Cost / b.Units;
        Tick(u);
        float total = 0f; int n = 0;
        for (int i = 0; i < 20; i++) { var r = TradeActions.Sell(u, p, st, g, 1f); if (r.Sold.GetValueOrDefault(g) <= 0f) break; total += r.Revenue; n++; }
        Assert.True(n >= 15, $"nur {n} Stücke verkauft");
        Assert.True(total <= n * paid + 0.5f, $"Erlös {total} > {n} × {paid}");
    }

    /// <summary>Der Posten bleibt mit Restmenge 0 bestehen, übersteht Speichern/Laden und endet erst im Tick, wenn Preis und Bestand zurück sind.</summary>
    [Fact]
    public void PostenMitMengeNull_BleibtBisZumVerfallImTick_UndUebersteht_SpeichernLaden()
    {
        var u = World();
        var (st, g) = Responsive(u);
        var p = u.Ships[u.PlayerShip];
        var b = TradeActions.Buy(u, p, st, g, 10f);
        Tick(u);
        SellRevenue(u, p, st, g, b.Units);
        var post = PlayerLedger.Post(u, st, g);
        Assert.NotNull(post);
        Assert.True(post!.BoughtOpen && post.BoughtQty <= 0.0001f);
        var u2 = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch)).Universe;
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(u2));
        var p2 = PlayerLedger.Post(u2, u2.Stations[st.Id], g);
        Assert.True(p2 is { BoughtOpen: true });
        // nach dem Rückverkauf ist der Bestand zurück; der Verfall wartet aber auf den Tick (und den Preis)
        new SimulationRunner().Run(u, 400);
        var after = PlayerLedger.Post(u, st, g);
        var e = st.Market[g];
        Assert.True(after is null || !(e.Amount >= after.BoughtStockBefore - 0.0001f && e.Price <= after.BoughtPrice + 0.0001f && after.BoughtOpen),
            "ein Posten, dessen Bedingungen erfüllt sind, ist im Tick verfallen");
    }
}
