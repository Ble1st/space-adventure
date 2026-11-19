using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 9 (Wunsch des Nutzers „Tanker umsetzen und Kurier“): Cistern (Tanker, nur Flüssiges) und Swift (Kurier, Feststoff, schnell) handeln für die KI-Flotte,
/// beide mit Laser („jeder Händler bewaffnet“); die Modellwahl wertet Formräume nach dem ungedeckten Bedarf ihrer Form.
/// </summary>
public class TankerCourierTests
{
    static Universe Standard() => TestWorlds.Default(null, fuel: false, research: false);

    static ShipStats StatsOf(Universe u, ShipModel m) =>
        ShipStats.Compute(u.ShipClasses[m.ClassKey], m.Fit.Select(k => k is null ? null : u.Modules[k]).ToList());

    static Ship AiShip(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var cls = u.ShipClasses[m.ClassKey];
        var s = ShipPurchase.Create(u, u.NewShipId(), model, cls.Slots, m.Fit, ShipRole.Trader, Controller.Ai, u.FreeActor, at, 8000f);
        u.AddShip(s);
        return s;
    }

    [Fact]
    public void Inhalt_TankerUndKurier_HandelnBewaffnet_TempoWieVorher()
    {
        var u = Standard();
        foreach (var key in new[] { "cistern", "swift" })
        {
            var m = u.ShipModels[key];
            var cls = u.ShipClasses[m.ClassKey];
            Assert.Contains("trade", m.Jobs);
            var st = StatsOf(u, m);
            Assert.True(st.WeaponPower > 0f && st.Capabilities.HasFlag(Capability.Combat) && st.Capabilities.HasFlag(Capability.Cargo), key);
            Assert.Null(FitRules.Check(u.SlotTypes, u.Modules, cls, cls.Slots, m.Fit));   // Leistungsbilanz gültig
            Assert.Equal("weapon", cls.Slots[^1].Type);
            // Tempo wie ohne Laser und mit der alten Grundmasse (Klasse um die 2 des Lasers leichter)
            var old = new ShipClass { Key = cls.Key, Size = cls.Size, BaseHull = cls.BaseHull, BaseMass = cls.BaseMass + 2f, BaseFuel = cls.BaseFuel,
                Slots = cls.Slots.Take(cls.Slots.Count - 1).ToList(), Kit = cls.Kit };
            var oldStats = ShipStats.Compute(old, m.Fit.Take(m.Fit.Count - 1).Select(k => k is null ? null : u.Modules[k]).ToList());
            Assert.Equal(oldStats.Speed, st.Speed, 3);
        }
        var tanker = StatsOf(u, u.ShipModels["cistern"]);
        Assert.Equal(360f, tanker.Cargo);          // sechs Kanisterräume à 60 (Runde 9: Spezialraum doppelt so groß wie 30)
        Assert.Equal(360f, tanker.CargoLiquid);
        Assert.Equal(0f, tanker.CargoSolid + tanker.CargoAny);
        var courier = StatsOf(u, u.ShipModels["swift"]);
        Assert.Equal(30f, courier.CargoSolid);
        Assert.True(courier.Speed > 11f, $"Kurier schnell ({courier.Speed:0.0})");
        Assert.Equal(new[] { "gas", "hydrogen", "ion_gas", "propellant", "water" },   // Gasernte: Nebelgas und Ionengas sind flüssig (Tanker fahren sie)
            u.Goods.Values.Where(g => g.Form == GoodForm.Liquid).Select(g => g.Key).OrderBy(k => k));
        Assert.Equal(3.6f, ShipJobs.Default.First(j => j.Key == "trade").Units(tanker, tanker.Cargo, u.Rules), 3);
    }

    /// <summary>Der Tanker plant und fährt nur Flüssiges und bleibt nie mit Feststoff stehen; der Kurier nie Flüssiges.</summary>
    [Fact]
    public void Tanker_FaehrtNurFluessiges_KurierNurFeststoff()
    {
        var u = Standard();
        var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
        var tanker = AiShip(u, "cistern", yard);
        var courier = AiShip(u, "swift", yard);
        var ev = new List<SimEvent>();
        u.Events.Raised += ev.Add;
        var runner = new SimulationRunner();
        var tankerGoods = new HashSet<string>(); var courierGoods = new HashSet<string>();
        for (int i = 0; i < 12000; i++)
        {
            runner.Step(u);
            if (u.Ships.TryGetValue(tanker.Id, out var t))
            {
                foreach (var g in t.Cargo.Where(kv => kv.Value > 0.001f)) Assert.Equal(GoodForm.Liquid, u.Goods[g.Key].Form);
                if (t.Order is TradeOrder to) tankerGoods.Add(u.Goods[to.Good].Key);
            }
            if (u.Ships.TryGetValue(courier.Id, out var c))
            {
                foreach (var g in c.Cargo.Where(kv => kv.Value > 0.001f)) Assert.Equal(GoodForm.Solid, u.Goods[g.Key].Form);
                if (c.Order is TradeOrder co) courierGoods.Add(u.Goods[co.Good].Key);
            }
        }
        Assert.NotEmpty(tankerGoods);
        Assert.All(tankerGoods, k => Assert.Equal(GoodForm.Liquid, u.FindGood(k)!.Form));
        Assert.All(courierGoods, k => Assert.Equal(GoodForm.Solid, u.FindGood(k)!.Form));
        Assert.Contains(ev, e => e is TradeCompleted tc && tc.Ship == tanker.Id);
    }

    /// <summary>
    /// Modellwahl: Formräume zählen nur bis zum ungedeckten Bedarf ihrer Form. Mit großem Flüssigbedarf kauft die KI den Tanker (bester Wert je Credit), ohne
    /// Flüssigbedarf nicht; Modelle nur mit Universalräumen sind unberührt (Faktor 1).
    /// </summary>
    [Fact]
    public void Modellwahl_TankerBeiFluessigBedarf_SonstNicht()
    {
        var u = Standard();
        new SimulationRunner().Run(u, 5);
        var job = ShipJobs.Default.First(j => j.Key == "trade");
        var tanker = StatsOf(u, u.ShipModels["cistern"]);
        Assert.Equal(1f, job.DemandFactor(u, StatsOf(u, u.ShipModels["unit_trader_100"])));
        var buyer = u.ShipsById().First(s => s.Role == ShipRole.Trader && s.Controller == Controller.Ai);
        buyer.Credits = 50000f;
        foreach (var st in u.StationsById().Where(s => s.TypeKey == "shipyard"))
            if (u.FindGood("canister_bay_s") is { } cb && st.Market.TryGetValue(cb.Id, out var e)) e.Amount = 12f;
        // großer Flüssigbedarf: alle Flüssig-Einträge der Abnehmer leer
        var liquids = u.Goods.Values.Where(g => g.Form == GoodForm.Liquid).Select(g => g.Id).ToHashSet();
        foreach (var st in u.Stations.Values)
            foreach (var (g, e) in st.Market)
                if (liquids.Contains(g) && !st.Produces(g)) { e.Amount = 0f; e.Target = MathF.Max(e.Target, 200f); }
        Assert.True(TradeJob.Unserved(u).Liquid >= tanker.Cargo);
        Assert.Equal(1f, job.DemandFactor(u, tanker), 3);
        var pick = FleetSystem.ChooseModel(u, job, buyer, used: 10f, limit: 26f);
        Assert.Equal("cistern", pick?.Model.Key);
        // kein Flüssigbedarf: Abnehmer voll
        foreach (var st in u.Stations.Values)
            foreach (var (g, e) in st.Market)
                if (liquids.Contains(g) && !st.Produces(g)) e.Amount = e.Target;
        Assert.Equal(0f, TradeJob.Unserved(u).Liquid);
        Assert.Equal(0f, job.DemandFactor(u, tanker));
        Assert.NotEqual("cistern", FleetSystem.ChooseModel(u, job, buyer, used: 10f, limit: 26f)?.Model.Key);
    }
}
