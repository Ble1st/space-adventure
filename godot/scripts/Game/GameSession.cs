using System;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure.Game;

/// <summary>
/// Hält Universum und Simulationslauf. Reines C# ohne Szenenbaum: die Darstellung fragt nur ab,
/// die Simulation läuft in festen Schritten unabhängig von der Framerate.
/// </summary>
public sealed class GameSession
{
    public Universe U { get; private set; } = null!;
    public RouteRecorder Routes { get; private set; } = null!;
    public SimulationRunner Runner { get; } = new();
    public Ship Player => U.Ships[U.PlayerShip];
    /// <summary>Registry der Stationsdienste (Instanz beim Aufrufer, kein globaler Zustand); die Dock-Bereiche lesen daraus.</summary>
    public StationServices Services { get; } = StationServices.Default();
    /// <summary>Runde 40: Registry der Comms-Aktionen (Strafe/Krieg/Waffenstillstand/Frieden/Tribut); der Comms-Bildschirm liest/ausführt nur darüber.</summary>
    public SpaceSim.Core.Comms.CommsActions Comms { get; } = SpaceSim.Core.Comms.CommsActions.Default();
    /// <summary>Runde 26: Preisgedächtnis für die Abnehmer-Auskunft (nicht gespeichert, nicht gehasht; beginnt mit jedem Universum neu).</summary>
    public SpaceSim.Core.Trading.PriceMemory Prices { get; } = new();
    /// <summary>Runde 38: Einnahmen der Spielerkasse im letzten Spieltag (aus Ereignissen; nicht gespeichert, beginnt mit jedem Universum neu).</summary>
    public PlayerIncome Income { get; private set; } = null!;

    /// <summary>Wird nach jedem Austausch des Universums ausgelöst (neues Spiel, Laden).</summary>
    public event Action? UniverseReplaced;

    float _acc;
    float _rateSeconds; int _rateSteps;
    /// <summary>Runde 27: gemessene Tickrate (Ticks je Echtzeitsekunde, über 2 s gemittelt) — bei ×1 soll sie <see cref="SimClock.TicksPerSecond"/> sein.</summary>
    public float TickRate { get; private set; }

    /// <summary>
    /// Neues Spiel aus der Standardwelt. Phase 14: <paramref name="configure"/> ändert die Weltbeschreibung vor dem Aufbau — der Selbsttest und die
    /// Bildschirmfotos schalten so Treibstoff und Forschung ein (in der Standardwelt aus), damit ihre Abschnitte weiter laufen.
    /// </summary>
    public static GameSession New(Action<WorldDef>? configure = null)
    {
        var s = new GameSession();
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        configure?.Invoke(def);
        s.Use(WorldBuilder.Build(def));
        return s;
    }

    public void Use(Universe u)
    {
        U = u;
        Routes = new RouteRecorder(u);
        Income = new PlayerIncome(u);
        _acc = 0f; _rateSeconds = 0f; _rateSteps = 0; TickRate = 0f;
        UniverseReplaced?.Invoke();
    }

    /// <summary>Lässt die Simulation für <paramref name="delta"/> Sekunden × <paramref name="speed"/> laufen. Gibt die Anzahl Ticks zurück.</summary>
    public int Advance(float delta, float speed)
    {
        _acc += delta * speed;
        _rateSeconds += delta;
        int steps = 0;
        while (_acc >= SimClock.TickInterval && steps < 80) { Runner.Step(U); Prices.Observe(U); _acc -= SimClock.TickInterval; steps++; }
        if (steps == 80) _acc = 0f;   // Rückstand verwerfen statt in eine Spirale zu laufen
        _rateSteps += steps;
        if (_rateSeconds >= 2f) { TickRate = _rateSteps / _rateSeconds; _rateSeconds = 0f; _rateSteps = 0; }
        return steps;
    }

    /// <summary>
    /// Phase 12: lässt eine feste Zahl Ticks ohne Darstellung laufen (Taxifahrt: die Reisezeit vergeht im Zeitraffer), höchstens
    /// <see cref="OfflineCatchUp.DefaultMaxTicks"/>. Gibt die gelaufenen Ticks zurück.
    /// </summary>
    public long RunTicks(long ticks)
    {
        long n = Math.Clamp(ticks, 0, OfflineCatchUp.DefaultMaxTicks);
        for (long i = 0; i < n; i++) { Runner.Step(U); Prices.Observe(U); }
        return n;
    }

    /// <summary>Tick inkl. Bruchteil, für flüssige Positionen zwischen zwei Simulationsschritten.</summary>
    public double FractionalTick => U.Tick + _acc / SimClock.TickInterval;
}
