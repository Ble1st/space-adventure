namespace SpaceSim.Core.Model;

/// <summary>
/// Gasernte und Nebel (SCHIFFSSLOTS.md „Gasernte und Nebel“): Stellschrauben als eigener Teil der Regeln (gespeichert wie alle Regeln). Code-Vorgabe =
/// altes Verhalten (keine Gassammler-Flotte, keine Nebelwirkung); die Standardwelt schaltet beides an.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Kapazitätsgrenze der KI-Gassammler (Aufgabe „gas“, 1 Einheit je Schiff). 0 = die Flotte kauft keine Gassammler nach.</summary>
    public float MaxCollectors;
    /// <summary>Schlüssel der Rohware, die Gassammler schöpfen.</summary>
    public string GasGoodKey = "gas";

    /// <summary>Hauptschalter der Nebelwirkungen (aus = ein Nebelsektor verhält sich wie jeder andere, nur die Darstellung bleibt).</summary>
    public bool NebulaEffects;
    /// <summary>Im Nebel: Zuschlag auf die Tarnstärke gegen jeden Sensor (Gate, Sprungspur, Kaperer, Polizei, Kleinstationen).</summary>
    public float NebulaCloakBonus = 1f;
    /// <summary>Im Nebel: Faktor auf die Überfallchance der Kaperer (sie finden Opfer schlechter).</summary>
    public float NebulaRaidFactor = 0.5f;
    /// <summary>Im Nebel: Faktor auf die Abfangchance von Streifen und Jägern gegen lauernde Kaperer.</summary>
    public float NebulaInterceptFactor = 0.5f;
    /// <summary>Im Nebel: Faktor auf die Schildregeneration der Schiffe (das Gas stört die Feldgeneratoren).</summary>
    public float NebulaShieldRegenFactor = 0.6f;
}
