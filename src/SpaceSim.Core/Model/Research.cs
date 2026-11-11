namespace SpaceSim.Core.Model;

/// <summary>Was ein Forschungsprojekt freischaltet (Phase 13, FORSCHUNG.md „Was freigeschaltet werden kann“): ein Modul (samt Modul-Ware und Fabrikrezept), ein Schiffsmodell oder einen Stationstyp.</summary>
public enum UnlockKind { Module, Model, StationType }

/// <summary>Eine Freischaltung: Art und Content-Schlüssel. Textform „module:&lt;key&gt;“, „model:&lt;key&gt;“, „stationType:&lt;key&gt;“ (Weltdatei, Spielstand).</summary>
public readonly record struct ResearchUnlock(UnlockKind Kind, string Key)
{
    /// <summary>Textform für Weltdatei und Spielstand.</summary>
    public override string ToString() => $"{KindKey(Kind)}:{Key}";

    public static string KindKey(UnlockKind k) => k switch { UnlockKind.Module => "module", UnlockKind.Model => "model", _ => "stationType" };

    /// <summary>Liest „art:schlüssel“ (Art ohne Groß-/Kleinschreibung); false bei unbekannter Art oder leerem Schlüssel.</summary>
    public static bool TryParse(string text, out ResearchUnlock unlock)
    {
        unlock = default;
        int i = text.IndexOf(':');
        if (i <= 0 || i == text.Length - 1) return false;
        UnlockKind? kind = text[..i].ToLowerInvariant() switch
        {
            "module" => UnlockKind.Module, "model" => UnlockKind.Model, "stationtype" => UnlockKind.StationType, _ => null,
        };
        if (kind is null) return false;
        unlock = new ResearchUnlock(kind.Value, text[(i + 1)..]);
        return true;
    }
}

/// <summary>
/// Forschungsprojekt (Content, Phase 13, Weltdatei-Abschnitt <c>research</c>): Schlüssel, Name, Beschreibung, nötige Forschungspunkte (FP),
/// Voraussetzungen (andere Projekte), Freischaltungen und zusätzlicher Güterverbrauch je Forschungstakt (Spezialzweige, z. B. Platin für
/// Reaktortechnik) über den Grundverbrauch der Station hinaus.
/// </summary>
public sealed class ResearchProject
{
    public string Key = "";
    public string Name = "";
    public string Description = "";
    public float Points = 1000f;
    public List<string> Requires = new();
    public List<ResearchUnlock> Unlocks = new();
    /// <summary>Zusätzlicher Verbrauch je Takt (Ware → Einheiten), solange dieses Projekt läuft.</summary>
    public Dictionary<GoodId, float> Inputs = new();
}

/// <summary>
/// Forschungs-Content eines Universums: Projekte in Content-Reihenfolge und der daraus abgeleitete Sperr-Index (was welches Projekt freischaltet).
/// Alles, was kein Projekt nennt, ist immer frei — die Startwirtschaft braucht nichts davon (FORSCHUNG.md „Totlauf“).
/// </summary>
public sealed class ResearchCatalog
{
    readonly List<ResearchProject> _projects = new();
    readonly Dictionary<string, ResearchProject> _byKey = new(StringComparer.Ordinal);
    readonly Dictionary<ResearchUnlock, ResearchProject> _locks = new();

    /// <summary>Projekte in Content-Reihenfolge.</summary>
    public IReadOnlyList<ResearchProject> Projects => _projects;

    public int Count => _projects.Count;

    /// <summary>Fügt ein Projekt an (Schlüssel eindeutig; eine Freischaltung gehört genau einem Projekt).</summary>
    public void Add(ResearchProject p)
    {
        if (!_byKey.TryAdd(p.Key, p)) throw new ArgumentException($"Forschungsprojekt '{p.Key}' doppelt");
        foreach (var unlock in p.Unlocks)
            if (!_locks.TryAdd(unlock, p)) throw new ArgumentException($"Freischaltung '{unlock}' gehört schon zu Projekt '{_locks[unlock].Key}'");
        _projects.Add(p);
    }

    public void Clear() { _projects.Clear(); _byKey.Clear(); _locks.Clear(); }

    public ResearchProject? Get(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>Das Projekt, das diesen Content freischaltet; null = immer frei.</summary>
    public ResearchProject? LockOf(UnlockKind kind, string key) => _locks.Count == 0 ? null : _locks.GetValueOrDefault(new ResearchUnlock(kind, key));

    /// <summary>Gibt es überhaupt gesperrte Module (schneller Ausstieg für die Produktion)?</summary>
    public bool HasModuleLocks => _locks.Keys.Any(k => k.Kind == UnlockKind.Module);
}

/// <summary>
/// Forschungsbetrieb eines Stationstyps (Phase 13, JSON <c>research</c>): alle <see cref="Ticks"/> Ticks ein Takt, der den Grundverbrauch
/// <see cref="Inputs"/> plus den Zusatzverbrauch des laufenden Projekts aus dem Lager nimmt, <see cref="Credits"/> (Personal, Geldsenke) aus der
/// Stationskasse zahlt und <see cref="Points"/> FP auf das Projekt der Besitzerfraktion bucht. Fehlen Güter oder Geld, stockt die Forschung.
/// Die Kasse der Besitzerfraktion füllt die Stationskasse bis <see cref="FundTarget"/> auf, höchstens <see cref="FundPerTick"/> je Tick
/// (Budget vom HQ: eine Senke für die Fraktionskassen).
/// </summary>
public sealed class ResearchConfig
{
    public int Ticks = 10;
    public float Points = 5f;
    public float Credits;
    public Dictionary<GoodId, float> Inputs = new();
    public float FundTarget;
    public float FundPerTick;
}

/// <summary>
/// Forschungsstand einer Fraktion (Phase 13, FORSCHUNG.md „Besitz: fraktionsweit“): laufendes Projekt, FP je begonnenem Projekt, abgeschlossene
/// Projekte mit Tick, Summen für Anzeige und Balance. Gespeichert und gehasht. Wird eine Forschungsstation zerstört, bleibt der Stand (kein Rückschritt).
/// </summary>
public sealed class ResearchState
{
    /// <summary>Laufendes Projekt (null = noch keins gewählt; die Forschung wählt beim nächsten Takt).</summary>
    public string? Current;
    /// <summary>Vom Spieler beauftragt: die Heuristik ersetzt das laufende Projekt nicht.</summary>
    public bool Commissioned;
    /// <summary>FP je begonnenem, nicht abgeschlossenem Projekt.</summary>
    public Dictionary<string, float> Progress = new(StringComparer.Ordinal);
    /// <summary>Abgeschlossene Projekte → Tick des Abschlusses.</summary>
    public Dictionary<string, long> Completed = new(StringComparer.Ordinal);
    /// <summary>Summe aller erforschten FP (Stationen und Spieler).</summary>
    public float PointsTotal;
    /// <summary>Summe der Personalkosten aller Takte (Geldsenke).</summary>
    public float Spent;
    /// <summary>Vom Spieler mitfinanziert (Credits).</summary>
    public float PlayerFunded;

    public bool IsCompleted(string key) => Completed.ContainsKey(key);
}
