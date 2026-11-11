# Umsetzungsplan

Gesamtplan für alles, was in den Planungsdokumenten steht. Reihenfolge nach Abhängigkeit und nach dem Grundsatz **Tiefe vor Breite**: Zuerst wird die Schiffs- und Handelskette vollständig, dann folgen Welt, Politik und Flotten. Der Plan ist ein Vorschlag, nichts davon ist begonnen. Größen: **S** (kleiner Schritt), **M** (mehrere Dateien, eigene Tests), **L** (neues Teilsystem mit Save-Änderung und Balance-Arbeit).

Verweise: [UI.md](UI.md), [SCHIFFE.md](SCHIFFE.md), [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md), [STATIONEN.md](STATIONEN.md), [POLITIK.md](POLITIK.md), [FLOTTEN.md](FLOTTEN.md), [FORSCHUNG.md](FORSCHUNG.md), [ROADMAP.md](ROADMAP.md).

## 0. Arbeitsregeln für jede Phase

1. **Architektur zuerst:** Neue Funktion als `ISimSystem` in einem Modul (`DefaultModules()`), Kopplung über `Universe.Events`, Inhalte in `default-world.json`, `SpaceSim.Core` ohne Godot und Dateizugriff.
2. **Test zuerst bei Umbauten:** Vor einem Umbau müssen die vorhandenen Tests grün sein; der Umbau darf kein Verhalten ändern, bis die Phase es ausdrücklich will (`UniverseHash`-Tests belegen es).
3. **Persistenz:** Neue Zustände in `SaveGame`-DTOs, `UniverseSerializer` und `UniverseHash`; bei Formatbruch `SaveGame.CurrentVersion` erhöhen und eine Migration schreiben (Stand heute: v16).
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
| 6 | Multirole: Job-Zuteilung, Kapazitätsgrenzen, erste Multirole-Modelle | M | v9 |
| 7 | NPC-Güteklassen und Upgrades | M | v10 |
| 8 | Bauschiff, Baustellen, Notbau, Asteroidenbrecher, Lagerhaus | L | v11 |
| 9 | Akteure, Fraktionen, Kasse, Beziehungen, Persönlichkeit, Zukauf | L | v12 |
| 10 | Gate-Zugang, Kontrollpunkte, Sektorkontrolle, Regierungsstationen | L | v13 |
| 11 | Polizei, Fahndung, Piraten, abstrakte Begegnungen | L | v14 |
| 12 | Verbände, Befehle, Spielerflotte und Oberfläche | L | v15 |
| 13 | Forschung | M | v16 |
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

**Stand:** umgesetzt auf Branch `phase6-multirole`.
- **Aufgaben als Bausteine:** `IShipJob` mit `TradeJob` und `MineJob` (`Simulation/ShipJobs.cs`): Etikett (`ShipRole` bleibt gespeichert), nötige Fähigkeit, Kapazitätsgrenze, Kapazität je Schiff, Kaufwert und Bewertung über das vorhandene Scoring (`TraderSystem.BestTrade`, `MinerSystem.BestMine`, aus `PickOrder` herausgelöst, Verhalten gleich). Modelle nennen ihre KI-Aufgaben in `shipModels[].jobs` (geprüft gegen die Fähigkeiten des Fits); Einheitsmodelle sind Spezialisten, Spielermodelle haben keine.
- **`JobAssignmentSystem`** (neues Modul „Jobs“ vor Schürfen und Handel): untätige Multirole-Schiffe wechseln die Aufgabe erst nach `JobMinTicks` (600), nur bei mindestens 25 % besserer Bewertung (`JobSwitchMargin`), mit Herdenschutz auf Aufgabenebene (Bewertung ÷ (1 + 0,5 × Auslastung)) und nicht in eine volle Aufgabe, solange die aktuelle noch etwas einbringt. Event `JobChanged`.
- **`FleetSystem` zählt Kapazität:** Handel = Frachtraum ÷ 100, Schürfen = Laserstufe; Einheitsmodelle zählen je 1. Die KI wählt das Modell (`ChooseModel`): Kandidaten mit passender Aufgabe, Werft mit Bausatz und allen Modulen, bezahlbar, Grenze höchstens um 0,5 überschritten; gewählt wird der beste Wert je Credit (Wert der Aufgabe + 0,75 × Wert der Zweitaufgaben). Ohne Kandidat gilt die Regel vor Phase 6 (Kopie des Elternschiffs).
- **Inhalte:** Klassen Kurier (*Swift*), leichter Frachter (*Mule*, *Hauler*, **Tinker** als erstes Multirole-Modell), schwerer Frachter (*Behemoth*), Bergbauer leicht (*Pickaxe*) und schwer (*Rockbreaker*, Laser Mk2), Tanker (*Cistern*); Bausätze light/medium/heavy je Klasse. Neue Module als Waren mit Fabrikrezept: Triebwerk M (Antriebswerk), Mining-Laser Mk2 (Systemwerk, `maxTier` 2). Neuer Klassenwert `toolEfficiency` (Mehrzweckrumpf 0,85: schwächerer Laser).
- **Save v9:** `JobSince` je Schiff, `jobs` an Modellen, `toolEfficiency` an Klassen, `Role`/`JobSince` im Hash; Migration v8→v9 ergänzt Module, Modul-Waren, Klassen, Modelle, Aufgabenlisten, Fabrikrezepte und Werftlager. Mit dem echten v2-Spielstand geprüft (20000 Ticks, 872 Handelsfahrten, Flotte 14 → 14).
- **Prüfung:** 345 Tests (15 neu in `MultiroleTests`), Selbsttest OK (Werft bietet Tinker, Behemoth, Rockbreaker). Balance (frischer Release-Build): Standard-Seed 20000 und 60000 Ticks „Preise im Zielbereich“, je 14/8, 0 Notkäufe; Seeds 1–4 bei 20000 alle grün (14/8, 14/8, 12/8, 14/8), 0 Notkäufe.

**Abweichungen und offene Punkte (Phase 6):**
- **Multirole wird in den Balance-Läufen selten gekauft** (0–1 Tinker je Lauf, 0 Aufgabenwechsel): Schürflücken füllt meist die Bausatz-Kopie, weil die Werft selten einen Laser vorrätig hat; Tinker werden eher als Händler gekauft. Im ersten Entwurf ohne `toolEfficiency` kaufte die KI 1–4 Tinker, die 1–23-mal wechselten. Der Mechanismus ist durch Tests abgedeckt (Wechsel, Flackerschutz, Hysterese).
- **Generalist schwächer, beim Schürfen moderat:** Laser 0,85 und langsamer Rumpf ergeben im kontrollierten Szenario −18 % Erz je Arbeitstick (allein fliegender Schürfer; gemessen wird nur die Arbeitszeit, weil ein einzelnes Schiff sonst an der Nachfrage hängt), im Handel −54 %. Stärkere Strafen machen den Tinker für die Kaufwahl wertlos.
- **Nicht umgesetzt:** Bauschiff (*Foreman*) und Asteroidenbrecher (*Quarry*) — ihre Module und Mechanik gehören zu Phase 8; Zusatzmaterial je Modell (SCHIFFE.md) — Klassen unterscheiden sich über Bausatzstufe und Module; *Swift* und *Cistern* sind nur für den Spieler (ein reiner Flüssigtanker würde als Handelskapazität gezählt, kann aber nur drei Waren fahren). Der Tinker hat keinen Geschützslot belegt (es gibt noch keine Waffen).
- **Verworfen:** Frachtraum M 200 und Kanisterraum M als neue Module — ohne KI-Abnehmer fielen sie unter 0,3× Basispreis (Warnung); Panzerung im Werftlager aus demselben Grund (Rockbreaker hat dafür Rumpfhülle 200).
- **Modulfabriken weiter unterkapitalisiert:** Reaktor Ø letzte 25 % (Tabelle) Standard-Seed 2,10 → 2,90, Seeds 1–4 2,65/2,65/2,90/2,65 → 2,90/1,65/2,25/2,90, 60000 Ticks 0,70 → 0,35; Triebwerk S ähnlich gemischt (2,60 → 2,70 Standard). Gleiches Band wie in 5c, keine strukturelle Besserung.
- **Nicht geprüft:** Handtest der Oberfläche, Android.

## Phase 7: Güteklassen und Upgrades

- **Güteklassen** beim Schiffskauf (Standard 70 %, Verbessert 20 %, Elite 8 %, Meisterwerk 2 %), Mehrkosten kommen aus dem Werftlager; Startwelt-Schiffe würfeln ohne Material.
- **Upgrades:** Dienst `upgrade` mit Stufenchancen und Mitleidszähler; `UpgradeSystem` für reiche NPC-Schiffe; Slot-Upgrade selten und je Klasse begrenzt.
- **Save v7:** Bonusslots in der Slotliste, Mitleidszähler.
- **Tests:** Wahrscheinlichkeiten über viele Würfe, Determinismus, Material wird verbraucht.

**Stand:** umgesetzt auf Branch `phase6-multirole` (nach Phase 6).
- **Güteklassen** (`Simulation/ShipQualityRoll`): ein Wurf über `Universe.Rng` (Standard 70 %, Verbessert 20 %, Elite 8 %, Meisterwerk 2 %, Regel `qualityChances`). Verbessert = ein Modul eine Stufe besser (`modules[].upgrade`), Elite = zwei bis drei Module besser, sonst ein Slot eine Größe größer, Meisterwerk = zusätzlich ein Bonusslot (Kopie des Frachtslots, höchstens `maxSlotUpgrades` der Klasse). Bessere Module, Bonusmodul und Slot-Material kommen zusätzlich aus dem Werftlager (die ersetzten Grundmodule nicht); fehlt etwas, fällt der Wurf stufenweise zurück. Gilt für Spielerkauf, KI-Flotte (die Kopie übernimmt jetzt das Modell, nicht die Ausstattung des Elternschiffs) und Gründerschiffe (ohne Material, abschaltbar mit `founderQuality`).
- **Upgrades** (`Simulation/ShipUpgrade`, gemeinsame Regel): Modul eine Stufe besser (Chance nach Stufe 90/60/30 %), Slot eine Größe größer oder Bonusslot (15 %, je Klasse begrenzt). Jeder Versuch verbraucht Material aus dem Stationslager und Credits (Material × 1,5, an die Station), auch bei Misserfolg; jeder Fehlschlag am selben Slot +10 Prozentpunkte (Mitleidszähler), Erfolg setzt ihn zurück. Dienst `upgrade` an der Werft (strikte Schlüssel `module:`/`slot:`/`bonus:`), `UpgradeSystem` für reiche, untätige NPC-Schiffe an einer Station mit dem Dienst (nur Modul-Upgrades, bestes erwartetes Plus je Credit für ihre Aufgabe).
- **Inhalte:** Reaktor Mk2 und Triebwerk S Mk2 als neue Module/Waren (Antriebswerk `maxTier` 2), Verbesserungspfade Reaktor, Triebwerk S, Mining-Laser, Frachtraum 60 → 70.
- **Oberfläche:** Abschnitt „Verbessern“ im Ausrüster (nur aus `Offers`/`Execute`: Chance, Material, Preis, Mitleidszähler, Grund); Güteklasse in Werft (Chancen), Hangar, Besitz und Schiffsdetail; Selbsttest-Schritt (Upgrade über die Oberfläche) und `--ui-shots` `11g-dock-ausruester-upgrade.png`.
- **Save v10:** Güteklasse, Mitleidszähler je Schiff (Bonusslots stehen in der Slotliste), Verbesserungen der Module, `maxSlotUpgrades`; Güte, Slotliste und Mitleid im Hash; Migration v9→v10 (Content-Nachtrag, Dienst `upgrade` an der Werft). Mit dem echten v2-Spielstand geprüft (20000 Ticks, 872 Handelsfahrten, Flotte 14 → 14).
- **Prüfung:** 357 Tests (12 neu in `QualityUpgradeTests`), Selbsttest OK. Balance (frischer Release-Build): Standard-Seed 20000 und 60000 Ticks „Preise im Zielbereich“ (14/8 bzw. 12/8 Schiffe bei voller Handelskapazität, 0 Notkäufe); Seeds 1–4 bei 20000 alle grün, 0 Notkäufe. Neubauten erreichen Standard/Verbessert/Elite/Meisterwerk 6/3/0/0 (Standard-Seed, gewürfelt 5/3/1/0).

**Abweichungen und offene Punkte (Phase 7):**
- **Save-Version v10 statt v7** (Platzhalter im Plan; v9 belegte Phase 6).
- **Kein eigener Dock-Bereich „Upgrade“:** Das Andock-Menü hat keinen Platz für eine neunte Kachel; die Upgrades sind ein Abschnitt im Ausrüster (wie im Auftrag als Möglichkeit genannt).
- **NPC-Upgrades sind selten** (0–3 Versuche je 20000 Ticks): Nur reiche Händler, die gerade an der Werft ausliefern, kommen in Frage. Güteklassen der KI-Neubauten fallen oft zurück, weil der Werft Mk2-Module fehlen.
- **Spieler würfelt beim Schiffskauf mit** (gleiche Regel wie die NPCs, SCHIFFSSLOTS.md beschreibt nur NPCs); das Startschiff würfelt nicht.
- **Keine Mk3-Module:** Die Stufenchancen 60 %/30 % greifen nur in den Regeltests. Bonusslots aus dem Dienst sind leer, bis der Spieler ein Modul einbaut; NPCs bauen nie Slots um.
- **Modulfabriken:** Reaktor/Triebwerk bleiben im Band 1,5–2,9× (Standard-Seed unverändert 2,86 bzw. 2,67), Reaktor Mk2 ≈2,5× bei kleinen Lagern.
- **Nicht geprüft:** Handtest der Oberfläche, Android.

## Phase 8: Bau, Brecher, Lagerhaus

- **Bauschiff** (Fähigkeit `Construction`) und `BuildSite`; `ConstructionSystem`; Spieler liefert per freiem Handel.
- **Notbau** (zustandsabhängig, nur fehlende Materialien, Kritikalität, Bremse); dafür die **kleine Aufspaltung von `Essential`**: `Critical` (abgeleitet) und `BankruptcyProtected` (STATIONEN.md). Der Rest der Aufschlüsselung (`Government`, `Capturable`, `Destructible`, `Immortal`) bleibt in Phase 10.
- **Zyklenfreie Baukosten** als Content-Test; Startbestand an Bauschiffen je Fraktion.
- **Asteroidenbrecher:** Spaltlaser, Fragmente mit Limit je Gürtel, Event `AsteroidSplit`.
- **Lagerhaus** als Station mit Dienst für Baustellenlager.
- **Save v8:** Baustellenliste, Fragment-Ids.
- **Balance:** Dauerhafte Nachfrage nach Stahl und Hüllenplatten; Anteil Notbau messen.

**Stand:** umgesetzt auf Branch `phase8-bau`.
- **Baustellen:** Eine Baustelle ist eine Station vom Typ `build_site` mit Datensatz `Station.Site` (Zieltyp, Stückliste, Investor, Treuhand-Bausumme, Bauschiff, Zeiten, Zustand). Dadurch ist sie Hindernis, sichtbar, andockbar und ein Markt: Bestand = geliefert, Wunschbestand = verlangt, festes Gebot Basispreis × 1,25, sie nimmt nur das Fehlende an und verkauft nichts zurück. Der Lebenszyklus entscheidet nur noch, *ob* gebaut wird: höchstens zwei laufende Baustellen, nie zweimal derselbe Typ, der Investor zahlt die Bausumme als Treuhandgeld ein; ohne Baustellen-Typ oder auflösbare Stückliste (sehr alte Stände) gilt der Direktbau.
- **Stückliste zyklenfrei:** Bausumme × 0,5 als Stahl/Hüllenplatten/Elektronik (Gewichte 0,4/0,35/0,25) zum Basispreis; Waren, die der Typ selbst herstellt, fallen weg. Eigene Listen je Typ (`construction`) werden geprüft. Content-Test: kein Typ verlangt eine nur von ihm hergestellte Ware, jedes Material hat einen anderen Hersteller-Typ in der Startwelt.
- **`ConstructionSystem`** (eigenes Modul „Construction“): Bauschiffe (Aufgabe `build` im Job-Register, `BuildJob`, Grenze 4) übernehmen Baustellen (eigene Fraktion zuerst), holen Fehlendes beim nächsten Hersteller oder Lager (höchstens Basispreis × 2, bezahlt aus dem Treuhandgeld), liefern und montieren vor Ort (200 Ticks); Montagegebühr 10 % an das Bauschiff, der Rest des Treuhandgelds ist der Credit-Anteil. Händler beliefern Baustellen über den normalen Handel (Reservierung des schon Unterwegs-Befindlichen), der Spieler per freiem Handel. Verfall nach 8000 Ticks ohne Fortschritt: Treuhand zurück, Material bleibt als Lager (neues Projekt im Sektor übernimmt es).
- **Notbau:** blockiert (kein Bauschiff erreichbar, kein Lieferant zum Höchstpreis, Treuhand reicht nicht) seit 2000 Ticks (kritisch: 1000) und Abkühlzeit der Fraktion (3000) vorbei → nur das Fehlende entsteht zum Basispreis × 2 aus dem Treuhandgeld (Geldsenke, ungedeckter Rest wird erlassen und gemeldet), Montage ohne Bauschiff. Das Balance-Tool zählt angelegte/fertige/Notbau/verfallene Baustellen und das gelieferte Material nach Weg.
- **`Essential` aufgespalten (klein):** `BankruptcyProtected` (JSON `essential` als Alias, Save-Feld neu, altes Feld wird gelesen) und `Critical` abgeleitet (`StationCriticality`: letzte erreichbare Herstellerin einer Ware; kritische Stationen gehen nicht pleite, kritische Baustellen — auch Typen der Ketten Stahl, Plattierung, Werft — lösen den Notbau früher aus).
- **Startbestand:** je Fraktion ein Bauschiff *Foreman* (Bauausleger + 160 Fracht). Neue Module als Waren mit Systemwerk-Rezept: Bauausleger, Spaltlaser; Klassen Bauschiff und Asteroidenbrecher; Modelle Foreman, Quarry, Rockbreaker II.
- **Asteroidenbrecher:** `AsteroidSplitter` (2–4 Fragmente gleicher Klasse, 10 % Staub, Mindestradius, höchstens 2 × Zielzahl je Gürtel, fortlaufende Ids, Event `AsteroidSplit`), für den Spieler als Schacht „Spalter“ im Flug-HUD, für NPC-Miner mit Spaltlaser (Rockbreaker II) bei großen Brocken.
- **Lagerhaus:** Typ `warehouse` mit Dienst `storage` (persönliches Lager des Spielers, Gebühr 0,5 Cr je Einheit, 500 Einheiten je Station) und Lagerposten Baumaterial als Baustellenlager für die Bauschiffe; Station „Lagerhaus Gamma“.
- **Oberfläche:** Baustellen als Gerüst im Sektor, als Raute mit Materialbalken in der Sektoransicht, Status in Station-Info und Sektorkarte (`ConstructionInfo`); Dock-Bereich „Lager“ (`StorageScreen`, ersetzt an Lagerhäusern die Taxi-Kachel). Selbsttest (Baustelle beliefern, Spalter, Lager) und `--ui-shots` 24–28 ergänzt.
- **Save v11** mit Migration v10→v11 (Content, Baustellen-Typ, Lagerhaus mit Station, ein Bauschiff je Fraktion, `BankruptcyProtected`); mit dem echten v2-Spielstand geprüft (20000 Ticks, 872 Handelsfahrten, Flotte 14 → 14; ohne Warenschlüssel keine Bauschiffe, Direktbau als Rückfall).
- **Prüfung:** 379 Tests (22 neu in `ConstructionTests`), Selbsttest OK. Balance (frischer Release-Build): Standard-Seed 20000 und 60000 Ticks „Preise im Zielbereich“, 0 Treibstoff-Notkäufe, je 4 Baustellen fertig, 0 Notbau; Seeds 1–4 bei 20000 alle grün.

**Abweichungen und offene Punkte (Phase 8):**
- **Save-Version v11 statt v8** (Platzhalter im Plan).
- **Baustelle als Station mit `Site`-Datensatz** statt eigener Entität: So nutzen Handel, Dock, Darstellung, Hindernisse und Andocken dieselben Wege; die „Baustellenliste“ des Spielstands sind die Stationen mit `Site`.
- **Keine dauerhafte Material-Nachfrage:** Die Welt erreicht `MaxStations` nach rund 10000 Ticks, danach wird kaum noch gebaut (Pleiten sind selten). Stahl und Hüllenplatten werden nur in der ersten Bauwelle verlangt (≈310–540 Einheiten je Lauf). Ein dauerhafter Verbraucher (Wartung/Verschleiß) wäre ein eigenes Thema.
- **Notbau griff in den Balance-Läufen nie** (Anteil 0 %), nur in den Tests; das Bauschiff kauft höchstens zum doppelten Basispreis, damit das Treuhandgeld reicht (im ersten Entwurf leerte es die Treuhand bei knappem Stahl und erzwang einen Notbau).
- **Foreman ohne Laser:** Mit Laser hätte er die Fähigkeit `Mining` und wäre für Flotte und Tests ein Miner; der zweite Werkzeug-Slot bleibt frei. Selbst schürfen tut das Bauschiff nicht.
- **NPC-Brecher selten:** Nur ein gekaufter Rockbreaker II spaltet (0 Spaltungen in den Balance-Läufen); Quarry ist Spielermodell. Spalten bringt in der abstrakten NPC-Schürfsimulation keinen Durchsatzvorteil (mehrere Miner teilen sich ohnehin einen Felsen).
- **Lagerhaus:** keine Gebühr über die Zeit, kein Diebstahl/Plünderung; Lager nur an der Station sichtbar.
- **Kein Startpaket „Bauer“** (der Spieler kann noch keine eigenen Baustellen anlegen).
- **Nicht geprüft:** Handtest der Oberfläche, Android.

## Phase 9: Akteure und Fraktionen

- **Akteur/Fraktion:** Akteur als Wirtschaftssubjekt, Fraktion als Akteur mit Politik; `Ship.Owner` statt `Ship.Faction`; Mitgliedschaft; Migration der bestehenden Fraktionen.
- **Größe und Größenklassen**, Zukauf mit Erst- und Zweitzugriff (gewichtete Ziehung), Einzelschiffe träge anlegen, Sammelgruppe „Freie“.
- **Kasse** (`Treasury`), sparsame Beziehungen mit Vorgabewerten, Persönlichkeitswerte (Aggression, Gier, Kooperation, Gesetzestreue).
- **Spieler** als Einzelakteur.
- **Save v9:** Akteure, Mitgliedschaft, Kasse, Persönlichkeit.
- **Prüfung:** Geldmenge bleibt erklärbar (`EconomyStats`), keine Beziehungs-Explosion.

**Stand:** umgesetzt auf Branch `phase9-akteure`.
- **Akteur und Fraktion:** `Actor` (Art, Mitgliedschaft, Kasse `Treasury`, Persönlichkeit, persönliche Beziehungsabweichungen) in `Universe.Actors`. Jede Fraktion ist ein Akteur gleicher Id-Zahl (Art Staat/Gilde/Gesetzlose aus der Weltdatei, Mitglied ihrer selbst); `Faction` behält nur den politischen Teil. `Ship.Faction` ist ersetzt durch `Ship.Owner` (Akteur), `Station.Owner` ist ein Akteur; die Fraktion folgt aus der Mitgliedschaft des Besitzers (`Universe.FactionOf`).
- **Freie und träger Aufstieg:** Händler und Miner der Startwelt sind freie Einzelschiffe der Sammelgruppe „Freie Kapitäne“ (implizite Akteure, Beziehungen einmal gespeichert). Investiert ein freies Schiff (kauft ein Schiff, finanziert eine Station), wird es zum Akteur „Unabhängig“ (Mitglied der Hoheit seines Sektors, gewürfelte Persönlichkeit) und besitzt, was es kauft. Ohne Besitz und Geld verschwindet der Akteur wieder (`ActorSystem`, eigenes Modul „Actors“). Bauschiffe gehören dem Fraktionsakteur, Spielerschiffe dem Spieler.
- **Größe:** Größenzahl aus Schiffen, Stationen, Sektoren und Geld (Gewichte und Schwellen in den Regeln), fünf Größenklassen Einzelakteur bis Staat.
- **Zukauf:** Stationsbau und Flottenwachstum ziehen den Geldgeber gewichtet über `Universe.Rng` (weicher Erstzugriff): Gewicht = Geld über der Schwelle × Appetit der Größenklasse (0,25 … 1) × (0,5 + Gier). Kandidaten für Stationen: Kassen der Akteure, Stationen, Schiffe; für Schiffe: Schiffe der Aufgabe. Das Balance-Tool zeigt die Verteilung nach Quelle und Größenklasse.
- **Kasse:** Die Vermögensabgaben (bisher Geldsenke) fließen in die Kasse der zuständigen Fraktion (Mitglieder an ihre Fraktion, freie Schiffe an die Hoheit ihres Sektors); für die Zahler ändert sich nichts. Ausgegeben wird die Kasse für Stationsbau (Treuhand, Rückzahlung bei Verfall). Ereignisse `TaxCollected`, `TaxSunk`, `InvestorChosen`, `ActorPromoted`, `ActorRemoved`; `EconomyStats` und Balance-Tool führen Kassen und Akteure.
- **Beziehungen sparsam:** nur Abweichungen (Fraktion–Fraktion an der Fraktion, persönliche am Akteur), sonst Vorgaben (gleiche Fraktion, Staat–Freie, alle–Gesetzlose). Startwelt: Union ↔ Freie Händler +0,25.
- **Persönlichkeit:** Aggression, Gier, Kooperation, Gesetzestreue aus der Weltdatei bzw. gewürfelt; wirksam ist vorerst nur die Gier (Appetit beim Zukauf).
- **Spieler als Einzelakteur:** Die Spielerkasse ist die Kasse des Spielerakteurs; `Universe.WalletOf(ship)` ist die Kasse des Besitzers (nur Akteure mit gemeinsamer Kasse, also der Spieler; NPC-Schiffe behalten ihre Betriebskasse).
- **Save v12** mit Migration v11→v12; mit dem echten v2-Spielstand geprüft (20000 Ticks, 883 Handelsfahrten, Flotte 14 → 14, Spielerkasse 1120 Cr übernommen).
- **Oberfläche:** „Fraktionen“ und „Meine Fraktion“ zeigen echte Akteure, Kassen, Größenklassen, Beziehungen und Persönlichkeit; Platzhalter dafür entfernt. Selbsttest erweitert, `--ui-shots` 16–18 angesehen.
- **Aufräumen aus Phase 8:** Bauschiffe, Freigabe und Marktindex laufen in Id-Reihenfolge (Balance-Hash vor den Phase-9-Änderungen unverändert).
- **Prüfung:** 394 Tests (15 neu in `ActorTests`), Selbsttest OK. Balance (frischer Release-Build): Standard-Seed 20000 und 60000 Ticks „Preise im Zielbereich“, Kapazität Handel 14,3–14,4 / Bergbau 8,3, 0 Treibstoff-Notkäufe; Seeds 1–4 bei 20000 alle grün. Umlaufgeld (Stationen + Schiffe) wie vorher ≈ 140 000 Cr; Kassen 0,98 Mio. (20000) bzw. 2,6 Mio. (60000) = die frühere Abgabensenke; 2 gespeicherte Beziehungen in allen Läufen.

**Abweichungen und offene Punkte (Phase 9):**
- **Save-Version v12 statt v9** (Platzhalter im Plan).
- **Keine Kassenzusammenlegung bei NPCs:** Fraktionen und aufgestiegene Akteure führen ihre Schiffe als Eigenbetriebe mit eigener Betriebskasse; nur der Spieler bucht auf die Akteurskasse. Die Kasse eines aufgestiegenen Einzelakteurs bleibt deshalb leer.
- **Kassen wachsen nur:** Einnahmen sind die Abgaben, Ausgaben nur der Stationsbau. Die Staatskassen überragen damit alles Umlaufgeld und gewinnen fast jede Ziehung um Stationen; Polizei, Zoll und Subventionen (Phase 10/11) sollen sie ausgeben. Kassen kaufen keine Schiffe.
- **Ereignisgetriebene Beziehungsänderungen fehlen** (keine Sperren, Überfälle, Bündnisse vor Phase 10/11); Aggression, Kooperation und Gesetzestreue sind reine Daten.
- **Spieler:** Mitgliedschaft fest aus dem Startpaket; Beitreten, Austreten und Gründen folgen mit der Politik. Das Startpaket „Mitglied oder unabhängig“ gibt es noch nicht.
- **Freie Schiffe verlieren ihre alte Fraktionszugehörigkeit** (Migration): Sie zahlen Abgaben an die Hoheit ihres Sektors, ein Aufsteiger wird Mitglied dort.
- **Nicht geprüft:** Handtest der Oberfläche, Android.

## Phase 10: Gate-Zugang, Kontrollpunkte, Sektorkontrolle

- **Gate-Zustände** (offen, eingeschränkt, geschlossen, zerstört) und Politik je Fraktion; `FindRoute` mit Mobilitätsprofil (Cache nach Profil).
- **Sperrstufen 0–5**, Richtung, Mindestdauer und Hysterese, Hauptroute je Region.
- **Kontrollpunkte** (`IAccessCheck`): Politik, Ware, Entdeckung.
- **Sektorkontrolle:** Einflussberechnung, abgeleitetes `Sector.Owner`, Stufen Präsenz bis Kontrolle, „Umkämpft“.
- **Regierungsstationen:** Hauptquartier (Hub), Firmensitz, Sektor-HQ, Büro, Polizeiwache; `Government`, `BankruptcyProtected`, `Critical` etc. (Aufschlüsselung von `Essential`).
- **Save v10:** Gate-Zustand und Politik, Einfluss.
- **Prüfung:** Balance meldet zerfallende Karte oder isolierte Sektoren.

**Stand:** umgesetzt auf Branch `phase10-gates`.
- **Gate-Zustände** offen/eingeschränkt/geschlossen/zerstört (`Gate.State`; `Gate.Open` ist abgeleitet). Geschlossen und zerstört sperren physisch für alle (nur über `GateAccess.SetState`), „eingeschränkt“ folgt aus den Sperrstufen. **Mobilitätsprofil** (Besitzer, beladen, Tarnstärke) in `FindRoute`/`CachedRoute`/`IsReachable`, Cache nach Profil getrennt; ohne jede Einschränkung ist jedes Profil physisch (keine Mehrkosten, gleiche Routen). Alle KI-Planer (Händler, Miner, Flotte, Bauschiffe, Tanken) planen mit dem Profil des Schiffs, Verkaufsfahrten mit dem beladenen Profil.
- **Kontrollpunkte** (`IAccessCheck`, Registry `AccessChecks`): Politik (Mitglieder frei; Stufe 1–3 Zoll für Nichtverbündete, Stufe 3 keine Gegner, Stufe 4 niemand hinein, Stufe 5 auch niemand hinaus), Ware (Embargo ab Stufe 2: beladene Gegner abgewiesen; Schmuggelware aus den Regeln an der echten Fracht), Entdeckung (Tarnstärke gegen Sensorstärke, deterministisch). Zoll beim Reiseantritt an die Kasse der Sektorfraktion; unerreichbare Ziele werden nicht mehr „teleportiert“ (Auftrag frei, Fracht bleibt).
- **Sperrstufen 0–5** je Sektor mit **Richtung** (Ein-/Ausreise), **Mindestdauer** (2000 Ticks), **Hysterese** (0,1), **Ablauf** (12000) mit Abkühlzeit (4000), gedeckelt durch die Kontrollstufe (Dominanz bis 2, Kontrolle bis 5, umkämpft höchstens 1), **Hauptroute** als Gate-Ausnahme (höchstens Stufe 3; in der Startwelt sind alle drei Gate-Paare Hauptroute). Durchsetzung kostet die Kasse (0,5 Cr/Tick je Stufe), ohne Geld gilt die Sperre nur auf dem Papier. `FactionPolicySystem` (Modul „Politics“) entscheidet aus der schlechtesten Beziehung der Sektorfraktion (Schwellen nach Aggression verschoben); `SetLevel` ist derselbe Weg für Spielerfraktion, Tests und Szenarien.
- **Beziehungen bewegen sich** (alle 100 Ticks): Drift zum Ruhewert aus Kooperation, Aggression und gemeinsamem Handel; umkämpfte Sektoren und Sperren der Gegenseite (gewichtet mit dem Handel) senken sie — so entstehen Sperren und Gegensperren aus der Mechanik. In der Startwelt bleiben beide Beziehungen freundlich (0,17–0,30).
- **Durchsetzung für laufende Reisen:** Ändert sich ein Zugang, werden Reisen über nun gesperrte Gates vom aktuellen Ort neu geplant oder angehalten (Auftrag frei, Fracht bleibt) — keine Geisteraufträge. Händler ohne Auftrag mit Fracht verkaufen sie zuerst.
- **Sektorkontrolle:** Einfluss aus Stationen nach Typ gewichtet, abgeleiteter `Sector.Owner` mit Wechsel-Hysterese, Stufen Präsenz/Einfluss/Dominanz/Kontrolle, „umkämpft“ ab zwei Fraktionen über 30 %; Territorium folgt.
- **Regierungsstationen:** Typen Hauptquartier, Firmensitz, Sektor-HQ, Büro, Polizeiwache (Einflussgewicht, Unterhalt aus der Kasse, vor Pleite geschützt, zählen nicht gegen die Bau-Obergrenzen). Startwelt: Hauptquartier der Union (Alpha), Gildenhaus Gamma (Hauptquartier), Sektor-HQ in Beta und Delta → Alpha/Gamma Kontrolle, Beta/Delta Dominanz. **`Essential` fertig aufgeschlüsselt:** `Government`, `Capturable` (Regel `allowStationCapture`, aus), `Destructible`, `Immortal` (nur JSON) neben `BankruptcyProtected`/`Critical`.
- **Oberfläche:** Universumskarte (Ebene Fraktion mit Kontrollstufe/umkämpft, Ebene Sicherheit echt, Gates nach Zustand und „für dich gesperrt“, Detailkarte mit Sperre, Einfluss, Gate-Urteilen und Route nach deinem Profil), Sektoransicht, Sternenkarte und Station-Info (Ruf, Zoll, Sperrstufe, Hinweise echt aus `AccessInfo`). Sprünge prüfen den Kontrollpunkt (Ablehnung mit Grund, Zoll aus der Spielerkasse), Autopilot und Routenplanung respektieren das Profil. Selbsttest erweitert, `--ui-shots` 29–32 angesehen.
- **Save v13** mit Migration v12→v13 (Hauptrouten, Regierungstypen und -stationen, Sperren offen, Einfluss beim ersten Laden berechnet); mit dem echten v2-Spielstand geprüft (20000 Ticks, 874 Handelsfahrten, Flotte 14 → 14).
- **Prüfung:** Balance-Tool mit Spalten `sperr`/`iso`, Politik-Zusammenfassung und **Warnung + Exit-Code 2** bei zerfallender Karte (physisch oder für freie Händler); mit einer Testwelt (feindliche Fraktionen, ohne Hauptrouten) geprüft: Sperren bis Stufe 5, Gegensperre, „Karte zerfallen“, Exit 2. 413 Tests (19 neu in `PoliticsTests`), Selbsttest OK. Balance (frischer Release-Build): Standard-Seed 20000 und 60000 Ticks „Preise im Zielbereich“, Kapazität Handel 14,8 / Bergbau 7,6 (20000) bzw. 15,2 / 8,5 (60000), 0 Treibstoff-Notkäufe, 0 Sperren, Karte zusammenhängend; Seeds 1–4 bei 20000 alle grün (Handel 13,2–14,5, Bergbau 7,6–8,3). Kassen: Regierungsunterhalt 18 Cr/Tick = 35 % (20000) bzw. 40 % (60000) der Abgaben, Kassen 0,64 statt 0,98 Mio. bzw. 1,57 statt 2,6 Mio.

**Abweichungen und offene Punkte (Phase 10):**
- **Save-Version v13 statt v10** (Platzhalter im Plan).
- **In der Startwelt entsteht nie eine Sperre:** Die Beziehungen bleiben freundlich, und kein Investor baut in fremden Sektoren (umkämpft wird nichts). Sperren entstehen real aus der Mechanik (umkämpfte Sektoren → Meinungsverlust → Sperre → Gegensperre), aber nur in Tests und eigenen Welten. Ereignisse, die Gates schließen oder zerstören (Wartung, Kampf), gibt es noch nicht — nur die Schnittstelle.
- **Hauptroute statt abgeleiteter Engpässe:** Die Startkarte ist ein Stern um Beta; jedes Gate ist die einzige Verbindung, daher sind alle drei Paare Hauptroute (höchstens Stufe 3). Freie Händler (neutral) zahlen dort höchstens Zoll; Stufe 4–5 wirkt nur an Gates ohne Hauptroute.
- **Zoll beim Reiseantritt** für die ganze Route (nicht beim Durchflug); die NPC-Routenbewertung rechnet ihn nicht ein. Entdeckung ohne Würfel (es gibt kein Tarnmodul). Ausreisestufen 1–4 wirken nicht (nur die Abriegelung sperrt die Ausreise).
- **Patrouillenabdeckung** ist bis Phase 11 ein Ersatz (Hauptquartier oder Polizeiwache). Firmensitz und Büro sind nur Typen ohne Instanz und ohne eigene Dienste (`headquarters`, `customs` usw. aus STATIONEN.md sind keine Stationsdienste geworden: Regierungsstationen haben keinen Markt; was sie tun, zeigt die Station-Info).
- **Keine Spielerfraktion**, keine Politikmaske in der Oberfläche, kein Krieg, Tribut oder Vermitteln; „Unbeteiligte reagieren auf Betroffenheit“ fehlt.
- **Fehlerkorrektur nebenbei:** Die Kopie-Regel des `FleetSystem` hielt die Kapazitätsgrenze nicht ein (ein kopierter Hauler konnte sie überschreiten) — jetzt geprüft. Über Aufgabenwechsel von Multirole-Schiffen kann die Handelskapazität weiterhin über der Grenze liegen (15,2/14 bei 60000 Ticks, Phase-6-Verhalten).
- **Nicht geprüft:** Handtest der Oberfläche, Android.

## Phase 11: Polizei, Fahndung, Piraten

- **Lagekarte** (Bedrohung, Sicherheit) zuerst nur als Beobachtung (Statistik, Anzeige).
- **`SecuritySystem`:** Zuweisung von Polizeischiffen nach Bedrohung und Wert; Heimatbasis; Unterhalt aus der Kasse.
- **Fahndungsniveau** je Hoheitsgebiet mit Verfall; Verbrechen und Folgen; Kopfgeld; Kopfgeldbüro und Gericht.
- **Piraten** als Gegenfraktion; Überfälle auf unbewachte Routen.
- **`IEncounterResolver`:** abstrakte Auflösung nach Stärke, später durch echten Kampf ersetzbar.
- **Save v11:** Fahndung, Lagekarte, Piratenzustand.
- **Prüfung:** Verlustquote, Polizeianteil am Haushalt, Rechenzeit der Scans.

**Stand:** umgesetzt auf Branch `phase11-polizei`.
- **Lagekarte** (`ThreatMap`, neues Modul „Security“ nach Bau, vor Lebenszyklus): je Sektor Bedrohung (steigt durch Überfälle, abgewehrte Überfälle, Schmuggel; verfällt; Streifen drücken sie), Sicherheit (Grundschutz der Regierungsstationen des Besitzers — HQ 1,5, Sektor-HQ 0,5, Wache 2 — plus Stärke seiner Streifen im Sektor) und Verkehrswert (Fracht der KI-Schiffe im Sektor). Daraus das Routenrisiko für Händler und Miner und die **Patrouillenabdeckung** der Sektorkontrolle (ersetzt die Phase-10-Annahme „HQ oder Wache“; Startstufen unverändert, Beta/Delta erreichen „Kontrolle“, solange eine Streife da ist).
- **`IEncounterResolver`** mit abstrakter Auflösung nach Stärke (Waffen, Schild, Hülle aus den Modulen) über `Universe.Rng`, austauschbar (`Universe.Encounters`); neues Waffenmodul Laserkanone.
- **Polizei** (`SecuritySystem`): je Fraktion zwei Wächter (Patrouillenboot) am Hauptquartier, Unterhalt aus der Kasse, Soll nach Territorium und Bedrohung (1–4), Nachkauf über die gemeinsame Kaufregel an der Werft aus der Kasse, Abbau bei dauerhafter Überzahl, Zuweisung nach Bedrohung und Verkehrswert nur im eigenen Gebiet, Streifenflug, Heimkehr zu Reparatur und Tanken, Abfangen lauernder Kaperschiffe, Eingreifen bei Überfällen.
- **Piraten**: gesetzloser Akteur „Rote Korsaren“ (neuer Weltabschnitt `actors`) mit Versteck in Delta und drei Korsaren; Revierwahl nach Beute gegen Sicherheit, Lauern am Gate, Überfälle auf Schiffe abseits der Stationen (Fracht anteilig weg, Hehlerei in die Piratenkasse, selten Schiffsverlust; die Flotte wächst nach), Heimkehr, Neubau aus der Kasse. Piraten finanzieren keine Stationen.
- **Spieler**: Überfall nur mit Vorwarnung (15 s; Andocken oder Sektor verlassen = entkommen), Hülle bleibt über 25 %, kein Schiffsverlust; Sieg über ein Kaperschiff bringt Kopfgeld. Beim Nachholen der Offline-Zeit keine Überfälle auf den Spieler.
- **Fahndung** je Akteur und Hoheitsgebiet (Heat, Stufen sauber … Staatsfeind, Mindestdauer, Verfall, Obergrenze, Strafe); Verbrechen: Schmuggel am Kontrollpunkt, **Zollflucht** (Sprung trotz fehlenden Zolls, früher abgewiesen), Piraterie. Ab Stufe 3 stellt eine Streife den Spieler im Sektor (Vorwarnung, Strafe eingezogen, Schmuggelware beschlagnahmt).
- **Gericht und Kopfgeldbüro** als Dienste `court`/`bounty` an Hauptquartier, Sektor-HQ und Wache (Strafe zahlen, Rechtshilfe unter nicht feindlichen Fraktionen; Kopfgeld aus der Fraktionskasse; Fahndungsliste).
- **Oberfläche:** Lage- und Fahndungszeile im Flug-HUD, Meldungen zu Vorwarnung/Überfall/Kontrolle, Universumskarte (Ebene Sicherheit aus der Lagekarte, Detailkarte mit Lage, Streifen, Risiko, Fahndung), Sektoransicht und Station-Info mit Lage, „Meine Fraktion“ mit Fahndung je Fraktion, neuer Dock-Bereich „Behörde“ (ersetzt dort „Aufträge“), Polizei blau und Piraten rot in Sektor und Sektoransicht. Selbsttest erweitert, `--ui-shots` 33–40 angesehen.
- **Save v14** mit Migration v13→v14 (Content, Grundschutz, Versteck-Typ, Gründer-Polizei, Piraten nur in Welten, deren Flotte Verluste ersetzen kann); mit dem echten v2-Spielstand geprüft (20000 Ticks, 926 Handelsfahrten, Flotte 14 → 14, Polizei ja, Piraten nein).
- **Prüfung:** 429 Tests (16 neu in `SecurityTests`), Selbsttest OK. Balance (frischer Release-Build): Standard-Seed 20000 und 60000 Ticks „Preise im Zielbereich“, Kapazität Handel 13,6/14,6, Bergbau 8,5/8,3, 0 Treibstoff-Notkäufe; 46 bzw. 127 Überfälle, **Verlustquote 0,49 % bzw. 0,56 %** des Handelsumsatzes, 0 bzw. 1 Schiffsverlust, **Polizeianteil 6,7 % bzw. 8,1 %** der Kasseneinnahmen, **Rechenzeit** des Sicherheitsmoduls 2,8 % bzw. 1,9 %. Seeds 1–4 bei 20000 alle grün (Handel 12,9–14,8, Bergbau 8,0–8,3, 24–42 Überfälle, Verlustquote 0,28–0,56 %, Polizei 8–9 %).

**Abweichungen und offene Punkte (Phase 11):**
- **Save-Version v14 statt v11** (Platzhalter im Plan).
- **Lagekarte je Sektor statt je Fraktion und Sektor:** Die Sicht ist die des Sektorbesitzers (nur er hat dort Polizeigewalt).
- **Abstrakte Begegnungen ohne Kampfansicht:** Der Spieler gewinnt nur mit Waffen (z. B. Wächter oder Frachter mit Laserkanone); Kopfgeld gibt es daher selten, aktive Kopfgeldjagd fehlt.
- **Verbrechen:** Angriff und Gebietsverletzung sind nicht verdrahtet (der Spieler greift keine Schiffe an); NPCs schmuggeln nicht. **Zollflucht ändert Phase 10:** Fehlt Geld für den Zoll, springt der Spieler jetzt (Verbrechen) statt abgewiesen zu werden.
- **Polizei:** kauft nur, wenn die Werft alle Module hat (im Standard-Seed nie); keine Verfolgung über Grenzen, keine Eskorten, keine Schutzverträge. Das Abfangen eines gesuchten Spielers zieht Strafen auch während des Offline-Nachholens ein (angekündigt, nur Strafe/Schmuggelware).
- **Piraten:** kein Schwarzmarkt, Flotte höchstens drei Kaperschiffe, Kasse meist nahe null; in Welten ohne Warenschlüssel (sehr alte Stände) keine Piraten. Die Zerstörungschance von Opfern wurde halbiert (0,015), weil Ersatzkäufe die knappen Mk1-Module (Reaktor, Triebwerk) über 3× trieben (Seed 4).
- **Nicht geprüft:** Handtest der Oberfläche, Android.

## Phase 12: Verbände, Befehle, Spielerflotte

- **`Squadron`** (Anführer, höchstens 8 Mitglieder, Formation nur Darstellung), Heimatbasis, Befehlskapazität nach Größe.
- **`IOrderExecutor`** pro Befehlstyp: `GoTo`, `Hold`, `Patrol`, `Guard`, `Escort`, `Follow`, `Trade`, `Mine`, `Build`, `Return` und weitere; Ebenen Notfall > Befehl > Automatik; Verhaltensregeln.
- **Spielerflotte:** Flottenliste und Befehlsmenü (höchstens drei Schritte), Automatikaufträge mit Grenzen.
- **`OrderSave`** erweitern (heute drei Ganzzahlen).
- **Hangar, Taxi, Leihschiff:** Dienste `hangar`, `taxi`, `rental`; Schiffe haben einen Standort (angedockt an einer Station, `Ship.DockedAt`); das Taxi bewegt nur den Spieler, das Schiff bleibt.
- **Oberfläche:** Flug-HUD, Andock-Menü, Dock-Bereiche und Kommandozentrale sind **bereits gebaut** (UI.md); hier bleibt nur die Anbindung der Platzhalter an echte Daten (Hangar, Taxi, Flotte, Schiffwechsel, Befehle).
- **Save v12:** Verbände, Befehle, Heimatbasis.

**Stand:** umgesetzt auf Branch `phase12-verbaende`.
- **Befehle mit Ausführern:** `ShipCommand` (Fliegen, Halten, Handel, Schürfen, Bauen, Patrouille, Wache, Eskorte, Folgen, Heimkehr) in `Ship.Command`, je Befehlstyp ein `IOrderExecutor` in der Registry `OrderExecutors`; `OrderSystem` (neues Modul „Orders“ vor der Aufgabenwahl) mit den Ebenen **Notfall > Befehl > Automatik** (Notfall: Hülle unter 30 % oder Tank leer → nächste Station mit Reparatur, versorgen, Befehl läuft weiter). Arbeitsbefehle setzen nur die Aufgabe; Händler-, Miner- und Bau-System bleiben die Ausführer (Automatik-Ebene), mit den **Grenzen** des Auftrags (Reichweite in Sprüngen um einen Mittelpunkt, höchster Einkaufspreis × Basis, Rücklage in der Kasse). NPCs ohne Befehl laufen unverändert.
- **Spielerflotte:** eigene Schiffe bekommen Befehle und Automatikaufträge, buchen auf die Spielerkasse (auch ein Bauschiff als Auftragnehmer einer Baustelle: Material aus der Treuhand, Baugebühr an die Kasse), arbeiten beim Offline-Nachholen weiter, zahlen Unterhalt (wie KI-Schiffe) und an fremden Stationen Liegegebühr je Klasse und Tag. **Befehlskapazität** nach Größenklasse (Einzelbefehle 4/8/16/32/64, Verbände 1/1/3/6/12).
- **Verbände:** Anführer, höchstens 8 Mitglieder, Formation nur Darstellung (Linie, Keil, Stern, Block), Heimatbasis, Verhaltensregel; Bewegungsbefehle plant der Anführer mit dem Tempo des langsamsten Mitglieds, die übrigen folgen; Arbeitsbefehle führt jedes fähige Mitglied aus; Nachfolge bei Ausfall des Anführers.
- **Verhaltensregeln:** Eskorten und Wachen (Verteidigen/Angriff) stehen Opfern im Überfall bei, Streifen/Wachen mit „Angriff“ stellen lauernde Kaperschiffe; nicht aktive Spielerschiffe können überfallen werden (ohne Vorwarnung, nie Verlust).
- **Hangar, Liegeplatz, Fernwechsel:** `Ship.DockedAt`; Dienst `hangar` (wechseln, heim schicken, verkaufen; fremde Schiffe sichtbar), Liegegebühr nur an fremden Stationen; Schiffwechsel aus der Ferne (UI.md „dort, wo es ist“), das alte Schiff hält die Position.
- **Taxi und Leihschiff:** Dienst `taxi` (die Person reist allein zu einer Station mit eigenem Schiff oder Leihschiff; Preis aus Grundgebühr, Sprüngen, Strecke und Zoll; gesperrte Routen mit Grund; die Reisezeit vergeht im Zeitraffer), Dienst `rental` (Pip, Gebühr, Pfand, Miete je angefangenem Tag).
- **Oberfläche:** Andock-Menü mit echten angedockten Schiffen und rollbarem Raster (bis neun Kacheln, Lager/Behörde verdrängen das Taxi nicht mehr), Hangar, neuer Bereich Taxi, Flotte mit Befehlsraster/Zielwahl/Grenzen/Verhalten/Verbänden, Besitz mit Fernwechsel/Befehlen/Heim/Verkaufen, Taxi auf der Universumskarte, „Meine Fraktion“ mit echter Liegegebühr und Kapazität; Platzhalter dafür aus `UiPlaceholders.cs` entfernt. Selbsttest erweitert, `--ui-shots` 41–47 angesehen.
- **Save v15** mit Migration v14→v15 (Dienste, Liegeplatz aus der Position), `OrderSave` um Listen erweitert; mit dem echten v2-Spielstand geprüft (20000 Ticks, 926 Handelsfahrten, Flotte 14 → 14, Speichern/Laden hash-gleich).
- **Prüfung:** 450 Tests (21 neu in `FleetCommandTests`), Selbsttest OK. Balance (frischer Release-Build) identisch zu Phase 11 bis auf den Hash: Standard-Seed 20000 und 60000 Ticks „Preise im Zielbereich“, 14/8 Händler/Miner (Kapazität 13,6/14 bzw. 14,6/14), 0 Notkäufe, Verlustquote 0,49 % bzw. 0,56 %, Polizei 6,7 % bzw. 8,1 % der Kasseneinnahmen; Seeds 1–4 bei 20000 alle grün (Verlustquote 0,28–0,56 %, Polizei 8,1–9,3 %).

**Abweichungen und offene Punkte (Phase 12):**
- **Save-Version v15 statt v12** (Platzhalter im Plan).
- **Polizei und Piraten** laufen weiter in ihren Phase-11-Systemen; sie nutzen nur die gemeinsame Streifenpunkt-Regel, und Eskorten/Wachen/Jäger der Spielerflotte greifen in ihre Begegnungen ein. Fraktionen geben Verbänden noch keine Befehle (Reihenfolge des Auftrags: dieser Punkt zuletzt, bewusst offen gelassen, um die Phase-11-Balance nicht zu verschieben).
- **Heimatbasis** ist für die Spielerflotte eine beliebige Station (Kaufort bzw. Liegeplatz bei der Verbandsgründung), nicht zwingend eine eigene — der Spieler besitzt noch keine Stationen. In der Oberfläche nicht änderbar.
- **Befehlskapazität:** Einzelbefehle und Verbände getrennt gezählt; Verbandsmitglieder zählen gegen die Verbandsgrenze. Werte sind Startwerte (Entscheidung offen in FLOTTEN.md Abschnitt 11).
- **Verhaltensregeln** wirken nur in den abstrakten Begegnungen (wer verteidigt, wer jagt); „Passiv“/„Feuer halten“ fliehen nicht eigens.
- **Blockade, Schleppen, Rettung, Erkunden** und weitere Befehle aus der Referenz fehlen (brauchen Kampf/Wracks/Rettungsdienst).
- **Taxi** ist in der Simulation sofort, die Reisezeit vergeht im Zeitraffer; Leihschiffe ohne Bestand je Station und ohne Schadensabrechnung.
- **Spielerflotte** verdient wie ein NPC-Händler, zahlt aber keine Vermögensabgabe; ein Balance-Lauf mit großer Spielerflotte steht aus. Überfälle auf Flottenschiffe laufen auch beim Offline-Nachholen.
- **Nicht geprüft:** Handtest der Oberfläche, Android.

## Phase 13: Forschung

- `research`-Sektion, `ResearchSystem`, Freischaltungen über `IsUnlocked`, Besitzer ist der Akteur mit Forschungsstation (Budget vom HQ). Details in [FORSCHUNG.md](FORSCHUNG.md).

**Stand:** umgesetzt auf Branch `phase13-forschung`.
- **Inhalte:** Abschnitt `research` in der Weltdatei (Projekt: Schlüssel, Name, Beschreibung, FP, Voraussetzungen nur früher genannter Projekte, Freischaltungen `module:`/`model:`/`stationType:`, Zusatzverbrauch je Takt). Startwelt: drei Projekte — *Fusionstechnik Mk3* (3000 FP, Platin) → Reaktor Mk3, *Präzisionslaser* (3000 FP, Wafer) → Mining-Laser Mk3, *Tiefbau-Schürfer* (5000 FP, braucht beide, Titan) → Schürfschiff *Excavator*. Die Mk3-Module sind Waren mit Fabrikrezept (Antriebs-/Systemwerk, `maxTier` 3, Phase-5a-Mechanismus), kleine Lager (Fabrik [1,1], Werft [0,1]) und Verbesserungspfade Mk2 → Mk3 (Phase 7). Alles, was Startflotte, KI-Flotte, Polizei und Stationsbau heute brauchen, nennt kein Projekt und ist frei.
- **Forschungsstation:** Stationstyp `research_station` mit neuer Regierungsform „research“ (offiziell: Unterhalt aus der Fraktionskasse, vor Pleite geschützt, zählt nicht gegen die Bau-Obergrenzen, Einfluss 0), Lager für Energie, Elektronik, Platin, Wafer, Titan, Dienste Handel/Forschung/Hangar. Je Fraktion eine: „Forschungsinstitut der Union“ (Alpha) und „Gildenlabor Gamma“.
- **`ResearchSystem`** (eigenes Modul „Research“ direkt nach Produktion/Markt): Gebotsdeckel 1,25 × Basispreis für die Takt-Güter, Budget aus der Kasse der Besitzerfraktion bis 5000 Cr (höchstens 8 Cr/Tick, `TreasuryUpkeep` „research“ — die Senke für die wachsenden Kassen), alle 10 Ticks ein Takt: 2 Energie + Zusatzverbrauch des Projekts aus dem Lager, 10 Cr Personal (Senke), 5 FP auf das laufende Projekt der Fraktion; fehlt etwas, stockt die Forschung. Die Heuristik wählt das am weitesten fortgeschrittene verfügbare Projekt, sonst die Content-Reihenfolge; ein Auftrag des Spielers hat Vorrang. Abschluss → `ResearchCompleted`.
- **Freischaltungen über `Research.IsUnlocked`** (fraktionsweit; eine Station nutzt den Stand ihrer Besitzer- bzw. Sektorfraktion): Produktion (Fabrik stellt gesperrte Module nicht her), Werft/KI-Flotte/Polizeikauf (`ShipPurchase.SellsModel`), Ausrüster (gesperrte Module „nicht erforscht“ sichtbar, aus dem Lager nicht einbaubar, aus der eigenen Fracht schon), Upgrades (Grund „nicht erforscht“), Güteklassen (nur erforschte Ziele, Gründer nur freie Module), Stationsbau (Typ muss für die Investorfraktion erforscht sein). Regel `researchActive` aus = alles frei.
- **Spieler:** Dienst `research` — Projekt beauftragen (500 Cr Gebühr an die Station) und das laufende Projekt mitfinanzieren (500/2000/10000 Cr, 20 Cr je FP; so kommt er früher an neue Module und Schiffe); Güter liefert er über den normalen Handel. Feindliche Fraktionen nehmen nichts an.
- **Oberfläche:** neuer Dock-Bereich „Forschung“ (`ResearchScreen`: Projekte mit Status, Balken, Freischaltungen, „Beauftragen“; Betrieb der Station mit Verbrauch, Stockungsgrund, Kasse, voraussichtlicher Dauer; „Mitfinanzieren“) aus `ResearchInfo` und `Offers`/`Execute`; Kommandozentrale: Fraktionsdetail mit Forschungsstand (statt des Ereignis-Platzhalters), „Meine Fraktion“ mit der Forschung der eigenen Fraktion; Werft und Ausrüster zeigen Gesperrtes als „nicht erforscht“. Selbsttest erweitert, `--ui-shots` 48–52 angesehen.
- **Save v16** mit Migration v15→v16 (Content, Verbesserungspfade, Forschungsstationen, Projekte nur bei auflösbarem Inhalt und vorhandener Station, sonst bleibt alles frei); mit dem echten v2-Spielstand geprüft (keine Forschung, alles frei, 20000 Ticks, 926 Handelsfahrten, Flotte 14 → 14, Speichern/Laden hash-gleich).
- **Balance-Tool:** Spalten `fp`/`erf`, Forschungszeile (Takte, FP, Stockungsgründe, Budget aus Kassen, Personal, Güter, Spieler), Freischaltungen mit Tick, Stand je Fraktion, Forschungsbudget als Anteil der Kasseneinnahmen; `--research off` für Vergleichsläufe.
- **Prüfung:** 469 Tests (17 neu in `ResearchTests`, 1 neu in `SecurityTests`), Selbsttest OK. Balance (frischer Release-Build): Standard-Seed 20000 Ticks „Preise im Zielbereich“, Kapazität Handel 16,9/14, Bergbau 7,6/8, 0 Notkäufe, Verlustquote 0,25 %, 4 Freischaltungen (beide Fraktionen Fusionstechnik und Präzisionslaser), Forschungsbudget 181 000 Cr = 15,8 % der Kasseneinnahmen; 60000 Ticks „Preise im Zielbereich“, Handel 14,2/14, Bergbau 8,5/8, 0 Notkäufe, Verlustquote 0,25 %, alle 6 Freischaltungen (letzte bei Tick ≈23 000), Kassen 1,10 statt 1,57 Mio. Seeds 1–4 bei 20000 alle grün (Handel 13,2–15,2, Bergbau 7,6–8,3, Verlustquote 0,30–0,57 %, 2–4 Freischaltungen); Seeds 1–6 bei 60000 alle grün.

**Abweichungen und offene Punkte (Phase 13):**
- **Save-Version v16 statt v13** (Platzhalter im Plan).
- **Elektronik ist nicht der Hauptverbrauch** (FORSCHUNG.md): Mit 0,2–0,5 Elektronik je Takt kippten auf drei von acht Seeds Reaktor/Triebwerk Mk1 oder Konsumgüter über 3× (die Elektronikkette ist seit Phase 5c der Engpass). Die Forschung verbraucht deshalb Energie, Personalgeld und kleine Projektzusätze (Platin, Wafer, Titan).
- **Nur ein kleiner Satz Freischaltungen:** Module und ein Modell. Der Mechanismus kann Stationstypen sperren (im Test geprüft), die Startwelt nutzt es nicht; Rezept-, Waren-/Erz-Freischaltungen und „Verbesserungen“ (Nachladen, ChunkChance, Treibstoff) fehlen. Kein exklusiver Frühzugriff für Geldgeber (nur Beschleunigung), keine Forschungsmissionen; eine zerstörte Forschungsstation nimmt nichts zurück.
- **Korrekturen außerhalb der Forschung:** (1) Speichern/Laden war nach Entfernen und Neuanlegen von Schiffen nicht bitgleich (Einfügereihenfolge der Dictionaries); jetzt werden die Lücken geschlossen, Fracht wird in Waren-Id-Reihenfolge verkauft. (2) Piraten-Neuanfang (Phase 11): Eine ausgelöschte Piratenflotte bekam nie wieder Geld und blieb weg (Verlustquote ≈0,1 % in langen Läufen, auch schon in Phase 12); jetzt kleine Versteck-Einnahme ohne Flotte und halber Preis für das erste Kaperschiff.
- **Tests bewusst angepasst** (Zufallsfolge verschoben): Polizei-Langlauf prüft Kaperschiffe im letzten Viertel (Standard-Seed: Flotte ab Tick ≈18 000 ausgelöscht, baut neu auf), Multirole-Kapazität Spielraum +3 statt +2 (Standard-Seed 16,9/14 durch vier handelnde Tinker).
- **Balance:** Seeds 1–8 bei 20000 sieben von acht grün (Seed 6: Konsumgüter 3,12×; Phase 12 acht von acht); die Mk1-Modul-Bänder bleiben knapp. KI kauft den Excavator selten (schlechterer Wert je Credit als Einheitsschürfer). Simulation ≈25–30 % langsamer (mehr Handelsrouten und Märkte).
- **Nicht geprüft:** Handtest der Oberfläche, Android.

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
- **Phase 8:** ~~Umfang der `Essential`-Aufspaltung~~ entschieden und umgesetzt: nur `Critical` (abgeleitet) und `BankruptcyProtected` (ROADMAP.md, Punkt 23).
- **Phase 9:** ~~Ob Einzelschiffe sofort oder träge Akteure werden~~ entschieden und umgesetzt: träge (Aufstieg beim ersten Investieren). Beitritt des Spielers zu Fraktionen: vorerst nicht — Mitglied der Fraktion des Startpakets, Beitreten/Gründen mit der Politik (Phase 10).
- **Phase 10:** ~~Sperren je Sektor oder je Gate~~ entschieden und umgesetzt: je Sektor (Ein- und Ausreise getrennt), Gate-Ausnahme über die Hauptroute (`mainRoute`, dort höchstens Stufe 3).
- **Jede Phase:** Nach dem Lauf des Balance-Tools entscheiden, ob Startwerte angepasst werden.
