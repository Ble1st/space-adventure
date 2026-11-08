using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

public sealed class RegenSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        foreach (var sector in u.Sectors.Values)
            foreach (var f in sector.Fields)
            {
                if (f.RegenPerTick <= 0f) continue;
                f.Amount += f.RegenPerTick * dt;
                if (f.Capacity > 0f) f.Amount = MathF.Min(f.Amount, f.Capacity);
            }
    }
}
