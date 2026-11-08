using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

public class PersistenceTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RoundTrip_PreservesStateExactly()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 4_000);
        var before = UniverseHash.Compute(u);

        var (loaded, savedAt) = SaveGameJson.Read(SaveGameJson.Write(u, Now));

        Assert.Equal(before, UniverseHash.Compute(loaded));
        Assert.Equal(Now, savedAt);
        Assert.Equal(u.Stations.Count, loaded.Stations.Count);
        Assert.Equal(u.Ships.Count, loaded.Ships.Count);
    }

    [Fact]
    public void LoadedUniverse_ContinuesIdentically_ToTheOriginal()
    {
        var original = DemoUniverse.Create();
        var runner = new SimulationRunner();
        runner.Run(original, 3_000);

        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(original, Now));
        runner.Run(original, 5_000);
        new SimulationRunner().Run(loaded, 5_000);

        Assert.Equal(UniverseHash.Compute(original), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Load_RebuildsShipIndex_AndKeepsOrdersAndTransits()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 777);
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));

        foreach (var ship in loaded.Ships.Values)
        {
            if (ship.Transit is null) Assert.Contains(ship.Id, loaded.Tracker.ParkedIn(ship.Sector));
            else Assert.All(ship.Transit.Legs, l => Assert.Contains(ship.Id, loaded.Tracker.TransitIn(l.Sector)));
            Assert.Equal(u.Ships[ship.Id].Order, ship.Order);
        }
        Assert.Contains(loaded.Ships.Values, s => s.Order is TradeOrder or MineOrder);
    }

    [Fact]
    public void Load_KeepsIdCountersSoNewEntitiesDoNotCollide()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 20_000);   // erzeugt Schiffe/Stationen mit neuen Ids
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.DoesNotContain(loaded.NewShipId(), loaded.Ships.Keys);
        Assert.DoesNotContain(loaded.NewStationId(), loaded.Stations.Keys);
    }

    [Fact]
    public void NewerVersion_IsRejected_WithClearMessage()
    {
        var json = SaveGameJson.Write(DemoUniverse.Create(), Now).Replace("\"Version\":1", "\"Version\":99");
        var ex = Assert.Throws<SaveFormatException>(() => SaveGameJson.Read(json));
        Assert.Contains("neueren Version", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ kaputt")]
    [InlineData("null")]
    public void Garbage_ThrowsSaveFormatException(string json) =>
        Assert.Throws<SaveFormatException>(() => SaveGameJson.Read(json));

    [Fact]
    public void InconsistentSave_ThrowsSaveFormatException_NotKeyNotFound()
    {
        var u = DemoUniverse.Create();
        var save = UniverseSerializer.ToSave(u, Now);
        save.Ships[0].Role = "Raumpirat";
        var json = System.Text.Json.JsonSerializer.Serialize(save, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        Assert.Throws<SaveFormatException>(() => SaveGameJson.Read(json));
    }

    [Fact]
    public void OfflineCatchUp_RunsElapsedTicks_AndRespectsCap()
    {
        var u = DemoUniverse.Create();
        var runner = new SimulationRunner();

        var small = OfflineCatchUp.Run(u, runner, TimeSpan.FromSeconds(30));
        Assert.Equal(30 * SimClock.TicksPerSecond, small.TicksRun);
        Assert.False(small.Capped);
        Assert.Equal(small.TicksRun, u.Tick);

        var big = OfflineCatchUp.Run(u, runner, TimeSpan.FromHours(10), maxTicks: 500);
        Assert.Equal(500, big.TicksRun);
        Assert.True(big.Capped);
    }

    [Fact]
    public void OfflineCatchUp_NegativeElapsed_DoesNothing()
    {
        var u = DemoUniverse.Create();
        Assert.Equal(0, OfflineCatchUp.Run(u, new SimulationRunner(), TimeSpan.FromSeconds(-5)).TicksRun);
    }
}
