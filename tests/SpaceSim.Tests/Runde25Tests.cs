using SpaceSim.Core.Content;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 25: Autopilot im dichten Felsfeld (Zickzack-Plan geglättet) und die Prüfung der Handelsmargen.</summary>
public class Runde25Tests
{
    /// <summary>
    /// Selbsttest Stufe 79 (Zustand nach den Vorstufen): der Autopilot kreiste 86 m vor dem HQ Alpha in einem dichten Felsfeld — die Umweg-Rekursion lieferte
    /// Zickzack-Punkte dicht nebeneinander (Tiefe/Budget erschöpft). Felsen wie gemessen; der geglättete Weg hat keine Punkte dichter als 5 m, jedes Stück ist
    /// frei, und er endet am Ziel.
    /// </summary>
    [Fact]
    public void Autopilot_DichtesFelsfeld_GeglaetteterWegOhneZickzack()
    {
        var obs = new List<Obstacle> { new(new(94, 73), 4.6f), new(new(96, 61), 3.9f), new(new(83, 74), 1.5f), new(new(76, 79), 4.5f), new(new(81, 43), 4.5f),
            new(new(91, 51), 5.2f), new(new(94, 85), 3.8f), new(new(136, -4), 30f) };
        float margin = PathPlanner.Margin + Collision.ShipRadius;
        foreach (var from in new[] { new Vec2(82, 58), new Vec2(86, 57), new Vec2(77, 54), new Vec2(78, 67) })
        {
            var raw = PathPlanner.Plan(from, new Vec2(148, 31), obs, margin);
            var p = PathPlanner.Smooth(raw, obs, margin);
            Assert.True(p.Count <= raw.Count);
            // wie PathFollower: läuft der geglättete Plan noch durch Felsen, der Ausweichweg über feste Ringpunkte
            bool blocked = false;
            for (int i = 1; i < p.Count; i++) blocked |= !PathPlanner.Clear(p[i - 1], p[i], obs, margin);
            if (blocked) p = PathPlanner.Detour(from, new Vec2(148, 31), obs, margin)!;
            Assert.NotNull(p);
            Assert.Equal(from, p[0]);
            Assert.Equal(new Vec2(148, 31), p[^1]);
            for (int i = 1; i < p.Count; i++)
            {
                Assert.True(Vec2.Distance(p[i - 1], p[i]) > 5f, $"Zickzack: ({p[i - 1].X:0},{p[i - 1].Y:0}) → ({p[i].X:0},{p[i].Y:0})");
                Assert.True(PathPlanner.Clear(p[i - 1], p[i], obs, margin), $"Stück durch Fels: ({p[i - 1].X:0},{p[i - 1].Y:0}) → ({p[i].X:0},{p[i].Y:0})");
            }
        }
    }

    /// <summary>
    /// Runde 25 (Gerätetest b02–b05): im Kriegstest flog „Route nach Vesta“ aus Beta über Delta — der Weg zum Vesta-Tor lief 3 m am Delta-Tor vorbei und das
    /// Schiff sprang ungewollt. Jetzt: der Folger-Weg betritt den Sprungradius keines anderen Tors und endet am Vesta-Tor.
    /// </summary>
    [Fact]
    public void Autopilot_Kriegstest_RouteNachVesta_OhneUngewolltenSprung()
    {
        var u = WorldBuilder.BuildDefault(1, "war");
        var p = u.Ships[u.PlayerShip];
        var vesta = u.Sectors.Values.First(s => s.Name.Contains("Vesta")).Id;
        var target = Navigator.ForSector(u, vesta, p.Id);
        var route = u.FindRoute(p.Sector, vesta, Travel.ProfileOf(u, p))!;
        Assert.Single(route);
        var goalGate = u.Gates[route[0]];
        var f = new PathFollower();
        var pos = p.Position;
        bool reached = false;
        for (int i = 0; i < 2000 && !reached; i++)
        {
            var st = f.Next(u, p.Sector, pos, target, p);
            Assert.Equal(goalGate.Id, st.Gate);
            var d = st.Waypoint - pos;
            float len = d.Length;
            if (len > 0.01f) pos += d * (MathF.Min(2f, len) / len);
            foreach (var gid in u.Sectors[p.Sector].Gates)
            {
                float dist = Vec2.Distance(pos, u.Gates[gid].Position);
                if (gid == goalGate.Id) reached |= dist < Navigator.GateJumpRadius;
                else Assert.True(dist >= Navigator.GateJumpRadius, $"Schritt {i}: im Sprungradius von Tor {gid.Value} ({pos.X:0}/{pos.Y:0})");
            }
        }
        Assert.True(reached, "Vesta-Tor nicht erreicht");
    }

    /// <summary>Runde 25: Preis bei Ankunft — dieselbe Bestandsformel wie der Markt mit den Lieferungen anderer; ohne Lieferungen der heutige Preis.</summary>
    [Fact]
    public void Abnehmer_PreisBeiAnkunft_FolgtDerBestandsformel()
    {
        var u = WorldBuilder.BuildDefault(1);
        new SimulationRunner().Run(u, 1);
        int checkedEntries = 0;
        foreach (var st in u.StationsById().Where(s => !s.IsBuildSite))
            foreach (var (gid, e) in st.Market)
            {
                var g = u.Goods[gid];
                if (e.Price <= 0f || MathF.Abs(e.Price - MarketSystem.StockPrice(g, e, e.Amount)) > 0.01f || e.Target <= 1f) continue;
                Assert.Equal(e.Price, MarketInfo.ArrivalPrice(u, st, gid, 0f));
                float later = MarketInfo.ArrivalPrice(u, st, gid, e.Target);
                Assert.Equal(MarketSystem.StockPrice(g, e, e.Amount + e.Target), later, 3);
                Assert.True(later <= e.Price + 0.001f);
                checkedEntries++;
            }
        Assert.True(checkedEntries > 20, $"nur {checkedEntries} Einträge geprüft");
    }

    static Ship Own(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        s.HomeBase = at.Id;
        u.AddShip(s);
        return s;
    }

    /// <summary>
    /// Runde 25 (Gerätetest „Befehle 5/4“ nach dem Auflösen eines Verbands): laufende Arbeitsbefehle bleiben, neue Einzelbefehle sind gesperrt, bis die Zahl
    /// unter die Kapazität fällt; einen bestehenden Befehl zu ändern bleibt erlaubt. Die Anzeige nennt das.
    /// </summary>
    [Fact]
    public void Befehlskapazitaet_UeberDerGrenzeNachAufloesen_NeueGesperrtLaufendeBleiben()
    {
        var u = WorldBuilder.BuildDefault(1);
        var beta = u.StationsById().First(s => s.Name == "Handelsstation Beta");
        var a = Own(u, "nomad", beta); var b = Own(u, "nomad", beta); var c = Own(u, "nomad", beta);
        var sq = Squadrons.Create(u, a, "Test", out var m0)!;
        Assert.NotNull(sq);
        Assert.True(Squadrons.Join(u, sq, b, out var m1), m1);
        Assert.True(Squadrons.Join(u, sq, c, out var m2), m2);
        Assert.True(Squadrons.Command(u, sq, new TradeCommand(new AutoLimits(beta.Sector, 2, 1.5f, 0f)), out var m3), m3);
        // erst alle Schiffe kaufen (jedes Schiff hebt die Befehlsstufe ein wenig), dann die Kapazität mit Einzelbefehlen füllen
        var spare = Enumerable.Range(0, 12).Select(_ => Own(u, "nomad", beta)).ToList();
        int cap = Commands.Capacity(u, u.PlayerActor).Ships;
        Assert.True(cap < spare.Count);
        foreach (var s in spare.Take(cap)) Assert.True(Commands.Give(u, s, new HoldCommand(), out var mg), mg);
        Assert.Equal(cap, Commands.Commanded(u, u.PlayerActor));
        Assert.Equal(0, Commands.OverCapacity(u, u.PlayerActor));
        Assert.Equal("", Commands.OverCapacityText(u, u.PlayerActor));
        // Runde 36: Auflösen über der Grenze ist verweigert (die Umgehung ist geschlossen); über die Kapazität kommt man nur noch, wenn die Befehlsstufe sinkt
        Assert.False(Squadrons.Disband(u, sq));
        u.Rules.CommandShips = u.Rules.CommandShips.Select(x => x - 2).ToList();
        cap = Commands.Capacity(u, u.PlayerActor).Ships;
        Assert.All(new[] { a, b, c }, s => Assert.IsType<TradeCommand>(s.Command));   // Arbeitsbefehle bleiben
        Assert.True(Commands.OverCapacity(u, u.PlayerActor) > 0, $"Befehle {Commands.Commanded(u, u.PlayerActor)}/{Commands.Capacity(u, u.PlayerActor).Ships} vorher {cap}, a {a.Command} b {b.Command} c {c.Command}");
        Assert.Contains("neue Einzelbefehle gesperrt", Commands.OverCapacityText(u, u.PlayerActor));
        var extra = spare[^1];
        Assert.Equal(cap, Commands.Capacity(u, u.PlayerActor).Ships);
        Assert.False(Commands.Give(u, extra, new HoldCommand(), out var mx));
        Assert.Contains("Befehlskapazität erreicht", mx);
        Assert.True(Commands.Give(u, spare[0], new HoldCommand(), out var my), my);   // ändern erlaubt
    }

    /// <summary>Runde 25 (Gerätetest: Werftkasse 59–449 Cr): kann die Station den Schiffspreis nicht zahlen, nennt das Angebot den Grund mit der Kasse.</summary>
    [Fact]
    public void Hangar_Verkauf_KasseDerStationZuKlein_GrundImAngebot()
    {
        var u = WorldBuilder.BuildDefault(1);
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        var own = Own(u, "nomad", yard);
        yard.Credits = 59f;
        var svc = new HangarService();
        var o = svc.Offers(u, yard, u.Ships[u.PlayerShip]).First(x => x.Key == $"{HangarService.SellShip}:{own.Id.Value}");
        Assert.Equal(0f, o.Amount);
        Assert.Contains("Kasse 59 von", o.Note);
    }
}
