using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Preisbildung je Station und Ware.
/// 1) Knappheitspreis: Basispreis * (Wunschbestand / Bestand), begrenzt auf 0.25x … 4x.
/// 2) Zahlungsbereitschaft: Eine Fabrik (Station mit Produkten) bietet für ihre Zutaten höchstens <see cref="EconomyRules.InputWillingness"/>
///    des Werts ihrer Produkte zu aktuellen Preisen, anteilig nach Basiswert der Zutaten. So treibt ein knappes Erz den Preis nicht über
///    das, was die Fabrik damit verdienen kann (sonst würde sie ausbluten, siehe Balancing).
/// Reine Verbraucher (Rezepte ohne Produkte) bezahlen den Knappheitspreis; sie werden über ihre Einnahmen finanziert.
/// </summary>
public sealed class MarketSystem : ISimSystem
{
    // Wiederverwendeter Puffer: höchstes Gebot je Zutat über alle Rezepte einer Station
    // Phase 17: Feld je Waren-Id (+ Stempel „gesetzt in dieser Station“) und Liste der gesetzten Waren in Einfügereihenfolge (= Reihenfolge des
    // früheren Dictionarys, das nie Einträge entfernte)
    float[] _ceiling = Array.Empty<float>();
    int[] _ceilingStamp = Array.Empty<int>();
    int _stamp;
    readonly List<GoodId> _ceilingGoods = new();
    // Phase 15: offene Fehlmenge je Ware (ProductionDemand) und die Zutaten, die ein nicht pausiertes Rezept der Station braucht
    float[]? _demand;
    readonly HashSet<GoodId> _wanted = new();
    // Phase 16: höchstes Gebot je Ware bei Stationen, die sie annehmen (Preise des Vortakts) — der Wert, den ein Hersteller für sein Produkt erzielen kann
    // Phase 17: Feld je Waren-Id; 0 = kein Abnehmer (ein Gebot ist immer > 0, BuyerBids nimmt nur positive Preise)
    float[] _bid = Array.Empty<float>();

    public void Step(Universe u, int dt)
    {
        var demand = _demand = ProductionDemand.OpenDemand(u, _demand);
        int bound = u.GoodIdBound;
        if (_bid.Length < bound) { _bid = new float[bound]; _ceiling = new float[bound]; _ceilingStamp = new int[bound]; }
        if (u.Rules.WillingnessFromBuyers) BuyerBids(u, _bid); else Array.Clear(_bid);
        foreach (var st in u.Stations.Values)
        {
            if (st.IsBuildSite)
            {
                // Phase 8: Baustellen bieten einen festen Preis für ihr Material (Basispreis × Faktor), unabhängig vom Liefergrad
                foreach (var (goodId, e) in st.Market) e.Price = u.GoodOf(goodId).BasePrice * u.Rules.ConstructionPriceFactor;
                continue;
            }
            // Phase 17: Rollen je Ware aus dem Rollen-Cache der Station (Zutat/Produkt) statt zweier Puffer je Station und Tick
            var roles = st.Roles();
            foreach (var (goodId, e) in st.Market)
            {
                e.Price = StockPrice(u.GoodOf(goodId), e, e.Amount);
                e.Overstock = 0f;
                e.NoIntake = Station.RoleIn(roles, goodId) == Station.RoleOutput;   // Phase 15: Hersteller kaufen ihr eigenes Produkt nicht zu
                // Runde 25 (Messregel, Standard aus): ein Hersteller verkauft sein reines Produkt höchstens zu ProducerPriceCapShare × dem besten Gebot eines
                // Abnehmers (Vortag) — die Spanne bleibt für den Händler
                if (u.Rules.ProducerPriceCapShare > 0f && e.NoIntake && (uint)goodId.Value < (uint)_bid.Length && _bid[goodId.Value] > 0f)
                    e.Price = MathF.Max(u.GoodOf(goodId).BasePrice * 0.25f, MathF.Min(e.Price, _bid[goodId.Value] * u.Rules.ProducerPriceCapShare));
            }
            // Phase 15: Regal-Einträge (Lager ohne Rezept: Werft- und Handelsstationsregale, Lagerhaus) nehmen nur bis zum Wunschbestand × ShelfOverstockFactor
            // an (vorher × OverstockFactor 3): Die reiche Handelsstation Beta horte sonst bis 9 Reaktoren, die der Werft fehlten, und unbenutzte Module
            // fielen auf 0,27–0,33×. Exportwaren behalten den Überhang (ExportSystem setzt ihn nach dem Markt), sonst sänke die Exportmenge.
            // Forschungsstationen verbrauchen ihr Lager (Takt-Güter) — kein Regal, sie behalten den Überhang als Puffer
            if (u.Rules.ShelfOverstockFactor < u.Rules.OverstockFactor && !(u.StationTypes.TryGetValue(st.TypeKey, out var stType) && stType.Research is not null))
                foreach (var (goodId, e) in st.Market)
                    if (Station.RoleIn(roles, goodId) == 0) e.Overstock = u.Rules.ShelfOverstockFactor;   // = Station.IsStockOnly
            // Phase 15: reine Instandhaltungs-Einträge bieten höchstens MaintenanceMaxBidFactor × Basispreis (sie sollen Fabriken nicht die Zutaten wegbieten)
            if (u.Rules.MaintenanceShare > 0f)
                foreach (var goodId in MaintenanceGoods(u))
                    if (st.Market.TryGetValue(goodId, out var e) && Station.RoleIn(roles, goodId) == 0 && Maintenance.IsPureTypeEntry(u, st, goodId))
                        e.Price = MathF.Min(e.Price, u.GoodOf(goodId).BasePrice * u.Rules.MaintenanceMaxBidFactor);

            // Zahlungsbereitschaft je Zutat: das höchste Gebot über alle Rezepte, die sie verwenden (Phase 5a). Eine Station mit
            // mehreren Rezepten (Modulfabrik, Werft mit Bausatzstufen) bietet, was ihre lohnendste Verwendung trägt; mit dem
            // Minimum würde ein Rezept auf Halde (Produkt billig) das Gebot für alle anderen drücken. Bei einem Rezept je Zutat
            // ist das Ergebnis dasselbe wie vorher.
            _stamp++;
            _ceilingGoods.Clear();
            foreach (var recipe in st.Production)
            {
                if (recipe.Outputs.Count == 0) continue;
                float outValue = 0f;
                // Phase 16: Produktwert = eigener Preis, höchstens das beste Gebot eines Abnehmers (sonst bot die Kupferhütte bei leerem eigenen Kupferlager
                // nach ihrem 4×-Knappheitspreis für Erz, verkaufte das Kupfer danach aber nur zum Gebot der Elektronikfabrik und ging pleite)
                foreach (var (g, qty) in recipe.Outputs)
                {
                    float own = st.Stock(g).Price;
                    float bid = (uint)g.Value < (uint)_bid.Length ? _bid[g.Value] : 0f;
                    outValue += qty * (bid > 0f ? MathF.Min(own, bid) : own);
                }
                float inBase = 0f;
                foreach (var (g, qty) in recipe.Inputs) inBase += qty * u.GoodOf(g).BasePrice;
                if (inBase <= 0f) continue;

                foreach (var (g, qty) in recipe.Inputs)
                {
                    if (recipe.Outputs.ContainsKey(g)) continue;   // Zwischenprodukt der eigenen Station
                    float ceiling = outValue * u.Rules.InputWillingness * (qty * u.GoodOf(g).BasePrice / inBase) / qty;
                    int gi = g.Value;
                    if (_ceilingStamp[gi] == _stamp) _ceiling[gi] = MathF.Max(_ceiling[gi], ceiling);
                    else { _ceilingStamp[gi] = _stamp; _ceiling[gi] = ceiling; _ceilingGoods.Add(g); }
                }
            }
            foreach (var g in _ceilingGoods)
            {
                var entry = st.Stock(g);
                entry.Price = Math.Max(u.GoodOf(g).BasePrice * 0.25f, Math.Min(entry.Price, _ceiling[g.Value]));
            }
            // Phase 15: Zutaten, die nur Rezepte mit vollem Lager brauchen (ProductionDemand.Saturated), nimmt die Station nur noch bis zum Wunschbestand an
            // (statt bis zum Überhang) — sie bindet kein Geld in Material für Ware, die niemand abnimmt (vorher kaufte z. B. die Werft 250 Hüllenplatten für
            // 77 Bausätze auf Halde und war dann pleite). Der Preis bleibt der Knappheitspreis (ein Bodenpreis hätte den Markt als Schwemme erscheinen lassen).
            if (_ceilingGoods.Count > 0)
            {
                _wanted.Clear();
                bool paused = false;
                foreach (var recipe in st.Production)
                {
                    if (recipe.Outputs.Count == 0 || !ProductionDemand.Saturated(u, st, recipe, demand)) foreach (var g in recipe.Inputs.Keys) _wanted.Add(g);
                    else paused = true;
                }
                if (paused)
                    foreach (var g in _ceilingGoods)
                        if (!_wanted.Contains(g)) st.Stock(g).Overstock = MathF.Min(u.Rules.OverstockFactor, u.Rules.NoDemandStockFactor);
            }
            // Phase 14: Endverbraucher (Rezepte ohne Produkte, z. B. Kolonie) bieten höchstens ConsumerMaxBidFactor × Basispreis. Ohne Deckel war die Kolonie
            // zweistabil: knapper Bestand → 4× Preis → Einnahmen reichen für zu wenig Ware → Bestand bleibt knapp (dauerhaft am Preisdeckel, Kasse leer).
            if (u.Rules.ConsumerMaxBidFactor < 4f && IsFinalConsumer(st))
                foreach (var recipe in st.Production)
                    foreach (var g in recipe.Inputs.Keys)
                        if (st.Market.TryGetValue(g, out var ce)) ce.Price = MathF.Min(ce.Price, u.GoodOf(g).BasePrice * u.Rules.ConsumerMaxBidFactor);
        }
    }

    /// <summary>
    /// Die Bestandsformel des Markts (vor den Deckeln der Station): Basispreis × clamp(Wunschbestand ÷ max(Bestand, 1), 0,25, 4). Runde 25: öffentlich, damit
    /// die Abnehmer-Auskunft (<see cref="Trading.MarketInfo.ArrivalPrice"/>) dieselbe Rechnung mit dem Bestand bei Ankunft benutzt.
    /// </summary>
    public static float StockPrice(Good good, StockEntry e, float amount) => good.BasePrice * Math.Clamp(e.Target / Math.Max(amount, 1f), 0.25f, 4f);

    // Phase 15: Instandhaltungswaren (aus den Regeln aufgelöst), zwischengespeichert je Regelsatz
    readonly List<GoodId> _maintGoods = new();
    EconomyRules? _maintFor;
    IReadOnlyList<GoodId> MaintenanceGoods(Universe u)
    {
        if (!ReferenceEquals(_maintFor, u.Rules))
        {
            _maintGoods.Clear();
            foreach (var key in u.Rules.MaintenanceMaterials.Keys.OrderBy(k => k, StringComparer.Ordinal))
                if (u.FindGood(key) is { } g) _maintGoods.Add(g.Id);
            _maintFor = u.Rules;
        }
        return _maintGoods;
    }

    /// <summary>
    /// Phase 16: höchstes aktuelles Gebot je Ware über alle fertigen Stationen, die sie abnehmen (Verbraucher, Regale, Exportsenken, Instandhaltung —
    /// nicht Hersteller ihres reinen Produkts, <see cref="StockEntry.NoIntake"/>). Waren ohne Abnehmer fehlen in der Liste. Reine Auswertung über die
    /// gespeicherten Preise des Vortakts; bewusst ohne die abgeleiteten Annahmegrenzen (Überhang), die nach dem Laden erst ein Markttakt wieder setzt —
    /// sonst liefe ein geladener Stand anders weiter. Ein voller Abnehmer bietet ohnehin wenig (Preis ∝ Wunsch/Bestand).
    /// </summary>
    public static void BuyerBids(Universe u, Dictionary<GoodId, float> into)
    {
        into.Clear();
        var arr = new float[u.GoodIdBound];
        BuyerBids(u, arr);
        for (int i = 0; i < arr.Length; i++)   // Schlüssel wie vorher: jede Ware mit mindestens einem Abnehmer (Gebote sind > 0)
            if (arr[i] > 0f) into[new GoodId(i)] = arr[i];
    }

    /// <summary>Phase 17 (Leistung): wie <see cref="BuyerBids(Universe, Dictionary{GoodId, float})"/> in ein Feld je Waren-Id (0 = kein Abnehmer).</summary>
    public static void BuyerBids(Universe u, float[] into)
    {
        Array.Clear(into);
        foreach (var st in u.Stations.Values)
        {
            if (st.IsBuildSite) continue;
            var roles = st.Roles();
            foreach (var (g, e) in st.Market)
            {
                if (e.Price <= 0f || Station.RoleIn(roles, g) == Station.RoleOutput) continue;   // reiner Hersteller (Produces && !Consumes)
                int i = g.Value;
                if ((uint)i < (uint)into.Length && e.Price > into[i]) into[i] = e.Price;
            }
        }
    }

    /// <summary>Phase 14: Endverbraucher — hat Rezepte, die alle nichts herstellen (Kolonie).</summary>
    public static bool IsFinalConsumer(Station st)
    {
        if (st.Production.Count == 0) return false;
        foreach (var r in st.Production) if (r.Outputs.Count > 0) return false;
        return true;
    }
}
