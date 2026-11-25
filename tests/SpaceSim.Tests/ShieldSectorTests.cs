using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Ui;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Richtungsschilde / Schildsektoren (KAMPF.md „Richtungsschilde“).</summary>
public class ShieldSectorTests
{
    /// <summary>Referenzhash-Tests: Schalter aus — ein Schildbetrag wie vorher.</summary>
    internal static void RevertShieldSectors(WorldDef def) => (def.Rules ??= new EconomyRules()).ShieldSectors = false;

    static readonly FactionId Union = new(1);

    static Universe World(bool on = true)
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.ShieldSectors = on;
        return WorldBuilder.Build(def);
    }

    static Ship NewShip(Universe u, string model = "falcon")
    {
        var m = u.ShipModels[model];
        var cls = u.ShipClasses[m.ClassKey];
        var yard = u.StationsById().First(s => !s.IsBuildSite);
        var ship = ShipPurchase.Create(u, u.NewShipId(), model, cls.Slots, m.Fit, ShipRole.Warship, Controller.Ai, (ActorId)Union, yard, 2000f);
        u.AddShip(ship);
        ship.DockedAt = default;
        return ship;
    }

    static float SectorSum(Ship s) => Enumerable.Range(0, ShieldSectors.Count).Sum(i => s.Systems.SectorMissing?[i] ?? 0f);

    [Fact]
    public void Schalter_CodeAus_WeltAn_RevertAus()
    {
        Assert.False(new EconomyRules().ShieldSectors);
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        Assert.True(def.Rules!.ShieldSectors);
        RevertShieldSectors(def);
        Assert.False(WorldBuilder.Build(def).Rules.ShieldSectors);
    }

    [Theory]
    [InlineData(ShieldFocus.Balanced)]
    [InlineData(ShieldFocus.Front)]
    [InlineData(ShieldFocus.Back)]
    [InlineData(ShieldFocus.Left)]
    public void Anteile_SummeEins_AusrichtungBevorzugtIhrenSektor(ShieldFocus f)
    {
        var r = new EconomyRules { ShieldSectors = true };
        float sum = 0f;
        for (int i = 0; i < ShieldSectors.Count; i++) sum += ShieldSectors.Share(r, f, i);
        Assert.Equal(1f, sum, 4);
        if (f == ShieldFocus.Balanced) Assert.Equal(0.25f, ShieldSectors.Share(r, f, ShieldSectors.Front), 4);
        else
        {
            int s = (int)f - 1;
            Assert.Equal(r.ShieldFocusShare, ShieldSectors.Share(r, f, s), 4);
            Assert.Equal(r.ShieldFocusOppositeShare, ShieldSectors.Share(r, f, (s + 2) % 4), 4);
        }
    }

    [Fact]
    public void Richtung_AusPositionenRelativZumBug()
    {
        var at = new Vec2(0, 0); var bow = new Vec2(0, -1);
        Assert.Equal(ShieldSectors.Front, ShieldSectors.SectorFrom(at, bow, new Vec2(0, -50)));
        Assert.Equal(ShieldSectors.Back, ShieldSectors.SectorFrom(at, bow, new Vec2(5, 50)));
        Assert.Equal(ShieldSectors.Right, ShieldSectors.SectorFrom(at, bow, new Vec2(50, 3)));
        Assert.Equal(ShieldSectors.Left, ShieldSectors.SectorFrom(at, bow, new Vec2(-50, -3)));
        // gedreht: Bug nach +X, ein Schütze bei +X kommt von vorne
        Assert.Equal(ShieldSectors.Front, ShieldSectors.SectorFrom(at, new Vec2(1, 0), new Vec2(40, 0)));
    }

    [Fact]
    public void LeererSektor_SchadenGehtDurch_AuchWennAndereVoll()
    {
        var u = World();
        var s = NewShip(u);
        float cap = s.Stats.Shield;
        Assert.True(cap > 0f);
        // vorderer Sektor leer schießen
        ShipSystems.TakeShield(u, s, cap, ShieldSectors.Front);
        Assert.Equal(0f, ShieldSectors.ChargeOf(u, s, ShieldSectors.Front), 3);
        Assert.Equal(cap * 0.75f, s.ShieldNow, 2);   // die anderen drei Viertel stehen noch
        Assert.Equal(s.Systems.ShieldMissing, SectorSum(s), 3);
        // ein Treffer von vorne trifft keinen Schild mehr: alles geht auf Panzer/Hülle
        var r = u.Rules;
        var hit = Hit.Damage(20f, DamageType.Energy, new TargetProfile(ShieldSectors.Facing(u, s, ShieldSectors.Front), s.Stats.Armor, 0f, 1f), 1f, r);
        Assert.Equal(0f, hit.ShieldDamage);
        Assert.True(hit.HullDamage > 0f);
        float hull = s.Hull;
        Hit.Apply(u, s, hit, 0f, ShieldSectors.Front);
        Assert.True(s.Hull < hull);
        // von hinten fängt der Schild noch ab
        var back = Hit.Damage(20f, DamageType.Energy, new TargetProfile(ShieldSectors.Facing(u, s, ShieldSectors.Back), s.Stats.Armor, 0f, 1f), 1f, r);
        Assert.True(back.ShieldDamage > 0f);
    }

    [Fact]
    public void SchalterAus_EinSchildbetragWieVorher()
    {
        var u = World(on: false);
        var s = NewShip(u);
        ShipSystems.TakeShield(u, s, s.Stats.Shield * 0.5f, ShieldSectors.Front);
        Assert.Null(s.Systems.SectorMissing);
        Assert.Equal(s.Stats.Shield * 0.5f, s.Systems.ShieldMissing, 3);
        Assert.Equal(s.ShieldNow, ShieldSectors.Facing(u, s, ShieldSectors.Front), 3);
        Assert.Equal(-1, ShieldSectors.Incoming(u, new Encounter(), new CombatParticipant(), null));
        Assert.Null(ShieldSectors.Fill(u, s));
        Assert.False(ShieldSectors.SetFocus(u, s, ShieldFocus.Front));
    }

    [Fact]
    public void Erholung_JeSektor_UndAbgleichNachAuffuellen()
    {
        var u = World();
        var s = NewShip(u);
        float cap = s.Stats.Shield;
        ShipSystems.TakeShield(u, s, cap * 0.2f, ShieldSectors.Back);
        Assert.NotNull(s.Systems.SectorMissing);
        float before = s.Systems.ShieldMissing;
        ShieldSectors.Regen(s.Systems, s.Stats, u.Rules, cap * 0.05f);
        Assert.Equal(before - cap * 0.05f, s.Systems.ShieldMissing, 3);
        Assert.Equal(s.Systems.ShieldMissing, SectorSum(s), 3);
        Assert.Equal(0f, s.Systems.SectorMissing![ShieldSectors.Front]);
        // über das Rest-Fehlende hinaus: voll, keine Verteilung mehr
        ShieldSectors.Regen(s.Systems, s.Stats, u.Rules, cap);
        Assert.Equal(0f, s.Systems.ShieldMissing);
        Assert.Null(s.Systems.SectorMissing);
        // andere Stelle füllt den Gesamtschild auf (Träger): Abgleich löscht die Verteilung
        ShipSystems.TakeShield(u, s, cap * 0.2f, ShieldSectors.Left);
        s.Systems.ShieldMissing = 0f;
        Assert.Equal(1f, ShieldSectors.Fill(u, s)![ShieldSectors.Left], 3);
        Assert.NotNull(s.Systems.SectorMissing);   // die Anzeige schreibt nichts
        ShieldSectors.Sync(s.Systems, s.Stats, u.Rules);
        Assert.Null(s.Systems.SectorMissing);
    }

    [Fact]
    public void Ausrichtung_VerteiltLadungNeu_SummeBleibt_RegenPausiert()
    {
        var u = World();
        var s = NewShip(u);
        float cap = s.Stats.Shield;
        ShipSystems.TakeShield(u, s, cap * 0.25f, ShieldSectors.Front);   // vorne leer
        float charge = s.ShieldNow;
        s.Systems.ShieldPauseUntil = 0;
        Assert.True(ShieldSectors.SetFocus(u, s, ShieldFocus.Front));
        Assert.Equal(ShieldFocus.Front, s.Systems.Focus);
        Assert.Equal(charge, s.ShieldNow, 3);
        Assert.True(ShieldSectors.ChargeOf(u, s, ShieldSectors.Front) > 0f);   // Ladung von hinten nach vorne verschoben
        Assert.Equal(cap * u.Rules.ShieldFocusShare, ShieldSectors.Capacity(s.Stats, u.Rules, s.Systems.Focus, ShieldSectors.Front), 3);
        Assert.True(s.Systems.ShieldPauseUntil > u.Tick);
        Assert.False(ShieldSectors.SetFocus(u, s, ShieldFocus.Front));   // nichts zu tun
        Assert.Equal(ShieldFocus.Back, ShieldSectors.NextPlayerFocus(ShieldFocus.Front));
        Assert.Equal(ShieldFocus.Balanced, ShieldSectors.NextPlayerFocus(ShieldFocus.Back));
    }

    [Fact]
    public void Linie_AufDerFluchtHinten_SonstVorne()
    {
        var u = World();
        var e = new Encounter();
        var p = new CombatParticipant { Move = CombatMove.Approach };
        Assert.Equal(ShieldSectors.Front, ShieldSectors.Incoming(u, e, p, null));
        p.Move = CombatMove.Flee;
        Assert.Equal(ShieldSectors.Back, ShieldSectors.Incoming(u, e, p, null));
    }

    [Fact]
    public void Npc_RichtetSchildZumAngreiferAus_Deterministisch()
    {
        string Run()
        {
            var u = World();
            var npc = NewShip(u, "warden");
            var raider = u.ShipsById().First(ThreatMap.IsRaider);
            if (raider.Transit is not null) u.FinishTransit(raider);
            if (raider.Sector != npc.Sector) u.MoveShip(raider, npc.Sector);
            if (npc.Sector == u.PlayerSector) u.PlayerSector = default;   // Linie, nicht räumlich
            raider.Position = npc.Position + new Vec2(0f, -30f); raider.DockedAt = default;
            var e = Engage.StartRaid(u, raider, npc, u.Sectors[npc.Sector]);
            var focus = new List<string>();
            var sys = new CombatSystem();
            for (int i = 0; i < 40 && !e.Done; i++)
            {
                sys.Step(u, 1);
                u.Tick++;
                foreach (var p in e.Participants)
                    if (p.Active && u.Ships.TryGetValue(p.Ship, out var sh) && sh.Stats.Shield > 0f)
                    {
                        var want = p.Move == CombatMove.Flee ? ShieldFocus.Back : ShieldFocus.Front;
                        Assert.Equal(want, sh.Systems.Focus);
                        Assert.Equal(sh.Systems.ShieldMissing, sh.Systems.SectorMissing is null ? sh.Systems.ShieldMissing : SectorSum(sh), 2);
                    }
                focus.Add(string.Join(",", e.Participants.Select(p => u.Ships.TryGetValue(p.Ship, out var sh) ? sh.Systems.Focus.ToString() + sh.Systems.ShieldMissing.ToString("0.000") : "-")));
            }
            return string.Join("|", focus) + UniverseHash.Compute(u);
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void SpeichernLaden_Bitgleich_HashNurWennGesetzt()
    {
        var u = World();
        var s = NewShip(u);
        ulong full = UniverseHash.Compute(u);
        ShipSystems.TakeShield(u, s, s.Stats.Shield * 0.2f, ShieldSectors.Right);
        ShieldSectors.SetFocus(u, s, ShieldFocus.Back);
        ShipSystems.TakeShield(u, s, s.Stats.Shield * 0.05f, ShieldSectors.Left);
        ulong set = UniverseHash.Compute(u);
        Assert.NotEqual(full, set);
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(set, UniverseHash.Compute(v));
        Assert.Equal(ShieldFocus.Back, v.Ships[s.Id].Systems.Focus);
        Assert.Equal(s.Systems.SectorMissing, v.Ships[s.Id].Systems.SectorMissing);
        new SimulationRunner().Run(u, 300);
        new SimulationRunner().Run(v, 300);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    [Fact]
    public void Langlauf_InvariantenUndAusrichtungen()
    {
        var u = World();
        int refocus = 0;
        u.Events.Raised += ev => { if (ev is ShieldFocusChanged) refocus++; };
        var runner = new SimulationRunner();
        for (int k = 0; k < 30; k++)
        {
            runner.Run(u, 200);
            foreach (var s in u.Ships.Values)
            {
                var st = s.Systems;
                if (st.SectorMissing is not { } a) continue;
                Assert.Equal(st.ShieldMissing, a.Sum(), 2);
                for (int i = 0; i < ShieldSectors.Count; i++)
                    Assert.InRange(a[i], 0f, ShieldSectors.Capacity(s.Stats, u.Rules, st.Focus, i) + 0.01f);
            }
        }
        Assert.True(refocus > 0);
    }

    [Theory]
    [InlineData(1280, 720, 1.3f)]
    [InlineData(1560, 720, 1.15f)]
    [InlineData(2340, 1080, 1.3f)]
    [InlineData(960, 540, 1f)]
    public void FlugHud_SchildTaste_UnterLeistung_NichtsUeberlappt(int w, int h, float factor)
    {
        var (w1, h1) = UiLayout.Logical(w, h, 1f);
        var safe1 = new UiInsets(53, 0, 0, 0);
        foreach (int groups in new[] { 0, 3, 6 })
            foreach (int bays in new[] { 0, 4, 10 })
                foreach (bool exp in new[] { false, true })
                {
                    float f = FlightHudLayout.FitFactor(w1, h1, safe1, groups, bays, factor, heat: true, shield: true);
                    var l = FlightHudLayout.Compute(w1 / f, h1 / f, new UiInsets(safe1.Left / f, 0, 0, 0), groups, bays, exp, heat: true, shield: true);
                    Assert.True(l.Overflow == 0, $"{w}×{h} f{f:0.00} g{groups} b{bays} {exp}: Überlauf {l.Overflow}");
                    Assert.False(l.Hidden(HudItem.ShieldFocus));
                    Assert.True(l[HudItem.ShieldFocus].W >= FlightHudLayout.Tap && l[HudItem.ShieldFocus].H >= 56f);
                    Assert.Equal(l[HudItem.Power].X, l[HudItem.ShieldFocus].X, 2);
                    var plain = FlightHudLayout.Compute(w1 / f, h1 / f, new UiInsets(safe1.Left / f, 0, 0, 0), groups, bays, exp, heat: true);
                    Assert.True(plain.Hidden(HudItem.ShieldFocus));
                }
    }
}
