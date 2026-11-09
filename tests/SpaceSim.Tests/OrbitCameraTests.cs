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
    public void OrbitDrag_BildschirmPixelWerdenZuWinkeln()
    {
        var c = new OrbitCamera();
        c.OrbitDrag(100f, 0f);    // 100 px nach rechts
        Assert.Equal(100f * OrbitCamera.DegPerPixelYaw, c.YawDeg, 3);
        c.OrbitDrag(0f, -100f);   // 100 px nach oben = Kamera steigt
        Assert.Equal(OrbitCamera.DefaultPitchDeg + 100f * OrbitCamera.DegPerPixelPitch, c.PitchDeg, 3);
        c.OrbitDrag(0f, -10000f); // weit nach oben ziehen: Pitch klemmt oben
        Assert.Equal(OrbitCamera.MaxPitchDeg, c.PitchDeg, 4);
        c.OrbitDrag(0f, 10000f);  // weit nach unten ziehen: Pitch klemmt unten
        Assert.Equal(OrbitCamera.MinPitchDeg, c.PitchDeg, 4);
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
}
