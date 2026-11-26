namespace SpaceSim.Core.Model;

/// <summary>
/// Welle A (Spieltest-Korrekturen nach drei Spielerbot-Durchläufen): Stellschrauben als eigener Teil der Regeln (gespeichert wie alle Regeln). Code-Vorgabe
/// AUS = altes Verhalten; die Standardwelt schaltet alles an. Alle Regeln betreffen nur Spielerhandlungen — ohne Spieler bleibt die NPC-Welt bitgleich.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>
    /// Angriffsbefehl der Flotte: ein Schiff tritt einer Belagerung nur bei, wenn es nicht schon aktiver Teilnehmer ist; Verbrechen und
    /// <c>FleetAttackStarted</c> je Befehl einmal (nicht bei jedem Wiedereintritt); ein im Krieg erteilter Angriffsbefehl endet mit dem Krieg
    /// (Frieden oder Waffenstillstand). Aus: alte Regel (jeder Tick Beitritt, Meldung und Verbrechen).
    /// </summary>
    public bool FleetAttackOnce;

    /// <summary>
    /// Tribut im Spielerkrieg: die KI verlangt für Waffenstillstand/Frieden nur Tribut, wenn sie vorne liegt (Siegerregel <c>Wars.TributeWinner</c> und
    /// höchstens so viele eigene Verluste wie der Spieler). Liegt der Spieler vorne oder gleichauf, sind Waffenstillstand und Frieden kostenlos.
    /// </summary>
    public bool PlayerTributeOnlyIfAiAhead;

    /// <summary>
    /// Kriegsstärke der Spielerfraktion: von seiner Kasse zählt nur dieser Anteil (0 = ganze Kasse, alte Regel) …
    /// </summary>
    public float PlayerWarMoneyShare;
    /// <summary>… und höchstens so viele Credits (0 = ohne Deckel). Die KI zählt nur ihr verfügbares Geld (über der Rücklage).</summary>
    public float PlayerWarMoneyCap;

    /// <summary>
    /// Tor/Sprungantrieb: hat der Spieler beim Zoll keine Credits (Kasse ≤ 0), verweigert das Tor den Durchflug mit Grund statt Zollflucht zu melden
    /// (kein Verbrechen). Mit etwas Geld, aber zu wenig, bleibt die Zollflucht (bewusst durchgeflogen, Rest als Bußgeld).
    /// </summary>
    public bool GateBrokeNoEvasion;

    /// <summary>
    /// Kopfgeldaufträge: kein Angebot auf ein Kaperschiff, das in seinem Versteck liegt oder in Geschützreichweite (+ <c>HideoutGunMargin</c>) davon
    /// steht; scheitert ein angenommener Auftrag, ohne dass das Ziel diesen Bereich je verlassen hat, gibt es nur die Rückgabe — kein Ansehensverlust,
    /// keine Sperre, das Pfand kommt zurück.
    /// </summary>
    public bool BountyReachableTargets;
}
