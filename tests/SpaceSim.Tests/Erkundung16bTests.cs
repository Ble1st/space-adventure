using SpaceSim.Core.Content;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 16b: <see cref="TradeActions.BestBuyer"/> berücksichtigt für Spielerschiffe (aktiv oder im Besitz des Spielerakteurs) nur Stationen,
/// die der Spieler kennt (<see cref="Knowledge.KnowsStation"/>) — Auto-Mining, Bergen/Liefern und Convoy-Rest fliegen keine unbekannten Stationen mehr an.
/// NPC-Schiffe sind unverändert; ohne beschränktes Wissen (Regel playerIntel aus, <c>Actor.Intel</c> null) kennt der Spieler alles und es bleibt wie bisher.
/// </summary>
public class Erkundung16bTests
{
    /// <summary>Standardwelt (feste Namen) mit Startpaket; Erkundung an/aus.</summary>
    static Universe World(bool intel = true, string start = "miner")
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.RandomNames = false;
        def.Rules!.PlayerIntel = intel;
        return WorldBuilder.Build(def, null, start);
    }

    static Ship Player(Universe u) => u.Ships[u.PlayerShip];

    static Ship Own(Universe u, string model, Station at, Controller controller = Controller.Player, ActorId? owner = null)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, controller, owner ?? u.PlayerActor, at, 0f);
        s.HomeBase = at.Id;
        u.AddShip(s);
        s.RecomputeStats(u);
        return s;
    }

    /// <summary>Aktives Spielerschiff freischalten (undocken) und an eine Stelle im Startsektor setzen.</summary>
    static Ship Place(Universe u, Ship p)
    {
        var home = u.Stations[u.Sectors[u.PlayerSector].Stations.OrderBy(id => id.Value).First()];
        p.Sector = u.PlayerSector;
        p.Position = home.Position;
        p.DockedAt = default;
        return p;
    }

    /// <summary>
    /// Eine dem Spieler unbekannte, erreichbare und andockbare Station mit einem künstlich attraktiven Kaufangebot für <paramref name="good"/>:
    /// danach ist sie für ein NPC-Schiff mit Fracht der beste Käufer überhaupt.
    /// </summary>
    static Station UnknownBestBuyer(Universe u, Ship p, GoodId good)
    {
        var size = Docking.SizeOf(u, p);
        var unk = u.Stations.Values
            .Where(s => !s.IsBuildSite && (!Knowledge.Limited(u) || !Knowledge.KnowsStation(u, s)) && Docking.Allows(s, size))
            .OrderBy(s => s.Id.Value)
            .First(s => !float.IsPositiveInfinity(Travel.Estimate(u, p.Sector, p.Position, s.Sector, s.DockPoint(p.Id), p)));
        unk.Market[good] = new StockEntry { Price = 1_000_000f, Amount = 0f, Target = 10_000f };
        unk.Credits = 1_000_000_000f;
        return unk;
    }

    static GoodId TradedGood(Universe u)
    {
        // eine Ware, die mindestens eine bekannte Station mit mindestens einer Einheit kauft (sonst quote = 0 überall außer dem künstlichen Angebot)
        var known = u.Stations.Values.Where(s => !s.IsBuildSite && Knowledge.KnowsStation(u, s)).ToList();
        var g = u.Goods.Keys.OrderBy(x => x.Value)
            .FirstOrDefault(g => known.Any(s => s.Market.TryGetValue(g, out var e) && e.Price > 0f && s.AcceptableUnits(g, u.Rules) >= 1f));
        return g.Value != 0 ? g : u.Goods.Keys.OrderBy(x => x.Value).First();
    }

    [Fact]
    public void BestBuyer_Spielerschiff_IgnoriertUnbekannteStation_NpcNicht()
    {
        var u = World();
        new SimulationRunner().Run(u, 2);
        var p = Place(u, Player(u));
        var good = TradedGood(u);
        var unk = UnknownBestBuyer(u, p, good);
        Assert.False(Knowledge.KnowsStation(u, unk));

        // NPC-Schiff an derselben Stelle mit derselben Fracht: die unbekannte Station ist der beste Käufer
        var home = u.Stations[u.Sectors[u.PlayerSector].Stations.OrderBy(id => id.Value).First()];
        var npc = Own(u, "nomad", home, Controller.Ai, u.FreeActor);
        npc.Role = ShipRole.Trader;
        npc.Sector = p.Sector; npc.Position = p.Position; npc.DockedAt = default;
        npc.Cargo[good] = 100f;
        Assert.Equal(unk.Id, TradeActions.BestBuyer(u, npc, npc.Sector, npc.Position)?.Id);

        // aktives Spielerschiff: nie die unbekannte Station, nur Bekanntes (oder null)
        p.Cargo[good] = 100f;
        var bp = TradeActions.BestBuyer(u, p, p.Sector, p.Position);
        Assert.NotEqual(unk.Id, bp?.Id);
        Assert.True(bp is null || Knowledge.KnowsStation(u, bp));

        // eigenes Flottenschiff (nicht das aktive): ebenso
        var own = Own(u, "nomad", home);
        own.Sector = p.Sector; own.Position = p.Position; own.DockedAt = default;
        own.Cargo[good] = 100f;
        var bo = TradeActions.BestBuyer(u, own, own.Sector, own.Position);
        Assert.NotEqual(unk.Id, bo?.Id);
        Assert.True(bo is null || Knowledge.KnowsStation(u, bo));

        // KI-gesteuertes Schiff im Besitz des Spielerakteurs (Besitzer zählt): ebenso
        var aiOwned = Own(u, "nomad", home, Controller.Ai, u.PlayerActor);
        aiOwned.Sector = p.Sector; aiOwned.Position = p.Position; aiOwned.DockedAt = default;
        aiOwned.Cargo[good] = 100f;
        Assert.False(aiOwned.IsPlayer);
        var ba = TradeActions.BestBuyer(u, aiOwned, aiOwned.Sector, aiOwned.Position);
        Assert.NotEqual(unk.Id, ba?.Id);
        Assert.True(ba is null || Knowledge.KnowsStation(u, ba));

        // deckt der Spieler die Station auf, darf er sie auch anfliegen
        Knowledge.RevealStation(u, unk.Id);
        Assert.Equal(unk.Id, TradeActions.BestBuyer(u, p, p.Sector, p.Position)?.Id);
    }

    [Fact]
    public void BestBuyer_OhneIntel_Unveraendert()
    {
        var u = World(intel: false);
        new SimulationRunner().Run(u, 2);
        Assert.Null(Knowledge.Of(u));
        Assert.All(u.Stations.Keys, id => Assert.True(Knowledge.KnowsStation(u, id)));
        var p = Place(u, Player(u));
        var good = TradedGood(u);
        var unk = UnknownBestBuyer(u, p, good);
        p.Cargo[good] = 100f;
        // mit Intel aus bleibt alles wie heute: das Spielerschiff bekommt die (hier "unbekannte") Station ganz normal
        Assert.Equal(unk.Id, TradeActions.BestBuyer(u, p, p.Sector, p.Position)?.Id);
    }

    [Fact]
    public void BestBuyer_NpcWelt_UnabhaengigVomIntel_Unveraendert()
    {
        // NPC-Schiffe liefern mit und ohne beschränktes Wissen dasselbe Ergebnis (die NPC-Welt merkt nichts vom Spielerwissen)
        foreach (var intel in new[] { true, false })
        {
            var u = World(intel);
            new SimulationRunner().Run(u, 2);
            var p = Place(u, Player(u));
            var good = TradedGood(u);
            var unk = UnknownBestBuyer(u, p, good);
            var home = u.Stations[u.Sectors[u.PlayerSector].Stations.OrderBy(id => id.Value).First()];
            var npc = Own(u, "nomad", home, Controller.Ai, u.FreeActor);
            npc.Role = ShipRole.Trader;
            npc.Sector = p.Sector; npc.Position = p.Position; npc.DockedAt = default;
            npc.Cargo[good] = 100f;
            Assert.Equal(unk.Id, TradeActions.BestBuyer(u, npc, npc.Sector, npc.Position)?.Id);
        }
    }
}
