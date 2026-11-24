namespace SpaceSim.Core.Model;

/// <summary>
/// Raketenlenkung und Gegenmaßnahmen (KAMPF.md „Raketenlenkung und Gegenmaßnahmen“): Stellschrauben als eigener Teil der Regeln (gespeichert wie alle Regeln).
/// Code-Vorgabe AUS = altes Verhalten (Phase 28: jede Rakete gleich, keine Täuschkörper/Störsender); die Standardwelt schaltet es an.
/// Die Lenkart steht am Raketenwerfer (JSON-Modulfeld <c>guidance</c>), die Gegenmaßnahmen sind Module (<c>flareCapacity</c>, <c>jamming</c>/<c>jamDraw</c>).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Hauptschalter Raketenlenkung. Aus: Lenkart, Täuschkörper und Störsender wirken nicht, und die Weltdatei lässt deren Inhalte weg.</summary>
    public bool MissileGuidance;

    /// <summary>Optisch: Ziel muss beim Start in diesem halben Winkel (Grad) vor dem Bug des Schützen liegen (nur räumliches Gefecht).</summary>
    public float OpticalArcDeg = 45f;
    /// <summary>Infrarot: Treffsicherheitsfaktor eines Ziels ohne Schub (kalt); mit voller Geschwindigkeit 1.</summary>
    public float HeatMin = 0.6f;
    /// <summary>Infrarot: Zuschlag auf den Wärmefaktor, solange das Ziel die Leistung auf den Antrieb legt.</summary>
    public float HeatEngineBonus = 0.15f;
    /// <summary>Infrarot: Chance, dass aktive Täuschkörper die Rakete ablenken (ein Zug).</summary>
    public float FlareChance = 0.6f;
    /// <summary>Täuschkörper wirken nach dem Ausstoß so viele Ticks.</summary>
    public int FlareTicks = 12;
    /// <summary>Halbaktives Radar: der Schütze muss das Ziel bis zum Einschlag in Reichweite und in diesem halben Winkel (Grad) vor dem Bug halten.</summary>
    public float SemiActiveArcDeg = 90f;
    /// <summary>Aktives Radar: Chance, dass ein laufender Störsender die Rakete ablenkt (ein Zug).</summary>
    public float JammerChance = 0.5f;
    /// <summary>NPC: stößt Täuschkörper aus, wenn eine Infrarotrakete höchstens so viele Ticks vor dem Einschlag ist.</summary>
    public int NpcFlareLeadTicks = 8;
}
