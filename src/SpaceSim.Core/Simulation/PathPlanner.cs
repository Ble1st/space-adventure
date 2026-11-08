using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Weicht statischen Kreis-Hindernissen aus: aus einer Geraden wird ein Polygonzug mit Umweg-Wegpunkten.
/// Läuft im Core, damit Reisezeit (Simulation) und Flugbahn (Darstellung) identisch sind.
/// Hindernisse, die Start oder Ziel (inkl. Sicherheitsabstand) enthalten, werden ignoriert.
/// </summary>
public static class PathPlanner
{
    public const float Margin = 3f;
    const int MaxDepth = 6;

    public static List<Vec2> Plan(Vec2 from, Vec2 to, IReadOnlyList<Obstacle> obstacles)
    {
        var pts = new List<Vec2> { from };
        Append(from, to, obstacles, 0, pts);
        return pts;
    }

    static void Append(Vec2 a, Vec2 b, IReadOnlyList<Obstacle> obs, int depth, List<Vec2> pts)
    {
        var blocker = FirstBlocker(a, b, obs);
        if (blocker is null || depth >= MaxDepth) { pts.Add(b); return; }

        var o = blocker.Value;
        var dir = (b - a).Normalized();
        var perp = new Vec2(-dir.Y, dir.X);
        float off = (o.Radius + Margin) * 1.4f;
        var w1 = o.Position + perp * off;
        var w2 = o.Position - perp * off;
        var w = Cost(a, w1, b, obs) <= Cost(a, w2, b, obs) ? w1 : w2;

        Append(a, w, obs, depth + 1, pts);
        Append(w, b, obs, depth + 1, pts);
    }

    static float Cost(Vec2 a, Vec2 w, Vec2 b, IReadOnlyList<Obstacle> obs) =>
        Vec2.Distance(a, w) + Vec2.Distance(w, b)
        + (FirstBlocker(a, w, obs) is null ? 0f : 1000f)
        + (FirstBlocker(w, b, obs) is null ? 0f : 1000f);

    static Obstacle? FirstBlocker(Vec2 a, Vec2 b, IReadOnlyList<Obstacle> obs)
    {
        Obstacle? best = null;
        float bestT = float.MaxValue;
        var ab = b - a;
        float len2 = ab.Dot(ab);
        foreach (var o in obs)
        {
            float r = o.Radius + Margin;
            if (Vec2.Distance(a, o.Position) < r || Vec2.Distance(b, o.Position) < r) continue;
            float t = len2 > 1e-6f ? Math.Clamp((o.Position - a).Dot(ab) / len2, 0f, 1f) : 0f;
            if (Vec2.Distance(a + ab * t, o.Position) < r && t < bestT) { best = o; bestT = t; }
        }
        return best;
    }
}
