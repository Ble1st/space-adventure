using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Content;

/// <summary>
/// Gerätetest (Phase 26): wendet ein Testszenario (<see cref="ScenarioDef"/>) auf die frisch gebaute Welt an — eigener Fit, Start neben einer Station mit
/// Reparatur/Werft/Ausrüster/Munition, beschädigtes und brennendes Modul, Wrack in Reichweite, Kaperschiff in Sichtweite, Fracht. Deterministisch (feste
/// Abstände, kein Zufall). Ungültige Angaben werfen <see cref="WorldFormatException"/> mit dem Paketnamen.
/// </summary>
public static class StartScenario
{
    public static void Apply(Universe u, Ship player, ScenarioDef sc, string key)
    {
        if (sc.Fit is { } fit)
        {
            var model = u.ShipModels[player.ModelKey];
            var cls = u.ShipClasses[model.ClassKey];
            if (FitRules.Check(u.SlotTypes, u.Modules, cls, player.Slots, fit) is { } issue)
                throw new WorldFormatException($"Startpaket '{key}': Fit ungültig — {FitRules.Describe(issue)}: {issue.Message}");
            player.Fit = new List<string?>(fit);
            player.RecomputeStats(u);
            player.CargoCapacity = player.Stats.Cargo;
            player.Hull = player.MaxHull;
        }
        if (sc.Station is { } typeKey)
        {
            var st = u.StationsById().Where(s => s.TypeKey == typeKey).OrderBy(s => s.Sector == player.Sector ? 0 : 1).ThenBy(s => s.Id.Value).FirstOrDefault()
                     ?? throw new WorldFormatException($"Startpaket '{key}': keine Station vom Typ '{typeKey}'");
            if (player.Sector != st.Sector) u.MoveShip(player, st.Sector);
            u.PlayerSector = st.Sector;
            player.Position = st.DockPoint(player.Id) + (st.DockPoint(player.Id) - st.Position).Normalized() * 20f;
        }
        int SlotOf(string module) => player.Fit.IndexOf(module) is var i and >= 0 ? i
            : throw new WorldFormatException($"Startpaket '{key}': Modul '{module}' ist nicht eingebaut");
        if (sc.Damage is { } dmg)
            foreach (var (m, c) in dmg.OrderBy(k => k.Key, StringComparer.Ordinal)) Combat.ModuleDamage.SetCondition(u, player, SlotOf(m), Math.Clamp(c, 0f, 1f));
        if (sc.Fire is { } fire) Combat.ModuleDamage.Ignite(u, player, SlotOf(fire));
        if (sc.Cargo is { } cargo)
            foreach (var (g, a) in cargo.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var good = u.FindGood(g) ?? throw new WorldFormatException($"Startpaket '{key}': unbekannte Ware '{g}'");
                player.Cargo[good.Id] = player.Cargo.GetValueOrDefault(good.Id) + MathF.Min(a, player.FreeUnits(u, good.Id));
            }
        if (sc.Wreck && u.Rules.DamageActive)
        {
            var loot = new Dictionary<GoodId, float>();
            if (u.FindGood("food") is { } food) loot[food.Id] = 8f;
            var w = Wrecks.Drop(u, player.Sector, player.Position + new Vec2(14f, 0f), loot, default, "Wrack: Testszenario");
            if (w is not null)
            {
                w.Kind = WreckKind.Wreck;
                if (u.Modules.ContainsKey("reactor_mk1")) w.Modules.Add(new WreckModule { Key = "reactor_mk1", Condition = 0.8f });
                w.ExpiresTick = u.Tick + u.Rules.WreckTicks * 4;
            }
        }
        if (sc.Outlaw && u.ShipsById().FirstOrDefault(ThreatMap.IsRaider) is { } raider)
        {
            if (raider.Sector != player.Sector) u.MoveShip(raider, player.Sector);
            raider.Position = player.Position + new Vec2(0f, -70f);
            raider.DockedAt = default;
            raider.Order = new PatrolOrder(player.Sector);
            raider.DutySector = player.Sector;
            raider.NextThinkTick = u.Tick + u.Rules.PirateLurkTicks;
        }
    }
}
