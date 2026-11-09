using SpaceSim.Core.Model;
using SpaceSim.Core.Weapons;

namespace SpaceSim.Tests;

public class FiringArcTests
{
    static readonly Vec2 Ship = new(0, 0);

    static WeaponBay Bay(Facing facing, float arc, float range = 1000f)
        => new() { Key = "t", Name = "T", Facing = facing, ArcDegrees = arc, Range = range };

    [Fact]
    public void Turm_EnthaeltJedeRichtung()
    {
        var turret = Bay(Facing.Bow, 360f);
        for (float b = 0; b < 360f; b += 15f)
        {
            var dir = new Vec2(-MathF.Sin(b * MathF.PI / 180f), -MathF.Cos(b * MathF.PI / 180f));
            Assert.True(FiringArc.Contains(turret, 0f, Ship, dir * 50f), $"Peilung {b}");
        }
    }

    [Theory]
    [InlineData(0f, true)]     // direkt voraus
    [InlineData(40f, true)]    // Grenze noch drin
    [InlineData(-40f, true)]
    [InlineData(41f, false)]
    [InlineData(-41f, false)]
    [InlineData(180f, false)]
    public void BugArc40_EnthaeltNurDenKegel(float targetBearing, bool expected)
    {
        var bay = Bay(Facing.Bow, 40f);
        var target = new Vec2(-MathF.Sin(targetBearing * MathF.PI / 180f), -MathF.Cos(targetBearing * MathF.PI / 180f)) * 30f;
        Assert.Equal(expected, FiringArc.Contains(bay, 0f, Ship, target));
    }

    [Fact]
    public void Backbord90_Arc50()
    {
        var bay = Bay(Facing.Port, 50f);
        Vec2 At(float b) => new Vec2(-MathF.Sin(b * MathF.PI / 180f), -MathF.Cos(b * MathF.PI / 180f)) * 30f;
        Assert.True(FiringArc.Contains(bay, 0f, Ship, At(90f)));    // Mitte des Backbord-Kegels
        Assert.True(FiringArc.Contains(bay, 0f, Ship, At(140f)));   // 90+50 noch drin
        Assert.False(FiringArc.Contains(bay, 0f, Ship, At(30f)));   // 90-60 draußen
        Assert.False(FiringArc.Contains(bay, 0f, Ship, At(0f)));    // Bug liegt nicht im Backbord-Winkel
    }

    [Fact]
    public void DrehungDesSchiffsVerschiebtDenKegel()
    {
        var bay = Bay(Facing.Bow, 40f);
        // Schiff zeigt auf Peilung 90 (nach Backbord): Ziel auf Peilung 90 liegt voraus
        Vec2 At(float b) => new Vec2(-MathF.Sin(b * MathF.PI / 180f), -MathF.Cos(b * MathF.PI / 180f)) * 30f;
        Assert.True(FiringArc.Contains(bay, 90f, Ship, At(90f)));
        Assert.False(FiringArc.Contains(bay, 90f, Ship, At(0f)));
    }

    [Fact]
    public void TurnHint_ZeigtRichtungUndBetrag()
    {
        var bay = Bay(Facing.Bow, 40f);
        Vec2 At(float b) => new Vec2(-MathF.Sin(b * MathF.PI / 180f), -MathF.Cos(b * MathF.PI / 180f)) * 30f;
        Assert.Equal(81f, FiringArc.TurnHintDegrees(bay, 0f, Ship, At(81f)), 3);    // links → „◀ 81° drehen“
        Assert.Equal(-30f, FiringArc.TurnHintDegrees(bay, 0f, Ship, At(-30f)), 3);  // rechts → „▶ 30° drehen“
        Assert.Equal(0f, FiringArc.TurnHintDegrees(bay, 0f, Ship, At(0f)), 3);
    }

    [Fact]
    public void Normalize180_WickeltKorrekt()
    {
        Assert.Equal(-170f, FiringArc.Normalize180(190f), 3);
        Assert.Equal(170f, FiringArc.Normalize180(-190f), 3);
        Assert.Equal(0f, FiringArc.Normalize180(360f), 3);
        Assert.Equal(-180f, FiringArc.Normalize180(180f), 3);
    }

    [Fact]
    public void State_KeinZielIstNoTarget()
    {
        var bay = Bay(Facing.Bow, 40f, range: 40f);
        Assert.Equal(BayState.NoTarget, FiringArc.State(bay, 0f, Ship, null, 0f, reloaded: true, 1f));
    }

    [Fact]
    public void State_AusserhalbReichweiteIstNoTarget_AsteroidenRadiusZaehlt()
    {
        var bay = Bay(Facing.Bow, 40f, range: 40f);
        var rock = new Vec2(48f, 0f);   // Kantenabstand 48 - 8 = 40 = noch in Reichweite
        Assert.Equal(BayState.Ready, FiringArc.State(bay, -90f, Ship, rock, 8f, reloaded: true, 1f));
        var far = new Vec2(49.5f, 0f);  // Kantenabstand 41.5 > 40
        Assert.Equal(BayState.NoTarget, FiringArc.State(bay, -90f, Ship, far, 8f, reloaded: true, 1f));
    }

    [Fact]
    public void State_NachladenSchlaegtAussenWinkel()
    {
        var bay = Bay(Facing.Bow, 40f, range: 100f);
        var target = new Vec2(0f, -30f);   // Peilung 0, im Bug-Winkel
        Assert.Equal(BayState.Reloading, FiringArc.State(bay, 0f, Ship, target, 0f, reloaded: false, 0.3f));
        var outside = new Vec2(30f, 0f);  // Peilung -90, außerhalb
        Assert.Equal(BayState.Reloading, FiringArc.State(bay, 0f, Ship, outside, 0f, reloaded: false, 0.3f));
        Assert.Equal(BayState.OutsideArc, FiringArc.State(bay, 0f, Ship, outside, 0f, reloaded: true, 1f));
        Assert.Equal(BayState.Ready, FiringArc.State(bay, 0f, Ship, target, 0f, reloaded: true, 1f));
    }

    [Fact]
    public void PlayerDefault_LaserUndTraktorAlsTuerme()
    {
        var bays = WeaponBays.PlayerDefault;
        Assert.Equal(2, bays.Count);
        Assert.All(bays, b => Assert.True(b.IsTurret));   // 360°: Spielverhalten ändert sich nicht
        Assert.Equal("laser", bays[0].Key);
        Assert.Equal("tractor", bays[1].Key);
    }
}
