using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Modules;

public sealed class ProductionModule : ISimModule
{
    public string Name => "Production";
    public void Register(SimulationBuilder b) => b.Add(new ProductionSystem()).Add(new MarketSystem());
}

/// <summary>
/// Phase 6: Aufgabenwahl der Multirole-Schiffe. Läuft vor den ausführenden Systemen (Schürfen, Handel), damit ein Wechsel im selben Tick
/// vom neuen Ausführer übernommen wird.
/// </summary>
public sealed class JobModule : ISimModule
{
    public string Name => "Jobs";
    public void Register(SimulationBuilder b) => b.Add(new JobAssignmentSystem());
}

public sealed class ResourceModule : ISimModule
{
    public string Name => "Resources";
    // Respawn ist ein seltenes Zufallsereignis → mit Intervall (Wahrscheinlichkeit skaliert mit dt)
    public void Register(SimulationBuilder b) => b.Add(new AsteroidRespawnSystem(), interval: 10).Add(new MinerSystem());
}

public sealed class TradeModule : ISimModule
{
    public string Name => "Trade";
    public void Register(SimulationBuilder b) => b.Add(new TraderSystem());
}

/// <summary>
/// Phase 7: NPC-Upgrades. Läuft nach dem Handel (wer gerade an einer Werft angekommen und untätig ist, liegt dort) und vor dem Lebenszyklus.
/// </summary>
public sealed class UpgradeModule : ISimModule
{
    public string Name => "Upgrades";
    public void Register(SimulationBuilder b) => b.Add(new UpgradeSystem());
}

public sealed class LifecycleModule : ISimModule
{
    public string Name => "Lifecycle";
    // Seltene, kostengünstige Prüfungen: Skalierung über dt bzw. Zeitstempel
    public void Register(SimulationBuilder b) =>
        b.Add(new UpkeepSystem(), interval: 10).Add(new StationLifecycleSystem(), interval: 20).Add(new FleetSystem(), interval: 20);
}
