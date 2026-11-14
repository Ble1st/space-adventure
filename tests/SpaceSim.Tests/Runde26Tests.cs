using SpaceSim.Core.Content;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 26/27: Abnehmer-Auskunft (Preisgedächtnis mit Bereich, Ampel nach dem schlechten Fall), Flugzeit des Spielers, Kaufmeldung mit Betrag, Hindernis-Cache.</summary>
public class Runde26Tests
{
    static Universe World()
    {
        var u = WorldBuilder.BuildDefault(11, "trader");
        new SimulationRunner().Run(u, 1);
        return u;
    }

    /// <summary>Runde 27/28: Preisgedächtnis — Bereich der letzten 1200 Ticks, jeder Tick zählt (auch ein kurzes Tal zwischen zwei 20-Tick-Stützstellen), Verlauf
    /// erst mit einem vollen Fenster; neues Universum leer.</summary>
    [Fact]
    public void Preisgedaechtnis_BereichDerLetztenMinuten_JederTick()
    {
        var u = World();
        var mem = new PriceMemory();
        mem.Observe(u);
        var st = u.StationsById().First(s => s.Market.Count > 0);
        var (gid, e) = st.Market.First(kv => kv.Value.Price > 0f);
        float p0 = e.Price;
        var r0 = mem.Range(st.Id, gid, u.Tick)!.Value;
        Assert.Equal(p0, r0.Lo); Assert.Equal(p0, r0.Hi); Assert.False(r0.Enough);
        // ein Tal von einem Tick mitten in einem 20-Tick-Abschnitt
        u.Tick++; e.Price = p0 * 0.5f; mem.Observe(u);
        u.Tick++; e.Price = p0 * 3f; mem.Observe(u);
        var r1 = mem.Range(st.Id, gid, u.Tick)!.Value;
        Assert.Equal(0.5f * p0, r1.Lo, 3); Assert.Equal(3f * p0, r1.Hi, 3);
        for (int i = 0; i < 1300; i++) { u.Tick++; mem.Observe(u); }   // das Tal fiel aus dem Fenster
        var r2 = mem.Range(st.Id, gid, u.Tick)!.Value;
        Assert.Equal(3f * p0, r2.Lo, 3); Assert.True(r2.Enough);
        mem.Observe(World());
        Assert.Null(mem.Range(new StationId(999999), gid, 0));
    }

    /// <summary>Runde 27: Ampel — grün nur, wenn selbst der schlechte Fall 10 % über dem Einkauf liegt, mit Verlauf, Kasse 4× und Annahme 2×; rot schon bei
    /// Verlust zum heutigen Preis oder Teilannahme; sonst gelb.</summary>
    [Fact]
    public void Ampel_GruenNurImSchlechtenFall()
    {
        var u = World();
        var st = u.StationsById().First();
        BuyerQuote Q(float lo, float now, float cash, float accept, bool hist = true, float units = 10f) =>
            new(st, lo, units, units * lo, 0f, 1, 10f, now, now, cash, accept, now, hist);
        Assert.Equal(TripVerdict.Good, MarketInfo.Verdict(Q(11.5f, 14f, 5f, 3f), 10f, 10f));
        Assert.Equal(TripVerdict.Unsure, MarketInfo.Verdict(Q(11.5f, 14f, 5f, 3f, hist: false), 10f, 10f));   // kein Verlauf → nie grün
        Assert.Equal(TripVerdict.Unsure, MarketInfo.Verdict(Q(10.5f, 14f, 5f, 3f), 10f, 10f));   // schlechter Fall unter +10 %
        Assert.Equal(TripVerdict.Unsure, MarketInfo.Verdict(Q(8f, 14f, 5f, 3f), 10f, 10f));     // schlechter Fall Verlust, heute Gewinn
        Assert.Equal(TripVerdict.Unsure, MarketInfo.Verdict(Q(11.5f, 14f, 3f, 3f), 10f, 10f));  // Kasse nur 3×
        Assert.Equal(TripVerdict.Unsure, MarketInfo.Verdict(Q(11.5f, 14f, 5f, 1.5f), 10f, 10f)); // Lager fast voll
        Assert.Equal(TripVerdict.Loss, MarketInfo.Verdict(Q(8f, 9f, 5f, 3f), 10f, 10f));        // schon heute Verlust
        Assert.Equal(TripVerdict.Loss, MarketInfo.Verdict(Q(11.5f, 14f, 5f, 3f, units: 5f), 10f, 10f));   // nimmt nur 5
    }

    /// <summary>Runde 27: Rangfolge nach dem schlechten Fall (Menge × niedrigster Preis), Preis ≤ Ankunftspreis ≤ heutiger Preis ≤ Höchstpreis.</summary>
    [Fact]
    public void Abnehmer_RangfolgeNachSchlechtemFall()
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
                Assert.True(q.Price <= q.PriceArrival + 0.001f && q.PriceArrival <= q.PriceNow + 0.001f && q.PriceNow <= q.PriceHigh + 0.001f);
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
        Assert.EndsWith($"= {-r.Credits:0.#} Cr", r.Message);
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
        // Runde 27: ein Fels schrumpft innerhalb des Ticks (Abbau in der Ansicht) → neue Liste
        var rock = u.Sectors[p.Sector].Asteroids.FirstOrDefault(r => r.MaxOre > 0f && r.Ore > 1f);
        if (rock is not null)
        {
            rock.Ore *= 0.5f;
            var c = Navigator.PlayerObstacles(u, p.Sector, p.Position, null);
            Assert.NotSame(a, c);
            Assert.Contains(c, o => MathF.Abs(o.Radius - rock.Radius) < 0.001f && o.Position == rock.Position);
            a = c;
        }
        u.Tick++;
        Assert.NotSame(a, Navigator.PlayerObstacles(u, p.Sector, p.Position, null));
    }
}
