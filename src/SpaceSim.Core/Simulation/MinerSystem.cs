using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Miner-KI: wählt Feld + Abnehmerstation mit dem besten Preis pro Zeit, fliegt abstrakt zum Feld,
/// baut dort Tick für Tick ab, liefert aus und verkauft. Baut nicht ab, wenn der Preis zu niedrig ist
/// (verhindert endlose Überproduktion).
/// </summary>
public sealed class MinerSystem : ISimSystem
{
    public const float ExtractPerTick = 2f;
    /// <summary>Mindestpreis relativ zum Basispreis, ab dem sich Abbau lohnt.</summary>
    public const float MinPriceFactor = 0.5f;
    public const int ThinkInterval = 10;

    public void Step(Universe u, int dt)
    {
        foreach (var ship in u.Ships.Values)
        {
            if (ship.Role != ShipRole.Miner) continue;

            if (ship.Transit is { } t)
            {
                if (u.Tick >= t.ArrivalTick) Arrive(u, ship, t);
                continue;
            }
            switch (ship.MinePhase)
            {
                case MinePhase.Idle:
                    if (u.Tick < ship.NextThinkTick) break;
                    if (!PickOrder(u, ship))
                    {
                        ship.NextThinkTick = u.Tick + ThinkInterval + ship.Id.Value % 7;
                        if (ship.IdleSince < 0) ship.IdleSince = u.Tick;
                    }
                    break;
                case MinePhase.Mining: Mine(u, ship); break;
            }
        }
    }

    static bool PickOrder(Universe u, Ship ship)
    {
        MineOrder? best = null;
        Vec2 bestFieldPos = default;
        float bestScore = 0f;
        foreach (var sector in u.Sectors.Values)
        {
            if (!u.IsReachable(ship.Sector, sector.Id)) continue;
            foreach (var field in sector.Fields)
            {
                if (field.Amount < 1f) continue;
                var good = u.Goods[field.Good];
                float mineTicks = ship.CargoCapacity / good.VolumePerUnit / ExtractPerTick;
                foreach (var st in u.Stations.Values)
                {
                    if (!st.Market.TryGetValue(field.Good, out var e) || e.Price < good.BasePrice * MinPriceFactor) continue;
                    if (!u.IsReachable(sector.Id, st.Sector)) continue;
                    float time = Travel.Ticks(u, ship.Sector, ship.Position, sector.Id, field.WorkPoint(ship.Id)) + mineTicks
                               + Travel.Ticks(u, sector.Id, field.WorkPoint(ship.Id), st.Sector, st.DockPoint(ship.Id));
                    float score = e.Price / time;
                    if (score > bestScore) { bestScore = score; bestFieldPos = field.WorkPoint(ship.Id); best = new MineOrder(sector.Id, field.Good, st.Id); }
                }
            }
        }
        if (best is null) return false;
        ship.IdleSince = -1;
        ship.Order = best;
        ship.MinePhase = MinePhase.GoingToField;
        Travel.Start(u, ship, best.Sector, bestFieldPos);
        return true;
    }

    static void Arrive(Universe u, Ship ship, Transit t)
    {
        u.FinishTransit(ship);
        if (ship.Order is not MineOrder order || !u.Stations.ContainsKey(order.DeliverTo))
        {
            ship.Order = null; ship.MinePhase = MinePhase.Idle; ship.Cargo.Clear();
            return;
        }

        if (ship.MinePhase == MinePhase.GoingToField)
        {
            ship.MinePhase = MinePhase.Mining;
        }
        else if (ship.MinePhase == MinePhase.GoingToStation)
        {
            var st = u.Stations[order.DeliverTo];
            var stock = st.Stock(order.Good);
            float amount = ship.Cargo.GetValueOrDefault(order.Good);
            float revenue = amount * stock.Price;
            stock.Amount += amount;
            st.Credits -= revenue;
            ship.Credits += revenue;
            ship.Cargo.Remove(order.Good);
            ship.Order = null;
            ship.MinePhase = MinePhase.Idle;
            ship.IdleSince = u.Tick;
            u.Events.Publish(new MineDelivered(ship.Id, order.DeliverTo, order.Good, amount, revenue));
        }
    }

    static void Mine(Universe u, Ship ship)
    {
        if (ship.Order is not MineOrder order) { ship.MinePhase = MinePhase.Idle; return; }

        var good = u.Goods[order.Good];
        var field = u.Sectors[ship.Sector].Fields.FirstOrDefault(f => f.Good == order.Good && f.Amount > 0f);
        float used = ship.Cargo.Sum(kv => kv.Value * u.Goods[kv.Key].VolumePerUnit);
        float free = ship.CargoCapacity - used;

        if (field is null || free < 0.01f)
        {
            if (ship.Cargo.GetValueOrDefault(order.Good) > 0f)
            {
                ship.MinePhase = MinePhase.GoingToStation;
                Travel.Start(u, ship, u.Stations[order.DeliverTo].Sector, u.Stations[order.DeliverTo].DockPoint(ship.Id));
            }
            else { ship.Order = null; ship.MinePhase = MinePhase.Idle; }
            return;
        }

        float amount = MathF.Min(MathF.Min(ExtractPerTick, field.Amount), free / good.VolumePerUnit);
        field.Amount -= amount;
        ship.Cargo[order.Good] = ship.Cargo.GetValueOrDefault(order.Good) + amount;
    }
}
