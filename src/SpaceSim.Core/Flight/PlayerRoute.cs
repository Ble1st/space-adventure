using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Flight;

/// <summary>Art eines Routenhalts (Interstellar-Pilot-2-Lücke: Mehrpunkt-Route des Spieler-Autopiloten).</summary>
public enum RouteStopKind { Station, Gate, Sector, Point }

/// <summary>
/// Ein Halt der Spielerroute. <see cref="Id"/> = Station/Tor (sonst 0); <see cref="Sector"/> = Sektor des Halts (beim Tor: der Sektor, in dem das Tor liegt);
/// <see cref="Position"/> nur bei <see cref="RouteStopKind.Point"/>.
/// </summary>
public sealed record RouteStop(RouteStopKind Kind, int Id, SectorId Sector, Vec2 Position);

/// <summary>
/// Mehrpunkt-Route des Spielers (am Spielerakteur, <see cref="Actor.Route"/>): Halte der Reihe nach, nächster Halt <see cref="Next"/>, Schalter
/// „an jeder Station andocken“. Gespeichert additiv, gehasht nur mit Spielerpolitik. Die NPC-Welt liest sie nie.
/// </summary>
public sealed class PlayerRoute
{
    public readonly List<RouteStop> Stops = new();
    /// <summary>Index des nächsten anzufliegenden Halts (= Zahl der Halte: Route abgeflogen).</summary>
    public int Next;
    public bool DockAtStations;
    public bool Finished => Next >= Stops.Count;
}

/// <summary>Nächster Flugabschnitt: Autopilot-Ziel und (bei einem Stationshalt) die Station zum Andocken.</summary>
public readonly record struct RouteLeg(NavTarget Target, StationId? Station);

/// <summary>
/// Reine Regeln der Spielerroute: Halte anlegen/entfernen, nächster Halt, „erreicht?“, Fortschritt, Anzeige, Flugzeitschätzung (mit <see cref="PlayerTrip"/>).
/// Keine Zufallszahl, kein Zustand außer <see cref="Actor.Route"/>; die Ansicht steuert damit ihren normalen Autopiloten.
/// </summary>
public static class PlayerRoutes
{
    public const int MaxStops = 10;

    public static PlayerRoute? Of(Universe u) => u.Actors.TryGetValue(u.PlayerActor, out var a) ? a.Route : null;

    static PlayerRoute Ensure(Universe u) => u.Actors[u.PlayerActor].Route ??= new PlayerRoute();

    public static RouteStop ForStation(Universe u, StationId id) => new(RouteStopKind.Station, id.Value, u.Stations[id].Sector, default);
    public static RouteStop ForGate(Universe u, GateId id) => new(RouteStopKind.Gate, id.Value, u.Gates[id].Sector, default);
    public static RouteStop ForSector(SectorId id) => new(RouteStopKind.Sector, 0, id, default);
    public static RouteStop ForPoint(SectorId id, Vec2 pos) => new(RouteStopKind.Point, 0, id, pos);

    /// <summary>Grund, warum der Halt nicht angehängt werden kann (null = geht).</summary>
    public static string? AddIssue(Universe u, RouteStop stop)
    {
        if (!u.Actors.ContainsKey(u.PlayerActor)) return "Kein Spieler";
        var r = Of(u);
        if (r is not null && !r.Finished && r.Stops.Count - r.Next >= MaxStops) return $"Route voll ({MaxStops} Halte)";
        if (!u.Sectors.ContainsKey(stop.Sector) || !Knowledge.KnowsSector(u, stop.Sector)) return "Sektor unbekannt";
        switch (stop.Kind)
        {
            case RouteStopKind.Station:
                if (!u.Stations.TryGetValue(new StationId(stop.Id), out var st) || st.Sector != stop.Sector) return "Station unbekannt";
                if (!Knowledge.KnowsStation(u, st)) return "Station unbekannt";
                break;
            case RouteStopKind.Gate:
                if (!u.Gates.TryGetValue(new GateId(stop.Id), out var g) || g.Sector != stop.Sector) return "Tor unbekannt";
                if (!Knowledge.KnowsGate(u, g.Id)) return "Tor unbekannt";
                break;
        }
        if (r is not null && !r.Finished && r.Stops.Count > 0 && r.Stops[^1] == stop) return "Schon letzter Halt";
        return null;
    }

    /// <summary>Hängt einen Halt an. Eine abgeflogene Route wird dabei geleert (neue Route).</summary>
    public static bool Add(Universe u, RouteStop stop)
    {
        if (AddIssue(u, stop) is not null) return false;
        var r = Ensure(u);
        if (r.Finished && r.Stops.Count > 0) { r.Stops.Clear(); r.Next = 0; }
        // abgeflogene Halte vorne fallen weg, damit die Liste nicht wächst
        if (r.Next > 0) { r.Stops.RemoveRange(0, r.Next); r.Next = 0; }
        r.Stops.Add(stop);
        return true;
    }

    /// <summary>Entfernt den Halt an <paramref name="index"/> (Index in <see cref="PlayerRoute.Stops"/>).</summary>
    public static bool Remove(Universe u, int index)
    {
        var r = Of(u);
        if (r is null || index < 0 || index >= r.Stops.Count) return false;
        r.Stops.RemoveAt(index);
        if (index < r.Next) r.Next--;
        if (r.Stops.Count == 0) Clear(u);
        return true;
    }

    public static void Clear(Universe u)
    {
        if (u.Actors.TryGetValue(u.PlayerActor, out var a)) a.Route = null;
    }

    public static void SetDock(Universe u, bool dock) => Ensure(u).DockAtStations = dock;

    /// <summary>Route von vorn (nach dem Abfliegen) — nur wenn sie abgeflogen ist.</summary>
    public static void Restart(Universe u) { if (Of(u) is { Finished: true } r) r.Next = 0; }

    /// <summary>Aktueller Halt (null = keine Route oder abgeflogen).</summary>
    public static RouteStop? Current(Universe u) => Of(u) is { } r && !r.Finished ? r.Stops[r.Next] : null;

    /// <summary>Ist der Halt noch gültig (Station/Tor/Sektor vorhanden)?</summary>
    public static bool Valid(Universe u, RouteStop s) => s.Kind switch
    {
        RouteStopKind.Station => u.Stations.ContainsKey(new StationId(s.Id)),
        RouteStopKind.Gate => u.Gates.TryGetValue(new GateId(s.Id), out var g) && u.Gates.ContainsKey(g.Target),
        _ => u.Sectors.ContainsKey(s.Sector),
    };

    /// <summary>Flugabschnitt zum Halt. Tor = in den Sektor dahinter (wie das Antippen eines Tors); null = Halt ungültig.</summary>
    public static RouteLeg? Leg(Universe u, RouteStop s)
    {
        if (!Valid(u, s)) return null;
        var ship = u.PlayerShip;
        return s.Kind switch
        {
            RouteStopKind.Station => new RouteLeg(Navigator.ForStation(u, new StationId(s.Id), ship), new StationId(s.Id)),
            RouteStopKind.Gate => new RouteLeg(Navigator.ForSector(u, u.Gates[u.Gates[new GateId(s.Id)].Target].Sector, ship) with { Label = Label(u, s) }, null),
            RouteStopKind.Sector => new RouteLeg(Navigator.ForSector(u, s.Sector, ship), null),
            _ => new RouteLeg(new NavTarget(s.Sector, s.Position, Label(u, s)), null),
        };
    }

    /// <summary>Halt erreicht? Tor: Schiff ist im Sektor dahinter; sonst wie der Autopilot (<see cref="Navigator.ReachOf"/>).</summary>
    public static bool Reached(Universe u, RouteStop s, SectorId sector, Vec2 pos)
    {
        if (!Valid(u, s)) return true;   // verschwundener Halt wird übersprungen
        if (s.Kind == RouteStopKind.Gate) return sector == u.Gates[u.Gates[new GateId(s.Id)].Target].Sector;
        var t = Leg(u, s)!.Value.Target;
        return sector == t.Sector && Vec2.Distance(pos, t.Position) <= Navigator.ReachOf(t);
    }

    /// <summary>Nächsten Halt freigeben; true = es gibt noch einen.</summary>
    public static bool Advance(Universe u)
    {
        if (Of(u) is not { } r || r.Finished) return false;
        r.Next++;
        return !r.Finished;
    }

    /// <summary>Überspringt erreichte oder ungültige Halte ab dem aktuellen; true = es bleibt einer.</summary>
    public static bool SkipReached(Universe u, SectorId sector, Vec2 pos)
    {
        while (Current(u) is { } s && Reached(u, s, sector, pos)) Advance(u);
        return Current(u) is not null;
    }

    public static string Label(Universe u, RouteStop s) => s.Kind switch
    {
        RouteStopKind.Station => u.Stations.TryGetValue(new StationId(s.Id), out var st) ? StationNames.WithoutOwner(u, st) : "Station ?",
        RouteStopKind.Gate => u.Gates.TryGetValue(new GateId(s.Id), out var g) && u.Gates.TryGetValue(g.Target, out var far) && u.Sectors.TryGetValue(far.Sector, out var fs)
            ? $"Tor → {fs.Name}" : "Tor ?",
        RouteStopKind.Sector => u.Sectors.TryGetValue(s.Sector, out var sec) ? sec.Name : "Sektor ?",
        _ => $"Punkt {(u.Sectors.TryGetValue(s.Sector, out var ps) ? ps.Name : "?")} ({s.Position.X:0}, {s.Position.Y:0})",
    };

    /// <summary>Zeile fürs Flug-HUD: „Route 2/4 → Werft Delta“ (+ „· angehalten“); leer ohne Route.</summary>
    public static string HudLine(Universe u, bool flying)
    {
        if (Of(u) is not { } r || r.Stops.Count == 0) return "";
        if (r.Finished) return "";
        return $"Route {r.Next + 1}/{r.Stops.Count} → {Label(u, r.Stops[r.Next])}{(flying ? "" : " · angehalten")}";
    }

    /// <summary>
    /// Geschätzte Flugzeit der restlichen Route ab <paramref name="pos"/> in Sekunden (Summe der Abschnitte mit <see cref="PlayerTrip.Seconds"/>;
    /// PositiveInfinity = ein Halt ist nicht erreichbar).
    /// </summary>
    public static float EstimateSeconds(Universe u, SectorId sector, Vec2 pos, Ship? ship = null)
    {
        if (Of(u) is not { } r || r.Finished) return 0f;
        float total = 0f;
        var at = sector; var p = pos;
        for (int i = r.Next; i < r.Stops.Count; i++)
        {
            var s = r.Stops[i];
            if (Leg(u, s) is not { } leg) continue;
            var t = leg.Target;
            float sec = PlayerTrip.Seconds(u, at, p, t.Sector, t.Position, ship);
            if (float.IsInfinity(sec)) return float.PositiveInfinity;
            total += sec;
            at = t.Sector; p = t.Position;
        }
        return total;
    }

    /// <summary>Kopfzeile der Routenliste: „3 Halte · Flug ~2:10 min · andocken an“.</summary>
    public static string Summary(Universe u, SectorId sector, Vec2 pos, Ship? ship = null)
    {
        if (Of(u) is not { } r || r.Stops.Count == 0) return "Keine Route";
        int left = r.Stops.Count - r.Next;
        if (left <= 0) return $"Abgeflogen · {r.Stops.Count} Halte";
        return $"{left} {(left == 1 ? "Halt" : "Halte")} · Flug {PlayerTrip.Text(EstimateSeconds(u, sector, pos, ship))}";
    }

    /// <summary>Ort eines Halts auf der Sektorkarte (Station, Tor, Punkt, bei einem Sektor das Anflugziel); null = Halt ungültig.</summary>
    public static Vec2? StopPoint(Universe u, RouteStop s)
    {
        if (!Valid(u, s)) return null;
        return s.Kind switch
        {
            RouteStopKind.Station => u.Stations[new StationId(s.Id)].Position,
            RouteStopKind.Gate => u.Gates[new GateId(s.Id)].Position,
            RouteStopKind.Point => s.Position,
            _ => Leg(u, s)!.Value.Target.Position,
        };
    }

    /// <summary>Restliche Halte in einem Sektor mit ihrer Nummer (1 = nächster Halt) — für Linie und Nummern der Sektorkarte.</summary>
    public static List<(int Number, RouteStop Stop, Vec2 At)> StopsIn(Universe u, SectorId sector)
    {
        var list = new List<(int, RouteStop, Vec2)>();
        if (Of(u) is not { } r || r.Finished) return list;
        for (int i = r.Next; i < r.Stops.Count; i++)
            if (r.Stops[i].Sector == sector && StopPoint(u, r.Stops[i]) is { } at) list.Add((i - r.Next + 1, r.Stops[i], at));
        return list;
    }

    /// <summary>
    /// Sektorfolge der restlichen Route ab dem Spielersektor für die Universumskarte: jeder Sektor, den der Flug durchquert (über die Torroute mit dem Profil
    /// des Schiffs), mit der Haltnummer (1 = nächster Halt) oder 0 bei Durchflug. Unerreichbare Abschnitte werden als direkter Sprung eingetragen.
    /// </summary>
    public static List<(SectorId Sector, int Stop)> MapPath(Universe u)
    {
        var path = new List<(SectorId, int)>();
        if (Of(u) is not { } r || r.Finished) return path;
        var profile = Travel.ProfileOf(u, u.Ships.TryGetValue(u.PlayerShip, out var me) ? me : null);
        var at = u.PlayerSector;
        path.Add((at, 0));
        void Go(SectorId to, int stop)
        {
            if (to != at)
            {
                var route = u.FindRoute(at, to, profile);
                if (route is { Count: > 0 })
                    for (int k = 0; k < route.Count - 1; k++) path.Add((u.Gates[u.Gates[route[k]].Target].Sector, 0));
                at = to;
                path.Add((to, stop));
            }
            else if (stop > 0) path.Add((to, stop));
        }
        for (int i = r.Next; i < r.Stops.Count; i++)
        {
            var s = r.Stops[i];
            if (!Valid(u, s)) continue;
            int n = i - r.Next + 1;
            if (s.Kind == RouteStopKind.Gate) { Go(s.Sector, 0); Go(u.Gates[u.Gates[new GateId(s.Id)].Target].Sector, n); }
            else Go(s.Sector, n);
        }
        return path;
    }
}
