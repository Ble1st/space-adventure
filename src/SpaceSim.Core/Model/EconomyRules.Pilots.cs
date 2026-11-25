namespace SpaceSim.Core.Model;

/// <summary>
/// Piloten, Ränge und Rangliste (docs/FLOTTEN.md „Piloten und Rangliste“): Kampfwertung des Spielers aus gewichteten Abschüssen, Aufträgen,
/// Kopfgeldern, Verbrechen und Verlusten; benannte NPC-Kapitäne auf Polizei-, Kriegs-, Kaper- und Söldnerschiffen. Reine Anzeige/Statistik — keine
/// Wirkung auf die Simulation. Code-Vorgabe aus; die Standardwelt schaltet ein.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Schalter: Piloten, Ränge, Rangliste und NPC-Kapitäne zählen.</summary>
    public bool PilotRanks;
    /// <summary>Punkte je Stärkeeinheit eines zerstörten Gegners (Stärke wie <c>CombatRules.Strength</c>, aber bei voller Hülle und vollem Schild).</summary>
    public float PilotKillPointsPerStrength = 2f;
    /// <summary>Faktor für Gesetzlose und gesuchte Ziele (Kopfgeldziele).</summary>
    public float PilotOutlawFactor = 1.5f;
    /// <summary>Punkte je erfülltem Auftrag und je Kopfgeld; Abzug je Verbrechen und je verlorenem eigenen Schiff.</summary>
    public float PilotMissionPoints = 5f;
    public float PilotBountyPoints = 10f;
    public float PilotCrimePenalty = 15f;
    public float PilotDeathPenalty = 20f;
}
