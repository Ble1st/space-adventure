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
