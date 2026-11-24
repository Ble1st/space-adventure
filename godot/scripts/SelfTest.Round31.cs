using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Ui;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 108 (Runde 31, UI-ANPASSUNG.md Etappe 1; Runde 33: Faktor je Schicht — die Flug-Schicht trägt den Faktor, die Wurzel bleibt 1): UI-Größe. (1) Voreinstellung „normal“ ergibt heute Faktor 1 (starre 1280×720-Bildschirme,
/// Obergrenze 1) — pixelgleich zum bisherigen Stand; (2) „groß“ wird in der Test-Einstellungsdatei gespeichert und bleibt bei Faktor 1; (3) erzwungener
/// Faktor 1,15 (Prüf-Obergrenze, angenommene A15-DPI): Wurzelfenster mit content_scale_factor 1,15, sichtbare Fläche entsprechend kleiner, Stationswahl
/// per Tipp in Fensterkoordinaten (durch die Streckung der Ansicht) und Kamera Bild↔Welt stimmen; (4) Faktor 1,3 ebenso; (5) zurück auf 1.
/// </summary>
public partial class SelfTest
{
	StationId _r31Station;
	Vector2 _r31Window;
	float _r31Factor;

	bool Round31Stage(int f)
	{
		var u = _m.Session.U;
		var root = GetTree().Root;
		// Runde 44: nach der Vorstufe (Handel an einer Station) ist das Schiff angedockt — erst abdocken, dann beginnt die Stufe von vorn (angedockt wirkte das
		// Versetzen nicht; im kleinen Sektor lag die Station trotzdem im Bild)
		if (f == 1 && _m.TestDocked) { _m.TestCloseCommand(); _m.TestUndock(); _stageStart += 120; return false; }
		switch (f)
		{
			case 1:
			{
				_m.TestCloseCommand();
				if (root.Size.X < 640) root.Size = new Vector2I(1280, 720);   // Start mit --selftest-from=108: Fenster noch 64 × 64
				// neben eine Station setzen und die Kamera weiter wegzoomen: die Station liegt dann im Bild (Tipp-Prüfung unten)
				_m.TestTeleportNear(u.StationsById().First(s => s.Sector == u.PlayerSector && s.Dockable).Id);
				for (int i = 0; i < 4; i++) _m.TestCommand("zoom_out");
				Check(UiScale.Size == UiSize.Normal && UiScale.Factor == 1f && root.ContentScaleFactor == 1f,
					$"UI-Größe: Voreinstellung normal mit Faktor 1 ({UiScale.Size}, {UiScale.Factor}, Wurzel {root.ContentScaleFactor})");
				UiScale.Set(UiSize.Large);
				var cfg = new ConfigFile();
				Check(cfg.Load(Audio.AudioSettings.Path) == Error.Ok && (int)cfg.GetValue("ui", "size", -1) == (int)UiSize.Large && Audio.AudioSettings.Path.Contains("settings-test"),
					$"UI-Größe gespeichert in der Test-Einstellungsdatei ({Audio.AudioSettings.Path})");
				// Runde 33: Faktor je Schicht — die Wurzel bleibt bei 1, die Flug-Schicht trägt den Faktor der Stufe
				Check(root.ContentScaleFactor == 1f && Mathf.Abs(_m.HudRef.Flight.Scale.X - UiScale.Factor) < 0.001f, $"UI-Größe groß: Wurzel 1, Flug-Schicht {_m.HudRef.Flight.Scale.X} = Faktor {UiScale.Factor} (Ziel {UiScale.Wanted:0.00})");
				UiScale.Set(UiSize.Normal);
				return false;
			}
			case 2:
			{
				// Runde 32: der Text nennt die echte Tastengröße der Stufe (nicht „mindestens 7 mm“) und dass die Stufen erst später wirken
				UiScale.TestPpi = 396f;
				UiScale.Set(UiSize.Small);
				float mm = UiLayout.Millimetres(UiLayout.BaseTapPx, root.Size.X, root.Size.Y, 1f, 396f);
				string d1 = UiScale.Describe();
				UiScale.Set(UiSize.Large);
				string d2 = UiScale.Describe();
				// Runde 33: „etwa“ (DPI auf Android = Dichteklasse) und dass Dock/Kommandozentrale vorerst bei 1 bleiben
				Check(d1.Contains($"etwa {mm:0.0} mm") && !d1.Contains("mindestens 7 mm") && d2.Contains("Kommandozentrale "),
					$"UI-Größe: Text nennt echte Tastengröße und den späteren Ausbau („{d1}“ / „{d2}“)");
				UiScale.TestPpi = null;
				UiScale.Set(UiSize.Normal);
				return false;
			}
			case 30:
				// Runde 44: der Tipp bei Faktor 1,15 wählte eine Station, der Autopilot flog hin und dockte an — erst abdocken, dann versetzen (Fall 33)
				_m.TestStopAutomation();
				if (_m.TestDocked) _m.TestUndock();
				return false;
			case 3: case 33:
			{
				_r31Factor = f == 3 ? 1.15f : 1.3f;
				// Runde 44: in den vergrößerten Sektoren liegt am Startpunkt keine Station mehr im Bild — das Schiff wird 60 m vor die nächste andockbare
				// Station gesetzt (die Stufe prüft Tipp und Umrechnung, nicht den Startpunkt)
				{
					var me = u.Ships[u.PlayerShip];
					var near = u.StationsById().Where(s => s.Sector == u.PlayerSector && s.Dockable).OrderBy(s => Vec2.Distance(s.Position, me.Position)).First();
					_m.TestStopAutomation();   // der Tipp bei Faktor 1,15 setzte einen Autopiloten zur Station
					_m.TestTeleportTo(near.Position + new Vec2(near.Radius + 45f, 0f));   // seitlich: die Station liegt in der Bildmitte-Zeile (oben deckt das Kompakt-HUD)
				}
				UiScale.TestPpi = 396f;
				UiScale.TestCap = _r31Factor;
				UiScale.Set(f == 3 ? UiSize.Normal : UiSize.Large);
				return false;
			}
			case 12: case 42:
			{
				// Runde 33: Faktor je Schicht — Flug-Schicht skaliert, Wurzel bei 1, logische Fläche der Schicht = sichtbare Fläche ÷ Faktor
				var fl = _m.HudRef.Flight;
				Check(Mathf.Abs(UiScale.Factor - _r31Factor) < 0.001f && fl.Scale.X <= _r31Factor + 0.001f && fl.Scale.X >= _r31Factor - 0.05f && root.ContentScaleFactor == 1f,   // UI-Umbau: die Flug-Schicht passt den Faktor an (FitFactor)
					$"UI-Größe: erzwungener Faktor {_r31Factor} auf der Flug-Schicht ({UiScale.Factor}, Schicht {fl.Scale.X}, Wurzel {root.ContentScaleFactor})");
				var win = root.Size;
				var vis = GetViewport().GetVisibleRect().Size;
				float stretch = UiLayout.StretchScale(win.X, win.Y);
				Check(Mathf.Abs(fl.Size.Y - win.Y / (stretch * fl.Scale.X)) < 2f && fl.Layout is { Compact: true },
					$"UI-Größe: Fläche der Flug-Schicht {fl.Size} bei Fenster {win} und Faktor {_r31Factor} (kompakt {fl.Layout?.Compact})");
				// Kamera: Bild ↔ Welt um das eigene Schiff
				var ship = _m.Player.GlobalPosition;
				var sp = _m.TestCam.UnprojectPosition(ship);
				var origin = _m.TestCam.ProjectRayOrigin(sp);
				var dir = _m.TestCam.ProjectRayNormal(sp);
				float t = Mathf.Abs(dir.Y) > 1e-4f ? (ship.Y - origin.Y) / dir.Y : 0f;
				var back = origin + dir * t;
				Check(new Rect2(Vector2.Zero, vis).HasPoint(sp) && back.DistanceTo(ship) < 0.5f,
					$"UI-Größe {_r31Factor}: Schiff auf dem Bild {sp} (Fläche {vis}), Bild→Welt {back} statt {ship}");
				// Tipp auf eine Station: nächste sichtbare Station im Sektor, in Fensterkoordinaten durch die Streckung
				_m.TestClearTarget();
				var st = u.StationsById().Where(s => s.Sector == u.PlayerSector && s.Dockable).Select(s => (s, p: BodyPoint(s, ship)))
					.Where(x => !_m.TestCam.IsPositionBehind(x.p) && new Rect2(vis * 0.1f, vis * 0.8f).HasPoint(_m.TestCam.UnprojectPosition(x.p))
						&& !_m.HudRef.Flight.OccupiedRects().Any(r => r.Grow(8f).HasPoint(_m.TestCam.UnprojectPosition(x.p))))
					.OrderBy(x => x.p.DistanceTo(ship)).Select(x => x.s).FirstOrDefault();
				if (st is null)
				{
					Check(false, $"UI-Größe {_r31Factor}: keine Station frei im Bild (Schiff {ship}; Stationen {string.Join(", ", u.StationsById().Where(s => s.Sector == u.PlayerSector).Select(s => $"{s.Name} {_m.TestCam.UnprojectPosition(new Vector3(s.Position.X, 0f, s.Position.Y))}"))})");
					return true;
				}
				_r31Station = st.Id;
				var logical = _m.TestCam.UnprojectPosition(BodyPoint(st, ship));
				_r31Window = root.GetFinalTransform() * logical;
				Check(Mathf.Abs(_r31Window.X / logical.X - stretch) < 0.02f || logical.X < 1f,
					$"UI-Größe: Fensterkoordinate {_r31Window} = Bild {logical} × {stretch:0.000} (Wurzel ungeskaliert)");
				PushWindow(_r31Window, true);
				return false;
			}
			case 14: case 44: PushWindow(_r31Window, false); return false;
			case 16: case 46:
				Check(_m.TestTargetStation == _r31Station.Value,
					$"UI-Größe {_r31Factor}: Tipp in Fensterkoordinaten wählt die Station (gewählt {_m.TestTargetStation}, erwartet {_r31Station.Value})");
				_m.TestStopAutomation();   // Runde 44: der Tipp startet den Autopiloten zur Station — nicht hinfliegen und andocken (der nächste Faktor versetzt das Schiff)
				return false;
			case 60:
			{
				UiScale.TestCap = null; UiScale.TestPpi = null;
				UiScale.Set(UiSize.Normal);
				_m.TestCommand("cam_reset");
				return false;
			}
			case 62:
			{
				Check(UiScale.Factor == 1f && root.ContentScaleFactor == 1f, $"UI-Größe: zurück auf Faktor 1 ({root.ContentScaleFactor})");
				return true;
			}
		}
		return false;
	}

	/// <summary>Punkt im Stationskörper auf der Seite zum Schiff (halber Radius) — die Mitte großer Stationen liegt bei naher Kamera oft außerhalb des Bildes.</summary>
	static Vector3 BodyPoint(Station s, Vector3 ship)
	{
		var c = new Vector3(s.Position.X, 0f, s.Position.Y);
		var d = new Vector3(ship.X, 0f, ship.Z) - c;
		return d.LengthSquared() < 1e-4f ? c : c + d.Normalized() * s.Radius * 0.5f;
	}

	/// <summary>Tipp in Fensterkoordinaten — die Ansicht rechnet ihn wie am Gerät durch Streckung und UI-Faktor in Bildkoordinaten um.</summary>
	void PushWindow(Vector2 at, bool down)
	{
		if (Ui.Pointer.TouchMode) GetViewport().PushInput(new InputEventScreenTouch { Index = 0, Position = at, Pressed = down }, false);
		else GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = at, GlobalPosition = at, Pressed = down, ButtonMask = down ? MouseButtonMask.Left : 0 }, false);
	}
}
