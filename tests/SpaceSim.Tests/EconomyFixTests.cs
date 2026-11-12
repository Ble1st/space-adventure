using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 15: Wirtschaftskorrekturen — ehrlicheres Balance-Urteil (<see cref="MarketWatch"/>), Produktion nach Bedarf, Regale und reine Produkte,
/// Betriebskapital, Steuerbremse, Instandhaltung, Subventionen, Lebenszyklus mit Rohstoff-Engpass, Abbau lange untätiger Schiffe, Langlauf der
/// unveränderten Standardwelt (Treibstoff und Forschung aus).
/// </summary>
public class EconomyFixTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    const int SmelterAlpha = 2, SteelAlpha = 1, SolarAlpha = 20, PowerAlpha = 4, Yard = 18, DriveWorks = 27, TradeBeta = 30, ExportGamma = 39, Colony = 10;

    static GoodId G(Universe u, string key) => u.FindGood(key)!.Id;
    static Station St(Universe u, int id) => u.Stations[new StationId(id)];

    // ───────── Balance-Urteil ─────────

    [Fact]
    public void MarketWatch_Deckel_LeereKasse_WareOhneFahrten()
    {
        var u = DemoUniverse.Create();
        new MarketSystem().Step(u, 1);
        var yard = St(u, Yard);
        var reactor = G(u, "reactor_mk1");
        yard.Market[reactor].Amount = 0f;   // leer → 4× (oberer Deckel)
        new MarketSystem().Step(u, 1);
        var smelter = St(u, SmelterAlpha);
        smelter.Credits = 0f;
        var watch = new MarketWatch(u);
        for (int i = 0; i < 4; i++) watch.Sample(u);
        Assert.Equal(1f, watch.Share(yard.Id, reactor).Upper);
        Assert.Contains(watch.CapHints(u), h => h.Station == yard.Id && h.Good == reactor && h.UpperShare == 1f);
        Assert.Contains(watch.CashHints(u), h => h.Station == smelter.Id);
        // Regierungsstationen zahlen aus der Kasse: ihre leere eigene Kasse ist kein Befund
        Assert.DoesNotContain(watch.CashHints(u), h => StationProtection.IsOfficial(u, u.Stations[h.Station]));
        // noch keine Fahrt: Eisenerz (Erz, Abnehmer Stahlwerk) gilt als gehandelte Ware ohne Fahrten, ein Modul ohne Abnehmer nicht
        Assert.Contains(G(u, "iron_ore"), watch.IdleGoods(u));
        Assert.DoesNotContain(G(u, "power_bank_s"), watch.IdleGoods(u));
        // eine Exportware am Höchstgebot der Senke zählt als oberer Deckel nur mit leerem Lager
        Assert.Equal(u.Rules.ExportMaxBidFactor, MarketWatch.UpperCap(u, St(u, ExportGamma), G(u, "food")), 3);
        Assert.Equal(4f, MarketWatch.UpperCap(u, smelter, G(u, "copper_ore")));
    }

    // ───────── Produktion nach Bedarf ─────────

    static void FillInputs(Station st)
    {
        foreach (var r in st.Production) foreach (var g in r.Inputs.Keys) st.Stock(g).Amount = 1000f;
    }

    [Fact]
    public void Produktion_OhneAbnehmer_PausiertBeimWunschbestand_MitAbnehmerBisZumUeberhang()
    {
        var u = DemoUniverse.Create();
        var works = St(u, DriveWorks);
        FillInputs(works);
        var bank = G(u, "power_bank_s");     // kein Regal führt Powerbanks: kein Abnehmer
        var reactor = G(u, "reactor_mk1");   // Werft und Handelsstation führen Reaktoren
        var bankEntry = works.Market[bank];
        bankEntry.Amount = bankEntry.Target + 0.5f;
        var reactorEntry = works.Market[reactor];
        reactorEntry.Amount = reactorEntry.Target + 0.5f;
        St(u, Yard).Market[reactor].Amount = 0f;   // offene Fehlmenge an der Werft
        u.Tick = 0;   // Takt 20: alle Modulrezepte fällig
        new ProductionSystem().Step(u, 1);
        Assert.Equal(bankEntry.Target + 0.5f, bankEntry.Amount, 3);        // pausiert ohne Abnehmer
        Assert.Equal(reactorEntry.Target + 1.5f, reactorEntry.Amount, 3);  // gefragt: läuft weiter
        // Abnehmer gesättigt (Werft und Handelsstation am Wunschbestand): auch der Reaktor pausiert jetzt beim Wunschbestand
        // Phase 21: alle Abnehmer (drei Werften, Handelsstation) — die 11-Sektoren-Welt hat mehr Regale mit Reaktoren
        foreach (var holder in u.Stations.Values.Where(s => s.Id != works.Id && s.Market.ContainsKey(reactor) && !s.Produces(reactor)))
            holder.Market[reactor].Amount = MathF.Max(holder.Market[reactor].Amount, holder.Market[reactor].Target);
        u.Tick = 20;
        new ProductionSystem().Step(u, 1);
        Assert.Equal(reactorEntry.Target + 1.5f, reactorEntry.Amount, 3);
        // alte Regel (NoDemandStockFactor ≥ OverstockFactor): produziert wieder bis zum Überhang
        u.Rules.NoDemandStockFactor = u.Rules.OverstockFactor;
        u.Tick = 40;
        new ProductionSystem().Step(u, 1);
        Assert.Equal(bankEntry.Target + 1.5f, bankEntry.Amount, 3);
    }

    [Fact]
    public void Zutaten_NurFuerPausierteRezepte_NurBisZumWunschbestand()
    {
        var u = DemoUniverse.Create();
        var smelter = St(u, SmelterAlpha);
        var copper = G(u, "copper");
        var ore = G(u, "copper_ore");
        foreach (var st in u.Stations.Values) if (st.Market.TryGetValue(copper, out var e)) e.Amount = e.Target;   // niemand braucht Kupfer
        smelter.Market[copper].Amount = smelter.Market[copper].Target + 1f;                                          // Hütte voll (ohne Abnehmer)
        smelter.Market[ore].Amount = 50f;
        new MarketSystem().Step(u, 1);
        Assert.Equal(smelter.Market[ore].Target - 50f, smelter.AcceptableUnits(ore, u.Rules), 3);
        // ein Abnehmer hat wieder Bedarf: das Rezept läuft, die Hütte nimmt Erz bis zum Überhang
        St(u, 6).Market[copper].Amount = 0f;
        new MarketSystem().Step(u, 1);
        Assert.Equal(smelter.Market[ore].Target * u.Rules.OverstockFactor - 50f, smelter.AcceptableUnits(ore, u.Rules), 3);
    }

    // ───────── Regale, reine Produkte ─────────

    [Fact]
    public void Regal_NimmtNurBisZumWunschbestand_ExportwareBisZumUeberhang()
    {
        var u = DemoUniverse.Create();
        var beta = St(u, TradeBeta);
        var reactor = G(u, "reactor_mk1");
        var food = G(u, "food");
        beta.Market[reactor].Amount = 0f;
        beta.Market[food].Amount = 0f;
        new MarketSystem().Step(u, 1);
        new ExportSystem().Step(u, 1);
        Assert.Equal(beta.Market[reactor].Target, beta.AcceptableUnits(reactor, u.Rules), 3);
        // Phase 19: Exportwaren mit eigenem Überhang-Faktor (ExportOverstockFactor; Standardwelt 3, allgemein 2,5)
        float exportFactor = u.Rules.ExportOverstockFactor > 0f ? u.Rules.ExportOverstockFactor : u.Rules.OverstockFactor;
        Assert.Equal(beta.Market[food].Target * exportFactor, beta.AcceptableUnits(food, u.Rules), 3);
        // ein volles Regal steht beim Basispreis, nicht beim Drittel
        beta.Market[reactor].Amount = beta.Market[reactor].Target;
        new MarketSystem().Step(u, 1);
        Assert.Equal(u.Goods[reactor].BasePrice, beta.Market[reactor].Price, 2);
    }

    [Fact]
    public void Hersteller_NimmtSeinReinesProduktNichtAn_VerbraucherSchon()
    {
        var u = DemoUniverse.Create();
        var energy = G(u, "energy");
        foreach (var st in u.Stations.Values) if (st.Market.TryGetValue(energy, out var e)) e.Amount = 0f;
        new MarketSystem().Step(u, 1);
        Assert.Equal(0f, St(u, SolarAlpha).AcceptableUnits(energy, u.Rules));   // Solarkraftwerk: Energie ist reines Produkt
        Assert.Equal(0f, St(u, PowerAlpha).AcceptableUnits(energy, u.Rules));   // Kraftwerk ebenso
        Assert.True(St(u, SteelAlpha).AcceptableUnits(energy, u.Rules) > 0f);   // Stahlwerk verbraucht Energie
        // Händler fahren deshalb keine Energie zwischen Erzeugern hin und her
        var trades = new List<TradeCompleted>();
        u.Events.Raised += e => { if (e is TradeCompleted t && t.Good == energy) trades.Add(t); };
        new SimulationRunner().Run(u, 3000);
        Assert.NotEmpty(trades);
        Assert.DoesNotContain(trades, t => u.Stations.TryGetValue(t.To, out var to) && to.Produces(energy) && !to.Consumes(energy));
    }

    // ───────── Betriebskapital, Steuerbremse ─────────

    [Fact]
    public void Vermoegensabgabe_NurUeberDemBetriebskapital_SteuerbremseLeitetUeberschussAus()
    {
        var u = DemoUniverse.Create();
        var st = St(u, SteelAlpha);
        var type = u.StationTypes[st.TypeKey];
        float w = WorkingCapital.Of(u, st);
        Assert.True(w > 0f);
        st.Credits = w - 10f;
        new UpkeepSystem().Step(u, 10);
        Assert.Equal(w - 10f + (type.IncomePerTick - type.UpkeepPerTick) * 10f, st.Credits, 1);   // keine Abgabe aufs Betriebskapital
        st.Credits = w + 10_000f;
        new UpkeepSystem().Step(u, 10);
        Assert.Equal(w + 10_000f + (type.IncomePerTick - type.UpkeepPerTick) * 10f - 10_000f * u.Rules.StationWealthTax * 10f, st.Credits, 1);

        // Steuerbremse: volle Kasse behält nichts, der Rest verlässt die Welt; leere Kasse behält alles. Die Zahler zahlen gleich viel.
        var union = u.Actors[new ActorId(1)];
        union.Treasury.Credits = 1e9f;
        Assert.Equal(0f, UpkeepSystem.FiscalFactors(u)[1]);
        float sunk = 0f, collected = 0f;
        u.Events.Raised += e => { if (e is TaxSunk ts) sunk += ts.Amount; else if (e is TaxCollected tc && tc.Treasury.Value == 1) collected += tc.Amount; };
        st.Credits = w + 10_000f;
        new UpkeepSystem().Step(u, 10);
        Assert.Equal(0f, collected);
        Assert.True(sunk > 0f);
        Assert.Equal(w + 10_000f + (type.IncomePerTick - type.UpkeepPerTick) * 10f - 10_000f * u.Rules.StationWealthTax * 10f, st.Credits, 1);
        union.Treasury.Credits = 0f;
        Assert.Equal(1f, UpkeepSystem.FiscalFactors(u)[1]);
        u.Rules.TreasuryTargetShare = 0f;
        Assert.Empty(UpkeepSystem.FiscalFactors(u));
    }

    // ───────── Instandhaltung ─────────

    [Fact]
    public void Instandhaltung_LegtEintraegeAn_VerbrauchtMaterial_KasseErstattet_OhneMaterialNurOffen()
    {
        var u = DemoUniverse.Create();
        var smelter = St(u, SmelterAlpha);
        var steel = G(u, "steel");
        var plates = G(u, "hull_plates");
        Assert.False(smelter.Market.ContainsKey(steel));
        var need = Maintenance.NeedPerDay(u, u.StationTypes[smelter.TypeKey]);
        Assert.Equal(3, need.Count);
        float steelPerDay = need.Single(n => n.Good == steel).PerDay;
        Assert.Equal(u.StationTypes[smelter.TypeKey].BuildCost * u.Rules.MaintenanceShare * 0.55f / u.Goods[steel].BasePrice, steelPerDay, 3);
        var sys = new MaintenanceSystem();
        sys.Step(u, 50);
        Assert.True(Maintenance.IsPureEntry(u, smelter, steel));
        Assert.Equal(MathF.Max(1f, steelPerDay * u.Rules.MaintenanceStockDays), smelter.Market[steel].Target, 3);
        Assert.Equal(steelPerDay * 50f / u.Rules.DayTicks, smelter.MaintenanceBacklog[steel], 4);   // kein Material: nur offen
        Assert.Contains("offen", Maintenance.StatusText(u, smelter));
        // Material da: Verbrauch (inkl. Rückstand), die Fraktionskasse erstattet der Station den Marktwert
        new MarketSystem().Step(u, 1);
        Assert.True(smelter.Market[steel].Price <= u.Goods[steel].BasePrice * u.Rules.MaintenanceMaxBidFactor + 0.001f);   // Gebotsdeckel
        foreach (var (g, _) in need) smelter.Market[g].Amount = 10f;
        var union = u.Actors[new ActorId(1)];
        union.Treasury.Credits = 10_000f;
        float credits = smelter.Credits;
        // erwartete Erstattung der Hütte: verbrauchte Menge (Tagesanteil + Rückstand) × Marktpreis, höchstens Basispreis × Gebotsdeckel
        float expected = need.Sum(n => 2f * n.PerDay * 50f / u.Rules.DayTicks
            * MathF.Min(smelter.Market[n.Good].Price, u.Goods[n.Good].BasePrice * u.Rules.MaintenanceMaxBidFactor));
        var upkeep = new List<TreasuryUpkeep>();
        u.Events.Raised += e => { if (e is TreasuryUpkeep t && t.Purpose == "maintenance") upkeep.Add(t); };
        sys.Step(u, 50);
        Assert.Equal(10f - 2f * steelPerDay * 50f / u.Rules.DayTicks, smelter.Market[steel].Amount, 3);
        Assert.Empty(smelter.MaintenanceBacklog);
        Assert.Equal("gedeckt", Maintenance.StatusText(u, smelter));
        float paid = upkeep.Where(t => t.Actor == union.Id).Sum(t => t.Amount);
        Assert.True(paid > 0f);
        Assert.Equal(credits + expected, smelter.Credits, 0.01);   // die Kasse erstattet der Hütte ihr verbrauchtes Material (Toleranz statt Rundung: float bei ~10 000 Cr)
        Assert.True(paid >= expected - 0.01f);                   // (und anderen Union-Stationen ihres)
        Assert.Equal(10_000f - paid, union.Treasury.Credits, 0.01);   // Toleranz statt Rundung (float)
        // lange ohne Material: der Rückstand wächst höchstens auf einen Tagesbedarf, nichts verfällt
        foreach (var (g, _) in need) smelter.Market[g].Amount = 0f;
        sys.Step(u, 100_000);
        Assert.Equal(steelPerDay, smelter.MaintenanceBacklog[steel], 3);
        Assert.True(smelter.MaintenanceBacklog[plates] <= need.Single(n => n.Good == plates).PerDay + 0.001f);
    }

    // ───────── Subvention ─────────

    [Fact]
    public void Subvention_FuelltNotleidendeStationAuf_DeckelJeTag_Ruecklage_NichtFuerUngeschuetzte()
    {
        var u = DemoUniverse.Create();
        var smelter = St(u, SmelterAlpha);   // geschützt (Startstation)
        var steel = St(u, SteelAlpha);
        steel.BankruptcyProtected = false;   // zweites Stahlwerk erreichbar: nicht kritisch → nicht förderfähig
        Assert.True(SubsidySystem.Eligible(u, smelter));
        Assert.False(SubsidySystem.Eligible(u, steel));
        Assert.True(SubsidySystem.Eligible(u, St(u, Colony)));
        foreach (var st in u.Stations.Values) st.Credits = 1e6f;   // alle anderen satt
        smelter.Credits = 0f; steel.Credits = 0f;
        var union = u.Actors[new ActorId(1)];
        union.Treasury.Credits = 100_000f;
        new SubsidySystem().Step(u, 50);
        Assert.Equal(u.Rules.SubsidyPerDay * 50f / u.Rules.DayTicks, smelter.Credits, 2);   // Deckel je Tag
        Assert.Equal(0f, steel.Credits);
        new SubsidySystem().Step(u, (int)u.Rules.DayTicks * 10);
        float floor = WorkingCapital.Of(u, smelter) * u.Rules.SubsidyFloorShare;
        Assert.Equal(floor, smelter.Credits, 1);   // höchstens bis zur Untergrenze
        // Rücklage: die Kasse gibt nichts unter SubsidyReserve aus
        smelter.Credits = 0f;
        union.Treasury.Credits = u.Rules.SubsidyReserve + 10f;
        new SubsidySystem().Step(u, (int)u.Rules.DayTicks);
        Assert.Equal(10f, smelter.Credits, 2);
        Assert.Equal(u.Rules.SubsidyReserve, union.Treasury.Credits, 2);
    }

    // ───────── Lebenszyklus ─────────

    [Fact]
    public void Lebenszyklus_KnappesErz_BautKeineWeitereHuette_SonstDenHersteller()
    {
        var u = DemoUniverse.Create();
        var copper = G(u, "copper");
        var ore = G(u, "copper_ore");
        var energy = G(u, "energy");
        var ratios = new Dictionary<GoodId, float> { [copper] = 10f, [ore] = 10f, [energy] = 1f };
        Assert.Null(StationLifecycleSystem.UpstreamProducer(u, copper, ratios, u.Rules.BuildPriceRatio));   // Erz knapp: Bergbau-Engpass
        ratios[ore] = 1f;
        Assert.Equal("copper_smelter", StationLifecycleSystem.UpstreamProducer(u, copper, ratios, u.Rules.BuildPriceRatio)!.Key);
        // hergestellte Zutat knapp (Kupfer für Elektronik) lenkt nicht um: gebaut wird die Elektronikfabrik
        ratios[G(u, "electronics")] = 10f;
        Assert.Equal("electronics_fab", StationLifecycleSystem.UpstreamProducer(u, G(u, "electronics"), ratios, u.Rules.BuildPriceRatio)!.Key);
    }

    // ───────── Abbau lange untätiger Schiffe ─────────

    [Fact]
    public void LangeUntaetig_MitGeld_WirdAbgebaut_GeldAnBesitzer_BauschiffBleibt()
    {
        var u = DemoUniverse.Create();
        u.Tick = 10_000;
        u.Rules.MaxTraders = 0; u.Rules.MaxMiners = 0;   // keine Nachkäufe in diesem Schritt
        var traders = u.Ships.Values.Where(s => s.Role == ShipRole.Trader && s.IsEconomyAi).OrderBy(s => s.Id.Value).Take(2).ToList();
        var free = traders[0];
        var promoted = traders[1];
        var owner = ActorRules.Promote(u, promoted);
        float ownerBefore = u.Actors[owner].Treasury.Credits;
        var builder = u.Ships.Values.First(s => s.Role == ShipRole.Builder);
        foreach (var s in new[] { free, promoted, builder })
        {
            s.Credits = 5000f; s.Order = null; s.Cargo.Clear();
            s.IdleSince = u.Tick - u.Rules.IdleRetireTicks - 1;
        }
        var retired = new List<ShipRetired>();
        u.Events.Raised += e => { if (e is ShipRetired r) retired.Add(r); };
        new FleetSystem().Step(u, 20);
        Assert.False(u.Ships.ContainsKey(free.Id));
        Assert.False(u.Ships.ContainsKey(promoted.Id));
        Assert.True(u.Ships.ContainsKey(builder.Id));
        Assert.Contains(retired, r => r.Ship == free.Id && !r.ToOwner && r.Credits == 5000f);
        Assert.Contains(retired, r => r.Ship == promoted.Id && r.ToOwner);
        Assert.Equal(ownerBefore + 5000f, u.Actors[owner].Treasury.Credits, 2);
        // kurz untätig: bleibt
        var other = u.Ships.Values.First(s => s.Role == ShipRole.Trader && s.IsEconomyAi);
        other.Credits = 5000f; other.Order = null; other.Cargo.Clear(); other.IdleSince = u.Tick - 100;
        new FleetSystem().Step(u, 20);
        Assert.True(u.Ships.ContainsKey(other.Id));
    }

    // ───────── Kapazitätsgrenze ─────────

    [Fact]
    public void Gueteklasse_DieDieKapazitaetSprengt_FaelltAufStandardZurueck_Deterministisch()
    {
        var a = DemoUniverse.Create();
        var b = DemoUniverse.Create();
        var cls = a.ShipClasses["heavy_freighter"];
        var model = a.ShipModels.Values.First(m => m.ClassKey == cls.Key);
        a.Rules.QualityChances = b.Rules.QualityChances = new() { 0f, 0f, 0f, 1f };   // immer Meisterwerk gewürfelt
        var qa = ShipQualityRoll.Roll(a, cls, cls.Slots, model.Fit, null, null, accept: (_, _) => false);
        var qb = ShipQualityRoll.Roll(b, b.ShipClasses[cls.Key], cls.Slots, model.Fit, null, null);
        Assert.Equal(ShipQuality.Masterwork, qa.Rolled);
        Assert.Equal(ShipQuality.Standard, qa.Grade);           // abgelehnt: Werksausstattung
        Assert.Equal(model.Fit, qa.Fit);
        Assert.NotEqual(ShipQuality.Standard, qb.Grade);         // ohne Prüfung: bessere Stufe
        // deterministisch: dieselbe Ablehnung in einer frischen Welt ergibt dieselbe Zufallsfolge
        var c = DemoUniverse.Create();
        c.Rules.QualityChances = new() { 0f, 0f, 0f, 1f };
        ShipQualityRoll.Roll(c, c.ShipClasses[cls.Key], cls.Slots, model.Fit, null, null, accept: (_, _) => false);
        Assert.Equal(a.Rng.State, c.Rng.State);
    }

    // ───────── Kleinkram ─────────

    [Fact]
    public void ServiceAtBase_TanktNichtOhneTreibstoff()
    {
        var u = DemoUniverse.Create();
        Assert.False(u.Rules.FuelActive);
        var police = u.Ships.Values.First(s => s.Role == ShipRole.Police);
        police.Fuel.Clear();
        police.Hull = police.MaxHull;
        var owner = u.Actors[police.Owner];
        float before = owner.Treasury.Credits = 50_000f;
        PirateSystem.ServiceAtBase(u, police, "police");
        Assert.Equal(before, owner.Treasury.Credits);
        Assert.Equal(0f, police.TotalFuel);
        u.Rules.FuelActive = true;   // mit Treibstoff: tankt wie bisher aus der Kasse
        PirateSystem.ServiceAtBase(u, police, "police");
        Assert.True(owner.Treasury.Credits < before);
        Assert.True(police.TotalFuel > 0f);
    }

    // ───────── Persistenz ─────────

    [Fact]
    public void Roundtrip_MitInstandhaltungsRueckstand_BitGleich()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 2000);
        Assert.Contains(u.Stations.Values, s => s.MaintenanceBacklog.Count > 0 || s.Market.Keys.Any(g => Maintenance.IsPureEntry(u, s, g)));
        var st = St(u, SmelterAlpha);
        st.MaintenanceBacklog[G(u, "steel")] = 0.75f;
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Equal(0.75f, loaded.Stations[st.Id].MaintenanceBacklog[G(u, "steel")]);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        new SimulationRunner().Run(u, 1000);
        new SimulationRunner().Run(loaded, 1000);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    // ───────── Langlauf der unveränderten Standardwelt ─────────

    [Fact]
    public void Standardwelt_20000Ticks_KapazitaetHaelt_KeineGeister_EndwarenFliessen_ExportkassenNichtLeer()
    {
        var u = DemoUniverse.Create();
        Assert.False(u.Rules.FuelActive);
        Assert.False(u.Rules.ResearchActive);
        var watch = new MarketWatch(u);
        var exported = new Dictionary<GoodId, float>();
        float smelterSubsidy = 0f;   // Phase 16: Kupferhütte Alpha
        u.Events.Raised += e =>
        {
            if (e is GoodsExported ge) exported[ge.Good] = exported.GetValueOrDefault(ge.Good) + ge.Amount;
            else if (e is StationSubsidized ss && ss.Station.Value == SmelterAlpha) smelterSubsidy += ss.Amount;
        };
        var runner = new SimulationRunner();
        float maxTrade = 0f, maxMine = 0f;
        for (int t = 1; t <= 20_000; t++)
        {
            runner.Step(u);
            if (t >= 2000 && t % 20 == 0) watch.Sample(u);
            if (t % 200 == 0)
            {
                maxTrade = MathF.Max(maxTrade, ShipJobs.UsedUnits(u, ShipJobs.ByKey("trade")!));
                maxMine = MathF.Max(maxMine, ShipJobs.UsedUnits(u, ShipJobs.ByKey("mine")!));
            }
        }
        // Flottenkapazität hält (Grenze + Überhang; Gründer mit Laser Mk2 zählen beim Bergbau mehr als 1)
        Assert.True(maxTrade <= u.Rules.MaxTraders + u.Rules.FleetCapacityOvershoot + 0.001f, $"Handel max {maxTrade}");
        Assert.True(maxMine <= u.Rules.MaxMiners + u.Rules.FleetCapacityOvershoot + 1.0f, $"Bergbau max {maxMine}");
        // Phase 19: gezählt in Kapazitätseinheiten (Phase-6-Semantik) statt Schiffen — die Flotte kauft jetzt auch einen Behemoth (5 Einheiten)
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.ByKey("trade")!) >= 12f, "Händler brechen nicht ein");
        // keine Geisteraufträge: Auftrag mit Reise oder an der Station/am Feld in Arbeit; GoTo/Bau nie ohne Reise am falschen Ort
        foreach (var s in u.Ships.Values)
        {
            Assert.False(s.Order is GoToOrder && s.Transit is null, $"Geister-GoTo bei Schiff {s.Id.Value}");
            // Phase 19: ein Restverkauf hat keine Einkaufsstation (Id 0) und ist immer auf dem Weg zum Käufer
            if (s.Order is TradeOrder to) Assert.True((u.Stations.ContainsKey(to.Buy) || (to.Buy.Value == 0 && s.Phase == TradePhase.GoingToSell)) && u.Stations.ContainsKey(to.Sell), $"Handelsauftrag ins Leere bei Schiff {s.Id.Value}");
            if (s.Transit is null) Assert.Contains(s.Id, u.Tracker.ParkedIn(s.Sector));
        }
        // Endwaren fließen: alle drei Endwaren wurden gefahren, Nahrung und Konsumgüter auch exportiert
        foreach (var key in new[] { "food", "consumer_goods", "luxury_goods" }) Assert.True(watch.Trips(G(u, key)) > 20, key);
        Assert.True(exported.GetValueOrDefault(G(u, "food")) > 1000f);
        Assert.True(exported.GetValueOrDefault(G(u, "consumer_goods")) > 20f);
        // Exportkassen nicht dauerhaft leer
        foreach (var st in u.Stations.Values.Where(s => ExportRules.ConfigOf(u, s) is not null))
            Assert.True(watch.EmptyShare(st.Id) < 0.25f, $"{st.Name}: Kasse {100 * watch.EmptyShare(st.Id):0}% leer");
        // Werft: Mk1-Reaktor und -Triebwerk nicht dauernd am Deckel
        foreach (var key in new[] { "reactor_mk1", "chem_engine_s" }) Assert.True(watch.Share(new StationId(Yard), G(u, key)).Upper < 0.5f, key);
        // Phase 16: Kupferkette — die Kupferhütte lebt nicht von der Subvention (vorher 55 % der Zeit leer, 15 500 Cr Subvention), Kupfererz unter 3×
        Assert.True(watch.EmptyShare(new StationId(SmelterAlpha)) < 0.25f, $"Kupferhütte: Kasse {100 * watch.EmptyShare(new StationId(SmelterAlpha)):0}% leer");
        Assert.True(smelterSubsidy < 3000f, $"Kupferhütte: Subvention {smelterSubsidy:0} Cr");
        Assert.True(watch.AverageRatio(G(u, "copper_ore"), 15_000) < 3f, $"Kupfererz {watch.AverageRatio(G(u, "copper_ore"), 15_000):0.00}×");
        // Kassen horten nicht: deutlich unter der Hälfte des Geldes
        var snap = EconomyStats.Snapshot(u);
        Assert.True(snap.TreasuryCredits < 0.5f * snap.TotalCredits, $"Kassen {snap.TreasuryCredits:0} von {snap.TotalCredits:0}");
    }
}
