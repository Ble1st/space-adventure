using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Gemeinsame abstrakte Reise für KI-Systeme: Fahrplan aus Gate-Sprüngen statt Physik.</summary>
public static class Travel
{
    public const int TicksPerHop = 20;
    public const int TicksLocal = 5;

    public static int Ticks(Universe u, SectorId from, SectorId to) =>
        TicksLocal + Math.Max(u.Hops(from, to), 0) * TicksPerHop;

    public static void Start(Universe u, Ship ship, SectorId to)
    {
        ship.Transit = new Transit
        {
            StartTick = u.Tick,
            ArrivalTick = u.Tick + Ticks(u, ship.Sector, to),
            ToSector = to,
        };
    }
}
