using SpaceSim.Core.Content;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Stationen entstehen bei Knappheit und verschwinden bei Pleite:
/// - Pleite: Credits dauerhaft unter dem Limit → Station wird aufgegeben (wesentliche Stationen nie).
/// - Bau: Ist eine Ware im Schnitt deutlich teurer als ihr Basispreis, baut die reichste Station einer Fraktion
///   eine Produktionsstätte für diese Ware in einem freien Sektor der Fraktion.
/// </summary>
public sealed class StationLifecycleSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        RemoveBankrupt(u);
        TryBuild(u);
    }

    static void RemoveBankrupt(Universe u)
    {
        foreach (var st in u.Stations.Values.Where(s => !s.Essential).ToList())
        {
            if (st.Credits >= u.Rules.BankruptcyLimit) { st.BelowLimitSince = -1; continue; }
            if (st.BelowLimitSince < 0) st.BelowLimitSince = u.Tick;
            if (u.Tick - st.BelowLimitSince >= u.Rules.BankruptcyTicks) u.RemoveStation(st.Id, "bankrupt");
        }
    }

    static void TryBuild(Universe u)
    {
        var rules = u.Rules;
        if (u.Stations.Count >= rules.MaxStations || u.Tick - u.LastStationBuildTick < rules.BuildCooldown) return;

        // Knappste Ware bestimmen (höchster Durchschnittspreis relativ zum Basispreis)
        GoodId? scarce = null;
        float worst = rules.BuildPriceRatio;
        foreach (var good in u.Goods.Values)
        {
            var prices = u.Stations.Values.Where(s => s.Market.ContainsKey(good.Id)).Select(s => s.Market[good.Id].Price).ToList();
            if (prices.Count == 0 || good.BasePrice <= 0) continue;
            float ratio = prices.Average() / good.BasePrice;
            if (ratio > worst) { worst = ratio; scarce = good.Id; }
        }
        if (scarce is null) return;

        var type = u.StationTypes.Values
            .Where(t => t.Recipes.Any(r => r.Outputs.ContainsKey(scarce.Value)))
            .OrderBy(t => t.BuildCost).FirstOrDefault();
        if (type is null) return;

        float cost = type.BuildCost * rules.StationBuildCostFactor;
        // Investor: reichster Teilnehmer (Station oder Händler-/Minenschiff), der Baukosten plus Reserve aufbringen kann
        float need = cost + rules.InvestorReserve;
        var stationInvestor = u.Stations.Values.Where(s => s.Credits >= need).OrderByDescending(s => s.Credits).FirstOrDefault();
        var shipInvestor = u.Ships.Values.Where(s => s.Role is ShipRole.Trader or ShipRole.Miner && s.Credits >= need)
                                         .OrderByDescending(s => s.Credits).FirstOrDefault();
        if (stationInvestor is null && shipInvestor is null) return;
        bool useShip = shipInvestor is not null && (stationInvestor is null || shipInvestor.Credits > stationInvestor.Credits);
        var investorFaction = useShip ? shipInvestor!.Faction : stationInvestor!.Owner;

        var sector = u.Sectors.Values
            .Where(s => s.Owner == investorFaction && s.Stations.Count < rules.MaxStationsPerSector)
            .OrderBy(s => s.Stations.Count).ThenBy(s => s.Id.Value).FirstOrDefault();
        if (sector is null) return;

        var spot = FindFreeSpot(u, sector);
        if (spot is null) return;

        var id = u.NewStationId();
        var station = StationFactory.Create(u, id, type, $"{type.Name} {sector.Name.Split(' ').Last()} {id.Value}", sector.Id, spot.Value, investorFaction);
        station.Credits = 2000f;
        if (useShip) shipInvestor!.Credits -= cost; else stationInvestor!.Credits -= cost;
        u.AddStation(station);
        u.LastStationBuildTick = u.Tick;
        u.NotifyStationBuilt(station);
    }

    /// <summary>Zufälliger Platz mit Abstand zu Gates, Stationen, Feldern und Asteroiden (deterministisch über u.Rng).</summary>
    public static Vec2? FindFreeSpot(Universe u, Sector sector)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var p = new Vec2(u.Rng.Range(-130, 130), u.Rng.Range(-130, 130));
            bool ok =
                sector.Gates.All(g => Vec2.Distance(p, u.Gates[g].Position) > 60f) &&
                sector.Stations.All(s => Vec2.Distance(p, u.Stations[s].Position) > 55f) &&
                sector.Fields.All(f => Vec2.Distance(p, f.Position) > 45f) &&
                sector.Asteroids.All(a => Vec2.Distance(p, a.Position) > a.Radius + Station.BodyRadius + 12f);
            if (ok) return p;
        }
        return null;
    }
}
