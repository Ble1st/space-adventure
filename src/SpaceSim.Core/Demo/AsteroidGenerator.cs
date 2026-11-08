using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Demo;

/// <summary>Deterministische Anfangsverteilung: Gürtel mit Erz um die Rohstofffelder plus erzlose Hintergrundfelsen.</summary>
public static class AsteroidGenerator
{
    public static void Populate(Universe u, Sector sector, int seed, int backgroundCount = 25)
    {
        var rng = new Random(seed);
        float Next(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        foreach (var f in sector.Fields)
            for (int i = 0; i < f.TargetAsteroids; i++)
                if (BeltPlacement.TryCreate(u, sector, f, Next) is { } rock) sector.Asteroids.Add(rock);

        for (int placed = 0, tries = 0; placed < backgroundCount && tries < 600; tries++)
        {
            var p = new Vec2(Next(-160, 160), Next(-160, 160));
            float r = Next(1.5f, 6f);
            if (!BeltPlacement.Fits(u, sector, p, r)) continue;
            sector.Asteroids.Add(new Asteroid { Id = u.NewAsteroidId(), Position = p, BaseRadius = r });   // ohne Erz: nur Hindernis
            placed++;
        }
    }
}
