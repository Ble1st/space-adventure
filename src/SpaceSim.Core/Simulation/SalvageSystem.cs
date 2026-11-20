using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 4 (NPC-Bergung, docs/KAMPF.md „NPC-Bergung“): ausführendes System der KI-Aufgabe „salvage“ (<see cref="SalvageJob"/>, Etikett
/// <see cref="ShipRole.Salvager"/>), Intervall 10. Ein KI-Bergeschiff fliegt zum nächsten erreichbaren Wrack mit bergbarem Gut (Fracht, die in den
/// Laderaum passt, oder Module ab <see cref="EconomyRules.SalvageMinCondition"/>), holt es per <see cref="Wrecks.TakeCargo"/>/<see cref="Wrecks.TakeModule"/>
/// und verkauft die Beute: Modulware an die beste Abnehmerin der wertvollsten Modulware (<see cref="TradeActions.BestBuyerFor"/> — Werft/Ausrüster),
/// sonstige Ladung an den besten Abnehmer (<see cref="TradeActions.BestBuyer"/>), Rest an den jeweils nächsten Abnehmer, unverwertbarer Rest über Bord
/// (wie beim Schürfer). Reisen über <see cref="Travel.Start"/> mit <see cref="Refuel.PrepareTrip"/> wie <see cref="GasCollectorSystem"/>; der Auftrag ist ein
/// <see cref="MineOrder"/>, dessen Feldnummer die Wrack-Id trägt (−1 = nur ausliefern) — kein neues Speicherformat. Deterministisch (Schiffe und Wracks in
/// Id-Reihenfolge, feste Entscheidungen), kein <see cref="Universe.Rng"/>. Hauptschalter <see cref="EconomyRules.NpcSalvage"/>: aus = nichts passiert.
/// </summary>
public sealed class SalvageSystem : ISimSystem
{
    /// <summary>Intervall des Systems (Ticks).</summary>
    public const int Interval = 10;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.NpcSalvage) return;
        foreach (var ship in u.ShipsById())
        {
            if (ship.Role != ShipRole.Salvager || ship.Controller != Controller.Ai) continue;
            if (u.Fights.IsEngaged(ship.Id)) continue;
            if (!ship.Has(Capability.Salvage) || !ship.Has(Capability.Cargo)) { JobRelease.Release(u, ship); continue; }
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
                    Plan(u, ship);
                    break;
                case MinePhase.Mining:
                    Work(u, ship);
                    break;
            }
        }
    }

    /// <summary>
    /// Nächstes erreichbares Wrack mit bergbarem Gut für dieses Schiff jetzt (nach Reisezeit, Gleichstand kleinste Id — wie
    /// <see cref="SalvageExecutor.Next"/>); die Bewertung (Aufgabe <see cref="SalvageJob.Score"/>) ist der Basiswert der bergbaren Beute je Tick
    /// Reisezeit. Reine Auswertung, Wracks in Id-Reihenfolge.
    /// </summary>
    public static (Wreck? Wreck, float Score) Best(Universe u, Ship ship)
    {
        Wreck? best = null;
        float bestTime = float.MaxValue, bestScore = 0f;
        var profile = GateAccess.ProfileOf(u, ship);
        var limits = AutoLimitRules.Of(ship);
        foreach (var w in u.Wrecks)
        {
            if (!SalvageExecutor.Worth(u, ship, w)) continue;
            if (limits is not null && !AutoLimitRules.InRange(u, limits, w.Sector)) continue;
            if (!u.IsReachable(ship.Sector, w.Sector, profile)) continue;
            float time = Travel.Estimate(u, ship.Sector, ship.Position, w.Sector, w.Position, ship);
            if (float.IsPositiveInfinity(time)) continue;
            if (time < bestTime - 1e-4f || (MathF.Abs(time - bestTime) <= 1e-4f && best is not null && w.Id < best.Id))
            {
                bestTime = time;
                best = w;
                bestScore = LootValue(u, ship, w) / (time + 10f);
            }
        }
        return (best, bestScore);
    }

    /// <summary>Zahl der Wracks mit bergbarem Gut (Fracht oder Modul ab <see cref="EconomyRules.SalvageMinCondition"/>) — der Bedarf an Bergeschiffen.</summary>
    public static int Demand(Universe u)
    {
        int n = 0;
        foreach (var w in u.Wrecks)
            if (w.Cargo.Values.Any(v => v >= 1f) || w.Modules.Any(m => m.Condition >= u.Rules.SalvageMinCondition)) n++;
        return n;
    }

    /// <summary>Basispreis der bergbaren Beute eines Wracks für dieses Schiff (Fracht, die passt, plus bergbare Module je 1 Einheit).</summary>
    static float LootValue(Universe u, Ship ship, Wreck w)
    {
        float value = 0f;
        foreach (var g in w.Cargo.Keys.OrderBy(k => k.Value))
        {
            float take = MathF.Min(w.Cargo[g], ship.FreeUnits(u, g));
            if (take > 0f && u.Goods.TryGetValue(g, out var good)) value += take * good.BasePrice;
        }
        foreach (var m in w.Modules)
            if (m.Condition >= u.Rules.SalvageMinCondition && u.FindGood(m.Key) is { } good && ship.FreeUnits(u, good.Id) >= 1f)
                value += good.BasePrice;
        return value;
    }

    /// <summary>
    /// Abnehmer für die Beute an Bord: Modulware geht an die beste Abnehmerin der wertvollsten Modulware (<see cref="TradeActions.BestBuyerFor"/>,
    /// das sind Werft/Ausrüster), sonst an den besten Abnehmer der ganzen Ladung (<see cref="TradeActions.BestBuyer"/>).
    /// </summary>
    static Station? BuyerFor(Universe u, Ship ship, StationId? exclude)
    {
        var limits = AutoLimitRules.Of(ship);
        Func<Station, bool>? allowed = limits is null ? null : st => AutoLimitRules.InRange(u, limits, st.Sector);
        GoodId bestGood = default;
        float bestVal = 0f;
        foreach (var g in ship.Cargo.Keys.OrderBy(k => k.Value))
        {
            float n = ship.Cargo[g];
            if (n < 1f || !u.Goods.TryGetValue(g, out var good) || !u.Modules.ContainsKey(good.Key)) continue;
            if (n * good.BasePrice > bestVal) { bestVal = n * good.BasePrice; bestGood = g; }
        }
        if (bestVal > 0f && TradeActions.BestBuyerFor(u, ship, bestGood, ship.Cargo[bestGood], exclude ?? default, allowed) is { } st) return st;
        return TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position, exclude);
    }

    /// <summary>Entscheidung im Leerlauf: Beute verkaufen, sonst zum nächsten Wrack, sonst warten.</summary>
    static void Plan(Universe u, Ship ship)
    {
        float loot = ship.Cargo.Values.Sum();
        bool full = ship.CargoCapacity - ship.UsedVolume(u) < 1f;
        if (loot > 0.001f || full)
        {
            if (BuyerFor(u, ship, ship.DockedAt.Value != 0 ? ship.DockedAt : null) is { } buyer)
            {
                ship.Order = new MineOrder(ship.Sector, -1, buyer.Id);
                ship.MinePhase = MinePhase.GoingToStation;
                ship.IdleSince = -1;
                if (Deliver(u, ship, buyer)) return;
                ship.Order = null;
                ship.MinePhase = MinePhase.Idle;
            }
            else ship.Cargo.Clear();   // Beute, die niemand nimmt: über Bord (wie beim Schürfer)
        }
        if (Best(u, ship).Wreck is { } w)
        {
            long idleBefore = ship.IdleSince;
            ship.IdleSince = -1;
            ship.Order = new MineOrder(w.Sector, w.Id, default);
            ship.MinePhase = MinePhase.GoingToField;
            var plan = Refuel.PrepareTrip(u, ship, w.Sector, w.Position);
            if (plan == Refuel.TripPlan.FuelStop) return;
            if (plan == Refuel.TripPlan.Stuck)
            {
                ship.Order = null;
                ship.MinePhase = MinePhase.Idle;
                ship.IdleSince = idleBefore >= 0 ? idleBefore : u.Tick;
                ship.NextThinkTick = u.Tick + TraderSystem.IdleBackoff(u, ship) + ship.Id.Value % 7;
                return;
            }
            Travel.Start(u, ship, w.Sector, w.Position);
            return;
        }
        if (ship.IdleSince < 0) ship.IdleSince = u.Tick;
        ship.NextThinkTick = u.Tick + TraderSystem.IdleBackoff(u, ship) + ship.Id.Value % 7;
    }

    /// <summary>Am Wrack angekommen: Fracht und Module bergen, dann weiter planen (nächstes Wrack oder Verkauf).</summary>
    static void Work(Universe u, Ship ship)
    {
        if (ship.Order is not MineOrder order || Wrecks.Get(u, order.Field) is not { } wreck || !u.Wrecks.Contains(wreck))
        {
            ship.Order = null;
            ship.MinePhase = MinePhase.Idle;
            Plan(u, ship);
            return;
        }
        if (ship.Sector != wreck.Sector || Vec2.Distance(ship.Position, wreck.Position) > u.Rules.SalvageRange)
        {
            // nicht (mehr) in Reichweite: wieder anfliegen
            var plan = Refuel.PrepareTrip(u, ship, wreck.Sector, wreck.Position);
            if (plan == Refuel.TripPlan.Depart) Travel.Start(u, ship, wreck.Sector, wreck.Position);
            else if (plan == Refuel.TripPlan.Stuck) { ship.Order = null; ship.MinePhase = MinePhase.Idle; Plan(u, ship); }
            return;
        }
        ship.DockedAt = default;   // neben einer Station gelandet: das Wrack zählt, nicht der Liegeplatz (wie der Spieler-Executor)
        Wrecks.TakeCargo(u, ship, wreck, float.MaxValue, out _);
        for (int guard = 0; guard < 16 && u.Wrecks.Contains(wreck); guard++)
        {
            int i = Wrecks.BestModule(u, ship, wreck);
            if (i < 0 || !Wrecks.TakeModule(u, ship, wreck, i, out _)) break;
        }
        bool wreckLeft = u.Wrecks.Contains(wreck) && SalvageExecutor.Worth(u, ship, wreck);
        bool holdFull = ship.CargoCapacity - ship.UsedVolume(u) < 1f;
        if (!wreckLeft || holdFull)
        {
            ship.Order = null;
            ship.MinePhase = MinePhase.Idle;
            Plan(u, ship);
        }
        // sonst: Wrack hat noch Beute und der Laderaum Platz — beim nächsten Intervall weiter bergen
    }

    static void Arrive(Universe u, Ship ship)
    {
        u.FinishTransit(ship);
        NpcRepair.OnArrival(u, ship);
        if (ship.Order is GoToOrder g && u.Stations.TryGetValue(g.Target, out var fuelSt))
        {
            Refuel.TankUp(u, ship, fuelSt);
            ship.Order = null;
            if (ship.MinePhase == MinePhase.GoingToStation && ship.Cargo.Values.Sum() > 0.001f && BuyerFor(u, ship, null) is { } next)
            {
                ship.Order = new MineOrder(ship.Sector, -1, next.Id);
                if (Deliver(u, ship, next)) return;
            }
            Idle(u, ship);
            return;
        }
        if (ship.Order is not MineOrder order) { Idle(u, ship); return; }
        if (ship.MinePhase == MinePhase.GoingToField)
        {
            ship.MinePhase = MinePhase.Mining;
            Work(u, ship);
            return;
        }
        if (ship.MinePhase == MinePhase.GoingToStation && u.Stations.TryGetValue(order.DeliverTo, out var target))
        {
            TradeActions.SellAll(u, ship, target);
            if (ship.Cargo.Values.Sum() > 0.001f)
            {
                // Rest, den diese Station nicht nimmt: nächster Abnehmer, sonst über Bord (wie beim Schürfer)
                if (BuyerFor(u, ship, target.Id) is { } next)
                {
                    ship.Order = order with { DeliverTo = next.Id };
                    if (Deliver(u, ship, next)) return;
                }
                ship.Cargo.Clear();
            }
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
        if (plan == Refuel.TripPlan.Stuck) { ship.Order = null; ship.MinePhase = MinePhase.Idle; return false; }
        Travel.Start(u, ship, st.Sector, st.DockPoint(ship.Id));
        return true;
    }
}
