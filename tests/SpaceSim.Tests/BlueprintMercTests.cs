using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Lücken gegenüber Interstellar Pilot 2: eigene Schiffsvarianten (Baupläne: speichern, in der Werft kaufen, beim Ausrüster anwenden) und Söldner per Comms
/// (anheuern, Eskorte, Tagessold, Ablauf/leere Kasse, Heimkehr). Beides reine Spieleraktionen — ohne sie bleibt die NPC-Welt bitgleich.
/// </summary>
public class BlueprintMercTests
{
    static readonly ShipDealerService Dealer = new();
    static readonly CommsActions Comms = CommsActions.Default();

    /// <summary>Referenzhash-Tests: Söldner aus (Code-Vorgabe).</summary>
    internal static void RevertMerc(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.Mercenaries = false;
    }

    static ServiceResult Exec(IStationService svc, Universe u, Station st, Ship ship, string offer) =>
        svc.Execute(u, st, ship, new ServiceRequest(offer, default, 0f));

    // ───────── Schalter ─────────

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_RevertAus()
    {
        Assert.False(new EconomyRules().Mercenaries);
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        Assert.True(WorldBuilder.Build(def, 1).Rules.Mercenaries);
        RevertMerc(def);
        Assert.False(WorldBuilder.Build(def, 1).Rules.Mercenaries);
    }

    [Fact]
    public void OhneAnheuern_NpcWeltBitgleich_MitUndOhneSchalter()
    {
        var on = WorldBuilder.Build(WorldBuilder.Parse(WorldBuilder.DefaultWorldJson()), 7);
        var defOff = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        RevertMerc(defOff);
        var off = WorldBuilder.Build(defOff, 7);
        new SimulationRunner().Run(on, 3000);
        new SimulationRunner().Run(off, 3000);
        Assert.Empty(on.Mercenaries);
        Assert.Equal(UniverseHash.Compute(off), UniverseHash.Compute(on));
    }

    // ───────── Baupläne ─────────

    [Fact]
    public void Bauplan_Speichern_Regeln()
    {
        var (u, yard, player) = QualityUpgradeTests.AtYard();
        Assert.NotNull(Blueprints.SaveIssue(u, player, ""));
        Assert.NotNull(Blueprints.SaveIssue(u, player, new string('x', 40)));
        var npc = u.Ships.Values.First(s => !s.IsPlayer && s.HasModel);
        Assert.Equal("Nur eigene Schiffe.", Blueprints.SaveIssue(u, npc, "Fremd"));
        var bp = Blueprints.Save(u, player, "Erzkahn", out var msg);
        Assert.NotNull(bp);
        Assert.Contains("Erzkahn", msg);
        Assert.Equal(player.ModelKey, bp!.ModelKey);
        Assert.Equal(u.ShipClasses[u.ShipModels[player.ModelKey].ClassKey].Slots.Count, bp.Fit.Count);
        // gleicher Name ersetzt, kein zweiter Eintrag
        Blueprints.Save(u, player, "erzkahn", out _);
        Assert.Single(Blueprints.Of(u));
        Assert.True(Blueprints.Delete(u, bp.Id));
        Assert.False(Blueprints.Delete(u, bp.Id));
        Assert.Empty(Blueprints.Of(u));
    }

    [Fact]
    public void Bauplan_InDerWerftKaufen_GleicheKaufregel()
    {
        var (u, yard, player) = QualityUpgradeTests.AtYard();
        // eigene Variante: Frachtmodul gegen ein kleineres tauschen (gilt nach FitRules)
        int cargo = player.Slots.FindIndex(s => s.Type == "cargo");
        player.Fit[cargo] = "cargo_universal_60";
        player.RecomputeStats(u);
        var bp = Blueprints.Save(u, player, "Kleiner Kahn", out _)!;
        string key = $"{ShipDealerService.BlueprintKey}:{bp.Id}";
        var offer = Dealer.Offers(u, yard, player).Single(o => o.Key == key);
        Assert.Equal(1f, offer.Amount);
        var q = ShipDealerService.QuoteBlueprint(u, yard, player, bp.Id)!;
        float wallet = u.PlayerWallet.Credits, cash = yard.Credits;
        var stockBefore = q.Materials.ToDictionary(m => m.Good, m => yard.Market[m.Good].Amount);
        int ships = u.Ships.Count;
        var r = Exec(Dealer, u, yard, player, key);
        Assert.True(r.Success, r.Message);
        Assert.Equal(ships + 1, u.Ships.Count);
        var bought = u.Ships.Values.OrderBy(s => s.Id.Value).Last();
        Assert.True(bought.IsPlayer);
        Assert.Equal(bp.Fit, bought.Fit);
        Assert.Equal(yard.Id, bought.DockedAt);
        Assert.Equal(wallet - q.Price, u.PlayerWallet.Credits, 2);
        Assert.Equal(cash + q.Price, yard.Credits, 2);
        foreach (var m in q.Materials) Assert.Equal(stockBefore[m.Good] - m.Units, yard.Market[m.Good].Amount, 3);
        Assert.Equal(u.PlayerShip, player.Id);   // kein Einsteigen

        // ohne Material: Angebot 0 und Ausführung ändert nichts
        var kit = u.FindGood(ShipPurchase.KitKeyOf(u, q.Class))!.Id;
        yard.Market[kit].Amount = 0f;
        Assert.Equal(0f, Dealer.Offers(u, yard, player).Single(o => o.Key == key).Amount);
        string before = UniverseHash.Compute(u).ToString() + u.PlayerWallet.Credits;
        var events = new List<SimEvent>();
        u.Events.Raised += e => events.Add(e);
        Assert.False(Exec(Dealer, u, yard, player, key).Success);
        Assert.False(Exec(Dealer, u, yard, player, "blueprint:x").Success);
        Assert.False(Exec(Dealer, u, yard, player, "blueprint:999").Success);
        Assert.Empty(events);
        Assert.Equal(before, UniverseHash.Compute(u).ToString() + u.PlayerWallet.Credits);
    }

    [Fact]
    public void Bauplan_AngebotGleichAusfuehrung()
    {
        foreach (float credits in new[] { 100f, 100_000f })
        {
            var (u, yard, player) = QualityUpgradeTests.AtYard(credits);
            var bp = Blueprints.Save(u, player, "A", out _)!;
            var o = Dealer.Offers(u, yard, player).Single(x => x.Key == $"blueprint:{bp.Id}");
            Assert.Equal(o.Amount > 0f, Exec(Dealer, u, yard, player, o.Key).Success);
        }
    }

    [Fact]
    public void Bauplan_BeimAusruesterAnwenden_BautFehlendeModuleEin()
    {
        var (u, yard, player) = QualityUpgradeTests.AtYard();
        var bp = Blueprints.Save(u, player, "Original", out _)!;
        int cargo = player.Slots.FindIndex(s => s.Type == "cargo");
        string orig = player.Fit[cargo]!;
        player.Fit[cargo] = "cargo_universal_60";
        player.RecomputeStats(u);
        player.CargoCapacity = player.Stats.Cargo;
        Assert.Equal(new List<int> { cargo }, Blueprints.Differences(u, player, bp));
        float wallet = u.PlayerWallet.Credits;
        var r = Blueprints.Apply(u, yard, player, bp);
        Assert.Equal(1, r.Installed);
        Assert.Equal(orig, player.Fit[cargo]);
        Assert.True(u.PlayerWallet.Credits <= wallet + 0.01f + MathF.Max(0f, r.Credits));
        Assert.Equal("Schiff entspricht schon dem Bauplan.", Blueprints.ApplyIssue(u, yard, player, bp));
        // anderer Schiffstyp
        bp.ModelKey = "pip";
        Assert.Equal("Bauplan gilt für ein anderes Modell.", Blueprints.ApplyIssue(u, yard, player, bp));
    }

    // ───────── Söldner ─────────

    static (Universe u, Ship player, Actor guild) MercWorld(float credits = 10_000f)
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        u.PlayerWallet.Credits = credits;
        var guild = Mercenaries.Providers(u).First(a => a.Kind == ActorKind.Guild);
        return (u, u.Ships[u.PlayerShip], guild);
    }

    [Fact]
    public void Soeldner_Anheuern_BegleitetUndKostetSold()
    {
        var (u, player, guild) = MercWorld();
        string key = $"merc:hire:{guild.Id.Value}:1";
        var pre = Comms.Preview(u, key);
        Assert.True(pre.Possible, pre.Reason);
        float wallet = u.PlayerWallet.Credits, treasury = guild.Treasury.Credits;
        var r = Comms.Execute(u, key);
        Assert.True(r.Success, r.Message);
        var c = Assert.Single(u.Mercenaries);
        var merc = u.Ships[c.Ship];
        Assert.Equal(guild.Id, merc.Owner);
        Assert.False(merc.IsPlayer);
        Assert.Equal(ShipRole.Fighter, merc.Role);
        Assert.True(merc.Capabilities.HasFlag(Capability.Combat));
        Assert.Equal(new EscortCommand(player.Id), merc.Command);
        Assert.Equal(wallet - u.Rules.MercenaryFeePerDay, u.PlayerWallet.Credits, 2);
        Assert.Equal(treasury + u.Rules.MercenaryFeePerDay, guild.Treasury.Credits, 2);
        // kein eigenes Schiff: nicht im Besitz, nicht verkaufbar, keine Befehle
        Assert.DoesNotContain(merc.Id, PlayerFleet.Owned(u).Select(s => s.Id));
        Assert.NotNull(Commands.Check(u, merc, new HoldCommand()) ?? (merc.IsPlayer ? null : "kein Spielerschiff"));
        // fliegt zum Spieler
        float d0 = Distance(u, merc, player);
        new SimulationRunner().Run(u, 600);
        Assert.True(Distance(u, merc, player) < d0 || Distance(u, merc, player) < 40f, $"Abstand {d0} → {Distance(u, merc, player)}");
        // nach Ablauf: Heimkehr und Abgang
        var events = new List<SimEvent>();
        u.Events.Raised += e => events.Add(e);
        new SimulationRunner().Run(u, u.Rules.DayTicks + 6000);
        Assert.Empty(u.Mercenaries);
        Assert.False(u.Ships.ContainsKey(merc.Id));
        Assert.Contains(events, e => e is MercenaryEnded me && me.Reason == "expired");
        Assert.Contains(events, e => e is ShipDeparted sd && sd.Ship == merc.Id && sd.Reason == DepartureReason.Returned);
    }

    static float Distance(Universe u, Ship a, Ship b)
    {
        var pa = SpaceSim.Core.Simulation.ThreatMap.Where(u, a);
        var pb = SpaceSim.Core.Simulation.ThreatMap.Where(u, b);
        return pa.Sector != pb.Sector ? 10_000f : Vec2.Distance(pa.Position, pb.Position);
    }

    [Fact]
    public void Soeldner_LeereKasse_VertragEndetOhneSchulden()
    {
        var (u, player, guild) = MercWorld(credits: 500f);
        Assert.True(Comms.Execute(u, $"merc:hire:{guild.Id.Value}:3").Success);
        var events = new List<SimEvent>();
        u.Events.Raised += e => events.Add(e);
        new SimulationRunner().Run(u, u.Rules.DayTicks + 50);
        Assert.Contains(events, e => e is MercenaryEnded me && me.Reason == "broke");
        Assert.True(u.PlayerWallet.Credits >= 0f);
        Assert.DoesNotContain(events, e => e is MercenaryPaid);
    }

    [Fact]
    public void Soeldner_StrengeSchluessel_UndGrenzen()
    {
        var (u, player, guild) = MercWorld();
        var events = new List<SimEvent>();
        u.Events.Raised += e => events.Add(e);
        foreach (var bad in new[] { "merc:hire", "merc:hire:x:1", $"merc:hire:{guild.Id.Value}:2", $"merc:hire:{guild.Id.Value}:1:1", "merc:end:5", "merc:hire:999:1",
                     $"merc:hire:{u.PlayerActor.Value}:1" })
            Assert.False(Comms.Execute(u, bad).Success, bad);
        Assert.Empty(events);
        Assert.Empty(u.Mercenaries);
        // Höchstzahl
        for (int i = 0; i < u.Rules.MercenaryMaxActive; i++) Assert.True(Comms.Execute(u, $"merc:hire:{guild.Id.Value}:1").Success);
        Assert.False(Comms.Preview(u, $"merc:hire:{guild.Id.Value}:1").Possible);
        // Angebot möglich ⇔ Ausführung gelingt (über alle Angebote)
        var (u2, _, _) = MercWorld();
        foreach (var o in Comms.Offers(u2).Where(o => o.Key.StartsWith("merc:")).ToList())
        {
            var snap = u2.Mercenaries.Count;
            bool possible = Comms.Preview(u2, o.Key).Possible;   // nach früheren Ausführungen neu (Höchstzahl)
            Assert.Equal(possible, Comms.Execute(u2, o.Key).Success);
            if (!possible) Assert.Equal(snap, u2.Mercenaries.Count);
        }
        // Krieg sperrt
        var (u3, _, g3) = MercWorld();
        u3.Rules.MercenaryMinRelation = 2f;
        Assert.False(Comms.Preview(u3, $"merc:hire:{g3.Id.Value}:1").Possible);
    }

    [Fact]
    public void Soeldner_Entlassen_FliegtHeim()
    {
        var (u, _, guild) = MercWorld();
        Assert.True(Comms.Execute(u, $"merc:hire:{guild.Id.Value}:3").Success);
        var c = u.Mercenaries[0];
        Assert.True(Comms.Execute(u, $"merc:end:{c.Ship.Value}").Success);
        Assert.True(c.Ending);
        Assert.IsType<ReturnCommand>(u.Ships[c.Ship].Command);
        Assert.False(Comms.Execute(u, $"merc:end:{c.Ship.Value}").Success);
        new SimulationRunner().Run(u, 8000);
        Assert.Empty(u.Mercenaries);
    }

    [Fact]
    public void Soeldner_Zerstoert_WrackOhneModule()
    {
        var (u, _, guild) = MercWorld();
        Assert.True(Comms.Execute(u, $"merc:hire:{guild.Id.Value}:1").Success);
        var merc = u.Ships[u.Mercenaries[0].Ship];
        Departures.RemoveShip(u, merc, DepartureReason.Destroyed, "test");
        Assert.All(u.Wrecks.Where(w => w.From == merc.Id), w => Assert.Empty(w.Modules));
        new SimulationRunner().Run(u, 20);
        Assert.Empty(u.Mercenaries);
    }

    [Fact]
    public void SpeichernLaden_Bitgleich_MitBauplanUndSoeldner()
    {
        var (u, yard, player) = QualityUpgradeTests.AtYard();
        Blueprints.Save(u, player, "Plan", out _);
        var guild = Mercenaries.Providers(u).First(a => a.Kind == ActorKind.Guild);
        Assert.True(Comms.Execute(u, $"merc:hire:{guild.Id.Value}:3").Success);
        new SimulationRunner().Run(u, 200);
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.Single(Blueprints.Of(v));
        Assert.Single(v.Mercenaries);
        new SimulationRunner().Run(u, 600);
        new SimulationRunner().Run(v, 600);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        // Baupläne nur mit Spielerpolitik im Hash
        ulong withBp = UniverseHash.Compute(v), noPol = UniverseHash.Compute(v, true, includePlayerPolitics: false);
        v.Player.Blueprints.Clear();
        Assert.NotEqual(withBp, UniverseHash.Compute(v));
        Assert.Equal(noPol, UniverseHash.Compute(v, true, includePlayerPolitics: false));
    }
}
