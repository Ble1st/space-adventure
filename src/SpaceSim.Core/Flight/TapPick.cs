namespace SpaceSim.Core.Flight;

/// <summary>
/// Runde 19 (Handy-Test: Tippen auf ein Tor wählte immer ein Händlerschiff davor/dahinter): Vorrang beim Antippen zwischen einem Körper (Station, Tor) und einem
/// Schiffsmarker in Bildpunkten. Rein, ohne Godot.
/// </summary>
public static class TapPick
{
    /// <summary>Ein Schiff gewinnt gegen ein Tor nur, wenn der Tipp deutlich näher an ihm liegt: Abstand zum Schiff &lt; dieser Anteil × Abstand zur Tormitte.</summary>
    public const float GateClearShare = 0.5f;

    /// <summary>Liegt der Tipp im projizierten Körper (Radius in Bildpunkten, mindestens <paramref name="minPx"/>)?</summary>
    public static bool InBody(float centrePx, float radiusPx, float minPx) => centrePx <= MathF.Max(radiusPx, minPx);

    /// <summary>Station: ein Schiff gewinnt, wenn der Tipp näher an seinem Marker liegt als an der Stationsmitte (Regel seit Runde 4/5).</summary>
    public static bool ShipBeatsStation(float shipPx, float stationCentrePx) => shipPx < stationCentrePx;

    /// <summary>Tor: ein Schiff gewinnt nur, wenn der Tipp deutlich näher an ihm liegt (<see cref="GateClearShare"/>).</summary>
    public static bool ShipBeatsGate(float shipPx, float gateCentrePx) => shipPx < GateClearShare * gateCentrePx;
}
