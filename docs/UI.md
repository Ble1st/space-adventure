# Oberfläche (Entwurf)

**Status: Stufen 1 (Flug-HUD, Abschnitt 2), 2 (Andock-Menü/Dock-Bereiche, Abschnitt 3) und 3 (Kommandozentrale, Abschnitt 4) sind umgesetzt** in `godot/scripts/Ui/` (Kit unter `Ui/Kit/`, Bildschirme unter `Ui/Screens/`, Platzhalter zentral in `Ui/UiPlaceholders.cs`). Die Bilder entstanden als Godot-Prototyp (`godot/prototypes/`, kein Teil des Spiels). Sie lassen sich neu erzeugen:

```
dotnet build godot
godot-mono --path godot res://prototypes/proto.tscn -- --backdrop out/backdrop.png
godot-mono --path godot res://prototypes/proto.tscn -- --final out out/backdrop.png     # Flug-HUD und Dock-Bereiche
godot-mono --path godot res://prototypes/proto.tscn -- --shots out out/backdrop.png     # fünf frühere Varianten
```

**Screenshot-Werkzeug fürs Review der echten Oberfläche** (nicht headless, braucht DISPLAY): Lädt eine Demo-Welt, wartet bis die Szene steht, und speichert über `RenderingServer.FramePostDraw` + `Viewport.GetTexture().GetImage().SavePng()` vier PNGs, dann beendet sich das Spiel.

```
godot-mono --path godot -- --ui-shots build/ui-shots/     # 01-04 Flug-HUD/Hamburger, 05-11 Dock, 12-22 Kommandozentrale, 23 HUD mit gedrehter Kamera
```

Verweise: [UMSETZUNGSPLAN.md](UMSETZUNGSPLAN.md), [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md) (Dienste, Feuerwinkel), [POLITIK.md](POLITIK.md) (Spielerkasse).

## 1. Grundregeln

- Querformat 1280×720, Touch zuerst. Trefferflächen mindestens 56 px, Text mindestens 18 px, kein Hover (Details per langem Drücken), Farbe nie allein (immer mit Text oder Symbol).
- Höchstens drei Antipper bis zur Aktion; häufige Aktionen (Max, Alles verkaufen, Voll tanken) haben eigene Knöpfe.
- **Die Oberfläche folgt den Daten:** Menü, Tabs und Zeilen entstehen aus `IStationService.Offers`; neue Dienste brauchen nur eine Zeilenform, keinen neuen Aufbau.
- Godot-Code bleibt dünn; Spiellogik (Feuerwinkel, Preise, Taxi) steht im Core.

## 2. Flug-HUD

**Status: umgesetzt** (`Ui/Screens/FlightHud.cs`, Slider `Ui/Kit/KitSlider.cs`, Waffenzustände `SpaceSim.Core/Weapons/FiringArc.cs` mit Tests, Slider-Rechnung `SpaceSim.Core/Flight/Throttle.cs` mit Tests, Kamerarechnung `SpaceSim.Core/Flight/OrbitCamera.cs` mit Tests). Schild, Sprit und Leistung sind gedämpfte Platzhalter (`UiPlaceholders`), Hülle und Credits echt. Der Mining-Laser sitzt als 360°-Turm, damit sich das Spielverhalten nicht ändert.

- **Steuerung:** Wendetasten ◀ und ▶ (große Haltetasten) unten links; Tempo als **waagerechter Slider unten rechts am Bildrand** von STOPP bis MAX (nur vorwärts), der Griff zeigt den Prozentwert, daneben steht die Geschwindigkeit. **Rückwärts ist eine eigene Haltetaste „RÜCK“** zwischen Wendetasten und Tempo-Slider (80×56 px = halbe Kanten, aber Mindest-Trefferfläche 56 px; gehalten: höchstens **20 % der Höchstgeschwindigkeit**, hat Vorrang vor dem Slider, der unverändert stehen bleibt; entschieden). Der virtuelle Stick entfällt. Der Slider bleibt stehen, bis man ihn ändert. **Wenden bleibt bei Haltetasten** (entschieden). Tastatur: A/D bzw. Pfeil links/rechts drehen (rechts = Nase nach rechts), W bzw. Pfeil hoch erhöht den Slider, X = STOPP, S bzw. Pfeil runter = Rückwärts (Haltetaste). Lenkpriorität: Slider/Tasten > Zeiger > Autopilot; manuelle Bedienung bricht Autopilot und Auto-Mining ab.
- **Kamera und Berührung (entschieden):** **Berührung und Mausklick geben dem Schiff nie eine Flugrichtung** – das Schiff fliegt nur über Wendetasten, Tempo-Slider und RÜCK. **Ein Finger bzw. Linksklick: Wischen schwenkt die Kamera, kurzes Antippen wählt das Ziel** (Station, Gate, Asteroid). Die Kamera umkreist das Schiff (Schiff = Drehpunkt, sie bleibt immer darauf ausgerichtet und folgt seiner Position geglättet): waagerechtes Wischen = Gierwinkel (volle 360°), senkrechtes = Neigung (15–85°, nie unter die Ebene, nie exakt senkrecht). **Schwenkrichtung „Welt greifen“:** Wischen nach rechts schiebt die Szene nach rechts (die Kamera wandert nach links), nach unten steigt die Kamera; im Menü lässt sich das **invertieren**. **Zwei Finger: Pinch zoomt** (Abstand begrenzt). Maus: Ziehen mit linker, rechter oder mittlerer Taste schwenkt, **Mausrad** zoomt. **Kamera-Einstellungen im Hamburger-Menü** (Abschnitt „Kamera“): Zoom −/+, **Freie Kamera** oder **Target-Lock** (die Kamera dreht sich hinter das Schiff, sodass das gewählte Ziel – Asteroid, sonst Autopilot-Ziel im selben Sektor – vor dem Schiff liegt; Neigung und Zoom bleiben frei, waagerechtes Wischen wirkt dann nicht; ohne Ziel bleibt die Kamera stehen), Schwenken invertieren und Zurücksetzen auf die Standardansicht (der frühere feste Offset (0, 38, 24); ohne Eingabe sieht das Spiel wie bisher aus). Die Rechnung (Yaw/Pitch/Abstand → Offset, Klemmung, Invertierung, Target-Lock-Yaw, weiches Nachführen) ist reine Core-Funktion in `SpaceSim.Core/Flight/OrbitCamera.cs` mit Unit-Tests. Alles Bildschirm↔Welt (Antippen, Zielmarker, Radar, Feuerwinkel-Kegel) rechnet mit der gedrehten Kamera. In Menüs, Dock und Vollbild-Bildschirmen greifen keine Kamera-Gesten. Die Ansicht bleibt nach Abdocken und Sprung erhalten und wird **nicht im Spielstand gespeichert**.
- **Hamburger-Menü** unten links: **Kamera-Einstellungen** (Zoom, Freie Kamera / Target-Lock, Schwenken invertieren, Zurücksetzen) und die Systemaktionen (Karte, Wirtschaft, Speichern, Laden, Neues Spiel). Die **Kommandozentrale** (Abschnitt 4) hat einen **eigenen Knopf „Kommando“ oben rechts** (mit Comms-Zähler). Die Schalter für Autopilot und Auto-Mining bleiben vorerst an der Zielkarte.
- **Waffen:** Rechts am Rand **eine Taste je Waffenschacht** (Laser, Geschütz, Traktor). Jede Taste zeigt den Feuerwinkel als Symbol und den Zustand:

| Zustand | Darstellung | Bedeutung |
|---|---|---|
| Im Winkel und bereit | grün, „FEUER“ | feuert sofort |
| Ziel außerhalb | orange, Hinweis „◀ 41° drehen“ | gesperrt, zeigt, wie weit zu drehen ist |
| Lädt nach | grau mit Balken | nach dem Schuss |
| Kein Ziel oder keine Beute | gedämpft | z. B. Traktor ohne Brocken in Reichweite |

  Die Feuerwinkel erscheinen auch als Kegel um das Schiff. „Alle Feuer“ löst nur bereite Waffen im Winkel aus. Datenbasis: `facing` und `arc` der Slots (SCHIFFSSLOTS.md).
- **Oben:** Statusbalken (Hülle, Schild, Sprit, Leistung) und Credits links, Sektorchip und Funkfeed Mitte, **„Kommando“ (Kommandozentrale), Karte und Zeitraffer** rechts, darunter die Zielkarte mit Radar und Schnellschaltern (Autopilot, Auto-Mining).
- **Kontexttaste:** Nahe einer Station erscheint „Andocken“, am Gate „Springen“.

## 3. Andock-Menü und Dock-Bereiche

**Status: umgesetzt** (`Ui/Screens/DockMenu.cs`, `DockAreaScreen.cs` + `TradeScreen.cs`, `StationInfoScreen.cs`, `HangarScreen.cs`, `PlaceholderAreaScreen.cs`; Bereichsliste datengetrieben in `DockAreas.cs`, Registry-Instanz in `GameSession.Services`). Das alte `DockPanel` entfällt. Handel: Zeilen und Aktionen ausschließlich über `IStationService.Offers`/`Execute` (`buy:<id>`/`sell:<id>`/`sell_all`), Liste/Kacheln umschaltbar, Zeile antippen klappt Stepper/Summe/Kaufen/Verkaufen auf, rechte Spalte Schiff mit Fracht/Laderaum/Credits echt, beste Verkaufsstelle je Ware über `MarketInfo.BestBuyerForGood` (Core, getestet). **Platzhalter:** Taxi, Aufträge (Reparatur & Tanken echt seit Phase 4c; Ausrüster und Werft echt seit Phase 5c: `OutfitterScreen`/`ShipyardScreen`, nur `Offers`/`Execute` der Dienste `outfitter`/`shipdealer`; der Hangar zeigt eigene geparkte Schiffe mit „Übernehmen“) (gedämpft mit „Bald“, kurze Erklärseite, Texte in `UiPlaceholders`); „Rückgängig“ und Preisverlauf-Trends im Handel; Ruf/Zoll/Sperrstufe/Hinweise der Station-Info; fremde/weitere Schiffe und Steuern/Befehle/Verkaufen/Heim schicken im Hangar; Liegeplätze/Gebühr. Schiffwechsel erst in Stufe 3.

Beim Andocken erscheint zuerst ein **kleines Auswahlmenü**:

1. **Angedockte Schiffe** oben als Karten (aktives Schiff, weitere eigene, fremde).
2. **Bereiche** darunter, je mit Untertitel: Handel, Reparatur und Tanken, Ausrüster, Werft, Hangar, Station-Info, Taxi, Aufträge.
3. **Abdocken** als große Taste unten.

Jeder Bereich öffnet im Vollbild mit linker Leiste (Wechsel zwischen den Bereichen), „← Menü“ und „Abdocken“ immer sichtbar; in Handel, Reparatur und Tanken zeigt eine rechte Spalte das Schiff mit Fracht.

| Bereich | Inhalt |
|---|---|
| Handel | Liste oder Kacheln (umschaltbar), Zeile antippen klappt Menge, Summe und Kaufen auf; Rückgängig; beste bekannte Verkaufsstelle |
| Reparatur und Tanken | Reparatur verbraucht Hüllenplatten aus dem Stationslager, Treibstoff je Antriebsart zum Marktpreis |
| Ausrüster | Schiffsskizze mit antippbaren Slots, Modulvergleich mit Pfeilen, Leistungsbalken, Upgrade mit offener Chance |
| Werft | Modelle mit Werten, Preis, Bausatz- und Materialverfügbarkeit (kleine Schiffe auch an anderen Stationen) |
| Hangar | Alle angedockten Schiffe mit Zustand und Auftrag; steuern, Befehle, verkaufen, nach Hause schicken |
| Station-Info | Besitzer, Ruf, Zoll, Sperrstufe, Nachfrage, Dienste, Hinweise |
| Taxi | Siehe unten |

### Taxi (entschieden)

Das **Taxi bringt den Spieler allein zu einer anderen Station, das Schiff bleibt im Hangar.** Fracht und Treibstoff bleiben im Schiff, das Geld bleibt bei der Person (POLITIK.md, „Spielerkasse“).

- Die Liste zeigt je Ziel Preis, Dauer, Zoll und Lage, und ob dort ein eigenes Schiff wartet („Dort wartet: Spatz“) oder ein Leihschiff nötig ist („Kein eigenes Schiff · Leihschiff ab 180 Cr“).
- Gesperrte Routen (Gate zu, Embargo) sind nicht buchbar und zeigen den Grund.
- Das zurückbleibende Schiff kostet **Liegegebühr nur an fremden Stationen**, **abhängig von der Schiffsklasse** (kleine Schiffe wenig, schwere viel); an eigenen Stationen ist sie gratis (entschieden). Beispielwerte: Skiff und Kurier 12 Cr je Tag, leichter Frachter und Bergbauer 24, schwerer Frachter 60, später Kampfschiffe mehr.
- Am Ziel wählt der Spieler im Andock-Menü ein dort wartendes eigenes Schiff oder mietet eines (`rental`).
- Die Routenkarte markiert die eigenen Schiffe an ihren Standorten.

## 4. Kommandozentrale

**Status: umgesetzt** (`Ui/Screens/CommandCenter.cs` als Basis wie `DockAreaScreen` – Vollbild, linke Leiste mit den Bereichen plus „System“, Inhalt rechts, „← Flug“ unten links; geöffnet über den Knopf „Kommando“ oben rechts im HUD (`CommandMenu.cs` ist jetzt das Hamburger-Menü mit Kamera-Einstellungen); registriert in `ScreenRegistry`). Echt angebunden: Sektorkarte (Sektoren/Gates/Fraktions-Halos/Spieler/Stationszahl), Ebenen Fraktion/Schiffe/Preise (Preise über die Core-Funktion `SectorEconomy.PriceLevel`, getestet), Sektor-Draufsicht (Stationen, Gürtel, Gates, NPC-Schiffe via `Materializer`, Spieler), Route über `Universe.FindRoute` (Sprungzahl + Weg), „Sektorkarte öffnen“, „Route planen/Autopilot“ und „Autopilot hierhin“ (setzen das Autopilot-Ziel in Main wie die Sternenkarte), Fraktionsliste (Name, Farbe, Stationen, Schiffe, Vermögen), „Gebiet auf Karte“, Kasse der Person (= Credits des Spielerschiffs, so beschriftet – wandert später zur Person, siehe POLITIK.md), Zahl eigener Schiffe/Stationen, Gesamtwert (Credits + Fracht zu Basispreisen über `SectorEconomy.TotalValue`, getestet), Flottenliste mit Auftrag aus `Ship.Order`/Auto-Mining, Besitz-Tabs und Schiffsdetail, Auto-Mining-Schalter in der Flotte (einzige echte Befehlswirkung). **Platzhalter** (gedämpft, „Platzhalter“/„Bald“, Texte zentral in `UiPlaceholders.cs`): Sicherheitsebene, Taxi buchen (Preis-Platzhalter), Beziehung/Art/Persönlichkeit/Ereignisse der Fraktionen, Größenstufe/Fortschritt/Mitgliedschaft/Unterhalt/Liegegebühr/Einnahmen/Ruf/Fähigkeiten, Befehlsraster/Verhalten/Befehlskapazität, „In dieses Schiff wechseln“ („Bereits aktiv“ bzw. „Bald (Phase 12)“ – der Spieler besitzt nur ein Schiff), Befehle/Heim schicken/Verkaufen, gesamter Comms-Bereich (Nachrichten, Antworten mit Folgenvorschau; Zähler am Hamburger-Menü).

Der Knopf „Kommando“ oben rechts öffnet die Kommandozentrale im Vollbild, gebaut wie das Dock: linke Leiste, Inhalt rechts, „← Flug“ unten links.

| Bereich | Inhalt |
|---|---|
| Universum | Sektoren mit Fraktionsfarben als Halo, Gates offen, gesperrt oder mit Zoll, eigene Schiffe als Symbole, Warnungen. Ebenen schaltbar (Fraktion, Sicherheit, Preise, Schiffe). Tippen wählt einen Sektor; die Karte rechts zeigt Besitzer, Kontrolle, Sicherheit, Stationen, eigene Schiffe, Route, Taxipreis. Knöpfe: Sektorkarte öffnen, Route planen, Taxi buchen. |
| Sektor | Draufsicht auf den Sektor: Stationen, Gürtel, Gates (mit Ziel und Zoll), eigene und fremde Schiffe. Der Umschalter „Universum / Sektor“ oben wechselt die Ebene. Knöpfe: Autopilot hierhin, Andocken, Info öffnen. |
| Fraktionen | Liste aller bekannten Fraktionen mit Beziehung (−100 bis +100), Art, Lage; Detail mit Persönlichkeit (Aggression, Gier, Kooperation, Gesetzestreue), Lage, Beziehungen zu anderen, Beziehung zum Spieler, letzten Ereignissen. Knöpfe: Kontakt aufnehmen, Gebiet auf Karte. |
| Meine Fraktion | Größenstufe und Fortschritt zur nächsten Stufe (Beiträge von Schiffen, Stationen, Kasse), Kasse der Person, Mitgliedschaft (Beitreten, Gründen ab Kleinfirma), Unterhalt, Liegegebühr, Einnahmen; Ruf und Fahndung je Fraktion; Fähigkeiten nach Größe mit Schloss für Gesperrtes. |
| Flotte | Verbände mit Anführer, aktuellem Befehl, Heimatbasis, Ebene und Notfallregel; Befehlsraster (Fliegen, Halten, Handel, Schürfen, Patrouille, Wache, Eskorte, Heimkehr), gesperrt nach Fähigkeit; Verhalten (Passiv, Verteidigen, Angriff, Feuer halten); Automatik mit Reichweite und Budget; Befehlskapazität im Kopf. |
| Besitz | Kasse, Zahl der Schiffe, Stationen, Verträge, Gesamtwert; Tabs Schiffe, Stationen, Verträge, Lager; je Schiff Standort, Auftrag, Hülle, Sprit. Tippen öffnet ein Detailfenster. |
| Comms | Posteingang (Zoll, Warnungen, Angebote) und Verhandlungsfenster mit Antworten und **Folgenvorschau** (Ruf, Kosten, Chance): Vertrag annehmen, Tarif verhandeln, Passierschein kaufen, Schutzvertrag, Krieg erklären (rot, nur mit Kriegsgrund). Ignorieren kostet keinen Ruf, Angebote verfallen. |

### Schiff wechseln (entschieden)

Im Besitz öffnet Tippen auf ein Schiff ein **Detailfenster** (Zustand, Module, Auftrag, Standort, Unterhalt, Wert). Der Knopf **„In dieses Schiff wechseln“** setzt den Spieler in das gewählte Schiff, **dort, wo es ist**:

- Das **alte Schiff bleibt an Ort und Stelle**: angedockt im Hangar (Liegegebühr nur an fremden Stationen), sonst gilt: **Hat es einen Befehl, führt es ihn weiter; hat es keinen, hält es die Position** (entschieden). Ein Schiff im freien Raum bleibt ungeschützt (Warnhinweis).
- Das **Geld bleibt bei der Person** (POLITIK.md, „Spielerkasse“); Fracht und Treibstoff bleiben im jeweiligen Schiff.
- Weitere Knöpfe im Fenster: Befehle, Heim schicken, Verkaufen.

## 5. Offene Fragen

- Leihschiff: welches Standardmodell, welche Miete und welches Pfand?
- Liegegebühr an fremden Stationen: genaue Staffel je Klasse, mit oder ohne Freitage, Rabatt bei gutem Ruf?
- Aufträge im Andock-Menü sind Platzhalter, bis es Missionen gibt.
