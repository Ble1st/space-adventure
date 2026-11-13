namespace SpaceSim.Core.Flight;

/// <summary>
/// Gefechtskamera (Handy-Test Runde 13: ein Gegner 30 m vor dem Bug lag in der Standardansicht unter dem Sektor-Chip): Blickpunkt und Abstand, bei denen
/// Spielerschiff und Ziel mit Rand im freien Bildfeld liegen. Reine Rechnung (Kamera mit gegebenem Yaw/Pitch/Bildwinkel, Bildfeld als Anteil der halben
/// Bildhöhe/-breite); die Ansicht blendet weich dorthin und zurück.
/// </summary>
public static class CombatFraming
{
    /// <summary>Anteil der halben Bildhöhe bzw. -breite, in dem beide Punkte liegen sollen (oben Sektor-Chip/Lage, unten Regler und Tasten).</summary>
    public const float UsableVertical = 0.55f, UsableHorizontal = 0.6f;
    /// <summary>Blickpunkt: so weit vom Schiff zum Ziel verschoben (0 = Schiff, 1 = Ziel).</summary>
    public const float FocusShare = 0.45f;

    /// <summary>Ergebnis: Blickpunkt in der Ebene (x, z) und Kameraabstand.</summary>
    public readonly record struct Frame(float FocusX, float FocusZ, float Distance);

    /// <summary>
    /// Blickpunkt zwischen Schiff (<paramref name="sx"/>, <paramref name="sz"/>) und Ziel (<paramref name="tx"/>, <paramref name="tz"/>), Abstand mindestens
    /// <paramref name="baseDistance"/>, höchstens <paramref name="maxDistance"/>: der kleinste Abstand, bei dem beide (mit <paramref name="margin"/>) im freien Feld liegen.
    /// </summary>
    public static Frame Compute(float sx, float sz, float tx, float tz, float yawDeg, float pitchDeg, float fovDeg, float aspect,
        float baseDistance, float maxDistance, float margin = 6f)
    {
        float fx = sx + (tx - sx) * FocusShare, fz = sz + (tz - sz) * FocusShare;
        float d = baseDistance;
        for (; d < maxDistance; d += 2f)
            if (Fits(sx, sz, fx, fz, d, yawDeg, pitchDeg, fovDeg, aspect, margin) && Fits(tx, tz, fx, fz, d, yawDeg, pitchDeg, fovDeg, aspect, margin)) break;
        return new Frame(fx, fz, MathF.Min(d, maxDistance));
    }

    /// <summary>Liegt der Punkt (Ebene, Höhe 0) mit Rand im freien Bildfeld einer Kamera, die aus Abstand <paramref name="d"/> auf den Blickpunkt schaut?</summary>
    public static bool Fits(float px, float pz, float fx, float fz, float d, float yawDeg, float pitchDeg, float fovDeg, float aspect, float margin = 0f)
    {
        var (h, v) = ScreenTan(px, pz, fx, fz, d, yawDeg, pitchDeg);
        float tv = MathF.Tan(fovDeg * MathF.PI / 360f);
        float th = tv * aspect;
        if (float.IsNaN(h)) return false;
        // Rand: als Winkel am Punkt in Kameraabstand ≈ margin ÷ Abstand
        float pad = margin / MathF.Max(1f, d);
        return MathF.Abs(v) + pad <= tv * UsableVertical && MathF.Abs(h) + pad <= th * UsableHorizontal;
    }

    /// <summary>Bildkoordinaten als Tangens (horizontal, vertikal; vertikal positiv = oben) eines Ebenenpunkts.</summary>
    public static (float H, float V) ScreenTan(float px, float pz, float fx, float fz, float d, float yawDeg, float pitchDeg)
    {
        float yaw = yawDeg * MathF.PI / 180f, pitch = pitchDeg * MathF.PI / 180f;
        float cp = MathF.Cos(pitch);
        // Kamera = Blickpunkt + Offset (wie OrbitCamera.Offset), Blickrichtung = −Offset
        float ox = d * cp * MathF.Sin(yaw), oy = d * MathF.Sin(pitch), oz = d * cp * MathF.Cos(yaw);
        float cx = fx + ox, cy = oy, cz = fz + oz;
        float fwx = -ox / d, fwy = -oy / d, fwz = -oz / d;
        // rechts = fwd × up(0,1,0) = (−fwz, 0, fwx) normiert; oben' = rechts × fwd
        float rl = MathF.Sqrt(fwz * fwz + fwx * fwx);
        if (rl < 1e-5f) return (float.NaN, float.NaN);
        float rx = -fwz / rl, rz = fwx / rl;
        // oben' = rechts × fwd
        float ux = -rz * fwy, uy = rz * fwx - rx * fwz, uz = rx * fwy;
        float vx = px - cx, vy = -cy, vz = pz - cz;
        float z = vx * fwx + vy * fwy + vz * fwz;
        if (z <= 0.1f) return (float.NaN, float.NaN);
        float x = (vx * rx + vz * rz) / z;
        float y = (vx * ux + vy * uy + vz * uz) / z;
        return (x, y);
    }
}
