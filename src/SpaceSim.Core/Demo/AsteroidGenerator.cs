using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Demo;

/// <summary>Deterministische Anfangsverteilung: Gürtel mit Erz um die Rohstofffelder plus erzlose Hintergrundfelsen.</summary>
public static class AsteroidGenerator
{
    /// <summary>Runde 44: Obergrenze der Hintergrundfelsen je Sektor (Rechenzeit).</summary>
    public const int MaxBackground = 150;

    /// <summary>Runde 44: Anzahl der Hintergrundfelsen — Anzahl der Datei × Maßstab, höchstens <see cref="MaxBackground"/> (bei Maßstab 1 unverändert).</summary>
    public static int BackgroundCount(int count, float scale) => scale <= 1f ? count : Math.Min(MaxBackground, (int)MathF.Round(count * scale));

    public static void Populate(Universe u, Sector sector, int seed, int backgroundCount = 25)
    {
        var rng = new Random(seed);
        float Next(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        foreach (var f in sector.Fields)
            for (int i = 0; i < f.TargetAsteroids; i++)
                if (BeltPlacement.TryCreate(u, sector, f, Next) is { } rock) sector.Asteroids.Add(rock);

        // Runde 44: Fläche ±160 × Maßstab (Versuche wachsen mit der Anzahl)
        float half = 160f * MathF.Max(1f, sector.Scale);
        int maxTries = sector.Scale <= 1f ? 600 : (int)(600 * sector.Scale);
        for (int placed = 0, tries = 0; placed < backgroundCount && tries < maxTries; tries++)
        {
            var p = new Vec2(Next(-half, half), Next(-half, half));
            float r = Next(1.5f, 6f);
            if (!BeltPlacement.Fits(u, sector, p, r)) continue;
            sector.Asteroids.Add(new Asteroid { Id = u.NewAsteroidId(), Position = p, BaseRadius = r });   // ohne Erz: nur Hindernis
            placed++;
        }
    }
}
