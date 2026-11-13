# Stationsmodelle (Blender, prozedural)

Ein Low-Poly-Modell je Stationstyp aus `src/SpaceSim.Core/Content/default-world.json`, erzeugt
ausschließlich mit eigenen Python-Skripten im lokal installierten Blender (kein Addon, kein Netz).

| Datei | Inhalt |
|---|---|
| `stationkit.py` | Materialpalette (u. a. `Accent` für die Fraktionsfarbe), `Builder` mit Primitiven (Rotationskörper, Prismen, Kästen, Streben, Gitterträger, Geschütztürme, Schüsseln, Fels, Druckmodule, Kernspindel, Andockstutzen, Radiator-/Solarflügel, Manövrierdüsen, Greifer), Abschluss: zentrieren, auf Radius 1 in der Spielebene normieren, scharfe Kanten ab 33° |
| `station_models.py` | eine Bau-Funktion je Typ-Key (`MODELS`), Kurzbeschreibung der Modellidee im Dekorator |
| `build_stations.py` | baut alle Typen, exportiert GLB, rendert Vorschaubilder (Eevee) und den Kontaktbogen |
| `verify_stations.py` | Re-Import aller GLBs: Dreiecke, Materialien, Radius, Höhe, Größe |

## Aufruf (im Repo-Wurzelverzeichnis)

```
blender --background --factory-startup --python tools/blender/build_stations.py
blender --background --factory-startup --python tools/blender/build_stations.py -- --only farm,colony
blender --background --factory-startup --python tools/blender/build_stations.py -- --no-render
blender --background --factory-startup --python tools/blender/build_stations.py -- --overview-only
blender --background --factory-startup --python tools/blender/verify_stations.py
```

Ausgaben: `godot/assets/stations/<typeKey>.glb`, `docs/stations/<typeKey>.png` (1024×768),
`docs/stations/_overview.png` und `docs/stations/overview.jpg` (JPEG q88, die eingecheckte Übersicht;
die PNGs sind per .gitignore ausgenommen). Ein neuer Stationstyp ohne Eintrag in `MODELS` bricht den Lauf ab.

## Konventionen

- Ein Mesh je Datei, Ursprung in der Mitte, glTF Y-up; größte Ausdehnung in der Spielebene (Godot XZ)
  = 1, also im Spiel einfach mit `Obstacle.Radius` skalieren. Aufbauten ober- und unterhalb der Ebene.
- ≤ 3000 Dreiecke, keine Texturen, gemeinsame Materialnamen (`Hull`, `HullDark`, `Accent`, `Window`,
  `Light`, `LightRed`, `Glass`, `Panel`, `Glow`, `Plasma`, `Rock`, `Gold`, `Copper`, `Hazard`, `Plant`,
  `Ice`, `Crystal`, `Container`); Leuchtmaterialien über Emission.
- `Accent` ist fast weiß, damit eine Fraktionsfarbe per Multiplikation darüber gelegt werden kann.
- Vorschau-Rendering: Eevee, Farbraum „Khronos PBR Neutral“, drei Sonnenlichter, Sternenhimmel.

## Gestaltungsregel

Raumstationen, keine Bodenbauten: kein Fundament und kein klares „Unten“, Aufbau um eine Nabe,
Achse oder einen Gitterträger in alle Richtungen (auch unter die Ebene), Module an Knoten und
Auslegern. Die Silhouette muss aus schräger Draufsicht (15–85°) lesbar sein.

# Schiffsmodelle

| Datei | Inhalt |
|---|---|
| `ship_models.py` | eine Bau-Funktion je Schiffsmodell (`SHIPS`, Key = `shipModels[].key`, ohne `unit_player`) plus `corvette` und `missile_boat`; Größenklasse und Modellidee im Dekorator |
| `build_ships.py` | baut alle Schiffe, exportiert GLB, schreibt `ships.json`, rendert Vorschaubilder und die Übersicht (nutzt Szene/Kamera/Export aus `build_stations.py`) |
| `verify_ships.py` | Re-Import: ein Mesh, `Accent` und `Engine` vorhanden, Halblänge 1, zentriert, Triebwerke hinten, Dreiecksgrenzen |

Die Schiffs-Bausteine (`hull`, `nozzle`, `canopy`, `gun`) und das Material `Engine` liegen in `stationkit.py`.

```
blender --background --factory-startup --python tools/blender/build_ships.py
blender --background --factory-startup --python tools/blender/build_ships.py -- --only falcon,bastion
blender --background --factory-startup --python tools/blender/build_ships.py -- --overview-only
blender --background --factory-startup --python tools/blender/verify_ships.py
```

Ausgaben: `godot/assets/ships/<key>.glb`, `godot/assets/ships/ships.json`, `docs/ships/<key>.png`,
`docs/ships/_overview.png`, `docs/ships/overview.jpg` (JPEG q88).

## Achsen und Maßstab

- **Bauen in Blender:** Bug = +Y, oben = +Z, Steuerbord = +X.
- **Nach glTF-Export (Y-up) in Godot:** Bug = **−Z** (Godot-Vorwärts, `-Basis.Z`), oben = +Y, Steuerbord = +X.
  Ein Schiff mit `LookAt(ziel)` zeigt also ohne Zusatzdrehung mit dem Bug zum Ziel.
- **Länge** entlang der Bug-Achse = 2,0 (Halblänge 1), Ursprung = Mitte der Bounding-Box; Breite und Höhe frei.
- **`ships.json`:** je Modell `name`, `size` (S/M/L) und `scale` = empfohlener relativer Maßstab
  (S 1,0 · M 1,5 · L 2,4). Die Übersicht rendert alle Schiffe in diesem Maßstab.
- **Materialien:** `Engine` = Schubflächen der Düsen (emissiv, für Einfärben/Pulsieren im Spiel),
  `Accent` = Fraktionsfarbe (Streifen, Finnen). Positionslichter backbord rot (`LightRed`), steuerbord blau (`Light`).
- **Dreiecksgrenzen:** S ≤ 1200, M ≤ 1800, L ≤ 2500.

## Abbauwerkzeuge (Rotor- und Backen-Nodes)

- Schiffe mit mechanischem Abbau (`ships.json`: `"mining": "contact"`) bestehen aus dem Rumpf-Node `Hull`
  und Kind-Nodes `Rotor_*` (drehende Werkzeuge: Bohrer, Schneidrad, Walze, Fraeskopf) bzw. `Jaw_*`
  (bewegliche Spaltbacken). Laser-Schiffe (`"mining": "laser"`) und alle anderen sind ein einziger `Hull`-Node.
- **Drehachse = lokale Z-Achse des Nodes in Godot** (in Blender beim Bauen: lokale Y-Achse; der glTF-Export
  macht daraus Z). Der Node-Ursprung liegt auf der Drehachse bzw. im Gelenk. `spin` in `ships.json` sagt,
  wie die Achse im Schiff liegt (laengs = Bugrichtung, quer = Steuerbord-Achse, Gelenk senkrecht).
- Das Werkzeug ist bei Kontaktabbau der vorderste Punkt (−Z in Godot). `contactRadius` = Radius der
  Werkzeugfront quer zur Flugrichtung relativ zur Halblaenge 1 (bei der Walze: halbe Walzenbreite).
- `verify_ships.py` prueft Node-Namen gegen `ships.json`, vordersten Punkt und Lage des Ursprungs.
- Verbreiterung: `ship_models.WIDEN` streckt je Schiff die Rumpf-Lofts in X; Anbauteile behalten ihre
  Form und wandern mit.

## Geschuetzstellungen (Hardpoints)

- **Schiffe:** je Waffen-Slot der Klasse und je Utility-Slot mit `point_defense_*` genau eine sichtbare
  Stellung, erfasst in `godot/assets/ships/ships.json` unter `hardpoints` (Slot-Reihenfolge):
  `slot` (Index in `shipClasses[].slots`), `kind` = `gun` (Slot mit `facing: bow`: starres Bugrohr) |
  `turret` (Slot ohne Ausrichtung: Drehturm) | `missile` (Raketenwerfer: Schachtfeld in der Huelle; nur
  `missile_boat` behaelt Werferkaesten) | `pd` (Punktverteidigung), `pos` (Muendung/Schachtoeffnung),
  `dir` (Grund-Schussrichtung), `module` (laut `fit`), `empty` (Slot ohne Modul: Lafette ohne Rohr),
  `node` bei Tuermen.
- **Stationen:** je Geschuetz aus `stationTypes[].defense.guns` eine Stellung in
  `godot/assets/stations/stations.json` (`gun` = Index, sonst gleiche Felder). Stationen ohne Geschuetze
  haben keine Tuerme.
- **Koordinaten:** Godot-Modellraum (Bug −Z, oben +Y; Schiffe Halblaenge 1, Stationen Radius 1), aus der
  erzeugten Geometrie berechnet.
- **Turm-Nodes `Turret_<slot>` / `Turret_<gun>`:** Kind von `Hull` bzw. vom Stationsmesh, Ursprung auf der
  Drehachse (Sockelmitte), **Drehachse = lokales +Y** (bei Bauchtuermen zeigt +Y nach unten, bei Flanken
  nach aussen), Rohr in Ruhe nach lokal −Z.
- `verify_ships.py`/`verify_stations.py` pruefen Anzahl gegen die Weltdatei, `pos` an der Geometrie
  (naechster Vertex) und die Turm-Nodes.
- **Stationsstil:** `station_models.restyle` setzt die hellen Schiffsmaterialien ein (ausser
  Piratenversteck), `dress_station` legt automatisch Technikgraeben, Greebles und grosse Accent-Felder
  auf die groessten Flaechen. Die Vorschaubilder zeigen `Accent` in einer Beispiel-Fraktionsfarbe.

## Symmetrie und Stationsausstattung (Durchlauf 7)

- `verify_ships.py` prueft die Triebwerke: Schubflaechen (`Engine`) werden zu Duesen gruppiert; jede Duese
  ausserhalb der Mittelachse braucht ein Spiegelbild mit gleichem Radius, gleicher Heck- und Hoehenposition.
  Asymmetrie ist nur bei Nutzlast/Werkzeug erlaubt (Nomade: Gondeln gleich lang, Kapazitaet ueber Dicke und
  Torzahl; Kesselflicker: nur ein mittiges Triebwerk).
- Stationen: `station_details` setzt an jeder Station Navigationsbaken an den aeussersten Punkten,
  Manoevrierduesen an den Diagonalen, eine Antennengruppe oben und eine Bake unten; `dress_station` legt
  Technikgraeben mit Greebles, Accent-Felder mit Leiste und Fensterband auf die groessten Flaechen; runde
  Stationen (Kolonie, Wasserwerk, Kraftwerk, Waferfabrik) bekommen gezielte Accent-Ringsegmente.
  Offene Gestelle, Erzbrocken und lose Container sind durch geschlossene Bauteile ersetzt (Ausnahmen:
  Baustelle, Piratenversteck). Grenzen: <= 5000 Dreiecke, <= 10 Materialien je Station.
