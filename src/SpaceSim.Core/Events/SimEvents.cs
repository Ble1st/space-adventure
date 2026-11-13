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

// ───────── Phase 9: Akteure und Kassen ─────────
/// <summary>
/// Vermögensabgaben eines Upkeep-Schritts (Phase 9): <paramref name="Amount"/> floss in die Kasse des Akteurs <paramref name="Treasury"/>
/// (zuständige Fraktion). Je Schritt und Kasse ein Ereignis; Abgaben ohne zuständige Fraktion verschwinden (<see cref="TaxSunk"/>).
/// </summary>
public sealed record TaxCollected(ActorId Treasury, float Amount) : SimEvent;
/// <summary>Vermögensabgaben eines Upkeep-Schritts ohne Empfänger (keine zuständige Fraktion oder <see cref="Model.EconomyRules.TaxToTreasuryShare"/> &lt; 1): Geldsenke.</summary>
public sealed record TaxSunk(float Amount) : SimEvent;
/// <summary>
/// Zukauf (Phase 9, POLITIK.md „Zukauf durch kleinere Akteure“): Die gewichtete Ziehung hat einen Geldgeber gewählt. <paramref name="Purpose"/> =
/// "station" oder "ship"; <paramref name="Source"/> = Kasse des Akteurs, Betriebskasse einer Station oder eines Schiffs; <paramref name="Holder"/> ist der
/// Akteur dahinter (bei freien Einzelschiffen die Sammelgruppe, vor dem Aufstieg), <paramref name="Class"/> seine Größenklasse.
/// </summary>
public sealed record InvestorChosen(string Purpose, Simulation.InvestorSource Source, ActorId Holder, ActorSizeClass Class, float Amount, int Candidates) : SimEvent;
/// <summary>Ein freies Einzelschiff ist zum eigenen Akteur aufgestiegen (lazy, beim ersten Investieren).</summary>
public sealed record ActorPromoted(ActorId Actor, ShipId Ship) : SimEvent;
/// <summary>Ein Akteur ohne Besitz und Geld wurde entfernt (Abstieg).</summary>
public sealed record ActorRemoved(ActorId Actor, string Reason) : SimEvent;

// ───────── Phase 10: Gate-Zugang, Sperren, Sektorkontrolle, Regierungsstationen ─────────
/// <summary>Zustand eines Gates hat sich geändert (offen, eingeschränkt, geschlossen, zerstört).</summary>
public sealed record GateStateChanged(GateId Gate, GateState From, GateState To) : SimEvent;
/// <summary>
/// Sperrstufe eines Sektors hat sich geändert (<see cref="Simulation.FactionPolicySystem"/>): Einreise-/Ausreisestufe vorher und nachher, die Fraktion
/// dahinter (Besitzer), gegen wen (schlechteste Beziehung) und warum ("relation", "expired", "stage", "owner").
/// </summary>
public sealed record SectorAccessChanged(SectorId Sector, FactionId Authority, int FromEntry, int ToEntry, int FromExit, int ToExit, FactionId? Against, string Reason) : SimEvent;
/// <summary>Der abgeleitete Besitzer eines Sektors hat gewechselt (Einfluss, mit Hysterese).</summary>
public sealed record SectorOwnerChanged(SectorId Sector, FactionId From, FactionId To) : SimEvent;
/// <summary>Kontrollstufe oder „umkämpft“ eines Sektors hat sich geändert.</summary>
public sealed record SectorControlChanged(SectorId Sector, ControlStage From, ControlStage To, bool Contested) : SimEvent;
/// <summary>Zoll an einem Gate gezahlt (von der Kasse des Schiffs an die Kasse der Sektorfraktion).</summary>
public sealed record TollPaid(ShipId Ship, GateId Gate, FactionId To, float Amount) : SimEvent;
/// <summary>Eine laufende Reise wurde wegen einer Sperre über einen Umweg neu geplant.</summary>
public sealed record ShipRerouted(ShipId Ship, GateId BlockedGate, string Reason) : SimEvent;
/// <summary>Ein Ziel ist für das Schiff nicht (mehr) erreichbar: Reise nicht angetreten oder angehalten, Auftrag frei, Fracht bleibt.</summary>
public sealed record RouteBlocked(ShipId Ship, SectorId Target, string Reason) : SimEvent;
/// <summary>
/// Unterhalt aus einer Fraktionskasse gezahlt (Geldsenke): Regierungsstationen ("government") und Durchsetzung von Sperren ("lock");
/// Unpaid = was die Kasse nicht decken konnte.
/// </summary>
public sealed record TreasuryUpkeep(ActorId Actor, string Purpose, float Amount, float Unpaid) : SimEvent;

// ───────── Phase 11: Lagekarte, Polizei, Fahndung, Piraten ─────────
/// <summary>Einnahme einer Kasse außerhalb der Abgaben (Phase 11): Hehlerei der Piraten ("fence"), Strafen ("fine"). Macht die Geldmenge erklärbar.</summary>
public sealed record TreasuryIncome(ActorId Actor, string Purpose, float Amount) : SimEvent;
/// <summary>Phase 15: Ein KI-Schiff hat sehr lange nichts gefunden und wurde abgebaut (<see cref="Simulation.FleetSystem"/>); Credits = sein Betriebsgeld, ToOwner = an die Kasse des Besitzers (sonst verlässt es die Welt).</summary>
public sealed record ShipRetired(ShipId Ship, ActorId Owner, float Credits, bool ToOwner) : SimEvent;
/// <summary>Phase 15: Die Kasse einer Fraktion hat eine notleidende Station gestützt (<see cref="Simulation.SubsidySystem"/>; zusätzlich <see cref="TreasuryUpkeep"/> „subsidy“).</summary>
public sealed record StationSubsidized(StationId Station, ActorId Actor, float Amount) : SimEvent;
/// <summary>
/// Überfall aufgelöst (Phase 11, <see cref="Simulation.PirateSystem"/>): Kaperschiff, Opfer, Sektor, ob der Angreifer gewann, Beutewert (Basispreis),
/// Hehlererlös in die Kasse, zerstörte Schiffe, ob die Polizei eingriff und ob das Opfer der Spieler war.
/// </summary>
public sealed record RaidResolved(SectorId Sector, ShipId Raider, ShipId Victim, bool AttackerWon, float LootValue, float Fenced,
    bool VictimDestroyed, bool RaiderDestroyed, ShipId Police, bool Player, string Message) : SimEvent;
/// <summary>Eine Streife hat ein lauerndes Kaperschiff gestellt (Phase 11): Sieger, Kaperschiff zerstört oder geflohen.</summary>
public sealed record RaiderIntercepted(SectorId Sector, ShipId Police, ShipId Raider, bool PoliceWon, bool RaiderDestroyed) : SimEvent;
/// <summary>Vorwarnung an den Spieler (Phase 11): Begegnung angekündigt (Überfall oder Abfangen), aufgelöst bei <paramref name="DueTick"/>.</summary>
public sealed record EncounterWarning(EncounterKind Kind, SectorId Sector, ShipId Source, ShipId Target, long DueTick, string Message) : SimEvent;
/// <summary>Angekündigte Begegnung entfallen: der Spieler hat angedockt oder den Sektor verlassen.</summary>
public sealed record EncounterEscaped(EncounterKind Kind, ShipId Source, ShipId Target, string Message) : SimEvent;
/// <summary>Die Polizei hat den Spieler gestellt (Fahndungsstufe „gesucht“): Strafe eingezogen, Schmuggelware beschlagnahmt.</summary>
public sealed record PlayerIntercepted(SectorId Sector, FactionId Faction, float FineCollected, float SeizedUnits, string Message) : SimEvent;
/// <summary>Ein Verbrechen wurde gemeldet (Fahndung steigt): Akteur, Fraktion (Hoheitsgebiet), Verbrechen, neue Heat und Stufe.</summary>
public sealed record CrimeReported(ActorId Actor, FactionId Faction, string Crime, float Heat, int Level) : SimEvent;
/// <summary>Fahndungsstufe eines Akteurs bei einer Fraktion hat sich geändert (Verbrechen, Verfall, Strafe bezahlt).</summary>
public sealed record WantedChanged(ActorId Actor, FactionId Faction, int From, int To) : SimEvent;
/// <summary>Strafe am Gericht bezahlt (in die Kasse der Fraktion).</summary>
public sealed record FinePaid(ActorId Actor, FactionId Faction, float Amount) : SimEvent;
/// <summary>Kopfgeld gutgeschrieben (Claim, beim Kopfgeldbüro abzuholen) bzw. ausgezahlt (aus der Fraktionskasse).</summary>
public sealed record BountyEarned(ActorId Hunter, FactionId Faction, ShipId Target, float Amount) : SimEvent;
public sealed record BountyPaid(ActorId Hunter, FactionId Faction, float Amount) : SimEvent;
/// <summary>Polizeischiff gekauft (über die Kaufregel aus der Fraktionskasse) bzw. abgebaut; Kaperschiff neu gebaut.</summary>
public sealed record PoliceCommissioned(FactionId Faction, ShipId Ship, StationId Yard, float Price) : SimEvent;
public sealed record PoliceDecommissioned(FactionId Faction, ShipId Ship) : SimEvent;
public sealed record RaiderBuilt(ActorId Actor, ShipId Ship, float Cost) : SimEvent;

// ───────── Phase 12: Befehle, Verbände, Spielerflotte, Hangar, Taxi, Leihschiff ─────────
/// <summary>Ein Schiff hat einen Befehl bekommen (Ebene „Befehl“).</summary>
public sealed record CommandGiven(ShipId Ship, string Kind, string Text) : SimEvent;
/// <summary>Ein Befehl ist beendet: erledigt ("done"), gescheitert ("failed") oder abgebrochen/ersetzt ("cancelled").</summary>
public sealed record CommandEnded(ShipId Ship, string Kind, string Outcome, string Text) : SimEvent;
/// <summary>Notfall-Ebene (Hülle kritisch, Tank leer): der Befehl ruht, das Schiff fliegt zur Reparatur; danach geht er weiter.</summary>
public sealed record EmergencyStarted(ShipId Ship, string Reason) : SimEvent;
public sealed record EmergencyEnded(ShipId Ship) : SimEvent;
/// <summary>Verband angelegt, geändert (Beitritt, Austritt, neuer Anführer, Befehl) oder aufgelöst.</summary>
public sealed record SquadronChanged(SquadronId Squadron, string Change, ShipId Ship) : SimEvent;
/// <summary>Liegegebühr an einer fremden Station gezahlt (Unpaid = was die Kasse nicht decken konnte).</summary>
public sealed record BerthFeePaid(ActorId Actor, ShipId Ship, StationId Station, float Amount, float Unpaid) : SimEvent;
/// <summary>Unterhalt der Spielerflotte (Schiffe mit Befehl) je Takt, Geldsenke wie der Unterhalt der KI-Schiffe.</summary>
public sealed record FleetUpkeepPaid(ActorId Actor, float Amount) : SimEvent;
/// <summary>Taxifahrt des Spielers (die Person reist, das Schiff bleibt): Preis und Dauer in Ticks.</summary>
public sealed record TaxiRide(StationId From, StationId To, float Price, long Ticks) : SimEvent;
/// <summary>Leihschiff gemietet (Gebühr an die Station, Pfand hinterlegt) bzw. zurückgegeben (Miete aus dem Pfand, Rest erstattet).</summary>
public sealed record ShipRented(ShipId Ship, StationId Station, float Fee, float Deposit) : SimEvent;
public sealed record RentalReturned(ShipId Ship, StationId Station, float Rent, float Refund) : SimEvent;
/// <summary>Der Spieler hat ein eigenes Schiff an einer Station verkauft.</summary>
public sealed record ShipSold(ShipId Ship, StationId Station, float Price) : SimEvent;

// ───────── Phase 13: Forschung ─────────
/// <summary>Ein Forschungstakt an einer Station: FP auf das Projekt gebucht (oder gestockt: <paramref name="Stalled"/> nennt den Grund), Personalkosten und Basiswert der verbrauchten Güter.</summary>
public sealed record ResearchCycle(StationId Station, FactionId Faction, string Project, float Points, float Credits, float GoodsValue, string? Stalled) : SimEvent;
/// <summary>Projekt abgeschlossen: die Freischaltungen gelten ab jetzt für alle Stationen der Fraktion.</summary>
public sealed record ResearchCompleted(FactionId Faction, string Project, long Tick) : SimEvent;
/// <summary>Der Spieler hat ein Projekt beauftragt (Gebühr an die Station) bzw. mitfinanziert (Credits → FP).</summary>
public sealed record ResearchCommissioned(FactionId Faction, string Project, float Fee) : SimEvent;
public sealed record ResearchFunded(FactionId Faction, string Project, float Credits, float Points) : SimEvent;

// ───────── Phase 14: Exportsenke ─────────
/// <summary>Eine Lieferung an eine Exportsenke wurde zur Charge (bezahlt beim Ankauf, fällig zum Tick <paramref name="DueTick"/>).</summary>
public sealed record ExportQueued(StationId Station, GoodId Good, float Amount, float Paid, long DueTick) : SimEvent;
/// <summary>Eine Charge ging an den Planeten: Ware aus der Welt, die Station erhielt <paramref name="Revenue"/> (= bezahlt × (1 + Aufschlag)); neu geschöpft ist Revenue − Paid.</summary>
public sealed record GoodsExported(StationId Station, GoodId Good, float Amount, float Paid, float Revenue) : SimEvent;

// ───────── Phase 18: Frachtreste ─────────
/// <summary>Ein Spielerschiff hat Fracht abgeworfen (Laderaum oder Dienst „trade“, Schlüssel „drop:&lt;id&gt;“): Die Ware verschwindet ohne Erlös aus der Welt.</summary>
/// <summary>
/// Phase 19: Der Lebenszyklus hat eine dauerhaft kaum ausgelastete Station stillgelegt (<see cref="Simulation.StationReview"/>), um ihren Platz für die
/// anhaltende Knappheit von <paramref name="ForGood"/> freizumachen (gebaut wird <paramref name="ForType"/>). Payout = Kasse + Lager zum Restwert an den Besitzer.
/// </summary>
public sealed record StationRetired(StationId Station, SectorId Sector, string TypeKey, string Name, ActorId Owner, float Payout, float Activity, GoodId ForGood, string ForType) : SimEvent;
/// <summary>Phase 19 (Messwert): Ein Händler hat bei einer Station mehr abgeladen, als sie annahm (letzter Ausweg ohne weiteren Käufer); Units = Überschuss.</summary>
public sealed record CargoDumped(ShipId Ship, StationId Station, GoodId Good, float Units) : SimEvent;
/// <summary>Phase 19: Die Kasse einer Fraktion hat für eine Baustelle ohne erreichbares Bauschiff eins gekauft (<see cref="Simulation.ConstructionRules.CommissionBuilder"/>; dazu <see cref="TreasuryUpkeep"/> „builder“).</summary>
public sealed record BuilderCommissioned(FactionId Faction, ShipId Ship, StationId Yard, StationId Site, float Cost) : SimEvent;
/// <summary>Phase 19: Ein KI-Händler hat Fracht, die keine erreichbare Station annahm, im All abgeworfen (statt sie unbezahlt bei einer vollen Station abzuladen).</summary>
public sealed record CargoDiscarded(ShipId Ship, SectorId Sector, GoodId Good, float Units) : SimEvent;
public sealed record CargoJettisoned(ShipId Ship, SectorId Sector, GoodId Good, float Units) : SimEvent;

/// <summary>
/// Phase 20: Versorgungspanik — im Kartenteil mit dem kleinsten Sektor <paramref name="Part"/> fehlt seit <see cref="Model.EconomyRules.PanicDelayTicks"/> der letzte
/// Hersteller einer dort gebrauchten Ware, weil das Gate <paramref name="Gate"/> (default = keins gefunden) geschlossen oder zerstört ist. <paramref name="TypeKey"/>:
/// Stationstyp, den die Panik bauen will (null = im Teil nicht herstellbar).
/// </summary>
public sealed record SupplyPanicRaised(SectorId Part, GoodId Good, GateId Gate, string? TypeKey) : SimEvent;
/// <summary>Phase 20: Ersatz-Baustelle (oder Direktbau) einer Versorgungspanik angelegt.</summary>
public sealed record SupplyPanicBuild(StationId Site, SectorId Sector, GoodId Good, string TypeKey) : SimEvent;


// ───────── Phase 22: Abgänge aus drei Gründen, handlungsfähige Fraktionen ─────────
/// <summary>
/// Phase 22: Ein Schiff verlässt die Welt (vor <see cref="ShipRemoved"/> veröffentlicht, solange es noch existiert): Aufgabe, Besitzer, Sektor, Grund
/// (<see cref="DepartureReason"/>) und Anlass (z. B. „raid“, „intercept“, „decommissioned“, „spare_builder“, „idle“).
/// </summary>
public sealed record ShipDeparted(ShipId Ship, ShipRole Role, ActorId Owner, SectorId Sector, DepartureReason Reason, string Detail) : SimEvent;
/// <summary>Phase 22: Eine Station (oder Baustelle) verlässt die Welt; wie <see cref="ShipDeparted"/>. Anlass z. B. „retired“, „surplus“, „site_expired“.</summary>
public sealed record StationDeparted(StationId Station, string TypeKey, string Name, ActorId Owner, SectorId Sector, DepartureReason Reason, string Detail) : SimEvent;
/// <summary>
/// Phase 22: Eine Fraktion hat ihren Mindestbestand per Notkauf wiederhergestellt (Polizei oder Bauschiff ohne Werftmaterial an der Heimatbasis): bezahlt aus der
/// Kasse, erlassener Rest (neues Geld, klar ausgewiesene Quelle).
/// </summary>
public sealed record FactionRestored(FactionId Faction, ShipId Ship, ShipRole Role, float Paid, float Waived) : SimEvent;
/// <summary>Phase 22: Kapitalausgleich innerhalb eines Akteurs (ein reiches Schiff gibt einem armen Geschwisterschiff Betriebsgeld; kein neues Geld).</summary>
public sealed record FirmCapitalMoved(ActorId Owner, ShipId From, ShipId To, float Amount) : SimEvent;

// ───────── Phase 24: Trefferregel und Rundengefecht ─────────
/// <summary>Phase 24: Ein Gefecht hat begonnen (Überfall oder Abfangen zwischen NPCs): Angreifer und angegriffenes Hauptschiff.</summary>
public sealed record EncounterStarted(int Encounter, SectorId Sector, EncounterCause Cause, ShipId Attacker, ShipId Defender, string Message) : SimEvent;
/// <summary>
/// Phase 24: Ein Schuss im Gefecht (Trefferregel <see cref="Combat.Hit.Roll"/>): Schütze, Ziel, Waffenmodul, Art, Treffer, Schaden an Schild und Hülle.
/// Die Darstellung zeichnet ihn als Linie, wenn er im Spielersektor fällt (wie <see cref="MinerShot"/>).
/// </summary>
public sealed record ShotFired(int Encounter, SectorId Sector, ShipId Shooter, ShipId Target, string Weapon, DamageType Type, bool Hit, float ShieldDamage, float HullDamage) : SimEvent;
/// <summary>Phase 24: Ein Teilnehmer hat das Gefecht verlassen (zerstört, geflohen, Fracht abgeworfen, Beute genommen, Zeit um).</summary>
public sealed record CombatExited(int Encounter, ShipId Ship, ShipRole Role, CombatExit Exit) : SimEvent;
/// <summary>Phase 24: Ein Händler hat sich ergeben und Fracht abgeworfen; der Kaperer nimmt sie (Basiswert) und bricht ab.</summary>
public sealed record CargoSurrendered(int Encounter, ShipId Ship, ShipId Taker, float Value) : SimEvent;
/// <summary>Phase 25: Ein vom Spieler überfallenes Schiff wirft Fracht ab (an <paramref name="Position"/>); die Ansicht macht daraus Brocken für den Traktorstrahl.</summary>
public sealed record CargoDropped(int Encounter, SectorId Sector, ShipId Ship, Vec2 Position, GoodId Good, float Units) : SimEvent;
/// <summary>Phase 25: Das aktive Schiff ist verloren; die Person sitzt jetzt in <paramref name="NewShip"/> an <paramref name="Station"/> (eigenes Schiff, Leihschiff oder Notfall-Leihschiff).</summary>
public sealed record PlayerShipLost(ShipId Lost, StationId Station, ShipId NewShip, string How, string Message) : SimEvent;
/// <summary>Phase 25: Der Spieler hat geschossen (Echtzeit, gleiche Trefferregel wie NPCs; das Ereignis <see cref="ShotFired"/> folgt).</summary>
public sealed record AmmoTransferred(ShipId Ship, float Units) : SimEvent;
/// <summary>Phase 25 (Korrektur): Ein Leihschiff ging verloren — Pfand verfällt an die Verleihstation, offene Miete aus der Kasse (notfalls ins Minus).</summary>
public sealed record RentalLost(ShipId Ship, StationId Station, float DepositForfeited, float RentPaid) : SimEvent;
/// <summary>Phase 25: Spieler greift an.</summary>
public sealed record PlayerAttacked(int Encounter, ShipId Target, bool Crime) : SimEvent;
/// <summary>
/// Phase 24: Ein Gefecht ist zu Ende. <paramref name="Outcome"/>: „surrendered“ (Fracht abgeworfen), „fled“ (eine Seite entkommen), „destroyed“ (eine Seite
/// zerstört), „timeout“ (Höchstdauer), „ended“ (Teilnehmer weggefallen). Dauer in Ticks, Schüsse/Treffer, Beute und Zahl der Abgänge je Art.
/// </summary>
public sealed record EncounterEnded(int Encounter, SectorId Sector, EncounterCause Cause, string Outcome, long Ticks, int Shots, int Hits, float Loot,
    int Destroyed, int Fled, int Surrendered, string Message) : SimEvent;
/// <summary>Phase 24: Munition nachgeladen (Station = Lager, default = ohne Lager zum Basispreis × Aufschlag); bezahlt von <paramref name="Payer"/> (Schiff oder Kasse).</summary>
public sealed record AmmoRearmed(ShipId Ship, StationId Station, float Units, float Cost, ActorId Payer, bool FromTreasury) : SimEvent;

// ───────── Phase 26: Schaden und Bergung ─────────
/// <summary>Phase 26: Ein Treffer hat ein Modul beschädigt (<paramref name="Failed"/>: es fiel dabei unter die Ausfallschwelle).</summary>
public sealed record ModuleDamaged(ShipId Ship, int Slot, string Module, float Condition, bool Failed) : SimEvent;
/// <summary>Phase 26: Ein Modul hat Feuer gefangen.</summary>
public sealed record FireStarted(ShipId Ship, SectorId Sector, int Slot, string Module) : SimEvent;
/// <summary>Phase 26: Ein Brand ist vorbei — <paramref name="How"/> „extinguished“ (gelöscht), „burnt_out“ (Modul ausgefallen), „docked“ (angedockt), „repaired“, „lost“ (Schiff weg).</summary>
public sealed record FireEnded(ShipId Ship, int Slot, string Module, string How) : SimEvent;
/// <summary>Phase 26: Wrack oder Frachtbehälter entstanden (Fracht in Einheiten, Zahl der bergbaren Module).</summary>
public sealed record WreckCreated(int Wreck, SectorId Sector, WreckKind Kind, ShipId From, float CargoUnits, int Modules) : SimEvent;
/// <summary>Phase 26: Aus einem Wrack wurde etwas geborgen — Fracht (<paramref name="Good"/>, <paramref name="Units"/>) oder ein Modul (<paramref name="Module"/>, Restzustand).</summary>
public sealed record WreckSalvaged(int Wreck, ShipId Ship, GoodId Good, float Units, string? Module, float Condition, bool ByNpc) : SimEvent;
/// <summary>Phase 26: Ein Wrack ist verfallen (Frist abgelaufen) mit dem, was noch darin war.</summary>
public sealed record WreckExpired(int Wreck, WreckKind Kind, float CargoUnits, int Modules) : SimEvent;
/// <summary>
/// Phase 26: eine Reparatur (Hülle und/oder Module) — über den Dienst „repair“ (<paramref name="AtBase"/> false; Spieler und NPC) oder an der Heimatbasis aus der
/// Kasse (Polizei, Piraten, Fraktionsbauschiffe). Material in Einheiten, Kosten in Credits.
/// </summary>
public sealed record ShipRepaired(ShipId Ship, StationId Station, float Hull, int Modules, float Cost, float Plates, float Electronics, bool AtBase) : SimEvent;
/// <summary>Phase 26 (Korrektur): ein KI-Schiff fliegt zur Reparatur (Station) — schwer beschädigt.</summary>
public sealed record RepairTripStarted(ShipId Ship, StationId Station, float HullShare) : SimEvent;
/// <summary>Phase 26: tägliche Leihmiete abgerechnet (aus der Kasse; <paramref name="Debt"/> = was davon ins Minus ging).</summary>
public sealed record RentCharged(ShipId Ship, StationId Station, int Day, float Amount, float Debt) : SimEvent;

// ───────── Phase 27: Flottenkampf ─────────
/// <summary>
/// Phase 27: Ein Schiff ist einem laufenden Gefecht als Verstärkung beigetreten — <paramref name="Reason"/> „escort“ (Geleit/Eskorte), „guard“ (Wache),
/// „squadron“ (Verbandsmitglied), „pack“ (Rudel des Kaperers), „pursuit“ (Verfolger), „police“ (Streife im Sektor).
/// </summary>
public sealed record CombatJoined(int Encounter, ShipId Ship, int Side, string Reason) : SimEvent;
/// <summary>Phase 27: Ein Fliehender hat seine Zuflucht erreicht — <paramref name="Kind"/> „station“ (angedockt) oder „gate“ (durch das Tor gesprungen).</summary>
public sealed record RefugeReached(int Encounter, ShipId Ship, string Kind, SectorId Sector) : SimEvent;
/// <summary>Phase 27: Die Polizei nimmt die Verfolgung eines geflohenen Kaperschiffs auf (Ende: Gefecht, Grenze der Zuständigkeit oder Frist).</summary>
public sealed record PursuitStarted(ShipId Hunter, ShipId Target, SectorId Sector) : SimEvent;
/// <summary>Phase 27: Eine Verfolgung ist zu Ende — <paramref name="How"/> „caught“ (Gefecht), „border“ (Zuständigkeit endet), „expired“, „lost“ (Ziel weg), „safe“ (Ziel im Versteck).</summary>
public sealed record PursuitEnded(ShipId Hunter, ShipId Target, string How) : SimEvent;
/// <summary>Phase 27: Eine Fraktion bzw. Piratengruppe hat einen Verband gebildet (Streife, Rudel) oder aufgelöst (<paramref name="Formed"/>).</summary>
public sealed record ForceSquadron(ActorId Owner, SquadronId Squadron, string Kind, int Members, bool Formed) : SimEvent;
/// <summary>Phase 27: Eine Streife gibt einem Handelsschiff Geleit.</summary>
public sealed record EscortAssigned(ShipId Police, ShipId Ward, SectorId Sector) : SimEvent;
/// <summary>
/// Phase 27: Der Spieler hat sich ergeben — gegenüber der Polizei (<paramref name="ToPolice"/>: Strafe bezahlt, Schmuggelware beschlagnahmt) oder Kaperern
/// (Fracht abgeworfen, die Kaperer drehen ab).
/// </summary>
public sealed record PlayerSurrendered(bool ToPolice, SectorId Sector, float Value, string Message) : SimEvent;

// ───────── Phase 28: Raketen, Stationen, Krieg ─────────
/// <summary>Phase 28: Rakete abgefeuert (Schiff oder Station <paramref name="FromStation"/>), Ziel Schiff oder Station; <paramref name="FlightTicks"/> = erwartete Flugzeit.</summary>
public sealed record MissileLaunched(int Encounter, SectorId Sector, ShipId Shooter, StationId FromStation, ShipId Target, StationId TargetStation, string Weapon, float FlightTicks) : SimEvent;
/// <summary>Phase 28: Rakete am Ziel — <paramref name="Outcome"/> „hit“, „miss“, „intercepted“ (Punktabwehr) oder „lost“ (Ziel weg/entkommen).</summary>
public sealed record MissileResolved(int Encounter, SectorId Sector, ShipId Shooter, ShipId Target, StationId TargetStation, string Outcome, float ShieldDamage, float HullDamage) : SimEvent;
/// <summary>
/// Gerätetest Phase 28 (Runde 6): der Spieler hat seine Belagerung verlassen; seine eigenen Schiffe brechen ab und folgen ihm. Runde 7: <paramref name="Won"/> =
/// die Station ist ausgeschaltet bzw. gefallen (Siegmeldung statt „Rückzug“).
/// </summary>
public sealed record SiegeWithdrawn(int Encounter, StationId Station, ShipId Player, int Wingmen, string Message, bool Won = false) : SimEvent;
/// <summary>Phase 28: Ein Stationsgeschütz hat auf ein Schiff geschossen.</summary>
public sealed record StationShot(int Encounter, SectorId Sector, StationId Station, ShipId Target, string Weapon, DamageType Type, bool Hit, float ShieldDamage, float HullDamage) : SimEvent;
/// <summary>Phase 28: Ein Schiff hat auf eine Station geschossen.</summary>
public sealed record ShotAtStation(int Encounter, SectorId Sector, ShipId Shooter, StationId Station, string Weapon, DamageType Type, bool Hit, float ShieldDamage, float HullDamage) : SimEvent;
/// <summary>Phase 28: Eine Station ist gefallen — <paramref name="How"/> „destroyed“ (über <see cref="SpaceSim.Core.Simulation.Departures"/>), „captured“ (Besitzer wechselt) oder „disabled“ (Regierung/unzerstörbar: ausgeschaltet).</summary>
public sealed record StationFell(StationId Station, string Name, SectorId Sector, ActorId OldOwner, ActorId Attacker, string How) : SimEvent;
/// <summary>Phase 28: Station repariert (Hüllenpunkte, Hüllenplatten aus dem Lager, Kosten aus der Kasse bzw. Stationskasse).</summary>
public sealed record StationRepaired(StationId Station, float Hull, float Plates, float Cost) : SimEvent;
/// <summary>Phase 28: Belagerung begonnen (Angreifer-Fraktion, Station).</summary>
public sealed record SiegeStarted(int Encounter, StationId Station, SectorId Sector, ActorId Attacker, string Message) : SimEvent;
/// <summary>Phase 28: Krieg erklärt (A erklärt B).</summary>
public sealed record WarDeclared(FactionId A, FactionId B, float Relation, string Message) : SimEvent;
/// <summary>Phase 28: Frieden (Grund „weary“, „broke“, „manual“).</summary>
public sealed record PeaceConcluded(FactionId A, FactionId B, string Reason, long Ticks, string Message) : SimEvent;
/// <summary>Phase 28: Kriegsschiff gekauft (Fraktion, Schiff, Werft, Preis).</summary>
public sealed record WarshipCommissioned(FactionId Faction, ShipId Ship, StationId Yard, float Price) : SimEvent;
/// <summary>Phase 28: Kriegsschiff nach dem Frieden oder wegen der Rücklage abgebaut.</summary>
public sealed record WarshipDecommissioned(FactionId Faction, ShipId Ship, string Reason) : SimEvent;
/// <summary>Phase 28 (Nacharbeit): ein KI-Händler hat am Einkaufsort geladen (Diagnose; nur Bus, nicht gespeichert).</summary>
public sealed record TraderBought(ShipId Ship, StationId Station, GoodId Good, float Units, float Cost) : SimEvent;
/// <summary>Phase 28 (Nacharbeit): ein KI-Händler hat am Einkaufsort nicht gekauft, weil der Preis dort den Preis seines Abnehmers erreicht hat (sicherer Verlust).</summary>
public sealed record TraderBuyRefused(ShipId Ship, StationId Seller, StationId Buyer, GoodId Good, float BuyPrice, float SellPrice) : SimEvent;
