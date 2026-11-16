using SpaceSim.Core.Model;

namespace SpaceSim.Core.Flight;

/// <summary>
/// Gerätetest Phase 27 (Hülle fiel direkt nach dem Abdocken bei Vollschub von 60 auf 36): Das Schiff verließ die Station immer radial über seinen Andockpunkt —
/// zeigte diese Richtung auf einen Felsen oder eine Nachbarstation, prallte es nach der Schonfrist mit voller Fahrt auf. <see cref="Heading"/> wählt die
/// Abflugrichtung, deren Strecke über <see cref="ClearLength"/> frei von Hindernissen ist: zuerst radial, dann in Schritten von 20° abwechselnd links/rechts
/// gedreht. Rein und deterministisch.
/// </summary>
public static class UndockPath
{
    /// <summary>Freie Strecke, die eine Abflugrichtung haben muss (bei Vollschub etwa die Schonfrist und die Bremszeit).</summary>
    /// <summary>Runde 44: 360 statt 140 — in den vergrößerten Sektoren liegt das nächste Tor weit weg; 8 s Vollschub nach dem Abdocken (Selbsttest Stufe 71)
    /// führten über die freie Strecke hinaus in Hintergrundfelsen.</summary>
    public const float ClearLength = 360f;

    /// <summary>Abflugrichtung (normiert) vom Andockpunkt <paramref name="spot"/> der Station bei <paramref name="station"/>; die Station selbst zählt mit.</summary>
    public static Vec2 Heading(IReadOnlyList<Obstacle> obstacles, Vec2 station, Vec2 spot, float shipRadius = 2.5f, float length = ClearLength)
    {
        var radial = (spot - station).Length > 1e-3f ? (spot - station).Normalized() : new Vec2(0f, -1f);
        // Phase 30: feinere Schritte (10°) — größere Stationen lassen schmalere Lücken zwischen den Nachbarn
        for (int k = 0; k <= 36; k++)
        {
            float deg = (k + 1) / 2 * 10f * (k % 2 == 1 ? 1f : -1f);
            var dir = Rotate(radial, deg);
            if (Clear(obstacles, spot, dir, length, shipRadius + 2f)) return dir;
        }
        return radial;
    }

    /// <summary>Ist die Strecke von <paramref name="from"/> in Richtung <paramref name="dir"/> über <paramref name="length"/> frei (Abstand <paramref name="clearance"/>)?</summary>
    public static bool Clear(IReadOnlyList<Obstacle> obstacles, Vec2 from, Vec2 dir, float length, float clearance)
    {
        foreach (var o in obstacles)
        {
            var rel = o.Position - from;
            float along = Math.Clamp(rel.Dot(dir), 0f, length);
            var closest = from + dir * along;
            if (Vec2.Distance(closest, o.Position) < o.Radius + clearance)
            {
                // der Startpunkt liegt schon im Kreis (Andockpunkt am Stationsrand): nur zählen, wenn die Strecke weiter hineinführt
                if (Vec2.Distance(from, o.Position) < o.Radius + clearance && rel.Dot(dir) <= 0f) continue;
                return false;
            }
        }
        return true;
    }

    static Vec2 Rotate(Vec2 v, float deg)
    {
        float r = deg * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
        return new Vec2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }
}
