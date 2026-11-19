namespace SpaceSim.Core.Model;

/// <summary>Runde 44: abgeleitete Ausdehnung der Sektoren (<see cref="Sector.Extent"/>) — nach dem Weltaufbau, dem Laden und neuen Stationen.</summary>
public static class SectorExtent
{
    /// <summary>Rand über das äußerste Objekt hinaus.</summary>
    public const float Margin = 60f;

    public static void Recompute(Universe u)
    {
        foreach (var s in u.Sectors.Values) Recompute(u, s);
    }

    public static void Recompute(Universe u, Sector s)
    {
        float m = 0f;
        foreach (var id in s.Stations) if (u.Stations.TryGetValue(id, out var st)) m = MathF.Max(m, st.Position.Length);
        foreach (var id in s.Gates) if (u.Gates.TryGetValue(id, out var g)) m = MathF.Max(m, g.Position.Length);
        foreach (var f in s.Fields) m = MathF.Max(m, f.Position.Length);
        foreach (var g in s.GasFields) m = MathF.Max(m, g.Position.Length);   // Gasernte: Feldmitte wie ein Gürtel
        s.Extent = m + Margin;
        s.Radius = s.Extent;   // Kreis der Sektorkarte (vorher fest 10 000; nicht gehasht)
    }
}
