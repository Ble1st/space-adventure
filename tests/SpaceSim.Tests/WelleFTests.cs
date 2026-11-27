using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Welle F (drei Sonnet-Spieltests „alle elf Sektoren erobern und halten“): (1) das Reich trägt sich — Steueranteil, Instandhaltung der Spielerbehörden,
/// Rechnung je Sektor; (2) Flottentreibstoff hat Vorrang — Pflichtmaß auf Schuld, Notkauf ganz oder gar nicht, ungelöster Notfall wartet; (3) Krieg endet
/// „leere Kasse“ erst nach anhaltender Pleite und nicht während einer Belagerung durch die zahlungsfähige Seite.
/// </summary>
public class WelleFTests
{
    readonly Xunit.Abstractions.ITestOutputHelper _out;
    public WelleFTests(Xunit.Abstractions.ITestOutputHelper o) => _out = o;

    /// <summary>Referenzhash-Tests: alle Schalter der Welle F aus (alte Regeln); erste Zeile in <see cref="SchutzKonvoiTests.RevertSchutz"/>.</summary>
    internal static void RevertWelleF(WorldDef def)
    {
        var r = def.Rules ??= new EconomyRules();
        r.PlayerSectorTaxShare = 0.6f;
        r.PlayerGovernmentMaintenanceShare = 0f;
        r.PlayerFuelPriority = false;
        r.PlayerFuelDebtLimit = 0f;
        r.WarBrokeTicks = 0;
        r.WarBrokeMaxTicks = 0;
    }

    static Universe World(Action<EconomyRules>? tweak = null)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        tweak?.Invoke(def.Rules!);
        var u = WorldBuilder.Build(def);
        new SimulationRunner().Run(u, 1);
        return u;
    }

    static Ship Own(Universe u, string model, Station at, bool player = true)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, player ? ShipRole.Player : ShipRole.Trader,
            player ? Controller.Player : Controller.Ai, player ? u.PlayerActor : u.FreeActor, at, 0f);
        u.AddShip(s);
        s.RecomputeStats(u);
        return s;
    }

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    static Station Refinery(Universe u) => u.StationsById().First(s => s.TypeKey == "fuel_refinery");

    [Fact]
    public void Schalter_CodeAus_WeltAn_RevertAus()
    {
        var c = new EconomyRules();
        Assert.False(c.PlayerFuelPriority);
        Assert.Equal(0f, c.PlayerGovernmentMaintenanceShare);
        Assert.Equal(0, c.WarBrokeTicks);
        Assert.Equal(0f, c.PlayerFuelDebtLimit);
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var r = def.Rules!;
        Assert.True(r.PlayerFuelPriority && r.PlayerFuelDebtLimit > 0f && r.WarBrokeTicks > 0 && r.WarBrokeMaxTicks > r.WarBrokeTicks);
        Assert.InRange(r.PlayerGovernmentMaintenanceShare, 0.01f, 0.99f);
        Assert.Equal(0.75f, r.PlayerSectorTaxShare);
        RevertWelleF(def);
        Assert.False(r.PlayerFuelPriority);
        Assert.Equal(0, r.WarBrokeTicks);
        Assert.Equal(0.6f, r.PlayerSectorTaxShare);
    }

    /// <summary>Ohne Spielerreich, Spielerflotte und Krieg ändert Welle F nichts an der NPC-Welt.</summary>
    [Fact]
    public void SchalterAus_GleicherHash_3000Ticks()
    {
        ulong Run(bool on)
        {
            var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
            if (!on) RevertWelleF(def);
            var u = WorldBuilder.Build(def);
            new SimulationRunner().Run(u, 3000);
            return UniverseHash.Compute(u);
        }
        Assert.Equal(Run(false), Run(true));
    }

    // ───────── 2: Treibstoffvorrang ─────────

    /// <summary>
    /// Nachgestellt aus der Probe fueltrace (Test 8): Frachter mit kritischer Hülle an einer Reparaturstation ohne Material, kaum Treibstoff, Handelsbefehl.
    /// Alte Regel: Notfall an, Reparatur scheitert, Notfall aus, Händler kauft Notbenzin, fliegt los, wird angehalten — jeden Tick neu.
    /// Neue Regel: das Schiff wartet, keine Notkäufe, eine Meldung.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Notfall_Ungeloest_SchiffWartet_StattNotbenzinSchleife(bool on)
    {
        var u = World(r => { if (!on) { r.PlayerFuelPriority = false; r.PlayerFuelDebtLimit = 0f; } });
        var hq = u.StationsById().First(s => s.TypeKey == "sector_hq");
        foreach (var e in hq.Market.Values) e.Amount = 0f;   // kein Reparaturmaterial
        var ship = Own(u, "cistern", hq);
        var fuel = Refuel.ActiveFuel(ship)!.Value;
        ship.Fuel.Clear(); ship.Fuel[fuel] = 1f;
        ship.Hull = 0.2f * ship.MaxHull;
        u.PlayerWallet.Credits = 3000f;
        Assert.True(Commands.Give(u, ship, new TradeCommand(new AutoLimits(ship.Sector, 3, 1.5f, 0f)), out var msg, ignoreCapacity: true), msg);
        var ev = Capture(u);
        new SimulationRunner().Run(u, 300);
        int emergencyBuys = ev.OfType<EmergencyRefuel>().Count(e => e.Ship == ship.Id);
        int starts = ev.OfType<EmergencyStarted>().Count(e => e.Ship == ship.Id);
        _out.WriteLine($"{(on ? "neu" : "alt")}: Notkäufe {emergencyBuys}, Notfall-Starts {starts}, Kasse {u.PlayerWallet.Credits:0}, gestrandet {ev.OfType<FleetShipStranded>().Count(e => e.Ship == ship.Id)}");
        if (on)
        {
            Assert.Equal(0, emergencyBuys);
            Assert.Equal(1, starts);
            Assert.True(ship.Emergency);
            Assert.Contains(ev.OfType<FleetShipStranded>(), e => e.Ship == ship.Id);
            Assert.True(u.PlayerWallet.Credits > 2900f, "nur Liegegebühr/Unterhalt, kein Notbenzin");
        }
        else Assert.True(emergencyBuys >= 3 || starts >= 3, $"alte Regel: Schleife erwartet (Notkäufe {emergencyBuys}, Starts {starts})");
    }

    [Fact]
    public void Tanken_PflichtmassAufSchuld_NurSpieler()
    {
        var u = World();
        var refinery = Refinery(u);
        var mine = Own(u, "cistern", refinery);
        var npc = Own(u, "cistern", refinery, player: false);
        var fuel = Refuel.ActiveFuel(mine)!.Value;
        mine.Fuel.Clear(); npc.Fuel.Clear();
        u.PlayerWallet.Credits = 0f; npc.Credits = 0f;
        float min = FuelPriority.MinimumAt(u, mine, refinery, fuel);
        Assert.True(min > 1f, $"Pflichtmaß {min}");
        float got = Refuel.TankUp(u, mine, refinery);
        float price = refinery.Market[fuel].Price;
        float stock = MathF.Floor(refinery.Market[fuel].Amount + got);
        Assert.Equal(MathF.Min(MathF.Ceiling(min), MathF.Min(stock, MathF.Floor(u.Rules.PlayerFuelDebtLimit / price))), got, 0.01f);   // Pflichtmaß, begrenzt durch Lager und Schuldgrenze
        Assert.True(u.PlayerWallet.Credits < 0f && u.PlayerWallet.Credits >= -u.Rules.PlayerFuelDebtLimit);
        Assert.Equal(0f, Refuel.TankUp(u, npc, refinery));   // KI-Schiff: kein Geld, kein Treibstoff (unverändert)
        // Schuldgrenze erreicht: nichts mehr auf Schuld
        u.PlayerWallet.Credits = -u.Rules.PlayerFuelDebtLimit;
        mine.Fuel.Clear();
        Assert.Equal(0f, Refuel.TankUp(u, mine, refinery));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Notkauf_GanzOderGarNicht(bool on)
    {
        var u = World(r => { if (!on) r.PlayerFuelPriority = false; });
        var ship = Own(u, "cistern", Refinery(u));
        var fuel = Refuel.ActiveFuel(ship)!.Value;
        ship.Fuel.Clear();
        float price = u.Goods[fuel].BasePrice * Refuel.EmergencyPriceFactor;
        u.PlayerWallet.Credits = price * 1.5f;   // reicht für 1 Einheit
        float limit = u.Rules.PlayerFuelDebtLimit;
        float got = Refuel.EmergencyBuy(u, ship, fuel, 10f);
        if (!on) { Assert.Equal(1f, got); return; }
        Assert.Equal(10f, got);   // mit Schuldgrenze ganz
        Assert.True(u.PlayerWallet.Credits < 0f);
        u.PlayerWallet.Credits = price * 1.5f;
        u.Rules.PlayerFuelDebtLimit = 0f;
        ship.Fuel.Clear();
        Assert.Equal(0f, Refuel.EmergencyBuy(u, ship, fuel, 10f));   // ohne Schuldraum: gar nicht statt 1 Einheit
        Assert.Equal(price * 1.5f, u.PlayerWallet.Credits, 0.01f);
        u.Rules.PlayerFuelDebtLimit = limit;
    }

    [Fact]
    public void Gestrandet_Meldungstext()
    {
        var u = World();
        var e = new FleetShipStranded(new ShipId(1), "fuel", 12f, 768f);
        Assert.Contains("768", FuelPriority.Text(u, e));
        Assert.Contains("wartet", FuelPriority.Text(u, new FleetShipStranded(new ShipId(1), "Hülle kritisch", 0f, 0f)));
    }

    // ───────── 3: Krieg endet „broke“ erst nach anhaltender Pleite ─────────

    static (Universe u, War w, Actor ai) WarWithBrokeAi(bool on)
    {
        var u = World(r => { if (!on) { r.WarBrokeTicks = 0; r.WarBrokeMaxTicks = 0; } });
        u.Tick = 20_000;   // Rücklage aufgebaut (FactionReserveBuildTicks)
        var me = u.PlayerFaction!.Value;
        var f = u.NpcFactionIds().First(x => !Bandits.IsEstablished(u, x));
        var w = Wars.Declare(u, me, f);
        var ai = u.Actors[new ActorId(f.Value)];
        u.PlayerWallet.Credits = 100_000f;
        ai.Treasury.Credits = FactionViability.Reserve(u, f) - 50f;
        return (u, w, ai);
    }

    static void StepWars(Universe u, int ticks)
    {
        var sys = new WarSystem();
        for (int t = 0; t < ticks; t += WarSystem.Interval) { u.Tick += WarSystem.Interval; sys.Step(u, WarSystem.Interval); }
    }

    [Fact]
    public void Krieg_AlteRegel_SofortFrieden()
    {
        var (u, w, _) = WarWithBrokeAi(on: false);
        StepWars(u, 100);
        Assert.DoesNotContain(w, u.Wars);
    }

    [Fact]
    public void Krieg_KurzePleite_KeinFrieden_AnhaltendePleite_Frieden()
    {
        var (u, w, ai) = WarWithBrokeAi(on: true);
        var ev = Capture(u);
        StepWars(u, u.Rules.WarBrokeTicks - 200);
        Assert.Contains(w, u.Wars);
        Assert.NotEqual(0, w.BrokeSinceB);
        Assert.Single(ev.OfType<WarBrokeNoticed>());
        // kurz über die Rücklage: Uhr beginnt von vorn
        ai.Treasury.Credits = FactionViability.Reserve(u, w.B) + 10f;
        StepWars(u, 100);
        Assert.Equal(0, w.BrokeSinceB);
        ai.Treasury.Credits = FactionViability.Reserve(u, w.B) - 50f;
        StepWars(u, u.Rules.WarBrokeTicks - 200);
        Assert.Contains(w, u.Wars);
        StepWars(u, 400);
        Assert.DoesNotContain(w, u.Wars);
        Assert.Contains(ev.OfType<PeaceConcluded>(), p => p.Reason == "broke");
    }

    [Fact]
    public void Krieg_SpielerPleite_EndetAuchNichtSofort()
    {
        var (u, w, ai) = WarWithBrokeAi(on: true);
        ai.Treasury.Credits = 1_000_000f;
        u.PlayerWallet.Credits = -10f;
        StepWars(u, 500);
        Assert.Contains(w, u.Wars);
        Assert.NotEqual(0, w.BrokeSinceA);
        Assert.Contains("leere Kasse", WarBroke.Text(u, w, w.A));
        StepWars(u, u.Rules.WarBrokeTicks);
        Assert.DoesNotContain(w, u.Wars);
    }

    [Fact]
    public void Krieg_BelagerungDerZahlungsfaehigen_HaeltBisObergrenze()
    {
        var (u, w, _) = WarWithBrokeAi(on: true);
        var target = u.StationsById().First(s => !s.IsBuildSite && u.FactionOf(s) == w.B);
        var me = u.Ships[u.PlayerShip];
        var enc = new Encounter { Id = 999, Sector = target.Sector, Cause = EncounterCause.Siege, Station = target.Id, StartTick = u.Tick };
        enc.Participants.Add(new CombatParticipant { Ship = me.Id, Side = 0, Owner = u.PlayerActor });
        u.Fights.Encounters.Add(enc);
        Assert.True(WarBroke.SiegeAgainst(u, w.B, w.A));
        Assert.False(WarBroke.SiegeAgainst(u, w.A, w.B));
        StepWars(u, u.Rules.WarBrokeTicks + 500);
        Assert.Contains(w, u.Wars);   // Belagerung läuft: kein Frieden „leere Kasse“
        StepWars(u, u.Rules.WarBrokeMaxTicks - u.Rules.WarBrokeTicks);
        Assert.DoesNotContain(w, u.Wars);   // Obergrenze: Handlungsfähigkeit geht vor
    }

    [Fact]
    public void Krieg_PleiteZustand_SpeichernLaden_Bitgleich()
    {
        var (u, w, _) = WarWithBrokeAi(on: true);
        StepWars(u, 300);
        Assert.NotEqual(0, w.BrokeSinceB);
        var json = SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
        var v = SaveGameJson.Read(json).Universe;
        Assert.Equal(w.BrokeSinceB, v.Wars.Single().BrokeSinceB);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(v));
        long before = UniverseHash.Compute(u) is var h ? (long)h : 0;
        w.BrokeSinceB = 0;
        Assert.NotEqual((ulong)before, UniverseHash.Compute(u));
    }

    // ───────── 1: das Reich trägt sich ─────────

    [Fact]
    public void Instandhaltung_Spielerbehoerden_Anteil_KiUnveraendert()
    {
        var u = World();
        var hq = u.StationsById().First(s => s.TypeKey == "sector_hq");
        var t = u.StationTypes[hq.TypeKey];
        Assert.Equal(1f, Maintenance.StationFactor(u, hq, t));
        float full = Maintenance.NeedPerDay(u, t).Sum(x => x.PerDay);
        hq.Owner = u.PlayerActor;
        Assert.Equal(u.Rules.PlayerGovernmentMaintenanceShare, Maintenance.StationFactor(u, hq, t));
        Assert.Equal(full * u.Rules.PlayerGovernmentMaintenanceShare, Maintenance.NeedFor(u, hq, t).Sum(x => x.PerDay), 0.001f);
        u.Rules.PlayerGovernmentMaintenanceShare = 0f;   // alte Regel
        Assert.Equal(full, Maintenance.NeedFor(u, hq, t).Sum(x => x.PerDay), 0.001f);
    }

    [Fact]
    public void Reichsrechnung_JeSektor_SteuerSkaliertMitAnteil_KeineGeldquelle()
    {
        var u = World();
        Assert.Empty(EmpireBalance.All(u));   // ohne eigenen Sektor keine Zeile
        var me = u.PlayerFaction!.Value;
        var sec = u.Sectors.Values.First(s => s.Name.Contains("Beta"));
        sec.Owner = me;
        var station = u.Stations[sec.Stations.First()];
        station.Owner = default;   // fraktionslos: zahlt die Abgabe an den Spieler
        station.Credits = WorkingCapital.Of(u, station) + 100_000f;
        var b = EmpireBalance.Of(u, sec);
        Assert.True(b.Tax > 0f, "Abgabe fremder Zahler");
        u.Rules.PlayerSectorTaxShare = 0.6f;
        var old = EmpireBalance.Of(u, sec);
        Assert.Equal(b.Tax * 0.6f / 0.75f, old.Tax, b.Tax * 0.001f);
        Assert.Contains("Steuer", EmpireBalance.Line(b));
        // eigene Leerlaufstation: kein Ertrag (Kasse unter dem Betriebskapital), nur Kosten
        var own = u.Stations[sec.Stations.First(id => u.StationTypes[u.Stations[id].TypeKey].Government == GovernmentKind.None && id != station.Id)];
        own.Owner = u.PlayerActor;
        own.Credits = 0f;
        var t = u.StationTypes[own.TypeKey];
        Assert.Equal(0f, PlayerStationSales.IncomePerTick(u, own, t));
        own.Credits = WorkingCapital.Of(u, own) + 10_000f;
        Assert.Equal(10_000f * u.Rules.StationWealthTax, PlayerStationSales.IncomePerTick(u, own, t), 0.01f);
        Assert.True(EmpireBalance.Of(u, sec).Maintenance > 0f);
    }
}
