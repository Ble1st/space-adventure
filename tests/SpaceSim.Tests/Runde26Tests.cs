using SpaceSim.Core.Content;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 26: vorsichtige Abnehmer-Auskunft (Preisgedächtnis, Ampel), Flugzeit des Spielers, Kaufmeldung mit Betrag.</summary>
public class Runde26Tests
{
    static Universe World()
    {
        var u = WorldBuilder.BuildDefault(11, "trader");
        new SimulationRunner().Run(u, 1);
        return u;
    }

    /// <summary>Eine Preisspitze über dem Basispreis wird nur zur Hälfte geglaubt, eine über dem schnellen Mittel der Station gar nicht; darunter bleibt der Preis.</summary>
    [Fact]
    public void VorsichtigerPreis_SpitzenWerdenNichtGeglaubt()
    {
        var u = World();
        var g = u.Goods.Values.First(x => x.Key == "energy");
        var st = u.StationsById().First(s => s.Market.ContainsKey(g.Id) && !s.Produces(g.Id) && !s.IsBuildSite);
        float b = g.BasePrice;
        Assert.Equal(0.5f * (2f * b + b), MarketInfo.CarefulPrice(u, st, g.Id, 2f * b), 3);
        Assert.Equal(0.8f * b, MarketInfo.CarefulPrice(u, st, g.Id, 0.8f * b), 3);
        var mem = new PriceMemory();
        mem.Observe(u);
        float now = st.Market[g.Id].Price;
        Assert.True(MarketInfo.CarefulPrice(u, st, g.Id, 3f * b, 0f, 0f, mem) <= now + 0.001f);   // das Mittel ist der heutige Preis
    }

    /// <summary>Preisgedächtnis: misst nur alle 20 Ticks, folgt dem Preis, beginnt für ein neues Universum leer.</summary>
    [Fact]
    public void Preisgedaechtnis_GleitendesMittel_NeuesUniversumLeer()
    {
        var u = World();
        var mem = new PriceMemory();
        mem.Observe(u);
        var st = u.StationsById().First(s => s.Market.Count > 0);
        var (gid, e) = st.Market.First(kv => kv.Value.Price > 0f);
        Assert.Equal(e.Price, mem.Of(st.Id, gid)!.Value.Fast, 3);
        float old = e.Price;
        e.Price = old * 3f;
        u.Tick += 10; mem.Observe(u);
        Assert.Equal(old, mem.Of(st.Id, gid)!.Value.Fast, 3);   // noch kein neuer Messpunkt
        u.Tick += 10; mem.Observe(u);
        var m = mem.Of(st.Id, gid)!.Value;
        Assert.True(m.Fast > old && m.Fast < old * 3f && m.Slow < m.Fast);
        mem.Observe(World());
        Assert.Null(mem.Of(new StationId(999999), gid));
    }

    /// <summary>Ampel: grün nur mit Spanne, Kassen- und Annahmepuffer; Verlust rot; sonst gelb.</summary>
    [Fact]
    public void Ampel_GruenNurMitSpanneUndPuffer()
    {
        var u = World();
        var st = u.StationsById().First();
        BuyerQuote Q(float price, float cash, float accept, float units = 10f) => new(st, price, units, units * price, 0f, 1, 10f, price, price, cash, accept);
        Assert.Equal(TripVerdict.Good, MarketInfo.Verdict(Q(12f, 3f, 3f), 10f, 10f));
        Assert.Equal(TripVerdict.Unsure, MarketInfo.Verdict(Q(11f, 3f, 3f), 10f, 10f));   // Spanne unter 15 %
        Assert.Equal(TripVerdict.Unsure, MarketInfo.Verdict(Q(12f, 1.5f, 3f), 10f, 10f));  // Kasse knapp
        Assert.Equal(TripVerdict.Unsure, MarketInfo.Verdict(Q(12f, 3f, 1.2f), 10f, 10f));  // Lager fast voll
        Assert.Equal(TripVerdict.Loss, MarketInfo.Verdict(Q(9f, 3f, 3f), 10f, 10f));
        Assert.Equal(TripVerdict.Loss, MarketInfo.Verdict(Q(12f, 3f, 3f, 5f), 10f, 10f));   // nimmt nur 5
    }

    /// <summary>Der Hinweis rechnet mit der vorsichtigen Schätzung und nennt beide Werte; die Rangfolge folgt ihr.</summary>
    [Fact]
    public void Abnehmer_RangfolgeNachVorsichtigerSchaetzung()
    {
        var u = World();
        var mem = new PriceMemory(); mem.Observe(u);
        var p = u.Ships[u.PlayerShip];
        foreach (var g in u.Goods.Values.Where(x => x.Category is "refined" or "component"))
        {
            var list = MarketInfo.HonestBuyers(u, g.Id, 10f, p.Sector, p.Position, null, p.Id, mem);
            for (int i = 1; i < list.Count; i++) Assert.True(list[i - 1].Revenue >= list[i].Revenue);
            foreach (var q in list)
            {
                Assert.True(q.Price <= q.PriceArrival + 0.001f && q.PriceArrival <= q.PriceNow + 0.001f);
                Assert.Equal(q.Units * q.Price, q.Revenue, 2);
            }
        }
    }

    /// <summary>Flugzeit des Spielers: endlich, länger als die NPC-Luftlinie innerhalb des Sektors nicht verschwindend, Text „~m:ss min“.</summary>
    [Fact]
    public void Flugzeit_Spieler_ErreichtJedeStationImNachbarsektor()
    {
        var u = World();
        var p = u.Ships[u.PlayerShip];
        int n = 0;
        foreach (var st in u.StationsById().Where(s => s.Dockable && u.FindRoute(p.Sector, s.Sector)?.Count <= 1).Take(12))
        {
            float s = PlayerTrip.SecondsTo(u, p.Sector, p.Position, st, p);
            Assert.False(float.IsInfinity(s), st.Name);
            Assert.True(s >= PlayerTrip.DockSeconds && s < 300f, $"{st.Name}: {s} s");
            n++;
        }
        Assert.True(n >= 5);
        Assert.Equal("~1:40 min", PlayerTrip.Text(100f));
    }

    /// <summary>Kauf und Verkauf nennen den gebuchten Betrag (Gerätetest: 53 statt angezeigter 56 Cr).</summary>
    [Fact]
    public void Handel_MeldungNenntGebuchtenBetrag()
    {
        var u = World();
        var p = u.Ships[u.PlayerShip];
        var st = u.StationsById().First(s => s.Dockable && s.Market.Values.Any(e => e.Amount >= 10f && e.Price > 0f) && !s.IsBuildSite && TradeActions.Refusal(u, p, s) is null);
        var gid = st.Market.First(kv => kv.Value.Amount >= 10f && kv.Value.Price > 0f && !ExportRules.Buys(u, st, kv.Key) && p.FreeUnits(u, kv.Key) >= 2f).Key;
        u.WalletOf(p).Credits = 100000f;
        var r = new TradeService().Execute(u, st, p, new ServiceRequest($"buy:{gid.Value}", default, 2f));
        Assert.True(r.Success, r.Message);
        Assert.StartsWith("Gekauft 2 × ", r.Message);
        Assert.EndsWith($"= {-r.Credits:0} Cr", r.Message);
    }

    /// <summary>Navigator: die Hindernisliste mit Toren wird je Tick zwischengespeichert (dieselbe Liste), ändert sich mit dem Tick.</summary>
    [Fact]
    public void Navigator_HindernisseMitToren_Zwischengespeichert()
    {
        var u = World();
        var p = u.Ships[u.PlayerShip];
        var a = Navigator.PlayerObstacles(u, p.Sector, p.Position, null);
        var b = Navigator.PlayerObstacles(u, p.Sector, p.Position, null);
        Assert.Same(a, b);
        Assert.Equal(u.GetObstacles(p.Sector).Count + u.Sectors[p.Sector].Gates.Count(g => Vec2.Distance(u.Gates[g].Position, p.Position) > 20f), a.Count);
        u.Tick++;
        Assert.NotSame(a, Navigator.PlayerObstacles(u, p.Sector, p.Position, null));
    }
}
