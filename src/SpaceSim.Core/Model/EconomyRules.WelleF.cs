namespace SpaceSim.Core.Model;

/// <summary>
/// Welle F (drei Sonnet-Spieltests „alle elf Sektoren erobern und halten“, docs/POLITIK.md und docs/FLOTTEN.md „Welle F“). Code-Vorgabe AUS = altes
/// Verhalten, die Standardwelt schaltet alles an. Alle Punkte wirken nur auf den Spieler (Reich, Flotte) bzw. auf laufende Kriege — die NPC-Welt ohne
/// Krieg und ohne Spielerreich bleibt bitgleich.
/// </summary>
public sealed partial class EconomyRules
{
    // ───────── 1: das Reich trägt sich ─────────

    /// <summary>
    /// Instandhaltung der Regierungsstationen des Spielers (Behörden, eigenes/erobertes Hauptquartier, Polizeiwachen) als Anteil des Typbedarfs — wie ihr
    /// Unterhalt mit <see cref="PlayerGovernmentUpkeepShare"/>. Weniger Materialverbrauch, keine Geldquelle. 0 = alte Regel (voller Bedarf).
    /// </summary>
    public float PlayerGovernmentMaintenanceShare;

    // ───────── 2: Flottentreibstoff hat Vorrang ─────────

    /// <summary>
    /// Spielerschiffe: an einer Tankstelle mindestens bis zur nächsten anderen Tankstelle plus Reserve tanken, notfalls mit Schuld bis
    /// <see cref="PlayerFuelDebtLimit"/>; ein Notkauf kauft auf einmal genug bis zur nächsten Tankstelle (oder gar nichts) und ein Schiff, dessen Notfall
    /// sich nicht lösen lässt (pleite, leerer Tank), wartet <see cref="PlayerStrandedWaitTicks"/> statt den Befehl jeden Tick neu zu starten. Aus = alte Regel.
    /// </summary>
    public bool PlayerFuelPriority;
    /// <summary>So weit darf die Spielerkasse für Pflichttreibstoff ins Minus gehen (Cr).</summary>
    public float PlayerFuelDebtLimit;
    /// <summary>Wartezeit eines liegengebliebenen Spielerschiffs bis zum nächsten Versuch (Ticks).</summary>
    public int PlayerStrandedWaitTicks = 200;

    // ───────── 3: Krieg endet „broke“ erst nach anhaltender Pleite ─────────

    /// <summary>
    /// Frieden wegen leerer Kasse erst, wenn die Kasse einer Seite so viele Ticks ununterbrochen unter ihrer Rücklage liegt (Spieler: unter 0) und die
    /// zahlungsfähige Seite keine Station/kein Tor der pleiten Seite belagert. 0 = alte Regel (sofort).
    /// </summary>
    public int WarBrokeTicks;
    /// <summary>Spätestens nach so vielen Ticks anhaltender Pleite ist Frieden, auch während einer Belagerung (0 = keine Obergrenze).</summary>
    public int WarBrokeMaxTicks;
}
