namespace SpaceSim.Core.Model;

/// <summary>
/// Ausbau Punkt 4 — NPC-Bergung (docs/KAMPF.md „NPC-Bergung“): KI-Bergeschiffe (Aufgabe „salvage“) holen Fracht und Module aus Wracks und
/// verkaufen sie. Code-Vorgabe = aus (keine Bergeschiffe, Grenze 0); die Standardwelt schaltet beides an. Aus = die Welt verhält sich wie vorher
/// (keine Bergeschiffe in der Flotte, <see cref="Simulation.SalvageSystem"/> tut nichts).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Kapazitätsgrenze der KI-Bergeschiffe (Aufgabe „salvage“, 1 Einheit je Schiff). 0 = die Flotte kauft keine Bergeschiffe nach.</summary>
    public float MaxSalvagers;
    /// <summary>Hauptschalter der NPC-Bergung: aus = <see cref="Simulation.SalvageSystem"/> läuft nicht und die Flottengrenze der Aufgabe ist 0.</summary>
    public bool NpcSalvage;
}
