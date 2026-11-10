using SpaceSim.Core.Model;

namespace SpaceSim.Core.Trading;

/// <summary>
/// Reine Core-Funktionen für die Kommandozentrale (UI): Preisniveau je Sektor und Besitzwert eines Schiffs.
/// Keine Zustandsänderung, keine Godot-Abhängigkeit.
/// </summary>
public static class SectorEconomy
{
    /// <summary>
    /// Durchschnittliches Preisniveau der Stationen eines Sektors relativ zu den Basispreisen
    /// (1,0 = Basispreisniveau, &gt; 1 = teurer, &lt; 1 = günstiger). Nur Marktpositionen mit Preis &gt; 0
    /// und Ware mit Basispreis &gt; 0; 1,0, wenn der Sektor keine solchen Daten hat.
    /// </summary>
    public static float PriceLevel(Universe u, SectorId sector)
    {
        if (!u.Sectors.TryGetValue(sector, out var s)) return 1f;
        double sum = 0;
        int n = 0;
        foreach (var sid in s.Stations)
        {
            if (!u.Stations.TryGetValue(sid, out var st)) continue;
            foreach (var (gid, e) in st.Market)
            {
                if (e.Price <= 0f) continue;
                if (!u.Goods.TryGetValue(gid, out var g) || g.BasePrice <= 0f) continue;
                sum += e.Price / g.BasePrice;
                n++;
            }
        }
        return n == 0 ? 1f : (float)(sum / n);
    }

    /// <summary>Wert der Fracht eines Schiffs zu Basispreisen (ohne Marktlage der Stationen).</summary>
    public static float CargoValueBasePrice(Universe u, Ship ship)
    {
        float sum = 0f;
        foreach (var (gid, amount) in ship.Cargo)
            if (u.Goods.TryGetValue(gid, out var g))
                sum += amount * g.BasePrice;
        return sum;
    }

    /// <summary>
    /// Gesamtwert eines Schiffs für die Besitz-Anzeige: eigene Credits + Fracht zu Basispreisen. Seit Phase 5b hat ein
    /// Spielerschiff keine eigenen Credits mehr (das Geld liegt in der Spielerkasse, siehe <see cref="PlayerNetWorth"/>),
    /// sein Wert ist also nur die Fracht; bei NPC-Schiffen ist das Schiff selbst die Kasse.
    /// </summary>
    public static float TotalValue(Universe u, Ship ship) =>
        (ReferenceEquals(u.WalletOf(ship), ship) ? ship.Credits : 0f) + CargoValueBasePrice(u, ship);

    /// <summary>Vermögen der Spielerperson: Spielerkasse + Wert aller Spielerschiffe (<see cref="TotalValue"/>).</summary>
    public static float PlayerNetWorth(Universe u) =>
        u.PlayerWallet.Credits + u.Ships.Values.Where(s => s.IsPlayer).OrderBy(s => s.Id.Value).Sum(s => TotalValue(u, s));
}
