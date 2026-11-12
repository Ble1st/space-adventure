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
                DiscardUnsellable(u, ship);   // Phase 19: niemand nimmt die Fracht — nach der Frist im All abwerfen statt sie ewig mitzuschleppen
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
    // Phase 17 (Leistung): wiederverwendete Puffer der Bewertung (je Thread; BestTrade ruft sich nicht selbst auf). Reine Zwischenwerte eines Aufrufs.
    [ThreadStatic] static Dictionary<(StationId, GoodId), float>? t_reserved;
    [ThreadStatic] static Dictionary<StationId, (float ToBuy, float DistToBuy, float FuelPrice)>? t_buyLeg;
    [ThreadStatic] static Dictionary<(StationId, StationId), (float ToSell, float FuelCost, float Risk)>? t_sellLeg;

    public static (TradeOrder? Order, float Score) BestTrade(Universe u, Ship ship)
    {
        var reserved = t_reserved ??= new Dictionary<(StationId, GoodId), float>();
        reserved.Clear();
        // Phase 17: Reisezeiten, Treibstoffkosten und Routenrisiko hängen nur an Kauf-/Verkaufsstation, nicht an der Ware — je Aufruf einmal je Station
        // bzw. Stationspaar berechnet (gleiche Formeln, gleiche Werte) statt für jede Ware erneut
        var buyLegs = t_buyLeg ??= new Dictionary<StationId, (float, float, float)>();
        var sellLegs = t_sellLeg ??= new Dictionary<(StationId, StationId), (float, float, float)>();
        buyLegs.Clear(); sellLegs.Clear();
        foreach (var o in u.Ships.Values)
            if (o.Order is TradeOrder to && o.Phase == TradePhase.GoingToBuy)
                reserved[(to.Buy, to.Good)] = reserved.GetValueOrDefault((to.Buy, to.Good)) + o.CapacityVolumeFor(u, to.Good) / u.Goods[to.Good].VolumePerUnit;

        // Phase 8: Baustellen nehmen nur die fehlende Menge an; was andere Händler schon dorthin fahren (unterwegs zum Kauf: ihre Kapazität,
        // unterwegs zum Verkauf: ihre Ladung), gilt als vergeben — sonst fahren alle dieselbe Lieferung und laden sie unbezahlt ab.
        // Phase 19: für alle Abnehmer (Regel ReserveIncoming) — seit kein Händler mehr über die Annahme ablädt, blieb der zweite Händler auf einer Ladung
        // sitzen, die schon ein anderer brachte, warf sie ab und verlor das Geld dafür (KI-Händler gingen reihenweise pleite)
        Dictionary<(StationId, GoodId), float>? incoming = null;
        bool reserveAll = u.Rules.ReserveIncoming;
        foreach (var o in u.Ships.Values)
            if (o != ship && o.Order is TradeOrder io && u.Stations.TryGetValue(io.Sell, out var target) && (reserveAll || target.IsBuildSite))
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
        // Phase 19: auch ohne Automatikgrenzen nur planen, was die eigene Kasse bezahlen kann — sonst fuhr ein mittelloses Schiff immer wieder zur
        // Kaufstation, konnte dort nichts kaufen und plante sofort neu (Leerlauf nie erreicht, nie als pleite aufgegeben)
        // Phase 20: ein KI-Händler behält eine Betriebsreserve (TraderCashReserve) — vorher setzte er die ganze Kasse auf eine Ladung, und blieb sie liegen
        // (Abnehmer pausiert), war er pleite
        float reserve = limits is null && ship.Controller == Controller.Ai ? u.Rules.TraderCashReserve : 0f;
        float spendable = limits is null ? MathF.Max(0f, u.WalletOf(ship).Credits - reserve) : AutoLimitRules.Spendable(u, ship, limits);
        var fuelGood = Refuel.Active(u) ? Refuel.ActiveFuel(ship) : null;   // Phase 14: Treibstoff aus = keine Treibstoffkosten
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
            float capacityUnits = ship.CapacityVolumeFor(good.Form) / good.VolumePerUnit;
            if (capacityUnits < 1f) continue;   // Ware passt nicht in den Laderaum (Frachtform-Regel)
            var traders = u.StationsTrading(good.Id);
            if (traders.Count < 2) continue;
            var entries = u.StationsTradingEntries(good.Id);   // Phase 17: Eintrag i = Markt der Station i (spart die Suche je Paar)
            for (int bi = 0; bi < traders.Count; bi++)
            {
                var buy = traders[bi];
                // Phase 5a: Lager ohne Rezept beliefern, nicht leerkaufen (IsStockOnly). Phase 14: nur beim Hersteller einkaufen (Rezeptausgang). Vorher
                // kauften Händler Zutaten bei Verbrauchern wieder auf und fuhren sie zum nächsten Verbraucher (Nahrung Kolonie → Farm, Wasser Lebenserhaltung
                // → Farm): Der Verbraucher kaufte teuer und verkaufte billig zurück, die Kolonie blutete so aus (dauerhaft am Preisdeckel, Kasse leer).
                // Beides zusammen = die Station stellt die Ware her (ein Lager ohne Rezept stellt nichts her).
                if (!buy.Produces(good.Id)) continue;
                var b = entries[bi];
                float available = b.Amount - reserved.GetValueOrDefault((buy.Id, good.Id));
                if (available < 1f) continue;
                float units = MathF.Min(capacityUnits, available);
                if (limits is not null && (!AutoLimitRules.InRange(u, limits, buy.Sector) || !AutoLimitRules.PriceOk(limits, good, b.Price))) continue;
                if (b.Price <= 0f) continue;
                units = MathF.Min(units, MathF.Floor(spendable / b.Price));
                if (units < 1f) continue;
                if (!buyLegs.TryGetValue(buy.Id, out var leg))
                {
                    float tb = Travel.Estimate(u, ship.Sector, ship.Position, buy.Sector, buy.DockPoint(ship.Id), ship);
                    float fp = fuelHere > 0f ? fuelHere
                        : fuelGood is { } fgb && buy.Market.TryGetValue(fgb, out var be) && be.Price > 0f ? be.Price : fuelAvg;
                    float db = fuelGood is null || float.IsPositiveInfinity(tb) ? 0f : Travel.EstimateDistance(u, ship.Sector, ship.Position, buy.Sector, buy.DockPoint(ship.Id), ship);
                    buyLegs[buy.Id] = leg = (tb, db, fp);
                }
                float toBuy = leg.ToBuy;
                if (float.IsPositiveInfinity(toBuy)) continue;
                float fuelPrice = leg.FuelPrice, distToBuy = leg.DistToBuy;
                for (int si = 0; si < traders.Count; si++)
                {
                    var sell = traders[si];
                    if (sell.Id == buy.Id) continue;
                    if (limits is not null && !AutoLimitRules.InRange(u, limits, sell.Sector)) continue;
                    var s = entries[si];
                    float margin = s.Price - b.Price;
                    if (margin <= 0f) continue;
                    // Die Station zahlt nur, was ihre Kasse hergibt (der Rest der Ladung wird ohne Bezahlung abgeladen): mit dem
                    // tatsächlichen Erlös rechnen statt arme Abnehmer ganz zu überspringen. Sonst verhungert eine Station, deren
                    // Eingangsware knapp und damit teuer ist, endgültig — sie kann die volle Ladung nie zu 25 % anzahlen.
                    // Phase 5a: Händler kaufen nur, was der Abnehmer noch annimmt (Wunschbestand × OverstockFactor). Vorher wurde der Rest
                    // unbezahlt abgeladen; bei kleinen Wunschbeständen (Modulfabriken, Lager) drückte das die Preise dauerhaft auf 0,25×.
                    float acceptAll = sell.AcceptableUnits(s, u.Rules);
                    float accept = acceptAll;
                    if (incoming is not null) accept -= incoming.GetValueOrDefault((sell.Id, good.Id));
                    float load = MathF.Min(units, MathF.Floor(accept));
                    // Phase 14: nur so viel einplanen, wie der Abnehmer bezahlen kann (beim Einkauf gilt dieselbe Grenze). Vorher rechnete die Bewertung mit
                    // voller Ladung und unbezahltem Rest: Bei teuren Waren und knapper Kasse des Abnehmers (Kolonie, Exportsenke) galt jede Runde als
                    // Verlust, Luxusgüter wurden nie gefahren.
                    load = MathF.Min(load, MathF.Floor(MathF.Max(0f, sell.Credits) / s.Price));
                    if (load < 1f) continue;
                    float paidUnits = MathF.Min(load, MathF.Max(0f, sell.Credits) / s.Price);
                    float profit = paidUnits * s.Price - load * b.Price;
                    if (profit <= 0f) continue;
                    if (acceptAll < MathF.Min(load, 5f)) continue;   // Lager voll: nimmt nichts mehr
                    if (!sellLegs.TryGetValue((buy.Id, sell.Id), out var sl))
                    {
                        float ts = Travel.Estimate(u, buy.Sector, buy.DockPoint(ship.Id), sell.Sector, sell.DockPoint(ship.Id), ship, ladenProfile);
                        sellLegs[(buy.Id, sell.Id)] = sl = float.IsPositiveInfinity(ts) ? (ts, 0f, 0f)
                            : (ts, Refuel.TripFuelCost(u, ship, buy, sell, distToBuy, fuelPrice), ThreatMap.RouteRisk(u, buy.Sector, sell.Sector));
                    }
                    float toSell = sl.ToSell;
                    if (float.IsPositiveInfinity(toSell)) continue;
                    // Phase 4b: Treibstoffkosten der Runde (Hin- und Rückfahrt) vom Gewinn abziehen
                    float fuelCost = sl.FuelCost;
                    // Phase 11: Risikoabschlag der beladenen Fahrt aus der Lagekarte (erwarteter Verlust = Routenrisiko × Wert der Ladung).
                    // Gleicher Abschlag für alle Händler, gedeckelt je Sektor — er verschiebt Routen, statt alle auf eine zu treiben.
                    float risk = sl.Risk * load * s.Price;
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
        if (!ship.Cargo.Values.Any(n => n > 0.001f)) return false;
        var buyer = TradeActions.BestBuyer(u, ship, ship.Sector, ship.Position);
        if (buyer is null) return false;
        if (AutoLimitRules.Of(ship) is { } lim && !AutoLimitRules.InRange(u, lim, buyer.Sector)) return false;   // Phase 19: Automatik bleibt im Umkreis
        // Phase 19: die (kleinste) Ware an Bord, die dieser Käufer führt — vorher galt nur die erste Ware an Bord, und eine andere blieb liegen
        var good = ship.Cargo.Where(kv => kv.Value > 0.001f && buyer.Market.ContainsKey(kv.Key)).Select(kv => kv.Key).OrderBy(g => g.Value).FirstOrDefault();
        if (good.Value == 0) return false;
        // Phase 19: ohne Einkaufsstation (Id 0) — die Ware stammt aus einer früheren Runde, der Käufer ist nicht ihr Hersteller
        ship.Order = new TradeOrder(default, buyer.Id, good);
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
        // Phase 19: ein Restverkauf (SellStranded) hat keine Einkaufsstation (Id 0)
        if (ship.Order is not TradeOrder order || !(u.Stations.ContainsKey(order.Buy) || (order.Buy.Value == 0 && ship.Phase == TradePhase.GoingToSell))
            || !u.Stations.ContainsKey(order.Sell))
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
            // Phase 20: KI-Händler ohne Automatikgrenzen behalten die Betriebsreserve (wie in der Bewertung)
            float money = AutoLimitRules.Of(ship) is { } lim ? AutoLimitRules.Spendable(u, ship, lim)
                : MathF.Max(0f, wallet.Credits - (ship.Controller == Controller.Ai ? u.Rules.TraderCashReserve : 0f));
            float amount = MathF.Min(MathF.Min(free / good.VolumePerUnit, stock.Amount),
                                     stock.Price > 0 ? money / stock.Price : 0f);
            // Phase 5a: nur so viel kaufen, wie der Abnehmer jetzt noch annimmt (sonst würde der Rest dort unbezahlt abgeladen)
            // Phase 19: abzüglich der Ladung anderer Händler, die schon auf dem Weg dorthin sind (ReserveIncoming)
            var sellSt = u.Stations[order.Sell];
            amount = MathF.Min(amount, MathF.Floor(sellSt.AcceptableUnits(order.Good, u.Rules) - (u.Rules.ReserveIncoming ? LadenTowards(u, ship, sellSt.Id, order.Good) : 0f)));
            // Phase 14: nur so viel kaufen, wie der Abnehmer jetzt bezahlen kann (wie in der Bewertung)
            if (sellSt.Market.TryGetValue(order.Good, out var se) && se.Price > 0f)
                amount = MathF.Min(amount, MathF.Floor(MathF.Max(0f, sellSt.Credits) / se.Price));
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
            float have = ship.Cargo.GetValueOrDefault(order.Good);
            // Phase 14: Die Station nimmt nur, was sie noch annimmt und bezahlen kann. Ein Rest (andere Händler waren schneller, Kasse inzwischen knapp)
            // fährt zum nächsten Käufer.
            // Phase 19: Menge nach der gemeinsamen Verkaufsregel (ganze Einheiten bis zur Annahme, alles bei einem Überhang unter der Resttoleranz,
            // TradeActions.SaleUnits). Einen Rest ohne weiteren Käufer lädt der Händler nicht mehr unbezahlt über die Annahme ab (vorher: Wasserstoff, Titan,
            // Elektronik in kleinen Lagern dauerhaft am unteren Preisdeckel) — er behält ihn und versucht es beim nächsten Nachdenken (SellStranded);
            // findet sich dann niemand, wirft ein KI-Händler ihn im All ab.
            var (cap, allCap) = stock.Price > 0f ? TradeActions.SaleCaps(u, st, order.Good, MathF.Max(0f, st.Credits)) : (0f, 0f);
            float amount = TradeActions.SaleUnits(have, have, cap, allCap);
            if (st.IsBuildSite) amount = MathF.Min(have, MathF.Max(0f, st.AcceptableUnits(stock, u.Rules)));   // Baustelle: genau die fehlende Menge (Treuhand zahlt)
            if (amount > 0.0001f) Deliver(u, ship, st, stock, order, amount);
            float rest = ship.Cargo.GetValueOrDefault(order.Good);
            if (rest <= 0.0001f) ship.Cargo.Remove(order.Good);
            else if (TradeActions.BestBuyerFor(u, ship, order.Good, rest, st.Id) is { } next)
            {
                ship.Order = new TradeOrder(order.Buy, next.Id, order.Good);
                ship.Phase = TradePhase.GoingToSell;
                var restPlan = Refuel.PrepareTrip(u, ship, next.Sector, next.DockPoint(ship.Id));
                if (restPlan == Refuel.TripPlan.FuelStop) return;
                if (restPlan == Refuel.TripPlan.Depart && Travel.Start(u, ship, next.Sector, next.DockPoint(ship.Id))) return;
            }
            ship.Order = null;
            ship.Phase = TradePhase.Idle;
            ship.IdleSince = u.Tick;
        }
    }

    /// <summary>Phase 19: Ladung dieser Ware, die andere Händler gerade zu dieser Station fahren (unterwegs zum Verkauf).</summary>
    static float LadenTowards(Universe u, Ship ship, StationId station, GoodId good)
    {
        float sum = 0f;
        foreach (var o in u.Ships.Values)
            if (o != ship && o.Order is TradeOrder io && io.Sell == station && io.Good == good && o.Phase == TradePhase.GoingToSell)
                sum += o.Cargo.GetValueOrDefault(good);
        return sum;
    }

    /// <summary>
    /// Phase 19: Ein KI-Händler behält Fracht, die gerade keine erreichbare Station annimmt, höchstens <see cref="EconomyRules.StrandedCargoTicks"/> lang (er
    /// versucht sie bei jedem Nachdenken zu verkaufen und fährt mit dem übrigen Laderaum weiter) und wirft sie dann im All ab (wie Schürfer ihre unverkäuflichen
    /// Reste) — statt sie unbezahlt bei einer vollen Station abzuladen. Spielerschiffe behalten sie (die Person entscheidet, <see cref="Trading.CargoJettison"/>).
    /// </summary>
    static void DiscardUnsellable(Universe u, Ship ship)
    {
        if (!ship.Cargo.Values.Any(n => n > 0.0001f)) { ship.Cargo.Clear(); ship.StrandedSince = -1; return; }
        if (ship.IsPlayer) return;
        if (ship.StrandedSince < 0) { ship.StrandedSince = u.Tick; return; }
        if (u.Tick - ship.StrandedSince < u.Rules.StrandedCargoTicks) return;   // Frist: behalten und beim nächsten Nachdenken wieder versuchen
        ship.StrandedSince = -1;
        foreach (var (g, n) in ship.Cargo.OrderBy(kv => kv.Key.Value).ToList())
        {
            if (n > 0.0001f) u.Events.Publish(new CargoDiscarded(ship.Id, ship.Sector, g, n));
            ship.Cargo.Remove(g);
        }
    }

    /// <summary>Verkauf einer Teilmenge an die Station: sie zahlt, was ihre Kasse hergibt (Rest unbezahlt), Ereignisse und Buchungshaken.</summary>
    static void Deliver(Universe u, Ship ship, Station st, StockEntry stock, TradeOrder order, float amount)
    {
        // Phase 19: Menge über der Annahme der Station (Abladen als letzter Ausweg) als Messwert melden
        float over = amount - MathF.Max(0f, st.AcceptableUnits(stock, u.Rules));
        if (over > 0.0001f) u.Events.Publish(new CargoDumped(ship.Id, st.Id, order.Good, over));
        float paid = stock.Price > 0 ? MathF.Min(amount, MathF.Max(0f, st.Credits) / stock.Price) : 0f;
        float revenue = paid * stock.Price;
        stock.Amount += amount;
        st.Credits -= revenue;
        u.WalletOf(ship).Credits += revenue;
        ship.Cargo[order.Good] = MathF.Max(0f, ship.Cargo.GetValueOrDefault(order.Good) - amount);
        u.Events.Publish(new TradeCompleted(ship.Id, order.Buy, order.Sell, order.Good, paid, revenue));
        if (st.IsBuildSite) ConstructionRules.OnDelivered(u, st, ship, order.Good, amount, "trader");
        ExportRules.OnDelivered(u, st, order.Good, amount, revenue);   // Phase 14: Lieferung an eine Exportsenke wird eine Charge
    }
}
