using System.Text.Json.Nodes;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 5a: Module sind Waren (automatisch hinter den JSON-Waren), drei Modulfabriken mit Rezepten = Modul-cost, Lager ohne
/// Rezept (stocks), Bausatzstufen je Schiffsklasse, Migration v5→v6, Determinismus.
/// </summary>
public class ModuleGoodsTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    static readonly string[] Factories = { "drive_works", "weapons_works", "systems_works" };

    static WorldDef DefaultDef() => WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());

    static void Invalid(Action<WorldDef> mutate, string enthaelt)
    {
        var def = DefaultDef();
        mutate(def);
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
        Assert.Contains(enthaelt, ex.Message);
    }

    static Good GoodOf(Universe u, string key) => u.FindGood(key) ?? throw new InvalidOperationException(key);

    // ───────── Modul-Waren ─────────

    [Fact]
    public void JedesModul_HatGenauEineWare_HinterDenJsonWaren_NachSchluessel()
    {
        var def = DefaultDef();
        var u = WorldBuilder.Build(def);
        Assert.Equal(25, def.Goods.Count);   // 23 bis Phase 4 + zwei Bausatzstufen
        var keys = u.Modules.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        for (int i = 0; i < keys.Count; i++)
        {
            var g = Assert.Single(u.Goods.Values, x => x.Key == keys[i]);
            Assert.Equal(def.Goods.Count + 1 + i, g.Id.Value);   // feste Reihenfolge: angehängt, nach Modulschlüssel
        }
        Assert.Equal(def.Goods.Count + keys.Count, u.Goods.Count);
        Assert.Equal(DemoUniverse.FirstModuleGoodId, def.Goods.Count + 1);
    }

    [Fact]
    public void ModulWare_Eigenschaften_FormVolumenPreisFarbe()
    {
        var u = DemoUniverse.Create();
        Assert.Equal(1.3f, u.Rules.ModuleMarkup);
        foreach (var m in u.Modules.Values)
        {
            var g = GoodOf(u, m.Key);
            Assert.Equal(GoodForm.Solid, g.Form);
            Assert.Equal(MathF.Max(1f, m.Mass), g.VolumePerUnit);
            float sum = m.Cost.Sum(kv => kv.Value * GoodOf(u, kv.Key).BasePrice);
            Assert.Equal(MathF.Round(sum * u.Rules.ModuleMarkup), g.BasePrice);
            Assert.Equal(u.SlotTypes[m.Slot].Color, g.Color);
            Assert.False(string.IsNullOrEmpty(g.Color));
            Assert.Equal(m.Name, g.Name);
            Assert.NotEqual(m.Key, m.Name);   // jedes Startmodul hat einen Anzeigenamen
        }
        // Beispiel: Reaktor = (4 Stahl × 24 + 2 Elektronik × 90) × 1,3 = 358,8 → 359
        Assert.Equal(359f, GoodOf(u, "reactor_mk1").BasePrice);
    }

    [Fact]
    public void ErsterModulsatz_Vollstaendig_TitanPlatinAlsEngpass()
    {
        var u = DemoUniverse.Create();
        var slots = u.Modules.Values.Select(m => m.Slot).ToHashSet();
        Assert.Superset(new HashSet<string> { "reactor", "storage", "engine", "cargo", "tool", "shield", "armor" }, slots);
        Assert.True(u.Modules["fuel_tank_s"].Stats["fuelCapacity"] > 0f);       // Tank
        Assert.True(u.Modules["tractor_beam_s"].Slot == "tool");                 // Traktor
        // Titan- und Platinketten sind Zutaten der neuen Module (Engpass)
        Assert.Contains(u.Modules.Values, m => m.Cost.ContainsKey("titanium"));
        Assert.Contains(u.Modules.Values, m => m.Cost.ContainsKey("platinum"));
        // die Module der Einheitsmodelle behalten ihre Werte (Startflotte unverändert)
        Assert.Equal(10f, u.Modules["reactor_mk1"].Stats["powerOutput"]);
        Assert.Equal(160f, u.Modules["chem_engine_s"].Stats["thrust"]);
        Assert.Equal(100f, u.Modules["cargo_universal_100"].Stats["capacity"]);
        Assert.All(u.Ships.Values.Where(s => s.HasModel && s.Quality == ShipQuality.Standard && s.Role is not (ShipRole.Builder or ShipRole.Police or ShipRole.Pirate)), s => Assert.Equal(8f, s.Stats.Speed));   // Phase 7: bessere Gründer sind schneller; Phase 8: Bauschiffe (Foreman) sind keine Einheitsmodelle; Phase 11: Polizei/Piraten auch nicht
    }

    [Fact]
    public void ModulSchluessel_GleichEinerJsonWare_Wirft()
        => Invalid(d => d.Goods.Add(new GoodDef { Key = "reactor_mk1", Name = "Doppelt" }), "reactor_mk1");

    // ───────── Fabriken ─────────

    [Fact]
    public void Fabrikrezepte_StimmenMitModulCost_JedesModulGenauEineFabrik()
    {
        var u = DemoUniverse.Create();
        var producedBy = new Dictionary<string, List<string>>();
        foreach (var key in Factories)
        {
            var type = u.StationTypes[key];
            Assert.NotEmpty(type.Recipes);
            foreach (var r in type.Recipes)
            {
                var (outGood, qty) = Assert.Single(r.Outputs);
                Assert.Equal(1f, qty);
                var module = u.Modules[u.Goods[outGood].Key];
                Assert.Equal(module.Cost.Count, r.Inputs.Count);
                foreach (var (k, v) in module.Cost) Assert.Equal(v, r.Inputs[GoodOf(u, k).Id]);
                Assert.True(r.TicksPerCycle > 0);
                producedBy.TryAdd(module.Key, new List<string>());
                producedBy[module.Key].Add(key);
            }
        }
        // jedes Modul mit cost entsteht in genau einer der drei Fabriken; sonst stellt nichts Module her
        foreach (var m in u.Modules.Values.Where(m => m.Cost.Count > 0))
            Assert.Single(producedBy[m.Key]);
        foreach (var t in u.StationTypes.Values.Where(t => !Factories.Contains(t.Key)))
            Assert.DoesNotContain(t.Recipes, r => r.Outputs.Keys.Any(g => u.Modules.ContainsKey(u.Goods[g].Key)));
        // Fachgebiete wie in SCHIFFSSLOTS.md
        Assert.Contains(u.StationTypes["drive_works"].Recipes, r => r.Outputs.ContainsKey(GoodOf(u, "reactor_mk1").Id));
        Assert.Contains(u.StationTypes["weapons_works"].Recipes, r => r.Outputs.ContainsKey(GoodOf(u, "shield_mk1").Id));
        Assert.Contains(u.StationTypes["systems_works"].Recipes, r => r.Outputs.ContainsKey(GoodOf(u, "mining_laser_1").Id));
    }

    [Fact]
    public void Startwelt_JeEineFabrik_MitAbstand_GuertelErreichenZielzahl()
    {
        var u = DemoUniverse.Create();
        foreach (var key in Factories)
        {
            var st = Assert.Single(u.Stations.Values, s => s.TypeKey == key);
            var sector = u.Sectors[st.Sector];
            Assert.All(sector.Fields, f => Assert.True(Vec2.Distance(f.Position, st.Position) > 101f));
            Assert.All(sector.Gates, g => Assert.True(Vec2.Distance(u.Gates[g].Position, st.Position) > 50f));
            Assert.All(sector.Stations.Where(x => x != st.Id), x => Assert.True(Vec2.Distance(u.Stations[x].Position, st.Position) > 50f));
            Assert.True(st.BankruptcyProtected);
            // jede Modul-Ware der Fabrik hat einen Markteintrag mit dem Fabrik-Wunschbestand
            // (Phase 6: die neuen Module nennen in stocks einen kleineren Wunschbestand, damit Fabriken ohne Abnehmer weniger horten)
            foreach (var r in st.Production)
            {
                var good = r.Outputs.Keys.Single();
                Assert.Equal(u.StationTypes[key].Stocks.Single(x => x.Good == good).Target, st.Market[good].Target);
            }
        }
        foreach (var sector in u.Sectors.Values)
            foreach (var f in sector.Fields)
                Assert.Equal(f.TargetAsteroids, SpaceSim.Core.Mining.Belt.AsteroidsOf(sector, f).Count());
    }

    [Fact]
    public void JedesModell_IstBaubar_ModuleUndBausatzHabenErreichbareKette()
    {
        var u = DemoUniverse.Create();
        // erreichbar: Erze aus Asteroiden, dann alles, was ein Rezept aus erreichbaren Zutaten erzeugt (Fixpunkt)
        var reachable = u.AsteroidClasses.Values.SelectMany(c => c.Yields).Select(y => y.Good).ToHashSet();
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var r in u.StationTypes.Values.SelectMany(t => t.Recipes))
                if (r.Outputs.Count > 0 && r.Inputs.Keys.All(reachable.Contains))
                    foreach (var o in r.Outputs.Keys) grew |= reachable.Add(o);
        }
        foreach (var model in u.ShipModels.Values)
        {
            var cls = u.ShipClasses[model.ClassKey];
            foreach (var key in model.Fit.Where(k => k is not null))
                Assert.True(reachable.Contains(GoodOf(u, key!).Id), $"Modell {model.Key}: Modul {key} ist nicht herstellbar");
            var kit = cls.Kit ?? u.Rules.ShipKitGoodKey;
            Assert.True(reachable.Contains(GoodOf(u, kit).Id), $"Modell {model.Key}: Bausatz {kit} ist nicht herstellbar");
        }
        // auch alle übrigen Module und Bausatzstufen
        Assert.All(u.Modules.Keys, k => Assert.Contains(GoodOf(u, k).Id, reachable));
        Assert.All(new[] { "ship_kit_light", "ship_kit_medium", "ship_kit_heavy" }, k => Assert.Contains(GoodOf(u, k).Id, reachable));
    }

    [Fact]
    public void ModuleFactory_Validierung()
    {
        Invalid(d => d.StationTypes.First(t => t.Key == "drive_works").ModuleFactory!.Slots.Add("warp"), "unbekannte Slotart 'warp'");
        Invalid(d => d.StationTypes.First(t => t.Key == "drive_works").ModuleFactory!.Slots = new List<string> { "utility" }, "erzeugt kein Modul");
        Invalid(d => d.StationTypes.First(t => t.Key == "drive_works").ModuleFactory!.Ticks = 0, "drive_works");
    }

    // ───────── stocks ─────────

    [Fact]
    public void Stocks_Validierung_MitTypname()
    {
        Invalid(d => d.StationTypes.First(t => t.Key == "shipyard").Stocks!["unobtainium"] = new[] { 1f, 2f }, "unbekannte Ware 'unobtainium'");
        Invalid(d => d.StationTypes.First(t => t.Key == "shipyard").Stocks!["reactor_mk1"] = new[] { 1f }, "shipyard");
        Invalid(d => d.StationTypes.First(t => t.Key == "shipyard").Stocks!["reactor_mk1"] = new[] { 1f, 0f }, "Wunschbestand > 0");
    }

    [Fact]
    public void Stocks_LegenMarkteintraegeAn_AuchBeimNeubauUeberStationFactory()
    {
        var u = DemoUniverse.Create();
        var yard = u.Stations.Values.Single(s => s.TypeKey == "shipyard");
        var reactor = GoodOf(u, "reactor_mk1").Id;
        Assert.Equal(2f, yard.Market[reactor].Amount);
        Assert.Equal(5f, yard.Market[reactor].Target);
        Assert.True(yard.IsStockOnly(reactor));                         // kein Rezept der Werft nutzt den Reaktor
        Assert.False(yard.IsStockOnly(DemoUniverse.ShipKitLight));      // Rezeptware
        Assert.False(yard.IsStockOnly(DemoUniverse.Food));              // nicht geführt
        // stocks setzt bei Rezeptwaren Anfangs-/Wunschbestand (Titan der Werft, Zutaten der Modulfabrik)
        Assert.Equal(10f, yard.Market[DemoUniverse.Titanium].Target);
        var drive = u.Stations.Values.Single(s => s.TypeKey == "drive_works");
        Assert.Equal(10f, drive.Market[DemoUniverse.Steel].Target);
        Assert.False(drive.IsStockOnly(DemoUniverse.Steel));

        // Lebenszyklus-Neubau nutzt dieselbe Fabrik: neue Werft hat die Lagerposten
        var type = u.StationTypes["shipyard"];
        var built = StationFactory.Create(u, u.NewStationId(), type, "Testwerft", yard.Sector, new Vec2(500, 500), yard.Owner);
        foreach (var s in type.Stocks)
        {
            Assert.Equal(s.Amount, built.Market[s.Good].Amount);
            Assert.Equal(s.Target, built.Market[s.Good].Target);
        }
        Assert.True(built.Market.ContainsKey(reactor));
    }

    [Fact]
    public void Stocks_HaendlerBeliefernLager_KaufenDortAberNicht()
    {
        var u = DemoUniverse.Create();
        var yard = u.Stations.Values.Single(s => s.TypeKey == "shipyard");
        var stocked = u.StationTypes["shipyard"].Stocks.Select(s => s.Good).Where(g => yard.IsStockOnly(g)).ToList();
        Assert.NotEmpty(stocked);
        float before = stocked.Sum(g => yard.Market[g].Amount);
        var fromStockOnly = new List<string>();
        float delivered = 0f;
        u.Events.Raised += e =>
        {
            if (e is not TradeCompleted t) return;
            if (u.Stations.TryGetValue(t.From, out var from) && from.IsStockOnly(t.Good)) fromStockOnly.Add($"{from.Name}: {u.Goods[t.Good].Key}");
            if (t.To == yard.Id && stocked.Contains(t.Good)) delivered += t.Amount;
        };
        new SimulationRunner().Run(u, 8_000);
        Assert.True(delivered > 0f, "kein Händler hat das Werftlager beliefert");
        Assert.True(stocked.Sum(g => yard.Market[g].Amount) > before);
        Assert.Empty(fromStockOnly);   // aus einem Lager ohne Rezept kauft kein KI-Händler
    }

    // ───────── Markt und Lebenszyklus ─────────

    [Fact]
    public void Zahlungsbereitschaft_HoechstesGebotUeberAlleRezepte()
    {
        // Werft: schwere Bausätze auf Halde (billig) dürfen das Gebot für Hüllenplatten (auch Zutat des leichten Bausatzes) nicht drücken
        var u = DemoUniverse.Create();
        var yard = u.Stations.Values.Single(s => s.TypeKey == "shipyard");
        yard.Stock(DemoUniverse.ShipKitHeavy).Amount = 100f;   // Halde → 0,25 × Basispreis
        yard.Stock(DemoUniverse.ShipKitLight).Amount = 10f;    // knapp → teuer
        yard.Stock(DemoUniverse.HullPlates).Amount = 10f;      // knapp → Knappheitspreis hoch
        new MarketSystem().Step(u, 1);
        var light = u.StationTypes["shipyard"].Recipes.Single(r => r.Outputs.ContainsKey(DemoUniverse.ShipKitLight));
        float inBase = light.Inputs.Sum(kv => kv.Value * u.Goods[kv.Key].BasePrice);
        float lightCeiling = yard.Market[DemoUniverse.ShipKitLight].Price * u.Rules.InputWillingness * (u.Goods[DemoUniverse.HullPlates].BasePrice / inBase);
        float scarcity = u.Goods[DemoUniverse.HullPlates].BasePrice * 4f;
        Assert.Equal(MathF.Min(scarcity, lightCeiling), yard.Market[DemoUniverse.HullPlates].Price, 2);
    }

    [Fact]
    public void Lebenszyklus_WareOhneVerbrauchertyp_BlockiertKeinenVerbraucherbau()
    {
        var u = DemoUniverse.Create();
        foreach (var st in u.Stations.Values) foreach (var e in st.Market.Values) e.Amount = e.Target;   // alles ausgeglichen
        foreach (var st in u.Stations.Values.Where(s => s.Market.ContainsKey(DemoUniverse.IronOre))) st.Market[DemoUniverse.IronOre].Amount *= 4f;   // Eisenerz-Überangebot
        var drive = u.Stations.Values.Single(s => s.TypeKey == "drive_works");
        drive.Market[GoodOf(u, "reactor_mk1").Id].Amount = 1000f;   // Modul noch stärker auf Halde, aber kein Typ verbraucht es
        int before = u.Stations.Count;
        string? builtType = null;
        u.Events.Raised += e => { if (e is ConstructionStarted b) builtType = b.TypeKey; };   // Phase 8: Baustelle statt sofortiger Station
        new StationLifecycleSystem().Step(u, 1);
        Assert.Equal(before + 1, u.Stations.Count);
        Assert.NotNull(builtType);
        Assert.Contains(u.StationTypes[builtType!].Recipes, r => r.Inputs.ContainsKey(DemoUniverse.IronOre));
    }

    // ───────── Bausatzstufen ─────────

    [Fact]
    public void Bausatzstufen_KlasseNenntBausatz_FallbackAufRegel()
    {
        var u = DemoUniverse.Create();
        Assert.Equal("ship_kit_light", u.Rules.ShipKitGoodKey);
        // Einheits- und Skiff-Klassen bauen aus dem leichten Bausatz; seit Phase 6 nutzt jede Bausatzstufe mindestens eine Klasse
        Assert.All(new[] { "unit_trader", "unit_miner", "skiff" }, k => Assert.Equal("ship_kit_light", u.ShipClasses[k].Kit));
        Assert.All(new[] { "ship_kit_light", "ship_kit_medium", "ship_kit_heavy" }, kit => Assert.Contains(u.ShipClasses.Values, c => c.Kit == kit));
        var trader = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        Assert.Equal("ship_kit_light", FleetSystem.KitKeyFor(u, trader));
        u.ShipClasses["unit_trader"].Kit = null;
        u.Rules.ShipKitGoodKey = "ship_kit_heavy";
        Assert.Equal("ship_kit_heavy", FleetSystem.KitKeyFor(u, trader));   // Klasse ohne Bausatz → Regel
        var shipyard = u.StationTypes["shipyard"];
        Assert.Contains(shipyard.Recipes, r => r.Outputs.ContainsKey(DemoUniverse.ShipKitMedium) && r.Inputs.ContainsKey(DemoUniverse.Titanium));
        Assert.Contains(shipyard.Recipes, r => r.Outputs.ContainsKey(DemoUniverse.ShipKitHeavy) && r.Inputs.ContainsKey(DemoUniverse.Platinum));
        Invalid(d => d.ShipClasses[0].Kit = "ship_kit_ultra", "ship_kit_ultra");
    }

    [Fact]
    public void FleetSystem_VerbrauchtDenBausatzDerKlasse()
    {
        var u = DemoUniverse.Create();
        u.ShipClasses["unit_trader"].Kit = "ship_kit_medium";
        u.Rules.FleetChoosesModels = false;   // geprüft wird die Kopierregel (Phase 5); die Modellwahl der Phase 6 hat eigene Tests
        var yard = u.Stations.Values.Single(s => s.TypeKey == "shipyard");
        yard.Stock(DemoUniverse.ShipKitLight).Amount = 5f;
        yard.Stock(DemoUniverse.ShipKitMedium).Amount = 3f;
        var rich = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        rich.Credits = 20_000f;
        int before = u.Ships.Count;
        new FleetSystem().Step(u, 20);
        Assert.Equal(before + 1, u.Ships.Count);
        Assert.Equal(2f, yard.Market[DemoUniverse.ShipKitMedium].Amount);   // mittlerer Bausatz verbraucht
        Assert.Equal(5f, yard.Market[DemoUniverse.ShipKitLight].Amount);    // leichter unberührt

        // ohne Bausatz der Klasse kein Schiff, auch wenn andere Stufen lagern
        yard.Stock(DemoUniverse.ShipKitMedium).Amount = 0f;
        rich.Credits = 20_000f;
        new FleetSystem().Step(u, 20);
        Assert.Equal(before + 1, u.Ships.Count);
    }

    // ───────── Persistenz ─────────

    [Fact]
    public void SaveLoad_V6_Roundtrip_ContentUndFortsetzungBitIdentisch()
    {
        Assert.Equal(15, SaveGame.CurrentVersion);   // v15: Befehle/Verbände/Hangar/Leihschiff (Phase 12); v14: Polizei/Fahndung/Piraten (Phase 11); v13: Gate-Zugang/Sektorkontrolle (Phase 10); v12: Akteure (Phase 9); v11: Bau/Baustellen/Lagerhaus (Phase 8); v10: Güteklassen/Upgrades (Phase 7); v9: Multirole (Phase 6); v8 (Phase 5c)
        var u = DemoUniverse.Create();
        var runner = new SimulationRunner();
        runner.Run(u, 3_000);
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        // Content liegt im Save
        Assert.Equal(u.Goods.Count, loaded.Goods.Count);
        Assert.All(u.Goods.Values, g => { var l = loaded.Goods[g.Id]; Assert.Equal(g.Key, l.Key); Assert.Equal(g.BasePrice, l.BasePrice); Assert.Equal(g.Color, l.Color); });
        Assert.Equal(u.StationTypes["shipyard"].Stocks, loaded.StationTypes["shipyard"].Stocks);
        Assert.Equal("ship_kit_light", loaded.ShipClasses["unit_miner"].Kit);
        Assert.Equal("Fusionsreaktor Mk1", loaded.Modules["reactor_mk1"].Name);
        Assert.Equal(u.SlotTypes["shield"].Color, loaded.SlotTypes["shield"].Color);
        Assert.Equal(u.StationTypes["drive_works"].Recipes.Count, loaded.StationTypes["drive_works"].Recipes.Count);

        // Fortsetzung bit-identisch
        runner.Run(u, 3_000);
        runner.Run(loaded, 3_000);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Determinismus_GleicherSeedGleicherHash()
    {
        var a = DemoUniverse.Create(7);
        var b = DemoUniverse.Create(7);
        new SimulationRunner().Run(a, 5_000);
        new SimulationRunner().Run(b, 5_000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }

    [Fact]
    public void Migration_V5ToV6_GoodIdsStabil_VerhaltenWieFrischeWelt()
    {
        var fresh = DemoUniverse.Create();
        var v5 = FuelModelTests.ToV5Json(SaveGameJson.Write(fresh, Now));
        Assert.Contains("\"Version\":5", v5);
        Assert.Contains("\"Key\":\"ship_kit\"", v5);
        Assert.DoesNotContain("ship_kit_light", v5);
        Assert.DoesNotContain("\"Key\":\"reactor_mk1\",\"Name\"", v5);

        var (loaded, _) = SaveGameJson.Read(v5);
        // gleiche Waren unter gleichen Ids wie in der frischen Welt (Umbenennung statt Verschiebung, Anhängen in fester Reihenfolge)
        Assert.Equal(fresh.Goods.Count, loaded.Goods.Count);
        Assert.All(fresh.Goods.Values, g => { Assert.Equal(g.Key, loaded.Goods[g.Id].Key); Assert.Equal(g.BasePrice, loaded.Goods[g.Id].BasePrice); });
        Assert.Equal("ship_kit_light", loaded.Rules.ShipKitGoodKey);
        Assert.Equal("ship_kit_light", loaded.ShipClasses["unit_trader"].Kit);
        Assert.Equal(fresh.StationTypes["shipyard"].Stocks, loaded.StationTypes["shipyard"].Stocks);

        var runner = new SimulationRunner();
        runner.Run(fresh, 2_000);
        runner.Run(loaded, 2_000);
        Assert.Equal(UniverseHash.Compute(fresh), UniverseHash.Compute(loaded));
    }

    /// <summary>Ein v5-Save, wie er vor Phase 5a aussah: ohne Modulfabriken (Typen und Stationen), ohne die neuen Mk1-Module, Werft ohne Bausatzstufen und Lager.</summary>
    static string RealisticV5()
    {
        var node = JsonNode.Parse(FuelModelTests.ToV5Json(SaveGameJson.Write(DemoUniverse.Create(), Now)))!.AsObject();
        var stations = node["Stations"]!.AsArray();
        var removed = new HashSet<int>();
        for (int i = stations.Count - 1; i >= 0; i--)
            if (Factories.Contains((string?)stations[i]!["Type"])) { removed.Add((int)stations[i]!["Id"]!); stations.RemoveAt(i); }
        Assert.Equal(3, removed.Count);
        foreach (var sec in node["Sectors"]!.AsArray())
        {
            var list = sec!["Stations"]!.AsArray();
            for (int i = list.Count - 1; i >= 0; i--) if (removed.Contains((int)list[i]!)) list.RemoveAt(i);
        }
        var types = node["StationTypes"]!.AsArray();
        for (int i = types.Count - 1; i >= 0; i--)
        {
            var t = types[i]!.AsObject();
            if (Factories.Contains((string?)t["Key"])) { types.RemoveAt(i); continue; }
            if ((string?)t["Key"] == "shipyard") { var r = t["Recipes"]!.AsArray(); while (r.Count > 1) r.RemoveAt(r.Count - 1); }
        }
        // Werften: nur das alte Rezept, Märkte nur mit Waren bis Id 23
        foreach (var st in stations)
        {
            if ((string?)st!["Type"] == "shipyard") { var r = st["Recipes"]!.AsArray(); while (r.Count > 1) r.RemoveAt(r.Count - 1); }
            var market = st["Market"]!.AsObject();
            foreach (var k in market.Select(kv => kv.Key).ToList()) if (int.Parse(k) > 23) market.Remove(k);
            if ((string?)st["Type"] == "shipyard") { market.Remove(DemoUniverse.Titanium.Value.ToString()); market.Remove(DemoUniverse.Platinum.Value.ToString()); }
        }
        var newModules = new HashSet<string> { "power_bank_s", "fuel_tank_s", "tractor_beam_s", "shield_mk1", "armor_plating_s" };
        var modules = node["Modules"]!.AsArray();
        for (int i = modules.Count - 1; i >= 0; i--) if (newModules.Contains((string)modules[i]!["Key"]!)) modules.RemoveAt(i);
        node["NextStationId"] = 27;
        return node.ToJsonString();
    }

    [Fact]
    public void Migration_AlterV5Save_BekommtFabrikenWerftstufenUndLager_FlotteBleibtAktiv()
    {
        var (u, _) = SaveGameJson.Read(RealisticV5());
        // Modul-Waren für alle Module (auch die per Upsert nachgereichten), angehängt ab Id 26 nach Schlüssel
        var moduleGoods = u.Goods.Values.Where(g => u.Modules.ContainsKey(g.Key)).OrderBy(g => g.Id.Value).ToList();
        Assert.Equal(u.Modules.Count, moduleGoods.Count);
        Assert.Equal(u.Modules.Keys.OrderBy(k => k, StringComparer.Ordinal), moduleGoods.Select(g => g.Key));
        Assert.Equal(DemoUniverse.FirstModuleGoodId, moduleGoods[0].Id.Value);
        Assert.Equal(DemoUniverse.ShipKitLight, u.FindGood("ship_kit_light")!.Id);
        // drei Fabriken an ihren Plätzen, Rezepte = cost
        foreach (var key in Factories)
        {
            var st = Assert.Single(u.Stations.Values, s => s.TypeKey == key);
            Assert.Contains(st.Id, u.Sectors[st.Sector].Stations);
            Assert.Equal(u.StationTypes[key].Recipes.Count, st.Production.Count);
            foreach (var r in st.Production)
            {
                var m = u.Modules[u.Goods[r.Outputs.Keys.Single()].Key];
                Assert.Equal(m.Cost.Count, r.Inputs.Count);
                Assert.Equal(u.StationTypes[key].Stocks.Single(x => x.Good == r.Outputs.Keys.Single()).Target, st.Market[r.Outputs.Keys.Single()].Target);
            }
        }
        // Werft: Bausatzstufen und Lager nachgetragen
        var yard = u.Stations.Values.Single(s => s.TypeKey == "shipyard");
        Assert.Equal(3, yard.Production.Count);
        Assert.Equal(3, u.StationTypes["shipyard"].Recipes.Count);
        Assert.True(yard.IsStockOnly(u.FindGood("reactor_mk1")!.Id));
        Assert.Equal(5f, yard.Market[DemoUniverse.ShipKitMedium].Target);

        int fleet = u.Ships.Values.Count(s => s.IsEconomyAi);
        int trades = 0;
        u.Events.Raised += e => { if (e is TradeCompleted) trades++; };
        new SimulationRunner().Run(u, 8_000);
        Assert.True(trades > 0);
        Assert.True(u.Ships.Values.Count(s => s.IsEconomyAi) >= fleet);
        var (again, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(again));
    }
}
