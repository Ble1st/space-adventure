namespace SpaceSim.Core.Model;

/// <summary>
/// Wirtschaftsstationen halten stand (docs/KAMPF.md „Wirtschaftsstationen halten stand“): Alarm ruft Verteidiger aus der Nähe, Notschild unter halber Hülle,
/// Punktabwehr je Größe. Code-Vorgabe = alte Regel (alles aus); die Standardwelt schaltet ein. Gespeichert wie alle Regeln (additiv, Spielstand bleibt v17).
/// Die Geschützzahl je Größe ist <see cref="EconomicStationGuns"/> (Standardwelt mehr als die Code-Vorgabe).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>
    /// Alarm: wird eine Wirtschaftsstation angegriffen, fliegen Polizei und Kriegsschiffe der Besitzerfraktion und ihrer Verbündeten aus dem Sektor und aus
    /// Sektoren bis <see cref="EconomicAlarmJumps"/> Sprüngen hin (über ihre normalen Befehle, kein Versetzen) und verteidigen sie.
    /// </summary>
    public bool EconomicStationAlarm;
    /// <summary>Reichweite des Alarms in Torsprüngen ab dem aktuellen Ort des Verteidigers.</summary>
    public int EconomicAlarmJumps = 2;
    /// <summary>Der Alarm hält so viele Ticks nach dem letzten Treffer an.</summary>
    public int EconomicAlarmTicks = 1200;
    /// <summary>Höchstens so viele Verteidiger je Fraktion folgen einem Alarm (die nächsten nach Sprüngen, dann Id; 0 = alle in Reichweite).</summary>
    public int EconomicAlarmMaxResponders = 4;

    /// <summary>Notschild: fällt die Hülle einer Wirtschaftsstation unter <see cref="EconomicEmergencyShieldHull"/>, sinkt der Hüllenschaden für eine Weile.</summary>
    public bool EconomicEmergencyShield;
    /// <summary>Auslöseschwelle (Anteil der Höchsthülle).</summary>
    public float EconomicEmergencyShieldHull = 0.5f;
    /// <summary>Anteil des Hüllenschadens, den der Notschild abhält (0..1).</summary>
    public float EconomicEmergencyShieldReduction = 0.75f;
    /// <summary>Dauer des Notschilds (Ticks).</summary>
    public int EconomicEmergencyShieldTicks = 240;
    /// <summary>Abklingzeit nach dem Notschild (Ticks), bis er wieder auslösen kann.</summary>
    public int EconomicEmergencyShieldCooldownTicks = 2400;

    /// <summary>
    /// Geschützmischung der Vorgabeverteidigung: die ersten n Einträge (n = <see cref="EconomicStationGuns"/> der Größe) — leichte Laser allein richten gegen
    /// gepanzerte Kriegsschiffe (Zerstörer Panzerung 90) fast nichts aus. Leer = n × <see cref="EconomicStationGun"/> (alte Regel); unbekannte Module fallen weg.
    /// </summary>
    public List<string> EconomicStationGunList = new();

    /// <summary>Punktabwehr der Vorgabeverteidigung je Größe M, L, XL (leer = keine).</summary>
    public List<float> EconomicStationPointDefense = new();
}
