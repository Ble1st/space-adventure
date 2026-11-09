# Schiffe: Klassen, Modelle, Multirole (Planung)

**Status: reine Planung, nichts davon ist umgesetzt.** Heute gibt es ein Standardschiff mit einer festen `ShipRole` (Trader, Miner, Fighter, Player), Fracht 100 und Hülle 100 (`Ship.MaxHull` ist eine Konstante). Neue Schiffe entstehen über den Bausatz `ship_kit` (`FleetSystem.Expand`). Siehe auch [ROADMAP.md](ROADMAP.md) und CLAUDE.md ("Planned: ship building").

## 1. Zwei Ebenen: Klasse und Modell

- **Klasse** (`ShipClass`): Rolle und Größenstufe, zum Beispiel "Leichter Frachter". Sie bestimmt Slots, Bausatzstufe und Materialien.
- **Modell** (`ShipModel`): das konkrete kaufbare Schiff innerhalb einer Klasse. Es bestimmt Werte (Fracht, Hülle, Tempo, Preis) und kann ein Spezialmaterial verlangen.

Beide stehen in der JSON mit eigenen Schlüsseln. Ein Schiff speichert nur `ModelKey` (plus Module und Aufgabe, siehe Abschnitt 3); die Klasse folgt daraus.

## 2. Klassen und Modelle

Alle Zahlen sind Startwerte zum Tunen (Fracht / Hülle / Tempo).

| Klasse | Modelle | Zusatzmaterial |
|---|---|---|
| **Skiff** (Einstieg) | *Pip* (30 / 60 / schnell), *Nomad* (50 / 80 / mittel) | keines, leichter Bausatz |
| **Kurier** | *Swift* (20 / 50 / sehr schnell), für wertvolle Waren pro Volumen | Elektronik |
| **Leichter Frachter** | *Mule* (120 / 100 / mittel), *Hauler* (180 / 120 / langsam) | Hüllenplatten |
| **Schwerer Frachter** | *Behemoth* (500 / 250 / langsam) | Hüllenplatten, Lebenserhaltung (Crew) |
| **Bergbauer leicht** | *Pickaxe* (60 / 100, Laser Stufe 1) | Hüllenplatten |
| **Bergbauer schwer** | *Rockbreaker* (150 / 200, Laser Stufe 2) | Hüllenplatten, Titan, Brennstoffzellen |
| **Tanker** | *Cistern* (nur Flüssig-/Gasgüter, hohe Kapazität) | Stahl |
| **Bauschiff** | *Foreman* (baut Stationen) | Stahl, Elektronik, Plattierung |
| **Asteroidenbrecher** | *Quarry*, *Rockbreaker II* | Titan, Brennstoffzellen, Hüllenplatten |
| **Fregatte** (später) | *Warden*, *Lancer* | Platin, Titan |
| **Schlachtschiff** (später) | *Bulwark*, *Warhammer* | Hub-Werft, Kapital-Bausatz |
| **Blockadebrecher** (später) | *Smuggler* | braucht Fraktionssystem |
| **Abwracker** (später) | *Scrapper* | braucht Wracks und Recycling |

### Bausätze und Erzketten

- Der eine `ship_kit` wird in Stufen aufgeteilt: `ship_kit_light`, `ship_kit_medium`, `ship_kit_heavy`, später `ship_kit_capital`. Die Werft stellt sie aus mehr oder anderen Komponenten her.
- Das Zusatzmaterial des Modells verbraucht die Werft beim Kauf aus dem eigenen Lager. So gibt es wenige neue Waren, aber viele Modelle.
- Leichte Schiffe brauchen nur Eisen, Kupfer, Silizium. Titan und Platin sind das Tor zu schweren und Kampfschiffen. Lebenserhaltung bindet Farm und Wasserkette an die Flotte.

## 3. Multirole: Fähigkeiten statt fester Rolle

Heute hängt die gesamte KI an `ShipRole`. Neu wird sie in zwei Dinge aufgeteilt:

- **Fähigkeiten (Capabilities)** hängen am Schiff: `Cargo`, `Mining`, `Combat`, `Construction`, `Research`, `Salvage`. Sie ergeben sich aus Klasse, Modell und eingebauten Modulen.
- **Aufgabe (Job)** ist Zustand: Handeln, Schürfen, Patrouille, Bauen. Die KI wählt sie unter den Aufgaben, die das Schiff kann.

Der Spieler ist ein Schiff ohne KI-Wahl; `ShipRole.Player` entfällt als Fähigkeit.

### Slots

Jede Klasse hat feste Slots, ein Modell belegt sie ab Werk, Spieler können sie am Ausrüster ändern.

| Slot | Beispiele | Gibt |
|---|---|---|
| Frachtraum | Standard, Gefahrgut, Flüssigtank | `Cargo` |
| Werkzeug | Mining-Laser I/II, Traktor, Bauausleger, Spaltlaser | `Mining`, `Construction` |
| Waffen | Geschütz, Rakete | `Combat` |
| Antrieb / Kern | Reaktor, Sprungantrieb, Schild | Tempo, Hülle, Energie |

Die Slotzahl ist der Preis der Vielseitigkeit: Wer wenige Slots hat, muss sich entscheiden. Spezialisten bleiben in ihrer Aufgabe besser, ein Generalist ist nie die beste Wahl für eine Einzelaufgabe, sondern die flexibelste.

### Multirole-Modelle (Beispiele)

| Modell | Klasse | Ab Werk | Charakter |
|---|---|---|---|
| *Prospector* | Skiff | 1 Laser + kleiner Frachtraum | Schürfer, der selbst verkauft; Spieler-Einstieg |
| *Tinker* | Leichter Frachter | Laser + großer Frachtraum + 1 Geschütz | Generalist |
| *Gunrunner* | Kurier | 2 Geschütze + kleiner Frachtraum | Schneller Händler mit Gegenwehr |
| *Salvager* | Mittel | Laser + Traktor + Frachtraum | Bergung von Wracks und Erz |
| *Foreman* | Bauschiff | Bauausleger + Frachtraum + Laser | Bauen und Material holen |

## 4. Schlachtschiffe (Capital-Klasse)

- **Bau nur an der Hub-Werft** (Stationsgröße "Hub", höchstens eine pro Sektor) mit `ship_kit_capital`: Platin, Titan, viel Hüllenplatten.
- **Hoher Unterhalt** (`UpkeepSystem`, Treibstoff, Crew über Lebenserhaltung). Eine Fraktion kann sich nur wenige leisten; das ist die natürliche Bremse.
- **Viele Slots, wenig Tempo**, etwa 6 Waffen, 2 Werkzeug, 1 Frachtraum.
- **Rolle:** Machtfaktor statt Händler. Es bewacht Sektoren, sichert Gates, eskortiert. Das setzt Fraktionsverhalten (Beziehungen, Krieg, geschlossene Gates) voraus und kommt daher nach diesem System.
- **Modelle:** *Bulwark* (defensiv), *Warhammer* (Angriff). Ein Carrier mit Hangar-Slots würde ich weglassen.

## 5. Brecher: drei Lesarten

Die Bedeutung ist nicht festgelegt. Empfehlung ist Lesart 1.

1. **Asteroidenbrecher (empfohlen):** schweres Bergbauschiff, dessen Spaltlaser große Asteroiden in mehrere kleinere zerlegt, die dieselbe Erzmischung tragen. Passt zu `Asteroid.Ore`, `AsteroidClass` und `AsteroidRespawnSystem`. Er verschafft Zugang zu Erz, produziert aber keines.
2. **Blockadebrecher:** gepanzerter Frachter/Kurier, der durch gesperrte Gates (`Gate.Open=false`) oder feindliche Sektoren liefert. Braucht Fraktionskonflikt und Kampf.
3. **Schiffsbrecher / Abwracker:** zerlegt Wracks und aufgegebene Schiffe zu Stahl und Komponenten. Braucht Wracks als neue Entität und die Recycling-Kette (siehe [STATIONEN.md](STATIONEN.md)).

## 6. Auswirkungen auf die Architektur

1. **`ShipRole` ersetzen:** Systeme fragen `ship.Has(Capability.Mining)` statt `ship.Role == Miner`. `MinerSystem`, `TraderSystem`, `FleetSystem` bleiben getrennte Systeme und verwalten nur Schiffe mit der jeweiligen aktuellen Aufgabe.
2. **`JobAssignmentSystem`:** Wählt für ein untätiges Schiff die Aufgabe mit dem höchsten erwarteten Gewinn pro Tick unter seinen Fähigkeiten, mit vorhandenem Scoring (Handelsmarge, `MiningRules.ExpectedOrePerSecond`).
3. **Wechselkosten:** Mindestdauer oder Hysterese für Aufgabenwechsel, sonst flackern die Schiffe und die Wirtschaft kippt.
4. **Flottengrenzen nach Kapazität:** `MaxTraders`/`MaxMiners` zählen heute Schiffe, künftig die aktuell ausgeführten Aufgaben. `FleetSystem` kauft das Modell, das die größte Lücke füllt. Die Klassenwahl läuft über eine Scoring-Regel in der JSON.
5. **Pro-Schiff-Werte:** `MaxHull`, Tempo, Fracht, Laserstufe, Spritverbrauch kommen aus dem Modell.
6. **Save v4:** Schiff speichert `ModelKey`, Module, aktuelle Aufgabe. Alte Schiffe werden nach altem `Role` auf Standardmodelle migriert. `UniverseHash` und DTOs müssen mit.
7. **Kauf:** `BuyShip` nutzt dieselbe Regel für Spieler und NPCs. Bausatz und Zusatzmaterial gehen aus dem Werftlager, die Werft bekommt das Geld. Der Spieler konkurriert um knappe Bausätze.
8. **Ausrüster (`IStationService`):** Slots ändern ist der Dock-Dienst "Ausrüster". Module sind Waren oder Bauteile (zum Beispiel Laser aus Elektronik und Platin).
9. **NPCs bauen nicht um:** Sie kaufen nur fertige Modelle. Das ist einfacher und besser balancierbar.

## 7. Risiken

- **Balance:** Ein Generalist darf nicht fast so gut schürfen wie ein Spezialist. Die Strafe (weniger Fracht, schwächerer Laser durch Energieteilung) muss messbar sein; vor dem KI-Kauf mit `tools/SpaceSim.Balance` prüfen.
- **Herdenverhalten:** Ein einheitlicher Job-Score lässt alle Schiffe gleichzeitig dieselbe Aufgabe wählen. Die Crowd-Penalty der Miner muss auf alle Aufgaben ausgeweitet werden.
- **Umfang:** Der Umbau von `ShipRole` betrifft viele Stellen. Er lohnt sich eher vor dem Kampf als danach.
- **Zählgrenzen:** Ein Behemoth darf nicht viele kleine Schiffe verdrängen, deshalb Kapazität statt Stückzahl zählen.

## 8. Vorgeschlagene Reihenfolge

1. Capabilities und Job trennen, noch ohne neue Schiffe (Verhalten bleibt gleich, Tests unverändert).
2. `ShipClass`/`ShipModel` mit Slots in der JSON, Save v4.
3. Job-Zuteilungssystem, erste Multirole-Modelle (*Prospector*, *Tinker*) und der Asteroidenbrecher als Test für das Slot-Modell.
4. Ausrüster als Dock-Service, `BuyShip` für den Spieler.
5. Später, nach Kampf und Fraktionsverhalten: Fregatten, Schlachtschiffe, Blockadebrecher. In der JSON dürfen sie vorher reserviert werden, die KI baut sie noch nicht. Abwracker mit dem Recycling.

## 9. Offene Entscheidungen

- Startschiff des Spielers: Skiff *Pip* (klein, Aufrüsten als Motivation) oder *Prospector*?
- Bauschiff schon in der Klassenphase oder erst nach den Schiffsklassen? (Dann bauen Fraktionen Stationen mit einem Schiff, das Stahl und Hüllenplatten liefert.)
- Welche Lesart von "Brecher" ist gemeint?
- Kampf-Platzhalter schon in der JSON oder erst mit dem Kampfsystem?
