using Xunit;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Persistence;

namespace SpaceSim.Tests;

/// <summary>Himmelskugeln: <c>sectors[].skybox</c>/<c>skyboxYaw</c> sind reine Darstellungsdaten — gelesen, geprüft, gespeichert, nicht gehasht.</summary>
public class SkyboxTests
{
    [Fact]
    public void Standardwelt_JederSektorHatEinenHimmel_PlanetensektorenEinenPlanetenhimmel()
    {
        var u = WorldBuilder.BuildDefault();
        foreach (var s in u.Sectors.Values)
        {
            Assert.False(string.IsNullOrEmpty(s.Skybox), s.Name);
            // Namensregel der Inhalte: Planetenhimmel heißen *_planet*, die anderen *_local_star_*
            Assert.Equal(s.Planet is not null, s.Skybox.Contains("_planet"));
        }
    }

    [Fact]
    public void Himmel_ÄndertDenHashNicht()
    {
        var a = WorldBuilder.BuildDefault();
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        foreach (var sd in def.Sectors) { sd.Skybox = null; sd.SkyboxYaw = 0f; }
        var b = WorldBuilder.Build(def);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
    }

    [Theory]
    [InlineData("Mit Leerzeichen")]
    [InlineData("")]
    [InlineData("Gross")]
    public void UngueltigerSchluessel_Wirft(string key)
    {
        var def = WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        def.Sectors[0].Skybox = key;
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(def));
        Assert.Contains("skybox", ex.Message);
    }

    [Fact]
    public void SpeichernLaden_BehaeltHimmel()
    {
        var u = WorldBuilder.BuildDefault();
        var loaded = UniverseSerializer.FromSave(UniverseSerializer.ToSave(u, DateTimeOffset.UnixEpoch));
        foreach (var s in u.Sectors.Values)
        {
            Assert.Equal(s.Skybox, loaded.Sectors[s.Id].Skybox);
            Assert.Equal(s.SkyboxYaw, loaded.Sectors[s.Id].SkyboxYaw);
        }
        Assert.Contains(u.Sectors.Values, s => s.SkyboxYaw != 0f);
    }

    /// <summary>Gerätetest: ein Spielstand von vor den Himmelskugeln zeigte in jedem Sektor nur Schwarz — Laden übernimmt die Himmel der Standardwelt.</summary>
    [Fact]
    public void AlterSpielstandOhneHimmel_BekommtDieHimmelDerStandardwelt()
    {
        var u = WorldBuilder.BuildDefault();
        var old = WorldBuilder.BuildDefault();
        foreach (var sec in old.Sectors.Values) { sec.Skybox = ""; sec.SkyboxYaw = 0f; }
        var json = SaveGameJson.Write(old, DateTimeOffset.UnixEpoch);
        Assert.DoesNotContain("\"Skybox\"", json);
        var loaded = SaveGameJson.Read(json).Universe;
        foreach (var s in u.Sectors.Values)
        {
            Assert.Equal(s.Skybox, loaded.Sectors[s.Id].Skybox);
            Assert.Equal(s.SkyboxYaw, loaded.Sectors[s.Id].SkyboxYaw);
        }
        Assert.Equal(UniverseHash.Compute(old), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void AlteVierSektorWelt_BekommtHimmel_EigeneWeltNicht()
    {
        var classic = UniverseSerializer.FromSave(UniverseSerializer.ToSave(TestWorlds.Classic(), DateTimeOffset.UnixEpoch));
        Assert.All(classic.Sectors.Values, s => Assert.NotEqual("", s.Skybox));
        var own = TestWorlds.Classic();
        own.Sectors.Values.First().Name = "Eigener Sektor";
        var loaded = UniverseSerializer.FromSave(UniverseSerializer.ToSave(own, DateTimeOffset.UnixEpoch));
        Assert.All(loaded.Sectors.Values, s => Assert.Equal("", s.Skybox));
    }
}
