# Flotten, Befehle, Patrouillen, Polizei (Planung)

**Status:** Lagekarte, Polizei, Fahndung, Piraten und abstrakte Begegnungen sind seit Phase 11 umgesetzt; Befehle mit Ausführern (Ebenen Notfall > Befehl > Automatik), Verbände, Verhaltensregeln, Befehlskapazität und die Spielerflotte mit Automatikaufträgen seit Phase 12 (siehe UMSETZUNGSPLAN.md, Abschnitt Phase 12). Offen: Polizei/Piraten über die Befehls-Ausführer, Blockade, Schleppen, echter Kampf. Baut auf [POLITIK.md](POLITIK.md) (Kontrollpunkte, Sperren) und [SCHIFFE.md](SCHIFFE.md) (Fähigkeiten, Aufgaben) auf.

## 1. Grundidee: ein Fundament für eigene Flotten und Polizei

Die Frage „eigene Flotten oder Polizeisystem?" ist **kein Entweder-oder**. Beide brauchen dasselbe:

- **Verbände** (mehrere Schiffe, ein Befehl),
- **Befehle** mit Ausführung und Verhaltensregeln,
- ein **Stärkemodell**, um Begegnungen aufzulösen,
- eine **Lagekarte** (wo ist es unsicher).

Der Unterschied liegt nur darin, **wer die Befehle gibt**:

| Betreiber | Gibt Befehle | Beispiel |
|---|---|---|
| Spieler | von Hand, über die Flottenoberfläche | „Eskortiere diesen Frachter", „Schürfe Eisen in Sektor B" |
| Fraktions-KI (Polizei) | ein Sicherheitssystem nach Lage und Budget | Patrouillen, Gate-Wache, Abfangen |
| Piraten (Gegenfraktion) | ein Raubsystem | Überfälle auf unbewachte Routen |

Daraus folgt für die Architektur: **Polizei ist keine Sonderlogik, sondern eine Fraktions-KI, die Verbänden Patrouillen- und Wachbefehle gibt**, mit denselben Befehlen, die der Spieler benutzt.

## 2. Bestehendes

`Ship.Order` ist ein `ShipOrder`-Record; es gibt bereits `TradeOrder`, `MineOrder`, `PatrolOrder(SectorId)` und `GoToOrder(StationId)`. Die beiden letzten werden noch nicht ausgeführt. `OrderSave` speichert Befehle als `Kind` mit drei Ganzzahlen. `Faction` hat `Relations` und `Territory`, aber keine Kasse. Der Name `FleetSystem` ist schon vergeben (er kauft Schiffe), deshalb heißt die neue Einheit **Verband** (Begriff in allen Dokumenten; „Flotte“ meint die Gesamtheit der Schiffe eines Akteurs).

## 3. Verband

Ein Verband ist eine Gruppe von Schiffen mit einem **Anführer**, einer Formation und einem gemeinsamen Befehl.

- **Planung einmal:** Der Anführer plant die Reise (`Travel.Plan`), die Mitglieder folgen mit Versatz. Das spart Rechenzeit, auch bei vielen Schiffen.
- **Bestand:** Neues Modell `Squadron` (Id, Besitzer, Anführer, Mitglieder, Befehl, Regeln). `Ship.Squadron` verweist darauf. Bewegung läuft weiter über `Universe.AddShip/StartTransit/FinishTransit`.
- **Tempo:** Der Verband fliegt mit dem langsamsten Mitglied.
- **Zusammensetzung:** Er braucht passende Fähigkeiten (Patrouille braucht `Combat`, Eskorte `Combat` und ein Begleitziel). Gemischte Verbände (ein Frachter mit Eskorte) sind erlaubt.
- **Auflösung:** Fällt der Anführer aus, übernimmt das nächste Mitglied; fehlt der Treibstoff, kehrt der Verband um.

## 3a. Referenz: Interstellar Pilot 2 und Verbände (entschieden)

Befund aus `~/ip2-decomp/docs/04-flotten-und-befehle.md` (Dekompilat-Analyse, zeigt Klassen und Felder, kaum Regeln) und dem Quellcode von `InterstellarPilot-OpenFrontier`:

- Die Flotte ist ein eigenes Objekt (`ModelFleet`) mit Heimatbasis, Strategie, Formation und Befehlsliste. Das Flag `ExcludeFromFactionAI` nimmt sie aus der Fraktions-KI.
- Limits: `Fleet.MaxFleetPilots = 8` (vermutlich Obergrenze je Flotte, nicht ausdrücklich dokumentiert), je Fraktion `MaxFleetUnitCount` und für KI-Gruppen `MaxGroupUnitCount`.
- Befehle: Bewegen, Patrouille, Handel (autonom und manuell), Abbauen, Plündern, Angreifen, Schützen, Wiederbewaffnen, Reparieren, Schleppen, Station bauen, Schiff verkaufen, Flotte beitreten, Erkunden, Passagiere, Kopfgeld, Heimkehr, Warten.
- Formationen: sechs feste Entwürfe (Linie, V, Stern, Box).
- „Hyperschlaf“: Flotten führen Befehle aus, während der Spieler weg ist; bei uns entspricht das `OfflineCatchUp`.
- Nicht belegt: ob und zu welchen Kosten der Spieler Flotten frei anlegen darf.

### Frei anlegen, aber begrenzt

**Frei** (kostenlos): Verbände anlegen, benennen, auflösen, zusammenführen; Schiffe zuordnen (`JoinFleet`); Befehle geben, soweit die Fähigkeiten reichen; Formation wählen (nur Darstellung ohne Wirkung auf die Simulation).

**Begrenzt:**
1. **Mitglieder je Verband:** höchstens 8 (Richtwert wie in der Referenz; Kapital- und Frachtverbände können später eine eigene Grenze bekommen). **Warum:** hält Rechenzeit und Bedienung klein.
2. **Befehlskapazität des Akteurs** (Anzahl Verbände und Schiffe) hängt von der Größe ab (Büro, Firmensitz, Hauptquartier, siehe POLITIK.md). **Warum:** die Obergrenze ist politisch und wirtschaftlich begründet.
3. **Heimatbasis:** Jeder Verband braucht eine eigene Station zum Tanken und Reparieren. **Warum:** sonst wären Verbände kostenlos und ohne Versorgungsweg.
4. **Unterhalt** für jedes Schiff, auch im Verband.
5. **Befehle nach Fähigkeit:** `Patrol`, `Escort`, `Blockade` setzen `Combat` voraus, `Mine` setzt `Mining` voraus. Gemischte Verbände (Frachter mit Eskorte) sind erlaubt; ein Befehl gilt für die Mitglieder, die ihn können, die übrigen folgen dem Anführer.
6. **Automatikauftrag:** Ein Verband ohne Befehl arbeitet im Rahmen seiner Fähigkeiten über das `JobAssignmentSystem`, mit Reichweite und Budget als Grenzen. Das ist das Gegenstück zu `ExcludeFromFactionAI`.

### Übernommen aus der Referenz

| Aus der Referenz | Bei uns |
|---|---|
| Heimatbasis je Flotte | ja |
| Autonome Befehle (Handel, Passagiere, Kopfgeld) | ja, als Automatikaufträge mit Grenzen |
| `Tow`, `Rearm`, `Repair` | ja, mit Rettungsdienst und Stationsdiensten |
| `Scavenge`, `ClaimUnit` | später, mit Wracks |
| `Explore` | später, wenn Sektoren verborgen sind |
| `JoinFleet` | ja |
| Feste Formationen | nur Darstellung |
| Persönlichkeitswerte der Fraktionen | ja, siehe POLITIK.md, Abschnitt 6 |
| Banditen-Fraktionen | ja, als Piraten |

## 4. Befehle

Befehle werden zu einer erweiterbaren Familie mit **Ausführern**, die man registriert (wie Systeme): `IOrderExecutor` pro Befehlstyp. Neue Befehle sind neue Ausführer, nicht neue Verzweigungen in zentralen Klassen.

| Befehl | Wirkung | Fähigkeit |
|---|---|---|
| `GoTo` (Ziel) | Fliegen, andocken | – |
| `Hold` | Position halten | – |
| `Patrol` (Sektoren, Route) | Runde fliegen, Kontrollen und Scans ausführen | `Combat` |
| `Guard` (Station, Gate, Sektor) | Bereich bewachen | `Combat` |
| `Escort` (Schiff oder Verband) | Begleiten und verteidigen | `Combat` |
| `Follow` | Folgen | – |
| `Trade` (Auftrag oder automatisch) | wie heute | `Cargo` |
| `Mine` (Gürtel, Ziel) | wie heute | `Mining` |
| `Build` (Baustelle) | Material liefern (siehe SCHIFFE.md) | `Construction` |
| `Blockade` (Gate) | Zugang verhindern | `Combat` |
| `Return` | Zur Heimatbasis, tanken, reparieren | – |

### Ebenen

Ein Schiff folgt immer der **höchsten dringenden Ebene**:

1. **Notfall** (Treibstoff leer, Hülle kritisch, Flucht): unterbricht alles.
2. **Befehl** (von Spieler oder Fraktion): gilt, bis er beendet oder ersetzt wird.
3. **Automatik** (`JobAssignmentSystem`): wählt nach Gewinn eine Aufgabe unter den Fähigkeiten, wenn kein Befehl vorliegt.

Das trennt Handbefehle von Autonomie und lässt NPCs und Spielerflotte dieselbe Maschine benutzen.

### Verhaltensregeln (Rules of Engagement)

| Regel | Bedeutung |
|---|---|
| `Passiv` | keine Waffen, flieht |
| `Verteidigen` | feuert nur, wenn angegriffen oder ein Schützling angegriffen wird |
| `Angriff` | greift Feinde und gesuchte Schiffe an (nur Fraktionen im Krieg oder Verbrecher) |
| `Feuer halten` | nie feuern, auch zur Selbstverteidigung (Schmuggel, Tarnung) |

### Speichern

`OrderSave` (`Kind`, A, B, C) trägt die neuen Befehle weiter, soweit drei Ganzzahlen reichen. Reicht das nicht, wächst das DTO um eine Liste. Dazu kommen `Squadron`-Liste und `Ship.Squadron`. Save-Version erhöhen, `UniverseHash` ergänzen.

## 5. Eigene Flotten des Spielers

- **Besitz:** Schiffe gehören einer Fraktion; für den Spieler braucht es die Spielerfraktion (POLITIK.md). Gekaufte Schiffe (`BuyShip`) sind ihre Flotte.
- **Bedienung auf dem Handy:** Eine Flottenliste (Schiff oder Verband wählen), ein Menü mit den wenigen Befehlen aus der Tabelle, Ziel per Tipp auf die Karte. Nicht mehr als drei Schritte bis zum Befehl.
- **Kosten:** Unterhalt (`UpkeepSystem`), Treibstoff, Reparatur. Die Flotte kostet laufend Geld, das sie durch Handel, Bergbau oder Zölle einspielen muss.
- **Automatik:** Ein Schiff ohne Befehl arbeitet im Rahmen seiner Fähigkeiten (Händler handelt, Schürfer schürft) und liefert Gewinn an den Spieler. Der Spieler gibt nur Richtung und Grenzen vor (Reichweite, Budget).
- **Ansicht:** Außerhalb des Spielersektors keine Physik, wie bei NPCs. Im Spielersektor zeigt `Materializer` die Verbände.

## 6. Polizei (Sicherheit der Fraktion)

### Lagekarte

Jede Fraktion führt pro Sektor ein **Sicherheitsniveau** und eine **Bedrohung**:

- Die Bedrohung steigt durch Vorfälle (Überfälle, Schmuggel, fremde Schiffe im Sperrgebiet) und fällt durch Patrouillen.
- Eine Statistik wie `EconomyStats` zeigt die Lage; sie ist zunächst reine Beobachtung.

### Zuweisung

Ein **`SecuritySystem`** verteilt die verfügbaren Polizeischiffe nach Bedrohung × Wert des Sektors (Handelsvolumen) auf Patrouillen- und Wachbefehle. Wie `FleetSystem` zieht es nach, wenn die Lage schlecht ist und die Kasse es erlaubt, und baut ab, wenn nichts passiert (Unterhalt). Polizeischiffe haben eine **Heimatstation** zum Tanken und Reparieren.

### Aufgaben

| Aufgabe | Mechanik |
|---|---|
| Scan | Kontrollpunkt-Prüfung (POLITIK.md) im Sektor, Entdeckungswurf gegen Tarnfeld |
| Zoll | Zahlung bei `Toll`-Politik; Einnahme für die Kasse |
| Abfangen | Schiffe mit Schmuggelware oder gesuchte Schiffe |
| Eskorte | Begleitschutz für Handelsrouten und Konvois |
| Gate-Wache | Gate bewachen, Sperren durchsetzen |

### Verbrechen und Fahndung

- Schiffe und Spieler haben pro Fraktion ein **Fahndungsniveau** („Heat"). Es steigt durch Verbrechen und fällt mit der Zeit.
- Folgen nach Stufen: Scan und Bußgeld, Beschlagnahme, Angriff auf Sicht, Kopfgeld.
- **Kopfgeld** zahlt die Kasse der Fraktion; Jäger (Spieler oder NPC) holen es sich. Das ist eine eigene Rolle.

### Piraten

Eine **Gegenfraktion** (Ausgestoßene) mit Räubern, die ihr Revier nach Beute wählen: unbewachte Routen mit wertvoller Fracht. Sie schwächen die Wirtschaft (verlorene Fracht), erzeugen Bedrohung und damit Nachfrage nach Polizei und Eskorten.

### Was die Polizei kann (entschieden)

| Fähigkeit | Was genau | Voraussetzung / Grenze |
|---|---|---|
| **Wahrnehmen** | Schiffe im Sektor, an Gates und Stationen entdecken | Sensorstärke gegen Tarnstärke; Treffer als Wurf über `Universe.Rng` |
| **Kontrollieren** | Fracht prüfen, Zoll kassieren, Lizenzen kontrollieren | nur an Kontrollpunkten oder nach dem Abfangen |
| **Abfangen** | Schiff stoppen, Frachtprüfung erzwingen | Schiff im Sektor, Patrouille in Reichweite, Stärke reicht |
| **Bestrafen** | Bußgeld, Beschlagnahme, Festsetzung, Kopfgeld | nur im Hoheitsgebiet und nach Fahndungsstufe |
| **Schützen** | Patrouille, Eskorte, Gate- und Stationswache | begrenzt durch Schiffszahl und Heimatbasis |
| **Durchsetzen** | Sperrstufen, Blockade, Sprungsperre | nur bei Kontrolle über den Sektor |
| **Helfen** | Havarierte Schiffe schleppen, Liegenbleiber retten | Rettungsdienst (Station, siehe STATIONEN.md) |

**Grenzen:** Die Polizei handelt nicht außerhalb des Hoheitsgebiets (nur Verträge oder Rechtshilfe), sieht nicht alles (Tarnfelder, fehlende Sensorabdeckung), ist nicht überall (jedes Schiff kostet Unterhalt und hat eine Heimatbasis) und straft nicht unbegrenzt (Beschlagnahmen belasten die Wirtschaft). **Warum:** Eine Polizei, die jedes Schiff immer entdeckt und überall wirkt, würde Schmuggel und Tarnung wertlos machen.

**Verbrechen und Folgen:**

| Verbrechen | Beispiel | Folge (Fahndungsstufe) |
|---|---|---|
| Zollhinterziehung | Gate ohne Zahlung passiert | Bußgeld (2) |
| Schmuggel | verbotene Ware an Bord | Beschlagnahme, Festsetzung (3) |
| Gebietsverletzung | gesperrten Sektor betreten | Abweisung, Bußgeld, Fahndung (2–3) |
| Flucht vor Kontrolle | Abfangen ignoriert | Fahndung erhöht, Angriff auf Sicht (4) |
| Piraterie | Frachter überfallen | Kopfgeld, Angriff auf Sicht (4) |
| Zerstörung | Schiff oder Station zerstört | Kopfgeld, Haftbefehl (4) |
| Unlizenzierte Tarnung | Tarnfeld im Hoheitsgebiet ohne Lizenz | Bußgeld, Beschlagnahme des Moduls (2–3) |

**Fahndungsniveau („Heat“):**

| Stufe | Name | Verhalten der Polizei |
|---|---|---|
| 0 | sauber | normal |
| 1 | auffällig | öfter gescannt |
| 2 | verwarnt | Bußgeld bei Kontrolle |
| 3 | gesucht | Abfangen, Beschlagnahme, Festsetzung |
| 4 | Staatsfeind | Angriff auf Sicht, Kopfgeld |

Das Niveau steigt durch Vorfälle und **fällt mit der Zeit** (Verfall), schneller bei bezahltem Bußgeld. Es gilt **je Hoheitsgebiet**, nicht global. Optional später: Bestechung gegen Gebühr mit Erfolgschance (bei Misserfolg steigt das Niveau), als Gegenmittel für Schmuggler.

**Simulation:** Kontrollen laufen an Kontrollpunkten ereignisbasiert (Durchflug, Andocken), Patrouillen würfeln in einem Intervall (LOD), wie oft sie ein Schiff im Sektor kontrollieren; die Rate hängt von Streifenzahl, Sektorgröße und Sensorstärke ab. Die Reaktionszeit ist die Flugzeit (`Travel.Estimate`) der nächsten Patrouille zum Vorfall; wer schneller wegkommt (Sprungantrieb), entkommt. Alles deterministisch über `Universe.Rng`.

### Polizeistufen, Zuständigkeit, Finanzierung

Die Stationsformen (Polizeiwache, Patrouillenbasis, Sektor-HQ) stehen in [STATIONEN.md](STATIONEN.md), Abschnitt 5; die Kontrollstufen in [POLITIK.md](POLITIK.md), Abschnitt 8.

| Stufe | Wer | Aufgabe |
|---|---|---|
| **Wache** (Stationssicherheit) | jede Station mit Dienst `police` | Zoll, Scan beim Andocken, Festsetzen, Kopfgeldbüro |
| **Sektorpolizei** | Sektor-HQ mit Patrouillenbasis | Patrouille, Gate-Kontrolle, Abfangen im Sektor |
| **Flottenpolizei / Marine** | Hauptquartier | Konvois, Verfolgung über Sektoren, Gegen-Piraten, Sperrdurchsetzung |

**Zuständigkeit:** Polizei handelt nur im eigenen Hoheitsgebiet.

| Gebiet | Befugnis |
|---|---|
| Eigener Sektor (Kontrolle) | volle Polizeigewalt |
| Eigener Sektor (nur Dominanz) | Scan, Zoll, Festnahme bei Haftbefehl |
| Verbündeter Sektor | Rechtshilfe, Verfolgung bis zur Grenze |
| Neutral oder umkämpft | nur Schutz eigener Konvois; Verträge möglich |
| Feindlicher Sektor | keine Befugnis |

**Warum:** Die Zuständigkeit verhindert, dass eine Fraktion überall Recht durchsetzt, und macht Grenzen spielerisch relevant.

**Finanzierung:** Zölle, Strafen und Steuern speisen die Fraktionskasse. Polizeischiffe verursachen Unterhalt, Treibstoff und Reparaturen und erzeugen damit echte Nachfrage nach Hüllenplatten und Treibstoff. **Warum:** Polizei als Kostenstelle begrenzt sie von selbst, denn sie wächst nur, wenn die Kasse es hergibt.

**Schutzverträge:** Kleinere Akteure ohne eigene Polizei können Sicherheit **mieten** (Söldner oder Schutz einer größeren Fraktion), gegen laufende Zahlung. **Warum:** Kleine bekommen einen Weg, ihre Stationen zu schützen, und es entstehen Beziehungen, die verschwinden, wenn man nicht zahlt.

**Fahndung** gilt je Hoheitsgebiet; Auslieferung nur unter Verbündeten.

### Verbände nach Größe

Die Zahl der Verbände und Schiffe je Verband hängt von der Größe des Akteurs ab (Befehlskapazität, siehe POLITIK.md, Abschnitt 7): Kleinfirma ein Verband, Firma bis zu drei, Konzern und Staat mehr, bestimmt durch Firmensitz oder Hauptquartier. **Warum:** Befehlsgewalt ist die Grenze der Macht; ohne sie wächst die Rechenlast mit jeder Flotte.

## 7. Begegnungen ohne Kampfsystem

Das Kampfsystem fehlt noch. Damit Polizei und Piraten trotzdem sinnvoll werden, gibt es eine **abstrakte Auflösung**:

- Eine Schnittstelle `IEncounterResolver`. Die einfache Variante vergleicht die **Stärke** beider Seiten (aus Modulen: Waffen, Schild, Hülle) und würfelt über `Universe.Rng`. Ergebnis: ein Schiff flieht, wird beschädigt, verliert Fracht oder wird zerstört.
- Im Spielersektor ersetzt später ein echter Kampf (Ansicht, Waffen, Schilde) diese Variante, und die Simulation übernimmt das Ergebnis. Der Core bleibt unverändert, weil nur der Auflöser wechselt.
- Das ist für NPC gegen NPC ohnehin der richtige Weg, weil ohne Physik außerhalb des Spielersektors nur ein Würfelwurf möglich ist.

## 7a. Eroberung (zunächst abgeschaltet)

Regel `rules.allowStationCapture` (JSON, im Save), **Standard aus**. Das Flag entspricht dem Szenario-Schalter `AllowStationCapture` der Referenz. Solange es aus ist, wirken Kriege nur über Sperren, Embargo, Blockade, Handel und Plünderung von Schiffen; Stationen werden weder erobert noch zerstört. Der Entwurf für später:

**Grundregel:** Erobern darf nur, wer mit dem Besitzer im Kriegszustand ist (Fehde: einzelne Stationen eines Akteurs, Krieg: auch Behörden, totaler Krieg: auch das HQ). Sonst ist es Piraterie.

1. **Belagern:** Der Verband sperrt Zufahrt und Versorgung; die Station verliert Nachschub, Kunden und Einnahmen.
2. **Niederkämpfen:** Hülle und Schild sinken (abstrakt über den Auflöser, später im Kampf). Wachen und Schiffe verteidigen.
3. **Wehrlos:** Bei null Verteidigung hat der Angreifer drei Optionen.

| Option | Folge |
|---|---|
| Erobern (Enterkommando) | Besitzer wechselt; die Station arbeitet weiter |
| Plündern | Fracht und ein Teil der Kasse wandern ab, Station bleibt beim Besitzer |
| Zerstören | Station verschwindet (`StationRemoved`), Rohstoffe als Beute |

**Folgen einer Eroberung:** Lager und Kasse bleiben zur Hälfte, der Rest ist Beute. Für eine Frist läuft die Produktion mit halber Leistung. Regierungsstationen (`Government`) sind nicht übernehmbar, nur zerstörbar oder entkernbar; ihr Verlust löst Regierungskrise oder Kontrollverlust aus. Kolonien erzeugen Unruhe und verkleinern den Bevölkerungspool. Eroberte Fabriken liefern nicht mehr an die alte Seite (Kriegswirtschaft, im Balance-Tool beobachten).

**Schutz gegen Weltuntergang:** Verliert ein Akteur die letzte Station, wird er ein **Exilakteur** (nur Schiffe, kann neu bauen). Die Schutzeigenschaften stehen in STATIONEN.md ("`Essential` aufgedröselt"): `Capturable`, `Destructible`, `Immortal`.

**Abhängigkeiten:** Kampf (oder der abstrakte Auflöser), Fraktionsverhalten, Stationsdienste und die Akteur/Fraktion-Unterscheidung.

## 8. Architektur

| Baustein | Aufgabe |
|---|---|
| `Squadron` | Verband (Anführer, Mitglieder, Befehl, Regeln) |
| `IOrderExecutor` + `OrderSystem` | führt Befehle aus, registriert pro Typ |
| `ThreatMap` / `SecuritySystem` | Lage und Zuweisung der Polizei |
| `IAccessCheck` / `CheckpointSystem` | Prüfungen an Kontrollpunkten (POLITIK.md) |
| `IEncounterResolver` | Auflösung von Begegnungen (abstrakt, später echter Kampf) |
| `WantedSystem` | Fahndungsniveau, Kopfgeld |
| Events | `PatrolScanned`, `ContrabandSeized`, `ShipIntercepted`, `BountyPaid`, `Raid` |
| Fraktionskasse (`Treasury`) | Quelle und Senke für Zölle, Strafen, Polizeikosten |

Alle in neuen oder bestehenden Modulen registriert (`DefaultModules()`), alle Zufallsentscheide über `Universe.Rng`, damit die Determinismus-Tests gelten.

## 9. Risiken

- **Eroberung** ist abgeschaltet; sobald sie eingeschaltet wird, gelten zusätzlich die Risiken aus Abschnitt 7a (Kriegswirtschaft, Kettenbruch).

- **Rechenzeit:** Scans und Patrouillen pro Tick wären teuer. Systeme laufen mit Intervall (LOD), Verbände planen einmal.
- **Wirtschaftlicher Schaden:** Zu viele Piraten ruinieren den Handel, zu viel Polizei die Fraktionskasse. Beides mit `tools/SpaceSim.Balance` messen (Verlustquote, Polizeianteil am Haushalt).
- **Fahndung:** Falsche Treffer und endlose Verfolgungen vermeiden: Mindestdauer, Verfall, Obergrenzen je Schiff.
- **Umfang:** Der Auflöser ist klein, das Kampfsystem ist groß. Deshalb zuerst abstrakt.
- **Abhängigkeiten:** Beziehungen, Zugangsmodell, Fähigkeiten und Schiffsmodule müssen vor Polizei und Piraten stehen.

## 10. Reihenfolge

1. **Befehls-Ausführer** (`IOrderExecutor`) für die bestehenden Befehle, Verhalten unverändert, dann `GoTo`, `Hold`, `Patrol`.
2. **Spielerfraktion und eigene Flotte** mit Automatik (Händler und Schürfer unter Befehl), Flottenoberfläche.
3. **Verbände** (`Squadron`), Eskorte.
4. **Lagekarte** (nur Beobachtung), dann `SecuritySystem` und Polizeischiffe mit abstrakter Auflösung.
5. **Fahndung und Kopfgeld**, Kontrollpunkt-Scans.
6. **Piraten** als Gegenfraktion.
7. **Echter Kampf** im Spielersektor als Ersatz des Auflösers.

## 11. Offene Entscheidungen

- Sollen Verbände schon vor der Spielerfraktion für NPCs (Polizei) kommen oder erst gleichzeitig?
- Soll es für den Spieler Polizeiarbeit geben (Kopfgeld, Eskortenaufträge) oder nur Besitz und Befehle?
- ~~Startet die Welt mit Piraten, oder entstehen sie durch Pleiten und Fahndung?~~ Entschieden (Phase 11): Die Welt startet mit einem gesetzlosen Akteur samt Versteck und drei Kaperschiffen.
- ~~Hat jede Fraktion eine eigene Polizeiflotte, oder teilen sich Verbündete die Lage?~~ Entschieden (Phase 11): eigene Polizei je Fraktion; die Lagekarte ist je Sektor (Sicht des Besitzers).
- Wie viele Verbände sind je Größenklasse erlaubt (Richtwerte: 1 / 3 / mehr)?

- Gemischte Verbände: erlaubt (Entscheidung steht in Abschnitt 3a); offen ist nur, wie Befehle an Mitglieder ohne passende Fähigkeit aufgeteilt werden.

## 12. Flottenbefehle des Spielers (Ausbau Punkt 1 und 1b)

Neue Befehle der eigenen Flotte, jeweils ein eigener Ausführer in der Registry (`OrderExecutors`), gespeichert als Befehlsart im `OrderSave` (additiv, Save bleibt v17, Codec `Persistence/FleetOpsCodec`), im Hash nur über Spielerschiffe (NPC-Schiffe tragen diese Befehle nie — die NPC-Welt bleibt bitgleich). Zielwahl im Flottenbildschirm in höchstens drei Tipps: **Schiff → Befehl → Ziel** (Ziellisten rein im Core: `FleetTargets`). Jeder Befehl zählt wie andere Einzelbefehle gegen die Befehlskapazität; die Verhaltensregeln gelten („Passiv“ und „Feuer halten“ greifen nicht an); unter der Rückzugsschwelle zieht sich ein Schiff zur Reparatur zurück (Notfall-Ebene), danach geht der Befehl weiter.

- **Angreifen** (`AttackCommand`): Ziel ist ein Schiff, eine Station oder ein Tor. Schiffsziel: das Schiff nähert sich bis 60 Einheiten und stellt das Ziel — es tritt dessen laufendem Gefecht auf der Gegenseite bei oder eröffnet ein Gefecht „Angriff“ (Begleit-Taktik). Verlässt das Ziel den Sektor, in dem es beim Befehl stand, ist es entkommen (dafür gibt es „Verfolgen“). Stationen und Tore: Anflug auf den Belagerungsabstand, dann Belagerung (bestehende Belagerung über `ISiegeTarget`; einer laufenden Belagerung tritt das Schiff auf der Angreiferseite bei). Erledigt, wenn das Ziel zerstört, ausgeschaltet, erobert oder weg ist; Verbandsmitglieder folgen danach wieder ihrem Anführer.
- **Verbrechen**: außerhalb eines Kriegs ist jeder Angriff ein Verbrechen mit derselben Meldung wie ein eigener Schuss (`Wanted.ReportAssault`, im Schutzradius einer Station „assault_station“, Stationen „assault_station“, Tore „assault_gate“ bei beiden Endfraktionen); eine Streife der Sektorfraktion greift mit der Antwortchance der Überfälle ein. Angriffe auf Gesetzlose und Kriegsgegner sind keine Verbrechen.
- **Verfolgen** (`PursueCommand`, jetzt auch für den Spieler): folgt dem Ziel über Sektorgrenzen (Reiseziel des Ziels) und stellt es in Reichweite; nach dem Gefecht geht die Verfolgung weiter. Ende: Ziel weg, Frist 2400 Ticks, oder der Sektor des Ziels ist für das Schiff gesperrt. Die NPC-Polizei behält ihren eigenen Zweig.
- **„Verband greift an“** (Flug, Zielkarte): rechts neben der Kontexttaste, zwei Tipps. Alle bewaffneten eigenen Schiffe im Verband des aktiven Schiffs und frei im Sektor bekommen den Angriffsbefehl auf das gewählte Schiff, die gewählte Station oder das gewählte Tor (`FleetAttack.OrderWing`).
- **Bergen** (`SalvageCommand`, braucht Bergungsgreifer und Frachtraum): leert Wracks und Behälter eines Sektors (Fracht, Module ab dem Mindestzustand), fährt die Beute zum besten Abnehmer, verkauft (Kasse des Besitzers, Rückkaufregeln) und wiederholt; ohne Wrack wartet es im Sektor.
- **Tor blockieren** (`BlockadeCommand`): ein bewaffnetes Schiff hält am Tor und greift jedes Schiff der gewählten Fraktion an, das im Umkreis von 80 Einheiten vorbeikommt. Nur im Krieg mit dieser Fraktion (Waffenstillstand: ruht; Frieden: erledigt) — also nie ein Verbrechen. Ohne Krieg zeigt die Zielwahl „nur im Krieg“.
- **Liefern** (`DeliverCommand`, Pendel): feste Route A → B mit fester Ware; Menge je Fahrt (voll, 10 … 200) und Einkaufsgrenze (wie bei Handel). Einkauf und Verkauf über die Handelsregeln des Spielers (`TradeActions`, Rückkaufdeckel). Nimmt B nichts oder hat A nichts, wartet das Schiff angedockt. Ziellisten: zuerst der Bedarf eigener Stationen, dann die besten Spannen bis zwei Sprünge. **Nachtrag 1c: Verlustschutz** wie beim Handel (Standard an, `DeliverCommand.AllowLoss` false): an A wird nicht gekauft, wenn B je Einheit (mit Rückkaufdeckel) nicht mehr zahlt, als der Einkauf an A kostet (mit Rückkaufboden) — Ereignis `TraderBuyRefused`, das Schiff wartet. Im Liefern-Bereich schaltet die Taste „Verlustschutz“ / „Verlust erlaubt“ (gleiche Einstellung wie „Verlustkauf“ bei den Grenzen) für die danach gewählte Route.
- **Sammelpunkt** (`RallyCommand`): links „mit“ tippen, dann Station wählen. Die Schiffe fliegen neben die Station (kein Andocken); sind alle da, bildet das zuerst gewählte Schiff den Verband. Geprüft vorab: mindestens zwei, höchstens die Verbandsgröße, freier Verbandsplatz der Befehlsstufe, kein Schiff schon im Verband. **Nachtrag 1c: Frist** (`rallyDeadlineTicks`, 2 400 Ticks = 10 min, im Befehl als `Until` gespeichert): danach bilden die angekommenen Schiffe den Verband (der Anführer, wenn er da ist, sonst das erste angekommene Schiff), wer fehlt, verliert den Sammelbefehl; sind weniger als zwei da, entsteht kein Verband und alle Befehle enden — niemand steht mehr dauerhaft über der Befehlskapazität. Die Befehlszeile nennt die Restzeit („· Frist 7:12“).
- **Konvoi** (`ConvoyCommand`): links „mit“ tippen, „Konvoi bilden“. Händler (Frachtraum) und Geleitschutz (bewaffnet ohne Frachtraum) bilden einen Verband; Anführer ist der langsamste Händler, er handelt automatisch in den Grenzen; die übrigen Händler übernehmen seine Kaufrunde (gleiche Einkaufs- und Verkaufsstation, gleiche Ware) und fahren sie mit dem Tempo des Verbands; der Geleitschutz begleitet den Anführer und tritt jedem Gefecht eines Konvoi-Schiffs im selben Sektor bei.

Bekannte Schwächen: Ein Schiffsangriff endet, sobald das Ziel den Sektor verlässt; „Verband greift an“ nimmt nur Schiffe im Sektor oder im eigenen Verband. Konvoi-Mitglieder fahren den Verkaufsweg mit eigenem Tempo (nur der Einkaufsweg nutzt das Verbandstempo). Bergen verkauft auch geborgene Module beim besten Abnehmer, nicht gezielt an einer Werft.
