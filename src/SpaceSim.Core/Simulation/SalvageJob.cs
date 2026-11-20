using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 4 (NPC-Bergung): KI-Bergeschiffe holen Fracht und Module (Restzustand ≥ <see cref="EconomyRules.SalvageMinCondition"/>) aus Wracks
/// (<see cref="Wrecks.TakeCargo"/>/<see cref="Wrecks.TakeModule"/>) und verkaufen sie (<see cref="SalvageSystem"/>). Eigene Aufgabe statt eines Zweigs
/// im <see cref="MinerSystem"/>: Bergen braucht weder Asteroiden noch Abbauwerkzeug, hat eine eigene Grenze (<see cref="EconomyRules.MaxSalvagers"/>,
/// 1 Einheit je Schiff, nur mit <see cref="EconomyRules.NpcSalvage"/>) und nimmt den Schürfern keine Plätze weg. Kaufwert wie beim Handel
/// (Laderaum × Tempo); Bewertung = Wert der Beute des besten Wracks je Reisezeit.
/// </summary>
public sealed class SalvageJob : IShipJob
{
    public const string JobKey = "salvage";
    public string Key => JobKey;
    public ShipRole Role => ShipRole.Salvager;
    public Capability Needs => Capability.Salvage;
    /// <summary>0 mit ausgeschaltetem Hauptschalter — dann kauft die Flotte keine Bergeschiffe nach.</summary>
    public float Limit(EconomyRules rules) => rules.NpcSalvage ? rules.MaxSalvagers : 0f;
    public float Units(ShipStats stats, float cargoCapacity, EconomyRules rules) => 1f;
    /// <summary>Laderaum-Einheiten × Tempo-Faktor wie beim Handel; ohne Bergungsgreifer kein Wert.</summary>
    public float FleetValue(ShipStats stats, EconomyRules rules)
    {
        if ((stats.Capabilities & Capability.Salvage) == 0) return 0f;
        return stats.Cargo / MathF.Max(1f, rules.TradeCapacityUnit) * MathF.Min(1.5f, stats.Speed / Travel.ShipSpeed);
    }
    /// <summary>Bewertung des besten Wracks: Basiswert der bergbaren Beute je Tick Reisezeit (reine Auswertung, <see cref="SalvageSystem.Best"/>).</summary>
    public float Score(Universe u, Ship ship) => SalvageSystem.Best(u, ship).Score;
}
