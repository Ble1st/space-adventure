using System.Linq;
using Godot;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 123 (Runde 44, Rückansicht): echter Tipp auf „Rückansicht“ im Menü, ◀ halten — die Kamera steht danach hinter dem Bug
/// (Skalarprodukt Blickrichtung·Bug &gt; 0,95); „Höhe +“ per Tipp ändert die Höhe und die Einstellungsdatei; neu geladene Einstellungen ergeben
/// denselben Zustand; Andocken/Abdocken und „Neues Spiel“ behalten den Modus. Zum Schluss zurück auf die freie Kamera (spätere Stufen).
/// </summary>
public partial class SelfTest
{
	Vector2 _r44Tap;
	float _r44Height;

	/// <summary>Waagrechte Blickrichtung der Kamera gegen den Bug des Schiffs (1 = genau hinter dem Bug).</summary>
	float R44BehindDot()
	{
		var cam = _m.TestCam;
		var fwd = -cam.GlobalTransform.Basis.Z; fwd.Y = 0; fwd = fwd.Normalized();
		float h = _m.TestHeadingRad;
		var bow = new Vector3(-Mathf.Sin(h), 0, -Mathf.Cos(h));
		return fwd.Dot(bow);
	}

	bool Round44Stage(int f)
	{
		var root = GetTree().Root;
		var fl = _m.HudRef.Flight;
		if (f == 0) { _m.TestMenu("new:fighter"); return false; }
		if (f == 20) { _m.TestStopShip(); _m.TestOpenCommand(); return false; }
		if (f == 26) { PressButton(_m.HudRef, "Rückansicht", true); return false; }
		if (f == 28) { PressButton(_m.HudRef, "Rückansicht", false); return false; }
		if (f == 32)
		{
			Check(_m.TestCamMode == CameraMode.Chase, "R44: Tipp auf „Rückansicht“ schaltet den Modus");
			Check(_m.HudRef.Command.TestHeightRowVisible, "R44: Zeile „Höhe − / +“ sichtbar");
			var up = _m.HudRef.Command.TestHeightUp;
			_r44Height = _m.TestCamChase.Height;
			_r44Tap = up?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
			Tap(_r44Tap, true);
			return false;
		}
		if (f == 34) { Tap(_r44Tap, false); return false; }
		if (f == 38)
		{
			var now = _m.TestCamChase;
			Check(Mathf.Abs(now.Height - (_r44Height + OrbitCamera.ChaseHeightStep)) < 0.01f, $"R44: „Höhe +“ {_r44Height:0} → {now.Height:0}");
			var file = SpaceAdventure.Game.CameraSettings.ReadFile();
			Check(file.Mode == CameraMode.Chase && Mathf.Abs(file.ChaseHeight - now.Height) < 0.01f, $"R44: Einstellungsdatei enthält {file}");
			var c2 = new OrbitCamera(); file.ApplyTo(c2);
			Check(c2.Mode == CameraMode.Chase && Mathf.Abs(c2.ChaseHeight - now.Height) < 0.01f && Mathf.Abs(c2.ChaseBack - now.Back) < 0.01f, "R44: neu geladene Einstellungen = gleicher Zustand");
			_m.TestCloseCommand();
			_r44Tap = root.GetFinalTransform() * fl.TestTurnLeftRect.GetCenter();
			PushWindow(_r44Tap, true);   // ◀ halten
			return false;
		}
		if (f == 110)
		{
			Check(fl.TurnLeftHeld, "R44: ◀ gehalten");
			PushWindow(_r44Tap, false);
			return false;
		}
		if (f == 200)
		{
			float dot = R44BehindDot();
			Check(dot > 0.95f, $"R44: Rückansicht nach dem Wenden hinter dem Bug (Skalarprodukt {dot:0.000}, Kurs {_m.TestHeadingDeg:0}°)");
			var u = _m.Session.U;
			var st = u.Stations.Values.Where(s => s.Sector == u.PlayerSector && s.Dockable).OrderBy(s => s.Id.Value).First();
			_m.TestDockAt(st.Id);
			return false;
		}
		if (f == 230) { _m.TestUndock(); return false; }
		if (f == 330)
		{
			Check(_m.TestCamMode == CameraMode.Chase, "R44: Andocken/Abdocken behält die Rückansicht");
			float dot = R44BehindDot();
			Check(dot > 0.95f, $"R44: nach dem Abdocken hinter dem Bug (Skalarprodukt {dot:0.000})");
			_m.TestMenu("new:trader");
			return false;
		}
		if (f == 360)
		{
			Check(_m.TestCamMode == CameraMode.Chase, "R44: „Neues Spiel“ behält die Rückansicht");
			_m.TestCameraAction("cam_reset");
			var now = _m.TestCamChase;
			Check(Mathf.Abs(now.Height - OrbitCamera.DefaultChaseHeight) < 0.01f && Mathf.Abs(now.Back - OrbitCamera.DefaultChaseBack) < 0.01f, "R44: „Zurücksetzen“ stellt Höhe 45 / Abstand 35 her");
			_m.TestCameraAction("cam_free");
			Check(SpaceSim.Core.Flight.CameraPrefs.Of(new OrbitCamera()).Mode == CameraMode.Free && SpaceAdventure.Game.CameraSettings.ReadFile().Mode == CameraMode.Free, "R44: zurück auf freie Kamera gespeichert");
			Log($"R44: Rückansicht geprüft (Höhe {now.Height:0}, Abstand {now.Back:0})");
			return true;
		}
		return false;
	}
}
