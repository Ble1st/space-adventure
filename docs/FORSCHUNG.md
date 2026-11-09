# Forschung und Technologie (Plan, nicht umgesetzt)

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
