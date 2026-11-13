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
