using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Nebelwirkungen (SCHIFFSSLOTS.md „Gasernte und Nebel“): ein Nebel bedeckt den ganzen Sektor (<see cref="Sector.Nebula"/>) und schwächt dort die Sensoren —
/// Tarnung wirkt stärker gegen jeden Sensor, Kaperer finden Opfer seltener, Streifen fangen lauernde Kaperer seltener ab — und bremst die
/// Schildregeneration. Kein Tempoabzug. Alles hinter dem Schalter <see cref="EconomyRules.NebulaEffects"/> (aus = Faktor 1, Zuschlag 0). Reine Abfragen.
/// </summary>
public static class Nebula
{
    /// <summary>Wirkt im Sektor ein Nebel (Schalter an und Sektor im Nebel)?</summary>
    public static bool In(Universe u, SectorId sector) =>
        u.Rules.NebulaEffects && u.Sectors.TryGetValue(sector, out var s) && s.Nebula;

    /// <summary>Zuschlag auf die Tarnstärke im Sektor (0 außerhalb).</summary>
    public static float CloakBonus(Universe u, SectorId sector) => In(u, sector) ? u.Rules.NebulaCloakBonus : 0f;

    /// <summary>Faktor auf die Überfallchance im Sektor (1 außerhalb).</summary>
    public static float RaidFactor(Universe u, SectorId sector) => In(u, sector) ? u.Rules.NebulaRaidFactor : 1f;

    /// <summary>Faktor auf die Abfangchance im Sektor (1 außerhalb).</summary>
    public static float InterceptFactor(Universe u, SectorId sector) => In(u, sector) ? u.Rules.NebulaInterceptFactor : 1f;

    /// <summary>Faktor auf die Schildregeneration im Sektor (1 außerhalb).</summary>
    public static float ShieldRegenFactor(Universe u, SectorId sector) => In(u, sector) ? u.Rules.NebulaShieldRegenFactor : 1f;
}
