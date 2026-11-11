using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceSim.Core.Simulation;

/// <summary>Händler-KI: sucht die beste Handelsspanne, reist abstrakt (Fahrplan) und handelt bei Ankunft.</summary>
public sealed class TraderSystem : ISimSystem
{
    /// <summary>Ticks, die ein Schiff ohne Auftrag wartet, bevor es erneut nach Geschäften sucht.</summary>
    public const int ThinkInterval = 10;

    public void Step(Universe u, int dt)
    {
        foreach (var ship in u.Ships.Values)
        {
            // Aufgabenfilter bleibt die Rolle, zusätzlich muss das Schiff Frachtraum haben; fehlt er, wird das Schiff freigegeben
            if (ship.Role != ShipRole.Trader) continue;
            if (!ship.Has(Capability.Cargo)) { JobRelease.Release(u, ship); continue; }

            if (ship.Transit is { } t)
            {
                if (u.Tick >= t.ArrivalTick) Arrive(u, ship, t);
            }
            else if (ship.Order is null && u.Tick >= ship.NextThinkTick)
            {
                // Phase 10: Fracht ohne Auftrag (Reise wegen einer Sperre angehalten, Abnehmer weggefallen) zuerst verkaufen
                if (SellStranded(u, ship)) continue;
                if (!PickOrder(u, ship))
                {
                    if (ship.IdleSince < 0) ship.IdleSince = u.Tick;
                    ship.NextThinkTick = u.Tick + IdleBackoff(u, ship) + ship.Id.Value % 7;
                }
            }
        }
    }

    /// <summary>
    /// Bewertet jede Kauf-/Verkaufsstation je Ware nach Gewinn pro Tick (Spanne × Menge / Reisezeit).
    /// Bestände, die andere Händler bereits angefahren haben, gelten als reserviert; sonst würden alle auf dieselbe Route laufen.
    /// </summary>
    /// <summary>
    /// Wartezeit bis zur nächsten Suche: wächst mit der Dauer der Arbeitslosigkeit (bis zum Fünffachen). Wer lange nichts findet,
    /// muss nicht jede Sekunde den ganzen Markt durchsuchen – das spart bei vielen Schiffen den Hauptteil der Rechenzeit.
    /// </summary>
    public static int IdleBackoff(Universe u, Ship ship) =>
        ThinkInterval * (1 + (int)Math.Min(4, Math.Max(0, u.Tick - ship.IdleSince) / 100));

    /// <summary>
    /// Beste Handelsrunde für dieses Schiff jetzt (Phase 6 auch für die Aufgabenwahl, <see cref="TradeJob"/>): Auftrag und Bewertung
    /// = Gewinn nach Treibstoffkosten je Tick Reisezeit (Credits/Tick). Reine Auswertung ohne Zustandsänderung; (null, 0) = nichts lohnt.
    /// </summary>
    public static (TradeOrder? Order, float Score) BestTrade(Universe u, Ship ship)
    {
        var reserved = new Dictionary<(StationId, GoodId), float>();
        foreach (var o in u.Ships.Values)
            if (o.Order is TradeOrder to && o.Phase == TradePhase.GoingToBuy)
                reserved[(to.Buy, to.Good)] = reserved.GetValueOrDefault((to.Buy, to.Good)) + o.CapacityVolumeFor(u, to.Good) / u.Goods[to.Good].VolumePerUnit;

        // Phase 8: Baustellen nehmen nur die fehlende Menge an; was andere Händler schon dorthin fahren (unterwegs zum Kauf: ihre Kapazität,
        // unterwegs zum Verkauf: ihre Ladung), gilt als vergeben — sonst fahren alle dieselbe Lieferung und laden sie unbezahlt ab
        Dictionary<(StationId, GoodId), float>? incoming = null;
        foreach (var o in u.Ships.Values)
            if (o != ship && o.Order is TradeOrder io && u.Stations.TryGetValue(io.Sell, out var target) && target.IsBuildSite)
            {
                incoming ??= new Dictionary<(StationId, GoodId), float>();
                float amount = o.Phase == TradePhase.GoingToSell ? o.Cargo.GetValueOrDefault(io.Good)
                    : o.CapacityVolumeFor(u, io.Good) / u.Goods[io.Good].VolumePerUnit;
                incoming[(io.Sell, io.Good)] = incoming.GetValueOrDefault((io.Sell, io.Good)) + amount;
            }

        TradeOrder? best = null;
        float bestScore = 0f;
        // Phase 12: Grenzen eines Automatikauftrags (Spielerflotte: Reichweite, höchster Einkaufspreis, Rücklage); KI ohne Befehl: keine
        var limits = AutoLimitRules.Of(ship);
        float spendable = limits is null ? 0f : AutoLimitRules.Spendable(u, ship, limits);
        var fuelGood = Refuel.ActiveFuel(ship);
        // Phase 10: Die Verkaufsfahrt geht beladen — mit diesem Profil schätzen, damit ein Embargo die Route schon hier ausschließt
        var ladenProfile = GateAccess.ProfileOf(u, ship) with { Laden = true };
        // Treibstoffpreis wie Refuel.FuelPrice (aktuelle Station, sonst Kaufstation, sonst Durchschnitt), aber nur einmal je Suche ermittelt
        float fuelHere = 0f, fuelAvg = 0f;
        if (fuelGood is { } fg)
        {
            var here = Refuel.CurrentStation(u, ship);
            if (here is not null && here.Market.TryGetValue(fg, out var he) && he.Price > 0f) fuelHere = he.Price;
            fuelAvg = Refuel.FuelPrice(u, ship, fg);
        }
        foreach (var good in u.Goods.Values)
        {
            float capacityUnits = ship.CapacityVolumeFor(u, good.Id) / good.VolumePerUnit;
            if (capacityUnits < 1f) continue;   // Ware passt nicht in den Laderaum (Frachtform-Regel)
            var traders = u.StationsTrading(good.Id);
            if (traders.Count < 2) continue;
            foreach (var buy in traders)
            {
                if (buy.IsStockOnly(good.Id)) continue;   // Phase 5a: Lager ohne Rezept beliefern, nicht leerkaufen
                var b = buy.Market[good.Id];
                float available = b.Amount - reserved.GetValueOrDefault((buy.Id, good.Id));
                if (available < 1f) continue;
                float units = MathF.Min(capacityUnits, available);
                if (limits is not null)
                {
                    if (!AutoLimitRules.InRange(u, limits, buy.Sector) || !AutoLimitRules.PriceOk(limits, good, b.Price) || b.Price <= 0f) continue;
                    units = MathF.Min(units, MathF.Floor(spendable / b.Price));
                    if (units < 1f) continue;
                }
                float toBuy = Travel.Estimate(u, ship.Sector, ship.Position, buy.Sector, buy.DockPoint(ship.Id), ship);
                if (float.IsPositiveInfinity(toBuy)) continue;
                float fuelPrice = fuelHere > 0f ? fuelHere
                    : fuelGood is { } fgb && buy.Market.TryGetValue(fgb, out var be) && be.Price > 0f ? be.Price : fuelAvg;
                float distToBuy = fuelGood is null ? 0f : Travel.EstimateDistance(u, ship.Sector, ship.Position, buy.Sector, buy.DockPoint(ship.Id), ship);
                foreach (var sell in traders)
                {
                    if (sell.Id == buy.Id) continue;
                    if (limits is not null && !AutoLimitRules.InRange(u, limits, sell.Sector)) continue;
                    var s = sell.Market[good.Id];
                    float margin = s.Price - b.Price;
                    if (margin <= 0f) continue;
                    // Die Station zahlt nur, was ihre Kasse hergibt (der Rest der Ladung wird ohne Bezahlung abgeladen): mit dem
                    // tatsächlichen Erlös rechnen statt arme Abnehmer ganz zu überspringen. Sonst verhungert eine Station, deren
                    // Eingangsware knapp und damit teuer ist, endgültig — sie kann die volle Ladung nie zu 25 % anzahlen.
                    // Phase 5a: Händler kaufen nur, was der Abnehmer noch annimmt (Wunschbestand × OverstockFactor). Vorher wurde der Rest
                    // unbezahlt abgeladen; bei kleinen Wunschbeständen (Modulfabriken, Lager) drückte das die Preise dauerhaft auf 0,25×.
                    float accept = sell.AcceptableUnits(good.Id, u.Rules);
                    if (incoming is not null && sell.IsBuildSite) accept -= incoming.GetValueOrDefault((sell.Id, good.Id));
                    float load = MathF.Min(units, MathF.Floor(accept));
                    if (load < 1f) continue;
                    float paidUnits = MathF.Min(load, MathF.Max(0f, sell.Credits) / s.Price);
                    float profit = paidUnits * s.Price - load * b.Price;
                    if (profit <= 0f) continue;
                    if (sell.AcceptableUnits(good.Id, u.Rules) < MathF.Min(load, 5f)) continue;   // Lager voll: nimmt nichts mehr
                    float toSell = Travel.Estimate(u, buy.Sector, buy.DockPoint(ship.Id), sell.Sector, sell.DockPoint(ship.Id), ship, ladenProfile);
                    if (float.IsPositiveInfinity(toSell)) continue;
                    // Phase 4b: Treibstoffkosten der Runde (Hin- und Rückfahrt) vom Gewinn abziehen
                    float fuelCost = Refuel.TripFuelCost(u, ship, buy, sell, distToBuy, fuelPrice);
                    // Phase 11: Risikoabschlag der beladenen Fahrt aus der Lagekarte (erwarteter Verlust = Routenrisiko × Wert der Ladung).
                    // Gleicher Abschlag für alle Händler, gedeckelt je Sektor — er verschiebt Routen, statt alle auf eine zu treiben.
                    float risk = ThreatMap.RouteRisk(u, buy.Sector, sell.Sector) * load * s.Price;
                    float score = (profit - fuelCost - risk) / (toBuy + toSell);
                    if (score > bestScore) { bestScore = score; best = new TradeOrder(buy.Id, sell.Id, good.Id); }
                }
            }
        }
        return (best, bestScore);
    }

    static bool PickOrder(Universe u, Ship ship)
    {
        var (best, _) = BestTrade(u, ship);
        if (best is null) return false;
        ship.Order = best;
        ship.Phase = TradePhase.GoingToBuy;
        long idleBefore = ship.IdleSince;   // ein Liegenbleiber (Stuck) behält seinen Leerlauf-Beginn, sonst wird er nie aufgegeben
        ship.IdleSince = -1;
        // Phase 4b: Treibstoff für die Reise sichern (Tanken hier oder Tankstopp unterwegs), erst dann starten
        var plan = Refuel.PrepareTrip(u, ship, u.Stations[best.Buy].Sector, u.Stations[best.Buy].DockPoint(ship.Id));
        if (plan == Refuel.TripPlan.FuelStop) return true;   // Order ist jetzt GoToOrder zur Tankstelle, Transit läuft
        if (plan == Refuel.TripPlan.Stuck)
        {
            // pleite: nicht reisen, liegen bleiben (FleetSystem gibt das Schiff nach Leerlauf auf)
            ship.Order = null; ship.Phase = TradePhase.Idle; ship.IdleSince = idleBefore >= 0 ? idleBefore : u.Tick;
            return false;
        }
        Travel.Start(u, ship, u.Stations[best.Buy].Sector, u.Stations[best.Buy].DockPoint(ship.Id));
        return true;
    }

    /// <summary>
    /// Phase 10: Ein Händler ohne Auftrag mit Fracht an Bord (angehaltene Reise hinter einer Sperre, weggefallener Abnehmer) fährt sie zum besten
    /// erreichbaren Käufer, statt sie dauerhaft mitzuschleppen. false = keine Fracht oder kein Käufer erreichbar.
    /// </summary>
    static bool SellStranded(Universe u, Ship ship)
    {
        var good = ship.Cargo.Where(kv => kv.Value > 0.001f).Select(kv => kv.Key).OrderBy(g => g.Value).FirstOrDefault();
        if (good.Value == 0) return false;
        var buyer = TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position);
        if (buyer is null || !buyer.Market.ContainsKey(good)) return false;
        ship.Order = new TradeOrder(buyer.Id, buyer.Id, good);
        ship.Phase = TradePhase.GoingToSell;
        ship.IdleSince = -1;
        var plan = Refuel.PrepareTrip(u, ship, buyer.Sector, buyer.DockPoint(ship.Id));
        if (plan == Refuel.TripPlan.FuelStop) return true;
        if (plan == Refuel.TripPlan.Stuck) { AccessEnforcement.Release(u, ship); return false; }
        return Travel.Start(u, ship, buyer.Sector, buyer.DockPoint(ship.Id));
    }

    /// <summary>Käufer muss einen nennenswerten Teil einer Ladung bezahlen können, sonst lohnt die Fahrt nicht.</summary>
    public static bool CanAfford(Station buyer, float price, float units) => buyer.Credits > 0.25f * units * price;

    static void Arrive(Universe u, Ship ship, Transit t)
    {
        u.FinishTransit(ship);
        if (ship.Order is GoToOrder g && u.Stations.ContainsKey(g.Target))
        {
            // Tankstopp (Phase 4b): bei Ankunft an der Tankstelle volltanken; ein laufender Verkauf wird danach neu gehangen
            var st = u.Stations[g.Target];
            Refuel.TankUp(u, ship, st);
            ship.Order = null;
            if (ship.Phase == TradePhase.GoingToSell && ship.Cargo.Count > 0)
            {
                var good = ship.Cargo.Keys.OrderBy(k => k.Value).First();
                var next = TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position);
                if (next is not null)
                {
                    ship.Order = new TradeOrder(st.Id, next.Id, good);
                    var plan = Refuel.PrepareTrip(u, ship, next.Sector, next.DockPoint(ship.Id));
                    if (plan == Refuel.TripPlan.FuelStop) return;
                    if (plan == Refuel.TripPlan.Depart)
                    {
                        Travel.Start(u, ship, next.Sector, next.DockPoint(ship.Id));
                        return;
                    }
                }
            }
            ship.Order = null;
            ship.Phase = TradePhase.Idle;
            ship.IdleSince = u.Tick;
            return;
        }
        if (ship.Order is not TradeOrder order || !u.Stations.ContainsKey(order.Buy) || !u.Stations.ContainsKey(order.Sell))
        {
            ship.Order = null; ship.Phase = TradePhase.Idle; ship.Cargo.Clear();
            return;
        }

        if (ship.Phase == TradePhase.GoingToBuy)
        {
            var st = u.Stations[order.Buy];
            var stock = st.Stock(order.Good);
            var good = u.Goods[order.Good];
            float free = ship.FreeVolumeFor(u, order.Good);   // Frachtform-Regel: Flüssigkeiten nur in liquid+any
            // Phase 12: Spielerschiffe buchen auf die Kasse des Besitzers (NPC: das Schiff selbst) und halten die Rücklage ihres Auftrags
            var wallet = u.WalletOf(ship);
            float money = AutoLimitRules.Of(ship) is { } lim ? AutoLimitRules.Spendable(u, ship, lim) : wallet.Credits;
            float amount = MathF.Min(MathF.Min(free / good.VolumePerUnit, stock.Amount),
                                     stock.Price > 0 ? money / stock.Price : 0f);
            // Phase 5a: nur so viel kaufen, wie der Abnehmer jetzt noch annimmt (sonst würde der Rest dort unbezahlt abgeladen)
            var sellSt = u.Stations[order.Sell];
            amount = MathF.Min(amount, MathF.Floor(sellSt.AcceptableUnits(order.Good, u.Rules)));
            if (amount > 0)
            {
                float cost = amount * stock.Price;
                stock.Amount -= amount;
                st.Credits += cost;
                wallet.Credits -= cost;
                ship.Cargo[order.Good] = ship.Cargo.GetValueOrDefault(order.Good) + amount;
                ship.Phase = TradePhase.GoingToSell;
                // Phase 4b: Treibstoff für die Fahrt zum Verkäufer sichern (Tankstopp verwirft den Auftrag, Ankunft hängt ihn neu)
                var plan = Refuel.PrepareTrip(u, ship, u.Stations[order.Sell].Sector, u.Stations[order.Sell].DockPoint(ship.Id));
                if (plan == Refuel.TripPlan.FuelStop) return;
                if (plan == Refuel.TripPlan.Stuck)
                {
                    ship.Order = null; ship.Phase = TradePhase.Idle; ship.IdleSince = u.Tick;
                    return;
                }
                Travel.Start(u, ship, u.Stations[order.Sell].Sector, u.Stations[order.Sell].DockPoint(ship.Id));
            }
            else { ship.Order = null; ship.Phase = TradePhase.Idle; ship.IdleSince = u.Tick; }
        }
        else if (ship.Phase == TradePhase.GoingToSell)
        {
            var st = u.Stations[order.Sell];
            if (!st.Market.ContainsKey(order.Good))
            {
                // Phase 8: Der Abnehmer führt die Ware nicht mehr (z. B. Baustelle inzwischen fertig): nächsten Käufer anfahren statt
                // unbezahlt abzuladen (und der Station einen falschen Markteintrag zu geben)
                var next = TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position, exclude: st.Id);
                if (next is not null)
                {
                    ship.Order = new TradeOrder(order.Buy, next.Id, order.Good);
                    var plan = Refuel.PrepareTrip(u, ship, next.Sector, next.DockPoint(ship.Id));
                    if (plan == Refuel.TripPlan.FuelStop) return;
                    if (plan == Refuel.TripPlan.Depart) { Travel.Start(u, ship, next.Sector, next.DockPoint(ship.Id)); return; }
                }
                ship.Cargo.Remove(order.Good);
                ship.Order = null; ship.Phase = TradePhase.Idle; ship.IdleSince = u.Tick;
                return;
            }
            var stock = st.Stock(order.Good);
            float amount = ship.Cargo.GetValueOrDefault(order.Good);
            // Die Station zahlt nur, was sie hat; der Rest der Ladung wird ohne Bezahlung abgeladen
            float paid = stock.Price > 0 ? MathF.Min(amount, MathF.Max(0f, st.Credits) / stock.Price) : 0f;
            float revenue = paid * stock.Price;
            stock.Amount += amount;
            st.Credits -= revenue;
            u.WalletOf(ship).Credits += revenue;
            ship.Cargo.Remove(order.Good);
            ship.Order = null;
            ship.Phase = TradePhase.Idle;
            ship.IdleSince = u.Tick;
            u.Events.Publish(new TradeCompleted(ship.Id, order.Buy, order.Sell, order.Good, paid, revenue));
            if (st.IsBuildSite) ConstructionRules.OnDelivered(u, st, ship, order.Good, amount, "trader");
        }
    }
}
