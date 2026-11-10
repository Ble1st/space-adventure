using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Gibt ein Schiff frei, dem die Fähigkeit für seine Aufgabe fehlt (zum Beispiel Frachtraum oder Werkzeug ausgebaut):
/// Der Auftrag fällt weg (damit keine Geisterreservierung Lagerbestand oder Felder blockiert), eine laufende Reise
/// wird zu Ende geflogen und bei Ankunft beendet, ohne dort zu handeln oder zu schürfen. Fracht bleibt an Bord.
/// Ohne diesen Schritt bliebe das Schiff mitten in der Reise stehen, weil die KI-Systeme es überspringen.
/// </summary>
public static class JobRelease
{
    public static void Release(Universe u, Ship ship)
    {
        ship.Order = null;
        ship.Phase = TradePhase.Idle;
        ship.MinePhase = MinePhase.Idle;
        if (ship.Transit is { } t && u.Tick >= t.ArrivalTick) u.FinishTransit(ship);
    }
}
