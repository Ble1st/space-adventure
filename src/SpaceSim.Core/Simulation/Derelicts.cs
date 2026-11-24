using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Ein herrenloses Schiff ist entstanden (<c>Cause</c>: „abandoned“ = Besatzung ausgestiegen, „event“ = Zufallsereignis, „breakdown“ = Pannenauftrag).</summary>
public sealed record DerelictCreated(ShipId Ship, SectorId Sector, ShipId From, string Cause, string Message) : SimEvent;

/// <summary>Der Spieler hat ein herrenloses Schiff übernommen.</summary>
public sealed record DerelictClaimed(ShipId Ship, ShipId By, ActorId PreviousOwner, string Message) : SimEvent;

/// <summary>Ein herrenloses Schiff ist verfallen (Frist abgelaufen).</summary>
public sealed record DerelictExpired(ShipId Ship, SectorId Sector) : SimEvent;

/// <summary>
/// Herrenlose Schiffe (Lücke gegenüber Interstellar Pilot 2, docs/KAMPF.md „Herrenlose Schiffe“). Ein herrenloses Schiff ist ein gewöhnliches
/// <see cref="Ship"/> mit Rolle <see cref="ShipRole.Derelict"/> und Steuerer <see cref="Controller.None"/>: kein System wählt es (Rollenfilter,
/// <see cref="Ship.IsEconomyAi"/> falsch, keine Kapazität, kein Opfer, kein Ziel), es liegt still und verfällt nach <see cref="EconomyRules.DerelictTicks"/>.
/// <list type="bullet">
/// <item><see cref="TryAbandon"/>: beim Zerstören (<see cref="Departures"/> Grund a) steigt die NPC-Besatzung mit <see cref="EconomyRules.DerelictChance"/> aus —
/// eine Zahl aus <see cref="Universe.Rng"/>; statt eines Wracks bleibt ein Schiff mit 5–15 % Hülle und einem Teil der Fracht.</item>
/// <item><see cref="SpawnEvent"/>: Zufallsereignis — eigener RNG aus Welt-Seed und Ereignis-Takt (nie <see cref="Universe.Rng"/>).</item>
/// <item><see cref="Claim"/>: ein eigenes Schiff bis <see cref="EconomyRules.DerelictClaimRange"/> heran → das Schiff gehört dem Spieler (geparkt).
/// Ein Pannenschiff einer Fraktion nur mit dem passenden Pannenauftrag; es folgt dann dem Übernehmer.</item>
/// </list>
/// </summary>
public static class Derelicts
{
    /// <summary>Darf dieses Schiff beim Zerstören herrenlos werden? (Schalter, kein Spielerschiff, Modell mit Klasse, nicht schon herrenlos.)</summary>
    static bool Eligible(Universe u, Ship ship) =>
        u.Rules.DerelictShips && !ship.IsPlayer && ship.Owner != u.PlayerActor && !ship.IsDerelict
        && u.ShipModels.TryGetValue(ship.ModelKey, out var m) && u.ShipClasses.ContainsKey(m.ClassKey);

    /// <summary>
    /// Beim Zerstören eines Nicht-Spielerschiffs (vor dem Entfernen aufrufen): eine Zufallszahl aus <see cref="Universe.Rng"/>; liegt sie unter
    /// <see cref="EconomyRules.DerelictChance"/>, entsteht ein herrenloses Schiff gleichen Modells am Ort (Hülle aus derselben Zahl, Fracht anteilig wie beim
    /// Wrack). null = kein herrenloses Schiff (dann hinterlässt es wie bisher ein Wrack). Schalter aus = keine Zahl, null.
    /// </summary>
    public static Ship? TryAbandon(Universe u, Ship ship)
    {
        if (!Eligible(u, ship)) return null;
        var r = u.Rules;
        double roll = u.Rng.NextDouble();
        if (roll >= r.DerelictChance || r.DerelictChance <= 0f) return null;
        float t = (float)(roll / r.DerelictChance);
        float hull = r.DerelictHullMin + (r.DerelictHullMax - r.DerelictHullMin) * t;
        var at = Combat.PlayerCombat.PositionOf(u, ship.Id);
        var cargo = new Dictionary<GoodId, float>();
        float share = Math.Clamp(r.WreckCargoShare, 0f, 1f);
        foreach (var g in ship.Cargo.Keys.OrderBy(k => k.Value))
            if (ship.Cargo[g] * share > 0.001f) cargo[g] = ship.Cargo[g] * share;
        var d = Create(u, ship.ModelKey, at?.Sector ?? ship.Sector, at?.Pos ?? ship.Position, u.FreeActor, hull, u.Tick + r.DerelictTicks, cargo, ship.Fuel);
        if (d is null) return null;
        u.Events.Publish(new DerelictCreated(d.Id, d.Sector, ship.Id, "abandoned", $"Die Besatzung hat {Name(u, d)} verlassen — es treibt herrenlos."));
        return d;
    }

    /// <summary>
    /// Legt ein herrenloses Schiff an (Rolle <see cref="ShipRole.Derelict"/>, Steuerer <see cref="Controller.None"/>, nicht angedockt). Ohne Modell/Klasse null.
    /// <paramref name="fuel"/> null = voller Tank.
    /// </summary>
    public static Ship? Create(Universe u, string modelKey, SectorId sector, Vec2 pos, ActorId owner, float hullShare, long until,
        IReadOnlyDictionary<GoodId, float>? cargo = null, IReadOnlyDictionary<GoodId, float>? fuel = null)
    {
        if (!u.ShipModels.TryGetValue(modelKey, out var m) || !u.ShipClasses.TryGetValue(m.ClassKey, out var cls)) return null;
        var ship = new Ship
        {
            Id = u.NewShipId(), Role = ShipRole.Derelict, Controller = Controller.None, Owner = owner, Sector = sector, Position = pos, ModelKey = modelKey,
            Slots = cls.Slots.Select(s => new ClassSlot { Type = s.Type, Size = s.Size, Facing = s.Facing, Arc = s.Arc }).ToList(),
            Fit = new List<string?>(m.Fit), DerelictUntil = until,
        };
        if (ship.RecomputeStats(u)) ship.CargoCapacity = ship.Stats.Cargo;
        ship.Hull = MathF.Max(1f, ship.MaxHull * Math.Clamp(hullShare, 0.01f, 1f));
        if (cargo is not null)
        {
            float free = ship.CargoCapacity;
            foreach (var (g, a) in cargo.OrderBy(k => k.Key.Value))
            {
                float vol = u.Goods.TryGetValue(g, out var good) ? MathF.Max(0.0001f, good.VolumePerUnit) : 1f;
                float take = MathF.Min(a, free / vol);
                if (take <= 0.001f) continue;
                ship.Cargo[g] = take;
                free -= take * vol;
            }
        }
        if (fuel is not null)
            foreach (var (g, a) in fuel) if (a > 0f) ship.Fuel[g] = a;
        u.AddShip(ship);
        return ship;
    }

    /// <summary>Anzeigename „Modell #Id“.</summary>
    public static string Name(Universe u, Ship s) => $"{(u.ShipModels.TryGetValue(s.ModelKey, out var m) ? m.Name : "Schiff")} #{s.Id.Value}";

    /// <summary>Läuft ein Pannenauftrag des Spielers, der dieses Schiff betrifft?</summary>
    public static Mission? BreakdownOf(Universe u, ShipId id) =>
        u.Missions.FirstOrDefault(m => m.State == MissionState.Active && m.Type == BreakdownMission.KeyId && m.Args.GetValueOrDefault("ship") == id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Position eines Schiffs (im Gefecht die Gefechtsposition, sonst die geparkte).</summary>
    static (SectorId Sector, Vec2 Pos) Where(Universe u, Ship s) => Combat.PlayerCombat.PositionOf(u, s.Id) is { } p ? (p.Sector, p.Pos) : (s.Sector, s.Position);

    /// <summary>Sperrgrund der Übernahme durch <paramref name="claimer"/> (null = möglich). Rein.</summary>
    public static string? ClaimIssue(Universe u, Ship claimer, Ship d)
    {
        if (!d.IsDerelict) return "Das Schiff ist nicht herrenlos.";
        if (!claimer.IsPlayer) return "Nur eigene Schiffe können übernehmen.";
        if (d.Owner != u.FreeActor && BreakdownOf(u, d.Id) is null)
            return $"Gehört {u.ActorName(d.Owner)} — nur mit dem passenden Pannenauftrag.";
        if (claimer.Transit is not null || claimer.DockedAt.Value != 0) return "Erst im Raum anhalten.";
        if (u.Fights.IsEngaged(claimer.Id)) return "Nicht im Gefecht.";
        var (cs, cp) = Where(u, claimer);
        if (cs != d.Sector) return "Nicht im selben Sektor.";
        float dist = Vec2.Distance(cp, d.Position);
        if (dist > u.Rules.DerelictClaimRange) return $"Zu weit weg ({dist:0} m, höchstens {u.Rules.DerelictClaimRange:0} m).";
        return null;
    }

    /// <summary>
    /// Übernimmt ein herrenloses Schiff: Besitzer Spieler, Steuerer Spieler, geparkt im Raum. Ein Pannenschiff folgt danach dem Übernehmer
    /// (<see cref="FollowCommand"/>, damit es mit zur Auftragsstation fliegt und dort andockt). false mit Grund = nichts geändert.
    /// </summary>
    public static bool Claim(Universe u, Ship claimer, Ship d, out string message)
    {
        if (ClaimIssue(u, claimer, d) is { } issue) { message = issue; return false; }
        var prev = d.Owner;
        bool breakdown = BreakdownOf(u, d.Id) is not null;
        d.Owner = u.PlayerActor;
        d.Role = ShipRole.Player;
        d.Controller = Controller.Player;
        d.DerelictUntil = 0;
        d.Order = null; d.IdleSince = -1;
        d.RecomputeStats(u);
        message = $"Du übernimmst {Name(u, d)} (Hülle {d.Hull / MathF.Max(1f, d.MaxHull) * 100f:0} %).";
        if (breakdown)
        {
            if (Commands.Give(u, d, new FollowCommand(claimer.Id), out _, ignoreCapacity: true)) message += " Es folgt dir zur Auftragsstation.";
        }
        else message += " Es liegt geparkt — Befehle über die Flotte.";
        u.Events.Publish(new DerelictClaimed(d.Id, claimer.Id, prev, message));
        return true;
    }

    // ───────── Zufallsereignis ─────────

    /// <summary>Eigener RNG des Ereignisses (Welt-Seed + Takt), nie <see cref="Universe.Rng"/>.</summary>
    public static DeterministicRng EventRng(Universe u, long bucket) =>
        new(u.Seed ^ (0xD3E1C7A55EEDUL * (ulong)(bucket + 1)) ^ 0x5DEECE66DUL);

    /// <summary>Freier Platz nahe einem Anker (Station/Tor) im Sektor, 80–220 m entfernt.</summary>
    public static Vec2? Spot(Universe u, Sector sec, Vec2 anchor) => Outposts.FreeSpot(u, sec, anchor, 8f, 80f, 220f);

    /// <summary>
    /// Zufallsereignis „herrenloses Schiff“ für einen Takt: mit <see cref="EconomyRules.DerelictEventChance"/> treibt in einem bekannten Sektor ein
    /// verlassenes Schiff eines KI-Modells (feste Reihenfolge, eigener RNG). null = diesmal keins.
    /// </summary>
    public static Ship? SpawnEvent(Universe u, long bucket)
    {
        var r = u.Rules;
        var rng = EventRng(u, bucket);
        if (rng.NextDouble() >= r.DerelictEventChance) return null;
        var sectors = u.Sectors.Values.Where(s => Intel.Knowledge.KnowsSector(u, s.Id)).OrderBy(s => s.Id.Value).ToList();
        var models = u.ShipModels.Values.Where(m => m.Jobs.Count > 0 && u.ShipClasses.ContainsKey(m.ClassKey)).OrderBy(m => m.Key, StringComparer.Ordinal).ToList();
        if (sectors.Count == 0 || models.Count == 0) return null;
        var sec = sectors[rng.NextInt(sectors.Count)];
        var model = models[rng.NextInt(models.Count)];
        float hull = rng.Range(r.DerelictHullMin, r.DerelictHullMax);
        var anchors = sec.Stations.Where(id => u.Stations.ContainsKey(id)).Select(id => u.Stations[id].Position)
            .Concat(sec.Gates.Where(id => u.Gates.ContainsKey(id)).Select(id => u.Gates[id].Position)).ToList();
        var anchor = anchors.Count == 0 ? default : anchors[rng.NextInt(anchors.Count)];
        if (Spot(u, sec, anchor) is not { } pos) return null;
        var d = Create(u, model.Key, sec.Id, pos, u.FreeActor, hull, u.Tick + r.DerelictTicks);
        if (d is null) return null;
        u.Events.Publish(new DerelictCreated(d.Id, sec.Id, default, "event", $"Notsignal: ein verlassenes {model.Name} treibt in {sec.Name} — herrenlos, wer es erreicht, kann es übernehmen."));
        return d;
    }
}

/// <summary>
/// Verfall herrenloser Schiffe und das Zufallsereignis (Kampfmodul nach den Wracks, Intervall <see cref="Interval"/>). Ohne Schalter nichts.
/// </summary>
public sealed class DerelictSystem : ISimSystem
{
    public const int Interval = 50;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.DerelictShips) return;
        List<Ship>? expired = null;
        foreach (var s in u.ShipsById())
            if (s.IsDerelict && s.DerelictUntil > 0 && u.Tick >= s.DerelictUntil) (expired ??= new()).Add(s);
        if (expired is not null)
            foreach (var s in expired)
            {
                u.Events.Publish(new DerelictExpired(s.Id, s.Sector));
                Departures.RemoveShip(u, s, DepartureReason.Destroyed, "derelict_expired");
            }
        if (r.DerelictEventTicks > 0 && u.Tick > 0 && u.Tick % r.DerelictEventTicks < dt)
            Derelicts.SpawnEvent(u, u.Tick / r.DerelictEventTicks);
    }
}
