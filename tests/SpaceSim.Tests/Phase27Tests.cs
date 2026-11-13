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
/// Phase 27 (KAMPF.md Stufe 27): Flottenkampf — Polizei und Piraten über die Befehls-Ausführer, Verbände der Fraktionen und Rudel der Piraten, Verstärkung
/// laufender Gefechte, Fokusfeuer, Geleit, Verfolgung in der Zuständigkeit, Flucht zu Stationen und durch Tore, Polizeigefecht gegen gesuchte Spieler, Ergeben.
/// </summary>
public class Phase27Tests
{
    static readonly SectorId Alpha = new(1);

    static Universe Standard(ulong? seed = null) => TestWorlds.Default(seed, fuel: false, research: false);

    /// <summary>Dreht Phase 27 in einer Weltdefinition zurück (Schalter aus).</summary>
    internal static void RevertPhase27(WorldDef def)
    {
        def.Rules!.FleetCombat = false;
        // Gerätetest (Phase 27): die Weltdatei setzte repairPlatesPerHull noch auf 0,05 — die Phase-26-Korrektur (0,02) wirkte nur im Code-Standard.
        // Ausgeliefert war 0,05; für den Vergleich mit Phase 26 gilt dieser Wert.
        def.Rules.RepairPlatesPerHull = 0.05f;
    }

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    // ───────── Schalter: Phase-26-Verhalten bleibt reproduzierbar ─────────

    [Fact]
    public void SchalterAus_StandardweltLaeuftBitIdentischZuPhase26()
    {
        // Balance-Tool, Standard-Seed, 20000 Ticks auf dem Stand Phase 26 (nur mit der Hash-Erweiterung um Actor.CreatedTick): 8BAFCA8A395897AE
        var def = TestWorlds.Def(fuel: false, research: false);
        RevertPhase27(def);
        Phase28Tests.RevertPhase28(def);   // Phase 28 ebenfalls zurückdrehen (Raketen, Stationsverteidigung, Krieg)
        var u = WorldBuilder.Build(def);
        new SimulationRunner().Run(u, 20000);
        // Phase 29: der Hash mischt zusätzlich die Akteurskürzel (Actor.Tag) — geprüft: ohne diese Mischung bleibt der bisherige Wert; kein Verhalten geändert (vorher 8BAFCA8A395897AE)
        Assert.Equal(0x02F599DEDC0101B4UL, UniverseHash.Compute(u));   // Runde 11: nur die Namensliste (Kürzel befördeter Akteure) geändert — mit der alten Liste geprüft: 0x885FAB4DD2BB8F0F
        // Runde 12: ohne die Kürzel (Namensliste) gilt weiter der Wert vor Phase 29 — die geänderte Konstante kommt nur von den Kürzeln
        Assert.Equal(0x8BAFCA8A395897AEUL, UniverseHash.Compute(u, includeTags: false));
    }

    // ───────── Hilfen ─────────

    static readonly SectorId Nebelmark = new(9);   // herrenlos
    static readonly FactionId Union = new(1);

    static void Place(Universe u, Ship s, SectorId sector, Vec2 pos)
    {
        if (s.Transit is not null) u.FinishTransit(s);
        s.Order = null; s.Phase = TradePhase.Idle; s.MinePhase = MinePhase.Idle;
        if (s.Sector != sector) u.MoveShip(s, sector);
        s.Position = pos;
        s.DockedAt = default;
    }

    static Ship NewShip(Universe u, string model, ShipRole role, ActorId owner, float credits = 2000f, Controller controller = Controller.Ai)
    {
        var m = u.ShipModels[model];
        var cls = u.ShipClasses[m.ClassKey];
        var yard = u.StationsById().First(s => !s.IsBuildSite);
        var ship = ShipPurchase.Create(u, u.NewShipId(), model, cls.Slots, m.Fit, role, controller, owner, yard, credits);
        u.AddShip(ship);
        return ship;
    }

    static List<Ship> Raiders(Universe u) => u.ShipsById().Where(ThreatMap.IsRaider).ToList();
    static Ship Police(Universe u, int i = 0) => SecuritySystem.PoliceOf(u, Union).OrderBy(s => s.Id.Value).Skip(i).First();

    /// <summary>Nur Gefecht und Schiffssysteme laufen (Rest der Welt steht).</summary>
    static void Fight(Universe u, int ticks, Func<bool>? stop = null)
    {
        var combat = new CombatSystem();
        var systems = new ShipSystemsSystem();
        for (int i = 0; i < ticks; i++)
        {
            u.Tick++;
            combat.Step(u, 1);
            systems.Step(u, 1);
            if (stop?.Invoke() == true) return;
        }
    }

    /// <summary>Ein Punkt im Sektor fern von Stationen und Toren (≥ <paramref name="clear"/>).</summary>
    static Vec2 Far(Universe u, SectorId sector, float clear = 120f)
    {
        var sec = u.Sectors[sector];
        for (int r = 0; r < 400; r += 10)
            for (int k = 0; k < 16; k++)
            {
                var c = Vec2.FromAngle(k * MathF.PI / 8f) * r;
                bool ok = sec.Stations.All(id => Vec2.Distance(u.Stations[id].Position, c) > clear) && sec.Gates.All(g => Vec2.Distance(u.Gates[g].Position, c) > clear);
                if (ok) return c;
            }
        return new Vec2(0f, 0f);
    }

    // ───────── Polizei und Piraten über Befehle ─────────

    [Fact]
    public void PolizeiUndPiraten_LaufenUeberBefehle_KeineSonderlogik()
    {
        var u = Standard();
        bool lurked = false;
        var runner = new SimulationRunner();
        for (int i = 0; i < 3000; i++) { runner.Step(u); lurked |= u.Ships.Values.Any(s => ThreatMap.IsRaider(s) && s.Command is LurkCommand); }
        Assert.True(lurked, "kein Kaperschiff lauerte über einen Befehl");
        var police = u.ShipsById().Where(ThreatMap.IsPolice).ToList();
        Assert.NotEmpty(police);
        // jede freie Streife führt einen Befehl (Streife im Einsatzsektor, Heimkehr, Geleit, Verfolgung) oder folgt ihrem Verbandsanführer
        foreach (var p in police.Where(p => !u.Fights.IsEngaged(p.Id) && !p.Emergency))
        {
            Assert.True(p.Command is PatrolCommand or ReturnCommand or EscortCommand or PursueCommand or FollowCommand || p.DutySector == default,
                $"Polizei {p.Id.Value} ohne Befehl ({p.Command?.Kind ?? "-"})");
            if (p.Command is PatrolCommand pc) Assert.Equal(p.DutySector, Assert.Single(pc.Sectors));
        }
        Assert.Contains(police, p => p.Command is PatrolCommand);
        // Kaperschiffe: Lauern, Heimkehr oder dem Rudel folgen — oder Rast im Versteck ohne Befehl
        foreach (var r in Raiders(u).Where(r => !u.Fights.IsEngaged(r.Id)))
            Assert.True(r.Command is LurkCommand or ReturnCommand or FollowCommand || PirateSystem.AtHome(u, r), $"Kaperer {r.Id.Value} ({r.Command?.Kind ?? "-"})");
    }

    [Fact]
    public void Piraten_RudelAlsVerband_AnfuehrerLauert_MitgliedFolgt_UndAllesHeim()
    {
        var u = Standard();
        var actor = u.Actors.Values.First(a => a.Kind == ActorKind.Outlaw && Raiders(u).Count(r => r.Owner == a.Id) >= 2);
        var pack = Raiders(u).Where(r => r.Owner == actor.Id).Take(2).ToList();
        var home = PirateSystem.Home(u, pack[0])!;
        foreach (var r in pack) { Place(u, r, home.Sector, home.DockPoint(r.Id)); r.NextThinkTick = 0; }
        var ev = Capture(u);
        PirateCommands.Step(u);
        var sq = Squadrons.Get(u, pack[0]);
        Assert.NotNull(sq);
        Assert.Contains(pack[1].Id, sq!.Members);
        Assert.Equal(sq.Owner, actor.Id);
        Assert.IsType<LurkCommand>(u.Ships[sq.Leader].Command);
        Assert.Equal(sq.Leader, Assert.IsType<FollowCommand>(u.Ships[sq.Members[1]].Command).Target);
        Assert.Contains(ev.OfType<ForceSquadron>(), f => f.Kind == "pack" && f.Formed && f.Members == 2);
        // Überfallchance des Rudels: je Mitglied im Sektor (gleiche Rate je Kaperschiff)
        Assert.Equal(2, PirateCommands.PackIn(u, pack[0], home.Sector));
    }

    [Fact]
    public void Polizei_StreifenImSelbenSektor_BildenVerband_UndPatrouillierenGemeinsam()
    {
        var u = Standard();
        var a = Police(u, 0); var b = Police(u, 1);
        a.DutySector = Alpha; b.DutySector = Alpha;
        var ev = Capture(u);
        PoliceCommands.Organize(u, Union, SecuritySystem.PoliceOf(u, Union));
        var sq = Squadrons.Get(u, a);
        Assert.NotNull(sq);
        Assert.Equal(sq, Squadrons.Get(u, b));
        Assert.Equal((ActorId)Union, sq!.Owner);
        var leader = u.Ships[sq.Leader];
        Assert.Equal(Alpha, Assert.Single(Assert.IsType<PatrolCommand>(leader.Command).Sectors));
        Assert.IsType<FollowCommand>(u.Ships[sq.Members.First(m => m != sq.Leader)].Command);
        Assert.Contains(ev.OfType<ForceSquadron>(), f => f.Kind == "patrol" && f.Formed);
        // anderer Einsatz → verlässt den Verband, der Verband löst sich auf
        u.Ships[sq.Members[1]].DutySector = u.Sectors.Keys.First(k => k != Alpha && u.Sectors[k].Owner == Union);
        PoliceCommands.Organize(u, Union, SecuritySystem.PoliceOf(u, Union));
        Assert.False(u.Squadrons.ContainsKey(sq.Id));
    }

    [Fact]
    public void Polizei_HeimkehrBefehl_VersorgungAusDerKasseAnDerBasis()
    {
        var u = Standard();
        var p = Police(u);
        var home = u.Stations[p.HomeBase];
        p.Hull = p.MaxHull * 0.4f;   // unter RetreatHull
        u.Actors[Union].Treasury.Credits = 50_000f;
        PoliceCommands.Step(u, p);
        Assert.IsType<ReturnCommand>(p.Command);
        var runner = new SimulationRunner();
        for (int i = 0; i < 3000 && p.Hull < p.MaxHull - 0.5f; i++) runner.Step(u);
        Assert.True(p.Hull >= p.MaxHull - 0.5f, "an der Basis aus der Kasse repariert");
        Assert.True(u.Actors[Union].Treasury.Credits < 50_000f + u.Rules.PoliceReserve);
    }

    // ───────── Gemeinsame Gefechte ─────────

    [Fact]
    public void Rudel_FaelltGemeinsamUeberDasOpferHer_FokusfeuerDerPolizei()
    {
        var u = Standard();
        var actor = u.Actors.Values.First(a => a.Kind == ActorKind.Outlaw && Raiders(u).Count(r => r.Owner == a.Id) >= 2);
        var pack = Raiders(u).Where(r => r.Owner == actor.Id).Take(2).ToList();
        var pos = Far(u, Nebelmark);
        foreach (var r in pack) Place(u, r, Nebelmark, pos);
        var sq = Squadrons.Create(u, pack[0], "Rudel", out _)!;
        Squadrons.Join(u, sq, pack[1], out _);
        var victim = NewShip(u, "unit_miner_100", ShipRole.Miner, u.FreeActor);
        Place(u, victim, Nebelmark, pos + new Vec2(20f, 0f));
        victim.Cargo[u.FindGood("iron_ore")!.Id] = 40f;
        var ev = Capture(u);
        var e = Engage.StartRaid(u, pack[0], victim, u.Sectors[Nebelmark]);
        Assert.Equal(2, e.Participants.Count(p => p.Side == 0));
        Assert.Contains(ev.OfType<CombatJoined>(), j => j.Ship == pack[1].Id && j.Reason == "pack" && j.Side == 0);
        // Fokusfeuer: zwei Kaperer, einer angeschlagen — die Streife nimmt den schwächsten in Reichweite
        var police = Police(u);
        Place(u, police, Nebelmark, pos + new Vec2(-10f, 0f));
        var pp = Reinforcements.JoinSide(u, e, police, 1, "police", e.Of(victim.Id));
        pack[1].Hull = 5f;
        foreach (var q in e.Participants) q.X = 0f;
        var d = new PoliceTactic().Decide(u, e, pp, police);
        Assert.Equal(pack[1].Id, d.Target);
        // ohne Flottenkampf: der nächste bzw. bisherige Gegner (Phase 24)
        u.Rules.FleetCombat = false;
        pp.Target = default;
        Assert.Equal(e.Participants.First(p => p.Side == 0).Ship, new PoliceTactic().Decide(u, e, pp, police).Target);
    }

    [Fact]
    public void Geleit_EskorteKommtNachUndGreiftDenAngreiferDesSchuetzlingsAn()
    {
        var u = Standard();
        var raider = Raiders(u)[0];
        var pos = Far(u, Nebelmark);
        Place(u, raider, Nebelmark, pos);
        var ward = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        Place(u, ward, Nebelmark, pos + new Vec2(25f, 0f));
        ward.Cargo[u.FindGood("electronics")!.Id] = 30f;
        var escort = Police(u);
        Place(u, escort, Alpha, u.Stations[escort.HomeBase].Position);   // noch woanders
        escort.Command = new EscortCommand(ward.Id);
        var e = Engage.StartRaid(u, raider, ward, u.Sectors[Nebelmark]);
        Assert.Null(e.Of(escort.Id));
        // die Eskorte erreicht den Sektor: beim nächsten Verstärkungstakt tritt sie bei und nimmt den Angreifer ihres Schützlings ins Ziel
        Place(u, escort, Nebelmark, pos + new Vec2(40f, 0f));
        var ev = Capture(u);
        e.Of(ward.Id)!.Courage = 0f;
        Fight(u, u.Rules.ReinforceTicks + 1, () => e.Of(escort.Id) is not null);
        var ep = e.Of(escort.Id);
        Assert.NotNull(ep);
        Assert.Equal(1, ep!.Side);
        Assert.Contains(ev.OfType<CombatJoined>(), j => j.Ship == escort.Id && j.Reason == "escort");
        e.Of(raider.Id)!.Target = ward.Id;
        Assert.Equal(raider.Id, new EscortTactic().Decide(u, e, ep, escort).Target);
    }

    // ───────── Zuflucht ─────────

    [Fact]
    public void Flucht_ZurStation_DocktAn_UndDurchsTor_SpringtInDenNachbarsektor()
    {
        // Station: Händler in Alpha nahe einer Station, Kaperer auf der anderen Seite
        var u = Standard();
        var st = u.Stations.Values.First(s => s.Sector == Alpha && !s.IsBuildSite && !StationProtection.IsHideout(u, s));
        var raider = Raiders(u)[0];
        var trader = NewShip(u, "unit_miner_100", ShipRole.Miner, u.FreeActor);
        var dir = Vec2.FromAngle(0.7f);
        Place(u, trader, Alpha, st.Position + dir * 38f);   // knapp außerhalb des Stationsschutzes, innerhalb der Zufluchtsweite (40)
        Place(u, raider, Alpha, st.Position + dir * 68f);
        trader.Cargo[u.FindGood("iron_ore")!.Id] = 20f;
        var e = Engage.StartRaid(u, raider, trader, u.Sectors[Alpha]);
        var tp = e.Of(trader.Id)!;
        Assert.Equal(st.Id.Value, tp.RefugeId);
        Assert.InRange(tp.Refuge, 30f, u.Rules.CombatRefugeMaxDistance);
        tp.Courage = 0f;
        var ev = Capture(u);
        Fight(u, 400, () => !tp.Active);
        Assert.Equal(CombatExit.Fled, tp.Exit);
        Assert.Contains(ev.OfType<RefugeReached>(), r => r.Ship == trader.Id && r.Kind == "station");
        Assert.Equal(st.Id, trader.DockedAt);
        // Tor: Kaperer flieht durch das Tor in den Nachbarsektor
        var u2 = Standard();
        var gate = u2.Gates.Values.Where(g => g.Sector == Alpha).OrderBy(g => g.Id.Value).First();
        var far = u2.Gates[gate.Target];
        var r2 = Raiders(u2)[0];
        var police = Police(u2);
        // Runde 12: der Wächter hat einen dritten Laser (Turm) — der Kaperer soll hier fliehen, nicht vorher fallen: Turmlaser für den Test ausbauen
        for (int i = 7; i < police.Fit.Count; i++) police.Fit[i] = null;
        police.RecomputeStats(u2);
        var gdir = gate.Position.Normalized();
        Place(u2, r2, Alpha, gate.Position - gdir * 30f);
        Place(u2, police, Alpha, gate.Position - gdir * 70f);
        var e2 = Engage.StartIntercept(u2, police, r2);
        var rp = e2.Of(r2.Id)!;
        Assert.Equal(-gate.Id.Value, rp.RefugeId);
        r2.Hull = r2.MaxHull * 0.2f;   // flieht
        var ev2 = Capture(u2);
        Fight(u2, 400, () => !rp.Active);
        Assert.Equal(CombatExit.Fled, rp.Exit);
        Assert.Contains(ev2.OfType<RefugeReached>(), x => x.Ship == r2.Id && x.Kind == "gate" && x.Sector == far.Sector);
        Assert.Equal(far.Sector, r2.Sector);
    }

    [Fact]
    public void Zuflucht_VersperrtWennDerGegnerNaeherSteht_UndOhneSchalterKeine()
    {
        var u = Standard();
        var st = u.Stations.Values.First(s => s.Sector == Alpha && !s.IsBuildSite && !StationProtection.IsHideout(u, s));
        var raider = Raiders(u)[0];
        var trader = NewShip(u, "unit_miner_100", ShipRole.Miner, u.FreeActor);
        var dir = Vec2.FromAngle(0.7f);
        Place(u, trader, Alpha, st.Position + dir * 38f);
        Place(u, raider, Alpha, st.Position + dir * 30f);   // zwischen Opfer und Station
        trader.Cargo[u.FindGood("iron_ore")!.Id] = 20f;
        var e = Engage.StartRaid(u, raider, trader, u.Sectors[Alpha]);
        Assert.NotEqual(st.Id.Value, e.Of(trader.Id)!.RefugeId);
        var u2 = Standard();
        u2.Rules.FleetCombat = false;
        var t2 = NewShip(u2, "unit_miner_100", ShipRole.Miner, u2.FreeActor);
        Place(u2, t2, Alpha, st.Position + dir * 38f);
        var r2 = Raiders(u2)[0];
        Place(u2, r2, Alpha, st.Position + dir * 80f);
        t2.Cargo[u2.FindGood("iron_ore")!.Id] = 20f;
        var e2 = Engage.StartRaid(u2, r2, t2, u2.Sectors[Alpha]);
        Assert.All(e2.Participants, p => Assert.Equal(0, p.RefugeId));
    }

    // ───────── Verfolgung ─────────

    [Fact]
    public void Verfolgung_NachFlucht_InDerZustaendigkeit_ImSelbenSektorGestellt_AnDerGrenzeAbgebrochen()
    {
        var u = Standard();
        var police = Police(u);
        var raider = Raiders(u)[0];
        var pos = Far(u, Alpha);
        Place(u, police, Alpha, pos);
        Place(u, raider, Alpha, pos + new Vec2(30f, 0f));
        var e = Engage.StartIntercept(u, police, raider);
        var rp = e.Of(raider.Id)!;
        rp.RefugeId = 0;
        // Kaperer flieht (außer Reichweite) — danach verfolgt die Streife
        Engage.Leave(u, e, rp, CombatExit.Fled);
        var ev = Capture(u);
        CombatSystem.Finish(u, e, "fled");
        var cmd = Assert.IsType<PursueCommand>(police.Command);
        Assert.Equal(raider.Id, cmd.Target);
        Assert.Contains(ev.OfType<PursuitStarted>(), x => x.Hunter == police.Id && x.Target == raider.Id);
        // das Ziel liegt im selben Sektor still: der Vorsprung schrumpft mit dem Tempo des Verfolgers, dann Gefecht
        if (raider.Command is not null) Commands.Cancel(u, raider);
        Place(u, raider, Alpha, raider.Position);
        var orders = new OrderSystem();
        for (int i = 0; i < 200 && !u.Fights.IsEngaged(police.Id); i++) { u.Tick++; orders.Step(u, 1); }
        Assert.True(u.Fights.IsEngaged(police.Id));
        Assert.Equal(EncounterCause.Intercept, u.Fights.Of(police.Id)!.Cause);
        Assert.Contains(ev.OfType<PursuitEnded>(), x => x.Hunter == police.Id && x.How == "caught");
        // Grenze: Ziel in einem herrenlosen Sektor — die Verfolgung endet
        var u2 = Standard();
        var p2 = Police(u2);
        var r2 = Raiders(u2)[0];
        Place(u2, p2, Alpha, Far(u2, Alpha));
        Place(u2, r2, Nebelmark, Far(u2, Nebelmark));
        Commands.Give(u2, p2, new PursueCommand(r2.Id, 40f, u2.Tick + 1000), out _, ignoreCapacity: true);
        var ev2 = Capture(u2);
        u2.Tick++; new OrderSystem().Step(u2, 1);
        Assert.Contains(ev2.OfType<PursuitEnded>(), x => x.How == "border");
        Assert.Null(p2.Command);
        Assert.False(Jurisdiction.Covers(u2, Union, Nebelmark));
        Assert.True(Jurisdiction.Covers(u2, Union, Alpha));
    }

    // ───────── Spieler: Polizeigefecht und Ergeben ─────────

    static (Universe u, Ship me, Ship police) WantedPlayer()
    {
        var u = Standard();
        var me = u.Ships[u.PlayerShip];
        var police = Police(u);
        var pos = Far(u, Alpha);
        Place(u, me, Alpha, pos);
        u.PlayerSector = Alpha;
        Place(u, police, Alpha, pos + new Vec2(40f, 0f));
        if (police.Command is not null) Commands.Cancel(u, police);
        Wanted.Report(u, u.PlayerActor, Union, "assault");
        Assert.True(Wanted.LevelOf(u, u.PlayerActor, Union) >= 3);
        u.PlayerWallet.Credits = 50_000f;
        return (u, me, police);
    }

    [Fact]
    public void GesuchterSpieler_PolizeiGreiftNachDerWarnungAn_ErgebenZahltDieStrafe()
    {
        var (u, me, police) = WantedPlayer();
        var ev = Capture(u);
        var sec = new SecuritySystem();
        u.Tick++; sec.Step(u, 1);
        Assert.Contains(ev, e => e is EncounterWarning { Kind: EncounterKind.Intercept });
        for (int i = 0; i < u.Rules.PlayerWarningTicks + 2; i++) { u.Tick++; sec.Step(u, 1); }
        var e = u.Fights.Of(me.Id);
        Assert.NotNull(e);
        Assert.Equal(EncounterCause.Arrest, e!.Cause);
        Assert.Contains(e.Participants, p => p.Ship == police.Id && p.Side == 0 && p.Tactic == CombatTactics.Police);
        Assert.Empty(ev.OfType<PlayerIntercepted>());   // kein abstraktes Abfangen mehr
        // ergeben: Strafe bezahlt, Gefecht vorbei, Fahndung gelöscht
        Assert.Null(PlayerSurrender.Issue(u));
        float fine = Wanted.FineOf(u, u.Player.Wanted[Union]);
        var res = PlayerSurrender.Surrender(u);
        Assert.NotNull(res);
        Assert.True(res!.ToPolice);
        Assert.False(u.Fights.IsEngaged(me.Id));
        Assert.False(u.Player.Wanted.ContainsKey(Union));
        Assert.Equal(50_000f - fine, u.PlayerWallet.Credits, 1);
        Assert.Contains(ev, x => x is EncounterEnded { Cause: EncounterCause.Arrest });
        Assert.NotNull(PlayerSurrender.Issue(u));   // nichts mehr zu ergeben
    }

    [Fact]
    public void GesuchterSpieler_ErgibtSichSchonInDerWarnung_OfflineBleibtEsAbstrakt()
    {
        var (u, me, _) = WantedPlayer();
        var sec = new SecuritySystem();
        u.Tick++; sec.Step(u, 1);
        Assert.Single(u.PendingEncounters);
        var res = PlayerSurrender.Surrender(u);
        Assert.True(res!.ToPolice);
        Assert.Empty(u.PendingEncounters);
        Assert.False(u.Player.Wanted.ContainsKey(Union));
        // offline (Spielerüberfälle aus) bleibt es beim abstrakten Abfangen
        var (u2, me2, _) = WantedPlayer();
        u2.Rules.PlayerRaids = false;
        var ev = Capture(u2);
        var sec2 = new SecuritySystem();
        for (int i = 0; i < u2.Rules.PlayerWarningTicks + 3; i++) { u2.Tick++; sec2.Step(u2, 1); }
        Assert.Single(ev.OfType<PlayerIntercepted>());
        Assert.False(u2.Fights.IsEngaged(me2.Id));
    }

    [Fact]
    public void Ueberfall_SpielerErgibtSich_FrachtAbgeworfen_PiratenDrehenAb()
    {
        // in der Warnung
        var u = Standard();
        var me = u.Ships[u.PlayerShip];
        var pos = Far(u, Nebelmark);
        Place(u, me, Nebelmark, pos);
        u.PlayerSector = Nebelmark;
        var food = u.FindGood("food")!.Id;
        me.Cargo[food] = 40f;
        var raider = Raiders(u)[0];
        Place(u, raider, Nebelmark, pos + new Vec2(50f, 0f));
        var pending = new PendingEncounter { Kind = EncounterKind.Raid, Source = raider.Id, Target = me.Id, Sector = Nebelmark, DueTick = u.Tick + 60 };
        u.PendingEncounters.Add(pending);
        float cash = u.Actors[raider.Owner].Treasury.Credits;
        var res = PlayerSurrender.Surrender(u);
        Assert.False(res!.ToPolice);
        Assert.Equal(20f, me.Cargo[food], 2);
        Assert.Empty(u.PendingEncounters);
        Assert.True(u.Actors[raider.Owner].Treasury.Credits >= cash);
        Assert.True(raider.Command is ReturnCommand || PirateSystem.AtHome(u, raider));
        // im Gefecht
        var u2 = Standard();
        var me2 = u2.Ships[u2.PlayerShip];
        var pos2 = Far(u2, Nebelmark);
        Place(u2, me2, Nebelmark, pos2);
        u2.PlayerSector = Nebelmark;
        me2.Cargo[food] = 40f;
        var r2 = Raiders(u2)[0];
        Place(u2, r2, Nebelmark, pos2 + new Vec2(30f, 0f));
        var e = Engage.StartRaid(u2, r2, me2, u2.Sectors[Nebelmark]);
        Assert.Null(PlayerSurrender.Issue(u2));
        var ev = Capture(u2);
        var res2 = PlayerSurrender.Surrender(u2);
        Assert.False(res2!.ToPolice);
        Assert.False(u2.Fights.IsEngaged(me2.Id));
        Assert.Equal(20f, me2.Cargo[food], 2);
        Assert.Contains(ev.OfType<CargoSurrendered>(), c => c.Ship == me2.Id);
        // ohne Fracht kein Ergeben gegenüber Kaperern; ohne Flottenkampf nie
        var u3 = Standard();
        u3.Rules.FleetCombat = false;
        Assert.NotNull(PlayerSurrender.Issue(u3));
    }

    // ───────── Persistenz und Hash ─────────

    [Fact]
    public void Speichern_MitBefehlenVerbaendenUndZuflucht_BitIdentisch()
    {
        var u = Standard();
        new SimulationRunner().Run(u, 2500);
        // ein laufendes Gefecht mit Zuflucht und eine Verfolgung erzwingen
        var raider = Raiders(u).First(r => !u.Fights.IsEngaged(r.Id));
        var trader = NewShip(u, "unit_miner_100", ShipRole.Miner, u.FreeActor);
        var st = u.Stations.Values.First(s => s.Sector == Alpha && !s.IsBuildSite && !StationProtection.IsHideout(u, s));
        // Phase 28: Händler in Zufluchtsweite der Station (≤ 40) — vorher hing die Vorbedingung „eine Zuflucht“ am zufälligen Eingreifen der Polizei
        Place(u, trader, Alpha, st.Position + new Vec2(36f, 0f));
        Place(u, raider, Alpha, st.Position + new Vec2(80f, 0f));
        trader.Cargo[u.FindGood("iron_ore")!.Id] = 20f;
        if (raider.Command is not null) Commands.Cancel(u, raider);
        Engage.StartRaid(u, raider, trader, u.Sectors[Alpha]);
        var hunter = u.ShipsById().First(p => ThreatMap.IsPolice(p) && !u.Fights.IsEngaged(p.Id));
        var other = Raiders(u).FirstOrDefault(r => r.Id != raider.Id && !u.Fights.IsEngaged(r.Id));
        if (other is not null) Commands.Give(u, hunter, new PursueCommand(other.Id, 40f, u.Tick + 800), out _, ignoreCapacity: true);
        Assert.Contains(u.Fights.Encounters.SelectMany(e => e.Participants), p => p.RefugeId != 0);
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        new SimulationRunner().Run(u, 1500);
        new SimulationRunner().Run(loaded, 1500);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Reparaturpreis_Standardwelt_ZweiHundertstelPlattenJePunkt()
    {
        // Gerätetest: Werft Delta verlangte 8 Cr je Hüllenpunkt (0,05 Platten aus der Weltdatei), gewollt war seit der Phase-26-Korrektur ≈ 3 Cr
        var u = Standard();
        Assert.Equal(0.02f, u.Rules.RepairPlatesPerHull, 4);
        new SimulationRunner().Run(u, 1);
        var yard = u.Stations.Values.First(s => s.Name == "Werft Delta");
        var plates = yard.Market[u.FindGood("hull_plates")!.Id];
        Assert.Equal(0.02f * plates.Price * SpaceSim.Core.Services.RepairService.LaborFactor, SpaceSim.Core.Services.RepairService.PricePerHull(u, yard), 2);
        Assert.InRange(SpaceSim.Core.Services.RepairService.PricePerHull(u, yard), 1f, 5f);
    }

    [Fact]
    public void Abdocken_FreieAbflugrichtung_AnJederStation()
    {
        // Gerätetest: Hülle 60 → 36 direkt nach dem Abdocken am Wasserwerk Alpha bei Vollschub (radialer Abflug auf ein Hindernis)
        var u = Standard();
        int turned = 0;
        foreach (var st in u.Stations.Values.Where(s => !s.IsBuildSite))
        {
            var obstacles = u.GetObstacles(st.Sector);
            foreach (var id in new[] { u.PlayerShip, new ShipId(1), new ShipId(77), new ShipId(123) })
            {
                var spot = st.DockPoint(id);
                var dir = SpaceSim.Core.Flight.UndockPath.Heading(obstacles, st.Position, spot);
                Assert.True(SpaceSim.Core.Flight.UndockPath.Clear(obstacles, spot, dir, SpaceSim.Core.Flight.UndockPath.ClearLength, Collision.ShipRadius + 2f),
                    $"{st.Name}: keine freie Abflugrichtung für Schiff {id.Value}");
                if (!SpaceSim.Core.Flight.UndockPath.Clear(obstacles, spot, (spot - st.Position).Normalized(), SpaceSim.Core.Flight.UndockPath.ClearLength, Collision.ShipRadius + 2f)) turned++;
            }
        }
        Assert.True(turned > 0, "kein radialer Abflug war versperrt — der Fall des Gerätetests wäre nicht abgedeckt");
    }

    [Fact]
    public void Hash_MischtGruendungszeitDerAkteure()
    {
        var u = Standard();
        ulong before = UniverseHash.Compute(u);
        u.Actors.Values.First().CreatedTick += 1;
        Assert.NotEqual(before, UniverseHash.Compute(u));
    }

    [Fact]
    public void Deterministisch_GleicherSeedGleicherHash()
    {
        var a = Standard(7); var b = Standard(7);
        new SimulationRunner().Run(a, 6000);
        new SimulationRunner().Run(b, 6000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }

    // ───────── Langlauf ─────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Langlauf_40000Ticks_VerlustquoteKeineGeisterBefehleHandlungsfaehig(bool classic)
    {
        var u = classic ? TestWorlds.Classic() : Standard();
        float loot = 0f, revenue = 0f;
        int joined = 0, refuge = 0;
        var removedNotDeparted = new List<ShipId>();
        var departed = new HashSet<ShipId>();
        u.Events.Raised += e =>
        {
            switch (e)
            {
                case RaidResolved rr: loot += rr.LootValue; break;
                case TradeCompleted tc: revenue += tc.Revenue; break;
                case MineDelivered md: revenue += md.Revenue; break;
                case CombatJoined: joined++; break;
                case RefugeReached: refuge++; break;
                case ShipDeparted sd: departed.Add(sd.Ship); break;
                case ShipRemoved sr when !departed.Contains(sr.Ship): removedNotDeparted.Add(sr.Ship); break;
            }
        };
        var runner = new SimulationRunner();
        for (int t = 0; t < 40000; t += 1000)
        {
            runner.Run(u, 1000);
            foreach (var s in u.Ships.Values)
            {
                if (u.Fights.IsEngaged(s.Id)) { Assert.Null(s.Transit); continue; }
                // Verfolgung nur mit Frist (läuft nie unbegrenzt) und nur durch Polizei
                if (s.Command is PursueCommand pc) { Assert.True(pc.Until <= u.Tick + u.Rules.PursuitTicks); Assert.True(ThreatMap.IsPolice(s)); }
                if (s.Command is LurkCommand) Assert.True(ThreatMap.IsRaider(s));
                if (s.Squadron.Value != 0) Assert.True(u.Squadrons.ContainsKey(s.Squadron), $"Schiff {s.Id.Value} in gelöschtem Verband");
            }
            foreach (var sq in u.Squadrons.Values) Assert.All(sq.Members, m => Assert.Equal(sq.Id, u.Ships[m].Squadron));
        }
        Assert.True(revenue > 0f);
        Assert.True(loot / revenue < 0.01f, $"Verlustquote {100f * loot / revenue:0.00} %");
        Assert.True(refuge > 0, "niemand ist zu einer Zuflucht geflohen");
        Assert.Empty(removedNotDeparted);
        foreach (var f in u.Factions.Keys)
        {
            Assert.True(SecuritySystem.PoliceOf(u, f).Count >= u.Rules.PoliceMinPerFaction || SecuritySystem.Territory(u, f).Count == 0, $"Fraktion {f.Value} unter Mindestpolizei");
        }
        Assert.Contains(u.Ships.Values, ThreatMap.IsRaider);
    }
}
