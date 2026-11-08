using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Händler-KI: sucht die beste Handelsspanne, reist abstrakt (Fahrplan) und handelt bei Ankunft.</summary>
public sealed class TraderSystem : ISimSystem
{
    public void Step(Universe u)
    {
        foreach (var ship in u.Ships.Values)
        {
            if (ship.Role != ShipRole.Trader) continue;

            if (ship.Transit is { } t)
            {
                if (u.Tick >= t.ArrivalTick) Arrive(u, ship, t);
            }
            else if (ship.Order is null)
            {
                PickOrder(u, ship);
            }
        }
    }

    static void PickOrder(Universe u, Ship ship)
    {
        TradeOrder? best = null;
        float bestMargin = 0f;
        foreach (var good in u.Goods.Values)
            foreach (var buy in u.Stations.Values)
            {
                if (!buy.Market.TryGetValue(good.Id, out var b) || b.Amount < 1f) continue;
                foreach (var sell in u.Stations.Values)
                {
                    if (sell.Id == buy.Id || !sell.Market.TryGetValue(good.Id, out var s)) continue;
                    float margin = s.Price - b.Price;
                    if (margin > bestMargin && u.Hops(buy.Sector, sell.Sector) >= 0)
                    {
                        bestMargin = margin;
                        best = new TradeOrder(buy.Id, sell.Id, good.Id);
                    }
                }
            }
        if (best is null) return;
        ship.Order = best;
        ship.Phase = TradePhase.GoingToBuy;
        Travel.Start(u, ship, u.Stations[best.Buy].Sector);
    }

    static void Arrive(Universe u, Ship ship, Transit t)
    {
        u.MoveShip(ship, t.ToSector);
        ship.Transit = null;
        if (ship.Order is not TradeOrder order) { ship.Phase = TradePhase.Idle; return; }

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
                Travel.Start(u, ship, u.Stations[order.Sell].Sector);
            }
            else { ship.Order = null; ship.Phase = TradePhase.Idle; }
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
        }
    }
}
