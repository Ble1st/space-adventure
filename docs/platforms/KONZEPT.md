# Kleinstationen: Waffenstellungen und Satelliten (Vorschlag, nicht umgesetzt)

Vier neue Arten kleiner Stationen. Nur Konzept und Modellentwürfe; Spielcode und Weltdatei sind unverändert.
Bilder: `vorschlaege_leicht.jpg`, `vorschlaege_mittel.jpg`, `vorschlaege_schwer.jpg`, `vorschlaege_satellit.jpg`,
Gesamtblatt im gemeinsamen Maßstab `vorschlaege_alle.jpg`; Einzelbilder `<art>_a|b|c.png`, Modelle `<art>_a|b|c.glb`
(Radius 1 in der Spielebene wie die Stationen), Geschützstellungen in `hardpoints.json`.
Erzeugt mit `tools/blender/platform_proposals.py`. Maßangaben in Spieleinheiten (E): Station Radius 15,
S-Schiff 4,8 lang, M 7,2, L 11,5, XL 17.

## Rollen

| Art | Zweck | Besitzer | Standort | Vorschlag Radius |
|---|---|---|---|---|
| Leichte Waffenstellung | Streifenschutz gegen Jäger und Kaperer (S), billiger Vorposten | Fraktionen; Piraten als Wache am Versteck; später Spieler | an Toren der Hauptrouten, an Asteroidenfeldern, Grenzsektoren zu herrenlosen Sektoren | 3 (≈ S-Schiff) |
| Mittlere Waffenstellung | Torsicherung und Stationsschutz gegen M-Schiffe, fängt Raketen ab | Fraktionen | an Grenztoren, um Werften/HQs, an umkämpften Sektoren | 5–6 (≈ M-Schiff) |
| Schwere Waffenstellung | Festung gegen Großkampfschiffe (Fregatte, Zerstörer) bei Krieg/Belagerung | Staaten/Gilden, nur im eigenen Gebiet | an Grenztoren zum Kriegsgegner, am Hauptquartier | 8–9 (≈ L-Schiff, gut halbe Station) |
| Satellit | unbewaffnete Funktionseinheit (Aufklärung, Relais, Vermessung) | Fraktionen; später Spieler | über das ganze Gebiet verteilt, auch in herrenlosen Sektoren | 2,5 (kleiner als ein S-Schiff) |

Alle sind kleine Stationen ohne Markt, ohne Dienste und ohne Andocken; sie stehen frei im Raum (Hindernis),
bleiben in der Hand ihres Besitzers und werden über Baustellen gebaut.

## Werte (Format `stationTypes[].defense`, aus vorhandenen Modulen abgeleitet)

Zum Vergleich: Polizeiwache hull 1800 / shield 300 / armor 60 / PV 0,25 / 2 Laser + Massetreiber; HQ 3000/400/80/0,3.

```json
"light_platform":  { "buildCost": 3000,  "upkeep": 0.3, "influence": 0,   "security": 0.4,
  "defense": { "hull": 450,  "shield": 80,  "shieldRegen": 1, "armor": 30, "pointDefense": 0,
               "guns": ["weapon_laser_s", "weapon_laser_s"] } },          // Variante: Laser + weapon_massdriver_s
"medium_platform": { "buildCost": 6500,  "upkeep": 0.7, "influence": 0.5, "security": 0.8,
  "defense": { "hull": 1000, "shield": 180, "shieldRegen": 2, "armor": 50, "pointDefense": 0.3,
               "guns": ["weapon_laser_m", "weapon_laser_s", "weapon_massdriver_s"] } },
"heavy_platform":  { "buildCost": 14000, "upkeep": 1.5, "influence": 1,   "security": 1.2,
  "defense": { "hull": 2400, "shield": 400, "shieldRegen": 4, "armor": 90, "pointDefense": 0.45,
               "guns": ["weapon_laser_m", "weapon_laser_m", "weapon_massdriver_s",
                        "missile_launcher_s", "missile_launcher_s"] } },
"satellite":       { "buildCost": 1200,  "upkeep": 0.1, "influence": 0,   "security": 0.3,
  "defense": { "hull": 150,  "shield": 0,   "shieldRegen": 0, "armor": 10, "pointDefense": 0, "guns": [] } }
```

Reichweiten aus den Modulen: Laser S 60, Laser M 70, Massetreiber 45, Rakete 110. Leicht ≈ 7 Schaden/s
(reicht gegen Kaperer 80 Hülle, nicht gegen Fregatten 520); mittel ≈ 12/s plus Punktverteidigung; schwer ≈ 20/s
mit Raketen auf 110 E — eine Fregatte im Nahkampf verliert gegen sie. Punktverteidigung = Wert `pointDefense`
(die PV-Türme im Modell sind nur die sichtbaren Stellen, keine `guns`). Bauen: Baumaterial nach der
bestehenden Regel (`BuildCost × 0,5` in Stahl/Hüllenplatten/Elektronik) – schwere Stellung ≈ 7000 Cr Material.
Grenzen je Sektor (Vorschlag): 4 leicht, 2 mittel, 1 schwer, 2 Satelliten.

## Was die bestehenden Regeln schon tragen

- `defense` mit `guns`: Stationsgeschütze feuern in jedem Gefecht des Sektors auf feindliche Teilnehmer,
  Raketen/Punktverteidigung, Belagerung, Fall (zerstört/außer Gefecht), `StationRepairSystem` (Phase 28).
- `security`: `ThreatMap.BaseSecurity` summiert `security` **jeder** Station des Sektorbesitzers → senkt
  Überfallrate, Routenrisiko und Jagdgewicht der Piraten ohne neuen Code.
- `influence`, `capturable`/`destructible`, Baustellen und Baumaterial, Hardpoint-Konvention `Turret_<n>` / `stations.json`.
- Sicherheitsradius: `ThreatMap` (Z. 31) zählt jede Station als Schutzzone (`StationSafeRadius` 35) –
  bei Waffenstellungen erwünscht (Tor wird sicher), bei Satelliten nicht.

## Was fehlen würde (Nahtstellen, keine großen Klassen aufblähen)

1. **Körperradius je Typ** (`stationTypes[].radius`): heute fest `Station.BodyRadius = 15` (Kollision,
   Platzierung, Belagerungsabstand, Raketenreichweite zum Rumpf).
2. **Einordnung**: neue `government`-Art (z. B. `outpost`) → offiziell: Unterhalt aus der Kasse, zählt nicht
   gegen `MaxStations`, kein Bankrott; aber keine Polizei-Heimatbasis, nicht `IsGovernment`.
3. **Nicht andockbar**: `"services": []` (sonst Vorgabe trade+hangar) plus Flag `dockable: false`, damit
   Andocken, Autopilot-Ziel, Taxi/Hangar-Listen und `StationAt` sie überspringen.
4. **Wer baut**: Lebenszyklus nie. Erste Stufe nur Weltdatei; später eine Fraktionsentscheidung (eigenes
   `ISimSystem` im Security-Modul: Bedrohung ≥ x oder Krieg, bezahlt aus `FactionViability.Spendable`).
5. **Feuereröffnung**: Stationsgeschütze greifen nur in laufende Gefechte ein. Für echte Torwachen eine
   kleine Regel: lauernde Kaperer in Geschützreichweite einer feindlichen Stellung werden abgefangen
   (wie `PirateSystem.Intercept` mit Polizei).
6. **Satelliten**: aus der Schutzzone ausnehmen; eigene Funktion je Art (unten).

## Satelliten – Funktionen

| Funktion | Wirkung | Mechanik |
|---|---|---|
| **Sensor/Überwachung** | erhöht Sicherheit im Sektor, zeigt lauernde Kaperer auf der Lagekarte | `security` (vorhanden) + Anzeige aus der Lagekarte; optional Abfangchance der Polizei ↑ |
| Kommunikations-/Navigationsrelais | Preise und Lage entfernter Sektoren sichtbar; Autopilot/Taxi schneller über Relaisketten | neue Sichtbarkeitsregel in `MarketInfo`/Lagekarte |
| Bergbau-/Vermessungssonde | zeigt Erzmischung/Restmenge der Gürtel, kleiner Bonus auf Schürfplanung | neue Info in `Belt`/`MinerSystem`-Bewertung |

**Empfehlung:** mit dem **Sensorsatelliten** anfangen – er wirkt sofort über `security` ohne neue Regel und
passt zur Lagekarte; Relais und Sonde brauchen neue Mechanik.

## Die Entwürfe

| Art | A | B | C |
|---|---|---|---|
| Leicht | **Mastturm** – Gittermast durch die Ebene, Laserturm oben und unten, Kühlflügel (1040 △) | **Panzerscheibe** – flache Sechseck-Scheibe, Laser oben, Massetreiber unten, Sensorknoten (800 △) | **Felsanker** – Gesteinsbrocken mit Panzerkragen und zwei Kasematten (780 △) |
| Mittel | **Dreiarm-Gitter** – Nabe, drei Gitterarme mit je einem Turm, PV an der Nabe (1592 △) | **Bastionstrommel** – geschlossene Achteck-Trommel, drei Randbastionen, PV-Kuppeln (1450 △) | **Felsfestung** – Asteroid mit Kasematte und zwei Flankenbastionen (1144 △) |
| Schwer | **Gitterbatterie** – Panzerblock mit Raketenschächten, schwere Zwillingstürme an den Trägerenden (1648 △) | **Zitadelle** – zweistufige Panzerscheibe, zwei schwere Türme, zwei Schachtfelder, 4 PV (1790 △) | **Asteroidenbastion** – großer Asteroid, Ringbastion, Raketen-Kasematte, Ausleger (1670 △) |
| Satellit | **Relais-Schüssel** – Bus mit gekippter Schüssel und Solarflügeln (504 △) | **Sensorkranz** – Ring aus sechs Sensorköpfen um einen Kern (860 △) | **Vermessungssonde** – Ausleger mit Paneelfeldern, Messkopf nach unten (456 △) |

Familie: gleiche Turmköpfe (leicht/mittel/schwer wachsen sichtbar), Accent-Felder in Fraktionsfarbe,
dunkle Technikgräben, blaue/rote Baken; die A-Reihe ist Gitterbau, B geschlossene Panzerform, C Asteroid.
Alle ≤ 8 Materialien, Dreiecksgrenzen eingehalten (leicht ≤ 1200, mittel ≤ 2000, schwer ≤ 3000, Satellit ≤ 1000).

**Empfehlung:** Leicht **B**, Mittel **B**, Schwer **B** (geschlossene Panzerformen: aus der Draufsicht am
klarsten als „Waffenstellung“ lesbar, Türme stehen frei auf der Oberseite, bilden eine sichtbare Steigerung);
Satellit **B** (Sensorkranz – passt zur empfohlenen Sensorfunktion und hebt sich klar von den Stationen ab).
Die C-Reihe (Asteroid) eignet sich als Piratenvariante.
