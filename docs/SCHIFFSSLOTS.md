# Schiffsslots, Treibstoff, Sprungantrieb, Tarnfeld, Dienste (Planung)

**Status: reine Planung, nichts davon ist umgesetzt.** Ergänzt [SCHIFFE.md](SCHIFFE.md) (Klassen, Modelle, Multirole). Alle Zahlen sind Startwerte zum Tunen mit `tools/SpaceSim.Balance`.

## 1. Was Slots sind

Ein Schiff besteht aus einer **Klasse** (Rumpf, Größe, Basismasse) mit einer festen Liste von **Slots**. In jeden Slot passt genau ein **Modul** einer bestimmten Art. Die Module bestimmen alle Werte des Schiffs. Dadurch sind Rolle und Leistung eine Frage der Ausrüstung und nicht des Schiffstyps.

- **Art:** Was in den Slot passt (Reaktor, Antrieb, Schild, Panzerung, Waffe, Werkzeug, Fracht, Utility).
- **Größe:** S, M, L, XL (Kapitalschiffe). Ein Modul passt, wenn seine Art stimmt und seine Größe höchstens der Slotgröße entspricht.
- **Pflichtslots:** Reaktor und Antrieb. Ohne sie ist der Fit ungültig.
- **Stufe (Tier):** Mk1 bis Mk3 (später mehr). Höhere Stufen verlangen seltenere Erze.
- **Leistungsbilanz:** Die Summe des Energiebedarfs aller Module darf die Reaktorleistung nicht übersteigen. Sie wird beim Einbauen geprüft, nicht pro Tick simuliert (NPC-Schiffe haben außerhalb des Spielersektors ohnehin keine Physik).

### Slotarten

| Art | Zweck | Beispiele | Wirkung |
|---|---|---|---|
| `reactor` | Energie | Fusionsreaktor Mk1–3 | liefert Leistung |
| `engine` | Antrieb | Chemischer, Ionen-, Hybridantrieb | Schub, Treibstoffart und -verbrauch |
| `shield` | Schild | Schildgenerator | Kapazität, Regeneration |
| `armor` | Panzerung | Plattierung, Titanpanzer | Hülle, aber Masse |
| `weapon` | Waffen | Geschütz, Rakete (später) | Schaden, Energiebedarf |
| `tool` | Werkzeug | Mining-Laser I/II, Traktor, Spaltlaser, Bauausleger | Fähigkeit und Leistung |
| `cargo` | Fracht | Frachtmodul, Tank, Flüssigtank | Laderaum, Frachttyp, Treibstofftank |
| `utility` | Sonstiges | Sensor, **Sprungantrieb**, **Tarnfeld**, Lebenserhaltung | Sonderfunktionen |

Sprungantrieb und Tarnfeld sitzen bewusst im selben Slottyp `utility`: Wer beides will, braucht zwei Slots und genug Leistung. Das ist die Entscheidung, die Vielseitigkeit kostet (Abschnitt 6).

### Fähigkeiten aus Modulen

`Capability` (Flags) wächst auf: `Cargo`, `Mining`, `Combat`, `Construction`, `Research`, `Salvage`, **`Jump`**, **`Stealth`**. Ein Modul trägt sie über `grants` ein; das Schiff hat die Vereinigung aller.

## 2. Datenformat (JSON)

Fünf Abschnitte in `default-world.json`, die aufeinander verweisen: `slotTypes` und `modules` sind unabhängig, `shipClasses` verweisen darauf, `shipModels` auf Klassen.

```json
"slotTypes": [
  { "key": "reactor", "name": "Reaktor",   "required": 1 },
  { "key": "engine",  "name": "Antrieb",   "required": 1 },
  { "key": "shield",  "name": "Schild" },
  { "key": "tool",    "name": "Werkzeug" },
  { "key": "cargo",   "name": "Fracht" },
  { "key": "utility", "name": "Utility" }
],
"modules": [
  { "key": "reactor_mk1", "slot": "reactor", "size": "S", "tier": 1,
    "stats": { "power": 10 }, "mass": 4,
    "cost": { "steel": 4, "electronics": 2 }, "upkeep": 0.2 },
  { "key": "chem_engine_s", "slot": "engine", "size": "S", "tier": 1,
    "stats": { "thrust": 40, "fuelUse": 1.0 }, "powerDraw": 3, "mass": 3,
    "fuel": [ { "good": "hydrogen", "efficiency": 1.0 } ],
    "cost": { "steel": 3, "electronics": 1 } },
  { "key": "mining_laser_1", "slot": "tool", "size": "S", "tier": 1,
    "grants": ["Mining"], "stats": { "chunkChanceMult": 1.0, "chunkSizeMult": 1.0 },
    "powerDraw": 2, "mass": 1, "cost": { "electronics": 2, "copper": 2 } },
  { "key": "jump_drive_s", "slot": "utility", "size": "S", "tier": 1,
    "grants": ["Jump"], "stats": { "jumpRange": 1500, "chargeTicks": 120, "cooldownTicks": 600, "jumpFuel": 6 },
    "powerDraw": 6, "mass": 5, "cost": { "electronics": 4, "platinum": 2, "fuel_cell": 2 } },
  { "key": "cloak_s", "slot": "utility", "size": "S", "tier": 1,
    "grants": ["Stealth"], "stats": { "stealth": 1.0 }, "powerDraw": 5, "mass": 2,
    "cost": { "electronics": 3, "wafer": 4, "platinum": 1 } }
],
"shipClasses": [
  { "key": "skiff", "name": "Skiff", "size": "S", "baseHull": 60, "baseMass": 10,
    "slots": [ { "type": "reactor", "size": "S" }, { "type": "engine", "size": "S" },
               { "type": "tool", "size": "S" }, { "type": "cargo", "size": "S" },
               { "type": "utility", "size": "S" } ],
    "kit": "ship_kit_light" }
],
"shipModels": [
  { "key": "prospector", "class": "skiff", "name": "Prospector",
    "fit": ["reactor_mk1", "chem_engine_s", "mining_laser_1", "cargo_s", null],
    "extraCost": {} }
]
```

- **Slots der Klasse** stehen in fester Reihenfolge, `fit` füllt sie per Index. `null` ist ein leerer Slot.
- **Gespeichert** wird pro Schiff `ModelKey`, die **Slotliste** und `Fit`. Alles andere (Tempo, Schild, Fähigkeiten, Unterhalt) wird beim Laden aus Klasse und Fit berechnet (`ShipStats`, reiner Core-Code, gut testbar).
- **Slotliste pro Schiff:** Sie ist nicht nur die der Klasse, weil einzelne Schiffe Bonusslots oder größere Slots haben können (Abschnitt 8). Ohne Abweichung entspricht sie der Klasse.
- **Prüfungen:** `WorldBuilder` validiert unbekannte Keys, falsche Slotart, zu große Module und die Leistungsbilanz und wirft `WorldFormatException` mit dem Namen des Modells. `ContentTests` stellen sicher, dass jedes Modell baubar ist, also seine Kosten aus der Erzkette herstellbar sind.
- **Save:** `Fit` und Slotliste gehören in `SaveGame` (v4), `UniverseSerializer` und `UniverseHash`.

### Abgeleitete Werte

| Wert | Berechnung |
|---|---|
| Leistungsbilanz | Summe `power` der Reaktoren ≥ Summe `powerDraw`; sonst Fit ungültig |
| Tempo | Schub ÷ Gesamtmasse (Klasse + Module + Fracht), begrenzt |
| Hülle | `baseHull` + Panzerung |
| Schild | Kapazität und Regeneration des Schildmoduls |
| Laderaum | Summe der Frachtmodule |
| Spritverbrauch | `fuelUse` × Masse × Strecke ÷ `efficiency` (ersetzt die pauschale Abbuchung in `Travel.Start`) |
| Laserleistung | `chunkChanceMult`, `chunkSizeMult`: stärkerer Laser = bessere Ertragschance und mehr Erz |
| Unterhalt | Summe `upkeep` der Module; höhere Stufen kosten mehr (`UpkeepSystem`) |
| Fähigkeiten | Vereinigung aller `grants` |

## 3. Treibstoff (entschieden)

Treibstoff wird eine echte Ware mit eigenem Tank, **für alle Schiffe** (Spieler und NPCs, gleiche Regeln). NPCs tanken beim Reiseantritt automatisch an der besten erreichbaren Station mit passendem Treibstoff. Die Routenbewertung bezieht Treibstoffkosten und -verfügbarkeit mit ein.

| Treibstoff | Quelle (Ware) | Antrieb | Reichweite je Einheit | Charakter |
|---|---|---|---|---|
| **Wasserstoff** | `hydrogen` | Chemisch | niedrig | billig, überall, Standard für Skiffs |
| **Wasser** | `water` | Ionenantrieb | mittel | sehr billig, geringer Schub |
| **Brennstoffzellen** | `fuel_cell` | Elektrisch/Hybrid | hoch | teuer, kompakt; für Kurier und schnelle Schiffe |
| **Fusionsgas** (später) | neue Ware aus Wasser/Eis | Fusion | sehr hoch | Fregatten, Schlachtschiffe, schwere Frachter; braucht eine Anreicherungsstation |
| **Energie** | `energy` | Solarsegel | unbegrenzt, schwach | nur Mini-Schiffe |

- Der Antrieb nennt seine zulässigen Treibstoffe mit `efficiency`; ein Mehrstoffantrieb nutzt zuerst, was an Bord ist.
- Das Schiff führt `Fuel` als Menge je Treibstoffware, **getrennt vom Laderaum**; die Tankgröße kommt aus einem Tankmodul oder der Klasse.
- **Start:** drei Arten (Wasserstoff, Wasser, Brennstoffzellen) plus Energie für die Minis. Fusionsgas kommt mit der Kapitalklasse.
- **Gate-Sprünge sind treibstofffrei**, nur die Strecke im Sektor kostet Treibstoff. Sonst bleiben Schiffe in Sektoren ohne Tankstelle hängen.
- **Liegenbleiber:** Notlösungen sind Rückflug auf Reserve, Schleppdienst und Notkauf zum Aufpreis. Das ist die Henne-Ei-Logik wie beim Notbau.
- **Wirtschaft:** neue Nachfrage nach Wasserstoff, Wasser und Brennstoffzellen aus der Flotte; teurer Treibstoff lenkt die KI zu sparsameren Antrieben.

## 4. Sprungantrieb

Der Sprungantrieb ist ein Utility-Modul (Fähigkeit `Jump`), mit dem ein Schiff **ohne Gate** in einen anderen Sektor springt.

### Regeln

- **Reichweite:** Sprung in Sektoren innerhalb von `jumpRange` auf der Sternenkarte. `Sector.MapPosition` existiert schon (heute nur Darstellung) und liefert die Entfernung. Wenn dieser Abstand zu ungenau ist, wären Gate-Hops die Alternative.
- **Aufladen:** `chargeTicks` ohne Bewegung, dann der Sprung; danach `cooldownTicks`.
- **Treibstoff:** `jumpFuel` je Sprung, bevorzugt Brennstoffzellen oder Fusionsgas. Das macht Sprünge teuer und die Treibstoffkette strategisch.
- **Massenschatten:** Kein Sprung in der Nähe großer Massen (Stationen, Gates, große Asteroiden). Das Schiff muss erst aus der Zone fliegen. Das nutzt die vorhandenen Hindernisse (`Obstacle`).
- **Ankunft:** An einer zufälligen sicheren Position am Sektorrand oder bei einem Zielpunkt, deterministisch über `Universe.Rng`, mit Schonfrist (`Grace`) nach dem Sprung.
- **Sprungsperre:** Fraktionsstationen und Gates können ein Sperrfeld haben, das Ankunft und Abflug im Umkreis verhindert. Das ist die Verteidigung gegen Sprungantriebe.

### Auswirkung auf die Architektur

Heute kennt `Universe.FindRoute` nur Gates (BFS über offene Gates) und zwischenspeichert nur nach (von, nach). Mit Sprung entstehen zusätzliche Kanten, die vom Schiff abhängen. Daraus folgt:

1. **Mobilitätsprofil:** Die Routensuche bekommt ein Profil (Gate-only, Sprung mit Reichweite R und Cooldown-Status). Der Cache wird nach (von, nach, Profil) aufgeteilt.
2. **Neuer Abschnitt:** `Travel.Plan` kennt neben der Gate-Etappe (`Leg`) eine `JumpLeg` mit Aufladezeit und Dauer.
3. **Keine Physik außerhalb:** NPC-Sprünge sind Fahrplanabschnitte wie bisher; nur im Spielersektor sieht man Aufladen und Ankunft.
4. **Preis:** Sprungantrieb ist teuer (Platin, Brennstoffzellen), schwer und braucht viel Leistung; deshalb haben ihn nur wenige Schiffe.

### Zweck im Spiel

- Umgehung geschlossener Gates (Abschnitt 5) und von Sperrzonen.
- Schnelle Verbindungen für Kuriere, Beute und Schmuggel.
- Eigene Rolle: Sprungschiffe können abgeschottete Märkte beliefern und Preisunterschiede ausnutzen; das stabilisiert die Wirtschaft, wenn Gates schließen.

## 5. Geschlossene Jumpgates

Heute hat `Gate` nur `Open` (Standard `true`); `FindRoute` ignoriert geschlossene Gates, aber nichts schaltet sie. Das Fraktionsverhalten ist noch nicht gebaut. Geplant ist ein Zugangsmodell statt eines Schalters:

| Zustand | Bedeutung | Wer löst aus |
|---|---|---|
| `Open` | frei | Standard |
| `Closed` | für alle gesperrt (Wartung, Quarantäne, Blockade) | Fraktion, Ereignis |
| `Restricted` | je Fraktion: erlaubt, Zoll oder gesperrt | Fraktionspolitik |
| `Destroyed` | dauerhaft kaputt, Wiederaufbau nötig | Kampf, später Bauschiff |

- **Zugangsregel pro Fraktion:** `GateAccess` mit Besitzerfraktion und Politik je andere Fraktion (`Allow`, `Toll`, `Deny`). `Toll` verlangt einen Betrag (Geldsenke, Einnahme der Besitzerfraktion).
- **Auslöser:** Ein `FactionPolicySystem` (Teil des Fraktionsverhaltens) ändert die Politik nach Beziehungen: Krieg schließt, Frieden öffnet. Entscheidungen haben Hysterese und Mindestdauer, damit Gates nicht flackern.
- **Schiffe unterwegs:** Eine Reise über ein Gate, das sich schließt, wird beim nächsten Nachdenken neu geplant. Schiffe, die den Sektor nicht mehr verlassen können, werden untätig und warten.
- **Sektorabschottung:** Wenn die Weltkarte durch geschlossene Gates zerfällt, driften Preise auseinander, und Stationen ohne Nachschub gehen pleite. Gegenmaßnahmen:
  - eine Hauptroute je Region bleibt immer offen (oder Gates öffnen sich nach einer Frist wieder);
  - die Wirtschaftsstatistik zeigt isolierte Sektoren an, und das Balance-Tool schlägt bei Aufspaltung Alarm;
  - Sprungschiffe und Blockadebrecher füllen die Lücke und verdienen am Preisunterschied.
- **Architektur:** `Gate.Open` wird durch einen Zugriff `gate.CanPass(ship, universe)` ersetzt. `FindRoute` bekommt dafür das Mobilitätsprofil und die Fraktion des Schiffs. Save: `GateSave` erhält Zustand und Politik, plus Migration (alle bisher `Open`).
- **Blockadebrecher** (später): Fähigkeit, Sperren mit Panzerung, Zollzahlung oder Tarnfeld zu passieren; mit Sprungantrieb gar nicht erst mit dem Gate.

## 6. Tarnfeld

Ein Utility-Modul mit der Fähigkeit `Stealth`. Es senkt die Wahrscheinlichkeit, von Fraktionen entdeckt zu werden. Es ist ein Gegenpol zu Sprungsperre und Gate-Kontrolle, nicht Unsichtbarkeit.

- **Entdeckungswurf:** An Kontrollpunkten (Gate-Passage, Sprung-Ankunft, Stationsscanner, später Patrouillen) vergleicht das System die Tarnstärke des Schiffs mit der Sensorstärke des Kontrollpunkts und würfelt über `Universe.Rng`. Das Ergebnis: unentdeckt (Schiff passiert), entdeckt (Zoll, Fracht beschlagnahmt, Abweisung, später Kampf).
- **Kosten:** hoher Leistungsbedarf; nicht gleichzeitig mit Waffen und Mining-Laser nutzbar (Schüsse brechen die Tarnung). Wer tarnen will, opfert Feuerkraft. Sprung-Ankunft ist durch Sensoren erkennbar (Sprungspur).
- **Stufen:** Mk1–Mk3 mit steigender Tarnstärke; höhere Stufen brauchen mehr Platin und Silizium (Wafer).
- **Zweck:** Schmuggel, Spionage und Blockadebrecher; für den Spieler eine Spielweise (Händler, der Zölle umgeht, Schürfer in fremden Sektoren).
- **Nicht-Ziel:** Kein echtes „unsichtbar für Kampf-KI“ in diesem Schritt, weil es das Kampfsystem noch nicht gibt.
- **Architektur:** Eine Schnittstelle `IDetectionCheck` (ein Eintrag je Kontrollpunkt), die `Stealth` und Sensorstärke vergleicht. Heute nutzt nur das Gate-Zugangsmodell sie.

## 7. Stationsdienste als Zwischenschritt

Die Dienste kommen **vor** den Slots, weil der Ausrüster einer ist und die Schnittstelle sonst fehlt.

```text
IStationService
  Key                                       "trade", "repair", "refuel", "outfitter", "shipdealer", "upgrade"
  IsAvailable(Universe, Station, Ship)      läuft hier / kann das Schiff das nutzen?
  Offers(Universe, Station, Ship)           Liste von Angeboten (Text, Preis, Menge)
  Execute(Universe, Station, Ship, Offer)   Ergebnis, Event `ServicePerformed`
```

- **Registrierung wie bei Systemen:** Ein Modul trägt Dienste in eine `StationServices`-Registry ein. Das Godot-Dock-Panel fragt nur `Offers` ab und zeichnet generische Zeilen und Buttons, ohne dienstspezifischen Code.
- **In der JSON:** `"services": ["trade", "repair"]` pro Stationstyp, später mit Parametern. Der heutige Markt wird selbst der Dienst `trade`.

| Dienst | Was er tut | Wirtschaftlicher Bezug |
|---|---|---|
| `trade` | Waren kaufen und verkaufen | wie heute (`TradeActions`) |
| `repair` | Hülle gegen Credits und **Hüllenplatten aus dem Stationslager** | verbraucht Platten, Station zahlt aus Bestand |
| `refuel` | Tank gegen Treibstoffware | Treibstoff wird Ware (Abschnitt 3) |
| `outfitter` | Module ein- und ausbauen | Module sind Waren aus dem Stationslager |
| `upgrade` | Module verbessern (Abschnitt 8) | Materialien, Wahrscheinlichkeit |
| `shipdealer` | Schiff kaufen (`BuyShip`) | Bausatz und Zusatzmaterial aus dem Lager |

Heute repariert das Andocken die Hülle kostenlos, und Treibstoff ist nur ein Credit-Abzug. Beides werden mit den Diensten echte Kosten und Nachfrage.

## 8. NPC-Variation und Upgrades (Wahrscheinlichkeiten)

### Bessere Ausstattung bei NPC-Schiffen

Neue NPC-Schiffe starten nicht alle gleich. Beim Kauf wird einmal gewürfelt (`Universe.Rng`, deterministisch):

| Güteklasse | Chance (Start) | Wirkung |
|---|---|---|
| Standard | 70 % | Fit des Modells |
| Verbessert | 20 % | ein Modul um eine Stufe besser |
| Elite | 8 % | zwei bis drei Module besser oder ein Slot eine Größe größer |
| Meisterwerk | 2 % | zusätzlich ein Bonusslot (höchstens `maxBonusSlots` der Klasse) |

- **Gütegewinn kostet Material:** Die Werft braucht die besseren Module im eigenen Lager. Fehlen sie, fällt der Wurf auf die nächste Stufe zurück. So gibt es keinen Gratisgewinn, und die Seltenheit der Erze begrenzt die Elite.
- **Einfluss:** Die Chancen verschieben sich mit der Werfttechnik (Werfttyp, später Forschung der Fraktion) und mit dem Reichtum der Fraktion. Startwelt-Schiffe („Gründerschiffe“) würfeln ohne Materialbedarf.
- **Speichern:** Nur der Fit und die Slotliste werden gespeichert, nicht die Chancen.

### Upgrades

Bestehende Schiffe verbessern ihre Module.

- **Spieler:** am Dienst `upgrade`. Pro Versuch werden Credits und Material verbraucht. Die Erfolgschance hängt von der Stufe ab:

| Stufensprung | Erfolgschance (Start) |
|---|---|
| Mk1 → Mk2 | 90 % |
| Mk2 → Mk3 | 60 % |
| Mk3 → Mk4 | 30 % |

  Bei Misserfolg bleibt das alte Modul, das Material ist verbraucht. Jeder Fehlschlag am selben Slot erhöht die nächste Chance um 10 Prozentpunkte (Mitleidszähler), damit niemand ewig scheitert. Ein Slot-Upgrade (Größe oder Bonusslot) ist deutlich seltener (zum Beispiel 15 %) und hat ein Limit je Klasse.
- **NPCs:** Ein `UpgradeSystem` lässt reiche, untätige Schiffe an Stationen mit `upgrade`-Dienst Upgrades versuchen. Sie wählen das mit dem höchsten erwarteten Gewinn pro Kosten und würfeln wie der Spieler. Auch hier kommen Material und Credits aus dem Marktlager der Station, damit Upgrades Nachfrage erzeugen.
- **Determinismus:** Alle Würfe laufen über `Universe.Rng`, deren Zustand gespeichert wird. Wer vor einem Upgrade lädt, bekommt dasselbe Ergebnis. Das schließt Save-Scumming aus und ist bewusst.

### Risiken

- **Balance:** Elite-Schiffe und Upgrades können die Wirtschaft verschieben. Das Balance-Tool bekommt einen Zähler für Güteverteilung und Upgrade-Quote.
- **Zu viele Kombinationen:** wenige Stufen und Slotarten zuerst, mehr als Datenerweiterung.
- **Flottengrenzen:** `MaxTraders`/`MaxMiners` zählen Kapazität, nicht Stückzahl (siehe SCHIFFE.md).

## 9. Reihenfolge

1. **Dienste-Rahmen** (`IStationService`, Registry, generisches Dock-Panel) mit `trade` als erstem Dienst. Keine Verhaltensänderung.
2. **`repair`** (Hüllenplatten), dann **`refuel`** mit `Ship.Fuel` für alle Schiffe (Save-Änderung).
3. **Slot-Format:** Content-Abschnitte, `WorldBuilder`-Prüfung, `ShipStats`, Save v4 mit `ModelKey`, Slotliste und `Fit`. Zuerst Einheitsmodelle, die das heutige Verhalten nachbilden.
4. **Module als Waren** und **`outfitter`**, danach `shipdealer` und Startpakete.
5. **NPC-Güteklassen und `upgrade`-Dienst** mit `UpgradeSystem`.
6. **Gate-Zugangsmodell** und Mobilitätsprofil in der Routensuche (Voraussetzung für Sprungantrieb und Sperren), danach **Sprungantrieb**.
7. **Tarnfeld** mit `IDetectionCheck`, erst wenn Gate-Kontrolle und Fraktionsverhalten stehen.

## 10. Noch offen

- Reichweite des Sprungantriebs über `Sector.MapPosition` oder über Gate-Hops?
- Wer darf Gate-Zugangsregeln ändern: nur Fraktionspolitik oder auch Ereignisse (Zerstörung, Wartung)?
- Soll es Sprungsperren als eigene Station oder Station-Dienst geben?
- Tarnung: Gilt sie auch für Handel (Stationen verweigern Tarnschiffe)?
- Zahlen: Güteklassenchancen, Upgrade-Chancen, Mitleidszähler, Sprungkosten.
