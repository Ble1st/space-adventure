using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Warum ein Platz für eine Station nicht frei ist (Art für Anzeige und Tests).</summary>
public enum PlacementBlock { None, OutsideSector, Gate, Station, Belt, Rock, Sun }

/// <summary>Ergebnis der Platzprüfung: <see cref="Ok"/>, sonst Art und Text des ersten Hindernisses.</summary>
public readonly record struct PlacementCheck(PlacementBlock Block, string Reason)
{
    public bool Ok => Block == PlacementBlock.None;
    public static readonly PlacementCheck Free = new(PlacementBlock.None, "");
}

/// <summary>
/// Die EINE Platzregel für Stationen (docs/STATIONEN.md „Eigene Stationen bauen“): dieselben Abstände wie der Bau des Lebenszyklus (Phase 8/21/30,
/// <see cref="StationLifecycleSystem.FindFreeSpot"/>) — Tore Radius + 35, Stationen Summe der Radien + 20, Gürtelmitten Radius + 30, Asteroiden Radius +
/// Asteroid + 6, innerhalb der Platzierungsfläche ±150 × Maßstab. Rein, ohne Zufall; der Lebenszyklus zieht seine Zufallspunkte und fragt hier, das Baufenster
/// des Spielers fragt mit dem Platz des Geists.
/// </summary>
public static class StationPlacement
{
    /// <summary>Halbe Kantenlänge der Platzierungsfläche eines Sektors (Runde 44: wächst mit dem Maßstab).</summary>
    public static float HalfArea(Sector sector) => 150f * MathF.Max(1f, sector.Scale);

    /// <summary>Ist der Platz frei (gleiche Vergleiche in gleicher Reihenfolge wie bis zu dieser Regel im Lebenszyklus)?</summary>
    public static bool Fits(Universe u, Sector sector, Vec2 p, float radius) => Check(u, sector, p, radius).Ok;

    /// <summary>Erstes Hindernis am Platz <paramref name="p"/> für eine Station mit Radius <paramref name="radius"/>, oder <see cref="PlacementCheck.Free"/>.</summary>
    public static PlacementCheck Check(Universe u, Sector sector, Vec2 p, float radius)
    {
        float half = HalfArea(sector);
        if (MathF.Abs(p.X) > half || MathF.Abs(p.Y) > half) return new(PlacementBlock.OutsideSector, "außerhalb der Sektorfläche");
        foreach (var g in sector.Gates)
            if (!(Vec2.Distance(p, u.Gates[g].Position) > radius + 35f)) return new(PlacementBlock.Gate, "zu nah am Sprungtor");
        foreach (var s in sector.Stations)
            if (!(Vec2.Distance(p, u.Stations[s].Position) > radius + u.Stations[s].Radius + 20f)) return new(PlacementBlock.Station, $"zu nah an {u.Stations[s].Name}");
        foreach (var f in sector.Fields)
            if (!(Vec2.Distance(p, f.Position) > radius + 30f)) return new(PlacementBlock.Belt, "zu nah am Asteroidengürtel");
        foreach (var a in sector.Asteroids)
            if (!(Vec2.Distance(p, a.Position) > a.Radius + radius + 6f)) return new(PlacementBlock.Rock, "zu nah an einem Felsen");
        if (Suns.TooClose(u, sector, p, radius)) return new(PlacementBlock.Sun, "zu nah an der Sonne");
        return PlacementCheck.Free;
    }
}
