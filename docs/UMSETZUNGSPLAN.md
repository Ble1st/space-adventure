# Umsetzungsplan

Gesamtplan für alles, was in den Planungsdokumenten steht. Reihenfolge nach Abhängigkeit und nach dem Grundsatz **Tiefe vor Breite**: Zuerst wird die Schiffs- und Handelskette vollständig, dann folgen Welt, Politik und Flotten. Der Plan ist ein Vorschlag, nichts davon ist begonnen. Größen: **S** (kleiner Schritt), **M** (mehrere Dateien, eigene Tests), **L** (neues Teilsystem mit Save-Änderung und Balance-Arbeit).

Verweise: [SCHIFFE.md](SCHIFFE.md), [SCHIFFSSLOTS.md](SCHIFFSSLOTS.md), [STATIONEN.md](STATIONEN.md), [POLITIK.md](POLITIK.md), [FLOTTEN.md](FLOTTEN.md), [FORSCHUNG.md](FORSCHUNG.md), [ROADMAP.md](ROADMAP.md).

## 0. Arbeitsregeln für jede Phase

1. **Architektur zuerst:** Neue Funktion als `ISimSystem` in einem Modul (`DefaultModules()`), Kopplung über `Universe.Events`, Inhalte in `default-world.json`, `SpaceSim.Core` ohne Godot und Dateizugriff.
2. **Test zuerst bei Umbauten:** Vor einem Umbau müssen die vorhandenen Tests grün sein; der Umbau darf kein Verhalten ändern, bis die Phase es ausdrücklich will (`UniverseHash`-Tests belegen es).
3. **Persistenz:** Neue Zustände in `SaveGame`-DTOs, `UniverseSerializer` und `UniverseHash`; bei Formatbruch `SaveGame.CurrentVersion` erhöhen und eine Migration schreiben (Stand heute: v3).
4. **Balance:** Nach jeder Wirtschaftsänderung `tools/SpaceSim.Balance` laufen lassen und die Kennzahlen im Commit nennen.
5. **Abnahme:** `dotnet test`, `godot-mono --headless --path godot --fixed-fps 60 -- --selftest` und, wo sichtbar, ein Handtest der Oberfläche.
6. **Dokumentation:** `CLAUDE.md` und die betroffenen Dokumente in derselben Phase aktualisieren.
7. **Git:** Ein Commit pro Phase oder Teilschritt; Push nur auf Anfrage.

## 1. Überblick

| Phase | Inhalt | Größe | Save |
|---|---|---|---|
| 1 | Dienste-Rahmen (`IStationService`), Handel als Dienst | M | – |
| 2 | Fähigkeiten und Steuerer statt fester Rolle | M | – |
| 3 | Slot- und Modulmodell, Warenform und Kanister, Leistung und Speicherbank | L | v4 |
| 4 | Treibstoff: Ware, Tank, Tanken, Reparatur, Rettungsdienst | L | v5 |
| 5 | Modul-Waren, drei Modulfabriken, Ausrüster, Schiffskauf, Startpakete | L | v6 |
| 6 | Multirole: Job-Zuteilung, Kapazitätsgrenzen, erste Multirole-Modelle | M | – |
| 7 | NPC-Güteklassen und Upgrades | M | v7 |
| 8 | Bauschiff, Baustellen, Notbau, Asteroidenbrecher, Lagerhaus | L | v8 |
| 9 | Akteure, Fraktionen, Kasse, Beziehungen, Persönlichkeit, Zukauf | L | v9 |
| 10 | Gate-Zugang, Kontrollpunkte, Sektorkontrolle, Regierungsstationen | L | v10 |
| 11 | Polizei, Fahndung, Piraten, abstrakte Begegnungen | L | v11 |
| 12 | Verbände, Befehle, Spielerflotte und Oberfläche | L | v12 |
| 13 | Forschung | M | v13 |
| – | Zurückgestellt: Kampf, Eroberung, Sprungantrieb, Tarnfeld, Besatzung, Blockadebrecher, Abwracker | – | – |

Die Versionsnummern sind Platzhalter; eine Phase erhöht die Version nur, wenn das Format wirklich bricht.

## Phase 1: Dienste-Rahmen

**Ziel:** Das Dock-Panel kennt Dienste statt fester Knöpfe. Verhalten bleibt gleich.
- `IStationService` (`Key`, `IsAvailable`, `Offers`, `Execute`), Registry `StationServices`, Event `ServicePerformed`.
- Der heutige Markt (`TradeActions.Buy/Sell/SellAll`) wird der Dienst `trade`; die JSON-Station-Typen bekommen `services: ["trade"]`, Standard für bestehende Typen.
- `DockPanel` (`godot/scripts/Ui/Panels.cs`) zeichnet generisch aus `Offers`.
- **Tests:** Dienst-Registry, `trade` als Dienst liefert dieselben Ergebnisse wie `TradeActions`; Selbsttest unverändert grün.
- **Abnahme:** Kein sichtbarer Unterschied im Spiel, aber neue Dienste lassen sich allein per JSON und Registrierung ergänzen.

## Phase 2: Fähigkeiten und Steuerer

**Ziel:** Systeme fragen `Capability`, nicht `ShipRole`. Verhalten bleibt gleich.
- `Capability` (Flags: `Cargo`, `Mining`, `Combat`, `Construction`, `Salvage`), `Ship.Has()`, abgeleitet aus der Rolle, bis es Modelle gibt.
- `Controller` (`Player`/`Ai`) ersetzt die `ShipRole.Player`-Abfragen in `TradeActions`, Ereignissen und `Materializer`.
- `TraderSystem` und `MinerSystem` prüfen die Fähigkeit; `FleetSystem`, `UpkeepSystem`, `EconomyStats`, `StationLifecycleSystem` und `SectorView` folgen.
- `Research` als Schiffsfähigkeit wird gestrichen, weil kein Dokument sie braucht.
- **Tests:** Fähigkeit ohne Mining lässt Miner leer; alle bestehenden Tests unverändert; `UniverseHash` gleich.
- Ein erster Versuch lag schon vor (Fähigkeiten aus Rolle abgeleitet); er wurde bewusst verworfen, bis die Planung steht, und kann neu begonnen werden.

## Phase 3: Slot- und Modulmodell, Warenform, Leistung

**Ziel:** Schiffe bestehen aus Klasse, Slots und Modulen. Einheitsmodelle bilden das heutige Verhalten nach.
- **Content:** Abschnitte `slotTypes`, `modules`, `shipClasses`, `shipModels` in `WorldDef`; `WorldBuilder` validiert (Schlüssel, Slotart, Größe, Leistungsbilanz, Frachtform) und wirft `WorldFormatException`.
- **Warenform:** `goods[].form` (`solid`/`liquid`); Frachtmodule mit `holds`; Universalraum (100 %) für migrierte Schiffe.
- **Leistung:** `ShipStats` rechnet `powerOutput`, `powerDraw`, `peakDraw`, Speicherbank (`powerStorage`) und leitet Tempo, Hülle, Laderaum, Laserleistung, Unterhalt und Fähigkeiten ab. `Ship.MaxHull` wird pro Schiff.
- **Save v4:** `ModelKey`, Slotliste und `Fit` je Schiff; Migration bildet bestehende Schiffe auf Einheitsmodelle ab; `UniverseHash` erweitert.
- **Heutige Pauschalwerte** (Fracht 100, Hülle 100, Tempo) kommen aus den Einheitsmodellen. `LaserShot.Roll` erhält die Laserstufe (`chunkChanceMult`, `chunkSizeMult`).
- **Tests:** Ungültige Fits werden abgelehnt; Einheitsmodell reproduziert Verhalten (Hash-Vergleich zu vorher mit denselben Seeds); Migration v3→v4.
- **Abnahme:** Balance-Lauf liefert dieselben Kennzahlen wie vor der Phase.

## Phase 4: Treibstoff

**Ziel:** Treibstoff ist eine echte Ware; Reparatur und Tanken kosten.
- **Waren und Stationen:** `propellant` (Flüssigstoff), `Treibstoffraffinerie` (3 Wasserstoff + 1 Energie → 4 Treibstoff), später `gas`, `ion_gas`, `fusion_fuel` mit Gasernte, Gasraffinerie und Anreicherungsanlage.
- **Schiff:** `Ship.Fuel` je Treibstoffware, Treibstofftank-Modul, Antrieb nennt zulässige Treibstoffe mit `efficiency`. `Travel.Start` rechnet echten Verbrauch statt der Credit-Pauschale (`Travel.cs`); Gate-Sprünge bleiben frei.
- **Dienste:** `refuel` und `repair` (verbraucht Hüllenplatten aus dem Stationslager), Rettungs- und Schleppdienst als Station mit Dienst für Liegenbleiber.
- **NPCs:** Tanken beim Reiseantritt an der besten erreichbaren Station; Routenbewertung (`TraderSystem`, `Travel.Estimate`) kennt Spritkosten.
- **Save v5:** `Fuel`, Tankgröße.
- **Risiken und Prüfung:** Wasserstoff konkurriert mit dem Kraftwerk; Balance-Lauf auf Energiemangel in der Startwelt prüfen. Liegenbleiber dürfen die Simulation nicht festfahren (Notkauf, Schlepp, Rückflug).
- **Tests:** Verbrauch, Tanken, Nachschub, leerer Tank, Rettungsdienst, Determinismus, Migration v4→v5.

## Phase 5: Module als Waren, Fabriken, Ausrüster, Schiffskauf

**Ziel:** Der Spieler kann Schiffe und Module kaufen und umrüsten; NPCs nutzen dieselben Regeln.
- **Modul-Waren:** Jedes Modul bekommt automatisch eine Ware in `goods`; sein `cost` wird das Rezept.
- **Drei Fabriken:** Antriebs- und Reaktorwerk, Waffen- und Schildwerk, Systemwerk, Stufe nach Stationsgröße (klein Mk1, mittel Mk2, Hub Mk3). Zuerst erster Modulsatz: Reaktor, Speicherbank, Triebwerk, Tank, Frachtmodule, Laser, Traktor, Schild, Panzerung.
- **Bausatzstufen:** `ship_kit` in `ship_kit_light/medium/heavy`; `EconomyRules.ShipKitGoodKey` wird je Klasse gewählt; `FleetSystem` und Werftrezepte angepasst.
- **Dienste:** `outfitter` (Module ein- und ausbauen), `shipdealer` (`BuyShip`), Handelsstation als neuer Typ.
- **Startpakete** (`player.startOptions`): Schürfer, Händler, Generalist.
- **Save v6:** Module als Waren sind Content; Spieler-Fit bleibt im Schiff.
- **Tests:** Rezepte stimmen mit `cost`, Werft verbraucht Bausatz und Zusatzmaterial, Spielerkauf, Umrüstung ändert `ShipStats`.
- **Balance:** Neue Nachfrage nach Titan und Platin; `tools/SpaceSim.Balance` auf Preisentgleisung prüfen.

## Phase 6: Multirole

**Ziel:** Schiffe wählen ihre Aufgabe nach Fähigkeit und Gewinn.
- `JobAssignmentSystem` wählt für untätige Schiffe die beste Aufgabe unter den Fähigkeiten (vorhandenes Scoring), mit Mindestdauer und Hysterese.
- `FleetSystem` zählt Kapazität statt Schiffe; Herdenschutz (Crowd-Penalty) für alle Aufgaben.
- Erste Multirole-Modelle (*Prospector*, *Tinker*) und der Rest der Klassen aus SCHIFFE.md (ohne Kampfklassen).
- **Tests:** Aufgabenwechsel flackert nicht; Generalist ist schlechter als Spezialist; Balance-Lauf auf Verdrängung.

## Phase 7: Güteklassen und Upgrades

- **Güteklassen** beim Schiffskauf (Standard 70 %, Verbessert 20 %, Elite 8 %, Meisterwerk 2 %), Mehrkosten kommen aus dem Werftlager; Startwelt-Schiffe würfeln ohne Material.
- **Upgrades:** Dienst `upgrade` mit Stufenchancen und Mitleidszähler; `UpgradeSystem` für reiche NPC-Schiffe; Slot-Upgrade selten und je Klasse begrenzt.
- **Save v7:** Bonusslots in der Slotliste, Mitleidszähler.
- **Tests:** Wahrscheinlichkeiten über viele Würfe, Determinismus, Material wird verbraucht.

## Phase 8: Bau, Brecher, Lagerhaus

- **Bauschiff** (Fähigkeit `Construction`) und `BuildSite`; `ConstructionSystem`; Spieler liefert per freiem Handel.
- **Notbau** (zustandsabhängig, nur fehlende Materialien, Kritikalität, Bremse); `Critical` abgeleitet (STATIONEN.md).
- **Zyklenfreie Baukosten** als Content-Test; Startbestand an Bauschiffen je Fraktion.
- **Asteroidenbrecher:** Spaltlaser, Fragmente mit Limit je Gürtel, Event `AsteroidSplit`.
- **Lagerhaus** als Station mit Dienst für Baustellenlager.
- **Save v8:** Baustellenliste, Fragment-Ids.
- **Balance:** Dauerhafte Nachfrage nach Stahl und Hüllenplatten; Anteil Notbau messen.

## Phase 9: Akteure und Fraktionen

- **Akteur/Fraktion:** Akteur als Wirtschaftssubjekt, Fraktion als Akteur mit Politik; `Ship.Owner` statt `Ship.Faction`; Mitgliedschaft; Migration der bestehenden Fraktionen.
- **Größe und Größenklassen**, Zukauf mit Erst- und Zweitzugriff (gewichtete Ziehung), Einzelschiffe träge anlegen, Sammelgruppe „Freie“.
- **Kasse** (`Treasury`), sparsame Beziehungen mit Vorgabewerten, Persönlichkeitswerte (Aggression, Gier, Kooperation, Gesetzestreue).
- **Spieler** als Einzelakteur.
- **Save v9:** Akteure, Mitgliedschaft, Kasse, Persönlichkeit.
- **Prüfung:** Geldmenge bleibt erklärbar (`EconomyStats`), keine Beziehungs-Explosion.

## Phase 10: Gate-Zugang, Kontrollpunkte, Sektorkontrolle

- **Gate-Zustände** (offen, eingeschränkt, geschlossen, zerstört) und Politik je Fraktion; `FindRoute` mit Mobilitätsprofil (Cache nach Profil).
- **Sperrstufen 0–5**, Richtung, Mindestdauer und Hysterese, Hauptroute je Region.
- **Kontrollpunkte** (`IAccessCheck`): Politik, Ware, Entdeckung.
- **Sektorkontrolle:** Einflussberechnung, abgeleitetes `Sector.Owner`, Stufen Präsenz bis Kontrolle, „Umkämpft“.
- **Regierungsstationen:** Hauptquartier (Hub), Firmensitz, Sektor-HQ, Büro, Polizeiwache; `Government`, `BankruptcyProtected`, `Critical` etc. (Aufschlüsselung von `Essential`).
- **Save v10:** Gate-Zustand und Politik, Einfluss.
- **Prüfung:** Balance meldet zerfallende Karte oder isolierte Sektoren.

## Phase 11: Polizei, Fahndung, Piraten

- **Lagekarte** (Bedrohung, Sicherheit) zuerst nur als Beobachtung (Statistik, Anzeige).
- **`SecuritySystem`:** Zuweisung von Polizeischiffen nach Bedrohung und Wert; Heimatbasis; Unterhalt aus der Kasse.
- **Fahndungsniveau** je Hoheitsgebiet mit Verfall; Verbrechen und Folgen; Kopfgeld; Kopfgeldbüro und Gericht.
- **Piraten** als Gegenfraktion; Überfälle auf unbewachte Routen.
- **`IEncounterResolver`:** abstrakte Auflösung nach Stärke, später durch echten Kampf ersetzbar.
- **Save v11:** Fahndung, Lagekarte, Piratenzustand.
- **Prüfung:** Verlustquote, Polizeianteil am Haushalt, Rechenzeit der Scans.

## Phase 12: Verbände, Befehle, Spielerflotte

- **`Squadron`** (Anführer, höchstens 8 Mitglieder, Formation nur Darstellung), Heimatbasis, Befehlskapazität nach Größe.
- **`IOrderExecutor`** pro Befehlstyp: `GoTo`, `Hold`, `Patrol`, `Guard`, `Escort`, `Follow`, `Trade`, `Mine`, `Build`, `Return` und weitere; Ebenen Notfall > Befehl > Automatik; Verhaltensregeln.
- **Spielerflotte:** Flottenliste und Befehlsmenü (höchstens drei Schritte), Automatikaufträge mit Grenzen.
- **`OrderSave`** erweitern (heute drei Ganzzahlen).
- **Save v12:** Verbände, Befehle, Heimatbasis.

## Phase 13: Forschung

- `research`-Sektion, `ResearchSystem`, Freischaltungen über `IsUnlocked`, Besitzer ist der Akteur mit Forschungsstation (Budget vom HQ). Details in [FORSCHUNG.md](FORSCHUNG.md).

## Zurückgestellt

| Thema | Grund |
|---|---|
| Kampfsystem (echt) | groß; der abstrakte Auflöser in Phase 11 genügt zunächst |
| Eroberung (`allowStationCapture`) | abgeschaltet, erst nach Kampf |
| Sprungantrieb, Tarnfeld | Referenz und Planung; brauchen Gate-Zugang und Fahndung |
| Besatzung und Lebenserhaltung für Schiffe | sprengt den Umfang |
| Blockadebrecher, Abwracker, Fregatten, Schlachtschiffe | hängen an Kampf, Politik und Wracks |
| Android-Test auf einem Gerät | unabhängig; sobald die .NET-Exporttemplates installiert sind |

## Reihenfolge und Abhängigkeiten

```text
1 Dienste ──► 4 Treibstoff/Reparatur ──► 5 Module, Fabriken, Schiffskauf ──► 6 Multirole ──► 7 Güte/Upgrades
2 Fähigkeiten ──► 3 Slots/Module ──┘                                         │
                                                                            ▼
                              8 Bau/Brecher ──► 9 Akteure ──► 10 Gate/Sektor ──► 11 Polizei ──► 12 Verbände ──► 13 Forschung
```

- Die Phasen 1 bis 7 sind die **Schiffs- und Handelskette** und sollten ohne Unterbrechung nacheinander laufen. Danach ist das Spiel für den Spieler deutlich tiefer (Schiffskauf, Umrüstung, Treibstoff).
- Phase 8 kann vor Phase 9 laufen, weil Bauschiffe nur Fähigkeiten und Modelle brauchen.
- Ab Phase 9 greift die Politik. Phase 10 vor 11, weil Polizei die Kontrollpunkte braucht; Phase 12 nutzt sie für Patrouille und Eskorte.

## Offene Entscheidungen, die vor dem Start einer Phase geklärt sein müssen

- **Phase 3:** Genaue Slotzahl je Klasse und die Zahlen der Einheitsmodelle (damit der Hash-Vergleich stimmt).
- **Phase 4:** Wie viel Treibstoff ein Gate-freier Sektorflug typischerweise kostet; Tankgröße der Einheitsmodelle.
- **Phase 5:** Preise und Rezepte der ersten Modulreihe, Verteilung der drei Fabriken in der Startwelt.
- **Phase 9:** Ob Einzelschiffe sofort oder träge Akteure werden (Vorschlag: träge); Beitritt des Spielers zu Fraktionen.
- **Phase 10:** Sperren je Sektor oder je Gate (Vorschlag: Sektor mit Gate-Ausnahmen).
- **Jede Phase:** Nach dem Lauf des Balance-Tools entscheiden, ob Startwerte angepasst werden.
