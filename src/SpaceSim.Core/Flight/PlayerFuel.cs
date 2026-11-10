using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Flight;

/// <summary>
/// Treibstoff des Spielerflugs (Phase 4c). Der Spieler fliegt nicht über <see cref="Travel.Start"/>, deshalb bucht die
/// Ansicht pro Physikframe die geflogene Strecke über <see cref="ConsumeFlown"/> aus dem Tank und begrenzt das Tempo
/// über <see cref="ThrottleCap"/> (Notantrieb bei leerem Tank). Reine, testbare Funktionen — kein Simulationssystem
/// ruft sie auf, das NPC-Verhalten ändert sich dadurch nicht.
/// </summary>
public static class PlayerFuel
{
    /// <summary>Tempo-Deckel bei leerem Tank: „Notantrieb“ als Anteil der Höchstgeschwindigkeit.</summary>
    public const float EmergencyThrottle = 0.2f;

    /// <summary>
    /// Bucht den Verbrauch für eine geflogene Strecke aus dem Tank (Strecke × <see cref="ShipStats.FuelUsePerUnit"/> /
    /// efficiency des aktiven Treibstoffs; Tank nie negativ) — dieselbe Regel wie <see cref="Refuel.Consume"/> für die
    /// KI-Schiffe, damit die Logik nicht doppelt geschrieben wird.
    /// </summary>
    public static void ConsumeFlown(Ship ship, float distance) => Refuel.Consume(ship, distance);

    /// <summary>
    /// Tempo-Deckel: 1.0 mit Treibstoff (oder ganz ohne Treibstoffsystem, z. B. Schiff ohne Modell),
    /// <see cref="EmergencyThrottle"/> bei leerem Tank.
    /// </summary>
    public static float ThrottleCap(Ship ship)
    {
        if (Refuel.ActiveFuel(ship) is null) return 1f;   // kein Antrieb/Tank: kein Verbrauch, keine Deckelung
        return ship.TotalFuel > 0.0001f ? 1f : EmergencyThrottle;
    }
}
