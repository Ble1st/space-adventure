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

## 7. Stand nach Runde 31 (E0 und E1)

- **E0 erledigt:** `godot-mono --path godot -- --ui-check <ordner>` (braucht ein Fenster; Auflösungen über der Bildschirmgröße werden mit gleichem Seitenverhältnis nachgebildet). Ergebnis Faktor 1 (IST): in keinem Zustand und keiner Auflösung Überlappungen oder Elemente außerhalb; Tippflächen am Handy fast alle unter 7 mm (56 px = 4,8 mm bei 720p, 5,4 mm am A15, 9,5/8,6 mm an den Tablets; kleinste 3,5–4,0 mm: Kartenebenen, Autopilot/Auto-Mining, Bereichsleiste, „Liste/Kacheln“); gekürzte Texte in Station-Info, Fraktions-/Universumskarten und langen Meldungen; Vollbilder füllen 95 % (16:9), 78–79 % (19,5:9), 85 % (16:10), 71 % (4:3). Bei Faktor 1,15/1,3 ragen in jedem Zustand Elemente aus der Fläche — erst nach E2–E5 sinnvoll.
- **E1 erledigt:** `SpaceSim.Core/Ui/UiLayout` + `godot/scripts/Ui/Kit/UiScale`; Schalter klein/normal/groß (System-Bereich „Bedienung“, Hamburger-Menü „UI-Größe“), gespeichert in `user://settings.cfg`. Heute ergibt jede Stufe Faktor 1 (Obergrenze für starre Bildschirme); Zielwerte nach E2–E5: normal 1,15, groß 1,3 (A15: 6,2 / 7,0 mm), Tablets 1. Die Obergrenze 1,3 hält die logische Fläche bei ≥ 985 × 554 (über dem Minimum 960 × 540).
- **Als Nächstes E2:** Flug-HUD an Rändern/Safe Area verankern statt im 1280×720-Raster (Anker aus `UiLayout.Usable`), dann die Obergrenze für das HUD freigeben und mit `--ui-check` bei 1,15/1,3 prüfen (Ziel: A0 im Flug-HUD).

## 8. Stand nach Runde 33 (E2: Flug-HUD in Zonen)

- [x] **E2 erledigt** (nur Flug-HUD; Vollbild-Bildschirme bleiben bis E3–E5 bei Faktor 1).
- **Zonen als reine Rechnung im Core:** `SpaceSim.Core/Ui/FlightHudLayout.cs` (`Compute(w, h, safe, bays)` → Rechteck je `HudItem`; `FindSpot` für die Meldung), Tests `FlightHudLayoutTests`. Zwei Anordnungen:
  - *klassisch* (nutzbare Fläche ≥ 1280 × 720): die bisherigen Lagen, waagerecht wie bisher und jetzt auch senkrecht verankert — untere Zone (Wenden, RÜCK, Menü, Schubregler, Beschuss-/Fluchtzeile) am unteren Rand, alles andere oben; Safe Area an allen vier Seiten. Bei 1280 × 720 ohne Ränder pixelgleich zu vorher (Test + Selbsttest).
  - *kompakt* (kleiner — UI-Faktor > 1 oder Safe-Area-Ränder): Ecken fest (Status oben links, Kommando/Karte/Zeit + Zielkarte oben rechts, Menü/Wenden/RÜCK unten links, Schubregler unten rechts, 320–600 breit), Sektorchip und Lagezeilen schrumpfen auf den Raum zwischen Status und Kommandotaste (zu langer Fraktionsname → Kürzel „[UNI]“), die Kampfspalte (Schächte, Alle Feuer, Dauerfeuer, Nächster Gegner, Ziel folgen, Angriff, Ergeben) fließt in 176 × 56-Zellen von unten rechts zeilenweise nach links/oben, ohne feste Flächen zu berühren; Zellen sind für alle Kampftasten reserviert (nichts springt, wenn „Ergeben“ erscheint). „WENDEN“ entfällt dort (Beschuss-/Fluchtzeile stehen an seiner Stelle), „n von m bereit“ steht in „Alle Feuer“. Grenze: auf 985 × 554 (16:9 bei 1,3) passen vier Schächte plus alle sechs Kampftasten (10 Zellen). **Berichtigt in Runde 33b:** mit fünf oder sechs Schächten verloren nicht die Schächte ihre Zelle, sondern die *letzten Kampftasten* (Reihenfolge Schächte, Alle Feuer, Dauerfeuer, Nächster Gegner, Ziel folgen, Angriff, Ergeben): „Ergeben“ lag auf „Station/Tor angreifen“, mit sechs Schächten zusätzlich „Ziel folgen“ — siehe Abschnitt 9. Ohne Absenken passen sechs Schächte ab 1113 × 696 (16:10 bei 1,15) bzw. 1200 × 554 (A15 groß).
- **Entscheidung Punkt 2 — Faktor je Schicht statt global.** `UiScale.Factor` gilt für die anpassungsfähigen Schichten (heute: Flug-HUD, im Flug auch Meldung/Hinweis): die Schicht setzt `Control.Scale = f` und ordnet sich in `Fläche ÷ f` neu an. Die Wurzel (`content_scale_factor`) bleibt 1. Begründung: (a) alles, was Bildschirm ↔ Welt rechnet (Antippen von Stationen/Toren/Schiffen, Zielring, Randpfeile, Kamera-Freifeld `Main.FramingField`, Label-Verdeckung, Feuerwinkel-Kegel), bleibt in Bildpunkten und liest die HUD-Flächen über `GetGlobalRect` (enthält die Skalierung) — keine Umrechnung im Main nötig; (b) die starren Vollbild-Bildschirme, das Hamburger-Menü und die Sternenkarte bleiben unverändert bei 1 und sehen korrekt aus, ohne Rück-Skalierung 1/f; (c) E3–E5 verbauen nichts: jeder umgebaute Bildschirm setzt denselben Faktor auf seine Schicht (z. B. `_center`), sobald er sich an die Fläche anpasst; ist alles umgebaut, kann E5 auf den globalen `content_scale_factor` wechseln (dann entfallen die Schicht-Skalierungen). Verworfen: global `content_scale_factor` + die starren Bildschirme mit 1/f zurückskalieren — hätte auch Meldungen über Vollbildern, Zielmarker, Karte und Menü mitvergrößert und jede Etappe müsste die Gegenskalierung wieder abbauen.
- **Obergrenze der Flug-Schicht:** die Mindestfläche 960 × 540 (`UiLayout.MaxFactor`) statt `FixedScreensCap` (= 1). Damit wirkt der Schalter im Flug: A15 klein 1,00 / normal 1,15 / groß 1,30; 720p-Handys ebenso; Tablets 1,00 (DPI-Ziel schon erfüllt).
- **DPI (Handytest A15, „Tasten ≈ 4,7 mm“ statt ≈ 5,4 mm):** `UiScale` nimmt `DisplayServer.ScreenGetDpi()`. Auf Android liefert Godot dafür `DisplayMetrics.densityDpi`, die *Dichteklasse* des Geräts (A15: 450), nicht die physische Pixeldichte (A15 ≈ 396 ppi; `xdpi/ydpi` sind über Godot nicht erreichbar). Belege: in der Godot-Android-Bibliothek 4.7.2 (`godot-lib.template_release.aar`, Klasse `GodotIO`, Methode `getScreenDPI`) kommt als DPI-Feld nur `densityDpi` vor; und 56 px × 1,5 ÷ 450 × 25,4 = 4,74 mm entspricht genau der Anzeige am Gerät. Folgen: die angezeigten mm sind am A15 um den Faktor 396/450 ≈ 0,88 zu klein (Text sagt jetzt „etwa“); der DPI-Faktor `UiLayout.DpiFactor` ist dort um 450/396 ≈ 1,14 zu groß (1,48 statt 1,30) — am A15 ohne Wirkung, weil die Stufen ohnehin bei 1,15 bzw. 1,3 gedeckelt sind; auf Geräten, deren Dichteklasse *unter* der echten Dichte liegt, würde „normal“/„groß“ etwas zu klein gewählt. Ein verlässlicherer Wert ist ohne eigenen Android-Code nicht ermittelbar.
- **Handytest-Befunde Runde 32b (im Hamburger-Menü):** (1) Zeile „UI-Größe …“ abgeschnitten — jetzt kurz „UI-Größe: normal“, Faktor und Tastengröße in der Unterzeile („1,15× · Taste etwa 6,2 mm“), im Selbsttest mit der Schrift gemessen. (2) Tipp ohne Wirkung — Ursache: die Menütaste meldet `ui_size` an `Main.OnCommandAction`; dort gab es keinen Fall dafür (der Fall stand nur in `OnMenu`, wie bei „Musik“ in Runde 13), also fiel die Aktion in den `default`-Zweig und tat nichts. Jetzt behandelt; das Menü bleibt offen, die Zeile zeigt die neue Stufe, der Hinweis liegt auf der Meldungsebene über dem Menü. Selbsttest: echter Tipp in Fensterkoordinaten.
- **Nachweis `--ui-check`** (Runde 33, alle Bildschirme, sechs Auflösungen × drei Faktoren; Ordner `space-adventure-scratch-archiv/e2/check5`; neu `--ui-check-flight` = nur Flug-Zustände, dann Bilder auch bei 1,15/1,3). Flug-Zustände (normal, Überfallwarnung, Gefecht, Auto-Mining, Stationsangriff, Hamburger-Menü, Meldung + Hinweis), Summe über alle sechs Auflösungen, A = außerhalb, Ü = Überlappung interaktiver Elemente, K = gekürzte Texte:

  | Faktor | vorher (IST Runde 31) A / Ü / K | nachher (Runde 33) A / Ü / K |
  |---|---|---|
  | 1,00 | 0 / 0 / 30 | 0 / 0 / 30 (Lagen bei 1280 × 720 unverändert) |
  | 1,15 | 320 / 0 / 30 | **0 / 0 / 30** |
  | 1,30 | 523 / 0 / 30 | **0 / 0 / 30** |

  Die K-Befunde sind dieselben wie vorher (lange Zielnamen „Korsar #22 ([RSC] …)“, lange Meldungstexte mit „…“). Tippflächen im Flug am A15 (2340 × 1080, 396 ppi angenommen): bei 1,30 sind nur noch 4–5 der 17–22 Tasten unter 7 mm (Autopilot/Auto-Mining, 42 px hoch), keine unter 5 mm; bei 1,15 alle ≥ 5 mm außer zwei. Die Vollbild-Bildschirme stehen bei jedem Faktor jetzt mit A0/Ü0 (sie bleiben bei 1,00; vorher ragten sie bei 1,15/1,3 heraus).
- **Selbsttest:** Stufe 108 prüft den Faktor auf der Flug-Schicht (Wurzel 1) und Antippen/Kamera bei 1,15/1,3; neue Stufe 109 (`SelfTest.Round33.cs`): klassische Lagen bei 1280 × 720, Menüzeile „UI-Größe“ (gemessen, echter Tipp, Stufe gewechselt, Zeile neu, Hinweis über dem Menü), Faktor 1,3/1,15 mit kompakten Zonen (alle sichtbaren Tasten in der Fläche, keine Überlappung, Meldung auf keiner Taste), Safe Area an allen vier Rändern.
- **Prüfläufe:** Selbsttest, `--ui-shots` und `--phone-shots` setzen `UiScale.PinnedFactor = 1` (headless ist das Fenster 64 × 64 groß; die DPI-Rechnung ergab dort „normal“ = 1,15 und ein kompaktes HUD) — ihre Bilder bleiben im 1280er-Raster; höhere Faktoren prüfen die Stufen 108/109 und `--ui-check` gezielt. Ergebnisse: `dotnet test` 1010 grün (26 neue), Selbsttest OK, Balance 20000 Ticks Hash D3567DA325B7FED9 (unverändert).
- **Offen / nicht geprüft:** nichts davon wurde auf dem Gerät geprüft (Safe-Area-Werte, echte Tippgefühl, Lesbarkeit der kompakten Anordnung). Bei 985 × 554 im Gefecht liegt die Kampfspalte teils in der Bildmitte (es gibt keinen anderen Platz); die große Meldung weicht dann notfalls auf Status/Sektorzeilen aus (sie meidet immer die Tasten). Zielmarker, Randpfeile und 3D-Beschriftungen wachsen nicht mit (Wurzel bei 1). Nächste Etappe: E3 (Dock + Handel) — `_center` bzw. die Dock-Bildschirme bekommen denselben Schicht-Faktor, sobald sie sich an die Fläche anpassen.

## 9. Runde 33b (Prüferbefunde zu E2, Handytest der E2-APK)

- **Nie zwei Tasten in einer Zelle.** Berichtigung zu Abschnitt 8: bei 985 × 554 (16:9 bei 1,3) und fünf Schächten (Bollwerk, Hornisse, Sturmvogel) lag „Ergeben“ genau auf „Station/Tor angreifen“, mit sechs zusätzlich „Ziel folgen“; mit pessimistischen Safe-Area-Rändern (60/20/60/30) traf es am A15 bei 1,3 schon vier Schächte. Lösung (reine Kernfunktion `FlightHudLayout.FitFactor`): der größte Faktor ≤ Stufenfaktor (Schritte 0,01, nie unter 1), bei dem Schächte und alle sechs Kampftasten eigene Zellen haben — gilt für jede Fläche, jeden Rand, jede Schachtzahl. 16:9 bei „groß“: vier Schächte 1,30, fünf 1,26, sechs 1,25. Der Test nimmt den Fall nicht mehr aus; er läuft über sechs Auflösungen × drei Faktoren × ohne/mit Rändern × 0–6 Schächte und prüft auch Beschriftungen/Warnzeilen gegen Tasten.
- **Leviathan (zehn Waffenschächte, das HUD hat sechs Tasten).** War schon vor E2 so: Schacht 7–10 feuerten nur über „Alle Feuer“/„Dauerfeuer“, ohne Anzeige. Jetzt fasst die sechste Taste den Rest zusammen: „+5 weitere“, Unterzeile „Schacht 6–10“, „n bereit · feuern“; ein Tipp feuert alle bereiten davon (`FlightHud.MoreBaysDown` → `Main.FireReadyBays(from)`). Keine Einzelsteuerung der Schächte 6–10.
- **Eigenes Schiff nicht verdeckt.** `FreeField.LargestNear` (Kern): ist die Bildmitte von HUD-Tasten belegt (Kompakt-HUD), das größte freie Feld um einen freien Rasterpunkt (vorher: ganzes Bild → das Schiff blieb unter „Ergeben“/„Dauerfeuer“). Ohne Gefechtsziel legt `CombatFraming.CenterShip` (Kern) den Blickpunkt so, dass das Schiff in der Mitte dieses Felds liegt; das Zurückziehen des Blickpunkts zur Bildmitte (`Main.ShipInFreeField`-Schleife) greift nur noch, wenn die Mitte frei ist. Meldung und Hinweis meiden im Kompaktmodus den Schiffsring (`FlightHud.ShipRectLocal`). Das Prüfwerkzeug meldet „S!“, wenn eine Taste, die Meldung oder der Hinweis die Schiffsmitte (±16 px) verdeckt.
- **Handytest r33 (Hamburger-Menü):** der Hinweis nach dem Umschalten der UI-Größe lief über dem offenen Menü unten aus dem Bild — über Vollbildern endet er jetzt 12 über dem Rand (Höhe gemessen), und er ist kurz („UI-Größe groß: Flug 1,30×, Dock 1,25× · Taste etwa 7,0 mm“).
- Selbsttest Stufe 109 erweitert: Kriegstest (Bollwerk, fünf Schächte) bei Stufe 1,3 → Schicht 1,26, alle Tasten in der Fläche ohne Überlappung, echte Tipps auf ◀ (gehalten), Schubregler (75 %) und „Dauerfeuer“ (schaltet), zehn Schächte → „+5 weitere“.

## 10. Runde 34 — Etappe E3: Dock-Menü und Dock-Bereiche

- [x] **E3 erledigt.** Dock-Menü und alle Dock-Bereiche (Handel, Stationsinfo, Hangar, Reparatur, Ausrüster, Werft, Lager, Behörde, Taxi, Forschung, Platzhalter) liegen in einer eigenen Schicht über die ganze Fläche (`Hud._dockLayer`, nicht mehr mittig in 1280 Breite) mit eigenem Faktor — dasselbe Prinzip „Faktor je Schicht“ wie E2.
- **Gerüst als reine Kernfunktion** `SpaceSim.Core/Ui/DockLayout.cs` (Tests `DockLayoutTests`): `Shell(w, h, safe, ship)` → Kopfleiste, Bereichsleiste, Mittelteil, Schiffsspalte, „← Menü“, „Abdocken“, Kasse, Lücke für die große Meldung; `MenuCard` für das Dock-Menü; `FitFactor` und `GridColumns`. *Klassisch* (≥ 1280 × 720): die bisherige Anordnung, gedehnt — der Mittelteil wächst in Breite und Höhe, die Schiffsspalte bleibt 342 breit, die Fußleiste sitzt am unteren Rand, höchstens 1600 breit (Desktop), Safe Area rundum; bei 1280 × 720 exakt die alten Lagen (Test + Selbsttest). *Kompakt* (UI-Faktor > 1): gleiche Anordnung, Kopfleiste schmaler, „Abdocken“ oben rechts, „← Menü“ unter der (170 breiten) Bereichsleiste, Schiffsspalte 260 — so bleibt dem Mittelteil die Höhe. Das Dock-Menü bleibt eine 580 breite Karte, mittig, höchstens so hoch wie die Fläche (die Kacheln rollen).
- **Faktor der Dock-Schicht** = größter Faktor ≤ Stufe, bei dem jeder Bereich seine Mindestfläche hat (mit Schiffsspalte Innenbreite ≥ 600, ohne ≥ 1008 = Entwurfsbreite, Höhe ≥ 400; Dock-Menü ≥ 480 hoch). Die 1008 kommen aus dem Prüfwerkzeug: schmaler kürzten Stationsinfo, Ausrüster, Werft und Hangar ihre Texte. Ergebnis: A15 normal 1,15 / groß 1,25 (Taste ≈ 6,7 mm), 16:9-Handys und Tablets 1,02 (bei 16:9 ist kaum Breite übrig; Tablets brauchen keinen Zuschlag). Ein Faktor für alle Dock-Bildschirme (nichts springt beim Bereichswechsel). Kommandozentrale und Hamburger-Menü bleiben bis E4/E5 bei 1.
- **Bereiche:** füllen die Innenfläche (`inner.Size`) statt fester 520 Höhe (Ausrüster, Werft, Taxi); Bereiche, deren Inhalt auf dem Handy höher ist als der Mittelteil (Hangar, Lager, Behörde …), rollen als Ganzes; die Schiffsspalte rollt bei langer Fracht; Kacheln im Handel: Spaltenzahl aus der Breite.
- **Handytest r33 (Handel):** (2) der Deckel-Hinweis brach am Zeilenende hart ab — jetzt die längste Fassung, die mit der Schrift gemessen passt („Deckel 5,4 · dein Kauf hier, bis Preis und Lager zurück“ …), zuletzt mit „…“; (3) Editorzeile 3 „hier: Wegen deines Kaufs hier: …“ — der Deckel-Hinweis ist ein ganzer Satz und steht ohne Vorsilbe, nur Annahmegründe der Station bekommen „Verkauf hier:“; (4) die geöffnete Zeile lag oben abgeschnitten — nach dem Öffnen rollt die Liste 30 Bilder lang nach den echten Knotenlagen nach (Oberkante zuerst), damit eine spätere Wiederherstellung der alten Rollposition (Neuaufbau je Tick) die Zeile nicht verschiebt. Selbsttest Stufe 110 belegt alle drei (Energie an einer Farm gekauft, Deckel aktiv; erste Zeile per echtem Tipp geöffnet, während die letzte offen und die Liste unten war).
- **Nachweis `--ui-check`** (Ordner `space-adventure-scratch-archiv/e3/check2`; neu: Dock-Menü und Bereiche an Werft, Lagerhaus, Sektor-HQ, Handelsstation — jeder Bereich einmal; Forschung nur mit eingeschalteter Forschung, in der Standardwelt aus, daher nicht vermessen). Dock-Zustände, Summe über sechs Auflösungen:

  | Faktor | vorher (Runde 31) A / Ü / K | nachher A / Ü / K |
  |---|---|---|
  | 1,00 | 0 / 0 / 36 | 0 / 0 / 31 |
  | 1,15 | 356 / 0 / 36 | **0 / 0 / 38** |
  | 1,30 | 471 / 3 / 36 | **0 / 0 / 46** |

  Alle K-Befunde stammen aus der Marktspalte der Stationsinfo (Warennamen wie „Leichter Schiffsbausatz“, „Universalfrachtraum 100“ mit „…“ — dieselbe Spaltenbreite wie vorher); mehr sind es, weil die höhere Fläche mehr Zeilen zeigt (z. B. „Chemisches Triebwerk M“), keine neue Art. Füllgrad der Dock-Bereiche bei Faktor 1 (Mittel je Auflösung 1280×720 / 1560×720 / A15 / 16:10 / 4:3 / 960×540): vorher 95 / 78 / 78 / 90 / 82 / 95 %, nachher 95 / 96 / 96 / 96 / 96 / 95 % (kleinster Wert vorher 71 % beim 4:3-Tablet, nachher 95 %). Das Dock-Menü füllt bewusst nur 31–42 % (580 breite Karte). Tippflächen: Dock-Tasten (56 px) am A15 bei groß ≈ 6,7 mm, bei normal ≈ 6,2 mm (vorher 5,4 mm).
- **Prüfungen Runde 33b/34:** `dotnet build`/`dotnet build godot` 0 Warnungen, `dotnet test` 1092 grün, Selbsttest OK (Stufe 82 setzt headless das Fenster auf 1280 × 720 — das Dock füllt jetzt die ganze, headless quadratische Fläche, dann passten alle Zeilen und nichts rollte), Balance 20000 Ticks Hash D3567DA325B7FED9; `--ui-check` Flug (e3/check3): bei 1 / 1,15 / 1,3 in allen Flug-Zuständen 0 außerhalb, 0 Überlappungen, kein verdecktes Schiff, gekürzte Texte unverändert 30.
- **Offen / nicht geprüft:** nichts auf dem Gerät. Die Zeilen im Handel behalten ihre Spaltenlagen (Preis, Bestand, Bord links) — auf breiten Flächen bleibt zwischen „Bord“ und dem Pfeil Platz frei. 16:9-Handys bekommen im Dock fast keine Vergrößerung (1,02). Forschung nicht im Prüfwerkzeug (Standardwelt ohne Forschung).
