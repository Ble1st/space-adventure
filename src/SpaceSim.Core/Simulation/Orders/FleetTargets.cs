using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Eine Zielwahl im Befehlsraster: Beschriftung, Unterzeile und der fertige Befehl.</summary>
public sealed record FleetTarget(string Label, string Sub, ShipCommand Command);

/// <summary>
/// Ausbau Punkt 1/1b: Ziellisten der Flottenbefehle für die Oberfläche (Schritt 3 „Ziel“ — ein Tipp gibt den Befehl). Rein und deterministisch (feste
/// Reihenfolge), damit die Oberfläche nichts entscheidet und der Selbsttest dieselben Listen sieht.
/// </summary>
public static class FleetTargets
{
    /// <summary>Höchstzahl der Ziele je Liste (Raster mit Rollen).</summary>
    public const int MaxShips = 12, MaxStations = 8, MaxGates = 6, MaxRoutes = 10;

    static string ShipLabel(Universe u, Ship s) => s.CustomName.Length > 0 ? ShipNames.Display(u, s) : $"{(u.ShipModels.TryGetValue(s.ModelKey, out var m) ? m.Name : "Schiff")} #{s.Id.Value}";
    static string Short(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
    static int Hops(Universe u, SectorId a, SectorId b) => a == b ? 0 : u.FindRoute(a, b)?.Count ?? int.MaxValue;

    /// <summary>Gegner zuerst (Gesetzlose, Kriegsgegner), dann Abstand, dann Id.</summary>
    static int Rank(Universe u, Ship s) => PlayerCombat.IsOutlaw(u, s.Owner) || Wars.AtWar(u, u.PlayerActor, s.Owner) ? 0 : 1;

    /// <summary>Sektoren, in denen die Flotte sucht: der des Schiffs und der des aktiven Schiffs.</summary>
    static List<SectorId> Near(Universe u, Ship ship, bool neighbours)
    {
        var list = new List<SectorId> { ThreatMap.Where(u, ship).Sector };
        if (!list.Contains(u.PlayerSector) && u.Sectors.ContainsKey(u.PlayerSector)) list.Add(u.PlayerSector);
        if (neighbours && u.Sectors.TryGetValue(list[0], out var sec))
            foreach (var gid in sec.Gates.OrderBy(g => g.Value))
                if (u.Gates.TryGetValue(gid, out var g) && u.Gates.TryGetValue(g.Target, out var far) && !list.Contains(far.Sector)) list.Add(far.Sector);
        return list;
    }

    static List<Ship> ShipsIn(Universe u, Ship ship, IReadOnlyList<SectorId> sectors)
    {
        var (ms, mp) = ThreatMap.Where(u, ship);
        var seen = new HashSet<int>();
        var all = new List<(Ship S, int R, float D)>();
        foreach (var sid in sectors)
            foreach (var id in u.Tracker.ParkedIn(sid).Concat(u.Tracker.TransitIn(sid)))
            {
                if (!seen.Add(id.Value) || !u.Ships.TryGetValue(id, out var s) || s.IsPlayer || s.DockedAt.Value != 0) continue;
                var (ts, tp) = ThreatMap.Where(u, s);
                if (!sectors.Contains(ts)) continue;
                all.Add((s, Rank(u, s), ts == ms ? Vec2.Distance(mp, tp) : 1e6f + Hops(u, ms, ts)));
            }
        return all.OrderBy(x => x.R).ThenBy(x => x.D).ThenBy(x => x.S.Id.Value).Select(x => x.S).ToList();
    }

    /// <summary>Angreifen: fremde Schiffe (nicht angedockt), fremde Stationen und Tore im Sektor des Schiffs und des aktiven Schiffs.</summary>
    public static List<FleetTarget> Attack(Universe u, Ship ship)
    {
        var list = new List<FleetTarget>();
        var sectors = Near(u, ship, neighbours: false);
        foreach (var s in ShipsIn(u, ship, sectors).Take(MaxShips))
            list.Add(new FleetTarget(ShipLabel(u, s), $"{ActorNames.Tag(u, s.Owner)} {SectorName(u, ThreatMap.Where(u, s).Sector)}".Trim(), new AttackCommand(s.Id, default, default, ThreatMap.Where(u, s).Sector)));
        var (ms, mp) = ThreatMap.Where(u, ship);
        foreach (var st in u.Stations.Values.Where(st => sectors.Contains(st.Sector) && Intel.Knowledge.KnowsStation(u, st) && !st.IsBuildSite && st.Owner != u.PlayerActor && !StationCombat.IsDown(u, st))
                     .OrderBy(st => st.Sector == ms ? Vec2.Distance(mp, st.Position) : 1e6f).ThenBy(st => st.Id.Value).Take(MaxStations))
            list.Add(new FleetTarget(Short(StationNames.Short(u, st), 22), "Station" + (FleetAttack.IsCrime(u, st.Owner) ? " · Verbrechen" : ""), new AttackCommand(default, st.Id, default, st.Sector)));
        if (u.Rules.GateCombat)
            foreach (var g in u.Gates.Values.Where(g => sectors.Contains(g.Sector) && Intel.Knowledge.KnowsGate(u, g.Id) && !GateCombat.IsDown(u, g) && !GateCombat.InGrace(u, g))
                         .OrderBy(g => g.Sector.Value).ThenBy(g => g.Id.Value).Take(MaxGates))
                list.Add(new FleetTarget(GateCombat.NameOf(u, g), "Tor · Verbrechen", new AttackCommand(default, default, g.Id, g.Sector)));
        return list;
    }

    /// <summary>Verfolgen: fremde Schiffe im Sektor des Schiffs, in den Nachbarsektoren und im Sektor des aktiven Schiffs.</summary>
    public static List<FleetTarget> Pursue(Universe u, Ship ship) =>
        ShipsIn(u, ship, Near(u, ship, neighbours: true)).Take(MaxShips + 4)
            .Select(s => new FleetTarget(ShipLabel(u, s), $"{ActorNames.Tag(u, s.Owner)} {SectorName(u, ThreatMap.Where(u, s).Sector)}".Trim(), new PursueCommand(s.Id, 0f, u.Tick + FleetAttack.PursuitTicks))).ToList();

    /// <summary>Bergen: Sektoren mit lohnenden Wracks zuerst (Zahl), dann nach Sprüngen.</summary>
    public static List<FleetTarget> Salvage(Universe u, Ship ship)
    {
        var ms = ThreatMap.Where(u, ship).Sector;
        return u.Sectors.Values
            .Select(s => (S: s, N: u.Wrecks.Count(w => w.Sector == s.Id && SalvageExecutor.Worth(u, ship, w)), H: Hops(u, ms, s.Id)))
            .Where(x => x.H != int.MaxValue && Intel.Knowledge.KnowsSector(u, x.S.Id)).OrderBy(x => x.N > 0 ? 0 : 1).ThenBy(x => x.H).ThenBy(x => x.S.Id.Value)
            .Select(x => new FleetTarget(x.S.Name.Replace("Sektor ", ""), x.N == 0 ? "keine Wracks" : $"{x.N} Wrack{(x.N == 1 ? "" : "s")}", new SalvageCommand(x.S.Id))).ToList();
    }

    /// <summary>Tor blockieren: je Kriegsgegner die Tore nach Sprüngen vom Schiff (leer = kein Krieg).</summary>
    public static List<FleetTarget> Blockade(Universe u, Ship ship)
    {
        var list = new List<FleetTarget>();
        var ms = ThreatMap.Where(u, ship).Sector;
        foreach (var f in u.NpcFactionIds().Where(f => Wars.PlayerWar(u, f) is not null))
            foreach (var g in u.Gates.Values.Where(g => Intel.Knowledge.KnowsGate(u, g.Id)).Select(g => (G: g, H: Hops(u, ms, g.Sector))).Where(x => x.H != int.MaxValue).OrderBy(x => x.H).ThenBy(x => x.G.Id.Value).Take(MaxShips).Select(x => x.G))
                list.Add(new FleetTarget(GateCombat.NameOf(u, g), $"gegen {ActorNames.Tag(u, new ActorId(f.Value))}", new BlockadeCommand(g.Id, f)));
        return list;
    }

    /// <summary>
    /// Liefern: Routen (Hersteller → Abnehmer). Zuerst der Bedarf eigener Stationen (Zutaten und Regale unter Ziel) vom nächsten Hersteller mit Lager, dann
    /// die besten Spannen bis zwei Sprünge um das Schiff. <paramref name="amount"/> Einheiten je Fahrt (0 = voll), Einkauf bis <paramref name="maxBuy"/> × Basis.
    /// </summary>
    /// <param name="allowLoss">Nachtrag 1c: Verlustschutz aus (Standard: an — an A wird nicht gekauft, wenn B nicht mehr zahlt).</param>
    public static List<FleetTarget> Deliver(Universe u, Ship ship, float amount, float maxBuy, bool allowLoss = false)
    {
        var list = new List<FleetTarget>();
        var seen = new HashSet<(int, int, int)>();
        var ms = ThreatMap.Where(u, ship).Sector;
        void Add(Station a, Station b, GoodId g, string sub)
        {
            if (!seen.Add((a.Id.Value, b.Id.Value, g.Value))) return;
            var cmd = new DeliverCommand(a.Id, b.Id, g, amount, maxBuy, allowLoss);
            if (Commands.Check(u, ship, cmd) is not null) return;
            list.Add(new FleetTarget(u.Goods[g].Name, sub, cmd));
        }
        Station? Producer(GoodId g, SectorId near) => u.Stations.Values
            .Where(a => a.Produces(g) && Intel.Knowledge.KnowsStation(u, a) && !a.IsBuildSite && a.Market.TryGetValue(g, out var e) && e.Amount >= 1f && Hops(u, ms, a.Sector) != int.MaxValue)
            .OrderBy(a => Hops(u, near, a.Sector)).ThenBy(a => a.Id.Value).FirstOrDefault();
        // eigene Stationen
        foreach (var b in u.Stations.Values.Where(s => s.Owner == u.PlayerActor && !s.IsBuildSite).OrderBy(s => s.Id.Value))
            foreach (var (g, e) in b.Market.OrderBy(kv => kv.Key.Value))
            {
                if (b.Produces(g) || e.Amount >= e.Target) continue;
                if (Producer(g, b.Sector) is { } a && a.Id != b.Id) Add(a, b, g, $"→ {Short(StationNames.Short(u, b), 18)} (eigen)");
                if (list.Count >= MaxRoutes) return list;
            }
        // beste Spannen in der Nähe
        var nearSt = u.Stations.Values.Where(s => !s.IsBuildSite && Intel.Knowledge.KnowsStation(u, s) && Hops(u, ms, s.Sector) <= 2).OrderBy(s => s.Id.Value).ToList();
        var routes = new List<(Station A, Station B, GoodId G, float M)>();
        foreach (var b in nearSt)
            foreach (var (g, e) in b.Market.OrderBy(kv => kv.Key.Value))
            {
                if (!b.Consumes(g) || e.Amount >= e.Target || e.Price <= 0f) continue;
                foreach (var a in nearSt)
                    if (a.Id != b.Id && a.Produces(g) && a.Market.TryGetValue(g, out var ea) && ea.Amount >= 1f && ea.Price > 0f && ea.Price < e.Price)
                        routes.Add((a, b, g, e.Price - ea.Price));
            }
        foreach (var r in routes.OrderByDescending(r => r.M).ThenBy(r => r.A.Id.Value).ThenBy(r => r.B.Id.Value).ThenBy(r => r.G.Value))
        {
            if (list.Count >= MaxRoutes) break;
            Add(r.A, r.B, r.G, $"{Short(StationNames.WithoutOwner(u, r.A), 10)} → {Short(StationNames.WithoutOwner(u, r.B), 10)}");   // 1c: ohne Kürzel (passt in die Taste)
        }
        return list;
    }

    /// <summary>Sammelpunkt: Stationen nach Sprüngen vom Schiff.</summary>
    public static List<FleetTarget> Rally(Universe u, Ship ship, IReadOnlyList<Ship> group)
    {
        var ms = ThreatMap.Where(u, ship).Sector;
        return u.Stations.Values.Where(s => !s.IsBuildSite && Intel.Knowledge.KnowsStation(u, s) && Hops(u, ms, s.Sector) != int.MaxValue)
            .OrderBy(s => Hops(u, ms, s.Sector)).ThenBy(s => s.Sector.Value).ThenBy(s => s.Id.Value).Take(16)
            .Select(s => new FleetTarget(Short(StationNames.Short(u, s), 22), SectorName(u, s.Sector), new RallyCommand(s.Id, ship.Id))).ToList();
    }

    /// <summary>
    /// Ausbau Punkt 16: Ziele der Taste „Erkunden“ — zuerst „Universum“ (wenn ein unbesuchter Sektor erreichbar ist), dann bekannte Sektoren mit Unbekanntem (nach Sprüngen, dann Id); Unterzeile „n unbekannt“
    /// bzw. „unbesucht“. Ohne beschränktes Wissen alle erreichbaren Sektoren.
    /// </summary>
    public static List<FleetTarget> ExploreSector(Universe u, Ship ship)
    {
        var ms = ThreatMap.Where(u, ship).Sector;
        bool lim = Intel.Knowledge.Limited(u);
        var list = ExploreUniverse(u, ship);   // erste Wahl: die Universumsebene
        list.AddRange(u.Sectors.Values
            .Where(s => Intel.Knowledge.KnowsSector(u, s.Id) && (!lim || !Intel.Knowledge.Visited(u, s.Id) || Intel.Knowledge.UnknownIn(u, s.Id) > 0))
            .Select(s => (S: s, H: Hops(u, ms, s.Id))).Where(x => x.H != int.MaxValue)
            .OrderBy(x => x.H).ThenBy(x => x.S.Id.Value).Take(16)
            .Select(x => new FleetTarget(x.S.Name.Replace("Sektor ", ""),
                !lim ? $"{x.H} Sprünge" : !Intel.Knowledge.Visited(u, x.S.Id) ? "unbesucht" : $"{Intel.Knowledge.UnknownIn(u, x.S.Id)} unbekannt",
                new ExploreSectorCommand(x.S.Id))));
        return list;
    }

    /// <summary>Ausbau Punkt 16: Erkunden (Universum) — ein Ziel ohne Wahl (der nächste unbesuchte Sektor).</summary>
    public static List<FleetTarget> ExploreUniverse(Universe u, Ship ship) =>
        Intel.ExploreRules.NextSector(u, ship) is { } next
            ? new List<FleetTarget> { new("Universum", $"zuerst {SectorName(u, next)}", new ExploreUniverseCommand()) }
            : new List<FleetTarget>();

    static string SectorName(Universe u, SectorId s) => u.Sectors.TryGetValue(s, out var sec) ? sec.Name.Replace("Sektor ", "") : "?";
}
