namespace SpaceSim.Core.Model;

/// <summary>
/// Ausbau Punkt 12 (docs/TODO-AUSBAU.md): ungenutzte Regale — stock-only Markteinträge (<see cref="Station.IsStockOnly"/>) von Modulwaren u. a. —
/// werden mit der Zeit günstiger, solange niemand an ihnen handelt. Code-Vorgabe = alte Regel (Schalter aus); die Standardwelt schaltet ein.
/// Gespeichert wie alle Regeln (additiv, Spielstand bleibt v17).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>
    /// Schalter: Ein Regal, das <see cref="IdleShelfTicks"/> lang keine Bewegung (Kauf oder Lieferung) hatte, senkt sein Ziel um 1 (mindestens 1)
    /// und verkauft an Spieler/NPC zum <see cref="IdleShelfPriceFactor"/>-fachen Preis, bis wieder eine Bewegung stattfindet
    /// (<see cref="Simulation.IdleShelfSystem"/> und <see cref="Simulation.MarketSystem"/>).
    /// </summary>
    public bool IdleShelfActive;
    /// <summary>Ab wie vielen Ticks ohne Bewegung ein Regaleintrag als unberührt gilt.</summary>
    public int IdleShelfTicks = 20000;
    /// <summary>Preisfaktor auf den Verkaufspreis eines unberührten Regaleintrags.</summary>
    public float IdleShelfPriceFactor = 0.5f;
}
