namespace SpaceSim.Core.Flight;

/// <summary>
/// Reine Rechnung Tempo-Regler → Zielfluggeschwindigkeit (engine-unabhängig, ohne Godot).
/// Der Slider am unteren Bildrand läuft nur vorwärts (STOPP 0 bis MAX 1); Rückwärts ist eine eigene Haltetaste
/// und fährt höchstens <see cref="ReverseShare"/> der Höchstgeschwindigkeit.
/// </summary>
public static class Throttle
{
    /// <summary>Anteil der Höchstgeschwindigkeit, der rückwärts maximal gefahren werden darf (entschieden: 20 %).</summary>
    public const float ReverseShare = 0.20f;

    /// <summary>Slider-Position (0..1) → Geschwindigkeitsfaktor vorwärts; Werte außerhalb werden gekappt.</summary>
    public static float SliderToFraction(float slider) => Math.Clamp(slider, 0f, 1f);

    /// <summary>
    /// Geschwindigkeitsfaktor aus Slider und Rückwärtstaste: Ist die Rückwärtstaste gehalten, gilt -<see cref="ReverseShare"/>
    /// (sie hat Vorrang vor dem Slider, der unverändert stehen bleibt); sonst der Slider.
    /// </summary>
    public static float Fraction(float slider, bool reverseHeld) => reverseHeld ? -ReverseShare : SliderToFraction(slider);

    /// <summary>Zielfluggeschwindigkeit in Einheiten/s (negativ = rückwärts) für Slider, Rückwärtstaste und Höchstgeschwindigkeit.</summary>
    public static float TargetSpeed(float slider, bool reverseHeld, float maxSpeed) => Fraction(slider, reverseHeld) * maxSpeed;
}
