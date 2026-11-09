# Projektstand

Stand: 2026-11-09. Alles unten ist umgesetzt, getestet (98 Unit-Tests) und gepusht, sofern nicht anders vermerkt.

## Vision
Privates, mobiles 2.5D-Weltraumspiel (Vorbild Interstellar Pilot 2) mit einer autonomen Wirtschaft und Fraktionen, die ohne Spieler laufen. Die Welt ist ein Graph aus Sektoren. Jeder Sektor ist eine eigene Instanz, Wechsel nur über Jumpgates.

## Grundentscheidungen
- **Engine:** Godot 4.7.2 (.NET-Build) mit C#. Die Simulation liegt in `SpaceSim.Core` (net8.0, ohne Godot und ohne Datei-IO).
- **Zwei Schichten:** Alle Sektoren existieren als reine Daten und werden immer simuliert. Nur der Sektor des Spielers wird als Szene materialisiert.
- **Architektur ist entscheidend:** Features als eigene Module/Systeme hinter klaren Nähten (Registrierung, Interfaces, Events, Datendateien).
- **Determinismus:** Gleicher Seed ergibt gleichen Hash; Speichern, Laden und Weiterlaufen ist bitgleich.

## Umgesetzt
- Projektaufbau, Git/GitHub, Godot-Import mit Grafiktest (C#-Brücke), Headless-`--selftest`.
- Sim-Kern: Ticks, Module und Systeme mit Intervallen (Simulations-LOD), Event-Bus, Schiffsindex, Reise als Routen-Legs.
- Navigation: Pfadplaner mit Hindernissen (Tiefe 16), Kollision, Ausweichen zwischen Schiffen (rein kosmetisch).
- Welt als JSON (`Content/default-world.json`): 22 Güter, 4 Asteroidenklassen, 18 Stationstypen, 4 Sektoren, 3 Gate-Verbindungen.
- Wirtschaft: Marktpreis nach Lagerbestand, Produktion, Händler-KI, Miner-KI, Lebenszyklus der Stationen (Neubau bei Knappheit, Pleite), Flottenwachstum über Schiffsbausätze, Geldkreislauf mit Senken (Unterhalt, Treibstoff, Vermögenssteuer).
- Speichern/Laden (Version 3 mit Migrationen) und Offline-Nachholen verpasster Zeit (max. 10k Ticks).
- Mobile UI: virtueller Stick, Zeigersteuerung, Autopilot, Karte, Wirtschaftspanel, Dock-Panel.
- Mining: Einzelschuss-Laser mit Rückstoß, Wahrscheinlichkeit pro Schuss, zufällige Brockengröße, Traktorstrahl, Auto-Modus, Asteroiden mit eigenem endlichem Erz und Respawn. NPC-Miner folgen derselben Regel.
- Sechs Erze (Eisen, Eis, Kupfer, Silizium, Titan, Platin) mit gemischten Asteroidenklassen und Veredelungsketten bis zu Endgütern und Schiffsbausätzen.
- Androidpreset (Export blockiert nur durch fehlende .NET-Exporttemplates).

## Bekannte Schwächen
- Pendelhandel derselben Güter in beide Richtungen (Titan, Lebenserhaltung).
- Nur eine Kolonie als Endnachfrage.
- Alle Zahlen sind Startwerte; Stabilität über 100k Ticks nicht für jeden Seed geprüft.
- Kein visueller Test und kein Test auf einem Gerät (kein Display in der Entwicklungsumgebung).

## Noch nicht umgesetzt
Kauf von Gütern durch den Spieler (nur Verkauf), Waffen/Kampf, Fraktionsverhalten (Beziehungen, Krieg, geschlossene Gates), Missionen, Schiffsausrüstung, echte Modelle/Texturen, Test auf Android-Gerät.
