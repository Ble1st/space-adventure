namespace SpaceSim.Core.Flight;

/// <summary>
/// Reine Rechnung Tempo-Slider → Zielfluggeschwindigkeit (engine-unabhängig, ohne Godot).
/// Der Slider steht waagerecht am unteren Bildrand: links von STOPP ein kurzer Rückwärtsbereich,
/// rechts davon STOPP (0) bis MAX (1). Sliderwert <paramref name="slider"/> in [-1, 1].
/// </summary>
public static class Throttle
{
    /// <summary>Anteil der Höchstgeschwindigkeit, der rückwärts maximal gefahren werden darf (entschieden: 20 %).</summary>
    public const float ReverseShare = 0.20f;

    /// <summary>
    /// Slider-Position (-1..1) → Geschwindigkeitsfaktor. Vorwärts 0..1, rückwärts höchstens -<see cref="ReverseShare"/>
    /// (der kurze Bereich links von STOPP wird auf 20 % gedehnt). Werte außerhalb werden gekappt.
    /// </summary>
    public static float SliderToFraction(float slider)
    {
        slider = Math.Clamp(slider, -1f, 1f);
        return slider >= 0f ? slider : slider * ReverseShare;
    }

    /// <summary>Zielfluggeschwindigkeit in Einheiten/s (negativ = rückwärts) für Slider-Position und Höchstgeschwindigkeit.</summary>
    public static float TargetSpeed(float slider, float maxSpeed) => SliderToFraction(slider) * maxSpeed;
}
