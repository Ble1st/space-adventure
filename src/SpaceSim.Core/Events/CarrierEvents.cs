using SpaceSim.Core.Model;

namespace SpaceSim.Core.Events;

/// <summary>Träger: Bordjäger gestartet (<paramref name="Count"/> Schiffe; <paramref name="Auto"/> = beim Gefechtsbeginn von selbst).</summary>
public sealed record CarrierLaunched(ShipId Carrier, int Count, bool Auto) : SimEvent;

/// <summary>Träger: ein Bordjäger ist eingeschleust (<paramref name="Reason"/>: „manual“, „return“, „left“ = Träger verließ den Sektor, „docked“ = Träger legte an).</summary>
public sealed record CarrierFighterDocked(ShipId Carrier, ShipId Fighter, string Reason) : SimEvent;

/// <summary>Träger: mit dem Träger gingen <paramref name="Count"/> eingeschleuste Bordjäger verloren.</summary>
public sealed record CarrierFightersLost(ShipId Carrier, int Count, string Reason) : SimEvent;
