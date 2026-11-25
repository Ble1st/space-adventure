using SpaceSim.Core.Model;
using SpaceSim.Core.Modules;

namespace SpaceSim.Core.Simulation;

/// <summary>Eine Simulationsaufgabe. <paramref name="dt"/> ist die Anzahl Ticks seit dem letzten Aufruf (Intervall des Systems).</summary>
public interface ISimSystem { void Step(Universe u, int dt); }

/// <summary>
/// Runde 43: ein System, das Ereignisse des Busses (<see cref="Universe.Events"/>) auswertet. Der Runner ruft <see cref="Attach"/> in
/// <b>jedem</b> Tick vor den Systemen auf — unabhängig vom Intervall des Systems. Sonst hörte ein Intervall-System nach dem Laden (neuer
/// Runner, Bus nicht gespeichert) erst ab seinem nächsten fälligen Schritt mit, und Ereignisse dazwischen gingen verloren
/// (Speichern → Laden → Weiterlaufen war dann nicht bitgleich). Muss billig sein (nur ein Referenzvergleich, wenn schon angemeldet).
/// </summary>
public interface ISimEventListener { void Attach(Universe u); }

/// <summary>Bündelt zusammengehörige Systeme. Neue Features werden als Modul hinzugefügt, nicht in bestehenden Systemen verdrahtet.</summary>
public interface ISimModule
{
    string Name { get; }
    void Register(SimulationBuilder builder);
}

public sealed class SimulationBuilder
{
    internal readonly List<(ISimSystem System, int Interval)> Systems = new();

    /// <summary>Registriert ein System. Mit <paramref name="interval"/> &gt; 1 läuft es seltener (Simulations-LOD) und erhält dt = interval.</summary>
    public SimulationBuilder Add(ISimSystem system, int interval = 1)
    {
        if (interval < 1) throw new ArgumentOutOfRangeException(nameof(interval));
        Systems.Add((system, interval));
        return this;
    }

    /// <summary>Die registrierten Systeme mit Intervall (z. B. für Zeitmessungen im Balance-Tool).</summary>
    public IReadOnlyList<(ISimSystem System, int Interval)> Registered => Systems;
}

public sealed class SimulationRunner
{
    readonly List<(ISimSystem System, int Interval)> _systems;
    readonly List<ISimEventListener> _listeners;

    public SimulationRunner(IEnumerable<ISimModule>? modules = null)
    {
        var builder = new SimulationBuilder();
        foreach (var m in modules ?? DefaultModules()) m.Register(builder);
        _systems = builder.Systems;
        _listeners = _systems.Select(x => x.System).OfType<ISimEventListener>().ToList();
    }

    /// <summary>Reihenfolge ist relevant: Produktion/Markt vor den KI-Systemen (Aufgabenwahl vor ihren Ausführern), Lebenszyklus zuletzt.</summary>
    public static IEnumerable<ISimModule> DefaultModules() => new ISimModule[]
    {
        new ProductionModule(), new ResearchModule(), new ExportModule(), new OrderModule(), new JobModule(), new RepairModule(), new ResourceModule(), new TradeModule(), new UpgradeModule(), new ConstructionModule(), new SecurityModule(), new BanditModule(), new CombatModule(), new LifecycleModule(), new ActorModule(), new ViabilityModule(), new PoliticsModule(), new MissionModule(), new IntelModule(), new UniverseEventModule(), new WormholeModule(), new PlayerStatsModule(),
    };

    public void Step(Universe u)
    {
        foreach (var l in _listeners) l.Attach(u);   // Runde 43: Bus-Anmeldung jeden Tick (auch direkt nach dem Laden)
        foreach (var (system, interval) in _systems)
            if (u.Tick % interval == 0) system.Step(u, interval);
        u.Tick++;
    }

    public void Run(Universe u, int ticks) { for (int i = 0; i < ticks; i++) Step(u); }
}
