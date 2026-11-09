# Roadmap (Vorschlag)

Reihenfolge nach Nutzen und Abhängigkeit. Nichts davon ist beschlossen.

1. **Spieler kauft Güter** – erledigt (`TradeActions.Buy/Sell`, Dock-Panel). Schiffe kaufen folgt mit den Schiffsklassen (Schritt 4 unten).
2. **Stationsdienste** (`IStationService`): Handel als Dienst, Reparatur, Tanken (Treibstoff als Ware), Ausrüster. Schaden und Treibstoff werden zu echten Kosten. Details in [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md).
3. **Modul-Modell für Stationstypen** (`recipes` + `services` frei kombinierbar), danach die Komplexe aus [STATIONEN.md](STATIONEN.md).
4. **Schiffsklassen und Multirole** (`ShipClass`/`ShipModel`, Capabilities statt `ShipRole`, Slots, Save v4), Details und Reihenfolge in [SCHIFFE.md](SCHIFFE.md).
5. **Neue Wirtschaftsketten** (Chemie, Recycling, Pharma) gegen das Pendelhandel-Problem und die einseitige Nachfrage.
6. **Fraktionsverhalten** (Beziehungen, Zölle, geschlossene Gates), siehe [POLITIK.md](POLITIK.md). Danach **Flotten, Befehle, Polizei** ([FLOTTEN.md](FLOTTEN.md)).
7. **Forschung** ([FORSCHUNG.md](FORSCHUNG.md)), weil sie erst mit Fraktionen und Inhalten zum Freischalten Sinn ergibt. Das Datenformat kann früher angelegt werden.
8. **Kampf** mit Waffen, Schilden, Patrouille, Piraten.
9. **Missionen**, Assets, Test auf einem Android-Gerät.

Nach jeder Wirtschaftsänderung: `tools/SpaceSim.Balance` laufen lassen.

## Bekannte offene Widersprüche in der Planung

Stand der Durchsicht der Dokumente (alles Planung, keine Code-Fehler):

1. **Forschung:** FORSCHUNG.md bindet sie an die Forschungsstation und Fraktion; POLITIK.md legt Budget und Richtung ins HQ. Offen: Wem gehört der Technologiestand bei Einzelakteuren und beim Spieler ohne HQ? Vorschlag: Der Akteur mit Forschungsstation besitzt ihn, das HQ steuert nur das Budget.
2. **Hub-Konflikt:** Hauptquartier und Kapitalwerft sind beide Hub-Stationen, aber es gibt nur einen Hub je Sektor (STATIONEN.md, Abschnitt 5).
3. **Größe/Klasse:** Das Wort „Klasse“ steht für Schiffsklasse, Asteroidenklasse und Größenklasse eines Akteurs. In künftigen Texten besser „Größenstufe“ für Akteure.
4. **Mehrere medium Stationen je Sektor:** Es ist nicht festgelegt, wie viele mittelgroße Stationen ein Sektor fasst (Sektor-HQ, Firmensitz, Patrouillenbasis, Handelsstation konkurrieren).
5. **Polizeiwache:** als Dienst oder als kleine Station beschrieben; noch zu entscheiden.
6. **Sprungreichweite:** hängt an `Sector.MapPosition`, die heute nur Darstellung ist.
7. **Tarnfeld im Hoheitsgebiet:** „unlizenziert“ setzt ein Lizenzsystem voraus, das nirgends beschrieben ist.
8. **Persönlichkeitswerte und Wirtschaft:** Sie verändern Zukauf und Zölle und müssen im Balance-Tool beobachtet werden.
