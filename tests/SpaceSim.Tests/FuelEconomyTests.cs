using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 4b: Treibstoff-Wirtschaft — echter Verbrauch in <see cref="Travel.Start"/> (nur Strecke innerhalb der Sektoren,
/// Gate-Sprünge frei, Credit-Pauschale entfällt), Tanken über <see cref="Refuel"/> (aktuelle Station, sonst Tankstopp als
/// <see cref="GoToOrder"/>, sonst Notkauf zum Aufpreis als Geldsenke), Treibstoffkosten in der Händler-Bewertung,
/// Treibstoffraffinerie als Quelle. Plus Determinismus und Langlauf-Invarianten.
/// </summary>
public class FuelEconomyTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    static Ship ParkedTrader(Universe u)
    {
        var s = u.Ships.Values.First(x => x.Role == ShipRole.Trader && !x.IsPlayer);
        if (s.Transit is not null) u.FinishTransit(s);   // Test braucht ein geparktes Schiff
        return s;
    }

    static Station Refinery(Universe u, int id = 25) => u.Stations[new StationId(id)];

    [Fact]
    public void TravelStart_VerbrauchtTreibstoff_NurTeilstreckenInSektoren_GateFrei_CreditsUnberuehrt()
    {
        var u = TestWorlds.Default();
        var ship = u.Ships.Values.First(s => s.Role == ShipRole.Trader && s.Sector == new SectorId(1) && s.Transit is null);
        Assert.Equal(0.002f, ship.Stats.FuelUsePerUnit);   // fuelUse des chem_engine_s in der Startwelt
        var fromSector = ship.Sector;
        var fromPos = ship.Position;
        var st = u.Stations[u.Sectors[new SectorId(3)].Stations[0]];   // Sektor Gamma: 2 Gate-Sprünge ab Alpha
        float fuelBefore = ship.TotalFuel, creditsBefore = ship.Credits;

        Travel.Start(u, ship, st.Sector, st.DockPoint(ship.Id));
        Assert.NotNull(ship.Transit);
        // Gate-Sprünge sind frei: Verbrauch = Summe der Legs (jede Leg liegt innerhalb eines Sektors) × fuelUse / efficiency
        Assert.Equal(new[] { new SectorId(1), new SectorId(2), new SectorId(3) },
                     ship.Transit.Legs.Select(l => l.Sector).Distinct().OrderBy(x => x.Value).ToArray());
        float distance = ship.Transit.Legs.Sum(l => Vec2.Distance(l.From, l.To));
        float expected = distance * ship.Stats.FuelUsePerUnit;   // efficiency propellant = 1
        Assert.Equal(fuelBefore - expected, ship.TotalFuel, 3);
        Assert.Equal(creditsBefore, ship.Credits, 3);            // Credit-Pauschale entfällt (Phase 4b)
        // Helfer konsistent: PlannedDistance = Leg-Summe, EstimateDistance/EstimateFuel = Luftlinie (≤ Umweg)
        Assert.Equal(distance, Travel.PlannedDistance(u, fromSector, fromPos, st.Sector, st.DockPoint(ship.Id), ship), 3);
        float estDist = Travel.EstimateDistance(u, fromSector, fromPos, st.Sector, st.DockPoint(ship.Id), ship);
        Assert.True(estDist <= distance + 0.001f);
        Assert.Equal(estDist * ship.Stats.FuelUsePerUnit,
                     Travel.EstimateFuel(u, ship, fromSector, fromPos, st.Sector, st.DockPoint(ship.Id)), 3);

        // Fahrt innerhalb eines Sektors: Verbrauch über die geplanten Umweg-Legs
        var ship2 = u.Ships.Values.First(s => s.Role == ShipRole.Trader && s.Sector == new SectorId(1) && s.Transit is null && s.Id != ship.Id);
        float f2 = ship2.TotalFuel;
        Travel.Start(u, ship2, new SectorId(1), new Vec2(0, 0));
        float d2 = ship2.Transit!.Legs.Sum(l => Vec2.Distance(l.From, l.To));
        Assert.Equal(f2 - d2 * ship2.Stats.FuelUsePerUnit, ship2.TotalFuel, 3);

        // Schiff ohne Modell/Antrieb verbraucht nichts und zahlt keine Pauschale
        var dummy = new Ship { Id = u.NewShipId(), Role = ShipRole.Fighter, Sector = new SectorId(1), Position = new Vec2(0, 0), Credits = 100f };
        u.AddShip(dummy);
        Travel.Start(u, dummy, new SectorId(2), new Vec2(0, 0));
        Assert.Equal(0f, dummy.TotalFuel);
        Assert.Equal(100f, dummy.Credits, 3);
    }

    [Fact]
    public void PrepareTrip_LeererTank_AnRaffinerie_TanktAuf_CreditsBestandKasseGebucht()
    {
        var u = TestWorlds.Default();
        var ship = ParkedTrader(u);
        var refi = Refinery(u);
        var prop = DemoUniverse.Propellant;
        // Schiff angedockt an der Raffinerie, Tank leer, genug Credits
        ship.Sector = refi.Sector;
        ship.Position = refi.DockPoint(ship.Id);
        ship.Fuel.Clear();
        ship.Credits = 100_000f;
        refi.Market[prop].Amount = 500f;
        refi.Market[prop].Price = 40f;
        float stockBefore = refi.Market[prop].Amount, stCredits = refi.Credits;
        int events = 0;
        float boughtUnits = 0f, boughtCost = 0f;
        u.Events.Raised += e => { if (e is FuelBought fb) { events++; boughtUnits = fb.Units; boughtCost = fb.Cost; } };

        var target = u.Stations[u.Sectors[new SectorId(3)].Stations[0]];
        var plan = Refuel.PrepareTrip(u, ship, target.Sector, target.DockPoint(ship.Id));

        Assert.Equal(Refuel.TripPlan.Depart, plan);          // an der Tankstelle volltanken, dann direkt weiter
        Assert.Equal(1, events);
        Assert.Equal(200f, boughtUnits, 3);                 // bis voll (Tankkapazität 200)
        Assert.Equal(200f, ship.FuelAmount(prop), 3);
        Assert.Equal(boughtUnits * 40f, boughtCost, 3);
        Assert.Equal(stockBefore - boughtUnits, refi.Market[prop].Amount, 3);
        Assert.Equal(stCredits + boughtCost, refi.Credits, 3);
        Assert.Equal(100_000f - boughtCost, ship.Credits, 3);

        // danach Fahrt möglich, Verbrauch wird aus dem Tank gebucht
        float fuelBefore = ship.TotalFuel;
        Travel.Start(u, ship, target.Sector, target.DockPoint(ship.Id));
        float dist = ship.Transit!.Legs.Sum(l => Vec2.Distance(l.From, l.To));
        Assert.Equal(fuelBefore - dist * ship.Stats.FuelUsePerUnit, ship.TotalFuel, 3);
    }

    [Fact]
    public void PrepareTrip_StationOhneTreibstoff_FahrtZurTankstelle_TankenBeiAnkunft()
    {
        var u = TestWorlds.Default();
        new MarketSystem().Step(u, 1);   // Preise setzen (im laufenden Betrieb macht das MarketSystem vor den KI-Systemen)
        var ship = ParkedTrader(u);
        var steel = u.Stations[new StationId(1)];   // Stahlwerk Alpha führt keinen Treibstoff
        Assert.False(steel.Market.ContainsKey(DemoUniverse.Propellant));
        ship.Sector = steel.Sector;
        ship.Position = steel.DockPoint(ship.Id);
        ship.Fuel[DemoUniverse.Propellant] = 1f;   // Resttank: für die Reserve zu wenig
        ship.Credits = 5000f;

        var target = u.Stations[u.Sectors[new SectorId(4)].Stations[0]];
        var plan = Refuel.PrepareTrip(u, ship, target.Sector, target.DockPoint(ship.Id));

        Assert.Equal(Refuel.TripPlan.FuelStop, plan);        // erst zur nächsten Tankstelle
        var go = Assert.IsType<GoToOrder>(ship.Order);
        Assert.Equal("fuel_refinery", u.Stations[go.Target].TypeKey);
        Assert.NotNull(ship.Transit);

        // bis zur Ankunft simulieren: dort wird getankt (FuelBought an der Raffinerie), Auftrag ist erledigt
        long arrival = ship.Transit.ArrivalTick;
        int tankEvents = 0;
        u.Events.Raised += e => { if (e is FuelBought) tankEvents++; };
        new SimulationRunner().Run(u, (int)(arrival - u.Tick) + 1);
        Assert.Null(ship.Transit);
        Assert.Null(ship.Order);
        Assert.True(tankEvents >= 1, "bei Ankunft an der Tankstelle wurde getankt");
        Assert.True(ship.TotalFuel > 1f, $"Tank nach Tankstopp: {ship.TotalFuel}");
    }

    [Fact]
    public void Notkauf_LeererTank_KeineTankstelleErreichbar_Aufpreis2x_NurErforderlicheMenge_Geldsenke()
    {
        var u = TestWorlds.Default();
        new MarketSystem().Step(u, 1);
        var ship = ParkedTrader(u);
        var steel = u.Stations[new StationId(1)];
        ship.Sector = steel.Sector;
        ship.Position = steel.DockPoint(ship.Id);
        ship.Fuel.Clear();
        ship.Credits = 1000f;
        float stCreditsBefore = u.Stations.Values.Sum(s => s.Credits);
        EmergencyRefuel? ev = null;
        u.Events.Raised += e => { if (e is EmergencyRefuel er) ev = er; };

        var target = u.Stations[u.Sectors[new SectorId(3)].Stations[0]];
        var plan = Refuel.PrepareTrip(u, ship, target.Sector, target.DockPoint(ship.Id));

        Assert.NotNull(ev);
        Assert.Equal(DemoUniverse.Propellant, ev!.Good);
        // Aufpreis Faktor 2 auf den Basispreis (32 → 64 je Einheit); Erlös fließt an keine Station
        Assert.Equal(ev.Units * 64f, ev.Cost, 3);
        Assert.Equal(1000f - ev.Cost, ship.Credits, 3);
        Assert.Equal(stCreditsBefore, u.Stations.Values.Sum(s => s.Credits), 3);
        // nur so viel Treibstoff, dass die nächste Tankstelle erreichbar ist (aufgerundet auf ganze Einheiten)
        var refi = u.Stations.Values.Where(s => s.TypeKey == "fuel_refinery")
            .OrderBy(s => Travel.Estimate(u, ship.Sector, ship.Position, s.Sector, s.DockPoint(ship.Id), ship))
            .ThenBy(s => s.Id.Value).First();
        float needToStop = Refuel.Consumption(ship, Travel.EstimateDistance(u, ship.Sector, ship.Position, refi.Sector, refi.DockPoint(ship.Id), ship));
        Assert.Equal(MathF.Ceiling(needToStop), ev.Units, 3);
        // der Tankstopp-Verbrauch der Anfahrt wird direkt wieder gebucht
        Assert.True(ship.FuelAmount(DemoUniverse.Propellant) > 0f && ship.FuelAmount(DemoUniverse.Propellant) <= ev.Units,
            $"Tank nach Notkauf+Anfahrt: {ship.FuelAmount(DemoUniverse.Propellant)} (gekauft {ev.Units})");
        // danach normal weiter: Tankstopp zur Raffinerie
        Assert.Equal(Refuel.TripPlan.FuelStop, plan);
        Assert.IsType<GoToOrder>(ship.Order);
    }

    [Fact]
    public void Notkauf_Pleite_KeinNotkauf_SondernStuck()
    {
        var u = TestWorlds.Default();
        new MarketSystem().Step(u, 1);
        var ship = ParkedTrader(u);
        var steel = u.Stations[new StationId(1)];
        ship.Sector = steel.Sector;
        ship.Position = steel.DockPoint(ship.Id);
        ship.Fuel.Clear();
        ship.Credits = 10f;   // zu wenig für eine Einheit zum Aufpreis (64 Cr)
        int events = 0;
        u.Events.Raised += e => { if (e is EmergencyRefuel) events++; };

        var target = u.Stations[u.Sectors[new SectorId(3)].Stations[0]];
        var plan = Refuel.PrepareTrip(u, ship, target.Sector, target.DockPoint(ship.Id));

        Assert.Equal(Refuel.TripPlan.Stuck, plan);   // pleite: nicht reisen, Leerlauf → FleetSystem gibt später auf
        Assert.Equal(0, events);
        Assert.Null(ship.Transit);
        Assert.Equal(10f, ship.Credits, 3);
        Assert.Equal(0f, ship.TotalFuel, 3);
    }

    [Fact]
    public void Raffinerie_ProduziertTreibstoff_und_VerkauftAnSchiff_KasseBestandCreditsKorrekt()
    {
        var u = TestWorlds.Default();
        var refi = Refinery(u);
        var prop = DemoUniverse.Propellant;
        float p0 = refi.Market[prop].Amount;
        float h0 = refi.Market[DemoUniverse.Hydrogen].Amount;
        float e0 = refi.Market[DemoUniverse.Energy].Amount;

        new SimulationRunner().Run(u, 30);   // Rezept alle 5 Ticks: 3 Wasserstoff + 1 Energie → 4 Treibstoff
        Assert.True(refi.Market[prop].Amount > p0, "Raffinerie produziert Treibstoff");
        Assert.True(refi.Market[DemoUniverse.Hydrogen].Amount < h0, "Wasserstoff wird verbraucht");
        Assert.True(refi.Market[DemoUniverse.Energy].Amount < e0, "Energie wird verbraucht");

        // Verkauf an ein angedocktes Schiff mit leerem Tank
        var ship = ParkedTrader(u);
        ship.Sector = refi.Sector;
        ship.Position = refi.DockPoint(ship.Id);
        ship.Fuel.Clear();
        ship.Credits = 100_000f;
        refi.Market[prop].Amount = 500f;   // genug Bestand fürs Volltanken
        float price = refi.Market[prop].Price;
        Assert.True(price > 0f);
        float stockBefore = refi.Market[prop].Amount, stC = refi.Credits, shC = ship.Credits;
        int ev = 0;
        float units = 0f, cost = 0f;
        u.Events.Raised += e => { if (e is FuelBought fb) { ev++; units = fb.Units; cost = fb.Cost; } };

        float bought = Refuel.TankUp(u, ship, refi);

        Assert.Equal(1, ev);
        Assert.Equal(200f, bought, 3);                    // bis voll (Tankkapazität)
        Assert.Equal(bought, units, 3);
        Assert.Equal(bought, ship.FuelAmount(prop), 3);
        Assert.Equal(bought * price, cost, 3);
        Assert.Equal(stockBefore - bought, refi.Market[prop].Amount, 3);
        Assert.Equal(stC + cost, refi.Credits, 3);
        Assert.Equal(shC - cost, ship.Credits, 3);
        Assert.Equal(0f, ship.FuelSpace, 3);
    }

    [Fact]
    public void Haendler_BewertungZiehtTreibstoffkostenAb()
    {
        var u = TestWorlds.Default();
        new MarketSystem().Step(u, 1);
        var ship = ParkedTrader(u);
        var prop = DemoUniverse.Propellant;
        var buy = u.Stations[new StationId(1)];
        var sell = u.Stations[new StationId(2)];

        // TripFuelCost = Verbrauch (Hin- + Rückfahrt, Luftlinie) × Treibstoffpreis
        float d = Travel.EstimateDistance(u, ship.Sector, ship.Position, buy.Sector, buy.DockPoint(ship.Id), ship)
                + Travel.EstimateDistance(u, buy.Sector, buy.DockPoint(ship.Id), sell.Sector, sell.DockPoint(ship.Id), ship);
        float expected = Refuel.Consumption(ship, d, prop) * Refuel.FuelPrice(u, ship, prop, buy);
        Assert.True(expected > 0f);
        Assert.Equal(expected, Refuel.TripFuelCost(u, ship, buy, sell), 3);

        // mit überhöhtem Verbrauch lohnt sich keine Route mehr → kein Auftrag
        u.Modules["chem_engine_s"].Stats["fuelUse"] = 1000f;
        ship.RecomputeStats(u);
        Assert.Equal(1000f, ship.Stats.FuelUsePerUnit);
        ship.Order = null; ship.IdleSince = -1; ship.NextThinkTick = u.Tick;
        new TraderSystem().Step(u, 1);
        Assert.Null(ship.Order);

        // normaler Verbrauch: wieder ein Auftrag (Kontrollfall)
        u.Modules["chem_engine_s"].Stats["fuelUse"] = 0.002f;
        ship.RecomputeStats(u);
        ship.Order = null; ship.IdleSince = -1; ship.NextThinkTick = u.Tick;
        new TraderSystem().Step(u, 1);
        Assert.NotNull(ship.Order);
    }

    [Fact]
    public void Raffinerie_ProduziertGrundmengeAuchOhneWasserstoff()
    {
        var u = TestWorlds.Default();
        var ref_ = Refinery(u);
        var good = DemoUniverse.Propellant;
        ref_.Market[DemoUniverse.Hydrogen].Amount = 0f;
        ref_.Market[good].Amount = 0f;
        ref_.Market[good].Target = 100f;
        var before = ref_.Market[good].Amount;
        // direkt die Produktion laufen lassen, ohne Händler (Wasserstoff bleibt 0)
        var prod = new ProductionSystem();
        for (int i = 0; i < 200; i++) { u.Tick++; ref_.Market[DemoUniverse.Hydrogen].Amount = 0f; prod.Step(u, 1); }
        Assert.True(ref_.Market[good].Amount >= before + 8f);   // 200 Ticks / 20 → ≥ 10 Zyklen à 1
    }

    [Fact]
    public void Determinismus_ZweiLaeufeGleicherSeed_und_SaveLoadFortsetzung_BitIdentisch()
    {
        static Universe Run(ulong seed, int ticks)
        {
            var u = TestWorlds.Default(seed);
            new SimulationRunner().Run(u, ticks);
            return u;
        }
        var a = Run(42, 5_000);
        var b = Run(42, 5_000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        Assert.Contains(a.Ships.Values, s => s.TotalFuel < 200f);   // Verbrauch war wirksam (Tanks nicht mehr voll)

        // Save → Load → Fortsetzung: bit-identisch (Fuel ist Save- und Hash-Bestandteil)
        var json = SaveGameJson.Write(a, Now);
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
        new SimulationRunner().Run(a, 3_000);
        new SimulationRunner().Run(loaded, 3_000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Langlauf_20000Ticks_KeineNegativenTanks_KeinDauerTransit_KeineGeister_SpielerTankUnberuehrt()
    {
        var u = TestWorlds.Default();
        int notk = 0, tankBuys = 0;
        u.Events.Raised += e =>
        {
            if (e is EmergencyRefuel) notk++;
            else if (e is FuelBought) tankBuys++;
        };
        new SimulationRunner().Run(u, 20_000);

        // Spielerschiff-Tank bleibt in 4b unangetastet (Spielerflug läuft nicht über Travel.Start)
        var player = u.Ships[u.PlayerShip];
        Assert.Equal(200f, player.TotalFuel, 3);
        Assert.Equal(200f, player.FuelAmount(DemoUniverse.Propellant), 3);

        foreach (var s in u.Ships.Values)
        {
            Assert.True(s.TotalFuel >= -0.001f, $"negativer Tank bei Schiff {s.Id.Value}");
            Assert.True(s.TotalFuel <= s.Stats.FuelCapacity + 0.001f, $"Tank über Kapazität bei Schiff {s.Id.Value}");
            // kein Schiff hängt dauerhaft im Transit (Ankunft längst vorbei)
            // Phase 15 (latente Annahme): Polizei und Piraten laufen im Intervall 5 (Phase 11) — ihre Ankunft wird bis zu 5 Ticks später verarbeitet
            int slack = s.Role is ShipRole.Police or ShipRole.Pirate ? 5 : 0;
            Assert.True(s.Transit is null || s.Transit.ArrivalTick >= u.Tick - slack,   // Ankunft im aktuellen Tick wird erst im nächsten Schritt verarbeitet
                $"Schiff {s.Id.Value} hängt im Transit (Ankunft {s.Transit?.ArrivalTick}, Tick {u.Tick})");
            // kein Tankstopp (GoToOrder) ohne laufende Reise = Geisterauftrag
            Assert.False(s.Order is GoToOrder && s.Transit is null, $"Geister-Tankstopp bei Schiff {s.Id.Value}");
            // Schiffsindex konsistent (keine Geisterreservierungen)
            if (s.Transit is null) Assert.Contains(s.Id, u.Tracker.ParkedIn(s.Sector));
            else foreach (var leg in s.Transit.Legs) Assert.Contains(s.Id, u.Tracker.TransitIn(leg.Sector));
        }
        foreach (var sec in u.Sectors.Keys)
            foreach (var id in u.Tracker.ParkedIn(sec)) Assert.Null(u.Ships[id].Transit);

        Assert.True(tankBuys > 0, "Raffinerien haben an Schiffe verkauft");
        Assert.True(notk < 50, $"massenhaft Notkäufe/Liegenbleiber: {notk}");
    }
}
