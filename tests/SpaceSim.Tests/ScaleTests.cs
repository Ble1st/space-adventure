using System.Diagnostics;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using Xunit.Abstractions;

namespace SpaceSim.Tests;

/// <summary>Grobe Leistungsgrenzen: schützt davor, dass ein Feature die Simulation unbemerkt um Größenordnungen verlangsamt.</summary>
public class ScaleTests
{
    readonly ITestOutputHelper _out;
    public ScaleTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void ManyShips_StillSimulateQuickly_AndMaterializeCheaply()
    {
        var u = DemoUniverse.Create();
        u.Rules.MaxTraders = 1_000;
        var sectors = u.Sectors.Keys.ToList();
        for (int i = 0; i < 300; i++)
        {
            var sec = sectors[i % sectors.Count];
            var ship = Core.Content.WorldBuilder.NewShip(u, u.NewShipId(), ShipRole.Trader, 1, sec, 1000, 100);
            u.AddShip(ship);
        }
        var runner = new SimulationRunner();
        runner.Run(u, 200);   // Einschwingen

        var sw = Stopwatch.StartNew();
        runner.Run(u, 600);
        double msPerTick = sw.Elapsed.TotalMilliseconds / 600;

        sw.Restart();
        for (int i = 0; i < 1_000; i++) Materializer.Snapshot(u, new SectorId(1 + i % 4), u.Tick + 0.5);
        double msPerSnapshot = sw.Elapsed.TotalMilliseconds / 1_000;

        _out.WriteLine($"{u.Ships.Count} Schiffe: {msPerTick:0.00} ms/Tick, Snapshot {msPerSnapshot:0.000} ms");
        Assert.True(msPerTick < 8.0, $"{msPerTick:0.0} ms/Tick bei {u.Ships.Count} Schiffen");   // Budget bei 4 Ticks/s: 250 ms
        Assert.True(msPerSnapshot < 0.5, $"Snapshot {msPerSnapshot:0.000} ms");                   // pro Frame (16 ms) aufgerufen
    }
}
