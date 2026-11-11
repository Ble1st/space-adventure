using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Abgeleitete Kritikalität (Phase 8, STATIONEN.md „Essential aufgedröselt“): Eine Station ist <c>Critical</c>, wenn sie für eine ihrer
/// Waren die einzige erreichbare Herstellerin ist. Wird beim Prüfen berechnet und nicht gespeichert. Kritische Stationen werden bei Pleite
/// nicht aufgegeben; Baustellen kritischer Typen lösen den Notbau früher aus.
/// </summary>
public static class StationCriticality
{
    /// <summary>Gibt es außer <paramref name="exclude"/> eine fertige, von <paramref name="from"/> erreichbare Station, die die Ware herstellt?</summary>
    public static bool OtherProducerReachable(Universe u, GoodId good, SectorId from, StationId? exclude)
    {
        foreach (var s in u.Stations.Values)
        {
            if (s.Id == exclude || s.IsBuildSite) continue;
            if (!s.Production.Any(r => r.Outputs.ContainsKey(good))) continue;
            if (u.IsReachable(from, s.Sector)) return true;
        }
        return false;
    }

    /// <summary>Ist diese (fertige) Station die letzte erreichbare Herstellerin einer ihrer Waren?</summary>
    public static bool IsCritical(Universe u, Station st)
    {
        if (st.IsBuildSite) return false;
        foreach (var g in st.Production.SelectMany(r => r.Outputs.Keys).Distinct().OrderBy(g => g.Value))
            if (!OtherProducerReachable(u, g, st.Sector, st.Id)) return true;
        return false;
    }

    /// <summary>
    /// Ist eine Baustelle kritisch? Ja, wenn ihr Typ eine Ware herstellt, die von dort aus keine fertige Station mehr liefert (die Kette ist
    /// gerissen), oder eine Ware einer kritischen Kette (<see cref="EconomyRules.CriticalGoods"/>: Stahl, Plattierung, Werft).
    /// </summary>
    public static bool IsCriticalSite(Universe u, Station site)
    {
        if (site.Site is not { } bs || !u.StationTypes.TryGetValue(bs.TypeKey, out var type)) return false;
        foreach (var g in type.Recipes.SelectMany(r => r.Outputs.Keys).Distinct().OrderBy(g => g.Value))
        {
            if (u.Rules.CriticalGoods.Contains(u.Goods[g].Key)) return true;
            if (!OtherProducerReachable(u, g, site.Sector, site.Id)) return true;
        }
        return false;
    }
}

/// <summary>
/// Regeln des Stationsbaus über Baustellen (Phase 8, SCHIFFE.md Abschnitt 10). Reine Funktionen bzw. kleine Zustandsänderungen am Universum,
/// gemeinsam für <see cref="StationLifecycleSystem"/> (entscheidet, ob gebaut wird), <see cref="ConstructionSystem"/> (wie gebaut wird),
/// Handel (Lieferung an die Baustelle) und Tests.
/// </summary>
public static class ConstructionRules
{
    /// <summary>Phase 19: so oft (Ticks) prüft eine blockierte Baustelle ohne erreichbares Bauschiff, ob ihre Fraktion eins kaufen kann.</summary>
    public const int CommissionInterval = 50;

    /// <summary>
    /// Phase 19: Bauschiff nachkaufen. Bauschiffe verdienen nur die Baugebühr und werden nach der ersten Bauwelle als mittellos aufgegeben
    /// (<see cref="FleetSystem"/>); der Flottenkauf der Aufgabe „build“ braucht aber ein Bauschiff als Käufer — ohne eines kam nie wieder eines in die Welt
    /// (im 200000-Tick-Lauf 0 Bauschiffe am Ende), und jede spätere Baustelle hing am Notbau. Jetzt kauft die Kasse der Fraktion, der die Baustelle gehört,
    /// ein Modell mit der Aufgabe „build“ über die gemeinsame Kaufregel (<see cref="ShipPurchase"/>: Bausatz und Module aus dem Lager einer erreichbaren
    /// Werft, Preis an die Werft), solange ihr danach die Rücklage <see cref="EconomyRules.SubsidyReserve"/> bleibt; das Schiff gehört dem Fraktionsakteur und
    /// bekommt <see cref="EconomyRules.NewShipStartCredits"/> als Betriebsgeld. Regel <see cref="EconomyRules.CommissionBuilders"/>. null = nicht möglich.
    /// </summary>
    public static Ship? CommissionBuilder(Universe u, Station site)
    {
        var r = u.Rules;
        if (!r.CommissionBuilders || u.FactionOf(site) is not { } f || !u.Actors.TryGetValue(f, out var actor)) return null;
        foreach (var model in u.ShipModels.Values.Where(m => m.Jobs.Contains("build")).OrderBy(m => m.Key, StringComparer.Ordinal).ToList())
        {
            if (!u.ShipClasses.TryGetValue(model.ClassKey, out var cls) || ShipPurchase.Materials(u, model) is not { } needs) continue;
            foreach (var yard in u.StationsById().Where(st => !st.IsBuildSite && ShipPurchase.SellsModel(u, st, model)).ToList())
            {
                if (!u.IsReachable(yard.Sector, site.Sector) || ShipPurchase.Missing(yard, needs).Count > 0 || !Research.Available(u, yard, UnlockKind.Model, model.Key)) continue;
                float price = ShipPurchase.Price(u, yard, needs, ShipPurchase.AssemblyOf(u, yard));
                float cost = price + r.NewShipStartCredits;
                if (actor.Treasury.Credits < cost + r.SubsidyReserve) return null;
                ShipPurchase.Consume(yard, needs);
                yard.Credits += price;
                actor.Treasury.Credits -= cost;
                var ship = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, ShipRole.Builder, Controller.Ai, actor.Id, yard, r.NewShipStartCredits);
                ship.JobSince = u.Tick;
                u.AddShip(ship);
                u.Events.Publish(new TreasuryUpkeep(actor.Id, "builder", cost, 0f));
                u.Events.Publish(new FleetShipBuilt(ship.Id, yard.Id, true, price));
                u.Events.Publish(new BuilderCommissioned(f, ship.Id, yard.Id, site.Id, cost));
                return ship;
            }
        }
        return null;
    }

    /// <summary>Startkapital einer neuen Station (wie beim alten Direktbau).</summary>
    public const float NewStationCredits = 2000f;
    /// <summary>Abstand, ab dem ein Bauschiff als „vor Ort“ gilt.</summary>
    public const float OnSiteRange = 30f;

    /// <summary>Stationstyp der Baustellen; null, wenn Baustellen abgeschaltet sind oder der Typ in dieser Welt fehlt (dann Direktbau).</summary>
    public static StationType? SiteType(Universe u) =>
        u.Rules.ConstructionSites && u.StationTypes.TryGetValue(u.Rules.ConstructionSiteType, out var t) ? t : null;

    /// <summary>
    /// Stückliste für den Bau eines Typs (Ware → Einheiten). Eigene Liste des Typs (<see cref="StationType.Construction"/>), sonst die Regel:
    /// Bausumme × <see cref="EconomyRules.ConstructionMaterialShare"/> nach den Gewichten von <see cref="EconomyRules.ConstructionMaterials"/>,
    /// in Einheiten zum Basispreis (mindestens 1). Waren, die der Typ selbst herstellt, fallen weg (zyklenfreie Baukosten). null, wenn eine
    /// Material-Ware in dieser Welt fehlt (sehr alte Spielstände ohne Warenschlüssel) — dann baut der Lebenszyklus wie früher direkt.
    /// </summary>
    public static Dictionary<GoodId, float>? Bill(Universe u, StationType type)
    {
        if (type.Construction is { } own)
            return own.Where(kv => kv.Value > 0f && u.Goods.ContainsKey(kv.Key)).OrderBy(kv => kv.Key.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
        var produced = type.Recipes.SelectMany(r => r.Outputs.Keys).ToHashSet();
        var mats = new List<(Good Good, float Weight)>();
        foreach (var (key, w) in u.Rules.ConstructionMaterials.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (w <= 0f) continue;
            var g = u.FindGood(key);
            if (g is null) return null;
            if (produced.Contains(g.Id)) continue;
            mats.Add((g, w));
        }
        var bill = new Dictionary<GoodId, float>();
        float total = mats.Sum(m => m.Weight);
        if (total <= 0f) return bill;
        float value = type.BuildCost * u.Rules.StationBuildCostFactor * u.Rules.ConstructionMaterialShare;
        foreach (var (g, w) in mats.OrderBy(m => m.Good.Id.Value))
            bill[g.Id] = MathF.Max(1f, MathF.Round(value * w / total / MathF.Max(0.01f, g.BasePrice)));
        return bill;
    }

    /// <summary>Kann dieser Typ über eine Baustelle gebaut werden (Baustellen-Typ vorhanden, Stückliste auflösbar)?</summary>
    public static bool UsesSites(Universe u, StationType type) => SiteType(u) is not null && Bill(u, type) is not null;

    /// <summary>Laufende (nicht verlassene) Baustellen in Id-Reihenfolge.</summary>
    public static IEnumerable<Station> ActiveSites(Universe u) =>
        u.Stations.Values.Where(s => s.Site is { Abandoned: false }).OrderBy(s => s.Id.Value);

    /// <summary>
    /// Legt eine Baustelle an (oder übernimmt eine verlassene im selben Sektor samt ihrem Material): Station vom Baustellen-Typ mit je
    /// verlangter Ware einem Markteintrag (Bestand = geliefert, Wunschbestand = verlangt) und der Bausumme als Treuhandgeld. Der Investor
    /// zahlt die Bausumme vorher selbst. Veröffentlicht <see cref="ConstructionStarted"/>.
    /// </summary>
    public static Station StartSite(Universe u, StationType type, SectorId sector, Vec2 spot, ActorId owner, float budget, StationId? investorStation, ShipId? investorShip, ActorId? investorActor = null)
    {
        var siteType = SiteType(u) ?? throw new InvalidOperationException("Keine Baustellen in dieser Welt");
        var bill = Bill(u, type) ?? throw new InvalidOperationException($"Stückliste für '{type.Key}' nicht auflösbar");
        var sec = u.Sectors[sector];
        string shortName = sec.Name.Split(' ').Last();
        var reuse = u.StationsById().Where(s => s.Sector == sector && s.Site is { Abandoned: true }).FirstOrDefault();
        Station site;
        if (reuse is not null)
        {
            site = reuse;
            // übriges Material, das das neue Projekt nicht braucht, nimmt die Baustelle nicht weiter an (Wunschbestand = Bestand)
            foreach (var e in site.Market.Values) e.Target = e.Amount;
            site.Name = $"Baustelle {type.Name} {shortName} {site.Id.Value}";
            site.Owner = owner;
            u.TouchStructure();
        }
        else
        {
            var id = u.NewStationId();
            site = StationFactory.Create(u, id, siteType, $"Baustelle {type.Name} {shortName} {id.Value}", sector, spot, owner);
            site.Credits = 0f;
        }
        foreach (var (g, units) in bill)
        {
            var e = site.Stock(g);
            e.Target = units;
            e.Price = u.Goods[g].BasePrice * u.Rules.ConstructionPriceFactor;
        }
        site.Credits += budget;
        site.Site = new BuildSite
        {
            TypeKey = type.Key, Required = new Dictionary<GoodId, float>(bill), Budget = budget,
            InvestorStation = investorStation?.Value ?? 0, InvestorShip = investorShip?.Value ?? 0, InvestorActor = investorActor?.Value ?? 0,
            StartedTick = u.Tick, LastProgressTick = u.Tick,
        };
        if (reuse is null) u.AddStation(site);
        u.Events.Publish(new ConstructionStarted(site.Id, sector, type.Key, budget, bill.Values.Sum()));
        return site;
    }

    /// <summary>
    /// Lieferung an eine Baustelle über den Handel (KI-Händler oder Spieler): Fortschritt merken und <see cref="ConstructionDelivered"/>
    /// veröffentlichen. Aufgerufen von <see cref="TraderSystem"/> und <see cref="TradeActions"/>, nachdem die Ware gebucht ist.
    /// </summary>
    public static void OnDelivered(Universe u, Station station, Ship ship, GoodId good, float units, string by)
    {
        if (station.Site is not { Abandoned: false } bs || units <= 0f || !bs.Required.ContainsKey(good)) return;
        bs.LastProgressTick = u.Tick;
        u.Events.Publish(new ConstructionDelivered(station.Id, ship.Id, good, units, by));
    }

    /// <summary>
    /// Lieferanten einer Ware für den Bau (vom Sektor aus erreichbar): fertige Stationen, die die Ware herstellen oder als Lager ohne Rezept
    /// führen (Lagerhaus, Handelsstation), mit mindestens einer Einheit und einem Preis bis Basispreis × <see cref="EconomyRules.ConstructionMaxBuyFactor"/>;
    /// Verbraucher (Rezept-Eingänge) werden nicht leergekauft.
    /// </summary>
    public static IEnumerable<Station> Suppliers(Universe u, SectorId from, GoodId good)
    {
        float cap = u.Goods[good].BasePrice * u.Rules.ConstructionMaxBuyFactor;
        foreach (var s in u.StationsTrading(good))
        {
            if (s.IsBuildSite || s.Market[good].Amount < 1f || s.Market[good].Price <= 0f || s.Market[good].Price > cap) continue;
            if (!s.IsStockOnly(good) && !s.Production.Any(r => r.Outputs.ContainsKey(good))) continue;
            if (!u.IsReachable(from, s.Sector)) continue;
            yield return s;
        }
    }

    /// <summary>
    /// Ist die Baustelle blockiert (Auslöser des Notbaus)? Ja, wenn kein Bauschiff erreichbar ist (und keins zugeteilt), oder wenn für eine
    /// fehlende Ware kein erreichbarer Lieferant Lager zum Höchstpreis hat oder das Treuhandgeld nicht einmal eine Einheit beim günstigsten bezahlt.
    /// </summary>
    public static bool IsBlocked(Universe u, Station site)
    {
        var bs = site.Site!;
        if (!bs.Emergency && bs.Builder.Value == 0
            && !u.Ships.Values.Any(s => s.IsSiteBuilder && u.IsReachable(s.Sector, site.Sector, GateAccess.ProfileOf(u, s))))
            return true;
        foreach (var g in bs.Required.Keys.OrderBy(g => g.Value))
        {
            if (bs.Missing(site, g) < 0.001f) continue;
            float cheapest = float.PositiveInfinity;
            foreach (var s in Suppliers(u, site.Sector, g)) cheapest = MathF.Min(cheapest, s.Market[g].Price);
            if (float.IsPositiveInfinity(cheapest) || site.Credits < cheapest) return true;
        }
        return false;
    }

    /// <summary>Steht das zugeteilte Bauschiff (geparkt) an der Baustelle?</summary>
    public static bool BuilderOnSite(Universe u, Station site) =>
        site.Site is { } bs && bs.Builder.Value != 0 && u.Ships.TryGetValue(bs.Builder, out var b) && b.Transit is null
        && b.Sector == site.Sector && Vec2.Distance(b.Position, site.Position) <= OnSiteRange;

    /// <summary>
    /// Notbau (SCHIFFE.md Abschnitt 10, Henne-Ei-Problem): Nur die fehlenden Materialien entstehen aus dem Nichts gegen Credits aus dem
    /// Treuhandgeld (Basispreis × <see cref="EconomyRules.EmergencyBuildMarkup"/>, Geldsenke); reicht das Treuhandgeld nicht, wird der Rest
    /// erlassen (Notfall). Danach montiert die Baustelle auch ohne Bauschiff. Setzt die Abkühlzeit der Fraktion.
    /// </summary>
    public static void Emergency(Universe u, Station site, bool critical)
    {
        var bs = site.Site!;
        float units = 0f, cost = 0f, waived = 0f;
        foreach (var g in bs.Required.Keys.OrderBy(g => g.Value).ToList())
        {
            float miss = bs.Missing(site, g);
            if (miss < 0.001f) continue;
            float full = miss * u.Goods[g].BasePrice * u.Rules.EmergencyBuildMarkup;
            float pay = MathF.Min(MathF.Max(0f, site.Credits), full);
            site.Credits -= pay;
            cost += pay;
            waived += full - pay;
            site.Stock(g).Amount += miss;
            units += miss;
            u.Events.Publish(new ConstructionDelivered(site.Id, default, g, miss, "emergency"));
        }
        bs.Emergency = true;
        bs.BlockedSince = -1;
        bs.LastProgressTick = u.Tick;
        var faction = u.FactionOf(site);
        if (faction is { } fid && u.Factions.TryGetValue(fid, out var f)) f.LastEmergencyBuildTick = u.Tick;
        u.Events.Publish(new EmergencyConstruction(site.Id, faction ?? default, units, cost, waived, critical));
    }

    /// <summary>
    /// Fertigstellung: Die Baustelle wird zur Station ihres Typs (gleiche Id, Ort und Besitzer; Markt und Rezepte aus dem Typ, Startkapital
    /// wie beim Direktbau), das Bauschiff bekommt die Montagegebühr aus dem Treuhandgeld, der Rest des Treuhandgelds ist der Credit-Anteil
    /// (verschwindet wie früher die Baukosten). Veröffentlicht <see cref="StationBuilt"/> und <see cref="ConstructionCompleted"/>.
    /// </summary>
    public static Station Complete(Universe u, Station site)
    {
        var bs = site.Site!;
        var type = u.StationTypes[bs.TypeKey];
        string shortName = u.Sectors[site.Sector].Name.Split(' ').Last();
        var st = StationFactory.Create(u, site.Id, type, $"{type.Name} {shortName} {site.Id.Value}", site.Sector, site.Position, site.Owner);
        st.Credits = NewStationCredits;
        var builder = bs.Builder;
        if (builder.Value != 0 && u.Ships.TryGetValue(builder, out var b) && b.IsSiteBuilder)
            u.WalletOf(b).Credits += MathF.Min(MathF.Max(0f, site.Credits), bs.Budget * u.Rules.ConstructionFeeShare);
        ReleaseBuilders(u, site.Id);
        u.ReplaceStation(st);
        u.NotifyStationBuilt(st);
        u.Events.Publish(new ConstructionCompleted(st.Id, st.Sector, type.Key, bs.Emergency, builder));
        return st;
    }

    /// <summary>
    /// Verfall (ohne Fortschritt seit <see cref="EconomyRules.ConstructionExpiryTicks"/>): Das restliche Treuhandgeld geht an den Investor
    /// zurück (Station, sonst Schiff; gibt es keinen mehr, verschwindet es). Geliefertes Material bleibt als Lager an der verlassenen Baustelle
    /// liegen (ein neues Projekt im Sektor übernimmt es, der Spieler kann es kaufen); ohne Material verschwindet die Baustelle.
    /// </summary>
    public static void Expire(Universe u, Station site)
    {
        var bs = site.Site!;
        float refund = MathF.Max(0f, site.Credits);
        if (bs.InvestorStation != 0 && u.Stations.TryGetValue(new StationId(bs.InvestorStation), out var inv) && !inv.IsBuildSite) inv.Credits += refund;
        else if (bs.InvestorShip != 0 && u.Ships.TryGetValue(new ShipId(bs.InvestorShip), out var ship)) ship.Credits += refund;
        else if (bs.InvestorActor != 0 && u.Actors.TryGetValue(new ActorId(bs.InvestorActor), out var actor)) actor.Treasury.Credits += refund;   // Phase 9: Kasse eines Akteurs
        site.Credits = 0f;
        ReleaseBuilders(u, site.Id);
        bool kept = site.Market.Values.Any(e => e.Amount >= 1f);
        u.Events.Publish(new ConstructionAbandoned(site.Id, bs.TypeKey, refund, kept));
        if (!kept) { u.RemoveStation(site.Id, "abandoned"); return; }
        bs.Abandoned = true;
        bs.Builder = default;
        foreach (var e in site.Market.Values) e.Target = MathF.Max(1f, e.Amount);
        site.Name = "Verlassene Baustelle " + u.Sectors[site.Sector].Name.Split(' ').Last() + " " + site.Id.Value;
        u.TouchStructure();
    }

    /// <summary>Gibt Bauschiffe frei, deren Auftrag diese Baustelle ist (Fracht bleibt an Bord; das Schiff denkt im nächsten Tick neu).</summary>
    static void ReleaseBuilders(Universe u, StationId site)
    {
        foreach (var s in u.ShipsById())
            if (s.Order is BuildOrder bo && bo.Site == site && s.Transit is null)
            {
                s.Order = null;
                s.Phase = TradePhase.Idle;
                s.NextThinkTick = u.Tick + 1;
            }
    }
}

/// <summary>
/// Phase 8: Bau über Baustellen (eigenes Modul „Construction“). Zwei Teile:
/// 1) Baustellen: Montage, sobald alles geliefert ist und das Bauschiff vor Ort ist (oder der Notbau gegriffen hat); Notbau, wenn die
///    Baustelle seit <see cref="EconomyRules.EmergencyBuildTicks"/> (kritisch × <see cref="EconomyRules.EmergencyCriticalFactor"/>) blockiert ist
///    und die Fraktion ihre Abkühlzeit hinter sich hat; Verfall ohne Fortschritt; verlassene Baustellen ohne Material verschwinden.
/// 2) Bauschiffe (Etikett <see cref="ShipRole.Builder"/>, Fähigkeit Construction) als Ausführer der Aufgabe „build“: übernehmen eine Baustelle
///    (eigene Fraktion zuerst — über die Besitzer, <see cref="Universe.FactionOf(Ship)"/> —, dann die nächste), holen fehlendes Material bei Herstellern oder Lagern (bezahlt aus dem Treuhandgeld der
///    Baustelle), liefern es ab und bleiben zur Montage vor Ort. Händler und Spieler beliefern Baustellen zusätzlich über den normalen Handel.
/// </summary>
public sealed class ConstructionSystem : ISimSystem
{
    public const int ThinkInterval = 10;

    public void Step(Universe u, int dt)
    {
        StepSites(u, dt);
        StepBuilders(u);
    }

    static void StepSites(Universe u, int dt)
    {
        var rules = u.Rules;
        foreach (var site in u.StationsById().Where(s => s.Site is not null).ToList())
        {
            var bs = site.Site!;
            if (bs.Abandoned)
            {
                if (site.Market.Values.All(e => e.Amount < 1f)) u.RemoveStation(site.Id, "cleared");
                continue;
            }
            if (!u.StationTypes.ContainsKey(bs.TypeKey)) { ConstructionRules.Expire(u, site); continue; }
            if (bs.Builder.Value != 0 && (!u.Ships.TryGetValue(bs.Builder, out var b) || !b.IsSiteBuilder)) bs.Builder = default;

            if (bs.Complete(site) && (bs.Emergency || ConstructionRules.BuilderOnSite(u, site)))
            {
                bs.BlockedSince = -1;
                bs.AssemblyTicks += dt;
                bs.LastProgressTick = u.Tick;
                if (bs.AssemblyTicks >= rules.ConstructionAssemblyTicks) ConstructionRules.Complete(u, site);
                continue;
            }

            if (ConstructionRules.IsBlocked(u, site))
            {
                if (bs.BlockedSince < 0) bs.BlockedSince = u.Tick;
                // Phase 19: kein Bauschiff mehr in der Welt (nach der ersten Bauwelle aufgegeben) — die Fraktion der Baustelle kauft eins nach
                if (u.Tick % ConstructionRules.CommissionInterval == 0 && bs.Builder.Value == 0 && !bs.Emergency
                    && !u.Ships.Values.Any(s => s.IsSiteBuilder && u.IsReachable(s.Sector, site.Sector, GateAccess.ProfileOf(u, s))))
                    ConstructionRules.CommissionBuilder(u, site);
                bool critical = StationCriticality.IsCriticalSite(u, site);
                long wait = (long)(rules.EmergencyBuildTicks * (critical ? rules.EmergencyCriticalFactor : 1f));
                long last = u.FactionOf(site) is { } fid && u.Factions.TryGetValue(fid, out var f) ? f.LastEmergencyBuildTick : -1_000_000;
                if (u.Tick - bs.BlockedSince >= wait && u.Tick - last >= rules.EmergencyBuildCooldown)
                    ConstructionRules.Emergency(u, site, critical);
                continue;   // blockierte Baustellen verfallen nicht: der Notbau löst sie
            }
            bs.BlockedSince = -1;
            if (u.Tick - bs.LastProgressTick >= rules.ConstructionExpiryTicks) ConstructionRules.Expire(u, site);
        }
    }

    static void StepBuilders(Universe u)
    {
        // feste Reihenfolge (Id), nicht die Einfügereihenfolge des Dictionarys: wer zuerst denkt, bekommt die freie Baustelle
        foreach (var ship in u.ShipsById().Where(s => s.Role == ShipRole.Builder).ToList())
        {
            // Phase 12: auch Spielerschiffe mit Befehl „Bauen“ (Ausführer ist dieses System)
            if (ship.Role != ShipRole.Builder || (ship.Controller != Controller.Ai && ship.Command is not BuildCommand)) continue;
            if (!ship.Has(Capability.Construction))
            {
                // Bauausleger ausgebaut: Auftrag fällt weg, Baustelle wird frei (wie JobRelease bei Händlern/Minern)
                foreach (var s in u.StationsById()) if (s.Site is { } sb && sb.Builder == ship.Id) sb.Builder = default;
                JobRelease.Release(u, ship);
                continue;
            }
            if (ship.Transit is { } t)
            {
                if (u.Tick >= t.ArrivalTick) Arrive(u, ship);
                continue;
            }
            if (u.Tick < ship.NextThinkTick) continue;
            Think(u, ship);
        }
    }

    /// <summary>Baustelle, die diesem Bauschiff zugeteilt ist (null = keine).</summary>
    public static Station? AssignedSite(Universe u, Ship ship) =>
        ConstructionRules.ActiveSites(u).FirstOrDefault(s => s.Site!.Builder == ship.Id);

    /// <summary>
    /// Nächste Baustelle ohne Bauschiff, die dieses Schiff erreichen kann: eigene Fraktion zuerst, dann kürzeste Reise, dann Id.
    /// Reine Auswertung (auch für die Aufgabenbewertung <see cref="BuildJob"/>).
    /// </summary>
    public static Station? OpenSite(Universe u, Ship ship) =>
        ConstructionRules.ActiveSites(u)
            .Where(s => s.Site!.Builder.Value == 0 && u.IsReachable(ship.Sector, s.Sector, GateAccess.ProfileOf(u, ship)))
            .OrderBy(s => u.FactionOf(ship) is { } own && u.FactionOf(s) == own ? 0 : 1)
            .ThenBy(s => Travel.Estimate(u, ship.Sector, ship.Position, s.Sector, s.DockPoint(ship.Id), ship))
            .ThenBy(s => s.Id.Value)
            .FirstOrDefault();

    static bool AtSite(Ship ship, Station site) =>
        ship.Transit is null && ship.Sector == site.Sector && Vec2.Distance(ship.Position, site.Position) <= ConstructionRules.OnSiteRange;

    static void Think(Universe u, Ship ship)
    {
        var site = AssignedSite(u, ship);
        // Phase 10: Ist die zugeteilte Baustelle für dieses Schiff gesperrt, gibt es sie frei (ein anderes Bauschiff darf übernehmen)
        if (site is not null && !u.IsReachable(ship.Sector, site.Sector, GateAccess.ProfileOf(u, ship)))
        {
            site.Site!.Builder = default;
            site = null;
        }
        // Phase 12: Befehl „Bauen“ mit genannter Baustelle zuerst
        if (site is null && ship.Command is BuildCommand bc && bc.Site.Value != 0 && u.Stations.TryGetValue(bc.Site, out var wanted)
            && wanted.Site is { Abandoned: false } ws && ws.Builder.Value == 0 && u.IsReachable(ship.Sector, wanted.Sector, GateAccess.ProfileOf(u, ship)))
        {
            ws.Builder = ship.Id;
            site = wanted;
        }
        if (site is null && OpenSite(u, ship) is { } open)
        {
            open.Site!.Builder = ship.Id;
            site = open;
        }
        if (site is null)
        {
            if (ship.Cargo.Values.Sum() > 0.001f && TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position) is { } buyer)
            {
                // Übriges Material (z. B. nach einem Notbau) verkaufen, damit der Laderaum für die nächste Baustelle frei ist
                GoTo(u, ship, new BuildOrder(default, buyer.Id, default), TradePhase.GoingToSell, buyer.Sector, buyer.DockPoint(ship.Id));
                return;
            }
            ship.Cargo.Clear();
            ship.Order = null;
            ship.Phase = TradePhase.Idle;
            if (ship.IdleSince < 0) ship.IdleSince = u.Tick;
            ship.NextThinkTick = u.Tick + TraderSystem.IdleBackoff(u, ship) + ship.Id.Value % 7;
            return;
        }
        ship.IdleSince = -1;
        var bs = site.Site!;

        // 1) Material für die Baustelle an Bord: abliefern (vor Ort sofort, sonst hinfliegen)
        if (ship.Cargo.Any(kv => kv.Value > 0.001f && bs.Missing(site, kv.Key) > 0.001f))
        {
            if (AtSite(ship, site)) { Deliver(u, ship, site); ship.NextThinkTick = u.Tick + 1; return; }
            GoTo(u, ship, new BuildOrder(site.Id, default, default), TradePhase.GoingToSell, site.Sector, site.DockPoint(ship.Id));
            return;
        }
        // 2) Fehlendes Material beim nächsten Lieferanten holen (bezahlt aus dem Treuhandgeld)
        if (PickSupply(u, ship, site) is { } pick)
        {
            GoTo(u, ship, new BuildOrder(site.Id, pick.Supplier.Id, pick.Good), TradePhase.GoingToBuy, pick.Supplier.Sector, pick.Supplier.DockPoint(ship.Id));
            return;
        }
        // 3) Nichts zu holen (alles geliefert oder blockiert): zur Baustelle und dort warten — die Montage braucht das Schiff vor Ort
        if (!AtSite(ship, site))
        {
            GoTo(u, ship, new BuildOrder(site.Id, default, default), TradePhase.Idle, site.Sector, site.DockPoint(ship.Id));
            return;
        }
        ship.Order = new BuildOrder(site.Id, default, default);
        ship.Phase = TradePhase.Idle;
        ship.NextThinkTick = u.Tick + ThinkInterval;
    }

    /// <summary>Nächster Lieferant (kürzeste Reise) für eine fehlende Ware, die in den Laderaum passt und die die Baustelle bezahlen kann.</summary>
    static (Station Supplier, GoodId Good)? PickSupply(Universe u, Ship ship, Station site)
    {
        var bs = site.Site!;
        (Station, GoodId)? best = null;
        float bestTime = float.PositiveInfinity;
        foreach (var g in bs.Required.Keys.OrderBy(g => g.Value))
        {
            float rest = bs.Missing(site, g) - ship.Cargo.GetValueOrDefault(g);
            if (rest < 0.001f || ship.FreeUnits(u, g) < 1f) continue;   // auch Reste unter einer Einheit holen (sonst hinge die Baustelle)
            foreach (var s in ConstructionRules.Suppliers(u, ship.Sector, g).OrderBy(s => s.Id.Value))
            {
                if (site.Credits < s.Market[g].Price) continue;
                float time = Travel.Estimate(u, ship.Sector, ship.Position, s.Sector, s.DockPoint(ship.Id), ship);
                if (time < bestTime) { bestTime = time; best = (s, g); }
            }
        }
        return best;
    }

    /// <summary>Auftrag setzen und losfliegen; Treibstoff wie bei Händlern über <see cref="Refuel.PrepareTrip"/> (Tankstopp oder liegen bleiben).</summary>
    static void GoTo(Universe u, Ship ship, BuildOrder order, TradePhase phase, SectorId sector, Vec2 pos)
    {
        ship.Order = order;
        ship.Phase = phase;
        var plan = Refuel.PrepareTrip(u, ship, sector, pos);
        if (plan == Refuel.TripPlan.FuelStop) return;   // Order ist jetzt GoToOrder zur Tankstelle, danach denkt das Schiff neu
        if (plan == Refuel.TripPlan.Stuck)
        {
            ship.Order = null;
            ship.Phase = TradePhase.Idle;
            if (ship.IdleSince < 0) ship.IdleSince = u.Tick;
            ship.NextThinkTick = u.Tick + TraderSystem.IdleBackoff(u, ship);
            return;
        }
        Travel.Start(u, ship, sector, pos);
    }

    static void Arrive(Universe u, Ship ship)
    {
        u.FinishTransit(ship);
        ship.NextThinkTick = u.Tick + 1;
        switch (ship.Order)
        {
            case GoToOrder g:
                if (u.Stations.TryGetValue(g.Target, out var fuelStation)) Refuel.TankUp(u, ship, fuelStation);
                ship.Order = null;
                break;
            case BuildOrder bo when ship.Phase == TradePhase.GoingToBuy:
                if (u.Stations.TryGetValue(bo.Supplier, out var supplier) && u.Stations.TryGetValue(bo.Site, out var site) && site.Site is { Abandoned: false })
                    BuyForSite(u, ship, supplier, site, bo.Good);
                ship.Order = new BuildOrder(bo.Site, default, default);
                break;
            case BuildOrder bo when ship.Phase == TradePhase.GoingToSell:
                if (bo.Site.Value == 0)
                {
                    if (u.Stations.TryGetValue(bo.Supplier, out var buyer)) SellLeftovers(u, ship, buyer);
                    ship.Order = null;
                }
                else if (u.Stations.TryGetValue(bo.Site, out var target) && target.Site is { Abandoned: false })
                    Deliver(u, ship, target);
                break;
        }
        ship.Phase = TradePhase.Idle;
    }

    /// <summary>Kauft beim Lieferanten so viel der Ware, wie der Baustelle fehlt (Laderaum, Lager, Treuhandgeld); die Baustelle bezahlt.</summary>
    static void BuyForSite(Universe u, Ship ship, Station supplier, Station site, GoodId good)
    {
        if (!supplier.Market.TryGetValue(good, out var e) || e.Price <= 0f) return;
        float rest = site.Site!.Missing(site, good) - ship.Cargo.GetValueOrDefault(good);
        float units = MathF.Floor(MathF.Min(MathF.Min(MathF.Ceiling(rest), ship.FreeUnits(u, good)),
                                            MathF.Min(e.Amount, MathF.Max(0f, site.Credits) / e.Price)));
        if (units < 1f) return;
        float cost = units * e.Price;
        e.Amount -= units;
        supplier.Credits += cost;
        site.Credits -= cost;
        ship.Cargo[good] = ship.Cargo.GetValueOrDefault(good) + units;
    }

    /// <summary>Liefert die Fracht ab, die der Baustelle fehlt (schon aus dem Treuhandgeld bezahlt); Überschuss bleibt an Bord.</summary>
    static void Deliver(Universe u, Ship ship, Station site)
    {
        var bs = site.Site!;
        foreach (var g in ship.Cargo.Keys.OrderBy(g => g.Value).ToList())
        {
            float units = MathF.Min(ship.Cargo[g], bs.Missing(site, g));
            if (units <= 0.001f) continue;
            site.Stock(g).Amount += units;
            if (ship.Cargo[g] - units <= 0.0001f) ship.Cargo.Remove(g); else ship.Cargo[g] -= units;
            bs.LastProgressTick = u.Tick;
            u.Events.Publish(new ConstructionDelivered(site.Id, ship.Id, g, units, "builder"));
        }
    }

    /// <summary>Verkauft übrige Fracht zu denselben Regeln wie der Handel (Station zahlt, was sie hat und annimmt); Unverkäufliches geht über Bord.</summary>
    static void SellLeftovers(Universe u, Ship ship, Station st)
    {
        foreach (var g in ship.Cargo.Keys.OrderBy(g => g.Value).ToList())
        {
            if (!st.Market.TryGetValue(g, out var e) || e.Price <= 0f) continue;
            float units = MathF.Min(MathF.Min(ship.Cargo[g], MathF.Max(0f, st.Credits) / e.Price), st.AcceptableUnits(g, u.Rules));
            if (units <= 0f) continue;
            e.Amount += units;
            st.Credits -= units * e.Price;
            u.WalletOf(ship).Credits += units * e.Price;   // Phase 12: Spieler-Bauschiffe buchen auf die Spielerkasse
            if (ship.Cargo[g] - units <= 0.0001f) ship.Cargo.Remove(g); else ship.Cargo[g] -= units;
            ConstructionRules.OnDelivered(u, st, ship, g, units, "builder");
        }
        ship.Cargo.Clear();
    }
}

/// <summary>Eine Materialzeile einer Baustelle für die Anzeige: Ware, geliefert, verlangt.</summary>
public sealed record SiteLine(GoodId Good, string Name, float Delivered, float Required);

/// <summary>Anzeigezustand einer Baustelle (Phase 8): Zieltyp, Materialfortschritt 0..1, Zeilen, Bauschiff, Status-Text, Treuhandgeld.</summary>
public sealed record SiteStatus(string TypeKey, string TypeName, float Progress, IReadOnlyList<SiteLine> Lines, string Builder, string State, bool Abandoned, float Escrow);

/// <summary>Reine Anzeige-Funktionen für Baustellen (Sektoransicht, Station-Info, Kommandozentrale); ändern nichts am Zustand.</summary>
public static class ConstructionInfo
{
    /// <summary>Zustand der Baustelle oder null, wenn die Station keine Baustelle ist.</summary>
    public static SiteStatus? Of(Universe u, Station st)
    {
        if (st.Site is not { } bs) return null;
        string typeName = u.StationTypes.TryGetValue(bs.TypeKey, out var t) ? t.Name : bs.TypeKey;
        var lines = bs.Required.OrderBy(kv => kv.Key.Value)
            .Select(kv => new SiteLine(kv.Key, u.Goods.TryGetValue(kv.Key, out var g) ? g.Name : kv.Key.Value.ToString(),
                                       MathF.Min(kv.Value, st.Market.TryGetValue(kv.Key, out var e) ? e.Amount : 0f), kv.Value))
            .ToList();
        float need = lines.Sum(l => l.Required);
        float progress = need <= 0f ? 1f : lines.Sum(l => l.Delivered) / need;
        string builder = bs.Builder.Value != 0 && u.Ships.TryGetValue(bs.Builder, out var b)
            ? (u.ShipModels.TryGetValue(b.ModelKey, out var m) ? m.Name : "Bauschiff") + $" #{b.Id.Value}" : "keins";
        string state;
        if (bs.Abandoned) state = "Verlassen – das Material lagert hier";
        else if (bs.Complete(st) && (bs.Emergency || ConstructionRules.BuilderOnSite(u, st)))
            state = $"{(bs.Emergency ? "Notbau – " : "")}Montage {100f * bs.AssemblyTicks / MathF.Max(1, u.Rules.ConstructionAssemblyTicks):0} %";
        else if (bs.Complete(st)) state = "Alles geliefert – wartet auf das Bauschiff";
        else if (bs.BlockedSince >= 0)
        {
            bool critical = StationCriticality.IsCriticalSite(u, st);
            long due = bs.BlockedSince + (long)(u.Rules.EmergencyBuildTicks * (critical ? u.Rules.EmergencyCriticalFactor : 1f));
            state = $"Blockiert – Notbau{(critical ? " (kritisch)" : "")} in {Math.Max(0, due - u.Tick)} Ticks";
        }
        else state = "Wartet auf Material";
        return new SiteStatus(bs.TypeKey, typeName, progress, lines, builder, state, bs.Abandoned, st.Credits);
    }
}
