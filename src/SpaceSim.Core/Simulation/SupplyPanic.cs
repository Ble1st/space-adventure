using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 20: Versorgungspanik. Zerfällt die Karte durch ein geschlossenes oder zerstörtes Gate (<see cref="MapParts"/>), prüft jeder Teil, welche dort gebrauchten
/// Waren keinen Hersteller mehr im Teil haben — Zutaten der Rezepte seiner Stationen, Exportwaren seiner Senken, Bausätze und Module der Schiffe, die die KI
/// kauft. Fehlt ein Hersteller länger als <see cref="EconomyRules.PanicDelayTicks"/>, bricht Panik aus (<see cref="SupplyPanicRaised"/>, die Ansicht zeigt einen
/// Hinweis), und der Stationsbau legt im Teil dringlich eine Ersatz-Baustelle an: vor den normalen Bauwünschen, notfalls über <see cref="EconomyRules.MaxStations"/>
/// und die Sektorgrenze hinaus, aber höchstens <see cref="EconomyRules.PanicReserveStations"/> Panik-Stationen je Teil und eine Panik-Baustelle je Teil
/// gleichzeitig. Gebaut wird nur, was im Teil arbeiten kann: Jede Zutat wird im Teil hergestellt oder abgebaut, sonst wird zuerst ihr Hersteller gebaut (die
/// Kette hinauf, höchstens <see cref="StationLifecycleSystem.MaxChainDepth"/> Glieder); ist eine Zutat im Teil gar nicht zu bekommen (Erz ohne Gürtel), bleibt es
/// bei der Panik. Geld: gewichtete Ziehung (<see cref="Investors"/>) unter den Geldgebern des Teils (Kassen der Fraktionen, die dort Sektoren besitzen,
/// Stationen und Schiffe im Teil). Allgemein über erreichbare Kartenteile — nichts nimmt die Startkarte an.
/// </summary>
public sealed class SupplyPanicSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        var rules = u.Rules;
        var parts = MapParts.Of(u);
        if (!rules.SupplyPanicActive || !parts.IsSplit) { if (u.PanicSince.Count > 0) u.PanicSince.Clear(); return; }

        var current = new HashSet<(int, GoodId)>();
        var needs = new Dictionary<int, List<(GoodId Good, float Weight)>>();
        foreach (var part in parts.Parts)
        {
            int key = part[0].Value;
            var missing = SupplyPanic.Missing(u, key);
            needs[key] = missing;
            foreach (var (g, _) in missing)
            {
                current.Add((key, g));
                if (!u.PanicSince.TryGetValue((key, g), out var since)) { u.PanicSince[(key, g)] = since = u.Tick; }
                long due = since + rules.PanicDelayTicks;
                if (due <= u.Tick && due > u.Tick - dt)
                {
                    var gate = MapParts.CutGate(u, key);
                    u.Events.Publish(new SupplyPanicRaised(part[0], g, gate ?? default, SupplyPanic.PlanFor(u, key, g)?.Key));
                }
            }
        }
        foreach (var k in u.PanicSince.Keys.Where(k => !current.Contains(k)).ToList()) u.PanicSince.Remove(k);

        // Bau: je Teil höchstens eine Panik-Baustelle gleichzeitig, wichtigste Ware zuerst
        foreach (var part in parts.Parts)
        {
            int key = part[0].Value;
            var active = needs[key].Where(n => u.Tick - u.PanicSince[(key, n.Good)] >= rules.PanicDelayTicks).ToList();
            if (active.Count == 0 || !SupplyPanic.MayBuild(u, key)) continue;
            foreach (var (good, _) in active)
            {
                if (SupplyPanic.PlanFor(u, key, good) is not { } type) continue;
                SupplyPanic.StartBuild(u, key, type, good);
                break;   // gebaut oder nicht bezahlbar: im nächsten Schritt weiter
            }
        }
    }
}

/// <summary>Phase 20: reine Regeln der Versorgungspanik (siehe <see cref="SupplyPanicSystem"/>).</summary>
public static class SupplyPanic
{
    /// <summary>Anzeigetext einer Panik (Hinweis in der Ansicht): betroffener Kartenteil, fehlende Ware, Ursache, geplanter Ersatz.</summary>
    public static string Describe(Universe u, SupplyPanicRaised e)
    {
        var part = MapParts.Of(u).SectorsOf(e.Part.Value);
        string where = part.Count == 0 ? (u.Sectors.TryGetValue(e.Part, out var ps) ? ps.Name : "?")
            : string.Join(", ", part.Select(id => u.Sectors[id].Name));
        string good = u.Goods.TryGetValue(e.Good, out var g) ? g.Name : "?";
        string cause = u.Gates.TryGetValue(e.Gate, out var gate) && u.Sectors.TryGetValue(gate.Sector, out var gs) && u.Gates.TryGetValue(gate.Target, out var far)
            && u.Sectors.TryGetValue(far.Sector, out var fs) ? $" — Tor {gs.Name} ↔ {fs.Name} {AccessNames.Gate(gate.State).ToLowerInvariant()}" : "";
        string answer = e.TypeKey is { } tk && u.StationTypes.TryGetValue(tk, out var t) ? $"; Ersatz geplant: {t.Name}" : "; im abgeschnittenen Teil nicht herstellbar";
        return $"Versorgungspanik in {where}: kein Hersteller für {good}{cause}{answer}";
    }

    static bool In(MapPartition p, int key, SectorId s) => p.KeyOf(s) == key;

    /// <summary>Stellt eine fertige Station (oder eine laufende Baustelle eines Herstellertyps) im Teil die Ware her?</summary>
    public static bool ProducedIn(Universe u, int key, GoodId good)
    {
        var p = MapParts.Of(u);
        foreach (var st in u.Stations.Values)
        {
            if (!In(p, key, st.Sector)) continue;
            if (st.Site is { Abandoned: false } bs)
            {
                if (u.StationTypes.TryGetValue(bs.TypeKey, out var t) && t.Recipes.Any(r => r.Outputs.ContainsKey(good))) return true;
                continue;
            }
            if (!st.IsBuildSite && st.Produces(good)) return true;
        }
        return false;
    }

    /// <summary>Kann die Ware im Teil abgebaut werden (ein Gürtel im Teil, dessen Asteroidenklasse sie liefert)?</summary>
    public static bool MinableIn(Universe u, int key, GoodId good)
    {
        var p = MapParts.Of(u);
        foreach (var s in u.Sectors.Values)
        {
            if (!In(p, key, s.Id)) continue;
            foreach (var f in s.Fields)
                if (u.AsteroidClasses.TryGetValue(f.ClassKey, out var c) && c.Yields.Any(y => y.Good == good && y.Weight > 0f)) return true;
        }
        return false;
    }

    /// <summary>
    /// Im Teil gebrauchte Waren ohne Hersteller im Teil, nach Gewicht (Wunschbestand × Basispreis der Abnehmer; Flottenwaren nach Schiffen im Teil), schwerste
    /// zuerst. Nur Waren, für die es überhaupt einen baubaren Herstellertyp gibt (Erze werden abgebaut, nicht gebaut).
    /// </summary>
    public static List<(GoodId Good, float Weight)> Missing(Universe u, int key)
    {
        var p = MapParts.Of(u);
        var weight = new Dictionary<GoodId, float>();
        void Add(GoodId g, float w) { if (w > 0f) weight[g] = weight.GetValueOrDefault(g) + w; }
        int economicStations = 0, fleetShips = 0;
        foreach (var st in u.StationsById())
        {
            if (st.IsBuildSite || !In(p, key, st.Sector)) continue;
            if (StationProtection.IsEconomic(u, st) && st.Market.Count > 0) economicStations++;
            foreach (var r in st.Production)
                foreach (var g in r.Inputs.Keys)
                    if (!st.Produces(g) && st.Market.TryGetValue(g, out var e)) Add(g, MathF.Max(1f, e.Target) * u.Goods[g].BasePrice);
            if (ExportRules.ConfigOf(u, st) is { } x)
                foreach (var g in x.Goods)
                    if (st.Market.TryGetValue(g, out var e)) Add(g, MathF.Max(1f, e.Target) * u.Goods[g].BasePrice);
        }
        foreach (var sh in u.ShipsById())
            if (sh.IsEconomyAi && In(p, key, sh.Transit?.ToSector ?? sh.Sector)) fleetShips++;
        // Flottenwaren: ein Teil mit Wirtschaft braucht Schiffe — Bausätze und Module der Modelle, die die KI kauft (Aufgaben) und der Polizei
        if (economicStations > 0)
        {
            float ships = MathF.Max(1f, fleetShips);
            foreach (var g in FleetGoods(u))
                Add(g, ships * u.Goods[g].BasePrice * (u.Modules.ContainsKey(u.Goods[g].Key) ? 0.1f : 1f));
        }
        var list = new List<(GoodId, float)>();
        foreach (var (g, w) in weight.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Value))
        {
            if (!u.StationTypes.Values.Any(t => StationLifecycleSystem.Buildable(u, t) && t.Recipes.Any(r => r.Outputs.ContainsKey(g)))) continue;
            if (ProducedIn(u, key, g)) continue;
            list.Add((g, w));
        }
        return list;
    }

    /// <summary>Bausätze und Module der Schiffsmodelle, die die KI kauft (mit Aufgaben) oder die Polizei bekommt (feste Reihenfolge).</summary>
    public static List<GoodId> FleetGoods(Universe u)
    {
        var set = new SortedSet<int>();
        foreach (var m in u.ShipModels.Values)
        {
            if (m.Jobs.Count == 0 && m.Key != u.Rules.PoliceModel) continue;
            if (!u.ShipClasses.TryGetValue(m.ClassKey, out var cls)) continue;
            if (u.FindGood(ShipPurchase.KitKeyOf(u, cls)) is { } kit) set.Add(kit.Id.Value);
            foreach (var k in m.Fit)
                if (k is not null && u.FindGood(k) is { } mg) set.Add(mg.Id.Value);
        }
        return set.Select(v => new GoodId(v)).ToList();
    }

    /// <summary>
    /// Welcher Typ wird gegen das Fehlen der Ware im Teil gebaut? Der günstigste baubare Hersteller, dessen Rezept für die Ware im Teil arbeiten kann; fehlt eine
    /// hergestellte Zutat im Teil, zuerst deren Hersteller (die Kette hinauf). null = im Teil nicht herstellbar.
    /// </summary>
    public static StationType? PlanFor(Universe u, int key, GoodId good) => Plan(u, key, good, 0, new HashSet<GoodId>());

    static StationType? Plan(Universe u, int key, GoodId good, int depth, HashSet<GoodId> visiting)
    {
        if (!visiting.Add(good)) return null;
        try
        {
            foreach (var type in u.StationTypes.Values.Where(t => StationLifecycleSystem.Buildable(u, t) && t.Recipes.Any(r => r.Outputs.ContainsKey(good)))
                         .OrderBy(t => t.BuildCost).ThenBy(t => t.Key, StringComparer.Ordinal))
                foreach (var recipe in type.Recipes.Where(r => r.Outputs.ContainsKey(good)))
                {
                    StationType? upstream = null;
                    bool ok = true;
                    foreach (var input in recipe.Inputs.Keys.OrderBy(g => g.Value))
                    {
                        if (ProducedIn(u, key, input) || MinableIn(u, key, input)) continue;
                        if (depth >= StationLifecycleSystem.MaxChainDepth || Plan(u, key, input, depth + 1, visiting) is not { } t) { ok = false; break; }
                        upstream ??= t;
                    }
                    if (ok) return upstream ?? type;
                }
            return null;
        }
        finally { visiting.Remove(good); }
    }

    /// <summary>Panik-Stationen und -Baustellen im Teil.</summary>
    public static int PanicStations(Universe u, int key)
    {
        var p = MapParts.Of(u);
        return u.Stations.Values.Count(s => (s.Panic || s.Site is { Panic: true, Abandoned: false }) && In(p, key, s.Sector));
    }

    /// <summary>Darf der Teil jetzt eine Panik-Baustelle anlegen? Keine laufende Panik-Baustelle im Teil und Notreserve nicht erschöpft.</summary>
    public static bool MayBuild(Universe u, int key)
    {
        var p = MapParts.Of(u);
        if (u.Stations.Values.Any(s => s.Site is { Panic: true, Abandoned: false } && In(p, key, s.Sector))) return false;
        return PanicStations(u, key) < u.Rules.PanicReserveStations;
    }

    /// <summary>
    /// Panik-Baustelle anlegen (oder, ohne Baustellen in dieser Welt, direkt bauen): Sektor im Teil mit den wenigsten Stationen und freiem Platz, Geldgeber aus
    /// dem Teil. true = angelegt.
    /// </summary>
    public static bool StartBuild(Universe u, int key, StationType type, GoodId good)
    {
        var rules = u.Rules;
        var p = MapParts.Of(u);
        if (u.Stations.Values.Any(s => s.Site is { Abandoned: false } bs && bs.TypeKey == type.Key && In(p, key, s.Sector))) return false;
        float cost = type.BuildCost * rules.StationBuildCostFactor;
        var factionsHere = u.Sectors.Values.Where(s => In(p, key, s.Id)).Select(s => s.Owner.Value).ToHashSet();
        var candidates = Investors.ForStation(u, cost + rules.InvestorReserve).Where(c => c.Source switch
        {
            InvestorSource.Treasury => factionsHere.Contains(c.Holder.Value) || (u.Actors[c.Holder].Membership is { } m && factionsHere.Contains(m.Value)),
            InvestorSource.Station => In(p, key, c.Station!.Sector),
            _ => In(p, key, c.Ship!.Transit?.ToSector ?? c.Ship!.Sector),
        }).ToList();
        var investor = Investors.Draw(u, candidates);
        if (investor is null) return false;
        var faction = u.FactionOf(investor.Holder);
        if (faction is not null && !Research.IsUnlocked(u, faction, UnlockKind.StationType, type.Key)) return false;

        Sector? sector = null;
        Vec2? spot = null;
        foreach (var candidate in u.Sectors.Values.Where(s => In(p, key, s.Id) && StationProtection.CountEconomic(u, s) < rules.MaxStationsPerSector + rules.PanicReserveStations)
                     .OrderBy(s => StationProtection.CountEconomic(u, s)).ThenBy(s => s.Id.Value))
        {
            spot = StationLifecycleSystem.FindFreeSpot(u, candidate);
            if (spot != null) { sector = candidate; break; }
        }
        if (sector is null || spot is null) return false;

        var owner = investor.Ship is { } payer ? ActorRules.Promote(u, payer) : investor.Holder;
        var account = investor.Account(u);
        u.Events.Publish(new InvestorChosen("station", investor.Source, investor.Holder, investor.Class, cost, candidates.Count));
        account.Credits -= cost;
        investor.Invested(cost);   // Phase 22
        if (ConstructionRules.UsesSites(u, type))
        {
            var site = ConstructionRules.StartSite(u, type, sector.Id, spot.Value, owner, cost,
                investor.Station?.Id, investor.Ship?.Id, investor.Source == InvestorSource.Treasury ? investor.Holder : null);
            site.Site!.Panic = true;
            u.Events.Publish(new SupplyPanicBuild(site.Id, sector.Id, good, type.Key));
            return true;
        }
        var id = u.NewStationId();
        var station = StationFactory.Create(u, id, type, $"{type.Name} {sector.Name.Split(' ').Last()} {id.Value}", sector.Id, spot.Value, owner);
        station.Credits = ConstructionRules.NewStationCredits;
        station.Panic = true;
        u.AddStation(station);
        u.NotifyStationBuilt(station);
        u.Events.Publish(new SupplyPanicBuild(station.Id, sector.Id, good, type.Key));
        return true;
    }
}
