using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>Phase 24: ein laufendes Gefecht für die Anzeige (Sektoransicht, Lagekarte).</summary>
public readonly record struct CombatView(int Id, EncounterCause Cause, string Title, string Detail, IReadOnlyList<ShipId> Ships);

/// <summary>Phase 24: reine Anzeige-Funktionen über laufende Gefechte (die Oberfläche erfindet nichts).</summary>
public static class CombatInfo
{
    /// <summary>Zahl der laufenden Gefechte in einem Sektor.</summary>
    public static int CountIn(Universe u, SectorId sector)
    {
        int n = 0;
        foreach (var _ in u.Fights.In(sector)) n++;
        return n;
    }

    /// <summary>Laufende Gefechte eines Sektors (Id-Reihenfolge) mit Titel („Überfall durch Rote Korsaren“), Dauer, Schüssen und den aktiven Schiffen.</summary>
    public static List<CombatView> InSector(Universe u, SectorId sector)
    {
        var list = new List<CombatView>();
        foreach (var e in u.Fights.In(sector))
        {
            string who = u.ActorName(e.RaiderOwner);
            string title = e.Cause == EncounterCause.Raid ? $"Überfall durch {who}" : e.Cause == EncounterCause.War ? $"Krieg: {who} stellt ein Schiff"
                : e.Cause == EncounterCause.Naval ? "Seeschlacht" : e.Cause == EncounterCause.Feud ? $"Fehde: {who} stellt ein Schiff" : $"Streife stellt {who}";
            var ships = e.Participants.Where(p => p.Active).Select(p => p.Ship).ToList();
            float seconds = (u.Tick - e.StartTick) * SimClock.TickInterval;
            string detail = $"{ships.Count} Schiffe · {seconds:0} s · {e.Shots} Schüsse, {e.Hits} Treffer";
            list.Add(new CombatView(e.Id, e.Cause, title, detail, ships));
        }
        return list;
    }

    /// <summary>Kurztext für einen Toast, wenn ein Gefecht im Spielersektor beginnt.</summary>
    public static string StartToast(Universe u, EncounterCause cause, ActorId raiderOwner) =>
        cause switch
        {
            EncounterCause.Raid => $"Gefecht: {u.ActorName(raiderOwner)} überfällt ein Schiff in diesem Sektor",
            EncounterCause.Assault => "Gefecht: Du greifst an",   // Phase 25 (die genaue Meldung kommt mit EncounterStarted)
            EncounterCause.War => $"Gefecht: Krieg — {u.ActorName(raiderOwner)} greift dein Schiff an",   // Runde 40
            EncounterCause.Naval => "Seeschlacht in diesem Sektor",   // Ausbau Punkt 2
            EncounterCause.Feud => $"Fehde: {u.ActorName(raiderOwner)} stellt ein Schiff in diesem Sektor",   // Ausbau Punkt 6
            _ => "Gefecht: Die Polizei stellt ein Kaperschiff",
        };
}
