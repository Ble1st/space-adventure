# Ausbau-Liste (Auftrag vom 2026-11-19: „Alle Punkte, vollständig ausgearbeitet und getestet“)

Ablauf je Punkt: Opus-Agent setzt im Worktree um → Sonnet-Agent testet (Build 0 Warnungen, `dotnet test`, Selftest, Balance 20000/60000, ui-check wo UI) → Merge `--ff-only` auf main, Push → Haken hier.
Sparmodus (Nutzer 2026-11-20): Sonnet wiederholt bei Zeit-/Lastfehlern nur betroffene Testklassen, nicht die ganze Suite; Punkte 4 und 16b schreibt Vibe (nur Code, Bauen/Testen macht der Orchestrator bzw. Sonnet); Punkte 3 und 6 in einer Runde.
Regeln: Save v17 additiv, Revert-Helfer für Referenz-Hash-Tests, „Handel so lassen“, Tests nur Sonnet.

## Laufend
- [x] 0 Gas und Nebel (Gassammler, Gasfelder, Gasraffinerie, Ionengas, Ionentriebwerk S/M, Gas in Schilden, Nebelsektoren zufällig, Nebelwirkung) + Fusionstreibstoff (Anreicherungsanlage, Fusionsantrieb L/XL) + Tankanzeige

## Reihenfolge
- [x] 1 Eigene Flotte: Befehle „Angreifen“ (Schiff/Station/Tor) und „Verfolgen“ (über Sektorgrenzen, Ende bei Ziel weg/2400 Ticks/Sperre), max. 3 Taps, Zielkarte „Verband greift an“, Verbrechen außerhalb Krieg, Befehlsstufe, Einsatzregeln
- [x] 1b Weitere Flottenbefehle (2026-11-19): „Bergen“ (Wracks/Container im Sektor leeren, Beute verkaufen), „Tor/Sektor blockieren“ (bewaffneter Verband greift Schiffe einer Fraktion am Tor an, nur Krieg), „Liefern/Pendel“ (feste Route A→B mit fester Ware, z. B. eigene Station versorgen), „Sammelpunkt“ (ausgewählte Schiffe treffen sich und bilden Verband), „Konvoi“ (eigene Händler fahren unter Schutz eigener Kampfschiffe)
- [x] 16 Erkundung und Intel (2026-11-19): Spieler kennt am Anfang nicht alles — unbekannte Sektoren, Stationen und Fraktionen (Startsektor + Nachbartore bekannt; Fraktionen erst nach Begegnung/Entdeckung bekannt). Befehl „Erkunden“ auf zwei Ebenen: Sektor (fliegt Punkte ab, deckt Stationen/Felder/Tore auf) und Universum (springt durch unbekannte Tore, deckt Sektoren auf). Intel kaufen bei bekannten Fraktionen (Comms/Behörde: Sektorkarten, Stationslisten, Preise; Preis je Umfang). Taxifahrt: Ziel wird sofort bekannt. Wirkt auf alle Karten, Routen/Autopilot, Taxi-/Auftrags-/Handelshinweise (nur bekannte Ziele). Wissen gehört dem Spieler-Akteur, gespeichert additiv, im Hash nur mit Spielerpolitik; NPC-Welt unverändert. Möglichst früh, weil alle UIs davon abhängen.
- [x] 1c Nachtrag Flotte: Sammelpunkt mit Frist (Mitglied kommt nie an → Abbruch statt ewig über Kapazität), „Liefern“ mit Verlustschutz-Schalter wie Handel
- [ ] 16b Nachtrag Intel: TradeActions.BestBuyer für Spielerschiffe nur bekannte Stationen (Auto-Mining, Liefern), Kriegs-/Friedens-Toasts nur mit bekannten Fraktionen
- [x] 2 KI-Kriegsschiffe: Seeschlacht (Anlass „Seeschlacht“ bei Feinden im selben Sektor), gezielte Abwehr gegen angreifende Verbände, Geleitschutz für wertvollste Händler im Krieg
- [x] 2b Nachtrag Seeschlacht: Reserve zählt bereits kämpfende Kriegsschiffe mit; Abkühlzeit je Gegnerpaar gegen sofortigen Neustart einer Seeschlacht
- [x] 13 Rechenzeit: messen, Pfadplanung/Andockprüfung zwischenspeichern, Index Station×Gut; Hash bitgleich, Ziel alter ms/Tick
- [x] 8 Stationsverschleiß: Rückstand > 50 % Tagesbedarf → −1 %/Tag, < 70 % langsamere Produktion, < 30 % Stillstand, Erholung mit Material; Regierung/Startstationen min. 50 %
- [ ] 9 Firmensitz (KI-Firmen ab Klasse 2, eigene Kasse/Investition; Spieler: Befehlsstufe + Fehden) und Büro (Außenstelle 1,5 Einfluss, Hangar, Auftragsbrett, Schutzverträge)
- [ ] 5 Schmuggler-KI: gesetzlose Firmen mit Tarnfeld + Sprungantrieb, verbotene Ware/Hehlerware/Embargo in gesperrte Sektoren, Sprung an Sperren vorbei, Tarnung bei Polizei, Durchsuchung mit Sensorstärke, max. 3 je Firma
- [ ] 10 Missionen: Schmuggel (Hehler/gesetzlose Firma, versiegelt, Sperrsektor, hohe Belohnung, Strafe/Beschlagnahme/Ansehen) und Forschung (Messpunkte im Nebel, Wrackproben, seltene Waren; Geld + FP)
- [ ] 6 Fehden (< −0,4 + umkämpfter Sektor, ~6000 Ticks, Überfälle/Sperren, keine Belagerung, Ende oder Krieg), Vermittlung (dritte Fraktion +0,1, Spieler über Comms gegen Geld), Regierungskrise nach HQ-Verlust (12000 Ticks: halbe Steuern, kein Polizeikauf, Sperren max. 1, leichteres Überlaufen)
- [ ] 7 Forschung: ~12 neue Projekte (Rezepte, Ware „Medizin“, Verbesserungen Nachladen/Bohren/Schild/Werft), früher Zugang ab 25 % Finanzierung (6000 Ticks), Rückschritt laufendes Projekt bei Verlust der letzten Forschungsstation
- [ ] 3 Polizei verfolgt 1 Sprung in befreundete/verbündete Sektoren, Schutzvertrag für Spieler über Comms (~200 Cr/Tag, sichere Hilfe), Konvois bei hoher Bedrohung (3–5 Händler + 1–2 Polizei)
- [ ] 4 NPC-Bergung: Bergeschiffe holen Module (Zustand ≥ 0,5) aus Wracks und verkaufen sie; neues Bergeschiff-Modell
- [ ] 11 Luxusexport Start: größeres Platin-/Elektroniklager der Luxuswerkstatt, zweite Luxuswerkstatt nahe Planet (Kalypso)
- [ ] 14 Fahrgäste/Personentaxi (2026-11-19, zusammen mit 10): Missionstyp „Fahrgast“ an Taxi-/Hangarstationen, Modul Passagierkabine S/M (2/6 Plätze, Fracht- oder Utility-Slot), Lohn nach Flugzeit/Entfernung, Eilzuschlag, VIP, Trinkgeld sinkt bei Treffern, Verlust = gescheitert + Ansehen; Sonderfahrgäste Flüchtling/Gesuchter (Kontrollpunkt-Risiko, Verbrechen) und Diplomat (Ansehen); neuer Flottenbefehl „Taxi fahren“ (automatisch, Umkreisgrenzen wie Handel); Swift bekommt Rolle
- [ ] 12 Ungenutzte Modulregale: nach 20000 Ticks unberührt Ziel −1 und halber Preis

## Erledigt
(Commits hier eintragen)
- 0 Gas/Nebel/Fusion/Tankanzeige: f3a107a, 9a218b0, eccfe81 — Sonnet: 1458/1458 Tests, Selftest OK, UI-Check OK, Balance 20000 48ECEC8C1781997D / 60000 8B66C504B54B9DA3
- 1/1b Flottenbefehle: 4281c9a — Sonnet: 1479/1479 Tests, Selftest OK (Stufe 146), UI-Check voll OK, Balance 20000 48ECEC8C1781997D unverändert
- 16 Erkundung/Intel: 779862e — Sonnet: 1493/1493 Tests, Selftest 2× OK (Stufe 147), UI-Check OK, Balance 20000 48ECEC8C1781997D unverändert
- 2 KI-Kriegsschiffe + 1c: 168af91 — Sonnet: 1503/1504 (Zeittest ManyShips nur unter Last, einzeln grün), Selftest OK (Stufe 148), UI-Check OK, Balance 20000 48ECEC8C1781997D unverändert; Krieg 2→1: 11 Seeschlachten, Belagerungen 1073→1, alle Fraktionen handlungsfähig
- 13 Rechenzeit: b60baf1 — Sonnet: 7 Läufe (bis 150000 Ticks, Krieg, fuel/research aus) bitgleich zu dff27ed, 60000 Ticks 0,871 → 0,583 ms/Tick (−33 %), 1509/1510 (Zeittest ManyShips nur unter Last), Selftest OK
- 8 Stationsverschleiß + 2b: 4a0fe21 — Sonnet: 1522/1522, Selftest OK (Stufe 149), UI-Check OK (1 Messwackler, einzeln OK), Balance 20000 6D3C01B1ED7DEBCD, 300000: Ø Zustand 85 %, keine Station < 75 %, Notinstandhaltung 0,94 M Cr (4,3 % der Steuereinnahmen)
