namespace SpaceSim.Core.Model;

/// <summary>
/// Ausbau Punkt 3 (docs/POLITIK.md „Schutzvertrag“, „Verfolgung über die Grenze“; docs/KAMPF.md „Konvois“) und Nachtrag 5b (Hehlergeld der Schmuggler).
/// Code-Vorgabe = alte Regel (alles aus); die Standardwelt schaltet ein. Gespeichert wie alle Regeln (additiv, Spielstand bleibt v17).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>
    /// Verfolgung über die Grenze: Polizei verfolgt höchstens <see cref="PursuitForeignJumps"/> Sprung weit in Sektoren befreundeter (Beziehung ≥
    /// <see cref="AllyRelation"/>) oder verbündeter Fraktionen — nie in Feindesgebiet (Krieg, Fehde, feindliche Beziehung), nie in Sektoren von Gesetzlosen
    /// oder etablierten Banditen. Aus = alte Zuständigkeit (ganzes befreundetes Gebiet).
    /// </summary>
    public bool PursuitCrossBorder;
    /// <summary>Höchstzahl der Sprünge vom eigenen Gebiet in fremdes befreundetes Gebiet.</summary>
    public int PursuitForeignJumps = 1;

    /// <summary>Schutzverträge des Spielers mit KI-Fraktionen (Comms, Büros der Fraktion bieten ihn ebenfalls an).</summary>
    public bool ProtectionContracts;
    /// <summary>Gebühr je Tag (<see cref="DayTicks"/>), im Voraus aus der Spielerkasse an die Kasse der Fraktion.</summary>
    public float ProtectionFeePerDay = 200f;

    /// <summary>
    /// KI-Konvois: ein beladener Händler (frei oder Mitglied der Fraktion) wartet an der Station, wenn das Routenrisiko seiner Verkaufsfahrt mindestens
    /// <see cref="ConvoyRouteRisk"/> ist und eine Streife der Sektorfraktion da ist; ab <see cref="ConvoyMinTraders"/> Händlern fahren sie zusammen mit
    /// 1–<see cref="ConvoyMaxEscorts"/> Polizeischiffen als Geleit los (spätestens nach <see cref="ConvoyWaitTicks"/>).
    /// </summary>
    public bool Convoys;
    /// <summary>Routenrisiko (<see cref="Simulation.ThreatMap.RouteRisk"/>) ab dem gebündelt wird.</summary>
    public float ConvoyRouteRisk = 0.02f;
    public int ConvoyMinTraders = 3;
    public int ConvoyMaxTraders = 5;
    /// <summary>Höchste Wartezeit des ersten Händlers; dann fährt der Konvoi los (mit Geleit nur ab <see cref="ConvoyMinTraders"/>).</summary>
    public int ConvoyWaitTicks = 120;
    public int ConvoyMaxEscorts = 2;
    /// <summary>Spätestens nach so vielen Ticks ab Abfahrt löst sich ein Konvoi auf.</summary>
    public int ConvoyTicks = 2400;

    /// <summary>
    /// Nachtrag 5b: Anteil des Kaufpreises, den Schmuggler beim Hehler an die Piratenkasse zahlen (der Rest versickert als Netzrabatt/Bestechung —
    /// Geldsenke). 1 = alte Regel.
    /// </summary>
    public float SmugglerFencePirateShare = 1f;

    /// <summary>
    /// Nachtrag 5b: Aufschlag auf den Bau eines Piratenschiffs je Modell (Schlüssel → Faktor; fehlt = 1). Der Bomber (zwei Werfer) wird für Piratengruppen
    /// teuer, damit Hehlergeld nicht in Bomberflotten fließt, die die Polizei aufreiben.
    /// </summary>
    public Dictionary<string, float> PirateModelCostFactors = new();
}
