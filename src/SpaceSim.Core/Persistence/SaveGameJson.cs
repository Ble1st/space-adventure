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
        //    Sehr alte Welten kennen Wasserstoff/Energie nicht als Schlüssel: dort bleibt nur die Rezeptur ohne Eingänge (Gasförderung).
        var usable = refType?.Recipes.Where(r => r.In.Keys.Concat(r.Out.Keys).All(goodIdByKey.ContainsKey)).ToList();
        if (refType is not null && usable is { Count: > 0 })
        {
            List<RecipeSave> Recipes() => usable.Select(r => new RecipeSave
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
            AddDefaultStations(s, def, refType.Key, Recipes, null, goodIdByKey);
        }
        s.Version = 5;
    }

    /// <summary>
    /// Ergänzt die Stationen eines Typs aus der Default-Welt (Migrationen v4→v5 Raffinerien, v5→v6 Modulfabriken): je Default-Sektor,
    /// der im Save existiert und noch keine Station dieses Typs hat; Bauplatz der Default-Welt, sonst gespiegelte Ausweichplätze frei von
    /// Stationen, Gates und Gürtelzentren. Markt: Rezeptwaren 50/100, dann die Lagerposten des Typs, dann der Stationsbestand der
    /// Default-Welt; Preise = Basispreis. Hindernis-Asteroiden am Bauplatz werden entfernt.
    /// </summary>
    static void AddDefaultStations(SaveGame s, WorldDef def, string typeKey, Func<List<RecipeSave>> recipes, List<StationStockSave>? stocks, Dictionary<string, int> goodIdByKey)
    {
        foreach (var sd in def.Sectors)
        {
            var sector = s.Sectors.FirstOrDefault(x => x.Id == sd.Id);
            if (sector is null) continue;
            if (s.Stations.Any(st => st.Sector == sd.Id && st.Type == typeKey)) continue;
            foreach (var std in sd.Stations.Where(x => x.Type == typeKey))
            {
                // Bauplatz der Default-Welt, sonst gespiegelte Ausweichplätze: frei von Stationen, Gates und Gürtelzentren
                var p0 = new Vec2(std.Pos[0], std.Pos[1]);
                Vec2? free = null;
                foreach (var c in new[] { p0, new Vec2(-p0.X, p0.Y), new Vec2(p0.X, -p0.Y), new Vec2(-p0.X, -p0.Y), new Vec2(p0.Y, p0.X), new Vec2(-p0.Y, -p0.X) })
                {
                    if (s.Stations.Any(st => st.Sector == sd.Id && Vec2.Distance(st.Pos, c) < 50f)) continue;
                    if (s.Gates.Any(g => g.Sector == sd.Id && Vec2.Distance(g.Pos, c) < 50f)) continue;
                    if (sector.Fields.Any(f => Vec2.Distance(f.Pos, c) < 60f)) continue;
                    free = c; break;
                }
                if (free is not { } pos) continue;   // kein Platz
                int id = s.Stations.Any(st => st.Id == std.Id) ? Math.Max(s.NextStationId, s.Stations.Max(st => st.Id) + 1) : std.Id;
                var save = new StationSave
                {
                    Id = id, Type = typeKey, Name = std.Name, Sector = sd.Id, Owner = sector.Owner, Pos = pos,
                    Credits = std.Credits, Essential = std.Essential, BelowLimitSince = -1, Recipes = recipes(),
                };
                foreach (var rc in save.Recipes)
                    foreach (var g in rc.In.Keys.Concat(rc.Out.Keys))
                        save.Market.TryAdd(g, new StockSave { Amount = 50f, Target = 100f });
                if (stocks is not null)
                    foreach (var x in stocks) save.Market[x.Good] = new StockSave { Amount = x.Amount, Target = x.Target };
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

    /// <summary>
    /// v5 → v6 (Phase 5a): Module sind Waren, drei Modulfabriken, Bausatzstufen. Alle Schritte arbeiten nach Schlüssel und hängen nur an,
    /// damit bestehende GoodIds nicht verschoben werden:
    /// 1) Ware ship_kit heißt jetzt ship_kit_light (gleiche Id), Regel ShipKitGoodKey ebenso.
    /// 2) ship_kit_medium und ship_kit_heavy werden ans Ende der Warenliste angehängt.
    /// 3) Slot-Content (Slotarten mit Farbe, Module mit Namen und die neuen Mk1-Module, Klassen mit Bausatz) per Upsert nach Schlüssel aus der Default-Welt.
    /// 4) Je Modul ohne Ware wird eine Modul-Ware angehängt (nach Modulschlüssel; Preis = Σ cost × Basispreis × ModuleMarkup).
    /// 5) Stationstypen der Default-Welt: fehlende Modulfabrik-Typen werden ergänzt; bei vorhandenen Typen werden fehlende Rezepte
    ///    (nach Ausgangswaren, z. B. die Bausatzstufen der Werft) und die Lagerposten (stocks) an Typ und bestehenden Stationen nachgetragen.
    /// 6) Die Modulfabriken der Default-Welt werden gesetzt, wo der Sektor existiert (wie die Raffinerien in v4→v5).
    /// Sehr alte Welten ohne Warenschlüssel bekommen nur, was sich auflösen lässt.
    /// </summary>
    static void MigrateV5ToV6(SaveGame s)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var dw = WorldBuilder.Build(def);   // Default-Welt mit Modul-Waren und ausgeklappten Modulfabrik-Rezepten

        // 1) ship_kit → ship_kit_light (Id bleibt)
        var lightDef = def.Goods.First(g => g.Key == "ship_kit_light");
        var oldKit = s.Goods.FirstOrDefault(g => g.Key == "ship_kit");
        if (oldKit is not null && s.Goods.All(g => g.Key != lightDef.Key)) { oldKit.Key = lightDef.Key; oldKit.Name = lightDef.Name; }
        if (s.Rules.ShipKitGoodKey == "ship_kit") s.Rules.ShipKitGoodKey = lightDef.Key;

        // 2) neue Bausatzstufen anhängen
        foreach (var key in new[] { "ship_kit_medium", "ship_kit_heavy" })
        {
            var gd = def.Goods.FirstOrDefault(g => g.Key == key);
            if (gd is null || s.Goods.Any(g => g.Key == key)) continue;
            s.Goods.Add(new GoodSave { Id = NextGoodId(s), Key = gd.Key, Name = gd.Name, Color = gd.Color, BasePrice = gd.BasePrice, Volume = gd.Volume, Form = gd.Form });
        }

        // 3) Slot-Content per Upsert nach Schlüssel (eigene Einträge des Save bleiben)
        var content = new SaveGame();
        UniverseSerializer.WriteSlotContent(dw, content);
        foreach (var t in content.SlotTypes) Upsert(s.SlotTypes, t, x => x.Key);
        foreach (var m in content.Modules) Upsert(s.Modules, m, x => x.Key);
        foreach (var c in content.ShipClasses) Upsert(s.ShipClasses, c, x => x.Key);
        foreach (var m in content.ShipModels) if (s.ShipModels.All(x => x.Key != m.Key)) s.ShipModels.Add(m);

        // 4) Modul-Waren anhängen (feste Reihenfolge nach Modulschlüssel)
        float? PriceOf(string key) => s.Goods.FirstOrDefault(g => g.Key == key)?.BasePrice ?? def.Goods.FirstOrDefault(g => g.Key == key)?.BasePrice;
        foreach (var m in ModuleGoods.InGoodOrder(s.Modules, m => m.Key))
        {
            if (s.Goods.Any(g => g.Key == m.Key)) continue;
            s.Goods.Add(new GoodSave
            {
                Id = NextGoodId(s), Key = m.Key, Name = m.Name ?? m.Key,
                Color = s.SlotTypes.FirstOrDefault(t => t.Key == m.Slot)?.Color ?? ModuleGoods.DefaultColor,
                BasePrice = ModuleGoods.BasePrice(m.Cost, PriceOf, s.Rules.ModuleMarkup), Volume = ModuleGoods.Volume(m.Mass), Form = "solid",
            });
        }

        // 5) Stationstypen: Default-Ids → Schlüssel → Save-Ids
        var goodIdByKey = new Dictionary<string, int>();
        foreach (var g in s.Goods) if (g.Key.Length > 0) goodIdByKey.TryAdd(g.Key, g.Id);
        int? Map(GoodId dg) => goodIdByKey.TryGetValue(dw.Goods[dg].Key, out var id) ? id : null;
        RecipeSave? ConvertRecipe(Recipe r)
        {
            var rs = new RecipeSave { Ticks = r.TicksPerCycle };
            foreach (var (g, v) in r.Inputs) { if (Map(g) is not { } id) return null; rs.In[id] = v; }
            foreach (var (g, v) in r.Outputs) { if (Map(g) is not { } id) return null; rs.Out[id] = v; }
            return rs;
        }
        List<StationStockSave> ConvertStocks(StationType t) =>
            t.Stocks.Where(x => Map(x.Good) is not null).Select(x => new StationStockSave { Good = Map(x.Good)!.Value, Amount = x.Amount, Target = x.Target }).ToList();
        var addedTypes = new List<StationTypeSave>();
        foreach (var dt in dw.StationTypes.Values)
        {
            var recipes = dt.Recipes.Select(ConvertRecipe).Where(r => r is not null).Select(r => r!).ToList();
            var stocks = ConvertStocks(dt);
            var existing = s.StationTypes.FirstOrDefault(t => t.Key == dt.Key);
            if (existing is null)
            {
                // nur die neuen Modulfabriken ergänzen (alle Ausgänge sind Modul-Waren); andere fehlende Typen älterer Welten bleiben weg
                bool moduleFactory = dt.Recipes.Count > 0 && dt.Recipes.All(r => r.Outputs.Count > 0 && r.Outputs.Keys.All(g => dw.Modules.ContainsKey(dw.Goods[g].Key)));
                if (!moduleFactory || recipes.Count == 0) continue;   // kein Modulwerk, oder Welt ohne die nötigen Waren
                var added = new StationTypeSave
                {
                    Key = dt.Key, Name = dt.Name, BuildCost = dt.BuildCost, Upkeep = dt.UpkeepPerTick, Income = dt.IncomePerTick,
                    Recipes = recipes, Services = new List<string>(dt.Services), Stocks = stocks.Count > 0 ? stocks : null,
                };
                s.StationTypes.Add(added);
                addedTypes.Add(added);
                continue;
            }
            // vorhandener Typ: Rezepte mit neuen Ausgangswaren und Lagerposten nachtragen (Typ und bestehende Stationen) — nur wenn
            // die Welt alle Waren des Default-Typs kennt (sehr alte Welten ohne Schlüssel bleiben unverändert)
            if (recipes.Count != dt.Recipes.Count) continue;
            var newRecipes = recipes.Where(r => r.Out.Count > 0 && !existing.Recipes.Any(e => e.Out.Keys.ToHashSet().SetEquals(r.Out.Keys))).ToList();
            var newStocks = existing.Stocks is null && stocks.Count > 0 ? stocks : new List<StationStockSave>();
            if (newRecipes.Count == 0 && newStocks.Count == 0) continue;
            existing.Recipes.AddRange(newRecipes.Select(CloneRecipe));
            if (newStocks.Count > 0) existing.Stocks = newStocks;
            foreach (var st in s.Stations.Where(x => x.Type == dt.Key))
            {
                foreach (var r in newRecipes)
                {
                    st.Recipes.Add(CloneRecipe(r));
                    foreach (var g in r.In.Keys.Concat(r.Out.Keys))
                        st.Market.TryAdd(g, new StockSave { Amount = 50f, Target = 100f, Price = s.Goods.First(x => x.Id == g).BasePrice });
                }
                foreach (var x in newStocks)
                {
                    // Lagerposten des Typs: neue Einträge anlegen, für frisch angelegte Rezeptwaren den Wunschbestand des Typs setzen
                    bool fresh = !st.Market.TryGetValue(x.Good, out var e) || newRecipes.Any(r => r.Out.ContainsKey(x.Good) || r.In.ContainsKey(x.Good));
                    if (fresh) st.Market[x.Good] = new StockSave { Amount = x.Amount, Target = x.Target, Price = s.Goods.First(g => g.Id == x.Good).BasePrice };
                }
            }
        }

        // 6) Modulfabriken (neu ergänzte Typen) an ihren Default-Plätzen setzen
        foreach (var t in addedTypes)
            AddDefaultStations(s, def, t.Key, () => t.Recipes.Select(CloneRecipe).ToList(), t.Stocks, goodIdByKey);

        s.Version = 6;
    }

    /// <summary>
    /// v6 → v7 (Phase 5b, Spielerkasse): Das Geld des Spielers hängt an der Person, nicht am Schiff. Die Credits aller
    /// Spielerschiffe (Steuerer „Player“, in alten Ständen ohne Steuerer die Rolle „Player“) wandern in die Spielerkasse,
    /// die Schiffe behalten 0. Fraktion der Kasse = die des gespeicherten Spielerschiffs. NPC-Schiffe bleiben unverändert.
    /// </summary>
    static void MigrateV6ToV7(SaveGame s)
    {
        var wallet = new PlayerWalletSave();
        foreach (var sh in s.Ships.OrderBy(x => x.Id))
        {
            bool player = sh.Controller is { } c ? c == nameof(Controller.Player) : sh.Role == nameof(ShipRole.Player);
            if (!player) continue;
            wallet.Credits += sh.Credits;
            sh.Credits = 0f;
        }
        var main = s.Ships.FirstOrDefault(x => x.Id == s.PlayerShip);
        if (main is not null) wallet.Faction = main.Faction;
        s.PlayerWallet = wallet;
        s.Version = 7;
    }

    /// <summary>
    /// v7 → v8 (Phase 5c, Ausrüster und Schiffskauf): Alles nach Schlüssel, nur ergänzend (eigene Einträge des Save bleiben):
    /// 1) Schiffsklassen und -modelle der Default-Welt, die fehlen (Skiff: Pip, Nomad, Prospector), werden angehängt.
    /// 2) Stationstypen, die es in Save und Default-Welt gibt: fehlende Dienste (outfitter, shipdealer), der Schiffshändler und fehlende
    ///    Lagerposten (stocks) werden nachgetragen — an Typ und bestehenden Stationen (neuer Markteintrag mit Preis = Basispreis).
    /// 3) Die Handelsstation (Typ ohne Rezept, nur stocks) wird als Typ ergänzt und an ihrem Default-Platz gesetzt, wenn alle ihre
    ///    Lagerwaren auflösbar sind; die Stationsobergrenze steigt je gesetzter Station um 1 (wie in der Default-Welt).
    /// Mehrere Spielerschiffe brauchen kein neues Feld: Besitz = Steuerer „Player“, aktiv = <c>PlayerShip</c>.
    /// </summary>
    static void MigrateV7ToV8(SaveGame s)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var dw = WorldBuilder.Build(def);
        var content = new SaveGame();
        UniverseSerializer.WriteSlotContent(dw, content);
        foreach (var c in content.ShipClasses) if (s.ShipClasses.All(x => x.Key != c.Key)) s.ShipClasses.Add(c);
        foreach (var m in content.ShipModels)
            if (s.ShipModels.All(x => x.Key != m.Key) && s.ShipClasses.Any(c => c.Key == m.Class)
                && m.Fit.All(k => k is null || s.Modules.Any(x => x.Key == k)))
                s.ShipModels.Add(m);

        var goodIdByKey = new Dictionary<string, int>();
        foreach (var g in s.Goods) if (g.Key.Length > 0) goodIdByKey.TryAdd(g.Key, g.Id);
        int? Map(GoodId dg) => goodIdByKey.TryGetValue(dw.Goods[dg].Key, out var id) ? id : null;
        float BasePrice(int id) => s.Goods.First(g => g.Id == id).BasePrice;
        ShipDealerSave? Dealer(StationType t) => t.ShipDealer is { } sd
            ? new ShipDealerSave { Classes = sd.Classes is null ? null : new List<string>(sd.Classes), MaxSize = sd.MaxSize.ToString(), Assembly = sd.Assembly }
            : null;

        // 2) vorhandene Typen: Dienste, Schiffshändler, Lagerposten nachtragen
        foreach (var dt in dw.StationTypes.Values.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var existing = s.StationTypes.FirstOrDefault(t => t.Key == dt.Key);
            if (existing is null) continue;
            var services = existing.Services ?? new List<string> { Services.TradeService.ServiceKey };
            foreach (var key in dt.Services) if (!services.Contains(key)) services.Add(key);
            existing.Services = services;
            existing.ShipDealer ??= Dealer(dt);
            var newStocks = dt.Stocks.Where(x => Map(x.Good) is { } id && (existing.Stocks is null || existing.Stocks.All(e => e.Good != id)))
                .Select(x => new StationStockSave { Good = Map(x.Good)!.Value, Amount = x.Amount, Target = x.Target }).ToList();
            if (newStocks.Count == 0) continue;
            existing.Stocks = (existing.Stocks ?? new List<StationStockSave>()).Concat(newStocks).ToList();
            foreach (var st in s.Stations.Where(x => x.Type == dt.Key))
                foreach (var x in newStocks)
                    st.Market.TryAdd(x.Good, new StockSave { Amount = x.Amount, Target = x.Target, Price = BasePrice(x.Good) });
        }

        // 3) neue Typen ohne Rezept (Handelsstation): ergänzen und an ihren Default-Plätzen setzen
        foreach (var dt in dw.StationTypes.Values.Where(t => t.Recipes.Count == 0 && t.Stocks.Count > 0).OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (s.StationTypes.Any(t => t.Key == dt.Key) || dt.Stocks.Any(x => Map(x.Good) is null)) continue;
            var stocks = dt.Stocks.Select(x => new StationStockSave { Good = Map(x.Good)!.Value, Amount = x.Amount, Target = x.Target }).ToList();
            s.StationTypes.Add(new StationTypeSave
            {
                Key = dt.Key, Name = dt.Name, BuildCost = dt.BuildCost, Upkeep = dt.UpkeepPerTick, Income = dt.IncomePerTick,
                Recipes = new List<RecipeSave>(), Services = new List<string>(dt.Services), Stocks = stocks, ShipDealer = Dealer(dt),
            });
            int before = s.Stations.Count;
            AddDefaultStations(s, def, dt.Key, () => new List<RecipeSave>(), stocks, goodIdByKey);
            s.Rules.MaxStations += s.Stations.Count - before;
        }
        s.Version = 8;
    }

    /// <summary>
    /// v8 → v9 (Phase 6, Multirole): Schiffe bekommen <c>JobSince</c> (fehlt = 0, nichts zu tun). Der neue Content wird nach Schlüssel und
    /// nur ergänzend nachgereicht, eigene Einträge des Save bleiben:
    /// 1) fehlende Module der Default-Welt (Frachtraum M, Kanisterraum M, Triebwerk M, Mining-Laser Mk2) und je Modul ohne Ware eine Modul-Ware
    ///    (angehängt nach Modulschlüssel, bestehende GoodIds bleiben);
    /// 2) fehlende Schiffsklassen und -modelle (Kurier, Frachter, Bergbauer, Tanker; Tinker als erstes Multirole-Modell); vorhandene Modelle der
    ///    Default-Welt ohne Aufgabenliste bekommen deren <c>jobs</c> (Einheitsmodelle: Handel bzw. Schürfen);
    /// 3) vorhandene Stationstypen: fehlende Rezepte (nach Ausgangswaren, nur wenn alle Waren auflösbar sind; z. B. die neuen Module der
    ///    Modulwerke) und fehlende Lagerposten an Typ und Stationen.
    /// Wird auch beim Anheben älterer Stände durchlaufen; alle Schritte prüfen, was die früheren Migrationen schon ergänzt haben.
    /// </summary>
    static void MigrateV8ToV9(SaveGame s)
    {
        AddMissingDefaultContent(s);
        s.Version = 9;
    }

    /// <summary>
    /// v9 → v10 (Phase 7, Güteklassen und Upgrades): Schiffe bekommen <c>Quality</c> (fehlt = Standard) und <c>UpgradePity</c> (fehlt = leer);
    /// Bonusslots stehen ohnehin in der gespeicherten Slotliste. Content nach Schlüssel und nur ergänzend: neue Module (Reaktor Mk2, Triebwerk S Mk2)
    /// mit Waren, Fabrikrezepten und Werftlager (wie v8→v9), Verbesserungen (<c>upgrade</c>) an den Default-Modulen, die noch keine haben,
    /// <c>maxSlotUpgrades</c> der Default-Klassen und der Dienst „upgrade“ an den Stationstypen der Default-Welt, die ihn führen (Werft).
    /// </summary>
    static void MigrateV9ToV10(SaveGame s)
    {
        AddMissingDefaultContent(s);
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        foreach (var md in def.Modules)
            if (md.Upgrade is { } up && s.Modules.FirstOrDefault(m => m.Key == md.Key) is { UpgradeTo: null } ms && s.Modules.Any(m => m.Key == up.To))
            { ms.UpgradeTo = up.To; ms.UpgradeCost = new Dictionary<string, float>(up.Cost); }
        foreach (var cd in def.ShipClasses)
            if (s.ShipClasses.FirstOrDefault(c => c.Key == cd.Key) is { } cs) cs.MaxSlotUpgrades = cd.MaxSlotUpgrades;
        s.Version = 10;
    }

    /// <summary>
    /// v10 → v11 (Phase 8, Bau, Brecher, Lagerhaus). Alles nach Schlüssel und nur ergänzend:
    /// 1) Content der Default-Welt (<see cref="AddMissingDefaultContent"/>): Bauausleger und Spaltlaser als Module mit Waren, Systemwerk-Rezepte
    ///    und Lagerposten, Klassen Bauschiff und Asteroidenbrecher, Modelle Foreman, Quarry, Rockbreaker II.
    /// 2) neue Stationstypen ohne Rezept: Baustelle (ohne Waren, immer) und Lagerhaus (nur wenn seine Lagerwaren auflösbar sind) samt der
    ///    Lagerhaus-Station der Default-Welt; die Stationsobergrenze steigt je gesetzter Station um 1.
    /// 3) Startbestand an Bauschiffen: je Fraktion ohne KI-Bauschiff ein Foreman (Sektor wie in der Default-Welt, sonst der erste eigene), mit
    ///    vollem Tank — nur wenn das Modell existiert und die Stückliste auflösbar ist (sonst baut die Welt weiter direkt, z. B. v2-Stände
    ///    ohne Warenschlüssel).
    /// 4) <c>Essential</c> heißt jetzt <c>BankruptcyProtected</c> (beim Laden ohnehin gleichbedeutend gelesen).
    /// Baustellen, Spielerlager und Notbau-Zeitstempel fehlen in v10 und starten leer.
    /// </summary>
    static void MigrateV10ToV11(SaveGame s)
    {
        AddMissingDefaultContent(s);
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var dw = WorldBuilder.Build(def);
        var goodIdByKey = new Dictionary<string, int>();
        foreach (var g in s.Goods) if (g.Key.Length > 0) goodIdByKey.TryAdd(g.Key, g.Id);
        int? Map(GoodId dg) => goodIdByKey.TryGetValue(dw.Goods[dg].Key, out var id) ? id : null;

        // 2) Baustellen-Typ und Lagerhaus
        foreach (var dt in dw.StationTypes.Values.Where(t => t.Recipes.Count == 0 && t.ShipDealer is null).OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (s.StationTypes.Any(t => t.Key == dt.Key) || dt.Stocks.Any(x => Map(x.Good) is null)) continue;
            var stocks = dt.Stocks.Select(x => new StationStockSave { Good = Map(x.Good)!.Value, Amount = x.Amount, Target = x.Target }).ToList();
            s.StationTypes.Add(new StationTypeSave
            {
                Key = dt.Key, Name = dt.Name, BuildCost = dt.BuildCost, Upkeep = dt.UpkeepPerTick, Income = dt.IncomePerTick,
                Recipes = new List<RecipeSave>(), Services = new List<string>(dt.Services), Stocks = stocks.Count == 0 ? null : stocks,
            });
            int before = s.Stations.Count;
            AddDefaultStations(s, def, dt.Key, () => new List<RecipeSave>(), stocks, goodIdByKey);
            s.Rules.MaxStations += s.Stations.Count - before;
        }

        // 3) ein Bauschiff je Fraktion
        bool billResolvable = s.Rules.ConstructionMaterials.Keys.All(goodIdByKey.ContainsKey);
        var model = s.ShipModels.FirstOrDefault(m => m.Key == "foreman");
        var cls = model is null ? null : s.ShipClasses.FirstOrDefault(c => c.Key == model.Class);
        if (billResolvable && model is not null && cls is not null && model.Fit.All(k => k is null || s.Modules.Any(m => m.Key == k)))
        {
            var fitted = model.Fit.Where(k => k is not null).Select(k => s.Modules.First(m => m.Key == k)).ToList();
            float fuelCap = cls.BaseFuel + fitted.Sum(m => m.Stats.GetValueOrDefault("fuelCapacity"));
            var fuelKey = fitted.SelectMany(m => m.Fuel).Select(f => f.Good).FirstOrDefault();
            float cargo = fitted.Sum(m => m.Stats.GetValueOrDefault("capacity"));
            float hull = cls.BaseHull + fitted.Sum(m => m.Stats.GetValueOrDefault("armor"));
            foreach (var f in s.Factions.OrderBy(f => f.Id))
            {
                if (s.Ships.Any(x => x.Faction == f.Id && x.Role == nameof(ShipRole.Builder))) continue;
                int? sectorId = def.Ships.Where(g => g.Role.Equals("builder", StringComparison.OrdinalIgnoreCase) && g.Faction == f.Id)
                    .Select(g => (int?)g.Sector).FirstOrDefault(id => s.Sectors.Any(x => x.Id == id && x.Owner == f.Id))
                    ?? s.Sectors.Where(x => x.Owner == f.Id).OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefault();
                if (sectorId is not { } sid) continue;
                int id = Math.Max(s.NextShipId, s.Ships.Count == 0 ? 1 : s.Ships.Max(x => x.Id) + 1);
                var anchor = s.Stations.Where(x => x.Sector == sid).OrderBy(x => x.Id).FirstOrDefault();
                var pos = anchor is null ? default : anchor.Pos + Vec2.FromAngle(id * 2.4f) * 22f;
                var ship = new ShipSave
                {
                    Id = id, Faction = f.Id, Role = nameof(ShipRole.Builder), Controller = nameof(Controller.Ai), ModelKey = model.Key,
                    CargoCapacity = cargo, Credits = 1500f, Hull = hull, Sector = sid, Pos = pos,
                    Phase = nameof(TradePhase.Idle), MinePhase = nameof(MinePhase.Idle), NextThinkTick = s.Tick, IdleSince = -1, JobSince = s.Tick,
                };
                if (fuelKey is not null && goodIdByKey.TryGetValue(fuelKey, out var fuelId) && fuelCap > 0f) ship.Fuel[fuelId] = fuelCap;
                s.Ships.Add(ship);
                s.NextShipId = id + 1;
            }
        }

        // 4) Essential → BankruptcyProtected
        foreach (var st in s.Stations) { st.BankruptcyProtected |= st.Essential; st.Essential = false; }
        s.Version = 11;
    }

    /// <summary>
    /// Reicht den Content der Default-Welt nach Schlüssel und nur ergänzend nach (gemeinsamer Schritt der Migrationen ab v8→v9):
    /// 1) fehlende Module und je Modul ohne Ware eine Modul-Ware (angehängt nach Modulschlüssel, bestehende GoodIds bleiben);
    /// 2) fehlende Schiffsklassen und -modelle; vorhandene Default-Modelle ohne Aufgabenliste bekommen deren <c>jobs</c>;
    /// 3) vorhandene Stationstypen: fehlende Dienste, fehlende Rezepte (nach Ausgangswaren, nur wenn alle Waren auflösbar sind) und fehlende
    ///    Lagerposten an Typ und Stationen.
    /// </summary>
    static void AddMissingDefaultContent(SaveGame s)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var dw = WorldBuilder.Build(def);
        var content = new SaveGame();
        UniverseSerializer.WriteSlotContent(dw, content);

        // 1) Module und ihre Waren
        foreach (var m in content.Modules) if (s.Modules.All(x => x.Key != m.Key)) s.Modules.Add(m);
        float? PriceOf(string key) => s.Goods.FirstOrDefault(g => g.Key == key)?.BasePrice ?? def.Goods.FirstOrDefault(g => g.Key == key)?.BasePrice;
        foreach (var m in ModuleGoods.InGoodOrder(s.Modules, m => m.Key))
        {
            if (s.Goods.Any(g => g.Key == m.Key)) continue;
            s.Goods.Add(new GoodSave
            {
                Id = NextGoodId(s), Key = m.Key, Name = m.Name ?? m.Key,
                Color = s.SlotTypes.FirstOrDefault(t => t.Key == m.Slot)?.Color ?? ModuleGoods.DefaultColor,
                BasePrice = ModuleGoods.BasePrice(m.Cost, PriceOf, s.Rules.ModuleMarkup), Volume = ModuleGoods.Volume(m.Mass), Form = "solid",
            });
        }

        // 2) Klassen, Modelle, Aufgabenlisten
        foreach (var c in content.ShipClasses) if (s.ShipClasses.All(x => x.Key != c.Key)) s.ShipClasses.Add(c);
        foreach (var m in content.ShipModels)
        {
            var existing = s.ShipModels.FirstOrDefault(x => x.Key == m.Key);
            if (existing is not null) { existing.Jobs ??= m.Jobs is null ? null : new List<string>(m.Jobs); continue; }
            if (s.ShipClasses.Any(c => c.Key == m.Class) && m.Fit.All(k => k is null || s.Modules.Any(x => x.Key == k)))
                s.ShipModels.Add(m);
        }

        // 3) Stationstypen: Rezepte und Lagerposten nachtragen
        var goodIdByKey = new Dictionary<string, int>();
        foreach (var g in s.Goods) if (g.Key.Length > 0) goodIdByKey.TryAdd(g.Key, g.Id);
        int? Map(GoodId dg) => goodIdByKey.TryGetValue(dw.Goods[dg].Key, out var id) ? id : null;
        float BasePrice(int id) => s.Goods.First(g => g.Id == id).BasePrice;
        foreach (var dt in dw.StationTypes.Values.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var existing = s.StationTypes.FirstOrDefault(t => t.Key == dt.Key);
            if (existing is null) continue;
            var services = existing.Services ?? new List<string> { Services.TradeService.ServiceKey };
            foreach (var key in dt.Services) if (!services.Contains(key)) services.Add(key);
            existing.Services = services;
            var newRecipes = new List<RecipeSave>();
            foreach (var r in dt.Recipes)
            {
                var rs = new RecipeSave { Ticks = r.TicksPerCycle };
                bool ok = true;
                foreach (var (g, v) in r.Inputs) { if (Map(g) is { } id) rs.In[id] = v; else ok = false; }
                foreach (var (g, v) in r.Outputs) { if (Map(g) is { } id) rs.Out[id] = v; else ok = false; }
                if (!ok || rs.Out.Count == 0 || existing.Recipes.Any(e => e.Out.Keys.ToHashSet().SetEquals(rs.Out.Keys))) continue;
                newRecipes.Add(rs);
            }
            var newStocks = dt.Stocks.Where(x => Map(x.Good) is { } id && (existing.Stocks is null || existing.Stocks.All(e => e.Good != id)))
                .Select(x => new StationStockSave { Good = Map(x.Good)!.Value, Amount = x.Amount, Target = x.Target }).ToList();
            if (newRecipes.Count == 0 && newStocks.Count == 0) continue;
            existing.Recipes.AddRange(newRecipes.Select(CloneRecipe));
            if (newStocks.Count > 0) existing.Stocks = (existing.Stocks ?? new List<StationStockSave>()).Concat(newStocks).ToList();
            foreach (var st in s.Stations.Where(x => x.Type == dt.Key))
            {
                var fresh = new HashSet<int>();
                foreach (var r in newRecipes)
                {
                    st.Recipes.Add(CloneRecipe(r));
                    foreach (var g in r.In.Keys.Concat(r.Out.Keys))
                        if (st.Market.TryAdd(g, new StockSave { Amount = 50f, Target = 100f, Price = BasePrice(g) })) fresh.Add(g);
                }
                foreach (var x in newStocks)
                    if (fresh.Contains(x.Good) || !st.Market.ContainsKey(x.Good))
                        st.Market[x.Good] = new StockSave { Amount = x.Amount, Target = x.Target, Price = BasePrice(x.Good) };
            }
        }
    }

    static int NextGoodId(SaveGame s) => s.Goods.Count == 0 ? 1 : s.Goods.Max(g => g.Id) + 1;

    static RecipeSave CloneRecipe(RecipeSave r) => new() { Ticks = r.Ticks, In = new Dictionary<int, float>(r.In), Out = new Dictionary<int, float>(r.Out) };

    static void Upsert<T>(List<T> list, T item, Func<T, string> key)
    {
        int i = list.FindIndex(x => key(x) == key(item));
        if (i < 0) list.Add(item); else list[i] = item;
    }

    /// <summary>Hebt ältere Spielstände schrittweise auf das aktuelle Format. Bei Formatänderungen hier je Version einen Schritt ergänzen.</summary>
    static SaveGame Migrate(SaveGame s)
    {
        if (s.Version < 2) MigrateV1ToV2(s);
        if (s.Version < 3) MigrateV2ToV3(s);
        if (s.Version < 4) MigrateV3ToV4(s);
        if (s.Version < 5) MigrateV4ToV5(s);
        if (s.Version < 6) MigrateV5ToV6(s);
        if (s.Version < 7) MigrateV6ToV7(s);
        if (s.Version < 8) MigrateV7ToV8(s);
        if (s.Version < 9) MigrateV8ToV9(s);
        if (s.Version < 10) MigrateV9ToV10(s);
        if (s.Version < 11) MigrateV10ToV11(s);
        return s;
    }
}
