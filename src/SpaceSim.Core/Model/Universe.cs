using SpaceSim.Core.Events;

namespace SpaceSim.Core.Model;

public sealed class Universe
{
    public long Tick;
    public ulong Seed = 1;
    public DeterministicRng Rng = new(1);

    public Dictionary<SectorId, Sector> Sectors = new();
    public Dictionary<GateId, Gate> Gates = new();
    public Dictionary<StationId, Station> Stations = new();
    public Dictionary<ShipId, Ship> Ships = new();
    public Dictionary<FactionId, Faction> Factions = new();
    public Dictionary<GoodId, Good> Goods = new();
    public Dictionary<string, StationType> StationTypes = new();
    /// <summary>Phase 31: Torklassen (JSON <c>gateTypes</c>; leer = Tore ohne Kampfwerte).</summary>
    public Dictionary<string, GateType> GateTypes = new();
    public Dictionary<string, AsteroidClass> AsteroidClasses = new();
    /// <summary>Slotarten (Content, leer wenn die Weltdatei keinen Abschnitt hat).</summary>
    public Dictionary<string, SlotType> SlotTypes = new();
    /// <summary>Module (Content).</summary>
    public Dictionary<string, Module> Modules = new();
    /// <summary>Schiffsklassen (Content).</summary>
    public Dictionary<string, ShipClass> ShipClasses = new();
    /// <summary>Schiffsmodelle (Content).</summary>
    public Dictionary<string, ShipModel> ShipModels = new();
    /// <summary>Phase 13: Forschungsprojekte (Content, Weltdatei-Abschnitt <c>research</c>) und Sperr-Index; leer = alles frei.</summary>
    public ResearchCatalog Research { get; } = new();
    public EconomyRules Rules = new();
    public long LastStationBuildTick = -1_000_000;
    /// <summary>Phase 19: Tick der letzten Stilllegung (Abklingzeit, <see cref="Simulation.StationReview"/>). Gespeichert.</summary>
    public long LastStationRetireTick = -1_000_000;
    /// <summary>
    /// Phase 19: seit wann eine Ware ununterbrochen knapp ist (Lebenszyklus-Messung, nur Waren, auf die ein Hersteller antworten kann). Gespeichert:
    /// die Stilllegung verlangt eine anhaltende Knappheit.
    /// </summary>
    public Dictionary<GoodId, long> ScarceSince = new();
    /// <summary>
    /// Phase 20: Versorgungspanik — seit wann in einem Kartenteil (Schlüssel = kleinste Sektor-Id, <see cref="Simulation.MapParts"/>) der letzte Hersteller einer
    /// dort gebrauchten Ware fehlt (<see cref="Simulation.SupplyPanic"/>). Gespeichert.
    /// </summary>
    public Dictionary<(int Part, GoodId Good), long> PanicSince = new();
    /// <summary>Phase 20: letzte Ausmusterung wegen Leerlauf je Aufgabe und Kartenteil (Kaufsperre <see cref="EconomyRules.ExpandAfterRetireTicks"/>). Gespeichert.</summary>
    public Dictionary<(string Job, int Part), long> IdleRetiredTick = new();

    /// <summary>Der einzige Sektor, der als Godot-Szene materialisiert ist.</summary>
    public SectorId PlayerSector;
    public ShipId PlayerShip;
    /// <summary>
    /// Station, in der die Spielerperson zu Fuß ist (0 = an Bord des aktiven Schiffs). „Schiff verlassen“ an einer Station
    /// und die Taxifahrt setzen dieses Feld; es bleibt gesetzt, bis die Person ein Schiff betritt
    /// (<see cref="Simulation.PlayerFleet.Activate"/>). Ist es gesetzt, ist die Person in dieser Station (nicht im aktiven
    /// Schiff): Das aktive Schiff bleibt geparkt und führt Befehle wie ein Flottenschiff aus, Abdocken/Fliegen braucht ein
    /// neues aktives Schiff. Gespeichert (additiv) und gehasht.
    /// </summary>
    public StationId PlayerAtStation;
    /// <summary>Wahr, wenn die Spielerperson zu Fuß in einer Station ist (nicht im aktiven Schiff).</summary>
    public bool PlayerOnFoot => PlayerAtStation.Value != 0;
    /// <summary>Runde 45 (Befehlsstand): Die Person sitzt im aktiven Schiff, das seinen Befehl NPC-artig weiter ausführt — sie schaut nur zu.
    /// Rein Ansichtszustand (nicht gespeichert, nicht im Hash): das Order-System pausiert den Befehl dann nicht, die Materialisierung zeigt das
    /// aktive Schiff als normales Sektor-Schiff, und die Ansicht sperrt die Steuerung (Kamera bleibt frei).</summary>
    public bool PlayerSpectates;

    /// <summary>
    /// Akteure (Phase 9): Fraktionsakteure (Id = Fraktions-Id), die Sammelgruppe „Freie“ (<see cref="FreeActor"/>), der Spieler
    /// (<see cref="PlayerActor"/>) und aufgestiegene Einzelschiffe (<see cref="Simulation.ActorRules.Promote"/>). Gespeichert und gehasht.
    /// </summary>
    public Dictionary<ActorId, Actor> Actors = new();
    /// <summary>Der Spieler als Einzelakteur (Phase 9; seine Kasse ist die Spielerkasse aus Phase 5b).</summary>
    public ActorId PlayerActor = new(ActorId.FirstGenerated + 1);
    /// <summary>Sammelgruppe aller freien Einzelschiffe (implizite Akteure, POLITIK.md „Einzelschiffe träge anlegen“).</summary>
    public ActorId FreeActor = new(ActorId.FirstGenerated);
    /// <summary>Nächste freie Akteur-Id (gespeichert).</summary>
    public int NextActorId { get; set; } = ActorId.FirstGenerated + 2;

    public Universe() => EnsureCoreActors();

    /// <summary>Legt Sammelgruppe „Freie“ und Spielerakteur an, falls sie fehlen (neues Universum, Laden).</summary>
    public void EnsureCoreActors()
    {
        if (!Actors.ContainsKey(FreeActor))
            Actors[FreeActor] = new Actor { Id = FreeActor, Name = "Freie Kapitäne", Kind = ActorKind.Free, Tag = "FRE" };
        if (!Actors.ContainsKey(PlayerActor))
            Actors[PlayerActor] = new Actor { Id = PlayerActor, Name = "Du", Kind = ActorKind.Player, ShipsUseTreasury = true, Tag = "SPL" };
        if (NextActorId <= Math.Max(FreeActor.Value, PlayerActor.Value)) NextActorId = Math.Max(FreeActor.Value, PlayerActor.Value) + 1;
    }

    /// <summary>Phase 29: gibt dem Akteur ein Kürzel, das kein anderer Akteur trägt (<see cref="ActorTags.Derive"/>).</summary>
    public void AssignTag(Actor a)
    {
        var used = new HashSet<string>(Actors.Values.Where(x => x.Id != a.Id && x.Tag.Length > 0).Select(x => x.Tag), StringComparer.Ordinal);
        a.Tag = ActorTags.Derive(a.Name, used.Contains);
    }

    /// <summary>Phase 29: Akteure ohne Kürzel (neue Welt, ältere Spielstände) bekommen eins, in Id-Reihenfolge.</summary>
    public void AssignMissingTags()
    {
        if (Actors.TryGetValue(FreeActor, out var free) && free.Tag.Length == 0) free.Tag = "FRE";
        if (Actors.TryGetValue(PlayerActor, out var me) && me.Tag.Length == 0) me.Tag = "SPL";
        foreach (var a in Actors.Values.OrderBy(x => x.Id.Value)) if (a.Tag.Length == 0) AssignTag(a);
    }

    /// <summary>Neue Akteur-Id (fortlaufend ab <see cref="ActorId.FirstGenerated"/>).</summary>
    public ActorId NewActorId() => new(NextActorId++);

    /// <summary>Runde 38: ist <paramref name="f"/> die Fraktion des Spielers (Id-Zahl des Spielerakteurs, nur mit angelegter Spielerfraktion)?</summary>
    public bool IsPlayerFaction(FactionId f) => f.Value == PlayerActor.Value && f.Value != 0 && Factions.ContainsKey(f);

    /// <summary>Runde 38: Fraktion des Spielers (null = Spieler ist keine eigene Fraktion, Regel <see cref="EconomyRules.PlayerOwnFaction"/> aus).</summary>
    public FactionId? PlayerFaction => Factions.ContainsKey(new FactionId(PlayerActor.Value)) ? new FactionId(PlayerActor.Value) : null;

    List<FactionId>? _npcFactions; int _npcFactionsCount = -1;
    /// <summary>
    /// Runde 38: alle KI-Fraktionen (ohne die Spielerfraktion) in Id-Reihenfolge — der zentrale Filter für jede Fraktionsregel der KI (Fiskalbremse,
    /// Beziehungsdrift, Handlungsfähigkeit, Polizei, Bauschiffe, Kriegs-KI, Forschung, Diagnose). Gecacht, solange sich die Zahl der Fraktionen nicht ändert.
    /// </summary>
    public IReadOnlyList<FactionId> NpcFactionIds()
    {
        if (_npcFactions is null || _npcFactionsCount != Factions.Count)
        {
            _npcFactions = Factions.Keys.Where(f => !IsPlayerFaction(f)).OrderBy(f => f.Value).ToList();
            _npcFactionsCount = Factions.Count;
        }
        return _npcFactions;
    }

    /// <summary>Runde 38: ist <paramref name="f"/> eine KI-Fraktion (vorhanden, nicht der Spieler)?</summary>
    public bool IsNpcFaction(FactionId f) => Factions.ContainsKey(f) && !IsPlayerFaction(f);

    /// <summary>Der Spielerakteur.</summary>
    public Actor Player => Actors[PlayerActor];

    /// <summary>Kasse der Spielerperson = Kasse des Spielerakteurs (Phase 5b eingeführt, seit Phase 9 die <see cref="Actor.Treasury"/>).</summary>
    public Treasury PlayerWallet => Player.Treasury;

    /// <summary>
    /// Kasse, auf die ein Schiff bucht (Phase 9: „Kasse des Besitzers“): die <see cref="Actor.Treasury"/> des Besitzers, wenn dieser über eine
    /// gemeinsame Kasse wirtschaftet (<see cref="Actor.ShipsUseTreasury"/>, der Spieler), sonst das Schiff selbst (Betriebskasse
    /// <see cref="Ship.Credits"/>, Verhalten wie vor Phase 9). Spielergesteuerte Schiffe ohne auflösbaren Besitzer buchen auf die Spielerkasse.
    /// </summary>
    public ICreditAccount WalletOf(Ship ship)
    {
        if (Actors.TryGetValue(ship.Owner, out var a) && a.ShipsUseTreasury) return a.Treasury;
        return ship.IsPlayer ? PlayerWallet : ship;
    }

    /// <summary>Mitgliedschaft eines Akteurs (Fraktion); null = unabhängig oder unbekannt. Fraktionsakteure sind Mitglied ihrer selbst.</summary>
    public FactionId? FactionOf(ActorId actor)
    {
        if (Actors.TryGetValue(actor, out var a)) return a.Membership;
        return Factions.ContainsKey(new FactionId(actor.Value)) ? new FactionId(actor.Value) : null;
    }

    /// <summary>Fraktion, der ein Schiff über seinen Besitzer angehört (freie Einzelschiffe: keine).</summary>
    public FactionId? FactionOf(Ship ship) => FactionOf(ship.Owner);

    /// <summary>Fraktion, der eine Station über ihren Besitzer angehört.</summary>
    public FactionId? FactionOf(Station station) => FactionOf(station.Owner);

    /// <summary>Anzeigename eines Akteurs ("?" wenn unbekannt).</summary>
    public string ActorName(ActorId actor) => Actors.TryGetValue(actor, out var a) ? a.Name : "?";

    /// <summary>
    /// Phase 11: angekündigte Begegnungen mit dem Spieler (Überfall, Abfangen) mit Vorwarnzeit; gespeichert. Aufgelöst von
    /// <see cref="Simulation.PirateSystem"/> bzw. <see cref="Simulation.SecuritySystem"/>.
    /// </summary>
    public List<PendingEncounter> PendingEncounters = new();
    /// <summary>Runde 30: Rückkaufdeckel — Merkposten je (Spieler-Akteur, Station, Ware), siehe <see cref="Trading.PlayerLedger"/>. Leer für NPC-Welten.</summary>
    public readonly Dictionary<(int Actor, int Station, int Good), LedgerPost> Ledger = new();

    /// <summary>Phase 12: Verbände (FLOTTEN.md Abschnitt 3); gespeichert und gehasht.</summary>
    public Dictionary<SquadronId, Squadron> Squadrons = new();
    /// <summary>Phase 12: nächste freie Verbands-Id (gespeichert).</summary>
    public int NextSquadronId { get; set; } = 1;
    /// <summary>Neue Verbands-Id (fortlaufend).</summary>
    public SquadronId NewSquadronId() => new(NextSquadronId++);

    /// <summary>
    /// Phase 11: Auflöser für Begegnungen (FLOTTEN.md Abschnitt 7). Standard ist die abstrakte Auflösung nach Stärke über <see cref="Rng"/>; ein echtes
    /// Kampfsystem kann ihn später ersetzen, ohne dass sich Polizei und Piraten ändern. Nicht gespeichert (Verhalten, kein Zustand).
    /// </summary>
    public Simulation.IEncounterResolver Encounters { get; set; } = new Simulation.AbstractEncounterResolver();

    /// <summary>Ereignisbus und Schiffsindex sind abgeleitet und gehören nicht zum Spielstand.</summary>
    public SimEvents Events { get; } = new();
    public ShipTracker Tracker { get; } = new();
    /// <summary>
    /// Runde 39: Journal der letzten Handelsbuchung des Spielers („Rückgängig“ im Handel), siehe <see cref="Trading.TradeJournal"/>.
    /// Abgeleiteter Zustand wie <see cref="Events"/>: <b>nicht gespeichert, nicht gehasht</b>; neues Spiel und Laden beginnen mit leerem
    /// Journal. Abdocken/Schiffswechsel/Entfernen der Station löschen den Eintrag (Hooks an den Stellen, die <see cref="Ship.DockedAt"/> auflösen).
    /// </summary>
    public Trading.TradeJournal Journal { get; } = new();
    /// <summary>
    /// Phase 23: Schiffe, deren Bordsysteme sich erholen (Schild, Bank nicht voll) — abgeleitet, nicht gespeichert, beim Laden neu aufgebaut
    /// (<see cref="Combat.RecoveryIndex.Rebuild"/>). Nur diese Schiffe kosten <see cref="Combat.ShipSystemsSystem"/> Rechenzeit.
    /// </summary>
    public Combat.RecoveryIndex Recovering { get; } = new();
    /// <summary>
    /// Punkt 13 (Rechenzeit): Schalter für die neuen Zwischenspeicher (Sektorpfade, Regierungsstationen, Zeugen des Mindestbestands). Aus = jedes Mal neu
    /// gerechnet wie vorher — nur für Vergleichstests (gleicher Hash mit und ohne); nicht gespeichert, nicht im Hash.
    /// </summary>
    public bool PerfCaches { get; set; } = true;
    /// <summary>Punkt 13 (Rechenzeit): Zwischenspeicher der NPC-Sektorpfade (<see cref="Simulation.SectorPathCache"/>) — abgeleitet, nicht gespeichert.</summary>
    public Simulation.SectorPathCache PathCache { get; } = new();
    /// <summary>Punkt 13: Zeugen der Mindestbestands-Prüfung (<see cref="Simulation.FactionViability"/>) — abgeleitet, nicht gespeichert.</summary>
    public Dictionary<(int Kind, int Faction, int Min), Ship[]> ViabilityWitness { get; } = new();
    /// <summary>
    /// Phase 24 (KAMPF.md 4.1): laufende Gefechte (gespeichert und gehasht) und der abgeleitete Index Schiff → Gefecht. Ausgeführt von
    /// <see cref="Combat.CombatSystem"/>; Schiffe im Gefecht sind für Handel, Bergbau und Befehle gesperrt.
    /// </summary>
    public Combat.CombatBook Fights { get; } = new();
    /// <summary>
    /// Phase 26 (KAMPF.md 4.4): Wracks und Frachtbehälter im All (gespeichert und gehasht, Reihenfolge = Entstehung). Regeln in <see cref="Simulation.Wrecks"/>,
    /// Verfall in <see cref="Simulation.WreckSystem"/>.
    /// </summary>
    public List<Wreck> Wrecks { get; } = new();
    /// <summary>Phase 26: nächste Wrack-Id (fortlaufend, gespeichert).</summary>
    public int NextWreckId { get; set; } = 1;
    /// <summary>Phase 28: laufende Kriege zwischen Fraktionen (Reihenfolge der Erklärung; gespeichert und gehasht, wenn welche laufen). Regeln in <see cref="Simulation.Wars"/>.</summary>
    public List<War> Wars { get; } = new();
    /// <summary>Phase 28: Friedensschluss je Fraktionspaar (kleinere Id zuerst) → Tick; sperrt eine neue Kriegserklärung für <see cref="EconomyRules.PeaceCooldownTicks"/>.</summary>
    public Dictionary<(int A, int B), long> PeaceSince { get; } = new();
    /// <summary>Nachtrag 2b: Ende der letzten Seeschlacht je (Sektor, Fraktionspaar mit kleinerer Id zuerst) → Tick; sperrt eine neue Seeschlacht desselben Paares im Sektor
    /// für <see cref="EconomyRules.NavalBattleCooldownTicks"/>. Gespeichert und gehasht, wenn vorhanden; abgelaufene Einträge räumt <see cref="Combat.NavalBattle.Trigger"/> weg.</summary>
    public Dictionary<(int Sector, int A, int B), long> NavalEnded { get; } = new();
    /// <summary>Runde 45: bestehende Bündnisse (Reihenfolge der Gründung; gespeichert, gehasht nur wenn vorhanden). Regeln in <see cref="Simulation.Alliances"/>.</summary>
    public List<Alliance> Alliances { get; } = new();
    /// <summary>Runde 45: Ende eines Bündnisses je Fraktionspaar (kleinere Id zuerst) → Tick; sperrt ein neues Bündnis für <see cref="EconomyRules.AllianceCooldownTicks"/>.</summary>
    public Dictionary<(int A, int B), long> AllianceEnded { get; } = new();
    /// <summary>Ausbau Punkt 6: laufende Fehden (Reihenfolge des Beginns; gespeichert, gehasht nur wenn vorhanden). Regeln in <see cref="Simulation.Feuds"/>.</summary>
    public List<Feud> Feuds { get; } = new();
    /// <summary>Ausbau Punkt 6: Ende der letzten Fehde je Paar (kleinere Id zuerst) → Tick; sperrt eine neue für <see cref="EconomyRules.FeudCooldownTicks"/>.</summary>
    public Dictionary<(int A, int B), long> FeudEnded { get; } = new();
    /// <summary>Ausbau Punkt 6: Regierungskrise je KI-Fraktion (Id → Ende-Tick) nach Verlust des Hauptquartiers; gespeichert, gehasht nur wenn vorhanden.</summary>
    public Dictionary<int, long> Crises { get; } = new();
    /// <summary>Ausbau Punkt 3: Schutzverträge des Spielers (gespeichert, gehasht nur mit Spielerpolitik). Regeln in <see cref="Simulation.Protection"/>.</summary>
    public List<ProtectionContract> Protections { get; } = new();
    /// <summary>Ausbau Punkt 3: KI-Konvois (sammelnd oder unterwegs; gespeichert, gehasht nur wenn vorhanden). Regeln in <see cref="Simulation.ConvoyRules"/>.</summary>
    public List<Convoy> Convoys { get; } = new();
    public int NextConvoyId { get; set; } = 1;
    /// <summary>
    /// Runde 40: Posteingang des Spielers (Nachrichten aus Simulationsereignissen, siehe <see cref="Comms.CommsSystem"/>). Additiv gespeichert,
    /// aber <b>nicht gehasht</b> — reine Anzeige für den Spieler, verbraucht keine Zufallszahl. Höchstes <see cref="Comms.Comms.MaxInbox"/> Einträge.
    /// </summary>
    public List<CommsMessage> Inbox { get; } = new();
    /// <summary>Runde 40: nächste Nachrichten-Id (fortlaufend, gespeichert).</summary>
    public int NextMessageId { get; set; } = 1;

    /// <summary>
    /// Runde 41: Auftragsangebote (<see cref="Missions.MissionBoardSystem"/>; eigener Zufall aus dem Welt-Seed, nie <see cref="Rng"/>).
    /// Additiv gespeichert, aber <b>nicht gehasht</b> — reine Anzeige, bis der Spieler annimmt; Angebote ändern keinen Hash.
    /// </summary>
    public List<MissionOffer> MissionOffers { get; } = new();
    /// <summary>
    /// Runde 41: angenommene Aufträge (Lieferung, Transport). Gespeichert und <b>gehasht</b>, sobald welche laufen — erst eine Annahme
    /// des Spielers ändert den Hash. Abgeschlossene/gescheiterte Aufträge werden entfernt (ihre Geldflüsse stehen in den Ereignissen).
    /// </summary>
    public List<Mission> Missions { get; } = new();
    /// <summary>Runde 41: nächste Auftrags-Id (fortlaufend, gespeichert, nicht gehasht).</summary>
    public int NextMissionId { get; set; } = 1;
    /// <summary>
    /// Runde 41: Budget-/Sperrbuchaltung der Aufträge je Auftraggeber (Akteur-Id). Wird nur durch Spielerhandlungen geschrieben
    /// (Annahme, Scheitern) — ohne angenommenen Auftrag leer, der NPC-Hash ändert sich nicht. Gespeichert und gehasht, sobald Einträge existieren.
    /// </summary>
    public Dictionary<int, MissionFactionState> MissionFactions { get; } = new();

    /// <summary>Zählt Änderungen an Sektorinhalten (Stationen gebaut/entfernt), damit die Darstellung neu aufbauen kann.</summary>
    public int StructureVersion { get; private set; }

    int _nextShipId = 1000, _nextStationId = 1000;
    public int NextAsteroidId { get; set; } = 1;
    public int NewAsteroidId() => NextAsteroidId++;
    public ShipId NewShipId() => new(_nextShipId++);
    public StationId NewStationId() => new(_nextStationId++);
    /// <summary>Für den Spielstand: Zähler wiederherstellen.</summary>
    public (int ship, int station) IdCounters { get => (_nextShipId, _nextStationId); set => (_nextShipId, _nextStationId) = value; }

    /// <summary>Ware über ihren Datenschlüssel; null, wenn unbekannt.</summary>
    public Good? FindGood(string key) => Goods.Values.FirstOrDefault(g => g.Key == key);

    // Phase 17 (Leistung): Waren als Feld nach Id (Index = GoodId.Value) für die heißen Schleifen (Markt, Handel). Je Tick (und bei geänderter Anzahl)
    // neu gefüllt — die Warenliste ändert sich nur beim Weltaufbau/Laden, die Warenobjekte selbst werden nicht kopiert.
    Good?[] _goodsById = Array.Empty<Good?>();
    long _goodsTick = -1;
    int _goodsCount = -1;

    /// <summary>Phase 17: obere Schranke der Waren-Ids (größte Id + 1) — Länge für Felder, die je Ware indiziert werden.</summary>
    public int GoodIdBound
    {
        get
        {
            if (_goodsTick != Tick || _goodsCount != Goods.Count) RebuildGoods();
            return _goodsById.Length;
        }
    }

    /// <summary>Ware zur Id wie <c>Goods[id]</c> (wirft bei unbekannter Id), über ein je Tick aufgebautes Feld statt des Dictionarys.</summary>
    public Good GoodOf(GoodId id)
    {
        if (_goodsTick != Tick || _goodsCount != Goods.Count) RebuildGoods();
        int i = id.Value;
        return (uint)i < (uint)_goodsById.Length && _goodsById[i] is { } g ? g : Goods[id];
    }

    void RebuildGoods()
    {
        int max = 0;
        foreach (var k in Goods.Keys) max = Math.Max(max, k.Value);
        if (_goodsById.Length != max + 1) _goodsById = new Good?[max + 1]; else Array.Clear(_goodsById);
        foreach (var (k, g) in Goods) if (k.Value >= 0) _goodsById[k.Value] = g;
        _goodsTick = Tick; _goodsCount = Goods.Count;
    }

    // ───────── Schiffe ─────────

    public void AddShip(Ship s)
    {
        // Schiffe mit auflösbarem Modell bekommen Stats (und Fähigkeiten, sofern nicht ausdrücklich gesetzt) aus dem Modell
        bool modelOk = s.HasModel && s.RecomputeStats(this);
        // Fähigkeiten/Steuerer aus der Rolle ableiten, nur wenn der Ersteller sie nicht ausdrücklich gesetzt hat
        if (!s.CapabilitiesSet && !modelOk) s.Capabilities = CapabilityDefaults.ForRole(s.Role);
        if (!s.ControllerSet) s.Controller = CapabilityDefaults.ControllerFor(s.Role);
        // Phase 9: ohne Besitzer gehört ein Spielerschiff dem Spieler, jedes andere ist ein freies Einzelschiff (Sammelgruppe „Freie“)
        if (s.Owner == ActorId.None) s.Owner = s.IsPlayer ? PlayerActor : FreeActor;
        // Phase 4: leerer Tank wird vollgetankt (erster zulässiger Treibstoff); Schiffe ohne Modell/Antrieb bleiben leer
        if (s.Fuel.Count == 0) s.FillFuel();
        Ships[s.Id] = s;
        if (s.Transit is { } t) Tracker.AddTransit(t, s.Id); else Tracker.AddParked(s.Sector, s.Id);
        if (s.Id.Value >= _nextShipId) _nextShipId = s.Id.Value + 1;
        Events.Publish(new ShipSpawned(s.Id));
    }

    /// <summary>
    /// Schließt die Lücke, die ein Remove im Dictionary hinterlässt (neu aufbauen in bisheriger Reihenfolge). TrimExcess genügt dafür nicht:
    /// es verdichtet nur, wenn die Kapazität dabei schrumpft.
    /// </summary>
    static void Compact<TKey, TValue>(Dictionary<TKey, TValue> d) where TKey : notnull
    {
        var items = d.ToList();
        d.Clear();
        foreach (var kv in items) d.Add(kv.Key, kv.Value);
    }

    public void RemoveShip(ShipId id, string reason)
    {
        if (!Ships.TryGetValue(id, out var s)) return;
        if (s.Transit is { } t) Tracker.RemoveTransit(t, id); else Tracker.RemoveParked(s.Sector, id);
        Ships.Remove(id);
        if (Journal.Last is { } je && je.Ship == id) Journal.Clear();   // Runde 39: Rücknahme mit dem Schiff weg
        // Phase 13 (Determinismus): Lücken schließen, damit spätere Schiffe hinten angehängt werden wie in einem geladenen Stand — sonst landet ein
        // neues Schiff im frei gewordenen Platz, und Systeme, die Ships.Values durchlaufen, sähen nach Speichern/Laden eine andere Reihenfolge
        Compact(Ships);
        Simulation.Squadrons.OnShipRemoved(this, s);   // Phase 12: Verband verlassen, Anführer folgt nach
        Fights.OnShipRemoved(id);   // Phase 24: aus dem Gefecht ausscheiden
        Events.Publish(new ShipRemoved(id, reason));
    }

    public void StartTransit(Ship s, Transit t)
    {
        if (s.Transit is { } old) Tracker.RemoveTransit(old, s.Id); else Tracker.RemoveParked(s.Sector, s.Id);
        s.Transit = t;
        s.DockedAt = default;   // Phase 12: wer abfliegt, liegt nicht mehr angedockt
        if (Journal.Last is { } je && je.Ship == s.Id) Journal.Clear();   // Runde 39: Abflug löscht die Rücknahme
        Tracker.AddTransit(t, s.Id);
    }

    /// <summary>Ankunft: Schiff wird am Zielort geparkt.</summary>
    public void FinishTransit(Ship s)
    {
        var t = s.Transit!;
        Tracker.RemoveTransit(t, s.Id);
        s.Transit = null;
        s.Sector = t.ToSector;
        s.Position = t.ToPosition;
        s.DockedAt = StationAt(s.Sector, s.Position);   // Phase 12: Ankunft am Andockpunkt = angedockt
        Tracker.AddParked(s.Sector, s.Id);
        Events.Publish(new ShipArrived(s.Id, s.Sector, s.Position));   // Runde 42: reine Meldung (z. B. Eskorte), nicht gespeichert/gehasht
        if (s.DockedAt != default) Events.Publish(new ShipDocked(s.Id, s.DockedAt));   // Runde 42b: Andocken an einer Station (z. B. Transport-Ablieferung)
    }

    /// <summary>Phase 12: Höchstabstand vom Stationszentrum, in dem ein liegendes Schiff als angedockt gilt (Andockpunkte liegen bei 22).</summary>
    public const float DockRadius = 30f;

    /// <summary>Phase 12: Station, an deren Andockpunkt diese Position liegt (nächste innerhalb <see cref="DockRadius"/>, bei Gleichstand kleinste Id); default = keine.</summary>
    public StationId StationAt(SectorId sector, Vec2 pos)
    {
        if (!Sectors.TryGetValue(sector, out var sec)) return default;
        // Phase 30: Abstand zur Außenhaut (Liegeplatz Radius + 15, bei einer mittleren Station wie bisher 30); Kleinstationen (XS) sind nicht andockbar
        StationId best = default;
        float bestD = DockRadius - Station.BodyRadius;
        foreach (var id in sec.Stations.OrderBy(x => x.Value))
        {
            if (!Stations.TryGetValue(id, out var st) || !st.Dockable) continue;
            float d = Vec2.Distance(pos, st.Position) - st.Radius;
            if (d <= bestD && (best.Value == 0 || d < bestD)) { bestD = d; best = id; }
        }
        return best;
    }

    /// <summary>Setzt ein geparktes Schiff in einen anderen Sektor (z.B. Sprung des Spielers).</summary>
    public void MoveShip(Ship ship, SectorId to)
    {
        Tracker.RemoveParked(ship.Sector, ship.Id);
        ship.Sector = to;
        ship.DockedAt = default;
        if (Journal.Last is { } je && je.Ship == ship.Id) Journal.Clear();   // Runde 39: Sprung löscht die Rücknahme
        Tracker.AddParked(to, ship.Id);
    }

    /// <summary>Baut den abgeleiteten Schiffsindex neu auf (nach dem Laden).</summary>
    public void RebuildTracker()
    {
        foreach (var s in Ships.Values)
            if (s.Transit is { } t) Tracker.AddTransit(t, s.Id); else Tracker.AddParked(s.Sector, s.Id);
    }

    // ───────── Stationen ─────────

    public void AddStation(Station st)
    {
        Stations[st.Id] = st;
        var sector = Sectors[st.Sector];
        if (!sector.Stations.Contains(st.Id)) sector.Stations.Add(st.Id);
        if (st.Id.Value >= _nextStationId) _nextStationId = st.Id.Value + 1;
        StructureVersion++;
    }

    /// <summary>Entfernt eine Station; Schiffe, deren Auftrag sie betrifft, verlieren Auftrag und (gebundene) Fracht.</summary>
    public void RemoveStation(StationId id, string reason)
    {
        if (!Stations.TryGetValue(id, out var st)) return;
        Stations.Remove(id);
        Compact(Stations);   // wie bei RemoveShip: Einfügereihenfolge wie nach dem Laden
        Sectors[st.Sector].Stations.Remove(id);
        if (Journal.Last is { } je && je.Station == id) Journal.Clear();   // Runde 39: Station weg löscht die Rücknahme
        foreach (var ship in Ships.Values)
        {
            if (ship.DockedAt == id) ship.DockedAt = default;   // Phase 12: Liegeplatz weg
            if (ship.HomeBase == id) ship.HomeBase = default;
            bool affected = ship.Order switch
            {
                TradeOrder o => o.Buy == id || o.Sell == id,
                MineOrder m => m.DeliverTo == id,
                GoToOrder g => g.Target == id,
                BuildOrder b => b.Site == id || b.Supplier == id,
                _ => false,
            };
            if (!affected) continue;
            ship.Order = null;
            ship.Phase = TradePhase.Idle;
            ship.MinePhase = MinePhase.Idle;
            if (!ship.IsPlayer) ship.Cargo.Clear();   // Fracht eigener Schiffe bleibt an Bord
        }
        foreach (var sq in Squadrons.Values) if (sq.HomeBase == id) sq.HomeBase = default;
        StructureVersion++;
        Events.Publish(new StationRemoved(id, st.Sector, reason));
    }

    /// <summary>
    /// Ersetzt eine Station gleicher Id an Ort und Stelle (Phase 8: fertige Baustelle wird zur Station), ohne Aufträge anderer Schiffe zu
    /// verwerfen; die Darstellung und der Marktindex bauen neu auf.
    /// </summary>
    public void ReplaceStation(Station st)
    {
        Stations[st.Id] = st;
        var sector = Sectors[st.Sector];
        if (!sector.Stations.Contains(st.Id)) sector.Stations.Add(st.Id);
        StructureVersion++;
    }

    /// <summary>Markiert eine Änderung an Sektorinhalten (z. B. Baustelle verlassen, Asteroid gespalten), damit Darstellung und Marktindex neu aufbauen.</summary>
    public void TouchStructure() => StructureVersion++;

    public void NotifyStationBuilt(Station st) => Events.Publish(new StationBuilt(st.Id, st.Sector, st.TypeKey));

    // ───────── Raum ─────────

    /// <summary>Statische Hindernisse eines Sektors: Asteroiden und Stationskörper.</summary>
    public List<Obstacle> GetObstacles(SectorId sector)
    {
        var s = Sectors[sector];
        var list = new List<Obstacle>(s.Asteroids.Count + s.Stations.Count);
        foreach (var a in s.Asteroids) list.Add(a.ToObstacle());
        foreach (var id in s.Stations) list.Add(new Obstacle(Stations[id].Position, Stations[id].Radius));   // Phase 30: Radius der Größenklasse
        return list;
    }

    /// <summary>
    /// Kürzester Weg als Liste der zu durchfliegenden Gates (BFS), null wenn nicht erreichbar. Ohne Profil zählt nur der physische Zustand der
    /// Gates (Weltkarte); mit Mobilitätsprofil (Phase 10) nur Gates, die der Zugang für dieses Profil nicht abweist
    /// (<see cref="Simulation.GateAccess.CanTraverse"/>). Zoll verhindert die Passage nicht.
    /// </summary>
    public List<GateId>? FindRoute(SectorId from, SectorId to) => FindRoute(from, to, MobilityProfile.Physical);

    /// <inheritdoc cref="FindRoute(SectorId, SectorId)"/>
    public List<GateId>? FindRoute(SectorId from, SectorId to, MobilityProfile profile)
    {
        if (from == to) return new List<GateId>();
        if (!Sectors.ContainsKey(from)) return null;
        profile = Normalize(profile);
        var prev = new Dictionary<SectorId, (SectorId from, GateId gate)>();
        var seen = new HashSet<SectorId> { from };
        var queue = new Queue<SectorId>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            foreach (var gid in Sectors[s].Gates)
            {
                var gate = Gates[gid];
                if (!gate.Open || !Gates.TryGetValue(gate.Target, out var far)) continue;
                if (seen.Contains(far.Sector)) continue;
                if (!profile.IsPhysical && !Simulation.GateAccess.CanTraverse(this, gate, profile)) continue;
                seen.Add(far.Sector);
                prev[far.Sector] = (s, gid);
                if (far.Sector == to)
                {
                    var route = new List<GateId>();
                    for (var cur = to; cur != from; cur = prev[cur].from) route.Insert(0, prev[cur].gate);
                    return route;
                }
                queue.Enqueue(far.Sector);
            }
        }
        return null;
    }

    /// <summary>Anzahl Gate-Sprünge zwischen zwei Sektoren, -1 wenn nicht erreichbar (physisch).</summary>
    public int Hops(SectorId from, SectorId to) => FindRoute(from, to)?.Count ?? -1;

    /// <summary>
    /// Zählt Änderungen am Gate-Zugang (Sperrstufen, Gate-Zustände, Sektorbesitz, Beziehungen mit Wirkung). Routen-Cache und
    /// <see cref="AccessRestricted"/> bauen danach neu auf. Abgeleitet, nicht gespeichert.
    /// </summary>
    public int AccessVersion { get; private set; }

    /// <summary>Markiert eine Änderung am Gate-Zugang (Phase 10).</summary>
    public void TouchAccess() => AccessVersion++;

    bool _restricted;
    long _restrictedTick = -1;
    int _restrictedVersion = -1;

    /// <summary>
    /// Gilt irgendwo eine Sperre oder ist ein Gate nicht offen? Ohne jede Einschränkung ist jedes Profil gleichwertig mit dem physischen
    /// (gemeinsamer Routen-Cache, keine Zugangsprüfung) — der Normalfall der Startwelt kostet so nichts.
    /// </summary>
    public bool AccessRestricted
    {
        get
        {
            if (_restrictedTick != Tick || _restrictedVersion != AccessVersion)
            {
                _restricted = Sectors.Values.Any(s => !s.Access.IsOpen) || Gates.Values.Any(g => g.State != GateState.Open);
                _restrictedTick = Tick; _restrictedVersion = AccessVersion;
            }
            return _restricted;
        }
    }

    /// <summary>Ohne Einschränkungen ist jedes Profil physisch (gleiche Routen, gemeinsamer Cache).</summary>
    MobilityProfile Normalize(MobilityProfile p) => p.IsPhysical || !AccessRestricted ? MobilityProfile.Physical : p;

    // Routen werden je Tick zwischengespeichert: KI-Systeme fragen sie in verschachtelten Schleifen ab
    // (Gates können sich zwischen Ticks ändern, innerhalb eines Ticks nur über TouchAccess). Phase 10: Cache nach Profil getrennt.
    readonly Dictionary<(SectorId, SectorId, MobilityProfile), List<GateId>?> _routes = new();
    long _routesTick = -1;
    int _routesVersion = -1, _routesGates = -1, _routesSectors = -1;
    bool _routesUnrestricted;

    public List<GateId>? CachedRoute(SectorId from, SectorId to) => CachedRoute(from, to, MobilityProfile.Physical);

    public List<GateId>? CachedRoute(SectorId from, SectorId to, MobilityProfile profile)
    {
        if (_routesTick != Tick || _routesVersion != AccessVersion)
        {
            // Phase 17 (Leistung): Ohne jede Einschränkung (alle Gates offen, keine Sperre) hängen Routen nur an der Gate-Topologie — dann bleibt der
            // Cache über Ticks stehen, solange auch beim Füllen nichts eingeschränkt war und die Zahl der Sektoren und Gates gleich ist
            bool keep = _routesVersion == AccessVersion && _routesUnrestricted && !AccessRestricted
                && Gates.Count == _routesGates && Sectors.Count == _routesSectors;
            if (!keep) _routes.Clear();
            _routesTick = Tick; _routesVersion = AccessVersion;
            _routesUnrestricted = !AccessRestricted; _routesGates = Gates.Count; _routesSectors = Sectors.Count;
        }
        profile = Normalize(profile);
        if (!_routes.TryGetValue((from, to, profile), out var r)) _routes[(from, to, profile)] = r = FindRoute(from, to, profile);
        return r;
    }

    public bool IsReachable(SectorId from, SectorId to) => CachedRoute(from, to) is not null;

    /// <summary>Erreichbar für dieses Mobilitätsprofil (Phase 10)?</summary>
    public bool IsReachable(SectorId from, SectorId to, MobilityProfile profile) => CachedRoute(from, to, profile) is not null;

    // Marktindex: Welche Stationen führen eine Ware? Händler-KI muss so nicht für jede Ware alle Stationen durchsuchen.
    readonly Dictionary<GoodId, List<Station>> _tradingIndex = new();
    long _indexTick = -1;
    int _indexVersion = -1, _indexEntries = -1, _indexStations = -1;

    /// <summary>Stationen mit Marktposition für diese Ware (je Tick und Stationsänderung zwischengespeichert).</summary>
    public IReadOnlyList<Station> StationsTrading(GoodId good)
    {
        if (_indexTick != Tick || _indexVersion != StructureVersion)
        {
            // Phase 17 (Leistung): Der Index hängt nur an der Stationsmenge und den Markteinträgen. Markteinträge kommen zur Laufzeit nur hinzu
            // (Station.Stock), also zeigt die Gesamtzahl der Einträge jede Änderung an; ist sie wie beim letzten Aufbau (und die Stationsmenge gleich),
            // bleibt der Index stehen, statt je Tick neu gebaut zu werden. Innerhalb eines Ticks gilt wie bisher nur die Strukturversion.
            int entries = 0;
            foreach (var st in Stations.Values) entries += st.Market.Count;
            if (_indexVersion == StructureVersion && entries == _indexEntries && Stations.Count == _indexStations) { _indexTick = Tick; goto done; }
            _indexEntries = entries; _indexStations = Stations.Count;
            _tradingIndex.Clear();   // neue Listen: eine laufende Schleife über eine alte Liste behält ihren Schnappschuss
            // feste Reihenfolge (Id), nicht die Einfügereihenfolge des Dictionarys (entfernte Stationen geben Plätze frei)
            _tradingEntries.Clear();
            foreach (var st in StationsById())
                foreach (var (g, e) in st.Market)
                {
                    if (!_tradingIndex.TryGetValue(g, out var list)) { _tradingIndex[g] = list = new List<Station>(); _tradingEntries[g] = new List<StockEntry>(); }
                    list.Add(st);
                    _tradingEntries[g].Add(e);
                }
            _indexTick = Tick; _indexVersion = StructureVersion;
        }
        done:
        return _tradingIndex.TryGetValue(good, out var l) ? l : Array.Empty<Station>();
    }

    readonly Dictionary<GoodId, List<StockEntry>> _tradingEntries = new();

    /// <summary>
    /// Phase 17 (Leistung): die Markteinträge dieser Ware in derselben Reihenfolge wie <see cref="StationsTrading"/> (Eintrag i gehört zu Station i).
    /// Nur direkt nach <see cref="StationsTrading"/> für dieselbe Ware verwenden (gleicher Indexstand).
    /// </summary>
    public IReadOnlyList<StockEntry> StationsTradingEntries(GoodId good) =>
        _tradingEntries.TryGetValue(good, out var l) ? l : Array.Empty<StockEntry>();

    // Phase 17 (Leistung): Schiffe und Stationen nach Id geordnet, zwischengespeichert. Gültig, solange das Dictionary dieselben Objekte enthält
    // (gleiche Anzahl, jedes gemerkte Objekt noch unter seiner Id) — exakt geprüft bei jedem Aufruf, auch wenn Tests das Dictionary direkt ändern.
    // Ein Neuaufbau legt eine neue Liste an, damit eine laufende Schleife über die alte Liste ihren Schnappschuss behält (wie vorher mit ToList()).
    List<Ship> _shipsById = new();
    List<Station> _stationsById = new();

    /// <summary>Alle Schiffe nach Id geordnet (Schnappschuss; gleiche Reihenfolge wie <c>Ships.Values.OrderBy(s =&gt; s.Id.Value)</c>).</summary>
    public IReadOnlyList<Ship> ShipsById()
    {
        if (!SnapshotValid(_shipsById, Ships, s => s.Id)) _shipsById = Ships.Values.OrderBy(s => s.Id.Value).ToList();
        return _shipsById;
    }

    /// <summary>Alle Stationen nach Id geordnet (Schnappschuss; gleiche Reihenfolge wie <c>Stations.Values.OrderBy(s =&gt; s.Id.Value)</c>).</summary>
    public IReadOnlyList<Station> StationsById()
    {
        if (!SnapshotValid(_stationsById, Stations, s => s.Id)) _stationsById = Stations.Values.OrderBy(s => s.Id.Value).ToList();
        return _stationsById;
    }

    // Punkt 13 (Rechenzeit): Stationen, deren Typ eine Regierungsart hat (Kandidaten für FactionViability.ObligatoryPerTick), nach Id. Gültig, solange
    // StationsById dieselbe Liste liefert (gleiche Stationsmenge) und jeder Stationstyp dasselbe Objekt mit derselben Regierungsart ist. Der Typ einer
    // Station steht nur bei ihrer Erstellung fest (StationFactory, Fertigstellung ersetzt das Objekt).
    List<Station> _govCandidates = new();
    IReadOnlyList<Station>? _govFor;
    StationType[] _govTypes = Array.Empty<StationType>();
    GovernmentKind[] _govKinds = Array.Empty<GovernmentKind>();

    /// <summary>Punkt 13: Stationen mit Regierungstyp nach Id (Obermenge für Prüfungen, die Typ und Besitzer selbst noch einmal ansehen).</summary>
    public IReadOnlyList<Station> GovernmentCandidates()
    {
        var all = StationsById();
        if (!PerfCaches) return all;
        bool valid = ReferenceEquals(all, _govFor) && _govTypes.Length == StationTypes.Count;
        if (valid)
        {
            int i = 0;
            foreach (var t in StationTypes.Values)
            {
                if (!ReferenceEquals(t, _govTypes[i]) || t.Government != _govKinds[i]) { valid = false; break; }
                i++;
            }
        }
        if (!valid)
        {
            _govFor = all;
            _govTypes = StationTypes.Values.ToArray();
            _govKinds = _govTypes.Select(t => t.Government).ToArray();
            var list = new List<Station>();
            foreach (var st in all)
                if (StationTypes.TryGetValue(st.TypeKey, out var t) && t.Government != GovernmentKind.None) list.Add(st);
            _govCandidates = list;
        }
        return _govCandidates;
    }

    static bool SnapshotValid<TId, T>(List<T> list, Dictionary<TId, T> dict, Func<T, TId> id) where TId : notnull where T : class
    {
        if (list.Count != dict.Count) return false;
        foreach (var x in list)
            if (!dict.TryGetValue(id(x), out var cur) || !ReferenceEquals(cur, x)) return false;
        return true;
    }
}
