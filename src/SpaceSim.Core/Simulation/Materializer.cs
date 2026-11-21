using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Momentaufnahme eines Schiffs für die Darstellung. Direction ist normiert.</summary>
public readonly record struct ShipSnapshot(ShipId Id, ShipRole Role, Vec2 Position, Vec2 Direction, bool Moving, bool Mining = false);

/// <summary>
/// Leitet aus dem abstrakten Zustand (Fahrplan, Ankunftspositionen) ab, welche Schiffe zu einem
/// (auch gebrochenen) Zeitpunkt wo in einem Sektor sind. Engine-unabhängig; die Darstellung rendert nur.
/// </summary>
public static class Materializer
{
    public static List<ShipSnapshot> Snapshot(Universe u, SectorId sector, double tick) => Snapshot(u, sector, tick, null);

    /// <summary>
    /// Runde 12: <paramref name="playerHeading"/> = Blickrichtung des aktiven Spielerschiffs (nur die Ansicht kennt sie) — Begleiter eines Verbands, den der Spieler
    /// führt, stehen damit in Formation um ihn (vorher liefen sie zur selben Stelle und überlagerten sich).
    /// </summary>
    public static List<ShipSnapshot> Snapshot(Universe u, SectorId sector, double tick, Vec2? playerHeading)
    {
        var result = new List<ShipSnapshot>();

        foreach (var id in u.Tracker.ParkedIn(sector))
        {
            var ship = u.Ships[id];
            if (ship.Id == u.PlayerShip && !u.PlayerOnFoot) continue;   // das aktive Spielerschiff rendert die Ansicht selbst; geparkte eigene Schiffe erscheinen (Phase 5c) — zu Fuß (Person in der Station, Schiff geparkt) erscheint es auch
            // Phase 25: Teilnehmer eines räumlichen Gefechts stehen an ihrer echten Gefechtsposition (zwischen zwei Ticks interpoliert) und blicken, wohin sie zielen
            if (u.Fights.Of(id) is { Spatial: true } fight && fight.Of(id) is { } p)
            {
                float f = (float)Math.Clamp(tick - u.Tick, 0.0, 1.0);
                result.Add(new ShipSnapshot(ship.Id, ship.Role, Vec2.Lerp(p.PrevPos, p.Pos, f), p.Dir, true));
                continue;
            }
            result.Add(new ShipSnapshot(ship.Id, ship.Role, ship.Position, new Vec2(0, -1), false,
                Mining: ship.Role == ShipRole.Miner && ship.MinePhase == MinePhase.Mining));
        }
        foreach (var id in u.Tracker.TransitIn(sector))
        {
            var ship = u.Ships[id];
            if (ship.Id == u.PlayerShip || ship.Transit is null) continue;
            foreach (var leg in ship.Transit.Legs)
            {
                if (leg.Sector != sector || tick < leg.StartTick || tick >= leg.EndTick) continue;
                float f = (float)((tick - leg.StartTick) / (double)(leg.EndTick - leg.StartTick));
                var pos = Vec2.Lerp(leg.From, leg.To, f);
                var dir = (leg.To - leg.From).Normalized();
                result.Add(new ShipSnapshot(ship.Id, ship.Role, pos, dir, true));
                break;
            }
        }
        if (u.Squadrons.Count > 0) ApplyFormations(u, result, playerHeading);
        return result;
    }

    /// <summary>
    /// Phase 12: Formation der Verbände (nur Darstellung): Mitglieder nahe ihrem Anführer werden um ihn versetzt (Richtung = Flugrichtung des
    /// Anführers). Die Simulation bleibt unberührt.
    /// </summary>
    static void ApplyFormations(Universe u, List<ShipSnapshot> snaps, Vec2? playerHeading)
    {
        var index = new Dictionary<ShipId, int>();
        for (int i = 0; i < snaps.Count; i++) index[snaps[i].Id] = i;
        foreach (var sq in u.Squadrons.Values.OrderBy(s => s.Id.Value))
        {
            ShipSnapshot lead;
            if (index.TryGetValue(sq.Leader, out int li)) lead = snaps[li];
            else if (sq.Leader == u.PlayerShip && playerHeading is { } ph && u.Ships.TryGetValue(sq.Leader, out var me) && me.Transit is null)
                lead = new ShipSnapshot(me.Id, me.Role, me.Position, ph, false);   // Runde 12: der Spieler führt
            else continue;
            float gap = Squadrons.FormationGap(u, sq);
            for (int m = 1; m < sq.Members.Count; m++)
            {
                if (!index.TryGetValue(sq.Members[m], out int mi)) continue;
                var s = snaps[mi];
                if (Vec2.Distance(s.Position, lead.Position) > 30f + gap * m) continue;
                snaps[mi] = s with { Position = lead.Position + Squadrons.Offset(sq.Formation, m, lead.Direction, gap), Direction = lead.Direction, Moving = lead.Moving };
            }
        }
    }
}
