using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Eine Aufgabe, die die KI einem Schiff zuteilen kann (Phase 6, SCHIFFE.md Abschnitt 3 „Multirole“). Die Aufgabe ist Zustand
/// (<see cref="Ship.Role"/> bleibt ihr gespeichertes Etikett); ausgeführt wird sie weiter von ihrem System (Händler: <see cref="TraderSystem"/>,
/// Schürfer: <see cref="MinerSystem"/>). Neue Aufgaben (Bauen, Patrouille) kommen als weitere Implementierung in <see cref="ShipJobs.Default"/>,
/// ohne <see cref="JobAssignmentSystem"/> oder <see cref="FleetSystem"/> zu ändern.
/// </summary>
public interface IShipJob
{
    /// <summary>Schlüssel im Content (<c>shipModels[].jobs</c>), z. B. "trade".</summary>
    string Key { get; }
    /// <summary>Etikett der Aufgabe am Schiff (gespeichert, Anzeige, Filter des ausführenden Systems).</summary>
    ShipRole Role { get; }
    /// <summary>Fähigkeit, ohne die das Schiff die Aufgabe nicht ausführen kann.</summary>
    Capability Needs { get; }
    /// <summary>Kapazitätsgrenze der KI-Flotte für diese Aufgabe (in Einheiten, siehe <see cref="Units"/>).</summary>
    float Limit(EconomyRules rules);
    /// <summary>Kapazität, die ein Schiff mit diesen Werten in dieser Aufgabe belegt (Einheitsmodell = 1).</summary>
    float Units(ShipStats stats, float cargoCapacity, EconomyRules rules);
    /// <summary>Erwartete Leistung eines Modells in dieser Aufgabe für die Kaufwahl (Kapazität × Tempo-Faktor; Einheitsmodell ≈ 1).</summary>
    float FleetValue(ShipStats stats, EconomyRules rules);
    /// <summary>Erwarteter Gewinn je Tick, wenn das Schiff jetzt diese Aufgabe übernähme (vorhandenes Scoring des ausführenden Systems); 0 = nichts zu tun.</summary>
    float Score(Universe u, Ship ship);
    /// <summary>
    /// Runde 9: Anteil der Leistung, den die Wirtschaft gerade abnimmt (0..1; Kaufwahl: Wert × Anteil). Standard 1. Handel: ein Laderaum nur für eine Form
    /// (Tanker, Feststoff-Kurier) zählt mit dem Anteil seiner Form am offenen Bedarf.
    /// </summary>
    float DemandFactor(Universe u, ShipStats stats) => 1f;
}

/// <summary>Handel: Fracht zwischen Stationen (<see cref="TraderSystem"/>). Kapazität = Frachtraum ÷ <see cref="EconomyRules.TradeCapacityUnit"/>.</summary>
public sealed class TradeJob : IShipJob
{
    public const string JobKey = "trade";
    public string Key => JobKey;
    public ShipRole Role => ShipRole.Trader;
    public Capability Needs => Capability.Cargo;
    public float Limit(EconomyRules rules) => rules.MaxTraders;
    public float Units(ShipStats stats, float cargoCapacity, EconomyRules rules) => cargoCapacity / MathF.Max(1f, rules.TradeCapacityUnit);
    /// <summary>Frachtraum-Einheiten × Tempo relativ zu <see cref="Travel.ShipSpeed"/>: ein langsamer Frachter liefert weniger je Zeit.</summary>
    public float FleetValue(ShipStats stats, EconomyRules rules) =>
        stats.Cargo / MathF.Max(1f, rules.TradeCapacityUnit) * MathF.Min(1.5f, stats.Speed / Travel.ShipSpeed);
    public float Score(Universe u, Ship ship) => TraderSystem.BestTrade(u, ship).Score;

    /// <summary>
    /// Runde 9 (Tanker, Kurier): wie viel des Laderaums die Wirtschaft gerade füllen kann. Ein Universalraum fährt jede Ware (Faktor 1, Modelle nur mit
    /// Universalräumen bleiben unverändert); ein Feststoff- bzw. Flüssigraum zählt nur bis zum noch nicht bedienten Bedarf seiner Form
    /// (<see cref="Unserved"/>): ein Tanker lohnt, wo Flüssigwaren gerade Kapazität brauchen, und sonst nicht.
    /// </summary>
    public float DemandFactor(Universe u, ShipStats stats)
    {
        if (stats.Cargo <= 0f || stats.CargoSolid + stats.CargoLiquid <= 0f) return 1f;
        var (solid, liquid) = Unserved(u);
        float s = MathF.Min(stats.CargoSolid, solid), l = MathF.Min(stats.CargoLiquid, liquid);
        float any = MathF.Min(stats.CargoAny, MathF.Max(0f, solid - s) + MathF.Max(0f, liquid - l));
        return Math.Clamp((s + l + any) / stats.Cargo, 0f, 1f);
    }

    /// <summary>
    /// Runde 9: noch nicht bedienter Bedarf je Form (Volumen): offener Bedarf (<see cref="ProductionDemand.OpenDemand(Universe, float[])"/>: was Abnehmer, Regale
    /// und Senken bis zu ihrem Ziel aufnehmen) abzüglich dessen, was Händler schon dorthin fahren (Ladung auf dem Weg zum Verkauf, Kapazität auf dem Weg zum
    /// Kauf). Rein, feste Reihenfolge.
    /// </summary>
    public static (float Solid, float Liquid) Unserved(Universe u)
    {
        var demand = ProductionDemand.OpenDemand(u, (float[]?)null);
        foreach (var o in u.ShipsById())
        {
            if (o.Order is not TradeOrder to || (uint)to.Good.Value >= (uint)demand.Length || !u.Goods.TryGetValue(to.Good, out var tg)) continue;
            float amount = o.Phase == TradePhase.GoingToSell ? o.Cargo.GetValueOrDefault(to.Good) : o.CapacityVolumeFor(u, to.Good) / tg.VolumePerUnit;
            demand[to.Good.Value] = MathF.Max(0f, demand[to.Good.Value] - amount);
        }
        float solid = 0f, liquid = 0f;
        for (int i = 0; i < demand.Length; i++)
        {
            if (demand[i] <= 0f || !u.Goods.TryGetValue(new GoodId(i), out var g)) continue;
            float vol = demand[i] * g.VolumePerUnit;
            if (g.Form == GoodForm.Liquid) liquid += vol; else solid += vol;
        }
        return (solid, liquid);
    }
}

/// <summary>Schürfen: Asteroiden abbauen und Erz verkaufen (<see cref="MinerSystem"/>). Kapazität = Laserstufe (Chance × Brockengröße; Mk1 = 1).</summary>
public sealed class MineJob : IShipJob
{
    public const string JobKey = "mine";
    /// <summary>Feststoff-Laderaum, ab dem ein Schürfer als voll ausgestattet gilt (kleinere Räume senken den Kaufwert).</summary>
    public const float ReferenceHold = 60f;
    public string Key => JobKey;
    public ShipRole Role => ShipRole.Miner;
    public Capability Needs => Capability.Mining;
    public float Limit(EconomyRules rules) => rules.MaxMiners;
    public float Units(ShipStats stats, float cargoCapacity, EconomyRules rules) => stats.LaserChunkChanceMult * stats.LaserChunkSizeMult;
    /// <summary>Laserstufe × Laderaumanteil (bis <see cref="ReferenceHold"/>) × (0,5 + 0,5 × Tempo-Faktor): Schürfen ist vor allem Laserzeit, die Reise zählt halb.</summary>
    public float FleetValue(ShipStats stats, EconomyRules rules)
    {
        if ((stats.Capabilities & Capability.Mining) == 0) return 0f;
        float hold = stats.CargoSolid + stats.CargoAny;
        return stats.LaserChunkChanceMult * stats.LaserChunkSizeMult * MathF.Min(1f, hold / ReferenceHold)
               * (0.5f + 0.5f * MathF.Min(1.5f, stats.Speed / Travel.ShipSpeed));
    }
    /// <summary>Bewertung des besten Abbauauftrags (Wert je Einheit je Tick) × Feststoff-Laderaum = Credits je Tick.</summary>
    public float Score(Universe u, Ship ship) => MinerSystem.BestMine(u, ship).Score * ship.CapacityVolumeFor(GoodForm.Solid);
}

/// <summary>
/// Phase 8: Bauen — Bauschiffe beliefern und montieren Baustellen (<see cref="ConstructionSystem"/>). Kapazität = 1 je Schiff
/// (Grenze <see cref="EconomyRules.MaxBuilders"/>); Kaufwert wie beim Handel (Material wird gefahren); Bewertung = Montagegebühr der nächsten
/// freien Baustelle je Tick Anreise (0 = keine Baustelle offen).
/// </summary>
public sealed class BuildJob : IShipJob
{
    public const string JobKey = "build";
    public string Key => JobKey;
    public ShipRole Role => ShipRole.Builder;
    public Capability Needs => Capability.Construction;
    public float Limit(EconomyRules rules) => rules.MaxBuilders;
    public float Units(ShipStats stats, float cargoCapacity, EconomyRules rules) => 1f;
    public float FleetValue(ShipStats stats, EconomyRules rules) =>
        MathF.Max(0.25f, stats.Cargo / MathF.Max(1f, rules.TradeCapacityUnit)) * MathF.Min(1.5f, stats.Speed / Travel.ShipSpeed);
    public float Score(Universe u, Ship ship)
    {
        if (ConstructionSystem.OpenSite(u, ship) is not { } site) return 0f;
        float time = Travel.Estimate(u, ship.Sector, ship.Position, site.Sector, site.DockPoint(ship.Id), ship);
        return site.Site!.Budget * u.Rules.ConstructionFeeShare / MathF.Max(1f, time + u.Rules.ConstructionAssemblyTicks);
    }
}

/// <summary>Registry der KI-Aufgaben (feste Reihenfolge: Handel, Schürfen, Bauen) und gemeinsame Abfragen.</summary>
public static class ShipJobs
{
    /// <summary>Alle Aufgaben in fester Reihenfolge (Reihenfolge = Reihenfolge der Flottenerweiterung, wie vor Phase 6 erst Händler, dann Miner; Phase 8: dann Bauschiffe).</summary>
    public static readonly IReadOnlyList<IShipJob> Default = new IShipJob[] { new TradeJob(), new MineJob(), new BuildJob() };

    /// <summary>Aufgabe zum Content-Schlüssel (null = unbekannt).</summary>
    public static IShipJob? ByKey(string key) => Default.FirstOrDefault(j => j.Key == key);

    /// <summary>Aufgabe zum Etikett (null = Rolle ohne KI-Aufgabe, z. B. Player, Fighter).</summary>
    public static IShipJob? ForRole(ShipRole role) => Default.FirstOrDefault(j => j.Role == role);

    /// <summary>Aufgaben eines Modells in Content-Reihenfolge (unbekannte Schlüssel fallen weg).</summary>
    public static IReadOnlyList<IShipJob> Of(ShipModel model)
    {
        var list = new List<IShipJob>(model.Jobs.Count);
        foreach (var key in model.Jobs)
            if (ByKey(key) is { } job && !list.Contains(job)) list.Add(job);
        return list;
    }

    /// <summary>Aufgaben, die dieses Schiff jetzt übernehmen darf: die seines Modells, die es mit seinem Fit auch kann.</summary>
    public static IReadOnlyList<IShipJob> Of(Universe u, Ship ship)
    {
        if (!ship.HasModel || !u.ShipModels.TryGetValue(ship.ModelKey, out var model)) return Array.Empty<IShipJob>();
        return Of(model).Where(j => ship.Has(j.Needs)).ToList();
    }

    /// <summary>Multirole-KI-Schiff: von der KI gesteuert und mindestens zwei ausführbare Aufgaben.</summary>
    public static bool IsMultirole(Universe u, Ship ship) => ship.Controller == Controller.Ai && Of(u, ship).Count >= 2;

    /// <summary>Kapazität eines Schiffs in einer Aufgabe (nach seinen Werten).</summary>
    public static float UnitsOf(Universe u, Ship ship, IShipJob job) => job.Units(ship.Stats, ship.CargoCapacity, u.Rules);

    /// <summary>
    /// Belegte Kapazität einer Aufgabe: Summe der Einheiten aller KI-Schiffe mit diesem Etikett (Phase 6: statt Stückzahl). Einheitsmodelle zählen
    /// je 1, ein Behemoth (500 Fracht) wie fünf Einheitsfrachter, ein Multirole-Schiff nur in der Aufgabe, die es gerade ausführt.
    /// </summary>
    public static float UsedUnits(Universe u, IShipJob job)
    {
        float sum = 0f;
        foreach (var s in u.ShipsById())
            if (s.Role == job.Role && s.Controller == Controller.Ai) sum += UnitsOf(u, s, job);
        return sum;
    }
}
