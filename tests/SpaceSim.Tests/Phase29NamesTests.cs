using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Phase 29 (Wunsch des Nutzers): Fraktionskürzel aus drei Buchstaben und gewürfelte Fraktionsnamen aus einer großen Liste.</summary>
public class Phase29NamesTests
{
    [Theory]
    [InlineData("Terranische Union", "TUN")]
    [InlineData("Freie Händler", "FHN")]
    [InlineData("Montanbund", "MNT")]
    [InlineData("Rat der Neun Monde", "RNM")]
    [InlineData("Schwarze Klingen", "SKL")]
    public void Kuerzel_AusDemNamen(string name, string tag) => Assert.Equal(tag, ActorTags.Base(name));

    [Fact]
    public void Kuerzel_EindeutigBeiKollision_Deterministisch()
    {
        var taken = new HashSet<string> { "TUN" };
        string t1 = ActorTags.Derive("Terranische Union", taken.Contains);
        Assert.NotEqual("TUN", t1);
        Assert.Equal(3, t1.Length);
        Assert.Equal(t1, ActorTags.Derive("Terranische Union", taken.Contains));
        // viele gleich beginnende Namen: alle verschieden
        var used = new HashSet<string>();
        for (int i = 0; i < 200; i++) Assert.True(used.Add(ActorTags.Derive($"Hanse {i}", used.Contains)));
    }

    [Fact]
    public void Namensliste_GrossOhneDoppelte_JeArt()
    {
        int total = 0;
        foreach (var kind in new[] { ActorKind.State, ActorKind.Guild, ActorKind.Independent, ActorKind.Outlaw })
        {
            var all = FactionNames.All(kind);
            Assert.True(all.Count >= 200, $"{kind}: {all.Count}");
            Assert.Equal(all.Count, all.Distinct().Count());
            total += all.Count;
        }
        Assert.True(total >= 3000, $"{total} Namen");
        // Runde 11: keine markennahen Namen, keine Sektor-/Planetennamen der Welt im Vorrat; die Liste wird je Art einmal gebaut
        var world = TestWorlds.Default(fuel: false, research: false);
        var places = world.Sectors.Values.Select(x => x.Name).Concat(world.Sectors.Values.Where(x => x.Planet is not null).Select(x => x.Planet!.Name)).ToList();
        foreach (var kind in new[] { ActorKind.State, ActorKind.Guild, ActorKind.Independent, ActorKind.Outlaw })
        {
            var all = FactionNames.All(kind);
            Assert.Same(all, FactionNames.All(kind));
            foreach (var bad in new[] { "Polaris", "Helion", "Kestrel" }) Assert.DoesNotContain(all, n => n.Contains(bad));
            foreach (var place in places) Assert.DoesNotContain(all, n => n.Split(' ', '-').Contains(place));
        }
    }

    [Fact]
    public void Start_WuerfeltJeSeedReproduzierbareNamen_KuerzelEindeutig_StationsnamenFolgen()
    {
        var a1 = WorldBuilder.BuildDefault(11);
        var a2 = WorldBuilder.BuildDefault(11);
        var b = WorldBuilder.BuildDefault(12);
        string Names(Universe u) => string.Join("|", u.Actors.Values.Where(x => x.Kind is ActorKind.State or ActorKind.Guild or ActorKind.Outlaw).OrderBy(x => x.Id.Value).Select(x => x.Name));
        Assert.Equal(Names(a1), Names(a2));
        Assert.NotEqual(Names(a1), Names(b));
        // kein Name doppelt, jedes Kürzel einmalig und dreibuchstabig
        Assert.Equal(a1.Actors.Count, a1.Actors.Values.Select(x => x.Name).Distinct().Count());
        Assert.Equal(a1.Actors.Count, a1.Actors.Values.Select(x => x.Tag).Distinct().Count());
        Assert.All(a1.Actors.Values, x => Assert.Matches("^[A-Z]{3}$", x.Tag));
        Assert.Equal("FRE", a1.Actors[a1.FreeActor].Tag);
        // Fraktion und Akteur tragen denselben Namen; Stationsnamen mit {owner} nehmen ihn auf
        foreach (var f in a1.Factions.Values) Assert.Equal(f.Name, a1.Actors[f.Id].Name);
        var hq = a1.StationsById().First(s => s.TypeKey == "headquarters");
        Assert.Contains(a1.ActorName(hq.Owner), hq.Name);
        Assert.DoesNotContain(a1.StationsById(), s => s.Name.Contains("{owner}"));
        // die Wirtschaft ist von den Namen unabhängig (eigener Namenszufall)
        var fixedNames = TestWorlds.Def(fuel: false, research: false);
        var c = WorldBuilder.Build(fixedNames, 11);
        new SimulationRunner().Run(a1, 300); new SimulationRunner().Run(c, 300);
        Assert.Equal(a1.Rng.State, c.Rng.State);
    }

    [Fact]
    public void BefoerderterAkteur_BekommtNamenUndKuerzel_SpeichernLaden()
    {
        var u = WorldBuilder.BuildDefault(3);
        var ship = u.ShipsById().First(s => s.Owner == u.FreeActor && s.Role == ShipRole.Trader);
        var id = ActorRules.Promote(u, ship);
        var a = u.Actors[id];
        Assert.Contains(a.Name, FactionNames.All(ActorKind.Independent));
        Assert.Matches("^[A-Z]{3}$", a.Tag);
        Assert.Equal(1, u.Actors.Values.Count(x => x.Tag == a.Tag));
        Assert.Equal($"[{a.Tag}] Werft X", ActorNames.Tagged(u, id, "Werft X"));
        var (copy, _) = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch));
        Assert.Equal(a.Tag, copy.Actors[id].Tag);
        Assert.Equal(a.Name, copy.Actors[id].Name);
        Assert.Equal(UniverseHash.Compute(u), UniverseHash.Compute(copy));
    }
}
