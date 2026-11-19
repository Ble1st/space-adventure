namespace SpaceSim.Core.Model;

/// <summary>
/// Fähigkeiten eines Schiffs, unabhängig von seiner aktuellen Aufgabe (<see cref="ShipRole"/> bleibt das Etikett).
/// Bis es Schiffsmodelle gibt, werden die Fähigkeiten aus der Rolle abgeleitet (<see cref="CapabilityDefaults"/>).
/// </summary>
[Flags]
public enum Capability
{
    None = 0,
    /// <summary>Frachtraum: Handeln und Ausliefern.</summary>
    Cargo = 1,
    /// <summary>Werkzeug: Abbau von Asteroiden.</summary>
    Mining = 2,
    /// <summary>Waffen: Kampf.</summary>
    Combat = 4,
    /// <summary>Bauausleger: Stationen bauen.</summary>
    Construction = 8,
    /// <summary>Traktor/Bergung: Wracks und verlorene Fracht bergen.</summary>
    Salvage = 16,
    /// <summary>Sprungantrieb: Sprung in einen Sektor ohne Gate (zurückgestellt, noch kein Modul nutzt sie).</summary>
    Jump = 32,
    /// <summary>Tarnfeld: verringerte Entdeckung an Kontrollpunkten (zurückgestellt, noch kein Modul nutzt sie).</summary>
    Stealth = 64,
    /// <summary>Gassammler: Nebelgas in Kanister schöpfen (Aufgabe „gas“).</summary>
    GasHarvest = 128,
}

/// <summary>Wer ein Schiff steuert: der Spieler selbst oder die KI.</summary>
public enum Controller { Ai, Player }

/// <summary>
/// Standard-Ableitung von Fähigkeiten und Steuerer aus der Rolle. Reine Funktion; läuft überall, wo Schiffe entstehen
/// (<c>WorldBuilder.NewShip</c>, <c>Universe.AddShip</c>, <c>UniverseSerializer</c>, <c>FleetSystem.Expand</c>),
/// damit kein Schiff mit <see cref="Capability.None"/> entsteht. Wird nicht gespeichert.
/// </summary>
public static class CapabilityDefaults
{
    /// <summary>Standardfähigkeiten je Rolle: Trader handeln, Miner handeln und schürfen, Fighter kämpfen, der Spieler handelt und schürft.</summary>
    public static Capability ForRole(ShipRole role) => role switch
    {
        ShipRole.Trader => Capability.Cargo,
        ShipRole.Miner => Capability.Cargo | Capability.Mining,
        ShipRole.Fighter => Capability.Combat,
        ShipRole.Player => Capability.Cargo | Capability.Mining,
        ShipRole.Builder => Capability.Cargo | Capability.Construction,
        ShipRole.Police or ShipRole.Pirate => Capability.Combat,   // Phase 11
        ShipRole.Collector => Capability.Cargo | Capability.GasHarvest,   // Gasernte
        _ => Capability.None,
    };

    /// <summary>Nur die Spielerrolle wird vom Spieler gesteuert, alle anderen von der KI.</summary>
    public static Controller ControllerFor(ShipRole role) => role == ShipRole.Player ? Controller.Player : Controller.Ai;
}
