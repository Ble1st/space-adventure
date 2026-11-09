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
| Schiffe | Werft | Hüllenplatten + Elektronik + Brennstoffzelle → Bausatz | `shipdealer`, `upgrade` |
| Nachfrage und Geldquelle | Kolonie | verbraucht Nahrung, Konsum-, Luxusgüter, Lebenserhaltung; Einkommen 8 | Missionsbüro, Bar (später) |

Alle Typen bieten außerdem den Dienst `trade`.

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

### `Essential` aufgedröselt (entschieden)

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
