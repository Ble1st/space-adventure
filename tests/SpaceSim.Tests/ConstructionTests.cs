using System.Text.Json.Nodes;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 8: Stationsbau über Baustellen (<see cref="ConstructionRules"/>, <see cref="ConstructionSystem"/>), Bauschiffe, Notbau, abgeleitete
/// Kritikalität und <c>BankruptcyProtected</c>, zyklenfreie Baukosten, Asteroidenbrecher (<see cref="AsteroidSplitter"/>), Lagerhaus
/// (<see cref="StorageService"/>), Save v11 mit Migration.
/// </summary>
public class ConstructionTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    internal static readonly string[] Phase8Modules = { "construction_rig_s", "split_laser_s" };

    /// <summary>
    /// Demotet einen aktuellen Save (v11) auf v10: Baustellen und Lagerhaus (Typen und Stationen, Stationsobergrenze zurück), Bauschiffe und
    /// Bauaufträge, Bauausleger und Spaltlaser (Waren, Rezepte, Lager), Bauschiff- und Brecherklasse samt Modellen, Dienst „storage“, Spielerlager,
    /// Notbau-Zeitstempel und die Phase-8-Regeln entfallen; <c>BankruptcyProtected</c> heißt wieder <c>Essential</c>.
    /// </summary>
    internal static readonly string[] Phase8Rules = { "ConstructionSites", "ConstructionSiteType", "ConstructionMaterials", "ConstructionMaterialShare", "ConstructionPriceFactor", "ConstructionMaxBuyFactor",
        "ConstructionAssemblyTicks", "ConstructionFeeShare", "ConstructionExpiryTicks", "MaxActiveSites", "MaxBuilders", "EmergencyBuildTicks",
        "EmergencyCriticalFactor", "EmergencyBuildMarkup", "EmergencyBuildCooldown", "CriticalGoods", "FragmentLimitFactor", "FragmentMinRadius",
        "SplitDustLoss", "NpcSplitMinRadius", "StorageFeePerUnit", "StorageCapacity" };

    /// <summary>Entfernt die Phase-8-Regeln (in älteren Ständen gab es sie nicht; beim Laden gelten die Standards).</summary>
    internal static void RemovePhase8Rules(JsonObject node)
    {
        foreach (var key in Phase8Rules) node["Rules"]!.AsObject().Remove(key);
    }

    internal static string ToV10Json(string v11Json)
    {
        var node = JsonNode.Parse(ActorTests.ToV11Json(v11Json))!.AsObject();   // Phase 9: erst die Akteure zurückbauen
        node["Version"] = 10;
        var types = node["StationTypes"]!.AsArray();
        foreach (var t in types.ToList())
        {
            if ((string)t!["Key"]! is "build_site" or "warehouse") { types.Remove(t); continue; }
            t.AsObject().Remove("Construction");
            if (t["Services"] is JsonArray sv) foreach (var x in sv.ToList()) if ((string)x! == "storage") sv.Remove(x);
        }
        var stations = node["Stations"]!.AsArray();
        var removed = new HashSet<int>();
        int warehouses = 0;
        foreach (var s in stations.ToList())
        {
            string type = (string)s!["Type"]!;
            if (type is "build_site" or "warehouse")
            {
                removed.Add((int)s["Id"]!);
                if (type == "warehouse") warehouses++;
                stations.Remove(s);
                continue;
            }
            var o = s.AsObject();
            o["Essential"] = (bool)o["BankruptcyProtected"]!;
            o.Remove("BankruptcyProtected"); o.Remove("Site"); o.Remove("PlayerStorage");
        }
        foreach (var sec in node["Sectors"]!.AsArray())
        {
            var list = sec!["Stations"]!.AsArray();
            foreach (var x in list.ToList()) if (removed.Contains((int)x!)) list.Remove(x);
        }
        node["Rules"]!["MaxStations"] = (int)node["Rules"]!["MaxStations"]! - warehouses;
        var ships = node["Ships"]!.AsArray();
        foreach (var sh in ships.ToList())
        {
            if ((string)sh!["Role"]! == "Builder") { ships.Remove(sh); continue; }
            if (sh["Order"] is JsonObject o && ((string)o["Kind"]! == "build"
                || (string)o["Kind"]! == "trade" && (removed.Contains((int)o["A"]!) || removed.Contains((int)o["B"]!))
                || (string)o["Kind"]! == "goto" && removed.Contains((int)o["A"]!)))
            { sh.AsObject()["Order"] = null; sh["Phase"] = "Idle"; sh["Transit"] = null; }
        }
        QualityUpgradeTests.RemoveModules(node, Phase8Modules);
        var classes = node["ShipClasses"]!.AsArray();
        foreach (var c in classes.ToList()) if ((string)c!["Key"]! is "construction_ship" or "breaker") classes.Remove(c);
        var models = node["ShipModels"]!.AsArray();
        foreach (var m in models.ToList()) if ((string)m!["Key"]! is "foreman" or "quarry" or "rockbreaker_ii") models.Remove(m);
        foreach (var f in node["Factions"]!.AsArray()) f!.AsObject().Remove("LastEmergencyBuildTick");
        RemovePhase8Rules(node);
        return node.ToJsonString();
    }

    // ───────── Helfer ─────────

    /// <summary>Standardwelt nach einem Tick (Preise gesetzt); der Lebenszyklus legt keine eigenen Baustellen an (Obergrenze = jetziger Stand + Reserve).</summary>
    static Universe World(int reserve = 0)
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        u.Rules.MaxStations = u.Stations.Count + reserve;
        return u;
    }

    static Station Site(Universe u, string type, int sector, FactionId? owner = null, float budget = 0f)
    {
        var sec = u.Sectors[new SectorId(sector)];
        var spot = StationLifecycleSystem.FindFreeSpot(u, sec)!.Value;
        var t = u.StationTypes[type];
        var site = ConstructionRules.StartSite(u, t, sec.Id, spot, owner ?? sec.Owner, budget > 0f ? budget : t.BuildCost, null, null);
        u.Rules.MaxStations = Math.Max(u.Rules.MaxStations, u.Stations.Count);
        return site;
    }

    /// <summary>
    /// Phase 15 (statt der Phase-14-Krücke BuildCooldown = int.MaxValue): Nur die Baustellen des Tests laufen — die Regel für gleichzeitige Baustellen
    /// (<see cref="EconomyRules.MaxActiveSites"/>) steht auf ihrer Zahl, der Lebenszyklus legt also keine weiteren an; die Notbau-Bremse der Fraktion gilt
    /// damit nur für die Baustellen des Tests. Schon angelegte Baustellen verschwinden.
    /// </summary>
    static void OnlyTestSites(Universe u, int sites)
    {
        u.Rules.MaxActiveSites = sites;
        foreach (var id in u.Stations.Values.Where(s => s.IsBuildSite).Select(s => s.Id).ToList()) u.RemoveStation(id, "test");
    }

    static void RemoveBuilders(Universe u)
    {
        foreach (var id in u.Ships.Values.Where(s => s.Role == ShipRole.Builder).Select(s => s.Id).ToList()) u.RemoveShip(id, "test");
    }

    // ───────── Inhalte ─────────

    [Fact]
    public void Content_BaustelleLagerhausBauschiffBrecher_UndStartbestandJeFraktion()
    {
        var u = DemoUniverse.Create();
        Assert.Empty(u.StationTypes["build_site"].Recipes);
        Assert.Equal(new[] { "trade" }, u.StationTypes["build_site"].Services);
        Assert.Equal(new[] { "trade", "storage", "hangar", "taxi" }, u.StationTypes["warehouse"].Services);   // Phase 12
        var wh = u.Stations.Values.First(s => s.TypeKey == "warehouse");
        Assert.True(wh.BankruptcyProtected);
        Assert.All(new[] { "steel", "hull_plates", "electronics" }, k => Assert.True(wh.IsStockOnly(u.FindGood(k)!.Id)));
        Assert.Contains(Capability.Construction, u.Modules["construction_rig_s"].Grants);
        Assert.True(u.Modules["split_laser_s"].Stats["splitPower"] > 0f);
        // Module sind Waren mit Rezept im Systemwerk
        var sys = u.Stations.Values.First(s => s.TypeKey == "systems_works");
        foreach (var key in Phase8Modules) Assert.Contains(sys.Production, r => r.Outputs.ContainsKey(u.FindGood(key)!.Id));
        // Foreman: Bauschiff mit Frachtraum, Aufgabe „build“; Brecher-Modelle mit Spaltlaser
        var foreman = u.ShipModels["foreman"];
        Assert.Equal(new List<string> { "build" }, foreman.Jobs);
        Assert.Equal("ship_kit_medium", u.ShipClasses["construction_ship"].Kit);
        Assert.Equal(new List<string> { "mine" }, u.ShipModels["rockbreaker_ii"].Jobs);
        Assert.Empty(u.ShipModels["quarry"].Jobs);
        // je KI-Fraktion ein KI-Bauschiff mit Fähigkeit Construction (und keinem Laser); Runde 38: die Spielerfraktion hat keins; Punkt 17: Banditen bauen nicht mit Bauschiff
        foreach (var f in u.NpcFactionIds().Where(f => !Bandits.IsEstablished(u, f)))
        {
            var b = Assert.Single(u.Ships.Values, s => s.IsAiBuilder && s.Owner == f);   // Phase 9: Bauschiffe gehören dem Fraktionsakteur
            Assert.Equal(Capability.Cargo | Capability.Construction, b.Capabilities);
            Assert.True(b.CargoCapacity >= 160f);   // 100 + 60 (Gründer können eine bessere Güteklasse würfeln)
            Assert.True(b.IsEconomyAi);
        }
        var quarry = new Ship { Id = u.NewShipId(), ModelKey = "quarry", Sector = u.PlayerSector, Fit = new List<string?>(u.ShipModels["quarry"].Fit) };
        quarry.RecomputeStats(u);
        Assert.True(quarry.Stats.SplitPower > 0f);
        Assert.True(quarry.Has(Capability.Mining));   // Phase 29: der Steinbruch trägt neben dem Spaltlaser einen Bohrkopf
    }

    [Fact]
    public void Baukosten_Zyklenfrei_JederTypErreichbar()
    {
        // SCHIFFE.md Abschnitt 10, Punkt 3: Kein Typ verlangt eine Ware, die nur er selbst herstellt; jedes Material hat einen anderen Hersteller-Typ,
        // der in der Startwelt steht (sonst bleibt nur der Notbau). Gilt für die Regel-Stückliste wie für eigene Listen.
        var u = DemoUniverse.Create();
        foreach (var t in u.StationTypes.Values.Where(t => t.Key != u.Rules.ConstructionSiteType))
        {
            var bill = ConstructionRules.Bill(u, t);
            Assert.NotNull(bill);
            Assert.NotEmpty(bill!);
            foreach (var g in bill!.Keys)
            {
                var producers = u.StationTypes.Values.Where(x => x.Recipes.Any(r => r.Outputs.ContainsKey(g))).Select(x => x.Key).ToList();
                Assert.DoesNotContain(t.Key, producers);
                Assert.Contains(producers, p => p != t.Key && u.Stations.Values.Any(s => s.TypeKey == p));
            }
        }
        // Stahlwerk braucht keinen Stahl, Panzerwerk keine Hüllenplatten, Elektronikfabrik keine Elektronik
        Assert.DoesNotContain(DemoUniverse.Steel, ConstructionRules.Bill(u, u.StationTypes["steelworks"])!.Keys);
        Assert.DoesNotContain(u.FindGood("hull_plates")!.Id, ConstructionRules.Bill(u, u.StationTypes["plating_works"])!.Keys);
        Assert.DoesNotContain(u.FindGood("electronics")!.Id, ConstructionRules.Bill(u, u.StationTypes["electronics_fab"])!.Keys);
        // Wert der Stückliste ≈ Anteil der Bausumme zum Basispreis
        var yard = u.StationTypes["shipyard"];
        float value = ConstructionRules.Bill(u, yard)!.Sum(kv => kv.Value * u.Goods[kv.Key].BasePrice);
        Assert.InRange(value, 0.45f * yard.BuildCost, 0.55f * yard.BuildCost);
    }

    [Fact]
    public void EigeneStueckliste_MitEigenemProdukt_Wirft()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.StationTypes.First(t => t.Key == "steelworks").Construction = new Dictionary<string, float> { ["steel"] = 10 };
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
        Assert.Contains("steelworks", ex.Message);
        def.StationTypes.First(t => t.Key == "steelworks").Construction = new Dictionary<string, float> { ["hull_plates"] = 3 };
        var u = WorldBuilder.Build(def);
        Assert.Equal(new Dictionary<GoodId, float> { [u.FindGood("hull_plates")!.Id] = 3f }, ConstructionRules.Bill(u, u.StationTypes["steelworks"]));
    }

    // ───────── Lebenszyklus legt Baustellen an ─────────

    [Fact]
    public void Lebenszyklus_LegtBaustelleAn_InvestorZahltTreuhand_HoechstensZweiGleichzeitig()
    {
        var u = DemoUniverse.Create();
        foreach (var st in u.Stations.Values) foreach (var e in st.Market.Values) e.Amount = e.Target;
        foreach (var st in u.Stations.Values) if (st.Market.TryGetValue(DemoUniverse.Food, out var f)) f.Amount = 0f;
        // Phase 9: Der Investor kommt aus einer gewichteten Ziehung — hier ist das Schiff der einzige, der Baukosten plus Reserve aufbringt
        foreach (var st in u.Stations.Values) st.Credits = 1000f;
        var investor = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        investor.Credits = 50_000f;
        new StationLifecycleSystem().Step(u, 1);

        var site = Assert.Single(u.Stations.Values, s => s.IsBuildSite);
        Assert.Equal("farm", site.Site!.TypeKey);
        Assert.Equal("build_site", site.TypeKey);
        float cost = u.StationTypes["farm"].BuildCost;
        Assert.Equal(cost, site.Credits);
        Assert.Equal(cost, site.Site.Budget);
        Assert.Equal(50_000f - cost, investor.Credits);
        Assert.Equal(investor.Id.Value, site.Site.InvestorShip);
        // Markt der Baustelle = Stückliste (Bestand 0, Wunschbestand = verlangt), nimmt genau die fehlende Menge an
        foreach (var (g, need) in site.Site.Required)
        {
            Assert.Equal(need, site.Market[g].Target);
            Assert.Equal(need, site.AcceptableUnits(g, u.Rules));
        }
        Assert.Empty(site.Production);
        // derselbe Typ wird nicht doppelt begonnen; höchstens MaxActiveSites
        u.LastStationBuildTick = -1_000_000;
        new StationLifecycleSystem().Step(u, 1);
        Assert.Single(u.Stations.Values, s => s.Site?.TypeKey == "farm");
        u.Rules.MaxActiveSites = 1;
        foreach (var st in u.Stations.Values.Where(s => !s.IsBuildSite)) if (st.Market.TryGetValue(DemoUniverse.Steel, out var e)) e.Amount = 0f;
        u.LastStationBuildTick = -1_000_000;
        new StationLifecycleSystem().Step(u, 1);
        Assert.Single(u.Stations.Values, s => s.IsBuildSite);
    }

    [Fact]
    public void OhneBaustellen_DirektbauWieFrueher()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Rules!.ConstructionSites = false;
        var u = WorldBuilder.Build(def);
        foreach (var st in u.Stations.Values) foreach (var e in st.Market.Values) e.Amount = e.Target;
        foreach (var st in u.Stations.Values) if (st.Market.TryGetValue(DemoUniverse.Food, out var f)) f.Amount = 0f;
        u.Ships.Values.First(s => s.Role == ShipRole.Trader).Credits = 50_000f;
        StationBuilt? built = null;
        u.Events.Raised += e => { if (e is StationBuilt b) built = b; };
        new StationLifecycleSystem().Step(u, 1);
        Assert.Equal("farm", built!.TypeKey);
        Assert.False(u.Stations[built.Station].IsBuildSite);
        Assert.DoesNotContain(u.Stations.Values, s => s.IsBuildSite);
        // Welt ohne Baustellen-Typ (alte Spielstände): ebenfalls Direktbau
        var u2 = DemoUniverse.Create();
        u2.StationTypes.Remove("build_site");
        Assert.False(ConstructionRules.UsesSites(u2, u2.StationTypes["farm"]));
    }

    // ───────── Bauschiff, Händler, Spieler ─────────

    [Fact]
    public void Bauschiff_HoltMaterial_LiefertUndMontiert_StationEntsteht()
    {
        var u = World();
        OnlyTestSites(u, 1);   // Runde 44: mit den größeren Lagerpuffern legte der Lebenszyklus schon im ersten Tick eine Baustelle an und band das Bauschiff der Union
        var site = Site(u, "farm", 1, budget: u.StationTypes["farm"].BuildCost * 2f);   // Gasernte: Treuhandgeld reicht auch bei teurem Stahl für die Platten
        // Gasernte (verschobener Lauf): Hüllenplatten waren gerade knapp (kein Lieferant unter der Preisgrenze), die Baustelle blockierte und bekam nach
        // 2000 Ticks den Notbau — hier geht es um den Weg über das Bauschiff (der Notbau hat eigene Tests): die Panzerwerke haben Platten auf Lager
        foreach (var pw in u.Stations.Values.Where(s => s.TypeKey == "plating_works" && s.Market.ContainsKey(DemoUniverse.HullPlates)))
            pw.Market[DemoUniverse.HullPlates].Amount = MathF.Max(pw.Market[DemoUniverse.HullPlates].Amount, 40f);
        var id = site.Id;
        var steelBefore = u.Stations.Values.Where(s => !s.IsBuildSite && s.Production.Any(r => r.Outputs.ContainsKey(DemoUniverse.Steel))).Sum(s => s.Market[DemoUniverse.Steel].Amount);
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        var runner = new SimulationRunner();
        // Runde 44: das Bauschiff, das die Baustelle übernimmt — in den vergrößerten Sektoren kann zuerst das Bauschiff einer anderen Fraktion denken, das keine
        // eigene Baustelle hat (Regel: eigene Fraktion zuerst, sonst die nächste); vorher stand hier fest das Bauschiff des Besitzers
        for (int i = 0; i < 400 && site.Site!.Builder.Value == 0; i++) runner.Step(u);
        var builder = u.Ships[site.Site!.Builder];
        Assert.True(builder.IsAiBuilder);
        float builderCredits = builder.Credits;
        for (int i = 0; i < 12000 && u.Stations[id].IsBuildSite; i++) runner.Step(u);

        var st = u.Stations[id];
        Assert.False(st.IsBuildSite);
        Assert.Equal("farm", st.TypeKey);
        Assert.NotEmpty(st.Production);
        Assert.InRange(st.Credits, ConstructionRules.NewStationCredits - 50f, ConstructionRules.NewStationCredits + 50f);   // Startkapital (plus Einnahmen seit der Fertigstellung)
        var done = Assert.Single(events.OfType<ConstructionCompleted>(), e => e.Station == id);
        Assert.False(done.Emergency);
        Assert.Equal(builder.Id, done.Builder);
        Assert.Contains(events.OfType<StationBuilt>(), e => e.Station == id && e.TypeKey == "farm");
        Assert.Contains(events.OfType<ConstructionDelivered>(), e => e.Site == id && e.By == "builder");
        Assert.DoesNotContain(events.OfType<EmergencyConstruction>(), e => e.Site == id);
        // Montagegebühr an das Bauschiff, Material kam aus der Wirtschaft (Hersteller haben verkauft)
        Assert.Contains(events.OfType<ConstructionDelivered>(), e => e.Site == id && e.Good == DemoUniverse.Steel);
        Assert.True(builder.Credits > builderCredits - 300f);   // Gebühr (900) deckt Treibstoff und Unterhalt
        Assert.True(steelBefore > 0f);
        Assert.Null(builder.Order is BuildOrder bo && bo.Site == id ? bo : null);
    }

    [Fact]
    public void Haendler_BeliefernBaustelle_UeberNormalenHandel()
    {
        var u = World();
        RemoveBuilders(u);
        u.Rules.EmergencyBuildTicks = 1_000_000;   // kein Notbau: nur Händler liefern
        u.Rules.CommissionBuilders = false;        // Phase 19: und die Fraktion kauft kein Bauschiff nach
        u.Rules.DockDwellTicks = 0; u.Rules.DockDwellTicksPlayer = 0;   // Liegezeit (danach) verschiebt den Weltpfad; geprüft wird die Händlerlieferung
        OnlyTestSites(u, 1);   // Runde 44: nur die Testbaustelle (der Lebenszyklus legte sonst im ersten Tick eigene an)
        var site = Site(u, "farm", 1);
        var delivered = new List<ConstructionDelivered>();
        u.Events.Raised += e => { if (e is ConstructionDelivered d && d.Site == site.Id) delivered.Add(d); };
        new SimulationRunner().Run(u, 12000);   // Runde 44: 12 000 statt 4000 Ticks — Fahrten in den vergrößerten Sektoren ≈ 3,4-mal so lang
        Assert.Contains(delivered, d => d.By == "trader");
        // Händler zahlen sich aus dem Treuhandgeld (Gebot = Basispreis × Faktor); nie mehr angenommen als verlangt
        var bs = u.Stations[site.Id].Site!;
        Assert.True(u.Stations[site.Id].Credits < bs.Budget);
        foreach (var g in bs.Required.Keys) Assert.Equal(u.Goods[g].BasePrice * u.Rules.ConstructionPriceFactor, u.Stations[site.Id].Market[g].Price, 2);
    }

    [Fact]
    public void Spieler_LiefertPerFreiemHandel_BekommtMarktpreis_KannNichtZurueckkaufen()
    {
        var u = World();
        var site = Site(u, "farm", 1);
        new SimulationRunner().Run(u, 1);   // Gebote der Baustelle setzen
        var player = u.Ships[u.PlayerShip];
        player.Position = site.DockPoint(player.Id);
        player.Cargo[DemoUniverse.Steel] = 10f;
        float wallet = u.PlayerWallet.Credits, escrow = site.Credits;
        var trade = new TradeService();
        ConstructionDelivered? ev = null;
        u.Events.Raised += e => { if (e is ConstructionDelivered d) ev = d; };
        var r = trade.Execute(u, site, player, new ServiceRequest($"sell:{DemoUniverse.Steel.Value}", default, 0f));
        Assert.True(r.Success);
        float price = u.Goods[DemoUniverse.Steel].BasePrice * u.Rules.ConstructionPriceFactor;
        Assert.Equal(10f * price, u.PlayerWallet.Credits - wallet, 2);
        Assert.Equal(-10f * price, site.Credits - escrow, 2);
        Assert.Equal(10f, site.Market[DemoUniverse.Steel].Amount);
        Assert.Equal("player", ev!.By);
        // eine laufende Baustelle verkauft nichts zurück
        var buy = trade.Execute(u, site, player, new ServiceRequest($"buy:{DemoUniverse.Steel.Value}", default, 1f));
        Assert.False(buy.Success);
        Assert.All(trade.Offers(u, site, player).Where(o => o.Key.StartsWith("buy:")), o => Assert.Equal(0f, o.Amount));
        // mehr als verlangt nimmt sie nicht an
        float need = site.Site!.Required[DemoUniverse.Steel];
        player.Cargo[DemoUniverse.Steel] = need + 50f;
        trade.Execute(u, site, player, new ServiceRequest($"sell:{DemoUniverse.Steel.Value}", default, 0f));
        Assert.Equal(need, site.Market[DemoUniverse.Steel].Amount, 2);
    }

    [Fact]
    public void BauschiffOhneBauausleger_WirdFreigegeben()
    {
        var u = World();
        OnlyTestSites(u, 1);   // Runde 44: siehe Bauschiff_HoltMaterial…
        var site = Site(u, "farm", 1);
        var runner = new SimulationRunner();
        runner.Run(u, 60);
        // Runde 44: das übernehmende Bauschiff (siehe Bauschiff_HoltMaterial…)
        var builder = u.Ships[site.Site!.Builder];
        Assert.True(builder.IsAiBuilder);
        builder.Fit[builder.Fit.IndexOf("construction_rig_s")] = null;
        builder.RecomputeStats(u);
        Assert.False(builder.Has(Capability.Construction));
        runner.Run(u, 2);
        Assert.NotEqual(builder.Id, site.Site!.Builder);
        Assert.False(builder.Order is BuildOrder);
    }

    // ───────── Notbau ─────────

    [Fact]
    public void Notbau_GreiftNachFrist_ErsetztNurFehlendes_BautOhneBauschiff()
    {
        var u = World();
        RemoveBuilders(u);
        OnlyTestSites(u, 1);   // Notbau-Bremse je Fraktion: keine Baustelle des Lebenszyklus darf den Notbau vorwegnehmen
        u.Rules.CommissionBuilders = false;   // Phase 19: die Fraktion kauft kein Bauschiff nach (geprüft wird der Bau ohne Bauschiff)
        var site = Site(u, "farm", 1);
        var id = site.Id;
        // ein Teil ist schon da: der Notbau ersetzt nur den Rest
        site.Stock(DemoUniverse.Steel).Amount = 5f;
        EmergencyConstruction? em = null;
        long emTick = -1;
        u.Events.Raised += e => { if (e is EmergencyConstruction ec && ec.Site == id) { em = ec; emTick = u.Tick; } };
        u.Rules.ConstructionPriceFactor = 0.01f;   // Händler liefern nicht (zu billig): der Notbau muss greifen
        var runner = new SimulationRunner();
        for (int i = 0; i < 3000 && u.Stations[id].IsBuildSite; i++) runner.Step(u);

        Assert.NotNull(em);
        Assert.False(em!.Critical);   // Farm: anderer Hersteller erreichbar, keine kritische Kette
        Assert.InRange(emTick, u.Rules.EmergencyBuildTicks, u.Rules.EmergencyBuildTicks + 25);
        var bill = ConstructionRules.Bill(u, u.StationTypes["farm"])!;
        float expectedUnits = bill.Sum(kv => kv.Value) - 5f;
        Assert.Equal(expectedUnits, em.Units, 2);
        float expectedCost = bill.Sum(kv => (kv.Value - (kv.Key == DemoUniverse.Steel ? 5f : 0f)) * u.Goods[kv.Key].BasePrice) * u.Rules.EmergencyBuildMarkup;
        Assert.Equal(expectedCost, em.Cost + em.Waived, 0);
        Assert.Equal(0f, em.Waived);   // die Bausumme deckt den Notbau
        Assert.Equal(emTick, u.Factions[u.FactionOf(site)!.Value].LastEmergencyBuildTick);
        Assert.False(u.Stations[id].IsBuildSite);   // ohne Bauschiff montiert
        Assert.Equal("farm", u.Stations[id].TypeKey);
    }

    [Fact]
    public void Notbau_Kritisch_Frueher_UndBremseJeFraktion()
    {
        var u = World();
        RemoveBuilders(u);
        u.Rules.CommissionBuilders = false;   // Phase 21: wie die anderen Notbau-Tests — die Fraktion kauft kein Bauschiff nach (geprüft wird der Bau ohne Bauschiff)
        OnlyTestSites(u, 2);   // Notbau-Bremse je Fraktion: nur die beiden Baustellen des Tests
        u.Rules.ConstructionPriceFactor = 0.01f;
        var steel = Site(u, "steelworks", 1);   // Stahl ist eine kritische Kette
        var farm = Site(u, "farm", 2);          // gleiche Fraktion (1), nicht kritisch
        Assert.True(StationCriticality.IsCriticalSite(u, steel));
        Assert.False(StationCriticality.IsCriticalSite(u, farm));
        var ticks = new Dictionary<StationId, long>();
        u.Events.Raised += e => { if (e is EmergencyConstruction ec) ticks[ec.Site] = u.Tick; };
        new SimulationRunner().Run(u, u.Rules.EmergencyBuildTicks / 2 + u.Rules.EmergencyBuildCooldown + 50);
        Assert.InRange(ticks[steel.Id], u.Rules.EmergencyBuildTicks / 2, u.Rules.EmergencyBuildTicks / 2 + 25);
        // die Farm wäre nach EmergencyBuildTicks dran, muss aber die Abkühlzeit der Fraktion abwarten
        Assert.True(ticks[farm.Id] - ticks[steel.Id] >= u.Rules.EmergencyBuildCooldown);
    }

    [Fact]
    public void Verfall_TreuhandZurueck_MaterialBleibt_NeuesProjektUebernimmt()
    {
        var u = World();
        var investor = u.Stations.Values.First(s => s.TypeKey == "solar_plant");
        float invCredits = investor.Credits;
        var type = u.StationTypes["farm"];
        var sec = u.Sectors[new SectorId(1)];
        var site = ConstructionRules.StartSite(u, type, sec.Id, StationLifecycleSystem.FindFreeSpot(u, sec)!.Value, sec.Owner, type.BuildCost, investor.Id, null);
        site.Stock(DemoUniverse.Steel).Amount = 7f;
        site.Credits -= 1000f;   // schon ausgegeben
        ConstructionRules.Expire(u, site);
        Assert.Equal(invCredits + type.BuildCost - 1000f, investor.Credits, 1);
        Assert.True(site.Site!.Abandoned);
        Assert.Equal(0f, site.AcceptableUnits(DemoUniverse.Steel, u.Rules));
        // verlassene Baustellen verkaufen ihr Lager (Spieler), laufende nicht
        new SimulationRunner().Run(u, 1);
        var player = u.Ships[u.PlayerShip];
        Assert.True(TradeActionsMax(u, player, site) > 0f);
        // ein neues Projekt im Sektor übernimmt Platz und Material
        var again = ConstructionRules.StartSite(u, u.StationTypes["goods_factory"], sec.Id, default, sec.Owner, 7000f, null, null);
        Assert.Same(site, again);
        Assert.False(again.Site!.Abandoned);
        Assert.Equal(7f, again.Market[DemoUniverse.Steel].Amount);
        // ohne Material verschwindet eine verfallene Baustelle
        var empty = Site(u, "farm", 3);
        ConstructionRules.Expire(u, empty);
        Assert.False(u.Stations.ContainsKey(empty.Id));
    }

    static float TradeActionsMax(Universe u, Ship player, Station st) => SpaceSim.Core.Trading.TradeActions.MaxBuyable(u, player, st, DemoUniverse.Steel);

    // ───────── Essential-Aufspaltung ─────────

    [Fact]
    public void Kritisch_Abgeleitet_LetzteHerstellerinGehtNichtPleite()
    {
        var u = World();
        // eine zusätzliche, ungeschützte Farm; die Startfarm wird zum Test entfernt → die neue ist die letzte Herstellerin von Nahrung
        var sec = u.Sectors[new SectorId(3)];
        var farm = StationFactory.Create(u, u.NewStationId(), u.StationTypes["farm"], "Testfarm", sec.Id, StationLifecycleSystem.FindFreeSpot(u, sec)!.Value, sec.Owner);
        u.AddStation(farm);
        Assert.False(farm.BankruptcyProtected);
        Assert.False(StationCriticality.IsCritical(u, farm));
        foreach (var other in u.Stations.Values.Where(s => s.TypeKey == "farm" && s.Id != farm.Id).ToList()) u.RemoveStation(other.Id, "test");
        Assert.True(StationCriticality.IsCritical(u, farm));
        farm.Credits = -10_000f;
        u.Rules.BankruptcyTicks = 1;
        var lc = new StationLifecycleSystem();
        lc.Step(u, 1); u.Tick += 5; lc.Step(u, 1);
        Assert.True(u.Stations.ContainsKey(farm.Id));   // kritisch: bleibt
        // nicht kritisch (zweite Farm daneben): wird aufgegeben
        var farm2 = StationFactory.Create(u, u.NewStationId(), u.StationTypes["farm"], "Testfarm 2", sec.Id, StationLifecycleSystem.FindFreeSpot(u, sec)!.Value, sec.Owner);
        farm2.BankruptcyProtected = true;
        u.AddStation(farm2);
        u.Tick += 5; lc.Step(u, 1);
        Assert.False(u.Stations.ContainsKey(farm.Id));
    }

    [Fact]
    public void BankruptcyProtected_JsonAliasEssential_UndAlterSaveFeldEssential()
    {
        var u = DemoUniverse.Create();
        Assert.All(u.Stations.Values, s => Assert.True(s.BankruptcyProtected));   // Startwelt: essential = vor Pleite geschützt
        var json = SaveGameJson.Write(u, Now);
        Assert.Contains("\"BankruptcyProtected\":true", json);
        Assert.DoesNotContain("\"Essential\"", json);
        // alter Stand: nur das Feld Essential
        var node = JsonNode.Parse(json)!.AsObject();
        foreach (var s in node["Stations"]!.AsArray()) { s!["Essential"] = true; s.AsObject().Remove("BankruptcyProtected"); }
        var (loaded, _) = SaveGameJson.Read(node.ToJsonString());
        Assert.All(loaded.Stations.Values, s => Assert.True(s.BankruptcyProtected));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    // ───────── Asteroidenbrecher ─────────

    static (Universe u, Sector s, ResourceField f, Asteroid rock) BigRock()
    {
        var u = World();
        var s = u.Sectors[new SectorId(1)];
        var f = s.Fields[0];
        var rock = Belt.AsteroidsOf(s, f).OrderByDescending(a => a.Radius).First();
        return (u, s, f, rock);
    }

    [Fact]
    public void Spalten_ZweiBisVierFragmente_ErzMinusStaub_FortlaufendeIds_ImGuertel()
    {
        var (u, s, f, rock) = BigRock();
        float ore = rock.Ore;
        int nextId = u.NextAsteroidId, before = Belt.AsteroidsOf(s, f).Count();
        AsteroidSplit? ev = null;
        u.Events.Raised += e => { if (e is AsteroidSplit a) ev = a; };
        var frags = AsteroidSplitter.Split(u, s, rock, default);
        Assert.NotNull(frags);
        Assert.InRange(frags!.Count, 2, 4);
        Assert.DoesNotContain(rock, s.Asteroids);
        Assert.Equal(Enumerable.Range(nextId, frags.Count), frags.Select(a => a.Id));
        Assert.Equal(ore * (1f - u.Rules.SplitDustLoss), frags.Sum(a => a.Ore), 2);
        Assert.Equal(ore * u.Rules.SplitDustLoss, ev!.Dust, 2);
        Assert.Equal(rock.Id, ev.Asteroid);
        Assert.All(frags, a =>
        {
            Assert.Equal(rock.ClassKey, a.ClassKey);
            Assert.Same(f, s.FieldFor(a.Position));
            Assert.True(a.BaseRadius >= u.Rules.FragmentMinRadius);
            Assert.True(a.BaseRadius < rock.BaseRadius);
        });
        Assert.Equal(before - 1 + frags.Count, Belt.AsteroidsOf(s, f).Count());
        // gleiche Welt, gleicher Seed → gleiche Spaltung
        var (u2, s2, _, rock2) = BigRock();
        var frags2 = AsteroidSplitter.Split(u2, s2, rock2, default)!;
        Assert.Equal(frags.Select(a => (a.Id, a.Position, a.Ore)), frags2.Select(a => (a.Id, a.Position, a.Ore)));
    }

    [Fact]
    public void Spalten_LimitJeGuertel_UndMindestgroesse()
    {
        var (u, s, f, rock) = BigRock();
        u.Rules.FragmentLimitFactor = Belt.AsteroidsOf(s, f).Count() / (float)f.TargetAsteroids;   // Gürtel ist genau am Limit
        Assert.Equal("Gürtel ist voll (Fragmentlimit)", AsteroidSplitter.Issue(u, s, rock));
        Assert.Null(AsteroidSplitter.Split(u, s, rock, default));
        Assert.Contains(rock, s.Asteroids);
        u.Rules.FragmentLimitFactor = 3f;
        rock.BaseRadius = 2f; rock.Ore = rock.MaxOre * 0.01f;   // zu klein
        Assert.Equal("Zu klein zum Spalten", AsteroidSplitter.Issue(u, s, rock));
        // Fire: ohne Spaltlaser oder außer Reichweite nicht
        var player = u.Ships[u.PlayerShip];
        Assert.Equal("Kein Spaltlaser an Bord", AsteroidSplitter.Fire(u, player, rock.Id).Issue);
    }

    [Fact]
    public void NpcBrecher_SpaltetGrosseBrocken_BeimSchuerfen()
    {
        var u = World();
        var s = u.Sectors[new SectorId(1)];
        var miner = u.Ships.Values.First(x => x.Role == ShipRole.Miner);
        var model = u.ShipModels["rockbreaker_ii"];
        miner.ModelKey = model.Key;
        miner.Slots.Clear();
        miner.Fit = new List<string?>(model.Fit);
        miner.RecomputeStats(u);
        miner.CargoCapacity = miner.Stats.Cargo;
        Assert.True(miner.Stats.SplitPower > 0f && miner.Has(Capability.Mining));
        int splits = 0;
        u.Events.Raised += e => { if (e is AsteroidSplit a && a.Ship == miner.Id) splits++; };
        new SimulationRunner().Run(u, 3000);
        Assert.True(splits > 0);
        Assert.All(u.Sectors.Values.SelectMany(sec => sec.Fields.Select(f => (sec, f))),
            x => Assert.True(Belt.AsteroidsOf(x.sec, x.f).Count() <= AsteroidSplitter.BeltLimit(u, x.f)));
    }

    [Fact]
    public void Anzeige_BaustellenStatus_UndSpalterSchacht()
    {
        var u = World();
        RemoveBuilders(u);
        var site = Site(u, "farm", 1);
        Assert.Null(ConstructionInfo.Of(u, u.Stations.Values.First(s => !s.IsBuildSite)));
        site.Stock(DemoUniverse.Steel).Amount = 10f;
        var info = ConstructionInfo.Of(u, site)!;
        Assert.Equal("Farm", info.TypeName);
        Assert.Equal("keins", info.Builder);
        float need = site.Site!.Required.Values.Sum();
        Assert.Equal(10f / need, info.Progress, 3);
        Assert.Equal(site.Site.Required.Count, info.Lines.Count);
        Assert.Equal("Wartet auf Material", info.State);
        new SimulationRunner().Run(u, 2);   // ohne Bauschiff blockiert: der Status nennt den Notbau
        Assert.StartsWith("Blockiert – Notbau in ", ConstructionInfo.Of(u, site)!.State);
        ConstructionRules.Expire(u, site);
        Assert.True(ConstructionInfo.Of(u, site)!.Abandoned);
        // Schächte: Spalter nur mit Spaltlaser
        var player = u.Ships[u.PlayerShip];
        Assert.DoesNotContain(SpaceSim.Core.Weapons.WeaponBays.For(player.Stats), b => b.Key == "split");
        player.Fit[player.Slots.FindIndex(x => x.Type == "tool")] = "split_laser_s";
        player.RecomputeStats(u);
        Assert.Contains(SpaceSim.Core.Weapons.WeaponBays.For(player.Stats), b => b.Key == "split");
    }

    // ───────── Lagerhaus ─────────

    [Fact]
    public void Lagerhaus_EinlagernAbholen_GebuehrKapazitaetStrengeSchluessel()
    {
        var u = World();
        var wh = u.Stations.Values.First(s => s.TypeKey == "warehouse");
        Assert.Contains(StationServices.Default().ForStation(u, wh), x => x.Key == "storage");
        var player = u.Ships[u.PlayerShip];
        player.Cargo[DemoUniverse.IronOre] = 40f;
        float wallet = u.PlayerWallet.Credits, stCredits = wh.Credits;
        var svc = new StorageService();
        var r = svc.Execute(u, wh, player, new ServiceRequest($"store:{DemoUniverse.IronOre.Value}", default, 30f));
        Assert.True(r.Success);
        Assert.Equal(30f, wh.PlayerStorage[DemoUniverse.IronOre]);
        Assert.Equal(10f, player.Cargo[DemoUniverse.IronOre]);
        Assert.Equal(30f * u.Rules.StorageFeePerUnit, wallet - u.PlayerWallet.Credits, 3);
        Assert.Equal(30f * u.Rules.StorageFeePerUnit, wh.Credits - stCredits, 3);
        var f = svc.Execute(u, wh, player, new ServiceRequest($"fetch:{DemoUniverse.IronOre.Value}", default, 0f));
        Assert.True(f.Success);
        Assert.Equal(40f, player.Cargo[DemoUniverse.IronOre]);
        Assert.False(wh.PlayerStorage.ContainsKey(DemoUniverse.IronOre));
        // Kapazität
        u.Rules.StorageCapacity = 5f;
        svc.Execute(u, wh, player, new ServiceRequest($"store:{DemoUniverse.IronOre.Value}", default, 0f));
        Assert.Equal(5f, wh.PlayerStorage[DemoUniverse.IronOre]);
        // strenge Schlüssel und nur der Spieler: nichts ändert sich, kein Event
        int events = 0;
        u.Events.Raised += _ => events++;
        foreach (var bad in new[] { "store", "store:", "store:x", "store:0", "fetch:-1", "keep:1", "store:1:2" })
            Assert.False(svc.Execute(u, wh, player, new ServiceRequest(bad, default, 0f)).Success);
        var npc = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        npc.Cargo[DemoUniverse.IronOre] = 5f;
        Assert.False(svc.Execute(u, wh, npc, new ServiceRequest($"store:{DemoUniverse.IronOre.Value}", default, 0f)).Success);
        Assert.Equal(0, events);
    }

    // ───────── Speichern, Laden, Migration ─────────

    [Fact]
    public void SaveRoundtrip_BaustellenLagerNotbau_BitIdentisch_UndDeterministisch()
    {
        var u = World(reserve: 2);
        var site = Site(u, "farm", 1);
        var abandoned = Site(u, "goods_factory", 2);
        abandoned.Stock(DemoUniverse.Steel).Amount = 4f;
        ConstructionRules.Expire(u, abandoned);
        u.Stations.Values.First(s => s.TypeKey == "warehouse").PlayerStorage[DemoUniverse.Ice] = 12f;
        u.Factions.Values.First().LastEmergencyBuildTick = 77;
        var runner = new SimulationRunner();
        runner.Run(u, 300);   // Bauschiff unterwegs, Baustelle beliefert
        var json = SaveGameJson.Write(u, Now);
        Assert.Contains("\"Version\":17", json);   // Phase 14: v17
        Assert.Contains("\"Site\":", json);
        Assert.Contains("\"PlayerStorage\":", json);
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        var ls = loaded.Stations[site.Id].Site!;
        Assert.Equal(site.Site!.Required, ls.Required);
        Assert.Equal(site.Site.Builder, ls.Builder);
        Assert.True(loaded.Stations[abandoned.Id].Site!.Abandoned);
        Assert.Equal(12f, loaded.Stations.Values.First(s => s.TypeKey == "warehouse").PlayerStorage[DemoUniverse.Ice]);
        Assert.Equal(77, loaded.Factions.Values.First().LastEmergencyBuildTick);
        runner.Run(u, 2000);
        new SimulationRunner().Run(loaded, 2000);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        // gleicher Seed → gleicher Verlauf
        var a = DemoUniverse.Create(); var b = DemoUniverse.Create();
        new SimulationRunner().Run(a, 3000); new SimulationRunner().Run(b, 3000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }

    [Fact]
    public void Migration_V10ToV11_LagerhausBauschiffeContent_WieFrischeWelt()
    {
        // Runde 38: ein v10-Stand kennt keine Spielerfraktion (die Kette legt aus jeder Fraktion einen Akteur an) — die Welt vor Runde 38 herabstufen
        var def = SpaceSim.Core.Content.WorldBuilder.Parse(SpaceSim.Core.Content.WorldBuilder.DefaultWorldJson());
        BanditenTests.RevertBandits(def);   // Ausbau Punkt 17: die Banditenfraktion kam nach v17 (die eingefrorene Kette kennt sie nicht)
        Runde38Tests.RevertRunde38(def);
        var u = SpaceSim.Core.Content.WorldBuilder.Build(def);
        var v10 = ToV10Json(SaveGameJson.Write(u, Now));
        Assert.Contains("\"Version\":10", v10);
        Assert.DoesNotContain("warehouse", v10.Replace("\"BanditExpansionType\":\"warehouse\"", ""));   // Ausbau Punkt 17: die gespeicherte Regel nennt den Typ, ist aber kein Inhalt
        Assert.DoesNotContain("foreman", v10);
        Assert.DoesNotContain("construction_rig_s", v10);
        Assert.DoesNotContain("BankruptcyProtected", v10);

        var (loaded, _) = SaveGameJson.Read(v10);
        Assert.Equal(u.Rules.MaxStations, loaded.Rules.MaxStations);
        // Phase 30: Kleinstationen (Regierungsform outpost) kamen nach v17 — die eingefrorene Migrationskette kennt sie nicht
        // Ausbau Punkt 17: der Typ bandit_seat kam ebenfalls nach v17 (SaveGameJson.AfterV17Keys)
        Assert.Equal(u.StationTypes.Values.Where(t => t.Government != GovernmentKind.Outpost && t.Key != "bandit_seat").Select(t => t.Key).OrderBy(k => k), loaded.StationTypes.Keys.Where(k => loaded.StationTypes[k].Government != GovernmentKind.Outpost).OrderBy(k => k));
        Assert.Equal(u.ShipModels.Keys.OrderBy(k => k), loaded.ShipModels.Keys.OrderBy(k => k));
        // gleiche Waren (neue Modul-Waren hängt die Migration hinten an, bestehende GoodIds bleiben)
        Assert.Equal(u.Goods.Values.Select(g => g.Key).OrderBy(k => k, StringComparer.Ordinal), loaded.Goods.Values.Select(g => g.Key).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Single(loaded.Stations.Values, s => s.TypeKey == "warehouse" && s.BankruptcyProtected);
        Assert.All(loaded.Stations.Values, s => Assert.True(s.BankruptcyProtected));
        foreach (var f in loaded.Factions.Keys)
        {
            var b = Assert.Single(loaded.Ships.Values, s => s.IsAiBuilder && s.Owner == f);
            Assert.Equal("foreman", b.ModelKey);
            Assert.Equal(b.Stats.FuelCapacity, b.TotalFuel);
            Assert.Equal(b.MaxHull, b.Hull);
        }
        // die migrierte Welt baut: eine Baustelle entsteht und wird fertig
        loaded.Rules.MaxStations = loaded.Stations.Count;
        var sec = loaded.Sectors[new SectorId(1)];
        var site = ConstructionRules.StartSite(loaded, loaded.StationTypes["farm"], sec.Id, StationLifecycleSystem.FindFreeSpot(loaded, sec)!.Value, sec.Owner, 3500f, null, null);
        loaded.Rules.MaxStations = loaded.Stations.Count;
        var runner = new SimulationRunner();
        for (int i = 0; i < 8000 && loaded.Stations[site.Id].IsBuildSite; i++) runner.Step(loaded);
        Assert.False(loaded.Stations[site.Id].IsBuildSite);
    }

    // ───────── Langlauf ─────────

    [Fact]
    public void Langlauf_20000Ticks_BauLaeuft_NotbauSelten_FlotteHaelt()
    {
        // Nachtrag 17b: mit dem größeren Wasserpuffer der Farmen ist Wasser am Anfang nicht mehr knapp — der Lebenszyklus beginnt in 20 000 Ticks
        // nur noch eine Baustelle. Der Test prüft den Bauablauf, nicht die Farm-Kalibrierung: Farmen ohne eigenen Wasserpuffer wie vorher.
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        if (def.StationTypes.First(t => t.Key == "farm") is { Stocks: { } fs }) fs.Remove("water");
        var u = WorldBuilder.Build(def);
        u.Rules.DockDwellTicks = 0; u.Rules.DockDwellTicksPlayer = 0;   // Liegezeit (danach) verschiebt den Weltpfad; geprüft wird der Bauablauf
        int started = 0, done = 0, emergency = 0;
        u.Events.Raised += e =>
        {
            if (e is ConstructionStarted) started++;
            else if (e is ConstructionCompleted c) { done++; if (c.Emergency) emergency++; }
        };
        var runner = new SimulationRunner();
        for (int i = 0; i < 20; i++)
        {
            runner.Run(u, 1000);
            foreach (var site in u.Stations.Values.Where(s => s.Site is { Abandoned: false }))
            {
                Assert.True(site.Credits >= -0.01f);   // Treuhandgeld wird nie überzogen
                foreach (var (g, need) in site.Site!.Required) Assert.True(site.Market[g].Target == need);
            }
            Assert.All(u.Ships.Values.Where(s => s.Order is BuildOrder), s => Assert.True(s.IsAiBuilder));
        }
        Assert.True(started >= 2, $"Baustellen begonnen {started}, fertig {done}");
        Assert.True(done >= 2);
        Assert.True(emergency * 2 <= done);   // Notbau ist nicht der Normalfall
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.ByKey("trade")!) >= 11f);
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.ByKey("mine")!) >= 6f);
        Assert.All(u.Ships.Values.Where(s => s.IsAiBuilder), b => Assert.True(b.Credits > 0f));
    }
}
