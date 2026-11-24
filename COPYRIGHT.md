# Urheberrecht und fremde Bestandteile

Diese Datei listet, was im Spiel **nicht selbst erstellt** wurde, und woher die übrigen Inhalte stammen.

## Fremde Inhalte im Spiel

### Hintergründe (Himmelskugeln / Skyboxen)

Dateien: `godot/assets/skyboxes/*.jpg` (zehn Himmel: vier mit Planet, sechs ohne).

- **Quelle:** Space Spheremaps, https://www.spacespheremaps.com/
  - mit Planet: https://www.spacespheremaps.com/planetary-spheremaps/ —
    `desert_planet`, `earthlike_planet`, `earthlike_planet_close`, `ice_planet`
  - ohne Planet: https://www.spacespheremaps.com/local-star-spheremaps/ —
    `blue_local_star_and_nebulae`, `blue_local_star_no_nebulae`, `red_local_star_and_nebulae`,
    `red_local_star_no_nebulae`, `white_local_star_and_nebulae`, `white_local_star_no_nebulae`
- **Lizenz:** Creative Commons Namensnennung 4.0 International (CC BY 4.0),
  https://creativecommons.org/licenses/by/4.0/deed.de
  Der Urheber verzichtet ausdrücklich auf die Namensnennung; wir nennen die Quelle trotzdem.
- **Zusatzbedingung des Urhebers:** Die Bilder dürfen nicht zum Training von KI-Bildmodellen verwendet werden.
- **Bearbeitung:** auf 4096 × 2048 verkleinert, Tonwerte, Sättigung und Schwarzpunkt einheitlich angepasst;
  bei den vier Planetenhimmeln zusätzlich der Planet abgedunkelt. Einzelheiten:
  `godot/assets/skyboxes/LICENSE.txt` und CLAUDE.md, Abschnitt „Skyboxes“.
  Die Originalbilder liegen nicht im Repository.

### Engine und Laufzeit (im fertigen Spiel enthalten)

| Bestandteil | Lizenz | Hinweis |
|---|---|---|
| Godot Engine 4.7.2 (.NET-Ausgabe) | MIT | https://godotengine.org/license — Godot selbst enthält weitere Drittbibliotheken, siehe deren `COPYRIGHT.txt` |
| Schrift „Open Sans“ (Standardschrift des Godot-Themes) | SIL Open Font License 1.1 | wird von Godot mitgeliefert, keine eigene Schriftdatei im Projekt |
| .NET-Laufzeit (Microsoft) | MIT | wird beim Android-Export ins APK gepackt |

### Nur zum Entwickeln (nicht im Spiel enthalten)

| Werkzeug / Paket | Lizenz | Wofür |
|---|---|---|
| xUnit, xunit.runner.visualstudio | Apache 2.0 | Unit-Tests |
| Microsoft.NET.Test.Sdk | MIT | Unit-Tests |
| coverlet.collector | MIT | Testabdeckung |
| Blender | GPL (die erzeugten Modelle sind frei vom GPL) | Skripte in `tools/blender/` erzeugen Schiffs-, Stations- und Tormodelle |
| ImageMagick | ImageMagick-Lizenz | Bearbeitung der Himmelsbilder |
| Python (Standardbibliothek) | PSF-Lizenz | Musik- und Hilfsskripte |

## Selbst erstellte Inhalte

Alles Folgende ist Eigenarbeit dieses Projekts (Code, Daten und erzeugte Dateien):

- **Programmcode:** `src/`, `godot/scripts/`, `tests/`, `tools/`.
- **Spielwelt und Daten:** `src/SpaceSim.Core/Content/` (Weltdatei, Namensliste der Fraktionen).
- **3D-Modelle** (`godot/assets/ships`, `stations`, `gates`): prozedural aus den Blender-Skripten in `tools/blender/` erzeugt.
- **Oberflächentexturen** (`godot/assets/textures`): prozedural aus `tools/blender/surface_textures.py` (fester Seed).
- **Musik** (`godot/assets/music`): Eigenkompositionen, prozedural synthetisiert mit `tools/audio/music_game.py`
  (nur Python-Standardbibliothek, keine fremden Samples).
- **Soundeffekte:** zur Laufzeit prozedural erzeugt (`godot/scripts/World/Sfx.cs`), keine Audiodateien.
- **Sterne, Nebel, Partikel:** zur Laufzeit aus eigenem Code erzeugt.

Neue fremde Dateien bitte hier eintragen (Quelle, Lizenz, Bearbeitung), bevor sie ins Spiel kommen.
