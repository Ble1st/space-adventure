using System.Globalization;

namespace SpaceSim.Core.Flight;

/// <summary>
/// Runde 44: Entfernungen für die Anzeige (Randpfeile, Sektorkarte, Universumkarte). Eine Welteinheit = 1 m; unter 1000 m auf 10 m gerundet,
/// darüber in km mit einer Nachkommastelle (deutsches Komma).
/// </summary>
public static class DistanceText
{
    static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Format(float meters)
    {
        if (!float.IsFinite(meters) || meters < 0f) return "–";
        if (meters < 995f) return $"{MathF.Round(meters / 10f) * 10f:0} m";
        return (meters / 1000f).ToString(meters < 9950f ? "0.0" : "0", De) + " km";
    }

    /// <summary>
    /// Maßstabsleiste: eine „runde“ Länge (1, 2 oder 5 × 10ⁿ m), die bei <paramref name="pixelsPerMeter"/> höchstens <paramref name="maxPixels"/> lang ist.
    /// </summary>
    public static float NiceLength(float pixelsPerMeter, float maxPixels)
    {
        if (!(pixelsPerMeter > 0f) || !(maxPixels > 0f)) return 0f;
        float max = maxPixels / pixelsPerMeter;
        float p = MathF.Pow(10f, MathF.Floor(MathF.Log10(max)));
        foreach (var m in new[] { 5f, 2f, 1f }) if (m * p <= max) return m * p;
        return p;
    }
}
