using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

/// <summary>
/// Schwarzmarkt (docs/SCHWARZMARKT.md): Dienst „blackmarket“ im Piratenversteck — Inhalt, Preise, Kassen, gesuchter Spieler, strenge Schlüssel,
/// Angebotsmenge = Ausführung, kein Rückkaufkreislauf (Kauf→Rückverkauf und Verkauf→Rückkauf ≤ 0 zu jedem Zeitpunkt), Beute im Hehlerlager ohne
/// Einfluss auf das NPC-Verhalten, Speichern/Laden.
/// </summary>
public class SchwarzmarktTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Schaltet den Schwarzmarkt einer Weltdefinition ab (Referenz-Hash-Tests: die Hashes stammen von davor; Beute im Hehlerlager ist gespeicherter Zustand).</summary>
    internal static void RevertSchwarzmarkt(WorldDef def) { def.Rules ??= new EconomyRules(); def.Rules.BlackMarket = false; def.Rules.BlackMarketNpc = false; }

    static readonly Lazy<string> Base = new(() => { var w = DemoUniverse.Create(); new SimulationRunner().Run(w, 1); return SaveGameJson.Write(w, Now); });

    static (Universe u, Ship me, Station hideout, BlackMarketService svc) Setup(int ticks = 1)
    {
        Universe u;
        if (ticks == 1) u = SaveGameJson.Read(Base.Value).Universe;   // eine Welt nach einem Tick, je Fall eine Kopie (schneller als neu bauen)
        else { u = DemoUniverse.Create(); new SimulationRunner().Run(u, ticks); }
        var me = u.Ships[u.PlayerShip];
        me.Cargo.Clear();
        u.WalletOf(me).Credits = 100_000f;
        var hideout = u.StationsById().First(s => BlackMarket.IsAt(u, s));
        me.DockedAt = hideout.Id;
        BlackMarket.Fence(u, hideout)!.Treasury.Credits = 20_000f;
        return (u, me, hideout, new BlackMarketService());
    }

    static GoodId Food(Universe u) => u.FindGood("food")!.Id;

    [Fact]
    public void Inhalt_VersteckeHabenSchwarzmarkt_AndockbarUndImRegister()
    {
        var u = DemoUniverse.Create();
        Assert.True(u.Rules.BlackMarket);
        Assert.Contains(BlackMarket.ServiceKey, StationServices.Default().Keys);
        var hideouts = u.StationsById().Where(s => u.StationTypes[s.TypeKey].Hideout).ToList();
        Assert.True(hideouts.Count >= 2);
        foreach (var h in hideouts)
        {
            Assert.True(h.Dockable, $"{h.Name} andockbar");
            Assert.True(BlackMarket.IsAt(u, h));
            Assert.Equal(ActorKind.Outlaw, u.Actors[h.Owner].Kind);
            var me = u.Ships[u.PlayerShip];
            Assert.Null(Docking.Issue(u, h, me));
            Assert.Contains(StationServices.Default().ForStation(u, h, me), s => s.Key == BlackMarket.ServiceKey);
            Assert.StartsWith("Schwarzmarkt: kauft jede Ware", BlackMarket.InfoLine(u, h));
        }
        // keine andere Station hat einen Schwarzmarkt
        Assert.All(u.StationsById().Where(s => !u.StationTypes[s.TypeKey].Hideout), s => Assert.False(BlackMarket.IsAt(u, s)));
        // Regel aus = nirgends; die alte Testwelt (Code-Standard) hat keinen
        u.Rules.BlackMarket = false;
        Assert.All(hideouts, h => Assert.False(BlackMarket.IsAt(u, h)));
        var classic = TestWorlds.Classic();
        Assert.False(classic.Rules.BlackMarket);
    }

    [Fact]
    public void Verkauf_AnDenHehler_ZahltAusDerPiratenkasse_Erhaltung()
    {
        var (u, me, h, svc) = Setup();
        var food = Food(u);
        me.Cargo[food] = 10f;
        var fence = BlackMarket.Fence(u, h)!;
        float wallet = u.WalletOf(me).Credits, purse = fence.Treasury.Credits;
        float price = BlackMarket.BuyPrice(u, food);
        float basePrice = u.GoodOf(food).BasePrice;
        Assert.True(price > 0f && price <= u.Rules.BlackMarketBuyShare * basePrice + 0.01f);
        Assert.True(price <= u.Rules.BlackMarketBuyShare * BlackMarket.HonestBid(u, food) + 0.01f);
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        var r = svc.Execute(u, h, me, new ServiceRequest($"sell:{food.Value}", default, 0f));
        Assert.True(r.Success, r.Message);
        Assert.Equal(10f, r.Units);
        Assert.False(me.Cargo.ContainsKey(food));
        Assert.Equal(10f, h.BlackStock[food]);
        Assert.Equal(wallet + 10f * price, u.WalletOf(me).Credits, 2);
        Assert.Equal(purse - 10f * price, fence.Treasury.Credits, 2);
        Assert.Equal(wallet + purse, u.WalletOf(me).Credits + fence.Treasury.Credits, 2);
        Assert.Contains(events, e => e is ServicePerformed { Service: "blackmarket", Offer: "sell" });
        Assert.Contains(events, e => e is TreasuryUpkeep { Purpose: "blackmarket" } t && MathF.Abs(t.Amount - 10f * price) < 0.01f);
    }

    [Fact]
    public void Kauf_VonHehlerware_GehtInDiePiratenkasse_ZumFestpreis()
    {
        var (u, me, h, svc) = Setup();
        var food = Food(u);
        h.BlackStock[food] = 8f;
        var fence = BlackMarket.Fence(u, h)!;
        float wallet = u.WalletOf(me).Credits, purse = fence.Treasury.Credits;
        float price = BlackMarket.SellPrice(u, food);
        Assert.Equal(u.Rules.BlackMarketSellFactor * u.GoodOf(food).BasePrice, price, 2);
        var r = svc.Execute(u, h, me, new ServiceRequest($"buy:{food.Value}", default, 5f));
        Assert.True(r.Success, r.Message);
        Assert.Equal(5f, me.Cargo[food]);
        Assert.Equal(3f, h.BlackStock[food]);
        Assert.Equal(wallet - 5f * price, u.WalletOf(me).Credits, 2);
        Assert.Equal(purse + 5f * price, fence.Treasury.Credits, 2);
    }

    [Fact]
    public void Gesuchter_Spieler_HandeltNichtEhrlich_AberBeimHehler()
    {
        var (u, me, h, svc) = Setup();
        var food = Food(u);
        me.Cargo[food] = 10f;
        foreach (var f in u.NpcFactionIds()) Wanted.Report(u, u.PlayerActor, f, "assault");
        // ehrliche Station mit Nahrungsankauf verweigert (Phase 25: Handelsverweigerung ab Stufe 3)
        var honest = u.StationsById().First(s => !s.IsBuildSite && s.Market.ContainsKey(food) && u.FactionOf(s) is not null && s.Consumes(food));
        Assert.NotNull(TradeActions.Refusal(u, me, honest));
        Assert.Equal(0f, TradeActions.Sellable(u, me, honest).Units);
        // der Hehler fragt nicht
        var offer = svc.Offers(u, h, me).Single(o => o.Key == $"sell:{food.Value}");
        Assert.Equal(10f, offer.Amount);
        Assert.True(svc.Execute(u, h, me, new ServiceRequest(offer.Key, default, 0f)).Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sell")]
    [InlineData("sell:")]
    [InlineData("sell:-3")]
    [InlineData("sell:3x")]
    [InlineData("buy: 3")]
    [InlineData("steal:3")]
    [InlineData("sell:9999")]
    public void StrengeSchluessel_ScheiternOhneZustandsaenderung(string key)
    {
        var (u, me, h, svc) = Setup();
        me.Cargo[Food(u)] = 4f;
        h.BlackStock[Food(u)] = 4f;
        ulong before = UniverseHash.Compute(u);
        int events = 0;
        u.Events.Raised += _ => events++;
        var r = svc.Execute(u, h, me, new ServiceRequest(key, default, 0f));
        Assert.False(r.Success);
        Assert.Equal(0, events);
        Assert.Equal(before, UniverseHash.Compute(u));
    }

    [Fact]
    public void NurSpielerschiffe_NpcBekommtNichts()
    {
        var (u, _, h, svc) = Setup();
        var npc = u.Ships.Values.First(s => s.IsEconomyAi);
        npc.Cargo[Food(u)] = 5f;
        Assert.Empty(svc.Offers(u, h, npc));
        Assert.False(svc.Execute(u, h, npc, new ServiceRequest($"sell:{Food(u).Value}", default, 0f)).Success);
    }

    /// <summary>Angebotsmenge &gt; 0 ⇔ Execute gelingt und bewegt genau diese Menge — über Fracht, Hehlerkasse, Spielerkasse, Lager und Laderaum.</summary>
    [Fact]
    public void Angebotsmenge_GleichAusfuehrung()
    {
        var holds = new[] { 0.4f, 1f, 7f, 60f };
        var purses = new[] { 0f, 1999f, 2010f, 2500f, 50_000f };
        var wallets = new[] { -50f, 0f, 30f, 100_000f };
        var stocks = new[] { 0.3f, 2f, 40f };
        int checkedCases = 0;
        foreach (var hold in holds)
        foreach (var purse in purses)
        foreach (var wallet in wallets)
        foreach (var stock in stocks)
        {
            var (u, me, h, svc) = Setup();
            var food = Food(u);
            var water = u.FindGood("water")!.Id;
            me.Cargo[food] = hold;
            h.BlackStock[water] = stock;
            BlackMarket.Fence(u, h)!.Treasury.Credits = purse;
            u.WalletOf(me).Credits = wallet;
            foreach (var offer in svc.Offers(u, h, me).ToList())
            {
                var probe = DemoUniverseCopy(u);
                var pMe = probe.Ships[probe.PlayerShip];
                var pH = probe.Stations[h.Id];
                var r = svc.Execute(probe, pH, pMe, new ServiceRequest(offer.Key, default, 0f));
                Assert.Equal(offer.Amount > 0f, r.Success);
                if (r.Success) Assert.Equal(offer.Amount, r.Units, 3);
                else Assert.Equal(offer.Note, r.Message);
                checkedCases++;
            }
        }
        Assert.True(checkedCases >= 400, $"{checkedCases} Fälle");
    }

    static Universe DemoUniverseCopy(Universe u) => SaveGameJson.Read(SaveGameJson.Write(u, Now)).Universe;

    /// <summary>
    /// Kein Geld aus dem Nichts: Kauf → Ticks → Rückverkauf und Verkauf → Ticks → Rückkauf geben am Schwarzmarkt für jede Ware ≤ 0, auch wenn
    /// zwischen den Buchungen die ehrlichen Preise schwanken (früh und spät, 0, 1, 50 und 600 Ticks Abstand). Strukturell: Ankauf ≤ Anteil × Basispreis &lt;
    /// Verkauf = Faktor × Basispreis.
    /// </summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 600)]
    [InlineData(3000, 1)]
    [InlineData(3000, 50)]
    public void KeinKreislauf_KaufRueckverkaufUndVerkaufRueckkauf_NieGewinn(int lead, int gap)
    {
        var (u, me, h, svc) = Setup(lead);
        var goods = u.Goods.Values.Where(g => g.Id.Value <= u.GoodIdBound).Select(g => g.Id).OrderBy(g => g.Value).ToList();
        foreach (var g in goods)
        {
            Assert.True(BlackMarket.BuyPrice(u, g) * 3f <= BlackMarket.SellPrice(u, g) + 0.02f, $"{u.GoodOf(g).Key}: Ankauf ≤ Verkauf / 3");
            // Kauf → Rückverkauf
            var a = DemoUniverseCopy(u);
            var aMe = a.Ships[a.PlayerShip]; var aH = a.Stations[h.Id];
            aMe.Cargo.Clear(); aH.BlackStock[g] = 5f;
            float w0 = a.WalletOf(aMe).Credits;
            var b1 = svc.Execute(a, aH, aMe, new ServiceRequest($"buy:{g.Value}", default, 0f));
            if (b1.Success)
            {
                new SimulationRunner().Run(a, gap);
                aMe.Cargo.TryGetValue(g, out float held);
                var s1 = svc.Execute(a, aH, aMe, new ServiceRequest($"sell:{g.Value}", default, 0f));
                float gain = a.WalletOf(aMe).Credits - w0;
                Assert.True(gain <= 0.001f, $"{a.GoodOf(g).Key}: Kauf→{gap} Ticks→Rückverkauf {gain:0.00} Cr ({s1.Message})");
            }
            // Verkauf → Rückkauf
            var c = DemoUniverseCopy(u);
            var cMe = c.Ships[c.PlayerShip]; var cH = c.Stations[h.Id];
            cMe.Cargo.Clear(); cMe.Cargo[g] = 5f; cH.BlackStock.Remove(g);
            float w1 = c.WalletOf(cMe).Credits;
            var s2 = svc.Execute(c, cH, cMe, new ServiceRequest($"sell:{g.Value}", default, 0f));
            if (!s2.Success) continue;
            new SimulationRunner().Run(c, gap);
            var b2 = svc.Execute(c, cH, cMe, new ServiceRequest($"buy:{g.Value}", default, s2.Units));
            // nicht zurückgekaufte Einheiten zum Festpreis des Hehlers bewerten (so viel kostete es, die Ware wieder an Bord zu haben)
            float missing = s2.Units - (b2.Success ? b2.Units : 0f);
            float gain2 = c.WalletOf(cMe).Credits - w1 - missing * BlackMarket.SellPrice(c, g);
            Assert.True(gain2 <= 0.001f, $"{c.GoodOf(g).Key}: Verkauf→{gap} Ticks→Rückkauf {gain2:0.00} Cr ({b2.Message})");
        }
    }

    [Fact]
    public void Ruecklage_HehlerZahltNurDarueber()
    {
        var (u, me, h, svc) = Setup();
        var food = Food(u);
        me.Cargo[food] = 1000f;
        var fence = BlackMarket.Fence(u, h)!;
        fence.Treasury.Credits = u.Rules.BlackMarketReserve + 100f;
        var r = svc.Execute(u, h, me, new ServiceRequest($"sell:{food.Value}", default, 0f));
        Assert.True(r.Success);
        Assert.True(fence.Treasury.Credits >= u.Rules.BlackMarketReserve - 0.01f, $"Kasse {fence.Treasury.Credits:0}");
        var offer = svc.Offers(u, h, me).Single(o => o.Key == $"sell:{food.Value}");
        Assert.Equal(0f, offer.Amount);
        Assert.Equal("Kasse des Hehlers leer", offer.Note);
        // Lager gedeckelt
        Assert.True(h.BlackStock[food] <= u.Rules.BlackMarketStockMax + 0.001f);
    }

    /// <summary>
    /// Beute landet beim Hehler, ändert aber kein NPC-Verhalten: dieselbe Welt mit Schwarzmarkt an und aus läuft 6000 Ticks; ohne das Hehlerlager
    /// ist der Hash gleich, das Lager hat Beute und hält den Deckel.
    /// </summary>
    [Fact]
    public void Beute_ImHehlerlager_OhneEinflussAufNpc()
    {
        var on = DemoUniverse.Create();
        on.Rules.BlackMarketNpc = false; on.Rules.Smugglers = false;   // Ausbau Punkt 5: Schmuggler kaufen Hehlerware (eigene Tests); hier geht es nur um die Ablage der Beute; NPCs beim Hehler ändern das Verhalten absichtlich (SchwarzmarktNpcTests)
        var defOff = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        RevertSchwarzmarkt(defOff);
        var off = WorldBuilder.Build(defOff);
        new SimulationRunner().Run(on, 6000);
        new SimulationRunner().Run(off, 6000);
        var stocked = on.StationsById().Where(s => s.BlackStock.Count > 0).ToList();
        Assert.NotEmpty(stocked);
        Assert.All(stocked, s => Assert.True(BlackMarket.IsAt(on, s)));
        Assert.All(stocked, s => Assert.All(s.BlackStock.Values, v => Assert.True(v <= on.Rules.BlackMarketStockMax + 0.001f)));
        Assert.All(off.Stations.Values, s => Assert.Empty(s.BlackStock));
        foreach (var s in stocked) s.BlackStock.Clear();
        on.Rules.BlackMarket = false;   // die Regel steht im gespeicherten Regelsatz — für den Vergleich gleichziehen
        Assert.Equal(UniverseHash.Compute(off), UniverseHash.Compute(on));
    }

    [Fact]
    public void Stash_DirektUndMitRegelAus()
    {
        var u = DemoUniverse.Create();
        var h = u.StationsById().First(s => BlackMarket.IsAt(u, s));
        var food = Food(u);
        BlackMarket.Stash(u, h.Owner, food, 25f);
        BlackMarket.Stash(u, h.Owner, food, 25f);
        Assert.Equal(u.Rules.BlackMarketStockMax, h.BlackStock[food]);
        u.Rules.BlackMarket = false;
        h.BlackStock.Clear();
        BlackMarket.Stash(u, h.Owner, food, 10f);
        Assert.Empty(h.BlackStock);
        Assert.False(BlackMarket.IsAt(u, h));
        Assert.Empty(new BlackMarketService().Offers(u, h, u.Ships[u.PlayerShip]));
    }

    [Fact]
    public void SpeichernLaden_HehlerlagerBitgleich()
    {
        var (u, me, h, svc) = Setup();
        h.BlackStock[Food(u)] = 7.5f;
        me.Cargo[Food(u)] = 3f;
        svc.Execute(u, h, me, new ServiceRequest($"sell:{Food(u).Value}", default, 0f));
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(10.5f, loaded.Stations[h.Id].BlackStock[Food(u)]);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        new SimulationRunner().Run(u, 300);
        new SimulationRunner().Run(loaded, 300);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }
}
