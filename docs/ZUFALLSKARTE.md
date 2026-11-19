# Zufallsgalaxie (Konzept, noch nicht umgesetzt)

Stand 2026-11-19. Entscheidung des Nutzers: als Dokument festhalten, **nicht umsetzen**.

## Heute
Die Karte ist fest in `src/SpaceSim.Core/Content/default-world.json`: 11 Sektoren, Tore mit Ringen und Hauptrouten, alle Stationen, Gürtel, Planeten, Gasfelder, Fraktionsgebiete. Jede Partie hat dieselbe Karte.

Zufällig je neuem Spiel (Seed des Spielstands): Asteroidenlage und -größen, Hintergrundfelsen, Fraktions- und Piratennamen samt Kürzel, Qualitätsstufen der Gründerschiffe, Auftragsangebote, Persönlichkeit aufsteigender Firmen, alle Wirtschafts- und Kampfabläufe.

Beim Entwurf einmal ausgewürfelt und seitdem fest: Sektorgrößen (Maßstab 3–8, Saat 44), Nebelsektoren (Saat 45).

## Ziel
Option „Zufallsgalaxie“ unter „Neues Spiel“. Die feste Karte bleibt Standard; Tests, Referenz-Hashes und Testszenarien (Startpakete mit `scenario`) laufen weiter auf ihr.

## Bausteine
1. **Generator** `Content/GalaxyGenerator` (Core, rein, eigener Seed-Zufall, nie `Universe.Rng`): erzeugt eine `WorldDef` wie aus JSON; `WorldBuilder` baut und prüft sie unverändert.
2. **Topologie:** Sektoren auf einem Raster (Größe wählbar, z. B. 9–16), Tore nur zwischen Nachbarn, kreuzungsfrei. Mehrere Ringe, 1–2 Sackgassen. Hauptrouten = Spannbaum. Sektormaßstab zufällig 3–8.
3. **Rohstoffe:** Gürtelklassen so verteilt, dass jedes Erz in ≥ 2 Sektoren liegt, die kein einzelnes Tor gemeinsam abschneidet; höchstens zwei Gürtelklassen je Sektor. Gasfelder in zufälligen Nebelsektoren (leer, Asteroiden oder Planet).
4. **Planeten:** 3–5, jeweils mit Exportstelle; mindestens zwei Kolonien.
5. **Fraktionen:** 3–4 Staaten/Gilden mit zusammenhängenden Gebieten (HQ, Sektor-HQ je Sektor, Polizeiwache), 1–3 herrenlose Grenzsektoren, 2 Piratengruppen mit Versteck in herrenlosen Sackgassen oder Grenzsektoren.
6. **Produktionsketten:** jede Ware mit Erzeuger in ≥ 2 Sektoren, Raffinerien nahe Erz, 2–3 Werften nicht hinter Sackgassen, je ≥ 1 Modulwerk jeder Art, Forschungsstationen, Treibstoff-, Gas- und Anreicherungsanlagen. Stationszahl und `maxStations`/Flottengrenzen proportional zur Sektorzahl.
7. **Platzierung:** dieselben Abstandsregeln wie heute (`StationPlacement`, `BeltPlacement`; Stationen > 101 von Gürtelzentren, > 50 von Toren/Stationen).
8. **Startflotten:** Händler, Schürfer, Gassammler, Bauschiffe, Polizei, Korsaren skaliert nach Sektorzahl.
9. **Startpakete:** die drei regulären Pakete (Jäger, Händler, Schürfer) suchen sich einen passenden Startsektor (Werft/Gürtel/Handelsdrehkreuz der Zufallskarte); Testszenarien nur auf der festen Karte.
10. **Speichern:** die erzeugte Welt steht vollständig im Spielstand (wie jede Welt heute); kein neues Format, Save bleibt v17.

## Abnahmeregeln (jede erzeugte Karte, sonst neu würfeln)
Die heutigen Kartenprüfungen aus `Phase21Tests` werden als allgemeine Prüffunktion herausgezogen und auf jede Zufallskarte angewandt:
- jedes Erz in ≥ 2 nicht gemeinsam abschneidbaren Sektoren
- jede Rezeptware wird erzeugt und abgenommen, Schlüsselketten in ≥ 2 Sektoren
- Werften außerhalb von Sackgassen, Hauptrouten als Spannbaum, nur Sackgassentore teilen die Karte
- Exportstellen nur an Planeten, alle Abstände eingehalten, Gürtel erreichen ihre Felsenzahl
- Kurzlauf (z. B. 5 000 Ticks) ohne Preisentartung

## Risiko und Messung
Die Wirtschaft ist auf die feste Karte geeicht. Vor der Freigabe: Balance-Läufe über viele Karten-Seeds (≥ 20 Karten × 20 000 Ticks, einige × 60 000), Kriterien wie bisher: „Preise im Zielbereich“, keine Pleitewellen, Export aller Planeten, Verlustrate < 1 %, alle Fraktionen handlungsfähig. Neuer Balance-Schalter `--galaxy <seed>`.

## Aufwand
Mittel bis groß, etwa eine Runde in der Größe von Phase 21 plus Messreihe über viele Seeds.
