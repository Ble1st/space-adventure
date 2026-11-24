using System.Globalization;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Herrenlose Schiffe und die Aufträge „Panne“ und „Schiff liefern“ (Lücken gegenüber Interstellar Pilot 2, docs/KAMPF.md „Herrenlose Schiffe“).
/// </summary>
public class HerrenlosTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Alles dieser Runde zurücknehmen (Referenz-Hash-Tests; über <see cref="SchutzKonvoiTests.RevertSchutz"/>): Schalter aus.</summary>
    internal static void RevertDerelict(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.DerelictShips = false; r.DerelictEventTicks = 0;
        r.MissionBreakdownChance = 0f; r.MissionShipDeliveryChance = 0f;
    }

    static Universe World(ulong seed = 1) => SchmuggelForschungAuftragTests.World(seed: seed);

    static Ship Trader(Universe u) => u.ShipsById().First(s => s.Role == ShipRole.Trader && s.IsEconomyAi);

    [Fact]
    public void Schalter_CodeAus_WeltAn()
    {
        var r = new EconomyRules();
        Assert.False(r.DerelictShips);
        Assert.Equal(0, r.DerelictEventTicks);
        Assert.Equal(0f, r.MissionBreakdownChance);
        Assert.Equal(0f, r.MissionShipDeliveryChance);
        var u = WorldBuilder.BuildDefault();
        Assert.True(u.Rules.DerelictShips);
        Assert.True(u.Rules.DerelictEventTicks > 0 && u.Rules.MissionBreakdownChance > 0f && u.Rules.MissionShipDeliveryChance > 0f);
        var def = TestWorlds.Def(fuel: false, research: false);
        RevertDerelict(def);
        Assert.False(def.Rules!.DerelictShips);
    }

    [Fact]
    public void Zerstoert_MitChance_BleibtHerrenlosStattWrack()
    {
        var u = World();
        u.Rules.DerelictChance = 1f;
        var t = Trader(u);
        var food = u.FindGood("food")!.Id;
        t.Cargo[food] = 20f;
        int wrecks = u.Wrecks.Count;
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        Departures.RemoveShip(u, t, DepartureReason.Destroyed, "raid");
        Assert.False(u.Ships.ContainsKey(t.Id));
        Assert.Equal(wrecks, u.Wrecks.Count);
        var c = ev.OfType<DerelictCreated>().Single();
        var d = u.Ships[c.Ship];
        Assert.True(d.IsDerelict);
        Assert.Equal(Controller.None, d.Controller);
        Assert.Equal(u.FreeActor, d.Owner);
        Assert.False(d.IsEconomyAi);
        Assert.InRange(d.Hull / d.MaxHull, 0.049f, 0.151f);
        Assert.Equal(10f, d.Cargo[food], 2);
        Assert.Equal(u.Tick + u.Rules.DerelictTicks, d.DerelictUntil);
    }

    [Fact]
    public void Schalter_Aus_KeinZugWrackWieBisher()
    {
        var u = World();
        u.Rules.DerelictShips = false;
        var t = Trader(u);
        t.Cargo[u.FindGood("food")!.Id] = 20f;
        ulong rng = u.Rng.State;
        // Wrack-Ziehungen (Module) laufen wie bisher — die herrenlose Regel selbst zieht nichts
        var copy = World(); copy.Rules.DerelictShips = false;
        var t2 = Trader(copy); t2.Cargo[copy.FindGood("food")!.Id] = 20f;
        Departures.RemoveShip(u, t, DepartureReason.Destroyed, "raid");
        Wrecks.FromShip(copy, t2);
        Assert.Equal(copy.Rng.State, u.Rng.State);
        Assert.NotEqual(rng, u.Rng.State);
        Assert.DoesNotContain(u.Ships.Values, s => s.IsDerelict);
    }

    [Fact]
    public void Herrenlos_LiegtStill_KeinOpfer_KeinZiel_Verfaellt()
    {
        var u = World();
        var sec = u.Sectors[u.PlayerSector];
        var pos = Derelicts.Spot(u, sec, u.Ships[u.PlayerShip].Position)!.Value;
        var d = Derelicts.Create(u, "unit_trader_100", sec.Id, pos, u.FreeActor, 0.1f, u.Tick + 3000)!;
        d.Cargo[u.FindGood("luxury_goods")!.Id] = 20f;   // wertvoll — trotzdem kein Opfer
        var runner = new SimulationRunner();
        for (int i = 0; i < 2900; i += 100)
        {
            runner.Run(u, 100);
            Assert.True(u.Ships.ContainsKey(d.Id));
            Assert.Equal(pos.X, d.Position.X, 3); Assert.Equal(pos.Y, d.Position.Y, 3);
            Assert.Null(d.Order); Assert.Null(d.Transit); Assert.Null(d.Command);
            Assert.True(d.IsDerelict);
            Assert.DoesNotContain(PirateSystem.Victims(u, sec.Id), v => v.Ship.Id == d.Id);
            Assert.False(u.Fights.IsEngaged(d.Id));
        }
        Assert.Equal(SpaceSim.Core.Combat.TargetBlock.Derelict, SpaceSim.Core.Combat.PlayerCombat.TargetBlockOf(u, u.Ships[u.PlayerShip], d));
        runner.Run(u, 200);
        Assert.False(u.Ships.ContainsKey(d.Id));   // verfallen
    }

    [Fact]
    public void Uebernehmen_PerFunk_NurNahGenug()
    {
        var u = World();
        var me = u.Ships[u.PlayerShip];
        me.DockedAt = default;
        var d = Derelicts.Create(u, "unit_trader_100", me.Sector, me.Position + new Vec2(80f, 0f), u.FreeActor, 0.1f, u.Tick + 5000)!;
        var view = Hail.View(u, HailTarget.OfShip(d.Id));
        var topic = view.Topics.Single(t => t.Key == "claim");
        Assert.False(topic.Possible);
        Assert.Contains("Zu weit", topic.Reason);
        Assert.False(Hail.Execute(u, HailTarget.OfShip(d.Id), "claim").Success);
        Assert.True(d.IsDerelict);
        d.Position = me.Position + new Vec2(20f, 0f);
        var r = Hail.Execute(u, HailTarget.OfShip(d.Id), "claim");
        Assert.True(r.Success, r.Message);
        Assert.Equal(u.PlayerActor, d.Owner);
        Assert.True(d.IsPlayer);
        Assert.Equal(ShipRole.Player, d.Role);
        Assert.Equal(0, d.DerelictUntil);
        Assert.Contains(d, PlayerFleet.Owned(u));
    }

    [Fact]
    public void Zufallsereignis_EigenerRng_Deterministisch()
    {
        var a = World(); var b = World();
        ulong ra = a.Rng.State;
        Ship? da = null;
        long bucket = 0;
        for (; bucket < 50 && da is null; bucket++) da = Derelicts.SpawnEvent(a, bucket);
        Assert.NotNull(da);
        Assert.Equal(ra, a.Rng.State);   // Welt-RNG unberührt
        var db = Derelicts.SpawnEvent(b, bucket - 1);
        Assert.NotNull(db);
        Assert.Equal(da!.ModelKey, db!.ModelKey);
        Assert.Equal(da.Sector, db.Sector);
        Assert.Equal(da.Position, db.Position);
        Assert.True(da.IsDerelict && da.DerelictUntil > 0);
    }

    static Station HqOf(Universe u) => u.StationsById().First(s => u.StationTypes[s.TypeKey].Government == GovernmentKind.SectorHq && u.FactionOf(s) is { } f && u.IsNpcFaction(f));

    [Fact]
    public void Panne_Annehmen_Uebernehmen_Folgen_Abliefern()
    {
        var u = World();
        var hq = HqOf(u);
        var o = SchmuggelForschungAuftragTests.Offer(u, BreakdownMission.KeyId, hq);
        var me = u.Ships[u.PlayerShip];
        float w0 = u.Player.Treasury.Credits;
        var (m, issue) = MissionRules.Accept(u, o.Id, me, remote: true);
        Assert.Null(issue);
        var d = BreakdownMission.ShipOf(u, m!)!;
        Assert.True(d.IsDerelict);
        Assert.Equal(o.Client, d.Owner);
        Assert.Equal(0, d.DerelictUntil);
        Assert.Equal(MissionMarkerKind.Ship, MissionMarkers.For(u).Single(x => x.Mission == m!.Id).Kind);
        // ein fremder Spieler-Übernehmer ohne Auftrag? hier: mit Auftrag ok, aber erst nah heran
        if (me.Sector != d.Sector) u.MoveShip(me, d.Sector);
        me.DockedAt = default;
        me.Position = d.Position + new Vec2(15f, 0f);
        Assert.True(Derelicts.Claim(u, me, d, out var msg), msg);
        Assert.True(d.IsPlayer);
        Assert.IsType<FollowCommand>(d.Command);
        Assert.Equal(MissionMarkerKind.Station, MissionMarkers.For(u).Single(x => x.Mission == m!.Id).Kind);
        Assert.Contains("Pannenauftrag", Hangar.SellIssue(u, hq, d) ?? "");
        // zur Station gebracht: der Spieler dockt an der Auftragsstation, das Pannenschiff folgt und dockt mit → erfüllt, Schiff zurück an die Fraktion
        if (me.Sector != hq.Sector) u.MoveShip(me, hq.Sector);
        PlayerFleet.Dock(u, me, hq);
        var paid = new List<MissionPaid>(); u.Events.Raised += e => { if (e is MissionPaid mp) paid.Add(mp); };
        var runner = new SimulationRunner();
        for (int i = 0; i < 6000 && m!.State == MissionState.Active; i += 20) runner.Run(u, 20);
        Assert.True(m!.State == MissionState.Completed, $"state {m.State} dock {d.DockedAt.Value} transit {d.Transit is not null} cmd {d.Command}");
        Assert.False(u.Ships.ContainsKey(d.Id));
        Assert.Equal(o.Reward, paid.Single(p => p.Mission == m.Id).Amount, 1);   // Belohnung bezahlt (Liegegebühren laufen daneben)
    }

    [Fact]
    public void Panne_OhneAuftrag_NichtUebernehmbar_SelbstGeflogen_PersonZuFuss()
    {
        var u = World();
        var hq = HqOf(u);
        var me = u.Ships[u.PlayerShip];
        // Pannenschiff einer Fraktion ohne passenden Auftrag
        var foreign = Derelicts.Create(u, "unit_trader_100", me.Sector, me.Position + new Vec2(10f, 0f), hq.Owner, 0.45f, 0)!;
        me.DockedAt = default;
        Assert.Contains("Pannenauftrag", Derelicts.ClaimIssue(u, me, foreign));
        var o = SchmuggelForschungAuftragTests.Offer(u, BreakdownMission.KeyId, hq);
        var (m, _) = MissionRules.Accept(u, o.Id, me, remote: true);
        var d = BreakdownMission.ShipOf(u, m!)!;
        if (me.Sector != d.Sector) u.MoveShip(me, d.Sector);
        me.Position = d.Position + new Vec2(10f, 0f);
        Assert.True(Derelicts.Claim(u, me, d, out _));
        Assert.True(PlayerFleet.SwitchRemote(u, d.Id, out var sw), sw);
        Assert.Equal(d.Id, u.PlayerShip);
        if (d.Sector != hq.Sector) u.MoveShip(d, hq.Sector);
        PlayerFleet.Dock(u, d, hq);
        new SimulationRunner().Run(u, 40);
        Assert.True(m!.State == MissionState.Completed, $"state {m.State} dock {d.DockedAt.Value} hq {hq.Id.Value} transit {d.Transit is not null} player {d.IsPlayer} active {u.PlayerShip.Value}/{d.Id.Value} cmd {d.Command} sector {d.Sector.Value}/{hq.Sector.Value} onfoot {u.PlayerAtStation.Value}");
        Assert.False(u.Ships.ContainsKey(d.Id));
        Assert.Equal(me.Id, u.PlayerShip);
        Assert.Equal(hq.Id, u.PlayerAtStation);
    }

    [Fact]
    public void Panne_Abbruch_HoltDasSchiffZurueck()
    {
        var u = World();
        var hq = HqOf(u);
        var o = SchmuggelForschungAuftragTests.Offer(u, BreakdownMission.KeyId, hq);
        var me = u.Ships[u.PlayerShip];
        var (m, _) = MissionRules.Accept(u, o.Id, me, remote: true);
        var d = BreakdownMission.ShipOf(u, m!)!;
        if (me.Sector != d.Sector) u.MoveShip(me, d.Sector);
        me.DockedAt = default;
        me.Position = d.Position + new Vec2(10f, 0f);
        Assert.True(Derelicts.Claim(u, me, d, out _));
        Assert.True(MissionRules.Abandon(u, m!.Id, me).Ok);
        Assert.False(u.Ships.ContainsKey(d.Id));
    }

    [Fact]
    public void SchiffLiefern_NurNichtAktivesPassendesSchiff_BelohnungUeberVerkaufspreis()
    {
        var u = World();
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard" && u.FactionOf(s) is { } f && u.IsNpcFaction(f));
        var o = SchmuggelForschungAuftragTests.Offer(u, ShipDeliveryMission.KeyId, yard);
        var me = u.Ships[u.PlayerShip];
        var (m, issue) = MissionRules.Accept(u, o.Id, me, remote: true);
        Assert.Null(issue);
        string model = m!.Args["model"];
        // aktives Schiff an der Werft
        if (me.Sector != yard.Sector) u.MoveShip(me, yard.Sector);
        PlayerFleet.Dock(u, me, yard);
        var svc = new MissionService();
        var offer = svc.Offers(u, yard, me).Single(x => x.Key == $"deliver:{m.Id}");
        Assert.Equal(0f, offer.Amount);
        Assert.False(svc.Execute(u, yard, me, new ServiceRequest($"deliver:{m.Id}", default, 1f)).Success);
        var ship = DebugSpawn.SpawnShip(u, model, out var msg)!;
        Assert.Equal(yard.Id, ship.DockedAt);
        float sell = Hangar.SellPrice(u, ship);
        Assert.True(m.Reserved > sell, $"Belohnung {m.Reserved} > Verkauf {sell}");
        offer = svc.Offers(u, yard, me).Single(x => x.Key == $"deliver:{m.Id}");
        Assert.Equal(1f, offer.Amount);
        float w0 = u.Player.Treasury.Credits;
        var res = svc.Execute(u, yard, me, new ServiceRequest($"deliver:{m.Id}", default, 1f));
        Assert.True(res.Success, res.Message);
        Assert.False(u.Ships.ContainsKey(ship.Id));
        Assert.True(u.Ships.ContainsKey(me.Id));
        Assert.Equal(MissionState.Completed, m.State);
        Assert.Equal(w0 + m.Reserved, u.Player.Treasury.Credits, 1);
    }

    [Fact]
    public void Speichern_Laden_MitHerrenlosemSchiffUndPanne_Bitgleich()
    {
        var u = World();
        var hq = HqOf(u);
        var o = SchmuggelForschungAuftragTests.Offer(u, BreakdownMission.KeyId, hq);
        MissionRules.Accept(u, o.Id, u.Ships[u.PlayerShip], remote: true);
        var sec = u.Sectors[u.PlayerSector];
        Derelicts.Create(u, "unit_miner_100", sec.Id, Derelicts.Spot(u, sec, default)!.Value, u.FreeActor, 0.1f, u.Tick + 8000);
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        new SimulationRunner().Run(u, 600);
        new SimulationRunner().Run(v, 600);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    [Fact]
    public void Langlauf_HerrenloseSchiffeBleibenUnbewegt_NieKiWirtschaft()
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.DerelictChance = 1f;
        def.Rules.DerelictEventTicks = 1000;
        var u = WorldBuilder.Build(def, 3);
        var seen = new Dictionary<ShipId, Vec2>();
        var runner = new SimulationRunner();
        int created = 0;
        u.Events.Raised += e => { if (e is DerelictCreated) created++; };
        for (int i = 0; i < 20000; i += 200)
        {
            runner.Run(u, 200);
            foreach (var s in u.ShipsById().Where(s => s.IsDerelict))
            {
                Assert.False(s.IsEconomyAi);
                Assert.Null(s.Order); Assert.Null(s.Transit); Assert.Null(s.Command);
                Assert.Equal(Controller.None, s.Controller);
                if (seen.TryGetValue(s.Id, out var p)) Assert.Equal(p, s.Position); else seen[s.Id] = s.Position;
            }
        }
        Assert.True(created > 0);
    }
}
