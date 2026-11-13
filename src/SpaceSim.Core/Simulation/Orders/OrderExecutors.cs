using System.Diagnostics.CodeAnalysis;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Zustand eines Befehls nach einem Schritt seines Ausführers.</summary>
public enum CommandStatus { Running, Done, Failed }

/// <summary>
/// Phase 12 (FLOTTEN.md Abschnitt 4): Ausführer eines Befehlstyps. Neue Befehle sind neue Ausführer (Registrierung in <see cref="OrderExecutors"/>),
/// keine Verzweigungen in zentralen Klassen. Ausführer arbeiten deterministisch (Zufall nur über <see cref="Universe.Rng"/>) und speichern ihren
/// Fortschritt nur in gespeicherten Schiffsfeldern (<see cref="Ship.CommandStep"/>, <see cref="Ship.Order"/>, <see cref="Ship.NextThinkTick"/>).
/// </summary>
public interface IOrderExecutor
{
    /// <summary>Befehlstyp (<see cref="CommandKinds"/>).</summary>
    string Kind { get; }
    /// <summary>Nötige Fähigkeit (<see cref="Capability.None"/> = keine).</summary>
    Capability Needs { get; }
    /// <summary>Kann das Schiff den Befehl jetzt annehmen? null = ja, sonst der Grund (Ziel fehlt, nicht erreichbar …). Die Fähigkeit prüft <see cref="Commands.Check"/>.</summary>
    string? Check(Universe u, Ship ship, ShipCommand command);
    /// <summary>Befehl beginnt (auch nach einem Notfall wieder).</summary>
    void Start(Universe u, Ship ship, ShipCommand command);
    /// <summary>Ein Takt der Ausführung.</summary>
    CommandStatus Step(Universe u, Ship ship, ShipCommand command, int dt);
    /// <summary>Befehl endet (ersetzt, abgebrochen, Notfall): Arbeitsanweisungen freigeben, laufende Reise an Ort und Stelle beenden.</summary>
    void Stop(Universe u, Ship ship, ShipCommand command);
    /// <summary>Anzeige des laufenden Befehls.</summary>
    string Describe(Universe u, Ship ship, ShipCommand command);
    /// <summary>Phase 27: Kann der Spieler diesen Befehl seiner Flotte geben (Befehlsraster)? Lauern und Verfolgen vergeben nur Fraktionen und Piratengruppen.</summary>
    bool ForPlayer => true;
}

/// <summary>Registry der Befehls-Ausführer (Registrierungsreihenfolge, doppelter Schlüssel wirft; keine statische Veränderung).</summary>
public sealed class OrderExecutors
{
    readonly List<IOrderExecutor> _list = new();
    readonly Dictionary<string, IOrderExecutor> _byKind = new();

    public void Register(IOrderExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        if (_byKind.ContainsKey(executor.Kind)) throw new ArgumentException($"Ausführer '{executor.Kind}' ist bereits registriert.", nameof(executor));
        _byKind.Add(executor.Kind, executor);
        _list.Add(executor);
    }

    public bool TryGet(string kind, [MaybeNullWhen(false)] out IOrderExecutor executor) => _byKind.TryGetValue(kind, out executor);

    /// <summary>Alle Befehlstypen in Registrierungsreihenfolge (Befehlsraster der Oberfläche).</summary>
    public IReadOnlyList<IOrderExecutor> All => _list;

    /// <summary>Standard: Fliegen, Halten, Handel, Schürfen, Bauen, Patrouille, Wache, Eskorte, Folgen, Heimkehr; Phase 27 dazu Lauern und Verfolgen (nicht für den Spieler).</summary>
    public static OrderExecutors CreateDefault()
    {
        var r = new OrderExecutors();
        r.Register(new GoToExecutor());
        r.Register(new HoldExecutor());
        r.Register(new TradeExecutor());
        r.Register(new MineExecutor());
        r.Register(new BuildExecutor());
        r.Register(new PatrolExecutor());
        r.Register(new GuardExecutor());
        r.Register(new EscortExecutor());
        r.Register(new FollowExecutor());
        r.Register(new ReturnExecutor());
        r.Register(new LurkExecutor());      // Phase 27: Piratengruppen
        r.Register(new PursueExecutor());    // Phase 27: Polizei
        r.Register(new AssaultExecutor());   // Phase 28: Kriegsschiffe
        return r;
    }

    /// <summary>Gemeinsame Standard-Registry (unveränderlich nach dem Aufbau).</summary>
    public static OrderExecutors Default { get; } = CreateDefault();
}
