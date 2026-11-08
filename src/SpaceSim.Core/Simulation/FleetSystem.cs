using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Flottenentwicklung: Erfolgreiche Schiffe kaufen weitere Schiffe (bis zur Obergrenze je Rolle),
/// dauerhaft untätige und mittellose Schiffe werden aufgegeben.
/// </summary>
public sealed class FleetSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        Abandon(u);
        Expand(u, ShipRole.Trader, u.Rules.MaxTraders);
        Expand(u, ShipRole.Miner, u.Rules.MaxMiners);
    }

    static void Abandon(Universe u)
    {
        foreach (var ship in u.Ships.Values.Where(s => s.Role is ShipRole.Trader or ShipRole.Miner).ToList())
            if (ship.Transit is null && ship.Order is null && ship.IdleSince >= 0
                && u.Tick - ship.IdleSince > u.Rules.ShipBankruptTicks && ship.Credits < 50f)
                u.RemoveShip(ship.Id, "bankrupt");
    }

    static void Expand(Universe u, ShipRole role, int max)
    {
        var rules = u.Rules;
        if (u.Ships.Values.Count(s => s.Role == role) >= max) return;
        var parent = u.Ships.Values
            .Where(s => s.Role == role && s.Transit is null && s.Credits >= rules.FleetExpandCredits)
            .OrderByDescending(s => s.Credits).ThenBy(s => s.Id.Value).FirstOrDefault();
        if (parent is null) return;

        parent.Credits -= rules.NewShipCost + rules.NewShipStartCredits;   // Schiffsbau vernichtet NewShipCost
        u.AddShip(new Ship
        {
            Id = u.NewShipId(), Role = role, Faction = parent.Faction, CargoCapacity = parent.CargoCapacity,
            Sector = parent.Sector, Position = parent.Position, Credits = rules.NewShipStartCredits,
        });
    }
}
