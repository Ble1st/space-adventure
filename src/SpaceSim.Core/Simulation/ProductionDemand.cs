using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 15: Produktion nach Bedarf. Vorher produzierte jede Station jedes Rezept bis zum Dreifachen ihres Wunschbestands
/// (<see cref="EconomyRules.OverstockFactor"/>), gleich ob jemand die Ware haben wollte: Die Modulwerke steckten Stahl, Elektronik und Geld in
/// Powerbanks, Schilde und Zusatzfrachtmodule auf Halde (je bis 15 Stück bei 0,33×), die Werft in 77 leichte Bausätze — und die knappen
/// Mk1-Reaktoren und -Triebwerke fehlten, die Werft war pleite und stand dauerhaft am 4×-Deckel.
/// Regel: <see cref="OpenDemand"/> ist die offene Fehlmenge einer Ware bei allen anderen Stationen, die sie führen, ohne sie herzustellen
/// (Verbraucher, Lager, Baustellen, Exportsenken: Wunschbestand − Bestand). Ein Rezept, für dessen Produkte niemand eine Fehlmenge hat, pausiert
/// schon beim Wunschbestand × <see cref="EconomyRules.NoDemandStockFactor"/> (kleiner Puffer) statt beim Überhang; gefragte Rezepte laufen zuerst
/// (<see cref="ProductionSystem"/>), und eine Station bietet für Zutaten, die nur pausierte Rezepte brauchen, nur noch den Bodenpreis
/// (<see cref="MarketSystem"/>) — sie bindet ihr Geld nicht mehr in Zutaten für Ware ohne Abnehmer.
/// </summary>
public static class ProductionDemand
{
    /// <summary>Offene Fehlmenge je Ware über alle Stationen, die die Ware führen und nicht herstellen (Baustellen: das noch fehlende Material).</summary>
    public static void OpenDemand(Universe u, Dictionary<GoodId, float> into) => OpenDemand(u, into, new HashSet<GoodId>());

    /// <summary>Wie <see cref="OpenDemand(Universe, Dictionary{GoodId, float})"/>; <paramref name="outputs"/> wird nicht mehr gebraucht (Phase 17: Rollen-Cache der Station).</summary>
    public static void OpenDemand(Universe u, Dictionary<GoodId, float> into, HashSet<GoodId> outputs)
    {
        into.Clear();
        foreach (var st in u.Stations.Values)
        {
            if (st.Site is { Abandoned: true }) continue;
            var roles = st.Roles();
            foreach (var (g, e) in st.Market)
            {
                float missing = e.Target - e.Amount;
                if (missing <= 0f || (Station.RoleIn(roles, g) & Station.RoleOutput) != 0) continue;   // Phase 17: Rollen-Cache der Station statt eines Produkt-Puffers je Station
                into[g] = into.GetValueOrDefault(g) + missing;
            }
        }
    }

    /// <summary>
    /// Phase 17 (Leistung): wie <see cref="OpenDemand(Universe, Dictionary{GoodId, float})"/> in ein Feld je Waren-Id (fehlende Waren = 0; gleiche
    /// Summationsreihenfolge, also dieselben Werte). Liefert das (bei Bedarf vergrößerte) Feld zurück.
    /// </summary>
    public static float[] OpenDemand(Universe u, float[]? into)
    {
        int n = u.GoodIdBound;
        if (into is null || into.Length < n) into = new float[n]; else Array.Clear(into);
        foreach (var st in u.Stations.Values)
        {
            if (st.Site is { Abandoned: true }) continue;
            var roles = st.Roles();
            foreach (var (g, e) in st.Market)
            {
                float missing = e.Target - e.Amount;
                if (missing <= 0f || (Station.RoleIn(roles, g) & Station.RoleOutput) != 0) continue;
                if ((uint)g.Value < (uint)into.Length) into[g.Value] = into[g.Value] + missing;
            }
        }
        return into;
    }

    static float DemandIn(float[] demand, GoodId g) => (uint)g.Value < (uint)demand.Length ? demand[g.Value] : 0f;

    /// <summary>Wie <see cref="Demanded(Recipe, IReadOnlyDictionary{GoodId, float})"/> mit dem Feld aus <see cref="OpenDemand(Universe, float[])"/>.</summary>
    public static bool Demanded(Recipe r, float[] demand)
    {
        foreach (var g in r.Outputs.Keys)
            if (DemandIn(demand, g) > 0f) return true;
        return false;
    }

    /// <summary>Wie <see cref="Saturated(Universe, Station, Recipe, IReadOnlyDictionary{GoodId, float})"/> mit dem Feld aus <see cref="OpenDemand(Universe, float[])"/>.</summary>
    public static bool Saturated(Universe u, Station st, Recipe r, float[] demand)
    {
        if (r.Outputs.Count == 0) return false;
        foreach (var g in r.Outputs.Keys)
        {
            var e = st.Stock(g);
            float factor = DemandIn(demand, g) > 0f ? u.Rules.OverstockFactor : MathF.Min(u.Rules.OverstockFactor, u.Rules.NoDemandStockFactor);
            if (e.Amount <= e.Target * factor) return false;
        }
        return true;
    }

    /// <summary>Hat irgendeine andere Station eine offene Fehlmenge für ein Produkt dieses Rezepts?</summary>
    public static bool Demanded(Recipe r, IReadOnlyDictionary<GoodId, float> demand)
    {
        foreach (var g in r.Outputs.Keys)
            if (demand.TryGetValue(g, out var d) && d > 0f) return true;
        return false;
    }

    /// <summary>
    /// Lager voll für dieses Rezept: Alle Produkte liegen über ihrer Pausengrenze — Wunschbestand × <see cref="EconomyRules.OverstockFactor"/>, wenn jemand
    /// das Produkt braucht, sonst Wunschbestand × <see cref="EconomyRules.NoDemandStockFactor"/>. Rezepte ohne Produkte sind nie voll.
    /// </summary>
    public static bool Saturated(Universe u, Station st, Recipe r, IReadOnlyDictionary<GoodId, float> demand)
    {
        if (r.Outputs.Count == 0) return false;
        foreach (var g in r.Outputs.Keys)
        {
            var e = st.Stock(g);
            float factor = demand.TryGetValue(g, out var d) && d > 0f ? u.Rules.OverstockFactor : MathF.Min(u.Rules.OverstockFactor, u.Rules.NoDemandStockFactor);
            if (e.Amount <= e.Target * factor) return false;
        }
        return true;
    }
}
