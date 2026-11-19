using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// „Treibstoff und Forschung dauerhaft an“: die Standardwelt hat beide Schalter an — fünf Raffinerien, Forschungsstationen, Startschiffe mit vollem Tank, die
/// eine Tankstelle erreichen; die Bau-Reserve des Lebenszyklus bleibt wie vorher (vier freie Plätze + zwei).
/// </summary>
public class FuelResearchDefaultTests
{
    [Fact]
    public void Standardwelt_TreibstoffUndForschungAn_RaffinerienUndForschungsstationen()
    {
        var u = DemoUniverse.Create();
        Assert.True(u.Rules.FuelActive);
        Assert.True(u.Rules.ResearchActive);
        Assert.Equal(5, u.Stations.Values.Count(s => s.TypeKey == "fuel_refinery"));
        Assert.Equal(3, u.Stations.Values.Count(s => s.TypeKey == "research_station"));
        Assert.NotEmpty(u.Research.Projects);
        // Kopfraum des Lebenszyklus: wie mit den Schaltern aus (dort fehlen die fünf Raffinerien und MaxStations sinkt um fünf)
        var off = TestWorlds.DefaultOff();
        int Free(Universe w) => w.Rules.MaxStations - StationProtection.CountEconomic(w);
        Assert.Equal(Free(off), Free(u));
        Assert.Equal(off.Rules.MaxStations + 9, u.Rules.MaxStations);   // Gasernte: + zwei Gasraffinerien; Fusion: + zwei Anreicherungsanlagen (reine Treibstoffhersteller)
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fighter")]
    [InlineData("trader")]
    [InlineData("miner")]
    public void Startschiffe_VollerTank_ErreichenEineTankstelle(string? start)
    {
        var u = WorldBuilder.BuildDefault(startOption: start);
        new SimulationRunner().Run(u, 1);   // Preise gesetzt (eine Tankstelle braucht einen Marktpreis)
        var me = u.Ships[u.PlayerShip];
        Assert.True(me.Stats.FuelCapacity > 0f);
        Assert.Equal(me.Stats.FuelCapacity, me.TotalFuel, 1);
        var fuel = Refuel.ActiveFuel(me);
        Assert.NotNull(Refuel.NearestFuelStation(u, me, fuel!.Value, me.TotalFuel * 0.25f));   // schon mit einem Viertel des Tanks erreichbar
    }
}
