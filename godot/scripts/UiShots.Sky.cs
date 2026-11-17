using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Bilder der Himmelskugeln (<c>--ui-shots-sky</c>, eigene Datei; Stufen 599_xxx): je Sektor Standardkamera, Rückansicht (Kurs -Z) und weit
/// herausgezoomt; Planetensektoren zusätzlich steil (80°), flach (20°) und flache Rückansicht, mit Messung der Planetenscheibe (<c>sky-messung.txt</c>). Aufruf am besten im Handyformat: <c>godot-mono --path godot --resolution 2340x1080 -- --ui-shots &lt;ordner&gt; --ui-shots-sky</c>.
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
		bool planet = sec.Planet is not null;
		string F(string view) => $"sky-{_skyIdx + 1:00}-{name}-{view}.png";
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
			case 599_011: SkyCapture(F("standard"), sec); _stage = planet ? 599_020 : 599_012; _wait = 5; return;
			// Planetensektoren: zusätzlich steil (80°) und flach (20°) — Lage der Planetenscheibe und ihre Helligkeit (sky-messung.txt)
			case 599_020: _m.TestSetCam(0f, 80f, 44.94f); _stage = 599_021; _wait = 20; return;
			case 599_021: SkyCapture(F("steil"), sec); _stage = 599_022; _wait = 5; return;
			case 599_022: _m.TestSetCam(0f, 20f, 44.94f); _stage = 599_023; _wait = 20; return;
			case 599_023: SkyCapture(F("flach"), sec); _stage = 599_012; _wait = 5; return;
			case 599_012: _m.TestCameraAction("cam_chase"); _m.TestCameraAction("cam_reset"); _stage = 599_013; _wait = 90; return;   // Rückansicht auf Höhe 45 / Abstand 35
			case 599_013: SkyCapture(F("rueckansicht"), sec); _stage = planet ? 599_030 : 599_014; _wait = 5; return;
			// flache Rückansicht: Höhe 45 → 15 (drei Stufen tiefer), Abstand 35
			case 599_030: for (int i = 0; i < 3; i++) _m.TestCameraAction("chase_down"); _stage = 599_031; _wait = 90; return;
			case 599_031: SkyCapture(F("rueck-flach"), sec); _m.TestCameraAction("cam_reset"); _stage = 599_014; _wait = 5; return;
			case 599_014: _m.TestCameraAction("cam_free"); _m.TestSetCam(0f, 30f, 140f); _stage = 599_015; _wait = 20; return;
			case 599_015: SkyCapture(F("weit"), sec); _skyIdx++; _stage = 599_010; _wait = 5; return;
		}
	}

	/// <summary>
	/// Bild speichern und — in Planetensektoren — die Planetenscheibe im Bild vermessen: Bildpunkte, deren Sichtstrahl höchstens den Planetenradius
	/// von der Planetenmitte (Welt: Gier <c>skyboxYaw</c>, Höhe <c>elevation</c>) abweicht. Zeile in <c>sky-messung.txt</c>: Anteil am Bild,
	/// mittlere und 90-%-Leuchtdichte (Rec. 709 auf den sRGB-Werten 0–255), ob die Bildmitte in der Scheibe liegt, Lage der Scheibe im Bild, Kameraneigung.
	/// </summary>
	void SkyCapture(string file, Sector sec)
	{
		string path = Path.Combine(_dir, file);
		var cam = _m.TestCam;
		RenderingServer.FramePostDraw += Shot;
		void Shot()
		{
			RenderingServer.FramePostDraw -= Shot;
			var img = GetViewport().GetTexture().GetImage();
			var err = img.SavePng(path);
			GD.Print($"[ui-shots] {file}  {img.GetWidth()}x{img.GetHeight()}  err={err}");
			if (sec.Planet is null || !World.SkyboxView.Manifest.TryGetValue(sec.Skybox, out var e) || e.Radius <= 0f) return;
			int w = img.GetWidth(), h = img.GetHeight();
			var c = World.SkyboxView.WorldDir(sec.SkyboxYaw, e.Elevation).Normalized();
			float cosR = Mathf.Cos(Mathf.DegToRad(e.Radius));
			float t = Mathf.Tan(Mathf.DegToRad(cam.Fov * 0.5f)), aspect = (float)w / h;
			var b = cam.GlobalBasis;
			var lums = new List<float>();
			long all = 0;
			float sx = 0f, sy = 0f, minY = 1f, maxY = 0f, minX = 1f, maxX = 0f;
			for (int y = 0; y < h; y += 2)
				for (int x = 0; x < w; x += 2)
				{
					all++;
					var r = (b * new Vector3((2f * (x + 0.5f) / w - 1f) * t * aspect, (1f - 2f * (y + 0.5f) / h) * t, -1f)).Normalized();
					if (r.Dot(c) < cosR) continue;
					var px = img.GetPixel(x, y);
					float fx = (x + 0.5f) / w, fy = (y + 0.5f) / h;
					sx += fx; sy += fy; minX = Mathf.Min(minX, fx); maxX = Mathf.Max(maxX, fx); minY = Mathf.Min(minY, fy); maxY = Mathf.Max(maxY, fy);
					lums.Add(255f * (0.2126f * px.R + 0.7152f * px.G + 0.0722f * px.B));
				}
			var fwd = -b.Z;
			bool centre = fwd.Normalized().Dot(c) >= cosR;
			float pitch = Mathf.RadToDeg(Mathf.Asin(-fwd.Normalized().Y));
			string line;
			if (lums.Count == 0) line = $"{file}\tNeigung {pitch:0.0}°\tPlanet nicht im Bild";
			else
			{
				lums.Sort();
				float mean = 0f; foreach (var l in lums) mean += l; mean /= lums.Count;
				line = $"{file}\tNeigung {pitch:0.0}°\tPlanet {100.0 * lums.Count / all:0.0} % des Bildes\tmittlere Leuchtdichte {mean:0.0}\t90 % {lums[(int)(0.9f * (lums.Count - 1))]:0.0}\tmax {lums[^1]:0}\tBildmitte {(centre ? "IM Planeten" : "frei")}\tScheibe x {minX:0.00}–{maxX:0.00} (Mitte {sx / lums.Count:0.00}), y {minY:0.00}–{maxY:0.00} (Mitte {sy / lums.Count:0.00}; 0 = oben)";
			}
			GD.Print("[sky-messung] " + line);
			File.AppendAllText(Path.Combine(_dir, "sky-messung.txt"), line + "\n");
		}
	}
}
