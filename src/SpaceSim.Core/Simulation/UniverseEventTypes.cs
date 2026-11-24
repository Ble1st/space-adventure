using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Erzschwemme: in einem Gürtel des Sektors erscheinen <see cref="EconomyRules.OreSurgeMinRocks"/>–<see cref="EconomyRules.OreSurgeMaxRocks"/> zusätzliche Felsen
/// über die Gürtelregeln (<see cref="BeltPlacement.TryCreate"/>), mit Erz × <see cref="EconomyRules.OreSurgeRichFactor"/>. Spieler und NPC-Schürfer bauen sie
/// ab wie jeden Gürtelfelsen. Am Ende verschwinden die unberührten (voller Vorrat, kein Anker daran); angebrochene bleiben als normale Gürtelfelsen.
/// </summary>
public sealed class OreSurgeEvent : IUniverseEventType
{
    public const string KeyValue = "ore_surge";
    public string Key => KeyValue;
    public string Name => "Erzschwemme";
    public float Weight(EconomyRules r) => r.OreSurgeWeight;

    static IEnumerable<ResourceField> Belts(Universe u, Sector s) =>
        s.Fields.Where(f => u.AsteroidClasses.TryGetValue(f.ClassKey, out var c) && c.Yields.Count > 0);

    public bool Possible(Universe u, Sector s) => Belts(u, s).Any();

    public bool Start(Universe u, UniverseEvent e, DeterministicRng rng, Vec2? at)
    {
        var s = u.Sectors[e.Sector];
        var belts = Belts(u, s).ToList();
        var f = at is { } p ? belts.OrderBy(b => Vec2.Distance(b.Position, p)).First() : belts[rng.NextInt(belts.Count)];
        var r = u.Rules;
        int lo = Math.Max(1, r.OreSurgeMinRocks), n = lo + rng.NextInt(Math.Max(lo, r.OreSurgeMaxRocks) - lo + 1);
        (Vec2, float)? avoid = s.Id == u.PlayerSector && u.Ships.TryGetValue(u.PlayerShip, out var me) ? (me.Position, AsteroidRespawnSystem.PlayerSafeRadius) : null;
        float rich = MathF.Max(1f, r.OreSurgeRichFactor);
        for (int i = 0; i < n; i++)
        {
            var rock = BeltPlacement.TryCreate(u, s, f, rng.Range, avoid);
            if (rock is null) continue;
            rock.Ore *= rich;
            rock.MaxOre = rock.Ore;
            s.Asteroids.Add(rock);
            e.Asteroids.Add(rock.Id);
            u.Events.Publish(new AsteroidRespawned(s.Id, rock.Id));
        }
        if (e.Asteroids.Count == 0) return false;
        e.Position = f.Position;
        e.Detail = u.AsteroidClasses[f.ClassKey].Name;
        return true;
    }

    public bool Exhausted(Universe u, UniverseEvent e)
    {
        var s = u.Sectors[e.Sector];
        return e.Asteroids.All(id => !s.Asteroids.Any(a => a.Id == id));
    }

    public void End(Universe u, UniverseEvent e)
    {
        var s = u.Sectors[e.Sector];
        foreach (var id in e.Asteroids)
        {
            var rock = s.Asteroids.Find(a => a.Id == id);
            if (rock is null || rock.Ore < rock.MaxOre - 0.01f) continue;   // abgebaut oder angebrochen: bleibt
            if (u.Ships.Values.Any(sh => sh.AnchorRock == id)) continue;     // jemand hängt mit Ankern daran
            s.Asteroids.Remove(rock);
        }
    }

    public string Title(Universe u, UniverseEvent e) => $"Erzschwemme: {e.Asteroids.Count} reiche Felsen im Gürtel ({e.Detail})";
}

/// <summary>
/// Frachtfund: <see cref="EconomyRules.CargoFindMinContainers"/>–<see cref="EconomyRules.CargoFindMaxContainers"/> treibende Frachtbehälter (<see cref="Wrecks"/>,
/// Art Container) mit zufälliger Ware (Rohstoff bis Endware) im Wert von <see cref="EconomyRules.CargoFindMinValue"/>–<see cref="EconomyRules.CargoFindMaxValue"/>
/// je Behälter; Traktorstrahl und NPC-Bergeschiffe holen sie. Behälter verfallen mit dem Ereignis.
/// </summary>
public sealed class CargoFindEvent : IUniverseEventType
{
    public const string KeyValue = "cargo_find";
    public string Key => KeyValue;
    public string Name => "Frachtfund";
    public float Weight(EconomyRules r) => r.CargoFindWeight;
    public bool Possible(Universe u, Sector s) => u.Rules.DamageActive && Goods(u).Count > 0;

    internal static List<Good> Goods(Universe u) =>
        u.Goods.Values.Where(g => g.BasePrice > 0f && g.Category is "ore" or "refined" or "component" or "end").OrderBy(g => g.Id.Value).ToList();

    public bool Start(Universe u, UniverseEvent e, DeterministicRng rng, Vec2? at)
    {
        var s = u.Sectors[e.Sector];
        var r = u.Rules;
        var center = at ?? UniverseEvents.FreeSpot(u, s, rng, 20f);
        if (center is not { } c) return false;
        var goods = Goods(u);
        int lo = Math.Max(1, r.CargoFindMinContainers), n = lo + rng.NextInt(Math.Max(lo, r.CargoFindMaxContainers) - lo + 1);
        var names = new List<string>();
        for (int i = 0; i < n; i++)
        {
            var g = goods[rng.NextInt(goods.Count)];
            float value = rng.Range(r.CargoFindMinValue, MathF.Max(r.CargoFindMinValue, r.CargoFindMaxValue));
            float units = MathF.Min(80f, MathF.Max(1f, MathF.Round(value / g.BasePrice)));
            var pos = i == 0 ? c : c + Vec2.FromAngle(rng.Range(0f, MathF.Tau)) * rng.Range(6f, 14f);
            var w = new Wreck { Kind = WreckKind.Container, Sector = s.Id, Position = pos, Name = $"Treibgut: {g.Name}", CreatedTick = u.Tick, ExpiresTick = e.EndTick };
            w.Cargo[g.Id] = units;
            e.Wrecks.Add(Wrecks.Add(u, w).Id);
            if (!names.Contains(g.Name)) names.Add(g.Name);
        }
        e.Position = c;
        e.Detail = string.Join(", ", names);
        return true;
    }

    public bool Exhausted(Universe u, UniverseEvent e) => e.Wrecks.All(id => Wrecks.Get(u, id) is null);
    public void End(Universe u, UniverseEvent e) => WreckFieldEvent.RemoveLeft(u, e);
    public string Title(Universe u, UniverseEvent e) => $"Frachtfund: {e.Wrecks.Count} treibende Behälter ({e.Detail})";
}

/// <summary>
/// Wrackfeld: nach einer „Schlacht“ treiben <see cref="EconomyRules.WreckFieldMinWrecks"/>–<see cref="EconomyRules.WreckFieldMaxWrecks"/> Wracks mit je 1–3
/// Modulen (Restzustand <see cref="EconomyRules.WreckFieldMinCondition"/>–<see cref="EconomyRules.WreckFieldMaxCondition"/>; unter
/// <see cref="EconomyRules.SalvageMinCondition"/> Schrott) und etwas Fracht; Bergungsgreifer (Spieler, NPC-Bergeschiffe) holen die Module.
/// </summary>
public sealed class WreckFieldEvent : IUniverseEventType
{
    public const string KeyValue = "wreck_field";
    public string Key => KeyValue;
    public string Name => "Wrackfeld";
    public float Weight(EconomyRules r) => r.WreckFieldWeight;
    public bool Possible(Universe u, Sector s) => u.Rules.DamageActive && Modules(u).Count > 0;

    static List<string> Modules(Universe u) => u.Modules.Keys.Where(k => u.FindGood(k) is not null).OrderBy(k => k, StringComparer.Ordinal).ToList();

    public bool Start(Universe u, UniverseEvent e, DeterministicRng rng, Vec2? at)
    {
        var s = u.Sectors[e.Sector];
        var r = u.Rules;
        var center = at ?? UniverseEvents.FreeSpot(u, s, rng, 40f);
        if (center is not { } c) return false;
        var mods = Modules(u);
        var goods = CargoFindEvent.Goods(u);
        int lo = Math.Max(1, r.WreckFieldMinWrecks), n = lo + rng.NextInt(Math.Max(lo, r.WreckFieldMaxWrecks) - lo + 1);
        int modules = 0;
        for (int i = 0; i < n; i++)
        {
            var pos = i == 0 ? c : c + Vec2.FromAngle(rng.Range(0f, MathF.Tau)) * rng.Range(8f, 35f);
            var w = new Wreck { Kind = WreckKind.Wreck, Sector = s.Id, Position = pos, Name = "Wrack: Gefechtsfeld", CreatedTick = u.Tick, ExpiresTick = e.EndTick };
            int m = 1 + rng.NextInt(3);
            for (int k = 0; k < m; k++)
                w.Modules.Add(new WreckModule { Key = mods[rng.NextInt(mods.Count)], Condition = rng.Range(r.WreckFieldMinCondition, MathF.Max(r.WreckFieldMinCondition, r.WreckFieldMaxCondition)) });
            if (goods.Count > 0 && rng.NextDouble() < 0.5)
            {
                var g = goods[rng.NextInt(goods.Count)];
                w.Cargo[g.Id] = MathF.Round(rng.Range(5f, 20f));
            }
            modules += m;
            e.Wrecks.Add(Wrecks.Add(u, w).Id);
        }
        e.Position = c;
        e.Detail = $"{modules} Module";
        return true;
    }

    public bool Exhausted(Universe u, UniverseEvent e) => e.Wrecks.All(id => Wrecks.Get(u, id) is null);
    public void End(Universe u, UniverseEvent e) => RemoveLeft(u, e);
    public string Title(Universe u, UniverseEvent e) => $"Wrackfeld: {e.Wrecks.Count} Wracks nach einem Gefecht ({e.Detail})";

    /// <summary>Übrige Wracks/Behälter des Ereignisses verschwinden (<see cref="WreckExpired"/>).</summary>
    internal static void RemoveLeft(Universe u, UniverseEvent e)
    {
        foreach (var id in e.Wrecks)
            if (Wrecks.Get(u, id) is { } w)
            {
                u.Events.Publish(new WreckExpired(w.Id, w.Kind, w.CargoUnits, w.Modules.Count));
                u.Wrecks.Remove(w);
            }
    }
}
