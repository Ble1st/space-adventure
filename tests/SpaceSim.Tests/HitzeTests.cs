using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Ui;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Hitze und Wärmeabfuhr (KAMPF.md „Hitze und Wärmeabfuhr“).</summary>
public class HitzeTests
{
    /// <summary>Referenzhash-Tests: Schalter aus — WorldBuilder lässt die Kühlermodule weg.</summary>
    internal static void RevertHeat(WorldDef def) => (def.Rules ??= new EconomyRules()).ShipHeat = false;

    static readonly FactionId Union = new(1);

    static Universe World(bool heat = true)
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.ShipHeat = heat;
        return WorldBuilder.Build(def);
    }

    static Ship NewShip(Universe u, string model, ShipRole role = ShipRole.Warship)
    {
        var m = u.ShipModels[model];
        var cls = u.ShipClasses[m.ClassKey];
        var yard = u.StationsById().First(s => !s.IsBuildSite);
        var ship = ShipPurchase.Create(u, u.NewShipId(), model, cls.Slots, m.Fit, role, Controller.Ai, (ActorId)Union, yard, 2000f);
        u.AddShip(ship);
        ship.DockedAt = default;
        return ship;
    }

    static WeaponProfile Laser(Universe u) => WeaponProfile.Of(u.Modules["weapon_laser_s"]);

    static int Draws(ulong before, ulong after)
    {
        var r = new DeterministicRng(0) { State = before };
        for (int i = 0; i <= 10; i++) { if (r.State == after) return i; r.NextDouble(); }
        return -1;
    }

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_AusLaesstKuehlerWeg_UndKeineHitze()
    {
        Assert.False(new EconomyRules().ShipHeat);
        var on = World();
        Assert.True(on.Rules.ShipHeat);
        Assert.True(on.Modules.ContainsKey("heat_sink_s") && on.Modules.ContainsKey("heat_sink_m"));
        Assert.Equal(1.5f, on.Modules["heat_sink_s"].Stat("heatDissipation"));

        var off = World(heat: false);
        foreach (var k in HeatContent.Modules) Assert.False(off.Modules.ContainsKey(k));
        var s = NewShip(off, "falcon");
        var w = Laser(off);
        for (int i = 0; i < 30; i++) Assert.True(Gunnery.DrawPower(off, s, w, out _));
        Assert.Equal(0f, s.Systems.Heat);
        Assert.True(s.Systems.IsDefault || s.Systems.ChargeMissing > 0f);
        // Schalter aus = Welt ohne die Hitzeinhalte (Hash nach 1500 Takten)
        var a = World(heat: false);
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.ShipHeat = false;
        HeatContent.Strip(def);
        var b = WorldBuilder.Build(def);
        new SimulationRunner().Run(a, 1500);
        new SimulationRunner().Run(b, 1500);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }

    [Fact]
    public void Schusshitze_LaserHeisserAlsMassetreiber_ModulwertUeberschreibt()
    {
        var u = World();
        float laser = Heat.ShotHeat(u, WeaponProfile.Of(u.Modules["weapon_laser_s"]));
        float md = Heat.ShotHeat(u, WeaponProfile.Of(u.Modules["weapon_massdriver_s"]));
        float missile = Heat.ShotHeat(u, WeaponProfile.Of(u.Modules["missile_launcher_s"]));
        Assert.Equal(7f * u.Rules.HeatPerDamageEnergy, laser, 3);
        Assert.True(laser / 7f > md / 10f, "Laser je Schaden heißer als Massetreiber");
        Assert.True(missile / 30f < md / 10f);
        u.Modules["weapon_laser_s"].Stats["heat"] = 2f;
        Assert.Equal(2f, Heat.ShotHeat(u, WeaponProfile.Of(u.Modules["weapon_laser_s"])));
    }

    [Fact]
    public void Abfuhr_KlasseUndKuehler_KuehltAb()
    {
        var u = World();
        var s = NewShip(u, "falcon");
        Assert.Equal(100f, Heat.Capacity(u, s));
        Assert.Equal(3f, Heat.Dissipation(u, s));
        int slot = s.Slots.FindIndex(c => c.Type == "utility");
        s.Fit[slot] = "heat_sink_s";
        s.RecomputeStats(u);
        Assert.Equal(4.5f, Heat.Dissipation(u, s), 3);
        Assert.Contains("Kühler 1.5", Heat.StatsLine(u, s).Replace(',', '.'));
        s.Systems.Heat = 35f;
        var sys = new HeatSystem();
        for (int i = 0; i < 10; i++) { sys.Step(u, HeatSystem.Interval); u.Tick += HeatSystem.Interval; }   // 10 s
        Assert.Equal(0f, s.Systems.Heat);
    }

    [Fact]
    public void Schwellen_LangsamGesperrtFrei_MitHysterese()
    {
        var u = World();
        var s = NewShip(u, "falcon");
        var w = Laser(u);
        s.Systems.Heat = 50f;
        Assert.True(Gunnery.DrawPower(u, s, w, out float r1));
        Assert.Equal(w.Reload, r1, 3);
        s.Systems.Heat = 72f;
        Assert.True(Gunnery.DrawPower(u, s, w, out float r2));
        Assert.Equal(w.Reload * (1f + u.Rules.HeatReloadPenalty), r2, 3);
        // Schüsse bis zur Sperre
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        while (!s.Systems.HeatLocked) Assert.True(Gunnery.DrawPower(u, s, w, out _));
        Assert.Contains(ev, e => e is HeatLockChanged { Locked: true });
        Assert.False(Gunnery.DrawPower(u, s, w, out _));
        Assert.False(Gunnery.HasPower(u, s, w));
        Assert.Equal(0f, Heat.ShieldRegenFactor(u, s));
        Assert.Contains("gesperrt", Heat.Warning(u, s));
        // zwischen Freigabe und Sperre bleibt sie (Hysterese), darunter frei
        s.Systems.Heat = 80f;
        new HeatSystem().Step(u, HeatSystem.Interval);
        Assert.True(s.Systems.HeatLocked);
        s.Systems.Heat = 74f;
        new HeatSystem().Step(u, HeatSystem.Interval);
        Assert.False(s.Systems.HeatLocked);
        Assert.True(Gunnery.DrawPower(u, s, w, out _));
    }

    [Fact]
    public void Volle_Hitze_HuelleNieUnterEins_GenauEinZugJeRunde()
    {
        var u = World();
        var s = NewShip(u, "falcon");
        s.Systems.Heat = 100f;
        s.Systems.HeatLocked = true;
        s.Hull = 3f;
        var sys = new HeatSystem();
        for (int i = 0; i < 40; i++)
        {
            s.Systems.Heat = 100f;   // Quelle hält sie voll (z. B. künftig eine Sonne)
            ulong before = u.Rng.State;
            sys.Step(u, HeatSystem.Interval);
            Assert.Equal(1, Draws(before, u.Rng.State));
            Assert.True(s.Hull >= 1f);
        }
        Assert.Equal(1f, s.Hull);
        Assert.True(s.Systems.Condition.Any(c => c < 1f), "nach 40 Runden mit 10 % ist ein Modul beschädigt");
        // unter 100 % kein Zug
        s.Systems.Heat = 60f;
        ulong b2 = u.Rng.State;
        sys.Step(u, HeatSystem.Interval);
        Assert.Equal(0, Draws(b2, u.Rng.State));
    }

    [Fact]
    public void Quellen_Antrieb_Sprung_Tarnung_Schub()
    {
        var u = World();
        var s = NewShip(u, "falcon");
        Assert.Equal(0f, Heat.SourceRate(u, s));
        ShipSystems.SetMode(u, s, PowerMode.Engine);
        Assert.Equal(u.Rules.HeatEngineMode, Heat.SourceRate(u, s), 3);
        s.JumpTarget = new SectorId(2);
        Assert.Equal(u.Rules.HeatEngineMode + u.Rules.HeatJumpCharge, Heat.SourceRate(u, s), 3);
        s.JumpTarget = default;
        Heat.AddThrust(u, s, 1f, 2f);
        Assert.Equal(2f * u.Rules.HeatThrustFull, s.Systems.Heat, 3);
        Assert.Equal(0f, Heat.Environment(u, s));   // Haken für Sonnen
    }

    [Fact]
    public void Infrarot_HeissesZielBesserZuTreffen()
    {
        var u = World();
        var t = NewShip(u, "falcon");
        float cold = Guidance.HeatFactor(u, t, 4f);
        t.Systems.Heat = 90f;
        float hot = Guidance.HeatFactor(u, t, 4f);
        Assert.Equal(cold + 0.9f * u.Rules.HeatIrBonus, hot, 3);
        u.Rules.ShipHeat = false;
        Assert.Equal(cold, Guidance.HeatFactor(u, t, 4f), 3);
    }

    [Fact]
    public void Npc_HaeltFeuerZurueck_WennZuHeiss()
    {
        var u = World();
        var s = NewShip(u, "warden");
        var w = Laser(u);
        Assert.False(Heat.NpcHolds(u, s, w));
        s.Systems.Heat = Heat.Capacity(u, s) * u.Rules.HeatNpcHoldShare;
        Assert.True(Heat.NpcHolds(u, s, w));
        // Duell zweier Wächter: kein NPC erreicht die Sperre
        var sector = u.Sectors.Values.OrderBy(x => x.Id.Value).First(x => x.Id != u.PlayerSector && !x.Nebula).Id;
        var a = NewShip(u, "warden"); var b = NewShip(u, "warden", ShipRole.Pirate);
        foreach (var x in new[] { a, b }) { if (x.Sector != sector) u.MoveShip(x, sector); x.Position = new Vec2(0f, 300f); x.Hull = 1e6f; }
        var e = Engage.Begin(u, EncounterCause.Intercept, sector);
        Engage.Join(u, e, a, 0, CombatTactics.Police, 0f);
        Engage.Join(u, e, b, 1, CombatTactics.Raider, 20f);
        var runner = new SimulationRunner();
        float max = 0f;
        for (int i = 0; i < 200 && u.Fights.Of(a.Id) is not null; i++)
        {
            runner.Step(u);
            max = MathF.Max(max, MathF.Max(Heat.Share(u, a), Heat.Share(u, b)));
        }
        Assert.True(max > 0f, "im Gefecht entsteht Hitze");
        Assert.True(max <= u.Rules.HeatNpcHoldShare + 1e-3f, $"NPC hält vor der Sperre an ({max:0.00})");
        Assert.False(a.Systems.HeatLocked || b.Systems.HeatLocked);
    }

    [Fact]
    public void SpeichernLaden_Bitgleich_HashNurWennGesetzt()
    {
        var u = World();
        var s = NewShip(u, "falcon");
        ulong cold = UniverseHash.Compute(u);
        s.Systems.Heat = 0f;
        Assert.Equal(cold, UniverseHash.Compute(u));
        s.Systems.Heat = 42.5f; s.Systems.HeatLocked = true;
        ulong hot = UniverseHash.Compute(u);
        Assert.NotEqual(cold, hot);
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(hot, UniverseHash.Compute(v));
        Assert.Equal(42.5f, v.Ships[s.Id].Systems.Heat);
        new SimulationRunner().Run(u, 300);
        new SimulationRunner().Run(v, 300);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    [Theory]
    [InlineData(1280, 720, 1.3f)]
    [InlineData(1560, 720, 1.15f)]
    [InlineData(2340, 1080, 1.3f)]
    [InlineData(960, 540, 1f)]
    public void FlugHud_Hitzezeile_StatusHoeher_NichtsUeberlappt(int w, int h, float factor)
    {
        var (w1, h1) = UiLayout.Logical(w, h, 1f);
        var safe1 = new UiInsets(53, 0, 0, 0);
        foreach (int groups in new[] { 0, 3, 6 })
            foreach (int bays in new[] { 0, 4, 10 })
                foreach (bool exp in new[] { false, true })
                {
                    float f = FlightHudLayout.FitFactor(w1, h1, safe1, groups, bays, factor, heat: true);
                    var l = FlightHudLayout.Compute(w1 / f, h1 / f, new UiInsets(safe1.Left / f, 0, 0, 0), groups, bays, exp, heat: true);
                    Assert.True(l.Overflow == 0, $"{w}×{h} f{f:0.00} g{groups} b{bays} {exp}: Überlauf {l.Overflow}");
                    var plain = FlightHudLayout.Compute(w1 / f, h1 / f, new UiInsets(safe1.Left / f, 0, 0, 0), groups, bays, exp);
                    Assert.Equal(plain[HudItem.Status].H + FlightHudLayout.HeatRowH, l[HudItem.Status].H, 3);
                    Assert.True(l[HudItem.Security].Y >= l[HudItem.Status].Bottom, "Lagezeilen unter dem höheren Status");
                }
    }
}
