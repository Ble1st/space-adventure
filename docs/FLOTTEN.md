# Flotten, Befehle, Patrouillen, Polizei (Planung)

**Status: reine Planung, nichts davon ist umgesetzt.** Baut auf [POLITIK.md](POLITIK.md) (Kontrollpunkte, Sperren) und [SCHIFFE.md](SCHIFFE.md) (Fähigkeiten, Aufgaben) auf.

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

`Ship.Order` ist ein `ShipOrder`-Record; es gibt bereits `TradeOrder`, `MineOrder`, `PatrolOrder(SectorId)` und `GoToOrder(StationId)`. Die beiden letzten werden noch nicht ausgeführt. `OrderSave` speichert Befehle als `Kind` mit drei Ganzzahlen. `Faction` hat `Relations` und `Territory`, aber keine Kasse. Der Name `FleetSystem` ist schon vergeben (er kauft Schiffe), deshalb heißen die neuen Teile **Verband** und **Geschwader**.

## 3. Verband (Geschwader)

Ein Verband ist eine Gruppe von Schiffen mit einem **Anführer**, einer Formation und einem gemeinsamen Befehl.

- **Planung einmal:** Der Anführer plant die Reise (`Travel.Plan`), die Mitglieder folgen mit Versatz. Das spart Rechenzeit, auch bei vielen Schiffen.
- **Bestand:** Neues Modell `Squadron` (Id, Besitzer, Anführer, Mitglieder, Befehl, Regeln). `Ship.Squadron` verweist darauf. Bewegung läuft weiter über `Universe.AddShip/StartTransit/FinishTransit`.
- **Tempo:** Der Verband fliegt mit dem langsamsten Mitglied.
- **Zusammensetzung:** Er braucht passende Fähigkeiten (Patrouille braucht `Combat`, Eskorte `Combat` und ein Begleitziel). Gemischte Verbände (ein Frachter mit Eskorte) sind erlaubt.
- **Auflösung:** Fällt der Anführer aus, übernimmt das nächste Mitglied; fehlt der Treibstoff, kehrt der Verband um.

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

### Geschwader nach Größe

Die Zahl der Verbände und Schiffe je Geschwader hängt von der Größe des Akteurs ab (Befehlskapazität, siehe POLITIK.md, Abschnitt 7): Kleinfirma ein Geschwader, Firma bis zu drei, Konzern und Staat mehr, bestimmt durch Firmensitz oder Hauptquartier. **Warum:** Befehlsgewalt ist die Grenze der Macht; ohne sie wächst die Rechenlast mit jeder Flotte.

## 7. Begegnungen ohne Kampfsystem

Das Kampfsystem fehlt noch. Damit Polizei und Piraten trotzdem sinnvoll werden, gibt es eine **abstrakte Auflösung**:

- Eine Schnittstelle `IEncounterResolver`. Die einfache Variante vergleicht die **Stärke** beider Seiten (aus Modulen: Waffen, Schild, Hülle) und würfelt über `Universe.Rng`. Ergebnis: ein Schiff flieht, wird beschädigt, verliert Fracht oder wird zerstört.
- Im Spielersektor ersetzt später ein echter Kampf (Ansicht, Waffen, Schilde) diese Variante, und die Simulation übernimmt das Ergebnis. Der Core bleibt unverändert, weil nur der Auflöser wechselt.
- Das ist für NPC gegen NPC ohnehin der richtige Weg, weil ohne Physik außerhalb des Spielersektors nur ein Würfelwurf möglich ist.

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
- Startet die Welt mit Piraten, oder entstehen sie durch Pleiten und Fahndung?
- Hat jede Fraktion eine eigene Polizeiflotte, oder teilen sich Verbündete die Lage?
- Wie viele Geschwader sind je Größenklasse erlaubt (Richtwerte: 1 / 3 / mehr)?
