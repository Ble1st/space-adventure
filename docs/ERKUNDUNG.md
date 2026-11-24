# Erkundung und Intel (Ausbau Punkt 16)

Der Spieler kennt am Anfang nicht alles. Was er weiß, gehört dem Spieler-Akteur (`Actor.Intel`, Klasse `PlayerIntel` im Core-Modul `Intel/`) und wird
nur von der Spielerseite gelesen — die NPC-Welt merkt nichts davon.

## Schalter und wer beschränktes Wissen hat

- Regel `rules.playerIntel` (Code aus, Standardwelt an).
- Beschränktes Wissen haben nur die drei **regulären Startpakete** (Jäger, Händler, Schürfer). Welten ohne Startpaket (Balance-Werkzeug, Tests über
  `BuildDefault()`, ältere Tests über `TestWorlds`, dort ist die Regel aus) und **Testszenarien** (Startpakete mit `scenario`) kennen alles wie bisher;
  ein Testszenario kann mit `"knowAll": false` beschränktes Wissen wählen.
- `Actor.Intel == null` heißt „alles bekannt“. Alle Abfragen (`Knowledge.KnowsSector/KnowsStation/KnowsGate/KnowsField/KnowsFaction`) geben dann wahr.
- Prüfläufe (Selbsttest, Bildschirmfotos, ui-check) beginnen neue Spiele mit vollem Wissen (`Main.TestKnowAll`); die Erkundungs-Stufe 147 schaltet das
  beschränkte Wissen für sich ein (`Main.TestIntelOn`).

## Was am Start bekannt ist

- Der Startsektor ganz: besucht, alle Stationen, Rohstoff- und Gasfelder, Tore.
- Die Sektoren hinter seinen Toren dem Namen nach („von fern gesehen“: Name und Lage, Inhalt unbekannt).
- Die Fraktion, der der Startsektor gehört, und die eigene Fraktion.
- Alles andere ist unbekannt: Sektoren (auf den Karten ein graues „?“), Stationen, Felder, Tore, Fraktionen.

## Wissensstufen eines Sektors

| Stufe | Bedeutung | Karten |
|---|---|---|
| unbekannt | kein bekanntes Tor führt hin | graues „?“, keine Tore, keine Route |
| von fern gesehen | Name bekannt (hinter einem bekannten Tor, aus einem Auftrag, aus einer gekauften Stationsliste) | Name, „unerkundet“, Route möglich |
| besucht | betreten oder Sektorkarte gekauft | Besitzerfarbe, alle Tore, bekannte Stationen/Felder |

## Aufdecken

- **Betreten** (jedes eigene Schiff, auch auf der Durchreise): Sektor besucht, alle seine Tore (und die Sektoren dahinter dem Namen nach), die
  Besitzerfraktion wird bekannt.
- **Sensoren**: Stationen (Abstand zum Rand), Tore und Feldmitten im Umkreis von `intelSensorRange` (450 m) um ein eigenes Schiff werden bekannt.
  Das `IntelSystem` prüft alle 5 Ticks jedes eigene Schiff (unterwegs an seiner gerechneten Position) — wenige Dutzend Abstände je Sektor.
- **Andocken**: die Station und ihre Fraktion.
- **Taxifahrt**: Zielstation und Zielsektor sofort (besucht).
- **Begegnung**: wer dich angreift oder den du angreifst (Gefechtsbeginn), wird bekannt; ebenso der Absender einer Comms-Nachricht.
- **Auftrag annehmen**: Zielstation (Lieferung, Transport) und Zielsektoren (Eskorte, Patrouillen-Wegpunkte) werden dem Namen nach bekannt, der
  Auftraggeber begegnet.
- **Intel kaufen** (unten).

Jede Aufdeckung mit Neuem meldet das Ereignis `IntelDiscovered` (Quelle sensor/dock/taxi/contact/comms/mission/intel); die Ansicht zeigt
„Neu entdeckt: Sektor Beta erkundet, 2 Stationen“ als kleinen Hinweis.

## Befehl „Erkunden“ (Flotte, zwei Ebenen)

- **Erkunden: Sektor** (`explore`, mit Zielwahl): das Schiff fliegt ein festes Punktmuster im Sektor ab (Mitte, Ring 0,45 × Ausdehnung mit 6 Punkten,
  Ring 0,85 × Ausdehnung mit 10 Punkten, aus Hindernissen geschoben) und überspringt Punkte, in deren Sensorkreis nichts Unbekanntes liegt; bleibt danach
  noch etwas unbekannt, fliegt es direkt dorthin. Endet, wenn im Sektor nichts mehr unbekannt ist. Zielliste: bekannte Sektoren mit Unbekanntem, nach
  Sprüngen.
- **Erkunden: Universum** (`explore_far`; im Befehlsraster gibt es nur eine Taste „Erkunden“, deren erstes Ziel „Universum“ ist): fliegt nacheinander in den nächsten bekannten, noch nicht besuchten Sektor (wenigste Sprünge,
  dann Id), dort durch das Ankunftstor ein Stück hinein; jeder Besuch deckt die Tore auf und damit die nächsten Ziele. Endet, wenn kein unbesuchter Sektor
  mehr erreichbar ist.
- Beide sind gewöhnliche Spielerbefehle (`IOrderExecutor`; die Universumsebene ohne eigene Rastertaste), zählen wie andere Einzelbefehle gegen die Befehlskapazität, brauchen keine
  Fähigkeit und tanken über die gemeinsame Reiseregel.

## Intel kaufen

- Wo: **Comms → Diplomatie** (Detail einer Fraktion, Abschnitt „Intel kaufen“, Aktion `intel:<F>:<Paket>`) und in der **Behörde** einer Station mit
  Gericht oder Kopfgeldbüro (Dienst `intel`, Schlüssel `buy:<Paket>`, Pakete der Fraktion der Station).
- Nur bei **bekannten** Fraktionen, die **nicht feindlich** sind (Beziehung über −0,15) und nicht im Krieg mit dir.
- Pakete:
  - **Sektorkarte <Sektor>** — ein Sektor der Fraktion: besucht, alle Tore, Stationen und Felder. Preis = 250 Cr (wenn Sektor/Tore neu) + 60 Cr je
    unbekannter Station + 30 Cr je unbekanntem Feld.
  - **Gebietskarte** — alle Sektoren der Fraktion (ab zwei Sektoren mit Neuem), Summe der Sektorpreise × 0,8.
  - **Stationsliste <Typ>** — alle Stationen dieses Typs, die der Fraktion gehören oder in ihrem Gebiet liegen: 60 Cr je Station + 40 Cr.
- Nur Pakete mit Neuem werden angeboten; ein gekauftes Paket verschwindet.
- Geld: Spielerkasse → Kasse der Fraktion; Ereignisse `TreasuryIncome` (Zweck „intel“) und `IntelBought`. Fehlschläge ändern nichts und melden nichts.

## Wirkung auf die Oberfläche (nur Bekanntes)

- Universumskarte und Sternenkarte: unbekannte Sektoren als „?“, nur entdeckte Tore; Route/Autopilot nur zu bekannten Sektoren.
- Sektorkarte: nur bekannte Stationen, Felder und Tore; Zeile „n unbekannt — Erkunden deckt auf“ bzw. „Unerkundet“; kein Autopilot zu Unbekanntem.
- Randpfeile im Flug: nur bekannte Stationen.
- Taxi: nur bekannte Ziele.
- Handelshinweis (bester/ehrlicher Abnehmer): nur bekannte Stationen.
- Auto-Verkauf der Flotte (`TradeActions.BestBuyer`: Auto-Mining, Bergen, Restverkauf im Konvoi): Spielerschiffe fahren nur bekannte Stationen an, NPC-Schiffe sind unverändert.
- Toasts/Meldungen (Krieg, Frieden, Torangriff, Stationsfall, Überfall, Fahndung usw.): eine dem Spieler unbekannte Fraktion wird nicht beim Namen genannt, sondern als „eine unbekannte Fraktion“ (Ist Intel aus, bleibt alles wie bisher).
- Zielwahl der Flottenbefehle (Angreifen, Bergen, Blockade, Liefern, Sammelpunkt): nur bekannte Stationen, Tore und Sektoren.
- Fraktionen und Comms-Diplomatie: nur bekannte Fraktionen (die eigene immer).

## Speichern und Hash

- Gespeichert additiv als `ActorSave.Intel` (Save bleibt v17); ohne beschränktes Wissen wird nichts geschrieben.
- Gehasht nur mit Spielerpolitik (`UniverseHash.Compute(u, tags, includePlayerPolitics: true)`), und nur wenn der Spieler beschränktes Wissen hat — die
  Balance-Welt (ohne Startpaket) hasht unverändert (20000 Ticks Release `48ECEC8C1781997D`). Der Hash ohne Spielerpolitik ist mit und ohne Wissen gleich.

## Schwächen

- Der Sektor-Erkunder weiß, wo noch etwas Unbekanntes liegt (er überspringt leere Musterpunkte) — bequem, aber nicht „blind“.
- Die 3D-Ansicht zeigt im Flug alles, was in Sichtweite liegt; die Sensorreichweite (450 m) deckt es ohnehin schnell auf.
- Missionsangebote nennen ihre Ziele auch vor der Annahme (Text); erst die Annahme deckt sie auf den Karten auf.
- Sprungantrieb: das Sprungziel darf auch ein unbekannter Sektor sein (Erkundung per Sprung).

## Universumsereignisse

Lücke gegenüber Interstellar Pilot 2: im Universum geschieht zufällig etwas, das man finden und nutzen kann. Schalter `rules.universeEvents`
(Code aus, Standardwelt an; Werte in `Model/EconomyRules.UniverseEvents.cs`).

- **Ablauf** (`Simulation/UniverseEvents.cs`, `UniverseEventSystem` im eigenen Modul am Ende des Ticks, Intervall 50): alle 1200 Ticks (5 min) wird mit
  Wahrscheinlichkeit 0,5 ein Ereignis gewürfelt, höchstens 3 gleichzeitig, eines je Sektor; Art nach Gewicht, Sektor gleichverteilt unter den möglichen,
  Dauer 6000–9600 Ticks (25–40 min). Zufall aus einem eigenen RNG aus Welt-Seed und Takt (wie die Auftrags-RNGs) — `Universe.Rng` wird nie gezogen.
  Ein Ereignis endet nach seiner Frist („expired“) oder sobald alles genutzt ist („exhausted“).
- **Arten** (je eigene Klasse hinter `IUniverseEventType`, Registry `UniverseEventTypes.Default`): *Erzschwemme* — 3–5 zusätzliche Felsen mit 2,5-fachem
  Erz in einem Gürtel (über `BeltPlacement`, nie neben dem Spieler); am Ende verschwinden die unberührten, angebrochene bleiben Gürtelfelsen.
  *Frachtfund* — 1–3 treibende Frachtbehälter (Wracks der Art Container) mit einer zufälligen Ware im Wert von 1500–4000 Cr. *Wrackfeld* — 3–5 Wracks
  mit je 1–3 Modulen (Restzustand 0,4–0,95; unter 0,5 Schrott) und manchmal etwas Fracht. Herrenlose Schiffe sind keine eigene Art (gibt es schon).
- **Nutzung**: alles sind gewöhnliche Felsen und Wracks — der Spieler baut ab, zieht mit dem Traktor, birgt mit dem Greifer; NPC-Schürfer und
  NPC-Bergeschiffe nutzen sie mit ihren eigenen Regeln.
- **Anzeige** (`Simulation/UniverseEventInfo.cs`): nur in Sektoren, die der Spieler kennt (Spielerwissen). Beginn im eigenen Sektor = wichtige Meldung
  (Toast), in einem anderen bekannten Sektor = Info, sonst still; Ende im eigenen Sektor = Info. Türkiser Ring am Sektorknoten der Universumskarte,
  Doppelring mit Beschriftung („Frachtfund · noch 25:00“) auf der Sektorkarte, Zeile „Ereignis: …“ in der Detailkarte.
- **Speicher/Hash**: `Universe.UniverseEvents` additiv gespeichert (`SaveGame.UniverseEvents`, `Persistence/UniverseEventCodec.cs`), gehasht nur wenn je
  eines lief. Mit Schalter aus bleibt die Welt bitgleich (20000 Ticks 1099FCA5A206E3D8). Mit Schalter an ändert sich der Hash, weil die Ereignisse die
  Welt verändern (neue Felsen und Wracks, die NPCs nutzen): 20000 Ticks 1FF1C44226010C8C, Preise im Zielbereich, Verlustquote 0,28 → 0,45 %; 60000 Ticks
  65A94E179F3D2323, 0,31 → 0,37 % (Kaperer finden mehr beladene Schürfer/Bergeschiffe; unter 1 %), 19 Ereignisse, 23 von 28 Schwemme-Felsen abgebaut,
  NPC-Bergeschiffe bargen 712 E. und 49 Module, Exporte Nahrung −1 %, Konsumgüter +6 %, Luxus +4 %. Ort eines Frachtfunds/Wrackfelds aus den Positionen von
  Stationen, Toren und Feldern (nicht `Sector.Extent` — abgeleitet und nach dem Laden anders; sonst wäre Laden → Weiterlaufen nicht bitgleich).
  Balance: Zeile „Universumsereignisse“, Option `--universeevents on|off`.
