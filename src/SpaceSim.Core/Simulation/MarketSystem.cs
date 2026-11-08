using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Preis = Basispreis * (Wunschbestand / Bestand), begrenzt auf 0.25x … 4x.</summary>
public sealed class MarketSystem : ISimSystem
{
    public void Step(Universe u)
    {
        foreach (var st in u.Stations.Values)
            foreach (var (goodId, e) in st.Market)
            {
                float factor = Math.Clamp(e.Target / Math.Max(e.Amount, 1f), 0.25f, 4f);
                e.Price = u.Goods[goodId].BasePrice * factor;
            }
    }
}
