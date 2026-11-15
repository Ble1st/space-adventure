using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 30: Andockregel der Stationsgrößen — eine Station nimmt Schiffe bis zur Klasse <see cref="StationSizes.MaxShip"/> ihrer Größe auf (S: bis M, M: bis L,
/// L/XL: alle; Kleinstationen XS: keine). Baustellen nehmen jedes Schiff (Belieferung). Schiffe ohne Modell (alte Welten) dürfen überall andocken.
/// Dieselbe Regel für Spieler-Andocken und die Zielwahl der KI (Handel, Bergbau, Tanken, Reparatur), damit kein NPC ein unzulässiges Ziel plant.
/// </summary>
public static class Docking
{
    /// <summary>Größenklasse des Schiffs (null = ohne Modell).</summary>
    public static SlotSize? SizeOf(Universe u, Ship ship) =>
        u.ShipModels.TryGetValue(ship.ModelKey, out var m) && u.ShipClasses.TryGetValue(m.ClassKey, out var c) ? c.Size : null;

    /// <summary>Darf das Schiff an der Station andocken?</summary>
    public static bool Allows(Universe u, Station st, Ship ship) => Allows(st, SizeOf(u, ship));

    /// <summary>Schnelle Form für Planer-Schleifen (Schiffsgröße einmal bestimmt): andockbar und die Größe passt (Baustellen nehmen jede).</summary>
    public static bool Allows(Station st, SlotSize? size) =>
        st.Dockable && (st.IsBuildSite || size is not { } sz || st.MaxShip is not { } max || sz <= max);

    /// <summary>Grund, warum das Schiff nicht andocken kann (null = es kann).</summary>
    public static string? Issue(Universe u, Station st, Ship ship)
    {
        if (!st.Dockable) return "Keine Andockmöglichkeit (Kleinstation)";
        // Runde 40: im Krieg mit der Stationsfraktion verweigert sie das Andocken (auch im Waffenstillstand)
        if (ship.IsPlayer && u.FactionOf(st) is { } wf && Wars.AtWar(u, u.PlayerActor, wf))
            return $"Andocken verweigert: Krieg mit {(u.Factions.TryGetValue(wf, out var wfac) ? wfac.Name : "der Fraktion")}.";
        if (Allows(u, st, ship)) return null;
        var t = u.StationTypes[st.TypeKey];
        return $"Station zu klein für dieses Schiff ({StationName(t.Size)} nimmt Schiffe bis {StationSizes.MaxShip(t.Size)})";
    }

    static string StationName(StationSize s) => s switch { StationSize.S => "kleine Station", StationSize.M => "mittlere Station", StationSize.L => "große Station", _ => "Station" };
}
