using System.Linq;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>HUD-Neu: Gruppierung der Waffenschächte nach Waffentyp (Daumenbogen).</summary>
public class HudNeuTests
{
    static Ship ShipOf(Universe u, string model)
    {
        var m = u.ShipModels[model];
        var st = u.StationsById().First();
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, st, 0f);
        u.AddShip(s);
        return s;
    }

    [Fact]
    public void Leviathan_ZehnWaffen_DreiGruppen_JederSchachtInGenauEinerGruppe()
    {
        var u = DemoUniverse.Create();
        var s = ShipOf(u, u.ShipModels.Values.First(m => m.ClassKey == "battlecruiser").Key);
        var bays = WeaponBays.For(u, s);
        var groups = BayGroups.Of(u, s, bays);
        int weapons = bays.Count(b => SpaceSim.Core.Combat.PlayerGunnery.SlotOf(b.Key) >= 0);
        Assert.True(weapons >= 9, $"Leviathan hat {weapons} Waffen");
        // Gruppen nach Waffentyp in fester Reihenfolge (Laser, Geschütz, Rakete, Kanone); der Leviathan der Standardwelt hat Laser und Raketen
        var keys = groups.Where(g => g.Combat).Select(g => g.Key).ToArray();
        Assert.True(keys.Length is >= 2 and <= 4 && keys.SequenceEqual(new[] { "laser", "gun", "missile", "cannon" }.Where(keys.Contains)), string.Join(",", keys));
        var all = groups.SelectMany(g => g.Members).OrderBy(i => i).ToList();
        Assert.Equal(Enumerable.Range(0, bays.Count).ToList(), all);
    }

    [Fact]
    public void Bergbauschiff_JederSchachtEigeneGruppe_Abbaulaser()
    {
        var u = DemoUniverse.Create();
        var tinker = ShipOf(u, "tinker");
        var groups = BayGroups.Of(u, tinker, WeaponBays.For(u, tinker));
        Assert.Contains(groups, g => g.Key == "laser" && g.Combat);          // Laserkanone
        Assert.Contains(groups, g => g.Label == "Abbaulaser" && !g.Combat);  // Bergbaulaser
        Assert.Contains(groups, g => g.Label == "Traktor" && !g.Combat);
    }
}
