using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Schiffe der Spielerperson (Phase 5c, erweitert in Phase 12): Alle Schiffe mit Steuerer <see cref="Controller.Player"/> gehören dem Spieler und buchen
/// auf die Spielerkasse; genau eines davon ist das aktive (<see cref="Universe.PlayerShip"/>), das die Ansicht fliegt. Die übrigen liegen angedockt
/// (<see cref="Ship.DockedAt"/>), halten ihre Position im Raum oder führen Befehle aus (<see cref="Commands"/>). Wechsel des aktiven Schiffs: an der
/// Station, an der es liegt (<see cref="SwitchTo"/>), oder aus der Ferne (<see cref="SwitchRemote"/>, UI.md Abschnitt 4 „Schiff wechseln“).
/// Die Person kann das aktive Schiff in einer Station verlassen (<see cref="LeaveShip"/>) und ist dann zu Fuß (<see cref="Universe.PlayerOnFoot"/>);
/// ein Befehl des aktiven Schiffs läuft dabei und nach einem Wechsel weiter — das Boarden beendet ihn nicht mehr.
/// </summary>
public static class PlayerFleet
{
    /// <summary>Höchstabstand eines geparkten Schiffs vom Stationszentrum (= <see cref="Universe.DockRadius"/>).</summary>
    public const float ParkedRadius = Universe.DockRadius;

    /// <summary>Alle Spielerschiffe, nach Id.</summary>
    public static IReadOnlyList<Ship> Owned(Universe u) => u.ShipsById().Where(s => s.IsPlayer).ToList();

    /// <summary>
    /// Eigene, nicht aktive Schiffe, die an dieser Station angedockt liegen. Ist die Person in dieser Station zu Fuß, zählt auch das aktive Schiff
    /// (es liegt dort geparkt, die Person sitzt nicht darin — so lässt es sich über den Hangar wieder betreten).
    /// </summary>
    public static IReadOnlyList<Ship> ParkedAt(Universe u, Station st) =>
        u.ShipsById().Where(s => s.IsPlayer && (s.Id != u.PlayerShip || u.PlayerOnFoot) && s.Transit is null && s.DockedAt == st.Id).ToList();

    /// <summary>
    /// Macht ein eigenes, an dieser Station angedocktes Schiff zum aktiven Schiff. Das bisherige aktive Schiff (angedockt an derselben Station, das
    /// prüft der Aufrufer) bleibt angedockt am Andockpunkt. Veröffentlicht <see cref="ActiveShipChanged"/>. false (mit Grund) = nichts geändert.
    /// </summary>
    public static bool SwitchTo(Universe u, Station st, ShipId id, out string message)
    {
        if (id == u.PlayerShip)
        {
            if (u.PlayerOnFoot && u.Ships.TryGetValue(id, out var back) && back.DockedAt == st.Id && back.Transit is null)
            {
                u.PlayerAtStation = default;   // zurück an Bord des geparkten eigenen Schiffs
                u.Journal.Clear();
                u.Events.Publish(new ActiveShipChanged(id, id, st.Id));
                message = $"Du gehst an Bord von Schiff #{back.Id.Value}" + (u.ShipModels.TryGetValue(back.ModelKey, out var bm) ? $" ({bm.Name})." : ".");
                return true;
            }
            message = "Dieses Schiff ist bereits aktiv."; return false;
        }
        if (!u.Ships.TryGetValue(id, out var target) || !target.IsPlayer) { message = "Das Schiff gehört dir nicht."; return false; }
        if (!ParkedAt(u, st).Contains(target)) { message = "Das Schiff liegt nicht an dieser Station."; return false; }
        var old = u.Ships.GetValueOrDefault(u.PlayerShip);
        if (old is not null && old.Transit is null && old.Sector == st.Sector && (old.DockedAt.Value == 0 || old.DockedAt == st.Id))
            Dock(u, old, st);   // bleibt als Spielerbesitz an der Station angedockt; liegt das bisherige aktive Schiff woanders (Person zu Fuß hier), bleibt es dort
        Activate(u, target, st.Id);
        message = $"Du fliegst jetzt Schiff #{target.Id.Value}" + (u.ShipModels.TryGetValue(target.ModelKey, out var m) ? $" ({m.Name})." : ".");
        return true;
    }

    /// <summary>
    /// Schiffwechsel aus der Ferne (UI.md Abschnitt 4, Runde 45 Nutzeranweisung „in jedes eigene Schiff oder Station“): Der Spieler übernimmt das
    /// gewählte eigene Schiff dort, wo es ist — auch zu Fuß: Die Person reist mit dem Fernwechsel dorthin (angedockt = steht sie in dessen Station,
    /// unterwegs = an Bord); das Taxi bleibt die Alternative. Ein reisendes Schiff
    /// hält an Ort und Stelle; sein Befehl bleibt erhalten — an Bord pausiert er, nach dem Aussteigen läuft er weiter. Das bisherige aktive
    /// Schiff bleibt, wo es ist (angedockt oder im Raum); hat es einen Befehl, führt es ihn weiter, sonst hält es die Position. Leihschiffe gehen nur
    /// an ihrer Station. false (mit Grund) = nichts geändert.
    /// </summary>
    public static bool SwitchRemote(Universe u, ShipId id, out string message)
    {
        if (u.Ships.TryGetValue(id, out var bf) && bf.CarrierHome.Value != 0) { message = "Bordjäger fliegt der Träger — „Jäger zurück“, dann den Träger übernehmen."; return false; }   // Träger
        // Runde 45 (Nutzeranweisung „in jedes eigene Schiff oder Station wechseln“): Der Fernwechsel kennt keine Personen-Sperre — auch zu Fuß
        // übernimmt die Person jedes eigene Schiff dort, wo es ist: angedockt steht sie in dessen Station, unterwegs sitzt sie an Bord.
        // Das bisherige aktive Schiff bleibt, wo es ist; ein Befehl läuft als Flottenschiff weiter. Das Taxi bleibt die Alternative, kein Muss.
        if (u.PlayerOnFoot)
        {
            if (!u.Ships.TryGetValue(id, out var foot) || !foot.IsPlayer) { message = "Das Schiff gehört dir nicht."; return false; }
            var here = u.PlayerAtStation;
            if (id == u.PlayerShip && foot.Transit is null && foot.DockedAt == here)
            {
                u.PlayerAtStation = default;   // Schiff liegt hier: an Bord gehen (wie im Hangar)
                u.Journal.Clear();
                u.Events.Publish(new ActiveShipChanged(id, id, here));
                message = $"Du gehst an Bord von Schiff #{foot.Id.Value}" + (u.ShipModels.TryGetValue(foot.ModelKey, out var fbm) ? $" ({fbm.Name})." : ".");
                return true;
            }
            if (id != u.PlayerShip)
            {
                if (foot.Squadron.Value != 0 && Squadrons.Get(u, foot) is { } fsq && fsq.Leader != foot.Id) Squadrons.Leave(u, foot);
                OrderMove.StopHere(u, foot);   // Reise endet an Ort und Stelle; der Befehl bleibt und läuft nach dem Aussteigen weiter
                foot.Role = ShipRole.Player;
                Activate(u, foot, foot.DockedAt);
                message = $"Du übernimmst Schiff #{foot.Id.Value}" + (u.ShipModels.TryGetValue(foot.ModelKey, out var fm) ? $" ({fm.Name})" : "")
                          + (u.Stations.TryGetValue(foot.DockedAt, out var fst) ? $", angedockt an {fst.Name}." : $" in {OrderMove.SectorName(u, foot.Sector)}.");
                return true;
            }
            // das eigene aktive Schiff liegt woanders: die Person reist mit dem Fernwechsel dorthin (angedockt in seiner Station, sonst an Bord)
            Activate(u, foot, foot.DockedAt);
            message = $"Du gehst an Bord von Schiff #{foot.Id.Value}"
                      + (u.Stations.TryGetValue(foot.DockedAt, out var ast) ? $" — angedockt an {ast.Name}." : " (unterwegs an Bord).");
            return true;
        }
        if (id == u.PlayerShip) { message = "Dieses Schiff ist bereits aktiv."; return false; }
        if (!u.Ships.TryGetValue(id, out var target) || !target.IsPlayer) { message = "Das Schiff gehört dir nicht."; return false; }
        if (target.Squadron.Value != 0 && Squadrons.Get(u, target) is { } sq && sq.Leader != target.Id) Squadrons.Leave(u, target);
        OrderMove.StopHere(u, target);   // laufende Reise endet an Ort und Stelle; der Befehl selbst bleibt und läuft nach dem Aussteigen weiter
        target.Role = ShipRole.Player;
        Activate(u, target, target.DockedAt);
        message = $"Du fliegst jetzt Schiff #{target.Id.Value}" + (u.ShipModels.TryGetValue(target.ModelKey, out var m) ? $" ({m.Name})" : "")
                  + (u.Stations.TryGetValue(target.DockedAt, out var st) ? $", angedockt an {st.Name}." : $" in {OrderMove.SectorName(u, target.Sector)}.");
        return true;
    }

    /// <summary>
    /// Runde 45 (Nutzeranweisung „in jedes eigene Schiff oder Station wechseln“): Die Person wechselt in ihre eigene Station — Hauptquartier-
    /// Sitz, Büro oder gebaute Station — und steht dort zu Fuß (<see cref="Universe.PlayerAtStation"/>). Das aktive Schiff bleibt die Referenz und
    /// bleibt, wo es ist; die Ansicht setzt der Aufruf nach dem Wechsel um (Main). false (mit Grund) = nichts geändert.
    /// </summary>
    public static bool SwitchToStation(Universe u, StationId id, out string message)
    {
        if (!u.Stations.TryGetValue(id, out var st)) { message = "Diese Station gibt es nicht mehr."; return false; }
        if (st.Owner != u.PlayerActor) { message = "Diese Station gehört dir nicht."; return false; }
        u.PlayerAtStation = st.Id;
        u.PlayerSector = st.Sector;
        u.Journal.Clear();
        message = $"Du stehst zu Fuß in {st.Name} ({OrderMove.SectorName(u, st.Sector)}).";
        return true;
    }

    /// <summary>Setzt ein liegendes Schiff angedockt an eine Station (Andockpunkt, gleicher Sektor). Veröffentlicht <see cref="ShipDocked"/> (Runde 42b: Ablieferung).</summary>
    public static void Dock(Universe u, Ship ship, Station st)
    {
        if (ship.Transit is not null) return;
        if (ship.Sector != st.Sector) u.MoveShip(ship, st.Sector);
        ship.Position = st.DockPoint(ship.Id);
        ship.DockedAt = st.Id;
        if (u.Rules.DockDwellTicks > 0 || u.Rules.DockDwellTicksPlayer > 0) ship.DockedSince = u.Tick;   // Liegezeit beginnt
        u.Events.Publish(new ShipDocked(ship.Id, st.Id));   // Runde 42b: Andocken meldet der Bus (z. B. Transport-Ablieferung)
    }

    /// <summary>Das aktive Schiff legt ab (Andocken endet; die Ansicht fliegt es). Runde 39: löscht die Rücknahme im Handel. Zu Fuß: nichts.</summary>
    public static void Undock(Universe u, Ship ship)
    {
        if (u.PlayerOnFoot && ship.Id == u.PlayerShip) return;   // die Person ist an Land — es legt niemand ab
        ship.DockedAt = default;
        if (u.Journal.Last is { } e && e.Ship == ship.Id) u.Journal.Clear();
    }

    /// <summary>Macht ein eigenes Schiff zum aktiven (Sektor der Ansicht folgt) und veröffentlicht <see cref="ActiveShipChanged"/>. Runde 39: Schiffswechsel
    /// löscht die Rücknahme. Das Betreten beendet einen laufenden Befehl nicht mehr (früher: „du übernimmst“): an Bord pausiert er, nach dem
    /// Aussteigen läuft er weiter. Betreten beendet das Zu-Fuß-Sein.</summary>
    public static void Activate(Universe u, Ship target, StationId at)
    {
        var old = u.PlayerShip;
        u.PlayerAtStation = default;   // Betreten eines Schiffs = nicht mehr zu Fuß
        u.PlayerShip = target.Id;
        u.PlayerSector = target.Sector;
        u.Journal.Clear();
        u.Events.Publish(new ActiveShipChanged(old, target.Id, at));
    }

    /// <summary>
    /// Die Person verlässt das aktive Schiff in der Station, an der es angedockt liegt (nur angedockt, nicht unterwegs): Sie ist dann zu Fuß
    /// (<see cref="Universe.PlayerAtStation"/>), das Schiff bleibt geparkt und führt einen laufenden Befehl wie ein Flottenschiff weiter. Zurück
    /// an Bord geht es über den Hangar (<see cref="SwitchTo"/>) oder aus der Ferne (<see cref="SwitchRemote"/>). false (mit Grund) = nichts geändert.
    /// </summary>
    public static bool LeaveShip(Universe u, out string message)
    {
        var ship = u.Ships.GetValueOrDefault(u.PlayerShip);
        if (u.PlayerOnFoot) { message = "Du bist schon zu Fuß in der Station."; return false; }
        if (ship is null || !ship.IsPlayer) { message = "Kein aktives Schiff."; return false; }
        if (ship.Transit is not null) { message = "Unterwegs geht das nicht — erst an einer Station andocken."; return false; }
        if (ship.DockedAt.Value == 0 || !u.Stations.TryGetValue(ship.DockedAt, out var st)) { message = "Du liegst an keiner Station angedockt."; return false; }
        u.PlayerAtStation = st.Id;
        string name = OrderMove.ShipName(u, ship.Id);
        message = $"Du verlässt {name} und bist zu Fuß in {st.Name}." + (ship.Command is null ? "" : " Sein Befehl läuft weiter.");
        return true;
    }
}
