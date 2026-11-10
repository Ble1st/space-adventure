using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Diagnostics;

public sealed record GoodStat(GoodId Good, string Name, float AvgPrice, float PriceRatio, float TotalStock);

/// <summary>
/// Momentaufnahme. Geldmenge nach Kassen (Phase 5b): <see cref="StationCredits"/> (alle Stationen), <see cref="ShipCredits"/>
/// (wirtschaftlich aktive KI-Schiffe, je Schiff eine Kasse), <see cref="PlayerCredits"/> (Spielerkasse plus etwaige Restcredits
/// auf Spielerschiffen, im Normalfall 0). <see cref="TotalCredits"/> ist die Summe der drei.
/// </summary>
public sealed record EconomySnapshot(
    long Tick, int Stations, int Traders, int Miners, float StationCredits, float ShipCredits, IReadOnlyList<GoodStat> Goods,
    float PlayerCredits = 0f)
{
    public float TotalCredits => StationCredits + ShipCredits + PlayerCredits;
}

/// <summary>Momentaufnahme der Wirtschaft für Balancing-Werkzeug und Debug-Ansicht.</summary>
public static class EconomyStats
{
    public static EconomySnapshot Snapshot(Universe u)
    {
        var goods = new List<GoodStat>();
        foreach (var g in u.Goods.Values)
        {
            var entries = u.Stations.Values.Where(s => s.Market.ContainsKey(g.Id)).Select(s => s.Market[g.Id]).ToList();
            float avg = entries.Count > 0 ? entries.Average(e => e.Price) : 0f;
            goods.Add(new GoodStat(g.Id, g.Name, avg, g.BasePrice > 0 ? avg / g.BasePrice : 0f, entries.Sum(e => e.Amount)));
        }
        return new EconomySnapshot(
            u.Tick, u.Stations.Count,
            u.Ships.Values.Count(s => s.Role == ShipRole.Trader), u.Ships.Values.Count(s => s.Role == ShipRole.Miner),
            u.Stations.Values.Sum(s => s.Credits),
            u.Ships.Values.Where(s => s.IsEconomyAi).Sum(s => s.Credits), goods,
            u.PlayerWallet.Credits + u.Ships.Values.Where(s => s.IsPlayer).Sum(s => s.Credits));
    }
}

public readonly record struct RouteKey(StationId From, StationId To, GoodId Good);
public sealed record RouteStat(RouteKey Route, float Volume, int Trips);

/// <summary>Sammelt abgeschlossene Handelsfahrten über den Ereignisbus. Ephemer (nicht im Spielstand).</summary>
public sealed class RouteRecorder
{
    readonly Dictionary<RouteKey, (float volume, int trips)> _routes = new();

    public RouteRecorder(Universe u) => u.Events.Raised += OnEvent;

    void OnEvent(SimEvent e)
    {
        RouteKey key;
        float amount;
        switch (e)
        {
            case TradeCompleted t: key = new RouteKey(t.From, t.To, t.Good); amount = t.Amount; break;
            case MineDelivered m: key = new RouteKey(new StationId(-m.Ship.Value), m.To, m.Good); amount = m.Amount; break;
            default: return;
        }
        _routes.TryGetValue(key, out var cur);
        _routes[key] = (cur.volume + amount, cur.trips + 1);
    }

    public IReadOnlyList<RouteStat> Top(int n) =>
        _routes.OrderByDescending(kv => kv.Value.volume).Take(n)
               .Select(kv => new RouteStat(kv.Key, kv.Value.volume, kv.Value.trips)).ToList();
}
