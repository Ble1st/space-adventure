namespace SpaceSim.Core.Model;

/// <summary>Phase 24: Schadensart einer Waffe (KAMPF.md 3.4): Energie ist stark gegen Schild und schwach gegen Panzerung, kinetisch umgekehrt.</summary>
public enum DamageType { Energy, Kinetic }

/// <summary>Phase 24: Anlass eines Gefechts (KAMPF.md 4.1). Angriffe des Spielers kommen mit Phase 25.</summary>
public enum EncounterCause { Raid, Intercept }

/// <summary>Phase 24: Bewegung eines Teilnehmers in einem Takt (Abstandszahl statt Physik, KAMPF.md 4.2).</summary>
public enum CombatMove { Approach, Hold, Flee }

/// <summary>Phase 24: Wie ein Teilnehmer das Gefecht verlassen hat.</summary>
public enum CombatExit { None, Destroyed, Fled, Surrendered, Looted, Timeout, Ended, Lost }

/// <summary>
/// Phase 24 (KAMPF.md 4.1, 4.2): ein Schiff in einem Gefecht. Außerhalb des Spielersektors gibt es keine Physik; das Gefecht führt je Teilnehmer eine
/// Position <see cref="X"/> auf einer Linie, der Abstand eines Paars ist |X₁ − X₂|. Die Nachladeuhren je Slot (<see cref="Timers"/>, Sekunden) und die
/// Fluchtschwelle (<see cref="Courage"/>, Hüllenanteil, unter dem ein Händler Fracht abwirft) gehören zum Gefecht. Gespeichert und gehasht.
/// </summary>
public sealed class CombatParticipant
{
    public ShipId Ship;
    /// <summary>0 = Angreifer, 1 = Verteidiger.</summary>
    public int Side;
    /// <summary>Schlüssel der Taktik (<see cref="Combat.CombatTactics"/>).</summary>
    public string Tactic = "";
    public float X;
    /// <summary>Aktuelles Ziel (default = keins).</summary>
    public ShipId Target;
    /// <summary>Bewegung des letzten Takts (für die Trefferregel: Zieltempo).</summary>
    public CombatMove Move = CombatMove.Approach;
    /// <summary>Tempo des letzten Takts (Einheiten je Sekunde).</summary>
    public float Speed;
    /// <summary>Nachladezeit je Slot in Sekunden (Index = Slot; fehlender Eintrag = bereit).</summary>
    public List<float> Timers = new();
    /// <summary>Hüllenanteil, unter dem der Teilnehmer aufgibt (Fracht abwirft); beim Eintritt gewürfelt.</summary>
    public float Courage;
    /// <summary>Anteil der Fracht, den er bei der Aufgabe abwirft; beim Eintritt gewürfelt.</summary>
    public float DropShare;
    /// <summary>Leistungsverteilung vor dem Gefecht (wird beim Austritt wiederhergestellt).</summary>
    public PowerMode PrevMode;
    public CombatExit Exit;

    public bool Active => Exit == CombatExit.None;
    public float TimerOf(int slot) => slot >= 0 && slot < Timers.Count ? Timers[slot] : 0f;
}

/// <summary>
/// Phase 24 (KAMPF.md 4.1): ein laufendes Gefecht in einem Sektor (<see cref="Universe.Fights"/>). Seiten 0 (Angreifer) und 1 (Verteidiger), Teilnehmer in
/// fester Reihenfolge (Angreifer zuerst, dann nach Eintritt). <see cref="Raider"/>/<see cref="Victim"/> merken die Hauptbeteiligten für die Auswertung (Ereignisse
/// <see cref="Events.RaidResolved"/>/<see cref="Events.RaiderIntercepted"/>), <see cref="Loot"/>/<see cref="Fenced"/> die abgeworfene Beute. Gespeichert und gehasht.
/// </summary>
public sealed class Encounter
{
    public int Id;
    public SectorId Sector;
    public long StartTick;
    public EncounterCause Cause;
    public List<CombatParticipant> Participants = new();
    public ShipId Raider;
    /// <summary>Besitzer des Kaperschiffs (für Fahndung und Kopfgeld, auch wenn es zerstört ist).</summary>
    public ActorId RaiderOwner;
    public ShipId Victim;
    /// <summary>Polizeischiff, das beim Überfall eingegriffen hat (default = keins).</summary>
    public ShipId Police;
    public float Loot;
    public float Fenced;
    public int Shots;
    public int Hits;
    /// <summary>Ausgewertet (Ereignisse veröffentlicht); wird im selben Schritt entfernt — nie gespeichert.</summary>
    public bool Done;

    public CombatParticipant? Of(ShipId ship)
    {
        foreach (var p in Participants) if (p.Ship == ship) return p;
        return null;
    }
}
