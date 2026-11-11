using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Stationen entstehen bei Knappheit und verschwinden bei Pleite:
/// - Pleite: Credits dauerhaft unter dem Limit → Station wird aufgegeben (nie: vor Pleite geschützte, kritische — letzte erreichbare
///   Herstellerin einer Ware, <see cref="StationCriticality"/> — und Baustellen).
/// - Phase 8: Das System entscheidet nur noch, <em>ob</em> gebaut wird. Gibt es Baustellen (<see cref="ConstructionRules.UsesSites"/>), legt es
///   eine Baustelle an (Investor zahlt die Bausumme als Treuhandgeld ein, <see cref="ConstructionSystem"/> baut); sonst wie früher Direktbau.
/// - Bau: Ist eine Ware im Schnitt knapp (Bestand deutlich unter Wunschbestand), baut ein Investor eine Produktionsstätte für sie; liegt sie
///   dauerhaft weit über dem Wunschbestand, entsteht stattdessen ein Verbraucher dafür. Phase 9: Der Investor kommt aus der gewichteten Ziehung
///   (<see cref="Investors"/>, Kassen der Akteure, Stationen, Schiffe); die Station gehört dem Akteur dahinter.
/// </summary>
public sealed class StationLifecycleSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        RemoveBankrupt(u);
        // Phase 19: Knappheit einmal messen — für den Bau und für die Überprüfung des Bestands (Stilllegung/Umwidmung)
        var scan = Scan(u);
        StationReview.TrackScarcity(u, scan);
        if (StationReview.TryRetire(u, scan) is { } forType) { TryBuild(u, scan, forType); return; }
        TryBuild(u, scan, null);
    }

    static void RemoveBankrupt(Universe u)
    {
        foreach (var st in u.Stations.Values.Where(s => !s.BankruptcyProtected && !s.IsBuildSite).ToList())
        {
            if (st.Credits >= u.Rules.BankruptcyLimit) { st.BelowLimitSince = -1; continue; }
            if (st.BelowLimitSince < 0) st.BelowLimitSince = u.Tick;
            // Phase 8: die letzte erreichbare Herstellerin einer Ware (Critical, abgeleitet) wird nicht aufgegeben
            if (u.Tick - st.BelowLimitSince >= u.Rules.BankruptcyTicks && !StationCriticality.IsCritical(u, st)) u.RemoveStation(st.Id, "bankrupt");
        }
    }

    /// <summary>
    /// Phase 19: Ergebnis einer Knappheitsmessung des Lebenszyklus: Verhältnis Wunsch/Bestand je Ware (über alle fertigen Halter), die knappste und die am
    /// stärksten überversorgte beantwortbare Ware und alle beantwortbar knappen Waren (Verhältnis über <see cref="EconomyRules.BuildPriceRatio"/>).
    /// </summary>
    public sealed record LifecycleScan(Dictionary<GoodId, float> Ratios, GoodId? Scarce, GoodId? Glut, List<GoodId> ScarceGoods);

    /// <summary>Knappheit messen (rein, ohne Zufall): siehe <see cref="LifecycleScan"/>.</summary>
    public static LifecycleScan Scan(Universe u)
    {
        var rules = u.Rules;
        // Knappste Ware (höchstes Verhältnis Wunsch/Bestand) und am stärksten überversorgte Ware (niedrigstes)
        GoodId? scarce = null, glut = null;
        float worst = rules.BuildPriceRatio, lowest = rules.GlutPriceRatio;
        var ratios = new Dictionary<GoodId, float>();
        var scarceGoods = new List<GoodId>();
        foreach (var good in u.Goods.Values)
        {
            // Knappheit aus den Beständen (Wunschbestand / Bestand über alle Stationen), nicht aus Preisen: Preise sind durch die
            // Zahlungsbereitschaft der Fabriken gedeckelt und würden Überangebot vortäuschen.
            // Phase 8: Baustellen sind keine Märkte (ihr Wunschbestand ist eine Stückliste) und zählen nicht mit
            // Phase 14: Exportsenken zählen mit — ihre Nachfrage (leeres Ankauflager) lässt Hersteller von Endwaren entstehen
            double target = 0, amount = 0;   // in double summiert wie LINQ Sum bis Phase 17
            bool any = false;
            foreach (var st in u.Stations.Values)
            {
                if (st.IsBuildSite || !st.Market.TryGetValue(good.Id, out var e)) continue;
                any = true;
                target += e.Target;
                amount += e.Amount;
            }
            if (!any) continue;
            float ratio = (float)target / MathF.Max(1f, (float)amount);
            ratios[good.Id] = ratio;
            // Phase 5a: nur Waren, auf die ein Stationstyp antworten kann (Erzeuger bei Knappheit, Rezept-Verbraucher bei Überangebot).
            // Sonst blockiert eine Ware ohne Verbraucher-Typ (Module, Bausatzstufen auf Halde) jeden Verbraucherbau für andere Waren.
            if (ratio > rules.BuildPriceRatio && u.StationTypes.Values.Any(t => t.Recipes.Any(r => r.Outputs.ContainsKey(good.Id)))
                && !(rules.BuildFollowsChain && FleetGoodWithProducer(u, good.Id)))
            {
                scarceGoods.Add(good.Id);
                if (ratio > worst) { worst = ratio; scarce = good.Id; }
            }
            if (ratio < lowest && ratio > 0f && u.StationTypes.Values.Any(t => t.Recipes.Any(r => r.Inputs.ContainsKey(good.Id)))) { lowest = ratio; glut = good.Id; }
        }
        return new LifecycleScan(ratios, scarce, glut, scarceGoods);
    }

    /// <summary>Welchen Typ baut der Lebenszyklus gegen die Knappheit dieser Ware (Phase 15/16/18: Rohstoff-Filter, Bau folgt der Kette)? null = nichts.</summary>
    public static StationType? ProducerFor(Universe u, GoodId good, IReadOnlyDictionary<GoodId, float> ratios) =>
        UpstreamProducer(u, good, ratios, u.Rules.BuildChecksRawOre ? u.Rules.BuildPriceRatio : float.PositiveInfinity,   // Phase 16: Filter abschaltbar
            u.Rules.BuildFollowsChain ? u.Rules.BuildPriceRatio : float.PositiveInfinity);                               // Phase 18: Bau folgt der Kette

    /// <summary><paramref name="forced"/> (Phase 19): nach einer Stilllegung der Typ, für den der Platz frei wurde; sonst entscheidet die Messung.</summary>
    static void TryBuild(Universe u, LifecycleScan scan, StationType? forced)
    {
        var rules = u.Rules;
        // Phase 10: Regierungsstationen und Wachen zählen nicht gegen die Obergrenzen
        if (StationProtection.CountEconomic(u) >= rules.MaxStations || u.Tick - u.LastStationBuildTick < rules.BuildCooldown) return;
        // Phase 8: höchstens MaxActiveSites Baustellen gleichzeitig (verlassene zählen nicht)
        var active = ConstructionRules.ActiveSites(u).ToList();
        if (active.Count >= rules.MaxActiveSites) return;

        // Knappheit → Produzent der Ware bauen; sonst bei Überangebot → Verbraucher der Ware bauen (Nachfrage wächst mit billigem Angebot)
        // Phase 13: nur Stationstypen, die schon irgendeine Fraktion erforscht hat (ohne Sperre: alle)
        // Phase 14: Exportsenken (Handelsstationen an Planeten) baut die Wirtschaft nie selbst, und Typen eines abgeschalteten Systems
        // (Forschungsstation, reiner Treibstoffhersteller) auch nicht
        StationType? type = forced;
        if (type is null && scan.Scarce is { } sc)
            type = ProducerFor(u, sc, scan.Ratios);
        else if (type is null && scan.Glut is { } gl)
            type = u.StationTypes.Values.Where(t => t.Recipes.Any(r => r.Inputs.ContainsKey(gl)) && Buildable(u, t)).OrderBy(t => t.BuildCost).FirstOrDefault();
        if (type is null) return;
        if (active.Any(s => s.Site!.TypeKey == type.Key)) return;   // derselbe Typ ist schon im Bau

        float cost = type.BuildCost * rules.StationBuildCostFactor;
        // Phase 9: Zukauf mit Erst- und Zweitzugriff — gewichtete Ziehung unter allen, die Baukosten plus Reserve aufbringen
        // (Kassen der Akteure, Betriebskassen der Stationen und der Händler-/Minenschiffe); Große haben den höheren Appetit
        float need = cost + rules.InvestorReserve;
        var candidates = Investors.ForStation(u, need);
        var investor = Investors.Draw(u, candidates);
        if (investor is null) return;
        // Fraktion des Investors; ein freies Einzelschiff wäre nach dem Aufstieg Mitglied der Hoheit seines Sektors
        var investorFaction = investor.Ship is { } invShip && invShip.Owner == u.FreeActor
            ? (u.Sectors.TryGetValue(invShip.Sector, out var home) && u.Factions.ContainsKey(home.Owner) ? (FactionId?)home.Owner : null)
            : u.FactionOf(investor.Holder);
        // Phase 13: gebaut wird nur, was die Fraktion des Investors erforscht hat (unabhängige Investoren: was irgendwo erforscht ist)
        if (investorFaction is not null && !Research.IsUnlocked(u, investorFaction, UnlockKind.StationType, type.Key)) return;

        // Sektoren der Fraktion des Investors (unabhängige Investoren: alle), dünn besiedelte zuerst; der erste mit freiem Platz bekommt die Station
        Sector? sector = null;
        Vec2? spot = null;
        bool sites = ConstructionRules.UsesSites(u, type);
        foreach (var candidate in u.Sectors.Values
                     .Where(s => (investorFaction is null || s.Owner == investorFaction) && StationProtection.CountEconomic(u, s) < rules.MaxStationsPerSector)
                     .OrderBy(s => StationProtection.CountEconomic(u, s)).ThenBy(s => s.Id.Value))
        {
            // Phase 8: eine verlassene Baustelle im Sektor wird mit ihrem Material übernommen (kein neuer Bauplatz nötig)
            var abandoned = sites ? candidate.Stations.Select(id => u.Stations[id]).FirstOrDefault(s => s.Site is { Abandoned: true }) : null;
            spot = abandoned?.Position ?? FindFreeSpot(u, candidate);
            if (spot != null) { sector = candidate; break; }
        }
        if (sector is null || spot is null) return;

        // Ein freies Einzelschiff, das eine Station finanziert, wird zum eigenen Akteur (lazy); Besitzer der Station ist der Akteur dahinter
        var owner = investor.Ship is { } payer ? ActorRules.Promote(u, payer) : investor.Holder;
        var account = investor.Account(u);
        u.Events.Publish(new InvestorChosen("station", investor.Source, investor.Holder, investor.Class, cost, candidates.Count));
        if (sites)
        {
            // Phase 8: Baustelle statt sofortiger Station; der Investor zahlt die Bausumme als Treuhandgeld ein
            account.Credits -= cost;
            ConstructionRules.StartSite(u, type, sector.Id, spot.Value, owner, cost,
                investor.Station?.Id, investor.Ship?.Id, investor.Source == InvestorSource.Treasury ? investor.Holder : null);
            u.LastStationBuildTick = u.Tick;
            return;
        }

        var id = u.NewStationId();
        var station = StationFactory.Create(u, id, type, $"{type.Name} {sector.Name.Split(' ').Last()} {id.Value}", sector.Id, spot.Value, owner);
        station.Credits = ConstructionRules.NewStationCredits;
        account.Credits -= cost;
        u.AddStation(station);
        u.LastStationBuildTick = u.Tick;
        u.NotifyStationBuilt(station);
    }

    /// <summary>
    /// Phase 15: Welcher Stationstyp hilft gegen die Knappheit einer Ware? Der günstigste baubare Hersteller — außer, eine seiner Zutaten ist ein Rohstoff
    /// ohne baubaren Hersteller (Erz) und selbst knapp (Verhältnis Wunsch/Bestand über <paramref name="threshold"/>): Dann liegt der Engpass beim Bergbau,
    /// und eine weitere Hütte würde nur um dasselbe Erz konkurrieren, mit leerem Erzlager am 4×-Deckel stehen und den Erzpreis hochtreiben (vorher
    /// entstanden so mehrere Kupferhütten und Waferfabriken ohne Erz). null = nichts bauen.
    /// Phase 18: Bau folgt der Kette — ist eine <em>hergestellte</em> Zutat des Herstellers selbst knapp (über <paramref name="chainThreshold"/>), liegt der
    /// Engpass dort: gebaut wird (rekursiv, höchstens <see cref="MaxChainDepth"/> Glieder) der Hersteller der knappsten Zutat statt eines weiteren
    /// Verbrauchers derselben knappen Zutat (vorher entstand bei Nahrungsknappheit eine zweite Farm, obwohl das Wasser für die erste schon fehlte).
    /// Die Knappheit gilt über alle Halter der Zutat (auch das Ausgangslager der Hersteller): Ist sie überall leer, begrenzt ihr Angebot.
    /// <see cref="float.PositiveInfinity"/> = alte Regel (hergestellte Zutaten lenken nicht um).
    /// </summary>
    public static StationType? UpstreamProducer(Universe u, GoodId good, IReadOnlyDictionary<GoodId, float> ratios, float threshold,
        float chainThreshold = float.PositiveInfinity) => UpstreamProducer(u, good, ratios, threshold, chainThreshold, 0);

    /// <summary>Phase 18: höchstens so viele Glieder geht der Lebenszyklus die Kette hinauf.</summary>
    public const int MaxChainDepth = 3;

    static StationType? UpstreamProducer(Universe u, GoodId good, IReadOnlyDictionary<GoodId, float> ratios, float threshold, float chainThreshold, int depth)
    {
        var type = CheapestProducer(u, good);
        if (type is null) return null;
        GoodId? bottleneck = null;
        float worst = chainThreshold;
        foreach (var r in type.Recipes.Where(r => r.Outputs.ContainsKey(good)))
            foreach (var g in r.Inputs.Keys)
            {
                if (!ratios.TryGetValue(g, out var x)) continue;
                bool made = CheapestProducer(u, g) is not null;
                if (!made && x > threshold) return null;
                if (made && g != good && x > worst) { worst = x; bottleneck = g; }
            }
        if (bottleneck is { } b && depth < MaxChainDepth) return UpstreamProducer(u, b, ratios, threshold, chainThreshold, depth + 1);
        return type;
    }

    /// <summary>
    /// Phase 18: Ware, die kein Rezept verbraucht und keine Exportsenke abnimmt (Schiffsbausätze, Module — sie gehen nur an Flotte und Regale), und für die
    /// schon eine fertige Herstellerin existiert. Ihre Knappheit löst keinen Bau aus: Eine Werft oder ein Modulwerk schafft 1 Stück je 20–60 Ticks und Rezept,
    /// die Flotte braucht ein Zehntel davon (gemessen: ≈ 10–20 Neubauten und ≈ 80 Regal-Module je 20000 Ticks gegen ≈ 1000 je Rezept); knapp sind sie,
    /// wenn ihre Zutaten fehlen (die dann selbst als knapp zählen) oder kurz nach dem Start. Vorher gingen Bauplätze an eine zweite Werft oder ein drittes
    /// Antriebswerk statt an ein fehlendes Wasserwerk.
    /// </summary>
    public static bool FleetGoodWithProducer(Universe u, GoodId good)
    {
        foreach (var t in u.StationTypes.Values)
        {
            if (t.Export is { } x && x.Goods.Contains(good)) return false;
            foreach (var r in t.Recipes) if (r.Inputs.ContainsKey(good)) return false;
        }
        foreach (var st in u.Stations.Values)
            if (!st.IsBuildSite && st.Produces(good)) return true;
        return false;
    }

    static StationType? CheapestProducer(Universe u, GoodId good) =>
        u.StationTypes.Values.Where(t => t.Recipes.Any(r => r.Outputs.ContainsKey(good)) && Buildable(u, t)).OrderBy(t => t.BuildCost).FirstOrDefault();

    /// <summary>Darf die Wirtschaft diesen Typ bauen? Erforscht (irgendwo), keine Exportsenke, kein Typ eines abgeschalteten Systems.</summary>
    internal static bool Buildable(Universe u, StationType t) =>
        t.Export is null && !FeatureSwitches.OmitFromWorld(u, t) && Research.UnlockedAnywhere(u, UnlockKind.StationType, t.Key);

    /// <summary>Zufälliger Platz mit Abstand zu Gates, Stationen, Feldern und Asteroiden (deterministisch über u.Rng).</summary>
    public static Vec2? FindFreeSpot(Universe u, Sector sector)
    {
        for (int attempt = 0; attempt < 400; attempt++)
        {
            var p = new Vec2(u.Rng.Range(-150, 150), u.Rng.Range(-150, 150));
            bool ok =
                sector.Gates.All(g => Vec2.Distance(p, u.Gates[g].Position) > 50f) &&
                sector.Stations.All(s => Vec2.Distance(p, u.Stations[s].Position) > 50f) &&
                sector.Fields.All(f => Vec2.Distance(p, f.Position) > 45f) &&
                sector.Asteroids.All(a => Vec2.Distance(p, a.Position) > a.Radius + Station.BodyRadius + 6f);
            if (ok) return p;
        }
        return null;
    }
}
