namespace SpaceSim.Core.Model;

/// <summary>
/// Söldner per Comms (Lücke gegenüber Interstellar Pilot 2, docs/FLOTTEN.md „Söldner“). Schalter im Code aus (Stand vorher), in der Standardwelt an.
/// Ohne Anheuern ändert sich nichts (kein Zufall, keine Zustände).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Söldner anheuern erlaubt (Comms „merc:hire:&lt;Akteur&gt;:&lt;Tage&gt;“).</summary>
    public bool Mercenaries;
    /// <summary>Modell des Söldnerschiffs (Schiff des Anbieters; ohne Ladung, ohne Aufgabe).</summary>
    public string MercenaryModel = "corvette";
    /// <summary>Sold je Spieltag (<see cref="DayTicks"/>), im Voraus aus der Spielerkasse an die Kasse des Anbieters.</summary>
    public float MercenaryFeePerDay = 400f;
    /// <summary>Angebotene Vertragslängen in Tagen.</summary>
    public int[] MercenaryDays = { 1, 3 };
    /// <summary>Höchstens so viele Söldner gleichzeitig.</summary>
    public int MercenaryMaxActive = 2;
    /// <summary>Mindestbeziehung des Anbieters (bzw. seiner Fraktion) zum Spieler.</summary>
    public float MercenaryMinRelation;
}
