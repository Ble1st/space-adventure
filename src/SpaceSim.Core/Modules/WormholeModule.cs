using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Modules;

/// <summary>Wurmlöcher: instabile reißen auf und fallen zusammen (eigener RNG, ohne Schalter nichts). Die stabilen sind Torpaare und brauchen kein System.</summary>
public sealed class WormholeModule : ISimModule
{
    public string Name => "Wormholes";
    public void Register(SimulationBuilder b) => b.Add(new WormholeSystem(), WormholeSystem.Interval);
}
