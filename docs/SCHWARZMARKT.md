# Schwarzmarkt — Hehler im Piratenversteck

Offener Punkt aus KAMPF.md („Händler überfallen: Ja, mit Fahndung. Ein Schwarzmarkt kommt später.“) und STATIONEN.md („Schwarzmarkt / Piratenversteck: illegaler Markt, keine Zölle, keine Polizeikontrolle“). Die Entscheidungen unten wurden selbstständig getroffen (offene Liste abarbeiten), klein und passend zu den vorhandenen Systemen.

## Entscheidungen

1. **Wo:** nur im Piratenversteck (Stationstyp `pirate_hideout`, Dienst `blackmarket` in seiner Dienstliste). Das Versteck ist Größe M und war schon andockbar; es bekommt nur den Dienst. Keine Stationen in herrenlosen Sektoren (dort gibt es keine Händlerstationen; das Versteck in Abgrund ist der Schwarzmarkt der Schwarzen Klingen).
2. **Wer:** nur Spielerschiffe (aktives Schiff, Spielerkasse). NPCs handeln dort nie — das NPC-Verhalten bleibt unverändert. Kein Zugang während eines Gefechts des Schiffs; ein ausgeschaltetes Versteck (Belagerung) hat keinen Markt.
3. **Fahndung:** spielt keine Rolle. Der Hehler fragt weder nach Herkunft noch nach Fahndungsstufe; das ist der Ausweg für einen gesuchten Spieler (Handelsverweigerung ab Stufe 3 an ehrlichen Stationen) und für Schmuggelware (`rules.contraband`). Der Handel beim Hehler selbst erzeugt **keine** Fahndung: das Verbrechen ist der Überfall, nicht der Verkauf; die Polizei kontrolliert im Versteck nicht.
4. **Welche Ware:** der Hehler kauft **jede** Ware an Bord. Beute wird nicht markiert (Ware ist austauschbar wie überall im Spiel); der Punkt ist, dass der Hehler auch dann kauft, wenn sonst niemand mehr kauft.
5. **Preise (fest, ohne Markt):**
   - Ankauf (Spieler verkauft): `blackMarketBuyShare` (0,5) × min(Basispreis, bestes ehrliches Gebot) — höchstens die Hälfte des Basispreises, bei Überangebot weniger.
   - Verkauf der Hehlerware (Spieler kauft): `blackMarketSellFactor` (1,5) × Basispreis, fest.
   - Der Hehler hat keinen Markteintrag; KI-Händler sehen ihn nie, sein Handel bewegt keine Preise.
6. **Geld:** der Hehler zahlt Ankäufe aus der **Piratenkasse** des Verstecksbesitzers, aber nur aus Geld über `blackMarketReserve` (2000 Cr) — er kann die Piraten nicht handlungsunfähig machen. Verkäufe der Hehlerware gehen in die Piratenkasse (ungedeckelt: das Geld kommt vom Spieler, nicht aus dem Nichts). Buchungen als `TreasuryUpkeep`/`TreasuryIncome` mit Zweck `blackmarket` (Kassen bleiben erklärbar).
7. **Lager des Hehlers** (`Station.BlackStock`, gespeichert): was der Spieler verkauft und die **Beute der Kaperer** — überall, wo bisher die Hehlerei Geld in die Piratenkasse zahlte (Aufgabe im Gefecht, Wrackfracht, Überfall ohne Rundengefecht, Aufgabe des Spielers), landet die erbeutete Ware zusätzlich im Lager des ersten Verstecks der Gruppe. Je Ware höchstens `blackMarketStockMax` (40) Einheiten, der Rest verfällt. Die Ablage zieht keinen Zufall und bewegt kein Geld — das NPC-Verhalten bleibt bitgleich (gemessen: Balance-Ausgabe identisch bis auf den Hash).
8. **Kein Kreislauf:** jeder Ankaufspreis ist ≤ 0,5 × Basispreis, jeder Verkaufspreis = 1,5 × Basispreis, zu jedem Zeitpunkt. Kauf → Rückverkauf und Verkauf → Rückkauf beim Hehler verlieren immer (mindestens zwei Drittel). Der Rückkaufdeckel (`PlayerLedger`) hängt an Markteinträgen der Station und wird hier nicht gebraucht; der Abstand ist strukturell. Kauf beim Hehler und Verkauf an einer ehrlichen Station (oder umgekehrt) ist gewöhnliche Arbitrage zwischen zwei Stationen (erlaubt).
9. **Schalter:** `rules.blackMarket` (Code-Standard aus, Standardwelt an). Aus = kein Dienst, keine Beute im Lager — Phase-28/Runde-44-Hashes bleiben gleich.

## Umsetzung

- Core: `Trading/BlackMarket.cs` (Regeln, Preise, Lager, Anzeige), `Services/BlackMarketService.cs` (Schlüssel `sell:<GoodId>`, `buy:<GoodId>`), `Station.BlackStock` (additiv gespeichert, im Hash nur wenn nicht leer).
- Ansicht: Dock-Kachel „Schwarzmarkt“ (`BlackMarketScreen`), Station-Info-Zeile „Schwarzmarkt: …“.
- Tests: `tests/SpaceSim.Tests/SchwarzmarktTests.cs`; Selbsttest-Stufe 128.
