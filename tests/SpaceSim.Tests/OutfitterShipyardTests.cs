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
/// Phase 5c: Dienste „outfitter“ (Module ein-/ausbauen, gemeinsame Fit-Regel <see cref="FitRules"/>) und „shipdealer“
/// (Schiffskauf nach <see cref="ShipPurchase"/>), mehrere Spielerschiffe (<see cref="PlayerFleet"/>), Handelsstation, Skiff-Modelle,
/// Startpakete, Save v8 mit Migration.
/// </summary>
public class OutfitterShipyardTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    static readonly OutfitterService Outfitter = new();
    static readonly ShipDealerService Dealer = new();

    /// <summary>Standardwelt nach einem Tick (Preise gesetzt), Spieler an der Werft angedockt mit reichlich Geld.</summary>
    static (Universe u, Station yard, Ship player) AtYard(float credits = 50_000f)
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        var yard = u.Stations.Values.First(s => s.TypeKey == "shipyard");
        var player = u.Ships[u.PlayerShip];
        u.MoveShip(player, yard.Sector);
        u.PlayerSector = yard.Sector;
        player.Position = yard.DockPoint(player.Id);
        u.PlayerWallet.Credits = credits;
        return (u, yard, player);
    }

    static ServiceResult Exec(IStationService svc, Universe u, Station st, Ship ship, string offer) =>
        svc.Execute(u, st, ship, new ServiceRequest(offer, default, 0f));

    /// <summary>Zustand, der sich bei einem abgelehnten Dienst nicht ändern darf.</summary>
    static string Snapshot(Universe u, Station st, Ship ship) =>
        $"{UniverseHash.Compute(u)}|{string.Join(",", ship.Fit)}|{ship.CargoCapacity}|{u.PlayerWallet.Credits}|{st.Credits}|" +
        string.Join(";", st.Market.OrderBy(k => k.Key.Value).Select(kv => $"{kv.Key.Value}:{kv.Value.Amount}")) + "|" +
        string.Join(";", ship.Cargo.OrderBy(k => k.Key.Value).Select(kv => $"{kv.Key.Value}:{kv.Value}"));

    static int Slot(Ship ship, string type) => ship.Slots.FindIndex(s => s.Type == type);

    // ───────── Content ─────────

    [Fact]
    public void Content_SkiffModelle_Handelsstation_DiensteUndSchiffshaendler()
    {
        var u = DemoUniverse.Create();
        var skiff = u.ShipClasses["skiff"];
        Assert.Equal("ship_kit_light", skiff.Kit);
        foreach (var key in new[] { "pip", "nomad", "prospector" })
        {
            Assert.Equal("skiff", u.ShipModels[key].ClassKey);
            Assert.Null(FitRules.Check(u.SlotTypes, u.Modules, skiff, skiff.Slots, u.ShipModels[key].Fit));
            Assert.All(u.ShipModels[key].Fit.Where(k => k is not null), k => Assert.Equal(1, u.Modules[k!].Tier));   // Mk1-Module aus 5a
        }
        var stats = (string k) => ShipStats.Compute(skiff, u.ShipModels[k].Fit.Select(m => m is null ? null : u.Modules[m]).ToList());
        Assert.True(stats("prospector").Capabilities.HasFlag(Capability.Mining));
        Assert.False(stats("nomad").Capabilities.HasFlag(Capability.Mining));
        Assert.True(stats("nomad").Cargo > stats("prospector").Cargo);
        Assert.Equal(60f, stats("pip").Hull);

        var trade = u.Stations.Values.Single(s => s.TypeKey == "trade_station");
        Assert.True(trade.BankruptcyProtected);
        Assert.Empty(trade.Production);   // nur Lager, keine Rezepte
        Assert.Equal(new[] { "trade", "outfitter", "shipdealer" }, u.StationTypes["trade_station"].Services);
        var services = StationServices.Default();
        Assert.Equal(new[] { "trade", "outfitter", "shipdealer" }, services.ForStation(u, trade).Select(s => s.Key));
        // Handelsstation montiert nur Skiffs, die Werft alle Klassen
        Assert.All(ShipDealerService.Models(u, trade), m => Assert.Equal("skiff", m.ClassKey));
        Assert.Equal(3, ShipDealerService.Models(u, trade).Count);
        var yard = u.Stations.Values.First(s => s.TypeKey == "shipyard");
        Assert.Equal(u.ShipModels.Count, ShipDealerService.Models(u, yard).Count);
        Assert.Contains("outfitter", services.ForStation(u, yard).Select(s => s.Key));
        // Raffinerie: kein Schiffshändler (ihre Klassen „Bergbauer leicht“/„kleiner Tanker“ gibt es noch nicht)
        Assert.Null(u.StationTypes["fuel_refinery"].ShipDealer);
    }

    [Fact]
    public void ShipDealer_UnbekannteKlasse_WirftWorldFormatException()
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.StationTypes.First(t => t.Key == "trade_station").ShipDealer!.Classes!.Add("lorem");
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
        Assert.Contains("unbekannte Schiffsklasse 'lorem'", ex.Message);
    }

    // ───────── Ausrüster ─────────

    [Fact]
    public void Umruesten_TauschtFrachtmodul_AendertShipStats_BuchtKasseUndLager()
    {
        var (u, yard, player) = AtYard();
        int cargo = Slot(player, "cargo");
        var g60 = u.FindGood("cargo_universal_60")!.Id;
        var g100 = u.FindGood("cargo_universal_100")!.Id;
        float stock60 = yard.Market[g60].Amount, stock100 = yard.Market[g100].Amount, price60 = yard.Market[g60].Price, buyBack = yard.Market[g100].Price;
        float wallet = u.PlayerWallet.Credits, yardCredits = yard.Credits;
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;

        var offer = Outfitter.Offers(u, yard, player).Single(o => o.Key == $"fit:{cargo}:cargo_universal_60");
        Assert.Equal(1f, offer.Amount);
        Assert.Equal(price60, offer.Price);
        var preview = OutfitterService.Preview(u, yard, player, cargo, "cargo_universal_60")!;
        Assert.Equal(100f, preview.Before.Cargo);
        Assert.Equal(60f, preview.After.Cargo);

        var r = Exec(Outfitter, u, yard, player, $"fit:{cargo}:cargo_universal_60");
        Assert.True(r.Success, r.Message);
        Assert.Equal("cargo_universal_60", player.Fit[cargo]);
        Assert.Equal(60f, player.Stats.Cargo);
        Assert.Equal(60f, player.CargoCapacity);
        Assert.Equal(stock60 - 1f, yard.Market[g60].Amount);          // neues Modul aus dem Lager
        Assert.Equal(stock100 + 1f, yard.Market[g100].Amount);        // altes Modul an die Station verkauft
        Assert.Equal(wallet - price60 + buyBack, u.PlayerWallet.Credits, 2);
        Assert.Equal(yardCredits + price60 - buyBack, yard.Credits, 2);
        Assert.Contains(events, e => e is ServicePerformed sp && sp.Service == "outfitter");
        Assert.Contains(events, e => e is ShipRefitted rf && rf.Installed == "cargo_universal_60" && rf.Removed == "cargo_universal_100");
    }

    [Fact]
    public void UngueltigerEinbau_WirdOhneZustandsaenderungAbgelehnt()
    {
        var (u, yard, player) = AtYard();
        int tool = Slot(player, "tool"), cargo = Slot(player, "cargo"), reactor = Slot(player, "reactor"), engine = Slot(player, "engine");
        int events = 0;
        u.Events.Raised += _ => events++;
        var before = Snapshot(u, yard, player);

        // Slotart: Reaktor in den Werkzeug-Slot
        var r = Exec(Outfitter, u, yard, player, $"fit:{tool}:reactor_mk1");
        Assert.False(r.Success);
        Assert.Contains("Slotart", r.Message);
        // Größe: Traktor wird zu M, der Slot ist S
        u.Modules["tractor_beam_s"].Size = SlotSize.M;
        r = Exec(Outfitter, u, yard, player, $"fit:{tool}:tractor_beam_s");
        Assert.False(r.Success);
        Assert.Contains("zu groß", r.Message);
        u.Modules["tractor_beam_s"].Size = SlotSize.S;
        // Leistungsbilanz: Traktor zieht mehr, als der Reaktor liefert
        u.Modules["tractor_beam_s"].PowerDraw = 50f;
        Assert.DoesNotContain(Outfitter.Offers(u, yard, player), o => o.Key == $"fit:{tool}:tractor_beam_s" && o.Amount > 0f);
        r = Exec(Outfitter, u, yard, player, $"fit:{tool}:tractor_beam_s");
        Assert.False(r.Success);
        Assert.Contains("Reaktorleistung", r.Message);
        u.Modules["tractor_beam_s"].PowerDraw = 1f;
        // Spitzenlast ohne Speicherbank
        u.Modules["tractor_beam_s"].PeakDraw = 50f;
        r = Exec(Outfitter, u, yard, player, $"fit:{tool}:tractor_beam_s");
        Assert.False(r.Success);
        Assert.Contains("Spitzenlast", r.Message);
        u.Modules["tractor_beam_s"].PeakDraw = 0f;
        // Pflicht-Slots: kein Ausbau, kein Angebot
        Assert.DoesNotContain(Outfitter.Offers(u, yard, player), o => o.Key == $"remove:{reactor}" || o.Key == $"remove:{engine}");
        r = Exec(Outfitter, u, yard, player, $"remove:{reactor}");
        Assert.False(r.Success);
        Assert.Contains("Pflichtslot", r.Message);
        Assert.False(Exec(Outfitter, u, yard, player, $"remove:{engine}").Success);
        // Fracht passt nicht mehr: 90 Einheiten an Bord, Tausch auf 60er-Raum
        var iron = DemoUniverse.IronOre;
        player.Cargo[iron] = 90f;
        var beforeCargo = Snapshot(u, yard, player);
        r = Exec(Outfitter, u, yard, player, $"fit:{cargo}:cargo_universal_60");
        Assert.False(r.Success);
        Assert.Contains("Fracht", r.Message);
        Assert.Equal(beforeCargo, Snapshot(u, yard, player));
        player.Cargo.Remove(iron);
        // Strikte Schlüssel
        foreach (var bad in new[] { "fit", "fit:x:reactor_mk1", $"fit:{tool}", $"fit:{tool}:lorem", "remove", "remove:-1", "remove:99", $"fit:{tool}:mining_laser_1:x", "buy:1", "" })
            Assert.False(Exec(Outfitter, u, yard, player, bad).Success, bad);
        // zu wenig Credits
        u.PlayerWallet.Credits = 1f;
        Assert.False(Exec(Outfitter, u, yard, player, $"fit:{tool}:tractor_beam_s").Success);
        u.PlayerWallet.Credits = 50_000f;

        Assert.Equal(before, Snapshot(u, yard, player));
        Assert.Equal(0, events);
    }

    [Fact]
    public void Ausbau_GehtAnDieStation_OderInDieFracht_EinbauAusDerFrachtKostenlos()
    {
        var (u, yard, player) = AtYard();
        int tool = Slot(player, "tool");
        var laser = u.FindGood("mining_laser_1")!.Id;
        Assert.True(player.Has(Capability.Mining));

        // Station nimmt an (Lagerposten mit Platz): Ankauf zum Marktpreis
        float price = yard.Market[laser].Price, wallet = u.PlayerWallet.Credits, stock = yard.Market[laser].Amount;
        var r = Exec(Outfitter, u, yard, player, $"remove:{tool}");
        Assert.True(r.Success, r.Message);
        Assert.Null(player.Fit[tool]);
        Assert.False(player.Has(Capability.Mining));   // Fähigkeit folgt dem Fit
        Assert.Equal(stock + 1f, yard.Market[laser].Amount);
        Assert.Equal(wallet + price, u.PlayerWallet.Credits, 2);
        Assert.Equal(0f, player.Cargo.GetValueOrDefault(laser));

        // Wieder einbauen (aus dem Lager), dann ausbauen bei voller Station → in die Fracht
        Assert.True(Exec(Outfitter, u, yard, player, $"fit:{tool}:mining_laser_1").Success);
        yard.Market[laser].Amount = yard.Market[laser].Target * u.Rules.OverstockFactor;   // Lager voll
        wallet = u.PlayerWallet.Credits;
        r = Exec(Outfitter, u, yard, player, $"remove:{tool}");
        Assert.True(r.Success, r.Message);
        Assert.Equal(1f, player.Cargo[laser]);
        Assert.Equal(wallet, u.PlayerWallet.Credits);

        // Einbau aus der Fracht: kostenlos, Fracht geht ab
        var offer = Outfitter.Offers(u, yard, player).Single(o => o.Key == $"fit:{tool}:mining_laser_1");
        Assert.Equal(0f, offer.Price);
        Assert.Equal(1f, offer.Held);
        r = Exec(Outfitter, u, yard, player, $"fit:{tool}:mining_laser_1");
        Assert.True(r.Success, r.Message);
        Assert.Equal(wallet, u.PlayerWallet.Credits);
        Assert.Equal(0f, player.Cargo.GetValueOrDefault(laser));
        Assert.True(player.Has(Capability.Mining));
    }

    [Fact]
    public void LaserOhneMiningModul_FeuertNicht()
    {
        var (u, yard, player) = AtYard();
        Assert.True(Exec(Outfitter, u, yard, player, $"remove:{Slot(player, "tool")}").Success);
        var sector = u.Sectors[player.Sector];
        var rock = sector.Asteroids.First(a => a.Minable);
        var chunks = new SpaceSim.Core.Mining.OreChunks();
        Assert.Equal(SpaceSim.Core.Mining.MineResult.NoLaser, chunks.TryFire(u, player, sector, rock, rock.Position + new Vec2(rock.Radius + 2f, 0f)));
    }

    // ───────── Schiffskauf ─────────

    [Fact]
    public void Spielerkauf_VerbrauchtBausatzUndModule_WerftBekommtGeld_NeuesSchiffAktiv_AltesGeparkt()
    {
        var (u, yard, player) = AtYard();
        u.Rules.QualityChances = new() { 1f, 0f, 0f, 0f };   // Phase 8: Güteklasse fest Standard — geprüft wird der Verbrauch der Grundregel (der Wurf hing sonst vom Zufallsstand ab, den die Bauschiffe der Startwelt verschieben)
        var model = u.ShipModels["prospector"];
        var needs = ShipPurchase.Materials(u, model)!;
        Assert.Contains(needs, n => u.Goods[n.Good].Key == "ship_kit_light" && n.Units == 1f);
        Assert.Equal(model.Fit.Count(k => k is not null) + 1, needs.Sum(n => n.Units));
        foreach (var n in needs) yard.Stock(n.Good).Amount = MathF.Max(yard.Stock(n.Good).Amount, 3f);
        var stockBefore = needs.ToDictionary(n => n.Good, n => yard.Market[n.Good].Amount);
        var quote = ShipDealerService.Quote(u, yard, player, "prospector")!;
        Assert.True(quote.Buyable);
        float expected = ShipPurchase.Price(u, yard, needs, 0.05f);
        Assert.Equal(expected, quote.Price, 2);
        float wallet = u.PlayerWallet.Credits, yardCredits = yard.Credits;
        int ships = u.Ships.Count;
        var oldId = player.Id;
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;

        var offer = Dealer.Offers(u, yard, player).Single(o => o.Key == "buy:prospector");
        Assert.Equal(1f, offer.Amount);
        var r = Exec(Dealer, u, yard, player, "buy:prospector");
        Assert.True(r.Success, r.Message);

        foreach (var n in needs) Assert.Equal(stockBefore[n.Good] - n.Units, yard.Market[n.Good].Amount);   // Bausatz + Module verbraucht
        Assert.Equal(wallet - quote.Price, u.PlayerWallet.Credits, 2);
        Assert.Equal(yardCredits + quote.Price, yard.Credits, 2);
        Assert.Equal(ships + 1, u.Ships.Count);
        var bought = u.Ships[u.PlayerShip];
        Assert.NotEqual(oldId, bought.Id);
        Assert.Equal("prospector", bought.ModelKey);
        Assert.True(bought.IsPlayer);
        Assert.Equal(0f, bought.Credits);
        Assert.Equal(60f, bought.CargoCapacity);
        Assert.Equal(bought.MaxHull, bought.Hull);
        Assert.Equal(bought.Stats.FuelCapacity, bought.TotalFuel);
        Assert.Same(u.PlayerWallet, u.WalletOf(bought));
        // altes Schiff: Spielerbesitz, an der Werft geparkt, im Index
        var old = u.Ships[oldId];
        Assert.True(old.IsPlayer);
        Assert.Contains(old, PlayerFleet.ParkedAt(u, yard));
        Assert.Contains(oldId, u.Tracker.ParkedIn(yard.Sector));
        Assert.Contains(bought.Id, u.Tracker.ParkedIn(yard.Sector));
        Assert.Equal(2, PlayerFleet.Owned(u).Count);
        // geparkte eigene Schiffe erscheinen in der Momentaufnahme, das aktive nicht
        var snap = Materializer.Snapshot(u, yard.Sector, u.Tick);
        Assert.Contains(snap, s => s.Id == oldId);
        Assert.DoesNotContain(snap, s => s.Id == bought.Id);
        Assert.Contains(events, e => e is ShipPurchased p && p.Ship == bought.Id && p.ModelKey == "prospector");
        Assert.Contains(events, e => e is ActiveShipChanged a && a.Old == oldId && a.New == bought.Id);

        // Wechsel zurück an derselben Station
        Assert.True(PlayerFleet.SwitchTo(u, yard, oldId, out var msg), msg);
        Assert.Equal(oldId, u.PlayerShip);
        Assert.Contains(bought, PlayerFleet.ParkedAt(u, yard));
        Assert.False(PlayerFleet.SwitchTo(u, yard, oldId, out _));   // schon aktiv
        var npc = u.Ships.Values.First(s => !s.IsPlayer);
        Assert.False(PlayerFleet.SwitchTo(u, yard, npc.Id, out _));  // fremdes Schiff
    }

    [Fact]
    public void Spielerkauf_OhneMaterialOderGeld_WirdAbgelehnt_OhneZustandsaenderung()
    {
        var (u, yard, player) = AtYard();
        var kit = u.FindGood("ship_kit_light")!.Id;
        yard.Market[kit].Amount = 0f;
        int events = 0;
        u.Events.Raised += _ => events++;
        var before = Snapshot(u, yard, player);
        var q = ShipDealerService.Quote(u, yard, player, "pip")!;
        Assert.Contains(q.Missing, m => m.Good == kit);
        Assert.Equal(0f, Dealer.Offers(u, yard, player).Single(o => o.Key == "buy:pip").Amount);
        var r = Exec(Dealer, u, yard, player, "buy:pip");
        Assert.False(r.Success);
        Assert.Contains("fehlt", r.Message);

        yard.Market[kit].Amount = 5f;
        foreach (var n in q.Materials) yard.Stock(n.Good).Amount = MathF.Max(yard.Stock(n.Good).Amount, 3f);
        before = Snapshot(u, yard, player);
        u.PlayerWallet.Credits = 10f;
        before = Snapshot(u, yard, player);
        r = Exec(Dealer, u, yard, player, "buy:pip");
        Assert.False(r.Success);
        Assert.Contains("Credits", r.Message);
        foreach (var bad in new[] { "buy:", "buy:lorem", "pip", "sell:pip", "" })
            Assert.False(Exec(Dealer, u, yard, player, bad).Success, bad);
        // Handelsstation verkauft keine Einheitsfrachter
        var trade = u.Stations.Values.Single(s => s.TypeKey == "trade_station");
        u.PlayerWallet.Credits = 50_000f;
        Assert.False(Exec(Dealer, u, trade, player, "buy:unit_trader_100").Success);
        u.PlayerWallet.Credits = 10f;
        Assert.Equal(before, Snapshot(u, yard, player));
        Assert.Equal(0, events);
    }

    [Fact]
    public void Handelsstation_MontiertSkiffAusLeichtemBausatz_MitMontagegebuehr()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        var trade = u.Stations.Values.Single(s => s.TypeKey == "trade_station");
        var player = u.Ships[u.PlayerShip];
        u.MoveShip(player, trade.Sector);
        u.PlayerSector = trade.Sector;
        player.Position = trade.DockPoint(player.Id);
        u.PlayerWallet.Credits = 50_000f;
        var needs = ShipPurchase.Materials(u, u.ShipModels["nomad"])!;
        foreach (var n in needs) trade.Stock(n.Good).Amount = MathF.Max(trade.Stock(n.Good).Amount, 2f);
        float price = ShipPurchase.Price(u, trade, needs, 0.10f);
        var r = Exec(Dealer, u, trade, player, "buy:nomad");
        Assert.True(r.Success, r.Message);
        Assert.Equal(50_000f - price, u.PlayerWallet.Credits, 1);
        Assert.Equal("nomad", u.Ships[u.PlayerShip].ModelKey);
        Assert.False(u.Ships[u.PlayerShip].Has(Capability.Mining));
    }

    [Fact]
    public void KiNeubau_OhneModulRegel_VerbrauchtNurDenBausatz()
    {
        var u = DemoUniverse.Create();
        u.Rules.FleetBuysModules = false;
        u.Rules.FleetChoosesModels = false;   // Phase 6: sonst kauft die Modellwahl (immer mit Modulen)
        var yard = u.Stations.Values.First(s => s.TypeKey == "shipyard");
        var kit = u.FindGood("ship_kit_light")!.Id;
        yard.Stock(kit).Amount = 5f;
        var reactor = u.FindGood("reactor_mk1")!.Id;
        float reactors = yard.Market[reactor].Amount, credits = yard.Credits;
        u.Ships.Values.First(s => s.Role == ShipRole.Trader).Credits = 20_000f;
        var built = new List<FleetShipBuilt>();
        u.Events.Raised += e => { if (e is FleetShipBuilt b) built.Add(b); };
        new FleetSystem().Step(u, 20);
        Assert.Equal(4f, yard.Market[kit].Amount);
        Assert.Equal(reactors, yard.Market[reactor].Amount);
        Assert.Equal(credits + u.Rules.NewShipCost, yard.Credits, 2);
        Assert.False(built.Single().WithModules);
    }

    [Fact]
    public void KiNeubau_FehlenModule_FaelltAufDenBausatzZurueck()
    {
        var u = DemoUniverse.Create();
        var yard = u.Stations.Values.First(s => s.TypeKey == "shipyard");
        var kit = u.FindGood("ship_kit_light")!.Id;
        yard.Stock(kit).Amount = 5f;
        yard.Market[u.FindGood("reactor_mk1")!.Id].Amount = 0f;   // kein Reaktor: volle Regel nicht erfüllbar
        u.Ships.Values.First(s => s.Role == ShipRole.Trader).Credits = 20_000f;
        var built = new List<FleetShipBuilt>();
        u.Events.Raised += e => { if (e is FleetShipBuilt b) built.Add(b); };
        float credits = yard.Credits;
        new FleetSystem().Step(u, 20);
        Assert.False(built.Single().WithModules);
        Assert.Equal(4f, yard.Market[kit].Amount);
        Assert.Equal(credits + u.Rules.NewShipCost, yard.Credits, 2);
    }

    // ───────── Startpakete ─────────

    [Fact]
    public void Startpakete_ErzeugenWeltMitGewaehltemModellUndGeld_OhneWahlWieBisher()
    {
        var options = WorldBuilder.DefaultStartOptions();
        Assert.Equal(new[] { "miner", "trader", "generalist" }, options.Select(o => o.Key));
        foreach (var (key, model) in new[] { ("miner", "prospector"), ("trader", "nomad"), ("generalist", "pip") })
        {
            var u = WorldBuilder.BuildDefault(startOption: key);
            var p = u.Ships[u.PlayerShip];
            var o = options.Single(x => x.Key == key);
            Assert.Equal(model, p.ModelKey);
            Assert.Equal(o.Credits, u.PlayerWallet.Credits);
            Assert.True(p.IsPlayer);
            Assert.Equal(p.MaxHull, p.Hull);
            Assert.Equal(p.Stats.Cargo, p.CargoCapacity);
            Assert.Equal(p.Stats.FuelCapacity, p.TotalFuel);
            new SimulationRunner().Run(u, 200);   // läuft
        }
        Assert.True(WorldBuilder.BuildDefault(startOption: "generalist").Ships[new ShipId(100)].Has(Capability.Mining));
        // ohne Wahl: Einheitsmodell und Startgeld des Spielereintrags (bisheriges Verhalten)
        var d = WorldBuilder.BuildDefault();
        Assert.Equal("unit_player", d.Ships[d.PlayerShip].ModelKey);
        Assert.Equal(500f, d.PlayerWallet.Credits);
        Assert.Equal(UniverseHash.Compute(DemoUniverse.Create()), UniverseHash.Compute(d));
        Assert.Throws<WorldFormatException>(() => WorldBuilder.BuildDefault(startOption: "lorem"));
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Player.StartOptions[0].Model = "lorem";
        Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
    }

    // ───────── Determinismus und Save ─────────

    static Universe WorldWithPurchase(ulong seed)
    {
        var u = WorldBuilder.BuildDefault(seed);
        new SimulationRunner().Run(u, 1);
        var yard = u.Stations.Values.First(s => s.TypeKey == "shipyard");
        var player = u.Ships[u.PlayerShip];
        u.MoveShip(player, yard.Sector);
        u.PlayerSector = yard.Sector;
        player.Position = yard.DockPoint(player.Id);
        u.PlayerWallet.Credits = 50_000f;
        Assert.True(Exec(Outfitter, u, yard, player, $"fit:{Slot(player, "cargo")}:cargo_universal_60").Success);
        foreach (var n in ShipPurchase.Materials(u, u.ShipModels["pip"])!) yard.Stock(n.Good).Amount = MathF.Max(yard.Stock(n.Good).Amount, 3f);
        Assert.True(Exec(Dealer, u, yard, player, "buy:pip").Success);
        return u;
    }

    [Fact]
    public void Determinismus_UndSaveRoundtrip_MitZweiSpielerschiffen_BitIdentisch()
    {
        var a = WorldWithPurchase(7);
        var b = WorldWithPurchase(7);
        var runner = new SimulationRunner();
        runner.Run(a, 1500);
        new SimulationRunner().Run(b, 1500);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));

        var json = SaveGameJson.Write(a, Now);
        Assert.Contains("\"Version\":11", json);
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
        Assert.Equal(a.PlayerShip, loaded.PlayerShip);
        Assert.Equal(2, PlayerFleet.Owned(loaded).Count);
        Assert.Equal("pip", loaded.Ships[loaded.PlayerShip].ModelKey);
        var oldShip = PlayerFleet.Owned(loaded).Single(s => s.Id != loaded.PlayerShip);
        Assert.Equal("cargo_universal_60", oldShip.Fit[Slot(oldShip, "cargo")]);   // Umrüstung bleibt im Schiff
        Assert.Equal(60f, oldShip.CargoCapacity);
        Assert.Equal(0.05f, loaded.StationTypes["shipyard"].ShipDealer!.Assembly);
        Assert.Equal(new[] { "skiff" }, loaded.StationTypes["trade_station"].ShipDealer!.Classes);
        runner.Run(a, 1000);
        new SimulationRunner().Run(loaded, 1000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
    }

    /// <summary>
    /// Demotet einen aktuellen Save (v8) auf v7: Handelsstation (Typ und Stationen), Schiffshändler, die Dienste outfitter/shipdealer, die
    /// neuen Werft-Lagerposten und die Skiff-Klasse mit ihren Modellen entfallen — so sah ein Stand vor Phase 5c aus.
    /// </summary>
    internal static string ToV7Json(string v8Json)
    {
        var node = JsonNode.Parse(ConstructionTests.ToV10Json(v8Json))!.AsObject();   // Phase 8: erst den Bau-Content entfernen
        node["Version"] = 7;
        var goodIds = node["Goods"]!.AsArray().ToDictionary(g => (string)g!["Key"]!, g => (int)g!["Id"]!);
        var newYardStocks = new[] { "cargo_universal_60", "cargo_solid_s", "canister_bay_s", "cargo_universal_70", "tractor_beam_s", "fuel_tank_s" }
            .Select(k => goodIds[k]).ToHashSet();
        var types = node["StationTypes"]!.AsArray();
        foreach (var t in types.ToList())
        {
            if ((string)t!["Key"]! == "trade_station") { types.Remove(t); continue; }
            t.AsObject().Remove("ShipDealer");
            if (t["Services"] is JsonArray sv)
                foreach (var x in sv.ToList()) if ((string)x! is "outfitter" or "shipdealer" or "upgrade") sv.Remove(x);   // upgrade kam erst mit Phase 7
            if ((string)t["Key"]! == "shipyard" && t["Stocks"] is JsonArray st)
                foreach (var x in st.ToList()) if (newYardStocks.Contains((int)x!["Good"]!)) st.Remove(x);
        }
        var stations = node["Stations"]!.AsArray();
        var removed = new HashSet<int>();
        foreach (var s in stations.ToList())
        {
            if ((string)s!["Type"]! == "trade_station") { removed.Add((int)s["Id"]!); stations.Remove(s); continue; }
            if ((string)s["Type"]! == "shipyard")
                foreach (var k in newYardStocks) s["Market"]!.AsObject().Remove(k.ToString());
        }
        foreach (var sec in node["Sectors"]!.AsArray())
        {
            var ids = sec!["Stations"]!.AsArray();
            foreach (var x in ids.ToList()) if (removed.Contains((int)x!)) ids.Remove(x);
        }
        var models = node["ShipModels"]!.AsArray();
        foreach (var m in models.ToList()) if ((string)m!["Class"]! == "skiff") models.Remove(m);
        var classes = node["ShipClasses"]!.AsArray();
        foreach (var c in classes.ToList()) if ((string)c!["Key"]! == "skiff") classes.Remove(c);
        node["Rules"]!["MaxStations"] = (int)node["Rules"]!["MaxStations"]! - removed.Count;
        return node.ToJsonString();
    }

    [Fact]
    public void Migration_V7ToV8_ErgaenztHandelsstationDiensteUndSkiffs_FlotteHandelt()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 300);
        var v7 = ToV7Json(SaveGameJson.Write(u, Now));
        Assert.Contains("\"Version\":7", v7);
        Assert.DoesNotContain("trade_station", v7);
        var (m, _) = SaveGameJson.Read(v7);

        var trade = m.Stations.Values.Single(s => s.TypeKey == "trade_station");
        var orig = u.Stations.Values.Single(s => s.TypeKey == "trade_station");
        Assert.Equal(orig.Id, trade.Id);
        Assert.Equal(orig.Position, trade.Position);
        Assert.True(trade.BankruptcyProtected);
        Assert.Equal(orig.Market.Keys.OrderBy(k => k.Value), trade.Market.Keys.OrderBy(k => k.Value));
        Assert.Equal(u.Rules.MaxStations, m.Rules.MaxStations);
        Assert.Equal(u.StationTypes["shipyard"].Services, m.StationTypes["shipyard"].Services);
        Assert.Equal(0.05f, m.StationTypes["shipyard"].ShipDealer!.Assembly);
        var yard = m.Stations.Values.First(s => s.TypeKey == "shipyard");
        foreach (var k in new[] { "cargo_universal_60", "tractor_beam_s", "fuel_tank_s" })
            Assert.True(yard.Market.ContainsKey(m.FindGood(k)!.Id), k);
        foreach (var k in new[] { "pip", "nomad", "prospector" }) Assert.True(m.ShipModels.ContainsKey(k), k);
        Assert.Contains(StationServices.Default().ForStation(m, yard), s => s.Key == "shipdealer");

        int trades = 0;
        m.Events.Raised += e => { if (e is TradeCompleted) trades++; };
        new SimulationRunner().Run(m, 3000);
        Assert.True(trades > 20, $"nur {trades} Handelsfahrten");
        Assert.True(m.Ships.Values.Count(s => s.Role == ShipRole.Trader) >= u.Ships.Values.Count(s => s.Role == ShipRole.Trader));
    }
}
