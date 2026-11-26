using SpaceSim.Core.Content;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

/// <summary>
/// Welle B (8): Messhilfe — legt viele fertige Wirtschaftsstationen des Spielers an (Sektoren und Typen reihum, freier Platz wie der Debug-Spawn mit
/// großem Suchradius), um die Rechenzeit einer Stationsflut zu messen (der Bot lief mit 460 Stationen bei 3 ms/Tick).
/// </summary>
static class StationFlood
{
    public static int Spawn(Universe u, int n)
    {
        var types = PlayerBuilding.Catalog(u);
        var sectors = u.Sectors.Values.OrderBy(s => s.Id.Value).ToList();
        int made = 0, tries = 0;
        while (made < n && tries < n * 4)
        {
            var sec = sectors[tries % sectors.Count];
            var t = types[(tries / sectors.Count) % types.Count];
            tries++;
            var spot = Outposts.FreeSpot(u, sec, new Vec2(0f, 0f), t.Radius, t.Radius + 25f, 300f * MathF.Max(1f, sec.Scale));
            if (spot is null) continue;
            var id = u.NewStationId();
            var st = StationFactory.Create(u, id, t, StationFactory.BuiltName(u, t, sec.Id), sec.Id, spot.Value, u.PlayerActor);
            st.Credits = 5000f;
            u.AddStation(st);
            made++;
        }
        foreach (var s in sectors) SectorControl.Update(u, s);
        u.TouchAccess();
        return made;
    }
}
