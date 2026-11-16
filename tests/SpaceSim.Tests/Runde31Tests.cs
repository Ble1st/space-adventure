using SpaceSim.Core.Content;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 31: Reste zum Rückkaufdeckel — Posten bleibt, solange der Spieler die Ware hält; Schiffskauf mit Mindestpreis; wirksamer Angebotspreis.</summary>
public class Runde31Tests
{
    static readonly Lazy<string> Saved = new(() =>
    {
        var u = WorldBuilder.BuildDefault(11, "trader");
        new SimulationRunner().Run(u, 2000);
        return SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
    });
    static Universe World()
    {
        var u = SaveGameJson.Read(Saved.Value).Universe;
        var p = u.Ships[u.PlayerShip]; p.Cargo.Clear(); u.WalletOf(p).Credits = 1_000_000f;
        return u;
    }

    static (Station St, GoodId G) Consumer(Universe u) =>
        u.StationsById().Where(s => s.Dockable && !s.IsBuildSite).SelectMany(s => s.Market.Where(kv => s.Consumes(kv.Key) && !s.Produces(kv.Key) && kv.Value.Amount >= 20f
            && kv.Value.Price > 0f && !ExportRules.Buys(u, s, kv.Key)).Select(kv => (s, kv.Key))).First();

    /// <summary>
    /// Prüfer-Fall: kaufen, der Bestand füllt sich wieder und der Preis fällt unter den Kaufpreis — der Posten verfiel, obwohl die Ware noch an Bord war.
    /// Jetzt bleibt er für die gehaltene Menge; Nachkauf und Verkauf der ganzen Menge bringen keinen Gewinn über die eigenen Kaufpreise.
    /// </summary>
    [Fact]
    public void Posten_BleibtSolangeDieWareGehaltenWird()
    {
        var u = World();
        var (st, g) = Consumer(u);
        st.Credits = MathF.Max(st.Credits, 1_000_000f);   // Runde 43: geprüft wird der Rückkaufdeckel, nicht die Kasse der Station (nach 2000 Ticks je nach Weltlage knapp)
        var p = u.Ships[u.PlayerShip];
        var e = st.Market[g];
        float before = e.Amount;
        var b1 = TradeActions.Buy(u, p, st, g, 10f);
        float paid = b1.Cost / b1.Units;
        // NPC füllt auf, Preis fällt unter den Kaufpreis → Bedingungen des Verfalls erfüllt
        // Runde 44: +1 statt +50 — mit den größeren Lagerpuffern (Wunschbestand 150) lag der Bestand nach +50 über der Annahmegrenze der Station und der
        // Testverkauf wurde aus diesem Grund abgelehnt; „Bestand zurück“ (≥ vor dem Kauf) ist auch mit +1 erfüllt
        e.Amount = before + 1f; e.Price = paid * 0.8f;
        PlayerLedger.Prune(u);
        var post = PlayerLedger.Post(u, st, g);
        Assert.NotNull(post);
        Assert.Equal(b1.Units, post!.BoughtQty);
        Assert.True(post.BoughtPrice <= paid + 0.001f);
        // Preis steigt wieder (Nachkauf oder Markt): die gehaltenen Einheiten bleiben gedeckelt
        e.Price = paid * 1.5f;
        var w = u.WalletOf(p);
        float c0 = w.Credits;
        var s = TradeActions.Sell(u, p, st, g, b1.Units);
        Assert.Equal(b1.Units, s.Capped);
        Assert.True(w.Credits - c0 <= b1.Units * paid + 0.5f);
    }

    /// <summary>Ehrlicher Handel: bei A gekauft, bei B verkauft — der Spieler hält nichts mehr, der Posten bei A verfällt nach den bisherigen Bedingungen.</summary>
    [Fact]
    public void EhrlicherHandel_NachVerkaufAnderswo_PostenVerfaellt()
    {
        var u = World();
        var (st, g) = Consumer(u);
        st.Credits = MathF.Max(st.Credits, 1_000_000f);   // Runde 43: geprüft wird der Rückkaufdeckel, nicht die Kasse der Station (nach 2000 Ticks je nach Weltlage knapp)
        var p = u.Ships[u.PlayerShip];
        var e = st.Market[g];
        float before = e.Amount;
        TradeActions.Buy(u, p, st, g, 5f);
        p.Cargo.Clear();   // anderswo verkauft
        e.Amount = before; e.Price = 0f + st.Market[g].Price * 0.5f;
        u.Tick++;   // Runde 32b: der Verfall prüft erst im Tick nach der letzten Buchung
        PlayerLedger.Prune(u);
        Assert.Null(PlayerLedger.Post(u, st, g));
    }

    /// <summary>Schiffskauf: Material, das der Spieler hier verkauft hat, zählt mindestens zum erhaltenen Preis (Spiegelposten).</summary>
    [Fact]
    public void Schiffskauf_MaterialMitMindestpreisAusSpiegelposten()
    {
        var u = World();
        var p = u.Ships[u.PlayerShip];
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        var model = u.ShipModels.Values.OrderBy(m => m.Key, StringComparer.Ordinal).First(m => ShipDealerService.Quote(u, yard, p, m.Key) is not null && ShipPurchase.Materials(u, m) is not null);
        var needs = ShipPurchase.Materials(u, model)!;
        var mat = needs.First(n => yard.Market.TryGetValue(n.Good, out var x) && x.Price > 0f);
        yard.Market[mat.Good].Amount = 0f; yard.Credits = 1_000_000f;   // leeres Regal und volle Kasse: die Werft nimmt ein Stück an
        float q0 = ShipDealerService.Quote(u, yard, p, model.Key)!.Price;
        // ein Stück dieses Materials teuer an die Werft verkaufen, dann sinkt der Marktpreis (Bestand höher) — der Schiffspreis darf dadurch nicht fallen
        p.Cargo[mat.Good] = 1f;
        var e = yard.Market[mat.Good];
        float sold = e.Price;
        var sr = TradeActions.Sell(u, p, yard, mat.Good, 1f);
        Assert.True(sr.Sold.GetValueOrDefault(mat.Good) >= 1f, $"Werft nimmt {u.Goods[mat.Good].Name} nicht: {MarketInfo.Acceptance(u, yard, mat.Good).Reason}");
        e.Price = sold * 0.5f;   // Folge des höheren Bestands im nächsten Tick
        float q1 = ShipDealerService.Quote(u, yard, p, model.Key)!.Price;
        float drop = (sold - e.Price) * (1f + ShipPurchase.AssemblyOf(u, yard));
        Assert.True(q1 >= q0 - drop * (mat.Units - 1f) - 0.5f, $"Schiffspreis {q0} → {q1} (ohne Mindestpreis wäre es {q0 - drop * mat.Units})");
    }

    /// <summary>Gerätetest handy31: „Alles verkaufen“ nennt Menge, Betrag und den Rückkaufteil; Meldungen ohne langen Klammerzusatz.</summary>
    [Fact]
    public void AllesVerkaufen_MeldungNenntMengeUndBetrag()
    {
        var u = World();
        var (st, g) = Consumer(u);
        st.Credits = MathF.Max(st.Credits, 1_000_000f);   // Runde 43: geprüft wird der Rückkaufdeckel, nicht die Kasse der Station (nach 2000 Ticks je nach Weltlage knapp)
        var p = u.Ships[u.PlayerShip];
        var e = st.Market[g];
        TradeActions.Buy(u, p, st, g, 5f);
        float paid = PlayerLedger.Post(u, st, g)!.BoughtPrice;
        e.Price = paid * 2f;
        float c0 = u.WalletOf(p).Credits;
        var r = new TradeService().Execute(u, st, p, new ServiceRequest(TradeService.SellAll, default, 0f));
        Assert.True(r.Success);
        float got = u.WalletOf(p).Credits - c0;
        // Runde 44: die Meldung nennt den gebuchten Erlös; die Kassendifferenz bei 1 000 000 Cr hat nur 0,0625 Cr Auflösung (float) und rundete hier anders
        Assert.Equal(r.Credits, got, 0.1f);
        Assert.Contains($"5 Einheiten für {r.Credits:0.#} Cr", r.Message);
        Assert.Contains("davon 5 zum Rückkaufpreis", r.Message);
        Assert.StartsWith("Alles verkauft:", r.Message);
        Assert.DoesNotContain("(dein Kauf hier)", r.Message);
    }

    /// <summary>Angebotspreis = wirksamer Stückpreis (mit Rückkaufdeckel), nicht nur der Hinweis.</summary>
    [Fact]
    public void Angebotspreis_IstDerWirksamePreis()
    {
        var u = World();
        var (st, g) = Consumer(u);
        st.Credits = MathF.Max(st.Credits, 1_000_000f);   // Runde 43: geprüft wird der Rückkaufdeckel, nicht die Kasse der Station (nach 2000 Ticks je nach Weltlage knapp)
        var p = u.Ships[u.PlayerShip];
        var e = st.Market[g];
        TradeActions.Buy(u, p, st, g, 5f);
        float paid = PlayerLedger.Post(u, st, g)!.BoughtPrice;
        e.Price = paid * 2f;
        var sell = new TradeService().Offers(u, st, p).First(o => o.Key == $"sell:{g.Value}");
        Assert.True(sell.Price <= paid + 0.001f, $"Angebot {sell.Price}, gezahlt {paid}");
    }
}
