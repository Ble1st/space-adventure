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
    readonly Dictionary<GoodId, float> _ceiling = new();

    public void Step(Universe u, int dt)
    {
        foreach (var st in u.Stations.Values)
        {
            if (st.IsBuildSite)
            {
                // Phase 8: Baustellen bieten einen festen Preis für ihr Material (Basispreis × Faktor), unabhängig vom Liefergrad
                foreach (var (goodId, e) in st.Market) e.Price = u.Goods[goodId].BasePrice * u.Rules.ConstructionPriceFactor;
                continue;
            }
            foreach (var (goodId, e) in st.Market)
            {
                float factor = Math.Clamp(e.Target / Math.Max(e.Amount, 1f), 0.25f, 4f);
                e.Price = u.Goods[goodId].BasePrice * factor;
            }

            // Zahlungsbereitschaft je Zutat: das höchste Gebot über alle Rezepte, die sie verwenden (Phase 5a). Eine Station mit
            // mehreren Rezepten (Modulfabrik, Werft mit Bausatzstufen) bietet, was ihre lohnendste Verwendung trägt; mit dem
            // Minimum würde ein Rezept auf Halde (Produkt billig) das Gebot für alle anderen drücken. Bei einem Rezept je Zutat
            // ist das Ergebnis dasselbe wie vorher.
            _ceiling.Clear();
            foreach (var recipe in st.Production)
            {
                if (recipe.Outputs.Count == 0) continue;
                float outValue = 0f;
                foreach (var (g, qty) in recipe.Outputs) outValue += qty * st.Stock(g).Price;
                float inBase = 0f;
                foreach (var (g, qty) in recipe.Inputs) inBase += qty * u.Goods[g].BasePrice;
                if (inBase <= 0f) continue;

                foreach (var (g, qty) in recipe.Inputs)
                {
                    if (recipe.Outputs.ContainsKey(g)) continue;   // Zwischenprodukt der eigenen Station
                    float ceiling = outValue * u.Rules.InputWillingness * (qty * u.Goods[g].BasePrice / inBase) / qty;
                    _ceiling[g] = _ceiling.TryGetValue(g, out var c) ? MathF.Max(c, ceiling) : ceiling;
                }
            }
            foreach (var (g, ceiling) in _ceiling)
            {
                var entry = st.Stock(g);
                entry.Price = Math.Max(u.Goods[g].BasePrice * 0.25f, Math.Min(entry.Price, ceiling));
            }
        }
    }
}
