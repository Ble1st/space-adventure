using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Momentaufnahme eines Schiffs für die Darstellung. Direction ist normiert.</summary>
public readonly record struct ShipSnapshot(ShipId Id, ShipRole Role, Vec2 Position, Vec2 Direction, bool Moving);

/// <summary>
/// Leitet aus dem abstrakten Zustand (Fahrplan, Ankunftspositionen) ab, welche Schiffe zu einem
/// (auch gebrochenen) Zeitpunkt wo in einem Sektor sind. Engine-unabhängig; die Darstellung rendert nur.
/// </summary>
public static class Materializer
{
    public static List<ShipSnapshot> Snapshot(Universe u, SectorId sector, double tick)
    {
        var result = new List<ShipSnapshot>();
        foreach (var ship in u.Ships.Values)
        {
            if (ship.Role == ShipRole.Player) continue;

            if (ship.Transit is null)
            {
                if (ship.Sector == sector)
                    result.Add(new ShipSnapshot(ship.Id, ship.Role, ship.Position, new Vec2(0, -1), false));
                continue;
            }
            foreach (var leg in ship.Transit.Legs)
            {
                if (leg.Sector != sector || tick < leg.StartTick || tick >= leg.EndTick) continue;
                float f = (float)((tick - leg.StartTick) / (double)(leg.EndTick - leg.StartTick));
                var pos = Vec2.Lerp(leg.From, leg.To, f);
                float dx = leg.To.X - leg.From.X, dy = leg.To.Y - leg.From.Y;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                var dir = len > 0.001f ? new Vec2(dx / len, dy / len) : new Vec2(0, -1);
                result.Add(new ShipSnapshot(ship.Id, ship.Role, pos, dir, true));
                break;
            }
        }
        return result;
    }
}
