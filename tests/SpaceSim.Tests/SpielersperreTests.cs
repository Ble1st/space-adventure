using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Spielersperre (Wunsch „Spieler sperren wenn Beziehung schlecht“): Verbrechen des Spielers senken sein Ansehen bei der verfolgenden KI-Fraktion
/// (<see cref="CrimeStanding"/>), je Tag gedeckelt; Erholung nur ohne Fahndung; die bestehenden Sperrregeln sperren ihn dann aus (Tore, Sprungantrieb).
/// </summary>
public class SpielersperreTests
{
    static Universe World(bool on = true, bool aiWar = false, ulong seed = 1)
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        if (!on) RevertSpielersperre(def);
        def.Rules!.AiWarOnPlayer = aiWar;   // Sperrpfad allein prüfen (sonst erklärt die Union ab −0,6 den Krieg)
        return WorldBuilder.Build(def, seed);
    }

    /// <summary>Regel aus (Code-Vorgabe): keine Ansehensverluste durch Verbrechen.</summary>
    internal static void RevertSpielersperre(WorldDef def)
    {
        def.Rules ??= new EconomyRules();
        def.Rules.CrimeStandingLoss = new();
        def.Rules.CrimeStandingCapPerDay = 0f; def.Rules.CrimeStandingRecoveryPerDay = 0f; def.Rules.CrimeStandingFineBonus = 0f;
    }

    static float Rel(Universe u, FactionId f) => Relations.Effective(u, f, u.PlayerActor);

    [Fact]
    public void Standardwelt_RegelAn_CodeVorgabeAus()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        Assert.Equal(0.10f, def.Rules!.CrimeStandingLoss["assault"], 3);
        Assert.Equal(0.30f, def.Rules.CrimeStandingCapPerDay, 3);
        Assert.Empty(new EconomyRules().CrimeStandingLoss);
    }

    [Theory]
    [InlineData("smuggling", 0.04f)]
    [InlineData("toll_evasion", 0.02f)]
    [InlineData("assault", 0.10f)]
    [InlineData("assault_station", 0.15f)]
    [InlineData("assault_gate", 0.15f)]
    [InlineData("piracy", 0.10f)]
    public void JedesVerbrechen_SenktAnsehen_UmSeinenRegelwert(string crime, float loss)
    {
        var u = World();
        var f = new FactionId(2);
        float before = Rel(u, f);
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        Wanted.Report(u, u.PlayerActor, f, crime);
        Assert.Equal(before - loss, Rel(u, f), 4);
        Assert.Equal(before - loss, Relations.Effective(u, u.PlayerActor, f), 4);   // beide Richtungen
        var sc = Assert.Single(events.OfType<StandingChanged>());
        Assert.Equal(CrimeStanding.ReasonPrefix + crime, sc.Reason);
        Assert.Equal(loss, events.OfType<CrimeReported>().Single().StandingLoss, 4);
        Assert.Contains(SecurityNames.Crime(crime), CrimeStanding.Text(u, f));
        // Andere Fraktionen bleiben unberührt (nur die verfolgende)
        foreach (var o in u.NpcFactionIds().Where(o => o != f)) Assert.DoesNotContain(events.OfType<StandingChanged>(), e => e.Faction == o);
    }

    [Fact]
    public void Abschuss_KostetZusaetzlich_NurSpieler_NurKiFraktion()
    {
        var u = World();
        var f = new FactionId(2);
        float before = Rel(u, f);
        var sector = u.Sectors.Values.First(s => s.Owner == f);
        Wanted.ReportAssault(u, u.PlayerActor, sector.Id, new ActorId(f.Value), kill: true);
        Assert.Equal(before - 0.10f - 0.05f, Rel(u, f), 4);
        // NPC-Täter (Kaperer): Fahndung ja, Ansehen des Spielers nein
        float rel3 = Rel(u, new FactionId(3));
        var outlaw = u.Actors.Values.First(a => a.Kind == ActorKind.Outlaw);
        Wanted.Report(u, outlaw.Id, new FactionId(3), "piracy");
        Assert.Equal(rel3, Rel(u, new FactionId(3)), 5);
    }

    [Fact]
    public void Tagesdeckel_EinGefechtSpringtNichtAufMinusEins()
    {
        var u = World();
        var f = new FactionId(2);
        float before = Rel(u, f);
        for (int i = 0; i < 10; i++) Wanted.Report(u, u.PlayerActor, f, "assault");
        Assert.Equal(before - 0.30f, Rel(u, f), 4);
        Assert.Equal(0.30f, u.Player.CrimeStanding[f].WindowLoss, 4);
        // Innerhalb des Tages nichts mehr …
        u.Tick += u.Rules.DayTicks - 1;
        Wanted.Report(u, u.PlayerActor, f, "assault");
        Assert.Equal(before - 0.30f, Rel(u, f), 4);
        // … ab dem nächsten Fenster wieder bis zum Deckel
        u.Tick += 1;
        for (int i = 0; i < 10; i++) Wanted.Report(u, u.PlayerActor, f, "assault");
        Assert.Equal(before - 0.60f, Rel(u, f), 4);
        Assert.Equal(0.60f, u.Player.CrimeStanding[f].Debt, 4);
        // Untergrenze −1
        for (int d = 0; d < 5; d++) { u.Tick += u.Rules.DayTicks; for (int i = 0; i < 4; i++) Wanted.Report(u, u.PlayerActor, f, "assault"); }
        Assert.Equal(-1f, Rel(u, f), 4);
        Assert.Equal(before + 1f, u.Player.CrimeStanding[f].Debt, 3);
    }

    [Fact]
    public void Erholung_NurOhneFahndung_HoechstensBisZumAusgangswert_StrafeGibtBonus()
    {
        var u = World();
        var f = new FactionId(2);
        float before = Rel(u, f);
        for (int i = 0; i < 3; i++) Wanted.Report(u, u.PlayerActor, f, "assault");
        float low = Rel(u, f);
        Assert.True(Wanted.LevelOf(u, u.PlayerActor, f) > 0);
        CrimeStanding.Recover(u, u.Rules.DayTicks * 5);
        Assert.Equal(low, Rel(u, f), 5);   // gesucht: keine Erholung
        // Strafe bezahlt → Fahndung weg, Bonus sofort
        Wanted.SetHeat(u, u.Player, f, 0f, 0f);
        CrimeStanding.OnFinePaid(u, u.PlayerActor, f);
        Assert.Equal(low + u.Rules.CrimeStandingFineBonus, Rel(u, f), 4);
        // dann langsam je Tag
        CrimeStanding.Recover(u, u.Rules.DayTicks);
        Assert.Equal(low + u.Rules.CrimeStandingFineBonus + u.Rules.CrimeStandingRecoveryPerDay, Rel(u, f), 4);
        // nie über den Wert vor dem Verbrechen, danach ist der Eintrag weg
        CrimeStanding.Recover(u, u.Rules.DayTicks * 100);
        Assert.Equal(before, Rel(u, f), 4);
        Assert.False(u.Player.CrimeStanding.ContainsKey(f));
        Assert.Null(CrimeStanding.Text(u, f));
    }

    [Fact]
    public void Erholung_NichtImKrieg_UndFriedenHebtDieSchuldAuf()
    {
        var u = World();
        var f = new FactionId(2);
        for (int i = 0; i < 3; i++) Wanted.Report(u, u.PlayerActor, f, "assault");
        Wanted.SetHeat(u, u.Player, f, 0f, 0f);
        u.Player.Treasury.Credits = 50000f;
        Wars.PlayerDeclare(u, f);
        float war = Rel(u, f);
        CrimeStanding.Recover(u, u.Rules.DayTicks * 10);
        Assert.Equal(war, Rel(u, f), 5);
        // Frieden hebt auf mindestens PlayerPeaceRelation (0) — der offene Verlust schrumpft auf den Abstand zum Ausgangswert
        PlayerStanding.Raise(u, f, 0.5f, "test");
        CrimeStanding.Recover(u, 50);
        Assert.False(u.Player.CrimeStanding.ContainsKey(f));
    }

    [Fact]
    public void WiederholteUeberfaelle_SperrenDenSpielerAus_ToreUndSprung_DritteUnberuehrt()
    {
        var u = World();
        var union = new FactionId(1);
        var runner = new SimulationRunner();
        var npcLocks = new List<SectorAccessChanged>();
        u.Events.Raised += e => { if (e is SectorAccessChanged sc && sc.ToEntry > 0 && sc.Against is { } ag && !u.IsPlayerFaction(ag)) npcLocks.Add(sc); };
        runner.Run(u, 200);
        // drei Tage je drei Angriffe mit Abschuss (der Tagesdeckel begrenzt jeden Tag auf −0,3)
        for (int day = 0; day < 3; day++)
        {
            for (int i = 0; i < 3; i++) { Wanted.Report(u, u.PlayerActor, union, "assault"); CrimeStanding.Apply(u, u.PlayerActor, union, "kill"); }
            runner.Run(u, u.Rules.DayTicks);
        }
        float rel = Rel(u, union);
        Assert.True(rel < -0.65f, $"Beziehung {rel:0.00}");
        var me = u.Ships[u.PlayerShip];
        var locked = u.Sectors.Values.Where(s => s.Owner == union && s.Access.Level(AccessDirection.Entry) > 0).ToList();
        Assert.NotEmpty(locked);
        foreach (var s in locked) Assert.True(s.Access.Against is { } ag && u.IsPlayerFaction(ag), s.Name);
        // Stufen folgen den Schwellen, gedeckelt durch die Kontrollstufe
        foreach (var s in locked) Assert.True(s.Access.EntryLevel <= u.Rules.StageMaxLevel[(int)s.Control], s.Name);
        // mindestens ein Sektor mit Stufe ≥ 3: der Spieler (feindlich) kommt über kein Tor hinein, auch nicht per Sprungantrieb
        var hard = locked.FirstOrDefault(s => s.Access.Level(AccessDirection.Entry) >= 3);
        Assert.NotNull(hard);
        var info = AccessInfo.ForSector(u, hard!.Id, me);
        Assert.True(info.PlayerDenied, info.ForPlayer);
        Assert.StartsWith("gesperrt", info.ForPlayer);
        var profile = GateAccess.ProfileOf(u, me);
        foreach (var gid in hard.Gates)
        {
            var inbound = u.Gates[u.Gates[gid].Target];
            Assert.Equal(AccessPolicy.Deny, GateAccess.Evaluate(u, inbound, profile, me).Policy);
        }
        var neighbour = u.Gates[u.Gates[hard.Gates[0]].Target].Sector;
        Assert.Equal(AccessPolicy.Deny, GateAccess.EvaluateJump(u, neighbour, hard.Id, profile, me).Policy);
        // Dritte: keine KI-Sperre gegen eine KI-Fraktion (Runde 38b)
        Assert.Empty(npcLocks);
    }

    [Fact]
    public void Erholung_HebtDieSperreWieder()
    {
        var u = World();
        u.Rules.CrimeStandingRecoveryPerDay = 0.1f;   // schneller als in der Standardwelt (0,02/Tag), damit der Test kurz bleibt — der Weg ist derselbe
        var union = new FactionId(1);
        var runner = new SimulationRunner();
        for (int day = 0; day < 2; day++)
        {
            for (int i = 0; i < 3; i++) Wanted.Report(u, u.PlayerActor, union, "assault");
            runner.Run(u, u.Rules.DayTicks);
        }
        Assert.Contains(u.Sectors.Values, s => s.Owner == union && s.Access.Level(AccessDirection.Entry) > 0);
        // Strafe zahlen und Fahndung los: Erholung je Tag hebt die Beziehung über die erste Schwelle — die Sperren gehen (Ablauf/Hysterese der Politikregel)
        Wanted.SetHeat(u, u.Player, union, 0f, 0f);
        CrimeStanding.OnFinePaid(u, u.PlayerActor, union);
        int guard = 0;
        while (u.Sectors.Values.Any(s => s.Owner == union && s.Access.Level(AccessDirection.Entry) > 0) && guard++ < 60) runner.Run(u, u.Rules.DayTicks);
        Assert.True(guard < 60, $"Sperre nach {guard} Tagen noch da, Beziehung {Rel(u, union):0.00}");
    }

    [Fact]
    public void SchalterAus_GleicherHash_UndKeinAnsehensverlust()
    {
        var on = World(true); var off = World(false);
        new SimulationRunner().Run(on, 3000);
        new SimulationRunner().Run(off, 3000);
        Assert.Equal(UniverseHash.Compute(off), UniverseHash.Compute(on));   // ohne Verbrechen des Spielers ändert die Regel nichts
        var f = new FactionId(2);
        float before = Rel(off, f);
        Wanted.Report(off, off.PlayerActor, f, "assault");
        Assert.Equal(before, Rel(off, f), 5);
        Assert.Empty(off.Player.CrimeStanding);
        // ohne Spielerverbrechen bleibt auch der Hash ohne Spielerpolitik gleich, mit ihnen ändert sich nur die Spielerpolitik
        Wanted.Report(on, on.PlayerActor, f, "assault");
        Assert.NotEmpty(on.Player.CrimeStanding);
    }

    [Fact]
    public void SpeichernLaden_Bitgleich_MitAnsehensschuld()
    {
        var u = World();
        var f = new FactionId(2);
        for (int i = 0; i < 4; i++) Wanted.Report(u, u.PlayerActor, f, "assault");
        new SimulationRunner().Run(u, 300);
        Assert.NotEmpty(u.Player.CrimeStanding);
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        var a = u.Player.CrimeStanding[f]; var b = copy.Player.CrimeStanding[f];
        Assert.Equal((a.Debt, a.Base, a.WindowStart, a.WindowLoss, a.LastReason, a.LastTick), (b.Debt, b.Base, b.WindowStart, b.WindowLoss, b.LastReason, b.LastTick));
        Wanted.SetHeat(u, u.Player, f, 0f, 0f); Wanted.SetHeat(copy, copy.Player, f, 0f, 0f);
        var r1 = new SimulationRunner(); var r2 = new SimulationRunner();
        for (int i = 0; i < 3000; i++) { r1.Step(u); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        // Die Schuld ist Spielerpolitik: der Hash ohne Spielerpolitik sieht sie nicht
        var c = World(); var d = World();
        Wanted.Report(c, c.PlayerActor, f, "smuggling");
        Assert.NotEqual(UniverseHash.Compute(d, true, includePlayerPolitics: false), UniverseHash.Compute(c, true, includePlayerPolitics: false));   // Fahndung zählt weiter mit
        c.Player.Wanted.Clear(); d.Player.Wanted.Clear();
        Assert.Equal(UniverseHash.Compute(d, true, includePlayerPolitics: false), UniverseHash.Compute(c, true, includePlayerPolitics: false));
    }
}
