namespace SpaceSim.Core.Model;

/// <summary>
/// Gasfeld (SCHIFFSSLOTS.md „Gasernte und Nebel“): ein kreisförmiges Gebiet im Sektor, in dem ein Schiff mit Gassammler (<see cref="ShipStats.GasCollect"/>)
/// die Rohware <c>gas</c> in seinen Flüssigraum (Kanister) schöpft. Kein Hindernis, keine Asteroiden, unerschöpflich (Nebelgas strömt nach); mehrere
/// Sammler im selben Feld teilen sich nichts. Aus der Weltdatei (<c>sectors[].gasFields</c>, Lage × Sektormaßstab), gespeichert.
/// </summary>
public sealed class GasField
{
    public Vec2 Position;
    /// <summary>Radius des Feldes (Welteinheiten, schon × Maßstab).</summary>
    public float Radius = 120f;
    /// <summary>Dichte (Faktor auf die Sammelrate, Standard 1).</summary>
    public float Density = 1f;

    /// <summary>Liegt der Punkt im Feld?</summary>
    public bool Contains(Vec2 p) => Vec2.Distance(p, Position) <= Radius;

    /// <summary>Arbeitsposition eines Sammlers im Feld (je Schiff leicht versetzt, innerhalb des halben Radius).</summary>
    public Vec2 WorkPoint(ShipId ship) => Position + Vec2.FromAngle(ship.Value * 2.4f) * MathF.Min(30f, Radius * 0.5f);
}
