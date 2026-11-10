using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>Phase 2: Systeme fragen Capability/Controller statt ShipRole.Player; die Ableitung aus der Rolle läuft an jeder Entstehungsstelle.</summary>
public class CapabilityTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Defaults_JeRolle_InklusiveSpieler()
    {
        Assert.Equal(Capability.Cargo, CapabilityDefaults.ForRole(ShipRole.Trader));
        Assert.Equal(Capability.Cargo | Capability.Mining, CapabilityDefaults.ForRole(ShipRole.Miner));
        Assert.Equal(Capability.Combat, CapabilityDefaults.ForRole(ShipRole.Fighter));
        Assert.Equal(Capability.Cargo | Capability.Mining, CapabilityDefaults.ForRole(ShipRole.Player));
        Assert.Equal(Controller.Ai, CapabilityDefaults.ControllerFor(ShipRole.Trader));
        Assert.Equal(Controller.Ai, CapabilityDefaults.ControllerFor(ShipRole.Miner));
        Assert.Equal(Controller.Ai, CapabilityDefaults.ControllerFor(ShipRole.Fighter));
        Assert.Equal(Controller.Player, CapabilityDefaults.ControllerFor(ShipRole.Player));
    }

    [Fact]
    public void NewShip_AddShip_AndDemoUniverse_DeriveCapabilitiesAndController()
    {
        var u = DemoUniverse.Create();
        var sec = u.Sectors.Keys.First();

        // WorldBuilder.NewShip leitet ab
        var fighter = WorldBuilder.NewShip(u, u.NewShipId(), ShipRole.Fighter, 1, sec, 100, 100);
        Assert.Equal(Capability.Combat, fighter.Capabilities);
        Assert.Equal(Controller.Ai, fighter.Controller);
        Assert.False(fighter.IsPlayer);

        // direkt konstruiertes Ship + AddShip leitet ab (kein Schiff mit Capability.None)
        var direct = new Ship { Id = u.NewShipId(), Role = ShipRole.Trader, Sector = sec };
        u.AddShip(direct);
        Assert.Equal(Capability.Cargo, direct.Capabilities);
        Assert.Equal(Controller.Ai, direct.Controller);
        var directPlayer = new Ship { Id = u.NewShipId(), Role = ShipRole.Player, Sector = sec };
        u.AddShip(directPlayer);
        Assert.True(directPlayer.IsPlayer);
        Assert.Equal(Capability.Cargo | Capability.Mining, directPlayer.Capabilities);

        // die Demo-Welt: jedes Schiff hat die abgeleiteten Werte
        foreach (var s in u.Ships.Values)
        {
            Assert.Equal(CapabilityDefaults.ForRole(s.Role), s.Capabilities);
            Assert.Equal(CapabilityDefaults.ControllerFor(s.Role), s.Controller);
        }
        Assert.True(u.Ships[u.PlayerShip].IsPlayer);
    }

    [Fact]
    public void Has_WorksWithFlagCombinations()
    {
        var u = DemoUniverse.Create();
        var sec = u.Sectors.Keys.First();
        var miner = u.Ships.Values.First(s => s.Role == ShipRole.Miner);
        var trader = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        var fighter = WorldBuilder.NewShip(u, u.NewShipId(), ShipRole.Fighter, 1, sec, 100, 100);

        Assert.True(miner.Has(Capability.Cargo));
        Assert.True(miner.Has(Capability.Mining));
        Assert.True(miner.Has(Capability.Cargo | Capability.Mining));
        Assert.False(miner.Has(Capability.Combat));
        Assert.False(miner.Has(Capability.Cargo | Capability.Mining | Capability.Combat));

        Assert.True(trader.Has(Capability.Cargo));
        Assert.False(trader.Has(Capability.Mining));
        Assert.False(trader.Has(Capability.Cargo | Capability.Mining));

        Assert.True(fighter.Has(Capability.Combat));
        Assert.False(fighter.Has(Capability.Cargo));
        Assert.False(fighter.Has(Capability.Mining));
    }

    /// <summary>Ein Miner in Schussposition am Feld, dazu alle anderen Schiffe entfernt (isoliertes System).</summary>
    static (Universe u, Ship miner, Sector sector, ResourceField field, List<SimEvent> events) MinerSetup(Capability caps)
    {
        var u = DemoUniverse.Create();
        var sector = u.Sectors[new SectorId(1)];
        var field = sector.Fields[0];
        var miner = u.Ships.Values.First(s => s.Role == ShipRole.Miner);
        foreach (var s in u.Ships.Values.Where(s => s.Id != miner.Id).ToList()) u.RemoveShip(s.Id, "test");
        miner.Capabilities = caps;
        miner.CargoCapacity = 1_000f;
        miner.Cargo.Clear();
        miner.Cargo[DemoUniverse.IronOre] = 5f;   // vorhandene Fracht darf weder aufgestockt noch verkauft werden
        miner.Position = field.WorkPoint(miner.Id);
        miner.Order = new MineOrder(sector.Id, 0, new StationId(1));
        miner.MinePhase = MinePhase.Mining;
        var events = new List<SimEvent>();
        u.Events.Raised += e => events.Add(e);
        return (u, miner, sector, field, events);
    }

    [Fact]
    public void Miner_WithoutMiningCapability_MinesAndSellsNothing()
    {
        var (u, miner, sector, field, events) = MinerSetup(Capability.Cargo);   // Frachtraum, aber kein Mining
        float oreBefore = Belt.OreIn(sector, field);
        float creditsBefore = miner.Credits;
        var system = new MinerSystem();
        for (int i = 0; i < 400; i++) system.Step(u, 1);

        Assert.Equal(oreBefore, Belt.OreIn(sector, field));            // Asteroiden-Erz unverändert
        Assert.Equal(5f, miner.Cargo.GetValueOrDefault(DemoUniverse.IronOre));   // Fracht unverändert (leer abgesehen vom Startbestand)
        Assert.Single(miner.Cargo);
        Assert.Equal(creditsBefore, miner.Credits);                    // weder gekauft noch verkauft
        Assert.Null(miner.Order);                                       // der Auftrag wird sauber freigegeben (nicht eingefroren)
        Assert.Equal(MinePhase.Idle, miner.MinePhase);
        Assert.DoesNotContain(events, e => e is MinerShot m && m.Ship == miner.Id);
        Assert.DoesNotContain(events, e => e is MineDelivered d && d.Ship == miner.Id);
        Assert.DoesNotContain(events, e => e is TradeCompleted t && t.Ship == miner.Id);
    }

    [Fact]
    public void Miner_WithMiningCapability_MinesAsBefore()
    {
        var (u, miner, sector, field, events) = MinerSetup(Capability.Cargo | Capability.Mining);
        float oreBefore = Belt.OreIn(sector, field);
        var system = new MinerSystem();
        for (int i = 0; i < 400; i++) system.Step(u, 1);

        Assert.True(Belt.OreIn(sector, field) < oreBefore, "Asteroidenerz muss abgebaut werden");
        Assert.Contains(events, e => e is MinerShot m && m.Ship == miner.Id);
        Assert.True(miner.Cargo.Values.Sum() > 5f, "Erz aus den Schüssen landet in der Fracht");
    }

    [Fact]
    public void Trader_WithoutCargoCapability_DoesNotTrade()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 300);   // Preise und Bestände einschwingen lassen
        var traders = u.Ships.Values.Where(s => s.Role == ShipRole.Trader).ToList();
        var noCargo = traders[0];
        var control = traders[1];
        foreach (var s in u.Ships.Values.Where(s => s.Id != noCargo.Id && s.Id != control.Id).ToList()) u.RemoveShip(s.Id, "test");
        foreach (var s in new[] { noCargo, control })
        {
            if (s.Transit != null) u.FinishTransit(s);
            s.Order = null; s.Phase = TradePhase.Idle; s.NextThinkTick = 0; s.Cargo.Clear();
        }
        noCargo.Capabilities = Capability.Combat;   // kein Frachtraum
        float creditsBefore = noCargo.Credits;
        var events = new List<SimEvent>();
        u.Events.Raised += e => events.Add(e);

        var system = new TraderSystem();
        bool noCargoOrdered = false, controlOrdered = false;
        for (int i = 0; i < 500; i++)
        {
            system.Step(u, 1);
            if (noCargo.Order != null) noCargoOrdered = true;
            if (control.Order != null) controlOrdered = true;
        }

        Assert.False(noCargoOrdered, "Trader ohne Cargo darf keinen Auftrag annehmen");
        Assert.True(controlOrdered, "normaler Trader handelt wie bisher");
        Assert.Empty(noCargo.Cargo);
        Assert.Equal(creditsBefore, noCargo.Credits);
        Assert.DoesNotContain(events, e => e is TradeCompleted t && t.Ship == noCargo.Id);
    }

    /// <summary>Ein Händler mit Auftrag auf einer laufenden Reise (Welt eingeschwungen, andere Schiffe entfernt).</summary>
    static (Universe u, Ship trader, Ship other) TraderInTransit()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 300);
        var traders = u.Ships.Values.Where(s => s.Role == ShipRole.Trader).ToList();
        var trader = traders[0];
        var other = traders[1];
        foreach (var s in u.Ships.Values.Where(s => s.Id != trader.Id && s.Id != other.Id).ToList()) u.RemoveShip(s.Id, "test");
        foreach (var s in new[] { trader, other })
        {
            if (s.Transit != null) u.FinishTransit(s);
            s.Order = null; s.Phase = TradePhase.Idle; s.NextThinkTick = 0; s.Cargo.Clear();
        }
        var system = new TraderSystem();
        for (int i = 0; i < 200 && trader.Transit is null; i++) { u.Tick++; system.Step(u, 1); }
        Assert.NotNull(trader.Transit);   // Voraussetzung: der Händler ist unterwegs
        Assert.IsType<TradeOrder>(trader.Order);
        return (u, trader, other);
    }

    [Fact]
    public void Trader_LosingCargoCapabilityInTransit_ArrivesAndIsReleased_NotFrozen()
    {
        var (u, trader, other) = TraderInTransit();
        var arrival = trader.Transit!.ArrivalTick;
        var destSector = trader.Transit!.ToSector;
        float creditsBefore = trader.Credits;
        trader.Capabilities = Capability.Combat;   // Frachtraum verloren, mitten auf der Reise

        var system = new TraderSystem();
        var events = new List<SimEvent>();
        u.Events.Raised += e => events.Add(e);
        int steps = (int)(arrival - u.Tick) + 5;   // vorher berechnen: u.Tick wächst in der Schleife
        for (int i = 0; i < steps; i++) { u.Tick++; system.Step(u, 1); }

        Assert.Null(trader.Transit);                      // die Reise wird beendet (kein ewiger Schwebezustand)
        Assert.Equal(destSector, trader.Sector);         // das Schiff steht am Ziel
        Assert.Null(trader.Order);                       // Auftrag freigegeben: keine Geisterreservierung mehr
        Assert.Equal(TradePhase.Idle, trader.Phase);
        Assert.Equal(creditsBefore, trader.Credits);     // ohne Frachtraum wird dabei nichts gekauft
        Assert.DoesNotContain(events, e => e is TradeCompleted t && t.Ship == trader.Id);
        Assert.NotNull(other);
    }

    [Fact]
    public void Trader_LosingCargoCapabilityWithOrder_ReleasesReservationForOthers()
    {
        var (u, trader, _) = TraderInTransit();
        var order = (TradeOrder)trader.Order!;
        trader.Capabilities = Capability.Combat;

        new TraderSystem().Step(u, 1);   // noch unterwegs: die Reise läuft zu Ende, der Auftrag wird sofort freigegeben

        Assert.Null(trader.Order);
        Assert.DoesNotContain(u.Ships.Values, s => s.Order is TradeOrder to && to.Buy == order.Buy && to.Good == order.Good && s.Id == trader.Id);
    }

    [Fact]
    public void Miner_LosingMiningCapabilityInTransit_ArrivesAndIsReleased_NotFrozen()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 50);
        var miner = u.Ships.Values.First(s => s.Role == ShipRole.Miner);
        foreach (var s in u.Ships.Values.Where(s => s.Id != miner.Id).ToList()) u.RemoveShip(s.Id, "test");
        if (miner.Transit != null) u.FinishTransit(miner);
        miner.Order = null; miner.MinePhase = MinePhase.Idle; miner.NextThinkTick = 0; miner.Cargo.Clear();
        var system = new MinerSystem();
        for (int i = 0; i < 300 && miner.Transit is null; i++) { u.Tick++; system.Step(u, 1); }
        Assert.NotNull(miner.Transit);
        Assert.IsType<MineOrder>(miner.Order);
        var arrival = miner.Transit!.ArrivalTick;

        miner.Capabilities = Capability.Cargo;   // Werkzeug verloren, mitten auf der Reise
        int steps = (int)(arrival - u.Tick) + 5;   // vorher berechnen: u.Tick wächst in der Schleife
        for (int i = 0; i < steps; i++) { u.Tick++; system.Step(u, 1); }

        Assert.Null(miner.Transit);
        Assert.Null(miner.Order);
        Assert.Equal(MinePhase.Idle, miner.MinePhase);
    }

    [Fact]
    public void SaveLoad_RestoresDerivedCapabilities_AndKeepsHashAndVersion()
    {
        Assert.Equal(7, SaveGame.CurrentVersion);   // v7: Spielerkasse (Phase 5b); v6: Modul-Waren/Bausatzstufen/stocks (Phase 5a); v5: zusätzlich Fuel je Schiff (v4: Modell/Slots/Fit der Schiffe)
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 500);
        var before = UniverseHash.Compute(u);

        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));

        Assert.Equal(before, UniverseHash.Compute(loaded));
        Assert.Equal(u.Ships.Count, loaded.Ships.Count);
        foreach (var s in loaded.Ships.Values)
        {
            Assert.Equal(CapabilityDefaults.ForRole(s.Role), s.Capabilities);
            Assert.Equal(CapabilityDefaults.ControllerFor(s.Role), s.Controller);
        }
        Assert.True(loaded.Ships[loaded.PlayerShip].IsPlayer);
        Assert.All(loaded.Ships.Values.Where(s => s.Role != ShipRole.Player), s => Assert.False(s.IsPlayer));
    }

    [Fact]
    public void Ueberschriebene_Faehigkeiten_ModellloserSchiffe_UeberlebenSaveLoad_UndHash()
    {
        // Schiffe mit Modell leiten ihre Fähigkeiten aus dem Fit ab; ein gespeicherter Wert zählt nur ohne Modell
        var u = DemoUniverse.Create();
        var sector = u.Sectors.Keys.First();
        var none = new Ship { Id = u.NewShipId(), Role = ShipRole.Fighter, Sector = sector, Capabilities = Capability.None };
        var odd = new Ship { Id = u.NewShipId(), Role = ShipRole.Fighter, Sector = sector, Capabilities = Capability.Combat | Capability.Salvage };
        u.AddShip(none);
        u.AddShip(odd);
        var before = UniverseHash.Compute(u);

        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));

        Assert.Equal(Capability.None, loaded.Ships[none.Id].Capabilities);
        Assert.Equal(Capability.Combat | Capability.Salvage, loaded.Ships[odd.Id].Capabilities);
        Assert.Equal(before, UniverseHash.Compute(loaded));
        Assert.NotEqual(before, UniverseHash.Compute(DemoUniverse.Create()));
    }

    [Fact]
    public void AddShip_BehaeltAusdruecklichGesetztesNone_UndLeitetNurUngesetztesAb()
    {
        var u = DemoUniverse.Create();
        var sector = u.Sectors.Keys.First();
        var none = new Ship { Id = new ShipId(9001), Role = ShipRole.Trader, Sector = sector, Capabilities = Capability.None };
        var unset = new Ship { Id = new ShipId(9002), Role = ShipRole.Trader, Sector = sector };
        u.AddShip(none);
        u.AddShip(unset);
        Assert.Equal(Capability.None, none.Capabilities);
        Assert.False(none.IsEconomyAi);
        Assert.Equal(Capability.Cargo, unset.Capabilities);
    }

    [Fact]
    public void AlterSave_OhneFaehigkeitsFelder_LeitetAusRolleAb()
    {
        var u = DemoUniverse.Create();
        var save = UniverseSerializer.ToSave(u, Now);
        foreach (var sh in save.Ships) { sh.Capabilities = null; sh.Controller = null; }
        var loaded = UniverseSerializer.FromSave(save);
        foreach (var s in loaded.Ships.Values)
        {
            Assert.Equal(CapabilityDefaults.ForRole(s.Role), s.Capabilities);
            Assert.Equal(CapabilityDefaults.ControllerFor(s.Role), s.Controller);
        }
    }
}
