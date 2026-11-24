using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Raketenlenkung und Gegenmaßnahmen (KAMPF.md „Raketenlenkung und Gegenmaßnahmen“).</summary>
public class RaketenlenkungTests
{
    /// <summary>Referenzhash-Tests: Schalter aus — WorldBuilder lässt Täuschkörper, neue Werfer, Störsender und die geänderten Fits weg.</summary>
    internal static void RevertGuidance(WorldDef def) => (def.Rules ??= new EconomyRules()).MissileGuidance = false;

    static readonly FactionId Union = new(1);

    static Universe World(bool guidance = true)
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.MissileGuidance = guidance;
        return WorldBuilder.Build(def);
    }

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    static void Place(Universe u, Ship s, SectorId sector, Vec2 pos)
    {
        if (s.Transit is not null) u.FinishTransit(s);
        s.Order = null; s.Command = null; s.Phase = TradePhase.Idle; s.MinePhase = MinePhase.Idle;
        if (s.Sector != sector) u.MoveShip(s, sector);
        s.Position = pos;
        s.DockedAt = default;
    }

    static Ship NewShip(Universe u, string model, ShipRole role = ShipRole.Warship)
    {
        var m = u.ShipModels[model];
        var cls = u.ShipClasses[m.ClassKey];
        var yard = u.StationsById().First(s => !s.IsBuildSite);
        var ship = ShipPurchase.Create(u, u.NewShipId(), model, cls.Slots, m.Fit, role, Controller.Ai, (ActorId)Union, yard, 2000f);
        u.AddShip(ship);
        return ship;
    }

    /// <summary>Zwei Schiffe im Gefecht auf der Linie (Sektor ohne Spieler): Schütze (Bollwerk) und Ziel (Donner mit Täuschkörpern, Störsender eingebaut).</summary>
    static (Encounter E, Ship Shooter, Ship Target) Duel(Universe u, float dist = 60f)
    {
        var sector = u.Sectors.Values.OrderBy(s => s.Id.Value).First(s => s.Id != u.PlayerSector && !s.Nebula).Id;
        var shooter = NewShip(u, "bastion");
        var target = NewShip(u, "bomber");
        target.Fit[^1] = "jammer_s";   // Speicher-Slot weg, Störsender rein (Test: beide Gegenmaßnahmen an einem Schiff)
        target.Slots[^1] = new ClassSlot { Type = "utility", Size = SlotSize.S };
        target.RecomputeStats(u);
        Place(u, shooter, sector, new Vec2(0f, 300f));
        Place(u, target, sector, new Vec2(dist, 300f));
        var e = Engage.Begin(u, EncounterCause.Intercept, sector);
        Engage.Join(u, e, shooter, 0, CombatTactics.Siege, 0f);
        Engage.Join(u, e, target, 1, CombatTactics.Raider, dist);
        return (e, shooter, target);
    }

    static MissileFlight Fly(Encounter e, Ship shooter, Ship target, string module, float left = 0f) =>
        new() { Shooter = shooter.Id, Owner = shooter.Owner, Side = 0, Module = module, Target = target.Id, Left = left, Total = 50f, Speed = 35f };

    static string Resolve(Universe u, Encounter e, MissileFlight f, out int draws)
    {
        if (u.Ships.TryGetValue(f.Target, out var t)) { t.Hull = t.MaxHull; t.Systems.ShieldMissing = 0f; }   // das Ziel überlebt jede Prüfung
        e.Missiles.Add(f);
        var ev = Capture(u);
        ulong before = u.Rng.State;
        Missiles.Step(u, e, 0.25f);
        draws = Draws(before, u.Rng.State);
        return ev.OfType<MissileResolved>().Single(r => r.Target == f.Target).Outcome;
    }

    /// <summary>Wie viele Zahlen zog der RNG zwischen zwei Zuständen? (höchstens 10)</summary>
    static int Draws(ulong before, ulong after)
    {
        var r = new DeterministicRng(0) { State = before };
        for (int i = 0; i <= 10; i++) { if (r.State == after) return i; r.NextDouble(); }
        return -1;
    }

    // ───────── Schalter und Inhalte ─────────

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_AusLaesstInhalteWeg()
    {
        Assert.False(new EconomyRules().MissileGuidance);
        var on = World();
        Assert.True(on.Rules.MissileGuidance);
        Assert.Equal(MissileGuidance.Infrared, on.Modules["missile_launcher_s"].Guidance);
        Assert.Equal(MissileGuidance.ActiveRadar, on.Modules["missile_launcher_s_mk2"].Guidance);
        Assert.Equal(MissileGuidance.Optical, on.Modules["missile_launcher_optical_s"].Guidance);
        Assert.Equal(MissileGuidance.SemiActive, on.Modules["missile_launcher_radar_s"].Guidance);
        Assert.Contains(on.Goods.Values, g => g.Key == "flare" && g.Category == Countermeasures.FlareCategory);
        Assert.Contains("flare_launcher_s", on.ShipModels["bomber"].Fit);
        Assert.Contains("jammer_s", on.ShipModels["battlecruiser"].Fit);
        Assert.Contains("missile_launcher_radar_s", on.ShipModels["missile_boat"].Fit);

        var off = World(guidance: false);
        Assert.DoesNotContain(off.Goods.Values, g => g.Key == "flare");
        foreach (var k in GuidanceContent.Modules) Assert.False(off.Modules.ContainsKey(k));
        Assert.Equal(MissileGuidance.None, off.Modules["missile_launcher_s"].Guidance);
        Assert.DoesNotContain(off.ShipModels.Values, m => m.Fit.Any(k => k is not null && GuidanceContent.Modules.Contains(k)));
        Assert.Null(off.ShipModels["bomber"].Fit[5]);
        // dieselbe Welt mit Schalter aus = dieselbe Welt ganz ohne die Raketenlenkung (Hash nach 1500 Takten)
        var a = World(guidance: false);
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.MissileGuidance = false;
        GuidanceContent.Strip(def);
        var b = WorldBuilder.Build(def);
        new SimulationRunner().Run(a, 1500);
        new SimulationRunner().Run(b, 1500);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }

    [Fact]
    public void Inhalt_UngueltigeLenkart_Wirft()
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Modules.First(m => m.Key == "missile_launcher_s").Guidance = "laser";
        Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
    }

    // ───────── Lenkarten ─────────

    [Fact]
    public void Infrarot_TaeuschkoerperLenkenAb_EinZugMehr()
    {
        var u = World();
        var (e, shooter, target) = Duel(u);
        // ohne Täuschkörper (Magazin leer, die NPC-Reaktion stößt keine aus): Punktabwehr (1) + Treffer (2) = 3 Züge
        target.FlaresMissing = target.Stats.FlareCapacity;
        string o1 = Resolve(u, e, Fly(e, shooter, target, "missile_launcher_s"), out int d1);
        Assert.NotEqual("decoyed", o1);
        Assert.Equal(3, d1);
        // Täuschkörper aktiv, Chance 1 → abgelenkt nach genau einem Zug
        u.Rules.FlareChance = 1f;
        target.FlaresMissing = 0f;
        Assert.Null(Countermeasures.FireFlares(u, target));
        Assert.Equal(1f, target.FlaresMissing);
        Assert.Equal("decoyed", Resolve(u, e, Fly(e, shooter, target, "missile_launcher_s"), out int d2));
        Assert.Equal(1, d2);
        // Chance 0 → nicht abgelenkt, vier Züge (höchstens)
        u.Rules.FlareChance = 0f;
        string o3 = Resolve(u, e, Fly(e, shooter, target, "missile_launcher_s"), out int d3);
        Assert.NotEqual("decoyed", o3);
        Assert.Equal(4, d3);
        // Täuschkörper wirken nicht gegen Radar
        u.Rules.FlareChance = 1f;
        Assert.NotEqual("decoyed", Resolve(u, e, Fly(e, shooter, target, "missile_launcher_s_mk2"), out _));
        // nach Ablauf wirken sie nicht mehr
        u.Tick = target.FlaresUntil;
        Assert.False(Countermeasures.FlaresActive(u, target));
    }

    [Fact]
    public void Infrarot_KaltesZielIstSchwererZuTreffen()
    {
        var u = World();
        var (_, _, target) = Duel(u);
        float cold = Guidance.HeatFactor(u, target, 0f), hot = Guidance.HeatFactor(u, target, target.Stats.Speed);
        Assert.Equal(u.Rules.HeatMin, cold, 3);
        Assert.Equal(1f, hot, 3);
        ShipSystems.SetMode(u, target, PowerMode.Engine);
        Assert.Equal(1f + u.Rules.HeatEngineBonus, Guidance.HeatFactor(u, target, target.Stats.Speed), 3);
    }

    [Fact]
    public void AktivesRadar_StoersenderLenktAb_KostetLeistung()
    {
        var u = World();
        var (e, shooter, target) = Duel(u);
        float drawBefore = target.Stats.PowerDraw;
        Assert.Null(Countermeasures.SetJammer(u, target, true));
        Assert.True(target.JammerOn);
        Assert.Equal(drawBefore + target.Stats.JamDraw, target.Stats.PowerDraw, 3);
        Assert.True(Countermeasures.JammerWorks(u, target));
        u.Rules.JammerChance = 1f;
        Assert.Equal("jammed", Resolve(u, e, Fly(e, shooter, target, "missile_launcher_s_mk2"), out int d));
        Assert.Equal(1, d);
        // gegen Infrarot hilft der Störsender nicht (ohne Täuschkörper kein Zusatzzug)
        target.FlaresMissing = target.Stats.FlareCapacity;
        Assert.NotEqual("jammed", Resolve(u, e, Fly(e, shooter, target, "missile_launcher_s"), out int d2));
        Assert.Equal(3, d2);
        Assert.Null(Countermeasures.SetJammer(u, target, false));
        Assert.Equal(drawBefore, target.Stats.PowerDraw, 3);
    }

    [Fact]
    public void Optisch_StartNurMitSichtkontakt_VerborgenesZielVerliertRakete()
    {
        var u = World();
        var (e, shooter, target) = Duel(u);
        var m = u.Modules["missile_launcher_optical_s"];
        var sp = e.Of(shooter.Id)!;
        Assert.Null(Guidance.LaunchIssue(u, e, sp, m, target, target.Position));
        Assert.Null(Guidance.LaunchIssue(u, e, sp, u.Modules["missile_launcher_s"], target, target.Position));
        // Winkel (räumlich): Ziel hinter dem Bug
        Assert.NotNull(Guidance.LaunchIssue(u, e.Sector, m, target, true, new Vec2(0f, 0f), new Vec2(0f, -1f), new Vec2(0f, 50f)));
        Assert.Null(Guidance.LaunchIssue(u, e.Sector, m, target, true, new Vec2(0f, 0f), new Vec2(0f, -1f), new Vec2(10f, -50f)));
        // Nebel: kein Start; eine fliegende Rakete verliert das Ziel ohne Zug
        u.Sectors[e.Sector].Nebula = true;
        Assert.NotNull(Guidance.LaunchIssue(u, e, sp, m, target, target.Position));
        Assert.Equal("blind", Resolve(u, e, Fly(e, shooter, target, "missile_launcher_optical_s"), out int d));
        Assert.Equal(0, d);
    }

    [Fact]
    public void Halbaktiv_SchuetzeVerliertErfassung_RaketeVerfaellt()
    {
        var u = World();
        var (e, shooter, target) = Duel(u, 60f);
        // in Reichweite: die Rakete fliegt weiter und wird am Ziel aufgelöst
        Assert.NotEqual("lock_lost", Resolve(u, e, Fly(e, shooter, target, "missile_launcher_radar_s"), out _));
        // Ziel außer Reichweite des Schützen → Erfassung verloren, kein Zug
        e.Of(target.Id)!.X = 500f;
        Assert.Equal("lock_lost", Resolve(u, e, Fly(e, shooter, target, "missile_launcher_radar_s", left: 30f), out int d));
        Assert.Equal(0, d);
        e.Of(target.Id)!.X = 60f;
        // Schütze verlässt das Gefecht → Erfassung verloren
        Engage.Leave(u, e, e.Of(shooter.Id)!, CombatExit.Fled);
        Assert.Equal("lock_lost", Resolve(u, e, Fly(e, shooter, target, "missile_launcher_radar_s", left: 30f), out _));
    }

    // ───────── NPC-Gegenmaßnahmen ─────────

    [Fact]
    public void Npc_StoesstTaeuschkoerperAus_UndSchaltetStoersender()
    {
        var u = World();
        var (e, shooter, target) = Duel(u);
        var ev = Capture(u);
        // Infrarot weit weg: noch nichts; kurz vor dem Einschlag: Täuschkörper
        e.Missiles.Add(Fly(e, shooter, target, "missile_launcher_s", left: 200f));
        Countermeasures.React(u, e);
        Assert.False(Countermeasures.FlaresActive(u, target));
        e.Missiles[0].Left = 10f;
        Countermeasures.React(u, e);
        Assert.True(Countermeasures.FlaresActive(u, target));
        Assert.Contains(ev.OfType<CountermeasureUsed>(), c => c.Kind == "flares" && c.Ship == target.Id);
        e.Missiles.Clear();
        // aktives Radar: Störsender an, ohne Rakete wieder aus
        e.Missiles.Add(Fly(e, shooter, target, "missile_launcher_s_mk2", left: 200f));
        Countermeasures.React(u, e);
        Assert.True(target.JammerOn);
        e.Missiles.Clear();
        Countermeasures.React(u, e);
        Assert.False(target.JammerOn);
        // das aktive Spielerschiff entscheidet selbst
        Assert.DoesNotContain(ev.OfType<CountermeasureUsed>(), c => c.Ship == u.PlayerShip);
    }

    // ───────── Warnung, Nachladen, Speichern ─────────

    [Fact]
    public void Warnung_NenntLenkartRestzeitRichtung()
    {
        var u = World();
        var (e, shooter, target) = Duel(u);
        Assert.Empty(Countermeasures.Incoming(u, target));
        e.Missiles.Add(Fly(e, shooter, target, "missile_launcher_s", left: 70f));
        e.Missiles.Add(Fly(e, shooter, target, "missile_launcher_s_mk2", left: 35f));
        var list = Countermeasures.Incoming(u, target);
        Assert.Equal(2, list.Count);
        Assert.Equal(MissileGuidance.ActiveRadar, list[0].Guidance);
        Assert.Equal(1f, list[0].Seconds, 3);
        string text = Countermeasures.WarningText(list, "◀ von links");
        Assert.Contains("RAKETE (+1)!", text);
        Assert.Contains("Radar (aktiv)", text);
        Assert.Contains("1.0 s", text.Replace(',', '.'));
        Assert.Contains("Störsender", text);
        // Richtung relativ zur Nase: Schütze links (x kleiner), Nase nach −Y (Peilung 0)
        Assert.Equal("◀ von links", Countermeasures.DirectionText(list[0], 0f));
    }

    [Fact]
    public void Nachladen_TaeuschkoerperAnDerWerft_UndAnDerBasis()
    {
        var u = World();
        new SimulationRunner().Run(u, 1);   // Preise
        var me = u.Ships[u.PlayerShip];
        int util = me.Slots.FindIndex(s => s.Type == "utility");
        if (util < 0) { me.Slots.Add(new ClassSlot { Type = "utility", Size = SlotSize.S }); me.Fit.Add(null); util = me.Slots.Count - 1; }
        me.Fit[util] = "flare_launcher_s";
        me.RecomputeStats(u);
        me.FlaresMissing = 4f;
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        yard.Market[Countermeasures.FlareGood(u)!.Id].Amount = 8f;
        u.WalletOf(me).Credits = 1000f;
        var svc = new RearmService();
        var offer = svc.Offers(u, yard, me).Single(o => o.Key == "rearm:flares");
        Assert.Equal(4f, offer.Amount);
        var r = svc.Execute(u, yard, me, new ServiceRequest("rearm:flares", default, 0f));
        Assert.True(r.Success, r.Message);
        Assert.Equal(0f, me.FlaresMissing);
        // NPC an der Basis aus der Kasse
        var (_, _, bomber) = Duel(u);
        bomber.FlaresMissing = 3f;
        float before = u.Actors[bomber.Owner].Treasury.Credits;
        Countermeasures.AtHome(u, bomber);
        Assert.Equal(0f, bomber.FlaresMissing, 3);
        Assert.True(u.Actors[bomber.Owner].Treasury.Credits < before);
    }

    [Fact]
    public void Speichern_Laden_Bitgleich_MitGegenmassnahmen()
    {
        var u = World();
        var (e, shooter, target) = Duel(u);
        Countermeasures.FireFlares(u, target);
        Countermeasures.SetJammer(u, target, true);
        e.Missiles.Add(Fly(e, shooter, target, "missile_launcher_radar_s", left: 30f));
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        var t2 = v.Ships[target.Id];
        Assert.True(t2.JammerOn);
        Assert.Equal(target.FlaresUntil, t2.FlaresUntil);
        Assert.Equal(target.Stats.PowerDraw, t2.Stats.PowerDraw, 3);
        new SimulationRunner().Run(u, 300);
        new SimulationRunner().Run(v, 300);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    [Fact]
    public void Schaechte_FuerSpieler_NurMitModul()
    {
        var u = World();
        var me = u.Ships[u.PlayerShip];
        Assert.DoesNotContain(WeaponBays.For(u, me), b => b.Key == WeaponBays.Flares.Key || b.Key == WeaponBays.Jammer.Key);
        me.Slots.Add(new ClassSlot { Type = "utility", Size = SlotSize.S }); me.Fit.Add("flare_launcher_s");
        me.Slots.Add(new ClassSlot { Type = "utility", Size = SlotSize.S }); me.Fit.Add("jammer_s");
        me.RecomputeStats(u);
        var bays = WeaponBays.For(u, me);
        Assert.Contains(bays, b => b.Key == WeaponBays.Flares.Key);
        Assert.Contains(bays, b => b.Key == WeaponBays.Jammer.Key);
        // keine Kampfgruppe: FEUER löst sie nicht aus
        Assert.All(BayGroups.Of(u, me, bays).Where(g => g.Key is "flares" or "jammer"), g => Assert.False(g.Combat));
    }
}
