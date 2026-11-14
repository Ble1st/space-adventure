using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 22: Besitz zeigt den Schiffswert wie der Verkauf (Fregatte war „≈ 0 Cr“); das Vermögen rechnet mit derselben Funktion.</summary>
public class Runde22Tests
{
    [Fact]
    public void Besitz_SchiffswertWieVerkauf_PlusFracht_VermoegenGleicheFunktion()
    {
        var u = TestWorlds.Default(null, fuel: false, research: false, startOption: "war");
        var me = u.Ships[u.PlayerShip];
        Assert.Equal("bastion", me.ModelKey);
        float sale = SectorEconomy.ShipSaleValue(u, me);
        Assert.True(sale > 1000f, $"Fregatte hat einen Verkaufswert ({sale:0})");
        Assert.Equal(Hangar.SellPrice(u, me), sale, 2);
        Assert.Equal(sale + SectorEconomy.CargoValueBasePrice(u, me), SectorEconomy.PlayerShipValue(u, me), 2);
        float sum = u.PlayerWallet.Credits + u.ShipsById().Where(s => s.IsPlayer).Sum(s => SectorEconomy.PlayerShipValue(u, s));
        Assert.Equal(sum, SectorEconomy.PlayerNetWorth(u), 1);
        Assert.True(SectorEconomy.PlayerNetWorth(u) > u.PlayerWallet.Credits + 3f * 1000f, "drei Bollwerke zählen ins Vermögen");
    }
}
