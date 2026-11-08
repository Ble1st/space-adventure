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

    /// <summary>Hebt ältere Spielstände schrittweise auf das aktuelle Format. Bei Formatänderungen hier je Version einen Schritt ergänzen.</summary>
    static SaveGame Migrate(SaveGame s)
    {
        // Version 1 ist die erste; künftig: if (s.Version < 2) { ...; s.Version = 2; }
        return s;
    }
}
