namespace SpaceSim.Core.Model;

/// <summary>
/// Physischer Zustand eines Jumpgates (Phase 10, SCHIFFSSLOTS.md Abschnitt 5). <see cref="Restricted"/> wird vom Zugangsmodell gesetzt,
/// solange an einem Ende eine Sperrstufe ≥ 1 gilt (das Gate funktioniert, Kontrollpunkte prüfen); <see cref="Closed"/> (Wartung, Ereignis) und
/// <see cref="Destroyed"/> sperren für alle — nur diese beiden sind physisch zu.
/// </summary>
public enum GateState { Open, Restricted, Closed, Destroyed }

/// <summary>Politik eines Kontrollpunkts gegenüber einem Schiff (POLITIK.md Abschnitt 1): erlauben, Zoll verlangen, abweisen.</summary>
public enum AccessPolicy { Allow, Toll, Deny }

/// <summary>Richtung einer Gate-Passage aus Sicht eines Sektors: Einreise in den Sektor oder Ausreise aus ihm.</summary>
public enum AccessDirection { Entry, Exit }

/// <summary>
/// Kontrollstufe einer Fraktion über einen Sektor (POLITIK.md Abschnitt 8): Präsenz (eigene Station), Einfluss (größter Anteil unter 50 %),
/// Dominanz (über 50 % und Wache oder Sektor-HQ), Kontrolle (Dominanz, Sektor-HQ und Patrouillenabdeckung). „Umkämpft“ ist ein eigener Schalter
/// (<see cref="Sector.Contested"/>).
/// </summary>
public enum ControlStage { None, Presence, Influence, Dominance, Control }

/// <summary>
/// Regierungsform eines Stationstyps (Phase 10, STATIONEN.md „Regierungsstationen“). Hauptquartier, Firmensitz, Sektor-HQ und Büro sind
/// <c>Government</c> (eigene Verlustregeln, nie eroberbar); die Polizeiwache ist ein Stationstyp mit Einfluss und Patrouillenabdeckung
/// (Polizei-Logik folgt in Phase 11). Phase 13: Forschungsstation (Unterhalt und Budget aus der Fraktionskasse, nicht eroberbar wie Behörden ist sie
/// aber nicht: sie zählt wie die Wache nur als „offiziell“).
/// </summary>
public enum GovernmentKind { None, Headquarters, CompanySeat, SectorHq, Office, Police, Research }

/// <summary>
/// Sperre eines Sektors (Phase 10, POLITIK.md Abschnitt 2; Entscheidung: Sperren je Sektor, Gates mit Ausnahmen über
/// <see cref="Gate.MainRoute"/>). Stufen 0 Offen … 5 Abriegelung, getrennt für Einreise und Ausreise; Mindestdauer, Ablauf und Abkühlzeit
/// halten Sperren in Grenzen. <see cref="Enforced"/> = die Kasse der Besitzerfraktion trägt die Durchsetzung (sonst nur eine Sperre auf dem Papier).
/// </summary>
public sealed class SectorAccess
{
    /// <summary>Sperrstufe der Einreise (0–5).</summary>
    public int EntryLevel;
    /// <summary>Sperrstufe der Ausreise (0 oder 5; Stufe 4 sperrt nur die Einreise).</summary>
    public int ExitLevel;
    /// <summary>Fraktion, deren Verhalten die Sperre ausgelöst hat (Anzeige, Meinungsverlust), null = keine.</summary>
    public FactionId? Against;
    /// <summary>Tick der letzten Stufenänderung (Mindestdauer).</summary>
    public long SinceTick = -1_000_000;
    /// <summary>Ablauf der laufenden Sperre (keine dauerhafte Schließung durch Fraktionen).</summary>
    public long ExpiresTick = -1;
    /// <summary>Bis zu diesem Tick darf nach einem Ablauf keine neue Sperre entstehen.</summary>
    public long CooldownUntil = -1_000_000;
    /// <summary>Trägt die Kasse die Durchsetzung? Ohne Geld gilt die Sperre nicht (Papier).</summary>
    public bool Enforced = true;

    /// <summary>Gilt im Sektor gar keine Sperre?</summary>
    public bool IsOpen => EntryLevel <= 0 && ExitLevel <= 0;

    /// <summary>Wirksame Stufe einer Richtung (0, wenn die Sperre nicht durchgesetzt wird).</summary>
    public int Level(AccessDirection dir) => !Enforced ? 0 : dir == AccessDirection.Entry ? EntryLevel : ExitLevel;
}

/// <summary>
/// Mobilitätsprofil für die Routensuche (Phase 10, SCHIFFSSLOTS.md Abschnitt 4): was über den Gate-Zugang entscheidet — Besitzer (Akteur, über
/// ihn Fraktion und Beziehungen), beladen oder leer (Embargo) und Tarnstärke (Entdeckung). <see cref="Physical"/> prüft nur den physischen
/// Gate-Zustand (Weltkarte, Diagnose). Sprungantrieb kommt später als weiteres Feld dazu. Der Routen-Cache ist nach Profil getrennt.
/// </summary>
public readonly record struct MobilityProfile(ActorId Actor, bool Laden, float Stealth, bool IsPhysical)
{
    /// <summary>Nur der physische Gate-Zustand zählt (Politik wird ignoriert).</summary>
    public static readonly MobilityProfile Physical = new(ActorId.None, false, 0f, true);

    /// <summary>Profil eines Akteurs (beladen oder leer, ohne Tarnung).</summary>
    public static MobilityProfile For(ActorId actor, bool laden = false, float stealth = 0f) => new(actor, laden, stealth, false);
}

/// <summary>Deutsche Texte für Stufen, Zustände und Kontrollstufen (Anzeige).</summary>
public static class AccessNames
{
    public static string Level(int level) => level switch
    {
        <= 0 => "Offen",
        1 => "Kontrolle",
        2 => "Embargo",
        3 => "Nur Verbündete",
        4 => "Gesperrt",
        _ => "Abriegelung",
    };

    public static string Gate(GateState s) => s switch
    {
        GateState.Open => "offen",
        GateState.Restricted => "eingeschränkt",
        GateState.Closed => "geschlossen",
        _ => "zerstört",
    };

    public static string Stage(ControlStage s) => s switch
    {
        ControlStage.Presence => "Präsenz",
        ControlStage.Influence => "Einfluss",
        ControlStage.Dominance => "Dominanz",
        ControlStage.Control => "Kontrolle",
        _ => "keine",
    };

    public static string Government(GovernmentKind k) => k switch
    {
        GovernmentKind.Headquarters => "Hauptquartier",
        GovernmentKind.CompanySeat => "Firmensitz",
        GovernmentKind.SectorHq => "Sektor-HQ",
        GovernmentKind.Office => "Büro",
        GovernmentKind.Police => "Polizeiwache",
        GovernmentKind.Research => "Forschungsstation",
        _ => "",
    };
}
