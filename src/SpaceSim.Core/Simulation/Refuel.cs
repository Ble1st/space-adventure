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

    /// <summary>Bucht den Verbrauch einer geflogenen Strecke aus dem Tank (nie negativ; aktives Bord-Treibstoff wird zuerst geleert).</summary>
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
        foreach (var st in u.Stations.Values.Where(s => s.Sector == ship.Sector).OrderBy(s => s.Id.Value))
            if (Vec2.Distance(st.DockPoint(ship.Id), ship.Position) < 1f) return st;
        return null;
    }

    /// <summary>Stationen, die die Ware mit Bestand und Preis führen, fest nach Id geordnet.</summary>
    public static List<Station> FuelStations(Universe u, GoodId good) =>
        u.Stations.Values
         .Where(s => s.Market.TryGetValue(good, out var e) && e.Amount > 0f && e.Price > 0f)
         .OrderBy(s => s.Id.Value).ToList();

    /// <summary>
    /// Nächste Tankstelle für das Schiff (kürzeste geschätzte Reisezeit, dann Id), deren Anflug höchstens
    /// <paramref name="maxFuel"/> Treibstoff kostet; ohne Treffer null.
    /// </summary>
    public static Station? NearestFuelStation(Universe u, Ship ship, GoodId good, float maxFuel, StationId? exclude = null)
    {
        Station? best = null;
        float bestTime = float.PositiveInfinity;
        foreach (var st in FuelStations(u, good))
        {
            if (st.Id == exclude) continue;
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
    /// begrenzt durch Stationsbestand, Schiffscredits und freien Tankplatz (ganze Einheiten). Bucht Bestand, Stationskasse
    /// und Schiffscredits und veröffentlicht <see cref="FuelBought"/>. Liefert die gekauften Einheiten.
    /// </summary>
    public static float BuyFuel(Universe u, Ship ship, Station st, GoodId good, float wantUnits)
    {
        if (wantUnits <= 0f || !st.Market.TryGetValue(good, out var e) || e.Price <= 0f) return 0f;
        float stock = MathF.Floor(e.Amount), afford = MathF.Floor(ship.Credits / e.Price), space = MathF.Floor(ship.FuelSpace);
        float n = MathF.Min(MathF.Min(MathF.Floor(wantUnits), stock), MathF.Min(afford, space));
        if (n <= 0f) return 0f;
        float cost = n * e.Price;
        e.Amount -= n;
        st.Credits += cost;
        ship.Credits -= cost;
        ship.Fuel[good] = ship.FuelAmount(good) + n;
        u.Events.Publish(new FuelBought(ship.Id, st.Id, good, n, cost));
        return n;
    }

    /// <summary>Tankt an dieser Station so viel auf wie möglich (bis voll). Liefert die gekauften Einheiten.</summary>
    public static float TankUp(Universe u, Ship ship, Station st)
    {
        var good = ActiveFuel(ship);
        return good is null ? 0f : BuyFuel(u, ship, st, good.Value, ship.FuelSpace);
    }

    /// <summary>
    /// Notkauf bei leerem Tank: Treibstoff zum Aufpreis (<see cref="EmergencyPriceFactor"/> × Basispreis) ohne Station —
    /// der Erlös verschwindet (Geldsenke). Kauft höchstens <paramref name="wantUnits"/>, begrenzt durch Credits und Tankplatz,
    /// und veröffentlicht <see cref="EmergencyRefuel"/>. Liefert die gekauften Einheiten.
    /// </summary>
    public static float EmergencyBuy(Universe u, Ship ship, GoodId good, float wantUnits)
    {
        if (wantUnits <= 0f || ship.FuelSpace <= 0f) return 0f;
        float price = u.Goods[good].BasePrice * EmergencyPriceFactor;
        // aufrunden: so viel, dass die anvisierte Tankstelle mit Sicherheit erreichbar ist (ganze Einheiten bezahlbar/Tankplatz)
        float n = MathF.Min(MathF.Min(MathF.Ceiling(wantUnits), MathF.Floor(ship.Credits / price)), MathF.Floor(ship.FuelSpace));
        if (n <= 0f) return 0f;
        float cost = n * price;
        ship.Credits -= cost;   // Geldsenke: keine Station erhält den Erlös
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
        foreach (var st in u.Stations.Values.OrderBy(s => s.Id.Value))
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
        if (fuel is null || ship.Stats.FuelUsePerUnit <= 0f) return 0f;
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
        var good = ActiveFuel(ship);
        if (good is null || ship.Stats.FuelUsePerUnit <= 0f) return 0f;
        float d = distanceToBuy
                + Travel.EstimateDistance(u, buy.Sector, buy.DockPoint(ship.Id), sell.Sector, sell.DockPoint(ship.Id), ship);
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
        if (good is null) return TripPlan.Depart;   // kein Antrieb/Tank (z. B. Schiff ohne Modell): kein Verbrauch
        float dist = Travel.PlannedDistance(u, ship.Sector, ship.Position, toSector, toPos, ship);
        // Der Tank muss für die Reise reichen UND die Reserve halten (sonst tankt das Schiff jetzt, solange es noch hinkommt)
        float need = MathF.Max(Consumption(ship, dist, good), MinFuelReserveFraction * ship.Stats.FuelCapacity);
        if (ship.FuelAmount(good.Value) >= need) return TripPlan.Depart;

        // 1) an der aktuellen Station tanken, wenn sie den Treibstoff verkauft
        var here = CurrentStation(u, ship);
        if (here is not null && here.Market.TryGetValue(good.Value, out var he) && he.Amount > 0f && he.Price > 0f)
        {
            TankUp(u, ship, here);
            if (ship.FuelAmount(good.Value) >= need) return TripPlan.Depart;
        }

        // 2) nächste Tankstelle mit dem Resttank erreichbar? Dann erst dorthin, der eigentliche Auftrag wird danach neu gewählt
        var stop = NearestFuelStation(u, ship, good.Value, ship.FuelAmount(good.Value), exclude: here?.Id);
        if (stop is not null)
        {
            ship.Order = new GoToOrder(stop.Id);
            Travel.Start(u, ship, stop.Sector, stop.DockPoint(ship.Id));
            return TripPlan.FuelStop;
        }

        // 3) Notkauf: so viel Treibstoff, dass die nächste Tankstelle erreichbar ist (Aufpreis, Geldsenke);
        //    danach wird diese Tankstelle angesteuert (dort volltanken), erst dann geht es weiter
        var any = NearestFuelStation(u, ship, good.Value, float.PositiveInfinity);
        if (any is null)
        {
            // Nirgends Treibstoff im erreichbaren Raum (z. B. alle Raffinerien leer oder Welt ohne Raffinerie): Notkauf direkt
            // für diese Reise, ohne Reserve. So bleibt die Flotte beweglich; liegen bleibt nur, wer den Aufpreis nicht zahlen kann.
            float trip = Consumption(ship, dist, good);
            float lack = trip - ship.FuelAmount(good.Value);
            if (lack > 0f) EmergencyBuy(u, ship, good.Value, lack);
            return ship.FuelAmount(good.Value) >= trip ? TripPlan.Depart : TripPlan.Stuck;
        }
        float needToStop = Consumption(ship, Travel.EstimateDistance(u, ship.Sector, ship.Position, any.Sector, any.DockPoint(ship.Id), ship), good);
        float missing = MathF.Max(0f, needToStop - ship.FuelAmount(good.Value));
        if (missing > 0f) EmergencyBuy(u, ship, good.Value, missing);
        stop = NearestFuelStation(u, ship, good.Value, ship.FuelAmount(good.Value), exclude: here?.Id);
        if (stop is not null)
        {
            ship.Order = new GoToOrder(stop.Id);
            Travel.Start(u, ship, stop.Sector, stop.DockPoint(ship.Id));
            return TripPlan.FuelStop;
        }
        // Notkauf hat nicht für die Tankstelle gereicht: direkt weiter, wenn der Tank für die Reise reicht
        return ship.FuelAmount(good.Value) >= need ? TripPlan.Depart : TripPlan.Stuck;
    }

    /// <summary>
    /// Notruf des Spielers (Phase 4c): nur bei leerem Tank und nicht angedockt. Kauft zum doppelten Basispreis
    /// (<see cref="EmergencyPriceFactor"/>, Geldsenke wie <see cref="EmergencyBuy"/>) genau so viel Treibstoff, dass die
    /// nächste Tankstelle (<see cref="NearestFuelStation"/>, Luftlinie im Sektor, sonst über Gates —
    /// <see cref="Travel.EstimateDistance"/>) erreichbar ist, gedeckelt durch die Credits: zu wenig Geld heißt Meldung
    /// und kein Kauf. Ruft die Simulations-KI nicht auf; <see cref="EmergencyBuy"/> veröffentlicht <see cref="EmergencyRefuel"/>.
    /// </summary>
    public static EmergencyResult PlayerEmergency(Universe u, Ship player)
    {
        var good = ActiveFuel(player);
        if (good is null) return new(false, 0f, 0f, "Kein Treibstoffantrieb an Bord – kein Notruf nötig.");
        if (player.TotalFuel > 0.0001f) return new(false, 0f, 0f, "Tank ist nicht leer – kein Notruf nötig.");
        if (CurrentStation(u, player) is not null) return new(false, 0f, 0f, "Bereits angedockt – dort direkt tanken.");
        var stop = NearestFuelStation(u, player, good.Value, float.PositiveInfinity);
        if (stop is null) return new(false, 0f, 0f, "Keine Tankstelle im erreichbaren Raum – Notruf zwecklos.");
        float dist = Travel.EstimateDistance(u, player.Sector, player.Position, stop.Sector, stop.DockPoint(player.Id), player);
        // Der Spieler fliegt von Hand (Umwege, Bremsen): Aufschlag auf die Luftlinie, mindestens ein paar Einheiten
        float need = MathF.Max(Consumption(player, dist, good) * PlayerEmergencyMargin, 5f);
        float price = u.Goods[good.Value].BasePrice * EmergencyPriceFactor;
        float n = MathF.Min(MathF.Ceiling(need), MathF.Floor(player.FuelSpace));
        if (n <= 0f || MathF.Floor(player.Credits / price) < n)
            return new(false, 0f, 0f,
                $"Notruf nicht möglich: {n:0} {u.Goods[good.Value].Name} kosten {n * price:0} Cr (2× Basispreis) – zu wenig Credits.");
        float bought = EmergencyBuy(u, player, good.Value, n);
        if (bought <= 0f) return new(false, 0f, 0f, "Notruf fehlgeschlagen.");
        float cost = bought * price;
        return new(true, bought, -cost,
            $"Notruf: {bought:0} {u.Goods[good.Value].Name} für {cost:0} Cr – Tankstelle „{stop.Name}“ in Reichweite.");
    }
}
