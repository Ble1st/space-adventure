using SpaceSim.Core.Model;

namespace SpaceSim.Core.Events;

/// <summary>Basistyp aller Simulationsereignisse. Module koppeln sich über Events statt über direkte Aufrufe.</summary>
public abstract record SimEvent;

public sealed record TradeCompleted(ShipId Ship, StationId From, StationId To, GoodId Good, float Amount, float Revenue) : SimEvent;
public sealed record MineDelivered(ShipId Ship, StationId To, GoodId Good, float Amount, float Revenue) : SimEvent;
public sealed record StationBuilt(StationId Station, SectorId Sector, string TypeKey) : SimEvent;
public sealed record StationRemoved(StationId Station, SectorId Sector, string Reason) : SimEvent;
public sealed record OreCollected(ShipId Ship, GoodId Good, float Units) : SimEvent;
public sealed record PlayerSold(ShipId Ship, StationId Station, GoodId Good, float Units, float Revenue) : SimEvent;
public sealed record PlayerBought(ShipId Ship, StationId Station, GoodId Good, float Units, float Cost) : SimEvent;
/// <summary>
/// Ein Stationsdienst wurde ausgeführt. Wird nur bei Erfolg veröffentlicht (mindestens eine Einheit bewegt, bei "alles verkaufen" Erlös &gt; 0);
/// Units = bewegte Einheiten, Credits = Veränderung der Schiffs-Credits (negativ = Zahlung), Offer = ausgeführter Angebots-Schlüssel ohne Ware ("buy", "sell", "sell_all"), Good = default bei "alles verkaufen".
/// </summary>
public sealed record ServicePerformed(ShipId Ship, StationId Station, string Service, string Offer, GoodId Good, float Units, float Credits) : SimEvent;
/// <summary>Ein NPC-Miner hat einen Laserschuss abgegeben (Units = 0: Fehlschuss). Die Darstellung zeigt ihn, wenn er im Spielersektor fällt.</summary>
public sealed record MinerShot(SectorId Sector, ShipId Ship, int Asteroid, Vec2 AsteroidPosition, float AsteroidRadius, float Units, GoodId Good = default) : SimEvent;
public sealed record AsteroidDepleted(SectorId Sector, int Asteroid) : SimEvent;
public sealed record AsteroidRespawned(SectorId Sector, int Asteroid) : SimEvent;
public sealed record ShipSpawned(ShipId Ship) : SimEvent;
public sealed record ShipRemoved(ShipId Ship, string Reason) : SimEvent;
/// <summary>Ein Schiff hat an einer Station Treibstoff in den Tank gekauft (Marktpreis; Bestand, Stationskasse und Schiffscredits sind gebucht). Phase 4b.</summary>
public sealed record FuelBought(ShipId Ship, StationId Station, GoodId Good, float Units, float Cost) : SimEvent;
/// <summary>Notkauf bei leerem Tank (Phase 4b): Treibstoff zum Aufpreis (2× Basispreis), der Erlös fließt an keine Station und verschwindet (Geldsenke).</summary>
public sealed record EmergencyRefuel(ShipId Ship, GoodId Good, float Units, float Cost) : SimEvent;

/// <summary>Der Spieler hat an einer Station ein Schiff gekauft (Phase 5c, Dienst „shipdealer“): neues Schiff, Modell, Preis.</summary>
public sealed record ShipPurchased(ShipId Ship, StationId Station, string ModelKey, float Price) : SimEvent;
/// <summary>KI-Neubau (Phase 5c, <see cref="Simulation.FleetSystem"/>): Werft, Preis und ob die Module des Fits aus dem Werftlager verbraucht wurden (sonst nur der Bausatz).</summary>
public sealed record FleetShipBuilt(ShipId Ship, StationId Yard, bool WithModules, float Price) : SimEvent;
/// <summary>Phase 6: Die KI-Flotte hat für eine Aufgabe ein Modell gewählt (Kaufwahl nach Aufgabe, Geld und Werftlager; <see cref="Simulation.FleetSystem"/>).</summary>
public sealed record FleetModelChosen(ShipId Ship, string ModelKey, ShipRole Job, float Price) : SimEvent;
/// <summary>Der Spieler steuert jetzt ein anderes eigenes Schiff (Phase 5c: nach Kauf oder Wechsel im Hangar); das alte bleibt geparkt.</summary>
public sealed record ActiveShipChanged(ShipId Old, ShipId New, StationId Station) : SimEvent;
/// <summary>Ein Modul wurde ein- oder ausgebaut (Phase 5c, Dienst „outfitter“): Slot-Index, neues und altes Modul (null = leer).</summary>
public sealed record ShipRefitted(ShipId Ship, StationId Station, int Slot, string? Installed, string? Removed) : SimEvent;

/// <summary>Ein Multirole-Schiff hat seine Aufgabe gewechselt (Phase 6, <see cref="Simulation.JobAssignmentSystem"/>): altes und neues Etikett, Bewertungen (Credits/Tick).</summary>
public sealed record JobChanged(ShipId Ship, ShipRole From, ShipRole To, float FromScore, float ToScore) : SimEvent;

/// <summary>Phase 7: Güteklasse eines neuen Schiffs gewürfelt (Rolled) und erreicht (Grade; niedriger, wenn der Werft Material fehlte).</summary>
public sealed record ShipQualityRolled(ShipId Ship, ShipQuality Rolled, ShipQuality Grade) : SimEvent;
/// <summary>Phase 7: Upgrade-Versuch (Dienst „upgrade“ oder NPC-<see cref="Simulation.UpgradeSystem"/>): Art, Slot (−1 Bonusslot), Module, Erfolg, Chance, Preis.</summary>
public sealed record UpgradeAttempted(ShipId Ship, StationId Station, UpgradeKind Kind, int Slot, string? From, string? To, bool Success, float Chance, float Price) : SimEvent;

/// <summary>Phase 8: Baustelle angelegt (Stationstyp, Bausumme im Treuhandgeld, Stückliste in Einheiten gesamt).</summary>
public sealed record ConstructionStarted(StationId Site, SectorId Sector, string TypeKey, float Budget, float MaterialUnits) : SimEvent;
/// <summary>Phase 8: Material an eine Baustelle geliefert. By: "builder" (Bauschiff im Auftrag), "trader" (KI-Händler), "player", "emergency" (Notbau).</summary>
public sealed record ConstructionDelivered(StationId Site, ShipId Ship, GoodId Good, float Units, string By) : SimEvent;
/// <summary>Phase 8: Baustelle fertig, Station steht (zusätzlich <see cref="StationBuilt"/>); Emergency = Notbau hat Material ersetzt.</summary>
public sealed record ConstructionCompleted(StationId Station, SectorId Sector, string TypeKey, bool Emergency, ShipId Builder) : SimEvent;
/// <summary>Phase 8: Baustelle verfallen (ohne Fortschritt); Refund = an den Investor zurück, KeptMaterial = bleibt als Lager liegen.</summary>
public sealed record ConstructionAbandoned(StationId Site, string TypeKey, float Refund, bool KeptMaterial) : SimEvent;
/// <summary>Phase 8: Notbau — fehlende Materialien gegen Credits (Basispreis × Aufschlag, Geldsenke) ersetzt; Waived = nicht gedeckter Rest.</summary>
public sealed record EmergencyConstruction(StationId Site, FactionId Faction, float Units, float Cost, float Waived, bool Critical) : SimEvent;
/// <summary>Phase 8: Ein Spaltlaser hat einen Asteroiden in Fragmente zerlegt (Ids der neuen Fragmente, verlorenes Erz als Staub).</summary>
public sealed record AsteroidSplit(SectorId Sector, int Asteroid, IReadOnlyList<int> Fragments, float Dust, ShipId Ship) : SimEvent;

/// <summary>Einfacher synchroner Ereignisbus. Wird nicht gespeichert; Abonnenten (Statistik, Darstellung) hängen sich nach dem Laden neu an.</summary>
public sealed class SimEvents
{
    public event Action<SimEvent>? Raised;
    public void Publish(SimEvent e) => Raised?.Invoke(e);
}
