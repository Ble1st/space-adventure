using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Gemeinsame Reiseplanung für KI-Systeme. Eine Reise besteht aus Teilstrecken (Legs) je Sektor:
/// Start → Gate, Gate → Gate, …, Gate → Ziel. Der Plan dient der abstrakten Simulation (Ankunftszeit)
/// und der Materialisierung (Position zu jedem Zeitpunkt).
/// </summary>
public static class Travel
{
    /// <summary>Einheiten pro Tick für Schiffe ohne Modell.</summary>
    public const float ShipSpeed = 8f;

    /// <summary>Tempo des Schiffs aus seinem Modell (<see cref="Ship.Stats"/>.Speed), sonst die Konstante.</summary>
    public static float SpeedOf(Ship? ship) =>
        ship is not null && ship.HasModel && ship.Stats.Speed > 0f ? ship.Stats.Speed : ShipSpeed;

    public static List<Leg>? Plan(Universe u, long startTick, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship = null)
    {
        var route = u.FindRoute(fromSector, toSector);
        if (route is null) return null;

        float speed = SpeedOf(ship);
        var legs = new List<Leg>();
        long t = startTick;
        var sector = fromSector;
        var pos = fromPos;
        foreach (var gid in route)
        {
            var gate = u.Gates[gid];
            t = AddSectorLegs(u, legs, sector, pos, gate.Position, t, speed);
            var far = u.Gates[gate.Target];
            sector = far.Sector;
            pos = far.Position;
        }
        AddSectorLegs(u, legs, sector, pos, toPos, t, speed);
        return legs;
    }

    /// <summary>Eine Teilstrecke je Segment des ausweichenden Pfads innerhalb eines Sektors.</summary>
    static long AddSectorLegs(Universe u, List<Leg> legs, SectorId sector, Vec2 from, Vec2 to, long start, float speed)
    {
        var pts = PathPlanner.Plan(from, to, u.GetObstacles(sector));
        long t = start;
        for (int i = 1; i < pts.Count; i++) t = AddLeg(legs, sector, pts[i - 1], pts[i], t, speed);
        return t;
    }

    static long AddLeg(List<Leg> legs, SectorId sector, Vec2 from, Vec2 to, long start, float speed)
    {
        long dur = Math.Max(1, (long)MathF.Ceiling(Vec2.Distance(from, to) / speed));
        legs.Add(new Leg(sector, from, to, start, start + dur));
        return start + dur;
    }

    /// <summary>
    /// Schnelle Schätzung der Reisedauer ohne Umwegplanung (Luftlinie je Sektor, Route aus dem Tick-Cache).
    /// Für KI-Bewertungen in verschachtelten Schleifen; PositiveInfinity wenn nicht erreichbar.
    /// </summary>
    public static float Estimate(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship = null)
    {
        var route = u.CachedRoute(fromSector, toSector);
        if (route is null) return float.PositiveInfinity;
        return DistanceAlong(u, route, fromPos, toPos) / SpeedOf(ship) + route.Count + 1;
    }

    /// <summary>Luftlinien-Strecke entlang der Route innerhalb der Sektoren (Gate-Sprünge zählen nicht); PositiveInfinity wenn nicht erreichbar.</summary>
    static float DistanceAlong(Universe u, List<GateId> route, Vec2 fromPos, Vec2 toPos)
    {
        float dist = 0f;
        var pos = fromPos;
        foreach (var gid in route)
        {
            var gate = u.Gates[gid];
            dist += Vec2.Distance(pos, gate.Position);
            pos = u.Gates[gate.Target].Position;
        }
        return dist + Vec2.Distance(pos, toPos);
    }

    /// <summary>Geschätzte Strecke innerhalb der Sektoren (Luftlinie je Sektor, Gate-Sprünge frei); PositiveInfinity wenn nicht erreichbar.</summary>
    public static float EstimateDistance(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship = null)
    {
        var route = u.CachedRoute(fromSector, toSector);
        if (route is null) return float.PositiveInfinity;
        return DistanceAlong(u, route, fromPos, toPos);
    }

    /// <summary>Geplante Strecke innerhalb der Sektoren inkl. Umwege (Summe der Legs; Gate-Sprünge sind frei); 0 wenn nicht erreichbar.</summary>
    public static float PlannedDistance(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship = null)
    {
        var legs = Plan(u, 0, fromSector, fromPos, toSector, toPos, ship);
        return legs is null ? 0f : legs.Sum(l => Vec2.Distance(l.From, l.To));
    }

    /// <summary>Treibstoffbedarf der geschätzten Strecke (Phase 4b: Strecke × fuelUse / efficiency); 0 ohne Antrieb.</summary>
    public static float EstimateFuel(Universe u, Ship? ship, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos) =>
        Refuel.Consumption(ship, EstimateDistance(u, fromSector, fromPos, toSector, toPos, ship));

    /// <summary>Genaue Reisedauer in Ticks inkl. Umwege; PositiveInfinity wenn nicht erreichbar.</summary>
    public static float Ticks(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship = null)
    {
        var legs = Plan(u, 0, fromSector, fromPos, toSector, toPos, ship);
        return legs is null ? float.PositiveInfinity : legs[^1].EndTick;
    }

    public static void Start(Universe u, Ship ship, SectorId toSector, Vec2 toPos)
    {
        var legs = Plan(u, u.Tick, ship.Sector, ship.Position, toSector, toPos, ship)
                   ?? new List<Leg> { new(toSector, toPos, toPos, u.Tick, u.Tick + 1) };   // unerreichbar: sofort "teleportieren"
        // Treibstoffverbrauch (Phase 4b): Strecke innerhalb der Sektoren × fuelUse / efficiency wird aus dem Tank gebucht
        // (Gate-Sprünge sind frei; die Legs liegen je Sektor, Sprünge dazwischen kosten nichts). KI-Schiffe stellen den
        // Treibstoff vorher über Refuel sicher; der Tankdeckel verhindert negative Stände.
        float distance = legs.Sum(l => Vec2.Distance(l.From, l.To));
        Refuel.Consume(ship, distance);
        u.StartTransit(ship, new Transit
        {
            StartTick = u.Tick,
            ArrivalTick = legs[^1].EndTick,
            ToSector = toSector,
            ToPosition = toPos,
            Legs = legs,
        });
    }
}
