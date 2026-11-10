using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Momentaufnahme eines Schiffs für die Darstellung. Direction ist normiert.</summary>
public readonly record struct ShipSnapshot(ShipId Id, ShipRole Role, Vec2 Position, Vec2 Direction, bool Moving, bool Mining = false);

/// <summary>
/// Leitet aus dem abstrakten Zustand (Fahrplan, Ankunftspositionen) ab, welche Schiffe zu einem
/// (auch gebrochenen) Zeitpunkt wo in einem Sektor sind. Engine-unabhängig; die Darstellung rendert nur.
/// </summary>
public static class Materializer
{
    public static List<ShipSnapshot> Snapshot(Universe u, SectorId sector, double tick)
    {
        var result = new List<ShipSnapshot>();

        foreach (var id in u.Tracker.ParkedIn(sector))
        {
            var ship = u.Ships[id];
            if (ship.IsPlayer) continue;
            result.Add(new ShipSnapshot(ship.Id, ship.Role, ship.Position, new Vec2(0, -1), false,
                Mining: ship.Role == ShipRole.Miner && ship.MinePhase == MinePhase.Mining));
        }
        foreach (var id in u.Tracker.TransitIn(sector))
        {
            var ship = u.Ships[id];
            if (ship.IsPlayer || ship.Transit is null) continue;
            foreach (var leg in ship.Transit.Legs)
            {
                if (leg.Sector != sector || tick < leg.StartTick || tick >= leg.EndTick) continue;
                float f = (float)((tick - leg.StartTick) / (double)(leg.EndTick - leg.StartTick));
                var pos = Vec2.Lerp(leg.From, leg.To, f);
                var dir = (leg.To - leg.From).Normalized();
                result.Add(new ShipSnapshot(ship.Id, ship.Role, pos, dir, true));
                break;
            }
        }
        return result;
    }
}
