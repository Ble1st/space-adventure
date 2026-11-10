using System.Text.Json.Nodes;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>Phase 3b: Schiffe bekommen Modell, Slotliste und Fit; Werte kommen aus ShipStats; Save v4.</summary>
public class ShipFitTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Demotet einen aktuellen v4-Save auf v3: Version 3, neue Schiffsfelder und Content-Sektionen entfernt.</summary>
    static string ToV3Json(string v4Json)
    {
        var node = JsonNode.Parse(WalletTests.ToV6Json(v4Json))!.AsObject();   // Spielerkasse zurück aufs Schiff (vor v7)
        node["Version"] = 3;
        node.Remove("SlotTypes");
        node.Remove("Modules");
        node.Remove("ShipClasses");
        node.Remove("ShipModels");
        foreach (var sh in node["Ships"]!.AsArray())
        {
            sh!.AsObject().Remove("ModelKey");
            sh.AsObject().Remove("Slots");
            sh.AsObject().Remove("Fit");
        }
        return node.ToJsonString();
    }

    [Fact]
    public void Startwelt_SchiffeHabenModellUndWerteWieBisher()
    {
        var u = DemoUniverse.Create();
        Assert.All(u.Ships.Values, s => Assert.NotEmpty(s.ModelKey));
        foreach (var s in u.Ships.Values)
        {
            var expectedModel = WorldBuilder.UnitModelFor(s.Role, s.Role == ShipRole.Miner ? 60f : 100f);
            Assert.Equal(expectedModel, s.ModelKey);
            Assert.True(u.ShipModels.ContainsKey(s.ModelKey));
            var model = u.ShipModels[s.ModelKey];
            var cls = u.ShipClasses[model.ClassKey];
            // Slotliste = die der Klasse, Fit = der des Modells
            Assert.Equal(cls.Slots.Count, s.Slots.Count);
            Assert.Equal(model.Fit, s.Fit);
            // Werte == bisher: Laderaum aus ships[].cargo, Hülle 100, Fähigkeiten aus der Rolle, Tempo 8
            float expectedCargo = s.Role == ShipRole.Miner ? 60f : 100f;
            Assert.Equal(expectedCargo, s.CargoCapacity);
            Assert.Equal(expectedCargo, s.Stats.Cargo);
            Assert.Equal(100f, s.MaxHull);
            Assert.Equal(100f, s.Hull);
            Assert.Equal(100f, s.Stats.Hull);
            Assert.Equal(CapabilityDefaults.ForRole(s.Role), s.Capabilities);
            Assert.Equal(CapabilityDefaults.ForRole(s.Role), s.Stats.Capabilities);
            Assert.Equal(Travel.ShipSpeed, s.Stats.Speed);
            Assert.Equal(Travel.ShipSpeed, Travel.SpeedOf(s));
        }
        // Konkrete Zuordnung: Trader 100, Miner 60, Spieler unit_player
        Assert.All(u.Ships.Values.Where(s => s.Role == ShipRole.Trader), s => Assert.Equal("unit_trader_100", s.ModelKey));
        Assert.All(u.Ships.Values.Where(s => s.Role == ShipRole.Miner), s => Assert.Equal("unit_miner_60", s.ModelKey));
        Assert.Equal("unit_player", u.Ships[u.PlayerShip].ModelKey);
    }

    [Fact]
    public void WeltJson_ExplizitesModellWirdUebernommen_UnbekannterKeyWirft()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Player.Model = "unit_miner_100";
        def.Ships[0].Model = "unit_miner_60";
        var u = WorldBuilder.Build(def);
        Assert.Equal("unit_miner_100", u.Ships[u.PlayerShip].ModelKey);
        Assert.Equal("unit_miner_60", u.Ships.Values.First(s => s.Role == ShipRole.Trader && s.Id.Value == 1).ModelKey);

        def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Ships[0].Model = "lorem";
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
        Assert.Contains("unbekanntes Schiffsmodell 'lorem'", ex.Message);
    }

    [Fact]
    public void Fighter_OhneModellBleibtPauschal()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Ships.Add(new ShipGroupDef { Role = "fighter", Faction = 1, Sector = 1, Cargo = 100, Count = 1 });
        var u = WorldBuilder.Build(def);
        var fighter = u.Ships.Values.First(s => s.Role == ShipRole.Fighter);
        Assert.Equal("", fighter.ModelKey);
        Assert.Equal("", WorldBuilder.UnitModelFor(ShipRole.Fighter, 100f));
        Assert.Equal(100f, fighter.MaxHull);
        Assert.Equal(100f, fighter.Hull);
        Assert.Equal(100f, fighter.CargoCapacity);
        Assert.Equal(Capability.Combat, fighter.Capabilities);
        Assert.Equal(Travel.ShipSpeed, Travel.SpeedOf(fighter));
    }

    [Fact]
    public void SaveLoad_V4_Roundtrip_IdentischInklHashModellSlotsFit()
    {
        Assert.Equal(7, SaveGame.CurrentVersion);   // v7: Spielerkasse (Phase 5b); v6: Modul-Waren/Bausatzstufen/stocks (Phase 5a); v5: zusätzlich Fuel je Schiff (v4: Modell/Slots/Fit)
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 2_000);
        var before = UniverseHash.Compute(u);

        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));

        Assert.Equal(before, UniverseHash.Compute(loaded));
        Assert.NotEmpty(loaded.SlotTypes);
        Assert.NotEmpty(loaded.Modules);
        Assert.NotEmpty(loaded.ShipClasses);
        Assert.NotEmpty(loaded.ShipModels);
        foreach (var s in u.Ships.Values)
        {
            var l = loaded.Ships[s.Id];
            Assert.Equal(s.ModelKey, l.ModelKey);
            Assert.Equal(s.Fit, l.Fit);
            Assert.Equal(s.Slots.Count, l.Slots.Count);
            Assert.Equal(s.Slots.Select(x => (x.Type, x.Size, x.Facing, x.Arc)), l.Slots.Select(x => (x.Type, x.Size, x.Facing, x.Arc)));
            Assert.Equal(s.Stats.Hull, l.Stats.Hull);
            Assert.Equal(s.Stats.Cargo, l.Stats.Cargo);
            Assert.Equal(s.Stats.Speed, l.Stats.Speed);
            Assert.Equal(s.Stats.Capabilities, l.Stats.Capabilities);
            Assert.Equal(s.CargoCapacity, l.CargoCapacity);
            Assert.Equal(s.MaxHull, l.MaxHull);
            Assert.Equal(s.Capabilities, l.Capabilities);
        }
    }

    [Fact]
    public void Migration_V3ToV4_SchiffeBekommenEinheitsmodelle_VerhaltenGleich()
    {
        var u = DemoUniverse.Create();
        var v3Json = ToV3Json(SaveGameJson.Write(u, Now));
        Assert.Contains("\"Version\":3", v3Json);
        Assert.DoesNotContain("ModelKey", v3Json);

        var (loaded, _) = SaveGameJson.Read(v3Json);

        // Modelle je Rolle/Laderaum wie in der Migration vorgesehen
        Assert.All(loaded.Ships.Values.Where(s => s.Role == ShipRole.Trader), s => Assert.Equal("unit_trader_100", s.ModelKey));
        Assert.All(loaded.Ships.Values.Where(s => s.Role == ShipRole.Miner), s => Assert.Equal("unit_miner_60", s.ModelKey));
        Assert.Equal("unit_player", loaded.Ships[loaded.PlayerShip].ModelKey);
        // Slot-Content kam mit der Migration aus der Default-Welt-Registry
        Assert.True(loaded.ShipModels.ContainsKey("unit_trader_100"));
        foreach (var s in loaded.Ships.Values)
        {
            var model = loaded.ShipModels[s.ModelKey];
            Assert.Equal(model.Fit, s.Fit);                            // Fit beim Laden aus dem Modell gefüllt
            Assert.Equal(loaded.ShipClasses[model.ClassKey].Slots.Count, s.Slots.Count);
            float expectedCargo = s.Role == ShipRole.Miner ? 60f : 100f;
            Assert.Equal(expectedCargo, s.CargoCapacity);              // Laderaum aus Stats.Cargo
            Assert.Equal(100f, s.MaxHull);
            Assert.Equal(CapabilityDefaults.ForRole(s.Role), s.Capabilities);
        }

        // Verhalten gleich: 2000 Ticks ab dem migrierten Save == 2000 Ticks ab frisch erzeugter Welt gleicher Seed
        var fresh = DemoUniverse.Create();
        var runner = new SimulationRunner();
        runner.Run(loaded, 2_000);
        runner.Run(fresh, 2_000);
        Assert.Equal(UniverseHash.Compute(fresh), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void FitOhneMiningModul_VerliertMining_MinerSystemGibtJobFrei()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1_000);
        var miner = u.Ships.Values.First(s => s.Role == ShipRole.Miner && s.Order is MineOrder);
        Assert.True(miner.Has(Capability.Mining));

        // Mining-Laser aus dem Tool-Slot ausbauen und Stats neu berechnen
        int tool = miner.Slots.FindIndex(s => s.Type == "tool");
        Assert.True(tool >= 0 && miner.Fit[tool] == "mining_laser_1");
        miner.Fit[tool] = null;
        Assert.True(miner.RecomputeStats(u));

        Assert.Equal(Capability.Cargo, miner.Capabilities);            // Fähigkeiten kommen aus Stats
        Assert.False(miner.Has(Capability.Mining));
        Assert.True(miner.Has(Capability.Cargo));

        new MinerSystem().Step(u, 1);                                  // JobRelease: Auftrag fällt weg
        Assert.Null(miner.Order);
        Assert.Equal(MinePhase.Idle, miner.MinePhase);
    }

    [Fact]
    public void Tempo16_SchiffFaehrtKuerzer()
    {
        var u = DemoUniverse.Create();
        var normal = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        var fast = WorldBuilder.NewShip(u, u.NewShipId(), ShipRole.Trader, 1, new SectorId(1), 1000f, 100f, "unit_trader_100");
        u.AddShip(fast);
        fast.Stats.Speed = 16f;                                        // Tempo 16 (reiner Travel-Test, über ShipStats.MaxSpeed)

        Assert.Equal(16f, Travel.SpeedOf(fast));
        Assert.Equal(Travel.ShipSpeed, Travel.SpeedOf(normal));
        Assert.Equal(Travel.ShipSpeed, Travel.SpeedOf(null));          // ohne Schiff: Konstante

        var from = new SectorId(1); var to = new SectorId(3);
        var posA = new Vec2(0, 0); var posB = new Vec2(500, 500);
        int hops = u.Hops(from, to);
        float estNormal = Travel.Estimate(u, from, posA, to, posB, normal);
        float estFast = Travel.Estimate(u, from, posA, to, posB, fast);
        // Strecke skaliert mit dem Tempo: dist/8 vs. dist/16 (Gate-Hops + 1 sind gleich)
        Assert.True(MathF.Abs((estNormal - hops - 1f) / 2f - (estFast - hops - 1f)) < 1e-3f,
            $"Estimate normal {estNormal}, fast {estFast}");
        float tNormal = Travel.Ticks(u, from, posA, to, posB, normal);
        float tFast = Travel.Ticks(u, from, posA, to, posB, fast);
        Assert.True(tFast < tNormal, $"Ticks normal {tNormal}, fast {tFast}");
        Assert.True((Travel.Plan(u, 0, from, posA, to, posB, fast)!)[^1].EndTick < (Travel.Plan(u, 0, from, posA, to, posB, normal)!)[^1].EndTick);
    }

    [Fact]
    public void FleetSystem_Expand_KopiertModellSlotsFitVomElternschiff()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 20_000);   // Flottenwachstum erzeugt neue Schiffe (Ids ab 1000)
        var bought = u.Ships.Values.Where(s => s.Id.Value >= 1000).ToList();
        Assert.NotEmpty(bought);
        foreach (var s in u.Ships.Values)
        {
            Assert.NotEmpty(s.ModelKey);
            var model = u.ShipModels[s.ModelKey];
            Assert.Equal(model.Fit, s.Fit);
            Assert.Equal(u.ShipClasses[model.ClassKey].Slots.Count, s.Slots.Count);
            Assert.Equal(s.Stats.Cargo, s.CargoCapacity);
        }
        Assert.All(bought, s => Assert.Equal(WorldBuilder.UnitModelFor(s.Role, s.CargoCapacity), s.ModelKey));
    }

    [Fact]
    public void NachSaveLoad_WirdAusbauEinesModulsWeiterInFaehigkeitenSichtbar()
    {
        var u = DemoUniverse.Create();
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero)));
        var miner = loaded.Ships.Values.First(s => s.Role == ShipRole.Miner);
        Assert.True(miner.Has(Capability.Mining));
        miner.Fit[miner.Slots.FindIndex(s => s.Type == "tool")] = null;
        Assert.True(miner.RecomputeStats(loaded));
        Assert.False(miner.Has(Capability.Mining));
    }
}
