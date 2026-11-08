using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausweichen zwischen Schiffen (nur Darstellung): berechnet für ein Schiff einen seitlichen Versatz weg von
/// allen Nachbarn in <see cref="Radius"/>. Die Simulation bleibt unberührt (Fahrpläne ändern sich nicht).
/// </summary>
public static class Avoidance
{
    public const float Radius = 16f;
    public const float MaxOffset = 9f;
    /// <summary>Id für das Spielerschiff in der Nachbarliste.</summary>
    public static readonly ShipId Player = new(-1);

    public static Vec2 Desired(ShipId self, Vec2 selfPos, IEnumerable<(ShipId Id, Vec2 Pos)> others)
    {
        var push = new Vec2(0, 0);
        foreach (var (id, pos) in others)
        {
            if (id == self) continue;
            var d = selfPos - pos;
            float dist = d.Length;
            if (dist >= Radius) continue;
            // Exakt übereinander: Richtung deterministisch aus den Ids, damit beide Schiffe entgegengesetzt ausweichen
            var dir = dist > 0.01f ? d * (1f / dist)
                                   : Vec2.FromAngle(Math.Min(self.Value, id.Value) * 2.4f + (self.Value > id.Value ? 0f : MathF.PI));
            push += dir * ((Radius - dist) * 0.8f);
        }
        float len = push.Length;
        return len > MaxOffset ? push * (MaxOffset / len) : push;
    }
}
