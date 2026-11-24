using System.Globalization;
using System.Text;
using System.Text.Json;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Persistence;

/// <summary>
/// Mehrere Spielstände: Metadaten eines Speicherplatzes (Slot) — klein, damit die Laden-Liste nicht jeden Spielstand einlesen muss. Die Datei-IO liegt in
/// der Godot-Ansicht (SaveService); hier nur, was sich ohne Dateien prüfen lässt.
/// </summary>
public sealed record SaveSlotMeta
{
    /// <summary>Dateiname ohne Endung (nur a–z, 0–9, '-').</summary>
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public DateTimeOffset SavedAt { get; init; }
    public long Tick { get; init; }
    public string Sector { get; init; } = "";
    public float Credits { get; init; }
    /// <summary>Anzeigename des aktiven Schiffsmodells.</summary>
    public string Ship { get; init; } = "";
    /// <summary>Schlüssel des Startpakets ("" = Vorgabe/unbekannt).</summary>
    public string Package { get; init; } = "";
    public string PackageName { get; init; } = "";
    /// <summary>Kennung des laufenden Spiels — der Autosave-Slot eines Spiels ist <see cref="SaveSlots.AutosaveId"/>(GameId).</summary>
    public string GameId { get; init; } = "";
    public bool Autosave { get; init; }

    /// <summary>Gespielte Zeit (Simulationszeit, ohne Zeitraffer-Unterschied: Ticks ÷ 4).</summary>
    public double PlaySeconds => Tick / (double)SimClock.TicksPerSecond;
}

public static class SaveSlots
{
    public const int MaxNameLength = 40;
    const int MaxIdLength = 32;
    public const string AutosavePrefix = "auto-";
    public const string NamedPrefix = "s-";
    /// <summary>Name des aus der alten Einzeldatei übernommenen Spielstands.</summary>
    public const string MigratedName = "Übernommener Spielstand";

    /// <summary>Autosave-Intervalle der Einstellung (Sekunden; 0 = aus).</summary>
    public static readonly int[] AutosaveOptions = { 0, 30, 60, 300 };
    public const int DefaultAutosaveSeconds = 30;

    public static string AutosaveId(string gameId) => AutosavePrefix + Slug(gameId, "spiel");

    /// <summary>Gültiges Intervall; unbekannte Werte → Vorgabe 30 s.</summary>
    public static int NormalizeAutosave(int seconds) => Array.IndexOf(AutosaveOptions, seconds) >= 0 ? seconds : DefaultAutosaveSeconds;

    public static string AutosaveText(int seconds) => seconds switch
    {
        <= 0 => "aus",
        < 60 => $"{seconds} s",
        _ => $"{seconds / 60} min",
    };

    /// <summary>Name bereinigen: Leerraum am Rand weg, innen zusammengefasst, Steuerzeichen raus.</summary>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new StringBuilder();
        bool space = false;
        foreach (char c in name.Trim())
        {
            if (char.IsControl(c)) continue;
            if (char.IsWhiteSpace(c)) { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Namensregel: nicht leer, höchstens <see cref="MaxNameLength"/> Zeichen, kein „/“ oder „\“, nicht doppelt (Groß-/Kleinschreibung egal) —
    /// außer der vorhandene Stand ist <paramref name="overwriteId"/>. null = in Ordnung, sonst der Grund.
    /// </summary>
    public static string? NameIssue(string? name, IEnumerable<SaveSlotMeta> existing, string? overwriteId = null)
    {
        var n = CleanName(name);
        if (n.Length == 0) return "Name fehlt";
        if (n.Length > MaxNameLength) return $"Name zu lang (höchstens {MaxNameLength} Zeichen)";
        if (n.IndexOfAny(new[] { '/', '\\' }) >= 0) return "„/“ und „\\“ sind nicht erlaubt";
        foreach (var m in existing)
            if (string.Equals(CleanName(m.Name), n, StringComparison.OrdinalIgnoreCase) && m.Id != overwriteId)
                return m.Autosave ? "Name eines Autosaves — anderen Namen wählen" : "Name schon vergeben — „Überschreiben“ am Stand antippen";
        return null;
    }

    /// <summary>Datei-Kennung zu einem Namen: Kleinbuchstaben, Umlaute ausgeschrieben, sonst '-', mit Präfix „s-“, eindeutig (-2, -3 …).</summary>
    public static string IdFor(string name, IEnumerable<string> existingIds)
    {
        var ids = new HashSet<string>(existingIds);
        string baseId = NamedPrefix + Slug(name, "stand");
        string id = baseId;
        for (int i = 2; ids.Contains(id); i++) id = baseId + "-" + i.ToString(CultureInfo.InvariantCulture);
        return id;
    }

    static string Slug(string s, string fallback)
    {
        var sb = new StringBuilder();
        foreach (char raw in s.ToLowerInvariant())
        {
            string part = raw switch
            {
                'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'ß' => "ss",
                >= 'a' and <= 'z' or >= '0' and <= '9' => raw.ToString(),
                _ => "-",
            };
            foreach (char c in part)
            {
                if (c == '-' && (sb.Length == 0 || sb[^1] == '-')) continue;
                sb.Append(c);
            }
        }
        while (sb.Length > 0 && sb[^1] == '-') sb.Length--;
        if (sb.Length > MaxIdLength) sb.Length = MaxIdLength;
        while (sb.Length > 0 && sb[^1] == '-') sb.Length--;
        return sb.Length == 0 ? fallback : sb.ToString();
    }

    /// <summary>Vorschlag für „Speichern unter“: Sektor und Datum.</summary>
    public static string SuggestName(string sector, DateTime local)
    {
        var s = CleanName(sector);
        string n = (s.Length == 0 ? "Spielstand" : s) + " " + local.ToString("dd.MM. HH:mm", CultureInfo.InvariantCulture);
        return n.Length <= MaxNameLength ? n : n[..MaxNameLength].TrimEnd();
    }

    /// <summary>Neueste zuerst; gleicher Zeitpunkt nach Kennung.</summary>
    public static List<SaveSlotMeta> Sorted(IEnumerable<SaveSlotMeta> slots) =>
        slots.OrderByDescending(m => m.SavedAt).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();

    /// <summary>Stand für „Weiter“: der zuletzt gespeicherte (Autosave oder benannt), null ohne Stand.</summary>
    public static SaveSlotMeta? Newest(IEnumerable<SaveSlotMeta> slots) => Sorted(slots).FirstOrDefault();

    /// <summary>Neue Spielkennung aus der Zeit (Millisekunden, Basis 36), eindeutig gegenüber vorhandenen.</summary>
    public static string NewGameId(DateTimeOffset now, IEnumerable<string> existingGameIds)
    {
        var ids = new HashSet<string>(existingGameIds);
        long ms = Math.Max(0, now.ToUnixTimeMilliseconds());
        string id;
        do id = "g" + Base36(ms++); while (ids.Contains(id));
        return id;
    }

    static string Base36(long v)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (v == 0) return "0";
        var sb = new StringBuilder();
        while (v > 0) { sb.Insert(0, digits[(int)(v % 36)]); v /= 36; }
        return sb.ToString();
    }

    /// <summary>Metadaten aus dem laufenden Universum (Sektor, Kasse, aktives Schiff, Tick).</summary>
    public static SaveSlotMeta Describe(Universe u, string id, string name, string gameId, string package, string packageName, bool autosave, DateTimeOffset now)
    {
        string sector = u.Sectors.TryGetValue(u.PlayerSector, out var s) ? s.Name : "";
        string ship = "";
        if (u.Ships.TryGetValue(u.PlayerShip, out var sh))
            ship = sh.ModelKey.Length > 0 && u.ShipModels.TryGetValue(sh.ModelKey, out var m) && m.Name.Length > 0 ? m.Name : sh.ModelKey;
        return new SaveSlotMeta
        {
            Id = id, Name = CleanName(name), SavedAt = now, Tick = u.Tick, Sector = sector, Credits = u.PlayerWallet.Credits,
            Ship = ship, Package = package, PackageName = packageName, GameId = gameId, Autosave = autosave,
        };
    }

    /// <summary>„1 h 05 min“ / „12 min“ / „< 1 min“.</summary>
    public static string PlayTimeText(double seconds)
    {
        long min = (long)(seconds / 60);
        if (min < 1) return "< 1 min";
        return min < 60 ? $"{min} min" : $"{min / 60} h {min % 60:00} min";
    }

    /// <summary>Zeile unter dem Namen: Sektor · Kasse · Schiff · Spielzeit (Zeitpunkt zeigt die Ansicht in Ortszeit).</summary>
    public static string Line(SaveSlotMeta m)
    {
        var parts = new List<string>();
        if (m.Sector.Length > 0) parts.Add(m.Sector);
        parts.Add(m.Credits.ToString("#,0", CultureInfo.GetCultureInfo("de-DE")) + " Cr");
        if (m.Ship.Length > 0) parts.Add(m.Ship);
        parts.Add("Spielzeit " + PlayTimeText(m.PlaySeconds));
        if (m.PackageName.Length > 0) parts.Add(m.PackageName);
        return string.Join(" · ", parts);
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static string WriteMeta(SaveSlotMeta m) => JsonSerializer.Serialize(m, Json);

    /// <summary>Metadaten lesen; null bei kaputter Datei (die Laden-Liste zeigt dann einen Ersatz aus dem Dateinamen).</summary>
    public static SaveSlotMeta? ReadMeta(string json)
    {
        try { return JsonSerializer.Deserialize<SaveSlotMeta>(json, Json); }
        catch (JsonException) { return null; }
    }
}
