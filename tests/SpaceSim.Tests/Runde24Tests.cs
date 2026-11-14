using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 24 (Befunde des Flottentests): Preisgrenze und Verlustkauf-Sperre der Spieler-Automatik beim Kauf (kurz, deterministisch), Befehlsstufe ohne die
/// eigenen Schiffe, Werften am Start lieferfähig.
/// </summary>
public class Runde24Tests
{
    static List<SimEvent> Capture(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    static Ship Own(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        s.HomeBase = at.Id;
        u.AddShip(s);
        return s;
    }

    /// <summary>Spieler-Händler mit Automatikauftrag kommt bei einem Hersteller an (Kauf-Phase); Preise fest, nur das Händlersystem läuft.</summary>
    static (Universe u, Ship t, List<SimEvent> ev) Arrive(AutoLimits limits, float sellerFactor, float buyerFactor)
    {
        var u = WorldBuilder.BuildDefault();
        new SimulationRunner().Step(u);
        var energy = u.FindGood("energy")!.Id;
        var seller = u.StationsById().First(st => !st.IsBuildSite && st.Produces(energy) && st.Market.ContainsKey(energy));
        var buyer = u.StationsById().First(st => !st.IsBuildSite && st.Consumes(energy) && st.Market.ContainsKey(energy) );
        var t = Own(u, "mule", buyer);   // startet beim Abnehmer und fliegt zum Hersteller (Ankunft = Kauf)
        u.PlayerWallet.Credits = 50_000f;
        t.Command = new TradeCommand(limits); t.Role = ShipRole.Trader;
        t.Order = new TradeOrder(seller.Id, buyer.Id, energy);
        t.Phase = TradePhase.GoingToBuy;
        buyer.Credits = 50_000f; buyer.Market[energy].Amount = 0f;
        seller.Market[energy].Amount = 100f;
        float basePrice = u.Goods[energy].BasePrice;
        seller.Market[energy].Price = basePrice * sellerFactor;
        buyer.Market[energy].Price = basePrice * buyerFactor;
        t.DockedAt = default;
        t.Position = seller.DockPoint(t.Id) + new Vec2(20f, 0f);
        Assert.True(Travel.Start(u, t, seller.Sector, seller.DockPoint(t.Id)));
        var ev = Capture(u);
        var sys = new TraderSystem();
        for (int i = 0; i < 400 && t.Transit is not null; i++) { u.Tick++; sys.Step(u, 1); }
        u.Tick++; sys.Step(u, 1);
        return (u, t, ev);
    }

    [Fact]
    public void Automatik_Preisgrenze_GiltBeimKauf_MitEreignis()
    {
        // Grenze 1,3× Basis, Preis bei der Ankunft 1,6× (Abnehmer zahlt 2×: kein Verlust) → kein Kauf, Verweigerung „price_limit“
        var (_, t, ev) = Arrive(new AutoLimits(default, -1, 1.3f, 0f), 1.6f, 2f);
        Assert.DoesNotContain(ev, e => e is TraderBought b && b.Ship == t.Id);
        Assert.Contains(ev, e => e is TraderBuyRefused r && r.Ship == t.Id && r.Reason == "price_limit");
        // unter der Grenze: Kauf
        var (_, t2, ev2) = Arrive(new AutoLimits(default, -1, 1.3f, 0f), 1.2f, 2f);
        Assert.Contains(ev2, e => e is TraderBought b && b.Ship == t2.Id);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Automatik_Verlustsperre_StandardAn_AbschaltbarJeAuftrag(bool allowLoss, bool buys)
    {
        // Einkauf 1,5×, Abnehmer zahlt 1,0× → sicherer Verlust
        var (_, t, ev) = Arrive(new AutoLimits(default, -1, 0f, 0f, allowLoss), 1.5f, 1.0f);
        Assert.Equal(buys, ev.Any(e => e is TraderBought b && b.Ship == t.Id));
        if (!buys) Assert.Contains(ev, e => e is TraderBuyRefused r && r.Ship == t.Id && r.Reason == "loss");
    }

    [Fact]
    public void Automatik_VerlustErlaubt_WirdGespeichertUndGehasht()
    {
        var u = WorldBuilder.BuildDefault();
        var beta = u.StationsById().First(s => s.Name == "Handelsstation Beta");
        var a = Own(u, "mule", beta); var b = Own(u, "mule", beta);
        Commands.Give(u, a, new TradeCommand(new AutoLimits(beta.Sector, 1, 1.4f, 500f, AllowLoss: true)), out _, ignoreCapacity: true);
        Commands.Give(u, b, new TradeCommand(new AutoLimits(beta.Sector, 1, 1.4f, 500f)), out _, ignoreCapacity: true);
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(((TradeCommand)loaded.Ships[a.Id].Command!).Limits.AllowLoss);
        Assert.False(((TradeCommand)loaded.Ships[b.Id].Command!).Limits.AllowLoss);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        var h0 = UniverseHash.Compute(u);
        a.Command = new TradeCommand(new AutoLimits(beta.Sector, 1, 1.4f, 500f));
        Assert.NotEqual(h0, UniverseHash.Compute(u));
        Assert.Contains("Verlustkäufe erlaubt", AutoLimitRules.Describe(u, new AutoLimits(beta.Sector, 1, 0f, 0f, true), true));
    }

    [Fact]
    public void Befehlsstufe_EigeneSchiffeZaehlenWenig_GrenzeGreift()
    {
        var u = WorldBuilder.BuildDefault(null, "trader");
        u.PlayerWallet.Credits = 1000f;
        var beta = u.StationsById().First(s => s.Name == "Handelsstation Beta");
        Assert.Equal((4, 1), Commands.Capacity(u, u.PlayerActor));
        var fleet = Enumerable.Range(0, 5).Select(_ => Own(u, "nomad", beta)).ToList();   // 6 Schiffe: Befehlsstufe bleibt Einzel
        Assert.Equal(ActorSizeClass.Single, Commands.CapacityClass(u, u.PlayerActor));
        Assert.True(ActorRules.ClassOf(u, ActorRules.Index(u), u.PlayerActor) > ActorSizeClass.Single, "die Größenklasse zählt die Schiffe weiter voll");
        int given = 0; string msg = "";
        foreach (var s in fleet) if (Commands.Give(u, s, new HoldCommand(), out var m)) given++; else { msg = m; break; }
        Assert.Equal(4, given);
        Assert.Contains("4 Schiffe mit Einzelbefehl", msg);
        Assert.Contains("Verband", msg);
        // Verband: einer erlaubt (Einzahl in der Meldung beim zweiten)
        var sq = Squadrons.Create(u, fleet[4], "Eins", out _);
        Assert.NotNull(sq);
        Assert.Null(Squadrons.Create(u, Own(u, "nomad", beta), "Zwei", out var m2));
        Assert.Contains("(1 Verband)", m2);
        // Geld hebt die Stufe (100 000 Cr = Kleinfirma), eine Station ebenso
        u.PlayerWallet.Credits = 100_000f;
        Assert.Equal(ActorSizeClass.SmallFirm, Commands.CapacityClass(u, u.PlayerActor));
        // Kriegstest bleibt spielbar: drei Fregatten in einem Verband
        var war = WorldBuilder.BuildDefault(null, "war");
        Assert.Single(Squadrons.Of(war, war.PlayerActor));
    }

    [Fact]
    public void Werften_AmStart_Lieferfaehig_JeWerftHandelSchuerfenKampf_JedesModellIrgendwo()
    {
        var u = WorldBuilder.BuildDefault(null, "trader");
        new SimulationRunner().Run(u, 200);
        u.PlayerWallet.Credits = 10_000_000f;
        var me = u.Ships[u.PlayerShip];
        var yards = u.StationsById().Where(s => s.TypeKey == "shipyard").ToList();
        Assert.Equal(3, yards.Count);
        bool Ok(Station y, ShipModel m) => ShipDealerService.Quote(u, y, me, m.Key) is { Missing.Count: 0, Locked: null };
        bool Combat(ShipModel m) => m.Jobs.Count == 0 && ShipStats.Compute(u.ShipClasses[m.ClassKey], m.Fit.Select(k => k is null ? null : u.Modules[k]).ToList()).Capabilities.HasFlag(Capability.Combat);
        foreach (var y in yards)
        {
            Assert.Contains(u.ShipModels.Values, m => m.Jobs.Contains("trade") && Ok(y, m));
            Assert.Contains(u.ShipModels.Values, m => m.Jobs.Contains("mine") && Ok(y, m));
            Assert.Contains(u.ShipModels.Values, m => Combat(m) && Ok(y, m));
        }
        foreach (var m in u.ShipModels.Values.Where(m => m.Key != "battlecruiser" && m.Key != "unit_player"))
            Assert.True(yards.Any(y => Ok(y, m)), $"{m.Name} an keiner Werft lieferbar");
    }
}
