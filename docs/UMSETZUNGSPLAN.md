# Umsetzungsplan

Gesamtplan für alles, was in den Planungsdokumenten steht. Reihenfolge nach Abhängigkeit und nach dem Grundsatz **Tiefe vor Breite**: Zuerst wird die Schiffs- und Handelskette vollständig, dann folgen Welt, Politik und Flotten. Der Plan ist ein Vorschlag, nichts davon ist begonnen. Größen: **S** (kleiner Schritt), **M** (mehrere Dateien, eigene Tests), **L** (neues Teilsystem mit Save-Änderung und Balance-Arbeit).

Verweise: [UI.md](UI.md), [SCHIFFE.md](SCHIFFE.md), [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md), [STATIONEN.md](STATIONEN.md), [POLITIK.md](POLITIK.md), [FLOTTEN.md](FLOTTEN.md), [FORSCHUNG.md](FORSCHUNG.md), [ROADMAP.md](ROADMAP.md).

## 0. Arbeitsregeln für jede Phase

1. **Architektur zuerst:** Neue Funktion als `ISimSystem` in einem Modul (`DefaultModules()`), Kopplung über `Universe.Events`, Inhalte in `default-world.json`, `SpaceSim.Core` ohne Godot und Dateizugriff.
2. **Test zuerst bei Umbauten:** Vor einem Umbau müssen die vorhandenen Tests grün sein; der Umbau darf kein Verhalten ändern, bis die Phase es ausdrücklich will (`UniverseHash`-Tests belegen es).
3. **Persistenz:** Neue Zustände in `SaveGame`-DTOs, `UniverseSerializer` und `UniverseHash`; bei Formatbruch `SaveGame.CurrentVersion` erhöhen und eine Migration schreiben (Stand heute: v4).
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
| 5 | Modul-Waren, drei Modulfabriken (5a), Spielerkasse (5b), Ausrüster, Schiffskauf, Startpakete (5c) | L | v6 |
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

**Stand:** 4a (Datenmodell: `propellant`, `baseFuel`/`fuelCapacity`, Antriebs-`fuel`-Liste mit `efficiency`, `Ship.Fuel`, Save v5 mit Migration v4→v5) und 4b (Wirtschaft) sind umgesetzt (Branch `phase4-treibstoff`). 4b: Stationstyp Treibstoffraffinerie (3 Wasserstoff + 1 Energie → 4 Treibstoff) mit zwei Instanzen (Beta, Alpha); `Travel.Start` verbraucht echten Treibstoff (Strecke in Sektoren × `fuelUse` 0,002 / `efficiency`, Gate-Sprünge frei) statt der Credit-Pauschale; `Simulation/Refuel.cs` (Tanken an der aktuellen Station, Tankstopp über `GoToOrder`, Notkauf zum doppelten Basispreis als Geldsenke, Reserve-Regel 75 %); `TraderSystem` zieht die Treibstoffkosten der Runde vom Gewinn ab. 274 Tests, Selbsttest OK, Balance 20000 Ticks (Seeds 1–8) „Preise im Zielbereich“, 0 Notkäufe. Der Spieler verbraucht noch keinen Treibstoff (kommt mit `refuel` in 4c).

**Bekannte Grenzen von 4b:** Der Treibstoffpreis stieg über lange Läufe auf 4× Basispreis (60000 Ticks, 5 von 8 Seeds), weil die Händler-KI die Energie-/Wasserstoffversorgung der Raffinerien nicht zuverlässig bediente (Händler verfallen später in Hin-und-her-Fahrten einzelner Waren, z.B. Lebenserhaltung; allgemeines Verhalten, kein Treibstoffproblem). Gelöst durch eine zweite Rezeptur der Raffinerie ohne Eingänge (Gasförderung, 1 Treibstoff je 20 Ticks): Treibstoff versiegt nicht mehr, die Wasserstoffkette liefert nur den Mehrbedarf. Ergebnis: bei 60000 Ticks auf allen 9 getesteten Seeds kein Treibstoff-Alarm (Ø ca. 0,33× Basispreis, Überangebot). Verworfen: Lifecycle baut Vorstufen bei knappem Eingang, mehr Händler (nicht monoton), Handel nur Erzeuger→Verbraucher (sättigt die ganze Wirtschaft, alle Preise 0,3×). Offen, unabhängig vom Treibstoff: auf manchen Seeds kippen Nahrung/Lebenserhaltung bei 60000 Ticks in den Warnbereich (Händler-Hin-und-her als wahrscheinliche Ursache). `fuelUse` wurde per Balance-Lauf auf 0,002 gesetzt (die wörtliche Kredit-Äquivalenz zur alten Pauschale hätte den Tank 20000 Ticks lang reichen lassen und keinen Warenfluss erzeugt); Fahrten kosten damit etwa das Siebenfache der alten Pauschale. Das Simulationstempo liegt bei ca. 2,3–2,9 Ticks/ms statt 4,5 (zusätzliche Reiseplanung je Fahrt); ein erster Wert von 1,1 Ticks/ms kam von neu berechneten Strecken und Preisen in der innersten Bewertungsschleife und ist behoben.

## Phase 5: Module als Waren, Fabriken, Ausrüster, Schiffskauf

**Ziel:** Der Spieler kann Schiffe und Module kaufen und umrüsten; NPCs nutzen dieselben Regeln. **Größe L, in drei Teilschritten umzusetzen (5a, 5b, 5c), je mit eigenem Test- und Balance-Lauf.**

**5a – Module als Waren, drei Fabriken, Bausatzstufen**
- **Modul-Waren (SCHIFFSSLOTS.md, „Module sind Waren“):** `WorldBuilder` legt zu jedem Modul automatisch eine Ware gleichen Schlüssels an, **angehängt hinter den JSON-Waren in fester Reihenfolge** (nach Modulschlüssel), damit `GoodId` (Index+1) und die Konstanten in `DemoUniverse` stabil bleiben. Eigenschaften: `form = solid`, `Volume` aus der Modulmasse, `BasePrice` = Summe der Zutatenpreise (`cost`) mal Verarbeitungsaufschlag (`EconomyRules.ModuleMarkup`), Farbe nach Slotart. **`cost` wird das Rezept** der Fabrik.
- **Drei Fabriken:** Antriebs- und Reaktorwerk, Waffen- und Schildwerk, Systemwerk als neue Stationstypen in der JSON, Stufe nach Stationsgröße (klein Mk1, mittel Mk2, Hub Mk3). Erster Modulsatz: Reaktor, Speicherbank, Triebwerk, Tank, Frachtmodule, Laser, Traktor, Schild, Panzerung. Verteilung in der Startwelt als offene Entscheidung (siehe unten).
- **Lager ohne Rezept:** Stationstypen bekommen `stocks` (Waren mit Zielbestand), die zusätzlich zum Rezept Markteinträge anlegen (heute entstehen Märkte nur aus Rezepten). Nötig für Ausrüster, Handelsstation und die Montage kleiner Schiffe. Händler beliefern sie über den normalen Handel.
- **Bausatzstufen:** `ship_kit` in `ship_kit_light/medium/heavy` (`ship_kit_capital` später mit der Hub-Werft); `EconomyRules.ShipKitGoodKey` wird je Klasse gewählt; `FleetSystem` und Werftrezepte angepasst.
- **Tests:** Jedes Modul hat genau eine Ware, Rezepte stimmen mit `cost`, `GoodId`-Konstanten unverändert, Content-Test „jedes Modell ist baubar“.

**5b – Spielerkasse (Voraussetzung für Schiffskauf, mehrere Schiffe, Taxi)**
- Das Geld des Spielers wandert von `Ship.Credits` in ein kleines Objekt `PlayerWallet` an `Universe` (die Person); `TradeActions` buchen über `Universe.WalletOf(ship)` (Spielerschiffe: Wallet, NPC-Schiffe: `ship.Credits`, Verhalten bleibt gleich). **Zwischenstufe:** Das Akteurmodell kommt erst in Phase 9, dann wird `PlayerWallet` zur Kasse des Spielerakteurs (POLITIK.md, „Spielerkasse“).
- **Save:** Spielerkasse, Migration von `Ship.Credits` des Spielerschiffs; `UniverseHash` und `EconomyStats` zählen die Geldmenge nach Kassen.
- **Tests:** Summe aus Wallet und Schiffen ändert sich durch Handel nicht außer durch dieselben Flüsse wie vorher; Migration; Hash-Vergleich NPC-Wirtschaft unverändert.

**5c – Dienste und Schiffskauf**
- **Dienste:** `outfitter` (Module ein- und ausbauen, Waren aus dem Stationslager; ausgebaute Module wandern ins Lager oder in die Fracht), `shipdealer` (`BuyShip`, Bausatz und Zusatzmaterial aus dem Lager, Werft bekommt das Geld), Handelsstation als neuer Typ. Kleine Schiffe auch an Handelsstation, Ausrüster und Raffinerie (Montage aus `ship_kit_light`, siehe SCHIFFSSLOTS.md).
- **Modelle der Skiff-Klasse** (*Pip*, *Nomad*, *Prospector*) kommen hier in die JSON, weil die Startpakete sie brauchen; Phase 3 liefert nur die Einheitsmodelle.
- **Startpakete** (`player.startOptions`): Schürfer (*Prospector*), Händler (*Nomad*), Generalist (*Pip* mit Werkzeug-Slot; *Tinker* kommt erst in Phase 6).
- **Save v6:** Spieler-Fit bleibt im Schiff; Module als Waren sind Content.
- **Tests:** Werft verbraucht Bausatz und Zusatzmaterial, Spielerkauf, Umrüstung ändert `ShipStats`, ungültiger Einbau wird abgelehnt.
- **Balance (alle Teilschritte):** Neue Nachfrage nach Titan und Platin; `tools/SpaceSim.Balance` auf Preisentgleisung prüfen.

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
