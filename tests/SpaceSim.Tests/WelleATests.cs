using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Welle A (Spieltest-Korrekturen nach den Spielerbot-Durchläufen): Angriffsbefehl der Flotte (einmal melden, endet mit dem Krieg), Tribut nur wenn die KI
/// vorne liegt, gedeckelte Spielerkasse in der Kriegsstärke, leere Kasse am Zoll = kein Durchflug ohne Verbrechen, Jäger-Startpaket, Kopfgeld nur auf
/// erreichbare Ziele.
/// </summary>
public class WelleATests
{
    /// <summary>Referenzhash-Tests: alle Schalter der Welle A aus (alte Regeln).</summary>
    internal static void RevertWelleA(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.FleetAttackOnce = false;
        r.PlayerTributeOnlyIfAiAhead = false;
        r.PlayerWarMoneyShare = 0f;
        r.PlayerWarMoneyCap = 0f;
        r.GateBrokeNoEvasion = false;
        r.BountyReachableTargets = false;
    }

    static Universe World()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        u.PlayerWallet.Credits = 50_000f;
        return u;
    }

    static Station St(Universe u, int id) => u.Stations[new StationId(id)];

    static Ship Own(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        s.HomeBase = at.Id;
        u.AddShip(s);
        s.RecomputeStats(u);
        return s;
    }

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

    [Fact]
    public void Schalter_CodeAus_WeltAn_RevertAus()
    {
        var c = new EconomyRules();
        Assert.False(c.FleetAttackOnce); Assert.False(c.PlayerTributeOnlyIfAiAhead); Assert.Equal(0f, c.PlayerWarMoneyShare);
        Assert.Equal(0f, c.PlayerWarMoneyCap); Assert.False(c.GateBrokeNoEvasion); Assert.False(c.BountyReachableTargets);
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var r = def.Rules!;
        Assert.True(r.FleetAttackOnce && r.PlayerTributeOnlyIfAiAhead && r.GateBrokeNoEvasion && r.BountyReachableTargets);
        Assert.True(r.PlayerWarMoneyShare > 0f && r.PlayerWarMoneyCap > 0f);
        RevertWelleA(def);
        var u = WorldBuilder.Build(def);
        Assert.False(u.Rules.FleetAttackOnce || u.Rules.GateBrokeNoEvasion || u.Rules.BountyReachableTargets || u.Rules.PlayerTributeOnlyIfAiAhead);
    }

    // ───────── Punkt 1: Angriffsbefehl ─────────

    static (int Crimes, int Started) AttackStation(bool once, int ticks)
    {
        var u = World();
        u.Rules.FleetAttackOnce = once;
        var st = St(u, 17);
        var falcon = Own(u, "falcon", St(u, 18));
        var ev = Capture(u);
        Assert.True(Commands.Give(u, falcon, new AttackCommand(default, st.Id, default, st.Sector), out var m), m);
        Assert.True(RunUntil(u, () => u.Fights.IsEngaged(falcon.Id), 3000), "Belagerung beginnt");
        new SimulationRunner().Run(u, ticks);
        return (ev.Count(x => x is CrimeReported c && c.Actor == u.PlayerActor && c.Crime == "assault_station"),
                ev.Count(x => x is FleetAttackStarted f && f.Ship == falcon.Id));
    }

    [Fact]
    public void Angriff_Station_EinVerbrechenEineMeldung()
    {
        var (crimes, started) = AttackStation(once: true, ticks: 600);
        Assert.Equal(1, crimes);
        Assert.Equal(1, started);
    }

    [Fact]
    public void Angriff_Station_AlteRegelMeldetMehrfach()
    {
        var (crimes, started) = AttackStation(once: false, ticks: 600);
        Assert.True(crimes > 100 && started > 100, $"alt: {crimes} Verbrechen, {started} Meldungen");   // der Fehler aus dem Spieltest (gemessen 543/543)
    }

    [Fact]
    public void Angriff_GleicheBelagerung_KeinZweiterBeitritt()
    {
        var u = World();
        var st = St(u, 17);
        var falcon = Own(u, "falcon", St(u, 18));
        Assert.True(Commands.Give(u, falcon, new AttackCommand(default, st.Id, default, st.Sector), out var m), m);
        Assert.True(RunUntil(u, () => u.Fights.IsEngaged(falcon.Id), 3000));
        var target = FleetAttack.SiegeTargetOf(u, (AttackCommand)falcon.Command!)!;
        var e = u.Fights.Of(falcon.Id)!;
        int before = e.Participants.Count;
        var ev = Capture(u);
        for (int i = 0; i < 5; i++) FleetAttack.Besiege(u, falcon, target);
        Assert.Equal(before, e.Participants.Count);
        Assert.DoesNotContain(ev, x => x is CrimeReported || x is FleetAttackStarted);
    }

    [Fact]
    public void Angriff_ImKrieg_EndetMitFrieden()
    {
        var u = World();
        var st = St(u, 17);
        var owner = u.FactionOf(st.Owner)!.Value;
        u.PlayerWallet.Credits = 1_000_000f;
        Wars.PlayerDeclare(u, owner);
        var falcon = Own(u, "falcon", St(u, 18));
        Assert.True(Commands.Give(u, falcon, new AttackCommand(default, st.Id, default, st.Sector), out var m), m);
        Assert.True(((AttackCommand)falcon.Command!).War);
        new SimulationRunner().Run(u, 30);
        Wars.MakePeace(u, Wars.PlayerWar(u, owner)!, "negotiated");
        // Gefecht kann laufen (Befehl ruht) — nach dem Austritt endet der Befehl im nächsten Schritt
        Assert.True(RunUntil(u, () => falcon.Command is null, 3000), "Befehl endet nach dem Frieden");
    }

    [Fact]
    public void Angriff_ImKrieg_EndetMitWaffenstillstand_SchiffsZiel()
    {
        var u = World();
        var st = St(u, 17);
        var owner = u.FactionOf(st.Owner)!.Value;
        u.PlayerWallet.Credits = 1_000_000f;
        var war = Wars.PlayerDeclare(u, owner);
        var police = u.ShipsById().First(s => ThreatMap.IsPolice(s) && u.FactionOf(s) == owner);
        var falcon = Own(u, "falcon", St(u, 18));
        Assert.True(Commands.Give(u, falcon, new AttackCommand(police.Id, default, default, ThreatMap.Where(u, police).Sector), out var m), m);
        Assert.True(((AttackCommand)falcon.Command!).War);
        war.CeasefireUntil = u.Tick + 5000;
        Assert.True(RunUntil(u, () => falcon.Command is null, 3000), "Befehl endet mit dem Waffenstillstand");
    }

    [Fact]
    public void Angriff_SpeichernLaden_FlaggenBleiben()
    {
        var u = World();
        var st = St(u, 17);
        var falcon = Own(u, "falcon", St(u, 18));
        Assert.True(Commands.Give(u, falcon, new AttackCommand(default, st.Id, default, st.Sector), out var m), m);
        Assert.True(RunUntil(u, () => u.Fights.IsEngaged(falcon.Id), 3000));
        Assert.True(((AttackCommand)falcon.Command!).Reported);
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.True(((AttackCommand)v.Ships[falcon.Id].Command!).Reported);
        new SimulationRunner().Run(u, 100); new SimulationRunner().Run(v, 100);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    // ───────── Punkt 2: Tribut ─────────

    [Fact]
    public void Tribut_SpielerVorne_FriedenKostenlos_KiVorne_Tribut()
    {
        var u = World();
        var f = new FactionId(1);
        u.PlayerWallet.Credits = 100_000f;
        var w = Wars.PlayerDeclare(u, f);
        u.Tick += u.Rules.WarMinTicks + 1;
        // Spieler gleichauf oder vorne (KI verliert mehr): kostenlos — Vorschau und Ausführung gleich
        w.LossA = w.A == f ? 50_000f : 0f; w.LossB = w.B == f ? 50_000f : 0f;
        Assert.Equal(0f, Wars.TributeFor(u, w, f));
        var prev = new SpaceSim.Core.Comms.PeaceAction().Preview(u, $"peace:{f.Value}");
        Assert.Equal(0f, prev.Cost);
        Assert.True(prev.Possible, prev.Reason);
        float before = u.PlayerWallet.Credits;
        Assert.Equal(0f, Wars.PeaceAccept(u, f));
        Assert.Equal(before, u.PlayerWallet.Credits);
        // KI klar vorne und ohne mehr Verluste: Tribut wie bisher
        var u2 = World();
        u2.PlayerWallet.Credits = 0f;   // kein Geld, keine Schiffe → Spieler schwach
        var w2 = new War { A = u2.PlayerFaction!.Value, B = f, Since = u2.Tick };
        u2.Wars.Add(w2);
        Assert.True(Wars.AiAhead(u2, w2, f));
        Assert.True(Wars.TributeFor(u2, w2, f) > 0f);
        // Regel aus: auch bei Spielervorsprung die alte Formel (Verluste machen es teurer)
        u.Rules.PlayerTributeOnlyIfAiAhead = false;
        var w3 = new War { A = u.PlayerFaction!.Value, B = f, Since = u.Tick, LossB = 50_000f };
        Assert.True(Wars.TributeFor(u, w3, f) > 0f);
    }

    // ───────── Punkt 3: Kriegsstärke mit gedeckelter Spielerkasse ─────────

    [Fact]
    public void Staerke_ReicheSpielerkasse_Gedeckelt_KiKannErklaeren()
    {
        var u = World();
        u.Rules.AiWarOnPlayer = true;
        var me = u.PlayerFaction!.Value;
        var f = new FactionId(1);
        u.PlayerWallet.Credits = 2_000_000f;
        Assert.True(Wars.Strength(u, me) <= u.Rules.PlayerWarMoneyCap / u.Rules.WarStrengthMoneyScale + 30f);
        Relations.Set(u, f, me, -1f);
        u.Actors[f].Treasury.Credits = 300_000f;
        Assert.Null(Wars.DeclareIssue(u, f, me));   // jetzt möglich
        // alte Regel: ganze Kasse zählt → „Klar unterlegen“
        u.Rules.PlayerWarMoneyShare = 0f; u.Rules.PlayerWarMoneyCap = 0f;
        Assert.Equal("Klar unterlegen.", Wars.DeclareIssue(u, f, me));
        // nicht bei jeder Kleinigkeit: neutrale Beziehung → kein Kriegsgrund
        u.Rules.PlayerWarMoneyShare = 0.2f; u.Rules.PlayerWarMoneyCap = 200_000f;
        Relations.Set(u, f, me, 0f);
        Assert.Equal("Kein Kriegsgrund.", Wars.DeclareIssue(u, f, me));
    }

    // ───────── Punkt 4: Zoll mit leerer Kasse, Jäger-Startpaket ─────────

    static (Universe U, Ship Ship, Gate Gate) TollWorld(float credits)
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        // Zollstufe 1 im Zielsektor eines Tors (gegen jeden außer dem Besitzer — der Spieler ist eigene Fraktion)
        var gate = u.Gates.Values.Where(g => g.Sector == player.Sector).OrderBy(g => g.Id.Value)
            .First(g => FactionPolicySystem.SetLevel(u, u.Sectors[u.Gates[g.Target].Sector], 1) == 1);
        u.PlayerWallet.Credits = credits;
        return (u, player, gate);
    }

    [Fact]
    public void Zoll_LeereKasse_VerweigertOhneVerbrechen()
    {
        var (u, ship, gate) = TollWorld(0f);
        var ev = Capture(u);
        var j = GateAccess.TryPlayerJump(u, ship, gate);
        Assert.False(j.Allowed);
        Assert.Contains("Kasse ist leer", j.Message);
        Assert.DoesNotContain(ev, x => x is CrimeReported);
        // Regel aus: Zollflucht wie bisher
        var (u2, ship2, gate2) = TollWorld(0f);
        u2.Rules.GateBrokeNoEvasion = false;
        var ev2 = Capture(u2);
        Assert.True(GateAccess.TryPlayerJump(u2, ship2, gate2).Allowed);
        Assert.Contains(ev2, x => x is CrimeReported c && c.Crime == "toll_evasion");
    }

    [Fact]
    public void Jaeger_Startpaket_GeldUndFrachtraum_SchnellerAlsKorsar()
    {
        var u = WorldBuilder.BuildDefault(startOption: "fighter");
        var p = u.Ships[u.PlayerShip];
        Assert.Equal("falcon", p.ModelKey);
        Assert.Equal(2000f, u.PlayerWallet.Credits);
        Assert.True(p.CargoCapacity >= 60f);
        var corsair = u.ShipModels["corsair"];
        var cs = ShipStats.Compute(u.ShipClasses[corsair.ClassKey], corsair.Fit.Select(k => k is null ? null : u.Modules[k]).ToList());
        Assert.True(p.Stats.Speed > cs.Speed, $"Falke {p.Stats.Speed} > Korsar {cs.Speed}");
        Assert.True(p.FreeUnits(u, u.FindGood("iron_ore")!.Id) >= 60f);
    }

    // ───────── Punkt 5: Kopfgeld ─────────

    [Fact]
    public void Kopfgeld_KeinAngebotFuerZielImVersteck()
    {
        var u = World();
        var raiders = u.ShipsById().Where(ThreatMap.IsRaider).ToList();
        Assert.NotEmpty(raiders);
        foreach (var s in raiders)
        {
            var home = u.Stations[s.HomeBase];
            if (s.Transit is not null) u.FinishTransit(s);
            u.MoveShip(s, home.Sector);
            s.Position = home.DockPoint(s.Id);
            s.DockedAt = home.Id;
            Assert.True(BountyMission.Sheltered(u, s));
        }
        var hq = u.StationsById().First(st => BountyMission.IsOfferStation(u, st) && u.FactionOf(st.Owner) is { } f && u.IsNpcFaction(f));
        var type = new BountyMission();
        for (long b = 0; b < 40; b++)
            Assert.Null(type.Generate(u, hq, MissionRng.For(u, hq.Id.Value, b), b));
    }

    [Fact]
    public void Kopfgeld_ZielNieAusDemVersteck_ScheiternOhneAnsehensverlust()
    {
        var u = World();
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        var home = u.Stations[raider.HomeBase];
        if (raider.Transit is not null) u.FinishTransit(raider);
        u.MoveShip(raider, home.Sector);
        raider.Position = home.DockPoint(raider.Id);
        raider.DockedAt = home.Id;
        var hq = u.StationsById().First(st => BountyMission.IsOfferStation(u, st) && u.FactionOf(st.Owner) is { } f && u.IsNpcFaction(f));
        var f = u.FactionOf(hq.Owner)!.Value;
        var m = new Mission { Type = BountyMission.KeyId, Client = hq.Owner, At = hq.Id, Deadline = u.Tick + 10, Reserved = 500f, Deposit = 50f };
        m.Args["target"] = raider.Id.Value.ToString();
        m.Args["actor"] = raider.Owner.Value.ToString();
        m.Args["lastSector"] = home.Sector.Value.ToString();
        m.Args["lastX"] = "0"; m.Args["lastY"] = "0";
        u.Missions.Add(m);
        float rel = Relations.Effective(u, f, u.PlayerFaction!.Value);
        float wallet = u.PlayerWallet.Credits;
        u.Tick += 20;
        new BountyMission().Check(u, m);
        Assert.Equal(MissionState.Failed, m.State);
        Assert.Equal(rel, Relations.Effective(u, f, u.PlayerFaction!.Value), 4);
        Assert.Equal(wallet + 50f, u.PlayerWallet.Credits, 2);   // Pfand zurück
        Assert.False(u.MissionFactions.TryGetValue(hq.Owner.Value, out var fs) && fs.BlockedUntil > u.Tick);
    }

    [Fact]
    public void NpcWelt_SchalterAusGleicherHash()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var a = WorldBuilder.Build(def);
        var def2 = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        RevertWelleA(def2);
        var b = WorldBuilder.Build(def2);
        new SimulationRunner().Run(a, 3000);
        new SimulationRunner().Run(b, 3000);
        Assert.Equal(UniverseHash.Compute(a, true, false), UniverseHash.Compute(b, true, false));
    }
}
