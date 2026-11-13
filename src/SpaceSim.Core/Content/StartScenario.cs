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
        if (sc.BonusSlots is { } bonus)
            foreach (var type in bonus)
            {
                if (!u.SlotTypes.ContainsKey(type)) throw new WorldFormatException($"Startpaket '{key}': unbekannte Slotart '{type}'");
                player.Slots.Add(new ClassSlot { Type = type, Size = SlotSize.S });
                player.Fit.Add(null);
            }
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
            player.Position = st.DockPoint(player.Id) + (st.DockPoint(player.Id) - st.Position).Normalized() * (sc.Distance ?? 20f);
            player.Position = Combat.CombatSpace.Clear(u, player.Sector, player.Position);
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
        ApplyCombat(u, player, sc, key);
        if (sc.Outlaw && sc.Raiders <= 0 && u.ShipsById().FirstOrDefault(ThreatMap.IsRaider) is { } raider)
        {
            if (raider.Sector != player.Sector) u.MoveShip(raider, player.Sector);
            raider.Position = player.Position + new Vec2(0f, -70f);
            raider.DockedAt = default;
            raider.Order = new PatrolOrder(player.Sector);
            raider.DutySector = player.Sector;
            raider.NextThinkTick = u.Tick + u.Rules.PirateLurkTicks;
        }
        }

    /// <summary>
    /// Gerätetest Phase 27 („Gefechtstest“): Rudel in Waffenreichweite mit angekündigtem Überfall (zu zweit stärker als der Jäger — es greift an, statt zu fliehen; ein beschädigter Antrieb würde es zur Reparatur heimschicken), ein Händler frei im Raum,
    /// eine Streife als Verband, Fahndung des Spielers. Feste Abstände, kein Zufall.
    /// </summary>
    static void ApplyCombat(Universe u, Ship player, ScenarioDef sc, string key)
    {
        var sector = u.Sectors[player.Sector];
        if (sc.Raiders > 0)
        {
            var group = u.ShipsById().Where(ThreatMap.IsRaider).GroupBy(r => r.Owner).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Value).FirstOrDefault()
                        ?? throw new WorldFormatException($"Startpaket '{key}': keine Kaperschiffe in der Welt");
            var pack = group.Take(sc.Raiders).ToList();
            bool later = sc.ReinforceAfterTicks > 0;
            for (int i = 0; i < pack.Count; i++)
            {
                var r = pack[i];
                if (r.Transit is not null) u.FinishTransit(r);
                if (r.Sector != player.Sector) u.MoveShip(r, player.Sector);
                // Gerätetest Phase 28: der Anführer vor dem Bug (Blickrichtung −y) außerhalb der eigenen Waffenreichweite, die übrigen weit dahinter (Verstärkung später)
                r.Position = Combat.CombatSpace.Clear(u, player.Sector, player.Position + new Vec2(0f, i == 0 ? -120f : -320f - 30f * i));
                r.Hull = r.MaxHull * Math.Clamp(i == 0 || !later ? sc.RaiderHull : sc.ReinforceHull, 0.05f, 1f);
                if (i == 0 && sc.LeadWithoutShield && r.Fit.FindIndex(m => m != null && u.Modules.TryGetValue(m, out var mod) && mod.Slot == "shield") is var sh and >= 0)
                {
                    r.Fit[sh] = null;   // zweiter Gerätetest Phase 28: der Anführer ohne Schild
                    r.RecomputeStats(u);
                    r.Engagement = Engagement.Attack;   // und er flieht nicht (sonst entkäme er dem Menschen unter halber Hülle sofort)
                }
                r.DockedAt = default;
                r.Order = new PatrolOrder(player.Sector);
                r.DutySector = player.Sector;
                r.NextThinkTick = u.Tick + 100_000;
            }
            if (!later && pack.Count >= 2 && Squadrons.Create(u, pack[0], "Rudel", out _) is { } sq)
                for (int i = 1; i < pack.Count; i++) Squadrons.Join(u, sq, pack[i], out _);
            // das Rudel lauert hier (Befehl wie die Piratengruppe ihn gibt), statt heimzufliegen
            if (u.Rules.FleetCombat)
            {
                Commands.Give(u, pack[0], new LurkCommand(player.Sector, pack[0].Position, u.Tick + 20_000), out _, ignoreCapacity: true);
                if (!later) for (int i = 1; i < pack.Count; i++) Commands.Give(u, pack[i], new FollowCommand(pack[0].Id), out _, ignoreCapacity: true);
            }
            if (sc.RaidAfterTicks > 0)
            {
                u.PendingEncounters.Add(new PendingEncounter { Kind = EncounterKind.Raid, Source = pack[0].Id, Target = player.Id, Sector = player.Sector, DueTick = u.Tick + sc.RaidAfterTicks });
                // zweiter Gerätetest Phase 28: Verstärkung als eigener angekündigter Überfall, der dem laufenden Gefecht beitritt (PirateSystem.ResolvePending)
                if (later)
                    for (int i = 1; i < pack.Count; i++)
                    {
                        pack[i].Position = Combat.CombatSpace.Clear(u, player.Sector, player.Position + new Vec2(0f, 380f + 30f * i));   // hinter dem Spieler, weit weg
                        // lauert dort (ohne Befehl schickte die Piratengruppe ihn heim — er kam repariert zurück)
                        if (u.Rules.FleetCombat) Commands.Give(u, pack[i], new LurkCommand(player.Sector, pack[i].Position, u.Tick + 20_000), out _, ignoreCapacity: true);
                        u.PendingEncounters.Add(new PendingEncounter { Kind = EncounterKind.Raid, Source = pack[i].Id, Target = player.Id, Sector = player.Sector,
                            DueTick = u.Tick + sc.RaidAfterTicks + sc.ReinforceAfterTicks + 20 * (i - 1) });
                    }
            }
        }
        if (sc.Trader && u.ShipsById().FirstOrDefault(s => s.Role == ShipRole.Trader && s.Controller == Controller.Ai && s.HasModel) is { } trader)
        {
            if (trader.Transit is not null) u.FinishTransit(trader);
            trader.Order = null; trader.Phase = TradePhase.Idle;
            if (trader.Sector != player.Sector) u.MoveShip(trader, player.Sector);
            trader.Position = Combat.CombatSpace.Clear(u, player.Sector, player.Position + new Vec2(40f, 10f));
            trader.DockedAt = default;
            trader.NextThinkTick = u.Tick + 2_000;
            if (u.FindGood("food") is { } food) trader.Cargo[food.Id] = MathF.Min(30f, trader.FreeUnits(u, food.Id));
        }
        if (sc.PoliceSquadron && u.Factions.ContainsKey(sector.Owner))
        {
            var cops = SecuritySystem.PoliceOf(u, sector.Owner).Take(2).ToList();
            for (int i = 0; i < cops.Count; i++)
            {
                var c = cops[i];
                if (c.Transit is not null) u.FinishTransit(c);
                c.Command = null; c.Order = null;
                if (c.Sector != player.Sector) u.MoveShip(c, player.Sector);
                c.Position = Combat.CombatSpace.Clear(u, player.Sector, player.Position + new Vec2(-50f, 20f + 15f * i));
                c.DockedAt = default;
                c.DutySector = player.Sector;
            }
            if (u.Rules.FleetCombat) PoliceCommands.Organize(u, sector.Owner, SecuritySystem.PoliceOf(u, sector.Owner));
        }
        if (sc.Wanted > 0 && u.Factions.ContainsKey(sector.Owner) && u.Actors.TryGetValue(player.Owner, out var me))
        {
            Wanted.Report(u, player.Owner, sector.Owner, "assault");
            float heat = sc.Wanted < u.Rules.WantedLevels.Count + 1 && sc.Wanted >= 1 ? u.Rules.WantedLevels[Math.Min(sc.Wanted, u.Rules.WantedLevels.Count) - 1] + 0.1f : 3.1f;
            Wanted.SetHeat(u, me, sector.Owner, heat, 500f);
        }
    }
}
