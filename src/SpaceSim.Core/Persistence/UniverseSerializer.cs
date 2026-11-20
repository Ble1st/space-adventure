using SpaceSim.Core.Content;
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

    /// <summary>Phase 22: Rentabilitätsmessung speichern (null = noch nicht gemessen).</summary>
    static ProfitMeterSave? ToSave(ProfitMeter m) => m.Since < 0 ? null : new() { Rate = m.Rate, Worth = m.Worth, Loss = m.LossTicks, Since = m.Since };
    // Phase 23: Bordsysteme (nichts gespeichert, solange alles auf Standard steht)
    static ShipSystemsSave? ToSave(ShipSystemsState st) => st.IsDefault ? null : new()
    {
        ShieldMissing = st.ShieldMissing, ChargeMissing = st.ChargeMissing, AmmoMissing = st.AmmoMissing, MissilesMissing = st.MissilesMissing,
        Condition = st.Condition.Count == 0 || st.AllIntact ? null : new List<float>(st.Condition),
        Mode = st.Mode == PowerMode.Balanced ? null : st.Mode.ToString(), ShieldPauseUntil = st.ShieldPauseUntil,
        Burning = st.Burning.Count == 0 ? null : new List<int>(st.Burning),
    };
    static ShipSystemsState FromSave(ShipSystemsSave? s) => s is null ? new ShipSystemsState() : new ShipSystemsState
    {
        ShieldMissing = s.ShieldMissing, ChargeMissing = s.ChargeMissing, AmmoMissing = s.AmmoMissing, MissilesMissing = s.MissilesMissing,
        Condition = s.Condition is null ? new List<float>() : new List<float>(s.Condition),
        Mode = s.Mode is { } m && Enum.TryParse<PowerMode>(m, out var pm) ? pm : PowerMode.Balanced, ShieldPauseUntil = s.ShieldPauseUntil,
        Burning = s.Burning is null ? new List<int>() : new List<int>(s.Burning),
    };
    static ProfitMeter FromSave(ProfitMeterSave? m) => m is null ? new ProfitMeter() : new ProfitMeter { Rate = m.Rate, Worth = m.Worth, LossTicks = m.Loss, Since = m.Since };

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
        DamageType = m.IsWeapon ? m.DamageType.ToString().ToLowerInvariant() : null,
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
        DamageType = m.DamageType is { } dt && Enum.TryParse<DamageType>(dt, ignoreCase: true, out var d) ? d : DamageType.Energy,
    };

    static ShipClassSave ToSave(ShipClass c) => new()
    {
        Key = c.Key, Name = c.Name, Size = c.Size.ToString(), BaseHull = c.BaseHull, BaseMass = c.BaseMass, BaseFuel = c.BaseFuel,
        Slots = c.Slots.Select(ToSave).ToList(), Kit = c.Kit, KitCount = c.KitCount == 1 ? 0 : c.KitCount, ToolEfficiency = c.ToolEfficiency, MaxSlotUpgrades = c.MaxSlotUpgrades, Restricted = c.Restricted, BerthFee = c.BerthFee,
    };

    static ShipClass FromSave(ShipClassSave c) => new()
    {
        Key = c.Key, Name = c.Name, Size = Enum.Parse<SlotSize>(c.Size, ignoreCase: true), BaseHull = c.BaseHull, BaseMass = c.BaseMass, BaseFuel = c.BaseFuel,
        Slots = c.Slots.Select(FromSave).ToList(), Kit = c.Kit, KitCount = c.KitCount > 0 ? c.KitCount : 1, ToolEfficiency = c.ToolEfficiency > 0f ? c.ToolEfficiency : 1f, MaxSlotUpgrades = Math.Max(0, c.MaxSlotUpgrades),
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
            LastStationRetireTick = u.LastStationRetireTick, ScarceSince = u.ScarceSince.Count == 0 ? null : u.ScarceSince.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
            PanicSince = u.PanicSince.Count == 0 ? null : u.PanicSince.OrderBy(kv => kv.Key.Part).ThenBy(kv => kv.Key.Good.Value).Select(kv => new long[] { kv.Key.Part, kv.Key.Good.Value, kv.Value }).ToList(),
            IdleRetiredTick = u.IdleRetiredTick.Count == 0 ? null : u.IdleRetiredTick.OrderBy(kv => kv.Key.Job, StringComparer.Ordinal).ThenBy(kv => kv.Key.Part).ToDictionary(kv => $"{kv.Key.Job}@{kv.Key.Part}", kv => kv.Value),
            NextShipId = u.IdCounters.ship, NextStationId = u.IdCounters.station, NextAsteroidId = u.NextAsteroidId,
            PlayerSector = u.PlayerSector.Value, PlayerShip = u.PlayerShip.Value, Rules = u.Rules,
            PlayerActor = u.PlayerActor.Value, FreeActor = u.FreeActor.Value, NextActorId = u.NextActorId,
        };
        foreach (var a in u.Actors.Values.OrderBy(a => a.Id.Value))
            s.Actors.Add(new ActorSave
            {
                Id = a.Id.Value, Name = a.Name, Tag = a.Tag.Length > 0 ? a.Tag : null, Kind = a.Kind.ToString(), Membership = a.Membership?.Value, Credits = a.Treasury.Credits,
                ShipsUseTreasury = a.ShipsUseTreasury, Aggression = a.Personality.Aggression, Greed = a.Personality.Greed,
                Cooperation = a.Personality.Cooperation, Lawfulness = a.Personality.Lawfulness,
                Relations = a.Relations.Count == 0 ? null : a.Relations.OrderBy(kv => kv.Key.Value).ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                Created = a.CreatedTick,
                Wanted = a.Wanted.Count == 0 ? null : a.Wanted.OrderBy(kv => kv.Key.Value)
                    .Select(kv => new WantedSave { Faction = kv.Key.Value, Heat = kv.Value.Heat, LastCrime = kv.Value.LastCrimeTick, Fine = kv.Value.Fine, Crime = kv.Value.LastCrime }).ToList(),
                BountyClaims = a.BountyClaims.Count == 0 ? null : a.BountyClaims.OrderBy(kv => kv.Key.Value).ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                LastForceTick = a.LastForceTick, PoliceNeed = a.PoliceNeed, PoliceSurplusSince = a.PoliceSurplusSince, LastRestoreTick = a.LastRestoreTick, LastOutpostTick = a.LastOutpostTick,
                ArrestCooldownUntil = a.ArrestCooldownUntil,
                CrimeStanding = a.CrimeStanding.Count == 0 ? null : a.CrimeStanding.OrderBy(kv => kv.Key.Value).Select(kv => new CrimeStandingSave
                {
                    Faction = kv.Key.Value, Debt = kv.Value.Debt, Base = kv.Value.Base, WindowStart = kv.Value.WindowStart, WindowLoss = kv.Value.WindowLoss,
                    Reason = kv.Value.LastReason, LastTick = kv.Value.LastTick, LastLoss = kv.Value.LastLoss,
                }).ToList(),
                Intel = IntelCodec.ToSave(a.Intel),
            });
        if (u.PendingEncounters.Count > 0)
            s.Encounters = u.PendingEncounters.Select(e => new PendingEncounterSave
            {
                Kind = e.Kind.ToString(), Source = e.Source.Value, Target = e.Target.Value, Sector = e.Sector.Value, Due = e.DueTick, Station = e.Station.Value,
            }).ToList();
        // Runde 30: Rückkauf-Posten (sortiert, nur wenn vorhanden)
        if (u.Ledger.Count > 0)
            s.Ledger = u.Ledger.OrderBy(kv => kv.Key.Actor).ThenBy(kv => kv.Key.Station).ThenBy(kv => kv.Key.Good).Select(kv => new LedgerSave
            {
                Actor = kv.Key.Actor, Station = kv.Key.Station, Good = kv.Key.Good, BoughtQty = kv.Value.BoughtQty, BoughtPrice = kv.Value.BoughtPrice,
                BoughtStockBefore = kv.Value.BoughtStockBefore, SoldQty = kv.Value.SoldQty, SoldPrice = kv.Value.SoldPrice, SoldStockBefore = kv.Value.SoldStockBefore,
                BoughtOpen = kv.Value.BoughtOpen, SoldOpen = kv.Value.SoldOpen, LastTick = kv.Value.LastTick,   // Runde 32b
                MissingQty = kv.Value.MissingQty, MissingSeen = kv.Value.MissingSeen,   // Runde 36
            }).ToList();
        // Phase 26: Wracks und Frachtbehälter
        if (u.NextWreckId != 1) s.NextWreckId = u.NextWreckId;
        if (u.Wrecks.Count > 0)
            s.Wrecks = u.Wrecks.Select(w => new WreckSave
            {
                Id = w.Id, Kind = w.Kind.ToString(), Sector = w.Sector.Value, X = w.Position.X, Y = w.Position.Y, Name = w.Name, From = w.From.Value,
                Created = w.CreatedTick, Expires = w.ExpiresTick,
                Cargo = w.Cargo.Count == 0 ? null : w.Cargo.OrderBy(k => k.Key.Value).ToDictionary(k => k.Key.Value, k => k.Value),
                Modules = w.Modules.Count == 0 ? null : w.Modules.Select(m => new WreckModuleSave { Key = m.Key, Condition = m.Condition }).ToList(),
            }).ToList();
        // Phase 28: Kriege und Friedensschlüsse
        if (u.Wars.Count > 0)
            s.Wars = u.Wars.Select(w => new WarSave { A = w.A.Value, B = w.B.Value, Since = w.Since, Weariness = w.Weariness, LossA = w.LossA, LossB = w.LossB, Sieges = w.Sieges, Blockades = w.Blockades, CeasefireUntil = w.CeasefireUntil, LastOfferTick = w.LastOfferTick, TributePaid = w.TributePaid, Join = w.Join, ShipsLostA = w.ShipsLostA, ShipsLostB = w.ShipsLostB }).ToList();
        if (u.PeaceSince.Count > 0)
            s.PeaceSince = u.PeaceSince.OrderBy(kv => kv.Key.A).ThenBy(kv => kv.Key.B).Select(kv => new[] { (long)kv.Key.A, kv.Key.B, kv.Value }).ToList();
        if (u.NavalEnded.Count > 0)   // Nachtrag 2b
            s.NavalEnded = u.NavalEnded.OrderBy(kv => kv.Key.Sector).ThenBy(kv => kv.Key.A).ThenBy(kv => kv.Key.B).Select(kv => new[] { (long)kv.Key.Sector, kv.Key.A, kv.Key.B, kv.Value }).ToList();
        if (u.Alliances.Count > 0)   // Runde 45
            s.Alliances = u.Alliances.Select(al => new[] { (long)al.A.Value, al.B.Value, al.Since }).ToList();
        if (u.AllianceEnded.Count > 0)
            s.AllianceEnded = u.AllianceEnded.OrderBy(kv => kv.Key.A).ThenBy(kv => kv.Key.B).Select(kv => new[] { (long)kv.Key.A, kv.Key.B, kv.Value }).ToList();
        // Runde 40: Posteingang (gespeichert, nicht gehasht)
        if (u.Inbox.Count > 0)
            s.Inbox = u.Inbox.Select(m => new CommsMessageSave
            {
                Id = m.Id, From = m.From.Value, Kind = m.Kind,
                Args = m.Args.Count == 0 ? null : m.Args.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
                Created = m.Created, Expires = m.Expires, Read = m.Read,
            }).ToList();
        if (u.NextMessageId != 1) s.NextMessageId = u.NextMessageId;
        // Runde 41: Auftragsangebote (gespeichert, nicht gehasht) und angenommene Aufträge (gehasht)
        if (u.MissionOffers.Count > 0)
            s.MissionOffers = u.MissionOffers.OrderBy(o => o.Id).Select(o => new MissionOfferSave
            {
                Id = o.Id, Type = o.Type, Client = o.Client.Value, At = o.At.Value,
                Args = o.Args.Count == 0 ? null : o.Args.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
                Reward = o.Reward, Deposit = o.Deposit, Expires = o.Expires, Created = o.Created, Haggled = o.Haggled,
            }).ToList();
        if (u.Missions.Count > 0)
            s.Missions = u.Missions.OrderBy(m => m.Id).Select(m => new MissionSave
            {
                Id = m.Id, Type = m.Type, Client = m.Client.Value, At = m.At.Value,
                Args = m.Args.Count == 0 ? null : m.Args.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
                Reserved = m.Reserved, Deposit = m.Deposit, ReservedCash = m.ReservedCash, CargoVolume = m.CargoVolume,
                Sold = m.Sold, Bought = m.Bought, Deadline = m.Deadline, AcceptedTick = m.AcceptedTick,
                State = m.State.ToString(), Haggled = m.Haggled,
            }).ToList();
        if (u.NextMissionId != 1) s.NextMissionId = u.NextMissionId;
        if (u.MissionFactions.Count > 0)
            s.MissionFactions = u.MissionFactions.OrderBy(kv => kv.Key).Select(kv => new MissionFactionSave
            {
                Actor = kv.Key, Day = kv.Value.Day, Spent = kv.Value.Spent, BlockedUntil = kv.Value.BlockedUntil,
            }).ToList();
        // Phase 24: laufende Gefechte
        s.NextEncounterId = u.Fights.NextId;
        if (u.Fights.Count > 0)
            s.Fights = u.Fights.Encounters.Select(e => new EncounterSave
            {
                Id = e.Id, Sector = e.Sector.Value, Start = e.StartTick, Cause = e.Cause.ToString(), Raider = e.Raider.Value, RaiderOwner = e.RaiderOwner.Value,
                Victim = e.Victim.Value, Police = e.Police.Value, Loot = e.Loot, Fenced = e.Fenced, Shots = e.Shots, Hits = e.Hits, Spatial = e.Spatial,
                Station = e.Station.Value, StationX = e.StationX, Gate = e.Gate.Value,   // Phase 28: Belagerung; Phase 31: Tor
                Missiles = e.Missiles.Count == 0 ? null : e.Missiles.Select(m => new MissileSave
                {
                    Shooter = m.Shooter.Value, Owner = m.Owner.Value, Side = m.Side, Module = m.Module, Target = m.Target.Value, Station = m.Station.Value,
                    Left = m.Left, Total = m.Total, Speed = m.Speed, FromX = m.From.X, FromY = m.From.Y, FromStation = m.FromStation.Value, Gate = m.Gate.Value,
                }).ToList(),
                Participants = e.Participants.Select(p => new CombatParticipantSave
                {
                    Ship = p.Ship.Value, Side = p.Side, Tactic = p.Tactic, X = p.X, Target = p.Target.Value, Move = p.Move.ToString(), Speed = p.Speed,
                    Timers = p.Timers.Count == 0 ? null : new List<float>(p.Timers), Courage = p.Courage, DropShare = p.DropShare,
                    PrevMode = p.PrevMode.ToString(), Exit = p.Exit.ToString(),
                    Pos = e.Spatial ? new[] { p.Pos.X, p.Pos.Y, p.Dir.X, p.Dir.Y } : null, WasDockedAt = p.WasDockedAt.Value, Owner = p.Owner.Value,
                    Refuge = p.RefugeId != 0 ? new[] { p.Refuge, p.RefugeId, p.RefugePos.X, p.RefugePos.Y } : null,   // Phase 27
                    LastShotTick = p.LastShotTick != 0 ? p.LastShotTick : null,   // Runde 6
                    CastOffUntil = p.CastOffUntil != 0 ? p.CastOffUntil : null,   // Phase 32
                }).ToList(),
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
                Size = t.Size == StationSize.M ? null : t.Size.ToString(), Radius = t.Radius == 15f ? null : t.Radius,
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
                Defense = t.Defense is { } df ? new StationDefenseSave
                {
                    Hull = df.Hull, Shield = df.Shield, ShieldRegen = df.ShieldRegen, Armor = df.Armor, PointDefense = df.PointDefense, Guns = new List<string>(df.Guns),
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
                Scale = sec.Scale == 1f ? null : sec.Scale,
                Skybox = sec.Skybox.Length == 0 ? null : sec.Skybox,
                SkyboxYaw = sec.SkyboxYaw == 0f ? null : sec.SkyboxYaw,
                Nebula = sec.Nebula,
                GasFields = sec.GasFields.Count == 0 ? null : sec.GasFields.Select(f => new GasFieldSave { Pos = f.Position, Radius = f.Radius, Density = f.Density }).ToList(),
            });
        foreach (var g in u.Gates.Values.OrderBy(g => g.Id.Value))
            s.Gates.Add(new GateSave
            {
                Id = g.Id.Value, Sector = g.Sector.Value, Pos = g.Position, Target = g.Target.Value, Open = g.Open,
                State = g.State == GateState.Open ? null : g.State.ToString(), MainRoute = g.MainRoute,
                Type = g.TypeKey.Length == 0 ? null : g.TypeKey,
                Condition = g.Condition is { } gc ? new GateConditionSave
                {
                    HullMissing = gc.HullMissing, ShieldMissing = gc.ShieldMissing, ShieldPauseUntil = gc.ShieldPauseUntil,
                    Timers = gc.Timers.Count == 0 ? null : new List<float>(gc.Timers), Disabled = gc.Disabled, DownSince = gc.DownSince, LastAttacker = gc.LastAttacker.Value,
                    GraceUntil = gc.GraceUntil, PrevState = gc.PrevState?.ToString(),
                } : null,
            });
        // Phase 31: Torklassen (Inhalt, wie die Stationstypen)
        if (u.GateTypes.Count > 0)
            s.GateTypes = u.GateTypes.Values.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => new GateTypeSave
            {
                Key = t.Key, Name = t.Name, RepairPerDay = t.RepairPerDay, MaxOutageTicks = t.MaxOutageTicks,
                Defense = new StationDefenseSave { Hull = t.Defense.Hull, Shield = t.Defense.Shield, ShieldRegen = t.Defense.ShieldRegen, Armor = t.Defense.Armor,
                    PointDefense = t.Defense.PointDefense, Guns = new List<string>(t.Defense.Guns) },
            }).ToList();
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
                    Emergency = bs.Emergency, Abandoned = bs.Abandoned, Panic = bs.Panic,
                } : null,
                PlayerStorage = st.PlayerStorage.Count == 0 ? null : st.PlayerStorage.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                BlackStock = st.BlackStock.Count == 0 ? null : st.BlackStock.ToDictionary(kv => kv.Key.Value, kv => kv.Value),   // Schwarzmarkt (additiv)
                Exports = st.Exports.Count == 0 ? null : st.Exports.Select(b => new ExportBatchSave { Good = b.Good.Value, Amount = b.Amount, Paid = b.Paid, Due = b.DueTick }).ToList(),
                Condition = st.Condition is { } sc ? new StationConditionSave
                {
                    HullMissing = sc.HullMissing, ShieldMissing = sc.ShieldMissing, ShieldPauseUntil = sc.ShieldPauseUntil, Disabled = sc.Disabled,
                    Timers = sc.Timers.Count == 0 ? null : new List<float>(sc.Timers),
                    AlarmUntil = sc.AlarmUntil, EmergencyUntil = sc.EmergencyUntil, EmergencyCooldownUntil = sc.EmergencyCooldownUntil,
                } : null,
                MaintenanceBacklog = st.MaintenanceBacklog.Count == 0 ? null : st.MaintenanceBacklog.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                BuiltTick = st.BuiltTick, Activity = st.Activity, Starved = st.Starved, Panic = st.Panic, Profit = ToSave(st.Profit),
                CalmSince = st.CalmSince < 0 ? null : st.CalmSince,
                Integrity = st.Integrity == 1f ? null : st.Integrity,   // Ausbau Punkt 8
                Rotation = st.Rotation == 0f ? null : st.Rotation,
                CapitulationLockUntil = st.CapitulationLockUntil, Unpaid = st.Unpaid,   // Ausgleich (additiv)
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
                NextThinkTick = sh.NextThinkTick, IdleSince = sh.IdleSince, ShotTimer = sh.ShotTimer, MineAsteroid = sh.MineAsteroid, AnchorRock = sh.AnchorRock, AnchorProgress = sh.AnchorProgress,
                Cargo = sh.Cargo.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                Fuel = sh.Fuel.ToDictionary(kv => kv.Key.Value, kv => kv.Value),
                JobSince = sh.JobSince, StrandedSince = sh.StrandedSince < 0 ? null : sh.StrandedSince, Profit = ToSave(sh.Profit),
                Quality = sh.Quality == ShipQuality.Standard ? null : sh.Quality.ToString(),
                UpgradePity = sh.UpgradePity.Count == 0 ? null : new Dictionary<int, int>(sh.UpgradePity),
                HomeBase = sh.HomeBase.Value, DutySector = sh.DutySector.Value,
                Command = sh.Command is null ? null : ToSave(sh.Command), CommandSince = sh.CommandSince, CommandStep = sh.CommandStep, Emergency = sh.Emergency,
                Engagement = sh.Engagement == Engagement.Defend ? null : sh.Engagement.ToString(), Squadron = sh.Squadron.Value, DockedAt = sh.DockedAt.Value,
                RentedFrom = sh.RentedFrom.Value, RentedSince = sh.RentedSince, RentalDeposit = sh.RentalDeposit, RentPaidDays = sh.RentPaidDays,
                MissionCargo = sh.MissionCargo,   // Runde 41
                JumpTarget = sh.JumpTarget.Value, JumpCharge = sh.JumpCharge, JumpFrom = sh.JumpTarget.Value != 0 ? sh.JumpFrom : null,   // Sprungantrieb
                JumpCooldownUntil = sh.JumpCooldownUntil, Cloaked = sh.Cloaked,   // Tarnfeld
                Systems = ToSave(sh.Systems),
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

    /// <summary>Himmel der eingebetteten Standardwelt je Sektor-Id (einmal gelesen).</summary>
    static readonly Lazy<Dictionary<int, (string Name, string Skybox, float Yaw)>> DefaultSkies = new(() =>
    {
        try
        {
            return WorldBuilder.Parse(WorldBuilder.DefaultWorldJson()).Sectors
                .ToDictionary(d => d.Id, d => (d.Name, d.Skybox ?? "", float.IsFinite(d.SkyboxYaw) ? d.SkyboxYaw : 0f));
        }
        catch (Exception) { return new Dictionary<int, (string, string, float)>(); }
    });

    /// <summary>
    /// Spielstände von vor den Himmelskugeln (ohne <c>Skybox</c> in jedem Sektor) zeigten für immer nur den Farbhintergrund (Gerätetest: schwarz
    /// in jedem Sektor). Gibt es jeden Sektor nach Id und Name auch in der eingebetteten Standardwelt (auch die alte 4-Sektor-Welt), übernimmt das Laden deren Himmel.
    /// Reine Darstellungsdaten (nicht gehasht); eigene Welten und Stände mit irgendeinem Himmel bleiben unberührt.
    /// </summary>
    public static void FillDefaultSkyboxes(Universe u)
    {
        if (u.Sectors.Count == 0 || u.Sectors.Values.Any(x => x.Skybox.Length > 0)) return;
        var byId = DefaultSkies.Value;
        if (byId.Count == 0) return;
        foreach (var sec in u.Sectors.Values)
            if (!byId.TryGetValue(sec.Id.Value, out var sd) || sd.Name != sec.Name) return;
        foreach (var sec in u.Sectors.Values)
        {
            var sd = byId[sec.Id.Value];
            sec.Skybox = sd.Skybox;
            sec.SkyboxYaw = sd.Yaw;
        }
    }

    public static Universe FromSave(SaveGame s)
    {
        var u = new Universe
        {
            Tick = s.Tick, Seed = s.Seed, LastStationBuildTick = s.LastStationBuildTick, Rules = s.Rules,
            LastStationRetireTick = s.LastStationRetireTick,
            ScarceSince = s.ScarceSince is null ? new Dictionary<GoodId, long>() : s.ScarceSince.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value), NextAsteroidId = s.NextAsteroidId,
            PanicSince = s.PanicSince is null ? new Dictionary<(int, GoodId), long>()
                : s.PanicSince.Where(e => e.Length >= 3).ToDictionary(e => ((int)e[0], new GoodId((int)e[1])), e => e[2]),
            IdleRetiredTick = s.IdleRetiredTick is null ? new Dictionary<(string, int), long>()
                : s.IdleRetiredTick.ToDictionary(kv => (kv.Key[..kv.Key.LastIndexOf('@')], int.Parse(kv.Key[(kv.Key.LastIndexOf('@') + 1)..], System.Globalization.CultureInfo.InvariantCulture)), kv => kv.Value),
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
                Id = new ActorId(a.Id), Name = a.Name, Tag = a.Tag ?? "", Kind = Enum.Parse<ActorKind>(a.Kind), Membership = a.Membership is { } m ? new FactionId(m) : null,
                Treasury = new Treasury { Credits = a.Credits }, ShipsUseTreasury = a.ShipsUseTreasury,
                Personality = new Personality(a.Aggression, a.Greed, a.Cooperation, a.Lawfulness),
                Relations = a.Relations is null ? new Dictionary<ActorId, float>() : a.Relations.ToDictionary(kv => new ActorId(kv.Key), kv => kv.Value),
                CreatedTick = a.Created,
                Wanted = a.Wanted is null ? new Dictionary<FactionId, WantedRecord>()
                    : a.Wanted.ToDictionary(w => new FactionId(w.Faction), w => new WantedRecord { Heat = w.Heat, LastCrimeTick = w.LastCrime, Fine = w.Fine, LastCrime = w.Crime }),
                BountyClaims = a.BountyClaims is null ? new Dictionary<FactionId, float>() : a.BountyClaims.ToDictionary(kv => new FactionId(kv.Key), kv => kv.Value),
                LastForceTick = a.LastForceTick, PoliceNeed = a.PoliceNeed, PoliceSurplusSince = a.PoliceSurplusSince, LastRestoreTick = a.LastRestoreTick, LastOutpostTick = a.LastOutpostTick,
                ArrestCooldownUntil = a.ArrestCooldownUntil,
                CrimeStanding = a.CrimeStanding is null ? new Dictionary<FactionId, CrimeStandingRecord>()
                    : a.CrimeStanding.ToDictionary(c => new FactionId(c.Faction), c => new CrimeStandingRecord
                    {
                        Debt = c.Debt, Base = c.Base, WindowStart = c.WindowStart, WindowLoss = c.WindowLoss, LastReason = c.Reason, LastTick = c.LastTick, LastLoss = c.LastLoss,
                    }),
                Intel = IntelCodec.FromSave(a.Intel),
            };
        if (s.Encounters is not null)
            foreach (var e in s.Encounters)
                u.PendingEncounters.Add(new PendingEncounter
                {
                    Kind = Enum.Parse<EncounterKind>(e.Kind), Source = new ShipId(e.Source), Target = new ShipId(e.Target), Sector = new SectorId(e.Sector), DueTick = e.Due,
                    Station = new StationId(e.Station),
                });
        if (s.Ledger is not null)   // Runde 30
            foreach (var l in s.Ledger)
                u.Ledger[(l.Actor, l.Station, l.Good)] = new LedgerPost
                {
                    BoughtQty = l.BoughtQty, BoughtPrice = l.BoughtPrice, BoughtStockBefore = l.BoughtStockBefore, SoldQty = l.SoldQty, SoldPrice = l.SoldPrice, SoldStockBefore = l.SoldStockBefore,
                    BoughtOpen = l.BoughtOpen ?? l.BoughtQty > 0f, SoldOpen = l.SoldOpen ?? l.SoldQty > 0f, LastTick = l.LastTick,   // Runde 32b
                    MissingQty = l.MissingQty, MissingSeen = l.MissingSeen,   // Runde 36
                };
        // Phase 28: Kriege und Friedensschlüsse
        if (s.Wars is not null)
            foreach (var w in s.Wars)
                u.Wars.Add(new War { A = new FactionId(w.A), B = new FactionId(w.B), Since = w.Since, Weariness = w.Weariness, LossA = w.LossA, LossB = w.LossB, Sieges = w.Sieges, Blockades = w.Blockades, CeasefireUntil = w.CeasefireUntil, LastOfferTick = w.LastOfferTick, TributePaid = w.TributePaid, Join = w.Join, ShipsLostA = w.ShipsLostA, ShipsLostB = w.ShipsLostB });
        if (s.PeaceSince is not null)
            foreach (var p in s.PeaceSince) if (p.Length >= 3) u.PeaceSince[((int)p[0], (int)p[1])] = p[2];
        if (s.NavalEnded is not null)   // Nachtrag 2b
            foreach (var p in s.NavalEnded) if (p.Length >= 4) u.NavalEnded[((int)p[0], (int)p[1], (int)p[2])] = p[3];
        if (s.Alliances is not null)   // Runde 45
            foreach (var p in s.Alliances) if (p.Length >= 3) u.Alliances.Add(new Alliance { A = new FactionId((int)p[0]), B = new FactionId((int)p[1]), Since = p[2] });
        if (s.AllianceEnded is not null)
            foreach (var p in s.AllianceEnded) if (p.Length >= 3) u.AllianceEnded[((int)p[0], (int)p[1])] = p[2];
        // Runde 40: Posteingang (gespeichert, nicht gehasht)
        if (s.Inbox is not null)
            foreach (var m in s.Inbox)
                u.Inbox.Add(new CommsMessage
                {
                    Id = m.Id, From = new ActorId(m.From), Kind = m.Kind,
                    Args = m.Args is null ? new() : new Dictionary<string, string>(m.Args),
                    Created = m.Created, Expires = m.Expires, Read = m.Read,
                });
        u.NextMessageId = Math.Max(Math.Max(1, s.NextMessageId), u.Inbox.Count == 0 ? 1 : u.Inbox.Max(m => m.Id) + 1);
        // Runde 41: Auftragsangebote und Aufträge (Angebote nicht gehasht, Aufträge schon)
        if (s.MissionOffers is not null)
            foreach (var o in s.MissionOffers)
                u.MissionOffers.Add(new MissionOffer
                {
                    Id = o.Id, Type = o.Type, Client = new ActorId(o.Client), At = new StationId(o.At),
                    Args = o.Args is null ? new() : new Dictionary<string, string>(o.Args),
                    Reward = o.Reward, Deposit = o.Deposit, Expires = o.Expires, Created = o.Created, Haggled = o.Haggled,
                });
        if (s.Missions is not null)
            foreach (var m in s.Missions)
                u.Missions.Add(new Mission
                {
                    Id = m.Id, Type = m.Type, Client = new ActorId(m.Client), At = new StationId(m.At),
                    Args = m.Args is null ? new() : new Dictionary<string, string>(m.Args),
                    Reserved = m.Reserved, Deposit = m.Deposit, ReservedCash = m.ReservedCash, CargoVolume = m.CargoVolume,
                    Sold = m.Sold, Bought = m.Bought, Deadline = m.Deadline, AcceptedTick = m.AcceptedTick,
                    State = Enum.TryParse<MissionState>(m.State, out var ms) ? ms : MissionState.Active, Haggled = m.Haggled,
                });
        u.NextMissionId = Math.Max(Math.Max(1, s.NextMissionId), u.MissionOffers.Count == 0 ? 1 : u.MissionOffers.Max(o => o.Id) + 1);
        u.NextMissionId = Math.Max(u.NextMissionId, u.Missions.Count == 0 ? 1 : u.Missions.Max(m => m.Id) + 1);
        if (s.MissionFactions is not null)
            foreach (var f in s.MissionFactions)
                u.MissionFactions[f.Actor] = new MissionFactionState { Day = f.Day, Spent = f.Spent, BlockedUntil = f.BlockedUntil };
        // Phase 26: Wracks und Frachtbehälter
        u.NextWreckId = Math.Max(1, s.NextWreckId);
        if (s.Wrecks is not null)
            foreach (var w in s.Wrecks)
            {
                var wr = new Wreck
                {
                    Id = w.Id, Kind = Enum.TryParse<WreckKind>(w.Kind, out var k) ? k : WreckKind.Wreck, Sector = new SectorId(w.Sector), Position = new Vec2(w.X, w.Y),
                    Name = w.Name, From = new ShipId(w.From), CreatedTick = w.Created, ExpiresTick = w.Expires,
                };
                if (w.Cargo is not null) foreach (var (g, a) in w.Cargo.OrderBy(x => x.Key)) wr.Cargo[new GoodId(g)] = a;
                if (w.Modules is not null) foreach (var m in w.Modules) wr.Modules.Add(new WreckModule { Key = m.Key, Condition = m.Condition });
                u.Wrecks.Add(wr);
            }
        // Phase 24: laufende Gefechte (der Index Schiff → Gefecht ist abgeleitet)
        u.Fights.NextId = Math.Max(1, s.NextEncounterId);
        if (s.Fights is not null)
            foreach (var e in s.Fights)
                u.Fights.Encounters.Add(new Encounter
                {
                    Id = e.Id, Sector = new SectorId(e.Sector), StartTick = e.Start, Cause = Enum.Parse<EncounterCause>(e.Cause), Raider = new ShipId(e.Raider),
                    RaiderOwner = new ActorId(e.RaiderOwner), Victim = new ShipId(e.Victim), Police = new ShipId(e.Police), Loot = e.Loot, Fenced = e.Fenced,
                    Shots = e.Shots, Hits = e.Hits, Spatial = e.Spatial,
                    Station = new StationId(e.Station), StationX = e.StationX, Gate = new GateId(e.Gate),
                    Missiles = e.Missiles is null ? new List<MissileFlight>() : e.Missiles.Select(m => new MissileFlight
                    {
                        Shooter = new ShipId(m.Shooter), Owner = new ActorId(m.Owner), Side = m.Side, Module = m.Module, Target = new ShipId(m.Target),
                        Station = new StationId(m.Station), Left = m.Left, Total = m.Total, Speed = m.Speed, From = new Vec2(m.FromX, m.FromY),
                        FromStation = new StationId(m.FromStation), Gate = new GateId(m.Gate),
                    }).ToList(),
                    Participants = e.Participants.Select(p => new CombatParticipant
                    {
                        Ship = new ShipId(p.Ship), Side = p.Side, Tactic = p.Tactic, X = p.X, Target = new ShipId(p.Target), Move = Enum.Parse<CombatMove>(p.Move),
                        Speed = p.Speed, Timers = p.Timers is null ? new List<float>() : new List<float>(p.Timers), Courage = p.Courage, DropShare = p.DropShare,
                        PrevMode = Enum.Parse<PowerMode>(p.PrevMode), Exit = Enum.Parse<CombatExit>(p.Exit),
                        Pos = p.Pos is { Length: >= 4 } ps ? new Vec2(ps[0], ps[1]) : default, Dir = p.Pos is { Length: >= 4 } pd ? new Vec2(pd[2], pd[3]) : new Vec2(0f, -1f),
                        WasDockedAt = new StationId(p.WasDockedAt), Owner = new ActorId(p.Owner),
                        Refuge = p.Refuge is { Length: >= 4 } rf ? rf[0] : 0f, RefugeId = p.Refuge is { Length: >= 4 } ri ? (int)ri[1] : 0,
                        RefugePos = p.Refuge is { Length: >= 4 } rp ? new Vec2(rp[2], rp[3]) : default,
                        LastShotTick = p.LastShotTick ?? 0,
                        CastOffUntil = p.CastOffUntil ?? 0,
                    }).ToList(),
                });
        foreach (var e in u.Fights.Encounters) Combat.CombatSpace.Derive(e);   // Phase 25: abgeleitete Raumkennung, Zwischenbild = Position
        u.Fights.Rebuild();
        u.NextActorId = s.NextActorId;
        u.EnsureCoreActors();
        u.AssignMissingTags();   // Phase 29: ältere Stände ohne Kürzel
        foreach (var g in s.Goods)
            u.Goods[new GoodId(g.Id)] = new Good { Id = new GoodId(g.Id), Key = g.Key, Name = g.Name, Color = g.Color, BasePrice = g.BasePrice, VolumePerUnit = g.Volume, Form = ParseForm(g.Form), Category = g.Category ?? "" };
        foreach (var c in s.AsteroidClasses)
            u.AsteroidClasses[c.Key] = new AsteroidClass
            {
                Key = c.Key, Name = c.Name, Color = c.Color, ChunkChance = c.ChunkChance, OreFactor = c.OreFactor,
                Yields = c.Yields.Select(y => new Yield(new GoodId(y.Good), y.Weight)).ToList(),
            };
        // Phase 31: Torklassen
        if (s.GateTypes is not null)
            foreach (var t in s.GateTypes)
                u.GateTypes[t.Key] = new GateType
                {
                    Key = t.Key, Name = t.Name, RepairPerDay = t.RepairPerDay, MaxOutageTicks = t.MaxOutageTicks,
                    Defense = new StationDefenseConfig { Hull = t.Defense.Hull, Shield = t.Defense.Shield, ShieldRegen = t.Defense.ShieldRegen, Armor = t.Defense.Armor,
                        PointDefense = t.Defense.PointDefense, Guns = new List<string>(t.Defense.Guns) },
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
                Size = t.Size is { } sz ? Enum.Parse<StationSize>(sz) : StationSize.M, Radius = t.Radius ?? 15f,
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
                // Phase 28: fehlt = keine Verteidigung
                Defense = t.Defense is { } df ? new StationDefenseConfig
                {
                    Hull = df.Hull, Shield = df.Shield, ShieldRegen = df.ShieldRegen, Armor = df.Armor, PointDefense = df.PointDefense, Guns = new List<string>(df.Guns),
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
                Scale = sec.Scale is { } sc && float.IsFinite(sc) && sc >= 1f ? sc : 1f,
                Skybox = sec.Skybox ?? "",
                SkyboxYaw = sec.SkyboxYaw is { } yaw && float.IsFinite(yaw) ? yaw : 0f,
                Nebula = sec.Nebula,
                GasFields = sec.GasFields?.Select(f => new GasField { Position = f.Pos, Radius = f.Radius, Density = f.Density }).ToList() ?? new List<GasField>(),
            };
        foreach (var g in s.Gates)
            u.Gates[new GateId(g.Id)] = new Gate
            {
                Id = new GateId(g.Id), Sector = new SectorId(g.Sector), Position = g.Pos, Target = new GateId(g.Target),
                // Phase 10: Zustand; ältere Stände kennen nur Open
                State = g.State is { } gs ? Enum.Parse<GateState>(gs) : g.Open ? GateState.Open : GateState.Closed,
                MainRoute = g.MainRoute,
                TypeKey = g.Type ?? "",
                Condition = g.Condition is { } gc ? new GateCondition
                {
                    HullMissing = gc.HullMissing, ShieldMissing = gc.ShieldMissing, ShieldPauseUntil = gc.ShieldPauseUntil,
                    Timers = gc.Timers is null ? new List<float>() : new List<float>(gc.Timers), Disabled = gc.Disabled, DownSince = gc.DownSince, LastAttacker = new ActorId(gc.LastAttacker),
                    GraceUntil = gc.GraceUntil, PrevState = gc.PrevState is { } ps ? Enum.Parse<GateState>(ps) : null,
                } : null,
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
                    Emergency = bs.Emergency, Abandoned = bs.Abandoned, Panic = bs.Panic,
                } : null,
                PlayerStorage = st.PlayerStorage is null ? new Dictionary<GoodId, float>() : st.PlayerStorage.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
                BlackStock = st.BlackStock is null ? new Dictionary<GoodId, float>() : st.BlackStock.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
                Exports = st.Exports is null ? new List<ExportBatch>() : st.Exports.Select(b => new ExportBatch { Good = new GoodId(b.Good), Amount = b.Amount, Paid = b.Paid, DueTick = b.Due }).ToList(),
                Condition = st.Condition is { } sc ? new StationCondition
                {
                    HullMissing = sc.HullMissing, ShieldMissing = sc.ShieldMissing, ShieldPauseUntil = sc.ShieldPauseUntil, Disabled = sc.Disabled,
                    Timers = sc.Timers is null ? new List<float>() : new List<float>(sc.Timers),
                    AlarmUntil = sc.AlarmUntil, EmergencyUntil = sc.EmergencyUntil, EmergencyCooldownUntil = sc.EmergencyCooldownUntil,
                } : null,
                MaintenanceBacklog = st.MaintenanceBacklog is null ? new Dictionary<GoodId, float>() : st.MaintenanceBacklog.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
                BuiltTick = st.BuiltTick, Activity = st.Activity, Starved = st.Starved, Panic = st.Panic, Profit = FromSave(st.Profit),
                CalmSince = st.CalmSince ?? -1,
                Integrity = st.Integrity ?? 1f,   // Ausbau Punkt 8
                Rotation = st.Rotation ?? 0f,
                CapitulationLockUntil = st.CapitulationLockUntil, Unpaid = st.Unpaid,   // Ausgleich (additiv)
            };
        // Phase 30: Radius und Andockbarkeit aus dem Typ (Baustelle: Zieltyp) — abgeleitet, nicht gespeichert
        foreach (var st in u.Stations.Values) Content.StationFactory.ApplySize(u, st);
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
                NextThinkTick = sh.NextThinkTick, IdleSince = sh.IdleSince, ShotTimer = sh.ShotTimer, MineAsteroid = sh.MineAsteroid, AnchorRock = sh.AnchorRock, AnchorProgress = sh.AnchorProgress,
                Cargo = sh.Cargo.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
                Fuel = sh.Fuel.ToDictionary(kv => new GoodId(kv.Key), kv => kv.Value),
                JobSince = sh.JobSince, StrandedSince = sh.StrandedSince ?? -1, Profit = FromSave(sh.Profit),
                Quality = sh.Quality is { } ql ? Enum.Parse<ShipQuality>(ql) : ShipQuality.Standard,
                UpgradePity = sh.UpgradePity is null ? new Dictionary<int, int>() : new Dictionary<int, int>(sh.UpgradePity),
                HomeBase = new StationId(sh.HomeBase), DutySector = new SectorId(sh.DutySector),
                Command = sh.Command is null ? null : CommandFromSave(sh.Command), CommandSince = sh.CommandSince, CommandStep = sh.CommandStep, Emergency = sh.Emergency,
                Engagement = sh.Engagement is { } eg ? Enum.Parse<Engagement>(eg) : Engagement.Defend, Squadron = new SquadronId(sh.Squadron),
                DockedAt = new StationId(sh.DockedAt), RentedFrom = new StationId(sh.RentedFrom), RentedSince = sh.RentedSince, RentalDeposit = sh.RentalDeposit,
                RentPaidDays = sh.RentPaidDays, MissionCargo = sh.MissionCargo,   // Runde 41
                JumpTarget = new SectorId(sh.JumpTarget), JumpCharge = sh.JumpCharge, JumpFrom = sh.JumpFrom ?? default,   // Sprungantrieb
                JumpCooldownUntil = sh.JumpCooldownUntil, Cloaked = sh.Cloaked,   // Tarnfeld
                Systems = FromSave(sh.Systems),
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
        SectorExtent.Recompute(u);   // Runde 44: abgeleitet (Gefechtsfläche, Sektorkarte)
        u.RebuildTracker();
        u.Recovering.Rebuild(u);   // Phase 23: abgeleitet — Schiffe, denen Schild oder Ladung fehlt
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
        FillDefaultSkyboxes(u);
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
        // Phase 27: Lauern (Revier, Posten, Frist) und Verfolgen (Ziel, Vorsprung, Frist)
        LurkCommand l => new OrderSave { Kind = CommandKinds.Lurk, A = l.Sector.Value, C = (int)l.Until, Values = new List<float> { l.Spot.X, l.Spot.Y } },
        PursueCommand q => new OrderSave { Kind = CommandKinds.Pursue, A = q.Target.Value, C = (int)q.Until, Values = new List<float> { q.Lead } },
        AssaultCommand a => new OrderSave { Kind = CommandKinds.Assault, A = a.Sector.Value, B = a.Station.Value, C = a.Gate.Value },   // Phase 28; Phase 31: Tor
        _ => FleetOpsCodec.ToSave(c) ?? throw new InvalidOperationException($"Unbekannter Befehl {c.GetType().Name}"),   // Flottenbefehle (Punkt 1/1b)
    };

    internal static OrderSave LimitsSave(string kind, AutoLimits l) =>
        new() { Kind = kind, A = l.Center.Value, B = l.RadiusJumps, Values = l.AllowLoss ? new List<float> { l.MaxBuyFactor, l.Reserve, 1f } : new List<float> { l.MaxBuyFactor, l.Reserve } };   // Runde 24: dritter Wert nur bei erlaubtem Verlustkauf (additiv)

    internal static AutoLimits LimitsFrom(OrderSave o) =>
        new(new SectorId(o.A), o.B, o.Values is { Count: > 0 } v ? v[0] : 0f, o.Values is { Count: > 1 } w ? w[1] : 0f, o.Values is { Count: > 2 } x && x[2] > 0.5f);

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
        CommandKinds.Lurk => new LurkCommand(new SectorId(o.A), new Vec2(o.Values?.ElementAtOrDefault(0) ?? 0f, o.Values?.ElementAtOrDefault(1) ?? 0f), o.C),
        CommandKinds.Pursue => new PursueCommand(new ShipId(o.A), o.Values?.ElementAtOrDefault(0) ?? 0f, o.C),
        CommandKinds.Assault => new AssaultCommand(new SectorId(o.A), new StationId(o.B), new GateId(o.C)),
        _ => FleetOpsCodec.FromSave(o) ?? throw new FormatException($"Unbekannter Befehl '{o.Kind}'"),
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
