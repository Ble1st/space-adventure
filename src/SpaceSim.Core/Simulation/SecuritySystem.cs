using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Polizei der Fraktionen (Phase 11, FLOTTEN.md „Zuweisung“, „Polizeistufen, Zuständigkeit, Finanzierung“): Jede Fraktion hält Polizeischiffe
/// (Etikett <see cref="ShipRole.Police"/>, Modell <see cref="EconomyRules.PoliceModel"/>) mit Heimatbasis an einer ihrer Regierungsstationen.
/// Alle 100 Ticks zahlt die Kasse den Unterhalt, die Sollstärke folgt aus Territorium und Bedrohung der Lagekarte; fehlen Schiffe, kauft die Kasse
/// über die gemeinsame Kaufregel (<see cref="ShipPurchase"/>) an einer Werft, bei dauerhafter Überzahl wird abgebaut; die Streifen werden nach
/// Bedrohung und Verkehrswert auf die Sektoren des Territoriums verteilt (Zuständigkeit: nur eigenes Gebiet). Jede Streife fliegt Streifenpunkte
/// ihres Sektors ab (Gates, Stationen, Felder) und kehrt zur Reparatur und zum Tanken heim. Gesuchte Spieler (Stufe 3) werden abgefangen — mit
/// Vorwarnung; die Strafe wird eingezogen, Schmuggelware beschlagnahmt. Nebenbei regenerieren liegende NPC-Schiffe ihre Hülle (Überfallschäden).
/// </summary>
public sealed class SecuritySystem : ISimSystem
{
    /// <summary>Takt der Fraktionsentscheidungen (Unterhalt, Kauf/Abbau, Zuweisung) in Ticks.</summary>
    public const int ManageTicks = 100;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.SecurityActive) return;
        ResolvePending(u);
        if (u.Tick % ManageTicks < dt)
            foreach (var f in u.Factions.Keys.OrderBy(k => k.Value).ToList()) Manage(u, f, ManageTicks);
        foreach (var police in u.ShipsById().Where(ThreatMap.IsPolice).ToList()) StepPolice(u, police);
        foreach (var s in u.Ships.Values)
            if (s.Transit is null && s.Id != u.PlayerShip && !ThreatMap.IsPolice(s) && !ThreatMap.IsRaider(s) && s.Hull < s.MaxHull)
                s.Hull = MathF.Min(s.MaxHull, s.Hull + u.Rules.NpcHullRegenPerTick * dt);
        CheckWantedPlayer(u);
    }

    // ───────── Fraktion: Unterhalt, Soll, Kauf/Abbau, Zuweisung ─────────

    /// <summary>Polizeischiffe einer Fraktion (feste Reihenfolge).</summary>
    public static List<Ship> PoliceOf(Universe u, FactionId f) =>
        u.ShipsById().Where(s => ThreatMap.IsPolice(s) && u.FactionOf(s) == f).ToList();

    /// <summary>Sektoren, für die die Fraktion zuständig ist (Besitzer), feste Reihenfolge.</summary>
    public static List<Sector> Territory(Universe u, FactionId f) => u.Sectors.Values.Where(s => s.Owner == f).OrderBy(s => s.Id.Value).ToList();

    /// <summary>Sollstärke: aufgerundet Σ (Grundbedarf je Sektor + Bedrohung × Faktor), begrenzt; ohne Territorium 0.</summary>
    public static int Desired(Universe u, FactionId f)
    {
        var r = u.Rules;
        var terr = Territory(u, f);
        if (terr.Count == 0) return 0;
        float need = terr.Sum(s => r.PolicePerSector + s.Threat * r.PolicePerThreat);
        return Math.Clamp((int)MathF.Ceiling(need - 1e-4f), r.PoliceMinPerFaction, r.PoliceMaxPerFaction);
    }

    /// <summary>Phase 20: ungeglätteter Bedarf Σ (Grundbedarf je Sektor + Bedrohung × Faktor) über das Territorium (ohne Rundung und Grenzen).</summary>
    public static float RawNeed(Universe u, FactionId f) =>
        Territory(u, f).Sum(s => u.Rules.PolicePerSector + s.Threat * u.Rules.PolicePerThreat);

    /// <summary>
    /// Phase 20: geglätteten Bedarf nachführen (<see cref="Actor.PoliceNeed"/>, gleitendes Mittel über <see cref="EconomyRules.PoliceNeedWindowTicks"/>; beim
    /// ersten Mal der Augenblickswert) und zurückgeben.
    /// </summary>
    public static float SmoothNeed(Universe u, Actor actor, FactionId f, int ticks)
    {
        float raw = RawNeed(u, f);
        int window = u.Rules.PoliceNeedWindowTicks;
        if (actor.PoliceNeed < 0f || window <= 0) actor.PoliceNeed = raw;
        else actor.PoliceNeed += (raw - actor.PoliceNeed) * MathF.Min(1f, (float)ticks / window);
        return actor.PoliceNeed;
    }

    /// <summary>
    /// Phase 20: Kauf- und Abbau-Schwelle zum geglätteten Bedarf: gekauft wird, solange die Zahl unter ⌈Bedarf − <see cref="EconomyRules.PoliceBuyMargin"/>⌉ liegt,
    /// abgebaut, wenn sie über ⌈Bedarf + <see cref="EconomyRules.PoliceSellMargin"/> − 1⌉ liegt — beide innerhalb Mindest-/Höchstzahl. Rein.
    /// </summary>
    public static (int BuyBelow, int SellAbove) Thresholds(Universe u, float need)
    {
        var r = u.Rules;
        if (need <= 0f) return (0, 0);
        int buy = Math.Clamp((int)MathF.Ceiling(need - r.PoliceBuyMargin - 1e-4f), r.PoliceMinPerFaction, r.PoliceMaxPerFaction);
        int keep = Math.Clamp((int)MathF.Ceiling(need + r.PoliceSellMargin - 1f - 1e-4f), r.PoliceMinPerFaction, r.PoliceMaxPerFaction);
        return (buy, Math.Max(buy, keep));
    }

    /// <summary>Gewicht eines Sektors bei der Zuweisung: Grundbedarf + Bedrohung + Verkehrswert ÷ 20000 (Wert des Sektors).</summary>
    public static float PatrolWeight(Universe u, Sector s) =>
        u.Rules.PolicePerSector + s.Threat * u.Rules.PolicePerThreat + MathF.Max(0f, s.Traffic) / 20000f;

    static void Manage(Universe u, FactionId f, int ticks)
    {
        if (!u.Actors.TryGetValue(f, out var actor) || actor.Kind == ActorKind.Outlaw) return;
        var r = u.Rules;
        var police = PoliceOf(u, f);
        float upkeep = r.PoliceUpkeepPerTick * police.Count * ticks;
        if (upkeep > 0f)
        {
            float paid = MathF.Min(upkeep, MathF.Max(0f, actor.Treasury.Credits));
            actor.Treasury.Credits -= paid;
            u.Events.Publish(new TreasuryUpkeep(actor.Id, "police", paid, upkeep - paid));
        }
        // Phase 20: Kauf und Abbau folgen dem geglätteten Bedarf mit Hysterese. Vorher kaufte jeder Überfall (Bedrohung +1 → Soll 3) ein drittes Schiff, das
        // 6000 Ticks nach dem Kauf wieder abgebaut wurde, sobald die Bedrohung verfallen war (144 h: 344 gekauft, 344 abgebaut, 17,5 % der Kasseneinnahmen).
        float need = SmoothNeed(u, actor, f, ticks);
        var (buyBelow, sellAbove) = Thresholds(u, need);
        if (police.Count > sellAbove) { if (actor.PoliceSurplusSince < 0) actor.PoliceSurplusSince = u.Tick; }
        else actor.PoliceSurplusSince = -1;
        if (police.Count < buyBelow && u.Tick - actor.LastForceTick >= r.PoliceChangeCooldown)
        {
            if (Commission(u, f) is { } ship) police.Add(ship);
        }
        else if (police.Count > sellAbove && actor.PoliceSurplusSince >= 0 && u.Tick - actor.PoliceSurplusSince >= r.PoliceDecommissionTicks
                 && u.Tick - actor.LastForceTick >= r.PoliceChangeCooldown)
        {
            var gone = police.OrderBy(s => s.Transit is null ? 0 : 1).ThenByDescending(s => s.Id.Value).First();
            police.Remove(gone);
            u.RemoveShip(gone.Id, "decommissioned");
            actor.LastForceTick = u.Tick;
            actor.PoliceSurplusSince = -1;
            u.Events.Publish(new PoliceDecommissioned(f, gone.Id));
        }
        Assign(u, f, police);
    }

    /// <summary>
    /// Kauft ein Polizeischiff aus der Fraktionskasse (gemeinsame Kaufregel: Bausatz + Module aus dem Werftlager, Preis an die Werft), wenn eine erreichbare
    /// Werft das Modell liefern kann und danach <see cref="EconomyRules.PoliceReserve"/> in der Kasse bleibt. null = kein Kauf.
    /// </summary>
    public static Ship? Commission(Universe u, FactionId f)
    {
        var r = u.Rules;
        if (!u.Actors.TryGetValue(f, out var actor) || !u.ShipModels.TryGetValue(r.PoliceModel, out var model)
            || !u.ShipClasses.TryGetValue(model.ClassKey, out var cls) || ShipPurchase.Materials(u, model) is not { } needs) return null;
        var home = HomeBaseFor(u, f, null);
        if (home is null) return null;
        foreach (var yard in u.StationsById().Where(st => !st.IsBuildSite && ShipPurchase.SellsModel(u, st, model)).ToList())
        {
            if (!u.IsReachable(yard.Sector, home.Sector) || ShipPurchase.Missing(yard, needs).Count > 0) continue;
            float price = ShipPurchase.Price(u, yard, needs, ShipPurchase.AssemblyOf(u, yard));
            if (actor.Treasury.Credits < price + r.PoliceReserve) return null;
            ShipPurchase.Consume(yard, needs);
            yard.Credits += price;
            actor.Treasury.Credits -= price;
            var ship = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, ShipRole.Police, Controller.Ai, actor.Id, yard, 0f);
            ship.HomeBase = home.Id;
            ship.JobSince = u.Tick;
            u.AddShip(ship);
            actor.LastForceTick = u.Tick;
            u.Events.Publish(new TreasuryUpkeep(actor.Id, "police", price, 0f));
            u.Events.Publish(new PoliceCommissioned(f, ship.Id, yard.Id, price));
            return ship;
        }
        return null;
    }

    /// <summary>Heimatbasis für Polizei einer Fraktion: Polizeiwache, sonst Hauptquartier, sonst Sektor-HQ/Büro — im Sektor <paramref name="near"/> zuerst.</summary>
    public static Station? HomeBaseFor(Universe u, FactionId f, SectorId? near)
    {
        int Rank(Station st) => StationProtection.KindOf(u, st) switch
        {
            GovernmentKind.Police => 0, GovernmentKind.Headquarters => 1, GovernmentKind.SectorHq => 2, _ => 3,
        };
        // Phase 13: Forschungsstationen sind keine Heimatbasis der Polizei
        return u.Stations.Values.Where(st => !st.IsBuildSite && st.Owner == (ActorId)f && StationProtection.IsOfficial(u, st) && StationProtection.KindOf(u, st) != GovernmentKind.Research)
            .OrderBy(st => near is { } n && st.Sector == n ? 0 : 1).ThenBy(Rank).ThenBy(st => st.Id.Value).FirstOrDefault();
    }

    /// <summary>
    /// Verteilt die Streifen nach Gewicht (größte Reste) auf das Territorium und weist nur so wenige Schiffe um wie nötig: Wer in einem Sektor über dem
    /// Soll oder außerhalb des Territoriums eingeteilt ist, geht in einen Sektor unter dem Soll.
    /// </summary>
    public static void Assign(Universe u, FactionId f, List<Ship> police)
    {
        var terr = Territory(u, f);
        if (terr.Count == 0) { foreach (var p in police) p.DutySector = default; return; }
        var quota = Quotas(u, terr, police.Count);
        var count = terr.ToDictionary(s => s.Id, _ => 0);
        var free = new List<Ship>();
        foreach (var p in police)
        {
            if (count.TryGetValue(p.DutySector, out int c) && c < quota[p.DutySector]) count[p.DutySector] = c + 1;
            else free.Add(p);
        }
        foreach (var p in free)
        {
            var target = terr.Where(s => count[s.Id] < quota[s.Id]).OrderBy(s => s.Id.Value).FirstOrDefault() ?? terr[0];
            if (p.DutySector != target.Id) { p.DutySector = target.Id; p.NextThinkTick = u.Tick; }
            count[target.Id]++;
        }
    }

    /// <summary>Sollverteilung von <paramref name="n"/> Streifen auf die Sektoren (größte Reste, bei Gleichstand nach Id). Reine Funktion.</summary>
    public static Dictionary<SectorId, int> Quotas(Universe u, List<Sector> terr, int n)
    {
        var weights = terr.Select(s => PatrolWeight(u, s)).ToList();
        float total = weights.Sum();
        var quota = new Dictionary<SectorId, int>();
        var rest = new List<(float Frac, int Index)>();
        int given = 0;
        for (int i = 0; i < terr.Count; i++)
        {
            float exact = total > 0f ? n * weights[i] / total : (float)n / terr.Count;
            int q = (int)MathF.Floor(exact);
            quota[terr[i].Id] = q;
            given += q;
            rest.Add((exact - q, i));
        }
        foreach (var (_, i) in rest.OrderByDescending(x => x.Frac).ThenBy(x => terr[x.Index].Id.Value).Take(n - given)) quota[terr[i].Id]++;
        return quota;
    }

    // ───────── Streife ─────────

    static void StepPolice(Universe u, Ship p)
    {
        if (p.Transit is { } t)
        {
            if (u.Tick < t.ArrivalTick) return;
            u.FinishTransit(p);
            if (p.Order is GoToOrder g && g.Target == p.HomeBase)
            {
                PirateSystem.ServiceAtBase(u, p, "police");
                p.Order = null;
                p.NextThinkTick = u.Tick + u.Rules.PatrolDwellTicks;
                return;
            }
            p.NextThinkTick = u.Tick + u.Rules.PatrolDwellTicks;
            return;
        }
        if (u.Tick < p.NextThinkTick) return;
        var home = u.Stations.GetValueOrDefault(p.HomeBase);
        if (home is null && u.FactionOf(p) is { } f && HomeBaseFor(u, f, p.Sector) is { } h) { p.HomeBase = h.Id; home = h; }
        bool needsBase = p.Hull < u.Rules.RetreatHull * p.MaxHull || u.Rules.FuelActive && p.Stats.FuelCapacity > 0f && p.TotalFuel < 0.3f * p.Stats.FuelCapacity;
        if (home is not null && (needsBase || p.DutySector == default))
        {
            if (p.Sector == home.Sector && Vec2.Distance(p.Position, home.Position) <= u.Rules.StationSafeRadius)
            {
                if (needsBase) PirateSystem.ServiceAtBase(u, p, "police");
                p.NextThinkTick = u.Tick + u.Rules.PatrolDwellTicks;
                if (p.DutySector == default || needsBase) return;
            }
            else
            {
                p.Order = new GoToOrder(home.Id);
                if (!Travel.Start(u, p, home.Sector, home.DockPoint(p.Id))) { p.Order = null; p.NextThinkTick = u.Tick + 100; }
                return;
            }
        }
        if (p.DutySector == default || !u.Sectors.TryGetValue(p.DutySector, out var duty)) { p.NextThinkTick = u.Tick + 100; return; }
        var spot = PatrolPoint(u, duty);
        p.Order = new PatrolOrder(duty.Id);
        if (!Travel.Start(u, p, duty.Id, spot)) { p.Order = null; p.NextThinkTick = u.Tick + 100; }
    }

    /// <summary>Nächster Streifenpunkt im Sektor: zufällig (über <see cref="Universe.Rng"/>) ein Gate, eine Station oder ein Feld, leicht versetzt.</summary>
    public static Vec2 PatrolPoint(Universe u, Sector s)
    {
        var points = new List<Vec2>();
        foreach (var g in s.Gates) if (u.Gates.TryGetValue(g, out var gate)) points.Add(gate.Position);
        foreach (var id in s.Stations) if (u.Stations.TryGetValue(id, out var st)) points.Add(st.Position);
        foreach (var fld in s.Fields) points.Add(fld.Position);
        var basis = points.Count == 0 ? default : points[u.Rng.NextInt(points.Count)];
        return basis + Vec2.FromAngle((float)(u.Rng.NextDouble() * Math.PI * 2)) * 40f;
    }

    // ───────── Gesuchter Spieler ─────────

    /// <summary>Spieler mit Fahndungsstufe ≥ 3 bei der Sektorfraktion und eine Streife im Sektor: Abfangen ankündigen.</summary>
    static void CheckWantedPlayer(Universe u)
    {
        if (!u.Ships.TryGetValue(u.PlayerShip, out var player) || !u.Sectors.TryGetValue(player.Sector, out var sector)) return;
        if (Wanted.LevelOf(u, player.Owner, sector.Owner) < 3 || u.PendingEncounters.Any(e => e.Target == player.Id)) return;
        var police = PirateSystem.PoliceIn(u, sector.Id);
        if (police.Count == 0) return;
        var pending = new PendingEncounter { Kind = EncounterKind.Intercept, Source = police[0].Id, Target = player.Id, Sector = sector.Id, DueTick = u.Tick + u.Rules.PlayerWarningTicks };
        u.PendingEncounters.Add(pending);
        string who = u.Factions.TryGetValue(sector.Owner, out var fac) ? fac.Name : "Polizei";
        u.Events.Publish(new EncounterWarning(EncounterKind.Intercept, sector.Id, police[0].Id, player.Id, pending.DueTick,
            $"Polizei von {who}: Du wirst gesucht — Strafe am Gericht zahlen oder den Sektor verlassen ({u.Rules.PlayerWarningTicks / SimClock.TicksPerSecond:0} s)."));
    }

    static void ResolvePending(Universe u)
    {
        if (u.PendingEncounters.Count == 0) return;
        foreach (var e in u.PendingEncounters.Where(e => e.Kind == EncounterKind.Intercept && u.Tick >= e.DueTick).ToList())
        {
            u.PendingEncounters.Remove(e);
            if (!u.Ships.TryGetValue(e.Target, out var player) || player.Id != u.PlayerShip || player.Sector != e.Sector || !u.Sectors.TryGetValue(e.Sector, out var sec))
            {
                u.Events.Publish(new EncounterEscaped(EncounterKind.Intercept, e.Source, e.Target, "Du hast dich der Kontrolle entzogen."));
                continue;
            }
            InterceptPlayer(u, player, sec);
        }
    }

    /// <summary>
    /// Die Polizei stellt den Spieler (Fahndung bei der Sektorfraktion): Schmuggelware wird beschlagnahmt, die Strafe aus der Kasse eingezogen (soweit
    /// vorhanden, in die Fraktionskasse); bezahlt = Eintrag gelöscht, sonst bleibt der Rest als Strafe mit Stufe „verwarnt“. Nichts darüber hinaus.
    /// </summary>
    public static PlayerIntercepted InterceptPlayer(Universe u, Ship player, Sector sector)
    {
        var f = sector.Owner;
        float seized = 0f;
        foreach (var g in player.Cargo.Keys.OrderBy(k => k.Value).ToList())
            if (u.Goods.TryGetValue(g, out var good) && u.Rules.Contraband.Contains(good.Key)) { seized += player.Cargo[g]; player.Cargo.Remove(g); }
        float collected = 0f;
        string msg;
        if (u.Actors.TryGetValue(player.Owner, out var actor) && actor.Wanted.TryGetValue(f, out var w))
        {
            float fine = Wanted.FineOf(u, w);
            var wallet = u.WalletOf(player);
            collected = MathF.Min(fine, MathF.Max(0f, wallet.Credits));
            wallet.Credits -= collected;
            if (collected > 0f && u.Actors.TryGetValue(f, out var authority))
            {
                authority.Treasury.Credits += collected;
                u.Events.Publish(new TreasuryIncome(authority.Id, "fine", collected));
                u.Events.Publish(new FinePaid(actor.Id, f, collected));
            }
            if (collected + 0.5f >= fine) Wanted.SetHeat(u, actor, f, 0f, 0f);
            else Wanted.SetHeat(u, actor, f, MathF.Min(w.Heat, u.Rules.WantedLevels.Count > 1 ? u.Rules.WantedLevels[1] : 2f), fine - collected);
            msg = $"Polizei: Strafe {collected:0} von {fine:0} Cr eingezogen" + (seized > 0f ? $", {seized:0} Einheiten Schmuggelware beschlagnahmt." : ".");
        }
        else msg = "Polizeikontrolle ohne Befund.";
        var ev = new PlayerIntercepted(sector.Id, f, collected, seized, msg);
        u.Events.Publish(ev);
        return ev;
    }
}
