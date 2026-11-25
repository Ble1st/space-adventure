# Bars in Stationen

Lücke gegenüber Interstellar Pilot 2, eigener Entwurf (kein Code, keine Grafik übernommen). Schalter `rules.bars` (Code aus, Standardwelt an;
`Model/EconomyRules.Bars.cs`). Reine Spieleraktion: ohne Spieler entsteht kein Zustand und kein Zufall, die NPC-Welt bleibt bitgleich
(Balance 20000 Ticks: 9987D125373CCE46 vor und nach dem Einbau).

## Wo

Dienst `bar` (`Bars/BarService.cs`) an fertigen, andockbaren Stationen, deren Typ in `rules.barStationTypes` steht (Code: leer; Standardwelt: Kolonie, Handelsstation,
Planetenhandel, Werft, Sektor-HQ, Hauptquartier, Polizeiwache). Die Typliste steht in den Regeln, nicht in der Dienstliste der Stationstypen — so
ändern sich weder Weltinhalt noch Migrationsbasis; `StationServices.ForStation` hängt den Dienst an wie „missions“. Im Krieg gilt wie überall die
Dienstsperre. Dock-Kachel „Bar“ (`Ui/Screens/BarScreen.cs`).

## Rubbellos

- Preis `barTicketPrice` 50 Cr: Spielerkasse → Stationskasse. Höchstens `barTicketsPerDay` 5 Lose je Station und Spieltag; ein offenes Los zur Zeit
  (es gehört zur Station, an der es gekauft wurde; verschwindet die Station, verfällt es). Verkauf nur, solange die Stationskasse mindestens
  `barMinStationCash` 1000 Cr hält.
- Neun Felder, sechs Symbole (Komet, Stern, Planet, Ring, Krone, Galaxie); drei gleiche gewinnen die Stufe des Symbols: 25 / 50 / 100 / 250 / 1000 /
  5000 Cr mit Gewichten 2000 / 1200 / 800 / 300 / 80 / 6 von 10000 (Nieten 56,1 %). **Erwartungswert 37,5 Cr bei 50 Cr Preis = 25 % Hausvorteil**
  (`Scratchcard.ExpectedValue`). Eine Niete hat kein Symbol dreimal, ein Gewinn genau einen Dreier.
- Der Ausgang wird **beim Kauf** gezogen — aus einem eigenen Zufall aus Welt-Seed, Tick, Station und Loszähler der Station (`Scratchcard.Rng`), nie
  aus `Universe.Rng`. Aufdecken ändert nur die Bitmaske der offenen Felder: die Reihenfolge ist egal, Speichern/Laden mitten im Los ändert nichts,
  erneutes Laden im selben Tick zieht dasselbe Los.
- **Auszahlung** beim Abholen (alle Felder offen): der Gewinn aus der Stationskasse, **höchstens das, was sie hat** — der Rest verfällt, und die Meldung
  sagt es („die Bar zahlt nur … von … Cr“). Kein Ersatz aus anderen Kassen (sonst würde Geld entstehen); die Mindestkasse beim Verkauf macht den Fall
  selten.
- Schlüssel streng: `ticket`, `reveal:<0..8>`, `reveal:all`, `claim`; Angebotsmenge > 0 ⇔ Ausführung gelingt; `ServicePerformed` nur bei Erfolg.

## Gerüchte

Aus dem Universum berechnet (`Bars.Rumours`, rein, nur über Orte, die der Spieler kennt): die knappste Ware mit Abnehmer (höchstes Preis/Basis-
Verhältnis ab 1,4 bei einer Station, die sie nimmt), ein laufendes Universumsereignis, der Jagdsektor einer lauernden Piratengruppe. Bei beschränktem
Wissen zusätzlich die nächste unbekannte Station (wenigste Sprünge): für `barRumourFee` 40 Cr (an die Stationskasse, Schlüssel `rumour:<StationId>`)
wird sie bekannt (Intel-Ereignis „bar“).

## Söldner

Keine eigene Mannschaft: gehört die Station einem Söldneranbieter (Gilden-Fraktion oder Unabhängiger), bietet die Bar `merc:<Tage>` als Abkürzung zur
Comms-Regel `Mercenaries.Hire` an (gleiche Bedingungen und Kosten).

## Speichern

Additiv (`SaveGame.Bar`, `Bars/BarCodec.cs`): Losverkauf je Station und das offene Los. Gehasht nur mit Spielerpolitik und nur, wenn vorhanden.
Tests `BarTests`, Selbsttest-Stufe 169 (`SelfTest.Bar.cs`), ui-check-Zustände `dock-bar`, `dock-bar-los`, `dock-bar-aufgedeckt`.
