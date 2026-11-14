using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 16 (dritter Handy-Test der Gefechtskamera): freies Bildfeld, „Nächster Gegner“ im Gefecht, Munitionshinweis, laufende Kosten.</summary>
public class Runde16Tests
{
    [Fact]
    public void FreiesFeld_TastenspalteRechtsObenSchneidetNichtDieHoehe()
    {
        // Bild 1560×720 wie am Gerät: Statusleiste links oben, Tastenspalte rechts (ab y 120), Schieber unten rechts, Wendetasten unten links
        var occ = new List<FreeField.Box>
        {
            new(14, 12, 300, 200), new(1300, 120, 250, 380), new(1000, 620, 540, 90), new(14, 600, 300, 110),
        };
        var f = FreeField.Largest(1560, 720, occ, 780, 360);
        Assert.True(f.H >= 500, $"Feld nur {f.H} hoch");      // vorher: gierig oben bei der Tastenspalte abgeschnitten (≈ 35 % der Höhe)
        Assert.True(f.W >= 900, $"Feld nur {f.W} breit");
        foreach (var r in occ) Assert.False(f.X < r.X1 && f.X1 > r.X && f.Y < r.Y1 && f.Y1 > r.Y, $"Feld schneidet {r}");
        Assert.InRange(780f, f.X, f.X1); Assert.InRange(360f, f.Y, f.Y1);
        // Mittelpunkt unter einer Fläche: ganzes Bild
        Assert.Equal(new FreeField.Box(0, 0, 1560, 720), FreeField.Largest(1560, 720, occ, 20, 20));
    }

    [Fact]
    public void FreiesFeld_Randfaelle_MittellinieVolleBreiteKeinBand()
    {
        static bool Clear(FreeField.Box f, List<FreeField.Box> occ) => occ.TrueForAll(r => !(f.X < r.X1 && f.X1 > r.X && f.Y < r.Y1 && f.Y1 > r.Y));
        // Runde 17: Fläche auf der Mittellinie (links/rechts vom Mittelpunkt auf gleicher Höhe) — das Feld schneidet sie nicht und enthält den Mittelpunkt
        var mid = new List<FreeField.Box> { new(100, 340, 400, 40), new(1000, 340, 400, 40) };
        var f1 = FreeField.Largest(1560, 720, mid, 780, 360);
        Assert.True(Clear(f1, mid)); Assert.InRange(780f, f1.X, f1.X1); Assert.InRange(360f, f1.Y, f1.Y1);
        Assert.True(f1.W >= 500 && f1.H >= 300, $"{f1}");
        // HUD über die volle Breite oben und unten: das Feld ist das Band dazwischen, volle Breite
        var full = new List<FreeField.Box> { new(0, 0, 1560, 150), new(0, 600, 1560, 120) };
        var f2 = FreeField.Largest(1560, 720, full, 780, 360);
        Assert.Equal(new FreeField.Box(0, 150, 1560, 450), f2);
        // kein freies Band: der Mittelpunkt liegt unter einer Fläche → ganzes Bild (Rückfall, die Kamera rahmt dann ohne Feld)
        var none = new List<FreeField.Box> { new(0, 300, 1560, 120) };
        Assert.Equal(new FreeField.Box(0, 0, 1560, 720), FreeField.Largest(1560, 720, none, 780, 360));
        // Fläche genau auf dem Mittelpunkt-Rand (berührt nur): kein Ausfall, Feld frei
        var touch = new List<FreeField.Box> { new(780, 0, 200, 360) };
        var f4 = FreeField.Largest(1560, 720, touch, 780, 360);
        Assert.True(Clear(f4, touch) && f4.W * f4.H > 0, $"{f4}");
    }

    [Fact]
    public void NaechsterGegner_BleibtImGefechtBeimAngreifer()
    {
        var u = WorldBuilder.BuildDefault(1, "combat");
        var me = u.Ships[u.PlayerShip];
        var raids = u.PendingEncounters.Where(p => p.Kind == EncounterKind.Raid).OrderBy(p => p.DueTick).ToList();
        Assert.True(raids.Count >= 2);
        var lead = raids[0].Source; var far = raids[1].Source;
        var runner = new SimulationRunner();
        for (int i = 0; i < 4 * 40 && !u.Fights.IsEngaged(me.Id); i++) runner.Step(u);
        Assert.True(u.Fights.IsEngaged(me.Id));
        Assert.Equal(lead, PlayerCombat.NextEnemy(u, me, default));
        // einziger Gefechtsgegner: „Nächster Gegner“ bleibt beim Angreifer, springt nicht zum fernen Verstärkungskaperer
        Assert.Equal(lead, PlayerCombat.NextEnemy(u, me, lead));
        Assert.Contains(far, PlayerCombat.Enemies(u, me));
    }

    [Fact]
    public void MunitionLeer_HinweisNenntWasNochFeuert()
    {
        var u = TestWorlds.Default(fuel: false, research: false, startOption: "war");
        var me = u.Ships[u.PlayerShip];
        Assert.Equal("", PlayerGunnery.DepletedText(u, me));
        me.Systems.MissilesMissing = me.Stats.MissileCapacity;
        Assert.Equal("Rak. leer · nur Laser/Massetr.", PlayerGunnery.DepletedText(u, me));
        me.Systems.AmmoMissing = me.Stats.AmmoCapacity;
        Assert.Equal("Rak. leer · Mun. leer · nur Laser", PlayerGunnery.DepletedText(u, me));
        u.Rules.AmmoActive = false;
        Assert.Equal("", PlayerGunnery.DepletedText(u, me));
    }

    [Fact]
    public void LaufendeKosten_VerbandsunterhaltErklaertDieSinkendeKasse()
    {
        var u = TestWorlds.Default(fuel: false, research: false, startOption: "war");
        var me = u.Ships[u.PlayerShip];
        Assert.Equal(2, Hangar.UpkeepShips(u, u.PlayerActor));   // die zwei Bollwerke des Verbands
        float perMin = Hangar.RunningCostsPerMinute(u, u.PlayerActor);
        Assert.Equal(2 * u.Rules.ShipUpkeepPerTick * SimClock.TicksPerSecond * 60f, perMin, 3);
        Assert.Equal("Unterhalt 2 Schiffe −4,8 Cr/min", Hangar.RunningCostsText(u, u.PlayerActor));
        float w0 = u.WalletOf(me).Credits;
        var runner = new SimulationRunner();
        for (int i = 0; i < 4 * 60 * 5; i++) runner.Step(u);   // fünf Minuten; HangarSystem bucht alle 50 Ticks
        Assert.InRange(w0 - u.WalletOf(me).Credits, 5 * perMin - 1.01f, 5 * perMin + 1.01f);
    }
}
