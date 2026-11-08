namespace SpaceSim.Core.Model;

public sealed class Sector
{
    public SectorId Id;
    public string Name = "";
    public FactionId Owner;
    public float Radius = 10_000f;
    public List<GateId> Gates = new();
    public List<StationId> Stations = new();
    public List<ShipId> Ships = new();
    public List<ResourceField> Fields = new();
}

public sealed class Gate
{
    public GateId Id;
    public SectorId Sector;
    public Vec2 Position;
    public GateId Target;
    public bool Open = true;
}

public sealed class ResourceField
{
    public GoodId Good;
    public Vec2 Position;
    public float Amount;
    public float RegenPerTick;
    /// <summary>Obergrenze für Regeneration; 0 = unbegrenzt.</summary>
    public float Capacity;
}
