namespace SpaceSim.Core.Model;

/// <summary>
/// Sprungantrieb und Tarnfeld (SCHIFFSSLOTS.md Abschnitte 4 und 6): Stellschrauben als eigener Teil der Regeln (gespeichert wie alle Regeln).
/// Die Modulwerte (Reichweite, Ladezeit, Abklingzeit, Leistung, Tarnstärke) stehen in den Modulen, hier nur die Weltregeln.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Hauptschalter Sprungantrieb (aus = kein Sprung möglich, Module bleiben Ware).</summary>
    public bool JumpDrives = true;
    /// <summary>Hauptschalter Tarnfeld (aus = Tarnung lässt sich nicht einschalten).</summary>
    public bool Cloaking = true;

    /// <summary>Massenschatten: kein Sprung, solange das Schiff näher als dieser Abstand an der Hülle einer Station ist.</summary>
    public float JumpShadowStation = 60f;
    /// <summary>Massenschatten: kein Sprung näher als dieser Abstand an einem Jumpgate.</summary>
    public float JumpShadowGate = 60f;
    /// <summary>Massenschatten: Felsen ab diesem Radius werfen einen Schatten …</summary>
    public float JumpShadowRockRadius = 6f;
    /// <summary>… bis zu diesem Abstand von ihrer Oberfläche.</summary>
    public float JumpShadowRock = 25f;
    /// <summary>Während des Aufladens darf sich das Schiff höchstens so weit vom Ladepunkt entfernen (sonst bricht das Laden ab).</summary>
    public float JumpMoveTolerance = 4f;
    /// <summary>Ankunft: Ring um die Sektormitte mit diesem Anteil der Sektorausdehnung (Sektorrand, frei vom Massenschatten).</summary>
    public float JumpArrivalRing = 0.7f;
    /// <summary>Sensorstärke an der Sprung-Ankunft (Sprungspur): stärker getarnte Schiffe springen unbemerkt in gesperrte Sektoren.</summary>
    public float JumpSensorStrength = 2f;

    /// <summary>Sensorstärke der Kaperer: getarnte Schiffe darüber sind keine Überfallopfer.</summary>
    public float RaiderSensorStrength = 1f;
    /// <summary>Sensorstärke der Polizei (Festnahme, Kriegsangriff auf den Spieler): darüber kündigt niemand etwas an.</summary>
    public float PoliceSensorStrength = 2f;
    /// <summary>Sensorstärke der Kleinstationen (Plattformen, Satelliten): darüber greifen sie von sich aus nicht an.</summary>
    public float StationSensorStrength = 2f;
}
