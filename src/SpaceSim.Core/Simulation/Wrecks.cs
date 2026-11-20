using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 26 (KAMPF.md 4.4): Wracks und Frachtbehälter als gespeicherte Objekte im Sektor.
/// <list type="bullet">
/// <item><b>Wrack:</b> Ein zerstörtes Schiff (jeder Abgang mit Grund „zerstört“ über <see cref="Departures.RemoveShip"/>) hinterlässt
/// <see cref="EconomyRules.WreckCargoShare"/> seiner Fracht (ohne Zufall) und einzelne Module: je eingebautem Modul genau zwei Zufallszüge — überlebt mit
/// <see cref="EconomyRules.WreckModuleChance"/>, Restzustand = Zustand beim Abschuss × U[<see cref="EconomyRules.WreckConditionMin"/>, 1]. Verfällt nach
/// <see cref="EconomyRules.WreckTicks"/>.</item>
/// <item><b>Frachtbehälter:</b> abgeworfene Fracht eines sich ergebenden Opfers (wenn der Spieler sie nimmt) und überzählige Fracht nach einem
/// Frachtmodul-Ausfall; verfällt nach <see cref="EconomyRules.ContainerTicks"/>.</item>
/// </list>
/// Bergen: Fracht nimmt jedes Schiff in Reichweite (<see cref="EconomyRules.SalvageRange"/>; der Spieler über den Traktorstrahl), Module nur mit
/// <see cref="Capability.Salvage"/> (Bergungsgreifer) und nur ab <see cref="EconomyRules.SalvageMinCondition"/> Restzustand — schlechtere Module sind Schrott.
/// Ein geborgenes Modul liegt als Modulware im Frachtraum und zählt dort wie neu (die Mindestschwelle ist der Wertabschlag: Schrott bleibt liegen).
/// NPC: Kaperer nehmen die Fracht des Wracks ihres Opfers mit (Hehlerei wie Beute); sonst verfällt das Wrack.
/// </summary>
public static class Wrecks
{
    /// <summary>Wracks und Behälter eines Sektors (Entstehungsreihenfolge).</summary>
    public static IEnumerable<Wreck> In(Universe u, SectorId sector) => u.Wrecks.Where(w => w.Sector == sector);

    public static Wreck? Get(Universe u, int id) => u.Wrecks.Find(w => w.Id == id);

    /// <summary>Wrack aus einem zerstörten Schiff (vor dem Entfernen aufrufen). null = Schalter aus oder nichts zu holen (dann ziehen nur die Modulzüge).</summary>
    public static Wreck? FromShip(Universe u, Ship ship)
    {
        var r = u.Rules;
        if (!r.DamageActive) return null;
        var at = Combat.PlayerCombat.PositionOf(u, ship.Id);
        var w = new Wreck
        {
            Kind = WreckKind.Wreck, Sector = at?.Sector ?? ship.Sector, Position = at?.Pos ?? ship.Position, From = ship.Id,
            Name = $"Wrack: {(u.ShipModels.TryGetValue(ship.ModelKey, out var mdl) ? mdl.Name : ship.Role.ToString())} #{ship.Id.Value}",
            CreatedTick = u.Tick, ExpiresTick = u.Tick + r.WreckTicks,
        };
        float share = Math.Clamp(r.WreckCargoShare, 0f, 1f);
        foreach (var g in ship.Cargo.Keys.OrderBy(k => k.Value))
        {
            float a = ship.Cargo[g] * share;
            if (a > 0.001f) w.Cargo[g] = a;
        }
        for (int slot = 0; slot < ship.Fit.Count; slot++)
        {
            if (ship.Fit[slot] is not { } key) continue;
            double survive = u.Rng.NextDouble(), rest = u.Rng.NextDouble();
            if (survive >= r.WreckModuleChance || !u.Modules.ContainsKey(key)) continue;
            float cond = ship.ConditionOf(slot) * (r.WreckConditionMin + (1f - r.WreckConditionMin) * (float)rest);
            w.Modules.Add(new WreckModule { Key = key, Condition = Math.Clamp(cond, 0f, 1f) });
        }
        if (w.IsEmpty) return null;
        return Add(u, w);
    }

    /// <summary>Frachtbehälter mit den angegebenen Waren (ohne Schalter: die Fracht geht verloren, null).</summary>
    public static Wreck? Drop(Universe u, SectorId sector, Vec2 pos, IReadOnlyDictionary<GoodId, float> cargo, ShipId from, string name)
    {
        if (!u.Rules.DamageActive) return null;
        var w = new Wreck
        {
            Kind = WreckKind.Container, Sector = sector, Position = pos, From = from, Name = name, CreatedTick = u.Tick, ExpiresTick = u.Tick + u.Rules.ContainerTicks,
        };
        foreach (var (g, a) in cargo.OrderBy(k => k.Key.Value)) if (a > 0.001f) w.Cargo[g] = a;
        if (w.IsEmpty) return null;
        return Add(u, w);
    }

    static Wreck Add(Universe u, Wreck w)
    {
        w.Id = u.NextWreckId++;
        u.Wrecks.Add(w);
        u.Events.Publish(new WreckCreated(w.Id, w.Sector, w.Kind, w.From, w.CargoUnits, w.Modules.Count));
        return w;
    }

    /// <summary>Warum dieses Schiff an dieses Wrack (noch) nicht herankommt (null = in Reichweite).</summary>
    public static string? ReachIssue(Universe u, Ship ship, Wreck w)
    {
        if (ship.Transit is not null) return "Unterwegs.";
        if (ship.DockedAt.Value != 0) return "Angedockt.";
        if (ship.Sector != w.Sector) return "Nicht im selben Sektor.";
        if (Vec2.Distance(ship.Position, w.Position) > u.Rules.SalvageRange) return $"Zu weit weg (Reichweite {u.Rules.SalvageRange:0}).";
        return null;
    }

    /// <summary>
    /// Fracht aus einem Wrack/Behälter in den Laderaum ziehen (Traktorstrahl): höchstens <paramref name="maxUnits"/> und was noch passt (Frachtform-Regel), Waren
    /// in Id-Reihenfolge. Liefert die bewegten Einheiten (0 = nichts; Grund in <paramref name="issue"/>).
    /// </summary>
    public static float TakeCargo(Universe u, Ship ship, Wreck w, float maxUnits, out string? issue)
    {
        issue = ReachIssue(u, ship, w);
        if (issue is not null) return 0f;
        float moved = 0f, budget = MathF.Max(0f, maxUnits);
        foreach (var g in w.Cargo.Keys.OrderBy(k => k.Value).ToList())
        {
            if (budget <= 1e-4f) break;
            float fit = ship.FreeUnits(u, g);
            float take = MathF.Min(MathF.Min(w.Cargo[g], fit), budget);
            if (take <= 1e-4f) continue;
            ship.Cargo[g] = ship.Cargo.GetValueOrDefault(g) + take;
            float left = w.Cargo[g] - take;
            if (left <= 1e-3f) w.Cargo.Remove(g); else w.Cargo[g] = left;
            budget -= take;
            moved += take;
            u.Events.Publish(new WreckSalvaged(w.Id, ship.Id, g, take, null, 0f, !ship.IsPlayer));
        }
        if (moved <= 0f) issue = w.Cargo.Count == 0 ? "Keine Fracht mehr im Wrack." : "Kein Platz im Frachtraum.";
        RemoveIfEmpty(u, w);
        return moved;
    }

    /// <summary>Warum dieses Modul nicht geborgen werden kann (null = es geht).</summary>
    public static string? ModuleIssue(Universe u, Ship ship, Wreck w, int index)
    {
        if (!ship.Has(Capability.Salvage)) return "Bergen braucht einen Bergungsgreifer.";
        if (ReachIssue(u, ship, w) is { } reach) return reach;
        if (index < 0 || index >= w.Modules.Count) return "Kein solches Modul im Wrack.";
        var m = w.Modules[index];
        if (m.Condition < u.Rules.SalvageMinCondition) return $"Schrott (Zustand {m.Condition * 100f:0} %).";
        if (u.FindGood(m.Key) is not { } good) return "Modul ist keine Ware.";
        if (ship.FreeUnits(u, good.Id) < 1f) return "Kein Platz im Frachtraum.";
        return null;
    }

    /// <summary>Bestes bergbares Modul (höchster Basispreis, dann Index) oder -1.</summary>
    public static int BestModule(Universe u, Ship ship, Wreck w)
    {
        int best = -1;
        float bestValue = -1f;
        for (int i = 0; i < w.Modules.Count; i++)
        {
            if (ModuleIssue(u, ship, w, i) is not null) continue;
            float v = u.FindGood(w.Modules[i].Key)?.BasePrice ?? 0f;
            if (v > bestValue) { best = i; bestValue = v; }
        }
        return best;
    }

    /// <summary>Ein Modul bergen: eine Einheit der Modulware in den Frachtraum. false = <see cref="ModuleIssue"/>.</summary>
    public static bool TakeModule(Universe u, Ship ship, Wreck w, int index, out string message)
    {
        if (ModuleIssue(u, ship, w, index) is { } issue) { message = issue; return false; }
        var m = w.Modules[index];
        var good = u.FindGood(m.Key)!;
        ship.Cargo[good.Id] = ship.Cargo.GetValueOrDefault(good.Id) + 1f;
        w.Modules.RemoveAt(index);
        u.Events.Publish(new WreckSalvaged(w.Id, ship.Id, good.Id, 1f, m.Key, m.Condition, !ship.IsPlayer));
        message = $"{good.Name} geborgen (Zustand {m.Condition * 100f:0} %).";
        RemoveIfEmpty(u, w);
        return true;
    }

    /// <summary>
    /// Kaperer nimmt die Fracht des Wracks seines Opfers mit: Basiswert × <see cref="EconomyRules.FenceShare"/> in die Piratenkasse (gedeckelt wie die Beute),
    /// die Fracht verschwindet aus dem Wrack. Liefert den Basiswert der Fracht.
    /// </summary>
    public static float LootByRaider(Universe u, Ship raider, Wreck w)
    {
        var r = u.Rules;
        if (!r.RaidersLootWrecks) return 0f;
        float value = 0f;
        foreach (var g in w.Cargo.Keys.OrderBy(k => k.Value).ToList())
        {
            value += w.Cargo[g] * u.Goods[g].BasePrice;
            u.Events.Publish(new WreckSalvaged(w.Id, raider.Id, g, w.Cargo[g], null, 0f, true));
            Trading.BlackMarket.Stash(u, raider.Owner, g, w.Cargo[g]);   // Schwarzmarkt
        }
        w.Cargo.Clear();
        if (value > 0f && u.Actors.TryGetValue(raider.Owner, out var pirates))
        {
            float fenced = value * r.FenceShare;
            if (Bandits.FenceCap(u, pirates) is > 0f and var fcap) fenced = MathF.Min(fenced, MathF.Max(0f, fcap - pirates.Treasury.Credits));   // Ausbau Punkt 17: Deckel je Akteur
            if (fenced > 0f)
            {
                pirates.Treasury.Credits += fenced;
                u.Events.Publish(new TreasuryIncome(pirates.Id, "fence", fenced));
            }
        }
        RemoveIfEmpty(u, w);
        return value;
    }

    /// <summary>Leeres Wrack sofort entfernen.</summary>
    public static void RemoveIfEmpty(Universe u, Wreck w)
    {
        if (w.IsEmpty) u.Wrecks.Remove(w);
    }
}

/// <summary>Phase 26: Wracks und Frachtbehälter verfallen nach ihrer Frist (Ereignis <see cref="WreckExpired"/>); leere werden entfernt.</summary>
public sealed class WreckSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        if (u.Wrecks.Count == 0) return;
        for (int i = u.Wrecks.Count - 1; i >= 0; i--)
        {
            var w = u.Wrecks[i];
            if (w.IsEmpty) { u.Wrecks.RemoveAt(i); continue; }
            if (u.Tick < w.ExpiresTick) continue;
            u.Events.Publish(new WreckExpired(w.Id, w.Kind, w.CargoUnits, w.Modules.Count));
            u.Wrecks.RemoveAt(i);
        }
    }
}
