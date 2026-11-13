using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Diagnostics;

public sealed record GoodStat(GoodId Good, string Name, float AvgPrice, float PriceRatio, float TotalStock);

/// <summary>
/// Momentaufnahme. Geldmenge nach Kassen (Phase 5b): <see cref="StationCredits"/> (alle Stationen), <see cref="ShipCredits"/>
/// (wirtschaftlich aktive KI-Schiffe, je Schiff eine Kasse), <see cref="PlayerCredits"/> (Spielerkasse plus etwaige Restcredits
/// auf Spielerschiffen, im Normalfall 0), Phase 9: <see cref="TreasuryCredits"/> (Kassen aller übrigen Akteure, v. a. der Fraktionen) und
/// <see cref="Actors"/> je Akteur. <see cref="TotalCredits"/> ist die Summe der vier.
/// </summary>
/// <remarks>Phase 8: <see cref="Stations"/> zählt fertige Stationen, <see cref="Sites"/> Baustellen (auch verlassene); Baustellen gehen nicht in die
/// Preisdurchschnitte ein (ihr Gebot ist fest, ihr Wunschbestand eine Stückliste), ihr Treuhandgeld steht in <see cref="StationCredits"/>.</remarks>
public sealed record EconomySnapshot(
    long Tick, int Stations, int Traders, int Miners, float StationCredits, float ShipCredits, IReadOnlyList<GoodStat> Goods,
    float PlayerCredits = 0f, int Sites = 0, int Builders = 0, float TreasuryCredits = 0f, IReadOnlyList<ActorStat>? Actors = null)
{
    /// <summary>Geldmenge: Stationen + Schiffe + Spieler + Kassen der übrigen Akteure (Phase 9).</summary>
    public float TotalCredits => StationCredits + ShipCredits + PlayerCredits + TreasuryCredits;
}

/// <summary>Phase 9: ein Akteur in der Momentaufnahme — Kasse, Besitz, Größenzahl und -klasse.</summary>
public sealed record ActorStat(ActorId Id, string Name, ActorKind Kind, float Treasury, int Ships, int Stations, int Sectors, float Size, ActorSizeClass Class, string Tag = "");

/// <summary>Momentaufnahme der Wirtschaft für Balancing-Werkzeug und Debug-Ansicht.</summary>
public static class EconomyStats
{
    public static EconomySnapshot Snapshot(Universe u)
    {
        var goods = new List<GoodStat>();
        foreach (var g in u.Goods.Values)
        {
            var entries = u.Stations.Values.Where(s => !s.IsBuildSite && s.Market.ContainsKey(g.Id)).Select(s => s.Market[g.Id]).ToList();
            float avg = entries.Count > 0 ? entries.Average(e => e.Price) : 0f;
            goods.Add(new GoodStat(g.Id, g.Name, avg, g.BasePrice > 0 ? avg / g.BasePrice : 0f, entries.Sum(e => e.Amount)));
        }
        return new EconomySnapshot(
            u.Tick, u.Stations.Values.Count(s => !s.IsBuildSite),
            u.Ships.Values.Count(s => s.Role == ShipRole.Trader), u.Ships.Values.Count(s => s.Role == ShipRole.Miner),
            u.Stations.Values.Sum(s => s.Credits),
            u.Ships.Values.Where(s => s.IsEconomyAi).Sum(s => s.Credits), goods,
            u.PlayerWallet.Credits + u.Ships.Values.Where(s => s.IsPlayer).Sum(s => s.Credits),
            u.Stations.Values.Count(s => s.IsBuildSite), u.Ships.Values.Count(s => s.IsAiBuilder),
            u.Actors.Values.Where(a => a.Id != u.PlayerActor).Sum(a => a.Treasury.Credits), Actors(u));
    }

    /// <summary>Phase 9: alle Akteure mit Kasse, Besitz und Größe (Id-Reihenfolge; die freien Einzelschiffe zählen bei der Sammelgruppe).</summary>
    public static IReadOnlyList<ActorStat> Actors(Universe u)
    {
        var index = ActorRules.Index(u);
        var list = new List<ActorStat>();
        foreach (var a in u.Actors.Values.OrderBy(a => a.Id.Value))
        {
            var h = index.TryGetValue(a.Id, out var x) ? x : default;
            if (a.Id == u.FreeActor) h = h with { Ships = u.Ships.Values.Count(s => s.Owner == a.Id) };
            float size = ActorRules.Size(u.Rules, h);
            // Die Sammelgruppe ist kein Unternehmen: jedes freie Schiff ist für sich ein Einzelakteur
            var cls = a.Id == u.FreeActor ? ActorSizeClass.Single : ActorRules.ClassOf(u.Rules, size);
            list.Add(new ActorStat(a.Id, a.Name, a.Kind, a.Treasury.Credits, h.Ships, h.Stations, h.Sectors, size, cls, a.Tag));
        }
        return list;
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

    /// <summary>Phase 14: Summe aller Fahrten einer Ware (Menge, Anzahl), gleich welche Route.</summary>
    public (float Volume, int Trips) ByGood(GoodId good)
    {
        float v = 0f; int n = 0;
        foreach (var (k, x) in _routes) if (k.Good == good) { v += x.volume; n += x.trips; }
        return (v, n);
    }

    public IReadOnlyList<RouteStat> Top(int n) =>
        _routes.OrderByDescending(kv => kv.Value.volume).Take(n)
               .Select(kv => new RouteStat(kv.Key, kv.Value.volume, kv.Value.trips)).ToList();
}
