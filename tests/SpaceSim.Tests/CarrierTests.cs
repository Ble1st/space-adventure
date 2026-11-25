using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Träger und Bordjäger (KAMPF.md „Träger und Bordjäger“).</summary>
public class CarrierTests
{
    /// <summary>Schaltet die Träger aus (Referenzhash-Tests; WorldBuilder nimmt Klasse, Modell und Testpaket heraus).</summary>
    internal static void RevertCarriers(WorldDef def) => (def.Rules ??= new EconomyRules()).Carriers = false;

    static Universe Package(ulong seed = 1)
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.Carriers = true;
        return WorldBuilder.Build(def, seed, "carrier");
    }

    static Ship Carrier(Universe u) => u.Ships[u.PlayerShip];

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    static Ship OwnFalcon(Universe u, Ship near, Vec2 offset)
    {
        var model = u.ShipModels["falcon"];
        var cls = u.ShipClasses[model.ClassKey];
        var at = u.StationsById().First(s => !s.IsBuildSite);
        var f = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        f.DockedAt = default; f.Sector = near.Sector; f.Position = near.Position + offset;
        u.AddShip(f);
        return f;
    }

    static void Run(Universe u, int ticks)
    {
        var runner = new SimulationRunner();
        for (int i = 0; i < ticks; i++) runner.Step(u);
    }

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_AusNimmtInhalteHeraus()
    {
        Assert.False(new EconomyRules().Carriers);
        var on = WorldBuilder.BuildDefault();
        Assert.True(on.Rules.Carriers);
        Assert.Equal(4, on.ShipClasses["carrier"].HangarBays);
        Assert.True(on.ShipModels.ContainsKey("carrier"));
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        RevertCarriers(def);
        var off = WorldBuilder.Build(def);
        Assert.False(off.ShipModels.ContainsKey("carrier"));
        Assert.False(off.ShipClasses.ContainsKey("carrier"));
        Assert.DoesNotContain(def.Player.StartOptions, o => o.Model == "carrier");
    }

    [Fact]
    public void KiWelt_MitUndOhneTraeger_Bitgleich_3000Ticks()
    {
        var a = WorldBuilder.Build(WorldBuilder.Parse(WorldBuilder.DefaultWorldJson()));
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        RevertCarriers(def);
        var b = WorldBuilder.Build(def);
        new SimulationRunner().Run(a, 3000);
        new SimulationRunner().Run(b, 3000);
        Assert.Equal(UniverseHash.Compute(b), UniverseHash.Compute(a));
        Assert.Empty(a.Hangars);
        Assert.DoesNotContain(a.Ships.Values, s => s.ModelKey == "carrier");
    }

    [Fact]
    public void Inhalt_Werft_LiefertTraeger_PolizeiUndPiratenNie()
    {
        var u = TestWorlds.DefaultOff();
        u.Rules.Carriers = true;
        var model = u.ShipModels["carrier"];
        var needs = ShipPurchase.Materials(u, model)!;
        Assert.Contains(u.StationsById(), st => ShipPurchase.SellsModel(u, st, model) && ShipPurchase.Missing(st, needs).Count == 0);
        Assert.DoesNotContain(CombatModels.All(u), x => x.Model.Key == "carrier");
        Assert.Contains(CombatModels.All(u, carriers: true), x => x.Model.Key == "carrier");
        Assert.True(Carriers.FighterStrength(u) > 0f);
    }

    [Fact]
    public void Startpaket_ZweiFalkenImHangar_NichtImRaum()
    {
        var u = Package();
        var c = Carrier(u);
        Assert.Equal("carrier", c.ModelKey);
        Assert.True(Carriers.IsCarrier(c));
        var stowed = Carriers.Stowed(u, c.Id);
        Assert.Equal(2, stowed.Count);
        Assert.All(stowed, f => { Assert.False(u.Ships.ContainsKey(f.Id)); Assert.Equal(c.Id, f.CarrierHome); Assert.Equal("falcon", f.ModelKey); });
        Assert.Equal(2, Carriers.FreeBays(u, c));
    }

    [Fact]
    public void Starten_Zurueck_Landen_Reparatur()
    {
        var u = Package();
        var c = Carrier(u);
        var ev = Capture(u);
        Assert.True(Carriers.TryLaunch(u, c, out var msg), msg);
        var out_ = Carriers.Launched(u, c.Id);
        Assert.Equal(2, out_.Count);
        Assert.Empty(Carriers.Stowed(u, c.Id));
        Assert.All(out_, f => { Assert.True(u.Ships.ContainsKey(f.Id)); Assert.IsType<EscortCommand>(f.Command); Assert.Equal(c.Sector, f.Sector); });
        Assert.Contains(ev, e => e is CarrierLaunched { Count: 2, Auto: false });
        // andere Befehle sind gesperrt, Fernwechsel auch
        Assert.NotNull(Commands.Check(u, out_[0], new HoldCommand()));
        Assert.False(PlayerFleet.SwitchRemote(u, out_[0].Id, out _));
        Assert.Equal(0, Commands.Commanded(u, u.PlayerActor));
        Run(u, 20);
        Assert.Equal(2, Carriers.Launched(u, c.Id).Count);   // ohne Gefecht bleiben sie draußen
        out_[0].Hull = out_[0].MaxHull * 0.5f;
        Assert.True(Carriers.TryRecall(u, c, out msg), msg);
        Run(u, u.Rules.CarrierReturnTicks + 5);
        Assert.Empty(Carriers.Launched(u, c.Id));
        Assert.Equal(2, Carriers.Stowed(u, c.Id).Count);
        Assert.Contains(ev, e => e is CarrierFighterDocked);
        var hurt = Carriers.Stowed(u, c.Id).First(f => f.Hull < f.MaxHull);
        float before = hurt.Hull;
        Run(u, 40);   // 10 s × 1 %/s
        Assert.True(hurt.Hull > before + hurt.MaxHull * 0.05f);
    }

    [Fact]
    public void Einschleusen_Regeln()
    {
        var u = Package();
        var c = Carrier(u);
        var near = OwnFalcon(u, c, new Vec2(15f, 0f));
        var far = OwnFalcon(u, c, new Vec2(200f, 0f));
        Assert.Null(Carriers.StowIssue(u, c, near));
        Assert.NotNull(Carriers.StowIssue(u, c, far));   // zu weit
        Assert.NotNull(Carriers.StowIssue(u, c, c));
        Assert.True(Carriers.TryStow(u, c, near, out var msg), msg);
        Assert.False(u.Ships.ContainsKey(near.Id));
        Assert.Equal(3, Carriers.Stowed(u, c.Id).Count);
        far.Position = c.Position + new Vec2(5f, 0f);
        Assert.True(Carriers.TryStow(u, c, far, out msg), msg);
        var fifth = OwnFalcon(u, c, new Vec2(10f, 0f));
        Assert.Equal("Hangar voll.", Carriers.StowIssue(u, c, fifth));
        // Verkauf mit Jägern an Bord gesperrt (Träger angedockt, nicht aktiv, leer)
        var st = u.StationsById().First(s => !s.IsBuildSite && s.Sector == c.Sector);
        u.PlayerShip = fifth.Id;
        c.Cargo.Clear(); c.DockedAt = st.Id;
        Assert.Equal("Erst die Jäger aus dem Hangar holen.", Hangar.SellIssue(u, st, c));
    }

    [Fact]
    public void NichtAktiverTraeger_StartetBeiGefechtsbeginn_JaegerAufSeinerSeite()
    {
        var u = Package();
        var c = Carrier(u);
        // Person verlässt den Träger nicht — wir machen einen zweiten Träger der KI-Marine (Union) daraus
        var union = u.Actors[new ActorId(1)];
        union.Treasury.Credits = 500000f;
        var model = u.ShipModels["carrier"];
        var cls = u.ShipClasses[model.ClassKey];
        var at = u.StationsById().First(s => !s.IsBuildSite && s.Sector == c.Sector);
        var npc = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, ShipRole.Warship, Controller.Ai, union.Id, at, 0f);
        npc.DockedAt = default; npc.Position = c.Position + new Vec2(-60f, 0f);
        u.AddShip(npc);
        Assert.Equal(4, Carriers.EquipNpc(u, npc, union));
        Assert.Equal(4, Carriers.Stowed(u, npc.Id).Count);
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        if (raider.Transit is not null) u.FinishTransit(raider);
        if (raider.Sector != npc.Sector) u.MoveShip(raider, npc.Sector);
        raider.Position = npc.Position + new Vec2(0f, -30f); raider.DockedAt = default;
        var e = Engage.StartRaid(u, raider, npc, u.Sectors[npc.Sector]);
        int side = e.Of(npc.Id)!.Side;
        Run(u, 1);
        var launched = Carriers.Launched(u, npc.Id);
        Assert.Equal(4, launched.Count);
        Assert.All(launched, f => Assert.Equal(side, e.Of(f.Id)?.Side));
        Assert.All(launched, f => Assert.Equal(ShipRole.Fighter, f.Role));
    }

    [Fact]
    public void TraegerVerlaesstSektor_JaegerLandenSofort()
    {
        var u = Package();
        var c = Carrier(u);
        Assert.True(Carriers.TryLaunch(u, c, out _));
        var other = u.Gates.Values.First(g => g.Sector == c.Sector).Target;
        u.MoveShip(c, u.Gates[other].Sector);
        u.PlayerSector = c.Sector;
        var ev = Capture(u);
        Run(u, 1);
        Assert.Empty(Carriers.Launched(u, c.Id));
        Assert.Equal(2, Carriers.Stowed(u, c.Id).Count);
        Assert.Contains(ev, x => x is CarrierFighterDocked { Reason: "left" });
    }

    [Fact]
    public void TraegerVerloren_HangarGehtMit_GestarteteOhneTraegerVerloren()
    {
        var u = Package();
        var c = Carrier(u);
        // ein Falke draußen, einer im Hangar: Start und gleich einen zurück
        Assert.True(Carriers.TryLaunch(u, c, out _));
        var keep = Carriers.Launched(u, c.Id)[0];
        Carriers.Stow(u, c, keep, "manual");
        var outside = Carriers.Launched(u, c.Id).Single();
        // die Person steigt zuerst um (aktives Schiff wäre über ShipLoss gelaufen): ein eigener Falke außerhalb wird aktiv
        var spare = OwnFalcon(u, c, new Vec2(300f, 0f));
        u.PlayerShip = spare.Id;
        var ev = Capture(u);
        Departures.RemoveShip(u, c, DepartureReason.Destroyed, "test");
        Assert.Contains(ev, x => x is CarrierFightersLost { Count: 1 });
        Assert.False(u.Hangars.ContainsKey(c.Id));
        Run(u, 1);
        Assert.False(u.Ships.ContainsKey(outside.Id));
        Assert.Contains(ev, x => x is ShipDeparted { Detail: "carrier_lost" });
    }

    [Fact]
    public void SpeichernLaden_Bitgleich_MitHangarUndGestartetenJaegern()
    {
        var u = Package();
        var c = Carrier(u);
        Assert.True(Carriers.TryLaunch(u, c, out _));
        Carriers.Stow(u, c, Carriers.Launched(u, c.Id)[0], "manual");
        Carriers.Stowed(u, c.Id)[0].Hull *= 0.4f;
        Run(u, 10);
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.Single(v.Hangars[c.Id]);
        Assert.Single(Carriers.Launched(v, c.Id));
        Run(u, 300);
        Run(v, 300);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    [Fact]
    public void Marine_ImKrieg_KauftTraeger_MitBordjaegernAusDerKasse_PolizeiNie()
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.Carriers = true;
        var u = WorldBuilder.Build(def, 1);
        // nur der Träger bleibt als Kampfmodell → die Marine wählt ihn, sofern zugelassen
        foreach (var k in CombatModels.All(u).Select(x => x.Model.Key).Where(k => k != "falcon").ToList()) u.ShipModels.Remove(k);
        u.ShipClasses["fighter"].Restricted = true;   // der Falke bleibt als Bordjägermodell, ist aber an keiner Werft zu kaufen
        var union = u.Actors[new ActorId(1)];
        union.Treasury.Credits = 5_000_000f;
        var ev = Capture(u);
        u.Rules.CarrierNavy = false;
        Assert.Null(NavySystem.Commission(u, new FactionId(1)));
        u.Rules.CarrierNavy = true;
        var w = NavySystem.Commission(u, new FactionId(1));
        Assert.NotNull(w);
        Assert.Equal("carrier", w!.ModelKey);
        Assert.Equal(4, Carriers.Stowed(u, w.Id).Count);
        Assert.Contains(ev, e => e is TreasuryUpkeep { Purpose: "carrier_fighters" });
        // Polizei wählt nie einen Träger
        Assert.Null(CombatModels.AtYard(u, w.Sector, 5_000_000f, 1f));
    }
}
