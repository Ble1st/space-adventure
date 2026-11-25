namespace SpaceSim.Core.Model;

/// <summary>
/// Hitze (KAMPF.md „Hitze und Wärmeabfuhr“): Stellschrauben als eigener Teil der Regeln (gespeichert wie alle Regeln). Code-Vorgabe AUS = altes Verhalten
/// (keine Hitze, Welt bitgleich); die Standardwelt schaltet es an. Wärmekapazität und Grundabfuhr kommen aus der Klassengröße (S, M, L, XL), zusätzliche
/// Abfuhr aus Kühlermodulen (Modulwert <c>heatDissipation</c>).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Hauptschalter Hitze. Aus: keine Hitze, die Weltdatei lässt die Kühlermodule weg (<see cref="Content.HeatContent"/>).</summary>
    public bool ShipHeat;

    /// <summary>Wärmekapazität je Klassengröße S, M, L, XL (Hitzeeinheiten bis 100 %).</summary>
    public List<float> HeatCapacityBySize = new() { 100f, 160f, 300f, 500f };
    /// <summary>Grundabfuhr je Klassengröße S, M, L, XL (Hitzeeinheiten je Sekunde).</summary>
    public List<float> HeatDissipationBySize = new() { 3f, 6f, 10f, 16f };

    /// <summary>Hitze je Schuss = Schaden × Faktor nach Schadensart (Energie heiß, kinetisch mäßig, Raketen kühl am Werfer); Modulwert <c>heat</c> überschreibt.</summary>
    public float HeatPerDamageEnergy = 0.7f;
    public float HeatPerDamageKinetic = 0.3f;
    public float HeatPerDamageExplosive = 0.15f;

    /// <summary>Dauerquellen je Sekunde: Leistungsstellung „Antrieb“, Schub bei voller Fahrt (Anteil), Sprungantrieb beim Laden.</summary>
    public float HeatEngineMode = 0.8f;
    public float HeatThrustFull = 0.5f;
    public float HeatJumpCharge = 3f;
    /// <summary>Tarnfeld und Störsender: Hitze je Sekunde je Leistungseinheit ihrer Last (<c>cloakDraw</c>, <c>jamDraw</c>).</summary>
    public float HeatPerDraw = 0.5f;

    /// <summary>Ab diesem Anteil laden Waffen langsamer nach (Nachladezeit × (1 + <see cref="HeatReloadPenalty"/>)).</summary>
    public float HeatSlowShare = 0.7f;
    public float HeatReloadPenalty = 0.5f;
    /// <summary>Ab diesem Anteil sind die Waffen gesperrt und der Schild lädt nicht nach — bis die Hitze unter <see cref="HeatResumeShare"/> fällt.</summary>
    public float HeatLockShare = 0.9f;
    public float HeatResumeShare = 0.75f;
    /// <summary>Bei 100 %: Hülle verliert diesen Anteil der Höchsthülle je Sekunde (nie unter 1), und je Hitzerunde (<see cref="HeatRoundTicks"/>) ein Zug: mit dieser Chance wird ein Modul beschädigt.</summary>
    public float HeatHullPerSecond = 0.01f;
    public float HeatModuleChance = 0.1f;
    public float HeatModuleLoss = 0.1f;
    public int HeatRoundTicks = 4;

    /// <summary>NPC-Taktik: ein NPC feuert nicht, wenn der Schuss ihn über diesen Anteil brächte (feste Regel, kein Zufall).</summary>
    public float HeatNpcHoldShare = 0.7f;
    /// <summary>Infrarotraketen: Zuschlag auf den Wärmefaktor je Hitzeanteil des Ziels (heißes Schiff = besser zu treffen).</summary>
    public float HeatIrBonus = 0.4f;
}
