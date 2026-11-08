namespace SpaceSim.Core.Model;

/// <summary>Kreisförmiges, statisches Hindernis in der Spielebene (Asteroid, Station).</summary>
public readonly record struct Obstacle(Vec2 Position, float Radius);
