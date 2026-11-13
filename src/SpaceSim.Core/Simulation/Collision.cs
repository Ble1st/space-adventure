using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Kreis-gegen-Kreis-Kollision in der Spielebene (für das vom Spieler gesteuerte Schiff in der Darstellung).</summary>
public static class Collision
{
    public const float ShipRadius = 2.5f;
    /// <summary>Aufprallgeschwindigkeit (Normalanteil), ab der ein Stoß gegen Asteroid oder Station die Hülle beschädigt (Spielerschiff; vorher nur in der Ansicht).</summary>
    public const float HardImpact = 14f;
    /// <summary>Höchsttempo des Spielerschiffs in der Ansicht (PlayerShip.MaxSpeed), für die Regel „Andrücken ist kein Rammen“.</summary>
    public const float PlayerMaxSpeed = 45f;
    /// <summary>Restenergie beim Abprallen von festen Hindernissen.</summary>
    public const float Restitution = 0.4f;

    /// <summary>
    /// Runde 11: Abprall an der Normale eines festen Hindernisses (die Regel der Ansicht, jetzt im Core und testbar): liefert die Aufprallgeschwindigkeit
    /// (Normalanteil gegen das Hindernis, 0 wenn das Schiff sich entfernt) und dreht diesen Anteil mit <see cref="Restitution"/> um.
    /// </summary>
    public static float Bounce(ref Vec2 velocity, Vec2 normal)
    {
        float vn = velocity.X * normal.X + velocity.Y * normal.Y;
        if (vn >= 0f) return 0f;
        velocity -= normal * ((1f + Restitution) * vn);
        return -vn;
    }

    /// <summary>Beschädigt dieser Aufprall die Hülle?</summary>
    public static bool IsHard(float impact) => impact > HardImpact;

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
