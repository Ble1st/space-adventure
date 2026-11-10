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
        foreach (var ship in u.Ships.Values.Where(s => s.IsEconomyAi).ToList())
            if (ship.Transit is null && ship.Order is null && ship.IdleSince >= 0
                && u.Tick - ship.IdleSince > u.Rules.ShipBankruptTicks && ship.Credits < 50f)
                u.RemoveShip(ship.Id, "bankrupt");
    }

    /// <summary>
    /// Schiffsbau: Ein neues Schiff entsteht nur, wenn eine erreichbare Werft einen Schiffsbausatz (Regel <c>ShipKitGoodKey</c>) auf Lager hat.
    /// Der Bausatz wird verbraucht, die Werft erhält den Schiffspreis; das neue Schiff startet an der Werft. Damit hängt das Flottenwachstum
    /// an der ganzen Kette Erz → Veredelung → Komponenten → Bausatz. Spätere Schiffsklassen setzen hier an (Bausatz je Klasse).
    /// </summary>
    static void Expand(Universe u, ShipRole role, int max)
    {
        var rules = u.Rules;
        if (u.Ships.Values.Count(s => s.Role == role) >= max) return;
        var kit = u.FindGood(rules.ShipKitGoodKey);
        if (kit is null) return;

        var parent = u.Ships.Values
            .Where(s => s.Role == role && s.Transit is null && s.Credits >= rules.FleetExpandCredits)
            .OrderByDescending(s => s.Credits).ThenBy(s => s.Id.Value).FirstOrDefault();
        if (parent is null) return;

        var yard = u.Stations.Values
            .Where(s => s.Market.TryGetValue(kit.Id, out var e) && e.Amount >= 1f && u.IsReachable(parent.Sector, s.Sector))
            .OrderBy(s => Travel.Estimate(u, parent.Sector, parent.Position, s.Sector, s.Position)).ThenBy(s => s.Id.Value).FirstOrDefault();
        if (yard is null) return;   // keine Werft mit Bausatz: kein neues Schiff

        yard.Market[kit.Id].Amount -= 1f;
        yard.Credits += rules.NewShipCost;
        parent.Credits -= rules.NewShipCost + rules.NewShipStartCredits;
        var id = u.NewShipId();
        u.AddShip(new Ship
        {
            Id = id, Role = role, Capabilities = CapabilityDefaults.ForRole(role), Controller = CapabilityDefaults.ControllerFor(role),
            Faction = parent.Faction, CargoCapacity = parent.CargoCapacity,
            Sector = yard.Sector, Position = yard.DockPoint(id), Credits = rules.NewShipStartCredits,
        });
    }
}
