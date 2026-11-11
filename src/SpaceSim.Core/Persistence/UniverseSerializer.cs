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
            .Select(m => new ShipModelSave { Key = m.Key, Class = m.ClassKey, Name = m.Name, Fit = new List<string?>(m.Fit), Jobs = m.Jobs.Count > 0 ? new List<string>(m.Jobs) : null }).ToList();
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
            u.ShipModels[m.Key] = new ShipModel { Key = m.Key, ClassKey = m.Class, Name = m.Name, Fit = new List<string?>(m.Fit), Jobs = m.Jobs is null ? new List<string>() : new List<string>(m.Jobs) };
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
        UpgradeTo = m.UpgradeTo, UpgradeCost = m.UpgradeTo is null ? null : new Dictionary<string, float>(m.UpgradeCost),
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
        UpgradeTo = m.UpgradeTo, UpgradeCost = m.UpgradeCost is null ? new Dictionary<string, float>() : new Dictionary<string, float>(m.UpgradeCost),
    };

    static ShipClassSave ToSave(ShipClass c) => new()
    {
        Key = c.Key, Name = c.Name, Size = c.Size.ToString(), BaseHull = c.BaseHull, BaseMass = c.BaseMass, BaseFuel = c.BaseFuel,
        Slots = c.Slots.Select(ToSave).ToList(), Kit = c.Kit, ToolEfficiency = c.ToolEfficiency, MaxSlotUpgrades = c.MaxSlotUpgrades, Restricted = c.Restricted, BerthFee = c.BerthFee,
    };

    static ShipClass FromSave(ShipClassSave c) => new()
    {
        Key = c.Key, Name = c.Name, Size = Enum.Parse<SlotSize>(c.Size, ignoreCase: true), BaseHull = c.BaseHull, BaseMass = c.BaseMass, BaseFuel = c.BaseFuel,
        Slots = c.Slots.Select(FromSave).ToList(), Kit = c.Kit, ToolEfficiency = c.ToolEfficiency > 0f ? c.ToolEfficiency : 1f, MaxSlotUpgrades = Math.Max(0, c.MaxSlotUpgrades),
        Restricted = c.Restricted, BerthFee = MathF.Max(0f, c.BerthFee),
    };

    static ResearchStateSave? ToSave(ResearchState r) =>
        r.Current is null && r.Progress.Count == 0 && r.Completed.Count == 0 && r.PointsTotal == 0f && r.Spent == 0f && r.PlayerFunded == 0f ? null
        : new ResearchStateSave
        {
            Current = r.Current, Commissioned = r.Commissioned, PointsTotal = r.PointsTotal, Spent = r.Spent, PlayerFunded = r.PlayerFunded,
            Progress = r.Progress.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
            Completed = r.Completed.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
        };

    static ResearchState FromSave(ResearchStateSave? r)
    {
        var state = new ResearchState();
        if (r is null) return state;
        state.Current = r.Current; state.Commissioned = r.Commissioned;
        state.PointsTotal = r.PointsTotal; state.Spent = r.Spent; state.PlayerFunded = r.PlayerFunded;
        foreach (var (k, v) in r.Progress) state.Progress[k] = v;
        foreach (var (k, v) in r.Completed) state.Completed[k] = v;
        return state;
    }

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
            PlayerActor = u.PlayerActor.Value, FreeActor = u.FreeActor.Value, NextActorId = u.NextActorId,
        };
        foreach (var a in u.Actors.Values.OrderBy(a => a.Id.Value))
            s.Actors.Add(new ActorSave
            {
                Id = a.Id.Value, Name = a.Name, Kind = a.Kind.ToString(), Membership = a.Membership?.Value, Credits = a.Treasury.Credits,
                ShipsUseTreasury = a.ShipsUseTreasury, Aggression = a.Personality.Aggression, Greed = a.Personality.Greed,
                Cooperation = a.Personality.Cooperation, Lawfulness = a.Personality.Lawfulness,
                Relations = a.Relations.Count == 0 ? null : a.Relations.OrderBy(kv => kv.Key.Value).ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                Created = a.CreatedTick,
                Wanted = a.Wanted.Count == 0 ? null : a.Wanted.OrderBy(kv => kv.Key.Value)
                    .Select(kv => new WantedSave { Faction = kv.Key.Value, Heat = kv.Value.Heat, LastCrime = kv.Value.LastCrimeTick, Fine = kv.Value.Fine, Crime = kv.Value.LastCrime }).ToList(),
                BountyClaims = a.BountyClaims.Count == 0 ? null : a.BountyClaims.OrderBy(kv => kv.Key.Value).ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                LastForceTick = a.LastForceTick,
            });
        if (u.PendingEncounters.Count > 0)
            s.Encounters = u.PendingEncounters.Select(e => new PendingEncounterSave
            {
                Kind = e.Kind.ToString(), Source = e.Source.Value, Target = e.Target.Value, Sector = e.Sector.Value, Due = e.DueTick,
            }).ToList();
        foreach (var g in u.Goods.Values.OrderBy(g => g.Id.Value))
            s.Goods.Add(new GoodSave { Id = g.Id.Value, Key = g.Key, Name = g.Name, Color = g.Color, BasePrice = g.BasePrice, Volume = g.VolumePerUnit, Form = g.Form.ToString().ToLowerInvariant(), Category = g.Category.Length > 0 ? g.Category : null });
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
                ShipDealer = t.ShipDealer is { } sd
                    ? new ShipDealerSave { Classes = sd.Classes is null ? null : new List<string>(sd.Classes), MaxSize = sd.MaxSize.ToString(), Assembly = sd.Assembly }
                    : null,
                Construction = t.Construction?.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                Government = Content.WorldBuilder.GovernmentKey(t.Government),
                Influence = t.Influence == 1f ? null : t.Influence,
                Capturable = t.Capturable ? null : false,
                Destructible = t.Destructible ? null : false,
                Security = t.Security == 0f ? null : t.Security,
                Hideout = t.Hideout ? true : null,
                Research = t.Research is { } rc ? new ResearchConfigSave
                {
                    Ticks = rc.Ticks, Points = rc.Points, Credits = rc.Credits, FundTarget = rc.FundTarget, FundPerTick = rc.FundPerTick,
                    Inputs = rc.Inputs.OrderBy(kv => kv.Key.Value).ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                } : null,
                Export = t.Export is { } ex ? new ExportConfigSave
                {
                    Goods = ex.Goods.Select(g => g.Value).ToList(), Categories = new List<string>(ex.Categories), DelayTicks = ex.DelayTicks, Markup = ex.Markup, Target = ex.Target, TargetValue = ex.TargetValue,
                    Bids = ex.Bids.Count == 0 ? null : ex.Bids.OrderBy(kv => kv.Key.Value).ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                } : null,
            });
        WriteSlotContent(u, s);
        foreach (var f in u.Factions.Values.OrderBy(f => f.Id.Value))
            s.Factions.Add(new FactionSave
            {
                Id = f.Id.Value, Name = f.Name,
                Relations = f.Relations.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                Territory = f.Territory.Select(t => t.Value).OrderBy(x => x).ToList(),
                LastEmergencyBuildTick = f.LastEmergencyBuildTick,
                Research = ToSave(f.Research),
            });
        // Phase 13: Forschungsprojekte (Content)
        if (u.Research.Count > 0)
            s.Research = u.Research.Projects.Select(p => new ResearchProjectSave
            {
                Key = p.Key, Name = p.Name, Description = p.Description, Points = p.Points, Requires = new List<string>(p.Requires),
                Unlocks = p.Unlocks.Select(x => x.ToString()).ToList(), Inputs = p.Inputs.OrderBy(kv => kv.Key.Value).ToDictionary(kv => kv.Key.Value, kv => kv.Value),
            }).ToList();
        foreach (var sec in u.Sectors.Values.OrderBy(x => x.Id.Value))
            s.Sectors.Add(new SectorSave
            {
                Id = sec.Id.Value, Name = sec.Name, Owner = sec.Owner.Value, Map = sec.MapPosition, Radius = sec.Radius,
                Gates = sec.Gates.Select(g => g.Value).ToList(), Stations = sec.Stations.Select(x => x.Value).ToList(),
                Fields = sec.Fields.Select(f => new FieldSave { Class = f.ClassKey, Pos = f.Position, TargetAsteroids = f.TargetAsteroids, RespawnTicks = f.RespawnTicks }).ToList(),
                Rocks = sec.Asteroids.Select(a => new AsteroidSave { Id = a.Id, Pos = a.Position, BaseRadius = a.BaseRadius, Class = a.ClassKey, Ore = a.Ore, MaxOre = a.MaxOre }).ToList(),
                Access = ToSave(sec.Access),
                Influence = sec.Influence.Count == 0 ? null : sec.Influence.OrderBy(kv => kv.Key.Value).ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                Control = sec.Control == ControlStage.None ? null : sec.Control.ToString(),
                Contested = sec.Contested,
                Threat = sec.Threat, Security = sec.Security, Traffic = sec.Traffic,
                Planet = sec.Planet is { } pl ? new PlanetSave { Name = pl.Name, Color = pl.Color, Radius = pl.Radius } : null,
            });
        foreach (var g in u.Gates.Values.OrderBy(g => g.Id.Value))
            s.Gates.Add(new GateSave
            {
                Id = g.Id.Value, Sector = g.Sector.Value, Pos = g.Position, Target = g.Target.Value, Open = g.Open,
                State = g.State == GateState.Open ? null : g.State.ToString(), MainRoute = g.MainRoute,
            });
        foreach (var st in u.Stations.Values)
            s.Stations.Add(new StationSave
            {
                Id = st.Id.Value, Type = st.TypeKey, Name = st.Name, Sector = st.Sector.Value, Owner = st.Owner.Value, Pos = st.Position,
                Credits = st.Credits, BankruptcyProtected = st.BankruptcyProtected, BelowLimitSince = st.BelowLimitSince, Immortal = st.Immortal,
                Recipes = st.Production.Select(ToSave).ToList(),
                Market = st.Market.ToDictionary(kv => kv.Key.Value, kv => new StockSave { Amount = kv.Value.Amount, Target = kv.Value.Target, Price = kv.Value.Price }),
                Site = st.Site is { } bs ? new BuildSiteSave
                {
                    Type = bs.TypeKey, Required = bs.Required.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                    InvestorStation = bs.InvestorStation, InvestorShip = bs.InvestorShip, InvestorActor = bs.InvestorActor, Budget = bs.Budget, Builder = bs.Builder.Value,
                    Started = bs.StartedTick, LastProgress = bs.LastProgressTick, BlockedSince = bs.BlockedSince, Assembly = bs.AssemblyTicks,
                    Emergency = bs.Emergency, Abandoned = bs.Abandoned,
                } : null,
                PlayerStorage = st.PlayerStorage.Count == 0 ? null : st.PlayerStorage.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                Exports = st.Exports.Count == 0 ? null : st.Exports.Select(b => new ExportBatchSave { Good = b.Good.Value, Amount = b.Amount, Paid = b.Paid, Due = b.DueTick }).ToList(),
                MaintenanceBacklog = st.MaintenanceBacklog.Count == 0 ? null : st.MaintenanceBacklog.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
            });
        foreach (var sh in u.Ships.Values)
            s.Ships.Add(new ShipSave
            {
                Id = sh.Id.Value, Owner = sh.Owner.Value, Role = sh.Role.ToString(), Capabilities = (int)sh.Capabilities, Controller = sh.Controller.ToString(),
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
                JobSince = sh.JobSince,
                Quality = sh.Quality == ShipQuality.Standard ? null : sh.Quality.ToString(),
                UpgradePity = sh.UpgradePity.Count == 0 ? null : new Dictionary<int, int>(sh.UpgradePity),
                HomeBase = sh.HomeBase.Value, DutySector = sh.DutySector.Value,
                Command = sh.Command is null ? null : ToSave(sh.Command), CommandSince = sh.CommandSince, CommandStep = sh.CommandStep, Emergency = sh.Emergency,
                Engagement = sh.Engagement == Engagement.Defend ? null : sh.Engagement.ToString(), Squadron = sh.Squadron.Value, DockedAt = sh.DockedAt.Value,
                RentedFrom = sh.RentedFrom.Value, RentedSince = sh.RentedSince, RentalDeposit = sh.RentalDeposit,
            });
        if (u.Squadrons.Count > 0)
            s.Squadrons = u.Squadrons.Values.OrderBy(q => q.Id.Value).Select(q => new SquadronSave
            {
                Id = q.Id.Value, Owner = q.Owner.Value, Name = q.Name, Leader = q.Leader.Value, Members = q.Members.Select(m => m.Value).ToList(),
                Formation = q.Formation.ToString(), HomeBase = q.HomeBase.Value, Rules = q.Rules.ToString(),
            }).ToList();
        s.NextSquadronId = u.NextSquadronId;
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
        // Phase 9: Akteure (die Migration v11→v12 legt sie für ältere Stände an)
        u.Actors.Clear();
        u.PlayerActor = new ActorId(s.PlayerActor);
        u.FreeActor = new ActorId(s.FreeActor);
        foreach (var a in s.Actors)
            u.Actors[new ActorId(a.Id)] = new Actor
            {
                Id = new ActorId(a.Id), Name = a.Name, Kind = Enum.Parse<ActorKind>(a.Kind), Membership = a.Membership is { } m ? new FactionId(m) : null,
                Treasury = new Treasury { Credits = a.Credits }, ShipsUseTreasury = a.ShipsUseTreasury,
                Personality = new Personality(a.Aggression, a.Greed, a.Cooperation, a.Lawfulness),
                Relations = a.Relations is null ? new Dictionary<ActorId, float>() : a.Relations.ToDictionary(kv => new ActorId(kv.Key), kv => kv.Value),
                CreatedTick = a.Created,
                Wanted = a.Wanted is null ? new Dictionary<FactionId, WantedRecord>()
                    : a.Wanted.ToDictionary(w => new FactionId(w.Faction), w => new WantedRecord { Heat = w.Heat, LastCrimeTick = w.LastCrime, Fine = w.Fine, LastCrime = w.Crime }),
                BountyClaims = a.BountyClaims is null ? new Dictionary<FactionId, float>() : a.BountyClaims.ToDictionary(kv => new FactionId(kv.Key), kv => kv.Value),
                LastForceTick = a.LastForceTick,
            };
        if (s.Encounters is not null)
            foreach (var e in s.Encounters)
                u.PendingEncounters.Add(new PendingEncounter
                {
                    Kind = Enum.Parse<EncounterKind>(e.Kind), Source = new ShipId(e.Source), Target = new ShipId(e.Target), Sector = new SectorId(e.Sector), DueTick = e.Due,
                });
        u.NextActorId = s.NextActorId;
        u.EnsureCoreActors();
        foreach (var g in s.Goods)
            u.Goods[new GoodId(g.Id)] = new Good { Id = new GoodId(g.Id), Key = g.Key, Name = g.Name, Color = g.Color, BasePrice = g.BasePrice, VolumePerUnit = g.Volume, Form = ParseForm(g.Form), Category = g.Category ?? "" };
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
                // Phase 5c: Schiffshändler (fehlt in älteren Ständen = keiner; die Migration v7→v8 ergänzt die Werft)
                ShipDealer = t.ShipDealer is { } sd
                    ? new ShipDealerConfig { Classes = sd.Classes is null ? null : new List<string>(sd.Classes), MaxSize = Enum.Parse<SlotSize>(sd.MaxSize, ignoreCase: true), Assembly = sd.Assembly }
                    : null,
                Construction = t.Construction?.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
                // Phase 10: fehlt in älteren Ständen = keine Regierungsform, Einfluss 1, eroberbar und zerstörbar
                Government = Content.WorldBuilder.ParseGovernment(t.Government, t.Key),
                Influence = t.Influence ?? 1f,
                Capturable = t.Capturable ?? true,
                Destructible = t.Destructible ?? true,
                // Phase 11: fehlt in älteren Ständen = kein Grundschutz, kein Versteck
                Security = t.Security ?? 0f,
                Hideout = t.Hideout ?? false,
                // Phase 13: fehlt in älteren Ständen = keine Forschungsstation
                Research = t.Research is { } rc ? new ResearchConfig
                {
                    Ticks = rc.Ticks, Points = rc.Points, Credits = rc.Credits, FundTarget = rc.FundTarget, FundPerTick = rc.FundPerTick,
                    Inputs = rc.Inputs.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
                } : null,
                // Phase 14: fehlt = keine Exportsenke
                Export = t.Export is { } ex ? new ExportConfig
                {
                    Goods = ex.Goods.Select(g => new GoodId(g)).ToList(), Categories = new List<string>(ex.Categories), DelayTicks = ex.DelayTicks, Markup = ex.Markup, Target = ex.Target, TargetValue = ex.TargetValue,
                    Bids = ex.Bids is null ? new Dictionary<GoodId, float>() : ex.Bids.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
                } : null,
            };
        ReadSlotContent(s, u);
        foreach (var f in s.Factions)
            u.Factions[new FactionId(f.Id)] = new Faction
            {
                Id = new FactionId(f.Id), Name = f.Name,
                Relations = f.Relations.ToDictionary(kv => new FactionId(kv.Key), kv => kv.Value),
                Territory = f.Territory.Select(t => new SectorId(t)).ToHashSet(),
                LastEmergencyBuildTick = f.LastEmergencyBuildTick,
                Research = FromSave(f.Research),
            };
        // Phase 13: Forschungsprojekte (fehlt = keine Forschung, alles frei)
        foreach (var p in s.Research ?? new List<ResearchProjectSave>())
        {
            var project = new ResearchProject
            {
                Key = p.Key, Name = p.Name, Description = p.Description, Points = p.Points, Requires = new List<string>(p.Requires),
                Inputs = p.Inputs.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
            };
            foreach (var text in p.Unlocks)
                project.Unlocks.Add(ResearchUnlock.TryParse(text, out var x) ? x : throw new SaveFormatException($"Forschungsprojekt '{p.Key}': Freischaltung '{text}' unlesbar"));
            u.Research.Add(project);
        }
        foreach (var sec in s.Sectors)
            u.Sectors[new SectorId(sec.Id)] = new Sector
            {
                Id = new SectorId(sec.Id), Name = sec.Name, Owner = new FactionId(sec.Owner), MapPosition = sec.Map, Radius = sec.Radius,
                Gates = sec.Gates.Select(g => new GateId(g)).ToList(), Stations = sec.Stations.Select(x => new StationId(x)).ToList(),
                Fields = sec.Fields.Select(f => new ResourceField { ClassKey = f.Class, Position = f.Pos, TargetAsteroids = f.TargetAsteroids, RespawnTicks = f.RespawnTicks }).ToList(),
                Asteroids = sec.Rocks.Select(a => new Asteroid { Id = a.Id, Position = a.Pos, BaseRadius = a.BaseRadius, ClassKey = a.Class, Ore = a.Ore, MaxOre = a.MaxOre }).ToList(),
                Access = FromSave(sec.Access),
                Influence = sec.Influence is null ? new Dictionary<FactionId, float>() : sec.Influence.ToDictionary(kv => new FactionId(kv.Key), kv => kv.Value),
                Control = sec.Control is { } ctl ? Enum.Parse<ControlStage>(ctl) : ControlStage.None,
                Contested = sec.Contested,
                Threat = sec.Threat, Security = sec.Security, Traffic = sec.Traffic,
                Planet = sec.Planet is { } pl ? new Planet { Name = pl.Name, Color = pl.Color, Radius = pl.Radius } : null,
            };
        foreach (var g in s.Gates)
            u.Gates[new GateId(g.Id)] = new Gate
            {
                Id = new GateId(g.Id), Sector = new SectorId(g.Sector), Position = g.Pos, Target = new GateId(g.Target),
                // Phase 10: Zustand; ältere Stände kennen nur Open
                State = g.State is { } gs ? Enum.Parse<GateState>(gs) : g.Open ? GateState.Open : GateState.Closed,
                MainRoute = g.MainRoute,
            };
        foreach (var st in s.Stations)
            u.Stations[new StationId(st.Id)] = new Station
            {
                Id = new StationId(st.Id), TypeKey = st.Type, Name = st.Name, Sector = new SectorId(st.Sector), Owner = new ActorId(st.Owner),
                Position = st.Pos, Credits = st.Credits, BankruptcyProtected = st.BankruptcyProtected || st.Essential, BelowLimitSince = st.BelowLimitSince,
                Immortal = st.Immortal,
                Production = st.Recipes.Select(FromSave).ToList(),
                Market = st.Market.ToDictionary(kv => new GoodId(kv.Key), kv => new StockEntry { Amount = kv.Value.Amount, Target = kv.Value.Target, Price = kv.Value.Price }),
                Site = st.Site is { } bs ? new BuildSite
                {
                    TypeKey = bs.Type, Required = bs.Required.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
                    InvestorStation = bs.InvestorStation, InvestorShip = bs.InvestorShip, InvestorActor = bs.InvestorActor, Budget = bs.Budget, Builder = new ShipId(bs.Builder),
                    StartedTick = bs.Started, LastProgressTick = bs.LastProgress, BlockedSince = bs.BlockedSince, AssemblyTicks = bs.Assembly,
                    Emergency = bs.Emergency, Abandoned = bs.Abandoned,
                } : null,
                PlayerStorage = st.PlayerStorage is null ? new Dictionary<GoodId, float>() : st.PlayerStorage.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
                Exports = st.Exports is null ? new List<ExportBatch>() : st.Exports.Select(b => new ExportBatch { Good = new GoodId(b.Good), Amount = b.Amount, Paid = b.Paid, DueTick = b.Due }).ToList(),
                MaintenanceBacklog = st.MaintenanceBacklog is null ? new Dictionary<GoodId, float>() : st.MaintenanceBacklog.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
            };
        foreach (var sh in s.Ships)
        {
            var role = Enum.Parse<ShipRole>(sh.Role);
            var ship = new Ship
            {
                Id = new ShipId(sh.Id), Owner = new ActorId(sh.Owner ?? sh.Faction), Role = role,
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
                JobSince = sh.JobSince,
                Quality = sh.Quality is { } ql ? Enum.Parse<ShipQuality>(ql) : ShipQuality.Standard,
                UpgradePity = sh.UpgradePity is null ? new Dictionary<int, int>() : new Dictionary<int, int>(sh.UpgradePity),
                HomeBase = new StationId(sh.HomeBase), DutySector = new SectorId(sh.DutySector),
                Command = sh.Command is null ? null : CommandFromSave(sh.Command), CommandSince = sh.CommandSince, CommandStep = sh.CommandStep, Emergency = sh.Emergency,
                Engagement = sh.Engagement is { } eg ? Enum.Parse<Engagement>(eg) : Engagement.Defend, Squadron = new SquadronId(sh.Squadron),
                DockedAt = new StationId(sh.DockedAt), RentedFrom = new StationId(sh.RentedFrom), RentedSince = sh.RentedSince, RentalDeposit = sh.RentalDeposit,
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
        // Phase 12: Verbände
        if (s.Squadrons is not null)
            foreach (var q in s.Squadrons)
                u.Squadrons[new SquadronId(q.Id)] = new Squadron
                {
                    Id = new SquadronId(q.Id), Owner = new ActorId(q.Owner), Name = q.Name, Leader = new ShipId(q.Leader),
                    Members = q.Members.Select(m => new ShipId(m)).ToList(),
                    Formation = Enum.TryParse<Formation>(q.Formation, out var f) ? f : Formation.Line, HomeBase = new StationId(q.HomeBase),
                    Rules = Enum.TryParse<Engagement>(q.Rules, out var e) ? e : Engagement.Defend,
                };
        u.NextSquadronId = Math.Max(s.NextSquadronId, u.Squadrons.Count == 0 ? 1 : u.Squadrons.Keys.Max(k => k.Value) + 1);
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

    /// <summary>Phase 12: Befehl ins erweiterte <see cref="OrderSave"/> (Art + Ganzzahlen, Listen für Streifenrunde und Kommazahlen).</summary>
    static OrderSave ToSave(ShipCommand c) => c switch
    {
        GoToCommand g => new OrderSave { Kind = CommandKinds.GoTo, A = g.Sector.Value, B = g.Station.Value, Values = new List<float> { g.Position.X, g.Position.Y } },
        HoldCommand => new OrderSave { Kind = CommandKinds.Hold },
        PatrolCommand p => new OrderSave { Kind = CommandKinds.Patrol, Ids = p.Sectors.Select(x => x.Value).ToList() },
        GuardCommand g => new OrderSave { Kind = CommandKinds.Guard, A = g.Station.Value },
        EscortCommand e => new OrderSave { Kind = CommandKinds.Escort, A = e.Target.Value },
        FollowCommand f => new OrderSave { Kind = CommandKinds.Follow, A = f.Target.Value },
        TradeCommand t => LimitsSave(CommandKinds.Trade, t.Limits),
        MineCommand m => LimitsSave(CommandKinds.Mine, m.Limits),
        BuildCommand b => new OrderSave { Kind = CommandKinds.Build, A = b.Site.Value },
        ReturnCommand => new OrderSave { Kind = CommandKinds.Return },
        _ => throw new InvalidOperationException($"Unbekannter Befehl {c.GetType().Name}"),
    };

    static OrderSave LimitsSave(string kind, AutoLimits l) =>
        new() { Kind = kind, A = l.Center.Value, B = l.RadiusJumps, Values = new List<float> { l.MaxBuyFactor, l.Reserve } };

    static AutoLimits LimitsFrom(OrderSave o) =>
        new(new SectorId(o.A), o.B, o.Values is { Count: > 0 } v ? v[0] : 0f, o.Values is { Count: > 1 } w ? w[1] : 0f);

    static ShipCommand CommandFromSave(OrderSave o) => o.Kind switch
    {
        CommandKinds.GoTo => new GoToCommand(new SectorId(o.A), new Vec2(o.Values?.ElementAtOrDefault(0) ?? 0f, o.Values?.ElementAtOrDefault(1) ?? 0f), new StationId(o.B)),
        CommandKinds.Hold => new HoldCommand(),
        CommandKinds.Patrol => new PatrolCommand((o.Ids ?? new List<int>()).Select(x => new SectorId(x)).ToList()),
        CommandKinds.Guard => new GuardCommand(new StationId(o.A)),
        CommandKinds.Escort => new EscortCommand(new ShipId(o.A)),
        CommandKinds.Follow => new FollowCommand(new ShipId(o.A)),
        CommandKinds.Trade => new TradeCommand(LimitsFrom(o)),
        CommandKinds.Mine => new MineCommand(LimitsFrom(o)),
        CommandKinds.Build => new BuildCommand(new StationId(o.A)),
        CommandKinds.Return => new ReturnCommand(),
        _ => throw new FormatException($"Unbekannter Befehl '{o.Kind}'"),
    };

    static OrderSave ToSave(ShipOrder o) => o switch
    {
        TradeOrder t => new OrderSave { Kind = "trade", A = t.Buy.Value, B = t.Sell.Value, C = t.Good.Value },
        MineOrder m => new OrderSave { Kind = "mine", A = m.Sector.Value, B = m.Field, C = m.DeliverTo.Value },
        PatrolOrder p => new OrderSave { Kind = "patrol", A = p.Sector.Value },
        GoToOrder g => new OrderSave { Kind = "goto", A = g.Target.Value },
        BuildOrder b => new OrderSave { Kind = "build", A = b.Site.Value, B = b.Supplier.Value, C = b.Good.Value },
        _ => throw new NotSupportedException($"Unbekannter Auftrag {o.GetType().Name}"),
    };

    static ShipOrder FromSave(OrderSave o) => o.Kind switch
    {
        "trade" => new TradeOrder(new StationId(o.A), new StationId(o.B), new GoodId(o.C)),
        "mine" => new MineOrder(new SectorId(o.A), o.B, new StationId(o.C)),
        "patrol" => new PatrolOrder(new SectorId(o.A)),
        "goto" => new GoToOrder(new StationId(o.A)),
        "build" => new BuildOrder(new StationId(o.A), new StationId(o.B), new GoodId(o.C)),
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

    /// <summary>Sperre in den Spielstand (null, wenn offen und ohne Zeitstempel — dann gilt der Standard).</summary>
    static SectorAccessSave? ToSave(SectorAccess a)
    {
        var d = new SectorAccess();
        if (a.EntryLevel == 0 && a.ExitLevel == 0 && a.Against is null && a.SinceTick == d.SinceTick && a.ExpiresTick == d.ExpiresTick
            && a.CooldownUntil == d.CooldownUntil && a.Enforced) return null;
        return new SectorAccessSave
        {
            Entry = a.EntryLevel, Exit = a.ExitLevel, Against = a.Against?.Value, Since = a.SinceTick, Expires = a.ExpiresTick,
            Cooldown = a.CooldownUntil, Enforced = a.Enforced,
        };
    }

    static SectorAccess FromSave(SectorAccessSave? a) => a is null ? new SectorAccess() : new SectorAccess
    {
        EntryLevel = Math.Clamp(a.Entry, 0, 5), ExitLevel = Math.Clamp(a.Exit, 0, 5), Against = a.Against is { } f ? new FactionId(f) : null,
        SinceTick = a.Since, ExpiresTick = a.Expires, CooldownUntil = a.Cooldown, Enforced = a.Enforced,
    };

    /// <summary>Warenform aus dem Save; fehlt das Feld (alte Spielstände), ist solid der Standard.</summary>
    static GoodForm ParseForm(string? form) => form?.ToLowerInvariant() switch
    {
        "liquid" => GoodForm.Liquid,
        _ => GoodForm.Solid,
    };
}
