using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Spielzeit-Konstanten, gemeinsam für Darstellung und Aufholen.</summary>
public static class SimClock
{
    public const int TicksPerSecond = 4;
    public const float TickInterval = 1f / TicksPerSecond;
}

public readonly record struct CatchUpResult(long TicksRun, long TicksWanted)
{
    public bool Capped => TicksRun < TicksWanted;
}

/// <summary>
/// Holt verpasste Spielzeit nach (Handy war aus/Spiel im Hintergrund): rechnet die Simulation ohne Darstellung schneller durch.
/// Gedeckelt, damit das Öffnen nicht Minuten dauert; darüber hinaus gilt die Welt als "eingefroren".
/// </summary>
public static class OfflineCatchUp
{
    public const long DefaultMaxTicks = 20_000;   // ~83 Minuten Spielzeit, auf Mobilgeräten wenige Sekunden

    public static CatchUpResult Run(Universe u, SimulationRunner runner, TimeSpan elapsed, long maxTicks = DefaultMaxTicks)
    {
        long wanted = (long)Math.Max(0, elapsed.TotalSeconds * SimClock.TicksPerSecond);
        long run = Math.Min(wanted, maxTicks);
        for (long i = 0; i < run; i++) runner.Step(u);
        return new CatchUpResult(run, wanted);
    }
}
