using SpaceSim.Core.Events;
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

    /// <summary>Mobilitätsprofil der Routensuche (Phase 10): das des Schiffs, ohne Schiff nur der physische Gate-Zustand.</summary>
    public static MobilityProfile ProfileOf(Universe u, Ship? ship) => ship is null ? MobilityProfile.Physical : GateAccess.ProfileOf(u, ship);

    /// <summary>Plant die Teilstrecken. Phase 12: <paramref name="speed"/> überschreibt das Tempo des Schiffs (Verband fliegt mit dem langsamsten Mitglied).</summary>
    public static List<Leg>? Plan(Universe u, long startTick, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship = null, float? speed = null)
    {
        var route = u.FindRoute(fromSector, toSector, ProfileOf(u, ship));
        if (route is null) return null;

        float v = speed is > 0f ? speed.Value : SpeedOf(ship);
        var legs = new List<Leg>();
        long t = startTick;
        var sector = fromSector;
        var pos = fromPos;
        foreach (var gid in route)
        {
            var gate = u.Gates[gid];
            t = AddSectorLegs(u, legs, sector, pos, gate.Position, t, v);
            var far = u.Gates[gate.Target];
            sector = far.Sector;
            pos = far.Position;
        }
        AddSectorLegs(u, legs, sector, pos, toPos, t, v);
        return legs;
    }

    /// <summary>Eine Teilstrecke je Segment des ausweichenden Pfads innerhalb eines Sektors.</summary>
    static long AddSectorLegs(Universe u, List<Leg> legs, SectorId sector, Vec2 from, Vec2 to, long start, float speed)
    {
        var pts = u.PathCache.Get(u, sector, from, to);   // Punkt 13: geteilter, unveränderter Pfad aus dem Zwischenspeicher
        long t = start;
        for (int i = 1; i < pts.Count; i++) t = AddLeg(legs, sector, pts[i - 1], pts[i], t, speed);
        return t;
    }

    /// <summary>
    /// Ausweichender Pfad einer NPC-Reise im Sektor. Runde 44: in vergrößerten Sektoren (Maßstab &gt; 1) mit Glätten und dem festen Ringpunkt-Ausweichweg
    /// (<see cref="PathPlanner.PlanRobust"/>) — die längeren Strecken kreuzen mehr Gürtel und Stationen, die Umweg-Rekursion erschöpfte ihre Tiefe in
    /// Gürtelnischen und der Polygonzug lief durch Stationen. Im Grundmaßstab unverändert.
    /// </summary>
    public static List<Vec2> SectorPath(Universe u, SectorId sector, Vec2 from, Vec2 to) =>
        new(u.PathCache.Get(u, sector, from, to));   // Punkt 13: aus dem Pfad-Zwischenspeicher (Kopie für den Aufrufer)

    /// <summary>Punkt 13: dieselbe Planung ohne Zwischenspeicher (Vergleich in Tests).</summary>
    public static List<Vec2> SectorPathUncached(Universe u, SectorId sector, Vec2 from, Vec2 to) =>
        SectorPathCache.Plan(u, sector, u.Sectors.GetValueOrDefault(sector), from, to, null);

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
    public static float Estimate(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship = null) =>
        Estimate(u, fromSector, fromPos, toSector, toPos, ship, ProfileOf(u, ship));

    /// <summary>
    /// Schätzung mit ausdrücklichem Mobilitätsprofil (Phase 10): z. B. die Verkaufsfahrt eines noch leeren Händlers mit dem beladenen Profil,
    /// damit ein Embargo die Route schon bei der Wahl ausschließt.
    /// </summary>
    public static float Estimate(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship, MobilityProfile profile)
    {
        var route = u.CachedRoute(fromSector, toSector, profile);
        if (route is null) return float.PositiveInfinity;
        return DistanceAlong(u, route, fromPos, toPos) / SpeedOf(ship) + route.Count + 1;
    }

    /// <summary>
    /// Punkt 13 (Rechenzeit): <see cref="Estimate(Universe, SectorId, Vec2, SectorId, Vec2, Ship?, MobilityProfile)"/> und die zugehörige
    /// <see cref="EstimateDistance(Universe, SectorId, Vec2, SectorId, Vec2, MobilityProfile)"/> in einem Durchgang (gleiche Route, gleiche Rechnung, also
    /// bitgleiche Werte); <paramref name="distance"/> ist PositiveInfinity, wenn nicht erreichbar.
    /// </summary>
    internal static float EstimateWithDistance(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship, MobilityProfile profile, out float distance)
    {
        var route = u.CachedRoute(fromSector, toSector, profile);
        if (route is null) { distance = float.PositiveInfinity; return float.PositiveInfinity; }
        distance = DistanceAlong(u, route, fromPos, toPos);
        return distance / SpeedOf(ship) + route.Count + 1;
    }

    /// <summary>Punkt 13: <see cref="DistanceAlong"/> für Aufrufer, die die Route schon haben (Händlerbewertung).</summary>
    internal static float DistanceAlongRoute(Universe u, List<GateId> route, Vec2 fromPos, Vec2 toPos) => DistanceAlong(u, route, fromPos, toPos);

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
    public static float EstimateDistance(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship = null) =>
        EstimateDistance(u, fromSector, fromPos, toSector, toPos, ProfileOf(u, ship));

    /// <summary>Geschätzte Strecke mit ausdrücklichem Mobilitätsprofil (Phase 10).</summary>
    public static float EstimateDistance(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, MobilityProfile profile)
    {
        var route = u.CachedRoute(fromSector, toSector, profile);
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

    /// <summary>
    /// Startet eine Reise. Phase 10: Die Route folgt dem Mobilitätsprofil des Schiffs; Zoll der Gates auf der Route wird beim Antritt fällig
    /// (<see cref="GateAccess.PayToll"/>). Ist das Ziel für das Schiff nicht erreichbar (Sperre, geschlossenes Gate), startet keine Reise:
    /// Der Auftrag fällt weg, die Fracht bleibt, das Schiff denkt bald neu (<see cref="RouteBlocked"/>) — früher wurde es ans Ziel „teleportiert“.
    /// </summary>
    public static bool Start(Universe u, Ship ship, SectorId toSector, Vec2 toPos, float? speed = null)
    {
        Mining.MiningAnchor.Release(u, ship, ship.Sector, "left");   // Phase 32: wer abfliegt, holt die Anker ein
        var profile = ProfileOf(u, ship);
        var route = u.FindRoute(ship.Sector, toSector, profile);
        var legs = route is null ? null : Plan(u, u.Tick, ship.Sector, ship.Position, toSector, toPos, ship, speed);
        if (legs is null)
        {
            AccessEnforcement.Release(u, ship);
            u.Events.Publish(new RouteBlocked(ship.Id, toSector, "Ziel nicht erreichbar"));
            return false;
        }
        // Zoll je Gate der Route (Profil mit echter Fracht), an die Kasse der Sektorfraktion
        if (!profile.IsPhysical && u.AccessRestricted)
            foreach (var gid in route!)
            {
                var gate = u.Gates[gid];
                var pass = GateAccess.Evaluate(u, gate, profile, ship);
                if (pass.Toll > 0f) GateAccess.PayToll(u, ship, gate, pass);
            }
        // Treibstoffverbrauch (Phase 4b): Strecke innerhalb der Sektoren × fuelUse / efficiency wird aus dem Tank gebucht
        // (Gate-Sprünge sind frei; die Legs liegen je Sektor, Sprünge dazwischen kosten nichts). KI-Schiffe stellen den
        // Treibstoff vorher über Refuel sicher; der Tankdeckel verhindert negative Stände.
        float distance = legs.Sum(l => Vec2.Distance(l.From, l.To));
        Refuel.Consume(u, ship, distance);   // Phase 14: nur bei eingeschaltetem Treibstoff
        u.StartTransit(ship, new Transit
        {
            StartTick = u.Tick,
            ArrivalTick = legs[^1].EndTick,
            ToSector = toSector,
            ToPosition = toPos,
            Legs = legs,
        });
        return true;
    }
}
