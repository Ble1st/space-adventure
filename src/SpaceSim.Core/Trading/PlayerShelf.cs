using SpaceSim.Core.Content;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Trading;

/// <summary>
/// Welle B, Punkt 1: Modulregale (Lager ohne Rezept, Modulware) nehmen von Spielerschiffen mehr an — bis Wunschbestand ×
/// <see cref="EconomyRules.PlayerModuleShelfFactor"/> statt nur bis zum Regal-Limit. So kann der Spieler gekaufte Module an die Werft liefern, die
/// ein Großkampfschiff braucht. NPC-Händler sehen die alte Annahme. Eine Regel für Anzeige (<see cref="MarketInfo.Acceptance"/>) und Ausführung
/// (<see cref="TradeActions.SaleCaps"/>).
/// </summary>
public static class PlayerShelf
{
    /// <summary>Annahme dieses Eintrags für <paramref name="ship"/>: mindestens <paramref name="baseAccept"/>, für Spielerschiffe an Modulregalen mehr.</summary>
    public static float Accept(Universe u, Station st, GoodId good, StockEntry e, Ship? ship, float baseAccept)
    {
        float factor = u.Rules.PlayerModuleShelfFactor;
        if (factor <= 0f || ship is null || ship.Owner != u.PlayerActor || u.PlayerActor.Value == 0) return baseAccept;
        if (!Applies(u, st, good, e)) return baseAccept;
        return MathF.Max(baseAccept, e.Target * factor - e.Amount);
    }

    /// <summary>Ist der Eintrag ein Modulregal (Lager ohne Rezept, Ware der Kategorie Modul) einer fertigen Station?</summary>
    public static bool Applies(Universe u, Station st, GoodId good, StockEntry e) =>
        !st.IsBuildSite && !e.NoIntake && st.IsStockOnly(good) && u.GoodOf(good).Category == ModuleGoods.ModuleCategory;
}
