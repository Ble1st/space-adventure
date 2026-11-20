# Kampfsystem und Schiffssysteme (Plan)

**Status:** Phasen 23 (Schiffssysteme), 24 (Trefferregel, Rundengefecht NPC gegen NPC), 25 (Spielerkampf), 26 (Schaden und Bergung), 27 (Flottenkampf) und 28 (Raketen, Stationen, Kriegsschiffe, Krieg) umgesetzt. Plan erstellt nach Phase 22. Baut auf [FLOTTEN.md](FLOTTEN.md) (Begegnungen, Verhaltensregeln), [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md) (Slots, Leistung, Feuerwinkel) und [POLITIK.md](POLITIK.md) (Fahndung) auf.

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

### 4.2a Bewaffnete Handelsschiffe (entschieden; seit Phase 25: jeder Händler bewaffnet)

- **Jeder Händler trägt eine Waffe** (Entscheidung des Nutzers in Phase 25): Alle Modelle mit Aufgabe Handel haben eine Laserkanone als Turm (Klassen ohne Waffenslot bekamen einen, die Klassen wurden um die Masse der Waffe leichter, Tempo und Frachtraum bleiben). Die getrennten bewaffneten Varianten entfallen; Bergbauschiffe wehren sich mit dem, was sie haben (unbewaffnet).
- (bis Phase 24:) Frachter mit Waffenslot (der leichte Frachter hat einen) gibt es zusätzlich als bewaffnete Modelle.
- Die KI-Flotte bewertet Bewaffnung beim Kauf mit dem Risiko ihrer Routen (Lagekarte): In ruhigen Sektoren lohnt die Waffe nicht, an der Grenze schon. Seit Phase 25 zählt dabei nur Bewaffnung über der Grundausstattung (`armedTraderBaseline`, eine Laserkanone).
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
| **26 Schaden und Bergung** (umgesetzt) | Modulschaden, Brand und Löschanlage, Reparatur von Modulen, Wracks, Bergungsmodul, Wegfall der Gratisreparatur beim Andocken. | alle |
| **27 Flottenkampf** (umgesetzt) | Polizei und Piraten über die Befehls-Ausführer aus Phase 12, Verbände kämpfen gemeinsam, Begleitschutz, Verfolgung. | Flotten, Fraktionen |
| **28 Raketen, Stationen, Krieg** (umgesetzt) | Raketen, Stationsgeschütze und Stationsangriff, Erobern (hinter Schalter), große Kriegsschiffe, Krieg zwischen Fraktionen. | Fraktionen, Spieler |

Phase 23 und 24 sind die Grundlage; ab Phase 25 merkt der Spieler den Unterschied.

**Stand Phase 23:** umgesetzt (siehe UMSETZUNGSPLAN.md, Phase 23): Laufzeitwerte als Fehlbeträge (`ShipSystemsState`), Leistungsregel `Combat/ShipSystems`, `CombatModule` mit `ShipSystemsSystem` (nur nicht volle Schiffe), Schild vor Hülle und aktueller Schild in der Stärke, Ware `ammo` mit Rezept im Waffenwerk, Lagerware an Werft/Polizeiwache/Handelsstation, Munitionsmagazin (`ammoCapacity`), Schalter `ammoActive` (Standardwelt an), HUD-Balken und drei Verteilungstasten. Abweichung: Die Stellung „Antrieb“ wirkt in Phase 23 nur auf den Spielerflug (+10 % Höchsttempo); ohne Speicherbank fließt der Waffenanteil außerhalb des Gefechts in den Schild.

**Stand Phase 24:** umgesetzt (siehe UMSETZUNGSPLAN.md, Phase 24): Waffenwerte als Modulwerte mit `damageType`, Laserkanone und kinetisches Geschütz, `Combat/Hit` (eine reine Trefferregel, zwei Zufallszüge je Wurf), Gefechte als gespeicherte Objekte (`Universe.Fights`) mit Positionen auf einer Gefechtslinie, `CombatSystem` mit den Taktiken Kaperer, Polizei, Händler und Begleiter, Flucht über die Reichweite, Frachtabwurf statt Beuteanteil, Munitionsverbrauch und Nachladen der NPCs, bewaffnete Frachtermodelle mit Kaufwahl nach Routenrisiko. Abweichungen: Der Überfall auf das aktive Spielerschiff bleibt abstrakt (Phase 25); Nachladeuhren liegen im Gefecht; Abstandszahl je Teilnehmer statt je Paar. Nach der Prüfung nachkalibriert (Kaperer fliehen früher, größerer Abwurfanteil, Munition nur auf Schüsse mit Aussicht): Verlustquote im Langlauf wieder ≈ 0,5 %, zwei Kaperer je Gruppe.

**Stand Phase 25:** umgesetzt (siehe UMSETZUNGSPLAN.md, Phase 25): Überfall auf das aktive Schiff als echtes Gefecht nach der Warnfrist, Spielerschüsse in Echtzeit über dieselbe Trefferregel (`PlayerGunnery`, gemeinsame Regeln in `Combat/Gunnery`), Waffenschächte je Kampfwaffe mit „keine Leistung/keine Munition“, Dauerfeuer, „Nächster Gegner“, Zielkarte mit Schild/Hülle, Beschusswarnung mit Richtung; Gefechte im Spielersektor räumlich mit gespeicherten Positionen und `CombatAutopilot` (ein Gefecht, Übergang Linie ↔ Raum beim Sektorwechsel); Angriff als Verbrechen „assault“, Kopfgeld auf Gesetzlose, Handelsverweigerung ab Fahndungsstufe 3; Verlust des aktiven Schiffs mit Landeregel (eigene, sonst neutrale Station; eigenes Schiff, Leihschiff oder Notfall-Leihschiff), Flottenschiffe zerstörbar; Dienst `rearm`, Munition aus der Fracht; Jäger „Falke“ und Wächter an der Werft, Startpaket „Jäger“; „Waffen“ wirkt als Überladung mit Speicherbank. Abweichungen: Der Gefechts-Autopilot kennt keine Hindernisse und keine Flucht durch Tore; abgeworfene Fracht ist nur ein Ansichts-Brocken (Wracks Phase 26); der Spieler kann sich nicht ergeben. Nach der Prüfung: Fahndung auch in herrenlosen Sektoren (Opferfraktion, sonst Nachbarsektoren), Angriff im Stationsschutz holt sicher die Polizei und wiegt schwerer, verlorene Leihschiffe verlieren ihr Pfand und zahlen die Miete (Notfall-Leihschiff notfalls auf Schulden), Landung bevorzugt erreichbare Stationen, Gefechtsteilnehmer werden aus Hindernissen geschoben, Überfallrate 0,008 → 0,0065 (Verlustquote Seeds 1–8 im Mittel 0,52 %). **Auch ein Aufprall, der die Hülle leert, zerstört das Schiff wirklich** (Entscheidung des Nutzers) — gleiche Landeregel wie im Gefecht.

**Stand Phase 26:** umgesetzt (siehe UMSETZUNGSPLAN.md, Phase 26): Modulschaden unter 50 % Hülle in der gemeinsamen Schussregel (genau drei Zufallszüge je Hüllentreffer unter der Schwelle), Ausfall mit halber Leistung für Reaktor/Antrieb, ausgefallene Frachtmodule werfen überzählige Fracht als Frachtbehälter ab; Brand mit Löschchance je Runde (Grundwert, Löschanlage), Andocken löscht, Brand läuft über den Erholungsindex auch nach dem Gefecht weiter; Dienst `repair` repariert Module (Hüllenplatten + Elektronik, „alles“, Modulzeilen), Gratisreparatur beim Andocken und Hüllenregeneration im Flug entfallen, NPC reparieren bezahlt (Händler/Schürfer an Stationen mit Reparatur, Polizei/Piraten/Fraktionsbauschiffe aus der Kasse), Regierungsstationen bieten Reparatur (in jedem Kartenteil mit Wirtschaft erreichbar); Wracks (Frachtanteil, Module mit Restzustand, Frist) und gespeicherte Frachtbehälter, Bergung von Fracht per Traktor und von Modulen mit dem Bergungsgreifer (ab Restzustand 50 %, sonst Schrott), Kaperer nehmen die Wrackfracht ihres Opfers mit; Leihmiete läuft täglich, Schulden sichtbar. Abweichungen: Brand allein zerstört kein Schiff (Hülle bleibt ≥ 1); geborgene Module zählen im Frachtraum wie neu (Wertabschlag nur über die Schrottgrenze); NPC bergen keine Module. Nach der Prüfung und dem ersten Gerätetest: Reparatur billiger (0,02 Hüllenplatten je Hüllenpunkt, NPC beheben auch mittlere Schäden, mittlere Hüllenquote KI 0,83–0,93), Notreparatur auf Schuld gegen den Soft-Lock, Upgrade/Verkauf beschädigter Module geregelt, Gefahr schaltet den Zeitraffer ab und zeigt eine große Warnung, Schiffe im Bildschirmraum antippbar, Testszenario als Startpaket; Verlustquote 0,25–0,61 %.

**Stand Phase 27:** umgesetzt (siehe UMSETZUNGSPLAN.md, Phase 27), Schalter `fleetCombat` (aus = Phase 26): Polizei und Piraten laufen über die Befehls-Ausführer — die Fraktion vergibt Streife (je Einsatzsektor, ab zwei Schiffen als Verband), Heimkehr zur Versorgung aus der Kasse, Geleit für wertvolle Fracht in bedrohten Sektoren und Verfolgung geflohener Kaperer (neuer Ausführer `pursue`, nur in der Zuständigkeit: eigenes Gebiet, verbündete Sektoren; Vorsprung schrumpft nur, wenn der Verfolger schneller ist oder das Ziel liegt); Piratengruppen schicken Rudel (Verband bis 2 Kaperer, Ausführer `lurk`, Überfallchance je Mitglied) und rufen sie heim. Laufende Gefechte bekommen Verstärkung (Eskorte, Wache, Verbands-/Rudelmitglied, Verfolger), Polizei, Begleiter und Rudel nutzen Fokusfeuer, Begleiter greifen zuerst den Angreifer ihres Schützlings an. NPC fliehen zu einer Zuflucht (Station mit nicht feindlichem Besitzer oder offenes Tor in höchstens 60 Einheiten, nicht versperrt durch einen näheren Gegner) und docken an bzw. springen durch das Tor. Der gesuchte Spieler (Stufe ≥ 3) wird nach der Warnung im echten Gefecht gestellt; er kann sich ergeben (Polizei: Strafe zahlen; Kaperer: halbe Fracht abwerfen) — vor oder im Gefecht. Abweichungen: Spielerflotte bekommt keinen Befehl „Verfolgen/Angreifen“; Verfolgung über die Grenze in verbündete Sektoren nur, wo die Beziehung ≥ 0,5 ist (Standardwelt: nie); der Vorsprung ist eine Zahl (keine Physik außerhalb des Spielersektors).

**Stand Phase 28:** umgesetzt (siehe UMSETZUNGSPLAN.md, Phase 28), Schalter `warfare`: Raketen als Waffe mit Flugzeit (dritte Schadensart Explosiv, eigenes Raketenmagazin, Ware „Rakete“), am Ziel Punktabwehr (ein Zug) und die gemeinsame Trefferregel mit dem Zieltempo (Ausweichen); Stationsverteidigung (Hülle, Schild, Panzerung, Geschütze, Punktabwehr je Stationstyp), Stationsgeschütze schießen in jedem Gefecht ihres Sektors auf Feinde; Belagerung als Gefechtsanlass (Kriegsschiffe, Spieler), Fall der Station: erobert (nur mit `allowStationCapture`), zerstört (über `Departures`, nie Regierungsstationen) oder ausgeschaltet; Stationsreparatur verbraucht Hüllenplatten; Fregatte „Bollwerk“ für Fraktionen nur im Krieg (Kauf aus Geld über der Rücklage, Unterhalt, Abbau nach dem Frieden); Krieg mit Kriegsgrund, Kriegskasse, Stärkevergleich, Kriegsmüdigkeit und Abkühlzeit — in der Standardwelt bricht keiner aus. Entscheidungen: Stationen versorgen ihre Geschütze selbst (keine Munition), Regierungsstationen werden ausgeschaltet statt zerstört (keine Regierungskrise), KI-Angreifer erobern für ihre Fraktion, der Spieler für sich selbst, Kriegsschiffe brechen durch Sperren. Offen: Schwarzmarkt, Tribut, Bündnisse, Regierungskrise, Gefechte zwischen Kriegsschiffen außerhalb von Belagerungen. Nacharbeit: Händler kaufen nicht mehr mit sicherem Verlust (Pleiten im Langlauf 0), Piraten-Unterhalt 0,03, Gefechtstest und Kriegstest als Startpakete, „Ergeben“ mit Grund; Verlustquote 0,18–0,36 %.

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
| 5 | Kampfschiffe für den Spieler | Ja; außerdem können sich Handelsschiffe wehren (Abschnitt 4.2a) — seit Phase 25: **jeder Händler bewaffnet**. Kaperschiffe bleiben gesperrt. |
| 6 | Händler überfallen | Ja, mit Fahndung. Ein Schwarzmarkt kommt später. **Umgesetzt:** Hehler im Piratenversteck, siehe [SCHWARZMARKT.md](SCHWARZMARKT.md). |

## Schwarzmarkt (umgesetzt)

Der offene Punkt „Schwarzmarkt“ ist umgesetzt: Hehler im Piratenversteck, kauft jede Ware ohne Blick auf die Fahndung zur Hälfte des Werts, verkauft die Beute der Kaperer zu 150 % des Basispreises. Entscheidungen und Begründung: [SCHWARZMARKT.md](SCHWARZMARKT.md).

## Kapitulation (umgesetzt)

Nutzerentscheidungen: „Übernehmen von Stationen mit Chance möglich, alle außer Piraten, wenn Hülle gering ist“, präzisiert zu „eher Kapitulation oder Station zerstört“ und „für alle, nicht nur Spieler“.

**Regel** (`Combat/Capitulation.cs`, Schalter `rules.stationCapitulation`, Code aus, Standardwelt an): Nicht der Angreifer entert — die **Station entscheidet**. Fällt ihre Hülle durch einen Treffer unter eine Schwelle (`capitulationThresholds` 25 % und 10 %), würfelt sie genau einmal (ein Zug aus `Universe.Rng`). Chance = Grundchance der Schwelle (`capitulationChances` 0,30 bzw. 0,55) − 0,10 je aktivem Verteidiger im Gefecht (Polizei, Kriegsschiffe) + 0,05 je weiterem Angreifer (höchstens 4) − (Aggression des Besitzers − 0,5) × 0,3, begrenzt auf 5–90 %. Ein Treffer über beide Schwellen würfelt zweimal. Kapituliert sie, gehört sie dem Angreifer: der Spielerperson bzw. der angreifenden KI-Fraktion, wenn diese im Krieg mit dem Besitzer ist (`aiStationCapture`, an). Sonst kämpft sie weiter und wird **bei Hülle 0 zerstört** (über `Departures`, Grund a, „siege“) — für alle Typen, auch Hauptquartier, Sektor-HQ, Polizeiwache, Forschung und Kleinstationen (vorher wurden Regierungsstationen nur ausgeschaltet).

**Piraten:** Stationen gesetzloser Besitzer und Verstecke kapitulieren nie. Verstecke werden bei Hülle 0 weiter nur **ausgeschaltet** (die Gruppe bleibt handlungsfähig, `NoHideout` kann nicht entstehen), Piratenstellungen wie bisher zerstört. Unsterbliche Stationen werden nur ausgeschaltet; Baustellen kapitulieren nicht.

**Nach der Kapitulation:** Besitzerwechsel, Geschütze schweigen (das Gefecht endet, weil die Station nicht mehr verteidigt), Hülle mindestens 5 %, Reparatur zahlt der neue Besitzer. Markt, Lager und **Kasse bleiben bei der Station** (sie ist ihr Betriebskonto und gehört jetzt dem neuen Besitzer). Die Sektorkontrolle wird sofort neu berechnet (der Einfluss zählt für den neuen Besitzer). Polizei/Kriegsschiffe, deren Heimatbasis die Station war, bekommen eine neue. Der Verlust zählt im Krieg (Kriegsmüdigkeit). Übernimmt der Spieler außerhalb eines Krieges, ist das das schwere Verbrechen „capture_station“ (Heat 5, Strafe 5000) bei der Fraktion des Besitzers und des Sektors; im Krieg keins. Weitere Treffer der neuen Seite schaden der Station nicht mehr.

**Sitz verloren:** Wird das Hauptquartier einer KI-Fraktion zerstört oder kapituliert es, entsteht sofort ein **Not-Sitz** (Hauptquartier-Typ) in ihrem Gebiet (Sektor mit den meisten eigenen Stationen, sonst ein herrenloser ohne Versteck); die Kasse zahlt höchstens 50 % der Baukosten, der Rest wird erlassen (ausdrückliche Geldquelle wie der Notkauf, Ereignis `SeatRelocated`). Findet sich kein Platz, versucht es die Handlungsfähigkeitsprüfung alle 100 Ticks erneut. Keine KI-Fraktion verliert dadurch ihre Handlungsfähigkeit.

**Der Spieler als Besitzer:** Übernommene Stationen zahlen ihren Unterhalt aus ihrer Kasse, was fehlt aus der Spielerkasse; Regierungsstationen zahlen Regierungsunterhalt aus der Spielerkasse (wie die Sektorbehörde aus Runde 45). Spielerbesitz verfällt nicht (keine Pleite, keine Stilllegung). Andocken und Dienste wie an jeder Station; die Station zählt für die Größenklasse und erscheint in „Meine Fraktion“. Gehört dem Spieler damit ein Sektor, setzt er Sperren über die Sektorpolitik (Runde 45).

**Anzeige:** „Station angreifen“ nennt beim Scharfschalten die nächste Schwelle mit Chance („Kapitulation möglich ab 25 % Hülle (Chance ≈ 35 %), sonst bei 0 zerstört.“ bzw. „Piratenstation — kapituliert nie.“); verfehlte Würfe als Hinweis („hält stand …“), die Kapitulation als Meldung („… kapituliert — die Station gehört jetzt dir. Andocken möglich.“). Keine eigene Taste.

**Messung** (Release, `--aiwar`, Kassen gesetzt; vorher = `--capitulation off`, Phase-28-Regel): Krieg Gilde → Union, 60 000 Ticks: vorher 7 Stationen zerstört, 1 Krieg (Müdigkeit), 3103 Belagerungen; nachher 10 kapituliert, 1 zerstört (17 Würfe, Ø Chance 0,43), Delta wechselt den Besitzer, die Gilde holt es in einem zweiten Krieg (Tick 40 900) zurück, Ende mit Tribut; 39 Belagerungen; alle Fraktionen handlungsfähig (Gilde 1,8 % der Proben unter dem Polizeiminimum), Preise im Zielbereich. Krieg Union → Montanbund, 200 000 Ticks: vorher 13 zerstört (darunter Kolonie und Farm Gamma im zweiten Krieg), nachher 11 kapituliert, 5 zerstört (28 Würfe, Ø 0,50), beide Kriege enden (Müdigkeit bzw. Tribut), 0 % Verstöße, Preise im Zielbereich. Die Standardwelt (kein Krieg) bleibt bit-gleich (20 000 Ticks F5F1965838C14B59).

## Ausgleich: Wirtschaftsstationen wehren sich

Botbefund: ein Zerstörer (≈ 7 900 Cr) nahm geschützlose Wirtschaftsstationen in 70–300 Ticks ohne Gegenwehr. Mit `economicStationDefense` bekommen Wirtschaftsstationen M/L/XL ohne eigenen `defense`-Abschnitt Hülle × 3, einen kleinen Schild, Panzerung 10 und 1/2/2 Laserkanonen, die nur auf Angreifer der Station selbst feuern; nach einer Kapitulation 480 Ticks Kapitulationssperre (IP2: 120 s). Einzelheiten in docs/STATIONEN.md „Ausgleich nach dem Bot-Test“. Regierungsstationen (Kriegstest gegen Sektor-HQ) bleiben unverändert.

## Wirtschaftsstationen halten stand (umgesetzt)

Nutzerwunsch „Wirtschaftsstationen stärken gegen Flotten“: trotz Vorgabeverteidigung nahm ein Zerstörer oder ein kleiner Verband eine Wirtschaftsstation in rund drei Minuten. Ziel: ein typischer Verband braucht mit Verteidigern sechs bis zehn Minuten, ein einzelner Zerstörer/Jäger nimmt eine M-Station in zehn Minuten gar nicht; ohne Hilfe darf ein klar überlegener Gegner schneller sein. Vier Hebel, alle datengetrieben (Code-Vorgabe = alte Regel, Standardwelt an; `Model/EconomyRules.EconomicHoldout.cs`, `Combat/EconomicHoldout.cs`):

1. **Alarm** (`economicStationAlarm`): Beginn der Belagerung und jeder Treffer halten den Alarm der Station 1 200 Ticks (`economicAlarmTicks`) aufrecht (`StationCondition.AlarmUntil`). Polizei und Kriegsschiffe der Besitzerfraktion und ihrer Verbündeten (Runde-45-Bündnisse) im Sektor und bis `economicAlarmJumps` (2) Sprünge entfernt — frei, nicht im Gefecht, ohne Versorgungsbedarf, Verbandsmitglieder folgen ihrem Anführer — bekommen eine Streife im Sektor der Station (`PoliceCommands.Step`, `NavySystem`; je Fraktion und Station höchstens `economicAlarmMaxResponders` 4, die nächsten nach Sprüngen, dann Id). Sie fliegen normal hin (kein Versetzen); im Sektor nimmt sie die Belagerungsverstärkung (`Siege.Reinforce`, jetzt auch mit den Verbündeten) ins Gefecht. Ereignis `StationAlarmRaised` (Zahl der Verteidiger); im Spielersektor Hinweis, bei eigenen Stationen auch anderswo.
2. **Notschild** (`economicEmergencyShield`): fällt die Hülle unter 50 %, hält die Station 600 Ticks (2,5 min, `economicEmergencyShieldTicks`) 75 % des Hüllenschadens ab, danach 2 400 Ticks Abklingzeit (`StationCondition.EmergencyUntil/EmergencyCooldownUntil`, Ereignis `StationEmergencyShield`, Stationszeile „· Notschild“). Regierungsstationen, Verstecke, Kleinstationen: keiner.
3. **Mehr Geschütze, schwerere Mischung, Punktabwehr** (`economicStationGuns` [3, 4, 5] innerhalb der Stellplätze der Größe, `economicStationGunList` Massetreiber, Laser M, Laser, Massetreiber, Laser M — leichte Laser allein richten gegen Panzerung 90 eines Zerstörers fast nichts aus; `economicStationPointDefense` [0, 0,2, 0,3] für M/L/XL). Die Geschütze feuern weiter nur auf Angreifer der Station selbst.
4. **Hülle × 5** (`economicStationHullFactor`, vorher 3): M 4 000, L 7 000, XL 12 500.

Alarm und Notschild greifen nur bei Angriffen auf Stationen — in einer Welt ohne Krieg ändert sich nichts.

**Messung** (`WirtschaftsabwehrTests.Kalibrierung_ZeitBisZumFall`: Kriegstest-Startpaket, Angriff wie am Handy — heranfliegen auf 0,7 × kürzeste Geschützreichweite ab Außenhaut, Zielhilfe, Dauerfeuer, Verband mit Regel „Angriff“ — auf die Farm Beta (M, abseits des Sektor-HQ), Kapitulation aus, also Zeit bis Hülle 0; „ohne Verteidiger“ = Polizei/Kriegsschiffe der Fraktion und ihrer Verbündeten entfernt, Nachkauf gesperrt; Saat 1):

| Angreifer | Verteidiger | vorher | jetzt |
|---|---|---|---|
| Zerstörer allein | mit (2 Streifen) | fällt nach 183 s | hält, Zerstörer nach 146 s zerstört (Station 62 %) |
| Zerstörer allein | ohne | fällt nach 198 s | hält, Zerstörer nach 439 s zerstört (Station 9 %) |
| Bollwerk allein | mit | hält (Bollwerk verloren) | hält, Bollwerk nach 92 s verloren (Station 86 %) |
| Bollwerk allein | ohne | fällt nach 802 s | hält, Bollwerk nach 312 s verloren (Station 63 %) |
| Zerstörer + 2 Hornissen | mit | fällt nach 108 s | hält, Zerstörer nach 229 s verloren (Station 44 %) |
| Zerstörer + 2 Hornissen | ohne | fällt nach 78 s | fällt nach 293 s |
| drei Bollwerke | mit | fällt nach 176 s | fällt nach 634 s |
| drei Bollwerke | ohne | fällt nach 105 s | fällt nach 544 s |

Mit Kapitulation (Standardwelt) kann die Station schon bei 25 % bzw. 10 % Hülle aufgeben; die Zeiten bis zur ersten Kapitulationsschwelle liegen entsprechend darunter. Offen: nur eine Saat gemessen; der Fall „Zerstörer allein ohne Verteidiger“ ist knapp (Station endet bei 9 %).

## KI-Kriegsschiffe: Seeschlacht, Abwehr, Geleit (Ausbau Punkt 2, umgesetzt)

Bis hier griffen KI-Kriegsschiffe im Krieg nur Stationen und Tore an; feindliche Kriegsschiffe flogen aneinander vorbei, und wer belagert wurde, wehrte sich nur mit dem, was zufällig im Sektor stand. Drei Regeln, je eigener Schalter (Code-Vorgabe aus = alte Regel, Standardwelt an; `Model/EconomyRules.WarNavy.cs`, `Combat/WarNavy.cs`). Ohne Krieg wirkt keine — die Standardwelt bleibt bitgleich (20 000 Ticks Release-Hash 48ECEC8C1781997D vorher wie nachher).

1. **Seeschlacht** (`navalBattles`, neuer Gefechtsanlass `EncounterCause.Naval` „Seeschlacht“, nach Namen gespeichert): `NavalBattleSystem` (Kampfmodul, alle 5 Ticks) prüft jedes Kriegsschiff (Id-Reihenfolge). Steht im selben Sektor auf höchstens `navalBattleRange` (400) ein Kriegs- oder Polizeischiff einer Fraktion, mit der seine Fraktion im Krieg ist (kein Waffenstillstand), beginnt das Gefecht mit dem nächsten Gegner. Polizei gegen Polizei beginnt keine Schlacht. Wer teilnimmt: frei, nicht angedockt, kein Notfall, nicht auf dem Heimweg, Hülle über der Rückzugsschwelle, ohne Versorgungsbedarf. Danach treten beim Beginn und alle `reinforceTicks` alle solchen Schiffe im Sektor bei, die einem Teilnehmer ihrer Seite auf `navalBattleRange` nahe sind — auf der Seite ihrer Fraktion oder eines Verbündeten im selben Krieg. Taktik: Kriegsschiffe als Begleiter (Fokusfeuer, Rückzug unter 30 % Hülle), Polizei als Streife — Rückzugsregeln wie bisher. Eine Seeschlacht dauert höchstens `navalBattleMaxTicks` (1 200). Die Auslösung zieht keine Zufallszahl; jeder Beitritt zieht wie immer genau zwei (Mut, Abwurfanteil). Ein in der Seeschlacht zerstörtes Schiff zählt mit seinem Materialwert × `navalLossShare` (1) als Kriegsverlust seiner Fraktion (Müdigkeit, Tribut).
2. **Gezielte Abwehr** (`warDefense`): Eine Fraktion schickt freie Kriegsschiffe gegen feindliche Verbände, die *gerade* ihre Stationen oder Tore angreifen — ein Kriegsschiff eines Kriegsgegners mit Angriffsbefehl, das schon im Sektor des Ziels steht, oder eine laufende Belagerung (auch durch den Spieler im Krieg). Die nächsten nach Sprüngen (höchstens `warDefenseJumps` 3, dann Id), höchstens `warDefenseMaxResponders` (3) je Ziel. **Rücklage:** hat die Fraktion mindestens zwei Kriegsschiffe, bleiben `warDefenseReserve` (1) bei ihrem Auftrag (Angriff, Geleit); ein einzelnes verteidigt. Die Abwehr geht dem eigenen Angriff, dem Geleit und dem Wirtschaftsalarm vor; sie ist eine Streife im Sektor des Ziels (normaler Flug), dort nimmt die Belagerungsverstärkung (`Siege.Reinforce`) bzw. die Seeschlacht das Schiff ins Gefecht. Ereignis `WarDefenseAssigned`. Erst Angreifer im Zielsektor zählen — eine Abwehr gegen Angreifer, die noch unterwegs sind, verhinderte in der ersten Messung jede Belagerung.
3. **Geleit im Krieg** (`warEscorts`): je Fraktion im Krieg begleiten bis zu `warEscortsPerFaction` (1) Kriegsschiffe den wertvollsten beladenen KI-Händler (Fracht ≥ `warEscortMinValue` 1 500 Cr zu Basispreisen, unterwegs, noch nicht begleitet; der Fraktion oder ohne Fraktion, dessen Route eigenes Gebiet berührt), dessen restliche Route durch einen feindlichen oder an feindliches Gebiet grenzenden Sektor führt, höchstens `warEscortJumps` (2) Sprünge entfernt. `EscortCommand` (Begleiter treten jedem Gefecht des Schützlings bei). Ende bei Ankunft, Frieden (Heimkehr) oder nach `warEscortMaxTicks` (4 800); danach nimmt das Kriegsschiff wieder Ziele.

**Messung** (Balance, 60 000 Ticks, Kasse 2 Mio. Cr je Kriegspartei bei Tick 3 980, `--aiwar` bei Tick 4 000; „aus“ = `--navy off`, gleich dem Stand vorher):

| Lauf | Regeln | Kriege (Frieden) | Seeschlachten (zerstört) | Kriegsschiffe zerstört | Belagerungen | Stationen gefallen | Abwehr / Geleit | Verlustquote | Handlungsfähig |
|---|---|---|---|---|---|---|---|---|---|
| 2 → 1 | aus | 1 (Tribut bei 10 000) | 0 | 4 | 1 073 | 0 | 0 / 0 | 0,16 % | Kompanie 6,6 % Verstoß (Polizei unter Minimum) |
| 2 → 1 | an | 1 (Tribut bei 10 000) | 11 (8) | 5 | 1 | 0 | 2 / 6 | 0,17 % | alle 0 % |
| 1 → 3 | aus | 2 (Tribut 10 000, 17 600) | 0 | 9 | 5 453 | 0 | 0 / 0 | 0,26 % | alle 0 % |
| 1 → 3 | an | 2 (Tribut 10 000, 17 200) | 21 (16) | 15 | 1 982 | 0 | 10 / 11 | 0,22 % | alle 0 % |

„Belagerungen“ zählt jeden Belagerungsbeginn — vorher meist dieselben Kriegsschiffe, die vor den Stationsgeschützen flohen und sofort neu ansetzten. Mit den Regeln treffen Angreifer unterwegs auf Kriegsschiffe und Streifen; Kriege enden wie vorher über den Tributfrieden (im Lauf 1 → 3 erklärt die Föderation danach wie vorher von sich aus der Kompanie den Krieg), alle Fraktionen bleiben handlungsfähig, Preise im Zielbereich. Kapitulationen: keine (vorher wie nachher).

**Offen:** Kriegsschiffe verlieren mehr Schiffe (Seeschlachten), Stationen fallen in diesen Läufen weiterhin nicht (Wirtschaftsstationen halten stand). Geleit gilt nur KI-Händlern, nicht der Spielerflotte. Keine Seeschlacht gegen den Spieler (dafür gibt es den Anlass „Krieg“ nach Vorwarnung). Gemessen nur eine Saat je Kriegspaar.

### Nachtrag 2b: Rücklage zählt kämpfende Kriegsschiffe, Abkühlzeit je Gegnerpaar (umgesetzt)

- **Rücklage ehrlich** (`warDefenseCountsEngaged`, Code-Vorgabe aus, Standardwelt an): Kriegsschiffe, die schon als Verteidiger in der Belagerung eines eigenen Ziels kämpfen (aktiv, Seite 1), zählen gegen die erlaubte Zahl (alle Kriegsschiffe minus `warDefenseReserve`) **und** gegen `warDefenseMaxResponders` dieses Ziels. Vorher zählten nur freie Schiffe: ein kämpfendes fiel aus der Rechnung, und die Abwehr schickte die Rücklage mit (Beispiel: vier Kriegsschiffe, eins kämpft schon, Rücklage 1 → jetzt zwei freie zur Abwehr statt drei).
- **Abkühlzeit** (`navalBattleCooldownTicks`, Code 0 = aus, Standardwelt 600): endet eine Seeschlacht, beginnt dasselbe Fraktionspaar im selben Sektor für 600 Ticks keine neue (`Universe.NavalEnded`: Sektor + Paar mit kleinerer Id zuerst → Tick; gespeichert additiv, gehasht nur wenn vorhanden; abgelaufene Einträge räumt der Auslöser weg). Vorher entbrannte sie im nächsten Takt neu, sobald die Rückzügler wieder einsatzbereit waren. Andere Paare und andere Sektoren sind frei; Verstärkung einer laufenden Schlacht ist nicht betroffen.
- Ohne Krieg wirkt keins von beiden. Balance-Option `--navycool off` (beide aus).

**Messung** (60 000 Ticks, Kasse 2 Mio. Cr je Kriegspartei bei Tick 3 980, Krieg bei 4 000, Standardwelt mit Verschleiß; „aus“ = `--navycool off`, eine Saat): 2 → 1: Seeschlachten 8 → 9 (zerstört 7 → 10), Kriegsschiffe zerstört 2 → 6, Abwehrbefehle 2 → 3, Belagerungen 2 → 3, Verlustquote 0,31 → 0,25 %; 1 → 3: Seeschlachten 12 → 13 (11 → 10), Kriegsschiffe zerstört 8 → 10, Abwehrbefehle 10 → 8, Belagerungen 4 → 7, Verlustquote 0,22 → 0,15 %. Keine Station fällt, Preise im Zielbereich, alle Fraktionen handlungsfähig (Kompanie in den „an“-Läufen 0,4 % der Proben kurz unter dem Polizeiminimum). Die Unterschiede liegen im Rauschen einer Saat — die Regeln verhindern das sofortige Neuansetzen und die mitgeschickte Rücklage (Tests), ändern die Kriegsbilanz aber nicht messbar.

## NPC-Bergung (Ausbau Punkt 4, umgesetzt)

Bis hier bergen nur Kaperer die Fracht frischer Wracks (Phase 26) und der Spieler per Greifer; Module aus Wracks verfallen ungenutzt. Jetzt räumt die KI selbst auf: Bergeschiffe der KI fliegen zu Wracks, holen, was sich lohnt, und verkaufen es in den normalen Wirtschaftskreislauf zurück. Zwei Schalter (Code-Vorgabe aus = alter Stand, Standardwelt an; Regeln in `Model/EconomyRules.Salvage.cs`): `npcSalvage` (Hauptschalter) und `maxSalvagers` (Flottengrenze der Aufgabe, Standardwelt 4). Aus = nichts passiert: `SalvageSystem` tut keinen Schritt und die Aufgabe „salvage“ hat Grenze 0, die Flotte kauft keine Bergeschiffe.

1. **Schiffsmodell** `salvager` „Bergeschiff“ (Klasse `light_freighter`, Werks-Fit: Reaktor Mk1, Chemtriebwerk M, Bergungsgreifer S, Laser S, 2 × Universal-Frachtraum 100 = 200, ein Nut-Slot frei; Aufgabe `["salvage"]`). Die Werft verkauft alle nicht gesperrten Klassen — also auch das Bergeschiff (die Handelsstation bleibt bei Skiffs). Kauf über die normale Flottenerweiterung (`FleetSystem`, Grenze `maxSalvagers`, 1 Einheit je Schiff; Start über die Fraktionskassen, sobald die Werft Bausatz und Module liefern kann).
2. **Aufgabe** `salvage` (`Simulation/SalvageJob.cs`, registriert in `ShipJobs.Default` nach „gas“): Etikett `ShipRole.Salvager`, braucht die Fähigkeit `Salvage`, 1 Einheit je Schiff, Kaufwert = Laderaum × Tempo wie beim Handel. Bergeschiffe zählen als Wirtschafts-KI (Abgänge und NPC-Reparatur wie die Sammler).
3. **Ausführung** `SalvageSystem` (Ressourcen-Modul nach `GasCollectorSystem`, Intervall 10, deterministisch in Id-Reihenfolge, kein `Universe.Rng`): Das Bergeschiff fliegt zum nächsten erreichbaren Wrack mit bergbarem Gut — Fracht, die in den Laderaum passt, oder Module ab `salvageMinCondition` (0,5; Schrott bleibt liegen) — und holt per `Wrecks.TakeCargo`/`Wrecks.TakeModule` alles, was passt. Danach verkauft es die Beute: Modulware an die beste Abnehmerin der wertvollsten Modulware (`TradeActions.BestBuyerFor` — Werft/Ausrüster), sonstige Ladung an den besten Abnehmer (`TradeActions.BestBuyer`), Rest an den jeweils nächsten Abnehmer, unverwertbarer Rest über Bord (wie beim Schürfer). Reisen über `Travel.Start`/`Refuel.PrepareTrip` wie der Gassammler; der Auftrag ist ein `MineOrder`, dessen Feldnummer die Wrack-Id trägt (−1 = nur ausliefern) — kein neues Speicherformat. Ohne Wrack wartet das Schiff und denkt später neu.

**Persistenz:** additiv (Save bleibt v17) — keine neuen Felder; das Modell steht in `SaveGameJson.AfterV17Keys`, die Regeln werden in der Migrationsbasis entfernt. Rücknahme für Referenz-Hash-Tests: `BergungTests.RevertSalvage` (Schalter aus, Grenze 0, Modell und Gründer entfernt), eingehängt in `BalanceFixTests.RevertBalanceFix` und `GasNebulaTests.RevertGas`.

**Tests** `BergungTests` (4): Schalter Code aus / Standardwelt an / Rücknahme; Inhalt (Modell, Aufgabe, Werft verkauft es, Handelsstation nicht, Rollen-Fähigkeiten); KI-Bergeschiff leert ein Wrack (Modul und Fracht geborgen, `WreckSalvaged`) und verkauft die Modulware mit Erlös (`MineDelivered`); Schalter aus = Wrack unberührt, Schiff untätig; Speichern/Laden bit-identisch inkl. 600 Ticks Weiterlauf.

## Konvois (Ausbau Punkt 3) und Nachtrag 5b

**KI-Konvois** (Schalter `convoys`, Code aus, Standardwelt an; `Simulation/Convoys.cs`): Ein beladener KI-Händler (frei oder Mitglied der Sektorfraktion, ohne Befehl) wartet nach dem Einkauf an der Station, wenn das Routenrisiko seiner Verkaufsfahrt (`ThreatMap.RouteRisk`) mindestens `convoyRouteRisk` (Standardwelt 0,006) ist und eine freie Streife der Sektorfraktion im Sektor ist. Gesammelt wird nur, wenn mindestens `convoyMinTraders` − 1 weitere Händler derselben Strecke (Fraktion, Start-, Zielsektor) innerhalb der Frist dort einkaufen (unterwegs zum Einkauf mit Ankunft in der Frist oder schon da) — sonst fährt der Händler sofort (kein Warten auf Verdacht); kommt niemand mehr, fährt der Konvoi sofort los; bei `convoyMaxTraders` (5) oder spätestens nach `convoyWaitTicks` (120) fahren alle zugleich los. Ab `convoyMinTraders` (3) bekommt der Konvoi 1 (bei mehr als 3 Händlern bis `convoyMaxEscorts` 2) Polizeischiffe als Geleit (`EscortCommand` auf den ersten bzw. letzten Händler; kommt der begleitete an, übernimmt die Streife den nächsten noch fahrenden), sonst fahren sie ohne Geleit. Das Geleit steht bei Überfällen jedem Konvoihändler im Sektor bei (`FleetGuards.Escorts`). Der Konvoi löst sich auf, wenn alle angekommen sind (spätestens nach `convoyTicks` 2400). Kein Zufall, feste Reihenfolge. Gespeichert und gehasht nur, wenn Konvois bestehen. Wartende Händler liegen an der Station (Sicherheitszone), der Auftrag bleibt.

**Nachtrag 5b — Piraten und Bomber.** Mit den Schmugglern (Punkt 5) wurden die Piraten durch Hehlergeld reich und bauten Bomber (zwei Raketenwerfer, Klasse S); die Polizeiverluste stiegen. Auf main mit den Banditen (Punkt 17) war das im Standardlauf nicht mehr zu sehen (300 000 Ticks: Polizei zerstört 2, Piraten nur Korsaren), kehrte aber mit dem veränderten Weg durch die Konvois zurück (Polizei zerstört 14, 4 Piratenbomber). Hebel (Daten): `pirateModelCostFactors` — Aufschlag je Modell auf den Piratenbau im Versteck (`CombatModels.PirateCost`; Standardwelt `bomber` ×3, Polizei und Marine unberührt). Zusätzlich als Schalter `smugglerFencePirateShare` (Anteil des Hehler-Einkaufs der Schmuggler, der in die Piratenkasse geht; Rest Geldsenke; Code und Standardwelt 1 — nicht nötig, die Kasse ist ohnehin durch `pirateTreasuryCap` gedeckelt).

**Nachtrag 6b — Fehdebeute und Magazinregale.** (a) Ein Fehdeüberfall schrieb `FeudSeizeShare` × Frachtwert der Kasse des Angreifers als Einnahme „feud“ gut, ohne Gegenbuchung (neues Geld). Mit `feudSeizeAsGoods` (Code aus, Standardwelt an) wird stattdessen der Anteil der abgeworfenen Ware real ins Lager der Angreifer gebracht (Heimatbasis des Schiffs, sonst erste eigene Station mit der Ware im Markt; sonst verloren; Ereignis `FeudGoodsSeized`) — kein Geld entsteht. (b) Das Munitionsmagazin (und die Punktabwehr) sanken in Kriegsläufen auf ≈ 0,30× Basis: die Werften hatten Regalziel 2, das die Regel „ungenutzte Regale“ auf 1 senkte (dann Rabattpreis), und das Systemwerk hatte nach der Kriegsnachfrage 13 bei Ziel 5 auf Lager. Hebel (Daten): Werftregal `[2, 3]` statt `[1, 2]`, Systemwerk eigener Eintrag `[5, 8]` für beide.
