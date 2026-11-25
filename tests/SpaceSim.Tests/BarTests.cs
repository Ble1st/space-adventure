using SpaceSim.Core.Bars;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Bars in Stationen (docs/BARS.md): Dienst „bar“ an bewohnten Stationen, Rubbellos (Ausgang beim Kauf fest, eigener Zufall, Hausvorteil, Kassendeckel,
/// Tageslimit), Gerüchte (kaufbare unbekannte Station), Söldner-Abkürzung, strenge Schlüssel, Angebotsmenge = Ausführung, Speichern/Laden, NPC-Welt unberührt.
/// </summary>
public class BarTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    static (Universe u, Ship me, Station bar, BarService svc) Setup(Universe? world = null)
    {
        var u = world ?? DemoUniverse.Create();
        var me = u.Ships[u.PlayerShip];
        u.WalletOf(me).Credits = 100_000f;
        var bar = u.StationsById().First(s => s.TypeKey == "shipyard" && Bars.IsAt(u, s));
        bar.Credits = 50_000f;
        me.DockedAt = bar.Id;
        return (u, me, bar, new BarService());
    }

    static ServiceResult Exec(BarService svc, Universe u, Station st, Ship me, string key) => svc.Execute(u, st, me, new ServiceRequest(key, default, 0f));

    [Fact]
    public void Inhalt_SchalterUndStationen()
    {
        Assert.False(new EconomyRules().Bars);
        var u = DemoUniverse.Create();
        Assert.True(u.Rules.Bars);
        Assert.Contains(Bars.ServiceKey, StationServices.Default().Keys);
        var me = u.Ships[u.PlayerShip];
        var withBar = u.StationsById().Where(s => Bars.IsAt(u, s)).ToList();
        Assert.True(withBar.Count >= 10, $"Bars: {withBar.Count}");
        foreach (var t in new[] { "colony", "trade_station", "trade_post", "shipyard", "sector_hq", "headquarters" })
            Assert.Contains(withBar, s => s.TypeKey == t);
        Assert.All(withBar, s => Assert.True(s.Dockable && !s.IsBuildSite));
        Assert.DoesNotContain(u.StationsById(), s => s.TypeKey == "farm" && Bars.IsAt(u, s));
        var yard = withBar.First(s => s.TypeKey == "shipyard");
        Assert.Contains(StationServices.Default().ForStation(u, yard, me), s => s.Key == Bars.ServiceKey);
        u.Rules.Bars = false;
        Assert.DoesNotContain(StationServices.Default().ForStation(u, yard, me), s => s.Key == Bars.ServiceKey);
        Assert.False(TestWorlds.Classic().Rules.Bars);
    }

    [Fact]
    public void Rubbellos_Erwartungswert_UnterPreis_VerteilungStimmt()
    {
        var r = new EconomyRules();
        double ev = Scratchcard.ExpectedValue(r);
        Assert.Equal(37.5, ev, 3);
        Assert.True(ev < r.BarTicketPrice, "Hausvorteil");
        Assert.Equal(0.25, 1 - ev / r.BarTicketPrice, 3);
        var counts = new int[r.BarPrizes.Length + 1];
        double paid = 0;
        const int N = 60000;
        for (int i = 0; i < N; i++)
        {
            var (tier, prize, symbols) = Scratchcard.Draw(new DeterministicRng(0xABCDEFUL + (ulong)i * 7919UL), r);
            Assert.Equal(tier, Scratchcard.TierOf(symbols));   // Symbole stimmen mit dem Ausgang überein
            Assert.Equal(9, symbols.Length);
            if (tier >= 0) Assert.Equal(3, symbols.Count(s => s == tier));
            counts[tier + 1]++;
            paid += prize;
        }
        Assert.InRange(paid / N, 33.0, 42.0);
        Assert.InRange(counts[0] / (double)N, 0.53, 0.59);   // Nieten ≈ 56 %
        Assert.InRange(counts[1] / (double)N, 0.19, 0.21);   // 25 Cr ≈ 20 %
    }

    [Fact]
    public void Kauf_Aufdecken_Abholen_Buchungen()
    {
        var (u, me, bar, svc) = Setup();
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        float wallet = u.WalletOf(me).Credits, cash = bar.Credits;
        var buy = Exec(svc, u, bar, me, BarService.Ticket);
        Assert.True(buy.Success, buy.Message);
        Assert.Equal(wallet - 50f, u.WalletOf(me).Credits, 3);
        Assert.Equal(cash + 50f, bar.Credits, 3);
        Assert.Single(events.OfType<ServicePerformed>());
        var t = Bars.OpenTicket(u)!;
        Assert.Equal(0, t.Revealed);
        Assert.False(Exec(svc, u, bar, me, BarService.Claim).Success);   // erst aufdecken
        Assert.False(Exec(svc, u, bar, me, BarService.Ticket).Success);  // kein zweites Los
        for (int i = 0; i < 9; i++) Assert.True(Exec(svc, u, bar, me, $"reveal:{i}").Success);
        Assert.False(Exec(svc, u, bar, me, "reveal:3").Success);   // schon offen
        float before = u.WalletOf(me).Credits;
        var claim = Exec(svc, u, bar, me, BarService.Claim);
        Assert.True(claim.Success, claim.Message);
        Assert.Equal(before + t.Prize, u.WalletOf(me).Credits, 3);
        Assert.Null(Bars.OpenTicket(u));
    }

    [Fact]
    public void Ausgang_BeimKaufFest_Reihenfolge_UndSpeichernLadenAendernNichts()
    {
        var (u, me, bar, svc) = Setup();
        Assert.True(Exec(svc, u, bar, me, BarService.Ticket).Success);
        Exec(svc, u, bar, me, "reveal:4");
        string json = SaveGameJson.Write(u, Now);
        ulong h = UniverseHash.Compute(u, true);
        var a = SaveGameJson.Read(json).Universe;
        Assert.Equal(h, UniverseHash.Compute(a, true));
        var b = SaveGameJson.Read(json).Universe;
        var ta = Bars.OpenTicket(a)!;
        Assert.True(ta.IsRevealed(4));
        // a: aufsteigend, b: absteigend, dann alles
        for (int i = 0; i < 9; i++) Exec(svc, a, a.Stations[bar.Id], a.Ships[me.Id], $"reveal:{i}");
        Exec(svc, b, b.Stations[bar.Id], b.Ships[me.Id], "reveal:8");
        Exec(svc, b, b.Stations[bar.Id], b.Ships[me.Id], "reveal:all");
        var tb = Bars.OpenTicket(b)!;
        Assert.Equal(ta.Symbols, tb.Symbols);
        Assert.Equal(ta.Prize, tb.Prize);
        var pa = Exec(svc, a, a.Stations[bar.Id], a.Ships[me.Id], BarService.Claim);
        var pb = Exec(svc, b, b.Stations[bar.Id], b.Ships[me.Id], BarService.Claim);
        Assert.Equal(pa.Credits, pb.Credits);
        Assert.Equal(UniverseHash.Compute(a, true), UniverseHash.Compute(b, true));
        // erneut geladen: dasselbe Los (kein Neuwürfeln durch Laden)
        var c = SaveGameJson.Read(SaveGameJson.Write(u, Now)).Universe;
        Assert.Equal(Bars.OpenTicket(u)!.Symbols, Bars.OpenTicket(c)!.Symbols);
    }

    [Fact]
    public void ZweiLoseImSelbenTick_Verschieden_TageslimitUndKasse()
    {
        var (u, me, bar, svc) = Setup();
        var seen = new HashSet<string>();
        for (int k = 0; k < u.Rules.BarTicketsPerDay; k++)
        {
            Assert.True(Exec(svc, u, bar, me, BarService.Ticket).Success);
            seen.Add(string.Join(",", Bars.OpenTicket(u)!.Symbols));
            Exec(svc, u, bar, me, "reveal:all");
            Assert.True(Exec(svc, u, bar, me, BarService.Claim).Success);
        }
        Assert.True(seen.Count >= 4, "Loszähler im Seed");
        var off = svc.Offers(u, bar, me).Single(o => o.Key == BarService.Ticket);
        Assert.Equal(0f, off.Amount);
        Assert.Contains("Heute keine Lose", off.Note);
        Assert.False(Exec(svc, u, bar, me, BarService.Ticket).Success);
        // nächster Tag: wieder möglich
        u.Tick += u.Rules.DayTicks;
        Assert.Equal(1f, svc.Offers(u, bar, me).Single(o => o.Key == BarService.Ticket).Amount);
        // knappe Stationskasse: kein Verkauf
        bar.Credits = 500f;
        Assert.Equal(0f, svc.Offers(u, bar, me).Single(o => o.Key == BarService.Ticket).Amount);
        bar.Credits = 50_000f;
        // ohne Geld: kein Verkauf
        u.WalletOf(me).Credits = 10f;
        Assert.False(Exec(svc, u, bar, me, BarService.Ticket).Success);
    }

    [Fact]
    public void Gewinn_HoechstensStationskasse()
    {
        var (u, me, bar, svc) = Setup();
        u.Bar = new BarState { Ticket = new BarTicket { Station = bar.Id, Tier = 4, Prize = 1000, Symbols = new[] { 4, 4, 4, 0, 1, 2, 3, 0, 1 }, Revealed = 511, Price = 50f } };
        bar.Credits = 300f;
        float w = u.WalletOf(me).Credits;
        var r = Exec(svc, u, bar, me, BarService.Claim);
        Assert.True(r.Success);
        Assert.Equal(w + 300f, u.WalletOf(me).Credits, 3);
        Assert.Equal(0f, bar.Credits, 3);
        Assert.Contains("nur 300", r.Message);
    }

    [Fact]
    public void Offenes_Los_AnAndererStation_SperrtKauf_VerschwundeneStation_Nicht()
    {
        var (u, me, bar, svc) = Setup();
        Assert.True(Exec(svc, u, bar, me, BarService.Ticket).Success);
        var other = u.StationsById().First(s => s.Id != bar.Id && Bars.IsAt(u, s));
        other.Credits = 50_000f;
        var o = svc.Offers(u, other, me).Single(x => x.Key == BarService.Ticket);
        Assert.Equal(0f, o.Amount);
        Assert.Contains("Offenes Los", o.Note);
        Assert.DoesNotContain(svc.Offers(u, other, me), x => x.Key.StartsWith("reveal:"));
        u.Stations.Remove(bar.Id);
        Assert.Null(Bars.OpenTicket(u));
        Assert.True(Exec(svc, u, other, me, BarService.Ticket).Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tickets")]
    [InlineData("reveal:9")]
    [InlineData("reveal:-1")]
    [InlineData("reveal:01")]
    [InlineData("reveal:")]
    [InlineData("claim:1")]
    [InlineData("rumour:x")]
    [InlineData("rumour:0")]
    [InlineData("merc:")]
    [InlineData("merc:+1")]
    public void StrengeSchluessel_OhneZustandUndEreignis(string key)
    {
        var (u, me, bar, svc) = Setup();
        Assert.True(Exec(svc, u, bar, me, BarService.Ticket).Success);
        int events = 0;
        u.Events.Raised += _ => events++;
        ulong h = UniverseHash.Compute(u, true);
        var r = Exec(svc, u, bar, me, key);
        Assert.False(r.Success);
        Assert.Equal(0, events);
        Assert.Equal(h, UniverseHash.Compute(u, true));
    }

    [Fact]
    public void AngebotsMenge_GleichAusfuehrung()
    {
        var (u, me, bar, svc) = Setup();
        for (int round = 0; round < 6; round++)
        {
            if (round == 2) u.WalletOf(me).Credits = 20f;
            if (round == 3) u.WalletOf(me).Credits = 100_000f;
            if (round == 4) bar.Credits = 100f;
            if (round == 5) bar.Credits = 50_000f;
            foreach (var o in svc.Offers(u, bar, me).ToList())
            {
                var copy = SaveGameJson.Read(SaveGameJson.Write(u, Now)).Universe;
                var r = Exec(svc, copy, copy.Stations[bar.Id], copy.Ships[me.Id], o.Key);
                Assert.Equal(o.Amount > 0f, r.Success);
            }
            Exec(svc, u, bar, me, BarService.Ticket);
            Exec(svc, u, bar, me, round % 2 == 0 ? "reveal:all" : "reveal:2");
        }
    }

    [Fact]
    public void Geruecht_UnbekannteStation_KaufMachtSieBekannt()
    {
        var w = WorldBuilder.BuildDefault(7, "trader");
        Assert.True(Knowledge.Limited(w));
        var me = w.Ships[w.PlayerShip];
        var bar = w.StationsById().Where(s => Bars.IsAt(w, s) && Knowledge.KnowsStation(w, s)).OrderBy(s => s.Id.Value).First();
        me.DockedAt = bar.Id;
        w.WalletOf(me).Credits = 1000f;
        var svc = new BarService();
        var rumours = Bars.Rumours(w, bar);
        var paid = rumours.Single(r => r.Paid);
        Assert.False(Knowledge.KnowsStation(w, paid.Reveal));
        var offer = svc.Offers(w, bar, me).Single(o => o.Key == $"rumour:{paid.Reveal.Value}");
        Assert.Equal(1f, offer.Amount);
        float cash = bar.Credits;
        var r = Exec(svc, w, bar, me, offer.Key);
        Assert.True(r.Success, r.Message);
        Assert.True(Knowledge.KnowsStation(w, paid.Reveal));
        Assert.Equal(1000f - w.Rules.BarRumourFee, w.WalletOf(me).Credits, 3);
        Assert.Equal(cash + w.Rules.BarRumourFee, bar.Credits, 3);
        Assert.False(Exec(svc, w, bar, me, offer.Key).Success);   // gibt es nicht mehr
        // die anderen Gerüchte nennen nur bekannte Stationen
        Assert.All(Bars.Rumours(w, bar), x => Assert.DoesNotContain("?", x.Text));
        // ohne beschränktes Wissen: kein kaufbares Gerücht
        var full = DemoUniverse.Create();
        Assert.DoesNotContain(Bars.Rumours(full, full.StationsById().First(s => Bars.IsAt(full, s))), x => x.Paid);
    }

    [Fact]
    public void Geruechte_KnappeWareUndPiraten()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 3000);
        var bar = u.StationsById().First(s => Bars.IsAt(u, s));
        var rumours = Bars.Rumours(u, bar);
        Assert.NotEmpty(rumours);
        Assert.All(rumours, r => Assert.False(string.IsNullOrWhiteSpace(r.Text)));
        // eine Ware künstlich knapp machen: das Gerücht nennt sie
        var target = u.StationsById().First(s => s.Id != bar.Id && s.Dockable && !s.IsBuildSite && s.Market.Keys.Any(g => s.Consumes(g)));
        var good = target.Market.Keys.First(g => target.Consumes(g));
        target.Market[good].Price = u.Goods[good].BasePrice * 10f;
        target.Market[good].Amount = 0f;
        Assert.Contains(Bars.Rumours(u, bar), r => r.Kind == "scarce" && r.Text.Contains(u.Goods[good].Name));
    }

    [Fact]
    public void Soeldner_Abkuerzung_AnGildenstation()
    {
        var u = DemoUniverse.Create();
        var me = u.Ships[u.PlayerShip];
        u.WalletOf(me).Credits = 10_000f;
        var bar = u.StationsById().FirstOrDefault(s => Bars.IsAt(u, s) && Bars.MercProvider(u, s) is not null);
        Assert.NotNull(bar);
        var svc = new BarService();
        var offer = svc.Offers(u, bar!, me).First(o => o.Key.StartsWith("merc:"));
        Assert.Equal(Mercenaries.HireIssue(u, bar!.Owner, int.Parse(offer.Key[5..])) is null, offer.Amount > 0f);
        if (offer.Amount > 0f)
        {
            var r = Exec(svc, u, bar, me, offer.Key);
            Assert.True(r.Success, r.Message);
            Assert.Single(u.Mercenaries);
            Assert.Equal(bar.Owner, u.Mercenaries[0].Actor);
        }
        // eine Staatsstation (Union) bietet keine Söldner
        var state = u.StationsById().First(s => Bars.IsAt(u, s) && u.Actors.TryGetValue(s.Owner, out var a) && a.Kind == ActorKind.State);
        Assert.DoesNotContain(svc.Offers(u, state, me), o => o.Key.StartsWith("merc:"));
    }

    [Fact]
    public void HashDesBarzustands_NurMitSpielerpolitik()
    {
        // Los kaufen: Hash ohne Spielerpolitik ändert sich nur durch Stationskasse/Spielerkasse, nicht durch den Barzustand
        var (u, me, bar, svc) = Setup();
        Exec(svc, u, bar, me, BarService.Ticket);
        ulong full = UniverseHash.Compute(u, true, includePlayerPolitics: true);
        ulong np = UniverseHash.Compute(u, true, includePlayerPolitics: false);
        Bars.OpenTicket(u)!.Revealed = 3;
        Assert.NotEqual(full, UniverseHash.Compute(u, true, includePlayerPolitics: true));
        Assert.Equal(np, UniverseHash.Compute(u, true, includePlayerPolitics: false));
    }

    [Fact]
    public void NpcWelt_BitgleichMitUndOhneBars_2000Ticks()
    {
        var a = DemoUniverse.Create();
        var b = DemoUniverse.Create();
        b.Rules.Bars = false;
        new SimulationRunner().Run(a, 2000);
        new SimulationRunner().Run(b, 2000);
        b.Rules.Bars = true;
        Assert.Equal(UniverseHash.Compute(a, true), UniverseHash.Compute(b, true));
        Assert.Null(a.Bar);
    }
}
