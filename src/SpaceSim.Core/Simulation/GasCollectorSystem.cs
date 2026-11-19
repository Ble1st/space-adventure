using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Gasernte (SCHIFFSSLOTS.md „Gasernte und Nebel“): ausführendes System der KI-Aufgabe „gas“ (<see cref="GasJob"/>, Etikett <see cref="ShipRole.Collector"/>).
/// Ein Gassammler wählt Gasfeld + Abnehmer mit dem besten Wert je Zeit, fliegt ins Feld, schöpft dort mit derselben Regel wie der Spieler
/// (<see cref="GasHarvest.Step"/>), bis der Flüssigraum voll ist, und verkauft an der Abnehmerstation (Gasraffinerie, Schildwerk). Der Auftrag ist ein
/// <see cref="MineOrder"/>, dessen Feldnummer auf <see cref="Sector.GasFields"/> zeigt (−1 = nur ausliefern) — kein neues Speicherformat.
/// Eigenes System statt eines Zweigs im <see cref="MinerSystem"/>: Gas braucht weder Asteroiden noch Kontakt noch Anker; die Asteroidenlogik bleibt unberührt.
/// </summary>
public sealed class GasCollectorSystem : ISimSystem
{
    /// <summary>Mindestwert an der Abnehmerstation relativ zum Basispreis (wie beim Schürfer).</summary>
    public const float MinPriceFactor = 0.5f;
    /// <summary>Abschlag je weiterem Sammler am selben Feld (verteilt die Sammler; das Feld selbst ist unerschöpflich).</summary>
    public const float CrowdPenalty = 0.3f;

    public void Step(Universe u, int dt)
    {
        if (GasHarvest.GasGood(u) is not { } gas) return;
        foreach (var ship in u.Ships.Values)
        {
            if (ship.Role != ShipRole.Collector) continue;
            if (u.Fights.IsEngaged(ship.Id)) continue;
            if (!ship.Has(Capability.GasHarvest)) { JobRelease.Release(u, ship); continue; }
            if (ship.Transit is { } t)
            {
                if (u.Tick >= t.ArrivalTick) Arrive(u, ship);
                continue;
            }
            switch (ship.MinePhase)
            {
                case MinePhase.Idle:
                    if (u.Tick < ship.NextThinkTick) break;
                    if (NpcRepair.Seek(u, ship)) break;
                    if (!PickOrder(u, ship))
                    {
                        if (ship.IdleSince < 0) ship.IdleSince = u.Tick;
                        ship.NextThinkTick = u.Tick + TraderSystem.IdleBackoff(u, ship) + ship.Id.Value % 7;
                    }
                    break;
                case MinePhase.Mining: Collect(u, ship, gas, dt); break;
            }
        }
    }

    /// <summary>
    /// Bester Sammelauftrag für dieses Schiff jetzt (auch die Aufgabenbewertung <see cref="GasJob.Score"/>): Auftrag, Arbeitspunkt im Feld und Bewertung =
    /// Preis einer Einheit an der Abnehmerstation je Tick Umlaufzeit, gemindert durch Gedränge und Risiko. Reine Auswertung.
    /// </summary>
    public static (MineOrder? Order, Vec2 FieldPos, float Score) Best(Universe u, Ship ship)
    {
        if (GasHarvest.GasGood(u) is not { } gas || !GasHarvest.HasTool(ship)) return (null, default, 0f);
        float units = ship.CapacityVolumeFor(GoodForm.Liquid) / MathF.Max(0.01f, gas.VolumePerUnit);
        if (units < 1f) return (null, default, 0f);
        var shipSize = Docking.SizeOf(u, ship);
        var profile = GateAccess.ProfileOf(u, ship);
        var laden = profile with { Laden = true };
        var limits = AutoLimitRules.Of(ship);
        MineOrder? best = null;
        Vec2 bestPos = default;
        float bestScore = 0f;
        foreach (var sector in u.Sectors.Values)
        {
            if (sector.GasFields.Count == 0 || !u.IsReachable(ship.Sector, sector.Id, profile)) continue;
            if (limits is not null && !AutoLimitRules.InRange(u, limits, sector.Id)) continue;
            for (int fi = 0; fi < sector.GasFields.Count; fi++)
            {
                var field = sector.GasFields[fi];
                float collectTicks = units / MathF.Max(0.001f, GasHarvest.Rate(ship, field) * SimClock.TickInterval);
                int crowd = 0;
                foreach (var other in u.Ships.Values)
                    if (other != ship && other.Role == ShipRole.Collector && other.Order is MineOrder mo && mo.Sector == sector.Id && mo.Field == fi) crowd++;
                float crowding = 1f / (1f + CrowdPenalty * crowd);
                var work = field.WorkPoint(ship.Id);
                float toField = Travel.Estimate(u, ship.Sector, ship.Position, sector.Id, work, ship);
                if (float.IsPositiveInfinity(toField)) continue;
                foreach (var st in u.Stations.Values)
                {
                    if (!st.Market.TryGetValue(gas.Id, out var e) || e.Price <= 0f || e.NoIntake) continue;
                    if (st.Site is not null || st.AcceptableUnits(gas.Id, u.Rules) < 5f) continue;
                    if (limits is not null && !AutoLimitRules.InRange(u, limits, st.Sector)) continue;
                    if (!Docking.Allows(st, shipSize)) continue;
                    if (e.Price < gas.BasePrice * MinPriceFactor) continue;
                    float sell = MathF.Min(units, st.AcceptableUnits(gas.Id, u.Rules));
                    if (!TraderSystem.CanAfford(st, e.Price, sell)) continue;
                    if (!u.IsReachable(sector.Id, st.Sector, laden)) continue;
                    float time = toField + collectTicks + Travel.Estimate(u, sector.Id, work, st.Sector, st.DockPoint(ship.Id), ship, laden);
                    float risk = MathF.Min(0.5f, ThreatMap.RiskOf(u, sector.Id) + ThreatMap.RouteRisk(u, sector.Id, st.Sector));
                    float score = e.Price * (sell / units) / MathF.Max(1f, time) * crowding * (1f - risk);
                    if (score > bestScore) { bestScore = score; bestPos = work; best = new MineOrder(sector.Id, fi, st.Id); }
                }
            }
        }
        return (best, bestPos, bestScore);
    }

    static bool PickOrder(Universe u, Ship ship)
    {
        var (best, pos, _) = Best(u, ship);
        if (best is null) return false;
        long idleBefore = ship.IdleSince;
        ship.IdleSince = -1;
        ship.Order = best;
        ship.MinePhase = MinePhase.GoingToField;
        var plan = Refuel.PrepareTrip(u, ship, best.Sector, pos);
        if (plan == Refuel.TripPlan.FuelStop) return true;
        if (plan == Refuel.TripPlan.Stuck)
        {
            ship.Order = null; ship.MinePhase = MinePhase.Idle; ship.IdleSince = idleBefore >= 0 ? idleBefore : u.Tick;
            return false;
        }
        Travel.Start(u, ship, best.Sector, pos);
        return true;
    }

    static void Arrive(Universe u, Ship ship)
    {
        u.FinishTransit(ship);
        NpcRepair.OnArrival(u, ship);
        if (ship.Order is GoToOrder g && u.Stations.TryGetValue(g.Target, out var fuelSt))
        {
            Refuel.TankUp(u, ship, fuelSt);
            ship.Order = null;
            if (ship.MinePhase == MinePhase.GoingToStation && ship.Cargo.Values.Sum() > 0.001f && TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position) is { } next)
            {
                ship.Order = new MineOrder(ship.Sector, -1, next.Id);
                if (Deliver(u, ship, next)) return;
            }
            Idle(u, ship);
            return;
        }
        if (ship.Order is not MineOrder order || !u.Stations.TryGetValue(order.DeliverTo, out var target)) { ship.Order = null; Idle(u, ship); return; }
        if (ship.MinePhase == MinePhase.GoingToField)
        {
            if (order.Field < 0) { ship.MinePhase = MinePhase.GoingToStation; if (!Deliver(u, ship, target)) Idle(u, ship); return; }
            ship.MinePhase = MinePhase.Mining;
            return;
        }
        if (ship.MinePhase != MinePhase.GoingToStation) { Idle(u, ship); return; }
        TradeActions.SellAll(u, ship, target);
        if (ship.Cargo.Values.Sum() > 0.001f)
        {
            // Rest, den diese Station nicht nimmt: nächster Abnehmer, sonst über Bord (wie beim Schürfer)
            if (TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position, exclude: target.Id) is { } next)
            {
                ship.Order = order with { DeliverTo = next.Id };
                if (Deliver(u, ship, next)) return;
            }
            ship.Cargo.Clear();
        }
        ship.Order = null;
        Idle(u, ship);
    }

    static void Idle(Universe u, Ship ship)
    {
        ship.MinePhase = MinePhase.Idle;
        if (ship.Order is not GoToOrder) ship.Order = null;
        ship.IdleSince = u.Tick;
    }

    /// <summary>Fahrt zur Abnehmerstation antreten (Treibstoff sichern). false = liegen geblieben.</summary>
    static bool Deliver(Universe u, Ship ship, Station st)
    {
        ship.MinePhase = MinePhase.GoingToStation;
        var plan = Refuel.PrepareTrip(u, ship, st.Sector, st.DockPoint(ship.Id));
        if (plan == Refuel.TripPlan.FuelStop) return true;
        if (plan == Refuel.TripPlan.Stuck) { ship.Order = null; return false; }
        Travel.Start(u, ship, st.Sector, st.DockPoint(ship.Id));
        return true;
    }

    static void Collect(Universe u, Ship ship, Good gas, int dt)
    {
        if (ship.Order is not MineOrder order || !u.Stations.TryGetValue(order.DeliverTo, out var target)) { ship.Order = null; Idle(u, ship); return; }
        var sector = u.Sectors[ship.Sector];
        var field = order.Field >= 0 && order.Field < sector.GasFields.Count && order.Sector == ship.Sector ? sector.GasFields[order.Field] : null;
        bool full = ship.FreeUnits(u, gas.Id) < 0.5f;
        if (field is null || full || !field.Contains(ship.Position))
        {
            if (ship.Cargo.Values.Sum() > 0.001f) { if (!Deliver(u, ship, target)) Idle(u, ship); }
            else { ship.Order = null; Idle(u, ship); }
            return;
        }
        GasHarvest.Step(u, ship, ship.Position, SimClock.TickInterval * dt);
    }
}
