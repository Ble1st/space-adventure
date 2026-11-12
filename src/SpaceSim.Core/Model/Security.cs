namespace SpaceSim.Core.Model;

/// <summary>
/// Fahndungseintrag eines Akteurs bei einer Fraktion (Phase 11, FLOTTEN.md „Fahndungsniveau (Heat)“): <see cref="Heat"/> steigt durch Verbrechen und
/// verfällt nach einer Mindestdauer (<see cref="LastCrimeTick"/>); <see cref="Fine"/> ist die aufgelaufene Strafe (Gericht). Die Stufe folgt aus der Heat
/// (<see cref="Simulation.Wanted.Level"/>).
/// </summary>
public sealed class WantedRecord
{
    public float Heat;
    public long LastCrimeTick;
    public float Fine;
    /// <summary>Letztes Verbrechen (Schlüssel aus <see cref="EconomyRules.CrimeHeat"/>, Anzeige).</summary>
    public string LastCrime = "";
}

/// <summary>Art einer angekündigten Begegnung mit dem Spieler (Vorwarnung, dann Auflösung).</summary>
public enum EncounterKind { Raid, Intercept }

/// <summary>
/// Angekündigte Begegnung mit dem Spieler (Phase 11): Ein Kaperschiff (Überfall) oder eine Streife (Abfangen bei Fahndungsstufe „gesucht“) hat ihn im
/// Sektor entdeckt; bis <see cref="DueTick"/> kann er andocken oder den Sektor verlassen. Gespeichert (Teil des Piratenzustands).
/// </summary>
public sealed class PendingEncounter
{
    public EncounterKind Kind;
    public ShipId Source;
    public ShipId Target;
    public SectorId Sector;
    public long DueTick;
}

/// <summary>Deutsche Anzeigenamen für Lagekarte und Fahndung.</summary>
public static class SecurityNames
{
    /// <summary>Fahndungsstufen 0–4 (FLOTTEN.md).</summary>
    public static string WantedLevel(int level) => level switch
    {
        <= 0 => "sauber",
        1 => "auffällig",
        2 => "verwarnt",
        3 => "gesucht",
        _ => "Staatsfeind",
    };

    /// <summary>Sicherheitsniveau als Wort (Anzeige der Lagekarte).</summary>
    public static string Security(float security) => security switch
    {
        < 0.5f => "ungeschützt",
        < 1.5f => "niedrig",
        < 3f => "mittel",
        _ => "hoch",
    };

    /// <summary>Bedrohung als Wort.</summary>
    public static string Threat(float threat) => threat switch
    {
        < 0.25f => "ruhig",
        < 1f => "gering",
        < 2.5f => "erhöht",
        _ => "hoch",
    };

    /// <summary>Verbrechen (Schlüssel der Regeln) als Wort.</summary>
    public static string Crime(string key) => key switch
    {
        "smuggling" => "Schmuggel",
        "toll_evasion" => "Zollflucht",
        "piracy" => "Piraterie",
        "assault" => "Angriff",
        "assault_station" => "Angriff im Stationsschutz",
        "" => "–",
        _ => key,
    };
}
