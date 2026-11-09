# Stationen

Abschnitt 1 beschreibt den Ist-Zustand (umgesetzt). Die Abschnitte 2 bis 4 sind **Planung, nichts davon ist umgesetzt**.

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
| Chemiefabrik | Wasser, Eis, Silizium → Kunststoff, Treibstoff. Neues Zwischenprodukt. |
| Pharmalabor | Wasser, Chemikalien, Platin → Medikamente. Zweite Endnachfrage neben der Kolonie. |
| Waffenfabrik | Stahl, Elektronik, Titan → Waffensysteme (mit Kampf). |
| Schildgenerator-Werk | Platin, Elektronik, Energie → Schildmodule. |
| Triebwerkswerk | Titan, Brennstoffzellen, Platin → Antriebe. |

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
