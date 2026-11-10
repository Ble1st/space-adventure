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

    public static Universe BuildDefault(ulong? seed = null) => Build(Parse(DefaultWorldJson()), seed);

    public static Universe Build(WorldDef def, ulong? seedOverride = null)
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
            u.Goods[new GoodId(i + 1)] = new Good { Id = new GoodId(i + 1), Key = g.Key, Name = g.Name, Color = g.Color, BasePrice = g.BasePrice, VolumePerUnit = g.Volume, Form = ParseForm(g.Form, g.Key) };
        }
        GoodId Good(string key) =>
            goodByKey.TryGetValue(key, out var id) ? id : throw new WorldFormatException($"Unbekannte Ware '{key}'");

        // Slot-/Modul-Content (Phase 3a: reine Daten + Validierung; nichts nutzt sie bisher)
        foreach (var st in def.SlotTypes)
            if (!u.SlotTypes.TryAdd(st.Key, new SlotType { Key = st.Key, Name = st.Name, Required = st.Required }))
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
                if (!goodByKey.ContainsKey(k)) throw new WorldFormatException($"Modul '{md.Key}': unbekannte Ware '{k}' in cost");
                cost[k] = v;
            }
            var module = new Module
            {
                Key = md.Key, Slot = md.Slot, Size = ParseSize(md.Size, $"Modul '{md.Key}'"), Tier = md.Tier,
                Grants = grants, Stats = new Dictionary<string, float>(md.Stats),
                PowerDraw = md.PowerDraw, PeakDraw = md.PeakDraw, Mass = md.Mass, Upkeep = md.Upkeep,
                Holds = ParseHolds(md.Holds, $"Modul '{md.Key}'"), Cost = cost,
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
            var cls = new ShipClass { Key = cd.Key, Name = cd.Name, Size = ParseSize(cd.Size, $"Schiffsklasse '{cd.Key}'"), BaseHull = cd.BaseHull, BaseMass = cd.BaseMass, Slots = slots };
            if (!u.ShipClasses.TryAdd(cd.Key, cls)) throw new WorldFormatException($"Schiffsklasse '{cd.Key}' doppelt");
        }

        foreach (var md in def.ShipModels)
        {
            if (!u.ShipClasses.TryGetValue(md.Class, out var cls)) throw new WorldFormatException($"Schiffsmodell '{md.Key}': unbekannte Klasse '{md.Class}'");
            if (md.Fit.Count != cls.Slots.Count) throw new WorldFormatException($"Schiffsmodell '{md.Key}': Fit hat {md.Fit.Count} Einträge, Klasse '{cls.Key}' hat {cls.Slots.Count} Slots");
            var fit = new List<Module?>(md.Fit.Count);
            for (int i = 0; i < md.Fit.Count; i++)
            {
                var key = md.Fit[i];
                if (key is null) { fit.Add(null); continue; }
                if (!u.Modules.TryGetValue(key, out var module)) throw new WorldFormatException($"Schiffsmodell '{md.Key}': unbekanntes Modul '{key}'");
                var slot = cls.Slots[i];
                if (module.Slot != slot.Type) throw new WorldFormatException($"Schiffsmodell '{md.Key}': Modul '{module.Key}' (Slotart '{module.Slot}') passt nicht in Slot '{slot.Type}'");
                if (module.Size > slot.Size) throw new WorldFormatException($"Schiffsmodell '{md.Key}': Modul '{module.Key}' ({module.Size}) ist größer als der Slot ({slot.Size})");
                fit.Add(module);
            }
            for (int i = 0; i < cls.Slots.Count; i++)
                if (fit[i] is null && u.SlotTypes[cls.Slots[i].Type].Required)
                    throw new WorldFormatException($"Schiffsmodell '{md.Key}': Pflichtslot '{cls.Slots[i].Type}' ist leer");
            var stats = ShipStats.Compute(cls, fit);
            if (stats.PowerOutput < stats.PowerDraw) throw new WorldFormatException($"Schiffsmodell '{md.Key}': powerOutput {stats.PowerOutput} < powerDraw {stats.PowerDraw}");
            if (stats.PeakDraw - (stats.PowerOutput - stats.PowerDraw) > stats.PowerStorage)
                throw new WorldFormatException($"Schiffsmodell '{md.Key}': Spitzenlast {stats.PeakDraw} übersteigt die Speicherbank ({stats.PowerStorage} + Reaktorüberschuss {stats.PowerOutput - stats.PowerDraw})");
            if (!u.ShipModels.TryAdd(md.Key, new ShipModel { Key = md.Key, ClassKey = md.Class, Name = md.Name, Fit = new List<string?>(md.Fit) }))
                throw new WorldFormatException($"Schiffsmodell '{md.Key}' doppelt");
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
                Services = t.Services is null ? new List<string> { Services.TradeService.ServiceKey } : new List<string>(t.Services),
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
                type.Recipes.Add(recipe);
            }
            if (!u.StationTypes.TryAdd(type.Key, type)) throw new WorldFormatException($"Stationstyp '{t.Key}' doppelt");
        }

        foreach (var f in def.Factions) u.Factions[new FactionId(f.Id)] = new Faction { Id = new FactionId(f.Id), Name = f.Name };

        foreach (var sd in def.Sectors)
        {
            var owner = new FactionId(sd.Owner);
            if (!u.Factions.TryGetValue(owner, out var faction)) throw new WorldFormatException($"Sektor {sd.Id}: unbekannte Fraktion {sd.Owner}");
            var sector = new Sector { Id = new SectorId(sd.Id), Name = sd.Name, Owner = owner, MapPosition = V(sd.Map) };
            if (!u.Sectors.TryAdd(sector.Id, sector)) throw new WorldFormatException($"Sektor {sd.Id} doppelt");
            faction.Territory.Add(sector.Id);

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
            u.Gates[a.Id] = a; u.Gates[b.Id] = b;
            u.Sectors[a.Sector].Gates.Add(a.Id);
            u.Sectors[b.Sector].Gates.Add(b.Id);
        }

        foreach (var sd in def.Sectors)
            foreach (var st in sd.Stations)
            {
                if (!u.StationTypes.TryGetValue(st.Type, out var type)) throw new WorldFormatException($"Station {st.Id}: unbekannter Typ '{st.Type}'");
                var station = StationFactory.Create(u, new StationId(st.Id), type, st.Name, new SectorId(sd.Id), V(st.Pos), u.Sectors[new SectorId(sd.Id)].Owner);
                station.Essential = st.Essential;
                station.Credits = st.Credits;
                foreach (var (k, v) in st.Stock)
                {
                    var e = station.Stock(Good(k));
                    e.Amount = v.Length > 0 ? v[0] : 0;
                    e.Target = v.Length > 1 ? v[1] : e.Target;
                }
                u.AddStation(station);
            }

        foreach (var sd in def.Sectors)
            AsteroidGenerator.Populate(u, u.Sectors[new SectorId(sd.Id)], seed: sd.Id * 7919, backgroundCount: sd.Asteroids);

        int nextShip = 1;
        foreach (var group in def.Ships)
        {
            var role = Enum.TryParse<ShipRole>(group.Role, ignoreCase: true, out var r) ? r : throw new WorldFormatException($"Unbekannte Schiffsrolle '{group.Role}'");
            for (int i = 0; i < group.Count; i++) u.AddShip(NewShip(u, new ShipId(nextShip++), role, group.Faction, new SectorId(group.Sector), group.Credits, group.Cargo));
        }

        var player = NewShip(u, new ShipId(100), ShipRole.Player, def.Player.Faction, new SectorId(def.Player.Sector), def.Player.Credits, 100);
        u.AddShip(player);
        u.PlayerShip = player.Id;
        u.PlayerSector = player.Sector;
        return u;
    }

    static Gate AddGate(Universe u, GateEndDef end, GateId target)
    {
        if (!u.Sectors.ContainsKey(new SectorId(end.Sector))) throw new WorldFormatException($"Gate {end.Id}: unbekannter Sektor {end.Sector}");
        return new Gate { Id = new GateId(end.Id), Sector = new SectorId(end.Sector), Position = V(end.Pos), Target = target };
    }

    public static Ship NewShip(Universe u, ShipId id, ShipRole role, int faction, SectorId sector, float credits, float cargo)
    {
        if (!u.Sectors.TryGetValue(sector, out var sec)) throw new WorldFormatException($"Schiff {id.Value}: unbekannter Sektor {sector.Value}");
        return new Ship
        {
            Id = id, Role = role, Capabilities = CapabilityDefaults.ForRole(role), Controller = CapabilityDefaults.ControllerFor(role),
            Faction = new FactionId(faction), Sector = sector, Credits = credits, CargoCapacity = cargo,
            Position = sec.Stations.Count > 0 ? u.Stations[sec.Stations[0]].DockPoint(id) : default,
        };
    }

    static Vec2 V(float[] a) => a.Length >= 2 ? new Vec2(a[0], a[1]) : throw new WorldFormatException("Position braucht zwei Zahlen");

    /// <summary>Warenform aus dem JSON: fehlt das Feld oder "solid" → Solid, "liquid" → Liquid, sonst Fehler mit Warenname.</summary>
    static GoodForm ParseForm(string? form, string goodKey) => form?.ToLowerInvariant() switch
    {
        null or "solid" => GoodForm.Solid,
        "liquid" => GoodForm.Liquid,
        _ => throw new WorldFormatException($"Ware '{goodKey}': unbekannte Form '{form}'"),
    };

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
