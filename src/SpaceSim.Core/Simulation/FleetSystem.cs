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

    /// <summary>
    /// Phase 22: Abgänge der wirtschaftlichen KI-Schiffe nur aus zwei der drei Gründe (zerstört regelt der Überfall): (b) pleite
    /// (<see cref="Departures.ShipBankrupt"/>: kein Geld, keine Fracht, kein Auftrag, seit der Frist keine Besserung) und (c) länger nicht rentabel
    /// (<see cref="Departures.ShipUnprofitable"/>: Schonfrist vorbei, Mittelwert des Geldflusses über das lange Fenster unter null; die Uhr ruht hinter einer
    /// physischen Sperre). Die Leerlauf-Ausmusterung (Phase 15/20: <see cref="EconomyRules.IdleRetireTicks"/> und volle Aufgabe) ist durch die Messung ersetzt.
    /// Beim Ausmustern geht das Betriebsgeld an den Besitzer (eigener Akteur mit Kasse), bei freien Einzelschiffen verlässt es mit dem Kapitän die Welt; die
    /// Flotte kauft im selben Kartenteil eine Weile kein Schiff dieser Aufgabe (<see cref="EconomyRules.ExpandAfterRetireTicks"/>).
    /// </summary>
    static void Abandon(Universe u)
    {
        foreach (var ship in u.ShipsById())
        {
            if (!ship.IsEconomyAi || !u.Ships.ContainsKey(ship.Id)) continue;
            if (Departures.ShipBankrupt(u, ship)) { Departures.RemoveShip(u, ship, DepartureReason.Bankrupt, "broke"); continue; }
            if (!Departures.ShipUnprofitable(u, ship)) continue;
            float credits = MathF.Max(0f, ship.Credits);
            bool toOwner = ship.Owner != u.FreeActor && u.Actors.TryGetValue(ship.Owner, out var owner) && owner.Id != u.PlayerActor;
            if (toOwner) u.Actors[ship.Owner].Treasury.Credits += credits;
            ship.Credits = 0f;
            // Phase 20: Kaufsperre für diese Aufgabe in diesem Kartenteil (kein Neukauf für eine Aufgabe, für die gerade ein Schiff ohne Ertrag ging)
            if (ShipJobs.ForRole(ship.Role) is { } job) u.IdleRetiredTick[(job.Key, FleetCapacity.PartOf(u, ship))] = u.Tick;
            u.Events.Publish(new ShipRetired(ship.Id, ship.Owner, credits, toOwner));
            Departures.RemoveShip(u, ship, DepartureReason.Unprofitable, "unprofitable");
        }
    }

    /// <summary>
    /// Phase 20: Darf die Flotte im Kartenteil ein weiteres Schiff dieser Aufgabe kaufen? Die Aufgabe hat dort Platz (<see cref="FleetCapacity"/>), es wurde
    /// dort nicht kürzlich ein Schiff dieser Aufgabe wegen Leerlauf ausgemustert (<see cref="EconomyRules.ExpandAfterRetireTicks"/>), und kein arbeitsfähiges
    /// Schiff der Aufgabe liegt dort seit <see cref="EconomyRules.ExpandIdleTicks"/> untätig (Betriebsgeld mindestens das Startkapital eines Neukaufs: Was es
    /// nicht findet, fände ein neues Schiff auch nicht). Vorher wurde gekauft und wegen Leerlaufs ausgemustert im Kreis.
    /// </summary>
    public static bool CanGrow(Universe u, IShipJob job, int partKey)
    {
        var r = u.Rules;
        if (FleetCapacity.Used(u, job, partKey) >= FleetCapacity.Limit(u, job, partKey)) return false;
        if (r.ExpandAfterRetireTicks > 0 && u.IdleRetiredTick.TryGetValue((job.Key, partKey), out var at) && u.Tick - at < r.ExpandAfterRetireTicks) return false;
        if (r.ExpandIdleTicks > 0)
            foreach (var s in u.ShipsById())
                if (s.Role == job.Role && s.Controller == Controller.Ai && s.Transit is null && s.Order is null && s.IdleSince >= 0
                    && u.Tick - s.IdleSince >= r.ExpandIdleTicks && s.Credits >= r.NewShipStartCredits && FleetCapacity.PartOf(u, s) == partKey)
                    return false;
        return true;
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
    public static ModelChoice? ChooseModel(Universe u, IShipJob job, Ship buyer, float used, float limit) =>
        ChooseModel(u, job, buyer.Sector, buyer.Position, buyer.Credits, buyer, GateAccess.ProfileOf(u, buyer), used, limit);

    /// <summary>Phase 20: Kaufwahl für einen Käufer an einem Ort mit Geld (auch ohne Käuferschiff, z. B. eine Fraktionskasse).</summary>
    public static ModelChoice? ChooseModel(Universe u, IShipJob job, SectorId sector, Vec2 pos, float credits, Ship? traveler, MobilityProfile profile, float used, float limit)
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
                .Where(s => (!dealers || ShipPurchase.SellsModel(u, s, model)) && Research.Available(u, s, UnlockKind.Model, model.Key) && u.IsReachable(sector, s.Sector, profile) && ShipPurchase.Missing(s, needs).Count == 0)
                .OrderBy(s => Travel.Estimate(u, sector, pos, s.Sector, s.Position, traveler)).ThenBy(s => s.Id.Value).FirstOrDefault();
            if (yard is null) continue;
            float price = ShipPurchase.Price(u, yard, needs, ShipPurchase.AssemblyOf(u, yard));
            if (price <= 0f || price + rules.NewShipStartCredits > credits) continue;
            float value = job.FleetValue(stats, rules);
            foreach (var other in jobs)
                if (other != job && (stats.Capabilities & other.Needs) == other.Needs) value += rules.FleetFlexWeight * other.FleetValue(stats, rules);
            float score = value / price;
            if (best is null || score > best.Score) best = new ModelChoice(model, cls, yard, needs, price, units, score);
        }
        return best;
    }

    /// <summary>
    /// Phase 20: Die Fraktion springt ein, wenn die Flotte eines Kartenteils zusammengebrochen ist. Bis Phase 19 kaufte nur ein reiches Schiff derselben Aufgabe
    /// nach; waren alle Händler eines Teils arm oder ausgestorben (z. B. nach einer Torsperre: Lauf F 3,7/14, Lauf G ein Teil mit Werft, aber ohne Schiffe), kam
    /// nie wieder eines. Liegt die belegte Kapazität eines wachsenden Teils unter <see cref="EconomyRules.FleetTreasuryFloorShare"/> seiner Grenze und hat dort kein
    /// Schiff der Aufgabe das Geld für einen Kauf, kauft die Kasse der Fraktion, der dort die meisten Sektoren gehören, ein Schiff über die gemeinsame Kaufwahl
    /// (<see cref="ChooseModel(Universe, IShipJob, SectorId, Vec2, float, Ship?, MobilityProfile, float, float)"/>) als Eigenbetrieb: Es gehört dem Fraktionsakteur,
    /// arbeitet aus eigener Betriebskasse (<see cref="EconomyRules.NewShipStartCredits"/>), die Kasse behält <see cref="EconomyRules.SubsidyReserve"/>.
    /// </summary>
    static void BuyFromTreasury(Universe u, IShipJob job, Func<int, bool> grows)
    {
        var rules = u.Rules;
        if (rules.FleetTreasuryFloorShare <= 0f || !rules.FleetChoosesModels) return;
        var parts = MapParts.Of(u);
        foreach (var part in parts.Parts)
        {
            int key = FleetCapacity.Divided(u) ? part[0].Value : 0;
            if (!grows(key)) continue;
            float used = FleetCapacity.Used(u, job, key), limit = FleetCapacity.Limit(u, job, key);
            if (used >= limit * rules.FleetTreasuryFloorShare) continue;
            // Besitzerfraktion mit den meisten Sektoren im Teil (Gleichstand: kleinste Id)
            var faction = part.Select(id => u.Sectors[id].Owner).Where(f => u.Factions.ContainsKey(f) && u.Actors.ContainsKey(f))
                .GroupBy(f => f.Value).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new FactionId(g.Key)).FirstOrDefault();
            if (faction.Value == 0 || !u.Actors.TryGetValue(faction, out var actor) || actor.Kind == ActorKind.Outlaw) continue;
            var home = u.Sectors[part.FirstOrDefault(id => u.Sectors[id].Owner == faction)];
            // Phase 22: freiwillige Ausgabe — die Betriebsreserve der Fraktion bleibt (FactionViability)
            float budget = MathF.Min(actor.Treasury.Credits - rules.SubsidyReserve, FactionViability.Spendable(u, actor));
            if (budget <= rules.NewShipStartCredits) continue;
            if (ChooseModel(u, job, home.Id, default, budget, null, MobilityProfile.For(actor.Id), used, limit) is not { } choice) continue;
            var q = ShipQualityRoll.Roll(u, choice.Class, choice.Class.Slots, choice.Model.Fit, choice.Yard, choice.Materials, Fits(u, job, choice.Class, used, limit));
            ShipPurchase.Consume(choice.Yard, q.Materials ?? choice.Materials);
            choice.Yard.Credits += choice.Price;
            float cost = choice.Price + rules.NewShipStartCredits;
            actor.Treasury.Credits -= cost;
            u.Events.Publish(new TreasuryUpkeep(actor.Id, "fleet", cost, 0f));
            u.Events.Publish(new InvestorChosen("ship", InvestorSource.Treasury, actor.Id, ActorRules.ClassOf(u, ActorRules.Index(u), actor.Id), cost, 1));
            var bought = ShipPurchase.Create(u, u.NewShipId(), choice.Model.Key, q.Slots, q.Fit, job.Role, Controller.Ai, actor.Id, choice.Yard, rules.NewShipStartCredits, q.Grade);
            bought.JobSince = u.Tick;
            u.AddShip(bought);
            u.Events.Publish(new FleetShipBuilt(bought.Id, choice.Yard.Id, true, choice.Price));
            u.Events.Publish(new FleetModelChosen(bought.Id, choice.Model.Key, job.Role, choice.Price));
            u.Events.Publish(new ShipQualityRolled(bought.Id, q.Rolled, q.Grade));
            return;   // höchstens ein Kauf je Schritt
        }
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
        // Phase 20: Bauschiffe sind Bestand der Fraktionen (FactionBuilderSystem: Mindestbestand, Nachkauf für blockierte Baustellen) — kein Zukauf aus
        // Bauschiff-Kassen mehr (sonst kaufte ein reiches Bauschiff ein weiteres, das die Fraktion als überzählig wieder ausmusterte)
        if (job.Role == ShipRole.Builder && rules.MinBuildersPerFaction > 0) return;
        // Phase 20: Wachstum je Kartenteil (ohne Teilung ein Teil); billige Vorprüfung, bevor die Kandidatenliste gebaut wird
        var grow = new Dictionary<int, bool>();
        bool Grows(int key) { if (!grow.TryGetValue(key, out var ok)) grow[key] = ok = CanGrow(u, job, key); return ok; }
        if (!FleetCapacity.Divided(u) && !Grows(0)) return;

        var role = job.Role;
        // Phase 9: Zukauf — statt des reichsten Schiffs zieht eine gewichtete Ziehung den Käufer (Geld über der Schwelle × Appetit der
        // Größenklasse seines Besitzers × Gier, Investors)
        var candidates = Investors.ForShip(u, role, rules.FleetExpandCredits).Where(c => c.Ship is { } cs && Grows(FleetCapacity.PartOf(u, cs))).ToList();
        // Phase 20: Teile ohne zahlungskräftiges Schiff der Aufgabe — dort springt bei zusammengebrochener Flotte die Fraktionskasse ein
        var withBuyers = candidates.Select(c => FleetCapacity.PartOf(u, c.Ship!)).ToHashSet();
        BuyFromTreasury(u, job, k => !withBuyers.Contains(k) && Grows(k));
        var pick = Investors.Draw(u, candidates);
        if (pick?.Ship is not { } parent) return;
        float before = parent.Credits;
        var (used, limit) = FleetCapacity.For(u, job, parent);

        if (rules.FleetChoosesModels && ChooseModel(u, job, parent, used, limit) is { } choice)
        {
            // Phase 7: Güteklasse würfeln; bessere Module kommen zusätzlich aus dem Werftlager (Mehrkosten trägt die Werft)
            var q = ShipQualityRoll.Roll(u, choice.Class, choice.Class.Slots, choice.Model.Fit, choice.Yard, choice.Materials, Fits(u, job, choice.Class, used, limit));
            ShipPurchase.Consume(choice.Yard, q.Materials ?? choice.Materials);
            choice.Yard.Credits += choice.Price;
            parent.Credits -= choice.Price + rules.NewShipStartCredits;
            Profitability.Transfer(parent.Profit, -(choice.Price + rules.NewShipStartCredits));   // Phase 22: Investition, kein Geschäftsverlust
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
        Profitability.Transfer(parent.Profit, -(price + rules.NewShipStartCredits));   // Phase 22: Investition, kein Geschäftsverlust
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
