using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Schiffe der Spielerperson (Phase 5c, erweitert in Phase 12): Alle Schiffe mit Steuerer <see cref="Controller.Player"/> gehören dem Spieler und buchen
/// auf die Spielerkasse; genau eines davon ist das aktive (<see cref="Universe.PlayerShip"/>), das die Ansicht fliegt. Die übrigen liegen angedockt
/// (<see cref="Ship.DockedAt"/>), halten ihre Position im Raum oder führen Befehle aus (<see cref="Commands"/>). Wechsel des aktiven Schiffs: an der
/// Station, an der es liegt (<see cref="SwitchTo"/>), oder aus der Ferne (<see cref="SwitchRemote"/>, UI.md Abschnitt 4 „Schiff wechseln“).
/// </summary>
public static class PlayerFleet
{
    /// <summary>Höchstabstand eines geparkten Schiffs vom Stationszentrum (= <see cref="Universe.DockRadius"/>).</summary>
    public const float ParkedRadius = Universe.DockRadius;

    /// <summary>Alle Spielerschiffe, nach Id.</summary>
    public static IReadOnlyList<Ship> Owned(Universe u) => u.ShipsById().Where(s => s.IsPlayer).ToList();

    /// <summary>Eigene, nicht aktive Schiffe, die an dieser Station angedockt liegen.</summary>
    public static IReadOnlyList<Ship> ParkedAt(Universe u, Station st) =>
        u.ShipsById().Where(s => s.IsPlayer && s.Id != u.PlayerShip && s.Transit is null && s.DockedAt == st.Id).ToList();

    /// <summary>
    /// Macht ein eigenes, an dieser Station angedocktes Schiff zum aktiven Schiff. Das bisherige aktive Schiff (angedockt an derselben Station, das
    /// prüft der Aufrufer) bleibt angedockt am Andockpunkt. Veröffentlicht <see cref="ActiveShipChanged"/>. false (mit Grund) = nichts geändert.
    /// </summary>
    public static bool SwitchTo(Universe u, Station st, ShipId id, out string message)
    {
        if (id == u.PlayerShip) { message = "Dieses Schiff ist bereits aktiv."; return false; }
        if (!u.Ships.TryGetValue(id, out var target) || !target.IsPlayer) { message = "Das Schiff gehört dir nicht."; return false; }
        if (!ParkedAt(u, st).Contains(target)) { message = "Das Schiff liegt nicht an dieser Station."; return false; }
        var old = u.Ships.GetValueOrDefault(u.PlayerShip);
        if (old is not null && old.Transit is null) Dock(u, old, st);   // bleibt als Spielerbesitz an der Station angedockt
        Activate(u, target, st.Id);
        message = $"Du fliegst jetzt Schiff #{target.Id.Value}" + (u.ShipModels.TryGetValue(target.ModelKey, out var m) ? $" ({m.Name})." : ".");
        return true;
    }

    /// <summary>
    /// Schiffwechsel aus der Ferne (UI.md Abschnitt 4, entschieden): Der Spieler übernimmt das gewählte eigene Schiff dort, wo es ist. Ein reisendes Schiff
    /// hält an Ort und Stelle; sein Befehl endet (der Spieler steuert selbst). Das bisherige aktive Schiff bleibt, wo es ist (angedockt oder im Raum) und
    /// hält ohne Befehl die Position. Leihschiffe gehen nur an ihrer Station. false (mit Grund) = nichts geändert.
    /// </summary>
    public static bool SwitchRemote(Universe u, ShipId id, out string message)
    {
        if (id == u.PlayerShip) { message = "Dieses Schiff ist bereits aktiv."; return false; }
        if (!u.Ships.TryGetValue(id, out var target) || !target.IsPlayer) { message = "Das Schiff gehört dir nicht."; return false; }
        if (target.Command is not null) Commands.Cancel(u, target, "du übernimmst");
        if (target.Squadron.Value != 0 && Squadrons.Get(u, target) is { } sq && sq.Leader != target.Id) Squadrons.Leave(u, target);
        OrderMove.StopHere(u, target);
        target.Role = ShipRole.Player;
        Activate(u, target, target.DockedAt);
        message = $"Du fliegst jetzt Schiff #{target.Id.Value}" + (u.ShipModels.TryGetValue(target.ModelKey, out var m) ? $" ({m.Name})" : "")
                  + (u.Stations.TryGetValue(target.DockedAt, out var st) ? $", angedockt an {st.Name}." : $" in {OrderMove.SectorName(u, target.Sector)}.");
        return true;
    }

    /// <summary>Setzt ein liegendes Schiff angedockt an eine Station (Andockpunkt, gleicher Sektor).</summary>
    public static void Dock(Universe u, Ship ship, Station st)
    {
        if (ship.Transit is not null) return;
        if (ship.Sector != st.Sector) u.MoveShip(ship, st.Sector);
        ship.Position = st.DockPoint(ship.Id);
        ship.DockedAt = st.Id;
    }

    /// <summary>Das aktive Schiff legt ab (Andocken endet; die Ansicht fliegt es). Runde 39: löscht die Rücknahme im Handel.</summary>
    public static void Undock(Universe u, Ship ship)
    {
        ship.DockedAt = default;
        if (u.Journal.Last is { } e && e.Ship == ship.Id) u.Journal.Clear();
    }

    /// <summary>Macht ein eigenes Schiff zum aktiven (Sektor der Ansicht folgt) und veröffentlicht <see cref="ActiveShipChanged"/>. Runde 39: Schiffswechsel löscht die Rücknahme.</summary>
    public static void Activate(Universe u, Ship target, StationId at)
    {
        var old = u.PlayerShip;
        if (target.Command is not null) Commands.Cancel(u, target, "du übernimmst");
        u.PlayerShip = target.Id;
        u.PlayerSector = target.Sector;
        u.Journal.Clear();
        u.Events.Publish(new ActiveShipChanged(old, target.Id, at));
    }
}
