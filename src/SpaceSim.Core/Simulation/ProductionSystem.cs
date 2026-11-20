using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

public sealed class ProductionSystem : ISimSystem
{
    // Wiederverwendeter Puffer: Rezepte einer Station, die in diesem Tick ihren Takt haben
    readonly List<(Recipe Recipe, bool Demanded, float Priority, int Index)> _due = new();
    // Phase 15: offene Fehlmenge je Ware bei den Abnehmern (einmal je Tick, ProductionDemand)
    float[]? _demand;   // Phase 17: Feld je Waren-Id statt Dictionary

    public void Step(Universe u, int dt)
    {
        bool locks = u.Rules.ResearchActive && u.Research.HasModuleLocks;
        var demand = _demand = ProductionDemand.OpenDemand(u, _demand);
        foreach (var st in u.Stations.Values)
        {
            _due.Clear();
            for (int i = 0; i < st.Production.Count; i++)
            {
                var r = st.Production[i];
                if (r.TicksPerCycle <= 0 || u.Tick % r.TicksPerCycle != 0) continue;
                // Phase 13: Module, die die Fraktion der Station noch nicht erforscht hat, stellt sie nicht her
                if (locks && !OutputsAvailable(u, st, r)) continue;
                _due.Add((r, ProductionDemand.Demanded(r, demand), Priority(u, st, r), i));
            }
            // Phase 5c: Teilen sich mehrere Rezepte einer Station die Zutaten (Modulfabriken, Werft), läuft zuerst das Rezept, dessen
            // Produkt gerade am knappsten ist (höchstes Preis/Basis-Verhältnis), bei Gleichstand in Listenreihenfolge. Vorher lief die
            // Reihenfolge der Modulschlüssel: Frachtmodule auf Halde verbrauchten Stahl und Elektronik, bevor der gefragte Reaktor dran war.
            // Phase 15: Rezepte, deren Produkte ein Abnehmer gerade braucht (offene Fehlmenge), laufen vor solchen ohne Abnehmer.
            if (_due.Count > 1) _due.Sort((a, b) => a.Demanded != b.Demanded ? b.Demanded.CompareTo(a.Demanded)
                : a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : a.Index.CompareTo(b.Index));
            bool ran = false, starved = false;
            // Ausbau Punkt 8: Zustand der Station — Faktor auf Zutaten und Ausstoß je Takt (1 ohne Verschleiß; 0 = Stillstand, zählt als Pause)
            float wear = st.Integrity < 1f ? StationWear.ProductionFactor(u, st) : 1f;
            if (wear <= 0f) _due.Clear();
            foreach (var (r, _, _, _) in _due)
            {
                if (wear < 1f)
                {
                    if (!HasInputs(st, r, wear)) { starved = true; continue; }
                    if (ProductionDemand.Saturated(u, st, r, demand)) continue;
                    foreach (var kv in r.Inputs) st.Stock(kv.Key).Amount -= kv.Value * wear;
                    foreach (var kv in r.Outputs) st.Stock(kv.Key).Amount += kv.Value * wear;
                    ran = true;
                    continue;
                }
                if (!HasInputs(st, r)) { starved = true; continue; }
                // Lager voll: nicht weiter auf Halde produzieren (wirkt wie ein Absatzdeckel, Angebot folgt der Nachfrage). Phase 15: ohne Abnehmer schon
                // beim kleinen Puffer (ProductionDemand.Saturated)
                if (ProductionDemand.Saturated(u, st, r, demand)) continue;
                foreach (var kv in r.Inputs) st.Stock(kv.Key).Amount -= kv.Value;
                foreach (var kv in r.Outputs) st.Stock(kv.Key).Amount += kv.Value;
                ran = true;
            }
            if (_due.Count > 0 || wear <= 0f && HasDue(u, st)) Measure(u, st, ran, !ran && starved);
        }
    }

    /// <summary>
    /// Phase 19: Auslastung der Station nachführen — gleitender Mittelwert über <see cref="EconomyRules.UtilizationWindowTicks"/>; ein fälliger Takt zählt
    /// mit dem Gewicht des kürzesten Rezepttakts der Station (dann wiegt jeder Takt so viel Zeit, wie bis zum nächsten vergeht).
    /// </summary>
    static void Measure(Universe u, Station st, bool ran, bool starved)
    {
        int window = u.Rules.UtilizationWindowTicks;
        if (window <= 0) return;
        int period = int.MaxValue;
        foreach (var r in st.Production) if (r.TicksPerCycle > 0 && r.TicksPerCycle < period) period = r.TicksPerCycle;
        float a = MathF.Min(1f, (float)period / window);
        st.Activity += a * ((ran ? 1f : 0f) - st.Activity);
        st.Starved += a * ((starved ? 1f : 0f) - st.Starved);
    }

    static bool HasInputs(Station st, Recipe r, float factor)
    {
        foreach (var kv in r.Inputs) if (st.Stock(kv.Key).Amount < kv.Value * factor) return false;
        return true;
    }

    /// <summary>Ausbau Punkt 8: hat die Station in diesem Tick einen fälligen Takt (für die Auslastung einer stillstehenden Station)?</summary>
    static bool HasDue(Universe u, Station st)
    {
        foreach (var r in st.Production) if (r.TicksPerCycle > 0 && u.Tick % r.TicksPerCycle == 0) return true;
        return false;
    }

    static bool HasInputs(Station st, Recipe r)
    {
        foreach (var kv in r.Inputs) if (st.Stock(kv.Key).Amount < kv.Value) return false;
        return true;
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
