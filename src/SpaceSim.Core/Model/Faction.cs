namespace SpaceSim.Core.Model;

public sealed class Faction
{
    public FactionId Id;
    public string Name = "";
    /// <summary>-1 Feind … +1 Verbündet.</summary>
    public Dictionary<FactionId, float> Relations = new();
    public HashSet<SectorId> Territory = new();
    /// <summary>Phase 8: Tick des letzten Notbaus dieser Fraktion (Bremse: höchstens einer je Abkühlzeit, <see cref="EconomyRules.EmergencyBuildCooldown"/>); gespeichert.</summary>
    public long LastEmergencyBuildTick = -1_000_000;
}
