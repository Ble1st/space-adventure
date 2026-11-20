using SpaceSim.Core;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Demo;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 2 — KI-Kriegsschiffe (docs/KAMPF.md „KI-Kriegsschiffe: Seeschlacht, Abwehr, Geleit“): Seeschlacht zwischen Kriegsschiffen/Polizei verfeindeter
/// Fraktionen, gezielte Abwehr mit Rücklage, Kriegsgeleit; dazu Nachtrag 1c (Frist des Sammelpunkts, Verlustschutz beim Liefern).
/// </summary>
public class KiKriegsschiffeTests
{
    static readonly FactionId A = new(1), B = new(2);

    /// <summary>Regeln dieser Runde zurücknehmen (Teil der Rücknahmekette über <see cref="BalanceFixTests.RevertBalanceFix"/>): Seeschlacht, Abwehr, Geleit aus.</summary>
    internal static void RevertWarNavy(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.NavalBattles = false; r.WarDefense = false; r.WarEscorts = false;
        r.NavalBattleCooldownTicks = 0; r.WarDefenseCountsEngaged = false;   // Nachtrag 2b
    }

    static Universe World()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        u.PlayerWallet.Credits = 50_000f;
        return u;
    }

    static Ship Warship(Universe u, FactionId f, SectorId sector, Vec2 pos, string model = "bastion")
    {
        var m = u.ShipModels[model];
        var yard = u.StationsById().First(s => !s.IsBuildSite);
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Warship, Controller.Ai, new ActorId(f.Value), yard, 0f);
        u.AddShip(s);
        Place(u, s, sector, pos);
        return s;
    }

    static void Place(Universe u, Ship s, SectorId sector, Vec2 pos, bool keepCommand = false)
    {
        if (s.Transit is not null) u.FinishTransit(s);
        s.Order = null;
        if (!keepCommand) s.Command = null;
        if (s.Sector != sector) u.MoveShip(s, sector);
        s.Position = pos;
        s.DockedAt = default;
    }

    /// <summary>Sektor einer Fraktion (nicht der Spielersektor), aus dem Polizei und Kriegsschiffe weggeräumt sind.</summary>
    static SectorId CleanSector(Universe u, FactionId owner)
    {
        var sector = u.Sectors.Values.OrderBy(s => s.Id.Value).First(s => s.Id != u.PlayerSector && s.Owner == owner).Id;
        var away = u.Sectors.Keys.OrderBy(k => k.Value).First(k => k != sector && k != u.PlayerSector);
        foreach (var s in u.ShipsById().ToList())
            if ((ThreatMap.IsPolice(s) || ThreatMap.IsRaider(s) || Wars.IsWarship(s)) && ThreatMap.Where(u, s).Sector == sector) Place(u, s, away, new Vec2(300f, 300f));
        return sector;
    }

    static void AssertRoundtrip(Universe u, int ticks)
    {
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        new SimulationRunner().Run(u, ticks);
        new SimulationRunner().Run(v, ticks);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    // ───────── Seeschlacht ─────────

    [Fact]
    public void Standardwelt_SchaltetEin_CodeVorgabeAus()
    {
        var c = new EconomyRules();
        Assert.False(c.NavalBattles || c.WarDefense || c.WarEscorts);
        var u = DemoUniverse.Create();
        Assert.True(u.Rules.NavalBattles && u.Rules.WarDefense && u.Rules.WarEscorts);
    }

    [Fact]
    public void Seeschlacht_FeindlicheKriegsschiffe_BeginnenGefecht_UndEnden()
    {
        var u = World();
        var sector = CleanSector(u, A);
        Wars.Declare(u, B, A);
        var attacker = Warship(u, B, sector, new Vec2(0f, 200f));
        var defender = Warship(u, A, sector, new Vec2(250f, 200f));
        var events = Capture(u);
        ulong before = u.Rng.State;
        NavalBattle.Trigger(u);
        var e = u.Fights.Encounters.Single(x => x.Cause == EncounterCause.Naval);
        Assert.Equal(sector, e.Sector);
        Assert.Equal(e.Of(attacker.Id)!.Side == 0 ? 1 : 0, e.Of(defender.Id)!.Side);
        // Auslösung ohne Zufallszug, je Beitritt zwei Zahlen
        var probe = new DeterministicRng(before);
        for (int i = 0; i < 2 * e.Participants.Count; i++) probe.NextDouble();
        Assert.Equal(probe.State, u.Rng.State);
        Assert.Contains(events, x => x is NavalBattleStarted);
        Assert.Contains(events, x => x is EncounterStarted es && es.Cause == EncounterCause.Naval);
        // läuft bis zum Ende: eine Seite zerstört oder geflohen
        var r = new SimulationRunner();
        for (int i = 0; i < 3000 && u.Fights.Encounters.Contains(e); i++) r.Step(u);
        Assert.DoesNotContain(e, u.Fights.Encounters);
        var end = events.OfType<EncounterEnded>().Single(x => x.Encounter == e.Id);
        Assert.True(end.Destroyed + end.Fled > 0, $"Ausgang {end.Outcome}");
        // ein zerstörtes Schiff zählt als Kriegsverlust (Müdigkeit)
        if (events.OfType<ShipDeparted>().Any(d => d.Detail == "naval"))
        {
            var w = Wars.Between(u, A, B);
            Assert.True(w is null || w.LossA + w.LossB > 0f);
        }
    }

    [Fact]
    public void Seeschlacht_NichtOhneKrieg_NichtWeit_NichtImWaffenstillstand_NichtPolizeiGegenPolizei_NichtAus()
    {
        var u = World();
        var sector = CleanSector(u, A);
        var w1 = Warship(u, B, sector, new Vec2(0f, 200f));
        var w2 = Warship(u, A, sector, new Vec2(250f, 200f));
        NavalBattle.Trigger(u);
        Assert.DoesNotContain(u.Fights.Encounters, x => x.Cause == EncounterCause.Naval);   // kein Krieg
        var war = Wars.Declare(u, B, A);
        Place(u, w2, sector, new Vec2(250f + u.Rules.NavalBattleRange + 50f, 200f));
        NavalBattle.Trigger(u);
        Assert.DoesNotContain(u.Fights.Encounters, x => x.Cause == EncounterCause.Naval);   // zu weit
        Place(u, w2, sector, new Vec2(250f, 200f));
        war.CeasefireUntil = u.Tick + 1000;
        NavalBattle.Trigger(u);
        Assert.DoesNotContain(u.Fights.Encounters, x => x.Cause == EncounterCause.Naval);   // Waffenstillstand
        war.CeasefireUntil = 0;
        u.Rules.NavalBattles = false;
        NavalBattle.Trigger(u);
        Assert.DoesNotContain(u.Fights.Encounters, x => x.Cause == EncounterCause.Naval);   // Regel aus
        u.Rules.NavalBattles = true;
        // Polizei gegen Polizei beginnt keine Schlacht
        u.RemoveShip(w1.Id, "test"); u.RemoveShip(w2.Id, "test");
        var pa = u.ShipsById().First(s => ThreatMap.IsPolice(s) && u.FactionOf(s) == A);
        var pb = u.ShipsById().First(s => ThreatMap.IsPolice(s) && u.FactionOf(s) == B);
        Place(u, pa, sector, new Vec2(0f, 200f));
        Place(u, pb, sector, new Vec2(100f, 200f));
        NavalBattle.Trigger(u);
        Assert.DoesNotContain(u.Fights.Encounters, x => x.Cause == EncounterCause.Naval);
        // ein Kriegsschiff stellt die feindliche Streife
        var w3 = Warship(u, B, sector, new Vec2(50f, 200f));
        NavalBattle.Trigger(u);
        var e = u.Fights.Encounters.Single(x => x.Cause == EncounterCause.Naval);
        Assert.NotNull(e.Of(w3.Id));
        Assert.NotNull(e.Of(pa.Id));
        // die Streife von B ist nicht Gegner: sie tritt (in Reichweite, einsatzbereit) der Seite des Kriegsschiffs bei
        if (e.Of(pb.Id) is { } pbp) Assert.Equal(e.Of(w3.Id)!.Side, pbp.Side);
    }

    [Fact]
    public void Seeschlacht_Verstaerkung_TrittDerRichtigenSeiteBei()
    {
        var u = World();
        var sector = CleanSector(u, A);
        Wars.Declare(u, B, A);
        var w1 = Warship(u, B, sector, new Vec2(0f, 200f));
        var w2 = Warship(u, A, sector, new Vec2(250f, 200f));
        var helperA = Warship(u, A, sector, new Vec2(300f, 260f));
        var helperB = Warship(u, B, sector, new Vec2(-30f, 210f));
        var far = Warship(u, A, sector, new Vec2(250f + 2f * u.Rules.NavalBattleRange, 200f));
        NavalBattle.Trigger(u);
        var e = u.Fights.Encounters.Single(x => x.Cause == EncounterCause.Naval);
        int sideA = e.Of(w2.Id)!.Side, sideB = e.Of(w1.Id)!.Side;
        Assert.Equal(sideA, e.Of(helperA.Id)!.Side);
        Assert.Equal(sideB, e.Of(helperB.Id)!.Side);
        Assert.Null(e.Of(far.Id));
    }

    [Fact]
    public void Seeschlacht_SpeichernLaden_MittenImGefecht_Bitgleich()
    {
        var u = World();
        var sector = CleanSector(u, A);
        Wars.Declare(u, B, A);
        Warship(u, B, sector, new Vec2(0f, 200f));
        Warship(u, A, sector, new Vec2(250f, 200f));
        NavalBattle.Trigger(u);
        new SimulationRunner().Run(u, 30);
        Assert.Contains(u.Fights.Encounters, x => x.Cause == EncounterCause.Naval);
        AssertRoundtrip(u, 300);
    }

    [Fact]
    public void Schalter_OhneKrieg_HashGleichMitUndOhneRegeln()
    {
        var on = DemoUniverse.Create();
        var off = DemoUniverse.Create();
        off.Rules.NavalBattles = off.Rules.WarDefense = off.Rules.WarEscorts = false;
        on.Rules.NavalBattles = on.Rules.WarDefense = on.Rules.WarEscorts = true;
        new SimulationRunner().Run(on, 3000);
        new SimulationRunner().Run(off, 3000);
        Assert.Equal(UniverseHash.Compute(on), UniverseHash.Compute(off));
    }

    // ───────── Gezielte Abwehr ─────────

    [Fact]
    public void Abwehr_SchicktNaechsteKriegsschiffe_MitRuecklage()
    {
        var u = World();
        Wars.Declare(u, B, A);
        var target = u.StationsById().First(st => !st.IsBuildSite && u.FactionOf(st) == A && st.Sector != u.PlayerSector);
        var enemy = Warship(u, B, target.Sector, target.Position + new Vec2(80f, 0f));
        enemy.Command = new AssaultCommand(target.Sector, target.Id);
        var near = u.Sectors[target.Sector].Gates.Select(g => u.Gates[u.Gates[g].Target].Sector).OrderBy(s => s.Value).First();
        var d1 = Warship(u, A, near, new Vec2(0f, 0f));
        var d2 = Warship(u, A, near, new Vec2(10f, 0f));
        var d3 = Warship(u, A, near, new Vec2(20f, 0f));
        // drei Kriegsschiffe, Rücklage 1 → zwei verteidigen (nach Sprüngen, dann Id)
        var a = WarNavy.Assignments(u);
        Assert.Equal(2, new[] { d1, d2, d3 }.Count(s => a.ContainsKey(s.Id)));
        Assert.True(a.ContainsKey(d1.Id) && a.ContainsKey(d2.Id));
        Assert.Equal(target.Sector, WarNavy.DefenseSectorFor(u, d1));
        // die Fraktion gibt den Befehl über das NavySystem (Streife im Sektor des Ziels)
        u.Tick++;
        new NavySystem().Step(u, NavySystem.Interval);
        Assert.True(d1.Command is PatrolCommand pc && pc.Sectors.Single() == target.Sector, d1.Command?.ToString());
        // ein einzelnes Kriegsschiff verteidigt
        u.RemoveShip(d2.Id, "test"); u.RemoveShip(d3.Id, "test");
        u.Tick++;
        Assert.Equal(target.Sector, WarNavy.DefenseSectorFor(u, d1));
        // Angreifer noch nicht im Sektor des Ziels: keine Abwehr
        Place(u, enemy, near, new Vec2(400f, 0f));
        enemy.Command = new AssaultCommand(target.Sector, target.Id);
        u.Tick++;
        Assert.Null(WarNavy.DefenseSectorFor(u, d1));
        // Regel aus
        Place(u, enemy, target.Sector, target.Position + new Vec2(80f, 0f));
        enemy.Command = new AssaultCommand(target.Sector, target.Id);
        u.Rules.WarDefense = false;
        u.Tick++;
        Assert.Null(WarNavy.DefenseSectorFor(u, d1));
    }

    // ───────── Geleitschutz ─────────

    [Fact]
    public void Geleit_WertvollsterHaendlerAufRiskanterRoute_UndFreigabe()
    {
        var u = World();
        Wars.Declare(u, B, A);
        // ein Sektor von A, der an einen Sektor von B grenzt (riskant)
        var border = u.Sectors.Values.OrderBy(s => s.Id.Value).First(s => s.Owner == A && WarNavy.Risky(u, A, s.Id));
        var dest = u.Sectors[border.Id].Gates.Select(g => u.Gates[u.Gates[g].Target].Sector).OrderBy(s => s.Value).First();
        var lux = u.FindGood("luxury_goods")!.Id;
        var trader = u.ShipsById().First(s => s.IsEconomyAi && s.Role == ShipRole.Trader && u.FactionOf(s) is null);
        Place(u, trader, border.Id, new Vec2(0f, 0f));
        trader.Cargo.Clear();
        trader.Cargo[lux] = 20f;
        Assert.True(Travel.Start(u, trader, dest, new Vec2(0f, 0f)));
        Assert.True(ThreatMap.CargoValue(u, trader) >= u.Rules.WarEscortMinValue);
        var guard = Warship(u, A, border.Id, new Vec2(30f, 30f));
        Assert.Equal(trader.Id, WarNavy.EscortWard(u, A, guard)?.Id);
        u.Tick++;
        new NavySystem().Step(u, NavySystem.Interval);
        Assert.True(guard.Command is EscortCommand ec && ec.Target == trader.Id, guard.Command?.ToString());
        // höchstens eins je Fraktion
        var second = Warship(u, A, border.Id, new Vec2(40f, 30f));
        Assert.Null(WarNavy.EscortWard(u, A, second));
        // Ankunft: Geleit endet
        u.FinishTransit(trader);
        Assert.False(WarNavy.EscortStillValid(u, guard, (EscortCommand)guard.Command!));
        u.Tick++;
        new NavySystem().Step(u, NavySystem.Interval);
        Assert.IsNotType<EscortCommand>(guard.Command);
        // Frieden: Heimkehr
        Assert.True(Travel.Start(u, trader, border.Id, new Vec2(10f, 10f)));
        guard.Command = new EscortCommand(trader.Id);
        Wars.MakePeace(u, Wars.Between(u, A, B)!, "test");
        Assert.False(WarNavy.EscortStillValid(u, guard, (EscortCommand)guard.Command!));
    }

    // ───────── Nachtrag 1c ─────────

    static Ship Own(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        s.HomeBase = at.Id;
        u.AddShip(s);
        s.RecomputeStats(u);
        return s;
    }

    [Fact]
    public void Sammelpunkt_Frist_AngekommeneBildenVerband_FehlendeVerlierenBefehl()
    {
        var u = World();
        u.Rules.RallyDeadlineTicks = 300;
        var yard = u.Stations[new StationId(18)];
        var a = Own(u, "falcon", yard);
        var c = Own(u, "falcon", yard);
        var b = Own(u, "falcon", yard);
        // b kommt nie an: sitzt weit weg ohne Antrieb fest (wir halten ihn jeden Takt an Ort und Stelle)
        Assert.True(FleetFormations.StartRally(u, a, new[] { c, b }, yard, out var msg), msg);
        Assert.Equal(u.Tick + 300, ((RallyCommand)a.Command!).Until);
        var far = u.Sectors.Keys.OrderBy(k => k.Value).First(k => k != yard.Sector);
        var events = Capture(u);
        var r = new SimulationRunner();
        for (int i = 0; i < 400; i++)
        {
            if (b.Sector != far || b.Transit is not null) Place(u, b, far, new Vec2(0f, 0f), keepCommand: true);
            r.Step(u);
        }
        Assert.NotEqual(0, a.Squadron.Value);
        Assert.Equal(2, u.Squadrons[a.Squadron].Members.Count);
        Assert.Equal(a.Squadron, c.Squadron);
        Assert.Equal(0, b.Squadron.Value);
        Assert.IsNotType<RallyCommand>(b.Command);
        Assert.Contains(events, x => x is RallyFormed rf && rf.Members == 2);
        // nur einer angekommen: kein Verband, alle Befehle weg (erst den Verband auflösen — Verbandskapazität)
        Squadrons.Disband(u, u.Squadrons[a.Squadron]);
        var x1 = Own(u, "falcon", yard);
        var x2 = Own(u, "falcon", yard);
        Assert.True(FleetFormations.StartRally(u, x1, new[] { x2 }, yard, out var m2), m2);
        for (int i = 0; i < 400; i++)
        {
            if (x2.Sector != far || x2.Transit is not null) Place(u, x2, far, new Vec2(0f, 0f), keepCommand: true);
            r.Step(u);
        }
        Assert.Equal(0, x1.Squadron.Value);
        Assert.IsNotType<RallyCommand>(x1.Command);
        Assert.IsNotType<RallyCommand>(x2.Command);
    }

    [Fact]
    public void Liefern_Verlustschutz_KauftNichtMitSicheremVerlust_AusSchalterKauft()
    {
        var u = World();
        var steel = u.FindGood("steel")!.Id;
        var a = u.Stations.Values.Where(s => s.Produces(steel) && !s.IsBuildSite).OrderBy(s => s.Id.Value).First();
        var b = u.Stations.Values.Where(s => s.Consumes(steel) && !s.IsBuildSite && s.Id != a.Id).OrderBy(s => s.Id.Value).First();
        var mule = Own(u, "mule", a);
        mule.Position = a.DockPoint(mule.Id);
        mule.DockedAt = a.Id;
        a.Market[steel].Amount = 500f;
        a.Market[steel].Price = 40f;
        b.Market[steel].Price = 20f;   // B zahlt weniger, als A kostet
        Assert.NotNull(DeliverExecutor.LossIssue(u, mule, a, b, steel, 10f));
        var events = Capture(u);
        var guarded = new DeliverCommand(a.Id, b.Id, steel, 10f, 0f);
        var ex = new DeliverExecutor();
        mule.NextThinkTick = u.Tick;
        ex.Step(u, mule, guarded, 1);
        Assert.Equal(0f, mule.Cargo.GetValueOrDefault(steel));
        Assert.Contains(events, x => x is TraderBuyRefused tr && tr.Ship == mule.Id);
        var free = guarded with { AllowLoss = true };
        mule.NextThinkTick = u.Tick;
        ex.Step(u, mule, free, 1);
        Assert.True(mule.Cargo.GetValueOrDefault(steel) >= 1f);
        // B zahlt mehr: kein Verlust
        b.Market[steel].Price = 80f;
        Assert.Null(DeliverExecutor.LossIssue(u, mule, a, b, steel, 10f));
    }

    [Fact]
    public void Codec_FristUndVerlustschutz_Rundweg()
    {
        var cmds = new ShipCommand[]
        {
            new DeliverCommand(new StationId(1), new StationId(2), new GoodId(3), 25f, 1.2f, AllowLoss: true),
            new DeliverCommand(new StationId(1), new StationId(2), new GoodId(3), 25f, 1.2f),
            new RallyCommand(new StationId(18), new ShipId(77), 5400),
            new RallyCommand(new StationId(18), new ShipId(77)),
        };
        foreach (var c in cmds) Assert.Equal(c, FleetOpsCodec.FromSave(FleetOpsCodec.ToSave(c)!));
    }
}
