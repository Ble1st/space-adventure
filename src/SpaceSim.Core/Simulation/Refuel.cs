using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Treibstoff-Logistik der KI-Schiffe (Phase 4b). Verbrauch = Strecke innerhalb der Sektoren × fuelUse / efficiency
/// des Treibstoffs (Gate-Sprünge sind frei; siehe <see cref="Travel"/>). Vor einer Reise sichert <see cref="PrepareTrip"/>
/// den Treibstoff: Tanken an der aktuellen Station zum Marktpreis, sonst Fahrt zur nächsten erreichbaren Tankstelle
/// (<see cref="GoToOrder"/> plus Tanken bei Ankunft), sonst Notkauf zum Aufpreis. Reine Funktionen über den Spielstand,
/// keine eigenen Zustände; alle Reihenfolgen sind fest (Stationen nach Id), damit der Lauf deterministisch bleibt.
/// </summary>
public static class Refuel
{
    /// <summary>Aufpreisfaktor des Notkaufs auf den Basispreis der Ware.</summary>
    public const float EmergencyPriceFactor = 2f;

    /// <summary>
    /// Reserve, die ein KI-Schiff im Tank halten will (Anteil der Tankkapazität). Fällt der Tank darunter, wird vor einer
    /// Reise getankt bzw. eine Tankstelle angesteuert — so verteilt sich die Treibstoffnachfrage über die Zeit, statt dass
    /// die ganze Flotte auf einmal leer fährt und dann keine Tankstelle mehr erreicht.
    /// </summary>
    public const float MinFuelReserveFraction = 0.75f;

    /// <summary>Unter diesem Tankanteil fährt ein Schiff eigens zur Tankstelle; darüber tankt es nur an Stationen, an denen es ohnehin steht.</summary>
    public const float DetourFuelFraction = 0.25f;

    /// <summary>Ein Umweg zur Tankstelle lohnt nur, wenn das Tankbudget dort mindestens so viele Einheiten kauft.</summary>
    public const float MinWorthwhileStopUnits = 10f;

    /// <summary>Aufschlag des Spieler-Notrufs auf den Luftlinien-Bedarf bis zur nächsten Tankstelle.</summary>
    public const float PlayerEmergencyMargin = 1.5f;

    /// <summary>Ergebnis des Spieler-Notrufs (<see cref="PlayerEmergency"/>): Success, gekaufte Einheiten, Credit-Veränderung (negativ = Zahlung) und Meldung für die Oberfläche.</summary>
    public sealed record EmergencyResult(bool Success, float Units, float Credits, string Message);

    /// <summary>Ergebnis von <see cref="PrepareTrip"/>.</summary>
    public enum TripPlan
    {
        /// <summary>Tank reicht (ggf. nach Tanken an der aktuellen Station); der Aufrufer startet die Reise selbst.</summary>
        Depart,
        /// <summary>Tankstelle wurde angefahren: Order = <see cref="GoToOrder"/>, Transit läuft bereits; der Aufrufer darf nicht selbst starten.</summary>
        FuelStop,
        /// <summary>Treibstoff nicht sicherstellbar (z. B. pleite); der Aufrufer darf nicht reisen, das Schiff bleibt liegen.</summary>
        Stuck,
    }

    /// <summary>
    /// Der Treibstoff, den das Schiff nutzt: die am Bord befindliche Ware mit der höchsten efficiency (bei Gleichstand die erste
    /// in der Fit-Reihenfolge), sonst die erste zulässige Ware; null ohne Antrieb/Tank (dann ist der Verbrauch 0).
    /// </summary>
    public static GoodId? ActiveFuel(Ship? ship)
    {
        if (ship is null || ship.Stats.FuelTypes.Count == 0) return null;
        GoodId? best = null;
        float bestEff = 0f;
        foreach (var f in ship.Stats.FuelTypes)
        {
            if (ship.FuelAmount(f.Good) <= 0f) continue;
            if (best is null || f.Efficiency > bestEff) { best = f.Good; bestEff = f.Efficiency; }
        }
        return best ?? ship.Stats.FuelTypes[0].Good;
    }

    /// <summary>Verbrauch für eine Strecke in Einheiten des Treibstoffs (Strecke × fuelUse / efficiency); 0 ohne Antrieb oder Strecke.</summary>
    public static float Consumption(Ship? ship, float distance, GoodId? good = null)
    {
        if (ship is null || ship.Stats.FuelUsePerUnit <= 0f || distance <= 0f) return 0f;
        good ??= ActiveFuel(ship);
        if (good is null) return 0f;
        float eff = 0f;
        foreach (var f in ship.Stats.FuelTypes)
            if (f.Good == good.Value) { eff = f.Efficiency; break; }
        if (eff <= 0f) eff = 1f;
        return distance * ship.Stats.FuelUsePerUnit / eff;
    }

    /// <summary>Phase 14: Treibstoff an (<see cref="EconomyRules.FuelActive"/>)? Aus = kein Verbrauch, kein Tanken, keine Treibstoffkosten.</summary>
    public static bool Active(Universe u) => u.Rules.FuelActive;

    /// <summary>Bucht den Verbrauch einer geflogenen Strecke aus dem Tank, nur wenn Treibstoff an ist (Phase 14).</summary>
    public static void Consume(Universe u, Ship ship, float distance)
    {
        if (Active(u)) Consume(ship, distance);
    }

    /// <summary>Bucht den Verbrauch einer geflogenen Strecke aus dem Tank (nie negativ; aktives Bord-Treibstoff wird zuerst geleert). Ohne Schalterprüfung.</summary>
    public static void Consume(Ship ship, float distance)
    {
        var good = ActiveFuel(ship);
        if (good is null) return;
        float have = ship.FuelAmount(good.Value);
        float take = MathF.Min(have, Consumption(ship, distance, good));
        if (take <= 0f) return;
        float left = have - take;
        if (left <= 0.0001f) ship.Fuel.Remove(good.Value); else ship.Fuel[good.Value] = left;
    }

    /// <summary>Die Station, an der das Schiff gerade angedockt ist (Position = ihr Dockpunkt), sonst null.</summary>
    public static Station? CurrentStation(Universe u, Ship ship)
    {
        foreach (var st in u.StationsById())
            if (st.Sector == ship.Sector && Vec2.Distance(st.DockPoint(ship.Id), ship.Position) < 1f) return st;
        return null;
    }

    /// <summary>Stationen, die die Ware mit Bestand und Preis führen, fest nach Id geordnet.</summary>
    public static List<Station> FuelStations(Universe u, GoodId good)
    {
        var list = new List<Station>();
        foreach (var s in u.StationsById()) if (SellsFuel(s, good)) list.Add(s);
        return list;
    }

    /// <summary>Punkt 13: Bedingung von <see cref="FuelStations"/> (Bestand und Preis &gt; 0).</summary>
    static bool SellsFuel(Station s, GoodId good) => s.Market.TryGetValue(good, out var e) && e.Amount > 0f && e.Price > 0f;

    /// <summary>
    /// Nächste Tankstelle für das Schiff (kürzeste geschätzte Reisezeit, dann Id), deren Anflug höchstens
    /// <paramref name="maxFuel"/> Treibstoff kostet; ohne Treffer null.
    /// </summary>
    public static Station? NearestFuelStation(Universe u, Ship ship, GoodId good, float maxFuel, StationId? exclude = null)
    {
        Station? best = null;
        float bestTime = float.PositiveInfinity;
        // Punkt 13: dieselbe Reihenfolge wie FuelStations (nach Id), ohne Liste und Sortierung je Aufruf
        foreach (var st in u.StationsById())
        {
            if (!SellsFuel(st, good)) continue;
            if (st.Id == exclude || !Docking.Allows(u, st, ship)) continue;   // Phase 30
            float time = Travel.Estimate(u, ship.Sector, ship.Position, st.Sector, st.DockPoint(ship.Id), ship);
            if (float.IsPositiveInfinity(time) || time >= bestTime) continue;
            float need = Consumption(ship, Travel.EstimateDistance(u, ship.Sector, ship.Position, st.Sector, st.DockPoint(ship.Id), ship), good);
            if (need > maxFuel) continue;
            best = st; bestTime = time;
        }
        return best;
    }

    /// <summary>
    /// Kauft Treibstoff an einer Station zum Marktpreis in den Tank (nicht in den Laderaum). Wie <see cref="Trading.TradeActions.Buy"/>:
    /// begrenzt durch Stationsbestand, Credits der Kasse (<see cref="Universe.WalletOf(Ship)"/>) und freien Tankplatz (ganze Einheiten).
    /// Bucht Bestand, Stationskasse und Kasse des Schiffs und veröffentlicht <see cref="FuelBought"/>. Liefert die gekauften Einheiten.
    /// </summary>
    public static float BuyFuel(Universe u, Ship ship, Station st, GoodId good, float wantUnits, float debt = 0f)
    {
        if (!Active(u) || wantUnits <= 0f || !st.Market.TryGetValue(good, out var e) || e.Price <= 0f) return 0f;
        var wallet = u.WalletOf(ship);
        // Welle F (2): Pflichttreibstoff des Spielers darf die Kasse bis zur Schuldgrenze ins Minus ziehen (debt > 0 nur von TankUp)
        float stock = MathF.Floor(e.Amount), afford = MathF.Floor(MathF.Max(0f, wallet.Credits + debt) / e.Price), space = MathF.Floor(ship.FuelSpace);
        float n = MathF.Min(MathF.Min(MathF.Floor(wantUnits), stock), MathF.Min(afford, space));
        if (n <= 0f) return 0f;
        float cost = n * e.Price;
        e.Amount -= n;
        st.Credits += cost;
        wallet.Credits -= cost;
        ship.Fuel[good] = ship.FuelAmount(good) + n;
        u.Events.Publish(new FuelBought(ship.Id, st.Id, good, n, cost));
        return n;
    }

    /// <summary>
    /// Anteil der Schiffscredits, den ein KI-Schiff bei einem Tankvorgang höchstens für Vorrat ausgibt. Ohne diese Grenze
    /// floss das gesamte Betriebskapital in den Tank: das Schiff konnte keine Ware mehr kaufen und blieb pleite liegen.
    /// </summary>
    public const float FuelBudgetShare = 0.3f;

    /// <summary>
    /// Tankt an dieser Station: mindestens <paramref name="minUnits"/> (Bedarf der anstehenden Reise), darüber hinaus Vorrat
    /// bis voll, soweit <see cref="FuelBudgetShare"/> der Credits reicht. Liefert die gekauften Einheiten.
    /// </summary>
    public static float TankUp(Universe u, Ship ship, Station st, float minUnits = 0f)
    {
        var good = ActiveFuel(ship);
        if (!Active(u) || good is null || !st.Market.TryGetValue(good.Value, out var e) || e.Price <= 0f) return 0f;
        float budget = MathF.Floor(FuelBudgetShare * MathF.Max(0f, u.WalletOf(ship).Credits) / e.Price);
        // Welle D (1): eigene Schiffe tanken mindestens bis zur Reserve — sonst kommen sie mit kleiner Spielerkasse leer an und kaufen doppelt teuer nach
        if (u.Rules.PlayerTankReserve && ship.Owner == u.PlayerActor && u.PlayerActor.Value != 0)
            minUnits = MathF.Max(minUnits, MinFuelReserveFraction * ship.Stats.FuelCapacity - ship.TotalFuel);
        float want = MathF.Min(ship.FuelSpace, MathF.Max(MathF.Ceiling(minUnits), budget));
        // Welle F (2): Spielerschiffe tanken das Pflichtmaß (nächste andere Tankstelle mit Aufschlag, Reserve) notfalls auf Schuld — erst das, dann den Vorrat
        if (FuelPriority.Applies(u, ship))
        {
            float must = MathF.Min(ship.FuelSpace, MathF.Ceiling(MathF.Max(minUnits, FuelPriority.MinimumAt(u, ship, st, good.Value))));
            float got = must > 0f ? BuyFuel(u, ship, st, good.Value, must, u.Rules.PlayerFuelDebtLimit) : 0f;
            return got + BuyFuel(u, ship, st, good.Value, want - got);
        }
        return BuyFuel(u, ship, st, good.Value, want);
    }

    /// <summary>
    /// Notkauf bei leerem Tank: Treibstoff zum Aufpreis (<see cref="EmergencyPriceFactor"/> × Basispreis) ohne Station —
    /// der Erlös verschwindet (Geldsenke). Kauft höchstens <paramref name="wantUnits"/>, begrenzt durch Credits und Tankplatz,
    /// und veröffentlicht <see cref="EmergencyRefuel"/>. Liefert die gekauften Einheiten.
    /// </summary>
    public static float EmergencyBuy(Universe u, Ship ship, GoodId good, float wantUnits)
    {
        if (!Active(u) || wantUnits <= 0f || ship.FuelSpace <= 0f) return 0f;
        float price = u.Goods[good].BasePrice * EmergencyPriceFactor;
        // aufrunden: so viel, dass die anvisierte Tankstelle mit Sicherheit erreichbar ist (ganze Einheiten bezahlbar/Tankplatz)
        var wallet = u.WalletOf(ship);
        float n = MathF.Min(MathF.Min(MathF.Ceiling(wantUnits), MathF.Floor(wallet.Credits / price)), MathF.Floor(ship.FuelSpace));
        // Welle F (2): Spielerschiffe kaufen ganz oder gar nicht (mit Schuldgrenze) — sonst rinnt die Kasse in Ein-Einheiten-Notkäufen weg
        if (FuelPriority.Applies(u, ship))
        {
            float want = MathF.Min(MathF.Ceiling(wantUnits), MathF.Floor(ship.FuelSpace));
            if (want * price > wallet.Credits + u.Rules.PlayerFuelDebtLimit) return 0f;
            n = want;
        }
        if (n <= 0f) return 0f;
        float cost = n * price;
        wallet.Credits -= cost;   // Geldsenke: keine Station erhält den Erlös
        ship.Fuel[good] = ship.FuelAmount(good) + n;
        u.Events.Publish(new EmergencyRefuel(ship.Id, good, n, cost));
        return n;
    }

    /// <summary>
    /// Treibstoffpreis für Bewertungen: an der aktuellen Station, wenn sie die Ware führt, sonst an der bevorzugten Station,
    /// sonst der Durchschnittspreis aller Stationen, die sie führen, sonst der Basispreis.
    /// </summary>
    public static float FuelPrice(Universe u, Ship ship, GoodId good, Station? preferred = null)
    {
        var here = CurrentStation(u, ship);
        if (here is not null && here.Market.TryGetValue(good, out var he) && he.Price > 0f) return he.Price;
        if (preferred is not null && preferred.Market.TryGetValue(good, out var pe) && pe.Price > 0f) return pe.Price;
        float sum = 0f;
        int n = 0;
        foreach (var st in u.StationsById())
            if (st.Market.TryGetValue(good, out var e) && e.Price > 0f) { sum += e.Price; n++; }
        return n > 0 ? sum / n : u.Goods[good].BasePrice;
    }

    /// <summary>
    /// Treibstoffkosten einer Handelsrunde (aktuelle Position → Kaufstation → Verkaufsstation) in Credits,
    /// bewertet zum Treibstoffpreis an der Kaufstation (bzw. aktuell/Durchschnitt, siehe <see cref="FuelPrice"/>).
    /// </summary>
    public static float TripFuelCost(Universe u, Ship ship, Station buy, Station sell)
    {
        var fuel = ActiveFuel(ship);
        if (!Active(u) || fuel is null || ship.Stats.FuelUsePerUnit <= 0f) return 0f;
        float toBuy = Travel.EstimateDistance(u, ship.Sector, ship.Position, buy.Sector, buy.DockPoint(ship.Id), ship);
        return TripFuelCost(u, ship, buy, sell, toBuy, FuelPrice(u, ship, fuel.Value, buy));
    }

    /// <summary>
    /// Wie <see cref="TripFuelCost(Universe, Ship, Station, Station)"/>, aber mit vorab berechneter Strecke zur Kaufstation und
    /// vorab ermitteltem Treibstoffpreis: für die Routenbewertung in <c>TraderSystem</c>, wo beides pro Schiff nur einmal anfallen darf
    /// (die Variante ohne Vorwerte sortiert bei jedem Aufruf alle Stationen und hat die Simulation um das Dreifache verlangsamt).
    /// </summary>
    public static float TripFuelCost(Universe u, Ship ship, Station buy, Station sell, float distanceToBuy, float fuelPrice)
    {
        if (!Active(u)) return 0f;   // Phase 17: Schalter zuerst (spart die Treibstoffwahl bei abgeschaltetem Treibstoff; gleiches Ergebnis)
        var good = ActiveFuel(ship);
        if (good is null || ship.Stats.FuelUsePerUnit <= 0f) return 0f;
        float d = distanceToBuy
                + Travel.EstimateDistance(u, buy.Sector, buy.DockPoint(ship.Id), sell.Sector, sell.DockPoint(ship.Id), GateAccess.ProfileOf(u, ship) with { Laden = true });
        return Consumption(ship, d, good) * fuelPrice;
    }

    /// <summary>
    /// Punkt 13 (Rechenzeit): <see cref="TripFuelCost(Universe, Ship, Station, Station, float, float)"/> mit der schon bekannten Strecke Kauf → Verkauf
    /// (beladenes Profil, <see cref="Travel.EstimateWithDistance"/>) — dieselbe Rechnung ohne die zweite Routen- und Streckenschätzung.
    /// </summary>
    internal static float TripFuelCostWithLeg(Universe u, Ship ship, float distanceToBuy, float buyToSell, float fuelPrice)
    {
        if (!Active(u)) return 0f;
        var good = ActiveFuel(ship);
        if (good is null || ship.Stats.FuelUsePerUnit <= 0f) return 0f;
        float d = distanceToBuy + buyToSell;
        return Consumption(ship, d, good) * fuelPrice;
    }

    /// <summary>
    /// Sichert den Treibstoff für eine geplante Reise; Aufruf direkt vor <see cref="Travel.Start"/>, während das Schiff geparkt ist.
    /// <see cref="TripPlan.Depart"/>: Tank reicht — der Aufrufer startet die Reise zum Ziel selbst.
    /// <see cref="TripPlan.FuelStop"/>: eine Tankstelle ist mit dem Resttank erreichbar — Order und Transit
    /// zur Tankstelle sind gesetzt, Tanken erfolgt bei Ankunft (GoToOrder). <see cref="TripPlan.Stuck"/>: nicht sicherstellbar
    /// (pleite oder keine Tankstelle) — das Schiff darf nicht reisen und bleibt liegen (Leerlauf → ggf. Aufgabe durch FleetSystem).
    /// </summary>
    public static TripPlan PrepareTrip(Universe u, Ship ship, SectorId toSector, Vec2 toPos)
    {
        var good = ActiveFuel(ship);
        if (good is null || !Active(u)) return TripPlan.Depart;   // kein Antrieb/Tank (z. B. Schiff ohne Modell) oder Treibstoff abgeschaltet (Phase 14): kein Verbrauch
        float dist = Travel.PlannedDistance(u, ship.Sector, ship.Position, toSector, toPos, ship);
        // Wunsch: Reise plus Reserve (das Schiff tankt, solange es noch hinkommt). Pflicht ist nur der Bedarf der Reise selbst.
        float trip = Consumption(ship, dist, good);
        float need = MathF.Max(trip, MinFuelReserveFraction * ship.Stats.FuelCapacity);
        if (ship.FuelAmount(good.Value) >= need) return TripPlan.Depart;

        // 1) an der aktuellen Station tanken, wenn sie den Treibstoff verkauft (Vorrat nur im Rahmen des Tankbudgets)
        var here = CurrentStation(u, ship);
        if (here is not null && here.Market.TryGetValue(good.Value, out var he) && he.Amount > 0f && he.Price > 0f)
            TankUp(u, ship, here, trip - ship.FuelAmount(good.Value));
        if (ship.FuelAmount(good.Value) >= need) return TripPlan.Depart;

        // 2) nächste Tankstelle mit dem Resttank erreichbar? Dann erst dorthin, der eigentliche Auftrag wird danach neu gewählt.
        //    Reicht der Tank für die Reise und das Schiff könnte sich ohnehin kaum Vorrat leisten, fährt es direkt.
        var stop = NearestFuelStation(u, ship, good.Value, ship.FuelAmount(good.Value), exclude: here?.Id);
        if (stop is not null && ship.FuelAmount(good.Value) >= trip
            && (ship.FuelAmount(good.Value) >= DetourFuelFraction * ship.Stats.FuelCapacity
                || FuelBudgetShare * u.WalletOf(ship).Credits < MinWorthwhileStopUnits * stop.Market[good.Value].Price))
            return TripPlan.Depart;   // Umweg erst unter der Umweg-Schwelle; darüber tankt das Schiff nur, wo es ohnehin steht
        if (stop is not null)
        {
            ship.Order = new GoToOrder(stop.Id);
            Travel.Start(u, ship, stop.Sector, stop.DockPoint(ship.Id));
            return TripPlan.FuelStop;
        }
        if (ship.FuelAmount(good.Value) >= trip) return TripPlan.Depart;   // keine Tankstelle in Reichweite, aber die Reise selbst geht

        // Welle D (1): ein eigener Versorger im Sektor hat die Sorte an Bord — warten statt doppelt teuer nachkaufen
        if (FleetSupply.WaitFor(u, ship, good.Value)) return TripPlan.Stuck;
        // 3) Notkauf: so viel Treibstoff, dass die nächste Tankstelle erreichbar ist (Aufpreis, Geldsenke);
        //    danach wird diese Tankstelle angesteuert (dort volltanken), erst dann geht es weiter
        var any = NearestFuelStation(u, ship, good.Value, float.PositiveInfinity);
        if (any is null)
        {
            // Nirgends Treibstoff im erreichbaren Raum (z. B. alle Raffinerien leer oder Welt ohne Raffinerie): Notkauf direkt
            // für diese Reise, ohne Reserve. So bleibt die Flotte beweglich; liegen bleibt nur, wer den Aufpreis nicht zahlen kann.
            float lack = trip - ship.FuelAmount(good.Value);
            if (lack > 0f && EmergencyBuy(u, ship, good.Value, lack) <= 0f && FuelPriority.Applies(u, ship))
                return FuelPriority.Strand(u, ship, good.Value, lack);
            return ship.FuelAmount(good.Value) >= trip ? TripPlan.Depart : TripPlan.Stuck;
        }
        float needToStop = Consumption(ship, Travel.EstimateDistance(u, ship.Sector, ship.Position, any.Sector, any.DockPoint(ship.Id), ship), good);
        // Welle F (2): Spielerschiffe rechnen mit der geplanten Strecke (Umwege) und Aufschlag, damit ein Notkauf bis zur Tankstelle reicht
        if (FuelPriority.Applies(u, ship)) needToStop = FuelPriority.WithMargin(Consumption(ship, Travel.PlannedDistance(u, ship.Sector, ship.Position, any.Sector, any.DockPoint(ship.Id), ship), good));
        float missing = MathF.Max(0f, needToStop - ship.FuelAmount(good.Value));
        if (missing > 0f && EmergencyBuy(u, ship, good.Value, missing) <= 0f && FuelPriority.Applies(u, ship))
            return FuelPriority.Strand(u, ship, good.Value, missing);
        stop = NearestFuelStation(u, ship, good.Value, ship.FuelAmount(good.Value), exclude: here?.Id);
        if (stop is not null)
        {
            ship.Order = new GoToOrder(stop.Id);
            Travel.Start(u, ship, stop.Sector, stop.DockPoint(ship.Id));
            return TripPlan.FuelStop;
        }
        // Notkauf hat nicht für die Tankstelle gereicht: direkt weiter, wenn der Tank für die Reise reicht
        return ship.FuelAmount(good.Value) >= trip ? TripPlan.Depart : TripPlan.Stuck;
    }

    /// <summary>
    /// Notruf des Spielers (Phase 4c): nur bei leerem Tank (ob das Schiff angedockt ist, prüft die Ansicht). Kauft zum doppelten Basispreis
    /// (<see cref="EmergencyPriceFactor"/>, Geldsenke wie <see cref="EmergencyBuy"/>) genau so viel Treibstoff, dass die
    /// nächste Tankstelle (<see cref="NearestFuelStation"/>, Luftlinie im Sektor, sonst über Gates —
    /// <see cref="Travel.EstimateDistance"/>) erreichbar ist, gedeckelt durch die Credits: zu wenig Geld heißt Meldung
    /// und kein Kauf. Ruft die Simulations-KI nicht auf; <see cref="EmergencyBuy"/> veröffentlicht <see cref="EmergencyRefuel"/>.
    /// </summary>
    public static EmergencyResult PlayerEmergency(Universe u, Ship player)
    {
        if (!Active(u)) return new(false, 0f, 0f, "Treibstoff ist abgeschaltet – kein Notruf nötig.");
        var good = ActiveFuel(player);
        if (good is null) return new(false, 0f, 0f, "Kein Treibstoffantrieb an Bord – kein Notruf nötig.");
        if (player.TotalFuel > 0.0001f) return new(false, 0f, 0f, "Tank ist nicht leer – kein Notruf nötig.");
        var stop = NearestFuelStation(u, player, good.Value, float.PositiveInfinity);
        if (stop is null) return new(false, 0f, 0f, "Keine Tankstelle im erreichbaren Raum – Notruf zwecklos.");
        float dist = Travel.EstimateDistance(u, player.Sector, player.Position, stop.Sector, stop.DockPoint(player.Id), player);
        // Der Spieler fliegt von Hand (Umwege, Bremsen): Aufschlag auf die Luftlinie, mindestens ein paar Einheiten
        float need = MathF.Max(Consumption(player, dist, good) * PlayerEmergencyMargin, 5f);
        float price = u.Goods[good.Value].BasePrice * EmergencyPriceFactor;
        float n = MathF.Min(MathF.Ceiling(need), MathF.Floor(player.FuelSpace));
        if (n <= 0f || MathF.Floor(u.WalletOf(player).Credits / price) < n)
            return new(false, 0f, 0f,
                $"Notruf nicht möglich: {n:0} {u.Goods[good.Value].Name} kosten {n * price:0} Cr (2× Basispreis) – zu wenig Credits.");
        float bought = EmergencyBuy(u, player, good.Value, n);
        if (bought <= 0f) return new(false, 0f, 0f, "Notruf fehlgeschlagen.");
        float cost = bought * price;
        return new(true, bought, -cost,
            $"Notruf: {bought:0} {u.Goods[good.Value].Name} für {cost:0} Cr – Tankstelle „{stop.Name}“ in Reichweite.");
    }
}
