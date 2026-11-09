# Oberfläche (Entwurf)

**Status: Entwurf. Nichts davon ist im Spiel umgesetzt.** Die Bilder entstanden als Godot-Prototyp (`godot/prototypes/`, kein Teil des Spiels). Sie lassen sich neu erzeugen:

```
dotnet build godot
godot-mono --path godot res://prototypes/proto.tscn -- --backdrop out/backdrop.png
godot-mono --path godot res://prototypes/proto.tscn -- --final out out/backdrop.png     # Flug-HUD und Dock-Bereiche
godot-mono --path godot res://prototypes/proto.tscn -- --shots out out/backdrop.png     # fünf frühere Varianten
```

Verweise: [UMSETZUNGSPLAN.md](UMSETZUNGSPLAN.md), [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md) (Dienste, Feuerwinkel), [POLITIK.md](POLITIK.md) (Spielerkasse).

## 1. Grundregeln

- Querformat 1280×720, Touch zuerst. Trefferflächen mindestens 56 px, Text mindestens 18 px, kein Hover (Details per langem Drücken), Farbe nie allein (immer mit Text oder Symbol).
- Höchstens drei Antipper bis zur Aktion; häufige Aktionen (Max, Alles verkaufen, Voll tanken) haben eigene Knöpfe.
- **Die Oberfläche folgt den Daten:** Menü, Tabs und Zeilen entstehen aus `IStationService.Offers`; neue Dienste brauchen nur eine Zeilenform, keinen neuen Aufbau.
- Godot-Code bleibt dünn; Spiellogik (Feuerwinkel, Preise, Taxi) steht im Core.

## 2. Flug-HUD

- **Steuerung:** Wendetasten ◀ und ▶ (große Haltetasten) unten links; Tempo als **waagerechter Slider unten rechts am Bildrand** von STOPP bis MAX, der Griff zeigt den Prozentwert, daneben steht die Geschwindigkeit. Der virtuelle Stick entfällt. Der Slider bleibt stehen, bis man ihn ändert.
- **Hamburger-Menü** unten links: zunächst leerer Platzhalter, später Karte, Schiff, Flotte, Aufträge, Einstellungen sowie die Schalter für Autopilot und Auto-Mining.
- **Waffen:** Rechts am Rand **eine Taste je Waffenschacht** (Laser, Geschütz, Traktor). Jede Taste zeigt den Feuerwinkel als Symbol und den Zustand:

| Zustand | Darstellung | Bedeutung |
|---|---|---|
| Im Winkel und bereit | grün, „FEUER“ | feuert sofort |
| Ziel außerhalb | orange, Hinweis „◀ 41° drehen“ | gesperrt, zeigt, wie weit zu drehen ist |
| Lädt nach | grau mit Balken | nach dem Schuss |
| Kein Ziel oder keine Beute | gedämpft | z. B. Traktor ohne Brocken in Reichweite |

  Die Feuerwinkel erscheinen auch als Kegel um das Schiff. „Alle Feuer“ löst nur bereite Waffen im Winkel aus. Datenbasis: `facing` und `arc` der Slots (SCHIFFSSLOTS.md).
- **Oben:** Statusbalken (Hülle, Schild, Sprit, Leistung) und Credits links, Sektorchip und Funkfeed Mitte, Karte und Zeit rechts, darunter die Zielkarte mit Radar und Schnellschaltern (Autopilot, Auto-Mining).
- **Kontexttaste:** Nahe einer Station erscheint „Andocken“, am Gate „Springen“.

## 3. Andock-Menü und Dock-Bereiche

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
- Das zurückbleibende Schiff kostet Liegegebühr (zum Beispiel 12 Cr je Tag).
- Am Ziel wählt der Spieler im Andock-Menü ein dort wartendes eigenes Schiff oder mietet eines (`rental`).
- Die Routenkarte markiert die eigenen Schiffe an ihren Standorten.

## 4. Offene Fragen

- Soll „Wenden“ später auch als waagerechter Slider mit Rückfederung wählbar sein?
- Rückwärtsfahren: unterhalb von STOPP oder gar nicht?
- Leihschiff: welches Standardmodell, welche Miete und welches Pfand?
- Liegegebühr im Hangar: je Tag, je Schiff, mit oder ohne Freitage?
- Aufträge im Andock-Menü sind Platzhalter, bis es Missionen gibt.
