using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Miner-KI: wählt Feld + Abnehmerstation mit dem besten Preis pro Zeit, fliegt abstrakt zum Feld,
/// baut dort mit denselben Laserschüssen ab wie der Spieler (Nachladezeit, Trefferwahrscheinlichkeit, zufällige Brockengröße,
/// Erz stammt aus dem Asteroiden), liefert aus und verkauft. Baut nicht ab, wenn der Preis zu niedrig ist
/// (verhindert endlose Überproduktion).
/// </summary>
public sealed class MinerSystem : ISimSystem
{
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
                if (Belt.OreIn(sector, field) < 1f) continue;
                var good = u.Goods[field.Good];
                float mineTicks = ship.CargoCapacity / good.VolumePerUnit / (MiningRules.ExpectedOrePerSecond * SimClock.TickInterval);
                foreach (var st in u.Stations.Values)
                {
                    if (!st.Market.TryGetValue(field.Good, out var e) || e.Price < good.BasePrice * MinPriceFactor) continue;
                    if (!TraderSystem.CanAfford(st, e.Price, ship.CargoCapacity / good.VolumePerUnit)) continue;
                    if (!u.IsReachable(sector.Id, st.Sector)) continue;
                    float time = Travel.Estimate(u, ship.Sector, ship.Position, sector.Id, field.WorkPoint(ship.Id)) + mineTicks
                               + Travel.Estimate(u, sector.Id, field.WorkPoint(ship.Id), st.Sector, st.DockPoint(ship.Id));
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
            ship.ShotTimer = 0f; ship.MineAsteroid = 0;
        }
        else if (ship.MinePhase == MinePhase.GoingToStation)
        {
            var st = u.Stations[order.DeliverTo];
            var stock = st.Stock(order.Good);
            float amount = ship.Cargo.GetValueOrDefault(order.Good);
            float paid = stock.Price > 0 ? MathF.Min(amount, MathF.Max(0f, st.Credits) / stock.Price) : 0f;
            float revenue = paid * stock.Price;
            stock.Amount += amount;
            st.Credits -= revenue;
            ship.Credits += revenue;
            ship.Cargo.Remove(order.Good);
            ship.Order = null;
            ship.MinePhase = MinePhase.Idle;
            ship.IdleSince = u.Tick;
            u.Events.Publish(new MineDelivered(ship.Id, order.DeliverTo, order.Good, paid, revenue));
        }
    }

    static void Mine(Universe u, Ship ship)
    {
        if (ship.Order is not MineOrder order) { ship.MinePhase = MinePhase.Idle; return; }

        var sector = u.Sectors[ship.Sector];
        var field = sector.Fields.FirstOrDefault(f => f.Good == order.Good && Belt.OreIn(sector, f) > 0.01f);

        if (field is null || ship.FreeUnits(u, order.Good) < MiningRules.MinChunkUnits)
        {
            if (ship.Cargo.GetValueOrDefault(order.Good) > 0f)
            {
                ship.MinePhase = MinePhase.GoingToStation;
                Travel.Start(u, ship, u.Stations[order.DeliverTo].Sector, u.Stations[order.DeliverTo].DockPoint(ship.Id));
            }
            else { ship.Order = null; ship.MinePhase = MinePhase.Idle; }
            return;
        }

        // Jeder Tick lädt die Waffe nach; ist sie bereit, fällt genau ein Schuss (nie mehrere auf einmal)
        ship.ShotTimer = MathF.Min(ship.ShotTimer + SimClock.TickInterval, MiningRules.ShotInterval + SimClock.TickInterval);
        if (ship.ShotTimer < MiningRules.ShotInterval) return;
        var rock = PickRock(sector, field, ship);
        if (rock is null) return;

        ship.ShotTimer -= MiningRules.ShotInterval;
        float units = LaserShot.Roll(u.Rng, rock);
        if (units > 0f)
        {
            Belt.Remove(u, sector, rock, units);
            ship.Cargo[order.Good] = ship.Cargo.GetValueOrDefault(order.Good) + MathF.Min(units, ship.FreeUnits(u, order.Good));   // Traktorstrahl lädt ein
        }
        u.Events.Publish(new MinerShot(sector.Id, ship.Id, rock.Id, rock.Position, rock.Radius, units));
    }

    /// <summary>Asteroid im Gürtel des Feldes, auf den der Miner feuert: bleibt beim selben, bis er leer ist; sonst der nächste in Laserreichweite.</summary>
    static Asteroid? PickRock(Sector sector, ResourceField field, Ship ship)
    {
        if (ship.MineAsteroid != 0)
        {
            var current = sector.Asteroids.FirstOrDefault(a => a.Id == ship.MineAsteroid);
            if (current is { Minable: true }) return current;
        }
        var rocks = Belt.AsteroidsOf(sector, field).OrderBy(a => Vec2.Distance(a.Position, ship.Position) - a.Radius).ThenBy(a => a.Id).ToList();
        var pick = rocks.FirstOrDefault(a => Vec2.Distance(a.Position, ship.Position) - a.Radius <= MiningRules.LaserRange) ?? rocks.FirstOrDefault();
        ship.MineAsteroid = pick?.Id ?? 0;
        return pick;
    }
}
