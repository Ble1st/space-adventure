using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 35c (Prüfung W2): Wächter für das Prüfwerkzeug — jeder selbst gezeichnete Text in godot/scripts/Ui geht über <c>UiKit.DrawText</c>/<c>DrawFit</c>/
/// <c>DrawFit2</c> und meldet sich damit an <c>DrawnText</c> (sonst sieht <c>--ui-check</c> ihn nicht). Ein Quelltext-Scan: <c>DrawString(</c> darf nur in
/// UiKit.cs stehen (in den Meldehelfern).
/// </summary>
public class Runde35cTests
{
    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "SpaceSim.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    [Fact]
    public void UiZeichnetTexteNurMitMeldung()
    {
        var ui = Path.Combine(RepoRoot(), "godot", "scripts", "Ui");
        Assert.True(Directory.Exists(ui), ui);
        var offenders = Directory.EnumerateFiles(ui, "*.cs", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != "UiKit.cs")
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, line, i)))
            .Where(x => x.line.Contains("DrawString(") && !x.line.TrimStart().StartsWith("//"))
            .Select(x => $"{Path.GetRelativePath(ui, x.f)}:{x.i + 1}")
            .ToList();
        Assert.True(offenders.Count == 0, "DrawString ohne Meldeweg (UiKit.DrawText/DrawFit benutzen): " + string.Join(", ", offenders));
        // die Meldehelfer selbst melden
        var kit = File.ReadAllText(Path.Combine(ui, "Kit", "UiKit.cs"));
        Assert.Equal(kit.Split("ci.DrawString(").Length - 1, kit.Split("DrawnText.Report(ci").Length - 1);
    }
}
