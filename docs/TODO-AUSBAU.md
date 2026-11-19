# Ausbau-Liste (Auftrag vom 2026-11-19: „Alle Punkte, vollständig ausgearbeitet und getestet“)

Ablauf je Punkt: Opus-Agent setzt im Worktree um → Sonnet-Agent testet (Build 0 Warnungen, `dotnet test`, Selftest, Balance 20000/60000, ui-check wo UI) → Merge `--ff-only` auf main, Push → Haken hier.
Regeln: Save v17 additiv, Revert-Helfer für Referenz-Hash-Tests, „Handel so lassen“, Tests nur Sonnet.

## Laufend
- [ ] 0 Gas und Nebel (Gassammler, Gasfelder, Gasraffinerie, Ionengas, Ionentriebwerk S/M, Gas in Schilden, Nebelsektoren zufällig, Nebelwirkung) + Fusionstreibstoff (Anreicherungsanlage, Fusionsantrieb L/XL) + Tankanzeige

## Reihenfolge
- [ ] 1 Eigene Flotte: Befehle „Angreifen“ (Schiff/Station/Tor) und „Verfolgen“ (über Sektorgrenzen, Ende bei Ziel weg/2400 Ticks/Sperre), max. 3 Taps, Zielkarte „Verband greift an“, Verbrechen außerhalb Krieg, Befehlsstufe, Einsatzregeln
- [ ] 1b Weitere Flottenbefehle (2026-11-19): „Bergen“ (Wracks/Container im Sektor leeren, Beute verkaufen), „Tor/Sektor blockieren“ (bewaffneter Verband greift Schiffe einer Fraktion am Tor an, nur Krieg), „Liefern/Pendel“ (feste Route A→B mit fester Ware, z. B. eigene Station versorgen), „Sammelpunkt“ (ausgewählte Schiffe treffen sich und bilden Verband), „Konvoi“ (eigene Händler fahren unter Schutz eigener Kampfschiffe)
- [ ] 16 Erkundung und Intel (2026-11-19): Spieler kennt am Anfang nicht alles — unbekannte Sektoren, Stationen und Fraktionen (Startsektor + Nachbartore bekannt; Fraktionen erst nach Begegnung/Entdeckung bekannt). Befehl „Erkunden“ auf zwei Ebenen: Sektor (fliegt Punkte ab, deckt Stationen/Felder/Tore auf) und Universum (springt durch unbekannte Tore, deckt Sektoren auf). Intel kaufen bei bekannten Fraktionen (Comms/Behörde: Sektorkarten, Stationslisten, Preise; Preis je Umfang). Taxifahrt: Ziel wird sofort bekannt. Wirkt auf alle Karten, Routen/Autopilot, Taxi-/Auftrags-/Handelshinweise (nur bekannte Ziele). Wissen gehört dem Spieler-Akteur, gespeichert additiv, im Hash nur mit Spielerpolitik; NPC-Welt unverändert. Möglichst früh, weil alle UIs davon abhängen.
- [ ] 2 KI-Kriegsschiffe: Seeschlacht (Anlass „Seeschlacht“ bei Feinden im selben Sektor), gezielte Abwehr gegen angreifende Verbände, Geleitschutz für wertvollste Händler im Krieg
- [ ] 13 Rechenzeit: messen, Pfadplanung/Andockprüfung zwischenspeichern, Index Station×Gut; Hash bitgleich, Ziel alter ms/Tick
- [ ] 8 Stationsverschleiß: Rückstand > 50 % Tagesbedarf → −1 %/Tag, < 70 % langsamere Produktion, < 30 % Stillstand, Erholung mit Material; Regierung/Startstationen min. 50 %
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
