using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 29 (Schiffsraster, docs/SCHIFFE.md): Raster aus Größe × Rolle, Skiffs als Standardmodelle, kein „Einheitsspieler“, Kampfmodule in Stufen und M-Größe,
/// gemeinsame Kampfmodellwahl für Polizei, Marine und Piraten; jedes Modell für jede Fraktion, die Piraten und den Spieler kaufbar.
/// </summary>
public class Phase29Tests
{
    static readonly string[] NewModules = { "shield_mk2", "shield_m", "armor_plating_s_mk2", "armor_plating_m", "weapon_laser_s_mk2", "weapon_laser_m",
        "weapon_massdriver_s_mk2", "missile_launcher_s_mk2", "power_bank_s_mk2", "point_defense_s_mk2", "reactor_m",
        "drill_head_s", "cutting_wheel_s", "cutting_wheel_m", "cutting_drum_l" };

    static readonly string[] LateShips = { "corvette", "missile_boat", "bomber", "gunboat", "destroyer", "battlecruiser", "strip_miner" };

    /// <summary>Phase 29 zurückdrehen (für die Referenz-Hash-Tests über <see cref="Phase28Tests.RevertPhase28"/>).</summary>
    /// <summary>Runde 24 zurückdrehen: Startbestände der Werftregale wie vorher; Befehlsstufe ohne Abschlag für eigene Schiffe.</summary>
    internal static void RevertRunde24(WorldDef def)
    {
        Runde44ScaleTests.RevertRunde44(def);   // Runde 44: Maßstäbe und Stellschrauben (die Referenz-Hashes stammen von davor)
        def.Rules!.CommandShipWeight = def.Rules.ActorShipWeight;
        Runde38Tests.RevertRunde38(def);   // Runde 38: Spieler wieder Mitglied der Startfraktion (die Referenz-Hashes stammen von davor)
        var yard = def.StationTypes.First(t => t.Key == "shipyard");
        foreach (var (good, amount, target) in new[] { ("cargo_universal_60", 2f, 5f), ("cargo_universal_100", 2f, 5f), ("chem_engine_m", 1f, 2f), ("reactor_mk2", 1f, 2f),
            ("missile_launcher_s", 1f, 2f), ("shield_m", 0f, 1f), ("armor_plating_m", 0f, 1f), ("reactor_m", 0f, 1f), ("weapon_laser_m", 0f, 1f), ("reactor_mk3", 0f, 1f),
            ("cutting_wheel_m", 0f, 1f), ("cutting_drum_l", 0f, 1f) })
            if (yard.Stocks!.ContainsKey(good)) yard.Stocks[good] = new[] { amount, target };
    }

    internal static void RevertPhase29(WorldDef def)
    {
        RevertRunde24(def);
        def.Rules!.PoliceChoosesModels = false;
        def.Rules.WarshipChoosesModels = false;
        def.Rules.PirateChoosesModels = false;
        def.Rules.RaidersSpareLoot = false;
        RevertRunde12(def);
        RevertPhase30(def);
        def.Player.Model = "unit_player";
        int at = def.ShipModels.FindIndex(m => m.Key == "unit_miner_60") + 1;
        def.ShipModels.Insert(at, new ShipModelDef { Key = "unit_player", Class = "unit_miner", Name = "Einheitsspieler",
            Fit = new() { "reactor_mk1", "chem_engine_s", "mining_laser_1", "cargo_universal_100" } });
        foreach (var key in new[] { "pip", "nomad", "prospector" })
        {
            var m = def.ShipModels.First(x => x.Key == key);
            m.Jobs = null;
            m.Fit.RemoveAt(m.Fit.Count - 1);
        }
        // Kontaktabbau zurück auf die Laser-Fits (Phase 29 H)
        var oldFits = new Dictionary<string, (int Slot, string? Module)[]>
        {
            ["unit_miner_100"] = new[] { (2, (string?)"mining_laser_1") }, ["unit_miner_60"] = new[] { (2, (string?)"mining_laser_1") },
            ["prospector"] = new[] { (2, (string?)"mining_laser_1") }, ["pickaxe"] = new[] { (2, (string?)"mining_laser_1") },
            ["rockbreaker"] = new[] { (2, (string?)"mining_laser_2") }, ["rockbreaker_ii"] = new[] { (3, (string?)"mining_laser_2") },
            ["quarry"] = new[] { (0, (string?)"reactor_mk1"), (3, (string?)null) }, ["excavator"] = new[] { (2, (string?)"mining_laser_3") },
        };
        foreach (var (key, changes) in oldFits)
            if (def.ShipModels.FirstOrDefault(x => x.Key == key) is { } fm) foreach (var (slot, module) in changes) fm.Fit[slot] = module;
        foreach (var sl in def.ShipClasses.First(c => c.Key == "miner_heavy").Slots) if (sl.Type == "tool") sl.Size = "S";
        var skiff = def.ShipClasses.First(c => c.Key == "skiff");
        skiff.Slots.RemoveAt(skiff.Slots.Count - 1);
        skiff.BaseMass = 6f;
        def.ShipClasses.First(c => c.Key == "raider").Restricted = true;
        foreach (var sl in def.ShipClasses.First(c => c.Key == "patrol_craft").Slots) if (sl.Type != "engine") sl.Size = "S";
        var frigate = def.ShipClasses.FirstOrDefault(c => c.Key == "frigate");
        if (frigate is not null) foreach (var sl in frigate.Slots) if (sl.Type != "engine") sl.Size = "S";
        def.ShipModels.RemoveAll(m => LateShips.Contains(m.Key));
        def.ShipClasses.RemoveAll(c => LateShips.Contains(c.Key));
        def.Modules.RemoveAll(m => NewModules.Contains(m.Key));
        foreach (var m in def.Modules) if (m.Upgrade is { } up && NewModules.Contains(up.To)) m.Upgrade = null;
        foreach (var t in def.StationTypes)
        {
            if (t.ModuleFactory is { } mf0) { mf0.InputTarget = 10; t.Stocks?.Remove("platinum"); }   // Runde 11: Zutatenziel der Modulwerke 20, Platin eigens bei 10
            if (t.Key == "shipyard" && t.Stocks is not null)   // Runde 11: Werftregale starten voll
                foreach (var (good, amount, target) in new[] { ("reactor_mk1", 2f, 5f), ("chem_engine_s", 2f, 5f), ("mining_laser_1", 2f, 5f), ("weapon_laser_s", 2f, 4f) })
                    if (t.Stocks.ContainsKey(good)) t.Stocks[good] = new[] { amount, target };
            if (t.ModuleFactory is { } mf && t.Key == "weapons_works") mf.MaxTier = 1;
            if (t.Stocks is not null) foreach (var k in NewModules) t.Stocks.Remove(k);
        }
        var names = new Dictionary<string, string> { ["nomad"] = "Nomad", ["swift"] = "Swift", ["mule"] = "Mule", ["hauler"] = "Hauler", ["cistern"] = "Cistern",
            ["behemoth"] = "Behemoth", ["prospector"] = "Prospector", ["pip"] = "Pip", ["pickaxe"] = "Pickaxe", ["rockbreaker"] = "Rockbreaker",
            ["rockbreaker_ii"] = "Rockbreaker II", ["quarry"] = "Quarry", ["excavator"] = "Excavator", ["foreman"] = "Foreman", ["tinker"] = "Tinker" };
        foreach (var m in def.ShipModels) if (names.TryGetValue(m.Key, out var n)) m.Name = n;
    }

    /// <summary>Phase 30 zurückdrehen: alle Stationen mittelgroß (Radius 15, Kostenfaktor 1), keine Kleinstationen, alte Positionen, keine Abwehr/Bau der Stellungen.</summary>
    /// <summary>Phase 31 zurückdrehen: Torkampf aus, keine Torklassen (Tore ohne Kampfwerte).</summary>
    internal static void RevertPhase31(WorldDef def)
    {
        def.Rules!.GateCombat = false;
        def.GateTypes.Clear();
        foreach (var g in def.Gates) g.Type = null;
    }

    internal static void RevertPhase30(WorldDef def)
    {
        RevertPhase31(def);
        def.Rules!.OutpostsEngage = false;
        def.Rules.OutpostsBuild = false;
        var small = def.StationTypes.Where(t => t.Government == "outpost").Select(t => t.Key).ToHashSet();
        def.StationTypes.RemoveAll(t => small.Contains(t.Key));
        foreach (var t in def.StationTypes) { t.Size = null; t.Radius = null; }
        if (def.StationTypes.FirstOrDefault(t => t.Key == "colony") is { } colony) colony.Income = 20f;   // Runde 13: Kolonie-Einkommen 20 → 30
        foreach (var sec in def.Sectors)
        {
            sec.Stations.RemoveAll(st => small.Contains(st.Type));
            foreach (var st in sec.Stations)
            {
                if (st.Id == 32) st.Pos = new[] { 130f, -25f };   // Hauptquartier Alpha
                if (st.Id == 13) st.Pos = new[] { -60f, -5f };    // Elektrolyse Gamma
                if (st.Id == 18) st.Pos = new[] { 5f, 65f };      // Werft Delta
            }
        }
    }

    /// <summary>Runde 12 zurückdrehen: mehr Geschützstellungen (Wächter-Turm, Leviathan, Sektor-HQ, Hauptquartier, Versteck), Verfolgung vor Versteckgeschützen.</summary>
    internal static void RevertRunde12(WorldDef def)
    {
        def.Rules!.PursuitStopsAtHideoutGuns = false;
        var pc = def.ShipClasses.First(c => c.Key == "patrol_craft");
        if (pc.Slots.Count > 7) pc.Slots.RemoveRange(7, pc.Slots.Count - 7);
        var warden = def.ShipModels.First(m => m.Key == "warden");
        if (warden.Fit.Count > 7) warden.Fit.RemoveRange(7, warden.Fit.Count - 7);
        warden.Fit[0] = "reactor_mk2";
        if (def.ShipClasses.FirstOrDefault(c => c.Key == "battlecruiser") is { } bc && bc.Slots.Count > 18) bc.Slots.RemoveRange(18, bc.Slots.Count - 18);
        if (def.ShipModels.FirstOrDefault(m => m.Key == "battlecruiser") is { } lev)
        {
            if (lev.Fit.Count > 18) lev.Fit.RemoveRange(18, lev.Fit.Count - 18);
            lev.Fit[16] = null;
        }
        foreach (var t in def.StationTypes)
        {
            if (t.Defense is null) continue;
            if (t.Key == "pirate_hideout") t.Defense = null;
            else if (t.Key == "sector_hq" && t.Defense.Guns.Count > 2) t.Defense.Guns.RemoveRange(2, t.Defense.Guns.Count - 2);
            else if (t.Key == "headquarters" && t.Defense.Guns.Count > 3) t.Defense.Guns.RemoveRange(3, t.Defense.Guns.Count - 3);
        }
    }

    static Universe Standard(ulong? seed = null, string? start = null) => TestWorlds.Default(seed, fuel: false, research: false, startOption: start);

    static ShipStats StatsOf(Universe u, ShipModel m) =>
        ShipStats.Compute(u.ShipClasses[m.ClassKey], m.Fit.Select(k => k is null ? null : u.Modules[k]).ToList());

    [Fact]
    public void Raster_JedeRolleUndGroesseBesetzt_KeineDoppeltenKernwerte_KeinEinheitsspieler()
    {
        var u = Standard();
        Assert.False(u.ShipModels.ContainsKey("unit_player"));
        Assert.Equal("unit_miner_100", u.Ships[u.PlayerShip].ModelKey);   // Startschiff ohne Startpaket
        var cells = new HashSet<string>();
        var cores = new Dictionary<string, string>();
        foreach (var m in u.ShipModels.Values)
        {
            var cls = u.ShipClasses[m.ClassKey];
            var st = StatsOf(u, m);
            Assert.Null(FitRules.Check(u.SlotTypes, u.Modules, cls, cls.Slots, m.Fit));   // Energiebilanz und Slots gültig
            string role = CombatModels.IsCombat(st) ? "Kampf" : m.Jobs.Contains("build") ? "Bau" : m.Jobs.Contains("trade") && m.Jobs.Contains("mine") ? "Mehrzweck"
                : m.Jobs.Contains("trade") ? "Handel" : m.Jobs.Contains("gas") ? "Gasernte" : (st.Capabilities & Capability.Mining) != 0 || st.SplitPower > 0f ? "Bergbau" : "?";
            Assert.True(role != "?", $"{m.Key}: keine Rolle");
            cells.Add($"{role}/{cls.Size}");
            string core = $"{st.Cargo}|{st.CargoSolid}|{st.CargoLiquid}|{st.Speed:0.0}|{st.Hull}|{st.WeaponPower:0.0}|{st.Shield}|{st.Armor}|{st.LaserChunkChanceMult}|{st.SplitPower}|{st.ContactMining}";
            Assert.False(cores.TryGetValue(core, out var other), $"{m.Key} hat dieselben Kernwerte wie {other}");
            cores[core] = m.Key;
            Assert.False(cls.Restricted, $"{m.Key}: Klasse gesperrt");
        }
        foreach (var need in new[] { "Handel/S", "Handel/M", "Handel/L", "Bergbau/S", "Bergbau/M", "Bergbau/L", "Mehrzweck/S", "Mehrzweck/M", "Kampf/S", "Kampf/M", "Kampf/L", "Kampf/XL", "Bau/M" })
            Assert.Contains(need, cells);
        Assert.Equal(31, u.ShipModels.Count);   // 4 Einheitsmodelle + 26 benannte; Gasernte: + Gasschöpfer
        // Skiffs sind Standardmodelle; jeder Händler bewaffnet
        Assert.Equal(new[] { "mine", "trade" }, u.ShipModels["pip"].Jobs.OrderBy(j => j));
        Assert.Contains("trade", u.ShipModels["nomad"].Jobs);
        Assert.Contains("mine", u.ShipModels["prospector"].Jobs);
        foreach (var m in u.ShipModels.Values.Where(m => m.Jobs.Contains("trade"))) Assert.True(StatsOf(u, m).WeaponPower > 0f, $"{m.Key} unbewaffnet");
        // Startpakete ohne Einheitsspieler
        Assert.DoesNotContain(WorldBuilder.DefaultStartOptions(), o => o.Model == "unit_player");
    }

    [Fact]
    public void JedesModell_FuerJedeFraktionPiratenUndSpielerKaufbar()
    {
        var u = Standard();
        new SimulationRunner().Run(u, 2);
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        var me = u.Ships[u.PlayerShip];
        foreach (var m in u.ShipModels.Values)
        {
            Assert.True(ShipPurchase.SellsModel(u, yard, m), $"Werft verkauft {m.Key} nicht");
            Assert.NotNull(ShipPurchase.Materials(u, m));
        }
        // Spieler: jedes Modell erscheint im Händlerangebot der Werft
        var offers = new SpaceSim.Core.Services.ShipDealerService().Offers(u, yard, me).Select(o => o.Key).ToHashSet();
        foreach (var m in u.ShipModels.Values) Assert.Contains($"buy:{m.Key}", offers);
        // Polizei/Marine/Piraten: jedes Kampfmodell ist Kandidat (auch Fregatte); die Piraten bauen jedes Kampfmodell im Versteck, wenn Geld und Platz reichen
        var combat = CombatModels.All(u).Select(c => c.Model.Key).ToList();
        Assert.Contains("bastion", combat); Assert.Contains("corsair", combat); Assert.Contains("corvette", combat); Assert.Contains("missile_boat", combat);
        foreach (var (model, cls, _) in CombatModels.All(u)) Assert.True(CombatModels.PirateCost(u, model, cls) > 0f, model.Key);
    }

    /// <summary>Klassenleiter: ohne Lage das beste Verhältnis Stärke je Preis, mit voller Lage das beste Stärke² je Preis (größere Klassen); knappe Kasse begrenzt.</summary>
    [Theory]
    [InlineData(0f, 1e7f)]
    [InlineData(1f, 1e7f)]
    [InlineData(1f, 9000f)]
    public void Kampfmodellwahl_AnDerWerft_NachLageUndKasse(float heavy, float budget)
    {
        var u = Standard();
        new SimulationRunner().Run(u, 2);
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        foreach (var g in u.Goods.Values) if (yard.Market.TryGetValue(g.Id, out var e) && g.Category == ModuleGoods.ModuleCategory) e.Amount = MathF.Max(e.Amount, 8f);
        foreach (var k in new[] { "ship_kit_light", "ship_kit_medium", "ship_kit_heavy" }) yard.Market[u.FindGood(k)!.Id].Amount = 6f;
        var pick = CombatModels.AtYard(u, yard.Sector, budget, heavy);
        Assert.NotNull(pick);
        Assert.True(pick!.Value.Price <= budget);
        float Price(ShipModel m) => ShipPurchase.Price(u, yard, ShipPurchase.Materials(u, m)!, ShipPurchase.AssemblyOf(u, yard));
        float best = CombatModels.All(u).Where(c => ShipPurchase.Missing(yard, ShipPurchase.Materials(u, c.Model)!).Count == 0 && Price(c.Model) <= budget)
            .Max(c => MathF.Pow(CombatModels.Strength(c.Stats), 1f + heavy) / Price(c.Model));
        Assert.Equal(best, MathF.Pow(CombatModels.Strength(StatsOf(u, pick.Value.Model)), 1f + heavy) / pick.Value.Price, 3);
        if (heavy >= 1f && budget > 1e6f)
            Assert.True(pick.Value.Class.Size >= SlotSize.L, $"volle Lage, volle Kasse: großes Schiff ({pick.Value.Model.Key})");
    }

    [Theory]
    [InlineData(0f, 2f, 1500f, null)]          // zu wenig Geld: nichts
    [InlineData(0f, 2f, 3000f, "corsair")]     // Korsar ≈ 2000
    [InlineData(1f, 1f, 50000f, "")]           // eine Einheit frei: nur ein S-Schiff (bei voller Lage der Bomber)
    [InlineData(0f, 0.5f, 50000f, null)]       // Gruppe voll
    public void Piraten_BauenNachKasseUndKapazitaet(float heavy, float free, float budget, string? expect)
    {
        var u = Standard();
        var pick = CombatModels.ForPirates(u, budget, free, heavy);
        if (expect == "") Assert.Equal(SlotSize.S, pick!.Value.Class.Size);
        else Assert.Equal(expect, pick?.Model.Key);
        if (pick is { } p) Assert.True(CombatModels.Units(p.Class) <= free);
        Assert.InRange(CombatModels.PirateCost(u, u.ShipModels["corsair"], u.ShipClasses["raider"]), 1800f, 2200f);
        // die Fregatte zählt zwei Einheiten = die ganze Gruppe
        Assert.Equal(2f, CombatModels.Units(u.ShipClasses["frigate"]));
    }

    [Fact]
    public void Polizei_KauftUeberDieModellwahl_PiratenBauenImVersteck()
    {
        var u = Standard();
        var ev = new List<SimEvent>();
        u.Events.Raised += ev.Add;
        // Polizei: Kasse voll, Werftlager voll — gekauft wird ein Kampfmodell (nicht zwingend der Wächter)
        var f = new FactionId(1);
        u.Actors[f].Treasury.Credits = 500_000f;
        foreach (var yard in u.StationsById().Where(s => s.TypeKey == "shipyard"))
            foreach (var g in u.Goods.Values) if (yard.Market.TryGetValue(g.Id, out var e) && (g.Category == ModuleGoods.ModuleCategory || g.Key.StartsWith("ship_kit"))) e.Amount = MathF.Max(e.Amount, 8f);
        new SimulationRunner().Run(u, 2);
        var ship = SecuritySystem.Commission(u, f, priority: true);
        Assert.NotNull(ship);
        Assert.True(CombatModels.IsCombat(StatsOf(u, u.ShipModels[ship!.ModelKey])));
        Assert.Equal(ShipRole.Police, ship.Role);
        // Piraten: ohne Kaperschiffe und mit Geld baut die Gruppe im Versteck
        var outlaw = u.Actors.Values.First(a => a.Kind == ActorKind.Outlaw);
        foreach (var r in u.ShipsById().Where(s => ThreatMap.IsRaider(s) && s.Owner == outlaw.Id).ToList()) Departures.RemoveShip(u, r, DepartureReason.Destroyed, "test");
        outlaw.Treasury.Credits = 9000f;
        outlaw.LastForceTick = -100_000;
        new SimulationRunner().Run(u, 60);
        Assert.Contains(ev, e => e is RaiderBuilt rb && rb.Actor == outlaw.Id);
        float units = u.ShipsById().Where(s => ThreatMap.IsRaider(s) && s.Owner == outlaw.Id)
            .Sum(s => CombatModels.Units(u.ShipClasses[u.ShipModels[s.ModelKey].ClassKey]));
        Assert.True(units <= u.Rules.MaxRaiderUnitsPerActor + 1e-3f, $"Gruppe {units} Einheiten");
    }

    [Fact]
    public void Modulstufen_RezepteUpgradesUndMSlots()
    {
        var u = Standard();
        foreach (var k in NewModules)
        {
            Assert.True(u.Modules.ContainsKey(k), k);
            var good = u.FindGood(k);
            Assert.NotNull(good);
            Assert.Contains(u.StationTypes.Values, t => t.Recipes.Any(r => r.Outputs.ContainsKey(good!.Id)));   // ein Werk stellt es her
            Assert.Contains(u.StationTypes.Values, t => t.Key == "shipyard" && t.Stocks.Any(s => s.Good == good!.Id));   // kleines Werftregal
        }
        foreach (var (from, to) in new[] { ("shield_mk1", "shield_mk2"), ("armor_plating_s", "armor_plating_s_mk2"), ("weapon_laser_s", "weapon_laser_s_mk2"),
            ("weapon_massdriver_s", "weapon_massdriver_s_mk2"), ("missile_launcher_s", "missile_launcher_s_mk2"), ("power_bank_s", "power_bank_s_mk2"), ("point_defense_s", "point_defense_s_mk2") })
            Assert.Equal(to, u.Modules[from].UpgradeTo);
        Assert.True(u.Modules["shield_mk2"].Stat("shieldCapacity") > u.Modules["shield_mk1"].Stat("shieldCapacity"));
        // M-Schild passt in den M-Slot des Wächters, nicht in einen S-Slot; kleinere Module passen weiter
        var warden = u.ShipModels["warden"];
        var cls = u.ShipClasses[warden.ClassKey];
        int shield = cls.Slots.FindIndex(s => s.Type == "shield");
        var fit = new List<string?>(warden.Fit) { [shield] = "shield_m" };
        Assert.Null(FitRules.Check(u.SlotTypes, u.Modules, cls, cls.Slots, fit));
        var falcon = u.ShipModels["falcon"];
        var fcls = u.ShipClasses[falcon.ClassKey];
        var ffit = new List<string?>(falcon.Fit) { [fcls.Slots.FindIndex(s => s.Type == "shield")] = "shield_m" };
        Assert.NotNull(FitRules.Check(u.SlotTypes, u.Modules, fcls, fcls.Slots, ffit));
        // Fregatte trägt stärkere, nicht nur mehr Schilde
        var frigate = u.ShipClasses["frigate"];
        Assert.Contains(frigate.Slots, s => s.Type == "shield" && s.Size == SlotSize.M);
        Assert.Contains(frigate.Slots, s => s.Type == "weapon" && s.Size == SlotSize.M);
    }

    [Fact]
    public void Ausruester_SchildMk2_UeberDenUpgradeDienst()
    {
        var u = Standard(start: "fighter");
        new SimulationRunner().Run(u, 2);
        var me = u.Ships[u.PlayerShip];
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        int slot = me.Fit.IndexOf("shield_mk1");
        var quote = ShipUpgrade.Quotes(u, yard, me).FirstOrDefault(q => q.Key == $"module:{slot}");
        Assert.NotNull(quote);
        Assert.Equal("shield_mk2", quote!.ToModule);
    }
}
