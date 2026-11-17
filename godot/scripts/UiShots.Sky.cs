using System.Linq;
using Godot;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Bilder der Himmelskugeln (<c>--ui-shots-sky</c>, eigene Datei; Stufen 599_xxx): je Sektor Standardkamera, Rückansicht (Kurs -Z) und weit
/// herausgezoomt. Aufruf am besten im Handyformat: <c>godot-mono --path godot --resolution 2340x1080 -- --ui-shots &lt;ordner&gt; --ui-shots-sky</c>.
/// </summary>
public partial class UiShots
{
	const int SkyShotsStart = 599_001;
	int _skyIdx;

	/// <summary>Reihenfolge: zuerst die Planetensektoren, dann alle anderen.</summary>
	SectorId[] SkySectors(Universe u) => u.Sectors.Values.OrderBy(s => s.Planet is null).ThenBy(s => s.Id.Value).Select(s => s.Id).ToArray();

	void SkyShots()
	{
		var u = _m.Session.U;
		if (_stage == SkyShotsStart) { _m.TestMenu("new"); _skyIdx = 0; _stage = 599_010; _wait = 120; return; }
		var list = SkySectors(u);
		if (_skyIdx >= list.Length) { _m.TestCameraAction("cam_free"); _m.TestMenu("cam_reset"); _stage = 600_000; _wait = 5; return; }
		var sec = u.Sectors[list[_skyIdx]];
		string name = sec.Name.Replace("Sektor ", "").ToLowerInvariant();
		switch (_stage)
		{
			case 599_010:
			{
				if (_m.TestDocked) _m.TestUndock();
				_m.TestEnterSector(sec.Id);
				// an die größte Station, aber mit Abstand — Schiff, Station und Himmel zugleich im Bild
				var st = sec.Stations.Select(id => u.Stations[id]).OrderByDescending(s => s.Radius).FirstOrDefault();
				var at = st is null ? (sec.Fields.Count > 0 ? sec.Fields[0].Position + new Vec2(0f, 90f) : new Vec2(0f, 0f)) : st.Position + new Vec2(0f, st.Radius + 45f);
				_m.TestTeleportTo(at);
				_m.TestFace(at + new Vec2(0f, -1000f));   // Kurs -Z (Blickrichtung der Standardkamera)
				_m.TestCameraAction("cam_free");
				_m.TestSetCam(0f, 57.72f, 44.94f);   // Standardansicht: Versatz (0, 38, 24)
				_stage = 599_011; _wait = 30; return;
			}
			case 599_011: Capture($"sky-{_skyIdx + 1:00}-{name}-standard.png"); _stage = 599_012; _wait = 5; return;
			case 599_012: _m.TestCameraAction("cam_chase"); _stage = 599_013; _wait = 45; return;
			case 599_013: Capture($"sky-{_skyIdx + 1:00}-{name}-rueckansicht.png"); _stage = 599_014; _wait = 5; return;
			case 599_014: _m.TestCameraAction("cam_free"); _m.TestSetCam(0f, 30f, 140f); _stage = 599_015; _wait = 20; return;
			case 599_015: Capture($"sky-{_skyIdx + 1:00}-{name}-weit.png"); _skyIdx++; _stage = 599_010; _wait = 5; return;
		}
	}
}
