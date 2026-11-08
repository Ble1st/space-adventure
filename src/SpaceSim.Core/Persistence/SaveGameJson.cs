using System.Text.Json;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

public sealed class SaveFormatException : Exception
{
    public SaveFormatException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>JSON-Ein-/Ausgabe für Spielstände. Dateizugriff liegt beim Aufrufer (Godot: user://), Core bleibt IO-frei.</summary>
public static class SaveGameJson
{
    static readonly JsonSerializerOptions Options = new() { IncludeFields = true, WriteIndented = false };

    public static string Write(Universe u, DateTimeOffset now) =>
        JsonSerializer.Serialize(UniverseSerializer.ToSave(u, now), Options);

    public static (Universe Universe, DateTimeOffset SavedAt) Read(string json)
    {
        SaveGame save;
        try { save = JsonSerializer.Deserialize<SaveGame>(json, Options) ?? throw new SaveFormatException("Leerer Spielstand"); }
        catch (JsonException e) { throw new SaveFormatException("Spielstand ist beschädigt: " + e.Message, e); }

        if (save.Version > SaveGame.CurrentVersion)
            throw new SaveFormatException($"Spielstand stammt aus einer neueren Version ({save.Version} > {SaveGame.CurrentVersion})");
        save = Migrate(save);

        try
        {
            var u = UniverseSerializer.FromSave(save);
            var savedAt = DateTimeOffset.TryParse(save.SavedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : DateTimeOffset.UtcNow;
            return (u, savedAt);
        }
        catch (Exception e) when (e is KeyNotFoundException or ArgumentException or FormatException or InvalidOperationException)
        {
            throw new SaveFormatException("Spielstand ist inkonsistent: " + e.Message, e);
        }
    }

    /// <summary>
    /// v1 → v2: Der Erzvorrat lag im Rohstofffeld, Asteroiden waren reine Hindernisse. Jetzt trägt jeder Gürtel-Asteroid
    /// seinen Anteil des alten Feldvorrats; Asteroiden außerhalb jedes Gürtels bleiben erzlose Hindernisse.
    /// </summary>
    static void MigrateV1ToV2(SaveGame s)
    {
        int nextId = Math.Max(1, s.NextAsteroidId);
        foreach (var sector in s.Sectors)
        {
            var legacy = sector.Asteroids ?? new List<Obstacle>();
            int FieldOf(Obstacle o)
            {
                int best = -1; float bestD = Mining.MiningRules.BeltRadius;
                for (int i = 0; i < sector.Fields.Count; i++)
                {
                    float d = Vec2.Distance(o.Position, sector.Fields[i].Pos);
                    if (d <= bestD) { bestD = d; best = i; }
                }
                return best;
            }
            var belongs = legacy.Select(FieldOf).ToList();
            for (int i = 0; i < legacy.Count; i++)
            {
                var o = legacy[i];
                int f = belongs[i];
                float ore = 0f; int good = 0;
                if (f >= 0)
                {
                    int n = belongs.Count(x => x == f);
                    ore = sector.Fields[f].Amount / Math.Max(1, n);
                    good = sector.Fields[f].Good;
                }
                sector.Rocks.Add(new AsteroidSave
                {
                    Id = nextId++, Pos = o.Position, BaseRadius = o.Radius, Good = good,
                    Ore = ore, MaxOre = Math.Max(ore, f >= 0 ? Asteroid.OrePerArea * o.Radius * o.Radius : 0f),
                });
            }
            sector.Asteroids = null;
            foreach (var field in sector.Fields)
            {
                field.TargetAsteroids = Math.Max(field.TargetAsteroids, 8);
                field.Amount = field.RegenPerTick = field.Capacity = 0f;
            }
        }
        s.NextAsteroidId = nextId;
        s.Version = 2;
    }

    /// <summary>Hebt ältere Spielstände schrittweise auf das aktuelle Format. Bei Formatänderungen hier je Version einen Schritt ergänzen.</summary>
    static SaveGame Migrate(SaveGame s)
    {
        if (s.Version < 2) MigrateV1ToV2(s);
        return s;
    }
}
