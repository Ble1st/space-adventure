# Stationen

Abschnitt 1 beschreibt den Ist-Zustand (umgesetzt). Die Abschnitte 2 bis 5 sind **Planung, nichts davon ist umgesetzt**.

## 1. Bestehende Stationstypen (18)
Werte aus `Content/default-world.json`. Rezept: Dauer in Ticks, Eingänge → Ausgänge.

| Typ | Name | Baukosten | Rezept |
|---|---|---|---|
| solar_plant | Solarkraftwerk | 4000 | 5: – → 8 Energie |
| powerplant | Kraftwerk | 3500 | 5: 1 Wasserstoff → 20 Energie |
| steelworks | Stahlwerk | 5000 | 5: 3 Eisenerz + 1 Energie → 2 Stahl |
| water_plant | Wasserwerk | 3500 | 5: 1 Eis + 1 Energie → 1 Wasser |
| electrolysis | Elektrolyse | 4500 | 5: 2 Eis + 2 Energie → 1 Wasserstoff |
| copper_smelter | Kupferhütte | 5000 | 5: 3 Kupfererz + 1 Energie → 2 Kupfer |
| wafer_fab | Waferfabrik | 6000 | 5: 3 Silizium + 2 Energie → 2 Wafer |
| titanium_works | Titanhütte | 7000 | 6: 3 Titanerz + 2 Energie → 2 Titan |
| platinum_refinery | Platinraffinerie | 9000 | 8: 2 Platinerz + 2 Energie → 1 Platin |
| farm | Farm | 3500 | 5: 1 Wasser + 1 Energie → 2 Nahrung |
| electronics_fab | Elektronikfabrik | 8000 | 10: 2 Kupfer + 2 Wafer → 2 Elektronik |
| plating_works | Panzerwerk | 8000 | 10: 4 Stahl + 1 Titan → 2 Hüllenplatten |
| fuel_cell_fab | Brennstoffzellenfabrik | 9000 | 10: 4 Wasserstoff + 1 Platin → 4 Brennstoffzellen |
| life_support_fab | Lebenserhaltungswerk | 7000 | 10: 3 Wasser + 1 Elektronik → 2 Lebenserhaltung |
| goods_factory | Konsumgüterfabrik | 7000 | 10: 1 Stahl + 1 Elektronik → 1 Konsumgut |
| luxury_workshop | Luxuswerkstatt | 10000 | 12: 1 Platin + 2 Elektronik → 1 Luxusgut |
| shipyard | Werft | 12000 | 20: 2 Hüllenplatten + 1 Elektronik + 1 Brennstoffzelle → 1 Schiffsbausatz |
| colony | Kolonie | 9000 | Verbraucht 4 Nahrung + 1 Konsumgut + 1 Lebenserhaltung (10 Ticks), 1 Luxusgut (20 Ticks); Einkommen 8 |

Ausgangswelt: Alpha (Eisen, Kupfer, Wasser, Energie), Beta (Silizium, Elektronik, Konsumgüter), Gamma (Farm, Kolonie, Lebenserhaltung, Luxus), Delta (Titan, Platin, Panzerwerk, Brennstoffzellen, Werft). Alle Startstationen sind `essential`, also vor Pleite geschützt.

## 2. Ideenliste neuer Stationen (nicht umgesetzt)

### Neue Produktionsketten
| Station | Zweck |
|---|---|
| Recyclinganlage | Schrott und Wracks werden zu Eisen, Kupfer, Titan. Schließt Kreisläufe. |
| Chemiefabrik | Wasser, Eis, Silizium → Kunststoff. Neues Zwischenprodukt (Schiffstreibstoff entsteht in der Treibstoffraffinerie). |
| Pharmalabor | Wasser, Chemikalien, Platin → Medikamente. Zweite Endnachfrage neben der Kolonie. |
| Waffenfabrik | Stahl, Elektronik, Titan → Waffensysteme (mit Kampf). |

### Schiffsbauteile und Treibstoff (entschieden, siehe SCHIFFSSLOTS.md)
| Station | Stellt her | Hinweis |
|---|---|---|
| **Antriebs- und Reaktorwerk** | Reaktoren, Speicherbänke, Triebwerke, Treibstofftanks, Frachtmodule | Stufe nach Stationsgröße (klein Mk1, mittel Mk2, Hub Mk3) |
| **Waffen- und Schildwerk** | Geschütze, Schilde, Panzerung | gleiche Staffelung |
| **Systemwerk** | Laser, Traktor, Sensoren, Automatisierung; später Sprungantrieb und Tarnfeld | gleiche Staffelung |
| **Treibstoffraffinerie** | 3 Wasserstoff + 1 Energie → 4 Treibstoff | Startwerte |
| **Gasernte-Plattform** | Rohware `gas` | siehe Rohstoffe |
| **Gasraffinerie** | 2 Gas + 1 Energie → 1 Ionengas | Startwerte |
| **Anreicherungsanlage** | 4 Eis + 2 Energie + 1 Platin → 1 Fusionsbrennstoff | Startwerte |

Module sind Waren; jedes Modul hat eine Ware gleichen Schlüssels, sein `cost` ist das Rezept der Fabrik.

**Lagerführung ohne Rezept (umgesetzt in Phase 5a: `stocks` am Stationstyp; die Werft führt Module, die drei Modulfabriken stehen in Alpha, Beta und Gamma):** Ausrüster, Handelsstation und Raffinerie sollen Module bzw. `ship_kit_light` im Lager führen und verkaufen, haben aber kein Rezept, aus dem der Markt entsteht (heute legt `StationFactory` Markteinträge nur aus Rezepten an). Vorschlag: Stationstypen bekommen eine Liste `stocks` (Waren mit Zielbestand), die zusätzlich zum Rezept Markteinträge erzeugt; Händler beliefern sie über den normalen Handel.

### Rohstoffe und Fundorte
| Station | Zweck |
|---|---|
| Minenaußenposten | Direkt am Gürtel, kauft Erz sofort. Kürzere Wege für Miner. |
| Eisförderstation | Dasselbe für Eisfelder. |
| Gasernte-Plattform | Neue Rohstoffart ohne Asteroiden (Nebel, Gasriesen). |
| Wrack-Bergung | Zahlt für Schrott, den der Spieler sammelt. |

### Dienste für den Spieler
| Station | Zweck |
|---|---|
| Reparaturdock | Reparatur gegen Geld (heute gratis beim Andocken). |
| Tankstelle | Treibstoff mit marktabhängigem Preis. |
| Handelsposten | Spieler kauft und verkauft frei (heute nur Verkauf). |
| Ausrüster | Laser, Traktor, Frachtraum, Schilde. |
| Schiffshändler | Schiffskauf mit Bausätzen. |
| Missionsbüro | Lieferung, Abbau, Eskorte. |
| Bar / Treffpunkt | Gerüchte, Preisinfos, Besatzung. |
| Bank | Kredite und Zinsen; Geldsenke und -quelle. |

### Sicherheit und Fraktionen
| Station | Zweck |
|---|---|
| Jumpgate-Station | Zoll und Wartung am Tor; Fraktionen können sperren (`Gate.Open`). |
| Patrouillenbasis | Spawnt Polizeischiffe. |
| Militärwerft | Baut Kampfschiffe, braucht Waffen und Schilde. |
| Piratenversteck | Illegaler Markt, Gegenspieler der Fraktionen. |
| Forschungsstation | Schaltet Technologien frei, siehe [FORSCHUNG.md](FORSCHUNG.md). |
| Fraktions-Hauptquartier | Essenziell, Ankerpunkt für Beziehungen und Krieg. |

### Handel und Finanzen (neu, nicht umgesetzt)
| Station | Funktion | Warum |
|---|---|---|
| **Handelsstation** (Handelsposten) | Marktplatz ohne Produktion für viele Waren, kleine Marge, hoher Durchsatz; Medium, in Knotensektoren | Hält Bestände vieler Waren vor, dämpft Preisspitzen und löst das Pendelhandel-Problem durch einen Knoten |
| **Börse** | Preis- und Lagerinformation anderer Stationen, Versteigerung beschlagnahmter Fracht | Information ist ein Spielzug; Beschlagnahme kehrt in die Wirtschaft zurück |
| **Lagerhaus / Depot** | Waren gegen Gebühr einlagern (Spieler und Akteure) | Zeit wird Dimension im Handel; Grundlage für Baustellenlager; Ziel für Plünderung |
| **Frachtterminal** | Sammelpunkt für Konvois, Eskortverträge, Verbandsbildung | Die Konvoimechanik braucht einen Treffpunkt |
| **Bank** | Kredite, Zinsen, Konten | Geldsenke und -quelle; bindet den Zukauf kleiner Akteure an Kapital |
| **Versicherung** | Deckt Verluste durch Überfall/Zerstörung gegen Prämie, Prämie folgt der Bedrohung des Sektors | Verbindet Piraten, Polizei und Wirtschaft |

### Sicherheit und Infrastruktur (neu, nicht umgesetzt)
| Station | Funktion | Warum |
|---|---|---|
| **Rettungs- und Schleppdienst** | Holt havarierte Schiffe ab (Sprit leer, Hülle kaputt), gegen Gebühr | Löst Liegenbleiber aus dem Treibstoffkonzept; Dienst für den Spieler |
| **Sensorstation / Radarposten** | Erhöht die Sensorstärke im Sektor, liefert Daten für die Lagekarte | Wahrnehmung wird eine Investition, die man bauen und verteidigen kann |
| **Sprungbake** | Markiert Ankunftspunkte, erzeugt oder hebt Sprungsperren auf | Sprungantrieb und Sperren brauchen einen räumlichen Anker |
| **Gericht / Haftanstalt** | Verwahrt Festgesetzte Schiffe und Fracht, verwaltet Bußgeld und Haft | Strafe bekommt einen Ort; Beschlagnahme geht an die Börse |
| **Kopfgeldbüro** | Nimmt Jäger an, zahlt Kopfgeld aus der Fraktionskasse (`bounty`) | Rolle als Jäger für Spieler und NPCs; verbindet Fahndung mit Einkommen |
| **Schwarzmarkt / Piratenversteck** | Illegaler Markt, keine Zölle, keine Polizeikontrolle im Versteck | Ohne Gegenpol hat die Polizei nichts zu tun und Embargos sind folgenlos |

**Empfohlene Reihenfolge der Stationsideen:** Handelsstation, Lagerhaus, Rettungsdienst, Kopfgeldbüro mit Gericht; danach Sensorstation, Bank, Versicherung; Börse, Sprungbake und Schwarzmarkt mit Piraten und Sprungantrieb.

### Spezielle Orte
Alien-Artefakt-Lager (seltene Güter, Missionen), Raumdock-Hotel (reine Geldsenke), Schiffsfriedhof (Wrackfeld mit zufälliger Bergung).

## 3. Mögliche Zusammenlegungen (nicht umgesetzt)
Zwei Arten: **Produktionskomplex** (mehrere Rezepte hintereinander, Zwischenprodukte bleiben intern) und **Servicehub** (mehrere Dienste an einem Ort). Hinweis: Das bestehende `water_plant` heißt bereits "Wasserwerk", die Kombination mit der Elektrolyse hieße daher besser *Wasserkomplex*.

### Bestehende Stationen
| Komplex | Enthält | Effekt |
|---|---|---|
| Wasserkomplex | Wasserwerk + Elektrolyse | Eis → Wasser und Wasserstoff direkt |
| Energiekomplex | Solarkraftwerk + Kraftwerk | Energie aus einer Hand, hilft beim Bootstrap |
| Stahl- und Panzerwerk | Stahlwerk + Panzerwerk | Eisen → Hüllenplatten (braucht weiter Titan) |
| Edelmetallraffinerie | Titanhütte + Platinraffinerie | Beide Erze der seltenen Asteroiden |
| Halbleiterwerk | Waferfabrik + Elektronikfabrik (Kupferhütte optional) | Silizium → Elektronik |
| Systemfabrik | Brennstoffzellen + Lebenserhaltung | Ähnliche Eingänge |
| Konsumkombinat | Konsumgüter + Luxuswerkstatt | Beide bedienen die Kolonie |
| Agrarkomplex | Farm + Wasserwerk | Farm mit eigenem Wasser |
| Siedlung | Kolonie + Farm | Teilweise Selbstversorgung |
| Werftkomplex | Werft + Panzerwerk (+ Schiffshändler) | Bausätze selbst herstellen und direkt verkaufen |

### Neue Stationen
| Komplex | Enthält |
|---|---|
| Servicedock | Reparatur + Tankstelle + Ausrüster |
| Handelszentrum | Handelsposten + Bar + Missionsbüro |
| Bank- und Verwaltungszentrum | Bank + Fraktions-HQ + Missionsbüro |
| Förderposten | Minenaußenposten + Eisförderstation + kleine Tankstelle |
| Verwertungswerk | Recycling + Chemiefabrik |
| Rüstungswerk | Waffen + Schilde + Triebwerke |
| Marinebasis | Militärwerft + Patrouillenbasis |
| Torwache | Jumpgate-Station + kleine Patrouille |
| Piratenhafen | Piratenversteck + Wrack-Bergung + Schiffsfriedhof |
| Forschungscampus | Forschungsstation + Pharmalabor |
| Freizeitstation | Hotel + Bar |

### Abwägung
Dafür: weniger Stationen und Routen (Rechenaufwand, weniger Pendelhandel), weniger Zwischenprodukte im Markt, Komplexe fangen die Pleite einzelner Rezepte auf.
Dagegen: weniger Preise für Zwischenprodukte (schlechter lesbar), Klumpenrisiko bei Ausfall, weniger Ziele für Händler, der Lebenszyklus (Neubau bei Knappheit) arbeitet bisher pro Produkt und müsste pro Rezept bewerten.

### Empfehlung
Statt harter Fusionen: ein Stationstyp besteht aus **Modulen** (`recipes: [...]`, `services: [...]`), frei kombinierbar in der JSON. Die Systeme laufen schon pro Rezept; neu wäre nur eine `IStationService`-Schnittstelle für Dienste im Dock-Panel.
Drei Größen: **klein** (ein Rezept, am Feld), **mittel** (zwei bis drei Rezepte), **Hub** (Dienste + Handel, höchstens einer pro Sektor).
Zuerst Servicehubs und Werft bündeln. Bei den Produktionsketten nur Wasserkomplex, Energiekomplex und Halbleiterwerk, weil deren Zwischenprodukte kaum separat gehandelt werden. Den Rest getrennt lassen, damit die Märkte lebendig bleiben.

## 5. Funktionen, Dienste und Regierungsstationen (Planung)

### Bestehende Stationen nach Funktion

Heute haben alle Stationen genau eine Funktion: den **Markt** (Handel) und das kostenlose Andocken mit Reparatur. Die Tabelle ordnet die 18 Typen nach ihrer Rolle in der Kette und nennt, **welche Dienste** (siehe [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md), Abschnitt 7) natürlich zu ihnen passen. Das Warum: Der Dienst sitzt dort, wo die Ware sowieso entsteht, damit sein Preis aus der Wirtschaft kommt.

| Gruppe | Typ | Funktion | Natürlicher Dienst (Vorschlag) |
|---|---|---|---|
| Energie | Solarkraftwerk | Energie ohne Eingang | – |
| Energie | Kraftwerk | viel Energie aus Wasserstoff | – |
| Veredelung | Stahlwerk | Eisenerz → Stahl | – |
| Veredelung | Wasserwerk | Eis → Wasser | `refuel` (Wasser für Ionenantriebe) |
| Veredelung | Elektrolyse | Eis → Wasserstoff | `refuel` (Wasserstoff) |
| Veredelung | Kupferhütte | Kupfererz → Kupfer | – |
| Veredelung | Waferfabrik | Silizium → Wafer | – |
| Veredelung | Titanhütte | Titanerz → Titan | – |
| Veredelung | Platinraffinerie | Platinerz → Platin | – |
| Komponenten | Elektronikfabrik | Kupfer + Wafer → Elektronik | `outfitter` (Elektronikmodule) |
| Komponenten | Panzerwerk | Stahl + Titan → Hüllenplatten | `repair` (verbraucht Hüllenplatten) |
| Komponenten | Brennstoffzellenfabrik | Wasserstoff + Platin → Brennstoffzellen | `refuel` (Brennstoffzellen) |
| Komponenten | Lebenserhaltungswerk | Wasser + Elektronik → Lebenserhaltung | – |
| Endgüter | Farm | Wasser + Energie → Nahrung | – |
| Endgüter | Konsumgüterfabrik | Stahl + Elektronik → Konsumgut | – |
| Endgüter | Luxuswerkstatt | Platin + Elektronik → Luxusgut | – |
| Schiffe | Werft | Hüllenplatten + Elektronik + Brennstoffzelle → Bausatz | `shipdealer` (alle Klassen), `upgrade`, `hangar` |
| Nachfrage und Geldquelle | Kolonie | verbraucht Nahrung, Konsum-, Luxusgüter, Lebenserhaltung; Einkommen 8 | Missionsbüro, Bar (später) |

Alle Typen bieten außerdem den Dienst `trade`.

**Kleine Schiffe** gibt es nicht nur an der Werft: Handelsstation (Skiff, Kurier), Ausrüster (Skiff mit Modulpaket) und Raffinerie (leichter Bergbauer, kleiner Tanker) bieten `shipdealer` mit Einschränkung; sie montieren das Schiff aus einem leichten Bausatz im Stationslager (siehe [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md), „Kleine Schiffe an anderen Stationen“).

**Hangar, Taxi und Leihschiff:** Stationen mit Liegeplätzen bieten `hangar` (angedockte Schiffe, Liegegebühr), viele auch `taxi` (der Spieler reist allein, das Schiff bleibt) und `rental` (Leihschiff). Das Andock-Menü zeigt zuerst die angedockten Schiffe und die verfügbaren Bereiche (siehe [UI.md](UI.md)).

### Regierungsstationen (neu)

Hier geht es um Machtstrukturen (siehe [POLITIK.md](POLITIK.md)). Alle sind **Dienste auf Stationen**, nicht Produktionsketten. Sie passen ins Stationsmodul-Modell (`recipes` + `services` frei kombinierbar).

| Form | Größe | Wer baut sie | Dienste | Anzahl |
|---|---|---|---|---|
| **Hauptquartier** | Hub | Staat | `headquarters`, `treasury`, `diplomacy`, `police_hq` | genau eines je Fraktion |
| **Firmensitz** | Medium | Firma, Konzern | `headquarters_company`, `treasury`, Verträge | eines je Akteur |
| **Sektor-HQ** (Sektorbehörde) | Medium | Staat, Konzern mit Dominanz im Sektor | `sector_authority`, `customs`, `police_base`, Baugenehmigung | höchstens eine je Sektor und Besitzer |
| **Büro** | Dienst an Station | Kleinfirma | `office` (Konto, Zoll auf der eigenen Station) | eines je Station |
| **Polizeiwache** | Dienst oder Klein | jede Fraktion mit Station | `police` (Scan, Zoll, Kopfgeldbüro) | je Station |
| **Patrouillenbasis** | Medium | Staat, Konzern | `police_base` (Heimat, Tanken, Reparieren für Polizeischiffe) | je Sektor mit Bedarf |

- **Hauptquartier als Hub:** Pro Sektor gibt es höchstens eine Hub-Station, also konkurriert das Hauptquartier mit anderen Großstationen (zum Beispiel einer Werft) um den Platz. Das Warum: Es macht das HQ zu einem greifbaren Ziel und bindet es an knappen Raum.
- **Hub-Konflikt:** Auch die Werft für Kapitalschiffe ist eine Hub-Werft (SCHIFFE.md). Da es pro Sektor nur einen Hub gibt, liegen Hauptquartier und Kapitalwerft in verschiedenen Sektoren, oder die Hauptstadt hat nur eines von beiden. Das ist gewollt (Entscheidung zwischen Verwaltung und Rüstung), aber noch nicht abschließend festgelegt.
- **Sektor-HQ ist keine Hub-Station:** Es ist mittelgroß, damit jeder kontrollierte Sektor eine bekommen kann, ohne den Hubplatz zu blockieren. Das Hauptquartier liegt in der Hauptstadt, die Sektorbehörden in den übrigen Sektoren.
- **Voraussetzungen:** Eine Sektorbehörde braucht mindestens **Dominanz** im Sektor (über 50 % Einfluss). Ein Hauptquartier braucht die Größenklasse Staat. Beide sind bankrottgeschützt und `Government` (siehe unten); ihr Verlust folgt eigenen Regeln.
- **Verlust:** Fällt ein Hauptquartier aus, folgt eine Regierungskrise. Fällt eine Sektorbehörde aus, sinkt die Kontrolle im Sektor auf Präsenz oder Einfluss.

### `Essential` aufgedröselt (entschieden; `BankruptcyProtected` und `Critical` umgesetzt in Phase 8)

**Heute** ist `Station.Essential` ein einzelnes Flag (aus der JSON, im Save), das an genau einer Stelle wirkt: `StationLifecycleSystem` entfernt bankrotte Stationen nicht, wenn es gesetzt ist. Alle Startstationen sind `essential`. Die Docs haben das Wort für mehr benutzt (Zerstörung, Eroberung, HQ). Das wird in getrennte Eigenschaften zerlegt:

| Eigenschaft | Bedeutung | Quelle |
|---|---|---|
| `BankruptcyProtected` (heute `Essential`) | wird bei Pleite nicht entfernt | JSON, Startwelt, Regierungsstationen |
| `Critical` | einzige oder letzte erreichbare Station einer Kette; löst den Notbau früher aus | **abgeleitet**, nicht gespeichert |
| `Government` | Hauptquartier, Firmensitz, Sektor-HQ, Büro: eigene Verlustregeln (Regierungskrise, Kontrollverlust) | aus dem Dienst |
| `Capturable` | kann erobert werden | Regel `allowStationCapture` (derzeit aus) und Typ; Regierungsstationen nie |
| `Destructible` | kann zerstört werden | Typ; Standard ja, erst mit Kampf relevant |
| `Immortal` | unzerstörbar (Tutorial, Szenario) | nur JSON |

- `Critical` wird beim Prüfen berechnet (gibt es eine andere erreichbare Station, die dieselbe Ware herstellt?) und ist kein Save-Feld.
- Die JSON-Eigenschaft `essential` bleibt zunächst als Alias für `BankruptcyProtected` erhalten, damit alte Welten und Saves unverändert laden.
- Eigene Eigenschaften machen jede Regel prüfbar und verhindern, dass ein Flag unbeabsichtigt mehr schützt als gedacht.


# Phase 30: Stationsgrößen und Kleinstationen – Plan und Fortschritt

Vom Nutzer angenommen: Größenklassen für Stationen „ähnlich Schiffe“, dazu vier Kleinstationen (Satellit, leichte, mittlere, schwere Waffenstellung,
Konzept in `docs/platforms/KONZEPT.md`). Maßgeblich ist die Weltdatei (`stationTypes[].size`, bei XS zusätzlich `radius`); `godot/assets/stations/stations.json`
gleicht der Blender-Agent danach an.

## Größenklassen (Regel im Core, `StationSizes`)

| Klasse | Radius | Grundhülle (ohne `defense`) | Geschütze höchstens (inkl. PD) | Andocken | Kosten/Unterhalt |
|---|---|---|---|---|---|
| XS | je Typ (`radius`) | je Typ (`defense.hull` Pflicht) | 7 | nicht andockbar | × 1 |
| S | 10 | 600 | 2 | Schiffe bis M | × 0,9 |
| M | 15 | 800 | 3 | Schiffe bis L | × 1 |
| L | 22 | 1400 | 4 | alle | × 1,1 |
| XL | 30 | 2500 | 6 | alle | × 1,2 |

Was am Radius hängt: Kollisionskreis/Hindernis, Andockplatz (Radius + 7), Liegeplatz-Erkennung (Radius + 15), Schutzzone gegen Überfälle (Radius + 20,
nur andockbare Stationen), Geschütz-/Raketenreichweite ab der Außenhaut, Belagerungsabstand, Platzwahl (Tore Radius + 35, Stationen Summe der Radien + 20,
Felder Radius + 30, Asteroiden Radius + Asteroid + 6), Modellmaßstab und Picking in der Ansicht.

## Fortschritt

- [x] Plan (diese Datei)
- [x] Größenklasse je Typ in der Weltdatei, Validierung (Radius, Geschützgrenze), Speichern
- [x] `Station.Radius` statt fester 15 in allen Regeln, Andock-Regel (Schiffsgröße) in Spieler-Dock und KI-Zielwahl
- [x] Startwelt: Positionen geprüft/verschoben (Ausweichen, Autopilot, Zollstation) — HQ Alpha, Elektrolyse Gamma, Werft Delta verschoben
- [x] Kleinstationen: vier Typen, Verhalten (Abfangen in Reichweite), Satellit ohne Schutzzone, Bau/Abbau durch Fraktionen, Piraten am Versteck, Startwelt-Exemplare
- [x] Ansicht: Modelle (Radius aus dem Typ), Türme, nicht andockbar („Zu klein“/„Kleinstation“), Kartensymbole — Zielkarte/Kurzinfo nutzen die allgemeine Stationszeile
- [x] Kolonie Gamma: Kassenlage (nicht nur im Frühspiel: dauerhaft ≈ 1000 Cr) — Einkommen 20 → 30, Luxus-Grenze wieder 0,45
- [x] Tests (`Phase30Tests`), Selbsttest-Stufe 93, Balance vorher/nachher, CLAUDE.md

# Eigene Stationen bauen (Nutzerentscheidung: der Spieler baut selbst)

Bisher legte nur der KI-Lebenszyklus Baustellen an. Jetzt baut auch der Spieler eigene Wirtschaftsstationen. Schalter `rules.playerBuilding` (Code aus, Standardwelt an). Aus — oder an, solange niemand baut — ist die NPC-Welt bitgleich (20 000 Ticks Release: Hash 8EEEBEA81B038A74 unverändert).

## Ablauf

1. **Baufenster** — eigener Bereich „Bauen“ in der Kommandozentrale (Leiste nach „Besitz“; ein eigener Bereich passt besser als ein weiterer Block in der schon vollen Karte „Meine Fraktion“). Links alle baubaren Typen (`PlayerBuilding.Catalog`): Produktion, Handelsstation, Planetenhandel, Lagerhaus, Raffinerie, Modulwerke, Werft, Kolonie … — **nicht**: Regierungstypen (dafür gibt es Behörde und Hauptquartier), Verstecke, Forschungsstationen, Kleinstationen (XS), Baustelle, Typen eines abgeschalteten Systems. Je Eintrag: Bausumme, Stückliste (`ConstructionRules.Bill`), Unterhalt, Einfluss, Rezepte (Zutaten → Erzeugnisse je Zyklus), Größe und Radius, „hier möglich“ oder der Grund. Darüber die eigenen Baustellen (Materialstand, „Baustelle aufgeben“ mit zwei Tipps).
2. **Platzierungsmodus im Flug** — „Bauplatz wählen“ schließt die Zentrale, das Schiff hält an, vor dem Bug steht ein durchsichtiger Geist des Stationsmodells mit Radius-Ring. Das Platzierungs-HUD ersetzt das Flug-HUD: Steuerkreuz ▲ ▼ ◀ ▶ (verschiebt um 8 m in der Ebene, relativ zur Kamera), „↻ 15°“, „↻ 45°“, „Abbrechen“, „Platzieren“ — alle Tasten 64 px. Am Desktop Pfeiltasten (60 m/s), Q/E drehen, Eingabe setzt, Escape bricht ab (Zurück-Taste ebenso). Grün = gültig, rot mit Grundzeile.
3. **Die eine Platzregel** — `StationPlacement.Check` (Core, rein): Platzierungsfläche ±150 × Maßstab, Tor Radius + 35, Station Summe der Radien + 20, Gürtelmitte Radius + 30, Fels Radius + Fels + 6. `StationLifecycleSystem.FindFreeSpot` fragt seit dieser Runde dieselbe Funktion (gleiche Vergleiche in gleicher Reihenfolge — Hash unverändert). Für den Spieler kommen davor die Typ-/Sektorbedingungen (`PlayerBuilding.TypeIssue`) und danach der Abstand zum eigenen Schiff (Radius + 10).
4. **Bauen im Core** — „Platzieren“ ruft `PlayerBuilding.Start`: Bausumme = Baukosten (mit Größenfaktor) × `StationBuildCostFactor` aus der Spielerkasse als Treuhandgeld, gewöhnliche Baustelle über `ConstructionRules.StartSite` (Besitzer und Investor = Spielerakteur; eine verlassene Baustelle im Sektor wird **nicht** übernommen — der gewählte Platz gilt). Material bringen eigene Bauschiffe mit Befehl „Bauen“ (aus dem Treuhandgeld bezahlt) oder NPC-Händler über den normalen Handel (Gebot Basispreis × 1,25 wie bei KI-Baustellen; im Test liefern Händler in 20 000 Ticks). Fertig gehört die Station dem Spieler.
5. **Drehung** — rein optisch (der Core rechnet mit Kreis-Hindernissen), `Station.Rotation` in Grad, additiv in `StationSave.Rotation` gespeichert und nur gehasht, wenn gesetzt (0 = alle KI-Stationen, alte Spielstände). Die Baustelle trägt sie, die fertige Station übernimmt sie, die Sektoransicht dreht das Modell.

## Entscheidungen

- **Erlaubte Sektoren:** eigene und herrenlose ohne Piratenversteck. **Fremde Sektoren nicht** (auch nicht mit Zoll oder guter Beziehung): eine Spielerstation gäbe dort Einfluss gegen den Besitzer und könnte Besitz kippen lassen — das wäre ein politischer Akt ohne Gegenzug. In der Standardwelt also Nebelmark (Abgrund hat das Versteck) und später eigene Sektoren.
- **Obergrenzen:** Spielerstationen zählen **nicht** gegen `MaxStations`/`MaxStationsPerSector` der KI (`StationProtection.CountEconomic` lässt sie mit Regel an weg; auch Baustellen des Spielers zählen nicht als KI-Baustellen oder als „Typ schon im Bau“), damit der Lebenszyklus nicht verhungert. Eigene Grenzen: höchstens 2 Baustellen gleichzeitig (`playerMaxSites`), 10 eigene Wirtschaftsstationen (`playerMaxStations`), 4 je Sektor (`playerMaxStationsPerSector`). Den Platz teilen sich KI und Spieler über dieselbe Platzregel.
- **Montage braucht ein eigenes Bauschiff** (Polier, an Werften käuflich, Befehl „Bauen“). KI-Bauschiffe übernehmen keine Spielerbaustelle (sonst fehlten sie den KI-Baustellen); die Fraktion kauft für eine Spielerbaustelle kein Bauschiff aus der Spielerkasse; es gibt **keinen Notbau** (er erschüfe Material) und **keinen Verfall** — die Baustelle wartet auf den Spieler, er kann sie aufgeben (Rest des Treuhandgelds zurück, geliefertes Material verfällt).
- **Kein Startkapital:** eine fertige Spielerstation beginnt mit 0 Cr (KI-Stationen bekommen 2000 Cr neues Geld); der Rest des Treuhandgelds verschwindet wie bei der KI als Kreditanteil, das eigene Bauschiff erhält die Montagegebühr (10 %) in die Spielerkasse.
- **Unterhalt** aus der Stationskasse, der Rest aus der Spielerkasse (die Regel der übernommenen Stationen). **Grundeinkommen höchstens der Unterhalt:** das Grundeinkommen der Typen (Raffinerie 1,5, Kolonie 30 je Tick) hält KI-Stationen am Leben; beim Spieler wäre es Geld ohne Ware zum Abschöpfen. Eine ruhende eigene Station kostet also nichts, verdient aber nichts — Geld kommt nur aus verkaufter Ware.
- **Nie als pleite oder unrentabel entfernt** (schon für übernommene Stationen so: Lebenszyklus und Überprüfung lassen Spielerstationen aus); keine Subvention (nur KI-Fraktionen).
- **Exportsenke des Spielers ohne Aufschlag:** der Planet zahlt einer Spielersenke nur, was sie bezahlt hat. Mit Aufschlag könnte der Spieler an seine eigene Senke verkaufen und 18 % neues Geld schöpfen. (In der Standardwelt ohnehin nicht baubar: alle Planetensektoren gehören KI-Fraktionen.)
- **Geld umbuchen:** „Besitz“ → „Stationen“: je Station Kasse, Produktion (Auslastung), Lager, Unterhalt und „+1000 einzahlen“, „−1000 auszahlen“, „Alles auszahlen“ (`PlayerBuilding.Transfer`, reine Umbuchung, Rentabilitätsmessung neutral). Nur fertige Stationen — das Treuhandgeld einer Baustelle bleibt gebunden. Die Stationsinfo zeigt „Deine Station: Produktion n %“.

## Missbrauchsprüfung (ehrlich)

Geprüft (Test `EigeneStationenTests.Missbrauch_KeineGeldschleifeUeberEigeneStationen` und Überlegung):
- **Kauf/Rückverkauf an der eigenen Station** — Geld wandert nur zwischen Spielerkasse und Stationskasse (Summe gleich); der Rückkaufdeckel (`PlayerLedger`) gilt auch dort, eigene Erzeugnisse nimmt die Station ohnehin nicht zurück (`NoIntake`). 30 Runden, mit und ohne Takt dazwischen: die Summe steigt nie.
- **Ruhende Station** — 3000 Ticks ohne Handel: kein Geldzuwachs (Grundeinkommen ≤ Unterhalt).
- **Baumaterial selbst liefern und aufgeben** — der Spieler bekommt für Lieferungen an die eigene Baustelle Geld aus seinem eigenen Treuhandgeld; beim Aufgeben kommt nur der Rest zurück: Summe = vorher (minus Einkaufspreis des Materials).
- **Eigene Exportsenke** — Planet zahlt genau den Ankaufspreis (kein Aufschlag).
- **Vermögensabgabe** der Spielerstation fließt in die Spielerkasse (Umbuchung, keine Bremse beim Spieler). **Instandhaltung** erstattet die Spielerkasse der eigenen Station (Umbuchung).
- **Eigene Werft/Raffinerie/Reparatur** — Kauf von Schiffen, Treibstoff, Reparatur an der eigenen Station bezahlt die eigene Station (Umbuchung, Material wird wirklich verbraucht); Schiffsverkauf im Hangar zahlt ½ des Materialwerts — Verlust.
- Nicht ausgeschlossen, aber kein Geldkreislauf: Die eigene Station verkauft Ware an NPC-Händler zum Marktpreis und kauft Zutaten — das ist die gewollte Einnahme (echte Ware, echtes Geld anderer).

## Prüfungen

Tests `EigeneStationenTests` (12): Schalter und Katalog, Platzregel = KI-Regel mit Gründen, Bedingungen (fremder Sektor, Geld, Schiffsabstand, abgedockt, Versteck, Planet), Kosten/Treuhand/eigene Grenzen/zählt nicht gegen die KI, Aufgeben, Fertigstellung mit eigenem Bauschiff (Besitzer, Drehung, kein Startkapital, nie pleite entfernt), KI-Bauschiffe nehmen keine Spielerbaustelle/kein Notbau/NPC-Händler liefern, Drehung gespeichert und gehasht, Speichern/Laden mitten im Bau bitgleich, Schalter aus = gleiche NPC-Welt, Umbuchung Nullsumme, Missbrauch, Grundeinkommen gedeckelt. Selbsttest-Stufe 143 (`SelfTest.PlayerBuild.cs`). Prüfwerkzeug: Zustände `kz-build`, `flug-platzierung2` (grün) und `flug-platzierung-rot`.

## Ausgleich nach dem Bot-Test: kein Baulimit, Bau in fremden und Piratensektoren, Wirtschaftsstationen wehren sich

Die Entscheidungen oben („nur eigene oder herrenlose Sektoren“, „höchstens 2/10/4“) sind durch drei Nutzerentscheidungen ersetzt (Schalter in `rules`, Code-Vorgabe = alte Regel, Standardwelt an):

- **Kein Baulimit** („Spieler soll kein Baulimit haben“): `playerMaxSites`, `playerMaxStations`, `playerMaxStationsPerSector` gelten mit **0 als unbegrenzt**; die Standardwelt setzt alle drei auf 0. Auch die Zahl gleichzeitiger Baustellen ist frei — technisch nötig ist sie nicht: jede Baustelle bindet ihr Treuhandgeld sofort und braucht zur Montage ein eigenes Bauschiff vor Ort. Platzregel, Sektorbedingungen und Kasse gelten weiter. Damit ist auch der Botbefund „39 von 10“ (eroberte Stationen zählten gegen die eigene Obergrenze) erledigt — die Anzeige nennt keine Obergrenze mehr („Eigene Baustellen (n)“).
- **Fremde Sektoren** („Bau auch möglich in Sektoren anderer Fraktionen“, `playerBuildForeignSectors`): erlaubt, solange der Spieler mit dem Besitzer **nicht im Krieg** ist, dessen Beziehung zu ihm **über** `hostileRelation` (−0,15) liegt und **keine durchgesetzte Sperre** ihn ausschließt (Stufe ≥ 4 schließt alle Fremden aus, jede Stufe ≥ 1, die gegen ihn gerichtet ist, ebenso). Der Einfluss der Spielerstation zählt normal in der Sektorkontrolle. Wird der Sektor dadurch zwischen Spieler und Besitzer **umkämpft** (beide über `contestedShare`), sinkt **nur diese eine Beziehung** um `contestOpinionLoss` je Beziehungsschritt (100 Ticks) — dieselbe Regel wie zwischen KI-Fraktionen, Dritte bleiben unberührt (Test `Umkaempft_SenktNurDieBeziehungZumBesitzer`). Das ist bewusst teuer (0,02 je 100 Ticks): wer einem Besitzer den Sektor streitig macht, verärgert ihn bald bis zur Feindschaft — danach verweigert der Besitzer weiteren Bau.
- **Piratensektoren** („auch in Sektoren mit Piratenversteck“, `playerBuildHideoutSectors`): erlaubt (Abgrund). Der Abstand zum Versteck folgt der gemeinsamen Platzregel wie zu jeder Station. Überfälle laufen nach den normalen Regeln (Piraten überfallen Schiffe, nicht Stationen; das Versteck hat Einfluss 0). Gründung von Behörde/Hauptquartier bleibt dort verboten (eigene Regeln).

**Wirtschaftsstationen wehren sich** (`economicStationDefense`, Botbefund: ein Zerstörer für ≈ 7 900 Cr nahm geschützlose Wirtschaftsstationen in 70–300 Ticks): Wirtschaftsstationen der Größe M/L/XL **ohne** eigenen `defense`-Abschnitt bekommen eine Vorgabeverteidigung — Hülle = Grundhülle × 3 (M 2 400, L 4 200, XL 7 500), Schild = 10 % der Grundhülle (M 80, L 140, XL 250; lädt 2 % je Sekunde), Panzerung 10, Geschütze 1/2/2 × Laserkanone S (`economicStationGuns`). Die Geschütze feuern nur auf Angreifer der Station selbst (`economicGunsSelfDefenseOnly`): Überfälle in der Nähe und die NPC-Wirtschaft ändern sich nicht (ohne Krieg bitgleich). Regierungsstationen, Werft, Versteck, Kleinstationen und S-Stationen behalten ihre Werte. Nach einer Kapitulation kann eine Station **480 Ticks (120 s, wie IP2)** nicht erneut kapitulieren (`capitulationCooldownTicks`; Station.CapitulationLockUntil gespeichert).

## Stationsverschleiß (Ausbau Punkt 8, umgesetzt)

Bis hier war offene Instandhaltung (Phase 15) nur ein Statuswert — ohne Material verfiel nichts. Jetzt hat jede instand gehaltene Station einen **Zustand** (`Station.Integrity`, 0–100 %, gespeichert additiv und gehasht nur, wenn ≠ 100 %). Schalter `stationWear` (Code-Vorgabe aus = alte Regel, Standardwelt an; Regeln in `Model/EconomyRules.Wear.cs`, Logik `Simulation/StationWear.cs`, aufgerufen aus `MaintenanceSystem`, Takt 50, skaliert mit dt, ohne Zufall).

- **Verfall:** Liegt die offene Instandhaltung (Basiswert) über `wearBacklogShare` (50 %) des Tagesbedarfs, sinkt der Zustand um `wearLossPerDay` (1 %) je Tag. **Regierungsstationen** (jede Behördenart) und **geschützte Startstationen** (`BankruptcyProtected` aus der Welt) höchstens bis `wearProtectedFloor` (50 %).
- **Erholung mit Material:** Liegt der Rückstand unter `wearRecoverBelowShare` (10 %), repariert die Station mit Material aus ihrem Instandhaltungslager: +`wearRecoverPerDay` (2 %) je Tag, je 1 % Zustand `wearRepairNeedPerPoint` (0,5) Tagesbedarfe — +2 %/Tag kosten also einen zusätzlichen Tagesbedarf; fehlt Material, nur anteilig; nie über 100 %. Die Fraktionskasse erstattet das Reparaturmaterial wie die Instandhaltung.
- **Wirkung:** unter `wearSlowBelow` (70 %) laufen Rezepte mit dem Faktor Zustand ÷ 70 % auf Zutaten **und** Ausstoß je Takt (gleiche Wirkung wie eine um den Kehrwert längere Taktzeit, ohne neuen Zustand und ohne Zufall); unter `wearStopBelow` (30 %) steht die Produktion (zählt als Pause, nicht als Zutatenmangel). **Andocken, Handel und Dienste bleiben** — eine stillstehende Station kann also weiter beliefert werden und sich erholen. Geschützte/Regierungsstationen kommen nie unter 50 % und laufen schlimmstenfalls mit 71 %.
- **Notinstandhaltung** (`wearEmergencyBelow`, Standardwelt 75 %, Code 0 = aus): liegt eine Station einer **KI-Fraktion** unter 75 % und ist ihr Rückstand über der Verfallsschwelle, kauft die Kasse den offenen Rückstand zum Basispreis × `wearEmergencyMarkup` (2) — nur aus `FactionViability.Spendable`, nie aus der Rücklage. Das Material wird nicht aus der Wirtschaft gezogen (Geldsenke wie der Notbau, `TreasuryUpkeep` „wear_emergency“, Ereignis `StationWearEmergency`). Damit bleibt der Zustand knapp unter 75 % stehen; erholen kann sich die Station nur mit echtem Material. Spielerstationen bekommen keine Notinstandhaltung (sie verfallen, bis der Spieler liefert). Eine Fraktion ohne Geld lässt ihre Stationen verfallen; ungeschützte Stationen, die dadurch stillstehen, verlieren Geld und fallen über die Rentabilitätsprüfung (Phase 22, `StationReview`) heraus; geschützte und Regierungsstationen halten 50 % (keine Zerfallsspirale).
- **Anzeige:** Stationsinfo „Zustand n %“ und darunter die Wirkung („Produktion 79 %, erholt sich“, „verfällt: Instandhaltung offen“, „Stillstand (Produktion steht)“); Sektoransicht der Kommandozentrale (Karte der gewählten Station) zeigt die Zeile bei Verschleiß und für eigene Stationen; „Besitz → Stationen“ stellt den Zustand vor die Lagerzeile. Balance-Werkzeug: Zeile „Stationsverschleiß (Punkt 8)“ (Stationen, Mittel, unter 70/30 %, Wechsel, Reparatur, Notinstandhaltung, schlechteste mit offenem Rückstand), Optionen `--wear on|off`, `--wearbid`, `--weardays`, `--wearrepair`.

**Warum die Notinstandhaltung (Messung).** Ohne sie blieben im großen Maßstab die kleinen Stationen (Wasserwerke, Elektrolysen, Farmen, Kraftwerke; Tagesbedarf 2–3 Stahl) **nie** beliefert: Ein Wunschbestand von zwei Tagesbedarfen lohnt keinem Händler die Fahrt (Gewinn × Menge minus Unterhalt × Flugzeit), sie verfielen ununterbrochen 1 %/Tag. 300 000 Ticks ohne Notinstandhaltung: 63 von 85 Stationen unter 70 %, 4 gebaute auf 0 %, Export Nahrung/Konsumgüter/Luxus −6/−13/−32 %. Verworfen: höheres Gebot der verschlissenen Station (`wearMaxBidFactor` 1,6–2,0) und größerer Wunschbestand (`wearStockDays` 6–15 Tage) — beides zog Stahl aus der Konsumgüterkette: 60 000 Ticks Konsumgüterexport −35 bis −90 %, 300 000 Ticks −43 % (Luxus −59 %). Beide Knöpfe bleiben als Regel (Vorgabe 0 = aus).

**Messung** (Release, Standardwelt, vorher = `--wear off --navycool off`):

| Lauf | Hash vorher → nachher | Preise | Ø Zustand (Messmittel) | unter 70/30 % | Notinstandhaltung | Instandhaltung erstattet | Export Nahrung/KG/Luxus |
|---|---|---|---|---|---|---|---|
| 20 000 | 48ECEC8C1781997D → 6D3C01B1ED7DEBCD | im Zielbereich | 94,4 % (97,0 %) | 0 / 0 | 0 | 118 235 → 121 602 Cr | 19 522/2 365/353 → 19 880/2 206/355 |
| 60 000 | 8B66C504B54B9DA3 → C0FC1F81475E9B0D | im Zielbereich | 86,4 % (91,7 %) | 0 / 0 | 0 | 488 315 → 507 867 Cr | 72 311/10 981/2 515 → 72 438/10 769/2 476 |
| 60 000, Saat 2 | 3F4F62E592FAF41B → 57FF1CB0DF3ADF74 | im Zielbereich | 86,5 % (92,2 %) | 0 / 0 | 0 | — | 87 157/8 658/940 → 76 695/10 045/2 019 |
| 300 000 | D59605CF2FDEB0EF → 36FADEC9DA5EB827 | im Zielbereich | 85,2 % (85,9 %) | 0 / 0 | 5 569× · 940 754 Cr | 2 809 539 → 2 814 810 Cr | 388 965/66 073/15 286 → 390 387/66 589/14 815 |

Stahl Ø Preis/Basis im letzten Viertel 1,23/1,21/1,19 → 1,21/1,17/1,18; das Reparaturmaterial ist 3–10 % zusätzliche Instandhaltungsnachfrage (Stahl, Hüllenplatten, Elektronik). Die Notinstandhaltung kostet über 300 000 Ticks 0,94 Mio. Cr (≈ 4 % der Abgaben in die Kassen); der Kassenanteil an der Geldmenge bleibt 27–28 %. Keine Station fällt unter 70 %, keine Stilllegung durch Verschleiß, keine Händlerpleiten. Die schlechtesten Stationen sind die kleinen Startstationen, die nie beliefert werden (75 %, gedeckt durch Notinstandhaltung). Der Spieler spürt den Verschleiß an eigenen Stationen (keine Notinstandhaltung) und in Kriegen/Pleiten (leere Kasse → Verfall bis 50 % bzw. Stillstand ungeschützter Stationen).

Tests `StationsverschleissTests` (13, mit Nachtrag 2b): Schalter, Verfall 1 %/Tag bis 0, geschützte/Regierungsstationen nicht unter 50 %, Erholung +2 %/Tag mit einem zusätzlichen Tagesbedarf und anteilig ohne Material, Notinstandhaltung (nur unter 75 %, Preis, nie unter die Rücklage, nie Spielerstation), Produktion 100 %/80 %/0 % je Zustand und Regel aus, Speichern/Laden bitgleich und Hash nur bei ≠ 100 %, Langlauf 20 000 Ticks Standardwelt. Rücknahme `StationsverschleissTests.RevertWear` in der Kette über `BalanceFixTests.RevertBalanceFix`. Selbsttest-Stufe 149 (`SelfTest.Wear.cs`).
# Ungenutzte Regale werden günstiger (Ausbau Punkt 12, umgesetzt)

Regale — stock-only Markteinträge ohne Rezept (`Station.IsStockOnly`, z. B. die Modulregale der Werften und Handelsstationen) —, die lange niemand anfährt, senken ihren Preis: Pro Regaleintrag wird der Tick der letzten Bewegung (Kauf oder Lieferung) gemerkt (`StockEntry.LastMove`, nur gesetzt solange der Schalter an ist; additiv gespeichert, im Hash nur gemischt, wenn gesetzt). Ist ein Regal `idleShelfTicks` (20 000) lang unberührt, senkt das `IdleShelfSystem` (Intervall 100, Trade-Modul) sein Ziel um 1 (mindestens 1, einmal je unberührter Periode) und der `MarketSystem` multipliziert den Verkaufspreis an Spieler und NPC mit `idleShelfPriceFactor` (0,5) — bis wieder eine Bewegung stattfindet (dann gilt wieder der normale Marktpreis zum gesenkten Ziel). Regeln in `EconomyRules.IdleShelves.cs` (`idleShelfActive`: Code-Vorgabe aus, Standardwelt an; `idleShelfTicks`, `idleShelfPriceFactor`). Deterministisch, kein Zufall. Tests: `IdleShelfTests` (u. a. Rücknahme-Helfer `RevertIdleShelf` in der Referenz-Hash-Kette).
