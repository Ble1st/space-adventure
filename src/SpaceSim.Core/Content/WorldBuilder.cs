using System.Reflection;
using System.Text.Json;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Model;
using Module = SpaceSim.Core.Model.Module;

namespace SpaceSim.Core.Content;

public sealed class WorldFormatException : Exception
{
    public WorldFormatException(string message) : base(message) { }
}

/// <summary>Baut aus einer Weltbeschreibung (JSON) ein Universum. Core selbst liest keine Dateien; Aufrufer liefern den Text.</summary>
public static class WorldBuilder
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static WorldDef Parse(string json)
    {
        try { return JsonSerializer.Deserialize<WorldDef>(json, Json) ?? throw new WorldFormatException("Leere Weltdatei"); }
        catch (JsonException e) { throw new WorldFormatException("Weltdatei ist kein gültiges JSON: " + e.Message); }
    }

    /// <summary>Die mitgelieferte Standardwelt (eingebettete Ressource).</summary>
    public static string DefaultWorldJson()
    {
        var asm = typeof(WorldBuilder).Assembly;
        using var stream = asm.GetManifestResourceStream("SpaceSim.Core.Content.default-world.json")
                           ?? throw new WorldFormatException("Eingebettete Standardwelt fehlt");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Baut die Standardwelt; <paramref name="startOption"/> wählt ein Startpaket (<c>player.startOptions</c>), null = Spielereintrag wie bisher.</summary>
    public static Universe BuildDefault(ulong? seed = null, string? startOption = null) => Build(Parse(DefaultWorldJson()), seed, startOption);

    /// <summary>Die Startpakete der Standardwelt (für die Auswahl bei „Neues Spiel“).</summary>
    public static IReadOnlyList<StartOptionDef> DefaultStartOptions() => Parse(DefaultWorldJson()).Player.StartOptions;

    /// <summary>
    /// Baut ein Universum aus der Weltbeschreibung. <paramref name="startOption"/> ist der Schlüssel eines Startpakets aus
    /// <c>player.startOptions</c> (Phase 5c): Es bestimmt Modell, Startcredits und optional den Startsektor des Spielerschiffs;
    /// null = der Spielereintrag selbst (bisheriges Verhalten). Ein unbekannter Schlüssel wirft <see cref="WorldFormatException"/>.
    /// </summary>
    public static Universe Build(WorldDef def, ulong? seedOverride = null, string? startOption = null)
    {
        var u = new Universe { Seed = seedOverride ?? def.Seed };
        u.Rng = new DeterministicRng(u.Seed);
        if (def.Rules is not null) u.Rules = def.Rules;

        // Waren: GoodId = Position + 1, Schlüssel für Verweise
        var goodByKey = new Dictionary<string, GoodId>();
        for (int i = 0; i < def.Goods.Count; i++)
        {
            var g = def.Goods[i];
            if (!goodByKey.TryAdd(g.Key, new GoodId(i + 1))) throw new WorldFormatException($"Ware '{g.Key}' doppelt");
            u.Goods[new GoodId(i + 1)] = new Good { Id = new GoodId(i + 1), Key = g.Key, Name = g.Name, Color = g.Color, BasePrice = g.BasePrice, VolumePerUnit = g.Volume, Form = ParseForm(g.Form, g.Key), Category = g.Category ?? "" };
        }
        GoodId Good(string key) =>
            goodByKey.TryGetValue(key, out var id) ? id : throw new WorldFormatException($"Unbekannte Ware '{key}'");

        // Slot-/Modul-Content (Phase 3: Daten + Validierung; wird von Schiffen über Modell/Slots/Fit genutzt)
        LoadSlotContent(u, def);

        // Phase 5a: Module sind Waren — je Modul eine Ware gleichen Schlüssels, hinter den JSON-Waren nach Modulschlüssel,
        // damit die GoodIds der JSON-Waren (und die DemoUniverse-Konstanten) stabil bleiben
        foreach (var m in ModuleGoods.InGoodOrder(u.Modules.Values, m => m.Key))
        {
            if (goodByKey.ContainsKey(m.Key)) throw new WorldFormatException($"Modul '{m.Key}': eine Ware gleichen Schlüssels steht schon in goods");
            var id = new GoodId(u.Goods.Count + 1);
            u.Goods[id] = ModuleGoods.Create(u, m, id);
            goodByKey[m.Key] = id;
        }

        foreach (var c in def.AsteroidClasses)
        {
            var cls = new AsteroidClass { Key = c.Key, Name = c.Name, Color = c.Color, ChunkChance = c.ChunkChance, OreFactor = c.OreFactor };
            foreach (var (k, w) in c.Yields) cls.Yields.Add(new Yield(Good(k), w));
            if (cls.Yields.Count == 0) throw new WorldFormatException($"Asteroidenklasse '{c.Key}' hat keine Erzsorten");
            if (!u.AsteroidClasses.TryAdd(cls.Key, cls)) throw new WorldFormatException($"Asteroidenklasse '{c.Key}' doppelt");
        }

        var knownServices = Services.StationServices.Default();
        foreach (var t in def.StationTypes)
        {
            var type = new StationType
            {
                Key = t.Key, Name = t.Name, BuildCost = t.BuildCost, UpkeepPerTick = t.Upkeep, IncomePerTick = t.Income,
                Services = t.Services is null ? new List<string> { Services.TradeService.ServiceKey, Services.HangarService.ServiceKey } : new List<string>(t.Services),
            };
            var seenServices = new HashSet<string>();
            foreach (var key in type.Services)
            {
                if (!knownServices.TryGet(key, out _)) throw new WorldFormatException($"Stationstyp '{t.Key}': unbekannter Dienst '{key}'");
                if (!seenServices.Add(key)) throw new WorldFormatException($"Stationstyp '{t.Key}': Dienst '{key}' doppelt");
            }
            foreach (var r in t.Recipes)
            {
                var recipe = new Recipe { TicksPerCycle = r.Ticks };
                foreach (var (k, v) in r.In) recipe.Inputs[Good(k)] = v;
                foreach (var (k, v) in r.Out) recipe.Outputs[Good(k)] = v;
                if (Simulation.FeatureSwitches.OmitRecipe(u, recipe)) continue;   // Phase 23: Munition abgeschaltet
                type.Recipes.Add(recipe);
            }
            // Phase 5a: Lager ohne Rezept (stocks) und Modulfabrik (je Modul ein Rezept aus seiner cost)
            if (t.Stocks is not null)
                foreach (var (k, v) in t.Stocks)
                {
                    if (!goodByKey.TryGetValue(k, out var gid)) throw new WorldFormatException($"Stationstyp '{t.Key}': unbekannte Ware '{k}' in stocks");
                    if (v.Length != 2) throw new WorldFormatException($"Stationstyp '{t.Key}': stocks '{k}' braucht [Anfangsbestand, Wunschbestand]");
                    if (v[0] < 0f || v[1] <= 0f) throw new WorldFormatException($"Stationstyp '{t.Key}': stocks '{k}' braucht Anfangsbestand >= 0 und Wunschbestand > 0");
                    if (Simulation.FeatureSwitches.OmitStock(u, gid)) continue;   // Phase 23: Munition abgeschaltet
                    type.Stocks.Add(new StationStock(gid, v[0], v[1]));
                }
            if (t.ModuleFactory is { } mf) AddModuleRecipes(u, t.Key, type, mf, Good);
            if (t.ShipDealer is { } sd)
            {
                if (sd.Classes is not null)
                    foreach (var c in sd.Classes)
                        if (!u.ShipClasses.ContainsKey(c)) throw new WorldFormatException($"Stationstyp '{t.Key}': unbekannte Schiffsklasse '{c}' in shipDealer");
                if (sd.Assembly < 0f) throw new WorldFormatException($"Stationstyp '{t.Key}': shipDealer.assembly muss >= 0 sein");
                type.ShipDealer = new ShipDealerConfig
                {
                    Classes = sd.Classes is null ? null : new List<string>(sd.Classes),
                    MaxSize = ParseSize(sd.MaxSize, $"Stationstyp '{t.Key}'"), Assembly = sd.Assembly,
                };
            }
            if (t.Construction is { } cons)
            {
                type.Construction = new Dictionary<GoodId, float>();
                foreach (var (k, v) in cons)
                {
                    if (!goodByKey.TryGetValue(k, out var gid)) throw new WorldFormatException($"Stationstyp '{t.Key}': unbekannte Ware '{k}' in construction");
                    if (v <= 0f) throw new WorldFormatException($"Stationstyp '{t.Key}': construction '{k}' muss > 0 sein");
                    if (type.Recipes.Any(r => r.Outputs.ContainsKey(gid))) throw new WorldFormatException($"Stationstyp '{t.Key}': construction verlangt '{k}', das der Typ selbst herstellt (zyklische Baukosten)");
                    type.Construction[gid] = v;
                }
            }
            // Phase 10: Regierungsform, Einfluss, Eroberbarkeit, Zerstörbarkeit
            type.Government = ParseGovernment(t.Government, t.Key);
            if (t.Influence < 0f || !float.IsFinite(t.Influence)) throw new WorldFormatException($"Stationstyp '{t.Key}': influence muss >= 0 sein");
            type.Influence = t.Influence;
            type.Capturable = t.Capturable && type.Government is not (GovernmentKind.Headquarters or GovernmentKind.CompanySeat or GovernmentKind.SectorHq or GovernmentKind.Office);
            type.Destructible = t.Destructible;
            // Phase 11: Grundschutz (Lagekarte) und Piratenversteck
            if (t.Security < 0f || !float.IsFinite(t.Security)) throw new WorldFormatException($"Stationstyp '{t.Key}': security muss >= 0 sein");
            type.Security = t.Security;
            type.Hideout = t.Hideout;
            if (type.Hideout && type.Government != GovernmentKind.None) throw new WorldFormatException($"Stationstyp '{t.Key}': ein Versteck kann keine Regierungsstation sein");
            // Phase 13: Forschungsbetrieb nur an Forschungsstationen (Regierungsform research), und eine Forschungsstation braucht ihn
            if (t.Research is { } rd)
            {
                if (type.Government != GovernmentKind.Research) throw new WorldFormatException($"Stationstyp '{t.Key}': research braucht government \"research\"");
                if (rd.Ticks <= 0 || rd.Points <= 0f || rd.Credits < 0f || rd.FundTarget < 0f || rd.FundPerTick < 0f)
                    throw new WorldFormatException($"Stationstyp '{t.Key}': research braucht ticks > 0, points > 0 und credits, fundTarget, fundPerTick >= 0");
                var cfg = new ResearchConfig { Ticks = rd.Ticks, Points = rd.Points, Credits = rd.Credits, FundTarget = rd.FundTarget, FundPerTick = rd.FundPerTick };
                foreach (var (k, v) in rd.Inputs)
                {
                    if (!goodByKey.TryGetValue(k, out var gid)) throw new WorldFormatException($"Stationstyp '{t.Key}': unbekannte Ware '{k}' in research.inputs");
                    if (v <= 0f) throw new WorldFormatException($"Stationstyp '{t.Key}': research.inputs '{k}' muss > 0 sein");
                    cfg.Inputs[gid] = v;
                }
                type.Research = cfg;
            }
            else if (type.Government == GovernmentKind.Research) throw new WorldFormatException($"Stationstyp '{t.Key}': eine Forschungsstation braucht den Abschnitt research");
            // Phase 14: Exportsenke — Waren aus Kategorien und Einzelnennung, als Lager ohne Rezept (Ankauf über den normalen Handel)
            if (t.Export is { } ex) type.Export = LoadExport(u, t.Key, type, ex);
            if (!u.StationTypes.TryAdd(type.Key, type)) throw new WorldFormatException($"Stationstyp '{t.Key}' doppelt");
        }

        // Phase 13: Forschungsprojekte (nach Modulen, Modellen und Stationstypen: Freischaltungen verweisen darauf)
        LoadResearch(u, def, Good);

        // Phase 9: jede Fraktion ist zugleich ein Akteur (gleiche Id-Zahl) mit Art, Persönlichkeit und Kasse; Hauptbeziehungen nur als Abweichung
        foreach (var f in def.Factions)
        {
            if (f.Id <= 0 || f.Id >= ActorId.FirstGenerated) throw new WorldFormatException($"Fraktion '{f.Name}': Id {f.Id} muss zwischen 1 und {ActorId.FirstGenerated - 1} liegen");
            var id = new FactionId(f.Id);
            if (!u.Factions.TryAdd(id, new Faction { Id = id, Name = f.Name })) throw new WorldFormatException($"Fraktion {f.Id} doppelt");
            var kind = f.Kind is null ? ActorKind.State
                : Enum.TryParse<ActorKind>(f.Kind, ignoreCase: true, out var k) && k is ActorKind.State or ActorKind.Guild or ActorKind.Outlaw ? k
                : throw new WorldFormatException($"Fraktion '{f.Name}': unbekannte Art '{f.Kind}' (state, guild, outlaw)");
            var p = f.Personality;
            u.Actors[id] = new Actor
            {
                Id = id, Name = f.Name, Kind = kind, Membership = id,
                Personality = p is null ? Personality.Neutral : new Personality(p.Aggression, p.Greed, p.Cooperation, p.Lawfulness).Clamped(),
                Treasury = new Treasury { Credits = f.Treasury },
            };
        }
        foreach (var f in def.Factions)
            foreach (var (other, v) in f.Relations ?? new Dictionary<int, float>())
            {
                if (!u.Factions.ContainsKey(new FactionId(other)) || other == f.Id) throw new WorldFormatException($"Fraktion '{f.Name}': Beziehung zu unbekannter Fraktion {other}");
                u.Factions[new FactionId(f.Id)].Relations[new FactionId(other)] = Math.Clamp(v, -1f, 1f);
            }

        // Phase 11: Akteure ohne Fraktion (Piraten) mit Schlüssel; Ids fortlaufend nach Freien und Spieler
        var actorByKey = new Dictionary<string, ActorId>(StringComparer.Ordinal);
        foreach (var ad in def.Actors)
        {
            if (ad.Key.Length == 0 || ad.Key is "faction" or "free" || !actorByKey.TryAdd(ad.Key, default))
                throw new WorldFormatException($"Akteur '{ad.Name}': Schlüssel '{ad.Key}' leer, reserviert oder doppelt");
            var kind = Enum.TryParse<ActorKind>(ad.Kind, ignoreCase: true, out var ak) && ak is ActorKind.Outlaw or ActorKind.Independent ? ak
                : throw new WorldFormatException($"Akteur '{ad.Key}': unbekannte Art '{ad.Kind}' (outlaw, independent)");
            var id = u.NewActorId();
            actorByKey[ad.Key] = id;
            var p = ad.Personality;
            u.Actors[id] = new Actor
            {
                Id = id, Name = ad.Name, Kind = kind,
                Personality = p is null ? Personality.Neutral : new Personality(p.Aggression, p.Greed, p.Cooperation, p.Lawfulness).Clamped(),
                Treasury = new Treasury { Credits = ad.Treasury },
            };
        }
        ActorId ActorByKey(string key, string owner) =>
            actorByKey.TryGetValue(key, out var a) ? a : throw new WorldFormatException($"{owner}: unbekannter Besitzer '{key}'");

        foreach (var sd in def.Sectors)
        {
            // Phase 21: owner 0 (oder fehlend) = herrenloser Sektor — keine Fraktion, kein Gebiet; Besitz entsteht erst durch Einfluss (Stationen)
            var owner = new FactionId(sd.Owner);
            Faction? faction = null;
            if (sd.Owner != 0 && !u.Factions.TryGetValue(owner, out faction)) throw new WorldFormatException($"Sektor {sd.Id}: unbekannte Fraktion {sd.Owner}");
            var sector = new Sector { Id = new SectorId(sd.Id), Name = sd.Name, Owner = owner, MapPosition = V(sd.Map) };
            // Phase 14: Planet (Kulisse, Abnehmer der Exportsenke)
            if (sd.Planet is { } pd)
            {
                if (pd.Name.Trim().Length == 0) throw new WorldFormatException($"Sektor {sd.Id}: Planet braucht einen Namen");
                if (pd.Radius <= 0f || !float.IsFinite(pd.Radius)) throw new WorldFormatException($"Sektor {sd.Id}: Planet '{pd.Name}' braucht radius > 0");
                sector.Planet = new Planet { Name = pd.Name, Color = pd.Color, Radius = pd.Radius };
            }
            if (!u.Sectors.TryAdd(sector.Id, sector)) throw new WorldFormatException($"Sektor {sd.Id} doppelt");
            faction?.Territory.Add(sector.Id);

            foreach (var fd in sd.Fields)
                sector.Fields.Add(new ResourceField
                {
                    ClassKey = u.AsteroidClasses.ContainsKey(fd.Class) ? fd.Class : throw new WorldFormatException($"Sektor {sd.Id}: unbekannte Asteroidenklasse '{fd.Class}'"), Position = V(fd.Pos), TargetAsteroids = fd.Belt, RespawnTicks = fd.Respawn,
                });
        }

        foreach (var link in def.Gates)
        {
            var a = AddGate(u, link.A, new GateId(link.B.Id));
            var b = AddGate(u, link.B, new GateId(link.A.Id));
            // Phase 10: Hauptroute und Anfangszustand gelten für beide Enden
            var state = link.State?.ToLowerInvariant() switch
            {
                null or "open" => GateState.Open,
                "closed" => GateState.Closed,
                "destroyed" => GateState.Destroyed,
                _ => throw new WorldFormatException($"Gate {link.A.Id}–{link.B.Id}: unbekannter Zustand '{link.State}' (open, closed, destroyed)"),
            };
            a.MainRoute = b.MainRoute = link.MainRoute;
            a.State = b.State = state;
            u.Gates[a.Id] = a; u.Gates[b.Id] = b;
            u.Sectors[a.Sector].Gates.Add(a.Id);
            u.Sectors[b.Sector].Gates.Add(b.Id);
        }

        int omittedEconomic = 0;
        foreach (var sd in def.Sectors)
            foreach (var st in sd.Stations)
            {
                if (!u.StationTypes.TryGetValue(st.Type, out var type)) throw new WorldFormatException($"Station {st.Id}: unbekannter Typ '{st.Type}'");
                // Phase 14: Exportsenken nur an Planeten
                if (type.Export is not null && u.Sectors[new SectorId(sd.Id)].Planet is null)
                    throw new WorldFormatException($"Station {st.Id} ({st.Name}): Typ '{type.Key}' hat eine Exportsenke und braucht einen Sektor mit Planet");
                // Phase 14: abgeschaltete Systeme — Forschungsstationen ohne Forschung, reine Treibstoffhersteller ohne Treibstoff fehlen in der Welt
                // (die Ids der übrigen Stationen bleiben, wie sie in der Weltdatei stehen; die Bau-Obergrenze sinkt um weggelassene Wirtschaftsstationen,
                // damit der Bau-Spielraum gleich bleibt)
                if (Simulation.FeatureSwitches.OmitFromWorld(u, type))
                {
                    if (type.Government == GovernmentKind.None && !type.Hideout) omittedEconomic++;
                    continue;
                }
                ActorId stOwner = st.Owner is { Length: > 0 } ok ? ActorByKey(ok, $"Station {st.Id}") : u.Sectors[new SectorId(sd.Id)].Owner;
                var station = StationFactory.Create(u, new StationId(st.Id), type, st.Name, new SectorId(sd.Id), V(st.Pos), stOwner);
                // Phase 10: Regierungsstationen und Wachen sind immer vor Pleite geschützt (Phase 11: Verstecke auch)
                station.BankruptcyProtected = st.Essential || st.BankruptcyProtected || type.Government != GovernmentKind.None || type.Hideout;
                station.Immortal = st.Immortal;
                station.Credits = st.Credits;
                foreach (var (k, v) in st.Stock)
                {
                    var e = station.Stock(Good(k));
                    e.Amount = v.Length > 0 ? v[0] : 0;
                    e.Target = v.Length > 1 ? v[1] : e.Target;
                }
                u.AddStation(station);
            }

        if (omittedEconomic > 0) u.Rules.MaxStations = Math.Max(0, u.Rules.MaxStations - omittedEconomic);

        foreach (var sd in def.Sectors)
            AsteroidGenerator.Populate(u, u.Sectors[new SectorId(sd.Id)], seed: sd.Id * 7919, backgroundCount: sd.Asteroids);

        int nextShip = 1;
        foreach (var group in def.Ships)
        {
            var role = Enum.TryParse<ShipRole>(group.Role, ignoreCase: true, out var r) ? r : throw new WorldFormatException($"Unbekannte Schiffsrolle '{group.Role}'");
            // Phase 9: Besitzer der Gruppe — Fraktionsakteur oder die Sammelgruppe „Freie“ (freie Einzelschiffe, implizite Akteure)
            ActorId owner = (group.Owner?.ToLowerInvariant()) switch
            {
                null or "faction" => new FactionId(group.Faction),
                "free" => u.FreeActor,
                _ => actorByKey.TryGetValue(group.Owner!, out var oa) ? oa
                    : throw new WorldFormatException($"Schiffsgruppe '{group.Role}': unbekannter Besitzer '{group.Owner}' (faction, free oder Akteur-Schlüssel)"),
            };
            for (int i = 0; i < group.Count; i++)
            {
                var ship = NewShip(u, new ShipId(nextShip++), role, owner, new SectorId(group.Sector), group.Credits, group.Cargo, group.Model);
                // Phase 11: Polizei und Kaperschiffe sind keine Gründerschiffe der Wirtschaft (keine Güteklasse); sie starten an ihrer Heimatbasis
                if (role is ShipRole.Police or ShipRole.Pirate) PlaceAtHomeBase(u, ship);
                else RollFounderQuality(u, ship);
                u.AddShip(ship);
            }
        }

        // Phase 5c: Startpakete prüfen (Schlüssel eindeutig, Modell und Sektor bekannt) und ggf. das gewählte anwenden
        var seenOptions = new HashSet<string>();
        foreach (var o in def.Player.StartOptions)
        {
            if (!seenOptions.Add(o.Key)) throw new WorldFormatException($"Startpaket '{o.Key}' doppelt");
            if (!u.ShipModels.ContainsKey(o.Model)) throw new WorldFormatException($"Startpaket '{o.Key}': unbekanntes Schiffsmodell '{o.Model}'");
            if (o.Sector is { } os && !u.Sectors.ContainsKey(new SectorId(os))) throw new WorldFormatException($"Startpaket '{o.Key}': unbekannter Sektor {os}");
        }
        string? playerModel = def.Player.Model;
        float playerCredits = def.Player.Credits;
        int playerSector = def.Player.Sector;
        if (startOption is not null)
        {
            var o = def.Player.StartOptions.FirstOrDefault(x => x.Key == startOption)
                    ?? throw new WorldFormatException($"Unbekanntes Startpaket '{startOption}'");
            playerModel = o.Model;
            playerCredits = o.Credits;
            playerSector = o.Sector ?? def.Player.Sector;
        }

        // Phase 5b: das Startgeld gehört der Person (Spielerkasse), nicht dem Schiff
        // Phase 9: der Spieler ist ein Einzelakteur; Mitglied der Fraktion des Spielereintrags, seine Kasse ist die Spielerkasse
        var player = NewShip(u, new ShipId(100), ShipRole.Player, u.PlayerActor, new SectorId(playerSector), 0f, 100, playerModel);
        u.Player.Treasury.Credits = playerCredits;
        u.Player.Membership = u.Factions.ContainsKey(new FactionId(def.Player.Faction)) ? new FactionId(def.Player.Faction) : null;
        u.AddShip(player);
        u.PlayerShip = player.Id;
        u.PlayerSector = player.Sector;
        // Phase 13: die Startwelt darf nichts brauchen, was erst erforscht werden muss (FORSCHUNG.md „Totlauf“)
        ValidateResearchStart(u, def);
        // Phase 11: Lagekarte der Startwelt (Sicherheit aus Grundschutz und Streifen, Verkehr) vor der Sektorkontrolle (Patrouillenabdeckung)
        Simulation.ThreatMap.Initialize(u);
        // Phase 10: Sektorkontrolle der Startwelt gleich berechnen (Einfluss, Stufe, umkämpft); Besitzer aus der Weltdatei bleibt der Startwert
        foreach (var sector in u.Sectors.Values.OrderBy(s => s.Id.Value)) Simulation.SectorControl.Update(u, sector);
        Simulation.GateAccess.SyncRestricted(u);
        // Phase 12: Liegeplatz der Startschiffe (am Andockpunkt einer Station = angedockt; das aktive Schiff dockt nur die Ansicht an); das Laden älterer Stände bestimmt ihn genauso
        foreach (var ship in u.Ships.Values.Where(x => x.Transit is null && x.Id != u.PlayerShip).OrderBy(x => x.Id.Value)) ship.DockedAt = u.StationAt(ship.Sector, ship.Position);
        return u;
    }

    /// <summary>
    /// Phase 14: Exportsenke eines Stationstyps laden — Waren aus Kategorien (in GoodId-Reihenfolge) und Einzelnennung, ohne Doppelte; jede braucht einen
    /// Lagerposten ohne Rezept (der Typ darf sie weder herstellen noch verbrauchen), sonst kaufen KI-Händler dort ein. Leere Liste, unbekannte
    /// Kategorie/Ware oder ungültige Zahlen werfen <see cref="WorldFormatException"/>.
    /// </summary>
    static ExportConfig LoadExport(Universe u, string typeKey, StationType type, ExportConfigDef ex)
    {
        string who = $"Stationstyp '{typeKey}': export";
        if (ex.DelayTicks is <= 0) throw new WorldFormatException($"{who}.delayTicks muss > 0 sein");
        if (ex.Markup is { } mk && (mk < 0f || !float.IsFinite(mk))) throw new WorldFormatException($"{who}.markup muss >= 0 sein");
        if (ex.Target <= 0f || !float.IsFinite(ex.Target)) throw new WorldFormatException($"{who}.target muss > 0 sein");
        if (ex.TargetValue < 0f || !float.IsFinite(ex.TargetValue)) throw new WorldFormatException($"{who}.targetValue muss >= 0 sein");
        var cfg = new ExportConfig { DelayTicks = ex.DelayTicks, Markup = ex.Markup, Target = ex.Target, TargetValue = ex.TargetValue, Categories = new List<string>(ex.Categories) };
        var set = new SortedSet<int>();
        foreach (var c in ex.Categories)
        {
            var ids = u.Goods.Values.Where(g => g.Category == c).Select(g => g.Id.Value).ToList();
            if (ids.Count == 0) throw new WorldFormatException($"{who}: Kategorie '{c}' hat keine Waren");
            set.UnionWith(ids);
        }
        foreach (var k in ex.Goods)
            set.Add((u.FindGood(k) ?? throw new WorldFormatException($"{who}: unbekannte Ware '{k}'")).Id.Value);
        if (set.Count == 0) throw new WorldFormatException($"{who}: keine Exportwaren (categories oder goods)");
        foreach (int id in set)
        {
            var g = new GoodId(id);
            if (type.Recipes.Any(r => r.Inputs.ContainsKey(g) || r.Outputs.ContainsKey(g)))
                throw new WorldFormatException($"{who}: '{u.Goods[g].Key}' steht in einem Rezept des Typs (Exportwaren sind reiner Ankauf)");
            cfg.Goods.Add(g);
            if (!type.Stocks.Any(s => s.Good == g)) type.Stocks.Add(new StationStock(g, 0f, cfg.TargetFor(u.Goods[g])));
        }
        // Phase 17: Höchstgebot je Ware oder Kategorie (Ware gewinnt über Kategorie); nur Exportwaren dieser Senke, Faktor in (0, 4]
        if (ex.Bids is { } bids)
        {
            foreach (var (key, f) in bids.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!(f > 0f && f <= 4f)) throw new WorldFormatException($"{who}.bids['{key}'] muss in (0, 4] liegen");
                bool isGood = u.FindGood(key) is { } kg && cfg.Goods.Contains(kg.Id);
                bool isCategory = cfg.Goods.Any(g => u.Goods[g].Category == key);
                if (!isGood && !isCategory) throw new WorldFormatException($"{who}.bids: '{key}' ist weder eine Exportware noch eine Kategorie der Senke");
            }
            foreach (var g in cfg.Goods)
            {
                var good = u.Goods[g];
                if (bids.TryGetValue(good.Key, out var byGood)) cfg.Bids[g] = byGood;
                else if (bids.TryGetValue(good.Category, out var byCat)) cfg.Bids[g] = byCat;
            }
        }
        return cfg;
    }

    /// <summary>
    /// Phase 13: Forschungsprojekte laden und prüfen — Schlüssel eindeutig, FP &gt; 0, Voraussetzungen nur früher aufgeführte Projekte (damit zyklenfrei),
    /// Freischaltungen lesbar und vorhanden, jede gehört genau einem Projekt, Zusatzverbrauch bekannte Waren &gt; 0. Gesperrte Module dürfen in keinem
    /// Rezept als Zutat stehen; ein freies Modell darf kein gesperrtes Modul tragen, ein gesperrtes nur Module seines Projekts oder seiner Voraussetzungen.
    /// </summary>
    static void LoadResearch(Universe u, WorldDef def, Func<string, GoodId> good)
    {
        foreach (var pd in def.Research)
        {
            string who = $"Forschungsprojekt '{pd.Key}'";
            if (pd.Key.Length == 0 || u.Research.Get(pd.Key) is not null) throw new WorldFormatException($"{who}: Schlüssel leer oder doppelt");
            if (pd.Points <= 0f || !float.IsFinite(pd.Points)) throw new WorldFormatException($"{who}: points muss > 0 sein");
            var p = new ResearchProject { Key = pd.Key, Name = pd.Name.Length > 0 ? pd.Name : pd.Key, Description = pd.Description, Points = pd.Points };
            foreach (var r in pd.Requires)
            {
                if (u.Research.Get(r) is null) throw new WorldFormatException($"{who}: Voraussetzung '{r}' unbekannt oder erst später aufgeführt");
                if (!p.Requires.Contains(r)) p.Requires.Add(r);
            }
            foreach (var text in pd.Unlocks)
            {
                if (!ResearchUnlock.TryParse(text, out var x)) throw new WorldFormatException($"{who}: Freischaltung '{text}' unlesbar (module:, model:, stationType:)");
                bool exists = x.Kind switch
                {
                    UnlockKind.Module => u.Modules.ContainsKey(x.Key),
                    UnlockKind.Model => u.ShipModels.ContainsKey(x.Key),
                    _ => u.StationTypes.ContainsKey(x.Key),
                };
                if (!exists) throw new WorldFormatException($"{who}: Freischaltung '{text}' verweist auf unbekannten Inhalt");
                if (u.Research.LockOf(x.Kind, x.Key) is { } other) throw new WorldFormatException($"{who}: '{text}' schaltet schon Projekt '{other.Key}' frei");
                p.Unlocks.Add(x);
            }
            foreach (var (k, v) in pd.Inputs)
            {
                if (v <= 0f) throw new WorldFormatException($"{who}: inputs '{k}' muss > 0 sein");
                p.Inputs[good(k)] = v;
            }
            u.Research.Add(p);
        }
        if (u.Research.Count == 0) return;
        // Totlauf: gesperrte Module nie als Rezept-Zutat (die Kette bliebe stehen, bis jemand forscht)
        foreach (var t in u.StationTypes.Values)
            foreach (var r in t.Recipes)
                foreach (var g in r.Inputs.Keys)
                    if (u.Modules.ContainsKey(u.Goods[g].Key) && u.Research.LockOf(UnlockKind.Module, u.Goods[g].Key) is { } lp)
                        throw new WorldFormatException($"Stationstyp '{t.Key}': Zutat '{u.Goods[g].Key}' ist erst mit Forschung '{lp.Key}' verfügbar");
        foreach (var m in u.ShipModels.Values)
        {
            var mp = u.Research.LockOf(UnlockKind.Model, m.Key);
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            if (mp is not null) Closure(u, mp, allowed);
            foreach (var k in m.Fit)
                if (k is not null && u.Research.LockOf(UnlockKind.Module, k) is { } lp && !allowed.Contains(lp.Key))
                    throw new WorldFormatException(mp is null
                        ? $"Schiffsmodell '{m.Key}': Modul '{k}' ist erst mit Forschung '{lp.Key}' verfügbar, das Modell selbst aber frei"
                        : $"Schiffsmodell '{m.Key}': Modul '{k}' braucht Forschung '{lp.Key}', die weder '{mp.Key}' noch dessen Voraussetzung ist");
        }
    }

    /// <summary>Projekt und alle (transitiven) Voraussetzungen.</summary>
    static void Closure(Universe u, ResearchProject p, HashSet<string> into)
    {
        if (!into.Add(p.Key)) return;
        foreach (var r in p.Requires) if (u.Research.Get(r) is { } rp) Closure(u, rp, into);
    }

    /// <summary>Phase 13: Startschiffe, Startpakete, Spielermodell und Stationen der Startwelt dürfen nichts Gesperrtes nutzen.</summary>
    static void ValidateResearchStart(Universe u, WorldDef def)
    {
        if (u.Research.Count == 0) return;
        foreach (var ship in u.Ships.Values.OrderBy(x => x.Id.Value))
        {
            if (ship.HasModel && u.Research.LockOf(UnlockKind.Model, ship.ModelKey) is { } p)
                throw new WorldFormatException($"Schiff {ship.Id.Value}: Modell '{ship.ModelKey}' ist erst mit Forschung '{p.Key}' verfügbar");
            foreach (var k in ship.Fit)
                if (k is not null && u.Research.LockOf(UnlockKind.Module, k) is { } mp)
                    throw new WorldFormatException($"Schiff {ship.Id.Value}: Modul '{k}' ist erst mit Forschung '{mp.Key}' verfügbar");
        }
        foreach (var o in def.Player.StartOptions)
            if (u.Research.LockOf(UnlockKind.Model, o.Model) is { } p)
                throw new WorldFormatException($"Startpaket '{o.Key}': Modell '{o.Model}' ist erst mit Forschung '{p.Key}' verfügbar");
        foreach (var st in u.Stations.Values.OrderBy(x => x.Id.Value))
            if (u.Research.LockOf(UnlockKind.StationType, st.TypeKey) is { } p)
                throw new WorldFormatException($"Station {st.Id.Value}: Typ '{st.TypeKey}' ist erst mit Forschung '{p.Key}' verfügbar");
    }

    /// <summary>
    /// Phase 11: Polizei- und Kaperschiffe der Startwelt bekommen ihre Heimatbasis (Wache/Hauptquartier der Fraktion bzw. Versteck des Akteurs, im
    /// Startsektor zuerst) und liegen dort.
    /// </summary>
    static void PlaceAtHomeBase(Universe u, Ship ship)
    {
        Station? home = ship.Role == ShipRole.Pirate
            ? u.Stations.Values.Where(st => st.Owner == ship.Owner && Simulation.StationProtection.IsHideout(u, st))
                .OrderBy(st => st.Sector == ship.Sector ? 0 : 1).ThenBy(st => st.Id.Value).FirstOrDefault()
            : u.FactionOf(ship.Owner) is { } f ? Simulation.SecuritySystem.HomeBaseFor(u, f, ship.Sector) : null;
        if (home is null) return;
        ship.HomeBase = home.Id;
        if (home.Sector == ship.Sector) ship.Position = home.DockPoint(ship.Id);
    }

    /// <summary>
    /// Phase 7: Gründerschiffe der Startwelt würfeln ihre Güteklasse ohne Material (SCHIFFSSLOTS.md Abschnitt 8); Schiffe ohne Modell bleiben Standard.
    /// Werte, Laderaum, Hülle und Tank folgen dem gewürfelten Fit.
    /// </summary>
    static void RollFounderQuality(Universe u, Ship ship)
    {
        if (!u.Rules.FounderQuality || !ship.HasModel || !u.ShipModels.TryGetValue(ship.ModelKey, out var model) || !u.ShipClasses.TryGetValue(model.ClassKey, out var cls)) return;
        var q = Simulation.ShipQualityRoll.Roll(u, cls, ship.Slots, ship.Fit, null, null);
        ship.Quality = q.Grade;
        if (q.Grade == ShipQuality.Standard) return;
        ship.Slots = q.Slots;
        ship.Fit = q.Fit;
        ship.RecomputeStats(u);
        ship.CargoCapacity = ship.Stats.Cargo;
        ship.Hull = ship.MaxHull;
        ship.FillFuel();
    }

    /// <summary>
    /// Modulfabrik (Phase 5a): je passendem Modul (Slotart in der Liste, Stufe ≤ maxTier, mit cost) ein Rezept
    /// cost → 1 Modul-Ware, in der Reihenfolge der Modulschlüssel; die Ausgänge bekommen einen Lagerposten (Ziel = target), die
    /// Zutaten einen mit inputTarget, sofern stocks sie nicht schon nennt.
    /// </summary>
    static void AddModuleRecipes(Universe u, string typeKey, StationType type, ModuleFactoryDef mf, Func<string, GoodId> good)
    {
        foreach (var slot in mf.Slots)
            if (!u.SlotTypes.ContainsKey(slot)) throw new WorldFormatException($"Stationstyp '{typeKey}': unbekannte Slotart '{slot}' in moduleFactory");
        if (mf.Ticks <= 0 || mf.Target <= 0f || mf.InputTarget <= 0f) throw new WorldFormatException($"Stationstyp '{typeKey}': moduleFactory braucht ticks > 0, target > 0 und inputTarget > 0");
        int made = 0;
        foreach (var m in ModuleGoods.InGoodOrder(u.Modules.Values, m => m.Key))
        {
            if (!mf.Slots.Contains(m.Slot) || m.Tier > mf.MaxTier || m.Cost.Count == 0) continue;
            var recipe = new Recipe { TicksPerCycle = mf.Ticks };
            foreach (var (k, v) in m.Cost.OrderBy(kv => kv.Key, StringComparer.Ordinal)) recipe.Inputs[good(k)] = v;
            var outGood = good(m.Key);
            recipe.Outputs[outGood] = 1f;
            type.Recipes.Add(recipe);
            if (!type.Stocks.Any(x => x.Good == outGood)) type.Stocks.Add(new StationStock(outGood, mf.Target, mf.Target));
            foreach (var input in recipe.Inputs.Keys)
                if (!type.Stocks.Any(x => x.Good == input)) type.Stocks.Add(new StationStock(input, mf.InputTarget, mf.InputTarget));
            made++;
        }
        if (made == 0) throw new WorldFormatException($"Stationstyp '{typeKey}': moduleFactory erzeugt kein Modul");
    }

    /// <summary>
    /// Lädt die Slot-/Modul-Content-Sektionen (slotTypes, modules, shipClasses, shipModels) samt Validierung in das
    /// Universum. Wird von <see cref="Build"/> und von der Save-Migration (Default-Welt-Registry) genutzt.
    /// </summary>
    public static void LoadSlotContent(Universe u, WorldDef def)
    {
        // Waren-Schlüssel → Id (Position + 1, dieselbe Regel wie in Build) und Form, auch für den Standalone-Aufruf aus der Save-Migration
        var goodId = new Dictionary<string, GoodId>();
        var goodForm = new Dictionary<string, GoodForm>();
        for (int i = 0; i < def.Goods.Count; i++)
        {
            var g = def.Goods[i];
            if (!goodId.TryAdd(g.Key, new GoodId(i + 1))) throw new WorldFormatException($"Ware '{g.Key}' doppelt");
            goodForm[g.Key] = ParseForm(g.Form, g.Key);
        }
        foreach (var st in def.SlotTypes)
            if (!u.SlotTypes.TryAdd(st.Key, new SlotType { Key = st.Key, Name = st.Name, Required = st.Required, Color = st.Color }))
                throw new WorldFormatException($"Slotart '{st.Key}' doppelt");

        foreach (var md in def.Modules)
        {
            if (!u.SlotTypes.TryGetValue(md.Slot, out _)) throw new WorldFormatException($"Modul '{md.Key}': unbekannte Slotart '{md.Slot}'");
            var grants = new List<Capability>();
            foreach (var g in md.Grants)
                if (!Enum.TryParse<Capability>(g, ignoreCase: true, out var cap)) throw new WorldFormatException($"Modul '{md.Key}': unbekannte Fähigkeit '{g}'");
                else grants.Add(cap);
            var cost = new Dictionary<string, float>();
            foreach (var (k, v) in md.Cost)
            {
                if (!goodId.ContainsKey(k)) throw new WorldFormatException($"Modul '{md.Key}': unbekannte Ware '{k}' in cost");
                cost[k] = v;
            }
            // Treibstoffliste des Antriebs: Ware bekannt und flüssig, efficiency > 0, keine Dopplung (Phase 4)
            var fuel = new List<FuelType>();
            foreach (var f in md.Fuel)
            {
                if (!goodId.TryGetValue(f.Good, out var gid)) throw new WorldFormatException($"Modul '{md.Key}': unbekannte Ware '{f.Good}' in fuel");
                if (goodForm[f.Good] != GoodForm.Liquid) throw new WorldFormatException($"Modul '{md.Key}': Ware '{f.Good}' in fuel ist nicht flüssig (form: liquid)");
                if (f.Efficiency <= 0f) throw new WorldFormatException($"Modul '{md.Key}': efficiency {f.Efficiency} für Ware '{f.Good}' in fuel muss > 0 sein");
                if (fuel.Any(x => x.Good == gid)) throw new WorldFormatException($"Modul '{md.Key}': Ware '{f.Good}' doppelt in fuel");
                fuel.Add(new FuelType(gid, f.Efficiency));
            }
            var module = new Module
            {
                Key = md.Key, Name = md.Name ?? md.Key, Slot = md.Slot, Size = ParseSize(md.Size, $"Modul '{md.Key}'"), Tier = md.Tier,
                Grants = grants, Stats = new Dictionary<string, float>(md.Stats),
                PowerDraw = md.PowerDraw, PeakDraw = md.PeakDraw, Mass = md.Mass, Upkeep = md.Upkeep,
                Holds = ParseHolds(md.Holds, $"Modul '{md.Key}'"), Cost = cost, FuelTypes = fuel,
                DamageType = ParseWeapon(md),
            };
            if (!u.Modules.TryAdd(md.Key, module)) throw new WorldFormatException($"Modul '{md.Key}' doppelt");
        }
        foreach (var cd in def.ShipClasses)
        {
            var slots = new List<ClassSlot>();
            foreach (var s in cd.Slots)
            {
                if (!u.SlotTypes.TryGetValue(s.Type, out _)) throw new WorldFormatException($"Schiffsklasse '{cd.Key}': unbekannte Slotart '{s.Type}'");
                slots.Add(new ClassSlot { Type = s.Type, Size = ParseSize(s.Size, $"Schiffsklasse '{cd.Key}'"), Facing = s.Facing, Arc = s.Arc });
            }
            if (cd.Kit is { } kit && !goodId.ContainsKey(kit)) throw new WorldFormatException($"Schiffsklasse '{cd.Key}': unbekannte Bausatz-Ware '{kit}'");
            if (cd.ToolEfficiency <= 0f || cd.ToolEfficiency > 1f) throw new WorldFormatException($"Schiffsklasse '{cd.Key}': toolEfficiency {cd.ToolEfficiency} muss in (0, 1] liegen");
            if (cd.MaxSlotUpgrades < 0) throw new WorldFormatException($"Schiffsklasse '{cd.Key}': maxSlotUpgrades muss >= 0 sein");
            if (cd.BerthFee < 0f) throw new WorldFormatException($"Schiffsklasse '{cd.Key}': berthFee muss >= 0 sein");
            var cls = new ShipClass { Key = cd.Key, Name = cd.Name, Size = ParseSize(cd.Size, $"Schiffsklasse '{cd.Key}'"), BaseHull = cd.BaseHull, BaseMass = cd.BaseMass, BaseFuel = cd.BaseFuel, Slots = slots, Kit = cd.Kit, ToolEfficiency = cd.ToolEfficiency, MaxSlotUpgrades = cd.MaxSlotUpgrades, Restricted = cd.Restricted, BerthFee = cd.BerthFee };
            if (!u.ShipClasses.TryAdd(cd.Key, cls)) throw new WorldFormatException($"Schiffsklasse '{cd.Key}' doppelt");
        }

        foreach (var md in def.ShipModels)
        {
            if (!u.ShipClasses.TryGetValue(md.Class, out var cls)) throw new WorldFormatException($"Schiffsmodell '{md.Key}': unbekannte Klasse '{md.Class}'");
            // Phase 5c: dieselbe Fit-Regel wie zur Laufzeit beim Ausrüster (FitRules), hier mit dem Modellnamen als Präfix
            if (FitRules.Check(u.SlotTypes, u.Modules, cls, cls.Slots, md.Fit) is { } issue)
                throw new WorldFormatException($"Schiffsmodell '{md.Key}': {issue.Message}");
            // Phase 6: KI-Aufgaben — bekannter Schlüssel, keine Dopplung, und der Fit muss die nötige Fähigkeit haben
            var jobs = new List<string>();
            if (md.Jobs is not null)
            {
                var caps = ShipStats.Compute(cls, md.Fit.Select(k => k is not null ? u.Modules[k] : null).ToList()).Capabilities;
                foreach (var key in md.Jobs)
                {
                    if (Simulation.ShipJobs.ByKey(key) is not { } job) throw new WorldFormatException($"Schiffsmodell '{md.Key}': unbekannte Aufgabe '{key}' in jobs");
                    if (jobs.Contains(key)) throw new WorldFormatException($"Schiffsmodell '{md.Key}': Aufgabe '{key}' doppelt in jobs");
                    if ((caps & job.Needs) != job.Needs) throw new WorldFormatException($"Schiffsmodell '{md.Key}': Aufgabe '{key}' braucht die Fähigkeit {job.Needs}, die der Fit nicht hat");
                    jobs.Add(key);
                }
            }
            if (!u.ShipModels.TryAdd(md.Key, new ShipModel { Key = md.Key, ClassKey = md.Class, Name = md.Name, Fit = new List<string?>(md.Fit), Jobs = jobs }))
                throw new WorldFormatException($"Schiffsmodell '{md.Key}' doppelt");
        }
        // Phase 7: Verbesserungen — Zielmodul bekannt, gleiche Slotart, nicht größer, nicht das Modul selbst; Material bekannt und > 0
        foreach (var md in def.Modules)
        {
            if (md.Upgrade is not { } up) continue;
            var module = u.Modules[md.Key];
            if (!u.Modules.TryGetValue(up.To, out var target)) throw new WorldFormatException($"Modul '{md.Key}': unbekanntes Zielmodul '{up.To}' in upgrade");
            if (target == module) throw new WorldFormatException($"Modul '{md.Key}': upgrade verweist auf sich selbst");
            if (target.Slot != module.Slot) throw new WorldFormatException($"Modul '{md.Key}': Zielmodul '{up.To}' hat eine andere Slotart ('{target.Slot}')");
            if (target.Size > module.Size) throw new WorldFormatException($"Modul '{md.Key}': Zielmodul '{up.To}' ist größer ({target.Size})");
            foreach (var (k, v) in up.Cost)
            {
                if (!goodId.ContainsKey(k)) throw new WorldFormatException($"Modul '{md.Key}': unbekannte Ware '{k}' in upgrade.cost");
                if (v <= 0f) throw new WorldFormatException($"Modul '{md.Key}': upgrade.cost '{k}' muss > 0 sein");
            }
            module.UpgradeTo = up.To;
            module.UpgradeCost = new Dictionary<string, float>(up.Cost);
        }
    }

    /// <summary>
    /// Einheitsmodell je Rolle und Laderaum (dieselbe Zuordnung wie die Save-Migration v3→v4);
    /// "" = keines (z.B. Fighter oder unbekannte Kapazität).
    /// </summary>
    public static string UnitModelFor(ShipRole role, float cargo) => (role, cargo) switch
    {
        (ShipRole.Trader, 100f) => "unit_trader_100",
        (ShipRole.Trader, 60f) => "unit_trader_60",
        (ShipRole.Miner, 100f) => "unit_miner_100",
        (ShipRole.Miner, 60f) => "unit_miner_60",
        (ShipRole.Player, _) => "unit_player",
        (ShipRole.Builder, _) => "foreman",   // Phase 8: Bauschiff
        _ => "",
    };

    /// <summary>Regierungsform aus der Weltdatei (Phase 10).</summary>
    public static GovernmentKind ParseGovernment(string? key, string typeKey) => key?.ToLowerInvariant() switch
    {
        null or "" or "none" => GovernmentKind.None,
        "headquarters" => GovernmentKind.Headquarters,
        "company_seat" => GovernmentKind.CompanySeat,
        "sector_hq" => GovernmentKind.SectorHq,
        "office" => GovernmentKind.Office,
        "police" => GovernmentKind.Police,
        "research" => GovernmentKind.Research,
        _ => throw new WorldFormatException($"Stationstyp '{typeKey}': unbekannte Regierungsform '{key}' (headquarters, company_seat, sector_hq, office, police, research)"),
    };

    /// <summary>Schlüssel einer Regierungsform für Weltdatei und Spielstand.</summary>
    public static string? GovernmentKey(GovernmentKind k) => k switch
    {
        GovernmentKind.Headquarters => "headquarters",
        GovernmentKind.CompanySeat => "company_seat",
        GovernmentKind.SectorHq => "sector_hq",
        GovernmentKind.Office => "office",
        GovernmentKind.Police => "police",
        GovernmentKind.Research => "research",
        _ => null,
    };

    static Gate AddGate(Universe u, GateEndDef end, GateId target)
    {
        if (!u.Sectors.ContainsKey(new SectorId(end.Sector))) throw new WorldFormatException($"Gate {end.Id}: unbekannter Sektor {end.Sector}");
        return new Gate { Id = new GateId(end.Id), Sector = new SectorId(end.Sector), Position = V(end.Pos), Target = target };
    }

    /// <summary>
    /// Erzeugt ein Schiff. <paramref name="model"/> ist der Modell-Schlüssel aus der Weltdatei; fehlt er, wird das
    /// Einheitsmodell nach Rolle und <paramref name="cargo"/> gewählt (<see cref="UnitModelFor"/>). Ein unbekannter
    /// expliziter Schlüssel wirft <see cref="WorldFormatException"/>; ein in dieser Welt nicht vorhandenes
    /// automatisch gewähltes Einheitsmodell lässt das Schiff ohne Modell (Pauschalwerte).
    /// </summary>
    public static Ship NewShip(Universe u, ShipId id, ShipRole role, int faction, SectorId sector, float credits, float cargo, string? model = null) =>
        NewShip(u, id, role, new ActorId(faction), sector, credits, cargo, model);

    /// <summary>Wie oben, mit ausdrücklichem Besitzer (Phase 9: Fraktionsakteur, Sammelgruppe „Freie“ oder Spieler).</summary>
    public static Ship NewShip(Universe u, ShipId id, ShipRole role, ActorId owner, SectorId sector, float credits, float cargo, string? model = null)
    {
        if (!u.Sectors.TryGetValue(sector, out var sec)) throw new WorldFormatException($"Schiff {id.Value}: unbekannter Sektor {sector.Value}");
        var ship = new Ship
        {
            Id = id, Role = role, Controller = CapabilityDefaults.ControllerFor(role),
            Owner = owner, Sector = sector, Credits = credits,
            Position = sec.Stations.Count > 0 ? u.Stations[sec.Stations[0]].DockPoint(id) : default,
        };
        var key = model ?? UnitModelFor(role, cargo);
        bool known = key.Length > 0 && u.ShipModels.TryGetValue(key, out var m) && u.ShipClasses.ContainsKey(m.ClassKey);
        if (key.Length > 0 && !known && model is not null)
            throw new WorldFormatException($"Schiff {id.Value}: unbekanntes Schiffsmodell '{key}'");
        if (known)
        {
            ship.ModelKey = key;
            ship.Fit = new List<string?>(u.ShipModels[key].Fit);
            if (ship.RecomputeStats(u))
            {
                ship.CargoCapacity = ship.Stats.Cargo;   // Laderaum aus dem Modell; Fähigkeiten leitet AddShip aus Stats ab
                ship.Hull = ship.MaxHull;                 // Phase 5c: Hülle des Modells (Skiffs haben weniger als die Pauschale 100)
                ship.FillFuel();                          // Phase 4: neuer Tank voll (erster zulässiger Treibstoff, z. B. propellant)
            }
            else
            {
                ship.ModelKey = "";
                ship.CargoCapacity = cargo;
                ship.Capabilities = CapabilityDefaults.ForRole(role);
            }
        }
        else
        {
            ship.CargoCapacity = cargo;
            ship.Capabilities = CapabilityDefaults.ForRole(role);
        }
        return ship;
    }

    static Vec2 V(float[] a) => a.Length >= 2 ? new Vec2(a[0], a[1]) : throw new WorldFormatException("Position braucht zwei Zahlen");

    /// <summary>Warenform aus dem JSON: fehlt das Feld oder "solid" → Solid, "liquid" → Liquid, sonst Fehler mit Warenname.</summary>
    static GoodForm ParseForm(string? form, string goodKey) => form?.ToLowerInvariant() switch
    {
        null or "solid" => GoodForm.Solid,
        "liquid" => GoodForm.Liquid,
        _ => throw new WorldFormatException($"Ware '{goodKey}': unbekannte Form '{form}'"),
    };

    static readonly string[] WeaponStats = { "damage", "reload", "range", "accuracy", "tracking", "ammoPerShot" };

    /// <summary>
    /// Phase 24: Waffenwerte eines Moduls prüfen und die Schadensart lesen. Eine Waffe hat damage &gt; 0, reload &gt; 0, range &gt; 0, accuracy und tracking in
    /// (0, 1], ammoPerShot ≥ 0 und eine Schadensart (energy/kinetic); einzelne Waffenwerte oder eine Schadensart ohne damage sind ein Fehler.
    /// </summary>
    static DamageType ParseWeapon(ModuleDef md)
    {
        string who = $"Modul '{md.Key}'";
        float Get(string k) => md.Stats.TryGetValue(k, out var v) ? v : 0f;
        bool weapon = md.Stats.ContainsKey("damage");
        if (!weapon)
        {
            foreach (var k in WeaponStats)
                if (md.Stats.ContainsKey(k)) throw new WorldFormatException($"{who}: Waffenwert '{k}' ohne damage");
            if (md.DamageType is not null) throw new WorldFormatException($"{who}: damageType ohne damage");
            return DamageType.Energy;
        }
        if (Get("damage") <= 0f) throw new WorldFormatException($"{who}: damage muss > 0 sein");
        if (Get("reload") <= 0f) throw new WorldFormatException($"{who}: reload muss > 0 sein");
        if (Get("range") <= 0f) throw new WorldFormatException($"{who}: range muss > 0 sein");
        if (Get("accuracy") <= 0f || Get("accuracy") > 1f) throw new WorldFormatException($"{who}: accuracy muss in (0, 1] liegen");
        if (Get("tracking") <= 0f || Get("tracking") > 1f) throw new WorldFormatException($"{who}: tracking muss in (0, 1] liegen");
        if (Get("ammoPerShot") < 0f) throw new WorldFormatException($"{who}: ammoPerShot muss >= 0 sein");
        if (md.DamageType is null) throw new WorldFormatException($"{who}: damageType fehlt (energy oder kinetic)");
        if (!Enum.TryParse<DamageType>(md.DamageType, ignoreCase: true, out var dt) || !Enum.IsDefined(dt))
            throw new WorldFormatException($"{who}: unbekannte damageType '{md.DamageType}' (energy oder kinetic)");
        return dt;
    }

    static SlotSize ParseSize(string size, string owner) => size.ToUpperInvariant() switch
    {
        "S" => SlotSize.S,
        "M" => SlotSize.M,
        "L" => SlotSize.L,
        "XL" => SlotSize.XL,
        _ => throw new WorldFormatException($"{owner}: unbekannte Größe '{size}'"),
    };

    /// <summary>Frachtform eines Moduls; fehlt das Feld, ist Any der Standard.</summary>
    static ModuleHolds ParseHolds(string? holds, string owner) => holds?.ToLowerInvariant() switch
    {
        null => ModuleHolds.Any,
        "solid" => ModuleHolds.Solid,
        "liquid" => ModuleHolds.Liquid,
        "any" => ModuleHolds.Any,
        _ => throw new WorldFormatException($"{owner}: unbekannte Frachtform '{holds}'"),
    };
}
