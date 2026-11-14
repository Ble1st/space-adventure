using SpaceSim.Core.Ui;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 31: Layout-Modul (UI-ANPASSUNG.md Etappe 1) — reine Rechnung für Fläche, Faktor, Ränder, Klasse und Spalten.</summary>
public class UiLayoutTests
{
    [Fact]
    public void Stretch_UndLogischeFlaeche_WieGodotExpand()
    {
        Assert.Equal(1.5f, UiLayout.StretchScale(2340, 1080), 3);
        var (w, h) = UiLayout.Logical(2340, 1080, 1f);
        Assert.Equal(1560f, w, 1); Assert.Equal(720f, h, 1);
        var (w2, h2) = UiLayout.Logical(2048, 1536, 1f);   // 4:3: Breite passt, Höhe wächst
        Assert.Equal(1280f, w2, 1); Assert.Equal(960f, h2, 1);
        var (w3, h3) = UiLayout.Logical(2340, 1080, 1.2f);
        Assert.Equal(1300f, w3, 1); Assert.Equal(600f, h3, 1);
    }

    [Fact]
    public void Tippflaeche_InMillimetern_UndDpiFaktor()
    {
        // Referenzgerät (Samsung A15): 56 px im Raster ≈ 5,4 mm; für 7 mm braucht es Faktor ≈ 1,3
        Assert.Equal(5.39f, UiLayout.Millimetres(56f, 2340, 1080, 1f, 396f), 1);
        float f = UiLayout.DpiFactor(2340, 1080, 396f);
        Assert.Equal(1.30f, f, 1);
        Assert.Equal(7f, UiLayout.Millimetres(56f, 2340, 1080, f, 396f), 1);
    }

    [Fact]
    public void Stufen_UndObergrenzeStarrerBildschirme()
    {
        // solange die Bildschirme starr 1280 × 720 sind, gibt es keinen Platz für einen Faktor über 1
        Assert.Equal(1f, UiLayout.FixedScreensCap(2340, 1080), 3);
        Assert.Equal(1f, UiLayout.FixedScreensCap(2048, 1536), 3);
        Assert.Equal(1f, UiLayout.Factor(UiSize.Normal, 2340, 1080, 396f, UiLayout.FixedScreensCap(2340, 1080)), 3);
        // ohne Obergrenze: klein = 1, normal = 1,15, groß = 1,3 auf dem Referenzgerät
        Assert.Equal(1f, UiLayout.Factor(UiSize.Small, 2340, 1080, 396f, 9f), 3);
        Assert.Equal(1.15f, UiLayout.Factor(UiSize.Normal, 2340, 1080, 396f, 9f), 3);
        Assert.Equal(1.3f, UiLayout.Factor(UiSize.Large, 2340, 1080, 396f, 9f), 3);
        // Tablet (226 ppi) braucht keinen Zuschlag: normal = 1
        Assert.Equal(1f, UiLayout.Factor(UiSize.Normal, 1920, 1200, 226f, 9f), 3);
        // Mindestfläche 960 × 540: auf dem Referenzgerät höchstens 720 ÷ 540
        Assert.Equal(720f / 540f, UiLayout.MaxFactor(2340, 1080, UiLayout.MinLogicalWidth, UiLayout.MinLogicalHeight), 3);
    }

    /// <summary>Runde 31 (Selbsttest headless: Fenster 0 × 0 ergab Faktor 0,78): nie unter 1, auch nicht bei ungültiger Fenstergröße.</summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(1280f, 720f)]
    [InlineData(960f, 540f)]
    public void Faktor_NieUnterEins(float w, float h)
    {
        foreach (var size in new[] { UiSize.Small, UiSize.Normal, UiSize.Large })
            foreach (float ppi in new[] { 0f, 96f, 160f, 396f })
                Assert.True(UiLayout.Factor(size, w, h, ppi, UiLayout.FixedScreensCap(w, h)) >= 1f);
    }

    [Fact]
    public void SafeArea_AllenVierRaendern_InLogischenEinheiten()
    {
        // Kamera-Aussparung links 90 px, Gestenleiste unten 48 px auf 2340 × 1080 (Maßstab 1,5)
        var i = UiLayout.Usable(2340, 1080, 90, 0, 2250, 1032, 1f);
        Assert.Equal(60f, i.Left, 2); Assert.Equal(0f, i.Top, 2); Assert.Equal(0f, i.Right, 2); Assert.Equal(32f, i.Bottom, 2);
        Assert.Equal(new UiInsets(0, 0, 0, 0), UiLayout.Usable(1280, 720, 0, 0, 0, 0, 1f));
    }

    [Fact]
    public void Klasse_Und_Spalten()
    {
        Assert.Equal(UiClass.Compact, UiLayout.Class(960, 540));
        Assert.Equal(UiClass.Normal, UiLayout.Class(1280, 720));
        var w = UiLayout.Columns(1000f, 10f, new UiColumn(200, 200, 0), new UiColumn(400, 500, 1), new UiColumn(200, 300, 0));
        Assert.Equal(980f, w.Sum(), 1);   // zwei Abstände
        Assert.Equal(200f, w[0], 1); Assert.Equal(290f, w[2], 1); Assert.Equal(490f, w[1], 1);   // Wunschzuschlag anteilig (90 %)
        var tight = UiLayout.Columns(500f, 0f, new UiColumn(400, 400, 0), new UiColumn(600, 600, 1));
        Assert.Equal(500f, tight.Sum(), 1);
        Assert.Equal(200f, tight[0], 1);
    }
}
