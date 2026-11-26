using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Welle D (Spieltests „Supermacht“ nach Welle C): Treibstoff der Spielerflotte (Schürfautomatik rechnet Treibstoff, Tankreserve, Versorger/„Versorgen“,
/// Kostenanzeige), Werftregale großer Module und des Schattens, Nachschub an eigenen Stationen.
/// </summary>
public class WelleDTests
{
    readonly Xunit.Abstractions.ITestOutputHelper _out;
    public WelleDTests(Xunit.Abstractions.ITestOutputHelper o) => _out = o;

    static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Referenzhash-Tests: alle Schalter der Welle D aus (alte Regeln); erste Zeile in <see cref="SchutzKonvoiTests.RevertSchutz"/>.</summary>
    internal static void RevertWelleD(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.FleetSupply = false;
        r.PlayerMinerFuelCost = false;
        r.PlayerTankReserve = false;
        r.PlayerWaitForSupplier = false;
        r.IdleShelfKeepLargeYard = false;
        r.IdleShelfKeepKeys = new List<string>();
        r.PlayerStationFunding = false;
        r.PlayerStationAmmoTarget = 0f;
        r.PlayerStationMissileTarget = 0f;
    }

    static Universe World(Action<EconomyRules>? tweak = null)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        tweak?.Invoke(def.Rules!);
        var u = WorldBuilder.Build(def);
        new SimulationRunner().Run(u, 1);
        u.PlayerWallet.Credits = 500_000f;
        return u;
    }

    static Station Yard(Universe u) => u.StationsById().First(s => s.TypeKey == "shipyard");

    static Ship Own(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        u.AddShip(s);
        s.RecomputeStats(u);
        return s;
    }

    static GoodId G(Universe u, string key) => u.FindGood(key)!.Id;

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    [Fact]
    public void Schalter_CodeAus_WeltAn_RevertAus()
    {
        var c = new EconomyRules();
        Assert.False(c.FleetSupply || c.PlayerMinerFuelCost || c.PlayerTankReserve || c.PlayerWaitForSupplier || c.IdleShelfKeepLargeYard || c.PlayerStationFunding);
        Assert.Empty(c.IdleShelfKeepKeys);
        Assert.Equal(0f, c.PlayerStationAmmoTarget);
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var r = def.Rules!;
        Assert.True(r.FleetSupply && r.PlayerMinerFuelCost && r.PlayerTankReserve && r.PlayerWaitForSupplier && r.IdleShelfKeepLargeYard && r.PlayerStationFunding);
        Assert.Contains("chem_engine_m", r.IdleShelfKeepKeys);
        Assert.True(r.PlayerStationAmmoTarget >= 60f);
        RevertWelleD(def);
        Assert.False(r.FleetSupply || r.PlayerStationFunding || r.IdleShelfKeepLargeYard);
    }

    /// <summary>Ohne Spielerreich und -flotte ändert Welle D in 3000 Ticks nichts (die Regalböden greifen erst nach 20 000 unbewegten Ticks).</summary>
    [Fact]
    public void SchalterAus_GleicherHash_3000Ticks()
    {
        ulong Run(bool on)
        {
            var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
            if (!on) RevertWelleD(def);
            var u = WorldBuilder.Build(def);
            new SimulationRunner().Run(u, 3000);
            return UniverseHash.Compute(u);
        }
        Assert.Equal(Run(false), Run(true));
    }

    // ───────── Punkt 2/3: Werftregale ─────────

    static void Idle(Universe u, Station yard, string key)
    {
        var e = yard.Market[G(u, key)];
        e.SeenAmount = e.Amount; e.LastMove = 0;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UngenutztesRegal_GrosseModuleUndSchattenteile_BleibenAmTypziel(bool on)
    {
        var u = World(r => { if (!on) { r.IdleShelfKeepLargeYard = false; r.IdleShelfKeepKeys = new(); } });
        var yard = Yard(u);
        foreach (var k in new[] { "weapon_laser_m", "reactor_mk2", "chem_engine_m", "heat_sink_m" }) Idle(u, yard, k);
        float before = yard.Market[G(u, "weapon_laser_m")].Target;
        Assert.Equal(2f, before);
        u.Tick = u.Rules.IdleShelfTicks + 50;
        new IdleShelfSystem().Step(u, IdleShelfSystem.Interval);
        Assert.Equal(on ? 2f : 1f, yard.Market[G(u, "weapon_laser_m")].Target);
        Assert.Equal(on ? 2f : 1f, yard.Market[G(u, "reactor_mk2")].Target);
        Assert.Equal(on ? 2f : 1f, yard.Market[G(u, "chem_engine_m")].Target);
        Assert.Equal(1f, yard.Market[G(u, "heat_sink_m")].Target);   // nicht in der Liste: alte Regel (Ziel 1 → bleibt 1)
    }

    /// <summary>Punkt 2: mit Boden nimmt die Werft vom Spieler die sechs Laserkanonen M eines Schlachtkreuzers an; ohne nur drei.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Schlachtkreuzer_UeberSpielerlieferung_Baubar(bool on)
    {
        var u = World(r => { if (!on) r.IdleShelfKeepLargeYard = false; });
        var yard = Yard(u);
        Idle(u, yard, "weapon_laser_m");
        u.Tick = u.Rules.IdleShelfTicks + 50;
        new IdleShelfSystem().Step(u, IdleShelfSystem.Interval);
        var ship = Own(u, "behemoth", yard);
        ship.CargoCapacity = 100000f;
        var laser = G(u, "weapon_laser_m");
        var entry = yard.Market[laser];
        entry.Amount = 0f;
        yard.Credits = 1_000_000f;
        ship.Cargo[laser] = 6f;
        var sold = TradeActions.Sell(u, ship, yard, laser, 6f);
        _out.WriteLine($"verkauft {sold.Revenue}, Ziel {entry.Target}");
        Assert.Equal(on ? 6f : 3f, yard.Market[laser].Amount, 3);
        // alle übrigen Teile des Schlachtkreuzers über Spielerlieferung auffüllen (bis zur Annahme)
        var q0 = ShipDealerService.Quote(u, yard, ship, "battlecruiser")!;
        foreach (var m in q0.Missing.Where(m => m.Good != laser))
        {
            ship.Cargo[m.Good] = ship.Cargo.GetValueOrDefault(m.Good) + m.Units;
            TradeActions.Sell(u, ship, yard, m.Good, m.Units);
        }
        var q = ShipDealerService.Quote(u, yard, ship, "battlecruiser")!;
        _out.WriteLine("fehlt: " + string.Join(", ", q.Missing.Select(m => $"{u.Goods[m.Good].Key} {m.Units}")));
        if (on) Assert.Empty(q.Missing); else Assert.Contains(q.Missing, m => m.Good == laser);
    }

    // ───────── Punkt 4: Nachschub an eigenen Stationen ─────────

    static Station OwnSectorHq(Universe u)
    {
        var st = u.StationsById().First(s => s.TypeKey == "sector_hq");
        st.Owner = u.PlayerActor;
        st.Credits = 0f;
        return st;
    }

    [Fact]
    public void EigeneStation_LeereKasse_SpielerkasseFuelltBisZumRegalbedarf()
    {
        var u = World();
        var hq = OwnSectorHq(u);
        var plates = G(u, "hull_plates");
        hq.Market[plates].Amount = 0f;
        float need = PlayerStationFunding.ShelfNeed(u, hq);
        Assert.True(need > 100f);
        var ev = Capture(u);
        float w0 = u.PlayerWallet.Credits;
        PlayerStationFunding.Step(u, PlayerStationFundingSystem.Interval);
        float moved = ev.OfType<StationCashMoved>().Where(x => x.Station == hq.Id).Sum(x => x.Amount);
        Assert.True(moved > 0f);
        Assert.Equal(w0 - u.PlayerWallet.Credits, moved, 1);
        Assert.True(hq.Credits <= need + 1f && hq.Credits <= u.Rules.PlayerStationFundPerDay * PlayerStationFundingSystem.Interval / u.Rules.DayTicks + 1f);
        // Schalter aus: nichts
        var u2 = World(r => r.PlayerStationFunding = false);
        var hq2 = OwnSectorHq(u2);
        PlayerStationFunding.Step(u2, 50);
        Assert.Equal(0f, hq2.Credits);
    }

    [Fact]
    public void EigeneStation_NieUnterDenBehoerdenvorrat()
    {
        var u = World();
        var hq = OwnSectorHq(u);
        hq.Market[G(u, "hull_plates")].Amount = 0f;
        u.PlayerWallet.Credits = PlayerUpkeepOrder.Reserve(u) + 10f;
        PlayerStationFunding.Step(u, 50);
        Assert.True(hq.Credits <= 10.01f);
    }

    /// <summary>Gemessene Ursache: NPC-Händler liefern an eine eigene Behörde mit leerer Kasse nichts; mit Auffüllen liefern sie Hüllenplatten.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EigeneBehoerde_HaendlerLiefernNurMitAuffuellen(bool on)
    {
        var u = World(r => r.PlayerStationFunding = on);
        var hqs = u.StationsById().Where(s => s.TypeKey == "sector_hq").ToList();
        foreach (var hq in hqs) { hq.Owner = u.PlayerActor; hq.Credits = 0f; hq.Market[G(u, "hull_plates")].Amount = 0f; }
        new SimulationRunner().Run(u, 6000);
        float plates = hqs.Sum(h => h.Market[G(u, "hull_plates")].Amount);
        _out.WriteLine($"Hüllenplatten an {hqs.Count} eigenen Sektor-HQ nach 6000 Ticks: {plates:0.0} (Auffüllen {on})");
        if (on) Assert.True(plates >= hqs.Count * 2f); else Assert.True(plates < hqs.Count * 1f);
    }

    [Fact]
    public void Spielerlieferung_AnEigeneStation_WirdBezahlt()
    {
        var u = World();
        var hq = OwnSectorHq(u);
        var plates = G(u, "hull_plates");
        hq.Market[plates].Amount = 0f;
        PlayerStationFunding.Step(u, 50);
        var ship = Own(u, "hauler", hq);
        ship.Cargo[plates] = 3f;
        // die Behörde hat keinen Handelsdienst im Typ — für eigene Stationen öffnet Welle D ihn (Lieferung über die Dock-Oberfläche)
        Assert.DoesNotContain("trade", u.StationTypes[hq.TypeKey].Services);
        var trade = StationServices.Default().ForStation(u, hq, ship).FirstOrDefault(s => s.Key == "trade");
        Assert.NotNull(trade);
        var offer = trade!.Offers(u, hq, ship).First(o => o.Key == $"sell:{plates.Value}");
        Assert.True(offer.Amount >= 1f, offer.Note);
        var res = trade.Execute(u, hq, ship, new ServiceRequest(offer.Key, plates, 3f));
        Assert.True(res.Success, res.Message);
        Assert.True(ship.Cargo.GetValueOrDefault(plates) < 3f);
        // fremde Behörde: kein Handelsdienst
        var foreign = u.StationsById().First(s => s.TypeKey == "sector_hq" && s.Id != hq.Id);
        Assert.DoesNotContain(StationServices.Default().ForStation(u, foreign, ship), s => s.Key == "trade");
    }

    [Fact]
    public void EigeneWerft_MunitionsUndRaketenregal_Angehoben()
    {
        var u = World();
        var yard = Yard(u);
        yard.Owner = u.PlayerActor;
        var ammo = SpaceSim.Core.Combat.Rearm.AmmoGood(u)!.Id;
        Assert.True(yard.Market[ammo].Target < 20f);
        PlayerStationFunding.Step(u, 50);
        Assert.Equal(u.Rules.PlayerStationAmmoTarget, yard.Market[ammo].Target);
        Assert.Equal(u.Rules.PlayerStationMissileTarget, yard.Market[SpaceSim.Core.Combat.Missiles.MissileGood(u)!.Id].Target);
        // fremde Werft unverändert
        var other = u.StationsById().Where(s => s.TypeKey == "shipyard").Skip(1).First();
        Assert.True(other.Market[ammo].Target < 20f);
    }

    // ───────── Punkt 1: Flottentreibstoff ─────────

    static GoodId Propellant(Universe u) => G(u, "propellant");

    [Fact]
    public void Versorgen_TankerFuelltTankMagazinUndRaketenAusDerFracht()
    {
        var u = World();
        var yard = Yard(u);
        var tanker = Own(u, "cistern", yard);
        var bastion = Own(u, "bastion", yard);
        var prop = Propellant(u);
        var fuel = Refuel.ActiveFuel(bastion)!.Value;
        bastion.Fuel.Clear();
        bastion.Systems.AmmoMissing = 30f;
        tanker.Cargo[fuel] = 200f;
        Assert.True(Commands.Give(u, tanker, new SupplyCommand(tanker.Sector), out var msg, ignoreCapacity: true), msg);
        var ev = Capture(u);
        var runner = new SimulationRunner();
        runner.Run(u, 60);
        Assert.True(bastion.FuelAmount(fuel) > 1f, $"Tank {bastion.FuelAmount(fuel)}");
        Assert.Contains(ev, e => e is FleetSupplied fs && fs.Target == bastion.Id && fs.Fuel > 0f);
        Assert.True(tanker.Cargo.GetValueOrDefault(fuel) < 200f);
        _out.WriteLine($"Bollwerk Tank {bastion.FuelAmount(fuel):0.#}/{bastion.Stats.FuelCapacity:0}; Tanker Fracht {tanker.Cargo.GetValueOrDefault(fuel):0.#}; prop {prop.Value} fuel {fuel.Value}");
    }

    [Fact]
    public void Versorgen_OhneFracht_HoltNachschubUndKehrtZurueck()
    {
        var u = World();
        var yard = Yard(u);
        var tanker = Own(u, "cistern", yard);
        var hauler = Own(u, "hauler", yard);
        var prop = Propellant(u);
        hauler.Fuel.Clear();
        Assert.Equal(prop, Refuel.ActiveFuel(hauler));
        Assert.True(Commands.Give(u, tanker, new SupplyCommand(tanker.Sector), out var msg, ignoreCapacity: true), msg);
        var tg = FleetSupply.Targets(u, tanker);
        var sh = FleetSupply.Shortest(u, tanker, tg);
        _out.WriteLine($"Ziele {tg.Count}, fehlt {sh?.Value}, Verkäufer {(sh is { } g ? FleetSupply.Seller(u, tanker, g)?.Name : "-")}, frei {tanker.FreeUnits(u, prop)}, Hauler Tankraum {hauler.FuelSpace}");
        var ev = Capture(u);
        new SimulationRunner().Run(u, 6000);
        Assert.Contains(ev, e => e is TradeCompleted || e is PlayerBought || e is FleetSupplied);
        Assert.True(hauler.FuelAmount(prop) > 1f, $"Tank {hauler.FuelAmount(prop)}; Tanker {tanker.Sector.Value} {OrderExecutors.Default.All.First(x => x.Kind == CommandKinds.Supply).Describe(u, tanker, tanker.Command!)}");
    }

    [Fact]
    public void Versorgen_SchalterAus_Abgelehnt()
    {
        var u = World(r => r.FleetSupply = false);
        var tanker = Own(u, "cistern", Yard(u));
        Assert.False(Commands.Give(u, tanker, new SupplyCommand(tanker.Sector), out var msg, ignoreCapacity: true));
        Assert.Contains("abgeschaltet", msg);
    }

    [Fact]
    public void Notkauf_WartetAufEigenenVersorger()
    {
        var u = World();
        var yard = Yard(u);
        var tanker = Own(u, "cistern", yard);
        var hauler = Own(u, "hauler", yard);
        var prop = Propellant(u);
        tanker.Command = new SupplyCommand(tanker.Sector);
        tanker.Cargo[prop] = 50f;
        Assert.True(FleetSupply.WaitFor(u, hauler, prop));
        Assert.False(FleetSupply.WaitFor(u, u.Ships[u.PlayerShip], prop));   // das aktive Schiff wartet nie
        tanker.Cargo.Clear();
        Assert.False(FleetSupply.WaitFor(u, hauler, prop));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tankreserve_EigeneSchiffeTankenMindestensBisZurReserve(bool on)
    {
        var u = World(r => r.PlayerTankReserve = on);
        var refinery = u.StationsById().First(s => s.TypeKey == "fuel_refinery");
        var hauler = Own(u, "hauler", refinery);
        var prop = Propellant(u);
        hauler.Fuel[prop] = 5f;
        u.PlayerWallet.Credits = 200f;   // kleine Kasse: 30 % reichen für kaum etwas
        refinery.Market[prop].Amount = 500f;
        Refuel.TankUp(u, hauler, refinery, 1f);
        float reserve = Refuel.MinFuelReserveFraction * hauler.Stats.FuelCapacity;
        _out.WriteLine($"Tank {hauler.FuelAmount(prop):0} (Reserve {reserve:0}), Kasse {u.PlayerWallet.Credits:0}");
        if (on) Assert.True(hauler.FuelAmount(prop) >= MathF.Min(reserve, 5f + MathF.Floor(200f / refinery.Market[prop].Price)) - 1f);
        else Assert.True(hauler.FuelAmount(prop) < 10f);
    }

    /// <summary>Die Schürfautomatik eigener Schiffe zieht Treibstoff ab: der Wert sinkt, die KI rechnet wie bisher.</summary>
    [Fact]
    public void Schuerfautomatik_RechnetTreibstoffFuerEigeneSchiffe()
    {
        var u = World();
        var yard = Yard(u);
        var miner = Own(u, "strip_miner", yard);
        miner.Role = ShipRole.Miner;
        var (o1, _, s1) = MinerSystem.BestMine(u, miner);
        u.Rules.PlayerMinerFuelCost = false;
        var (o0, _, s0) = MinerSystem.BestMine(u, miner);
        _out.WriteLine($"Bewertung mit Treibstoff {s1:0.00} ({o1?.Sector.Value}/{o1?.DeliverTo.Value}), ohne {s0:0.00} ({o0?.Sector.Value}/{o0?.DeliverTo.Value})");
        Assert.NotNull(o1);
        Assert.True(s1 < s0);
        // KI-Schiff: unverändert
        u.Rules.PlayerMinerFuelCost = true;
        miner.Owner = u.FreeActor;
        var (_, _, sNpc) = MinerSystem.BestMine(u, miner);
        Assert.Equal(s0, sNpc, 4);
    }

    [Fact]
    public void Anzeige_TreibstoffkostenJeStunde()
    {
        var u = World();
        var inc = new PlayerIncome(u);
        var yard = Yard(u);
        var hauler = Own(u, "hauler", yard);
        Assert.StartsWith("keine", inc.FuelText());
        u.Events.Publish(new FuelBought(hauler.Id, yard.Id, Propellant(u), 10f, 320f));
        u.Events.Publish(new EmergencyRefuel(hauler.Id, Propellant(u), 2f, 128f));
        u.Tick += u.Rules.DayTicks / 2;
        var (h, em, t) = inc.FuelPerHour();
        Assert.Equal(448f * 14400f / t, h, 1);
        Assert.Equal(128f * 14400f / t, em, 1);
        Assert.Contains("Cr/h", inc.FuelText());
        Assert.Contains("Notkauf", inc.FuelText());
    }

    [Fact]
    public void SpeichernLaden_MitVersorgungsbefehl_Bitgleich()
    {
        var u = World();
        var yard = Yard(u);
        var tanker = Own(u, "cistern", yard);
        var hauler = Own(u, "hauler", yard);
        tanker.Cargo[Propellant(u)] = 100f;
        hauler.Fuel.Clear();
        Assert.True(Commands.Give(u, tanker, new SupplyCommand(tanker.Sector), out _, ignoreCapacity: true));
        new SimulationRunner().Run(u, 30);
        var json = SaveGameJson.Write(u, Now);
        var v = SaveGameJson.Read(json).Universe;
        Assert.IsType<SupplyCommand>(v.Ships[tanker.Id].Command);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        new SimulationRunner().Run(u, 300);
        new SimulationRunner().Run(v, 300);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
    }
}
