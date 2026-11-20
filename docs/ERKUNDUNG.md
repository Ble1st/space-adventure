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
