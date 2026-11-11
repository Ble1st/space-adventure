using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>Phase 3a: Slot-/Modul-Content (slotTypes, modules, shipClasses, shipModels), Warenform und ShipStats — reine Daten und Berechnung, noch ohne Verbraucher.</summary>
public class SlotModelTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    static WorldDef DefaultDef() => WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());

    /// <summary>Wirft die erwartete WorldFormatException (mit Namen) für eine Mutation der Default-Welt.</summary>
    static WorldFormatException Invalid(Action<WorldDef> mutate, string enthaelt)
    {
        var def = DefaultDef();
        mutate(def);
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
        Assert.Contains(enthaelt, ex.Message);
        return ex;
    }

    /// <summary>ShipStats eines Modells der geladenen Welt (Klasse + Fit auflösen).</summary>
    static ShipStats StatsOf(Universe u, string modelKey)
    {
        var model = u.ShipModels[modelKey];
        var cls = u.ShipClasses[model.ClassKey];
        var fit = model.Fit.Select(k => k is null ? null : u.Modules[k]).ToList();
        return ShipStats.Compute(cls, fit);
    }

    [Fact]
    public void GueltigeDefaultWelt_Laedt_SlotContentInRegistern()
    {
        var u = DemoUniverse.Create();
        Assert.Equal(9, u.SlotTypes.Count);
        Assert.Equal(22, u.Modules.Count);   // Phase 13: + Reaktor Mk3, Mining-Laser Mk3; Phase 11: + Laserkanone; Phase 8: + Bauausleger, Spaltlaser; Phase 5a: erster Modulsatz Mk1 (+ Speicherbank, Tank, Traktor, Schild, Panzerung); Phase 6: + Triebwerk M, Laser Mk2; Phase 7: + Reaktor Mk2, Triebwerk S Mk2
        Assert.Equal(13, u.ShipClasses.Count);   // Phase 11: + Patrouillenboot, Kaperschiff; Phase 8: + Bauschiff, Asteroidenbrecher; Phase 5c: + Skiff; Phase 6: + Kurier, leichter/schwerer Frachter, Bergbauer leicht/schwer, Tanker
        Assert.Equal(22, u.ShipModels.Count);   // Phase 13: + Excavator; Phase 11: + Wächter, Korsar; Phase 8: + Foreman, Quarry, Rockbreaker II; Phase 5c: + Pip, Nomad, Prospector; Phase 6: + Swift, Mule, Hauler, Tinker, Behemoth, Pickaxe, Rockbreaker, Cistern
        Assert.True(u.SlotTypes["reactor"].Required);
        Assert.True(u.SlotTypes["engine"].Required);
        Assert.False(u.SlotTypes["cargo"].Required);
        Assert.Equal(SlotSize.S, u.Modules["reactor_mk1"].Size);
        Assert.Equal(10f, u.Modules["reactor_mk1"].Stats["powerOutput"]);
        Assert.Equal(ModuleHolds.Any, u.Modules["cargo_universal_100"].Holds);
        Assert.Equal(100f, u.Modules["cargo_universal_100"].Stats["capacity"]);
        Assert.Contains(Capability.Mining, u.Modules["mining_laser_1"].Grants);
        Assert.Contains(Capability.Cargo, u.Modules["cargo_universal_100"].Grants);
        Assert.Equal(4, u.ShipModels["unit_trader_100"].Fit.Count);
        Assert.Null(u.ShipModels["unit_trader_100"].Fit[2]);   // leerer Tool-Slot erlaubt
        Assert.Equal("unit_trader", u.ShipModels["unit_trader_100"].ClassKey);
    }

    [Fact]
    public void AltJson_OhneDieNeuenAbschnitte_Laedt_LeereRegistern()
    {
        const string altJson = """
        {
         "seed": 7,
         "goods": [ { "key": "steel", "name": "Stahl", "basePrice": 10 } ],
         "factions": [ { "id": 1, "name": "A" } ],
         "sectors": [ { "id": 1, "name": "S1", "owner": 1, "map": [ 0, 0 ], "asteroids": 0 } ],
         "player": { "faction": 1, "sector": 1, "credits": 100 }
        }
        """;
        var u = WorldBuilder.Build(WorldBuilder.Parse(altJson));
        Assert.Empty(u.SlotTypes);
        Assert.Empty(u.Modules);
        Assert.Empty(u.ShipClasses);
        Assert.Empty(u.ShipModels);
        Assert.Equal(GoodForm.Solid, u.FindGood("steel")!.Form);   // fehlendes form-Feld = solid
        Assert.NotNull(u.Ships[u.PlayerShip]);
    }

    [Fact]
    public void DoppelteSchluessel_WerfenWorldFormatExceptionMitNamen()
    {
        Invalid(d => d.SlotTypes[1].Key = "reactor", "Slotart 'reactor' doppelt");
        Invalid(d => d.Modules[1].Key = "reactor_mk1", "Modul 'reactor_mk1' doppelt");
        Invalid(d => d.ShipClasses[1].Key = "unit_trader", "Schiffsklasse 'unit_trader' doppelt");
        Invalid(d => d.ShipModels[3].Key = "unit_trader_100", "Schiffsmodell 'unit_trader_100' doppelt");
    }

    [Fact]
    public void UnbekannteVerweise_Groessen_ImFit_WerfenMitNamen()
    {
        Invalid(d => d.Modules[0].Slot = "warp", "Modul 'reactor_mk1': unbekannte Slotart 'warp'");
        Invalid(d => d.ShipClasses[0].Slots[0].Type = "warp", "Schiffsklasse 'unit_trader': unbekannte Slotart 'warp'");
        Invalid(d => d.Modules[0].Size = "XXL", "Modul 'reactor_mk1': unbekannte Größe 'XXL'");
        Invalid(d => d.ShipClasses[0].Size = "Q", "Schiffsklasse 'unit_trader': unbekannte Größe 'Q'");
        Invalid(d => d.ShipClasses[0].Slots[0].Size = "XXL", "Schiffsklasse 'unit_trader': unbekannte Größe 'XXL'");
        Invalid(d => d.ShipModels[0].Class = "lorem", "Schiffsmodell 'unit_trader_100': unbekannte Klasse 'lorem'");
        Invalid(d => d.ShipModels[0].Fit[0] = "lorem", "Schiffsmodell 'unit_trader_100': unbekanntes Modul 'lorem'");
    }

    [Fact]
    public void UnbekannteWarenInCost_UnbekannterGrant_UnbekannteFrachtform_WerfenMitModulnamen()
    {
        Invalid(d => d.Modules[0].Cost["unobtainium"] = 1, "Modul 'reactor_mk1': unbekannte Ware 'unobtainium' in cost");
        Invalid(d => d.Modules[2].Grants.Add("Fly"), "Modul 'mining_laser_1': unbekannte Fähigkeit 'Fly'");
        Invalid(d => d.Modules[3].Holds = "plasma", "Modul 'cargo_universal_100': unbekannte Frachtform 'plasma'");
    }

    [Fact]
    public void ModulPasstNichtZumSlot_WirftMitModellUndModulname()
    {
        Invalid(d => d.Modules[3].Size = "XL", "Schiffsmodell 'unit_trader_100': Modul 'cargo_universal_100' (XL) ist größer als der Slot (S)");
        Invalid(d => d.Modules[2].Slot = "cargo", "Schiffsmodell 'unit_miner_100': Modul 'mining_laser_1' (Slotart 'cargo') passt nicht in Slot 'tool'");
    }

    [Fact]
    public void FitLaengeFalsch_oderPflichtslotLeer_WirftMitModellname()
    {
        Invalid(d => d.ShipModels[0].Fit.Add("reactor_mk1"), "Schiffsmodell 'unit_trader_100': Fit hat 5 Einträge, Klasse 'unit_trader' hat 4 Slots");
        Invalid(d => d.ShipModels[0].Fit[0] = null, "Schiffsmodell 'unit_trader_100': Pflichtslot 'reactor' ist leer");
        Invalid(d => d.ShipModels[0].Fit[1] = null, "Schiffsmodell 'unit_trader_100': Pflichtslot 'engine' ist leer");
    }

    [Fact]
    public void Leistungsbilanz_Unterdeckung_WirftMitModellname()
    {
        Invalid(d => d.Modules[0].Stats["powerOutput"] = 2, "Schiffsmodell 'unit_trader_100': powerOutput 2 < powerDraw 3");
        Invalid(d => d.Modules[1].PeakDraw = 100, "Schiffsmodell 'unit_trader_100': Spitzenlast 100 übersteigt die Speicherbank");
    }

    /// <summary>Kleine handgebaute Welt für die Leistungsbilanz-Randfälle und den Tempo-Clamp.</summary>
    static WorldDef KleineWelt()
    {
        var def = new WorldDef { Seed = 1 };
        def.Goods.Add(new GoodDef { Key = "steel", Name = "Stahl", BasePrice = 10 });
        def.SlotTypes.Add(new SlotTypeDef { Key = "reactor", Name = "Reaktor", Required = true });
        def.SlotTypes.Add(new SlotTypeDef { Key = "storage", Name = "Speicher" });
        def.SlotTypes.Add(new SlotTypeDef { Key = "engine", Name = "Antrieb", Required = true });
        def.Modules.Add(new ModuleDef { Key = "r1", Slot = "reactor", Size = "S", Stats = new() { ["powerOutput"] = 10 }, Cost = new() { ["steel"] = 1 } });
        def.Modules.Add(new ModuleDef { Key = "bank", Slot = "storage", Size = "S", Stats = new() { ["powerStorage"] = 18 } });
        def.Modules.Add(new ModuleDef { Key = "e1", Slot = "engine", Size = "S", PowerDraw = 3, PeakDraw = 25 });
        def.ShipClasses.Add(new ShipClassDef
        {
            Key = "c1", Name = "C1", Size = "S", BaseHull = 100, BaseMass = 10,
            Slots = new()
            {
                new ClassSlotDef { Type = "reactor", Size = "S" },
                new ClassSlotDef { Type = "storage", Size = "S" },
                new ClassSlotDef { Type = "engine", Size = "S" },
            },
        });
        def.ShipModels.Add(new ShipModelDef { Key = "m1", Class = "c1", Name = "M1", Fit = new() { "r1", "bank", "e1" } });
        def.Factions.Add(new FactionDef { Id = 1, Name = "A" });
        def.Sectors.Add(new SectorDef { Id = 1, Name = "S1", Owner = 1, Asteroids = 0 });
        return def;
    }

    [Fact]
    public void Leistungsbilanz_Randfaelle_GleichstandIstZulaessig()
    {
        // Spitzenlast 25, Reaktorüberschuss 10-3=7 → Überschuss 18; Speicherbank 18 darf genau reichen
        var u = WorldBuilder.Build(KleineWelt());
        Assert.NotNull(u.ShipModels["m1"]);

        // eine Einheit weniger Speicher → ungültig
        var def = KleineWelt();
        def.Modules[1].Stats["powerStorage"] = 17;
        Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));

        // powerOutput == powerDraw ist zulässig (kein Puffer nötig ohne Spitzenlast)
        def = KleineWelt();
        def.Modules[0].Stats["powerOutput"] = 3;
        def.Modules[2].PeakDraw = 0;
        WorldBuilder.Build(def);

        // powerOutput < powerDraw → ungültig
        def = KleineWelt();
        def.Modules[0].Stats["powerOutput"] = 2;
        def.Modules[2].PeakDraw = 0;
        Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
    }

    [Fact]
    public void Einheitsmodelle_ReproduzierenDasBisherigeVerhalten()
    {
        var u = DemoUniverse.Create();
        var faelle = new[]
        {
            ("unit_trader_100", ShipRole.Trader, 100f),
            ("unit_trader_60", ShipRole.Trader, 60f),
            ("unit_miner_100", ShipRole.Miner, 100f),
            ("unit_miner_60", ShipRole.Miner, 60f),
            ("unit_player", ShipRole.Player, 100f),
        };
        foreach (var (key, role, cargo) in faelle)
        {
            var st = StatsOf(u, key);
            Assert.Equal(cargo, st.Cargo);                                    // Laderaum == bisheriges ships[].cargo
            Assert.Equal(100f, st.Hull);                                      // Hülle == bisher 100 (Ship.MaxHull ohne Modell)
            Assert.Equal(Travel.ShipSpeed, st.Speed);                         // Tempo == bisher 8
            Assert.Equal(CapabilityDefaults.ForRole(role), st.Capabilities);  // Fähigkeiten == Ableitung aus der Rolle
            Assert.Equal(10f, st.PowerOutput);
            Assert.True(st.PowerOutput >= st.PowerDraw, $"{key}: Leistungsbilanz");
        }
        // Miner haben den Laser, Trader nicht
        Assert.Equal(1f, StatsOf(u, "unit_miner_60").LaserChunkChanceMult);
        Assert.Equal(1f, StatsOf(u, "unit_miner_60").LaserChunkSizeMult);
        Assert.Equal(Capability.Cargo | Capability.Mining, StatsOf(u, "unit_miner_60").Capabilities);
        Assert.Equal(Capability.Cargo, StatsOf(u, "unit_trader_100").Capabilities);
    }

    [Fact]
    public void ShipStats_ReineFunktion_TempoClampUndLaserStufe()
    {
        var cls = new ShipClass { Key = "c", Name = "C", BaseHull = 80, BaseMass = 10 };
        var reactor = new Module { Key = "r", Slot = "reactor", Stats = new() { ["powerOutput"] = 10 } };
        var engine = new Module { Key = "e", Slot = "engine", PowerDraw = 3, Stats = new() { ["thrust"] = 10_000 }, Mass = 5 };
        var laser = new Module { Key = "l", Slot = "tool", Grants = new() { Capability.Mining }, Stats = new() { ["chunkChanceMult"] = 1.5f, ["chunkSizeMult"] = 2f } };
        var cargo = new Module { Key = "h", Slot = "cargo", Grants = new() { Capability.Cargo }, Stats = new() { ["capacity"] = 60, ["armor"] = 20 } };

        // Schub 10000 / Masse 15 → über Maximum → begrenzt
        var st = ShipStats.Compute(cls, new List<Module?> { reactor, engine, laser, cargo });
        Assert.Equal(ShipStats.MaxSpeed, st.Speed);
        Assert.Equal(100f, st.Hull);                 // baseHull 80 + Panzerung 20
        Assert.Equal(60f, st.Cargo);
        Assert.Equal(15f, st.Mass);
        Assert.Equal(Capability.Cargo | Capability.Mining, st.Capabilities);
        Assert.Equal(1.5f, st.LaserChunkChanceMult);
        Assert.Equal(2f, st.LaserChunkSizeMult);

        // ohne Mining-Modul: Laserstufe 1/1
        st = ShipStats.Compute(cls, new List<Module?> { reactor, engine, null, cargo });
        Assert.Equal(1f, st.LaserChunkChanceMult);
        Assert.Equal(1f, st.LaserChunkSizeMult);
        Assert.Equal(Capability.Cargo, st.Capabilities);
    }

    [Fact]
    public void Warenform_WasserUndWasserstoffSindLiquid_AlleAnderenSolid()
    {
        var u = DemoUniverse.Create();
        Assert.Equal(GoodForm.Liquid, u.FindGood("water")!.Form);
        Assert.Equal(GoodForm.Liquid, u.FindGood("hydrogen")!.Form);
        Assert.All(u.Goods.Values.Where(g => g.Key is not ("water" or "hydrogen" or "propellant")), g => Assert.Equal(GoodForm.Solid, g.Form));

        Invalid(d => d.Goods.First(g => g.Key == "water").Form = "gas", "Ware 'water': unbekannte Form 'gas'");
    }

    [Fact]
    public void SaveLoad_Roundtrip_V4_AltSavesLadbar_SlotContentWirdMitgespeichert()
    {
        Assert.Equal(16, SaveGame.CurrentVersion);   // v16: Forschung (Phase 13); v15: Befehle/Verbände/Hangar/Leihschiff (Phase 12); v14: Polizei/Fahndung/Piraten (Phase 11); v13: Gate-Zugang/Sektorkontrolle (Phase 10); v12: Akteure (Phase 9); v11: Bau/Baustellen/Lagerhaus (Phase 8); v10: Güteklassen/Upgrades (Phase 7); v9: Multirole (Phase 6); v8: Ausrüster/Schiffskauf (Phase 5c); v7: Spielerkasse (Phase 5b); v6: Modul-Waren/Bausatzstufen/stocks (Phase 5a); v5: zusätzlich Fuel je Schiff sowie fuel/baseFuel im Slot-Content (v4: ModelKey/Slots/Fit plus Slot-Content-Sektionen)
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 100);
        var before = UniverseHash.Compute(u);

        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));

        Assert.Equal(before, UniverseHash.Compute(loaded));
        Assert.Single(loaded.Ships.Values, s => s.IsPlayer);
        Assert.NotEmpty(loaded.ShipModels);
    }
}
