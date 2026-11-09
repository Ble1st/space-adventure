namespace SpaceSim.Core.Content;

// Datenmodell der Weltdatei (JSON). Reine DTOs: Änderungen am Dateiformat bleiben hier lokalisiert.

public sealed class WorldDef
{
    public ulong Seed { get; set; } = 1;
    public Model.EconomyRules? Rules { get; set; }
    public List<GoodDef> Goods { get; set; } = new();
    public List<AsteroidClassDef> AsteroidClasses { get; set; } = new();
    public List<StationTypeDef> StationTypes { get; set; } = new();
    public List<FactionDef> Factions { get; set; } = new();
    public List<SectorDef> Sectors { get; set; } = new();
    public List<GateLinkDef> Gates { get; set; } = new();
    public List<ShipGroupDef> Ships { get; set; } = new();
    public PlayerDef Player { get; set; } = new();
}

public sealed class GoodDef
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public float BasePrice { get; set; } = 10;
    public float Volume { get; set; } = 1;
    public string Color { get; set; } = "#c8c8c8";
}

public sealed class AsteroidClassDef
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#8a7a6a";
    /// <summary>Wahrscheinlichkeit pro Laserschuss, einen Brocken zu lösen.</summary>
    public float ChunkChance { get; set; } = 0.5f;
    /// <summary>Multipliziert den Erzvorrat der Asteroiden dieser Klasse.</summary>
    public float OreFactor { get; set; } = 1f;
    /// <summary>Erzsorte (Warenschlüssel) → Gewicht in der Mischung.</summary>
    public Dictionary<string, float> Yields { get; set; } = new();
}

public sealed class RecipeDef
{
    public int Ticks { get; set; } = 10;
    public Dictionary<string, float> In { get; set; } = new();
    public Dictionary<string, float> Out { get; set; } = new();
}

public sealed class StationTypeDef
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public float BuildCost { get; set; } = 5000;
    public float Upkeep { get; set; } = 0.05f;
    public float Income { get; set; }
    public List<RecipeDef> Recipes { get; set; } = new();
    /// <summary>Dienst-Schlüssel, die dieser Typ anbietet; fehlt das Feld, ist der Standard ["trade"] (Handel).</summary>
    public List<string> Services { get; set; } = new() { SpaceSim.Core.Services.TradeService.ServiceKey };
}

public sealed class FactionDef { public int Id { get; set; } public string Name { get; set; } = ""; }

public sealed class SectorDef
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Owner { get; set; }
    public float[] Map { get; set; } = { 0, 0 };
    public List<FieldDef> Fields { get; set; } = new();
    public List<StationDef> Stations { get; set; } = new();
    /// <summary>Anzahl erzloser Hintergrund-Asteroiden (Hindernisse); Felder erhalten zusätzlich ihren Erzgürtel.</summary>
    public int Asteroids { get; set; } = 25;
}

public sealed class FieldDef
{
    /// <summary>Schlüssel der Asteroidenklasse des Gürtels.</summary>
    public string Class { get; set; } = "";
    public float[] Pos { get; set; } = { 0, 0 };
    /// <summary>Soll-Anzahl Erz-Asteroiden im Gürtel.</summary>
    public int Belt { get; set; } = 14;
    /// <summary>Mittlere Ticks bis zum Nachwachsen eines fehlenden Asteroiden.</summary>
    public int Respawn { get; set; } = 120;
}

public sealed class StationDef
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public float[] Pos { get; set; } = { 0, 0 };
    public bool Essential { get; set; }
    public float Credits { get; set; } = 10_000;
    /// <summary>Ware → [Bestand, Wunschbestand]. Fehlende Waren aus den Rezepten bekommen Standardwerte.</summary>
    public Dictionary<string, float[]> Stock { get; set; } = new();
}

public sealed class GateEndDef { public int Id { get; set; } public int Sector { get; set; } public float[] Pos { get; set; } = { 0, 0 }; }
public sealed class GateLinkDef { public GateEndDef A { get; set; } = new(); public GateEndDef B { get; set; } = new(); }

public sealed class ShipGroupDef
{
    public string Role { get; set; } = "trader";
    public int Faction { get; set; }
    public int Sector { get; set; }
    public float Credits { get; set; } = 1000;
    public float Cargo { get; set; } = 100;
    public int Count { get; set; } = 1;
}

public sealed class PlayerDef { public int Faction { get; set; } = 1; public int Sector { get; set; } = 1; public float Credits { get; set; } = 500; }
