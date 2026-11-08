namespace SpaceSim.Core.Model;

public sealed class Sector
{
    public SectorId Id;
    /// <summary>Lage auf der Sternenkarte (nur Darstellung).</summary>
    public Vec2 MapPosition;
    public string Name = "";
    public FactionId Owner;
    public float Radius = 10_000f;
    public List<GateId> Gates = new();
    public List<StationId> Stations = new();
    public List<ResourceField> Fields = new();
    public List<Obstacle> Asteroids = new();

    /// <summary>
    /// Rohstofffeld, zu dessen Gürtel eine Position gehört (nächstes Feld im <see cref="MiningRules.BeltRadius"/>).
    /// Asteroiden sind keine eigenen Erzspeicher: Abgebaut wird immer aus dem Bestand des Feldes.
    /// </summary>
    public ResourceField? FieldFor(Vec2 p)
    {
        ResourceField? best = null;
        float bestD = Mining.MiningRules.BeltRadius;
        foreach (var f in Fields)
        {
            float d = Vec2.Distance(p, f.Position);
            if (d <= bestD) { bestD = d; best = f; }
        }
        return best;
    }
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
