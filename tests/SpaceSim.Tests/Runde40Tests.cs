using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 40: Comms (Posteingang + Diplomatie). Posteingang aus Ereignissen (<see cref="CommsSystem"/>), Aktionsregistry
/// <see cref="CommsActions"/> (fine/war/ceasefire/peace/tribute; strenge Schlüssel, Fehlschlag ändert nichts), Waffenstillstand/Frieden
/// mit deterministischer KI-Entscheidung (<see cref="Wars.CeasefireIssue"/>/<see cref="Wars.PeaceIssue"/>/<see cref="Wars.TributeFor"/>),
/// Tribut in die Fraktionskasse, Kriegswirkungen (Handel/Andocken/Dienste verweigert, Angriffe des Spielers kein Verbrechen,
/// <see cref="EncounterCause.War"/> gegen Spielerschiffe, Waffenstillstand blockiert Angriffe), Persistenz (Inbox gespeichert, nicht
/// gehasht) und der Beweis, dass die NPC-Welt ohne Spielerkrieg unverändert bleibt (AiWarOnPlayer an in der Standardwelt).
/// </summary>
public class Runde40Tests
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    static readonly FactionId Union = new(1);
    static readonly FactionId Guild = new(2);

    static Universe World(ulong seed = 1)
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        return WorldBuilder.Build(def, seed);
    }

    static FactionId Me(Universe u) => u.PlayerFaction ?? throw new Xunit.Sdk.XunitException("keine Spielerfraktion");

    /// <summary>Ein Tick laufen lassen, damit das CommsSystem am Ereignisbus hängt (im Spiel läuft der Runner jeden Tick).</summary>
    static void Wire(Universe u) => new SimulationRunner().Run(u, 1);

    /// <summary>Spieler erklärt Union den Krieg (Kasse gestellt); liefert den Krieg.</summary>
    static War DeclareWar(Universe u, float credits = 50000f)
    {
        u.Player.Treasury.Credits = credits;
        Assert.Null(Wars.PlayerDeclareIssue(u, Union));
        return Wars.PlayerDeclare(u, Union);
    }

    static Station UnionStation(Universe u) => u.StationsById().First(s => !s.IsBuildSite && u.FactionOf(s) == Union);

    // ───────── Posteingang ─────────

    [Fact]
    public void Posteingang_NachrichtenAusEreignissen_OhneRng()
    {
        var u = World();
        var runner = new SimulationRunner();
        runner.Run(u, 25);   // CommsSystem hängt sich nach dem ersten Schritt an den Bus
        var me = Me(u);
        Assert.True(u.Rules.CommsActive);
        ulong rngBefore = u.Rng.State;
        u.Events.Publish(new WarDeclared(Union, me, -0.8f, "Terranische Union erklärt Du den Krieg."));
        u.Actors[u.PlayerActor].Wanted[Union] = new WantedRecord { Heat = 3f, Fine = 1200f, LastCrimeTick = u.Tick, LastCrime = "assault" };
        u.Events.Publish(new WantedChanged(u.PlayerActor, Union, 2, 3));
        u.Events.Publish(new BountyEarned(u.PlayerActor, Union, new ShipId(5), 450f));
        u.Events.Publish(new PeaceConcluded(Union, me, "weary", 1000, "Frieden."));
        u.Events.Publish(new CeasefireOffered(Guild, me, "Freie Händler bieten Waffenstillstand an."));
        Assert.Equal(rngBefore, u.Rng.State);   // keine Zufallszahl verbraucht
        Assert.Equal(5, u.Inbox.Count);
        Assert.Equal(5, Comms.Unread(u));
        var kinds = u.Inbox.Select(m => m.Kind).ToList();
        Assert.Equal(new[] { "war_declared", "wanted_up", "bounty_earned", "peace_concluded", "ceasefire_offer" }, kinds);
        var offer = u.Inbox.First(m => m.Kind == "ceasefire_offer");
        Assert.Equal(Guild.Value.ToString(), offer.Args["faction"]);
        Assert.Equal(new[] { "ceasefire:2" }, CommsText.Answers(u, offer));
        var (subj, body) = CommsText.Describe(u, u.Inbox.First(m => m.Kind == "wanted_up"));
        Assert.Contains("Fahndung", subj);
        Assert.Contains("gesucht", body);   // Stufe 3
        Comms.MarkRead(u, offer.Id);
        Assert.Equal(4, Comms.Unread(u));
        // Nachrichten anderer Fraktionen (KI–KI) landen nicht im Posteingang des Spielers
        u.Events.Publish(new WarDeclared(Union, Guild, -0.9f, "KI-Krieg."));
        Assert.Equal(5, u.Inbox.Count);
    }

    [Fact]
    public void Posteingang_Obergrenze40_AeltesteGeleseneZuerst_Ablauf()
    {
        var u = World();
        for (int i = 0; i < 45; i++) Comms.Add(u, Union, "war_declared", null, ttl: 0);
        Assert.Equal(40, u.Inbox.Count);   // Deckel 40
        Assert.Equal(45, u.Inbox.Max(m => m.Id));
        // gelesene fallen zuerst weg
        Comms.MarkRead(u, u.Inbox[0].Id);
        Comms.Add(u, Union, "peace_concluded");
        Assert.Equal(40, u.Inbox.Count);
        Assert.DoesNotContain(u.Inbox, m => m.Id == 6);   // die gelesene (Id 6) ist weg, nicht die älteste ungelesene
        // Verfall
        var m = Comms.Add(u, Union, "ceasefire_offer", null, ttl: 100);
        u.Tick += 101;
        Comms.Prune(u);
        Assert.DoesNotContain(u.Inbox, x => x.Id == m.Id);
    }

    [Fact]
    public void Posteingang_KiAngebotUeberWarSystem_ImLauf()
    {
        var u = World();
        var me = Me(u);
        u.Player.Treasury.Credits = 50000f;
        Relations.Set(u, Union, u.PlayerActor, -0.9f);
        u.Actors[Union].Treasury.Credits = 500000f;
        Assert.True(u.Rules.AiWarOnPlayer);   // Standardwelt: an seit Runde 40
        u.Tick = 20000;
        var runner = new SimulationRunner();
        runner.Run(u, 200);   // WarSystem erklärt (Intervall 100), CommsSystem hängte sich vorher an den Bus
        var w = Wars.Between(u, Union, me);
        Assert.NotNull(w);
        w!.Since = u.Tick - 21000;   // Müdigkeit ≈ 0,525: ≥ 0,5 (Angebot), < 0,6 (noch kein Frieden) — WarSystem rechnet sie je Schritt neu
        runner.Run(u, 100);   // Angebot ab Müdigkeit 0,5
        Assert.Contains(u.Inbox, m => m.Kind == "ceasefire_offer" && m.Args["faction"] == "1");
        // höchstens ein Angebot je 2400 Ticks: direkt danach keines neues
        int offers = u.Inbox.Count(m => m.Kind == "ceasefire_offer");
        runner.Run(u, 100);
        Assert.Equal(offers, u.Inbox.Count(m => m.Kind == "ceasefire_offer"));
    }

    // ───────── Strenge Schlüssel / Fehlschlag ändert nichts ─────────

    [Fact]
    public void Aktionen_StrengeSchluessel_FehlschlagAendertNichtsUndPubliziertNichts()
    {
        var u = World();
        var actions = CommsActions.Default();
        // Runde 41: mission_abandon dazu; Runde 45: Tribut fordern, Bündnis, Sektorbehörde, eigene Sperre hinten angehängt (Registrierungsreihenfolge)
        Assert.Equal(new[] { "fine", "war", "ceasefire", "peace", "tribute", "mission", "mission_abandon", "contract", "demand", "alliance", "seat", "lock" }, actions.Keys);
        int evts = 0;
        u.Events.Raised += _ => evts++;
        float wallet = u.Player.Treasury.Credits;
        var bad = new[] { "foo:1", "fine:x", "fine:", "war:", "war:0", "ceasefire:2", "peace:2", "tribute:1:0", "tribute:1:abc", "tribute:1", "mission:accept:1", "contract:haggle:3", "" };
        foreach (var offer in bad)
        {
            var r = actions.Execute(u, offer);
            Assert.False(r.Success, offer);
            Assert.Equal(0.0, (double)r.Credits);
        }
        Assert.Empty(u.Wars);
        Assert.Equal(wallet, u.Player.Treasury.Credits);
        Assert.Equal(0, evts);
        Assert.Empty(u.Inbox);
        // Vorschau ebenso rein
        var p = actions.Preview(u, "war:1");
        Assert.False(p.Possible);
        Assert.Contains("Kriegskasse", p.Reason);
        Assert.Equal(0, evts);
        Assert.Empty(u.Wars);
    }

    // ───────── Strafe (fine) ─────────

    [Fact]
    public void Strafe_Erfolg_ZehnProzentAufschlagAnDieFraktionskasse()
    {
        var u = World();
        var actions = CommsActions.Default();
        Wanted.Report(u, u.PlayerActor, Union, "assault", extraFine: 100f);
        var w = u.Actors[u.PlayerActor].Wanted[Union];
        float fineOf = Wanted.FineOf(u, w);
        float cost = MathF.Ceiling(fineOf * 1.10f);   // Fernzahlungsaufschlag +10 %
        u.Player.Treasury.Credits = cost + 500f;
        float wallet = u.Player.Treasury.Credits, treasury = u.Actors[Union].Treasury.Credits;
        var events = new List<SimEvent>();
        u.Events.Raised += e => events.Add(e);
        var r = actions.Execute(u, "fine:1");
        Assert.True(r.Success, r.Message);
        Assert.Equal((double)-cost, (double)r.Credits, 3);
        Assert.Equal((double)(wallet - cost), (double)u.Player.Treasury.Credits, 3);
        Assert.Equal((double)(treasury + cost), (double)u.Actors[Union].Treasury.Credits, 3);   // an die betroffene Fraktion
        Assert.False(u.Actors[u.PlayerActor].Wanted.ContainsKey(Union));     // Eintrag gelöscht
        Assert.Contains(events, e => e is FinePaid fp && fp.Amount == cost);
        Assert.Contains(events, e => e is TreasuryIncome ti && ti.Purpose == "fine" && ti.Amount == cost);
        Assert.Contains(events, e => e is CommsActionPerformed ca && ca.Action == "fine" && ca.Faction == 1);
        // nur an die betroffene Fraktion: kein Eintrag bei der Gilde entstanden
        Assert.False(u.Actors[u.PlayerActor].Wanted.ContainsKey(Guild));
        // erneute Zahlung ohne offene Strafe schlägt fehl
        Assert.False(actions.Execute(u, "fine:1").Success);
    }

    [Fact]
    public void Strafe_Sperrgruende_KeineZustandsaenderung()
    {
        var u = World();
        var actions = CommsActions.Default();
        // keine Strafe offen
        Assert.False(actions.Execute(u, "fine:1").Success);
        Wanted.Report(u, u.PlayerActor, Union, "assault");
        var w = u.Actors[u.PlayerActor].Wanted[Union];
        float cost = FineAction.CostOf(u, w);
        // Kasse reicht nicht
        u.Player.Treasury.Credits = cost - 1f;
        int evts = 0; u.Events.Raised += _ => evts++;
        float treasury = u.Actors[Union].Treasury.Credits;
        Assert.False(actions.Execute(u, "fine:1").Success);
        Assert.Equal(0, evts);
        Assert.Equal(treasury, u.Actors[Union].Treasury.Credits);
        Assert.True(u.Actors[u.PlayerActor].Wanted.ContainsKey(Union));
        // Festnahmewarnung läuft (PendingEncounter Intercept mit Quelle der Union)
        var police = SecuritySystem.PoliceOf(u, Union).First();
        u.PendingEncounters.Add(new PendingEncounter { Kind = EncounterKind.Intercept, Source = police.Id, Target = u.PlayerShip, Sector = u.PlayerSector, DueTick = u.Tick + 60 });
        u.Player.Treasury.Credits = cost + 100f;
        var r = actions.Execute(u, "fine:1");
        Assert.False(r.Success);
        Assert.Contains("Festnahmewarnung", r.Message);
        Assert.Equal(0, evts);
        Assert.True(u.Actors[u.PlayerActor].Wanted.ContainsKey(Union));
        Assert.Equal(treasury, u.Actors[Union].Treasury.Credits);
        Assert.Equal(cost + 100f, u.Player.Treasury.Credits);
    }

    // ───────── Krieg erklären (war) ─────────

    [Fact]
    public void Krieg_Erklaeren_KasseBedingungNichtVerbraucht_Abkuehlzeit_Beziehungsverlust()
    {
        var u = World();
        Wire(u);
        var actions = CommsActions.Default();
        var me = Me(u);
        // Kriegskasse als Bedingung (15 000 Cr), wird nicht verbraucht
        u.Player.Treasury.Credits = 14999f;
        Assert.Contains("Kriegskasse", Wars.PlayerDeclareIssue(u, Union));
        Assert.False(actions.Execute(u, "war:1").Success);
        u.Player.Treasury.Credits = 15000f;
        Assert.Null(Wars.PlayerDeclareIssue(u, Union));
        float relGuildBefore = Relations.Effective(u, Guild, u.PlayerActor);
        var r = actions.Execute(u, "war:1");
        Assert.True(r.Success, r.Message);
        Assert.True(Wars.AtWar(u, me, Union));
        Assert.Equal(15000.0, (double)u.Player.Treasury.Credits);   // Bedingung, kein Preis
        Assert.Equal(-0.8, (double)Relations.Effective(u, me, Union), 3);
        Assert.Equal((double)(relGuildBefore - u.Rules.PlayerWarRelationLoss), (double)Relations.Effective(u, Guild, u.PlayerActor), 3);
        Assert.Contains(u.Inbox, m => m.Kind == "war_declared");
        // schon im Krieg
        var r2 = actions.Execute(u, "war:1");
        Assert.False(r2.Success);
        Assert.Contains("Schon im Krieg", r2.Message);
        // nicht kriegsfähig (id ohne Fraktion)
        Assert.False(actions.Execute(u, "war:99").Success);
        // Abkühlzeit nach Frieden
        Wars.MakePeace(u, Wars.Between(u, me, Union)!, "manual");
        Assert.False(Wars.AtWar(u, me, Union));
        Assert.Contains("zu frisch", Wars.PlayerDeclareIssue(u, Union));
        Assert.False(actions.Execute(u, "war:1").Success);
    }

    [Fact]
    public void Krieg_HandelDiensteAndockenVerweigert_AngriffeKeinVerbrechen()
    {
        var u = World();
        var ship = u.Ships[u.PlayerShip];
        var st = UnionStation(u);
        new SimulationRunner().Run(u, 1);   // Preise gesetzt
        Assert.Null(TradeActions.Refusal(u, ship, st));
        Assert.Null(Docking.Issue(u, st, ship));
        Assert.NotEmpty(StationServices.Default().ForStation(u, st, ship));
        DeclareWar(u);
        Assert.Contains("Krieg", TradeActions.Refusal(u, ship, st));
        Assert.Contains("Krieg", Docking.Issue(u, st, ship));
        Assert.Empty(StationServices.Default().ForStation(u, st, ship));
        // Angriff des Spielers auf ein Kriegsgegner-Schiff: kein Verbrechen
        var enemy = SecuritySystem.PoliceOf(u, Union).First();
        enemy.DockedAt = default;
        enemy.Transit = null;
        enemy.Position = ship.Position + new Vec2(60f, 0f);
        u.MoveShip(enemy, u.PlayerSector);
        var events = new List<SimEvent>();
        u.Events.Raised += e => events.Add(e);
        var (e, _, _) = PlayerCombat.Engage(u, ship, enemy);
        Assert.NotNull(e);
        Assert.Contains(events, x => x is PlayerAttacked pa && !pa.Crime);
        Assert.False(u.Actors[u.PlayerActor].Wanted.ContainsKey(Union));
        // Vergleich ohne Krieg: derselbe Angriff ist ein Verbrechen
        var u2 = World();
        var ship2 = u2.Ships[u2.PlayerShip];
        var enemy2 = SecuritySystem.PoliceOf(u2, Union).First();
        enemy2.DockedAt = default; enemy2.Transit = null;
        enemy2.Position = ship2.Position + new Vec2(60f, 0f);
        u2.MoveShip(enemy2, u2.PlayerSector);
        PlayerCombat.Engage(u2, ship2, enemy2);
        Assert.True(u2.Actors[u2.PlayerActor].Wanted.ContainsKey(Union));
    }

    // ───────── Waffenstillstand / Frieden / Tribut ─────────

    [Fact]
    public void Waffenstillstand_FreiAbMuedigkeit_BlockiertAngriffe_Cooldown()
    {
        var u = World();
        var actions = CommsActions.Default();
        var me = Me(u);
        var w = DeclareWar(u);
        w.Weariness = 0.5f;   // ≥ CeasefireWeariness 0,4 → kostenlose Annahme
        Assert.Null(Wars.CeasefireIssue(u, Union));
        var r = actions.Execute(u, "ceasefire:1");
        Assert.True(r.Success, r.Message);
        Assert.Equal(0.0, (double)r.Credits, 3);   // kein Tribut
        Assert.Equal(u.Tick + u.Rules.CeasefireTicks, w.CeasefireUntil);
        Assert.Equal(Math.Max(1L, u.Tick), w.LastOfferTick);   // MarkOffered hebt Tick 0 auf 1 (Sentinel)
        Assert.True(Wars.CeasefireActive(u, w));
        Assert.False(Wars.Hostile(u, me, Union));   // Waffenstillstand blockiert Angriffe
        Assert.False(Wars.Hostile(u, u.PlayerActor, (ActorId)Union));
        // Stationsgeschütze: Kriegsgegner nicht feindlich während des Waffenstillstands
        var st = UnionStation(u);
        var meShip = u.Ships[u.PlayerShip];
        var enc = SpaceSim.Core.Combat.Engage.Begin(u, EncounterCause.War, u.PlayerSector);
        var ep = SpaceSim.Core.Combat.Engage.Join(u, enc, meShip, 0, CombatTactics.Player, 10f);
        Assert.False(StationCombat.Hostile(u, st, enc, ep, meShip));   // Waffenstillstand: kein Angriff auf den Kriegsgegner
        Assert.True(Wars.AtWar(u, me, Union));   // Krieg läuft weiter (Handel bleibt gesperrt)
        Assert.Contains("Krieg", TradeActions.Refusal(u, u.Ships[u.PlayerShip], st));
        // Sperrgründe: aktiver Waffenstillstand, dann Cooldown
        Assert.Contains("läuft schon", Wars.CeasefireIssue(u, Union));
        Assert.False(actions.Execute(u, "ceasefire:1").Success);
        w.CeasefireUntil = 0;
        Assert.True(StationCombat.Hostile(u, st, enc, ep, meShip));    // ohne Waffenstillstand wieder feindlich
        Assert.Contains("Angebot erst wieder", Wars.CeasefireIssue(u, Union));
        Assert.False(actions.Execute(u, "ceasefire:1").Success);
        // nach Ablauf lebt der Krieg wieder
        u.Tick = w.CeasefireUntil == 0 ? u.Tick : Math.Max(u.Tick, u.Tick + u.Rules.OfferCooldownTicks + 1);
        u.Tick += u.Rules.OfferCooldownTicks + 1;
        Assert.True(Wars.Hostile(u, me, Union));
        // kein Krieg → Sperrgrund
        var u2 = World();
        Assert.Contains("Kein Krieg", Wars.CeasefireIssue(u2, Union));
        Assert.False(CommsActions.Default().Execute(u2, "ceasefire:1").Success);
    }

    static Vec2 ship_pos(Universe u) => u.Ships[u.PlayerShip].Position;

    [Fact]
    public void Waffenstillstand_TributVariante_TributFliesstInFraktionskasse()
    {
        var u = World();
        var actions = CommsActions.Default();
        var w = DeclareWar(u);
        w.Weariness = 0.05f;   // unter Schwelle; KI nicht klar unterlegen (Polizei + Kasse)
        Assert.False(Wars.CeasefireFree(u, w, Union));
        float tribute = Wars.TributeFor(u, w, Union);
        Assert.Equal((double)MathF.Max(0f, u.Rules.TributeBase * 0.95f), (double)tribute, 1);
        u.Player.Treasury.Credits = 50000f;
        float wallet = u.Player.Treasury.Credits, treasury = u.Actors[Union].Treasury.Credits;
        var r = actions.Execute(u, "ceasefire:1");
        Assert.True(r.Success, r.Message);
        Assert.Equal((double)-tribute, (double)r.Credits, 1);
        Assert.Equal((double)(wallet - tribute), (double)u.Player.Treasury.Credits, 1);
        Assert.Equal((double)(treasury + tribute), (double)u.Actors[Union].Treasury.Credits, 1);   // Tribut in die Fraktionskasse
        Assert.Equal((double)tribute, (double)w.TributePaid, 1);
        Assert.True(Wars.CeasefireActive(u, w));
        // Kasse reicht nicht für den Tribut → Fehlschlag ohne Änderung
        var u2 = World();
        var w2 = DeclareWar(u2);
        u2.Player.Treasury.Credits = 2000f;   // nach dem Erklären (Kriegskasse war Bedingung): zu wenig für den Tribut
        w2.Weariness = 0.05f;
        float t2 = Wars.TributeFor(u2, w2, Union);
        Assert.True(t2 > 2000f);
        Assert.Contains("Kasse reicht nicht", Wars.CeasefireIssue(u2, Union));
        float tr2 = u2.Actors[Union].Treasury.Credits;
        Assert.False(CommsActions.Default().Execute(u2, "ceasefire:1").Success);
        Assert.Equal(tr2, u2.Actors[Union].Treasury.Credits);
        Assert.Equal(0f, w2.TributePaid);
        Assert.False(Wars.CeasefireActive(u2, w2));
    }

    [Fact]
    public void Frieden_NachSchwellen_oderGegenTribut_AuswegAusDemKiKrieg()
    {
        var u = World();
        Wire(u);
        var actions = CommsActions.Default();
        var me = Me(u);
        // KI erklärt dem Spieler den Krieg (Schalter an in der Standardwelt)
        Relations.Set(u, Union, u.PlayerActor, -0.9f);
        u.Actors[Union].Treasury.Credits = 500000f;
        u.Tick = 20000;
        new WarSystem().Step(u, WarSystem.Interval);
        var w = Wars.Between(u, Union, me);
        Assert.NotNull(w);
        // zu früh / zu wenig müde → nur gegen Tribut
        w!.Weariness = 0.2f;
        Assert.False(Wars.PeaceFree(u, w, Union));
        float tribute = Wars.TributeFor(u, w, Union);
        Assert.True(tribute > 0f);
        u.Player.Treasury.Credits = 100000f;
        float treasury = u.Actors[Union].Treasury.Credits;
        var r = actions.Execute(u, "peace:1");
        Assert.True(r.Success, r.Message);
        Assert.Equal((double)-tribute, (double)r.Credits, 1);
        Assert.Equal(treasury + tribute, u.Actors[Union].Treasury.Credits, 1);
        Assert.Empty(u.Wars);
        Assert.True(u.PeaceSince.ContainsKey((Math.Min(me.Value, Union.Value), Math.Max(me.Value, Union.Value))));
        Assert.True(Relations.Effective(u, me, Union) >= u.Rules.PlayerPeaceRelation - 1e-4f);
        // Abkühlzeit: erneute Kriegserklärung gesperrt
        Assert.Contains("zu frisch", Wars.PlayerDeclareIssue(u, Union));
        // kostenlose Annahme: Mindestdauer + Müdigkeit ≥ 0,6
        var u2 = World();
        Wire(u2);
        var me2 = Me(u2);
        var w2 = DeclareWar(u2);
        w2.Weariness = 0.7f;
        u2.Tick = w2.Since + u2.Rules.WarMinTicks + 100;
        Assert.True(Wars.PeaceFree(u2, w2, Union));
        Assert.Null(Wars.PeaceIssue(u2, Union));
        float wallet2 = u2.Player.Treasury.Credits, tr2 = u2.Actors[Union].Treasury.Credits;
        var r2 = actions.Execute(u2, "peace:1");
        Assert.True(r2.Success, r2.Message);
        Assert.Equal(0.0, (double)r2.Credits, 3);
        Assert.Equal((double)wallet2, (double)u2.Player.Treasury.Credits, 3);
        Assert.Equal((double)tr2, (double)u2.Actors[Union].Treasury.Credits, 3);
        Assert.Empty(u2.Wars);
        Assert.Contains(u2.Inbox, m => m.Kind == "peace_concluded");
    }

    [Fact]
    public void Tribut_Aktion_FliesstInDieKasse_MindertGefordertenBetrag()
    {
        var u = World();
        var actions = CommsActions.Default();
        var w = DeclareWar(u);
        w.Weariness = 0.1f;
        float before = Wars.TributeFor(u, w, Union);
        u.Player.Treasury.Credits = 20000f;
        float wallet = u.Player.Treasury.Credits, treasury = u.Actors[Union].Treasury.Credits;
        var events = new List<SimEvent>();
        u.Events.Raised += e => events.Add(e);
        var r = actions.Execute(u, "tribute:1:5000");
        Assert.True(r.Success, r.Message);
        Assert.Equal(-5000.0, (double)r.Credits, 1);
        Assert.Equal((double)(wallet - 5000f), (double)u.Player.Treasury.Credits, 1);
        Assert.Equal((double)(treasury + 5000f), (double)u.Actors[Union].Treasury.Credits, 1);
        Assert.Equal(5000.0, (double)w.TributePaid, 1);
        Assert.Equal((double)(before - 5000f), (double)Wars.TributeFor(u, w, Union), 1);
        Assert.Contains(events, e => e is TributePaid tp && tp.Amount == 5000f && tp.To == Union);
        // Sperrgründe
        Assert.Contains("Kein Krieg", Wars.TributeIssue(World(), Union, 100f));
        Assert.False(actions.Execute(World(), "tribute:1:100").Success);
        var poor = World();
        var wp = DeclareWar(poor);
        poor.Player.Treasury.Credits = 100f;
        Assert.Contains("Kasse reicht nicht", Wars.TributeIssue(poor, Union, 5000f));
        Assert.False(actions.Execute(poor, "tribute:1:5000").Success);
        Assert.Equal(0f, wp.TributePaid);
    }

    // ───────── Kriegsangriffe auf Spielerschiffe ─────────

    [Fact]
    public void Kriegsangriff_aufSpielerschiff_NachWarnung_KeinErgeben_WaffenstillstandStoppt()
    {
        var u = World();
        var me = Me(u);
        var ship = u.Ships[u.PlayerShip];
        // Spieler im Unionssektor (Startsektor 1 gehört der Union)
        Assert.Equal(Union, u.Sectors[u.PlayerSector].Owner);
        var w = DeclareWar(u);
        Assert.True(Wars.Hostile(u, Union, me));
        // Polizei der Union in den Spielersektor stellen
        var police = SecuritySystem.PoliceOf(u, Union).First();
        police.DockedAt = default; police.Transit = null;
        police.Position = ship.Position + new Vec2(80f, 0f);
        u.MoveShip(police, u.PlayerSector);
        new SecuritySystem().Step(u, 5);
        var pending = u.PendingEncounters.FirstOrDefault(e => e.Target == ship.Id && e.Kind == EncounterKind.War);
        Assert.NotNull(pending);
        // kein Ergeben gegen Geld im Krieg
        Assert.Contains("kein Ergeben", PlayerSurrender.Issue(u));
        // nach Ablauf der Warnung: echtes Gefecht mit EncounterCause.War
        u.Tick = pending!.DueTick;
        new SecuritySystem().Step(u, 5);
        Assert.DoesNotContain(u.PendingEncounters, e => e.Kind == EncounterKind.War);
        var fight = u.Fights.Of(ship.Id);
        Assert.NotNull(fight);
        Assert.Equal(EncounterCause.War, fight!.Cause);
        Assert.True(u.Fights.Of(police.Id) == fight);
        // Ergeben im Kriegsgefecht gesperrt
        Assert.Contains("kein Ergeben", PlayerSurrender.Issue(u));
        // Waffenstillstand beendet die Angriffe (frische Welt, kein laufendes Gefecht)
        var u3 = World();
        var me3 = Me(u3);
        var w3 = DeclareWar(u3);
        var ship3 = u3.Ships[u3.PlayerShip];
        var police3 = SecuritySystem.PoliceOf(u3, Union).First();
        police3.DockedAt = default; police3.Transit = null;
        police3.Position = ship3.Position + new Vec2(80f, 0f);
        u3.MoveShip(police3, u3.PlayerSector);
        w3.Weariness = 0.5f;
        Assert.True(CommsActions.Default().Execute(u3, "ceasefire:1").Success);
        new SecuritySystem().Step(u3, 5);
        Assert.DoesNotContain(u3.PendingEncounters, e => e.Kind == EncounterKind.War);
        Assert.False(Wars.Hostile(u3, Union, me3));
    }

    // ───────── Persistenz ─────────

    [Fact]
    public void SpeichernLaden_InboxGespeichert_Bitgleich_HashUnabhaengigVonInbox()
    {
        var u = World();
        var me = Me(u);
        var w = DeclareWar(u);
        w.Weariness = 0.5f;
        Assert.True(CommsActions.Default().Execute(u, "ceasefire:1").Success);
        var runner = new SimulationRunner();
        runner.Run(u, 300);
        Comms.Add(u, Union, "war_declared", new() { ["faction"] = "1" }, ttl: 0);
        Comms.Add(u, Guild, "peace_offer", new() { ["faction"] = "2" }, ttl: 5000);
        Comms.MarkRead(u, u.Inbox[0].Id);
        Assert.True(u.Inbox.Count >= 2);
        // Hash unabhängig vom Posteingang
        ulong h1 = UniverseHash.Compute(u);
        Comms.Add(u, Union, "bounty_earned", new() { ["faction"] = "1", ["amount"] = "300" }, ttl: 0);
        Assert.Equal(h1, UniverseHash.Compute(u));
        int inboxCount = u.Inbox.Count;
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(inboxCount, copy.Inbox.Count);
        Assert.Equal(u.Inbox.Select(m => (m.Id, m.From.Value, m.Kind, m.Created, m.Expires, m.Read)),
            copy.Inbox.Select(m => (m.Id, m.From.Value, m.Kind, m.Created, m.Expires, m.Read)));
        Assert.Equal(u.Inbox.Select(m => m.Args.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList()),
            copy.Inbox.Select(m => m.Args.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList()));
        Assert.Equal(u.NextMessageId, copy.NextMessageId);
        var cw = copy.Wars.Single();
        Assert.Equal(w.CeasefireUntil, cw.CeasefireUntil);
        Assert.Equal(w.LastOfferTick, cw.LastOfferTick);
        Assert.Equal(w.TributePaid, cw.TributePaid);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        var r1 = new SimulationRunner(); var r2 = new SimulationRunner();
        for (int i = 0; i < 1000; i++) { r1.Step(u); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        Assert.Equal(u.Inbox.Count, copy.Inbox.Count);
        // alter Stand ohne Inbox lädt leer
        var json = SaveGameJson.Write(u, Now);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("Inbox", out _));
    }

    [Fact]
    public void AiWarOnPlayer_StandardweltAn_NpcWeltUnveraendertOhneSpielerkrieg()
    {
        var on = World(1);
        Assert.True(on.Rules.AiWarOnPlayer);   // Runde 40: in der Standardwelt eingeschaltet
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.AiWarOnPlayer = false;
        var off = WorldBuilder.Build(def, 1);
        new SimulationRunner().Run(on, 20000);
        new SimulationRunner().Run(off, 20000);
        Assert.Empty(on.Wars);   // keine KI erklärt dem Spieler Krieg (Beziehungen bleiben freundlich)
        Assert.Empty(on.Inbox);
        Assert.Equal(UniverseHash.Compute(off), UniverseHash.Compute(on));
        Assert.Equal(UniverseHash.Compute(off, true, includePlayerPolitics: false), UniverseHash.Compute(on, true, includePlayerPolitics: false));
    }
}
