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

    /// <summary>
    /// v2 → v3: Felder und Asteroiden trugen eine einzelne Erzsorte (<c>Good</c>); jetzt gehören sie zu einer Asteroidenklasse mit
    /// Erzmischung. Jede alte Sorte wird zu einer Klasse "legacy_&lt;id&gt;" mit genau dieser Sorte. Bergbauaufträge verweisen statt auf die
    /// Ware auf den Feldindex.
    /// </summary>
    static void MigrateV2ToV3(SaveGame s)
    {
        var classes = new Dictionary<int, string>();
        string ClassFor(int good)
        {
            if (good <= 0) return "";
            if (!classes.TryGetValue(good, out var key))
            {
                key = "legacy_" + good;
                classes[good] = key;
                var name = s.Goods.FirstOrDefault(g => g.Id == good)?.Name ?? ("Ware " + good);
                s.AsteroidClasses.Add(new AsteroidClassSave
                {
                    Key = key, Name = name + "-Asteroid",
                    Yields = new List<YieldSave> { new() { Good = good, Weight = 1f } },
                });
            }
            return key;
        }

        var sectorFields = new Dictionary<int, List<FieldSave>>();
        foreach (var sector in s.Sectors)
        {
            foreach (var f in sector.Fields) { f.Class = ClassFor(f.Good); f.Good = 0; }
            foreach (var r in sector.Rocks) { r.Class = ClassFor(r.Good); r.Good = 0; }
            sectorFields[sector.Id] = sector.Fields;
        }
        foreach (var ship in s.Ships)
        {
            if (ship.Order is not { Kind: "mine" } order) continue;
            // Alt: B = Warenid. Neu: B = Index des Feldes dieser Ware im Sektor
            string key = ClassFor(order.B);
            var fields = sectorFields.GetValueOrDefault(order.A) ?? new List<FieldSave>();
            int idx = fields.FindIndex(f => f.Class == key);
            if (idx < 0) { ship.Order = null; ship.MinePhase = "Idle"; ship.Cargo.Clear(); continue; }
            order.B = idx;
        }
        s.Version = 3;
    }

    /// <summary>Hebt ältere Spielstände schrittweise auf das aktuelle Format. Bei Formatänderungen hier je Version einen Schritt ergänzen.</summary>
    static SaveGame Migrate(SaveGame s)
    {
        if (s.Version < 2) MigrateV1ToV2(s);
        if (s.Version < 3) MigrateV2ToV3(s);
        return s;
    }
}
