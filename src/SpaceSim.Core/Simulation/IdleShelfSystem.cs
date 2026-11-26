using SpaceSim.Core.Content;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 12 (docs/TODO-AUSBAU.md „Ungenutzte Modulregale“): Modulregale — stock-only Markteinträge (<see cref="Station.IsStockOnly"/>) einer
/// Modulware (<see cref="Good.Category"/> = <see cref="ModuleGoods.ModuleCategory"/>), etwa die Modulregale der Werften und Handelsstationen —, deren
/// Bestand sich lange nicht ändert, werden günstiger. Andere stock-only Einträge (Export, Instandhaltung, Forschungslager, Lagerhaus, Munition) bleiben
/// unberührt.
/// <para>
/// Bewegung = jede Änderung des Bestands, gleich über welchen Weg (Kauf/Verkauf über <see cref="Trading.PlayerLedger"/>, Verbrauch durch Schiffsbau der KI,
/// Polizei, Kriegsschiffe und Spieler, Upgrades, Reparatur, Ausrüster …): Alle <see cref="Interval"/> Ticks vergleicht das System den Bestand mit dem
/// zuletzt beobachteten (<see cref="StockEntry.SeenAmount"/>) — eine Nahtstelle statt eines Aufrufs an jedem Verbrauchsweg. Die Buchungsstellen des Handels
/// (<see cref="MarkMoved"/>) merken die Bewegung zusätzlich sofort, damit ein Kauf vom vergünstigten Regal den Preis schon im nächsten Tick zurücksetzt.
/// </para>
/// <para>
/// Ein Regal, das seit <see cref="EconomyRules.IdleShelfTicks"/> Ticks unbewegt ist, senkt sein Ziel um 1 (mindestens 1) — einmal je unbewegter Periode,
/// festgemacht am Übergang der Schwelle; solange es unbewegt ist und etwas zu verkaufen hat, setzt <see cref="MarketSystem"/> seinen Preis auf
/// <see cref="EconomyRules.IdleShelfPriceFactor"/> × (<see cref="Discounted"/>). Deterministisch, kein Zufall.
/// </para>
/// </summary>
public sealed class IdleShelfSystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.IdleShelfActive) return;
        int ticks = u.Rules.IdleShelfTicks;
        foreach (var st in u.StationsById())
        {
            if (st.IsBuildSite) continue;   // Baustellen haben feste Materialpreise
            var roles = st.Roles();
            foreach (var (good, e) in st.Market)
            {
                if (!Tracked(u, st, good, roles)) continue;
                if (e.SeenAmount is not { } seen) e.SeenAmount = e.Amount;   // erste Beobachtung: keine Bewegung (unbewegt seit Weltbeginn)
                else if (MathF.Abs(e.Amount - seen) > 1e-4f) { e.LastMove = u.Tick; e.SeenAmount = e.Amount; }
                long last = e.LastMove ?? 0;
                bool idle = u.Tick - last >= ticks;
                if (idle && u.Tick - dt - last < ticks)   // Schwelle in diesem Intervall überschritten: Ziel einmal senken
                    e.Target = MathF.Max(Floor(u, st, good), e.Target - 1f);
            }
        }
    }

    /// <summary>
    /// Welle D (Punkte 2/3): Untergrenze des Ziels — 1; an Stationen mit Schiffshändler für die großen Module (<see cref="EconomyRules.YardLargeModuleKeys"/>,
    /// mit <see cref="EconomyRules.IdleShelfKeepLargeYard"/>) und die Module in <see cref="EconomyRules.IdleShelfKeepKeys"/> der Wunschbestand des Stationstyps
    /// (bei den großen Modulen also der Welle-B-Wert).
    /// </summary>
    public static float Floor(Universe u, Station st, GoodId good)
    {
        var r = u.Rules;
        if (!r.IdleShelfKeepLargeYard && r.IdleShelfKeepKeys.Count == 0) return 1f;
        if (!u.StationTypes.TryGetValue(st.TypeKey, out var t) || t.ShipDealer is null || u.GoodOf(good) is not { } g) return 1f;
        bool keep = r.IdleShelfKeepKeys.Contains(g.Key) || (r.IdleShelfKeepLargeYard && r.YardLargeModuleKeys.Contains(g.Key));
        if (!keep) return 1f;
        foreach (var s in t.Stocks)
            if (s.Good == good) return MathF.Max(1f, s.Target);
        return 1f;
    }

    /// <summary>Wird dieser Eintrag als Modulregal verfolgt? (Schalter an, keine Baustelle, stock-only, Modulware.)</summary>
    public static bool Tracked(Universe u, Station st, GoodId good, byte[] roles) =>
        u.Rules.IdleShelfActive && !st.IsBuildSite && Station.RoleIn(roles, good) == 0
        && u.GoodOf(good) is { } g && g.Category == ModuleGoods.ModuleCategory;

    /// <summary>
    /// Verkauft das Regal gerade zum vergünstigten Preis? Verfolgt, seit <see cref="EconomyRules.IdleShelfTicks"/> unbewegt und mindestens eine Einheit
    /// im Bestand (ein leeres Regal hat nichts zu verkaufen; sein Gebot für Lieferungen bleibt unverändert, sonst würde es noch seltener aufgefüllt).
    /// </summary>
    public static bool Discounted(Universe u, Station st, GoodId good, StockEntry e, byte[] roles) =>
        e.Amount >= 1f && Tracked(u, st, good, roles) && u.Tick - (e.LastMove ?? 0) >= u.Rules.IdleShelfTicks;

    /// <summary>
    /// Merkt eine Bewegung sofort (Handelsbuchungen <see cref="Trading.PlayerLedger.BookBuy"/>/<see cref="Trading.PlayerLedger.BookSale"/>, nach der
    /// Bestandsänderung). Alle anderen Wege fängt der Bestandsvergleich in <see cref="Step"/> ab. Ohne Schalter bleibt alles null (alte Hashes gleich).
    /// </summary>
    public static void MarkMoved(Universe u, Station st, GoodId good)
    {
        if (st.Market.TryGetValue(good, out var e) && Tracked(u, st, good, st.Roles()))
        {
            e.LastMove = u.Tick;
            e.SeenAmount = e.Amount;
        }
    }
}
