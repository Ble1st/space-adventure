using System.Globalization;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Diagnostics;

/// <summary>Lücken gegenüber IP2: Zeilen des Statistik-Bildschirms (rein, aus <see cref="PlayerStats"/>; ohne Statistik alles 0).</summary>
public static class PlayerStatsInfo
{
    static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    static string Cr(double v) => v.ToString("#,0", De) + " Cr";
    static string N(double v) => v.ToString("#,0.#", De);

    /// <summary>Spielzeit aus dem Tick (Spielwelt-Zeit, Zeitraffer zählt mit).</summary>
    public static string PlayTime(Universe u)
    {
        long sec = u.Tick / SimClock.TicksPerSecond;
        return $"{sec / 3600}:{sec / 60 % 60:00}:{sec % 60:00}";
    }

    public static List<(string Label, string Value)> Rows(Universe u)
    {
        var s = u.Actors.TryGetValue(u.PlayerActor, out var me) ? me.Stats ?? new PlayerStats() : new PlayerStats();
        double wallet = me?.Treasury.Credits ?? 0;
        return new()
        {
            ("Spielzeit", PlayTime(u)),
            ("Kasse jetzt", Cr(wallet)),
            ("Höchste Kasse", Cr(Math.Max(s.MaxWallet, wallet))),
            ("Einnahmen gesamt", Cr(s.Earned)),
            ("Ausgaben gesamt", Cr(s.Spent)),
            ("Handel: Erlös aus Verkäufen", Cr(s.TradeRevenue)),
            ("Handel: Ausgaben für Einkäufe", Cr(s.TradeSpent)),
            ("Handel: Verkäufe / Einkäufe", $"{s.Sales} / {s.Purchases} · {N(s.TradeUnits)} Einheiten"),
            ("Erz abgebaut", $"{N(s.OreMined)} Einheiten"),
            ("Fracht umgeladen", $"{N(s.Transferred)} Einheiten"),
            ("Abschüsse", s.Kills.ToString(De)),
            ("Eigene Schiffe verloren", s.ShipsLost.ToString(De)),
            ("Kopfgelder", $"{s.Bounties} · {Cr(s.BountyCredits)}"),
            ("Aufträge erfüllt / gescheitert", $"{s.MissionsDone} / {s.MissionsFailed}"),
            ("Sektorwechsel", s.Jumps.ToString(De)),
        };
    }
}
