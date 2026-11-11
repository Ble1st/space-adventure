using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Flottenentwicklung: Erfolgreiche Schiffe kaufen weitere Schiffe (bis zur Kapazitätsgrenze je Aufgabe),
/// dauerhaft untätige und mittellose Schiffe werden aufgegeben.
/// Phase 6: Die Grenzen <see cref="EconomyRules.MaxTraders"/>/<see cref="EconomyRules.MaxMiners"/> zählen Kapazität statt Stückzahl
/// (<see cref="ShipJobs.UsedUnits"/>; Einheitsmodelle zählen 1, ein großer Frachter mehrfach), und die KI wählt das Modell nach Aufgabe,
/// Geld und Werftlager (<see cref="ChooseModel"/>).
/// </summary>
public sealed class FleetSystem : ISimSystem
{
    readonly IReadOnlyList<IShipJob> _jobs;

    public FleetSystem(IReadOnlyList<IShipJob>? jobs = null) => _jobs = jobs ?? ShipJobs.Default;

    public void Step(Universe u, int dt)
    {
        Abandon(u);
        foreach (var job in _jobs) Expand(u, job);
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

    /// <summary>Ein möglicher Neukauf: Modell, Werft, Material, Preis, belegte Kapazität und Kaufwert je Credit.</summary>
    public sealed record ModelChoice(ShipModel Model, ShipClass Class, Station Yard, IReadOnlyList<MaterialNeed> Materials, float Price, float Units, float Score);

    /// <summary>
    /// Kaufwahl der KI (Phase 6, SCHIFFE.md Abschnitt 6 Punkt 4 „kauft das Modell, das die größte Lücke füllt“): Kandidaten sind alle Modelle,
    /// deren <see cref="ShipModel.Jobs"/> die Aufgabe nennen und deren Fit die nötige Fähigkeit hat. Ein Kandidat zählt nur, wenn eine erreichbare
    /// Station, die seine Klasse verkauft, Bausatz und alle Module auf Lager hat (dieselbe Regel wie der Spielerkauf, <see cref="ShipPurchase"/>),
    /// der Käufer Preis plus Startkapital des neuen Schiffs bezahlen kann und die Aufgabe danach höchstens um
    /// <see cref="EconomyRules.FleetCapacityOvershoot"/> über ihrer Grenze liegt. Gewählt wird der höchste Kaufwert je Credit:
    /// (Wert in der Aufgabe + <see cref="EconomyRules.FleetFlexWeight"/> × Wert der übrigen Aufgaben des Modells) ÷ Preis, bei Gleichstand der
    /// Modellschlüssel. Ein Generalist ist in keiner Aufgabe der beste, gewinnt aber, wenn seine Flexibilität den Abstand ausgleicht.
    /// Reine Funktion; null = kein Kandidat.
    /// </summary>
    public static ModelChoice? ChooseModel(Universe u, IShipJob job, Ship buyer, float used, float limit)
    {
        var rules = u.Rules;
        bool dealers = u.StationTypes.Values.Any(t => t.ShipDealer is not null);
        ModelChoice? best = null;
        foreach (var model in u.ShipModels.Values.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            var jobs = ShipJobs.Of(model);
            if (!jobs.Contains(job) || !u.ShipClasses.TryGetValue(model.ClassKey, out var cls)) continue;
            var stats = ShipStats.Compute(cls, model.Fit.Select(k => k is not null && u.Modules.TryGetValue(k, out var m) ? m : null).ToList());
            if ((stats.Capabilities & job.Needs) != job.Needs) continue;
            float units = job.Units(stats, stats.Cargo, rules);
            if (units <= 0f || used + units > limit + rules.FleetCapacityOvershoot) continue;
            if (ShipPurchase.Materials(u, cls, model.Fit) is not { } needs) continue;
            var yard = u.Stations.Values
                .Where(s => (!dealers || ShipPurchase.Sells(u, s, cls)) && u.IsReachable(buyer.Sector, s.Sector) && ShipPurchase.Missing(s, needs).Count == 0)
                .OrderBy(s => Travel.Estimate(u, buyer.Sector, buyer.Position, s.Sector, s.Position, buyer)).ThenBy(s => s.Id.Value).FirstOrDefault();
            if (yard is null) continue;
            float price = ShipPurchase.Price(u, yard, needs, ShipPurchase.AssemblyOf(u, yard));
            if (price <= 0f || price + rules.NewShipStartCredits > buyer.Credits) continue;
            float value = job.FleetValue(stats, rules);
            foreach (var other in jobs)
                if (other != job && (stats.Capabilities & other.Needs) == other.Needs) value += rules.FleetFlexWeight * other.FleetValue(stats, rules);
            float score = value / price;
            if (best is null || score > best.Score) best = new ModelChoice(model, cls, yard, needs, price, units, score);
        }
        return best;
    }

    /// <summary>
    /// Schiffsbau für eine Aufgabe: Liegt die belegte Kapazität unter der Grenze, kauft das reichste Schiff dieser Aufgabe (ab
    /// <see cref="EconomyRules.FleetExpandCredits"/>) ein neues. Phase 6: zuerst die Kaufwahl <see cref="ChooseModel"/>; findet sie nichts
    /// (z. B. Werft ohne passende Module, Content ohne <c>jobs</c>), gilt die Regel vor Phase 6 — eine Kopie des Elternschiffs, bevorzugt mit den
    /// Modulen aus dem Werftlager (Phase 5c), sonst nur aus dem Bausatz zur Pauschale <see cref="EconomyRules.NewShipCost"/>, damit
    /// Modulengpässe das Flottenwachstum nicht blockieren. Werften sind Stationen, deren Typ die Klasse verkauft; Welten ganz ohne
    /// Schiffshändler nehmen jede Station mit dem Material. Das neue Schiff startet an der Werft mit der Aufgabe, für die es gekauft wurde.
    /// </summary>
    static void Expand(Universe u, IShipJob job)
    {
        var rules = u.Rules;
        float limit = job.Limit(rules);
        float used = ShipJobs.UsedUnits(u, job);
        if (used >= limit) return;

        var role = job.Role;
        var parent = u.Ships.Values
            .Where(s => s.Role == role && s.Controller == Controller.Ai && s.Transit is null && s.Credits >= rules.FleetExpandCredits)
            .OrderByDescending(s => s.Credits).ThenBy(s => s.Id.Value).FirstOrDefault();
        if (parent is null) return;

        if (rules.FleetChoosesModels && ChooseModel(u, job, parent, used, limit) is { } choice)
        {
            // Phase 7: Güteklasse würfeln; bessere Module kommen zusätzlich aus dem Werftlager (Mehrkosten trägt die Werft)
            var q = ShipQualityRoll.Roll(u, choice.Class, choice.Class.Slots, choice.Model.Fit, choice.Yard, choice.Materials);
            ShipPurchase.Consume(choice.Yard, q.Materials ?? choice.Materials);
            choice.Yard.Credits += choice.Price;
            parent.Credits -= choice.Price + rules.NewShipStartCredits;
            var newId = u.NewShipId();
            var bought = ShipPurchase.Create(u, newId, choice.Model.Key, q.Slots, q.Fit, role, Controller.Ai, parent.Faction, choice.Yard, rules.NewShipStartCredits, q.Grade);
            bought.JobSince = u.Tick;
            u.AddShip(bought);
            u.Events.Publish(new FleetShipBuilt(bought.Id, choice.Yard.Id, true, choice.Price));
            u.Events.Publish(new FleetModelChosen(bought.Id, choice.Model.Key, role, choice.Price));
            u.Events.Publish(new ShipQualityRolled(bought.Id, q.Rolled, q.Grade));
            return;
        }

        // Regel vor Phase 6: Kopie des Elternschiffs. Bausatzstufe nach dessen Klasse (Phase 5a), sonst die Regel
        var cls = ClassOf(u, parent);
        var kit = u.FindGood(ShipPurchase.KitKeyOf(u, cls));
        if (kit is null) return;
        bool dealers = u.StationTypes.Values.Any(t => t.ShipDealer is not null);
        bool Yard(Station s) => (!dealers || cls is null || ShipPurchase.Sells(u, s, cls)) && u.IsReachable(parent.Sector, s.Sector);
        Station? Nearest(IReadOnlyList<MaterialNeed> needs) => u.Stations.Values
            .Where(s => ShipPurchase.Missing(s, needs).Count == 0 && Yard(s))
            .OrderBy(s => Travel.Estimate(u, parent.Sector, parent.Position, s.Sector, s.Position, parent)).ThenBy(s => s.Id.Value).FirstOrDefault();

        IReadOnlyList<MaterialNeed> kitOnly = new[] { new MaterialNeed(kit.Id, 1f) };
        IReadOnlyList<MaterialNeed> needs = kitOnly;
        Station? yard = null;
        bool withModules = false;
        // Phase 7: kopiert wird das Modell (Klassenslots, Werks-Fit), nicht die individuelle Ausstattung des Elternschiffs (Güteklasse, Upgrades)
        var parentModel = parent.HasModel ? u.ShipModels.GetValueOrDefault(parent.ModelKey) : null;
        IReadOnlyList<string?> baseFit = parentModel?.Fit ?? (IReadOnlyList<string?>)parent.Fit;
        IReadOnlyList<ClassSlot> baseSlots = cls?.Slots ?? (IReadOnlyList<ClassSlot>)parent.Slots;
        if (rules.FleetBuysModules && cls is not null && ShipPurchase.Materials(u, cls, baseFit) is { } full && Nearest(full) is { } y)
        { needs = full; yard = y; withModules = true; }
        yard ??= Nearest(kitOnly);
        if (yard is null) return;   // keine Werft mit Bausatz: kein neues Schiff

        float price = withModules ? ShipPurchase.Price(u, yard, needs, ShipPurchase.AssemblyOf(u, yard)) : rules.NewShipCost;
        // Phase 7: Güteklasse würfeln (Mehrkosten aus dem Werftlager, sonst Rückfall auf die nächste Stufe)
        QualityOutcome? quality = parent.HasModel && cls is not null ? ShipQualityRoll.Roll(u, cls, baseSlots, baseFit, yard, needs) : null;
        ShipPurchase.Consume(yard, quality?.Materials ?? needs);
        yard.Credits += price;
        parent.Credits -= price + rules.NewShipStartCredits;
        var id = u.NewShipId();
        // Neukauf übernimmt das Modell des Elternschiffs (Phase 3), seit Phase 7 mit eigener Güteklasse
        Ship child;
        if (quality is not null)
            child = ShipPurchase.Create(u, id, parent.ModelKey, quality.Slots, quality.Fit, role, CapabilityDefaults.ControllerFor(role), parent.Faction, yard, rules.NewShipStartCredits, quality.Grade);
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
        child.JobSince = u.Tick;
        u.AddShip(child);
        u.Events.Publish(new FleetShipBuilt(child.Id, yard.Id, withModules, price));
        if (quality is not null) u.Events.Publish(new ShipQualityRolled(child.Id, quality.Rolled, quality.Grade));
    }
}
