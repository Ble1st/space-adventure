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

/// <summary>Kameramodus: frei schwenkbar oder auf das gewählte Ziel gesperrt (Target-Lock).</summary>
public enum CameraMode
{
    /// <summary>Spieler schwenkt Yaw und Pitch selbst.</summary>
    Free,
    /// <summary>Yaw wird automatisch so geführt, dass das Ziel vor dem Schiff liegt (Kamera hinter dem Schiff, vom Ziel weg blickend); Pitch und Zoom bleiben frei.</summary>
    TargetLock,
    /// <summary>Runde 44: Rückansicht — die Kamera steht in fester Höhe und festem Abstand hinter dem Bug und folgt dem Kurs; waagrechtes Wischen wirkt nicht.</summary>
    Chase,
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
    /// <summary>Maximaler Abstand zum Schiff. Runde 9: 100 → 140, damit eine Station (Radius 15) samt Schiff bei größtem Abstand ganz ins Bild passt.</summary>
    public const float MaxDistance = 140f;
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
    public float PitchDeg { get => Mode == CameraMode.Chase ? MathF.Atan2(ChaseHeight, ChaseBack) * 180f / MathF.PI : _pitch; private set => _pitch = value; }
    float _pitch, _distance;
    /// <summary>Abstand zum Schiff, geklemmt auf [<see cref="MinDistance"/>, <see cref="MaxDistance"/>].</summary>
    public float Distance { get => Mode == CameraMode.Chase ? MathF.Sqrt(ChaseHeight * ChaseHeight + ChaseBack * ChaseBack) : _distance; private set => _distance = value; }

    /// <summary>Runde 44: Rückansicht — Standardhöhe über dem Schiff (höher als die alte feste Ansicht 38).</summary>
    public const float DefaultChaseHeight = 45f;
    /// <summary>Runde 44: Rückansicht — waagrechter Standardabstand hinter dem Bug.</summary>
    public const float DefaultChaseBack = 35f;
    public const float MinChaseHeight = 8f, MaxChaseHeight = 120f, MinChaseBack = 8f, MaxChaseBack = 140f;
    /// <summary>Runde 44: Höhenänderung je Pixel senkrechten Wischens in der Rückansicht.</summary>
    public const float ChaseHeightPerPixel = 0.15f;
    /// <summary>Runde 44: Vorausblick — der Blickpunkt liegt um diesen Anteil des Abstands vor dem Bug (Schiff im unteren Bilddrittel).</summary>
    public const float LookAheadShare = 0.4f;
    /// <summary>Runde 44: Rückansicht — Höhe über dem Schiff, geklemmt auf [<see cref="MinChaseHeight"/>, <see cref="MaxChaseHeight"/>].</summary>
    public float ChaseHeight { get; private set; } = DefaultChaseHeight;
    /// <summary>Runde 44: Rückansicht — waagrechter Abstand hinter dem Bug, geklemmt auf [<see cref="MinChaseBack"/>, <see cref="MaxChaseBack"/>].</summary>
    public float ChaseBack { get; private set; } = DefaultChaseBack;

    /// <summary>Runde 44: Höhe und Abstand der Rückansicht setzen (geklemmt).</summary>
    public void SetChase(float height, float back)
    {
        ChaseHeight = Clamp(float.IsFinite(height) ? height : DefaultChaseHeight, MinChaseHeight, MaxChaseHeight);
        ChaseBack = Clamp(float.IsFinite(back) ? back : DefaultChaseBack, MinChaseBack, MaxChaseBack);
    }
    /// <summary>Runde 44: Rückansicht auf Standardhöhe/-abstand.</summary>
    public void ResetChase() => SetChase(DefaultChaseHeight, DefaultChaseBack);
    /// <summary>Runde 44: Höhe der Rückansicht um <paramref name="delta"/> ändern (Menü „Höhe − / +“, senkrechtes Wischen).</summary>
    public void ChaseRaise(float delta) => SetChase(ChaseHeight + delta, ChaseBack);
    /// <summary>Runde 44: Menüschritt der Höhe.</summary>
    public const float ChaseHeightStep = 10f;

    /// <summary>
    /// Runde 44: Yaw weich auf den Kurs führen (Kamera hinter dem Bug), auf dem kürzesten Weg über 0/360. Kurs-Konvention wie
    /// <c>PlayerShip.Heading</c>: vorwärts = (−sin h, −cos h), also Kamera bei (sin h, cos h) · Abstand ⇒ Yaw = h in Grad.
    /// Wirkt nur in der Rückansicht.
    /// </summary>
    public void FollowHeading(float headingRad, float delta, float rate = 5f)
    {
        if (Mode != CameraMode.Chase) return;
        float want = Wrap360(headingRad * 180f / MathF.PI);
        float t = 1f - MathF.Exp(-rate * MathF.Max(delta, 0f));
        YawDeg = Wrap360(YawDeg + ShortestAngleDelta(YawDeg, want) * t);
    }

    /// <summary>Runde 45 (Befehlsstand): Yaw folgt dem Kurs des verfolgten Schiffs, ohne den Kameramodus zu wechseln — die Kamera
    /// löst sich nicht vom Schiff; Zoom/Schwenken/Modus des Spielers bleiben unverändert. Sanfter als die Rückansicht.</summary>
    public void FollowCourse(float headingRad, float delta)
    {
        float want = Wrap360(headingRad * 180f / MathF.PI);
        float t = 1f - MathF.Exp(-1.2f * MathF.Max(delta, 0f));
        YawDeg = Wrap360(YawDeg + ShortestAngleDelta(YawDeg, want) * t);
    }

    /// <summary>Runde 44: Yaw sofort auf den Kurs (Sprung, Abdocken, Respawn — kein Schwenk). Nur in der Rückansicht.</summary>
    public void SnapHeading(float headingRad)
    {
        if (Mode == CameraMode.Chase) YawDeg = Wrap360(headingRad * 180f / MathF.PI);
    }

    /// <summary>
    /// Runde 44: Vorausblick der Rückansicht — Blickpunkt relativ zum Schiff (x, z) vor dem Bug; in anderen Modi (0, 0).
    /// </summary>
    public (float X, float Z) LookAhead(float headingRad, float zoom = 1f)
    {
        if (Mode != CameraMode.Chase) return (0f, 0f);
        float d = LookAheadShare * ChaseBack * zoom;
        return (-MathF.Sin(headingRad) * d, -MathF.Cos(headingRad) * d);
    }
    /// <summary>Kameramodus (Standard: frei).</summary>
    public CameraMode Mode { get; set; } = CameraMode.Free;
    /// <summary>
    /// Schwenkrichtung umkehren. Standard (false) = „die Welt greifen“: Wischen nach rechts schiebt die Szene nach rechts
    /// (die Kamera wandert nach links), Wischen nach oben schiebt sie nach oben (die Kamera sinkt).
    /// </summary>
    public bool InvertDrag { get; set; }

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
    /// Schwenken per Bildschirm-Pixel (Finger- oder Mauszug). Standard („Welt greifen“): Zug nach rechts dreht die Kamera nach
    /// links um das Schiff (die Szene folgt dem Finger), Zug nach oben senkt die Kamera (Pitch sinkt). <see cref="InvertDrag"/>
    /// kehrt beides um. Im Modus <see cref="CameraMode.TargetLock"/> wirkt nur die senkrechte Bewegung (Yaw führt das Ziel).
    /// </summary>
    public void OrbitDrag(float dxPx, float dyPx)
    {
        float sign = InvertDrag ? -1f : 1f;
        if (Mode == CameraMode.Chase) { ChaseDrag(dyPx); return; }
        float dYaw = Mode == CameraMode.TargetLock ? 0f : -dxPx * DegPerPixelYaw * sign;
        Orbit(dYaw, dyPx * DegPerPixelPitch * sign);
    }

    /// <summary>
    /// Runde 44: senkrechtes Wischen in der Rückansicht ändert nur die Höhe (Wischen nach unten hebt die Kamera — wie der Pitch der freien Kamera);
    /// <see cref="InvertDrag"/> kehrt um.
    /// </summary>
    public void ChaseDrag(float dyPx) => ChaseRaise(dyPx * ChaseHeightPerPixel * (InvertDrag ? -1f : 1f));

    /// <summary>Zoom um einen Faktor (&lt; 1 = näher heran, z. B. Pinch-Verhältnis oder Mausrad-Stufe). Rückansicht: Höhe und Abstand gemeinsam.</summary>
    public void Zoom(float factor)
    {
        if (Mode == CameraMode.Chase) { ScaleChase(factor); return; }
        Distance = Clamp(Distance * factor, MinDistance, MaxDistance);
    }

    /// <summary>Runde 44: Höhe und Abstand der Rückansicht mit demselben Faktor (die Neigung bleibt, solange keine Grenze greift).</summary>
    void ScaleChase(float factor)
    {
        if (!float.IsFinite(factor) || factor <= 0f) return;
        float kMax = MathF.Min(MaxChaseHeight / ChaseHeight, MaxChaseBack / ChaseBack);
        float kMin = MathF.Max(MinChaseHeight / ChaseHeight, MinChaseBack / ChaseBack);
        float k = Clamp(factor, kMin, kMax);
        SetChase(ChaseHeight * k, ChaseBack * k);
    }

    /// <summary>Runde 12 (Handy-Test: „−“ zoomte kaum heraus, „+“ sehr stark hinein): gleichmäßige Menü-Stufen über den ganzen Bereich.</summary>
    public const int ZoomSteps = 8;
    /// <summary>Eine Menü-Zoomstufe: <paramref name="direction"/> &gt; 0 heraus (weiter weg), &lt; 0 hinein; Schritt (Max − Min) ÷ <see cref="ZoomSteps"/>.</summary>
    public void ZoomStep(int direction)
    {
        float step = Math.Sign(direction) * (MaxDistance - MinDistance) / ZoomSteps;
        if (Mode == CameraMode.Chase) { float d = Distance; ScaleChase((d + step) / d); return; }
        Distance = Clamp(Distance + step, MinDistance, MaxDistance);
    }

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

    /// <summary>
    /// Yaw in Grad, bei dem die Kamera genau auf der dem Ziel abgewandten Seite des Schiffs steht (das Ziel liegt vor dem
    /// Schiff, in Blickrichtung). <paramref name="dx"/>/<paramref name="dz"/> = Vektor Schiff → Ziel in der XZ-Ebene.
    /// Liegt das Ziel (fast) auf dem Schiff, ist die Richtung unbestimmt: dann <c>null</c>.
    /// </summary>
    public static float? YawToward(float dx, float dz)
    {
        if (dx * dx + dz * dz < MinLockDistance * MinLockDistance) return null;
        return Wrap360(MathF.Atan2(-dx, -dz) * 180f / MathF.PI);
    }

    /// <summary>Unterhalb dieser Entfernung zum Ziel (Einheiten) wird der Yaw im Target-Lock nicht mehr nachgeführt.</summary>
    public const float MinLockDistance = 1.5f;

    /// <summary>Kürzeste Winkeldifferenz von <paramref name="from"/> nach <paramref name="to"/> in Grad, im Bereich (-180, 180].</summary>
    public static float ShortestAngleDelta(float from, float to)
    {
        float d = Wrap360(to - from);
        return d > 180f ? d - 360f : d;
    }

    /// <summary>
    /// Target-Lock: führt den Yaw weich auf das Ziel (Vektor Schiff → Ziel) nach, auf dem kürzesten Weg.
    /// Wirkt nur im Modus <see cref="CameraMode.TargetLock"/>; ohne Ziel oder bei zu kleiner Entfernung bleibt der Yaw stehen.
    /// </summary>
    public void FollowTarget(float dx, float dz, float delta, float rate = 4f)
    {
        if (Mode != CameraMode.TargetLock || YawToward(dx, dz) is not { } want) return;
        float t = 1f - MathF.Exp(-rate * MathF.Max(delta, 0f));
        YawDeg = Wrap360(YawDeg + ShortestAngleDelta(YawDeg, want) * t);
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
