# Roadmap (Vorschlag)

Reihenfolge nach Nutzen und Abhängigkeit. Nichts davon ist beschlossen. Der ausgearbeitete Gesamtplan steht in [UMSETZUNGSPLAN.md](UMSETZUNGSPLAN.md).

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

### Zweite Durchsicht: Module, Waren und Lücken

9. **Erledigt (siehe SCHIFFSSLOTS.md, „Module sind Waren“).** **Module sind Waren, stehen aber nicht in `goods`:** SCHIFFSSLOTS.md definiert Module in einem eigenen Abschnitt mit `cost`, sagt aber, sie lägen als Waren im Stationslager. Entscheidung offen. Empfehlung: Jedes Modul bekommt automatisch eine Ware gleichen Schlüssels (mit Volumen); das `cost`-Feld wird das Rezept einer Modulfabrik; der Ausrüster verkauft und baut aus dem Lager ein.
10. **Erledigt: drei Modulfabriken.** **Wer stellt die Module her?** In der Ideenliste gibt es nur Schildgenerator-Werk und Triebwerkswerk. Reaktoren, Panzerung, Laser, Tanks, Sprungantrieb und Tarnfeld haben keine Fabrik. Es braucht Modulfabriken (zum Beispiel Reaktor-, Waffen-, Utility-Werk), sonst sind die Module nicht herstellbar.
11. **Erledigt: Flüssigstoffe in Kanistern (SCHIFFSSLOTS.md, „Warenform und Kanister“).** **Warenform fehlt:** Tanker und Gefahrgut setzen eine Eigenschaft der Ware voraus (fest, flüssig, Gas, gefährlich). Die `goods`-Einträge haben sie nicht.
12. **Zurückgestellt (SCHIFFSSLOTS.md, Abschnitt 11); Lebenserhaltung für Schiffe entfällt vorerst.** **Besatzung fehlt:** SCHIFFE.md verlangt Lebenserhaltung für große Frachter und Kapitalschiffe als laufenden Verbrauch; kein Dokument beschreibt Besatzung oder diesen Verbrauch.
13. **Erledigt: `Research` ist als Schiffsfähigkeit gestrichen** (SCHIFFE.md, SCHIFFSSLOTS.md, UMSETZUNGSPLAN.md Phase 2); Forschung gehört dem Akteur.
14. **Erledigt: Leistung (`power`, Schiffe, Reaktor und Speicherbank) und Energie (`energy`, Stationen und Produktion) sind getrennt; Treibstoff ist eine eigene Ware.** **Namenskollision „Energie“:** `energy` ist eine Ware (Strom der Stationen), `power` die Reaktorleistung eines Schiffs, und „Energie“ steht auch als Treibstoff (Solarsegel). Drei Bedeutungen, besser trennen.
15. **Bausatz:** Im Code gibt es einen `EconomyRules.ShipKitGoodKey` (ein Schlüssel). Die Planung hat vier Stufen; `FleetSystem` und die Werftrezepte müssten pro Klasse wählen.
16. **Entschärft durch eine Verarbeitungsstufe (Treibstoffraffinerie), Balance-Test bleibt.** **Treibstoff gegen Kraftwerke:** Wasserstoff speist das Kraftwerk und soll zugleich Schiffe antreiben. Neue Nachfrage kann die Energieversorgung der Startwelt verdrängen. Vor der Umsetzung mit `tools/SpaceSim.Balance` testen.
17. **Erledigt (POLITIK.md, „Akteur und Fraktion“).** **Spieler gehört heute einer Fraktion:** In der JSON hat der Spieler `player.faction`. Mit dem Akteurmodell wird er ein eigener Akteur; Unterhalt, Beziehungen und Startpakete müssen darauf migriert werden.
18. **Erledigt: Eroberung zunächst abgeschaltet, `Essential` aufgedröselt (STATIONEN.md, FLOTTEN.md 7a).** **Eroberung:** Sektorkontrolle und Totaler Krieg sprechen von Eroberung. Was mit einer eroberten Station passiert (Besitzerwechsel, Zerstörung, Plünderung), ist nirgends beschrieben; `Station.Owner` existiert, aber `Essential` schützt nur vor Pleite.

Geprüft und in Ordnung: alle Code-Bezeichner, die die Dokumente als vorhanden nennen (`TradeActions`, `FindRoute`, `UpkeepSystem`, `PatrolOrder` und weitere), existieren; alle Warenschlüssel in den JSON-Beispielen stimmen jetzt mit `default-world.json` überein (neue Schlüssel wie `ship_kit_light` sind bewusst Planung); die Wahrscheinlichkeiten der Güteklassen summieren sich auf 100 %; Sektor-HQ und Dominanz sind nicht zirkulär (die Wache genügt für Dominanz).

### Dritte Durchsicht: Umsetzungsplan gegen die Dokumente (2026-11-10)

19. **Erledigt (Plan, Phase 4 und Diagramm):** Phase 4 (Treibstoff) hängt von Phase 3 ab (Tank- und Antriebsmodule, Warenform `liquid`, `ShipStats`), das Diagramm zeigte nur Phase 1. SCHIFFSSLOTS.md, Abschnitt 9, nannte noch die Reihenfolge Treibstoff vor Slots; angeglichen, **Slots vor Treibstoff**.
20. **Offen, Entscheidung vor Phase 5: Lager ohne Rezept.** Ausrüster, Handelsstation, Raffinerie und Werft-Montage brauchen Module bzw. `ship_kit_light` im Lager, aber Märkte entstehen heute nur aus Rezepten. Vorschlag: `stocks` am Stationstyp (STATIONEN.md).
21. **Offen, Entscheidung vor Phase 5: Preis und Form der Modul-Waren.** Automatische Waren brauchen `BasePrice` (Vorschlag: Summe der Zutatenpreise mal Verarbeitungsaufschlag), `Volume`, `form = solid`, Farbe und eine **feste Reihenfolge am Ende von `goods`**, damit die `GoodId`s (Index+1) und die Konstanten in `DemoUniverse` stabil bleiben.
22. **Erledigt (POLITIK.md):** Die Spielerkasse (Phase 5) kommt vor den Akteuren (Phase 9): Zwischenstufe `PlayerWallet`.
23. **Offen, Entscheidung vor Phase 8:** `Critical` (abgeleitet) wird in Phase 8 gebraucht, die Aufschlüsselung von `Essential` steht in Phase 10. Vorschlag: In Phase 8 nur `Critical` und `BankruptcyProtected`, der Rest in Phase 10.
24. **Erledigt (Plan):** Das Startpaket „Generalist“ (Phase 5) nutzt *Pip* mit Werkzeug-Slot; *Tinker* kommt erst mit Phase 6 (SCHIFFE.md, Abschnitt 9, nennt beide).
25. **Erledigt (Plan):** Die Oberfläche in Phase 12 ist bereits gebaut (Flug-HUD, Andock-Menü, Kommandozentrale); offen bleibt nur die Anbindung an echte Daten (Hangar, Taxi, Flotte).

## Phase 3 abgeschlossen (Stand der Umsetzung)

26. **Erledigt:** Slot-/Modulmodell, Warenform, `ShipStats`, Save v4 (Phase 3a–3c). Beim Abschluss gefundener und behobener Fehler: Nach Speichern/Laden hielten Schiffe mit Modell ihre alten Fähigkeiten fest, ein Ausbau (zum Beispiel des Lasers) wirkte nicht mehr; jetzt folgen die Fähigkeiten von Schiffen mit Modell immer dem Fit.
27. **Offen für Phase 4/5:** Leistungsbilanz auch zur Laufzeit beim Umrüsten prüfen (Ausrüster); Höchsttempo (`ShipStats.MaxSpeed` = 12) gegen echte Antriebe neu bewerten; Laserstufen als Handelsware.

