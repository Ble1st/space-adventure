using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

public sealed class ProductionSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        foreach (var st in u.Stations.Values)
            foreach (var r in st.Production)
            {
                if (r.TicksPerCycle <= 0 || u.Tick % r.TicksPerCycle != 0) continue;
                if (!r.Inputs.All(kv => st.Stock(kv.Key).Amount >= kv.Value)) continue;
                foreach (var kv in r.Inputs) st.Stock(kv.Key).Amount -= kv.Value;
                foreach (var kv in r.Outputs) st.Stock(kv.Key).Amount += kv.Value;
            }
    }
}
