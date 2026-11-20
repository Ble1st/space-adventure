using SpaceSim.Core.Combat;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 6 (docs/POLITIK.md „Fehden, Vermittlung, Regierungskrise“): Fehden zwischen KI-Fraktionen und mit dem Spieler (Beginn, Sperren, Überfälle,
/// Ende), Vermittlung durch Dritte und den Spieler, Regierungskrise nach Verlust des Hauptquartiers, Speichern/Laden, Schalter.
/// </summary>
public class FehdenTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    static readonly FactionId Union = new(1), Guild = new(2), Mont = new(3);
    static readonly SectorId Alpha = new(1), Beta = new(2), Nebelmark = new(9);

    /// <summary>Alles dieser Runde zurücknehmen (Referenz-Hash-Tests; Teil der Rücknahmekette über <see cref="BalanceFixTests.RevertBalanceFix"/>/<see cref="GasNebulaTests.RevertGas"/>).</summary>
    internal static void RevertFeuds(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.FeudsActive = false; r.PlayerFeuds = false; r.Mediation = false; r.PlayerMediation = false; r.GovernmentCrisis = false;
    }

    static Universe World(ulong seed = 1) => WorldBuilder.Build(TestWorlds.Def(fuel: false, research: false), seed);

    static void SetRel(Universe u, FactionId a, FactionId b, float v) { Relations.Set(u, a, b, v); Relations.Set(u, b, a, v); }

    static void Contest(Universe u, SectorId sid, FactionId a, FactionId b)
    {
        var s = u.Sectors[sid];
        s.Contested = true;
        s.Influence = new Dictionary<FactionId, float> { [a] = 0.45f, [b] = 0.4f };
    }

    static List<SimEvent> Record(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += e => list.Add(e);
        return list;
    }

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_RuecknahmeAus()
    {
        var c = new EconomyRules();
        Assert.False(c.FeudsActive || c.PlayerFeuds || c.Mediation || c.PlayerMediation || c.GovernmentCrisis);
        var u = World();
        Assert.True(u.Rules.FeudsActive && u.Rules.PlayerFeuds && u.Rules.Mediation && u.Rules.PlayerMediation && u.Rules.GovernmentCrisis);
        var def = TestWorlds.Def(fuel: false, research: false);
        RevertFeuds(def);
        var off = WorldBuilder.Build(def, 1);
        Assert.False(off.Rules.FeudsActive || off.Rules.PlayerFeuds || off.Rules.Mediation || off.Rules.PlayerMediation || off.Rules.GovernmentCrisis);
    }

    [Fact]
    public void Standardwelt_OhneAusloeser_KeineFehde_Bitgleich()
    {
        var on = World(3);
        var def = TestWorlds.Def(fuel: false, research: false);
        RevertFeuds(def);
        var off = WorldBuilder.Build(def, 3);
        var r1 = new SimulationRunner(); var r2 = new SimulationRunner();
        for (int i = 0; i < 6000; i++) { r1.Step(on); r2.Step(off); }
        Assert.Empty(on.Feuds);
        Assert.Empty(on.Crises);
        Assert.Equal(UniverseHash.Compute(off), UniverseHash.Compute(on));
    }

    [Fact]
    public void KiFehde_Beginn_NurMitFeindschaftUndUmkaempftemSektor_SperreGegeneinander()
    {
        var u = World();
        var ev = Record(u);
        Assert.Equal("Keine Feindschaft.", Feuds.StartIssue(u, Union, Guild));
        SetRel(u, Union, Guild, -0.45f);
        Assert.Equal("Kein umkämpfter Sektor.", Feuds.StartIssue(u, Union, Guild));
        Contest(u, Beta, Union, Guild);
        Assert.Null(Feuds.StartIssue(u, Union, Guild));
        new FeudSystem().Step(u, 100);
        var fd = Assert.Single(u.Feuds);
        Assert.Equal((Union, Guild), (fd.A, fd.B));
        Assert.Equal(u.Tick + u.Rules.FeudTicks, fd.Until);
        Assert.Contains(ev, e => e is FeudStarted);
        Assert.False(Wars.Hostile(u, Union, Guild));   // keine Belagerung: Fehde ist kein Krieg
        Assert.StartsWith("Fehde mit", Feuds.FactionLine(u, Union));
        // Sperre: mindestens FeudLockLevel gegen den Fehdegegner (hier höher als die Beziehungsstufe)
        u.Rules.FeudLockLevel = 4;
        SetRel(u, Union, Guild, -0.2f);
        var alpha = u.Sectors[Alpha];
        Assert.Equal(Union, alpha.Owner);
        var (level, against, _) = FactionPolicySystem.Desired(u, alpha);
        Assert.Equal(Math.Min(4, SectorControl.MaxLevel(u, alpha)), level);
        Assert.Equal(Guild, against);
        // eine zweite Fehde derselben Fraktion beginnt nicht
        SetRel(u, Union, Mont, -0.5f);
        Contest(u, Alpha, Union, Mont);
        Assert.Equal("Fehde läuft schon.", Feuds.StartIssue(u, Guild, Union));
        Assert.Equal("Schon in einer Fehde.", Feuds.StartIssue(u, Union, Mont));
    }

    [Fact]
    public void KiFehde_Ende_Ablauf_Beziehung_Krieg_UndAbkuehlzeit()
    {
        var u = World();
        var ev = Record(u);
        var fd = Feuds.Start(u, Union, Guild);
        SetRel(u, Union, Guild, -0.5f);
        fd.Until = u.Tick;
        new FeudSystem().Step(u, 100);
        Assert.Empty(u.Feuds);
        Assert.Contains(ev, e => e is FeudEnded { Reason: "expired" });
        Contest(u, Beta, Union, Guild);
        Assert.Equal("Fehde zu frisch.", Feuds.StartIssue(u, Union, Guild));
        // Beziehung erholt
        u.FeudEnded.Clear();
        Feuds.Start(u, Union, Guild);
        SetRel(u, Union, Guild, 0f);
        new FeudSystem().Step(u, 100);
        Assert.Contains(ev, e => e is FeudEnded { Reason: "relation" });
        // Krieg ersetzt die Fehde
        u.FeudEnded.Clear();
        Feuds.Start(u, Union, Guild);
        Wars.Declare(u, Union, Guild);
        new FeudSystem().Step(u, 100);
        Assert.Contains(ev, e => e is FeudEnded { Reason: "war" });
        Assert.Empty(u.Feuds);
    }

    /// <summary>Polizeischiff der Union und ein Händler der Gilde im umkämpften Beta: Fehdeüberfall als Gefecht, Beziehung sinkt, keine Fahndung, keine Belagerung.</summary>
    [Fact]
    public void Fehdeueberfall_PolizeiStelltHaendlerDerGegenseite_ImUmkaempftenSektor()
    {
        var u = World();
        var ev = Record(u);
        u.Rules.MinLootValue = 50f;
        var police = SecuritySystem.PoliceOf(u, Union).First(s => s.Transit is null);
        var trader = u.ShipsById().First(s => s.IsEconomyAi && s.Role == ShipRole.Trader && s.Transit is null && !u.Fights.IsEngaged(s.Id));
        trader.Owner = (ActorId)Guild;
        trader.Order = null;
        var food = u.Goods.Values.First(g => g.Key == "food").Id;
        trader.Cargo.Clear();
        trader.Cargo[food] = 40f;
        if (police.Sector != Beta) u.MoveShip(police, Beta);
        if (trader.Sector != Beta) u.MoveShip(trader, Beta);
        // freier Fleck fern der Stationen
        Vec2 spot = default;
        for (float x = -600f; x <= 600f && spot == default; x += 50f)
            for (float y = -600f; y <= 600f; y += 50f)
                if (!ThreatMap.NearStation(u, Beta, new Vec2(x, y)) && !ThreatMap.NearStation(u, Beta, new Vec2(x + 20f, y))) { spot = new Vec2(x, y); break; }
        trader.Position = spot; police.Position = spot + new Vec2(20f, 0f);
        Contest(u, Beta, Union, Guild);
        SetRel(u, Union, Guild, -0.5f);
        var fd = Feuds.Start(u, Union, Guild);
        fd.Since = u.Tick - u.Rules.FeudRaidTicks;
        Assert.Contains(Beta, Feuds.RaidSectors(u, Union, Guild).Select(s => s.Id));
        Assert.Same(police, Feuds.AttackerIn(u, Union, Beta));
        Assert.Same(trader, Feuds.VictimIn(u, Guild, Beta));
        Assert.True(Feuds.TryRaid(u, fd));
        Assert.Equal(1, fd.Raids);
        Assert.False(Feuds.TryRaid(u, fd));   // Abstand FeudRaidTicks
        Assert.True(Relations.Effective(u, Union, Guild) < -0.5f + 1e-3f - u.Rules.FeudRaidRelationLoss + 0.0001f);
        Assert.Contains(ev, e => e is FeudRaid { Attacker: var a, Victim: var v } && a == Union && v == Guild);
        var e = u.Fights.Encounters.Single(x => x.Cause == EncounterCause.Feud);
        Assert.Equal(trader.Id, e.Victim);
        var runner = new SimulationRunner();
        for (int i = 0; i < 400 && !e.Done; i++) runner.Step(u);
        Assert.True(e.Done);
        Assert.Contains(ev, x => x is EncounterEnded { Cause: EncounterCause.Feud });
        Assert.DoesNotContain(ev, x => x is SiegeStarted);
        Assert.Empty(u.Actors[(ActorId)Union].Wanted);
    }

    [Fact]
    public void SpielerFehde_NurMitFirmensitz_Erklaeren_KeinVerbrechen_Beenden()
    {
        var u = World();
        var comms = CommsActions.Default();
        var me = u.PlayerFaction!.Value;
        var ev = Record(u);
        Assert.Contains("Firmensitz", Feuds.PlayerDeclareIssue(u, Guild));
        Assert.False(comms.Execute(u, $"feud:declare:{Guild.Value}").Success);
        Assert.Empty(u.Feuds);
        // Firmensitz in Nebelmark
        u.Player.Treasury.Credits = 400_000f;
        var ship = u.Ships[u.PlayerShip];
        if (ship.DockedAt.Value != 0) PlayerFleet.Undock(u, ship);
        u.MoveShip(ship, Nebelmark);
        float half = 150f * MathF.Max(1f, u.Sectors[Nebelmark].Scale);
        bool ok = false;
        for (float x = -half; x <= half && !ok; x += 37f)
            for (float y = -half; y <= half; y += 37f) { ship.Position = new Vec2(x, y); if (PlayerSeatsOffices.SeatIssue(u, Nebelmark) is null) { ok = true; break; } }
        Assert.True(comms.Execute(u, $"firmseat:found:{Nebelmark.Value}").Success);
        Assert.True(PlayerSeatsOffices.FeudUnlocked(u));
        new SimulationRunner().Step(u);   // hängt den Posteingang (CommsSystem) an den Ereignisbus
        float monBefore = Relations.Effective(u, Mont, u.PlayerActor);
        var pre = comms.Preview(u, $"feud:declare:{Guild.Value}");
        Assert.True(pre.Possible, pre.Reason);
        var res = comms.Execute(u, $"feud:declare:{Guild.Value}");
        Assert.True(res.Success, res.Message);
        var fd = Assert.Single(u.Feuds);
        Assert.True(fd.ByPlayer);
        Assert.True(Relations.Effective(u, Guild, u.PlayerActor) < u.Rules.FeudRelation);
        Assert.Equal(monBefore - u.Rules.FeudThirdPartyLoss, Relations.Effective(u, Mont, u.PlayerActor), 3);
        Assert.Contains(u.Inbox, m => m.Kind == "feud_started");
        // Angriff auf ein Schiff des Fehdegegners ist kein Verbrechen — auf ein Schiff der Union schon
        Assert.Empty(Wanted.ReportAssault(u, u.PlayerActor, Beta, (ActorId)Guild));
        Assert.Equal(0, Wanted.LevelOf(u, u.PlayerActor, Guild));
        Assert.NotEmpty(Wanted.ReportAssault(u, u.PlayerActor, Beta, (ActorId)Union));
        Assert.True(Feuds.PlayerFeudWith(u, (ActorId)Guild));
        // Sperren der Gilde gegen den Spieler (Fehdegegner) in ihren Sektoren
        var gSec = u.Sectors.Values.First(s => s.Owner == Guild);
        var (_, against, _) = FactionPolicySystem.Desired(u, gSec);
        Assert.Equal(me, against);
        // Fehde beenden
        Assert.True(comms.Execute(u, $"feud:end:{Guild.Value}").Success);
        Assert.Empty(u.Feuds);
        Assert.Contains(ev, e => e is FeudEnded { Reason: "ended" });
        Assert.False(comms.Execute(u, $"feud:end:{Guild.Value}").Success);
        Assert.Contains("zu frisch", Feuds.PlayerDeclareIssue(u, Guild));
        Assert.False(comms.Execute(u, "feud:declare:x").Success);
    }

    [Fact]
    public void Vermittlung_DritteFraktion_HebtBeziehung_KriegWirdMuede_FehdeEndet()
    {
        var u = World();
        var ev = Record(u);
        u.Rules.Warfare = true;
        var w = Wars.Declare(u, Union, Guild);
        SetRel(u, Mont, Union, 0.4f); SetRel(u, Mont, Guild, 0.4f);
        var c = Mediation.Find(u, Union, Guild)!.Value;
        Assert.NotNull(Mediation.RipeIssue(u, c));   // zu jung
        Mediation.Step(u);
        Assert.Equal(0f, w.MediationBonus);
        w.Since -= u.Rules.MediationMinTicks;
        Assert.Equal(Mont, Mediation.MediatorFor(u, Mediation.Find(u, Union, Guild)!.Value));
        Mediation.Step(u);
        Assert.Equal(u.Rules.MediationWearinessBonus, w.MediationBonus);
        Assert.Equal(0.5f, Relations.Effective(u, Union, Mont), 3);
        Assert.Equal(0.5f, Relations.Effective(u, Guild, Mont), 3);
        Assert.Contains(ev, e => e is MediationConcluded { Kind: "war" } mc && mc.Mediator == Mont);
        Mediation.Step(u);   // Abkühlzeit
        Assert.Equal(u.Rules.MediationWearinessBonus, w.MediationBonus);
        // Müdigkeit zählt den Zuschlag
        new WarSystem().Step(u, 100);
        Assert.True(w.Weariness >= u.Rules.MediationWearinessBonus);
        // Fehde: Vermittlung beendet sie
        Wars.MakePeace(u, w, "manual");
        var fd = Feuds.Start(u, Union, Guild);
        fd.Since -= u.Rules.MediationMinTicks;
        Mediation.Step(u);
        Assert.Empty(u.Feuds);
        Assert.Contains(ev, e => e is FeudEnded { Reason: "mediated" });
        // ohne gute Beziehung keine Vermittlerin
        SetRel(u, Mont, Guild, 0f);
        var fd2 = Feuds.Start(u, Union, Guild);
        fd2.Since -= u.Rules.MediationMinTicks;
        Assert.Null(Mediation.MediatorFor(u, Mediation.Find(u, Union, Guild)!.Value));
    }

    [Fact]
    public void Spielervermittlung_GebuehrBeiderSeiten_Ansehen_Regeln()
    {
        var u = World();
        var comms = CommsActions.Default();
        u.Rules.Mediation = false;   // nur der Spieler vermittelt
        var fd = Feuds.Start(u, Union, Guild);
        string key = MediateAction.KeyFor(Guild, Union);
        Assert.Equal($"mediate:{Union.Value}:{Guild.Value}", key);
        Assert.Contains("zu jung", comms.Preview(u, key).Reason ?? "");
        fd.Since -= u.Rules.MediationMinTicks;
        u.Actors[(ActorId)Union].Treasury.Credits = 500_000f;
        u.Actors[(ActorId)Guild].Treasury.Credits = 500_000f;
        float uBefore = u.Actors[(ActorId)Union].Treasury.Credits, gBefore = u.Actors[(ActorId)Guild].Treasury.Credits, wallet = u.Player.Treasury.Credits;
        float relU = Relations.Effective(u, Union, u.PlayerActor);
        var pre = comms.Preview(u, key);
        Assert.True(pre.Possible, pre.Reason);
        float fee = Mediation.FeeOf(u, Union) + Mediation.FeeOf(u, Guild);
        Assert.True(fee > 0f);
        Assert.Equal(fee, pre.Cost, 1);
        var res = comms.Execute(u, key);
        Assert.True(res.Success, res.Message);
        Assert.Equal(wallet + fee, u.Player.Treasury.Credits, 1);
        Assert.Equal(uBefore + gBefore - fee, u.Actors[(ActorId)Union].Treasury.Credits + u.Actors[(ActorId)Guild].Treasury.Credits, 1);
        Assert.Equal(relU + u.Rules.MediationRelationGain, Relations.Effective(u, Union, u.PlayerActor), 3);
        Assert.Empty(u.Feuds);
        Assert.False(comms.Execute(u, key).Success);   // kein Konflikt mehr
        // misstraut: kein Vermitteln
        u.FeudEnded.Clear();
        var fd2 = Feuds.Start(u, Union, Guild);
        fd2.Since -= u.Rules.MediationMinTicks;
        PlayerStanding.Change(u, Guild, -1f, "test");
        Assert.Contains("traut dir nicht", Mediation.PlayerIssue(u, Union, Guild));
        Assert.False(comms.Execute(u, "mediate:1:1").Success);
        Assert.False(comms.Execute(u, "mediate:1").Success);
    }

    [Fact]
    public void Regierungskrise_NachHqVerlust_HalbeSteuern_KeinPolizeikauf_SperreHoechstens1_EndetVonSelbst()
    {
        var u = World();
        var ev = Record(u);
        var hq = u.StationsById().First(s => s.Owner == (ActorId)Union && StationProtection.KindOf(u, s) == GovernmentKind.Headquarters);
        Capitulation.Capitulate(u, hq, (ActorId)Guild, (ActorId)Guild);
        Assert.True(GovernmentCrisis.Active(u, Union));
        Assert.Contains(ev, e => e is GovernmentCrisisChanged { Started: true } g && g.Faction == Union);
        Assert.Contains(ev, e => e is SeatRelocated);   // Not-Sitz entsteht trotzdem
        Assert.Equal(0.5f, GovernmentCrisis.TaxFactor(u, Union));
        Assert.Equal(1f, GovernmentCrisis.TaxFactor(u, Guild));
        foreach (var s in u.Sectors.Values.Where(s => s.Owner == Union)) Assert.True(SectorControl.MaxLevel(u, s) <= u.Rules.CrisisMaxLockLevel);
        u.Actors[(ActorId)Union].Treasury.Credits = 1_000_000f;
        Assert.Null(SecuritySystem.Commission(u, Union));   // kein freiwilliger Polizeikauf
        Assert.Contains("Regierungskrise", Feuds.FactionLine(u, Union));
        // Ende nach CrisisTicks
        u.Tick += u.Rules.CrisisTicks;
        new FeudSystem().Step(u, 100);
        Assert.False(GovernmentCrisis.Active(u, Union));
        Assert.Contains(ev, e => e is GovernmentCrisisChanged { Started: false });
        Assert.Empty(u.Crises);
    }

    [Fact]
    public void Regierungskrise_Steuern_HalbSoVielWieOhne()
    {
        static float Taxes(bool crisis)
        {
            var u = World(4);
            u.Rules.TreasuryTargetShare = 0f;   // ohne Steuerbremse vergleichbar
            if (crisis) { u.Crises[Union.Value] = u.Tick + 100_000; }
            float sum = 0f;
            u.Events.Raised += e => { if (e is TaxCollected t && t.Treasury == (ActorId)Union) sum += t.Amount; };
            new UpkeepSystem().Step(u, 50);
            return sum;
        }
        float full = Taxes(false), half = Taxes(true);
        Assert.True(full > 0f);
        Assert.Equal(full * 0.5f, half, 2);
    }

    [Fact]
    public void SpeichernLaden_Bitgleich_MitFehdeKriseUndVermittlung()
    {
        var a = World(5);
        var r0 = new SimulationRunner();
        for (int i = 0; i < 200; i++) r0.Step(a);
        a.Rules.Warfare = true;
        var w = Wars.Declare(a, Union, Mont);
        w.MediationBonus = 0.25f; w.LastMediationTick = a.Tick;
        var fd = Feuds.Start(a, Union, Guild);
        fd.Raids = 2; fd.LastRaidTick = a.Tick;
        a.FeudEnded[(2, 3)] = a.Tick - 5;
        GovernmentCrisis.Start(a, Guild);
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(a, Now));
        Assert.Equal(a.Feuds.Select(x => (x.A, x.B, x.Since, x.Until, x.ByPlayer, x.LastRaidTick, x.Raids)), copy.Feuds.Select(x => (x.A, x.B, x.Since, x.Until, x.ByPlayer, x.LastRaidTick, x.Raids)));
        Assert.Equal(a.Crises.OrderBy(k => k.Key), copy.Crises.OrderBy(k => k.Key));
        Assert.Equal(0.25f, copy.Wars.Single().MediationBonus);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(copy));
        var r1 = new SimulationRunner(); var r2 = new SimulationRunner();
        for (int i = 0; i < 1500; i++) { r1.Step(a); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(copy));
    }

    [Fact]
    public void Hash_FehdeMitSpieler_NurMitSpielerpolitik()
    {
        var u = World();
        var me = u.PlayerFaction!.Value;
        ulong full0 = UniverseHash.Compute(u), npc0 = UniverseHash.Compute(u, includeTags: true, includePlayerPolitics: false);
        Feuds.Start(u, Guild, me, byPlayer: true);
        Assert.NotEqual(full0, UniverseHash.Compute(u));
        Assert.Equal(npc0, UniverseHash.Compute(u, includeTags: true, includePlayerPolitics: false));
        Feuds.Start(u, Union, Mont);
        Assert.NotEqual(npc0, UniverseHash.Compute(u, includeTags: true, includePlayerPolitics: false));
    }
}
