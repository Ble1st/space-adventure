namespace SpaceSim.Core.Model;

/// <summary>
/// Runde 40: eine Nachricht im Posteingang des Spielers (<see cref="Universe.Inbox"/>). Entsteht aus Simulationsereignissen
/// (<see cref="Comms.CommsSystem"/>), nie aus Zufall. <see cref="Kind"/> bestimmt Betreff/Text und die Antworten
/// (<see cref="Comms.CommsText"/>); <see cref="Args"/> hält Parameter (z. B. „faction" → Fraktions-Id als Text, „amount" → Betrag).
/// Gespeichert (additiv), nicht gehasht — reine Anzeige.
/// </summary>
public sealed class CommsMessage
{
    public int Id;
    /// <summary>Absender (Akteur; 0 = System/Spiel).</summary>
    public ActorId From;
    /// <summary>Art: „war_declared“, „peace_concluded“, „ceasefire_concluded“, „ceasefire_offer“, „peace_offer“, „wanted_up“, „bounty_earned“.</summary>
    public string Kind = "";
    /// <summary>Parameter (Texte; Ids als Dezimaltext).</summary>
    public Dictionary<string, string> Args = new();
    /// <summary>Entstehungs-Tick.</summary>
    public long Created;
    /// <summary>Verfalls-Tick (0 = nie); abgelaufene Nachrichten werden beim nächsten Einstroem entfernt.</summary>
    public long Expires;
    /// <summary>Gelesen (Zähler am Menü zählt ungelesene).</summary>
    public bool Read;
}
