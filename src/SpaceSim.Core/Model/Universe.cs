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

    public void MoveShip(Ship ship, SectorId to)
    {
        if (Sectors.TryGetValue(ship.Sector, out var from)) from.Ships.Remove(ship.Id);
        ship.Sector = to;
        Sectors[to].Ships.Add(ship.Id);
    }

    /// <summary>Anzahl Gate-Sprünge zwischen zwei Sektoren (BFS), -1 wenn nicht erreichbar.</summary>
    public int Hops(SectorId from, SectorId to)
    {
        if (from == to) return 0;
        var seen = new HashSet<SectorId> { from };
        var queue = new Queue<(SectorId s, int d)>();
        queue.Enqueue((from, 0));
        while (queue.Count > 0)
        {
            var (s, d) = queue.Dequeue();
            foreach (var gid in Sectors[s].Gates)
            {
                var gate = Gates[gid];
                if (!gate.Open || !Gates.TryGetValue(gate.Target, out var far)) continue;
                if (!seen.Add(far.Sector)) continue;
                if (far.Sector == to) return d + 1;
                queue.Enqueue((far.Sector, d + 1));
            }
        }
        return -1;
    }
}
