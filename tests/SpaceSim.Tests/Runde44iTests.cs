using SpaceSim.Core.Content;
using SpaceSim.Core.Model;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 44i: kürzere Anzeigeformen langer Stationsnamen (Kontexttaste, Zielkarte, Ziel-Hinweis) — reine Anzeige.</summary>
public class Runde44iTests
{
    [Theory]
    [InlineData("Hauptquartier – Föderation Nyxar", "Föderation Nyxar", "Hauptquartier")]
    [InlineData("Brennstoffzellenfabrik Hephaistos II", "Hephaistos", "Brennstoffzellenfabrik II")]
    [InlineData("Werft Vesta", "Union", "Werft Vesta")]
    [InlineData("Sektor-HQ Beta", "Beta", "Sektor-HQ")]
    [InlineData("Alpha II", "Alpha", "Alpha II")]   // bliebe nur die Zahl: Name bleibt
    public void Strip_EntferntTeilSamtTrenner(string name, string part, string expected)
        => Assert.Equal(expected, StationNames.Strip(name, part));

    [Fact]
    public void Standardwelt_HauptquartierKurzformMitKuerzel_KandidatenVonLangNachKurz()
    {
        var u = WorldBuilder.BuildDefault();
        var hq = u.Stations.Values.First(s => s.Name.StartsWith("Hauptquartier –"));
        string full = ActorNames.Tagged(u, hq.Owner, hq.Name);
        var c = StationNames.Candidates(u, hq);
        Assert.Equal(full, c[0]);
        Assert.Equal($"{ActorNames.Tag(u, hq.Owner)} Hauptquartier", StationNames.Short(u, hq));
        Assert.Contains(StationNames.Short(u, hq), c);
        Assert.Equal(c.Count, c.Distinct().Count());
        Assert.All(c, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Equal("Hauptquartier – " + u.ActorName(hq.Owner), hq.Name);   // gespeicherter Name unverändert
    }
}
