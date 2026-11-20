using SpaceSim.Core;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 8 — Stationsverschleiß (docs/STATIONEN.md „Stationsverschleiß“): Zustand sinkt bei offener Instandhaltung, steigt mit Reparaturmaterial,
/// langsamere Produktion unter 70 %, Stillstand unter 30 %, geschützte/Regierungsstationen höchstens bis 50 %. Dazu Nachtrag 2b (Seeschlacht: Abkühlzeit je Paar,
/// Rücklage zählt kämpfende Kriegsschiffe).
/// </summary>
public class StationsverschleissTests
{
    static readonly FactionId A = new(1), B = new(2);

    /// <summary>Regel dieser Runde zurücknehmen (Teil der Rücknahmekette über <see cref="BalanceFixTests.RevertBalanceFix"/>): kein Verschleiß.</summary>
    internal static void RevertWear(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.StationWear = false;
    }

    static Universe World()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        return u;
    }

    /// <summary>Instand gehaltene Station mit Rezept (unbeschützt oder beschützt nach Wunsch) und ihr Tagesbedarf.</summary>
    static (Station St, List<(GoodId Good, float PerDay)> Need) Station(Universe u, bool isProtected)
    {
        var st = u.StationsById().First(s => !s.IsBuildSite && s.Production.Count > 0 && Maintenance.Applies(u, s) && !StationProtection.IsOfficial(u, s));
        st.BankruptcyProtected = isProtected;
        return (st, Maintenance.NeedPerDay(u, u.StationTypes[st.TypeKey]));
    }

    static void Empty(Station st, List<(GoodId Good, float PerDay)> need)
    {
        foreach (var (g, _) in need) st.Stock(g).Amount = 0f;
    }

    static void Fill(Station st, List<(GoodId Good, float PerDay)> need, float days)
    {
        foreach (var (g, perDay) in need) st.Stock(g).Amount = perDay * days;
        st.MaintenanceBacklog.Clear();
    }

    [Fact]
    public void Standardwelt_SchaltetEin_CodeVorgabeAus()
    {
        Assert.False(new EconomyRules().StationWear);
        Assert.True(DemoUniverse.Create().Rules.StationWear);
        Assert.Equal(0, new EconomyRules().NavalBattleCooldownTicks);
        Assert.False(new EconomyRules().WarDefenseCountsEngaged);
        var u = DemoUniverse.Create();
        Assert.Equal(0.75f, u.Rules.WearEmergencyBelow);
        Assert.Equal(0f, new EconomyRules().WearEmergencyBelow);
        Assert.Equal(600, u.Rules.NavalBattleCooldownTicks);
        Assert.True(u.Rules.WarDefenseCountsEngaged);
    }

    [Fact]
    public void Verfall_EinProzentJeTag_BeiOffenemRueckstand_UngeschuetztBisNull()
    {
        var u = World();
        var (st, need) = Station(u, false);
        u.Rules.WearEmergencyBelow = 0f;   // ohne Notinstandhaltung (eigener Test)
        var sys = new MaintenanceSystem();
        int day = u.Rules.DayTicks;
        for (int d = 0; d < 10; d++) { Empty(st, need); sys.Step(u, day); }
        // der erste Tag baut den Rückstand auf (danach > 50 % des Tagesbedarfs) → 9 bis 10 Tage Verfall
        Assert.InRange(st.Integrity, 0.899f, 0.911f);
        st.Integrity = 0.05f;
        for (int d = 0; d < 10; d++) { Empty(st, need); sys.Step(u, day); }
        Assert.Equal(0f, st.Integrity);
        Assert.Equal(StationWearLevel.Stopped, StationWear.Level(u, st));
        // Regel aus: nichts passiert
        u.Rules.StationWear = false;
        st.Integrity = 0.8f;
        Empty(st, need); sys.Step(u, day);
        Assert.Equal(0.8f, st.Integrity);
    }

    [Fact]
    public void Verfall_GeschuetzteUndRegierungsstationen_HoechstensBisFuenfzigProzent()
    {
        var u = World();
        var (st, need) = Station(u, true);
        u.Rules.WearEmergencyBelow = 0f;
        var gov = u.StationsById().First(s => StationProtection.IsOfficial(u, s) && Maintenance.Applies(u, s));
        gov.BankruptcyProtected = false;
        var gneed = Maintenance.NeedPerDay(u, u.StationTypes[gov.TypeKey]);
        st.Integrity = gov.Integrity = 0.52f;
        var sys = new MaintenanceSystem();
        for (int d = 0; d < 10; d++) { Empty(st, need); Empty(gov, gneed); sys.Step(u, u.Rules.DayTicks); }
        Assert.Equal(0.5f, st.Integrity);
        Assert.Equal(0.5f, gov.Integrity);
        Assert.Equal(0.5f, StationWear.Floor(u, gov));
        Assert.Equal(StationWearLevel.Slow, StationWear.Level(u, st));
    }

    [Fact]
    public void Erholung_MitMaterial_ZweiProzentJeTag_VerbrauchtZusaetzlichenTagesbedarf()
    {
        var u = World();
        var (st, need) = Station(u, false);
        st.Integrity = 0.5f;
        Fill(st, need, 10f);
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        new MaintenanceSystem().Step(u, u.Rules.DayTicks);
        Assert.InRange(st.Integrity, 0.5199f, 0.5201f);
        // Instandhaltung (1 Tagesbedarf) + Reparatur (2 % × 0,5 × 100 = 1 Tagesbedarf)
        foreach (var (g, perDay) in need) Assert.InRange(st.Market[g].Amount, perDay * 8f - 1e-3f, perDay * 8f + 1e-3f);
        var rep = events.OfType<StationWearRepaired>().Single();
        Assert.True(rep.MaterialValue > 0f);
        // ohne Material nur anteilig: 1,5 Tagesbedarfe im Lager → nach der Instandhaltung bleibt ein halber für die Reparatur (+1 %)
        st.Integrity = 0.5f;
        Fill(st, need, 1.5f);
        new MaintenanceSystem().Step(u, u.Rules.DayTicks);
        Assert.InRange(st.Integrity, 0.5099f, 0.5101f);
        // nie über 100 %
        st.Integrity = 0.995f;
        Fill(st, need, 10f);
        new MaintenanceSystem().Step(u, u.Rules.DayTicks);
        Assert.Equal(1f, st.Integrity);
    }

    [Fact]
    public void Notinstandhaltung_KasseKauftRueckstandZumAufpreis_NurUnterSchwelle_NieUnterRuecklage_NieSpieler()
    {
        var u = World();
        var (st, need) = Station(u, false);
        var actor = u.Actors[u.FactionOf(st)!.Value];
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        var sys = new MaintenanceSystem();
        // über der Schwelle: kein Kauf, der Zustand sinkt
        st.Integrity = 0.8f;
        actor.Treasury.Credits = 1_000_000f;
        Empty(st, need); sys.Step(u, u.Rules.DayTicks);
        Empty(st, need); sys.Step(u, u.Rules.DayTicks);
        Assert.DoesNotContain(events, e => e is StationWearEmergency);
        Assert.True(st.Integrity < 0.8f);
        // unter der Schwelle: die Kasse kauft den Rückstand (Basiswert × Aufpreis), der Rückstand ist weg
        st.Integrity = 0.74f;
        Empty(st, need);
        float before = actor.Treasury.Credits;
        sys.Step(u, u.Rules.DayTicks);
        var em = events.OfType<StationWearEmergency>().Single();
        Assert.Equal(st.Id, em.Station);
        Assert.Empty(st.MaintenanceBacklog);
        float backlogValue = need.Sum(n => n.PerDay * u.Goods[n.Good].BasePrice);   // voller Tagesbedarf offen
        Assert.InRange(em.Cost, backlogValue * u.Rules.WearEmergencyMarkup * 0.99f, backlogValue * u.Rules.WearEmergencyMarkup * 1.01f);
        Assert.True(before - actor.Treasury.Credits >= em.Cost - 0.01f);
        Assert.Contains(events, e => e is TreasuryUpkeep { Purpose: "wear_emergency" });
        // nie unter die Rücklage: leere Kasse → kein Kauf, der Verfall läuft weiter
        events.Clear();
        actor.Treasury.Credits = 0f;
        float i0 = st.Integrity;
        Empty(st, need); sys.Step(u, u.Rules.DayTicks);
        Empty(st, need); sys.Step(u, u.Rules.DayTicks);
        Assert.DoesNotContain(events, e => e is StationWearEmergency);
        Assert.True(st.Integrity < i0);
        // Spielerstation: nie Notinstandhaltung
        st.Owner = u.PlayerActor;
        u.PlayerWallet.Credits = 1_000_000f;
        Empty(st, need); sys.Step(u, u.Rules.DayTicks);
        Empty(st, need); sys.Step(u, u.Rules.DayTicks);
        Assert.DoesNotContain(events, e => e is StationWearEmergency);
    }

    [Fact]
    public void Produktion_UnterSiebzigLangsamer_UnterDreissigStillstand()
    {
        var u = World();
        var st = u.StationsById().First(s => !s.IsBuildSite && s.Production.Count == 1 && s.Production[0].Inputs.Count > 0 && Maintenance.Applies(u, s));
        var r = st.Production[0];
        float Produce(float integrity)
        {
            st.Integrity = integrity;
            foreach (var (g, q) in r.Inputs) st.Stock(g).Amount = q * 5f;
            var og = r.Outputs.Keys.First();
            st.Stock(og).Amount = 0f;
            st.Stock(og).Target = 1000f;
            while (u.Tick % r.TicksPerCycle != 0) u.Tick++;
            new ProductionSystem().Step(u, 1);
            u.Tick++;
            return st.Stock(og).Amount;
        }
        float full = Produce(1f);
        Assert.True(full > 0f);
        Assert.Equal(full, Produce(0.75f));
        Assert.InRange(Produce(0.56f), full * 0.8f - 1e-4f, full * 0.8f + 1e-4f);
        Assert.Equal(0f, Produce(0.29f));
        Assert.Equal(0f, StationWear.ProductionFactor(u, st));
        Assert.Contains("Stillstand", StationWear.Text(u, st));
        u.Rules.StationWear = false;
        Assert.Equal(full, Produce(0.29f));   // Regel aus: Zustand wirkt nicht
    }

    [Fact]
    public void SpeichernLaden_ZustandBitgleich_UndNurGehashtWennUngleichEins()
    {
        var u = World();
        var (st, _) = Station(u, false);
        ulong h1 = UniverseHash.Compute(u);
        st.Integrity = 0.63f;
        Assert.NotEqual(h1, UniverseHash.Compute(u));
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(0.63f, v.Stations[st.Id].Integrity);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        new SimulationRunner().Run(u, 2400);
        new SimulationRunner().Run(v, 2400);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        st.Integrity = 1f;
        Assert.DoesNotContain("\"Integrity\"", SaveGameJson.Write(World(), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Langlauf_20000Ticks_KeineZerfallsspirale_GeschuetzteNieUnterGrenze()
    {
        var u = DemoUniverse.Create();
        var r = new SimulationRunner();
        for (int i = 0; i < 20000; i++)
        {
            r.Step(u);
            if (i % 500 != 0) continue;
            foreach (var st in u.Stations.Values)
            {
                Assert.InRange(st.Integrity, 0f, 1f);
                if (st.BankruptcyProtected || StationProtection.IsOfficial(u, st)) Assert.True(st.Integrity >= 0.5f - 1e-5f, $"{st.Name} {st.Integrity}");
            }
        }
        var maintained = u.Stations.Values.Where(s => Maintenance.Applies(u, s)).ToList();
        Assert.True(maintained.Average(s => s.Integrity) > 0.8f, $"Ø {maintained.Average(s => s.Integrity)}");
        Assert.True(maintained.Count(s => s.Integrity < u.Rules.WearStopBelow) <= 2, string.Join(", ", maintained.Where(s => s.Integrity < 0.3f).Select(s => s.Name)));
    }

    // ───────── Nachtrag 2b ─────────

    static Ship Warship(Universe u, FactionId f, SectorId sector, Vec2 pos)
    {
        var m = u.ShipModels["bastion"];
        var yard = u.StationsById().First(s => !s.IsBuildSite);
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Warship, Controller.Ai, new ActorId(f.Value), yard, 0f);
        u.AddShip(s);
        Place(u, s, sector, pos);
        return s;
    }

    static void Place(Universe u, Ship s, SectorId sector, Vec2 pos)
    {
        if (s.Transit is not null) u.FinishTransit(s);
        s.Order = null; s.Command = null;
        if (s.Sector != sector) u.MoveShip(s, sector);
        s.Position = pos;
        s.DockedAt = default;
    }

    static SectorId CleanSector(Universe u, FactionId owner)
    {
        var sector = u.Sectors.Values.OrderBy(s => s.Id.Value).First(s => s.Id != u.PlayerSector && s.Owner == owner).Id;
        var away = u.Sectors.Keys.OrderBy(k => k.Value).First(k => k != sector && k != u.PlayerSector);
        foreach (var s in u.ShipsById().ToList())
            if ((ThreatMap.IsPolice(s) || ThreatMap.IsRaider(s) || Wars.IsWarship(s)) && ThreatMap.Where(u, s).Sector == sector) Place(u, s, away, new Vec2(300f, 300f));
        return sector;
    }

    static Universe WarWorld()
    {
        var u = World();
        u.PlayerWallet.Credits = 50_000f;
        return u;
    }

    [Fact]
    public void Seeschlacht_Abkuehlzeit_JePaarUndSektor_UndSpeichern()
    {
        var u = WarWorld();
        var sector = CleanSector(u, A);
        Wars.Declare(u, B, A);
        var w1 = Warship(u, B, sector, new Vec2(0f, 200f));
        var w2 = Warship(u, A, sector, new Vec2(250f, 200f));
        NavalBattle.Trigger(u);
        var e = u.Fights.Encounters.Single(x => x.Cause == EncounterCause.Naval);
        // eine Seite verschwindet: die Schlacht endet im nächsten Gefechtsschritt
        u.RemoveShip(w1.Id, "test");
        var r = new SimulationRunner();
        for (int i = 0; i < 50 && u.Fights.Encounters.Contains(e); i++) r.Step(u);
        Assert.DoesNotContain(e, u.Fights.Encounters);
        long ended = u.NavalEnded[(sector.Value, Math.Min(A.Value, B.Value), Math.Max(A.Value, B.Value))];
        Assert.True(NavalBattle.Cooling(u, sector, A, B));
        // neue Gegner im selben Sektor: keine neue Schlacht in der Abkühlzeit
        if (u.Ships.ContainsKey(w2.Id)) Place(u, w2, sector, new Vec2(250f, 200f)); else w2 = Warship(u, A, sector, new Vec2(250f, 200f));
        w2.Hull = w2.MaxHull; w2.Emergency = false;
        var w3 = Warship(u, B, sector, new Vec2(0f, 200f));
        NavalBattle.Trigger(u);
        Assert.DoesNotContain(u.Fights.Encounters, x => x.Cause == EncounterCause.Naval && !x.Done);
        // Speichern/Laden: Abkühlzeit bleibt
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.True(NavalBattle.Cooling(v, sector, A, B));
        // nach der Abkühlzeit: Schlacht beginnt, abgelaufener Eintrag wird weggeräumt
        u.Tick = ended + u.Rules.NavalBattleCooldownTicks;
        NavalBattle.Trigger(u);
        Assert.Contains(u.Fights.Encounters, x => x.Cause == EncounterCause.Naval && x.Of(w3.Id) is not null);
        Assert.Empty(u.NavalEnded);
        // Regel aus: sofort
        u.Rules.NavalBattleCooldownTicks = 0;
        Assert.False(NavalBattle.Cooling(u, sector, A, B));
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    public void Abwehr_RuecklageZaehltKaempfendeKriegsschiffe(bool countEngaged, int expected)
    {
        var u = WarWorld();
        u.Rules.WarDefenseCountsEngaged = countEngaged;
        u.Rules.WarDefenseMaxResponders = 5;
        Wars.Declare(u, B, A);
        var target = u.StationsById().First(st => !st.IsBuildSite && st.Dockable && u.FactionOf(st) == A && st.Sector != u.PlayerSector);
        var enemy = Warship(u, B, target.Sector, target.Position + new Vec2(80f, 0f));
        enemy.Command = new AssaultCommand(target.Sector, target.Id);
        var engaged = Warship(u, A, target.Sector, target.Position + new Vec2(60f, 20f));
        var e = Siege.Start(u, enemy, target);
        if (e.Of(engaged.Id) is not { Active: true, Side: 1 }) Engage.Join(u, e, engaged, 1, CombatTactics.Siege, 20f);
        Assert.True(u.Fights.IsEngaged(engaged.Id));
        var near = u.Sectors[target.Sector].Gates.Select(g => u.Gates[u.Gates[g].Target].Sector).OrderBy(s => s.Value).First();
        var free = new[] { Warship(u, A, near, new Vec2(0f, 0f)), Warship(u, A, near, new Vec2(10f, 0f)), Warship(u, A, near, new Vec2(20f, 0f)) };
        u.Tick++;
        var a = WarNavy.Assignments(u);
        // vier Kriegsschiffe, Rücklage 1 → drei dürfen verteidigen; eins kämpft schon → mit Regel nur noch zwei freie
        Assert.Equal(expected, free.Count(s => a.ContainsKey(s.Id)));
        // je Ziel höchstens WarDefenseMaxResponders, kämpfende zählen mit
        u.Rules.WarDefenseMaxResponders = 2;
        u.Tick++;
        a = WarNavy.Assignments(u);
        Assert.Equal(countEngaged ? 1 : 2, free.Count(s => a.ContainsKey(s.Id)));
    }

    [Fact]
    public void Standardwelt_OhneKrieg_NachtragOhneWirkung()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 3000);
        Assert.Empty(u.NavalEnded);
    }
}
