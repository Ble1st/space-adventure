using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Fusionsbrennstoff (docs/SCHIFFSSLOTS.md „Fusionsbrennstoff und Tankanzeige“): Ware, Anreicherungsanlage, Fusionstriebwerk L in Bollwerk, Sturmvogel, Leviathan;
/// KI-Kriegsschiffe/Polizei tanken an der Heimatbasis aus der Kasse und bleiben nicht liegen; Tankanzeige je Treibstoffart (<see cref="TankInfo"/>).
/// </summary>
public class FusionTankTests
{
    static readonly string[] Capital = { "bastion", "destroyer", "battlecruiser" };

    /// <summary>
    /// Fusionsbrennstoff zurücknehmen (Teil der Rücknahmekette, aufgerufen von <see cref="GasNebulaTests.RevertGas"/>): Ware, Modul, Anreicherungsanlage (Typ und
    /// Stationen), Werftregal, Triebwerksslots der Großkampfklassen wieder M, Fits wieder chemisch, maxStations.
    /// </summary>
    internal static void RevertFusion(WorldDef def)
    {
        def.Goods.RemoveAll(g => g.Key == "fusion_fuel");
        def.Modules.RemoveAll(m => m.Key == "fusion_drive_l");
        def.StationTypes.RemoveAll(t => t.Key == "enrichment_plant");
        foreach (var t in def.StationTypes) t.Stocks?.Remove("fusion_drive_l");
        foreach (var s in def.Sectors) s.Stations.RemoveAll(x => x.Type == "enrichment_plant");
        foreach (var c in def.ShipClasses.Where(c => c.Key is "frigate" or "destroyer" or "battlecruiser"))
            foreach (var sl in c.Slots.Where(sl => sl.Type == "engine")) sl.Size = "M";
        foreach (var m in def.ShipModels)
            for (int i = 0; i < m.Fit.Count; i++) if (m.Fit[i] == "fusion_drive_l") m.Fit[i] = "chem_engine_m";
        var r = def.Rules ??= new EconomyRules();
        if (r.MaxStations == 73) r.MaxStations = 71;
    }

    static Universe World(ulong seed = 1, string? start = null) => WorldBuilder.Build(TestWorlds.Def(fuel: true, research: false), seed, start);

    [Fact]
    public void Inhalt_FusionsbrennstoffAnlageTriebwerkGrosskampfschiffe()
    {
        var u = World();
        var ff = u.FindGood("fusion_fuel")!;
        Assert.Equal(GoodForm.Liquid, ff.Form);
        Assert.Equal("fuel", ff.Category);
        var plants = u.Stations.Values.Where(s => s.TypeKey == "enrichment_plant").ToList();
        Assert.Equal(2, plants.Count);
        var r = u.StationTypes["enrichment_plant"].Recipes.Single();
        Assert.True(r.Inputs.ContainsKey(u.FindGood("hydrogen")!.Id) && r.Inputs.ContainsKey(u.FindGood("platinum")!.Id) && r.Inputs.ContainsKey(u.FindGood("energy")!.Id));
        Assert.Equal(StationSize.L, u.StationTypes["enrichment_plant"].Size);
        var drive = u.Modules["fusion_drive_l"];
        Assert.Equal(SlotSize.L, drive.Size);
        Assert.Equal(ff.Id, Assert.Single(drive.FuelTypes).Good);
        Assert.True(drive.Stats["thrust"] > u.Modules["chem_engine_m"].Stats["thrust"]);
        foreach (var key in Capital)
        {
            var m = u.ShipModels[key];
            Assert.Contains("fusion_drive_l", m.Fit);
            var cls = u.ShipClasses[m.ClassKey];
            Assert.Null(FitRules.Check(u.SlotTypes, u.Modules, cls, cls.Slots, m.Fit));
        }
        // eine Einheit ist teuer (mehr als das Dreifache), reicht aber weit (weniger als die Hälfte der Einheiten je Strecke)
        var prop = u.FindGood("propellant")!;
        float fusionPerUnit = drive.Stats["fuelUse"] / drive.FuelTypes[0].Efficiency, chemPerUnit = u.Modules["chem_engine_m"].Stats["fuelUse"];
        Assert.True(fusionPerUnit < chemPerUnit / 2f);
        Assert.True(ff.BasePrice > 3f * prop.BasePrice);
    }

    [Fact]
    public void Kriegsschiff_TanktAnDerHeimatbasisAusDerKasse_BleibtNichtLiegen()
    {
        var u = World();
        var ff = u.FindGood("fusion_fuel")!;
        var faction = u.Actors[new ActorId(1)];
        var home = u.StationsById().First(s => s.Owner == faction.Id && s.TypeKey == "headquarters");
        var m = u.ShipModels["bastion"];
        var ship = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Police, Controller.Ai, faction.Id, home, 0f);
        ship.HomeBase = home.Id;
        u.AddShip(ship);
        Assert.Equal(ship.Stats.FuelCapacity, ship.FuelAmount(ff.Id), 3);   // neue Schiffe starten voll (Fusion)
        ship.Fuel[ff.Id] = 0f;
        faction.Treasury.Credits = 1_000_000f;
        float before = faction.Treasury.Credits;
        PirateSystem.ServiceAtBase(u, ship, "test");
        Assert.True(ship.FuelAmount(ff.Id) > 0.9f * ship.Stats.FuelCapacity, $"getankt {ship.FuelAmount(ff.Id)}");
        Assert.True(faction.Treasury.Credits < before);
        // Lauf: keine Polizei/kein Kriegsschiff bleibt mit leerem Tank liegen
        var runner = new SimulationRunner();
        runner.Run(u, 6000);
        foreach (var s in u.Ships.Values.Where(s => s.Role is ShipRole.Police or ShipRole.Warship && s.HasModel))
            Assert.True(s.TotalFuel > 0.5f || s.Transit is not null, $"#{s.Id.Value} {s.ModelKey} Tank leer");
    }

    [Fact]
    public void Kriegstest_Startpaket_BollwerkMitFusionstankUndReichweite()
    {
        var u = World(1, "war");
        var me = u.Ships[u.PlayerShip];
        var ff = u.FindGood("fusion_fuel")!;
        Assert.Equal("bastion", me.ModelKey);
        var lines = TankInfo.For(u, me);
        var line = Assert.Single(lines);
        Assert.Equal(ff.Id, line.Good);
        Assert.Equal(me.Stats.FuelCapacity, line.Amount, 3);
        Assert.False(line.Low);
        Assert.True(line.Range > 100_000f, $"Reichweite {line.Range}");
        Assert.Contains("Spr.", TankInfo.RangeText(line));
    }

    [Fact]
    public void Tankanzeige_NurAngenommeneArten_ReichweiteNachRegel_KnappRot()
    {
        var u = World();
        var me = u.Ships[u.PlayerShip];
        var prop = u.FindGood("propellant")!;
        var line = Assert.Single(TankInfo.For(u, me));
        Assert.Equal(prop.Id, line.Good);
        Assert.Equal(me.FuelAmount(prop.Id) / Refuel.Consumption(me, 1f, prop.Id), line.Range, 0);
        Assert.Equal(line.Range / TankInfo.UnitsPerJump, line.Jumps, 3);
        me.Fuel[prop.Id] = 0.1f * me.Stats.FuelCapacity;
        Assert.True(TankInfo.For(u, me)[0].Low);
        // Ionentriebwerk: nur Ionengas wird angezeigt
        int engine = me.Slots.FindIndex(s => s.Type == "engine");
        me.Fit[engine] = "ion_engine_s";
        me.RecomputeStats(u);
        Assert.Equal("ion_gas", u.Goods[Assert.Single(TankInfo.For(u, me)).Good].Key);
        // Treibstoff aus: keine Zeile
        u.Rules.FuelActive = false;
        Assert.Empty(TankInfo.For(u, me));
    }

    [Fact]
    public void Tankdienst_BietetDieRichtigeArt_AnlageVerkauftFusion_RaffinerieNicht()
    {
        var u = World(1, "war");
        new SimulationRunner().Run(u, 1);
        var me = u.Ships[u.PlayerShip];
        var ff = u.FindGood("fusion_fuel")!;
        me.Fuel[ff.Id] = 10f;
        var svc = new RefuelService();
        var plant = u.Stations.Values.First(s => s.TypeKey == "enrichment_plant");
        var offers = svc.Offers(u, plant, me);
        Assert.NotEmpty(offers);
        Assert.All(offers, o => Assert.Equal(ff.Id, o.Good));
        var refinery = u.Stations.Values.First(s => s.TypeKey == "fuel_refinery");
        Assert.Empty(svc.Offers(u, refinery, me));
        u.Player.Treasury.Credits = 1_000_000f;
        Assert.True(svc.Execute(u, plant, me, new ServiceRequest("refuel:fill", default, 0f)).Success);
        Assert.True(me.FuelAmount(ff.Id) > 10f);
    }

    [Fact]
    public void SpeichernLaden_FusionstankBitgleich()
    {
        var u = World(2, "war");
        new SimulationRunner().Run(u, 800);
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        new SimulationRunner().Run(u, 400);
        new SimulationRunner().Run(loaded, 400);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }
}
