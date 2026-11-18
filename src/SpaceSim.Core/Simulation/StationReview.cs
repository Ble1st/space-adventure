using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 19/22: Überprüfung des Stationsbestands (Stilllegung/Umwidmung). Phase 22: Eine Station verschwindet wegen Grund c erst, wenn sie <em>länger nicht
/// rentabel</em> ist (<see cref="Profitability.Unprofitable"/>: Schonfrist seit Bau, Mittelwert des Netto-Geldflusses über das lange Fenster unter null, mit
/// Hysterese; die Uhr ruht hinter einer physischen Sperre) — die Phase-19-Näherung „Auslastung unter 10 %“ ist ersetzt. Weiterhin geprüft wird nur, was die
/// Wirtschaft entbehren kann (<see cref="Useless"/>): nicht vor Pleite geschützt, keine Regierungs-, Export- oder Endverbraucherstation, kein Versteck, keines
/// ihrer Produkte knapp (Hysterese <see cref="EconomyRules.RetireMaxOutputRatio"/>), nie die letzte erreichbare Herstellerin (<see cref="StationCriticality"/>)
/// und nie die letzte eigene Station eines Typs, den die Fraktion für ihre Versorgung braucht (<see cref="FactionViability.KeepsOwnSupply"/>).
/// Die Stilllegung (<see cref="Retire"/>) gibt dem Besitzer Kasse und Restwert; ist eine Ware seit <see cref="EconomyRules.RetireScarceTicks"/> knapp und
/// würde der Lebenszyklus für sie einen anderen Typ bauen, geht der Platz im selben Schritt an diesen Bedarf (Umwidmung). Höchstens eine Stilllegung je
/// <see cref="EconomyRules.RetireCooldownTicks"/>. Phase 22: Die Stilllegung hängt nicht mehr an vollen Plätzen — eine dauerhaft unrentable Station verfällt.
/// </summary>
public static class StationReview
{
    /// <summary>Knappheitsbeginn je Ware nachführen (<see cref="Universe.ScarceSince"/>): neue knappe Waren eintragen, nicht mehr knappe entfernen.</summary>
    public static void TrackScarcity(Universe u, StationLifecycleSystem.LifecycleScan scan)
    {
        foreach (var g in scan.ScarceGoods) u.ScarceSince.TryAdd(g, u.Tick);
        if (u.ScarceSince.Count == scan.ScarceGoods.Count) return;
        foreach (var g in u.ScarceSince.Keys.Where(g => !scan.ScarceGoods.Contains(g)).ToList()) u.ScarceSince.Remove(g);
    }

    /// <summary>Die am stärksten knappe Ware, die seit mindestens <see cref="EconomyRules.RetireScarceTicks"/> ununterbrochen knapp ist; null = keine.</summary>
    public static GoodId? PersistentScarcity(Universe u, StationLifecycleSystem.LifecycleScan scan)
    {
        GoodId? best = null;
        float worst = 0f;
        foreach (var g in scan.ScarceGoods)
        {
            if (!u.ScarceSince.TryGetValue(g, out var since) || u.Tick - since < u.Rules.RetireScarceTicks) continue;
            float r = scan.Ratios[g];
            if (r > worst) { worst = r; best = g; }
        }
        return best;
    }

    /// <summary>Verhältnis Wunsch/Bestand einer Ware über alle fertigen Halter (wie der Lebenszyklus); 0 ohne Halter.</summary>
    public static float Ratio(Universe u, GoodId good)
    {
        double target = 0, amount = 0;
        bool any = false;
        foreach (var st in u.Stations.Values)
        {
            if (st.IsBuildSite || !st.Market.TryGetValue(good, out var e)) continue;
            any = true; target += e.Target; amount += e.Amount;
        }
        return any ? (float)target / MathF.Max(1f, (float)amount) : 0f;
    }

    /// <summary>Darf die Regel diese Station überhaupt prüfen? Fertig, mit Rezepten, nicht geschützt, keine Regierungs-/Export-/Endverbraucherstation, kein Versteck.</summary>
    public static bool Reviewable(Universe u, Station st) =>
        !st.IsBuildSite && st.Production.Count > 0 && !st.BankruptcyProtected && !st.Immortal && st.Owner != u.PlayerActor   // Kapitulation: Spielerbesitz verfällt nicht
        && !StationProtection.IsOfficial(u, st) && !StationProtection.IsHideout(u, st)
        && ExportRules.ConfigOf(u, st) is null && !MarketSystem.IsFinalConsumer(st);

    /// <summary>
    /// Dauerhaft nutzlos? Prüfbar (<see cref="Reviewable"/>), Schonfrist seit Bau vorbei, länger nicht rentabel (Phase 22, <see cref="Profitability.Unprofitable"/>;
    /// vorher Auslastung unter <see cref="EconomyRules.RetireMaxActivity"/>) und keines ihrer Produkte knapp (<paramref name="ratios"/> aus der
    /// Lebenszyklus-Messung, sonst neu gemessen). Kritisch zu sein schützt vor der Stilllegung, nicht vor diesem Befund (die Subvention fragt ihn ab).
    /// </summary>
    public static bool Useless(Universe u, Station st, IReadOnlyDictionary<GoodId, float>? ratios = null)
    {
        var rules = u.Rules;
        if (!Reviewable(u, st) || !Profitability.Unprofitable(u, st.Profit) || u.Tick - st.BuiltTick < rules.RetireGraceTicks) return false;
        foreach (var r in st.Production)
            foreach (var g in r.Outputs.Keys)
            {
                float ratio = ratios is not null && ratios.TryGetValue(g, out var x) ? x : Ratio(u, g);
                if (ratio > rules.RetireMaxOutputRatio) return false;
            }
        return true;
    }

    /// <summary>Darf eine Stilllegung diese (nutzlose) Station nehmen? Nicht kritisch, nicht die letzte eigene Versorgung der Fraktion, kein Spielerbesitz.</summary>
    public static bool MayRetire(Universe u, Station st)
    {
        if (StationCriticality.IsCritical(u, st) || !FactionViability.KeepsOwnSupply(u, st)) return false;
        return st.PlayerStorage.Count == 0 && !u.Ships.Values.Any(s => s.IsPlayer && s.DockedAt == st.Id);   // Spielerbesitz an der Station
    }

    /// <summary>
    /// Stilllegen, wenn die Regel greift (siehe Klassenbeschreibung): die nutzlose Station mit dem schlechtesten Geldfluss. Rückgabe: der Typ, für den der Platz
    /// frei wurde (Umwidmung; der Lebenszyklus legt im selben Schritt dessen Baustelle an), sonst null — auch wenn ohne Umwidmung stillgelegt wurde.
    /// </summary>
    public static StationType? TryRetire(Universe u, StationLifecycleSystem.LifecycleScan scan)
    {
        var rules = u.Rules;
        if (!rules.StationRetireActive || u.Tick - u.LastStationRetireTick < rules.RetireCooldownTicks) return null;
        // Phase 22: Umwidmung — eine anhaltend knappe Ware, deren Herstellertyp gerade nicht gebaut wird und für die ein Platz frei werden muss
        StationType? type = null;
        GoodId good = default;
        bool full = StationProtection.CountEconomic(u) >= rules.MaxStations;
        var active = ConstructionRules.ActiveSites(u).ToList();
        if (full && u.Tick - u.LastStationBuildTick >= rules.BuildCooldown && active.Count < rules.MaxActiveSites && PersistentScarcity(u, scan) is { } g
            && StationLifecycleSystem.ProducerFor(u, g, scan.Ratios) is { } t && !active.Any(s => s.Site!.TypeKey == t.Key) && !StationLifecycleSystem.TypeStarved(u, t))
        { type = t; good = g; }

        Station? pick = null;
        foreach (var st in u.StationsById())
        {
            if ((type is not null && st.TypeKey == type.Key) || !Useless(u, st, scan.Ratios) || !MayRetire(u, st)) continue;
            if (pick is null || st.Profit.Rate < pick.Profit.Rate) pick = st;
        }
        if (pick is null) return null;
        Retire(u, pick, good, type?.Key ?? "");
        return type;
    }

    /// <summary>
    /// Phase 20: Überzahl abbauen. Liegt die Zahl der Wirtschaftsstationen über <see cref="EconomyRules.MaxStations"/> (Panik-Ersatz aus der Notreserve) und hängt
    /// die Karte wieder zusammen, wird eine dauerhaft nutzlose Station (<see cref="Useless"/>, nicht kritisch) stillgelegt — Panik-Stationen zuerst, dann die
    /// geringste Auslastung; höchstens eine je <see cref="EconomyRules.RetireCooldownTicks"/>, ohne Neubau an ihrer Stelle. Solange die Karte zerfallen ist,
    /// bleibt der Ersatz (er ist dort die letzte Herstellerin). true = stillgelegt.
    /// </summary>
    public static bool TryRetireSurplus(Universe u, StationLifecycleSystem.LifecycleScan scan)
    {
        var rules = u.Rules;
        if (!rules.StationRetireActive || u.Tick - u.LastStationRetireTick < rules.RetireCooldownTicks) return false;
        if (StationProtection.CountEconomic(u) <= rules.MaxStations || MapParts.Of(u).IsSplit) return false;
        Station? pick = null;
        foreach (var st in u.StationsById())
        {
            if (!Useless(u, st, scan.Ratios) || !MayRetire(u, st)) continue;
            if (pick is null || (st.Panic && !pick.Panic) || (st.Panic == pick.Panic && st.Profit.Rate < pick.Profit.Rate)) pick = st;
        }
        if (pick is null) return false;
        Retire(u, pick, default, "", "surplus");
        return true;
    }

    /// <summary>
    /// Station stilllegen: der Besitzer erhält die Stationskasse (falls positiv) und das Lager zum Restwert (<see cref="EconomyRules.RetireSalvageShare"/> ×
    /// Basispreis; der Rest verfällt), Aufträge werden über <see cref="Universe.RemoveStation"/> storniert, <see cref="StationRetired"/>.
    /// </summary>
    public static float Retire(Universe u, Station st, GoodId forGood, string forType, string detail = "retired")
    {
        float stock = 0f;
        foreach (var (g, e) in st.Market.OrderBy(k => k.Key.Value)) stock += MathF.Max(0f, e.Amount) * u.Goods[g].BasePrice;
        float payout = MathF.Max(0f, st.Credits) + stock * MathF.Max(0f, u.Rules.RetireSalvageShare);
        bool paid = u.Actors.TryGetValue(st.Owner, out var owner);
        if (paid && payout > 0f)
        {
            owner!.Treasury.Credits += payout;
            u.Events.Publish(new TreasuryIncome(st.Owner, "salvage", payout));
        }
        st.Credits = 0f;
        float activity = st.Activity;
        u.LastStationRetireTick = u.Tick;
        Departures.RemoveStation(u, st, DepartureReason.Unprofitable, detail);   // Phase 22: Grund c
        u.Events.Publish(new StationRetired(st.Id, st.Sector, st.TypeKey, st.Name, st.Owner, paid ? payout : 0f, activity, forGood, forType));
        return paid ? payout : 0f;
    }
}
