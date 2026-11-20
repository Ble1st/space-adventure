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
/// Ausbau Punkt 5 — Schmuggler-KI (docs/SCHWARZMARKT.md „Schmuggler-KI“): Kauf durch gesetzlose Firmen (höchstens 3), Hehlerware zu ehrlichen Abnehmern,
/// Embargo-Ware per Sprung an der Sperre vorbei, Tarnung, Durchsuchung mit Sensorstärke (erwischt: Beschlagnahme, Strafe, Fahndung), Speichern/Laden,
/// Schalter aus = alter Hash.
/// </summary>
public class SchmugglerTests
{
    static readonly string[] Keys = { "passenger_cabin_s", "passenger_cabin_m" };

    /// <summary>
    /// Alles der Punkte 5/10/14 zurücknehmen (Referenz-Hash-Tests; Teil der Rücknahmekette über <see cref="BalanceFixTests.RevertBalanceFix"/> und
    /// <see cref="GasNebulaTests.RevertGas"/>): Schalter aus, Passagierkabinen und Regale, Blockadebrecher/Schatten, Mauersegler ohne Kabine.
    /// </summary>
    internal static void RevertSmuggling(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.Smugglers = false; r.MissionSmuggle = false; r.MissionResearch = false; r.MissionPassengers = false; r.PassengerTaxi = false;
        def.Modules.RemoveAll(m => Keys.Contains(m.Key));
        foreach (var t in def.StationTypes) if (t.Stocks is { } st) foreach (var k in Keys) st.Remove(k);
        def.ShipModels.RemoveAll(m => m.Key == "shadow");
        def.ShipClasses.RemoveAll(c => c.Key == "blockade_runner");
        if (def.ShipModels.FirstOrDefault(m => m.Key == "swift") is { } swift)
            for (int i = 0; i < swift.Fit.Count; i++) if (swift.Fit[i] == "passenger_cabin_s") swift.Fit[i] = null;
    }

    static Universe World(ulong seed = 1) => WorldBuilder.Build(TestWorlds.Def(fuel: false, research: false), seed);

    /// <summary>Eine gesetzlose Firma aus drei Schiffen in Alpha, jede mit <paramref name="credits"/>.</summary>
    static Actor MakeFirm(Universe u, float credits = 9000f)
    {
        var free = u.ShipsById().Where(s => s.IsEconomyAi && s.Owner == u.FreeActor && s.Transit is null && s.Role == ShipRole.Trader).Take(3).ToList();
        foreach (var s in free) s.Credits = credits;
        var id = ActorRules.Promote(u, free[0]);
        foreach (var s in free) s.Owner = id;
        var a = u.Actors[id];
        a.Personality = a.Personality with { Lawfulness = 0.1f };
        // Hehlerware in den Verstecken (sonst kauft keine Firma einen Schmuggler)
        foreach (var st in u.StationsById().Where(s => SpaceSim.Core.Trading.BlackMarket.IsAt(u, s))) { st.BlackStock[u.FindGood("food")!.Id] = 40f; st.BlackStock[u.FindGood("electronics")!.Id] = 40f; }
        return a;
    }

    /// <summary>Ein Schmuggler der Firma, angedockt an der Werft (Kauf erzwungen).</summary>
    static Ship BuySmuggler(Universe u, Actor firm)
    {
        var ship = SmugglerSystem.Purchase(u);
        Assert.NotNull(ship);
        Assert.Equal(firm.Id, ship!.Owner);
        return ship;
    }

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_Inhalt()
    {
        var c = new EconomyRules();
        Assert.False(c.Smugglers || c.MissionSmuggle || c.MissionResearch || c.MissionPassengers || c.PassengerTaxi);
        var u = World();
        Assert.True(u.Rules.Smugglers && u.Rules.MissionSmuggle && u.Rules.MissionResearch && u.Rules.MissionPassengers && u.Rules.PassengerTaxi);
        var shadow = u.ShipModels["shadow"];
        Assert.Equal("blockade_runner", shadow.ClassKey);
        var stats = ShipPurchase.Create(u, new ShipId(99999), "shadow", u.ShipClasses["blockade_runner"].Slots, shadow.Fit, ShipRole.Smuggler, Controller.Ai,
            u.FreeActor, u.StationsById().First(st => st.Dockable), 0f).Stats;
        Assert.True(stats.Cloak > 0f && stats.JumpRange > 0 && stats.Cargo >= 100f);
        Assert.True(stats.PowerOutput - stats.PowerDraw >= stats.CloakDraw && stats.PowerOutput - stats.PowerDraw >= stats.JumpDraw);
        Assert.Null(u.ShipModels["shadow"].Jobs is { Count: > 0 } ? "jobs" : null);   // keine Handelsaufgabe: zählt nicht in der Handelskapazität
    }

    [Fact]
    public void GesetzloseFirma_KauftHoechstensDreiSchmuggler_AusFreiemGeld()
    {
        var u = World();
        var firm = MakeFirm(u, 20000f);
        // Werft mit allem Material für mehrere Schmuggler
        var model = u.ShipModels["shadow"];
        var needs = ShipPurchase.Materials(u, model)!;
        foreach (var st in u.StationsById().Where(s => ShipPurchase.SellsModel(u, s, model)))
            foreach (var n in needs) st.Market[n.Good].Amount = 20f;
        float money0 = u.Ships.Values.Where(s => s.Owner == firm.Id).Sum(s => s.Credits);
        var bought = new List<SmugglerBought>();
        u.Events.Raised += e => { if (e is SmugglerBought b) bought.Add(b); };
        for (int i = 0; i < 5; i++) { SmugglerSystem.Purchase(u); u.Tick += u.Rules.SmugglerBuyCooldownTicks; }
        Assert.Equal(3, bought.Count);
        Assert.Equal(3, SmugglerSystem.CountOf(u, firm.Id));
        Assert.All(u.Ships.Values.Where(s => s.Role == ShipRole.Smuggler), s =>
        {
            Assert.Equal(firm.Id, s.Owner);
            Assert.True(s.Has(Capability.Stealth) && s.Has(Capability.Jump));
            Assert.Equal(u.Rules.SmugglerStartCredits, s.Credits, 1);
        });
        // nur aus freiem Geld (Schiffe behalten ihr Betriebsgeld)
        Assert.All(u.Ships.Values.Where(s => s.Owner == firm.Id && s.Role == ShipRole.Trader), s => Assert.True(s.Credits >= u.Rules.FirmShipKeep - 0.01f));
        // gesetzestreue Firma kauft nicht
        var u2 = World();
        var lawful = MakeFirm(u2, 20000f);
        lawful.Personality = lawful.Personality with { Lawfulness = 0.9f };
        Assert.Null(SmugglerSystem.Purchase(u2));
        _ = money0;
    }

    [Fact]
    public void Hehlerfahrt_KauftImVersteck_VerkauftAnEhrlichenAbnehmer_PiratenkasseBekommtDenEinkauf()
    {
        var u = World();
        var firm = MakeFirm(u, 20000f);
        foreach (var st in u.StationsById().Where(s => ShipPurchase.SellsModel(u, s, u.ShipModels["shadow"])))
            foreach (var n in ShipPurchase.Materials(u, u.ShipModels["shadow"])!) st.Market[n.Good].Amount = 20f;
        var ship = BuySmuggler(u, firm);
        new SimulationRunner().Run(u, 2);   // Preise stehen erst nach dem ersten Takt
        ship.Order = null; ship.Phase = TradePhase.Idle;
        ship.Credits = 5000f;
        var fence = u.StationsById().First(s => SpaceSim.Core.Trading.BlackMarket.IsAt(u, s));
        var food = u.FindGood("food")!.Id;
        foreach (var other in u.StationsById().Where(s => SpaceSim.Core.Trading.BlackMarket.IsAt(u, s))) other.BlackStock.Clear();
        fence.BlackStock[food] = 30f;
        var pirates = SpaceSim.Core.Trading.BlackMarket.Fence(u, fence)!;
        pirates.Treasury.Credits = 1000f;
        var ev = new List<SimEvent>();
        u.Events.Raised += e => { if (e is SmugglerRunStarted or SmugglerTraded or SmugglerCaught) ev.Add(e); };
        u.Rules.SecurityActive = false;   // ohne Polizei: die Fahrt ist das Thema
        var dbg = string.Join("; ", u.StationsById().Where(s => s.Market.TryGetValue(food, out var e) && !s.IsBuildSite).Select(s => $"{s.Name}:{s.Market[food].Price:0.0}/acc{s.AcceptableUnits(food, u.Rules):0}/hops{u.Hops(fence.Sector, s.Sector)}"));
        Assert.True(SmugglerSystem.Plan(u, ship), $"fence {fence.Name} hops {u.Hops(ship.Sector, fence.Sector)} price {SmugglerSystem.FenceBuyPrice(u, food)} :: {dbg}");
        var run = Assert.IsType<SmugglerRunStarted>(ev[0]);
        Assert.Equal(fence.Id, run.From);
        Assert.Equal("fence", run.Kind);
        new SimulationRunner().Run(u, 6000);
        var load = ev.OfType<SmugglerTraded>().FirstOrDefault(t => !t.Sold && t.Station == fence.Id);
        Assert.NotNull(load);
        Assert.Equal(load!.Units * SmugglerSystem.FenceBuyPrice(u, food), load.Credits, 1);
        Assert.Contains(ev.OfType<SmugglerTraded>(), t => t.Sold && t.Station == run.To && t.Credits > load.Credits);
    }

    [Fact]
    public void Durchsuchung_ErwischtUngetarnt_BeschlagnahmeStrafeFahndung_TarnfeldMk2SchuetztVorPolizei()
    {
        var u = World();
        var firm = MakeFirm(u, 20000f);
        foreach (var st in u.StationsById().Where(s => ShipPurchase.SellsModel(u, s, u.ShipModels["shadow"])))
            foreach (var n in ShipPurchase.Materials(u, u.ShipModels["shadow"])!) st.Market[n.Good].Amount = 20f;
        var ship = BuySmuggler(u, firm);
        var police = u.ShipsById().First(s => s.Role == ShipRole.Police && s.Transit is null && u.Sectors[s.Sector].Owner.Value != 0 && u.FactionOf(s) == u.Sectors[s.Sector].Owner);
        u.MoveShip(ship, police.Sector);
        ship.DockedAt = default;
        ship.Position = police.Position + new Vec2(30f, 0f);
        var food = u.FindGood("food")!.Id;
        ship.Cargo[food] = 20f;
        ship.Credits = 5000f;
        Assert.NotNull(SmuggleSearch.PoliceNear(u, ship.Sector, ship.Position));
        // Mk2-Tarnfeld schlägt den Polizeisensor (2,5 > 2): nie gefunden
        int mk2Slot = ship.Fit.IndexOf("cloak_s");
        ship.Fit[mk2Slot] = "cloak_s_mk2";
        ship.RecomputeStats(u);
        ship.Cloaked = true; ship.RecomputeStats(u);
        u.Rules.SmugglerSearchChance = 1f;
        Assert.Null(SmuggleSearch.Roll(u, ship));
        // ungetarnt: gefunden
        ship.Cloaked = false; ship.RecomputeStats(u);
        var f = SmuggleSearch.Roll(u, ship);
        Assert.NotNull(f);
        var factionActor = u.Actors[f!.Value];
        float before = factionActor.Treasury.Credits;
        var caught = new List<SmugglerCaught>();
        u.Events.Raised += e => { if (e is SmugglerCaught c) caught.Add(c); };
        SmugglerSystem.Caught(u, ship, f.Value);
        var c0 = Assert.Single(caught);
        Assert.Empty(ship.Cargo);
        Assert.Equal(20f * u.GoodOf(food).BasePrice * u.Rules.SmugglerFineShare, c0.Fine, 1);
        Assert.Equal(before + c0.Fine, factionActor.Treasury.Credits, 1);
        Assert.True(Wanted.LevelOf(u, firm.Id, f.Value) >= 1);
    }

    [Fact]
    public void Embargo_ZielNurPerSprung_SchmugglerSpringtAnDerSperreVorbei()
    {
        var u = World();
        var firm = MakeFirm(u, 20000f);
        foreach (var st in u.StationsById().Where(s => ShipPurchase.SellsModel(u, s, u.ShipModels["shadow"])))
            foreach (var n in ShipPurchase.Materials(u, u.ShipModels["shadow"])!) st.Market[n.Good].Amount = 20f;
        var ship = BuySmuggler(u, firm);
        ship.Credits = 8000f;
        u.Rules.SecurityActive = false;
        u.Rules.SmugglerLockBonus = 3f;   // die Sperre macht Ware dort teuer
        // Aurelia (8) ist eine Sackgasse hinter Tor 17: Tor zu → auf dem Torweg unerreichbar, der Sprungantrieb braucht das Tor nicht
        GateAccess.SetState(u, u.Gates[new GateId(17)], GateState.Closed);
        var aurelia = new SectorId(8);
        Assert.False(u.IsReachable(new SectorId(3), aurelia));
        var jumps = new List<ShipDriveJumped>();
        var runs = new List<SmugglerRunStarted>();
        u.Events.Raised += e => { if (e is ShipDriveJumped j && j.Ship == ship.Id) jumps.Add(j); if (e is SmugglerRunStarted r && r.Ship == ship.Id) runs.Add(r); };
        new SimulationRunner().Run(u, 8000);
        Assert.Contains(runs, r => r.Jump && u.Stations.TryGetValue(r.To, out var to) && to.Sector == aurelia);
        Assert.Contains(jumps, j => j.To == aurelia);
    }

    [Fact]
    public void ArmerSchmuggler_WirdVerkauftStattPleite_GeldAnDieFirma_KeinNeukaufWaehrendDerSperre()
    {
        var u = World();
        var firm = MakeFirm(u, 20000f);
        foreach (var st in u.StationsById().Where(s => ShipPurchase.SellsModel(u, s, u.ShipModels["shadow"])))
            foreach (var n in ShipPurchase.Materials(u, u.ShipModels["shadow"])!) st.Market[n.Good].Amount = 20f;
        var ship = BuySmuggler(u, firm);
        Assert.True(u.Rules.SmugglerRetireCredits > 0f);
        ship.Credits = 100f;
        ship.IdleSince = u.Tick;
        Assert.False(SmugglerSystem.ShouldRetire(u, ship));   // erst nach der Leerlaufzeit (die Firma kann aufstocken)
        u.Tick += Math.Min(u.Rules.SmugglerRetireIdleTicks, u.Rules.ShipBankruptTicks / 2);
        Assert.True(SmugglerSystem.ShouldRetire(u, ship));
        float firmMoney = u.Ships.Values.Where(s => s.Owner == firm.Id && s.Id != ship.Id).Sum(s => s.Credits) + firm.Treasury.Credits;
        var retired = new List<SmugglerRetired>(); var departed = new List<ShipDeparted>();
        u.Events.Raised += e => { if (e is SmugglerRetired r) retired.Add(r); if (e is ShipDeparted d) departed.Add(d); };
        SmugglerSystem.Retire(u, ship);
        var r0 = Assert.Single(retired);
        Assert.False(u.Ships.ContainsKey(ship.Id));
        Assert.Equal(DepartureReason.Unprofitable, Assert.Single(departed).Reason);   // Grund c, nicht pleite
        Assert.True(r0.Sale > 0f, "die Werft kauft das Schiff zurück");
        float after = u.Ships.Values.Where(s => s.Owner == firm.Id).Sum(s => s.Credits) + firm.Treasury.Credits;
        Assert.Equal(firmMoney + r0.Sale + r0.Cash, after, 1);
        Assert.Equal(Math.Max(1L, u.Tick), firm.LastSmugglerRetireTick);
        firm.LastSmugglerTick = 0;   // die Kauf-Abklingzeit ist hier kein Thema
        u.Tick += u.Rules.SmugglerRetireBlockTicks - 1;
        Assert.Null(SmugglerSystem.Purchase(u));   // Sperre nach dem Verkauf
        // mit Fracht oder unterwegs wird nicht verkauft
        var other = BuySmugglerAfter(u, firm);
        other.Credits = 100f; other.IdleSince = 0; other.Cargo[u.FindGood("food")!.Id] = 5f;
        Assert.False(SmugglerSystem.ShouldRetire(u, other));
    }

    static Ship BuySmugglerAfter(Universe u, Actor firm)
    {
        firm.LastSmugglerRetireTick = 0; firm.LastSmugglerTick = 0;
        return BuySmuggler(u, firm);
    }

    [Fact]
    public void SpeichernLaden_MitSchmugglerUnterwegs_Bitgleich()
    {
        var u = World(3);
        var firm = MakeFirm(u, 20000f);
        foreach (var st in u.StationsById().Where(s => ShipPurchase.SellsModel(u, s, u.ShipModels["shadow"])))
            foreach (var n in ShipPurchase.Materials(u, u.ShipModels["shadow"])!) st.Market[n.Good].Amount = 20f;
        BuySmuggler(u, firm);
        new SimulationRunner().Run(u, 1500);
        var (v, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        Assert.Equal(firm.LastSmugglerTick, v.Actors[firm.Id].LastSmugglerTick);
        new SimulationRunner().Run(u, 800);
        new SimulationRunner().Run(v, 800);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }

    static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SchalterAus_KeinSchmuggler_GleicherHashWieOhneInhalt()
    {
        var off = TestWorlds.Def(fuel: false, research: false);
        off.Rules!.Smugglers = false; off.Rules.MissionSmuggle = false; off.Rules.MissionResearch = false; off.Rules.MissionPassengers = false; off.Rules.PassengerTaxi = false;
        var u = WorldBuilder.Build(off, 1);
        new SimulationRunner().Run(u, 6000);
        Assert.DoesNotContain(u.Ships.Values, s => s.Role == ShipRole.Smuggler);
        var reverted = TestWorlds.Def(fuel: false, research: false);
        RevertSmuggling(reverted);
        var w = WorldBuilder.Build(reverted, 1);
        new SimulationRunner().Run(w, 6000);
        Assert.DoesNotContain(w.Ships.Values, s => s.Role == ShipRole.Smuggler);
    }

    [Fact]
    public void Standardwelt_20000Ticks_SchmugglerUnterwegs_WirtschaftImRahmen()
    {
        // die echte Standardwelt (Treibstoff/Forschung an). Ob eine gesetzlose Firma in 20 000 Ticks einen Schmuggler kauft, hängt am Weg
        // (Punkt 3: Standardsaat 0 Käufe, main 3; Saaten 1–4 1–3) — daher über zwei Saaten zusammengezählt
        var bought = 0; var runs = 0; var broke = 0;
        Universe u = null!;
        foreach (ulong seed in new ulong[] { 1, 2 })
        {
            u = WorldBuilder.BuildDefault(seed);
            u.Events.Raised += e => { if (e is SmugglerBought) bought++; if (e is SmugglerRunStarted) runs++; if (e is ShipDeparted { Role: ShipRole.Smuggler, Reason: DepartureReason.Bankrupt }) broke++; };
            new SimulationRunner().Run(u, 20000);
            foreach (var f in u.Actors.Values.Where(a => SmugglerSystem.LawlessFirm(u, a))) Assert.True(SmugglerSystem.CountOf(u, f.Id) <= u.Rules.SmugglersPerFirm);
            Assert.All(u.Ships.Values.Where(s => s.Role == ShipRole.Smuggler), s => Assert.True(s.UsedVolume(u) <= s.CargoCapacity + 0.01f));
        }
        Assert.True(bought >= 1, $"gekauft {bought}");
        Assert.True(runs >= 1, $"Fahrten {runs}");
        Assert.Equal(0, broke);   // arme Schmuggler werden verkauft, nicht pleite
        // keine Preise am Rand
        var stats = EconomyStats.Snapshot(u);
        Assert.NotNull(stats);
    }
}
