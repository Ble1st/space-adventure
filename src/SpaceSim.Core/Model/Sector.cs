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
    public List<Obstacle> Asteroids = new();
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

    /// <summary>Freie Zone um den Feldmittelpunkt, in der keine Asteroiden liegen.</summary>
    public const float ClearRadius = 18f;

    /// <summary>Arbeitsposition eines Miners (je Schiff leicht versetzt).</summary>
    public Vec2 WorkPoint(ShipId ship) => Position + Vec2.FromAngle(ship.Value * 2.4f) * 8f;
}
