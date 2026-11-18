using SpaceSim.Core.Combat;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Kapitulation (docs/KAMPF.md „Kapitulation“): die angegriffene Station würfelt an Hüllenschwellen, ob sie aufgibt (Besitz an den Angreifer — Spieler oder
/// KI-Fraktion im Krieg), sonst wird sie bei Hülle 0 zerstört — auch Regierungsstationen. Piratenstationen kapitulieren nie (Verstecke nur ausgeschaltet).
/// Verliert eine KI-Fraktion ihren Sitz, entsteht ein Not-Sitz; Spielerbesitz zahlt Unterhalt aus der Spielerkasse und verfällt nicht.
/// </summary>
public class KapitulationTests
{
    static readonly FactionId Union = new(1), Guild = new(2);

    static Universe Standard() => TestWorlds.Default(null, fuel: false, research: false);

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    static HitResult HullHit(float hull) => new(true, 1f, 0f, 0f, hull);

    /// <summary>Hülle der Station auf einen Anteil setzen (ohne Treffer).</summary>
    static void SetShare(Universe u, Station st, float share)
    {
        float max = StationCombat.MaxHull(u, st);
        st.Condition = new StationCondition { HullMissing = max * (1f - share), ShieldMissing = StationCombat.MaxShield(u, st) };
    }

    /// <summary>Trifft die Station so, dass ihr Hüllenanteil auf <paramref name="to"/> fällt.</summary>
    static void HitTo(Universe u, Station st, float to, ActorId attacker, Encounter? e = null)
    {
        float max = StationCombat.MaxHull(u, st);
        StationCombat.Apply(u, e, st, HullHit(MathF.Max(0.01f, StationCombat.Hull(u, st) - to * max)), attacker);
    }

    static void ForceChance(Universe u, float c)
    {
        u.Rules.CapitulationChances = new() { c, c };
        u.Rules.CapitulationChanceMin = c;
        u.Rules.CapitulationChanceMax = c;
    }

    static Station GuildEconomic(Universe u) => u.StationsById().First(st => !st.IsBuildSite && u.FactionOf(st) == Guild && StationProtection.IsEconomic(u, st) && st.Sector != u.PlayerSector);
    static Station GuildHq(Universe u) => u.StationsById().First(st => !st.IsBuildSite && st.Owner == (ActorId)Guild && StationProtection.KindOf(u, st) == GovernmentKind.Headquarters);

    [Fact]
    public void Standardwelt_RegelAn_KiUebernahmeAn()
    {
        var u = Standard();
        Assert.True(u.Rules.StationCapitulation);
        Assert.True(u.Rules.AiStationCapture);
        Assert.False(new EconomyRules().StationCapitulation);   // Code-Standard aus (Testwelten und alte Stände: Phase-28-Regel)
    }

    [Fact]
    public void Chance_Formel_Schwelle_Verteidiger_Angreifer_Aggression_Grenzen()
    {
        var u = Standard();
        var st = GuildEconomic(u);
        var r = u.Rules;
        float aggr = u.Actors[st.Owner].Personality.Aggression;
        float baseAdj = -(aggr - 0.5f) * r.CapitulationAggressionWeight;
        Assert.Equal(Math.Clamp(r.CapitulationChances[0] + baseAdj, r.CapitulationChanceMin, r.CapitulationChanceMax), Capitulation.Chance(u, st, 0, 0, 1), 4);
        Assert.Equal(Math.Clamp(r.CapitulationChances[1] + baseAdj, r.CapitulationChanceMin, r.CapitulationChanceMax), Capitulation.Chance(u, st, 1, 0, 1), 4);
        // ein Verteidiger weniger Chance, ein weiterer Angreifer mehr
        Assert.True(Capitulation.Chance(u, st, 1, 2, 1) < Capitulation.Chance(u, st, 1, 0, 1));
        Assert.True(Capitulation.Chance(u, st, 1, 0, 3) > Capitulation.Chance(u, st, 1, 0, 1));
        // Angreiferbonus gedeckelt
        Assert.Equal(Capitulation.Chance(u, st, 0, 0, 1 + r.CapitulationAttackerMax), Capitulation.Chance(u, st, 0, 0, 20), 5);
        // Grenzen
        Assert.Equal(r.CapitulationChanceMin, Capitulation.Chance(u, st, 0, 50, 1), 5);
        u.Actors[st.Owner].Personality = new Personality(1f, 0.5f, 0.5f, 0.5f);
        float aggressive = Capitulation.Chance(u, st, 1, 0, 1);
        u.Actors[st.Owner].Personality = new Personality(0f, 0.5f, 0.5f, 0.5f);
        Assert.True(Capitulation.Chance(u, st, 1, 0, 1) > aggressive, "friedliche Besitzer geben eher auf");
        // fehlt ein Wert der Liste, gilt der letzte
        Assert.Equal(r.CapitulationChances[^1], Capitulation.BaseChance(r, 7));
    }

    [Fact]
    public void Uebernehmer_SpielerKiImKriegNiePiratenNieOhneKrieg()
    {
        var u = Standard();
        var st = GuildEconomic(u);
        Assert.Equal(u.PlayerActor, Capitulation.TakerFor(u, st, u.PlayerActor));
        Assert.Null(Capitulation.TakerFor(u, st, (ActorId)Union));        // ohne Krieg übernimmt keine KI
        Wars.Declare(u, Union, Guild);
        Assert.Equal((ActorId)Union, Capitulation.TakerFor(u, st, (ActorId)Union));
        Assert.Null(Capitulation.TakerFor(u, st, (ActorId)Guild));        // eigene Station
        u.Rules.AiStationCapture = false;
        Assert.Null(Capitulation.TakerFor(u, st, (ActorId)Union));
        Assert.Equal(u.PlayerActor, Capitulation.TakerFor(u, st, u.PlayerActor));
        // Piraten: Versteck und gesetzlose Besitzer kapitulieren nie; Gesetzlose übernehmen nichts
        var hideout = u.StationsById().First(x => StationProtection.IsHideout(u, x));
        Assert.True(Capitulation.IsPirate(u, hideout));
        Assert.Null(Capitulation.TakerFor(u, hideout, u.PlayerActor));
        var outlaw = hideout.Owner;
        Assert.Null(Capitulation.TakerFor(u, st, outlaw));
        st.Owner = outlaw;
        Assert.Null(Capitulation.TakerFor(u, st, u.PlayerActor));
        u.Rules.StationCapitulation = false;
        Assert.Null(Capitulation.TakerFor(u, GuildHq(u), u.PlayerActor));
    }

    [Fact]
    public void Schwelle_GenauEinZug_NurBeimUnterschreiten()
    {
        var u = Standard();
        var st = GuildEconomic(u);
        ForceChance(u, 0f);
        u.Rules.CapitulationChanceMin = 0f;
        var ev = Capture(u);
        SetShare(u, st, 0.6f);
        ulong before = u.Rng.State;
        HitTo(u, st, 0.4f, u.PlayerActor);                     // über allen Schwellen: kein Zug
        Assert.Equal(before, u.Rng.State);
        Assert.Empty(ev.OfType<StationCapitulationRolled>());
        HitTo(u, st, 0.2f, u.PlayerActor);                     // unter 25 %: genau ein Zug
        var expected = new DeterministicRng(before); expected.NextDouble();
        Assert.Equal(expected.State, u.Rng.State);
        Assert.Single(ev.OfType<StationCapitulationRolled>());
        HitTo(u, st, 0.15f, u.PlayerActor);                    // keine neue Schwelle
        Assert.Single(ev.OfType<StationCapitulationRolled>());
        HitTo(u, st, 0.05f, u.PlayerActor);                    // unter 10 %: zweiter Zug
        Assert.Equal(2, ev.OfType<StationCapitulationRolled>().Count());
        Assert.All(ev.OfType<StationCapitulationRolled>(), x => Assert.False(x.Capitulated));
        // ein Treffer über beide Schwellen: zwei Züge
        var st2 = u.StationsById().Where(x => x.Id != st.Id && !x.IsBuildSite && u.FactionOf(x) == Guild && StationProtection.IsEconomic(u, x)).First();
        SetShare(u, st2, 0.9f);
        HitTo(u, st2, 0.05f, u.PlayerActor);
        Assert.Equal(2, ev.OfType<StationCapitulationRolled>().Count(x => x.Station == st2.Id));
    }

    [Fact]
    public void Kapitulation_AnSpieler_BesitzSektoreinflussVerbrechenOhneKrieg()
    {
        var u = Standard();
        var hq = u.StationsById().First(x => x.TypeKey == "sector_hq" && u.FactionOf(x) == Guild);
        ForceChance(u, 1f);
        var ev = Capture(u);
        SetShare(u, hq, 0.5f);
        float credits = hq.Credits;
        HitTo(u, hq, 0.2f, u.PlayerActor);
        Assert.Equal(u.PlayerActor, hq.Owner);
        Assert.Contains(ev.OfType<StationFell>(), f => f.How == "capitulated" && f.Station == hq.Id && f.Attacker == u.PlayerActor);
        Assert.Equal(credits, hq.Credits);   // Betriebskasse bleibt bei der Station
        Assert.False(StationCombat.IsDown(u, hq));
        Assert.True(StationCombat.Hull(u, hq) >= u.Rules.CapitulationHullFloor * StationCombat.MaxHull(u, hq) - 0.01f);
        // Verbrechen außerhalb des Krieges
        Assert.Contains(ev.OfType<CrimeReported>(), c => c.Crime == "capture_station" && c.Faction == Guild);
        // Sektoreinfluss: die Spielerfraktion hat jetzt Einfluss im Sektor
        var inf = SectorControl.Compute(u, u.Sectors[hq.Sector]);
        Assert.True(inf.Shares.GetValueOrDefault(u.PlayerFaction!.Value) > 0f);
        // weitere Treffer des Spielers schaden der eigenen Station nicht
        float hull = StationCombat.Hull(u, hq);
        StationCombat.Apply(u, null, hq, HullHit(500f), u.PlayerActor);
        Assert.Equal(hull, StationCombat.Hull(u, hq));
    }

    [Fact]
    public void Kapitulation_ImKrieg_KeinVerbrechen()
    {
        var u = Standard();
        var st = GuildEconomic(u);
        Wars.PlayerDeclare(u, Guild);
        ForceChance(u, 1f);
        var ev = Capture(u);
        SetShare(u, st, 0.5f);
        HitTo(u, st, 0.2f, u.PlayerActor);
        Assert.Equal(u.PlayerActor, st.Owner);
        Assert.DoesNotContain(ev.OfType<CrimeReported>(), c => c.Crime == "capture_station");
        Assert.True(Wars.PlayerWar(u, Guild)!.LossB + Wars.PlayerWar(u, Guild)!.LossA > 0f, "Verlust zählt im Krieg");
    }

    [Fact]
    public void KiImKrieg_UebernimmtStation_FuerIhreFraktion()
    {
        var u = Standard();
        var st = GuildEconomic(u);
        Wars.Declare(u, Union, Guild);
        ForceChance(u, 1f);
        SetShare(u, st, 0.5f);
        HitTo(u, st, 0.2f, (ActorId)Union);
        Assert.Equal((ActorId)Union, st.Owner);
        Assert.True(Wars.Between(u, Union, Guild)!.LossB > 0f);
    }

    [Fact]
    public void Regierungsstation_OhneKapitulation_WirdZerstoert_SitzWirdVerlegt()
    {
        var u = Standard();
        var hq = GuildHq(u);
        ForceChance(u, 0f);
        u.Rules.CapitulationChanceMin = 0f;
        var ev = Capture(u);
        SetShare(u, hq, 0.3f);
        u.Actors[Guild].Treasury.Credits = 1000f;
        HitTo(u, hq, 0f, u.PlayerActor);
        Assert.False(u.Stations.ContainsKey(hq.Id));
        var dep = Assert.Single(ev.OfType<StationDeparted>(), d => d.Station == hq.Id);
        Assert.Equal(DepartureReason.Destroyed, dep.Reason);
        Assert.Equal("siege", dep.Detail);
        var seat = Assert.Single(ev.OfType<SeatRelocated>());
        Assert.Equal(Guild, seat.Faction);
        Assert.True(seat.Paid <= 1000f && seat.Paid >= 0f);
        Assert.True(u.Actors[Guild].Treasury.Credits >= 0f);
        Assert.True(SeatRelocation.HasSeat(u, Guild));
        Assert.DoesNotContain(FactionViability.Check(u, Guild), i => i.Kind == ViabilityIssueKind.NoSeat);
        // der Not-Sitz steht frei (kein Überlappen mit Stationen)
        var newSeat = u.Stations[seat.Station];
        foreach (var id in u.Sectors[newSeat.Sector].Stations)
            if (id != newSeat.Id) Assert.True(Vec2.Distance(u.Stations[id].Position, newSeat.Position) >= u.Stations[id].Radius + newSeat.Radius);
    }

    [Fact]
    public void Hauptquartier_Kapituliert_FraktionBleibtHandlungsfaehig()
    {
        var u = Standard();
        var hq = GuildHq(u);
        ForceChance(u, 1f);
        SetShare(u, hq, 0.5f);
        HitTo(u, hq, 0.2f, u.PlayerActor);
        Assert.Equal(u.PlayerActor, hq.Owner);
        Assert.True(SeatRelocation.HasSeat(u, Guild));
        Assert.DoesNotContain(FactionViability.Check(u, Guild), i => i.Kind == ViabilityIssueKind.NoSeat);
    }

    [Fact]
    public void Piratenversteck_WirdNurAusgeschaltet_StellungZerstoert()
    {
        var u = Standard();
        var hideout = u.StationsById().First(x => StationProtection.IsHideout(u, x));
        var ev = Capture(u);
        SetShare(u, hideout, 0.3f);
        ulong before = u.Rng.State;
        HitTo(u, hideout, 0f, u.PlayerActor);
        Assert.Equal(before, u.Rng.State);   // kein Kapitulationswurf
        Assert.True(u.Stations.ContainsKey(hideout.Id));
        Assert.True(StationCombat.IsDown(u, hideout));
        Assert.Contains(ev.OfType<StationFell>(), f => f.How == "disabled" && f.Station == hideout.Id);
        Assert.DoesNotContain(FactionViability.Check(u, hideout.Owner), i => i.Kind == ViabilityIssueKind.NoHideout);
    }

    [Fact]
    public void RegelAus_Phase28Verhalten_RegierungNurAusgeschaltet()
    {
        var u = Standard();
        u.Rules.StationCapitulation = false;
        var hq = GuildHq(u);
        SetShare(u, hq, 0.5f);
        ulong before = u.Rng.State;
        HitTo(u, hq, 0f, u.PlayerActor);
        Assert.Equal(before, u.Rng.State);
        Assert.True(u.Stations.ContainsKey(hq.Id));
        Assert.True(StationCombat.IsDown(u, hq));
        Assert.Equal((ActorId)Guild, hq.Owner);
    }

    [Fact]
    public void Spielerstation_UnterhaltAusDerKasse_KeinePleite()
    {
        var u = Standard();
        var st = GuildEconomic(u);
        ForceChance(u, 1f);
        SetShare(u, st, 0.5f);
        HitTo(u, st, 0.2f, u.PlayerActor);
        Assert.Equal(u.PlayerActor, st.Owner);
        var type = u.StationTypes[st.TypeKey];
        type.UpkeepPerTick = type.IncomePerTick + 0.5f;   // Testannahme: Unterhalt über dem Grundeinkommen (in der Standardwelt tragen Stationen ihn selbst)
        st.Credits = 0f;
        u.Player.Treasury.Credits = 1000f;
        new UpkeepSystem().Step(u, 10);
        Assert.Equal(0f, st.Credits, 3);
        Assert.Equal(1000f - (type.UpkeepPerTick - type.IncomePerTick) * 10f, u.Player.Treasury.Credits, 2);
        // Pleite und Stilllegung berühren Spielerbesitz nicht
        st.Credits = -1f;
        u.Player.Treasury.Credits = 0f;
        st.BelowLimitSince = 0;
        u.Tick = u.Rules.BankruptcyTicks * 4L;
        new StationLifecycleSystem().Step(u, 1);
        Assert.True(u.Stations.ContainsKey(st.Id));
        Assert.False(StationReview.Reviewable(u, st));
    }

    [Fact]
    public void Anzeige_NaechsteSchwelle()
    {
        var u = Standard();
        var st = GuildEconomic(u);
        SetShare(u, st, 0.8f);
        var next = Capitulation.Next(u, st, u.PlayerActor);
        Assert.NotNull(next);
        Assert.Equal(0.25f, next!.Value.Threshold, 4);
        SetShare(u, st, 0.2f);
        Assert.Equal(0.1f, Capitulation.Next(u, st, u.PlayerActor)!.Value.Threshold, 4);
        SetShare(u, st, 0.05f);
        Assert.Null(Capitulation.Next(u, st, u.PlayerActor));
        Assert.Null(Capitulation.Next(u, u.StationsById().First(x => StationProtection.IsHideout(u, x)), u.PlayerActor));
    }

    [Fact]
    public void SpeichernLaden_NachKapitulationUndNotSitz_BitIdentisch()
    {
        var u = Standard();
        ForceChance(u, 1f);
        var hq = GuildHq(u);
        SetShare(u, hq, 0.5f);
        HitTo(u, hq, 0.2f, u.PlayerActor);
        Assert.Equal(u.PlayerActor, hq.Owner);
        var runner = new SimulationRunner();
        for (int i = 0; i < 200; i++) runner.Step(u);
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (copy, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        var r2 = new SimulationRunner();
        for (int i = 0; i < 400; i++) { runner.Step(u); r2.Step(copy); }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
        Assert.Equal(u.PlayerActor, copy.Stations[hq.Id].Owner);
    }

    [Fact]
    public void OhneBelagerung_NpcWeltUnveraendert_RegelAnGleichAus()
    {
        var a = Standard();
        var b = Standard();
        b.Rules.StationCapitulation = false;
        var ra = new SimulationRunner();
        var rb = new SimulationRunner();
        for (int i = 0; i < 3000; i++) { ra.Step(a); rb.Step(b); }
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }
}
