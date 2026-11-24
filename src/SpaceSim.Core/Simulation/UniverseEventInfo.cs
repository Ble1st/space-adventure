using SpaceSim.Core.Events;
using SpaceSim.Core.Intel;
using SpaceSim.Core.Model;
using SpaceSim.Core.Ui;

namespace SpaceSim.Core.Simulation;

/// <summary>Marker eines sichtbaren Universumsereignisses (Sektor, Ort, Beschriftung).</summary>
public sealed record UniverseEventMarker(int Event, string Kind, SectorId Sector, Vec2 Position, string Label);

/// <summary>
/// Anzeige der Universumsereignisse (rein): nur Ereignisse in Sektoren, die der Spieler kennt (Spielerwissen, Ausbau Punkt 16). Meldungsgewicht nach den
/// Regeln von <see cref="MessageRules"/>: im eigenen Sektor wichtig (eine Gelegenheit für dich), in einem anderen bekannten Sektor Info, sonst aus.
/// </summary>
public static class UniverseEventInfo
{
    public static bool Visible(Universe u, SectorId sector) => Knowledge.KnowsSector(u, sector);

    public static string Name(string kind) => UniverseEventTypes.Of(kind)?.Name ?? "Ereignis";

    /// <summary>Restzeit als „m:ss“ (Spielzeit).</summary>
    public static string Rest(Universe u, UniverseEvent e)
    {
        long ticks = Math.Max(0, e.EndTick - u.Tick);
        long sec = ticks / SimClock.TicksPerSecond;
        return $"{sec / 60}:{sec % 60:00}";
    }

    /// <summary>Kurze Marker-Beschriftung („Erzschwemme · noch 25:00“).</summary>
    public static string Label(Universe u, UniverseEvent e) => $"{Name(e.Kind)} · noch {Rest(u, e)}";

    /// <summary>Marker aller sichtbaren Ereignisse (Reihenfolge des Beginns).</summary>
    public static List<UniverseEventMarker> Markers(Universe u) =>
        u.UniverseEvents.Where(e => Visible(u, e.Sector)).Select(e => new UniverseEventMarker(e.Id, e.Kind, e.Sector, e.Position, Label(u, e))).ToList();

    /// <summary>Zeile für die Detailkarte der Universumskarte (leer = kein sichtbares Ereignis im Sektor).</summary>
    public static string Line(Universe u, SectorId sector)
    {
        if (!Visible(u, sector)) return "";
        var list = u.UniverseEvents.Where(e => e.Sector == sector).ToList();
        if (list.Count == 0) return "";
        return "Ereignis: " + string.Join(" · ", list.Select(e => $"{Name(e.Kind)} ({e.Detail}), noch {Rest(u, e)}"));
    }

    /// <summary>Meldungsgewicht eines Ereignisbeginns: eigener Sektor wichtig, bekannter Sektor Info, unbekannter aus.</summary>
    public static MessageWeight Weight(Universe u, SectorId sector) =>
        sector == u.PlayerSector ? MessageWeight.Important : Visible(u, sector) ? MessageWeight.Info : MessageWeight.Off;

    /// <summary>Meldungstext zum Beginn (null = nicht zeigen).</summary>
    public static string? StartText(Universe u, UniverseEventStarted e)
    {
        if (Weight(u, e.Sector) == MessageWeight.Off) return null;
        string where = u.Sectors.TryGetValue(e.Sector, out var s) ? s.Name : "?";
        return e.Sector == u.PlayerSector ? $"{e.Title} — hier in {where}!" : $"{e.Title} in {where}.";
    }

    /// <summary>Meldungstext zum Ende (nur im eigenen Sektor; null = nicht zeigen).</summary>
    public static string? EndText(Universe u, UniverseEventEnded e) =>
        e.Sector != u.PlayerSector ? null : e.Reason == "exhausted" ? $"{Name(e.Kind)} hier vorbei: alles geborgen." : $"{Name(e.Kind)} hier vorbei.";
}
