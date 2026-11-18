namespace SpaceSim.Core.Model;

/// <summary>
/// Kapitulation und Zerstörung von Stationen (docs/KAMPF.md „Kapitulation“): Stellschrauben als eigener Teil der Regeln (gespeichert wie alle Regeln).
/// Nutzerentscheidung: nicht der Angreifer entert — die <b>Station</b> entscheidet. Fällt ihre Hülle unter eine Schwelle, kapituliert sie mit einer Chance
/// (Besitz geht an den Angreifer: die Spielerperson bzw. die angreifende Fraktion); sonst kämpft sie weiter und wird bei Hülle 0 zerstört — für alle Typen,
/// auch Regierungsstationen und Kleinstationen. Piratenstationen kapitulieren nie (Verstecke werden nur ausgeschaltet, Piratenstellungen zerstört).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Hauptschalter (Code aus = Phase-28-Regel: Regierungsstationen werden nur ausgeschaltet; Standardwelt an).</summary>
    public bool StationCapitulation = false;
    /// <summary>Hüllenschwellen (Anteil der Höchsthülle, absteigend): wird eine überschritten, würfelt die Station genau einmal, ob sie kapituliert.</summary>
    public List<float> CapitulationThresholds = new() { 0.25f, 0.1f };
    /// <summary>Grundchance je Schwelle (gleiche Reihenfolge wie <see cref="CapitulationThresholds"/>; fehlt ein Wert, gilt der letzte).</summary>
    public List<float> CapitulationChances = new() { 0.3f, 0.55f };
    /// <summary>Abzug je aktivem Verteidiger (Polizei/Kriegsschiff im Gefecht auf Seite der Station).</summary>
    public float CapitulationDefenderPenalty = 0.1f;
    /// <summary>Zuschlag je weiterem aktivem Angreifer (über den ersten hinaus) …</summary>
    public float CapitulationAttackerBonus = 0.05f;
    /// <summary>… höchstens so viele zählen.</summary>
    public int CapitulationAttackerMax = 4;
    /// <summary>Gewicht der Aggression des Besitzers: Chance −(Aggression − 0,5) × Gewicht (aggressive Fraktionen geben seltener auf).</summary>
    public float CapitulationAggressionWeight = 0.3f;
    /// <summary>Untere und obere Grenze der Chance.</summary>
    public float CapitulationChanceMin = 0.05f, CapitulationChanceMax = 0.9f;
    /// <summary>Hülle einer kapitulierten Station mindestens dieser Anteil (sie war vielleicht im selben Treffer auf 0).</summary>
    public float CapitulationHullFloor = 0.05f;
    /// <summary>KI-Fraktionen im Krieg übernehmen kapitulierte Stationen ebenfalls (Nutzer: „für alle, nicht nur Spieler“).</summary>
    public bool AiStationCapture = true;
    /// <summary>Verliert eine KI-Fraktion ihren Sitz (zerstört/kapituliert), entsteht sofort ein Not-Sitz in ihrem Gebiet; die Kasse zahlt höchstens diesen Anteil der Baukosten.</summary>
    public float SeatRelocationShare = 0.5f;
}
