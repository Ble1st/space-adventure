using SpaceSim.Core.Model;

namespace SpaceSim.Core.Events;

/// <summary>Basistyp aller Simulationsereignisse. Module koppeln sich über Events statt über direkte Aufrufe.</summary>
public abstract record SimEvent;

public sealed record TradeCompleted(ShipId Ship, StationId From, StationId To, GoodId Good, float Amount, float Revenue) : SimEvent;
public sealed record MineDelivered(ShipId Ship, StationId To, GoodId Good, float Amount, float Revenue) : SimEvent;
public sealed record StationBuilt(StationId Station, SectorId Sector, string TypeKey) : SimEvent;
public sealed record StationRemoved(StationId Station, SectorId Sector, string Reason) : SimEvent;
public sealed record ShipSpawned(ShipId Ship) : SimEvent;
public sealed record ShipRemoved(ShipId Ship, string Reason) : SimEvent;

/// <summary>Einfacher synchroner Ereignisbus. Wird nicht gespeichert; Abonnenten (Statistik, Darstellung) hängen sich nach dem Laden neu an.</summary>
public sealed class SimEvents
{
    public event Action<SimEvent>? Raised;
    public void Publish(SimEvent e) => Raised?.Invoke(e);
}
