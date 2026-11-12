using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 26 (KAMPF.md 3.3, 4.5): Schäden werden bezahlt repariert — die Gratis-Regeneration liegender NPC-Schiffe entfällt (mit
/// <see cref="EconomyRules.DamageActive"/>).
/// <list type="bullet">
/// <item><b>Händler und Schürfer</b> (eigene Kasse) reparieren über die gemeinsame Dienstregel <see cref="RepairService.Perform"/> (Material aus dem Lager,
/// Geld an die Station, höchstens <see cref="EconomyRules.NpcRepairBudgetShare"/> ihrer Kasse; leichte Schäden nur aus dem Geld über
/// <see cref="EconomyRules.NpcRepairReserve"/>): bei jeder Ankunft an einer Station mit „repair“
/// (<see cref="OnArrival"/>) und — liegen sie dort — über <see cref="NpcRepairSystem"/>. Schwer beschädigte (Hülle unter <see cref="EconomyRules.NpcRepairSeekHull"/>,
/// ausgefallenes Modul oder Brand) fliegen untätig die nächste erreichbare Reparaturstation an (<see cref="GoToOrder"/>, wie ein Tankstopp).</item>
/// <item><b>Polizei, Piraten und Fraktionsbauschiffe</b> reparieren an der Heimatbasis bzw. über die Fraktion aus der Kasse des Besitzers
/// (<see cref="AtBase"/>, wie <see cref="PirateSystem.ServiceAtBase"/>).</item>
/// <item>Spielerschiffe: Dienst „repair“ an der Station; Flottenschiffe mit Befehl über die Notfall-Ebene (<see cref="Orders.ReturnExecutor.Service"/>).</item>
/// </list>
/// Reparaturkosten sind Geschäftsergebnis (Rentabilitätsmessung Phase 22: sie mindern die Kasse, nichts wird neutralisiert).
/// </summary>
public static class NpcRepair
{
    /// <summary>Schwer beschädigt: Hülle unter der Suchschwelle, ein ausgefallenes Modul oder ein Brand.</summary>
    public static bool Serious(Universe u, Ship ship)
    {
        if (ship.Systems.Burning.Count > 0 || ship.Hull < u.Rules.NpcRepairSeekHull * ship.MaxHull) return true;
        for (int i = 0; i < ship.Fit.Count; i++) if (Combat.ModuleDamage.Failed(u, ship, i)) return true;
        return false;
    }

    /// <summary>Ausgefallenes Modul oder Brand (Polizei kehrt dann heim).</summary>
    public static bool Crippled(Universe u, Ship ship)
    {
        if (ship.Systems.Burning.Count > 0) return true;
        for (int i = 0; i < ship.Fit.Count; i++) if (Combat.ModuleDamage.Failed(u, ship, i)) return true;
        return false;
    }

    /// <summary>Bietet die Station Reparatur (Typ führt „repair“, Platten auf Lager)?</summary>
    public static bool Repairs(Universe u, Station st) =>
        !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Services.Contains(RepairService.ServiceKey) && RepairService.Available(u, st);

    /// <summary>Repariert ein KI-Schiff mit eigener Kasse an dieser Station (Hülle und alle Module, Budgetanteil). Liefert das Ergebnis.</summary>
    public static RepairOutcome AtStation(Universe u, Ship ship, Station st)
    {
        if (!u.Rules.DamageActive || !RepairService.NeedsRepair(ship) || !Repairs(u, st)) return default;
        float credits = MathF.Max(0f, u.WalletOf(ship).Credits);
        float budget = credits * Math.Clamp(u.Rules.NpcRepairBudgetShare, 0f, 1f);
        if (!Serious(u, ship)) budget = MathF.Min(budget, credits - u.Rules.NpcRepairReserve);   // leichte Schäden nur aus dem Polster
        // ausgefallenes Modul oder Brand: das Schiff ist (teils) arbeitsunfähig (z. B. Frachtraum 0) — Module zuerst und notfalls mit der ganzen Kasse
        bool crippled = Crippled(u, ship);
        if (crippled) budget = credits;
        if (budget <= 0f) return default;
        var slots = RepairService.DamagedSlots(ship);
        if (crippled) slots = slots.OrderBy(s => Combat.ModuleDamage.Failed(u, ship, s) || ship.Systems.IsBurning(s) ? 0 : 1).ThenBy(s => s).ToList();
        var o = RepairService.Perform(u, st, ship, 1f, slots, budget, modulesFirst: crippled);
        if (o.Any) u.Events.Publish(new ServicePerformed(ship.Id, st.Id, RepairService.ServiceKey, "repair:all", RepairService.PlatesGood(u)!.Value, o.Hull + o.Modules, -o.Cost));
        return o;
    }

    /// <summary>Nach der Ankunft eines KI-Schiffs: ist es an einer Station mit Reparatur angedockt, repariert es dort (nur wenn etwas fehlt — kostet sonst nichts).</summary>
    public static void OnArrival(Universe u, Ship ship)
    {
        if (!u.Rules.DamageActive || ship.Controller != Controller.Ai || ship.DockedAt.Value == 0 || !RepairService.NeedsRepair(ship)) return;
        if (u.Stations.TryGetValue(ship.DockedAt, out var st)) AtStation(u, ship, st);
    }

    /// <summary>Nächste erreichbare Station, die jetzt reparieren kann (Reisezeit, dann Id).</summary>
    public static Station? Nearest(Universe u, Ship ship) =>
        u.StationsById().Where(st => Repairs(u, st))
            .Select(st => (st, t: Travel.Estimate(u, ship.Sector, ship.Position, st.Sector, st.DockPoint(ship.Id), ship)))
            .Where(x => !float.IsPositiveInfinity(x.t)).OrderBy(x => x.t).ThenBy(x => x.st.Id.Value).Select(x => x.st).FirstOrDefault();

    /// <summary>
    /// Untätiger, schwer beschädigter Händler/Schürfer: Flug zur nächsten Reparaturstation (Auftrag <see cref="GoToOrder"/>; die Ankunft behandeln
    /// Händler- und Schürfer-System wie einen Tankstopp, repariert wird in <see cref="OnArrival"/>). Nicht, wenn es schon dort liegt oder kein Geld hat.
    /// true = losgeflogen.
    /// </summary>
    public static bool Seek(Universe u, Ship ship)
    {
        if (!u.Rules.DamageActive || !ship.IsEconomyAi || ship.Transit is not null || ship.Order is not null || u.Fights.IsEngaged(ship.Id)) return false;
        if (ship.Credits < 50f || !Serious(u, ship)) return false;
        // Fracht an Bord: erst verkaufen (SellStranded/Auslieferung), dann zur Reparatur — sonst würde die Ankunft die Reparaturstation zur „Einkaufsstation“
        // eines Restverkaufs machen
        if (ship.Cargo.Values.Any(v => v > 0.001f)) return false;
        var st = Nearest(u, ship);
        if (st is null || st.Id == ship.DockedAt) return false;
        ship.Order = new GoToOrder(st.Id);
        if (Travel.Start(u, ship, st.Sector, st.DockPoint(ship.Id)))
        {
            u.Events.Publish(new RepairTripStarted(ship.Id, st.Id, ship.Hull / MathF.Max(1f, ship.MaxHull)));
            return true;
        }
        ship.Order = null;
        return false;
    }

    /// <summary>
    /// Reparatur aus der Kasse des Besitzers (Polizei, Piraten, Fraktionsbauschiffe): Hülle zu <see cref="EconomyRules.BaseRepairPerHull"/> je Punkt, Module zu
    /// <see cref="EconomyRules.BaseModuleRepairCost"/> je Zustandspunkt und Größenstufe; Brände gelöscht. Was die Kasse nicht deckt, bleibt. Ereignisse
    /// <see cref="TreasuryUpkeep"/> (<paramref name="purpose"/>) und <see cref="ShipRepaired"/> (an der Basis). Liefert die Kosten.
    /// </summary>
    public static float AtBase(Universe u, Ship s, Actor owner, string purpose, StationId at)
    {
        var r = u.Rules;
        float spent = 0f, hull = 0f;
        int modules = 0;
        float missing = MathF.Max(0f, s.MaxHull - s.Hull);
        if (missing > 0f && r.BaseRepairPerHull > 0f)
        {
            float fix = MathF.Min(missing, MathF.Max(0f, owner.Treasury.Credits) / r.BaseRepairPerHull);
            s.Hull += fix;
            hull = fix;
            spent += fix * r.BaseRepairPerHull;
            owner.Treasury.Credits -= fix * r.BaseRepairPerHull;
        }
        if (r.DamageActive)
        {
            Combat.ModuleDamage.ExtinguishAll(u, s, "repaired");
            foreach (int slot in RepairService.DamagedSlots(s))
            {
                float points = 1f - s.ConditionOf(slot), per = r.BaseModuleRepairCost * RepairService.SizeFactor(s, slot);
                float can = per > 0f ? MathF.Min(points, MathF.Max(0f, owner.Treasury.Credits) / per) : points;
                if (can <= 0.001f) continue;
                owner.Treasury.Credits -= can * per;
                spent += can * per;
                Combat.ModuleDamage.SetCondition(u, s, slot, s.ConditionOf(slot) + can);
                modules++;
            }
        }
        if (spent > 0f)
        {
            u.Events.Publish(new TreasuryUpkeep(owner.Id, purpose, spent, 0f));
            if (r.DamageActive) u.Events.Publish(new ShipRepaired(s.Id, at, hull, modules, spent, 0f, 0f, true));
        }
        return spent;
    }
}

/// <summary>
/// Phase 26: bezahlte Reparatur der KI-Schiffe (Takt 10). Liegende Händler, Schürfer und Bauschiffe an einer Station mit „repair“ reparieren dort aus ihrer
/// Kasse; schwer beschädigte untätige Händler/Schürfer fliegen eine Reparaturstation an; Fraktionsbauschiffe repariert ihre Fraktion aus der Kasse. Läuft vor
/// Händlern und Schürfern. Ohne Schaden kostet es nur die Prüfung je Schiff.
/// </summary>
public sealed class NpcRepairSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        if (!u.Rules.DamageActive) return;
        foreach (var ship in u.ShipsById())
        {
            if (ship.Controller != Controller.Ai || ship.Transit is not null || ship.Order is not null || u.Fights.IsEngaged(ship.Id)) continue;
            if (ThreatMap.IsPolice(ship) || ThreatMap.IsRaider(ship) || !RepairService.NeedsRepair(ship)) continue;
            if (FactionBuilderSystem.IsFactionBuilder(u, ship))
            {
                // Bauschiffe gehören der Fraktion: sie repariert aus ihrer Kasse (wie Polizei an der Basis), sobald das Schiff nicht arbeitet
                if (u.Actors.TryGetValue(ship.Owner, out var faction)) NpcRepair.AtBase(u, ship, faction, "builder", ship.DockedAt);
                continue;
            }
            if (ship.DockedAt.Value != 0 && u.Stations.TryGetValue(ship.DockedAt, out var st) && NpcRepair.Repairs(u, st))
            {
                NpcRepair.AtStation(u, ship, st);
                if (!NpcRepair.Serious(u, ship)) continue;
            }
            if (ship.IsEconomyAi && ship.Role is ShipRole.Trader or ShipRole.Miner && u.Tick >= ship.NextThinkTick && ship.MinePhase == MinePhase.Idle)
                NpcRepair.Seek(u, ship);
        }
    }
}
