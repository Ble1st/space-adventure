using System.Linq;
using Godot;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 124 (Runde 44, größere Sektoren): (1) Randpfeile tragen die Entfernung („· 1,2 km“); (2) Sterne kacheln mit der Kamera, der Planet
/// folgt mit Parallaxe, die Sichtweite reicht über den Sektor; (3) Sektorkarte mit Maßstabsleiste, Universumkarte mit „Ø … · Flug …“; (4) Zeitraffer
/// mit Autopilot: der eigene Flug läuft in Teilschritten (×10), ohne harten Aufprall, und das Andocken schaltet den Zeitraffer ab.
/// </summary>
public partial class SelfTest
{
	Vector3 _r44bStart;
	StationId _r44bTarget;
	int _r44bDockFrame = -1;
	float _r44bDist;

	bool Round44bStage(int f)
	{
		var u = _m.Session.U;
		if (f == 0) { _m.TestMenu("new:trader"); return false; }
		if (f == 40)
		{
			var sec = u.Sectors[u.PlayerSector];
			Check(sec.Scale >= 3f && sec.Scale <= 8f, $"R44: Startsektor {sec.Name} Maßstab {sec.Scale:0.0} (3–8)");
			var list = _m.HudRef.StationScreenPositions?.Invoke();
			Check(list is { Count: > 0 } && list.All(e => e.Name.Contains(" · ") && (e.Name.EndsWith(" km") || e.Name.EndsWith(" m"))),
				"R44: Randpfeil-Namen tragen die Entfernung: " + string.Join(" | ", (list ?? new System.Collections.Generic.List<(Vector2, string, int)>()).Take(3).Select(e => e.Name)));
			Check(_m.HudRef.EdgeMarkers.Count > 0, $"R44: {_m.HudRef.EdgeMarkers.Count} Randpfeile im großen Sektor");
			var cam = _m.TestCam.Position;
			var tiles = _m.GetChildren().OfType<MultiMeshInstance3D>().Where(n => n.Name.ToString().StartsWith("Sterne")).ToList();
			Check(tiles.Count == 9 && tiles.Any(t => Mathf.Abs(t.Position.X - cam.X) <= 400.1f && Mathf.Abs(t.Position.Z - cam.Z) <= 400.1f), $"R44: Sternkacheln um die Kamera ({tiles.Count})");
			Check(_m.TestCam.Far >= sec.Extent * 2f, $"R44: Sichtweite {_m.TestCam.Far:0} ≥ 2 × Ausdehnung {sec.Extent:0}");
			// Sektorkarte mit Maßstabsleiste
			_m.TestOpenCcArea(Ui.Screens.CommandAreas.Sector);
			return false;
		}
		if (f == 60)
		{
			var bar = _m.HudRef.CcSector.TestScaleBar;
			Check(bar.Text.Length > 0 && bar.Px > 20f && bar.Px <= 151f, $"R44: Maßstabsleiste der Sektorkarte „{bar.Text}“ ({bar.Px:0} px)");
			var other = u.Sectors.Values.Where(s => s.Id != u.PlayerSector).OrderBy(s => s.Id.Value).First();
			_m.HudRef.CcUniverse.TestSelect(other.Id);
			_m.TestOpenCcArea(Ui.Screens.CommandAreas.Universe);
			return false;
		}
		if (f == 80)
		{
			string line = _m.HudRef.CcUniverse.TestSizeLine;
			Check(line.StartsWith("Ø ") && line.Contains("Flug ~"), $"R44: Universumkarte „{line}“");
			_m.TestCloseCommand();
			// Zeitraffer mit Autopilot: zur entferntesten andockbaren Station im Sektor
			var me = u.Ships[u.PlayerShip];
			var far = u.Sectors[u.PlayerSector].Stations.Select(id => u.Stations[id]).Where(s => s.Dockable && s.Id != me.DockedAt)
				.OrderByDescending(s => Vec2.Distance(s.Position, me.Position)).First();
			_r44bTarget = far.Id;
			_r44bDist = Vec2.Distance(far.Position, me.Position);
			if (_m.TestDocked) _m.TestUndock();
			return false;
		}
		if (f == 100)
		{
			_m.TestSetStationTarget(_r44bTarget);
			_m.TestSetFast(true);
			_r44bStart = _m.Player.Position;
			_lowestHullR44 = _m.Session.U.Ships[_m.Session.U.PlayerShip].Hull;
			return false;
		}
		if (f > 100 && f < 100 + 60 * 40)
		{
			var me = u.Ships[u.PlayerShip];
			_lowestHullR44 = Mathf.Min(_lowestHullR44, me.Hull);
			if (f == 160)
			{
				Check(_m.TestFlightSteps > 1, $"R44: Zeitraffer mit Autopilot — {_m.TestFlightSteps} Flug-Teilschritte je Bild");
				float moved = _m.Player.Position.DistanceTo(_r44bStart);
				Check(moved > 45f * 1f * 2.5f, $"R44: in 1 s Echtzeit {moved:0} m geflogen (Zeitraffer, Echtzeit wären ≤ 45 m)");
			}
			if (_m.TestDocked && _r44bDockFrame < 0) _r44bDockFrame = f;
			if (_r44bDockFrame >= 0 && f >= _r44bDockFrame + 10)
			{
				Check(_m.TestDockedStation == _r44bTarget, $"R44: per Autopilot im Zeitraffer angedockt ({_r44bDist:0} m in {(_r44bDockFrame - 100) / 60f:0.0} s Echtzeit)");
				Check(!_m.TestFast, "R44: Andocken schaltet den Zeitraffer ab");
				Check(_lowestHullR44 >= me.MaxHull - 0.01f, $"R44: kein Aufprall im Zeitraffer-Flug (Hülle min {_lowestHullR44:0}/{me.MaxHull:0})");
				_m.TestUndock();
				return true;
			}
			return false;
		}
		if (f >= 100 + 60 * 40) { Check(false, $"R44: Zeitraffer-Flug kam nicht an (Autopilot {_m.TestAutopilot}, Abstand {_m.Player.Position.DistanceTo(new Vector3(u.Stations[_r44bTarget].Position.X, 0, u.Stations[_r44bTarget].Position.Y)):0})"); _m.TestSetFast(false); return true; }
		return false;
	}
	float _lowestHullR44;
}
