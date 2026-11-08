using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Trading;

public sealed record SaleResult(float Revenue, IReadOnlyDictionary<GoodId, float> Sold, float Unsold);

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

            float units = MathF.Min(have, MathF.Max(0f, station.Credits) / entry.Price);
            if (units <= 0f) continue;
            float pay = units * entry.Price;
            entry.Amount += units;
            station.Credits -= pay;
            ship.Credits += pay;
            if (have - units <= 0.0001f) ship.Cargo.Remove(good); else ship.Cargo[good] = have - units;
            revenue += pay;
            sold[good] = units;
            u.Events.Publish(new PlayerSold(ship.Id, station.Id, good, units, pay));
        }
        return new SaleResult(revenue, sold, ship.Cargo.Values.Sum());
    }

    /// <summary>Erwarteter Erlös der aktuellen Fracht an dieser Station (ohne zu verkaufen).</summary>
    public static float Quote(Ship ship, Station station)
    {
        float total = 0f;
        foreach (var (good, have) in ship.Cargo)
            if (station.Market.TryGetValue(good, out var e) && e.Price > 0f)
                total += MathF.Min(have, MathF.Max(0f, station.Credits) / e.Price) * e.Price;
        return total;
    }

    /// <summary>Beste Verkaufsstation für die Fracht: Erlös pro Reisezeit, nur erreichbare Stationen.</summary>
    public static Station? BestBuyer(Universe u, Ship ship, SectorId fromSector, Vec2 fromPos)
    {
        Station? best = null;
        float bestScore = 0f;
        foreach (var st in u.Stations.Values)
        {
            float quote = Quote(ship, st);
            if (quote <= 0f) continue;
            float time = Travel.Estimate(u, fromSector, fromPos, st.Sector, st.DockPoint(ship.Id));
            if (float.IsPositiveInfinity(time)) continue;
            float score = quote / (time + 10f);
            if (score > bestScore) { bestScore = score; best = st; }
        }
        return best;
    }
}
