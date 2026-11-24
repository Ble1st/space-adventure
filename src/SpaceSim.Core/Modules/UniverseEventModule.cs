using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Modules;

/// <summary>Universumsereignisse (Erzschwemme, Frachtfund, Wrackfeld): Ablauf und neue Ereignisse, am Ende des Ticks; ohne Schalter nichts.</summary>
public sealed class UniverseEventModule : ISimModule
{
    public string Name => "UniverseEvents";
    public void Register(SimulationBuilder b) => b.Add(new UniverseEventSystem(), UniverseEventSystem.Interval);
}
