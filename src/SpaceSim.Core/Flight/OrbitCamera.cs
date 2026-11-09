namespace SpaceSim.Core.Flight;

/// <summary>
/// Kleiner 3D-Vektor für die Kamerarechnung (Core bleibt ohne Godot, deshalb eigene Struktur statt Godot.Vector3).
/// </summary>
public readonly record struct CamVec3(float X, float Y, float Z)
{
    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);
    public static CamVec3 operator +(CamVec3 a, CamVec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static CamVec3 operator -(CamVec3 a, CamVec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static CamVec3 Lerp(CamVec3 a, CamVec3 b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
}

/// <summary>
/// Reine Rechnung der frei schwenkbaren Flugkamera (engine-unabhängig, ohne Godot): Die Kamera umkreist das Schiff
/// (Schiff = Drehpunkt) und bleibt dabei immer auf das Schiff ausgerichtet. <see cref="YawDeg"/> ist der Drehwinkel
/// um das Schiff in Grad (volle 360°), <see cref="PitchDeg"/> die Höhe über der XZ-Ebene in Grad (geklemmt:
/// nie unter die Ebene, nie exakt senkrecht) und <see cref="Distance"/> der Abstand zum Schiff (geklemmt).
/// Die Standardansicht (Yaw 0, Standard-Neigung/-Abstand) entspricht exakt dem früheren festen Kamera-Offset
/// (0, <see cref="LegacyOffsetY"/>, <see cref="LegacyOffsetZ"/>), sodass das Spiel ohne Eingabe genauso aussieht wie bisher.
/// </summary>
public sealed class OrbitCamera
{
    /// <summary>Untergrenze Neigung in Grad über der Ebene.</summary>
    public const float MinPitchDeg = 15f;
    /// <summary>Obergrenze Neigung in Grad (nie exakt senkrecht, damit die Ausrichtung stabil bleibt).</summary>
    public const float MaxPitchDeg = 85f;
    /// <summary>Minimaler Abstand zum Schiff.</summary>
    public const float MinDistance = 18f;
    /// <summary>Maximaler Abstand zum Schiff.</summary>
    public const float MaxDistance = 100f;
    /// <summary>Bisheriger fester Kamera-Offset: Y-Komponente (Höhe).</summary>
    public const float LegacyOffsetY = 38f;
    /// <summary>Bisheriger fester Kamera-Offset: Z-Komponente (hinter dem Schiff).</summary>
    public const float LegacyOffsetZ = 24f;
    /// <summary>Standard-Drehwinkel in Grad (0 = bisherige Ansicht).</summary>
    public const float DefaultYawDeg = 0f;
    /// <summary>Grad pro Pixel beim Schwenken per Finger/Mauszug (Yaw).</summary>
    public const float DegPerPixelYaw = 0.35f;
    /// <summary>Grad pro Pixel beim Schwenken per Finger/Mauszug (Pitch).</summary>
    public const float DegPerPixelPitch = 0.25f;

    /// <summary>Standard-Neigung in Grad, abgeleitet aus dem bisherigen Offset (0, 38, 24).</summary>
    public static float DefaultPitchDeg => MathF.Atan2(LegacyOffsetY, LegacyOffsetZ) * 180f / MathF.PI;
    /// <summary>Standard-Abstand, abgeleitet aus dem bisherigen Offset (0, 38, 24).</summary>
    public static float DefaultDistance => MathF.Sqrt(LegacyOffsetY * LegacyOffsetY + LegacyOffsetZ * LegacyOffsetZ);

    /// <summary>Drehwinkel um das Schiff in Grad, 0..360 (0 = Kamera auf +Z hinter dem Schiff).</summary>
    public float YawDeg { get; private set; }
    /// <summary>Höhe über der XZ-Ebene in Grad, geklemmt auf [<see cref="MinPitchDeg"/>, <see cref="MaxPitchDeg"/>].</summary>
    public float PitchDeg { get; private set; }
    /// <summary>Abstand zum Schiff, geklemmt auf [<see cref="MinDistance"/>, <see cref="MaxDistance"/>].</summary>
    public float Distance { get; private set; }

    public OrbitCamera() => Reset();

    /// <summary>Zurück auf die Standardansicht (bisheriger fester Offset).</summary>
    public void Reset()
    {
        YawDeg = DefaultYawDeg;
        PitchDeg = DefaultPitchDeg;
        Distance = DefaultDistance;
    }

    /// <summary>Setzt Winkel und Abstand direkt (geklemmt; für Test-Hooks und programmatisches Setzen).</summary>
    public void SetAngles(float yawDeg, float pitchDeg, float distance)
    {
        YawDeg = Wrap360(yawDeg);
        PitchDeg = Clamp(pitchDeg, MinPitchDeg, MaxPitchDeg);
        Distance = Clamp(distance, MinDistance, MaxDistance);
    }

    /// <summary>Schwenken um Delta-Winkel: Yaw läuft volle 360°, Pitch wird geklemmt.</summary>
    public void Orbit(float dYawDeg, float dPitchDeg)
    {
        YawDeg = Wrap360(YawDeg + dYawDeg);
        PitchDeg = Clamp(PitchDeg + dPitchDeg, MinPitchDeg, MaxPitchDeg);
    }

    /// <summary>
    /// Schwenken per Bildschirm-Pixel (Zwei-Finger-Wischen oder Mauszug): nach rechts = Kamera um das Schiff
    /// nach rechts, nach oben = Kamera steigt (Pitch wächst).
    /// </summary>
    public void OrbitDrag(float dxPx, float dyPx) => Orbit(dxPx * DegPerPixelYaw, -dyPx * DegPerPixelPitch);

    /// <summary>Zoom um einen Faktor (&lt; 1 = näher heran, z. B. Pinch-Verhältnis oder Mausrad-Stufe).</summary>
    public void Zoom(float factor) => Distance = Clamp(Distance * factor, MinDistance, MaxDistance);

    /// <summary>
    /// Offset-Vektor Schiff → Kamera. Bei Yaw 0 und Standard-Pitch liegt die Kamera exakt auf dem bisherigen
    /// festen Offset (0, 38, 24); die Länge des Vektors ist immer <see cref="Distance"/>.
    /// </summary>
    public CamVec3 Offset()
    {
        float yaw = YawDeg * MathF.PI / 180f;
        float pitch = PitchDeg * MathF.PI / 180f;
        float cp = MathF.Cos(pitch);
        return new CamVec3(Distance * cp * MathF.Sin(yaw), Distance * MathF.Sin(pitch), Distance * cp * MathF.Cos(yaw));
    }

    /// <summary>
    /// Weiches Nachführen: Der Ist-Offset folgt dem Soll-Offset exponentiell (rate pro Sekunde, delta in s).
    /// </summary>
    public static CamVec3 Follow(CamVec3 current, CamVec3 target, float delta, float rate = 6f)
    {
        float t = 1f - MathF.Exp(-rate * MathF.Max(delta, 0f));
        return CamVec3.Lerp(current, target, t);
    }

    /// <summary>Winkel auf [0, 360) gewickelt.</summary>
    public static float Wrap360(float deg)
    {
        deg %= 360f;
        if (deg < 0f) deg += 360f;
        return deg;
    }

    static float Clamp(float v, float min, float max) => MathF.Min(max, MathF.Max(min, v));
}
