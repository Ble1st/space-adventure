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

    /// <summary>
    /// Runde 37 (Prüfung): ganz godot/scripts (nicht nur Ui), alle Zeichenaufrufe für Text (DrawString, DrawMultilineString, DrawStringOutline,
    /// DrawMultilineStringOutline, DrawChar, DrawCharOutline) auch mit Leerzeichen vor der Klammer; Kommentare (auch hinter Code) zählen nicht.
    /// </summary>
    static readonly System.Text.RegularExpressions.Regex DrawCall = new(@"\bDraw(String|MultilineString|StringOutline|MultilineStringOutline|Char|CharOutline)\s*\(");

    static string Code(string line)
    {
        int c = line.IndexOf("//", StringComparison.Ordinal);
        return c >= 0 ? line[..c] : line;
    }

    [Fact]
    public void UiZeichnetTexteNurMitMeldung()
    {
        var scripts = Path.Combine(RepoRoot(), "godot", "scripts");
        Assert.True(Directory.Exists(scripts), scripts);
        var offenders = Directory.EnumerateFiles(scripts, "*.cs", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != "UiKit.cs")
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, line, i)))
            .Where(x => DrawCall.IsMatch(Code(x.line)))
            .Select(x => $"{Path.GetRelativePath(scripts, x.f)}:{x.i + 1}")
            .ToList();
        Assert.True(offenders.Count == 0, "Textzeichnen ohne Meldeweg (UiKit.DrawText/DrawFit benutzen): " + string.Join(", ", offenders));
        // die Meldehelfer selbst melden: jeder Zeichenaufruf in UiKit.cs steht neben einer Meldung
        var kit = File.ReadAllLines(Path.Combine(scripts, "Ui", "Kit", "UiKit.cs")).Select(Code).ToList();
        int calls = kit.Count(l => DrawCall.IsMatch(l));
        int reports = kit.Count(l => l.Contains("DrawnText.Report(", StringComparison.Ordinal));
        Assert.True(calls > 0 && calls == reports, $"UiKit.cs: {calls} Zeichenaufrufe, {reports} Meldungen");
    }

    [Theory]
    [InlineData("x.DrawString(font, p, t);", true)]
    [InlineData("x.DrawString (font, p, t);", true)]
    [InlineData("DrawMultilineString(font, p, t);", true)]
    [InlineData("DrawStringOutline(font, p, t);", true)]
    [InlineData("DrawChar(font, p, \"a\");", true)]
    [InlineData("var a = 1; // DrawString(font)", false)]
    [InlineData("UiKit.DrawText(this, 0, font, p, t, 12, c);", false)]
    public void Waechter_ErkenntAlleZeichenaufrufe(string line, bool hit) => Assert.Equal(hit, DrawCall.IsMatch(Code(line)));
}
