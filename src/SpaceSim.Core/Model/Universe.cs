using SpaceSim.Core.Events;

namespace SpaceSim.Core.Model;

public sealed class Universe
{
    public long Tick;
    public ulong Seed = 1;
    public DeterministicRng Rng = new(1);

    public Dictionary<SectorId, Sector> Sectors = new();
    public Dictionary<GateId, Gate> Gates = new();
    public Dictionary<StationId, Station> Stations = new();
    public Dictionary<ShipId, Ship> Ships = new();
    public Dictionary<FactionId, Faction> Factions = new();
    public Dictionary<GoodId, Good> Goods = new();
    public Dictionary<string, StationType> StationTypes = new();
    public EconomyRules Rules = new();
    public long LastStationBuildTick = -1_000_000;

    /// <summary>Der einzige Sektor, der als Godot-Szene materialisiert ist.</summary>
    public SectorId PlayerSector;
    public ShipId PlayerShip;

    /// <summary>Ereignisbus und Schiffsindex sind abgeleitet und gehören nicht zum Spielstand.</summary>
    public SimEvents Events { get; } = new();
    public ShipTracker Tracker { get; } = new();

    /// <summary>Zählt Änderungen an Sektorinhalten (Stationen gebaut/entfernt), damit die Darstellung neu aufbauen kann.</summary>
    public int StructureVersion { get; private set; }

    int _nextShipId = 1000, _nextStationId = 1000;
    public int NextAsteroidId { get; set; } = 1;
    public int NewAsteroidId() => NextAsteroidId++;
    public ShipId NewShipId() => new(_nextShipId++);
    public StationId NewStationId() => new(_nextStationId++);
    /// <summary>Für den Spielstand: Zähler wiederherstellen.</summary>
    public (int ship, int station) IdCounters { get => (_nextShipId, _nextStationId); set => (_nextShipId, _nextStationId) = value; }

    // ───────── Schiffe ─────────

    public void AddShip(Ship s)
    {
        Ships[s.Id] = s;
        if (s.Transit is { } t) Tracker.AddTransit(t, s.Id); else Tracker.AddParked(s.Sector, s.Id);
        if (s.Id.Value >= _nextShipId) _nextShipId = s.Id.Value + 1;
        Events.Publish(new ShipSpawned(s.Id));
    }

    public void RemoveShip(ShipId id, string reason)
    {
        if (!Ships.TryGetValue(id, out var s)) return;
        if (s.Transit is { } t) Tracker.RemoveTransit(t, id); else Tracker.RemoveParked(s.Sector, id);
        Ships.Remove(id);
        Events.Publish(new ShipRemoved(id, reason));
    }

    public void StartTransit(Ship s, Transit t)
    {
        if (s.Transit is { } old) Tracker.RemoveTransit(old, s.Id); else Tracker.RemoveParked(s.Sector, s.Id);
        s.Transit = t;
        Tracker.AddTransit(t, s.Id);
    }

    /// <summary>Ankunft: Schiff wird am Zielort geparkt.</summary>
    public void FinishTransit(Ship s)
    {
        var t = s.Transit!;
        Tracker.RemoveTransit(t, s.Id);
        s.Transit = null;
        s.Sector = t.ToSector;
        s.Position = t.ToPosition;
        Tracker.AddParked(s.Sector, s.Id);
    }

    /// <summary>Setzt ein geparktes Schiff in einen anderen Sektor (z.B. Sprung des Spielers).</summary>
    public void MoveShip(Ship ship, SectorId to)
    {
        Tracker.RemoveParked(ship.Sector, ship.Id);
        ship.Sector = to;
        Tracker.AddParked(to, ship.Id);
    }

    /// <summary>Baut den abgeleiteten Schiffsindex neu auf (nach dem Laden).</summary>
    public void RebuildTracker()
    {
        foreach (var s in Ships.Values)
            if (s.Transit is { } t) Tracker.AddTransit(t, s.Id); else Tracker.AddParked(s.Sector, s.Id);
    }

    // ───────── Stationen ─────────

    public void AddStation(Station st)
    {
        Stations[st.Id] = st;
        var sector = Sectors[st.Sector];
        if (!sector.Stations.Contains(st.Id)) sector.Stations.Add(st.Id);
        if (st.Id.Value >= _nextStationId) _nextStationId = st.Id.Value + 1;
        StructureVersion++;
    }

    /// <summary>Entfernt eine Station; Schiffe, deren Auftrag sie betrifft, verlieren Auftrag und (gebundene) Fracht.</summary>
    public void RemoveStation(StationId id, string reason)
    {
        if (!Stations.TryGetValue(id, out var st)) return;
        Stations.Remove(id);
        Sectors[st.Sector].Stations.Remove(id);
        foreach (var ship in Ships.Values)
        {
            bool affected = ship.Order switch
            {
                TradeOrder o => o.Buy == id || o.Sell == id,
                MineOrder m => m.DeliverTo == id,
                GoToOrder g => g.Target == id,
                _ => false,
            };
            if (!affected) continue;
            ship.Order = null;
            ship.Phase = TradePhase.Idle;
            ship.MinePhase = MinePhase.Idle;
            ship.Cargo.Clear();
        }
        StructureVersion++;
        Events.Publish(new StationRemoved(id, st.Sector, reason));
    }

    public void NotifyStationBuilt(Station st) => Events.Publish(new StationBuilt(st.Id, st.Sector, st.TypeKey));

    // ───────── Raum ─────────

    /// <summary>Statische Hindernisse eines Sektors: Asteroiden und Stationskörper.</summary>
    public List<Obstacle> GetObstacles(SectorId sector)
    {
        var s = Sectors[sector];
        var list = new List<Obstacle>(s.Asteroids.Count + s.Stations.Count);
        foreach (var a in s.Asteroids) list.Add(a.ToObstacle());
        foreach (var id in s.Stations) list.Add(new Obstacle(Stations[id].Position, Station.BodyRadius));
        return list;
    }

    /// <summary>Kürzester Weg als Liste der zu durchfliegenden Gates (BFS), null wenn nicht erreichbar.</summary>
    public List<GateId>? FindRoute(SectorId from, SectorId to)
    {
        if (from == to) return new List<GateId>();
        var prev = new Dictionary<SectorId, (SectorId from, GateId gate)>();
        var seen = new HashSet<SectorId> { from };
        var queue = new Queue<SectorId>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            foreach (var gid in Sectors[s].Gates)
            {
                var gate = Gates[gid];
                if (!gate.Open || !Gates.TryGetValue(gate.Target, out var far)) continue;
                if (!seen.Add(far.Sector)) continue;
                prev[far.Sector] = (s, gid);
                if (far.Sector == to)
                {
                    var route = new List<GateId>();
                    for (var cur = to; cur != from; cur = prev[cur].from) route.Insert(0, prev[cur].gate);
                    return route;
                }
                queue.Enqueue(far.Sector);
            }
        }
        return null;
    }

    /// <summary>Anzahl Gate-Sprünge zwischen zwei Sektoren, -1 wenn nicht erreichbar.</summary>
    public int Hops(SectorId from, SectorId to) => FindRoute(from, to)?.Count ?? -1;

    // Routen werden je Tick zwischengespeichert: KI-Systeme fragen sie in verschachtelten Schleifen ab
    // (Gates können sich zwischen Ticks ändern, innerhalb eines Ticks nicht).
    readonly Dictionary<(SectorId, SectorId), List<GateId>?> _routes = new();
    long _routesTick = -1;

    public List<GateId>? CachedRoute(SectorId from, SectorId to)
    {
        if (_routesTick != Tick) { _routes.Clear(); _routesTick = Tick; }
        if (!_routes.TryGetValue((from, to), out var r)) _routes[(from, to)] = r = FindRoute(from, to);
        return r;
    }

    public bool IsReachable(SectorId from, SectorId to) => CachedRoute(from, to) is not null;
}
