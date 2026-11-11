using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 19: Der Stationsbau überprüft seinen Bestand (Stilllegung/Umwidmung). Der Lebenszyklus vergibt seine freien Plätze (bis
/// <see cref="EconomyRules.MaxStations"/>) früh im Spiel; danach gehen Stationen kaum pleite (geschützt, kritisch, subventioniert). Diese Regel gibt einen
/// Platz frei, wenn beides zusammenkommt:
/// <list type="bullet">
/// <item>eine Station produziert seit langem kaum (<see cref="Station.Activity"/> unter <see cref="EconomyRules.RetireMaxActivity"/>), keines ihrer
/// Produkte ist knapp (Hysterese <see cref="EconomyRules.RetireMaxOutputRatio"/>), sie ist nicht vor Pleite geschützt, nicht kritisch, keine Regierungs-,
/// Export- oder Endverbraucherstation und hat ihre Schonfrist hinter sich (<see cref="Useless"/>);</item>
/// <item>eine Ware ist seit mindestens <see cref="EconomyRules.RetireScarceTicks"/> ununterbrochen knapp, alle Plätze sind belegt und der Lebenszyklus
/// würde für sie einen anderen Typ bauen.</item>
/// </list>
/// Dann wird die Station stillgelegt (<see cref="Universe.RemoveStation"/> storniert laufende Aufträge, der Besitzer erhält Kasse und Lager zum Restwert,
/// <see cref="StationRetired"/>), und der Platz geht über den normalen Bauweg (Baustelle, Material, Bauschiff) an den neuen Bedarf. Höchstens eine
/// Stilllegung je <see cref="EconomyRules.RetireCooldownTicks"/>.
/// Entscheidung: Die Regel prüft alle nicht geschützten Stationen, nicht nur vom Lebenszyklus gebaute — die Startwelt markiert ihre Stationen als
/// geschützt (<c>essential</c>), so trifft sie dort nur Gebautes; eine eigene Welt kann Startstationen bewusst ungeschützt lassen.
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
        !st.IsBuildSite && st.Production.Count > 0 && !st.BankruptcyProtected && !st.Immortal
        && !StationProtection.IsOfficial(u, st) && !StationProtection.IsHideout(u, st)
        && ExportRules.ConfigOf(u, st) is null && !MarketSystem.IsFinalConsumer(st);

    /// <summary>
    /// Dauerhaft nutzlos? Prüfbar (<see cref="Reviewable"/>), Schonfrist vorbei, Auslastung unter <see cref="EconomyRules.RetireMaxActivity"/> und keines
    /// ihrer Produkte knapp (<paramref name="ratios"/> aus der Lebenszyklus-Messung, sonst neu gemessen). Kritisch zu sein schützt vor der Stilllegung,
    /// nicht vor diesem Befund (die Subvention fragt ihn ab).
    /// </summary>
    public static bool Useless(Universe u, Station st, IReadOnlyDictionary<GoodId, float>? ratios = null)
    {
        var rules = u.Rules;
        if (!Reviewable(u, st) || st.Activity >= rules.RetireMaxActivity || u.Tick - st.BuiltTick < rules.RetireGraceTicks) return false;
        foreach (var r in st.Production)
            foreach (var g in r.Outputs.Keys)
            {
                float ratio = ratios is not null && ratios.TryGetValue(g, out var x) ? x : Ratio(u, g);
                if (ratio > rules.RetireMaxOutputRatio) return false;
            }
        return true;
    }

    /// <summary>
    /// Stilllegen, wenn die Regel greift (siehe Klassenbeschreibung). Rückgabe: der Typ, für den der Platz frei wurde (der Lebenszyklus legt im selben
    /// Schritt dessen Baustelle an), sonst null.
    /// </summary>
    public static StationType? TryRetire(Universe u, StationLifecycleSystem.LifecycleScan scan)
    {
        var rules = u.Rules;
        if (!rules.StationRetireActive || u.Tick - u.LastStationRetireTick < rules.RetireCooldownTicks) return null;
        if (StationProtection.CountEconomic(u) < rules.MaxStations) return null;               // freie Plätze: der normale Bau antwortet
        if (u.Tick - u.LastStationBuildTick < rules.BuildCooldown) return null;
        var active = ConstructionRules.ActiveSites(u).ToList();
        if (active.Count >= rules.MaxActiveSites) return null;
        if (PersistentScarcity(u, scan) is not { } good) return null;
        var type = StationLifecycleSystem.ProducerFor(u, good, scan.Ratios);
        if (type is null || active.Any(s => s.Site!.TypeKey == type.Key)) return null;

        Station? pick = null;
        foreach (var st in u.StationsById())
        {
            if (st.TypeKey == type.Key || !Useless(u, st, scan.Ratios) || StationCriticality.IsCritical(u, st)) continue;
            if (st.PlayerStorage.Count > 0 || u.Ships.Values.Any(s => s.IsPlayer && s.DockedAt == st.Id)) continue;   // Spielerbesitz an der Station
            if (pick is null || st.Activity < pick.Activity) pick = st;
        }
        if (pick is null) return null;
        Retire(u, pick, good, type.Key);
        return type;
    }

    /// <summary>
    /// Station stilllegen: der Besitzer erhält die Stationskasse (falls positiv) und das Lager zum Restwert (<see cref="EconomyRules.RetireSalvageShare"/> ×
    /// Basispreis; der Rest verfällt), Aufträge werden über <see cref="Universe.RemoveStation"/> storniert, <see cref="StationRetired"/>.
    /// </summary>
    public static float Retire(Universe u, Station st, GoodId forGood, string forType)
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
        u.RemoveStation(st.Id, "retired");
        u.Events.Publish(new StationRetired(st.Id, st.Sector, st.TypeKey, st.Name, st.Owner, paid ? payout : 0f, activity, forGood, forType));
        return paid ? payout : 0f;
    }
}
