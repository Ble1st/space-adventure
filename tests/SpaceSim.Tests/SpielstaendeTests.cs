using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Persistence;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Mehrere Spielstände (<see cref="SaveSlots"/>): Namensregel, Kennungen, Sortierung, Metadaten, Autosave-Einstellung.</summary>
public class SpielstaendeTests
{
    static SaveSlotMeta Meta(string id, string name, int minute, bool auto = false) =>
        new() { Id = id, Name = name, SavedAt = new DateTimeOffset(2026, 11, 24, 12, minute, 0, TimeSpan.Zero), Autosave = auto };

    [Fact]
    public void Namensregel()
    {
        var list = new[] { Meta("s-alpha", "Alpha 1", 1), Meta("auto-g1", "Autosave", 2, auto: true) };
        Assert.Equal("Name fehlt", SaveSlots.NameIssue("   ", list));
        Assert.NotNull(SaveSlots.NameIssue(new string('x', SaveSlots.MaxNameLength + 1), list));
        Assert.NotNull(SaveSlots.NameIssue("a/b", list));
        Assert.NotNull(SaveSlots.NameIssue("  alpha   1 ", list));   // doppelt, Groß/Klein und Leerraum egal
        Assert.Null(SaveSlots.NameIssue("alpha 1", list, overwriteId: "s-alpha"));   // Überschreiben desselben Stands
        Assert.NotNull(SaveSlots.NameIssue("autosave", list));
        Assert.Null(SaveSlots.NameIssue("Beta", list));
        Assert.Equal("Alpha 1", SaveSlots.CleanName("  Alpha \t  1\n"));
    }

    [Fact]
    public void Kennungen_EindeutigUndDateitauglich()
    {
        Assert.Equal("s-oede-stoerung-2", SaveSlots.IdFor("Öde Störung", new[] { "s-oede-stoerung" }));
        Assert.Equal("s-stand", SaveSlots.IdFor("!!!", Array.Empty<string>()));
        Assert.Equal("s-beta-24-11-12-30", SaveSlots.IdFor("Beta 24.11. 12:30", Array.Empty<string>()));
        Assert.All(new[] { "Ä/B", "Name mit vielen Wörtern und sehr langem Text dahinter" }, n =>
        {
            var id = SaveSlots.IdFor(n, Array.Empty<string>());
            Assert.Matches("^s-[a-z0-9-]+$", id);
            Assert.True(id.Length <= 34);
            Assert.False(id.EndsWith('-'));
        });
        Assert.Equal("auto-g1", SaveSlots.AutosaveId("g1"));
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1000);
        var g = SaveSlots.NewGameId(now, Array.Empty<string>());
        Assert.NotEqual(g, SaveSlots.NewGameId(now, new[] { g }));
    }

    [Fact]
    public void Sortierung_NeuesterZuerst()
    {
        var list = new[] { Meta("s-b", "B", 5), Meta("auto-g", "A", 9, auto: true), Meta("s-a", "C", 5) };
        Assert.Equal(new[] { "auto-g", "s-a", "s-b" }, SaveSlots.Sorted(list).Select(m => m.Id));
        Assert.Equal("auto-g", SaveSlots.Newest(list)!.Id);
        Assert.Null(SaveSlots.Newest(Array.Empty<SaveSlotMeta>()));
    }

    [Fact]
    public void Metadaten_AusDemUniversum_UndRundreise()
    {
        var u = WorldBuilder.BuildDefault(startOption: "miner");
        u.Tick = 4 * 3600 + 4 * 300;   // 1 h 05 min
        var hash = UniverseHash.Compute(u);
        var m = SaveSlots.Describe(u, "s-x", " Mein  Stand ", "g1", "miner", "Schürfer", autosave: false, DateTimeOffset.UnixEpoch);
        Assert.Equal(hash, UniverseHash.Compute(u));   // nur lesen
        Assert.Equal("Mein Stand", m.Name);
        Assert.Equal(u.Sectors[u.PlayerSector].Name, m.Sector);
        Assert.Equal(u.PlayerWallet.Credits, m.Credits);
        Assert.Equal(u.ShipModels[u.Ships[u.PlayerShip].ModelKey].Name, m.Ship);
        Assert.Equal("1 h 05 min", SaveSlots.PlayTimeText(m.PlaySeconds));
        Assert.Contains(m.Sector, SaveSlots.Line(m));
        Assert.Contains("Schürfer", SaveSlots.Line(m));
        var back = SaveSlots.ReadMeta(SaveSlots.WriteMeta(m));
        Assert.Equal(m, back);
        Assert.Null(SaveSlots.ReadMeta("{kaputt"));
    }

    [Fact]
    public void Vorschlag_UndAutosaveEinstellung()
    {
        var s = SaveSlots.SuggestName("Alpha", new DateTime(2026, 11, 24, 9, 5, 0));
        Assert.Equal("Alpha 24.11. 09:05", s);
        Assert.Null(SaveSlots.NameIssue(s, Array.Empty<SaveSlotMeta>()));
        Assert.Equal(new[] { "aus", "30 s", "1 min", "5 min" }, SaveSlots.AutosaveOptions.Select(SaveSlots.AutosaveText));
        Assert.Equal(60, SaveSlots.NormalizeAutosave(60));
        Assert.Equal(SaveSlots.DefaultAutosaveSeconds, SaveSlots.NormalizeAutosave(45));
    }
}
