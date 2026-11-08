using SpaceSim.Core.Model;

namespace SpaceSim.Core.Demo;

/// <summary>Deterministische Asteroidenverteilung; hält Gates, Stationen und Feldmittelpunkte frei.</summary>
public static class AsteroidGenerator
{
    public const float GateClear = 35f, StationClear = 50f;

    public static void Populate(Universe u, Sector sector, int seed, int backgroundCount = 25, int perField = 14)
    {
        var rng = new Random(seed);
        var keepClear = new List<Obstacle>();
        foreach (var g in sector.Gates) keepClear.Add(new Obstacle(u.Gates[g].Position, GateClear));
        foreach (var s in sector.Stations) keepClear.Add(new Obstacle(u.Stations[s].Position, StationClear));
        foreach (var f in sector.Fields) keepClear.Add(new Obstacle(f.Position, ResourceField.ClearRadius));

        bool Fits(Vec2 p, float r) =>
            keepClear.All(k => Vec2.Distance(p, k.Position) > k.Radius + r) &&
            sector.Asteroids.All(a => Vec2.Distance(p, a.Position) > a.Radius + r + 2f);

        float Next(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        foreach (var f in sector.Fields)
            for (int placed = 0, tries = 0; placed < perField && tries < 400; tries++)
            {
                var p = f.Position + Vec2.FromAngle(Next(0, MathF.Tau)) * Next(22, 45);
                float r = Next(1.5f, 6f);
                if (!Fits(p, r)) continue;
                sector.Asteroids.Add(new Obstacle(p, r));
                placed++;
            }

        for (int placed = 0, tries = 0; placed < backgroundCount && tries < 600; tries++)
        {
            var p = new Vec2(Next(-160, 160), Next(-160, 160));
            float r = Next(1.5f, 6f);
            if (!Fits(p, r)) continue;
            sector.Asteroids.Add(new Obstacle(p, r));
            placed++;
        }
    }
}
