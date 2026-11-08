using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Kreis-gegen-Kreis-Kollision in der Spielebene (für das vom Spieler gesteuerte Schiff in der Darstellung).</summary>
public static class Collision
{
    public const float ShipRadius = 2.5f;

    /// <summary>Schiebt <paramref name="pos"/> aus allen überlappenden Hindernissen heraus. Normal zeigt vom zuletzt berührten Hindernis weg.</summary>
    public static bool Resolve(ref Vec2 pos, float radius, IReadOnlyList<Obstacle> obstacles, out Vec2 normal)
    {
        bool hit = false;
        normal = default;
        for (int pass = 0; pass < 2; pass++)   // zweite Runde löst Überlappung mit mehreren Hindernissen
            foreach (var o in obstacles)
            {
                var d = pos - o.Position;
                float dist = d.Length, min = o.Radius + radius;
                if (dist >= min) continue;
                normal = dist > 1e-4f ? d * (1f / dist) : new Vec2(1, 0);
                pos = o.Position + normal * min;
                hit = true;
            }
        return hit;
    }
}
