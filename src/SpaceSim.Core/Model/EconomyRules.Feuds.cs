namespace SpaceSim.Core.Model;

/// <summary>
/// Ausbau Punkt 6 (docs/POLITIK.md „Fehden, Vermittlung, Regierungskrise“): Code-Vorgabe = alte Regel (alles aus); die Standardwelt schaltet ein. Ohne feindliche
/// Beziehung mit umkämpftem Sektor, ohne Krieg und ohne Verlust eines Hauptquartiers wirkt keine dieser Regeln (die Standardwelt bleibt bitgleich).
/// Gespeichert wie alle Regeln (additiv, Spielstand bleibt v17).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>KI-Fehden: zwei KI-Fraktionen mit Beziehung unter <see cref="FeudRelation"/> und einem umkämpften Sektor beginnen eine Fehde.</summary>
    public bool FeudsActive;
    /// <summary>Fehden mit dem Spieler: der Spieler erklärt/beendet per Comms (braucht einen Firmensitz), eine KI kann ihm nach derselben Regel eine Fehde erklären.</summary>
    public bool PlayerFeuds;
    /// <summary>Beziehung (schlechtere Richtung), unter der eine Fehde beginnt.</summary>
    public float FeudRelation = -0.4f;
    /// <summary>Dauer einer Fehde (Ticks), danach endet sie von selbst.</summary>
    public int FeudTicks = 6000;
    /// <summary>Erholt sich die Beziehung (beide Richtungen) über diesen Wert, endet eine KI-Fehde vorzeitig.</summary>
    public float FeudEndRelation = -0.2f;
    /// <summary>Nach dem Ende einer Fehde keine neue zwischen demselben Paar für diese Zeit (Ticks).</summary>
    public int FeudCooldownTicks = 12000;
    /// <summary>Mindest-Sperrstufe der Fehdeparteien gegeneinander in ihren Sektoren (gedeckelt durch die Kontrollstufe wie jede Sperre).</summary>
    public int FeudLockLevel = 2;
    /// <summary>Abstand (Ticks) zwischen zwei Fehdeüberfällen einer Fehde.</summary>
    public int FeudRaidTicks = 600;
    /// <summary>Jeder Fehdeüberfall senkt die Beziehung beider Seiten um diesen Wert (so kann eine Fehde nach den Kriegsregeln zum Krieg werden).</summary>
    public float FeudRaidRelationLoss = 0.02f;
    /// <summary>Anteil des Werts der abgeworfenen Fracht, den die überfallende Fraktion in ihre Kasse nimmt (Beschlagnahme; der Rest ist verloren).</summary>
    public float FeudSeizeShare = 0.5f;
    /// <summary>Nachtrag 6b: die Beschlagnahme ist Ware (Anteil der abgeworfenen Fracht ins Lager der Angreifer) statt Geld ohne Gegenbuchung. Aus = alte Regel.</summary>
    public bool FeudSeizeAsGoods;
    /// <summary>Erklärt der Spieler eine Fehde, sinkt sein Ansehen bei allen anderen KI-Fraktionen um diesen Wert.</summary>
    public float FeudThirdPartyLoss = 0.02f;

    /// <summary>Vermittlung durch eine dritte KI-Fraktion in Kriegen und Fehden zwischen KI-Fraktionen.</summary>
    public bool Mediation;
    /// <summary>Vermittlung durch den Spieler per Comms (gegen Gebühr).</summary>
    public bool PlayerMediation;
    /// <summary>Mindestbeziehung (beide Richtungen) einer KI-Vermittlerin zu beiden Konfliktparteien.</summary>
    public float MediatorRelation = 0.25f;
    /// <summary>Mindestbeziehung beider Konfliktparteien zum Spieler, damit er vermitteln kann.</summary>
    public float PlayerMediatorRelation = 0.1f;
    /// <summary>Beide Konfliktparteien heben ihre Beziehung zur Vermittlerin (bzw. zum Spieler) um diesen Wert.</summary>
    public float MediationRelationGain = 0.1f;
    /// <summary>Zuschlag auf die Kriegsmüdigkeit je Vermittlung (eine Fehde endet bei Vermittlung sofort).</summary>
    public float MediationWearinessBonus = 0.25f;
    /// <summary>Abstand zwischen zwei Vermittlungen im selben Krieg (Ticks).</summary>
    public int MediationCooldownTicks = 10000;
    /// <summary>Ein Konflikt muss so lange laufen (Ticks), bevor jemand vermittelt.</summary>
    public int MediationMinTicks = 2000;
    /// <summary>Gebühr an den Spieler je Konfliktpartei (höchstens die Hälfte ihres verfügbaren Geldes).</summary>
    public float MediationFee = 1500f;

    /// <summary>Regierungskrise: verliert eine KI-Fraktion ihr Hauptquartier (zerstört oder kapituliert), ist sie für <see cref="CrisisTicks"/> gelähmt.</summary>
    public bool GovernmentCrisis;
    /// <summary>Dauer der Regierungskrise (Ticks).</summary>
    public int CrisisTicks = 12000;
    /// <summary>Anteil der Steuern, der in der Krise noch in die Kasse fließt (der Rest verlässt die Welt).</summary>
    public float CrisisTaxFactor = 0.5f;
    /// <summary>Höchste Sperrstufe in den Sektoren einer Fraktion in der Krise.</summary>
    public int CrisisMaxLockLevel = 1;
    /// <summary>Vorsprung, mit dem ein Sektor einer Fraktion in der Krise den Besitzer wechselt (statt <see cref="OwnerSwitchMargin"/>).</summary>
    public float CrisisOwnerSwitchMargin = 0.02f;
}
