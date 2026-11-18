# Schwarzmarkt — Hehler im Piratenversteck

Offener Punkt aus KAMPF.md („Händler überfallen: Ja, mit Fahndung. Ein Schwarzmarkt kommt später.“) und STATIONEN.md („Schwarzmarkt / Piratenversteck: illegaler Markt, keine Zölle, keine Polizeikontrolle“). Die Entscheidungen unten wurden selbstständig getroffen (offene Liste abarbeiten), klein und passend zu den vorhandenen Systemen.

## Entscheidungen

1. **Wo:** nur im Piratenversteck (Stationstyp `pirate_hideout`, Dienst `blackmarket` in seiner Dienstliste). Das Versteck ist Größe M und war schon andockbar; es bekommt nur den Dienst. Keine Stationen in herrenlosen Sektoren (dort gibt es keine Händlerstationen; das Versteck in Abgrund ist der Schwarzmarkt der Schwarzen Klingen).
2. **Wer:** Spielerschiffe über den Dienst (aktives Schiff, Spielerkasse). Seit „NPCs beim Hehler“ (unten) auch gesetzlose KI-Händler und -Schürfer — nicht über den Dienst, sondern über eigene Regeln im Kern. Kein Zugang während eines Gefechts des Schiffs; ein ausgeschaltetes Versteck (Belagerung) hat keinen Markt.
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

## NPCs beim Hehler (Nutzerwunsch „Schwarzmarkt auch entsprechende NPCs mit schlechteren Charaktereigenschaften“)

Entscheidungen (selbstständig getroffen):

1. **Wer:** KI-Händler und KI-Schürfer, deren Besitzer ein ausdrücklicher Akteur mit Gesetzestreue unter `blackMarketNpcLawfulness` (0,45) ist. In der Standardwelt sind das die aufgestiegenen Einzelakteure (gewürfelt 0,55 ± 0,2, also 0,35–0,75: etwa ein Viertel liegt darunter). Freie Einzelschiffe („Freie Kapitäne“) haben keinen eigenen Charakter und gehen nie hin; Fraktionen sind gesetzestreu, Gesetzlose besitzen nur Kaperschiffe (deren Beute geht ohnehin zum Hehler). Der Persönlichkeitswurf wurde **nicht** geändert (keine Messung nötig, keine neue Zufallsziehung); die Schwelle 0,45 statt 0,35, weil bei 0,35 kein Einzelakteur darunter läge.
2. **Gier = Reichweite:** höchstens max(1, round(Gier × `blackMarketNpcMaxJumps` 4)) Sprünge bis zum Hehler (Einzelakteure: Gier 0,4–0,8 → 2–3 Sprünge). Nächster erreichbarer Hehler nach Sprüngen, dann Id; das Schiff muss dort andocken dürfen.
3. **(a) Restfracht verkaufen statt abwerfen:** ein gesetzloser Händler, dessen Restfracht nach `strandedCargoTicks` (3000) niemand annimmt, und ein gesetzloser Schürfer, dessen Rest kein Käufer nimmt, fahren ihn zum Hehler — wenn dessen freie Kasse (über `blackMarketReserve`) mindestens `blackMarketNpcMinSale` (20 Cr) dafür zahlt und der Erlös den Schiffsunterhalt der Fahrt übersteigt. Dort verkaufen sie alles zum Ankaufspreis (0,5 × min(Basis, bestes ehrliches Gebot)), so weit die Hehlerkasse reicht; der Rest bleibt beim Händler an Bord (neue Frist), beim Schürfer geht er wie bisher über Bord.
4. **(b) Hehlerware kaufen (selten):** ein untätiger gesetzloser Händler mit leerem Laderaum (ohne Automatikgrenzen) prüft vor der normalen Planung den nächsten Hehler: lohnt eine Runde Hehler → ehrlicher Abnehmer (Menge nach Lager, Laderaum, Geld, Annahme des Abnehmers abzüglich dessen, was andere Händler schon hinfahren, und seiner Kasse) mit Gewinn ≥ `blackMarketNpcBuyMargin` (25 %) der Kosten nach Unterhalt? Bei Ankunft kauft er nur, wenn der Abnehmer dann noch mehr als den Hehlerpreis zahlt. Die Hehlerware kostet 1,5 × Basispreis — das trägt nur bei knapper Ware (Preis nahe dem oberen Deckel).
5. **Piratenkasse:** Verkäufe zahlt die Piratenkasse nur über der Rücklage (2000 Cr) — sie kann die Piraten nicht handlungsunfähig machen. Käufe bringen Geld in die Piratenkasse, aber nur bis `pirateTreasuryCap` (10 000): an ihrem Deckel kauft kein NPC beim Hehler. Buchungen wie beim Spieler `TreasuryUpkeep`/`TreasuryIncome` „blackmarket“, dazu das Ereignis `BlackMarketNpcTrade` je Ware.
6. **Keine Preise, keine Fahndung:** der Hehler hat keinen Markteintrag; nichts davon bewegt Marktpreise. Hehlerei der NPCs hat keine Folgen bei der Polizei (wie beim Spieler: das Verbrechen ist der Überfall).
7. **Deterministisch:** kein Zufall, Schiffe und Stationen in Id-Reihenfolge; die Aufträge sind gewöhnliche `TradeOrder`/`MineOrder` mit dem Versteck als Kauf-/Verkaufs-/Lieferstation (gespeichert wie bisher).
8. **Schalter:** `rules.blackMarketNpc` (Code-Standard aus, Standardwelt an); wirkt nur zusammen mit `blackMarket`. Aus = Standardwelt bitgleich (20000 Ticks F5F1965838C14B59, 60000 Ticks 5492CF79367BE8A7).

Gemessen (Standardwelt, Balance-Werkzeug, aus → an):

| | 20000 Ticks | 60000 Ticks |
|---|---|---|
| Urteil | Preise im Zielbereich (beide) | Preise im Zielbereich (beide) |
| NPC-Verkäufe beim Hehler | 0 → 44 (1220 E., 9007 Cr) | 0 → 82 (2128 E., 13 939 Cr) |
| NPC-Käufe beim Hehler | 0 → 5 (58 E., 4199 Cr) | 0 → 5 (58 E., 4199 Cr) |
| gesetzlose Händler/Schürfer | 9 | 9 |
| Verlustquote | 0,32 % → 0,34 % | 0,21 % → 0,19 % |
| Pleiten/Abgänge | keine → keine | Polizei zerstört 1 → Pirat zerstört 1 (keine Pleiten) |
| Export Nahrung/Konsumgüter/Luxus | 23 555/2387/284 → 24 282/2576/305 | 85 916/11 892/1799 → 90 830/12 935/2501 |
| Piratenkassen am Ende (Schakale/Korsaren) | 9919/9692 → 5597/9231 | 9570/7967 → 1685/8214 (Schakale 3 → 2 Kaperschiffe) |
| HINWEIS-Zeilen | 9 → 4 | 4 → 3 |
| Hash (an) | 20000: C46A46224CAEBBA7 | 60000: 52DE1504E9D47AE1 |

Die Piratenkassen sinken (die Hehler zahlen Restfracht aus; die Hehlerkasse zahlt nur über 2000 Cr, Kassenstände darunter kommen von Unterhalt und Neubauten), beide Gruppen bleiben jagdfähig. Im Flottentest (`FlottenTests`) verschiebt der Hehler den Pfad: zwei gesetzestreue NPC-Händler gehen im 2er-Lauf in der Spirale dünner Ladungen pleite (Schalter aus: einer im 8er-Lauf); die Grenze dort ist jetzt zwei. Die übrigen Unterschiede sind Pfadrauschen (die Welt läuft ab dem ersten Hehlerverkauf einen anderen Weg).

Umsetzung: `Simulation/BlackMarketNpc.cs` (Regeln, Hehlerverkauf/-kauf), Haken in `TraderSystem` (Restfracht, Kaufrunde, Ankunft) und `MinerSystem` (Rest ohne Käufer, Ankunft), Ereignis `BlackMarketNpcTrade`, Balance-Zeile „Schwarzmarkt NPC“ und Option `--blackmarketnpc on|off`. Tests `tests/SpaceSim.Tests/SchwarzmarktNpcTests.cs`.
