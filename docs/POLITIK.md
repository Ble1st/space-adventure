# Politik: Kontrollpunkte, Sperren, Folgen (Planung)

**Status: reine Planung, nichts davon ist umgesetzt.** Setzt das Fraktionsverhalten voraus. Heute hat `Faction` nur `Relations` und `Territory`, die nichts benutzt, und `Gate` nur den Schalter `Open` (siehe [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md), Abschnitte 4–6). Fortsetzung: [FLOTTEN.md](FLOTTEN.md).

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

## 6. Offene Entscheidungen

- Gilt eine Sperre für Sektoren oder für einzelne Gates? Vorschlag: Sektor-Stufen als Voreinstellung, Gate-Ausnahmen erlaubt.
- Soll die Spielerfraktion früh (direkt nach dem Bauschiff) oder erst am Ende kommen?
- Hat eine Fraktion eine eigene Kasse (`Treasury`)? Heute besitzen nur Stationen und Schiffe Credits; Zölle, Strafen und Polizeikosten brauchen einen Ort. Vorschlag: ja, gespeist aus Zöllen und Steuern.
