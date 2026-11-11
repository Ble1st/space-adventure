namespace SpaceSim.Core.Model;

/// <summary>
/// Politische Seite einer Fraktion (Phase 9: Fraktion = Akteur mit Politik). Wirtschaftliches (Kasse, Persönlichkeit, Besitz) steht beim
/// Akteur gleicher Id-Zahl (<see cref="Universe.Actors"/>); hier liegen Hauptbeziehungen zu anderen Fraktionen (nur Abweichungen von der Vorgabe),
/// Territorium und die Notbau-Bremse.
/// </summary>
public sealed class Faction
{
    public FactionId Id;
    public string Name = "";
    /// <summary>Hauptbeziehungen zu anderen Fraktionen, −1 Feind … +1 Verbündet; nur Abweichungen von der Vorgabe (<see cref="Simulation.Relations"/>).</summary>
    public Dictionary<FactionId, float> Relations = new();
    public HashSet<SectorId> Territory = new();
    /// <summary>Phase 8: Tick des letzten Notbaus dieser Fraktion (Bremse: höchstens einer je Abkühlzeit, <see cref="EconomyRules.EmergencyBuildCooldown"/>); gespeichert.</summary>
    public long LastEmergencyBuildTick = -1_000_000;
}
