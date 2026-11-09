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
| **Schwerer Frachter** | *Behemoth* (500 / 250 / langsam) | Hüllenplatten, Titan |
| **Bergbauer leicht** | *Pickaxe* (60 / 100, Laser Stufe 1) | Hüllenplatten |
| **Bergbauer schwer** | *Rockbreaker* (150 / 200, Laser Stufe 2) | Hüllenplatten, Titan, Brennstoffzellen |
| **Tanker** | *Cistern* (viel Kanisterraum, nur Flüssigstoffe, hohe Kapazität) | Stahl, Titan |
| **Bauschiff** | *Foreman* (baut Stationen) | Stahl, Elektronik, Plattierung |
| **Asteroidenbrecher** | *Quarry*, *Rockbreaker II* | Titan, Brennstoffzellen, Hüllenplatten |
| **Korvette** (später) | *Sting*, *Hornet* | `ship_kit_medium`, Platin |
| **Fregatte** (später) | *Warden*, *Lancer* | Platin, Titan |
| **Schlachtschiff** (später) | *Bulwark*, *Warhammer* | Hub-Werft, Kapital-Bausatz |
| **Blockadebrecher** (später) | *Smuggler* | braucht Fraktionssystem |
| **Abwracker** (später) | *Scrapper* | braucht Wracks und Recycling |

### Bausätze und Erzketten

- Der eine `ship_kit` wird in Stufen aufgeteilt: `ship_kit_light`, `ship_kit_medium`, `ship_kit_heavy`, später `ship_kit_capital`. Die Werft stellt sie aus mehr oder anderen Komponenten her.
- Das Zusatzmaterial des Modells verbraucht die Werft beim Kauf aus dem eigenen Lager. So gibt es wenige neue Waren, aber viele Modelle.
- Leichte Schiffe brauchen nur Eisen, Kupfer, Silizium. Titan und Platin sind das Tor zu schweren und Kampfschiffen. Treibstoff bindet die Gas- und Wasserstoffketten an die Flotte (siehe SCHIFFSSLOTS.md, Abschnitt 3).

## 3. Multirole: Fähigkeiten statt fester Rolle

Heute hängt die gesamte KI an `ShipRole`. Neu wird sie in zwei Dinge aufgeteilt:

- **Fähigkeiten (Capabilities)** hängen am Schiff: `Cargo`, `Mining`, `Combat`, `Construction`, `Research`, `Salvage`. Sie ergeben sich aus Klasse, Modell und eingebauten Modulen.
- **Aufgabe (Job)** ist Zustand: Handeln, Schürfen, Patrouille, Bauen. Die KI wählt sie unter den Aufgaben, die das Schiff kann.

Der Spieler ist ein Schiff ohne KI-Wahl. `ShipRole.Player` entfällt als Fähigkeit; der Spieler wird stattdessen über einen **Steuerer** (`Controller = Player`) erkannt. Das ist nötig, weil heute `TradeActions` und die Ereignisse (`PlayerSold`, `PlayerBought`) und `Materializer` die Rolle `Player` abfragen.

### Slots

Jede Klasse hat feste Slots, ein Modell belegt sie ab Werk, Spieler können sie am Ausrüster ändern.

| Slot | Beispiele | Gibt |
|---|---|---|
| Frachtraum | Standard, Gefahrgut, Flüssigtank | `Cargo` |
| Werkzeug | Mining-Laser I/II, Traktor, Bauausleger, Spaltlaser | `Mining`, `Construction` |
| Waffen | Geschütz, Rakete | `Combat` |
| Antrieb / Kern | Reaktor, Sprungantrieb, Schild | Tempo, Hülle, Leistung |

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
- **Hoher Unterhalt** (`UpkeepSystem`, Treibstoff; Besatzung ist zurückgestellt, siehe SCHIFFSSLOTS.md, Abschnitt 11). Eine Fraktion kann sich nur wenige leisten; das ist die natürliche Bremse.
- **Viele Slots, wenig Tempo**, etwa 6 Waffen, 2 Werkzeug, 1 Frachtraum.
- **Rolle:** Machtfaktor statt Händler. Es bewacht Sektoren, sichert Gates, eskortiert. Das setzt Fraktionsverhalten (Beziehungen, Krieg, geschlossene Gates) voraus und kommt daher nach diesem System.
- **Modelle:** *Bulwark* (defensiv), *Warhammer* (Angriff). Ein Carrier mit Hangar-Slots würde ich weglassen.

## 5. Brecher: drei Lesarten

**Entschieden: Lesart 1 (Asteroidenbrecher).** Die anderen beiden bleiben als spätere Optionen stehen.

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

- **Balance:** Ein Generalist darf nicht fast so gut schürfen wie ein Spezialist. Die Strafe (weniger Fracht, schwächerer Laser durch geteilte Leistung) muss messbar sein; vor dem KI-Kauf mit `tools/SpaceSim.Balance` prüfen.
- **Herdenverhalten:** Ein einheitlicher Job-Score lässt alle Schiffe gleichzeitig dieselbe Aufgabe wählen. Die Crowd-Penalty der Miner muss auf alle Aufgaben ausgeweitet werden.
- **Umfang:** Der Umbau von `ShipRole` betrifft viele Stellen. Er lohnt sich eher vor dem Kampf als danach.
- **Zählgrenzen:** Ein Behemoth darf nicht viele kleine Schiffe verdrängen, deshalb Kapazität statt Stückzahl zählen.

## 8. Vorgeschlagene Reihenfolge

1. Capabilities und Job trennen, noch ohne neue Schiffe (Verhalten bleibt gleich, Tests unverändert).
2. `ShipClass`/`ShipModel` mit Slots in der JSON, Save v4.
3. Job-Zuteilungssystem, erste Multirole-Modelle (*Prospector*, *Tinker*) und der Asteroidenbrecher als Test für das Slot-Modell.
4. Ausrüster als Dock-Service, `BuyShip` für den Spieler.
5. Später, nach Kampf und Fraktionsverhalten: Fregatten, Schlachtschiffe, Blockadebrecher. In der JSON dürfen sie vorher reserviert werden, die KI baut sie noch nicht. Abwracker mit dem Recycling.

## 9. Startpakete (entschieden)

Der Spieler startet nicht mit einem festen Schiff, sondern wählt ein **Startpaket**. Es ist ein Eintrag in der JSON (`player.startOptions`): Schiffsmodell, Startcredits, optional Startausrüstung und Startsektor. Alle Pakete nutzen dieselben Systeme; nur die Ausgangslage unterscheidet sich.

| Paket | Schiff | Idee |
|---|---|---|
| **Schürfer** | *Prospector* (Skiff mit Laser) | Erz abbauen und selbst verkaufen; wenig Credits, dafür sofort produktiv |
| **Händler** | *Nomad* (Skiff, mehr Fracht) | Etwas mehr Startgeld, kein Laser; lebt von Preisunterschieden |
| **Generalist** | *Tinker* oder *Pip* mit Werkzeug-Slot | Von allem etwas, nichts richtig |
| **Kämpfer** (später) | Korvette | Reserviert, erst mit dem Kampfsystem |
| **Bauer** (später) | *Foreman* | Reserviert, erst mit dem Bauschiffsystem (Abschnitt 10) |

Die Pakete sind austauschbar, weil das Fähigkeitenmodell (Abschnitt 3) die Rolle nicht mehr festlegt: Wer als Schürfer beginnt, kann später Fracht oder Waffen nachrüsten. Das Paket ist in `SaveGame` nicht nötig; nur das Schiff zählt.

## 10. Bauschiff genauer

### Heute
`StationLifecycleSystem` baut eine neue Station, wenn eine Ware knapp ist. Der reichste Teilnehmer (Station oder Schiff) zahlt die Baukosten in **Credits**, und die Station erscheint sofort am Zielort. Es werden keine Materialien verbraucht und kein Schiff gebraucht.

### Ziel
Eine Station entsteht in einem **Bauprojekt** und braucht echte Materialien und ein Schiff, das sie liefert:

1. Das Lifecycle-System entscheidet wie bisher, dass ein Typ an einem Ort fehlt, und legt ein **Bauprojekt** an (`BuildSite`): Typ, Position, Besitzer, noch fehlende Materialien. Es verbraucht noch keine Station.
2. Ein **Bauschiff** (Fähigkeit `Construction`) übernimmt den Auftrag. Es holt Stahl, Hüllenplatten und Elektronik bei Stationen, die sie liefern, und fliegt zur Baustelle.
3. Es liefert Material ab (Event `ConstructionDelivered`). Sind alle Materialien da, erscheint die Station (`StationBuilt`) und der Besitzer zahlt den Credit-Anteil.
4. Die Baustelle ist ein eigenes Objekt im Sektor: Sie hat eine Position, ist ein Hindernis und kann vom Spieler gesehen und beliefert werden. **Spielerbau (entschieden):** Der Spieler bringt Material hin und bekommt den Marktpreis (freier Handel an der Baustelle, ohne Missionssystem), über dieselbe Handelslogik wie an Stationen.

Das Bauschiff ist ein normales Schiff mit der Fähigkeit `Construction`; es nutzt dieselbe Handels-Logik wie Händler, aber sein Ziel ist die Baustelle statt eines Marktes. Es kann nebenbei Fracht transportieren und, mit Laser, selbst Material schürfen (*Foreman*).

### Das Henne-Ei-Problem

Sobald Bau echte Materialien braucht, entstehen Abhängigkeitskreise:

- **Materialkreis:** Eine Stahlhütte braucht Stahl zum Bauen, den nur Stahlhütten herstellen. Fällt die letzte Stahlhütte aus, kann keine neue gebaut werden.
- **Werftkreis:** Ein Bauschiff entsteht aus einem Bausatz der Werft; die Werft braucht Hüllenplatten, die ein Plattierwerk herstellt. Fehlt eine Station in der Kette, gibt es keine Bauschiffe, also keine neuen Stationen und keine Reparatur der Kette.
- **Startwelt:** Alle bestehenden Stationen und die Startflotte sind ohne Bauschiff entstanden. Alte Spielstände haben weder Baustellen noch Bauschiffe.

Die Lösungen sind gestaffelt und sollen zusammen verhindern, dass die Welt in einen Zustand ohne Weg zurück gerät:

1. **Bestandsschutz:** Bestehende Stationen bleiben unverändert. Sie müssen nicht nachträglich "gebaut" worden sein. Das Bauschiffsystem gilt nur für neue Stationen. Die vorhandene Markierung `Essential` verhindert, dass die letzte Station einer kritischen Kette (Stahl, Plattierung, Werft) entfernt wird.
2. **Notbau gegen Deadlock (entschieden):**
   - *Auslöser nach Zustand:* Er greift erst, wenn das Projekt **blockiert** ist (kein erreichbarer Lieferant hat das Material auf Lager oder kein Bauschiff ist erreichbar) und das seit einer Frist anhält. Die Frist verhindert, dass Material, das gerade unterwegs ist, umgangen wird.
   - *Nur Fehlendes ersetzen:* Nur die fehlenden Materialien werden durch Credits ersetzt (Grundpreis × Aufschlag). Die Credits verschwinden (Geldsenke), die Ware entsteht aus dem Nichts; das ist nur im Notfall vertretbar.
   - *Kritikalität:* Stationen kritischer Ketten (Stahl, Plattierung, Werft) lösen den Notbau früher aus als unkritische.
   - *Bremse:* Pro Fraktion läuft höchstens ein Notbau gleichzeitig, mit Abkühlzeit. Das Balance-Tool misst den Anteil des Notbaus an allen Neubauten, er darf nicht zum Normalfall werden.
   - *Werte in `EconomyRules` (JSON):* Aufschlag, Frist, Kritikalitätsfaktor, Abkühlzeit. Startwerte zum Tunen: Aufschlag x2, Frist 2000 Ticks (etwa 8 Minuten Spielzeit bei 4 Ticks/s).
3. **Zyklenfreie Baukosten:** Die Baukosten eines Typs dürfen keine Ware verlangen, die nur dieser Typ herstellt. Ein `ContentTests`-Test prüft den Abhängigkeitsgraphen: Jeder Stationstyp muss aus der Startwelt oder über den Notbau erreichbar sein.
4. **Startbestand an Bauschiffen:** Die Startwelt enthält pro Fraktion mindestens ein Bauschiff, damit die Mechanik von Anfang an läuft und nicht erst nach dem Kauf eines Bausatzes.
5. **Zeitgrenzen für Baustellen:** Ein Projekt ohne Fortschritt verfällt nach einer Frist, die Credits gehen zurück. Bereits gelieferte Materialien **bleiben als Lager an der Baustelle** (entschieden) und können von einem neuen Projekt oder vom Spieler aufgenommen werden. Wird das Bauschiff entfernt, lässt sich das Projekt neu vergeben.
6. **Migration:** Save v4 legt eine leere Baustellenliste an. Bestehende Welten laufen unverändert weiter, bis ein neues Projekt entsteht. Ein Schalter in `EconomyRules` (JSON) erlaubt zunächst den alten Direktbau, bis genug Bauschiffe existieren.

### Architektur
Neu wären das Modell `BuildSite`, ein `ConstructionSystem` (Module "Lifecycle") und die Events `ConstructionStarted/Delivered/Completed/Abandoned`. `StationLifecycleSystem` entscheidet nur noch, **ob** etwas gebaut wird, nicht mehr **wie**. `UniverseHash` und `SaveGame` bekommen die Baustellenliste. Die Balance muss danach mit `tools/SpaceSim.Balance` neu geprüft werden, weil Materialverbrauch die Nachfrage nach Stahl und Hüllenplatten dauerhaft erhöht.

## 11. Asteroidenbrecher (entschieden)

Der Brecher ist ein schweres Bergbauschiff, dessen **Spaltlaser** große Asteroiden in kleinere zerlegt.

- **Spaltregel:** Ein Spaltschuss teilt einen Asteroiden in 2 bis 4 Fragmente mit derselben `ClassKey`. Das Erz wird auf die Fragmente verteilt (ein kleiner Teil geht als Staub verloren, damit Spalten nicht kostenlos Erz erzeugt). Der Radius jedes Fragments folgt wie bisher aus dem Resterz.
- **Laserstärke (entschieden):** Es gibt **keine Härte und keine Sperre**. Ein stärkerer Laser hat eine **bessere Chance auf Ertrag und mehr Erz je Schuss** (Laserstufe geht in `LaserShot.Roll` ein: höhere `ChunkChance`, größere Chunks). So bleibt jeder Fels für jeden Laser abbaubar, und höhere Stufen sind einfach effizienter. Das gilt für den Spieler und die NPC-Miner gleichermaßen.
- **Warum Brecher:** Große Asteroiden sind für wenige Spieler gleichzeitig nutzbar. Der Brecher zerlegt sie, sodass mehrere Miner parallel arbeiten können, und erhöht das nutzbare Erz eines Gürtels.
- **Wirtschaftliche Rolle:** Er erschließt Erz, statt es selbst zu liefern. Spieler und NPCs können ihn zusätzlich als Dienstleistung nutzen (Gürtel "vorbereiten").
- **Beziehung zu bestehenden Systemen:**
  - `ResourceField.TargetAsteroids` und `AsteroidRespawnSystem`: Fragmente erhöhen die Zahl der Asteroiden. Respawn darf erst wieder greifen, wenn die Zahl unter dem Ziel liegt. **Limit pro Gürtel (entschieden):** eine Obergrenze als Vielfaches von `TargetAsteroids` plus eine Mindestgröße der Fragmente (Performance, Hindernisse für `PathPlanner`).
  - `BeltPlacement`: Fragmente werden in der Nähe des Ursprungsfelsens platziert und müssen die Platzregeln einhalten, sonst überlappen sie Hindernisse.
  - Determinismus: Alle Würfe laufen über `Universe.Rng`. Fragment-Ids sind sequenziell und gehören zum Save.
  - Event `AsteroidSplit` für Darstellung und Statistik; die Ansicht zeigt Spaltung nur im Spielersektor.
- **Risiken:** Mehr nutzbares Erz kann die Wirtschaft aus dem Gleichgewicht bringen (Erzpreise fallen, Miner verlieren), ebenso die höhere Ausbeute starker Laser. Balancetest nötig.
- **Modelle:** *Quarry* (reiner Brecher), *Rockbreaker II* (Brecher mit größerem Frachtraum).

## 12. Kampf-Platzhalter (nur Dokumentation)

Die Kampfklassen werden zunächst **nur hier beschrieben** (entschieden, ebenso: kein Kämpfer-Startpaket, bis es Kampf gibt). Es gibt weder JSON-Einträge noch Code, bis das Kampfsystem eingeplant ist. Später können sie mit einem Flag `reserved` in der JSON stehen, damit Klassenliste und Bausatzstufen schon stimmen.

| Klasse | Modelle | Voraussetzungen |
|---|---|---|
| **Korvette** | *Sting*, *Hornet* | `ship_kit_medium`, Platin, Waffen-Slots |
| **Fregatte** | *Warden*, *Lancer* | Titan, Platin, Fraktionsverhalten |
| **Schlachtschiff** | *Bulwark*, *Warhammer* | Hub-Werft, `ship_kit_capital` |
| **Blockadebrecher** | *Smuggler* | Fraktionssystem, geschlossene Gates |

Das Kampfsystem selbst (Waffen, Schilde, Patrouille, Piraten) steht auf der Roadmap hinter dem Fraktionsverhalten.

## 13. Noch offen

Keine inhaltlichen Fragen mehr offen. Die Zahlen (Notbau-Aufschlag und -Frist, Fragmentlimit, Laserstufenbonus) sind Startwerte und werden mit `tools/SpaceSim.Balance` getunt, sobald die jeweilige Funktion existiert.
