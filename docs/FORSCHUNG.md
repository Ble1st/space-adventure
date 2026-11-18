# Forschung und Technologie

> **Stand:** In Phase 13 umgesetzt (Save v16) — Projekte im Weltdatei-Abschnitt `research`, Forschungsstationen je Fraktion, fraktionsweite Freischaltungen (Mk3-Module, Excavator), Dienst „research“ (beauftragen, mitfinanzieren). Abweichungen (Verbrauch ohne Elektronik als Hauptgut, nur Modul-/Modell-Freischaltungen) stehen in [UMSETZUNGSPLAN.md](UMSETZUNGSPLAN.md), Phase 13.

## Idee
Die Forschungsstation ist eine **Wissenssenke mit Langzeitwirkung**: Sie verbraucht teure Güter und liefert keine Ware, sondern schaltet über die Zeit neue Möglichkeiten frei. Sie ist die einzige Station, die den *Inhalt* der Wirtschaft verändert und nicht nur ihre Mengen.

## Verbrauch
Elektronik (Hauptverbrauch), Platin (selten, teuer), Energie; optional Titan und Siliziumwafer für Spezialzweige. Die Station hängt damit an Halbleiterwerk, Platinraffinerie und Energie und ist ein zusätzlicher Abnehmer neben Schiffsbau und Kolonie (hilft gegen die einseitige Endnachfrage).

## Ablauf
1. Die Station sammelt Forschungspunkte (FP), indem sie pro Intervall (z. B. 10 Ticks) Güter verbraucht.
2. Eine Warteschlange bestimmt das aktuelle Projekt (Fraktionsplaner oder Heuristik nach Knappheit; später der Spieler).
3. Reichen die FP, gilt das Projekt als abgeschlossen und wird freigeschaltet (Event `ResearchCompleted`).
4. Fehlen Güter oder Geld, stockt die Forschung, wie bei der Produktion.

## Was freigeschaltet werden kann
| Art | Beispiel |
|---|---|
| Rezepte | Besserer Stahlofen mit weniger Eisenverbrauch |
| Stationstypen | Rüstungswerk, Chemiefabrik werden erst baubar |
| Schiffsklassen | Frachter mit mehr Laderaum, Miner mit besserem Laser |
| Verbesserungen | Höhere `ChunkChance`, schnelleres Nachladen, weniger Fuel-Kosten |
| Güter/Erze | Seltene Erze, die erst nach Forschung sinnvoll abbaubar sind |

## Besitz
- **Fraktionsweit (empfohlen):** Der Tech-Stand gehört der Fraktion und gilt für alle ihre Stationen und Schiffe. Technologie wird zum Wettbewerbsvorteil, die Station zum Angriffsziel. Passt zu den geplanten Fraktionen.
- Weltweit: einfacher, aber ohne Wettbewerb.

## Spieler
Kann Forschung beschleunigen, indem er Güter liefert (z. B. Mission "Liefere 50 Platin"), und erhält früheren Zugriff auf neue Schiffe und Ausrüstung.

## Architektur (Nähte)
- **Daten:** Abschnitt `research` in der Welt-JSON. Projekt: `key`, `name`, `cost` (Güterliste + FP), `requires`, `unlocks`.
- **System:** `ResearchSystem` in einem eigenen Modul, mit Intervall (dt-skaliert), veröffentlicht `ResearchCompleted` über den Event-Bus.
- **Nutzung:** Systeme, die etwas freischalten (Lifecycle für Stationsbau, `FleetSystem` für Schiffsklassen, Rezepte), fragen `faction.IsUnlocked(key)`, nichts wird hartcodiert.
- **Persistenz:** abgeschlossene Projekte und FP-Stand im `SaveGame` (Version 4 mit Migration), Eintrag in `UniverseHash`.
- Zufall nur über `Universe.Rng`, damit die Simulation deterministisch bleibt.

## Risiken
- **Balance:** Zu schnell entwertet neue Inhalte, zu langsam lässt sie nie erscheinen. Das Balance-Tool braucht einen Parameter für den Forschungsstand.
- **Totlauf:** Eine Kette darf nicht Güter brauchen, die erst durch Forschung entstehen. Die Startwirtschaft muss ohne Forschung vollständig sein.
- **Rückschritt:** Wird die Station zerstört, soll nichts rückgängig gemacht werden, oder es wird bewusst entworfen.

## Reihenfolge
Erst Handelsposten, Schiffshändler und Fraktionsverhalten, dann Forschung, denn ihr Wert entsteht durch sinnvolle Freischaltungen. Das `research`-Datenformat kann früh angelegt werden, damit neue Inhalte von Anfang an als freischaltbar markiert werden können.

## Forschung im Großmaßstab (entschieden)

Forschung bleibt in der Standardwelt **aus** (`rules.researchActive` false, Phase 14). Eingeschaltet (`--research on`) lief sie in der vergrößerten Welt (Runde 44) langsamer als bei der Kalibrierung in Phase 13 (Fusion ≈ 6700, Präzisionslaser ≈ 12700, Tiefbau ≈ 22700 Ticks):

- **Vorher** (mit Treibstoff an, Standard-Seed und Seeds 1–4): Fusionstechnik 8470–9980, Präzisionslaser 14700–36580, Tiefbau-Schürfer 26910–51080 Ticks (die dritte Fraktion bis zu doppelt so spät); 1600–5000 Forschungstakte gestockt (Wafer, Energie, Titan fehlen).
- **Ursache:** Die Lager der Forschungsstation (Energie 30, Wafer 8, Platin 4, Titan 6) reichten für 150–400 Ticks Betrieb — kürzer als eine Lieferfahrt in der vergrößerten Welt (Ø 150–200 Ticks, 90 % bis 390). Kleine Lieferungen über lange Wege sind für Händler zudem wenig lohnend.
- **Entscheidung:** Lagerziele der Forschungsstation verdreifacht (Energie 60/90, Platin 6/12, Wafer 12/24, Titan 9/18; Elektronik unverändert 6/10) — derselbe Hebel wie die Rezeptlager in Runde 44. Punkte, Takte, Projektkosten und Budget bleiben.
- **Nachher:** Fusionstechnik **7080–9040**, Präzisionslaser **13080–15040**, Tiefbau-Schürfer **23080–25040** Ticks — wieder die Zeiten aus Phase 13, und alle drei Fraktionen liegen dicht beieinander. Gestockt nur noch 500–630 Takte (etwa die Hälfte „Budget fehlt“). Budget aus den Kassen: **10,9–11,4 %** der Kasseneinnahmen über 20000 Ticks (Phase 13: 15,8 %), **5,4–5,6 %** über 60000 Ticks (Phase 13: 7,4 %; nach Tick ≈ 25000 ist alles erforscht, die Kassen der größeren Welt wachsen weiter).
