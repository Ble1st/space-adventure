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
    public Dictionary<string, AsteroidClass> AsteroidClasses = new();
    /// <summary>Slotarten (Content, leer wenn die Weltdatei keinen Abschnitt hat).</summary>
    public Dictionary<string, SlotType> SlotTypes = new();
    /// <summary>Module (Content).</summary>
    public Dictionary<string, Module> Modules = new();
    /// <summary>Schiffsklassen (Content).</summary>
    public Dictionary<string, ShipClass> ShipClasses = new();
    /// <summary>Schiffsmodelle (Content).</summary>
    public Dictionary<string, ShipModel> ShipModels = new();
    public EconomyRules Rules = new();
    public long LastStationBuildTick = -1_000_000;

    /// <summary>Der einzige Sektor, der als Godot-Szene materialisiert ist.</summary>
    public SectorId PlayerSector;
    public ShipId PlayerShip;

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
            Actors[FreeActor] = new Actor { Id = FreeActor, Name = "Freie Kapitäne", Kind = ActorKind.Free };
        if (!Actors.ContainsKey(PlayerActor))
            Actors[PlayerActor] = new Actor { Id = PlayerActor, Name = "Du", Kind = ActorKind.Player, ShipsUseTreasury = true };
        if (NextActorId <= Math.Max(FreeActor.Value, PlayerActor.Value)) NextActorId = Math.Max(FreeActor.Value, PlayerActor.Value) + 1;
    }

    /// <summary>Neue Akteur-Id (fortlaufend ab <see cref="ActorId.FirstGenerated"/>).</summary>
    public ActorId NewActorId() => new(NextActorId++);

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

    /// <summary>Ereignisbus und Schiffsindex sind abgeleitet und gehören nicht zum Spielstand.</summary>
    public SimEvents Events { get; } = new();
    public ShipTracker Tracker { get; } = new();

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

    public void RemoveShip(ShipId id, string reason)
    {
        if (!Ships.TryGetValue(id, out var s)) return;
        if (s.Transit is { } t) Tracker.RemoveTransit(t, id); else Tracker.RemoveParked(s.Sector, id);
        Ships.Remove(id);
        Events.Publish(new ShipRemoved(id, reason));
    }

    public void StartTransit(Ship s, Transit t)
    {
        if (s.Transit is { } old) Tracker.RemoveTransit(old, s.Id); else Tracker.RemoveParked(s.Sector, s.Id);
        s.Transit = t;
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
        Tracker.AddParked(s.Sector, s.Id);
    }

    /// <summary>Setzt ein geparktes Schiff in einen anderen Sektor (z.B. Sprung des Spielers).</summary>
    public void MoveShip(Ship ship, SectorId to)
    {
        Tracker.RemoveParked(ship.Sector, ship.Id);
        ship.Sector = to;
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
        Sectors[st.Sector].Stations.Remove(id);
        foreach (var ship in Ships.Values)
        {
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
            ship.Cargo.Clear();
        }
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
        foreach (var id in s.Stations) list.Add(new Obstacle(Stations[id].Position, Station.BodyRadius));
        return list;
    }

    /// <summary>Kürzester Weg als Liste der zu durchfliegenden Gates (BFS), null wenn nicht erreichbar.</summary>
    public List<GateId>? FindRoute(SectorId from, SectorId to)
    {
        if (from == to) return new List<GateId>();
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
                if (!seen.Add(far.Sector)) continue;
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

    /// <summary>Anzahl Gate-Sprünge zwischen zwei Sektoren, -1 wenn nicht erreichbar.</summary>
    public int Hops(SectorId from, SectorId to) => FindRoute(from, to)?.Count ?? -1;

    // Routen werden je Tick zwischengespeichert: KI-Systeme fragen sie in verschachtelten Schleifen ab
    // (Gates können sich zwischen Ticks ändern, innerhalb eines Ticks nicht).
    readonly Dictionary<(SectorId, SectorId), List<GateId>?> _routes = new();
    long _routesTick = -1;

    public List<GateId>? CachedRoute(SectorId from, SectorId to)
    {
        if (_routesTick != Tick) { _routes.Clear(); _routesTick = Tick; }
        if (!_routes.TryGetValue((from, to), out var r)) _routes[(from, to)] = r = FindRoute(from, to);
        return r;
    }

    public bool IsReachable(SectorId from, SectorId to) => CachedRoute(from, to) is not null;

    // Marktindex: Welche Stationen führen eine Ware? Händler-KI muss so nicht für jede Ware alle Stationen durchsuchen.
    readonly Dictionary<GoodId, List<Station>> _tradingIndex = new();
    long _indexTick = -1;
    int _indexVersion = -1;

    /// <summary>Stationen mit Marktposition für diese Ware (je Tick und Stationsänderung zwischengespeichert).</summary>
    public IReadOnlyList<Station> StationsTrading(GoodId good)
    {
        if (_indexTick != Tick || _indexVersion != StructureVersion)
        {
            _tradingIndex.Clear();
            // feste Reihenfolge (Id), nicht die Einfügereihenfolge des Dictionarys (entfernte Stationen geben Plätze frei)
            foreach (var st in Stations.Values.OrderBy(s => s.Id.Value))
                foreach (var g in st.Market.Keys)
                {
                    if (!_tradingIndex.TryGetValue(g, out var list)) _tradingIndex[g] = list = new List<Station>();
                    list.Add(st);
                }
            _indexTick = Tick; _indexVersion = StructureVersion;
        }
        return _tradingIndex.TryGetValue(good, out var l) ? l : Array.Empty<Station>();
    }
}
