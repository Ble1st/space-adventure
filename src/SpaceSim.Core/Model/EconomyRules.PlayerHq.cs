namespace SpaceSim.Core.Model;

/// <summary>
/// Eigenes Hauptquartier der Spielerfraktion (docs/POLITIK.md „Eigenes Hauptquartier“). Code aus, Standardwelt an; aus und ohne Gründung ist die
/// NPC-Welt bitgleich (es gibt dann keine Station, die diese Regeln liest).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Die Spielerfraktion darf in einem eigenen Sektor genau ein Hauptquartier gründen (<see cref="PlayerHqType"/>).</summary>
    public bool PlayerHq;
    /// <summary>Stationstyp des Spieler-Hauptquartiers.</summary>
    public string PlayerHqType = "headquarters";
    /// <summary>Mindest-Größenklasse für die Gründung (Index in <see cref="ActorSizeThresholds"/>: 3 = Konzern).</summary>
    public int PlayerHqMinClass = 3;
    /// <summary>
    /// Regierungsstationen der Spielerfraktion (Sektorbehörde, Hauptquartier, übernommene Behörden) buchen ihren Unterhalt immer aus der Spielerkasse,
    /// notfalls ins Minus (wie Liegegebühr und Leihmiete). Aus = Regel bis Runde 45: reicht die Kasse für einen Schritt nicht, bleibt er unbezahlt —
    /// eine leere Kasse hielt Behörde und Hauptquartier also umsonst.
    /// </summary>
    public bool PlayerGovernmentDebt;
}
