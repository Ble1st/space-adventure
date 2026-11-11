using System.Text.Json.Nodes;
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
/// Phase 5b: Spielerkasse (<see cref="PlayerWallet"/>, <see cref="Universe.WalletOf(Ship)"/>). Geld hängt an der Person,
/// Handel und Dienste buchen für Spielerschiffe auf die Kasse, NPC-Schiffe bleiben ihre eigene Kasse.
/// </summary>
public class WalletTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Demotet einen aktuellen Save (v7) auf v6: Version 6, die Spielerkasse wandert zurück in die Credits des
    /// Spielerschiffs (so lag das Geld vor Phase 5b), der Abschnitt <c>PlayerWallet</c> entfällt.
    /// </summary>
    internal static string ToV6Json(string v7Json)
    {
        var node = JsonNode.Parse(ActorTests.ToV11Json(v7Json))!.AsObject();   // Phase 9: Akteure zurückbauen (Spielerkasse wieder PlayerWallet)
        node["Version"] = 6;
        ConstructionTests.RemovePhase8Rules(node);   // Phase 8: die Bau-Regeln gab es damals nicht (Inhalte wie Bauschiffe bleiben, die Migration ergänzt nur)
        if (node["PlayerWallet"] is JsonObject w)
        {
            int playerId = (int)node["PlayerShip"]!;
            foreach (var sh in node["Ships"]!.AsArray())
                if ((int)sh!["Id"]! == playerId) sh["Credits"] = (float)sh["Credits"]! + (float)w["Credits"]!;
            node.Remove("PlayerWallet");
        }
        return node.ToJsonString();
    }

    /// <summary>Gesamte Geldmenge nach Kassen: Spielerkasse + alle Schiffe + alle Stationen.</summary>
    static double Money(Universe u) =>
        (double)u.PlayerWallet.Credits + u.Ships.Values.Sum(s => (double)s.Credits) + u.Stations.Values.Sum(s => (double)s.Credits);

    static Universe World()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);   // Preise setzen
        return u;
    }

    [Fact]
    public void Startwelt_GeldLiegtInDerSpielerkasse_SchiffHatKeines()
    {
        var def = SpaceSim.Core.Content.WorldBuilder.Parse(SpaceSim.Core.Content.WorldBuilder.DefaultWorldJson());
        var u = DemoUniverse.Create();
        var player = u.Ships[u.PlayerShip];
        Assert.Equal(def.Player.Credits, u.PlayerWallet.Credits);
        Assert.Equal(new FactionId(def.Player.Faction), u.Player.Membership);   // Phase 9: Mitgliedschaft des Spielerakteurs
        Assert.Equal(0f, player.Credits);
        Assert.Same(u.PlayerWallet, u.WalletOf(player));
        // NPC-Schiffe sind ihre eigene Kasse
        foreach (var s in u.Ships.Values.Where(s => !s.IsPlayer))
            Assert.Same(s, u.WalletOf(s));
        Assert.Equal(u.PlayerWallet.Credits, EconomyStats.Snapshot(u).PlayerCredits);
    }

    [Fact]
    public void Handel_BuchtAufDieKasse_GeldmengeBleibtGleich()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var st = u.Stations.Values.OrderBy(s => s.Id.Value).First(s => s.Market.Values.Any(m => m.Amount >= 20f && m.Price > 0f));
        var good = st.Market.OrderBy(k => k.Key.Value).First(kv => kv.Value.Amount >= 20f && kv.Value.Price > 0f).Key;
        player.Cargo.Clear();
        u.PlayerWallet.Credits = 50_000f;
        double before = Money(u);
        float totalBefore = EconomyStats.Snapshot(u).TotalCredits;
        var svc = new TradeService();

        var buy = svc.Execute(u, st, player, new ServiceRequest($"buy:{good.Value}", good, 10f));
        Assert.True(buy.Success);
        Assert.Equal(50_000f + buy.Credits, u.PlayerWallet.Credits, 2);   // Credits < 0: Zahlung aus der Kasse
        Assert.Equal(0f, player.Credits);
        Assert.Equal(before, Money(u), 1);

        var sell = svc.Execute(u, st, player, new ServiceRequest($"sell:{good.Value}", good, 4f));
        Assert.True(sell.Success);
        Assert.Equal(0f, player.Credits);
        Assert.Equal(before, Money(u), 1);

        var all = svc.Execute(u, st, player, new ServiceRequest("sell_all", default, 0f));
        Assert.True(all.Success);
        Assert.Equal(0f, player.Credits);
        Assert.Equal(before, Money(u), 1);
        Assert.Equal(totalBefore, EconomyStats.Snapshot(u).TotalCredits, 0);
    }

    [Fact]
    public void Tanken_Reparatur_BuchenAufDieKasse_Notruf_IstDieselbeGeldsenke()
    {
        var u = TestWorlds.WithFuel();   // Phase 14: Treibstoff ist in der Standardwelt aus
        new SimulationRunner().Run(u, 1);
        var player = u.Ships[u.PlayerShip];
        u.PlayerWallet.Credits = 20_000f;

        // Tanken an der Raffinerie: Kasse → Station
        var refinery = u.Stations[new StationId(25)];
        if (player.Sector != refinery.Sector) u.MoveShip(player, refinery.Sector);
        player.Position = refinery.DockPoint(player.Id);
        player.Fuel[DemoUniverse.Propellant] = 50f;
        double before = Money(u);
        float wallet = u.PlayerWallet.Credits;
        var fuel = new RefuelService().Execute(u, refinery, player, new ServiceRequest("refuel:fill", default, 0f));
        Assert.True(fuel.Success);
        Assert.Equal(wallet + fuel.Credits, u.PlayerWallet.Credits, 2);
        Assert.Equal(0f, player.Credits);
        Assert.Equal(before, Money(u), 1);

        // Reparatur: Kasse → Station
        var plating = u.Stations.Values.First(s => s.TypeKey == "plating_works");
        plating.Market[DemoUniverse.HullPlates].Amount = 50f;
        player.Hull = player.MaxHull - 30f;
        wallet = u.PlayerWallet.Credits;
        var rep = new RepairService().Execute(u, plating, player, new ServiceRequest("repair:full", default, 0f));
        Assert.True(rep.Success);
        Assert.Equal(wallet + rep.Credits, u.PlayerWallet.Credits, 2);
        Assert.Equal(0f, player.Credits);
        Assert.Equal(before, Money(u), 1);

        // Notruf: Geldsenke wie vorher, nur aus der Kasse statt vom Schiff
        player.Fuel[DemoUniverse.Propellant] = 0f;
        player.Position = new Vec2(70f, 70f);
        wallet = u.PlayerWallet.Credits;
        var sos = Refuel.PlayerEmergency(u, player);
        Assert.True(sos.Success);
        Assert.Equal(wallet + sos.Credits, u.PlayerWallet.Credits, 2);
        Assert.Equal(before + sos.Credits, Money(u), 1);
        Assert.Equal(0f, player.Credits);
    }

    [Fact]
    public void Kasse_ReichtNicht_KaufScheitert_AuchWennDasSchiffCreditsHaette()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var st = u.Stations.Values.OrderBy(s => s.Id.Value).First(s => s.Market.Values.Any(m => m.Amount >= 20f && m.Price > 0f));
        var good = st.Market.OrderBy(k => k.Key.Value).First(kv => kv.Value.Amount >= 20f && kv.Value.Price > 0f).Key;
        u.PlayerWallet.Credits = 0f;
        player.Credits = 1_000_000f;   // Restgeld am Schiff zählt nicht: Spielerschiffe buchen nur über die Kasse
        Assert.Equal(0f, TradeActions.MaxBuyable(u, player, st, good));
        Assert.Equal(BuyOutcome.NoCredits, TradeActions.Buy(u, player, st, good, 1f).Outcome);
        Assert.Equal(1_000_000f, player.Credits);
    }

    [Fact]
    public void NpcWirtschaft_LiestDieSpielerkasseNie_20000Ticks()
    {
        // Gleiche Welt, sehr unterschiedliche Spielerkasse: nach 20000 Ticks ist bis auf die Kasse alles bitgleich
        var a = DemoUniverse.Create();
        var b = DemoUniverse.Create();
        b.PlayerWallet.Credits = 9_999_999f;
        var runner = new SimulationRunner();
        runner.Run(a, 20_000);
        runner.Run(b, 20_000);
        Assert.Equal(9_999_999f, b.PlayerWallet.Credits);   // kein System bucht auf die Spielerkasse
        b.PlayerWallet.Credits = a.PlayerWallet.Credits;
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        Assert.Equal(0f, a.Ships[a.PlayerShip].Credits);
    }

    [Fact]
    public void SaveLoad_V7_KasseRoundtrip_HashGleich()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 500);
        u.PlayerWallet.Credits = 12_345.5f;
        var json = SaveGameJson.Write(u, Now);
        Assert.Contains("\"Version\":17", json);   // Phase 14: v17   // Phase 5c: v8, Phase 8: v11, Phase 9: v12
        Assert.Contains("\"Actors\"", json);        // Phase 9: die Kasse ist die des Spielerakteurs
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.Equal(12_345.5f, loaded.PlayerWallet.Credits);
        Assert.Equal(u.Player.Membership, loaded.Player.Membership);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        // Fortsetzung bitgleich
        new SimulationRunner().Run(u, 500);
        new SimulationRunner().Run(loaded, 500);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Migration_V6ToV7_SchiffsCreditsWandernInDieKasse_NpcUnveraendert()
    {
        var u = DemoUniverse.Create();
        // Phase 9: Kassen der Fraktionen und aufgestiegene Akteure gibt es vor v12 nicht — abgeschaltet, damit das Demotieren verlustfrei bleibt
        u.Rules.TaxToTreasuryShare = 0f; u.Rules.TreasuryInvests = false; u.Rules.PromoteInvestors = false;
        // Phase 10: Regierungsstationen gibt es vor v13 nicht (die Migration setzt sie neu mit leerer Kasse) — ihr Unterhalt bleibt hier aus
        foreach (var t in u.StationTypes.Values.Where(t => t.Government != GovernmentKind.None)) t.UpkeepPerTick = 0f;
        // Phase 11: Polizei und Piraten gibt es vor v14 nicht (die Migration setzt sie neu) — hier abgeschaltet und entfernt
        SecurityTests.WithoutSecurity(u);
        new SimulationRunner().Run(u, 1_000);
        u.PlayerWallet.Credits = 777f;
        var v6 = ToV6Json(SaveGameJson.Write(u, Now));
        Assert.Contains("\"Version\":6", v6);
        Assert.DoesNotContain("PlayerWallet", v6);

        var (loaded, _) = SaveGameJson.Read(v6);
        Assert.Equal(777f, loaded.PlayerWallet.Credits);
        Assert.Equal(0f, loaded.Ships[loaded.PlayerShip].Credits);
        Assert.Equal(u.Player.Membership, loaded.Player.Membership);
        foreach (var s in u.Ships.Values.Where(s => !s.IsPlayer))
            Assert.Equal(s.Credits, loaded.Ships[s.Id].Credits);
        // Gesamter Zustand inkl. Kasse wie vor dem Demotieren
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        Assert.Equal(EconomyStats.Snapshot(u).TotalCredits, EconomyStats.Snapshot(loaded).TotalCredits);
    }

    [Fact]
    public void Migration_V5Kette_KasseKommtAusDemSpielerschiff()
    {
        var u = DemoUniverse.Create();
        u.PlayerWallet.Credits = 4_321f;
        var (loaded, _) = SaveGameJson.Read(FuelModelTests.ToV5Json(SaveGameJson.Write(u, Now)));
        Assert.Equal(4_321f, loaded.PlayerWallet.Credits);
        Assert.Equal(0f, loaded.Ships[loaded.PlayerShip].Credits);
    }

    [Fact]
    public void Migration_OhneSteuererfeld_ErkenntSpielerschiffAnDerRolle()
    {
        var u = DemoUniverse.Create();
        u.PlayerWallet.Credits = 250f;
        var node = JsonNode.Parse(ToV6Json(SaveGameJson.Write(u, Now)))!.AsObject();
        foreach (var sh in node["Ships"]!.AsArray()) sh!.AsObject().Remove("Controller");   // Stand vor Phase 2
        var (loaded, _) = SaveGameJson.Read(node.ToJsonString());
        Assert.Equal(250f, loaded.PlayerWallet.Credits);
        Assert.Equal(0f, loaded.Ships[loaded.PlayerShip].Credits);
    }
}
