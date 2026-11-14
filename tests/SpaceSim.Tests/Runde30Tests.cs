using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 30: Rückkaufdeckel (PlayerLedger) — beide Richtungen der Schleife an derselben Station geschlossen, ehrlicher Handel und NPCs unberührt.</summary>
public class Runde30Tests
{
    static readonly Lazy<string> Saved = new(() => SaveGameJson.Write(World(), DateTimeOffset.UnixEpoch));
    static readonly Lazy<string> SavedOff = new(() => SaveGameJson.Write(World(cap: false), DateTimeOffset.UnixEpoch));
    static Universe CopyOff() => SaveGameJson.Read(SavedOff.Value).Universe;
    /// <summary>Kopie der Welt nach dem Vorlauf (über Speichern/Laden — schneller als 2000 Ticks je Kombination).</summary>
    static Universe Copy() => SaveGameJson.Read(Saved.Value).Universe;

    static Universe World(bool cap = true)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Rules!.BuybackCap = cap;
        var u = WorldBuilder.Build(def, 11, "trader");
        new SimulationRunner().Run(u, 2000);
        var p = u.Ships[u.PlayerShip];
        p.Cargo.Clear();
        u.WalletOf(p).Credits = 1_000_000f;
        return u;
    }

    /// <summary>Ein Regal, dessen Preis nach einem Leerkauf steigt (Mk1-Laser in einer Werft).</summary>
    static (Station St, GoodId G) Shelf(Universe u)
    {
        foreach (var st in u.StationsById().Where(s => s.TypeKey == "shipyard"))
            foreach (var (g, e) in st.Market.OrderBy(kv => kv.Key.Value))
                if (st.IsStockOnly(g) && e.Amount >= 2f && e.Amount <= 8f && e.Price > 0f && e.Target >= 2f) return (st, g);
        throw new InvalidOperationException("kein Regal");
    }

    /// <summary>Eine Verbraucherware, die die Station annimmt (Spiegel: teuer verkaufen, billiger zurückkaufen).</summary>
    static (Station St, GoodId G) Consumer(Universe u)
    {
        foreach (var st in u.StationsById().Where(s => s.Dockable && !s.IsBuildSite))
            foreach (var (g, e) in st.Market.OrderBy(kv => kv.Key.Value))
                if (st.Consumes(g) && !st.Produces(g) && e.Price > 0f && !ExportRules.Buys(u, st, g) && TradeActions.SaleCap(u, st, g) >= 20f && e.Amount < e.Target) return (st, g);
        throw new InvalidOperationException("kein Verbraucher");
    }

    static float BuyThenSell(Universe u, Ship a, Ship b, Station st, GoodId g)
    {
        var w = u.WalletOf(a);
        float before = w.Credits;
        var r = TradeActions.Buy(u, a, st, g, 9999f);
        Assert.True(r.Units > 0f);
        new SimulationRunner().Run(u, 1);
        if (!ReferenceEquals(a, b)) { b.Cargo[g] = a.Cargo[g]; a.Cargo.Remove(g); }
        TradeActions.Sell(u, b, st, g, b.Cargo[g]);
        float left = b.Cargo.GetValueOrDefault(g);   // was die Station nicht mehr nimmt, zum Einkaufspreis gegenrechnen
        return w.Credits - before + left * (r.Cost / r.Units);
    }

    static float SellThenBuy(Universe u, Ship ship, Station st, GoodId g, float q)
    {
        ship.Cargo[g] = q;
        var w = u.WalletOf(ship);
        float before = w.Credits;
        var s = TradeActions.Sell(u, ship, st, g, q);
        float n = s.Sold.GetValueOrDefault(g);
        Assert.True(n > 0f);
        new SimulationRunner().Run(u, 1);
        var b = TradeActions.Buy(u, ship, st, g, n);
        float missing = n - b.Units;   // nicht zurückgekaufte Menge zum heutigen Preis ersetzen
        return w.Credits - before - missing * st.Market[g].Price;
    }

    [Fact]
    public void Regal_KaufTickRueckverkauf_OhneGewinn_OhneDeckelMitGewinn()
    {
        var off = World(cap: false);
        var (so, go) = Shelf(off);
        Assert.True(BuyThenSell(off, off.Ships[off.PlayerShip], off.Ships[off.PlayerShip], so, go) > 1f, "ohne Deckel müsste die Schleife Gewinn bringen");
        var u = World();
        var (st, g) = Shelf(u);
        Assert.True(BuyThenSell(u, u.Ships[u.PlayerShip], u.Ships[u.PlayerShip], st, g) <= 0.5f);
    }

    [Fact]
    public void Verbraucher_VerkaufTickRueckkauf_OhneGewinn_OhneDeckelMitGewinn()
    {
        // ohne Deckel: die erste Verbraucherware, an der die Spiegelschleife Gewinn bringt; mit Deckel dieselbe ≤ 0
        var probe = CopyOff();
        var cands = probe.StationsById().Where(s => s.Dockable && !s.IsBuildSite)
            .SelectMany(s => s.Market.Where(kv => s.Consumes(kv.Key) && !s.Produces(kv.Key) && kv.Value.Price > 0f && !ExportRules.Buys(probe, s, kv.Key)).Select(kv => (s.Id, kv.Key)))
            .OrderBy(x => x.Id.Value).ThenBy(x => x.Key.Value).ToList();
        (StationId St, GoodId G)? hit = null;
        foreach (var (sid, g) in cands.Take(40))
        {
            var off = CopyOff();
            var st = off.Stations[sid];
            float q = MathF.Floor(MathF.Min(TradeActions.SaleCap(off, st, g), 20f));
            if (q < 5f) continue;
            if (SellThenBuy(off, off.Ships[off.PlayerShip], st, g, q) > 1f) { hit = (sid, g); break; }
        }
        Assert.True(hit is not null, "ohne Deckel bringt keine Verbraucherware die Spiegelschleife");
        var u = Copy();
        var s2 = u.Stations[hit!.Value.St];
        float q2 = MathF.Floor(MathF.Min(TradeActions.SaleCap(u, s2, hit.Value.G), 20f));
        float gain = SellThenBuy(u, u.Ships[u.PlayerShip], s2, hit.Value.G, q2);
        Assert.True(gain <= 0.5f, $"{s2.Name}/{u.Goods[hit.Value.G].Name} q {q2}: Gewinn {gain}");
    }

    [Fact]
    public void ZweitesSchiffDesSpielers_UmgehtDenDeckelNicht()
    {
        var u = World();
        var (st, g) = Shelf(u);
        var m = u.ShipModels["nomad"];
        var b = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, st, 0f);
        u.AddShip(b);
        Assert.True(BuyThenSell(u, u.Ships[u.PlayerShip], b, st, g) <= 0.5f);
    }

    /// <summary>Gemischte Menge: gekaufte Einheiten zum Kaufpreis, mitgebrachte zum Markt; die Meldung nennt den Rückkaufteil.</summary>
    [Fact]
    public void GemischteMenge_GedeckeltUndMarkt_MeldungNenntDenTeil()
    {
        var u = World();
        var (st, g) = Shelf(u);
        var p = u.Ships[u.PlayerShip];
        var e = st.Market[g];
        var r = TradeActions.Buy(u, p, st, g, 2f);
        Assert.Equal(2f, r.Units);
        float paid = r.Cost / 2f;
        new SimulationRunner().Run(u, 1);
        Assert.True(e.Price > paid);
        p.Cargo[g] = p.Cargo[g] + 1f;   // eine Einheit von anderswo
        float market = e.Price;
        var res = new TradeService().Execute(u, st, p, new ServiceRequest($"sell:{g.Value}", default, 3f));
        Assert.True(res.Success, res.Message);
        Assert.Equal(2f * paid + market, res.Credits, 1);
        Assert.Contains("davon 2 zum Rückkaufpreis", res.Message);
    }

    /// <summary>Verfall: ist der Bestand wieder auf dem Stand vor dem Kauf, verfällt der Posten — der Preis ist dann nicht höher als gezahlt (kein Gewinn).</summary>
    [Fact]
    public void Verfall_BestandZurueck_PostenWeg_PreisNichtHoeher()
    {
        var u = World();
        var (st, g) = Shelf(u);
        var p = u.Ships[u.PlayerShip];
        var e = st.Market[g];
        float before = e.Amount;
        var r = TradeActions.Buy(u, p, st, g, 2f);
        float paid = r.Cost / r.Units;
        Assert.NotNull(PlayerLedger.Post(u, st, g));
        p.Cargo.Clear();     // Runde 31: der Spieler hält die Ware nicht mehr (anderswo verkauft) — sonst bleibt der Posten für die gehaltene Menge
        e.Amount = before;   // NPC füllt auf
        new SimulationRunner().Run(u, 1);
        Assert.Null(PlayerLedger.Post(u, st, g));
        Assert.True(e.Price <= paid + 0.001f, $"Preis nach Rückkehr {e.Price} > gezahlt {paid}");
        // sinkt der Bestand später wieder, lebt der Posten nicht auf
        e.Amount = before - 2f;
        new SimulationRunner().Run(u, 1);
        Assert.Null(PlayerLedger.Post(u, st, g));
    }

    [Fact]
    public void Ausruester_AusbauTickEinbau_OhneGewinn()
    {
        var u = World();
        var p = u.Ships[u.PlayerShip];
        var svc = new OutfitterService();
        Station? yard = null; int slot = -1; string key = "";
        foreach (var cand in u.StationsById().Where(s => svc.IsAvailable(u, s)))
        {
            p.Position = cand.DockPoint(p.Id); p.DockedAt = cand.Id;
            for (int i = 0; i < p.Slots.Count && slot < 0; i++)
                if (p.Fit[i] is { } k && svc.Offers(u, cand, p).Any(o => o.Key == $"remove:{i}" && o.Amount >= 1f && o.Price > 0f)) { slot = i; key = k; yard = cand; }
            if (slot >= 0) break;
        }
        Assert.True(slot >= 0 && yard is not null, "kein ausbaubares Modul, das eine Station ankauft");
        var w = u.WalletOf(p);
        float before = w.Credits;
        Assert.True(svc.Execute(u, yard, p, new ServiceRequest($"remove:{slot}", default, 0f)).Success);
        new SimulationRunner().Run(u, 1);
        var r = svc.Execute(u, yard, p, new ServiceRequest($"fit:{slot}:{key}", default, 0f));
        Assert.True(r.Success, r.Message);
        Assert.True(w.Credits - before <= 0.5f, $"Gewinn {w.Credits - before}");
    }

    /// <summary>NPC-Welt: ohne Spielerhandel kein Posten, der Hash ist mit und ohne Deckel gleich.</summary>
    [Fact]
    public void NpcWelt_KeinPosten_HashUnveraendert()
    {
        var a = WorldBuilder.BuildDefault(5);
        var defB = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson()); defB.Rules!.BuybackCap = false;
        var b = WorldBuilder.Build(defB, 5);
        new SimulationRunner().Run(a, 3000); new SimulationRunner().Run(b, 3000);
        Assert.Empty(a.Ledger);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }

    [Fact]
    public void Speichern_Laden_MitOffenenPosten_Bitgleich()
    {
        var u = World();
        var (st, g) = Shelf(u);
        var (cs, cg) = Consumer(u);
        var p = u.Ships[u.PlayerShip];
        TradeActions.Buy(u, p, st, g, 2f);
        p.Cargo[cg] = 5f; TradeActions.Sell(u, p, cs, cg, 5f);
        Assert.Equal(2, u.Ledger.Count);
        var v = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch)).Universe;
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        new SimulationRunner().Run(u, 300); new SimulationRunner().Run(v, 300);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    /// <summary>Angebot = Ausführung mit offenen Posten: Menge des sell:/buy:-Angebots &gt; 0 ⇔ Execute gelingt, bewegt genau diese Menge zu den Kosten nach dem Deckel.</summary>
    [Fact]
    public void AngebotGleichAusfuehrung_MitPosten_VieleKombinationen()
    {
        int checkedCombos = 0;
        foreach (float credits in new[] { 50f, 400f, 5000f, 1_000_000f })
            foreach (float extra in new[] { 0f, 1f, 3f })
                foreach (int stationPick in new[] { 0, 1, 2 })
                foreach (float want in new[] { 0f, 1f, 2f })
                {
                    var u = Copy();
                    var p = u.Ships[u.PlayerShip];
                    var shelves = u.StationsById().Where(s => s.TypeKey == "shipyard").ToList();
                    var st = shelves[stationPick % shelves.Count];
                    var g = st.Market.Where(kv => st.IsStockOnly(kv.Key) && kv.Value.Amount >= 2f && kv.Value.Price > 0f).Select(kv => kv.Key).FirstOrDefault();
                    if (g.Value == 0) continue;
                    TradeActions.Buy(u, p, st, g, 2f);
                    new SimulationRunner().Run(u, 1);
                    p.Cargo[g] = p.Cargo.GetValueOrDefault(g) + extra;
                    u.WalletOf(p).Credits = credits;
                    var svc = new TradeService();
                    var offers = svc.Offers(u, st, p);
                    var sell = offers.First(o => o.Key == $"sell:{g.Value}");
                    var cashBefore = u.WalletOf(p).Credits;
                    float n = want <= 0f ? sell.Amount : MathF.Min(want, sell.Amount);
                    var expected = PlayerLedger.SaleValue(u, p, st, g, n, st.Market[g].Price).Revenue;
                    var r = svc.Execute(u, st, p, new ServiceRequest(sell.Key, default, want));
                    Assert.Equal(sell.Amount > 0f, r.Success);
                    if (r.Success) { Assert.Equal(n, r.Units, 3); Assert.Equal(expected, u.WalletOf(p).Credits - cashBefore, 1); }
                    // Spiegel: zurückkaufen — Angebotsmenge = Kaufmenge, Kosten nach dem Mindestpreis
                    var buy = svc.Offers(u, st, p).First(o => o.Key == $"buy:{g.Value}");
                    cashBefore = u.WalletOf(p).Credits;
                    float expCost = buy.Amount > 0f ? PlayerLedger.BuyCost(u, p, st, g, buy.Amount, st.Market[g].Price).Cost : 0f;
                    var rb = svc.Execute(u, st, p, new ServiceRequest(buy.Key, default, buy.Amount > 0f ? buy.Amount : 1f));
                    Assert.Equal(buy.Amount >= 1f, rb.Success);
                    if (rb.Success) { Assert.Equal(buy.Amount, rb.Units, 3); Assert.Equal(expCost, cashBefore - u.WalletOf(p).Credits, 1); Assert.True(cashBefore - u.WalletOf(p).Credits <= cashBefore + 0.01f); }
                    checkedCombos++;
                }
        Assert.True(checkedCombos >= 100, $"nur {checkedCombos} Kombinationen");
    }

    /// <summary>
    /// Ehrlicher Handel unverändert: eine Handelsflotte des Spielers (3 Nomaden mit Handelsbefehl) und eine Handrunde (Kauf beim Hersteller A, Verkauf bei B)
    /// bringen mit und ohne Deckel genau dasselbe — die Flotte kauft nie dort, wo sie verkauft.
    /// </summary>
    [Fact]
    public void EhrlicherHandel_FlotteUndRundreise_MitUndOhneDeckelGleich()
    {
        (float Wallet, ulong Hash) Run(bool cap)
        {
            var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
            def.Rules!.BuybackCap = cap;
            var u = WorldBuilder.Build(def, 11, "trader");
            var beta = u.StationsById().First(s => s.Name == "Handelsstation Beta");
            var m = u.ShipModels["nomad"];
            for (int i = 0; i < 3; i++)
            {
                var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, beta, 0f);
                s.HomeBase = beta.Id;
                u.AddShip(s);
                Assert.True(Commands.Give(u, s, new TradeCommand(new AutoLimits(beta.Sector, 2, 1.5f, 0f)), out var msg), msg);
            }
            u.WalletOf(u.Ships[u.PlayerShip]).Credits = 20000f;
            new SimulationRunner().Run(u, 6000);
            // Handrunde: 10 Einheiten beim billigsten Hersteller, Verkauf beim besten anderen Abnehmer
            var p = u.Ships[u.PlayerShip]; p.Cargo.Clear();
            var g = u.Goods.Values.First(x => x.Key == "energy").Id;
            var seller = u.StationsById().Where(s => s.Produces(g) && s.Market[g].Amount >= 10f).OrderBy(s => s.Market[g].Price).First();
            TradeActions.Buy(u, p, seller, g, 10f);
            var buyer = u.StationsById().Where(s => s.Id != seller.Id && s.Market.ContainsKey(g) && TradeActions.SaleCap(u, s, g) >= 10f).OrderByDescending(s => s.Market[g].Price).First();
            TradeActions.Sell(u, p, buyer, g, 10f);
            // ohne die Posten (die nur mit Deckel existieren) muss der Rest der Welt gleich sein
            u.Ledger.Clear();
            return (u.WalletOf(p).Credits, UniverseHash.Compute(u));
        }
        var on = Run(true);
        var off = Run(false);
        Assert.Equal(off.Wallet, on.Wallet);
        Assert.Equal(off.Hash, on.Hash);
    }
}
