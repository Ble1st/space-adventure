using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using SpaceSim.Core.Weapons;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 26 (KAMPF.md 3.3, 4.4, 4.5): Modulschaden, Brand und Löschanlage, Reparatur von Modulen, keine Gratisreparatur mehr, bezahlte NPC-Reparatur, Wracks und
/// Frachtbehälter, Bergung, laufende Leihmiete, robustes Freiräumen im räumlichen Gefecht.
/// </summary>
public class Phase26Tests
{
    static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    static readonly SectorId Alpha = new(1);
    static readonly SectorId Nebelmark = new(9);

    static Universe Standard(ulong? seed = null) => TestWorlds.Default(seed, fuel: false, research: false);

    /// <summary>Dreht Phase 26 in einer Weltdefinition zurück (Schalter aus, neue Module und Regale, Reparaturdienst/-lager der Regierungsstationen).</summary>
    internal static void RevertPhase26(WorldDef def)
    {
        def.Rules!.DamageActive = false;
        def.Rules.RepairPlatesPerHull = 0.05f;   // Korrektur: Reparaturpreis vorher
        def.Modules.RemoveAll(m => m.Key is "wet_suppressor_s" or "wreck_grapple_s");
        foreach (var t in def.StationTypes)
        {
            t.Stocks?.Remove("wet_suppressor_s");
            t.Stocks?.Remove("wreck_grapple_s");
            if (t.Key is "headquarters" or "sector_hq" or "police_station")
            {
                t.Services.Remove("repair");
                t.Stocks?.Remove("hull_plates");
                t.Stocks?.Remove("electronics");
                if (t.Stocks is { Count: 0 }) t.Stocks = null;
            }
        }
    }

    static List<SimEvent> Capture(Universe u)
    {
        var list = new List<SimEvent>();
        u.Events.Raised += list.Add;
        return list;
    }

    static Ship NewShip(Universe u, string model, ShipRole role, ActorId owner, float credits = 2000f, Controller controller = Controller.Ai)
    {
        var m = u.ShipModels[model];
        var cls = u.ShipClasses[m.ClassKey];
        var yard = u.StationsById().First(s => !s.IsBuildSite);
        var ship = ShipPurchase.Create(u, u.NewShipId(), model, cls.Slots, m.Fit, role, controller, owner, yard, credits);
        u.AddShip(ship);
        return ship;
    }

    static void Place(Universe u, Ship s, SectorId sector, Vec2 pos)
    {
        if (s.Transit is not null) u.FinishTransit(s);
        s.Order = null; s.Phase = TradePhase.Idle; s.MinePhase = MinePhase.Idle;
        if (s.Sector != sector) u.MoveShip(s, sector);
        s.Position = pos;
        s.DockedAt = default;
    }

    static Station Yard(Universe u) => u.StationsById().First(s => s.TypeKey == "shipyard");

    static void DockAt(Universe u, Ship s, Station st)
    {
        Place(u, s, st.Sector, st.DockPoint(s.Id));
        s.DockedAt = st.Id;
    }

    static void Step(Universe u, ISimSystem sys, int ticks)
    {
        for (int i = 0; i < ticks; i++) { u.Tick++; sys.Step(u, 1); }
    }

    // ───────── Modulschaden ─────────

    [Fact]
    public void Modulschaden_ErstUnterHalberHuelle_GenauDreiZuege_AusfallUndReaktorHalb()
    {
        var u = Standard();
        var s = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        // über der Schwelle: kein Zug, kein Schaden
        s.Hull = s.MaxHull * 0.8f;
        ulong before = u.Rng.State;
        ModuleDamage.OnHullHit(u, s, 5f);
        Assert.Equal(before, u.Rng.State);
        Assert.Empty(s.Systems.Condition);
        // unter der Schwelle: genau drei Züge, unabhängig vom Ausgang
        s.Hull = s.MaxHull * 0.4f;
        for (int i = 0; i < 20; i++)
        {
            var rng = new DeterministicRng(1) { State = u.Rng.State };
            for (int k = 0; k < ModuleDamage.DrawsPerHullHit; k++) rng.NextDouble();
            ModuleDamage.OnHullHit(u, s, 5f);
            Assert.Equal(rng.State, u.Rng.State);
        }
        // Schalter aus: keine Züge
        u.Rules.DamageActive = false;
        before = u.Rng.State;
        ModuleDamage.OnHullHit(u, s, 5f);
        Assert.Equal(before, u.Rng.State);
        u.Rules.DamageActive = true;
        // Ausfall: Reaktor läuft mit halber Leistung, Waffe ausgefallen
        var fresh = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        float output = fresh.Stats.PowerOutput;
        int reactor = fresh.Fit.IndexOf("reactor_mk1"), laser = fresh.Fit.IndexOf("weapon_laser_s");
        var ev = Capture(u);
        Assert.True(ModuleDamage.Damage(u, fresh, reactor, 0.9f));
        Assert.Equal(output * 0.5f, fresh.Stats.PowerOutput, 3);
        Assert.Contains(ev.OfType<ModuleDamaged>(), e => e.Slot == reactor && e.Failed);
        ModuleDamage.Damage(u, fresh, laser, 0.8f);
        Assert.False(fresh.Has(Capability.Combat));
        Assert.Null(Gunnery.WeaponAt(u, fresh, laser));
    }

    [Fact]
    public void Modulschaden_UeberGefechtstreffer_SenktZustand()
    {
        // Treffer der gemeinsamen Schussregel (NPC wie Spieler) unter 50 % Hülle beschädigen Module
        var u = Standard();
        var s = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        s.Hull = s.MaxHull * 0.45f;
        var ev = Capture(u);
        for (int i = 0; i < 200 && !ev.OfType<ModuleDamaged>().Any(); i++)
        {
            s.Hull = s.MaxHull * 0.45f;
            ModuleDamage.OnHullHit(u, s, 4f);
        }
        Assert.Contains(ev.OfType<ModuleDamaged>(), e => e.Ship == s.Id && e.Condition < 1f);
    }

    [Fact]
    public void AusgefallenesFrachtmodul_UeberzaehligeFrachtFaelltAlsBehaelterIns_All()
    {
        var u = Standard();
        var s = NewShip(u, "unit_miner_60", ShipRole.Miner, u.FreeActor);
        Place(u, s, Nebelmark, new Vec2(10f, 10f));
        var iron = u.FindGood("iron_ore")!.Id;
        s.Cargo[iron] = 50f;
        int cargo = s.Fit.IndexOf("cargo_universal_60");
        ModuleDamage.Damage(u, s, cargo, 0.9f);
        Assert.Equal(0f, s.Stats.Cargo);
        Assert.Equal(0f, s.CargoCapacity);
        Assert.False(s.Cargo.ContainsKey(iron));   // nichts passt mehr: alles im Behälter
        var box = Assert.Single(u.Wrecks);
        Assert.Equal(WreckKind.Container, box.Kind);
        Assert.Equal(50f, box.Cargo[iron], 3);
        Assert.Equal(Nebelmark, box.Sector);
        // gültig: nie mehr Fracht als Raum
        Assert.True(s.UsedVolume(u) <= s.CargoCapacity + 1e-3f);
    }

    // ───────── Brand ─────────

    [Fact]
    public void Brand_Entzuendung_Fortschritt_Loeschen_Loeschanlage_Andocken_BrenntNachGefechtWeiter()
    {
        var u = Standard();
        var s = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        Place(u, s, Nebelmark, new Vec2(20f, 0f));
        int reactor = s.Fit.IndexOf("reactor_mk1"), cargo = s.Fit.IndexOf("cargo_universal_100");
        // Entzündungschance steigt mit sinkendem Zustand, Reaktor brennt leichter
        float c1 = ModuleDamage.IgniteChance(u, s, cargo);
        ModuleDamage.SetCondition(u, s, cargo, 0.4f);
        Assert.True(ModuleDamage.IgniteChance(u, s, cargo) > c1);
        Assert.True(ModuleDamage.IgniteChance(u, s, reactor) > c1);
        var ev = Capture(u);
        ModuleDamage.Ignite(u, s, reactor);
        Assert.True(s.Systems.IsBurning(reactor));
        Assert.True(u.Recovering.Contains(s.Id));
        Assert.Contains(ev.OfType<FireStarted>(), e => e.Slot == reactor);
        // Fortschritt: ohne Löschen frisst er Zustand und Hülle — auch ohne Gefecht (kein Gefecht läuft)
        u.Rules.FireExtinguishChance = 0f;
        float hull = s.Hull, cond = s.ConditionOf(reactor);
        var fire = new FireSystem();
        var systems = new ShipSystemsSystem();
        for (int i = 0; i < 40; i++) { u.Tick++; systems.Step(u, 1); fire.Step(u, 1); }
        Assert.False(u.Fights.IsEngaged(s.Id));
        Assert.True(s.ConditionOf(reactor) < cond);
        Assert.True(s.Hull < hull && s.Hull >= 1f);
        Assert.True(s.Systems.IsBurning(reactor) || ev.OfType<FireEnded>().Any(e => e.How == "burnt_out"));
        // Löschchance: Grundwert, mit Löschanlage höher; eine Runde zieht genau einen Zug je Brand
        u.Rules.FireExtinguishChance = 0.12f;
        float baseChance = ModuleDamage.ExtinguishChance(u, s);
        Assert.Equal(0.12f, baseChance, 3);
        var pip = NewShip(u, "pip", ShipRole.Trader, u.FreeActor);
        int util = pip.Slots.FindIndex(sl => sl.Type == "utility");
        pip.Fit[util] = "wet_suppressor_s";
        pip.RecomputeStats(u);
        Assert.Equal(0.12f + u.Modules["wet_suppressor_s"].Stats["extinguish"], ModuleDamage.ExtinguishChance(u, pip), 3);
        ModuleDamage.Ignite(u, s, reactor);
        var rng = new DeterministicRng(1) { State = u.Rng.State };
        rng.NextDouble();
        FireSystem.Round(u, s);
        Assert.Equal(rng.State, u.Rng.State);
        // Löschen sicher bei Chance 1
        u.Rules.FireExtinguishChance = 1f;
        ModuleDamage.Ignite(u, s, reactor);
        FireSystem.Round(u, s);
        Assert.False(s.Systems.IsBurning(reactor));
        Assert.Contains(ev.OfType<FireEnded>(), e => e.How == "extinguished");
        // Andocken löscht
        u.Rules.FireExtinguishChance = 0f;
        ModuleDamage.Ignite(u, s, reactor);
        s.DockedAt = Yard(u).Id;
        u.Tick++; fire.Step(u, 1);
        Assert.Empty(s.Systems.Burning);
        Assert.Contains(ev.OfType<FireEnded>(), e => e.How == "docked");
    }

    // ───────── Reparatur ─────────

    [Fact]
    public void Reparaturdienst_Module_StrikteSchluessel_MaterialUndPreis()
    {
        var u = Standard();
        new SimulationRunner().Run(u, 1);   // Preise
        var yard = Yard(u);
        var me = u.Ships[u.PlayerShip];
        DockAt(u, me, yard);
        var svc = new RepairService();
        int reactor = me.Fit.IndexOf(me.Fit.First(k => k is not null && k.StartsWith("reactor")));
        // unbeschädigt: nichts
        Assert.Empty(svc.Offers(u, yard, me));
        Assert.False(svc.Execute(u, yard, me, new ServiceRequest($"repair:module:{reactor}", default, 0f)).Success);
        ModuleDamage.SetCondition(u, me, reactor, 0.3f);
        ModuleDamage.Ignite(u, me, reactor);
        var plates = RepairService.PlatesGood(u)!.Value; var elec = RepairService.ElectronicsGood(u)!.Value;
        yard.Market[plates].Amount = 50f; yard.Market[elec].Amount = 50f;
        var offers = svc.Offers(u, yard, me);
        Assert.Contains(offers, o => o.Key == $"repair:module:{reactor}" && o.Amount > 0.69f);
        Assert.Contains(offers, o => o.Key == "repair:all");
        // strikte Schlüssel: nichts geändert, kein Ereignis
        var ev = Capture(u);
        float wallet = u.PlayerWallet.Credits;
        foreach (var bad in new[] { "repair:module:", "repair:module:x", $"repair:module:0{reactor}", "repair:module:-1", "repair:modul:1", "repair:ALL", $"repair:module:{reactor} " })
            Assert.False(svc.Execute(u, yard, me, new ServiceRequest(bad, default, 0f)).Success, bad);
        Assert.Empty(ev);
        Assert.Equal(wallet, u.PlayerWallet.Credits);
        // Preis = (Platten + Elektronik) × Marktpreis × Größe × Aufschlag je Zustandspunkt
        float size = RepairService.SizeFactor(me, reactor);
        float expectPerPoint = (u.Rules.ModuleRepairPlates * yard.Market[plates].Price + u.Rules.ModuleRepairElectronics * yard.Market[elec].Price) * size * RepairService.LaborFactor;
        Assert.Equal(expectPerPoint, RepairService.PricePerModulePoint(u, yard, me, reactor), 2);
        float stationCredits = yard.Credits;
        var r = svc.Execute(u, yard, me, new ServiceRequest($"repair:module:{reactor}", default, 0f));
        Assert.True(r.Success, r.Message);
        Assert.Equal(1f, me.ConditionOf(reactor), 3);
        Assert.False(me.Systems.IsBurning(reactor));   // repariert = gelöscht
        Assert.Equal(50f - 0.7f * size * u.Rules.ModuleRepairPlates, yard.Market[plates].Amount, 2);
        Assert.Equal(50f - 0.7f * size * u.Rules.ModuleRepairElectronics, yard.Market[elec].Amount, 2);
        Assert.Equal(0.7f * expectPerPoint, wallet - u.PlayerWallet.Credits, 1);
        Assert.Equal(stationCredits + 0.7f * expectPerPoint, yard.Credits, 1);
        Assert.Contains(ev.OfType<ShipRepaired>(), e => e.Modules == 1);
        Assert.Contains(ev.OfType<ServicePerformed>(), e => e.Service == "repair");
        // beschädigtes Modul nicht ausbauen, bevor es repariert ist (keine Gratisreparatur über den Ausrüster)
        int cargo = me.Fit.FindIndex(k => k is not null && k.StartsWith("cargo"));
        ModuleDamage.SetCondition(u, me, cargo, 0.6f);
        Assert.NotNull(OutfitterService.DamagedIssue(u, me, cargo));
        Assert.False(new OutfitterService().Execute(u, yard, me, new ServiceRequest($"remove:{cargo}", default, 0f)).Success);
    }

    [Fact]
    public void KeineGratisreparatur_LiegendeNpcRegenerierenNichtMehr_ReparierenBezahlt()
    {
        var u = Standard();
        new SimulationRunner().Run(u, 1);
        var trader = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor, credits: 30000f);   // leichte Schäden nur aus dem Polster (NpcRepairReserve)
        Place(u, trader, Nebelmark, new Vec2(30f, 30f));
        trader.Hull = trader.MaxHull * 0.8f;
        var sec = new SecuritySystem();
        float hull = trader.Hull;
        Step(u, sec, 200);
        Assert.Equal(hull, trader.Hull);   // keine Gratis-Regeneration mehr
        // bezahlt an einer Station mit Reparatur (Ankunft/liegt dort): Station bekommt Geld und Material, Schiff zahlt
        var yard = Yard(u);
        var plates = RepairService.PlatesGood(u)!.Value; var elec = RepairService.ElectronicsGood(u)!.Value;
        yard.Market[plates].Amount = 40f; yard.Market[elec].Amount = 40f;
        DockAt(u, trader, yard);
        int laser = trader.Fit.IndexOf("weapon_laser_s");
        ModuleDamage.SetCondition(u, trader, laser, 0.5f);
        float credits = trader.Credits, stCredits = yard.Credits;
        var ev = Capture(u);
        var o = NpcRepair.AtStation(u, trader, yard);
        Assert.True(o.Any);
        Assert.Equal(trader.MaxHull, trader.Hull, 2);
        Assert.Equal(1f, trader.ConditionOf(laser), 3);
        Assert.Equal(credits - o.Cost, trader.Credits, 2);
        Assert.Equal(stCredits + o.Cost, yard.Credits, 2);
        Assert.True(o.Plates > 0f && o.Electronics > 0f);
        Assert.Contains(ev.OfType<ShipRepaired>(), e => e.Ship == trader.Id && !e.AtBase);
        // Budget: höchstens der Anteil der Kasse
        trader.Hull = trader.MaxHull * 0.2f;
        trader.Credits = 100f;
        var o2 = NpcRepair.AtStation(u, trader, yard);
        Assert.True(o2.Cost <= 100f * u.Rules.NpcRepairBudgetShare + 0.01f);
        // leichter Schaden und wenig Geld: keine Reparatur (die Flotte blutet sonst an Schönheitsreparaturen aus)
        trader.Hull = trader.MaxHull * 0.8f;
        trader.Credits = u.Rules.NpcRepairReserve * 0.9f;   // unter dem Polster
        Assert.False(NpcRepair.Serious(u, trader));
        Assert.False(NpcRepair.AtStation(u, trader, yard).Any);
        // Schalter aus: wie bis Phase 25 (Gratis-Regeneration liegender NPC)
        u.Rules.DamageActive = false;
        Place(u, trader, Nebelmark, new Vec2(30f, 30f));
        float h0 = trader.Hull;
        Step(u, sec, 50);
        Assert.True(trader.Hull > h0);
    }

    [Fact]
    public void NpcReparaturweg_SchwerBeschaedigt_FliegtZurReparaturstation_UndBucht()
    {
        var u = Standard();
        new SimulationRunner().Run(u, 1);
        var trader = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor, credits: 5000f);
        Place(u, trader, Nebelmark, new Vec2(30f, 30f));
        trader.Hull = trader.MaxHull * 0.3f;
        Assert.True(NpcRepair.Serious(u, trader));
        var target = NpcRepair.Nearest(u, trader);
        Assert.NotNull(target);
        Assert.True(NpcRepair.Seek(u, trader));
        Assert.True(trader.Order is GoToOrder g && g.Target == target!.Id);
        Assert.NotNull(trader.Transit);
        var ev = Capture(u);
        var runner = new SimulationRunner();
        for (int i = 0; i < 4000 && trader.Hull < trader.MaxHull * 0.99f && u.Ships.ContainsKey(trader.Id); i += 50) runner.Run(u, 50);
        Assert.True(u.Ships.ContainsKey(trader.Id));
        Assert.True(trader.Hull > trader.MaxHull * 0.6f, $"Hülle {trader.Hull:0}/{trader.MaxHull:0}");
        Assert.Contains(ev.OfType<ShipRepaired>(), e => e.Ship == trader.Id && e.Cost > 0f);
        // jede Station mit Reparaturdienst hat das Material im Markt (Standardwelt: in jedem Kartenteil, auch hinter den Sackgassen-Toren)
        foreach (var gate in new[] { new GateId(17), new GateId(27) })
            foreach (var part in MapParts.IfClosed(u, gate).Parts)
            {
                bool economy = u.Stations.Values.Any(s => part.Contains(s.Sector) && StationProtection.IsEconomic(u, s) && s.Market.Count > 0);
                if (!economy) continue;
                Assert.Contains(u.Stations.Values, s => part.Contains(s.Sector) && u.StationTypes[s.TypeKey].Services.Contains(RepairService.ServiceKey));
            }
    }

    [Fact]
    public void Polizei_ReparatAnDerBasisAusDerKasse_AuchModule()
    {
        var u = Standard();
        var police = u.ShipsById().First(ThreatMap.IsPolice);
        var owner = u.Actors[police.Owner];
        owner.Treasury.Credits = 20000f;   // Startwelt: Fraktionskassen beginnen leer
        police.Hull = police.MaxHull * 0.5f;
        ModuleDamage.SetCondition(u, police, police.Fit.IndexOf("weapon_laser_s"), 0.1f);
        ModuleDamage.Ignite(u, police, police.Fit.IndexOf("reactor_mk2"));
        float treasury = owner.Treasury.Credits;
        var ev = Capture(u);
        PirateSystem.ServiceAtBase(u, police, "police");
        Assert.Equal(police.MaxHull, police.Hull, 2);
        Assert.Empty(police.Systems.Condition);
        Assert.Empty(police.Systems.Burning);
        Assert.True(owner.Treasury.Credits < treasury);
        Assert.Contains(ev.OfType<ShipRepaired>(), e => e.AtBase && e.Modules == 1);
    }

    // ───────── Wracks ─────────

    [Fact]
    public void Wrack_InhaltFristBergung_MitUndOhneBergungsgreifer_TraktorNurFracht()
    {
        var u = Standard();
        var victim = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        Place(u, victim, Nebelmark, new Vec2(50f, 0f));
        var food = u.FindGood("food")!.Id;
        victim.Cargo[food] = 40f;
        var ev = Capture(u);
        // genau zwei Züge je eingebautem Modul
        int fitted = victim.Fit.Count(k => k is not null);
        var rng = new DeterministicRng(1) { State = u.Rng.State };
        for (int i = 0; i < 2 * fitted; i++) rng.NextDouble();
        Departures.RemoveShip(u, victim, DepartureReason.Destroyed, "raid");
        Assert.Equal(rng.State, u.Rng.State);
        var w = Assert.Single(u.Wrecks);
        Assert.Equal(WreckKind.Wreck, w.Kind);
        Assert.Equal(20f, w.Cargo[food], 3);   // Frachtanteil 0,5
        Assert.Equal(u.Tick + u.Rules.WreckTicks, w.ExpiresTick);
        Assert.Contains(ev.OfType<WreckCreated>(), e => e.Wreck == w.Id);
        // ein bergbares Modul sicherstellen
        w.Modules.Clear();
        w.Modules.Add(new WreckModule { Key = "reactor_mk1", Condition = 0.8f });
        w.Modules.Add(new WreckModule { Key = "chem_engine_s", Condition = 0.2f });   // Schrott
        // Spieler ohne Greifer: Fracht ja (Traktor), Module nein
        var me = u.Ships[u.PlayerShip];
        Place(u, me, Nebelmark, new Vec2(60f, 0f));
        Assert.NotNull(Wrecks.ModuleIssue(u, me, w, 0));
        Assert.False(Wrecks.TakeModule(u, me, w, 0, out _));
        float got = Wrecks.TakeCargo(u, me, w, 5f, out var issue);
        Assert.Equal(5f, got, 3);
        Assert.Null(issue);
        Assert.Equal(5f, me.Cargo[food], 3);
        // außer Reichweite: nichts
        Place(u, me, Nebelmark, new Vec2(200f, 0f));
        Assert.Equal(0f, Wrecks.TakeCargo(u, me, w, 5f, out var far));
        Assert.NotNull(far);
        // mit Bergungsgreifer: Modul in den Frachtraum, Schrott bleibt
        var salvager = NewShip(u, "prospector", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        int tool = salvager.Slots.FindIndex(sl => sl.Type == "tool");
        salvager.Fit[tool] = "wreck_grapple_s";
        salvager.RecomputeStats(u);
        salvager.CargoCapacity = salvager.Stats.Cargo;
        Assert.True(salvager.Has(Capability.Salvage));
        Assert.Contains(WeaponBays.For(salvager.Stats), b => b.Key == WeaponBays.Salvager.Key);
        Place(u, salvager, Nebelmark, new Vec2(55f, 0f));
        Assert.Equal(0, Wrecks.BestModule(u, salvager, w));
        Assert.True(Wrecks.TakeModule(u, salvager, w, 0, out var msg), msg);
        Assert.Equal(1f, salvager.Cargo[u.FindGood("reactor_mk1")!.Id]);
        Assert.Contains("Schrott", Wrecks.ModuleIssue(u, salvager, w, 0));
        Assert.Contains(ev.OfType<WreckSalvaged>(), e => e.Module == "reactor_mk1");
        // Frist: verfällt mit dem, was noch drin ist
        var sys = new WreckSystem();
        u.Tick = w.ExpiresTick;
        sys.Step(u, 50);
        Assert.Empty(u.Wrecks);
        Assert.Contains(ev.OfType<WreckExpired>(), e => e.Wreck == w.Id);
        // Schalter aus: kein Wrack, keine Züge
        u.Rules.DamageActive = false;
        var v2 = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        ulong before = u.Rng.State;
        Departures.RemoveShip(u, v2, DepartureReason.Destroyed, "raid");
        Assert.Equal(before, u.Rng.State);
        Assert.Empty(u.Wrecks);
    }

    [Fact]
    public void Kaperer_NimmtWrackfrachtSeinesOpfersMit()
    {
        var u = Standard();
        var raider = u.ShipsById().First(ThreatMap.IsRaider);
        var pirates = u.Actors[raider.Owner];
        pirates.Treasury.Credits = 0f;
        var w = Wrecks.Drop(u, Nebelmark, new Vec2(0f, 0f), new Dictionary<GoodId, float> { [u.FindGood("food")!.Id] = 10f }, default, "Test")!;
        float value = Wrecks.LootByRaider(u, raider, w);
        Assert.Equal(10f * u.FindGood("food")!.BasePrice, value, 2);
        Assert.Equal(value * u.Rules.FenceShare, pirates.Treasury.Credits, 2);
        Assert.Empty(u.Wrecks);
    }

    [Fact]
    public void AbgeworfeneFracht_UndWrack_UeberlebenSpeichernUndLaden_MitBrandBitIdentisch()
    {
        var u = Standard();
        new SimulationRunner().Run(u, 50);
        var w = Wrecks.Drop(u, Alpha, new Vec2(5f, 5f), new Dictionary<GoodId, float> { [u.FindGood("steel")!.Id] = 7.5f }, default, "Frachtbehälter")!;
        var wr = Wrecks.Drop(u, Alpha, new Vec2(-5f, 9f), new Dictionary<GoodId, float> { [u.FindGood("food")!.Id] = 3f }, default, "Wrack: Test")!;
        wr.Kind = WreckKind.Wreck;
        wr.Modules.Add(new WreckModule { Key = "reactor_mk1", Condition = 0.62f });
        var s = u.ShipsById().First(x => x.IsEconomyAi && x.Role == ShipRole.Trader);
        ModuleDamage.SetCondition(u, s, s.Fit.IndexOf("weapon_laser_s"), 0.5f);
        ModuleDamage.Ignite(u, s, s.Fit.IndexOf("reactor_mk1"));
        var me = u.Ships[u.PlayerShip];
        me.RentPaidDays = 3;
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        Assert.Equal(2, loaded.Wrecks.Count);
        Assert.Equal(7.5f, loaded.Wrecks.First(x => x.Id == w.Id).Cargo[u.FindGood("steel")!.Id]);
        Assert.Equal(WreckKind.Container, loaded.Wrecks.First(x => x.Id == w.Id).Kind);
        Assert.Equal(0.62f, loaded.Wrecks.First(x => x.Id == wr.Id).Modules[0].Condition);
        Assert.True(loaded.Ships[s.Id].Systems.IsBurning(s.Fit.IndexOf("reactor_mk1")));
        Assert.True(loaded.Recovering.Contains(s.Id));   // brennend → im Erholungsindex
        Assert.Equal(u.NextWreckId, loaded.NextWreckId);
        // Weiterlaufen bit-identisch
        new SimulationRunner().Run(u, 600);
        new SimulationRunner().Run(loaded, 600);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Aufgabe_GegenueberDemSpieler_WirdGespeicherterFrachtbehaelter()
    {
        var u = Standard();
        u.Rules.PlayerRaids = false;
        var me = NewShip(u, "falcon", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        Place(u, me, Nebelmark, new Vec2(0f, 0f));
        PlayerFleet.Activate(u, me, default);
        var trader = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        Place(u, trader, Nebelmark, new Vec2(0f, -25f));
        trader.Cargo[u.FindGood("food")!.Id] = 40f;
        // Angriff → das unterlegene Opfer ergibt sich irgendwann; dafür Mut hoch setzen
        PlayerGunnery.TryFire(u, me, me.Fit.IndexOf("weapon_laser_s"), trader.Id, 180f);
        var e = u.Fights.Of(trader.Id);
        if (e is null) PlayerCombat.Engage(u, me, trader);
        e = u.Fights.Of(trader.Id)!;
        var p = e.Of(trader.Id)!;
        p.Courage = 1.1f;   // sofort aufgeben
        var combat = new CombatSystem();
        for (int i = 0; i < 20 && u.Fights.IsEngaged(trader.Id); i++) { u.Tick++; combat.Step(u, 1); }
        var box = Assert.Single(u.Wrecks, w => w.Kind == WreckKind.Container);
        Assert.True(box.Cargo[u.FindGood("food")!.Id] > 0f);
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Single(loaded.Wrecks, w => w.Kind == WreckKind.Container);
    }

    // ───────── Leihmiete ─────────

    [Fact]
    public void Leihmiete_LaeuftTaeglich_AuchOhneRueckgabe_SchuldenSichtbar()
    {
        var u = Standard();
        var station = u.StationsById().First(s => u.StationTypes[s.TypeKey].Services.Contains("rental"));
        var player = u.Ships[u.PlayerShip];
        u.MoveShip(player, station.Sector); u.PlayerSector = station.Sector;
        PlayerFleet.Dock(u, player, station);
        u.PlayerWallet.Credits = 1000f;
        var ok = new RentalService().Execute(u, station, player, new ServiceRequest("rent:1", default, 0f));
        Assert.True(ok.Success, ok.Message);
        var rented = u.Ships[u.PlayerShip];
        u.PlayerWallet.Credits = 50f;
        var ev = Capture(u);
        var hangar = new HangarSystem();
        u.Tick += 1; hangar.Step(u, 50);
        Assert.Equal(1, rented.RentPaidDays);
        for (int d = 0; d < 3; d++) { u.Tick += u.Rules.DayTicks; hangar.Step(u, 50); }
        Assert.Equal(4, rented.RentPaidDays);
        // Runde 38a: der erste Tag wurde beim Mieten bezahlt — täglich abgebucht werden die drei folgenden (Liegegebühr des geparkten eigenen Schiffs gesondert)
        float other = ev.OfType<BerthFeePaid>().Sum(b => b.Amount) + ev.OfType<FleetUpkeepPaid>().Sum(f => f.Amount);
        Assert.Equal(50f - 3 * u.Rules.RentalPerDay - other, u.PlayerWallet.Credits, 2);
        Assert.True(Hangar.Debt(u) > 0f);
        Assert.Equal(3, ev.OfType<RentCharged>().Count());
        Assert.Equal(0f, Hangar.OpenRent(u, rented));
        // Verlust: nur offene Tage, keine doppelte Miete
        u.Tick += 10;
        var (deposit, fromWallet) = Hangar.SettleLostRental(u, rented);
        Assert.Equal(0f, fromWallet);
        Assert.Equal(u.Rules.RentalDeposit, deposit);
    }

    // ───────── Freiräumen im räumlichen Gefecht ─────────

    [Fact]
    public void CombatSpaceClear_LaesstKeineUeberlappung_UndBleibtInDerGefechtsflaeche()
    {
        var u = Standard();
        float radius = Collision.ShipRadius + 1f;
        foreach (var sector in u.Sectors.Values)
        {
            var obs = u.GetObstacles(sector.Id);
            foreach (var o in obs.Take(40))
            {
                var p = CombatSpace.Clear(u, sector.Id, o.Position);
                Assert.True(CombatSpace.Free(p, radius, obs), $"{sector.Name}: Überlappung bei {p.X:0.0},{p.Y:0.0}");
                Assert.True(p.Length <= u.Rules.CombatArenaRadius + 1e-3f);
            }
            var outside = CombatSpace.Clear(u, sector.Id, new Vec2(5000f, 0f));
            Assert.True(outside.Length <= u.Rules.CombatArenaRadius + 1e-3f);
        }
    }

    // ───────── Anzeige ─────────

    [Fact]
    public void Schadensliste_UndBrandwarnung()
    {
        var u = Standard();
        var s = NewShip(u, "unit_trader_100", ShipRole.Trader, u.FreeActor);
        Assert.Null(DamageInfo.FireWarning(u, s));
        Assert.All(DamageInfo.For(u, s).Rows, r => Assert.Equal(1f, r.Condition));
        int reactor = s.Fit.IndexOf("reactor_mk1");
        ModuleDamage.SetCondition(u, s, reactor, 0.1f);
        ModuleDamage.Ignite(u, s, reactor);
        var d = DamageInfo.For(u, s);
        var row = d.Rows.Single(r => r.Slot == reactor);
        Assert.True(row.Failed && row.Burning);
        Assert.Contains("BRENNT", row.Text);
        Assert.Equal(1, d.Fires);
        Assert.Contains("BRAND", DamageInfo.FireWarning(u, s));
    }

    // ───────── Korrektur nach der Prüfung ─────────

    [Fact]
    public void SoftLock_NotreparaturAufSchuld_SchiffKommtWiederZuEinnahmen()
    {
        var u = Standard();
        new SimulationRunner().Run(u, 1);
        var yard = Yard(u);
        var me = NewShip(u, "prospector", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        PlayerFleet.Activate(u, me, default);
        DockAt(u, me, yard);
        int laser = me.Slots.FindIndex(s => s.Type == "tool");   // Phase 29: Bohrkopf im Werkzeugslot
        int cargo = me.Fit.FindIndex(k => k is not null && k.StartsWith("cargo"));
        ModuleDamage.SetCondition(u, me, laser, 0.05f);
        ModuleDamage.SetCondition(u, me, cargo, 0.05f);
        me.Hull = 1f;
        u.PlayerWallet.Credits = 0f;
        Assert.False(me.Has(Capability.Mining));
        var svc = new RepairService();
        // ohne Geld: normale Reparatur unmöglich, Notreparatur angeboten
        Assert.False(svc.Execute(u, yard, me, new ServiceRequest("repair:all", default, 0f)).Success);
        Assert.Contains(svc.Offers(u, yard, me), o => o.Key == "repair:emergency" && o.Amount >= 1f);
        var r = svc.Execute(u, yard, me, new ServiceRequest("repair:emergency", default, 0f));
        Assert.True(r.Success, r.Message);
        Assert.True(me.Has(Capability.Mining));
        Assert.True(me.CargoCapacity > 0f);
        Assert.True(me.Hull >= u.Rules.EmergencyRepairHullShare * me.MaxHull - 0.01f);
        Assert.True(me.ConditionOf(laser) < 0.5f);   // nur Funktionsfähigkeit, keine volle Reparatur
        Assert.True(u.PlayerWallet.Credits < 0f);   // auf Schuld
        Assert.True(Hangar.Debt(u) > 0f);
        // wieder Einnahmen: Erz an Bord (Abbau) und an einer Station verkaufen
        var ore = u.FindGood("iron_ore")!.Id;
        me.Cargo[ore] = MathF.Min(10f, me.FreeUnits(u, ore));
        var buyer = TradeActions.BestBuyer(u, me, me.Sector, me.Position)!;
        float before = u.PlayerWallet.Credits;
        var sale = TradeActions.SellAll(u, me, buyer);
        Assert.True(sale.Revenue > 0f && u.PlayerWallet.Credits > before);
        // funktionsfähig: keine Notreparatur mehr
        me.Hull = me.MaxHull;
        Assert.False(RepairService.NeedsEmergency(u, me));
        Assert.False(svc.Execute(u, yard, me, new ServiceRequest("repair:emergency", default, 0f)).Success);
    }

    [Fact]
    public void ReparaturAlles_BeiGeldmangel_ZuerstFunktionswichtigeModule()
    {
        var u = Standard();
        new SimulationRunner().Run(u, 1);
        var yard = Yard(u);
        var me = u.Ships[u.PlayerShip];
        DockAt(u, me, yard);
        int engine = me.Fit.FindIndex(k => k is not null && k.StartsWith("chem_engine"));
        ModuleDamage.SetCondition(u, me, engine, 0.1f);
        me.Hull = me.MaxHull * 0.3f;
        var plates = RepairService.PlatesGood(u)!.Value; var elec = RepairService.ElectronicsGood(u)!.Value;
        yard.Market[plates].Amount = 50f; yard.Market[elec].Amount = 50f;
        u.PlayerWallet.Credits = RepairService.PricePerModulePoint(u, yard, me, engine) * 0.5f;   // reicht nur für einen Teil des Moduls
        var r = new RepairService().Execute(u, yard, me, new ServiceRequest("repair:all", default, 0f));
        Assert.True(r.Success, r.Message);
        Assert.True(me.ConditionOf(engine) > 0.1f);
        Assert.Equal(me.MaxHull * 0.3f, me.Hull, 2);   // das Geld ging ins Modul, nicht in die Hülle
    }

    [Fact]
    public void Upgrade_BeschaedigtesModul_ErstReparieren()
    {
        var u = Standard();
        new SimulationRunner().Run(u, 1);
        var yard = Yard(u);
        var me = u.Ships[u.PlayerShip];
        DockAt(u, me, yard);
        int engine = me.Fit.FindIndex(k => k is not null && k.StartsWith("chem_engine"));
        ModuleDamage.SetCondition(u, me, engine, 0.6f);
        var q = ShipUpgrade.Quotes(u, yard, me).FirstOrDefault(x => x.Kind == UpgradeKind.Module && x.Slot == engine);
        Assert.NotNull(q);
        Assert.Contains("erst reparieren", q!.Issue);
        float wallet = u.PlayerWallet.Credits;
        Assert.False(ShipUpgrade.Attempt(u, yard, me, q));
        Assert.Equal(0.6f, me.ConditionOf(engine), 3);
        Assert.Equal(wallet, u.PlayerWallet.Credits);
        Assert.False(new UpgradeService().Execute(u, yard, me, new ServiceRequest(q.Key, default, 0f)).Success);
    }

    [Fact]
    public void Verkaufspreis_MitSchadensabschlag_UndOfflineBrandGeloescht()
    {
        var u = Standard();
        var s = NewShip(u, "pip", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        float full = Hangar.SellPrice(u, s);
        Assert.True(full > 0f);
        s.Hull = s.MaxHull * 0.5f;
        int reactor = s.Fit.FindIndex(k => k is not null && k.StartsWith("reactor"));
        ModuleDamage.SetCondition(u, s, reactor, 0.4f);
        int n = s.Fit.Count(k => k is not null);
        float expect = full * 0.5f * ((n - 1) + 0.4f) / n;
        Assert.Equal(expect, Hangar.SellPrice(u, s), 1);
        // kein lohnender Kreislauf „Schiff zerstören → bergen“: ein Wrack gibt höchstens die Module mit ≥ 50 % Restzustand und halbe Fracht zurück,
        // der Neuwert des Schiffs (Bausatz + Module) geht verloren
        // Offline: ein Brand am aktiven Schiff wird vor dem Aufholen gelöscht
        PlayerFleet.Activate(u, s, default);
        ModuleDamage.Ignite(u, s, reactor);
        OfflineCatchUp.Run(u, new SimulationRunner(), TimeSpan.FromSeconds(10));
        Assert.Empty(s.Systems.Burning);
    }

    [Fact]
    public void Bergung_KeinLohnenderKreislauf_EigenesSchiffZerstoerenUndBergen()
    {
        // Obergrenze des Rückflusses: jedes Modul überlebt (Chance 1) mit vollem Zustand, die halbe Fracht — der Bausatz und die Arbeit sind immer weg
        var u = Standard();
        u.Rules.WreckModuleChance = 1f;
        var s = NewShip(u, "prospector", ShipRole.Player, u.PlayerActor, 0f, Controller.Player);
        float shipValue = Hangar.MaterialValue(u, s);
        Departures.RemoveShip(u, s, DepartureReason.Destroyed, "test");
        var w = Assert.Single(u.Wrecks);
        float back = w.Modules.Where(m => m.Condition >= u.Rules.SalvageMinCondition).Sum(m => u.FindGood(m.Key)!.BasePrice);
        Assert.True(back < shipValue, $"Rückfluss {back:0} < Neuwert {shipValue:0}");
    }

    [Fact]
    public void Ruecklage_ImAufbau_KeinVerstoss_DanachVerstoss()
    {
        // Gerätetest: die Fraktionskassen beginnen leer — in der Aufbauzeit ist „Kasse unter Rücklage“ kein Verstoß und die Anzeige ruhig
        var u = Standard();
        var union = u.Actors[new ActorId(1)];
        Assert.True(union.Treasury.Credits < FactionViability.ReserveOf(u, union));
        Assert.DoesNotContain(FactionViability.Check(u, union.Id), i => i.Kind == ViabilityIssueKind.TreasuryBelowReserve);
        Assert.StartsWith("handlungsfähig – Rücklage im Aufbau", FactionViability.Text(u, union.Id));
        // nach der Aufbauzeit bleibt eine unterschrittene Rücklage ein Verstoß (Balance-Messung), die Anzeige bleibt sachlich
        u.Tick = u.Rules.FactionReserveBuildTicks;
        union.Treasury.Credits = 0f;
        Assert.Contains(FactionViability.Check(u, union.Id), i => i.Kind == ViabilityIssueKind.TreasuryBelowReserve);
        Assert.StartsWith("handlungsfähig – Rücklage unterschritten", FactionViability.Text(u, union.Id));
        union.Treasury.Credits = FactionViability.ReserveOf(u, union) + 1f;
        Assert.Equal("handlungsfähig", FactionViability.Text(u, union.Id));
    }

    [Fact]
    public void Testszenario_StartetMitVorbereiteterLage()
    {
        var u = WorldBuilder.BuildDefault(null, "test");
        var me = u.Ships[u.PlayerShip];
        Assert.Equal("tinker", me.ModelKey);
        Assert.Contains("wreck_grapple_s", me.Fit);
        Assert.True(me.Has(Capability.Salvage) && me.Has(Capability.Combat));
        int engine = me.Fit.IndexOf("chem_engine_m");
        Assert.True(me.ConditionOf(engine) < 0.5f && me.Systems.IsBurning(engine));
        var yard = u.Stations.Values.Where(s => s.TypeKey == "shipyard" && s.Sector == me.Sector).OrderBy(s => Vec2.Distance(s.Position, me.Position)).First();
        Assert.True(Vec2.Distance(yard.Position, me.Position) < 60f);
        foreach (var svc in new[] { "repair", "outfitter", "shipdealer", "rearm" }) Assert.Contains(svc, u.StationTypes[yard.TypeKey].Services);
        var w = Assert.Single(u.Wrecks);
        Assert.Null(Wrecks.ReachIssue(u, me, w));
        Assert.Contains(u.Ships.Values, s => ThreatMap.IsRaider(s) && s.Sector == me.Sector && Vec2.Distance(s.Position, me.Position) < 100f);
        Assert.True(me.Cargo[u.FindGood("ammo")!.Id] > 0f);
        Assert.Contains(WorldBuilder.DefaultStartOptions(), o => o.Key == "test" && o.Name.Contains("Test"));
        new SimulationRunner().Run(u, 200);   // läuft
    }

    // ───────── Langläufe ─────────

    [Fact]
    public void Langlauf_Standardwelt_40000Ticks() => LongRun(Standard(), "Standardwelt");

    [Fact]
    public void Langlauf_Classic_40000Ticks() => LongRun(TestWorlds.Classic(), "Classic");

    static void LongRun(Universe u, string name)
    {
        float loot = 0f, revenue = 0f;
        int repairs = 0;
        u.Events.Raised += e =>
        {
            switch (e)
            {
                case RaidResolved rr: loot += rr.LootValue; break;
                case TradeCompleted tc: revenue += tc.Revenue; break;
                case MineDelivered md: revenue += md.Revenue; break;
                case ShipRepaired: repairs++; break;
            }
        };
        var runner = new SimulationRunner();
        for (int chunk = 0; chunk < 40; chunk++)
        {
            runner.Run(u, 1000);
            foreach (var s in u.Ships.Values)
            {
                Assert.False(s.Order is GoToOrder && s.Transit is null, $"{name}: Geister-GoTo bei {s.Id.Value}");
                Assert.True(s.UsedVolume(u) <= s.CargoCapacity + 0.01f, $"{name}: Schiff {s.Id.Value} über dem Laderaum");
                Assert.True(s.Hull > 0f);
                foreach (int b in s.Systems.Burning) Assert.True(b < s.Fit.Count && s.Fit[b] is not null);
            }
            foreach (var w in u.Wrecks) Assert.True(w.ExpiresTick > u.Tick - 50 && !w.IsEmpty);
        }
        Assert.True(revenue > 0f && loot / revenue < 0.01f, $"{name}: Verlustquote {100 * loot / revenue:0.00} %");
        Assert.True(repairs > 0, $"{name}: keine Reparatur");
        foreach (var f in u.Factions.Keys)
            Assert.DoesNotContain(FactionViability.Check(u, f), i => i.Kind is ViabilityIssueKind.PoliceBelowMinimum or ViabilityIssueKind.NoSeat);
        var trade = ShipJobs.ByKey("trade")!; var mine = ShipJobs.ByKey("mine")!;
        Assert.True(ShipJobs.UsedUnits(u, trade) >= 0.8f * trade.Limit(u.Rules), $"{name}: Handel {ShipJobs.UsedUnits(u, trade):0.0}");
        Assert.True(ShipJobs.UsedUnits(u, mine) >= 0.8f * mine.Limit(u.Rules), $"{name}: Bergbau {ShipJobs.UsedUnits(u, mine):0.0}");
        // beschädigte KI-Schiffe zerfallen nicht: schwer beschädigte (unter der Suchschwelle) reparieren — fast alle Händler/Schürfer liegen darüber
        var eco = u.Ships.Values.Where(s => s.IsEconomyAi).ToList();
        int serious = eco.Count(s => NpcRepair.Serious(u, s));
        Assert.True(serious <= 0.1f * eco.Count, $"{name}: {serious} von {eco.Count} Schiffen schwer beschädigt");
    }
}
