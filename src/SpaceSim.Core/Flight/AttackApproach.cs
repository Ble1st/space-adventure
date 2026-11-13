namespace SpaceSim.Core.Flight;

/// <summary>
/// Runde 15 (Handy-Test Kriegstest: mit 50 % Schub war man in 5 s an der Außenhaut vorbei): Annäherungsautomatik beim Stations-/Torangriff — „auf
/// Geschützreichweite heran und halten“. Reine Regel wie <c>CombatAutopilot</c>: aus dem Abstand zur Außenhaut und der Reichweite der eigenen kürzesten
/// Waffe (ohne Raketen) ein Schubanteil (−<see cref="Throttle.ReverseShare"/> … 1; die Nase zeigt über die Zielhilfe auf das Ziel). Manuelle Eingabe
/// übersteuert (die Ansicht ruft die Regel nur ohne Regler-/Wendeeingabe).
/// </summary>
public static class AttackApproach
{
    /// <summary>Halteband: Wunschabstand = Anteil der kürzesten Reichweite (mindestens 12 m Außenhaut), ± Toleranz.</summary>
    public const float HoldShare = 0.7f, MinHull = 12f, Band = 5f;

    /// <summary>Wunschabstand zur Außenhaut.</summary>
    public static float Desired(float shortestRange) => MathF.Max(MinHull, shortestRange * HoldShare);

    /// <summary>
    /// Schubanteil: weiter als Wunsch + Band → vorwärts (sanft auslaufend, höchstens <paramref name="maxForward"/>), näher als Wunsch − Band → rückwärts,
    /// dazwischen 0 (halten). <paramref name="centreDistance"/> = Abstand zur Mitte, <paramref name="radius"/> = Körperradius des Ziels.
    /// </summary>
    public static float Throttle(float centreDistance, float radius, float shortestRange, float maxForward = 0.6f)
    {
        if (shortestRange <= 0f) return 0f;
        float hull = MathF.Max(0f, centreDistance - radius);
        float want = Desired(shortestRange);
        if (hull > want + Band) return Math.Clamp((hull - want) / 40f, 0.15f, maxForward);
        if (hull < want - Band) return -Flight.Throttle.ReverseShare;
        return 0f;
    }
}
