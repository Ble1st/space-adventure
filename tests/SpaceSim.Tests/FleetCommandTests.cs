using System.Text.Json.Nodes;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 12: Befehle und Ausführer (<see cref="OrderExecutors"/>, <see cref="OrderSystem"/>, Ebenen Notfall &gt; Befehl &gt; Automatik), Spielerflotte mit
/// Automatikaufträgen und Grenzen, Verbände (<see cref="Squadrons"/>), Hangar/Liegeplatz/Liegegebühr, Fernwechsel, Taxi, Leihschiff, Eskorten in
/// Begegnungen, Save v15 mit Migration.
/// </summary>
public class FleetCommandTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    static readonly SectorId Alpha = new(1), Beta = new(2), Gamma = new(3), Delta = new(4);

    internal static readonly string[] Phase12Rules =
    {
        "CommandShips", "CommandSquadrons", "SquadronMaxMembers", "EmergencyHullFraction", "FleetRaids", "DayTicks", "BerthFeeBySize", "ShipSellShare",
        "TaxiBaseFee", "TaxiPerJump", "TaxiPerUnit", "TaxiSpeed", "TaxiJumpTicks", "RentalModel", "RentalFee", "RentalPerDay", "RentalDeposit",
        "RentalMaxPerPlayer", "AutoRadiusJumps", "AutoMaxBuyFactor", "AutoReserve",
    };
    static readonly string[] Phase12Services = { "hangar", "taxi", "rental" };

    /// <summary>
    /// Demotet einen aktuellen Save (v15) auf v14: Befehle, Verbände, Verhaltensregeln, Liegeplätze, Leihschiffe (samt Schiff), die Dienste „hangar“,
    /// „taxi“, „rental“, die Liegegebühr der Klassen und die Phase-12-Regeln entfallen. Ältere Stände bleiben unverändert.
    /// </summary>
    internal static string ToV14Json(string json)
    {
        json = ResearchTests.ToV15Json(json);   // Phase 13: erst die Forschung zurückbauen
        var node = JsonNode.Parse(json)!.AsObject();
        if ((int)node["Version"]! < 15) return json;
        node["Version"] = 14;
        node.Remove("Squadrons"); node.Remove("NextSquadronId");
        var ships = node["Ships"]!.AsArray();
        foreach (var sh in ships.ToList())
        {
            var o = sh!.AsObject();
            if (o["RentedFrom"] is JsonNode rf && (int)rf != 0) { ships.Remove(sh); continue; }
            foreach (var k in new[] { "Command", "CommandSince", "CommandStep", "Emergency", "Engagement", "Squadron", "DockedAt", "RentedFrom", "RentedSince", "RentalDeposit" })
                o.Remove(k);
        }
        foreach (var t in node["StationTypes"]!.AsArray())
            if (t!["Services"] is JsonArray sv) foreach (var x in sv.ToList()) if (Phase12Services.Contains((string)x!)) sv.Remove(x);
        foreach (var c in node["ShipClasses"]!.AsArray()) c!.AsObject().Remove("BerthFee");
        foreach (var key in Phase12Rules) node["Rules"]!.AsObject().Remove(key);
        return node.ToJsonString();
    }

    // ───────── Hilfen ─────────

    static Universe World()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);   // Preise gesetzt
        u.PlayerWallet.Credits = 20_000f;
        return u;
    }

    static Station St(Universe u, int id) => u.Stations[new StationId(id)];

    /// <summary>Legt ein eigenes Schiff (Modell) angedockt an einer Station an.</summary>
    static Ship Own(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        s.HomeBase = at.Id;
        u.AddShip(s);
        return s;
    }

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    static void Run(Universe u, int ticks) => new SimulationRunner().Run(u, ticks);

    static bool RunUntil(Universe u, Func<bool> done, int max)
    {
        var r = new SimulationRunner();
        for (int i = 0; i < max; i++) { if (done()) return true; r.Step(u); }
        return done();
    }

    // ───────── Registry und Prüfung ─────────

    [Fact]
    public void Registry_AlleBefehle_InFesterReihenfolge_DoppeltWirft()
    {
        var r = OrderExecutors.CreateDefault();
        Assert.Equal(new[] { "goto", "hold", "trade", "mine", "build", "patrol", "guard", "escort", "follow", "return" }, r.All.Select(e => e.Kind));
        Assert.Throws<ArgumentException>(() => r.Register(new HoldExecutor()));
        Assert.Equal(Capability.Combat, r.All.First(e => e.Kind == "patrol").Needs);
        Assert.Equal(Capability.Cargo, r.All.First(e => e.Kind == "trade").Needs);
        Assert.Equal(Capability.Mining, r.All.First(e => e.Kind == "mine").Needs);
        Assert.Equal(Capability.Construction, r.All.First(e => e.Kind == "build").Needs);
    }

    [Fact]
    public void Pruefung_AktivesSchiff_Faehigkeit_Ziel()
    {
        var u = World();
        var nomad = Own(u, "nomad", St(u, 1));
        Assert.NotNull(Commands.Check(u, u.Ships[u.PlayerShip], new HoldCommand()));                // aktives Schiff steuert der Spieler
        Assert.Contains("Waffen", Commands.Check(u, nomad, new PatrolCommand(new[] { Alpha }))!);   // keine Waffen
        Assert.Contains("Mining", Commands.Check(u, nomad, new MineCommand(AutoLimits.None))!);
        Assert.NotNull(Commands.Check(u, nomad, new GoToCommand(default, default, new StationId(999))));
        Assert.Null(Commands.Check(u, nomad, new TradeCommand(AutoLimits.None)));
        var ev = Capture(u);
        Assert.False(Commands.Give(u, nomad, new PatrolCommand(new[] { Alpha }), out _));
        Assert.Null(nomad.Command);
        Assert.Empty(ev);
    }

    // ───────── Bewegungsbefehle ─────────

    [Fact]
    public void Fliegen_ZurStationInAnderemSektor_DocktAn_BefehlEndet()
    {
        var u = World();
        var nomad = Own(u, "nomad", St(u, 1));
        var target = St(u, 30);   // Handelsstation Beta
        var ev = Capture(u);
        Assert.True(Commands.Give(u, nomad, new GoToCommand(default, default, target.Id), out var msg));
        Assert.Contains("Handelsstation", msg);
        Assert.True(RunUntil(u, () => nomad.Command is null, 3000));
        Assert.Equal(target.Id, nomad.DockedAt);
        Assert.Equal(Beta, nomad.Sector);
        Assert.Contains(ev, e => e is CommandEnded { Outcome: "done", Kind: "goto" } ce && ce.Ship == nomad.Id);
        Assert.Equal(0f, nomad.Credits);   // Spielerschiff ohne eigene Kasse
    }

    [Fact]
    public void Halten_BeendetDieReiseAnOrtUndStelle()
    {
        var u = World();
        var nomad = Own(u, "nomad", St(u, 1));
        Commands.Give(u, nomad, new GoToCommand(default, default, St(u, 30).Id), out _);
        Run(u, 8);
        Assert.NotNull(nomad.Transit);
        Assert.True(Commands.Give(u, nomad, new HoldCommand(), out _));
        Assert.Null(nomad.Transit);
        var pos = nomad.Position;
        Run(u, 200);
        Assert.Equal(pos, nomad.Position);
        Assert.IsType<HoldCommand>(nomad.Command);
    }

    [Fact]
    public void Folgen_UndHeimkehr_MitVersorgung()
    {
        var u = TestWorlds.WithFuel();   // Phase 14: Versorgung mit Treibstoff an der Raffinerie (in der Standardwelt aus)
        new SimulationRunner().Run(u, 1);
        u.PlayerWallet.Credits = 20_000f;
        var leader = Own(u, "nomad", St(u, 1));
        var follower = Own(u, "pip", St(u, 2));
        Commands.Give(u, leader, new GoToCommand(default, default, St(u, 30).Id), out _);
        Assert.True(Commands.Give(u, follower, new FollowCommand(leader.Id), out _));
        Assert.True(RunUntil(u, () => leader.Command is null && follower.DockedAt == St(u, 30).Id, 4000));

        // Heimkehr: Heimatbasis = Steelworks (Werk 1), dort volltanken
        follower.HomeBase = St(u, 26).Id;   // Raffinerie Alpha
        follower.Fuel[follower.Fuel.Keys.First()] = 10f;
        Commands.Cancel(u, follower);
        Assert.True(Commands.Give(u, follower, new ReturnCommand(), out _));
        Assert.True(RunUntil(u, () => follower.Command is null, 4000));
        Assert.Equal(St(u, 26).Id, follower.DockedAt);
        Assert.True(follower.TotalFuel > follower.Stats.FuelCapacity - 1f);
    }

    // ───────── Automatikaufträge der Spielerflotte ─────────

    [Fact]
    public void Handel_Automatisch_BuchtAufDieSpielerkasse_UndHaeltDieGrenzen()
    {
        var u = World();
        var nomad = Own(u, "nomad", St(u, 5));   // Beta
        var limits = new AutoLimits(Beta, 1, 1.3f, 5000f);
        var ev = Capture(u);
        Assert.True(Commands.Give(u, nomad, new TradeCommand(limits), out _));
        Assert.Equal(ShipRole.Trader, nomad.Role);
        float minWallet = float.MaxValue;
        var runner = new SimulationRunner();
        for (int i = 0; i < 6000; i++)
        {
            runner.Step(u);
            if (nomad.Order is TradeOrder o && nomad.Phase == TradePhase.GoingToBuy)
            {
                Assert.True(AutoLimitRules.InRange(u, limits, u.Stations[o.Buy].Sector));
                Assert.True(AutoLimitRules.InRange(u, limits, u.Stations[o.Sell].Sector));
            }
            minWallet = MathF.Min(minWallet, u.PlayerWallet.Credits);
        }
        var trades = ev.OfType<TradeCompleted>().Where(t => t.Ship == nomad.Id).ToList();
        Assert.NotEmpty(trades);
        // Phase 19: ein Restverkauf (Ware aus einer früheren Runde, die die Station nicht ganz nahm) hat keine Einkaufsstation (Id 0)
        Assert.All(trades, t => Assert.True((t.From.Value == 0 || AutoLimitRules.InRange(u, limits, u.Stations[t.From].Sector)) && AutoLimitRules.InRange(u, limits, u.Stations[t.To].Sector)));
        Assert.Equal(0f, nomad.Credits);                       // Geld bei der Person
        Assert.True(minWallet >= limits.Reserve - 200f);        // Rücklage hält (bis auf Treibstoffkäufe)
    }

    [Fact]
    public void Schuerfen_Automatisch_VerkauftAufDieSpielerkasse()
    {
        var u = World();
        var miner = Own(u, "prospector", St(u, 1));
        float before = u.PlayerWallet.Credits;
        var ev = Capture(u);
        Assert.True(Commands.Give(u, miner, new MineCommand(new AutoLimits(Alpha, 1, 0f, 0f)), out _));
        Run(u, 8000);
        var sales = ev.OfType<PlayerSold>().Where(m => m.Ship == miner.Id).ToList();   // Spielerschiffe verkaufen als Spieler
        Assert.NotEmpty(sales);
        Assert.True(u.PlayerWallet.Credits > before - 300f);   // Erlöse kommen bei der Person an (abzüglich Treibstoff)
        Assert.Equal(0f, miner.Credits);
        Assert.All(ev.OfType<MinerShot>().Where(s => s.Ship == miner.Id), s => Assert.True(AutoLimitRules.InRange(u, new AutoLimits(Alpha, 1, 0f, 0f), s.Sector)));
    }

    [Fact]
    public void Notfall_HuelleKritisch_FliegtZurReparatur_UndSetztDenBefehlFort()
    {
        var u = World();
        var nomad = Own(u, "nomad", St(u, 5));
        Commands.Give(u, nomad, new TradeCommand(AutoLimits.None), out _);
        nomad.Hull = nomad.MaxHull * 0.2f;
        var ev = Capture(u);
        Run(u, 1);
        Assert.True(nomad.Emergency);
        Assert.Contains(ev, e => e is EmergencyStarted es && es.Ship == nomad.Id);
        Assert.True(RunUntil(u, () => !nomad.Emergency, 6000));
        Assert.True(nomad.Hull > nomad.MaxHull * 0.9f);
        Assert.IsType<TradeCommand>(nomad.Command);
        Assert.Equal(ShipRole.Trader, nomad.Role);
        Assert.Contains(ev, e => e is EmergencyEnded ee && ee.Ship == nomad.Id);
    }

    [Fact]
    public void Befehlskapazitaet_BegrenztEinzelbefehleUndVerbaende()
    {
        var u = World();
        var (ships, squads) = Commands.Capacity(u, u.PlayerActor);
        var fleet = Enumerable.Range(0, ships + 1).Select(_ => Own(u, "pip", St(u, 1))).ToList();
        (ships, squads) = Commands.Capacity(u, u.PlayerActor);   // mehr Schiffe = größerer Akteur
        int given = fleet.Count(s => Commands.Give(u, s, new HoldCommand(), out _));
        Assert.Equal(Math.Min(fleet.Count, ships), given);
        Assert.Equal(given, Commands.Commanded(u, u.PlayerActor));
        // Verbände: höchstens so viele wie die Größenklasse erlaubt
        int created = 0;
        foreach (var s in fleet) if (s.Squadron.Value == 0 && Squadrons.Create(u, s, "", out _) is not null) created++;
        Assert.Equal(Math.Min(squads, fleet.Count), created);
    }

    // ───────── Verbände ─────────

    [Fact]
    public void Verband_AnfuehrerPlant_MitgliederFolgen_LangsamstesTempo_Nachfolge()
    {
        var u = World();
        var lead = Own(u, "prospector", St(u, 1));   // 9.4
        var slow = Own(u, "pip", St(u, 1));          // 8.0
        var other = Own(u, "nomad", St(u, 1));
        var sq = Squadrons.Create(u, lead, "Alpha-Staffel", out _)!;
        Assert.True(Squadrons.Join(u, sq, slow, out _));
        Assert.True(Squadrons.Join(u, sq, other, out _));
        Assert.IsType<FollowCommand>(slow.Command);
        Assert.Equal(8.0f, Squadrons.Speed(u, lead)!.Value, 2);
        Assert.True(Squadrons.Command(u, sq, new GoToCommand(default, default, St(u, 30).Id), out _));
        Run(u, 2);
        Assert.NotNull(lead.Transit);
        // Tempo des langsamsten Mitglieds: Reisedauer wie Pip, nicht wie Prospector
        var legs = lead.Transit!.Legs;
        float dist = legs.Sum(l => Vec2.Distance(l.From, l.To));
        Assert.True(lead.Transit.ArrivalTick - lead.Transit.StartTick >= dist / 8.0f - legs.Count);
        Assert.True(RunUntil(u, () => lead.Command is null && slow.DockedAt == St(u, 30).Id && other.DockedAt == St(u, 30).Id, 4000));

        // höchstens 8 Mitglieder
        for (int i = 0; i < 10; i++) Squadrons.Join(u, sq, Own(u, "pip", St(u, 30)), out _);
        Assert.Equal(u.Rules.SquadronMaxMembers, sq.Members.Count);

        // Anführer fällt aus: das nächste Mitglied übernimmt Befehl und Folger
        Squadrons.Command(u, sq, new GoToCommand(default, default, St(u, 5).Id), out _);
        u.RemoveShip(lead.Id, "test");
        Assert.Equal(slow.Id, sq.Leader);
        Assert.IsType<GoToCommand>(slow.Command);
        Assert.Equal(slow.Id, ((FollowCommand)other.Command!).Target);

        // Arbeitsbefehl: wer kann, handelt; der Rest folgt
        Assert.True(Squadrons.Command(u, sq, new TradeCommand(AutoLimits.None), out _));
        Assert.All(sq.Members.Select(id => u.Ships[id]), m => Assert.True(m.Command is TradeCommand || m.Command is FollowCommand));
        Squadrons.Disband(u, sq);
        Assert.Empty(u.Squadrons);
        Assert.All(u.Ships.Values, s => Assert.Equal(0, s.Squadron.Value));
    }

    [Fact]
    public void Formation_NurDarstellung_HashUnveraendert()
    {
        var u = World();
        var lead = Own(u, "pip", St(u, 1));
        var b = Own(u, "pip", St(u, 1));
        var sq = Squadrons.Create(u, lead, "", out _)!;
        Squadrons.Join(u, sq, b, out _);
        b.Position = lead.Position + new Vec2(3f, 0f);   // liegt neben dem Anführer
        ulong h = UniverseHash.Compute(u);
        var snaps = Materializer.Snapshot(u, Alpha, u.Tick);
        var sl = snaps.Single(s => s.Id == lead.Id);
        var sb = snaps.Single(s => s.Id == b.Id);
        Assert.Equal(sl.Position + Squadrons.Offset(sq.Formation, 1, sl.Direction), sb.Position);
        Assert.Equal(h, UniverseHash.Compute(u));
    }

    // ───────── Eskorte, Wache, Streife in Begegnungen ─────────

    sealed class Recorder : IEncounterResolver
    {
        public int Defenders;
        public EncounterOutcome Resolve(Universe u, IReadOnlyList<Ship> attackers, IReadOnlyList<Ship> defenders, float destroyBase)
        {
            Defenders = defenders.Count;
            return new AbstractEncounterResolver().Resolve(u, attackers, defenders, destroyBase);
        }
    }

    [Fact]
    public void Eskorte_VerteidigtImUeberfall_PassivNicht_FlottenschiffGehtNieVerloren()
    {
        var u = World();
        var hauler = Own(u, "nomad", St(u, 5));
        var guard = Own(u, "warden", St(u, 5));
        hauler.Cargo[u.FindGood("luxury_goods")!.Id] = 20f;
        Commands.Give(u, guard, new EscortCommand(hauler.Id), out _);
        var raider = u.Ships.Values.First(ThreatMap.IsRaider);
        var rec = new Recorder();
        u.Encounters = rec;
        var sector = u.Sectors[hauler.Sector];
        PirateSystem.ResolveRaid(u, raider.Transit is null ? raider : raider, hauler, sector);
        Assert.Equal(2, rec.Defenders);
        Assert.True(u.Ships.ContainsKey(hauler.Id));
        Assert.True(hauler.Hull >= hauler.MaxHull * u.Rules.PlayerMinHull - 0.01f);
        guard.Engagement = Engagement.Passive;
        if (u.Ships.Values.FirstOrDefault(ThreatMap.IsRaider) is { } r2) { PirateSystem.ResolveRaid(u, r2, hauler, sector); Assert.Equal(1, rec.Defenders); }
        // Flottenschiffe abseits der Stationen sind Opfer (ohne Vorwarnung); die Regel FleetRaids schaltet das ab
        hauler.Position = new Vec2(150f, 150f);
        hauler.DockedAt = default;
        hauler.Cargo[u.FindGood("luxury_goods")!.Id] = 20f;
        Assert.Contains(PirateSystem.Victims(u, sector.Id), v => v.Ship.Id == hauler.Id);
        u.Rules.FleetRaids = false;
        Assert.DoesNotContain(PirateSystem.Victims(u, sector.Id), v => v.Ship.Id == hauler.Id);
    }

    // ───────── Hangar, Liegegebühr, Verkauf, Fernwechsel ─────────

    [Fact]
    public void Hangar_Wechseln_Heimschicken_Verkaufen_StrengeSchluessel()
    {
        var u = World();
        var yard = u.Stations.Values.First(s => s.TypeKey == "shipyard");
        var player = u.Ships[u.PlayerShip];
        PlayerFleet.Dock(u, player, yard);
        u.PlayerSector = yard.Sector;
        var a = Own(u, "nomad", yard);
        var b = Own(u, "pip", yard);
        b.HomeBase = St(u, 30).Id;
        var svc = new HangarService();
        var offers = svc.Offers(u, yard, player);
        Assert.Contains(offers, o => o.Key == $"switch:{a.Id.Value}");
        Assert.Contains(offers, o => o.Key == $"sell:{a.Id.Value}" && o.Amount == 1f && o.Price > 0f);
        var npc = u.Ships.Values.First(s => !s.IsPlayer && s.Transit is null);
        PlayerFleet.Dock(u, npc, yard);   // ein fremdes Schiff liegt ebenfalls hier
        Assert.Contains(Hangar.ForeignDocked(u, yard, u.PlayerActor), s => s.Id == npc.Id);
        Assert.DoesNotContain(Hangar.ForeignDocked(u, yard, u.PlayerActor), s => s.IsPlayer);
        var ev = Capture(u);
        foreach (var bad in new[] { "switch", "switch:", "switch:x", "sell:-1", "fly:5", $"switch:{a.Id.Value} " })
            Assert.False(svc.Execute(u, yard, player, new ServiceRequest(bad, default, 0f)).Success);
        Assert.Empty(ev);

        // verkaufen: Erlös = Anteil × Material zum niedrigsten Marktpreis, von der Station an die Person
        float price = Hangar.SellPrice(u, a);
        float wallet = u.PlayerWallet.Credits, stc = yard.Credits;
        Assert.True(svc.Execute(u, yard, player, new ServiceRequest($"sell:{a.Id.Value}", default, 0f)).Success);
        Assert.False(u.Ships.ContainsKey(a.Id));
        Assert.Equal(wallet + price, u.PlayerWallet.Credits, 2);
        Assert.Equal(stc - price, yard.Credits, 2);
        Assert.Contains(ev, e => e is ShipSold);

        // heim schicken
        Assert.True(svc.Execute(u, yard, player, new ServiceRequest($"home:{b.Id.Value}", default, 0f)).Success);
        Assert.IsType<ReturnCommand>(b.Command);

        // wechseln: das alte Schiff bleibt angedockt
        var c = Own(u, "pip", yard);
        Assert.True(svc.Execute(u, yard, player, new ServiceRequest($"switch:{c.Id.Value}", default, 0f)).Success);
        Assert.Equal(c.Id, u.PlayerShip);
        Assert.Equal(yard.Id, player.DockedAt);
        Assert.Contains(player, PlayerFleet.ParkedAt(u, yard));
    }

    [Fact]
    public void Liegegebuehr_NurAnFremdenStationen_JeKlasseUndTag()
    {
        var u = World();
        var st = St(u, 5);
        var pip = Own(u, "pip", st);
        var ev = Capture(u);
        float before = u.PlayerWallet.Credits, stBefore = st.Credits;
        var hs = new HangarSystem();
        for (int i = 0; i < u.Rules.DayTicks / 50; i++) hs.Step(u, 50);
        float fee = Hangar.BerthFeePerDay(u, pip);
        Assert.Equal(12f, fee);   // Skiff (Größe S)
        Assert.Equal(before - fee, u.PlayerWallet.Credits, 1);
        Assert.Equal(stBefore + fee, st.Credits, 1);
        Assert.Contains(ev, e => e is BerthFeePaid);
        // eigene Station: gratis; aktives Schiff zahlt nie
        st.Owner = u.PlayerActor;
        before = u.PlayerWallet.Credits;
        hs.Step(u, 500);
        Assert.Equal(before, u.PlayerWallet.Credits);
        Assert.Equal(0f, Hangar.BerthFeeNow(u, u.Ships[u.PlayerShip]));
    }

    [Fact]
    public void Fernwechsel_HaeltReisendesSchiffAn_BefehlEndet_AltesBleibt()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var nomad = Own(u, "nomad", St(u, 1));
        Commands.Give(u, nomad, new GoToCommand(default, default, St(u, 30).Id), out _);
        Run(u, 8);
        Assert.NotNull(nomad.Transit);
        var oldPos = player.Position;
        var ev = Capture(u);
        Assert.True(PlayerFleet.SwitchRemote(u, nomad.Id, out _));
        Assert.Equal(nomad.Id, u.PlayerShip);
        Assert.Equal(nomad.Sector, u.PlayerSector);
        Assert.Null(nomad.Transit);
        Assert.Null(nomad.Command);
        Assert.Equal(ShipRole.Player, nomad.Role);
        Assert.Equal(oldPos, player.Position);
        Assert.Contains(ev, e => e is ActiveShipChanged ac && ac.New == nomad.Id);
        Run(u, 100);
        Assert.Equal(oldPos, player.Position);   // das alte Schiff hält ohne Befehl die Position
    }

    // ───────── Taxi und Leihschiff ─────────

    [Fact]
    public void Taxi_BringtDenSpielerZumWartendenSchiff_SchiffBleibt_GesperrteRouteNichtBuchbar()
    {
        var u = World();
        var from = St(u, 30);   // Handelsstation Beta (Taxi)
        var player = u.Ships[u.PlayerShip];
        u.MoveShip(player, from.Sector); u.PlayerSector = from.Sector;
        PlayerFleet.Dock(u, player, from);
        var waiting = Own(u, "nomad", St(u, 18));   // Werft Delta
        var taxi = new TaxiService();
        var q = TaxiService.Quote(u, from, player, St(u, 18))!;
        Assert.True(q.Bookable);
        Assert.Equal(waiting.Id, q.WaitingShip!.Id);
        Assert.Equal(1, q.Jumps);
        Assert.True(q.Price > u.Rules.TaxiBaseFee);
        var noShip = TaxiService.Quote(u, from, player, St(u, 1))!;   // Werk Alpha: kein Schiff, kein Leihdienst
        Assert.False(noShip.Bookable);
        float wallet = u.PlayerWallet.Credits;
        var res = taxi.Execute(u, from, player, new ServiceRequest($"taxi:{St(u, 18).Id.Value}", default, 0f));
        Assert.True(res.Success, res.Message);
        Assert.Equal(q.Ticks, (long)res.Units);
        Assert.Equal(waiting.Id, u.PlayerShip);
        Assert.Equal(from.Id, player.DockedAt);   // das Schiff bleibt in Beta
        Assert.Equal(wallet - q.Price, u.PlayerWallet.Credits, 2);

        // gesperrte Route: alle Tore Deltas geschlossen → nicht buchbar, mit Grund (Phase 21: Delta hat Ringverbindungen, ein Tor allein sperrt nicht mehr)
        foreach (var gid in u.Sectors[St(u, 18).Sector].Gates.ToList()) GateAccess.SetState(u, u.Gates[gid], GateState.Closed);
        var blocked = TaxiService.Quote(u, St(u, 18), waiting, from)!;
        Assert.False(blocked.Bookable);
        Assert.Contains("gesperrt", blocked.Issue!);
    }

    [Fact]
    public void Leihschiff_Mieten_Zurueckgeben_MieteJeTagAusDemPfand()
    {
        var u = World();
        var station = St(u, 30);
        var player = u.Ships[u.PlayerShip];
        u.MoveShip(player, station.Sector); u.PlayerSector = station.Sector;
        PlayerFleet.Dock(u, player, station);
        var rental = new RentalService();
        float wallet = u.PlayerWallet.Credits;
        var res = rental.Execute(u, station, player, new ServiceRequest("rent:1", default, 0f));
        Assert.True(res.Success, res.Message);
        var rented = u.Ships[u.PlayerShip];
        Assert.True(rented.IsRental);
        Assert.Equal("pip", rented.ModelKey);
        Assert.Equal(wallet - u.Rules.RentalFee - u.Rules.RentalDeposit, u.PlayerWallet.Credits, 2);
        Assert.False(rental.Execute(u, station, rented, new ServiceRequest("rent:1", default, 0f)).Success);   // höchstens eines
        Assert.False(rental.Execute(u, station, rented, new ServiceRequest("rent:2", default, 0f)).Success);   // strenger Schlüssel
        Assert.NotNull(Hangar.SellIssue(u, station, rented));                                                    // nicht verkaufbar

        float charged = 0f;
        u.Events.Raised += e => { if (e is RentCharged rc && rc.Ship == rented.Id) charged += rc.Amount; };
        Run(u, u.Rules.DayTicks + 60);   // zwei angefangene Tage
        // Phase 26: die Miete läuft täglich (HangarSystem) — zwei Tage sind schon abgerechnet, nicht erst bei der Rückgabe
        Assert.Equal(2, rented.RentPaidDays);
        float rent = 2 * u.Rules.RentalPerDay;
        Assert.Equal(rent, charged, 1);
        wallet = u.PlayerWallet.Credits;
        var back = rental.Execute(u, station, rented, new ServiceRequest($"return:{rented.Id.Value}", default, 0f));
        Assert.True(back.Success, back.Message);
        Assert.False(u.Ships.ContainsKey(rented.Id));
        Assert.Equal(player.Id, u.PlayerShip);   // zurück ins eigene Schiff
        Assert.Equal(wallet + u.Rules.RentalDeposit, u.PlayerWallet.Credits, 1);   // Pfand ganz zurück, keine offenen Tage
    }

    // ───────── Determinismus, Save, Migration, NPC-Wirtschaft ─────────

    [Fact]
    public void Determinismus_UndSaveRoundtrip_V15_MitBefehlenVerbandUndLeihschiff_BitIdentisch()
    {
        Universe Make()
        {
            var u = World();
            var t = Own(u, "nomad", St(u, 5));
            Commands.Give(u, t, new TradeCommand(new AutoLimits(Beta, 2, 1.5f, 1000f)), out _);
            var m = Own(u, "prospector", St(u, 1));
            Commands.Give(u, m, new MineCommand(new AutoLimits(Alpha, 1, 0f, 0f)), out _);
            var w = Own(u, "warden", St(u, 5));
            Commands.Give(u, w, new PatrolCommand(new[] { Beta, Gamma }), out _);
            w.Engagement = Engagement.Attack;
            var lead = Own(u, "pip", St(u, 1));
            var sq = Squadrons.Create(u, lead, "Test", out _)!;
            Squadrons.Join(u, sq, Own(u, "pip", St(u, 1)), out _);
            sq.Formation = Formation.Wedge;
            Squadrons.Command(u, sq, new GoToCommand(default, default, St(u, 30).Id), out _);
            Hangar.Rent(u, St(u, 30), u.Ships[u.PlayerShip], out _);
            return u;
        }
        var a = Make(); var b = Make();
        Run(a, 3000); Run(b, 3000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));

        var json = SaveGameJson.Write(a, Now);
        Assert.Contains("\"Version\":17", json);   // Phase 14: v17
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
        Assert.Single(loaded.Squadrons);
        Assert.Contains(loaded.Ships.Values, s => s.IsRental);
        Assert.Contains(loaded.Ships.Values, s => s.Command is PatrolCommand p && p.Sectors.SequenceEqual(new[] { Beta, Gamma }));
        Assert.Contains(loaded.Ships.Values, s => s.Command is TradeCommand t && t.Limits == new AutoLimits(Beta, 2, 1.5f, 1000f));
        Run(a, 2000); Run(loaded, 2000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void OfflineNachholen_FlotteArbeitetWeiter()
    {
        var u = World();
        var t = Own(u, "nomad", St(u, 5));
        Commands.Give(u, t, new TradeCommand(AutoLimits.None), out _);
        var ev = Capture(u);
        var r = OfflineCatchUp.Run(u, new SimulationRunner(), TimeSpan.FromMinutes(20));
        Assert.True(r.TicksRun > 4000);
        Assert.Contains(ev, e => e is TradeCompleted tc && tc.Ship == t.Id);
        Assert.IsType<TradeCommand>(t.Command);
    }

    [Fact]
    public void Migration_V14ToV15_HashGleich_UndDiensteNachgetragen()
    {
        var fresh = World();
        var v14 = ToV14Json(SaveGameJson.Write(fresh, Now));
        Assert.Contains("\"Version\":14", v14);
        Assert.DoesNotContain("\"hangar\"", v14);
        var (loaded, _) = SaveGameJson.Read(v14);
        Assert.Equal(UniverseHash.Compute(fresh), UniverseHash.Compute(loaded));
        Assert.Contains("hangar", loaded.StationTypes["steelworks"].Services);
        Assert.Contains("rental", loaded.StationTypes["shipyard"].Services);
        Assert.Equal(fresh.Ships.Values.Count(s => s.DockedAt.Value != 0), loaded.Ships.Values.Count(s => s.DockedAt.Value != 0));
        Run(fresh, 1000); Run(loaded, 1000);
        Assert.Equal(UniverseHash.Compute(fresh), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void NpcWirtschaft_OhneBefehle_UnveraendertDurchDieBefehlsebene()
    {
        // Gleiche Welt mit und ohne Befehlsmodul: ohne Befehle der Spielerflotte darf die Befehlsebene nichts ändern
        var a = WorldBuilder.BuildDefault();
        var b = WorldBuilder.BuildDefault();
        var withOrders = new SimulationRunner();
        var without = new SimulationRunner(SimulationRunner.DefaultModules().Where(m => m is not SpaceSim.Core.Modules.OrderModule));
        withOrders.Run(a, 5000);
        without.Run(b, 5000);
        Assert.Equal(UniverseHash.Compute(b), UniverseHash.Compute(a));
    }

    [Fact]
    public void Schiffsverkauf_BringtNieMehrAlsDerBilligsteKauf()
    {
        // Kaufen und sofort verkaufen darf kein Geld bringen, auch wenn Module/Bausätze im Überangebot weit unter Basispreis liegen.
        var u = World();
        new SimulationRunner().Run(u, 3000);
        var probe = u.Ships.Values.OrderBy(s => s.Id.Value).First(s => s.IsPlayer);
        string model0 = probe.ModelKey; var fit0 = probe.Fit.ToList(); var slots0 = probe.Slots.ToList();
        int checkedModels = 0;
        foreach (var model in u.ShipModels.Values.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            var cls = u.ShipClasses[model.ClassKey];
            var needs = ShipPurchase.Materials(u, cls, model.Fit);
            if (needs is null) continue;
            float cheapest = u.Stations.Values.Where(st => ShipPurchase.Sells(u, st, cls))
                .Select(st => ShipPurchase.Price(u, st, needs, ShipPurchase.AssemblyOf(u, st))).DefaultIfEmpty(float.MaxValue).Min();
            if (cheapest == float.MaxValue) continue;
            probe.ModelKey = model.Key; probe.Slots.Clear(); probe.Slots.AddRange(cls.Slots.Select(c => new ClassSlot { Type = c.Type, Size = c.Size }));
            probe.Fit.Clear(); probe.Fit.AddRange(model.Fit);
            Assert.True(Hangar.SellPrice(u, probe) < cheapest, $"{model.Key}: Verkauf {Hangar.SellPrice(u, probe)} >= Kauf {cheapest}");
            checkedModels++;
        }
        probe.ModelKey = model0; probe.Slots.Clear(); probe.Slots.AddRange(slots0); probe.Fit.Clear(); probe.Fit.AddRange(fit0);
        Assert.True(checkedModels > 5);
    }
}
