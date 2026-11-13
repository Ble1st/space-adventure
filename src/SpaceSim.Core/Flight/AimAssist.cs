namespace SpaceSim.Core.Flight;

/// <summary>
/// Gerätetest Phase 28 (Gefecht am Handy kaum gewinnbar): Zielhilfe „Ziel folgen“ — der Flugassistent dreht die Nase des Spielerschiffs mit der normalen
/// Drehrate zum gewählten Ziel, damit die Bugwaffen (±45°) es im Bogen halten; die Wendetasten übersteuern. Reine Funktionen, Winkel im Bogenmaß in der
/// Konvention der Ansicht (Blickrichtung 0 = −y, gegen den Uhrzeigersinn positiv; Peilung = atan2(−dx, −dy)).
/// </summary>
public static class AimAssist
{
    /// <summary>Drehrate des Spielerschiffs mit den Wendetasten (Bogenmaß je Sekunde) — dieselbe Zahl nutzt die Ansicht (PlayerShip).</summary>
    public const float PlayerTurnRate = 3.2f;

    /// <summary>Peilung von <paramref name="from"/> nach <paramref name="to"/> (Bogenmaß, Konvention wie die Blickrichtung).</summary>
    public static float Bearing(Model.Vec2 from, Model.Vec2 to) => MathF.Atan2(-(to.X - from.X), -(to.Y - from.Y));

    /// <summary>Kleinster Winkelunterschied <paramref name="to"/> − <paramref name="from"/> in (−π, π].</summary>
    public static float Delta(float from, float to)
    {
        float d = (to - from) % (2f * MathF.PI);
        if (d > MathF.PI) d -= 2f * MathF.PI;
        if (d <= -MathF.PI) d += 2f * MathF.PI;
        return d;
    }

    /// <summary>Neue Blickrichtung: höchstens <paramref name="turnRate"/> × <paramref name="dt"/> zur Peilung gedreht (kein Überschwingen).</summary>
    public static float Turn(float heading, float bearing, float turnRate, float dt)
    {
        float d = Delta(heading, bearing), step = MathF.Max(0f, turnRate) * MathF.Max(0f, dt);
        return MathF.Abs(d) <= step ? heading + d : heading + MathF.Sign(d) * step;
    }
}
