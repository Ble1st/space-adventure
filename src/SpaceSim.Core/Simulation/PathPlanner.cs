using System.Linq;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Weicht statischen Kreis-Hindernissen aus: aus einer Geraden wird ein Polygonzug mit Umweg-Wegpunkten.
/// Läuft im Core, damit Reisezeit (Simulation) und Flugbahn (Darstellung) identisch sind.
/// Hindernisse, die das Ziel enthalten, oder den Start, wenn man sich davon wegbewegt, werden ignoriert.
/// </summary>
public static class PathPlanner
{
    public const float Margin = 3f;
    /// <summary>Maximale Verschachtelungstiefe der Umwege.</summary>
    public const int MaxDepth = 16;
    /// <summary>Obergrenze für Umweg-Entscheidungen pro Pfad, begrenzt den Rechenaufwand in dichten Feldern.</summary>
    public const int MaxExpansions = 256;

    public static List<Vec2> Plan(Vec2 from, Vec2 to, IReadOnlyList<Obstacle> obstacles) => Plan(from, to, obstacles, Margin);

    /// <summary>
    /// Gerätetest Phase 28: Planung mit eigenem Sicherheitsabstand — der Spieler-Autopilot rechnet den Schiffsradius mit ein (mit dem Abstand der NPC-Reisen,
    /// 3 Einheiten, streifte die Gerade einen Felsen, an dem die Kollision das Schiff festhielt: es kreiste vor dem HQ Alpha).
    /// </summary>
    public static List<Vec2> Plan(Vec2 from, Vec2 to, IReadOnlyList<Obstacle> obstacles, float margin)
    {
        var pts = new List<Vec2> { from };
        int budget = MaxExpansions;
        _margin = margin;
        try { Append(from, to, obstacles, 0, pts, ref budget); }
        finally { _margin = Margin; }
        return pts;
    }

    /// <summary>
    /// Runde 25 (Selbsttest Stufe 79: der Autopilot kreiste in einem dichten Felsfeld vor dem HQ Alpha): Glätten eines geplanten Polygonzugs — von jedem
    /// Punkt springt der Weg zum entferntesten späteren Punkt, der ohne Hindernis (mit <paramref name="margin"/>) erreichbar ist. Die Umweg-Rekursion liefert in
    /// dichten Feldern Zickzack-Punkte dicht nebeneinander (Tiefe/Budget erschöpft), denen das Schiff nicht folgen kann. Nur für den Spieler-Autopiloten
    /// (<see cref="PathFollower"/>); NPC-Reisen planen unverändert.
    /// </summary>
    public static List<Vec2> Smooth(List<Vec2> pts, IReadOnlyList<Obstacle> obstacles, float margin)
    {
        if (pts.Count <= 2) return pts;
        _margin = margin;
        try
        {
            var res = new List<Vec2> { pts[0] };
            int i = 0;
            while (i < pts.Count - 1)
            {
                int j = pts.Count - 1;
                while (j > i + 1 && FirstBlocker(pts[i], pts[j], obstacles) is not null) j--;
                res.Add(pts[j]);
                i = j;
            }
            return res;
        }
        finally { _margin = Margin; }
    }

    /// <summary>
    /// Runde 44: Planung mit Glätten und — läuft der Plan trotzdem durch ein Hindernis — dem festen Ringpunkt-Ausweichweg (<see cref="Detour"/>), wie der
    /// Spieler-Autopilot seit Runde 25. Ohne freien Ausweichweg bleibt der geglättete Plan.
    /// </summary>
    public static List<Vec2> PlanRobust(Vec2 from, Vec2 to, IReadOnlyList<Obstacle> obstacles, float margin)
    {
        var pts = Smooth(Plan(from, to, obstacles, margin), obstacles, margin);
        for (int i = 1; i < pts.Count; i++)
            if (!Clear(pts[i - 1], pts[i], obstacles, margin))
                return Detour(from, to, obstacles, margin) ?? pts;
        return pts;
    }

    /// <summary>Runde 25: ist das Stück a→b frei (gleiche Regel wie die Planung: Ziel im Hindernis zählt nicht, Start im Hindernis beim Wegfliegen nicht)?</summary>
    public static bool Clear(Vec2 a, Vec2 b, IReadOnlyList<Obstacle> obstacles, float margin)
    {
        _margin = margin;
        try { return FirstBlocker(a, b, obstacles) is null; }
        finally { _margin = Margin; }
    }

    /// <summary>
    /// Runde 25: Ausweichweg, wenn die Umweg-Rekursion in einem dichten Feld kein freies Stück findet (Tiefe/Budget erschöpft: ihr Polygonzug läuft dann durch
    /// Felsen). Feste Kandidaten auf Ringen um Start und Ziel (16 Richtungen, vier Abstände, kein Zufall): zuerst der kürzeste Weg über einen Kandidaten, dessen
    /// beide Stücke frei sind, sonst über zwei Kandidaten. null = auch so nichts frei.
    /// </summary>
    public static List<Vec2>? Detour(Vec2 from, Vec2 to, IReadOnlyList<Obstacle> obstacles, float margin)
    {
        var cand = new List<Vec2>();
        foreach (var c in new[] { from, to, (from + to) * 0.5f })
            foreach (float r in new[] { 15f, 30f, 55f, 90f })
                for (int k = 0; k < 16; k++)
                {
                    float a = k * MathF.PI / 8f;
                    cand.Add(c + new Vec2(MathF.Cos(a), MathF.Sin(a)) * r);
                }
        // Punkt 13 (Rechenzeit): Kandidaten in einem Hindernis entfernen — mit Rechteck-Vorprüfung (weiter als Radius + Abstand + 1 in einer Achse = sicher draußen)
        cand.RemoveAll(w =>
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                var o = obstacles[i];
                float r = o.Radius + margin;
                if (MathF.Abs(w.X - o.Position.X) > r + 1f || MathF.Abs(w.Y - o.Position.Y) > r + 1f) continue;
                if (Vec2.Distance(w, o.Position) < r) return true;
            }
            return false;
        });
        List<Vec2>? best = null; float bestLen = float.MaxValue;
        var fromOk = new List<Vec2>();
        foreach (var w in cand)
        {
            if (!Clear(from, w, obstacles, margin)) continue;
            fromOk.Add(w);
            float len = Vec2.Distance(from, w) + Vec2.Distance(w, to);
            if (len < bestLen && Clear(w, to, obstacles, margin)) { bestLen = len; best = new List<Vec2> { from, w, to }; }
        }
        if (best is not null) return best;
        var toOk = cand.Where(w => Clear(w, to, obstacles, margin)).ToList();
        foreach (var w1 in fromOk)
            foreach (var w2 in toOk)
            {
                float len = Vec2.Distance(from, w1) + Vec2.Distance(w1, w2) + Vec2.Distance(w2, to);
                if (len < bestLen && Clear(w1, w2, obstacles, margin)) { bestLen = len; best = new List<Vec2> { from, w1, w2, to }; }
            }
        return best;
    }

    [ThreadStatic] static float _margin;
    static float M => _margin > 0f ? _margin : Margin;

    static void Append(Vec2 a, Vec2 b, IReadOnlyList<Obstacle> obs, int depth, List<Vec2> pts, ref int budget)
    {
        var blocker = FirstBlocker(a, b, obs);
        if (blocker is null || depth >= MaxDepth || budget <= 0) { pts.Add(b); return; }
        budget--;

        var o = blocker.Value;
        var dir = (b - a).Normalized();
        var perp = new Vec2(-dir.Y, dir.X);
        float off = (o.Radius + M) * 1.4f;
        var w1 = PushOut(o.Position + perp * off, obs);
        var w2 = PushOut(o.Position - perp * off, obs);
        var w = Cost(a, w1, b, obs) <= Cost(a, w2, b, obs) ? w1 : w2;

        Append(a, w, obs, depth + 1, pts, ref budget);
        Append(w, b, obs, depth + 1, pts, ref budget);
    }

    /// <summary>Schiebt einen Wegpunkt aus allen Hindernissen heraus, in denen er landet (z.B. Asteroidenketten).</summary>
    static Vec2 PushOut(Vec2 w, IReadOnlyList<Obstacle> obs)
    {
        for (int i = 0; i < 12; i++)
        {
            bool moved = false;
            foreach (var o in obs)
            {
                float r = (o.Radius + M) * 1.4f;
                if (Vec2.Distance(w, o.Position) >= (o.Radius + M)) continue;
                w = o.Position + (w - o.Position).Normalized() * r;
                moved = true;
            }
            if (!moved) break;
        }
        return w;
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
        float m = M;
        // Punkt 13 (Rechenzeit): Rechteck um die Strecke — ein Hindernis, dessen Kreis (mit Sicherheitsabstand und 1 Einheit Spiel gegen Rundung) es nicht
        // berührt, kann die Strecke nicht blockieren (der nächste Streckenpunkt liegt im Rechteck). Überspringt nur Nicht-Blocker: Ergebnis unverändert.
        float minX = MathF.Min(a.X, b.X), maxX = MathF.Max(a.X, b.X), minY = MathF.Min(a.Y, b.Y), maxY = MathF.Max(a.Y, b.Y);
        for (int i = 0; i < obs.Count; i++)
        {
            var o = obs[i];
            float r = o.Radius + m;
            float reach = r + 1f;
            if (o.Position.X + reach < minX || o.Position.X - reach > maxX || o.Position.Y + reach < minY || o.Position.Y - reach > maxY) continue;
            // Ziel im Hindernis (z.B. Andockpunkt am Rand): ignorieren. Start im Hindernis: nur ignorieren, wenn man sich davon
            // wegbewegt – sonst wäre es genau das Hindernis, in das man hineinfliegt.
            if (Vec2.Distance(b, o.Position) < r) continue;
            if (Vec2.Distance(a, o.Position) < r && ab.Dot(a - o.Position) >= 0f) continue;
            float t = len2 > 1e-6f ? Math.Clamp((o.Position - a).Dot(ab) / len2, 0f, 1f) : 0f;
            if (Vec2.Distance(a + ab * t, o.Position) < r && t < bestT) { best = o; bestT = t; }
        }
        return best;
    }
}
