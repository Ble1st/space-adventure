using SpaceSim.Core.Model;

namespace SpaceSim.Core.Weapons;

/// <summary>
/// Reine Geometrie der Feuerwinkel (engine-unabhängig, ohne Godot): Ein Ziel liegt im Winkel eines Schachts
/// (<see cref="WeaponBay"/>), wenn der Winkel zwischen Schiffs-Ausrichtung plus <see cref="Facing"/> und der
/// Zielrichtung höchstens <see cref="WeaponBay.ArcDegrees"/> beträgt (SCHIFFSSLOTS.md).
/// Winkel-Einheit ist Grad im Peilungsmaßstab <see cref="Bearing"/>: 0 = „Norden“ (-Y), +90 = Backbord (links, -X),
/// -90 = Steuerbord (rechts, +X), 180 = Heck – derselbe Maßstab wie die Schiffsrichtung in der Ansicht.
/// </summary>
public static class FiringArc
{
    /// <summary>Peilung eines Richtungsvektors in Grad: 0 = -Y („Norden“), +90 = -X (Backbord/links).</summary>
    public static float Bearing(Vec2 dir) => MathF.Atan2(-dir.X, -dir.Y) * 180f / MathF.PI;

    /// <summary>Ausrichtungsoffset des Schachts in Grad relativ zur Schiffsrichtung.</summary>
    public static float FacingOffset(Facing facing) => facing switch
    {
        Facing.Bow => 0f,
        Facing.Port => 90f,
        Facing.Starboard => -90f,
        Facing.Stern => 180f,
        _ => 0f,
    };

    /// <summary>Winkel auf [-180, 180) gewickelt.</summary>
    public static float Normalize180(float deg)
    {
        deg = MathF.IEEERemainder(deg, 360f);
        if (deg >= 180f) deg -= 360f;
        else if (deg < -180f) deg += 360f;
        return deg;
    }

    /// <summary>Liegt das Ziel im Feuerwinkel des Schachts? Turm (arc >= 360) enthält jede Richtung.</summary>
    public static bool Contains(WeaponBay bay, float shipBearingDeg, Vec2 shipPos, Vec2 targetPos)
    {
        if (bay.IsTurret) return true;
        var d = targetPos - shipPos;
        if (d.Length < 1e-4f) return true;   // Ziel steht auf dem Schiff
        float diff = Normalize180(Bearing(d) - (shipBearingDeg + FacingOffset(bay.Facing)));
        return MathF.Abs(diff) <= bay.ArcDegrees + 1e-4f;
    }

    /// <summary>
    /// Wie weit (und wohin) das Schiff drehen muss, damit das Ziel in der Mitte des Winkels liegt:
    /// positiv = nach Backbord/links („◀ drehen“), negativ = nach Steuerbord/rechts („▶ drehen“), in Grad.
    /// </summary>
    public static float TurnHintDegrees(WeaponBay bay, float shipBearingDeg, Vec2 shipPos, Vec2 targetPos)
    {
        var d = targetPos - shipPos;
        if (d.Length < 1e-4f) return 0f;
        return Normalize180(Bearing(d) - (shipBearingDeg + FacingOffset(bay.Facing)));
    }

    /// <summary>
    /// Zustand der Schacht-Taste (UI.md Abschnitt 2). <paramref name="targetRadius"/> ist der Zielradius
    /// (Asteroid), die Reichweite gilt für den Abstand Schiff–Zieloberfläche; 0 für punktförmige Ziele (Brocken).
    /// Vorrang: kein Ziel in Reichweite → <see cref="BayState.NoTarget"/>, dann lädt die Waffe noch →
    /// <see cref="BayState.Reloading"/>, dann Ziel außerhalb des Winkels → <see cref="BayState.OutsideArc"/>, sonst bereit.
    /// </summary>
    public static BayState State(WeaponBay bay, float shipBearingDeg, Vec2 shipPos, Vec2? targetPos, float targetRadius, bool reloaded, float reloadProgress)
    {
        if (targetPos is not { } t) return BayState.NoTarget;
        float dist = Vec2.Distance(shipPos, t) - targetRadius;
        if (dist > bay.Range) return BayState.NoTarget;
        if (!reloaded) return BayState.Reloading;
        if (!Contains(bay, shipBearingDeg, shipPos, t)) return BayState.OutsideArc;
        return BayState.Ready;
    }
}
