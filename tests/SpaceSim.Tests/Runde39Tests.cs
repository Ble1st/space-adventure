using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 39: „Rückgängig“ im Handel — <see cref="TradeJournal"/> (letzte Buchung, nicht gespeichert/gehasht),
/// <see cref="TradeActions.UndoIssue"/>/<see cref="TradeActions.Undo"/>, Angebot „undo“ im <see cref="TradeService"/>.
/// Kauf/Verkauf zurück stellen Kasse, Laderaum, Stationslager, Stationskasse und Ledger exakt wieder her; jeder
/// Sperrgrund einzeln; Exportcharge entfernt; Preisbewegung nie besser als der Markt jetzt (Missbrauch: kein
/// Mehrgewinn gegen „nichts tun“); zweites Schiff; Speichern/Laden löscht das Journal.
/// </summary>
public class Runde39Tests
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Welt nach einem Tick (Preise gesetzt); aktives Spielerschiff angedockt an einer Station mit handelbarer Ware (keine Baustelle, kein Export-Ankauf, genug Lager/Kasse/Laderaum).</summary>
    static (Universe u, Ship ship, Station st, GoodId good, TradeService svc) Setup()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        var ship = u.Ships[u.PlayerShip];
        ship.Cargo.Clear();
        u.WalletOf(ship).Credits = 1_000_000f;
        foreach (var st in u.Stations.Values.OrderBy(s => s.Id.Value))
        {
            if (st.IsBuildSite) continue;
            foreach (var (good, e) in st.Market.OrderBy(kv => kv.Key.Value))
            {
                if (e.Amount < 20f || e.Price <= 0f || ExportRules.Buys(u, st, good)) continue;
                if (TradeActions.MaxBuyable(u, ship, st, good) < 10f) continue;
                if (st.AcceptableUnits(good, u.Rules) < 10f || st.Credits < 10f * e.Price) continue;
                ship.DockedAt = st.Id;
                ship.Position = st.DockPoint(ship.Id);
                return (u, ship, st, good, new TradeService());
            }
        }
        throw new InvalidOperationException("keine handelbare Station/Ware gefunden");
    }

    // ───────── Zustands-Schnappschuss ─────────

    sealed class Snap
    {
        public float Wallet, StationCash;
        public int Exports;
        public Dictionary<GoodId, float> Cargo = new(), Stock = new();
        public List<string> Ledger = new();
    }

    static List<string> LedgerOf(Universe u) => u.Ledger
        .OrderBy(kv => kv.Key.Actor).ThenBy(kv => kv.Key.Station).ThenBy(kv => kv.Key.Good)
        .Select(kv =>
        {
            var p = kv.Value;
            return $"{kv.Key.Actor}/{kv.Key.Station}/{kv.Key.Good}: b{p.BoughtQty:0.####}/{p.BoughtPrice:0.####}/{p.BoughtStockBefore:0.####}"
                + $" s{p.SoldQty:0.####}/{p.SoldPrice:0.####}/{p.SoldStockBefore:0.####} o{p.BoughtOpen}/{p.SoldOpen} t{p.LastTick} m{p.MissingQty:0.####}/{p.MissingSeen:0.####}";
        }).ToList();

    static Snap SnapOf(Universe u, Ship ship, Station st)
    {
        var s = new Snap { Wallet = u.WalletOf(ship).Credits, StationCash = st.Credits, Exports = st.Exports.Count, Ledger = LedgerOf(u) };
        foreach (var kv in ship.Cargo) s.Cargo[kv.Key] = kv.Value;
        foreach (var kv in st.Market) s.Stock[kv.Key] = kv.Value.Amount;
        return s;
    }

    static void AssertSame(Snap s, Universe u, Ship ship, Station st, string what)
    {
        Assert.True(MathF.Abs(s.Wallet - u.WalletOf(ship).Credits) < 0.001f, $"{what}: Spielerkasse {s.Wallet:0.###} != {u.WalletOf(ship).Credits:0.###}");
        Assert.True(MathF.Abs(s.StationCash - st.Credits) < 0.001f, $"{what}: Stationskasse {s.StationCash:0.###} != {st.Credits:0.###}");
        Assert.Equal(s.Exports, st.Exports.Count);
        Assert.Equal(s.Cargo.Count, ship.Cargo.Count);
        foreach (var kv in s.Cargo)
            Assert.True(MathF.Abs(kv.Value - ship.Cargo.GetValueOrDefault(kv.Key)) < 0.001f, $"{what}: Fracht {u.GoodOf(kv.Key).Name} {kv.Value} != {ship.Cargo.GetValueOrDefault(kv.Key)}");
        foreach (var kv in s.Stock)
            Assert.True(MathF.Abs(kv.Value - (st.Market.TryGetValue(kv.Key, out var e) ? e.Amount : 0f)) < 0.001f, $"{what}: Lager {u.GoodOf(kv.Key).Name} {kv.Value} != {(st.Market.TryGetValue(kv.Key, out var e2) ? e2.Amount : 0f)}");
        Assert.Equal(string.Join("|", s.Ledger), string.Join("|", LedgerOf(u)));
    }

    static List<SimEvent> Capture(Universe u)
    {
        var ev = new List<SimEvent>();
        u.Events.Raised += e => ev.Add(e);
        return ev;
    }

    static ServiceOffer UndoOffer(TradeService svc, Universe u, Station st, Ship ship) => svc.Offers(u, st, ship).Single(o => o.Key == TradeService.Undo);

    /// <summary>Setzt Bestand und Preis nach der Marktformel (ohne Tick-Lauf), z. B. für eine simulierte NPC-Lieferung.</summary>
    static void Reprice(Universe u, Station st, GoodId good, float newAmount)
    {
        var e = st.Market[good];
        e.Amount = newAmount;
        e.Price = MarketSystem.StockPrice(u.GoodOf(good), e, newAmount);
    }

    // ───────── Exakte Rücknahme ─────────

    [Fact]
    public void Kauf_Rueckgaengig_stelltKasseLaderaumLagerStationskasseLedgerExaktWiederHer()
    {
        var (u, ship, st, good, svc) = Setup();
        var before = SnapOf(u, ship, st);
        var ev = Capture(u);

        var buy = svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        Assert.True(buy.Success && buy.Units == 10f);
        float paid = -buy.Credits;
        Assert.NotEqual(before.Wallet, u.WalletOf(ship).Credits);

        var offer = UndoOffer(svc, u, st, ship);
        Assert.Equal(1f, offer.Amount);
        Assert.Null(offer.Note);
        Assert.Contains("Kauf 10", offer.Text);

        var undo = svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.True(undo.Success);
        Assert.Equal(10f, undo.Units);
        Assert.True(MathF.Abs(undo.Credits - paid) < 0.001f, $"Erstattung = gezahlter Betrag ({undo.Credits} vs {paid})");

        AssertSame(before, u, ship, st, "nach Kauf→Rückgängig");
        var undone = Assert.Single(ev.OfType<TradeUndone>());
        Assert.Equal(ship.Id, undone.Ship);
        Assert.Equal(st.Id, undone.Station);
        Assert.True(MathF.Abs(undone.Amount - paid) < 0.001f);
        var line = Assert.Single(undone.Lines);
        Assert.Equal(good, line.Good);
        Assert.Equal(10f, line.Units);
        Assert.Contains(ev.OfType<ServicePerformed>(), s => s.Offer == TradeService.Undo && s.Service == TradeService.ServiceKey);
        Assert.Null(u.Journal.Last);   // Eintrag gelöscht: zweites Rückgängig schlägt fehl
        var again = svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.False(again.Success);
        Assert.Equal("Keine offene Buchung", again.Message);
        AssertSame(before, u, ship, st, "nach zweitem Rückgängig");
    }

    [Fact]
    public void Verkauf_Rueckgaengig_stelltKasseLaderaumLagerStationskasseLedgerExaktWiederHer()
    {
        var (u, ship, st, good, svc) = Setup();
        ship.Cargo[good] = 30f;
        var before = SnapOf(u, ship, st);
        var ev = Capture(u);

        var sell = svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Sell}:{good.Value}", good, 10f));
        Assert.True(sell.Success && sell.Units == 10f);
        float received = sell.Credits;

        var offer = UndoOffer(svc, u, st, ship);
        Assert.Equal(1f, offer.Amount);
        Assert.Contains("Verkauf 10", offer.Text);

        var undo = svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.True(undo.Success);
        Assert.True(MathF.Abs(undo.Credits + received) < 0.001f, $"Zahlung = erhaltener Betrag ({undo.Credits} vs {-received})");

        AssertSame(before, u, ship, st, "nach Verkauf→Rückgängig");
        var undone = Assert.Single(ev.OfType<TradeUndone>());
        Assert.True(MathF.Abs(undone.Amount + received) < 0.001f);
        Assert.False(svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f)).Success);
    }

    [Fact]
    public void VerkaufAll_Rueckgaengig_stelltAlleWarenWiederHer()
    {
        var (u, ship, st, good, svc) = Setup();
        st.Credits = 1_000_000f;
        // zweite Ware an Bord, die die Station annimmt
        var good2 = st.Market.Where(kv => kv.Key != good && kv.Value.Price > 0f && st.AcceptableUnits(kv.Key, u.Rules) >= 7f)
            .OrderBy(kv => kv.Key.Value).Select(kv => kv.Key).FirstOrDefault();
        Assert.NotEqual(default, good2);
        ship.Cargo[good] = 12f;
        ship.Cargo[good2] = 7f;
        var before = SnapOf(u, ship, st);

        var all = svc.Execute(u, st, ship, new ServiceRequest(TradeService.SellAll, default, 0f));
        Assert.True(all.Success);
        Assert.Equal(0f, ship.Cargo.Values.Sum(), 3);

        var undo = svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.True(undo.Success);
        Assert.Equal(19f, undo.Units, 3);
        AssertSame(before, u, ship, st, "nach Verkauf-Alles→Rückgängig");
    }

    [Fact]
    public void NeueBuchung_ersetztDenEintrag_nurDieLetzteWirdRueckgaengig()
    {
        var (u, ship, st, good, svc) = Setup();
        var before = SnapOf(u, ship, st);
        float price = st.Market[good].Price;
        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        var buy2 = svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 5f));
        Assert.True(buy2.Success && buy2.Units == 5f);

        var undo = svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.True(undo.Success);
        Assert.Equal(5f, undo.Units);   // nur die letzte Buchung (5), nicht beide
        Assert.Equal(10f, ship.Cargo.GetValueOrDefault(good), 3);   // Stand vor der zweiten Buchung
        Assert.True(MathF.Abs(u.WalletOf(ship).Credits - (before.Wallet - 10f * price)) < 0.01f, "nur der zweite Kauf wurde erstattet");
    }

    // ───────── Angebot ⇔ Ausführung ─────────

    [Fact]
    public void Angebot_undo_MengeUndGrund_entsprichtExecute_strengeSchluessel()
    {
        var (u, ship, st, good, svc) = Setup();
        st.Credits = 10_000_000f;   // Station überlebt das Fenster sicher
        var ev = Capture(u);

        // ohne Buchung: Menge 0, Grund als Notiz, Execute fails ohne Zustand/Event
        var none = UndoOffer(svc, u, st, ship);
        Assert.Equal(0f, none.Amount);
        Assert.Equal("Keine offene Buchung", none.Note);
        Assert.Equal("Rückgängig", none.Text);
        var before = SnapOf(u, ship, st);
        var r0 = svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.False(r0.Success);
        Assert.Equal("Keine offene Buchung", r0.Message);
        Assert.Empty(ev);
        AssertSame(before, u, ship, st, "fehlgeschlagenes undo");

        // nach Kauf: Menge 1, kein Grund — Execute gelingt genau dann
        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        var ok = UndoOffer(svc, u, st, ship);
        Assert.Equal(1f, ok.Amount);
        Assert.Null(ok.Note);
        Assert.True(ok.Price > 0f);
        Assert.True(svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f)).Success);

        // Sperrgrund erscheint in der Notiz und sperrt Execute: Fenster abgelaufen
        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        new SimulationRunner().Run(u, (int)(TradeJournal.WindowTicks + 1));
        var blocked = UndoOffer(svc, u, st, ship);
        Assert.Equal(0f, blocked.Amount);
        Assert.Equal($"Fenster {TradeJournal.WindowTicks} Ticks abgelaufen", blocked.Note);
        var r1 = svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.False(r1.Success);
        Assert.Equal(blocked.Note, r1.Message);

        // strenge Schlüssel: „undo:5“, „undo:x“ sind unbekannt
        foreach (var bad in new[] { "undo:5", "undo:x", "undo:", "UNDO" })
        {
            var rb = svc.Execute(u, st, ship, new ServiceRequest(bad, default, 0f));
            Assert.False(rb.Success);
            Assert.Contains("Unbekanntes Angebot", rb.Message);
        }
    }

    // ───────── Sperrgründe ─────────

    [Fact]
    public void Sperrgrund_NichtMehrAngedockt_und_AbdockenLoeschtDenEintrag()
    {
        var (u, ship, st, good, svc) = Setup();
        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        ship.DockedAt = default;   // abgedockt, Eintrag noch da → Grund
        Assert.Equal("Nicht mehr angedockt", TradeActions.UndoIssue(u, u.Journal, ship, st));
        Assert.False(svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f)).Success);

        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        ship.DockedAt = st.Id;
        PlayerFleet.Undock(u, ship);   // Abdocken löscht den Eintrag
        Assert.Null(u.Journal.Last);
        Assert.Equal("Keine offene Buchung", TradeActions.UndoIssue(u, u.Journal, ship, st));
        Assert.Equal(0f, UndoOffer(svc, u, st, ship).Amount);
    }

    [Fact]
    public void Sperrgrund_AnderesSchiff_und_SchiffswechselLoeschtDenEintrag()
    {
        var (u, ship, st, good, svc) = Setup();
        var s2 = WorldBuilder.NewShip(u, u.NewShipId(), ShipRole.Trader, u.PlayerActor, ship.Sector, 0f, 100f);
        u.AddShip(s2);
        s2.DockedAt = st.Id;

        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        Assert.Equal("Anderes Schiff", TradeActions.UndoIssue(u, u.Journal, s2, st));
        Assert.False(svc.Execute(u, st, s2, new ServiceRequest(TradeService.Undo, default, 0f)).Success);

        PlayerFleet.Activate(u, s2, st.Id);   // Schiffswechsel löscht den Eintrag
        Assert.Null(u.Journal.Last);
        Assert.Equal("Keine offene Buchung", TradeActions.UndoIssue(u, u.Journal, s2, st));
    }

    [Fact]
    public void Sperrgrund_FensterAbgelaufen()
    {
        var (u, ship, st, good, svc) = Setup();
        st.Credits = 10_000_000f;
        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        new SimulationRunner().Run(u, (int)TradeJournal.WindowTicks);   // genau 480: nicht das Fenster
        Assert.NotEqual($"Fenster {TradeJournal.WindowTicks} Ticks abgelaufen", TradeActions.UndoIssue(u, u.Journal, ship, st));
        new SimulationRunner().Run(u, 1);   // 481: abgelaufen (Fenster-Prüfung vor allen anderen)
        Assert.Equal($"Fenster {TradeJournal.WindowTicks} Ticks abgelaufen", TradeActions.UndoIssue(u, u.Journal, ship, st));
    }

    [Fact]
    public void Sperrgrund_WareNichtMehrAnBord()
    {
        var (u, ship, st, good, svc) = Setup();
        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        ship.Cargo[good] = 3f;   // nur noch 3 der 10 an Bord
        Assert.Equal("Ware nicht mehr an Bord", TradeActions.UndoIssue(u, u.Journal, ship, st));
        ship.Cargo.Remove(good);
        Assert.Equal("Ware nicht mehr an Bord", TradeActions.UndoIssue(u, u.Journal, ship, st));
    }

    [Fact]
    public void Sperrgrund_WareNichtMehrImLager()
    {
        var (u, ship, st, good, svc) = Setup();
        ship.Cargo[good] = 30f;
        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Sell}:{good.Value}", good, 10f));
        st.Market[good].Amount = 5f;   // Produktion hat die gelieferten Einheiten verbraucht
        Assert.Equal("Ware nicht mehr im Lager", TradeActions.UndoIssue(u, u.Journal, ship, st));
    }

    [Fact]
    public void Sperrgrund_KasseReichtNicht()
    {
        var (u, ship, st, good, svc) = Setup();
        ship.Cargo[good] = 30f;
        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Sell}:{good.Value}", good, 10f));
        u.WalletOf(ship).Credits = 0f;   // Rückkauf würde max(erhalten, Kosten) kosten
        Assert.Equal("Kasse reicht nicht", TradeActions.UndoIssue(u, u.Journal, ship, st));
    }

    [Fact]
    public void Sperrgrund_StationKannNichtZahlen()
    {
        var (u, ship, st, good, svc) = Setup();
        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        st.Credits = 0f;   // Erstattung wäre > 0
        Assert.Equal("Station kann nicht zahlen", TradeActions.UndoIssue(u, u.Journal, ship, st));
    }

    [Fact]
    public void Sperrgrund_Baustelle()
    {
        var (u, ship, _, _, svc) = Setup();
        foreach (var id in u.Stations.Values.Where(s => s.IsBuildSite).Select(s => s.Id).ToList()) u.RemoveStation(id, "test");
        u.Rules.MaxActiveSites = 1;
        var sec = u.Sectors[ship.Sector];
        var spot = StationLifecycleSystem.FindFreeSpot(u, sec)!.Value;
        var t = u.StationTypes.Values.First(x => x.BuildCost > 0f && ConstructionRules.Bill(u, x) is not null);
        var site = ConstructionRules.StartSite(u, t, sec.Id, spot, sec.Owner, t.BuildCost, null, null);
        u.Rules.MaxStations = Math.Max(u.Rules.MaxStations, u.Stations.Count);
        new SimulationRunner().Run(u, 1);   // Markt der Baustelle bepreisen
        ship.DockedAt = site.Id;
        ship.Position = site.DockPoint(ship.Id);
        var need = site.Site!.Required.Where(kv => kv.Value >= 10f).OrderBy(kv => kv.Key.Value).First().Key;
        Assert.True(site.Market.TryGetValue(need, out var me) && me.Price > 0f);
        ship.Cargo.Clear();
        ship.Cargo[need] = 20f;
        var sell = svc.Execute(u, site, ship, new ServiceRequest($"{TradeService.Sell}:{need.Value}", need, 10f));
        Assert.True(sell.Success);
        Assert.Equal("Baustelle", TradeActions.UndoIssue(u, u.Journal, ship, site));
        Assert.False(svc.Execute(u, site, ship, new ServiceRequest(TradeService.Undo, default, 0f)).Success);
    }

    [Fact]
    public void Sperrgrund_Gefecht()
    {
        var (u, ship, st, good, svc) = Setup();
        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        var raider = u.Ships.Values.First(s => s.Id != u.PlayerShip && s.Has(Capability.Combat));
        Engage.StartRaid(u, raider, ship, u.Sectors[ship.Sector]);
        Assert.True(u.Fights.IsEngaged(ship.Id));
        ship.DockedAt = st.Id;   // Gefecht löscht den Liegeplatz (Engage.Join) — hier wieder angedockt, um den Grund allein zu prüfen
        Assert.Equal("Gefecht", TradeActions.UndoIssue(u, u.Journal, ship, st));
        Assert.False(svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f)).Success);
    }

    [Fact]
    public void Sperrgrund_RueckkaufeintragHatSichVeraendert()
    {
        var (u, ship, st, good, svc) = Setup();
        // zweites Spielerschiff (Flottenschiff), das nicht über den Dienst handelt
        var s2 = WorldBuilder.NewShip(u, u.NewShipId(), ShipRole.Trader, u.PlayerActor, ship.Sector, 0f, 100f);
        u.AddShip(s2);

        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        Assert.Null(TradeActions.UndoIssue(u, u.Journal, ship, st));
        // das Flottenschiff handelt an derselben Station → Posten ändert sich (aktuell != PostAfter)
        PlayerLedger.BookBuy(u, s2, st, good, 5f, u.WalletOf(s2));
        Assert.Equal("Rückkaufeintrag hat sich geändert", TradeActions.UndoIssue(u, u.Journal, ship, st));
        var before = SnapOf(u, ship, st);
        Assert.False(svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f)).Success);
        AssertSame(before, u, ship, st, "gesperrtes undo");
    }

    // ───────── Exportcharge ─────────

    [Fact]
    public void Exportcharge_ausDemVerkauf_wirdBeiRueckgaengigEntfernt()
    {
        var u = DemoUniverse.Create();
        new SimulationRunner().Run(u, 1);
        var ship = u.Ships[u.PlayerShip];
        ship.Cargo.Clear();
        u.WalletOf(ship).Credits = 1_000_000f;
        var svc = new TradeService();
        var sink = u.Stations.Values.First(s => ExportRules.ConfigOf(u, s) is { } cfg && cfg.Goods.Count > 0 && s.Market.Values.Any(m => m.Price > 0f));
        var cfg = ExportRules.ConfigOf(u, sink)!;
        var good = cfg.Goods.First(g => sink.Market.TryGetValue(g, out var e) && e.Price > 0f && sink.AcceptableUnits(g, u.Rules) >= 10f);
        ship.DockedAt = sink.Id;
        ship.Position = sink.DockPoint(ship.Id);
        ship.Cargo[good] = 25f;
        var before = SnapOf(u, ship, sink);

        var sell = svc.Execute(u, sink, ship, new ServiceRequest($"{TradeService.Sell}:{good.Value}", good, 10f));
        Assert.True(sell.Success);
        Assert.Single(sink.Exports);   // eine Charge entstanden

        var undo = svc.Execute(u, sink, ship, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.True(undo.Success);
        Assert.Empty(sink.Exports);    // Charge entfernt (noch nicht fällig: 1200 > 480 Ticks Fenster)
        AssertSame(before, u, ship, sink, "nach Export-Verkauf→Rückgängig");
    }

    // ───────── Preisbewegung / Missbrauch ─────────

    [Fact]
    public void Preisbewegung_Kauf_NpcLieferungSenktPreis_Rueckgaengig_ErstattungHoechtensHeutigerWert()
    {
        var (u, ship, st, good, svc) = Setup();
        float walletBefore = u.WalletOf(ship).Credits;
        Reprice(u, st, good, 10f);   // knapper Bestand → hoher Preis
        var buy = svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        Assert.True(buy.Success && buy.Units == 10f);
        float paid = -buy.Credits;
        float pricePaid = st.Market[good].Price;

        // NPC-Lieferung senkt den Preis der Station
        Reprice(u, st, good, st.Market[good].Amount + 400f);
        float priceNow = st.Market[good].Price;
        Assert.True(priceNow < pricePaid, $"Preis gefallen expected: {priceNow} < {pricePaid}");
        float saleValueNow = PlayerLedger.SaleValue(u, ship, st, good, 10f, priceNow).Revenue;

        var undo = svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.True(undo.Success);
        Assert.True(undo.Credits <= paid + 0.001f, $"Erstattung nie über gezahltem Betrag: {undo.Credits} <= {paid}");
        Assert.True(undo.Credits <= saleValueNow + 0.001f, $"Erstattung nie über heutigem Verkaufswert: {undo.Credits} <= {saleValueNow}");
        Assert.True(MathF.Abs(undo.Credits - MathF.Min(paid, saleValueNow)) < 0.001f, "Erstattung = min(gezahlt, heutiger Wert mit Rückkaufdeckel)");
        // gegen „nichts tun“: kein Mehrgewinn, hier sogar Verlust (Preis gefallen)
        Assert.True(u.WalletOf(ship).Credits <= walletBefore + 0.001f,
            $"Kauf→Rückgängig nach Preissturz bringt nichts: {u.WalletOf(ship).Credits} <= {walletBefore}");
    }

    [Fact]
    public void Missbrauch_Verkauf_PreisSteigt_Rueckgaengig_DannTeurerVerkaufen_keinMehrgewinnGegenNichtsTun()
    {
        // Zwilling A: verkaufen → Preis steigt (Bestand sinkt) → Rückgängig → teurer verkaufen
        var (a, shipA, stA, goodA, svcA) = Setup();
        shipA.Cargo[goodA] = 30f;
        float startA = a.WalletOf(shipA).Credits;
        var sellA = svcA.Execute(a, stA, shipA, new ServiceRequest($"{TradeService.Sell}:{goodA.Value}", goodA, 10f));
        float receivedA = sellA.Credits;
        // Preis steigt (z. B. knapper Bestand der Station) — Bestand selbst bleibt, damit die Ware noch im Lager ist
        stA.Market[goodA].Price = MarketSystem.StockPrice(a.GoodOf(goodA), stA.Market[goodA], 1f);
        float priceRose = stA.Market[goodA].Price;
        var undoA = svcA.Execute(a, stA, shipA, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.True(undoA.Success);
        Assert.True(-undoA.Credits >= receivedA - 0.001f, "Rückkauf nie billiger als erhalten (Rückkaufboden)");
        var resellA = svcA.Execute(a, stA, shipA, new ServiceRequest($"{TradeService.Sell}:{goodA.Value}", goodA, 10f));
        Assert.True(resellA.Success);
        float finalA = a.WalletOf(shipA).Credits;

        // Zwilling B (nichts tun): dasselbe verkaufen, dann Preis steigt, dann nichts mehr
        var (b, shipB, stB, goodB, svcB) = Setup();
        shipB.Cargo[goodB] = 30f;
        Assert.Equal(goodA, goodB);
        var sellB = svcB.Execute(b, stB, shipB, new ServiceRequest($"{TradeService.Sell}:{goodB.Value}", goodB, 10f));
        Assert.True(MathF.Abs(sellB.Credits - receivedA) < 0.001f, "Zwillinge verkaufen zum selben Preis");
        stB.Market[goodB].Price = MarketSystem.StockPrice(b.GoodOf(goodB), stB.Market[goodB], 1f);
        float finalB = b.WalletOf(shipB).Credits;

        Assert.True(priceRose > 0f);
        Assert.True(finalA <= finalB + 0.001f,
            $"Verkauf→Preis steigt→Rückgängig→teurer verkaufen bringt KEINEN Mehrgewinn: A {finalA:0.##} <= B {finalB:0.##} (start {startA:0.##}, erhalten {receivedA:0.##}, Rückkauf {-undoA.Credits:0.##}, Wiederverkauf {resellA.Credits:0.##})");
    }

    [Fact]
    public void Missbrauch_Kauf_Rueckgaengig_keinGewinnGegenNichtsTun()
    {
        // Zwilling A: kaufen → Preis fällt → Rückgängig (Erstattung = heutiger Wert < gezahlt)
        var (a, shipA, stA, goodA, svcA) = Setup();
        Reprice(a, stA, goodA, 10f);   // knapper Bestand → hoher Preis
        float startA = a.WalletOf(shipA).Credits;
        svcA.Execute(a, stA, shipA, new ServiceRequest($"{TradeService.Buy}:{goodA.Value}", goodA, 10f));
        Reprice(a, stA, goodA, stA.Market[goodA].Amount + 400f);   // Lieferung → Preis gefallen
        var undoA = svcA.Execute(a, stA, shipA, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.True(undoA.Success);
        float finalA = a.WalletOf(shipA).Credits;

        // Zwilling B: nichts tun
        var (b, shipB, stB, goodB, _) = Setup();
        Assert.Equal(goodA, goodB);
        float finalB = b.WalletOf(shipB).Credits;

        Assert.True(finalA <= finalB + 0.001f, $"Kauf→Rückgängig nach Preissturz: A {finalA:0.##} <= B {finalB:0.##}");
        Assert.True(finalA < finalB - 0.001f, $"hier sogar Verlust (Erstattung zum gesunkenen Preis): A {finalA:0.##} < B {finalB:0.##}, Erstattung {undoA.Credits:0.##}");
    }

    // ───────── Persistenz ─────────

    [Fact]
    public void SpeichernLaden_loeschtDasJournal()
    {
        var (u, ship, st, good, svc) = Setup();
        svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        Assert.NotNull(u.Journal.Last);

        var (loaded, _) = SaveGameJson.Read(SaveGameJson.Write(u, Now));
        Assert.Null(loaded.Journal.Last);   // nicht gespeichert
        var shipL = loaded.Ships[loaded.PlayerShip];
        var stL = loaded.Stations[st.Id];
        Assert.Equal("Keine offene Buchung", TradeActions.UndoIssue(loaded, loaded.Journal, shipL, stL));
        Assert.False(new TradeService().Execute(loaded, stL, shipL, new ServiceRequest(TradeService.Undo, default, 0f)).Success);

        // neues Spiel: leeres Journal
        Assert.Null(DemoUniverse.Create().Journal.Last);
    }
}
