using System.Text;

namespace SpaceSim.Core.Help;

/// <summary>
/// Kodex (Hilfe): Suche über die Themen. Rein und deterministisch — jedes Suchwort muss in Titel, Suchwörtern oder Text vorkommen (ohne Groß-/Kleinschreibung,
/// Umlaute wie „ae“/„oe“/„ue“/„ss“ gleichwertig); Treffer im Titel zuerst, sonst in der Reihenfolge der Daten. Leere Suche = alle Themen.
/// </summary>
public static class HelpBook
{
    /// <summary>Kleinbuchstaben, Umlaute ausgeschrieben, Satzzeichen als Leerzeichen.</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c0 in s)
        {
            char c = char.ToLowerInvariant(c0);
            switch (c)
            {
                case 'ä': sb.Append("ae"); break;
                case 'ö': sb.Append("oe"); break;
                case 'ü': sb.Append("ue"); break;
                case 'ß': sb.Append("ss"); break;
                default: sb.Append(char.IsLetterOrDigit(c) ? c : ' '); break;
            }
        }
        return sb.ToString();
    }

    public static IReadOnlyList<HelpTopic> Search(IReadOnlyList<HelpTopic> topics, string query)
    {
        var words = Normalize(query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return topics;
        var hits = new List<(HelpTopic T, int Rank, int Index)>();
        for (int i = 0; i < topics.Count; i++)
        {
            var t = topics[i];
            string title = Normalize(t.Title), all = title + " " + Normalize(t.Keywords) + " " + Normalize(t.Text);
            if (!words.All(w => all.Contains(w, StringComparison.Ordinal))) continue;
            int rank = words.All(w => title.Contains(w, StringComparison.Ordinal)) ? 0 : words.Any(w => title.Contains(w, StringComparison.Ordinal)) ? 1 : 2;
            hits.Add((t, rank, i));
        }
        return hits.OrderBy(h => h.Rank).ThenBy(h => h.Index).Select(h => h.T).ToList();
    }

    public static IReadOnlyList<HelpTopic> Search(string query) => Search(HelpData.Default.Topics, query);

    public static HelpTopic? Find(string key) => HelpData.Default.Topics.FirstOrDefault(t => t.Key == key);
}
