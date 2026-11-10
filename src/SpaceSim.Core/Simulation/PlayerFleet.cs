using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Schiffe der Spielerperson (Phase 5c): Alle Schiffe mit Steuerer <see cref="Controller.Player"/> gehören dem Spieler und buchen auf
/// die Spielerkasse; genau eines davon ist das aktive (<see cref="Universe.PlayerShip"/>), das die Ansicht fliegt. Die übrigen liegen
/// geparkt an Stationen (am Andockpunkt), werden von keinem KI-System bewegt (Rolle Player) und lassen sich an der Station, an der sie
/// liegen, zum aktiven Schiff machen.
/// </summary>
public static class PlayerFleet
{
    /// <summary>Höchstabstand eines geparkten Schiffs vom Stationszentrum, damit es als „an dieser Station“ gilt (Andockpunkt liegt bei 22).</summary>
    public const float ParkedRadius = 30f;

    /// <summary>Alle Spielerschiffe, nach Id.</summary>
    public static IReadOnlyList<Ship> Owned(Universe u) => u.Ships.Values.Where(s => s.IsPlayer).OrderBy(s => s.Id.Value).ToList();

    /// <summary>Eigene, nicht aktive Schiffe, die an dieser Station geparkt liegen (kein Transit, gleicher Sektor, am Andockpunkt).</summary>
    public static IReadOnlyList<Ship> ParkedAt(Universe u, Station st) =>
        u.Ships.Values.Where(s => s.IsPlayer && s.Id != u.PlayerShip && s.Transit is null && s.Sector == st.Sector
                                  && Vec2.Distance(s.Position, st.Position) <= ParkedRadius)
            .OrderBy(s => s.Id.Value).ToList();

    /// <summary>
    /// Macht ein eigenes, an dieser Station geparktes Schiff zum aktiven Schiff. Das bisherige aktive Schiff (angedockt an derselben
    /// Station, das prüft der Aufrufer) wird am Andockpunkt geparkt. Veröffentlicht <see cref="ActiveShipChanged"/>. false (mit Grund) =
    /// nichts geändert.
    /// </summary>
    public static bool SwitchTo(Universe u, Station st, ShipId id, out string message)
    {
        if (id == u.PlayerShip) { message = "Dieses Schiff ist bereits aktiv."; return false; }
        if (!u.Ships.TryGetValue(id, out var target) || !target.IsPlayer) { message = "Das Schiff gehört dir nicht."; return false; }
        if (!ParkedAt(u, st).Contains(target)) { message = "Das Schiff liegt nicht an dieser Station."; return false; }
        var old = u.Ships.GetValueOrDefault(u.PlayerShip);
        if (old is not null && old.Transit is null)
        {
            if (old.Sector != st.Sector) u.MoveShip(old, st.Sector);
            old.Position = st.DockPoint(old.Id);   // bleibt als Spielerbesitz an der Station geparkt
        }
        u.PlayerShip = target.Id;
        u.PlayerSector = target.Sector;
        u.Events.Publish(new ActiveShipChanged(old?.Id ?? default, target.Id, st.Id));
        message = $"Du fliegst jetzt Schiff #{target.Id.Value}" + (u.ShipModels.TryGetValue(target.ModelKey, out var m) ? $" ({m.Name})." : ".");
        return true;
    }
}
