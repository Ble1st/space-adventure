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
    public void Step(Universe u, int dt)
    {
        foreach (var st in u.Stations.Values)
        {
            foreach (var (goodId, e) in st.Market)
            {
                float factor = Math.Clamp(e.Target / Math.Max(e.Amount, 1f), 0.25f, 4f);
                e.Price = u.Goods[goodId].BasePrice * factor;
            }

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
                    var good = u.Goods[g];
                    float ceiling = outValue * u.Rules.InputWillingness * (qty * good.BasePrice / inBase) / qty;
                    var entry = st.Stock(g);
                    entry.Price = Math.Max(good.BasePrice * 0.25f, Math.Min(entry.Price, ceiling));
                }
            }
        }
    }
}
