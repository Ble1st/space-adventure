# Schiffsslots, Treibstoff, Sprungantrieb, Tarnfeld, Dienste (Planung; Sprungantrieb und Tarnfeld umgesetzt)

**Status: reine Planung, nichts davon ist umgesetzt.** Ergänzt [SCHIFFE.md](SCHIFFE.md) (Klassen, Modelle, Multirole). Alle Zahlen sind Startwerte zum Tunen mit `tools/SpaceSim.Balance`.

**Zurückgestellt (nur Referenz und Planung, nicht im ersten Modulsatz):** Sprungantrieb (Abschnitt 4), Tarnfeld (Abschnitt 6), Besatzung und Lebenserhaltung für Schiffe (Abschnitt 11). Die Kolonie verbraucht Lebenserhaltung weiter, Schiffe tun es vorerst nicht.

## 1. Was Slots sind

Ein Schiff besteht aus einer **Klasse** (Rumpf, Größe, Basismasse) mit einer festen Liste von **Slots**. In jeden Slot passt genau ein **Modul** einer bestimmten Art. Die Module bestimmen alle Werte des Schiffs. Dadurch sind Rolle und Leistung eine Frage der Ausrüstung und nicht des Schiffstyps.

- **Art:** Was in den Slot passt (Reaktor, Antrieb, Schild, Panzerung, Waffe, Werkzeug, Fracht, Utility).
- **Größe:** S, M, L, XL (Kapitalschiffe). Ein Modul passt, wenn seine Art stimmt und seine Größe höchstens der Slotgröße entspricht.
- **Pflichtslots:** Reaktor und Antrieb. Ohne sie ist der Fit ungültig.
- **Stufe (Tier):** Mk1 bis Mk3 (später mehr). Höhere Stufen verlangen seltenere Erze.
- **Leistungsbilanz:** Die Dauerlast aller Module (`powerDraw`) darf die Reaktorleistung (`powerOutput`) nicht übersteigen. Kurzzeitige Spitzen (`peakDraw`, zum Beispiel Waffen) darf eine **Speicherbank** abdecken. Die Prüfung findet beim Einbauen statt, nicht pro Tick (NPC-Schiffe haben außerhalb des Spielersektors ohnehin keine Physik).

### Leistung und Energie (entschieden)

Zwei Begriffe, die nicht vermischt werden:

| Begriff | Schlüssel | Wo | Bedeutung |
|---|---|---|---|
| **Leistung** | `power` (`powerOutput`, `powerDraw`, `peakDraw`) | **Schiffe** | was der Reaktor bereitstellt und die Module ziehen; Einheit „Leistung“ |
| **Energie** | `energy` | **Stationen und Produktion** | die Ware, die Kraftwerke herstellen und Fabriken verbrauchen |

Energie (die Ware) ist weder Schiffstreibstoff noch Schiffsleistung. Reaktoren stellen Leistung bereit, ohne die Ware `energy` oder Treibstoff zu verbrauchen.

**Speicherbank (neues Modul, Slotart `storage`):** Sie puffert Leistung. Sie hat eine Kapazität (`powerStorage`) und lädt sich aus dem Überschuss des Reaktors auf. Module mit Spitzenlast (Waffen, später Sprungantrieb und Tarnfeld) dürfen die Dauerleistung kurzzeitig überschreiten, solange die Bank reicht. Für NPC-Schiffe genügt die statische Prüfung beim Einbau (`Spitzenlast − Reaktorüberschuss` über Dauer ≤ Kapazität). Eine dynamische Ladung gibt es höchstens in der Ansicht des Spielers.

### Slotarten

| Art | Zweck | Beispiele | Wirkung |
|---|---|---|---|
| `reactor` | Leistung | Fusionsreaktor Mk1–3 | liefert Dauerleistung (`powerOutput`) |
| `storage` | Speicher | Speicherbank Mk1–3 | puffert Leistung (`powerStorage`), deckt Spitzen |
| `engine` | Antrieb | Chemischer, Ionen-, Hybridantrieb | Schub, Treibstoffart und -verbrauch |
| `shield` | Schild | Schildgenerator | Kapazität, Regeneration |
| `armor` | Panzerung | Plattierung, Titanpanzer | Hülle, aber Masse |
| `weapon` | Waffen | Geschütz, Rakete (später) | Schaden, Leistungsbedarf |
| `tool` | Werkzeug | Mining-Laser I/II, Traktor, Spaltlaser, Bauausleger | Fähigkeit und Leistung |
| `cargo` | Fracht | Frachtmodul, Tank, Flüssigtank | Laderaum, Frachttyp, Treibstofftank |
| `utility` | Sonstiges | Sensor; später Sprungantrieb, Tarnfeld | Sonderfunktionen |

Sprungantrieb und Tarnfeld (beide zurückgestellt) sitzen bewusst im selben Slottyp `utility`: Wer beides will, braucht zwei Slots und genug Leistung. Das ist die Entscheidung, die Vielseitigkeit kostet (Abschnitt 6).

### Fähigkeiten aus Modulen

`Capability` (Flags) wächst auf: `Cargo`, `Mining`, `Combat`, `Construction`, `Salvage`, **`Jump`**, **`Stealth`** (`Research` ist als Schiffsfähigkeit gestrichen, Forschung gehört dem Akteur). Ein Modul trägt sie über `grants` ein; das Schiff hat die Vereinigung aller.

## 2. Datenformat (JSON)

Fünf Abschnitte in `default-world.json`, die aufeinander verweisen: `slotTypes` und `modules` sind unabhängig, `shipClasses` verweisen darauf, `shipModels` auf Klassen.

```json
"slotTypes": [
  { "key": "reactor", "name": "Reaktor",   "required": 1 },
  { "key": "storage", "name": "Speicher" },
  { "key": "engine",  "name": "Antrieb",   "required": 1 },
  { "key": "shield",  "name": "Schild" },
  { "key": "tool",    "name": "Werkzeug" },
  { "key": "cargo",   "name": "Fracht" },
  { "key": "utility", "name": "Utility" }
],
"modules": [
  { "key": "reactor_mk1", "slot": "reactor", "size": "S", "tier": 1,
    "stats": { "powerOutput": 10 }, "mass": 4,
    "cost": { "steel": 4, "electronics": 2 }, "upkeep": 0.2 },
  { "key": "chem_engine_s", "slot": "engine", "size": "S", "tier": 1,
    "stats": { "thrust": 40, "fuelUse": 1.0 }, "powerDraw": 3, "mass": 3,
    "fuel": [ { "good": "propellant", "efficiency": 1.0 } ],
    "cost": { "steel": 3, "electronics": 1 } },
  { "key": "mining_laser_1", "slot": "tool", "size": "S", "tier": 1,
    "grants": ["Mining"], "stats": { "chunkChanceMult": 1.0, "chunkSizeMult": 1.0 },
    "powerDraw": 2, "mass": 1, "cost": { "electronics": 2, "copper": 2 } },
  { "key": "power_bank_s", "slot": "storage", "size": "S", "tier": 1,
    "stats": { "powerStorage": 20 }, "mass": 2,
    "cost": { "steel": 2, "electronics": 2, "copper": 2 } },
  { "key": "cargo_s", "slot": "cargo", "size": "S", "tier": 1,
    "holds": "solid", "stats": { "capacity": 30 }, "mass": 2, "cost": { "steel": 2 } },
  { "key": "canister_bay_s", "slot": "cargo", "size": "S", "tier": 1,
    "holds": "liquid", "stats": { "capacity": 30 }, "mass": 3, "cost": { "steel": 2, "titanium": 1 } },
  { "key": "universal_bay_s", "slot": "cargo", "size": "S", "tier": 1,
    "holds": "any", "stats": { "capacity": 20 }, "mass": 3, "cost": { "steel": 3, "titanium": 1 } },
  // Zurückgestellt (nur Referenz): Sprungantrieb und Tarnfeld stehen nicht im ersten Modulsatz.
  { "key": "jump_drive_s", "slot": "utility", "size": "S", "tier": 1,
    "grants": ["Jump"], "stats": { "jumpRange": 1500, "chargeTicks": 120, "cooldownTicks": 600, "jumpFuel": 6 },
    "powerDraw": 6, "mass": 5, "cost": { "electronics": 4, "platinum": 2, "fuel_cells": 2 } },
  { "key": "cloak_s", "slot": "utility", "size": "S", "tier": 1,
    "grants": ["Stealth"], "stats": { "stealth": 1.0 }, "powerDraw": 5, "mass": 2,
    "cost": { "electronics": 3, "wafers": 4, "platinum": 1 } }
],
"shipClasses": [
  { "key": "skiff", "name": "Skiff", "size": "S", "baseHull": 60, "baseMass": 10,
    "slots": [ { "type": "reactor", "size": "S" }, { "type": "engine", "size": "S" },
               { "type": "tool", "size": "S", "facing": "bow", "arc": 40 },
               { "type": "cargo", "size": "S" }, { "type": "utility", "size": "S" } ],
    "kit": "ship_kit_light" }
],
"shipModels": [
  { "key": "prospector", "class": "skiff", "name": "Prospector",
    "fit": ["reactor_mk1", "chem_engine_s", "mining_laser_1", "cargo_s", null],
    "extraCost": {} }
]
```

- **Slots der Klasse** stehen in fester Reihenfolge, `fit` füllt sie per Index. `null` ist ein leerer Slot.
- **Feuerwinkel (entschieden):** Waffen- und Werkzeug-Slots (`weapon`, `tool`) haben eine Ausrichtung `facing` (`bow` Bug, `port` Backbord, `starboard` Steuerbord, `stern` Heck) und einen halben Öffnungswinkel `arc` in Grad. Ein Modul darf den Winkel einengen (`arcMax`). Ein Ziel ist **im Feuerwinkel**, wenn der Winkel zwischen Schiffsausrichtung plus `facing` und Zielrichtung höchstens `arc` beträgt. Die Oberfläche zeigt je Schacht eine Taste mit Winkelsymbol und Zustand (im Winkel, außerhalb mit Drehhinweis, lädt nach, kein Ziel); „Alle Feuer“ löst nur bereite Waffen im Winkel aus (siehe [UI.md](UI.md)). Die Winkelprüfung ist reine Geometrie im Core (`FireArc.Contains`) und wird für NPC-Schüsse mit derselben Regel verwendet.
- **Gespeichert** wird pro Schiff `ModelKey`, die **Slotliste** und `Fit`. Alles andere (Tempo, Schild, Fähigkeiten, Unterhalt) wird beim Laden aus Klasse und Fit berechnet (`ShipStats`, reiner Core-Code, gut testbar).
- **Slotliste pro Schiff:** Sie ist nicht nur die der Klasse, weil einzelne Schiffe Bonusslots oder größere Slots haben können (Abschnitt 8). Ohne Abweichung entspricht sie der Klasse.
- **Prüfungen:** `WorldBuilder` validiert unbekannte Keys, falsche Slotart, zu große Module und die Leistungsbilanz und wirft `WorldFormatException` mit dem Namen des Modells. `ContentTests` stellen sicher, dass jedes Modell baubar ist, also seine Kosten aus der Erzkette herstellbar sind.
- **Save:** `Fit` und Slotliste gehören in `SaveGame` (v4), `UniverseSerializer` und `UniverseHash`.

### Abgeleitete Werte

| Wert | Berechnung |
|---|---|
| Leistungsbilanz | Summe `powerOutput` ≥ Summe `powerDraw`; Spitzenlast nur mit ausreichender Speicherbank (`powerStorage`); sonst Fit ungültig |
| Tempo | Schub ÷ Gesamtmasse (Klasse + Module + Fracht), begrenzt |
| Hülle | `baseHull` + Panzerung |
| Schild | Kapazität und Regeneration des Schildmoduls |
| Laderaum | Summe der Frachtmodule |
| Spritverbrauch | `fuelUse` × Masse × Strecke ÷ `efficiency` (ersetzt die pauschale Abbuchung in `Travel.Start`) |
| Laserleistung | `chunkChanceMult`, `chunkSizeMult`: stärkerer Laser = bessere Ertragschance und mehr Erz |
| Unterhalt | Summe `upkeep` der Module; höhere Stufen kosten mehr (`UpkeepSystem`) |
| Fähigkeiten | Vereinigung aller `grants` |

### Warenform und Kanister (entschieden)

Jede Ware in `goods` bekommt eine **Form** (`form`): `solid` (fest) oder `liquid` (Flüssigstoff). Gase werden verdichtet und zählen als `liquid`.

| Form | Waren (Start) |
|---|---|
| `liquid` | `water`, `hydrogen`, `propellant`; später `ion_gas`, `fusion_fuel` |
| `solid` | alle übrigen (Erze, Eis, Metalle, Komponenten, Güter, Bausätze, Module) |

- **Flüssigstoffe reisen in Kanistern:** Ein Frachtmodul hat `holds`: `solid`, `liquid` oder `any`. Das **Standardfrachtmodul** fasst nur feste Waren, der **Kanisterraum** (`canister_bay`) nur Flüssigstoffe, der **Universalraum** beides, aber mit geringerer Kapazität (etwa 70 %). Kanister sind Teil des Moduls, keine eigene Ware (ein Pfandsystem mit Leerkanistern wäre zu aufwendig).
- **Der Treibstofftank** (Antriebsgruppe) ist vom Laderaum getrennt. Treibstoff als Handelsware (`propellant`) dagegen liegt im Kanisterraum.
- **Tanker:** eine Klasse mit viel Kanisterkapazität, zum Beispiel für Wasser, Wasserstoff und Treibstoff.
- **Migration:** Bestehende Schiffe bekommen ein Universalraum-Modul mit 100 % Kapazität, damit sich beim Laden nichts ändert. Neue Modelle nutzen die getrennten Räume.
- **Gefahrgut** (`hazard`) ist ein späterer, eigener Marker und noch nicht Teil dieser Entscheidung.
- **Content-Test:** Jede Ware hat eine Form, und jedes Modell kann seine Ware tragen oder weiß, dass es sie nicht kann.

### Module sind Waren, drei Modulfabriken (entschieden)

Ein **Slot** ist der Platz am Schiff, ein **Modul** (Schiffsbauteil) steckt darin, zum Beispiel Laser oder Triebwerk. Jedes Modul bekommt automatisch eine **Ware** gleichen Schlüssels (mit Volumen) in `goods`. Das `cost`-Feld des Moduls wird das **Rezept** der Fabrik. Das Lager einer Station verkauft Module, der Ausrüster baut sie ein.

Statt einer Fabrik für alles oder einer Fabrik je Bauteil gibt es **drei Fabriken nach Fachgebiet**:

| Fabrik | Stellt her | Zutaten (Schwerpunkt) |
|---|---|---|
| **Antriebs- und Reaktorwerk** | Reaktoren, Speicherbänke, Triebwerke, Treibstofftanks, Frachtmodule | Stahl, Titan, Brennstoffzellen |
| **Waffen- und Schildwerk** | Geschütze, Schilde, Panzerung | Platin, Wafer, Titan, Hüllenplatten |
| **Systemwerk** | Laser, Traktor, Sensoren, Automatisierung; später Sprungantrieb und Tarnfeld | Elektronik, Wafer |

- **Stufe nach Stationsgröße:** Eine kleine Fabrik stellt Mk1 her, eine mittlere Mk2, ein Hub Mk3. Das spart Typen und macht Größe zu einer Entscheidung.
- **Warum nicht eine einzige Fabrik:** Sie wäre ein Totalausfall-Punkt, bräuchte alle Zutaten zugleich und würde die Ketten nivellieren.
- **Warum nicht eine pro Bauteil:** 20 und mehr Stationstypen wären zu viel Pflege und würden die Wirtschaft zerstückeln.
- **Erster Modulsatz:** Reaktor, Triebwerke, Tank, Laser, Traktor, Frachtmodul, Schild, Panzerung. Sprungantrieb und Tarnfeld kamen später ins Systemwerk (Abschnitte 4 und 6).

## 3. Treibstoff (entschieden)

Treibstoff ist eine eigene Ware mit eigenem Tank, **für alle Schiffe** (Spieler und NPCs, gleiche Regeln). **Wasserstoff, Wasser, Energie und Brennstoffzellen sind kein Schiffstreibstoff.** Wasserstoff ist nur Rohstoff für die Treibstoffherstellung; Brennstoffzellen bleiben ein Bauteil für Bausätze und Module. NPCs tanken beim Reiseantritt automatisch an der besten erreichbaren Station mit passendem Treibstoff. Die Routenbewertung bezieht Treibstoffkosten und -verfügbarkeit mit ein.

| Treibstoff | Herkunft | Antrieb | Eigenschaft |
|---|---|---|---|
| **Treibstoff** (`propellant`) | Treibstoffraffinerie: Wasserstoff + Energie → Treibstoff | chemisch | billig, viel Verbrauch, Standard für Skiffs und Frachter |
| **Ionengas** (`ion_gas`) | Gasernte-Plattform (Edelgas aus Nebel oder Gasriesen), Gasraffinerie | Ionenantrieb | sparsam, geringer Schub, für lange Strecken |
| **Fusionsbrennstoff** (`fusion_fuel`) | Anreicherung: Eis + Energie + Platin als Katalysator | Fusion | sehr hohe Reichweite, nur große Schiffe |

Neue Stationen dafür (Startwerte, siehe STATIONEN.md):

| Station | Rezept (Start) |
|---|---|
| Treibstoffraffinerie | 3 Wasserstoff + 1 Energie → 4 Treibstoff |
| Gasernte-Plattform | liefert die neue Rohware `gas` |
| Gasraffinerie | 2 Gas + 1 Energie → 1 Ionengas |
| Anreicherungsanlage | 4 Eis + 2 Energie + 1 Platin → 1 Fusionsbrennstoff |

- Der Antrieb nennt seine zulässigen Treibstoffe mit `efficiency`; ein Mehrstoffantrieb nutzt zuerst, was an Bord ist.
- Das Schiff führt `Fuel` als Menge je Treibstoffware, **getrennt vom Laderaum**; die Tankgröße kommt aus einem Tankmodul oder der Klasse.
- **Reaktoren verbrauchen keinen Treibstoff**, sie haben nur eine Leistungsbilanz (hält das System klein). Ein Betriebsstoff für Kapitalreaktoren ist später möglich.
- **Start:** `propellant` allein reicht für die ersten Modelle; Ionengas und Fusionsbrennstoff kommen mit der Gasernte und der Kapitalklasse.
- **Gate-Sprünge sind treibstofffrei**, nur die Strecke im Sektor kostet Treibstoff. Sonst bleiben Schiffe in Sektoren ohne Tankstelle hängen.
- **Liegenbleiber:** Notlösungen sind Rückflug auf Reserve, Schleppdienst und Notkauf zum Aufpreis (Henne-Ei-Logik wie beim Notbau).
- **Wirtschaft:** Wasserstoff konkurriert weiter mit dem Kraftwerk, aber nur über die Verarbeitungsstufe, was die Nachfrage dämpft. Trotzdem vor der Umsetzung mit `tools/SpaceSim.Balance` prüfen, ob die Energieversorgung der Startwelt leidet.

## 4. Sprungantrieb (umgesetzt)

Der Sprungantrieb ist ein Utility-Modul (Fähigkeit `Jump`), mit dem ein Schiff **ohne Gate** in einen anderen Sektor springt. Umgesetzt in `SpaceSim.Core/Flight/JumpDrive.cs` (Regel), `Simulation/JumpCloakSystem.cs` (je Tick im Kampfmodul), Ansicht in `godot/scripts/Main.JumpCloak.cs`; Tests `tests/SpaceSim.Tests/SprungTarnTests.cs`, Selbsttest-Stufe 130.

### Entscheidungen

- **Reichweite in Gate-Hops, nicht auf der Sternenkarte.** Gezählt wird über das ganze Tornetz, **auch über geschlossene, gesperrte und zerstörte Tore** (der Antrieb braucht das Tor nicht, nur die Nachbarschaft). `Sector.MapPosition` bleibt reine Darstellung (kein Hash, keine Spielstandsprüfung). Grund: robust, ohne neue Spieldaten, und die Karte bleibt frei gestaltbar. Modul `jump_drive_s` „Sprungantrieb“: Reichweite 2 Hops.
- **Aufladen** `jumpCharge` (60 Ticks = 15 s) ohne Bewegung (höchstens `jumpMoveTolerance` 4 m vom Ladepunkt), dann der Sprung; danach **Abklingzeit** `jumpCooldown` (1200 Ticks = 5 min). Laden kostet `jumpDraw` (2) Leistung aus dem Reaktorüberschuss (die Dauerlast steigt, Schild/Bank/Antrieb bekommen weniger); ohne genug Überschuss lässt sich nicht laden.
- **Treibstoff nur mit Schalter:** `jumpFuel` (20) wird nur bei `fuelActive` verbraucht; in der Standardwelt (Treibstoff aus) kostet der Sprung nur Zeit und Leistung.
- **Massenschatten** (die Hindernisse der Welt): kein Laden und keine Ankunft näher als 60 an der Hülle einer Station, 60 an einem Gate oder 25 an der Oberfläche eines Felsens ab Radius 6 (Regeln `jumpShadowStation`, `jumpShadowGate`, `jumpShadowRock`, `jumpShadowRockRadius`).
- **Ankunft** deterministisch ohne Zufall (kein `Universe.Rng`, kein `System.Random`): Ring mit 0,7 × Sektorausdehnung um die Mitte (`jumpArrivalRing`), Startrichtung zum Tor, das zum Herkunftssektor führt (sonst aus Schiffs- und Sektor-Ids), dann 16 Richtungen und vier Ringgrößen — die erste Stelle ohne Massenschatten. Die Ansicht gibt danach 2,5 s Schonfrist wie nach einem Gate-Sprung.
- **Politik wie am Gate, aber ohne Gate:** Ausreise aus dem Startsektor und Einreise in den Zielsektor werden mit den vollen Sperrstufen geprüft (`GateAccess.EvaluateJump`; **keine Hauptroute-Ausnahme** — die gilt nur am Gate). Abriegelung (Ausreise Stufe 5) und gesperrte Einreise **weisen den Sprung ab** (kein Schlupfloch durch die Hintertür); Zoll wird beim Sprung gezahlt (sonst Zollflucht wie am Gate), Schmuggelware an der Ankunft ist **Schmuggel** (Fahndung, Lagekarte). Einziger Weg durch eine Sperre: ein **Tarnfeld stärker als die Sprung-Sensoren** (`jumpSensorStrength` 2, „Sprungspur“) — die Sperre ist rechtlich, also undicht, wie am Gate mit dem `DetectionCheck`. Geprüft wird beim Start (Ablehnung ohne Verbrechen) und noch einmal beim Sprung (dann mit Meldung des Verbrechens).
- **Abbruch** des Ladens: Schiff bewegt, angedockt, unterwegs, Torsprung, Gefechtsbeginn, Treffer, Massenschatten, Modul ausgefallen, Leistung fehlt, Schiffswechsel, erneutes Tippen auf „Sprung“.
- **Nur der Spieler** nutzt den Antrieb (die KI nicht): keine NPC-Fahrplanabschnitte, kein `JumpLeg`, keine Routensuche mit Sprungkanten. Die Regeln selbst ändern die NPC-Welt nicht; nur die neuen Modulwaren, Systemwerk-Rezepte und Werftregale verschieben ihren Lauf (ohne diesen Inhalt ist der Referenz-Hash gleich, Test `NpcWelt_OhneDenInhalt_Referenzhash`). Schmuggler/Piraten mit Sprungantrieb sind ein späterer Schritt.
- **Herstellung:** Modulware, gebaut im Systemwerk (Stufe 2; Elektronik 3, Wafer 2, Platin 2, Brennstoffzellen 2), Werftregal [1, 1]. **Keine Forschungssperre:** eine Freischaltung in „Fusionstechnik Mk3“ hätte die eingefrorene Migrationskette (v15 → v16 ergänzt die Projekte aus der Standardwelt) verändert; mit eingeschalteter Forschung ist der Antrieb frei.
- **Sprungsperre** (Sperrfeld von Stationen/Toren) ist **nicht** gebaut: der Massenschatten deckt die Nähe zu Stationen und Toren schon ab.

### Bedienung

Schacht „Sprung“ im Daumenbogen (eigene Gruppe): „Ziel wählen“ öffnet die Sternenkarte im Sprungmodus (erreichbare Sektoren cyan umrandet), ein Tipp auf einen davon startet das Laden (Regler auf STOPP, Autopilot und Auto-Mining aus). Die Taste zeigt „lädt n %“ bzw. „kühlt m:ss“ oder den Grund („Massenschatten“, „Leistung fehlt“); ein Tipp während des Ladens bricht ab. Ladeleuchten (Kugel und Ring) wachsen mit dem Fortschritt, die Ankunft blitzt. Der Zeitraffer läuft auch beim Laden (Simulationsticks).

### Spielstand

Additiv (v17 bleibt): `ShipSave.JumpTarget`, `JumpCharge`, `JumpFrom`, `JumpCooldownUntil`; gehasht nur wenn gesetzt.

## 5. Geschlossene Jumpgates

Heute hat `Gate` nur `Open` (Standard `true`); `FindRoute` ignoriert geschlossene Gates, aber nichts schaltet sie. Das Fraktionsverhalten ist noch nicht gebaut. Geplant ist ein Zugangsmodell statt eines Schalters:

| Zustand | Bedeutung | Wer löst aus |
|---|---|---|
| `Open` | frei | Standard |
| `Closed` | für alle gesperrt (Wartung, Quarantäne, Blockade) | Fraktion, Ereignis |
| `Restricted` | je Fraktion: erlaubt, Zoll oder gesperrt | Fraktionspolitik |
| `Destroyed` | dauerhaft kaputt, Wiederaufbau nötig | Kampf, später Bauschiff |

- **Zustände sind physisch, Sperrstufen rechtlich:** Die Zustände dieser Tabelle beschreiben das Gate selbst. Die Sperrstufen 0–5 aus [POLITIK.md](POLITIK.md) (Abschnitt 2) sind Politik und werden auf `Restricted` abgebildet (Stufen 1–3) oder setzen die Politik für alle auf `Deny` (Stufen 4–5), ohne das Gate physisch zu schließen. Physisch `Closed` wird ein Gate nur durch Wartung, Ereignis oder Zerstörung.
- **Zugangsregel pro Fraktion:** `GateAccess` mit Besitzerfraktion und Politik je andere Fraktion (`Allow`, `Toll`, `Deny`). `Toll` verlangt einen Betrag (Geldsenke, Einnahme der Besitzerfraktion).
- **Auslöser:** Ein `FactionPolicySystem` (Teil des Fraktionsverhaltens) ändert die Politik nach Beziehungen: Krieg schließt, Frieden öffnet. Entscheidungen haben Hysterese und Mindestdauer, damit Gates nicht flackern.
- **Schiffe unterwegs:** Eine Reise über ein Gate, das sich schließt, wird beim nächsten Nachdenken neu geplant. Schiffe, die den Sektor nicht mehr verlassen können, werden untätig und warten.
- **Sektorabschottung:** Wenn die Weltkarte durch geschlossene Gates zerfällt, driften Preise auseinander, und Stationen ohne Nachschub gehen pleite. Gegenmaßnahmen:
  - eine Hauptroute je Region bleibt immer offen (oder Gates öffnen sich nach einer Frist wieder);
  - die Wirtschaftsstatistik zeigt isolierte Sektoren an, und das Balance-Tool schlägt bei Aufspaltung Alarm;
  - Sprungschiffe und Blockadebrecher füllen die Lücke und verdienen am Preisunterschied.
- **Architektur:** `Gate.Open` wird durch einen Zugriff `gate.CanPass(ship, universe)` ersetzt. `FindRoute` bekommt dafür das Mobilitätsprofil und die Fraktion des Schiffs. Save: `GateSave` erhält Zustand und Politik, plus Migration (alle bisher `Open`).
- **Blockadebrecher** (später): Fähigkeit, Sperren mit Panzerung, Zollzahlung oder Tarnfeld zu passieren; mit Sprungantrieb gar nicht erst mit dem Gate.

## 6. Tarnfeld (umgesetzt)

Ein Utility-Modul mit der Fähigkeit `Stealth`. Es senkt die Wahrscheinlichkeit, entdeckt zu werden — kein Unsichtbarkeitsmantel für den Kampf. Umgesetzt in `SpaceSim.Core/Flight/Cloak.cs`; Tests in `SprungTarnTests.cs`, Selbsttest-Stufe 130.

### Entscheidungen

- **Umschalter statt Dauerwirkung:** Das Modul wirkt nur **eingeschaltet** (`Ship.Cloaked`); eingebaut, aber aus, ist das Schiff normal sichtbar (der passive Modulwert `stealth` bleibt für spätere Inhalte).
- **Entdeckung ohne Würfel:** Tarnstärke gegen Sensorstärke, deterministisch wie der bestehende `DetectionCheck`: stärker getarnt = unbemerkt. Sensoren: Kontrollpunkt am Gate 1 (`gateSensorStrength`), Kaperer 1 (`raiderSensorStrength`), Polizei 2 (`policeSensorStrength`), Kleinstationen 2 (`stationSensorStrength`), Sprung-Ankunft 2 (`jumpSensorStrength`). **Mk1** `cloak_s` (Stärke 1,5) entgeht Kontrollpunkten und Kaperern, **Mk2** `cloak_s_mk2` (2,5) zusätzlich Polizei, Plattformen und der Sprungspur.
- **Wirkung:** am Gate unentdeckt (Sperre und Zoll entfallen, wie bisher im `DetectionCheck`); beim Sprung in gesperrte Sektoren (nur Mk2); Kaperer wählen getarnte Schiffe nicht als Opfer; die Polizei kündigt weder Festnahme noch Kriegsangriff an; Plattformen warnen und schießen nicht von sich aus. Im Gefecht bricht die Tarnung ohnehin.
- **Kosten:** eingeschaltet zieht es `cloakDraw` (Mk1 2, Mk2 3) aus dem Reaktorüberschuss (Dauerlast steigt — Schild, Bank und Antrieb bekommen weniger, gleiche Regel wie die Leistungsverteilung); einschalten nur mit genug Überschuss. Tarnung und Sprungladen zugleich braucht einen stärkeren Reaktor.
- **Bricht bei:** eigenem Schuss oder Abbau (Laser, Spalter, Bohrer/Kontakt, Anker), Andocken, Gefechtsbeginn, Treffer, Modulausfall, Leistungsmangel, Schiffswechsel.
- **Nicht einschaltbar:** angedockt, unterwegs, im Gefecht, solange eine Warnung (Überfall, Festnahme, Krieg, Plattform) auf das Schiff läuft, ohne Leistung. Wer gewarnt ist, ist schon entdeckt.
- **Kein Lizenzsystem:** Tarnung im Hoheitsgebiet ist erlaubt; sie wirkt nur über die Entdeckung (ROADMAP Punkt 7 erledigt).
- **Nur der Spieler** nutzt es (die KI nicht); Schmuggler-KI ist ein späterer Schritt.
- **Herstellung:** Modulwaren im Systemwerk (Mk1 Stufe 2: Wafer 3, Elektronik 2, Platin 1; Mk2 Stufe 3: Wafer 4, Elektronik 3, Platin 2; Aufrüstpfad Mk1 → Mk2), Werftregale [1, 1] und [0, 1]. Keine Forschungssperre (wie beim Sprungantrieb, Abschnitt 4).

### Bedienung

Schacht „Tarnung“ im Daumenbogen (Umschalter): „aus“ / „AN · −2 Leist.“ / Grund. Das eigene Schiff wird eingeschaltet halb durchsichtig (Modell und Marker getarnter Schiffe). Meldungen „Tarnung an“, „Tarnung gebrochen: Treffer“.

### Spielstand

Additiv (v17 bleibt): `ShipSave.Cloaked`; gehasht nur wenn gesetzt.

## 7. Stationsdienste als Zwischenschritt

Die Dienste kommen **vor** den Slots, weil der Ausrüster einer ist und die Schnittstelle sonst fehlt.

```text
IStationService
  Key                                       "trade", "repair", "refuel", "outfitter", "shipdealer", "upgrade", "hangar", "taxi", "rental"
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
| `refuel` | Tank gegen Treibstoff (`propellant`, `ion_gas`, `fusion_fuel`) | Treibstoff ist eine Ware (Abschnitt 3) |
| `outfitter` | Module ein- und ausbauen | Module sind Waren aus dem Stationslager |
| `upgrade` | Module verbessern (Abschnitt 8) | Materialien, Wahrscheinlichkeit |
| `shipdealer` | Schiff kaufen (`BuyShip`) | Bausatz und Zusatzmaterial aus dem Lager; Parameter `classes`, `maxSize`, `assembly` (siehe „Kleine Schiffe an anderen Stationen“) |
| `hangar` | Angedockte Schiffe ansehen, wechseln, Befehle geben, nach Hause schicken | Liegeplätze; Liegegebühr je Tag **nur an fremden Stationen**, nach Schiffsklasse gestaffelt, an eigenen gratis |
| `taxi` | Der **Spieler** reist gegen Gebühr zu einer anderen Station, das **Schiff bleibt** im Hangar | Geld an der Person (POLITIK.md, „Spielerkasse“); Preis aus Strecke, Zoll und Lage |
| `rental` | Leihschiff (Standardmodell) gegen Gebühr und Pfand | damit der Spieler nie ohne Schiff festsitzt |

### Kleine Schiffe an anderen Stationen (entschieden)

Nicht nur die Werft verkauft Schiffe. Kleine Schiffe gibt es auch an **Handelsstationen**, **Ausrüstern (Equipmentshops)** und **Raffinerien**, über den Dienst `shipdealer` mit Einschränkung:

```json
"services": [ { "key": "shipdealer", "classes": ["skiff", "courier"], "maxSize": "S", "assembly": 0.10 } ]
```

- **Montage statt eigener Herstellung:** Die Station baut das Schiff beim Kauf aus einem **leichten Bausatz** (`ship_kit_light`) und den Zusatzmaterialien des Modells zusammen. Beides liegt als Ware im Stationslager und wird über den normalen Handel nachgeliefert. Der Preis ist Bausatz- und Materialpreis plus `assembly` (Montagegebühr). Fehlt der Bausatz, ist das Schiff nicht lieferbar; das Angebot zeigt den Grund.
- **Wer verkauft was:**

| Stationstyp | Klassen | Besonderheit |
|---|---|---|
| Handelsstation | Skiff, Kurier | Standardmodelle, schnell verfügbar |
| Ausrüster | Skiff | mit gewähltem Modulpaket vorgerüstet |
| Raffinerie | Bergbauer leicht, kleiner Tanker | passt zur Rolle der Station (Erz, Flüssigstoffe) |
| Werft | alle Klassen | baut auch mittlere und schwere Schiffe; braucht die passenden Bausatzstufen |

- **Grenzen:** Mittlere und schwere Klassen, Kampfschiffe und Kapitalschiffe gibt es nur an der Werft. Die Größe der Station begrenzt die Auswahl (kleine Station: ein Modell, mittlere: mehrere).
- **Warum:** Die Schiffskette bleibt an Bausätze und Ketten gebunden, aber der Spieler muss für ein erstes kleines Schiff nicht zur Werft reisen. Das verteilt Nachfrage nach Bausätzen auf mehrere Orte und macht Bausätze zu einer Handelsware.
- **Leihschiff (`rental`, Vorschlag):** Ein einfaches Standardmodell (zum Beispiel das Skiff *Pip*) gegen Gebühr, Tagesmiete und Pfand, an Stationen mit Hangar. Es ist der Ausweg, wenn der Spieler nach dem Taxi an einem Ort ohne eigenes Schiff ankommt.

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
2. **Slot-Format:** Content-Abschnitte, `WorldBuilder`-Prüfung, `ShipStats`, Save v4 mit `ModelKey`, Slotliste und `Fit`. Zuerst Einheitsmodelle, die das heutige Verhalten nachbilden (entschieden: **vor** dem Treibstoff, weil Tank, Antrieb mit Treibstoffliste und Warenform im Modell stecken).
3. **`repair`** (Hüllenplatten), dann **`refuel`** mit `Ship.Fuel` für alle Schiffe (Save-Änderung).
4. **Module als Waren** und **`outfitter`**, danach `shipdealer` (zuerst Werft, dann kleine Schiffe an Handelsstation, Ausrüster und Raffinerie) und Startpakete. `hangar`, `taxi` und `rental` kommen mit der Spielerflotte.
5. **NPC-Güteklassen und `upgrade`-Dienst** mit `UpgradeSystem`.
6. **Gate-Zugangsmodell** und Mobilitätsprofil in der Routensuche (Voraussetzung für Sprungantrieb und Sperren), danach **Sprungantrieb** (zurückgestellt).
7. **Tarnfeld** (zurückgestellt) mit `IDetectionCheck`, erst wenn Gate-Kontrolle und Fraktionsverhalten stehen.

## 10. Noch offen

- Reichweite des Sprungantriebs über `Sector.MapPosition` oder über Gate-Hops?
- Wer darf Gate-Zugangsregeln ändern: nur Fraktionspolitik oder auch Ereignisse (Zerstörung, Wartung)?
- Soll es Sprungsperren als eigene Station oder Station-Dienst geben?
- Tarnung: Gilt sie auch für Handel (Stationen verweigern Tarnschiffe)?
- Zahlen: Güteklassenchancen, Upgrade-Chancen, Mitleidszähler, Sprungkosten.

## 11. Zurückgestellt: Besatzung und Lebenserhaltung für Schiffe

Das Besatzungssystem würde den Umfang sprengen und kommt später. Bis dahin gilt: **Schiffe brauchen keine Besatzung und keine Lebenserhaltung.** Kolonien verbrauchen `life_support` unverändert. Damit die Idee nicht verloren geht, der Stand der Überlegung:

- Besatzung ist abstrakt: Zahl in Einheiten und Erfahrungsstufe (Neuling, Normal, Veteran, Elite), Mindest- und Wunschbesatzung je Klasse.
- Kosten: Sold als Unterhalt, Lebenserhaltung als Verbrauch (beim Tanken mitgekauft).
- Herkunft: Kolonien erzeugen einen Bevölkerungspool, Rekrutierung an Bars und Heuerbüros, die Werft braucht Besatzung für Neubauten. Verluste verkleinern den Pool.
- NPC-Schiffe haben nur Zahl und gewürfelte Erfahrung, der Spieler ist Kapitän mit Rang.
- Offene Fragen: Besatzung als Zahl oder Personen, handelbare Ware oder Pool an der Station, Betriebsstoff für Kapitalreaktoren.

## 12. Treibstoff im Großmaßstab (entschieden)

Treibstoff bleibt in der Standardwelt **aus** (`rules.fuelActive` false, Entscheidung Phase 14). Schaltet ein Spieler oder eine eigene Welt ihn ein (`--fuel on`, `TestWorlds.Default(fuel: true)`), muss die vergrößerte Welt (Runde 44: Sektor-Maßstäbe 3–8, Fahrten ≈ 3,4-mal so lang, Flotte verdreifacht) so gut laufen wie die Grundmaßstab-Welt bei der Kalibrierung in Phase 4b.

**Gemessen vorher** (Balance-Tool `--fuel on --research on`, Standard-Seed und Seeds 1–4, 20000/60000 Ticks; Ausgaben unter `space-adventure-scratch-archiv/treibstoff/`):
- Treibstoffpreis im Schnitt **2,7–3,5× Basis**, Warnung „instabil“ in 6 von 10 Läufen; Raffinerie Beta 55–59 % der Zeit leer.
- **Notkäufe 133–219 (20000) bzw. 423–668 (60000)**; **Pleiten 19–38 Händler/Schürfer je 60000 Ticks** (mit Treibstoff aus: 0).
- Treibstoffkosten = **15–17 % des Handelsumsatzes** (Grundmaßstab mit verdreifachter Flotte: 7,3 % bei Preis 1,26).
- Spieler: Drei-Sektoren-Flug (Median aller Stationspaare mit genau zwei Sprüngen) 2540 Einheiten Strecke statt 484 im Grundmaßstab → 5,1 Treibstoff je Flug, 30–39 Flüge je Tank.

**Ursachen.** (1) Nur zwei Raffinerien (Alpha, Beta — die alte 4-Sektoren-Mitte) für eine dreimal so große Flotte auf 11 Sektoren: Angebot gedeckelt, Preis am Deckel, lange Umwege zur Tankstelle, Notkäufe. (2) Verbrauch je Strecke unverändert, Strecken aber 5-mal so lang (Drei-Sektoren-Flug), während die Handelsspanne je Fahrt an der Ladung hängt und nicht an der Strecke — das Gewicht des Treibstoffs je Fahrt verdoppelte sich gegenüber der Kalibrierung.

**Entscheidungen.**
- **Verbrauch halbiert:** `fuelUse` der Antriebe 0,002 → **0,001** (chem_engine_s), 0,003 → 0,0015 (chem_engine_m), 0,0018 → 0,0009 (chem_engine_s_mk2). Damit liegt das Treibstoffgewicht wieder beim Grundmaßstab (6,4–6,9 % des Handelsumsatzes). Tankgrößen bleiben.
- **Drei weitere Raffinerien** (Stationen 82 Gamma (−65, 130), 83 Delta (130, −65), 84 Vesta (150, 70), geschützt): Osten, Süden und Norden der Karte haben eine Tankstelle in der Nähe; alle fünf Raffinerien haben größere Lager (Wasserstoff 90/150, Energie 120/150, Treibstoff 150/150 — längere Lieferwege brauchen Puffer wie die Rezeptlager in Runde 44). Mit Treibstoff aus fehlen sie wie die beiden alten (`FeatureSwitches.OmitFromWorld`); `maxStations` 66 → **69**, damit der Bau-Spielraum in beiden Fällen gleich bleibt (aus: 69 − 5 = 64 wie bisher 66 − 2; an: drei Plätze mehr für drei Stationen mehr).
- **Spielerreichweite (Phase 4c offen):** Mit halbiertem Verbrauch kostet ein Drei-Sektoren-Flug **2,3–2,6 Treibstoff** (≈ 80 Cr zum Basispreis); ein Tank (150–200) reicht für **59–79** solche Flüge (Grundmaßstab: 155–207, „kaum spürbar“). Entschieden: so lassen — Treibstoff ist für den Spieler ein spürbarer Kostenposten und alle paar Stunden ein Tankstopp, aber keine Reichweitengrenze, die einzelne Flüge plant. Der Notruf bleibt für den leergeflogenen Tank.
- Die Migrationsbasis (`SaveGameJson.MigrationBaseJson`, Stand v17) stellt Verbrauch, Raffinerien und Lager des Stands v17 wieder her.
- Die Hilfswelt `TestWorlds.FuelBaseScale` (Treibstoff-Langläufe im Grundmaßstab) ist entfallen: die beiden Langläufe laufen wieder in der Standardwelt.

**Gemessen nachher** (gleiche Läufe): Treibstoffpreis **1,01–1,10×**, Notkäufe **0** (20000) bzw. **0–2** (60000), Pleiten 0–3 je 60000 Ticks, Treibstoffkosten 2,1–2,8 % (20000) bzw. 6,4–6,9 % (60000) des Handelsumsatzes, Kapazität Handel/Bergbau 80,2–80,5/54,7–55,5 (Grenze 80/55), alle Läufe „Preise im Zielbereich“. Die Raffinerien laufen 18–26 % der Zeit (Reserve für Spieler und Flottenwachstum). Mit Treibstoff und Forschung aus bleibt die Standardwelt bitgleich (Hash nach 20000 Ticks 1364698EE92E10DC vorher wie nachher).
