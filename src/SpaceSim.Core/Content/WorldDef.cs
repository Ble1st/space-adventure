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
    /// <summary>Slotarten; fehlt der Abschnitt, bleibt die Registry leer und die Welt gültig.</summary>
    public List<SlotTypeDef> SlotTypes { get; set; } = new();
    /// <summary>Module; fehlt der Abschnitt, bleibt die Registry leer.</summary>
    public List<ModuleDef> Modules { get; set; } = new();
    /// <summary>Schiffsklassen; fehlt der Abschnitt, bleibt die Registry leer.</summary>
    public List<ShipClassDef> ShipClasses { get; set; } = new();
    /// <summary>Schiffsmodelle; fehlt der Abschnitt, bleibt die Registry leer.</summary>
    public List<ShipModelDef> ShipModels { get; set; } = new();
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
    /// <summary>Warenform "solid" (Standard, Feld fehlt) oder "liquid".</summary>
    public string? Form { get; set; }
}

/// <summary>Slotart (Reaktor, Antrieb, …).</summary>
public sealed class SlotTypeDef
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Pflichtslot: ein Fit ohne Modul in diesem Slot ist ungültig.</summary>
    public bool Required { get; set; }
}

/// <summary>Ein zulässiger Treibstoff eines Antriebsmoduls: Warenschlüssel + efficiency (Phase 4).</summary>
public sealed class FuelEntryDef
{
    /// <summary>Warenschlüssel (muss eine flüssige Ware sein).</summary>
    public string Good { get; set; } = "";
    /// <summary>Wirkungsgrad; Verbrauch = Menge ÷ efficiency. Muss > 0 sein.</summary>
    public float Efficiency { get; set; } = 1f;
}

public sealed class ModuleDef
{
    public string Key { get; set; } = "";
    /// <summary>Slotart-Schlüssel, in die dieses Modul passt.</summary>
    public string Slot { get; set; } = "";
    /// <summary>Größe S/M/L/XL als Text.</summary>
    public string Size { get; set; } = "S";
    /// <summary>Stufe Mk1–Mk3.</summary>
    public int Tier { get; set; } = 1;
    /// <summary>Fähigkeitsnamen (Capability), die das Modul verleiht.</summary>
    public List<string> Grants { get; set; } = new();
    /// <summary>Freie Kennzahlen (z.B. powerOutput, capacity, thrust, armor, chunkChanceMult, fuelCapacity).</summary>
    public Dictionary<string, float> Stats { get; set; } = new();
    /// <summary>Dauer-Leistungsaufnahme.</summary>
    public float PowerDraw { get; set; }
    /// <summary>Kurzzeitige Spitzenlast.</summary>
    public float PeakDraw { get; set; }
    public float Mass { get; set; }
    public float Upkeep { get; set; }
    /// <summary>Warenform, die das Frachtmodul fasst: solid/liquid/any; fehlt = any.</summary>
    public string? Holds { get; set; }
    /// <summary>Herstellkosten: Warenschlüssel → Menge.</summary>
    public Dictionary<string, float> Cost { get; set; } = new();
    /// <summary>Zulässige Treibstoffe des Antriebsmoduls (good = Warenschlüssel, liquid, efficiency > 0, keine Dopplung).</summary>
    public List<FuelEntryDef> Fuel { get; set; } = new();
}

/// <summary>Ein Slot einer Schiffsklasse.</summary>
public sealed class ClassSlotDef
{
    /// <summary>Slotart-Schlüssel.</summary>
    public string Type { get; set; } = "";
    /// <summary>Slotgröße S/M/L/XL als Text.</summary>
    public string Size { get; set; } = "S";
    /// <summary>Ausrichtung (bow/port/starboard/stern), optional, nur Waffen-/Werkzeug-Slots.</summary>
    public string? Facing { get; set; }
    /// <summary>Halber Öffnungswinkel in Grad, optional, nur Waffen-/Werkzeug-Slots.</summary>
    public float? Arc { get; set; }
}

public sealed class ShipClassDef
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Größe des Rumpfs S/M/L/XL als Text.</summary>
    public string Size { get; set; } = "S";
    public float BaseHull { get; set; } = 100;
    public float BaseMass { get; set; } = 10;
    /// <summary>Tankkapazität des Rumpfs (baseFuel); Standard 0. Zusammen mit fuelCapacity-Modulen (z. B. Tanks) ergibt sich ShipStats.FuelCapacity.</summary>
    public float BaseFuel { get; set; }
    public List<ClassSlotDef> Slots { get; set; } = new();
}

public sealed class ShipModelDef
{
    public string Key { get; set; } = "";
    /// <summary>Klassen-Schlüssel.</summary>
    public string Class { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Modulschlüssel je Slot der Klasse; null = leerer Slot; Länge = Slotanzahl der Klasse.</summary>
    public List<string?> Fit { get; set; } = new();
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
    /// <summary>Schiffsmodell-Schlüssel; fehlt das Feld, wählt <c>WorldBuilder.NewShip</c> das Einheitsmodell nach Rolle und <see cref="Cargo"/>.</summary>
    public string? Model { get; set; }
}

public sealed class PlayerDef
{
    public int Faction { get; set; } = 1; public int Sector { get; set; } = 1; public float Credits { get; set; } = 500;
    /// <summary>Schiffsmodell-Schlüssel; fehlt das Feld, wählt <c>WorldBuilder.NewShip</c> das Einheitsmodell der Spielerrolle.</summary>
    public string? Model { get; set; }
}
