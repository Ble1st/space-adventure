using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Sprungantrieb und Tarnfeld (SCHIFFSSLOTS.md Abschnitte 4 und 6): Inhalt, Aufladen/Abklingen, Massenschatten, Ankunftsort, Reichweite in Hops über
/// geschlossene Tore, Politik (Abriegelung, gesperrte Einreise, Zoll, Schmuggel als Verbrechen), Treibstoff; Tarnfeld gegen Kaperer, Polizei, Plattformen
/// und Kontrollpunkte, Brechen, Leistung; Spielstand bitgleich; NPC-Welt ohne den Inhalt = Referenz-Hash.
/// </summary>
public class SprungTarnTests
{
    static readonly SectorId Alpha = new(1), Beta = new(2), Gamma = new(3), Delta = new(4), Hephaistos = new(7), Aurelia = new(8);
    static readonly string[] Keys = { "jump_drive_s", "cloak_s", "cloak_s_mk2" };

    static List<SimEvent> Capture(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    /// <summary>Sprungantrieb und Tarnfeld zurücknehmen (für die Referenz-Hash-Tests über <see cref="Runde44ScaleTests.RevertRunde44"/>).</summary>
    internal static void RevertSprungTarn(WorldDef def)
    {
        def.Rules ??= new EconomyRules();
        def.Rules.BlackMarketNpc = false;   // die Referenz stammt von vor „NPCs beim Hehler“ (docs/SCHWARZMARKT.md)
        def.Modules.RemoveAll(m => Keys.Contains(m.Key));
        foreach (var t in def.StationTypes) if (t.Stocks is { } st) foreach (var k in Keys) st.Remove(k);
        foreach (var p in def.Research) p.Unlocks.RemoveAll(x => Keys.Any(k => x == "module:" + k));
    }

    /// <summary>Händler-Start (Nomade mit freiem Utility-Slot) ohne Treibstoff/Forschung; Sprungantrieb eingebaut, wahlweise Tarnfeld in einem Zusatzslot.</summary>
    static (Universe u, Ship me) Setup(string? cloak = null, bool jump = true, bool fuel = false, bool research = false)
    {
        var u = TestWorlds.Default(fuel: fuel, research: research, startOption: "trader");
        var me = u.Ships[u.PlayerShip];
        int util = me.Slots.FindIndex(s => s.Type == "utility");
        Assert.True(util >= 0);
        me.Fit[util] = jump ? "jump_drive_s" : null;
        if (cloak is not null)
        {
            me.Slots.Add(new ClassSlot { Type = "utility", Size = SlotSize.S });
            me.Fit.Add(cloak);
        }
        me.RecomputeStats(u);
        Place(u, me, Beta, Clear(u, Beta));
        u.PlayerSector = Beta;
        return (u, me);
    }

    static void Place(Universe u, Ship s, SectorId sector, Vec2 pos)
    {
        if (s.Transit is not null) u.FinishTransit(s);
        s.Order = null; s.Phase = TradePhase.Idle; s.MinePhase = MinePhase.Idle;
        if (s.Sector != sector) u.MoveShip(s, sector);
        s.Position = pos;
        s.DockedAt = default;
    }

    /// <summary>Eine Stelle ohne Massenschatten (feste Suchreihenfolge).</summary>
    static Vec2 Clear(Universe u, SectorId sector)
    {
        for (int r = 100; r < 3000; r += 40)
            for (int k = 0; k < 16; k++)
            {
                var p = Vec2.FromAngle(k * MathF.PI / 8f) * r;
                if (JumpDrive.ShadowAt(u, sector, p) is null) return p;
            }
        throw new InvalidOperationException("keine freie Stelle");
    }

    /// <summary>Nur das Sprung-/Tarnsystem laufen lassen (der Rest der Welt steht).</summary>
    static void Tick(Universe u, int n = 1)
    {
        var sys = new JumpCloakSystem();
        for (int i = 0; i < n; i++) { sys.Step(u, 1); u.Tick++; }
    }

    // ───────── Inhalt ─────────

    [Fact]
    public void Inhalt_ModuleWarenSystemwerkWerftregalForschung()
    {
        var u = TestWorlds.Default(fuel: false, research: false);
        var jd = u.Modules["jump_drive_s"];
        Assert.Equal("utility", jd.Slot);
        Assert.Contains(Capability.Jump, jd.Grants);
        Assert.Contains(Capability.Stealth, u.Modules["cloak_s"].Grants);
        Assert.Equal("cloak_s_mk2", u.Modules["cloak_s"].UpgradeTo);
        foreach (var k in Keys)
        {
            Assert.NotNull(u.FindGood(k));   // Modul ist Ware
            Assert.Contains(u.StationTypes.Values, t => t.Key == "systems_works" && t.Recipes.Any(r => r.Outputs.ContainsKey(u.FindGood(k)!.Id)));
            Assert.Contains(k, u.StationTypes["shipyard"].Stocks.Select(s => u.Goods[s.Good].Key));
        }
        // Forschung: keine Sperre (eine Freischaltung in „Fusionstechnik Mk3“ hätte die eingefrorene v15→v16-Migrationskette verändert, siehe SCHIFFSSLOTS.md 4)
        var r = TestWorlds.Default(fuel: false, research: true);
        foreach (var k in Keys) Assert.True(Research.IsUnlocked(r, new FactionId(1), UnlockKind.Module, k));
    }

    [Fact]
    public void Werte_AusDemFit_UndLeistung()
    {
        var (u, me) = Setup(cloak: "cloak_s");
        Assert.Equal(2, me.Stats.JumpRange);
        Assert.Equal(60f, me.Stats.JumpChargeTicks);
        Assert.Equal(1200f, me.Stats.JumpCooldownTicks);
        Assert.Equal(1.5f, me.Stats.Cloak);
        Assert.True(me.Has(Capability.Jump | Capability.Stealth));
        float draw = me.Stats.PowerDraw;
        Assert.Null(Cloak.TurnOn(u, me));
        Assert.Equal(draw + me.Stats.CloakDraw, me.Stats.PowerDraw, 3);   // eingeschaltet kostet Leistung
        Assert.Equal(1.5f, GateAccess.ProfileOf(u, me).Stealth);
        Cloak.Break(u, me, "off");
        Assert.Equal(draw, me.Stats.PowerDraw, 3);
        Assert.Equal(0f, GateAccess.ProfileOf(u, me).Stealth);   // ausgeschaltet keine Tarnung (kein passiver Stealth)
    }

    // ───────── Sprungantrieb ─────────

    [Fact]
    public void Aufladen_Springen_Abklingen()
    {
        var (u, me) = Setup();
        var ev = Capture(u);
        Assert.Null(JumpDrive.Start(u, me, Gamma));
        Assert.Single(ev.OfType<JumpChargeStarted>());
        Tick(u, 59);
        Assert.Equal(Beta, me.Sector);
        Assert.InRange(JumpDrive.Progress(me), 0.97f, 0.99f);
        Tick(u, 1);
        Assert.Equal(Gamma, me.Sector);
        Assert.Equal(Gamma, u.PlayerSector);
        Assert.False(JumpDrive.Charging(me));
        var j = Assert.Single(ev.OfType<ShipDriveJumped>());
        Assert.Equal(me.Position, j.Position);
        Assert.Null(JumpDrive.ShadowAt(u, Gamma, me.Position));   // Ankunft frei vom Massenschatten
        Assert.Contains(me.Id, u.Tracker.ParkedIn(Gamma));
        // Abklingen
        Place(u, me, Gamma, Clear(u, Gamma));
        Assert.StartsWith("kühlt ab", JumpDrive.StartIssue(u, me, Beta));
        Tick(u, 1199);
        Assert.Null(JumpDrive.StartIssue(u, me, Beta));
    }

    [Fact]
    public void Ankunft_DeterministischUndAmTorZumHerkunftssektor()
    {
        Vec2 Run()
        {
            var (u, me) = Setup();
            JumpDrive.Start(u, me, Gamma);
            Tick(u, 60);
            return me.Position;
        }
        var a = Run();
        Assert.Equal(a, Run());
        // Ankunft auf der Seite des Tores Gamma → Beta
        var w = TestWorlds.Default(fuel: false, research: false);
        var gate = w.Sectors[Gamma].Gates.Select(g => w.Gates[g]).First(g => w.Gates[g.Target].Sector == Beta);
        Assert.True(a.Normalized().Dot(gate.Position.Normalized()) > 0.7f);
    }

    [Fact]
    public void Reichweite_InHops_UeberGeschlosseneTore()
    {
        var (u, me) = Setup();
        Assert.Equal(1, JumpDrive.Hops(u, Beta, Gamma));
        Assert.Equal(2, JumpDrive.Hops(u, Beta, Aurelia));
        Assert.Equal(2, JumpDrive.Hops(u, Beta, new SectorId(11)));
        Assert.Equal(3, JumpDrive.Hops(u, Alpha, Aurelia));
        Place(u, me, Alpha, Clear(u, Alpha));
        Assert.StartsWith("außer Reichweite (3 Sprünge", JumpDrive.StartIssue(u, me, Aurelia));
        Place(u, me, Beta, Clear(u, Beta));
        Assert.Null(JumpDrive.StartIssue(u, me, Aurelia));
        // Tor Gamma–Aurelia (Sackgasse) geschlossen: kein Gate-Weg, der Antrieb springt trotzdem
        foreach (var g in u.Gates.Values.Where(g => (g.Sector == Gamma && u.Gates[g.Target].Sector == Aurelia) || (g.Sector == Aurelia && u.Gates[g.Target].Sector == Gamma)).ToList())
            GateAccess.SetState(u, g, GateState.Closed);
        Assert.Null(u.FindRoute(Beta, Aurelia));
        Assert.Equal(2, JumpDrive.Hops(u, Beta, Aurelia));
        Assert.Null(JumpDrive.Start(u, me, Aurelia));
        Tick(u, 60);
        Assert.Equal(Aurelia, me.Sector);
        Assert.Contains(JumpDrive.Targets(u, me), t => t.Sector == Gamma);
    }

    [Fact]
    public void Massenschatten_StartAbgewiesen_BewegungBrichtAb()
    {
        var (u, me) = Setup();
        var st = u.Stations[u.Sectors[Beta].Stations[0]];
        me.Position = st.Position + new Vec2(st.Radius + 20f, 0f);
        Assert.StartsWith("Massenschatten: Station", JumpDrive.StartIssue(u, me, Gamma));
        var gate = u.Gates[u.Sectors[Beta].Gates[0]];
        me.Position = gate.Position + new Vec2(30f, 0f);
        Assert.Contains("Massenschatten", JumpDrive.StartIssue(u, me, Gamma));
        me.Position = Clear(u, Beta);
        var ev = Capture(u);
        Assert.Null(JumpDrive.Start(u, me, Gamma));
        Tick(u, 10);
        me.Position += new Vec2(10f, 0f);   // Schiff fliegt los
        Tick(u, 1);
        Assert.False(JumpDrive.Charging(me));
        Assert.Equal("moved", Assert.Single(ev.OfType<JumpChargeAborted>()).Key);
        Assert.Equal(Beta, me.Sector);
    }

    [Theory]
    [InlineData("docked")]
    [InlineData("hit")]
    [InlineData("module")]
    [InlineData("switch")]
    public void Abbruch_DurchAndockenTrefferModulSchiffswechsel(string what)
    {
        var (u, me) = Setup();
        var ev = Capture(u);
        Assert.Null(JumpDrive.Start(u, me, Gamma));
        Tick(u, 5);
        switch (what)
        {
            case "docked": me.DockedAt = u.Sectors[Beta].Stations[0]; break;
            case "hit": Hit.Apply(u, me, new HitResult { Hit = true, HullDamage = 1f }); break;
            case "module": ShipSystems.SetCondition(u, me, me.Fit.IndexOf("jump_drive_s"), 0.1f); break;
            case "switch": u.PlayerShip = default; break;
        }
        Tick(u, 70);
        Assert.Equal(Beta, me.Sector);
        Assert.False(JumpDrive.Charging(me));
        Assert.Single(ev.OfType<JumpChargeAborted>());
    }

    [Fact]
    public void Leistung_ZuWenigUeberschuss_LadenAbgewiesen()
    {
        var (u, me) = Setup(cloak: "cloak_s");
        Assert.Null(Cloak.TurnOn(u, me));   // Nomade: 10 Leistung, 6+2 Dauerlast, Tarnung 2 → kein Rest für das Laden (2)
        Assert.StartsWith("zu wenig Leistung", JumpDrive.StartIssue(u, me, Gamma));
        Cloak.Break(u, me, "off");
        Assert.Null(JumpDrive.StartIssue(u, me, Gamma));
    }

    [Fact]
    public void Politik_AbriegelungUndGesperrteEinreise_NurStarkeTarnungKommtDurch()
    {
        // Einreise Stufe 4 in Gamma: niemand außer Mitgliedern
        var (u, me) = Setup(cloak: "cloak_s");
        u.Sectors[Gamma].Access.EntryLevel = 4;
        u.TouchAccess();
        Assert.StartsWith("verweigert", JumpDrive.StartIssue(u, me, Gamma));
        Cloak.TurnOn(u, me);   // Mk1 (1,5) schlägt die Gate-Sensoren (1), nicht die Sprungspur (2)
        Assert.StartsWith("zu wenig Leistung", JumpDrive.StartIssue(u, me, Gamma) ?? "");
        // Mk2 (2,5) mit einem stärkeren Reaktor
        var (v, ship) = Setup(cloak: "cloak_s_mk2");
        int reactor = ship.Slots.FindIndex(s => s.Type == "reactor");
        ship.Fit[reactor] = "reactor_mk3"; ship.RecomputeStats(v);
        v.Sectors[Gamma].Access.EntryLevel = 4;
        v.TouchAccess();
        Assert.StartsWith("verweigert", JumpDrive.StartIssue(v, ship, Gamma));
        Assert.Null(Cloak.TurnOn(v, ship));
        Assert.Null(JumpDrive.StartIssue(v, ship, Gamma));
        Assert.Null(JumpDrive.Start(v, ship, Gamma));
        Tick(v, 60);
        Assert.Equal(Gamma, ship.Sector);
        // Abriegelung der Ausreise (Stufe 5) hält auch den Sprung fest
        var (w, me3) = Setup();
        w.Sectors[Beta].Access.ExitLevel = 5;
        w.TouchAccess();
        Assert.Contains("Abriegelung", JumpDrive.StartIssue(w, me3, Gamma));
        // Hauptroute-Ausnahme gilt nur am Gate: Stufe 4 auf der Hauptroute lässt am Gate Zoll zu, den Sprung nicht
        var (x, me4) = Setup();
        x.Sectors[Gamma].Access.EntryLevel = 4;
        x.TouchAccess();
        var mainGate = x.Sectors[Beta].Gates.Select(g => x.Gates[g]).First(g => x.Gates[g.Target].Sector == Gamma);
        Assert.True(mainGate.MainRoute);
        Assert.True(GateAccess.Evaluate(x, mainGate, GateAccess.ProfileOf(x, me4), me4).Allowed);
        Assert.NotNull(JumpDrive.StartIssue(x, me4, Gamma));
    }

    [Fact]
    public void Zoll_BeimSprung_InDieFraktionskasse()
    {
        var (u, me) = Setup();
        u.Sectors[Gamma].Access.EntryLevel = 1;
        u.TouchAccess();
        u.WalletOf(me).Credits = 1000f;
        var owner = u.Actors[u.Sectors[Gamma].Owner];
        float before = owner.Treasury.Credits;
        Assert.Null(JumpDrive.Start(u, me, Gamma));
        Tick(u, 60);
        Assert.Equal(Gamma, me.Sector);
        float toll = u.Rules.GateTollPerLevel;
        Assert.Equal(1000f - toll, u.WalletOf(me).Credits, 2);
        Assert.Equal(before + toll, owner.Treasury.Credits, 2);
    }

    [Fact]
    public void Schmuggelware_BeiDerAnkunft_IstVerbrechen()
    {
        var (u, me) = Setup();
        u.Rules.Contraband.Add("food");
        u.Sectors[Gamma].Access.EntryLevel = 1;
        u.TouchAccess();
        var food = u.FindGood("food")!.Id;
        me.Cargo[food] = 5f;
        Assert.StartsWith("verweigert", JumpDrive.StartIssue(u, me, Gamma));   // vor dem Laden: abgewiesen, kein Verbrechen
        Assert.Equal(0, Wanted.LevelOf(u, me.Owner, u.Sectors[Gamma].Owner));
        me.Cargo.Remove(food);
        var ev = Capture(u);
        Assert.Null(JumpDrive.Start(u, me, Gamma));
        Tick(u, 30);
        me.Cargo[food] = 5f;   // Ware während des Ladens an Bord genommen
        Tick(u, 30);
        Assert.Equal(Beta, me.Sector);
        var ab = Assert.Single(ev.OfType<JumpChargeAborted>());
        Assert.Equal("denied", ab.Key);
        Assert.Contains("Schmuggel", ab.Reason);
        Assert.True(Wanted.LevelOf(u, me.Owner, u.Sectors[Gamma].Owner) >= 1);
    }

    [Fact]
    public void Treibstoff_NurMitSchalter()
    {
        var (u, me) = Setup(fuel: true);
        float before = me.TotalFuel;
        Assert.Null(JumpDrive.Start(u, me, Gamma));
        Tick(u, 60);
        Assert.Equal(before - 20f, me.TotalFuel, 2);
        var (v, ship) = Setup(fuel: false);
        float b2 = ship.TotalFuel;
        JumpDrive.Start(v, ship, Gamma);
        Tick(v, 60);
        Assert.Equal(Gamma, ship.Sector);
        Assert.Equal(b2, ship.TotalFuel);
        // zu wenig Treibstoff
        var (w, s3) = Setup(fuel: true);
        s3.Fuel.Clear();
        Assert.StartsWith("zu wenig Treibstoff", JumpDrive.StartIssue(w, s3, Gamma));
    }

    // ───────── Tarnfeld ─────────

    [Fact]
    public void Tarnung_NichtEinschaltbar_AngedocktGefechtWarnungLeistung()
    {
        var (u, me) = Setup(cloak: "cloak_s", jump: false);
        me.DockedAt = u.Sectors[Beta].Stations[0];
        Assert.Equal("angedockt", Cloak.ToggleIssue(u, me));
        me.DockedAt = default;
        u.PendingEncounters.Add(new PendingEncounter { Kind = EncounterKind.Raid, Target = me.Id, Sector = Beta, DueTick = u.Tick + 60 });
        Assert.Equal("Warnung läuft", Cloak.ToggleIssue(u, me));
        u.PendingEncounters.Clear();
        Assert.Null(Cloak.ToggleIssue(u, me));
        // Pip: Überschuss 2 − Leerlast 1 < Tarnlast 2
        var p = TestWorlds.Default(fuel: false, research: false);
        var pip = p.Ships[p.PlayerShip];
        pip.ModelKey = "pip"; pip.Slots.Clear(); pip.Fit = new List<string?>(p.ShipModels["pip"].Fit); pip.RecomputeStats(p);
        pip.Fit[pip.Slots.FindIndex(s => s.Type == "utility")] = "cloak_s"; pip.RecomputeStats(p);
        Assert.StartsWith("zu wenig Leistung", Cloak.ToggleIssue(p, pip));
    }

    [Theory]
    [InlineData("docked")]
    [InlineData("hit")]
    [InlineData("mining")]
    [InlineData("module")]
    [InlineData("switch")]
    public void Tarnung_Bricht(string what)
    {
        var (u, me) = Setup(cloak: "cloak_s", jump: false);
        var ev = Capture(u);
        Assert.Null(Cloak.TurnOn(u, me));
        switch (what)
        {
            case "docked": me.DockedAt = u.Sectors[Beta].Stations[0]; break;
            case "hit": Hit.Apply(u, me, new HitResult { Hit = true, ShieldDamage = 1f }); break;
            case "mining": me.AnchorRock = 1; break;
            case "module": ShipSystems.SetCondition(u, me, me.Fit.IndexOf("cloak_s"), 0.1f); break;
            case "switch": u.PlayerShip = default; break;
        }
        Tick(u, 1);
        Assert.False(me.Cloaked);
        var off = Assert.Single(ev.OfType<CloakChanged>(), c => !c.On);
        Assert.Equal(what == "switch" ? "ship" : what, off.Reason);
    }

    [Fact]
    public void Tarnung_Gefechtsbeginn_Bricht()
    {
        var (u, me) = Setup(cloak: "cloak_s", jump: false);
        Cloak.TurnOn(u, me);
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        Place(u, raider, Beta, me.Position + new Vec2(20f, 0f));
        Engage.StartRaid(u, raider, me, u.Sectors[Beta]);
        Assert.False(me.Cloaked);
    }

    [Fact]
    public void Tarnung_KaperWaehlenGetarnteNichtAlsOpfer()
    {
        var (u, me) = Setup(cloak: "cloak_s", jump: false);
        u.Rules.PlayerRaids = true;
        me.Cargo[u.FindGood("consumer_goods")!.Id] = 40f;
        Assert.False(ThreatMap.NearStation(u, Beta, me.Position));
        Assert.Contains(PirateSystem.Victims(u, Beta), v => v.Ship.Id == me.Id);
        Cloak.TurnOn(u, me);   // Mk1 1,5 > Kaperer-Sensoren 1
        Assert.DoesNotContain(PirateSystem.Victims(u, Beta), v => v.Ship.Id == me.Id);
    }

    static (Universe u, Ship me, Ship police) Wanted3(string cloak)
    {
        var (u, me) = Setup(cloak: cloak, jump: false);
        int reactor = me.Slots.FindIndex(s => s.Type == "reactor");
        me.Fit[reactor] = "reactor_mk3"; me.RecomputeStats(u);
        var union = u.Sectors[Alpha].Owner;
        var police = SecuritySystem.PoliceOf(u, union).OrderBy(s => s.Id.Value).First();
        var pos = Clear(u, Alpha);
        Place(u, me, Alpha, pos);
        u.PlayerSector = Alpha;
        Place(u, police, Alpha, pos + new Vec2(40f, 0f));
        if (police.Command is not null) Commands.Cancel(u, police);
        Wanted.Report(u, u.PlayerActor, union, "assault");
        Assert.True(Wanted.LevelOf(u, u.PlayerActor, union) >= 3);
        return (u, me, police);
    }

    [Theory]
    [InlineData("cloak_s", true)]       // Mk1 1,5 < Polizei-Sensoren 2: bemerkt
    [InlineData("cloak_s_mk2", false)]  // Mk2 2,5: unbemerkt
    public void Tarnung_Polizei_NurMk2Unbemerkt(string cloak, bool warned)
    {
        var (u, me, _) = Wanted3(cloak);
        Assert.Null(Cloak.TurnOn(u, me));
        var ev = Capture(u);
        var sec = new SecuritySystem();
        u.Tick++; sec.Step(u, 1);
        Assert.Equal(warned, ev.Any(e => e is EncounterWarning { Kind: EncounterKind.Intercept }));
    }

    [Fact]
    public void Tarnung_KontrollpunktAmGate_Mk1Unentdeckt()
    {
        var (u, me) = Setup(cloak: "cloak_s", jump: false);
        // Nebentor Hephaistos → Gamma, Gamma sperrt die Einreise (Stufe 4)
        u.Sectors[Gamma].Access.EntryLevel = 4;
        u.TouchAccess();
        var gate = u.Gates.Values.First(g => g.Sector == Hephaistos && u.Gates[g.Target].Sector == Gamma);
        Assert.False(gate.MainRoute);
        Assert.False(GateAccess.Evaluate(u, gate, GateAccess.ProfileOf(u, me), me).Allowed);
        Cloak.TurnOn(u, me);
        var pass = GateAccess.Evaluate(u, gate, GateAccess.ProfileOf(u, me), me);
        Assert.True(pass.Allowed);
        Assert.Equal(0f, pass.Toll);
    }

    [Fact]
    public void Tarnung_Plattform_WarntNurUngetarnteOderMk1()
    {
        foreach (var (cloak, warned) in new[] { ((string?)null, true), ("cloak_s_mk2", false) })
        {
            var (u, me) = Setup(cloak: cloak ?? "cloak_s", jump: false);
            int reactor = me.Slots.FindIndex(s => s.Type == "reactor");
            me.Fit[reactor] = "reactor_mk3"; me.RecomputeStats(u);
            var platform = u.StationsById().First(s => Outposts.IsOutpost(u, s) && StationCombat.GunRange(u, s) > 0f && u.FactionOf(s) is { } f && u.IsNpcFaction(f));
            Place(u, me, platform.Sector, platform.Position + new Vec2(platform.Radius + 15f, 0f));
            u.PlayerSector = platform.Sector;
            Wanted.Report(u, u.PlayerActor, u.FactionOf(platform)!.Value, "assault");
            if (cloak is not null) Assert.Null(Cloak.TurnOn(u, me));
            var ev = Capture(u);
            u.Tick++; new OutpostDefenseSystem().Step(u, 1);
            Assert.Equal(warned, ev.Any(e => e is EncounterWarning));
        }
    }

    // ───────── Spielstand, Determinismus, NPC-Welt ─────────

    [Fact]
    public void SpeichernLaden_Bitgleich_MitLadenTarnungUndAbklingen()
    {
        var (u, me) = Setup(cloak: "cloak_s", fuel: false);
        int reactor = me.Slots.FindIndex(s => s.Type == "reactor");
        me.Fit[reactor] = "reactor_mk3"; me.RecomputeStats(u);
        var runner = new SimulationRunner();
        Assert.Null(Cloak.TurnOn(u, me));
        Assert.Null(JumpDrive.Start(u, me, Gamma));
        u.Rules.PlayerRaids = false;
        runner.Run(u, 25);
        Assert.True(JumpDrive.Charging(me));
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        Assert.Contains("\"JumpTarget\"", json);
        Assert.Contains("\"Cloaked\":true", json);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        var vme = v.Ships[v.PlayerShip];
        Assert.Equal(me.Stats.PowerDraw, vme.Stats.PowerDraw, 4);   // Last von Tarnung und Laden auch nach dem Laden
        var r2 = new SimulationRunner();
        runner.Run(u, 300); r2.Run(v, 300);
        Assert.Equal(Gamma, me.Sector);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.True(me.JumpCooldownUntil > 0);
    }

    [Fact]
    public void OhneZustand_HashUnveraendert_ZustandNurWennGesetzt()
    {
        var u = TestWorlds.Default(fuel: false, research: false, startOption: "trader");
        ulong h = UniverseHash.Compute(u);
        var me = u.Ships[u.PlayerShip];
        me.JumpCooldownUntil = 5;
        Assert.NotEqual(h, UniverseHash.Compute(u));
        me.JumpCooldownUntil = 0;
        Assert.Equal(h, UniverseHash.Compute(u));
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        Assert.DoesNotContain("JumpTarget", json);
        Assert.DoesNotContain("Cloaked", json);
    }

    /// <summary>
    /// Die NPC-Welt ohne die neuen Module (Inhalt zurückgenommen) läuft bitgleich wie vor dem Sprungantrieb: Referenz 20000 Ticks 1364698EE92E10DC
    /// (Standardwelt wie das Balance-Werkzeug). Die Regeln selbst (System, Haken in Kaperern, Polizei, Plattformen, Gefecht) ändern ohne Zustand nichts.
    /// </summary>
    [Fact]
    public void NpcWelt_OhneDenInhalt_Referenzhash()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        RevertSprungTarn(def);
        var u = WorldBuilder.Build(def);
        new SimulationRunner().Run(u, 20000);
        Assert.Equal(0x1364698EE92E10DCUL, UniverseHash.Compute(u));
    }
}
