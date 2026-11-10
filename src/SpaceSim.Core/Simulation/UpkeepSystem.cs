using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Geldkreislauf: Stationen haben Unterhalt (Sink) und optional Einnahmen aus Verkäufen außerhalb der Welt (Faucet);
/// Handels- und Minenschiffe zahlen Wartung (Sink). Ohne diese Ströme würden Credits nur im Kreis laufen oder endlos wachsen.
/// </summary>
public sealed class UpkeepSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        foreach (var st in u.Stations.Values)
            if (u.StationTypes.TryGetValue(st.TypeKey, out var type))
                st.Credits += (type.IncomePerTick - type.UpkeepPerTick) * dt;
        foreach (var st in u.Stations.Values)
            if (st.Credits > 0f) st.Credits -= st.Credits * u.Rules.StationWealthTax * dt;

        float upkeep = u.Rules.ShipUpkeepPerTick * dt;
        foreach (var ship in u.Ships.Values)
            if (ship.IsEconomyAi)
            {
                ship.Credits -= MathF.Min(ship.Credits, upkeep);
                if (ship.Credits > u.Rules.WealthTaxFree) ship.Credits -= (ship.Credits - u.Rules.WealthTaxFree) * u.Rules.ShipWealthTax * dt;
            }
    }
}
