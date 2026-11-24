using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Ui;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 131 (Geräte-Eingabe, Handytest A15: Autopilot-Tipp wirkte nur jeder vierte, der Autopilot fiel nach 2–3 s mit „manuelle Steuerung“ aus und der
/// Regler stand auf 6 % ohne Berührung; „Menü“/„Karte“ oben rechts reagierten nur teilweise): Fenster 2340×1080, UI-Faktor 1,15, echte Ränder des A15.
/// Touch wird erzwungen (<see cref="Ui.Pointer.ForceTouch"/>); jedes Ereignis läuft über <see cref="Input.ParseInputEvent"/> — wie am Gerät mit Godots Maus-
/// Nachbildung aus Touch — in Fensterpunkten (= Gerätepunkte). Geprüft: ein Tipp = ein Umschalten (auch mit kleiner Fingerbewegung), ein abgebrochener
/// Tipp schaltet nicht, der Autopilot hält ≥ 10 s ohne Reglerbewegung trotz fremder Berührungen (Weltwisch über den Regler, verlorenes Loslassen am
/// Regler, Maus-Nachbildung), der Regler ändert sich nur durch einen Finger, der auf ihm aufsetzt, ein solcher bricht den Autopiloten bewusst ab;
/// „Karte“ und „Menü“ öffnen bei Tipps überall in ihrem gezeichneten Rechteck (auch oben, y = 70 Gerätepunkte) — mit den Laufzeiträndern („a“) und den dumpsys-Rändern („d“).
/// </summary>
public partial class SelfTest
{
	readonly List<(int At, Action Act)> _inScript = new();
	int _inScriptPos;
	bool _inHold;            // Haltephase: Autopilot muss an, Regler 0 bleiben
	int _inHoldFrames, _inHoldBad;
	string _inHoldWhy = "";
	int _inToggles;          // gezählte Umschaltungen des Autopiloten
	bool _inLastAp;

	static readonly UiInsets InsetsA15Runtime = new(80f * 720f / 1080f, 0f, 0f, 0f);
	static readonly UiInsets InsetsA15Dumpsys = new(80f * 720f / 1080f, 84f * 720f / 1080f, 135f * 720f / 1080f, 0f);

	/// <summary>Gezeichnetes Rechteck eines Steuerelements in Fensterpunkten (= Gerätepunkte am Handy).</summary>
	static Rect2 DevRect(Control c)
	{
		var t = c.GetScreenTransform();
		var a = t * Vector2.Zero; var b = t * c.Size;
		float k = WinScale(c);
		return new Rect2(a * k, (b - a) * k);
	}

	/// <summary>Streckfaktor Fenster ↔ logische Wurzel (canvas_items/expand: Fensterhöhe ÷ 720 logische Punkte).</summary>
	static float WinScale(Node n) => n.GetWindow().Size.Y / n.GetViewport().GetVisibleRect().Size.Y;

	static void Ev(InputEvent e) => Input.ParseInputEvent(e);
	static void TDown(Vector2 p, int i = 0) => Ev(new InputEventScreenTouch { Index = i, Position = p, Pressed = true });
	static void TUp(Vector2 p, int i = 0, bool canceled = false) => Ev(new InputEventScreenTouch { Index = i, Position = p, Pressed = false, Canceled = canceled });
	static void TDrag(Vector2 p, Vector2 rel, int i = 0) => Ev(new InputEventScreenDrag { Index = i, Position = p, Relative = rel });

	KitButton? FlightKit(string label) => FindKit(_m.HudRef.Flight, label);
	KitButton? FlightKitByItem(HudItem item) => _m.HudRef.Flight.TestItem(item) as KitButton;

	/// <summary>Kurzer Tipp an <paramref name="p"/> (Druck jetzt, Loslassen 4 Bilder später; optional mit 3 px Bewegung dazwischen).</summary>
	void InTap(int at, Func<Vector2> p, bool wiggle = false, bool canceled = false)
	{
		Vector2 pos = default;
		_inScript.Add((at, () => { pos = p(); TDown(pos); }));
		if (wiggle) _inScript.Add((at + 2, () => TDrag(pos + new Vector2(3, 2), new Vector2(3, 2))));
		_inScript.Add((at + 4, () => TUp(wiggle ? pos + new Vector2(3, 2) : pos, canceled: canceled)));
	}

	void InCheckAp(int at, bool want, string what) => _inScript.Add((at, () => Check(_m.TestAutopilotOn == want, $"Eingabe: {what} → Autopilot {(want ? "an" : "aus")} (ist {(_m.TestAutopilotOn ? "an" : "aus")}, Regler {_m.HudRef.Flight.SliderValue:0.000})")));

	bool InputStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		if (f == 1)
		{
			Ui.Pointer.ForceTouch = true;
			GetWindow().Size = new Vector2I(2340, 1080);
			UiScale.TestInsets = InsetsA15Runtime;
			UiScale.TestForce = 1.15f;
			UiScale.Apply();
			_m.TestMenu("new:trader");
			return false;
		}
		if (f == 40)
		{
			if (_m.TestDocked) _m.TestUndock();
			return false;
		}
		if (f == 80)
		{
			_m.TestStopShip();
			hud.Flight.SetSlider(0f);
			var player = u.Ships[u.PlayerShip];
			var far = u.Stations.Values.Where(s => s.Sector != u.PlayerSector && u.FindRoute(u.PlayerSector, s.Sector) is { Count: > 1 }).OrderBy(s => s.Id.Value).First();   // anderer Sektor: Flug über ein Tor (wie am Gerät „Sektor Gamma“), dockt in 11 s nicht an
			_m.TestSelectStation(far.Id);
			Check(Math.Abs(UiScale.Factor - 1.15f) < 0.001f && Math.Abs(hud.Flight.Scale.X - 1.15f) < 0.02f, $"Eingabe: Flug-Schicht bei 1,15 (Faktor {UiScale.Factor:0.00}, Schicht {hud.Flight.Scale.X:0.00})");
			BuildInputScript();
			_inScriptPos = 0;
			_inLastAp = _m.TestAutopilotOn;
			return false;
		}
		if (f < 80) return false;
		int rel = f - 80;
		// Umschaltungen des Autopiloten zählen (jede Änderung des Zustands)
		if (_m.TestAutopilotOn != _inLastAp) { _inToggles++; _inLastAp = _m.TestAutopilotOn; }
		while (_inScriptPos < _inScript.Count && _inScript[_inScriptPos].At <= rel) { _inScript[_inScriptPos].Act(); _inScriptPos++; }
		if (_inHold)
		{
			_inHoldFrames++;
			if (!_m.TestAutopilotOn || hud.Flight.SliderValue != 0f)
			{
				if (_inHoldBad++ == 0) _inHoldWhy = $"Bild {_inHoldFrames}: Autopilot {(_m.TestAutopilotOn ? "an" : "aus")}, Regler {hud.Flight.SliderValue:0.000}";
			}
		}
		return _inScriptPos >= _inScript.Count;
	}

	void BuildInputScript()
	{
		_inScript.Clear();
		var hud = _m.HudRef;
		var fl = hud.Flight;
		Func<Vector2> apCenter = () => DevRect(FlightKitByItem(HudItem.Autopilot)!).GetCenter();
		Func<float, float, Vector2> apAt = (fx, fy) => { var r = DevRect(FlightKitByItem(HudItem.Autopilot)!); return r.Position + new Vector2(r.Size.X * fx, r.Size.Y * fy); };
		Func<float, Vector2> sliderAt = v => fl.TestSliderScreenPoint(v) * WinScale(fl);
		int t = 0;

		_inScript.Add((t, () =>
		{
			var ap = DevRect(FlightKitByItem(HudItem.Autopilot)!);
			var map = DevRect(FlightKitByItem(HudItem.Command)!);
			var menu = DevRect(FlightKitByItem(HudItem.Hamburger)!);
			var sl = DevRect((Control)fl.TestItem(HudItem.Slider)!);
			GD.Print($"[eingabe] Geräterechtecke (Fenster {GetWindow().Size}, Faktor {UiScale.Factor:0.00}): Autopilot {ap}, Karte {map}, Menü {menu}, Regler {sl}");
		}));

		// ── 1: ein Tipp = ein Umschalten (Mitte, Ränder innen, mit kleiner Bewegung) ──
		t = 10;
		var points = new (float, float, bool)[] { (0.5f, 0.5f, false), (0.1f, 0.15f, false), (0.9f, 0.85f, false), (0.5f, 0.5f, true), (0.2f, 0.8f, true), (0.85f, 0.2f, false) };
		bool want = false;
		foreach (var (fx, fy, wig) in points)
		{
			float fx0 = fx, fy0 = fy;
			InTap(t, () => apAt(fx0, fy0), wiggle: wig);
			want = !want;
			InCheckAp(t + 16, want, $"Tipp auf „Autopilot“ ({fx0:0.00}, {fy0:0.00}{(wig ? ", 3 px Bewegung" : "")})");
			t += 30;
		}
		// ── 2: abgebrochener Tipp (System übernimmt die Geste) schaltet nicht ──
		InTap(t, apCenter, canceled: true);
		InCheckAp(t + 16, want, "abgebrochener Tipp auf „Autopilot“");
		t += 30;
		// gerade Zahl Tipps → aus; jetzt bewusst ein
		bool needUp = false;
		_inScript.Add((t, () => { needUp = !_m.TestAutopilotOn; if (needUp) TDown(apCenter()); }));
		_inScript.Add((t + 4, () => { if (needUp) TUp(apCenter()); }));
		InCheckAp(t + 16, true, "Autopilot einschalten");
		t += 30;

		// ── 3: Autopilot hält ≥ 10 s, der Regler bleibt 0 — trotz fremder Berührungen ──
		_inScript.Add((t, () => { _inHold = true; _inHoldFrames = 0; _inHoldBad = 0; _inHoldWhy = ""; }));
		// a) Tipp in die Welt
		InTap(t + 20, () => new Vector2(1170, 760));
		// b) Wisch in der Welt, der über den Regler streicht und dort endet
		_inScript.Add((t + 60, () => TDown(new Vector2(700, 700))));
		for (int k = 1; k <= 10; k++) { int kk = k; _inScript.Add((t + 60 + k * 2, () => { var a = new Vector2(700, 700); var b = sliderAt(0.4f); var p = a.Lerp(b, kk / 10f); TDrag(p, (b - a) / 10f); })); }
		_inScript.Add((t + 84, () => TUp(sliderAt(0.4f))));
		// c) verlorenes Loslassen am Regler (Android verwirft ein inkonsistentes Ereignis): Druck auf den Regler an seinem Nullpunkt, kein Loslassen —
		//    danach ein Tipp auf „Karte“-freie Welt und ein Weltwisch über den Regler mit demselben Fingerindex
		_inScript.Add((t + 120, () => TDown(sliderAt(0f) + new Vector2(0, 30))));   // unter dem Nullpunkt (STOPP) — Wert bleibt 0
		_inScript.Add((t + 140, () => TDown(new Vector2(1500, 800))));              // neuer Druck desselben Index in der Welt (Loslassen davor verloren)
		for (int k = 1; k <= 8; k++) { int kk = k; _inScript.Add((t + 140 + k * 2, () => { var a = new Vector2(1500, 800); var b = sliderAt(0.06f); TDrag(a.Lerp(b, kk / 8f), (b - a) / 8f); })); }
		_inScript.Add((t + 160, () => TUp(sliderAt(0.06f))));
		// d) nur Maus-Ereignisse (wie Godots Nachbildung) über dem Regler
		_inScript.Add((t + 190, () => Ev(new InputEventMouseMotion { Position = sliderAt(0.5f), GlobalPosition = sliderAt(0.5f), ButtonMask = MouseButtonMask.Left })));
		_inScript.Add((t + 192, () => Ev(new InputEventMouseButton { Position = sliderAt(0.5f), GlobalPosition = sliderAt(0.5f), ButtonIndex = MouseButton.Left, Pressed = false })));
		// e) zweiter Finger (Index 1) tippt die Welt, während Index 0 frei ist
		_inScript.Add((t + 220, () => TDown(new Vector2(1300, 600), 1)));
		_inScript.Add((t + 226, () => TUp(new Vector2(1300, 600), 1)));
		t += 660;   // 11 s bei 60 Bildern/s
		_inScript.Add((t, () =>
		{
			_inHold = false;
			Check(_inHoldBad == 0 && _inHoldFrames >= 600, $"Eingabe: Autopilot hält {_inHoldFrames / 60f:0.0} s an, Regler bleibt 0 trotz fremder Berührungen{(_inHoldBad > 0 ? $" — {_inHoldBad} schlechte Bilder, zuerst {_inHoldWhy}" : "")}");
		}));

		// ── 4: bewusster Zug am Regler bricht ab und setzt den Wert ──
		t += 10;
		_inScript.Add((t, () => TDown(sliderAt(0.5f))));
		_inScript.Add((t + 4, () => TDrag(sliderAt(0.6f), sliderAt(0.6f) - sliderAt(0.5f))));
		_inScript.Add((t + 8, () => TUp(sliderAt(0.6f))));
		_inScript.Add((t + 14, () =>
		{
			Check(!_m.TestAutopilotOn, "Eingabe: Finger auf dem Regler bricht den Autopiloten ab");
			Check(Math.Abs(fl.SliderValue - 0.6f) < 0.04f, $"Eingabe: Regler folgt dem Finger (0,60 ↔ {fl.SliderValue:0.00})");
			Check(hud.TestToastText.Contains("manuelle Steuerung"), $"Eingabe: Hinweis „Autopilot aus (manuelle Steuerung)“ (ist „{hud.TestToastText}“)");
		}));
		// zurück auf STOPP über den Regler
		_inScript.Add((t + 20, () => TDown(sliderAt(0.6f))));
		_inScript.Add((t + 24, () => TDrag(sliderAt(0f) + new Vector2(0, 20), new Vector2(0, 100))));
		_inScript.Add((t + 28, () => TUp(sliderAt(0f) + new Vector2(0, 20))));
		_inScript.Add((t + 34, () => Check(fl.SliderValue == 0f, $"Eingabe: Regler zurück auf STOPP ({fl.SliderValue:0.00})")));
		t += 50;

		// ── 5: „Karte“ und „Menü“ öffnen überall in ihrem gezeichneten Rechteck — Laufzeitränder und dumpsys-Ränder ──
		foreach (var ins in new[] { InsetsA15Runtime, InsetsA15Dumpsys })
		{
			var insets = ins;
			_inScript.Add((t, () => { UiScale.TestInsets = insets; UiScale.Apply(); }));
			t += 20;
			var spots = new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.1f), (0.1f, 0.1f), (0.9f, 0.9f), (0.5f, 0.9f) };
			foreach (var item in new[] { HudItem.Command, HudItem.Hamburger })
			{
				var it = item;
				foreach (var (fx, fy) in spots)
				{
					float fx0 = fx, fy0 = fy;
					string where = "";
					InTap(t, () => { var r = DevRect(FlightKitByItem(it)!); var p = r.Position + new Vector2(r.Size.X * fx0, r.Size.Y * fy0); where = $"{p.X:0},{p.Y:0} in {r}"; return p; });
					_inScript.Add((t + 14, () =>
					{
						bool open = it == HudItem.Command ? hud.Screens.Current == hud.CcFleet : hud.Screens.Current == hud.Command;
						Check(open, $"Eingabe ({(insets.Top > 0 ? "d" : "a")}): Tipp auf „{(it == HudItem.Command ? "Befehle" : "Menü")}“ bei {where} öffnet");
						hud.Map.Visible = false; _m.TestMenu("close"); _m.TestCloseCommand();
					}));
					t += 30;
				}
			}
			// Gerätepunkt y = 70 (Handytest) — liegt er im gezeichneten Rechteck, muss der Tipp öffnen
			foreach (var item in new[] { HudItem.Command, HudItem.Hamburger })
			{
				var it = item;
				InTap(t, () => { var r = DevRect(FlightKitByItem(it)!); return new Vector2(r.GetCenter().X, Math.Max(70f, r.Position.Y + 4f)); });
				_inScript.Add((t + 14, () =>
				{
					bool open = it == HudItem.Command ? hud.Screens.Current == hud.CcFleet : hud.Screens.Current == hud.Command;
					var r = DevRect(FlightKitByItem(it)!);
					Check(open, $"Eingabe ({(insets.Top > 0 ? "d" : "a")}): „{(it == HudItem.Command ? "Befehle" : "Menü")}“ öffnet bei y = {Math.Max(70f, r.Position.Y + 4f):0} (Rechteck {r})");
					hud.Map.Visible = false; _m.TestMenu("close"); _m.TestCloseCommand();
				}));
				t += 30;
			}
		}
		_inScript.Add((t, () => { UiScale.TestInsets = InsetsA15Runtime; UiScale.Apply(); }));
		t += 10;

		t = AdbPattern(t);

		// ── Aufräumen ──
		_inScript.Add((t, () =>
		{
			GD.Print($"[eingabe] Umschaltungen des Autopiloten gezählt: {_inToggles}");
			Ui.Pointer.ForceTouch = null;
			UiScale.TestInsets = null; UiScale.TestForce = null; UiScale.Apply();
			GetWindow().Size = new Vector2I(1280, 720);
			_m.TestStopShip();
		}));
		t += 20;

		// ── 6: Speicher über Sektorwechsel (Handytest: 775 → 838 MB PSS in 22 min, 5 Sektoren) — drei Runden durch alle Sektoren (alle Himmelsarten) ──
		_inScript.Add((t, () => { if (_m.TestDocked) _m.TestUndock(); _inSkySectors = _m.Session.U.Sectors.Values.OrderBy(x => x.Id.Value).Select(x => x.Id).ToArray(); }));
		t += 10;
		for (int round = 1; round <= 3; round++)
		{
			for (int i = 0; i < 11; i++)
			{
				int ii = i;
				_inScript.Add((t, () => { if (ii < _inSkySectors.Length) _m.TestEnterSector(_inSkySectors[ii]); }));
				_inScript.Add((t + 5, () => { if (ii < _inSkySectors.Length) { _inSkyAliveMax = Math.Max(_inSkyAliveMax, SkyTexturesAlive()); _inVideoMax = Math.Max(_inVideoMax, RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576.0); } }));
				t += 10;
			}
			int r = round;
			_inScript.Add((t, () => { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }));
			_inScript.Add((t + 10, () => MemoryLine(r)));
			t += 20;
		}
		_inScript.Add((t, () =>
		{
			Check(_inSkyAliveMax <= 2, $"Speicher: höchstens zwei Himmelstexturen gleichzeitig am Leben (gesehen {_inSkyAliveMax}; erlaubt: der neue und — bis zur Freigabe im selben Bild — der alte)");
			Check(SkyTexturesAlive() == 1, $"Speicher: nach den Runden genau eine Himmelstextur im Speicher (ist {SkyTexturesAlive()})");
			if (_inMem.Count == 3)
			{
				var (o2, r2, s2, v2) = _inMem[1]; var (o3, r3, s3, v3) = _inMem[2];
				Check(r3 <= r2 + 2, $"Speicher: Ressourcen wachsen in Runde 3 nicht ({r2} → {r3})");
				Check(o3 <= o2 + 50, $"Speicher: Objekte wachsen in Runde 3 nicht ({o2} → {o3})");
				Check(s3 <= s2 + 4.0, $"Speicher: statischer Speicher wächst in Runde 3 höchstens 4 MB ({s2:0.0} → {s3:0.0} MB)");
			}
		}));
		_inScript.Add((t + 10, () => { }));
	}

	SectorId[] _inSkySectors = Array.Empty<SectorId>();
	int _inSkyAliveMax;
	double _inVideoMax;   // höchster Videospeicher während der Runden (ohne Speicherbereinigung)
	readonly List<(double Objects, double Resources, double StaticMb, double VideoMb)> _inMem = new();

	/// <summary>Wie viele Himmelstexturen leben gerade (Godots Ladecache hält sie nur schwach — „gecacht“ = noch irgendwo referenziert).</summary>
	static int SkyTexturesAlive() => World.SkyboxView.Manifest.Values.Select(e => e.File).Distinct().Count(f => ResourceLoader.HasCached("res://assets/skyboxes/" + f));

	void MemoryLine(int round)
	{
		double objects = Performance.GetMonitor(Performance.Monitor.ObjectCount), res = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
		double nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
		double stat = Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0;
		double video = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576.0;
		double tex = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) / 1048576.0;
		_inMem.Add((objects, res, stat, video));
		GD.Print($"[selftest] Speicher Sektorwechsel Runde {round} (11 Sektoren): Knoten {nodes}, Objekte {objects}, verwaist {orphans}, Ressourcen {res}, " +
			$"statisch {stat:0.0} MB, Video {video:0.0} MB (Texturen {tex:0.0} MB), verwaltet {GC.GetTotalMemory(false) / 1048576.0:0.0} MB, Himmelstexturen am Leben {SkyTexturesAlive()} (höchstens {_inSkyAliveMax} während der Runden, Video höchstens {_inVideoMax:0.0} MB)");
	}
}
