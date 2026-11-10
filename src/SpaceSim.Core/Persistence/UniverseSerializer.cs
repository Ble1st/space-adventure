using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

/// <summary>Wandelt zwischen Laufzeitmodell (<see cref="Universe"/>) und Speicherformat (<see cref="SaveGame"/>).</summary>
public static class UniverseSerializer
{
    /// <summary>Schreibt die Slot-/Modul-Content-Sektionen (slotTypes, modules, shipClasses, shipModels) in den Save.</summary>
    public static void WriteSlotContent(Universe u, SaveGame s)
    {
        s.SlotTypes = u.SlotTypes.Values.OrderBy(t => t.Key)
            .Select(t => new SlotTypeSave { Key = t.Key, Name = t.Name, Required = t.Required, Color = t.Color }).ToList();
        s.Modules = u.Modules.Values.OrderBy(m => m.Key).Select(m => ToSave(m, u)).ToList();
        s.ShipClasses = u.ShipClasses.Values.OrderBy(c => c.Key).Select(ToSave).ToList();
        s.ShipModels = u.ShipModels.Values.OrderBy(m => m.Key)
            .Select(m => new ShipModelSave { Key = m.Key, Class = m.ClassKey, Name = m.Name, Fit = new List<string?>(m.Fit) }).ToList();
    }

    /// <summary>Stellt die Slot-/Modul-Content-Sektionen aus dem Save im Universum wieder her.</summary>
    public static void ReadSlotContent(SaveGame s, Universe u)
    {
        // Alte Spielstände (vor v3) haben Waren ohne Schlüssel — die dürfen nicht in die Schlüssel→Id-Map (doppelt/leer)
        var goodId = new Dictionary<string, GoodId>();
        foreach (var g in s.Goods) if (g.Key.Length > 0) goodId.TryAdd(g.Key, new GoodId(g.Id));
        foreach (var t in s.SlotTypes)
            u.SlotTypes[t.Key] = new SlotType { Key = t.Key, Name = t.Name, Required = t.Required, Color = t.Color };
        foreach (var m in s.Modules)
            u.Modules[m.Key] = FromSave(m, goodId);
        foreach (var c in s.ShipClasses)
            u.ShipClasses[c.Key] = FromSave(c);
        foreach (var m in s.ShipModels)
            u.ShipModels[m.Key] = new ShipModel { Key = m.Key, ClassKey = m.Class, Name = m.Name, Fit = new List<string?>(m.Fit) };
    }

    static ModuleSave ToSave(Module m, Universe u) => new()
    {
        Key = m.Key, Name = m.Name.Length > 0 && m.Name != m.Key ? m.Name : null, Slot = m.Slot, Size = m.Size.ToString(), Tier = m.Tier,
        Grants = m.Grants.Select(g => g.ToString()).ToList(),
        Stats = new Dictionary<string, float>(m.Stats),
        PowerDraw = m.PowerDraw, PeakDraw = m.PeakDraw, Mass = m.Mass, Upkeep = m.Upkeep,
        Holds = m.Holds.ToString().ToLowerInvariant(), Cost = new Dictionary<string, float>(m.Cost),
        // Treibstoff als Warenschlüssel (wie cost), damit GoodIds im Save nicht verschoben werden müssen
        Fuel = m.FuelTypes.Select(f => new FuelSave { Good = u.Goods[f.Good].Key, Efficiency = f.Efficiency }).ToList(),
    };

    static Module FromSave(ModuleSave m, Dictionary<string, GoodId> goodId) => new()
    {
        Key = m.Key, Name = m.Name ?? m.Key, Slot = m.Slot, Size = Enum.Parse<SlotSize>(m.Size, ignoreCase: true), Tier = m.Tier,
        Grants = m.Grants.Select(g => Enum.Parse<Capability>(g, ignoreCase: true)).ToList(),
        Stats = new Dictionary<string, float>(m.Stats),
        PowerDraw = m.PowerDraw, PeakDraw = m.PeakDraw, Mass = m.Mass, Upkeep = m.Upkeep,
        Holds = Enum.Parse<ModuleHolds>(m.Holds, ignoreCase: true), Cost = new Dictionary<string, float>(m.Cost),
        FuelTypes = m.Fuel.Select(f => new FuelType(
            goodId.TryGetValue(f.Good, out var gid) ? gid : throw new SaveFormatException($"Modul '{m.Key}': unbekannte Ware '{f.Good}' in fuel"),
            f.Efficiency)).ToList(),
    };

    static ShipClassSave ToSave(ShipClass c) => new()
    {
        Key = c.Key, Name = c.Name, Size = c.Size.ToString(), BaseHull = c.BaseHull, BaseMass = c.BaseMass, BaseFuel = c.BaseFuel,
        Slots = c.Slots.Select(ToSave).ToList(), Kit = c.Kit,
    };

    static ShipClass FromSave(ShipClassSave c) => new()
    {
        Key = c.Key, Name = c.Name, Size = Enum.Parse<SlotSize>(c.Size, ignoreCase: true), BaseHull = c.BaseHull, BaseMass = c.BaseMass, BaseFuel = c.BaseFuel,
        Slots = c.Slots.Select(FromSave).ToList(), Kit = c.Kit,
    };

    static ShipSlotSave ToSave(ClassSlot s) => new() { Type = s.Type, Size = s.Size.ToString(), Facing = s.Facing, Arc = s.Arc };

    static ClassSlot FromSave(ShipSlotSave s) => new() { Type = s.Type, Size = Enum.Parse<SlotSize>(s.Size, ignoreCase: true), Facing = s.Facing, Arc = s.Arc };

    public static SaveGame ToSave(Universe u, DateTimeOffset now)
    {
        var s = new SaveGame
        {
            SavedAtUtc = now.ToUniversalTime().ToString("o"),
            Tick = u.Tick, Seed = u.Seed, RngState = u.Rng.State, LastStationBuildTick = u.LastStationBuildTick,
            NextShipId = u.IdCounters.ship, NextStationId = u.IdCounters.station, NextAsteroidId = u.NextAsteroidId,
            PlayerSector = u.PlayerSector.Value, PlayerShip = u.PlayerShip.Value, Rules = u.Rules,
        };
        foreach (var g in u.Goods.Values.OrderBy(g => g.Id.Value))
            s.Goods.Add(new GoodSave { Id = g.Id.Value, Key = g.Key, Name = g.Name, Color = g.Color, BasePrice = g.BasePrice, Volume = g.VolumePerUnit, Form = g.Form.ToString().ToLowerInvariant() });
        foreach (var c in u.AsteroidClasses.Values)
            s.AsteroidClasses.Add(new AsteroidClassSave
            {
                Key = c.Key, Name = c.Name, Color = c.Color, ChunkChance = c.ChunkChance, OreFactor = c.OreFactor,
                Yields = c.Yields.Select(y => new YieldSave { Good = y.Good.Value, Weight = y.Weight }).ToList(),
            });
        foreach (var t in u.StationTypes.Values)
            s.StationTypes.Add(new StationTypeSave
            {
                Key = t.Key, Name = t.Name, BuildCost = t.BuildCost, Upkeep = t.UpkeepPerTick, Income = t.IncomePerTick,
                Recipes = t.Recipes.Select(ToSave).ToList(),
                Services = new List<string>(t.Services),
                Stocks = t.Stocks.Count == 0 ? null : t.Stocks.Select(x => new StationStockSave { Good = x.Good.Value, Amount = x.Amount, Target = x.Target }).ToList(),
            });
        WriteSlotContent(u, s);
        foreach (var f in u.Factions.Values.OrderBy(f => f.Id.Value))
            s.Factions.Add(new FactionSave
            {
                Id = f.Id.Value, Name = f.Name,
                Relations = f.Relations.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                Territory = f.Territory.Select(t => t.Value).OrderBy(x => x).ToList(),
            });
        foreach (var sec in u.Sectors.Values.OrderBy(x => x.Id.Value))
            s.Sectors.Add(new SectorSave
            {
                Id = sec.Id.Value, Name = sec.Name, Owner = sec.Owner.Value, Map = sec.MapPosition, Radius = sec.Radius,
                Gates = sec.Gates.Select(g => g.Value).ToList(), Stations = sec.Stations.Select(x => x.Value).ToList(),
                Fields = sec.Fields.Select(f => new FieldSave { Class = f.ClassKey, Pos = f.Position, TargetAsteroids = f.TargetAsteroids, RespawnTicks = f.RespawnTicks }).ToList(),
                Rocks = sec.Asteroids.Select(a => new AsteroidSave { Id = a.Id, Pos = a.Position, BaseRadius = a.BaseRadius, Class = a.ClassKey, Ore = a.Ore, MaxOre = a.MaxOre }).ToList(),
            });
        foreach (var g in u.Gates.Values.OrderBy(g => g.Id.Value))
            s.Gates.Add(new GateSave { Id = g.Id.Value, Sector = g.Sector.Value, Pos = g.Position, Target = g.Target.Value, Open = g.Open });
        foreach (var st in u.Stations.Values)
            s.Stations.Add(new StationSave
            {
                Id = st.Id.Value, Type = st.TypeKey, Name = st.Name, Sector = st.Sector.Value, Owner = st.Owner.Value, Pos = st.Position,
                Credits = st.Credits, Essential = st.Essential, BelowLimitSince = st.BelowLimitSince,
                Recipes = st.Production.Select(ToSave).ToList(),
                Market = st.Market.ToDictionary(kv => kv.Key.Value, kv => new StockSave { Amount = kv.Value.Amount, Target = kv.Value.Target, Price = kv.Value.Price }),
            });
        foreach (var sh in u.Ships.Values)
            s.Ships.Add(new ShipSave
            {
                Id = sh.Id.Value, Faction = sh.Faction.Value, Role = sh.Role.ToString(), Capabilities = (int)sh.Capabilities, Controller = sh.Controller.ToString(),
                ModelKey = sh.ModelKey.Length > 0 ? sh.ModelKey : null,
                Slots = sh.Slots.Count > 0 ? sh.Slots.Select(ToSave).ToList() : null,
                Fit = sh.Fit.Count > 0 ? new List<string?>(sh.Fit) : null,
                CargoCapacity = sh.CargoCapacity,
                Credits = sh.Credits, Hull = sh.Hull, Sector = sh.Sector.Value, Pos = sh.Position,
                Order = sh.Order is null ? null : ToSave(sh.Order),
                Phase = sh.Phase.ToString(), MinePhase = sh.MinePhase.ToString(),
                Transit = sh.Transit is null ? null : ToSave(sh.Transit),
                NextThinkTick = sh.NextThinkTick, IdleSince = sh.IdleSince, ShotTimer = sh.ShotTimer, MineAsteroid = sh.MineAsteroid,
                Cargo = sh.Cargo.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                Fuel = sh.Fuel.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
            });
        return s;
    }

    public static Universe FromSave(SaveGame s)
    {
        var u = new Universe
        {
            Tick = s.Tick, Seed = s.Seed, LastStationBuildTick = s.LastStationBuildTick, Rules = s.Rules, NextAsteroidId = s.NextAsteroidId,
            Rng = new DeterministicRng(s.Seed) { State = s.RngState },
            PlayerSector = new SectorId(s.PlayerSector), PlayerShip = new ShipId(s.PlayerShip),
        };
        foreach (var g in s.Goods)
            u.Goods[new GoodId(g.Id)] = new Good { Id = new GoodId(g.Id), Key = g.Key, Name = g.Name, Color = g.Color, BasePrice = g.BasePrice, VolumePerUnit = g.Volume, Form = ParseForm(g.Form) };
        foreach (var c in s.AsteroidClasses)
            u.AsteroidClasses[c.Key] = new AsteroidClass
            {
                Key = c.Key, Name = c.Name, Color = c.Color, ChunkChance = c.ChunkChance, OreFactor = c.OreFactor,
                Yields = c.Yields.Select(y => new Yield(new GoodId(y.Good), y.Weight)).ToList(),
            };
        foreach (var t in s.StationTypes)
            u.StationTypes[t.Key] = new StationType
            {
                Key = t.Key, Name = t.Name, BuildCost = t.BuildCost, UpkeepPerTick = t.Upkeep, IncomePerTick = t.Income,
                Recipes = t.Recipes.Select(FromSave).ToList(),
                // Alte Spielstände ohne das Feld bekommen den Standard ["trade"]
                Services = t.Services is null ? new List<string> { Services.TradeService.ServiceKey } : new List<string>(t.Services),
                Stocks = t.Stocks is null ? new List<StationStock>() : t.Stocks.Select(x => new StationStock(new GoodId(x.Good), x.Amount, x.Target)).ToList(),
            };
        ReadSlotContent(s, u);
        foreach (var f in s.Factions)
            u.Factions[new FactionId(f.Id)] = new Faction
            {
                Id = new FactionId(f.Id), Name = f.Name,
                Relations = f.Relations.ToDictionary(kv => new FactionId(kv.Key), kv => kv.Value),
                Territory = f.Territory.Select(t => new SectorId(t)).ToHashSet(),
            };
        foreach (var sec in s.Sectors)
            u.Sectors[new SectorId(sec.Id)] = new Sector
            {
                Id = new SectorId(sec.Id), Name = sec.Name, Owner = new FactionId(sec.Owner), MapPosition = sec.Map, Radius = sec.Radius,
                Gates = sec.Gates.Select(g => new GateId(g)).ToList(), Stations = sec.Stations.Select(x => new StationId(x)).ToList(),
                Fields = sec.Fields.Select(f => new ResourceField { ClassKey = f.Class, Position = f.Pos, TargetAsteroids = f.TargetAsteroids, RespawnTicks = f.RespawnTicks }).ToList(),
                Asteroids = sec.Rocks.Select(a => new Asteroid { Id = a.Id, Position = a.Pos, BaseRadius = a.BaseRadius, ClassKey = a.Class, Ore = a.Ore, MaxOre = a.MaxOre }).ToList(),
            };
        foreach (var g in s.Gates)
            u.Gates[new GateId(g.Id)] = new Gate { Id = new GateId(g.Id), Sector = new SectorId(g.Sector), Position = g.Pos, Target = new GateId(g.Target), Open = g.Open };
        foreach (var st in s.Stations)
            u.Stations[new StationId(st.Id)] = new Station
            {
                Id = new StationId(st.Id), TypeKey = st.Type, Name = st.Name, Sector = new SectorId(st.Sector), Owner = new FactionId(st.Owner),
                Position = st.Pos, Credits = st.Credits, Essential = st.Essential, BelowLimitSince = st.BelowLimitSince,
                Production = st.Recipes.Select(FromSave).ToList(),
                Market = st.Market.ToDictionary(kv => new GoodId(kv.Key), kv => new StockEntry { Amount = kv.Value.Amount, Target = kv.Value.Target, Price = kv.Value.Price }),
            };
        foreach (var sh in s.Ships)
        {
            var role = Enum.Parse<ShipRole>(sh.Role);
            var ship = new Ship
            {
                Id = new ShipId(sh.Id), Faction = new FactionId(sh.Faction), Role = role,
                // gespeichert; alte Spielstände ohne die Felder: aus der Rolle ableiten
                Controller = sh.Controller is { } ctl ? Enum.Parse<Controller>(ctl) : CapabilityDefaults.ControllerFor(role),
                ModelKey = sh.ModelKey ?? "",
                Slots = sh.Slots is null ? new List<ClassSlot>() : sh.Slots.Select(FromSave).ToList(),
                Fit = sh.Fit is null ? new List<string?>() : new List<string?>(sh.Fit),
                CargoCapacity = sh.CargoCapacity,
                Credits = sh.Credits, Hull = sh.Hull, Sector = new SectorId(sh.Sector), Position = sh.Pos,
                Order = sh.Order is null ? null : FromSave(sh.Order),
                Phase = Enum.Parse<TradePhase>(sh.Phase), MinePhase = Enum.Parse<MinePhase>(sh.MinePhase),
                Transit = sh.Transit is null ? null : FromSave(sh.Transit),
                NextThinkTick = sh.NextThinkTick, IdleSince = sh.IdleSince, ShotTimer = sh.ShotTimer, MineAsteroid = sh.MineAsteroid,
                Cargo = sh.Cargo.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
                Fuel = sh.Fuel.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
            };
            // Modell auflösen: Slots/Fit aus dem Modell füllen, wenn das Save sie nicht enthält (v3-Migration)
            ShipModel? model = ship.HasModel ? u.ShipModels.GetValueOrDefault(ship.ModelKey) : null;
            bool modelOk = false;
            if (model is not null && u.ShipClasses.TryGetValue(model.ClassKey, out var cls))
            {
                modelOk = true;
                if (ship.Slots.Count == 0)
                    foreach (var sl in cls.Slots)
                        ship.Slots.Add(new ClassSlot { Type = sl.Type, Size = sl.Size, Facing = sl.Facing, Arc = sl.Arc });
                if (ship.Fit.Count == 0) ship.Fit = new List<string?>(model.Fit);
            }
            ship.RecomputeStats(u);
            if (modelOk) ship.CargoCapacity = ship.Stats.Cargo;   // Laderaum aus dem Modell
            // Fähigkeiten: mit Modell hat RecomputeStats sie aus dem Fit abgeleitet; ohne Modell gilt der gespeicherte Wert, sonst die Rolle
            if (!modelOk)
                ship.Capabilities = sh.Capabilities is { } caps ? (Capability)caps : CapabilityDefaults.ForRole(role);
            u.Ships[ship.Id] = ship;   // AddShip würde ein ShipSpawned-Ereignis auslösen
        }
        u.IdCounters = (s.NextShipId, s.NextStationId);
        u.RebuildTracker();
        return u;
    }

    static RecipeSave ToSave(Recipe r) => new()
    {
        Ticks = r.TicksPerCycle,
        In = r.Inputs.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
        Out = r.Outputs.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
    };

    static Recipe FromSave(RecipeSave r) => new()
    {
        TicksPerCycle = r.Ticks,
        Inputs = r.In.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
        Outputs = r.Out.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
    };

    static OrderSave ToSave(ShipOrder o) => o switch
    {
        TradeOrder t => new OrderSave { Kind = "trade", A = t.Buy.Value, B = t.Sell.Value, C = t.Good.Value },
        MineOrder m => new OrderSave { Kind = "mine", A = m.Sector.Value, B = m.Field, C = m.DeliverTo.Value },
        PatrolOrder p => new OrderSave { Kind = "patrol", A = p.Sector.Value },
        GoToOrder g => new OrderSave { Kind = "goto", A = g.Target.Value },
        _ => throw new NotSupportedException($"Unbekannter Auftrag {o.GetType().Name}"),
    };

    static ShipOrder FromSave(OrderSave o) => o.Kind switch
    {
        "trade" => new TradeOrder(new StationId(o.A), new StationId(o.B), new GoodId(o.C)),
        "mine" => new MineOrder(new SectorId(o.A), o.B, new StationId(o.C)),
        "patrol" => new PatrolOrder(new SectorId(o.A)),
        "goto" => new GoToOrder(new StationId(o.A)),
        _ => throw new SaveFormatException($"Unbekannte Auftragsart '{o.Kind}'"),
    };

    static TransitSave ToSave(Transit t) => new()
    {
        StartTick = t.StartTick, ArrivalTick = t.ArrivalTick, ToSector = t.ToSector.Value, ToPosition = t.ToPosition,
        Legs = t.Legs.Select(l => new LegSave { Sector = l.Sector.Value, From = l.From, To = l.To, Start = l.StartTick, End = l.EndTick }).ToList(),
    };

    static Transit FromSave(TransitSave t) => new()
    {
        StartTick = t.StartTick, ArrivalTick = t.ArrivalTick, ToSector = new SectorId(t.ToSector), ToPosition = t.ToPosition,
        Legs = t.Legs.Select(l => new Leg(new SectorId(l.Sector), l.From, l.To, l.Start, l.End)).ToList(),
    };

    /// <summary>Warenform aus dem Save; fehlt das Feld (alte Spielstände), ist solid der Standard.</summary>
    static GoodForm ParseForm(string? form) => form?.ToLowerInvariant() switch
    {
        "liquid" => GoodForm.Liquid,
        _ => GoodForm.Solid,
    };
}
