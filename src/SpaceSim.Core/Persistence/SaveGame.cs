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
    /// <summary>Phase 19 (additiv): letzte Stilllegung und Knappheit seit (GoodId → Tick).</summary>
    public long LastStationRetireTick { get; set; } = -1_000_000;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<int, long>? ScarceSince { get; set; }
    /// <summary>Phase 20 (additiv): Versorgungspanik je Kartenteil und Ware ([Teil, GoodId, seit]); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<long[]>? PanicSince { get; set; }
    /// <summary>Phase 20 (additiv): letzte Ausmusterung wegen Leerlauf je Aufgabe und Kartenteil ("aufgabe@teil" → Tick); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, long>? IdleRetiredTick { get; set; }
    public int NextShipId { get; set; }
    public int NextStationId { get; set; }
    public int NextAsteroidId { get; set; } = 1;
    public int PlayerSector { get; set; }
    public int PlayerShip { get; set; }
    /// <summary>Additiv: Station, in der die Spielerperson zu Fuß ist (0 = an Bord des aktiven Schiffs; fehlt/ältere Stände = an Bord).</summary>
    public int PlayerAtStation { get; set; }
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
    /// <summary>Phase 31 (additiv, v17): Torklassen; fehlt = keine (Tore ohne Kampfwerte).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<GateTypeSave>? GateTypes { get; set; }
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
    /// <summary>Runde 30: Rückkauf-Posten des Spielers (nur, wenn welche offen sind).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<LedgerSave>? Ledger { get; set; }
    /// <summary>Phase 24 (additiv, v17): laufende Gefechte (Rundengefecht); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<EncounterSave>? Fights { get; set; }
    /// <summary>Phase 24: nächste freie Gefechts-Id.</summary>
    public int NextEncounterId { get; set; } = 1;
    /// <summary>Phase 26 (additiv, v17): Wracks und Frachtbehälter; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<WreckSave>? Wrecks { get; set; }
    /// <summary>Phase 26: nächste freie Wrack-Id.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int NextWreckId { get; set; }
    /// <summary>Universumsereignisse (additiv, v17 bleibt): laufende Ereignisse; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<UniverseEventSave>? UniverseEvents { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int NextUniverseEventId { get; set; }
    /// <summary>Wurmlöcher: offene instabile (additiv).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<UnstableWormholeSave>? UnstableWormholes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int NextWormholeId { get; set; }
    /// <summary>Piloten: Bilanz benannter NPC-Kapitäne (additiv; nur Anzeige, nicht gehasht).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<Pilots.CaptainSave>? Captains { get; set; }
    /// <summary>Hilfe und Tutorial (additiv; nicht gehasht): laufendes Tutorial; fehlt = keins.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Help.TutorialSave? Tutorial { get; set; }
    /// <summary>Bars (additiv): Losverkauf je Station und offenes Rubbellos; fehlt = nie benutzt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Bars.BarSave? Bar { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Simulation.WelleBSave? WelleB { get; set; }   // Welle B: Flottenabgabe, Spenden
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Simulation.WelleCSave? WelleC { get; set; }   // Welle C: weiches Ansehen
    /// <summary>Phase 28 (additiv, v17 bleibt): laufende Kriege und Friedensschlüsse je Fraktionspaar; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<WarSave>? Wars { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<long[]>? PeaceSince { get; set; }
    /// <summary>Nachtrag 2b (additiv): Ende der letzten Seeschlacht [Sektor, A, B, Tick]; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<long[]>? NavalEnded { get; set; }
    /// <summary>Runde 45 (additiv): Bündnisse [A, B, Since] und Bündnisenden [A, B, Tick].</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<long[]>? Alliances { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<long[]>? AllianceEnded { get; set; }
    /// <summary>Ausbau Punkt 6: Fehden [A, B, Since, Until, ByPlayer 0/1, LastRaidTick, Raids], Fehdenenden [A, B, Tick], Regierungskrisen [Fraktion, Ende].</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<long[]>? Feuds { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<long[]>? FeudEnded { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<long[]>? Crises { get; set; }
    /// <summary>Ausbau Punkt 3: Schutzverträge [Fraktion, Since, PaidUntil]; Konvois [Id, Fraktion, From, To, Formed, Departed, nHändler, Händler…, Geleit…]; nächste Konvoi-Id (fehlt = 1).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<long[]>? Protections { get; set; }
    /// <summary>Baupläne und Söldnerverträge (additiv; fehlt = keine). Siehe <see cref="BlueprintMercCodec"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<BlueprintSave>? Blueprints { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<MercContractSave>? Mercenaries { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<long[]>? Convoys { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? NextConvoyId { get; set; }
    /// <summary>Runde 40 (additiv, v17 bleibt): Posteingang des Spielers (reine Anzeige; gespeichert, aber nie gehasht); fehlt = leer.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<CommsMessageSave>? Inbox { get; set; }
    /// <summary>Runde 40: nächste Nachrichten-Id des Posteingangs; fehlt (alte Stände) = 1.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int NextMessageId { get; set; }
    /// <summary>Runde 41 (additiv): Auftragsangebote (gespeichert, nicht gehasht); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<MissionOfferSave>? MissionOffers { get; set; }
    /// <summary>Runde 41 (additiv): angenommene Aufträge (gespeichert und gehasht); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<MissionSave>? Missions { get; set; }
    /// <summary>Runde 41: nächste Auftrags-Id; fehlt = 1.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int NextMissionId { get; set; }
    /// <summary>Runde 41 (additiv): Budget-/Sperrbuchaltung der Aufträge je Auftraggeber; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<MissionFactionSave>? MissionFactions { get; set; }
    /// <summary>Phase 12 (v15): Verbände; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<SquadronSave>? Squadrons { get; set; }
    /// <summary>Träger (additiv): eingeschleuste Bordjäger je Träger; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<HangarSave>? Hangars { get; set; }
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
    /// <summary>Phase 29: Kürzel (fehlt in älteren Ständen — wird beim Laden abgeleitet).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Tag { get; set; }
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
    /// <summary>Phase 20 (additiv): geglätteter Polizeibedarf (&lt; 0 = ungemessen) und Überzahl seit.</summary>
    public float PoliceNeed { get; set; } = -1f;
    public long PoliceSurplusSince { get; set; } = -1;
    /// <summary>Phase 22 (additiv): letzter Notkauf zur Wiederherstellung des Mindestbestands.</summary>
    public long LastRestoreTick { get; set; } = -1_000_000;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long LastOutpostTick { get; set; }
    /// <summary>Ausbau Punkt 9 (additiv): letzte Gründung (Sitz/Büro) und Unterhaltsrückstand des Firmensitzes seit; fehlt = nie / bezahlt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long LastFoundTick { get; set; }
    /// <summary>Ausbau Punkt 5: letzter Schmugglerkauf (0 = nie).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long LastSmugglerTick { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long LastSmugglerRetireTick { get; set; }
    /// <summary>Ausbau Punkt 17 (additiv): letzter Banditenbau (Wirtschaftsstation oder Vorposten); fehlt = nie.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long LastBanditTick { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? SeatArrearsSince { get; set; }
    /// <summary>Phase 28 (additiv): Festnahme-Sperre bis zu diesem Tick; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long ArrestCooldownUntil { get; set; }
    /// <summary>Spielersperre (additiv): durch Verbrechen verlorenes Ansehen je Fraktion; fehlt = keins.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<CrimeStandingSave>? CrimeStanding { get; set; }
    /// <summary>Ausbau Punkt 16 (additiv): Wissen des Spielerakteurs; fehlt = alles bekannt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IntelSave? Intel { get; set; }
    /// <summary>Lücken gegenüber IP2 (additiv): Spielerstatistik; fehlt = nichts gezählt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PlayerStatsSave? Stats { get; set; }
    /// <summary>Mehrpunkt-Route (additiv): Halte des Spielerautopiloten; fehlt = keine Route.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RouteSave? Route { get; set; }
}

/// <summary>Lücken gegenüber IP2: <see cref="Model.PlayerStats"/>.</summary>
public sealed class PlayerStatsSave
{
    public double Earned { get; set; }
    public double Spent { get; set; }
    public double MaxWallet { get; set; }
    public double LastWallet { get; set; }
    public int LastSector { get; set; }
    public double TradeRevenue { get; set; }
    public double TradeSpent { get; set; }
    public double TradeUnits { get; set; }
    public int Sales { get; set; }
    public int Purchases { get; set; }
    public double OreMined { get; set; }
    public int Kills { get; set; }
    public int ShipsLost { get; set; }
    public int MissionsDone { get; set; }
    public int MissionsFailed { get; set; }
    public int Jumps { get; set; }
    public int Bounties { get; set; }
    public double BountyCredits { get; set; }
    public double Transferred { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double KillPoints { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Crimes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int RankSeen { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PilotName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double TaxCredits { get; set; }   // Welle C
}

/// <summary>Mehrpunkt-Route: Halte als [Art, Id, Sektor] mit Punkt (x, y), nächster Halt, Andock-Schalter.</summary>
public sealed class RouteSave
{
    public List<RouteStopSave> Stops { get; set; } = new();
    public int Next { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Dock { get; set; }
}

public sealed class RouteStopSave
{
    public int Kind { get; set; }
    public int Id { get; set; }
    public int Sector { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float X { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float Y { get; set; }
}

/// <summary>Ausbau Punkt 16: <see cref="Intel.PlayerIntel"/> — Listen nach Id sortiert; Felder als [Sektor, Index].</summary>
public sealed class IntelSave
{
    /// <summary>Sektor-Id → 1 = von fern gesehen, 2 = besucht.</summary>
    public Dictionary<int, int> Sectors { get; set; } = new();
    public List<int> Stations { get; set; } = new();
    public List<int> Gates { get; set; } = new();
    public List<int[]> Fields { get; set; } = new();
    public List<int[]> GasFields { get; set; } = new();
    public List<int> Factions { get; set; } = new();
}

/// <summary>Spielersperre: <see cref="Model.CrimeStandingRecord"/>.</summary>
public sealed class CrimeStandingSave
{
    public int Faction { get; set; } public float Debt { get; set; } public float Base { get; set; }
    public long WindowStart { get; set; } public float WindowLoss { get; set; }
    public string Reason { get; set; } = ""; public long LastTick { get; set; } public float LastLoss { get; set; }
}

/// <summary>Phase 28: Krieg zwischen zwei Fraktionen.</summary>
public sealed class WarSave
{
    public int A { get; set; } public int B { get; set; } public long Since { get; set; } public float Weariness { get; set; }
    public float LossA { get; set; } public float LossB { get; set; } public int Sieges { get; set; }
    /// <summary>Phase 31: Torblockaden.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Blockades { get; set; }
    /// <summary>Runde 40: Waffenstillstand bis zu diesem Tick (0 = keiner).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long CeasefireUntil { get; set; }
    /// <summary>Runde 40: Tick des letzten Angebots (0 = keines).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long LastOfferTick { get; set; }
    /// <summary>Runde 40: bereits gezahlter Tribut (0 = keiner).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float TributePaid { get; set; }
    /// <summary>Ausgleich (additiv): Schiffe, die A bzw. B in einem Krieg mit der Spielerfraktion verloren hat.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int ShipsLostA { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int ShipsLostB { get; set; }
    /// <summary>Runde 45: Beitrittskrieg eines Verbündeten.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Join { get; set; }
    /// <summary>Ausbau Punkt 6: Müdigkeitszuschlag aus Vermittlungen und Tick der letzten Vermittlung.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float MediationBonus { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long LastMediationTick { get; set; }
}

/// <summary>Runde 40: eine Nachricht im Posteingang des Spielers (gespeichert, nicht gehasht).</summary>
public sealed class CommsMessageSave
{
    public int Id { get; set; }
    public int From { get; set; }
    public string Kind { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? Args { get; set; }
    public long Created { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long Expires { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Read { get; set; }
}

/// <summary>Runde 41: ein Auftragsangebot im Save (gespeichert, nicht gehasht).</summary>
public sealed class MissionOfferSave
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int Client { get; set; }
    public int At { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? Args { get; set; }
    public float Reward { get; set; }
    public float Deposit { get; set; }
    public long Expires { get; set; }
    public long Created { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Haggled { get; set; }
}

/// <summary>Runde 41: ein angenommener Auftrag im Save (gespeichert und gehasht).</summary>
public sealed class MissionSave
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int Client { get; set; }
    public int At { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? Args { get; set; }
    public float Reserved { get; set; }
    public float Deposit { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float ReservedCash { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float CargoVolume { get; set; }
    public float Sold { get; set; }
    public float Bought { get; set; }
    public long Deadline { get; set; }
    public long AcceptedTick { get; set; }
    public string State { get; set; } = "Active";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Haggled { get; set; }
}

/// <summary>Runde 41: Budget-/Sperrbuchaltung der Aufträge je Auftraggeber (gespeichert und gehasht).</summary>
public sealed class MissionFactionSave
{
    public int Actor { get; set; }
    public long Day { get; set; }
    public float Spent { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long BlockedUntil { get; set; }
}

/// <summary>Phase 28: Rakete im Flug.</summary>
public sealed class MissileSave
{
    public int Shooter { get; set; } public int Owner { get; set; } public int Side { get; set; } public string Module { get; set; } = "";
    public int Target { get; set; } public int Station { get; set; } public float Left { get; set; } public float Total { get; set; } public float Speed { get; set; }
    public float FromX { get; set; } public float FromY { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int FromStation { get; set; }
    /// <summary>Phase 31: Ziel Tor (0 = keins).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Gate { get; set; }
}

/// <summary>Phase 28: Kampfzustand einer Station (Fehlbeträge, Schildpause, Geschützuhren, ausgeschaltet).</summary>
public sealed class StationConditionSave
{
    public float HullMissing { get; set; } public float ShieldMissing { get; set; } public long ShieldPauseUntil { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<float>? Timers { get; set; }
    public bool Disabled { get; set; }
    /// <summary>Wirtschaftsstationen halten stand (additiv): Alarm und Notschild (Fristen, 0 = keine).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long AlarmUntil { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long EmergencyUntil { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long EmergencyCooldownUntil { get; set; }
}

/// <summary>Phase 28: Verteidigung eines Stationstyps.</summary>
public sealed class StationDefenseSave
{
    public float Hull { get; set; } public float Shield { get; set; } public float ShieldRegen { get; set; } public float Armor { get; set; } public float PointDefense { get; set; }
    public List<string> Guns { get; set; } = new();
}

/// <summary>Phase 22 (additiv, v17 bleibt): Rentabilitätsmessung (gleitender Mittelwert, Bezug, Verlust-Ticks, Messbeginn).</summary>
public sealed class ProfitMeterSave
{
    public float Rate { get; set; }
    public float Worth { get; set; }
    public int Loss { get; set; }
    public long Since { get; set; } = -1;
}

/// <summary>Kasse der Spielerperson (Phase 5b, v7 bis v11; seit v12 nur noch gelesen).</summary>
/// <summary>Fahndungseintrag (Phase 11, v14).</summary>
public sealed class WantedSave
{
    public int Faction { get; set; } public float Heat { get; set; } public long LastCrime { get; set; } public float Fine { get; set; }
    public string Crime { get; set; } = "";
}

/// <summary>Angekündigte Begegnung (Phase 11, v14).</summary>
/// <summary>Runde 30: ein Rückkauf-Posten (Spieler-Akteur, Station, Ware; Kauf- und Verkaufsteil).</summary>
public sealed class LedgerSave
{
    public int Actor { get; set; }
    public int Station { get; set; }
    public int Good { get; set; }
    public float BoughtQty { get; set; }
    public float BoughtPrice { get; set; }
    public float BoughtStockBefore { get; set; }
    public float SoldQty { get; set; }
    public float SoldPrice { get; set; }
    public float SoldStockBefore { get; set; }
    /// <summary>Runde 32b: Teile wirken auch mit Restmenge 0 (fehlt = aus der Menge abgeleitet).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? BoughtOpen { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? SoldOpen { get; set; }
    /// <summary>Runde 32b: Tick der letzten Buchung (fehlt = 0).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long LastTick { get; set; }
    /// <summary>Runde 36: noch fehlende gekaufte Einheiten und zuletzt gesehener Bestand (fehlt = 0).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float MissingQty { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float MissingSeen { get; set; }
}

public sealed class PendingEncounterSave
{
    public string Kind { get; set; } = ""; public int Source { get; set; } public int Target { get; set; } public int Sector { get; set; } public long Due { get; set; }
    /// <summary>Runde 13b (additiv): warnende Waffenstellung; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Station { get; set; }
    /// <summary>Funk (additiv): schon hingehalten; fehlt = nein.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Stalled { get; set; }
}

/// <summary>Phase 24: laufendes Gefecht (Rundengefecht).</summary>
public sealed class EncounterSave
{
    public int Id { get; set; } public int Sector { get; set; } public long Start { get; set; } public string Cause { get; set; } = "";
    public int Raider { get; set; } public int RaiderOwner { get; set; } public int Victim { get; set; } public int Police { get; set; }
    public float Loot { get; set; } public float Fenced { get; set; } public int Shots { get; set; } public int Hits { get; set; }
    /// <summary>Phase 25: räumliches Gefecht (Spielersektor).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Spatial { get; set; }
    /// <summary>Phase 28: belagerte Station und ihre Linienposition; Raketen im Flug.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Station { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float StationX { get; set; }
    /// <summary>Phase 31: belagertes Tor (0 = keins).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Gate { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<MissileSave>? Missiles { get; set; }
    public List<CombatParticipantSave> Participants { get; set; } = new();
}

/// <summary>Phase 24: Teilnehmer eines Gefechts.</summary>
public sealed class CombatParticipantSave
{
    public int Ship { get; set; } public int Side { get; set; } public string Tactic { get; set; } = ""; public float X { get; set; }
    public int Target { get; set; } public string Move { get; set; } = ""; public float Speed { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<float>? Timers { get; set; }
    public float Courage { get; set; } public float DropShare { get; set; } public string PrevMode { get; set; } = ""; public string Exit { get; set; } = "";
    /// <summary>Phase 25: echte Position/Blickrichtung (nur räumliche Gefechte), Liegeplatz vor dem Gefecht, Besitzer beim Eintritt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float[]? Pos { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int WasDockedAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Owner { get; set; }
    /// <summary>Phase 27: Zuflucht eines Fliehenden [Abstand, Id (&gt; 0 Station, &lt; 0 Tor), x, y]; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float[]? Refuge { get; set; }
    /// <summary>Runde 6: letzter eigener Schuss des Spielers (nur gesetzt).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? LastShotTick { get; set; }
    /// <summary>Phase 32: Trossen kappen bis zu diesem Tick (fehlt = frei).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? CastOffUntil { get; set; }
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
    /// <summary>Phase 28: Stationsverteidigung; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public StationDefenseSave? Defense { get; set; }
    /// <summary>Phase 30: Größenklasse und Radius (fehlen in älteren Ständen: M, 15).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Size { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Radius { get; set; }
}

/// <summary>Exportsenke eines Stationstyps (Phase 14, v17): Waren (GoodIds), Kategorien, Verzögerung/Aufschlag (null = Regel), Wunschbestand.</summary>
public sealed class ExportConfigSave
{
    public List<int> Goods { get; set; } = new(); public List<string> Categories { get; set; } = new();
    public int? DelayTicks { get; set; } public float? Markup { get; set; } public float Target { get; set; } public float TargetValue { get; set; }
    /// <summary>Phase 17: Höchstgebot je Exportware (GoodId → × Basispreis); fehlt = Regel (additiv, keine neue Version).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<int, float>? Bids { get; set; }
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
    /// <summary>Runde 44: Maßstab des Sektors; fehlt = 1.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Scale { get; set; }
    /// <summary>Himmelskugel (Darstellung, additiv); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Skybox { get; set; }
    /// <summary>Drehung des Himmels in Grad; fehlt = 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? SkyboxYaw { get; set; }
    /// <summary>Gasernte: Nebelsektor (additiv); fehlt = kein Nebel.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Nebula { get; set; }
    /// <summary>Gasernte: Gasfelder (additiv); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<GasFieldSave>? GasFields { get; set; }
    /// <summary>Sonne (additiv); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SunSave? Sun { get; set; }
}

/// <summary>Sonne eines Sektors: Lage, Radius (Welteinheiten), Farbe, Sternart.</summary>
public sealed class SunSave
{
    public Vec2 Pos { get; set; }
    public float Radius { get; set; }
    public string Color { get; set; } = "#ffe6a8";
    public string Kind { get; set; } = "yellow";
}

/// <summary>Gasfeld (Gasernte): Lage, Radius, Dichte.</summary>
public sealed class GasFieldSave
{
    public Vec2 Pos { get; set; }
    public float Radius { get; set; }
    public float Density { get; set; } = 1f;
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
    /// <summary>Phase 31: Torklasse und Kampfzustand des Paars (am Tor mit der kleineren Id); fehlen = keine/unbeschädigt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Type { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GateConditionSave? Condition { get; set; }
    /// <summary>Wurmlöcher: Hälfte eines stabilen Wurmlochs (additiv).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Wormhole { get; set; }
}

/// <summary>Phase 31: Torklasse.</summary>
public sealed class GateTypeSave
{
    public string Key { get; set; } = ""; public string Name { get; set; } = ""; public StationDefenseSave Defense { get; set; } = new();
    public float RepairPerDay { get; set; } public long MaxOutageTicks { get; set; }
}

/// <summary>Phase 31: Kampfzustand eines Tor-Paars.</summary>
public sealed class GateConditionSave
{
    public float HullMissing { get; set; } public float ShieldMissing { get; set; } public long ShieldPauseUntil { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<float>? Timers { get; set; }
    public bool Disabled { get; set; } public long DownSince { get; set; } public int LastAttacker { get; set; }
    /// <summary>Runde 14b: Schonfrist und Zustand vor dem Ausfall (fehlen = keine/offen).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long GraceUntil { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PrevState { get; set; }
}

public sealed class StockSave
{
    public float Amount { get; set; } public float Target { get; set; } public float Price { get; set; }
    /// <summary>Ausbau Punkt 12 (additiv): Tick der letzten Bewegung (Kauf/Lieferung) eines Regaleintrags; fehlt = keine seit Weltbeginn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? LastMove { get; set; }
    /// <summary>Ausbau Punkt 12 (additiv): zuletzt beobachteter Bestand eines Modulregals; fehlt = noch nicht beobachtet.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Seen { get; set; }
}

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
    /// <summary>Schwarzmarkt (additiv, v17 bleibt): Hehlerware des Verstecks (GoodId → Menge); fehlt = leer.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<int, float>? BlackStock { get; set; }
    /// <summary>Phase 14 (v17): wartende Chargen der Exportsenke in Ankaufsreihenfolge; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ExportBatchSave>? Exports { get; set; }
    /// <summary>Phase 28 (additiv): Kampfzustand; fehlt = unbeschädigt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public StationConditionSave? Condition { get; set; }
    /// <summary>Phase 15 (additiv, v17 bleibt): offene Instandhaltung (Ware → fehlende Einheiten); fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<int, float>? MaintenanceBacklog { get; set; }
    /// <summary>Phase 19 (additiv, v17 bleibt): Fertigstellungs-Tick und Auslastung (gleitende Mittel: lief / fehlende Zutaten).</summary>
    public long BuiltTick { get; set; }
    public float Activity { get; set; } = 1f;
    public float Starved { get; set; }
    /// <summary>Phase 20 (additiv): als Ersatz in einer Versorgungspanik gebaut.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Panic { get; set; }
    /// <summary>Phase 22 (additiv): Rentabilitätsmessung; fehlt = noch nicht gemessen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProfitMeterSave? Profit { get; set; }
    /// <summary>Phase 30: ruhig seit (Kleinstation), null = nicht ruhig.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? CalmSince { get; set; }
    /// <summary>Ausbau Punkt 8 (additiv): Zustand der Station 0..1, null = 1.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Integrity { get; set; }
    /// <summary>Eigene Stationen: Drehung des Modells in Grad (additiv), null = 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Rotation { get; set; }
    /// <summary>Ausgleich (additiv): Kapitulationssperre bis Tick.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long CapitulationLockUntil { get; set; }
    /// <summary>Ausgleich (additiv): Regierungsunterhalt unbezahlt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Unpaid { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Dormant { get; set; }   // Welle C: ruhende eigene Behörde
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
    /// <summary>Phase 20 (additiv): Baustelle einer Versorgungspanik.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Panic { get; set; }
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
    /// <summary>Phase 24 (additiv): Schadensart der Waffe (energy/kinetic); fehlt = energy.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DamageType { get; set; }
    /// <summary>Raketenlenkung (additiv; fehlt = keine).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Guidance { get; set; }
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
    /// <summary>Phase 29: Bausätze je Schiff (fehlt = 1).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int KitCount { get; set; }
    /// <summary>Träger (additiv): Hangarplätze des Rumpfs; fehlt = 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int HangarBays { get; set; }
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
    /// <summary>Phase 32: Verankerung beim Kontaktabbau (Asteroid, Fortschritt in Ticks); fehlt = nicht verankert.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int AnchorRock { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float AnchorProgress { get; set; }
    /// <summary>Sprungantrieb (additiv): Zielsektor, geladene Ticks, Ladepunkt, Abklingende; Tarnfeld an. Fehlt = nicht gesetzt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int JumpTarget { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float JumpCharge { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Vec2? JumpFrom { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long JumpCooldownUntil { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Cloaked { get; set; }
    /// <summary>Gegenmaßnahmen (additiv): fehlende Täuschkörper, Täuschkörper aktiv bis Tick, Störsender an.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float FlaresMissing { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long FlaresUntil { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool JammerOn { get; set; }
    /// <summary>Träger (additiv): Heimatträger eines Bordjägers, Rückkehr seit Tick, hat gekämpft.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int CarrierHome { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? CarrierReturnSince { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool CarrierFought { get; set; }
    public Dictionary<int, float> Cargo { get; set; } = new();
    /// <summary>Treibstoff an Bord je Ware (GoodId → Menge); fehlt in v4-Saves (die Migration trägt einen vollen Tank nach).</summary>
    public Dictionary<int, float> Fuel { get; set; } = new();
    /// <summary>Tick der letzten Aufgabenzuteilung (Phase 6, v9); fehlt in älteren Spielständen = 0.</summary>
    public long JobSince { get; set; }
    /// <summary>Phase 19 (additiv): seit wann unverkäufliche Fracht an Bord ist; fehlt = keine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? StrandedSince { get; set; }
    /// <summary>Liegezeit: Tick der Ankunft an einer Station (additiv).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? DockedSince { get; set; }
    /// <summary>Phase 22 (additiv): Rentabilitätsmessung; fehlt = noch nicht gemessen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProfitMeterSave? Profit { get; set; }
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
    /// <summary>Phase 26 (additiv): bereits abgerechnete Miettage.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int RentPaidDays { get; set; }
    /// <summary>Runde 41 (additiv): versiegelte Fracht eines Transportauftrags (Laderaum-Volumen, keine Ware); fehlt = 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float MissionCargo { get; set; }
    /// <summary>Lücken gegenüber IP2 (additiv): vom Spieler vergebener Schiffsname; fehlt = Modellname.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
    /// <summary>Herrenlose Schiffe (additiv): verfällt bei diesem Tick; fehlt = 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long DerelictUntil { get; set; }
    /// <summary>Phase 23 (additiv): Laufzeitzustand der Bordsysteme; fehlt = voll, ausgeglichen, unbeschädigt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShipSystemsSave? Systems { get; set; }
}

/// <summary>Phase 23: Bordsysteme eines Schiffs als Fehlbeträge (0 = voll), Zustand je Slot (fehlt = alle heil), Leistungsverteilung, Schildpause.</summary>
public sealed class ShipSystemsSave
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float ShieldMissing { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float ChargeMissing { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float AmmoMissing { get; set; }
    /// <summary>Phase 28 (additiv): fehlende Raketen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float MissilesMissing { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<float>? Condition { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Mode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long ShieldPauseUntil { get; set; }
    /// <summary>Phase 26 (additiv): brennende Slots; fehlt = kein Brand.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<int>? Burning { get; set; }
    /// <summary>Hitze (additiv): angestaute Hitze und Sperre; fehlt = kalt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float Heat { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool HeatLocked { get; set; }
    /// <summary>Richtungsschilde (additiv): Fehlbetrag je Sektor (vorne, rechts, hinten, links) und Ausrichtung; fehlt = Normalverteilung, ausgeglichen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<float>? ShieldSectors { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ShieldFocus { get; set; }
}

/// <summary>Phase 26: ein Wrack oder Frachtbehälter (Art, Sektor, Position, Name, Herkunft, Zeiten, Fracht je Ware, Module mit Restzustand).</summary>
public sealed class WreckSave
{
    public int Id { get; set; }
    public string Kind { get; set; } = "Wreck";
    public int Sector { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int From { get; set; }
    public long Created { get; set; }
    public long Expires { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<int, float>? Cargo { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<WreckModuleSave>? Modules { get; set; }
}

/// <summary>Phase 26: Modul im Wrack.</summary>
public sealed class WreckModuleSave
{
    public string Key { get; set; } = "";
    public float Condition { get; set; }
}
