# Politik: Kontrollpunkte, Sperren, Folgen (Planung)

**Status: Planung; seit Phase 9 umgesetzt ist Abschnitt 6 in Teilen** (Akteure und Fraktionen, `Ship.Owner`, Sammelgruppe „Freie“ mit trägem Aufstieg, Größenklassen, Zukauf als gewichtete Ziehung, Kassen aus Abgaben, sparsame Beziehungen, Persönlichkeit; siehe UMSETZUNGSPLAN.md Phase 9). **Seit Phase 10 umgesetzt:** Kontrollpunkte (Abschnitt 1, `IAccessCheck` Politik/Ware/Entdeckung am Gate; Station und Sprung-Ankunft noch nicht), Sperrstufen 0–5 je Sektor mit Richtung, Mindestdauer, Hysterese, Ablauf, Hauptroute und Durchsetzungskosten (Abschnitt 2), Beziehungsänderungen aus Handel, umkämpften Sektoren und Sperren (Abschnitt 3, ohne Krieg), Sektorkontrolle (Abschnitt 8) und die Regierungsstationen als Typen (Abschnitt 7, ohne Regierungskrise); siehe UMSETZUNGSPLAN.md Phase 10. Offen: Spielerfraktion (Abschnitt 4), Krieg, Polizei.

## 1. Kontrollpunkt

Ein Kontrollpunkt ist **jeder Ort, an dem das Spiel prüft, ob ein Schiff durchdarf**. Die Prüfung hat drei Teile:

1. **Politik:** Was erlaubt der Besitzer dieser Fraktion? (`Allow`, `Toll`, `Deny`)
2. **Ware:** Ist die Fracht erlaubt? (Embargo, Schmuggelware)
3. **Entdeckung:** Hat der Kontrollpunkt das Schiff bemerkt? (Sensorstärke gegen Tarnfeld)

| Kontrollpunkt | Wann geprüft | Was er kann |
|---|---|---|
| Gate | beim Durchflug | Sperren, Zoll, Scan |
| Station (Zoll) | beim Andocken | Andockverbot, Zoll, Beschlagnahme |
| Sprung-Ankunft | beim Auftauchen im Sektor | Sensor erkennt die Sprungspur, Sprungsperre verhindert die Ankunft |
| Patrouille (siehe FLOTTEN.md) | im Sektor | Abfangen, Scan, Kampf |

Jeder Kontrollpunkt hat **Sensorstärke** und **Durchsetzungsstärke**. Ein Kontrollpunkt ohne Durchsetzung ist nur ein Schild: Er verbietet, hält aber niemanden auf.

## 2. Sektor dicht: Stufen statt Schalter

Die Stufen gelten für alle Gates des Sektors; Einzelne Gates dürfen Ausnahmen haben.

| Stufe | Bedeutung | Wirkung |
|---|---|---|
| 0 Offen | frei | normal |
| 1 Kontrolle | Scan und Zoll | Schiffe zahlen, Schmuggelware wird gefunden |
| 2 Embargo | bestimmte Waren verboten | erlaubte Waren fließen, verbotene nicht |
| 3 Nur Verbündete | nur Freunde und Neutrale | Feinde bleiben draußen |
| 4 Gesperrt | niemand rein, wer drin ist, darf raus | Schiffe im Sektor verlassen ihn |
| 5 Abriegelung | niemand rein oder raus | Schiffe sitzen fest |

- **Richtung:** Einreise und Ausreise werden getrennt gesteuert (Stufe 4 sperrt nur die Einreise).
- **Zusammenhang mit den Gate-Zuständen:** Die Stufen hier sind Politik; die Zustände des Gates (offen, eingeschränkt, geschlossen, zerstört) stehen in [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md), Abschnitt 5. Stufen 1–3 setzen `Restricted`, Stufen 4–5 setzen für alle `Deny`; physisch geschlossen wird ein Gate nur durch Wartung, Ereignis oder Zerstörung.
- **Physisch oder rechtlich:** *Physisch geschlossen* heißt, das Gate ist wirklich zu (Wartung, Zerstörung) und kann nur per Sprungantrieb umgangen werden. *Rechtlich eingeschränkt* heißt, das Gate funktioniert und Scanner kontrollieren es. Das ist undicht (Tarnfeld, Entdeckungswurf).
- **Kosten:** Durchsetzung hat laufende Kosten im `UpkeepSystem`. Wer nicht zahlt, hat nur eine Sperre auf dem Papier.
- **Dauer:** Jede Sperre hat Mindestdauer (Hysterese) und Ablauf. Keine dauerhafte Schließung durch Fraktionen (entschieden: mit Ablauf, damit die Welt nicht erstarrt).

## 3. Politische Folgen

Alle Folgen entstehen aus bestehenden Daten und Events, nicht aus Sonderregeln.

**Wirtschaftlich:**
- Handelsströme (`TradeCompleted`, `RouteRecorder`) durch den Sektor brechen ein; Händler zahlen Umwege oder verlieren den Markt.
- Liegt der Sektor im Zentrum des Graphen, ist er ein **Engpass**: Seine Schließung teilt die Karte. Das ist Hebel, aber auch Provokation.
- Die eigenen Stationen verlieren Importe, Exporte und Zolleinnahmen. Wer zu hart abriegelt, hungert sich selbst aus.

**Diplomatisch:**
- Jede Sperre gegen eine Fraktion senkt deren Meinung. Das Maß ist der Handelsverlust, den sie dadurch hat. Wer wenig handelt, ärgert sich kaum.
- Unbeteiligte Fraktionen reagieren auf Betroffenheit, nicht auf Absicht.
- Schwellen: Unter einem Grenzwert antwortet die Gegenseite mit Gegensperren, Embargo oder Krieg. Verbündete können vermitteln oder mitziehen.

**Sicherheit:**
- Im Sektor festsitzende Fremdschiffe können ausgewiesen, beschlagnahmt oder festgesetzt werden (Anlass für Streit).
- Wo hinter einer Sperre ein Preisunterschied wächst, lohnt sich Schmuggel mit Tarnfeld oder Sprungantrieb. Blockadebrecher werden zu einer echten Rolle.

**Dämpfer:**
- Mindestdauer und Hysterese, eine Hauptroute je Region bleibt offen, Sperren laufen aus.
- Das Balance-Tool meldet, wenn die Karte zerfällt oder Stationen ohne Nachschub pleitegehen.

## 4. Spielerfraktion

Der Spieler besitzt heute keinen Sektor und keine Fraktion. Für Sperren braucht er eine **Spielerfraktion**, die Sektoren oder Stationen besitzt (zum Beispiel über Bauschiffe oder Kauf).

- Der Spieler stellt die Politik von Hand, KI-Fraktionen tun es über das `FactionPolicySystem`. **Beide nutzen dieselben Regeln und Kosten.**
- Die Folgen treffen ihn genauso: Beziehungen sinken, Handel bricht ein, Durchsetzung kostet Geld.
- Der Spieler kann sich zum Engpassherrscher machen (Zoll kassieren) und riskiert Krieg. Das ist ein Spielstil, der aus den Regeln folgt.

## 5. Was dafür gebaut werden müsste

1. **Beziehungsmodell** zwischen Fraktionen (Wert, Verlauf, Auslöser). `Faction.Relations` existiert als Skelett.
2. **Gate-Zugangsmodell** (Stufen, Richtung, Politik je Fraktion) statt `Gate.Open`; Save-Migration (alle bisher offen).
3. **Prüfschnittstelle** `IAccessCheck` für Politik, Ware und Entdeckung.
4. **`FactionPolicySystem`**: entscheidet nach Beziehungen, Wirtschaft und Stärke, mit Hysterese.
5. **Mobilitätsprofil** in `FindRoute`, damit Schiffe um Sperren herum planen.
6. **Spielerfraktion** und Politikmaske in der Oberfläche.

Reihenfolge: Beziehungen, Zugangsmodell, Kontrollpunkte und Politik, zuletzt Spielerfraktion. Sprungantrieb und Tarnfeld hängen am Zugangsmodell.

## 6. Akteure und Größe

Statt harter Arten (Staat, Konzern, Einzelschiff) gibt es **einen Akteurtyp** (`Faction` / `Actor`) mit einer **Größenzahl**, aus Besitz berechnet:

`Größe = Schiffe × Gewicht + Stationen × Gewicht + Sektoreinfluss × Gewicht + Kasse / Skala`

Die Art (`Kind`: Staat, Gilde, Gesetzlose, Spieler) bleibt als Etikett. **Was ein Akteur darf, hängt von der Größe ab.** Richtwerte:

| Klasse | Richtwert | Typisch |
|---|---|---|
| Einzelakteur | 1 Schiff, keine Station | freier Händler, Spieler am Anfang |
| Kleinfirma | 2–5 Schiffe oder 1 Station | Werftbesitzer, kleiner Schürfbetrieb |
| Firma | mehrere Stationen, 6+ Schiffe | regionaler Händler |
| Konzern | Stationen in mehreren Sektoren | großer Wirtschaftsakteur |
| Staat | kontrolliert Sektoren | die heutigen Fraktionen |

**Warum:** Eine feste Trennung bricht beim ersten Aufsteiger. Eine Zahl erlaubt stufenloses Wachstum, und der Spieler wächst nach denselben Regeln wie jeder NPC.

### Akteur und Fraktion (entschieden)

**Akteur** ist ein Wirtschaftssubjekt: Besitz, Konto, Fahndung, Ruf. **Fraktion** ist eine politische Organisation: Sektoren, Politik, Beziehungen, Krieg. Eine Fraktion ist ein Akteur mit **Politik-Fähigkeit** (ab Firma); jeder andere Akteur hat sie nicht.

| | Akteur | Fraktion |
|---|---|---|
| Wesen | besitzt und handelt | herrscht und verhandelt |
| Besitz und Konto | ja | über die Kasse der Organisation |
| Fahndung und Ruf | persönlich | als Gruppe (Staatsruf) |
| Politik (Sperren, Zoll) | nein | ja |
| Krieg | Fehde möglich | Krieg, totaler Krieg |
| Beziehungen | Abweichungen | Hauptbeziehungen |
| HQ und Sektoren | Büro, Firmensitz | Hauptquartier, Sektorbehörde |

- **Mitgliedschaft:** Ein Akteur kann Mitglied einer Fraktion sein (Bürger, Gildenmitglied, Söldner) oder unabhängig. Er erbt Beziehungen, Schutz und Pflichten (Steuer), behält aber Besitz und Fahndung.
- **Spieler:** immer ein Akteur. Er startet unabhängig oder als Mitglied (Startpaket) und kann ab einer Größe eine eigene Fraktion gründen.
- **Migration:** `Ship.Faction` (heute Eigentümer) wird `Ship.Owner` (ein Akteur); `Actor.Faction` ist die Mitgliedschaft. Jede bestehende Fraktion wird ein Akteur der Art Staat, der zugleich Fraktion ist. Der Spieler wird ein Einzelakteur mit Mitgliedschaft in der bisherigen Fraktion, damit sich beim Laden nichts ändert.
- **Effektive Beziehung eines Schiffs:** persönliche Abweichung, sonst Beziehung seiner Fraktion, sonst Vorgabe der Art.

### Spielerkasse: das Geld hängt an der Person (entschieden)

Heute hängt das Geld des Spielers am Schiff (`Ship.Credits`). Das trägt nicht, sobald der Spieler ohne Schiff reist (**Taxi**, siehe [UI.md](UI.md)), mehrere Schiffe besitzt (**Hangar**, Spielerflotte) oder Schiffe verkauft.

- **Der Spieler ist von Anfang an ein ausdrücklicher Akteur (Person)** mit eigener Kasse `Actor.Credits`. Einnahmen und Ausgaben des Spielers (Verkauf, Kauf, Taxi, Reparatur, Zoll) laufen über diese Kasse, nicht über das Schiff.
- **Handelsaktionen** (`TradeActions.Buy/Sell/SellAll`) buchen auf die Kasse des **Besitzers** des handelnden Schiffs (`Universe.WalletOf(ship)`), nicht mehr auf `ship.Credits`. Für NPC-Einzelschiffe, die nur implizite Akteure sind, ist das Schiff die Kasse; das Verhalten bleibt gleich.
- **Schiffe sind Besitz, keine Geldbeutel.** Verkauft der Spieler ein Schiff, fließt der Erlös in seine Kasse. Verliert er ein Schiff, bleibt sein Geld.
- **Geld bei der Person, Fracht beim Schiff:** Fracht und Treibstoff bleiben im Schiff, beim Taxi bleiben sie also mit dem Schiff zurück.
- **Zwischenstufe (Plan Phase 5):** Die Spielerkasse kommt vor dem Akteurmodell (Phase 9). Bis dahin ist die Person ein kleines Objekt `PlayerWallet` an `Universe` (Kasse, Besitzer-Fraktion), `WalletOf(ship)` liefert sie für Spielerschiffe und `ship.Credits` für NPCs; Phase 9 macht daraus die Kasse des Spielerakteurs.
- **Migration:** Das heutige `Ship.Credits` des Spielerschiffs wird in die Kasse des Spielerakteurs übernommen; für NPC-Schiffe ändert sich nichts. Ein Save-Format erhöht die Version.
- **Warum:** Sonst gäbe es drei unlösbare Fälle: reisen ohne Schiff, Schiff verlieren und dabei das Geld verlieren, und Schiffe verkaufen oder tauschen, ohne dass das Geld mitwandert.
- **Prüfung:** `EconomyStats` und das Balance-Tool zählen die Geldmenge nach Akteuren; die Summe aus Spielerkasse und Schiffen darf sich durch Taxi und Schiffswechsel nicht ändern.

### Einzelschiffe träge anlegen

- Alle freien Schiffe teilen sich die Sammelgruppe **Freie**; ihre Beziehungen sind einmal gespeichert.
- Ein Einzelschiff wird erst zum eigenen Akteur, wenn es etwas Besonderes tut (Station bauen, weiteres Schiff kaufen, Verbrechen, vom Spieler geführt). Davor ist das Schiff der implizite Akteur und `Ship.Credits` seine Kasse (nur für NPC-Einzelschiffe, siehe „Spielerkasse“ unten).
- **Aufstieg** Einzelakteur → Kleinfirma → Firma → Konzern ergibt sich aus Besitz; **Abstieg** ohne Besitz und Geld entfernt den Akteur (wie eine bankrotte Station).
- **Warum:** Tausende Mini-Fraktionen würden die Beziehungen quadratisch aufblähen. Träges Anlegen und Sammelgruppe halten das klein.

### Beziehungen sparsam speichern

- Vorgabewerte nach Art und Gruppe (Staat–Staat, Staat–Freie, alle–Gesetzlose).
- Nur Abweichungen werden als Eintrag gespeichert; Änderungen entstehen aus Ereignissen (Handelsverlust durch Sperre, Überfall, Bündnis), nicht aus Neuberechnung aller Paare.
- Einzelne Schiffe haben keinen Ruf bei jedem, sondern ein **Fahndungsniveau** bei den Staaten, mit denen sie Ärger haben.
- **Warum:** Rechenzeit und Speicher bleiben linear.

### Zukauf durch kleinere Akteure

Heute baut bei Knappheit der **reichste** Teilnehmer eine Station oder kauft ein Schiff. Neu:

1. **Erstzugriff für Große:** Der Akteur, der den Bedarf strategisch decken will (Besitz im Sektor, passende Kette, Politik), entscheidet zuerst.
2. **Zweitzugriff für Kleine:** Winkt er ab (Budget, kein Interesse, Reserve zu niedrig), dürfen kleinere Akteure mit genug Geld einspringen. Die Wahl ist eine **gewichtete Ziehung** über `Universe.Rng`: Gewicht = Geld über der Schwelle × Appetit der Größenklasse. Kleine kaufen **seltener** (Appetit etwa 0,25 gegenüber 1,0 bei Großen).

**Warum:** So bleibt die Welt in Bewegung, wenn die Großen stocken, und es entstehen kleine Firmen, die aufsteigen. Der Vorrang der Großen verhindert Chaos, die Ziehung verhindert, dass ein Einzelner alles bekommt. Das Balance-Tool zeigt die Verteilung der Neubauten auf Größenklassen. Der Erstzugriff ist weich: Große haben nur höheres Gewicht, keine absolute Priorität.

### Krieg: jeder darf, es kostet

| Stufe | Wer | Wirkung |
|---|---|---|
| Fehde | auch Kleinakteure | betrifft nur Schiffe und Stationen der Beteiligten |
| Krieg | Firmen aufwärts | Gebiete werden angegriffen, Gates gesperrt, Embargo |
| Totaler Krieg | Konzerne und Staaten | Ziel ist das HQ, Tribut oder Sektorverlust (Eroberung ist zunächst abgeschaltet, siehe FLOTTEN.md) |

- Erklären verlangt einen **Kriegsgrund** (Beziehung unter Schwelle, Vorfall, Gebietsverletzung) und eine **Kriegskasse**; die KI wertet das Stärkeverhältnis aus und erklärt selten, wenn sie klar unterlegen wäre.
- Kriege enden durch **Kriegsmüdigkeit** (Dauer, Verluste, leere Kasse), Waffenstillstand oder Tribut; Dritte können vermitteln oder beitreten.
- **Warum:** Wer Krieg nur großen Staaten erlaubt, nimmt Spielern und kleinen Gruppen Spielraum. Kosten und Grund verhindern ständige Kriege. Die Stufen halten kleine Konflikte klein. Kriegsmüdigkeit stoppt ewige Kriege, die die Wirtschaft aushungern.

### Fraktionskasse (entschieden)

Akteure mit Besitz haben eine **Kasse** (`Treasury`), gespeist aus Zöllen, Steuern und Strafen, belastet durch Polizei, Unterhalt und Kopfgeld. Bei träge angelegten NPC-Einzelschiffen ist `Ship.Credits` die Kasse des impliziten Akteurs, beim Spieler und bei jedem ausdrücklich angelegten Akteur gehört sie dem **Akteur** (siehe „Spielerkasse“). Alle Übertragungen laufen über Ereignisse, damit das Balance-Tool die Geldmenge prüfen kann.

### Persönlichkeitswerte (entschieden, nach dem Vorbild der Referenz)

Die Referenz (`~/ip2-decomp/docs/08-fraktionen-krieg-intel.md`) gibt jeder Fraktion Werte wie `Aggression`, `Virtue`, `Greed`, `Cooperation` und `TradeEfficiency`. Wir übernehmen vier, jeweils zwischen 0 und 1:

| Wert | Wirkung | Beispiel |
|---|---|---|
| **Aggression** | senkt die Schwelle für Kriegserklärung, Fehde und Angriff auf Sicht | aggressive Akteure erklären früher Krieg |
| **Gier** | erhöht den Appetit beim Zukauf und den Zoll; senkt die Bereitschaft zu Frieden mit Tribut | gierige Akteure kaufen häufiger zu |
| **Kooperation** | Bündnisse, Waffenstillstand, Schutzverträge | kooperative Akteure schließen leichter Bündnisse |
| **Gesetzestreue** (Referenz: `Virtue`) | Strenge der Polizei und Neigung zu Schmuggel | gesetzestreue Akteure verbieten mehr, handeln nie mit verbotener Ware |

- **Herkunft:** Benannte Fraktionen bekommen Werte aus der JSON, erzeugte Akteure würfeln sie bei der Anlage über `Universe.Rng` (Mittelwert nach Art, etwa höhere Aggression bei Gesetzlosen).
- **Veränderung:** langsam durch Ereignisse (verlorener Krieg senkt Aggression, Verrat senkt Kooperation), mit Grenzen, damit Akteure erkennbar bleiben.
- **Speichern:** Werte gehören in den Akteur, in `SaveGame`, `UniverseSerializer` und `UniverseHash`.
- **Warum:** Ohne sie handeln alle Akteure identisch, wenn die Zahlen gleich sind. Mit ihnen unterscheidet sich eine zurückhaltende Handelsfirma von einem kriegerischen Staat bei gleicher Größe.

## 7. Hauptquartier, Firmensitz, Sektor-HQ

Die Formen stehen mit Größe, Diensten und Anzahl in [STATIONEN.md](STATIONEN.md), Abschnitt 5. Hier die politische Bedeutung:

| Größe des Akteurs | Was möglich ist |
|---|---|
| Einzelakteur | kein HQ; Eigentum an Station und Schiff |
| Kleinfirma | **Büro** an einer eigenen Station: Konto, Zoll auf der eigenen Station, ein Verband |
| Firma | **Firmensitz** (Medium): Verträge, bis zu 3 Verbände, Fehde, kleine Wachstation |
| Konzern | Firmensitz plus **Sektor-HQs** in bis zu N Sektoren, Sektorpolitik bis Stufe 2 |
| Staat | **Hauptquartier** (Hub, genau eines) plus **Sektor-HQ** in jedem kontrollierten Sektor |

- **Hauptquartier (Hub):** Krieg und Frieden, Diplomatie, Steuerrahmen, Forschung, Gesamtbudget. Liegt in der Hauptstadt.
- **Sektor-HQ (Sektorbehörde, Medium):** Durchsetzung vor Ort: Gate-Einstellungen, Zoll, Polizeibasis, Baugenehmigung, Sektorsperre. Es ist **keine Hub-Station**, damit jeder kontrollierte Sektor eine haben kann.
- **Verlust:** Fällt das Hauptquartier aus, folgt eine **Regierungskrise** mit Frist: Kasse eingefroren, keine Kriegserklärung, Diplomatie ausgesetzt, Neuaufbau kostet Geld. Fällt ein Sektor-HQ aus, sinkt die Kontrolle im Sektor.
- **Umzug** des Hauptquartiers ist teuer und mit Ruhezeit möglich.
- **Warum:** Das HQ legt die Richtung fest, die Behörde setzt sie am Ort um. Die Rechenlast bleibt lokal, und ein verlorener Sektor schwächt nicht gleich das ganze Reich. Es spiegelt auch, dass Zentralmacht und Durchsetzung vor Ort verschiedene Dinge sind.

| | **Hauptquartier** (global, eines) | **Sektor-HQ** (lokal, mehrere) |
|---|---|---|
| Krieg und Frieden | ja | nein |
| Diplomatie, Verträge | ja | nein |
| Steuersätze, Haushalt | Rahmen | innerhalb des Rahmens |
| Forschung | Budget und Richtung (Durchführung in der Forschungsstation, siehe FORSCHUNG.md) | nein |
| Sperren, Zoll, Embargo | Richtlinie | Durchsetzung im Sektor |
| Polizeibasis, Patrouillen | Gesamtbudget | Einsatz im Sektor |
| Baugenehmigung | Grundsatz | Einzelentscheidung |
| Bei Verlust | Regierungskrise | Sektor sinkt auf Präsenz oder Einfluss |

## 8. Sektorkontrolle

**Kontrolle** entsteht aus dem **Einfluss** jedes Akteurs: Stationen (nach Typ gewichtet), Sektor-HQ oder Wache, Gate-Kontrolle, Patrouillenpräsenz. `Sector.Owner` existiert schon und wird dann **abgeleitet**, nicht von Hand gesetzt. Änderungen haben Hysterese, damit der Besitzer nicht flackert.

| Stufe | Bedingung | Rechte |
|---|---|---|
| **Präsenz** | mindestens eine eigene Station | Steuern auf die eigene Station |
| **Einfluss** | größter Anteil, aber unter 50 % | Zoll an eigenen Gates der Station, Hausrecht in den eigenen Anlagen |
| **Dominanz** | über 50 % und Wache oder Sektor-HQ | Sektorzoll, Sperrstufe 1–2, Polizeibefugnis, Baugenehmigung für Fremde |
| **Kontrolle** | Dominanz, Sektor-HQ und Patrouillenabdeckung | Sperrstufe 3–5, Sprungsperre, Embargo, Tribut, Kriegsgrund bei Gebietsverletzung |
| **Umkämpft** | zwei Akteure über 30 % | niemand höher als Stufe 1; Konflikte entstehen |

**Warum:** Einfluss beruht auf tatsächlicher Präsenz, nicht auf einer Behauptung. Sektoren werden erobert und verloren: Wer Stationen baut, wächst, wer sie verliert, verliert Rechte. Die Stufen verbinden Sperren (Abschnitt 2) mit echtem Aufwand: Ein Sektor ist erst dicht, wenn jemand ihn durchsetzen kann. „Umkämpft“ ist der natürliche Auslöser von Konflikten, und niemand soll einen Sektor schließen, den er nicht durchsetzen kann.

## 9. Weitere Annahmen und ihr Grund

- **Kriegsmüdigkeit statt Frieden per Zufall:** Sonst enden Kriege nie oder zufällig.
- **Hysterese bei Kontrolle, Sperren und Krieg:** Ohne sie flackert der Zustand bei kleinen Ausschlägen, und die Wirtschaft schwankt.
- **Gewichtete Ziehung statt „der Reichste“:** Reiche sollen bevorzugt werden, aber nicht immer gewinnen; sonst entstehen Monopole.
- **Zölle und Strafen fließen in die Kasse, nicht ins Nichts:** Sonst gäbe es eine Geldquelle ohne Senke, und das Balance-Tool fände die Geldmenge nicht stimmig.
- **Gründung von Akteuren nur mit Besitz, Gebühr und Frist:** Sonst umgehen Spieler Fahndung und Ruf mit Schalenfirmen.
- **Politik nur für Akteure mit Besitz:** Das `FactionPolicySystem` läuft nur für Akteure über einer Besitzschwelle. Das hält die Rechenzeit klein und verhindert, dass ein Einzelschiff einen Krieg erklärt. Einzelakteure entscheiden nur über ihr Schiff.
- **Spieler und NPC nutzen dieselben Regeln:** Einziger Unterschied ist die Entscheidungsquelle (Oberfläche statt KI). Das macht das Spiel erklärbar und testbar.

## 10. Reihenfolge der Fraktionsarbeit

1. Größenzahl und Größenklassen, Zukauf mit Erst- und Zweitzugriff (nur Investitionswahl in der Wirtschaft).
2. Fraktionskasse und Beziehungsmodell.
3. Einflussberechnung, abgeleiteter `Sector.Owner`, Sektorstufen.
4. Dienst `headquarters`, Büro, Firmensitz, Sektor-HQ (siehe STATIONEN.md, Abschnitt 5).
5. Polizeistufen und Zuständigkeit (FLOTTEN.md), Schutzverträge.
6. Krieg mit Stufen, Kriegsgrund, Müdigkeit.
7. Spielerfraktion nutzt dieselben Regeln.

## 11. Offene Entscheidungen

- ~~Gilt eine Sperre für Sektoren oder für einzelne Gates?~~ Entschieden (Phase 10): Sektor-Stufen, Gate-Ausnahme über die Hauptroute.
- Soll die Spielerfraktion früh (direkt nach dem Bauschiff) oder erst am Ende kommen?
- Wie hart soll der Erstzugriff der Großen sein? Vorschlag: weich (nur höheres Gewicht).
- Darf ein Kleinakteur Sektoren erobern oder nur Stationen halten? Eroberung von Stationen ist zunächst abgeschaltet; Sektoren wechseln über Einfluss (Abschnitt 8).
- Soll der Spieler Polizeigewalt bekommen, sobald er Dominanz erreicht, oder bleibt Polizei immer KI?
- Soll ein freies Schiff sofort ein eigener Akteur sein (einfacher, teurer) oder träge zum Akteur werden (Vorschlag)?
- Darf der Spieler einer Fraktion beitreten und aufsteigen, oder bleibt er unabhängig?

## Runde 45: Tribut, Bündnisse, eigene Sperren

Die Entscheidungen dieser Runde traf der Bearbeiter selbstständig (Auftrag „offene Liste abarbeiten“). Bindend blieben: Handel bleibt wie er ist, der Spieler ist eine eigene Fraktion, Krieg ohne Kriegsgrund, Sperren gegen den Spieler ärgern Dritte nicht (38b), Speicherstand bleibt v17 (nur additiv), NPC-Wirtschaft unverändert, solange niemand die neuen Wege benutzt.

**Befund vorab.** `aiWarOnPlayer` steht in der Standardwelt **schon seit Runde 40 auf an** (`default-world.json`, `Runde40Tests.AiWarOnPlayer_StandardweltAn_…`); der Ausweg des Spielers (Waffenstillstand, Frieden, Tribut zahlen per Comms) existiert und ist getestet. Diese Runde ergänzt ihn um „Tribut fordern“ und um Verbündete, die einen angegriffenen Spieler verteidigen. Die Comms-Diplomatie von Runde 40 hatte keinen eigenen Abschnitt in CLAUDE.md; der Entwurf `plan-1.0/hermes-diplomatie.md` beschrieb den Code im Wesentlichen richtig.

### Tribut

| Regel | Wert | Bedeutung |
|---|---|---|
| `aiTributePeace` | Code aus, Standardwelt an | KI-gegen-KI-Kriege können durch Tribut enden |
| `TributePeaceWeariness` | 0,15 | entspricht der Mindestdauer (6000 von 40 000 Ticks); vorher kein Tributfrieden |
| `TributeStrengthRatio` | 1,2 | Sieger ist, wer mindestens 1,2-mal so stark ist (`Wars.Strength`) |
| `TributeAcceptShare` | 0,8 | angenommen, wenn der Verlierer mindestens 80 % der Forderung zahlen kann |
| `TributeMaxSpendableShare` | 0,5 | ein KI-Zahler gibt höchstens die Hälfte seines verfügbaren Geldes — **nie** die Rücklage |
| `PlayerDemandRelationLoss` | 0,03 | fordert der Spieler, sinkt die Achtung aller anderen KI-Fraktionen (wie beim Krieg) |

- **KI gegen KI** (`WarSystem`, alle 100 Ticks): nach der Mindestdauer, ohne laufende Angebots-Abkühlzeit und mit einem klaren Sieger fordert der Sieger `TributeFor` (Grundbetrag × (1 − Müdigkeit) + halbe eigene Verluste, abzüglich schon gezahlten Tributs). Kann der Verlierer genug aus seinem Budget zahlen, zahlt er und es ist Frieden (Grund „tribute“), sonst gilt das als Angebot (`LastOfferTick`) und der nächste Versuch kommt erst nach `OfferCooldownTicks` — kein Flackern. Die Pflicht-Friedensschlüsse („broke“, „weary“) bleiben vorrangig.
- **Spieler fordert** (`demand:<F>`, Comms → Diplomatie, zwei Tipps): **dieselbe Regel** — Mindestdauer, Müdigkeit, klare Überlegenheit, Zahlungsfähigkeit. Geld geht von der KI-Kasse in die Spielerkasse, Frieden, alle anderen KI-Fraktionen −0,03.
- **Spieler zahlt**: unverändert aus Runde 40 (Waffenstillstand/Frieden gegen Tribut, „Tribut zahlen“).
- **Buchung**: jede Zahlung als Paar `TreasuryUpkeep`/`TreasuryIncome` „tribute“ plus `TributePaid` und `TributePeace` — die Kassen bleiben aus Ereignissen exakt erklärbar.
- **Missbrauch**: kein Geldkreislauf. Nach einem Frieden sperrt die Friedens-Abkühlzeit (30 000 Ticks) den nächsten Krieg mit derselben Fraktion; vorab gezahlter Tribut mindert die Forderung (Test: wer erst zahlt und dann fordert, steht um mindestens das Doppelte der Zahlung schlechter); jede Forderung kostet Achtung bei allen Dritten, die nie zurückdriftet (Runde 38). **Bewusst offen:** die Kriegsstärke zählt das Geld mit (wie seit Phase 28), ein sehr reicher Spieler ist darum „überlegen“, auch mit wenigen Schiffen; die KI-Polizei greift ihn im Krieg trotzdem an, und die Forderung ist auf das halbe verfügbare Geld der KI gedeckelt.

### Bündnisse

| Regel | Wert | Bedeutung |
|---|---|---|
| `alliancesActive` | Code aus, Standardwelt an | Hauptschalter |
| `AllianceMinRelation` | 0,6 | Gründung/Annahme ab dieser Beziehung, beide Richtungen |
| `AllianceBreakRelation` | 0,4 | darunter zerbricht das Bündnis (Hysterese) |
| `AllianceMinCooperation` | 0,55 | KI-Fraktionen nur mit genug Kooperation |
| `MaxAlliancesPerFaction` | 2 | kein Netz aus Schutzbündnissen |
| `AllianceCooldownTicks` | 20 000 | nach dem Ende kein neues Bündnis mit demselben Partner |
| `AllianceRestBonus` | 0,4 | Bündnistreue: der Ruhewert der Beziehungsdrift steigt um 0,4 (mit 0,25 zog die Drift ein erzwungenes Bündnis in 4900 Ticks unter 0,4 — gemessen) |
| `AllianceCallTicks` | 2400 | Frist des Beistandsrufs an den Spieler |
| `AllianceBetrayalLoss` / `AllianceDeclineLoss` / `AllianceLeaveLoss` | 0,3 / 0,1 / 0,05 | Achtungsverlust bei Verrat / ausbleibendem Beistand / Austritt |

- **Gespeichert** als `Universe.Alliances` (A, B, seit) und `AllianceEnded` (Paar → Tick); gehasht nur, wenn vorhanden — der Hash ohne Spielerpolitik lässt Paare mit der Spielerfraktion aus. Beitrittskriege tragen `War.Join` (gespeichert, gehasht nur wenn gesetzt).
- **Gründung (KI)**: `AllianceSystem` (Politik, nach dem Krieg, Intervall 100) prüft KI-Paare in Id-Reihenfolge, höchstens eine Gründung je Takt; nicht während eines Kriegs einer der beiden (kein Bündnis, um jemanden in einen laufenden Krieg zu ziehen), keine Gesetzlosen. In der Standardwelt liegen die KI-Beziehungen bei 0,1–0,3: es entsteht kein Bündnis (gemessen, Hash unverändert).
- **Spieler**: schlägt per Comms vor (`alliance:propose:<F>`), die KI entscheidet sofort nach derselben Prüfung; der Posteingang meldet, wann eine Fraktion annehmen würde („Bündnis möglich“). KI-Fraktionen schlagen dem Spieler selbst nichts vor. Austritt jederzeit (`alliance:leave:<F>`). Der Weg dorthin ist die Beziehung: Aufträge heben sie (Runde 41), Krieg, Forderungen und Sperren senken sie.
- **Wirkung 1 — Beistandspakt (defensiv, Tiefe 1)**: erklärt jemand einem Mitglied den Krieg, erklären die **KI-Verbündeten** des Opfers dem Angreifer sofort den Krieg (ohne Kriegskasse und Kriegsgrund, aber nicht in der Friedens-Abkühlzeit mit dem Angreifer). Beitrittskriege rufen niemanden weiter — keine Weltkriegskaskade. Kriegsschiffe kaufen die Beigetretenen nach den normalen Kriegsregeln. Der **Spieler** als Verbündeter wird nicht gezwungen: er bekommt einen Beistandsruf (Posteingang „Beistandsruf“, Taste „Beistand: Krieg gegen …“, ohne Kriegskasse); tritt er binnen 2400 Ticks nicht bei, endet das Bündnis (−0,1). Wird der **Spieler** angegriffen (KI-Krieg auf den Spieler), verteidigen ihn seine KI-Verbündeten. Greift der Spieler eine Fraktion mit KI-Verbündeten an, treten diese gegen ihn bei — Folge seiner eigenen Handlung, nicht KI-Initiative (deshalb nicht hinter `aiWarOnPlayer`).
- **Wirkung 2 — Verbündete an Kontrollpunkten, bei der Zuständigkeit der Polizei und beim Sperrgroll**: `PolicyCheck`, `CargoCheck`, `Jurisdiction.Covers` und `LocksAgainst` behandeln Bündnispartner wie Verbündete. Die alte Beziehungsschwelle `AllyRelation` (0,5) gilt **zusätzlich** weiter — ohne Bündnis ist das Verhalten also genau wie bisher.
- **Auflösung**: Beziehung einer Richtung unter 0,4 („relation“); Angriff auf den Partner („betrayal“); Angriff auf einen Verbündeten des Partners („betrayal_ally“, der Verratene verliert 0,3 und steht dem Opfer bei); fehlender Beistand („no_aid“); Austritt („left“). Danach 20 000 Ticks Abkühlzeit. Zusammen mit der Hysterese 0,6/0,4 verhindert das ein Hin und Her (Test: Beziehung pendelt 40 Mal, höchstens eine Gründung je Abkühlzeit).
- **Nicht gebaut**: Vermittlung durch Dritte, gemeinsamer Kriegsplan, Angriffsbündnisse, Bündnisse von Kleinakteuren.

### Eigene Sperren der Spielerfraktion

**Befund**: Die Sperrlogik (`FactionPolicySystem.SetLevel`) war bereit; es fehlte der Besitz. Die Spielerfraktion besaß keinen Sektor und hatte keinen Weg dorthin.

**Entscheidung (kleinster ehrlicher Weg, keine zweite Regelwelt):**
- **Sektorbehörde gründen** (`seat:found:<Sektor>`, „Meine Fraktion“ → Karte „Sektorpolitik“, zwei Tipps): ein **Sektor-HQ** (`playerSeatType`), fertig gekauft wie eine Kleinstation, Kosten = Baukosten × `StationBuildCostFactor` (Standardwelt 22 000 Cr, Senke), Unterhalt 3,3 Cr/Tick (≈ 790 Cr/min) aus der Spielerkasse über den normalen Regierungsunterhalt, dazu die normale Instandhaltung. Nur in einem **herrenlosen** Sektor **ohne Piratenversteck**, in dem das aktive Schiff steht, ab Größenklasse **Firma** (§7: Sektorpolitik für größere Akteure). In der Standardwelt ist das Nebelmark; Abgrund ist wegen des Verstecks ausgeschlossen (sonst sperrte ein Spieler die Piraten aus ihrer Heimat und schaltete sie billig ab). KI-Sektoren können nicht übernommen werden — dort ist der Einfluss der Altsassen ohnehin uneinholbar, und ein Teilanteil würde Sektoren „umkämpft“ machen und KI-Sperren deckeln.
- **Einfluss nach den bestehenden Regeln** (`SectorControl`): Anteil 100 % + Behörde → **Dominanz** → Sperrstufe höchstens 2 (`StageMaxLevel`). Stufe 3–5 verlangen Kontrolle = Behörde **und** Patrouillenabdeckung; der Spieler hat keine Polizei, darum bleiben sie gesperrt — die Oberfläche nennt den Grund.
- **Sperre setzen** (`lock:<Sektor>:<Stufe>`): über `FactionPolicySystem.SetLevel` mit denselben Regeln wie die KI — Deckel nach Kontrollstufe, Mindestdauer zwischen Änderungen (`LockMinTicks`), Ablauf (`LockMaxTicks`), danach Abkühlzeit. Aufheben auf 0 startet die Abkühlzeit ebenfalls (sonst ließe sich der Ablauf durch Aus/An umgehen). Die KI-Politik entscheidet in Spielersektoren nichts (nur Ablauf und Deckel) — sonst hätte `Desired` die Stufe des Spielers nach 2000 Ticks aus seinen Beziehungen überschrieben.
- **Kosten und Wirkung**: Durchsetzung 0,5 Cr je Stufe und Tick aus der Spielerkasse (leer → Sperre nur auf dem Papier), Zoll an der Einreise in die Spielerkasse, **Groll der KI-Fraktionen nach derselben Formel wie KI-Sperren** (`LockOpinionLoss` × Stufen × 0,5; Verbündete ausgenommen). Weil Spielerbeziehungen nie zurückdriften (Runde 38), summiert sich der Groll — eine Sperre ist ein teures politisches Werkzeug, keine Einnahmequelle.
- **Schließen** (`seat:close:<Station>`): ohne Erlös; die Stufe fällt mit der Kontrollstufe.
- **Nebenwirkung, bewusst gelassen**: Die Kasse der Spielerbehörde über ihrem Betriebskapital wird wie jede Stationskasse besteuert — die Abgabe geht an den Besitzer, also in die Spielerkasse (klein: die Behörde verdient nur an Reparaturen von NPC-Schiffen).

### Messung

- Standardwelt 20 000 Ticks: Hash 1364698EE92E10DC (unverändert), ohne Spielerpolitik 96750C8EBDA8B9EE; 60 000 Ticks DE796E82E883BA84 (unverändert). Kein Bündnis entsteht, kein Tributfrieden — die neuen Wege ändern die NPC-Wirtschaft nur, wenn jemand sie benutzt.
- Vorführung mit Testtreibern des Balance-Tools (`--alliance A:B@tick`, `--aiwar A:B@tick`, `--treasury F:Betrag@tick`): Zahlen in CLAUDE.md „Round 45“, Ausgaben in `/home/gerd/space-adventure-scratch-archiv/runde45/`.

## Spielersperre: Verbrechen senken das Ansehen

Wunsch „Spieler sperren wenn Beziehung schlecht“. Die Sperrregel gab es schon (Runde 38: `FactionPolicySystem.WorstRelation` sieht die Spielerfraktion), aber nichts, was der Spieler verbrach, senkte seine Beziehung — Verbrechen hoben nur die Fahndung. Jetzt senkt jedes gemeldete Verbrechen des Spielers (`Wanted.Report`, nur Spielerakteur, nur KI-Fraktionen) auch sein Ansehen bei der verfolgenden Fraktion (`Simulation/CrimeStanding.cs`, über `PlayerStanding.Change`, Grund „crime:<Schlüssel>“).

- **Werte** (`rules.crimeStandingLoss`, Code-Vorgabe leer = aus, Standardwelt an): Schmuggel −0,04, Zollflucht −0,02, Piraterie −0,10, Angriff −0,10, Angriff im Stationsschutz −0,15, Angriff auf ein Tor −0,15, Abschuss zusätzlich −0,05 (`Wanted.ReportAssault(…, kill: true)` an allen Abschussstellen von `CombatSystem.Finish`).
- **Deckel:** höchstens −0,30 je Fraktion und Tag (`crimeStandingCapPerDay`, festes Fenster von 2400 Ticks ab dem ersten Verlust). Entscheidung: ein Gefecht allein bringt den Spieler von +0,2 auf −0,1 (noch keine Sperre), ein zweiter Tag auf −0,4 (Stufe 2), ein dritter auf −0,7 (Stufe 4 und, mit `aiWarOnPlayer`, Kriegserklärung). So bleibt Zeit zu reagieren (Strafe zahlen).
- **Erholung:** nur solange die Fahndungsstufe bei der Fraktion 0 ist und kein Krieg herrscht, +0,02 je Tag (`crimeStandingRecoveryPerDay`; −0,5 sind nach 25 Tagen ≈ 4 h Spielzeit zurück), höchstens bis zur Beziehung vor dem ersten Verbrechen (`CrimeStandingRecord.Base`; was Frieden, Aufträge oder Bündnis schon gehoben haben, zählt mit). Bezahlte Strafe (Gericht oder Comms) gibt sofort +0,05 (`crimeStandingFineBonus`).
- **Keine eigene Fahndungssperre:** eine hohe Fahndungsstufe allein zählt nicht als feindlich — eine Regel (die Beziehung) erklärt alle Sperren; Fahndung bleibt der Weg zu Festnahme und Handelsverweigerung.
- **Dritte:** Sperren gegen den Spieler gelten für alle (Runde 38) und ärgern Dritte nicht (Runde 38b) — unverändert.
- **Anzeige:** Hinweis „Fahndung bei X: … (Angriff) · Beziehung −0,10“; Fraktionen-Detail neben der Bündniszeile „Ansehen −0,30 durch Angriff (vor 3 min)“ (offen = Abstand zum Ausgangswert).
- **Speicherstand:** additiv `ActorSave.CrimeStanding`, gehasht nur mit Spielerpolitik.
- **Messung** (Balance-Tool `--playercrime F:art[*n]@tick,…`, Ausgaben `/home/gerd/space-adventure-scratch-archiv/spielersperre/`): Standardwelt ohne Verbrechen unverändert (20 000 Ticks F5F1965838C14B59, ohne Spielerpolitik 4A53E782E7FA5B2D). Union: 3 Angriffe bei Tick 2000 → −0,10 (keine Sperre), 3 bei 4500 → −0,40, Alpha/Beta/Borealis/Vesta Stufe 2 gegen dich; 2 Abschüsse bei 7000 → −0,70, Stufe 4; Tick 9000 Kriegserklärung, Frieden (pleite) bei 10 620, Sektoren wieder offen; Zoll der übrigen Welt 41 472 Cr in 40 000 Ticks (1,3 % der Abgaben), 5 Reisen umgeplant, 7 angehalten, 0 Pleiten, Preise im Zielbereich. Gilde mit Schmuggel/Zollflucht über vier Tage: Stufen 1 → 2 → 3 → 4, dann Krieg (Kriegssperren bis Tick 47 000), Zoll 133 868 Cr in 60 000 Ticks, 40 umgeplant, 23 angehalten, 0 Pleiten, Preise im Zielbereich.

## Eigenes Hauptquartier

**Entscheidung des Nutzers:** Der Spieler darf genau ein eigenes Hauptquartier bauen (Stationstyp `headquarters`: Größe XL, Einfluss 6, Sicherheit 1,5, die vorhandene Verteidigung mit sechs Geschützen, Dienste Gericht, Kopfgeld, Reparatur, Hangar, Taxi). Bisher konnte er nur Sektorbehörden gründen (Runde 45).

**Regeln (Schalter `playerHq`, Code aus, Standardwelt an):**
- **Gründen** (`hq:found:<Sektor>`, „Meine Fraktion“ → Karte „Sektorpolitik“, Block „Hauptquartier“, zwei Tipps): nur in einem Sektor, der **der Spielerfraktion schon gehört** (in der Standardwelt also erst über eine Sektorbehörde in Nebelmark), wo das aktive Schiff steht, ab Größenklasse **Konzern** (`playerHqMinClass` 3; §7: das Hauptquartier ist die Form der Großen). Mit Sektorbehörde (Sektor 10 + Station 3 + Schiff) reicht dafür schon etwa 50 000 Cr Kasse. Kosten = Baukosten × `StationBuildCostFactor` = 40 000 × Größenfaktor XL 1,2 = **48 000 Cr** aus der Spielerkasse (Senke, kein Erlös). Platz über dieselbe Freiplatzregel wie die Behörde (Radius des XL-Typs, 48–150 Einheiten vom Schiff).
- **Höchstens eines:** Ein durch Kapitulation übernommenes KI-Hauptquartier zählt als sein Hauptquartier. Solange er eines besitzt (gegründet oder übernommen), ist die Gründung gesperrt („Du hast schon ein Hauptquartier (…)“); er kann das übernommene aufgeben und danach ein eigenes gründen. Zwei übernommene (zwei Kapitulationen) bleiben ihm beide — Kapitulation ist eine Kriegsfolge, keine Gründung; die Aufgabe geht für jedes.
- **Wirkung über die bestehenden Regeln:** Einfluss über `SectorControl`; die Sicherheit 1,5 ist Abdeckung (`CoverageSecurity` 1), damit erreicht der Sektor **Kontrolle** und die Sperrstufe bis 5 (vorher höchstens 2: „Stufe 3–5 brauchen Patrouillenabdeckung, z. B. ein eigenes Hauptquartier“). Die Stufenknöpfe der Karte reichen bis zum Deckel. Eigene bewaffnete Schiffe ohne eigene Heimatbasis bekommen das Hauptquartier als Heimat (Heimbefehl, Notfall-Reparatur dort); beim Abriss löscht `Universe.RemoveStation` die Heimat wie bei jeder Station.
- **Gericht und Kopfgeld am eigenen Hauptquartier:** ohne Sonderregel. Das Gericht nimmt Strafen nach der Rechtshilferegel an (Beziehung der fordernden Fraktion zur Spielerfraktion ≥ `ExtraditionRelation` 0); das Geld geht wie überall an die fordernde Fraktion. Nach Verbrechen sinkt diese Beziehung (Spielersperre), dann lehnt das eigene Gericht ab — ein Krimineller kauft sich nicht am eigenen Schalter frei, wenn ihm die Fraktion zürnt. Kopfgeld gegen die eigene Fraktion gibt es nie (Fahndungen werden der Spielerfraktion nie gemeldet, `BountyOn` ist 0) — das Büro zeigt nichts und zahlt nichts aus.
- **Unterhalt** = Regierungsunterhalt des Typs, 6 × 1,2 = 7,2 Cr/Tick ≈ **1 728 Cr/min** aus der Spielerkasse (Sektorbehörde ≈ 792 Cr/min). Zum Vergleich: ein automatischer Händler der Spielerflotte verdient im Flottentest 1,4–1,7 Cr/Tick (≈ 340–410 Cr/min), das Hauptquartier kostet also etwa vier bis fünf Handelsschiffe. Bewusst gleich wie für die KI — kein Rabatt, kein Sonderrecht.
- **Schulden statt Gratis-Behörde** (Schalter `playerGovernmentDebt`, Code aus, Standardwelt an): Bis Runde 45 galt für jede Regierungsstation „reicht die Kasse für einen Schritt nicht, bleibt er unbezahlt (keine Schulden)“. Für den Spieler hieß das: eine leere Kasse hielt Behörde und Hauptquartier mit Geschützen, Gericht und Einfluss umsonst. Jetzt buchen Regierungsstationen des Spielers (auch übernommene) immer aus der Spielerkasse, notfalls ins Minus — wie Liegegebühr und Leihmiete (Runde 26). Schulden sperren Käufe; der Ausweg ist Aufgeben. KI-Fraktionen unverändert.
- **Aufgeben** (`hq:close:<Station>`): ohne Erlös, Abgang „geschlossen“; Einfluss und Kontrolle fallen sofort, eine zu hohe Sperrstufe sinkt auf den neuen Deckel. Die Sektorbehörde hält den Sektor weiter.
- **Anzeige:** „Meine Fraktion“ zeigt Bedingungen, Kosten und den Grund, wenn es nicht geht, oder Name, Sektor und Unterhalt mit „Hauptquartier aufgeben“; die Fähigkeit „Zoll und Sperren“ nennt „Stufe 1–5“; das Fraktionen-Detail der eigenen Fraktion nennt das Hauptquartier.
- **Speicherstand:** nichts Neues (die Station ist eine gewöhnliche Station, die Regeln stehen in den gespeicherten Regeln); Version bleibt 17.

**Messung:** Standardwelt 20 000 Ticks (Release) Hash 8EEEBEA81B038A74 unverändert — ohne Gründung liest keine Station die neuen Regeln. Test `SchalterAus_OhneGruendung_NpcWeltGleich`: 6 000 Ticks mit beiden Schaltern an und aus bitgleich.

## Ausgleich nach dem Bot-Test

Ein skeptischer Spielerbot (tools/SpaceSim.PlayerBot, Lauf `spielerbot2/lauf4`) und der Vergleich mit Interstellar Pilot 2 / OpenFrontier fanden Schwächen der Spielerpolitik. Jede Änderung hat einen eigenen Schalter (Code-Vorgabe = alte Regel, Standardwelt an; `BalanceFixTests.RevertBalanceFix` nimmt alles zurück).

- **Regierungsunterhalt** (`playerGovernmentUpkeepShare` 0,15; `playerUpkeepFloorActive`, `playerUpkeepFloor` 0, `playerUpkeepResumeBuffer` 2 000): Im Botlauf kosteten sechs Behörden, das Hauptquartier und eroberte Behörden 672 000 Cr/h, die Kasse stand nach 51 h im Minus. IP2 kennt gar keinen Unterhalt. Jetzt zahlt der Spieler 15 % des Typ-Unterhalts: Hauptquartier 7,2 → 1,08 Cr/Tick (≈ 15 600 Cr/h), Behörde 3,3 → 0,5 (≈ 7 100 Cr/h); HQ + 6 Behörden ≈ 58 000 Cr/h ≈ 13 % einer mittleren Flotteneinnahme von 450 000 Cr/h — spürbar, aber tragbar. Eroberte Behörden kosten denselben Anteil (sie bringen Einfluss, Gericht und Kontrolle). **Untergrenze 0** (wie IP2, das Credits bei 0 kappt): würde der Unterhalt die Kasse unter 0 drücken, wird die Station **„unbezahlt“** statt Schulden zu machen — kein Einfluss, kein Grundschutz, zählt nicht als Behörde (die Kontrollstufe und damit die erlaubte Sperrstufe fallen), kein Gericht und kein Kopfgeldbüro. Sie läuft wieder, wenn danach noch 2 000 Cr über der Untergrenze bleiben (kein Flackern). Meldung als Alarm, Zeile in „Meine Fraktion“. Andere gewollte Schulden (Leihmiete, Notreparatur, Strafe bei Kapitulation, Liegegebühr) bleiben möglich.
- **Sperren des Spielers ärgern nur ihr Ziel** (`playerLocksSourOnlyTarget`): Eine Sperrstufe 5 des Spielers trieb alle KI-Beziehungen in 15 000 Ticks auf −1 (Embargos, Flottenhandel brach ein). Wie Runde 38b für Sperren *gegen* den Spieler: Sperren *des* Spielers zählen beim Groll nur für die Fraktion, gegen die sie gerichtet sind (`SectorAccess.Against`). In IP2 reagieren Dritte erst, wenn man sie um Kriegsbeistand bittet.
- **Größenklasse nach Kampfstärke** (`playerSizeByStrength`, `playerSizeStrengthRef` 20, Gewicht 0,25–4): Schiffe zählten je 1, ein Schwarm billiger Schürfer machte schnell einen „Staat“. Nach IP2 (Kampfwertung) zählt ein Spielerschiff jetzt Stärke ÷ 20 (Waffenleistung + Schild/20 + Hülle/50 + 0,5): Schürfer ¼, Händler mit Laser ≈ 0,4, Wächter ≈ 1,2, Zerstörer ≈ 3,1, Schlachtkreuzer 4. Schwellen unverändert; 30 Schürfer sind jetzt eine Firma statt eines Staats. Nur der Spieler (KI-Akteure unverändert); die Befehlsstufe (Runde 24) rechnet weiter mit `commandShipWeight`.
- **Tribut fordern erst nach echten Verlusten** (`tributeDemandNeedsLosses`): Die KI zahlte Tribut ohne jeden Kampf (Kriegsmüdigkeit entsteht auch aus der Dauer). Jetzt muss der Gegner im Krieg mit dem Spieler etwas verloren haben — ein im Gefecht zerstörtes Schiff (`War.ShipsLostA/B`, gezählt nur in Kriegen mit der Spielerfraktion) oder eine Station/ein Tor (`LossA/B`). Freikaufen durch Zahlung (Weg des Zahlers) bleibt.
- **Eigenes Gericht nimmt jede Strafe** (`ownCourtAcceptsAll`): Das Gericht am eigenen Sitz lehnte bei Beziehung < 0 ab, und ein Verbrechen kostet ≈ 0,25 Beziehung. Wie IP2 (Kopfgeld 1:1 ohne Beziehungsbedingung) nimmt das Gericht an Behörde/Hauptquartier des Spielers Strafen jeder Fraktion an; das Geld geht an die Kasse dieser Fraktion (keine Geldschleife), der Ansehensbonus bleibt. Fremde Gerichte unverändert (Rechtshilfe ab Beziehung 0).

## Firmensitz und Büro (Ausbau Punkt 9)

Schalter `firmSeats`, `playerFirmSeat`, `offices`, `playerOffices` (Code aus, Standardwelt an; Regeln in `Model/EconomyRules.Seats.cs`, Logik `Simulation/Seats.cs`, Comms `Comms/SeatOfficeActions.cs`). Aus — oder an, solange niemand gründet — rechnet die Welt bitgleich wie vorher (20 000 Ticks 6D3C01B1ED7DEBCD, 60 000 C0FC1F81475E9B0D mit `--firmseats off`).

### Firmensitz der KI-Firmen

- **Wer:** aufgestiegene unabhängige Akteure (Firmen) ab Größenklasse `firmSeatMinClass` mit Mitgliedschaft; genau ein Sitz je Firma (Typ `company_seat`, Größe L, Einfluss 3). **Abweichung von der Vorgabe „ab Firma“:** gemessen erreicht in 300 000 Ticks keine Firma die Klasse Firma (höchstens 4 Schiffe, Größe 4,3; Schiffe sind arm, Ø 2 600 Cr). Die Standardwelt setzt deshalb `firmSeatMinClass` 1 (Kleinfirma); der Code-Standard bleibt 2. Mit dem Sitz (Station × 3) steigt die Firma dann auf Firma.
- **Wo:** im Sektor ihrer **Mitgliedsfraktion** mit dem meisten eigenen Besitz (Stationen × 3 + Schiffe), höchstens `firmSeatsPerSector` 2 Sitze je Sektor. Begründung: der Einfluss 3 zählt für die Mitgliedsfraktion — in einem fremden oder herrenlosen Sektor würde ein Firmensitz Grenzen verschieben (Nebelmark bliebe dem Spieler nicht offen). Firmen ohne Mitgliedschaft gründen nicht.
- **Kosten:** Baukosten × Baukostenfaktor × `firmSeatCostShare` 0,4 (≈ 8 800 Cr; die Firmen sind klein, der Rest entfällt — fertig gekauft wie eine Behörde, keine Baustelle). Die Firma gründet erst, wenn ihr verfügbares Geld (Kasse + Schiffskassen über `firmShipKeep` 1 000 + Stationskassen über dem Betriebskapital) mindestens Kosten × `firmSeatMoneyFactor` 1,5 beträgt; bezahlt wird aus Kasse, dann Stationen, dann Schiffen (Id-Reihenfolge, Rentabilität neutral). Abklingzeit `firmFoundCooldownTicks` 6 000 zwischen Gründungen (Sitz und Büro).
- **Firmenkasse:** Der Sitz schöpft je 50 Ticks `firmSkimShare` 0,3 des positiven Gewinns jedes Firmenschiffs (Rentabilitätsmessung) über dem Rückhalt ab und `firmSkimPerTick` 0,2 %/Tick des Stationsüberschusses über dem Betriebskapital — nur solange die Kasse unter `firmTreasuryTarget` 15 000 liegt. Aus der Kasse stockt die Firma arme Schiffe bis zum Rückhalt auf (über einer Rücklage von einem Spieltag Sitz-Unterhalt) und kauft Schiffe (Kassen-Kandidat in der Zukaufziehung `Investors.ForShip`, Ort = erstes geparktes Schiff der Rolle) und Stationen (`Investors.ForStation`, wie bisher). Ereignisse `TreasuryIncome` „firm_skim“, `TreasuryUpkeep` „firm_capital“/„company_seat“, `InvestorChosen` (Kasse) — die Kassen bleiben aus Ereignissen erklärbar.
- **Unterhalt:** `firmSeatUpkeepShare` 0,1 des Typ-Unterhalts (2,2 → 0,22 Cr/Tick) aus der Firmenkasse; die Mitgliedsfraktion subventioniert Sitz und Büros einer Firma nicht. Trägt die Kasse einen Unterhaltsschritt `firmSeatArrearsTicks` 4 800 lang nicht, wird der Sitz aufgegeben (Grund b, „Sitz unbezahlt“). **Sitz verloren** (unbezahlt oder zerstört) → keine Abschöpfung, kein Schiffskauf aus der Kasse; die Firma verhält sich wieder wie vorher (die Restkasse bleibt Stationsinvestor wie jede Akteurskasse).
- **Gemessen:** Mit 0,5 Unterhaltsanteil fraß der Sitz fast alles Abgeschöpfte; mit `firmSkimShare` 0,5 sanken die Schiffskassen und zwei Händler gingen pleite. Ohne Kassenziel und Aufstocken lagen 80 000–140 000 Cr ungenutzt in Firmenkassen (die KI-Flotte steht an der Kapazitätsgrenze, Schiffskäufe aus der Kasse sind selten) — deshalb Ziel 15 000 und Rückfluss an arme Schiffe.

### Büros

- **Typ** `office` (Größe S, Einfluss 1,5, andockbar): Dienste Hangar und Auftragsbrett (Runde 41 hängt „Aufträge“ an jede andockbare Station; der Hangar kam neu in den Typ). **Schutzverträge** kommen mit Punkt 3 — Haken `Offices.ProtectionContracts` (heute immer nein), die Stationsinfo sagt es.
- **Zustimmung des Gastgebers** (`Offices.HostIssue`, für KI und Spieler gleich): nur im Sektor einer **anderen** KI-Fraktion; Beziehung beider Richtungen ≥ `officeMinRelation` 0, kein Krieg, keine durchgesetzte Sperre gegen den Gründer (Stufe ≥ 4 oder ≥ 1 gegen ihn). Fällt die Zustimmung weg, schließt `OfficeSystem` das Büro (Grund c „unerwünscht“, auch Spielerbüros, nicht während einer Belagerung).
- **KI:** Je Fraktion höchstens `officesPerFaction` 3 Büros, Abklingzeit `officeCooldownTicks` 12 000; Ort = fremder Sektor mit den meisten laufenden Handels- und Schürfaufträgen von Schiffen der Fraktion samt Mitgliedsfirmen (ab `officeMinTrade` 3), in dem sie noch keine Station hat; bezahlt aus `FactionViability.Spendable`. Firmen mit Sitz: höchstens ein Büro, ab 2 eigenen Aufträgen, aus der Firmenkasse.
- 1,5 Einfluss verschiebt in den gemessenen Läufen keinen Besitz und macht keinen Sektor umkämpft.

### Spieler

- **Firmensitz** (Comms `firmseat:found:<Sektor>` / `firmseat:close:<Station>`, „Meine Fraktion“ → Karte „Sektorpolitik“, zwei Tipps): genau einer, im **eigenen oder herrenlosen** Sektor ohne Piratenversteck, aktives Schiff dort, ab Größenklasse `playerFirmSeatMinClass` 2 (Firma), volle Kosten 22 000 Cr (Senke), Unterhalt als Regierungsunterhalt mit Spieleranteil. Wirkung: **Befehlsstufe +`firmSeatCommandBonus` 1** (Befehlskapazität eine Klasse höher, höchstens Staat), **Fehden freigeschaltet** (`PlayerSeatsOffices.FeudUnlocked`; seit Punkt 6 echt: Fehden per Comms, „Meine Fraktion“: „Fehde erklären · freigeschaltet · Comms“), Einfluss 3 (im herrenlosen Sektor wird er damit Besitzer, Stufe Einfluss — Sperren brauchen weiter eine Behörde). Aufgeben ohne Erlös.
- **Büro** (Comms `office:found:<Sektor>` / `office:close:<Station>`): im fremden Sektor mit Zustimmung des Gastgebers, eines je Sektor, ab `playerOfficeMinClass` 1 (Kleinfirma), 4 500 Cr, Unterhalt mit Spieleranteil. Hangar und Auftragsbrett in fremden Sektoren.
- **Anzeige:** Fähigkeiten in „Meine Fraktion“ (Firmensitz, Fehde, Büros) statt der Platzhalter; Fraktionen-Detail: Firmensitz und Firmenkasse einer KI-Firma, Sitz und Büros der eigenen Fraktion; Stationsinfo: „Firmensitz von … — Firmenkasse …“ bzw. „Büro von … — Hangar und Auftragsbrett; Schutzverträge folgen (Punkt 3)“.

### Messung (Release, Standardwelt; vorher = Stand Punkt 8)

| | 20 000 | 60 000 | 300 000 |
|---|---|---|---|
| Hash vorher → nachher | 6D3C01B1ED7DEBCD → 9D6D0AD5906A7040 | C0FC1F81475E9B0D → 426643BEE164E802 | 36FADEC9DA5EB827 → D4D8541348E459C5 |
| Urteil | im Zielbereich | im Zielbereich | im Zielbereich |
| Sitze / Büros (gegründet, verloren/geschlossen) | 7 / 6 (0/0) | 9 / 11 (0/0) | 13 / 13 (0/0) |
| abgeschöpft, Firmenkassen am Ende | 40 652 / 16 009 Cr | 251 825 / 36 797 Cr | 1 824 303 / 35 917 Cr |
| Geldmenge vorher → nachher | 1,83 M → 1,78 M | 2,31 M → 2,39 M | 2,27 M → 2,26 M |
| Kassenanteil am Ende | 21 % → 22 % | 28 % → 28 % | 27 % → 26 % |
| Händler/Schürfer pleite oder nicht rentabel | 0 → 0 | 0 → 1 | 0 → 1 |
| Export Nahrung/KG/Luxus | 19 880/2 206/355 → 19 268/2 257/370 | 72 438/10 769/2 476 → 71 624/11 307/2 629 | 390 387/66 589/14 815 → 388 435/66 803/16 322 |

Firmen 24–25 (vorher 24); Schiffskäufe aus Firmenkassen selten (0–1: die KI-Flotte steht an der Kapazitätsgrenze), das Geld fließt über das Aufstocken an die Schiffe zurück. Neue HINWEIS-Zeilen nur aus der bekannten Klasse (Werft-Regale, Stahlwerk Kalypso am Deckel bei 300 000).

## Fehden, Vermittlung, Regierungskrise (Ausbau Punkt 6)

Schalter `feudsActive`, `playerFeuds`, `mediation`, `playerMediation`, `governmentCrisis` (Code aus, Standardwelt an; Regeln in `Model/EconomyRules.Feuds.cs`, Logik `Simulation/Feuds.cs` mit `Feuds`, `Mediation`, `GovernmentCrisis` und `FeudSystem` im Politik-Modul nach den Bündnissen, Intervall 100; Comms `Comms/FeudActions.cs`). Ohne feindliche Beziehung mit umkämpftem Sektor, ohne Krieg und ohne Verlust eines Hauptquartiers greift keine Regel — die Standardwelt rechnet bitgleich wie mit allen fünf Schaltern aus (gemessen, siehe unten). Spielstand bleibt v17 (additiv: `SaveGame.Feuds`, `FeudEnded`, `Crises`, `WarSave.MediationBonus/LastMediationTick`; gehasht nur, wenn gesetzt; Fehden mit der Spielerfraktion nur im Hash mit Spielerpolitik).

### Fehde

- **Beginn (KI):** zwei kriegsfähige KI-Fraktionen (Staat/Gilde), die schlechtere Beziehung beider Richtungen unter `feudRelation` −0,4 **und** mindestens ein zwischen ihnen umkämpfter Sektor (beide über dem Umkämpft-Anteil); kein Krieg, kein Bündnis, keine laufende Fehde einer der beiden, keine Fehde desselben Paares in den letzten `feudCooldownTicks` 12 000. Höchstens eine neue Fehde je Takt (Paare in Id-Reihenfolge). Eine KI kann dem Spieler nach derselben Regel eine Fehde erklären — aber nur, wenn er Fehden freigeschaltet hat (Firmensitz).
- **Dauer:** `feudTicks` 6 000 (25 min). Ende von selbst nach Ablauf („expired“), wenn sich die Beziehung beider Richtungen über `feudEndRelation` −0,2 erholt („relation“, nur KI-Fehden — Spielerbeziehungen driften nicht), wenn Krieg herrscht („war“: die Kriegsregeln von Phase 28 entscheiden — jeder Überfall senkt die Beziehung um `feudRaidRelationLoss` 0,02, so kann eine Fehde unter die Kriegsschwelle −0,6 rutschen), durch Vermittlung („mediated“) oder durch den Spieler („ended“).
- **Wirkung:**
  - **Überfälle:** je Fehde höchstens alle `feudRaidTicks` 600 Ticks stellt ein einsatzbereites Polizei- oder Kriegsschiff einer Seite ein Handelsschiff der Gegenseite — zuerst in den zwischen beiden umkämpften Sektoren, dann im eigenen Gebiet des Angreifers (dort sind seine Streifen; ein umkämpfter Sektor kann während der Fehde wieder ruhig werden, und ohne diese Erweiterung fand eine erzwungene Fehde gemessen keinen einzigen Überfall). Handelsschiff der Gegenseite = Mitglied der Fraktion oder unterwegs, um an eine ihrer Stationen zu verkaufen; beim Spieler seine nicht aktiven Flottenschiffe (wie die Flottenüberfälle der Piraten). Das Gefecht hat den neuen Anlass `EncounterCause.Feud`: Angreifer mit der Kaperer-Taktik, Opfer wehrt sich, flieht oder wirft Fracht ab; die überfallende Fraktion beschlagnahmt `feudSeizeShare` 0,5 des Werts in ihre Kasse (`TreasuryIncome` „feud“, keine Hehlerei, kein Piratendeckel). „Beute schonen“ gilt wie bei Piraten. Keine Fahndung (KI gegen KI), keine Belagerung, keine Eroberung — eine Fehde ist kein Krieg (`Wars.Hostile` bleibt falsch).
  - **Sperren:** in den Sektoren beider Seiten mindestens `feudLockLevel` 2 gegen die Gegenseite (über `FactionPolicySystem.Desired`, gedeckelt durch die Kontrollstufe wie jede Sperre).
- **Spieler** (Comms `feud:declare:<F>` / `feud:end:<F>`, Diplomatie-Detail „Fehde erklären“ (zwei Tipps) / „Fehde beenden“): nur mit Firmensitz (`PlayerSeatsOffices.FeudUnlocked`), nicht im Krieg, nicht verbündet, Abkühlzeit wie oben. Die Beziehung der Gegenseite zu ihm fällt auf höchstens −0,45 (Sperren gegen ihn folgen), alle anderen KI-Fraktionen −`feudThirdPartyLoss` 0,02 (leicht). Während der Fehde ist ein Angriff des Spielers auf **Schiffe** seines Fehdegegners kein Verbrechen (wie im Krieg); Stationen bleiben geschützt (Stationsangriff bleibt ein Verbrechen).
- **Anzeige:** Fraktionen-Detail und Comms-Diplomatie zeigen „Fehde mit … (noch m:ss)“ (`Feuds.FactionLine`), Nachrichten „Fehde – …“, „Fehde vorbei – …“; „Meine Fraktion“: „Fehde erklären · freigeschaltet · Comms“ bzw. „läuft · Comms“.

### Vermittlung

- **KI:** In jedem Krieg und jeder Fehde zwischen KI-Fraktionen, der mindestens `mediationMinTicks` 2 000 läuft, vermittelt die dritte KI-Fraktion mit der besten Beziehung zu beiden, wenn sie in beiden Richtungen mindestens `mediatorRelation` 0,25 hat und mit keiner im Krieg oder in Fehde ist. Beide Seiten heben ihre Beziehung zur Vermittlerin um `mediationRelationGain` 0,1. **Krieg:** Zuschlag `mediationWearinessBonus` 0,25 auf die Kriegsmüdigkeit (`War.MediationBonus`, zählt in der Müdigkeitsformel; höchstens eine Vermittlung je `mediationCooldownTicks` 10 000); **Fehde:** endet. Konflikte mit der Spielerfraktion vermittelt die KI nicht (sein Ausweg bleibt Comms: Waffenstillstand, Frieden, Tribut).
- **Spieler** (Comms `mediate:<A>:<B>`, Diplomatie-Detail „Vermitteln: Krieg/Fehde mit …“; Nachricht „Konflikt – …“ beim Beginn mit Antwort, sobald möglich): beide Parteien mindestens `playerMediatorRelation` 0,1 zu ihm, er selbst mit keiner im Krieg/in Fehde, Mindestdauer und Abkühlzeit wie bei der KI. Beide zahlen ihm je höchstens `mediationFee` 1 500 Cr, höchstens die Hälfte ihres verfügbaren Geldes (`TreasuryUpkeep` „mediation“, nie unter die Rücklage), beide +0,1 Ansehen bei ihm, Wirkung wie oben. Entscheidung: „für Geld“ heißt hier, der Spieler **bekommt** eine Gebühr (er leistet einen Dienst).

### Regierungskrise

- **Auslöser:** eine KI-Fraktion verliert ihr **Hauptquartier** (zerstört oder kapituliert). Der Not-Sitz aus `SeatRelocation` entsteht trotzdem sofort (Handlungsfähigkeit bleibt).
- **Dauer** `crisisTicks` 12 000; Wirkung: nur `crisisTaxFactor` 0,5 der Steuern erreicht die Kasse (der Rest verlässt die Welt wie bei der Steuerbremse), **keine freiwilligen Polizeikäufe** (der Ersatz der Mindestpolizei mit Vorrang und der Notkauf bleiben — keine Todesspirale), Sperren in ihren Sektoren höchstens `crisisMaxLockLevel` 1, ihre Sektoren laufen schon mit `crisisOwnerSwitchMargin` 0,02 Vorsprung statt 0,1 über. Ein zweiter HQ-Verlust verlängert die Krise. Ereignis `GovernmentCrisisChanged`, Nachricht „Regierungskrise – …“, Fraktionszeile „Regierungskrise (noch m:ss)“.

### Messung (Release, Standardwelt)

| Lauf (60 000 Ticks, Standardwelt) | Ergebnis |
|---|---|
| Standardwelt 20 000 / 60 000, Schalter an gegen `--feuds off` | bitgleich: 12D6D4F32D86E79B / 70CBA501910FEFB7 — keine Fehde, keine Krise, keine Vermittlung (KI-Beziehungen +0,12…+0,25, nichts umkämpft); Preise im Zielbereich |
| `--feud 1:2@2000` (Beziehung −0,5, Fehde erzwungen) | 9 Fehdeüberfälle, Fehde läuft ab (Tick 8000); die Beziehung sinkt weiter, die Union erklärt der Gilde bei Tick 9 900 den Krieg (−0,62), Tributfrieden bei 15 900 (11 686 Cr). Händlerabgänge Ø 0,08 je 5 000 Ticks, Verlustquote 0,20 %, Preise im Zielbereich |
| `--aiwar 2:1@4000` + Kassen, ohne Vermittlerin | keine Vermittlung (Montanbund 0,2 zu beiden — unter 0,25); bitgleich mit `--feuds off`; Tributfrieden bei 10 000 für 7 274 Cr |
| dasselbe mit `--mediator 3@5900` (Montanbund 0,5 zu allen) | Montanbund vermittelt bei Tick 6 000 (Müdigkeit +0,25); Tributfrieden bei 10 000 für 4 771 Cr (die höhere Müdigkeit senkt die Forderung) |
| `--crisis 1@5000` | Regierungskrise der Union von 5 000 bis 17 000; Preise im Zielbereich, keine Händlerabgänge, kein Sektor wechselt den Besitzer |

Testtreiber im Bilanzwerkzeug: `--feud A:B@tick`, `--crisis F@tick`, `--mediator F@tick`, `--feuds on|off`; Zeile „Fehden (Punkt 6 …)“ mit Beginn, Überfällen, Enden nach Grund, Vermittlungen, Krisen.
