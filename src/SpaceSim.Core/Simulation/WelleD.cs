using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Welle D, Punkt 4 (docs/FLOTTEN.md „Welle D“): Nachschub an eigenen Stationen. Gemessen (Spielerbot, Spielstand bei Tick 260 000): eroberte und gegründete
/// Behörden hatten 0 von 8 Hüllenplatten, weil ihre Kasse leer war (19–253 Cr) — die Regalware stand auf dem Knappheitspreis 400 Cr, ein Händler bekommt
/// nichts bezahlt und liefert nicht (die KI-Fraktionen stützen ihre Behörden über <see cref="SubsidySystem"/>, die Spielerfraktion nicht). Mit
/// <see cref="EconomyRules.PlayerStationFunding"/> füllt die Spielerkasse die Kasse eigener Stationen bis zum Wert der fehlenden Regalware auf (Umbuchung
/// wie „+1000“ in „Besitz → Stationen“, <see cref="StationCashMoved"/>); dazu heben eigene Stationen mit Nachladedienst ihre Munitions- und Raketenregale
/// auf die Magazingröße großer Schiffe an.
/// </summary>
public static class PlayerStationFunding
{
    /// <summary>Wert der fehlenden Regalware einer Station: Σ (Ziel − Bestand)⁺ × max(Preis, Basispreis) über alle Lager ohne Rezept. Rein.</summary>
    public static float ShelfNeed(Universe u, Station st)
    {
        float sum = 0f;
        foreach (var (g, e) in st.Market)
        {
            if (!st.IsStockOnly(g)) continue;
            float miss = e.Target - e.Amount;
            if (miss <= 0f) continue;
            sum += miss * MathF.Max(e.Price, u.Goods[g].BasePrice);
        }
        return sum;
    }

    /// <summary>Eigene, fertige, andockbare Station des Spielers?</summary>
    public static bool Applies(Universe u, Station st) =>
        u.PlayerActor.Value != 0 && st.Owner == u.PlayerActor && !st.IsBuildSite && st.Dockable && st.Market.Count > 0;

    /// <summary>
    /// Öffnet eine eigene Station ohne Handelsdienst im Typ den Dienst „trade“ für den Spieler? (<see cref="EconomyRules.PlayerStationFunding"/>, eigene
    /// fertige Station mit Lager ohne Rezept.)
    /// </summary>
    public static bool TradeOpen(Universe u, Station st)
    {
        if (!u.Rules.PlayerStationFunding || !Applies(u, st)) return false;
        if (u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Services.Contains(Services.TradeService.ServiceKey)) return false;
        foreach (var g in st.Market.Keys) if (st.IsStockOnly(g)) return true;
        return false;
    }

    /// <summary>Werden eigene Stationen dieses Typs aufgefüllt? Versorgungsstationen: Typ mit Reparatur oder Nachladen (Werften, Behörden, Wachen).</summary>
    public static bool Supplies(Universe u, Station st) =>
        u.StationTypes.TryGetValue(st.TypeKey, out var t)
        && (t.Services.Contains(Services.RepairService.ServiceKey) || t.Services.Contains(Services.RearmService.ServiceKey));

    /// <summary>Hebt die Munitions-/Raketenregale einer eigenen Station mit Nachladedienst auf die Regel-Mindestziele (idempotent).</summary>
    public static void RaiseShelves(Universe u, Station st)
    {
        if (!u.StationTypes.TryGetValue(st.TypeKey, out var t) || !t.Services.Contains(Services.RearmService.ServiceKey)) return;
        Raise(st, Combat.Rearm.AmmoGood(u)?.Id, u.Rules.PlayerStationAmmoTarget);
        Raise(st, Combat.Missiles.MissileGood(u)?.Id, u.Rules.PlayerStationMissileTarget);
    }

    static void Raise(Station st, GoodId? good, float target)
    {
        if (good is null || target <= 0f || !st.Market.TryGetValue(good.Value, out var e) || !st.IsStockOnly(good.Value)) return;
        if (e.Target < target) e.Target = target;
    }

    /// <summary>Ein Durchlauf (Intervall <paramref name="dt"/>): Regale anheben und Kassen auffüllen, in Id-Reihenfolge.</summary>
    public static void Step(Universe u, int dt)
    {
        if (!u.Rules.PlayerStationFunding || u.PlayerActor.Value == 0) return;
        var wallet = u.PlayerWallet;
        float cap = u.Rules.PlayerStationFundPerDay * dt / MathF.Max(1f, u.Rules.DayTicks);
        float keep = PlayerUpkeepOrder.Reserve(u);   // nie unter den Vorrat der Behörden (Welle C)
        foreach (var st in u.StationsById())
        {
            if (!Applies(u, st) || !Supplies(u, st)) continue;   // nur Versorgungsstationen; Wirtschaftsstationen leben von ihrem Absatz
            RaiseShelves(u, st);
            float need = ShelfNeed(u, st) - st.Credits;
            if (need <= 1f) continue;
            float amount = MathF.Min(MathF.Min(need, cap), wallet.Credits - keep);
            if (amount <= 1f) continue;
            wallet.Credits -= amount;
            st.Credits += amount;
            Profitability.Transfer(st.Profit, amount);   // Umbuchung, kein Geschäftsergebnis
            u.Events.Publish(new StationCashMoved(st.Id, amount));
        }
    }
}

/// <summary>Welle D, Punkt 4: <see cref="PlayerStationFunding"/> im Aktoren-Modul (Intervall 50, nach den Subventionen).</summary>
public sealed class PlayerStationFundingSystem : ISimSystem
{
    public const int Interval = 50;
    public void Step(Universe u, int dt) => PlayerStationFunding.Step(u, dt);
}
