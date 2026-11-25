using System.Text.Json;
using SpaceSim.Core.Content;

namespace SpaceSim.Core.Help;

/// <summary>Kontexthinweis: Schlüssel (für „schon gesehen“), Auslöser (<see cref="HintTriggers"/>) und Text.</summary>
public sealed class HintDef
{
    public string Key { get; set; } = "";
    public string Trigger { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>Kodex-Thema der Hilfe: Schlüssel, Titel, Suchwörter und Artikeltext (Absätze durch Leerzeilen getrennt).</summary>
public sealed class HelpTopic
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>Absätze des Artikels (an Leerzeilen getrennt, ohne leere).</summary>
    public IReadOnlyList<string> Paragraphs => Text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Schritt des Tutorials: Schlüssel, Prüfart (<see cref="TutorialChecks"/>), Ziel im HUD (<c>{station}</c> = Name der Tutorialstation), Menge.</summary>
public sealed class TutorialStepDef
{
    public string Key { get; set; } = "";
    public string Check { get; set; } = "";
    public string Goal { get; set; } = "";
    public float Amount { get; set; }
}

/// <summary>
/// Hilfe, Hinweise und Tutorial (UI.md „Hilfe und Tutorial“): alle Texte als Daten — eingebettete Ressource <c>Content/help.json</c> (der Core liest keine
/// Dateien). Reine Anzeige: nichts davon wirkt auf die Simulation.
/// </summary>
public sealed class HelpData
{
    public List<HintDef> Hints { get; set; } = new();
    public List<HelpTopic> Topics { get; set; } = new();
    public List<TutorialStepDef> Tutorial { get; set; } = new();

    static readonly Lazy<HelpData> DefaultData = new(() => Parse(DefaultJson()));

    /// <summary>Die eingebetteten Hilfetexte (einmal gelesen und geprüft).</summary>
    public static HelpData Default => DefaultData.Value;

    public static string DefaultJson()
    {
        var asm = typeof(HelpData).Assembly;
        using var stream = asm.GetManifestResourceStream("SpaceSim.Core.Content.help.json") ?? throw new WorldFormatException("Eingebettete Hilfetexte fehlen");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Liest und prüft Hilfetexte: Schlüssel eindeutig und nicht leer, bekannte Auslöser und Prüfarten, Texte nicht leer.</summary>
    public static HelpData Parse(string json)
    {
        var d = JsonSerializer.Deserialize<HelpData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip })
                ?? throw new WorldFormatException("Hilfetexte leer");
        void Unique(IEnumerable<string> keys, string what)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                if (string.IsNullOrWhiteSpace(k)) throw new WorldFormatException($"Hilfetexte: {what} ohne Schlüssel");
                if (!seen.Add(k)) throw new WorldFormatException($"Hilfetexte: {what} '{k}' doppelt");
            }
        }
        Unique(d.Hints.Select(h => h.Key), "Hinweis");
        Unique(d.Topics.Select(t => t.Key), "Thema");
        Unique(d.Tutorial.Select(t => t.Key), "Tutorialschritt");
        foreach (var h in d.Hints)
        {
            if (!HintTriggers.All.Contains(h.Trigger)) throw new WorldFormatException($"Hilfetexte: Hinweis '{h.Key}' mit unbekanntem Auslöser '{h.Trigger}'");
            if (string.IsNullOrWhiteSpace(h.Text)) throw new WorldFormatException($"Hilfetexte: Hinweis '{h.Key}' ohne Text");
        }
        foreach (var t in d.Topics)
            if (string.IsNullOrWhiteSpace(t.Title) || string.IsNullOrWhiteSpace(t.Text)) throw new WorldFormatException($"Hilfetexte: Thema '{t.Key}' ohne Titel oder Text");
        foreach (var s in d.Tutorial)
        {
            if (!TutorialChecks.All.Contains(s.Check)) throw new WorldFormatException($"Hilfetexte: Tutorialschritt '{s.Key}' mit unbekannter Prüfung '{s.Check}'");
            if (string.IsNullOrWhiteSpace(s.Goal)) throw new WorldFormatException($"Hilfetexte: Tutorialschritt '{s.Key}' ohne Ziel");
        }
        return d;
    }
}
