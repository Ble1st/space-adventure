namespace SpaceSim.Core.Model;

public sealed class Good
{
    public GoodId Id;
    public string Name = "";
    public float BasePrice;
    public float VolumePerUnit = 1f;
}

public sealed class Recipe
{
    public Dictionary<GoodId, float> Inputs = new();
    public Dictionary<GoodId, float> Outputs = new();
    public int TicksPerCycle = 10;
}

public sealed class StockEntry
{
    public float Amount;
    /// <summary>Wunschbestand. Amount unter Target erhöht den Preis, darüber senkt er ihn.</summary>
    public float Target = 100f;
    public float Price;
}

public sealed class Station
{
    public StationId Id;
    public string Name = "";
    public SectorId Sector;
    public FactionId Owner;
    public Vec2 Position;
    public float Credits;

    /// <summary>Kollisionsradius des Stationskörpers (inkl. Ring).</summary>
    public const float BodyRadius = 15f;

    /// <summary>Parkposition eines Schiffs an der Station, knapp außerhalb des Körpers.</summary>
    public Vec2 DockPoint(ShipId ship) => Position + Vec2.FromAngle(ship.Value * 2.4f) * 22f;
    public List<Recipe> Production = new();
    public Dictionary<GoodId, StockEntry> Market = new();

    public StockEntry Stock(GoodId good)
    {
        if (!Market.TryGetValue(good, out var e)) Market[good] = e = new StockEntry();
        return e;
    }
}
