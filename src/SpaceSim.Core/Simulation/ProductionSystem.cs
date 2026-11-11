using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

public sealed class ProductionSystem : ISimSystem
{
    // Wiederverwendeter Puffer: Rezepte einer Station, die in diesem Tick ihren Takt haben
    readonly List<(Recipe Recipe, bool Demanded, float Priority, int Index)> _due = new();
    // Phase 15: offene Fehlmenge je Ware bei den Abnehmern (einmal je Tick, ProductionDemand)
    readonly Dictionary<GoodId, float> _demand = new();
    readonly HashSet<GoodId> _scratch = new();

    public void Step(Universe u, int dt)
    {
        bool locks = u.Rules.ResearchActive && u.Research.HasModuleLocks;
        ProductionDemand.OpenDemand(u, _demand, _scratch);
        foreach (var st in u.Stations.Values)
        {
            _due.Clear();
            for (int i = 0; i < st.Production.Count; i++)
            {
                var r = st.Production[i];
                if (r.TicksPerCycle <= 0 || u.Tick % r.TicksPerCycle != 0) continue;
                // Phase 13: Module, die die Fraktion der Station noch nicht erforscht hat, stellt sie nicht her
                if (locks && !OutputsAvailable(u, st, r)) continue;
                _due.Add((r, ProductionDemand.Demanded(r, _demand), Priority(u, st, r), i));
            }
            // Phase 5c: Teilen sich mehrere Rezepte einer Station die Zutaten (Modulfabriken, Werft), läuft zuerst das Rezept, dessen
            // Produkt gerade am knappsten ist (höchstes Preis/Basis-Verhältnis), bei Gleichstand in Listenreihenfolge. Vorher lief die
            // Reihenfolge der Modulschlüssel: Frachtmodule auf Halde verbrauchten Stahl und Elektronik, bevor der gefragte Reaktor dran war.
            // Phase 15: Rezepte, deren Produkte ein Abnehmer gerade braucht (offene Fehlmenge), laufen vor solchen ohne Abnehmer.
            if (_due.Count > 1) _due.Sort((a, b) => a.Demanded != b.Demanded ? b.Demanded.CompareTo(a.Demanded)
                : a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : a.Index.CompareTo(b.Index));
            foreach (var (r, _, _, _) in _due)
            {
                if (!r.Inputs.All(kv => st.Stock(kv.Key).Amount >= kv.Value)) continue;
                // Lager voll: nicht weiter auf Halde produzieren (wirkt wie ein Absatzdeckel, Angebot folgt der Nachfrage). Phase 15: ohne Abnehmer schon
                // beim kleinen Puffer (ProductionDemand.Saturated)
                if (ProductionDemand.Saturated(u, st, r, _demand)) continue;
                foreach (var kv in r.Inputs) st.Stock(kv.Key).Amount -= kv.Value;
                foreach (var kv in r.Outputs) st.Stock(kv.Key).Amount += kv.Value;
            }
        }
    }

    static bool OutputsAvailable(Universe u, Station st, Recipe r)
    {
        foreach (var g in r.Outputs.Keys)
            if (!Research.GoodAvailable(u, st, g)) return false;
        return true;
    }

    /// <summary>Knappheit der Produkte eines Rezepts: Σ Marktpreis / Σ Basispreis der Ausgänge (0 ohne Ausgänge).</summary>
    static float Priority(Universe u, Station st, Recipe r)
    {
        float price = 0f, basis = 0f;
        foreach (var (g, qty) in r.Outputs)
        {
            price += qty * (st.Market.TryGetValue(g, out var e) ? e.Price : 0f);
            basis += qty * u.Goods[g].BasePrice;
        }
        return basis > 0f ? price / basis : 0f;
    }
}
