namespace SpaceSim.Core.Model;

/// <summary>
/// Sonne eines Sektors (ERKUNDUNG.md „Sonnen“): ein Himmelskörper in der Spielebene mit Atmosphäre (Korona). Lage und Radius kommen aus der Weltdatei
/// (<c>sectors[].sun</c>, × Sektormaßstab) und stehen nur mit Regel <see cref="EconomyRules.Suns"/> in der Welt. Der Körper ist ein Hindernis, in der
/// Atmosphäre heizt sie Schiffe auf, nahe der Oberfläche (Korona) leidet die Hülle — Regeln in <see cref="Simulation.Suns"/>. Gespeichert, gehasht nur wenn gesetzt.
/// </summary>
public sealed class Sun
{
    public Vec2 Position;
    /// <summary>Radius des Sonnenkörpers (Welteinheiten, schon × Maßstab).</summary>
    public float Radius = 150f;
    /// <summary>Farbe des Lichts und der Korona (#RRGGBB).</summary>
    public string Color = "#ffe6a8";
    /// <summary>Sternart (Anzeige): yellow, orange, white, blue, red.</summary>
    public string Kind = "yellow";
}
