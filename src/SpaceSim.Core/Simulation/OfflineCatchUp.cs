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
        var job = Begin(u, elapsed, maxTicks);
        try { while (!job.Done) job.Step(runner, long.MaxValue); }
        finally { job.Finish(); }
        return job.Result;
    }

    /// <summary>
    /// Phase 28 (Prüfpunkt f, Startzeit): Aufholen in Teilen — die Ansicht rechnet je Bild nur einen Teil (Fortschrittsanzeige statt minutenlangem Splash).
    /// Gleiches Ergebnis wie <see cref="Run"/>: dieselben Vorbereitungen (keine Überfälle auf den Spieler, Gefecht des Spielers beendet, Brand gelöscht), dieselben
    /// Ticks in derselben Reihenfolge; <see cref="CatchUpJob.Finish"/> stellt die Regel wieder her (auch bei Abbruch vor dem Ende).
    /// </summary>
    public static CatchUpJob Begin(Universe u, TimeSpan elapsed, long maxTicks = DefaultMaxTicks)
    {
        long wanted = (long)Math.Max(0, elapsed.TotalSeconds * SimClock.TicksPerSecond);
        long run = Math.Min(wanted, maxTicks);
        // Phase 11: Wer nicht spielt, kann nicht auf eine Vorwarnung reagieren — während des Nachholens überfällt niemand den Spieler
        // (offene Ankündigungen entfallen; NPC-Überfälle laufen weiter)
        var job = new CatchUpJob(u, run, wanted, u.Rules.PlayerRaids);
        u.Rules.PlayerRaids = false;
        u.PendingEncounters.RemoveAll(e => e.Kind == EncounterKind.Raid || e.Station.Value != 0);   // Runde 13b: auch Warnungen der Waffenstellungen
        if (run > 0) Combat.PlayerCombat.Withdraw(u);   // Phase 25: ein laufendes Gefecht des Spielers endet (er kann offline nicht reagieren)
        // Phase 26 (Prüfung): offline keine Reaktion möglich — ein Brand am aktiven Schiff wird vor dem Aufholen gelöscht (sonst fräße er unbemerkt)
        if (run > 0 && u.Ships.TryGetValue(u.PlayerShip, out var active)) Combat.ModuleDamage.ExtinguishAll(u, active, "offline");
        return job;
    }
}

/// <summary>Phase 28: laufendes Aufholen (siehe <see cref="OfflineCatchUp.Begin"/>).</summary>
public sealed class CatchUpJob
{
    readonly Universe _u;
    readonly bool _raids;
    bool _finished;

    internal CatchUpJob(Universe u, long total, long wanted, bool raids) { _u = u; Total = total; Wanted = wanted; _raids = raids; }

    public long Total { get; }
    public long Wanted { get; }
    public long Ran { get; private set; }
    public bool Done => Ran >= Total;
    public float Progress => Total <= 0 ? 1f : (float)Ran / Total;
    public CatchUpResult Result => new(Ran, Wanted);

    /// <summary>Höchstens <paramref name="maxTicks"/> weitere Ticks rechnen (bis zum Ende). Liefert die gerechneten Ticks.</summary>
    public long Step(SimulationRunner runner, long maxTicks)
    {
        if (_finished) return 0;
        long n = Math.Min(maxTicks, Total - Ran);
        for (long i = 0; i < n; i++) { runner.Step(_u); Ran++; }
        return n;
    }

    /// <summary>Regel wiederherstellen (am Ende oder bei Abbruch, z. B. Speichern vor dem Ende — der Rest verfällt).</summary>
    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        _u.Rules.PlayerRaids = _raids;
    }
}
