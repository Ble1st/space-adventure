namespace SpaceSim.Core.Model;

/// <summary>
/// Wurmlöcher (docs/ERKUNDUNG.md „Wurmlöcher“): stabile Paare aus der Weltdatei (zusätzliche Kanten im Torgraphen, ohne Kontrollpunkt) und instabile, die
/// ein eigener RNG aus Welt-Seed und Takt öffnet (nur der Spieler nutzt sie). Code-Vorgabe aus (Welt bitgleich zu vorher); die Standardwelt schaltet ein.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Schalter: Wurmlöcher (stabile aus der Weltdatei, instabile per Zufall).</summary>
    public bool Wormholes;
    /// <summary>Instabile Wurmlöcher: alle so viele Ticks wird gewürfelt, ob eines aufreißt (0 = keine instabilen).</summary>
    public int WormholeCheckTicks = 2400;
    /// <summary>Wahrscheinlichkeit eines neuen instabilen Wurmlochs je Prüfung.</summary>
    public float WormholeChance = 0.4f;
    /// <summary>Höchstens so viele instabile Wurmlöcher gleichzeitig.</summary>
    public int WormholeMaxUnstable = 2;
    /// <summary>Lebensdauer eines instabilen Wurmlochs in Ticks (gleichverteilt zwischen Min und Max).</summary>
    public int WormholeMinTicks = 3600;
    public int WormholeMaxTicks = 7200;
    /// <summary>Einflugradius eines instabilen Wurmlochs (wie der Sprungradius der Tore).</summary>
    public float WormholeRadius = 10f;
}
