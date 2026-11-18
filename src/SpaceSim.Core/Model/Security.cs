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

/// <summary>
/// Spielersperre: durch Verbrechen verlorenes Ansehen des Spielers bei einer KI-Fraktion (<see cref="Simulation.CrimeStanding"/>). <see cref="Debt"/> =
/// noch nicht zurückgewonnener Verlust, <see cref="Base"/> = Beziehung vor dem ersten Verlust dieser Schuld (Erholungsziel), Tagesfenster für den Deckel,
/// letzter Grund für die Anzeige. Gespeichert, gehasht (Spielerpolitik).
/// </summary>
public sealed class CrimeStandingRecord
{
    public float Debt;
    public float Base;
    public long WindowStart;
    public float WindowLoss;
    public string LastReason = "";
    public long LastTick;
    public float LastLoss;
}

/// <summary>Art einer angekündigten Begegnung mit dem Spieler (Vorwarnung, dann Auflösung). Runde 40: <see cref="War"/> = Polizei/Kriegsschiffe einer Kriegsfraktion stellen das Spielerschiff in ihrem Gebiet (kein Ergeben gegen Geld).</summary>
public enum EncounterKind { Raid, Intercept, War }

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
    /// <summary>Runde 13b: Warnung einer Waffenstellung (Kleinstation) an einen Gesuchten — <see cref="Source"/> bleibt leer; default = keine.</summary>
    public StationId Station;
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
        "assault_gate" => "Angriff auf ein Tor",
        "kill" => "Abschuss",
        "capture_station" => "Stationsübernahme",
        "" => "–",
        _ => key,
    };
}
