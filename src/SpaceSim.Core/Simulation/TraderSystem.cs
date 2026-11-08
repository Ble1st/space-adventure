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
                    if (ship.IdleSince < 0) ship.IdleSince = u.Tick;
                    ship.NextThinkTick = u.Tick + IdleBackoff(u, ship) + ship.Id.Value % 7;
                }
            }
        }
    }

    /// <summary>
    /// Bewertet jede Kauf-/Verkaufsstation je Ware nach Gewinn pro Tick (Spanne × Menge / Reisezeit).
    /// Bestände, die andere Händler bereits angefahren haben, gelten als reserviert; sonst würden alle auf dieselbe Route laufen.
    /// </summary>
    /// <summary>
    /// Wartezeit bis zur nächsten Suche: wächst mit der Dauer der Arbeitslosigkeit (bis zum Fünffachen). Wer lange nichts findet,
    /// muss nicht jede Sekunde den ganzen Markt durchsuchen – das spart bei vielen Schiffen den Hauptteil der Rechenzeit.
    /// </summary>
    public static int IdleBackoff(Universe u, Ship ship) =>
        ThinkInterval * (1 + (int)Math.Min(4, Math.Max(0, u.Tick - ship.IdleSince) / 100));

    static bool PickOrder(Universe u, Ship ship)
    {
        var reserved = new Dictionary<(StationId, GoodId), float>();
        foreach (var o in u.Ships.Values)
            if (o.Order is TradeOrder to && o.Phase == TradePhase.GoingToBuy)
                reserved[(to.Buy, to.Good)] = reserved.GetValueOrDefault((to.Buy, to.Good)) + o.CargoCapacity / u.Goods[to.Good].VolumePerUnit;

        TradeOrder? best = null;
        float bestScore = 0f;
        foreach (var good in u.Goods.Values)
        {
            float capacityUnits = ship.CargoCapacity / good.VolumePerUnit;
            var traders = u.StationsTrading(good.Id);
            if (traders.Count < 2) continue;
            foreach (var buy in traders)
            {
                var b = buy.Market[good.Id];
                float available = b.Amount - reserved.GetValueOrDefault((buy.Id, good.Id));
                if (available < 1f) continue;
                float units = MathF.Min(capacityUnits, available);
                float toBuy = Travel.Estimate(u, ship.Sector, ship.Position, buy.Sector, buy.DockPoint(ship.Id));
                if (float.IsPositiveInfinity(toBuy)) continue;
                foreach (var sell in traders)
                {
                    if (sell.Id == buy.Id) continue;
                    var s = sell.Market[good.Id];
                    float margin = s.Price - b.Price;
                    if (margin <= 0f || !CanAfford(sell, s.Price, units)) continue;
                    if (sell.AcceptableUnits(good.Id, u.Rules) < MathF.Min(units, 5f)) continue;   // Lager voll: nimmt nichts mehr
                    float toSell = Travel.Estimate(u, buy.Sector, buy.DockPoint(ship.Id), sell.Sector, sell.DockPoint(ship.Id));
                    if (float.IsPositiveInfinity(toSell)) continue;
                    float score = margin * units / (toBuy + toSell);
                    if (score > bestScore) { bestScore = score; best = new TradeOrder(buy.Id, sell.Id, good.Id); }
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

    /// <summary>Käufer muss einen nennenswerten Teil einer Ladung bezahlen können, sonst lohnt die Fahrt nicht.</summary>
    public static bool CanAfford(Station buyer, float price, float units) => buyer.Credits > 0.25f * units * price;

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
            // Die Station zahlt nur, was sie hat; der Rest der Ladung wird ohne Bezahlung abgeladen
            float paid = stock.Price > 0 ? MathF.Min(amount, MathF.Max(0f, st.Credits) / stock.Price) : 0f;
            float revenue = paid * stock.Price;
            stock.Amount += amount;
            st.Credits -= revenue;
            ship.Credits += revenue;
            ship.Cargo.Remove(order.Good);
            ship.Order = null;
            ship.Phase = TradePhase.Idle;
            ship.IdleSince = u.Tick;
            u.Events.Publish(new TradeCompleted(ship.Id, order.Buy, order.Sell, order.Good, paid, revenue));
        }
    }
}
