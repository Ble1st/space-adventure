namespace SpaceSim.Core.Model;

/// <summary>
/// Eigene Stationen des Spielers (docs/STATIONEN.md „Eigene Stationen bauen“). Code aus, Standardwelt an; aus oder ohne Bauplatz des Spielers ist die
/// NPC-Welt bitgleich (keine Station gehört dem Spieler, die diese Regeln liest).
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Der Spieler darf über das Baufenster Baustellen eigener Wirtschaftsstationen anlegen.</summary>
    public bool PlayerBuilding;
    /// <summary>Höchstens so viele eigene Baustellen gleichzeitig.</summary>
    public int PlayerMaxSites = 2;
    /// <summary>Höchstens so viele eigene Wirtschaftsstationen (fertig + Baustellen) — eigene Obergrenze, zählt nicht gegen <see cref="MaxStations"/>.</summary>
    public int PlayerMaxStations = 10;
    /// <summary>Höchstens so viele eigene Wirtschaftsstationen (fertig + Baustellen) je Sektor.</summary>
    public int PlayerMaxStationsPerSector = 4;
}
