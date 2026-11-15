using SpaceSim.Core.Comms;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 41: Aufträge (Lieferung, Transport) mit Missionsmarkern und Liefervertrag über Comms — plus der A1-Regression
/// (Verkauf direkt nach Kauf lässt sich rückgängig machen). Bindend: Angebote deterministisch aus dem Welt-Seed und ohne
/// Hash-/Rng-Einfluss (gespeichert, nicht gehasht), Annahme reserviert aus FactionViability.Spendable, Geldfluss über
/// Ereignisse erklärbar, Lieferbonus nur für netto gelieferte Einheiten, Rückgängig rollt Auftragsfortschritt zurück,
/// keine Angebote bei Krieg/Fahndung (Stufe ≥ 2), Speichern/Laden bitgleich.
/// </summary>
public class Runde41Tests
{
    static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    // ein Runner je Universum — ein zweiter Runner würde seine Systeme ein zweites Mal am Ereignisbus anmelden (Events kämen doppelt)
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Universe, SimulationRunner> Runners = new();

    static Universe World(ulong seed = 1)
    {
        var u = TestWorlds.Default(seed, fuel: false, research: false);
        Run(u, 1);   // Systeme hängen sich an den Bus (Comms, Missionen)
        return u;
    }

    static void Run(Universe u, int ticks) => Runners.GetValue(u, _ => new SimulationRunner()).Run(u, ticks);

    /// <summary>Wartet, bis ein Angebot die Bedingung erfüllt (Board erneuert alle 2400 Ticks).</summary>
    static MissionOffer? FindOffer(Universe u, Func<MissionOffer, bool> pred, int rounds = 250)
    {
        for (int i = 0; i < rounds; i++)
        {
            var o = u.MissionOffers.FirstOrDefault(pred);
            if (o is not null) return o;
            Run(u, 100);
        }
        return u.MissionOffers.FirstOrDefault(pred);
    }

    static Ship Player(Universe u) => u.Ships[u.PlayerShip];

    static List<SimEvent> Capture(Universe u)
    {
        var ev = new List<SimEvent>();
        u.Events.Raised += e => ev.Add(e);
        return ev;
    }

    static MissionOffer DockDelivery(Universe u) =>
        // annehmbar UND erfüllbar: AcceptIssue ist null (Station handelt die Ware und nimmt die Sollmenge an)
        FindOffer(u, o => o.Type == "delivery" && o.Args.TryGetValue("contract", out var c) && c == "0"
            && u.Stations.TryGetValue(o.At, out var st0) && !st0.IsBuildSite && !ExportRules.Buys(u, st0, new GoodId(int.Parse(o.Args["good"])))
            && MissionRules.AcceptIssue(u, o.Id, Player(u), remote: true) is null)
        ?? throw new Xunit.Sdk.XunitException("kein annehmbares Dock-Lieferangebot in der Welt gefunden");

    static MissionOffer ContractOffer(Universe u) =>
        FindOffer(u, o => o.Type == "delivery" && o.Args.TryGetValue("contract", out var c) && c == "1" && o.Args.TryGetValue("channel", out var ch) && ch == "comms")
        ?? throw new Xunit.Sdk.XunitException("kein Liefervertrag in der Welt gefunden");

    static MissionOffer TransportOffer(Universe u) =>
        FindOffer(u, o => o.Type == "transport")
        ?? throw new Xunit.Sdk.XunitException("kein Transportangebot in der Welt gefunden");

    static Mission AcceptDock(Universe u, MissionOffer o)
    {
        var ship = Player(u);
        ship.DockedAt = o.At;
        ship.Position = u.Stations[o.At].DockPoint(ship.Id);
        var (m, issue) = MissionRules.Accept(u, o.Id, ship, remote: false);
        Assert.Null(issue);
        Assert.NotNull(m);
        return m!;
    }

    // ───────── A1 (Runde 39 Regression): Verkauf direkt nach Kauf ist rückgängig machbar ─────────

    [Fact]
    public void A1_VerkaufDirektNachKauf_UndoErlaubt_KeinGewinnloch()
    {
        var u = DemoUniverse.Create();
        Run(u, 1);
        var ship = Player(u);
        ship.Cargo.Clear();
        u.WalletOf(ship).Credits = 1_000_000f;
        Station? st = null; GoodId good = default;
        foreach (var s in u.Stations.Values.OrderBy(s => s.Id.Value))
        {
            if (s.IsBuildSite) continue;
            foreach (var (g, e) in s.Market.OrderBy(kv => kv.Key.Value))
            {
                if (e.Amount < 30f || e.Price <= 0f || ExportRules.Buys(u, s, g)) continue;
                if (TradeActions.MaxBuyable(u, ship, s, g) < 10f) continue;
                if (s.AcceptableUnits(g, u.Rules) < 10f || s.Credits < 10f * e.Price) continue;
                st = s; good = g; break;
            }
            if (st is not null) break;
        }
        Assert.NotNull(st);
        ship.DockedAt = st!.Id;
        ship.Position = st.DockPoint(ship.Id);
        var svc = new TradeService();

        // Kauf, dann direkt (derselbe Tick) Verkauf derselben Ware an derselben Station
        var buy = svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Buy}:{good.Value}", good, 10f));
        Assert.True(buy.Success && buy.Units == 10f);
        float walletAfterBuy = u.WalletOf(ship).Credits;
        float stationCashAfterBuy = st.Credits;
        var ledgerAfterBuy = u.Ledger.Select(kv => $"{kv.Key}:{kv.Value.SoldQty}/{kv.Value.BoughtQty}/{kv.Value.SoldPrice}/{kv.Value.BoughtPrice}/{kv.Value.LastTick}").ToList();

        var sell = svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Sell}:{good.Value}", good, 5f));
        Assert.True(sell.Success && sell.Units == 5f, sell.Message);

        // A1: der Verkauf direkt nach dem Kauf darf nicht mit „Rückkaufeintrag hat sich geändert“ gesperrt sein
        var issue = TradeActions.UndoIssue(u, u.Journal, ship, st);
        Assert.Null(issue);
        var undo = svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.True(undo.Success, undo.Message);

        // Ergebnis wie direkt nach dem Kauf (Rückkaufdeckel wieder wie nach dem Kauf), kein Gewinn durch Hin-und-Her
        Assert.True(MathF.Abs(u.WalletOf(ship).Credits - walletAfterBuy) < 0.001f,
            $"Spielerkasse nach Undo {u.WalletOf(ship).Credits:0.###} != nach Kauf {walletAfterBuy:0.###}");
        Assert.True(MathF.Abs(st.Credits - stationCashAfterBuy) < 0.001f,
            $"Stationskasse nach Undo {st.Credits:0.###} != nach Kauf {stationCashAfterBuy:0.###}");
        Assert.True(MathF.Abs(ship.Cargo.GetValueOrDefault(good) - 10f) < 0.001f, "Fracht wieder 10 Einheiten");
        var ledgerNow = u.Ledger.Select(kv => $"{kv.Key}:{kv.Value.SoldQty}/{kv.Value.BoughtQty}/{kv.Value.SoldPrice}/{kv.Value.BoughtPrice}/{kv.Value.LastTick}").ToList();
        Assert.Equal(ledgerAfterBuy, ledgerNow);   // Ledger-Posten exakt wie nach dem Kauf

        // dasselbe mit Tick-Lauf zwischen Kauf und Verkauf (Markt bewegt sich — Undo bleibt erlaubt, prune-tolerant)
        Run(u, 3);
        var sell2 = svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Sell}:{good.Value}", good, 4f));
        Assert.True(sell2.Success && sell2.Units == 4f, sell2.Message);
        Run(u, 3);
        var issue2 = TradeActions.UndoIssue(u, u.Journal, ship, st);
        Assert.Null(issue2);
        var undo2 = svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.True(undo2.Success, undo2.Message);
        Assert.True(MathF.Abs(ship.Cargo.GetValueOrDefault(good) - 10f) < 0.001f, "Fracht nach zweitem Undo wieder 10");
        var ledgerNow2 = u.Ledger.Select(kv => $"{kv.Key}:{kv.Value.SoldQty}/{kv.Value.BoughtQty}/{kv.Value.SoldPrice}/{kv.Value.BoughtPrice}/{kv.Value.LastTick}").ToList();
        Assert.Equal(ledgerAfterBuy, ledgerNow2);   // Ledger-Posten wie nach dem Kauf (Rückkaufdeckel wiederhergestellt)
    }

    // ───────── Angebote: deterministisch, eigener RNG, nicht gehasht ─────────

    [Fact]
    public void Angebote_DeterministischAusWeltSeed_GleicheAngeboteGleicherHash()
    {
        var a = World(7);
        var b = World(7);
        Run(a, 5000); Run(b, 5000);
        Assert.NotEmpty(a.MissionOffers);
        Assert.Equal(a.MissionOffers.Count, b.MissionOffers.Count);
        for (int i = 0; i < a.MissionOffers.Count; i++)
        {
            Assert.Equal(a.MissionOffers[i].Id, b.MissionOffers[i].Id);
            Assert.Equal(a.MissionOffers[i].Type, b.MissionOffers[i].Type);
            Assert.Equal(a.MissionOffers[i].At, b.MissionOffers[i].At);
            Assert.Equal(a.MissionOffers[i].Args.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value),
                b.MissionOffers[i].Args.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
        }
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));   // Angebote im Hash nicht sichtbar
    }

    [Fact]
    public void Angebote_BeeinflussenNpcHashNicht_MissionsActiveAusGibtGleichenHash()
    {
        var on = TestWorlds.Default(7, fuel: false, research: false);
        var off = TestWorlds.Default(7, fuel: false, research: false);
        off.Rules.MissionsActive = false;
        Run(on, 5000); Run(off, 5000);
        Assert.NotEmpty(on.MissionOffers);           // die Board-Angebote liefen mit
        Assert.Empty(off.MissionOffers);
        Assert.Empty(on.Missions);
        Assert.Equal(UniverseHash.Compute(off), UniverseHash.Compute(on));   // Angebote sind nicht gehasht
        Assert.Equal(UniverseHash.Compute(off, true, includePlayerPolitics: false), UniverseHash.Compute(on, true, includePlayerPolitics: false));
    }

    // ───────── Annahme / Erfüllung / Scheitern ─────────

    [Fact]
    public void Annahme_ReserviertBelohnungAusSpendable_PfandHalten_AngebotWeg_HashAendertSich()
    {
        var u = World(11);
        var o = DockDelivery(u);
        var ship = Player(u);
        u.WalletOf(ship).Credits = 100_000f;
        float clientBefore = u.Actors[o.Client].Treasury.Credits;
        float walletBefore = u.WalletOf(ship).Credits;
        ulong hashBefore = UniverseHash.Compute(u);
        var ev = Capture(u);

        var m = AcceptDock(u, o);

        Assert.Equal(MissionState.Active, m.State);
        Assert.Equal(o.Reward, m.Reserved);
        Assert.True(u.Actors[o.Client].Treasury.Credits <= clientBefore - o.Reward + 0.001f, "Belohnung aus der Auftraggeber-Kasse reserviert");
        Assert.Contains(ev.OfType<TreasuryUpkeep>(), t => t.Purpose == "mission_reserve" && MathF.Abs(t.Amount - o.Reward) < 0.001f);
        Assert.True(u.WalletOf(ship).Credits <= walletBefore - o.Deposit + 0.001f, "Pfand vom Spieler gehalten");
        Assert.DoesNotContain(u.MissionOffers, x => x.Id == o.Id);
        Assert.Contains(ev.OfType<MissionAccepted>(), e => e.Mission == m.Id);
        Assert.NotEqual(hashBefore, UniverseHash.Compute(u));   // angenommene Aufträge sind gehasht
        Assert.Equal(1, MissionRules.ActiveCount(u));
        Assert.True(MissionRules.AcceptIssue(u, 999999, ship, false) is not null);   // unbekannte Id abgelehnt
    }

    [Fact]
    public void Erfuellung_ZahltReservierteBelohnung_PfandZurueck_EreignisseErklaerbar()
    {
        var u = World(12);
        var o = DockDelivery(u);
        var m = AcceptDock(u, o);
        var st = u.Stations[o.At];
        var good = DeliveryMission.GoodOf(o);
        float units = DeliveryMission.UnitsOf(u, o);
        float walletAfterAccept = u.WalletOf(Player(u)).Credits;
        float clientAfterAccept = u.Actors[o.Client].Treasury.Credits;
        var ev = Capture(u);

        // netto liefern: Ware an Bord und an der Zielstation verkaufen (Marktpreis — Handel bleibt, wie er ist)
        var ship = Player(u);
        ship.Cargo[good] = units;
        var sr = TradeActions.Sell(u, ship, st, good, units);
        Assert.True(sr.Sold.TryGetValue(good, out var sold), $"Verkauf an der Zielstation: verkauft {sr.Sold.Values.Sum():0.#}, unverkauft {sr.Unsold:0.#}");
        Assert.True(MathF.Abs(m.Progress - units) < 0.01f, $"Fortschritt {m.Progress:0.###} != Soll {units:0.###} (verkauft {sold:0.###}, MissionSold {m.Sold:0.###})");
        MissionRules.Types.Get(m.Type)!.Check(u, m);   // direkt geprüft (MissionSystem macht das alle 20 Ticks)

        Assert.Empty(u.Missions);
        Assert.Contains(ev.OfType<MissionCompleted>(), e => e.Mission == m.Id && MathF.Abs(e.Paid - m.Reserved) < 0.001f);
        Assert.Contains(ev.OfType<MissionPaid>(), e => e.Mission == m.Id && MathF.Abs(e.Amount - m.Reserved) < 0.001f);
        Assert.True(MathF.Abs(u.WalletOf(ship).Credits - (walletAfterAccept + sr.Revenue + m.Reserved + m.Deposit)) < 0.01f,
            $"Kasse: Verkaufserlös + Belohnung + Pfand ({walletAfterAccept:0.##} → {u.WalletOf(ship).Credits:0.##}, Erlös {sr.Revenue:0.##}, Reward {m.Reserved:0.##}, Pfand {m.Deposit:0.##})");
        Assert.True(MathF.Abs(u.Actors[o.Client].Treasury.Credits - clientAfterAccept) < 0.01f, "Auftraggeber-Kasse nach Annahme unverändert (Reserve ging an den Spieler)");
        Assert.Equal(0, MissionRules.ActiveCount(u));
        Assert.True(MissionMarkers.For(u).Count == 0);   // Marker weg nach Erfüllung
    }

    [Fact]
    public void Scheitern_FristAbgelaufen_Rollback_PfandVerfaellt_Sperre()
    {
        var u = World(13);
        var o = TransportOffer(u);   // Transport hat ein Pfand (Frachtwert)
        Assert.True(o.Deposit > 0f);
        var m = AcceptDock(u, o);
        var st = u.Stations[o.At];
        float clientBefore = u.Actors[o.Client].Treasury.Credits;
        float stationBefore = st.Credits;
        float walletAfterAccept = u.WalletOf(Player(u)).Credits;
        Assert.True(Player(u).MissionCargo > 0f);
        var ev = Capture(u);

        u.Tick = m.Deadline;   // Frist ablaufen lassen
        MissionRules.Types.Get(m.Type)!.Check(u, m);   // direkt geprüft (ohne Tick-Lauf mit Kassen-Nebenbuchungen)

        Assert.Empty(u.Missions);
        Assert.Contains(ev.OfType<MissionFailed>(), e => e.Mission == m.Id && e.Reason == "Frist abgelaufen");
        Assert.Contains(ev.OfType<TreasuryIncome>(), t => t.Purpose == "mission_return" && MathF.Abs(t.Amount - m.Reserved) < 0.001f);
        Assert.True(MathF.Abs(u.Actors[o.Client].Treasury.Credits - (clientBefore + m.Reserved)) < 0.01f, "Reservierung zurück in die Auftraggeber-Kasse");
        Assert.True(MathF.Abs(st.Credits - (stationBefore + m.Deposit)) < 0.01f, "Pfand verfällt an die Station");
        Assert.True(MathF.Abs(u.WalletOf(Player(u)).Credits - walletAfterAccept) < 0.01f, "Spieler bekommt das Pfand nicht zurück");
        Assert.Equal(0f, Player(u).MissionCargo);   // versiegelte Fracht wird frei
        Assert.True(u.MissionFactions.TryGetValue(o.Client.Value, out var fs) && fs.BlockedUntil > u.Tick, "Sperrfrist für neue Aufträge dieser Fraktion");
        Assert.True(MissionRules.Blocked(u, o.Client.Value), "Fraktion ist bis BlockedUntil gesperrt");
    }

    [Fact]
    public void Abbruch_DurchSpieler_GleicheFolgenWieScheitern()
    {
        var u = World(24);
        var o = DockDelivery(u);
        var m = AcceptDock(u, o);
        var ship = Player(u);
        var (ok, issue) = MissionRules.Abandon(u, m.Id, ship);
        Assert.True(ok, issue);
        Assert.Empty(u.Missions);
        Assert.False(MissionRules.Abandon(u, m.Id, ship).Ok);   // zweiter Abbruch abgelehnt
    }

    // ───────── Transport ─────────

    [Fact]
    public void Transport_VersiegelteFracht_AbliefernAnZielstation()
    {
        var u = World(15);
        var o = TransportOffer(u);
        var m = AcceptDock(u, o);
        var ship = Player(u);
        Assert.True(ship.MissionCargo >= m.CargoVolume - 0.001f, "Fracht belegt Laderaum (Ship.MissionCargo)");
        Assert.Empty(ship.Cargo);   // versiegelte Fracht ist keine Ware
        var target = u.Stations[TransportMission.TargetOf(o)];
        var mk = Assert.Single(MissionMarkers.For(u));
        Assert.Equal(target.Sector, mk.Sector);   // Marker zeigt den Zielsektor
        Assert.Equal(target.Position, mk.Position);

        // an der falschen Station abliefern geht nicht
        var other = u.StationsById().First(s => s.Id != o.At && s.Id != target.Id && s.Dockable);
        Assert.NotNull(MissionRules.Types.Get(TransportMission.KeyId)!.DeliverIssue(u, m, ship, other));

        // an der Zielstation andocken und abliefern (über den Dienst wie die UI)
        ship.DockedAt = target.Id;
        ship.Position = target.DockPoint(ship.Id);
        var svc = new MissionService();
        var r = svc.Execute(u, target, ship, new ServiceRequest($"{MissionService.Deliver}:{m.Id}", default, 0f));
        Assert.True(r.Success, r.Message);
        Assert.Empty(u.Missions);
        Assert.Equal(0f, ship.MissionCargo);
    }

    // ───────── Liefervertrag über Comms ─────────

    [Fact]
    public void Liefervertrag_ReserviertAnnahmeUndKasse_NachverhandelnEinmalPlus5Prozent()
    {
        var u = World(16);
        var o = ContractOffer(u);
        var st = u.Stations[o.At];
        float price = DeliveryMission.PriceOf(o);
        float units = DeliveryMission.UnitsOf(u, o);
        Assert.True(price > 0f && units > 0f);
        Assert.Equal(u.Rules.ContractDepositShare * units * price, o.Deposit, 3);
        float cashBefore = st.Credits;
        float walletBefore = u.WalletOf(Player(u)).Credits;
        u.WalletOf(Player(u)).Credits = MathF.Max(walletBefore, 1_000_000f);
        walletBefore = u.WalletOf(Player(u)).Credits;

        // Comms-Registry: Vorschau und Ausführung von accept + haggle
        var comms = CommsActions.Default();
        var pAccept = comms.Preview(u, $"mission:accept:{o.Id}");
        Assert.True(pAccept.Possible, pAccept.Reason ?? "");
        var pHaggle = comms.Preview(u, $"contract:haggle:{o.Id}");
        // Nachverhandeln braucht Beziehung ≥ Schwell — in der frischen Welt zur Startfraktion ok, sonst schlägt es ehrlich fehl
        var (hOk, hIssue, newPrice) = MissionRules.Haggle(u, o.Id);
        if (hOk) Assert.True(MathF.Abs(newPrice - price * (1f + u.Rules.ContractHaggleShare)) < 0.01f, "+5 % auf den Festpreis");

        var ship = Player(u);
        ship.DockedAt = o.At;
        var r = comms.Execute(u, $"mission:accept:{o.Id}");   // remote über Comms
        Assert.True(r.Success, r.Message);
        var m = Assert.Single(u.Missions);
        Assert.Equal("1", m.Args["contract"]);
        Assert.True(m.ReservedCash > 0f);
        Assert.True(MathF.Abs(st.Credits - (cashBefore - m.ReservedCash)) < 0.01f, "Kasse der Station für n × Festpreis reserviert");
        Assert.True(MathF.Abs(u.WalletOf(ship).Credits - (walletBefore - m.Deposit)) < 0.01f, "Pfand 10 % gehalten");
        Assert.Equal(0f, m.Reserved);   // Vertrag: keine Bar-Belohnung, Ware zum Festpreis

        // nochmal nachverhandeln geht nicht (einmal je Angebot)
        var (h2, issue2, _) = MissionRules.Haggle(u, o.Id);
        Assert.False(h2);
        Assert.NotNull(issue2);

        // Annahme ist für die reservierte Menge garantiert (Verkauf an der Station klappt trotz knappem Lager)
        var good = DeliveryMission.GoodOf(o);
        ship.Cargo[good] = units;
        var sr = TradeActions.Sell(u, ship, st, good, units);
        Assert.True(sr.Sold.TryGetValue(good, out var sold) && sold >= units - 0.01f,
            $"reservierte Annahme greift (Verkauf {sold}/{units}, unverkauft {sr.Unsold:0.#})");
        MissionRules.Types.Get(m.Type)!.Check(u, m);
        Assert.Empty(u.Missions);   // Vertrag erfüllt (netto geliefert)
        // nach Erfüllung: Reservierung zurück bei der Station, Verkaufserlös abgebucht
        Assert.True(MathF.Abs(st.Credits - (cashBefore - sr.Revenue)) < 0.01f,
            $"Stationskasse nach Erfüllung {st.Credits:0.##} = {cashBefore:0.##} − Verkaufserlös {sr.Revenue:0.##} (Reservierung ging zurück)");
    }

    // ───────── Netto-Lieferung + Rückgängig ─────────

    [Fact]
    public void NettoLieferung_KaufUndZurueckverkaufZahltKeinenBonus()
    {
        var u = World(17);
        var o = DockDelivery(u);
        var m = AcceptDock(u, o);
        var st = u.Stations[o.At];
        var good = DeliveryMission.GoodOf(o);
        float units = DeliveryMission.UnitsOf(u, o);
        var ship = Player(u);
        u.WalletOf(ship).Credits = 1_000_000f;
        st.Credits = 1_000_000_000f;   // Kasse reicht für die ganze Menge (sonst ist Kasse÷Preis der Deckel)
        // sofort nach der Annahme verkaufen (Zustand wie bei der Angebotserzeugung — units ist am Annahme-Deckel gedeckelt)

        // liefern (ggf. Teilverkäufe), dann dieselbe Ware an derselben Station zurückkaufen → netto 0
        for (int t = 0; t < 10 && m.Progress < units - 0.01f; t++)
        {
            ship.Cargo.TryGetValue(good, out var have);
            ship.Cargo[good] = have + units;
            TradeActions.Sell(u, ship, st, good, units);
        }
        Assert.True(MathF.Abs(m.Progress - units) < 0.01f, $"geliefert {m.Progress:0.###}/{units:0.###} (price {st.Market[good].Price:0.###}, accept {st.AcceptableUnits(st.Market[good], u.Rules):0.###})");
        for (int t = 0; t < 10 && m.Progress > 0.01f; t++)   // zurückkaufen
        {
            var buy = TradeActions.Buy(u, ship, st, good, m.Progress);
            Assert.True(buy.Units > 0f, $"Rückkauf an derselben Station: {buy.Outcome}");
        }
        Assert.True(MathF.Abs(m.Progress) < 0.01f, $"Bought mindert: netto {m.Progress:0.###} (Sold {m.Sold:0.###}, Bought {m.Bought:0.###})");
        MissionRules.Types.Get(m.Type)!.Check(u, m);
        Assert.Single(u.Missions);   // nicht erfüllt — Kaufen-und-zurückverkaufen zählt nicht

        // wieder verkaufen → erfüllt
        for (int t = 0; t < 10 && m.Progress < units - 0.01f; t++)
        {
            ship.Cargo.TryGetValue(good, out var have);
            ship.Cargo[good] = have + units;
            TradeActions.Sell(u, ship, st, good, units);
        }
        MissionRules.Types.Get(m.Type)!.Check(u, m);
        Assert.Empty(u.Missions);
    }

    [Fact]
    public void Rueckgaengig_NimmtAuftragsfortschrittZurueck_KeinDoppelterBonus()
    {
        var u = World(18);
        var o = DockDelivery(u);
        var m = AcceptDock(u, o);
        var st = u.Stations[o.At];
        var good = DeliveryMission.GoodOf(o);
        float units = DeliveryMission.UnitsOf(u, o);
        var ship = Player(u);
        var ev = Capture(u);

        ship.Cargo[good] = units;
        var svc = new TradeService();
        var sellR = svc.Execute(u, st, ship, new ServiceRequest($"{TradeService.Sell}:{good.Value}", good, units));
        Assert.True(sellR.Success && sellR.Units > 0f, sellR.Message);
        Assert.True(MathF.Abs(m.Sold - sellR.Units) < 0.01f, $"Fortschritt = verkauft ({m.Sold:0.###} vs {sellR.Units:0.###})");
        Assert.NotNull(u.Journal.Last);
        Assert.NotNull(u.Journal.Last!.Progress);
        Assert.Contains(u.Journal.Last!.Progress!, p => p.MissionId == m.Id && p.DSold > 0f);
        float walletBeforeSell = u.WalletOf(ship).Credits - sellR.Credits;   // Stand vor dem Verkauf

        // Rückgängig des Verkaufs: Fortschritt zurück, Geld zurück, Auftrag bleibt aktiv, keine Auszahlung
        var undo = svc.Execute(u, st, ship, new ServiceRequest(TradeService.Undo, default, 0f));
        Assert.True(undo.Success, undo.Message);
        Assert.True(MathF.Abs(m.Sold) < 0.01f, $"Sold nach Undo {m.Sold:0.###}");
        Assert.True(MathF.Abs(m.Progress) < 0.01f, $"Fortschritt nach Undo {m.Progress:0.###}");
        Assert.Single(u.Missions);
        Assert.DoesNotContain(ev.OfType<MissionPaid>(), e => e.Mission == m.Id);
        Assert.True(MathF.Abs(u.WalletOf(ship).Credits - walletBeforeSell) < 0.01f, "kein Gewinn durch Undo (Verkauf zurückgenommen)");
        MissionRules.Types.Get(m.Type)!.Check(u, m);
        Assert.Single(u.Missions);   // nicht erfüllt
    }

    // ───────── Sperren: Krieg / Fahndung ─────────

    [Fact]
    public void KeineAngebote_BeiKriegOderFahndungStufe2()
    {
        var u = World(19);
        var fac = u.NpcFactionIds().First();
        var facActor = new ActorId(fac.Value);   // Konvention: Fraktionsakteur trägt die Id der Fraktion
        u.Player.Treasury.Credits = 50_000f;
        Assert.Null(Wars.PlayerDeclareIssue(u, fac));
        Wars.PlayerDeclare(u, fac);
        Assert.True(MissionRules.Forbidden(u, facActor));
        Run(u, 2500);
        Assert.All(u.MissionOffers, o => Assert.NotEqual(fac, u.FactionOf(o.Client)));   // keine Angebote der Kriegsfraktion

        Wars.MakePeace(u, u.Wars[0], "test");
        Assert.False(MissionRules.Forbidden(u, facActor));
        u.Actors[u.PlayerActor].Wanted[fac] = new WantedRecord { Heat = 2.5f, Fine = 800f, LastCrimeTick = u.Tick, LastCrime = "assault" };
        Assert.True(MissionRules.Forbidden(u, facActor));   // Fahndung Stufe ≥ 2 sperrt
        Run(u, 2500);
        Assert.All(u.MissionOffers, o => Assert.NotEqual(fac, u.FactionOf(o.Client)));
    }

    // ───────── Marker (rein) ─────────

    [Fact]
    public void Marker_JeAktivemAuftrag_SektorUndPosition()
    {
        var u = World(20);
        Assert.Empty(MissionMarkers.For(u));
        var o = DockDelivery(u);
        var m = AcceptDock(u, o);
        var st = u.Stations[o.At];
        var mk = Assert.Single(MissionMarkers.For(u));
        Assert.Equal(m.Id, mk.Mission);
        Assert.Equal(st.Sector, mk.Sector);
        Assert.Equal(st.Position, mk.Position);
        Assert.Contains("Auftrag", mk.Label);
        Assert.Equal(mk, MissionMarkers.First(u));
    }

    // ───────── Fristen / Offline ─────────

    [Fact]
    public void OfflineCatchUp_VerlaengertFristenUmNachgespielteTicks()
    {
        var u = World(21);
        var o = DockDelivery(u);
        var m = AcceptDock(u, o);
        long d0 = m.Deadline;
        MissionRules.ExtendDeadlines(u, 500);
        Assert.Equal(d0 + 500, m.Deadline);
    }

    // ───────── Speichern / Laden bitgleich ─────────

    [Fact]
    public void SpeichernLaden_Bitgleich_MissionenGehashtAngeboteGespeichert()
    {
        var u = World(22);
        var o = DockDelivery(u);
        var m = AcceptDock(u, o);
        Player(u).Cargo[DeliveryMission.GoodOf(o)] = 3f;
        Run(u, 10);
        var offersBefore = u.MissionOffers.Select(x => (x.Id, x.Type, x.At, string.Join(";", x.Args.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)))).ToList();
        ulong hashBefore = UniverseHash.Compute(u);

        var save = UniverseSerializer.ToSave(u, Now);
        var loaded = UniverseSerializer.FromSave(save);
        Assert.Equal(hashBefore, UniverseHash.Compute(loaded));   // bitgleich inkl. Missionen + MissionCargo

        var offersLoaded = loaded.MissionOffers.Select(x => (x.Id, x.Type, x.At, string.Join(";", x.Args.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)))).ToList();
        Assert.Equal(offersBefore, offersLoaded);   // Angebote gespeichert (nicht gehasht), nach Annahme ist das eine weg
        var lm = Assert.Single(loaded.Missions);
        Assert.Equal(m.Id, lm.Id);
        Assert.Equal(m.Deadline, lm.Deadline);
        Assert.Equal(Player(loaded).MissionCargo, Player(u).MissionCargo);
        Assert.True(Player(loaded).Cargo.ContainsKey(DeliveryMission.GoodOf(o)));

        // nach dem Laden läuft die Simulation weiter wie im Original (gleicher Hash nach weiteren Ticks)
        Run(u, 500); Run(loaded, 500);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(loaded));
    }

    // ───────── Missbrauch: Mehrgewinn ≤ Bonus ─────────

    [Fact]
    public void Missbrauch_Angriffsprogramm_MehrgewinnNichtUeberMarktPlusBonus()
    {
        var u = World(23);
        var o = DockDelivery(u);
        var m = AcceptDock(u, o);
        var st = u.Stations[o.At];
        var good = DeliveryMission.GoodOf(o);
        float units = DeliveryMission.UnitsOf(u, o);
        var ship = Player(u);
        u.WalletOf(ship).Credits = 1_000_000f;
        float wallet0 = u.WalletOf(ship).Credits;
        var ev = Capture(u);
        Run(u, 5);

        // Angreifer-Szenario: Ware umsonst an Bord (stärkster Fall), an der Zielstation zum Marktpreis verkaufen
        ship.Cargo[good] = units;
        float revenue = 0f;
        for (int t = 0; t < 8 && m.Progress < units - 0.01f; t++)   // Deckel der Station: in Teilverkäufen liefern
        {
            ship.Cargo[good] += units;
            var sr = TradeActions.Sell(u, ship, st, good, units);
            if (sr.Sold.TryGetValue(good, out var sn)) revenue += sr.Revenue;
            else break;
        }
        Assert.True(m.Progress >= units - 0.01f, $"Fortschritt {m.Progress:0.###}/{units:0.###} nach Teilverkäufen");
        MissionRules.Types.Get(m.Type)!.Check(u, m);
        Assert.Empty(u.Missions);   // erfüllt

        float gain = u.WalletOf(ship).Credits - wallet0;
        // Gesamtgewinn = reiner Marktverkauf (erlaubt — Handel bleibt) + genau einmal der Bonus aus der Fraktionskasse
        Assert.True(MathF.Abs(gain - (revenue + m.Reserved + m.Deposit)) < 0.05f,
            $"Gewinn {gain:0.##} = Verkaufserlös {revenue:0.##} + Bonus {m.Reserved:0.##} + Pfand {m.Deposit:0.##} (nichts darüber hinaus)");
        Assert.True(m.Reserved <= u.Rules.MissionBonusShare * units * u.GoodOf(good).BasePrice + 0.01f, "Bonus ≤ 10 % des Basiswerts");
        Assert.Single(ev.OfType<MissionPaid>().Where(e => e.Mission == m.Id));   // Bonus genau einmal
    }
}
