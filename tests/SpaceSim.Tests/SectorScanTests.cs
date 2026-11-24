using SpaceSim.Core.Content;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>UI-Umbau: Scanliste („☰ → Scan“) — alles Sichtbare im Sektor nach Entfernung.</summary>
public class SectorScanTests
{
    [Fact]
    public void Startwelt_ListeNachEntfernung_StationenToreFelsen()
    {
        var u = WorldBuilder.BuildDefault();
        var me = u.Ships[u.PlayerShip];
        var list = SectorScan.For(u, me.Id, me.Position);
        Assert.NotEmpty(list);
        Assert.Contains(list, c => c.Kind == ScanKind.Station);
        Assert.Contains(list, c => c.Kind == ScanKind.Gate);
        Assert.True(list.Count(c => c.Kind == ScanKind.Rock) <= SectorScan.MaxRocks);
        for (int i = 1; i < list.Count; i++) Assert.True(list[i - 1].Distance <= list[i].Distance + 1e-3f);
        Assert.DoesNotContain(list, c => c.Kind == ScanKind.Ship && c.Id == me.Id.Value);
    }
}
