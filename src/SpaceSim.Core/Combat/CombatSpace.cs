using SpaceSim.Core.Model;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 25 (KAMPF.md 4.2, 4.3, Risiko „Positionen im Spielersektor“): ein Gefechtsmodell, zwei Darstellungen. Außerhalb des Spielersektors führt das Gefecht
/// je Teilnehmer die Abstandszahl <see cref="CombatParticipant.X"/> (Phase 24); im Spielersektor echte, gespeicherte Positionen
/// (<see cref="CombatParticipant.Pos"/>/<see cref="CombatParticipant.Dir"/>), die die Simulation über den <see cref="CombatAutopilot"/> fortschreibt und auf die
/// Schiffsposition überträgt. Übergang, wenn der Spieler den Sektor betritt oder verlässt (<see cref="Sync"/>, vor jedem Gefechtsschritt):
/// <list type="bullet">
/// <item>Linie → Raum: Bezug ist der erste aktive Verteidiger an seiner echten Position; die Achse zeigt zur echten Position seines nächsten Gegners; jeder
/// Teilnehmer steht auf dieser Achse im Linienabstand zum Bezug (Abstände zum Bezug bleiben erhalten), Blick zum nächsten Gegner.</item>
/// <item>Raum → Linie: Projektion auf die Achse vom Bezug zu seinem nächsten Gegner (dieser Abstand bleibt exakt erhalten); die Schiffe bleiben an ihrer
/// letzten echten Position stehen.</item>
/// </list>
/// Beide Übergänge ziehen keine Zufallszahl. Ein Gefecht ist also immer genau ein Objekt mit genau einem Ergebnis; nur die Geometrie wechselt.
/// </summary>
public static class CombatSpace
{
    /// <summary>Soll dieses Gefecht räumlich laufen (Sektor des Spielers, Regel <see cref="EconomyRules.SpatialCombat"/>)?</summary>
    public static bool WantsSpace(Universe u, Encounter e) => u.Rules.SpatialCombat && e.Sector == u.PlayerSector;

    /// <summary>Bringt die Geometrie des Gefechts in Einklang mit dem Spielersektor (Übergang Linie ↔ Raum, siehe Klassenbeschreibung).</summary>
    public static void Sync(Universe u, Encounter e)
    {
        bool want = WantsSpace(u, e);
        if (want == e.Spatial) return;
        if (want) ToSpace(u, e); else ToLine(u, e);
    }

    /// <summary>Setzt das abgeleitete <see cref="CombatParticipant.InSpace"/> nach dem Laden.</summary>
    public static void Derive(Encounter e)
    {
        foreach (var p in e.Participants) { p.InSpace = e.Spatial; p.PrevPos = p.Pos; }
    }

    static CombatParticipant? Anchor(Encounter e)
    {
        foreach (var p in e.Participants) if (p.Active && p.Side == 1) return p;
        foreach (var p in e.Participants) if (p.Active) return p;
        return null;
    }

    static CombatParticipant? NearestOnLine(Encounter e, CombatParticipant self)
    {
        CombatParticipant? best = null;
        foreach (var p in e.Participants)
            if (p.Active && p.Side != self.Side && (best is null || MathF.Abs(p.X - self.X) < MathF.Abs(best.X - self.X))) best = p;
        return best;
    }

    static void ToSpace(Universe u, Encounter e)
    {
        var anchor = Anchor(e);
        e.Spatial = true;
        if (anchor is null || !u.Ships.TryGetValue(anchor.Ship, out var aShip)) { Derive(e); return; }
        var origin = aShip.Position;
        var enemy = NearestOnLine(e, anchor);
        var axis = new Vec2(1f, 0f);
        if (enemy is not null && u.Ships.TryGetValue(enemy.Ship, out var eShip) && Vec2.Distance(eShip.Position, origin) > 1e-3f)
            axis = (eShip.Position - origin).Normalized();
        float sign = enemy is not null && enemy.X < anchor.X ? -1f : 1f;   // die Achse zeigt zum Gegner, auch wenn er auf der Linie „links“ steht
        foreach (var p in e.Participants)
        {
            p.InSpace = true;
            if (!u.Ships.TryGetValue(p.Ship, out var ship)) continue;
            p.Pos = ship.Id == u.PlayerShip ? ship.Position : Clear(u, e.Sector, origin + axis * (sign * (p.X - anchor.X)));
            p.PrevPos = p.Pos;
            if (p.Active && ship.Id != u.PlayerShip) ship.Position = p.Pos;
        }
        foreach (var p in e.Participants)
        {
            var near = CombatSense.NearestInSpace(e, p);
            if (near is not null && Vec2.Distance(near.Pos, p.Pos) > 1e-3f) p.Dir = (near.Pos - p.Pos).Normalized();
        }
    }

    static void ToLine(Universe u, Encounter e)
    {
        var anchor = Anchor(e);
        e.Spatial = false;
        foreach (var p in e.Participants) p.InSpace = false;
        if (anchor is null) return;
        var enemy = CombatSense.NearestInSpace(e, anchor);
        var axis = enemy is not null && Vec2.Distance(enemy.Pos, anchor.Pos) > 1e-3f ? (enemy.Pos - anchor.Pos).Normalized() : new Vec2(1f, 0f);
        foreach (var p in e.Participants) p.X = (p.Pos - anchor.Pos).Dot(axis);
    }

    /// <summary>
    /// Phase 25 (Korrektur): schiebt eine Schiffsposition aus Asteroiden und Stationskörpern heraus (gleiche Kreisregel wie <see cref="Simulation.Collision"/>,
    /// Schiffsradius plus 1 Abstand; deterministisch, ohne Zufallszug). Gefechtsteilnehmer fliegen ohne Hindernisse — nach dem Gefecht und beim Übergang
    /// Linie → Raum dürfen sie nicht in einem Felsen stehen bleiben.
    /// </summary>
    public static Vec2 Clear(Universe u, SectorId sector, Vec2 pos)
    {
        var obstacles = u.GetObstacles(sector);
        for (int i = 0; i < 4; i++)
            if (!Simulation.Collision.Resolve(ref pos, Simulation.Collision.ShipRadius + 1f, obstacles, out _)) break;
        return pos;
    }

    /// <summary>Startposition eines neuen Teilnehmers im räumlichen Gefecht: in <paramref name="distance"/> Abstand vom Bezug, aus der Richtung seiner echten Position.</summary>
    public static Vec2 Around(Vec2 reference, Vec2 from, float distance)
    {
        var d = from - reference;
        var dir = d.Length > 1e-3f ? d.Normalized() : new Vec2(1f, 0f);
        return reference + dir * distance;
    }
}
