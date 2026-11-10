# Umsetzungsplan

Gesamtplan für alles, was in den Planungsdokumenten steht. Reihenfolge nach Abhängigkeit und nach dem Grundsatz **Tiefe vor Breite**: Zuerst wird die Schiffs- und Handelskette vollständig, dann folgen Welt, Politik und Flotten. Der Plan ist ein Vorschlag, nichts davon ist begonnen. Größen: **S** (kleiner Schritt), **M** (mehrere Dateien, eigene Tests), **L** (neues Teilsystem mit Save-Änderung und Balance-Arbeit).

Verweise: [UI.md](UI.md), [SCHIFFE.md](SCHIFFE.md), [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md), [STATIONEN.md](STATIONEN.md), [POLITIK.md](POLITIK.md), [FLOTTEN.md](FLOTTEN.md), [FORSCHUNG.md](FORSCHUNG.md), [ROADMAP.md](ROADMAP.md).

## 0. Arbeitsregeln für jede Phase

1. **Architektur zuerst:** Neue Funktion als `ISimSystem` in einem Modul (`DefaultModules()`), Kopplung über `Universe.Events`, Inhalte in `default-world.json`, `SpaceSim.Core` ohne Godot und Dateizugriff.
2. **Test zuerst bei Umbauten:** Vor einem Umbau müssen die vorhandenen Tests grün sein; der Umbau darf kein Verhalten ändern, bis die Phase es ausdrücklich will (`UniverseHash`-Tests belegen es).
3. **Persistenz:** Neue Zustände in `SaveGame`-DTOs, `UniverseSerializer` und `UniverseHash`; bei Formatbruch `SaveGame.CurrentVersion` erhöhen und eine Migration schreiben (Stand heute: v8).
4. **Balance:** Nach jeder Wirtschaftsänderung `tools/SpaceSim.Balance` laufen lassen und die Kennzahlen im Commit nennen.
5. **Abnahme:** `dotnet test`, `godot-mono --headless --path godot --fixed-fps 60 -- --selftest` und, wo sichtbar, ein Handtest der Oberfläche.
6. **Dokumentation:** `CLAUDE.md` und die betroffenen Dokumente in derselben Phase aktualisieren.
7. **Git:** Ein Commit pro Phase oder Teilschritt; Push nur auf Anfrage.

## 1. Überblick

| Phase | Inhalt | Größe | Save |
|---|---|---|---|
| 1 | Dienste-Rahmen (`IStationService`), Handel als Dienst | M | – |
| 2 | Fähigkeiten und Steuerer statt fester Rolle | M | – |
| 3 | Slot- und Modulmodell, Warenform und Kanister, Leistung und Speicherbank | L | v4 |
| 4 | Treibstoff: Ware, Tank, Tanken, Reparatur, Rettungsdienst | L | v5 |
| 5 | Modul-Waren, drei Modulfabriken (5a), Spielerkasse (5b), Ausrüster, Schiffskauf, Startpakete (5c) | L | v6 (5a), v7 (5b), v8 (5c) |
| 6 | Multirole: Job-Zuteilung, Kapazitätsgrenzen, erste Multirole-Modelle | M | – |
| 7 | NPC-Güteklassen und Upgrades | M | v7 |
| 8 | Bauschiff, Baustellen, Notbau, Asteroidenbrecher, Lagerhaus | L | v8 |
| 9 | Akteure, Fraktionen, Kasse, Beziehungen, Persönlichkeit, Zukauf | L | v9 |
| 10 | Gate-Zugang, Kontrollpunkte, Sektorkontrolle, Regierungsstationen | L | v10 |
| 11 | Polizei, Fahndung, Piraten, abstrakte Begegnungen | L | v11 |
| 12 | Verbände, Befehle, Spielerflotte und Oberfläche | L | v12 |
| 13 | Forschung | M | v13 |
| – | Zurückgestellt: Kampf, Eroberung, Sprungantrieb, Tarnfeld, Besatzung, Blockadebrecher, Abwracker | – | – |

Die Versionsnummern sind Platzhalter; eine Phase erhöht die Version nur, wenn das Format wirklich bricht.

## Phase 1: Dienste-Rahmen

**Ziel:** Das Dock-Panel kennt Dienste statt fester Knöpfe. Verhalten bleibt gleich.
- `IStationService` (`Key`, `IsAvailable`, `Offers`, `Execute`), Registry `StationServices`, Event `ServicePerformed`.
- Der heutige Markt (`TradeActions.Buy/Sell/SellAll`) wird der Dienst `trade`; die JSON-Station-Typen bekommen `services: ["trade"]`, Standard für bestehende Typen.
- Dock-Oberfläche zeichnet generisch aus `Offers` (umgesetzt mit UI Stufe 2: Andock-Menü und Bereiche unter `godot/scripts/Ui/Screens/`, `DockAreas` baut die Bereichsliste aus `ForStation`).
- **Tests:** Dienst-Registry, `trade` als Dienst liefert dieselben Ergebnisse wie `TradeActions`; Selbsttest unverändert grün.
- **Abnahme:** Kein sichtbarer Unterschied im Spiel, aber neue Dienste lassen sich allein per JSON und Registrierung ergänzen.

**Stand:** Schritt 1 (Core: `IStationService`, `StationServices`, `TradeService`, Event `ServicePerformed`, 16 Tests) und 1b (Nachbesserung: `ServiceOffer` mit Ware/Bestand/an Bord, strikte Angebotsschlüssel, Event nur bei Erfolg, Fehlpfad-Tests) sind umgesetzt. Schritt 2 (Dienste je Stationstyp: `services` in JSON, Standard `["trade"]`, Prüfung beim Weltaufbau, Speicherung ohne Versionssprung, `StationServices.ForStation`, 9 Tests) ist umgesetzt. Schritt 3 ist umgesetzt (UI-Umsetzung Stufe 2): Das Dock besteht aus Andock-Menü und Vollbild-Bereichen (`godot/scripts/Ui/Screens/`, Registry-Instanz in `GameSession.Services`); der Handel zeichnet Zeilen aus `IStationService.Offers` und führt Aktionen nur über `IStationService.Execute` aus (`buy:<id>`/`sell:<id>`/`sell_all`), das alte `DockPanel` entfällt. Selbsttest prüft Kauf/Verkauf/„Alles verkaufen“ über Dienst+Oberfläche, die Bereichsliste (Handel nur mit Dienst „trade“, Platzhalter inaktiv) und Abdocken aus einem Bereich; `MarketInfo` (beste Verkaufsstelle je Ware, Nachfrage/Überschuss) ist reine Core-Funktion mit Tests.

## Phase 2: Fähigkeiten und Steuerer

**Ziel:** Systeme fragen `Capability`, nicht `ShipRole`. Verhalten bleibt gleich.
- `Capability` (Flags: `Cargo`, `Mining`, `Combat`, `Construction`, `Salvage`), `Ship.Has()`, abgeleitet aus der Rolle, bis es Modelle gibt.
- `Controller` (`Player`/`Ai`) ersetzt die `ShipRole.Player`-Abfragen in `TradeActions`, Ereignissen und `Materializer`.
- `TraderSystem` und `MinerSystem` prüfen die Fähigkeit; `FleetSystem`, `UpkeepSystem`, `EconomyStats`, `StationLifecycleSystem` und `SectorView` folgen.
- `Research` als Schiffsfähigkeit wird gestrichen, weil kein Dokument sie braucht.
- **Tests:** Fähigkeit ohne Mining lässt Miner leer; alle bestehenden Tests unverändert; `UniverseHash` gleich.
- Ein erster Versuch lag schon vor (Fähigkeiten aus Rolle abgeleitet); er wurde bewusst verworfen, bis die Planung steht, und wurde neu umgesetzt.

**Stand:** Umgesetzt (Branch `phase2-faehigkeiten`): `Capability` (ohne `Research`) und `Controller` in `Model/Capability.cs`, `Ship.Capabilities/Controller/Has/IsPlayer/IsEconomyAi`, Ableitung aus der Rolle an allen Entstehungsstellen (`WorldBuilder.NewShip`, `Universe.AddShip`, `UniverseSerializer.FromSave`, `FleetSystem.Expand`), nichts wird gespeichert (v3 bleibt). `TradeActions`, `Materializer` und die Oberfläche erkennen den Spieler über `IsPlayer`; `TraderSystem` und `MinerSystem` verlangen zusätzlich `Cargo` bzw. `Mining`; `FleetSystem`, `UpkeepSystem`, `EconomyStats`, `StationLifecycleSystem` zählen über `IsEconomyAi`. 7 neue Tests, 209 insgesamt; Balance-Lauf (20000 Ticks) und Hash sind byte-identisch zu vorher.
**Bekannte Schwächen:** (1) ~~`Capability.None` als „nicht gesetzt“~~ **behoben:** `Ship.CapabilitiesSet/ControllerSet` unterscheiden „nie gesetzt“ von ausdrücklich `None`; `AddShip` leitet nur Ungesetztes ab. (2) ~~Fähigkeit entzogen = Schiff eingefroren~~ **behoben:** `JobRelease.Release` (Auftrag und Reservierung fallen weg, laufende Reise endet ohne Handel/Schürfen, Fracht bleibt). (3) ~~Fähigkeiten nicht gespeichert~~ **behoben:** `ShipSave.Capabilities/Controller` (nullable, Format weiter v3, alte Spielstände leiten aus der Rolle ab), beides im `UniverseHash`. Offen bleibt nur: neue Aufgaben teilt erst `JobAssignmentSystem` (Phase 6) zu; ab Phase 3 kommen die Fähigkeiten aus `ShipStats`.

## Phase 3: Slot- und Modulmodell, Warenform, Leistung

**Ziel:** Schiffe bestehen aus Klasse, Slots und Modulen. Einheitsmodelle bilden das heutige Verhalten nach.
- **Content:** Abschnitte `slotTypes`, `modules`, `shipClasses`, `shipModels` in `WorldDef`; `WorldBuilder` validiert (Schlüssel, Slotart, Größe, Leistungsbilanz, Frachtform) und wirft `WorldFormatException`.
- **Module in Phase 3:** `modules` ist eine eigene Content-Sektion (Registry für `Fit` und `ShipStats`); die **Waren zu den Modulen** entstehen erst in Phase 5 (siehe dort), Phase 3 verändert `goods` nicht außer um `form`.
- **Warenform:** `goods[].form` (`solid`/`liquid`); Frachtmodule mit `holds`; Universalraum (100 %) für migrierte Schiffe.
- **Leistung:** `ShipStats` rechnet `powerOutput`, `powerDraw`, `peakDraw`, Speicherbank (`powerStorage`) und leitet Tempo, Hülle, Laderaum, Laserleistung, Unterhalt und Fähigkeiten ab. `Ship.MaxHull` wird pro Schiff.
- **Save v4:** `ModelKey`, Slotliste und `Fit` je Schiff; Migration bildet bestehende Schiffe auf Einheitsmodelle ab; `UniverseHash` erweitert.
- **Heutige Pauschalwerte** (Fracht 100, Hülle 100, Tempo) kommen aus den Einheitsmodellen. `LaserShot.Roll` erhält die Laserstufe (`chunkChanceMult`, `chunkSizeMult`).
- **Tests:** Ungültige Fits werden abgelehnt; Einheitsmodell reproduziert Verhalten (Hash-Vergleich zu vorher mit denselben Seeds); Migration v3→v4.
- **Abnahme:** Balance-Lauf liefert dieselben Kennzahlen wie vor der Phase.

**Stand:** Umgesetzt in drei Schritten (Branch `phase3-slots`). **3a:** Content-Abschnitte `slotTypes/modules/shipClasses/shipModels` und `goods[].form`, Validierung im `WorldBuilder` (inkl. Leistungsbilanz und Spitzenlast gegen die Speicherbank), `ShipStats.Compute` als reine Funktion, fünf Einheitsmodelle (`unit_trader_100/60`, `unit_miner_100/60`, `unit_player`), `Capability` um `Jump`/`Stealth` erweitert. **3b:** `Ship.ModelKey/Slots/Fit/Stats`, `MaxHull`, Laderaum und Tempo je Schiff (`Travel.SpeedOf`), Fähigkeiten von Schiffen mit Modell folgen immer dem Fit (`RecomputeStats`), Save v4 mit Migration `MigrateV3ToV4` (Einheitsmodell nach Rolle und Laderaum; die Slot-Content-Sektionen liegen im Save wie Waren und Rezepte), `UniverseHash` mit `ModelKey` und `Fit`. **3c:** Laserstufe in `LaserShot.Roll` (Faktor 1 = unveränderte Zufallsfolge), Frachtformen `solid/liquid/any` (`Ship.FreeVolumeFor`, geteilter Universalraum), `GoodSave.Form`. 250 Tests; Balance-Lauf (20000 Ticks) und Hash byte-identisch zu vor der Phase, Selbsttest OK.

**Bekannte Grenzen:** Die Laserstufe ist im Spiel noch immer 1/1 (nur in Tests stärker); `ShipStats.MaxSpeed` begrenzt das Tempo auf 12; die Leistungsbilanz wird nur im Content geprüft, nicht bei Fit-Änderungen zur Laufzeit (kommt mit dem Ausrüster, Phase 5); Fähigkeiten gespeicherter Schiffe ohne Modell (Fighter) gelten weiter als gespeicherter Wert; `tanker_test` existiert nur in Tests. Die Migration wurde mit einem nachgebauten v3-Save getestet, nicht mit einem echten Altspielstand.

## Phase 4: Treibstoff

**Ziel:** Treibstoff ist eine echte Ware; Reparatur und Tanken kosten.
- **Voraussetzung:** Phase 3 (Tank- und Antriebsmodule, `form: liquid`, `ShipStats`). Entschieden: Slots vor Treibstoff, die Tankgröße kommt aus dem Tankmodul.
- **Waren und Stationen:** `propellant` (Flüssigstoff), `Treibstoffraffinerie` (3 Wasserstoff + 1 Energie → 4 Treibstoff), später `gas`, `ion_gas`, `fusion_fuel` mit Gasernte, Gasraffinerie und Anreicherungsanlage.
- **Schiff:** `Ship.Fuel` je Treibstoffware, Treibstofftank-Modul, Antrieb nennt zulässige Treibstoffe mit `efficiency`. `Travel.Start` rechnet echten Verbrauch statt der Credit-Pauschale (`Travel.cs`); Gate-Sprünge bleiben frei.
- **Dienste:** `refuel` und `repair` (verbraucht Hüllenplatten aus dem Stationslager), Rettungs- und Schleppdienst als Station mit Dienst für Liegenbleiber.
- **NPCs:** Tanken beim Reiseantritt an der besten erreichbaren Station; Routenbewertung (`TraderSystem`, `Travel.Estimate`) kennt Spritkosten.
- **Save v5:** `Fuel`, Tankgröße.
- **Risiken und Prüfung:** Wasserstoff konkurriert mit dem Kraftwerk; Balance-Lauf auf Energiemangel in der Startwelt prüfen. Liegenbleiber dürfen die Simulation nicht festfahren (Notkauf, Schlepp, Rückflug).
- **Tests:** Verbrauch, Tanken, Nachschub, leerer Tank, Rettungsdienst, Determinismus, Migration v4→v5.

**Stand:** Phase 4 ist in drei Schritten umgesetzt (Branch `phase4-treibstoff`).
- **4a (Datenmodell):** `propellant`, `baseFuel`/`fuelCapacity`, Antriebs-`fuel`-Liste mit `efficiency`, `Ship.Fuel`, Save v5 mit Migration v4→v5.
- **4b (Wirtschaft):** Stationstyp Treibstoffraffinerie (3 Wasserstoff + 1 Energie → 4 Treibstoff, dazu eine Grundproduktion ohne Eingänge: 1 Treibstoff je 20 Ticks) mit zwei Instanzen (Beta, Alpha). `Travel.Start` verbraucht echten Treibstoff (Strecke in Sektoren × `fuelUse` 0,002 / `efficiency`, Gate-Sprünge frei) statt der Credit-Pauschale. `Simulation/Refuel.cs`: Tanken an der aktuellen Station, Tankstopp über `GoToOrder`, Notkauf zum doppelten Basispreis als Geldsenke. `TraderSystem` zieht die Treibstoffkosten der Runde vom Gewinn ab.
- **4c (Dienste und Spieler):** Dienste `refuel` (`refuel:fill`, `refuel:25/50/100`, Marktpreis) und `repair` (`repair:full`, `repair:half`; verbraucht Hüllenplatten, `EconomyRules.RepairPlatesPerHull` 0,05, Arbeitsaufschlag 1,5 × Plattenpreis). Raffinerien führen `refuel`, Panzerwerk und Werft `repair`. Der Spieler verbraucht im Flug Treibstoff (`Flight/PlayerFuel`), bei leerem Tank gilt der Notantrieb (20 % Tempo), der Notruf im Menü liefert Treibstoff zum doppelten Basispreis bis zur nächsten Tankstelle. Dock-Bereich „Reparatur & Tanken“ (`RepairFuelScreen`), echte Tankanzeige im HUD und im Dock.
- **Prüfung:** 287 Tests, Selbsttest OK. Balance: Standard-Seed bei 20000 und 60000 Ticks „Preise im Zielbereich“; Seeds 1–8: 5 von 8 grün bei 20000, 5 von 8 bei 60000; Händler/Miner bleiben bei 14/8; 0 Notkäufe.

**Bei der Prüfung der Phasen 1–4 gefundene und behobene Fehler:**
1. **Alte Spielstände ohne Raffinerie:** Die Migration v4→v5 gab Schiffen Tanks, der Welt aber keine Treibstoffquelle; nach dem ersten Tank wäre die KI-Flotte liegengeblieben. Die Migration ergänzt jetzt Stationstyp und Raffinerien (in sehr alten Welten ohne Wasserstoff/Energie nur mit der Grundproduktion); zusätzlich hält ein Notkauf die Schiffe beweglich, wenn nirgends Treibstoff erreichbar ist. Mit einer Kopie des echten v2-Spielstands geprüft (2 Raffinerien, Flotte tankt, 0 Notkäufe über 40000 Ticks).
2. **Tanken fraß das Betriebskapital:** KI-Schiffe tankten für ihr gesamtes Geld voll und konnten danach keine Ware kaufen. Jetzt gilt ein Tankbudget (30 % der Credits über den Reisebedarf hinaus); ein Umweg zur Tankstelle erst unter 25 % Tank.
3. **Liegenbleiber wurden nie aufgegeben:** Der Leerlauf-Zähler startete bei jedem Planungsversuch neu. Ein Schiff ohne Geld und Treibstoff wird jetzt nach `ShipBankruptTicks` entfernt.
4. **Arme Abnehmer verhungerten:** Händler übersprangen Stationen, die nicht 25 % einer vollen Ladung anzahlen konnten. Knappe Eingangsware wurde so teuer, dass die Station nie mehr beliefert wurde (z. B. Farm ohne Wasser, Nahrung auf 4× Basispreis). Die Bewertung rechnet jetzt mit dem tatsächlich zahlbaren Erlös.
5. **Treibstoffpreis stieg über lange Läufe** (60000 Ticks, 5 von 8 Seeds bei 4×): behoben durch die Grundproduktion der Raffinerie.

**Abweichungen vom Plan und offene Punkte (nicht behoben, Entscheidung nötig):**
- **Treibstoff ist für den Spieler kaum spürbar:** Ein Flug über drei Sektoren kostet etwa 1 von 200 Einheiten. `fuelUse` 0,002 wurde auf die Stabilität der KI-Wirtschaft abgestimmt. Stellräder: eigener Verbrauch für das Spielermodell, kleinerer Tank oder höherer Verbrauch mit neuer Balance.
- **Reparatur kostet in der Praxis nichts:** Andocken repariert weiter automatisch und die Hülle regeneriert im Flug. Der Dienst `repair` funktioniert, wird aber erst gebraucht, wenn diese beiden Automatiken entfallen (sinnvoll mit dem Kampf).
- **Rettungs- und Schleppdienst als Station** fehlt; Ersatz sind Notkauf (KI) und Notruf (Spieler).
- **Tankgröße** kommt aus der Schiffsklasse (`baseFuel` 200), nicht aus einem Tankmodul, wie im Plan entschieden. Der Modul-Stat `fuelCapacity` existiert, kein Startmodell nutzt ihn.
- **Phase 3, Unterhalt:** `ShipStats.Upkeep` wird berechnet, aber `UpkeepSystem` rechnet weiter pauschal (`ShipUpkeepPerTick`). Ebenso ungenutzt: Masse und Schub; das Spielerschiff fliegt mit festem Tempo (`PlayerShip.MaxSpeed`), nicht mit `Stats.Speed`.
- **Phase 3, Leistung:** Die Leistungsbilanz wird nur beim Laden des Contents geprüft, nicht zur Laufzeit; die Laserstufe ist im Spiel überall 1/1.
- **Wirtschaft allgemein:** Über 60000 Ticks geraten auf einzelnen Seeds einzelne Waren in den Warnbereich (in den geprüften Läufen immer Überangebot um 0,3× Basispreis). Händler fahren teils dieselbe Ware zwischen zwei Stationen hin und her. Ein Verbot (Handel nur Erzeuger→Verbraucher) sättigt die ganze Wirtschaft und wurde verworfen.
- **Nicht geprüft:** Handtest im Spiel und Test auf einem Android-Gerät.

## Phase 5: Module als Waren, Fabriken, Ausrüster, Schiffskauf

**Ziel:** Der Spieler kann Schiffe und Module kaufen und umrüsten; NPCs nutzen dieselben Regeln. **Größe L, in drei Teilschritten umzusetzen (5a, 5b, 5c), je mit eigenem Test- und Balance-Lauf.**

**5a – Module als Waren, drei Fabriken, Bausatzstufen**
- **Modul-Waren (SCHIFFSSLOTS.md, „Module sind Waren“):** `WorldBuilder` legt zu jedem Modul automatisch eine Ware gleichen Schlüssels an, **angehängt hinter den JSON-Waren in fester Reihenfolge** (nach Modulschlüssel), damit `GoodId` (Index+1) und die Konstanten in `DemoUniverse` stabil bleiben. Eigenschaften: `form = solid`, `Volume` aus der Modulmasse, `BasePrice` = Summe der Zutatenpreise (`cost`) mal Verarbeitungsaufschlag (`EconomyRules.ModuleMarkup`), Farbe nach Slotart. **`cost` wird das Rezept** der Fabrik.
- **Drei Fabriken:** Antriebs- und Reaktorwerk, Waffen- und Schildwerk, Systemwerk als neue Stationstypen in der JSON, Stufe nach Stationsgröße (klein Mk1, mittel Mk2, Hub Mk3). Erster Modulsatz: Reaktor, Speicherbank, Triebwerk, Tank, Frachtmodule, Laser, Traktor, Schild, Panzerung. Verteilung in der Startwelt als offene Entscheidung (siehe unten).
- **Lager ohne Rezept:** Stationstypen bekommen `stocks` (Waren mit Zielbestand), die zusätzlich zum Rezept Markteinträge anlegen (heute entstehen Märkte nur aus Rezepten). Nötig für Ausrüster, Handelsstation und die Montage kleiner Schiffe. Händler beliefern sie über den normalen Handel.
- **Bausatzstufen:** `ship_kit` in `ship_kit_light/medium/heavy` (`ship_kit_capital` später mit der Hub-Werft); `EconomyRules.ShipKitGoodKey` wird je Klasse gewählt; `FleetSystem` und Werftrezepte angepasst.
- **Tests:** Jedes Modul hat genau eine Ware, Rezepte stimmen mit `cost`, `GoodId`-Konstanten unverändert, Content-Test „jedes Modell ist baubar“.

**Stand (5a):** umgesetzt auf Branch `phase5-module`.
- **Modul-Waren:** `WorldBuilder` hängt je Modul eine Ware gleichen Schlüssels hinter die JSON-Waren (nach Modulschlüssel, erste Id 26), Regeln in `Content/ModuleGoods.cs`: fest, Volumen = Masse (mind. 1), Basispreis = Σ Zutatenpreise × `ModuleMarkup` (1,3), Farbe nach Slotart, Name aus dem neuen Modulfeld `name`.
- **Erster Modulsatz Mk1:** vorhandene Module der Einheitsmodelle unverändert; neu Speicherbank, Treibstofftank (Frachtslot, `fuelCapacity` 100), Traktorstrahl, Schildgenerator, Panzerplattierung. Titan steckt in Tank und Panzerung, Platin im Schild, Brennstoffzellen in der Speicherbank.
- **Drei Fabriken:** Stationstypen mit `moduleFactory` (Slotarten, Höchststufe, Takt, Wunschbestände); die Rezepte entstehen automatisch aus `cost`, damit Rezept und Modul nie auseinanderlaufen. Antriebswerk Alpha, Systemwerk Beta, Waffen- und Schildwerk Gamma (Delta ist mit 8 Stationen voll).
- **`stocks`:** Lagerposten am Stationstyp (`[Anfang, Ziel]`), auch für Lebenszyklus-Neubauten. Händler beliefern solche Lager, kaufen dort aber nicht ein. Erster Nutzer: Die Werft führt Reaktor, Triebwerk, Laser und Frachtraum 100.
- **Bausatzstufen:** `ship_kit` heißt `ship_kit_light` (Id 22 bleibt), `ship_kit_medium`/`ship_kit_heavy` angehängt, Werft mit drei Rezepten; `kit` an der Schiffsklasse, Rückfall auf `ShipKitGoodKey`. Die KI-Flotte wächst weiter auf 14/8.
- **Save v6** mit Migration v5→v6 (Umbenennung, Anhängen, Fabriken und Werftstufen nachgereicht); mit dem echten v2-Spielstand geprüft.
- **Prüfung:** 306 Tests, Selbsttest OK (Modul-Ware im Werfthandel). Balance: Standard-Seed bei 20000 und 60000 Ticks „Preise im Zielbereich“ (13/8 bzw. 14/8 Schiffe, 0 Notkäufe); Seeds 1–8 bei 20000 alle grün, bei 60000 sieben von acht (Seed 3: Hüllenplatten 0,28×); vor 5a je fünf von acht.

**Abweichungen und offene Punkte (5a):**
- **Abnehmer der Module:** In 5a nur das Werftlager (je Lauf etwa 25–30 Module). Die Fabriken produzieren bis zum Überbestands-Stopp und stehen dann; Modulpreise liegen dort bei etwa 0,31× Basispreis. Echte Abnehmer kommen mit Ausrüster und Schiffskauf (5c).
- **Stationsobergrenze angehoben:** `maxStations` 30→33 und `maxStationsPerSector` 8→9 in der Startwelt, sonst verbrauchten die drei Fabriken den Spielraum des Lebenszyklus und Elektronik verknappte (Lebenserhaltung 3,5×, Konsumgüter 4×).
- **Drei Regeländerungen außerhalb der Fabriken:** (1) Der Markt bietet bei mehreren Rezepten mit derselben Zutat das höchste Gebot (vorher hätte ein Rezept auf Halde das Gebot der anderen gedrückt); (2) der Lebenszyklus betrachtet nur Waren, auf die ein Stationstyp antworten kann (sonst blockierten Module ohne Verbraucher jeden Verbraucherbau); (3) Händler kaufen nur noch so viel, wie der Abnehmer annimmt (vorher wurde der Rest einer vollen Ladung unbezahlt abgeladen; bei den kleinen Wunschbeständen der Fabriken drückte das Titan und Stahl auf 0,25×). Alle drei ändern das NPC-Verhalten (anderer Hash).
- **Stufe nach Stationsgröße** gibt es noch nicht (keine Stationsgrößen); `moduleFactory.maxTier` ist der Platz dafür.
- **Mittlere/schwere Bausätze** starten über dem Überbestands-Deckel und werden erst produziert, wenn eine Klasse sie verbraucht; noch keine Klasse nutzt sie.
- **Schild-, Traktor- und Tankwerte** sind reine Daten; `ShipStats` wertet `fuelCapacity` und `armor` aus, Schild und Traktor noch nicht.
- **Alte Spielstände ohne Warenschlüssel** bekommen Modul-Waren und Bausatzstufen, aber keine Fabriken (Zutaten nicht auflösbar).
- **Leistung:** Die Simulation ist durch die zusätzlichen Waren etwa 15 % langsamer.
- **Nicht geprüft:** Handtest der Oberfläche, Android.

**5b – Spielerkasse (Voraussetzung für Schiffskauf, mehrere Schiffe, Taxi)**
- Das Geld des Spielers wandert von `Ship.Credits` in ein kleines Objekt `PlayerWallet` an `Universe` (die Person); `TradeActions` buchen über `Universe.WalletOf(ship)` (Spielerschiffe: Wallet, NPC-Schiffe: `ship.Credits`, Verhalten bleibt gleich). **Zwischenstufe:** Das Akteurmodell kommt erst in Phase 9, dann wird `PlayerWallet` zur Kasse des Spielerakteurs (POLITIK.md, „Spielerkasse“).
- **Save:** Spielerkasse, Migration von `Ship.Credits` des Spielerschiffs; `UniverseHash` und `EconomyStats` zählen die Geldmenge nach Kassen.
- **Tests:** Summe aus Wallet und Schiffen ändert sich durch Handel nicht außer durch dieselben Flüsse wie vorher; Migration; Hash-Vergleich NPC-Wirtschaft unverändert.

**Stand (5b):** umgesetzt auf Branch `phase5-module`.
- **Kasse:** `Model/Wallet.cs` mit `ICreditAccount` (nur `Credits`) und `PlayerWallet` (Credits, Fraktion); `Ship` implementiert `ICreditAccount` über sein Feld `Credits`. `Universe.PlayerWallet` ist die Kasse der Person, `Universe.WalletOf(ship)` liefert sie für Spielerschiffe (`IsPlayer`) und das Schiff selbst für NPCs. Spielerschiffe behalten `Credits = 0`; die Startwelt legt `player.credits` in die Kasse.
- **Umgestellt auf `WalletOf`:** `TradeActions` (Kauf, Verkauf, Alles verkaufen, Höchstmenge), `Refuel.BuyFuel/TankUp/EmergencyBuy/PlayerEmergency` und die Budgetprüfung in `PrepareTrip`, Dienste `refuel` und `repair` (der Dienst `trade` bucht über `TradeActions`), alle Geldanzeigen der Oberfläche (Flug-HUD, Andock-Menü und -Bereiche, Hangar, Kommandozentrale, „Meine Fraktion“, „Besitz“ mit `SectorEconomy.PlayerNetWorth`), Selbsttest und UI-Bilder.
- **Bewusst bei `ship.Credits` geblieben** (nur KI-Schiffe): `TraderSystem`, `UpkeepSystem` (Unterhalt und Vermögenssteuer nur für `IsEconomyAi`, der Spieler bleibt wie bisher unbesteuert), `FleetSystem`, `StationLifecycleSystem`.
- **Save v7:** `SaveGame.PlayerWallet`, Migration v6→v7 übernimmt die Credits aller Spielerschiffe (Steuerer „Player“, ohne Steuerer-Feld Rolle „Player“) in die Kasse. `UniverseHash` mischt die Kasse ein; `EconomyStats` führt `PlayerCredits` und `TotalCredits`, das Balance-Tool eine Spalte `plCredits`. Mit dem echten v2-Spielstand geprüft (Kasse = alte Schiffscredits 1120, 20000 Ticks, 872 Handelsfahrten).
- **NPC-Wirtschaft unverändert:** gleicher Temporärtest (bitgenauer Hash über alles außer dem Spielergeld) nach 20000 Ticks auf dem Stand von 5a und von 5b, Standard-Seed und Seeds 1–3: identisch; Balance-CSV bis auf die neue Spalte byte-identisch; dauerhafter Test mit Kasse 500 gegen 9 999 999.
- **Prüfung:** 316 Tests (9 neu in `WalletTests`), Selbsttest OK. Balance unverändert gegenüber 5a: Standard-Seed 20000 und 60000 Ticks „Preise im Zielbereich“ (13/8 bzw. 14/8, 0 Notkäufe), Seeds 1–4 bei 20000 grün.

**Abweichungen und offene Punkte (5b):**
- **Abstraktion als Schnittstelle** statt reiner Lese-/Buchungsfunktionen: `ICreditAccount` mit `Credits`; Buchungen bleiben `kasse.Credits -= x`, damit die Gleitkomma-Arithmetik der NPCs exakt gleich bleibt. Phase 9 lässt `WalletOf` die Kasse des Besitzers liefern.
- **Fraktionsvermögen** (`FactionsScreen`) zählt Stationen und Schiffscredits, also nicht mehr das Geld des Spielers; die Person ist keine Fraktionskasse.
- **„Besitz“-Gesamtwert** = Spielerkasse + Fracht aller Spielerschiffe; der Schiffswert (`TotalValue`) eines Spielerschiffs ist nur noch seine Fracht.
- **Nicht geprüft:** Handtest der Oberfläche, Android.

**5c – Dienste und Schiffskauf**
- **Dienste:** `outfitter` (Module ein- und ausbauen, Waren aus dem Stationslager; ausgebaute Module wandern ins Lager oder in die Fracht), `shipdealer` (`BuyShip`, Bausatz und Zusatzmaterial aus dem Lager, Werft bekommt das Geld), Handelsstation als neuer Typ. Kleine Schiffe auch an Handelsstation, Ausrüster und Raffinerie (Montage aus `ship_kit_light`, siehe SCHIFFSSLOTS.md).
- **Modelle der Skiff-Klasse** (*Pip*, *Nomad*, *Prospector*) kommen hier in die JSON, weil die Startpakete sie brauchen; Phase 3 liefert nur die Einheitsmodelle.
- **Startpakete** (`player.startOptions`): Schürfer (*Prospector*), Händler (*Nomad*), Generalist (*Pip* mit Werkzeug-Slot; *Tinker* kommt erst in Phase 6).
- **Save v6:** Spieler-Fit bleibt im Schiff; Module als Waren sind Content.
- **Tests:** Werft verbraucht Bausatz und Zusatzmaterial, Spielerkauf, Umrüstung ändert `ShipStats`, ungültiger Einbau wird abgelehnt.
- **Balance (alle Teilschritte):** Neue Nachfrage nach Titan und Platin; `tools/SpaceSim.Balance` auf Preisentgleisung prüfen.

**Stand (5c):** umgesetzt auf Branch `phase5-module`.
- **Gemeinsame Fit-Regel:** `Model/FitRules.Check` (Fit-Länge, Modul bekannt, Slotart, Größe, Pflichtslots, Leistungsbilanz und Spitzenlast) ist eine reine Funktion; der `WorldBuilder` prüft die Modelle damit (gleiche Meldungen), der Ausrüster jeden Umbau zur Laufzeit.
- **Dienst `outfitter`:** Angebote `fit:<Slot>:<Modul>` (aus der Fracht kostenlos, sonst aus dem Stationslager zum Marktpreis) und `remove:<Slot>` (nur Nicht-Pflichtslots; Pflichtslots nur per Tausch). Ausgebaute Module kauft die Station zum Marktpreis an, soweit sie annimmt und zahlen kann, sonst gehen sie in die Fracht. Ungültiges (Slotart, Größe, Leistung, Spitzenlast, Pflichtslot, Fracht passt nicht mehr, Lager, Credits, falscher Schlüssel) wird ohne Zustandsänderung und ohne Event abgelehnt. `OutfitterService.Preview` liefert Vorher/Nachher-Werte für die Oberfläche. Ohne Mining-Modul feuert der Laser nicht (`MineResult.NoLaser`).
- **Schiffskauf:** gemeinsame Regel `Simulation/ShipPurchase` (Bausatz der Klasse + je Modul des Fits eine Modul-Ware aus dem Stationslager, Preis = Marktpreis × (1 + Montagegebühr), Geld an die Station). Dienst `shipdealer` (`buy:<Modell>`), Stationstyp-Feld `shipDealer { classes, maxSize, assembly }`: Werft alle Klassen (5 %), Handelsstation nur Skiffs (10 %). Das gekaufte Schiff gehört dem Spieler und wird sofort aktiv; das alte bleibt als Spielerbesitz an der Station geparkt und lässt sich im Hangar dieser Station wieder übernehmen (`Simulation/PlayerFleet`, Event `ActiveShipChanged`).
- **KI:** `FleetSystem.Expand` nutzt dieselbe Regel, wenn eine Werft Bausatz und alle Module vorrätig hat, sonst wie vor 5c nur den Bausatz zur Pauschale (Regel `FleetBuysModules`, Event `FleetShipBuilt`). `ProductionSystem` lässt bei mehreren fälligen Rezepten das knappste Produkt zuerst laufen.
- **Inhalte:** Handelsstation (`trade_station`, nur `stocks`, Dienste trade/outfitter/shipdealer) als „Handelsstation Beta“ im Knotensektor Beta; Werft mit Ausrüster, Schiffshändler und weiteren Modul-Lagerposten; Skiff-Klasse mit *Pip*, *Nomad*, *Prospector* aus den Mk1-Modulen; Startpakete `player.startOptions` (Schürfer/Prospector 300 Cr, Händler/Nomad 1500 Cr, Generalist/Pip 800 Cr) mit `WorldBuilder.BuildDefault(startOption: …)`, ohne Wahl wie bisher.
- **Oberfläche:** Dock-Bereiche „Ausrüster“ (Slots, Leistungsbilanz, Angebote je Slot mit Preis, Herkunft und Vorher/Nachher) und „Werft“ (Modelle mit Preis, Werten, Material vorhanden/fehlt) zeichnen nur aus `Offers` und führen nur über `Execute` aus; Hangar und Andock-Menü zeigen eigene geparkte Schiffe („Übernehmen“); „Neues Spiel“ mit Auswahl der Startpakete. `--ui-shots` um beide Bereiche und die Startpakete erweitert, Selbsttest tauscht an der Werft ein Modul, kauft ein Schiff und wechselt zurück.
- **Save v8:** `StationTypeSave.ShipDealer`; Migration v7→v8 ergänzt Skiff-Klasse/-Modelle, Dienste, Schiffshändler, Lagerposten und die Handelsstation. Mehrere Spielerschiffe brauchen kein neues Feld (Besitz = Steuerer „Player“, aktiv = `PlayerShip`, jetzt im Hash). Mit dem echten v2-Spielstand geprüft (20000 Ticks, 872 Handelsfahrten, Flotte 14 → 14).
- **Prüfung:** 330 Tests (14 neu in `OutfitterShipyardTests`), Selbsttest OK. Balance (frischer Release-Build): Standard-Seed 20000 und 60000 Ticks „Preise im Zielbereich“, je 14/8 Schiffe, 0 Notkäufe; Seeds 1–4 bei 20000 alle grün, je 14/8, 0 Notkäufe. Titan und Platin bleiben im Band (Titan ≈0,3–0,5×, Platin ≈0,7–0,9×, Platinerz ≈0,8×).

**Abweichungen und offene Punkte (5c):**
- **KI kauft nur bevorzugt mit Modulen:** Mit der strikten Regel (Bausatz + Module) blieb die KI-Flotte auf Seed 2 bei 12 Händlern stehen, weil das Antriebswerk keine Triebwerke liefern konnte. Deshalb Rückfall auf den Bausatz, wenn eine Werft die Module nicht vorrätig hat. KI-Neubauten mit Modulen: 2–4 von 7–10 je 20000 Ticks, 18 von 28 bei 60000.
- **Wer nimmt Module ab:** KI-Neubauten an der Werft (Reaktor, Triebwerk, Frachtraum, Laser), der Spieler über Ausrüster und Schiffskauf, und das Nachfüllen der Lagerposten von Werft und Handelsstation durch Händler (≈70–90 Modul-Einheiten je 20000 Ticks statt 28 in 5a).
- **Modulfabriken unterkapitalisiert (bekannte Schwäche):** Sie stecken ihr Startgeld in Module ohne Abnehmer (Speicherbank, Schild, Panzerung, Frachtmodule auf Halde) und können danach Elektronik zum Knappheitspreis nicht mehr anzahlen; Reaktor und Triebwerk bleiben bei 20000 Ticks knapp (Reaktor 2–2,9×, unter der Warnschwelle 3,0; bei 60000 Ticks ≈0,6×). Verworfen: Händler liefern nur, was der Abnehmer bezahlen kann (blockierte arme Werften, Flotten fielen auf 9–11 Händler), kleinere Fabrik-Wunschbestände (Module ohne Abnehmer fallen unter 0,3×), Fabrik-Einnahmen 2 (kaum Wirkung).
- **Skiff-Werte weichen von SCHIFFE.md ab** (Pip 60/60, Nomad 100/60 statt 30/60 und 50/80): Die Klasse hat zwei Frachtslots, die Modelle nutzen nur vorhandene Mk1-Module (kein neues Modul, damit die Modul-GoodIds stabil bleiben). Pip ist nicht schneller als Nomad (8,0 vs. 8,4).
- **Raffinerie verkauft keine Schiffe:** Ihre Klassen (Bergbauer leicht, kleiner Tanker) gibt es noch nicht. Einen eigenen Stationstyp „Ausrüster“ gibt es nicht; der Dienst sitzt an Werft und Handelsstation.
- **Handelsstation führt nur Schiffsware** (leichter Bausatz, Skiff-Module), keine Handelsgüter — Lagerposten ohne Rezept wären sonst eine einmalige Senke für diese Waren. Skiffs sind dort teuer (≈4000–5000 Cr), solange ihre Modul-Lager knapp sind.
- **Schiffwechsel nur an der Station, an der das Schiff liegt;** Fernwechsel, Liegegebühr, Verkaufen und Befehle für geparkte Schiffe bleiben Phase 12. Das Startpaket wird nicht gespeichert (wie geplant).
- **Nicht geprüft:** Handtest der Oberfläche, Android.

## Phase 6: Multirole

**Ziel:** Schiffe wählen ihre Aufgabe nach Fähigkeit und Gewinn.
- `JobAssignmentSystem` wählt für untätige Schiffe die beste Aufgabe unter den Fähigkeiten (vorhandenes Scoring), mit Mindestdauer und Hysterese.
- `FleetSystem` zählt Kapazität statt Schiffe; Herdenschutz (Crowd-Penalty) für alle Aufgaben.
- Erstes Multirole-Modell (*Tinker*) und der Rest der Klassen aus SCHIFFE.md (ohne Kampfklassen); *Prospector* gibt es seit Phase 5.
- **Tests:** Aufgabenwechsel flackert nicht; Generalist ist schlechter als Spezialist; Balance-Lauf auf Verdrängung.

## Phase 7: Güteklassen und Upgrades

- **Güteklassen** beim Schiffskauf (Standard 70 %, Verbessert 20 %, Elite 8 %, Meisterwerk 2 %), Mehrkosten kommen aus dem Werftlager; Startwelt-Schiffe würfeln ohne Material.
- **Upgrades:** Dienst `upgrade` mit Stufenchancen und Mitleidszähler; `UpgradeSystem` für reiche NPC-Schiffe; Slot-Upgrade selten und je Klasse begrenzt.
- **Save v7:** Bonusslots in der Slotliste, Mitleidszähler.
- **Tests:** Wahrscheinlichkeiten über viele Würfe, Determinismus, Material wird verbraucht.

## Phase 8: Bau, Brecher, Lagerhaus

- **Bauschiff** (Fähigkeit `Construction`) und `BuildSite`; `ConstructionSystem`; Spieler liefert per freiem Handel.
- **Notbau** (zustandsabhängig, nur fehlende Materialien, Kritikalität, Bremse); dafür die **kleine Aufspaltung von `Essential`**: `Critical` (abgeleitet) und `BankruptcyProtected` (STATIONEN.md). Der Rest der Aufschlüsselung (`Government`, `Capturable`, `Destructible`, `Immortal`) bleibt in Phase 10.
- **Zyklenfreie Baukosten** als Content-Test; Startbestand an Bauschiffen je Fraktion.
- **Asteroidenbrecher:** Spaltlaser, Fragmente mit Limit je Gürtel, Event `AsteroidSplit`.
- **Lagerhaus** als Station mit Dienst für Baustellenlager.
- **Save v8:** Baustellenliste, Fragment-Ids.
- **Balance:** Dauerhafte Nachfrage nach Stahl und Hüllenplatten; Anteil Notbau messen.

## Phase 9: Akteure und Fraktionen

- **Akteur/Fraktion:** Akteur als Wirtschaftssubjekt, Fraktion als Akteur mit Politik; `Ship.Owner` statt `Ship.Faction`; Mitgliedschaft; Migration der bestehenden Fraktionen.
- **Größe und Größenklassen**, Zukauf mit Erst- und Zweitzugriff (gewichtete Ziehung), Einzelschiffe träge anlegen, Sammelgruppe „Freie“.
- **Kasse** (`Treasury`), sparsame Beziehungen mit Vorgabewerten, Persönlichkeitswerte (Aggression, Gier, Kooperation, Gesetzestreue).
- **Spieler** als Einzelakteur.
- **Save v9:** Akteure, Mitgliedschaft, Kasse, Persönlichkeit.
- **Prüfung:** Geldmenge bleibt erklärbar (`EconomyStats`), keine Beziehungs-Explosion.

## Phase 10: Gate-Zugang, Kontrollpunkte, Sektorkontrolle

- **Gate-Zustände** (offen, eingeschränkt, geschlossen, zerstört) und Politik je Fraktion; `FindRoute` mit Mobilitätsprofil (Cache nach Profil).
- **Sperrstufen 0–5**, Richtung, Mindestdauer und Hysterese, Hauptroute je Region.
- **Kontrollpunkte** (`IAccessCheck`): Politik, Ware, Entdeckung.
- **Sektorkontrolle:** Einflussberechnung, abgeleitetes `Sector.Owner`, Stufen Präsenz bis Kontrolle, „Umkämpft“.
- **Regierungsstationen:** Hauptquartier (Hub), Firmensitz, Sektor-HQ, Büro, Polizeiwache; `Government`, `BankruptcyProtected`, `Critical` etc. (Aufschlüsselung von `Essential`).
- **Save v10:** Gate-Zustand und Politik, Einfluss.
- **Prüfung:** Balance meldet zerfallende Karte oder isolierte Sektoren.

## Phase 11: Polizei, Fahndung, Piraten

- **Lagekarte** (Bedrohung, Sicherheit) zuerst nur als Beobachtung (Statistik, Anzeige).
- **`SecuritySystem`:** Zuweisung von Polizeischiffen nach Bedrohung und Wert; Heimatbasis; Unterhalt aus der Kasse.
- **Fahndungsniveau** je Hoheitsgebiet mit Verfall; Verbrechen und Folgen; Kopfgeld; Kopfgeldbüro und Gericht.
- **Piraten** als Gegenfraktion; Überfälle auf unbewachte Routen.
- **`IEncounterResolver`:** abstrakte Auflösung nach Stärke, später durch echten Kampf ersetzbar.
- **Save v11:** Fahndung, Lagekarte, Piratenzustand.
- **Prüfung:** Verlustquote, Polizeianteil am Haushalt, Rechenzeit der Scans.

## Phase 12: Verbände, Befehle, Spielerflotte

- **`Squadron`** (Anführer, höchstens 8 Mitglieder, Formation nur Darstellung), Heimatbasis, Befehlskapazität nach Größe.
- **`IOrderExecutor`** pro Befehlstyp: `GoTo`, `Hold`, `Patrol`, `Guard`, `Escort`, `Follow`, `Trade`, `Mine`, `Build`, `Return` und weitere; Ebenen Notfall > Befehl > Automatik; Verhaltensregeln.
- **Spielerflotte:** Flottenliste und Befehlsmenü (höchstens drei Schritte), Automatikaufträge mit Grenzen.
- **`OrderSave`** erweitern (heute drei Ganzzahlen).
- **Hangar, Taxi, Leihschiff:** Dienste `hangar`, `taxi`, `rental`; Schiffe haben einen Standort (angedockt an einer Station, `Ship.DockedAt`); das Taxi bewegt nur den Spieler, das Schiff bleibt.
- **Oberfläche:** Flug-HUD, Andock-Menü, Dock-Bereiche und Kommandozentrale sind **bereits gebaut** (UI.md); hier bleibt nur die Anbindung der Platzhalter an echte Daten (Hangar, Taxi, Flotte, Schiffwechsel, Befehle).
- **Save v12:** Verbände, Befehle, Heimatbasis.

## Phase 13: Forschung

- `research`-Sektion, `ResearchSystem`, Freischaltungen über `IsUnlocked`, Besitzer ist der Akteur mit Forschungsstation (Budget vom HQ). Details in [FORSCHUNG.md](FORSCHUNG.md).

## Zurückgestellt

| Thema | Grund |
|---|---|
| Kampfsystem (echt) | groß; der abstrakte Auflöser in Phase 11 genügt zunächst |
| Eroberung (`allowStationCapture`) | abgeschaltet, erst nach Kampf |
| Sprungantrieb, Tarnfeld | Referenz und Planung; brauchen Gate-Zugang und Fahndung |
| Besatzung und Lebenserhaltung für Schiffe | sprengt den Umfang |
| Blockadebrecher, Abwracker, Fregatten, Schlachtschiffe | hängen an Kampf, Politik und Wracks |
| Android-Test auf einem Gerät | unabhängig; Exporttemplates sind installiert, die Debug-APK baut (`build/space-adventure.apk`); offen ist nur der Test auf einem Gerät (Touch, Leistung) |

## Reihenfolge und Abhängigkeiten

```text
1 Dienste ───────────────────────────┐
2 Fähigkeiten ──► 3 Slots/Module ──► 4 Treibstoff ──► 5 Module, Fabriken, Kasse, Schiffskauf ──► 6 Multirole ──► 7 Güte/Upgrades
                                                                                                        │
                                                                                                        ▼
                              8 Bau/Brecher ──► 9 Akteure ──► 10 Gate/Sektor ──► 11 Polizei ──► 12 Verbände ──► 13 Forschung
```

- Die Phasen 1 bis 7 sind die **Schiffs- und Handelskette** und sollten ohne Unterbrechung nacheinander laufen. Danach ist das Spiel für den Spieler deutlich tiefer (Schiffskauf, Umrüstung, Treibstoff).
- Phase 8 kann vor Phase 9 laufen, weil Bauschiffe nur Fähigkeiten und Modelle brauchen.
- Ab Phase 9 greift die Politik. Phase 10 vor 11, weil Polizei die Kontrollpunkte braucht; Phase 12 nutzt sie für Patrouille und Eskorte.

## Offene Entscheidungen, die vor dem Start einer Phase geklärt sein müssen

- **Phase 3:** Genaue Slotzahl je Klasse und die Zahlen der Einheitsmodelle (damit der Hash-Vergleich stimmt).
- **Phase 4:** Wie viel Treibstoff ein Gate-freier Sektorflug typischerweise kostet; Tankgröße der Einheitsmodelle.
- **Phase 5:** Preise und Rezepte der ersten Modulreihe, Verteilung der drei Fabriken in der Startwelt, Verarbeitungsaufschlag der Modul-Waren (`ModuleMarkup`), Form von `stocks` am Stationstyp (ROADMAP.md, Punkte 20 und 21).
- **Phase 8:** Umfang der `Essential`-Aufspaltung (Vorschlag: nur `Critical` und `BankruptcyProtected`, ROADMAP.md, Punkt 23).
- **Phase 9:** Ob Einzelschiffe sofort oder träge Akteure werden (Vorschlag: träge); Beitritt des Spielers zu Fraktionen.
- **Phase 10:** Sperren je Sektor oder je Gate (Vorschlag: Sektor mit Gate-Ausnahmen).
- **Jede Phase:** Nach dem Lauf des Balance-Tools entscheiden, ob Startwerte angepasst werden.
