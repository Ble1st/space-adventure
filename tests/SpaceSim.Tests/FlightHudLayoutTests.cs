using SpaceSim.Core.Ui;
using Xunit;
using Xunit.Abstractions;

namespace SpaceSim.Tests;

/// <summary>Runde 33 (UI-ANPASSUNG.md Etappe 2): Zonen des Flug-HUD — reine Rechnung, unabhängig von Godot.</summary>
public class FlightHudLayoutTests
{
    readonly ITestOutputHelper _out;
    public FlightHudLayoutTests(ITestOutputHelper o) => _out = o;

    static readonly UiInsets None = new(0, 0, 0, 0);

    /// <summary>Logische Flächen der sechs Prüfauflösungen bei den UI-Faktoren 1 / 1,15 / 1,3 (wie --ui-check), dazu die Mindestfläche 960 × 540.</summary>
    public static IEnumerable<object[]> Areas()
    {
        var seen = new HashSet<(int, int)>();
        foreach (var (w, h) in new[] { (1280, 720), (1560, 720), (2340, 1080), (1920, 1200), (2048, 1536), (960, 540) })
            foreach (float f in new[] { 1f, 1.15f, 1.3f })
            {
                var (lw, lh) = UiLayout.Logical(w, h, f);
                if (seen.Add(((int)MathF.Round(lw), (int)MathF.Round(lh)))) yield return new object[] { lw, lh };
            }
        yield return new object[] { 960f, 540f };
    }

    static List<(HudItem Item, UiRect R)> Visible(FlightHudLayout l, int bays) =>
        l.Rects.Where(kv => !l.Hidden(kv.Key) && !(kv.Key >= HudItem.Bay0 && kv.Key <= HudItem.Bay5 && kv.Key - HudItem.Bay0 >= bays))
            .Select(kv => (kv.Key, kv.Value)).ToList();

    [Fact]
    public void Klassisch_1280x720_LagenUnveraendert()
    {
        var l = FlightHudLayout.Compute(1280, 720, None, 4);
        Assert.False(l.Compact);
        Assert.Equal(new UiRect(14, 12, 300, 200), l[HudItem.Status]);
        Assert.Equal(new UiRect(420, 12, 430, 42), l[HudItem.Chip]);
        Assert.Equal(new UiRect(862, 8, 160, 56), l[HudItem.Command]);
        Assert.Equal(new UiRect(930, 72, 340, 132), l[HudItem.Card]);
        Assert.Equal(new UiRect(660, 646, 600, 68), l[HudItem.Slider]);
        Assert.Equal(new UiRect(470, 590, 80, 56), l[HudItem.Reverse]);
        Assert.Equal(new UiRect(1090, 330, 176, 56), l[HudItem.Bay0]);
        Assert.Equal(new UiRect(906, 516, 176, 56), l[HudItem.Bay5]);
        Assert.Equal(new UiRect(1090, 574, 176, 56), l[HudItem.AllFire]);
        Assert.Equal(new UiRect(906, 574, 176, 56), l[HudItem.AutoFire]);
        Assert.Equal(new UiRect(764, 206, 160, 56), l[HudItem.NextEnemy]);
        Assert.Equal(new UiRect(590, 534, 310, 28), l[HudItem.HitWarning]);
        Assert.Equal(462f, FlightHudLayout.Compute(1280, 720, None, 1)[HudItem.AllFire].Y);
    }

    [Fact]
    public void Klassisch_Breit_UndHoch_RaenderUndUntereZone()
    {
        // 19,5:9 bei Faktor 1 (wie bisher waagerecht verankert)
        var w = FlightHudLayout.Compute(1560, 720, None, 2);
        Assert.Equal(1090f + 280f, w[HudItem.Bay0].X);
        Assert.Equal(420f + 140f, w[HudItem.Chip].X);
        Assert.Equal(14f, w[HudItem.Status].X);
        // 4:3-Tablet: untere Zone am unteren Rand, obere bleibt oben
        var t = FlightHudLayout.Compute(1280, 960, None, 2);
        Assert.Equal(960f - 6f, t[HudItem.Slider].Bottom);
        Assert.Equal(960f - 16f, t[HudItem.Hamburger].Bottom);
        Assert.Equal(12f, t[HudItem.Status].Y);
        Assert.Equal(330f, t[HudItem.Bay0].Y);
        // Safe Area unten/oben (Gestenleiste, Aussparung)
        var s = FlightHudLayout.Compute(1400, 800, new UiInsets(40, 20, 30, 24), 2);
        Assert.False(s.Compact);
        Assert.Equal(800f - 24f - 6f, s[HudItem.Slider].Bottom);
        Assert.Equal(54f, s[HudItem.Status].X);
        Assert.Equal(32f, s[HudItem.Status].Y);
        Assert.Equal(1400f - 30f - 10f, s[HudItem.Card].Right);
    }

    /// <summary>Runde 33b: sechs Auflösungen × drei Faktoren (wie --ui-check) × 0–6 Schächte × ohne/mit pessimistischen Safe-Area-Rändern.</summary>
    public static IEnumerable<object[]> Matrix()
    {
        foreach (var (w, h) in new[] { (1280, 720), (1560, 720), (2340, 1080), (1920, 1200), (2048, 1536), (960, 540) })
            foreach (float f in new[] { 1f, 1.15f, 1.3f })
                foreach (bool insets in new[] { false, true })
                    yield return new object[] { w, h, f, insets };
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void JedeFlaeche_JedeSchachtzahl_AllesInnen_NichtsUeberlappt(int w, int h, float factor, bool insets)
    {
        var (w1, h1) = UiLayout.Logical(w, h, 1f);
        var ins1 = insets ? new UiInsets(60, 20, 60, 30) : None;
        for (int bays = 0; bays <= FlightHudLayout.MaxBays; bays++)
        {
            float f = FlightHudLayout.FitFactor(w1, h1, ins1, bays, factor);
            Assert.InRange(f, 1f, factor + 1e-4f);
            var l = FlightHudLayout.Compute(w1 / f, h1 / f, new UiInsets(ins1.Left / f, ins1.Top / f, ins1.Right / f, ins1.Bottom / f), bays);
            Assert.Equal(0, l.Overflow);
            Check(l, bays, $"{w}×{h} Faktor {factor} → {f:0.00}{(insets ? " mit Rändern" : "")}, Schächte {bays}");
        }
    }

    [Theory]
    [MemberData(nameof(Areas))]
    public void MindestflaecheUndAlleFlaechen_ViereSchaechte_OhneSenken(float w, float h)
    {
        // vier Schächte (Wächter, Falke, Bollwerk ohne Startwaffen …) passen auf jede Prüffläche ohne Absenken des Faktors
        var l = FlightHudLayout.Compute(w, h, None, 4);
        Assert.Equal(0, l.Overflow);
        Check(l, 4, $"{w:0}×{h:0} vier Schächte");
    }

    [Fact]
    public void Kleinste_Flaeche_FuenfUndSechsSchaechte_FaktorWirdGesenkt_NieDoppelt()
    {
        // 985 × 554 (16:9 bei 1,3): vier Schächte + sechs Kampftasten passen; mit fünf/sechs Schächten (Bollwerk, Hornisse, Sturmvogel, Leviathan) lägen
        // sonst „Ergeben“ auf „Angriff“ bzw. zusätzlich „Ziel folgen“ — der Faktor sinkt, bis jede Taste ihre Zelle hat
        var (w, h) = UiLayout.Logical(1280, 720, 1f);
        Assert.Equal(1.3f, FlightHudLayout.FitFactor(w, h, None, 4, 1.3f), 3);
        Assert.True(FlightHudLayout.Compute(w / 1.3f, h / 1.3f, None, 5).Overflow > 0);
        float f5 = FlightHudLayout.FitFactor(w, h, None, 5, 1.3f), f6 = FlightHudLayout.FitFactor(w, h, None, 6, 1.3f);
        Assert.True(f5 < 1.3f && f6 <= f5 && f6 >= 1f, $"Faktoren {f5} / {f6}");
        _out.WriteLine($"16:9 bei 1,3: vier Schächte 1,30 · fünf {f5:0.00} · sechs {f6:0.00}");
        // A15 bei 1,3 mit pessimistischen Rändern: schon bei vier Schächten gesenkt, aber ohne Doppelbelegung
        var (aw, ah) = UiLayout.Logical(2340, 1080, 1f);
        float fa = FlightHudLayout.FitFactor(aw, ah, new UiInsets(60, 20, 60, 30), 4, 1.3f);
        _out.WriteLine($"A15 mit Rändern 60/20/60/30, vier Schächte: {fa:0.00}");
        Assert.InRange(fa, 1f, 1.3f);
    }

    [Theory]
    [InlineData(1200f, 554f)]    // A15 bei Faktor 1,3
    [InlineData(1357f, 626f)]    // A15 bei Faktor 1,15
    [InlineData(1113f, 696f)]    // 16:10 bei 1,15 (16:9 bei 1,15 wäre 1113 × 626)
    [InlineData(1280f, 720f)]
    [InlineData(1560f, 720f)]
    public void SechsSchaechte_PassenAbMittlererBreite(float w, float h)
    {
        var l = FlightHudLayout.Compute(w, h, None, 6);   // ohne Absenken des Faktors
        Assert.Equal(0, l.Overflow);
        Check(l, 6, $"{w:0}×{h:0} sechs Schächte");
    }

    void Check(FlightHudLayout l, int bays, string what)
    {
        var vis = Visible(l, bays);
        foreach (var (item, r) in vis)
        {
            if (r.W <= 0f) continue;
            Assert.True(r.Inside(l.Area), $"{what}: {item} {r} außerhalb {l.Area}");
        }
        for (int i = 0; i < vis.Count; i++)
            for (int j = i + 1; j < vis.Count; j++)
            {
                var (a, ra) = vis[i]; var (b, rb) = vis[j];
                if (ra.W <= 0f || rb.W <= 0f) continue;
                // Runde 33b: Tasten nie übereinander (auch keine gemeinsame Zelle); Beschriftungen/Warnzeilen nie auf Tasten; Beschriftung gegen Beschriftung nur im
                // Kompaktmodus (klassisch berühren sich „RÜCK · max 20 %“ und die Fluchtzeile seit jeher, beide ohne Taste)
                bool anyInteractive = FlightHudLayout.IsInteractive(a) || FlightHudLayout.IsInteractive(b);
                if (!anyInteractive && !l.Compact) continue;
                Assert.False(ra.Intersects(rb), $"{what}: {a} {ra} überlappt {b} {rb}");
            }
    }

    [Fact]
    public void Kompakt_SchieberUndKampfzellen_Drucken()
    {
        var (w, h) = UiLayout.Logical(1280, 720, 1.3f);
        var l = FlightHudLayout.Compute(w, h, None, 4);
        Assert.True(l.Compact);
        Assert.True(l[HudItem.Slider].W >= 320f, $"Schubregler {l[HudItem.Slider]}");
        Assert.True(l[HudItem.Chip].W >= 200f, $"Sektorchip {l[HudItem.Chip]}");
        Assert.True(l[HudItem.Security].W >= 300f, $"Lagezeile {l[HudItem.Security]}");
        foreach (var kv in l.Rects.OrderBy(k => k.Key)) _out.WriteLine($"{kv.Key,-13} {kv.Value}{(l.Hidden(kv.Key) ? " (aus)" : "")}");
    }

    [Fact]
    public void FindSpot_WeichtBelegtenFlaechenAus()
    {
        var area = new UiRect(0, 0, 1000, 600);
        var occ = new List<UiRect> { new(300, 400, 400, 100) };
        var r = FlightHudLayout.FindSpot(area, occ, 536, 62, 380, 500, 420);
        Assert.NotNull(r);
        Assert.False(r!.Value.Intersects(occ[0]));
        Assert.True(r.Value.Inside(area));
        // frei: bleibt genau an der Wunschlage
        var free = FlightHudLayout.FindSpot(area, new List<UiRect>(), 536, 62, 380, 500, 420);
        Assert.Equal(new UiRect(232, 420, 536, 62), free);
        // kein Platz
        Assert.Null(FlightHudLayout.FindSpot(area, new List<UiRect> { area }, 536, 62, 380, 500, 420));
    }
}
