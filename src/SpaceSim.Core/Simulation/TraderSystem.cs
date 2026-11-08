using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Händler-KI: sucht die beste Handelsspanne, reist abstrakt (Fahrplan) und handelt bei Ankunft.</summary>
public sealed class TraderSystem : ISimSystem
{
    /// <summary>Ticks, die ein Schiff ohne Auftrag wartet, bevor es erneut nach Geschäften sucht.</summary>
    public const int ThinkInterval = 10;

    public void Step(Universe u, int dt)
    {
        foreach (var ship in u.Ships.Values)
        {
            if (ship.Role != ShipRole.Trader) continue;

            if (ship.Transit is { } t)
            {
                if (u.Tick >= t.ArrivalTick) Arrive(u, ship, t);
            }
            else if (ship.Order is null && u.Tick >= ship.NextThinkTick)
            {
                if (!PickOrder(u, ship))
                {
                    ship.NextThinkTick = u.Tick + ThinkInterval + ship.Id.Value % 7;
                    if (ship.IdleSince < 0) ship.IdleSince = u.Tick;
                }
            }
        }
    }

    static bool PickOrder(Universe u, Ship ship)
    {
        TradeOrder? best = null;
        float bestMargin = 0f;
        foreach (var good in u.Goods.Values)
            foreach (var buy in u.Stations.Values)
            {
                if (!buy.Market.TryGetValue(good.Id, out var b) || b.Amount < 1f) continue;
                if (!u.IsReachable(ship.Sector, buy.Sector)) continue;
                foreach (var sell in u.Stations.Values)
                {
                    if (sell.Id == buy.Id || !sell.Market.TryGetValue(good.Id, out var s)) continue;
                    float margin = s.Price - b.Price;
                    if (margin > bestMargin && u.IsReachable(buy.Sector, sell.Sector))
                    {
                        bestMargin = margin;
                        best = new TradeOrder(buy.Id, sell.Id, good.Id);
                    }
                }
            }
        if (best is null) return false;
        ship.Order = best;
        ship.Phase = TradePhase.GoingToBuy;
        ship.IdleSince = -1;
        Travel.Start(u, ship, u.Stations[best.Buy].Sector, u.Stations[best.Buy].DockPoint(ship.Id));
        return true;
    }

    static void Arrive(Universe u, Ship ship, Transit t)
    {
        u.FinishTransit(ship);
        if (ship.Order is not TradeOrder order || !u.Stations.ContainsKey(order.Buy) || !u.Stations.ContainsKey(order.Sell))
        {
            ship.Order = null; ship.Phase = TradePhase.Idle; ship.Cargo.Clear();
            return;
        }

        if (ship.Phase == TradePhase.GoingToBuy)
        {
            var st = u.Stations[order.Buy];
            var stock = st.Stock(order.Good);
            var good = u.Goods[order.Good];
            float free = ship.CargoCapacity - ship.Cargo.Sum(kv => kv.Value * u.Goods[kv.Key].VolumePerUnit);
            float amount = MathF.Min(MathF.Min(free / good.VolumePerUnit, stock.Amount),
                                     stock.Price > 0 ? ship.Credits / stock.Price : 0f);
            if (amount > 0)
            {
                float cost = amount * stock.Price;
                stock.Amount -= amount;
                st.Credits += cost;
                ship.Credits -= cost;
                ship.Cargo[order.Good] = ship.Cargo.GetValueOrDefault(order.Good) + amount;
                ship.Phase = TradePhase.GoingToSell;
                Travel.Start(u, ship, u.Stations[order.Sell].Sector, u.Stations[order.Sell].DockPoint(ship.Id));
            }
            else { ship.Order = null; ship.Phase = TradePhase.Idle; ship.IdleSince = u.Tick; }
        }
        else if (ship.Phase == TradePhase.GoingToSell)
        {
            var st = u.Stations[order.Sell];
            var stock = st.Stock(order.Good);
            float amount = ship.Cargo.GetValueOrDefault(order.Good);
            float revenue = amount * stock.Price;
            stock.Amount += amount;
            st.Credits -= revenue;
            ship.Credits += revenue;
            ship.Cargo.Remove(order.Good);
            ship.Order = null;
            ship.Phase = TradePhase.Idle;
            ship.IdleSince = u.Tick;
            u.Events.Publish(new TradeCompleted(ship.Id, order.Buy, order.Sell, order.Good, amount, revenue));
        }
    }
}
