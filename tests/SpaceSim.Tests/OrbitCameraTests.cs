using SpaceSim.Core.Flight;

namespace SpaceSim.Tests;

public class OrbitCameraTests
{
    [Fact]
    public void Default_EntsprichtExaktDemBisherigenFestenOffset()
    {
        var c = new OrbitCamera();
        Assert.Equal(OrbitCamera.DefaultYawDeg, c.YawDeg, 5);
        Assert.Equal(OrbitCamera.DefaultPitchDeg, c.PitchDeg, 5);
        Assert.Equal(OrbitCamera.DefaultDistance, c.Distance, 5);
        var o = c.Offset();
        Assert.Equal(0f, o.X, 3);
        Assert.Equal(OrbitCamera.LegacyOffsetY, o.Y, 3);   // 38
        Assert.Equal(OrbitCamera.LegacyOffsetZ, o.Z, 3);   // 24
    }

    [Theory]
    [InlineData(0f, 15f, 18f)]
    [InlineData(90f, 45f, 50f)]
    [InlineData(270f, 85f, 100f)]
    [InlineData(359f, 57.67f, 44.94f)]
    [InlineData(123.4f, 30f, 77f)]
    public void OffsetLaenge_EntsprichtImmerDemAbstand(float yaw, float pitch, float dist)
    {
        var c = new OrbitCamera();
        c.SetAngles(yaw, pitch, dist);
        Assert.Equal(dist, c.Offset().Length, 3);
    }

    [Theory]
    [InlineData(-30f, OrbitCamera.MinPitchDeg)]
    [InlineData(0f, OrbitCamera.MinPitchDeg)]
    [InlineData(15f, 15f)]
    [InlineData(45f, 45f)]
    [InlineData(85f, 85f)]
    [InlineData(120f, OrbitCamera.MaxPitchDeg)]
    [InlineData(720f, OrbitCamera.MaxPitchDeg)]
    public void Pitch_WirdGeklemmt_NieUnterDerEbene_NieSenkrecht(float pitch, float expected)
    {
        var c = new OrbitCamera();
        c.SetAngles(0f, pitch, 50f);
        Assert.Equal(expected, c.PitchDeg, 4);
        Assert.True(c.Offset().Y > 0f, "Kamera darf nie unter die XZ-Ebene");
        Assert.True(c.PitchDeg < 90f, "Kamera nie exakt senkrecht (Stabilität)");
    }

    [Fact]
    public void Yaw_LaeuftUeberVolle360Grad()
    {
        var c = new OrbitCamera();
        c.Orbit(370f, 0f);
        Assert.Equal(10f, c.YawDeg, 4);
        c.Orbit(-20f, 0f);
        Assert.Equal(350f, c.YawDeg, 4);
        c.SetAngles(-90f, 45f, 50f);
        Assert.Equal(270f, c.YawDeg, 4);
    }

    [Fact]
    public void PitchYaw_BeziehungZumOffset()
    {
        var c = new OrbitCamera();
        c.SetAngles(0f, 60f, 40f);
        var o = c.Offset();
        Assert.Equal(0f, o.X, 3);            // Yaw 0: Kamera hinter dem Schiff (+Z), X = 0
        Assert.True(o.Z > 0f);
        Assert.Equal(40f * MathF.Sin(MathF.PI / 3f), o.Y, 3);   // Höhe hängt nur vom Pitch ab

        c.SetAngles(90f, 60f, 40f);
        o = c.Offset();
        Assert.True(o.X > 0f);               // Yaw 90: Kamera auf der +X-Seite
        Assert.Equal(0f, o.Z, 3);
        Assert.Equal(40f * MathF.Sin(MathF.PI / 3f), o.Y, 3);

        c.SetAngles(180f, 60f, 40f);
        o = c.Offset();
        Assert.True(o.Z < 0f);               // Yaw 180: Kamera vor dem Schiff (-Z)
        Assert.Equal(0f, o.X, 3);
    }

    [Fact]
    public void Zoom_WirdAufMinMaxGeklemmt()
    {
        var c = new OrbitCamera();
        for (int i = 0; i < 100; i++) c.Zoom(0.5f);
        Assert.Equal(OrbitCamera.MinDistance, c.Distance, 4);
        for (int i = 0; i < 100; i++) c.Zoom(2f);
        Assert.Equal(OrbitCamera.MaxDistance, c.Distance, 4);
    }

    [Fact]
    public void OrbitDrag_WeltGreifen_SzeneFolgtDemFinger()
    {
        var c = new OrbitCamera();
        c.OrbitDrag(100f, 0f);    // 100 px nach rechts: Kamera wandert nach links um das Schiff
        Assert.Equal(OrbitCamera.Wrap360(-100f * OrbitCamera.DegPerPixelYaw), c.YawDeg, 3);
        Assert.True(c.Offset().X < 0f);   // links vom Schiff
        c.OrbitDrag(0f, 100f);    // 100 px nach unten: Kamera steigt (Szene folgt dem Finger nach unten)
        Assert.Equal(OrbitCamera.DefaultPitchDeg + 100f * OrbitCamera.DegPerPixelPitch, c.PitchDeg, 3);
        c.OrbitDrag(0f, 10000f);  // weit nach unten ziehen: Pitch klemmt oben
        Assert.Equal(OrbitCamera.MaxPitchDeg, c.PitchDeg, 4);
        c.OrbitDrag(0f, -10000f); // weit nach oben ziehen: Pitch klemmt unten
        Assert.Equal(OrbitCamera.MinPitchDeg, c.PitchDeg, 4);
    }

    [Fact]
    public void OrbitDrag_Invertiert_KehrtBeideAchsenUm()
    {
        var normal = new OrbitCamera();
        var inv = new OrbitCamera { InvertDrag = true };
        normal.OrbitDrag(40f, 30f);
        inv.OrbitDrag(40f, 30f);
        Assert.Equal(OrbitCamera.Wrap360(-40f * OrbitCamera.DegPerPixelYaw), normal.YawDeg, 3);
        Assert.Equal(OrbitCamera.Wrap360(40f * OrbitCamera.DegPerPixelYaw), inv.YawDeg, 3);
        Assert.Equal(OrbitCamera.DefaultPitchDeg + 30f * OrbitCamera.DegPerPixelPitch, normal.PitchDeg, 3);
        Assert.Equal(OrbitCamera.DefaultPitchDeg - 30f * OrbitCamera.DegPerPixelPitch, inv.PitchDeg, 3);
    }

    [Fact]
    public void OrbitDrag_TargetLock_AendertNurDenPitch()
    {
        var c = new OrbitCamera { Mode = CameraMode.TargetLock };
        c.OrbitDrag(200f, 20f);
        Assert.Equal(0f, c.YawDeg, 4);
        Assert.Equal(OrbitCamera.DefaultPitchDeg + 20f * OrbitCamera.DegPerPixelPitch, c.PitchDeg, 3);
    }

    [Theory]
    [InlineData(0f, -10f)]    // Ziel nördlich (-Z): Kamera südlich (+Z) = Yaw 0, wie die Standardansicht
    [InlineData(10f, 0f)]     // Ziel östlich: Kamera westlich
    [InlineData(0f, 10f)]     // Ziel südlich: Kamera nördlich
    [InlineData(-10f, 0f)]    // Ziel westlich: Kamera östlich
    [InlineData(7f, -3f)]
    [InlineData(-4f, 9f)]
    public void YawToward_KameraStehtAufDerDemZielAbgewandtenSeite(float dx, float dz)
    {
        var c = new OrbitCamera();
        var yaw = OrbitCamera.YawToward(dx, dz);
        Assert.NotNull(yaw);
        c.SetAngles(yaw!.Value, 45f, 50f);
        var o = c.Offset();
        // Offset (Schiff → Kamera) zeigt exakt entgegengesetzt zur Richtung Schiff → Ziel
        float len = MathF.Sqrt(dx * dx + dz * dz), ol = MathF.Sqrt(o.X * o.X + o.Z * o.Z);
        Assert.Equal(-dx / len, o.X / ol, 3);
        Assert.Equal(-dz / len, o.Z / ol, 3);
    }

    [Fact]
    public void YawToward_ZielAufDemSchiff_IstUnbestimmt()
    {
        Assert.Null(OrbitCamera.YawToward(0f, 0f));
        Assert.Null(OrbitCamera.YawToward(0.5f, 0.5f));
    }

    [Theory]
    [InlineData(10f, 350f, -20f)]
    [InlineData(350f, 10f, 20f)]
    [InlineData(0f, 180f, 180f)]
    [InlineData(90f, 90f, 0f)]
    public void ShortestAngleDelta_GehtDenKuerzestenWeg(float from, float to, float expected)
        => Assert.Equal(expected, OrbitCamera.ShortestAngleDelta(from, to), 3);

    [Fact]
    public void FollowTarget_Frei_AendertNichts_TargetLock_FuehrtDenYawAufsZiel()
    {
        var free = new OrbitCamera();
        free.FollowTarget(10f, 0f, 1f);
        Assert.Equal(0f, free.YawDeg, 4);

        var lockCam = new OrbitCamera { Mode = CameraMode.TargetLock };
        float want = OrbitCamera.YawToward(10f, 0f)!.Value;
        for (int i = 0; i < 200; i++) lockCam.FollowTarget(10f, 0f, 0.05f);
        Assert.Equal(want, lockCam.YawDeg, 1);

        float stay = lockCam.YawDeg;
        lockCam.FollowTarget(0.2f, 0.2f, 1f);   // Ziel praktisch auf dem Schiff: bleibt stehen
        Assert.Equal(stay, lockCam.YawDeg, 4);
    }

    [Fact]
    public void Reset_SetztAufStandardansichtZurueck()
    {
        var c = new OrbitCamera();
        c.SetAngles(123f, 30f, 80f);
        c.Reset();
        Assert.Equal(OrbitCamera.DefaultYawDeg, c.YawDeg, 5);
        Assert.Equal(OrbitCamera.DefaultPitchDeg, c.PitchDeg, 5);
        Assert.Equal(OrbitCamera.DefaultDistance, c.Distance, 5);
        var o = c.Offset();
        Assert.Equal(0f, o.X, 3);
        Assert.Equal(OrbitCamera.LegacyOffsetY, o.Y, 3);
        Assert.Equal(OrbitCamera.LegacyOffsetZ, o.Z, 3);
    }

    [Fact]
    public void Follow_NaehertSichExponentiellAn()
    {
        var target = new CamVec3(0, 38, 24);
        var cur = new CamVec3(0, 0, 0);
        cur = OrbitCamera.Follow(cur, target, 1f, 6f);
        Assert.True(cur.Y > 30f && cur.Y < 38f, "ein großer Schritt landet nahe am Ziel, aber nicht exakt drauf");
        for (int i = 0; i < 600; i++) cur = OrbitCamera.Follow(cur, target, 1f / 60f, 6f);
        Assert.True((target - cur).Length < 0.05f, "nach genug Frames ist der Soll-Offset erreicht");
        var before = cur;
        cur = OrbitCamera.Follow(cur, new CamVec3(100, 0, 0), 0f);
        Assert.Equal(before, cur);   // delta 0: keine Bewegung
    }

    [Fact]
    public void MenueZoom_GleichmaessigeStufen_BisZurHoechstdistanz()
    {
        var cam = new OrbitCamera();
        float d0 = cam.Distance;
        cam.ZoomStep(+1);
        float step = cam.Distance - d0;
        Assert.Equal((OrbitCamera.MaxDistance - OrbitCamera.MinDistance) / OrbitCamera.ZoomSteps, step, 3);
        cam.ZoomStep(-1);
        Assert.Equal(d0, cam.Distance, 3);   // hin und zurück gleich groß
        for (int i = 0; i < OrbitCamera.ZoomSteps + 2; i++) cam.ZoomStep(+1);
        Assert.Equal(OrbitCamera.MaxDistance, cam.Distance);   // über das Menü bis 140 erreichbar
        for (int i = 0; i < OrbitCamera.ZoomSteps + 2; i++) cam.ZoomStep(-1);
        Assert.Equal(OrbitCamera.MinDistance, cam.Distance);
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(60f)]
    [InlineData(110f)]
    public void Gefechtskamera_RahmtSchiffUndZiel(float range)
    {
        // Standardansicht: Ziel genau vor dem Bug (Kamera dahinter), Abstand 45 — bei 30 m lag das Ziel schon unter dem Sektor-Chip
        var cam = new OrbitCamera();
        float pitch = cam.PitchDeg, yaw = cam.YawDeg;
        Assert.False(CombatFraming.Fits(0f, -range, 0f, 0f, OrbitCamera.DefaultDistance, yaw, pitch, 50f, 16f / 9f), "ohne Rahmung außerhalb des freien Felds");
        var f = CombatFraming.Compute(0f, 0f, 0f, -range, yaw, pitch, 50f, 16f / 9f, OrbitCamera.DefaultDistance, OrbitCamera.MaxDistance);
        Assert.True(f.Distance >= OrbitCamera.DefaultDistance && f.Distance <= OrbitCamera.MaxDistance);
        if (f.Distance < OrbitCamera.MaxDistance)
        {
            Assert.True(CombatFraming.Fits(0f, 0f, f.FocusX, f.FocusZ, f.Distance, yaw, pitch, 50f, 16f / 9f), "Schiff im Bild");
            Assert.True(CombatFraming.Fits(0f, -range, f.FocusX, f.FocusZ, f.Distance, yaw, pitch, 50f, 16f / 9f), "Ziel im Bild");
        }
        Assert.True(f.FocusZ < 0f, "Blickpunkt rückt zum Ziel");
        // nahes Ziel braucht weniger Abstand als fernes
        var near = CombatFraming.Compute(0f, 0f, 0f, -10f, yaw, pitch, 50f, 16f / 9f, OrbitCamera.DefaultDistance, OrbitCamera.MaxDistance);
        Assert.True(near.Distance <= f.Distance);
    }

    /// <summary>
    /// Runde 14 (Handy-Test 2340×1080): unsymmetrisches freies Feld (oben Sektor-Chip/Lage, rechts Zielkarte und Tasten, unten Regler/Meldung) — die Mitte
    /// zwischen Schiff und Ziel liegt in der Mitte des freien Felds, beide passen mit Rand hinein; bei 30, 60 und 100 m.
    /// </summary>
    [Theory]
    [InlineData(30f)]
    [InlineData(60f)]
    [InlineData(100f)]
    public void Gefechtskamera_FreiesFeldUnsymmetrisch_BeideDarin(float range)
    {
        var cam = new OrbitCamera();
        float pitch = cam.PitchDeg, yaw = cam.YawDeg, aspect = 2340f / 1080f;
        var free = new CombatFraming.FreeRect(-0.55f, 0.35f, -0.5f, 0.6f);
        var f = CombatFraming.Compute(0f, 0f, 0f, -range, yaw, pitch, 50f, aspect, OrbitCamera.DefaultDistance, OrbitCamera.MaxDistance, free);
        if (range > 60f && f.Distance >= OrbitCamera.MaxDistance) { Assert.True(f.FocusZ < 0f); return; }   // 100 m: Höchstabstand reicht nicht ganz — Blickpunkt mittig, Kamera ganz heraus
        Assert.True(f.Distance < OrbitCamera.MaxDistance, $"passt ohne Höchstabstand ({f.Distance})");
        float tv = MathF.Tan(25f * MathF.PI / 180f), th = tv * aspect;
        foreach (var (px, pz) in new[] { (0f, 0f), (0f, -range) })
        {
            var (h, v) = CombatFraming.ScreenTan(px, pz, f.FocusX, f.FocusZ, f.Distance, yaw, pitch);
            float x = h / th, y = v / tv;
            Assert.InRange(x, free.X0, free.X1);
            Assert.InRange(y, free.Y0, free.Y1);
        }
        var (h1, v1) = CombatFraming.ScreenTan(0f, 0f, f.FocusX, f.FocusZ, f.Distance, yaw, pitch);
        var (h2, v2) = CombatFraming.ScreenTan(0f, -range, f.FocusX, f.FocusZ, f.Distance, yaw, pitch);
        Assert.Equal(free.CenterX, (h1 + h2) / 2f / th, 2);
        Assert.Equal(free.CenterY, (v1 + v2) / 2f / tv, 2);
    }

    /// <summary>Runde 15: Gefechtsrahmung mit Mindestgröße — im Kampf nah (Abstand füllt höchstens ~55 % des Felds), weit weg nur das Schiff, Station mit Radius.</summary>
    [Fact]
    public void Gefechtskamera_Kampf_Nah_Anflug_NurSchiff_StationMitRadius()
    {
        var cam = new OrbitCamera();
        float aspect = 2340f / 1080f;
        var free = new CombatFraming.FreeRect(-0.55f, 0.35f, -0.5f, 0.6f);
        var near = CombatFraming.ComputeCombat(0f, 0f, 0f, -30f, cam.YawDeg, cam.PitchDeg, 50f, aspect, OrbitCamera.MinDistance, 90f, OrbitCamera.DefaultDistance, free);
        Assert.True(near.Both);
        // so nah, dass der Abstand Schiff–Ziel gut die Hälfte des freien Felds füllt (höchstens FillShare)
        float tv0 = MathF.Tan(25f * MathF.PI / 180f);
        var (_, va) = CombatFraming.ScreenTan(0f, 0f, near.FocusX, near.FocusZ, near.Distance, cam.YawDeg, cam.PitchDeg);
        var (_, vb) = CombatFraming.ScreenTan(0f, -30f, near.FocusX, near.FocusZ, near.Distance, cam.YawDeg, cam.PitchDeg);
        float fill = MathF.Abs(va - vb) / tv0 / (free.Y1 - free.Y0);
        Assert.InRange(fill, 0.5f, CombatFraming.FillShare);
        var far = CombatFraming.ComputeCombat(0f, 0f, 0f, -200f, cam.YawDeg, cam.PitchDeg, 50f, aspect, OrbitCamera.MinDistance, 90f, OrbitCamera.DefaultDistance, free);
        Assert.False(far.Both);
        Assert.Equal(OrbitCamera.DefaultDistance, far.Distance, 1);
        float tv = MathF.Tan(25f * MathF.PI / 180f), th = tv * aspect;
        var (h, v) = CombatFraming.ScreenTan(0f, 0f, far.FocusX, far.FocusZ, far.Distance, cam.YawDeg, cam.PitchDeg);
        Assert.InRange(h / th, free.X0, free.X1);
        Assert.InRange(v / tv, free.Y0, free.Y1);
        var phone = new CombatFraming.FreeRect(-0.59f, 0.54f, -0.41f, 0.77f);   // gemessenes freies Feld im Handyformat (Selbsttest)
        var st = CombatFraming.ComputeCombat(0f, 0f, 0f, -45f, cam.YawDeg, cam.PitchDeg, 50f, aspect, OrbitCamera.MinDistance, OrbitCamera.MaxDistance, OrbitCamera.DefaultDistance, phone, 22f);
        Assert.True(st.Both, $"Station mit Radius gerahmt ({st.Distance})");
    }

    [Theory]
    [InlineData(120f, 1f)]   // weit weg: vorwärts
    [InlineData(22f + 31.5f, 0f)]   // im Halteband (0,7 × 45 m ab Außenhaut): halten
    [InlineData(22f + 5f, -1f)]   // zu nah: rückwärts
    public void Annaeherung_AufReichweiteHeranUndHalten(float centre, float sign)
    {
        float t = AttackApproach.Throttle(centre, 22f, 45f);
        Assert.Equal(sign, MathF.Sign(t));
        Assert.InRange(t, -Throttle.ReverseShare, 0.6f);
    }
}
