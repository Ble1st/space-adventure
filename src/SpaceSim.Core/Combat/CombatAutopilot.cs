using SpaceSim.Core.Model;
using SpaceSim.Core.Weapons;

namespace SpaceSim.Core.Combat;

/// <summary>Phase 25: Eingabe eines Schritts des Gefechts-Autopiloten (reine Daten).</summary>
/// <param name="Pos">Eigene Position.</param>
/// <param name="Dir">Eigene Blickrichtung (normiert).</param>
/// <param name="MaxSpeed">Höchsttempo in Einheiten je Sekunde.</param>
/// <param name="Target">Position des Bezugsschiffs (Ziel oder nächster Gegner).</param>
/// <param name="Move">Bewegung der Taktik (anfliegen, halten, absetzen).</param>
/// <param name="Wanted">Gewünschter Abstand der Taktik.</param>
/// <param name="Reach">Eigene größte Waffenreichweite (0 = unbewaffnet; „Halten“ fliegt nur heran, wenn das Ziel außer Reichweite ist).</param>
/// <param name="OrbitSign">Drehsinn beim Kreisen (+1/−1, fest je Teilnehmer).</param>
public readonly record struct CombatPilotInput(Vec2 Pos, Vec2 Dir, float MaxSpeed, Vec2 Target, CombatMove Move, float Wanted, float Reach, float OrbitSign);

/// <summary>Phase 25: Ergebnis eines Schritts: neue Position, neue Blickrichtung, gefahrenes Tempo (Einheiten je Sekunde).</summary>
public readonly record struct CombatPilotStep(Vec2 Pos, Vec2 Dir, float Speed);

/// <summary>
/// Phase 25 (KAMPF.md 4.3): Gefechts-Autopilot für NPC-Schiffe im Spielersektor — wie der Mining-Autopilot eine reine Schrittfunktion. Die Taktik
/// (<see cref="ICombatTactic"/>) entscheidet <i>was</i> (anfliegen, Abstand halten, abdrehen), der Autopilot <i>wie</i>: Blickrichtung dreht mit begrenzter
/// Wendegeschwindigkeit zum Ziel (zum Fliehen weg vom Ziel), damit Bugwaffen tragen; die Bewegung folgt dem Wunschkurs (anfliegen bis zum Abstandsband,
/// im Band um das Ziel kreisen, zu nah: absetzen; fliehen: geradeaus weg). Keine Hindernisse (Gefechte sind kurz; die Ansicht schiebt die Marker aus
/// Hindernissen heraus). Rein und deterministisch.
/// </summary>
public static class CombatAutopilot
{
    public static CombatPilotStep Step(in CombatPilotInput i, float seconds, EconomyRules r)
    {
        if (seconds <= 0f) return new CombatPilotStep(i.Pos, i.Dir, 0f);
        var to = i.Target - i.Pos;
        float dist = to.Length;
        var toward = dist > 1e-4f ? to * (1f / dist) : i.Dir;
        var side = new Vec2(-toward.Y, toward.X) * (i.OrbitSign >= 0f ? 1f : -1f);
        float step = MathF.Max(0f, i.MaxSpeed) * seconds;
        float wanted = MathF.Max(0f, i.Wanted);
        Vec2 moveDir;
        float travel;
        Vec2 face = toward;
        switch (i.Move)
        {
            case CombatMove.Flee:
                face = toward * -1f;
                moveDir = face;
                travel = step;
                break;
            case CombatMove.Hold when !(i.Reach > 0f && dist > i.Reach && wanted > 0f):
                moveDir = toward;
                travel = 0f;   // stehen bleiben, nur zum Ziel drehen
                break;
            default:   // anfliegen (auch „Halten“ außer Reichweite)
            {
                float lo = wanted * r.CombatBandMin, hi = wanted * r.CombatBandMax;
                if (dist > hi)
                {
                    moveDir = toward;
                    travel = MathF.Min(step, dist - wanted);
                }
                else if (dist < lo)
                {
                    moveDir = (toward * -0.8f + side * 0.6f).Normalized();
                    travel = MathF.Min(step, (wanted - dist) * 1.2f);
                }
                else
                {
                    // im Band kreisen; leichter Zug zum Wunschabstand hält den Kreis stabil
                    float pull = wanted > 0f ? Math.Clamp((dist - wanted) / MathF.Max(1f, wanted), -0.5f, 0.5f) : 0f;
                    moveDir = (side + toward * pull).Normalized();
                    travel = step * Math.Clamp(r.CombatOrbitSpeedShare, 0f, 1f);
                }
                break;
            }
        }
        var dir = Turn(i.Dir, face, r.CombatTurnRate * seconds);
        var pos = i.Pos + moveDir * travel;
        return new CombatPilotStep(pos, dir, travel / seconds);
    }

    /// <summary>Dreht <paramref name="from"/> höchstens um <paramref name="maxDeg"/> Grad zu <paramref name="to"/> (beide normiert).</summary>
    public static Vec2 Turn(Vec2 from, Vec2 to, float maxDeg)
    {
        float a = FiringArc.Bearing(from), b = FiringArc.Bearing(to);
        float diff = FiringArc.Normalize180(b - a);
        float stepDeg = Math.Clamp(diff, -MathF.Max(0f, maxDeg), MathF.Max(0f, maxDeg));
        return FromBearing(a + stepDeg);
    }

    /// <summary>Richtungsvektor zu einer Peilung (Grad, Maßstab <see cref="FiringArc.Bearing"/>: 0 = −Y, +90 = −X).</summary>
    public static Vec2 FromBearing(float deg)
    {
        float rad = deg * MathF.PI / 180f;
        return new Vec2(-MathF.Sin(rad), -MathF.Cos(rad));
    }
}
