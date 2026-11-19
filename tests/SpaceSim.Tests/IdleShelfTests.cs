using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 12 (docs/TODO-AUSBAU.md): ungenutzte Regale — stock-only Markteinträge (<see cref="Station.IsStockOnly"/>), besonders Modulwaren —
/// werden günstiger, solange niemand an ihnen handelt. Regeln in <see cref="EconomyRules.IdleShelves"/> (eigene partial-Datei): pro Regaleintrag
/// Tick der letzten Bewegung (Kauf oder Lieferung, <see cref="StockEntry.LastMove"/>); nach <see cref="EconomyRules.IdleShelfTicks"/> ohne Bewegung
/// Ziel −1 (mindestens 1, <see cref="IdleShelfSystem"/>) und Verkaufspreis × <see cref="EconomyRules.IdleShelfPriceFactor"/> (<see cref="MarketSystem"/>),
/// bis wieder eine Bewegung stattfindet. Code-Standard Schalter aus, Standardwelt an; additiv gespeichert, im Hash nur gemischt wenn gesetzt.
/// </summary>
public class IdleShelfTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Regaleintrag (stock-only) der ersten Werft, die das Mk1-Reaktor-Modul führt.</summary>
    static (Station St, StockEntry Entry, GoodId Good) Regal(Universe u, string goodKey = "reactor_mk1")
    {
        var good = u.FindGood(goodKey)!.Id;
        foreach (var st in u.Stations.Values)
            if (st.TypeKey == "shipyard" && st.Market.TryGetValue(good, out var e) && st.IsStockOnly(good))
                return (st, e, good);
        throw new Xunit.Sdk.XunitException($"kein Werftregal für {goodKey}");
    }

    /// <summary>
    /// Alles dieser Runde zurücknehmen (Referenz-Hash-Tests; Teil der Rücknahmekette über <see cref="BalanceFixTests.RevertBalanceFix"/>):
    /// Schalter aus, Ticks/Preisfaktor auf Code-Standard.
    /// </summary>
    internal static void RevertIdleShelf(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        var code = new EconomyRules();
        r.IdleShelfActive = false;
        r.IdleShelfTicks = code.IdleShelfTicks;
        r.IdleShelfPriceFactor = code.IdleShelfPriceFactor;
    }

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_RücknahmeAus()
    {
        var code = new EconomyRules();
        Assert.False(code.IdleShelfActive);
        Assert.Equal(20000, code.IdleShelfTicks);
        Assert.Equal(0.5f, code.IdleShelfPriceFactor);
        var u = DemoUniverse.Create();
        Assert.True(u.Rules.IdleShelfActive);   // Standardwelt schaltet ein
        Assert.Equal(20000, u.Rules.IdleShelfTicks);
        Assert.Equal(0.5f, u.Rules.IdleShelfPriceFactor);
        var def = TestWorlds.Def(fuel: false, research: false);
        RevertIdleShelf(def);
        var off = WorldBuilder.Build(def, 1);
        Assert.False(off.Rules.IdleShelfActive);
        Assert.Equal(20000, off.Rules.IdleShelfTicks);
        Assert.Equal(0.5f, off.Rules.IdleShelfPriceFactor);
    }

    [Fact]
    public void UnberührtesRegal_WirdNachAblaufBilliger_ZielSinkt_EinmalJePeriode_Mindestens1()
    {
        var u = DemoUniverse.Create();
        Assert.True(u.Rules.IdleShelfActive);
        var (st, e, good) = Regal(u);
        Assert.Null(e.LastMove);   // nie bewegt
        e.Amount = e.Target;       // voller Bestand: Preis = Basispreis
        float basePrice = u.Goods[good].BasePrice;
        float target0 = e.Target;

        u.Tick = 0;
        new MarketSystem().Step(u, 1);
        Assert.Equal(basePrice, e.Price, 2);   // noch nicht idle

        // Schwelle erreicht: Tick 20000, Regal seit Tick 0 unberührt
        u.Tick = 20000;
        new MarketSystem().Step(u, 1);
        Assert.Equal(basePrice * 0.5f, e.Price, 2);   // halber Preis
        Assert.Equal(target0, e.Target, 3);           // Ziel noch nicht gesenkt (erst das IdleShelfSystem)

        new IdleShelfSystem().Step(u, IdleShelfSystem.Interval);
        Assert.Equal(target0 - 1f, e.Target, 3);      // Ziel um 1 gesenkt
        // weiterer Lauf im selben unberührten Zeitraum senkt nicht erneut
        u.Tick = 20100;
        new IdleShelfSystem().Step(u, IdleShelfSystem.Interval);
        Assert.Equal(target0 - 1f, e.Target, 3);
        // der Preis bleibt vergünstigt, solange keine Bewegung stattfindet
        new MarketSystem().Step(u, 1);
        Assert.Equal(MarketSystem.StockPrice(u.Goods[good], e, e.Amount) * 0.5f, e.Price, 2);

        // Ziel mindestens 1
        e.Target = 1f;
        e.LastMove = null;
        u.Tick = 60000;
        new IdleShelfSystem().Step(u, IdleShelfSystem.Interval);   // 60000-0 >= 20000, 59900-0 < 20000: Übergang
        Assert.Equal(1f, e.Target, 3);
    }

    [Fact]
    public void KaufVomRegal_SetztZurück_PreisNormal_ZielBleibt_SenktErstNachErneutemAblauf()
    {
        var u = DemoUniverse.Create();
        var (st, e, good) = Regal(u);
        e.Amount = e.Target;
        float basePrice = u.Goods[good].BasePrice;
        float target0 = e.Target;

        u.Tick = 20000;   // idle
        new MarketSystem().Step(u, 1);
        Assert.Equal(basePrice * 0.5f, e.Price, 2);
        new IdleShelfSystem().Step(u, IdleShelfSystem.Interval);
        Assert.Equal(target0 - 1f, e.Target, 3);

        // Kauf vom Regal (Spieler): Bewegung um Tick 20000
        var ship = u.Ships[u.PlayerShip];
        u.WalletOf(ship).Credits = 1_000_000f;
        Assert.Equal(BuyOutcome.Ok, TradeActions.Buy(u, ship, st, good, 1f).Outcome);
        Assert.Equal(20000, e.LastMove);
        Assert.Equal(target0 - 1f, e.Amount, 3);

        // nächster Tick: wieder Normalpreis, Ziel bleibt gesenkt
        u.Tick = 20001;
        new MarketSystem().Step(u, 1);
        Assert.Equal(MarketSystem.StockPrice(u.Goods[good], e, e.Amount), e.Price, 2);
        Assert.True(e.Price > basePrice * 0.5f);
        new IdleShelfSystem().Step(u, IdleShelfSystem.Interval);
        Assert.Equal(target0 - 1f, e.Target, 3);   // 20001-20000 < 20000: nicht idle

        // erst nach erneut 20000 Ticks ohne Bewegung wieder vergünstigt und Ziel −1
        u.Tick = 40000;
        new MarketSystem().Step(u, 1);
        Assert.Equal(MarketSystem.StockPrice(u.Goods[good], e, e.Amount) * 0.5f, e.Price, 2);
        new IdleShelfSystem().Step(u, IdleShelfSystem.Interval);
        Assert.Equal(target0 - 2f, e.Target, 3);   // 40000-20000 >= 20000, 39900-20000 < 20000: Übergang
    }

    [Fact]
    public void LieferungAnsRegal_SetztZurück()
    {
        var u = DemoUniverse.Create();
        var (st, e, good) = Regal(u);
        st.Credits = 1_000_000f;
        var ship = u.Ships[u.PlayerShip];
        u.WalletOf(ship).Credits = 1_000_000f;
        ship.Cargo[good] = 2f;
        e.Amount = 0f;   // leer: das Regal nimmt bis zum Wunschbestand an
        u.Tick = 5000;
        new MarketSystem().Step(u, 1);
        var priceBefore = e.Price;
        Assert.Null(e.LastMove);
        var res = TradeActions.Sell(u, ship, st, good, 2f);
        Assert.Equal(2f, res.Sold[good]);
        Assert.Equal(5000, e.LastMove);   // Lieferung ans Regal = Bewegung
        Assert.Equal(priceBefore * 2f, res.Revenue, 1);   // zwei Einheiten zum Tickpreis
    }

    [Fact]
    public void SchalterAus_KeineÄnderung_LastMoveBleibtNull()
    {
        var u = DemoUniverse.Create();
        u.Rules.IdleShelfActive = false;
        var (st, e, good) = Regal(u);
        e.Amount = e.Target;
        float basePrice = u.Goods[good].BasePrice;
        float target0 = e.Target;

        u.Tick = 20000;
        new MarketSystem().Step(u, 1);
        Assert.Equal(basePrice, e.Price, 2);   // kein Preisfaktor
        new IdleShelfSystem().Step(u, IdleShelfSystem.Interval);
        Assert.Equal(target0, e.Target, 3);    // Ziel unverändert

        var ship = u.Ships[u.PlayerShip];
        u.WalletOf(ship).Credits = 1_000_000f;
        Assert.Equal(BuyOutcome.Ok, TradeActions.Buy(u, ship, st, good, 1f).Outcome);
        Assert.Null(e.LastMove);   // ohne Schalter wird nichts gemerkt
    }

    [Fact]
    public void SpeichernLaden_Bitgleich_mitLastMoveUndGesunkenemZiel()
    {
        var u = DemoUniverse.Create();
        var (st, e, good) = Regal(u);
        e.Amount = e.Target;
        u.Tick = 20000;
        new MarketSystem().Step(u, 1);   // setzt die Preise (sonst 0 vor dem ersten Markt-Tick)
        new IdleShelfSystem().Step(u, IdleShelfSystem.Interval);   // Ziel −1
        float targetAfter = e.Target;
        // eine Bewegung merken (wie ein Kauf/Lieferung um Tick 20000)
        var ship = u.Ships[u.PlayerShip];
        u.WalletOf(ship).Credits = 1_000_000f;
        Assert.Equal(BuyOutcome.Ok, TradeActions.Buy(u, ship, st, good, 1f).Outcome);
        Assert.Equal(20000, e.LastMove);

        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        var le = loaded.Stations[st.Id].Market[good];
        Assert.Equal(targetAfter, le.Target, 3);
        Assert.Equal(20000, le.LastMove);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));

        new SimulationRunner().Run(u, 1000);
        new SimulationRunner().Run(loaded, 1000);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Deterministisch_ZweiWelten_GleicherHash()
    {
        var a = DemoUniverse.Create(7);
        var b = DemoUniverse.Create(7);
        new SimulationRunner().Run(a, 300);
        new SimulationRunner().Run(b, 300);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }
}
