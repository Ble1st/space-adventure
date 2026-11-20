using SpaceSim.Core;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 22: Schiffe und Stationen verschwinden nur noch aus drei Gründen — (a) zerstört, (b) pleite, (c) länger nicht rentabel (gleitende Rentabilitätsmessung
/// mit Schonfrist und Hysterese; die Uhr ruht hinter einer physischen Torsperre). Fraktionen bleiben handlungsfähig (Betriebsreserve, Mindestbestand, Ersatz mit
/// Vorrang, Notkauf, keine Stilllegung der letzten eigenen Versorgung).
/// </summary>
public class Phase22Tests
{
    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    /// <summary>Standardwelt wie im Spiel (11 Sektoren; Treibstoff und Forschung aus).</summary>
    static Universe Standard(ulong? seed = null) => TestWorlds.Default(seed, fuel: false, research: false);

    static readonly DepartureReason[] Decay = { DepartureReason.Destroyed, DepartureReason.Bankrupt, DepartureReason.Unprofitable };

    /// <summary>Jeder Abgang (Schiff/Station) ist von einem Abgangs-Ereignis mit einem der drei Verfallsgründe begleitet.</summary>
    static void AssertEveryRemovalHasDecayReason(List<SimEvent> ev)
    {
        var shipReasons = ev.OfType<ShipDeparted>().ToDictionary(d => d.Ship, d => d.Reason);
        var stationReasons = new Dictionary<StationId, DepartureReason>();
        foreach (var d in ev.OfType<StationDeparted>()) stationReasons[d.Station] = d.Reason;
        foreach (var r in ev.OfType<ShipRemoved>())
        {
            Assert.True(shipReasons.TryGetValue(r.Ship, out var why), $"Schiff {r.Ship.Value} ohne Abgangsgrund entfernt ({r.Reason})");
            Assert.Contains(why, Decay);
            Assert.Equal(DepartureNames.Key(why), r.Reason);
        }
        foreach (var r in ev.OfType<StationRemoved>())
        {
            Assert.True(stationReasons.TryGetValue(r.Station, out var why), $"Station {r.Station.Value} ohne Abgangsgrund entfernt ({r.Reason})");
            Assert.Contains(why, Decay);
        }
    }

    static Ship Trader(Universe u) => u.ShipsById().First(s => s.IsEconomyAi && s.Role == ShipRole.Trader);

    /// <summary>Setzt ein Schiff/eine Station auf „seit langem im Minus, Schonfrist vorbei“.</summary>
    static void MakeUnprofitable(Universe u, ProfitMeter m)
    {
        if (u.Tick < 100_000) u.Tick = 100_000;
        m.Since = 0; m.Rate = -0.5f; m.LossTicks = u.Rules.UnprofitableTicks + 10; m.Worth = 0f;
    }

    // ───────── Abgangswege: nur über a/b/c ─────────

    [Fact]
    public void Ueberfall_ZerstoertesOpferUndKaperschiff_GrundZerstoert()
    {
        var u = TestWorlds.Classic();
        var ev = Capture(u);
        var victim = Trader(u);
        victim.Cargo[DemoUniverse.Food] = 80f;
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        if (raider.Sector != victim.Sector) u.MoveShip(raider, victim.Sector);
        u.Encounters = new Fixed(attackerWins: true, destroyed: true);
        PirateSystem.ResolveRaid(u, raider, victim, u.Sectors[victim.Sector]);
        var d = Assert.Single(ev.OfType<ShipDeparted>());
        Assert.Equal((victim.Id, DepartureReason.Destroyed, "raid"), (d.Ship, d.Reason, d.Detail));
        // Abfangen: Polizei gewinnt, Kaperschiff zerstört
        var police = u.ShipsById().First(s => s.Role == ShipRole.Police);
        var r2 = u.ShipsById().First(ThreatMap.IsRaider);
        PirateSystem.Intercept(u, police, r2);
        Assert.Contains(ev.OfType<ShipDeparted>(), x => x.Ship == r2.Id && x.Reason == DepartureReason.Destroyed && x.Detail == "intercept");
        AssertEveryRemovalHasDecayReason(ev);
    }

    [Fact]
    public void Hehlerei_ZahltNurBisZurKappungDerPiratenkasse()
    {
        var u = TestWorlds.Classic();
        u.Rules.PirateTreasuryCap = 1000f;
        var victim = Trader(u);
        victim.Cargo[DemoUniverse.Food] = 80f;
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        if (raider.Sector != victim.Sector) u.MoveShip(raider, victim.Sector);
        var pirates = u.Actors[raider.Owner];
        pirates.Treasury.Credits = 900f;
        u.Encounters = new Fixed(attackerWins: true, destroyed: false);
        var ev = PirateSystem.ResolveRaid(u, raider, victim, u.Sectors[victim.Sector]);
        Assert.True(ev.LootValue * u.Rules.FenceShare > 100f);
        Assert.Equal(100f, ev.Fenced, 2);
        Assert.Equal(1000f, pirates.Treasury.Credits, 2);
    }

    sealed class Fixed(bool attackerWins, bool destroyed) : IEncounterResolver
    {
        public EncounterOutcome Resolve(Universe u, IReadOnlyList<Ship> attackers, IReadOnlyList<Ship> defenders, float destroyBase) =>
            new(attackerWins, 1f, 1f, attackerWins ? 1f : 0f, 0.1f, 0.1f, destroyed, attackerWins ? 0.5f : 0f);
    }

    [Fact]
    public void Pleite_NurOhneGeldFrachtUndAuftrag_NachFrist()
    {
        var u = Standard();
        var ship = Trader(u);
        if (ship.Transit is not null) u.FinishTransit(ship);
        ship.Order = null; ship.Phase = TradePhase.Idle; ship.Cargo.Clear();
        ship.Credits = 10f;
        ship.IdleSince = u.Tick;
        Assert.False(Departures.ShipBankrupt(u, ship));   // Frist läuft noch
        u.Tick += u.Rules.ShipBankruptTicks + 1;
        Assert.True(Departures.ShipBankrupt(u, ship));
        ship.Cargo[DemoUniverse.Food] = 3f;
        Assert.False(Departures.ShipBankrupt(u, ship));   // Fracht an Bord: noch Aussicht
        ship.Cargo.Clear();
        ship.Credits = u.Rules.ShipBankruptCredits + 1f;
        Assert.False(Departures.ShipBankrupt(u, ship));   // Geld da
        ship.Credits = 0f;
        var ev = Capture(u);
        ship.NextThinkTick = long.MaxValue;   // denkt in diesem Schritt nicht neu
        new FleetSystem().Step(u, 20);
        var d = Assert.Single(ev.OfType<ShipDeparted>(), x => x.Ship == ship.Id);
        Assert.Equal(DepartureReason.Bankrupt, d.Reason);
        AssertEveryRemovalHasDecayReason(ev);
    }

    [Fact]
    public void Unrentabel_SchiffWirdAusgemustert_NachSchonfristUndFenster_GeldAnBesitzer()
    {
        var u = Standard();
        var ship = Trader(u);
        if (ship.Transit is not null) u.FinishTransit(ship);
        ship.Order = null; ship.Phase = TradePhase.Idle; ship.Cargo.Clear(); ship.IdleSince = -1;
        ship.Credits = 900f;
        MakeUnprofitable(u, ship.Profit);
        ship.Profit.LossTicks = u.Rules.UnprofitableTicks - 100;
        Assert.False(Departures.ShipUnprofitable(u, ship));   // Fenster noch nicht voll
        ship.Profit.LossTicks = u.Rules.UnprofitableTicks;
        ship.Profit.Since = u.Tick - u.Rules.ProfitGraceTicks + 10;
        Assert.False(Departures.ShipUnprofitable(u, ship));   // Schonfrist
        ship.Profit.Since = 0;
        Assert.True(Departures.ShipUnprofitable(u, ship));
        var ev = Capture(u);
        new FleetSystem().Step(u, 20);
        var d = Assert.Single(ev.OfType<ShipDeparted>(), x => x.Ship == ship.Id);
        Assert.Equal(DepartureReason.Unprofitable, d.Reason);
        Assert.Contains(ev.OfType<ShipRetired>(), r => r.Ship == ship.Id && Math.Abs(r.Credits - 900f) < 0.01f);
        AssertEveryRemovalHasDecayReason(ev);
    }

    [Fact]
    public void Polizei_AbbauUeberzahl_GrundUnrentabel_NieUnterMindestbestand()
    {
        var u = TestWorlds.Classic();
        var r = u.Rules;
        var guild = new FactionId(2);
        var actor = u.Actors[guild];
        // Bedarf null → Abbau-Schwelle = Mindestbestand; Überzahl dauerhaft
        r.PolicePerThreat = 0f; r.PolicePerSector = 0f;
        actor.PoliceNeed = 0f;
        var ev = Capture(u);
        var sec = new SecuritySystem();
        for (int i = 0; i < 400; i++) { actor.PoliceNeed = 0f; sec.Step(u, 100); u.Tick += 100; }
        var police = SecuritySystem.PoliceOf(u, guild);
        Assert.Equal(r.PoliceMinPerFaction, police.Count);
        Assert.All(ev.OfType<ShipDeparted>(), d => Assert.Equal((DepartureReason.Unprofitable, "decommissioned"), (d.Reason, d.Detail)));
        AssertEveryRemovalHasDecayReason(ev);
    }

    [Fact]
    public void Bauschiff_Ueberzaehlig_GrundUnrentabel_MindestbestandBleibt()
    {
        var u = Standard();
        var f = new FactionId(1);
        var ev = Capture(u);
        u.Actors[f].Treasury.Credits = 100_000f;
        foreach (var yard in u.Stations.Values.Where(s => s.TypeKey == "shipyard")) foreach (var e in yard.Market.Values) e.Amount = MathF.Max(e.Amount, 5f);
        Assert.NotNull(ConstructionRules.CommissionBuilder(u, f, u.Stations.Values.First(s => s.Owner == (ActorId)f).Sector, default, priority: true));
        Assert.Equal(2, FactionBuilderSystem.Of(u, f).Count);
        foreach (var b in FactionBuilderSystem.Of(u, f)) { if (b.Transit is not null) u.FinishTransit(b); b.Order = null; b.IdleSince = 0; }
        u.Tick = u.Rules.IdleRetireTicks + 1000;
        new FactionBuilderSystem().Step(u, 100);
        Assert.Single(FactionBuilderSystem.Of(u, f));
        var d = Assert.Single(ev.OfType<ShipDeparted>());
        Assert.Equal((DepartureReason.Unprofitable, "spare_builder"), (d.Reason, d.Detail));
        new FactionBuilderSystem().Step(u, 100);
        Assert.Single(FactionBuilderSystem.Of(u, f));   // nie unter den Mindestbestand
        AssertEveryRemovalHasDecayReason(ev);
    }

    /// <summary>Zusätzliche, ungeschützte Station eines Typs neben einer bestehenden (wie vom Lebenszyklus gebaut).</summary>
    static Station AddBuilt(Universe u, Station like, Vec2 offset)
    {
        var st = StationFactory.Create(u, u.NewStationId(), u.StationTypes[like.TypeKey], "Test " + like.TypeKey, like.Sector, like.Position + offset, like.Owner);
        u.AddStation(st);
        return st;
    }

    [Fact]
    public void Station_PleiteUndUnrentabel_UndBaustellenVerfall_NurUeberABC()
    {
        var u = TestWorlds.Classic();
        var ev = Capture(u);
        u.Tick = 50_000;
        var steel = u.Stations.Values.First(s => s.TypeKey == "steelworks");
        // (b) pleite: Kasse dauerhaft unter der Grenze
        var broke = AddBuilt(u, steel, new Vec2(0, 70));
        broke.Credits = u.Rules.BankruptcyLimit - 100f;
        broke.BelowLimitSince = u.Tick - u.Rules.BankruptcyTicks - 1;
        new StationLifecycleSystem().Step(u, 20);
        Assert.Contains(ev.OfType<StationDeparted>(), d => d.Station == broke.Id && d.Reason == DepartureReason.Bankrupt);
        // (c) länger nicht rentabel, Produkte nicht knapp
        var idle = AddBuilt(u, steel, new Vec2(0, -70));
        foreach (var st in u.Stations.Values) if (st.Market.TryGetValue(DemoUniverse.Steel, out var e)) e.Amount = e.Target * 2f;
        MakeUnprofitable(u, idle.Profit);
        idle.BuiltTick = 0;
        u.LastStationRetireTick = -1_000_000;
        new StationLifecycleSystem().Step(u, 20);
        Assert.Contains(ev.OfType<StationDeparted>(), d => d.Station == idle.Id && d.Reason == DepartureReason.Unprofitable);
        // Baustelle ohne Fortschritt (Projekt aufgegeben) → Grund c
        var type = u.StationTypes["steelworks"];
        var site = ConstructionRules.StartSite(u, type, steel.Sector, steel.Position + new Vec2(70, 0), steel.Owner, 1000f, null, null, null)!;
        ConstructionRules.Expire(u, site);
        Assert.Contains(ev.OfType<StationDeparted>(), d => d.Station == site.Id && d.Reason == DepartureReason.Unprofitable && d.Detail == "site_expired");
        AssertEveryRemovalHasDecayReason(ev);
        // Hinweistext für den Spielersektor
        var toast = Departures.StationToast(ev.OfType<StationDeparted>().First(d => d.Station == broke.Id));
        Assert.Contains("pleite", toast);
    }

    [Fact]
    public void Spielerverkauf_IstKeinVerfallsweg()
    {
        var u = TestWorlds.Classic();
        var ev = Capture(u);
        var player = u.Ships[u.PlayerShip];
        var yard = u.Stations.Values.First(s => s.TypeKey == "shipyard");
        var extra = ShipPurchase.Create(u, u.NewShipId(), "pip", u.ShipClasses[u.ShipModels["pip"].ClassKey].Slots, u.ShipModels["pip"].Fit, ShipRole.Player, Controller.Player, u.PlayerActor, yard, 0f);
        u.AddShip(extra);
        Assert.True(Hangar.Sell(u, yard, extra, out var msg), msg);
        var d = Assert.Single(ev.OfType<ShipDeparted>());
        Assert.Equal(DepartureReason.Sold, d.Reason);
        Assert.Equal("sold", ev.OfType<ShipRemoved>().Single().Reason);
        Assert.True(u.Ships.ContainsKey(player.Id));
    }

    // ───────── Rentabilitätsmessung ─────────

    [Fact]
    public void Messung_GleitenderMittelwert_Hysterese_Uebertragungen()
    {
        var u = Standard();
        var m = new ProfitMeter();
        u.Tick = 1000;
        Profitability.Step(u, m, 500f, 50, false);   // Messbeginn
        Assert.Equal(1000, m.Since);
        Assert.Equal(0f, m.Rate);
        // dauerhaft −1 Cr/Tick: Mittelwert nähert sich −1, Verlustzähler wächst
        float worth = 500f;
        for (int i = 0; i < 400; i++) { worth -= 50f; u.Tick += 50; Profitability.Step(u, m, worth, 50, false); }
        Assert.InRange(m.Rate, -1f, -0.6f);   // 20000 Ticks bei Fenster 20000: 1 − e^−1 ≈ 0,63
        Assert.Equal(400 * 50, m.LossTicks);
        // leicht positiv, aber unter der Hysterese: Zähler ruht nicht zurück; erst über der Schwelle wird er null
        int before = m.LossTicks;
        for (int i = 0; i < 200 && m.Rate < 0f; i++) { worth += 50f * 3f; u.Tick += 50; Profitability.Step(u, m, worth, 50, false); }
        Assert.True(m.Rate >= 0f);
        Assert.True(m.LossTicks >= before);
        for (int i = 0; i < 400 && m.Rate <= u.Rules.ProfitableAbove; i++) { worth += 50f * 3f; u.Tick += 50; Profitability.Step(u, m, worth, 50, false); }
        Assert.Equal(0, m.LossTicks);
        // Übertragungen (Investition, Subvention) sind kein Geschäftsergebnis
        float rate = m.Rate;
        Profitability.Transfer(m, -5000f);
        worth -= 5000f;
        u.Tick += 50;
        Profitability.Step(u, m, worth, 50, false);
        Assert.True(MathF.Abs(m.Rate - rate) < 0.01f, $"Investition verändert die Rentabilität ({rate} → {m.Rate})");
    }

    [Fact]
    public void Messung_UhrRuhtHinterGeschlossenemTor_InAbgeschnittenemTeil()
    {
        var u = Standard();
        // Aurelia (8) hängt nur an Tor 17/18: zu → Aurelia abgeschnitten, Rest ist der Hauptteil
        GateAccess.SetState(u, u.Gates[new GateId(17)], GateState.Closed);
        int main = Profitability.MainPart(u);
        Assert.NotEqual(0, main);
        Assert.True(Profitability.Resting(u, new SectorId(8), main));
        Assert.False(Profitability.Resting(u, new SectorId(2), main));
        var m = new ProfitMeter();
        Profitability.Step(u, m, 1000f, 50, false);
        float worth = 1000f;
        for (int i = 0; i < 100; i++) { worth -= 100f; u.Tick += 50; Profitability.Step(u, m, worth, 50, resting: true); }
        Assert.Equal(0, m.LossTicks);
        Assert.Equal(0f, m.Rate);
        Assert.Equal(worth, m.Worth);   // Bezug läuft mit, kein Sprung beim Wiederöffnen
        // Schiff im abgeschnittenen Teil: das System misst, die Uhr ruht
        var ship = Trader(u);
        if (ship.Transit is not null) u.FinishTransit(ship);
        u.MoveShip(ship, new SectorId(8));
        ship.Profit.Since = 0; ship.Profit.Worth = Profitability.WorthOf(u, ship) + 5000f; ship.Profit.LossTicks = 7;
        new ProfitabilitySystem().Step(u, 50);
        Assert.Equal(7, ship.Profit.LossTicks);
        Assert.Equal(0f, ship.Profit.Rate);
    }

    [Fact]
    public void Messung_SpeichernLaden_BitIdentisch()
    {
        var u = Standard(5);
        new SimulationRunner().Run(u, 3000);
        Assert.Contains(u.Ships.Values, s => s.Profit.Since >= 0);
        Assert.Contains(u.Stations.Values, s => s.Profit.Since >= 0);
        u.Actors[new ActorId(1)].LastRestoreTick = 1234;
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var (v, _) = SaveGameJson.Read(json);
        Assert.Equal(SpaceSim.Core.Diagnostics.UniverseHash.Compute(u), SpaceSim.Core.Diagnostics.UniverseHash.Compute(v));
        new SimulationRunner().Run(u, 2000);
        new SimulationRunner().Run(v, 2000);
        Assert.Equal(SpaceSim.Core.Diagnostics.UniverseHash.Compute(u), SpaceSim.Core.Diagnostics.UniverseHash.Compute(v));
    }

    // ───────── Handlungsfähigkeit ─────────

    /// <summary>Freiwillige Ausgaben einer Fraktion halten die Betriebsreserve (Theorie über jede Ausgabe).</summary>
    [Theory]
    [InlineData("subvention")]
    [InlineData("instandhaltung")]
    [InlineData("polizeikauf")]
    [InlineData("bauschiff_baustelle")]
    [InlineData("flottenkauf")]
    [InlineData("investition")]
    [InlineData("sperre")]
    [InlineData("forschung")]
    public void Reserve_JedeFreiwilligeAusgabeHaeltSie(string fall)
    {
        var u = fall == "forschung" ? TestWorlds.Default(fuel: false, research: true) : Standard();
        new SimulationRunner().Run(u, 200);
        var f = new FactionId(1);
        var a = u.Actors[f];
        float reserve = FactionViability.Reserve(u, f);
        Assert.True(reserve > 0f);
        a.Treasury.Credits = reserve + 50f;   // knapp darüber: jede freiwillige Ausgabe darf höchstens 50 nehmen
        switch (fall)
        {
            case "subvention":
                foreach (var st in u.Stations.Values.Where(s => u.FactionOf(s) == f)) st.Credits = 0f;
                new SubsidySystem().Step(u, 50);
                break;
            case "instandhaltung":
                foreach (var st in u.Stations.Values) foreach (var e in st.Market.Values) e.Amount = MathF.Max(e.Amount, e.Target);
                new MaintenanceSystem().Step(u, 2400);
                break;
            case "polizeikauf":
                u.Rules.PoliceReserve = 0f;
                SecuritySystem.Commission(u, f);
                break;
            case "bauschiff_baustelle":
                u.Rules.SubsidyReserve = 0f;
                ConstructionRules.CommissionBuilder(u, f, u.Stations.Values.First(s => s.Owner == (ActorId)f).Sector, default);
                break;
            case "flottenkauf":
                u.Rules.SubsidyReserve = 0f;
                foreach (var s in u.Ships.Values.Where(s => s.IsEconomyAi && s.Role == ShipRole.Trader).ToList()) u.RemoveShip(s.Id, "test");
                new FleetSystem().Step(u, 20);
                break;
            case "investition":
                Assert.DoesNotContain(Investors.ForStation(u, 100f), i => i.Source == InvestorSource.Treasury && i.Holder == (ActorId)f);
                break;
            case "sperre":
                var sec = u.Sectors.Values.Where(s => s.Owner == f).OrderBy(s => s.Id.Value).First();
                u.Rules.LockMinTicks = 1_000_000;   // die Politik hebt die Hand-Sperre im Test nicht auf
                Relations.Set(u, f, new FactionId(2), -0.9f);
                Assert.True(FactionPolicySystem.SetLevel(u, sec, 2) > 0);
                u.Rules.LockUpkeepPerLevel = 100f;
                new FactionPolicySystem().Step(u, 20);
                Assert.False(sec.Access.IsOpen);
                Assert.False(sec.Access.Enforced);   // nicht aus der Reserve bezahlt: Sperre auf dem Papier
                break;
            case "forschung":
                foreach (var st in Research.StationsOf(u, f)) st.Credits = 0f;
                new ResearchSystem().Step(u, 1);
                break;
        }
        Assert.True(a.Treasury.Credits >= reserve - 0.01f, $"{fall}: Kasse {a.Treasury.Credits:0} unter Reserve {reserve:0}");
    }

    [Fact]
    public void Mindestbestand_ErsatzHatVorrang_DannNotkauf_MitBremse()
    {
        var u = Standard();
        var f = new FactionId(3);   // Montanbund: 2 Polizeischiffe, 1 Bauschiff
        var a = u.Actors[f];
        var ev = Capture(u);
        foreach (var p in SecuritySystem.PoliceOf(u, f)) Departures.RemoveShip(u, p, DepartureReason.Destroyed, "test");
        Assert.True(FactionViability.MinimumMissing(u, f));
        Assert.Contains(FactionViability.Check(u, f), i => i.Kind == ViabilityIssueKind.PoliceBelowMinimum);
        // Freiwilliges wartet: die Ersatzkosten bleiben in der Kasse
        Assert.True(FactionViability.ReplacementCost(u, f) > 0f);
        Assert.True(FactionViability.Spendable(u, a) <= MathF.Max(0f, a.Treasury.Credits - FactionViability.Reserve(u, f) - FactionViability.ReplacementCost(u, f)) + 0.01f);
        // Kasse unter Preis + Rücklage, aber über dem Preis: der Vorrangkauf greift trotzdem (auch in die Reserve)
        a.Treasury.Credits = 6000f;
        foreach (var yard in u.Stations.Values.Where(s => s.TypeKey == "shipyard")) foreach (var e in yard.Market.Values) e.Amount = MathF.Max(e.Amount, 5f);
        Assert.NotNull(SecuritySystem.Commission(u, f, priority: true));
        Assert.Null(SecuritySystem.Commission(u, f));   // ohne Vorrang hält er die Rücklage
        // keine Werft mit Material, Kasse leer: Notkauf an der Heimatbasis, Rest erlassen, Bremse
        foreach (var yard in u.Stations.Values.Where(s => s.TypeKey == "shipyard")) foreach (var e in yard.Market.Values) e.Amount = 0f;
        foreach (var p in SecuritySystem.PoliceOf(u, f)) Departures.RemoveShip(u, p, DepartureReason.Destroyed, "test");
        a.Treasury.Credits = 100f;
        a.LastRestoreTick = -1_000_000;
        var sys = new FactionViabilitySystem();
        sys.Step(u, 100);
        var restored = Assert.Single(ev.OfType<FactionRestored>());
        Assert.Equal(ShipRole.Police, restored.Role);
        Assert.Equal(100f, restored.Paid, 2);
        Assert.True(restored.Waived > 0f);
        Assert.Equal(u.Rules.PoliceMinPerFaction, SecuritySystem.PoliceOf(u, f).Count);
        Assert.DoesNotContain(FactionViability.Check(u, f), i => i.Kind == ViabilityIssueKind.PoliceBelowMinimum);
        // Bremse: ein zweiter Verlust wird erst nach der Abklingzeit per Notkauf ersetzt
        Departures.RemoveShip(u, SecuritySystem.PoliceOf(u, f)[0], DepartureReason.Destroyed, "test");
        sys.Step(u, 100);
        Assert.Single(ev.OfType<FactionRestored>());
        u.Tick += u.Rules.ViabilityRestoreCooldown;
        sys.Step(u, 100);
        Assert.Equal(2, ev.OfType<FactionRestored>().Count());
    }

    [Fact]
    public void Stilllegung_NieLetzteEigeneVersorgung_UndNieLetzterHerstellerImTeil()
    {
        var u = Standard();
        // Montanbund hat genau eine Farm (Aurelia), seine Station(en) verbrauchen Nahrung nicht — Wasserwerk Aurelia versorgt die eigene Farm
        var water = u.Stations.Values.First(s => s.TypeKey == "water_plant" && u.FactionOf(s) == new FactionId(3));
        Assert.Single(u.Stations.Values, s => s.TypeKey == "water_plant" && u.FactionOf(s) == new FactionId(3));
        Assert.False(FactionViability.KeepsOwnSupply(u, water));   // die eigene Farm braucht Wasser
        Assert.False(StationReview.MayRetire(u, water));
        // ein zweites Wasserwerk der Fraktion: dann darf eines gehen (falls nicht kritisch)
        var second = AddBuilt(u, water, new Vec2(0, 70));
        Assert.True(FactionViability.KeepsOwnSupply(u, second));
        // letzter Hersteller im abgeschnittenen Teil: kritisch
        GateAccess.SetState(u, u.Gates[new GateId(17)], GateState.Closed);
        u.RemoveStation(second.Id, "test");
        Assert.True(StationCriticality.IsCritical(u, water));
    }

    // ───────── Langläufe ─────────

    static void LongRun(Universe u, int ticks, string world)
    {
        var ev = Capture(u);
        var runner = new SimulationRunner();
        var bad = new List<string>();
        var factions = u.Actors.Values.Where(a => a.IsFaction(u) || a.Kind == ActorKind.Outlaw).Select(a => a.Id).ToList();
        for (int t = 0; t < ticks; t += 500)
        {
            runner.Run(u, 500);
            if (u.Tick < 2000) continue;   // Startkassen füllen sich erst über die Abgaben
            foreach (var f in factions)
                foreach (var i in FactionViability.Check(u, f))
                    if (i.Kind != ViabilityIssueKind.TreasuryBelowReserve) bad.Add($"{world} Tick {u.Tick}: {u.ActorName(f)} {i.Text}");
        }
        Assert.True(bad.Count == 0, string.Join("; ", bad.Take(5)));
        AssertEveryRemovalHasDecayReason(ev);
        // Mindestbestand gilt am Ende für jede KI-Fraktion (Runde 38: die Spielerfraktion hat keine Pflichten; Punkt 17: etablierte Banditen haben weder Polizei noch Bauschiffe)
        foreach (var f in u.NpcFactionIds().Where(f => !Bandits.IsEstablished(u, f)))
        {
            Assert.True(SecuritySystem.PoliceOf(u, f).Count >= u.Rules.PoliceMinPerFaction, $"{world}: Polizei von {u.Factions[f].Name}");
            Assert.True(FactionBuilderSystem.Of(u, f).Count >= u.Rules.MinBuildersPerFaction, $"{world}: Bauschiff von {u.Factions[f].Name}");
        }
        // die Kasse ist im Mittel über der Rücklage (Ausgaben halten sie; nur die Einnahmenseite kann sie unterschreiten; Banditen ohne Rücklage, Punkt 17)
        foreach (var f in u.NpcFactionIds().Where(f => !Bandits.IsEstablished(u, f)))
            Assert.True(u.Actors[f].Treasury.Credits >= FactionViability.Reserve(u, f) * 0.5f, $"{world}: Kasse {u.Factions[f].Name} {u.Actors[f].Treasury.Credits:0} / Rücklage {FactionViability.Reserve(u, f):0}");
    }

    [Fact]
    public void Langlauf_Standardwelt_HandlungsfaehigUndAbgaengeNurAusDreiGruenden() => LongRun(Standard(), 40_000, "Standardwelt");

    [Fact]
    public void Langlauf_Classic_HandlungsfaehigUndAbgaengeNurAusDreiGruenden() => LongRun(TestWorlds.Classic(), 40_000, "Classic");

    // ───────── Phase 20/21 auf der 11-Sektoren-Welt ─────────

    [Fact]
    public void Versorgungspanik_ElfSektoren_AureliaAbgeschnitten_MeldetUndEndetBeimOeffnen()
    {
        var u = Standard();
        var ev = Capture(u);
        GateAccess.SetState(u, u.Gates[new GateId(17)], GateState.Closed);
        var runner = new SimulationRunner();
        runner.Run(u, u.Rules.PanicDelayTicks + 200);
        var raised = ev.OfType<SupplyPanicRaised>().ToList();
        Assert.NotEmpty(raised);
        Assert.All(raised, r => Assert.Equal(new SectorId(8), r.Part));   // nur der abgeschnittene Teil (Aurelia) gerät in Panik
        Assert.NotEmpty(u.PanicSince);
        GateAccess.SetState(u, u.Gates[new GateId(17)], GateState.Open);
        runner.Run(u, 60);
        Assert.Empty(u.PanicSince);
    }

    [Fact]
    public void Ueberfaelle_ElfSektoren_VerlustquoteUnterEinemProzent_PiratenBleiben()
    {
        var u = Standard();
        float loot = 0f, revenue = 0f;
        int raids = 0;
        u.Events.Raised += e =>
        {
            if (e is RaidResolved rr) { raids++; loot += rr.LootValue; }
            else if (e is TradeCompleted tc) revenue += tc.Revenue;
        };
        new SimulationRunner().Run(u, 40_000);
        Assert.True(raids > 0);
        Assert.True(loot / revenue < 0.01f, $"Verlustquote {100 * loot / revenue:0.00} %");
        foreach (var a in u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw))
            Assert.Empty(FactionViability.Check(u, a.Id));   // Versteck steht, Neuanfang möglich
    }
}
