using System.Text.Json.Nodes;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 7: Güteklassen beim Schiffskauf (<see cref="ShipQualityRoll"/>), Dienst „upgrade“ und NPC-<see cref="UpgradeSystem"/> (gemeinsame Regel
/// <see cref="ShipUpgrade"/>, Mitleidszähler, Slot-Upgrades je Klasse begrenzt), Save v10 mit Migration.
/// </summary>
public class QualityUpgradeTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Standardwelt ohne Gütewurf der Gründerschiffe (alle Standard, wie vor Phase 7) — für Tests, die die Einheitsflotte voraussetzen.</summary>
    internal static Universe WithoutFounderQuality(ulong? seed = null)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Rules!.FounderQuality = false;
        return WorldBuilder.Build(def, seed);
    }

    internal static readonly string[] Phase7Modules = { "reactor_mk2", "chem_engine_s_mk2" };

    /// <summary>Entfernt Module samt Waren, Rezepten, Lagerposten, Marktwaren, Fracht und Handelsaufträgen aus einem Save (für Demotionen).</summary>
    internal static void RemoveModules(JsonObject node, IReadOnlyCollection<string> keys)
    {
        var goods = node["Goods"]!.AsArray();
        var removed = goods.Where(g => keys.Contains((string)g!["Key"]!)).Select(g => (int)g!["Id"]!).ToHashSet();
        foreach (var g in goods.ToList()) if (removed.Contains((int)g!["Id"]!)) goods.Remove(g);
        bool Uses(JsonNode r) => r["In"]!.AsObject().Concat(r["Out"]!.AsObject()).Any(kv => removed.Contains(int.Parse(kv.Key)));
        foreach (var t in node["StationTypes"]!.AsArray())
        {
            var recipes = t!["Recipes"]!.AsArray();
            foreach (var r in recipes.ToList()) if (Uses(r!)) recipes.Remove(r);
            if (t["Stocks"] is JsonArray st)
                foreach (var x in st.ToList()) if (removed.Contains((int)x!["Good"]!)) st.Remove(x);
        }
        foreach (var s in node["Stations"]!.AsArray())
        {
            var recipes = s!["Recipes"]!.AsArray();
            foreach (var r in recipes.ToList()) if (Uses(r!)) recipes.Remove(r);
            foreach (var id in removed) s["Market"]!.AsObject().Remove(id.ToString());
        }
        var modules = node["Modules"]!.AsArray();
        foreach (var m in modules.ToList()) if (keys.Contains((string)m!["Key"]!)) modules.Remove(m);
        foreach (var sh in node["Ships"]!.AsArray())
        {
            foreach (var id in removed) sh!["Cargo"]!.AsObject().Remove(id.ToString());
            if (sh!["Order"] is JsonObject o && (string)o["Kind"]! == "trade" && removed.Contains((int)o["C"]!)) { sh.AsObject()["Order"] = null; sh["Phase"] = "Idle"; }
        }
    }

    /// <summary>
    /// Demotet einen aktuellen Save (v10) auf v9: Güteklasse, Mitleidszähler, Verbesserungen der Module, Slotgrenzen der Klassen, die Mk2-Module
    /// (Reaktor, Triebwerk S), der Dienst „upgrade“ und die Phase-7-Regeln entfallen; Schiffe verlieren ihre individuelle Slotliste und ihren Fit
    /// (beim Laden aus dem Modell gefüllt, wie in alten Ständen).
    /// </summary>
    internal static string ToV9Json(string v10Json)
    {
        var node = JsonNode.Parse(v10Json)!.AsObject();
        node["Version"] = 9;
        RemoveModules(node, Phase7Modules);
        foreach (var m in node["Modules"]!.AsArray()) { m!.AsObject().Remove("UpgradeTo"); m.AsObject().Remove("UpgradeCost"); }
        foreach (var c in node["ShipClasses"]!.AsArray()) c!.AsObject().Remove("MaxSlotUpgrades");
        foreach (var sh in node["Ships"]!.AsArray())
        {
            var o = sh!.AsObject();
            o.Remove("Quality"); o.Remove("UpgradePity");
            if (o["ModelKey"] is not null) { o.Remove("Slots"); o.Remove("Fit"); }
        }
        foreach (var t in node["StationTypes"]!.AsArray())
            if (t!["Services"] is JsonArray sv) foreach (var x in sv.ToList()) if ((string)x! == "upgrade") sv.Remove(x);
        foreach (var key in new[] { "FounderQuality", "QualityChances", "UpgradeChances", "UpgradePityStep", "SlotUpgradeChance", "UpgradeLabor",
                     "SlotUpgradeCost", "BonusSlotCost", "UpgradeMinCredits", "UpgradeBudgetShare" })
            node["Rules"]!.AsObject().Remove(key);
        return node.ToJsonString();
    }

    // ───────── Helfer ─────────

    static readonly UpgradeService Upgrades = new();
    static readonly ShipDealerService Dealer = new();

    /// <summary>Standardwelt nach einem Tick, Spieler an der Werft angedockt, reichlich Geld, Werftlager gut gefüllt.</summary>
    internal static (Universe u, Station yard, Ship player) AtYard(float credits = 100_000f, bool fill = true)
    {
        var u = WithoutFounderQuality();
        new SimulationRunner().Run(u, 1);
        var yard = u.Stations.Values.First(s => s.TypeKey == "shipyard");
        var player = u.Ships[u.PlayerShip];
        u.MoveShip(player, yard.Sector);
        u.PlayerSector = yard.Sector;
        player.Position = yard.DockPoint(player.Id);
        u.PlayerWallet.Credits = credits;
        if (fill)
            foreach (var g in u.Goods.Values.Where(g => u.Modules.ContainsKey(g.Key) || g.Key.StartsWith("ship_kit") || g.Key is "electronics" or "platinum" or "titanium" or "hull_plates"))
            {
                var e = yard.Stock(g.Id);
                e.Amount = Math.Max(e.Amount, 10f);
                if (e.Price <= 0f) e.Price = g.BasePrice;
            }
        return (u, yard, player);
    }

    static ServiceResult Exec(IStationService svc, Universe u, Station st, Ship ship, string offer) =>
        svc.Execute(u, st, ship, new ServiceRequest(offer, default, 0f));

    static float Stock(Universe u, Station st, string key) => st.Market.TryGetValue(u.FindGood(key)!.Id, out var e) ? e.Amount : 0f;

    static int Slot(Ship ship, string type) => ship.Slots.FindIndex(s => s.Type == type);

    static string Snapshot(Universe u, Station st, Ship ship) =>
        $"{UniverseHash.Compute(u)}|{string.Join(",", ship.Fit)}|{ship.Slots.Count}|{u.PlayerWallet.Credits}|{st.Credits}|"
        + string.Join(";", ship.UpgradePity.OrderBy(k => k.Key).Select(kv => $"{kv.Key}:{kv.Value}")) + "|"
        + string.Join(";", st.Market.OrderBy(k => k.Key.Value).Select(kv => $"{kv.Key.Value}:{kv.Value.Amount}"));

    // ───────── Güteklassen ─────────

    [Fact]
    public void Gueteklassen_Wahrscheinlichkeiten_UeberVieleWuerfe_FesterSeed()
    {
        var rules = new EconomyRules();
        var rng = new DeterministicRng(4242);
        var count = new int[4];
        const int n = 40_000;
        for (int i = 0; i < n; i++) count[(int)ShipQualityRoll.RollGrade(rng, rules.QualityChances)]++;
        Assert.Equal(new[] { 0.70f, 0.20f, 0.08f, 0.02f }, rules.QualityChances);
        for (int g = 0; g < 4; g++)
            Assert.InRange(count[g] / (float)n, rules.QualityChances[g] - 0.01f, rules.QualityChances[g] + 0.01f);
        // gleicher Seed → gleiche Folge
        var a = new DeterministicRng(7); var b = new DeterministicRng(7);
        for (int i = 0; i < 100; i++) Assert.Equal(ShipQualityRoll.RollGrade(a, rules.QualityChances), ShipQualityRoll.RollGrade(b, rules.QualityChances));
    }

    [Fact]
    public void Gruenderschiffe_WuerfelnOhneMaterial_Deterministisch()
    {
        var u = DemoUniverse.Create();
        var plain = WithoutFounderQuality();
        var better = u.Ships.Values.Where(s => s.Quality != ShipQuality.Standard).ToList();
        Assert.NotEmpty(better);
        Assert.True(better.Count < u.Ships.Count / 2, $"{better.Count} von {u.Ships.Count}");
        Assert.Equal(ShipQuality.Standard, u.Ships[u.PlayerShip].Quality);   // der Spieler wählt sein Startpaket, er würfelt nicht
        foreach (var s in better)
        {
            var model = u.ShipModels[s.ModelKey];
            var cls = u.ShipClasses[model.ClassKey];
            Assert.Null(FitRules.Check(u.SlotTypes, u.Modules, cls, s.Slots, s.Fit));
            Assert.True(!s.Fit.SequenceEqual(model.Fit) || s.Slots.Count > cls.Slots.Count || s.Slots.Where((x, i) => i < cls.Slots.Count && x.Size > cls.Slots[i].Size).Any());
            Assert.Equal(s.Stats.Cargo, s.CargoCapacity);
            Assert.Equal(s.Stats.FuelCapacity, s.TotalFuel);
        }
        // kein Material: Lager aller Stationen wie in der Welt ohne Gütewurf
        foreach (var st in u.Stations.Values)
            Assert.Equal(plain.Stations[st.Id].Market.ToDictionary(k => k.Key, k => k.Value.Amount), st.Market.ToDictionary(k => k.Key, k => k.Value.Amount));
        // gleicher Seed → gleiche Güteklassen und Fits
        var again = DemoUniverse.Create();
        foreach (var s in u.Ships.Values)
        {
            Assert.Equal(s.Quality, again.Ships[s.Id].Quality);
            Assert.Equal(s.Fit, again.Ships[s.Id].Fit);
        }
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(again));
    }

    [Fact]
    public void Kauf_Verbessert_NimmtDasBessereModulAusDemWerftlager_PreisBleibt()
    {
        var (u, yard, player) = AtYard();
        u.Rules.QualityChances = new List<float> { 0f, 1f, 0f, 0f };   // immer „Verbessert“
        var quote = ShipDealerService.Quote(u, yard, player, "unit_trader_100")!;
        float kit = Stock(u, yard, "ship_kit_light"), r1 = Stock(u, yard, "reactor_mk1"), r2 = Stock(u, yard, "reactor_mk2"),
              e1 = Stock(u, yard, "chem_engine_s"), e2 = Stock(u, yard, "chem_engine_s_mk2"), wallet = u.PlayerWallet.Credits;
        var rolled = new List<ShipQualityRolled>();
        u.Events.Raised += e => { if (e is ShipQualityRolled q) rolled.Add(q); };
        var r = Exec(Dealer, u, yard, player, "buy:unit_trader_100");
        Assert.True(r.Success, r.Message);
        Assert.Contains("Verbessert", r.Message);
        var ship = u.Ships[u.PlayerShip];
        Assert.Equal(ShipQuality.Improved, ship.Quality);
        Assert.Equal(ShipQuality.Improved, rolled.Single().Grade);
        Assert.Equal(wallet - quote.Price, u.PlayerWallet.Credits, 1);              // Mehrkosten trägt die Werft
        Assert.Equal(kit - 1f, Stock(u, yard, "ship_kit_light"));
        bool reactorUp = ship.Fit.Contains("reactor_mk2"), engineUp = ship.Fit.Contains("chem_engine_s_mk2");
        Assert.True(reactorUp ^ engineUp);                                           // genau ein Modul besser
        if (reactorUp) { Assert.Equal(r2 - 1f, Stock(u, yard, "reactor_mk2")); Assert.Equal(r1, Stock(u, yard, "reactor_mk1")); Assert.Equal(e1 - 1f, Stock(u, yard, "chem_engine_s")); }
        else { Assert.Equal(e2 - 1f, Stock(u, yard, "chem_engine_s_mk2")); Assert.Equal(e1, Stock(u, yard, "chem_engine_s")); Assert.Equal(r1 - 1f, Stock(u, yard, "reactor_mk1")); }
    }

    [Fact]
    public void Kauf_Meisterwerk_MitVollemLager_BonusslotUndBessereModule_OhneMaterialRueckfall()
    {
        var (u, yard, player) = AtYard();
        u.Rules.QualityChances = new List<float> { 0f, 0f, 0f, 1f };   // immer „Meisterwerk“
        var cargo = Stock(u, yard, "cargo_universal_100");
        Assert.True(Exec(Dealer, u, yard, player, "buy:unit_trader_100").Success);
        var ship = u.Ships[u.PlayerShip];
        var cls = u.ShipClasses["unit_trader"];
        Assert.Equal(ShipQuality.Masterwork, ship.Quality);
        Assert.Equal(cls.Slots.Count + 1, ship.Slots.Count);                         // Bonusslot (Kopie des Frachtslots)
        Assert.Equal("cargo_universal_100", ship.Fit[^1]);
        Assert.Equal(200f, ship.Stats.Cargo);
        Assert.Contains("reactor_mk2", ship.Fit);
        Assert.Contains("chem_engine_s_mk2", ship.Fit);
        Assert.Equal(cargo - 2f, Stock(u, yard, "cargo_universal_100"));              // Grundmodul + Bonusmodul
        Assert.Equal(1, ShipUpgrade.SlotUpgradesUsed(cls, ship.Slots));

        // ohne bessere Module und Bonusmodul im Lager fällt der Wurf bis auf Standard zurück
        var (u2, yard2, player2) = AtYard();
        u2.Rules.QualityChances = new List<float> { 0f, 0f, 0f, 1f };
        foreach (var k in new[] { "reactor_mk2", "chem_engine_s_mk2" }) yard2.Stock(u2.FindGood(k)!.Id).Amount = 0f;
        yard2.Stock(u2.FindGood("cargo_universal_100")!.Id).Amount = 1f;               // nur das Grundmodul
        foreach (var k in u2.Rules.SlotUpgradeCost.Keys) yard2.Stock(u2.FindGood(k)!.Id).Amount = 0f;
        var rolled = new List<ShipQualityRolled>();
        u2.Events.Raised += e => { if (e is ShipQualityRolled q) rolled.Add(q); };
        Assert.True(Exec(Dealer, u2, yard2, player2, "buy:unit_trader_100").Success);
        Assert.Equal(ShipQuality.Standard, u2.Ships[u2.PlayerShip].Quality);
        Assert.Equal(new ShipQualityRolled(u2.PlayerShip, ShipQuality.Masterwork, ShipQuality.Standard), rolled.Single());
        Assert.Equal(u2.ShipModels["unit_trader_100"].Fit, u2.Ships[u2.PlayerShip].Fit);
    }

    [Fact]
    public void KiNeubau_WuerfeltGuete_VerbrauchtBessereModuleAusDemLager()
    {
        var (u, yard, _) = AtYard();
        u.Rules.QualityChances = new List<float> { 0f, 1f, 0f, 0f };
        u.Rules.MaxMiners = 0;
        var buyer = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        if (buyer.Transit is not null) u.FinishTransit(buyer);
        buyer.Credits = 50_000f;
        foreach (var s in u.Ships.Values.Where(s => s.Role == ShipRole.Trader && s != buyer)) s.Credits = 0f;
        float r2 = Stock(u, yard, "reactor_mk2"), e2 = Stock(u, yard, "chem_engine_s_mk2");
        var rolled = new List<ShipQualityRolled>();
        u.Events.Raised += e => { if (e is ShipQualityRolled q) rolled.Add(q); };
        new FleetSystem().Step(u, 20);
        var q = rolled.Single();
        Assert.Equal(ShipQuality.Improved, q.Grade);
        var ship = u.Ships[q.Ship];
        Assert.Equal(ShipQuality.Improved, ship.Quality);
        int better = ship.Fit.Count(k => k is not null && u.Modules[k].Tier == 2 && !u.ShipModels[ship.ModelKey].Fit.Contains(k));
        Assert.Equal(1, better);
        Assert.Equal(r2 + e2 - 1f, Stock(u, yard, "reactor_mk2") + Stock(u, yard, "chem_engine_s_mk2"));
    }

    // ───────── Dienst „upgrade“ ─────────

    [Fact]
    public void Upgrade_Erfolg_TauschtModul_VerbrauchtMaterialUndCredits()
    {
        var (u, yard, player) = AtYard();
        u.Rules.UpgradeChances = new List<float> { 1f };
        int engine = Slot(player, "engine");
        var offer = Upgrades.Offers(u, yard, player).Single(o => o.Key == $"module:{engine}");
        Assert.Equal(1f, offer.Amount);
        var q = ShipUpgrade.Quote(u, yard, player, offer.Key)!;
        Assert.Equal(1f, q.Chance);
        float speed = player.Stats.Speed, wallet = u.PlayerWallet.Credits, yardCredits = yard.Credits;
        float elec = Stock(u, yard, "electronics"), ti = Stock(u, yard, "titanium");
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        var r = Exec(Upgrades, u, yard, player, offer.Key);
        Assert.True(r.Success && r.Units == 1f, r.Message);
        Assert.Equal("chem_engine_s_mk2", player.Fit[engine]);
        Assert.True(player.Stats.Speed > speed);
        Assert.Equal(wallet - q.Price, u.PlayerWallet.Credits, 1);
        Assert.Equal(yardCredits + q.Price, yard.Credits, 1);
        Assert.Equal(elec - 1f, Stock(u, yard, "electronics"));
        Assert.Equal(ti - 1f, Stock(u, yard, "titanium"));
        Assert.True(events.OfType<UpgradeAttempted>().Single().Success);
        Assert.Single(events.OfType<ServicePerformed>());
        Assert.Empty(player.UpgradePity);
        // Mk2 hat keine weitere Stufe: kein Modul-Angebot mehr für diesen Slot
        Assert.DoesNotContain(Upgrades.Offers(u, yard, player), o => o.Key == $"module:{engine}");
    }

    [Fact]
    public void Upgrade_Mitleidszaehler_GarantiertErfolg_MaterialJeVersuchVerbraucht()
    {
        var (u, yard, player) = AtYard();
        u.Rules.UpgradeChances = new List<float> { 0f };   // ohne Mitleid nie
        u.Rules.UpgradePityStep = 1f;                       // ein Fehlschlag macht den nächsten Versuch sicher
        int reactor = Slot(player, "reactor");
        string key = $"module:{reactor}";
        float plat = Stock(u, yard, "platinum"), wallet = u.PlayerWallet.Credits;
        float price = ShipUpgrade.Quote(u, yard, player, key)!.Price;
        Assert.Equal(0f, ShipUpgrade.Quote(u, yard, player, key)!.Chance);
        var fail = Exec(Upgrades, u, yard, player, key);
        Assert.True(fail.Success && fail.Units == 0f, fail.Message);   // ausgeführt, aber misslungen
        Assert.Equal("reactor_mk1", player.Fit[reactor]);              // altes Modul bleibt
        Assert.Equal(1, player.UpgradePity[reactor]);
        Assert.Equal(plat - 1f, Stock(u, yard, "platinum"));          // Material trotzdem verbraucht
        Assert.Equal(wallet - price, u.PlayerWallet.Credits, 1);       // und bezahlt
        Assert.Equal(1f, ShipUpgrade.Quote(u, yard, player, key)!.Chance);
        var ok = Exec(Upgrades, u, yard, player, key);
        Assert.Equal(1f, ok.Units);
        Assert.Equal("reactor_mk2", player.Fit[reactor]);
        Assert.False(player.UpgradePity.ContainsKey(reactor));       // Erfolg setzt den Zähler zurück
        Assert.Equal(plat - 2f, Stock(u, yard, "platinum"));

        // Standardregeln: jeder Fehlschlag +10 Prozentpunkte — nach höchstens 9 Fehlschlägen ist auch ein Slot-Upgrade (15 %) sicher
        var (u2, yard2, p2) = AtYard();
        int engine = Slot(p2, "engine");
        for (int k = 0; k <= 9; k++)
        {
            p2.UpgradePity[engine] = k;
            p2.UpgradePity[ShipUpgrade.BonusPityKey] = k;
            Assert.Equal(MathF.Min(1f, 0.9f + 0.1f * k), ShipUpgrade.Quote(u2, yard2, p2, $"module:{engine}")!.Chance, 4);
            Assert.Equal(MathF.Min(1f, 0.15f + 0.1f * k), ShipUpgrade.Quote(u2, yard2, p2, "bonus:cargo")!.Chance, 4);
        }
        Assert.Equal(1f, ShipUpgrade.Quote(u2, yard2, p2, "bonus:cargo")!.Chance);
        // Stufenchancen laut Regel: Mk1→Mk2 90 %, Mk2→Mk3 60 %, Mk3→Mk4 30 %
        var rules = new EconomyRules();
        Assert.Equal(0.9f, ShipUpgrade.BaseChance(rules, 1));
        Assert.Equal(0.6f, ShipUpgrade.BaseChance(rules, 2));
        Assert.Equal(0.3f, ShipUpgrade.BaseChance(rules, 3));
        Assert.Equal(0.3f, ShipUpgrade.BaseChance(rules, 5));
    }

    [Fact]
    public void Upgrade_SlotUpgrade_IstBegrenztJeKlasse_BonusslotNutzbar()
    {
        var (u, yard, player) = AtYard();
        u.Rules.SlotUpgradeChance = 1f;
        var cls = u.ShipClasses["unit_miner"];
        Assert.Equal(1, cls.MaxSlotUpgrades);
        Assert.Equal(0.15f, new EconomyRules().SlotUpgradeChance);
        int slots = player.Slots.Count;
        var r = Exec(Upgrades, u, yard, player, "bonus:cargo");
        Assert.True(r.Success && r.Units == 1f, r.Message);
        Assert.Equal(slots + 1, player.Slots.Count);
        Assert.Equal(player.Slots.Count, player.Fit.Count);
        Assert.Null(player.Fit[^1]);
        Assert.Equal("cargo", player.Slots[^1].Type);
        // Grenze erreicht: weitere Slot-Upgrades nennen den Grund und werden ohne Zustandsänderung abgelehnt
        Assert.All(Upgrades.Offers(u, yard, player).Where(o => o.Key.StartsWith("bonus:") || o.Key.StartsWith("slot:")), o => Assert.Equal(0f, o.Amount));
        var before = Snapshot(u, yard, player);
        int events = 0;
        u.Events.Raised += _ => events++;
        var deny = Exec(Upgrades, u, yard, player, $"slot:{Slot(player, "tool")}");
        Assert.False(deny.Success);
        Assert.Contains("ausgeschöpft", deny.Message);
        Assert.Equal(before, Snapshot(u, yard, player));
        Assert.Equal(0, events);
        // der Bonusslot nimmt über den Ausrüster ein Frachtmodul auf
        var fit = new OutfitterService().Execute(u, yard, player, new ServiceRequest($"fit:{player.Slots.Count - 1}:cargo_universal_60", default, 0f));
        Assert.True(fit.Success, fit.Message);
        Assert.Equal(160f, player.Stats.Cargo);
    }

    [Fact]
    public void Upgrade_UngueltigeAnfragen_AendernNichts()
    {
        var (u, yard, player) = AtYard(credits: 1f);
        int events = 0;
        u.Events.Raised += _ => events++;
        var before = Snapshot(u, yard, player);
        Assert.False(Exec(Upgrades, u, yard, player, $"module:{Slot(player, "reactor")}").Success);   // zu wenig Credits
        Assert.False(Exec(Upgrades, u, yard, player, "module:99").Success);
        Assert.False(Exec(Upgrades, u, yard, player, "module").Success);
        Assert.False(Exec(Upgrades, u, yard, player, "bonus:reactor").Success);                     // Pflichtslotart: kein Bonusslot
        u.PlayerWallet.Credits = 100_000f;
        before = Snapshot(u, yard, player);
        yard.Stock(u.FindGood("platinum")!.Id).Amount = 0f;
        before = Snapshot(u, yard, player);
        var r = Exec(Upgrades, u, yard, player, $"module:{Slot(player, "reactor")}");
        Assert.False(r.Success);
        Assert.Contains("fehlt", r.Message);
        Assert.Equal(before, Snapshot(u, yard, player));
        Assert.Equal(0, events);
    }

    // ───────── NPC ─────────

    [Fact]
    public void UpgradeSystem_ReichesUntaetigesNpcSchiffAnDerWerft_VerbessertSeinTriebwerk()
    {
        var (u, yard, _) = AtYard();
        u.Rules.UpgradeChances = new List<float> { 1f };
        var npc = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        if (npc.Transit is not null) u.FinishTransit(npc);
        u.MoveShip(npc, yard.Sector);
        npc.Position = yard.DockPoint(npc.Id);
        npc.Order = null; npc.Phase = TradePhase.Idle;
        npc.Credits = 20_000f;
        var attempts = new List<UpgradeAttempted>();
        u.Events.Raised += e => { if (e is UpgradeAttempted a) attempts.Add(a); };
        float speed = npc.Stats.Speed;
        new UpgradeSystem().Step(u, 1);
        var a = attempts.Single();
        Assert.Equal(npc.Id, a.Ship);
        Assert.Equal(UpgradeKind.Module, a.Kind);
        Assert.Equal("chem_engine_s_mk2", a.To);                     // Händler: Tempo zählt, der Reaktor bringt ihm nichts
        Assert.True(npc.Stats.Speed > speed);
        Assert.True(npc.Credits < 20_000f);
        // arme oder beschäftigte Schiffe versuchen nichts, der Spieler nie
        npc.Credits = 100f;
        new UpgradeSystem().Step(u, 1);
        Assert.Single(attempts);
        Assert.DoesNotContain(attempts, x => x.Ship == u.PlayerShip);
    }

    // ───────── Save ─────────

    [Fact]
    public void SaveRoundtrip_GueteBonusslotMitleid_BitIdentisch_UndDeterministisch()
    {
        var (u, yard, player) = AtYard();
        u.Rules.SlotUpgradeChance = 1f;
        Assert.True(Exec(Upgrades, u, yard, player, "bonus:cargo").Success);
        u.Rules.UpgradeChances = new List<float> { 0f };
        Exec(Upgrades, u, yard, player, $"module:{Slot(player, "reactor")}");   // misslingt: Mitleidszähler 1
        player.Quality = ShipQuality.Elite;
        u.Rules.UpgradeChances = new List<float> { 0.9f, 0.6f, 0.3f };
        new SimulationRunner().Run(u, 500);

        var json = SaveGameJson.Write(u, Now);
        Assert.Contains("\"Version\":10", json);
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        var lp = loaded.Ships[player.Id];
        Assert.Equal(ShipQuality.Elite, lp.Quality);
        Assert.Equal(player.Slots.Count, lp.Slots.Count);
        Assert.Equal(1, lp.UpgradePity[Slot(lp, "reactor")]);
        Assert.Equal("reactor_mk2", loaded.Modules["reactor_mk1"].UpgradeTo);
        Assert.Equal(u.Ships.Values.Select(s => s.Quality), loaded.Ships.Values.Select(s => s.Quality));
        new SimulationRunner().Run(u, 3000);
        new SimulationRunner().Run(loaded, 3000);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Migration_V9ToV10_ErgaenztModuleVerbesserungenDienstUndSlotgrenzen()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 300);
        var v9 = ToV9Json(SaveGameJson.Write(u, Now));
        Assert.Contains("\"Version\":9", v9);
        Assert.DoesNotContain("reactor_mk2", v9);
        Assert.DoesNotContain("\"upgrade\"", v9);
        var (m, _) = SaveGameJson.Read(v9);

        foreach (var k in Phase7Modules) { Assert.True(m.Modules.ContainsKey(k), k); Assert.NotNull(m.FindGood(k)); }
        Assert.Equal("reactor_mk2", m.Modules["reactor_mk1"].UpgradeTo);
        Assert.Equal("mining_laser_2", m.Modules["mining_laser_1"].UpgradeTo);
        Assert.Equal(2, m.ShipClasses["heavy_freighter"].MaxSlotUpgrades);
        Assert.Equal(1, m.ShipClasses["unit_trader"].MaxSlotUpgrades);
        Assert.Contains("upgrade", m.StationTypes["shipyard"].Services);
        var drive = m.Stations.Values.Single(s => s.TypeKey == "drive_works");
        Assert.Contains(drive.Production, r => r.Outputs.ContainsKey(m.FindGood("reactor_mk2")!.Id));
        var yard = m.Stations.Values.First(s => s.TypeKey == "shipyard");
        Assert.True(yard.Market.ContainsKey(m.FindGood("chem_engine_s_mk2")!.Id));
        Assert.All(m.Ships.Values, s => Assert.Equal(ShipQuality.Standard, s.Quality));
        Assert.All(m.Ships.Values.Where(s => s.HasModel), s => Assert.Equal(m.ShipModels[s.ModelKey].Fit, s.Fit));
        Assert.Contains(StationServices.Default().ForStation(m, yard), s => s.Key == UpgradeService.ServiceKey);

        int trades = 0;
        m.Events.Raised += e => { if (e is TradeCompleted) trades++; };
        new SimulationRunner().Run(m, 3000);
        Assert.True(trades > 20, $"nur {trades} Handelsfahrten");
    }
}
