using System;
using System.Collections.Generic;
using System.Linq;
using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Debug-Menü (Nutzerwunsch: „Schiffe und Stationen für meine Fraktion zugehörig für Testzwecke spawnen, sowie Waren“): erzeugt ohne Kosten und ohne
/// Material eigene Schiffe, fertige eigene Stationen und Waren in einem eigenen Schiff, und setzt die Kasse. Nur für Tests — das Spiel ruft es nur über
/// den Eintrag „Debug“ im Hamburger-Menü auf. Rein über die üblichen Wege (<see cref="Universe.AddShip"/>, <see cref="Universe.AddStation"/>), damit
/// Tracker, Ereignisse und Sektorkontrolle stimmen.
/// </summary>
public static class DebugSpawn
{
    /// <summary>Modelle, die gespawnt werden können (alle bekannten, nach Name).</summary>
    public static List<ShipModel> ShipModels(Universe u) =>
        u.ShipModels.Values.Where(m => u.ShipClasses.ContainsKey(m.ClassKey)).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

    /// <summary>Stationstypen, die gespawnt werden können (alles außer Baustellen), nach Name.</summary>
    public static List<StationType> StationTypes(Universe u) =>
        u.StationTypes.Values.Where(t => t.Key != "build_site").OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

    /// <summary>Wo die Person gerade ist: Station (angedockt/zu Fuß) oder die Lage des aktiven Schiffs.</summary>
    static (SectorId Sector, Vec2 Pos, Station? At) Here(Universe u)
    {
        if (u.PlayerOnFoot && u.Stations.TryGetValue(u.PlayerAtStation, out var shore)) return (shore.Sector, shore.Position, shore);
        var me = u.Ships.GetValueOrDefault(u.PlayerShip);
        if (me is null) return (u.PlayerSector, default, null);
        if (u.Stations.TryGetValue(me.DockedAt, out var dock)) return (dock.Sector, dock.Position, dock);
        return (me.Sector, me.Position, null);
    }

    /// <summary>
    /// Ein eigenes Schiff des Modells: liegt die Person an einer Station, angedockt dort (im Hangar); sonst neben dem aktiven Schiff im Raum.
    /// Voller Tank und Rumpf, keine Kosten. null mit Grund, wenn das Modell fehlt.
    /// </summary>
    public static Ship? SpawnShip(Universe u, string modelKey, out string message)
    {
        if (!u.ShipModels.TryGetValue(modelKey, out var m) || !u.ShipClasses.TryGetValue(m.ClassKey, out var cls))
        { message = $"Unbekanntes Modell „{modelKey}“."; return null; }
        var (sector, pos, at) = Here(u);
        var anchor = at ?? u.StationsById().Where(s => s.Sector == sector).OrderBy(s => Vec2.Distance(s.Position, pos)).FirstOrDefault();
        var id = u.NewShipId();
        Ship ship;
        if (anchor is not null)
            ship = ShipPurchase.Create(u, id, modelKey, cls.Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, anchor, 0f);
        else
        {
            ship = new Ship { Id = id, Role = ShipRole.Player, Controller = Controller.Player, Owner = u.PlayerActor, Sector = sector, ModelKey = modelKey,
                Slots = cls.Slots.Select(s => new ClassSlot { Type = s.Type, Size = s.Size, Facing = s.Facing, Arc = s.Arc }).ToList(), Fit = new List<string?>(m.Fit) };
            if (ship.RecomputeStats(u)) { ship.CargoCapacity = ship.Stats.Cargo; ship.Hull = ship.MaxHull; ship.FillFuel(); }
        }
        if (at is null)
        {
            // im Raum: neben dem aktiven Schiff, nicht angedockt
            float a = (id.Value % 16) * MathF.PI / 8f;
            ship.Position = pos + new Vec2(MathF.Cos(a), MathF.Sin(a)) * 18f;
            ship.Sector = sector;
            ship.DockedAt = default;
        }
        ship.HomeBase = anchor?.Id ?? default;
        u.AddShip(ship);
        u.Events.Publish(new ShipPurchased(id, ship.DockedAt, modelKey, 0f));
        message = $"Debug: {m.Name} #{id.Value} gespawnt" + (at is not null ? $" (angedockt an {at.Name})." : " (neben dir im Raum).");
        return ship;
    }

    /// <summary>
    /// Eine fertige eigene Station des Typs in der Nähe (gleicher Sektor, freier Platz wie beim Hauptquartier). Startkasse 10.000 Cr, keine Kosten.
    /// null mit Grund, wenn der Typ fehlt oder kein Platz frei ist.
    /// </summary>
    public static Station? SpawnStation(Universe u, string typeKey, out string message)
    {
        if (!u.StationTypes.TryGetValue(typeKey, out var t)) { message = $"Unbekannter Stationstyp „{typeKey}“."; return null; }
        var (sector, pos, _) = Here(u);
        if (!u.Sectors.TryGetValue(sector, out var sec)) { message = "Kein Sektor."; return null; }
        var spot = Outposts.FreeSpot(u, sec, pos, t.Radius, t.Radius + 25f, 400f);
        if (spot is null) { message = "Kein freier Platz in der Nähe."; return null; }
        var id = u.NewStationId();
        var st = StationFactory.Create(u, id, t, StationFactory.BuiltName(u, t, sector), sector, spot.Value, u.PlayerActor);
        u.AddStation(st);
        u.Events.Publish(new StationBuilt(id, sector, t.Key));
        if (SectorControl.Update(u, sec)) u.TouchAccess();
        message = $"Debug: {st.Name} gespawnt ({sec.Name}).";
        return st;
    }

    /// <summary>Waren in den Laderaum eines eigenen Schiffs (höchstens so viel, wie hineinpasst). Gibt die eingeladene Menge zurück.</summary>
    public static float AddCargo(Universe u, Ship ship, GoodId good, float units, out string message)
    {
        if (!u.Goods.TryGetValue(good, out var g)) { message = "Unbekannte Ware."; return 0f; }
        float n = MathF.Min(units, MathF.Floor(ship.FreeUnits(u, good)));
        if (n <= 0f) { message = $"Kein Platz für {g.Name} in #{ship.Id.Value}."; return 0f; }
        ship.Cargo[good] = ship.Cargo.GetValueOrDefault(good) + n;
        message = $"Debug: {n:0} {g.Name} in #{ship.Id.Value} geladen.";
        return n;
    }

    /// <summary>Setzt die Spielerkasse.</summary>
    public static void SetCredits(Universe u, float credits) => u.Player.Treasury.Credits = credits;
}
