using System.Text.Json.Nodes;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 4c: Dienste „refuel“ und „repair“, Spielerverbrauch (<see cref="PlayerFuel"/>), Notruf
/// (<see cref="Refuel.PlayerEmergency"/>), sowie die Absicherung alter Welten ohne Raffinerie (Migration, Notkauf).
/// </summary>
public class RefuelRepairTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    static readonly GoodId Prop = DemoUniverse.Propellant;

    static (Universe u, Ship player, Station refinery) AtRefinery()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var st = u.Stations[new StationId(25)];
        if (player.Sector != st.Sector) u.MoveShip(player, st.Sector);
        player.Position = st.DockPoint(player.Id);
        u.WalletOf(player).Credits = 5_000f;
        return (u, player, st);
    }

    /// <summary>Frische Welt nach einem Tick: die Marktpreise sind gesetzt.</summary>
    static Universe World()
    {
        var u = TestWorlds.Default();
        new SimulationRunner().Run(u, 1);
        return u;
    }

    static Station Plating(Universe u) => u.Stations.Values.First(s => s.TypeKey == "plating_works");

    static List<ServicePerformed> Performed(Universe u)
    {
        var list = new List<ServicePerformed>();
        u.Events.Raised += e => { if (e is ServicePerformed p) list.Add(p); };
        return list;
    }

    // ───────── refuel ─────────

    [Fact]
    public void Refuel_Fill_BuchtBestandKasseCreditsTank_UndMeldetEreignis()
    {
        var (u, player, st) = AtRefinery();
        player.Fuel[Prop] = 50f;
        var events = Performed(u);
        var svc = new RefuelService();
        Assert.True(svc.IsAvailable(u, st));
        var e = st.Market[Prop];
        float price = e.Price, stock = e.Amount, stCr = st.Credits, shCr = u.WalletOf(player).Credits;
        float expect = MathF.Min(MathF.Floor(stock), 150f);

        var offers = svc.Offers(u, st, player);
        Assert.Equal(new[] { "refuel:fill", "refuel:25", "refuel:50", "refuel:100" }, offers.Select(o => o.Key));
        Assert.Equal(expect, offers[0].Amount);
        Assert.Equal(25f, offers[1].Amount);

        var r = svc.Execute(u, st, player, new ServiceRequest("refuel:fill", default, 0f));
        Assert.True(r.Success);
        Assert.Equal(expect, r.Units);
        Assert.Equal(50f + expect, player.FuelAmount(Prop));
        Assert.Equal(stock - expect, e.Amount, 3);
        Assert.Equal(stCr + expect * price, st.Credits, 2);
        Assert.Equal(shCr - expect * price, u.WalletOf(player).Credits, 2);
        Assert.Equal(-expect * price, r.Credits, 2);
        Assert.Empty(player.Cargo);   // Treibstoff landet im Tank, nicht im Laderaum
        var ev = Assert.Single(events);
        Assert.Equal("refuel", ev.Service);
    }

    [Fact]
    public void Refuel_FesteStufe_Und_Grenzen_VollPleiteLeerUnbekannt()
    {
        var (u, player, st) = AtRefinery();
        var svc = new RefuelService();
        var events = Performed(u);
        player.Fuel[Prop] = 100f;
        var r = svc.Execute(u, st, player, new ServiceRequest("refuel:25", default, 0f));
        Assert.True(r.Success);
        Assert.Equal(125f, player.FuelAmount(Prop));
        Assert.Single(events);

        // unbekannte/unsaubere Schlüssel: kein Zustandswechsel, kein Ereignis
        float fuel = player.TotalFuel, cr = u.WalletOf(player).Credits, stock = st.Market[Prop].Amount;
        foreach (var key in new[] { "refuel:30", "refuel:", "refuel", "fill", "refuel:fill:1", "repair:full", "refuel:-25" })
            Assert.False(svc.Execute(u, st, player, new ServiceRequest(key, default, 0f)).Success);
        // voll
        player.Fuel[Prop] = 200f;
        Assert.False(svc.Execute(u, st, player, new ServiceRequest("refuel:fill", default, 0f)).Success);
        player.Fuel[Prop] = fuel;
        // pleite
        u.WalletOf(player).Credits = 0f;
        Assert.False(svc.Execute(u, st, player, new ServiceRequest("refuel:fill", default, 0f)).Success);
        u.WalletOf(player).Credits = cr;
        // Lager leer: Dienst nicht verfügbar
        st.Market[Prop].Amount = 0f;
        float ion = 0f;
        if (u.FindGood("ion_gas") is { } ig && st.Market.TryGetValue(ig.Id, out var ie)) { ion = ie.Amount; ie.Amount = 0f; }   // Gasernte: Ionengas-Regal der Raffinerie leeren
        Assert.False(svc.IsAvailable(u, st));
        Assert.False(svc.Execute(u, st, player, new ServiceRequest("refuel:fill", default, 0f)).Success);
        st.Market[Prop].Amount = stock;
        if (u.FindGood("ion_gas") is { } ig2 && st.Market.TryGetValue(ig2.Id, out var ie2)) ie2.Amount = ion;

        Assert.Equal(fuel, player.TotalFuel);
        Assert.Equal(cr, u.WalletOf(player).Credits);
        Assert.Equal(stock, st.Market[Prop].Amount);
        Assert.Single(events);

        // knapper Bestand begrenzt die Menge
        st.Market[Prop].Amount = 10.7f;
        player.Fuel[Prop] = 0f;
        Assert.Equal(10f, svc.Execute(u, st, player, new ServiceRequest("refuel:100", default, 0f)).Units);
    }

    // ───────── repair ─────────

    [Fact]
    public void Repair_Voll_Halb_VerbrauchtPlatten_ZahltAufschlag()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var st = Plating(u);
        var svc = new RepairService();
        var events = Performed(u);
        var e = st.Market[DemoUniverse.HullPlates];
        e.Amount = 50f;
        Assert.True(svc.IsAvailable(u, st));
        u.WalletOf(player).Credits = 5_000f;

        // unbeschädigt: keine Angebote, kein Erfolg
        Assert.Empty(svc.Offers(u, st, player));
        Assert.False(svc.Execute(u, st, player, new ServiceRequest("repair:full", default, 0f)).Success);

        player.Hull = player.MaxHull - 40f;
        float price = e.Price, stCr = st.Credits;
        Assert.Equal(new[] { "repair:full", "repair:half" }, svc.Offers(u, st, player).Select(o => o.Key));
        var half = svc.Execute(u, st, player, new ServiceRequest("repair:half", default, 0f));
        Assert.True(half.Success);
        Assert.Equal(20f, half.Units, 3);
        Assert.Equal(player.MaxHull - 20f, player.Hull, 3);
        float plates = 20f * u.Rules.RepairPlatesPerHull;
        Assert.Equal(50f - plates, e.Amount, 3);
        float cost = plates * price * RepairService.LaborFactor;
        Assert.Equal(5_000f - cost, u.WalletOf(player).Credits, 2);
        Assert.Equal(stCr + cost, st.Credits, 2);

        Assert.True(svc.Execute(u, st, player, new ServiceRequest("repair:full", default, 0f)).Success);
        Assert.Equal(player.MaxHull, player.Hull, 3);
        Assert.Equal(2, events.Count);
        Assert.False(svc.Execute(u, st, player, new ServiceRequest("repair:all", default, 0f)).Success);
    }

    [Fact]
    public void Repair_Plattenmangel_Teilreparatur_OhnePlatten_NichtVerfuegbar()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        var st = Plating(u);
        var svc = new RepairService();
        u.WalletOf(player).Credits = 5_000f;
        player.Hull = player.MaxHull - 60f;
        st.Market[DemoUniverse.HullPlates].Amount = 1f;   // reicht für 1 / Regel Hüllenpunkte (Phase 27: Weltdatei 0,02 → 50; vorher 0,05 → 20)
        var r = svc.Execute(u, st, player, new ServiceRequest("repair:full", default, 0f));
        Assert.True(r.Success);
        Assert.Equal(1f / u.Rules.RepairPlatesPerHull, r.Units, 2);
        Assert.True(r.Units < 60f);
        Assert.Contains("reichten nicht", r.Message);
        Assert.Equal(0f, st.Market[DemoUniverse.HullPlates].Amount, 3);
        Assert.False(svc.IsAvailable(u, st));
        // pleite: nichts passiert
        st.Market[DemoUniverse.HullPlates].Amount = 20f;
        u.WalletOf(player).Credits = 0f;
        float hull = player.Hull;
        Assert.False(svc.Execute(u, st, player, new ServiceRequest("repair:full", default, 0f)).Success);
        Assert.Equal(hull, player.Hull);
        Assert.Equal(20f, st.Market[DemoUniverse.HullPlates].Amount);
    }

    [Fact]
    public void Dienste_NachStationstyp_RaffinerieTankt_PanzerwerkRepariert_FabrikNichts()
    {
        var u = World();
        var reg = StationServices.Default();
        Assert.Contains("refuel", reg.ForStation(u, u.Stations[new StationId(25)]).Select(s => s.Key));
        Assert.DoesNotContain("repair", reg.ForStation(u, u.Stations[new StationId(25)]).Select(s => s.Key));
        Assert.Contains("repair", reg.ForStation(u, Plating(u)).Select(s => s.Key));
        var solar = u.Stations.Values.First(s => s.TypeKey == "solar_plant");
        Assert.Equal(new[] { "trade", "hangar", "missions" }, reg.ForStation(u, solar).Select(s => s.Key));   // Phase 12: Hangar; Runde 41: missions
    }

    // ───────── Spielerverbrauch und Notruf ─────────

    [Fact]
    public void PlayerFuel_Verbrauch_NieNegativ_Notantrieb()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        Assert.Equal(200f, player.TotalFuel);
        Assert.Equal(1f, PlayerFuel.ThrottleCap(u, player));
        PlayerFuel.ConsumeFlown(u, player, 1000f);
        Assert.Equal(200f - 1000f * player.Stats.FuelUsePerUnit, player.TotalFuel, 3);
        PlayerFuel.ConsumeFlown(u, player, 1e9f);
        Assert.Equal(0f, player.TotalFuel);
        Assert.Equal(PlayerFuel.EmergencyThrottle, PlayerFuel.ThrottleCap(u, player));
        // Schiff ohne Modell: kein Treibstoffsystem, keine Deckelung
        var bare = new Ship { Id = u.NewShipId(), Sector = player.Sector, Role = ShipRole.Fighter };
        u.AddShip(bare);
        PlayerFuel.ConsumeFlown(u, bare, 100f);
        Assert.Equal(1f, PlayerFuel.ThrottleCap(u, bare));
    }

    [Fact]
    public void Notruf_NurBeiLeeremTank_ReichtBisTankstelle_Geldsenke_ZuWenigGeld()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        u.WalletOf(player).Credits = 5_000f;
        Assert.False(Refuel.PlayerEmergency(u, player).Success);   // Tank nicht leer

        player.Fuel[Prop] = 0f;
        player.Position = new Vec2(70f, 70f);   // frei im Raum, nicht an einem Andockpunkt
        float stationCredits = u.Stations.Values.Sum(s => s.Credits);
        var stop = Refuel.NearestFuelStation(u, player, Prop, float.PositiveInfinity)!;
        float dist = Travel.EstimateDistance(u, player.Sector, player.Position, stop.Sector, stop.DockPoint(player.Id), player);
        var r = Refuel.PlayerEmergency(u, player);
        Assert.True(r.Success);
        Assert.True(player.TotalFuel >= Refuel.Consumption(player, dist, Prop));
        Assert.Equal(r.Units * u.Goods[Prop].BasePrice * Refuel.EmergencyPriceFactor, 5_000f - u.WalletOf(player).Credits, 2);
        Assert.Equal(-(5_000f - u.WalletOf(player).Credits), r.Credits, 2);
        Assert.Equal(stationCredits, u.Stations.Values.Sum(s => s.Credits));   // Geldsenke: keine Station erhält den Erlös

        player.Fuel[Prop] = 0f;
        u.WalletOf(player).Credits = 1f;
        var broke = Refuel.PlayerEmergency(u, player);
        Assert.False(broke.Success);
        Assert.Equal(0f, player.TotalFuel);
        Assert.Equal(1f, u.WalletOf(player).Credits);
    }

    [Fact]
    public void SaveLoad_BehaeltTankstand_Huelle_Und_Reparaturregel()
    {
        var u = World();
        var player = u.Ships[u.PlayerShip];
        player.Fuel[Prop] = 37.5f;
        u.Rules.RepairPlatesPerHull = 0.08f;
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(37.5f, loaded.Ships[loaded.PlayerShip].FuelAmount(Prop));
        Assert.Equal(0.08f, loaded.Rules.RepairPlatesPerHull);
        Assert.Equal(new[] { "trade", "refuel", "hangar", "taxi" }, loaded.StationTypes["fuel_refinery"].Services);   // Phase 12
    }

    // ───────── alte Welten ohne Raffinerie ─────────

    /// <summary>v4-Save einer Welt, wie sie vor Phase 4 aussah: ohne Raffinerie-Stationen und ohne den Stationstyp.</summary>
    static string V4WithoutRefineries()
    {
        var node = JsonNode.Parse(FuelModelTests.ToV4Json(SaveGameJson.Write(TestWorlds.Classic(null, fuel: true, research: true), Now)))!.AsObject();   // Phase 23: klassische Welt (die eingefrorene Kette kennt keine Munition)
        var stations = node["Stations"]!.AsArray();
        var removed = new HashSet<int>();
        for (int i = stations.Count - 1; i >= 0; i--)
            if ((string?)stations[i]!["Type"] == "fuel_refinery") { removed.Add((int)stations[i]!["Id"]!); stations.RemoveAt(i); }
        Assert.Equal(2, removed.Count);
        foreach (var sec in node["Sectors"]!.AsArray())
        {
            var list = sec!["Stations"]!.AsArray();
            for (int i = list.Count - 1; i >= 0; i--) if (removed.Contains((int)list[i]!)) list.RemoveAt(i);
        }
        var types = node["StationTypes"]!.AsArray();
        for (int i = types.Count - 1; i >= 0; i--) if ((string?)types[i]!["Key"] == "fuel_refinery") types.RemoveAt(i);
        return node.ToJsonString();
    }

    [Fact]
    public void Migration_AlteWeltOhneRaffinerie_BekommtRaffinerien_FlotteBleibtAktiv()
    {
        var (u, _) = SaveGameJson.Read(V4WithoutRefineries());
        var refineries = u.Stations.Values.Where(s => s.TypeKey == "fuel_refinery").ToList();
        Assert.Equal(2, refineries.Count);
        Assert.True(u.StationTypes.ContainsKey("fuel_refinery"));
        Assert.All(refineries, st =>
        {
            Assert.True(st.Market[Prop].Amount > 0f);
            Assert.Contains(st.Id, u.Sectors[st.Sector].Stations);
            Assert.Equal(2, st.Production.Count);
            Assert.DoesNotContain(u.Sectors[st.Sector].Asteroids, a => Vec2.Distance(a.Position, st.Position) < a.Radius + Station.BodyRadius);
        });
        int fleet = u.Ships.Values.Count(s => s.IsEconomyAi);
        int tankUps = 0;
        u.Events.Raised += e => { if (e is FuelBought) tankUps++; };
        new SimulationRunner().Run(u, 12_000);
        Assert.True(tankUps > 0);
        Assert.True(u.Ships.Values.Count(s => s.IsEconomyAi) >= fleet);   // niemand wurde wegen leerem Tank aufgegeben
        var (again, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(again));
    }

    [Fact]
    public void WeltGanzOhneTreibstoffquelle_NotkaufHaeltSchiffeBeweglich()
    {
        var u = TestWorlds.Default();   // Treibstoff im Großmaßstab neu kalibriert (fuelUse halbiert, fünf Raffinerien): der Langlauf läuft wieder in der Standardwelt statt im Grundmaßstab
        foreach (var st in u.Stations.Values.Where(s => s.TypeKey == "fuel_refinery").ToList())
            u.RemoveStation(st.Id, "test");
        int emergencies = 0, trades = 0;
        u.Events.Raised += e => { if (e is EmergencyRefuel) emergencies++; else if (e is TradeCompleted) trades++; };
        foreach (var s in u.Ships.Values.Where(x => x.IsEconomyAi)) s.Fuel[Prop] = 0.5f;   // Tanks fast leer
        var runner = new SimulationRunner();
        runner.Run(u, 3_000);
        trades = 0;
        runner.Run(u, 3_000);
        Assert.True(emergencies > 0);
        Assert.True(trades > 0);   // der Handel läuft weiter, auch wenn die Starttanks längst unter der Reserve sind
        Assert.All(u.Ships.Values, s => Assert.True(s.TotalFuel >= 0f));
    }

    // ───────── Tankbudget und Liegenbleiber ─────────

    [Fact]
    public void KiTanken_GibtNurTankbudgetAus_BetriebskapitalBleibt()
    {
        var (u, _, st) = AtRefinery();
        var ship = u.Ships.Values.First(s => s.Role == ShipRole.Trader && !s.IsPlayer);
        if (ship.Transit is not null) u.FinishTransit(ship);
        ship.Fuel[Prop] = 20f;
        ship.Credits = 1_000f;
        st.Market[Prop].Amount = 500f;
        float price = st.Market[Prop].Price;
        float bought = Refuel.TankUp(u, ship, st);
        Assert.Equal(MathF.Floor(Refuel.FuelBudgetShare * 1_000f / price), bought);
        Assert.True(ship.Credits >= 1_000f * (1f - Refuel.FuelBudgetShare) - 0.01f);
        // Der Bedarf der anstehenden Reise geht vor dem Budget
        ship.Credits = 1_000f;
        ship.Fuel[Prop] = 0f;
        float need = bought + 5f;
        Assert.Equal(need, Refuel.TankUp(u, ship, st, need));
    }

    [Fact]
    public void Liegenbleiber_OhneGeldUndTreibstoff_WirdAufgegeben_StattEwigZuStehen()
    {
        var u = World();
        var ship = u.Ships.Values.First(s => s.Role == ShipRole.Trader && !s.IsPlayer);
        if (ship.Transit is not null) u.FinishTransit(ship);
        ship.Order = null; ship.Phase = TradePhase.Idle; ship.Cargo.Clear();
        ship.Position = new Vec2(140f, 140f);   // frei im Raum, an keiner Station
        ship.Fuel[Prop] = 0f;
        ship.Credits = 0f;
        var id = ship.Id;
        new SimulationRunner().Run(u, u.Rules.ShipBankruptTicks + 400);
        Assert.False(u.Ships.ContainsKey(id), u.Ships.TryGetValue(id, out var x) ? $"idle {x.IdleSince} tick {u.Tick} cr {x.Credits} fuel {x.TotalFuel} order {x.Order} transit {x.Transit != null} pos {x.Position.X},{x.Position.Y} sec {x.Sector.Value}" : "");
    }
}
