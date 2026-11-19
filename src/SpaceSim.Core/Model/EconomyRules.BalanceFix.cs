namespace SpaceSim.Core.Model;

/// <summary>
/// Ausgleichsrunde nach dem Spielerbot-Test (docs/POLITIK.md „Ausgleich nach dem Bot-Test“): neun Stellschrauben, jede mit eigenem Schalter.
/// Code-Vorgabe = alte Regel; die Standardwelt schaltet sie ein. Gespeichert wie alle Regeln (additiv, Spielstand bleibt v17).
/// </summary>
public sealed partial class EconomyRules
{
    // 1 — Obergrenze eigener Stationen: Nutzerentscheidung „Spieler soll kein Baulimit haben“ — PlayerMaxSites/PlayerMaxStations/PlayerMaxStationsPerSector
    // gelten mit 0 als unbegrenzt (EconomyRules.PlayerBuilding.cs); die Standardwelt setzt alle drei auf 0.

    /// <summary>
    /// Nutzerentscheidung „Bau auch möglich in Sektoren anderer Fraktionen“: der Spieler darf auch in Sektoren einer KI-Fraktion bauen, solange er mit ihr
    /// nicht im Krieg ist, ihre Beziehung zu ihm über <see cref="HostileRelation"/> liegt und keine Sperre ihn ausschließt. Der Einfluss zählt dort normal
    /// (Sektorkontrolle); wird der Sektor dadurch zwischen ihm und dem Besitzer umkämpft, sinkt nur diese eine Beziehung (<see cref="ContestOpinionLoss"/>).
    /// </summary>
    public bool PlayerBuildForeignSectors;
    /// <summary>Nutzerentscheidung „auch in Sektoren mit Piratenversteck“: der Spieler darf dort bauen (Überfälle nach den normalen Regeln).</summary>
    public bool PlayerBuildHideoutSectors;

    // 2 — Unterhalt der Spieler-Regierungsstationen
    /// <summary>Anteil des Typ-Unterhalts, den Regierungsstationen des Spielers (Behörden, Hauptquartier, eroberte Behörden) zahlen. 1 = alte Regel.</summary>
    public float PlayerGovernmentUpkeepShare = 1f;
    /// <summary>
    /// Untergrenze der Spielerkasse für Regierungsunterhalt: würde der Unterhalt die Kasse unter <see cref="PlayerUpkeepFloor"/> drücken, wird die Station
    /// „unbezahlt“ (kein Einfluss, kein Grundschutz, keine Behörde, kein Gericht) statt Schulden zu machen. Aus = alte Regel (<see cref="PlayerGovernmentDebt"/>).
    /// </summary>
    public bool PlayerUpkeepFloorActive;
    /// <summary>Untergrenze der Kasse (Cr) für Regierungsunterhalt.</summary>
    public float PlayerUpkeepFloor = 0f;
    /// <summary>Eine unbezahlte Station läuft erst wieder, wenn die Kasse diesen Betrag über der Untergrenze hat (kein Flackern).</summary>
    public float PlayerUpkeepResumeBuffer = 2000f;

    // 3 — Sperren der Spielerfraktion
    /// <summary>Sperren des Spielers verärgern nur die Fraktion, gegen die sie gerichtet sind (<see cref="SectorAccess.Against"/>), nicht alle KI-Fraktionen.</summary>
    public bool PlayerLocksSourOnlyTarget;

    // 4 — Eroberung von Wirtschaftsstationen
    /// <summary>Wirtschaftsstationen der Größe M/L/XL ohne eigene <c>defense</c> bekommen mehr Hülle, einen kleinen Schild und Geschütze (Selbstverteidigung).</summary>
    public bool EconomicStationDefense;
    /// <summary>Hülle = Grundhülle der Größe × Faktor.</summary>
    public float EconomicStationHullFactor = 3f;
    /// <summary>Schild = Grundhülle × Anteil; Schildladung je Sekunde = Schild × <see cref="EconomicStationShieldRegenShare"/>.</summary>
    public float EconomicStationShieldShare = 0.1f;
    public float EconomicStationShieldRegenShare = 0.02f;
    /// <summary>Panzerung der Wirtschaftsstationen.</summary>
    public float EconomicStationArmor = 10f;
    /// <summary>Geschützmodul der Wirtschaftsstationen.</summary>
    public string EconomicStationGun = "weapon_laser_s";
    /// <summary>Geschütze je Größe M, L, XL.</summary>
    public List<int> EconomicStationGuns = new() { 1, 2, 2 };
    /// <summary>Die Geschütze der Wirtschaftsstationen feuern nur auf Angreifer der Station selbst (keine Wirkung auf Überfälle in der Nähe).</summary>
    public bool EconomicGunsSelfDefenseOnly = true;
    /// <summary>Nach einem Besitzerwechsel durch Kapitulation kann die Station so viele Ticks nicht erneut kapitulieren (0 = keine Sperre).</summary>
    public int CapitulationCooldownTicks;

    // 5 — Größenklasse des Spielers
    /// <summary>Die Schiffe des Spielers zählen für die Größenklasse nach ihrer Kampfstärke statt je 1.</summary>
    public bool PlayerSizeByStrength;
    /// <summary>Stärke (Waffenleistung + Schild/20 + Hülle/50 + 0,5) eines Schiffs, das als 1 zählt.</summary>
    public float PlayerSizeStrengthRef = 20f;
    /// <summary>Gewicht je Schiff mindestens/höchstens.</summary>
    public float PlayerSizeShipMin = 0.25f, PlayerSizeShipMax = 4f;

    // 6 — Schiffspreise und Flottenunterhalt des Spielers
    /// <summary>Schiffskauf des Spielers: Materialpreis × (1 + Händleraufschlag ± Ansehen) mit Mindestpreis je Kampfstärke, auf <see cref="PlayerShipPriceRounding"/> gerundet.</summary>
    public bool PlayerShipPricing;
    /// <summary>Händleraufschlag (ersetzt beim Spieler die Montagegebühr).</summary>
    public float PlayerDealerMarkup = 0.2f;
    /// <summary>Ansehen: Aufschlag −sign(rel) × rel² × Gewicht (freundlich billiger, feindlich teurer).</summary>
    public float PlayerDealerOpinionWeight = 0.2f;
    /// <summary>Mindestmaterialpreis je Kampfstärke (zivile Schiffe liegen bei ≈ 190–230; vorher Zerstörer 163, Schlachtkreuzer 180).</summary>
    public float PlayerShipMinPricePerStrength = 210f;
    /// <summary>Kaufpreis aufgerundet auf ein Vielfaches (0 = nicht runden).</summary>
    public float PlayerShipPriceRounding = 250f;
    /// <summary>Faktor des Flottenunterhalts je Klassengröße S, M, L, XL (leer = flach wie bisher).</summary>
    public List<float> PlayerFleetUpkeepBySize = new();

    // 7 — Tributforderung
    /// <summary>Der Spieler kann Tribut erst fordern, wenn der Gegner im Krieg wirklich verloren hat (Schiff, Station oder Tor an die Spielerseite).</summary>
    public bool TributeDemandNeedsLosses;

    // 8 — Gericht am eigenen Sitz
    /// <summary>Das Gericht an Behörde/Hauptquartier des Spielers nimmt Strafen jeder Fraktion an (Geld geht an die Fraktion), unabhängig von der Beziehung.</summary>
    public bool OwnCourtAcceptsAll;
}
