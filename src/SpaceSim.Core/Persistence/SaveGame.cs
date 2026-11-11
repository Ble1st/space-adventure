using System.Text.Json.Serialization;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

// Speicherformat (JSON). Bewusst getrennt vom Laufzeitmodell: Das Modell darf sich ändern, das Format wird versioniert
// und über Migrationen in SaveGameJson angehoben. Ids sind einfache ints, Dictionaries nutzen int-Schlüssel.

public sealed class SaveGame
{
    public const int CurrentVersion = 17;

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
    /// <summary>
    /// Spielerkasse (Phase 5b, v7 bis v11); ältere Stände bekommen sie per Migration aus den Credits des Spielerschiffs. Seit v12 ist sie die Kasse
    /// des Spielerakteurs (<see cref="Actors"/>) und wird nur noch gelesen (Migration v11→v12).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PlayerWalletSave? PlayerWallet { get; set; }
    /// <summary>Akteure (Phase 9, v12): Fraktionsakteure, Sammelgruppe „Freie“, Spieler, aufgestiegene Einzelakteure.</summary>
    public List<ActorSave> Actors { get; set; } = new();
    /// <summary>Id des Spielerakteurs und der Sammelgruppe „Freie“ (v12).</summary>
    public int PlayerActor { get; set; }
    public int FreeActor { get; set; }
    public int NextActorId { get; set; }

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
    /// <summary>Phase 11 (v14): angekündigte Begegnungen mit dem Spieler (Überfall, Abfangen); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<PendingEncounterSave>? Encounters { get; set; }
    /// <summary>Phase 12 (v15): Verbände; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<SquadronSave>? Squadrons { get; set; }
    /// <summary>Phase 12 (v15): nächste freie Verbands-Id.</summary>
    public int NextSquadronId { get; set; } = 1;
    /// <summary>Phase 13 (v16): Forschungsprojekte (Content wie Rezepte und Module); fehlt = keine Forschung, alles frei.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ResearchProjectSave>? Research { get; set; }
}

/// <summary>Forschungsprojekt (Phase 13, v16): Freischaltungen als Text („module:key“ …), Zusatzverbrauch als GoodId → Einheiten.</summary>
public sealed class ResearchProjectSave
{
    public string Key { get; set; } = ""; public string Name { get; set; } = ""; public string Description { get; set; } = "";
    public float Points { get; set; }
    public List<string> Requires { get; set; } = new();
    public List<string> Unlocks { get; set; } = new();
    public Dictionary<int, float> Inputs { get; set; } = new();
}

/// <summary>Forschungsbetrieb eines Stationstyps (Phase 13, v16).</summary>
public sealed class ResearchConfigSave
{
    public int Ticks { get; set; } public float Points { get; set; } public float Credits { get; set; }
    public Dictionary<int, float> Inputs { get; set; } = new();
    public float FundTarget { get; set; } public float FundPerTick { get; set; }
}

/// <summary>Forschungsstand einer Fraktion (Phase 13, v16).</summary>
public sealed class ResearchStateSave
{
    public string? Current { get; set; } public bool Commissioned { get; set; }
    public Dictionary<string, float> Progress { get; set; } = new();
    public Dictionary<string, long> Completed { get; set; } = new();
    public float PointsTotal { get; set; } public float Spent { get; set; } public float PlayerFunded { get; set; }
}

/// <summary>Verband (Phase 12, v15): Besitzer, Name, Anführer, Mitglieder (Anführer zuerst), Formation, Heimatbasis, Verhaltensregel.</summary>
public sealed class SquadronSave
{
    public int Id { get; set; } public int Owner { get; set; } public string Name { get; set; } = "";
    public int Leader { get; set; } public List<int> Members { get; set; } = new();
    public string Formation { get; set; } = ""; public int HomeBase { get; set; } public string Rules { get; set; } = "";
}

/// <summary>
/// Ein Akteur (Phase 9, v12): Art, Mitgliedschaft (Fraktions-Id, null = unabhängig), Kasse, gemeinsame Kasse für Schiffe, Persönlichkeit,
/// persönliche Beziehungsabweichungen (Akteur-Id → Wert, nur Abweichungen) und Anlagetick.
/// </summary>
public sealed class ActorSave
{
    public int Id { get; set; } public string Name { get; set; } = ""; public string Kind { get; set; } = "";
    public int? Membership { get; set; }
    public float Credits { get; set; }
    public bool ShipsUseTreasury { get; set; }
    public float Aggression { get; set; } = 0.5f; public float Greed { get; set; } = 0.5f;
    public float Cooperation { get; set; } = 0.5f; public float Lawfulness { get; set; } = 0.5f;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<int, float>? Relations { get; set; }
    public long Created { get; set; }
    /// <summary>Phase 11 (v14): Fahndung je Fraktion; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<WantedSave>? Wanted { get; set; }
    /// <summary>Phase 11 (v14): offenes Kopfgeld je Fraktion; fehlt = keins.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<int, float>? BountyClaims { get; set; }
    /// <summary>Phase 11 (v14): letzte Änderung der Streitkräfte (Abkühlzeit).</summary>
    public long LastForceTick { get; set; } = -1_000_000;
}

/// <summary>Kasse der Spielerperson (Phase 5b, v7 bis v11; seit v12 nur noch gelesen).</summary>
/// <summary>Fahndungseintrag (Phase 11, v14).</summary>
public sealed class WantedSave
{
    public int Faction { get; set; } public float Heat { get; set; } public long LastCrime { get; set; } public float Fine { get; set; }
    public string Crime { get; set; } = "";
}

/// <summary>Angekündigte Begegnung (Phase 11, v14).</summary>
public sealed class PendingEncounterSave
{
    public string Kind { get; set; } = ""; public int Source { get; set; } public int Target { get; set; } public int Sector { get; set; } public long Due { get; set; }
}

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
    /// <summary>Phase 14 (v17): Warenkategorie; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Category { get; set; }
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
    /// <summary>Schiffshändler (Phase 5c, v8); fehlt = Station verkauft keine Schiffe.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShipDealerSave? ShipDealer { get; set; }
    /// <summary>Eigene Stückliste für den Bau (Phase 8, v11; GoodId → Einheiten); fehlt = Regel.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<int, float>? Construction { get; set; }
    /// <summary>Regierungsform (Phase 10, v13: headquarters, company_seat, sector_hq, office, police); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Government { get; set; }
    /// <summary>Einflussgewicht (Phase 10, v13); fehlt = 1.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Influence { get; set; }
    /// <summary>Eroberbar (Phase 10, v13); fehlt = ja.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Capturable { get; set; }
    /// <summary>Zerstörbar (Phase 10, v13); fehlt = ja.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Destructible { get; set; }
    /// <summary>Phase 11 (v14): Grundschutz auf der Lagekarte; fehlt = 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Security { get; set; }
    /// <summary>Phase 11 (v14): Piratenversteck; fehlt = nein.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Hideout { get; set; }
    /// <summary>Phase 13 (v16): Forschungsbetrieb; fehlt = keine Forschungsstation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ResearchConfigSave? Research { get; set; }
    /// <summary>Phase 14 (v17): Exportsenke; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ExportConfigSave? Export { get; set; }
}

/// <summary>Exportsenke eines Stationstyps (Phase 14, v17): Waren (GoodIds), Kategorien, Verzögerung/Aufschlag (null = Regel), Wunschbestand.</summary>
public sealed class ExportConfigSave
{
    public List<int> Goods { get; set; } = new(); public List<string> Categories { get; set; } = new();
    public int? DelayTicks { get; set; } public float? Markup { get; set; } public float Target { get; set; } public float TargetValue { get; set; }
}

/// <summary>Charge einer Exportsenke (Phase 14, v17): Ware, Menge, bezahlter Betrag, Fälligkeit.</summary>
public sealed class ExportBatchSave { public int Good { get; set; } public float Amount { get; set; } public float Paid { get; set; } public long Due { get; set; } }

/// <summary>Planet eines Sektors (Phase 14, v17).</summary>
public sealed class PlanetSave { public string Name { get; set; } = ""; public string Color { get; set; } = ""; public float Radius { get; set; } }

/// <summary>Schiffshändler eines Stationstyps (Phase 5c): erlaubte Klassen (null = alle), Höchstgröße als Text, Montagegebühr.</summary>
public sealed class ShipDealerSave
{
    public List<string>? Classes { get; set; }
    public string MaxSize { get; set; } = "XL";
    public float Assembly { get; set; }
}

/// <summary>Lagerposten eines Stationstyps (Phase 5a): GoodId, Anfangs- und Wunschbestand.</summary>
public sealed class StationStockSave { public int Good { get; set; } public float Amount { get; set; } public float Target { get; set; } }

public sealed class FactionSave
{
    public int Id { get; set; } public string Name { get; set; } = "";
    public Dictionary<int, float> Relations { get; set; } = new();
    public List<int> Territory { get; set; } = new();
    /// <summary>Tick des letzten Notbaus (Phase 8, v11); fehlt = lange her.</summary>
    public long LastEmergencyBuildTick { get; set; } = -1_000_000;
    /// <summary>Phase 13 (v16): Forschungsstand; fehlt = nichts erforscht.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ResearchStateSave? Research { get; set; }
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

    /// <summary>Sperre des Sektors (Phase 10, v13); fehlt = offen ohne Zeitstempel.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SectorAccessSave? Access { get; set; }
    /// <summary>Einflussanteile je Fraktions-Id (Phase 10, v13); fehlt = noch nicht berechnet.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<int, float>? Influence { get; set; }
    /// <summary>Kontrollstufe des Besitzers (Phase 10, v13: None/Presence/Influence/Dominance/Control); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Control { get; set; }
    /// <summary>Umkämpft (Phase 10, v13).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Contested { get; set; }
    /// <summary>Phase 11 (v14): Lagekarte — Bedrohung, Sicherheit, Verkehrswert; fehlt = 0 (Sicherheit und Verkehr misst das Laden neu).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float Threat { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float Security { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float Traffic { get; set; }
    /// <summary>Phase 14 (v17): Planet; fehlt = keiner.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PlanetSave? Planet { get; set; }
}

/// <summary>Sperre eines Sektors (Phase 10): Stufen für Ein- und Ausreise, Auslöser, Zeiten (Beginn, Ablauf, Abkühlzeit), Durchsetzung.</summary>
public sealed class SectorAccessSave
{
    public int Entry { get; set; } public int Exit { get; set; }
    public int? Against { get; set; }
    public long Since { get; set; } = -1_000_000; public long Expires { get; set; } = -1; public long Cooldown { get; set; } = -1_000_000;
    public bool Enforced { get; set; } = true;
}

public sealed class GateSave
{
    public int Id { get; set; } public int Sector { get; set; } public Vec2 Pos { get; set; } public int Target { get; set; }
    /// <summary>Physisch passierbar (bis v12 der einzige Zustand; seit v13 aus <see cref="State"/> abgeleitet und weiter geschrieben).</summary>
    public bool Open { get; set; }
    /// <summary>Zustand (Phase 10, v13: Open/Restricted/Closed/Destroyed); fehlt = aus <see cref="Open"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? State { get; set; }
    /// <summary>Hauptroute (Phase 10, v13; Gate-Ausnahme von Sektorsperren).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool MainRoute { get; set; }
}

public sealed class StockSave { public float Amount { get; set; } public float Target { get; set; } public float Price { get; set; } }

public sealed class StationSave
{
    public int Id { get; set; } public string Type { get; set; } = ""; public string Name { get; set; } = "";
    public int Sector { get; set; } public int Owner { get; set; } public Vec2 Pos { get; set; }
    public float Credits { get; set; } public long BelowLimitSince { get; set; }
    /// <summary>Vor Pleite geschützt (Phase 8, v11; früher <see cref="Essential"/>).</summary>
    public bool BankruptcyProtected { get; set; }
    /// <summary>Unzerstörbar und nicht eroberbar (Phase 10, v13; nur aus der Weltdatei).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Immortal { get; set; }
    /// <summary>Altes Feld (bis v10): gleichbedeutend mit <see cref="BankruptcyProtected"/>; wird nur noch gelesen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Essential { get; set; }
    public List<RecipeSave> Recipes { get; set; } = new();
    public Dictionary<int, StockSave> Market { get; set; } = new();
    /// <summary>Baustelle (Phase 8, v11); fehlt = fertige Station. Zusammen bilden diese Einträge die Baustellenliste des Spielstands.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BuildSiteSave? Site { get; set; }
    /// <summary>Persönliches Lager des Spielers (Phase 8, v11, Dienst „storage“; GoodId → Menge); fehlt = leer.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<int, float>? PlayerStorage { get; set; }
    /// <summary>Phase 14 (v17): wartende Chargen der Exportsenke in Ankaufsreihenfolge; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ExportBatchSave>? Exports { get; set; }
    /// <summary>Phase 15 (additiv, v17 bleibt): offene Instandhaltung (Ware → fehlende Einheiten); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<int, float>? MaintenanceBacklog { get; set; }
}

/// <summary>Baustelle (Phase 8): Zieltyp, Stückliste, Investor, Treuhand-Bausumme, Bauschiff, Zeiten und Zustand.</summary>
public sealed class BuildSiteSave
{
    public string Type { get; set; } = "";
    public Dictionary<int, float> Required { get; set; } = new();
    public int InvestorStation { get; set; } public int InvestorShip { get; set; }
    /// <summary>Akteur, dessen Kasse gezahlt hat (Phase 9, v12); fehlt = keiner.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int InvestorActor { get; set; }
    public float Budget { get; set; } public int Builder { get; set; }
    public long Started { get; set; } public long LastProgress { get; set; } public long BlockedSince { get; set; } = -1;
    public int Assembly { get; set; } public bool Emergency { get; set; } public bool Abandoned { get; set; }
}

/// <summary>
/// Auftrag bzw. Befehl im Save: Art und drei Ganzzahlen (Phase 1–11); Phase 12 (v15) erweitert um optionale Listen für Befehle, die mehr brauchen
/// (Streifenrunde: Sektor-Ids; Ziele im Raum und Grenzen der Automatik: Kommazahlen).
/// </summary>
public sealed class OrderSave
{
    public string Kind { get; set; } = ""; public int A { get; set; } public int B { get; set; } public int C { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<int>? Ids { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<float>? Values { get; set; }
}
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
    /// <summary>Zielmodul der Verbesserung (Phase 7, v10); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? UpgradeTo { get; set; }
    /// <summary>Material je Upgrade-Versuch (Warenschlüssel → Menge, Phase 7, v10); fehlt = keins.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, float>? UpgradeCost { get; set; }
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
    /// <summary>Wirkungsgrad der Werkzeug-Slots (Phase 6); fehlt in älteren Spielständen = 1.</summary>
    public float ToolEfficiency { get; set; } = 1f;
    /// <summary>Höchstzahl der Slot-Upgrades je Schiff (Phase 7, v10); fehlt in älteren Spielständen = 1.</summary>
    public int MaxSlotUpgrades { get; set; } = 1;
    /// <summary>Nicht im freien Handel (Phase 11, v14); fehlt = nein.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Restricted { get; set; }
    /// <summary>Liegegebühr je Tag (Phase 12, v15); fehlt = nach Größe.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float BerthFee { get; set; }
}

public sealed class ShipModelSave
{
    public string Key { get; set; } = "";
    public string Class { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string?> Fit { get; set; } = new();
    /// <summary>KI-Aufgaben des Modells (Phase 6, v9); fehlt in älteren Spielständen (die Migration v8→v9 trägt sie für Default-Modelle nach).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Jobs { get; set; }
}

public sealed class TransitSave
{
    public long StartTick { get; set; } public long ArrivalTick { get; set; }
    public int ToSector { get; set; } public Vec2 ToPosition { get; set; }
    public List<LegSave> Legs { get; set; } = new();
}

public sealed class ShipSave
{
    public int Id { get; set; } public string Role { get; set; } = "";
    /// <summary>Fraktion des Schiffs (bis v11, damals zugleich Eigentümer); seit v12 nur noch gelesen (Migration), ersetzt durch <see cref="Owner"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Faction { get; set; }
    /// <summary>Besitzer (Akteur-Id, Phase 9, v12); null in älteren Ständen (die Migration v11→v12 setzt ihn).</summary>
    public int? Owner { get; set; }
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
    /// <summary>Tick der letzten Aufgabenzuteilung (Phase 6, v9); fehlt in älteren Spielständen = 0.</summary>
    public long JobSince { get; set; }
    /// <summary>Güteklasse (Phase 7, v10: Standard/Improved/Elite/Masterwork); fehlt = Standard.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Quality { get; set; }
    /// <summary>Mitleidszähler je Slot (Phase 7, v10; Schlüssel −1 = Bonusslot); fehlt = keine Fehlschläge.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<int, int>? UpgradePity { get; set; }
    /// <summary>Heimatbasis (Station-Id, Phase 11, v14); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int HomeBase { get; set; }
    /// <summary>Einsatzsektor (Phase 11, v14); fehlt = keiner.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int DutySector { get; set; }
    /// <summary>Phase 12 (v15): Befehl (Ebene „Befehl“); fehlt = Automatik.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OrderSave? Command { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long CommandSince { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int CommandStep { get; set; }
    /// <summary>Phase 12 (v15): Notfall-Ebene aktiv.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Emergency { get; set; }
    /// <summary>Phase 12 (v15): Verhaltensregel (Defend/Passive/Attack/HoldFire); fehlt = Defend.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Engagement { get; set; }
    /// <summary>Phase 12 (v15): Verband (Id); fehlt = keiner.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Squadron { get; set; }
    /// <summary>Phase 12 (v15): angedockt an (Station-Id); fehlt = nicht angedockt (ältere Stände: beim Laden aus der Position bestimmt).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int DockedAt { get; set; }
    /// <summary>Phase 12 (v15): Leihschiff — Station, Beginn, Pfand.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int RentedFrom { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long RentedSince { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float RentalDeposit { get; set; }
}
