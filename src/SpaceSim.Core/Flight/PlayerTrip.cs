using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Flight;

/// <summary>
/// Runde 26: Flugzeit des Spielerschiffs per Autopilot bei ×1 — dieselbe Wegwahl wie im Spiel (<see cref="PathFollower"/> mit
/// <see cref="Navigator.PlayerObstacles"/>), dasselbe Tempo wie die Ansicht (höchstens <see cref="Collision.PlayerMaxSpeed"/>, Schub
/// <see cref="Accel"/>, sanftes Abbremsen vor dem Ziel wie <c>Main</c>: Ziel ÷ 40, Hindernisnähe ÷ 30), je Torsprung <see cref="JumpSeconds"/>.
/// Reine Rechnung ohne Zustand (für die Abnehmer-Auskunft und die Sonde <c>--probe hinweis</c>); ersetzt dort <c>Travel.Estimate</c>, das mit dem NPC-Tempo
/// und der Luftlinie rechnet.
/// </summary>
public static class PlayerTrip
{
    public const float Step = 0.25f;
    public const float Accel = 40f;
    public const float JumpSeconds = 1f;
    public const float DockSeconds = 2f;
    /// <summary>Anlauf: Ausrichten der Nase und Abdocken (Selbsttest Stufe 79: die kurzen Flüge dauerten real 1–7 s länger als die reine Strecke).</summary>
    public const float StartSeconds = 1.5f;
    /// <summary>Runde 44: 24 000 Schritte (vorher 8000 = 2000 s) — Flüge über mehrere vergrößerte Sektoren dauern Minuten.</summary>
    const int MaxSteps = 24000;

    /// <summary>Sekunden von <paramref name="fromPos"/> bis zum Andockpunkt; PositiveInfinity = nicht erreichbar.</summary>
    public static float Seconds(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship = null) =>
        Seconds(u, fromSector, fromPos, toSector, toPos, ship, u.PerfCaches);

    /// <summary>Punkt 13: <paramref name="memo"/> = Route und Hindernisliste je Sektor in dieser Rechnung merken (gleiches Ergebnis; aus nur für Vergleichstests).</summary>
    public static float Seconds(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship, bool memo)
    {
        var target = new NavTarget(toSector, toPos, "");
        var f = new PathFollower { Memo = memo ? new PathFollower.StaticMemo() : null };   // Punkt 13: der Weltstand ändert sich in dieser Rechnung nicht
        var sector = fromSector;
        var pos = fromPos;
        float v = 0f, t = DockSeconds + StartSeconds;
        var obsBy = new Dictionary<SectorId, List<Obstacle>>();
        for (int i = 0; i < MaxSteps; i++)
        {
            var step = f.Next(u, sector, pos, target, ship);
            if (step.Status == NavStatus.Arrived) return t;
            if (step.Status == NavStatus.Unreachable) return float.PositiveInfinity;
            float final = sector == toSector ? Vec2.Distance(pos, toPos) : 999f;
            float edge = float.MaxValue;
            if (!obsBy.TryGetValue(sector, out var rocks)) obsBy[sector] = rocks = u.GetObstacles(sector);
            // Punkt 13 (Rechenzeit): nur Hindernisse, die näher als die Bremsgrenze (30) liegen können, verändern die Geschwindigkeit (darüber gilt 1) — ein
            // Hindernis, das schon in einer Achse mehr als 31 entfernt ist, kann das Minimum unter 30 nicht bestimmen; gleiche Geschwindigkeit wie vorher
            foreach (var o in rocks)
            {
                float reach = o.Radius + Collision.ShipRadius + 31f;
                if (MathF.Abs(pos.X - o.Position.X) > reach || MathF.Abs(pos.Y - o.Position.Y) > reach) continue;
                edge = MathF.Min(edge, Vec2.Distance(pos, o.Position) - o.Radius - Collision.ShipRadius);
            }
            float vmax = Collision.PlayerMaxSpeed * MathF.Min(Math.Clamp(final / 40f, 0.25f, 1f), Math.Clamp(edge / 30f, 0.22f, 1f));
            v = MathF.Min(vmax, v + Accel * Step);
            var d = step.Waypoint - pos;
            float len = d.Length;
            if (len > 0.001f) pos += d * (MathF.Min(v * Step, len) / len);
            t += Step;
            if (step.Gate is { } gid && u.Gates.TryGetValue(gid, out var gate) && Vec2.Distance(pos, gate.Position) < Navigator.GateJumpRadius)
            {
                var other = u.Gates[gate.Target];
                sector = other.Sector; pos = other.Position; t += JumpSeconds;
                f.Reset();
            }
        }
        return float.PositiveInfinity;
    }

    /// <summary>Wie <see cref="Seconds"/>, zum Andockpunkt einer Station.</summary>
    public static float SecondsTo(Universe u, SectorId fromSector, Vec2 fromPos, Station st, Ship? ship = null) =>
        Seconds(u, fromSector, fromPos, st.Sector, st.DockPoint(ship?.Id ?? default), ship);

    /// <summary>„~1:40 min“ für die Anzeige.</summary>
    public static string Text(float seconds) => float.IsInfinity(seconds) ? "?" : $"~{(int)(seconds / 60)}:{(int)(seconds % 60):00} min";
}
