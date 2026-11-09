using SpaceSim.Core.Model;

namespace SpaceSim.Core.Trading;

/// <summary>Marktlage einer Ware an einer Station, abgeleitet aus Bestand gegen Wunschbestand (Preisbildung siehe <see cref="Simulation.MarketSystem"/>).</summary>
public enum MarketPressure
{
    /// <summary>Bestand unter Wunschbestand: Nachfrage, der Preis steigt über den Basispreis.</summary>
    Scarce,
    /// <summary>Bestand genau auf Wunschbestand.</summary>
    Balanced,
    /// <summary>Bestand über Wunschbestand: Überschuss, der Preis fällt unter den Basispreis.</summary>
    Glut,
}

/// <summary>
/// Reine Core-Funktionen für Marktanzeigen der Oberfläche: beste bekannte Verkaufsstelle je Ware und
/// Nachfrage/Überschuss aus Bestand gegen Ziel. Keine Godot-Abhängigkeit, keine Zustandsänderung.
/// </summary>
public static class MarketInfo
{
    /// <summary>Verhältnis Bestand/Wunschbestand; &lt; 1 = Nachfrage, &gt; 1 = Überschuss (Preisfaktor umgekehrt, siehe MarketSystem).</summary>
    public static float StockRatio(StockEntry e) => e.Target > 0f ? e.Amount / e.Target : 1f;

    /// <summary>Marktlage aus Bestand gegen Wunschbestand.</summary>
    public static MarketPressure Classify(StockEntry e)
    {
        float r = StockRatio(e);
        return r < 1f ? MarketPressure.Scarce : r > 1f ? MarketPressure.Glut : MarketPressure.Balanced;
    }

    /// <summary>
    /// Beste bekannte Verkaufsstelle für eine Ware aus Sicht eines Verkäufers: die Station mit dem höchsten
    /// aktuellen Ankaufspreis unter allen Stationen, die die Ware handeln (ohne <paramref name="exclude"/>).
    /// null, wenn außer der Ausschlussstation niemand die Ware kauft.
    /// </summary>
    public static Station? BestBuyerForGood(Universe u, GoodId good, StationId? exclude = null)
    {
        Station? best = null;
        float bestPrice = 0f;
        foreach (var st in u.StationsTrading(good))
        {
            if (st.Id == exclude) continue;
            if (!st.Market.TryGetValue(good, out var e) || e.Price <= 0f) continue;
            if (e.Price > bestPrice) { bestPrice = e.Price; best = st; }
        }
        return best;
    }
}
