using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 31 (docs/TORE.md, Variante mittel): angreifbare Tore — Torklassen, gemeinsamer Vorrat beider Hälften, Ausfall als „geschlossen“, Reparatur aus
/// der freien Kasse, Notregel, KI-Blockade nur im Krieg und nie kartenteilend, Spielerangriff als Verbrechen, Speichern, Schalter aus = bisher.
/// </summary>
public class Phase31Tests
{
    static readonly FactionId Union = new(1), Guild = new(2);

    static Universe Standard(ulong? seed = null)
    {
        var u = TestWorlds.Default(seed, fuel: false, research: false);
        u.Rules.GateCombat = true;
        return u;
    }

    static List<SimEvent> Capture(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    static Gate G(Universe u, int id) => u.Gates[new GateId(id)];

    static HitResult Hull(float hull, float shield = 0f) => new(true, 1f, shield, 0f, hull);

    static void Down(Universe u, Gate g)
    {
        GateCombat.Apply(u, g, Hull(GateCombat.MaxHull(u, g) + 10f), new ActorId(9999));
        Assert.True(GateCombat.IsDown(u, g));
    }

    static void Place(Universe u, Ship s, SectorId sector, Vec2 pos)
    {
        if (s.Transit is not null) u.FinishTransit(s);
        s.Order = null; s.Phase = TradePhase.Idle; s.MinePhase = MinePhase.Idle;
        if (s.Sector != sector) u.MoveShip(s, sector);
        s.Position = pos;
        s.DockedAt = default;
    }

    // ───────── Daten ─────────

    [Fact]
    public void Torklassen_WerteUndZuordnung()
    {
        var u = Standard();
        var side = u.GateTypes["side"].Defense; var main = u.GateTypes["main"].Defense;
        Assert.Equal((2000f, 300f, 70f, 0.2f), (side.Hull, side.Shield, side.Armor, side.PointDefense));
        Assert.Equal((4000f, 500f, 100f, 0.35f), (main.Hull, main.Shield, main.Armor, main.PointDefense));
        Assert.Empty(side.Guns); Assert.Empty(main.Guns);
        foreach (var g in u.Gates.Values) Assert.Equal(g.MainRoute ? "main" : "side", g.TypeKey);
        Assert.Equal(4000f, GateCombat.MaxHull(u, G(u, 17)));
        // Schalter aus: keine Kampfwerte
        u.Rules.GateCombat = false;
        Assert.Null(GateCombat.TypeOf(u, G(u, 17)));
    }

    [Theory]
    [InlineData("type", "\"nope\"", "unbekannte Torklasse")]
    public void Torklassen_UngueltigWirft(string field, string value, string message)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(WorldBuilder.DefaultWorldJson())!;
        node["gates"]![0]![field] = System.Text.Json.Nodes.JsonNode.Parse(value);
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(WorldBuilder.Parse(node.ToJsonString())));
        Assert.Contains(message, ex.Message);
        var n2 = System.Text.Json.Nodes.JsonNode.Parse(WorldBuilder.DefaultWorldJson())!;
        n2["gateTypes"]![1]!.AsObject().Remove("defense");
        Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(WorldBuilder.Parse(n2.ToJsonString())));
    }

    // ───────── Vorrat, Ausfall, Reparatur, Notregel ─────────

    [Fact]
    public void BeideHaelften_TeilenDenVorrat()
    {
        var u = Standard();
        var a = G(u, 3); var b = G(u, 4);
        GateCombat.Apply(u, b, Hull(500f, 100f), new ActorId(9999));
        Assert.Equal(3500f, GateCombat.Hull(u, a));
        Assert.Equal(400f, GateCombat.Shield(u, a));
        Assert.NotNull(a.Condition);   // Zustand am Tor mit der kleineren Id
        Assert.Null(b.Condition);
    }

    [Fact]
    public void Ausfall_SchliesstBeideHaelften_RoutenUndReisenWerdenUmgeplant()
    {
        var u = Standard();
        var gate = G(u, 3);   // Beta–Gamma, Ringtor
        var beta = gate.Sector; var gamma = G(u, 4).Sector;
        // ein Händler auf dem Weg durch dieses Tor
        var trader = u.ShipsById().First(s => s.Role == ShipRole.Trader && s.Controller == Controller.Ai);
        Place(u, trader, beta, new Vec2(0f, 0f));
        var dest = u.StationsById().First(s => s.Sector == gamma && s.Dockable);
        Assert.True(Travel.Start(u, trader, gamma, dest.DockPoint(trader.Id)));
        Assert.Contains(trader.Transit!.Legs, l => l.Sector == gamma);
        var ev = Capture(u);
        Down(u, gate);
        Assert.Equal(GateState.Closed, G(u, 3).State);
        Assert.Equal(GateState.Closed, G(u, 4).State);
        var dis = Assert.Single(ev.OfType<GateDisabled>());
        Assert.False(dis.Splits);
        Assert.Equal(u.Rules.GateMaxOutageTicks, dis.OutageTicks);
        Assert.DoesNotContain(u.FindRoute(beta, gamma)!, id => id == gate.Id);
        // Reise neu geplant (Umweg) oder angehalten — nie über das ausgefallene Tor, keine Geisteraufträge
        if (trader.Transit is { } t) Assert.DoesNotContain(t.Legs.Zip(t.Legs.Skip(1)), p => p.First.Sector == beta && p.Second.Sector == gamma);
        else Assert.True(trader.Order is null || trader.Order is GoToOrder);
        // von Hand öffnen geht nicht
        GateAccess.SetState(u, gate, GateState.Open);
        Assert.Equal(GateState.Closed, G(u, 3).State);
    }

    [Fact]
    public void Notregel_KartenteilendesTorNach1000_AnderesNach4800Ticks()
    {
        var u = Standard();
        foreach (var a in u.Actors.Values.Where(a => a.IsFaction(u))) a.Treasury.Credits = 0f;   // keine Kasse → keine Reparatur
        var split = G(u, 17); var ring = G(u, 3);
        Assert.True(MapParts.SplitsWhenClosed(u, split.Id));
        Down(u, split);
        Assert.True(GateCombat.Splits(u, split));
        Assert.Equal(1000L, GateCombat.OutageLimit(u, split));
        var sys = new GateRepairSystem();
        var ev = Capture(u);
        u.Tick += 950; sys.Step(u, 50);
        Assert.True(GateCombat.IsDown(u, split));
        u.Tick += 50; sys.Step(u, 50);
        Assert.False(GateCombat.IsDown(u, split));
        Assert.Contains(ev.OfType<GateRestored>(), r => r.Reason == "emergency");
        Assert.Equal(GateState.Open, G(u, 17).State);
        Assert.True(GateCombat.Hull(u, split) >= u.Rules.GateReenableShare * GateCombat.MaxHull(u, split) - 0.01f);
        Down(u, ring);
        Assert.Equal(4800L, GateCombat.OutageLimit(u, ring));
        long start = u.Tick;
        while (GateCombat.IsDown(u, ring) && u.Tick - start < 6000) { u.Tick += 50; sys.Step(u, 50); }
        Assert.InRange(u.Tick - start, 4800, 4850);
    }

    [Fact]
    public void Reparatur_AusFreierKasse_NieUnterRuecklage_WiederInBetriebAb25Prozent()
    {
        var u = Standard();
        var gate = G(u, 9);   // Nebentor Borealis–Vesta (beide Union)
        var owner = u.Actors[GateCombat.DefendersOf(u, gate)[0]];
        float reserve = FactionViability.ReserveOf(u, owner);
        owner.Treasury.Credits = reserve + 100f;   // fast nichts frei
        Down(u, gate);
        var sys = new GateRepairSystem();
        u.Tick += 50; sys.Step(u, 50);
        Assert.True(owner.Treasury.Credits >= reserve - 0.01f, "Reparatur nie unter die Rücklage");
        Assert.True(GateCombat.IsDown(u, gate));
        owner.Treasury.Credits = reserve + 1_000_000f;
        var ev = Capture(u);
        long start = u.Tick;
        while (GateCombat.IsDown(u, gate) && u.Tick - start < 4000) { u.Tick += 50; sys.Step(u, 50); }
        Assert.Contains(ev.OfType<GateRestored>(), r => r.Reason == "repaired");
        Assert.True(u.Tick - start < 4000);
        Assert.Contains(ev.OfType<GateRepaired>(), r => r.Payer == GateCombat.DefendersOf(u, gate)[0]);
        Assert.Contains(ev.OfType<TreasuryUpkeep>(), t => t.Purpose == "gate_repair");
        // repariert weiter bis voll, dann kein Zustand mehr
        while (gate.Condition is not null && u.Tick - start < 40000) { u.Tick += 50; sys.Step(u, 50); }
        Assert.Null(G(u, 9).Condition);
    }

    [Fact]
    public void Routenplanung_ErklaertDieSperre()
    {
        var u = Standard();
        var split = G(u, 17);   // Gamma–Aurelia: einziger Weg
        Down(u, split);
        var me = u.Ships[u.PlayerShip];
        string? why = GateAccess.BlockReason(u, me, split.Sector, G(u, 18).Sector);
        Assert.NotNull(why);
        Assert.Contains("außer Betrieb", why);
        Assert.Contains("läuft in", why);
    }

    // ───────── KI-Blockade ─────────

    [Fact]
    public void KiBlockade_NurImKrieg_NieKartenteilend_HoechstensEines_EineJeKrieg()
    {
        var u = Standard();
        var ship = u.ShipsById().First(s => ThreatMap.IsPolice(s) && u.FactionOf(s) == Union);
        Assert.Null(Wars.PickGateTarget(u, Union, ship));   // Frieden
        var war = Wars.Declare(u, Union, Guild);
        var pick = Wars.PickGateTarget(u, Union, ship);
        Assert.NotNull(pick);
        Assert.False(MapParts.SplitsWhenClosed(u, pick!.Id));
        Assert.Contains(Guild, GateCombat.DefendersOf(u, pick));
        // Theorie über alle Paare: mit jedem schon ausgefallenen Tor wählt die KI keins mehr (GateMaxDown 1)
        foreach (var g in u.Gates.Values.Where(g => g.Id.Value < g.Target.Value).ToList())
        {
            var w = Standard();
            Wars.Declare(w, Union, Guild);
            Down(w, w.Gates[g.Id]);
            Assert.Null(Wars.PickGateTarget(w, Union, w.Ships[ship.Id]));
            // mit Obergrenze 2: nie ein Tor, das zusammen mit dem ausgefallenen die Karte teilt
            w.Rules.GateMaxDown = 2;
            if (Wars.PickGateTarget(w, Union, w.Ships[ship.Id]) is { } second)
                Assert.False(MapParts.SplitsWhenClosed(w, second.Id), $"{GateCombat.NameOf(w, second)} teilt mit {GateCombat.NameOf(w, w.Gates[g.Id])}");
        }
        // eine Blockade je Krieg
        war.Blockades = u.Rules.GateBlockadesPerWar;
        Assert.Null(Wars.PickGateTarget(u, Union, ship));
    }

    [Fact]
    public void KiBlockade_KriegsschiffeLegenEinTorStill_KarteBleibtGanz()
    {
        var u = Standard();
        u.Rules.WarDeclarations = false;
        u.Actors[Union].Treasury.Credits = 2_000_000f;
        u.Actors[Guild].Treasury.Credits = FactionViability.ReserveOf(u, u.Actors[Guild]) + 100f;   // kein Kriegsschiffkauf der Gilde, aber nicht pleite
        u.Rules.WarshipsPerWar = 3;   // die drei Fregatten bleiben im Dienst
        var war = Wars.Declare(u, Union, Guild);
        // drei Fregatten der Union; die Fraktion gibt ihnen den Blockadebefehl (NavySystem → Wars.PickGateTarget)
        var m = u.ShipModels["bastion"];
        var yard = u.StationsById().First(s => s.Dockable);
        var home = SecuritySystem.HomeBaseFor(u, Union, null)!;
        var ships = new List<Ship>();
        for (int i = 0; i < 3; i++)
        {
            var w = ShipPurchase.Create(u, u.NewShipId(), "bastion", u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Warship, Controller.Ai, Union, yard, 0f);
            w.HomeBase = home.Id;
            u.AddShip(w);
            ships.Add(w);
        }
        // die Fraktion vergibt den Blockadebefehl (NavySystem → Wars.PickGateTarget)
        new NavySystem().Step(u, NavySystem.Interval);
        var cmd = Assert.IsType<AssaultCommand>(ships[0].Command);
        Assert.NotEqual(0, cmd.Gate.Value);
        Assert.Equal(1, war.Blockades);
        var pair = new[] { cmd.Gate, u.Gates[cmd.Gate].Target };
        Assert.All(ships, w => Assert.Contains(Assert.IsType<AssaultCommand>(w.Command).Gate, pair));   // alle auf dasselbe Paar (eine Blockade)
        var target = u.Gates[cmd.Gate];
        // nur das Gefecht läuft (Verteidiger und Geschütze aus dem Weg — hier geht es um die Regeln der Blockade, nicht um die Kräfte)
        var far = u.Gates[target.Target];
        var away = u.Sectors.Keys.OrderBy(k => k.Value).First(k => k != target.Sector && k != far.Sector);
        u.Rules.StationGunsActive = false;
        u.Rules.AmmoActive = false;   // ohne Nachladefahrten (die Fregatten kämen sonst zwischendurch heim)
        foreach (var s in u.ShipsById().ToList())
            if ((ThreatMap.IsPolice(s) || ThreatMap.IsRaider(s)) && ThreatMap.Where(u, s).Sector is var sec && (sec == target.Sector || sec == far.Sector))
            { Place(u, s, away, new Vec2(300f, 300f)); s.Command = null; }
        foreach (var w in ships) { Place(u, w, target.Sector, AssaultExecutor.Standoff(u, target.Sector, target.Position, w)); }
        var exec = new AssaultExecutor();
        foreach (var w in ships) exec.Step(u, w, w.Command!, 1);
        var e = Assert.IsType<Encounter>(Siege.OfGate(u, target.Id));
        Assert.True(e.Participants.Count(p => p.Side == 0) >= 1);
        Assert.DoesNotContain(e.Participants, p => p.Side == 1 && u.FactionOf(p.Owner) == Union);   // die Angreiferfraktion verteidigt ihr eigenes Torende nicht
        var ev = Capture(u);
        var combat = new CombatSystem();
        for (int i = 0; i < 20000 && !ev.OfType<GateDisabled>().Any(); i++)
        {
            u.Tick++; combat.Step(u, 1); new ShipSystemsSystem().Step(u, 1);
            foreach (var w in ships) if (u.Ships.ContainsKey(w.Id) && !u.Fights.IsEngaged(w.Id) && w.Command is { } c) exec.Step(u, w, c, 1);   // Gefecht endet nach CombatMaxTicks — der Befehl belagert weiter
        }
        Assert.True(ev.OfType<GateDisabled>().Any(), $"hull {GateCombat.Hull(u, target):0} sh {GateCombat.Shield(u, target):0} shots {ev.OfType<ShotAtGate>().Count()} hits {ev.OfType<ShotAtGate>().Count(x => x.Hit)} dmg {ev.OfType<ShotAtGate>().Sum(x => x.HullDamage):0} msl {ev.OfType<MissileResolved>().Count()} e {Siege.OfGate(u, target.Id) is not null} parts {string.Join(",", e.Participants.Select(p => $"{p.Ship.Value}:{p.Side}:{p.Active}:{p.Exit}:x{p.X:0}"))} stX {e.StationX}");
        var down = Assert.Single(ev.OfType<GateDisabled>());
        Assert.False(down.Splits);
        Assert.False(MapParts.Of(u).IsSplit);
        Assert.Equal(1, war.Blockades);
        Assert.Equal(1, GateCombat.DownCount(u));
        // keine zweite Blockade in diesem Krieg, solange eines aus ist
        Assert.Null(Wars.PickGateTarget(u, Union, ships[0]));
        // Piraten greifen keine Tore an
        Assert.DoesNotContain(u.Ships.Values, s => ThreatMap.IsRaider(s) && s.Command is AssaultCommand);
        Assert.DoesNotContain(ev.OfType<ShotAtGate>(), x => u.Ships.TryGetValue(x.Shooter, out var sh) && ThreatMap.IsRaider(sh));
    }

    // ───────── Spieler ─────────

    [Fact]
    public void Spielerangriff_IstVerbrechenBeiBeidenEndfraktionen_VerteidigerKommen()
    {
        var u = Standard();
        var gate = G(u, 3);   // Beta (Union?) – Gamma (Gilde?)
        var me = u.Ships[u.PlayerShip];
        me.ModelKey = "bastion"; me.Slots = new(u.ShipClasses[u.ShipModels["bastion"].ClassKey].Slots); me.Fit = new(u.ShipModels["bastion"].Fit); me.RecomputeStats(u);
        Place(u, me, gate.Sector, gate.Position + new Vec2(30f, 0f));
        u.PlayerSector = gate.Sector;
        var cop = u.ShipsById().First(s => ThreatMap.IsPolice(s) && GateCombat.DefendersOf(u, gate).Contains(u.FactionOf(s)!.Value));
        Place(u, cop, gate.Sector, gate.Position + new Vec2(-40f, 20f)); cop.Command = null;
        var ev = Capture(u);
        float heading = SpaceSim.Core.Flight.AimAssist.Bearing(me.Position, gate.Position) * 180f / MathF.PI;
        GunShot shot = default;
        foreach (int slot in PlayerGunnery.WeaponSlots(u, me)) { shot = PlayerGunnery.TryFireAtGate(u, me, slot, gate.Id, heading); if (shot.Result == GunResult.Fired) break; }
        Assert.Equal(GunResult.Fired, shot.Result);
        var e = Assert.IsType<Encounter>(Siege.OfGate(u, gate.Id));
        Assert.Equal(gate.Id, e.Gate);
        Assert.Contains(ev, x => x is GateAttacked);
        foreach (var f in GateCombat.DefendersOf(u, gate)) Assert.True(Wanted.LevelOf(u, u.PlayerActor, f) >= 3, $"gesucht bei {f.Value}");
        Assert.True(e.Of(cop.Id) is { Side: 1 }, "Polizei verteidigt das Tor");
    }

    [Fact]
    public void Speichern_MittenInDerTorbelagerung_Bitgleich()
    {
        var u = Standard();
        Wars.Declare(u, Union, Guild);
        var gate = G(u, 3);
        var m = u.ShipModels["bastion"];
        var ship = ShipPurchase.Create(u, u.NewShipId(), "bastion", u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Warship, Controller.Ai, Union, u.StationsById().First(), 0f);
        u.AddShip(ship);
        Place(u, ship, gate.Sector, gate.Position + new Vec2(40f, 0f));
        GateCombat.Apply(u, gate, Hull(1000f), Union);
        Siege.Start(u, ship, new GateTarget(u, gate));
        var runner = new SimulationRunner();
        for (int i = 0; i < 40; i++) runner.Step(u);
        Assert.NotNull(Siege.OfGate(u, gate.Id));
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        Assert.Equal(gate.Id, copy.Fights.Encounters.Single(x => x.Gate.Value != 0).Gate);
        var r2 = new SimulationRunner();
        for (int i = 0; i < 400; i++) { runner.Step(u); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
    }

    [Fact]
    public void SchalterAus_UndOhneTorklassen_GleicherHash()
    {
        var a = TestWorlds.Default(fuel: false, research: false);
        var def = TestWorlds.Def(fuel: false, research: false);
        def.GateTypes.Clear();
        var b = WorldBuilder.Build(def);
        var ra = new SimulationRunner(); var rb = new SimulationRunner();
        for (int i = 0; i < 3000; i++) { ra.Step(a); rb.Step(b); }
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        // eingeschaltet, aber ohne Krieg und ohne Spielerangriff: dieselbe Welt
        var c = TestWorlds.Default(fuel: false, research: false);
        c.Rules.GateCombat = true;
        var rc = new SimulationRunner();
        for (int i = 0; i < 3000; i++) rc.Step(c);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(c));
    }

    [Fact]
    public void Langlauf_WiederholtAusgeschalteteTore_WirtschaftErholtSich()
    {
        var u = Standard();
        var runner = new SimulationRunner();
        var ring = new[] { 3, 9, 21, 25, 1 };
        int k = 0; long restored = 0;
        u.Events.Raised += e => { if (e is GateRestored) restored++; };
        for (int t = 1; t <= 40000; t++)
        {
            runner.Step(u);
            if (t % 6000 == 0 && GateCombat.DownCount(u) == 0) Down(u, G(u, ring[k++ % ring.Length]));
            if (t % 1000 == 0)
            {
                foreach (var s in u.Ships.Values)
                    if (s.Transit is { } tr)
                        foreach (var gid in u.Gates.Keys.Where(id => GateCombat.IsDown(u, u.Gates[id])))
                            Assert.False(tr.Legs.Zip(tr.Legs.Skip(1)).Any(p => p.First.Sector == u.Gates[gid].Sector && p.Second.Sector == u.Gates[u.Gates[gid].Target].Sector && p.First.EndTick > u.Tick),
                                $"Reise über ausgefallenes Tor (Schiff {s.Id.Value})");
            }
        }
        Assert.True(restored >= 5, $"Tore wieder in Betrieb: {restored}");
        foreach (var a in u.Actors.Values.Where(a => a.IsFaction(u)))
            Assert.DoesNotContain(FactionViability.Check(u, a.Id), i => i.Kind is ViabilityIssueKind.PoliceBelowMinimum or ViabilityIssueKind.NoSeat);
        var trade = ShipJobs.UsedUnits(u, ShipJobs.Default.First(j => j.Key == "trade"));
        Assert.True(trade >= 0.8f * u.Rules.MaxTraders, $"Handelskapazität {trade}");
    }

    [Fact]
    public void Stationsgeschuetze_GesuchtesMitgliedDerEigenenFraktionIstZiel()
    {
        var u = Standard();
        var me = u.Ships[u.PlayerShip];
        var hq = u.StationsById().First(s => StationCombat.DefenseOf(u, s) is { Guns.Count: > 0 } && u.FactionOf(s) == u.Player.Membership);
        var e = new Encounter { Cause = EncounterCause.Assault, Sector = hq.Sector };
        var p = new CombatParticipant { Ship = me.Id, Side = 0 };
        Assert.False(StationCombat.Hostile(u, hq, e, p, me));
        Wanted.Report(u, u.PlayerActor, u.FactionOf(hq)!.Value, "assault");
        Assert.True(StationCombat.Hostile(u, hq, e, p, me));
    }

    // ───────── Runde 14b: Schonfrist nach dem Wiederanlauf ─────────

    [Fact]
    public void Dauerbeschuss_SackgassentorBleibtNieDauerhaftUnten_SchonfristNachJedemWiederanlauf()
    {
        var u = Standard();
        foreach (var a in u.Actors.Values.Where(a => a.IsFaction(u))) a.Treasury.Credits = 0f;   // keine Reparatur: nur die Notregel
        var gate = G(u, 17);   // Gamma–Aurelia, kartenteilend
        var sys = new GateRepairSystem();
        var ev = Capture(u);
        long openSince = u.Tick; var openSpans = new List<long>(); long downTicks = 0, total = 0;
        bool wasDown = false;
        for (int t = 0; t < 30000; t++)
        {
            u.Tick++;
            // Dauerfeuer: jeder Tick ein großer Treffer, solange das Tor angreifbar ist
            if (!GateCombat.IsDown(u, gate)) GateCombat.Apply(u, gate, Hull(1000f, 500f), u.PlayerActor);
            if (u.Tick % GateRepairSystem.Interval == 0) sys.Step(u, GateRepairSystem.Interval);
            bool down = GateCombat.IsDown(u, gate);
            if (down) downTicks++;
            total++;
            if (down && !wasDown && t > 0) openSpans.Add(u.Tick - openSince);
            if (!down && wasDown) openSince = u.Tick;
            wasDown = down;
        }
        var restored = ev.OfType<GateRestored>().Count();
        Assert.True(restored >= 3, $"Wiederanläufe {restored}");
        // jede offene Phase nach einem Wiederanlauf dauert mindestens die Schonfrist (die erste Phase vor dem ersten Ausfall zählt nicht)
        Assert.All(openSpans.Skip(1), span => Assert.True(span >= u.Rules.GateSplitRestoreGraceTicks, $"offen nur {span} Ticks"));
        Assert.True(downTicks < total * 0.3, $"zu lange unten: {downTicks} von {total}");
    }

    [Fact]
    public void Schonfrist_NichtAngreifbar_SchildVoll_BelagerungEndet_Gespeichert()
    {
        var u = Standard();
        var gate = G(u, 3);
        var me = u.Ships[u.PlayerShip];
        Place(u, me, gate.Sector, gate.Position + new Vec2(30f, 0f));
        u.PlayerSector = gate.Sector;
        var e = Siege.Start(u, me, new GateTarget(u, gate));
        Down(u, gate);
        GateCombat.Restore(u, gate, "emergency");
        Assert.True(GateCombat.InGrace(u, gate));
        Assert.Equal(GateCombat.MaxShield(u, gate), GateCombat.Shield(u, gate));
        Assert.DoesNotContain(e.Participants, p => p.Active);   // die Belagerung endet sauber
        Assert.Contains("Notbetrieb", PlayerGunnery.GateIssue(u, me, gate));
        float hull = GateCombat.Hull(u, gate);
        GateCombat.Apply(u, gate, Hull(500f), u.PlayerActor);
        Assert.Equal(hull, GateCombat.Hull(u, gate));
        Assert.Null(Wars.PickGateTarget(u, Union, u.ShipsById().First(s => ThreatMap.IsPolice(s))));
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        Assert.True(GateCombat.InGrace(copy, copy.Gates[gate.Id]));
        // nach der Frist wieder angreifbar
        u.Tick += u.Rules.GateRestoreGraceTicks + 1;
        Assert.Null(PlayerGunnery.GateIssue(u, me, gate));
    }

    [Fact]
    public void Wiederanlauf_VonHandGeschlossenesTorBleibtGeschlossen()
    {
        var u = Standard();
        var gate = G(u, 9);
        GateAccess.SetState(u, gate, GateState.Closed);
        Down(u, gate);
        GateCombat.Restore(u, gate, "emergency");
        Assert.False(GateCombat.IsDown(u, gate));
        Assert.Equal(GateState.Closed, G(u, 9).State);
        Assert.Equal(GateState.Closed, G(u, 10).State);
        // ein offenes Tor öffnet wieder
        var ring = G(u, 3);
        Down(u, ring);
        GateCombat.Restore(u, ring, "repaired");
        Assert.True(MapParts.Passable(G(u, 3)));
    }
}
