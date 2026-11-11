using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Mining;

/// <summary>Abfragen und Abbau im Asteroidengürtel eines Rohstofffelds. Der Erzvorrat eines Feldes ist die Summe seiner Asteroiden.</summary>
public static class Belt
{
    public static IEnumerable<Asteroid> AsteroidsOf(Sector s, ResourceField f) =>
        s.Asteroids.Where(a => a.Minable && a.ClassKey == f.ClassKey && ReferenceEquals(s.FieldFor(a.Position), f));

    public static float OreIn(Sector s, ResourceField f) => AsteroidsOf(s, f).Sum(a => a.Ore);

    /// <summary>
    /// Phase 17 (Leistung): genau <c>OreIn(s, f) &gt;= threshold</c>, bricht aber ab, sobald die Teilsumme die Schwelle erreicht (alle Summanden sind
    /// positiv — abbaubare Asteroiden haben Erz &gt; 0,001 —, die Summe wächst also nur; summiert wie <see cref="OreIn"/> in double, Vergleich als float).
    /// </summary>
    public static bool HasOre(Sector s, ResourceField f, float threshold)
    {
        double sum = 0;
        foreach (var a in s.Asteroids)
        {
            if (!a.Minable || a.ClassKey != f.ClassKey || !ReferenceEquals(s.FieldFor(a.Position), f)) continue;
            sum += a.Ore;
            if ((float)sum >= threshold) return true;
        }
        return (float)sum >= threshold;
    }

    /// <summary>Abstrakter Abbau (NPC-Miner): entnimmt <paramref name="amount"/> aus den Asteroiden, nahe am Feldmittelpunkt zuerst. Leere Asteroiden verschwinden.</summary>
    public static float Take(Universe u, Sector s, ResourceField f, float amount)
    {
        float taken = 0f;
        foreach (var rock in AsteroidsOf(s, f).OrderBy(a => Vec2.Distance(a.Position, f.Position)).ThenBy(a => a.Id).ToList())
        {
            if (taken >= amount) break;
            float t = MathF.Min(rock.Ore, amount - taken);
            Remove(u, s, rock, t);
            taken += t;
        }
        return taken;
    }

    /// <summary>Verringert den Vorrat eines Asteroiden; bei 0 wird er entfernt (und kann später nachwachsen).</summary>
    public static void Remove(Universe u, Sector s, Asteroid rock, float units)
    {
        rock.Ore = MathF.Max(0f, rock.Ore - units);
        if (rock.Minable) return;
        rock.Ore = 0f;
        if (s.Asteroids.Remove(rock)) u.Events.Publish(new AsteroidDepleted(s.Id, rock.Id));
    }
}

/// <summary>Platzfindung für Gürtel-Asteroiden (Weltaufbau und Respawn): hält Gates, Stationen, Feldmitten und andere Asteroiden frei.</summary>
public static class BeltPlacement
{
    public const float GateClear = 35f, StationClear = 50f;

    /// <param name="range">Liefert eine Zufallszahl in [lo, hi) – deterministisch vom Aufrufer.</param>
    /// <param name="avoid">Optionaler Bereich, in dem nichts entstehen darf (z.B. um das Spielerschiff).</param>
    public static Asteroid? TryCreate(Universe u, Sector s, ResourceField f, Func<float, float, float> range, (Vec2 Pos, float Radius)? avoid = null)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            var p = f.Position + Vec2.FromAngle(range(0f, MathF.Tau)) * range(22f, 45f);
            float r = range(1.5f, 6f);
            if (avoid is { } av && Vec2.Distance(p, av.Pos) < av.Radius + r) continue;
            if (!Fits(u, s, p, r)) continue;
            float factor = u.AsteroidClasses.TryGetValue(f.ClassKey, out var cls) ? cls.OreFactor : 1f;
            float ore = Asteroid.OrePerArea * r * r * range(0.6f, 1.4f) * factor;
            return new Asteroid { Id = u.NewAsteroidId(), Position = p, BaseRadius = r, ClassKey = f.ClassKey, Ore = ore, MaxOre = ore };
        }
        return null;
    }

    public static bool Fits(Universe u, Sector s, Vec2 p, float r) =>
        s.Gates.All(g => Vec2.Distance(p, u.Gates[g].Position) > GateClear + r) &&
        s.Stations.All(st => Vec2.Distance(p, u.Stations[st].Position) > StationClear + r) &&
        s.Fields.All(fl => Vec2.Distance(p, fl.Position) > ResourceField.ClearRadius + r) &&
        s.Asteroids.All(a => Vec2.Distance(p, a.Position) > a.BaseRadius + r + 2f);
}
