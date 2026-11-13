using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 23 (KAMPF.md Abschnitte 3 und 6): Schiffssysteme — Laufzeitzustand (Schild, Speicherbank, Zustand je Slot, Magazin, Leistungsverteilung), die
/// Leistungsregel als reine Funktionen, das System, das nur nicht volle Schiffe fortschreibt, Schild vor Hülle im abstrakten Auflöser, Munition als Ware
/// (noch ohne Verbrauch) mit Schalter.
/// </summary>
public class Phase23Tests
{
    static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Standardwelt wie im Spiel (11 Sektoren; Treibstoff und Forschung aus).</summary>
    static Universe Standard(ulong? seed = null) => TestWorlds.Default(seed, fuel: false, research: false);

    static Ship Police(Universe u) => SecuritySystem.PoliceOf(u, new FactionId(1)).OrderBy(s => s.Id.Value).First();

    static Ship AnyTrader(Universe u) => u.Ships.Values.Where(s => s.Role == ShipRole.Trader && s.Stats.Shield == 0f).OrderBy(s => s.Id.Value).First();

    // ───────── Leistungsregel ─────────

    [Fact]
    public void Verteilung_AusgeglichenJeEinDrittel_StellungVerschiebtDenFestenAnteil()
    {
        var even = ShipSystems.Split(PowerMode.Balanced, 0.6f);
        Assert.Equal(1f / 3f, even.Weapons, 4); Assert.Equal(1f / 3f, even.Shield, 4); Assert.Equal(1f / 3f, even.Engine, 4);
        foreach (var mode in new[] { PowerMode.Weapons, PowerMode.Shield, PowerMode.Engine })
        {
            var s = ShipSystems.Split(mode, 0.6f);
            Assert.Equal(1f, s.Weapons + s.Shield + s.Engine, 4);
            float focus = mode switch { PowerMode.Weapons => s.Weapons, PowerMode.Shield => s.Shield, _ => s.Engine };
            Assert.Equal(0.6f, focus, 4);
        }
        // ein Fokus unter einem Drittel wäre eine Schwächung — wird auf ausgeglichen angehoben
        Assert.Equal(1f / 3f, ShipSystems.Split(PowerMode.Shield, 0.1f).Shield, 4);
    }

    [Fact]
    public void Leistungsfluss_UeberschussLaedtBankUndSchild_VollesSystemGibtAb_SchildGedeckelt()
    {
        var r = new EconomyRules();
        var s = new ShipStats { PowerOutput = 14f, PowerDraw = 8f, Shield = 40f, ShieldRegen = 0.5f, PowerStorage = 20f };
        Assert.Equal(6f, ShipSystems.Surplus(s));
        // beide brauchen Leistung: je ein Drittel, der Antriebsanteil bleibt beim Antrieb
        var both = ShipSystems.Flow(s, PowerMode.Balanced, shieldNeeds: true, bankNeeds: true, r);
        Assert.Equal(2f, both.ShieldPower, 4); Assert.Equal(2f, both.BankPower, 4); Assert.Equal(2f, both.EnginePower, 4);
        Assert.Equal(MathF.Min(0.5f, 2f * r.ShieldPerPower), both.ShieldRate, 4);
        Assert.Equal(2f * r.ChargePerPower, both.ChargeRate, 4);
        // Schild voll: sein Anteil lädt die Bank mit
        var bankOnly = ShipSystems.Flow(s, PowerMode.Balanced, shieldNeeds: false, bankNeeds: true, r);
        Assert.Equal(4f, bankOnly.BankPower, 4); Assert.Equal(0f, bankOnly.ShieldRate);
        // Bank voll: ihr Anteil geht an den Schild — die Regeneration bleibt beim Modulwert gedeckelt
        var shieldOnly = ShipSystems.Flow(s, PowerMode.Shield, shieldNeeds: true, bankNeeds: false, r);
        Assert.Equal(6f * (0.6f + 0.2f), shieldOnly.ShieldPower, 4);
        Assert.Equal(0.5f, shieldOnly.ShieldRate, 4);
        // Stellung „Waffen“ lädt die Bank schneller als ausgeglichen, „Schild“ den Schild (ungedeckelt gerechnet)
        var fast = new ShipStats { PowerOutput = 14f, PowerDraw = 8f, Shield = 40f, ShieldRegen = 100f, PowerStorage = 20f };
        Assert.True(ShipSystems.Flow(fast, PowerMode.Weapons, true, true, r).ChargeRate > ShipSystems.Flow(fast, PowerMode.Balanced, true, true, r).ChargeRate);
        Assert.True(ShipSystems.Flow(fast, PowerMode.Shield, true, true, r).ShieldRate > ShipSystems.Flow(fast, PowerMode.Balanced, true, true, r).ShieldRate);
        // kein Überschuss: nichts lädt
        var none = new ShipStats { PowerOutput = 5f, PowerDraw = 7f, Shield = 40f, ShieldRegen = 0.5f, PowerStorage = 20f };
        Assert.Equal(0f, ShipSystems.Flow(none, PowerMode.Balanced, true, true, r).ShieldRate);
        Assert.Equal(0f, ShipSystems.Flow(none, PowerMode.Balanced, true, true, r).ChargeRate);
    }

    [Fact]
    public void Fortschreiben_SkaliertMitDt_PauseHaeltDenSchildAn()
    {
        var r = new EconomyRules();
        var s = new ShipStats { PowerOutput = 14f, PowerDraw = 8f, Shield = 40f, ShieldRegen = 0.5f, PowerStorage = 20f };
        var once = new ShipSystemsState { ShieldMissing = 30f, ChargeMissing = 15f };
        var twice = new ShipSystemsState { ShieldMissing = 30f, ChargeMissing = 15f };
        ShipSystems.Advance(s, once, ShipSystems.Seconds(10), false, r);
        ShipSystems.Advance(s, twice, ShipSystems.Seconds(5), false, r);
        ShipSystems.Advance(s, twice, ShipSystems.Seconds(5), false, r);
        Assert.Equal(once.ShieldMissing, twice.ShieldMissing, 4);
        Assert.Equal(once.ChargeMissing, twice.ChargeMissing, 4);
        Assert.True(once.ShieldMissing < 30f && once.ChargeMissing < 15f);
        // in der Pause nach einem Treffer lädt der Schild nicht, die Bank bekommt seinen Anteil
        var paused = new ShipSystemsState { ShieldMissing = 30f, ChargeMissing = 15f };
        ShipSystems.Advance(s, paused, ShipSystems.Seconds(10), true, r);
        Assert.Equal(30f, paused.ShieldMissing);
        Assert.True(paused.ChargeMissing < once.ChargeMissing);
        // voll = fertig
        var full = new ShipSystemsState { ShieldMissing = 0.01f };
        Assert.False(ShipSystems.Advance(s, full, 1f, false, r));
        Assert.Equal(0f, full.ShieldMissing);
    }

    // ───────── Schild fängt Schaden zuerst ─────────

    [Fact]
    public void Begegnung_SchadenTrifftZuerstDenSchild_DannDieHuelle_StaerkeMitAktuellemSchild()
    {
        var u = Standard();
        var p = Police(u);
        Assert.True(p.Stats.Shield > 0f);
        Assert.Equal(p.Stats.Shield, p.ShieldNow);
        float hull = p.Hull, full = CombatRules.Strength(p);
        // kleiner Treffer: nur Schild
        float small = 0.1f;   // × Maximalhülle (140) = 14 < Schild 40
        CombatRules.Damage(u, new[] { p }, small);
        Assert.Equal(hull, p.Hull);
        Assert.Equal(p.Stats.Shield - small * p.MaxHull, p.ShieldNow, 3);
        Assert.True(CombatRules.Strength(p) < full);   // Phase 23: aktueller Schild zählt
        Assert.True(u.Recovering.Contains(p.Id));
        Assert.True(p.Systems.ShieldPauseUntil > u.Tick);
        // großer Treffer: Schild leer, Rest auf die Hülle
        float before = p.ShieldNow;
        CombatRules.Damage(u, new[] { p }, 0.5f);
        Assert.Equal(0f, p.ShieldNow);
        Assert.Equal(hull - (0.5f * p.MaxHull - before), p.Hull, 3);
        // Hüllenboden gilt nur für die Hülle
        var t = AnyTrader(u);
        CombatRules.Damage(u, new[] { t }, 0.9f, _ => 0.25f);
        Assert.Equal(0.25f * t.MaxHull, t.Hull, 3);
        // Helfer zur Hälfte, ebenfalls zuerst auf den Schild
        var helpers = SecuritySystem.PoliceOf(u, new FactionId(2)).OrderBy(s => s.Id.Value).Take(2).ToList();
        CombatRules.Damage(u, helpers, 0.2f);
        Assert.Equal(helpers[0].Stats.Shield - 0.2f * helpers[0].MaxHull, helpers[0].ShieldNow, 3);
        Assert.Equal(helpers[1].Stats.Shield - 0.1f * helpers[1].MaxHull, helpers[1].ShieldNow, 3);
    }

    // ───────── System rührt volle Schiffe nicht an ─────────

    [Fact]
    public void System_VolleSchiffeKostenNichts_GetroffeneErholenSichUndFallenHeraus()
    {
        var u = Standard();
        var sys = new ShipSystemsSystem();
        Assert.Equal(0, u.Recovering.Count);
        Assert.All(u.Ships.Values, s => Assert.True(s.Systems.IsDefault));
        ulong h = UniverseHash.Compute(u);
        for (int i = 0; i < 50; i++) sys.Step(u, 1);
        Assert.Equal(h, UniverseHash.Compute(u));   // nichts geändert, nichts im Index
        Assert.Equal(0, u.Recovering.Count);

        var p = Police(u);
        ShipSystems.Absorb(u, p, 25f);
        Assert.Equal(1, u.Recovering.Count);
        float low = p.ShieldNow;
        for (int i = 0; i < u.Rules.ShieldDelayTicks; i++) { sys.Step(u, 1); u.Tick++; }
        Assert.Equal(low, p.ShieldNow);   // Pause nach dem Treffer
        int ticks = 0;
        while (u.Recovering.Count > 0 && ticks < 10000) { sys.Step(u, 1); u.Tick++; ticks++; }
        Assert.Equal(p.Stats.Shield, p.ShieldNow);
        Assert.Equal(0, u.Recovering.Count);
        Assert.True(p.Systems.IsDefault);   // voll und ohne Pause = Standardzustand (nichts im Spielstand)
        // Erholungsdauer folgt aus der Regel: Überschuss × Anteil × ShieldPerPower, gedeckelt durch shieldRegen
        var flow = ShipSystems.Flow(p.Stats, PowerMode.Balanced, true, false, u.Rules);
        Assert.InRange(ticks, 25f / flow.ShieldRate * SimClockTicks - 2, 25f / flow.ShieldRate * SimClockTicks + 2);
        // entferntes Schiff fällt heraus
        ShipSystems.Absorb(u, p, 5f);
        u.RemoveShip(p.Id, "test");
        sys.Step(u, 1);
        Assert.Equal(0, u.Recovering.Count);
    }

    const float SimClockTicks = SimClock.TicksPerSecond;

    [Fact]
    public void Leistungsverteilung_StellungSchildLaedtSchnellerAlsAntrieb()
    {
        var u = Standard();
        var a = Police(u);
        var b = SecuritySystem.PoliceOf(u, new FactionId(1)).OrderBy(s => s.Id.Value).Skip(1).First();
        // Runde 12: der Wächter hat jetzt einen M-Reaktor — mit dem großen Überschuss laden beide Schilde am Regenerationsdeckel gleich schnell; geprüft wird die
        // Aufteilung des Überschusses, also beide mit dem früheren Reaktor Mk2
        foreach (var s in new[] { a, b }) { s.Fit[0] = "reactor_mk2"; s.RecomputeStats(u); }
        ShipSystems.SetMode(u, a, PowerMode.Shield);
        // ohne Speicherbank fließt der Waffenanteil außerhalb des Gefechts in den Schild (voll/fehlt = abgeben); der Antriebsanteil bleibt beim Antrieb
        ShipSystems.SetMode(u, b, PowerMode.Engine);
        ShipSystems.Absorb(u, a, 30f);
        ShipSystems.Absorb(u, b, 30f);
        var sys = new ShipSystemsSystem();
        u.Tick += u.Rules.ShieldDelayTicks;
        for (int i = 0; i < 40; i++) { sys.Step(u, 1); u.Tick++; }
        Assert.True(a.ShieldNow > b.ShieldNow, $"Schild {a.ShieldNow} vs Antrieb {b.ShieldNow}");
        // Antrieb: nur das Tempo des Spielerflugs
        var pl = u.Ships[u.PlayerShip];
        Assert.Equal(1f, ShipSystems.SpeedFactor(u, pl));
        ShipSystems.SetMode(u, pl, PowerMode.Engine);
        Assert.Equal(1f + u.Rules.EngineModeSpeedBonus, ShipSystems.SpeedFactor(u, pl), 4);
    }

    // ───────── Zustand je Slot (vorbereitet) ─────────

    [Fact]
    public void Zustand_AusgefallenesModulZaehltNicht_ReaktorUndAntriebHalbeLeistung()
    {
        var u = Standard();
        var miner = u.Ships.Values.Where(s => s.ModelKey == "unit_miner_60").OrderBy(s => s.Id.Value).First();
        var before = miner.Stats;
        Assert.True(miner.Has(Capability.Mining));
        int laser = miner.Fit.IndexOf("drill_head_s"), reactor = miner.Fit.IndexOf("reactor_mk1"), engine = miner.Fit.IndexOf("chem_engine_s"), cargo = miner.Fit.IndexOf("cargo_universal_60");
        Assert.True(miner.Systems.Condition.Count == 0 && miner.ConditionOf(laser) == 1f);   // Phase 23: alle heil
        ShipSystems.SetCondition(u, miner, laser, 0.1f);
        Assert.False(miner.Has(Capability.Mining));
        Assert.Equal(before.PowerDraw - u.Modules["drill_head_s"].PowerDraw, miner.Stats.PowerDraw, 3);
        Assert.Equal(before.Mass, miner.Stats.Mass, 3);   // Masse bleibt
        ShipSystems.SetCondition(u, miner, reactor, 0f);
        Assert.Equal(before.PowerOutput * 0.5f, miner.Stats.PowerOutput, 3);
        ShipSystems.SetCondition(u, miner, engine, 0.2f);
        Assert.Equal(before.Thrust * 0.5f, miner.Stats.Thrust, 3);
        ShipSystems.SetCondition(u, miner, cargo, 0f);
        Assert.Equal(before.Cargo - 60f, miner.Stats.Cargo);   // Phase 26: ein ausgefallenes Frachtmodul hält keine Fracht mehr (Tank, Magazin, Bank bleiben)
        // über der Schwelle zählt das Modul voll
        ShipSystems.SetCondition(u, miner, laser, u.Rules.ModuleFailCondition);
        Assert.True(miner.Has(Capability.Mining));
        // Spielstand: Zustand wird gespeichert und nach dem Laden wieder angewandt
        var (l, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        var lm = l.Ships[miner.Id];
        Assert.Equal(miner.Systems.Condition, lm.Systems.Condition);
        Assert.Equal(miner.Stats.PowerOutput, lm.Stats.PowerOutput);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(l));
        // alles heil = leere Liste
        foreach (var slot in new[] { laser, reactor, engine, cargo }) ShipSystems.SetCondition(u, miner, slot, 1f);
        Assert.Empty(miner.Systems.Condition);
        Assert.Equal(before.PowerOutput, miner.Stats.PowerOutput);
        Assert.Equal(before.Thrust, miner.Stats.Thrust);
    }

    // ───────── Munition ─────────

    [Fact]
    public void Munition_WareRezeptLagerUndMagazin()
    {
        var u = Standard();
        var ammo = u.Goods[DemoUniverse.Ammo];
        Assert.Equal("ammo", ammo.Key);
        Assert.Equal("Munition", ammo.Name);
        Assert.Equal("ammo", ammo.Category);
        Assert.Equal(GoodForm.Solid, ammo.Form);
        Assert.Equal(DemoUniverse.FirstModuleGoodId - 2, ammo.Id.Value);   // Phase 28: vor der Rakete angehängt, Modul-Waren dahinter
        // keine Exportware
        foreach (var st in u.Stations.Values)
            if (ExportRules.ConfigOf(u, st) is { } cfg) Assert.DoesNotContain(DemoUniverse.Ammo, cfg.Goods);
        // Rezept im Waffen- und Schildwerk aus Stahl und Kupfer
        var works = u.StationTypes["weapons_works"];
        var recipe = Assert.Single(works.Recipes, r => r.Outputs.ContainsKey(DemoUniverse.Ammo));
        Assert.Equal(new[] { DemoUniverse.Steel, DemoUniverse.Copper }.OrderBy(g => g.Value), recipe.Inputs.Keys.OrderBy(g => g.Value));
        Assert.Single(u.StationTypes.Values, t => t.Recipes.Any(r => r.Outputs.ContainsKey(DemoUniverse.Ammo)));
        // Lagerware an Werft, Polizeiwache und Handelsstation, kleine Ziele
        foreach (var key in new[] { "shipyard", "police_station", "trade_station" })
        {
            var stock = Assert.Single(u.StationTypes[key].Stocks, s => s.Good == DemoUniverse.Ammo);
            Assert.InRange(stock.Target, 1f, 10f);
            Assert.True(stock.Amount < stock.Target);   // Startbestand unter dem Ziel: Händler liefern
            Assert.All(u.Stations.Values.Where(s => s.TypeKey == key), st => Assert.True(st.IsStockOnly(DemoUniverse.Ammo)));
        }
        // Magazinmodul: ammoCapacity → ShipStats.AmmoCapacity, getrennt vom Laderaum
        var mag = u.Modules["weapon_magazine_s"];
        Assert.Equal("utility", mag.Slot);
        var skiff = u.ShipClasses["skiff"];
        var fit = u.ShipModels["prospector"].Fit.Select(k => k is null ? null : u.Modules[k]).ToList();
        var without = ShipStats.Compute(skiff, fit);
        fit[skiff.Slots.FindIndex(s => s.Type == "utility")] = mag;
        var with = ShipStats.Compute(skiff, fit);
        Assert.Equal(0f, without.AmmoCapacity);
        Assert.Equal(mag.Stats["ammoCapacity"], with.AmmoCapacity);
        Assert.Equal(without.Cargo, with.Cargo);
        // das Magazin ist eine Modul-Ware mit Fabrikrezept und Werftlager
        var magGood = u.FindGood("weapon_magazine_s")!;
        Assert.Contains(u.StationTypes.Values, t => t.Recipes.Any(r => r.Outputs.ContainsKey(magGood.Id)));
        Assert.Contains(u.StationTypes["shipyard"].Stocks, s => s.Good == magGood.Id);
    }

    [Fact]
    public void Munition_MagazinStartetVoll_SchalterAusGiltAlsVoll_UndLaesstRezeptUndLagerWeg()
    {
        var u = Standard();
        var p = Police(u);
        // Phase 24: der Wächter trägt ein kinetisches Geschütz (30 Schuss) und das Munitionsmagazin (60) im Utility-Slot
        int util = p.Slots.FindIndex(s => s.Type == "utility"), gun = p.Fit.IndexOf("weapon_massdriver_s");
        Assert.Equal("weapon_magazine_s", p.Fit[util]);
        Assert.True(gun >= 0);
        Assert.Equal(90f, p.Stats.AmmoCapacity);
        Assert.Equal(90f, ShipSystems.Ammo(u, p));   // neu = voll
        p.Systems.AmmoMissing = 75f;
        Assert.Equal(15f, ShipSystems.Ammo(u, p));
        u.Rules.AmmoActive = false;
        Assert.Equal(90f, ShipSystems.Ammo(u, p));   // abgeschaltet: Magazine gelten als voll
        // Fehlbetrag wird auf die Kapazität begrenzt (Ausbau des Magazins: 30 bleiben im Geschütz, ohne Geschütz 0)
        p.Fit[util] = null;
        p.RecomputeStats(u);
        Assert.Equal(30f, p.Systems.AmmoMissing);
        p.Fit[gun] = null;
        p.RecomputeStats(u);
        Assert.Equal(0f, p.Systems.AmmoMissing);

        // Weltaufbau mit abgeschalteter Munition: kein Rezept, keine Lagerposten, kein Markteintrag
        var def = TestWorlds.Def(fuel: false, research: false);
        def.Rules!.AmmoActive = false;
        var off = WorldBuilder.Build(def);
        var ammo = off.FindGood("ammo")!.Id;
        Assert.DoesNotContain(off.StationTypes.Values, t => t.Recipes.Any(r => r.Outputs.ContainsKey(ammo)));
        Assert.DoesNotContain(off.StationTypes.Values, t => t.Stocks.Any(s => s.Good == ammo));
        Assert.DoesNotContain(off.Stations.Values, s => s.Market.ContainsKey(ammo));
        // Standardwelt: Munition an
        Assert.True(Standard().Rules.AmmoActive);
    }

    // ───────── Spielstand und Determinismus ─────────

    [Fact]
    public void Spielstand_BordsystemeBitIdentisch_AuchBeimWeiterlaufen()
    {
        var u = Standard();
        new SimulationRunner().Run(u, 300);
        var p = Police(u);
        ShipSystems.Absorb(u, p, 22f);
        ShipSystems.SetMode(u, p, PowerMode.Shield);
        var q = SecuritySystem.PoliceOf(u, new FactionId(2)).OrderBy(s => s.Id.Value).First();
        ShipSystems.Absorb(u, q, 100f);
        ShipSystems.SetCondition(u, q, q.Fit.IndexOf("weapon_laser_s"), 0.1f);
        q.Systems.AmmoMissing = 0f;
        ShipSystems.SetMode(u, u.Ships[u.PlayerShip], PowerMode.Engine);
        var json = SaveGameJson.Write(u, Now);
        Assert.Contains("\"Systems\"", json);
        Assert.Contains("\"ShieldMissing\"", json);
        var (l, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(l));
        Assert.True(l.Recovering.Contains(p.Id) && l.Recovering.Contains(q.Id));
        Assert.Equal(PowerMode.Shield, l.Ships[p.Id].PowerMode);
        Assert.Equal(PowerMode.Engine, l.Ships[l.PlayerShip].PowerMode);
        var r1 = new SimulationRunner(); var r2 = new SimulationRunner();
        for (int i = 0; i < 4; i++)
        {
            r1.Run(u, 500); r2.Run(l, 500);
            Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(l));
        }
        // unbeschädigte Schiffe schreiben nichts in den Spielstand
        var fresh = SaveGameJson.Write(Standard(), Now);
        Assert.DoesNotContain("\"Systems\"", fresh);
    }

    [Fact]
    public void Determinismus_GleicherSeedGleicherHash()
    {
        var a = Standard(7); var b = Standard(7);
        new SimulationRunner().Run(a, 4000);
        new SimulationRunner().Run(b, 4000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }

    // ───────── Langlauf ─────────

    [Fact]
    public void Langlauf_20000Ticks_SchildeErholenSich_MunitionWirdGeliefert_WirtschaftStabil()
    {
        var u = Standard();
        var ev = new List<SimEvent>();
        u.Events.Raised += ev.Add;
        int recoveringMax = 0;
        var runner = new SimulationRunner();
        for (int i = 0; i < 200; i++)
        {
            runner.Run(u, 100);
            recoveringMax = Math.Max(recoveringMax, u.Recovering.Count);
            foreach (var s in u.Ships.Values)
            {
                Assert.InRange(s.ShieldNow, 0f, s.Stats.Shield);
                Assert.InRange(s.Systems.ShieldMissing, 0f, s.Stats.Shield);
                Assert.InRange(s.Systems.ChargeMissing, 0f, s.Stats.PowerStorage);
                if (ShipSystems.Recovers(s)) Assert.True(u.Recovering.Contains(s.Id), $"Schiff {s.Id.Value} fehlt im Erholungsindex");   // Runde 13b: nur wer sich erholen KANN (Phase-24-Regel; ohne Reaktorüberschuss bleibt ein Schiff draußen)
            }
        }
        // Begegnungen haben Schilde getroffen, die Schiffe haben sich erholt (der Index ist nie groß)
        Assert.Contains(ev, e => e is RaidResolved or RaiderIntercepted);
        Assert.True(recoveringMax > 0, "kein Schild wurde je getroffen");
        Assert.True(recoveringMax <= u.Ships.Values.Count(s => s.Stats.Shield > 0f || s.Stats.PowerStorage > 0f));
        // Munition: Händler haben die Lager beliefert, das Waffenwerk hat sie hergestellt
        Assert.Contains(ev, e => e is TradeCompleted t && t.Good == DemoUniverse.Ammo);
        // Verlustquote unter 1 % des Handelsumsatzes, Händler und Schürfer halten ihre Kapazität
        float loot = ev.OfType<RaidResolved>().Sum(r => r.LootValue), revenue = ev.OfType<TradeCompleted>().Sum(t => t.Revenue);
        Assert.True(loot < 0.01f * revenue, $"Verlustquote {100f * loot / revenue:0.00} %");
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.Default.First(j => j.Key == "trade")) >= 0.8f * u.Rules.MaxTraders);
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.Default.First(j => j.Key == "mine")) >= 0.8f * u.Rules.MaxMiners);
    }
}
