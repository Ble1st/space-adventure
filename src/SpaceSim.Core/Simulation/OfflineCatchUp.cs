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
    public const long DefaultMaxTicks = 10_000;   // ~42 Minuten Spielzeit; bei ~0,3 ms/Tick am Desktop gut 3 s, auf Mobilgeräten entsprechend länger

    public static CatchUpResult Run(Universe u, SimulationRunner runner, TimeSpan elapsed, long maxTicks = DefaultMaxTicks)
    {
        long wanted = (long)Math.Max(0, elapsed.TotalSeconds * SimClock.TicksPerSecond);
        long run = Math.Min(wanted, maxTicks);
        // Phase 11: Wer nicht spielt, kann nicht auf eine Vorwarnung reagieren — während des Nachholens überfällt niemand den Spieler
        // (offene Ankündigungen entfallen; NPC-Überfälle laufen weiter)
        bool raids = u.Rules.PlayerRaids;
        u.Rules.PlayerRaids = false;
        u.PendingEncounters.RemoveAll(e => e.Kind == EncounterKind.Raid);
        if (run > 0) Combat.PlayerCombat.Withdraw(u);   // Phase 25: ein laufendes Gefecht des Spielers endet (er kann offline nicht reagieren)
        try { for (long i = 0; i < run; i++) runner.Step(u); }
        finally { u.Rules.PlayerRaids = raids; }
        return new CatchUpResult(run, wanted);
    }
}
