using SpaceSim.Core.Flight;

namespace SpaceSim.Tests;

public class ThrottleTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0.25f, 0.25f)]
    [InlineData(2f, 1f)]          // gekappt
    [InlineData(-1f, 0f)]         // der Slider hat keinen Rückwärtsbereich mehr
    [InlineData(-0.5f, 0f)]
    public void SliderToFraction_MapptUndKappt(float slider, float expected)
        => Assert.Equal(expected, Throttle.SliderToFraction(slider), 4);

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void ReverseHeld_FaehrtImmer20ProzentRueckwaerts_UnabhaengigVomSlider(float slider)
        => Assert.Equal(-Throttle.ReverseShare, Throttle.Fraction(slider, reverseHeld: true), 4);

    [Fact]
    public void ReverseReleased_GiltWiederDerSlider()
    {
        Assert.Equal(0.5f, Throttle.Fraction(0.5f, reverseHeld: false), 4);
        Assert.Equal(0f, Throttle.Fraction(0f, reverseHeld: false), 4);
    }

    [Fact]
    public void TargetSpeed_ReverseIstAuf20ProzentBegrenzt()
    {
        Assert.Equal(45f, Throttle.TargetSpeed(1f, false, 45f));
        Assert.Equal(22.5f, Throttle.TargetSpeed(0.5f, false, 45f));
        Assert.Equal(0f, Throttle.TargetSpeed(0f, false, 45f));
        Assert.Equal(-9f, Throttle.TargetSpeed(1f, true, 45f));   // -20 % von 45
        Assert.True(Throttle.TargetSpeed(1f, true, 45f) >= -45f * Throttle.ReverseShare - 1e-4f);
    }

    [Fact]
    public void SliderToFraction_IstMonotonSteigend()
    {
        float prev = Throttle.SliderToFraction(0f);
        for (float s = 0.05f; s <= 1.01f; s += 0.05f)
        {
            float f = Throttle.SliderToFraction(s);
            Assert.True(f >= prev - 1e-6f, $"nicht monoton bei {s}");
            prev = f;
        }
    }
}
