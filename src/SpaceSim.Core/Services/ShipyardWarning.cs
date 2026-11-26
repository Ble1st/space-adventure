using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Services;

/// <summary>
/// Welle B (7): Kaufwarnung der Werft — ein weiteres Schiff läge über der Befehlskapazität (alle freien eigenen Schiffe außer dem aktiven, außerhalb von
/// Verbänden, füllen die Einzelbefehle schon). Nur Hinweis, kein Verbot. Rein.
/// </summary>
public static class ShipyardWarning
{
    public const string Text = "über der Befehlskapazität – Schiff bekommt keinen Einzelbefehl";

    /// <summary>Warnung für einen Kauf durch <paramref name="buyer"/> (null = keine oder Regel aus).</summary>
    public static string? For(Universe u, Ship buyer)
    {
        if (!u.Rules.ShipyardCapacityWarning || !buyer.IsPlayer) return null;
        var actor = buyer.Owner;
        var (cap, _) = Commands.Capacity(u, actor);
        int free = 0;
        foreach (var s in u.Ships.Values)
            if (s.Owner == actor && s.Id != u.PlayerShip && s.Squadron.Value == 0 && s.CarrierHome.Value == 0) free++;
        return free + 1 > cap ? $"{Text} ({Commands.Count(cap, "Einzelbefehl", "Einzelbefehle")} möglich, dann {free + 1} Schiffe)." : null;
    }
}
