using SpaceSim.Core.Flight;

namespace SpaceSim.Tests;

/// <summary>Runde 44: Rückansicht (<see cref="CameraMode.Chase"/>) und gespeicherte Kamera-Einstellungen.</summary>
public class Runde44CameraTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(270f)]
    public void Rueckansicht_StehtNachEinschwingenHinterDemBug(float headingDeg)
    {
        var c = new OrbitCamera { Mode = CameraMode.Chase };
        float h = headingDeg * MathF.PI / 180f;
        for (int i = 0; i < 300; i++) c.FollowHeading(h, 1f / 60f);
        var o = c.Offset();
        // vorwärts = (−sin h, −cos h): die Kamera liegt dahinter
        float fx = -MathF.Sin(h), fz = -MathF.Cos(h);
        float len = MathF.Sqrt(o.X * o.X + o.Z * o.Z);
        Assert.True((-(o.X * fx + o.Z * fz)) / len > 0.99f, $"Kamera nicht hinter dem Bug bei {headingDeg}°");
        Assert.Equal(OrbitCamera.DefaultChaseHeight, o.Y, 2);
        Assert.Equal(OrbitCamera.DefaultChaseBack, len, 2);
        var la = c.LookAhead(h);
        Assert.True(la.X * fx + la.Z * fz > 0f, "Vorausblick liegt vor dem Bug");
    }

    [Fact]
    public void Rueckansicht_KuerzesterWegUeberNull()
    {
        var c = new OrbitCamera { Mode = CameraMode.Chase };
        c.SnapHeading(350f * MathF.PI / 180f);
        Assert.Equal(350f, c.YawDeg, 2);
        c.FollowHeading(10f * MathF.PI / 180f, 0.05f);
        // ein kleiner Schritt geht über 360 → 0, nicht rückwärts über 180
        Assert.True(c.YawDeg > 350f || c.YawDeg < 10f, $"Yaw {c.YawDeg}");
    }

    [Fact]
    public void Rueckansicht_NurSenkrechtesWischenWirkt_UndGrenzen()
    {
        var c = new OrbitCamera { Mode = CameraMode.Chase };
        c.SnapHeading(0f);
        float yaw = c.YawDeg;
        c.OrbitDrag(200f, 0f);
        Assert.Equal(yaw, c.YawDeg, 4);
        Assert.Equal(OrbitCamera.DefaultChaseHeight, c.ChaseHeight, 4);
        c.OrbitDrag(0f, 100f);
        Assert.Equal(OrbitCamera.DefaultChaseHeight + 100f * OrbitCamera.ChaseHeightPerPixel, c.ChaseHeight, 3);
        c.OrbitDrag(0f, 100000f);
        Assert.Equal(OrbitCamera.MaxChaseHeight, c.ChaseHeight, 3);
        c.OrbitDrag(0f, -100000f);
        Assert.Equal(OrbitCamera.MinChaseHeight, c.ChaseHeight, 3);
        c.SetChase(1000f, -5f);
        Assert.Equal(OrbitCamera.MaxChaseHeight, c.ChaseHeight, 3);
        Assert.Equal(OrbitCamera.MinChaseBack, c.ChaseBack, 3);
    }

    [Fact]
    public void Rueckansicht_ZoomSkaliertHoeheUndAbstandGemeinsam()
    {
        var c = new OrbitCamera { Mode = CameraMode.Chase };
        float ratio = c.ChaseHeight / c.ChaseBack;
        c.ZoomStep(+1);
        Assert.True(c.Distance > MathF.Sqrt(45f * 45f + 35f * 35f));
        Assert.Equal(ratio, c.ChaseHeight / c.ChaseBack, 3);
        for (int i = 0; i < 20; i++) c.ZoomStep(+1);
        Assert.True(c.ChaseHeight <= OrbitCamera.MaxChaseHeight && c.ChaseBack <= OrbitCamera.MaxChaseBack);
        for (int i = 0; i < 40; i++) c.Zoom(0.5f);
        Assert.True(c.ChaseHeight >= OrbitCamera.MinChaseHeight - 1e-3f && c.ChaseBack >= OrbitCamera.MinChaseBack - 1e-3f);
        // Freie Kamera: Rücksetzen lässt die Rückansicht-Werte in Ruhe
        c.SetChase(70f, 50f); c.Reset();
        Assert.Equal(70f, c.ChaseHeight, 3);
    }

    [Fact]
    public void Rueckansicht_FreierModusUnveraendert()
    {
        var c = new OrbitCamera();
        c.FollowHeading(1f, 1f);   // wirkt nur in der Rückansicht
        Assert.Equal(0f, c.YawDeg, 4);
        Assert.Equal((0f, 0f), c.LookAhead(1f));
        Assert.Equal(OrbitCamera.LegacyOffsetY, c.Offset().Y, 3);
    }

    [Fact]
    public void Prefs_RoundtripUndKaputteWerte()
    {
        var c = new OrbitCamera { Mode = CameraMode.Chase, InvertDrag = true };
        c.SetChase(60f, 20f);
        var p = CameraPrefs.Of(c);
        var v = p.ToValues();
        var back = CameraPrefs.FromValues(v.Mode, v.Height, v.Back, v.Invert);
        Assert.Equal(p, back);
        var c2 = new OrbitCamera();
        back.ApplyTo(c2);
        Assert.Equal(CameraMode.Chase, c2.Mode);
        Assert.Equal(60f, c2.ChaseHeight, 3);
        Assert.True(c2.InvertDrag);

        var bad = CameraPrefs.FromValues("unsinn", float.NaN, 99999f, false);
        Assert.Equal(CameraMode.Free, bad.Mode);
        Assert.Equal(OrbitCamera.DefaultChaseHeight, bad.ChaseHeight);
        Assert.Equal(OrbitCamera.MaxChaseBack, bad.ChaseBack);
        Assert.Equal(CameraMode.TargetLock, CameraPrefs.FromValues("lock", 45, 35, false).Mode);
        Assert.Equal(CameraMode.Free, CameraPrefs.FromValues(null, 45, 35, false).Mode);
    }
}
