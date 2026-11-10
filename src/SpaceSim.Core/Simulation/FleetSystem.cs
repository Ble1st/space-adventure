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
    /// Bausatz-Warenschlüssel für ein Schiff wie <paramref name="ship"/> (Phase 5a): <see cref="ShipClass.Kit"/> der Klasse seines Modells,
    /// sonst (kein Modell, Klasse ohne Bausatz) die Regel <see cref="EconomyRules.ShipKitGoodKey"/>.
    /// </summary>
    public static string KitKeyFor(Universe u, Ship ship) =>
        ship.HasModel && u.ShipModels.TryGetValue(ship.ModelKey, out var model) && u.ShipClasses.TryGetValue(model.ClassKey, out var cls)
            && cls.Kit is { Length: > 0 } kit ? kit : u.Rules.ShipKitGoodKey;

    /// <summary>
    /// Schiffsbau: Ein neues Schiff entsteht nur, wenn eine erreichbare Werft einen Schiffsbausatz der passenden Stufe auf Lager hat.
    /// Der Bausatz wird verbraucht, die Werft erhält den Schiffspreis; das neue Schiff startet an der Werft. Damit hängt das Flottenwachstum
    /// an der ganzen Kette Erz → Veredelung → Komponenten → Bausatz; die Bausatzstufe kommt aus der Schiffsklasse (<see cref="KitKeyFor"/>).
    /// </summary>
    static void Expand(Universe u, ShipRole role, int max)
    {
        var rules = u.Rules;
        if (u.Ships.Values.Count(s => s.Role == role) >= max) return;

        var parent = u.Ships.Values
            .Where(s => s.Role == role && s.Transit is null && s.Credits >= rules.FleetExpandCredits)
            .OrderByDescending(s => s.Credits).ThenBy(s => s.Id.Value).FirstOrDefault();
        if (parent is null) return;
        // Phase 5a: Bausatzstufe nach der Schiffsklasse des neuen Schiffs (= Klasse des Elternschiffs), sonst die Regel
        var kit = u.FindGood(KitKeyFor(u, parent));
        if (kit is null) return;

        var yard = u.Stations.Values
            .Where(s => s.Market.TryGetValue(kit.Id, out var e) && e.Amount >= 1f && u.IsReachable(parent.Sector, s.Sector))
            .OrderBy(s => Travel.Estimate(u, parent.Sector, parent.Position, s.Sector, s.Position, parent)).ThenBy(s => s.Id.Value).FirstOrDefault();
        if (yard is null) return;   // keine Werft mit Bausatz: kein neues Schiff

        yard.Market[kit.Id].Amount -= 1f;
        yard.Credits += rules.NewShipCost;
        parent.Credits -= rules.NewShipCost + rules.NewShipStartCredits;
        var id = u.NewShipId();
        // Neukauf übernimmt Modell, Slotliste und Fit des Elternschiffs (Phase 3)
        var child = new Ship
        {
            Id = id, Role = role, Controller = CapabilityDefaults.ControllerFor(role),
            Faction = parent.Faction, Sector = yard.Sector, Position = yard.DockPoint(id), Credits = rules.NewShipStartCredits,
        };
        if (parent.HasModel)
        {
            child.ModelKey = parent.ModelKey;
            child.Slots = parent.Slots.Select(s => new ClassSlot { Type = s.Type, Size = s.Size, Facing = s.Facing, Arc = s.Arc }).ToList();
            child.Fit = new List<string?>(parent.Fit);
            if (child.RecomputeStats(u))
            {
                child.CargoCapacity = child.Stats.Cargo;
                child.FillFuel();   // Phase 4: Neuschiff startet mit vollem Tank (erster zulässiger Treibstoff)
            }
            else { child.ModelKey = ""; child.Slots.Clear(); child.Fit.Clear(); child.CargoCapacity = parent.CargoCapacity; child.Capabilities = CapabilityDefaults.ForRole(role); }
        }
        else
        {
            child.CargoCapacity = parent.CargoCapacity;
            child.Capabilities = CapabilityDefaults.ForRole(role);
        }
        u.AddShip(child);
    }
}
