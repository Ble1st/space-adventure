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
    /// <summary>Farbe der Modul-Waren dieser Slotart (#RRGGBB, Phase 5a); fehlt = Standardfarbe.</summary>
    public string? Color { get; set; }
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
    /// <summary>Anzeigename, zugleich Name der automatisch angelegten Modul-Ware (Phase 5a); fehlt = Schlüssel.</summary>
    public string? Name { get; set; }
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
    /// <summary>Herstellkosten: Warenschlüssel → Menge; zugleich das Rezept der Modulfabrik und Grundlage des Warenpreises (Phase 5a).</summary>
    public Dictionary<string, float> Cost { get; set; } = new();
    /// <summary>Zulässige Treibstoffe des Antriebsmoduls (good = Warenschlüssel, liquid, efficiency > 0, keine Dopplung).</summary>
    public List<FuelEntryDef> Fuel { get; set; } = new();
    /// <summary>Phase 7: Verbesserung dieses Moduls (Dienst „upgrade“, Güteklassen); fehlt = keine.</summary>
    public ModuleUpgradeDef? Upgrade { get; set; }
}

/// <summary>Verbesserung eines Moduls (Phase 7): Zielmodul gleicher Slotart und Material je Versuch.</summary>
public sealed class ModuleUpgradeDef
{
    /// <summary>Schlüssel des besseren Moduls (gleiche Slotart, nicht größer, nicht das Modul selbst).</summary>
    public string To { get; set; } = "";
    /// <summary>Material je Versuch (Warenschlüssel → Menge) aus dem Stationslager.</summary>
    public Dictionary<string, float> Cost { get; set; } = new();
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
    /// <summary>Bausatz-Warenschlüssel der Klasse (Phase 5a, z. B. ship_kit_light); fehlt = Regel <c>shipKitGoodKey</c>.</summary>
    public string? Kit { get; set; }
    /// <summary>Phase 6: Wirkungsgrad der Werkzeug-Slots (Laser-Trefferchance × Wert); fehlt = 1. Muss in (0, 1] liegen.</summary>
    public float ToolEfficiency { get; set; } = 1f;
    /// <summary>Phase 7: Höchstzahl der Slot-Upgrades (Bonusslots + vergrößerte Slots) je Schiff; fehlt = 1, muss ≥ 0 sein.</summary>
    public int MaxSlotUpgrades { get; set; } = 1;
}

public sealed class ShipModelDef
{
    public string Key { get; set; } = "";
    /// <summary>Klassen-Schlüssel.</summary>
    public string Class { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Modulschlüssel je Slot der Klasse; null = leerer Slot; Länge = Slotanzahl der Klasse.</summary>
    public List<string?> Fit { get; set; } = new();
    /// <summary>Phase 6: KI-Aufgaben dieses Modells ("trade", "mine"); fehlt = keine (Spielermodell, die KI kauft es nicht).</summary>
    public List<string>? Jobs { get; set; }
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
    /// <summary>
    /// Lager ohne Rezept (Phase 5a): Ware → [Anfangsbestand, Wunschbestand]. Legt zusätzlich zu den Rezepten Markteinträge an;
    /// für Rezeptwaren setzt der Eintrag Anfangs-/Wunschbestand. Fehlt das Feld, gibt es keine.
    /// </summary>
    public Dictionary<string, float[]>? Stocks { get; set; }
    /// <summary>Modulfabrik (Phase 5a): erzeugt je passendem Modul ein Rezept aus dessen <c>cost</c>; fehlt = keine Modulfabrik.</summary>
    public ModuleFactoryDef? ModuleFactory { get; set; }
    /// <summary>Schiffshändler (Phase 5c): welche Klassen die Station montiert und verkauft; fehlt = keine Schiffe. Nur sinnvoll mit Dienst "shipdealer".</summary>
    public ShipDealerDef? ShipDealer { get; set; }
    /// <summary>Phase 8: eigene Stückliste für den Bau (Warenschlüssel → Einheiten); fehlt = Regel <c>rules.constructionMaterials</c>.</summary>
    public Dictionary<string, float>? Construction { get; set; }
    /// <summary>Phase 10: Regierungsform (headquarters, company_seat, sector_hq, office, police); fehlt = keine.</summary>
    public string? Government { get; set; }
    /// <summary>Phase 10: Einflussgewicht in der Sektorkontrolle (Standard 1).</summary>
    public float Influence { get; set; } = 1f;
    /// <summary>Phase 10: eroberbar (Standard ja; Regierungsstationen nie, Regel <c>allowStationCapture</c> derzeit aus).</summary>
    public bool Capturable { get; set; } = true;
    /// <summary>Phase 10: zerstörbar (Standard ja; erst mit Kampf relevant).</summary>
    public bool Destructible { get; set; } = true;
}

/// <summary>
/// Schiffshändler eines Stationstyps (Phase 5c, SCHIFFSSLOTS.md „Kleine Schiffe an anderen Stationen“): <see cref="Classes"/> (fehlt = alle),
/// <see cref="MaxSize"/> (Standard XL) und Montagegebühr <see cref="Assembly"/> (Anteil auf Bausatz- und Materialpreis).
/// </summary>
public sealed class ShipDealerDef
{
    public List<string>? Classes { get; set; }
    public string MaxSize { get; set; } = "XL";
    public float Assembly { get; set; }
}

/// <summary>
/// Modulfabrik eines Stationstyps (Phase 5a): Für jedes Modul, dessen Slotart in <see cref="Slots"/> steht und dessen Stufe höchstens
/// <see cref="MaxTier"/> ist (und das eine <c>cost</c> hat), entsteht ein Rezept: Eingänge = <c>cost</c>, Ausgang = 1 Modul-Ware, alle
/// <see cref="Ticks"/> Ticks. Reihenfolge nach Modulschlüssel. Die Ausgänge bekommen den Lagerposten [<see cref="Target"/>, <see cref="Target"/>],
/// die Zutaten [<see cref="InputTarget"/>, <see cref="InputTarget"/>], sofern <c>stocks</c> sie nicht selbst nennt.
/// </summary>
public sealed class ModuleFactoryDef
{
    public List<string> Slots { get; set; } = new();
    public int MaxTier { get; set; } = 1;
    public int Ticks { get; set; } = 20;
    public float Target { get; set; } = 5;
    /// <summary>Wunschbestand der Zutaten (Anfangsbestand = Wunschbestand), sofern <c>stocks</c> sie nicht nennt. Klein halten: Eine Fabrik
    /// ohne Abnehmer hortet sonst bis Wunschbestand × OverstockFactor von jeder Zutat (z. B. Elektronik), die anderen Ketten fehlt.</summary>
    public float InputTarget { get; set; } = 10;
}

public sealed class FactionDef
{
    public int Id { get; set; } public string Name { get; set; } = "";
    /// <summary>Art des Fraktionsakteurs (Phase 9): state, guild, outlaw; fehlt = state.</summary>
    public string? Kind { get; set; }
    /// <summary>Persönlichkeitswerte 0..1 (Phase 9); fehlt = neutral (0,5).</summary>
    public PersonalityDef? Personality { get; set; }
    /// <summary>Hauptbeziehungen zu anderen Fraktionen (Fraktions-Id → −1 … +1); nur Abweichungen von der Vorgabe.</summary>
    public Dictionary<int, float>? Relations { get; set; }
    /// <summary>Startkasse der Fraktion (Phase 9); fehlt = 0.</summary>
    public float Treasury { get; set; }
}

/// <summary>Persönlichkeitswerte eines Akteurs in der Weltdatei (POLITIK.md „Persönlichkeitswerte“).</summary>
public sealed class PersonalityDef
{
    public float Aggression { get; set; } = 0.5f; public float Greed { get; set; } = 0.5f;
    public float Cooperation { get; set; } = 0.5f; public float Lawfulness { get; set; } = 0.5f;
}

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
    /// <summary>Alias für <see cref="BankruptcyProtected"/> (Phase 8: <c>essential</c> bleibt lesbar, damit alte Welten unverändert laden).</summary>
    public bool Essential { get; set; }
    /// <summary>Vor Pleite geschützt (Phase 8; gleichbedeutend mit dem Alias <see cref="Essential"/>).</summary>
    public bool BankruptcyProtected { get; set; }
    public float Credits { get; set; } = 10_000;
    /// <summary>Phase 10: unzerstörbar und nicht eroberbar (Tutorial, Szenario); nur aus der Weltdatei.</summary>
    public bool Immortal { get; set; }
    /// <summary>Ware → [Bestand, Wunschbestand]. Fehlende Waren aus den Rezepten bekommen Standardwerte.</summary>
    public Dictionary<string, float[]> Stock { get; set; } = new();
}

public sealed class GateEndDef { public int Id { get; set; } public int Sector { get; set; } public float[] Pos { get; set; } = { 0, 0 }; }
public sealed class GateLinkDef
{
    public GateEndDef A { get; set; } = new(); public GateEndDef B { get; set; } = new();
    /// <summary>Phase 10: Hauptroute (Gate-Ausnahme von Sektorsperren, höchstens <c>rules.mainRouteMaxLevel</c>).</summary>
    public bool MainRoute { get; set; }
    /// <summary>Phase 10: Anfangszustand des Gate-Paars (open, closed, destroyed); fehlt = offen.</summary>
    public string? State { get; set; }
}

public sealed class ShipGroupDef
{
    public string Role { get; set; } = "trader";
    public int Faction { get; set; }
    /// <summary>
    /// Besitzer (Phase 9): "faction" (Standard) = der Fraktionsakteur <see cref="Faction"/>, "free" = freie Einzelschiffe der Sammelgruppe
    /// „Freie“ (implizite Akteure; <see cref="Faction"/> zählt dann nicht).
    /// </summary>
    public string? Owner { get; set; }
    public int Sector { get; set; }
    public float Credits { get; set; } = 1000;
    public float Cargo { get; set; } = 100;
    public int Count { get; set; } = 1;
    /// <summary>Schiffsmodell-Schlüssel; fehlt das Feld, wählt <c>WorldBuilder.NewShip</c> das Einheitsmodell nach Rolle und <see cref="Cargo"/>.</summary>
    public string? Model { get; set; }
}

/// <summary>Ein Startpaket: Schiffsmodell, Startcredits und optional Startsektor (fehlt = Sektor des Spielereintrags).</summary>
public sealed class StartOptionDef
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Model { get; set; } = "";
    public float Credits { get; set; } = 500;
    public int? Sector { get; set; }
}

public sealed class PlayerDef
{
    public int Faction { get; set; } = 1; public int Sector { get; set; } = 1; public float Credits { get; set; } = 500;
    /// <summary>Schiffsmodell-Schlüssel; fehlt das Feld, wählt <c>WorldBuilder.NewShip</c> das Einheitsmodell der Spielerrolle.</summary>
    public string? Model { get; set; }
    /// <summary>
    /// Startpakete (Phase 5c, SCHIFFE.md Abschnitt 9): wählbar bei "Neues Spiel"; ohne Wahl gilt der Spielereintrag selbst
    /// (Modell <see cref="Model"/> bzw. Einheitsmodell, <see cref="Credits"/>, <see cref="Sector"/>).
    /// </summary>
    public List<StartOptionDef> StartOptions { get; set; } = new();
}
