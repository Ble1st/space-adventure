using SpaceSim.Core;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;
using Xunit;
using Xunit.Abstractions;

namespace SpaceSim.Tests;

/// <summary>
/// Wirtschaftsstationen gegen Flotten (docs/KAMPF.md „Wirtschaftsstationen halten stand“): Alarm ruft Polizei und Kriegsschiffe aus Nachbarsektoren,
/// Notschild unter halber Hülle, mehr Geschütze und Punktabwehr je Größe. Messung der Zeit bis zum Fall über den echten Angriffsweg (Spieler + Verband,
/// <see cref="PlayerGunnery.TryFireAtStation"/>, <see cref="Siege"/>, Verstärkung).
/// </summary>
public class WirtschaftsabwehrTests
{
    readonly ITestOutputHelper _out;
    public WirtschaftsabwehrTests(ITestOutputHelper o) => _out = o;

    /// <summary>
    /// Regeln dieser Runde zurücknehmen (Teil der Rücknahmekette über <see cref="BalanceFixTests.RevertBalanceFix"/> → <see cref="Runde44ScaleTests.RevertRunde44"/>):
    /// Alarm und Notschild aus, Geschütze/Hülle/Punktabwehr der Vorgabeverteidigung wie vorher (1/2/2 leichte Laser, Faktor 3, keine Punktabwehr).
    /// </summary>
    internal static void RevertWirtschaftsabwehr(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.EconomicStationAlarm = false; r.EconomicEmergencyShield = false;
        r.EconomicStationGuns = new() { 1, 2, 2 }; r.EconomicStationGunList = new(); r.EconomicStationPointDefense = new();
        r.EconomicStationHullFactor = 3f; r.EconomicEmergencyShieldTicks = 240;
    }

    /// <summary>Kriegstest-Welt der Kalibrierung. Gasernte und Nebel ändern keine Belagerungsregel, verschieben aber den Zufallsweg (Güteklassen der
    /// Gasschöpfer-Gründer) — gemessen wurde die Kalibrierung auf der Welt davor, daher ohne den Gasinhalt.</summary>
    static Universe War(ulong seed = 1)
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        GasNebulaTests.RevertGas(def);
        return WorldBuilder.Build(def, seed, "war");
    }

    static void Place(Universe u, Ship s, SectorId sector, Vec2 pos)
    {
        if (s.Transit is not null) u.FinishTransit(s);
        s.Order = null; s.Phase = TradePhase.Idle; s.MinePhase = MinePhase.Idle;
        if (s.Sector != sector) u.MoveShip(s, sector);
        s.Position = pos;
        s.DockedAt = default;
    }

    static Ship NewPlayerShip(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        u.AddShip(s);
        s.DockedAt = default;
        return s;
    }

    internal static Action<string>? Log;
    internal sealed record Run(bool Fell, bool AttackerAlive, long Seconds, float HullLeft, int Defenders, int Alarms, int Shields);

    /// <summary>
    /// Angriff wie am Handy (heranfliegen, Zielhilfe, Dauerfeuer) mit <paramref name="models"/> (erstes = aktives Schiff, Rest = Verband mit Regel „Angriff“)
    /// auf die Farm Beta (M, abseits des Sektor-HQ). Ohne Verteidiger: Polizei/Kriegsschiffe der Fraktion und ihrer Verbündeten weg, Nachkauf gesperrt.
    /// </summary>
    internal static Run Attack(Universe u, string[] models, bool defenders, int maxSeconds, Action<SimEvent>? watch = null)
    {
        u.Rules.StationCapitulation = false;   // gemessen wird die Zeit bis Hülle 0
        var target = u.Stations.Values.First(x => x.Name == "Farm Beta");
        var me0 = u.Ships[u.PlayerShip];
        // alten Verband weg
        foreach (var id in (Squadrons.Get(u, me0)?.Members ?? new List<ShipId>()).Where(id => id != me0.Id).ToList()) Departures.RemoveShip(u, u.Ships[id], DepartureReason.Sold, "test");
        var me = me0;
        if (models[0] != me0.ModelKey)
        {
            me = NewPlayerShip(u, models[0], target);
            PlayerFleet.Activate(u, me, default);
            Departures.RemoveShip(u, me0, DepartureReason.Sold, "test");
        }
        var dir = new Vec2(-1f, 0.2f).Normalized();
        Place(u, me, target.Sector, target.Position + dir * (target.Radius + 60f));
        if (models.Length > 1)
        {
            var sq = Squadrons.Get(u, me) ?? Squadrons.Create(u, me, "Testverband", out _)!;
            for (int i = 1; i < models.Length; i++)
            {
                var w = NewPlayerShip(u, models[i], target);
                Place(u, w, target.Sector, me.Position + new Vec2(0f, 12f * i));
                w.Engagement = Engagement.Attack;
                Squadrons.Join(u, sq, w, out _);
                Commands.Give(u, w, new FollowCommand(me.Id), out _, ignoreCapacity: true);
            }
        }
        var f = u.FactionOf(target)!.Value;
        if (!defenders)
        {
            var sides = new HashSet<FactionId>(Alliances.AlliesOf(u, f)) { f };
            foreach (var a in u.Actors.Values.Where(a => a.IsNpcFaction(u))) a.LastForceTick = u.Tick + 10_000_000;
            foreach (var s in u.Ships.Values.Where(s => (ThreatMap.IsPolice(s) || Wars.IsWarship(s)) && u.FactionOf(s) is { } sf && sides.Contains(sf)).ToList())
                Departures.RemoveShip(u, s, DepartureReason.Sold, "test");
        }
        var ids = new HashSet<ShipId>();
        int alarms = 0, shields = 0;
        void Watch(SimEvent e)
        {
            if (e is CombatJoined j && j.Encounter != 0 && u.Fights.Encounters.FirstOrDefault(x => x.Id == j.Encounter) is { } je && je.Station == target.Id && u.Ships.TryGetValue(j.Ship, out var js) && !js.IsPlayer) ids.Add(j.Ship);
            if (e is StationAlarmRaised a && a.Station == target.Id) { alarms++; Log?.Invoke($"t{u.Tick}: {a.Message}"); }
            if (e is PlayerShipLost pl) Log?.Invoke($"t{u.Tick}: {pl.Message}");
            if (e is EncounterStarted es && (es.Sector == target.Sector)) Log?.Invoke($"t{u.Tick}: Gefecht {es.Encounter} {es.Cause}: {es.Message}");
            if (e is StationEmergencyShield sh && sh.Station == target.Id && sh.Raised) shields++;
            watch?.Invoke(e);
        }
        u.Events.Raised += Watch;
        var runner = new SimulationRunner();
        float heading = AimAssist.Bearing(me.Position, target.Position);
        long start = u.Tick;
        // Abstand: 0,7 × kürzeste Nicht-Raketen-Reichweite ab Außenhaut (wie die Anflugautomatik)
        float shortest = float.MaxValue;
        foreach (int slot in PlayerGunnery.WeaponSlots(u, me))
            if (Gunnery.WeaponAt(u, me, slot) is { } wm && !Missiles.IsMissile(wm)) shortest = MathF.Min(shortest, wm.Stat("range"));
        float standoff = target.Radius + MathF.Max(12f, 0.7f * (shortest == float.MaxValue ? 40f : shortest));
        bool fell = false;
        for (int i = 0; i < maxSeconds * SimClock.TicksPerSecond; i++)
        {
            runner.Step(u);
            if (u.Fights.Encounters.FirstOrDefault(x => x.Station == target.Id && !x.Done) is { } se)
                foreach (var q in se.Participants) if (q.Side == 1 && q.Active && u.Ships.TryGetValue(q.Ship, out var qs) && !qs.IsPlayer) ids.Add(q.Ship);
            if (!u.Stations.ContainsKey(target.Id) || StationCombat.IsDown(u, target)) { fell = true; break; }
            if (u.PlayerShip != me.Id || !u.Ships.ContainsKey(me.Id)) break;
            float d = Vec2.Distance(me.Position, target.Position);
            if (d > standoff) me.Position += (target.Position - me.Position).Normalized() * MathF.Min(d - standoff, me.Stats.Speed / SimClock.TicksPerSecond);
            heading = AimAssist.Turn(heading, AimAssist.Bearing(me.Position, target.Position), AimAssist.PlayerTurnRate, 1f / SimClock.TicksPerSecond);
            foreach (int slot in PlayerGunnery.WeaponSlots(u, me)) PlayerGunnery.TryFireAtStation(u, me, slot, target.Id, heading * 180f / MathF.PI);
        }
        u.Events.Raised -= Watch;
        if (u.Ships.TryGetValue(me.Id, out var mz)) Log?.Invoke($"Angreifer {mz.ModelKey}: Hülle {mz.Hull:0}/{mz.MaxHull:0} Schild {mz.ShieldNow:0}/{mz.Stats.Shield:0} Regen {mz.Stats.ShieldRegen:0.0} Panzer {mz.Stats.Armor:0} Stärke {CombatRules.Strength(mz):0}");
        float left = u.Stations.ContainsKey(target.Id) ? StationCombat.Hull(u, target) / MathF.Max(1f, StationCombat.MaxHull(u, target)) : 0f;
        return new Run(fell, u.PlayerShip == me.Id && u.Ships.ContainsKey(me.Id), (u.Tick - start) / SimClock.TicksPerSecond, left, ids.Count, alarms, shields);
    }

    public static IEnumerable<object[]> Cases() => new[]
    {
        new object[] { "Zerstörer allein", new[] { "destroyer" } },
        new object[] { "Bollwerk allein", new[] { "bastion" } },
        new object[] { "Zerstörer + 2 Hornissen", new[] { "destroyer", "corvette", "corvette" } },
        new object[] { "drei Bollwerke", new[] { "bastion", "bastion", "bastion" } },
    };

    static Universe WarOld()
    {
        var def = TestWorlds.Def(fuel: false, research: false);
        GasNebulaTests.RevertGas(def);
        RevertWirtschaftsabwehr(def);
        return WorldBuilder.Build(def, 1, "war");
    }

    /// <summary>
    /// Kalibrierung (Tabelle in docs/KAMPF.md „Wirtschaftsstationen halten stand“): Zeit bis zum Fall der Farm Beta (M) über den echten Angriffsweg, mit und
    /// ohne Verteidiger, jeweils neu gegen die Regel davor. Richtwerte: allein nimmt kein Zerstörer/Bollwerk die Station in zehn Minuten; Verbände brauchen mit
    /// Verteidigern mindestens sechs Minuten (drei Bollwerke höchstens zwölf), ohne Hilfe weniger, aber nicht im Handstreich.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Kalibrierung_ZeitBisZumFall(string name, string[] models)
    {
        Log = _out.WriteLine;
        var res = new Dictionary<bool, Run>();
        foreach (bool def in new[] { true, false })
        {
            var old = Attack(WarOld(), models, def, 900);
            var r = Attack(War(), models, def, 900);
            res[def] = r;
            string T(Run x) => x.Fell ? $"fällt nach {x.Seconds} s" : $"hält ({x.Seconds} s, Hülle {x.HullLeft:P0}, Angreifer {(x.AttackerAlive ? "lebt" : "verloren")})";
            _out.WriteLine($"TABELLE | {name} | {(def ? "mit" : "ohne")} Verteidiger | vorher {T(old)} | jetzt {T(r)} | Verteidiger {r.Defenders}");
            Assert.True(r.Alarms >= 1, "Alarm ausgelöst");
        }
        bool Holds10(Run x) => !x.Fell || x.Seconds >= 600;
        if (models.Length == 1)
        {
            // allein: nie in zehn Minuten
            Assert.True(Holds10(res[true]), $"{name} mit Verteidigern: {res[true]}");
            Assert.True(Holds10(res[false]), $"{name} ohne Verteidiger: {res[false]}");
            return;
        }
        // mit Verteidigern hält die Station mindestens sechs Minuten (oder schlägt den Angriff ab)
        Assert.True(!res[true].Fell || res[true].Seconds >= 360, $"{name} mit Verteidigern: {res[true]}");
        // ohne Hilfe darf ein klar überlegener Verband sie nehmen, aber nicht im Handstreich
        Assert.True(!res[false].Fell || res[false].Seconds >= 200, $"{name} ohne Verteidiger: {res[false]}");
        if (models.All(m => m == "bastion"))
        {
            Assert.True(res[true].Fell && res[true].Seconds <= 720, $"drei Bollwerke schaffen es mit Verteidigern in höchstens zwölf Minuten: {res[true]}");
            Assert.True(res[false].Fell, "drei Bollwerke schaffen es ohne Hilfe");
        }
    }

    [Fact]
    public void Schalter_CodeAlteRegel_StandardweltAn_RuecknahmeAus()
    {
        var c = new EconomyRules();
        Assert.False(c.EconomicStationAlarm || c.EconomicEmergencyShield);
        Assert.Empty(c.EconomicStationGunList);
        Assert.Empty(c.EconomicStationPointDefense);
        var u = War();
        Assert.True(u.Rules.EconomicStationAlarm && u.Rules.EconomicEmergencyShield);
        var farm = u.Stations.Values.First(x => x.Name == "Farm Beta");
        var def = StationCombat.DefenseOf(u, farm)!;
        Assert.Equal(u.Rules.EconomicStationGuns[0], def.Guns.Count);
        Assert.Contains("weapon_massdriver_s", def.Guns);
        var big = u.StationsById().First(s => u.StationTypes[s.TypeKey].Size == StationSize.L && EconomicDefense.For(u, u.StationTypes[s.TypeKey]) is not null);
        Assert.True(StationCombat.DefenseOf(u, big)!.PointDefense > 0f, "L-Stationen haben Punktabwehr");
        Assert.All(u.StationsById(), st =>
        {
            if (StationCombat.DefenseOf(u, st) is { } d && u.StationTypes[st.TypeKey].Defense is null)
                Assert.True(d.Guns.Count <= StationSizes.MaxGuns(u.StationTypes[st.TypeKey].Size), st.Name);
        });
        var old = WarOld();
        Assert.False(old.Rules.EconomicStationAlarm || old.Rules.EconomicEmergencyShield);
        Assert.Single(StationCombat.DefenseOf(old, old.Stations.Values.First(x => x.Name == "Farm Beta"))!.Guns);
    }

    /// <summary>Alarm ruft eine Streife aus dem Nachbarsektor: sie fliegt über ihren Streifenbefehl hin (kein Versetzen) und tritt der Belagerung bei.</summary>
    [Fact]
    public void Alarm_HoltStreifeAusNachbarsektor_FliegtHinUndVerteidigt()
    {
        var u = War();
        var farm = u.Stations.Values.First(x => x.Name == "Farm Beta");
        var f = u.FactionOf(farm)!.Value;
        var police = u.ShipsById().Where(s => ThreatMap.IsPolice(s) && u.FactionOf(s) == f).ToList();
        Assert.NotEmpty(police);
        // eine Streife in einen Nachbarsektor (ein Sprung), die anderen weg
        var gate = u.Gates.Values.First(g => g.Sector == farm.Sector);
        var far = u.Gates[gate.Target];
        var cop = police[0];
        if (Squadrons.Get(u, cop) is { } csq) Squadrons.Disband(u, csq);
        if (cop.Command is not null) Commands.Cancel(u, cop);
        Place(u, cop, far.Sector, far.Position + new Vec2(30f, 0f));
        foreach (var p in police.Skip(1)) Departures.RemoveShip(u, p, DepartureReason.Sold, "test");
        foreach (var a in u.Actors.Values.Where(a => a.IsNpcFaction(u))) a.LastForceTick = u.Tick + 10_000_000;
        var ev = new List<SimEvent>();
        u.Events.Raised += ev.Add;
        var me = u.Ships[u.PlayerShip];
        foreach (var id in (Squadrons.Get(u, me)?.Members ?? new List<ShipId>()).Where(id => id != me.Id).ToList()) Departures.RemoveShip(u, u.Ships[id], DepartureReason.Sold, "test");
        Place(u, me, farm.Sector, farm.Position + new Vec2(-(farm.Radius + 20f), 0f));
        int launcher = me.Fit.IndexOf("missile_launcher_s");
        var shot = PlayerGunnery.TryFireAtStation(u, me, launcher, farm.Id, FiringArc.Bearing(farm.Position - me.Position));
        Assert.Equal(GunResult.Fired, shot.Result);
        Assert.True(EconomicHoldout.Alarmed(u, farm));
        var alarm = ev.OfType<StationAlarmRaised>().Single(a => a.Station == farm.Id);
        Assert.Equal(1, alarm.Responders);
        Assert.Equal(farm.Sector, EconomicHoldout.AlarmSectorFor(u, cop));
        var runner = new SimulationRunner();
        bool joined = false;
        for (int i = 0; i < 2400 && !joined; i++)
        {
            runner.Step(u);
            if (i % 60 == 0) foreach (int slot in PlayerGunnery.WeaponSlots(u, me)) PlayerGunnery.TryFireAtStation(u, me, slot, farm.Id, FiringArc.Bearing(farm.Position - me.Position));
            if (!u.Ships.ContainsKey(cop.Id)) break;
            joined = u.Fights.Of(cop.Id) is { } e && e.Station == farm.Id && e.Of(cop.Id)!.Side == 1;
        }
        Assert.True(joined, $"die Streife aus {u.Sectors[far.Sector].Name} verteidigt die Farm (Befehl {cop.Command})");
        Assert.Contains(ev, e => e is ShipArrived a && a.Ship == cop.Id);   // über den normalen Flug, nicht versetzt
        Assert.Equal(farm.Sector, cop.Sector);
    }

    /// <summary>Notschild: unter halber Hülle hebt die Station ihn (Ereignis), Hüllenschaden sinkt, danach Abklingzeit; Zustand übersteht Speichern/Laden.</summary>
    [Fact]
    public void Notschild_UnterHalberHuelle_DaempftUndKlingtAb()
    {
        var u = War();
        var farm = u.Stations.Values.First(x => x.Name == "Farm Beta");
        var ev = new List<SimEvent>();
        u.Events.Raised += ev.Add;
        float max = StationCombat.MaxHull(u, farm);
        StationCombat.Apply(u, null, farm, Hull(max * 0.6f), u.PlayerActor);
        Assert.True(EconomicHoldout.ShieldUp(u, farm));
        Assert.Single(ev.OfType<StationEmergencyShield>());
        float before = StationCombat.Hull(u, farm);
        StationCombat.Apply(u, null, farm, Hull(100f), u.PlayerActor);
        Assert.Equal(before - 100f * (1f - u.Rules.EconomicEmergencyShieldReduction), StationCombat.Hull(u, farm), 2);
        Assert.Contains("Notschild", WarInfo.StationLine(u, farm));
        // Speichern/Laden bitgleich mit gesetztem Zustand
        var back = SpaceSim.Core.Persistence.UniverseSerializer.FromSave(SpaceSim.Core.Persistence.UniverseSerializer.ToSave(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(SpaceSim.Core.Diagnostics.UniverseHash.Compute(u), SpaceSim.Core.Diagnostics.UniverseHash.Compute(back));
        Assert.Equal(farm.Condition!.EmergencyCooldownUntil, back.Stations[farm.Id].Condition!.EmergencyCooldownUntil);
        Assert.Equal(farm.Condition!.AlarmUntil, back.Stations[farm.Id].Condition!.AlarmUntil);
        // abgelaufen: voller Schaden, die Abklingzeit verhindert ein neues Auslösen
        u.Tick = farm.Condition!.EmergencyUntil;
        before = StationCombat.Hull(u, farm);
        StationCombat.Apply(u, null, farm, Hull(50f), u.PlayerActor);
        Assert.Equal(before - 50f, StationCombat.Hull(u, farm), 2);
        Assert.False(EconomicHoldout.ShieldUp(u, farm));
        Assert.Single(ev.OfType<StationEmergencyShield>());
        u.Tick = farm.Condition!.EmergencyCooldownUntil;
        StationCombat.Apply(u, null, farm, Hull(1f), u.PlayerActor);
        Assert.True(EconomicHoldout.ShieldUp(u, farm));
        // Regierungsstationen bekommen keinen
        var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
        StationCombat.Apply(u, null, hq, Hull(StationCombat.MaxHull(u, hq) * 0.6f), u.PlayerActor);
        Assert.False(EconomicHoldout.ShieldUp(u, hq));
    }

    static HitResult Hull(float damage) => new(true, 1f, 0f, 0f, damage);

    /// <summary>Verbündete verteidigen mit: ihre Streifen in Reichweite folgen dem Alarm.</summary>
    [Fact]
    public void Alarm_VerbuendeteFolgen()
    {
        var u = War();
        var farm = u.Stations.Values.First(x => x.Name == "Farm Beta");
        var f = u.FactionOf(farm)!.Value;
        var other = u.NpcFactionIds().First(x => x != f);
        var ally = u.ShipsById().First(s => ThreatMap.IsPolice(s) && u.FactionOf(s) == other);
        if (Squadrons.Get(u, ally) is { } asq) Squadrons.Disband(u, asq);
        if (ally.Command is not null) Commands.Cancel(u, ally);
        Place(u, ally, farm.Sector, farm.Position + new Vec2(200f, 0f));
        farm.Condition = new StationCondition { AlarmUntil = u.Tick + 1000 };
        Assert.Null(EconomicHoldout.AlarmSectorFor(u, ally));
        Alliances.Form(u, f, other);
        u.Tick++;   // neue Zuteilung im nächsten Takt
        Assert.Equal(farm.Sector, EconomicHoldout.AlarmSectorFor(u, ally));
        Assert.Contains(other, EconomicHoldout.AlliedDefenders(u, farm));
    }

    /// <summary>Ohne die Schalter: kein Alarm, kein Notschild (alte Regel).</summary>
    [Fact]
    public void SchalterAus_KeinAlarmKeinNotschild()
    {
        var u = WarOld();
        u.Rules.EconomicStationDefense = true;
        var farm = u.Stations.Values.First(x => x.Name == "Farm Beta");
        var ev = new List<SimEvent>();
        u.Events.Raised += ev.Add;
        StationCombat.Apply(u, null, farm, Hull(StationCombat.MaxHull(u, farm) * 0.6f), u.PlayerActor);
        Assert.False(EconomicHoldout.Alarmed(u, farm) || EconomicHoldout.ShieldUp(u, farm));
        Assert.DoesNotContain(ev, e => e is StationAlarmRaised or StationEmergencyShield);
        Assert.Equal(0L, farm.Condition!.AlarmUntil);
    }
}
