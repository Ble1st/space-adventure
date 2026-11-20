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

## Schmuggler-KI (Ausbau Punkt 5)

**Entscheidungen.**
- **Eigene Rolle statt Handelsaufgabe mit Merker.** Schmuggler tragen die Rolle `Smuggler` (Fähigkeit Fracht), nicht die Aufgabe „trade“. Begründung: sie zählen
  dann weder in der Handelskapazität (`maxTraders`) noch im Kaufzug der Flotte (`FleetSystem`, Aufgabenwechsel), der `TraderSystem`-Pfad bleibt unverändert, und
  ihr Höchstbestand ist die Firmengrenze (3 je Firma). Pleite (Grund b) gilt wie für jedes Wirtschaftsschiff (`FleetSystem.Abandon`).
- **Wer:** gesetzlose Firmen = aufgestiegene unabhängige Akteure mit Gesetzestreue unter `blackMarketNpcLawfulness` (0,45). Freie Einzelschiffe nie.
- **Kauf** (`SmugglerSystem.Purchase`, alle 200 Ticks, höchstens einer): Modell `shadow` „Schatten“ (Klasse `blockade_runner` „Blockadebrecher“, M:
  Reaktor Mk2, Triebwerk M, 2 × Universalfrachtraum 60, Sprungantrieb, Tarnfeld Mk1, Laserkanone) über die gemeinsame Kaufregel an der nächsten Werft mit
  Material. Bezahlt aus dem freien Geld der Firma (Firmenkasse + Schiffskassen über `firmShipKeep`), es muss danach `smugglerBuyReserve` (3000) übrig sein;
  Startkapital `smugglerStartCredits` 3000; Abklingzeit `smugglerBuyCooldownTicks` (20000) je Firma (`Actor.LastSmugglerTick`, gespeichert, gehasht wenn gesetzt). Gekauft wird nur, wenn in den Verstecken
  Hehlerware im Basiswert von mindestens `smugglerMinFenceValue` (1500) liegt oder irgendwo eine Sperre gilt.
- **Fahrten** (`SmugglerSystem`, Intervall 5, Id-Reihenfolge): (a) **Hehlerware** — Einkauf im Versteck zu `smugglerFenceBuyShare` (0,6) × Basispreis
  (Netzrabatt; Geld an die Piratenkasse, nicht wenn sie am Deckel ist), Verkauf an den ehrlichen Abnehmer mit dem besten Gewinn je Reisezeit (Mindestgewinn
  `smugglerMinMargin` 25 % der Kosten nach Unterhalt); (b) **Embargo-Ware** — nur wenn eine Sperre gilt oder ein Tor zu ist: Kauf beim Erzeuger auf dem Torweg,
  Abnehmer in einem Sektor, den das Schiff auf dem Torweg nicht erreicht. Solche Abnehmer bewertet der Schmuggler mit `smugglerLockBonus` (+50 %), weil dort Ware
  fehlt. Der Schmuggler fliegt zum **Absprungpunkt** (Sektor in Antriebsreichweite, auf dem Torweg erreichbar; Stelle ohne Massenschatten) und springt mit der
  Regel des Spielers (`JumpDrive`: Laden, Politik der Sprungprüfung, Zoll, Abklingen). Rest, den niemand nimmt: nächster Abnehmer, sonst Hehler, sonst über Bord.
- **Tarnung:** mit Fracht und abgedockt schaltet er das Tarnfeld ein (Leistung vorausgesetzt), zum Laden des Sprungs aus. Tarnfeld Mk1 (1,5) schlägt Torsensor
  und Kaperer (1), nicht die Polizei (2); im Nebel kommt der Nebelbonus dazu.
- **Durchsuchung** (`SmuggleSearch`, eine Regel für KI-Schmuggler, Schmuggelaufträge und gesuchte Fahrgäste): eine Streife der Fraktion, der der Sektor gehört,
  in `smugglerSearchRange` (120) und das Schiff für ihren Sensor sichtbar → je Prüfung (alle 5 Ticks) Wurf `smugglerSearchChance` (0,06) aus `Universe.Rng` —
  gewürfelt wird nur, wenn wirklich eine Streife in Reichweite ist. Erwischt: Fracht beschlagnahmt, Strafe `smugglerFineShare` (0,5) × Basiswert aus der
  Schiffskasse an die Fraktion (`TreasuryIncome` „smuggling_fine“), Verbrechen „smuggling“ für den Besitzer (`Wanted.Report`), Ereignis `SmugglerCaught`.

## Schmuggel- und Forschungsaufträge (Ausbau Punkt 10)

- **Schmuggel** (`SmuggleMission`, Schalter `missionSmuggle`): angeboten am Hehler (Dienst „blackmarket“) und an Stationen gesetzloser Firmen, nur am Dock
  (Kanal „station“, nicht über Comms). Ziel: Station in einem Sektor mit Eintrittssperre (≥ 1, durchgesetzt); gibt es keine, ein Sektor einer Staatsfraktion
  unter Polizeischutz (Sicherheit ≥ 1). Versiegelte Ware 15 m³ (`Ship.MissionCargo`), kein Pfand. Belohnung `missionSmuggleBaseReward` 600 + 80 je Flugminute
  + 250 je Sperrstufe aus der Kasse des Auftraggebers (Piratengruppe/Firma). **Kontrolle:** (1) Tor in einen gesperrten Sektor — der Torsensor (1) findet ein
  ungetarntes Schiff sicher (Tarnfeld Mk1 genügt); (2) Streife in Reichweite (Regel oben, alle 20 Ticks; Tarnfeld Mk2 schlägt den Polizeisensor). Gefunden:
  Auftrag gescheitert (Ware beschlagnahmt), Verbrechen „smuggling“ mit Zusatzstrafe `missionSmuggleFine` 800 Cr, Ansehensverlust über `CrimeStanding`. Der
  Sprungantrieb umgeht die Torkontrolle. Ablieferung durch Andocken an der Zielstation.
- **Forschung** (`ResearchMission`, Schalter `missionResearch`): an Forschungsstationen, drei Aufgaben (Missions-RNG): zwei **Messpunkte** in einem Nebelsektor
  der Reihe nach anfliegen (60 m), eine **Wrackprobe** aus einem bestimmten Wrack bergen und an der Station andocken, oder eine **seltene Ware** des
  Stationsregals liefern (netto verkauft). Lohn: Geld (reserviert) + `missionResearchPoints` (300) FP auf das laufende Projekt der Fraktion (`Research.AddPoints`).
- Beide Arten sind an die Registry angehängt (die Würfe der älteren Arten im Missions-RNG bleiben gleich), Angebote ohne Hash; Marker, Vorschau, Dock- und
  Comms-Listen kommen aus der gemeinsamen Auftragsoberfläche. Ein angenommener Auftrag macht Zielstation/Zielsektor bekannt (`IntelSystem.RevealMission`).

**Gemessen** (Release, Standard-Seed; vorher = a168d34): 20 000 Ticks Hash 9D6D0AD5906A7040 → F497580E12102265, 60 000 426643BEE164E802 → F7D94A44DD7BD033,
300 000 D4D8541348E459C5 → A8D246F0497812D1; alle „Preise im Zielbereich“. Schmuggler: 20 000 2 gekauft / 21 Fahrten / 3 erwischt; 60 000 4 / 121 / 8
(Einkauf 19 244 Cr, Verkauf 24 436 Cr, Strafen 1 313 Cr); 300 000 14 gekauft, 3 am Ende, 520 Fahrten, 23 erwischt, 11 pleite (Grund b). Erste Fassung (800 Cr
Startkapital, 6000 Ticks Abklingzeit) kaufte 38 in 300 000 Ticks, 23 gingen pleite — daher 3000/20 000. Per Sprung: 0 in der Standardwelt (keine Sperre, kein
geschlossenes Tor); im Test mit geschlossenem Tor 17 springt der Schmuggler nach Aurelia. Export Konsumgüter/Luxus 60 000 Ticks, drei Seeds vorher → nachher:
Mittel 10 508 → 10 525 / 2 295 → 2 191 (Seed 1: 11 307 → 10 440 / 2 629 → 1 940; Seed 2: 10 255 → 9 794 / 2 014 → 2 066; Seed 3: 9 962 → 11 340 / 2 242 → 2 566)
— im Rauschen; die neuen Modulwaren verschieben die Waren-Ids. Angebote je 60 000 Ticks: Schmuggel 38, Forschung 63, Fahrgast 1 472.
