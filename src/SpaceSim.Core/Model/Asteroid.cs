namespace SpaceSim.Core.Model;

/// <summary>
/// Asteroid mit eigenem, endlichem Erzvorrat. Beim Abbau schrumpft er <b>nicht mehr</b> (Runde 43: der Radius bleibt der
/// Basisradius; die Ansicht zeigt abfallende Brocken und ein Zerbrechen bei 0) und verschwindet bei 0
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

    /// <summary>Aktueller Radius: konstant der Basisradius (Runde 43: Asteroiden schrumpfen beim Abbau nicht mehr).</summary>
    public float Radius => BaseRadius;

    public Obstacle ToObstacle() => new(Position, Radius);
}
