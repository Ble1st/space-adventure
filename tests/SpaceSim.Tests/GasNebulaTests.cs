using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Gasernte und Nebel (docs/SCHIFFSSLOTS.md „Gasernte und Nebel“): Gasfelder, Gassammler (eine Regel für Spieler und KI), Gasraffinerie → Ionengas,
/// Ionentriebwerke, Schilde mit Gas, Nebelwirkungen (Tarnung, Überfälle, Abfangen, Schildregeneration), Speichern/Laden, Schalter aus = alter Hash.
/// </summary>
public class GasNebulaTests
{
    static readonly SectorId Borealis = new(5), Hephaistos = new(7), Alpha = new(1);
    static readonly string[] Keys = { "gas", "ion_gas", "gas_scoop_s", "ion_engine_s", "ion_engine_m" };

    /// <summary>
    /// Alles dieser Runde zurücknehmen (Referenz-Hash-Tests; Teil der Rücknahmekette über <see cref="BalanceFixTests.RevertBalanceFix"/>): Waren, Module,
    /// Gas in den Schildkosten, Gasraffinerie (Typ und Stationen), Nebel und Gasfelder, Gasschöpfer-Gründer, Regeln.
    /// </summary>
    internal static void RevertGas(WorldDef def)
    {
        FusionTankTests.RevertFusion(def);   // Fusionsbrennstoff kam danach
        StationsverschleissTests.RevertWear(def);   // Ausbau Punkt 8 (Stationsverschleiß) kam danach
        def.Goods.RemoveAll(g => g.Key is "gas" or "ion_gas");
        def.Modules.RemoveAll(m => Keys.Contains(m.Key));
        foreach (var m in def.Modules) { m.Cost.Remove("gas"); m.Upgrade?.Cost.Remove("gas"); }
        def.StationTypes.RemoveAll(t => t.Key == "gas_refinery");
        foreach (var t in def.StationTypes) t.Recipes.RemoveAll(r => r.In.ContainsKey("gas"));   // Gasweg der Treibstoffraffinerie
        foreach (var t in def.StationTypes)
        {
            if (t.Stocks is not { } st) continue;
            foreach (var k in Keys) st.Remove(k);
            if (st.Count == 0) t.Stocks = null;
        }
        foreach (var s in def.Sectors) { s.Nebula = false; s.GasFields.Clear(); s.Stations.RemoveAll(x => x.Type == "gas_refinery"); }
        def.Ships.RemoveAll(g => g.Model == "skimmer");
        def.ShipModels.RemoveAll(m => m.Key == "skimmer");
        var r = def.Rules ??= new EconomyRules();
        r.MaxCollectors = 0; r.NebulaEffects = false;
        if (r.MaxStations == 71) r.MaxStations = 69;
    }

    static Universe World(ulong seed = 1, bool fuel = true) => WorldBuilder.Build(TestWorlds.Def(fuel: fuel, research: false), seed);

    static void Place(Universe u, Ship s, SectorId sector, Vec2 pos)
    {
        if (s.Transit is not null) u.FinishTransit(s);
        s.Order = null; s.Phase = TradePhase.Idle; s.MinePhase = MinePhase.Idle;
        if (s.Sector != sector) u.MoveShip(s, sector);
        s.Position = pos;
        s.DockedAt = default;
    }

    static Ship PlayerSkimmer(Universe u)
    {
        var at = u.Stations.Values.First(s => s.Sector == Borealis && s.TypeKey == "gas_refinery");
        var m = u.ShipModels["skimmer"];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        u.AddShip(s);
        s.DockedAt = default;
        return s;
    }

    [Fact]
    public void Inhalt_GasIonengasNebelGasfelderRaffinerienSchilde()
    {
        var u = World();
        var gas = u.FindGood("gas")!; var ion = u.FindGood("ion_gas")!;
        Assert.Equal(GoodForm.Liquid, gas.Form); Assert.Equal(GoodForm.Liquid, ion.Form);
        Assert.Equal(DemoUniverse.Gas, gas.Id); Assert.Equal(DemoUniverse.IonGas, ion.Id);
        var neb = u.Sectors.Values.Where(s => s.Nebula).Select(s => s.Id.Value).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 5, 7, 8, 11 }, neb);   // feste, mit Seed 45 gewürfelte Auswahl: Asteroiden, Schwerindustrie, Planet, Piratensektor
        Assert.All(neb, id => Assert.NotEmpty(u.Sectors[new SectorId(id)].GasFields));
        Assert.Contains(u.Sectors.Values, s => s.Nebula && s.Planet is not null);
        Assert.Equal(2, u.Stations.Values.Count(s => s.TypeKey == "gas_refinery"));
        var r = u.StationTypes["gas_refinery"].Recipes.Single();
        Assert.Equal(2f, r.Inputs[gas.Id]); Assert.Equal(1f, r.Outputs[ion.Id]);
        Assert.Contains(gas.Key, u.Modules["shield_mk1"].Cost.Keys);
        Assert.Contains(gas.Key, u.Modules["shield_m"].Cost.Keys);
        Assert.Equal(6, u.Ships.Values.Count(s => s.Role == ShipRole.Collector));
        // Gasfelder liegen in der Sektorausdehnung
        foreach (var s in u.Sectors.Values) foreach (var f in s.GasFields) Assert.True(f.Position.Length < s.Extent);
    }

    [Fact]
    public void Ionentriebwerk_ReichweiteZweieinhalbfach_SchubZwanzigProzentWeniger()
    {
        var u = World();
        var chemS = u.Modules["chem_engine_s"]; var ionS = u.Modules["ion_engine_s"];
        var chemM = u.Modules["chem_engine_m"]; var ionM = u.Modules["ion_engine_m"];
        Assert.Equal(0.8f * chemS.Stats["thrust"], ionS.Stats["thrust"], 3);
        Assert.Equal(0.8f * chemM.Stats["thrust"], ionM.Stats["thrust"], 3);
        var ion = u.FindGood("ion_gas")!;
        Assert.Equal(ion.Id, Assert.Single(ionS.FuelTypes).Good);
        var me = u.Ships[u.PlayerShip];
        float chem = Refuel.Consumption(me, 1000f);
        int engine = me.Slots.FindIndex(s => s.Type == "engine");
        me.Fit[engine] = "ion_engine_s";
        me.RecomputeStats(u);
        Assert.Empty(me.Fuel);   // Treibstoff, den das Ionentriebwerk nicht verbrennt, wird abgelassen
        me.Fuel[ion.Id] = 50f;
        float ionUse = Refuel.Consumption(me, 1000f, ion.Id);
        Assert.InRange(chem / ionUse, 2.4f, 2.6f);
    }

    [Fact]
    public void Gassammler_Spieler_SchoepftNurImFeld_BisKanisterVoll()
    {
        var u = World();
        var me = PlayerSkimmer(u);
        var gas = u.FindGood("gas")!;
        var field = u.Sectors[Borealis].GasFields[0];
        Place(u, me, Borealis, field.Position + new Vec2(field.Radius + 50f, 0f));
        Assert.Equal(GasState.NoField, GasHarvest.State(u, me, me.Position));
        Assert.Equal(0f, GasHarvest.Step(u, me, me.Position, 10f));
        me.Position = field.Position;
        Assert.Equal(GasState.Collecting, GasHarvest.State(u, me, me.Position));
        int events = 0; u.Events.Raised += e => { if (e is GasCollected) events++; };
        float got = GasHarvest.Step(u, me, me.Position, 2f);
        Assert.Equal(me.Stats.GasCollect * 2f, got, 3);
        Assert.Equal(got, me.Cargo[gas.Id], 3);
        Assert.Equal(1, events);
        for (int i = 0; i < 200; i++) GasHarvest.Step(u, me, me.Position, 1f);
        Assert.Equal(GasState.Full, GasHarvest.State(u, me, me.Position));
        Assert.True(me.Cargo[gas.Id] <= me.CapacityVolumeFor(GoodForm.Liquid) + 1e-3f);
        // ohne Kanister: kein Platz; ohne Sammler: kein Werkzeug
        var miner = u.Ships[u.PlayerShip];
        Assert.Equal(GasState.NoTool, GasHarvest.State(u, miner, field.Position));
    }

    [Fact]
    public void Gassammler_KI_SchoepfenUndLiefernAnDieRaffinerie_RaffinerieMachtIonengas()
    {
        var u = World();
        var gas = u.FindGood("gas")!; var ion = u.FindGood("ion_gas")!;
        float collected = 0f, delivered = 0f;
        u.Events.Raised += ev =>
        {
            if (ev is GasCollected e && u.Ships.TryGetValue(e.Ship, out var s) && s.Controller == Controller.Ai) collected += e.Units;
            if (ev is MineDelivered d && d.Good == gas.Id) delivered += d.Amount;
        };
        var runner = new SimulationRunner();
        float ionBefore = u.Stations.Values.Where(s => s.TypeKey == "gas_refinery").Sum(s => s.Market[ion.Id].Amount);
        runner.Run(u, 6000);
        Assert.True(collected > 100f, $"geschöpft {collected}");
        Assert.True(delivered > 50f, $"geliefert {delivered}");
        // Gassammler schöpfen nur in Gasfeldern, laden nie über den Raum
        foreach (var s in u.Ships.Values.Where(s => s.Role == ShipRole.Collector))
            Assert.True(s.Cargo.GetValueOrDefault(gas.Id) <= s.CapacityVolumeFor(GoodForm.Liquid) + 1e-3f);
        Assert.True(u.Stations.Values.Where(s => s.TypeKey == "gas_refinery").Sum(s => s.Market[ion.Id].Amount) > 0f || ionBefore > 0f);
    }

    [Fact]
    public void Nebel_StaerktTarnung_DaempftUeberfallAbfangenSchild_SchalterAusOhneWirkung()
    {
        var u = World();
        Assert.True(Nebula.In(u, Borealis)); Assert.False(Nebula.In(u, Alpha));
        Assert.Equal(u.Rules.NebulaRaidFactor, Nebula.RaidFactor(u, Borealis));
        Assert.Equal(1f, Nebula.RaidFactor(u, Alpha));
        Assert.Equal(u.Rules.NebulaInterceptFactor, Nebula.InterceptFactor(u, Hephaistos));
        var me = u.Ships[u.PlayerShip];
        me.Cloaked = true;
        me.Stats.Cloak = 1.5f;
        Place(u, me, Alpha, new Vec2(0, 0));
        Assert.False(Cloak.HiddenFrom(u, me, 2f));   // Polizeisensor 2 schlägt Mk1 außerhalb
        Place(u, me, Borealis, new Vec2(0, 0));
        Assert.True(Cloak.HiddenFrom(u, me, 2f));    // im Nebel +1
        // Schildregeneration langsamer
        var s = new ShipStats { PowerOutput = 20, Shield = 100, ShieldRegen = 2f };
        var a = new ShipSystemsState { ShieldMissing = 50 }; var b = new ShipSystemsState { ShieldMissing = 50 };
        ShipSystems.Advance(s, a, 5f, false, u.Rules);
        ShipSystems.Advance(s, b, 5f, false, u.Rules, Nebula.ShieldRegenFactor(u, Borealis));
        Assert.True(b.ShieldMissing > a.ShieldMissing);
        // Schalter aus: kein Unterschied
        u.Rules.NebulaEffects = false;
        Assert.False(Cloak.HiddenFrom(u, me, 2f));
        Assert.Equal(1f, Nebula.RaidFactor(u, Borealis));
        Assert.Equal(1f, Nebula.ShieldRegenFactor(u, Borealis));
    }

    [Fact]
    public void SpeichernLaden_MitNebelGasfeldernUndSammlern_Bitgleich()
    {
        var u = World(3);
        var runner = new SimulationRunner();
        runner.Run(u, 2500);
        Assert.Contains(u.Ships.Values, s => s.Role == ShipRole.Collector && s.Order is MineOrder);
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        Assert.True(loaded.Sectors[Borealis].Nebula);
        Assert.Single(loaded.Sectors[Borealis].GasFields);
        runner.Run(u, 800);
        new SimulationRunner().Run(loaded, 800);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Schalter_Aus_NebelUndSammlerKostenNichts_WeltOhneGasHashtWieVorher()
    {
        // Welt mit Gasinhalt, aber Nebel aus und ohne Sammler, hasht bis auf Nebel/Gasfelder wie zuvor: hier geprüft, dass das Zurücknehmen den Stand vor
        // der Gasernte exakt wiederherstellt (Referenz 20000 Ticks der Standardwelt davor).
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        RevertGas(def);
        var u = WorldBuilder.Build(def);
        new SimulationRunner().Run(u, 20000);
        Assert.Equal(0x8EEEBEA81B038A74UL, UniverseHash.Compute(u));
    }
}
