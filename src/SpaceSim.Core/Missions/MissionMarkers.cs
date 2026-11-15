using SpaceSim.Core.Model;

namespace SpaceSim.Core.Missions;

/// <summary>Runde 41: Art des Missionsmarkers (Anzeige).</summary>
public enum MissionMarkerKind { Station, Ship, Point }

/// <summary>
/// Runde 41: ein Missionsmarker — Sektor und Position des Ziels je aktivem Auftrag (reine Core-Funktion; die Ansicht zeichnet
/// Leuchtfeuer in der Welt, Randpfeile in Gold, Symbole auf den Karten und die HUD-Zeile daraus).
/// </summary>
public sealed record MissionMarker(int Mission, SectorId Sector, Vec2 Position, string Label, MissionMarkerKind Kind);

/// <summary>Runde 41: Marker aller aktiven Aufträge (feste Reihenfolge nach Auftrags-Id).</summary>
public static class MissionMarkers
{
    /// <summary>Alle Marker aktiver Aufträge (Lieferung → Angebots-Station, Transport → Zielstation; später: bewegte Ziele).</summary>
    public static List<MissionMarker> For(Universe u)
    {
        var list = new List<MissionMarker>();
        if (!u.Rules.MissionsActive || u.Missions.Count == 0) return list;
        foreach (var m in u.Missions.Where(x => x.State == MissionState.Active).OrderBy(x => x.Id))
        {
            if (MissionRules.Types.Get(m.Type) is not { } type) continue;
            if (type.Marker(u, m) is { } mk) list.Add(new MissionMarker(m.Id, mk.Sector, mk.Pos, mk.Label, MissionMarkerKind.Station));
        }
        return list;
    }

    /// <summary>Marker des aktiven Auftrags mit der kleinsten Id (HUD-Zeile „der aktive Auftrag“).</summary>
    public static MissionMarker? First(Universe u) => For(u).FirstOrDefault();
}
