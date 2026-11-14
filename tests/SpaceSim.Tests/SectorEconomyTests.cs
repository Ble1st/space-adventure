using SpaceSim.Core.Demo;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

public class SectorEconomyTests
{
    [Fact]
    public void PriceLevel_WithoutMarketData_IsOne()
    {
        var u = DemoUniverse.Create();
        var sector = u.PlayerSector;
        foreach (var sid in u.Sectors[sector].Stations) u.Stations[sid].Market.Clear();

        Assert.Equal(1f, SectorEconomy.PriceLevel(u, sector));
        Assert.Equal(1f, SectorEconomy.PriceLevel(u, new SectorId(999)));   // unbekannter Sektor
    }

    [Fact]
    public void PriceLevel_AveragesPriceOverBasePrice_AndSkipsZeroPrice()
    {
        var u = DemoUniverse.Create();
        var sector = u.PlayerSector;
        foreach (var sid in u.Sectors[sector].Stations)
        {
            var st = u.Stations[sid];
            foreach (var gid in System.Linq.Enumerable.ToList(st.Market.Keys))
            {
                var basePrice = u.Goods[gid].BasePrice;
                st.Market[gid].Price = 2f * basePrice;
            }
            // eine Position mit Preis 0 zählt nicht mit
            var someGood = u.Goods.Keys.First();
            st.Market[someGood] = new StockEntry { Amount = 5f, Target = 10f, Price = 0f };
        }

        Assert.Equal(2f, SectorEconomy.PriceLevel(u, sector), 3);
    }

    [Fact]
    public void PriceLevel_OfDemoWorld_IsPositiveAndNearOne()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 200);   // Marktsystem setzt die Preise

        foreach (var (id, _) in u.Sectors)
        {
            float level = SectorEconomy.PriceLevel(u, id);
            Assert.True(level > 0f, $"Sektor {id}: Preisniveau muss positiv sein");
            Assert.InRange(level, 0.2f, 5f);   // Preisbildung klemmt auf 0.25..4 × Basis
        }
    }

    [Fact]
    public void TotalValue_IsCreditsPlusCargoAtBasePrice()
    {
        // NPC-Schiff: das Schiff ist seine eigene Kasse → Credits + Fracht
        var u = DemoUniverse.Create();
        var ship = u.Ships.Values.First(s => !s.IsPlayer);
        ship.Credits = 100f;
        ship.Cargo.Clear();
        ship.Cargo[DemoUniverse.IronOre] = 5f;
        ship.Cargo[DemoUniverse.Platinum] = 2f;

        float expectedCargo = 5f * u.Goods[DemoUniverse.IronOre].BasePrice + 2f * u.Goods[DemoUniverse.Platinum].BasePrice;
        Assert.Equal(expectedCargo, SectorEconomy.CargoValueBasePrice(u, ship), 3);
        Assert.Equal(100f + expectedCargo, SectorEconomy.TotalValue(u, ship), 3);

        ship.Cargo.Clear();
        Assert.Equal(100f, SectorEconomy.TotalValue(u, ship), 3);   // leere Fracht: nur Credits
    }

    [Fact]
    public void TotalValue_Spielerschiff_NurFracht_VermoegenMitSpielerkasse()
    {
        // Phase 5b: das Geld des Spielers liegt in der Spielerkasse, das Schiff ist nur Besitz (Fracht)
        var u = DemoUniverse.Create();
        var ship = u.Ships[u.PlayerShip];
        u.PlayerWallet.Credits = 100f;
        ship.Cargo.Clear();
        ship.Cargo[DemoUniverse.IronOre] = 5f;
        float cargo = 5f * u.Goods[DemoUniverse.IronOre].BasePrice;
        Assert.Equal(cargo, SectorEconomy.TotalValue(u, ship), 3);
        // Runde 22: das Vermögen enthält auch den Verkaufswert des Schiffs (wie der Hangar-Verkauf)
        Assert.Equal(100f + cargo + SpaceSim.Core.Simulation.Hangar.SellPrice(u, ship), SectorEconomy.PlayerNetWorth(u), 1);
    }
}
