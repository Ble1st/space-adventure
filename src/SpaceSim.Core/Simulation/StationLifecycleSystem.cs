using SpaceSim.Core.Content;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Stationen entstehen bei Knappheit und verschwinden bei Pleite:
/// - Pleite: Credits dauerhaft unter dem Limit → Station wird aufgegeben (wesentliche Stationen nie).
/// - Bau: Ist eine Ware im Schnitt knapp (Bestand deutlich unter Wunschbestand), baut ein Investor (Station oder Schiff) eine
///   Produktionsstätte für sie; liegt sie dauerhaft weit über dem Wunschbestand, entsteht stattdessen ein Verbraucher dafür.
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

        // Knappste Ware (höchster Durchschnittspreis relativ zum Basispreis) und am stärksten überversorgte Ware (niedrigster)
        GoodId? scarce = null, glut = null;
        float worst = rules.BuildPriceRatio, lowest = rules.GlutPriceRatio;
        foreach (var good in u.Goods.Values)
        {
            // Knappheit aus den Beständen (Wunschbestand / Bestand über alle Stationen), nicht aus Preisen: Preise sind durch die
            // Zahlungsbereitschaft der Fabriken gedeckelt und würden Überangebot vortäuschen.
            var entries = u.Stations.Values.Where(s => s.Market.ContainsKey(good.Id)).Select(s => s.Market[good.Id]).ToList();
            if (entries.Count == 0) continue;
            float ratio = entries.Sum(e => e.Target) / MathF.Max(1f, entries.Sum(e => e.Amount));
            if (ratio > worst) { worst = ratio; scarce = good.Id; }
            if (ratio < lowest && ratio > 0f) { lowest = ratio; glut = good.Id; }
        }

        // Knappheit → Produzent der Ware bauen; sonst bei Überangebot → Verbraucher der Ware bauen (Nachfrage wächst mit billigem Angebot)
        StationType? type = null;
        if (scarce is { } sc)
            type = u.StationTypes.Values.Where(t => t.Recipes.Any(r => r.Outputs.ContainsKey(sc))).OrderBy(t => t.BuildCost).FirstOrDefault();
        else if (glut is { } gl)
            type = u.StationTypes.Values.Where(t => t.Recipes.Any(r => r.Inputs.ContainsKey(gl))).OrderBy(t => t.BuildCost).FirstOrDefault();
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

        // Sektoren der Fraktion des Investors, dünn besiedelte zuerst; der erste mit freiem Platz bekommt die Station
        Sector? sector = null;
        Vec2? spot = null;
        foreach (var candidate in u.Sectors.Values
                     .Where(s => s.Owner == investorFaction && s.Stations.Count < rules.MaxStationsPerSector)
                     .OrderBy(s => s.Stations.Count).ThenBy(s => s.Id.Value))
        {
            spot = FindFreeSpot(u, candidate);
            if (spot != null) { sector = candidate; break; }
        }
        if (sector is null || spot is null) return;

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
        for (int attempt = 0; attempt < 400; attempt++)
        {
            var p = new Vec2(u.Rng.Range(-150, 150), u.Rng.Range(-150, 150));
            bool ok =
                sector.Gates.All(g => Vec2.Distance(p, u.Gates[g].Position) > 50f) &&
                sector.Stations.All(s => Vec2.Distance(p, u.Stations[s].Position) > 50f) &&
                sector.Fields.All(f => Vec2.Distance(p, f.Position) > 45f) &&
                sector.Asteroids.All(a => Vec2.Distance(p, a.Position) > a.Radius + Station.BodyRadius + 6f);
            if (ok) return p;
        }
        return null;
    }
}
