namespace SpaceSim.Core.Model;

/// <summary>
/// Zufällige Universumsereignisse (Lücke zu Interstellar Pilot 2): ein laufendes Ereignis in einem Sektor — Erzschwemme (zusätzliche reiche Felsen in einem
/// Gürtel), Frachtfund (treibende Frachtbehälter), Wrackfeld (Wracks mit Modulen). Gespeichert und gehasht (nur wenn vorhanden); Regeln in
/// <see cref="Simulation.UniverseEvents"/>, Arten hinter <see cref="Simulation.IUniverseEventType"/>.
/// </summary>
public sealed class UniverseEvent
{
    public int Id;
    /// <summary>Schlüssel der Art („ore_surge“, „cargo_find“, „wreck_field“).</summary>
    public string Kind = "";
    public SectorId Sector;
    /// <summary>Ort des Ereignisses (Gürtelmitte bzw. Fundort) — für Marker und Ansicht.</summary>
    public Vec2 Position;
    public long StartTick;
    public long EndTick;
    /// <summary>Anzeigetext der Art (z. B. Gürtelname oder Waren), nur Anzeige (gespeichert, nicht gehasht).</summary>
    public string Detail = "";
    /// <summary>Erzschwemme: Ids der erzeugten Felsen.</summary>
    public List<int> Asteroids = new();
    /// <summary>Frachtfund/Wrackfeld: Ids der erzeugten Wracks/Behälter.</summary>
    public List<int> Wrecks = new();
}
