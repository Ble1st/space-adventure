using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Trading;

public sealed record SaleResult(float Revenue, IReadOnlyDictionary<GoodId, float> Sold, float Unsold);

public enum BuyOutcome { Ok, NotSold, OutOfStock, NoCredits, NoSpace }

/// <summary>Ergebnis eines Einkaufs: tatsächlich gekaufte Einheiten und Kosten; Outcome nennt den begrenzenden Grund, wenn weniger als gewünscht gekauft wurde.</summary>
public sealed record BuyResult(float Units, float Cost, BuyOutcome Outcome);

/// <summary>Verkauf der Fracht eines Schiffs an eine Station (Spieler und Automatik). Gleiche Regeln wie für NPC-Händler.</summary>
public static class TradeActions
{
    /// <summary>Verkauft alle Waren, die die Station führt, zum aktuellen Preis. Die Station zahlt nur, was sie hat; der Rest bleibt an Bord.</summary>
    public static SaleResult SellAll(Universe u, Ship ship, Station station)
    {
        float revenue = 0f;
        var sold = new Dictionary<GoodId, float>();
        foreach (var good in ship.Cargo.Keys.ToList())
        {
            float have = ship.Cargo[good];
            if (have <= 0f || !station.Market.TryGetValue(good, out var entry) || entry.Price <= 0f) continue;

            float units = MathF.Min(MathF.Min(have, MathF.Max(0f, station.Credits) / entry.Price), station.AcceptableUnits(good, u.Rules));
            if (units <= 0f) continue;
            float pay = units * entry.Price;
            entry.Amount += units;
            station.Credits -= pay;
            ship.Credits += pay;
            if (have - units <= 0.0001f) ship.Cargo.Remove(good); else ship.Cargo[good] = have - units;
            revenue += pay;
            sold[good] = units;
            if (ship.IsPlayer) u.Events.Publish(new PlayerSold(ship.Id, station.Id, good, units, pay));
            else u.Events.Publish(new MineDelivered(ship.Id, station.Id, good, units, pay));
        }
        return new SaleResult(revenue, sold, ship.Cargo.Values.Sum());
    }

    /// <summary>Kauft bis zu <paramref name="units"/> Einheiten einer Ware zum Stationspreis. Begrenzt durch Lagerbestand, Credits und Laderaum; die Station behält nichts zurück, was sie nicht liefern kann.</summary>
    public static BuyResult Buy(Universe u, Ship ship, Station station, GoodId good, float units)
    {
        if (units <= 0f || !station.Market.TryGetValue(good, out var e) || e.Price <= 0f) return new BuyResult(0f, 0f, BuyOutcome.NotSold);
        float stock = MathF.Floor(e.Amount), afford = MathF.Floor(ship.Credits / e.Price), space = MathF.Floor(ship.FreeUnits(u, good));
        float n = MathF.Min(MathF.Min(MathF.Floor(units), stock), MathF.Min(afford, space));
        if (n <= 0f)
            return new BuyResult(0f, 0f, stock <= 0f ? BuyOutcome.OutOfStock : afford <= 0f ? BuyOutcome.NoCredits : BuyOutcome.NoSpace);
        float cost = n * e.Price;
        e.Amount -= n;
        station.Credits += cost;
        ship.Credits -= cost;
        ship.Cargo[good] = ship.Cargo.GetValueOrDefault(good) + n;
        if (ship.IsPlayer) u.Events.Publish(new PlayerBought(ship.Id, station.Id, good, n, cost));
        var outcome = n < MathF.Floor(units) ? (n == stock ? BuyOutcome.OutOfStock : n == afford ? BuyOutcome.NoCredits : BuyOutcome.NoSpace) : BuyOutcome.Ok;
        return new BuyResult(n, cost, outcome);
    }

    /// <summary>Höchstmenge, die <see cref="Buy"/> für diese Ware jetzt liefern würde.</summary>
    public static float MaxBuyable(Universe u, Ship ship, Station station, GoodId good) =>
        station.Market.TryGetValue(good, out var e) && e.Price > 0f
            ? MathF.Max(0f, MathF.Min(MathF.Min(MathF.Floor(e.Amount), MathF.Floor(ship.Credits / e.Price)), MathF.Floor(ship.FreeUnits(u, good))))
            : 0f;

    /// <summary>Verkauft bis zu <paramref name="units"/> Einheiten einer einzelnen Ware (gleiche Regeln wie <see cref="SellAll"/>).</summary>
    public static SaleResult Sell(Universe u, Ship ship, Station station, GoodId good, float units)
    {
        float have = ship.Cargo.GetValueOrDefault(good);
        var sold = new Dictionary<GoodId, float>();
        if (have <= 0f || units <= 0f || !station.Market.TryGetValue(good, out var e) || e.Price <= 0f)
            return new SaleResult(0f, sold, ship.Cargo.Values.Sum());
        float n = MathF.Min(MathF.Min(units, have), MathF.Min(MathF.Max(0f, station.Credits) / e.Price, station.AcceptableUnits(good, u.Rules)));
        if (n <= 0f) return new SaleResult(0f, sold, ship.Cargo.Values.Sum());
        float pay = n * e.Price;
        e.Amount += n;
        station.Credits -= pay;
        ship.Credits += pay;
        if (have - n <= 0.0001f) ship.Cargo.Remove(good); else ship.Cargo[good] = have - n;
        sold[good] = n;
        if (ship.IsPlayer) u.Events.Publish(new PlayerSold(ship.Id, station.Id, good, n, pay));
        return new SaleResult(pay, sold, ship.Cargo.Values.Sum());
    }

    /// <summary>Erwarteter Erlös der aktuellen Fracht an dieser Station (ohne zu verkaufen).</summary>
    public static float Quote(Universe u, Ship ship, Station station)
    {
        float total = 0f;
        foreach (var (good, have) in ship.Cargo)
            if (station.Market.TryGetValue(good, out var e) && e.Price > 0f)
                total += MathF.Min(MathF.Min(have, MathF.Max(0f, station.Credits) / e.Price), station.AcceptableUnits(good, u.Rules)) * e.Price;
        return total;
    }

    /// <summary>Beste Verkaufsstation für die Fracht: Erlös pro Reisezeit, nur erreichbare Stationen.</summary>
    public static Station? BestBuyer(Universe u, Ship ship, SectorId fromSector, Vec2 fromPos, StationId? exclude = null)
    {
        Station? best = null;
        float bestScore = 0f;
        foreach (var st in u.Stations.Values)
        {
            if (st.Id == exclude) continue;
            float quote = Quote(u, ship, st);
            if (quote <= 0f) continue;
            float time = Travel.Estimate(u, fromSector, fromPos, st.Sector, st.DockPoint(ship.Id), ship);
            if (float.IsPositiveInfinity(time)) continue;
            float score = quote / (time + 10f);
            if (score > bestScore) { bestScore = score; best = st; }
        }
        return best;
    }
}
