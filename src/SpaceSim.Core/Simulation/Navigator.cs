using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Ziel eines Autopiloten: Sektor + Position + Anzeigename.</summary>
public readonly record struct NavTarget(SectorId Sector, Vec2 Position, string Label);

public enum NavStatus { EnRoute, Arrived, Unreachable }

/// <summary>Nächster Wegpunkt für den Autopiloten. <see cref="Gate"/> ist gesetzt, solange noch ein Sprung bevorsteht.</summary>
public readonly record struct NavStep(NavStatus Status, Vec2 Waypoint, GateId? Gate);

/// <summary>
/// Autopilot-Logik für das Spielerschiff, engine-unabhängig: nutzt dieselben Routen (Gates) und denselben
/// Hindernis-Planer wie die NPC-Reisen. Die Darstellung steuert nur zum gelieferten Wegpunkt.
/// </summary>
public static class Navigator
{
    public const float ArriveRadius = 6f;

    public static NavTarget ForStation(Universe u, StationId id, ShipId ship)
    {
        var st = u.Stations[id];
        return new NavTarget(st.Sector, st.DockPoint(ship), st.Name);
    }

    /// <summary>Ziel "Sektor erreichen": Parkpunkt der ersten Station, sonst Sektormitte.</summary>
    public static NavTarget ForSector(Universe u, SectorId id, ShipId ship)
    {
        var s = u.Sectors[id];
        return s.Stations.Count > 0
            ? new NavTarget(id, u.Stations[s.Stations[0]].DockPoint(ship), s.Name)
            : new NavTarget(id, new Vec2(0, 0), s.Name);
    }

    /// <summary>
    /// Nächster Wegpunkt. Phase 10: Mit Schiff folgt die Route dessen Mobilitätsprofil (gesperrte Gates werden umflogen; ohne Weg
    /// <see cref="NavStatus.Unreachable"/>); ohne Schiff zählt nur der physische Gate-Zustand.
    /// </summary>
    public static NavStep Next(Universe u, SectorId sector, Vec2 pos, NavTarget target, Ship? ship = null)
    {
        if (sector == target.Sector)
        {
            if (Vec2.Distance(pos, target.Position) <= ArriveRadius) return new NavStep(NavStatus.Arrived, pos, null);
            return new NavStep(NavStatus.EnRoute, FirstWaypoint(u, sector, pos, target.Position), null);
        }

        var route = u.FindRoute(sector, target.Sector, Travel.ProfileOf(u, ship));
        if (route is null || route.Count == 0) return new NavStep(NavStatus.Unreachable, pos, null);
        var gate = u.Gates[route[0]];
        return new NavStep(NavStatus.EnRoute, FirstWaypoint(u, sector, pos, gate.Position), gate.Id);
    }

    static Vec2 FirstWaypoint(Universe u, SectorId sector, Vec2 from, Vec2 to)
    {
        var pts = PathPlanner.Plan(from, to, u.GetObstacles(sector));
        return pts.Count > 1 ? pts[1] : to;
    }
}
