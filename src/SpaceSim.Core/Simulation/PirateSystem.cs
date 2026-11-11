using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Piraten als Gegenfraktion (Phase 11, FLOTTEN.md „Piraten“): gesetzlose Akteure (<see cref="ActorKind.Outlaw"/>) mit Versteck
/// (<see cref="StationType.Hideout"/>) und Kaperschiffen (Etikett <see cref="ShipRole.Pirate"/>). Ein Kaperschiff wählt sein Revier nach Beute
/// (Verkehrswert der Lagekarte, gemieden wird Sicherheit), lauert dort an einem Gate und überfällt Schiffe mit wertvoller Fracht, die gerade im Sektor
/// sind und nicht im Schutz einer Station liegen. Aufgelöst wird über <see cref="Universe.Encounters"/>; Streifen im Sektor können eingreifen und
/// stellen lauernde Kaperschiffe. Beute geht über die Hehlerei in die Kasse des Akteurs, Überfälle erhöhen Bedrohung und Fahndung. Den Spieler warnt
/// eine angekündigte Begegnung (<see cref="PendingEncounter"/>) vor; wer andockt oder den Sektor verlässt, entkommt. Verlorene Kaperschiffe baut der
/// Akteur aus seiner Kasse nach.
/// </summary>
public sealed class PirateSystem : ISimSystem
{
    /// <summary>Takt der Kassen-Buchungen (Unterhalt, Neubau) in Ticks.</summary>
    const int BookingTicks = 50;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.SecurityActive) return;
        ResolvePending(u);
        foreach (var raider in u.Ships.Values.Where(ThreatMap.IsRaider).OrderBy(s => s.Id.Value).ToList())
            if (u.Ships.ContainsKey(raider.Id)) StepRaider(u, raider, dt);
        if (u.Tick % BookingTicks < dt) Book(u, BookingTicks);
    }

    // ───────── Kaperschiffe ─────────

    static void StepRaider(Universe u, Ship raider, int dt)
    {
        if (raider.Transit is { } t)
        {
            if (u.Tick < t.ArrivalTick) return;
            u.FinishTransit(raider);
            OnArrive(u, raider);
            return;
        }
        if (raider.Order is PatrolOrder p && raider.Sector == p.Sector)
        {
            if (u.PendingEncounters.Any(e => e.Source == raider.Id)) return;   // wartet auf den gewarnten Spieler
            if (TryIntercept(u, raider, dt) || !u.Ships.ContainsKey(raider.Id)) return;
            if (u.Tick >= raider.NextThinkTick) { GoHome(u, raider); return; }
            TryRaid(u, raider, dt);
            return;
        }
        if (u.Tick < raider.NextThinkTick) return;
        if (AtHome(u, raider)) ChooseHunt(u, raider);
        else GoHome(u, raider);
    }

    static Station? Home(Universe u, Ship s)
    {
        if (u.Stations.TryGetValue(s.HomeBase, out var h)) return h;
        var hideout = u.Stations.Values.Where(st => st.Owner == s.Owner && IsHideout(u, st)).OrderBy(st => st.Id.Value).FirstOrDefault();
        if (hideout is not null) s.HomeBase = hideout.Id;
        return hideout;
    }

    /// <summary>Ist die Station ein Piratenversteck?</summary>
    public static bool IsHideout(Universe u, Station st) => StationProtection.IsHideout(u, st);

    static bool AtHome(Universe u, Ship s) =>
        Home(u, s) is { } h && s.Sector == h.Sector && Vec2.Distance(s.Position, h.Position) <= u.Rules.StationSafeRadius;

    static void OnArrive(Universe u, Ship raider)
    {
        if (raider.Order is GoToOrder g && g.Target == raider.HomeBase)
        {
            raider.Order = null;
            raider.DutySector = default;
            ServiceAtBase(u, raider, "pirates");
            raider.NextThinkTick = u.Tick + u.Rules.PirateRestTicks;
            return;
        }
        if (raider.Order is PatrolOrder)
        {
            raider.NextThinkTick = u.Tick + u.Rules.PirateLurkTicks;   // Lauerzeit beginnt
            return;
        }
        raider.Order = null;
        raider.NextThinkTick = u.Tick + 20;
    }

    /// <summary>
    /// Reparatur und Auftanken an der Heimatbasis aus der Kasse des Besitzers (Polizei und Piraten): Hülle zu <see cref="EconomyRules.BaseRepairPerHull"/>
    /// je Punkt, Treibstoff zum Basispreis; was die Kasse nicht deckt, bleibt aus. Ereignis <see cref="TreasuryUpkeep"/> mit <paramref name="purpose"/>.
    /// </summary>
    public static void ServiceAtBase(Universe u, Ship s, string purpose)
    {
        if (!u.Actors.TryGetValue(s.Owner, out var owner)) return;
        float spent = 0f;
        float missing = MathF.Max(0f, s.MaxHull - s.Hull);
        if (missing > 0f && u.Rules.BaseRepairPerHull > 0f)
        {
            float fix = MathF.Min(missing, MathF.Max(0f, owner.Treasury.Credits) / u.Rules.BaseRepairPerHull);
            s.Hull += fix;
            spent += fix * u.Rules.BaseRepairPerHull;
            owner.Treasury.Credits -= fix * u.Rules.BaseRepairPerHull;
        }
        if (Refuel.ActiveFuel(s) is { } fuel && s.FuelSpace > 0f && u.Goods.TryGetValue(fuel, out var good))
        {
            float units = MathF.Min(s.FuelSpace, MathF.Max(0f, owner.Treasury.Credits) / MathF.Max(0.01f, good.BasePrice));
            s.Fuel[fuel] = s.FuelAmount(fuel) + units;
            spent += units * good.BasePrice;
            owner.Treasury.Credits -= units * good.BasePrice;
        }
        if (spent > 0f) u.Events.Publish(new TreasuryUpkeep(owner.Id, purpose, spent, 0f));
    }

    static void GoHome(Universe u, Ship raider)
    {
        var home = Home(u, raider);
        if (home is null) { raider.Order = null; raider.NextThinkTick = u.Tick + 200; return; }
        if (AtHome(u, raider)) { raider.Order = null; raider.DutySector = default; raider.NextThinkTick = u.Tick + u.Rules.PirateRestTicks; return; }
        raider.Order = new GoToOrder(home.Id);
        if (!Travel.Start(u, raider, home.Sector, home.DockPoint(raider.Id))) { raider.Order = null; raider.NextThinkTick = u.Tick + 200; }
    }

    /// <summary>
    /// Reviergewicht eines Sektors für Piraten: (Verkehrswert + 500) ÷ (1 + Abneigung × Sicherheit) — Beute lockt, Streifen schrecken ab. Reine Funktion.
    /// </summary>
    public static float HuntWeight(Universe u, Sector s) =>
        (MathF.Max(0f, s.Traffic) + 500f) / (1f + u.Rules.PirateSecurityAversion * MathF.Max(0f, s.Security));

    static void ChooseHunt(Universe u, Ship raider)
    {
        var profile = GateAccess.ProfileOf(u, raider);
        var sectors = u.Sectors.Values.OrderBy(s => s.Id.Value).Where(s => s.Gates.Count > 0 && u.IsReachable(raider.Sector, s.Id, profile)).ToList();
        float total = sectors.Sum(s => HuntWeight(u, s));
        if (sectors.Count == 0 || total <= 0f) { raider.NextThinkTick = u.Tick + 200; return; }
        double pick = u.Rng.NextDouble() * total;
        var target = sectors[^1];
        foreach (var s in sectors) { pick -= HuntWeight(u, s); if (pick < 0) { target = s; break; } }
        // Lauerposten nahe einem Gate (dort kommt der Verkehr vorbei), etwas abseits
        var gate = u.Gates[target.Gates[u.Rng.NextInt(target.Gates.Count)]];
        var spot = gate.Position + Vec2.FromAngle((float)(u.Rng.NextDouble() * Math.PI * 2)) * 30f;
        raider.DutySector = target.Id;
        raider.Order = new PatrolOrder(target.Id);
        if (!Travel.Start(u, raider, target.Id, spot)) { raider.Order = null; raider.DutySector = default; raider.NextThinkTick = u.Tick + 200; }
    }

    // ───────── Überfall ─────────

    /// <summary>
    /// Mögliche Opfer im Sektor (feste Reihenfolge): KI-Schiffe ohne Polizei/Piraten und — wenn erlaubt — das aktive Spielerschiff, die gerade dort sind,
    /// nicht im Schutzradius einer Station liegen und Fracht im Wert von mindestens <see cref="EconomyRules.MinLootValue"/> tragen; mit Gewicht (Wert).
    /// </summary>
    public static List<(Ship Ship, float Weight)> Victims(Universe u, SectorId sector)
    {
        var list = new List<(Ship, float)>();
        var ids = u.Tracker.ParkedIn(sector).Concat(u.Tracker.TransitIn(sector)).Distinct().OrderBy(i => i.Value);
        foreach (var id in ids)
        {
            if (!u.Ships.TryGetValue(id, out var s) || ThreatMap.IsPolice(s) || ThreatMap.IsRaider(s)) continue;
            bool player = s.IsPlayer;
            // Phase 12: nicht aktive Spielerschiffe (Flotte) nur mit FleetRaids, das aktive nur mit PlayerRaids im Spielersektor
            if (player && s.Id == u.PlayerShip && (!u.Rules.PlayerRaids || u.PlayerSector != sector)) continue;
            if (player && s.Id != u.PlayerShip && !u.Rules.FleetRaids) continue;
            var (where, pos) = ThreatMap.Where(u, s);
            if (where != sector || ThreatMap.NearStation(u, sector, pos)) continue;
            float value = ThreatMap.CargoValue(u, s);
            if (value < u.Rules.MinLootValue) continue;
            list.Add((s, player ? value * u.Rules.PlayerRaidWeight : value));
        }
        return list;
    }

    /// <summary>Polizeischiffe der Sektorfraktion, die gerade im Sektor sind (feste Reihenfolge).</summary>
    public static List<Ship> PoliceIn(Universe u, SectorId sector)
    {
        var list = new List<Ship>();
        if (!u.Sectors.TryGetValue(sector, out var sec)) return list;
        var ids = u.Tracker.ParkedIn(sector).Concat(u.Tracker.TransitIn(sector)).Distinct().OrderBy(i => i.Value);
        foreach (var id in ids)
            if (u.Ships.TryGetValue(id, out var s) && ThreatMap.IsPolice(s) && u.FactionOf(s) == sec.Owner && ThreatMap.Where(u, s).Sector == sector) list.Add(s);
        return list;
    }

    static void TryRaid(Universe u, Ship raider, int dt)
    {
        var sector = u.Sectors[raider.Sector];
        float p = u.Rules.RaidRatePerTick * dt / (1f + u.Rules.RaidSecurityDeterrence * MathF.Max(0f, sector.Security));
        if (u.Rng.NextDouble() >= p) return;
        var victims = Victims(u, sector.Id);
        float total = victims.Sum(v => v.Weight);
        if (victims.Count == 0 || total <= 0f) return;
        double pick = u.Rng.NextDouble() * total;
        var victim = victims[^1].Ship;
        foreach (var (s, w) in victims) { pick -= w; if (pick < 0) { victim = s; break; } }
        if (victim.Id == u.PlayerShip)   // Phase 12: nur das aktive Schiff bekommt eine Vorwarnung, Flottenschiffe werden sofort aufgelöst
        {
            var pending = new PendingEncounter { Kind = EncounterKind.Raid, Source = raider.Id, Target = victim.Id, Sector = sector.Id, DueTick = u.Tick + u.Rules.PlayerWarningTicks };
            u.PendingEncounters.Add(pending);
            u.Events.Publish(new EncounterWarning(EncounterKind.Raid, sector.Id, raider.Id, victim.Id, pending.DueTick,
                $"Piraten! {u.ActorName(raider.Owner)} nimmt Kurs auf dich — andocken oder den Sektor verlassen ({u.Rules.PlayerWarningTicks / SimClock.TicksPerSecond:0} s)."));
            return;
        }
        ResolveRaid(u, raider, victim, sector);
    }

    /// <summary>
    /// Löst einen Überfall auf (auch für Tests): Streifen der Sektorfraktion greifen mit Chance ein; Sieg des Angreifers = Fracht anteilig weg (Hehlerei in
    /// die Kasse), Schaden, selten Verlust des Opfers (nie der Spieler); Niederlage = das Kaperschiff flieht oder geht verloren (Kopfgeld für den
    /// Spieler, wenn er es war). Bedrohung und Fahndung steigen. Liefert das Ereignis.
    /// </summary>
    public static RaidResolved ResolveRaid(Universe u, Ship raider, Ship victim, Sector sector)
    {
        var r = u.Rules;
        bool player = victim.Id == u.PlayerShip;   // aktives Schiff: Meldung an den Spieler
        bool own = victim.IsPlayer;                // Spielerschiffe gehen nie verloren (Hüllenboden)
        var defenders = new List<Ship> { victim };
        // Phase 12: Eskorten und Wachen (Regel Verteidigen/Angriff) stehen dem Opfer bei
        var escorts = FleetGuards.Escorts(u, victim, sector.Id);
        defenders.AddRange(escorts);
        var police = PoliceIn(u, sector.Id);
        ShipId helper = default;
        if (police.Count > 0 && u.Rng.NextDouble() < MathF.Min(r.PoliceResponseMax, r.PoliceResponseChance * police.Count))
        {
            defenders.Add(police[0]);
            helper = police[0].Id;
        }
        var outcome = u.Encounters.Resolve(u, new[] { raider }, defenders, own ? 0f : r.DestroyChance);
        Func<Ship, float> floor = s => s.IsPlayer ? r.PlayerMinHull : 0.02f;
        float lootValue = 0f, fenced = 0f;
        bool victimDestroyed = false, raiderDestroyed = false;
        var stolen = new List<string>();
        var faction = sector.Owner;
        if (outcome.AttackerWon)
        {
            foreach (var g in victim.Cargo.Keys.OrderBy(k => k.Value).ToList())
            {
                float take = victim.Cargo[g] * outcome.LootFraction;
                if (take <= 0f) continue;
                float left = victim.Cargo[g] - take;
                if (left <= 0.001f) victim.Cargo.Remove(g); else victim.Cargo[g] = left;
                lootValue += take * u.Goods[g].BasePrice;
                stolen.Add($"{take:0} {u.Goods[g].Name}");
            }
            fenced = lootValue * r.FenceShare;
            if (fenced > 0f && u.Actors.TryGetValue(raider.Owner, out var pirates))
            {
                pirates.Treasury.Credits += fenced;
                u.Events.Publish(new TreasuryIncome(pirates.Id, "fence", fenced));
            }
            CombatRules.Damage(defenders, outcome.DefenderDamage, floor);
            CombatRules.Damage(new[] { raider }, outcome.AttackerDamage);
            victimDestroyed = !own && (outcome.LoserDestroyed || victim.Hull <= 0f);
            ThreatMap.Report(u, sector.Id, r.RaidThreat);
        }
        else
        {
            CombatRules.Damage(defenders, outcome.DefenderDamage, floor);
            CombatRules.Damage(new[] { raider }, outcome.AttackerDamage);
            // gegen den Spieler oder ein Handelsschiff allein flieht ein unterlegener Pirat meist; mit Streife geht er eher verloren
            float destroyBase = helper.Value != 0 || escorts.Count > 0 ? r.RaiderDestroyChance : r.RaiderDestroyChance * 0.5f;
            raiderDestroyed = raider.Hull <= 0f || u.Rng.NextDouble() < destroyBase;
            ThreatMap.Report(u, sector.Id, r.RepelledThreat);
        }
        Wanted.Report(u, raider.Owner, faction, "piracy");
        string msg;
        if (player)
        {
            msg = outcome.AttackerWon
                ? $"Überfall durch {u.ActorName(raider.Owner)}: {(stolen.Count > 0 ? string.Join(", ", stolen) : "keine Fracht")} verloren, Hülle {victim.Hull / victim.MaxHull:P0}."
                : raiderDestroyed ? $"Überfall abgewehrt — das Kaperschiff ist zerstört." : "Überfall abgewehrt — die Piraten fliehen.";
        }
        else msg = outcome.AttackerWon ? $"Überfall auf Schiff {victim.Id.Value}: Beute {lootValue:0} Cr" : $"Überfall auf Schiff {victim.Id.Value} abgewehrt";
        if (raiderDestroyed && (own || escorts.Any(e => e.IsPlayer)))
        {
            float bounty = Wanted.BountyOn(u, raider.Owner, faction);
            var claimant = own ? victim.Owner : escorts.First(e => e.IsPlayer).Owner;
            if (bounty > 0f && u.Actors.TryGetValue(claimant, out var hunter))
            {
                hunter.BountyClaims[faction] = hunter.BountyClaims.GetValueOrDefault(faction) + bounty;
                u.Events.Publish(new BountyEarned(hunter.Id, faction, raider.Id, bounty));
                msg += $" Kopfgeld {bounty:0} Cr beim Kopfgeldbüro abholen.";
            }
        }
        var ev = new RaidResolved(sector.Id, raider.Id, victim.Id, outcome.AttackerWon, lootValue, fenced, victimDestroyed, raiderDestroyed, helper, player, msg);
        u.Events.Publish(ev);
        if (victimDestroyed) u.RemoveShip(victim.Id, "raid");
        if (raiderDestroyed) u.RemoveShip(raider.Id, "destroyed");
        else GoHome(u, raider);   // mit Beute zur Hehlerei bzw. nach der Niederlage zur Reparatur
        return ev;
    }

    /// <summary>Eine Streife im Sektor stellt das lauernde Kaperschiff (Chance je Tick und Streife). true = es kam zur Begegnung.</summary>
    static bool TryIntercept(Universe u, Ship raider, int dt)
    {
        var police = PoliceIn(u, raider.Sector);
        var hunters = FleetGuards.Hunters(u, raider.Sector);   // Phase 12: Streifen/Wachen mit Regel „Angriff“
        int n = police.Count + hunters.Count;
        if (n == 0) return false;
        if (u.Rng.NextDouble() >= u.Rules.InterceptRatePerTick * dt * n) return false;
        Intercept(u, police.Count > 0 ? police[0] : hunters[0], raider);
        return true;
    }

    /// <summary>Begegnung Streife gegen Kaperschiff (auch für Tests): Sieg der Polizei = Kaperschiff zerstört oder flieht beschädigt; sonst wird die Streife beschädigt.</summary>
    public static RaiderIntercepted Intercept(Universe u, Ship police, Ship raider)
    {
        var r = u.Rules;
        var outcome = u.Encounters.Resolve(u, new[] { police }, new[] { raider }, r.RaiderDestroyChance);
        CombatRules.Damage(new[] { police }, outcome.AttackerDamage, s => s.IsPlayer ? r.PlayerMinHull : 0.05f);
        CombatRules.Damage(new[] { raider }, outcome.DefenderDamage);
        bool destroyed = outcome.AttackerWon && (outcome.LoserDestroyed || raider.Hull <= 0f);
        var sector = raider.Sector;
        if (outcome.AttackerWon && u.Sectors.TryGetValue(sector, out var sec)) sec.Threat = MathF.Max(0f, sec.Threat - r.RepelledThreat);
        var ev = new RaiderIntercepted(sector, police.Id, raider.Id, outcome.AttackerWon, destroyed);
        u.Events.Publish(ev);
        if (destroyed && police.IsPlayer && Wanted.BountyOn(u, raider.Owner, u.Sectors.TryGetValue(sector, out var bs) ? bs.Owner : default) is var bounty and > 0f
            && u.Actors.TryGetValue(police.Owner, out var hunter) && u.Sectors.TryGetValue(sector, out var hs))
        {
            // Phase 12: Kopfgeld für die Jagd der Spielerflotte (wie beim abgewehrten Überfall)
            hunter.BountyClaims[hs.Owner] = hunter.BountyClaims.GetValueOrDefault(hs.Owner) + bounty;
            u.Events.Publish(new BountyEarned(hunter.Id, hs.Owner, raider.Id, bounty));
        }
        if (destroyed) u.RemoveShip(raider.Id, "destroyed");
        else if (outcome.AttackerWon) GoHome(u, raider);
        return ev;
    }

    // ───────── Vorwarnung an den Spieler ─────────

    static void ResolvePending(Universe u)
    {
        if (u.PendingEncounters.Count == 0) return;
        foreach (var e in u.PendingEncounters.Where(e => e.Kind == EncounterKind.Raid && u.Tick >= e.DueTick).ToList())
        {
            u.PendingEncounters.Remove(e);
            if (!u.Ships.TryGetValue(e.Source, out var raider) || raider.Transit is not null || raider.Sector != e.Sector)
            {
                u.Events.Publish(new EncounterEscaped(EncounterKind.Raid, e.Source, e.Target, "Die Piraten sind abgedreht."));
                continue;
            }
            if (!u.Ships.TryGetValue(e.Target, out var player) || player.Id != u.PlayerShip || player.Sector != e.Sector || u.PlayerSector != e.Sector
                || ThreatMap.NearStation(u, e.Sector, player.Position))
            {
                u.Events.Publish(new EncounterEscaped(EncounterKind.Raid, e.Source, e.Target, "Du bist den Piraten entkommen."));
                GoHome(u, raider);
                continue;
            }
            ResolveRaid(u, raider, player, u.Sectors[e.Sector]);
        }
    }

    // ───────── Kasse: Unterhalt und Neubau ─────────

    static void Book(Universe u, int ticks)
    {
        var r = u.Rules;
        foreach (var a in u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw).OrderBy(a => a.Id.Value).ToList())
        {
            var raiders = u.Ships.Values.Where(s => ThreatMap.IsRaider(s) && s.Owner == a.Id).ToList();
            float upkeep = r.RaiderUpkeepPerTick * raiders.Count * ticks;
            if (upkeep > 0f)
            {
                float paid = MathF.Min(upkeep, MathF.Max(0f, a.Treasury.Credits));
                a.Treasury.Credits -= paid;
                u.Events.Publish(new TreasuryUpkeep(a.Id, "pirates", paid, upkeep - paid));
            }
            var hideout = u.Stations.Values.Where(st => st.Owner == a.Id && IsHideout(u, st)).OrderBy(st => st.Id.Value).FirstOrDefault();
            // Phase 13 (Korrektur): eine ausgelöschte Flotte fängt neu an — kleine Einnahme aus dem Versteck und halber Preis für das erste Kaperschiff
            if (hideout is not null && raiders.Count == 0 && r.PirateIdleIncomePerTick > 0f)
            {
                float income = r.PirateIdleIncomePerTick * ticks;
                a.Treasury.Credits += income;
                u.Events.Publish(new TreasuryIncome(a.Id, "hideout", income));
            }
            float cost = raiders.Count == 0 ? r.RaiderCost * Math.Clamp(r.RaiderRestartCostFactor, 0f, 1f) : r.RaiderCost;
            if (hideout is null || raiders.Count >= r.MaxRaidersPerActor || a.Treasury.Credits < cost || u.Tick - a.LastForceTick < r.RaiderRebuildTicks) continue;
            if (!u.ShipModels.TryGetValue(r.PirateModel, out var model) || !u.ShipClasses.TryGetValue(model.ClassKey, out var cls)) continue;
            var ship = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, ShipRole.Pirate, Controller.Ai, a.Id, hideout, 0f);
            ship.HomeBase = hideout.Id;
            ship.NextThinkTick = u.Tick + r.PirateRestTicks;
            a.Treasury.Credits -= cost;
            a.LastForceTick = u.Tick;
            u.AddShip(ship);
            u.Events.Publish(new TreasuryUpkeep(a.Id, "pirates", cost, 0f));
            u.Events.Publish(new RaiderBuilt(a.Id, ship.Id, cost));
        }
    }
}
