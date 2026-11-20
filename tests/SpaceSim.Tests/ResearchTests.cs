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
/// Phase 13: Forschung — Content (<c>research</c>, Forschungsstationen, Mk3-Module, Excavator), Validierung, <see cref="ResearchSystem"/>,
/// Freischaltungen über <see cref="Research.IsUnlocked"/> in Produktion, Werft, Ausrüster, Upgrades, Güteklassen, KI-Flotte und Stationsbau, Dienst
/// „research“, Save v16 mit Migration.
/// </summary>
public class ResearchTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    internal static readonly string[] Phase13Rules =
        { "ResearchActive", "ResearchMaxBidFactor", "ResearchCreditsPerPoint", "ResearchFundAmounts", "ResearchCommissionFee", "RaiderRestartCostFactor", "PirateIdleIncomePerTick" };

    /// <summary>
    /// Demotet einen aktuellen Save (v16) auf v15: Forschungsprojekte und die Phase-13-Regeln entfallen. Forschungsstationen (Typ und Stationen), die
    /// Mk3-Module und — ohne <paramref name="full"/> — die Forschungsstände der Fraktionen bleiben stehen (wie die Lagekarte in <see cref="SecurityTests.ToV13Json"/>),
    /// damit ältere Migrationstests eine laufende Welt hash-gleich zurückladen; die Migration v15→v16 ergänzt dann nur die Projekte. Ältere Stände bleiben unverändert.
    /// </summary>
    internal static string ToV15Json(string json, bool full = false)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        if ((int)node["Version"]! < 16) return json;
        node["Version"] = 15;
        node.Remove("Research");
        if (full) foreach (var f in node["Factions"]!.AsArray()) f!.AsObject().Remove("Research");
        foreach (var key in Phase13Rules) node["Rules"]!.AsObject().Remove(key);
        return node.ToJsonString();
    }

    static readonly FactionId Union = new(1), Guild = new(2);
    const string Fusion = "fusion_mk3", Lasers = "precision_lasers", Deep = "deep_mining";

    static Universe Fresh()
    {
        // Phase 14: Forschung (und Treibstoff) wie bis Phase 13 eingeschaltet; ohne Gründer-Güteklassen
        var def = TestWorlds.Def();
        def.Rules!.FounderQuality = false;
        return WorldBuilder.Build(def);
    }

    /// <summary>Schließt ein Projekt für eine Fraktion ab (über dieselbe Buchung wie Station und Spieler).</summary>
    static void Complete(Universe u, FactionId f, string key)
    {
        var p = u.Research.Get(key)!;
        Assert.True(Research.AddPoints(u, u.Factions[f], p, p.Points));
    }

    static Station St(Universe u, int id) => u.Stations[new StationId(id)];
    static GoodId G(Universe u, string key) => u.FindGood(key)!.Id;
    static ServiceResult Exec(IStationService svc, Universe u, Station st, Ship ship, string offer) => svc.Execute(u, st, ship, new ServiceRequest(offer, default, 0f));

    /// <summary>Spieler an einer Station angedockt, mit Geld.</summary>
    static Ship PlayerAt(Universe u, Station st, float credits = 100_000f)
    {
        var p = u.Ships[u.PlayerShip];
        u.MoveShip(p, st.Sector);
        u.PlayerSector = st.Sector;
        p.Position = st.DockPoint(p.Id);
        u.PlayerWallet.Credits = credits;
        return p;
    }

    static WorldDef Def() => TestWorlds.Def();

    // ───────── Content und Validierung ─────────

    [Fact]
    public void Content_ProjekteForschungsstationenUndGesperrteInhalte()
    {
        var u = Fresh();
        Assert.Equal(new[] { Fusion, Lasers, Deep }, u.Research.Projects.Select(p => p.Key));
        Assert.Equal(new[] { Fusion, Lasers }, u.Research.Get(Deep)!.Requires);
        Assert.Equal(Fusion, u.Research.LockOf(UnlockKind.Module, "reactor_mk3")!.Key);
        Assert.Equal(Lasers, u.Research.LockOf(UnlockKind.Module, "mining_laser_3")!.Key);
        Assert.Equal(Deep, u.Research.LockOf(UnlockKind.Model, "excavator")!.Key);
        // alles, was die Startwirtschaft braucht, ist frei: kein Projekt nennt Mk1/Mk2-Module, Einheits-/Startmodelle oder Stationstypen
        Assert.All(u.Ships.Values, s => Assert.True(!s.HasModel || u.Research.LockOf(UnlockKind.Model, s.ModelKey) is null));
        Assert.All(u.Stations.Values, st => Assert.Null(u.Research.LockOf(UnlockKind.StationType, st.TypeKey)));
        Assert.Null(u.Research.LockOf(UnlockKind.Module, "reactor_mk2"));
        // je Fraktion eine Forschungsstation, offiziell (Unterhalt aus der Kasse), vor Pleite geschützt, mit Lager und Dienst
        var labs = u.Stations.Values.Where(st => Research.ConfigOf(u, st) is not null).OrderBy(st => st.Id.Value).ToList();
        Assert.Equal(new[] { 37, 38, 75 }, labs.Select(st => st.Id.Value));   // Phase 21: + Forschungszentrum des Montanbunds
        Assert.Equal(new FactionId?[] { Union, Guild, new FactionId(3) }, labs.Select(st => Research.TechOf(u, st)));
        Assert.All(labs, st =>
        {
            Assert.Equal(GovernmentKind.Research, StationProtection.KindOf(u, st));
            Assert.True(StationProtection.IsOfficial(u, st));
            Assert.False(StationProtection.IsEconomic(u, st));
            Assert.True(st.BankruptcyProtected);
            Assert.True(st.IsStockOnly(G(u, "energy")));
            Assert.Contains(ResearchService.ServiceKey, u.StationTypes[st.TypeKey].Services);
        });
        // Mk3-Module: Waren, Fabrikrezepte (Antriebswerk, Systemwerk), Verbesserungspfade Mk2 → Mk3; Excavator mit Aufgabe Schürfen
        Assert.Contains(St(u, 27).Production, r => r.Outputs.ContainsKey(G(u, "reactor_mk3")));
        Assert.Contains(St(u, 28).Production, r => r.Outputs.ContainsKey(G(u, "mining_laser_3")));
        Assert.Equal("reactor_mk3", u.Modules["reactor_mk2"].UpgradeTo);
        Assert.Equal("mining_laser_3", u.Modules["mining_laser_2"].UpgradeTo);
        Assert.Equal(new[] { "mine" }, u.ShipModels["excavator"].Jobs);
        // zu Beginn hat keine Fraktion etwas erforscht
        Assert.All(u.Factions.Keys, f => Assert.False(Research.IsUnlocked(u, f, UnlockKind.Module, "reactor_mk3")));
        Assert.True(Research.IsUnlocked(u, Union, UnlockKind.Module, "reactor_mk1"));
        u.Rules.ResearchActive = false;
        Assert.True(Research.IsUnlocked(u, Union, UnlockKind.Module, "reactor_mk3"));   // abgeschaltet: alles frei
    }

    [Fact]
    public void Validierung_UngueltigeForschung_WirftMitName()
    {
        void Bad(Action<WorldDef> change, string expected)
        {
            var def = Def();
            change(def);
            var e = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
            Assert.Contains(expected, e.Message);
        }
        Bad(d => d.Research[0].Unlocks.Add("module:gibtsnicht"), "unbekannten Inhalt");
        Bad(d => d.Research[0].Unlocks.Add("waffe:x"), "unlesbar");
        Bad(d => d.Research[1].Unlocks.Add("module:reactor_mk3"), "schaltet schon");
        Bad(d => d.Research[0].Requires.Add(Deep), "erst später");
        Bad(d => d.Research[1].Key = Fusion, "doppelt");
        Bad(d => d.Research[0].Points = 0, "points");
        Bad(d => d.Research[0].Inputs["gibtsnicht"] = 1, "gibtsnicht");
        // freies Modell mit gesperrtem Modul; Startschiff/Startpaket mit gesperrtem Modell
        Bad(d => d.Research[0].Unlocks.Add("module:reactor_mk2"), "Modul 'reactor_mk2' ist erst mit Forschung");
        Bad(d => d.Player.StartOptions[0].Model = "excavator", "Startpaket");
        // Forschungsbetrieb nur an Forschungsstationen und umgekehrt
        Bad(d => d.StationTypes.First(t => t.Key == "farm").Research = new ResearchConfigDef(), "government");
        Bad(d => d.StationTypes.First(t => t.Key == "research_station").Research = null, "research");
        Bad(d => d.StationTypes.First(t => t.Key == "research_station").Research!.Inputs["gibtsnicht"] = 1, "gibtsnicht");
        // Stationstyp der Startwelt darf nicht gesperrt sein
        Bad(d => d.Research[0].Unlocks.Add("stationType:farm"), "Station 9");
    }

    // ───────── Forschungsbetrieb ─────────

    [Fact]
    public void Forschungstakt_VerbrauchtGueterUndPersonal_BuchtPunkte_BudgetAusDerKasse()
    {
        var u = Fresh();
        var lab = St(u, 37);
        var cfg = Research.ConfigOf(u, lab)!;
        var union = u.Factions[Union];
        u.Actors[Union].Treasury.Credits = 100_000f;
        lab.Stock(G(u, "energy")).Amount = 50f;
        lab.Stock(G(u, "platinum")).Amount = 5f;
        lab.Credits = 1000f;
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        u.Tick = 10;   // Takt fällig
        new ResearchSystem().Step(u, 1);
        Assert.Equal(Fusion, union.Research.Current);
        Assert.Equal(cfg.Points, union.Research.Progress[Fusion], 3);
        Assert.Equal(48f, lab.Stock(G(u, "energy")).Amount, 3);
        Assert.Equal(5f - 0.05f, lab.Stock(G(u, "platinum")).Amount, 3);   // Zusatzverbrauch des Projekts
        // Budget: Kasse füllt bis fundTarget auf, höchstens fundPerTick; Personal geht ab (Senke)
        var funded = Assert.Single(events.OfType<TreasuryUpkeep>(), t => t.Purpose == "research");
        Assert.Equal(cfg.FundPerTick, funded.Amount, 3);
        Assert.Equal(1000f + cfg.FundPerTick - cfg.Credits, lab.Credits, 3);
        Assert.Equal(100_000f - cfg.FundPerTick, u.Actors[Union].Treasury.Credits, 3);
        var cycle = Assert.Single(events.OfType<ResearchCycle>(), c => c.Station == lab.Id);
        Assert.Null(cycle.Stalled);
        // Gebotsdeckel: die Station bietet höchstens Basispreis × Faktor
        lab.Stock(G(u, "energy")).Price = 1000f;
        u.Tick = 11;
        new ResearchSystem().Step(u, 1);
        Assert.Equal(u.Goods[G(u, "energy")].BasePrice * u.Rules.ResearchMaxBidFactor, lab.Stock(G(u, "energy")).Price, 3);
        // fehlt eine Ware, stockt die Forschung (keine Punkte, nichts verbraucht)
        lab.Stock(G(u, "platinum")).Amount = 0f;
        events.Clear();
        u.Tick = 20;
        new ResearchSystem().Step(u, 1);
        Assert.Equal(cfg.Points, union.Research.Progress[Fusion], 3);
        Assert.Contains("Platin", Assert.Single(events.OfType<ResearchCycle>(), c => c.Station == lab.Id).Stalled);
        Assert.Equal(46f + 2f, lab.Stock(G(u, "energy")).Amount + 0f, 3);
    }

    [Fact]
    public void Abschluss_SchaltetFrei_FabrikProduziertMk3_NurMitTechStandDerFraktion()
    {
        var u = Fresh();
        var drive = St(u, 27);   // Antriebswerk Alpha (Union)
        var mk3 = G(u, "reactor_mk3");
        foreach (var g in new[] { "steel", "electronics", "platinum" }) drive.Stock(G(u, g)).Amount = 200f;
        drive.Stock(mk3).Amount = 0f;
        var production = new ProductionSystem();
        void Cycle() { u.Tick += 20 - u.Tick % 20; new MarketSystem().Step(u, 1); production.Step(u, 1); }
        Cycle();
        Assert.Equal(0f, drive.Stock(mk3).Amount);   // gesperrt: kein Mk3-Reaktor
        var completed = new List<ResearchCompleted>();
        u.Events.Raised += e => { if (e is ResearchCompleted c) completed.Add(c); };
        Complete(u, Union, Fusion);
        var done = Assert.Single(completed);
        Assert.Equal((Union, Fusion), (done.Faction, done.Project));
        Assert.True(Research.IsUnlocked(u, Union, UnlockKind.Module, "reactor_mk3"));
        Assert.False(Research.IsUnlocked(u, Guild, UnlockKind.Module, "reactor_mk3"));   // fraktionsweit, nicht weltweit
        Assert.Null(u.Factions[Union].Research.Current);
        Cycle();
        Assert.Equal(1f, drive.Stock(mk3).Amount);
        // das nächste Projekt wählt die Heuristik (Präzisionslaser; Tiefbau braucht beide)
        Assert.Equal(Lasers, Research.CurrentOf(u, u.Factions[Union])!.Key);
        // eine Fabrik der Gilde stellt den Reaktor nicht her: Antriebswerk an die Gilde → Produktion steht
        drive.Owner = Guild;
        drive.Stock(mk3).Amount = 0f;
        Cycle();
        Assert.Equal(0f, drive.Stock(mk3).Amount);
    }

    // ───────── Werft, Ausrüster, Upgrades, Güteklassen, KI-Flotte ─────────

    [Fact]
    public void Werft_GesperrtesModell_SichtbarAberNichtKaufbar_NachForschungKaufbar()
    {
        var (u, yard, player) = QualityUpgradeTests.AtYard();   // Werft Delta gehört der Gilde, Lager gefüllt
        u.Rules.ResearchActive = true;   // Phase 14: Standardwelt hat die Forschung aus
        var dealer = new ShipDealerService();
        var offer = dealer.Offers(u, yard, player).Single(o => o.Key == "buy:excavator");
        Assert.Equal(0f, offer.Amount);
        var q = ShipDealerService.Quote(u, yard, player, "excavator")!;
        Assert.Contains("nicht erforscht", q.Locked);
        Assert.False(q.Buyable);
        string before = $"{UniverseHash.Compute(u)}|{u.PlayerWallet.Credits}|{u.Ships.Count}";
        int events = 0;
        u.Events.Raised += _ => events++;
        var r = Exec(dealer, u, yard, player, "buy:excavator");
        Assert.False(r.Success);
        Assert.Contains("nicht erforscht", r.Message);
        Assert.Equal(before, $"{UniverseHash.Compute(u)}|{u.PlayerWallet.Credits}|{u.Ships.Count}");
        Assert.Equal(0, events);
        // die Union hat es erforscht — die Werft gehört aber der Gilde: weiter gesperrt
        foreach (var k in new[] { Fusion, Lasers, Deep }) Complete(u, Union, k);
        Assert.False(ShipDealerService.Quote(u, yard, player, "excavator")!.Buyable);
        foreach (var k in new[] { Fusion, Lasers, Deep }) Complete(u, Guild, k);
        Assert.True(ShipDealerService.Quote(u, yard, player, "excavator")!.Buyable);
        Assert.True(Exec(dealer, u, yard, player, "buy:excavator").Success);
        Assert.Equal("excavator", u.Ships[u.PlayerShip].ModelKey);
    }

    [Fact]
    public void Ausruester_GesperrtesModul_NichtErforscht_AusDerFrachtErlaubt()
    {
        var (u, yard, player) = QualityUpgradeTests.AtYard();
        u.Rules.ResearchActive = true;   // Phase 14: Standardwelt hat die Forschung aus
        var outfitter = new OutfitterService();
        int reactor = player.Slots.FindIndex(s => s.Type == "reactor");
        string key = $"fit:{reactor}:reactor_mk3";
        var offer = outfitter.Offers(u, yard, player).Single(o => o.Key == key);
        Assert.Equal(0f, offer.Amount);
        Assert.Contains("nicht erforscht", offer.Text);
        var r = Exec(outfitter, u, yard, player, key);
        Assert.False(r.Success);
        Assert.Contains("nicht erforscht", r.Message);
        Assert.Equal("reactor_mk1", player.Fit[reactor]);
        // ein Modul in der eigenen Fracht darf jeder Ausrüster einbauen
        player.Cargo[G(u, "reactor_mk3")] = 1f;
        Assert.True(Exec(outfitter, u, yard, player, key).Success);
        Assert.Equal("reactor_mk3", player.Fit[reactor]);
    }

    [Fact]
    public void Upgrade_AufGesperrtesZiel_MitGrund_NachForschungMoeglich()
    {
        var (u, yard, player) = QualityUpgradeTests.AtYard();
        u.Rules.ResearchActive = true;   // Phase 14: Standardwelt hat die Forschung aus
        int reactor = player.Slots.FindIndex(s => s.Type == "reactor");
        player.Fit[reactor] = "reactor_mk2";
        player.RecomputeStats(u);
        var q = ShipUpgrade.Quote(u, yard, player, $"module:{reactor}")!;
        Assert.Contains("nicht erforscht", q.Issue);
        Assert.False(q.Possible);
        Assert.False(Exec(new UpgradeService(), u, yard, player, $"module:{reactor}").Success);
        Complete(u, Guild, Fusion);   // Werft Delta = Gilde
        Assert.True(ShipUpgrade.Quote(u, yard, player, $"module:{reactor}")!.Possible);
    }

    [Fact]
    public void Gueteklassen_NieGesperrteModule_GruenderUndWerft()
    {
        // Gründerschiffe würfeln ohne Werft: über viele Seeds nie ein gesperrtes Modul (Reaktor Mk2 → Mk3, Laser Mk2 → Mk3 wären Ziele)
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var u = TestWorlds.Default(seed);
            Assert.All(u.Ships.Values, s => Assert.DoesNotContain(s.Fit, k => k is "reactor_mk3" or "mining_laser_3"));
        }
        // Werftwurf: Elite (zwei Module besser) mit Mk2-Modell und vollem Mk3-Lager bleibt ohne Mk3, solange die Werftfraktion nicht forscht
        var (w, yard, _) = QualityUpgradeTests.AtYard();
        w.Rules.ResearchActive = true;   // Phase 14: Standardwelt hat die Forschung aus
        var model = w.ShipModels["rockbreaker_ii"];
        var cls = w.ShipClasses[model.ClassKey];
        w.Rules.QualityChances = new List<float> { 0f, 0f, 1f, 0f };
        for (int i = 0; i < 20; i++)
        {
            var q = ShipQualityRoll.Roll(w, cls, cls.Slots, model.Fit, yard, ShipPurchase.Materials(w, model));
            Assert.DoesNotContain(q.Fit, k => k is "reactor_mk3" or "mining_laser_3");
        }
        Complete(w, Guild, Fusion);
        Assert.Contains(Enumerable.Range(0, 30).Select(_ => ShipQualityRoll.Roll(w, cls, cls.Slots, model.Fit, yard, ShipPurchase.Materials(w, model))),
            q => q.Fit.Contains("reactor_mk3"));
    }

    [Fact]
    public void KiFlotte_KauftGesperrtesModellNicht()
    {
        var (u, yard, _) = QualityUpgradeTests.AtYard();
        u.Rules.ResearchActive = true;   // Phase 14: Standardwelt hat die Forschung aus
        var buyer = u.Ships.Values.First(s => s.Role == ShipRole.Miner);
        buyer.Credits = 1_000_000f;
        u.MoveShip(buyer, yard.Sector);
        var job = ShipJobs.ByKey("mine")!;
        // Excavator ist der beste Kauf je Credit, wenn alles andere fehlt: nur noch Excavator-Material im Lager
        // Runde 11: an allen Werften (die Regale starten jetzt voll — sonst kauft die KI an einer anderen Werft ein leichtes Modell)
        foreach (var y in u.Stations.Values.Where(st => st.TypeKey == "shipyard" || st.TypeKey == "trade_station"))
        {
            foreach (var m in u.ShipModels.Values.Where(m => m.Key != "excavator")) foreach (var k in m.Fit) if (k is not null && k is not "reactor_mk3" and not "mining_laser_3" and not "cutting_wheel_m" and not "chem_engine_m" and not "cargo_universal_100" and not "cargo_universal_60" && y.Market.ContainsKey(G(u, k))) y.Stock(G(u, k)).Amount = 0f;
            if (y.Market.ContainsKey(G(u, "ship_kit_light"))) y.Stock(G(u, "ship_kit_light")).Amount = 0f;
        }
        Assert.NotEqual("excavator", FleetSystem.ChooseModel(u, job, buyer, 0f, 8f)?.Model.Key);
        foreach (var k in new[] { Fusion, Lasers, Deep }) Complete(u, Guild, k);
        var chosen = FleetSystem.ChooseModel(u, job, buyer, 0f, 8f)?.Model.Key;
        Assert.True(chosen == "excavator", $"gewählt: {chosen ?? "nichts"}");
    }

    [Fact]
    public void Lebenszyklus_BautGesperrtenStationstypNicht()
    {
        // Testwelt: ein billiger zweiter Stahlwerkstyp, gesperrt durch ein Projekt; Stahl knapp
        var def = Def();
        var steel = def.StationTypes.First(t => t.Key == "steelworks");
        def.StationTypes.Add(new StationTypeDef { Key = "steelworks_2", Name = "Stahlwerk II", BuildCost = 100, Upkeep = steel.Upkeep, Income = steel.Income, Recipes = steel.Recipes });
        def.Research.Add(new ResearchProjectDef { Key = "steel_tech", Name = "Stahltechnik", Points = 100, Unlocks = new() { "stationType:steelworks_2" } });
        var u = WorldBuilder.Build(def);
        // Phase 16: der Rohstoff-Filter des Lebenszyklus (Phase 15: kein Hersteller, dessen Erz selbst knapp ist) ist hier abgeschaltet, statt Erz und
        // Energie künstlich zu füllen — er würde sonst in beiden Hälften „nichts bauen“ liefern und die Sperrprüfung leer laufen lassen
        u.Rules.BuildChecksRawOre = false;
        u.LastStationBuildTick = -1_000_000;
        foreach (var st in u.Stations.Values) if (st.Market.TryGetValue(G(u, "steel"), out var e)) e.Amount = 0f;
        Assert.False(Research.UnlockedAnywhere(u, UnlockKind.StationType, "steelworks_2"));
        var started = new List<ConstructionStarted>();
        u.Events.Raised += e => { if (e is ConstructionStarted c) started.Add(c); };
        new StationLifecycleSystem().Step(u, 20);
        Assert.DoesNotContain(started, c => c.TypeKey == "steelworks_2");
        Assert.Contains(started, c => c.TypeKey == "steelworks");   // nicht leer gelaufen: gebaut wird der erlaubte Typ
        // erforscht (alle Fraktionen; Phase 21: auch der Montanbund — sonst kann der gezogene Investor zu ihm gehören): jetzt wird der billigere Typ gewählt
        foreach (var f in u.Factions.Keys.OrderBy(f => f.Value).ToList()) Complete(u, f, "steel_tech");
        foreach (var site in u.Stations.Values.Where(s => s.IsBuildSite).ToList()) u.RemoveStation(site.Id, "test");
        started.Clear();
        u.LastStationBuildTick = -1_000_000;
        foreach (var st in u.Stations.Values) if (st.Market.TryGetValue(G(u, "steel"), out var e)) e.Amount = 0f;
        new StationLifecycleSystem().Step(u, 20);
        Assert.Contains(started, c => c.TypeKey == "steelworks_2");
    }

    // ───────── Dienst „research“ ─────────

    [Fact]
    public void Dienst_Beauftragen_Mitfinanzieren_StrengeSchluessel()
    {
        var u = Fresh();
        new SimulationRunner().Run(u, 1);
        var lab = St(u, 37);
        var player = PlayerAt(u, lab, 50_000f);
        var svc = new ResearchService();
        Assert.True(svc.IsAvailable(u, lab));
        Assert.Contains(ResearchService.ServiceKey, StationServices.Default().ForStation(u, lab).Select(x => x.Key));
        var offers = svc.Offers(u, lab, player);
        // laufendes Projekt (Fusion) ist nicht beauftragbar, Präzisionslaser schon, Tiefbau fehlt eine Voraussetzung
        Assert.Equal(0f, offers.Single(o => o.Key == "commission:" + Fusion).Amount);
        Assert.Equal(1f, offers.Single(o => o.Key == "commission:" + Lasers).Amount);
        Assert.Equal(0f, offers.Single(o => o.Key == "commission:" + Deep).Amount);
        Assert.Equal(u.Rules.ResearchFundAmounts.Count, offers.Count(o => o.Key.StartsWith("fund:")));

        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        // ungültige Schlüssel: nichts ändert sich, kein Event
        string snap = $"{UniverseHash.Compute(u)}|{u.PlayerWallet.Credits}|{lab.Credits}";
        foreach (var bad in new[] { "commission:gibtsnicht", "commission:" + Deep, "fund:123", "fund:abc", "fund", "commission:", "kaufen:1", "fund:-500" })
            Assert.False(Exec(svc, u, lab, player, bad).Success, bad);
        Assert.Equal(snap, $"{UniverseHash.Compute(u)}|{u.PlayerWallet.Credits}|{lab.Credits}");
        Assert.Empty(events);

        // beauftragen: Gebühr an die Station, Projekt läuft
        float fee = u.Rules.ResearchCommissionFee, labBefore = lab.Credits;
        var r = Exec(svc, u, lab, player, "commission:" + Lasers);
        Assert.True(r.Success, r.Message);
        Assert.Equal(Lasers, u.Factions[Union].Research.Current);
        Assert.True(u.Factions[Union].Research.Commissioned);
        Assert.Equal(50_000f - fee, u.PlayerWallet.Credits, 2);
        Assert.Equal(labBefore + fee, lab.Credits, 2);
        Assert.Single(events.OfType<ResearchCommissioned>());
        Assert.Single(events.OfType<ServicePerformed>());
        // die Heuristik ersetzt den Auftrag nicht
        Assert.Equal(Lasers, Research.CurrentOf(u, u.Factions[Union])!.Key);

        // mitfinanzieren: Credits → FP; genug Geld schließt das Projekt ab
        float pts = 2000f / u.Rules.ResearchCreditsPerPoint;
        r = Exec(svc, u, lab, player, "fund:2000");
        Assert.True(r.Success, r.Message);
        Assert.Equal(pts, u.Factions[Union].Research.Progress[Lasers], 2);
        Assert.Equal(2000f, u.Factions[Union].Research.PlayerFunded, 2);
        u.PlayerWallet.Credits = 1_000_000f;
        while (!u.Factions[Union].Research.IsCompleted(Lasers)) Assert.True(Exec(svc, u, lab, player, "fund:10000").Success);
        Assert.Contains(events, e => e is ResearchCompleted { Project: Lasers });
        Assert.False(Research.IsUnlocked(u, Guild, UnlockKind.Module, "mining_laser_3"));
        // zu wenig Geld: abgelehnt
        u.PlayerWallet.Credits = 10f;
        Assert.False(Exec(svc, u, lab, player, "fund:500").Success);
        // feindliche Fraktion nimmt keine Aufträge an
        u.PlayerWallet.Credits = 50_000f;
        Relations.Set(u, (ActorId)Union, u.PlayerActor, -0.8f);
        Assert.NotNull(ResearchService.Refusal(u, lab, player));
        Assert.False(Exec(svc, u, lab, player, "fund:500").Success);
        Assert.All(svc.Offers(u, lab, player), o => Assert.Equal(0f, o.Amount));
    }

    [Fact]
    public void Anzeige_ForschungsstandJeFraktion()
    {
        var u = Fresh();
        Complete(u, Union, Fusion);
        var v = ResearchInfo.ForFaction(u, Union)!;
        Assert.Equal((1, 3), (v.Completed, v.Total));
        Assert.Equal(ResearchStatus.Done, v.Projects[0].Status);
        Assert.Equal(ResearchStatus.Running, v.Projects[1].Status);
        Assert.Equal(ResearchStatus.Blocked, v.Projects[2].Status);
        Assert.Contains("Präzisionslaser", v.Projects[2].MissingRequires);
        Assert.Contains("Modul Fusionsreaktor Mk3", v.Projects[0].Unlocks);
        Assert.Single(v.Stations);
        Assert.Contains("1/3 erforscht", v.Summary);
        Assert.Equal(v.Faction, ResearchInfo.ForStation(u, St(u, 37))!.Faction);
        Assert.Null(ResearchInfo.ForStation(u, St(u, 27)));
        // reine Anzeige: ändert nichts
        Assert.Null(u.Factions[Guild].Research.Current);
        _ = ResearchInfo.ForFaction(u, Guild);
        Assert.Null(u.Factions[Guild].Research.Current);
    }

    // ───────── Langlauf, Determinismus, Speichern ─────────

    [Fact]
    public void Langlauf_20000Ticks_ForschungFindetStatt_GeldErklaerbar_NichtsGesperrtesHergestellt()
    {
        var u = TestWorlds.Default();
        int done = 0;
        double funded = 0, salary = 0;
        var producedLocked = new List<string>();
        u.Events.Raised += e =>
        {
            if (e is ResearchCompleted) done++;
            else if (e is TreasuryUpkeep { Purpose: "research" } t) funded += t.Amount;
            else if (e is ResearchCycle c) salary += c.Credits;
        };
        var runner = new SimulationRunner();
        for (int i = 0; i < 20; i++)
        {
            runner.Run(u, 1000);
            // keine Station hält ein gesperrtes Modul aus eigener Fertigung (Fabrik einer Fraktion ohne Tech-Stand: Bestand nie über dem Anfang)
            foreach (var st in u.Stations.Values)
                foreach (var r in st.Production)
                    foreach (var g in r.Outputs.Keys)
                        if (!Research.GoodAvailable(u, st, g) && st.Market[g].Amount > 1.001f) producedLocked.Add($"{st.Name}:{u.Goods[g].Key}");
        }
        Assert.Empty(producedLocked);
        Assert.True(done >= 1, "über 20000 Ticks wird mindestens ein Projekt fertig");
        Assert.True(funded > 0 && salary > 0);
        Assert.All(u.NpcFactionIds().Where(id => !Bandits.IsEstablished(u, id)).Select(id => u.Factions[id]), f => Assert.True(f.Research.PointsTotal > 0f));   // Runde 38: die Spielerfraktion forscht nicht; Punkt 17: Banditen forschen nicht
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.ByKey("trade")!) >= 12f);
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.ByKey("mine")!) >= 7f);
    }

    [Fact]
    public void Determinismus_UndSaveRoundtrip_V16_MitForschungsstand_BitIdentisch()
    {
        var a = TestWorlds.Default(); var b = TestWorlds.Default();
        new SimulationRunner().Run(a, 3000);
        new SimulationRunner().Run(b, 3000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        Complete(a, Union, Fusion);
        a.Factions[Guild].Research.Current = Lasers;
        a.Factions[Guild].Research.Commissioned = true;
        a.Factions[Guild].Research.PlayerFunded = 1234f;
        var json = SaveGameJson.Write(a, Now);
        Assert.Contains("\"Version\":17", json);
        Assert.Contains("\"Research\":[", json);
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
        Assert.True(loaded.Factions[Union].Research.IsCompleted(Fusion));
        Assert.Equal((Lasers, true, 1234f), (loaded.Factions[Guild].Research.Current, loaded.Factions[Guild].Research.Commissioned, loaded.Factions[Guild].Research.PlayerFunded));
        Assert.Equal(a.Research.Projects.Select(p => (p.Key, p.Points, string.Join(",", p.Unlocks))), loaded.Research.Projects.Select(p => (p.Key, p.Points, string.Join(",", p.Unlocks))));
        Assert.NotNull(loaded.StationTypes["research_station"].Research);
        new SimulationRunner().Run(a, 3000);
        new SimulationRunner().Run(loaded, 3000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Migration_V15ToV16_ProjekteErgaenzt_HashGleich()
    {
        var fresh = TestWorlds.Default();
        var v15 = ToV15Json(SaveGameJson.Write(fresh, Now), full: true);
        Assert.Contains("\"Version\":15", v15);
        Assert.DoesNotContain("fusion_mk3", v15);
        var (loaded, _) = SaveGameJson.Read(v15);
        Assert.Equal(3, loaded.Research.Count);
        Assert.Equal(UniverseHash.Compute(fresh), UniverseHash.Compute(loaded));
        new SimulationRunner().Run(fresh, 1000);
        new SimulationRunner().Run(loaded, 1000);
        Assert.Equal(UniverseHash.Compute(fresh), UniverseHash.Compute(loaded));
    }

    /// <summary>Ein v15-Stand, wie er vor Phase 13 aussah: ohne Forschungsstationen, ohne Mk3-Module, Excavator und Verbesserungspfade Mk2 → Mk3.</summary>
    static string RealisticV15(Universe u)
    {
        var node = JsonNode.Parse(ToV15Json(SaveGameJson.Write(u, Now), full: true))!.AsObject();
        var types = node["StationTypes"]!.AsArray();
        foreach (var t in types.ToList()) if ((string)t!["Key"]! == "research_station") types.Remove(t);
        var stations = node["Stations"]!.AsArray();
        var removed = new HashSet<int>();
        foreach (var s in stations.ToList()) if ((string)s!["Type"]! == "research_station") { removed.Add((int)s["Id"]!); stations.Remove(s); }
        foreach (var sec in node["Sectors"]!.AsArray())
        {
            var list = sec!["Stations"]!.AsArray();
            foreach (var x in list.ToList()) if (removed.Contains((int)x!)) list.Remove(x);
        }
        var models = node["ShipModels"]!.AsArray();
        foreach (var m in models.ToList()) if ((string)m!["Key"]! == "excavator") models.Remove(m);
        QualityUpgradeTests.RemoveModules(node, new[] { "reactor_mk3", "mining_laser_3" });
        foreach (var m in node["Modules"]!.AsArray())
            if ((string)m!["Key"]! is "reactor_mk2" or "mining_laser_2") { m.AsObject().Remove("UpgradeTo"); m.AsObject().Remove("UpgradeCost"); }
        return node.ToJsonString();
    }

    [Fact]
    public void Migration_RealistischerV15Stand_BekommtForschungsstationenModuleUndProjekte_UndLaeuft()
    {
        var v15 = RealisticV15(TestWorlds.Classic(null, fuel: true, research: true));
        Assert.DoesNotContain("research_station", v15);
        Assert.DoesNotContain("reactor_mk3", v15);
        var (u, _) = SaveGameJson.Read(v15);
        Assert.Equal(3, u.Research.Count);
        Assert.Equal(2, u.Stations.Values.Count(st => Research.ConfigOf(u, st) is not null));
        Assert.True(u.Modules.ContainsKey("reactor_mk3") && u.ShipModels.ContainsKey("excavator"));
        Assert.Equal("reactor_mk3", u.Modules["reactor_mk2"].UpgradeTo);
        Assert.False(Research.IsUnlocked(u, Union, UnlockKind.Model, "excavator"));
        Assert.Contains(u.Stations.Values.First(s => s.TypeKey == "drive_works").Production, r => r.Outputs.ContainsKey(G(u, "reactor_mk3")));
        int cycles = 0;
        u.Events.Raised += e => { if (e is ResearchCycle { Stalled: null }) cycles++; };
        new SimulationRunner().Run(u, 3000);
        Assert.True(cycles > 0);
        Assert.All(u.Factions.Values.Where(f => !Bandits.IsEstablished(u, f.Id)), f => Assert.True(f.Research.PointsTotal > 0f));   // Punkt 17: Banditen forschen nicht
    }

    [Fact]
    public void Migration_OhneAufloesbareForschung_BleibtAllesFrei()
    {
        // Welt ohne Warenschlüssel für die Forschungsgüter (wie sehr alte Stände): kein Forschungsstations-Typ, keine Projekte, nichts gesperrt
        var node = JsonNode.Parse(RealisticV15(TestWorlds.Default()))!.AsObject();
        foreach (var g in node["Goods"]!.AsArray()) if ((string)g!["Key"]! == "energy") g["Key"] = "";
        var (u, _) = SaveGameJson.Read(node.ToJsonString());
        Assert.Equal(0, u.Research.Count);
        Assert.DoesNotContain(u.Stations.Values, st => Research.ConfigOf(u, st) is not null);
        Assert.True(Research.IsUnlocked(u, Union, UnlockKind.Model, "excavator"));
        Assert.True(Research.IsUnlocked(u, null, UnlockKind.Module, "reactor_mk3"));
        new SimulationRunner().Run(u, 500);
    }
}
