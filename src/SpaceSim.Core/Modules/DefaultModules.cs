using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Modules;

public sealed class ProductionModule : ISimModule
{
    public string Name => "Production";
    public void Register(SimulationBuilder b) => b.Add(new ProductionSystem()).Add(new MarketSystem());
}

public sealed class ResourceModule : ISimModule
{
    public string Name => "Resources";
    // Regeneration ist langsam und idempotent skalierbar → seltener rechnen
    public void Register(SimulationBuilder b) => b.Add(new RegenSystem(), interval: 4).Add(new MinerSystem());
}

public sealed class TradeModule : ISimModule
{
    public string Name => "Trade";
    public void Register(SimulationBuilder b) => b.Add(new TraderSystem());
}

public sealed class LifecycleModule : ISimModule
{
    public string Name => "Lifecycle";
    public void Register(SimulationBuilder b) { /* wird in der Wirtschaftserweiterung befüllt */ }
}
