namespace SpaceSim.Core.Model;

/// <summary>Runde 41: Zustand eines angenommenen Auftrags.</summary>
public enum MissionState { Active, Completed, Failed }

/// <summary>
/// Runde 41: ein Auftragsangebot einer Station (oder über Comms an eine Fraktion). Entsteht aus dem Weltzustand mit einem eigenen
/// Zufall aus dem Welt-Seed (<see cref="Missions.MissionRng"/>, nie <see cref="Universe.Rng"/>). Gespeichert (additiv), aber
/// <b>nicht gehasht</b> — reine Anzeige, bis der Spieler annimmt. <see cref="Args"/> hält die fachlichen Parameter als Text
/// („good“, „units", „target", „price", „channel", „contract" …), siehe <see cref="Missions.IMissionType"/>.
/// </summary>
public sealed class MissionOffer
{
    public int Id;
    /// <summary>Auftragsart (Schlüssel der <see cref="Missions.MissionTypes"/>-Registry), z. B. „delivery“ oder „transport“.</summary>
    public string Type = "";
    /// <summary>Auftraggeber (Akteur — der Besitzer der Station; bei Fraktionen der Fraktionsakteur).</summary>
    public ActorId Client;
    /// <summary>Station, an der das Angebot hängt (Dock-Bereich „Aufträge“).</summary>
    public StationId At;
    public Dictionary<string, string> Args = new();
    /// <summary>Belohnung aus der Fraktionskasse (beim Annehmen reserviert); 0 = keine Bar-Belohnung (Liefervertrag: Ware zum Marktpreis).</summary>
    public float Reward;
    /// <summary>Pfand des Spielers (wird bei Annahme von der Spielerkasse gehalten; verfällt bei Scheitern an die Station).</summary>
    public float Deposit;
    /// <summary>Verfalls-Tick des Angebots (0 = nie).</summary>
    public long Expires;
    /// <summary>Entstehungs-Tick.</summary>
    public long Created;
    /// <summary>Runde 41: Liefervertrag — wurde schon einmal nachverhandelt (+5 %)? Einmal je Angebot.</summary>
    public bool Haggled;
}

/// <summary>
/// Runde 41: ein angenommener Auftrag. Gespeichert und <b>gehasht</b> (nur vorhandene Aufträge mischen in den Hash ein).
/// <see cref="Reserved"/> ist die bei der Annahme aus <see cref="Simulation.FactionViability.Spendable"/> der Auftraggeber-Kasse
/// zurückgelegte Belohnung; <see cref="Deposit"/> das Pfand des Spielers. <see cref="Progress"/> ist der fachliche Fortschritt
/// (gelieferte Netto-Einheiten bzw. 0/1 bei Transport), <see cref="Sold"/>/<see cref="Bought"/> die Brutto-Ströme an der Zielstation
/// (Verkauf minus Einkauf derselben Ware dort seit Annahme = netto; „Kaufen und zurückverkaufen“ zählt nicht).
/// </summary>
public sealed class Mission
{
    public int Id;
    public string Type = "";
    public ActorId Client;
    public StationId At;
    public Dictionary<string, string> Args = new();
    /// <summary>Bei Annahme aus der Kasse des Auftraggebers reservierte Belohnung (TreasuryUpkeep „mission_reserve“).</summary>
    public float Reserved;
    /// <summary>Vom Spieler hinterlegtes Pfand (zurück bei Erfüllung, verfällt an die Station bei Scheitern).</summary>
    public float Deposit;
    /// <summary>Von der Station für einen Liefervertrag reservierte Kasse (n × Festpreis), bis zum Abschluss gehalten.</summary>
    public float ReservedCash;
    /// <summary>Vom Spieler an Bord genommene versiegelte Fracht (Volumen; <see cref="Ship.MissionCargo"/>).</summary>
    public float CargoVolume;
    /// <summary>Gelieferte Einheiten brutto (Verkäufe der Ware an der Zielstation seit Annahme).</summary>
    public float Sold;
    /// <summary>Gekaufte Einheiten derselben Ware an der Zielstation seit Annahme (mindern den Netto-Fortschritt).</summary>
    public float Bought;
    /// <summary>Netto-Fortschritt (gelieferte Einheiten nach Abzug der Käufe dort).</summary>
    public float Progress => MathF.Max(0f, Sold - Bought);
    public long Deadline;
    public long AcceptedTick;
    public MissionState State = MissionState.Active;
    /// <summary>Runde 41: Liefervertrag — einmal Nachverhandeln (+5 %) war möglich und wurde genutzt.</summary>
    public bool Haggled;
}

/// <summary>
/// Runde 41: Budget-/Sperrbuchaltung der Aufträge je Fraktion (nur geschrieben durch Spielerhandlungen — ohne angenommenen Auftrag
/// leer, der NPC-Hash ändert sich nicht). <see cref="Day"/> = Spieltag (Tick ÷ <see cref="EconomyRules.DayTicks"/>), <see cref="Spent"/>
/// = heute reservierte Belohnungen, <see cref="BlockedUntil"/> = bis wann die Fraktion keine neuen Aufträge vergibt (nach Scheitern/Abbruch).
/// Gespeichert und gehasht, sobald Einträge existieren.
/// </summary>
public sealed class MissionFactionState
{
    public long Day;
    public float Spent;
    public long BlockedUntil;
}
