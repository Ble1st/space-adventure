namespace SpaceSim.Core.Model;

public sealed class Faction
{
    public FactionId Id;
    public string Name = "";
    /// <summary>-1 Feind … +1 Verbündet.</summary>
    public Dictionary<FactionId, float> Relations = new();
    public HashSet<SectorId> Territory = new();
}
