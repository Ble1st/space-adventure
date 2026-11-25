using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Bilder der Sonnen (<c>--ui-shots-sun</c>, eigene Datei; Stufen 600_3xx): je ein Sektor mit weißem, blauem, rotem und gelbem Stern — Schiff kurz vor der
/// Atmosphäre (Standardkamera), herausgezoomt und in der Atmosphäre (Warnzeile). Aufruf: <c>godot-mono --path godot --resolution 1560x720 -- --ui-shots &lt;ordner&gt; --ui-shots-sun</c>.
/// </summary>
public partial class UiShots
{
	const int SunShotsStart = 600_301;
	int _sunIdx;
	static readonly int[] SunSectors = { 1, 4, 7, 3 };

	void SunShots()
	{
		var u = _m.Session.U;
		if (_stage == SunShotsStart) { _m.TestMenu("new"); _sunIdx = 0; _stage = 600_310; _wait = 120; return; }
		if (_sunIdx >= SunSectors.Length) { _m.TestMenu("cam_reset"); _stage = 600_400; _wait = 5; return; }
		var sec = u.Sectors[new SectorId(SunSectors[_sunIdx])];
		if (sec.Sun is not { } sun) { _sunIdx++; return; }
		string name = sec.Name.Replace("Sektor ", "").ToLowerInvariant();
		string F(string view) => $"sun-{_sunIdx + 1:00}-{name}-{view}.png";
		float len = sun.Position.Length;
		var dir = len < 1e-3f ? new Vec2(1f, 0f) : (default(Vec2) - sun.Position) * (1f / len);
		switch (_stage)
		{
			case 600_310:
				if (_m.TestDocked) _m.TestUndock();
				_m.TestEnterSector(sec.Id);
				_m.TestTeleportTo(sun.Position + dir * (Suns.AtmosphereRadius(u.Rules, sun) + 40f));
				_m.TestFace(sun.Position);
				_m.TestCameraAction("cam_free");
				_m.TestSetCam(0f, 57.72f, 44.94f);
				_stage = 600_311; _wait = 40; return;
			case 600_311: Capture(F("nah")); _stage = 600_312; _wait = 5; return;
			case 600_312: _m.TestSetCam(Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Y)), 35f, 260f); _stage = 600_313; _wait = 30; return;
			case 600_313: Capture(F("weit")); _stage = 600_314; _wait = 5; return;
			case 600_314:
				_m.TestTeleportTo(sun.Position + dir * (Suns.CoronaRadius(u.Rules, sun) + 25f));
				_m.TestFace(sun.Position);
				_m.TestSetCam(0f, 57.72f, 44.94f);
				_stage = 600_315; _wait = 40; return;
			case 600_315: Capture(F("atmosphaere")); _sunIdx++; _stage = 600_310; _wait = 5; return;
		}
	}
}
