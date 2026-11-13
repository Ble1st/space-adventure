using System.Text.Json;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Content;

/// <summary>
/// Phase 29 (Wunsch des Nutzers): große Namensliste für Fraktionen und Akteure, nach Art getrennt (Staat, Gilde/Handelsbund, Firma, Gesetzlose) — feste
/// Namen plus kombinatorisch „Form Ort“ bzw. „Adjektiv Nomen“ (zusammen über 3000). Eingebettete Ressource <c>Content/faction-names.json</c> (der Core liest
/// keine Dateien). Runde 11: keine markennahen Firmennamen, keine Orte der Welt (Korvan, Borea ~ Borealis) im Ortsvorrat; die festen Namen der
/// Startfraktionen (Freie Händler, Montanbund, Rote Korsaren, Schwarze Klingen) bleiben bewusst im Vorrat — doppelt kann ein Name nie vergeben werden
/// (<see cref="Pick"/> prüft gegen die vergebenen). Gewürfelt wird mit einem eigenen, aus dem Seed abgeleiteten Zufall (nie <see cref="Universe.Rng"/>, damit die Wirtschaft unverändert bleibt);
/// ein Name kommt nie doppelt vor.
/// </summary>
public static class FactionNames
{
    sealed class Category { public List<string> Names { get; set; } = new(); public List<string> Forms { get; set; } = new(); public List<string> Adjectives { get; set; } = new(); public List<string> Nouns { get; set; } = new(); }
    sealed class File { public List<string> Places { get; set; } = new(); public Category State { get; set; } = new(); public Category Guild { get; set; } = new(); public Category Corp { get; set; } = new(); public Category Outlaw { get; set; } = new(); }

    static readonly Lazy<File> Data = new(() =>
    {
        var asm = typeof(FactionNames).Assembly;
        using var stream = asm.GetManifestResourceStream("SpaceSim.Core.Content.faction-names.json") ?? throw new WorldFormatException("Eingebettete Namensliste fehlt");
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<File>(reader.ReadToEnd(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new File();
    });

    /// <summary>Alle Namen einer Art (feste zuerst, dann die Kombinationen), ohne Doppelte — feste Reihenfolge.</summary>
    public static IReadOnlyList<string> All(ActorKind kind)
    {
        var cat = kind is ActorKind.State or ActorKind.Guild or ActorKind.Outlaw ? kind : ActorKind.Independent;
        lock (Cache)
        {
            if (Cache.TryGetValue(cat, out var hit)) return hit;
            var built = Build(cat);
            Cache[cat] = built;
            return built;
        }
    }

    /// <summary>Runde 11: die Listen werden je Art einmal gebaut (vorher bei jedem Würfeln).</summary>
    static readonly Dictionary<ActorKind, IReadOnlyList<string>> Cache = new();

    static IReadOnlyList<string> Build(ActorKind kind)
    {
        var d = Data.Value;
        var c = CategoryOf(d, kind);
        var list = new List<string>(c.Names);
        if (c.Adjectives.Count > 0) foreach (var a in c.Adjectives) foreach (var n in c.Nouns) list.Add($"{a} {n}");
        else foreach (var f in c.Forms) foreach (var p in d.Places) list.Add($"{f} {p}");
        return list.Distinct(StringComparer.Ordinal).ToList();
    }

    static Category CategoryOf(File d, ActorKind kind) => kind switch
    {
        ActorKind.State => d.State,
        ActorKind.Guild => d.Guild,
        ActorKind.Outlaw => d.Outlaw,
        _ => d.Corp,
    };

    /// <summary>Ein Name der Art, der nicht in <paramref name="taken"/> steht (gewürfelt mit <paramref name="rng"/>); bei erschöpfter Liste mit Ordnungszahl.</summary>
    public static string Pick(ActorKind kind, DeterministicRng rng, ICollection<string> taken)
    {
        var all = All(kind);
        for (int tries = 0; tries < 64; tries++)
        {
            var n = all[(int)(rng.NextDouble() * all.Count) % all.Count];
            if (!taken.Contains(n)) return n;
        }
        foreach (var n in all) if (!taken.Contains(n)) return n;
        for (int i = 2; ; i++) { var n = $"{all[0]} {i}"; if (!taken.Contains(n)) return n; }
    }

    /// <summary>Eigener Namenszufall der Welt (aus dem Seed; berührt <see cref="Universe.Rng"/> nicht).</summary>
    public static DeterministicRng WorldRng(ulong seed) => new(seed * 0x9E3779B97F4A7C15UL ^ 0x4E414D45UL);

    /// <summary>Namenszufall für einen Akteur, der mitten im Spiel entsteht (aus Seed und Akteur-Id; reproduzierbar, berührt <see cref="Universe.Rng"/> nicht).</summary>
    public static DeterministicRng ActorRng(ulong seed, ActorId id) => new((seed ^ 0xA5A5A5A5UL) * 0x100000001B3UL + (ulong)id.Value * 0x9E3779B97F4A7C15UL);
}
