namespace SpaceSim.Core.Model;

/// <summary>
/// Lücken gegenüber Interstellar Pilot 2: Spielerstatistik (Zähler auf dem Spielerakteur, <see cref="Actor.Stats"/>). Entsteht erst beim ersten
/// Spielerereignis (null = nichts gezählt), wird additiv gespeichert und nur mit Spielerpolitik gehasht — die NPC-Welt bleibt bitgleich.
/// Geführt von <see cref="Simulation.PlayerStatsSystem"/> aus dem Ereignisbus und der Kasse des Spielers.
/// </summary>
public sealed class PlayerStats
{
    /// <summary>Summe aller Zugänge der Spielerkasse (Cr).</summary>
    public double Earned;
    /// <summary>Summe aller Abgänge der Spielerkasse (Cr).</summary>
    public double Spent;
    /// <summary>Höchster je gesehener Kassenstand.</summary>
    public double MaxWallet;
    /// <summary>Kassenstand beim letzten Vergleich (Grundlage für Zu-/Abgänge; gespeichert, damit Laden die Zählung nicht verschiebt).</summary>
    public double LastWallet;
    /// <summary>Sektor beim letzten Vergleich (Grundlage für Sprünge).</summary>
    public int LastSector;
    /// <summary>Handel: Erlös aus Verkäufen bzw. Ausgaben für Einkäufe (Cr) und gehandelte Einheiten.</summary>
    public double TradeRevenue, TradeSpent, TradeUnits;
    public int Sales, Purchases;
    /// <summary>Abgebautes Erz (Einheiten; eigene Schiffe).</summary>
    public double OreMined;
    /// <summary>Abschüsse (Gegner zerstört, an deren Gefecht ein eigenes Schiff beteiligt war) und verlorene eigene Schiffe.</summary>
    public int Kills, ShipsLost;
    /// <summary>Erfüllte und gescheiterte Aufträge.</summary>
    public int MissionsDone, MissionsFailed;
    /// <summary>Sektorwechsel des aktiven Schiffs (Torsprünge, Sprungantrieb, Taxi).</summary>
    public int Jumps;
    /// <summary>Verdiente Kopfgelder (Anzahl und Summe).</summary>
    public int Bounties;
    public double BountyCredits;
    /// <summary>Umgeladene Einheiten zwischen eigenen Schiffen.</summary>
    public double Transferred;
    /// <summary>Piloten: gewichtete Abschusspunkte, gezählte Verbrechen (je Tick und Art einmal), zuletzt gemeldeter Rang und eigener Pilotenname
    /// (null = aus dem Welt-Seed). Geführt von <see cref="Pilots.PilotSystem"/>; gespeichert/gehasht nur, wenn gesetzt.</summary>
    public double KillPoints;
    public int Crimes;
    public int RankSeen;
    public string? PilotName;
    /// <summary>Welle C (1): Steuern aus eigenem Gebiet (Cr); gespeichert/gehasht nur, wenn gesetzt.</summary>
    public double TaxCredits;
}
