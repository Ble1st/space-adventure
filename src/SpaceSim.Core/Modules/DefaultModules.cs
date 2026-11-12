using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Modules;

public sealed class ProductionModule : ISimModule
{
    public string Name => "Production";
    public void Register(SimulationBuilder b) => b.Add(new ProductionSystem()).Add(new MarketSystem());
}

/// <summary>
/// Phase 13: Forschung (FORSCHUNG.md). Läuft direkt nach Produktion und Markt: deckelt die Gebote der Forschungsstationen auf ihre Verbrauchsgüter,
/// bevor Händler ihre Ziele wählen, füllt ihr Budget aus der Fraktionskasse und bucht je Takt Forschungspunkte; Freischaltungen gelten sofort.
/// </summary>
public sealed class ResearchModule : ISimModule
{
    public string Name => "Research";
    public void Register(SimulationBuilder b) => b.Add(new ResearchSystem());
}

/// <summary>
/// Phase 14: Exportsenke (Handelsstationen an Planeten). Läuft direkt nach Produktion/Markt (und Forschung): deckelt die Gebote für Exportwaren, bevor
/// Händler ihre Ziele wählen, und schickt fällige Chargen an den Planeten (Ware verschwindet, Station erhält Ankauf × (1 + Aufschlag)).
/// </summary>
public sealed class ExportModule : ISimModule
{
    public string Name => "Export";
    public void Register(SimulationBuilder b) => b.Add(new ExportSystem());
}

/// <summary>
/// Phase 12: Befehle (FLOTTEN.md Abschnitt 4, Ebenen Notfall &gt; Befehl &gt; Automatik) und Hangar (Liegegebühr). Läuft vor der Aufgabenwahl und den
/// ausführenden Systemen, damit ein neuer Arbeitsbefehl (Handel, Abbau, Bau) im selben Tick ausgeführt wird. Schiffe ohne Befehl kosten nichts.
/// </summary>
public sealed class OrderModule : ISimModule
{
    public string Name => "Orders";
    public void Register(SimulationBuilder b) => b.Add(new OrderSystem()).Add(new HangarSystem(), interval: 50);
}

/// <summary>
/// Phase 6: Aufgabenwahl der Multirole-Schiffe. Läuft vor den ausführenden Systemen (Schürfen, Handel), damit ein Wechsel im selben Tick
/// vom neuen Ausführer übernommen wird.
/// </summary>
public sealed class JobModule : ISimModule
{
    public string Name => "Jobs";
    public void Register(SimulationBuilder b) => b.Add(new JobAssignmentSystem());
}

/// <summary>Phase 26: bezahlte Reparatur der KI-Schiffe (vor Händlern und Schürfern; ohne Schaden nur eine Prüfung je Schiff).</summary>
public sealed class RepairModule : ISimModule
{
    public string Name => "Repair";
    public void Register(SimulationBuilder b) => b.Add(new NpcRepairSystem(), interval: 10);
}

public sealed class ResourceModule : ISimModule
{
    public string Name => "Resources";
    // Respawn ist ein seltenes Zufallsereignis → mit Intervall (Wahrscheinlichkeit skaliert mit dt)
    public void Register(SimulationBuilder b) => b.Add(new AsteroidRespawnSystem(), interval: 10).Add(new MinerSystem());
}

public sealed class TradeModule : ISimModule
{
    public string Name => "Trade";
    public void Register(SimulationBuilder b) => b.Add(new TraderSystem());
}

/// <summary>
/// Phase 7: NPC-Upgrades. Läuft nach dem Handel (wer gerade an einer Werft angekommen und untätig ist, liegt dort) und vor dem Lebenszyklus.
/// </summary>
public sealed class UpgradeModule : ISimModule
{
    public string Name => "Upgrades";
    public void Register(SimulationBuilder b) => b.Add(new UpgradeSystem());
}

/// <summary>
/// Phase 8: Stationsbau über Baustellen (Montage, Notbau, Verfall) und die Bauschiffe als Ausführer der Aufgabe „build“. Läuft nach Handel und
/// Upgrades (Händler haben ihre Lieferungen gebucht) und vor dem Lebenszyklus, der neue Baustellen anlegt.
/// </summary>
public sealed class ConstructionModule : ISimModule
{
    public string Name => "Construction";
    public void Register(SimulationBuilder b)
    {
        b.Add(new ConstructionSystem());
        b.Add(new FactionBuilderSystem(), FactionBuilderSystem.Interval);   // Phase 20: Bauschiffe der Fraktionen (Unterhalt, Mindestbestand)
    }
}

/// <summary>
/// Phase 11: Sicherheit (FLOTTEN.md Abschnitte 6–7). Läuft nach Handel und Bau (Reisen sind geplant, Fracht ist an Bord) und vor dem Lebenszyklus, damit
/// verlorene Schiffe im selben Tick vom <see cref="FleetSystem"/> nachgekauft werden können: Lagekarte (Beobachtung), Piraten (Revier, Überfälle,
/// Abfangen durch Streifen), Polizei (Unterhalt, Soll, Kauf, Zuweisung, Streife, gesuchter Spieler) und Verfall der Fahndung.
/// </summary>
public sealed class SecurityModule : ISimModule
{
    public string Name => "Security";
    public void Register(SimulationBuilder b) =>
        b.Add(new ThreatMapSystem(), interval: 20).Add(new PirateSystem(), interval: 5).Add(new SecuritySystem(), interval: 5).Add(new WantedSystem(), interval: 50);
}

/// <summary>
/// Phase 23/24: Kampf und Schiffssysteme (KAMPF.md Abschnitte 3 und 4). Läuft nach der Sicherheit (Überfälle und Abfangen haben Gefechte eröffnet):
/// zuerst das Rundengefecht (<see cref="SpaceSim.Core.Combat.CombatSystem"/>, nur aktive Gefechte), dann laden Schild und Speicherbank aus dem
/// Reaktorüberschuss nach (nur Schiffe, denen etwas fehlt), zuletzt laden angedockte NPCs mit Geschützen Munition nach.
/// </summary>
public sealed class CombatModule : ISimModule
{
    public string Name => "Combat";
    public void Register(SimulationBuilder b) =>
        b.Add(new SpaceSim.Core.Combat.CombatSystem()).Add(new SpaceSim.Core.Combat.ShipSystemsSystem())
         .Add(new SpaceSim.Core.Combat.FireSystem())                       // Phase 26: Brände über den Erholungsindex
         .Add(new SpaceSim.Core.Combat.RearmSystem(), interval: 10)
         .Add(new WreckSystem(), interval: 50);                            // Phase 26: Wracks verfallen
}

public sealed class LifecycleModule : ISimModule
{
    public string Name => "Lifecycle";
    // Seltene, kostengünstige Prüfungen: Skalierung über dt bzw. Zeitstempel
    public void Register(SimulationBuilder b) =>
        b.Add(new UpkeepSystem(), interval: 10).Add(new SupplyPanicSystem(), interval: 20)   // Phase 20: Versorgungspanik vor dem normalen Bau
         .Add(new StationLifecycleSystem(), interval: 20).Add(new FleetSystem(), interval: 20);
}

/// <summary>
/// Phase 9: Akteure (POLITIK.md Abschnitt 6). Läuft nach dem Lebenszyklus (Flotte und Stationen haben sich geändert) und entfernt aufgestiegene
/// Einzelakteure ohne Besitz (Abstieg). Aufstieg, Zukauf und Kassen hängen an den Systemen, in denen sie geschehen (Flotte, Lebenszyklus, Unterhalt).
/// Phase 15: dazu die Ausgaben der Fraktionskassen (<see cref="MaintenanceSystem"/>, <see cref="SubsidySystem"/>).
/// </summary>
public sealed class ActorModule : ISimModule
{
    public string Name => "Actors";
    // Phase 15: Ausgaben der Fraktionskassen — Stationsinstandhaltung (Material über den Handel, Kasse erstattet) und Subvention notleidender Stationen
    public void Register(SimulationBuilder b) => b.Add(new ActorSystem(), interval: 20).Add(new MaintenanceSystem(), interval: 50).Add(new SubsidySystem(), interval: 50);
}

/// <summary>
/// Phase 22: Abgänge und Handlungsfähigkeit. Misst die Rentabilität jedes wirtschaftlichen KI-Schiffs und jeder Station (<see cref="ProfitabilitySystem"/>,
/// Grund c für Flotte und Stationsprüfung), gleicht das Betriebsgeld innerhalb eines Akteurs aus (<see cref="FirmCapitalSystem"/>) und hält den Mindestbestand
/// jeder Fraktion, notfalls per Notkauf (<see cref="FactionViabilitySystem"/>).
/// </summary>
public sealed class ViabilityModule : ISimModule
{
    public string Name => "Viability";
    public void Register(SimulationBuilder b) =>
        b.Add(new ProfitabilitySystem(), ProfitabilitySystem.Interval).Add(new FirmCapitalSystem(), interval: 50)
         .Add(new FactionViabilitySystem(), FactionViabilitySystem.Interval);
}

/// <summary>
/// Phase 10: Politik (POLITIK.md Abschnitte 2, 3 und 8). Läuft nach Lebenszyklus und Akteuren (Stationen und Besitz haben sich geändert):
/// Sektorkontrolle aus dem Einfluss (selten), dann Beziehungen und Sperrstufen der Fraktionen mit Durchsetzungskosten; geänderte Sperren planen
/// laufende Reisen neu (<see cref="AccessEnforcement"/>). Der Gate-Zugang selbst wird beim Planen und Reisen geprüft (<see cref="GateAccess"/>).
/// </summary>
public sealed class PoliticsModule : ISimModule
{
    public string Name => "Politics";
    public void Register(SimulationBuilder b) => b.Add(new SectorControlSystem(), interval: 50).Add(new FactionPolicySystem(), interval: 20);
}
