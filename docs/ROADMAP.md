# Roadmap (Vorschlag)

Reihenfolge nach Nutzen und Abhängigkeit. Nichts davon ist beschlossen.

1. **Spieler kauft Güter** – erledigt (`TradeActions.Buy/Sell`, Dock-Panel). Schiffe kaufen folgt mit den Schiffsklassen (Schritt 4 unten).
2. **Stationsdienste** (`IStationService`): Reparatur, Tanken, Ausrüster. Schaden und Treibstoff werden zu echten Kosten.
3. **Modul-Modell für Stationstypen** (`recipes` + `services` frei kombinierbar), danach die Komplexe aus [STATIONEN.md](STATIONEN.md).
4. **Schiffsklassen und Multirole** (`ShipClass`/`ShipModel`, Capabilities statt `ShipRole`, Slots, Save v4), Details und Reihenfolge in [SCHIFFE.md](SCHIFFE.md).
5. **Neue Wirtschaftsketten** (Chemie, Recycling, Pharma) gegen das Pendelhandel-Problem und die einseitige Nachfrage.
6. **Fraktionsverhalten** (Beziehungen, Zölle, geschlossene Gates).
7. **Forschung** ([FORSCHUNG.md](FORSCHUNG.md)), weil sie erst mit Fraktionen und Inhalten zum Freischalten Sinn ergibt. Das Datenformat kann früher angelegt werden.
8. **Kampf** mit Waffen, Schilden, Patrouille, Piraten.
9. **Missionen**, Assets, Test auf einem Android-Gerät.

Nach jeder Wirtschaftsänderung: `tools/SpaceSim.Balance` laufen lassen.
