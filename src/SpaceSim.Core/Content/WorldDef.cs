namespace SpaceSim.Core.Content;

// Datenmodell der Weltdatei (JSON). Reine DTOs: Änderungen am Dateiformat bleiben hier lokalisiert.

public sealed class WorldDef
{
    public ulong Seed { get; set; } = 1;
    /// <summary>
    /// Phase 29: Fraktionen und Akteure bekommen beim Weltaufbau gewürfelte Namen ihrer Art aus der Namensliste (<see cref="FactionNames"/>); Stationsnamen mit
    /// <c>{owner}</c> nehmen den Namen des Besitzers auf. false = Namen aus der Weltdatei (ein leerer Name wird trotzdem gewürfelt).
    /// </summary>
    public bool RandomNames { get; set; }
    public Model.EconomyRules? Rules { get; set; }
    public List<GoodDef> Goods { get; set; } = new();
    public List<AsteroidClassDef> AsteroidClasses { get; set; } = new();
    public List<StationTypeDef> StationTypes { get; set; } = new();
    public List<FactionDef> Factions { get; set; } = new();
    /// <summary>Phase 11: weitere Akteure ohne Fraktion (z. B. Piraten, Art outlaw); Stationen und Schiffsgruppen verweisen über <c>owner</c> auf den Schlüssel.</summary>
    public List<ActorDef> Actors { get; set; } = new();
    public List<SectorDef> Sectors { get; set; } = new();
    public List<GateLinkDef> Gates { get; set; } = new();
    /// <summary>Wurmlöcher: stabile Paare (zwei Enden mit Tor-Id, Sektor, Lage im Grundmaßstab); nur mit Regel <c>wormholes</c> in der Welt.</summary>
    public List<WormholeDef> Wormholes { get; set; } = new();
    /// <summary>Phase 31: Torklassen (Kampfwerte der Tore); fehlt = Tore ohne Kampfwerte.</summary>
    public List<GateTypeDef> GateTypes { get; set; } = new();
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
    /// <summary>Phase 13: Forschungsprojekte (FORSCHUNG.md „Architektur“); fehlt der Abschnitt, ist alles frei.</summary>
    public List<ResearchProjectDef> Research { get; set; } = new();
}

/// <summary>
/// Forschungsprojekt der Weltdatei (Phase 13): Schlüssel, Name, Beschreibung, nötige FP (<c>points</c>), Voraussetzungen (<c>requires</c>, nur
/// früher aufgeführte Projekte), Freischaltungen (<c>unlocks</c>: "module:&lt;key&gt;", "model:&lt;key&gt;", "stationType:&lt;key&gt;") und
/// Zusatzverbrauch je Forschungstakt (<c>inputs</c>: Warenschlüssel → Einheiten).
/// </summary>
public sealed class ResearchProjectDef
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public float Points { get; set; } = 1000;
    public List<string> Requires { get; set; } = new();
    public List<string> Unlocks { get; set; } = new();
    public Dictionary<string, float> Inputs { get; set; } = new();
}

/// <summary>
/// Forschungsbetrieb eines Stationstyps (Phase 13, nur mit <c>government: "research"</c>): alle <c>ticks</c> Ticks ein Takt mit <c>points</c> FP,
/// Personalkosten <c>credits</c> und Grundverbrauch <c>inputs</c> (Warenschlüssel → Einheiten); Budget aus der Fraktionskasse bis <c>fundTarget</c>,
/// höchstens <c>fundPerTick</c> je Tick.
/// </summary>
public sealed class ResearchConfigDef
{
    public int Ticks { get; set; } = 10;
    public float Points { get; set; } = 5;
    public float Credits { get; set; }
    public Dictionary<string, float> Inputs { get; set; } = new();
    public float FundTarget { get; set; }
    public float FundPerTick { get; set; }
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
    /// <summary>Phase 14: Warenkategorie (z. B. ore, refined, component, end, kit, fuel); fehlt = keine. Modul-Waren bekommen automatisch "module".</summary>
    public string? Category { get; set; }
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
    /// <summary>Raketenlenkung des Raketenwerfers: optical, infrared, radar_semi, radar_active; fehlt = keine (altes Verhalten).</summary>
    public string? Guidance { get; set; }
    /// <summary>
    /// Phase 24: Schadensart einer Waffe (energy/kinetic). Waffenwerte stehen in <see cref="Stats"/>: damage, reload, range, accuracy, tracking, ammoPerShot.
    /// </summary>
    public string? DamageType { get; set; }
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
    /// <summary>Phase 29: Bausätze je Schiff (Standard 1).</summary>
    public int KitCount { get; set; } = 1;
    /// <summary>Träger: Hangarplätze für Bordjäger (Klasse S); fehlt = 0.</summary>
    public int HangarBays { get; set; }
    /// <summary>Phase 6: Wirkungsgrad der Werkzeug-Slots (Laser-Trefferchance × Wert); fehlt = 1. Muss in (0, 1] liegen.</summary>
    public float ToolEfficiency { get; set; } = 1f;
    /// <summary>Phase 7: Höchstzahl der Slot-Upgrades (Bonusslots + vergrößerte Slots) je Schiff; fehlt = 1, muss ≥ 0 sein.</summary>
    public int MaxSlotUpgrades { get; set; } = 1;
    /// <summary>Phase 11: nicht im freien Handel — Schiffshändler verkaufen die Klasse nur, wenn ihre Liste sie nennt (Kaperschiffe).</summary>
    public bool Restricted { get; set; }
    /// <summary>Phase 12: Liegegebühr je Tag an fremden Stationen; fehlt = nach Größe (Regel berthFeeBySize). Muss ≥ 0 sein.</summary>
    public float BerthFee { get; set; }
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
    /// <summary>Dienst-Schlüssel, die dieser Typ anbietet; fehlt das Feld, ist der Standard ["trade", "hangar"] (Handel; seit Phase 12 auch Hangar).</summary>
    public List<string> Services { get; set; } = new() { SpaceSim.Core.Services.TradeService.ServiceKey, SpaceSim.Core.Services.HangarService.ServiceKey };
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
    /// <summary>Phase 11: Grundschutz auf der Lagekarte (Wache, Hauptquartier, Sektor-HQ); Standard 0.</summary>
    public float Security { get; set; }
    /// <summary>Phase 11: Piratenversteck (Heimat der Kaperschiffe, zählt nicht gegen die Bau-Obergrenzen); Standard nein.</summary>
    public bool Hideout { get; set; }
    /// <summary>Phase 13: Forschungsbetrieb (nur mit <c>government: "research"</c>); fehlt = keine Forschungsstation.</summary>
    public ResearchConfigDef? Research { get; set; }
    /// <summary>Phase 14: Exportsenke (Handelsstation an einem Planeten); fehlt = keine. Stationen dieses Typs nur in Sektoren mit Planet.</summary>
    public ExportConfigDef? Export { get; set; }
    /// <summary>Phase 28: Stationsverteidigung (Hülle, Schild, Panzerung, Geschütze, Punktabwehr); fehlt = Grundhülle ohne Geschütze.</summary>
    public StationDefenseDef? Defense { get; set; }
    /// <summary>Phase 30: Größenklasse XS/S/M/L/XL (Vorgabe M) und — Pflicht bei XS, sonst optional — eigener Körperradius.</summary>
    public string? Size { get; set; }
    public float? Radius { get; set; }
}

/// <summary>Phase 28: Verteidigung eines Stationstyps (JSON <c>defense</c>): Geschütze sind Schlüssel von Waffenmodulen.</summary>
public sealed class StationDefenseDef
{
    public float Hull { get; set; }
    public float Shield { get; set; }
    public float ShieldRegen { get; set; }
    public float Armor { get; set; }
    public float PointDefense { get; set; }
    public List<string> Guns { get; set; } = new();
}

/// <summary>
/// Exportsenke eines Stationstyps (Phase 14): Die Station kauft die Waren der genannten Kategorien (<see cref="Categories"/>, z. B. "end") und/oder
/// die einzeln genannten Waren (<see cref="Goods"/>) über den normalen Handel an; jede Lieferung wird zu einer Charge, die nach
/// <see cref="DelayTicks"/> an den Planeten geht (fehlt = Regel <c>exportDelayTicks</c>) und Ankaufpreis × (1 + <see cref="Markup"/>) einbringt
/// (fehlt = Regel <c>exportMarkup</c>). <see cref="Target"/> ist der Wunschbestand je Exportware (Preisformel über den Bestand inkl. wartender Chargen).
/// </summary>
public sealed class ExportConfigDef
{
    public List<string> Categories { get; set; } = new();
    public List<string> Goods { get; set; } = new();
    public int? DelayTicks { get; set; }
    public float? Markup { get; set; }
    public float Target { get; set; } = 20f;
    /// <summary>Wunschbestand als Warenwert in Credits (Basispreis); &gt; 0 = je Ware max(1, round(targetValue ÷ Basispreis)) statt <see cref="Target"/> —
    /// teure Waren (Luxusgüter) bekommen kleine, billige (Nahrung) große Wunschbestände, so passt eine Ladung zur Kasse der Station.</summary>
    public float TargetValue { get; set; }
    /// <summary>
    /// Phase 17: höchstes Gebot je Ware oder Kategorie als Vielfaches des Basispreises (statt <see cref="Model.EconomyRules.ExportMaxBidFactor"/>);
    /// Schlüssel = Warenschlüssel (gewinnt) oder Kategorie der Senke, Wert in (0, 4].
    /// </summary>
    public Dictionary<string, float>? Bids { get; set; }
}

/// <summary>Phase 14: Planet eines Sektors (Kulisse und Abnehmer der Exportsenke): Name, optional Farbe und Größe für die Ansicht.</summary>
public sealed class PlanetDef
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#5a7fa8";
    public float Radius { get; set; } = 400f;
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

/// <summary>Akteur ohne Fraktion in der Weltdatei (Phase 11): Schlüssel für Verweise, Name, Art (outlaw, independent), Persönlichkeit, Startkasse.</summary>
public sealed class ActorDef
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "outlaw";
    public PersonalityDef? Personality { get; set; }
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
    /// <summary>Phase 14: Planet des Sektors; fehlt = keiner (dann sind keine Exportstationen erlaubt).</summary>
    public PlanetDef? Planet { get; set; }
    /// <summary>
    /// Runde 44: Maßstab des Sektors (1–10, Standard 1). Die Koordinaten in der Datei bleiben im Grundmaßstab; der Weltaufbau multipliziert
    /// Felder, Stationen und Tor-Enden mit dem Faktor, die Hintergrundfelsen bekommen Fläche und Anzahl × Faktor.
    /// </summary>
    public float Scale { get; set; } = 1f;
    /// <summary>
    /// Himmelskugel (Skybox) des Sektors: Schlüssel eines Himmels der Darstellung (Godot: <c>assets/skyboxes/skyboxes.json</c>); fehlt = keiner
    /// (dann der einfache Hintergrund). Reine Darstellung — nicht im <c>UniverseHash</c>.
    /// </summary>
    public string? Skybox { get; set; }
    /// <summary>Drehung des Himmels um die Hochachse in Grad (Abwechslung bei gleichem Himmel, Lage des Planeten im Blick); Standard 0.</summary>
    public float SkyboxYaw { get; set; }
    /// <summary>Gasernte: der ganze Sektor liegt in einem Nebel (Wirkungen mit Regel <c>nebulaEffects</c>); Standard false.</summary>
    public bool Nebula { get; set; }
    /// <summary>Gasernte: Gasfelder (Lage im Grundmaßstab wie Felder; Radius × Maßstab); fehlt = keine.</summary>
    public List<GasFieldDef> GasFields { get; set; } = new();
    /// <summary>Sonne des Sektors (ERKUNDUNG.md „Sonnen“; nur mit Regel <c>suns</c> in der Welt); fehlt = keine.</summary>
    public SunDef? Sun { get; set; }
}

/// <summary>Sonne eines Sektors: Lage und Radius im Grundmaßstab (beide × Sektormaßstab), Farbe #RRGGBB, Sternart (Anzeige).</summary>
public sealed class SunDef
{
    public float[] Pos { get; set; } = { 0, 0 };
    public float Radius { get; set; } = 30f;
    public string Color { get; set; } = "#ffe6a8";
    public string Kind { get; set; } = "yellow";
}

/// <summary>Gasfeld eines Sektors (Gasernte).</summary>
public sealed class GasFieldDef
{
    public float[] Pos { get; set; } = { 0, 0 };
    /// <summary>Radius im Grundmaßstab (wird × Sektormaßstab genommen).</summary>
    public float Radius { get; set; } = 40f;
    /// <summary>Dichte: Faktor auf die Sammelrate (Standard 1).</summary>
    public float Density { get; set; } = 1f;
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
    /// <summary>Phase 11: Besitzer als Akteur-Schlüssel aus <c>actors</c> (z. B. Piratenversteck); fehlt = Fraktion des Sektors.</summary>
    public string? Owner { get; set; }
}

public sealed class GateEndDef { public int Id { get; set; } public int Sector { get; set; } public float[] Pos { get; set; } = { 0, 0 }; }
/// <summary>Stabiles Wurmloch: zwei Enden (Ids wie Tore, eindeutig über alle Tore; Lage × Sektormaßstab).</summary>
public sealed class WormholeDef { public GateEndDef A { get; set; } = new(); public GateEndDef B { get; set; } = new(); }

public sealed class GateLinkDef
{
    public GateEndDef A { get; set; } = new(); public GateEndDef B { get; set; } = new();
    /// <summary>Phase 10: Hauptroute (Gate-Ausnahme von Sektorsperren, höchstens <c>rules.mainRouteMaxLevel</c>).</summary>
    public bool MainRoute { get; set; }
    /// <summary>Phase 10: Anfangszustand des Gate-Paars (open, closed, destroyed); fehlt = offen.</summary>
    public string? State { get; set; }
    /// <summary>Phase 31: Torklasse (<c>gateTypes[].key</c>); fehlt = „main“ bei Hauptroute, sonst „side“ (wenn vorhanden).</summary>
    public string? Type { get; set; }
}

/// <summary>Phase 31: Torklasse — Verteidigung wie <c>stationTypes[].defense</c>, Reparatur je Tag, Frist bis zum Wiederanlauf ohne Kasse.</summary>
public sealed class GateTypeDef
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public StationDefenseDef? Defense { get; set; }
    public float RepairPerDay { get; set; }
    public long MaxOutageTicks { get; set; }
}

public sealed class ShipGroupDef
{
    public string Role { get; set; } = "trader";
    public int Faction { get; set; }
    /// <summary>
    /// Besitzer (Phase 9): "faction" (Standard) = der Fraktionsakteur <see cref="Faction"/>, "free" = freie Einzelschiffe der Sammelgruppe
    /// „Freie“ (implizite Akteure; <see cref="Faction"/> zählt dann nicht). Phase 11: sonst der Schlüssel eines Akteurs aus <c>actors</c> (z. B. Piraten).
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
    /// <summary>
    /// Welle A: zusätzliche Module des Startschiffs — je Modul ein Bonus-Slot seiner Slotart und Größe, gleich bestückt (z. B. ein kleiner Frachtraum für
    /// den Jäger). Geprüft mit <c>FitRules</c>; fehlt = keine.
    /// </summary>
    public List<string>? ExtraModules { get; set; }
    /// <summary>Welle A: eigener Fit der Klassen-Slots des Startschiffs (vor den Zusatzmodulen; null-Einträge = leer); fehlt = Modell-Fit.</summary>
    public List<string?>? Fit { get; set; }
    /// <summary>Gerätetest (Phase 26): Testszenario — klar als Test markierter Start mit vorbereiteter Lage (siehe <see cref="ScenarioDef"/>); null = normales Paket.</summary>
    public ScenarioDef? Scenario { get; set; }
}

/// <summary>
/// Gerätetest (Phase 26): vorbereitete Lage eines Testszenarios — damit lassen sich Kampf, Schaden, Reparatur und Bergung in Minuten per Touch prüfen.
/// Alles optional; angewandt von <see cref="StartScenario.Apply"/>.
/// </summary>
public sealed class ScenarioDef
{
    /// <summary>Ausbau Punkt 16: Testszenarien kennen alles (fehlt = true); <c>false</c> = beschränktes Wissen wie die regulären Startpakete.</summary>
    public bool? KnowAll { get; set; }
    /// <summary>Eigener Fit des Startschiffs (Modulschlüssel je Slot), geprüft mit <c>FitRules</c>.</summary>
    public List<string?>? Fit { get; set; }
    /// <summary>Stationstyp, neben dem das Schiff startet (im Startsektor, sonst irgendwo).</summary>
    public string? Station { get; set; }
    /// <summary>Beschädigte Module (Modulschlüssel → Zustand 0..1).</summary>
    public Dictionary<string, float>? Damage { get; set; }
    /// <summary>Modul, das beim Start brennt.</summary>
    public string? Fire { get; set; }
    /// <summary>Ein Wrack in Bergungsreichweite (Fracht und ein bergbares Modul).</summary>
    public bool Wreck { get; set; }
    /// <summary>Ein Kaperschiff (Gesetzloser) in Sichtweite.</summary>
    public bool Outlaw { get; set; }
    /// <summary>Fracht beim Start (Warenschlüssel → Einheiten), z. B. Munition.</summary>
    public Dictionary<string, float>? Cargo { get; set; }
    /// <summary>Gerätetest Phase 27: Startabstand vom Andockpunkt der Station (Standard 20; für Überfälle weiter weg, außerhalb des Stationsschutzes).</summary>
    public float? Distance { get; set; }
    /// <summary>Gerätetest Phase 27: so viele Kaperschiffe einer Gruppe als Rudel in Waffenreichweite (zu zweit stärker als ein Jäger: sie greifen an statt zu fliehen).</summary>
    public int Raiders { get; set; }
    /// <summary>Gerätetest Phase 28: Hüllenanteil der Kaperschiffe (angeschlagen, 1 = heil) — der Gefechtstest soll für einen Menschen am Handy bestehbar sein.</summary>
    public float RaiderHull { get; set; } = 1f;
    /// <summary>Nach so vielen Ticks überfällt das Rudel den Spieler (angekündigt; 0 = kein Überfall).</summary>
    public int RaidAfterTicks { get; set; }
    /// <summary>Ein KI-Händler mit Fracht frei im Raum in Waffenreichweite (nicht angedockt).</summary>
    public bool Trader { get; set; }
    /// <summary>Zwei Streifen der Sektorfraktion als Verband im Sektor.</summary>
    public bool PoliceSquadron { get; set; }
    /// <summary>Fahndungsstufe des Spielers bei der Sektorfraktion (0 = sauber; 3 = gesucht → Festnahme-Warnung, sobald eine Streife im Sektor ist).</summary>
    public int Wanted { get; set; }
    /// <summary>Zweiter Gerätetest Phase 28: zusätzliche Slots (Slotart, Größe S) hinter den Klassenslots — z. B. ein Frachtslot für den Falken im Gefechtstest.</summary>
    public List<string>? BonusSlots { get; set; }
    /// <summary>Zweiter Gerätetest Phase 28: der erste Kaperer (Anführer) ohne Schildmodul (angeschlagen, für Menschen bestehbar).</summary>
    public bool LeadWithoutShield { get; set; }
    /// <summary>
    /// Zweiter Gerätetest Phase 28: &gt; 0 = die übrigen Kaperer bilden kein Rudel, sondern stoßen erst so viele Ticks nach dem Überfallbeginn als Verstärkung
    /// dazu (eigene angekündigte Überfälle, die einem laufenden Gefecht beitreten); 0 = Rudel wie bisher.
    /// </summary>
    public int ReinforceAfterTicks { get; set; }
    /// <summary>Zweiter Gerätetest Phase 28: Hüllenanteil der später dazustoßenden Kaperer (1 = heil).</summary>
    public float ReinforceHull { get; set; } = 1f;
    /// <summary>
    /// Runde 13 (Handy-Test: Gefechtstest nicht bestehbar): die Verstärkung ebenfalls ohne Schildmodul — mit Schild (40) und 60 % Hülle überstand sie auf
    /// manchen Seeds den schon angeschlagenen Falken, dessen Hülle zwischen den Überfällen nicht nachwächst.
    /// </summary>
    public bool ReinforceWithoutShield { get; set; }
    /// <summary>
    /// Runde 19 („Ankertest“): Asteroidenklasse — das Schiff startet mit dem Bug am größten abbaubaren Fels dieser Klasse im Startsektor (freie Seite, Bug in Kontakt),
    /// sodass ein Kontaktschürfer ohne Automatik sofort verankert.
    /// </summary>
    public string? AtRock { get; set; }
    /// <summary>Runde 19: Erzvorrat dieses Felsens (Vorrat und Höchstwert; 0 = unverändert) — reicht für minutenlangen Abbau.</summary>
    public float RockOre { get; set; }
    /// <summary>Runde 19: ruhige Lage — keine Überfälle auf das aktive Spielerschiff in diesem Spielstand (setzt die gespeicherte Regel PlayerRaids aus; Überfälle auf Flottenschiffe bleiben).</summary>
    public bool Calm { get; set; }
    /// <summary>
    /// Runde 44h („Bergbautest“): &gt; 0 = statt des größten nimmt <see cref="AtRock"/> den Fels der Klasse, dessen Radius diesem am nächsten liegt, verkleinert ihn
    /// höchstens auf diesen Radius und setzt seinen Erzvorrat <b>genau</b> auf <see cref="RockOre"/> (kleiner Fels, schnell leer → zerbricht).
    /// </summary>
    public float RockRadius { get; set; }
    /// <summary>Runde 44h: ein zweiter kleiner Fels derselben Klasse (gleicher Radius und Erzvorrat) gleich neben dem ersten — zum Weitermachen nach dem Zerbrechen.</summary>
    public bool SecondRock { get; set; }
    /// <summary>
    /// Runde 44h: KI-Kontaktschürfer (Modellschlüssel) frei im Raum, 40–60 Einheiten vom Spieler; jeder fliegt von selbst einen nahen Fels an und bohrt ihn an
    /// (Core-Schürfer mit Anflug, kein Versetzen). Feste Richtungen, kein Zufall.
    /// </summary>
    public List<string>? NpcMiners { get; set; }
    /// <summary>
    /// Runde 24: die Fraktionen kaufen in den ersten so vielen Ticks weder Kriegsschiffe noch zusätzliche Polizei (danach noch die Wechselsperre) — der Kriegstest
    /// prüft den Verband gegen die Verteidigung, die da ist; seit die Werften am Start liefern, hätte die Union sonst gleich zwei Kriegsschiffe nachgekauft.
    /// </summary>
    public int? PoliceHoldTicks { get; set; }
    /// <summary>Gerätetest Phase 28 (Runde 4): Fracht des ersten Kaperers (Beute an Bord) — sein Wrack hat dann Fracht für den Traktorstrahl.</summary>
    public Dictionary<string, float>? LeadCargo { get; set; }
    /// <summary>
    /// Gerätetest Phase 28 (Runde 5): eigene Begleitschiffe (Modellschlüssel) neben dem Spieler — ein Verband mit dem aktiven Schiff als Anführer und Regel
    /// „Angriff“; sie treten jedem Gefecht des Spielers bei (auch einem Stationsangriff).
    /// </summary>
    public List<string>? Wingmen { get; set; }
    /// <summary>Träger: Bordjäger (Modellschlüssel, Klasse S), die das Startschiff — ein Träger — im Hangar hat.</summary>
    public List<string>? CarrierFighters { get; set; }
    /// <summary>Hilfe und Tutorial: das Paket ist das Tutorial — Schritte aus <c>help.json</c>, Tutorialstation = <see cref="Station"/>; erscheint bei den regulären Paketen.</summary>
    public bool Tutorial { get; set; }
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
