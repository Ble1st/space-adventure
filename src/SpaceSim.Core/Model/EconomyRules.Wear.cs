namespace SpaceSim.Core.Model;

/// <summary>
/// Ausbau Punkt 8 — Stationsverschleiß (docs/STATIONEN.md „Stationsverschleiß“). Code-Vorgabe = alte Regel (aus: kein Zerfall, <see cref="Station.Integrity"/>
/// bleibt 1); die Standardwelt schaltet ein. Gespeichert wie alle Regeln (additiv, Spielstand bleibt v17).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Schalter: Stationen verlieren Zustand, wenn ihre Instandhaltung lange offen bleibt, und gewinnen ihn mit Material zurück.</summary>
    public bool StationWear;
    /// <summary>Verfall beginnt, wenn die offene Instandhaltung (Basiswert) mehr als dieser Anteil des Tagesbedarfs ist.</summary>
    public float WearBacklogShare = 0.5f;
    /// <summary>Verlust an Zustand je Tag (0..1), solange der Rückstand über <see cref="WearBacklogShare"/> liegt.</summary>
    public float WearLossPerDay = 0.01f;
    /// <summary>Erholung nur, solange der Rückstand unter diesem Anteil des Tagesbedarfs liegt.</summary>
    public float WearRecoverBelowShare = 0.1f;
    /// <summary>Erholung je Tag (0..1), wenn das Reparaturmaterial im Lager ist.</summary>
    public float WearRecoverPerDay = 0.02f;
    /// <summary>Reparaturmaterial je 1 % Zustand als Anteil des Tagesbedarfs der Instandhaltung (0,5 → +2 %/Tag kosten einen zusätzlichen Tagesbedarf).</summary>
    public float WearRepairNeedPerPoint = 0.5f;
    /// <summary>Unter diesem Zustand produziert die Station langsamer: Ausstoß je Takt × Zustand ÷ Schwelle.</summary>
    public float WearSlowBelow = 0.7f;
    /// <summary>Unter diesem Zustand steht die Produktion still (Handel, Andocken und Dienste bleiben).</summary>
    public float WearStopBelow = 0.3f;
    /// <summary>
    /// Höchstgebot (× Basispreis) der Instandhaltungs-Einträge einer Station mit Verschleiß (wirkt nur über <see cref="MaintenanceMaxBidFactor"/>; 0 = aus).
    /// Gemessen und verworfen (docs/STATIONEN.md): 1,6–2,0 zog Stahl aus der Konsumgüterkette.
    /// </summary>
    public float WearMaxBidFactor;
    /// <summary>
    /// Wunschbestand (Tagesbedarfe) der Instandhaltungs-Einträge einer Station mit Verschleiß (wirkt nur über <see cref="MaintenanceStockDays"/>; 0 = aus).
    /// Gemessen und verworfen: 6–15 Tage halbierten im großen Maßstab den Konsumgüter- und Luxusexport.
    /// </summary>
    public float WearStockDays;
    /// <summary>Notinstandhaltung: unter diesem Zustand kauft eine KI-Fraktion den offenen Rückstand zum Aufpreis (0 = aus).</summary>
    public float WearEmergencyBelow;
    /// <summary>Aufpreis der Notinstandhaltung (× Basispreis; Geldsenke).</summary>
    public float WearEmergencyMarkup = 2f;
    /// <summary>Regierungsstationen und geschützte Startstationen zerfallen höchstens bis hier.</summary>
    public float WearProtectedFloor = 0.5f;
}
