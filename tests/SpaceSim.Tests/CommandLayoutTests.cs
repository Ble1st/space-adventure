using SpaceSim.Core.Ui;
using Xunit;
using Xunit.Abstractions;

namespace SpaceSim.Tests;

/// <summary>Runde 35 (UI-ANPASSUNG.md Etappe E4): Gerüst der Kommandozentrale — reine Rechnung.</summary>
public class CommandLayoutTests
{
    readonly ITestOutputHelper _out;
    public CommandLayoutTests(ITestOutputHelper o) => _out = o;
    static readonly UiInsets None = new(0, 0, 0, 0);

    /// <summary>UI-Umbau (genehmigt): Reiterleiste oben mit „← Flug“ links und Kasse rechts, Unterreiter darunter, Inhalt über die ganze Breite.</summary>
    [Fact]
    public void Reiter_1280x720_KopfleisteUnterreiterInhaltVolleBreite()
    {
        var s = CommandLayout.Shell(1280, 720, None);
        Assert.False(s.Compact);
        Assert.Equal(new UiRect(10, 8, 1260, 64), s.Top);
        Assert.Equal(new UiRect(16, 12, 120, 56), s.Back);
        Assert.True(s.Rail.X > s.Back.Right && s.Rail.Right < s.Credits.X && s.Rail.W > s.Rail.H, $"Reiter waagerecht {s.Rail}");
        Assert.Equal(new UiRect(10, 80, 1260, 50), s.SubRail);
        Assert.Equal(new UiRect(10, 136, 1260, 518), s.Center);
        Assert.Equal(1240f, s.InnerW); Assert.Equal(498f, s.InnerH);
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
    public void JedeFlaeche_VollerFaktor_MindestflaecheUndNichtsUeberlappt(int w, int h, float wanted, bool insets)
    {
        var (w1, h1) = UiLayout.Logical(w, h, 1f);
        var i1 = insets ? new UiInsets(60, 20, 60, 30) : None;
        float f = CommandLayout.FitFactor(w1, h1, i1, wanted);
        Assert.InRange(f, 1f, wanted + 1e-4f);
        var s = CommandLayout.Shell(w1 / f, h1 / f, new UiInsets(i1.Left / f, i1.Top / f, i1.Right / f, i1.Bottom / f));
        Assert.True(s.InnerW >= CommandLayout.MinInnerW - 0.01f && s.InnerH >= CommandLayout.MinInnerH - 0.01f, $"{w}×{h} {f:0.00}: Innen {s.InnerW}×{s.InnerH}");
        // UI-Umbau: „← Flug“, Reiter und Kasse liegen in der Kopfleiste
        foreach (var (n, r) in new[] { ("Back", s.Back), ("Rail", s.Rail), ("Credits", s.Credits) }) Assert.True(r.Inside(s.Top), $"{n} {r} außerhalb der Kopfleiste {s.Top}");
        var parts = new[] { ("Top", s.Top), ("Unterreiter", s.SubRail), ("Center", s.Center), ("Streifen", s.MessageStrip) };
        foreach (var (n, r) in parts) Assert.True(r.Inside(s.Area), $"{n} {r} außerhalb {s.Area}");
        Assert.False(s.Back.Intersects(s.Rail) || s.Rail.Intersects(s.Credits), "Kopfleiste überlappt");
        for (int a = 0; a < parts.Length; a++)
            for (int b = a + 1; b < parts.Length; b++)
                Assert.False(parts[a].Item2.Intersects(parts[b].Item2), $"{w}×{h} {f:0.00}: {parts[a].Item1} × {parts[b].Item1}");
        // aus E3 gelernt: ohne Ränder erreicht jede Prüffläche den vollen Faktor der Stufe
        if (!insets) Assert.Equal(wanted, f, 2);
        if (!insets) _out.WriteLine($"{w}×{h} Stufe {wanted:0.00} → Kommandozentrale {f:0.00}, Innen {s.InnerW:0}×{s.InnerH:0}");
    }
}
