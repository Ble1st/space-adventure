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
    /// <summary>Phase 28 (Prüfung Phase 27): bis zu diesem Tick kündigt die Polizei keine neue Festnahme an (nach Flucht, Ergeben oder Ende eines Polizeigefechts); 0 = keine Sperre.</summary>
    public long ArrestCooldownUntil;
    /// <summary>Ist der Akteur eine Fraktion (hat Politik)?</summary>
    public bool IsFaction(Universe u) => u.Factions.ContainsKey(new FactionId(Id.Value));
}
