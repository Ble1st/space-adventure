using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Welle B (2b): KI-Reaktion auf eine Stationsflut des Spielers. Ein Sektor einer KI-Fraktion gilt als vom Spieler umkämpft, wenn der Einfluss der
/// Spielerfraktion dort über <see cref="EconomyRules.ContestedShare"/> liegt. Dann (deterministisch, ohne Zufall):
/// Sperre gegen den Spieler mindestens <see cref="EconomyRules.PlayerContestLockLevel"/> (<see cref="FactionPolicySystem.Desired"/>),
/// je <see cref="EconomyRules.PlayerContestReactTicks"/> ein Gegenbau aus der freien Kasse (<see cref="FactionViability.Spendable"/>) und bei
/// Beziehung unter <see cref="EconomyRules.FeudRelation"/> eine Fehde (vorhandene Fehden-Regel, hier ohne Firmensitz des Spielers).
/// </summary>
public static class PlayerContest
{
    /// <summary>Gehört der Sektor einer KI-Fraktion und liegt der Einfluss der Spielerfraktion dort über dem Umkämpft-Anteil? Rein.</summary>
    public static bool ContestedByPlayer(Universe u, Sector sector) =>
        u.Rules.PlayerContestReaction && u.PlayerFaction is { } me && u.IsNpcFaction(sector.Owner)
        && sector.Influence.TryGetValue(me, out var share) && share > u.Rules.ContestedShare;

    /// <summary>Welcher Typ als nächstes gegengebaut würde (null = keiner mehr frei). Rein.</summary>
    public static StationType? CounterType(Universe u, Sector sector, FactionId f)
    {
        foreach (var key in u.Rules.PlayerContestCounterTypes)
        {
            if (!u.StationTypes.TryGetValue(key, out var t)) continue;
            int own = 0;
            foreach (var id in sector.Stations)
                if (u.Stations.TryGetValue(id, out var st) && !st.IsBuildSite && st.TypeKey == key && u.FactionOf(st) == f) own++;
            if (own < Math.Max(0, u.Rules.PlayerContestCounterMax)) return t;
        }
        return null;
    }

    /// <summary>Ein Reaktionsschritt für alle umkämpften Sektoren (Id-Reihenfolge). Liefert die Zahl der Gegenbauten.</summary>
    public static int React(Universe u)
    {
        if (!u.Rules.PlayerContestReaction || u.PlayerFaction is not { } me) return 0;
        int built = 0;
        foreach (var sector in u.Sectors.Values.OrderBy(s => s.Id.Value).ToList())
        {
            if (!ContestedByPlayer(u, sector)) continue;
            var f = sector.Owner;
            // Fehde: vorhandene Regel (Beziehung unter FeudRelation, kein Krieg/Bündnis/laufende Fehde), hier ohne Firmensitz des Spielers — vor dem
            // Gegenbau, der den Sektor schon wieder unter die Umkämpft-Grenze drücken kann
            if (u.Rules.FeudsActive && u.Rules.PlayerFeuds && Feuds.StartIssue(u, f, me, playerContest: true) is null) Feuds.Start(u, f, me);
            if (Counter(u, sector, f)) built++;
        }
        return built;
    }

    /// <summary>Baut eine fertige Station des nächsten Gegenbau-Typs, wenn die freie Kasse reicht und ein Platz frei ist.</summary>
    static bool Counter(Universe u, Sector sector, FactionId f)
    {
        if (CounterType(u, sector, f) is not { } t) return false;
        if (!u.Actors.TryGetValue(new ActorId(f.Value), out var a)) return false;
        float cost = t.BuildCost * u.Rules.StationBuildCostFactor;
        if (FactionViability.Spendable(u, a) < cost) return false;
        if (StationLifecycleSystem.FindFreeSpot(u, sector, t.Radius) is not { } spot) return false;
        a.Treasury.Credits -= cost;
        u.Events.Publish(new TreasuryUpkeep(a.Id, "contest_build", cost, 0f));
        var id = u.NewStationId();
        var st = StationFactory.Create(u, id, t, StationFactory.BuiltName(u, t, sector.Id), sector.Id, spot, a.Id);
        st.Credits = 0f;
        u.AddStation(st);
        u.Events.Publish(new StationBuilt(id, sector.Id, t.Key));
        u.Events.Publish(new PlayerContestCountered(f, sector.Id, id, t.Key, cost,
            $"{a.Name} baut gegen deine Stationen in {sector.Name}: {t.Name}."));
        if (SectorControl.Update(u, sector)) u.TouchAccess();
        return true;
    }
}

/// <summary>Welle B (2b): reagiert im Politik-Modul auf vom Spieler umkämpfte Sektoren (Takt <see cref="EconomyRules.PlayerContestReactTicks"/>, zustandslos).</summary>
public sealed class PlayerContestSystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.PlayerContestReaction || u.PlayerFaction is null) return;
        long period = Math.Max(1, u.Rules.PlayerContestReactTicks);
        // nur im Schritt, der eine Periodengrenze überschreitet (zustandslos, Speichern/Laden bitgleich)
        if (u.Tick / period == (u.Tick - dt) / period) return;
        PlayerContest.React(u);
    }
}
