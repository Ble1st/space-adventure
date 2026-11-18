using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 45 (docs/POLITIK.md „Runde 45“): Tribut (KI gegen KI und Forderung des Spielers), Bündnisse (Gründung, Beistandspakt Tiefe 1, Verrat,
/// Auflösung, Abkühlzeit, Spieler per Comms), eigene Sperren der Spielerfraktion über eine Sektorbehörde (dieselben Sperrregeln wie die KI),
/// Missbrauchsgrenzen, Determinismus und Speichern/Laden.
/// </summary>
public class Runde45Tests
{
    static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    static readonly FactionId Union = new(1), Guild = new(2), Mont = new(3);
    static readonly SectorId Nebelmark = new(9), Abgrund = new(11), Alpha = new(1);

    static Universe World(ulong seed = 1) => WorldBuilder.Build(TestWorlds.Def(fuel: false, research: false), seed);
    static FactionId Me(Universe u) => u.PlayerFaction ?? throw new Xunit.Sdk.XunitException("keine Spielerfraktion");

    static List<SimEvent> Record(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    static void SetRel(Universe u, FactionId a, FactionId b, float v)
    {
        if (u.IsPlayerFaction(a) || u.IsPlayerFaction(b))
        {
            var npc = u.IsPlayerFaction(a) ? b : a;
            Relations.Set(u, npc, Me(u), v); Relations.Set(u, Me(u), npc, v);
            return;
        }
        Relations.Set(u, a, b, v); Relations.Set(u, b, a, v);
    }

    /// <summary>KI-Krieg <paramref name="a"/> → <paramref name="b"/>, schon <paramref name="age"/> Ticks alt (Müdigkeit wird im nächsten WarSystem-Schritt daraus gerechnet).</summary>
    static War AgedWar(Universe u, FactionId a, FactionId b, long age)
    {
        var w = Wars.Declare(u, a, b);
        w.Since = u.Tick - age;
        w.Weariness = age / (float)u.Rules.WarWearyTicks;
        return w;
    }

    // ───────── Tribut ─────────

    [Fact]
    public void Tribut_KiGegenKi_KlarerSiegerNimmtTribut_GeldExaktErklaerbar()
    {
        var u = World();
        Assert.True(u.Rules.AiTributePeace);   // Standardwelt an
        u.Actors[Union].Treasury.Credits = 2_000_000f;   // Union klar stärker (Geld zählt zur Kriegsstärke)
        u.Actors[Guild].Treasury.Credits = 200_000f;
        var w = AgedWar(u, Guild, Union, 13_000);
        float union0 = u.Actors[Union].Treasury.Credits, guild0 = u.Actors[Guild].Treasury.Credits;
        var ev = Record(u);
        new WarSystem().Step(u, WarSystem.Interval);
        Assert.Empty(u.Wars);
        var tp = Assert.Single(ev.OfType<TributePeace>());
        Assert.Equal(Guild, tp.Payer); Assert.Equal(Union, tp.Winner);
        Assert.Contains(ev.OfType<PeaceConcluded>(), p => p.Reason == "tribute");
        // Doppelbuchung: Ausgabe des Zahlers = Einnahme des Siegers = Kassendifferenz
        float up = ev.OfType<TreasuryUpkeep>().Where(t => t.Purpose == "tribute" && t.Actor == (ActorId)Guild).Sum(t => t.Amount);
        float inc = ev.OfType<TreasuryIncome>().Where(t => t.Purpose == "tribute" && t.Actor == (ActorId)Union).Sum(t => t.Amount);
        Assert.Equal(tp.Amount, up, 2); Assert.Equal(tp.Amount, inc, 2);
        Assert.Equal(guild0 - tp.Amount, u.Actors[Guild].Treasury.Credits, 1);
        Assert.Equal(union0 + tp.Amount, u.Actors[Union].Treasury.Credits, 1);
        // nie aus der Rücklage: höchstens TributeMaxSpendableShare des verfügbaren Geldes
        Assert.True(u.Actors[Guild].Treasury.Credits >= FactionViability.Reserve(u, Guild));
        Assert.True(tp.Amount <= tp.Demanded + 0.01f);
        Assert.True(u.PeaceSince.ContainsKey((1, 2)));
    }

    [Theory]
    [InlineData("jung")]
    [InlineData("pleite")]
    [InlineData("aus")]
    [InlineData("gleichstark")]
    public void Tribut_KiGegenKi_KeinTributfrieden_KriegLaeuftWeiter(string fall)
    {
        var u = World();
        u.Actors[Union].Treasury.Credits = 2_000_000f;
        u.Actors[Guild].Treasury.Credits = 200_000f;
        long age = 13_000;
        if (fall == "jung") age = 2_000;
        if (fall == "pleite") u.Actors[Guild].Treasury.Credits = FactionViability.Reserve(u, Guild) + 2_000f;   // Budget unter 80 % der Forderung
        if (fall == "aus") u.Rules.AiTributePeace = false;
        if (fall == "gleichstark") u.Actors[Union].Treasury.Credits = u.Actors[Guild].Treasury.Credits;
        var w = AgedWar(u, Guild, Union, age);
        var ev = Record(u);
        new WarSystem().Step(u, WarSystem.Interval);
        Assert.Single(u.Wars);
        Assert.Empty(ev.OfType<TributePeace>());
        Assert.DoesNotContain(ev, e => e is TreasuryUpkeep { Purpose: "tribute" });
        if (fall == "pleite")
        {
            // abgelehnt → ein Angebot je Abkühlzeit, kein Flackern: im nächsten Takt keine neue Prüfung
            Assert.True(w.LastOfferTick > 0);
            Assert.True(Wars.OfferCooldownLeft(u, w) > 0);
        }
    }

    [Fact]
    public void Tribut_SpielerFordert_KasseUndFrieden_DritteGrollen_KeinGeldkreislauf()
    {
        var u = World();
        var me = Me(u);
        u.Player.Treasury.Credits = 2_000_000f;
        u.Actors[Guild].Treasury.Credits = 200_000f;
        Assert.Null(Wars.PlayerDeclareIssue(u, Guild));
        var w = Wars.PlayerDeclare(u, Guild);
        var actions = CommsActions.Default();
        // zu früh: Sperrgrund, nichts ändert sich, kein Ereignis
        var ev = Record(u);
        Assert.False(actions.Preview(u, $"demand:{Guild.Value}").Possible);
        Assert.False(actions.Execute(u, $"demand:{Guild.Value}").Success);
        Assert.Empty(ev);
        w.Since = u.Tick - 13_000; w.Weariness = 0.33f;
        float relUnion0 = Relations.Effective(u, Union, me);
        float wallet0 = u.Player.Treasury.Credits, guild0 = u.Actors[Guild].Treasury.Credits;
        var pre = actions.Preview(u, $"demand:{Guild.Value}");
        Assert.True(pre.Possible, pre.Reason);
        var res = actions.Execute(u, $"demand:{Guild.Value}");
        Assert.True(res.Success, res.Message);
        Assert.Empty(u.Wars);
        float paid = res.Credits;
        Assert.Equal(pre.Cost, paid, 1);   // Vorschau = Ausführung
        Assert.Equal(wallet0 + paid, u.Player.Treasury.Credits, 1);
        Assert.Equal(guild0 - paid, u.Actors[Guild].Treasury.Credits, 1);
        Assert.Equal(relUnion0 - u.Rules.PlayerDemandRelationLoss, Relations.Effective(u, Union, me), 3);
        Assert.Contains(ev.OfType<PeaceConcluded>(), p => p.Reason == "tribute");
        // Missbrauch: kein sofortiger neuer Krieg (Friedens-Abkühlzeit), also keine zweite Forderung
        Assert.NotNull(Wars.PlayerDeclareIssue(u, Guild));
        Assert.False(actions.Execute(u, $"demand:{Guild.Value}").Success);
    }

    [Fact]
    public void Tribut_ErstZahlenDannFordern_BringtNieMehrAlsOhneZahlung()
    {
        // „Kreislauf“: Tribut zahlen und danach fordern — der gezahlte Tribut mindert die Forderung (War.TributePaid), der Saldo bleibt ≤ Forderung − 2 × Zahlung
        float Saldo(float payFirst)
        {
            var u = World();
            u.Player.Treasury.Credits = 2_000_000f;
            u.Actors[Guild].Treasury.Credits = 200_000f;
            var w = Wars.PlayerDeclare(u, Guild);
            w.Since = u.Tick - 13_000; w.Weariness = 0.33f;
            float start = u.Player.Treasury.Credits;
            if (payFirst > 0f) Wars.PayTribute(u, Guild, payFirst, w);
            Assert.True(Wars.Demand(u, Guild) is not null);
            return u.Player.Treasury.Credits - start;
        }
        float plain = Saldo(0f), paid = Saldo(3000f);
        Assert.True(plain > 0f);
        Assert.True(paid <= plain - 2 * 3000f + 1f, $"ohne Zahlung {plain:0}, mit 3000 vorab {paid:0}");
    }

    [Fact]
    public void Tribut_SpielerNichtUeberlegen_Gesperrt()
    {
        var u = World();
        u.Player.Treasury.Credits = 20_000f;   // Kriegskasse, aber schwach
        u.Actors[Guild].Treasury.Credits = 2_000_000f;
        var w = Wars.PlayerDeclare(u, Guild);
        w.Since = u.Tick - 13_000; w.Weariness = 0.33f;
        Assert.Contains("überlegen", Wars.DemandIssue(u, Guild));
    }

    // ───────── Bündnisse ─────────

    [Fact]
    public void Buendnis_KiGruendung_NurMitBeziehungUndKooperation_HoechstensEinesJeTakt()
    {
        var u = World();
        Assert.True(u.Rules.AlliancesActive);
        new AllianceSystem().Step(u, AllianceSystem.Interval);
        Assert.Empty(u.Alliances);   // Startbeziehungen 0,1–0,3
        SetRel(u, Union, Mont, 0.7f);
        SetRel(u, Union, Guild, 0.7f);
        var ev = Record(u);
        new AllianceSystem().Step(u, AllianceSystem.Interval);
        var al = Assert.Single(u.Alliances);   // höchstens ein neues Bündnis je Takt (Paare in Id-Reihenfolge: 1–2 zuerst)
        Assert.Equal((Union, Guild), (al.A, al.B));
        Assert.Single(ev.OfType<AllianceFormed>());
        new AllianceSystem().Step(u, AllianceSystem.Interval);
        Assert.Equal(2, u.Alliances.Count);
        // Kooperation zu gering → kein Bündnis
        var v = World();
        v.Actors[Guild].Personality = v.Actors[Guild].Personality with { Cooperation = 0.4f };
        SetRel(v, Union, Guild, 0.7f);
        Assert.Contains("kooperativ", Alliances.FormIssue(v, Union, Guild));
        // Gesetzlose nie
        var outlaw = v.Factions.Keys.FirstOrDefault(f => v.Actors.TryGetValue(f, out var a) && a.Kind == ActorKind.Outlaw);
        if (outlaw.Value != 0) Assert.NotNull(Alliances.FormIssue(v, Union, outlaw));
    }

    [Fact]
    public void Buendnis_Beistandspakt_VerbuendeterTrittBei_NurTiefeEins()
    {
        var u = World();
        SetRel(u, Union, Mont, 0.7f);
        Alliances.Form(u, Union, Mont);
        var me = Me(u);
        SetRel(u, Guild, me, 0.7f);
        Alliances.Form(u, Guild, me);   // Spieler mit der Gilde verbündet
        var ev = Record(u);
        Wars.Declare(u, Guild, Union);   // Gilde greift Union an
        var join = Assert.Single(ev.OfType<AllyJoinedWar>());
        Assert.Equal((Mont, Union, Guild), (join.Ally, join.Defender, join.Attacker));
        var jw = Wars.Between(u, Mont, Guild)!;
        Assert.True(jw.Join && jw.A == Mont);
        // Tiefe 1: der Beitritt der Montanbund ruft die Verbündeten der Gilde (den Spieler) NICHT
        Assert.Empty(Alliances.Calls(u));
        Assert.Equal(2, u.Wars.Count);
        // Angriff ist offensiv — der Spieler als Verbündeter des Angreifers wird nicht gerufen
        Assert.False(Wars.AtWar(u, me, Union));
    }

    [Fact]
    public void Buendnis_Verrat_LoestAuf_VerrateneTreteDemOpferBei()
    {
        var u = World();
        SetRel(u, Union, Mont, 0.7f); SetRel(u, Guild, Mont, 0.7f); SetRel(u, Union, Guild, 0.7f);
        Alliances.Form(u, Union, Mont);
        Alliances.Form(u, Guild, Mont);
        Alliances.Form(u, Union, Guild);
        var ev = Record(u);
        Wars.Declare(u, Union, Guild);   // Union greift den eigenen Verbündeten an
        var reasons = ev.OfType<AllianceDissolved>().Select(d => (d.A.Value, d.B.Value, d.Reason)).ToList();
        Assert.Contains((1, 2, "betrayal"), reasons);
        Assert.Contains((1, 3, "betrayal_ally"), reasons);
        Assert.True(Relations.Effective(u, Mont, Union) < 0.7f - u.Rules.AllianceBetrayalLoss + 0.01f);
        Assert.True(Wars.AtWar(u, Mont, Union));   // Montanbund steht der verratenen Gilde bei
        Assert.True(Alliances.Allied(u, Guild, Mont));
    }

    [Fact]
    public void Buendnis_ZerbrichtUnterSchwelle_AbkuehlzeitVerhindertFlackern()
    {
        var u = World();
        SetRel(u, Union, Mont, 0.7f);
        Alliances.Form(u, Union, Mont);
        Relations.Set(u, Union, Mont, 0.3f);
        var sys = new AllianceSystem();
        sys.Step(u, AllianceSystem.Interval);
        Assert.Empty(u.Alliances);
        SetRel(u, Union, Mont, 0.7f);
        sys.Step(u, AllianceSystem.Interval);
        Assert.Empty(u.Alliances);   // Abkühlzeit
        Assert.Contains("erst wieder", Alliances.FormIssue(u, Union, Mont));
        // Langlauf: höchstens eine Gründung je Abkühlzeit, auch wenn die Beziehung um die Schwellen pendelt
        var v = World();
        int formed = 0;
        v.Events.Raised += e => { if (e is AllianceFormed) formed++; };
        var runner = new SimulationRunner();
        for (int i = 0; i < 40; i++)
        {
            SetRel(v, Union, Mont, i % 2 == 0 ? 0.7f : 0.3f);
            runner.Run(v, 1000);
        }
        Assert.True(formed <= 1 + 40_000 / v.Rules.AllianceCooldownTicks + 1, $"{formed} Gründungen");
    }

    [Fact]
    public void Buendnis_Spieler_VorschlagenVerlassen_PerComms_StrengeSchluessel()
    {
        var u = World();
        var me = Me(u);
        var actions = CommsActions.Default();
        var ev = Record(u);
        Assert.False(actions.Execute(u, $"alliance:propose:{Union.Value}").Success);   // Beziehung 0,1–0,2
        Assert.False(actions.Execute(u, "alliance:propose:x").Success);
        Assert.False(actions.Execute(u, "alliance:bogus:1").Success);
        Assert.Empty(ev);
        SetRel(u, Union, me, 0.7f);
        var pre = actions.Preview(u, $"alliance:propose:{Union.Value}");
        Assert.True(pre.Possible, pre.Reason);
        Assert.True(actions.Execute(u, $"alliance:propose:{Union.Value}").Success);
        Assert.True(Alliances.Allied(u, me, Union));
        // Bündnis wirkt an Kontrollpunkten: Union-Sektor mit Sperre 2 → Spieler frei statt Zoll
        var acc = new AccessContext(u, u.Gates.Values.First(), u.Sectors[Alpha], AccessDirection.Entry, 2, Union, new MobilityProfile(u.PlayerActor, true, 0f, false), null);
        Relations.Set(u, Union, me, 0.45f);   // unter AllyRelation, über der Auflösungsschwelle
        Assert.Equal(AccessPolicy.Allow, new PolicyCheck().Check(acc).Policy);
        Assert.Equal(0, FactionPolicySystem.LocksAgainst(u, Union, me));
        Assert.True(actions.Execute(u, $"alliance:leave:{Union.Value}").Success);
        Assert.False(Alliances.Allied(u, me, Union));
        Assert.Equal(AccessPolicy.Toll, new PolicyCheck().Check(acc).Policy);
        Assert.Contains("erst wieder", Alliances.ProposeIssue(u, Union));
    }

    [Fact]
    public void Buendnis_Beistandsruf_AnDenSpieler_OhneBeistandEndetEs_MitBeistandBleibtEs()
    {
        foreach (bool aid in new[] { false, true })
        {
            var u = World();
            var me = Me(u);
            new SimulationRunner().Run(u, 1);   // CommsSystem am Bus
            SetRel(u, Union, me, 0.7f);
            Alliances.Form(u, me, Union);
            Wars.Declare(u, Guild, Union);
            Assert.Single(Alliances.Calls(u));
            Assert.Contains(u.Inbox, m => m.Kind == "ally_call");
            var actions = CommsActions.Default();
            if (aid)
            {
                var r = actions.Execute(u, $"alliance:aid:{Guild.Value}");
                Assert.True(r.Success, r.Message);
                Assert.True(Wars.AtWar(u, me, Guild));
                Assert.True(Wars.Between(u, me, Guild)!.Join);
            }
            var runner = new SimulationRunner();
            runner.Run(u, u.Rules.AllianceCallTicks + 2 * AllianceSystem.Interval);
            Assert.Equal(aid, Alliances.Allied(u, me, Union));
            if (!aid) Assert.True(Relations.Effective(u, Union, me) < 0.7f - u.Rules.AllianceDeclineLoss + 0.01f);
        }
    }

    [Fact]
    public void Buendnis_KiVerbuendeterVerteidigtDenSpieler()
    {
        var u = World();
        var me = Me(u);
        SetRel(u, Mont, me, 0.7f);
        Alliances.Form(u, me, Mont);
        Wars.Declare(u, Union, me);   // KI-Krieg auf den Spieler (Regel AiWarOnPlayer ist für die Erklärung selbst zuständig)
        Assert.True(Wars.AtWar(u, Mont, Union));
        Assert.True(Wars.Between(u, Mont, Union)!.Join);
    }

    // ───────── Sektorbehörde und eigene Sperren ─────────

    /// <summary>Spieler reich (Firma) und das aktive Schiff an einen freien Platz in Nebelmark.</summary>
    static void ToNebelmark(Universe u)
    {
        u.Player.Treasury.Credits = 400_000f;
        var ship = u.Ships[u.PlayerShip];
        if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
        u.MoveShip(ship, Nebelmark);
        var sec = u.Sectors[Nebelmark];
        foreach (var cand in new[] { new Vec2(0, 0), new Vec2(200, 200), new Vec2(-200, 200), new Vec2(200, -200), new Vec2(-200, -200), new Vec2(400, 0), new Vec2(-400, 0) })
        {
            ship.Position = cand;
            if (PlayerSeats.FoundIssue(u, Nebelmark) is null) return;
        }
        throw new Xunit.Sdk.XunitException("kein freier Platz in Nebelmark: " + PlayerSeats.FoundIssue(u, Nebelmark));
    }

    [Fact]
    public void Sektorbehoerde_NurHerrenlosOhneVersteck_AbFirma()
    {
        var u = World();
        Assert.True(u.Rules.PlayerSeats);
        u.Player.Treasury.Credits = 400_000f;
        var ship = u.Ships[u.PlayerShip];
        if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
        u.MoveShip(ship, Alpha);
        Assert.Contains("herrenlos", PlayerSeats.FoundIssue(u, Alpha));
        u.MoveShip(ship, Abgrund);
        Assert.Contains("Piratenversteck", PlayerSeats.FoundIssue(u, Abgrund));
        Assert.Contains("aktives Schiff", PlayerSeats.FoundIssue(u, Nebelmark));
        ToNebelmark(u);
        u.Player.Treasury.Credits = 5_000f;
        Assert.NotNull(PlayerSeats.FoundIssue(u, Nebelmark));   // klein und arm
    }

    [Fact]
    public void Sektorbehoerde_GibtBesitz_SperreNachKiRegeln_ZollUndUnterhaltAusDerKasse()
    {
        var u = World();
        var me = Me(u);
        ToNebelmark(u);
        var actions = CommsActions.Default();
        float wallet0 = u.Player.Treasury.Credits;
        var res = actions.Execute(u, $"seat:found:{Nebelmark.Value}");
        Assert.True(res.Success, res.Message);
        var sec = u.Sectors[Nebelmark];
        Assert.Equal(me, sec.Owner);
        Assert.Equal(ControlStage.Dominance, sec.Control);
        Assert.Equal(2, SectorControl.MaxLevel(u, sec));
        Assert.True(u.Player.Treasury.Credits < wallet0);
        Assert.Contains(sec.Id, u.Factions[me].Territory);
        Assert.True(SpaceSim.Core.Missions.MissionRules.Forbidden(u, u.PlayerActor));   // keine Aufträge an sich selbst an der eigenen Behörde
        // Stufe 3 übersteigt die Kontrollstufe
        Assert.False(actions.Execute(u, $"lock:{Nebelmark.Value}:3").Success);
        Assert.False(actions.Execute(u, $"lock:{Nebelmark.Value}:9").Success);
        Assert.False(actions.Execute(u, $"lock:{Alpha.Value}:1").Success);   // nicht dein Sektor
        Assert.True(actions.Execute(u, $"lock:{Nebelmark.Value}:2").Success);
        Assert.Equal(2, sec.Access.EntryLevel);
        Assert.False(actions.Execute(u, $"lock:{Nebelmark.Value}:1").Success);   // Mindestdauer
        // die KI-Politik überschreibt die Spielerstufe nicht
        var policy = new FactionPolicySystem();
        for (int i = 0; i < 10; i++) { u.Tick += 20; policy.Step(u, 20); }
        Assert.Equal(2, sec.Access.EntryLevel);
        // Zoll an der Einreise nach Nebelmark (Tor 19 Alpha → 20 Nebelmark) in die Spielerkasse
        var trader = u.ShipsById().First(s => s.IsEconomyAi && s.Owner == u.FreeActor);
        var pass = GateAccess.Evaluate(u, u.Gates[new GateId(19)], GateAccess.ProfileOf(u, trader));
        Assert.Equal(AccessPolicy.Toll, pass.Policy);
        Assert.Equal(me, pass.TollTo);
        Assert.True(pass.Toll > 0f);
        // Durchsetzung aus der Spielerkasse; Groll der KI-Fraktionen wie bei KI-Sperren
        float rel0 = Relations.Effective(u, Union, me);
        float w1 = u.Player.Treasury.Credits;
        var ev = Record(u);
        u.Tick = (u.Tick / u.Rules.RelationStepTicks + 1) * u.Rules.RelationStepTicks;
        policy.Step(u, 20);
        Assert.True(u.Player.Treasury.Credits < w1);
        Assert.Contains(ev.OfType<TreasuryUpkeep>(), t => t.Purpose == "lock" && t.Actor == u.PlayerActor && t.Amount > 0f);
        Assert.True(Relations.Effective(u, Union, me) < rel0);
        // Ablauf nach LockMaxTicks, dann Abkühlzeit
        u.Tick = sec.Access.ExpiresTick;
        policy.Step(u, 20);
        Assert.Equal(0, sec.Access.EntryLevel);
        Assert.Contains("Abkühlzeit", PlayerSeats.LockIssue(u, Nebelmark, 1));
        // Schließen: kein Besitz an Stufe mehr möglich (Kontrollstufe weg)
        var seat = PlayerSeats.Seats(u).Single();
        Assert.True(actions.Execute(u, $"seat:close:{seat.Id.Value}").Success);
        Assert.False(u.Stations.ContainsKey(seat.Id));
        Assert.Equal(0, SectorControl.MaxLevel(u, sec));
    }

    [Fact]
    public void Sektorbehoerde_AufhebenStartetAbkuehlzeit_KeinUmgehenDesAblaufs()
    {
        var u = World();
        ToNebelmark(u);
        Assert.True(CommsActions.Default().Execute(u, $"seat:found:{Nebelmark.Value}").Success);
        Assert.True(PlayerSeats.SetLock(u, Nebelmark, 1) == 1);
        u.Tick += u.Rules.LockMinTicks;
        Assert.Null(PlayerSeats.LockIssue(u, Nebelmark, 0));
        PlayerSeats.SetLock(u, Nebelmark, 0);
        Assert.NotNull(PlayerSeats.LockIssue(u, Nebelmark, 1));   // sofort neu sperren (frischer Ablauf) geht nicht
    }

    // ───────── Standardwelt, Determinismus, Speichern ─────────

    [Fact]
    public void Standardwelt_OhneBuendnisOhneSpielerhandlung_HashGleichMitUndOhneRunde45()
    {
        var on = World(3);
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.AlliancesActive = false; def.Rules.AiTributePeace = false; def.Rules.PlayerSeats = false;
        var off = WorldBuilder.Build(def, 3);
        new SimulationRunner().Run(on, 6000);
        new SimulationRunner().Run(off, 6000);
        Assert.Empty(on.Alliances);
        Assert.Equal(UniverseHash.Compute(off), UniverseHash.Compute(on));
    }

    [Fact]
    public void HashOhneSpielerpolitik_IgnoriertSpielerbuendnis()
    {
        var u = World();
        ulong before = UniverseHash.Compute(u, true, includePlayerPolitics: false);
        SetRel(u, Union, Me(u), 0.7f);
        ulong relOnly = UniverseHash.Compute(u, true, includePlayerPolitics: false);
        Alliances.Form(u, Me(u), Union);
        Assert.Equal(before, relOnly);
        Assert.Equal(relOnly, UniverseHash.Compute(u, true, includePlayerPolitics: false));
        Assert.NotEqual(UniverseHash.Compute(u), before);
    }

    static Universe Scenario(ulong seed)
    {
        var u = World(seed);
        SetRel(u, Union, Mont, 0.7f);
        Alliances.Form(u, Union, Mont);
        u.Actors[Union].Treasury.Credits = 1_500_000f;
        var runner = new SimulationRunner();
        runner.Run(u, 200);
        Wars.Declare(u, Guild, Union);
        ToNebelmark(u);
        Assert.True(CommsActions.Default().Execute(u, $"seat:found:{Nebelmark.Value}").Success);
        PlayerSeats.SetLock(u, Nebelmark, 2);
        runner.Run(u, 3000);
        return u;
    }

    [Fact]
    public void Determinismus_UndSpeichernLaden_Bitgleich_MitBuendnisKriegTributUndSperre()
    {
        var a = Scenario(7); var b = Scenario(7);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        Assert.NotEmpty(a.Alliances);
        Assert.Contains(a.Wars, w => w.Join);
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(a, Now));
        Assert.Equal(a.Alliances.Select(x => (x.A, x.B, x.Since)), copy.Alliances.Select(x => (x.A, x.B, x.Since)));
        Assert.Equal(a.Wars.Select(w => w.Join), copy.Wars.Select(w => w.Join));
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(copy));
        var r1 = new SimulationRunner(); var r2 = new SimulationRunner();
        for (int i = 0; i < 1500; i++) { r1.Step(a); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(copy));
        // Bündnisende (Abkühlzeit) ebenfalls gespeichert
        Alliances.Dissolve(a, a.Alliances[0], "relation");
        var (copy2, _) = SaveGameJson.Read(SaveGameJson.Write(a, Now));
        Assert.Equal(a.AllianceEnded.OrderBy(kv => kv.Key), copy2.AllianceEnded.OrderBy(kv => kv.Key));
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(copy2));
    }

    [Fact]
    public void Langlauf_KiKriegMitBuendnis_EndetDurchTributOderMuedigkeit_FraktionenHandlungsfaehig()
    {
        var u = World(5);
        SetRel(u, Union, Mont, 0.7f);
        Alliances.Form(u, Union, Mont);
        var runner = new SimulationRunner();
        runner.Run(u, 500);
        Wars.Declare(u, Guild, Union);
        Assert.Equal(2, u.Wars.Count);
        var peace = new List<PeaceConcluded>();
        u.Events.Raised += e => { if (e is PeaceConcluded p) peace.Add(p); };
        runner.Run(u, 45_000);
        Assert.Empty(u.Wars);
        Assert.True(peace.Count >= 2);
        foreach (var f in u.NpcFactionIds())
            if (Wars.CanWage(u, f))
                Assert.DoesNotContain(FactionViability.Check(u, (ActorId)f), i => i.Kind is ViabilityIssueKind.PoliceBelowMinimum or ViabilityIssueKind.NoSeat);
    }
}
