using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 28 (KAMPF.md Stufe 28): Raketen — ein Waffentyp mit Flugzeit über die gemeinsame Trefferregel. Ein Raketenwerfer (Waffenmodul mit Modulwert
/// <c>missileSpeed</c> &gt; 0) verbraucht je Schuss eine Rakete aus dem Raketenmagazin (<see cref="ShipStats.MissileCapacity"/>, Ware der Kategorie
/// <see cref="Category"/>; mit abgeschaltetem <see cref="EconomyRules.AmmoActive"/> gilt das Magazin als voll) und startet eine <see cref="MissileFlight"/>.
/// Die Rakete fliegt Abstand ÷ Tempo; am Ziel prüft zuerst die <b>Punktabwehr</b> des Ziels (ein Zufallszug, Chance gedeckelt auf
/// <see cref="EconomyRules.PointDefenseMax"/>), dann würfelt <see cref="Hit.Roll"/> mit dem Tempo des Ziels in diesem Moment (<b>Ausweichen</b>: schnelle, kleine
/// Ziele weichen aus; zwei Züge). Ist das Ziel inzwischen entkommen oder zerstört, verfällt die Rakete ohne Zug. Ohne <see cref="EconomyRules.Warfare"/> feuert ein
/// Raketenwerfer wie eine gewöhnliche Waffe (Testhilfe; die Standardinhalte enthalten ihn dann nicht).
/// </summary>
public static class Missiles
{
    /// <summary>Warenkategorie der Raketen.</summary>
    public const string Category = "missile";

    public static bool IsMissile(Module m) => m.Stat("missileSpeed") > 0f;

    /// <summary>Raketenware der Welt (kleinste Id der Kategorie); null = keine.</summary>
    public static Good? MissileGood(Universe u)
    {
        Good? best = null;
        foreach (var g in u.Goods.Values)
            if (g.Category == Category && (best is null || g.Id.Value < best.Id.Value)) best = g;
        return best;
    }

    /// <summary>Raketen an Bord (mit abgeschalteter Munition: voll).</summary>
    public static float Loaded(Universe u, Ship ship) =>
        !u.Rules.AmmoActive ? ship.Stats.MissileCapacity : MathF.Max(0f, ship.Stats.MissileCapacity - ship.Systems.MissilesMissing);

    public static bool HasMissile(Universe u, Ship ship) => Loaded(u, ship) >= 1f - 1e-4f;

    public static float Missing(Universe u, Ship ship) => u.Rules.AmmoActive ? MathF.Min(ship.Systems.MissilesMissing, ship.Stats.MissileCapacity) : 0f;

    /// <summary>
    /// Startet eine Rakete (Schiff <paramref name="shooter"/> oder Station <paramref name="fromStation"/>) auf ein Schiff oder eine Station. Kein Zufallszug.
    /// Flugstrecke = <paramref name="distance"/>, Tempo aus <c>missileSpeed</c>.
    /// </summary>
    public static MissileFlight Launch(Universe u, Encounter e, Ship? shooter, Station? fromStation, int side, Module m, in WeaponProfile w,
        ShipId target, StationId targetStation, float distance, Vec2 from)
    {
        if (shooter is not null && u.Rules.AmmoActive)
            shooter.Systems.MissilesMissing = MathF.Min(shooter.Stats.MissileCapacity, shooter.Systems.MissilesMissing + 1f);
        float speed = MathF.Max(1f, m.Stat("missileSpeed"));
        var f = new MissileFlight
        {
            Shooter = shooter?.Id ?? default, FromStation = fromStation?.Id ?? default, Owner = shooter?.Owner ?? fromStation?.Owner ?? default, Side = side, Module = m.Key,
            Target = target, Station = targetStation, Left = MathF.Max(0f, distance), Total = MathF.Max(0f, distance), Speed = speed, From = from,
        };
        e.Missiles.Add(f);
        e.Shots++;
        u.Events.Publish(new MissileLaunched(e.Id, e.Sector, f.Shooter, fromStation?.Id ?? default, target, targetStation, m.Key,
            f.Total / speed * SimClock.TicksPerSecond));
        return f;
    }

    /// <summary>Raketen eines Gefechts fliegen weiter; angekommene werden in Abschussreihenfolge aufgelöst.</summary>
    public static void Step(Universe u, Encounter e, float seconds)
    {
        if (e.Missiles.Count == 0) return;
        foreach (var f in e.Missiles.ToList())
        {
            f.Left -= f.Speed * seconds;
            if (f.Left > 0f) continue;
            e.Missiles.Remove(f);
            Resolve(u, e, f);
        }
    }

    static void Resolve(Universe u, Encounter e, MissileFlight f)
    {
        var r = u.Rules;
        if (!u.Modules.TryGetValue(f.Module, out var m)) return;
        var w = WeaponProfile.Of(m);
        if (f.Station.Value != 0)
        {
            if (!u.Stations.TryGetValue(f.Station, out var st) || StationCombat.IsDown(u, st))
            { u.Events.Publish(new MissileResolved(e.Id, e.Sector, f.Shooter, default, f.Station, "lost", 0f, 0f)); return; }
            float pd = Math.Clamp(StationCombat.DefenseOf(u, st)?.PointDefense ?? 0f, 0f, r.PointDefenseMax);
            if (u.Rng.NextDouble() < pd) { u.Events.Publish(new MissileResolved(e.Id, e.Sector, f.Shooter, default, st.Id, "intercepted", 0f, 0f)); return; }
            var hit = Hit.Roll(u.Rng, w, StationCombat.Profile(u, st), 0f, 1f, r);
            float hull = StationCombat.Apply(u, e, st, hit, f.Owner);
            if (hit.Hit) e.Hits++;
            u.Events.Publish(new MissileResolved(e.Id, e.Sector, f.Shooter, default, f.Station, hit.Hit ? "hit" : "miss", hit.ShieldDamage, hull));
            return;
        }
        var tp = e.Of(f.Target);
        if (tp is not { Active: true } || !u.Ships.TryGetValue(f.Target, out var target))
        { u.Events.Publish(new MissileResolved(e.Id, e.Sector, f.Shooter, f.Target, default, "lost", 0f, 0f)); return; }
        float pdShip = Math.Clamp(target.Stats.PointDefense, 0f, r.PointDefenseMax);
        if (u.Rng.NextDouble() < pdShip) { u.Events.Publish(new MissileResolved(e.Id, e.Sector, f.Shooter, f.Target, default, "intercepted", 0f, 0f)); return; }
        var profile = new TargetProfile(target.ShieldNow, target.Stats.Armor, tp.Speed, Gunnery.Signature(u, target));
        var res = Hit.Roll(u.Rng, w, profile, 0f, 1f, r);
        float dealt = Hit.Apply(u, target, res);
        if (dealt > 0f) ModuleDamage.OnHullHit(u, target, dealt);
        if (res.Hit) e.Hits++;
        u.Events.Publish(new MissileResolved(e.Id, e.Sector, f.Shooter, f.Target, default, res.Hit ? "hit" : "miss", res.ShieldDamage, dealt));
        if (target.Hull <= 0f && tp.Active)
            Engage.Destroy(u, e, tp, target, f.FromStation.Value != 0   // Runde 6: Station ausdrücklich gemerkt (vorher an Schützen-Id 0 erkannt)
                ? $"Stationsrakete ({(u.Stations.TryGetValue(f.FromStation, out var fs) ? fs.Name : u.ActorName(f.Owner))})"
                : u.Ships.TryGetValue(f.Shooter, out var by) ? $"Rakete: {CombatSense.Who(u, by)}" : $"Rakete: {u.ActorName(f.Owner)}");   // Runde 5: der letzte Schütze
    }

    /// <summary>Raketen aus dem Lager einer Station nachladen (Marktpreis, höchstens Bestand und was der Zahler hat). Liefert die Einheiten.</summary>
    public static float AtStation(Universe u, Ship ship, Station st, ICreditAccount payer, ActorId payerActor, bool treasury, float maxUnits = float.MaxValue)
    {
        float missing = MathF.Min(Missing(u, ship), MathF.Max(0f, maxUnits));
        if (missing <= 0f || MissileGood(u) is not { } good || !st.Market.TryGetValue(good.Id, out var entry) || entry.Amount < 1f) return 0f;
        float price = entry.Price > 0f ? entry.Price : good.BasePrice;
        float units = MathF.Floor(MathF.Min(missing, entry.Amount));
        if (price > 0f) units = MathF.Min(units, MathF.Floor(MathF.Max(0f, payer.Credits) / price));
        if (units < 1f) return 0f;
        float cost = units * price;
        entry.Amount -= units;
        st.Credits += cost;
        payer.Credits -= cost;
        ship.Systems.MissilesMissing = MathF.Max(0f, ship.Systems.MissilesMissing - units);
        if (treasury && cost > 0f) u.Events.Publish(new TreasuryUpkeep(payerActor, "missiles", cost, 0f));
        return units;
    }

    /// <summary>Ohne Lager zum Basispreis × <see cref="EconomyRules.AmmoBaseMarkup"/> (Geldsenke), bezahlt aus der Kasse des Besitzers (Kriegsschiffe an der Basis).</summary>
    public static float AtBase(Universe u, Ship ship, ICreditAccount payer, ActorId payerActor)
    {
        float missing = Missing(u, ship);
        if (missing <= 0f || MissileGood(u) is not { } good) return 0f;
        float price = good.BasePrice * MathF.Max(0f, u.Rules.AmmoBaseMarkup);
        float units = price > 0f ? MathF.Min(missing, MathF.Max(0f, payer.Credits) / price) : missing;
        if (units <= 0.001f) return 0f;
        float cost = units * price;
        payer.Credits -= cost;
        ship.Systems.MissilesMissing = MathF.Max(0f, ship.Systems.MissilesMissing - units);
        if (cost > 0f) u.Events.Publish(new TreasuryUpkeep(payerActor, "missiles", cost, 0f));
        return units;
    }

    /// <summary>An der Heimatbasis: erst aus deren Lager, der Rest ohne Lager — aus der Kasse des Besitzers (nur mit <see cref="EconomyRules.Warfare"/>).</summary>
    public static void AtHome(Universe u, Ship ship, Station? home)
    {
        if (!u.Rules.Warfare || Missing(u, ship) <= 0f || !u.Actors.TryGetValue(ship.Owner, out var owner)) return;
        if (home is not null) AtStation(u, ship, home, owner.Treasury, owner.Id, true);
        AtBase(u, ship, owner.Treasury, owner.Id);
    }

    /// <summary>Position einer Rakete im räumlichen Gefecht (Darstellung): vom Start zum aktuellen Ziel nach dem Fortschritt.</summary>
    public static Vec2 PositionOf(Universe u, Encounter e, MissileFlight f)
    {
        Vec2 to = f.Station.Value != 0 && u.Stations.TryGetValue(f.Station, out var st) ? st.Position
            : e.Of(f.Target) is { } tp ? tp.Pos : f.From;
        float k = f.Total > 0f ? Math.Clamp(1f - f.Left / f.Total, 0f, 1f) : 1f;
        return f.From + (to - f.From) * k;
    }
}
