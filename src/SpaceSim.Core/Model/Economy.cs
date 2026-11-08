namespace SpaceSim.Core.Model;

public sealed class Good
{
    public GoodId Id;
    /// <summary>Stabiler Schlüssel aus den Inhaltsdaten ("iron_ore"); Verweise zwischen Daten und Regeln laufen darüber.</summary>
    public string Key = "";
    public string Name = "";
    /// <summary>Farbe für Brocken und Anzeige (#RRGGBB).</summary>
    public string Color = "#c8c8c8";
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

public sealed class StationType
{
    public string Key = "";
    public string Name = "";
    public List<Recipe> Recipes = new();
    public float BuildCost = 5000f;
    public float UpkeepPerTick = 0.05f;
    /// <summary>Einnahmen aus Verkäufen außerhalb der simulierten Welt (Faucet).</summary>
    public float IncomePerTick;
}

public sealed class Station
{
    public StationId Id;
    public string TypeKey = "";
    /// <summary>Wesentliche Stationen werden nie wegen Pleite aufgegeben.</summary>
    public bool Essential;
    public long BelowLimitSince = -1;
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

    /// <summary>
    /// Wie viele Einheiten die Station noch annimmt, bevor ihr Lager voll ist (Wunschbestand × <see cref="EconomyRules.OverstockFactor"/>).
    /// Eine volle Station kauft nicht mehr – sonst würde Beifang einer Erzmischung ewig auf Halde landen.
    /// </summary>
    public float AcceptableUnits(GoodId good, EconomyRules rules) =>
        Market.TryGetValue(good, out var e) ? MathF.Max(0f, e.Target * rules.OverstockFactor - e.Amount) : 0f;

    public StockEntry Stock(GoodId good)
    {
        if (!Market.TryGetValue(good, out var e)) Market[good] = e = new StockEntry();
        return e;
    }
}
