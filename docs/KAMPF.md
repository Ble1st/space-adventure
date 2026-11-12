# Kampfsystem und Schiffssysteme (Plan)

**Status:** Phase 23 (Schiffssysteme) umgesetzt, Phasen 24–28 Planung. Plan erstellt nach Phase 22. Baut auf [FLOTTEN.md](FLOTTEN.md) (Begegnungen, Verhaltensregeln), [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md) (Slots, Leistung, Feuerwinkel) und [POLITIK.md](POLITIK.md) (Fahndung) auf.

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
| `Burning[slot]` | Modul brennt (ja/nein) | — |
| `Ammo` | Munition im Magazin, getrennt vom Frachtraum (wie der Tank) | Modulwert `ammoCapacity` |

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
- **Brand:** Ein Treffer auf ein Modul kann es entzünden (Chance steigt mit sinkendem Zustand; Reaktor, Antrieb und Munitionsmagazin brennen leichter). Ein brennendes Modul verliert jede Runde Zustand und gibt etwas Schaden an die Hülle ab. Jede Runde wird gelöscht mit einer **Löschchance** (Grundwert aus den Regeln, höher mit dem neuen Utility-Modul „Löschanlage“). Der Brand endet durch Löschen, durch Ausfall des Moduls oder beim Andocken. Es gibt keine Besatzung; gelöscht wird automatisch, der Spieler sieht Brand und Chance in der Schadensliste. Auch brennend fallen Reaktor und Antrieb nie ganz aus.
- **Reparatur:** Der bestehende Dienst `repair` repariert auch Module und verbraucht dafür Hüllenplatten und Elektronik. Die automatische Vollreparatur beim Andocken entfällt mit dieser Stufe.

### 3.4 Waffen und Schadensarten (Daten)

Zwei Schadensarten reichen für den Anfang:

| Art | Stark gegen | Schwach gegen | Beispiel |
|---|---|---|---|
| Energie | Schild | Panzerung | Laserkanone (gibt es) |
| Kinetisch | Panzerung, Hülle | Schild | Geschütz (neu), verbraucht Munition |

Neue Modulwerte: `damage`, `damageType`, `reload`, `range`, `accuracy`, `tracking` (wie gut die Waffe schnelle kleine Ziele trifft). Die Trefferchance ergibt sich aus `accuracy`, Entfernung, `tracking` gegen Zieltempo und Zielgröße. `weaponPower` bleibt als abgeleitete Kennzahl für die KI-Bewertung.

**Munition als Ware (entschieden):**

- Neue Ware `ammo` „Munition“ (fest, eigene Kategorie, keine Exportware). Das Waffen- und Schildwerk stellt sie aus Stahl und Kupfer her.
- Kinetische Waffen verbrauchen je Schuss Munition aus dem Magazin (`ammoCapacity` an der Waffe, dazu ein Magazinmodul). Leeres Magazin: Die Waffe schweigt. Energiewaffen brauchen keine Munition, aber Leistung.
- Neuer Stationsdienst `rearm` (Werft, Polizeiwache, Handelsstation; Munition liegt dort als Lagerware, Händler liefern sie). Der Spieler lädt dort nach oder führt Munition als Fracht mit und lädt im Flug um.
- NPCs laden an der Heimatbasis bzw. beim Andocken nach und zahlen dafür (Polizei aus der Fraktionskasse, Piraten aus ihrer Kasse, Händler selbst).
- Schalter `ammoActive` in den Regeln (wie Treibstoff): aus = Magazine gelten als voll. Damit bleibt die Wirtschaft vergleichbar, falls die neue Stahl- und Kupfernachfrage etwas kippt.

Raketen kommen erst in einer späten Stufe.

## 4. Kampfsystem

### 4.1 Gefecht als Objekt

`Encounter` (in `Universe.Encounters`, gespeichert): Sektor, Seiten, Teilnehmer, Startzeit, Anlass (Überfall, Abfangen, Angriff des Spielers, Polizei). Ein Schiff im Gefecht ist für Handel, Bergbau und Befehle gesperrt (Ebene „Notfall“ der Befehlsordnung aus Phase 12).

Ein Gefecht endet, wenn eine Seite zerstört, geflohen oder aufgegeben ist oder eine Höchstdauer abläuft.

### 4.2 Rundengefecht (`CombatSystem`)

- Läuft jeden Tick nur über aktive Gefechte. Bei etwa 50 Überfällen je 20 000 Ticks ist die Rechenzeit vernachlässigbar.
- Je Tick: Bank laden, Schild regenerieren, jede bereite Waffe feuert auf ihr Ziel (`Hit.Roll`), Schaden anwenden, Ereignis `ShotFired` (Schütze, Ziel, Waffe, Treffer, Schaden).
- **Entfernung:** Schiffe außerhalb des Spielersektors haben keine Physik. Dort führt das Gefecht eine einzige Zahl je Paar, den Abstand, der sich nach Tempo und Taktik ändert (annähern, halten, absetzen). Feuerwinkel zählen dort als fester Abschlag je Waffe. Im Spielersektor kommen Abstand und Winkel aus den echten Positionen.
- **Zielwahl und Taktik:** Schnittstelle `ICombatTactic` (Ziel, gewünschter Abstand, Leistungsverteilung, Fluchtentscheidung). Erste Taktiken: Kaperer (schwächstes Ziel mit wertvoller Fracht, flieht unter 40 % Hülle), Polizei (hält stand, verfolgt nicht über die Grenze), Händler (bewaffnet und stark genug: wehrt sich; sonst flieht er und wirft notfalls Fracht ab), Begleiter (schützt sein Schiff).
- **Flucht:** gelingt, wenn das Schiff den Abstand über die Waffenreichweite bringt, eine Station erreicht oder durch ein Tor springt. Schnellere Schiffe entkommen also wirklich.
- **Aufgabe:** Ein Händler kann Fracht abwerfen; der Kaperer nimmt sie und bricht ab. Das ersetzt den heutigen pauschalen Beuteanteil.

### 4.2a Bewaffnete Handelsschiffe (entschieden)

- Frachter mit Waffenslot (der leichte Frachter hat einen) gibt es zusätzlich als bewaffnete Modelle; Bergbauschiffe wehren sich mit dem, was sie haben.
- Die KI-Flotte bewertet Bewaffnung beim Kauf mit dem Risiko ihrer Routen (Lagekarte): In ruhigen Sektoren lohnt die Waffe nicht, an der Grenze schon.
- Die Händlertaktik vergleicht die eigene Stärke mit der des Angreifers: kämpfen, fliehen oder Fracht abwerfen.
- Dasselbe gilt für Flottenschiffe des Spielers; die Verhaltensregel (Verteidigen, Passiv, Feuer einstellen) entscheidet.

### 4.3 Spieler im Gefecht

- Die Schachttasten der Waffen arbeiten wie beim Bergbau: ein Schuss je Auslösung, „Alle Feuer“ für alles Bereite im Winkel, Dauerfeuer als Schalter.
- Der Spieler schießt in Echtzeit über dieselbe Trefferregel. Schüsse auf ihn kommen aus dem Tick-Takt und werden zwischen den Ticks interpoliert gezeichnet.
- **NPC-Bewegung im Spielersektor:** Gefechtsteilnehmer bekommen dort echte Positionen. Ein `CombatAutopilot` liefert wie der Mining-Autopilot nur Schritte (anfliegen, Abstand halten, abdrehen); die Simulation schreibt die Position fort, die Ansicht zeichnet sie.
- **Angriff durch den Spieler:** Ein Schuss auf ein fremdes Schiff eröffnet ein Gefecht und meldet das Verbrechen „assault“ beim Sektorbesitzer. Auf Gesetzlose zu schießen ist kein Verbrechen; ein Abschuss bringt das Kopfgeld.
- **Entkommen:** Andocken oder Sprung beenden das Gefecht wie heute die Überfallwarnung.
- **Verlust des aktiven Schiffs (entschieden):** Schiff und Fracht sind weg (Wrack), das Geld bleibt. Die Person landet an der nächsten erreichbaren eigenen Station, sonst an der nächsten neutralen Station (Besitzer nicht feindlich, dort nicht gesucht). Dort wechselt sie in ein eigenes Schiff, das dort liegt, sonst bekommt sie ein Leihschiff. Die Hüllenuntergrenze von 25 % entfällt. Auch Flottenschiffe des Spielers können zerstört werden.
- **Überfall durch den Spieler (entschieden):** erlaubt; Fahndung beim Sektorbesitzer, Beute nur dort verkäuflich, wo man nicht gesucht wird.

### 4.4 Wracks und Beute

Ein zerstörtes Schiff hinterlässt ein Wrack (eigenes Objekt im Sektor, verfällt nach einer Frist): ein Teil der Fracht und einzelne Module mit Restzustand. Bergen kann, wer die Fähigkeit `Salvage` hat (neues Werkzeugmodul); Fracht aufnehmen kann jeder mit Traktorstrahl. Das ist ein neuer Zufluss von Modulen und muss im Balance-Lauf gemessen werden.

### 4.5 Wirkung auf die Wirtschaft

| Wirkung | Erwartung | Messung |
|---|---|---|
| Schiffsverluste | mehr Bausatz- und Modulnachfrage | Verlustquote < 1 %, Neubauten je 72 h |
| Reparaturen | Dauerverbrauch an Hüllenplatten/Elektronik | Reparaturkosten je Lauf |
| Wracks | Module fließen zurück | geborgene Module je Lauf |
| Munition | neue Stahl- und Kupfernachfrage | Verbrauch je Lauf, Preis von Stahl/Kupfer mit Schalter an und aus |
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
| **23 Schiffssysteme** | Laufzeitwerte (Schild, Bank, Modulzustand, Magazin), Leistungsverteilung, HUD-Balken echt. Ware Munition mit Herstellung und Lager (noch ohne Verbrauch). Der alte Auflöser rechnet mit dem aktuellen Schild. | Anzeige; Simulation fast unverändert |
| **24 Trefferregel und Rundengefecht** | Waffendaten, Schadensarten, `Hit.Roll`, `Encounter`, `CombatSystem` mit Abstandszahl, Taktiken, Flucht und Frachtabwurf, Munitionsverbrauch und Nachladen der NPCs, bewaffnete Händlermodelle. Ersetzt die Sofortauflösung für NPC gegen NPC. Kalibriert auf die heutigen Verlustquoten. | NPCs (Piraten, Polizei, Händler) |
| **25 Spielerkampf** | Waffenschächte, Echtzeitschüsse, NPC-Bewegung im Spielersektor, Angriff als Verbrechen, Kopfgeld, Verlust des eigenen Schiffs, Dienst `rearm`, Kampfschiffe an der Werft (Patrouillenboot, leichter Jäger). | Spieler |
| **26 Schaden und Bergung** | Modulschaden, Brand und Löschanlage, Reparatur von Modulen, Wracks, Bergungsmodul, Wegfall der Gratisreparatur beim Andocken. | alle |
| **27 Flottenkampf** | Polizei und Piraten über die Befehls-Ausführer aus Phase 12, Verbände kämpfen gemeinsam, Begleitschutz, Verfolgung. | Flotten, Fraktionen |
| **28 später** | Raketen, Stationsgeschütze und Stationsangriff, Erobern, große Kriegsschiffe, Krieg zwischen Fraktionen. | — |

Phase 23 und 24 sind die Grundlage; ab Phase 25 merkt der Spieler den Unterschied.

**Stand Phase 23:** umgesetzt (siehe UMSETZUNGSPLAN.md, Phase 23): Laufzeitwerte als Fehlbeträge (`ShipSystemsState`), Leistungsregel `Combat/ShipSystems`, `CombatModule` mit `ShipSystemsSystem` (nur nicht volle Schiffe), Schild vor Hülle und aktueller Schild in der Stärke, Ware `ammo` mit Rezept im Waffenwerk, Lagerware an Werft/Polizeiwache/Handelsstation, Munitionsmagazin (`ammoCapacity`), Schalter `ammoActive` (Standardwelt an), HUD-Balken und drei Verteilungstasten. Abweichung: Die Stellung „Antrieb“ wirkt in Phase 23 nur auf den Spielerflug (+10 % Höchsttempo); ohne Speicherbank fließt der Waffenanteil außerhalb des Gefechts in den Schild.

## 7. Risiken

- **Determinismus mit Echtzeiteingabe:** Spielerschüsse fallen zwischen Ticks. Lösung wie beim Bergbau: Der Schuss wird sofort über `Universe.Rng` gewürfelt und gebucht; Speichern mitten im Gefecht sichert das Gefecht mit.
- **Positionen im Spielersektor:** Heute sind NPC-Positionen dort reine Ableitung aus der Reise. Gefechtsteilnehmer brauchen eine eigene, gespeicherte Position. Das ist der größte Eingriff und gehört allein in Phase 25.
- **Wirtschaft kippt durch Verluste:** Phase 11 hat gezeigt, dass schon 3 % Zerstörungschance die Mk1-Module leerzieht. Deshalb Kalibrierung auf den heutigen Stand, bevor etwas schärfer gestellt wird.
- **Rechenzeit:** Gefechte sind selten; trotzdem bekommt das Modul eine eigene Zeile in „Rechenzeit je System“.
- **Umfang:** Phase 25 ist die größte Stufe (Ansicht, Eingabe, KI-Bewegung) und wird notfalls geteilt.

## 8. Entscheidungen (12.11.2026)

| Nr. | Frage | Entscheidung |
|---|---|---|
| 1 | Verlust des aktiven Schiffs | Echter Verlust. Die Person landet an der nächsten eigenen, sonst an der nächsten neutralen Station (Abschnitt 4.3). |
| 2 | Munition als Ware | Ja, von Anfang an (Abschnitt 3.4), mit Schalter. |
| 3 | Gefechte außerhalb des Spielersektors | Dasselbe Rundengefecht mit Abstandszahl. |
| 4 | Tiefe des Modulschadens | Zustandswert je Modul, dazu Brand mit Löschchance (Abschnitt 3.3). Keine Besatzung. |
| 5 | Kampfschiffe für den Spieler | Ja; außerdem können sich Handelsschiffe wehren (Abschnitt 4.2a). Kaperschiffe bleiben gesperrt. |
| 6 | Händler überfallen | Ja, mit Fahndung. Ein Schwarzmarkt kommt später. |
