using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 18: Frachtreste (Abwerfen als Ausweg, Resttoleranz im Verkauf, Mining-Autopilot ohne Restschleife), Wasserversorgung der Farmen (Lebenszyklus
/// folgt der Kette, keine Werften/Modulwerke für Flottenwaren, Startwelt mit zweiter Farm, Wasserwerk 2 je Takt) und Rohstoffbasis (Konsumgüterfabrik
/// 2 je Takt, Platin im seltenen Brocken, Eisen im Metallasteroiden).
/// </summary>
public class CargoWaterOreTests
{
    static GoodId G(Universe u, string key) => u.FindGood(key)!.Id;

    // ───────── Frachtreste ─────────

    [Fact]
    public void Abwerfen_SpielerschiffAuchImFlug_NpcNicht()
    {
        var u = DemoUniverse.Create();
        var player = u.Ships[u.PlayerShip];
        var ore = DemoUniverse.Ore;
        player.Cargo.Clear();
        player.Cargo[ore] = 3.4f;
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;

        Assert.Equal(1f, CargoJettison.Jettison(u, player, ore, 1f));
        Assert.Equal(2.4f, player.Cargo[ore], 4);
        Assert.Equal(2.4f, CargoJettison.Jettison(u, player, ore, 0f), 4);   // ≤ 0 = alles
        Assert.False(player.Cargo.ContainsKey(ore));
        var ev = events.OfType<CargoJettisoned>().ToList();
        Assert.Equal(2, ev.Count);
        Assert.Equal(player.Sector, ev[1].Sector);

        // nichts an Bord / NPC: nichts passiert, kein Ereignis
        Assert.Equal("Nichts an Bord", CargoJettison.Issue(player, ore));
        Assert.Equal(0f, CargoJettison.Jettison(u, player, ore, 1f));
        var npc = u.Ships.Values.First(s => !s.IsPlayer && s.Has(Capability.Cargo));
        npc.Cargo[ore] = 5f;
        Assert.NotNull(CargoJettison.Issue(npc, ore));
        Assert.Equal(0f, CargoJettison.Jettison(u, npc, ore, 0f));
        Assert.Equal(5f, npc.Cargo[ore]);
        Assert.Equal(2, events.OfType<CargoJettisoned>().Count());
    }

    [Fact]
    public void Handelsdienst_Abwerfen_Strikt_MitGrundUndEreignis()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        var trade = new TradeService();
        var st = u.Stations[new StationId(1)];   // Stahlwerk Alpha: nimmt Eisenerz, handelt keine Nahrung
        var player = u.Ships[u.PlayerShip];
        player.Cargo.Clear();
        var food = DemoUniverse.Food;
        player.Cargo[food] = 2.5f;
        player.Cargo[DemoUniverse.Ore] = 4f;

        var offers = trade.Offers(u, st, player);
        var drops = offers.Where(o => o.Key.StartsWith(TradeService.Drop + ":")).ToList();
        Assert.Equal(new[] { $"drop:{DemoUniverse.Ore.Value}", $"drop:{food.Value}" }.OrderBy(k => k), drops.Select(o => o.Key).OrderBy(k => k));
        var dropFood = drops.Single(o => o.Good == food);
        Assert.Equal(2.5f, dropFood.Amount);
        Assert.Equal("Handelt diese Ware nicht", dropFood.Note);              // Grund wie in der Verkaufszeile
        Assert.Null(drops.Single(o => o.Good == DemoUniverse.Ore).Note);      // Eisenerz nimmt die Station ganz

        // strenge Schlüssel und Ware nicht an Bord: nichts ändert sich, kein Ereignis
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        foreach (var bad in new[] { "drop", "drop:", "drop:0", "drop:x", "drop:-3", $"drop:{food.Value}:1", $"drop:{G(u, "steel").Value}" })
            Assert.False(trade.Execute(u, st, player, new ServiceRequest(bad, default, 0f)).Success, bad);
        Assert.Empty(events);
        Assert.Equal(2.5f, player.Cargo[food]);

        float wallet = u.WalletOf(player).Credits;
        var r = trade.Execute(u, st, player, new ServiceRequest($"drop:{food.Value}", default, 1f));
        Assert.True(r.Success);
        Assert.Equal(1f, r.Units);
        Assert.Equal(1.5f, player.Cargo[food], 4);
        Assert.Equal(wallet, u.WalletOf(player).Credits);   // ohne Erlös
        Assert.Single(events.OfType<CargoJettisoned>());
        Assert.Single(events.OfType<ServicePerformed>(), e => e.Offer == TradeService.Drop);
        Assert.True(trade.Execute(u, st, player, new ServiceRequest($"drop:{food.Value}", default, 0f)).Success);
        Assert.False(player.Cargo.ContainsKey(food));

        // NPC-Schiff: abgelehnt
        var npc = u.Ships.Values.First(s => !s.IsPlayer && s.Has(Capability.Cargo));
        npc.Cargo[food] = 1f;
        Assert.False(trade.Execute(u, st, npc, new ServiceRequest($"drop:{food.Value}", default, 0f)).Success);
        Assert.Equal(1f, npc.Cargo[food]);
    }

    [Fact]
    public void MiningAutopilot_RestUnterEinerEinheit_KeineSchleifeKeinStopp()
    {
        var u = DemoUniverse.Create();
        var ship = u.Ships[u.PlayerShip];
        var sector = u.Sectors[u.PlayerSector];
        sector.Asteroids.RemoveAll(a => a.Minable);   // hier gibt es nichts mehr abzubauen
        ship.Cargo.Clear();
        var propellant = DemoUniverse.Propellant;     // Treibstoff aus: niemand handelt ihn
        Assert.DoesNotContain(u.Stations.Values, s => s.Market.ContainsKey(propellant));
        ship.Cargo[propellant] = 0.4f;
        var auto = new MiningAutopilot();
        auto.Start();
        var step = auto.Step(u, new OreChunks(), ship, ship.Position);
        Assert.True(auto.Enabled, auto.Status);                     // vorher: „Kein Käufer erreichbar“ → gestoppt
        Assert.NotNull(step.Nav);                                    // fliegt zum nächsten Feld
        Assert.Null(step.DockAt);
        Assert.Equal(MiningAutopilot.Phase.Seek, auto.Current);
        // angedockt: der Rest wird nicht verkauft, stoppt die Automatik aber nicht
        auto.OnDocked(u, ship, u.Stations[new StationId(1)]);
        Assert.True(auto.Enabled, auto.Status);
        // eine ganze Einheit, die niemand nimmt, ist Ladung: dann stoppt sie wie bisher
        ship.Cargo[propellant] = 2f;
        auto.OnDocked(u, ship, u.Stations[new StationId(1)]);
        Assert.False(auto.Enabled);
    }

    // ───────── Wasserversorgung / Lebenszyklus ─────────

    static Dictionary<GoodId, float> AllAt(Universe u, float ratio) => u.Goods.Values.ToDictionary(g => g.Id, _ => ratio);

    [Fact]
    public void Lebenszyklus_FolgtDerKette_FehlendesVorproduktZuerst()
    {
        var u = DemoUniverse.Create();
        var food = DemoUniverse.Food;
        float limit = u.Rules.BuildPriceRatio;
        var ratios = AllAt(u, 1f);
        ratios[food] = 5f;
        Assert.Equal("farm", StationLifecycleSystem.UpstreamProducer(u, food, ratios, limit, limit)!.Key);
        // Wasser knapp: das Wasserwerk statt einer zweiten Farm
        ratios[G(u, "water")] = 3f;
        Assert.Equal("water_plant", StationLifecycleSystem.UpstreamProducer(u, food, ratios, limit, limit)!.Key);
        // Energie knapper als Wasser: die knappste Zutat zuerst
        ratios[G(u, "energy")] = 4f;
        Assert.Equal("powerplant", StationLifecycleSystem.UpstreamProducer(u, food, ratios, limit, limit)!.Key);   // günstigste Energiequelle; ihr Wasserstoff ist nicht knapp
        ratios[G(u, "energy")] = 1f;
        // Eis (Erz ohne baubaren Hersteller) für das Wasserwerk knapp: nichts bauen — der Engpass liegt beim Bergbau
        ratios[G(u, "ice")] = 3f;
        Assert.Null(StationLifecycleSystem.UpstreamProducer(u, food, ratios, limit, limit));
        // Regel aus (Phase 17): die Farm
        ratios[G(u, "ice")] = 1f;
        Assert.Equal("farm", StationLifecycleSystem.UpstreamProducer(u, food, ratios, limit)!.Key);
        // Kette über zwei Glieder: Konsumgüter → Elektronik → Kupfer
        ratios[G(u, "water")] = 1f;
        ratios[G(u, "consumer_goods")] = 5f;
        ratios[G(u, "electronics")] = 3f;
        ratios[G(u, "copper")] = 4f;
        Assert.Equal("copper_smelter", StationLifecycleSystem.UpstreamProducer(u, G(u, "consumer_goods"), ratios, limit, limit)!.Key);
    }

    [Fact]
    public void Flottenwaren_LoesenKeinenBauAus_SolangeEsEineHerstellerinGibt()
    {
        var u = DemoUniverse.Create();
        Assert.True(StationLifecycleSystem.FleetGoodWithProducer(u, G(u, "ship_kit_light")));
        Assert.True(StationLifecycleSystem.FleetGoodWithProducer(u, G(u, "reactor_mk1")));
        Assert.False(StationLifecycleSystem.FleetGoodWithProducer(u, G(u, "steel")));          // Rezeptzutat
        Assert.False(StationLifecycleSystem.FleetGoodWithProducer(u, DemoUniverse.Food));      // Exportware
        // ohne Herstellerin (Werft entfernt) darf eine Werft entstehen
        foreach (var yard in u.Stations.Values.Where(s => s.TypeKey == "shipyard").ToList()) u.RemoveStation(yard.Id, "test");
        Assert.False(StationLifecycleSystem.FleetGoodWithProducer(u, G(u, "ship_kit_light")));

        // Lebenszyklus: Bausätze am knappsten, Nahrung knapp — gebaut wird für die Nahrung, keine zweite Werft
        u = DemoUniverse.Create();
        u.Rules.BuildCooldown = 0;
        foreach (var st in u.Stations.Values) foreach (var (g, e) in st.Market) e.Amount = e.Target;
        foreach (var st in u.Stations.Values) foreach (var (g, e) in st.Market)
            if (g == G(u, "ship_kit_light")) e.Amount = 0f; else if (g == DemoUniverse.Food) e.Amount = e.Target / 3f;
        foreach (var st in u.Stations.Values) st.Credits = 100_000f;
        var started = new List<ConstructionStarted>();
        u.Events.Raised += e => { if (e is ConstructionStarted c) started.Add(c); };
        new StationLifecycleSystem().Step(u, 1);
        Assert.Equal("farm", Assert.Single(started).TypeKey);
        // Regel aus: wie bis Phase 17 die Werft
        u = DemoUniverse.Create();
        u.Rules.BuildCooldown = 0; u.Rules.BuildFollowsChain = false;
        foreach (var st in u.Stations.Values) foreach (var (g, e) in st.Market)
            e.Amount = g == G(u, "ship_kit_light") ? 0f : g == DemoUniverse.Food ? e.Target / 3f : e.Target;
        foreach (var st in u.Stations.Values) st.Credits = 100_000f;
        started.Clear();
        u.Events.Raised += e => { if (e is ConstructionStarted c) started.Add(c); };
        new StationLifecycleSystem().Step(u, 1);
        Assert.Equal("shipyard", Assert.Single(started).TypeKey);
    }

    [Fact]
    public void Startwelt_ZweiteFarm_WasserwerkUndKonsumgueterfabrikMitDoppeltemTakt_ErzMix()
    {
        var u = DemoUniverse.Create();
        var farms = u.Stations.Values.Where(s => s.TypeKey == "farm").ToList();
        Assert.True(farms.Count >= 2);   // Phase 21: die 11-Sektoren-Welt hat vier Farmen
        var beta = farms.Single(f => f.Id == new StationId(40));
        Assert.Equal("Farm Beta", beta.Name);
        Assert.True(beta.BankruptcyProtected);
        // Treibstoff aus: −2 Raffinerien gegenüber der Weltdatei; Spielraum für den Lebenszyklus mindestens 4 Plätze (Phase 21: größere Welt, mehr Plätze)
        Assert.Equal(WorldBuilder.Parse(WorldBuilder.DefaultWorldJson()).Rules!.MaxStations - 2, u.Rules.MaxStations);
        Assert.True(u.Rules.MaxStations - StationProtection.CountEconomic(u) >= 4);
        var water = u.StationTypes["water_plant"].Recipes.Single();
        Assert.Equal(2f, water.Outputs[G(u, "water")]);
        Assert.Equal(2f, water.Inputs[G(u, "ice")]);
        var cg = u.StationTypes["goods_factory"].Recipes.Single();
        Assert.Equal(2f, cg.Outputs[G(u, "consumer_goods")]);
        Assert.Equal(2f, cg.Inputs[G(u, "steel")]);
        Assert.Equal(2f, cg.Inputs[G(u, "electronics")]);
        float Share(string cls, string good) { var c = u.AsteroidClasses[cls]; return c.Yields.Where(y => y.Good == G(u, good)).Sum(y => y.Weight) / c.Yields.Sum(y => y.Weight); }
        Assert.Equal(0.4f, Share("rare_rock", "platinum_ore"), 3);
        Assert.Equal(3.5f / 10f, Share("metal_rock", "iron_ore"), 3);
        Assert.Equal(1.5f / 10f, Share("metal_rock", "titanium_ore"), 3);
    }

    /// <summary>
    /// Phase 18: unveränderte Standardwelt (Schalter aus), 20000 Ticks — beide Farmen bekommen Wasser, der Nahrungsexport bricht nicht ein, der Lebenszyklus
    /// baut keine Werft und kein Modulwerk (Flottenwaren), Konsum- und Luxusexport laufen; Preise bleiben über dem unteren Deckel.
    /// </summary>
    [Fact]
    public void Standardwelt_20000Ticks_WasserFuerBeideFarmen_KeineFlottenwarenWerke()
    {
        var u = DemoUniverse.Create();
        var exported = new Dictionary<GoodId, float>();
        var built = new List<string>();
        u.Events.Raised += e =>
        {
            if (e is GoodsExported x) exported[x.Good] = exported.GetValueOrDefault(x.Good) + x.Amount;
            if (e is ConstructionStarted c) built.Add(c.TypeKey);
        };
        var water = G(u, "water");
        var farms = u.Stations.Values.Where(s => s.TypeKey == "farm").Select(s => s.Id).ToList();
        int samples = 0, dry = 0;
        var sim = new SimulationRunner();
        for (int t = 0; t < 20_000; t += 20)
        {
            sim.Run(u, 20);
            if (u.Tick < 2000) continue;
            foreach (var f in farms) { samples++; if (u.Stations[f].Market[water].Amount < 1f) dry++; }
        }
        Assert.True(dry < samples * 0.15f, $"Farmen ohne Wasser {dry}/{samples}");
        Assert.True(exported.GetValueOrDefault(DemoUniverse.Food) > 5000f, $"Nahrungsexport {exported.GetValueOrDefault(DemoUniverse.Food):0}");
        Assert.True(exported.GetValueOrDefault(G(u, "consumer_goods")) > 900f, $"Konsumgüterexport {exported.GetValueOrDefault(G(u, "consumer_goods")):0}");
        Assert.True(exported.GetValueOrDefault(G(u, "luxury_goods")) > 200f, $"Luxusexport {exported.GetValueOrDefault(G(u, "luxury_goods")):0}");
        Assert.DoesNotContain(built, k => k is "shipyard" or "drive_works" or "weapons_works" or "systems_works");
    }
}
