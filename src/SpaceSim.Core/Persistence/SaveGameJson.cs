using System.Text.Json;
using SpaceSim.Core.Content;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

public sealed class SaveFormatException : Exception
{
    public SaveFormatException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>JSON-Ein-/Ausgabe für Spielstände. Dateizugriff liegt beim Aufrufer (Godot: user://), Core bleibt IO-frei.</summary>
public static class SaveGameJson
{
    static readonly JsonSerializerOptions Options = new() { IncludeFields = true, WriteIndented = false };

    public static string Write(Universe u, DateTimeOffset now) =>
        JsonSerializer.Serialize(UniverseSerializer.ToSave(u, now), Options);

    public static (Universe Universe, DateTimeOffset SavedAt) Read(string json)
    {
        SaveGame save;
        try { save = JsonSerializer.Deserialize<SaveGame>(json, Options) ?? throw new SaveFormatException("Leerer Spielstand"); }
        catch (JsonException e) { throw new SaveFormatException("Spielstand ist beschädigt: " + e.Message, e); }

        if (save.Version > SaveGame.CurrentVersion)
            throw new SaveFormatException($"Spielstand stammt aus einer neueren Version ({save.Version} > {SaveGame.CurrentVersion})");
        save = Migrate(save);

        try
        {
            var u = UniverseSerializer.FromSave(save);
            var savedAt = DateTimeOffset.TryParse(save.SavedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : DateTimeOffset.UtcNow;
            return (u, savedAt);
        }
        catch (Exception e) when (e is KeyNotFoundException or ArgumentException or FormatException or InvalidOperationException or WorldFormatException)
        {
            throw new SaveFormatException("Spielstand ist inkonsistent: " + e.Message, e);
        }
    }

    /// <summary>
    /// v1 → v2: Der Erzvorrat lag im Rohstofffeld, Asteroiden waren reine Hindernisse. Jetzt trägt jeder Gürtel-Asteroid
    /// seinen Anteil des alten Feldvorrats; Asteroiden außerhalb jedes Gürtels bleiben erzlose Hindernisse.
    /// </summary>
    static void MigrateV1ToV2(SaveGame s)
    {
        int nextId = Math.Max(1, s.NextAsteroidId);
        foreach (var sector in s.Sectors)
        {
            var legacy = sector.Asteroids ?? new List<Obstacle>();
            int FieldOf(Obstacle o)
            {
                int best = -1; float bestD = Mining.MiningRules.BeltRadius;
                for (int i = 0; i < sector.Fields.Count; i++)
                {
                    float d = Vec2.Distance(o.Position, sector.Fields[i].Pos);
                    if (d <= bestD) { bestD = d; best = i; }
                }
                return best;
            }
            var belongs = legacy.Select(FieldOf).ToList();
            for (int i = 0; i < legacy.Count; i++)
            {
                var o = legacy[i];
                int f = belongs[i];
                float ore = 0f; int good = 0;
                if (f >= 0)
                {
                    int n = belongs.Count(x => x == f);
                    ore = sector.Fields[f].Amount / Math.Max(1, n);
                    good = sector.Fields[f].Good;
                }
                sector.Rocks.Add(new AsteroidSave
                {
                    Id = nextId++, Pos = o.Position, BaseRadius = o.Radius, Good = good,
                    Ore = ore, MaxOre = Math.Max(ore, f >= 0 ? Asteroid.OrePerArea * o.Radius * o.Radius : 0f),
                });
            }
            sector.Asteroids = null;
            foreach (var field in sector.Fields)
            {
                field.TargetAsteroids = Math.Max(field.TargetAsteroids, 8);
                field.Amount = field.RegenPerTick = field.Capacity = 0f;
            }
        }
        s.NextAsteroidId = nextId;
        s.Version = 2;
    }

    /// <summary>
    /// v2 → v3: Felder und Asteroiden trugen eine einzelne Erzsorte (<c>Good</c>); jetzt gehören sie zu einer Asteroidenklasse mit
    /// Erzmischung. Jede alte Sorte wird zu einer Klasse "legacy_&lt;id&gt;" mit genau dieser Sorte. Bergbauaufträge verweisen statt auf die
    /// Ware auf den Feldindex.
    /// </summary>
    static void MigrateV2ToV3(SaveGame s)
    {
        var classes = new Dictionary<int, string>();
        string ClassFor(int good)
        {
            if (good <= 0) return "";
            if (!classes.TryGetValue(good, out var key))
            {
                key = "legacy_" + good;
                classes[good] = key;
                var name = s.Goods.FirstOrDefault(g => g.Id == good)?.Name ?? ("Ware " + good);
                s.AsteroidClasses.Add(new AsteroidClassSave
                {
                    Key = key, Name = name + "-Asteroid",
                    Yields = new List<YieldSave> { new() { Good = good, Weight = 1f } },
                });
            }
            return key;
        }

        var sectorFields = new Dictionary<int, List<FieldSave>>();
        foreach (var sector in s.Sectors)
        {
            foreach (var f in sector.Fields) { f.Class = ClassFor(f.Good); f.Good = 0; }
            foreach (var r in sector.Rocks) { r.Class = ClassFor(r.Good); r.Good = 0; }
            sectorFields[sector.Id] = sector.Fields;
        }
        foreach (var ship in s.Ships)
        {
            if (ship.Order is not { Kind: "mine" } order) continue;
            // Alt: B = Warenid. Neu: B = Index des Feldes dieser Ware im Sektor
            string key = ClassFor(order.B);
            var fields = sectorFields.GetValueOrDefault(order.A) ?? new List<FieldSave>();
            int idx = fields.FindIndex(f => f.Class == key);
            if (idx < 0) { ship.Order = null; ship.MinePhase = "Idle"; ship.Cargo.Clear(); continue; }
            order.B = idx;
        }
        s.Version = 3;
    }

    /// <summary>
    /// v3 → v4: Schiffe bekommen <c>ModelKey</c> (Einheitsmodell nach Rolle und Laderaum, dieselbe Zuordnung wie
    /// <c>WorldBuilder.UnitModelFor</c>: trader/100 → unit_trader_100, trader/60 → unit_trader_60,
    /// miner/100 → unit_miner_100, miner/60 → unit_miner_60, player → unit_player, sonst "" ohne Slots).
    /// Slots/Fit setzt die Migration nicht; sie werden beim Laden aus dem Modell gefüllt. Da v3-Saves die
    /// Slot-Content-Sektionen nicht kennen, reicht die Migration die Default-Welt-Registry nach (v4-Saves
    /// speichern die Sektionen selbst wie die übrigen Content-Sektionen).
    /// </summary>
    static void MigrateV3ToV4(SaveGame s)
    {
        foreach (var ship in s.Ships)
        {
            var role = Enum.TryParse<ShipRole>(ship.Role, ignoreCase: true, out var r) ? r : (ShipRole)(-1);
            ship.ModelKey = WorldBuilder.UnitModelFor(role, ship.CargoCapacity);
        }
        if (s.ShipModels.Count == 0 && s.Ships.Any(sh => !string.IsNullOrEmpty(sh.ModelKey)))
        {
            var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
            var content = new Universe();
            // Waren mitladen, damit WriteSlotContent die Treibstoff-GoodIds der Module als Schlüssel schreiben kann
            for (int i = 0; i < def.Goods.Count; i++)
            {
                var g = def.Goods[i];
                content.Goods[new GoodId(i + 1)] = new Good { Id = new GoodId(i + 1), Key = g.Key, Name = g.Name, Color = g.Color, BasePrice = g.BasePrice, VolumePerUnit = g.Volume };
            }
            WorldBuilder.LoadSlotContent(content, def);
            UniverseSerializer.WriteSlotContent(content, s);
        }
        s.Version = 4;
    }

    /// <summary>
    /// v4 → v5: Schiffe bekommen <c>Fuel</c> — Schiffe mit Modell einen vollen Tank ihres ersten zulässigen Treibstoffs
    /// (in der Default-Welt propellant), Schiffe ohne Modell bleiben leer. Die Content-Sektionen des Save sind v4-Stand:
    /// Die neue Ware <c>propellant</c> wird ans Ende der Warenliste angehängt (GoodId = Index+1, bestehende Ids verschieben
    /// sich nicht; die Ware hat noch keinen Erzeuger/Markt — Stationen und ihre Märkte bleiben unberührt, Phase 4b), und
    /// die geänderten Slot-Content-Einträge (Module mit <c>fuel</c>, Klassen mit <c>baseFuel</c>) werden per Upsert nach
    /// Schlüssel aus der Default-Welt nachgereicht, wie MigrateV3ToV4 die Slot-Registry nachgereicht hat; eigene Einträge
    /// des Save, die die Default-Welt nicht kennt, bleiben erhalten.
    /// </summary>
    static void MigrateV4ToV5(SaveGame s)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());

        // 1) Ware propellant ans Ende der Warenliste anhängen (GoodIds der bestehenden Waren bleiben stabil)
        int propId = s.Goods.FirstOrDefault(g => g.Key == "propellant")?.Id ?? 0;
        var propDef = def.Goods.FirstOrDefault(g => g.Key == "propellant");
        if (propId == 0 && propDef is not null)
        {
            propId = s.Goods.Count + 1;
            s.Goods.Add(new GoodSave
            {
                Id = propId, Key = propDef.Key, Name = propDef.Name, Color = propDef.Color,
                BasePrice = propDef.BasePrice, Volume = propDef.Volume, Form = propDef.Form,
            });
        }

        // 2) Geänderte Module/Klassen (fuel, baseFuel) aus der Default-Welt nachreichen; Slotarten/Modelle haben keine neuen Felder
        foreach (var md in def.Modules)
        {
            var converted = new ModuleSave
            {
                Key = md.Key, Slot = md.Slot, Size = md.Size, Tier = md.Tier, Grants = new List<string>(md.Grants),
                Stats = new Dictionary<string, float>(md.Stats), PowerDraw = md.PowerDraw, PeakDraw = md.PeakDraw,
                Mass = md.Mass, Upkeep = md.Upkeep, Holds = md.Holds ?? "any", Cost = new Dictionary<string, float>(md.Cost),
                Fuel = md.Fuel.Select(f => new FuelSave { Good = f.Good, Efficiency = f.Efficiency }).ToList(),
            };
            int i = s.Modules.FindIndex(m => m.Key == md.Key);
            if (i < 0) s.Modules.Add(converted); else s.Modules[i] = converted;
        }
        foreach (var cd in def.ShipClasses)
        {
            var converted = new ShipClassSave
            {
                Key = cd.Key, Name = cd.Name, Size = cd.Size, BaseHull = cd.BaseHull, BaseMass = cd.BaseMass, BaseFuel = cd.BaseFuel,
                Slots = cd.Slots.Select(sl => new ShipSlotSave { Type = sl.Type, Size = sl.Size, Facing = sl.Facing, Arc = sl.Arc }).ToList(),
            };
            int i = s.ShipClasses.FindIndex(c => c.Key == cd.Key);
            if (i < 0) s.ShipClasses.Add(converted); else s.ShipClasses[i] = converted;
        }

        // 3) Schiffe mit Modell: voller Tank des ersten zulässigen Treibstoffs (Kapazität = baseFuel der Klasse + fuelCapacity der Fit-Module)
        // (alte Spielstände haben Waren ohne Schlüssel — doppelte/leere Schlüssel überspringen)
        var goodIdByKey = new Dictionary<string, int>();
        foreach (var g in s.Goods) if (g.Key.Length > 0) goodIdByKey.TryAdd(g.Key, g.Id);
        foreach (var ship in s.Ships)
        {
            ship.Fuel ??= new Dictionary<int, float>();
            if (ship.ModelKey is not { Length: > 0 } mk) continue;   // Schiff ohne Modell: leer
            var model = s.ShipModels.FirstOrDefault(m => m.Key == mk);
            var cls = model is null ? null : s.ShipClasses.FirstOrDefault(c => c.Key == model.Class);
            if (model is null || cls is null) continue;
            float cap = cls.BaseFuel;
            string? firstFuel = null;
            foreach (var fk in model.Fit)
            {
                var m = fk is null ? null : s.Modules.FirstOrDefault(x => x.Key == fk);
                if (m is null) continue;
                if (m.Stats.TryGetValue("fuelCapacity", out var fc)) cap += fc;
                if (firstFuel is null && m.Fuel.Count > 0) firstFuel = m.Fuel[0].Good;   // erster zulässiger Treibstoff im Fit
            }
            if (firstFuel is not null && cap > 0f && goodIdByKey.TryGetValue(firstFuel, out var fid))
                ship.Fuel[fid] = cap;
        }

        // 4) Treibstoffraffinerien nachreichen: Welten vor Phase 4 haben keine Treibstoffquelle, die KI-Flotte bliebe sonst
        //    nach dem ersten Tank liegen. Stationstyp und die Raffinerien der Default-Welt werden ergänzt, wo der Sektor existiert
        //    und noch keine Raffinerie steht; Hindernis-Asteroiden am Bauplatz werden entfernt.
        var refType = def.StationTypes.FirstOrDefault(t => t.Key == "fuel_refinery");
        if (refType is not null && refType.Recipes.All(r => r.In.Keys.Concat(r.Out.Keys).All(goodIdByKey.ContainsKey)))
        {
            List<RecipeSave> Recipes() => refType.Recipes.Select(r => new RecipeSave
            {
                Ticks = r.Ticks,
                In = r.In.ToDictionary(kv => goodIdByKey[kv.Key], kv => kv.Value),
                Out = r.Out.ToDictionary(kv => goodIdByKey[kv.Key], kv => kv.Value),
            }).ToList();
            if (s.StationTypes.All(t => t.Key != refType.Key))
                s.StationTypes.Add(new StationTypeSave
                {
                    Key = refType.Key, Name = refType.Name, BuildCost = refType.BuildCost, Upkeep = refType.Upkeep, Income = refType.Income,
                    Recipes = Recipes(), Services = new List<string>(refType.Services),
                });
            foreach (var sd in def.Sectors)
            {
                var sector = s.Sectors.FirstOrDefault(x => x.Id == sd.Id);
                if (sector is null) continue;
                if (s.Stations.Any(st => st.Sector == sd.Id && st.Type == refType.Key)) continue;
                foreach (var std in sd.Stations.Where(x => x.Type == refType.Key))
                {
                    var pos = new Vec2(std.Pos[0], std.Pos[1]);
                    if (s.Stations.Any(st => st.Sector == sd.Id && Vec2.Distance(st.Pos, pos) < 50f)) continue;   // Platz belegt
                    int id = s.Stations.Any(st => st.Id == std.Id) ? Math.Max(s.NextStationId, s.Stations.Max(st => st.Id) + 1) : std.Id;
                    var save = new StationSave
                    {
                        Id = id, Type = refType.Key, Name = std.Name, Sector = sd.Id, Owner = sector.Owner, Pos = pos,
                        Credits = std.Credits, Essential = std.Essential, BelowLimitSince = -1, Recipes = Recipes(),
                    };
                    foreach (var rc in save.Recipes)
                        foreach (var g in rc.In.Keys.Concat(rc.Out.Keys))
                            save.Market.TryAdd(g, new StockSave { Amount = 50f, Target = 100f });
                    foreach (var (key, v) in std.Stock)
                        if (goodIdByKey.TryGetValue(key, out var gid) && v.Length >= 2)
                            save.Market[gid] = new StockSave { Amount = v[0], Target = v[1] };
                    foreach (var (gid, e) in save.Market)
                        e.Price = s.Goods.First(g => g.Id == gid).BasePrice;
                    s.Stations.Add(save);
                    sector.Stations.Add(id);
                    if (id >= s.NextStationId) s.NextStationId = id + 1;
                    sector.Rocks.RemoveAll(a => Vec2.Distance(a.Pos, pos) < a.BaseRadius + 30f);
                }
            }
        }
        s.Version = 5;
    }

    /// <summary>Hebt ältere Spielstände schrittweise auf das aktuelle Format. Bei Formatänderungen hier je Version einen Schritt ergänzen.</summary>
    static SaveGame Migrate(SaveGame s)
    {
        if (s.Version < 2) MigrateV1ToV2(s);
        if (s.Version < 3) MigrateV2ToV3(s);
        if (s.Version < 4) MigrateV3ToV4(s);
        if (s.Version < 5) MigrateV4ToV5(s);
        return s;
    }
}
