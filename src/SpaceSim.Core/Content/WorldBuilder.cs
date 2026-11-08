using System.Reflection;
using System.Text.Json;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Model;

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
            u.Goods[new GoodId(i + 1)] = new Good { Id = new GoodId(i + 1), Key = g.Key, Name = g.Name, Color = g.Color, BasePrice = g.BasePrice, VolumePerUnit = g.Volume };
        }
        GoodId Good(string key) =>
            goodByKey.TryGetValue(key, out var id) ? id : throw new WorldFormatException($"Unbekannte Ware '{key}'");

        foreach (var c in def.AsteroidClasses)
        {
            var cls = new AsteroidClass { Key = c.Key, Name = c.Name, Color = c.Color, ChunkChance = c.ChunkChance, OreFactor = c.OreFactor };
            foreach (var (k, w) in c.Yields) cls.Yields.Add(new Yield(Good(k), w));
            if (cls.Yields.Count == 0) throw new WorldFormatException($"Asteroidenklasse '{c.Key}' hat keine Erzsorten");
            if (!u.AsteroidClasses.TryAdd(cls.Key, cls)) throw new WorldFormatException($"Asteroidenklasse '{c.Key}' doppelt");
        }

        foreach (var t in def.StationTypes)
        {
            var type = new StationType { Key = t.Key, Name = t.Name, BuildCost = t.BuildCost, UpkeepPerTick = t.Upkeep, IncomePerTick = t.Income };
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
            Id = id, Role = role, Faction = new FactionId(faction), Sector = sector, Credits = credits, CargoCapacity = cargo,
            Position = sec.Stations.Count > 0 ? u.Stations[sec.Stations[0]].DockPoint(id) : default,
        };
    }

    static Vec2 V(float[] a) => a.Length >= 2 ? new Vec2(a[0], a[1]) : throw new WorldFormatException("Position braucht zwei Zahlen");
}
