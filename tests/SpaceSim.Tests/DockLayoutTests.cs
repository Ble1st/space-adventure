using SpaceSim.Core.Ui;
using Xunit;
using Xunit.Abstractions;

namespace SpaceSim.Tests;

/// <summary>Runde 34 (UI-ANPASSUNG.md Etappe E3): Gerüst der Dock-Schicht — reine Rechnung.</summary>
public class DockLayoutTests
{
    readonly ITestOutputHelper _out;
    public DockLayoutTests(ITestOutputHelper o) => _out = o;
    static readonly UiInsets None = new(0, 0, 0, 0);

    [Fact]
    public void Klassisch_1280x720_LagenWieBisher()
    {
        var s = DockLayout.Shell(1280, 720, None, ship: true);
        Assert.False(s.Compact);
        Assert.Equal(new UiRect(20, 10, 1240, 64), s.Top);
        Assert.Equal(new UiRect(14, 84, 200, 540), s.Rail);
        Assert.Equal(new UiRect(224, 84, 690, 540), s.Center);
        Assert.Equal(new UiRect(924, 84, 342, 540), s.Ship);
        Assert.Equal(new UiRect(14, 648, 160, 58), s.Back);
        Assert.Equal(new UiRect(1066, 648, 200, 58), s.Undock);
        Assert.Equal(940f, s.Credits.X - s.Top.X);
        Assert.Equal(420f, s.AlertSlot.X);
        var n = DockLayout.Shell(1280, 720, None, ship: false);
        Assert.Equal(new UiRect(224, 84, 1040, 540), n.Center);
        Assert.Equal(658f, s.InnerW); Assert.Equal(520f, s.InnerH);
        Assert.Equal(new UiRect(350, 30, 580, 662), DockLayout.MenuCard(1280, 720, None));
    }

    public static IEnumerable<object[]> Matrix()
    {
        foreach (var (w, h) in new[] { (1280, 720), (1560, 720), (2340, 1080), (1920, 1200), (2048, 1536), (960, 540), (2560, 1080) })
            foreach (float f in new[] { 1f, 1.15f, 1.3f })
                foreach (bool ins in new[] { false, true })
                    yield return new object[] { w, h, f, ins };
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void JedeFlaeche_GefitteterFaktor_AllesInnen_NichtsUeberlappt_Mindestflaeche(int w, int h, float wanted, bool insets)
    {
        var (w1, h1) = UiLayout.Logical(w, h, 1f);
        var i1 = insets ? new UiInsets(60, 20, 60, 30) : None;
        float f = DockLayout.FitFactor(w1, h1, i1, wanted);
        Assert.InRange(f, 1f, wanted + 1e-4f);
        var i = new UiInsets(i1.Left / f, i1.Top / f, i1.Right / f, i1.Bottom / f);
        foreach (bool ship in new[] { true, false })
        {
            var s = DockLayout.Shell(w1 / f, h1 / f, i, ship);
            var parts = new List<(string, UiRect)> { ("Top", s.Top), ("Rail", s.Rail), ("Center", s.Center), ("Back", s.Back), ("Undock", s.Undock) };
            if (ship) parts.Add(("Ship", s.Ship));
            foreach (var (n, r) in parts) Assert.True(r.Inside(s.Area), $"{w}×{h} {wanted}→{f:0.00} {n} {r} außerhalb {s.Area}");
            for (int a = 0; a < parts.Count; a++)
                for (int b = a + 1; b < parts.Count; b++)
                    Assert.False(parts[a].Item2.Intersects(parts[b].Item2), $"{w}×{h} {f:0.00}: {parts[a].Item1} {parts[a].Item2} × {parts[b].Item1} {parts[b].Item2}");
            if (f > 1f || s.Compact)   // bei Faktor 1 auf der Mindestfläche (960 × 540 ist logisch 1280 × 720) gilt der Entwurf
            {
                Assert.True(s.InnerH >= DockLayout.MinInnerH - 0.01f || f == 1f, $"Innenhöhe {s.InnerH}");
                Assert.True(s.InnerW >= (ship ? DockLayout.MinInnerShipW : DockLayout.MinInnerW) - 0.01f || f == 1f, $"Innenbreite {s.InnerW}");
            }
            Assert.True(s.Credits.Inside(s.Top), "Kasse in der Kopfleiste");
        }
        var m = DockLayout.MenuCard(w1 / f, h1 / f, i);
        Assert.True(m.Inside(new UiRect(i.Left, i.Top, w1 / f - i.Left - i.Right, h1 / f - i.Top - i.Bottom)), $"Menükarte {m}");
        if (!insets) _out.WriteLine($"{w}×{h} Stufe {wanted:0.00} → Dock {f:0.00}");
    }

    [Fact]
    public void Faktor_A15_Und16zu9_WerdenGesenktWoNoetig()
    {
        var (w, h) = UiLayout.Logical(2340, 1080, 1f);
        float a15 = DockLayout.FitFactor(w, h, None, 1.3f);
        var (w2, h2) = UiLayout.Logical(1280, 720, 1f);
        float p169 = DockLayout.FitFactor(w2, h2, None, 1.3f);
        _out.WriteLine($"A15 groß → {a15:0.00}, 16:9 groß → {p169:0.00}");
        Assert.True(a15 > 1.1f, $"A15 {a15}");
        Assert.True(p169 >= 1f && p169 <= a15);
    }

    [Theory]
    [InlineData(658f, 200f, 8f, 3)]
    [InlineData(1000f, 200f, 8f, 4)]
    [InlineData(150f, 200f, 8f, 1)]
    public void Rasterspalten(float width, float tile, float gap, int want) => Assert.Equal(want, DockLayout.GridColumns(width, tile, gap));
}
