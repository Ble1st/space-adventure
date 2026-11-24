using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Ausbau Punkt 4 — NPC-Bergung (docs/KAMPF.md „NPC-Bergung“): Bergeschiff-Modell „Bergeschiff“ (salvager), KI-Aufgabe „salvage“ mit Grenze
/// maxSalvagers und Schalter npcSalvage, ausführendes <see cref="SalvageSystem"/> (Intervall 10): KI-Bergeschiffe holen Fracht und Module aus Wracks
/// und verkaufen sie (Module an Werft/Ausrüster über <see cref="SpaceSim.Core.Trading.TradeActions.BestBuyerFor"/>, Ladung an den besten Abnehmer). Schalter aus = nichts passiert;
/// Speichern/Laden bit-identisch.
/// </summary>
public class BergungTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    static readonly SectorId Alpha = new(1);

    /// <summary>
    /// Alles dieser Runde zurücknehmen (Referenz-Hash-Tests; Teil der Rücknahmekette über <see cref="BalanceFixTests.RevertBalanceFix"/> und
    /// <see cref="GasNebulaTests.RevertGas"/>): Schalter aus, Grenze 0, Bergeschiff-Modell und -Gründer entfernt.
    /// </summary>
    internal static void RevertSalvage(WorldDef def)
    {
        def.ShipModels.RemoveAll(m => m.Key == "salvager");
        def.ShipClasses.RemoveAll(c => c.Key == "salvage_tug");   // eigene Klasse Bergeschlepper
        def.Ships.RemoveAll(g => g.Model == "salvager");
        var r = def.Rules ??= new EconomyRules();
        r.NpcSalvage = false; r.MaxSalvagers = 0;
    }

    static Universe World(ulong seed = 1) => WorldBuilder.Build(TestWorlds.Def(fuel: false, research: false), seed);

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

    /// <summary>Wrack mit Fracht und einem bergbaren Modul im Sektor.</summary>
    static Wreck WrackAt(Universe u, SectorId sector, Vec2 pos, float food = 10f)
    {
        var w = Wrecks.Drop(u, sector, pos, new Dictionary<GoodId, float> { [u.FindGood("food")!.Id] = food }, default, "Wrack: Test")!;
        w.Kind = WreckKind.Wreck;
        w.Modules.Add(new WreckModule { Key = "reactor_mk1", Condition = 0.8f });
        return w;
    }

    [Fact]
    public void Schalter_CodeAus_StandardweltAn_RuecknahmeAus()
    {
        var c = new EconomyRules();
        Assert.False(c.NpcSalvage);
        Assert.Equal(0f, c.MaxSalvagers);
        var r = World().Rules;
        Assert.True(r.NpcSalvage);
        Assert.Equal(4f, r.MaxSalvagers);
        // Schalter aus allein genügt: die Aufgabe hat dann Grenze 0 (die Flotte kauft keine Bergeschiffe)
        r.NpcSalvage = false;
        Assert.Equal(0f, ShipJobs.ByKey(SalvageJob.JobKey)!.Limit(r));
        // Rücknahme: Modell weg, Regeln aus
        var def = TestWorlds.Def(fuel: false, research: false);
        RevertSalvage(def);
        Assert.DoesNotContain(def.ShipModels, m => m.Key == "salvager");
        Assert.DoesNotContain(def.Ships, g => g.Model == "salvager");
        Assert.False(def.Rules!.NpcSalvage);
        Assert.Equal(0f, def.Rules.MaxSalvagers);
        var u = WorldBuilder.Build(def, 1);
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        Assert.DoesNotContain(ShipDealerService.Models(u, yard), m => m.Key == "salvager");
    }

    [Fact]
    public void Inhalt_BergeschiffModell_Aufgabe_undWerftVerkauftEs()
    {
        var u = World();
        var m = u.ShipModels["salvager"];
        Assert.Equal("Bergeschiff", m.Name);
        Assert.Equal("salvage_tug", m.ClassKey);
        Assert.Equal(new[] { "salvage" }, m.Jobs);
        var cls = u.ShipClasses[m.ClassKey];
        Assert.Equal("Bergeschlepper", cls.Name);
        Assert.Equal("ship_kit_medium", cls.Kit);
        var stats = ShipStats.Compute(cls, m.Fit.Select(k => k is not null ? u.Modules[k] : null).ToList());
        Assert.True((stats.Capabilities & Capability.Salvage) != 0);   // Bergungsgreifer im Werks-Fit
        Assert.True((stats.Capabilities & Capability.Cargo) != 0);
        Assert.True((stats.Capabilities & Capability.Combat) != 0);   // jeder Händler trägt eine Waffe
        Assert.True(stats.Cargo >= 200f);   // großer universeller Laderaum (2 × 100)
        Assert.Contains("tractor_beam_s", m.Fit);
        Assert.Contains("armor_plating_s", m.Fit);
        Assert.Null(FitRules.Check(u.SlotTypes, u.Modules, cls, cls.Slots, m.Fit));   // Leistungsbilanz gültig
        Assert.InRange(stats.Speed, 5.5f, 7.7f);   // zwischen Lastkahn und Maultier
        var job = ShipJobs.ByKey("salvage");
        Assert.NotNull(job);
        Assert.Equal(ShipRole.Salvager, job!.Role);
        Assert.Equal(Capability.Salvage, job.Needs);
        Assert.Contains(ShipJobs.Default, j => j.Key == "salvage");
        // Werft (alle Klassen) verkauft es, die Handelsstation (nur Skiffs) nicht
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        Assert.Contains(ShipDealerService.Models(u, yard), x => x.Key == "salvager");
        var trade = u.StationsById().First(s => s.TypeKey == "trade_station");
        Assert.DoesNotContain(ShipDealerService.Models(u, trade), x => x.Key == "salvager");
        // Rolle leitet die Fähigkeiten ab
        Assert.Equal(Capability.Cargo | Capability.Salvage, CapabilityDefaults.ForRole(ShipRole.Salvager));
    }

    [Fact]
    public void KiBergeschiff_HoltModulUndFrachtAusWrack_VerkauftAnAbnehmer()
    {
        var u = World();
        u.Rules.UniverseEvents = false;   // Universumsereignisse (danach): ein Wrackfeld anderswo lenkt das Bergeschiff vom Testwrack ab
        var runner = new SimulationRunner();
        runner.Run(u, 10);   // Markt warm laufen lassen (Preise)
        var salvager = NewShip(u, "salvager", ShipRole.Salvager, u.FreeActor, 2000f);
        Place(u, salvager, Alpha, new Vec2(55f, 0f));
        var w = WrackAt(u, Alpha, new Vec2(50f, 0f));
        var moduleGood = u.FindGood("reactor_mk1")!.Id;
        var ev = Capture(u);
        runner.Run(u, 4000);
        // Wrack geleert und entfernt
        Assert.DoesNotContain(u.Wrecks, x => x.Id == w.Id);
        // Modul geborgen (Zustand 0,8 ≥ Schwelle) und Fracht gezogen
        Assert.Contains(ev.OfType<WreckSalvaged>(), e => e.Wreck == w.Id && e.Module == "reactor_mk1");
        Assert.Contains(ev.OfType<WreckSalvaged>(), e => e.Wreck == w.Id && e.Module is null);
        // Beute verkauft: Modulware ging als MineDelivered an eine Station (Werft/Ausrüster), Laderaum am Ende leer
        Assert.Contains(ev.OfType<MineDelivered>(), e => e.Ship == salvager.Id && e.Good == moduleGood && e.Amount >= 1f && e.Revenue > 0f);
        Assert.True(salvager.Cargo.Values.All(v => v <= 0.001f));
    }

    [Fact]
    public void SchalterAus_NichtsPassiert()
    {
        var u = World();
        u.Rules.NpcSalvage = false;
        var salvager = NewShip(u, "salvager", ShipRole.Salvager, u.FreeActor, 2000f);
        Place(u, salvager, Alpha, new Vec2(55f, 0f));
        var w = WrackAt(u, Alpha, new Vec2(50f, 0f));
        var ev = Capture(u);
        new SimulationRunner().Run(u, 500);
        // Wrack unberührt, Schiff untätig (kein Auftrag, keine Reise, keine Bergung)
        Assert.Contains(u.Wrecks, x => x.Id == w.Id);
        Assert.Single(w.Modules);
        Assert.Equal(10f, w.Cargo[u.FindGood("food")!.Id], 3);
        Assert.Empty(salvager.Cargo);
        Assert.Null(salvager.Transit);
        Assert.Null(salvager.Order);
        Assert.DoesNotContain(ev.OfType<WreckSalvaged>(), e => e.Ship == salvager.Id);
    }

    [Fact]
    public void Bergung_UeberlebtSpeichernUndLaden_BitIdentisch()
    {
        var u = World();
        var runner = new SimulationRunner();
        runner.Run(u, 50);
        var salvager = NewShip(u, "salvager", ShipRole.Salvager, u.FreeActor, 2000f);
        Place(u, salvager, Alpha, new Vec2(55f, 0f));
        var w = WrackAt(u, Alpha, new Vec2(50f, 0f), 7.5f);
        runner.Run(u, 20);   // Bergeschiff unterwegs/am Bergen (Bergeschlepper: Wrack nach 120 Ticks schon leer)
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        Assert.Equal(u.Wrecks.Count, loaded.Wrecks.Count);   // das Wrack kann schon geborgen sein (Bergeschiff 5 Einheiten daneben)
        var ls = loaded.Ships[salvager.Id];
        Assert.Equal(ShipRole.Salvager, ls.Role);
        Assert.Equal(salvager.Order?.GetType(), ls.Order?.GetType());
        // Weiterlaufen bit-identisch
        runner.Run(u, 600);
        new SimulationRunner().Run(loaded, 600);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }
}
