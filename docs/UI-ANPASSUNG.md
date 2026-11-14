# UI-Anpassung an Bildschirmgröße und Layout — Analyse und Plan

Stand: nach Runde 29 (Repo-Stand ohne Änderungen; nur gelesen). Nutzerwunsch: „UI soll sich auch automatisch Bildschirmgröße und Layout anpassen.“

## 0. Kurzfassung

- Heute: ein festes 1280 × 720-Raster (`window/stretch/mode="canvas_items"`, `aspect="expand"`). Nur das Flug-HUD verschiebt seine Elemente **waagerecht** (links/mitte/rechts, Safe Area links/rechts). Alle Vollbild-Bildschirme sind 1280 × 720 groß und liegen mittig vor einem dunklen Rand. **Senkrecht passt sich nichts an**: auf 16:10 und 4:3 (mehr Höhe) bleibt alles im oberen 720er-Streifen, der Rest bleibt leer — auch die Flug-Bedienung unten (Schieberegler, Wenden, „RÜCK“) sitzt dann in der Bildmitte statt am unteren Rand.
- Tippflächen: 56 px im Raster sind auf dem Testgerät (Samsung A15, 6,5″, 2340 × 1080, ≈ 396 ppi, Skalierung 1080 ÷ 720 = 1,5) **≈ 5,4 mm** — unter der üblichen Empfehlung von ≈ 7–9 mm (Android 48 dp ≈ 7,6 mm). Auf kleineren Handys noch weniger.
- Vorschlag: ein zentrales Layout-Modul (reine Funktionen im Core, `Flight/`-artig: `SpaceSim.Core/Ui/Layout`), das aus Fenstergröße, Safe Area, DPI und einer Nutzer-Einstellung „UI-Größe“ den **UI-Skalierungsfaktor** (Godot `content_scale_factor`) und eine **Geräteklasse** (kompakt / normal / breit / hoch) liefert. Darauf: Flug-HUD in Zonen mit Kollisionsregeln (auch senkrecht), Vollbild-Bildschirme füllen die nutzbare Fläche (Rail links, Inhalt dehnt sich in Breite und Höhe, gleiche Anordnung auf allen Geräten). Kein Umstieg auf Godot-Container überall — nur dort, wo Listen/Spalten wachsen sollen.
- 6 Etappen, je 1–3 Runden, zusammen **≈ 9–12 Runden**. Reihenfolge nach Nutzen: Prüfwerkzeug → Skalierung/Tippfläche/UI-Größe → Flug-HUD senkrecht → Handel/Dock → Kommandozentrale → Rest.
- **Entschieden (Nutzer):** Querformat bleibt fest (kein Hochformat); UI-Größe als Schalter klein/normal/groß im System-Bereich und im Menü, gespeichert in `user://settings.cfg` wie die Tonwerte (nicht im Spielstand); Tablets wie Handys — gleicher Inhalt, gleiche Anordnung, auf die Fläche skaliert, zusätzliche Höhe bei 16:10/4:3 sinnvoll füllen, keine eigenen Tablet-Layouts. Offen bleibt nur die Mindestauflösung (Empfehlung **960 × 540 logisch nach Skalierung**).
- **Reihenfolge (Koordinator):** nach dem Commit von Runde 29 zuerst der Rückkaufdeckel (Variante d′ aus Runde 29), danach Etappe E0 dieses Plans.

## 1. Bestandsaufnahme (am Code geprüft)

Grundlagen:
- `godot/project.godot`: `window/size/viewport_width=1280`, `viewport_height=720`, `stretch/mode="canvas_items"`, `stretch/aspect="expand"`, `handheld/orientation=0` (Querformat). Kein `content_scale_factor`, keine Laufzeit-Skalierung.
- Bausteine: `Ui/Kit/UiKit.At(parent, control, x, y, w, h)` setzt feste Positionen; `KitButton` mit fester Größe (`new Vector2(w, h)`), `KitScroll`, `KitWindow`, `KitBar`, `KitSlider`, `LabelLayout` (kollisionsfreie Kartenbeschriftung), `UiKit.Wrap/WrappedText` (Text mit Schrift gemessen).
- Zählung in `godot/scripts/Ui` (grep): **361 `At(`-Aufrufe** mit festen Koordinaten, **241 feste `new Vector2(<Zahl>…)`**, dazu feste Rechtecke im HUD (`BayPos`, Alert-Positionen, `ScreenAlertX` usw.).

| Teil | Datei(en) | Heute | Anpassung | Aufwand |
|---|---|---|---|---|
| Flug-HUD | `Ui/Screens/FlightHud.cs` (772 Z., 33 `At`, 28 Vektoren) | waagerecht verankert (`Relayout`: Mitte < 400 links, > 820 rechts, sonst mittig; Safe Area links/rechts); Höhe fest 720; Schächte `BayPos` fest; `OccupiedRects` für Kamera/Labels | **teilweise** — senkrecht starr, Safe Area oben/unten fehlt | M (1–2 Runden) |
| Meldungen/Toasts | `Ui/Hud.cs` (Alert-Layer, Toasts, `_center` −640…+640) | Positionen aus `vp.X` und festen y (z. B. `FlightAlertY`, `ScreenAlertX` 420) | teilweise (x), starr (y) | S |
| Kommandozentrale + Bereiche | `CommandCenter.cs` (Rail 14/84/200×540, Inhalt 1022 × 520, „← Flug“ bei y 648), `UniverseScreen`, `SectorScreen`, `FleetScreen` (Liste 368 breit, Detail 618 × 506), `AssetsScreen` (40 `At`), `FactionsScreen`, `MyFactionScreen` (Karten 500/486 breit), `CommsScreen`, `SystemScreen` (Startpaket-Auswahl) | alles 1280 × 720, mittig | **starr** | L (3 Runden) |
| Dock-Menü | `DockMenu.cs` | fest, mittig | starr | S |
| Dock-Bereiche | `DockAreaScreen.cs` (Rail, Inhalt `cw − 32` × 520, Schiffsspalte rechts, „Abdocken“ bei 1066/648), Bereiche `TradeScreen` (806 Z.; Editor 134/200 hoch, Zeilen 56), `HangarScreen`, `OutfitterScreen`, `ShipyardScreen`, `RepairFuelScreen`, `StationInfoScreen`, `AuthorityScreen`, `ResearchScreen`, `StorageScreen`, `TaxiScreen` | fest; Listen in `KitScroll` (scrollen schon) | **starr**, aber Listen wachsen leicht mit | M–L (2 Runden) |
| Sternenkarte | `Ui/Panels.cs` `MapOverlay` | passt sich dem Fenster an (seit Phase 21), Beschriftung über `LabelLayout` | **schon anpassungsfähig** | – |
| Wirtschaftspanel | `Ui/Panels.cs` | oben rechts verankert | gut | – |
| KitWindow | `Ui/Kit/KitWindow.cs` (31 Z.) | Größe vom Aufrufer | neutral | – |
| Startpaket-Auswahl | `SystemScreen.BuildStartChooser` | Karten nach Textmaß, scrollt | teilweise | S |
| Hamburger-Menü | `CommandMenu.cs` (15 Vektoren) | fest unten links | starr | S |
| 3D-Beschriftungen/Kamera | `Main` (`FramingField`, `FreeField`, Label-Ausblendung) | arbeiten schon mit echten HUD-Rechtecken und Fenstergröße | **schon anpassungsfähig** | – |

Was schon gut ist: Text wird mit der Schrift gemessen (`Wrap`, `KitButton.Fit`, `Pick`-Varianten im Handel), Listen scrollen (`KitScroll` mit stabilen Schlüsseln), die Kamera rechnet mit den tatsächlichen HUD-Flächen, Selftest und `--ui-shots`/`--phone-shots` können Fenstergrößen setzen (Stufen 93/95 in 2340 × 1080).

## 2. Zielbild je Geräteklasse

Logische Auflösung nach heutigem Stretch = Fenster ÷ (Fensterhöhe ÷ 720) in der Höhe; Breite wächst mit.

| Geräteklasse | Beispiel | logisch heute | Ziel |
|---|---|---|---|
| Schmales Handy 19,5–21:9 | 2340 × 1080, 2400 × 1080 | 1560 × 720 | gleiche Anordnung; Vollbild-Bildschirme füllen die Breite (Inhaltsbereich breiter), Tippflächen ≥ 7 mm |
| 16:9 | 1920 × 1080, 1280 × 720 | 1280 × 720 | Basis (unverändert) |
| Tablet 16:10 | 1920 × 1200, 2560 × 1600 | 1280 × 800 | gleiche Anordnung wie am Handy; Höhe füllen: Flug-Bedienung am unteren Rand, Listen länger, Karten höher |
| Tablet 4:3 | 2048 × 1536 | 1280 × 960 | wie 16:10 (deutlich mehr Höhe, dieselbe Anordnung) |
| Kleines Display | 5″-Handy 1920 × 1080 (≈ 440 ppi), 960 × 540-Fenster | 1280 × 720 | UI-Größe „groß“ automatisch, wenn eine 56-px-Taste < 7 mm wird |
| Große Displays/Desktop | frei skalierbares Fenster, 2560 × 1440 | 1280 × 720 | Inhalt nicht endlos strecken: Vollbild-Bildschirme bis zu einer Höchstbreite (≈ 1600 logisch), dann mittig |
| Notch/Safe Area | alle Seiten (Querformat links/rechts, bei manchen Geräten auch unten Gestenleiste) | heute nur links/rechts im HUD | alle Ränder im HUD und in Vollbild-Bildschirmen |

**Mindest-Tippfläche:** 56 logische px ergeben physisch `56 × Skalierung ÷ ppi`. A15: 56 × 1,5 ÷ 396 ppi ≈ 0,212 in ≈ **5,4 mm**; 5″-Handy mit 1080 px Höhe (≈ 440 ppi): ≈ 4,8 mm; 10″-Tablet 1920 × 1200 (≈ 226 ppi, Skalierung 1,67): ≈ 10,5 mm. Ziel: **≥ 7 mm** auf allen Geräten (Android-Empfehlung 48 dp ≈ 7,6 mm) — über den UI-Skalierungsfaktor, nicht über größere Rasterwerte.

**Hochformat — entschieden: nein.** Das Spiel bleibt auf Querformat festgelegt (`orientation=0`, unverändert). (Unterstützen hätte ≈ 4–6 Runden zusätzlich gekostet.)

**Tablets — entschieden: wie Handys.** Gleicher Inhalt und gleiche Anordnung, auf die Fläche skaliert; die zusätzliche Höhe bei 16:10/4:3 wird gefüllt (Bedienung an den unteren Rand, Listen und Karten länger/höher, Fußleisten unten), keine zweite Spalte und keine eigenen Tablet-Layouts. Dasselbe gilt für breite Handys: die Breite wird gefüllt (Inhalt breiter), die Anordnung bleibt.

## 3. Architekturvorschlag

Leitlinie wie im Projekt: Ansicht dünn, Regeln rein und testbar, Bausteine im Kit.

1. **Layout-Modul (rein, Core):** `SpaceSim.Core/Ui/Layout.cs` (ohne Godot, mit Unit-Tests, wie `Flight/FreeField`, `CombatFraming`):
   - `UsableArea(window, safeArea)` → nutzbare Fläche in logischen Einheiten mit Rändern an allen vier Seiten.
   - `Scale(windowPx, ppi, userSize)` → Faktor, sodass eine 56-px-Taste ≥ 7 mm wird (geklemmt, z. B. 0,85–1,6), multipliziert mit der **Nutzer-Stufe „klein/normal/groß“ (entschieden: Schalter im System-Bereich und im Hamburger-Menü, gespeichert in `user://settings.cfg` wie die Tonwerte, nicht im Spielstand)**, z. B. 0,9 / 1,0 / 1,15.
   - `Class(logicalSize)` → nur noch `Compact` (Höhe < 640 oder Breite < 1100: Abstände enger) und `Normal` — keine eigenen Breit-/Tablet-Anordnungen (entschieden).
   - `Columns(width, specs)` → Spaltenbreiten aus Mindest-/Wunschbreiten (z. B. Rail 200 fest, Inhalt füllt, Schiffsspalte im Dock fest 300) — dieselben Spalten auf allen Geräten, nur die füllende Spalte wächst.
2. **Godot-Seite:** `Ui/Kit/Layout.cs` liest Fenster, Safe Area (`DisplayServer.GetDisplaySafeArea`), DPI (`DisplayServer.ScreenGetDpi`), setzt `GetTree().Root.ContentScaleFactor` und meldet Änderungen (`NOTIFICATION_WM_SIZE_CHANGED` / `SizeChanged`) an alle Bildschirme. Bildschirme bauen bei Größen-/Klassenwechsel neu (ihr Zustand liegt schon im Bildschirm — Runde 28 —, nicht in den Knoten; `ISessionScreen`/`KitScroll`-Schlüssel bleiben).
3. **Kit-Helfer statt fester x/y:** `UiKit.Row/Column(...)` mit Mindest-/Wunschbreiten aus `Layout.Columns`, `At` bleibt für Inhalte innerhalb einer Karte (die Karten selbst bekommen ihre Größe vom Helfer). Kein großflächiger Umstieg auf Godot-`Container` — das Projekt hat sich bewusst für freie Platzierung entschieden (Container strecken Kinder, Autowrap in freien Controls war unzuverlässig); Container nur für Listen (`VBoxContainer` in `KitScroll`, gibt es schon) und Leisten.
4. **Flug-HUD als Zonen:** oben links (Status), oben Mitte (Sektor/Lage), oben rechts (Kommando/Karte/Zeit, Zielkarte), rechte Spalte (Schächte, Kampftasten), unten links (Wenden, RÜCK, Menü), unten rechts (Regler). Jede Zone wird an ihrer Ecke/Kante der nutzbaren Fläche verankert (auch **unten**: Bedienung bleibt am unteren Rand auf 16:10/4:3). Kollisionsregeln: Zonen kennen ihre Rechtecke, `Layout` prüft Überschneidung, Schächte weichen in eine zweite Spalte (heute schon `BayPos`), Meldungen/Toasts nehmen den freien Platz (heute: feste y). `OccupiedRects` speist wie heute Kamera und Labels.
5. **Vollbild-Bildschirme:** `CommandCenter`/`DockAreaScreen` bekommen die nutzbare Fläche statt 1280 × 720: Rail links fest (200), Inhalt füllt Breite und Höhe, Fußleiste („← Flug“, „Abdocken“) am unteren Rand, gleiche Anordnung auf allen Geräten (entschieden). Höchstbreite ≈ 1600 logisch (Desktop-Fenster), darüber mittig — nur dort bleibt der dunkle Rand (`_backdrop`).
6. **Godot-Mittel:** `stretch/mode=canvas_items` + `aspect=expand` beibehalten (Raster bleibt Basis, Bilder bleiben scharf); zusätzlich `content_scale_factor` zur Laufzeit für die Tippflächen-Skalierung. Nicht: `aspect=keep` (Balken), nicht `viewport`-Modus (unscharf), keine Themes pro Gerät.

## 4. Migrationspfad (Etappen, je einzeln prüfbar)

Jede Etappe mit Selftest in den Zielauflösungen **1280 × 720, 1560 × 720 (≙ 2340 × 1080), 2340 × 1080, 1920 × 1200 (16:10), 2048 × 1536 (4:3), 960 × 540** — über `DisplayServer.WindowSetSize` wie in Stufe 93/95. (Kein Hochformat — entschieden.)

Vorher (Koordinator): nach dem Commit von Runde 29 zuerst der Rückkaufdeckel (Variante d′), dann E0.

| Etappe | Inhalt | sichtbar besser | Aufwand |
|---|---|---|---|
| E0 Prüfwerkzeug | `--ui-check` (Selftest-Modus): öffnet jeden Bildschirm/Bereich in jeder Zielauflösung, prüft automatisch Überlappung von Tasten/Labels (Rechtecke), Überlauf aus der Fläche/Karte, gekürzte Texte (`…`), Tippflächen < Mindestmaß, Safe-Area-Verletzung; schreibt Bilder (`--ui-shots` erweitert) und eine Tabelle | Regressionen fallen auf; Ausgangszahlen | 1 Runde |
| E1 Layout-Modul + Skalierung | `Core/Ui/Layout` mit Tests; `content_scale_factor` aus DPI (≥ 7 mm) + Schalter „UI-Größe klein/normal/groß“ im System-Bereich und im Hamburger-Menü (gespeichert in `user://settings.cfg` wie Ton) | Tasten am Handy größer, auf Tablets nicht übergroß | 1–2 Runden |
| E2 Flug-HUD senkrecht + Safe Area rundum | Zonen-Verankerung oben/unten, Meldungen/Toasts in freien Flächen, Kamera-Freifeld folgt | 16:10/4:3: Bedienung unten, kein toter Streifen; Notch/Gestenleiste unten | 1–2 Runden |
| E3 Dock + Handel | `DockAreaScreen` auf nutzbare Fläche: Inhalt breiter/höher, Schiffsspalte rechts wie heute, Fußleiste unten; Handel: mehr Zeilen sichtbar, Editor nutzt die Breite | Handel auf Tablets/breiten Handys übersichtlicher, keine leeren Ränder | 2 Runden |
| E4 Kommandozentrale | Rail + Inhalt + Detail nach `Layout.Columns`; Universum/Sektor/Flotte/Besitz/Fraktion/Meine Fraktion/Komm/System | Karten und Listen nutzen die Fläche | 2–3 Runden |
| E5 Rest | Dock-Menü, Hamburger-Menü, Startpaket-Auswahl, Stationsinfo, Taxi | einheitliches Verhalten | 1 Runde |

Reihenfolge nach Nutzen: (Rückkaufdeckel) → E0 → E1 → E2 → E3 → E4 → E5. Nach jeder Etappe Gerätetest (Handy) und ein Tablet-Bild aus dem Prüfwerkzeug.

## 5. Risiken und offene Fragen an den Nutzer

Risiken: viele feste Koordinaten (≈ 360 Platzierungen) — Umbau schrittweise, sonst Regressionen; Selftests prüfen heute Positionen im 1280er-Raster (müssen auf relative Prüfungen umgestellt werden); `content_scale_factor` ändert Pixelmaße für Kamera-Freifeld und Picking (Umrechnung zentral halten); Godot/Android-Safe-Area-Werte sind geräteabhängig (nur am Gerät prüfbar).

Entschieden (Nutzer):
1. **Hochformat:** nein — Querformat bleibt fest.
2. **UI-Größe:** ja — Schalter klein/normal/groß im System-Bereich und im Hamburger-Menü, gespeichert in `user://settings.cfg` wie die Tonwerte, nicht im Spielstand.
3. **Tablets:** wie Handys — gleicher Inhalt und gleiche Anordnung, auf die Fläche skaliert, zusätzliche Höhe sinnvoll füllen, keine eigenen Tablet-Layouts.

Noch offen:
4. **Mindestauflösung?** Empfehlung: **960 × 540 logisch nach Skalierung** (darunter Hinweis „Fenster zu klein“, Abstände in der Klasse `Compact` enger).

## 6. Aufwand und Abgrenzung

Summe E0–E5: **≈ 9–12 Runden** (ohne eigene Tablet-Anordnungen etwas weniger als zuerst geschätzt; Hochformat entfällt).

Nicht Teil des Umbaus: neue Inhalte/Funktionen der Bildschirme, Grafik-Neugestaltung (Farben, Schriften, Symbole), 3D-Szene/Kamera-Logik (außer der Umrechnung für `content_scale_factor`), Barrierefreiheit über die Tippflächengröße hinaus (Kontrast, Screenreader), Lokalisierung, Gamepad/Tastatur-Navigation.
