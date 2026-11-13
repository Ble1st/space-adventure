using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Events;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 17 (offene Punkte aus Phase 16): eine Mengenregel für Verkäufe (<see cref="TradeActions.SaleUnits"/> — Anzeige und Ausführung stimmen überein,
/// Bruchteile aus dem Bergbau bleiben verkäuflich) und die Zwischenspeicher der Leistungsarbeit (Rollen je Station, Marktindex, Schnappschüsse nach Id,
/// Routen-Cache über Ticks), die das Verhalten nicht ändern dürfen.
/// </summary>
public class OpenPointsTests
{
    static (Universe u, Ship ship, Station st, GoodId ore) SellSetup()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);   // Preise und abgeleitete Annahmegrenzen
        var st = u.Stations[new StationId(1)];   // Stahlwerk Alpha: nimmt Eisenerz
        var ship = u.Ships[u.PlayerShip];
        ship.Cargo.Clear();
        Assert.True(st.Market[DemoUniverse.Ore].Price > 0f);
        return (u, ship, st, DemoUniverse.Ore);
    }

    /// <summary>Setzt die Annahme (Lagerplatz) der Station für die Ware auf genau <paramref name="accept"/> Einheiten und ihre Kasse auf <paramref name="credits"/>.</summary>
    static void Limit(Universe u, Station st, GoodId good, float accept, float credits)
    {
        var e = st.Market[good];
        e.Amount += st.AcceptableUnits(good, u.Rules) - accept;
        st.Credits = credits;
        Assert.Equal(accept, st.AcceptableUnits(good, u.Rules), 3);
    }

    [Fact]
    public void SaleUnits_AllesOderGanzeEinheiten()
    {
        Assert.Equal(0.4f, TradeActions.SaleUnits(0.4f, 0.4f, 0.5f));   // Rest passt ganz → ganz
        Assert.Equal(0f, TradeActions.SaleUnits(0.4f, 0.4f, 0.3f));     // Rest passt nicht ganz → nichts (kein Bruchteil eines Bruchteils)
        Assert.Equal(0f, TradeActions.SaleUnits(3f, 3f, 0.6f));         // Annahme unter einer Einheit → nichts
        Assert.Equal(2f, TradeActions.SaleUnits(3.4f, 3.4f, 2.5f));     // nur ganze Einheiten
        Assert.Equal(3.4f, TradeActions.SaleUnits(3.4f, 3.4f, 3.4f));   // alles
        Assert.Equal(1f, TradeActions.SaleUnits(3.4f, 1f, 100f));       // gewünschte Menge
        Assert.Equal(0.4f, TradeActions.SaleUnits(0.4f, 1f, 100f));     // Wunsch ≥ Bestand = alles
        Assert.Equal(0f, TradeActions.SaleUnits(0f, 1f, 100f));
    }

    /// <summary>Für viele Kombinationen aus Bestand, Annahme und Kasse: Angebotsmenge &gt; 0 (Taste aktiv) ⇔ Execute gelingt, und es bewegt genau die Angebotsmenge.</summary>
    [Fact]
    public void Verkaufsangebot_StimmtMitDerAusfuehrungUeberein()
    {
        var trade = new TradeService();
        int cases = 0, enabledCases = 0;
        foreach (float held in new[] { 0.4f, 1f, 3.4f, 7f })
        foreach (float accept in new[] { 0.3f, 0.5f, 2.5f, 3.4f, 10f })
        foreach (float payUnits in new[] { 0.45f, 2.5f, 1000f })
        foreach (bool viaQty in new[] { false, true })
        {
            var (u, ship, st, ore) = SellSetup();
            float price = st.Market[ore].Price;
            Limit(u, st, ore, accept, payUnits * price);
            ship.Cargo[ore] = held;
            var offer = trade.Offers(u, st, ship).Single(o => o.Key == $"sell:{ore.Value}");
            bool enabled = offer.Amount > 0f;
            // Oberfläche: Menge aus dem Editor (ganze Zahl ≥ 1, höchstens der aufgerundete Bestand) oder „alles“
            float qty = viaQty ? MathF.Max(1f, MathF.Ceiling(held)) : 0f;
            float walletBefore = u.WalletOf(ship).Credits;
            var r = trade.Execute(u, st, ship, new ServiceRequest($"sell:{ore.Value}", default, qty));
            Assert.Equal(enabled, r.Success);
            if (enabled)
            {
                Assert.Equal(offer.Amount, r.Units, 4);
                Assert.Equal(held - offer.Amount, ship.Cargo.GetValueOrDefault(ore), 4);
                Assert.True(st.Credits >= -0.001f);
                Assert.Null(offer.Note is { } n && r.Units >= held - 0.001f ? n : null);   // alles verkauft → kein Hinweis
                enabledCases++;
            }
            else
            {
                Assert.Equal(held, ship.Cargo.GetValueOrDefault(ore));
                Assert.Equal(walletBefore, u.WalletOf(ship).Credits);
                Assert.Equal(offer.Note, r.Message);   // gleicher Grund in Zeile und Ablehnung
            }
            cases++;
        }
        Assert.Equal(4 * 5 * 3 * 2, cases);
        Assert.InRange(enabledCases, 1, cases - 1);   // beide Richtungen kommen vor
    }

    /// <summary>„Alles verkaufen“: Angebotsmenge &gt; 0 ⇔ Execute gelingt; bewegte Menge und Erlös = Angebot.</summary>
    [Fact]
    public void AllesVerkaufen_StimmtMitDerAusfuehrungUeberein()
    {
        var trade = new TradeService();
        foreach (float accept in new[] { 0.5f, 2.5f, 100f })
        foreach (float held in new[] { 0.4f, 3.4f })
        {
            var (u, ship, st, ore) = SellSetup();
            Limit(u, st, ore, accept, 1_000_000f);
            ship.Cargo[ore] = held;
            var all = trade.Offers(u, st, ship).Single(o => o.Key == TradeService.SellAll);
            var r = trade.Execute(u, st, ship, new ServiceRequest(TradeService.SellAll, default, 0f));
            Assert.Equal(all.Amount > 0f, r.Success);
            Assert.Equal(all.Amount, r.Units, 4);
            Assert.Equal(all.Price, r.Credits, 2);
            if (!r.Success) { Assert.Equal(held, ship.Cargo[ore]); Assert.Equal(all.Note, r.Message); }
        }
    }

    /// <summary>
    /// Bergbau-Bruchteile: Der Mining-Autopilot verkauft über <see cref="TradeActions.SellAll"/>; nimmt die Station alles, bleibt nichts im Laderaum
    /// (auch kein Rest unter einer Einheit); nimmt sie nur einen Teil, bleiben ganze Einheiten plus der Bruchteil an Bord und gehen an der nächsten
    /// Station, die alles nimmt, vollständig weg.
    /// </summary>
    [Fact]
    public void Bergbaurest_BleibtNichtImLaderaumHaengen()
    {
        var (u, ship, st, ore) = SellSetup();
        ship.Cargo[ore] = 37.3f;
        Limit(u, st, ore, 10.6f, 1_000_000f);
        var r1 = TradeActions.SellAll(u, ship, st);
        Assert.Equal(10f, r1.Sold[ore], 4);
        Assert.Equal(27.3f, ship.Cargo[ore], 3);
        // Beste Abnehmerin für den Rest nimmt mindestens eine ganze Einheit oder den ganzen Rest (gleiche Regel wie der Verkauf)
        var next = TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position, exclude: st.Id);
        Assert.NotNull(next);
        Assert.True(TradeActions.Sellable(u, ship, next!).Units >= 1f);
        Limit(u, st, ore, 1000f, 1_000_000f);
        var autopilot = new MiningAutopilot();
        autopilot.Start();
        var r2 = autopilot.OnDocked(u, ship, st);
        Assert.Equal(27.3f, r2!.Sold[ore], 3);
        Assert.False(ship.Cargo.ContainsKey(ore));
        // ein reiner Bruchteil wird an einer Station verkauft, die ihn ganz nimmt — und (Regel von Phase 17, ohne Resttoleranz) nicht an einer, die weniger
        // als ihn nimmt; Phase 18 nimmt ihn dort mit (Resttoleranz_NimmtRestUnterEinerEinheit)
        u.Rules.SaleRestTolerance = 0f;
        ship.Cargo[ore] = 0.37f;
        Limit(u, st, ore, 0.2f, 1_000_000f);
        Assert.Equal(0f, TradeActions.Quote(u, ship, st));
        Assert.Empty(TradeActions.SellAll(u, ship, st).Sold);
        Limit(u, st, ore, 0.5f, 1_000_000f);
        Assert.Equal(0.37f, TradeActions.SellAll(u, ship, st).Sold[ore], 4);
        Assert.False(ship.Cargo.ContainsKey(ore));
    }

    /// <summary>Annahmehinweis nach derselben Regel: ein Bruchteil an Bord, den die Station ganz nimmt, hat keinen Hinweis; nimmt sie weniger, nennt er den Grund.</summary>
    [Fact]
    public void Annahmehinweis_FolgtDerMengenregel()
    {
        var (u, _, st, ore) = SellSetup();
        u.Rules.SaleRestTolerance = 0f;   // Regel von Phase 17; mit Resttoleranz siehe Resttoleranz_NimmtRestUnterEinerEinheit
        Limit(u, st, ore, 0.5f, 1_000_000f);
        var intake = MarketInfo.Acceptance(u, st, ore);
        Assert.False(intake.Accepts);              // keine ganze Einheit
        Assert.Equal(0.5f, intake.Cap, 3);
        Assert.Null(intake.NoteFor(0.4f));          // 0,4 passt ganz
        Assert.Equal(intake.Reason, intake.NoteFor(3f));
        Limit(u, st, ore, 2.5f, 1_000_000f);
        Assert.Equal("nimmt noch 2", MarketInfo.Acceptance(u, st, ore).NoteFor(3f));
    }

    /// <summary>
    /// Phase 18: Ein Überhang unter einer Einheit über die Annahme ist unkritisch — nimmt die Station die Ware überhaupt an und kann sie die ganze Menge
    /// bezahlen, geht alles; Anzeige (Angebotsmenge, Hinweis) und Ausführung folgen derselben Regel. Nicht bei voller Station (Annahme 0), nicht bei
    /// knapper Kasse, nicht bei Baustellen (genaue Stückliste), und ein Überhang ab einer Einheit bleibt bei ganzen Einheiten.
    /// </summary>
    [Fact]
    public void Resttoleranz_NimmtRestUnterEinerEinheit()
    {
        Assert.Equal(1f, new EconomyRules().SaleRestTolerance);
        var trade = new TradeService();
        var (u, ship, st, ore) = SellSetup();
        void Reset() => st.Market[ore].Amount = 0f;   // Annahme jedes Mal frisch setzen (ein Verkauf über die Grenze lässt sie bei 0 stehen)
        float price = st.Market[ore].Price;
        // Rest 0,37 an einer Station, die nur 0,2 annimmt: geht ganz weg
        ship.Cargo[ore] = 0.37f;
        Reset(); Limit(u, st, ore, 0.2f, 1_000_000f);
        var offer = trade.Offers(u, st, ship).Single(o => o.Key == $"sell:{ore.Value}");
        Assert.Equal(0.37f, offer.Amount, 4);
        Assert.Null(offer.Note);
        Assert.Equal(0.37f, TradeActions.SellAll(u, ship, st).Sold[ore], 4);
        Assert.False(ship.Cargo.ContainsKey(ore));
        // 3,4 an Bord, Annahme 2,5: Überhang 0,9 → alles; 4 an Bord: Überhang 1,5 → nur ganze Einheiten (2), Hinweis „nimmt noch 2“
        ship.Cargo[ore] = 3.4f;
        Reset(); Limit(u, st, ore, 2.5f, 1_000_000f);
        Assert.Equal(3.4f, TradeActions.Sellable(u, ship, st).Units, 4);
        Assert.Null(MarketInfo.Acceptance(u, st, ore).NoteFor(3.4f));
        Assert.Equal("nimmt noch 2", MarketInfo.Acceptance(u, st, ore).NoteFor(4f));
        Assert.Equal(3.4f, trade.Execute(u, st, ship, new ServiceRequest($"sell:{ore.Value}", default, 0f)).Units, 4);
        // volle Station (Annahme 0): der Rest bleibt
        ship.Cargo[ore] = 0.4f;
        Reset(); Limit(u, st, ore, 0f, 1_000_000f);
        Assert.Equal(0f, TradeActions.Sellable(u, ship, st).Units);
        Assert.False(trade.Execute(u, st, ship, new ServiceRequest($"sell:{ore.Value}", default, 0f)).Success);
        // knappe Kasse: nur, wenn die ganze Menge bezahlt werden kann
        Reset(); Limit(u, st, ore, 0.2f, 0.3f * price);
        Assert.Equal(0f, TradeActions.Sellable(u, ship, st).Units);
        Reset(); Limit(u, st, ore, 0.2f, 0.5f * price);
        Assert.Equal(0.4f, TradeActions.Sellable(u, ship, st).Units, 4);
        // Regel aus: wie Phase 17
        u.Rules.SaleRestTolerance = 0f;
        Assert.Equal(0f, TradeActions.Sellable(u, ship, st).Units);
    }

    // ───────── Leistung (Phase 17): Zwischenspeicher ändern nichts am Ergebnis ─────────

    [Fact]
    public void Rollencache_FolgtGeaendertenRezepten()
    {
        var u = DemoUniverse.Create();
        var st = u.Stations[new StationId(1)];
        var steel = u.FindGood("steel")!.Id;
        Assert.True(st.Produces(steel) && !st.Consumes(steel));
        Assert.True(st.Consumes(DemoUniverse.Ore));
        st.Production.Clear();
        Assert.False(st.Produces(steel));
        Assert.False(st.Consumes(DemoUniverse.Ore));
        Assert.True(st.IsStockOnly(steel));
        st.Production.Add(new Recipe { Inputs = { [steel] = 1f }, TicksPerCycle = 5 });
        Assert.True(st.Consumes(steel) && !st.Produces(steel) && !st.IsStockOnly(steel));
        st.Production[0] = new Recipe { Outputs = { [steel] = 1f }, TicksPerCycle = 5 };   // gleiche Länge, anderes Rezept
        Assert.True(st.Produces(steel) && !st.Consumes(steel));
        st.Production = new List<Recipe>();
        Assert.False(st.Produces(steel));
    }

    [Fact]
    public void Marktindex_UndSchnappschuesse_FolgenAenderungen()
    {
        var u = DemoUniverse.Create();
        var runner = new SimulationRunner();
        runner.Run(u, 1);
        var luxury = u.FindGood("luxury_goods")!.Id;
        var st = u.Stations[new StationId(1)];
        Assert.DoesNotContain(st, u.StationsTrading(luxury));
        st.Stock(luxury);
        runner.Run(u, 1);   // neuer Tick: der Index sieht den neuen Eintrag (wie vorher: Neuaufbau je Tick)
        var list = u.StationsTrading(luxury);
        Assert.Contains(st, list);
        Assert.Equal(list.Count, u.StationsTradingEntries(luxury).Count);
        Assert.Same(st.Market[luxury], u.StationsTradingEntries(luxury)[list.ToList().IndexOf(st)]);
        Assert.Equal(u.Stations.Values.OrderBy(s => s.Id.Value).Select(s => s.Id), u.StationsById().Select(s => s.Id));

        var snapshot = u.ShipsById();
        var victim = snapshot[0];
        u.Ships.Remove(victim.Id);   // direkt am Dictionary (wie manche Tests)
        Assert.DoesNotContain(victim, u.ShipsById());
        Assert.Contains(victim, snapshot);   // die alte Liste bleibt ein Schnappschuss
        Assert.Equal(u.Ships.Values.OrderBy(s => s.Id.Value).Select(s => s.Id), u.ShipsById().Select(s => s.Id));
    }

    [Fact]
    public void Routencache_UeberTicks_GeschlossenesGateWirktSofort()
    {
        var u = DemoUniverse.Create();
        var runner = new SimulationRunner();
        runner.Run(u, 1);
        var gate = u.Gates.Values.OrderBy(g => g.Id.Value).First();
        var from = gate.Sector;
        var to = u.Gates[gate.Target].Sector;
        Assert.NotNull(u.CachedRoute(from, to));
        runner.Run(u, 1);
        Assert.NotNull(u.CachedRoute(from, to));   // über den Tick behalten (nichts eingeschränkt)
        GateAccess.SetState(u, gate, GateState.Closed);
        Assert.Equal(u.FindRoute(from, to)?.Count, u.CachedRoute(from, to)?.Count);
        runner.Run(u, 1);
        Assert.Equal(u.FindRoute(from, to)?.Count, u.CachedRoute(from, to)?.Count);
        GateAccess.SetState(u, gate, GateState.Open);
        runner.Run(u, 1);
        Assert.Equal(u.FindRoute(from, to)!.Count, u.CachedRoute(from, to)!.Count);
    }

    // ───────── Tests, die seit Phase 16 weniger abdeckten ─────────

    /// <summary>
    /// Geld-Erklärbarkeit je Kasse mit allem, was in der Standardwelt an ist — Sicherheit (Polizeiunterhalt und -käufe, Piratenkosten, Hehlerei, Strafen),
    /// Instandhaltung, Subventionen und Steuerbremse: Für jeden Akteur außer dem Spieler ist Endkasse = Startkasse + Abgaben + Einnahmen + Zoll
    /// + Rückzahlungen + Geld abgebauter Schiffe − Ausgaben − Investitionen aus der Kasse (relative Toleranz für Gleitkomma-Summen).
    /// Der Abgabenvergleich in <c>ActorTests</c> läuft ohne Sicherheit, weil er zwei Welten vergleicht; dieser Test prüft dieselbe Buchhaltung mit allem an.
    /// </summary>
    [Fact]
    public void Kassen_JeAkteurAusEreignissenErklaerbar_MitSicherheitInstandhaltungSubventionUndBremse_20000Ticks()
    {
        var u = DemoUniverse.Create();
        Assert.True(u.Rules.SecurityActive && u.Rules.MaintenanceShare > 0f && u.Rules.SubsidyPerDay > 0f && u.Rules.TreasuryTargetShare > 0f);
        var start = u.Actors.Values.ToDictionary(a => a.Id, a => (double)a.Treasury.Credits);
        var flow = new Dictionary<ActorId, double>();
        var purposes = new SortedSet<string>(StringComparer.Ordinal);
        double sunk = 0;
        void Add(ActorId a, double v) => flow[a] = flow.GetValueOrDefault(a) + v;
        u.Events.Raised += e =>
        {
            switch (e)
            {
                case TaxCollected t: Add(t.Treasury, t.Amount); break;
                case TaxSunk ts: sunk += ts.Amount; break;
                case TreasuryIncome ti: Add(ti.Actor, ti.Amount); purposes.Add("+" + ti.Purpose); break;
                case TreasuryUpkeep k: Add(k.Actor, -k.Amount); purposes.Add("-" + k.Purpose); break;
                case TollPaid p: Add(p.To, p.Amount); break;
                case InvestorChosen { Source: InvestorSource.Treasury } c: Add(c.Holder, -c.Amount); break;
                case ShipRetired { ToOwner: true } sr: Add(sr.Owner, sr.Credits); break;
                case ConstructionAbandoned ab when u.Stations.TryGetValue(ab.Site, out var st) && st.Site is { InvestorActor: > 0 } bs: Add(new ActorId(bs.InvestorActor), ab.Refund); break;
            }
        };
        new SimulationRunner().Run(u, 20_000);
        double scale = flow.Values.Sum(Math.Abs) + 1;
        int checkedActors = 0;
        foreach (var a in u.Actors.Values.Where(a => a.Id != u.PlayerActor))
        {
            double expected = start.GetValueOrDefault(a.Id) + flow.GetValueOrDefault(a.Id);
            Assert.InRange(a.Treasury.Credits, expected - 1e-4 * scale - 1, expected + 1e-4 * scale + 1);
            checkedActors++;
        }
        // abgestiegene Akteure (ohne Besitz und ohne Kasse entfernt) dürfen keine offene Buchung hinterlassen
        foreach (var (id, v) in flow) if (!u.Actors.ContainsKey(id)) Assert.InRange(start.GetValueOrDefault(id) + v, -1, 1);
        Assert.True(checkedActors >= 4);
        // die geprüften Wege kamen im Lauf wirklich vor: Polizei, Piraten, Instandhaltung, Subvention, Regierungsunterhalt, Hehlerei; die Bremse griff
        foreach (var p in new[] { "-police", "-pirates", "-maintenance", "-subsidy", "-government", "+fence", "-ammo" }) Assert.Contains(p, purposes);
        Assert.True(sunk > 0);
    }

    /// <summary>
    /// Der Rohstoff-Filter des Stationsbaus (<see cref="EconomyRules.BuildChecksRawOre"/>, Phase 15) über den ganzen Lebenszyklus: Stahl ist überall knapp;
    /// ist auch Eisenerz knapp, entsteht kein Stahlwerk (der Engpass liegt beim Bergbau); ist Eisenerz da — oder ist der Filter aus —, wird eines gebaut.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]   // Erz knapp, Filter an → keine Hütte
    [InlineData(false, false, true)]   // Erz da, Filter an → Hütte
    [InlineData(true, true, true)]     // Erz knapp, Filter aus → Hütte (alte Regel)
    public void Stationsbau_RohstoffFilter_KeineHuetteBeiKnappemErz(bool oreScarce, bool filterOff, bool expectSteelworks)
    {
        var u = DemoUniverse.Create();
        new MarketSystem().Step(u, 1);
        u.Rules.BuildChecksRawOre = !filterOff;
        u.LastStationBuildTick = -1_000_000;
        var steel = u.FindGood("steel")!.Id;
        var ironOre = u.FindGood("iron_ore")!.Id;
        foreach (var st in u.Stations.Values)
        {
            if (st.Market.TryGetValue(steel, out var e)) e.Amount = 0f;
            if (st.Market.TryGetValue(ironOre, out var o)) o.Amount = oreScarce ? 0f : o.Target;
        }
        var started = new List<ConstructionStarted>();
        u.Events.Raised += e => { if (e is ConstructionStarted c) started.Add(c); };
        new StationLifecycleSystem().Step(u, 20);
        Assert.Equal(expectSteelworks, started.Any(c => c.TypeKey == "steelworks"));
        if (!expectSteelworks) Assert.Empty(started);   // der Filter baut nichts anderes stattdessen
    }

    // ───────── Exportsenken: Gebot je Ware ─────────

    [Fact]
    public void Exportgebot_JeWare_InhaltPruefungDeckelUndSpeichern()
    {
        var u = DemoUniverse.Create();
        var gamma = u.Stations[new StationId(39)];
        var cfg = ExportRules.ConfigOf(u, gamma)!;
        var food = u.FindGood("food")!.Id; var cg = u.FindGood("consumer_goods")!.Id; var lux = u.FindGood("luxury_goods")!.Id;
        Assert.Equal(u.Rules.ExportMaxBidFactor, cfg.BidOf(food, u.Rules));   // Nahrung: Regel (0,9×)
        Assert.Equal(1.25f, cfg.BidOf(cg, u.Rules));
        Assert.Equal(1.25f, cfg.BidOf(lux, u.Rules));
        // Deckel nach dem Markt: leeres Lager bietet genau Basis × Gebot der Ware; UpperCap der Messung folgt derselben Zahl
        new SimulationRunner().Run(u, 1);
        Assert.Equal(u.Goods[cg].BasePrice * 1.25f, gamma.Market[cg].Price, 2);
        Assert.True(gamma.Market[food].Price <= u.Goods[food].BasePrice * u.Rules.ExportMaxBidFactor + 0.01f);
        Assert.Equal(1.25f, MarketWatch.UpperCap(u, gamma, lux), 3);
        // gespeichert und geladen
        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(1.25f, ExportRules.ConfigOf(loaded, loaded.Stations[gamma.Id])!.BidOf(lux, loaded.Rules));
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
        // Prüfung: Faktor außerhalb (0, 4], unbekannter Schlüssel
        foreach (var (key, f, part) in new[] { ("luxury_goods", 0f, "(0, 4]"), ("luxury_goods", 5f, "(0, 4]"), ("steel", 1f, "steel"), ("ore", 1f, "ore") })
        {
            var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
            def.StationTypes.First(t => t.Key == "trade_post").Export!.Bids = new() { [key] = f };
            Assert.Contains(part, Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def)).Message);
        }
        // Ware gewinnt über Kategorie
        var d2 = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        d2.StationTypes.First(t => t.Key == "trade_post").Export!.Bids = new() { ["end"] = 1.1f, ["food"] = 0.8f };
        var u2 = WorldBuilder.Build(d2);
        var c2 = ExportRules.ConfigOf(u2, u2.Stations[new StationId(39)])!;
        Assert.Equal((0.8f, 1.1f, 1.1f), (c2.BidOf(food, u2.Rules), c2.BidOf(cg, u2.Rules), c2.BidOf(lux, u2.Rules)));
    }

    /// <summary>
    /// Phase 17 (Abnahme der Exportsenken, Standardwelt 20000 Ticks): Konsum- und Luxusgüter erreichen die Senken — jede Senke steht bei beiden Waren
    /// weniger als die Hälfte der Messzeit leer am Gebotsdeckel, beide werden in nennenswerter Menge exportiert, und die Kolonie bleibt versorgt
    /// (Konsumgüter-Takte wie ohne Export: alle; Luxus fast alle).
    /// </summary>
    [Fact]
    public void Exportsenken_BekommenKonsumUndLuxus_KolonieBleibtVersorgt_20000Ticks()
    {
        var u = DemoUniverse.Create();
        var watch = new MarketWatch(u);
        var cg = u.FindGood("consumer_goods")!.Id; var lux = u.FindGood("luxury_goods")!.Id;
        var exported = new Dictionary<GoodId, float>();
        u.Events.Raised += e => { if (e is GoodsExported ge) exported[ge.Good] = exported.GetValueOrDefault(ge.Good) + ge.Amount; };
        var colony = u.Stations[new StationId(10)];
        int colonyCgEmpty = 0, colonyLuxEmpty = 0, samples = 0;
        var runner = new SimulationRunner();
        for (int t = 0; t < 20_000; t += 20)
        {
            runner.Run(u, 20);
            if (u.Tick < 2000) continue;
            watch.Sample(u);
            samples++;
            if (colony.Market[cg].Amount < 1f) colonyCgEmpty++;
            if (colony.Market[lux].Amount < 1f) colonyLuxEmpty++;
        }
        foreach (var sink in u.Stations.Values.Where(s => ExportRules.ConfigOf(u, s) is not null))
            foreach (var g in new[] { cg, lux })
                Assert.True(watch.Share(sink.Id, g).Upper < 0.5f, $"{sink.Name}: {u.Goods[g].Name} {watch.Share(sink.Id, g).Upper:P0} leer am Deckel");
        Assert.True(exported.GetValueOrDefault(cg) > 500f, $"Konsumgüter exportiert {exported.GetValueOrDefault(cg):0}");
        Assert.True(exported.GetValueOrDefault(lux) > 100f, $"Luxusgüter exportiert {exported.GetValueOrDefault(lux):0}");
        Assert.True(colonyCgEmpty < samples / 10, $"Kolonie ohne Konsumgüter {colonyCgEmpty}/{samples}");
        // Runde 9: Median über drei Seeds — der Anteil schwankt je Pfad stark (gemessen ohne Tanker/Kurier: 168, 190, 420 von 901; mit: 383, 153, 168)
        var lux3 = new List<int> { colonyLuxEmpty };
        foreach (ulong seed in new ulong[] { 1, 2 })
        {
            var w = TestWorlds.Default(seed, fuel: false, research: false);
            var col = w.Stations[new StationId(10)];
            int empty = 0;
            var r2 = new SimulationRunner();
            for (int t = 0; t < 20_000; t += 20) { r2.Run(w, 20); if (w.Tick >= 2000 && col.Market[lux].Amount < 1f) empty++; }
            lux3.Add(empty);
        }
        lux3.Sort();
        Assert.True(lux3[1] < samples / 3, $"Kolonie ohne Luxus (Median über drei Seeds) {string.Join("/", lux3)} von {samples}");
        Assert.All(watch.CapHints(u).Where(h => MarketWatch.IsExportDemand(u, h)), h => Assert.Fail($"Exportnachfrage offen: {u.Stations[h.Station].Name}"));
    }
}
