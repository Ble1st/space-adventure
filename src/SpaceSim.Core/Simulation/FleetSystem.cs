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
    public static string KitKeyFor(Universe u, Ship ship) => ShipPurchase.KitKeyOf(u, ClassOf(u, ship));

    static ShipClass? ClassOf(Universe u, Ship ship) =>
        ship.HasModel && u.ShipModels.TryGetValue(ship.ModelKey, out var model) ? u.ShipClasses.GetValueOrDefault(model.ClassKey) : null;

    /// <summary>
    /// Schiffsbau: Ein neues Schiff entsteht nur, wenn eine erreichbare Werft einen Bausatz auf Lager hat. Seit Phase 5c bevorzugt die
    /// KI dieselbe Kaufregel wie der Spieler (<see cref="ShipPurchase"/>): Hat eine Werft Bausatz und alle Module des Fits, verbraucht sie
    /// beides und bekommt Marktpreis × (1 + Montagegebühr); sonst wie vor 5c nur den Bausatz und die Pauschale <see cref="EconomyRules.NewShipCost"/>
    /// (Regel <see cref="EconomyRules.FleetBuysModules"/>, false = immer nur der Bausatz). Werften sind Stationen, deren Typ diese Klasse
    /// verkauft (<see cref="StationType.ShipDealer"/>); Welten ganz ohne Schiffshändler nehmen jede Station mit dem Material.
    /// Das neue Schiff übernimmt Modell, Slotliste und Fit des Elternschiffs und startet an der Werft.
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
        var cls = ClassOf(u, parent);
        var kit = u.FindGood(ShipPurchase.KitKeyOf(u, cls));
        if (kit is null) return;
        // Phase 5c: bevorzugt dieselbe Regel wie der Spielerkauf (Bausatz + Module des Fits aus dem Werftlager, Marktpreis × (1 + Montage));
        // hat keine erreichbare Werft alle Module, baut sie wie vor 5c nur aus dem Bausatz zur Pauschale — Modulengpässe sollen das
        // Flottenwachstum nicht blockieren (Balance-Befund 5c: unterkapitalisierte Modulfabriken liefern auf manchen Seeds lange nichts)
        bool dealers = u.StationTypes.Values.Any(t => t.ShipDealer is not null);
        bool Yard(Station s) => (!dealers || cls is null || ShipPurchase.Sells(u, s, cls)) && u.IsReachable(parent.Sector, s.Sector);
        Station? Nearest(IReadOnlyList<MaterialNeed> needs) => u.Stations.Values
            .Where(s => ShipPurchase.Missing(s, needs).Count == 0 && Yard(s))
            .OrderBy(s => Travel.Estimate(u, parent.Sector, parent.Position, s.Sector, s.Position, parent)).ThenBy(s => s.Id.Value).FirstOrDefault();

        IReadOnlyList<MaterialNeed> kitOnly = new[] { new MaterialNeed(kit.Id, 1f) };
        IReadOnlyList<MaterialNeed> needs = kitOnly;
        Station? yard = null;
        bool withModules = false;
        if (rules.FleetBuysModules && cls is not null && ShipPurchase.Materials(u, cls, parent.Fit) is { } full && Nearest(full) is { } y)
        { needs = full; yard = y; withModules = true; }
        yard ??= Nearest(kitOnly);
        if (yard is null) return;   // keine Werft mit Bausatz: kein neues Schiff

        float price = withModules ? ShipPurchase.Price(u, yard, needs, ShipPurchase.AssemblyOf(u, yard)) : rules.NewShipCost;
        ShipPurchase.Consume(yard, needs);
        yard.Credits += price;
        parent.Credits -= price + rules.NewShipStartCredits;
        var id = u.NewShipId();
        // Neukauf übernimmt Modell, Slotliste und Fit des Elternschiffs (Phase 3)
        Ship child;
        if (parent.HasModel && cls is not null)
            child = ShipPurchase.Create(u, id, parent.ModelKey, parent.Slots, parent.Fit, role, CapabilityDefaults.ControllerFor(role), parent.Faction, yard, rules.NewShipStartCredits);
        else
        {
            child = new Ship
            {
                Id = id, Role = role, Controller = CapabilityDefaults.ControllerFor(role),
                Faction = parent.Faction, Sector = yard.Sector, Position = yard.DockPoint(id), Credits = rules.NewShipStartCredits,
                CargoCapacity = parent.CargoCapacity,
            };
            child.Capabilities = CapabilityDefaults.ForRole(role);
        }
        u.AddShip(child);
        u.Events.Publish(new FleetShipBuilt(child.Id, yard.Id, withModules, price));
    }
}
