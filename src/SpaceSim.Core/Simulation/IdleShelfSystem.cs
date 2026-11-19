using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 12 (docs/TODO-AUSBAU.md): Regale (stock-only Markteinträge, <see cref="Station.IsStockOnly"/>), die lange niemand anfährt, werden
/// günstiger. Alle <see cref="Interval"/> Ticks: Ein Regal, das seit <see cref="EconomyRules.IdleShelfTicks"/> Ticks keine Bewegung (Kauf oder
/// Lieferung, <see cref="MarkMoved"/>) hatte, senkt sein Ziel um 1 (mindestens 1) — einmal je unberührter Periode, festgemacht am Übergang der
/// Schwelle. Der Verkaufspreis-Senker liegt im <see cref="MarketSystem"/> (wo die Regalpreise gesetzt werden). Deterministisch, kein Zufall.
/// </summary>
public sealed class IdleShelfSystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.IdleShelfActive) return;
        int ticks = u.Rules.IdleShelfTicks;
        foreach (var st in u.Stations.Values)
        {
            if (st.IsBuildSite) continue;   // Baustellen haben feste Materialpreise
            var roles = st.Roles();
            foreach (var (good, e) in st.Market)
            {
                if (Station.RoleIn(roles, good) != 0) continue;   // nur Regale (stock-only)
                long last = e.LastMove ?? 0;   // nie bewegt = unberührt seit Weltbeginn
                bool idle = u.Tick - last >= ticks;
                if (idle && u.Tick - dt - last < ticks)   // Schwelle in diesem Intervall überschritten: Ziel einmal senken
                    e.Target = MathF.Max(1f, e.Target - 1f);
            }
        }
    }

    /// <summary>
    /// Merkt die letzte Bewegung (Kauf oder Lieferung) an einem Regaleintrag — die eine Buchungsstelle für alles, was Ware gegen Geld über
    /// einen Stationsmarkt bewegt (<see cref="Trading.PlayerLedger.BookBuy"/>/<see cref="Trading.PlayerLedger.BookSale"/>). Ohne Schalter bleibt
    /// <see cref="StockEntry.LastMove"/> null (alte Hashes bleiben gleich).
    /// </summary>
    public static void MarkMoved(Universe u, Station st, GoodId good)
    {
        if (u.Rules.IdleShelfActive && st.Market.TryGetValue(good, out var e) && st.IsStockOnly(good))
            e.LastMove = u.Tick;
    }
}
