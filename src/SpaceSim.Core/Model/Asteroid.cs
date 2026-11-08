namespace SpaceSim.Core.Model;

/// <summary>
/// Asteroid mit eigenem, endlichem Erzvorrat. Beim Abbau schrumpft er und verschwindet bei 0
/// (<see cref="Simulation.AsteroidRespawnSystem"/> lässt im Gürtel eines Feldes neue nachwachsen).
/// Ohne Vorrat (<see cref="MaxOre"/> = 0) ist er ein reines Hindernis.
/// </summary>
public sealed class Asteroid
{
    public const float OrePerArea = 8f;   // Erz je Flächeneinheit (Radius²) eines vollen Asteroiden

    public int Id;
    public Vec2 Position;
    public float BaseRadius;
    /// <summary>Schlüssel der <see cref="AsteroidClass"/>; leer = erzloser Felsen (nur Hindernis).</summary>
    public string ClassKey = "";
    public float Ore;
    public float MaxOre;

    public bool Minable => Ore > 0.001f;

    /// <summary>Aktueller Radius: schrumpft mit dem Restvorrat (auf bis zu 45 % des Ausgangswerts).</summary>
    public float Radius => MaxOre <= 0f ? BaseRadius : BaseRadius * (0.45f + 0.55f * MathF.Sqrt(Math.Clamp(Ore / MaxOre, 0f, 1f)));

    public Obstacle ToObstacle() => new(Position, Radius);
}
