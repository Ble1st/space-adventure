namespace SpaceSim.Core.Model;

/// <summary>
/// Art eines Akteurs (POLITIK.md Abschnitt 6): nur ein Etikett für Anzeige und Vorgabe-Beziehungen; was ein Akteur darf, hängt von seiner
/// Größe ab (<see cref="ActorSizeClass"/>). <see cref="Free"/> ist die Sammelgruppe aller freien Einzelschiffe (ihre Beziehungen stehen einmal dort).
/// </summary>
public enum ActorKind { State, Guild, Outlaw, Independent, Player, Free }

/// <summary>Größenklassen nach der Größenzahl (POLITIK.md Abschnitt 6, Richtwerte); Schwellen in <see cref="EconomyRules.ActorSizeThresholds"/>.</summary>
public enum ActorSizeClass { Single, SmallFirm, Firm, Corporation, State }

/// <summary>Deutsche Namen der Größenklassen und Arten (Anzeige).</summary>
public static class ActorNames
{
    /// <summary>Phase 29: Kürzel eines Akteurs in eckigen Klammern („[UNI]“); leer ohne Kürzel.</summary>
    public static string Tag(Universe u, ActorId id) => u.Actors.TryGetValue(id, out var a) && a.Tag.Length > 0 ? $"[{a.Tag}]" : "";

    /// <summary>Phase 29: Text mit dem Kürzel des Besitzers davor („[UNI] Werft Vesta“); ohne Kürzel der Text allein.</summary>
    public static string Tagged(Universe u, ActorId owner, string text) => Tag(u, owner) is { Length: > 0 } t ? $"{t} {text}" : text;

    /// <summary>Phase 29: Name eines Akteurs mit Kürzel („[UNI] Terranische Union“).</summary>
    public static string Tagged(Universe u, ActorId id) => Tagged(u, id, u.ActorName(id));

    public static string Size(ActorSizeClass c) => c switch
    {
        ActorSizeClass.Single => "Einzelakteur",
        ActorSizeClass.SmallFirm => "Kleinfirma",
        ActorSizeClass.Firm => "Firma",
        ActorSizeClass.Corporation => "Konzern",
        _ => "Staat",
    };

    public static string Kind(ActorKind k) => k switch
    {
        ActorKind.State => "Staat",
        ActorKind.Guild => "Gilde",
        ActorKind.Outlaw => "Gesetzlose",
        ActorKind.Independent => "Unabhängig",
        ActorKind.Player => "Spieler",
        _ => "Freie (Sammelgruppe)",
    };
}

/// <summary>
/// Persönlichkeitswerte eines Akteurs (POLITIK.md „Persönlichkeitswerte“), je 0..1. Phase 9 nutzt nur <see cref="Greed"/> (Appetit beim Zukauf);
/// Aggression, Kooperation und Gesetzestreue sind Daten für Politik, Krieg und Polizei (Phase 10/11).
/// </summary>
public readonly record struct Personality(float Aggression, float Greed, float Cooperation, float Lawfulness)
{
    public static readonly Personality Neutral = new(0.5f, 0.5f, 0.5f, 0.5f);

    /// <summary>Alle Werte auf 0..1 begrenzt.</summary>
    public Personality Clamped() => new(C(Aggression), C(Greed), C(Cooperation), C(Lawfulness));
    static float C(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 0.5f;
}

/// <summary>
/// Akteur (Phase 9, POLITIK.md Abschnitt 6 „Akteur und Fraktion“): ein Wirtschaftssubjekt mit Besitz (Schiffe über <see cref="Ship.Owner"/>,
/// Stationen über <see cref="Station.Owner"/>), Kasse und Persönlichkeit. Eine Fraktion ist ein Akteur mit Politik: Ihr Akteur trägt dieselbe
/// Id-Zahl, die politischen Daten (Territorium, Hauptbeziehungen, Notbau-Bremse) stehen in <see cref="Faction"/>.
/// <see cref="Membership"/> ist die Mitgliedschaft (Bürger, Gildenmitglied); Fraktionsakteure sind Mitglied ihrer selbst.
/// </summary>
public sealed class Actor
{
    public ActorId Id;
    public string Name = "";
    /// <summary>Phase 29: eindeutiges Kürzel aus drei Buchstaben („UNI“), aus dem Namen abgeleitet (<see cref="ActorTags.Derive"/>); gespeichert und gehasht.</summary>
    public string Tag = "";
    public ActorKind Kind;
    /// <summary>Mitgliedschaft in einer Fraktion; null = unabhängig.</summary>
    public FactionId? Membership;
    /// <summary>Kasse des Akteurs (<see cref="Treasury"/>).</summary>
    public Treasury Treasury = new();
    /// <summary>
    /// Buchen die Schiffe dieses Akteurs auf seine Kasse (Spieler: ja)? Sonst wirtschaftet jedes Schiff mit seiner eigenen Betriebskasse
    /// (<see cref="Ship.Credits"/>) wie vor Phase 9 — Fraktionen und aufgestiegene Einzelakteure betreiben ihre Schiffe als Eigenbetriebe.
    /// </summary>
    public bool ShipsUseTreasury;
    public Personality Personality = Personality.Neutral;
    /// <summary>Persönliche Abweichungen der Beziehung zu anderen Akteuren (−1 … +1); nur Abweichungen werden gespeichert (<see cref="Simulation.Relations"/>).</summary>
    public Dictionary<ActorId, float> Relations = new();
    /// <summary>Tick der Anlage (Aufstieg eines Einzelschiffs).</summary>
    public long CreatedTick;
    /// <summary>
    /// Phase 11: Fahndung je Hoheitsgebiet (FLOTTEN.md „Fahndungsniveau“, POLITIK.md: Akteure haben Fahndung): Fraktion → Eintrag; nur Fraktionen,
    /// mit denen der Akteur Ärger hat (sparsam). Gespeichert.
    /// </summary>
    public Dictionary<FactionId, WantedRecord> Wanted = new();
    /// <summary>Phase 11: noch nicht abgeholtes Kopfgeld je Fraktion (Kopfgeldbüro an deren Regierungsstationen). Gespeichert.</summary>
    public Dictionary<FactionId, float> BountyClaims = new();
    /// <summary>Phase 11: Tick der letzten Änderung der Streitkräfte (Polizei gekauft/abgebaut, Kaperschiff neu); Abkühlzeit. Gespeichert.</summary>
    public long LastForceTick = -1_000_000;
    /// <summary>
    /// Phase 20: geglätteter Polizeibedarf der Fraktion (gleitendes Mittel über <see cref="EconomyRules.PoliceNeedWindowTicks"/>); &lt; 0 = noch nicht gemessen.
    /// Kauf und Abbau folgen diesem Wert mit Hysterese statt dem Augenblickswert der Lagekarte. Gespeichert.
    /// </summary>
    public float PoliceNeed = -1f;
    /// <summary>Phase 20: seit diesem Tick ununterbrochen Polizei-Überzahl (Abbau erst nach <see cref="EconomyRules.PoliceDecommissionTicks"/>); −1 = keine. Gespeichert.</summary>
    public long PoliceSurplusSince = -1;
    /// <summary>
    /// Phase 22: Tick des letzten Notkaufs zur Wiederherstellung des Mindestbestands (<see cref="Simulation.FactionViability"/>; Bremse
    /// <see cref="EconomyRules.ViabilityRestoreCooldown"/>). Gespeichert.
    /// </summary>
    public long LastRestoreTick = -1_000_000;
    /// <summary>Phase 30: Tick der letzten aufgestellten Kleinstation (0 = nie; Abklingzeit). Gespeichert, gehasht wenn gesetzt.</summary>
    public long LastOutpostTick;
    /// <summary>Ausbau Punkt 9: Tick der letzten Gründung eines Firmensitzes/Büros (0 = nie; Abklingzeit). Gespeichert, gehasht wenn gesetzt.</summary>
    public long LastFoundTick;
    /// <summary>Ausbau Punkt 5: Tick des letzten Schmugglerkaufs einer gesetzlosen Firma (0 = nie; Abklingzeit). Gespeichert, gehasht wenn gesetzt.</summary>
    public long LastSmugglerTick;
    /// <summary>Ausbau Punkt 5: Tick des letzten Schmugglerverkaufs der Firma (0 = nie). Gespeichert, gehasht wenn gesetzt.</summary>
    public long LastSmugglerRetireTick;
    /// <summary>Ausbau Punkt 17: Tick des letzten Banditenbaus (Wirtschaftsstation oder Vorposten), 0 = nie.
    /// Abklingzeit <see cref="EconomyRules.BanditRebuildCooldownTicks"/>. Gespeichert, gehasht nur wenn ≠ 0.</summary>
    public long LastBanditTick;
    /// <summary>Ausbau Punkt 9: seit diesem Tick trägt die Firmenkasse den Unterhalt ihres Sitzes nicht (−1 = bezahlt). Gespeichert, gehasht wenn gesetzt.</summary>
    public long SeatArrearsSince = -1;
    /// <summary>Phase 28 (Prüfung Phase 27): bis zu diesem Tick kündigt die Polizei keine neue Festnahme an (nach Flucht, Ergeben oder Ende eines Polizeigefechts); 0 = keine Sperre.</summary>
    public long ArrestCooldownUntil;
    /// <summary>Spielersperre: durch Verbrechen verlorenes Ansehen je KI-Fraktion (nur beim Spielerakteur). Gespeichert, gehasht wenn vorhanden.</summary>
    public Dictionary<FactionId, CrimeStandingRecord> CrimeStanding = new();
    /// <summary>Ausbau Punkt 16: Wissen des Spielerakteurs (null = alles bekannt). Gespeichert additiv, gehasht nur mit Spielerpolitik.</summary>
    public Intel.PlayerIntel? Intel;
    /// <summary>Lücken gegenüber IP2: Spielerstatistik (null = nichts gezählt). Gespeichert additiv, gehasht nur mit Spielerpolitik.</summary>
    public PlayerStats? Stats;
    /// <summary>Mehrpunkt-Route des Spielerautopiloten (nur beim Spielerakteur; null = keine). Gespeichert additiv, gehasht nur mit Spielerpolitik.</summary>
    public Flight.PlayerRoute? Route;
    /// <summary>Eigene Schiffsvarianten (Baupläne, nur beim Spielerakteur). Gespeichert additiv, gehasht nur mit Spielerpolitik und nur wenn vorhanden.</summary>
    public List<ShipBlueprint> Blueprints = new();
    /// <summary>Ist der Akteur eine Fraktion (hat Politik)?</summary>
    public bool IsFaction(Universe u) => u.Factions.ContainsKey(new FactionId(Id.Value));
    /// <summary>Runde 38: ist der Akteur eine KI-Fraktion (Fraktion, aber nicht der Spieler)? Die KI-Fraktionsregeln (Rücklage, Mindestbestand, Kleinstationen) fragen dies.</summary>
    public bool IsNpcFaction(Universe u) => IsFaction(u) && !u.IsPlayerFaction(new FactionId(Id.Value));
}

/// <summary>
/// Phase 29 (Wunsch des Nutzers „Fraktionskürzel aus drei Buchstaben, Unikat pro Fraktion“): Kürzel aus dem Namen — Anfangsbuchstaben der Wörter (Füllwörter
/// wie „der“, „von“ zählen nicht), bei weniger als drei Wörtern dazu die ersten Konsonanten bzw. Buchstaben; Umlaute werden aufgelöst (Ä → A). Bei Kollision
/// deterministisch variiert: der letzte Buchstabe läuft durch die übrigen Buchstaben des Namens, dann durch A–Z, dann der mittlere. Rein.
/// </summary>
public static class ActorTags
{
    static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase) { "der", "die", "das", "des", "dem", "den", "von", "vom", "und", "zu", "zur", "zum", "im", "am", "an", "auf" };

    static string Letters(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            char c = char.ToUpperInvariant(ch);
            sb.Append(c switch { 'Ä' => 'A', 'Ö' => 'O', 'Ü' => 'U', 'ß' => 'S', _ => c });
        }
        var r = new System.Text.StringBuilder();
        foreach (var c in sb.ToString()) if (c is >= 'A' and <= 'Z') r.Append(c);
        return r.ToString();
    }

    /// <summary>Wunschkürzel ohne Rücksicht auf andere (drei Großbuchstaben A–Z).</summary>
    public static string Base(string name)
    {
        var words = name.Split(new[] { ' ', '-', '\'', '.', '·', '–' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Filler.Contains(w)).Select(Letters).Where(w => w.Length > 0).ToList();
        if (words.Count == 0) return "XXX";
        string tag;
        if (words.Count >= 3) tag = $"{words[0][0]}{words[1][0]}{words[2][0]}";
        else if (words.Count == 2) tag = $"{words[0][0]}{words[1][0]}{NextConsonant(words[1], 1) ?? NextConsonant(words[0], 1) ?? 'X'}";
        else
        {
            var w = words[0];
            var cons = w.Skip(1).Where(c => !"AEIOU".Contains(c)).ToList();
            tag = $"{w[0]}{(cons.Count > 0 ? cons[0] : w.Length > 1 ? w[1] : 'X')}{(cons.Count > 1 ? cons[1] : w.Length > 2 ? w[2] : 'X')}";
        }
        return tag;
    }

    static char? NextConsonant(string w, int from)
    {
        for (int i = from; i < w.Length; i++) if (!"AEIOU".Contains(w[i])) return w[i];
        return w.Length > from ? w[from] : null;
    }

    /// <summary>Eindeutiges Kürzel: <see cref="Base"/>, bei Kollision mit <paramref name="taken"/> deterministisch variiert.</summary>
    public static string Derive(string name, Func<string, bool> taken)
    {
        var b = Base(name);
        if (!taken(b)) return b;
        var letters = Letters(name).Distinct().ToList();
        foreach (var c in letters) { var t = $"{b[0]}{b[1]}{c}"; if (!taken(t)) return t; }
        for (char c = 'A'; c <= 'Z'; c++) { var t = $"{b[0]}{b[1]}{c}"; if (!taken(t)) return t; }
        for (char m = 'A'; m <= 'Z'; m++) for (char c = 'A'; c <= 'Z'; c++) { var t = $"{b[0]}{m}{c}"; if (!taken(t)) return t; }
        for (char f = 'A'; f <= 'Z'; f++) for (char m = 'A'; m <= 'Z'; m++) for (char c = 'A'; c <= 'Z'; c++) { var t = $"{f}{m}{c}"; if (!taken(t)) return t; }
        return b;
    }
}
