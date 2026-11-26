namespace SpaceSim.Core.Model;

/// <summary>
/// Welle C (fünf Spieltests mit dem Ziel „Supermacht“, docs/POLITIK.md „Welle C“): ein Reich des Spielers soll sich tragen. Code-Vorgabe AUS = altes
/// Verhalten, die Standardwelt schaltet alles an. Alle vier Punkte wirken nur, wenn der Spieler Sektoren/Behörden besitzt oder Ansehen verliert —
/// die NPC-Welt ohne Spielerreich bleibt bitgleich.
/// </summary>
public sealed partial class EconomyRules
{
    // ───────── 1: Steuern aus eigenem Gebiet ─────────

    /// <summary>
    /// Die Spielerfraktion ist Steuerbehörde ihrer Sektoren wie eine KI-Fraktion: Vermögensabgaben freier Schiffe und fraktionsloser Stationen in
    /// Spielersektoren (und von Mitgliedern der Spielerfraktion) gehen in die Spielerkasse (<see cref="Simulation.UpkeepSystem.TaxAuthority"/>).
    /// Die Steuerbremse zählt den Spieler weiterhin nicht. Aus = solche Abgaben verschwinden (alte Regel).
    /// </summary>
    public bool PlayerSectorTaxes;
    /// <summary>
    /// Anteil dieser fremden Abgaben, den die Spielerkasse behält (der Rest versinkt wie vorher) — an Stelle der Steuerbremse der KI-Fraktionen, die den
    /// Spieler nicht zählt. Gemessen (Spielerbot, 4–5 Sektoren): voller Anteil ≈ 1,8 × Regierungsunterhalt, 0,6 ≈ 1,1 ×.
    /// </summary>
    public float PlayerSectorTaxShare = 1f;

    // ───────── 2: gestaffelter Ausfall statt Kaskade ─────────

    /// <summary>
    /// Regierungsunterhalt des Spielers in fester Rangfolge (Hauptquartier, dann Behörden, deren Ausfall den Sektor kosten würde, dann übrige eigene
    /// Sektoren, dann der Rest): eine Station wird nur bezahlt, wenn danach der Vorrat <see cref="PlayerUpkeepReserveTicks"/> für alle wichtigeren Stationen
    /// über der Untergrenze bleibt — bei Geldmangel fallen zuerst die unwichtigsten, nicht alle zugleich. Zusätzlich kann der Spieler eine Behörde ruhen
    /// lassen (kein Unterhalt, kein Einfluss). Braucht <see cref="PlayerUpkeepFloorActive"/>. Aus = alte Regel (Reihenfolge der Stationsliste).
    /// </summary>
    public bool PlayerUpkeepPriority;
    /// <summary>Vorrat je wichtigerer Station (Ticks Unterhalt), der vor einer weniger wichtigen in der Kasse bleiben muss.</summary>
    public int PlayerUpkeepReserveTicks = 2400;
    /// <summary>Vorwarnung, wenn die Kasse weniger als so viele Spieltage Regierungsunterhalt deckt (Anzeige „Meine Fraktion“, Alarm im Flug). 0 = aus.</summary>
    public float PlayerUpkeepWarnDays;

    // ───────── 3: Ansehen erholt sich ─────────

    /// <summary>
    /// Boden des Ansehens bei KI-Fraktionen für „weiche“ Verluste (<see cref="PlayerStandingSoftReasons"/>: Streitsektor, Fehde): sie senken die Beziehung
    /// nicht unter diesen Wert. Krieg und Verbrechen gehen weiter tiefer. 0 = aus.
    /// </summary>
    public float PlayerStandingFloor;
    /// <summary>Erholung je Spieltag Richtung Ausgangswert, solange kein Krieg, keine Fehde und kein Streitsektor mit der Fraktion besteht.</summary>
    public float PlayerStandingRecoveryPerDay;
    /// <summary>Gründe von <see cref="Simulation.PlayerStanding.Change"/>, die als weicher Verlust zählen (Boden und Erholung).</summary>
    public List<string> PlayerStandingSoftReasons = new();

    // ───────── 4: Behörde im umkämpften Fremdsektor ─────────

    /// <summary>
    /// Sektorbehörde und Firmensitz auch in einem Sektor einer KI-Fraktion, wenn der Spieler dort umkämpft ist (Einfluss &gt; <see cref="ContestedShare"/>)
    /// und nicht im Krieg mit dem Besitzer. Kosten wie sonst; die Reaktion des Besitzers (Welle B, Gegenbau) bleibt. Aus = nur herrenlose/eigene Sektoren.
    /// </summary>
    public bool PlayerSeatContested;
}
