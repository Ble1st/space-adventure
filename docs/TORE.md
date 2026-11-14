# Angreifbare Jumpgates (Konzept; umgesetzt in Phase 31, Variante mittel — siehe Checkliste am Ende)

Stand: Phase 28 + Runde 12. Nur Konzept: Code, Weltdatei und Tests sind unverändert. Die Zahlen stammen aus
`src/SpaceSim.Core/Content/default-world.json`, `Model/EconomyRules.cs` und den Langläufen in
`/home/gerd/space-adventure-langlaeufe/elf-sektoren/` (E–I). Zeit: 4 Ticks/s (`SimClock`), 1 Spieltag = `DayTicks` 2400 = 10 min.

**Ist-Stand in einem Satz:** Ein Tor (`Model/Sector.cs` `Gate`) hat heute nur `State` (Open/Restricted/Closed/Destroyed) und
`MainRoute`, aber keine Hülle, keinen Schild und keine Geschütze. Geschlossen oder zerstört wird es nur über
`GateAccess.SetState` (beide Hälften, `GateStateChanged`, `TouchAccess`, `AccessEnforcement.Revalidate`); nichts in der
Simulation tut das von sich aus. Die Ansicht kennt die Zustände schon (`World/GateModels`: Feld-Shader, `gate_destroyed.glb`
mit Glut-Shader).

## 1. Ziel und Spielgefühl

**Wozu:** (a) *Blockade als Kriegsziel* – eine Fraktion im Krieg schneidet eine Nachschublinie des Gegners ab, statt nur
Stationen zu belagern; (b) *Schutzaufgabe* – Polizei/Marine/Spieler verteidigen ein Tor, die Zollstation am Tor wird ein Ort;
(c) *Risiko für den Spieler* – Routen können sich ändern, der Umweg kostet Zeit; (d) der Spieler kann als Verbrecher eine Route
sperren (mit Folgen).

**Was unbedingt vermieden werden muss:**

| Gefahr | Beleg aus dem Ist-Stand | Gegenmittel (Abschnitt) |
|---|---|---|
| Karte zerfällt dauerhaft, Teile verhungern | Lauf F (Aurelia dauerhaft zu): Aurelia trade 0/1,6, 12 Panik-Ereignisse „nicht herstellbar“; Lauf I (Delta ganz abgeschnitten, mehrere Tore): Delta-Teil trade 2,2/4,7, 9 Händlerpleiten | nie kartenteilend durch die KI; Wiederanlauf nach Frist (4, 6) |
| Spieler sperrt sich aus | Spieler kann heute schon in Aurelia/Abgrund (Sackgassen 17/27) landen | Wiederanlauf kartenteilender Tore vor der Panikfrist (4) |
| Fraktion wird handlungsunfähig (Nutzerregel) | `FactionViability`: Rücklage, Mindestpolizei, Sitz | Reparatur nur aus `Spendable`; Notregel ohne Kasse (4) |
| Gefechte an Toren verschieben die Piratenwirtschaft | Kaperer lauern nahe Toren (`PirateSystem`/`LurkCommand`); Stationsgeschütze haben in Phase 28 die zerstörten Kaperer von 4 auf 11 je 300 000 Ticks erhöht | keine eigenen Torgeschütze in Schritt 1 (2) |

Gute Nachricht aus den Läufen: Ein geschlossenes **Ringtor** ist harmlos (Lauf H, Beta–Gamma zu: 0 Panik, „Preise im
Zielbereich“, 1 Händlerpleite), ein kurz geschlossenes Sackgassentor auch (Lauf E, Aurelia 10 h zu: erholt sich; Phase-20-Test
„kurze Schließung löst nichts aus“, `PanicDelayTicks` 1000). Nur die Tore 17/18 (Gamma–Aurelia) und 27/28 (Delta–Abgrund)
teilen die Karte allein (`MapParts.SplitsWhenClosed`); Kombinationen mehrerer Ringtore können es auch (Lauf I).

## 2. Werte

### Vergleich: vorhandene Ziele und Angreifer

| Ziel (`stationTypes[].defense`) | Hülle | Schild | Aufl./s | Panzer | PV | Geschütze |
|---|---|---|---|---|---|---|
| ohne Abschnitt (`StationBaseHull`) | 800 | – | – | – | – | – |
| Sektor-HQ | 1300 | 250 | 3 | 60 | 0,2 | 3 Laser S, Massetreiber |
| Polizeiwache | 1800 | 300 | 3 | 60 | 0,25 | 2 Laser S, Massetreiber |
| Werft | 2000 | 200 | 2 | 60 | 0,2 | 2 Laser S |
| Hauptquartier | 3000 | 400 | 4 | 80 | 0,3 | 3 Laser S, Laser M, 2 Raketen |

Nominaler Schaden je Sekunde (Σ Schaden × Genauigkeit ÷ Nachladezeit, vor Reichweite/Panzer):
Falke 5,5 · Korsar 5,5 · Wächter 8,4 · Bollwerk (Fregatte) 11,8 · Zerstörer 16,8 · Leviathan (Schlachtkreuzer) 39,8.

### Vorschlag: zwei Torklassen

| Klasse | Hülle | Schild | Aufl./s | Panzer | PV | Geschütze | Begründung |
|---|---|---|---|---|---|---|---|
| `side` (Nebentor, `mainRoute` false: 9/10, 15/16, 21/22, 25/26) | 2000 | 300 | 3 | 70 | 0,2 | keine | ≈ Werft/Polizeiwache |
| `main` (Hauptroute, übrige 10 Paare) | 4000 | 500 | 5 | 100 | 0,35 | keine (später 2 Laser S) | über dem HQ: nur ein Kriegsverband bricht es |

Panzer 100 frisst einen Laser-S-Treffer (7 − 100 × 0,05 × 1,5 = 0): kleine Schiffe richten gegen Haupttore fast nur mit
Massetreibern etwas aus – gewollt.

### Rechenbeispiele (Zeit bis „außer Betrieb“, ohne Gegenwehr)

Annahmen nach der vorhandenen Trefferregel (`Combat/Hit.cs`): Feuer aus 0,7 × kürzester Waffenreichweite
(`CombatPreferredRange`), Trefferchance Genauigkeit × (1 − 0,5 × (d/Reichweite)²), Torsignatur wie Station
(`StationCombat.Signature` 4, steht still), Schild × Artfaktor (Energie 1,25, kinetisch 0,6, Sprengstoff 0,8), Panzerabzug je
Treffer Panzer × `ArmorPerHitShare` 0,05 × Artfaktor, Raketen × (1 − PV), kein Schildaufladen (Pause `ShieldDelayTicks` nach
jedem Treffer).

| Angreifer | Nebentor (2000/300) | Haupttor (4000/500) | zum Vergleich HQ heute |
|---|---|---|---|
| Falke (Spieler-Jäger) | 16 min | 49 min | 28 min |
| Wächter (Polizei) | 12 min | 48 min | 23 min |
| 2 Korsaren | 8 min | 25 min | 14 min |
| 3 Bollwerk (Fregatten) | 1,9 min | 5,2 min | 3,3 min |
| 1 Zerstörer | 3,8 min | 9,5 min | 6,0 min |
| 1 Leviathan | 1,7 min | 4,6 min (≈ 1100 Ticks) | 2,9 min |
| Leviathan + 2 Zerstörer | 0,9 min | 2,3 min | – |

Fazit: Ein Haupttor hält einen Kriegsverband 2–10 Minuten auf – genug Reaktionsfenster für Polizei/Marine im Sektor
(`Reinforcements` alle `ReinforceTicks` 10), für einen Einzelspieler ohne Großkampfschiff praktisch unknackbar.

**Eigene Geschütze an der Zollstation:** Empfehlung *Datenfeld ja, Standardwelt zunächst leer.* Torgeschütze würden in jedem
Gefecht ihres Sektors feuern (`StationCombat.Fire`), also genau dort, wo Kaperer lauern – das verschiebt Verlustrate und
Piratenkassen, die mühsam kalibriert sind (Phase 24–28). Verteidigung zunächst über Polizei/Marine; später über die geplanten
Waffenstellungen (`docs/platforms/KONZEPT.md`: leichte Stellung „an Toren der Hauptrouten“, mittlere „Torsicherung“ – noch nicht
in der Weltdatei) oder nach einem Balance-Lauf 2 Laser S an Haupttoren.

## 3. Zustandsmodell

| Zustand | Bedingung | Durchflug / Routen | Maut | Ansicht |
|---|---|---|---|---|
| intakt | Hülle > 50 % | wie heute (`Open`/`Restricted` aus Sperrstufen) | wie heute | wie heute |
| beschädigt | 0 < Hülle ≤ 50 % | unverändert passierbar | unverändert | Funken/Glut an der Zollstation (Teile des Glut-Shaders) |
| außer Betrieb | Hülle 0 | `GateState.Closed` für alle → `FindRoute`, `MapParts`, `Revalidate` greifen ohne neuen Code | entfällt | vorhandener Look `Closed` (Feld aus) |
| zerstört (nur Variante groß) | außer Betrieb + zerstörbar + Regel an | `GateState.Destroyed` | entfällt | vorhandenes `gate_destroyed.glb` |

- **Laufende Reisen:** nichts Neues – `GateAccess.SetState` ruft `AccessEnforcement.Revalidate` (neu planen ab interpolierter
  Position oder anhalten, keine Geisteraufträge).
- **Wieder in Betrieb** ab Hülle ≥ `GateReenableShare` (Vorschlag 0,25, wie `StationReenableShare`), dann `SetState(Open)` +
  `SyncRestricted`.
- **Eine Torhälfte oder beide?** Empfehlung **gemeinsame Trefferpunkte je Paar** (Zustand am Tor mit der kleineren Id):
  `SetState` setzt ohnehin beide Hälften, Routen kennen kein „Einbahntor“, Angreifer von beiden Seiten treffen denselben Vorrat.
  Getrennte Werte brächten nur Sonderfälle (halb kaputtes Tor, einseitige Reparatur).
- **Abgrenzung zu „geschlossen“ von Hand:** `GateCondition.Disabled` unterscheidet Kampfausfall von administrativer Schließung
  (Tests, Balance-Läufe `--gate`, spätere Ereignisse); `SyncRestricted` und ein Öffnen von Hand dürfen ein ausgefallenes Tor
  nicht wieder öffnen.

## 4. Reparatur und Wiederaufbau

- **Wer:** die Fraktion(en), denen die beiden Endsektoren gehören (`Sector.Owner`). Im Krieg repariert nur die Seite, die nicht
  angreift. In der Standardwelt hat jedes der 14 Paare mindestens ein besessenes Ende (Nebelmark und Abgrund sind herrenlos, ihre
  Gegenseite nicht); für Welten mit beidseitig herrenlosen Toren gilt die Notregel.
- **Womit, wie schnell:** wie `StationRepairSystem` – `GateRepairPerDay` (Vorschlag 2000 Hülle/Tag; Stationen 600 wären bei
  4000 Hülle fast 7 Spieltage), Hüllenplatten 0,02 je Hüllenpunkt (`StationRepairPlatesPerHull`) zum Grundpreis ×
  `StationRepairMarkup` 1,5 aus der Kasse: Haupttor voll ≈ 80 Platten ≈ 12 000 Cr, Nebentor ≈ 6000 Cr (zum Vergleich `WarChest`
  15 000, Unionsrücklage ≈ 41 000). Nur aus `FactionViability.Spendable` – nie unter die Rücklage.
- **Notregel (Karte zerfällt nie dauerhaft):** nach `GateMaxOutageTicks` läuft ein ausgefallenes Tor mit 25 % Hülle von selbst
  wieder an, ohne Kasse („Torbehörde“: ausdrückliche Geldquelle wie die Notbeschaffung `FactionRestored`, Ereignis
  `GateRestored`). Vorschlag: **1000 Ticks für kartenteilende Tore** (= `PanicDelayTicks`, löst also keine Versorgungspanik und
  keine Sonderkapazität aus) und **4800 Ticks (2 Spieltage) für alle anderen**.
- **Wiederaufbau zerstörter Tore (nur Variante groß):** Baustelle am Torort über die vorhandene Baustellenmechanik
  (`BuildSite`, Stückliste aus `ConstructionMaterials`, Bauschiff „Polier“, Notbau als Rückfall) mit Baukosten z. B. 30 000
  (→ 15 000 Cr Material). Bis dahin gilt die Notregel trotzdem – ein zerstörtes Hauptroutentor ist nicht vorgesehen
  (Hauptrouten: „nicht zerstörbar, nur ausschaltbar“, sie bilden den Spannbaum aus Phase 21).

## 5. Wer greift an, wer verteidigt

| Angreifer | Empfehlung | Grenzen |
|---|---|---|
| KI-Fraktion im Krieg (`NavySystem`) | ja, als zweite Zielart neben `Wars.PickTarget` | Tor mit mindestens einem Ende beim Gegner; `MapParts.IfClosed(u, gate)` darf die Karte **nicht** teilen (berücksichtigt bereits ausgefallene Tore → deckt Kombinationen wie Lauf I ab); auch `MapConnectivity` (Profil Wirtschaft) bleibt zusammen; höchstens `GateMaxDown` (1) Tore weltweit außer Betrieb; höchstens eine Torblockade je Krieg; Wert = Verkehr (`Sector.Traffic`) des Gegners über das Tor; nach Frieden repariert der Besitzer zuerst diese Tore |
| Piraten | nein | Tore sind ihr Jagdrevier; eine Sperre nimmt ihnen die Beute |
| Spieler | ja, jedes Tor | schweres Verbrechen `assault_gate` (wie `assault_station`, Hitze 4,5) bei beiden Endfraktionen (`Wanted.ReportAssault`); ab Ausfall eines Tors einer Fraktion im Frieden: Beziehung −; Krieg gegen den Spieler gibt es nicht (Spielerfraktion fehlt) |

Verteidiger: Polizei und Kriegsschiffe der Endfraktionen im Sektor (wie `Siege.Reinforce`), Torgeschütze nur falls per Daten
gesetzt, später Waffenstellungen. **Hinweis:** In der Standardwelt bricht kein Krieg aus (Beziehungen +0,12…+0,25,
Erklärungsschwelle −0,6) – KI-Torangriffe gibt es dort nur in feindlichen Test-/Eigenwelten; im Normalspiel ist der Spieler der
einzige Angreifer.

## 6. Folgen für die Wirtschaft und Schutzmechanismen

- **Ringtor aus:** nur Umweg (Lauf H). `Revalidate` plant laufende Reisen um; Händler planen mit dem Profil neu.
- **Kartenteilendes Tor aus** (nur durch den Spieler): `MapParts` teilt, `FleetCapacity` rechnet je Teil, `SupplyPanicSystem`
  würde nach 1000 Ticks melden – deshalb Wiederanlauf genau zu dieser Frist. Exporte hinter dem Tor (Handelsposten Aurelia)
  stocken kurz.
- **Ruhende Rentabilitätsuhr** (Phase 22, `Profitability.Resting`) greift bei einer Teilung bereits – keine Abgänge „nicht
  rentabel“ wegen eines Torausfalls.
- **Schutzregeln zusammengefasst:** `GateMaxDown` 1 · KI nie kartenteilend · Wiederanlauf 1000/4800 Ticks · Hauptrouten nie
  zerstörbar · Reparatur nur aus `Spendable` · Ereignis `GateAttacked` beim ersten Treffer (Vorwarnung, Toast/Alarm, Polizei
  greift an).
- **Messen im Balance-Werkzeug:** Torausfälle (Anzahl, Dauer, gleichzeitig max.), Wiederanläufe durch Notregel,
  Reparaturkosten, Händlerpleiten während eines Ausfalls.

## 7. Architektur-Skizze

- **Gemeinsame Naht statt Kopie:** `Siege`/`StationCombat` arbeiten heute direkt auf `Station` (`Encounter.Station`,
  `StationX`, `Station.Condition`). Neu: `ISiegeTarget` (Besitzer/Fraktion, Position, Körperradius, `DefenseConfig`, Hülle/Schild,
  `Profile`, `ApplyDamage`, `OnFall`, `IsDown`) mit Adaptern `StationTarget` und `GateTarget`; `Encounter` bekommt ein
  additives Ziel (`SiegeTarget { Kind, Id }`, Station bleibt Standard). Die Verteidigungsdaten nutzen denselben Parser
  (`StationDefenseConfig`).
- **Daten:** neuer Abschnitt `gateTypes[] { key, name, defense, destructible, repairPerDay, maxOutageTicks }` und optional
  `gates[].type`; ohne Angabe `main` bei `mainRoute`, sonst `side`. Fehlender Abschnitt = Tore ohne Kampfwerte (heutiges Verhalten).
- **Zustand:** `GateCondition` (Hüllen-/Schild-Fehlbetrag, Schildpause, Geschützuhren, `Disabled`, `DownSince`) am Tor mit
  kleinerer Id; null = intakt. Speichern additiv (`GateSave.Condition`, Save bleibt v17), Hash nur wenn gesetzt.
- **Systeme:** `GateRepairSystem` (Combat-Modul, Intervall 50, neben `StationRepairSystem`) inkl. Notregel; Torblockade als
  Zielwahl im `NavySystem`/`AssaultExecutor` (`AssaultCommand` bekommt das Ziel als `SiegeTarget`).
- **Ereignisse:** `GateAttacked`, `GateDisabled`, `GateRestored(reason: repaired|emergency)`, später `GateDestroyed`.
- **Schalter:** `EconomyRules.GateCombat` (Standard **aus** bis zur Balance), `GateMaxDown`, `GateReenableShare`,
  `GateMaxOutageTicks`/`GateSplitOutageTicks`, `AllowGateDestruction` (aus).
- **Ansicht:** Tor antippbar (wie Stationen), Zielkarte „Hülle · Schild“ (`WarInfo.StationShort`-Muster), Knopf „Tor angreifen“
  mit `KitConfirm`, Beschädigt-Look, Kreuz/Strichlinie auf der Universumskarte gibt es für „geschlossen“ schon.

### Aufwand (grob, Teilschritte)

| Schritt | Inhalt | Größe |
|---|---|---|
| 1 | Daten `gateTypes`, `GateCondition`, Speichern/Hash, Schalter; Hash-Test „Schalter aus = bisher“ | klein |
| 2 | Naht `ISiegeTarget`, Siege/StationCombat umbauen ohne Verhaltensänderung (Hash-Test Phase 28) | mittel |
| 3 | Tor als Ziel: Schaden, Ausfall → `Closed`, Ereignisse, Reparatur + Notregel | mittel |
| 4 | Spieler: Torangriff, Verbrechen, Zielkarte, Look „beschädigt“, Selbsttest-Stufe | mittel |
| 5 | KI-Blockade im Krieg mit Schutzregeln, Verteidigung | mittel |
| 6 | (groß) Zerstörung + Wiederaufbau über Baustelle, Torgeschütze/Waffenstellungen | groß |
| 7 | Balance: Werkzeugzeilen, Läufe | mittel |

**Risiken:** Umbau von `Siege` darf Phase-28-Hashes nicht verschieben (Prüfung mit `RevertPhase…`-Muster); Kaperer-Lauerplätze
an Toren; Spielerfrust bei Sackgassen (Abhilfe: kurze Frist); Doppelzählung, wenn Station und Tor im selben Gefecht belagert werden
(Empfehlung: ein Gefecht hat genau ein Belagerungsziel).

**Tests:** Schalter aus bit-identisch; gemeinsamer Vorrat beider Hälften; Ausfall → Route/Revalidate ohne Geisteraufträge;
Theorie über alle 14 Paare + bereits ausgefallene Tore: KI wählt nie ein kartenteilendes; `GateMaxDown`; Wiederanlauf nach Frist
ohne Kasse; Reparatur nie unter die Rücklage; Speichern mitten in der Torbelagerung bit-identisch; Spieler kann sich nicht
dauerhaft aussperren. **Balance-Läufe:** Standard 20 000/60 000 mit Schalter an (nichts darf sich ändern, da kein Krieg);
feindliche Testwelt 300 000 (Krieg, Torblockaden); Läufe mit erzwungenen Torausfällen alle n Ticks auf 17, 3 und einer
Ringkombination (wie I) über 300 000 Ticks; Kriterium „Preise im Zielbereich“, keine Panik bei Ausfällen < Frist, Händlerpleiten
nicht über dem offenen Lauf.

## 8. Varianten und Entscheidungen

| Variante | Umfang | Vorteile | Nachteile |
|---|---|---|---|
| **Klein** | Schritte 1–4: Tore haben Werte, nur der Spieler kann sie ausschalten; Reparatur aus der Kasse + Notregel | wenig Risiko, schnell sichtbar, Wirtschaft praktisch unberührt | Tore spielen im Krieg keine Rolle |
| **Mittel (Empfehlung)** | + Schritt 5 und 7: KI-Blockade im Krieg unter Schutzregeln, Torklassen, Verteidigung | Tore werden Kriegsziel und Schutzaufgabe; Schutzregeln schließen Kartenzerfall aus | KI-Blockaden nur in Welten mit Krieg sichtbar; Naht-Umbau nötig |
| **Groß** | + Schritt 6: Nebentore zerstörbar, Wiederaufbau mit Polier, Torgeschütze, Waffenstellungen am Tor | volle Fantasie (Trümmer, Wiederaufbau als Projekt) | größter Eingriff in Routen/Piraten-Balance; braucht Kleinstationen |

**Empfehlung:** Variante **mittel**, in der Reihenfolge der Schritte 1–5 + 7, Schalter `GateCombat` erst nach den Balance-Läufen
an. Die Naht (Schritt 2) macht Variante groß später zu einer Ergänzung statt einem Umbau.

### Entscheidungsfragen

1. **Zerstören oder nur ausschalten?** – Empfehlung: zunächst nur ausschalten (außer Betrieb, reparierbar); Zerstören später nur
   für Nebentore.
2. **Gemeinsame Trefferpunkte für beide Torhälften?** – Empfehlung: ja.
3. **Eigene Geschütze an der Zollstation?** – Empfehlung: als Datenfeld ja, in der Standardwelt zunächst keine; nach Balance evtl.
   2 Laser S an Haupttoren oder Verteidigung über die Waffenstellungen.
4. **Dürfen KI-Fraktionen im Krieg Tore blockieren?** – Empfehlung: ja, aber nur nicht kartenteilende Tore, höchstens eines
   weltweit, eine Blockade je Krieg.
5. **Darf der Spieler auch die Sackgassentore 17/27 ausschalten?** – Empfehlung: ja, als schweres Verbrechen bei beiden
   Endfraktionen, mit Wiederanlauf nach 1000 Ticks (unterhalb der Panikfrist).
6. **Sind die Werte richtig dimensioniert** (Haupttor: Leviathan 4,6 min, Falke 49 min; Nebentor: Leviathan 1,7 min, Falke
   16 min)? – Empfehlung: so übernehmen; Haupttore sollen für Einzelschiffe praktisch unknackbar sein.

# Phase 31: Umsetzung (Variante mittel) – Checkliste

Entscheidung des Nutzers: „Torkonzept ohne Neubau von Toren genehmigt“ → Variante **mittel**, alle sechs Entscheidungsfragen nach Empfehlung
(nur ausschalten, gemeinsamer Vorrat, Geschütze als leeres Datenfeld, KI-Blockade nur im Krieg/nie kartenteilend/höchstens eines/eine je Krieg,
Spieler darf jedes Tor angreifen, Werte wie Abschnitt 2).

- [x] Daten: `gateTypes` (side/main), `gates[].type`, `GateCondition` am Tor mit kleinerer Id, Speichern additiv, Hash nur wenn gesetzt, Schalter `GateCombat`
- [x] Naht `ISiegeTarget` (Adapter `StationTarget`, `GateTarget`); Siege, Gefechtsschritt, Taktik, Raketen, Spielerschüsse darüber — Referenz-Hashes unverändert
- [x] Tor als Ziel: Schaden/Schild/Panzerung, Ausfall → `Closed` (Routen, Kartenteile, Umplanung), Ereignisse `GateAttacked/GateDisabled/GateRestored/GateRepaired`
- [x] Reparatur aus `Spendable` der Endsektor-Besitzer (nicht der Angreifer), Wiederanlauf ab 25 %, Notregel 1000/4800 Ticks, herrenlose Enden
- [x] Spieler: Torangriff = `assault_gate` bei beiden Endfraktionen, Verteidiger wie bei Stationen; Routenplanung erklärt die Sperre
- [x] KI-Blockade im Krieg (`Wars.PickGateTarget`): nie kartenteilend, `GateMaxDown` 1, eine je Krieg; Piraten nie
- [x] Stationsgeschütze: gesuchtes Mitglied der eigenen Fraktion ist Ziel (wie Kleinstationen)
- [x] Ansicht: Tor wählbar, Zielkarte, „Tor angreifen“ (zwei Tipps, Warnung), Treffer am Modell, beschädigt/außer Betrieb, Karten, Meldungen, Autopilot-Grund
- [x] Tests, Selbsttest-Stufe, Balance (Schalter an: Standardwelt unverändert; Lauf mit periodischen Torausfällen), CLAUDE.md

## Runde 15: Werte nachkalibriert (Handy-Test: Nebentor mit 3 Fregatten ≈ 15 min)

Ursache: Die Panzerung wird je Treffer abgezogen (`ArmorPerHitShare` 0,05 × Artfaktor; Energie 1,5) — Panzer 70 fraß vom Laser S (7) 5,25, Panzer 100 alles;
die Raketen (12 je Fregatte) sind nach ~30 s leer, danach tragen nur Laser und Massetreiber. Die Rechenbeispiele in Abschnitt 2 hatten den Panzerabzug je
Treffer unterschätzt. Neue Werte (nur Tore, die Panzerregel bleibt):

| Klasse | Hülle | Schild | Aufl./s | Panzer | PV |
|---|---|---|---|---|---|
| `side` | 2000 | 300 | 3 | **30** (vorher 70) | 0,2 |
| `main` | **3500** (vorher 4000) | 500 | 5 | **25** (vorher 100) | 0,35 |

Gemessen (Core-Test `Kriegstestverband_NebentorIn2Bis4Min_HaupttorIn5Bis8Min_EinzeljaegerChancenlos`: echter Kriegstest-Verband aus drei Bollwerken,
begrenzte Munition und Raketen, Zielhilfe, ohne Gegenwehr): Nebentor Borealis–Vesta **2,3 min**, Haupttor Alpha–Beta **6,7 min** (Vesta–Beta 6,3 min);
Falke allein am Nebentor in 6 min nicht (≈ 2 Hülle/s → ≈ 17 min, mit Verteidigern aussichtslos).

## Runde 16: Gerätetest Haupttor ≈ 9 min — Abstand, nicht die Werte

Nachgemessen mit dem echten Kriegstest-Verband (drei Bollwerke, echte Fits, begrenzte Munition/Raketen, Zielhilfe, Verband tritt bei), Haupttor Alpha–Beta:

| Abstand aller drei zur Tormitte | Ausfall nach | Raketen leer | Munition leer |
|---|---|---|---|
| 40 m | 6,3 min | 40 s | 4,5 min |
| 55 m (Massetreiber 45 m schweigt) | 7,4 min | 40 s | — |
| 70 m / 100 m (nur Raketen reichen) | nie (nach den Raketen lädt der Schild wieder) | 40 s | — |

Nebentor Borealis–Vesta: 40 m 2,3 min, 55 m 3,0 min. Die ≈ 9 min am Gerät = Anflug + Zeit außerhalb 45 m. Werte bleiben; ehrlich gemacht:
die Zielkarte zeigt im Gefecht „Raketen leer · Munition leer – nur noch Laser“ (`PlayerGunnery.DepletedText`, dazu ein einmaliger Hinweis mit dem
Nachladeort), die Beschreibung des Startpakets „Kriegstest“ nennt Zeiten, Abstand und Munitionsdauer.
