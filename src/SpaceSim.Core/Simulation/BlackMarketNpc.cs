using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Schwarzmarkt für NPCs mit schlechten Charakterzügen (docs/SCHWARZMARKT.md „NPCs beim Hehler“, Regel <see cref="EconomyRules.BlackMarketNpc"/>).
/// <list type="bullet">
/// <item>Wer: KI-Händler und KI-Schürfer, deren Besitzer ein ausdrücklicher Akteur mit Gesetzestreue unter
/// <see cref="EconomyRules.BlackMarketNpcLawfulness"/> ist (aufgestiegene Einzelakteure, Fraktionen, Gesetzlose). Freie Einzelschiffe
/// (Gruppe „Freie Kapitäne“) haben keinen eigenen Charakter und gehen nie hin; Spielerschiffe nutzen den Dienst.</item>
/// <item>Reichweite: Gier bestimmt, wie weit sie fahren — höchstens max(1, round(Gier × <see cref="EconomyRules.BlackMarketNpcMaxJumps"/>)) Sprünge.</item>
/// <item>(a) Unverkäufliche Fracht (Händler: Restfracht nach <see cref="EconomyRules.StrandedCargoTicks"/>, Schürfer: Rest ohne Käufer) wird zum
/// nächsten erreichbaren Hehler gefahren und dort verkauft statt im All abgeworfen — wenn der Hehler genug Kasse hat und der Erlös die Fahrt trägt.</item>
/// <item>(b) Ein untätiger gesetzloser Händler kauft Hehlerware, wenn ein ehrlicher Abnehmer so viel mehr zahlt, dass die Runde mindestens
/// <see cref="EconomyRules.BlackMarketNpcBuyMargin"/> der Kosten abwirft (selten: die Hehlerware kostet 1,5 × Basispreis). Nicht, solange die
/// Piratenkasse an ihrem Deckel steht (<see cref="EconomyRules.PirateTreasuryCap"/>).</item>
/// </list>
/// Rein deterministisch (kein Zufall, feste Id-Reihenfolge); der Hehler hat keinen Markteintrag, darum bewegt nichts davon Preise. Folgen bei der Polizei hat
/// der Handel nicht (wie beim Spieler: das Verbrechen ist der Überfall, nicht die Hehlerei).
/// </summary>
public static class BlackMarketNpc
{
    /// <summary>Gilt die NPC-Regel überhaupt (Schwarzmarkt und NPC-Schalter an)?</summary>
    public static bool Active(Universe u) => u.Rules.BlackMarket && u.Rules.BlackMarketNpc;

    /// <summary>Besitzer des Schiffs, wenn es ein gesetzloses KI-Schiff ist (Charakter reicht für den Hehler); sonst null.</summary>
    public static Actor? LawlessOwner(Universe u, Ship ship)
    {
        if (!Active(u) || ship.IsPlayer || ship.Controller != Controller.Ai) return null;
        if (ship.Owner == u.FreeActor || ship.Owner == u.PlayerActor) return null;   // freie Einzelschiffe: kein eigener Charakter
        if (!u.Actors.TryGetValue(ship.Owner, out var a) || a.Kind == ActorKind.Player || a.Kind == ActorKind.Free) return null;
        return a.Personality.Lawfulness < u.Rules.BlackMarketNpcLawfulness ? a : null;
    }

    /// <summary>Wie viele Sprünge ein gesetzloser Besitzer für den Hehler fährt (Gier).</summary>
    public static int MaxJumps(Universe u, Actor owner) => Math.Max(1, (int)MathF.Round(owner.Personality.Greed * u.Rules.BlackMarketNpcMaxJumps));

    /// <summary>Ist das eine Station, deren Typ einen Hehler führt (auch wenn er gerade ausgeschaltet ist)?</summary>
    public static bool IsFenceType(Universe u, Station st) =>
        u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Services.Contains(BlackMarket.ServiceKey);

    /// <summary>Nächster erreichbarer Hehler für dieses Schiff (wenigste Sprünge, dann Id) innerhalb der Gier-Reichweite; null = keiner.</summary>
    public static Station? NearestFence(Universe u, Ship ship, Actor owner)
    {
        var profile = GateAccess.ProfileOf(u, ship);
        int max = MaxJumps(u, owner);
        Station? best = null; int bestHops = int.MaxValue;
        foreach (var st in u.StationsById())
        {
            if (!BlackMarket.IsAt(u, st) || !Docking.Allows(u, st, ship)) continue;
            var route = u.CachedRoute(ship.Sector, st.Sector, profile);
            if (route is null || route.Count > max || route.Count >= bestHops) continue;
            best = st; bestHops = route.Count;
        }
        return best;
    }

    /// <summary>Was der Hehler jetzt für die ganze Fracht zahlen würde (gedeckelt durch seine freie Kasse).</summary>
    public static float SaleValue(Universe u, Station fence, Ship ship)
    {
        float value = 0f;
        foreach (var (g, n) in ship.Cargo) if (n > 0.0001f && u.Goods.ContainsKey(g)) value += n * BlackMarket.BuyPrice(u, g);
        return MathF.Min(value, BlackMarket.Purse(u, fence));
    }

    /// <summary>Lohnt die Fahrt zum Hehler (Erlös über dem Schiffsunterhalt der Fahrzeit und über dem Mindestbetrag)?</summary>
    static bool Worth(Universe u, Ship ship, Station fence)
    {
        float value = SaleValue(u, fence, ship);
        if (value < u.Rules.BlackMarketNpcMinSale) return false;
        float ticks = Travel.Estimate(u, ship.Sector, ship.Position, fence.Sector, fence.DockPoint(ship.Id), ship);
        return value > ticks * u.Rules.ShipUpkeepPerTick;
    }

    /// <summary>(a) Händler: unverkäufliche Fracht zum Hehler fahren statt sie abzuwerfen. true = Fahrt begonnen (oder Tankstopp).</summary>
    public static bool TrySendTrader(Universe u, Ship ship)
    {
        if (LawlessOwner(u, ship) is not { } owner) return false;
        if (NearestFence(u, ship, owner) is not { } fence || !Worth(u, ship, fence)) return false;
        var good = ship.Cargo.Where(kv => kv.Value > 0.0001f).Select(kv => kv.Key).OrderBy(g => g.Value).First();
        ship.Order = new TradeOrder(default, fence.Id, good);
        ship.Phase = TradePhase.GoingToSell;
        ship.IdleSince = -1;
        return StartTrip(u, ship, fence, () => { ship.Order = null; ship.Phase = TradePhase.Idle; ship.IdleSince = u.Tick; });
    }

    /// <summary>(a) Schürfer: Rest ohne Käufer zum Hehler fahren statt über Bord. true = Fahrt begonnen (oder Tankstopp).</summary>
    public static bool TrySendMiner(Universe u, Ship ship, MineOrder order)
    {
        if (LawlessOwner(u, ship) is not { } owner) return false;
        if (NearestFence(u, ship, owner) is not { } fence || fence.Id == order.DeliverTo || !Worth(u, ship, fence)) return false;
        ship.Order = order with { DeliverTo = fence.Id };
        ship.MinePhase = MinePhase.GoingToStation;
        return StartTrip(u, ship, fence, () => { ship.Order = null; ship.MinePhase = MinePhase.Idle; ship.IdleSince = u.Tick; });
    }

    static bool StartTrip(Universe u, Ship ship, Station fence, Action stuck)
    {
        var plan = Refuel.PrepareTrip(u, ship, fence.Sector, fence.DockPoint(ship.Id));
        if (plan == Refuel.TripPlan.FuelStop) return true;
        if (plan == Refuel.TripPlan.Stuck || !Travel.Start(u, ship, fence.Sector, fence.DockPoint(ship.Id))) { stuck(); return false; }
        return true;
    }

    /// <summary>
    /// Verkauf der ganzen Fracht (je Ware in Id-Reihenfolge, so weit die freie Hehlerkasse reicht) an den Hehler. Bucht Piratenkasse → Schiffskasse
    /// (<see cref="TreasuryUpkeep"/> „blackmarket“) und veröffentlicht je Ware <see cref="BlackMarketNpcTrade"/>. Liefert den Erlös.
    /// </summary>
    public static float SellAll(Universe u, Station fence, Ship ship)
    {
        if (!BlackMarket.IsAt(u, fence) || BlackMarket.Fence(u, fence) is not { } pirates) return 0f;
        float total = 0f;
        foreach (var (g, have) in ship.Cargo.OrderBy(kv => kv.Key.Value).ToList())
        {
            if (have <= 0.0001f || !u.Goods.ContainsKey(g)) continue;
            float price = BlackMarket.BuyPrice(u, g);
            float purse = BlackMarket.Purse(u, fence);
            if (price <= 0f || purse <= 0f) continue;
            float units = MathF.Min(have, purse / price);
            if (units <= 0.0001f) continue;
            float pay = units * price;
            float left = have - units;
            if (left <= 0.0001f) ship.Cargo.Remove(g); else ship.Cargo[g] = left;
            BlackMarket.Add(u, fence, g, units);
            pirates.Treasury.Credits -= pay;
            u.WalletOf(ship).Credits += pay;
            total += pay;
            u.Events.Publish(new TreasuryUpkeep(pirates.Id, BlackMarket.ServiceKey, pay, 0f));
            u.Events.Publish(new BlackMarketNpcTrade(ship.Id, fence.Id, g, units, pay, true));
        }
        return total;
    }

    /// <summary>
    /// Ankunft eines KI-Händlers mit einem Hehlerauftrag (Kauf oder Verkauf an einer Station mit Hehler-Typ). true = erledigt (TraderSystem macht nichts weiter).
    /// Verkauf: alles an den Hehler, ein Rest (Kasse leer) bleibt an Bord (die Frist der Restfracht beginnt neu). Kauf: Hehlerware laden und zum Abnehmer.
    /// </summary>
    public static bool OnTraderArrive(Universe u, Ship ship, TradeOrder order)
    {
        if (ship.IsPlayer) return false;
        if (ship.Phase == TradePhase.GoingToSell && u.Stations.TryGetValue(order.Sell, out var sellSt) && IsFenceType(u, sellSt))
        {
            SellAll(u, sellSt, ship);
            ship.Order = null; ship.Phase = TradePhase.Idle; ship.IdleSince = u.Tick;
            ship.StrandedSince = -1;
            if (!ship.Cargo.Values.Any(n => n > 0.0001f)) ship.Cargo.Clear();
            return true;
        }
        if (ship.Phase == TradePhase.GoingToBuy && u.Stations.TryGetValue(order.Buy, out var buySt) && IsFenceType(u, buySt))
        {
            float units = BuyableUnits(u, buySt, ship, order.Good, u.Stations.GetValueOrDefault(order.Sell));
            if (units < 1f || !u.Stations.TryGetValue(order.Sell, out var dest))
            {
                ship.Order = null; ship.Phase = TradePhase.Idle; ship.IdleSince = u.Tick;
                return true;
            }
            Buy(u, buySt, ship, order.Good, units);
            ship.Phase = TradePhase.GoingToSell;
            StartTrip(u, ship, dest, () => { ship.Order = null; ship.Phase = TradePhase.Idle; ship.IdleSince = u.Tick; });
            return true;
        }
        return false;
    }

    /// <summary>Ankunft eines KI-Schürfers an einem Hehler: Rest verkaufen, was übrig bleibt (Kasse leer) geht über Bord wie bisher. true = erledigt.</summary>
    public static bool OnMinerArrive(Universe u, Ship ship, Station st)
    {
        if (ship.IsPlayer || !IsFenceType(u, st)) return false;
        SellAll(u, st, ship);
        ship.Cargo.Clear();
        ship.Order = null; ship.MinePhase = MinePhase.Idle; ship.IdleSince = u.Tick;
        return true;
    }

    static void Buy(Universe u, Station fence, Ship ship, GoodId g, float units)
    {
        var pirates = BlackMarket.Fence(u, fence)!;
        float price = BlackMarket.SellPrice(u, g);
        float cost = units * price;
        float rest = fence.BlackStock[g] - units;
        if (rest <= 0.0001f) fence.BlackStock.Remove(g); else fence.BlackStock[g] = rest;
        ship.Cargo[g] = ship.Cargo.GetValueOrDefault(g) + units;
        u.WalletOf(ship).Credits -= cost;
        pirates.Treasury.Credits += cost;
        u.Events.Publish(new TreasuryIncome(pirates.Id, BlackMarket.ServiceKey, cost));
        u.Events.Publish(new BlackMarketNpcTrade(ship.Id, fence.Id, g, units, cost, false));
    }

    /// <summary>Ganze Einheiten Hehlerware, die das Schiff jetzt kaufen würde (Lager, Laderaum, Geld, Annahme und Kasse des Abnehmers, Deckel der Piratenkasse).</summary>
    static float BuyableUnits(Universe u, Station fence, Ship ship, GoodId g, Station? dest)
    {
        if (!BlackMarket.IsAt(u, fence) || dest is null || !dest.Market.TryGetValue(g, out var e) || e.Price <= 0f) return 0f;
        if (BlackMarket.Fence(u, fence) is not { } pirates) return 0f;
        float price = BlackMarket.SellPrice(u, g);
        if (e.Price <= price) return 0f;   // kein Einkauf mit sicherem Verlust (wie TraderBuyGuard)
        float units = MathF.Min(fence.BlackStock.GetValueOrDefault(g), ship.FreeUnits(u, g));
        units = MathF.Min(units, MathF.Max(0f, u.WalletOf(ship).Credits - u.Rules.TraderCashReserve) / price);
        units = MathF.Min(units, MathF.Max(0f, dest.AcceptableUnits(e, u.Rules) - TraderSystem.LadenTowards(u, ship, dest.Id, g)));   // was andere schon hinfahren
        units = MathF.Min(units, MathF.Max(0f, dest.Credits) / e.Price);
        if (u.Rules.PirateTreasuryCap > 0f) units = MathF.Min(units, MathF.Max(0f, u.Rules.PirateTreasuryCap - pirates.Treasury.Credits) / price);
        return MathF.Floor(units);
    }

    /// <summary>
    /// (b) Untätiger gesetzloser Händler mit leerem Laderaum: lohnt sich eine Runde Hehler → ehrlicher Abnehmer? Bewertung = Menge × (Preis des Abnehmers −
    /// Hehlerpreis) − Unterhalt der Fahrzeit, verlangt mindestens <see cref="EconomyRules.BlackMarketNpcBuyMargin"/> der Kosten. true = Fahrt begonnen.
    /// </summary>
    public static bool TryBuyRound(Universe u, Ship ship)
    {
        if (LawlessOwner(u, ship) is not { } owner || ship.Cargo.Values.Any(n => n > 0.0001f)) return false;
        if (AutoLimitRules.Of(ship) is not null) return false;
        if (NearestFence(u, ship, owner) is not { } fence || fence.BlackStock.Count == 0) return false;
        float toFence = Travel.Estimate(u, ship.Sector, ship.Position, fence.Sector, fence.DockPoint(ship.Id), ship);
        float upkeep = u.Rules.ShipUpkeepPerTick;
        (Station dest, GoodId good, float score)? best = null;
        foreach (var (g, stock) in fence.BlackStock.OrderBy(kv => kv.Key.Value))
        {
            if (stock < 1f || !u.Goods.ContainsKey(g)) continue;
            float price = BlackMarket.SellPrice(u, g);
            foreach (var st in u.StationsById())
            {
                if (st.IsBuildSite || st.Id == fence.Id || !st.Market.TryGetValue(g, out var e) || e.Price <= price) continue;
                if (st.Produces(g) && !st.Consumes(g)) continue;
                if (!Docking.Allows(u, st, ship)) continue;
                float units = BuyableUnits(u, fence, ship, g, st);
                if (units < 1f) continue;
                float leg = Travel.Estimate(u, fence.Sector, fence.DockPoint(ship.Id), st.Sector, st.DockPoint(ship.Id), ship);
                if (float.IsInfinity(leg) || float.IsNaN(leg)) continue;
                float cost = units * price;
                float profit = units * (e.Price - price) - upkeep * (toFence + leg);
                if (profit < cost * u.Rules.BlackMarketNpcBuyMargin) continue;
                float score = profit / MathF.Max(1f, toFence + leg);
                if (best is null || score > best.Value.score) best = (st, g, score);
            }
        }
        if (best is not { } b) return false;
        ship.Order = new TradeOrder(fence.Id, b.dest.Id, b.good);
        ship.Phase = TradePhase.GoingToBuy;
        ship.IdleSince = -1;
        return StartTrip(u, ship, fence, () => { ship.Order = null; ship.Phase = TradePhase.Idle; ship.IdleSince = u.Tick; });
    }
}
