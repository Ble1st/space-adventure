namespace SpaceSim.Core.Model;

/// <summary>
/// Runde 44i (Handytest: „Andocken: Hauptquartier – Födera…“, Zielkarte „[FNY] Hauptquartier – Föderatio…“): kürzere Anzeigeformen eines
/// Stationsnamens für enge Felder (Kontexttaste, Zielkarte, Hinweise). Reine Anzeige — der gespeicherte Name bleibt unverändert.
/// Reihenfolge: voll mit Kürzel → ohne den Besitzernamen (das Kürzel nennt ihn) → zusätzlich ohne den Sektornamen (die Zielkarte/der
/// Sektorchip nennt ihn; eine römische Zahl bleibt) → dasselbe ohne Kürzel. Doppelte Formen fallen weg.
/// </summary>
public static class StationNames
{
    /// <summary>Name mit Kürzel, ohne den Besitzernamen im Namen („Hauptquartier – Föderation Nyxar“ → „[FNY] Hauptquartier“).</summary>
    public static string Short(Universe u, Station st) => ActorNames.Tagged(u, st.Owner, WithoutOwner(u, st));

    /// <summary>Anzeigeformen vom längsten zum kürzesten; das Feld nimmt die erste, die passt.</summary>
    public static IReadOnlyList<string> Candidates(Universe u, Station st)
    {
        string noOwner = WithoutOwner(u, st);
        string noSector = WithoutSector(u, st, noOwner);
        var list = new List<string>(5);
        void Add(string s) { if (s.Length > 0 && !list.Contains(s)) list.Add(s); }
        Add(ActorNames.Tagged(u, st.Owner, st.Name));
        Add(ActorNames.Tagged(u, st.Owner, noOwner));
        Add(ActorNames.Tagged(u, st.Owner, noSector));
        Add(noOwner);
        Add(noSector);
        return list;
    }

    public static string WithoutOwner(Universe u, Station st)
    {
        string owner = st.Owner.Value != 0 ? u.ActorName(st.Owner) : "";
        return Strip(st.Name, owner);
    }

    static string WithoutSector(Universe u, Station st, string name)
    {
        if (!u.Sectors.TryGetValue(st.Sector, out var sec) || sec.Name.Length == 0) return name;
        string s = Strip(name, sec.Name);
        if (s == name && sec.Name.StartsWith("Sektor ")) s = Strip(name, sec.Name.Substring(7));
        return s;
    }

    /// <summary>Entfernt <paramref name="part"/> aus dem Namen samt Trennzeichen; bliebe nichts Sinnvolles, bleibt der Name.</summary>
    public static string Strip(string name, string part)
    {
        if (part.Length < 3) return name;
        int i = name.IndexOf(part, StringComparison.Ordinal);
        if (i < 0) return name;
        string s = (name.Substring(0, i) + " " + name.Substring(i + part.Length)).Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        s = s.Trim(' ', '–', '—', '-', '·', ',', ':');
        s = s.Replace(" – –", " –").Replace("– –", "–");
        if (s.Length < 3 || IsRoman(s)) return name;
        return s;
    }

    static bool IsRoman(string s) => s.All(c => c is 'I' or 'V' or 'X');
}
