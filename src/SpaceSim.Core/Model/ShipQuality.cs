namespace SpaceSim.Core.Model;

/// <summary>
/// Güteklasse eines Schiffs (Phase 7, SCHIFFSSLOTS.md Abschnitt 8): einmal beim Kauf gewürfelt (<see cref="Simulation.ShipQualityRoll"/>).
/// Standard = Fit des Modells; Verbessert = ein Modul eine Stufe besser; Elite = zwei bis drei Module besser oder ein Slot eine Größe größer;
/// Meisterwerk = zusätzlich ein Bonusslot. Gespeichert je Schiff; die Wirkung steckt in Slotliste und Fit.
/// </summary>
public enum ShipQuality { Standard, Improved, Elite, Masterwork }

public static class ShipQualityNames
{
    /// <summary>Deutscher Anzeigename.</summary>
    public static string Name(ShipQuality q) => q switch
    {
        ShipQuality.Improved => "Verbessert",
        ShipQuality.Elite => "Elite",
        ShipQuality.Masterwork => "Meisterwerk",
        _ => "Standard",
    };
}

/// <summary>Art eines Upgrades (Phase 7, Dienst „upgrade“): Modul eine Stufe besser, Slot eine Größe größer, Bonusslot anbauen.</summary>
public enum UpgradeKind { Module, Slot, Bonus }
