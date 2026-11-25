namespace SpaceSim.Core.Model;

/// <summary>
/// Richtungsschilde (KAMPF.md „Richtungsschilde“): Stellschrauben als eigener Teil der Regeln (gespeichert wie alle Regeln). Code-Vorgabe AUS = altes
/// Verhalten (ein Schildbetrag, Welt bitgleich); die Standardwelt schaltet es an. Der Schild eines Schiffs zerfällt in vier Sektoren (vorne, rechts, hinten,
/// links); ein Treffer belastet den Sektor, aus dessen Richtung er kommt. Stationsschilde bleiben ohne Sektoren.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Hauptschalter Richtungsschilde.</summary>
    public bool ShieldSectors;
    /// <summary>Anteil der Schildkapazität im ausgerichteten Sektor (ausgeglichen: je ein Viertel).</summary>
    public float ShieldFocusShare = 0.4f;
    /// <summary>Anteil im gegenüberliegenden Sektor; die beiden Seiten teilen den Rest.</summary>
    public float ShieldFocusOppositeShare = 0.1f;
}
