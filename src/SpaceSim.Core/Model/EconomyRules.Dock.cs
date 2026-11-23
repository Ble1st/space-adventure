namespace SpaceSim.Core.Model;

/// <summary>
/// Liegezeit an Stationen (Nutzerwunsch: „Schiffe sollten etwas länger im Hangar bleiben als rein verkaufen und direkt weiter; neu angekommene Schiffe
/// sollen im Hangar sichtbar sein“): Ein Schiff, das an einer Station ankommt (<see cref="Ship.DockedSince"/>), legt frühestens nach dieser Zahl
/// Ticks wieder ab — <see cref="Universe.StartTransit"/> verschiebt die geplante Reise um die Restzeit; bis dahin liegt es am Andockpunkt und zählt
/// im Hangar (<see cref="Simulation.Hangar.DockedShips"/>). Gilt nur im Sektor der Person (<see cref="Universe.PlayerSector"/>) — nur dort ist es
/// sichtbar; überall sonst kostete es die Wirtschaft Lieferzeit (gemessen: Exporte −12 %, Farmen öfter ohne Wasser). Code-Vorgabe 0 = alte Regel;
/// die Standardwelt setzt Werte.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Liegezeit in Ticks für KI-Schiffe (Händler, Schürfer, …). 0 = sofort weiter.</summary>
    public int DockDwellTicks;
    /// <summary>Liegezeit in Ticks für eigene Schiffe des Spielers, die einen Befehl ausführen. 0 = sofort weiter.</summary>
    public int DockDwellTicksPlayer;
}
