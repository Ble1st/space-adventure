namespace SpaceSim.Core.Model;

/// <summary>Phase 24: Schadensart einer Waffe (KAMPF.md 3.4): Energie ist stark gegen Schild und schwach gegen Panzerung, kinetisch umgekehrt.</summary>
public enum DamageType { Energy, Kinetic, Explosive }


/// <summary>Phase 24: Anlass eines Gefechts (KAMPF.md 4.1); Phase 25: <see cref="Assault"/> = der Spieler greift ein Schiff an (Schuss auf ein fremdes Schiff);
/// Phase 27: <see cref="Arrest"/> = die Polizei stellt einen gesuchten Spieler (Fahndungsstufe ≥ 3) als echtes Gefecht; Phase 28: <see cref="Siege"/> = Angriff
/// auf eine Station (<see cref="Encounter.Station"/>; Kriegsschiffe im Krieg, der Spieler).</summary>
public enum EncounterCause { Raid, Intercept, Assault, Arrest, Siege }

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
    /// <summary>
    /// Phase 25: echte Position und Blickrichtung im Spielersektor (<see cref="Encounter.Spatial"/>); außerhalb bleibt die Abstandszahl <see cref="X"/>
    /// maßgeblich. Gespeichert und gehasht nur in räumlichen Gefechten.
    /// </summary>
    public Vec2 Pos;
    public Vec2 Dir = new(0f, -1f);
    /// <summary>Phase 25: Position vor dem letzten Takt (nur Darstellung: Zwischenbilder zwischen den Ticks; nicht gespeichert, nach dem Laden = <see cref="Pos"/>).</summary>
    public Vec2 PrevPos;
    /// <summary>Phase 25: Station, an der das Schiff vor dem Gefecht angedockt lag (default = keine) — nur dieser Liegeplatz wird beim Austritt wiederhergestellt.</summary>
    public StationId WasDockedAt;
    /// <summary>Phase 25: Besitzer des Schiffs beim Eintritt (Kopfgeld/Fahndung auch nach der Zerstörung).</summary>
    public ActorId Owner;
    /// <summary>
    /// Phase 27: Zuflucht eines Fliehenden — Abstand zur nächsten erreichbaren Zuflucht beim Eintritt (Station, deren Besitzer nicht feindlich ist, oder ein offenes
    /// Tor) auf der Gefechtslinie; jeder Fluchtschritt verkürzt ihn, bei 0 ist das Schiff entkommen (dockt an bzw. springt). <see cref="RefugeId"/>: &gt; 0 Station,
    /// &lt; 0 Tor (−Id), 0 = keine Zuflucht. <see cref="RefugePos"/> ist der Zielpunkt im räumlichen Gefecht. Gespeichert und gehasht nur, wenn gesetzt.
    /// </summary>
    public float Refuge;
    public int RefugeId;
    /// <summary>
    /// Gerätetest Phase 28 (Runde 6): Tick des letzten eigenen Schusses des aktiven Spielerschiffs (0 = keiner). Solange er höchstens
    /// <see cref="EconomyRules.PlayerFireHoldTicks"/> zurückliegt, hält die eigene Reichweite das Gefecht fest (<c>CombatSense.StillShooting</c>).
    /// </summary>
    public long LastShotTick;
    public Vec2 RefugePos;
    /// <summary>Phase 28: Belagerungsziel des Takts (Station; Phase 31 auch Tor — aus der Taktik, jeden Takt neu, nicht gespeichert).</summary>
    public SiegeRef TargetSiege;
    /// <summary>Phase 25: abgeleitet = <see cref="Encounter.Spatial"/> des Gefechts (Abstand aus <see cref="Pos"/> statt <see cref="X"/>); nicht gespeichert.</summary>
    public bool InSpace;

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
    /// <summary>
    /// Phase 25: Das Gefecht läuft räumlich (im Spielersektor): Abstand und Feuerwinkel kommen aus <see cref="CombatParticipant.Pos"/>/<see cref="CombatParticipant.Dir"/>,
    /// die Simulation schreibt die Positionen fort (<see cref="Combat.CombatAutopilot"/>). Wechselt beim Betreten/Verlassen des Sektors durch den Spieler
    /// (<see cref="Combat.CombatSpace"/>). Gespeichert und gehasht.
    /// </summary>
    public bool Spatial;
    /// <summary>
    /// Phase 28: Belagerung — die angegriffene Station (default = keine) und ihre Position auf der Gefechtslinie (im räumlichen Gefecht gilt ihre echte Position).
    /// Gespeichert und gehasht nur, wenn gesetzt.
    /// </summary>
    public StationId Station;
    public float StationX;
    /// <summary>Phase 31: belagertes Tor (Tor-Paar, kanonische Hälfte mit kleinerer Id; 0 = keins). Die Linienposition teilt es mit <see cref="StationX"/>.
    /// Gespeichert und gehasht nur, wenn gesetzt. Ein Gefecht hat höchstens ein Belagerungsziel.</summary>
    public GateId Gate;
    /// <summary>Phase 31: das Belagerungsziel (Station oder Tor; keins = default).</summary>
    public SiegeRef Target => Station.Value != 0 ? SiegeRef.Of(Station) : Gate.Value != 0 ? SiegeRef.Of(Gate) : default;
    /// <summary>Phase 28: Raketen im Flug (Abschussreihenfolge); gespeichert und gehasht nur, wenn welche fliegen.</summary>
    public List<MissileFlight> Missiles = new();
    /// <summary>Ausgewertet (Ereignisse veröffentlicht); wird im selben Schritt entfernt — nie gespeichert.</summary>
    public bool Done;

    public CombatParticipant? Of(ShipId ship)
    {
        // Runde 6: der Spieler tritt über Engage.Rejoin mit seinem alten Eintrag wieder bei (kein zweiter Eintrag); NPC-Wiederbeitritte bleiben wie bisher
        foreach (var p in Participants) if (p.Ship == ship) return p;
        return null;
    }
}
