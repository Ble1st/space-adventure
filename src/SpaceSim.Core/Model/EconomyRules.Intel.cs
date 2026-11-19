namespace SpaceSim.Core.Model;

/// <summary>
/// Ausbau Punkt 16: Erkundung und Intel (docs/ERKUNDUNG.md). Code aus = der Spieler kennt alles wie bisher; Standardwelt an — dann beginnen die drei
/// regulären Startpakete mit beschränktem Wissen (Testszenarien und Welten ohne Startpaket kennen weiterhin alles). Das Wissen liest nur die Spielerseite:
/// die NPC-Welt ist bitgleich.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Reguläre Startpakete beginnen mit beschränktem Wissen (<see cref="Intel.PlayerIntel"/>).</summary>
    public bool PlayerIntel;
    /// <summary>Sensorreichweite eigener Schiffe: Stationen, Tore und Feldmitten in diesem Abstand werden bekannt.</summary>
    public float IntelSensorRange = 450f;
    /// <summary>Intel-Preis je Sektor (Sektorkarte = Sektor + Tore).</summary>
    public float IntelSectorPrice = 250f;
    /// <summary>Intel-Preis je Station.</summary>
    public float IntelStationPrice = 60f;
    /// <summary>Intel-Preis je Rohstoff-/Gasfeld.</summary>
    public float IntelFieldPrice = 30f;
    /// <summary>Rabatt der Gebietskarte (alle Sektoren der Fraktion) auf die Summe der Einzelpreise.</summary>
    public float IntelTerritoryDiscount = 0.8f;
}
