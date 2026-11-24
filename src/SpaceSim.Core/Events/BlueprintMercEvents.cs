using SpaceSim.Core.Model;

namespace SpaceSim.Core.Events;

/// <summary>Bauplan gespeichert (neu oder ersetzt).</summary>
public sealed record BlueprintSaved(int Id, string Name, string ModelKey) : SimEvent;
/// <summary>Bauplan gelöscht.</summary>
public sealed record BlueprintDeleted(int Id) : SimEvent;
/// <summary>Bauplan beim Ausrüster angewendet (Credits negativ = bezahlt).</summary>
public sealed record BlueprintApplied(ShipId Ship, StationId Station, int Blueprint, int Installed, int Missing, float Credits) : SimEvent;
/// <summary>Schiff nach Bauplan in der Werft gekauft.</summary>
public sealed record BlueprintPurchased(ShipId Ship, StationId Station, int Blueprint, float Price) : SimEvent;
/// <summary>Söldner angeheuert: Schiff, Anbieter, Tage, Tagessold.</summary>
public sealed record MercenaryHired(ShipId Ship, ActorId Actor, StationId From, int Days, float FeePerDay) : SimEvent;
/// <summary>Söldnersold für einen Tag bezahlt.</summary>
public sealed record MercenaryPaid(ShipId Ship, ActorId Actor, int Day, float Amount) : SimEvent;
/// <summary>Söldnervertrag beendet (Grund: expired, broke, destroyed, cancelled); das Schiff kehrt heim.</summary>
public sealed record MercenaryEnded(ShipId Ship, ActorId Actor, string Reason) : SimEvent;
