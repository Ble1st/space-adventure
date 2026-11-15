using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 38: der Spieler ist von Anbeginn eine eigene Fraktion. Beweise: die KI-Welt merkt davon nichts (Hash ohne Spielerpolitik mit Regel an/aus gleich),
/// zentraler Filter (Fiskalbremse, Drift, Handlungsfähigkeit, Polizei, Bauschiffe, Fahndung), Startbeziehungen, Krieg des Spielers ohne Kriegsgrund,
/// KI-Kriegserklärung gegen den Spieler nur mit Schalter, Frieden durch Erschöpfung/leere Kasse, Speichern/Laden, Einnahmen aus Ereignissen.
/// </summary>
public class Runde38Tests
{
    /// <summary>Standardwelt wie im Spiel (Treibstoff/Forschung aus), Spielerfraktion an oder aus.</summary>
    static Universe World(bool own, ulong seed = 1)
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.PlayerOwnFaction = own;
        return WorldBuilder.Build(def, seed);
    }

    /// <summary>Runde 38 zurückdrehen (Referenz-Hash-Tests über <see cref="Phase29Tests.RevertRunde24"/>): Spieler wieder Mitglied der Startfraktion.</summary>
    internal static void RevertRunde38(WorldDef def) { def.Rules ??= new EconomyRules(); def.Rules.PlayerOwnFaction = false; }

    static FactionId Me(Universe u) => u.PlayerFaction ?? throw new Xunit.Sdk.XunitException("keine Spielerfraktion");

    [Fact]
    public void Standardwelt_SpielerIstEigeneFraktion_MitStartbeziehungen()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        Assert.True(def.Rules!.PlayerOwnFaction, "Standardwelt: Regel an");
        var u = World(true);
        var me = Me(u);
        Assert.Equal(u.PlayerActor.Value, me.Value);
        Assert.True(u.IsPlayerFaction(me));
        Assert.Equal(me, u.Player.Membership);
        Assert.Equal(ActorKind.Player, u.Player.Kind);
        Assert.Equal(500f, u.Player.Treasury.Credits);   // Kasse = bisherige Spielerkasse
        Assert.Empty(u.Factions[me].Territory);
        Assert.DoesNotContain(me, u.NpcFactionIds());
        Assert.Equal(u.Factions.Count - 1, u.NpcFactionIds().Count);
        var startOwner = u.Sectors[u.PlayerSector].Owner;
        foreach (var f in u.NpcFactionIds())
        {
            float want = f == startOwner ? 0.2f : 0.1f;
            Assert.Equal(want, Relations.Effective(u, f, u.PlayerActor), 3);
            Assert.Equal(want, Relations.Effective(u, u.PlayerActor, f), 3);
        }
        // Freie Händler und Gesetzlose bleiben bei ihrer Vorgabe
        Assert.Equal(u.Rules.RelationFree, Relations.Effective(u, u.FreeActor, u.PlayerActor), 3);
        var outlaw = u.Actors.Values.First(a => a.Kind == ActorKind.Outlaw);
        Assert.Equal(u.Rules.RelationOutlaw, Relations.Effective(u, outlaw.Id, u.PlayerActor), 3);
        // Startpaket in einem anderen Sektor: +0,2 zu dessen Besitzer
        var fighter = WorldBuilder.Build(TestWorlds.Def(false, false), 1, "fighter");
        var owner4 = fighter.Sectors[fighter.PlayerSector].Owner;
        Assert.Equal(0.2f, Relations.Effective(fighter, owner4, fighter.PlayerActor), 3);
    }

    [Fact]
    public void RegelAus_VerhaltenBisRunde37()
    {
        var u = World(false);
        Assert.Null(u.PlayerFaction);
        Assert.Equal(new FactionId(1), u.Player.Membership);
        Assert.Equal(u.Factions.Count, u.NpcFactionIds().Count);
    }

    [Theory]
    [InlineData(1UL, 20000)]
    [InlineData(1UL, 60000)]
    [InlineData(3UL, 20000)]
    public void HashOhneSpielerpolitik_GleichMitRegelAnUndAus(ulong seed, int ticks)
    {
        var on = World(true, seed);
        var off = World(false, seed);
        Assert.Equal(UniverseHash.Compute(off, true, includePlayerPolitics: false), UniverseHash.Compute(on, true, includePlayerPolitics: false));
        Assert.NotEqual(UniverseHash.Compute(off), UniverseHash.Compute(on));   // der volle Hash ändert sich zwingend
        new SimulationRunner().Run(on, ticks);
        new SimulationRunner().Run(off, ticks);
        Assert.Equal(UniverseHash.Compute(off, true, includePlayerPolitics: false), UniverseHash.Compute(on, true, includePlayerPolitics: false));
        // Ohne Spielerhandlung bewegt sich keine Spielerbeziehung (keine Drift) und es gibt keine Fahndung bei der Spielerfraktion
        var me = Me(on);
        var startOwner = on.Sectors[new SectorId(1)].Owner;
        foreach (var f in on.NpcFactionIds())
            Assert.Equal(f == startOwner ? 0.2f : 0.1f, Relations.Effective(on, f, on.PlayerActor), 3);
        Assert.All(on.Actors.Values, a => Assert.False(a.Wanted.ContainsKey(me)));
        // Spielerfraktion ohne Polizei, Bauschiff, Kriegsschiff, Kleinstation
        Assert.DoesNotContain(on.Ships.Values, s => s.Owner == on.PlayerActor && s.Controller == Controller.Ai);
        Assert.DoesNotContain(on.Stations.Values, s => s.Owner == on.PlayerActor);
        Assert.Equal(500f, on.Player.Treasury.Credits, 3);   // keine Steuer, kein Unterhalt (der Spieler handelt nicht)
    }

    [Fact]
    public void ZentralerFilter_KeineKiRegelSiehtDieSpielerfraktion()
    {
        var u = World(true);
        var me = Me(u);
        // Fiskalbremse: Fraktionszahl ohne den Spieler
        var factors = UpkeepSystem.FiscalFactors(u);
        Assert.False(factors.ContainsKey(me.Value));
        Assert.Equal(u.NpcFactionIds().Count, factors.Count);
        // Handlungsfähigkeit: keine Pflichten, keine Rücklage, freie Kasse = ganze Kasse
        Assert.Empty(FactionViability.Check(u, u.PlayerActor));
        Assert.Equal(0f, FactionViability.Reserve(u, me));
        Assert.Null(FactionViability.Text(u, u.PlayerActor));
        Assert.Equal(u.Player.Treasury.Credits, FactionViability.Spendable(u, u.Player));
        // Fahndung: die Spielerfraktion führt keine
        var outlaw = u.Actors.Values.First(a => a.Kind == ActorKind.Outlaw);
        Wanted.Report(u, outlaw.Id, me, "piracy");
        Assert.False(outlaw.Wanted.ContainsKey(me));
        Assert.DoesNotContain(me, Wanted.AssaultAuthorities(u, u.PlayerSector, u.PlayerActor));
        // Steuerempfänger eines Spielerschiffs wäre nie die Spielerfraktion
        Assert.NotEqual(me, UpkeepSystem.TaxAuthority(u, u.Ships[u.PlayerShip]));
        // Beziehungsdrift lässt Spielerbeziehungen liegen
        Relations.Set(u, new FactionId(1), u.PlayerActor, -0.4f);
        FactionPolicySystem.StepRelations(u);
        Assert.Equal(-0.4f, Relations.Effective(u, new FactionId(1), u.PlayerActor), 4);
        // Diagnose: Beziehungseinträge der KI-Welt ohne die Spielereinträge
        Assert.Equal(Relations.StoredEntries(World(false)), Relations.StoredNpcEntries(u));
        Assert.True(Relations.StoredPlayerEntries(u) >= 2 * u.NpcFactionIds().Count);
    }

    [Fact]
    public void Spielerkrieg_OhneKriegsgrund_KasseUndAbkuehlzeit_BeziehungAndererSinkt()
    {
        var u = World(true);
        var me = Me(u);
        var target = new FactionId(2);
        Assert.Contains("Kriegskasse", Wars.PlayerDeclareIssue(u, target));
        Assert.NotNull(Wars.PlayerDeclareIssue(u, me));
        u.Player.Treasury.Credits = 20000f;
        Assert.True(Relations.Effective(u, target, u.PlayerActor) > u.Rules.WarDeclareRelation, "kein Kriegsgrund nötig");
        Assert.Null(Wars.PlayerDeclareIssue(u, target));
        var before = u.NpcFactionIds().ToDictionary(f => f, f => Relations.Effective(u, f, u.PlayerActor));
        var changes = new List<StandingChanged>();
        u.Events.Raised += e => { if (e is StandingChanged sc) changes.Add(sc); };
        var war = Wars.PlayerDeclare(u, target);
        Assert.True(Wars.AtWar(u, me, target));
        Assert.True(Wars.AtWar(u, u.PlayerActor, (ActorId)target));
        Assert.Equal(20000f, u.Player.Treasury.Credits);   // Kriegskasse ist Bedingung, kein Preis
        Assert.True(Relations.Effective(u, target, u.PlayerActor) <= u.Rules.WarRelation + 1e-4f);
        foreach (var f in u.NpcFactionIds().Where(f => f != target && Wars.CanWage(u, f)))
            Assert.Equal(before[f] - u.Rules.PlayerWarRelationLoss, Relations.Effective(u, f, u.PlayerActor), 4);
        Assert.Equal(u.NpcFactionIds().Count(f => f != target && Wars.CanWage(u, f)), changes.Count);
        Assert.Equal("Schon im Krieg.", Wars.PlayerDeclareIssue(u, target));
        // Frieden: Beziehung mindestens PlayerPeaceRelation (keine Dauersperre), danach Abkühlzeit
        Wars.MakePeace(u, war, "weary");
        Assert.True(Relations.Effective(u, target, u.PlayerActor) >= u.Rules.PlayerPeaceRelation - 1e-4f);
        Assert.Contains("Frieden ist zu frisch", Wars.PlayerDeclareIssue(u, target));
    }

    [Fact]
    public void Spielerkrieg_KiKauftKeineSchiffeFuerDenSpieler_SperrenFolgenDerBeziehung_FriedenDurchErschoepfung()
    {
        var u = World(true);
        var me = Me(u);
        var target = new FactionId(1);
        u.Player.Treasury.Credits = 20000f;
        Wars.PlayerDeclare(u, target);
        // Sperrwunsch des Gegners richtet sich wie in KI-Kriegen nach der schlechtesten Beziehung — hier gegen den Spieler
        var sector = u.Sectors.Values.Where(s => s.Owner == target).OrderByDescending(s => (int)s.Control).First();
        var (level, against, _) = FactionPolicySystem.Desired(u, sector);
        Assert.Equal(me, against);
        Assert.True(level > 0, $"Sperrstufe im Krieg gegen den Spieler ({level})");
        var runner = new SimulationRunner();
        bool lockedAgainstMe = false;
        for (int i = 0; i < 46000; i++)
        {
            runner.Step(u);
            if (u.Sectors.Values.Any(s => s.Owner == target && s.Access.EntryLevel > 0 && s.Access.Against == me)) lockedAgainstMe = true;
            if (!Wars.AtWar(u, me, target)) break;
        }
        Assert.True(lockedAgainstMe, "der Gegner sperrt Sektoren gegen den Spieler (gleiche Regeln wie KI-Kriege)");
        Assert.False(Wars.AtWar(u, me, target), "Frieden durch Erschöpfung");
        // Die Spielerfraktion kauft keine KI-Kriegsschiffe und keine Polizei
        Assert.DoesNotContain(u.Ships.Values, s => s.Owner == u.PlayerActor && s.Controller == Controller.Ai);
        Assert.True(Relations.Effective(u, target, u.PlayerActor) >= u.Rules.PlayerPeaceRelation - 1e-4f);
    }

    [Fact]
    public void Spielerkrieg_SchuldenBeendenIhn_SpielerWirdNieHandlungsunfaehig()
    {
        var u = World(true);
        var me = Me(u);
        u.Player.Treasury.Credits = 20000f;
        Wars.PlayerDeclare(u, new FactionId(2));
        var runner = new SimulationRunner();
        runner.Run(u, 6200);   // nach der Aufbauzeit der „Rücklage“ (der Spieler hat keine: nur Schulden zählen)
        Assert.True(Wars.AtWar(u, me, new FactionId(2)));
        u.Player.Treasury.Credits = -10f;
        runner.Run(u, 200);
        Assert.False(Wars.AtWar(u, me, new FactionId(2)), "Schulden beenden den Krieg (leere Kasse)");
    }

    [Fact]
    public void KiKriegGegenSpieler_NurMitSchalter()
    {
        var u = World(true);
        var me = Me(u);
        var a = new FactionId(1);
        Relations.Set(u, a, u.PlayerActor, -0.9f);
        u.Actors[a].Treasury.Credits = 500000f;
        Assert.False(u.Rules.AiWarOnPlayer);
        Assert.Contains("abgeschaltet", Wars.DeclareIssue(u, a, me));
        new WarSystem().Step(u, WarSystem.Interval);
        Assert.False(Wars.AtWar(u, a, me));
        u.Rules.AiWarOnPlayer = true;
        u.Tick = 20000;   // Rücklage aufgebaut
        Assert.Null(Wars.DeclareIssue(u, a, me));
        new WarSystem().Step(u, WarSystem.Interval);
        Assert.True(Wars.AtWar(u, a, me));
        // ohne Kriegsgrund nicht
        var b = new FactionId(2);
        Assert.Equal("Kein Kriegsgrund.", Wars.DeclareIssue(u, b, me));
    }

    [Fact]
    public void SpeichernLaden_MitSpielerfraktionUndKrieg_Bitgleich()
    {
        var u = World(true);
        u.Player.Treasury.Credits = 20000f;
        Wars.PlayerDeclare(u, new FactionId(2));
        new SimulationRunner().Run(u, 500);
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        Assert.Equal(u.PlayerFaction, copy.PlayerFaction);
        Assert.Equal(u.NpcFactionIds(), copy.NpcFactionIds());
        var r1 = new SimulationRunner(); var r2 = new SimulationRunner();
        for (int i = 0; i < 1000; i++) { r1.Step(u); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
    }

    [Fact]
    public void Einnahmen_AusEreignissen_LetzterSpieltag()
    {
        var u = World(true);
        var inc = new PlayerIncome(u);
        Assert.StartsWith("keine", inc.Text());
        var npc = u.Ships.Values.First(s => s.IsEconomyAi);
        u.Events.Publish(new PlayerSold(u.PlayerShip, new StationId(1), new GoodId(1), 10f, 120f));
        u.Events.Publish(new TradeCompleted(npc.Id, new StationId(1), new StationId(2), new GoodId(1), 5f, 999f));   // fremder Händler zählt nicht
        u.Events.Publish(new BountyPaid(u.PlayerActor, new FactionId(1), 300f));
        u.Events.Publish(new BountyPaid(npc.Owner, new FactionId(1), 50f));
        Assert.Equal(420f, inc.LastDay().Credits, 3);
        u.Tick += u.Rules.DayTicks + 1;
        Assert.Equal(0f, inc.LastDay().Credits, 3);
        Assert.Equal(u.Rules.DayTicks, inc.LastDay().Ticks);
    }
}
