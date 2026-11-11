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
        TryBuild(u);
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

    static void TryBuild(Universe u)
    {
        var rules = u.Rules;
        // Phase 10: Regierungsstationen und Wachen zählen nicht gegen die Obergrenzen
        if (StationProtection.CountEconomic(u) >= rules.MaxStations || u.Tick - u.LastStationBuildTick < rules.BuildCooldown) return;
        // Phase 8: höchstens MaxActiveSites Baustellen gleichzeitig (verlassene zählen nicht)
        var active = ConstructionRules.ActiveSites(u).ToList();
        if (active.Count >= rules.MaxActiveSites) return;

        // Knappste Ware (höchster Durchschnittspreis relativ zum Basispreis) und am stärksten überversorgte Ware (niedrigster)
        GoodId? scarce = null, glut = null;
        float worst = rules.BuildPriceRatio, lowest = rules.GlutPriceRatio;
        var ratios = new Dictionary<GoodId, float>();
        foreach (var good in u.Goods.Values)
        {
            // Knappheit aus den Beständen (Wunschbestand / Bestand über alle Stationen), nicht aus Preisen: Preise sind durch die
            // Zahlungsbereitschaft der Fabriken gedeckelt und würden Überangebot vortäuschen.
            // Phase 8: Baustellen sind keine Märkte (ihr Wunschbestand ist eine Stückliste) und zählen nicht mit
            // Phase 14: Exportsenken zählen mit — ihre Nachfrage (leeres Ankauflager) lässt Hersteller von Endwaren entstehen
            var entries = u.Stations.Values.Where(s => !s.IsBuildSite && s.Market.ContainsKey(good.Id)).Select(s => s.Market[good.Id]).ToList();
            if (entries.Count == 0) continue;
            float ratio = entries.Sum(e => e.Target) / MathF.Max(1f, entries.Sum(e => e.Amount));
            ratios[good.Id] = ratio;
            // Phase 5a: nur Waren, auf die ein Stationstyp antworten kann (Erzeuger bei Knappheit, Rezept-Verbraucher bei Überangebot).
            // Sonst blockiert eine Ware ohne Verbraucher-Typ (Module, Bausatzstufen auf Halde) jeden Verbraucherbau für andere Waren.
            if (ratio > worst && u.StationTypes.Values.Any(t => t.Recipes.Any(r => r.Outputs.ContainsKey(good.Id)))) { worst = ratio; scarce = good.Id; }
            if (ratio < lowest && ratio > 0f && u.StationTypes.Values.Any(t => t.Recipes.Any(r => r.Inputs.ContainsKey(good.Id)))) { lowest = ratio; glut = good.Id; }
        }

        // Knappheit → Produzent der Ware bauen; sonst bei Überangebot → Verbraucher der Ware bauen (Nachfrage wächst mit billigem Angebot)
        // Phase 13: nur Stationstypen, die schon irgendeine Fraktion erforscht hat (ohne Sperre: alle)
        // Phase 14: Exportsenken (Handelsstationen an Planeten) baut die Wirtschaft nie selbst, und Typen eines abgeschalteten Systems
        // (Forschungsstation, reiner Treibstoffhersteller) auch nicht
        StationType? type = null;
        if (scarce is { } sc)
            type = UpstreamProducer(u, sc, ratios, rules.BuildPriceRatio);
        else if (glut is { } gl)
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
    /// entstanden so mehrere Kupferhütten und Waferfabriken ohne Erz). null = nichts bauen. Hergestellte Zutaten lenken nicht um: Ihr Lager beim
    /// Verbraucher ist im Betrieb meist niedrig, das sagt nichts über ihr Angebot.
    /// </summary>
    public static StationType? UpstreamProducer(Universe u, GoodId good, IReadOnlyDictionary<GoodId, float> ratios, float threshold)
    {
        var type = u.StationTypes.Values.Where(t => t.Recipes.Any(r => r.Outputs.ContainsKey(good)) && Buildable(u, t)).OrderBy(t => t.BuildCost).FirstOrDefault();
        if (type is null) return null;
        foreach (var r in type.Recipes.Where(r => r.Outputs.ContainsKey(good)))
            foreach (var g in r.Inputs.Keys)
                if (ratios.TryGetValue(g, out var x) && x > threshold && !u.StationTypes.Values.Any(t => t.Recipes.Any(rr => rr.Outputs.ContainsKey(g)) && Buildable(u, t)))
                    return null;
        return type;
    }

    /// <summary>Darf die Wirtschaft diesen Typ bauen? Erforscht (irgendwo), keine Exportsenke, kein Typ eines abgeschalteten Systems.</summary>
    static bool Buildable(Universe u, StationType t) =>
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
