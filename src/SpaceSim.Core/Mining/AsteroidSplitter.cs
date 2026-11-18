using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Mining;

/// <summary>
/// Asteroidenbrecher (Phase 8, SCHIFFE.md Abschnitt 11): Ein Spaltschuss zerlegt einen Erz-Asteroiden in 2 bis 4 Fragmente derselben Klasse.
/// Das Erz wird nach Zufallsgewichten verteilt, ein Anteil geht als Staub verloren (<see cref="EconomyRules.SplitDustLoss"/>: Spalten erzeugt
/// kein Erz); die Fragmente sind volle, kleinere Asteroiden (Radius aus dem Flächenanteil, mindestens <see cref="EconomyRules.FragmentMinRadius"/>)
/// nahe am Ursprung, mit denselben Platzregeln wie der Respawn (<see cref="BeltPlacement.Fits"/>) und im selben Gürtel. Ein Gürtel darf höchstens
/// <see cref="EconomyRules.FragmentLimitFactor"/> × <c>TargetAsteroids</c> Asteroiden haben; der Respawn greift ohnehin erst unter dem Ziel.
/// Fragment-Ids sind fortlaufend (<see cref="Universe.NewAsteroidId"/>, im Spielstand). Alle Würfe über <see cref="Universe.Rng"/>.
/// Spieler und NPC-Brecher nutzen dieselbe Regel.
/// </summary>
public static class AsteroidSplitter
{
    /// <summary>Höchstzahl Asteroiden im Gürtel eines Feldes (Fragmentlimit).</summary>
    public static int BeltLimit(Universe u, ResourceField f) => (int)MathF.Floor(f.TargetAsteroids * u.Rules.FragmentLimitFactor);

    /// <summary>Warum sich dieser Asteroid nicht spalten lässt (null = er lässt sich spalten).</summary>
    public static string? Issue(Universe u, Sector s, Asteroid rock)
    {
        if (!rock.Minable || rock.ClassKey.Length == 0) return "Kein Erz-Asteroid";
        var field = s.FieldFor(rock.Position);
        if (field is null || field.ClassKey != rock.ClassKey) return "Gehört zu keinem Gürtel";
        if (rock.Radius < 2f * u.Rules.FragmentMinRadius) return "Zu klein zum Spalten";
        if (Belt.AsteroidsOf(s, field).Count() + 1 > BeltLimit(u, field)) return "Gürtel ist voll (Fragmentlimit)";
        return null;
    }

    /// <summary>
    /// Spaltet den Asteroiden; gibt die neuen Fragmente zurück (null = nicht möglich, nichts verändert außer dem Zufallsstand).
    /// Veröffentlicht <see cref="AsteroidSplit"/>.
    /// </summary>
    public static IReadOnlyList<Asteroid>? Split(Universe u, Sector s, Asteroid rock, ShipId by)
    {
        if (Issue(u, s, rock) is not null) return null;
        var field = s.FieldFor(rock.Position)!;
        int room = BeltLimit(u, field) - Belt.AsteroidsOf(s, field).Count() + 1;   // der Ursprung verschwindet
        var rng = u.Rng;
        int n = Math.Min(4, 2 + (int)(rng.NextDouble() * 3));
        n = Math.Min(n, room);
        float r0 = rock.Radius, minR = u.Rules.FragmentMinRadius;
        var weights = new List<float>();
        for (int i = 0; i < n; i++) weights.Add(0.5f + (float)rng.NextDouble());
        // Fragmente unter der Mindestgröße fallen weg (ihr Anteil geht an die übrigen)
        while (n >= 2 && r0 * MathF.Sqrt(weights.Min() / weights.Sum()) < minR) { weights.RemoveAt(weights.IndexOf(weights.Min())); n--; }
        if (n < 2) return null;
        float total = weights.Sum();
        float ore = rock.Ore * (1f - u.Rules.SplitDustLoss);
        float dust = rock.Ore - ore;

        int index = s.Asteroids.IndexOf(rock);
        s.Asteroids.Remove(rock);
        var made = new List<Asteroid>(n);
        foreach (var w in weights)
        {
            float r = MathF.Max(minR, r0 * MathF.Sqrt(w / total));
            Asteroid? frag = null;
            for (int attempt = 0; attempt < 24 && frag is null; attempt++)
            {
                var p = rock.Position + Vec2.FromAngle((float)rng.NextDouble() * MathF.Tau) * (r0 * 0.3f + (float)rng.NextDouble() * (r0 + r));
                if (!ReferenceEquals(s.FieldFor(p), field) || !FitsFragment(u, s, p, r)) continue;
                float share = ore * w / total;
                frag = new Asteroid { Id = 0, Position = p, BaseRadius = r, ClassKey = rock.ClassKey, Ore = share, MaxOre = share };
            }
            if (frag is null)
            {
                // kein Platz: Spaltung abbrechen, Ursprung unverändert zurück
                foreach (var m in made) s.Asteroids.Remove(m);
                s.Asteroids.Insert(Math.Min(index, s.Asteroids.Count), rock);
                return null;
            }
            s.Asteroids.Add(frag);
            made.Add(frag);
        }
        foreach (var m in made) m.Id = u.NewAsteroidId();
        MiningAnchor.OnSplit(u, s.Id, rock.Id);   // Phase 32: am gespaltenen Fels reißen die Trossen
        u.Events.Publish(new AsteroidSplit(s.Id, rock.Id, made.Select(m => m.Id).ToList(), dust, by));
        return made;
    }

    /// <summary>
    /// Platzregel für Fragmente: dieselben Abstände zu Gates, Stationen und Feldmitte wie <see cref="BeltPlacement.Fits"/>, zu anderen Asteroiden
    /// aber nur deren Radius plus ein kleiner Spalt (Fragmente liegen dicht am Ursprung; Runde 43: Radien sind konstant).
    /// </summary>
    static bool FitsFragment(Universe u, Sector s, Vec2 p, float r) =>
        s.Gates.All(g => Vec2.Distance(p, u.Gates[g].Position) > BeltPlacement.GateClear + r) &&
        s.Stations.All(st => Vec2.Distance(p, u.Stations[st].Position) > BeltPlacement.StationClear - Station.BodyRadius + u.Stations[st].Radius + r) &&
        s.Fields.All(fl => Vec2.Distance(p, fl.Position) > ResourceField.ClearRadius + r) &&
        s.Asteroids.All(a => Vec2.Distance(p, a.Position) > a.Radius + r + 0.5f);

    /// <summary>
    /// Spaltschuss eines Schiffs (Spieler oder NPC): Schiff braucht einen Spaltlaser (<see cref="ShipStats.SplitPower"/> &gt; 0) und den
    /// Asteroiden in Laserreichweite. Ergebnis: Fragmente oder ein Grund, warum es nicht geht.
    /// </summary>
    public static (IReadOnlyList<Asteroid>? Fragments, string? Issue) Fire(Universe u, Ship ship, int asteroidId)
    {
        if (ship.Stats.SplitPower <= 0f) return (null, "Kein Spaltlaser an Bord");
        var s = u.Sectors[ship.Sector];
        var rock = s.Asteroids.FirstOrDefault(a => a.Id == asteroidId);
        if (rock is null) return (null, "Kein Asteroid gewählt");
        if (Vec2.Distance(ship.Position, rock.Position) - rock.Radius > MiningRules.LaserRange) return (null, "Außer Reichweite");
        if (Issue(u, s, rock) is { } issue) return (null, issue);
        Flight.Cloak.Break(u, ship, "fired");   // Tarnfeld: der Spaltschuss bricht die Tarnung
        var frags = Split(u, s, rock, ship.Id);
        return frags is null ? (null, "Kein Platz für die Fragmente") : (frags, null);
    }
}
