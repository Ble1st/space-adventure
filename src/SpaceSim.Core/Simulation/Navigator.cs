using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Ziel eines Autopiloten: Sektor + Position + Anzeigename. <see cref="ArriveAt"/> = Abstand zum Ziel, ab dem es als erreicht gilt
/// (0 = <see cref="Navigator.ArriveRadius"/>). Runde 45: Stationsziele liegen mittig IN der Station — erreicht ist das Ziel am Stationsrand
/// (Radius + <see cref="Navigator.ArriveRadius"/>), nicht erst 6 Einheiten vor der Mitte.</summary>
public readonly record struct NavTarget(SectorId Sector, Vec2 Position, string Label, float ArriveAt = 0f);

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
        // Runde 45 (Nutzeranweisung): der Navpunkt einer Station liegt mittig IN der Station — nicht am Andockpunkt außen;
        // "angekommen" gilt am Stationsrand (Andocken / Liegeplätze bleiben unverändert: Station.DockPoint)
        return new NavTarget(st.Sector, st.Position, st.Name, st.Radius + ArriveRadius);
    }

    /// <summary>Ziel "Sektor erreichen": Mitte der ersten Station (Runde 45: wie ForStation mittig, Rand gilt als erreicht), sonst Sektormitte.</summary>
    public static NavTarget ForSector(Universe u, SectorId id, ShipId ship)
    {
        var s = u.Sectors[id];
        return s.Stations.Count > 0
            ? ForStation(u, s.Stations[0], ship) with { Label = s.Name }
            : new NavTarget(id, new Vec2(0, 0), s.Name);
    }

    /// <summary>
    /// Nächster Wegpunkt. Phase 10: Mit Schiff folgt die Route dessen Mobilitätsprofil (gesperrte Gates werden umflogen; ohne Weg
    /// <see cref="NavStatus.Unreachable"/>); ohne Schiff zählt nur der physische Gate-Zustand.
    /// </summary>
    /// <summary>Abstand zum Zielpunkt, ab dem "angekommen" gilt: Ziel-eigene Reichweite (<see cref="NavTarget.ArriveAt"/>) oder der Standard.</summary>
    public static float ReachOf(NavTarget target) => target.ArriveAt > 0f ? target.ArriveAt : ArriveRadius;

    public static NavStep Next(Universe u, SectorId sector, Vec2 pos, NavTarget target, Ship? ship = null)
    {
        if (sector == target.Sector)
        {
            if (Vec2.Distance(pos, target.Position) <= ReachOf(target)) return new NavStep(NavStatus.Arrived, pos, null);
            return new NavStep(NavStatus.EnRoute, FirstWaypoint(u, sector, pos, target.Position, null), null);
        }

        var route = u.FindRoute(sector, target.Sector, Travel.ProfileOf(u, ship));
        if (route is null || route.Count == 0) return new NavStep(NavStatus.Unreachable, pos, null);
        var gate = u.Gates[route[0]];
        return new NavStep(NavStatus.EnRoute, FirstWaypoint(u, sector, pos, gate.Position, gate.Id), gate.Id);
    }

    /// <summary>Sprungradius eines Tors: ein Spielerschiff, das näher kommt, springt (wie <c>Main.GateRadius</c>).</summary>
    public const float GateJumpRadius = 10f;

    /// <summary>
    /// Runde 25 (Gerätetest Kriegstest: „Route nach Vesta“ aus Beta flog über Delta): der Weg zum Vesta-Tor führte 3 m am Delta-Tor vorbei, das Schiff
    /// sprang ungewollt — der Autopilot kam erst über Delta → Beta zurück. Für den Spieler-Autopiloten sind darum alle anderen Tore des Sektors Hindernisse
    /// mit ihrem Sprungradius; ausgenommen das Zieltor und ein Tor, in dessen Radius das Schiff gerade steht (Ankunft nach einem Sprung).
    /// NPC-Reisen bleiben unverändert (sie springen nur am geplanten Tor).
    /// </summary>
    public static IReadOnlyList<Obstacle> PlayerObstacles(Universe u, SectorId sector, Vec2 pos, GateId? goalGate) => PlayerObstacles(u, sector, pos, goalGate, null);

    /// <summary>
    /// Punkt 13 (Rechenzeit): wie oben, mit einem Gedächtnis des Aufrufers für eine Rechnung über einen unveränderten Weltstand (<see cref="Flight.PlayerTrip"/>
    /// simuliert tausende Schritte in einem Aufruf): je (Sektor, Zieltor, ausgenommenes Tor) einmal ermittelt — dieselbe Liste wie ohne Gedächtnis.
    /// </summary>
    internal static IReadOnlyList<Obstacle> PlayerObstacles(Universe u, SectorId sector, Vec2 pos, GateId? goalGate, Dictionary<(SectorId, GateId, GateId), IReadOnlyList<Obstacle>>? memo)
    {
        if (!u.Sectors.TryGetValue(sector, out var sec)) return Array.Empty<Obstacle>();
        // ausgenommen: Zieltor und ein Tor, in dessen Radius das Schiff steht
        GateId skip = default;
        foreach (var gid in sec.Gates)
            if (u.Gates.TryGetValue(gid, out var g) && Vec2.Distance(pos, g.Position) <= GateJumpRadius + PathPlanner.Margin + Collision.ShipRadius + 2f) { skip = gid; break; }
        if (memo is not null)
        {
            var mk = (sector, goalGate ?? default, skip);
            if (!memo.TryGetValue(mk, out var ml))
            {
                // gleiche Liste wie unten (ohne den Zwischenspeicher je Thread: der Aufrufer hält sie selbst)
                var nl = u.GetObstacles(sector);
                foreach (var gid in sec.Gates)
                {
                    if (gid == goalGate || gid == skip || !u.Gates.TryGetValue(gid, out var gg)) continue;
                    nl.Add(new Obstacle(gg.Position, GateJumpRadius));
                }
                memo[mk] = ml = nl;
            }
            return ml;
        }
        // Runde 26 (Prüferhinweis): nicht jedes Bild neu kopieren — Zwischenspeicher je Universum, Tick, Sektor, Hindernisliste und ausgenommenen Toren
        var key = (sector, u.Tick, goalGate ?? default, skip);
        // Runde 27 (Prüferhinweis): auch die Radien gehören zum Schlüssel — Felsen schrumpfen beim Abbau in der Ansicht innerhalb eines Ticks
        float radii = 0f;
        foreach (var a in sec.Asteroids) radii += a.Radius;
        foreach (var id in sec.Stations) if (u.Stations.TryGetValue(id, out var sst)) radii += sst.Radius;
        if (_cacheU is not null && _cacheU.TryGetTarget(out var cu) && ReferenceEquals(cu, u) && _cacheKey == key && _cacheCount == sec.Asteroids.Count + sec.Stations.Count
            && _cacheRadii == radii) return _cacheList!;
        var list = u.GetObstacles(sector);
        foreach (var gid in sec.Gates)
        {
            if (gid == goalGate || gid == skip || !u.Gates.TryGetValue(gid, out var g)) continue;
            list.Add(new Obstacle(g.Position, GateJumpRadius));
        }
        (_cacheU ??= new WeakReference<Universe>(u)).SetTarget(u); _cacheKey = key; _cacheCount = sec.Asteroids.Count + sec.Stations.Count; _cacheRadii = radii; _cacheList = list;
        return list;
    }

    // je Thread (Tests laufen parallel); die Liste wird nur gelesen
    [ThreadStatic] static WeakReference<Universe>? _cacheU;
    [ThreadStatic] static (SectorId, long, GateId, GateId) _cacheKey;
    [ThreadStatic] static int _cacheCount;
    [ThreadStatic] static float _cacheRadii;
    [ThreadStatic] static List<Obstacle>? _cacheList;

    /// <summary>Ein Umweg-Punkt näher als dies gilt als erreicht (der nächste wird angeflogen).</summary>
    public const float WaypointReached = 1.5f;

    static Vec2 FirstWaypoint(Universe u, SectorId sector, Vec2 from, Vec2 to, GateId? goalGate)
    {
        var pts = PathPlanner.Plan(from, to, PlayerObstacles(u, sector, from, goalGate), PathPlanner.Margin + Collision.ShipRadius);
        // Gerätetest Phase 28 (Autopilot kreiste vor dem HQ Alpha): liegt der nächste Umweg-Punkt schon auf der eigenen Position (das Schiff steht genau auf
        // einer Tangente), lieferte der Planer ihn immer wieder — der Autopilot stand still. Dann den folgenden Punkt nehmen.
        for (int i = 1; i < pts.Count; i++)
            if (Vec2.Distance(pts[i], from) > WaypointReached) return pts[i];
        return to;
    }
}

/// <summary>
/// Gerätetest Phase 28: Autopilot mit Gedächtnis. <see cref="Navigator.Next"/> plant jedes Bild neu — in dichten Gürteln sprang der erste Umweg-Punkt hin und
/// her bzw. lag auf der eigenen Position, und das Schiff kreiste (HQ Alpha). Der Folger plant einmal und fliegt die Punkte der Reihe nach ab; neu geplant wird
/// bei neuem Ziel/Sektor, nach dem letzten Punkt oder wenn das Schiff ein paar Schritte keinen Fortschritt zum aktuellen Punkt macht (dann zuerst ein
/// Seitenschritt weg vom nächsten Hindernis). Rein bis auf den eigenen Zustand; nicht gespeichert (Ansichtszustand).
/// </summary>
public sealed class PathFollower
{
    /// <summary>Ein Punkt näher als dies gilt als erreicht.</summary>
    public const float Reach = 4f;
    /// <summary>So viele Aufrufe ohne Annäherung an den aktuellen Punkt gelten als festgefahren.</summary>
    public const int StallCalls = 25;

    List<Vec2>? _pts;
    int _idx;
    Vec2 _goal;
    SectorId _sector;
    float _best = float.MaxValue;
    int _stall, _escapes;

    public void Reset() { _pts = null; _escapes = 0; }

    /// <summary>
    /// Punkt 13 (Rechenzeit): Gedächtnis für eine Rechnung über einen unveränderten Weltstand mit festem Ziel (nur <see cref="Flight.PlayerTrip"/>): Route je
    /// Sektor und Hindernisliste je (Sektor, Zieltor, ausgenommenes Tor). null (Ansicht, Autopilot) = wie bisher jedes Mal neu.
    /// </summary>
    internal sealed class StaticMemo
    {
        public readonly Dictionary<SectorId, List<GateId>?> Routes = new();
        public readonly Dictionary<(SectorId, GateId, GateId), IReadOnlyList<Obstacle>> Obstacles = new();
    }
    internal StaticMemo? Memo;

    public NavStep Next(Universe u, SectorId sector, Vec2 pos, NavTarget target, Ship? ship = null)
    {
        GateId? gate = null;
        Vec2 goal;
        if (sector == target.Sector)
        {
            if (Vec2.Distance(pos, target.Position) <= Navigator.ReachOf(target)) { Reset(); return new NavStep(NavStatus.Arrived, pos, null); }
            goal = target.Position;
        }
        else
        {
            List<GateId>? route;
            if (Memo is null) route = u.FindRoute(sector, target.Sector, Travel.ProfileOf(u, ship));
            else if (!Memo.Routes.TryGetValue(sector, out route)) Memo.Routes[sector] = route = u.FindRoute(sector, target.Sector, Travel.ProfileOf(u, ship));
            if (route is null || route.Count == 0) { Reset(); return new NavStep(NavStatus.Unreachable, pos, null); }
            gate = route[0];
            goal = u.Gates[route[0]].Position;
        }
        var obs = Navigator.PlayerObstacles(u, sector, pos, gate, Memo?.Obstacles);
        if (_pts is null || sector != _sector || Vec2.Distance(goal, _goal) > 0.5f) Plan(pos, goal, obs, sector);
        while (_idx < _pts!.Count - 1 && Vec2.Distance(pos, _pts[_idx]) <= Reach) { _idx++; _best = float.MaxValue; _stall = 0; }
        float d = Vec2.Distance(pos, _pts[_idx]);
        if (d < _best - 0.2f) { _best = d; _stall = 0; }
        else if (++_stall > StallCalls)
        {
            // festgefahren: Seitenschritt weg vom nächsten Hindernis, dann von dort neu planen
            Obstacle? near = null; float nd = float.MaxValue;
            foreach (var o in obs) { float od = Vec2.Distance(o.Position, pos) - o.Radius; if (od < nd) { nd = od; near = o; } }
            _escapes++;
            Plan(pos, goal, obs, sector);
            if (near is { } n)
            {
                var away = (pos - n.Position).Normalized();
                var side = new Vec2(-away.Y, away.X) * ((_escapes % 2 == 0) ? 1f : -1f);
                _pts!.Insert(1, pos + (away + side).Normalized() * (n.Radius + 10f));
                _idx = 1;
            }
        }
        return new NavStep(NavStatus.EnRoute, _pts[_idx], gate);
    }

    void Plan(Vec2 pos, Vec2 goal, IReadOnlyList<Obstacle> obs, SectorId sector)
    {
        // Runde 25 (Selbsttest Stufe 79: Kreisen im dichten Felsfeld vor dem HQ Alpha): Zickzack glätten; läuft der Plan trotzdem durch Felsen (Umweg-Rekursion
        // erschöpft), ein Ausweichweg über feste Ringpunkte
        float m = PathPlanner.Margin + Collision.ShipRadius;
        _pts = PathPlanner.Smooth(PathPlanner.Plan(pos, goal, obs, m), obs, m);
        bool blocked = false;
        for (int i = 1; i < _pts.Count && !blocked; i++) blocked = !PathPlanner.Clear(_pts[i - 1], _pts[i], obs, m);
        if (blocked && PathPlanner.Detour(pos, goal, obs, m) is { } detour) _pts = detour;
        _idx = _pts.Count > 1 ? 1 : 0;
        _goal = goal; _sector = sector; _best = float.MaxValue; _stall = 0;
    }
}
