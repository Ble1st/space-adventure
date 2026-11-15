using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Runde 16 (dritter Handy-Test der Gefechtskamera): Bildschirmfotos im Handyformat aus dem echten Spielablauf. Startargument
/// <c>--phone-shots &lt;ordner&gt;</c> (nicht headless, braucht DISPLAY; Fenster 2340×1080). „Neues Spiel“ mit dem Startpaket „Gefechtstest“ über den
/// Menüweg (zufälliger Seed), die Warnung läuft in Echtzeit ab; dann Tipps auf „Nächster Gegner“ und „Dauerfeuer“ wie am Gerät. Aufnahmen: Warnphase,
/// Kampfbeginn, 10 s Kampf, nach dem Kampf; je Aufnahme die Diagnosezeile der Kamera in der Ausgabe.
/// Aufruf: godot-mono --path godot --resolution 2340x1080 -- --phone-shots /pfad/ordner
/// </summary>
public partial class PhoneShots : Node
{
	readonly Main _m;
	readonly string _dir;
	int _frame, _fightFrame = -1, _endFrame = -1;
	Vector2? _press;
	readonly List<string> _log = new();

	public PhoneShots(Main m, string dir) { _m = m; _dir = dir; }
	/// <summary>Runde 17: Bergbau-Variante (<c>--phone-mining</c>): Schürfer am Fels — Anker fliegen, verankert mit Abraum, Abriss durch Schub, Auto-Mining.</summary>
	public bool Mining { get; init; }
	int _rock;

	void PlaceAtRock()
	{
		var u = _m.Session.U;
		var rock = u.Sectors[u.PlayerSector].Asteroids.First(a => a.Id == _rock);
		var p = _m.Player.Position;
		var from = new Vec2(p.X, p.Z);
		var dir = (from - rock.Position).Length > 1e-3f ? (from - rock.Position).Normalized() : new Vec2(1, 0);
		_m.TestTeleportTo(rock.Position + dir * (rock.Radius + SpaceSim.Core.Simulation.Collision.ShipRadius + 0.2f));
		_m.TestFace(rock.Position);
		_m.TestSetSlider(0f);
	}

	void MiningFrame()
	{
		var u = _m.Session.U;
		switch (_frame)
		{
			case 30: _m.TestMenu("new:miner"); break;
			case 32: _m.TestUndock(); break;
			case 150:
			{
				var me = u.Ships[u.PlayerShip];
				var rock = u.Sectors[u.PlayerSector].Asteroids.Where(a => a.Minable && a.Radius > 4f && u.AsteroidClasses.ContainsKey(a.ClassKey))
					.OrderBy(a => Vec2.Distance(a.Position, me.Position)).First();
				rock.Ore = rock.MaxOre = Mathf.Max(rock.MaxOre, 400f);
				_rock = rock.Id;
				PlaceAtRock();
				break;
			}
			case 175: Capture("m01-anker-fliegt.png"); break;
			case 330: Capture("m02-verankert-abbau.png"); break;
			case 334: Capture("m02b-verankert-spaeter.png"); break;   // Runde 19: zweite Phase der Bohrerdrehung (4 Bilder später)
			case 340: _m.TestSetSlider(0.7f); break;
			case 350: Capture("m03-abriss-schub.png"); break;
			case 360: _m.TestSetSlider(0f); PlaceAtRock(); _m.TestStartMining(); break;
			case 900: Capture("m04-automatik.png"); break;
			case 960: Finish(); break;
		}
	}

	public override void _Ready()
	{
		SpaceAdventure.Ui.Kit.UiScale.PinnedFactor = 1f; SpaceAdventure.Ui.Kit.UiScale.Apply();   // Runde 33: Bilder im 1280er-Raster bei Faktor 1, unabhängig von der DPI des Rechners
		DirAccess.MakeDirRecursiveAbsolute(_dir);
		// Fenstergröße über --resolution (z. B. 1560x720 = Seitenverhältnis des Geräts 2340×1080, logisch 1560×720)
	}

	void Capture(string file)
	{
		string path = Path.Combine(_dir, file);
		string diag = _m.CameraDebugLine();
		RenderingServer.FramePostDraw += Shot;
		void Shot()
		{
			RenderingServer.FramePostDraw -= Shot;
			var img = GetViewport().GetTexture().GetImage();
			img.SavePng(path);
			string line = $"[phone-shots] {file} {img.GetWidth()}x{img.GetHeight()} · {diag}";
			GD.Print(line);
			_log.Add(line);
		}
	}

	static IEnumerable<Ui.Kit.KitButton> Buttons(Node root)
	{
		foreach (var c in root.GetChildren())
		{
			if (c is Ui.Kit.KitButton b) yield return b;
			foreach (var x in Buttons(c)) yield return x;
		}
	}

	void Tap(string label, bool down)
	{
		label = SelfTest.HudAlias(label);
		if (down) _press = Buttons(_m.HudRef.Flight).FirstOrDefault(b => b.Text == label && b.IsVisibleInTree())?.GetGlobalRect().GetCenter();
		if (_press is not { } at) { GD.Print($"[phone-shots] Taste „{label}“ nicht gefunden"); return; }
		var vp = GetViewport();
		if (Ui.Pointer.TouchMode) vp.PushInput(new InputEventScreenTouch { Index = 0, Position = at, Pressed = down }, true);
		else vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = at, GlobalPosition = at, Pressed = down, ButtonMask = down ? MouseButtonMask.Left : 0 }, true);
	}

	public override void _Process(double delta)
	{
		_frame++;
		if (Mining) { MiningFrame(); return; }
		var u = _m.Session.U;
		switch (_frame)
		{
			case 30: _m.TestMenu("new:combat"); return;   // Menüweg „Neues Spiel → Gefechtstest“ (BuildDefault ohne Seed)
			case 150: Capture("01-warnphase.png"); return;
		}
		if (_frame > 150 && _fightFrame < 0 && u.Fights.Of(u.PlayerShip) is not null) _fightFrame = _frame;
		if (_fightFrame > 0)
		{
			int f = _frame - _fightFrame;
			switch (f)
			{
				case 30: Capture("02-kampfbeginn.png"); break;
				case 60: Tap("Nächster Gegner", true); break;
				case 64: Tap("", false); break;
				case 90: Tap("Dauerfeuer", true); break;
				case 94: Tap("", false); break;
				case 120: Capture("03-nach-tipps.png"); break;
				case 630: Capture("04-kampf-10s.png"); break;
			}
			if (f > 630 && _endFrame < 0 && u.Fights.Of(u.PlayerShip) is null) _endFrame = _frame;
			if (_endFrame > 0 && _frame - _endFrame == 90) Capture("05-nach-kampf.png");
			if ((_endFrame > 0 && _frame - _endFrame == 120) || f == 3600) Finish();
		}
		if (_frame == 2400 && _fightFrame < 0) { GD.Print("[phone-shots] kein Kampf nach 40 s"); Finish(); }
	}

	void Finish()
	{
		File.WriteAllLines(Path.Combine(_dir, "diagnose.txt"), _log);
		GD.Print("[phone-shots] fertig");
		GetTree().Quit(0);
	}
}
