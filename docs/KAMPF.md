# Kampfsystem und Schiffssysteme (Plan)

**Status:** Nur Planung, nichts umgesetzt. Stand nach Phase 22. Baut auf [FLOTTEN.md](FLOTTEN.md) (Begegnungen, Verhaltensregeln), [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md) (Slots, Leistung, Feuerwinkel) und [POLITIK.md](POLITIK.md) (Fahndung) auf.

**Auslegung:** „Schiffssystem“ heißt hier die Bordsysteme eines Schiffs zur Laufzeit: Schild, Leistung/Speicherbank, Waffen, Hülle und der Zustand einzelner Module. Die Slots und Module selbst gibt es schon; es fehlt ihr Verhalten im Betrieb.

## 1. Ausgangslage

Was es schon gibt und wiederverwendet wird:

| Baustein | Stand | Rolle im Plan |
|---|---|---|
| `IEncounterResolver`, `AbstractEncounterResolver` | Begegnung wird in einem Schritt ausgewürfelt (fünf Zufallszahlen) | bleibt als Rückfall und für Tests; wird durch das Rundengefecht ersetzt |
| `CombatRules.Strength` | Stärke aus `weaponPower`, Schild ÷ 20, Hülle ÷ 50 | dient zur Kalibrierung (gleiche Erwartungswerte) |
| `PendingEncounter` | angekündigter Überfall auf den Spieler (15 s Warnung) | wird zum Einstieg ins Gefecht |
| `FiringArc`, `WeaponBay`, „Alle Feuer“ | Feuerwinkel und Schachttasten, bisher nur Mining-Laser, Traktor, Spalter | Waffenschächte nutzen dieselbe Regel |
| `ShipStats` | `Shield`, `WeaponPower`, `powerStorage`, `peakDraw` als feste Werte | werden um Laufzeitwerte ergänzt |
| `Engagement` (Defend, Passive, Attack, HoldFire) | entscheidet nur, wer mitverteidigt | steuert künftig Angriff, Flucht, Feuer einstellen |
| `Departures` (Grund „zerstört“) | einziger Abgangsweg seit Phase 22 | Gefechtsverluste laufen darüber |
| `Wanted` (Verbrechen „assault“) | definiert, nicht angeschlossen | wird durch Spielerangriffe ausgelöst |
| `Capability.Salvage` | existiert, ungenutzt | Bergung von Wracks |
| Schild- und Leistungsanzeige im Flug-HUD | Platzhalter | werden echt |

Vorbild für die Architektur ist der Bergbau: NPC-Schürfer schießen in der Simulation (`MinerSystem`, ein Schuss je Tick, Ereignis `MinerShot`), der Spieler schießt in Echtzeit (`OreChunks.TryFire`), und beide benutzen dieselbe Regel `LaserShot.Roll`. Die Ansicht zeichnet NPC-Schüsse nur aus Ereignissen. Genau so soll der Kampf gebaut werden.

## 2. Grundsätze

1. **Eine Trefferregel für alle.** Eine reine Funktion (`Combat/Hit.Roll`) entscheidet Treffer und Schaden. NPC-Gefecht, Spielerwaffen und Stationsgeschütze rufen sie auf.
2. **Kampf ist Simulation, nicht Ansicht.** Gefechte laufen im Core, deterministisch über `Universe.Rng`, gespeichert und gehasht. Die Ansicht zeichnet nur.
3. **Ein System, zwei Eingaben.** NPCs feuern im Tick-Takt, der Spieler in Echtzeit. Es gibt kein zweites Kampfmodell für den Spielersektor.
4. **Kein Totlauf der Wirtschaft.** Die Verlustquote bleibt unter 1 % des Handelsumsatzes; jede Stufe wird mit dem Balance-Tool gegen den Stand von Phase 22 gemessen.
5. **Mobile zuerst.** Geschütze zielen selbst, der Spieler wählt Ziel, Abstand und Leistungsverteilung. Kein Zielen mit dem Finger.
6. **Module hinter Nähten.** Eigenes `CombatModule`, Waffenarten und Schadensarten als Daten, Taktik als austauschbare Schnittstelle.

## 3. Schiffssysteme

### 3.1 Laufzeitzustand je Schiff

| Wert | Bedeutung | Quelle der Obergrenze |
|---|---|---|
| `Hull` | Hülle (gibt es) | `ShipStats.Hull` |
| `ShieldNow` | aktueller Schild | `ShipStats.Shield` (Schildmodule) |
| `Charge` | Ladung der Speicherbank | `powerStorage` |
| `Condition[slot]` | Zustand je Modul, 0–1 | — |
| `WeaponTimer[slot]` | Nachladezeit je Waffe | Modulwert `reload` |

Alle Werte werden gespeichert und gehasht. Außerhalb eines Gefechts sind Schild und Bank voll und kosten keine Rechenzeit: Sie werden nur fortgeschrieben, solange ein Schiff im Gefecht ist oder sich danach erholt.

### 3.2 Leistung

- Der Reaktorüberschuss (`powerOutput − powerDraw`) lädt die Bank.
- Jeder Schuss zieht seine Spitzenlast (`peakDraw`) aus der Bank. Ist sie leer, feuert die Waffe nur im Takt des Überschusses.
- Die Schildregeneration zieht ebenfalls aus dem Überschuss.
- **Leistungsverteilung:** drei Stellungen (Waffen, Schild, Antrieb) plus „ausgeglichen“. Die Stellung verschiebt den Überschuss um einen festen Anteil. NPCs wählen sie über ihre Taktik, der Spieler über drei Tasten.

### 3.3 Schaden

Reihenfolge: Schild → Panzerung → Hülle → Modul.

- **Schild** fängt Schaden vollständig ab, bis er leer ist, und lädt nach einer kurzen Pause wieder.
- **Panzerung** (Modulwert `armor`) zieht einen festen Betrag je Treffer ab; kleine Waffen prallen an schweren Schiffen ab.
- **Hülle** nimmt den Rest.
- **Modulschaden:** Unter 50 % Hülle trifft ein Teil der Treffer ein zufälliges Modul und senkt seinen Zustand. Unter einer Schwelle fällt das Modul aus (`Ship.RecomputeStats` rechnet ohne es). Reaktor und Antrieb fallen nie ganz aus, sondern laufen mit halber Leistung, damit kein Schiff unrettbar liegen bleibt.
- **Reparatur:** Der bestehende Dienst `repair` repariert auch Module und verbraucht dafür Hüllenplatten und Elektronik. Die automatische Vollreparatur beim Andocken entfällt mit dieser Stufe.

### 3.4 Waffen und Schadensarten (Daten)

Zwei Schadensarten reichen für den Anfang:

| Art | Stark gegen | Schwach gegen | Beispiel |
|---|---|---|---|
| Energie | Schild | Panzerung | Laserkanone (gibt es) |
| Kinetisch | Panzerung, Hülle | Schild | Geschütz (neu) |

Neue Modulwerte: `damage`, `damageType`, `reload`, `range`, `accuracy`, `tracking` (wie gut die Waffe schnelle kleine Ziele trifft). Die Trefferchance ergibt sich aus `accuracy`, Entfernung, `tracking` gegen Zieltempo und Zielgröße. `weaponPower` bleibt als abgeleitete Kennzahl für die KI-Bewertung.

Raketen und Munition als Ware kommen erst in einer späten Stufe (siehe offene Entscheidungen).

## 4. Kampfsystem

### 4.1 Gefecht als Objekt

`Encounter` (in `Universe.Encounters`, gespeichert): Sektor, Seiten, Teilnehmer, Startzeit, Anlass (Überfall, Abfangen, Angriff des Spielers, Polizei). Ein Schiff im Gefecht ist für Handel, Bergbau und Befehle gesperrt (Ebene „Notfall“ der Befehlsordnung aus Phase 12).

Ein Gefecht endet, wenn eine Seite zerstört, geflohen oder aufgegeben ist oder eine Höchstdauer abläuft.

### 4.2 Rundengefecht (`CombatSystem`)

- Läuft jeden Tick nur über aktive Gefechte. Bei etwa 50 Überfällen je 20 000 Ticks ist die Rechenzeit vernachlässigbar.
- Je Tick: Bank laden, Schild regenerieren, jede bereite Waffe feuert auf ihr Ziel (`Hit.Roll`), Schaden anwenden, Ereignis `ShotFired` (Schütze, Ziel, Waffe, Treffer, Schaden).
- **Entfernung:** Schiffe außerhalb des Spielersektors haben keine Physik. Dort führt das Gefecht eine einzige Zahl je Paar, den Abstand, der sich nach Tempo und Taktik ändert (annähern, halten, absetzen). Feuerwinkel zählen dort als fester Abschlag je Waffe. Im Spielersektor kommen Abstand und Winkel aus den echten Positionen.
- **Zielwahl und Taktik:** Schnittstelle `ICombatTactic` (Ziel, gewünschter Abstand, Leistungsverteilung, Fluchtentscheidung). Erste Taktiken: Kaperer (schwächstes Ziel mit wertvoller Fracht, flieht unter 40 % Hülle), Polizei (hält stand, verfolgt nicht über die Grenze), Händler (flieht, wirft notfalls Fracht ab), Begleiter (schützt sein Schiff).
- **Flucht:** gelingt, wenn das Schiff den Abstand über die Waffenreichweite bringt, eine Station erreicht oder durch ein Tor springt. Schnellere Schiffe entkommen also wirklich.
- **Aufgabe:** Ein Händler kann Fracht abwerfen; der Kaperer nimmt sie und bricht ab. Das ersetzt den heutigen pauschalen Beuteanteil.

### 4.3 Spieler im Gefecht

- Die Schachttasten der Waffen arbeiten wie beim Bergbau: ein Schuss je Auslösung, „Alle Feuer“ für alles Bereite im Winkel, Dauerfeuer als Schalter.
- Der Spieler schießt in Echtzeit über dieselbe Trefferregel. Schüsse auf ihn kommen aus dem Tick-Takt und werden zwischen den Ticks interpoliert gezeichnet.
- **NPC-Bewegung im Spielersektor:** Gefechtsteilnehmer bekommen dort echte Positionen. Ein `CombatAutopilot` liefert wie der Mining-Autopilot nur Schritte (anfliegen, Abstand halten, abdrehen); die Simulation schreibt die Position fort, die Ansicht zeichnet sie.
- **Angriff durch den Spieler:** Ein Schuss auf ein fremdes Schiff eröffnet ein Gefecht und meldet das Verbrechen „assault“ beim Sektorbesitzer. Auf Gesetzlose zu schießen ist kein Verbrechen; ein Abschuss bringt das Kopfgeld.
- **Entkommen:** Andocken oder Sprung beenden das Gefecht wie heute die Überfallwarnung.

### 4.4 Wracks und Beute

Ein zerstörtes Schiff hinterlässt ein Wrack (eigenes Objekt im Sektor, verfällt nach einer Frist): ein Teil der Fracht und einzelne Module mit Restzustand. Bergen kann, wer die Fähigkeit `Salvage` hat (neues Werkzeugmodul); Fracht aufnehmen kann jeder mit Traktorstrahl. Das ist ein neuer Zufluss von Modulen und muss im Balance-Lauf gemessen werden.

### 4.5 Wirkung auf die Wirtschaft

| Wirkung | Erwartung | Messung |
|---|---|---|
| Schiffsverluste | mehr Bausatz- und Modulnachfrage | Verlustquote < 1 %, Neubauten je 72 h |
| Reparaturen | Dauerverbrauch an Hüllenplatten/Elektronik | Reparaturkosten je Lauf |
| Wracks | Module fließen zurück | geborgene Module je Lauf |
| Flucht statt Verlust | weniger zerstörte Opfer als heute | Opfer zerstört je 72 h (heute 73) |

Das Balance-Tool bekommt dafür Zeilen: Gefechte, Dauer, Schüsse, Ausgänge (zerstört, geflohen, aufgegeben), Modulschäden, Reparaturen, Wracks.

## 5. Oberfläche

- **Flug-HUD:** Schild- und Leistungsbalken echt; drei Tasten für die Leistungsverteilung; Zielkarte mit Schild und Hülle des Ziels; Warnung mit Richtung, wenn man beschossen wird.
- **Waffenschächte:** Zustand wie bisher (bereit, außerhalb des Winkels mit Drehhinweis, lädt nach, kein Ziel), neu „keine Leistung“.
- **Zielwahl:** Antippen eines Schiffs; Taste „nächster Gegner“. Die vorhandene Zielverfolgung der Kamera dient als Kampfkamera.
- **Schadensanzeige:** Liste der eigenen Module mit Zustand im Schiffsdetail und im Ausrüster; Reparaturzeilen im Dienst „Reparatur“.
- **Flotte:** Verhaltensregeln wirken echt (Passiv flieht, Feuer einstellen schießt nicht zurück).

## 6. Stufen

Jede Stufe ist einzeln lauffähig, hat eigene Tests und einen Balance-Vergleich.

| Phase | Inhalt | Verhalten ändert sich für |
|---|---|---|
| **23 Schiffssysteme** | Laufzeitwerte (Schild, Bank, Modulzustand), Leistungsverteilung, HUD-Balken echt. Der alte Auflöser rechnet mit dem aktuellen Schild. | Anzeige; Simulation fast unverändert |
| **24 Trefferregel und Rundengefecht** | Waffendaten, Schadensarten, `Hit.Roll`, `Encounter`, `CombatSystem` mit Abstandszahl, Taktiken, Flucht und Frachtabwurf. Ersetzt die Sofortauflösung für NPC gegen NPC. Kalibriert auf die heutigen Verlustquoten. | NPCs (Piraten, Polizei, Händler) |
| **25 Spielerkampf** | Waffenschächte, Echtzeitschüsse, NPC-Bewegung im Spielersektor, Angriff als Verbrechen, Kopfgeld, Regel für den Verlust des eigenen Schiffs. | Spieler |
| **26 Schaden und Bergung** | Modulschaden, Reparatur von Modulen, Wracks, Bergungsmodul, Wegfall der Gratisreparatur beim Andocken. | alle |
| **27 Flottenkampf** | Polizei und Piraten über die Befehls-Ausführer aus Phase 12, Verbände kämpfen gemeinsam, Begleitschutz, Verfolgung. | Flotten, Fraktionen |
| **28 später** | Raketen und Munition, Stationsgeschütze und Stationsangriff, Erobern, große Kriegsschiffe, Krieg zwischen Fraktionen. | — |

Phase 23 und 24 sind die Grundlage; ab Phase 25 merkt der Spieler den Unterschied.

## 7. Risiken

- **Determinismus mit Echtzeiteingabe:** Spielerschüsse fallen zwischen Ticks. Lösung wie beim Bergbau: Der Schuss wird sofort über `Universe.Rng` gewürfelt und gebucht; Speichern mitten im Gefecht sichert das Gefecht mit.
- **Positionen im Spielersektor:** Heute sind NPC-Positionen dort reine Ableitung aus der Reise. Gefechtsteilnehmer brauchen eine eigene, gespeicherte Position. Das ist der größte Eingriff und gehört allein in Phase 25.
- **Wirtschaft kippt durch Verluste:** Phase 11 hat gezeigt, dass schon 3 % Zerstörungschance die Mk1-Module leerzieht. Deshalb Kalibrierung auf den heutigen Stand, bevor etwas schärfer gestellt wird.
- **Rechenzeit:** Gefechte sind selten; trotzdem bekommt das Modul eine eigene Zeile in „Rechenzeit je System“.
- **Umfang:** Phase 25 ist die größte Stufe (Ansicht, Eingabe, KI-Bewegung) und wird notfalls geteilt.

## 8. Offene Entscheidungen

| Nr. | Frage | Empfehlung |
|---|---|---|
| 1 | Was passiert, wenn das aktive Schiff des Spielers zerstört wird? | Das Schiff ist weg, Fracht verloren, Geld bleibt (Kasse gehört der Person). Die Person landet an der Heimatbasis und wechselt in ein eigenes Schiff oder ein Leihschiff. Heute sinkt die Hülle nie unter 25 %. |
| 2 | Munition als Ware? | Erst in Phase 28. Kinetische Waffen laden bis dahin ohne Verbrauch nach. |
| 3 | Gefechte außerhalb des Spielersektors: dasselbe Rundengefecht oder weiter die Sofortauflösung? | Dasselbe Rundengefecht mit Abstandszahl. Ein Modell, ein Ergebnis, egal ob man zusieht. |
| 4 | Wie tief geht Modulschaden? | Ein Zustandswert je Modul, Ausfall unter einer Schwelle, Reaktor und Antrieb nie ganz. Keine Brände, keine Besatzung. |
| 5 | Kauft der Spieler Kampfschiffe an der Werft? | Ja, ab Phase 25: Das Patrouillenboot wird verkäuflich, dazu ein leichter Jäger. Kaperschiffe bleiben gesperrt. |
| 6 | Darf der Spieler Händler überfallen? | Ja, mit Fahndung beim Sektorbesitzer und Verkauf der Beute nur dort, wo man nicht gesucht wird. Ein Schwarzmarkt kommt später. |
