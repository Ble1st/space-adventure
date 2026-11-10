using System.Text.Json.Serialization;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

// Speicherformat (JSON). Bewusst getrennt vom Laufzeitmodell: Das Modell darf sich ändern, das Format wird versioniert
// und über Migrationen in SaveGameJson angehoben. Ids sind einfache ints, Dictionaries nutzen int-Schlüssel.

public sealed class SaveGame
{
    public const int CurrentVersion = 7;

    public int Version { get; set; } = CurrentVersion;
    public string SavedAtUtc { get; set; } = "";
    public long Tick { get; set; }
    public ulong Seed { get; set; }
    public ulong RngState { get; set; }
    public long LastStationBuildTick { get; set; }
    public int NextShipId { get; set; }
    public int NextStationId { get; set; }
    public int NextAsteroidId { get; set; } = 1;
    public int PlayerSector { get; set; }
    public int PlayerShip { get; set; }
    public EconomyRules Rules { get; set; } = new();
    /// <summary>Spielerkasse (Phase 5b, v7); ältere Stände bekommen sie per Migration aus den Credits des Spielerschiffs.</summary>
    public PlayerWalletSave PlayerWallet { get; set; } = new();

    public List<GoodSave> Goods { get; set; } = new();
    public List<AsteroidClassSave> AsteroidClasses { get; set; } = new();
    public List<StationTypeSave> StationTypes { get; set; } = new();
    /// <summary>Slotarten (Content; v3-Saves kennen die Sektionen nicht, die Migration reicht die Default-Welt nach).</summary>
    public List<SlotTypeSave> SlotTypes { get; set; } = new();
    /// <summary>Module (Content, wie die Rezepte der Stationstypen im Save abgelegt).</summary>
    public List<ModuleSave> Modules { get; set; } = new();
    /// <summary>Schiffsklassen (Content).</summary>
    public List<ShipClassSave> ShipClasses { get; set; } = new();
    /// <summary>Schiffsmodelle (Content).</summary>
    public List<ShipModelSave> ShipModels { get; set; } = new();
    public List<FactionSave> Factions { get; set; } = new();
    public List<SectorSave> Sectors { get; set; } = new();
    public List<GateSave> Gates { get; set; } = new();
    public List<StationSave> Stations { get; set; } = new();
    public List<ShipSave> Ships { get; set; } = new();
}

/// <summary>Kasse der Spielerperson (Phase 5b).</summary>
public sealed class PlayerWalletSave
{
    public float Credits { get; set; } public int Faction { get; set; }
}

public sealed class GoodSave
{
    public int Id { get; set; } public string Key { get; set; } = ""; public string Name { get; set; } = "";
    public string Color { get; set; } = "#c8c8c8"; public float BasePrice { get; set; } public float Volume { get; set; }
    /// <summary>Warenform (solid/liquid); fehlt in alten Spielständen → solid. Additiv, daher kein Versionssprung.</summary>
    public string? Form { get; set; }
}

public sealed class YieldSave { public int Good { get; set; } public float Weight { get; set; } }

public sealed class AsteroidClassSave
{
    public string Key { get; set; } = ""; public string Name { get; set; } = ""; public string Color { get; set; } = "#8a7a6a";
    public float ChunkChance { get; set; } = 0.5f; public float OreFactor { get; set; } = 1f;
    public List<YieldSave> Yields { get; set; } = new();
}

public sealed class RecipeSave
{
    public int Ticks { get; set; }
    public Dictionary<int, float> In { get; set; } = new();
    public Dictionary<int, float> Out { get; set; } = new();
}

public sealed class StationTypeSave
{
    public string Key { get; set; } = ""; public string Name { get; set; } = "";
    public float BuildCost { get; set; } public float Upkeep { get; set; } public float Income { get; set; }
    public List<RecipeSave> Recipes { get; set; } = new();
    /// <summary>Dienst-Schlüssel des Typs. Fehlt das Feld (Spielstände vor dieser Erweiterung), gilt der Standard ["trade"].</summary>
    public List<string>? Services { get; set; }
    /// <summary>Lager ohne Rezept (Phase 5a, v6); fehlt in älteren Spielständen = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<StationStockSave>? Stocks { get; set; }
}

/// <summary>Lagerposten eines Stationstyps (Phase 5a): GoodId, Anfangs- und Wunschbestand.</summary>
public sealed class StationStockSave { public int Good { get; set; } public float Amount { get; set; } public float Target { get; set; } }

public sealed class FactionSave
{
    public int Id { get; set; } public string Name { get; set; } = "";
    public Dictionary<int, float> Relations { get; set; } = new();
    public List<int> Territory { get; set; } = new();
}

public sealed class FieldSave
{
    public string Class { get; set; } = "";
    public Vec2 Pos { get; set; }
    public int TargetAsteroids { get; set; } = 14;
    public int RespawnTicks { get; set; } = 120;

    // Nur Format v1/v2 (ein Erz je Feld, Vorrat im Feld); wird beim Laden umgerechnet und nicht mehr geschrieben
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Good { get; set; }
    // Nur Format v1 (Vorrat lag im Feld); wird beim Laden in Asteroiden-Erz umgerechnet und nicht mehr geschrieben
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float Amount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float RegenPerTick { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float Capacity { get; set; }
}

public sealed class AsteroidSave
{
    public int Id { get; set; } public Vec2 Pos { get; set; } public float BaseRadius { get; set; }
    public string Class { get; set; } = "";
    public float Ore { get; set; } public float MaxOre { get; set; }

    /// <summary>Nur Format v2: einzelne Erzsorte statt Klasse.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Good { get; set; }
}

public sealed class SectorSave
{
    public int Id { get; set; } public string Name { get; set; } = ""; public int Owner { get; set; }
    public Vec2 Map { get; set; } public float Radius { get; set; }
    public List<int> Gates { get; set; } = new();
    public List<int> Stations { get; set; } = new();
    public List<FieldSave> Fields { get; set; } = new();
    public List<AsteroidSave> Rocks { get; set; } = new();

    /// <summary>Nur Format v1 (Asteroiden ohne Erz).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<Obstacle>? Asteroids { get; set; }
}

public sealed class GateSave { public int Id { get; set; } public int Sector { get; set; } public Vec2 Pos { get; set; } public int Target { get; set; } public bool Open { get; set; } }

public sealed class StockSave { public float Amount { get; set; } public float Target { get; set; } public float Price { get; set; } }

public sealed class StationSave
{
    public int Id { get; set; } public string Type { get; set; } = ""; public string Name { get; set; } = "";
    public int Sector { get; set; } public int Owner { get; set; } public Vec2 Pos { get; set; }
    public float Credits { get; set; } public bool Essential { get; set; } public long BelowLimitSince { get; set; }
    public List<RecipeSave> Recipes { get; set; } = new();
    public Dictionary<int, StockSave> Market { get; set; } = new();
}

public sealed class OrderSave { public string Kind { get; set; } = ""; public int A { get; set; } public int B { get; set; } public int C { get; set; } }
public sealed class LegSave { public int Sector { get; set; } public Vec2 From { get; set; } public Vec2 To { get; set; } public long Start { get; set; } public long End { get; set; } }

/// <summary>Ein Slot eines Schiffs im Save (Art, Größe, optional facing/arc als Text).</summary>
public sealed class ShipSlotSave
{
    public string Type { get; set; } = "";
    /// <summary>Slotgröße S/M/L/XL als Text.</summary>
    public string Size { get; set; } = "S";
    public string? Facing { get; set; }
    public float? Arc { get; set; }
}

public sealed class SlotTypeSave
{
    public string Key { get; set; } = ""; public string Name { get; set; } = ""; public bool Required { get; set; }
    /// <summary>Farbe der Modul-Waren (Phase 5a); fehlt in älteren Spielständen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Color { get; set; }
}

/// <summary>Ein zulässiger Treibstoff eines Antriebsmoduls im Save (Warenschlüssel wie in cost, damit GoodIds nicht verschoben werden müssen).</summary>
public sealed class FuelSave
{
    public string Good { get; set; } = "";
    public float Efficiency { get; set; } = 1f;
}

public sealed class ModuleSave
{
    public string Key { get; set; } = "";
    /// <summary>Anzeigename (Phase 5a); fehlt in älteren Spielständen (dann = Schlüssel).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
    public string Slot { get; set; } = "";
    public string Size { get; set; } = "S";
    public int Tier { get; set; } = 1;
    public List<string> Grants { get; set; } = new();
    public Dictionary<string, float> Stats { get; set; } = new();
    public float PowerDraw { get; set; }
    public float PeakDraw { get; set; }
    public float Mass { get; set; }
    public float Upkeep { get; set; }
    public string Holds { get; set; } = "any";
    public Dictionary<string, float> Cost { get; set; } = new();
    /// <summary>Zulässige Treibstoffe des Antriebs; fehlt in v4-Saves (die Migration reicht sie aus der Default-Welt nach).</summary>
    public List<FuelSave> Fuel { get; set; } = new();
}

public sealed class ShipClassSave
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Size { get; set; } = "S";
    public float BaseHull { get; set; } = 100;
    public float BaseMass { get; set; } = 10;
    /// <summary>Tankkapazität des Rumpfs; fehlt in v4-Saves (die Migration reicht sie aus der Default-Welt nach).</summary>
    public float BaseFuel { get; set; }
    public List<ShipSlotSave> Slots { get; set; } = new();
    /// <summary>Bausatz-Warenschlüssel der Klasse (Phase 5a); null = Regel ShipKitGoodKey.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Kit { get; set; }
}

public sealed class ShipModelSave
{
    public string Key { get; set; } = "";
    public string Class { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string?> Fit { get; set; } = new();
}

public sealed class TransitSave
{
    public long StartTick { get; set; } public long ArrivalTick { get; set; }
    public int ToSector { get; set; } public Vec2 ToPosition { get; set; }
    public List<LegSave> Legs { get; set; } = new();
}

public sealed class ShipSave
{
    public int Id { get; set; } public int Faction { get; set; } public string Role { get; set; } = "";
    /// <summary>Fähigkeiten als Flag-Wert; null in alten Spielständen = aus der Rolle ableiten.</summary>
    public int? Capabilities { get; set; }
    /// <summary>Steuerer ("Ai"/"Player"); null in alten Spielständen = aus der Rolle ableiten.</summary>
    public string? Controller { get; set; }
    /// <summary>Schiffsmodell-Schlüssel; null in v3-Saves (die Migration vergibt das Einheitsmodell nach Rolle/Laderaum).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ModelKey { get; set; }
    /// <summary>Slotliste des Schiffs; null in v3-Saves (beim Laden aus dem Modell gefüllt).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ShipSlotSave>? Slots { get; set; }
    /// <summary>Modul-Schlüssel je Slot (null = leer); null in v3-Saves (beim Laden aus dem Modell gefüllt).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string?>? Fit { get; set; }
    public float CargoCapacity { get; set; } public float Credits { get; set; } public float Hull { get; set; }
    public int Sector { get; set; } public Vec2 Pos { get; set; }
    public OrderSave? Order { get; set; }
    public string Phase { get; set; } = ""; public string MinePhase { get; set; } = "";
    public TransitSave? Transit { get; set; }
    public long NextThinkTick { get; set; } public long IdleSince { get; set; }
    public float ShotTimer { get; set; } public int MineAsteroid { get; set; }
    public Dictionary<int, float> Cargo { get; set; } = new();
    /// <summary>Treibstoff an Bord je Ware (GoodId → Menge); fehlt in v4-Saves (die Migration trägt einen vollen Tank nach).</summary>
    public Dictionary<int, float> Fuel { get; set; } = new();
}
