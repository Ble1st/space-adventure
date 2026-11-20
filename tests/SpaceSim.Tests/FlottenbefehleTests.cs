using SpaceSim.Core.Combat;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 1/1b: Befehle der Spielerflotte — Angreifen (Schiff/Station/Tor), Verfolgen, Bergen, Tor blockieren, Liefern, Sammelpunkt, Konvoi.
/// Erfolgsweg, Verweigerungen, Verbrechen, Befehlskapazität, Speichern/Laden mitten im Befehl.
/// </summary>
public class FlottenbefehleTests
{
    static readonly SectorId Alpha = new(1), Beta = new(2), Gamma = new(3), Delta = new(4);

    static Universe World()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        u.PlayerWallet.Credits = 50_000f;
        return u;
    }

    static Station St(Universe u, int id) => u.Stations[new StationId(id)];

    static Ship Own(Universe u, string model, Station at, IReadOnlyList<string?>? fit = null)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, fit ?? m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        s.HomeBase = at.Id;
        u.AddShip(s);
        s.RecomputeStats(u);
        return s;
    }

    /// <summary>Ein freies NPC-Schiff, geparkt an einer Stelle im Sektor.</summary>
    static Ship Npc(Universe u, string model, Station near, Vec2 pos, ActorId? owner = null)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Trader, Controller.Ai, owner ?? u.FreeActor, near, 500f);
        u.AddShip(s);
        Park(s, pos);
        s.NextThinkTick = long.MaxValue / 4;   // hält still (Test)
        return s;
    }

    static void Park(Ship s, Vec2 pos) { s.Position = pos; s.DockedAt = default; }

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    static bool RunUntil(Universe u, Func<bool> done, int max)
    {
        var r = new SimulationRunner();
        for (int i = 0; i < max; i++) { if (done()) return true; r.Step(u); }
        return done();
    }

    static void Give(Universe u, Ship s, ShipCommand c)
    {
        Assert.True(Commands.Give(u, s, c, out var m), m);
    }

    /// <summary>Speichern mitten im Befehl, laden, beide Welten gleich weiterlaufen lassen — bitgleich.</summary>
    static void AssertRoundtrip(Universe u, int ticks = 300)
    {
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        new SimulationRunner().Run(u, ticks);
        new SimulationRunner().Run(v, ticks);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    // ───────── Registry ─────────

    [Fact]
    public void Registry_NeueBefehle_FuerDenSpieler()
    {
        var player = OrderExecutors.Default.All.Where(e => e.ForPlayer).Select(e => e.Kind).ToList();
        foreach (var k in new[] { CommandKinds.Attack, CommandKinds.Pursue, CommandKinds.Salvage, CommandKinds.Blockade, CommandKinds.Deliver, CommandKinds.Rally, CommandKinds.Convoy })
            Assert.Contains(k, player);
        Assert.DoesNotContain(CommandKinds.Lurk, player);
        Assert.DoesNotContain(CommandKinds.Assault, player);
        Assert.Equal("Angreifen", CommandNames.Kind(CommandKinds.Attack));
        Assert.Equal("Verfolgen", CommandNames.Kind(CommandKinds.Pursue));
    }

    // ───────── Angreifen ─────────

    [Fact]
    public void Angreifen_Schiff_StelltEsUndMeldetVerbrechen()
    {
        var u = World();
        var yard = St(u, 18);   // Werft Delta
        var falcon = Own(u, "falcon", yard);
        Park(falcon, yard.Position + new Vec2(80f, 0f));
        var trader = Npc(u, "mule", yard, falcon.Position + new Vec2(120f, 0f));
        var ev = Capture(u);
        Give(u, falcon, new AttackCommand(trader.Id, default, default, trader.Sector));
        Assert.True(RunUntil(u, () => u.Fights.IsEngaged(falcon.Id), 400), "Flottenschiff stellt das Ziel");
        var e = u.Fights.Of(falcon.Id)!;
        Assert.Equal(EncounterCause.Assault, e.Cause);
        Assert.Equal(1 - e.Of(falcon.Id)!.Side, e.Of(trader.Id)!.Side);
        Assert.Contains(ev, x => x is FleetAttackStarted { Crime: true } f && f.TargetShip == trader.Id);
        Assert.Contains(ev, x => x is CrimeReported c && c.Actor == u.PlayerActor && c.Crime is "assault" or "assault_station");
        Assert.True(Wanted.MaxLevel(u, u.PlayerActor) > 0);
    }

    [Fact]
    public void Angreifen_Kaperschiff_KeinVerbrechen_ZielWeg_Erledigt()
    {
        var u = World();
        var yard = St(u, 18);
        var falcon = Own(u, "falcon", yard);
        Park(falcon, yard.Position + new Vec2(80f, 0f));
        var pirates = u.Actors.Values.First(a => a.Kind == ActorKind.Outlaw && !Bandits.IsEstablished(u, a.Id)).Id;   // Punkt 17: kleine Piratengruppe, nicht die etablierte Fraktion
        var raider = Npc(u, "corsair", yard, falcon.Position + new Vec2(40f, 0f), pirates);
        raider.Role = ShipRole.Pirate;
        var ev = Capture(u);
        Give(u, falcon, new AttackCommand(raider.Id, default, default, raider.Sector));
        Assert.True(RunUntil(u, () => u.Fights.IsEngaged(falcon.Id), 50));
        Assert.Contains(ev, x => x is FleetAttackStarted { Crime: false });
        Assert.DoesNotContain(ev, x => x is CrimeReported c && c.Actor == u.PlayerActor);
        // Ziel weg → Befehl erledigt
        u.Fights.Encounters.Clear();
        Departures.RemoveShip(u, raider, DepartureReason.Destroyed, "test");
        u.Fights.Rebuild();
        Engage.Resume(u, falcon);
        Assert.True(RunUntil(u, () => falcon.Command is null, 20));
    }

    [Fact]
    public void Angreifen_Verweigerungen()
    {
        var u = World();
        var yard = St(u, 18);
        var falcon = Own(u, "falcon", yard);
        var other = Own(u, "falcon", yard);
        var nomad = Own(u, "nomad", yard);
        var trader = Npc(u, "mule", yard, yard.Position + new Vec2(100f, 0f));
        // eigenes Schiff, eigene Station? (keine), unbewaffnet (nomad hat Laser — Fähigkeit vorhanden; Pip ohne Waffen?) und Verhaltensregel
        Assert.Contains("Eigene Schiffe", Commands.Check(u, falcon, new AttackCommand(other.Id, default, default, other.Sector)));
        falcon.Engagement = Engagement.Passive;
        Assert.Contains("Passiv", Commands.Check(u, falcon, new AttackCommand(trader.Id, default, default, trader.Sector)));
        falcon.Engagement = Engagement.HoldFire;
        Assert.Contains("Feuer halten", Commands.Check(u, falcon, new AttackCommand(trader.Id, default, default, trader.Sector)));
        falcon.Engagement = Engagement.Defend;
        Assert.Null(Commands.Check(u, falcon, new AttackCommand(trader.Id, default, default, trader.Sector)));
        // ohne Waffen keine Fähigkeit
        var prospector = Own(u, "prospector", yard);
        Assert.Contains("Fähigkeit", Commands.Check(u, prospector, new AttackCommand(trader.Id, default, default, trader.Sector)));
        // aktives Schiff
        Assert.NotNull(Commands.Check(u, u.Ships[u.PlayerShip], new AttackCommand(trader.Id, default, default, trader.Sector)));
        // ausgeschaltete Station
        var st = St(u, 17);
        st.Condition = new StationCondition { Disabled = true };
        Assert.Contains("ausgeschaltet", Commands.Check(u, falcon, new AttackCommand(default, st.Id, default, st.Sector)));
        Assert.NotNull(nomad);
    }

    [Fact]
    public void Angreifen_ZaehltGegenDieBefehlskapazitaet()
    {
        var u = World();
        var yard = St(u, 18);
        var trader = Npc(u, "mule", yard, yard.Position + new Vec2(100f, 0f));
        var ships = Enumerable.Range(0, 24).Select(_ => Own(u, "falcon", yard)).ToList();
        foreach (var s in ships) { if (Commands.CapacityIssue(u, s) is not null) break; Give(u, s, new HoldCommand()); }
        var last = ships.First(s => s.Command is null);
        Assert.False(Commands.Give(u, last, new AttackCommand(trader.Id, default, default, trader.Sector), out var m));
        Assert.Contains("Befehlskapazität", m);
    }

    [Fact]
    public void Angreifen_Station_Belagerung_Verbrechen()
    {
        var u = World();
        var st = St(u, 17);   // eine Station in Delta (Wirtschaft)
        var yard = St(u, 18);
        var falcon = Own(u, "falcon", yard);
        var ev = Capture(u);
        Give(u, falcon, new AttackCommand(default, st.Id, default, st.Sector));
        Assert.True(RunUntil(u, () => u.Fights.IsEngaged(falcon.Id), 3000), "Belagerung beginnt");
        var e = u.Fights.Of(falcon.Id)!;
        Assert.Equal(EncounterCause.Siege, e.Cause);
        Assert.Equal(st.Id, e.Station);
        Assert.Contains(ev, x => x is CrimeReported c && c.Actor == u.PlayerActor && c.Crime == "assault_station");
    }

    [Fact]
    public void Angreifen_Tor_Belagerung_VerbrechenBeiBeidenEnden()
    {
        var u = World();
        Assert.True(u.Rules.GateCombat);
        var yard = St(u, 18);
        var gate = u.Gates.Values.Where(g => g.Sector == yard.Sector).OrderBy(g => g.Id.Value).First();
        var falcon = Own(u, "falcon", yard);
        var ev = Capture(u);
        Give(u, falcon, new AttackCommand(default, default, gate.Id, gate.Sector));
        Assert.True(RunUntil(u, () => u.Fights.IsEngaged(falcon.Id), 3000), "Torbelagerung beginnt");
        Assert.Equal(gate.Id, u.Fights.Of(falcon.Id)!.Gate);
        Assert.Contains(ev, x => x is CrimeReported c && c.Actor == u.PlayerActor && c.Crime == "assault_gate");
    }

    [Fact]
    public void Angreifen_ImKrieg_KeinVerbrechen()
    {
        var u = World();
        var yard = St(u, 18);
        var enemy = u.FactionOf(yard)!.Value;
        u.Player.Treasury.Credits = 50_000f;
        Assert.Null(Wars.PlayerDeclareIssue(u, enemy));
        Wars.PlayerDeclare(u, enemy);
        var falcon = Own(u, "falcon", St(u, 18));
        var victim = Npc(u, "mule", yard, yard.Position + new Vec2(140f, 0f), new ActorId(enemy.Value));
        Park(falcon, victim.Position + new Vec2(30f, 0f));
        falcon.DockedAt = default;
        var ev = Capture(u);
        Give(u, falcon, new AttackCommand(victim.Id, default, default, victim.Sector));
        Assert.True(RunUntil(u, () => u.Fights.IsEngaged(falcon.Id), 50));
        Assert.DoesNotContain(ev, x => x is CrimeReported c && c.Actor == u.PlayerActor);
    }

    [Fact]
    public void VerbandGreiftAn_GibtDenBewaffnetenDenBefehl()
    {
        var u = World();
        var yard = St(u, 18);
        var me = u.Ships[u.PlayerShip];
        u.MoveShip(me, yard.Sector);
        Park(me, yard.Position + new Vec2(60f, 0f));
        u.PlayerSector = yard.Sector;
        var a = Own(u, "falcon", yard); Park(a, me.Position + new Vec2(10f, 0f));
        var b = Own(u, "falcon", yard); Park(b, me.Position + new Vec2(-10f, 0f));
        var trader = Npc(u, "mule", yard, me.Position + new Vec2(150f, 0f));
        var cmd = new AttackCommand(trader.Id, default, default, trader.Sector);
        Assert.Equal("2 Schiffe", FleetAttack.WingLabel(u, cmd));
        Assert.Equal(2, FleetAttack.OrderWing(u, cmd, out var msg));
        Assert.Contains("Verbrechen", msg);
        Assert.IsType<AttackCommand>(a.Command);
        Assert.IsType<AttackCommand>(b.Command);
        // kein gültiges Ziel (eigenes Schiff) → keine Taste
        Assert.Null(FleetAttack.WingLabel(u, new AttackCommand(a.Id, default, default, a.Sector)));
    }

    // ───────── Verfolgen ─────────

    [Fact]
    public void Verfolgen_UeberSektorgrenzen_StelltDasZiel()
    {
        var u = World();
        var yard = St(u, 18);
        var falcon = Own(u, "falcon", yard);
        Park(falcon, yard.Position + new Vec2(60f, 0f));
        var trader = Npc(u, "mule", yard, yard.Position + new Vec2(-200f, 0f));
        // Ziel fliegt in einen Nachbarsektor
        var gate = u.Gates.Values.Where(g => g.Sector == yard.Sector).OrderBy(g => g.Id.Value).First();
        var far = u.Gates[gate.Target];
        Assert.True(Travel.Start(u, trader, far.Sector, far.Position + new Vec2(30f, 30f)));
        Assert.True(RunUntil(u, () => ThreatMap.Where(u, trader).Sector == far.Sector, 3000), "Ziel ist durchs Tor");
        Give(u, falcon, new PursueCommand(trader.Id, 0f, u.Tick + FleetAttack.PursuitTicks));
        Assert.True(RunUntil(u, () => u.Fights.IsEngaged(falcon.Id), FleetAttack.PursuitTicks), "verfolgt und gestellt");
        Assert.Equal(far.Sector, u.Fights.Of(falcon.Id)!.Sector);
    }

    [Fact]
    public void Verfolgen_EndetNachFrist_ZielWeg_Sperre()
    {
        var u = World();
        var yard = St(u, 18);
        var falcon = Own(u, "falcon", yard);
        var trader = Npc(u, "mule", yard, yard.Position + new Vec2(-200f, 0f));
        var ev = Capture(u);
        // Frist
        Give(u, falcon, new PursueCommand(trader.Id, 0f, u.Tick + 3));
        Assert.True(RunUntil(u, () => falcon.Command is null, 20));
        Assert.Contains(ev, x => x is PursuitEnded { How: "expired" });
        // Ziel weg
        Give(u, falcon, new PursueCommand(trader.Id, 0f, u.Tick + 2400));
        Departures.RemoveShip(u, trader, DepartureReason.Destroyed, "test");
        Assert.True(RunUntil(u, () => falcon.Command is null, 20));
        Assert.Contains(ev, x => x is PursuitEnded { How: "lost" });
        // Sperre: Ziel in einem Sektor hinter geschlossenen Toren
        var t2 = Npc(u, "mule", yard, yard.Position + new Vec2(-200f, 0f));
        var gate = u.Gates.Values.Where(g => g.Sector == yard.Sector).OrderBy(g => g.Id.Value).First();
        var far = u.Gates[gate.Target];
        u.MoveShip(t2, far.Sector);
        Park(t2, far.Position + new Vec2(40f, 40f));
        Give(u, falcon, new PursueCommand(t2.Id, 0f, u.Tick + 2400));
        foreach (var g in u.Gates.Values.Where(g => g.Sector == far.Sector).ToList()) GateAccess.SetState(u, g, GateState.Closed);
        Assert.True(RunUntil(u, () => falcon.Command is null, 50));
        Assert.Contains(ev, x => x is PursuitEnded { How: "locked" });
    }

    // ───────── Bergen ─────────

    static Ship Salvager(Universe u, Station at) =>
        Own(u, "tinker", at, new[] { "reactor_mk2", "chem_engine_m", "wreck_grapple_s", "weapon_massdriver_s", "cargo_universal_60", "cargo_universal_60", "weapon_magazine_s" });

    [Fact]
    public void Bergen_LeertWrackUndVerkauftDieBeute()
    {
        var u = World();
        var yard = St(u, 18);
        var s = Salvager(u, yard);
        Assert.True(s.Has(Capability.Salvage));
        var food = u.FindGood("food")!.Id;
        var w = Wrecks.Drop(u, yard.Sector, yard.Position + new Vec2(90f, 40f), new Dictionary<GoodId, float> { [food] = 12f }, default, "Wrack: Test")!;
        w.Modules.Add(new WreckModule { Key = "reactor_mk1", Condition = 0.8f });
        float before = u.PlayerWallet.Credits;
        var ev = Capture(u);
        Give(u, s, new SalvageCommand(yard.Sector));
        Assert.True(RunUntil(u, () => !u.Wrecks.Contains(w), 2000), "Wrack geleert");
        Assert.Contains(ev, x => x is WreckSalvaged ws && ws.Ship == s.Id && ws.Module == "reactor_mk1");
        Assert.True(RunUntil(u, () => s.Cargo.Values.Sum() < 1f, 4000), "Beute verkauft");
        Assert.True(u.PlayerWallet.Credits > before, "Erlös auf der Kasse");
        Assert.Contains(ev, x => x is PlayerSold ps && ps.Ship == s.Id);
        Assert.IsType<SalvageCommand>(s.Command);   // wiederholt (wartet auf neue Wracks)
    }

    [Fact]
    public void Bergen_OhneGreifer_Verweigert()
    {
        var u = World();
        var mule = Own(u, "mule", St(u, 18));
        Assert.Contains("Fähigkeit", Commands.Check(u, mule, new SalvageCommand(Delta)));
    }

    // ───────── Tor blockieren ─────────

    [Fact]
    public void TorBlockieren_NurImKrieg_GreiftDurchfahrerAn_OhneVerbrechen()
    {
        var u = World();
        var yard = St(u, 18);
        var enemy = u.FactionOf(yard)!.Value;
        var gate = u.Gates.Values.Where(g => g.Sector == yard.Sector).OrderBy(g => g.Id.Value).First();
        var falcon = Own(u, "falcon", yard);
        var cmd = new BlockadeCommand(gate.Id, enemy);
        Assert.Contains("Krieg", Commands.Check(u, falcon, cmd));
        u.Player.Treasury.Credits = 50_000f;
        Wars.PlayerDeclare(u, enemy);
        Assert.Null(Commands.Check(u, falcon, cmd));
        var ev = Capture(u);
        Give(u, falcon, cmd);
        Assert.True(RunUntil(u, () => falcon.Transit is null && Vec2.Distance(falcon.Position, BlockadeExecutor.Spot(u, gate, falcon)) <= 5f, 3000), "am Tor");
        var passer = Npc(u, "mule", yard, gate.Position + new Vec2(30f, 0f), new ActorId(enemy.Value));
        Assert.True(RunUntil(u, () => u.Fights.IsEngaged(falcon.Id), 50), "greift den Durchfahrer an");
        Assert.True(u.Fights.Of(falcon.Id)!.Of(passer.Id) is not null);
        Assert.DoesNotContain(ev, x => x is CrimeReported c && c.Actor == u.PlayerActor);
        // Frieden: Befehl erledigt
        u.Fights.Encounters.Clear(); u.Fights.Rebuild();
        Engage.Resume(u, falcon);
        Wars.MakePeace(u, Wars.PlayerWar(u, enemy)!, "test");
        Assert.True(RunUntil(u, () => falcon.Command is null, 20));
    }

    // ───────── Liefern ─────────

    [Fact]
    public void Liefern_PendeltAB_MitFesterMenge()
    {
        var u = World();
        var steel = u.FindGood("steel")!.Id;
        var a = u.Stations.Values.Where(s => s.Produces(steel) && !s.IsBuildSite && s.Market[steel].Amount >= 20f).OrderBy(s => s.Id.Value).First();
        var b = u.Stations.Values.Where(s => s.Consumes(steel) && s.Sector == a.Sector && !s.IsBuildSite).OrderBy(s => s.Id.Value).FirstOrDefault()
            ?? u.Stations.Values.Where(s => s.Consumes(steel) && !s.IsBuildSite).OrderBy(s => s.Id.Value).First();
        b.Market[steel].Amount = 0f;
        var mule = Own(u, "mule", a);
        var ev = Capture(u);
        Give(u, mule, new DeliverCommand(a.Id, b.Id, steel, 10f, 0f));
        Assert.True(RunUntil(u, () => ev.Any(x => x is PlayerSold ps && ps.Ship == mule.Id && ps.Station == b.Id), 4000), "an B geliefert");
        var bought = ev.OfType<PlayerBought>().First(x => x.Ship == mule.Id);
        Assert.Equal(a.Id, bought.Station);
        Assert.True(bought.Units <= 10f);
        Assert.True(RunUntil(u, () => ev.OfType<PlayerBought>().Count(x => x.Ship == mule.Id) >= 2, 4000), "wiederholt");
        Assert.Contains("nimmt diese Ware nicht", Commands.Check(u, mule, new DeliverCommand(a.Id, u.Stations.Values.First(s => !s.Market.ContainsKey(steel) && !s.IsBuildSite).Id, steel, 10f, 0f)));
        Assert.Contains("dieselbe", Commands.Check(u, mule, new DeliverCommand(a.Id, a.Id, steel, 10f, 0f)));
    }

    // ───────── Sammelpunkt ─────────

    [Fact]
    public void Sammelpunkt_SchiffeTreffenSichUndBildenEinenVerband()
    {
        var u = World();
        var yard = St(u, 18);
        var a = Own(u, "falcon", yard);
        var b = Own(u, "falcon", St(u, 17));
        var c = Own(u, "mule", yard);
        Assert.True(FleetFormations.StartRally(u, a, new[] { b, c }, yard, out var msg), msg);
        Assert.All(new[] { a, b, c }, s => Assert.IsType<RallyCommand>(s.Command));
        Assert.True(RunUntil(u, () => a.Squadron.Value != 0, 3000), "Verband gebildet");
        var sq = u.Squadrons[a.Squadron];
        Assert.Equal(a.Id, sq.Leader);
        Assert.Equal(3, sq.Members.Count);
        Assert.IsType<FollowCommand>(b.Command);
        // Verweigerungen: ein Schiff allein, schon im Verband
        Assert.False(FleetFormations.StartRally(u, Own(u, "falcon", yard), Array.Empty<Ship>(), yard, out var m1));
        Assert.Contains("zwei", m1);
        Assert.False(FleetFormations.StartRally(u, b, new[] { Own(u, "falcon", yard) }, yard, out var m2));
        Assert.Contains("Verband", m2);
    }

    [Fact]
    public void Sammelpunkt_RespektiertVerbandskapazitaet()
    {
        var u = World();
        var yard = St(u, 18);
        var (_, cap) = Commands.Capacity(u, u.PlayerActor);
        for (int i = 0; i < cap; i++) Assert.NotNull(Squadrons.Create(u, Own(u, "falcon", yard), "", out _));
        Assert.False(FleetFormations.StartRally(u, Own(u, "falcon", yard), new[] { Own(u, "falcon", yard) }, yard, out var m));
        Assert.Contains("Befehlskapazität", m);
    }

    // ───────── Konvoi ─────────

    [Fact]
    public void Konvoi_LangsamsterHaendlerFuehrt_GeleitschutzBegleitet_MitgliederFahrenMit()
    {
        var u = World();
        var yard = St(u, 18);
        var fast = Own(u, "mule", yard);
        var slow = Own(u, "hauler", yard);
        var guard = Own(u, "falcon", yard);
        Assert.True(Travel.SpeedOf(slow) < Travel.SpeedOf(fast));
        // Verweigerung: nur Kampfschiffe
        Assert.False(FleetFormations.StartConvoy(u, new[] { Own(u, "falcon", yard), Own(u, "warden", yard) }, AutoLimits.None, out var m2));
        Assert.Contains("Händler", m2);
        Assert.True(FleetFormations.StartConvoy(u, new[] { fast, slow, guard }, new AutoLimits(yard.Sector, 2, 0f, 0f), out var msg), msg);
        var sq = u.Squadrons[slow.Squadron];
        Assert.Equal(slow.Id, sq.Leader);
        Assert.IsType<ConvoyCommand>(slow.Command);
        Assert.IsType<ConvoyCommand>(fast.Command);
        Assert.Equal(new EscortCommand(slow.Id), guard.Command);
        // das Mitglied übernimmt die Kaufrunde des Anführers (gleiche Einkaufs-/Verkaufsstation und Ware)
        var r = new SimulationRunner();
        TradeOrder? copied = null, leaderAtCopy = null;
        for (int i = 0; i < 6000 && copied is null; i++)
        {
            bool idle = fast.Order is null;
            r.Step(u);
            if (idle && fast.Order is TradeOrder fo) { copied = fo; leaderAtCopy = slow.Order as TradeOrder; }
        }
        Assert.NotNull(copied);
        Assert.Equal(leaderAtCopy, copied);
    }

    // ───────── Speichern/Laden ─────────

    [Fact]
    public void SpeichernLaden_MittenImBefehl_Bitgleich()
    {
        var u = World();
        var yard = St(u, 18);
        var steel = u.FindGood("steel")!.Id;
        var a = u.Stations.Values.Where(s => s.Produces(steel) && !s.IsBuildSite).OrderBy(s => s.Id.Value).First();
        var b = u.Stations.Values.Where(s => s.Consumes(steel) && !s.IsBuildSite).OrderBy(s => s.Id.Value).First();
        var attacker = Own(u, "falcon", yard);
        var target = Npc(u, "mule", yard, yard.Position + new Vec2(-300f, 100f));
        Give(u, attacker, new AttackCommand(target.Id, default, default, target.Sector));
        Give(u, Own(u, "falcon", yard), new PursueCommand(target.Id, 0f, u.Tick + 2400));
        var salv = Salvager(u, yard);
        Wrecks.Drop(u, yard.Sector, yard.Position + new Vec2(90f, 40f), new Dictionary<GoodId, float> { [u.FindGood("food")!.Id] = 5f }, default, "Wrack");
        Give(u, salv, new SalvageCommand(yard.Sector));
        Give(u, Own(u, "mule", a), new DeliverCommand(a.Id, b.Id, steel, 0f, 1.5f));
        FleetFormations.StartRally(u, Own(u, "falcon", yard), new[] { Own(u, "falcon", St(u, 17)) }, yard, out _);
        FleetFormations.StartConvoy(u, new[] { Own(u, "mule", yard), Own(u, "hauler", yard), Own(u, "falcon", yard) }, new AutoLimits(yard.Sector, 2, 0f, 0f), out _);
        new SimulationRunner().Run(u, 40);
        AssertRoundtrip(u, 400);
    }

    [Fact]
    public void SpeichernLaden_Blockade_Bitgleich()
    {
        var u = World();
        var yard = St(u, 18);
        var enemy = u.FactionOf(yard)!.Value;
        u.Player.Treasury.Credits = 50_000f;
        Wars.PlayerDeclare(u, enemy);
        var gate = u.Gates.Values.Where(g => g.Sector == yard.Sector).OrderBy(g => g.Id.Value).First();
        Give(u, Own(u, "falcon", yard), new BlockadeCommand(gate.Id, enemy));
        new SimulationRunner().Run(u, 30);
        AssertRoundtrip(u, 300);
    }

    [Fact]
    public void Codec_AlleNeuenBefehle_Rundweg()
    {
        var cmds = new ShipCommand[]
        {
            new AttackCommand(new ShipId(5), default, default, new SectorId(3)),
            new AttackCommand(default, new StationId(7), default, new SectorId(2)),
            new AttackCommand(default, default, new GateId(4), new SectorId(1)),
            new SalvageCommand(new SectorId(4)),
            new BlockadeCommand(new GateId(9), new FactionId(2)),
            new DeliverCommand(new StationId(1), new StationId(2), new GoodId(3), 25f, 1.2f),
            new RallyCommand(new StationId(18), new ShipId(77)),
            new ConvoyCommand(new AutoLimits(new SectorId(2), 3, 1.3f, 500f, true)),
        };
        foreach (var c in cmds)
        {
            var back = FleetOpsCodec.FromSave(FleetOpsCodec.ToSave(c)!);
            Assert.Equal(c, back);
        }
    }
}
