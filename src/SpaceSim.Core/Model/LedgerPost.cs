namespace SpaceSim.Core.Model;

/// <summary>
/// Runde 30: ein Merkposten des Rückkaufdeckels für (Spieler-Akteur, Station, Ware). <see cref="BoughtQty"/> Einheiten hat der Spieler hier zum Stückpreis
/// <see cref="BoughtPrice"/> gekauft (Bestand der Station vor dem ersten Kauf: <see cref="BoughtStockBefore"/>); <see cref="SoldQty"/> hat er hier zum
/// Stückpreis <see cref="SoldPrice"/> verkauft (Bestand vor dem ersten Verkauf: <see cref="SoldStockBefore"/>).
/// </summary>
public sealed class LedgerPost
{
    public float BoughtQty, BoughtPrice, BoughtStockBefore;
    public float SoldQty, SoldPrice, SoldStockBefore;
    /// <summary>
    /// Runde 32b: Kauf- bzw. Verkaufsteil wirkt (Deckel/Boden gelten), unabhängig von der Restmenge — eine Gegenbuchung baut nur die Menge ab, beendet den Teil aber
    /// nicht (Prüfung: Teilverkauf leerte den Posten, der Rest ging im selben Tick zum selbst getriebenen Preis). Beendet wird er nur von
    /// <see cref="Trading.PlayerLedger.Prune"/> im Tick, wenn Bestand und Preis zurück sind (frühestens nach <see cref="LastTick"/>).
    /// </summary>
    public bool BoughtOpen, SoldOpen;
    /// <summary>Runde 32b: Tick der letzten Buchung des Spielers an diesem Posten — der Verfall prüft erst, wenn der Markt danach neu bepreist hat.</summary>
    public long LastTick;
    public bool Empty => !BoughtOpen && !SoldOpen && BoughtQty <= 0.0001f && SoldQty <= 0.0001f;
}
