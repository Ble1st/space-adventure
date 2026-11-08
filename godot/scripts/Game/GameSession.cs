using System;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
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

    /// <summary>Wird nach jedem Austausch des Universums ausgelöst (neues Spiel, Laden).</summary>
    public event Action? UniverseReplaced;

    float _acc;

    public static GameSession New()
    {
        var s = new GameSession();
        s.Use(WorldBuilder.BuildDefault());
        return s;
    }

    public void Use(Universe u)
    {
        U = u;
        Routes = new RouteRecorder(u);
        _acc = 0f;
        UniverseReplaced?.Invoke();
    }

    /// <summary>Lässt die Simulation für <paramref name="delta"/> Sekunden × <paramref name="speed"/> laufen. Gibt die Anzahl Ticks zurück.</summary>
    public int Advance(float delta, float speed)
    {
        _acc += delta * speed;
        int steps = 0;
        while (_acc >= SimClock.TickInterval && steps < 80) { Runner.Step(U); _acc -= SimClock.TickInterval; steps++; }
        if (steps == 80) _acc = 0f;   // Rückstand verwerfen statt in eine Spirale zu laufen
        return steps;
    }

    /// <summary>Tick inkl. Bruchteil, für flüssige Positionen zwischen zwei Simulationsschritten.</summary>
    public double FractionalTick => U.Tick + _acc / SimClock.TickInterval;
}
