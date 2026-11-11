using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Modules;

public sealed class ProductionModule : ISimModule
{
    public string Name => "Production";
    public void Register(SimulationBuilder b) => b.Add(new ProductionSystem()).Add(new MarketSystem());
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
    public void Register(SimulationBuilder b) => b.Add(new ConstructionSystem());
}

public sealed class LifecycleModule : ISimModule
{
    public string Name => "Lifecycle";
    // Seltene, kostengünstige Prüfungen: Skalierung über dt bzw. Zeitstempel
    public void Register(SimulationBuilder b) =>
        b.Add(new UpkeepSystem(), interval: 10).Add(new StationLifecycleSystem(), interval: 20).Add(new FleetSystem(), interval: 20);
}

/// <summary>
/// Phase 9: Akteure (POLITIK.md Abschnitt 6). Läuft nach dem Lebenszyklus (Flotte und Stationen haben sich geändert) und entfernt aufgestiegene
/// Einzelakteure ohne Besitz (Abstieg). Aufstieg, Zukauf und Kassen hängen an den Systemen, in denen sie geschehen (Flotte, Lebenszyklus, Unterhalt).
/// </summary>
public sealed class ActorModule : ISimModule
{
    public string Name => "Actors";
    public void Register(SimulationBuilder b) => b.Add(new ActorSystem(), interval: 20);
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
