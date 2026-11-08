using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Gemeinsame Reiseplanung für KI-Systeme. Eine Reise besteht aus Teilstrecken (Legs) je Sektor:
/// Start → Gate, Gate → Gate, …, Gate → Ziel. Der Plan dient der abstrakten Simulation (Ankunftszeit)
/// und der Materialisierung (Position zu jedem Zeitpunkt).
/// </summary>
public static class Travel
{
    /// <summary>Einheiten pro Tick für NPC-Schiffe.</summary>
    public const float ShipSpeed = 8f;

    public static List<Leg>? Plan(Universe u, long startTick, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos)
    {
        var route = u.FindRoute(fromSector, toSector);
        if (route is null) return null;

        var legs = new List<Leg>();
        long t = startTick;
        var sector = fromSector;
        var pos = fromPos;
        foreach (var gid in route)
        {
            var gate = u.Gates[gid];
            t = AddSectorLegs(u, legs, sector, pos, gate.Position, t);
            var far = u.Gates[gate.Target];
            sector = far.Sector;
            pos = far.Position;
        }
        AddSectorLegs(u, legs, sector, pos, toPos, t);
        return legs;
    }

    /// <summary>Eine Teilstrecke je Segment des ausweichenden Pfads innerhalb eines Sektors.</summary>
    static long AddSectorLegs(Universe u, List<Leg> legs, SectorId sector, Vec2 from, Vec2 to, long start)
    {
        var pts = PathPlanner.Plan(from, to, u.GetObstacles(sector));
        long t = start;
        for (int i = 1; i < pts.Count; i++) t = AddLeg(legs, sector, pts[i - 1], pts[i], t);
        return t;
    }

    static long AddLeg(List<Leg> legs, SectorId sector, Vec2 from, Vec2 to, long start)
    {
        long dur = Math.Max(1, (long)MathF.Ceiling(Vec2.Distance(from, to) / ShipSpeed));
        legs.Add(new Leg(sector, from, to, start, start + dur));
        return start + dur;
    }

    /// <summary>Geschätzte Reisedauer in Ticks; PositiveInfinity wenn nicht erreichbar.</summary>
    public static float Ticks(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos)
    {
        var legs = Plan(u, 0, fromSector, fromPos, toSector, toPos);
        return legs is null ? float.PositiveInfinity : legs[^1].EndTick;
    }

    public static void Start(Universe u, Ship ship, SectorId toSector, Vec2 toPos)
    {
        var legs = Plan(u, u.Tick, ship.Sector, ship.Position, toSector, toPos)
                   ?? new List<Leg> { new(toSector, toPos, toPos, u.Tick, u.Tick + 1) };   // unerreichbar: sofort "teleportieren"
        ship.Transit = new Transit
        {
            StartTick = u.Tick,
            ArrivalTick = legs[^1].EndTick,
            ToSector = toSector,
            ToPosition = toPos,
            Legs = legs,
        };
    }
}
