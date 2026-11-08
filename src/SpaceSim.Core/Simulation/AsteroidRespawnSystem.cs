using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Respawn: Fehlt einem Rohstoffgürtel ein Asteroid (leer abgebaut), wächst nach durchschnittlich
/// <see cref="ResourceField.RespawnTicks"/> Ticks ein neuer mit zufälligem Erzvorrat nach.
/// Nutzt ausschließlich <see cref="Universe.Rng"/> (deterministisch) und entsteht nie nahe beim Spielerschiff.
/// </summary>
public sealed class AsteroidRespawnSystem : ISimSystem
{
    public const float PlayerSafeRadius = 45f;

    public void Step(Universe u, int dt)
    {
        foreach (var sector in u.Sectors.Values.OrderBy(s => s.Id.Value))
            foreach (var field in sector.Fields)
            {
                if (Belt.AsteroidsOf(sector, field).Count() >= field.TargetAsteroids) continue;
                if (u.Rng.NextDouble() >= dt / (double)Math.Max(1, field.RespawnTicks)) continue;

                (Vec2, float)? avoid = null;
                if (sector.Id == u.PlayerSector && u.Ships.TryGetValue(u.PlayerShip, out var player))
                    avoid = (player.Position, PlayerSafeRadius);

                var rock = BeltPlacement.TryCreate(u, sector, field, u.Rng.Range, avoid);
                if (rock is null) continue;
                sector.Asteroids.Add(rock);
                u.Events.Publish(new AsteroidRespawned(sector.Id, rock.Id));
            }
    }
}
