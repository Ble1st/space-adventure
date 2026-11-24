# Schiffe: Klassen, Modelle, Multirole (Planung)

**Status: reine Planung, nichts davon ist umgesetzt.** Heute gibt es ein Standardschiff mit einer festen `ShipRole` (Trader, Miner, Fighter, Player), Fracht 100 und Hülle 100 (`Ship.MaxHull` ist eine Konstante). Neue Schiffe entstehen über den Bausatz `ship_kit` (`FleetSystem.Expand`). Siehe auch [ROADMAP.md](ROADMAP.md) und CLAUDE.md ("Planned: ship building").

## 1. Zwei Ebenen: Klasse und Modell

- **Klasse** (`ShipClass`): Rolle und Größenstufe, zum Beispiel "Leichter Frachter". Sie bestimmt Slots, Bausatzstufe und Materialien.
- **Modell** (`ShipModel`): das konkrete kaufbare Schiff innerhalb einer Klasse. Es bestimmt Werte (Fracht, Hülle, Tempo, Preis) und kann ein Spezialmaterial verlangen.

Beide stehen in der JSON mit eigenen Schlüsseln. Ein Schiff speichert nur `ModelKey` (plus Module und Aufgabe, siehe Abschnitt 3); die Klasse folgt daraus.

## 2. Klassen und Modelle

Jede Klasse hat auch einen **Liegegebührsatz** für fremde Stationen (kleine Klassen wenig, schwere Klassen viel); siehe [UI.md](UI.md).

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

- **Fähigkeiten (Capabilities)** hängen am Schiff: `Cargo`, `Mining`, `Combat`, `Construction`, `Salvage` (`Research` entfällt: Forschung gehört dem Akteur, nicht dem Schiff; `Jump` und `Stealth` kommen mit den zurückgestellten Modulen). Sie ergeben sich aus Klasse, Modell und eingebauten Modulen.
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
8. **Ausrüster (`IStationService`):** Slots ändern ist der Dock-Dienst "Ausrüster". Module sind Waren (SCHIFFSSLOTS.md, „Module sind Waren“): Jedes Modul hat eine Ware gleichen Schlüssels, die eine der drei Modulfabriken nach dem `cost`-Rezept herstellt; der Ausrüster verkauft sie aus dem Stationslager und baut sie ein.
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

## Phase 29: Schiffsraster – Umsetzungsplan und Fortschritt

Entscheidungen des Nutzers: kein „Einheitsspieler“, Startschiff ist der Einheitsschürfer 100; Skiffs sind Standardmodelle; jede Fraktion und die Piraten
können alle Modelle kaufen und nutzen (auch die Fregatte); Module sind änderbar und in Stufen aufrüstbar (z. B. stärkere Schilde).

**Entscheidung Einheitsmodelle:** `unit_trader_100/60` und `unit_miner_100/60` bleiben als reguläre Standardmodelle im Raster (sie tragen die
Gründerflotte und fast alle Wirtschaftstests; ein Ersatz durch umbenannte Modelle mit denselben Werten brächte nur Umbenennungen). `unit_player`
entfällt (gleiche Werte wie `unit_miner_100`; alte Spielstände laden ihn als `unit_miner_100`). Die Skiffs grenzen sich davon ab: leichter, schneller,
kleiner Rumpf, Nutzlast kleiner bzw. formgebunden.

**Namen:** Schlüssel englisch (unverändert), Anzeigenamen einheitlich deutsche Eigennamen; nur die Standardmodelle heißen beschreibend
„Einheitsfrachter/-schürfer“ (Wortlaut des Nutzers).

| Rolle | S | M | L |
|---|---|---|---|
| Handel | `unit_trader_100` Einheitsfrachter 100, `unit_trader_60` Einheitsfrachter 60, `nomad` Nomade (Skiff, 70+30), `swift` Mauersegler (Kurier) | `mule` Maultier (schnell), `hauler` Lastkahn (viel Raum), `cistern` Zisterne (Tanker) | `behemoth` Behemoth |
| Bergbau | `unit_miner_100` Einheitsschürfer 100, `unit_miner_60` Einheitsschürfer 60, `prospector` Goldsucher (Skiff, schnell), `pickaxe` Spitzhacke (alle Bohrkopf) | `rockbreaker` Felsbrecher (Schneidrad S), `rockbreaker_ii` Felsbrecher II (Spaltlaser + Schneidrad S), `quarry` Steinbruch (Spaltlaser + Bohrkopf, Spieler), `excavator` Bagger (Schneidrad M) | – |
| Mehrzweck | `pip` Kiebitz (Skiff: Handel + Bergbau) | `tinker` Kesselflicker (Handel + Bergbau) | – |
| Kampf | `falcon` Falke (Jäger, Energiespeicher), `corsair` Korsar (Abfangjäger, nicht mehr gesperrt), `bomber` Donner (Bomber: zwei Raketenwerfer, langsam, gegen Stationen/Großschiffe) | `warden` Wächter (Patrouillenboot, zäh), `corvette` Hornisse (Geleitschutz: 2 Bug- + 2 Turmwaffen, Punktabwehr, M-Schild), `missile_boat` Lanze (Raketenboot, Distanz, keine Panzerung), `gunboat` Keiler (Kanonenboot: zwei M-Laser, doppelte M-Panzerung, langsam, schwaches Nachführen) | `bastion` Bollwerk (Fregatte: Raketen, Belagerung), `destroyer` Sturmvogel (Zerstörer: vier Türme, M-Schilde, Schiffsjäger) |
| Kampf XL | – | – | `battlecruiser` Leviathan (Schlachtkreuzer, Klasse XL, drei schwere Bausätze, L-Slots) |
| Bergbau L | – | – | `strip_miner` Maulwurf (Großschürfer: Schneidwalze L, 300 Erzraum, langsam) |
| Sonder | – | `foreman` Polier (Bau) | – |

**Stein-Schere-Papier im Kampf:** Raketen (Bomber, Lanze, Bollwerk) treffen große, langsame Ziele gut, werden von Punktabwehr (Hornisse, Zerstörer,
Schlachtkreuzer) abgefangen und verfehlen schnelle Jäger (Ausweichen über das Tempo). M-Laser (Keiler, Sturmvogel, Leviathan) schaden viel, führen aber schlecht
nach (Tracking 0,65) — Jäger (Falke, Korsar) entkommen ihnen. Jäger sind schnell, aber dünn gepanzert und verlieren gegen Panzerung und Schilde großer Schiffe.
Die Kampfmodellwahl nutzt die Klassenleiter: ohne Lage das beste Verhältnis Stärke je Preis (Korsar/Bomber), mit wachsender Lage Stärke² je Preis (Hornisse,
Zerstörer, Schlachtkreuzer, sofern bezahlbar und lieferbar). `ShipClass.KitCount` (Weltdatei `kitCount`): Bausätze je Schiff (Schlachtkreuzer 3 schwere).

**Skiffs als Standardmodelle:** Klasse `skiff` bekommt einen Waffenslot (S) und wird um 2 leichter (Laser 2); `nomad` Handel + Laser, `pip` Handel + Bergbau + Laser,
`prospector` Bergbau (ohne Laser im Waffenslot – Bergbauschiffe sind nicht pflichtbewaffnet; dadurch etwas schneller).

**Module in Stufen (Kampf):** neue Modulgüter mit Rezept im Waffen- und Schildwerk bzw. Antriebswerk/Systemwerk (Fabrik-Mechanik, `maxTier` 2), Upgrade-Pfad Mk1 → Mk2
(Upgrade-Dienst), kleine Werftregale:
`shield_mk2` (S, 70 / 0,8), `shield_m` (M, 120 / 1,2), `armor_plating_s_mk2` (S, 60), `armor_plating_m` (M, 90), `weapon_laser_s_mk2` (9 Schaden),
`weapon_laser_m` (M, 14 Schaden, 70 m), `weapon_massdriver_s_mk2` (13), `missile_launcher_s_mk2` (38, Magazin 8), `power_bank_s_mk2` (35), `point_defense_s_mk2` (0,5),
`reactor_m` (M, 24). M-Slots an M/L-Kampfklassen (Schild, Bugwaffe, Panzerung, Reaktor); kleinere Module passen weiter (FitRules unverändert).

Werftregale der neuen Module klein ([0, 1]: Wunschbestand 1) — größere Regale zogen Elektronik/Titan aus der Kette der Mk1-Reaktoren (Werft Delta 70 % der Zeit leer).
Neue Module ohne Platin (Platin bleibt der Luxusgüterkette).

**Wer kauft was:**
- Wirtschaftsflotte: alle Handels-/Bergbaumodelle über `ChooseModel` wie bisher (mit Jobs).
- Polizei und Marine: gemeinsame Kampfmodellwahl (`CombatModels.Choose`) unter allen Modellen ohne Fracht-/Bergbau-/Baufähigkeit mit Kampffähigkeit,
  Wert = Kampfstärke^(1+w) ÷ Preis, w = Gewicht der Lage (Bedrohung der Polizei, im Krieg 1) — schwerere Schiffe, wo es brennt; Kauf an Werften über die gemeinsame Kaufregel.
- Piraten: dieselbe Wahl nach Kasse; Bau im Versteck zum Materialwert (Basispreise) × `PirateBuildFactor` aus der Kasse (keine Fraktionswerft); Gruppengröße in
  Kapazitätseinheiten (S 1, M 1,5, L 2; höchstens `MaxRaiderUnitsPerActor` 2: zwei Korsaren oder ein M-Schiff oder eine Fregatte).
- Spieler: alle Modelle an Werften.

**Fortschritt**
- [x] Plan (dieser Abschnitt)
- [x] Inhalt: unit_player weg, Skiffs, Korsar frei, neue Module/Klassen/Modelle, M-Slots, Namen
- [x] Code: Startschiff (`WorldBuilder.UnitModelFor` Spieler → unit_miner_100), Migrationsbasis (Stand v17)
- [x] Kampfmodellwahl Polizei/Marine/Piraten (`CombatModels`), Piratenbau (Materialwert × 0,65 × Einheiten, Tempo zählt), Kapazitätseinheiten, Güteklassenwurf
- [x] Zusatz: 30 Modelle (Bomber, Kanonenboot, Zerstörer, Schlachtkreuzer XL, Großschürfer), `kitCount`
- [x] Tests (Raster, Kaufbarkeit, Modellwahl, Modulstufen, Startpakete, Revert-Helfer `Phase29Tests.RevertPhase29`)
- [x] Balance-Läufe (Ergebnis im Bericht), Selbsttest, CLAUDE.md
- [x] Zusatz F/G: Fraktionskürzel, Namensliste, Schilder mit fester Bildschirmgröße, kräftigerer Akzent
- [x] Zusatz H: Kontaktabbau mit physischen Abbaugeräten (Regel, Module, Fits, NPC, Spieler, Automodus, Anzeige, Tests)

**Kontaktabbau (Zusatz H, Wunsch „bei fast allen Miningschiffen physische Abbaugeräte“).** Ein Kontaktwerkzeug (Werkzeugslot, Wert `contactMining` = Erz je
Sekunde an einer Klasse mit Brockenchance 0,5) baut ab, solange der Bug den Asteroiden berührt (`Mining/ContactMining`: Bug höchstens 35° neben der Mitte und
höchstens 2,5 von der Oberfläche). Erz geht direkt in den Laderaum (keine Brocken, kein Traktor, kein Rückstoß, kein Verlust), in Portionen zu einer Einheit, Erzsorte
je Portion nach den Klassengewichten (ein Zufallszug). Spieler (`OreChunks.Contact`) und NPC-Schürfer (`MinerSystem`, Ereignis `ContactMined`) benutzen dieselbe Regel.
Stufen so gewählt, dass der Erzfluss dem Laser derselben Stufe entspricht: `drill_head_s` Bohrkopf (3,4 ≈ Laser Mk1, Upgrade → Schneidrad), `cutting_wheel_s`
Schneidrad S (5,525 ≈ Laser Mk2), `cutting_wheel_m` Schneidrad M (8,16 ≈ Laser Mk3; Werkzeugslot des schweren Schürfers jetzt M), `cutting_drum_l` Schneidwalze L
(10,2; Großschürfer). Laser behalten nur die Mehrzweckschiffe (Kiebitz, Kesselflicker) — sie bauen aus der Distanz und brauchen keinen Fels-Kontakt.
Ansicht: Schacht „Bohrer“ zeigt „kein Kontakt · Bug an Fels“ / „Abbau läuft“ / „Laderaum voll“, Funken an der Kontaktstelle (Spieler und NPC im Spielersektor);
der Automodus fliegt mit `ContactPush` (12 % Schub) an die Oberfläche — weit unter der Rammschwelle (`PlayerShip.HardImpact` 14), kein Hüllenschaden.
Drehende Werkzeuge an den Schiffsmodellen folgen, sobald die Blender-Modelle sie haben (`ContactMining.Spin` liefert die Drehzahl).

**Kaperer schonen die Beute (Regel `RaidersSpareLoot`).** Seit die Piraten Bomber kaufen, vernichtete eine Raketensalve leichte Schürfer (Skiff, Hülle 60) statt sie zur
Aufgabe zu zwingen (300000 Ticks: 59 statt 6 zerstörte Schürfer). Im Überfall feuert ein Kaperer keine Waffe mehr, deren Höchstschaden mit den schon fliegenden Raketen
Schild und Hülle eines beladenen Opfers aufbrauchen könnte (nicht beim aktiven Spielerschiff) — danach 7.

## Ausgleich nach dem Bot-Test: Schiffspreise wie in IP2, Flottenunterhalt nach Größe, schneller Falke

Nutzer: „Schiffspreise wie im decomp“. Schalter `playerShipPricing` (Code aus, Standardwelt an), **nur für den Spieler** — die KI-Flotte und die Polizei kaufen weiter zum Materialpreis × (1 + Montage), damit sich die NPC-Wirtschaft nicht verschiebt.

- **Kaufpreis** = max(Materialpreis zu Marktpreisen, Kampfstärke × 210) × (1 + 0,2 Händleraufschlag − sign(rel) × rel² × 0,2), aufgerundet auf 250 Cr (IP2/OpenFrontier: Herstellkosten × (1 + 0,2 ± |Ansehen|² × 0,2), gerundet auf 250). rel = Beziehung der Werftfraktion zum Spieler: freundlich bis 20 % billiger, feindlich bis 20 % teurer, nie unter dem Materialpreis. Der Aufschlag ersetzt beim Spieler die Montagegebühr.
- **Mindestpreis je Kampfstärke 210** (`playerShipMinPricePerStrength`): Kampfschiffe waren gemessen an ihrer Stärke billig (Material je Stärke: Zerstörer 163, Schlachtkreuzer 180, Bomber/Korsar 180–205, Händler 187–230, Schürfer 570–840). Jetzt kostet Stärke beim Spieler mindestens so viel wie bei zivilen Schiffen; zivile Schiffe ändern sich dadurch nicht.
- **Wiederverkauf** bleibt bei 0,5 × Material (immer unter dem Kaufpreis — keine Schleife; die 2,5 % Abgabe von IP2 würde mit dem Ansehensrabatt eine Schleife öffnen).
- **Flottenunterhalt nach Größe** (`playerFleetUpkeepBySize` [1, 1,5, 2,5, 4] für S/M/L/XL): statt flach 144 Cr/h je Schiff mit Befehl kostet ein Schlachtkreuzer 576 Cr/h. (IP2 hat keinen Unterhalt; ganz streichen hätte die letzte laufende Flottenkost genommen.)
- **Falke schneller als der Korsar** (Botbefund: Kopfgeldjagd unmöglich, Korsaren waren so schnell wie der Falke und schneller als jedes andere Kampfschiff): Grundmasse des Jägers 5 → 2, Tempo 8,64 → 10,0 (Korsar 8,64, mit Antriebsmodus +10 % 9,5). Datenänderung in der Weltdatei; Polizei und Piraten wählen den Falken kaum (Stärke je Preis wie der Korsar). Hinweis zum Bot: Seine Jagd bewegt das aktive Schiff im Kopflos-Gefecht nicht (der Spieler fliegt sonst in der Ansicht), dort flieht ein Korsar auch vor einem schnelleren Schiff.

## Bergeschiff (Bergeschlepper) und Schatten – eigene Modelle

**Bergeschiff `salvager`, Klasse `salvage_tug` „Bergeschlepper“ (neu).** Das Bergeschiff stand bisher auf der Klasse des leichten
Frachters (ein Werkzeugslot, kein Panzer); die NPC-Bergung braucht aber ein Arbeitsschiff, das an Wracks in umkämpften Sektoren
länger aushält. Eigene Klasse M, Bausatz `ship_kit_medium`, Rumpf 160, Grundmasse 14, Tank 250, Slots: Reaktor S, Triebwerk M,
**zwei** Werkzeug S, Panzer S, Waffe S (Drehturm — „jeder Händler bewaffnet“), zwei Fracht S, Utility S.
Werks-Fit: `reactor_mk2`, `chem_engine_m`, `wreck_grapple_s` (Bergen), `tractor_beam_s` (Traktorstrahl für Fracht),
`armor_plating_s`, `weapon_laser_s`, 2 × `cargo_universal_100` (200 universell), `wet_suppressor_s` (Löschanlage — brennende Wracks).
Leistung 14 gegen Verbrauch 13 (Triebwerk 4, Greifer 2, Traktor 1, Laser 3, Löschanlage 1 … Spitze Laser 1 im Überschuss),
mit dem Mk1-Reaktor ginge die Spitzenlast nicht auf. Masse 41 → Tempo ≈ 5,9 (zwischen Lastkahn 5,5 und Maultier 7,7), Hülle 160 + 40 Panzer.
Werft (alle Klassen) verkauft es, die Handelsstation nicht; die KI kauft es über die Aufgabe `salvage`.
`BergungTests.RevertSalvage` entfernt auch die Klasse, `SaveGameJson.AfterV17Keys` kennt `salvage_tug`.

**3D-Modell** (`tools/blender/ship_models.py`, `salvager`): aus dem Werkrumpf von Polier/Lastkahn abgeleitet — Achtkantrumpf mit
dunklem Bugpanzer und Warnring, **zwei bewegliche Greiferarme** (`Jaw_R`/`Jaw_L`, Gelenk senkrecht wie die Spaltbacken) mit nach innen
gebogenen Klauen, dazwischen der **Traktoremitter** mit Warnring, auf dem Rücken eine **offene Schrottwiege** (5 Rahmensegmente
mit Warnschienen, Schrott und Container darin, Ladekran), Seitenlager mit 5 Segmenten (zusammen 10 Segmente = 200), Laserturm
(`Turret_5`, Hardpoint Slot 5), Löschmitteltanks am Heck, zwei beringte Düsen. 2358 Dreiecke, zehn Materialien (`Hazard`, `Accent`,
`Engine` dabei), Box-UV (Oberflächentexturen greifen). Die Greifer klappen im Spiel bei jedem `WreckSalvaged` im Spielersektor
(Spieler und NPC) einmal auf und zu (`Main` → `ShipVisual.SnapJaws`). `verify_ships.py`: Nutzlast in der Wiege darf asymmetrisch sein.

**Schatten `shadow`** (Schmuggler-Blockadebrecher, Klasse `blockade_runner` M — Weltdaten auf einem anderen Zweig): hier nur das
Modell. Flacher dunkler Rautenpfeil aus dem Klingenrumpf des Korsar, facettierte Tarnplatten in dunklem Stahlblau, **Sprungspule**
als freistehender Ring mit leuchtender Wicklung um das Mittelschiff, zwei schräge **Tarnemitter-Finnen** mit Leuchtkante, versenkte
Triebwerke in Heckschächten, zwei flache Schmuggelkapseln unter dem Bauch (je 3 Segmente = 120), versenkter Laserturm
(`Turret_6`, Slot 6 laut Fit). 1734 Dreiecke. `build_ships.EXTRA_FITS` liefert den Fit für den Hardpoint-Eintrag, solange die
Weltdatei das Modell nicht kennt; `ShipModels` lädt es per Schlüssel, sobald beide Zweige zusammen sind.
`build_ships.py` bricht bei Modellen ohne Blender-Modell (z. B. `skimmer`) nicht mehr ab, sondern meldet sie.
`--model-shots <dir> --model-shots-ships=a,b,c` rendert nur diese Schiffe (nah, Spielabstand, bei Backen/Greifern zusätzlich halb offen).

## Baupläne (eigene Schiffsvarianten, Lücke gegenüber Interstellar Pilot 2)

- **Speichern:** Schiffsmenü → Reiter „Module“ → Namensfeld + „Als Bauplan speichern“. Gespeichert werden Modell (Klasse und Werks-Steckplätze) und das Fit der Werks-Steckplätze; Module in Zusatzsteckplätzen aus Upgrades fallen weg (Meldung nennt es). Gleicher Name ersetzt; höchstens 20 Baupläne, Name ≤ 32 Zeichen. Löschen mit zwei Tipps. Nur eigene Schiffe. Das Fit muss auf den Werks-Steckplätzen nach `FitRules` gültig sein.
- **Kaufen:** Werft (Dienst `shipdealer`) listet Baupläne als Angebot `blueprint:<Id>` neben den Modellen — gleiche Kaufregel `ShipPurchase` (Bausatz + Module aus dem Lager, Preis an die Station, Spiegelboden des Rückkaufdeckels, Ausgleichspreis für Spielerschiffe), die Werft muss die Klasse verkaufen; gesperrt, wenn Modell oder ein Modul für die Fraktion der Werft nicht erforscht ist. Kein Güteklassen-Wurf (der Bauplan legt die Module fest). Das Schiff liegt danach im Hangar (kein automatisches Einsteigen).
- **Anwenden:** Ausrüster → Abschnitt „Baupläne“ (gleiches Modell): „Anwenden“ baut je abweichendem Steckplatz das Modul des Bauplans über `OutfitterService.Execute` ein (aus der Fracht kostenlos, sonst aus dem Lager zum Preis; Fit-, Fracht- und Schadensregeln wie beim Einzeleinbau; zwei Durchgänge wegen der Leistungsbilanz). Was fehlt, bleibt.
- Zustand `Actor.Blueprints` (Spielerakteur), additiv gespeichert (`SaveGame.Blueprints`), gehasht nur mit Spielerpolitik und nur wenn vorhanden. Reine Spieleraktion — die NPC-Welt bleibt bitgleich.
