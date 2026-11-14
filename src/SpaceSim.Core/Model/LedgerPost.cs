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
    public bool Empty => BoughtQty <= 0.0001f && SoldQty <= 0.0001f;
}
