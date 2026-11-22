using SpaceSim.Core.Demo;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Debug-Menü (Nutzerwunsch): eigene Schiffe, Stationen und Waren für Tests spawnen.</summary>
public class DebugSpawnTests
{
    static Universe World()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        return u;
    }

    [Fact]
    public void Schiff_AngedocktAnDerStation_GehoertDemSpieler()
    {
        var u = World();
        var me = u.Ships[u.PlayerShip];
        var st = u.StationsById().First(s => s.Sector == me.Sector && s.Dockable);
        PlayerFleet.Dock(u, me, st);
        int before = u.Ships.Count;
        var ship = DebugSpawn.SpawnShip(u, "pickaxe", out var msg);
        Assert.NotNull(ship);
        Assert.Equal(before + 1, u.Ships.Count);
        Assert.True(ship!.IsPlayer);
        Assert.Equal(u.PlayerActor, ship.Owner);
        Assert.Equal(st.Id, ship.DockedAt);
        Assert.Contains(ship, PlayerFleet.ParkedAt(u, st));
        Assert.Equal(ship.MaxHull, ship.Hull);
        Assert.Contains("gespawnt", msg);
        new SimulationRunner().Run(u, 50);   // läuft in der Welt ohne Fehler
        Assert.True(u.Ships.ContainsKey(ship.Id));
    }

    [Fact]
    public void Schiff_ImRaum_NebenDemAktivenSchiff()
    {
        var u = World();
        var me = u.Ships[u.PlayerShip];
        PlayerFleet.Undock(u, me);
        var ship = DebugSpawn.SpawnShip(u, "falcon", out _)!;
        Assert.Equal(0, ship.DockedAt.Value);
        Assert.Equal(me.Sector, ship.Sector);
        Assert.InRange(Vec2.Distance(ship.Position, me.Position), 1f, 40f);
        Assert.Null(DebugSpawn.SpawnShip(u, "gibtsnicht", out var why));
        Assert.Contains("Unbekannt", why);
    }

    [Fact]
    public void Station_FertigUndEigen_ImSektor()
    {
        var u = World();
        var me = u.Ships[u.PlayerShip];
        PlayerFleet.Undock(u, me);
        var st = DebugSpawn.SpawnStation(u, "steelworks", out var msg);
        Assert.NotNull(st);
        Assert.Equal(u.PlayerActor, st!.Owner);
        Assert.Equal(me.Sector, st.Sector);
        Assert.False(st.IsBuildSite);
        Assert.True(u.Stations.ContainsKey(st.Id));
        Assert.Contains("gespawnt", msg);
        new SimulationRunner().Run(u, 50);
    }

    [Fact]
    public void Waren_BisZumLaderaum_UndKasse()
    {
        var u = World();
        var me = u.Ships[u.PlayerShip];
        var iron = u.FindGood("iron_ore")!;
        float added = DebugSpawn.AddCargo(u, me, iron.Id, 100000f, out _);
        Assert.True(added > 0f);
        Assert.True(me.UsedVolume(u) <= me.CargoCapacity + 0.01f);
        Assert.Equal(0f, DebugSpawn.AddCargo(u, me, iron.Id, 10f, out var full));
        Assert.Contains("Kein Platz", full);
        DebugSpawn.SetCredits(u, 123456f);
        Assert.Equal(123456f, u.PlayerWallet.Credits);
        Assert.NotEmpty(DebugSpawn.ShipModels(u));
        Assert.DoesNotContain(DebugSpawn.StationTypes(u), t => t.Key == "build_site");
    }
}
