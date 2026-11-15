using SpaceSim.Core.Flight;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 33b: freies Feld bei belegter Bildmitte und Kamera-Blickpunkt, der das Schiff in dieses Feld legt.</summary>
public class Runde33bTests
{
    [Fact]
    public void FreiesFeld_MitteBelegt_GroesstesFeldDaneben()
    {
        var occ = new List<FreeField.Box> { new(560, 300, 180, 120), new(0, 0, 320, 280) };
        var b = FreeField.LargestNear(1280, 720, occ, 640, 360);
        Assert.False(b.X < 640 && b.X1 > 640 && b.Y < 360 && b.Y1 > 360, $"Mitte belegt, Feld {b} enthält sie nicht");
        foreach (var o in occ) Assert.False(b.X < o.X1 && o.X < b.X1 && b.Y < o.Y1 && o.Y < b.Y1, $"Feld {b} schneidet {o}");
        Assert.True(b.W * b.H > 0.25f * 1280 * 720);
        // Mitte frei: dasselbe wie Largest
        var free = new List<FreeField.Box> { new(0, 0, 320, 280) };
        Assert.Equal(FreeField.Largest(1280, 720, free, 640, 360), FreeField.LargestNear(1280, 720, free, 640, 360));
    }

    [Fact]
    public void Kamera_SchiffInDieMitteDesFreienFelds()
    {
        var field = new CombatFraming.FreeRect(0.1f, 0.9f, -0.5f, 0.3f);
        var f = CombatFraming.CenterShip(100f, -40f, 30f, 55f, 60f, 16f / 9f, 60f, field);
        var (h, v) = CombatFraming.ScreenTan(100f, -40f, f.FocusX, f.FocusZ, f.Distance, 30f, 55f);
        float tv = MathF.Tan(60f * MathF.PI / 360f), th = tv * 16f / 9f;
        Assert.Equal(field.CenterX, h / th, 2);
        Assert.Equal(field.CenterY, v / tv, 2);
    }
}
