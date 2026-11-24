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

    static List<(HudItem Item, UiRect R)> Visible(FlightHudLayout l) =>
        l.Rects.Where(kv => !l.Hidden(kv.Key) && kv.Value.W > 0f).Select(kv => (kv.Key, kv.Value)).ToList();

    /// <summary>HUD-Neu: auf jeder Prüffläche, mit und ohne Ränder, für 0–6 Gruppen und 0–15 Einzelschächte (auf und zu) — nach dem Faktor, den
    /// <see cref="FlightHudLayout.FitFactor"/> wählt, liegt alles innen und keine Taste auf einer anderen; jede Taste ist mindestens 64 groß.</summary>
    [Theory]
    [InlineData(1280, 720, 1.3f, false)]
    [InlineData(1280, 720, 1.3f, true)]
    [InlineData(1560, 720, 1.15f, true)]
    [InlineData(1560, 720, 1.3f, true)]
    [InlineData(2340, 1080, 1.3f, true)]
    [InlineData(1920, 1200, 1.3f, false)]
    [InlineData(2048, 1536, 1.3f, false)]
    [InlineData(960, 540, 1f, false)]
    public void JedeFlaeche_JedeGruppenUndSchachtzahl_AllesInnen_NichtsUeberlappt(int w, int h, float factor, bool insets)
    {
        var (w1, h1) = UiLayout.Logical(w, h, 1f);
        var safe1 = insets ? new UiInsets(53, 0, 0, 0) : None;
        foreach (int groups in new[] { 0, 1, 3, 6 })
            foreach (int bays in new[] { 0, 2, 4, 10 })   // UI-Umbau: höchstens 10 Einzelschächte (Leviathan)
                foreach (bool exp in new[] { false, true })
                {
                    float f = FlightHudLayout.FitFactor(w1, h1, safe1, groups, bays, factor);
                    var l = FlightHudLayout.Compute(w1 / f, h1 / f, new UiInsets(safe1.Left / f, 0, 0, 0), groups, bays, exp);
                    Assert.True(l.Overflow == 0, $"{w}×{h} f{f:0.00} g{groups} b{bays} auf {exp}: Überlauf {l.Overflow}");
                    var vis = Visible(l);
                    foreach (var (it, r) in vis)
                    {
                        Assert.True(r.Inside(l.Area, 0.5f), $"{it} {r} außerhalb {l.Area} ({w}×{h} f{f:0.00} g{groups} b{bays} {exp})");
                        if (FlightHudLayout.IsInteractive(it)) Assert.True(r.W >= FlightHudLayout.Tap - 0.01f && r.H >= FlightHudLayout.Tap - 0.01f, $"{it} {r} kleiner als 64");
                    }
                    var taps = vis.Where(v => FlightHudLayout.IsInteractive(v.Item)).ToList();
                    for (int a = 0; a < taps.Count; a++)
                        for (int b = a + 1; b < taps.Count; b++)
                            Assert.False(taps[a].R.Intersects(taps[b].R, -0.5f), $"{taps[a].Item} {taps[a].R} × {taps[b].Item} {taps[b].R} ({w}×{h} f{f:0.00} g{groups} b{bays} {exp})");
                }
    }

    [Fact]
    public void A15_Normal_KeinSenken_Mit3GruppenUnd10Schaechten_Tasten7mm()
    {
        // A15 bei „normal“: 1560 × 720 logisch, Aussparung links 53, Faktor 1,15 — Leviathan: 3 Gruppen, 10 Schächte
        float f = FlightHudLayout.FitFactor(1560, 720, new UiInsets(53, 0, 0, 0), 3, 10, 1.15f);
        Assert.Equal(1.15f, f, 3);
        Assert.True(UiLayout.Millimetres(FlightHudLayout.Tap, 2340, 1080, f, 396f) >= 7f);
        var l = FlightHudLayout.Compute(1560 / f, 720 / f, new UiInsets(53 / f, 0, 0, 0), 3, 10, expanded: true);
        for (int b = 0; b < 10; b++) Assert.False(l.Hidden(HudItem.Bay0 + b));
        Assert.True(l.Hidden(HudItem.Context));
        Assert.False(l.Hidden(HudItem.Expand));
    }

    /// <summary>UI-Umbau (Nutzerwunsch nach Interstellar Pilot 2): Kopfleiste oben, Regler waagerecht unten rechts, „☰ Sektor“ unten links, Geräte am rechten Rand.</summary>
    [Fact]
    public void Anordnung_KopfleisteReglerUntenRechtsSektorUntenLinks()
    {
        var l = FlightHudLayout.Compute(1280, 720, None, 3, 0, false);
        Assert.Equal(0f, l[HudItem.TopBar].Y);
        Assert.Equal(1280f, l[HudItem.TopBar].W);
        foreach (var it in new[] { HudItem.Hamburger, HudItem.Command, HudItem.Fast })
            Assert.True(l[it].Bottom <= FlightHudLayout.BarH + 0.01f, $"{it} in der Kopfleiste");
        Assert.True(l.Hidden(HudItem.Map) && l.Hidden(HudItem.Comms), "Karte und Nachrichten im Regal unten links");
        var s = l[HudItem.Slider];
        Assert.True(s.W > s.H && s.Right > 1240f && s.Bottom > 690f, $"Regler waagerecht unten rechts {s}");
        Assert.True(l[HudItem.Scan].X < 40f && l[HudItem.Scan].Bottom > 690f, "„☰ Sektor“ unten links");
        Assert.True(l[HudItem.TurnLeft].X > l[HudItem.Scan].Right && l[HudItem.TurnRight].X > l[HudItem.TurnLeft].Right);
        Assert.True(l[HudItem.Reverse].Right <= s.X);
        Assert.True(l[HudItem.FireBtn].Right > 1100f && l[HudItem.FireBtn].Bottom <= s.Y);   // FEUER am rechten Rand über dem Regler
        Assert.True(l[HudItem.Power].Right > 1260f && l[HudItem.Power].Y >= l[HudItem.Card].Bottom);
        var c = l[HudItem.Context];
        Assert.True(c.X > l[HudItem.TurnRight].Right && c.Right < l[HudItem.Reverse].X, $"Kontexttaste unten Mitte {c}");
        Assert.True(l.Hidden(HudItem.Expand) && l.Hidden(HudItem.Bay0));
        Assert.Equal(0, l.Overflow);
        var none = FlightHudLayout.Compute(1280, 720, None, 2, 2, false, fire: false);
        Assert.True(none.Hidden(HudItem.FireBtn));
        Assert.Equal(0, none.Overflow);
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

    [Fact]
    public void Runde35b_FindSpot_EngeLueckeZwischenFlaechen_WirdGefunden()
    {
        // Lücke 343 × 95 zwischen zwei Reihen (Raster 8/24 trifft sie nicht genau): die Kantensuche findet sie
        var area = new UiRect(0, 0, 985, 554);
        var occ = new List<UiRect>
        {
            new(0, 0, 985, 258.4f),          // oben alles belegt
            new(0, 353.3f, 985, 200.7f),     // unten alles belegt
            new(0, 258.4f, 636.2f, 95f),      // links in der Lücke belegt
        };
        var spot = FlightHudLayout.FindSpot(area, occ, 336f, 92f, 336f, 492f, 400f);
        Assert.NotNull(spot);
        Assert.True(spot!.Value.Inside(area));
        Assert.DoesNotContain(occ, o => o.Intersects(spot.Value));
        Assert.Equal(336f, spot.Value.W);
    }
}
