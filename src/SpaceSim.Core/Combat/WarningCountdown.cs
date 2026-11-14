using System.Text.RegularExpressions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Runde 21 (Handy-Test c01_w1..w3: „Angriff in 15 s“ stand über drei Bilder im 5-s-Abstand gleich): Restzeit einer Vorwarnung an den Spieler aus
/// <see cref="PendingEncounter.DueTick"/> und der Warntext mit heruntergezählter Sekundenangabe. Rein, ohne Zustand.
/// </summary>
public static class WarningCountdown
{
    static readonly Regex Paren = new(@"\((\d+) s\)", RegexOptions.CultureInvariant);
    static readonly Regex In = new(@"in (\d+) s\b", RegexOptions.CultureInvariant);

    /// <summary>Ganze Sekunden bis zur Fälligkeit (aufgerundet, mindestens 0).</summary>
    public static int SecondsLeft(long dueTick, long tick) => (int)Math.Max(0, (dueTick - tick + SimClock.TicksPerSecond - 1) / SimClock.TicksPerSecond);

    /// <summary>Die laufende Vorwarnung an <paramref name="target"/> mit der gegebenen Fälligkeit, sonst null (aufgelöst, entkommen).</summary>
    public static PendingEncounter? Pending(Universe u, ShipId target, long dueTick) =>
        u.PendingEncounters.FirstOrDefault(p => p.Target == target && p.DueTick == dueTick);

    /// <summary>Warntext mit der Restzeit: „(15 s)“ bzw. „in 15 s“ wird zu <paramref name="seconds"/>; ein Text ohne Zeitangabe bleibt.</summary>
    public static string Apply(string message, int seconds)
    {
        string s = Paren.Replace(message, $"({seconds} s)", 1);
        return ReferenceEquals(s, message) || s == message ? In.Replace(message, $"in {seconds} s", 1) : s;
    }
}
