using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 16: Kupferkette (Zahlungsbereitschaft aus dem Abnehmergebot statt aus dem eigenen Knappheitspreis), rasterunabhängiges Balance-Urteil
/// (<see cref="MarketWatch.AverageRatio"/>) und Annahmegründe im Handel (<see cref="MarketInfo.Acceptance"/>, Hinweis im Verkaufsangebot,
/// gleicher Grund bei Ablehnung, beste Verkaufsstelle nur unter annehmenden Stationen).
/// </summary>
public class CopperIntakeTests
{
    const int SteelAlpha = 1, SmelterAlpha = 2, ElectronicsBeta = 6, TradeBeta = 30, ExportGamma = 39;

    static GoodId G(Universe u, string key) => u.FindGood(key)!.Id;
    static Station St(Universe u, int id) => u.Stations[new StationId(id)];

    static Universe Ready()
    {
        var u = DemoUniverse.Create();
        new MarketSystem().Step(u, 1);   // Preise und abgeleitete Annahmegrenzen setzen
        return u;
    }

    // ───────── Kupferkette: Zahlungsbereitschaft ─────────

    [Fact]
    public void Zahlungsbereitschaft_RechnetDasProduktZumAbnehmergebot_NichtZumEigenenKnappheitspreis()
    {
        float OrePrice(bool fromBuyers)
        {
            var u = DemoUniverse.Create();
            u.Rules.WillingnessFromBuyers = fromBuyers;
            var smelter = St(u, SmelterAlpha);
            var copper = G(u, "copper");
            var ore = G(u, "copper_ore");
            smelter.Market[copper].Amount = 0f;   // eigenes Kupferlager leer → eigener Preis 4×
            smelter.Market[ore].Amount = 0f;      // Erz leer → Knappheitspreis 4×, die Zahlungsbereitschaft deckelt
            foreach (var st in u.Stations.Values.Where(s => s.Id != smelter.Id))
                if (st.Market.TryGetValue(copper, out var e)) e.Amount = e.Target;   // Abnehmer satt: bieten den Basispreis
            new MarketSystem().Step(u, 1);   // Vortakt: Preise der Abnehmer
            new MarketSystem().Step(u, 1);
            return smelter.Market[ore].Price;
        }
        var u0 = DemoUniverse.Create();
        float copperBase = u0.Goods[G(u0, "copper")].BasePrice, oreBase = u0.Goods[G(u0, "copper_ore")].BasePrice, energyBase = u0.Goods[G(u0, "energy")].BasePrice;
        // Rezept 3 Erz + 1 Energie → 2 Kupfer; Erzanteil am Basiswert der Zutaten
        float share = 3 * oreBase / (3 * oreBase + energyBase);
        float expectedNew = 2 * copperBase * u0.Rules.InputWillingness * share / 3;     // Abnehmer bieten den Basispreis
        float expectedOld = 2 * 4 * copperBase * u0.Rules.InputWillingness * share / 3; // eigener Preis 4× → über dem 4×-Erzdeckel
        Assert.Equal(expectedNew, OrePrice(true), 2);
        Assert.Equal(MathF.Min(4 * oreBase, expectedOld), OrePrice(false), 2);
        Assert.True(OrePrice(true) < OrePrice(false));
    }

    [Fact]
    public void Abnehmergebote_OhneReineHersteller_UnabhaengigVonAbgeleitetenGrenzen()
    {
        var u = Ready();
        var steel = G(u, "steel");
        var bids = new Dictionary<GoodId, float>();
        MarketSystem.BuyerBids(u, bids);
        // Stahl: die Stahlwerke (reiner Hersteller) zählen nicht, die Verbraucher schon
        float maxConsumer = u.Stations.Values.Where(s => !s.IsBuildSite && s.Market.ContainsKey(steel) && !(s.Produces(steel) && !s.Consumes(steel))).Max(s => s.Market[steel].Price);
        Assert.Equal(maxConsumer, bids[steel], 3);
        St(u, SteelAlpha).Market[steel].Price = 1000f;
        MarketSystem.BuyerBids(u, bids);
        Assert.Equal(maxConsumer, bids[steel], 3);
        // die abgeleiteten Felder (Überhang, NoIntake) sind nach dem Laden leer — das Gebot hängt nicht von ihnen ab
        foreach (var st in u.Stations.Values) foreach (var e in st.Market.Values) { e.Overstock = 0f; e.NoIntake = false; }
        var again = new Dictionary<GoodId, float>();
        MarketSystem.BuyerBids(u, again);
        Assert.Equal(bids.OrderBy(k => k.Key.Value), again.OrderBy(k => k.Key.Value));
    }

    // ───────── Urteil aus der dichten Probe ─────────

    [Fact]
    public void MarketWatch_DurchschnittProWare_AusDenMesspunktenAbEinemTick()
    {
        var u = Ready();
        var ore = G(u, "copper_ore");
        var watch = new MarketWatch(u);
        var entries = u.Stations.Values.Where(s => !s.IsBuildSite && s.Market.ContainsKey(ore)).ToList();
        float Avg() => entries.Average(s => s.Market[ore].Price) / u.Goods[ore].BasePrice;
        u.Tick = 100; watch.Sample(u);
        float first = Avg();
        foreach (var s in entries) s.Market[ore].Price *= 2f;
        u.Tick = 120; watch.Sample(u);
        float second = Avg();
        Assert.Equal((first + second) / 2f, watch.AverageRatio(ore, 0)!.Value, 3);
        Assert.Equal(second, watch.AverageRatio(ore, 110)!.Value, 3);
        Assert.Null(watch.AverageRatio(ore, 200));
        Assert.Equal(1, watch.SamplesSince(110));
    }

    // ───────── Annahmegründe ─────────

    [Fact]
    public void Annahme_HerstellerNimmtSeinProduktNicht()
    {
        var u = Ready();
        var a = MarketInfo.Acceptance(u, St(u, SteelAlpha), G(u, "steel"));
        Assert.Equal(0f, a.Units);
        Assert.Equal(IntakeLimit.OwnProduct, a.Limit);
        Assert.Equal("Stellt die Ware selbst her", a.Reason);
        Assert.False(a.Accepts);
        Assert.Equal("Stellt die Ware selbst her", a.NoteFor(10f));
    }

    [Fact]
    public void Annahme_RegalVoll_LagerVoll_SonstBisZumWunschbestand()
    {
        var u = Ready();
        var beta = St(u, TradeBeta);
        var reactor = G(u, "reactor_mk1");
        beta.Market[reactor].Amount = beta.Market[reactor].Target;
        new MarketSystem().Step(u, 1);
        var full = MarketInfo.Acceptance(u, beta, reactor);
        Assert.Equal((0f, IntakeLimit.Full, "Lager voll"), (full.Units, full.Limit, full.Reason));
        beta.Market[reactor].Amount = beta.Market[reactor].Target - 2f;
        new MarketSystem().Step(u, 1);
        var two = MarketInfo.Acceptance(u, beta, reactor);
        Assert.Equal(2f, two.Units, 3);
        Assert.Equal(IntakeLimit.None, two.Limit);
        Assert.Null(two.NoteFor(1f));
        Assert.Equal("nimmt noch 2", two.NoteFor(5f));
    }

    [Fact]
    public void Annahme_ZutatEinesPausiertenRezepts_KeinBedarf()
    {
        var u = DemoUniverse.Create();
        var smelter = St(u, SmelterAlpha);
        var copper = G(u, "copper");
        var ore = G(u, "copper_ore");
        foreach (var st in u.Stations.Values) if (st.Market.TryGetValue(copper, out var e)) e.Amount = e.Target;   // niemand braucht Kupfer
        smelter.Market[copper].Amount = smelter.Market[copper].Target + 1f;                                          // Hütte voll
        smelter.Market[ore].Amount = smelter.Market[ore].Target;                                                     // Erz bis zum Wunschbestand
        new MarketSystem().Step(u, 1);
        var a = MarketInfo.Acceptance(u, smelter, ore);
        Assert.Equal((0f, IntakeLimit.Paused, "Kein Bedarf: Produktion pausiert"), (a.Units, a.Limit, a.Reason));
    }

    [Fact]
    public void Annahme_ExportsenkeNimmtNurEndwaren()
    {
        var u = Ready();
        var post = St(u, ExportGamma);
        var a = MarketInfo.Acceptance(u, post, G(u, "copper_ore"));
        Assert.Equal((0f, IntakeLimit.NotTraded, "Nimmt nur Endwaren"), (a.Units, a.Limit, a.Reason));
        Assert.True(MarketInfo.Acceptance(u, post, G(u, "food")).Accepts);
        var other = MarketInfo.Acceptance(u, St(u, SteelAlpha), G(u, "food"));
        Assert.Equal("Handelt diese Ware nicht", other.Reason);
    }

    [Fact]
    public void Annahme_BaustelleNurFehlmenge_VerlassenOderVollstaendigNichts()
    {
        var u = Ready();
        var type = u.StationTypes["steelworks"];
        var site = ConstructionRules.StartSite(u, type, new SectorId(2), new Vec2(-140, -140), new ActorId(1), 10000f, null, null);
        new MarketSystem().Step(u, 1);
        var (good, entry) = site.Market.OrderBy(kv => kv.Key.Value).First();
        entry.Amount = entry.Target - 3f;
        var a = MarketInfo.Acceptance(u, site, good);
        Assert.Equal(3f, a.Units, 3);
        Assert.Equal(IntakeLimit.Site, a.Limit);
        Assert.Equal("Baustelle: nur noch 3", a.Reason);
        Assert.Equal("Baustelle: nur noch 3", a.NoteFor(10f));
        entry.Amount = entry.Target;
        Assert.Equal("Baustelle: Material vollständig", MarketInfo.Acceptance(u, site, good).Reason);
        site.Site!.Abandoned = true;
        Assert.Equal("Verlassene Baustelle", MarketInfo.Acceptance(u, site, good).Reason);
    }

    [Fact]
    public void Annahme_StationKannNichtZahlen_OderNurTeilweise()
    {
        var u = Ready();
        var fab = St(u, ElectronicsBeta);
        var copper = G(u, "copper");
        fab.Market[copper].Amount = 0f;
        new MarketSystem().Step(u, 1);
        fab.Credits = 0f;
        var none = MarketInfo.Acceptance(u, fab, copper);
        Assert.Equal((0f, IntakeLimit.Credits, "Station kann nicht zahlen"), (none.Units, none.Limit, none.Reason));
        fab.Credits = fab.Market[copper].Price * 4.5f;
        var part = MarketInfo.Acceptance(u, fab, copper);
        Assert.Equal(4.5f, part.Units, 3);
        Assert.Equal("kann nur 4 bezahlen", part.Reason);
        Assert.Equal("kann nur 4 bezahlen", part.NoteFor(10f));
        Assert.Null(part.NoteFor(3f));
    }

    // ───────── Handelsdienst, beste Verkaufsstelle, Autopilot ─────────

    [Fact]
    public void Handelsdienst_VerkaufszeileTraegtDenGrund_AblehnungMitDemselbenGrund_OhneZustandswechsel()
    {
        var u = Ready();
        var works = St(u, SteelAlpha);
        var steel = G(u, "steel");
        var ship = u.Ships[u.PlayerShip];
        ship.Cargo[steel] = 5f;
        var svc = new TradeService();
        var offer = svc.Offers(u, works, ship).Single(o => o.Key == $"{TradeService.Sell}:{steel.Value}");
        Assert.Equal(0f, offer.Amount);
        Assert.Equal("Stellt die Ware selbst her", offer.Note);
        var events = new List<SimEvent>();
        u.Events.Raised += e => events.Add(e);
        float wallet = u.WalletOf(ship).Credits, stock = works.Market[steel].Amount;
        var r = svc.Execute(u, works, ship, new ServiceRequest($"{TradeService.Sell}:{steel.Value}", default, 5f));
        Assert.False(r.Success);
        Assert.Equal(offer.Note, r.Message);
        Assert.Empty(events);
        Assert.Equal(5f, ship.Cargo[steel]);
        Assert.Equal(wallet, u.WalletOf(ship).Credits);
        Assert.Equal(stock, works.Market[steel].Amount);
        // ein Verbraucher nimmt an: kein Hinweis, solange er alles nimmt
        var consumer = u.Stations.Values.First(s => !s.IsBuildSite && s.Consumes(steel) && MarketInfo.Acceptance(u, s, steel).Units >= 5f);
        Assert.Null(svc.Offers(u, consumer, ship).Single(o => o.Key == $"{TradeService.Sell}:{steel.Value}").Note);
    }

    [Fact]
    public void BesteVerkaufsstelle_NenntNurStationenDieAnnehmen()
    {
        var u = Ready();
        var steel = G(u, "steel");
        // alle Verbraucher voll bzw. pleite: dann gibt es keine Verkaufsstelle, nicht das Stahlwerk mit seinem Preis
        foreach (var st in u.Stations.Values.Where(s => s.Market.ContainsKey(steel) && !s.Produces(steel))) st.Credits = 0f;
        Assert.Null(MarketInfo.BestBuyerForGood(u, steel));
        var best = MarketInfo.BestBuyerForGood(u, steel, exclude: null);
        Assert.Null(best);
        var any = u.Stations.Values.First(s => !s.IsBuildSite && s.Consumes(steel));
        any.Credits = 100000f;
        best = MarketInfo.BestBuyerForGood(u, steel);
        Assert.NotNull(best);
        Assert.True(MarketInfo.Acceptance(u, best!, steel).Accepts);
    }

    [Fact]
    public void BesterKaeufer_IgnoriertStationenDieWenigerAlsEineEinheitNehmen()
    {
        var u = Ready();
        var ship = u.Ships[u.PlayerShip];
        var ore = G(u, "copper_ore");
        ship.Cargo[ore] = 20f;
        foreach (var st in u.Stations.Values.Where(s => s.Market.ContainsKey(ore)))
            st.Market[ore].Amount = st.AcceptableUnits(ore, u.Rules) + st.Market[ore].Amount - 0.5f;   // nimmt nur noch eine halbe Einheit
        Assert.True(u.Stations.Values.Where(s => s.Market.ContainsKey(ore)).All(s => s.AcceptableUnits(ore, u.Rules) is > 0f and < 1f));
        Assert.Null(TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position));
        // ein Rest unter einer Einheit, den die Station ganz nimmt, zählt
        ship.Cargo[ore] = 0.4f;
        Assert.NotNull(TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position));
    }
}
