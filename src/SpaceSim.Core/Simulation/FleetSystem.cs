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
        {
            if (ship.Transit is not null || ship.Order is not null || ship.IdleSince < 0) continue;
            long idle = u.Tick - ship.IdleSince;
            if (idle > u.Rules.ShipBankruptTicks && ship.Credits < 50f) { u.RemoveShip(ship.Id, "bankrupt"); continue; }
            // Phase 15: Auch ein Schiff mit Geld, das sehr lange nichts findet (z. B. ein Mehrzweckschiff, dessen Aufgabe nichts einbringt und dessen andere
            // Aufgabe voll ist), wird abgebaut statt ewig zu liegen; sein Platz in der Kapazität wird frei, die Flotte kauft bei Bedarf nach.
            // Sein Betriebsgeld geht an den Besitzer (eigener Akteur mit Kasse), bei freien Einzelschiffen verlässt es mit dem Kapitän die Welt.
            if (u.Rules.IdleRetireTicks > 0 && idle > u.Rules.IdleRetireTicks && ship.Role is ShipRole.Trader or ShipRole.Miner && ship.Cargo.Values.Sum() < 0.001f)
            {
                float credits = MathF.Max(0f, ship.Credits);
                bool toOwner = ship.Owner != u.FreeActor && u.Actors.TryGetValue(ship.Owner, out var owner) && owner.Id != u.PlayerActor;
                if (toOwner) u.Actors[ship.Owner].Treasury.Credits += credits;
                ship.Credits = 0f;
                u.Events.Publish(new ShipRetired(ship.Id, ship.Owner, credits, toOwner));
                u.RemoveShip(ship.Id, "retired");
            }
        }
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
                .Where(s => (!dealers || ShipPurchase.SellsModel(u, s, model)) && Research.Available(u, s, UnlockKind.Model, model.Key) && u.IsReachable(buyer.Sector, s.Sector, GateAccess.ProfileOf(u, buyer)) && ShipPurchase.Missing(s, needs).Count == 0)
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
    /// Phase 15: Hält eine gewürfelte Ausstattung (bessere Güteklasse, z. B. Bonus-Frachtslot) die Kapazitätsgrenze der Aufgabe samt Überhang ein? Vorher
    /// prüfte die Kaufwahl nur das Werksmodell; ein Meisterwerk-Frachter mit zusätzlichem Frachtmodul hob den Handel auf 14,8/14.
    /// </summary>
    static Func<List<ClassSlot>, List<string?>, bool> Fits(Universe u, IShipJob job, ShipClass cls, float used, float limit) => (slots, fit) =>
    {
        var stats = ShipStats.Compute(cls, fit.Select(k => k is not null && u.Modules.TryGetValue(k, out var m) ? m : null).ToList());
        return used + job.Units(stats, stats.Cargo, u.Rules) <= limit + u.Rules.FleetCapacityOvershoot + 0.0001f;
    };

    /// <summary>
    /// Phase 9: Nach dem Kauf steigt ein freies Einzelschiff zum eigenen Akteur auf (lazy, POLITIK.md „Einzelschiffe träge anlegen“); das neue
    /// Schiff gehört dann diesem Akteur (sonst dem Besitzer des Käufers). Meldet die Ziehung als <see cref="InvestorChosen"/>.
    /// </summary>
    static ActorId Bought(Universe u, Ship parent, Investor pick, int candidates, float paid)
    {
        u.Events.Publish(new InvestorChosen("ship", pick.Source, pick.Holder, pick.Class, paid, candidates));
        return ActorRules.Promote(u, parent);
    }

    /// <summary>
    /// Schiffsbau für eine Aufgabe: Liegt die belegte Kapazität unter der Grenze, kauft ein Schiff dieser Aufgabe (ab
    /// <see cref="EconomyRules.FleetExpandCredits"/>, Phase 9: gewichtete Ziehung statt des reichsten) ein neues. Phase 6: zuerst die Kaufwahl <see cref="ChooseModel"/>; findet sie nichts
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
        // Phase 9: Zukauf — statt des reichsten Schiffs zieht eine gewichtete Ziehung den Käufer (Geld über der Schwelle × Appetit der
        // Größenklasse seines Besitzers × Gier, Investors)
        var candidates = Investors.ForShip(u, role, rules.FleetExpandCredits);
        var pick = Investors.Draw(u, candidates);
        if (pick?.Ship is not { } parent) return;
        float before = parent.Credits;

        if (rules.FleetChoosesModels && ChooseModel(u, job, parent, used, limit) is { } choice)
        {
            // Phase 7: Güteklasse würfeln; bessere Module kommen zusätzlich aus dem Werftlager (Mehrkosten trägt die Werft)
            var q = ShipQualityRoll.Roll(u, choice.Class, choice.Class.Slots, choice.Model.Fit, choice.Yard, choice.Materials, Fits(u, job, choice.Class, used, limit));
            ShipPurchase.Consume(choice.Yard, q.Materials ?? choice.Materials);
            choice.Yard.Credits += choice.Price;
            parent.Credits -= choice.Price + rules.NewShipStartCredits;
            var newId = u.NewShipId();
            var owner = Bought(u, parent, pick, candidates.Count, before - parent.Credits);
            var bought = ShipPurchase.Create(u, newId, choice.Model.Key, q.Slots, q.Fit, role, Controller.Ai, owner, choice.Yard, rules.NewShipStartCredits, q.Grade);
            bought.JobSince = u.Tick;
            u.AddShip(bought);
            u.Events.Publish(new FleetShipBuilt(bought.Id, choice.Yard.Id, true, choice.Price));
            u.Events.Publish(new FleetModelChosen(bought.Id, choice.Model.Key, role, choice.Price));
            u.Events.Publish(new ShipQualityRolled(bought.Id, q.Rolled, q.Grade));
            return;
        }

        // Regel vor Phase 6: Kopie des Elternschiffs. Bausatzstufe nach dessen Klasse (Phase 5a), sonst die Regel.
        // Phase 10 (Fehlerkorrektur): auch die Kopie hält die Kapazitätsgrenze samt Überhang ein (vorher konnte ein kopierter großer Frachter
        // die Grenze um mehr als FleetCapacityOvershoot überschreiten)
        if (used + ShipJobs.UnitsOf(u, parent, job) > limit + rules.FleetCapacityOvershoot) return;
        var cls = ClassOf(u, parent);
        var kit = u.FindGood(ShipPurchase.KitKeyOf(u, cls));
        if (kit is null) return;
        bool dealers = u.StationTypes.Values.Any(t => t.ShipDealer is not null);
        // Phase 13: das Modell des Elternschiffs muss an der Werft erforscht sein (sonst kauft die Kopie dort nicht)
        bool Yard(Station s) => (!dealers || cls is null || ShipPurchase.Sells(u, s, cls)) && (!parent.HasModel || Research.Available(u, s, UnlockKind.Model, parent.ModelKey))
            && u.IsReachable(parent.Sector, s.Sector, GateAccess.ProfileOf(u, parent));
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
        QualityOutcome? quality = parent.HasModel && cls is not null ? ShipQualityRoll.Roll(u, cls, baseSlots, baseFit, yard, needs, Fits(u, job, cls, used, limit)) : null;
        ShipPurchase.Consume(yard, quality?.Materials ?? needs);
        yard.Credits += price;
        parent.Credits -= price + rules.NewShipStartCredits;
        var id = u.NewShipId();
        var childOwner = Bought(u, parent, pick, candidates.Count, before - parent.Credits);
        // Neukauf übernimmt das Modell des Elternschiffs (Phase 3), seit Phase 7 mit eigener Güteklasse
        Ship child;
        if (quality is not null)
            child = ShipPurchase.Create(u, id, parent.ModelKey, quality.Slots, quality.Fit, role, CapabilityDefaults.ControllerFor(role), childOwner, yard, rules.NewShipStartCredits, quality.Grade);
        else
        {
            child = new Ship
            {
                Id = id, Role = role, Controller = CapabilityDefaults.ControllerFor(role),
                Owner = childOwner, Sector = yard.Sector, Position = yard.DockPoint(id), Credits = rules.NewShipStartCredits,
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
