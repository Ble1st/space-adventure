using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

public interface ISimSystem { void Step(Universe u); }

public sealed class SimulationRunner
{
    readonly List<ISimSystem> _systems;

    public SimulationRunner(IEnumerable<ISimSystem>? systems = null)
    {
        _systems = systems?.ToList() ?? new List<ISimSystem>
        {
            new ProductionSystem(), new MarketSystem(), new RegenSystem(), new MinerSystem(), new TraderSystem(),
        };
    }

    public void Step(Universe u)
    {
        foreach (var s in _systems) s.Step(u);
        u.Tick++;
    }

    public void Run(Universe u, int ticks) { for (int i = 0; i < ticks; i++) Step(u); }
}
