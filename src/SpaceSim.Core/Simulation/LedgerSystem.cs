using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Simulation;

/// <summary>Runde 30: lässt Rückkauf-Posten verfallen, sobald der Bestand der Station wieder den Stand vor dem Spielerhandel erreicht (<see cref="PlayerLedger.Prune"/>).</summary>
public sealed class LedgerSystem : ISimSystem
{
    public void Step(Universe u, int dt) => PlayerLedger.Prune(u);
}
