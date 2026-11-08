namespace SpaceSim.Core.Model;

public sealed class Universe
{
    public long Tick;
    public Dictionary<SectorId, Sector> Sectors = new();
    public Dictionary<GateId, Gate> Gates = new();
    public Dictionary<StationId, Station> Stations = new();
    public Dictionary<ShipId, Ship> Ships = new();
    public Dictionary<FactionId, Faction> Factions = new();
    public Dictionary<GoodId, Good> Goods = new();

    /// <summary>Der einzige Sektor, der als Godot-Szene materialisiert ist.</summary>
    public SectorId PlayerSector;
    public ShipId PlayerShip;

    /// <summary>Statische Hindernisse eines Sektors: Asteroiden und Stationskörper.</summary>
    public List<Obstacle> GetObstacles(SectorId sector)
    {
        var s = Sectors[sector];
        var list = new List<Obstacle>(s.Asteroids.Count + s.Stations.Count);
        list.AddRange(s.Asteroids);
        foreach (var id in s.Stations) list.Add(new Obstacle(Stations[id].Position, Station.BodyRadius));
        return list;
    }

    public void MoveShip(Ship ship, SectorId to)
    {
        if (Sectors.TryGetValue(ship.Sector, out var from)) from.Ships.Remove(ship.Id);
        ship.Sector = to;
        Sectors[to].Ships.Add(ship.Id);
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
}
