using System;
using System.Linq;
using Godot;
using SpaceSim.Core.Ui;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 131, Teil 7 (zweiter Handytest A15 nach dem Eingabe-Fix, Tipps per <c>adb shell input tap</c>): Autopilot schaltete sich in 5 von 5 Versuchen
/// nach ~1 s mit „manuelle Steuerung“ ab, „Menü“ öffnete in 0 von 15 Tipps, die Zurück-Taste öffnete es im Flug auch nicht; logcat „dropping inconsistent event“.
/// Nachgestellt werden die Ereignisfolgen, die ein adb-Tipp am Gerät erzeugt — Drücken und Loslassen im selben Bild bzw. 1 Bild später (mit Godots
/// Maus-Nachbildung, die <see cref="Input.ParseInputEvent"/> selbst erzeugt) — und die Folge nach einem verlorenen Loslassen: Godots Android-Eingabe gibt beim
/// nächsten ACTION_DOWN zuerst das Loslassen des alten Fingers an der alten Stelle und dann das neue Drücken, beides im selben Bild. Die Zurück-Taste kommt als
/// Benachrichtigung. Geprüft: ein Tipp öffnet/schaltet, das Menü bleibt nach dem Öffnen offen, der Autopilot hält 10 s ohne Reglerbewegung.
/// </summary>
public partial class SelfTest
{
	Vector2 _adbLast;

	/// <summary>adb-Tipp: Drücken und Loslassen im selben Bild (<paramref name="gap"/> = 0) oder <paramref name="gap"/> Bilder später.</summary>
	void AdbTap(int at, Func<Vector2> p, int gap)
	{
		Vector2 pos = default;
		if (gap == 0) { _inScript.Add((at, () => { pos = p(); TDown(pos); TUp(pos); _adbLast = pos; })); return; }
		_inScript.Add((at, () => { pos = p(); TDown(pos); _adbLast = pos; }));
		_inScript.Add((at + gap, () => TUp(pos)));
	}

	/// <summary>Tipp nach verlorenem Loslassen: Loslassen des alten Fingers an der alten Stelle, dann das neue Drücken — beides in einem Bild, kein Loslassen.</summary>
	void AdbLostUpTap(int at, Func<Vector2> p) => _inScript.Add((at, () => { var pos = p(); TUp(_adbLast); TDown(pos); _adbLast = pos; }));

	void AdbBack(int at) => _inScript.Add((at, () => GetTree().Root.PropagateNotification((int)Node.NotificationWMGoBackRequest)));

	int AdbPattern(int t)
	{
		var hud = _m.HudRef;
		var fl = hud.Flight;
		Func<Vector2> ap = () => DevRect(FlightKitByItem(HudItem.Autopilot)!).GetCenter();
		Func<Vector2> menu = () => { var r = DevRect(FlightKitByItem(HudItem.Hamburger)!); return new Vector2(r.GetCenter().X, r.Position.Y + r.Size.Y * 0.4f); };
		Func<Vector2> map = () => DevRect(FlightKitByItem(HudItem.Command)!).GetCenter();
		bool MenuOpen() => hud.Screens.Current == hud.Command && hud.Command.Visible;
		void Base() { hud.Map.Visible = false; if (hud.Screens.Current != null) _m.TestMenu("close"); _m.TestCloseCommand(); hud.HideAlert(); }

		_inScript.Add((t, () => { Base(); _m.TestStopAutomation(); fl.SetSlider(0f); GD.Print("[eingabe] Teil 7: adb-Muster"); }));
		t += 10;

		// ── a) „Menü“, „Karte“ und „Autopilot“ mit adb-Tipps (0 und 1 Bild zwischen Drücken und Loslassen) ──
		foreach (int gap in new[] { 0, 1 })
		{
			int g = gap;
			AdbTap(t, menu, g);
			_inScript.Add((t + 12, () => Check(MenuOpen(), $"adb: Tipp auf „Menü“ (Abstand {g} Bilder) öffnet das Menü")));
			_inScript.Add((t + 40, () => Check(MenuOpen(), $"adb: Menü bleibt offen (Abstand {g} Bilder, 40 Bilder später)")));
			_inScript.Add((t + 42, Base));
			t += 50;
			AdbTap(t, map, g);
			_inScript.Add((t + 12, () => Check(hud.Screens.Current == hud.CcFleet, $"adb: Tipp auf „Befehle“ (Abstand {g} Bilder) öffnet die Befehle")));
			_inScript.Add((t + 14, Base));
			t += 30;
		}

		// ── b) verlorenes Loslassen: jeder Tipp = altes Loslassen + neues Drücken in einem Bild. Tipp 2 löst „Menü“ aus — das Menü muss dann offen bleiben
		//    (vorher schloss das neue Drücken auf dem abgedunkelten Hintergrund es im selben Bild wieder: das Menü erschien nie) ──
		_inScript.Add((t, () => { _adbLast = menu(); TDown(_adbLast); }));   // Tipp 1: Drücken, Loslassen verloren
		AdbLostUpTap(t + 20, menu);                                          // Tipp 2
		_inScript.Add((t + 30, () => Check(MenuOpen(), "adb (verlorenes Loslassen): „Menü“ öffnet beim zweiten Tipp und bleibt offen")));
		AdbLostUpTap(t + 50, menu);                                          // Tipp 3: altes Loslassen gehört zum ignorierten Druck im Öffnungsbild — bleibt offen
		_inScript.Add((t + 60, () => Check(MenuOpen(), "adb (verlorenes Loslassen): dritter Tipp — Loslassen eines im Öffnungsbild ignorierten Drucks schließt nicht")));
		AdbLostUpTap(t + 80, menu);                                          // Tipp 4: ganzer Tipp auf den Hintergrund (Drücken aus Tipp 3) — schließt
		_inScript.Add((t + 90, () => Check(!MenuOpen(), "adb (verlorenes Loslassen): vierter Tipp schließt das Menü (Umschalter)")));
		AdbLostUpTap(t + 110, menu);                                         // Tipp 5: altes Loslassen auf „Menü“ öffnet wieder
		_inScript.Add((t + 120, () => Check(MenuOpen(), "adb (verlorenes Loslassen): fünfter Tipp öffnet wieder")));
		_inScript.Add((t + 122, () => { TUp(_adbLast); }));                 // letztes Loslassen kommt doch an
		_inScript.Add((t + 130, () => Check(MenuOpen(), "adb: das späte Loslassen eines Drucks aus dem Öffnungsbild schließt das Menü nicht")));
		_inScript.Add((t + 132, Base));
		t += 150;

		// ── c) Zurück-Taste im Flug (Benachrichtigung) öffnet das Menü, die nächste schließt es ──
		AdbBack(t);
		_inScript.Add((t + 10, () => Check(MenuOpen(), $"adb: Zurück im Flug öffnet das Menü ({_m.LastBackResult})")));
		AdbBack(t + 40);
		_inScript.Add((t + 50, () => Check(!MenuOpen() && fl.Visible, $"adb: zweites Zurück schließt es ({_m.LastBackResult})")));
		t += 60;
		// Zurück, während ein adb-Tipp auf „Menü“ im selben Bild ankommt: genau ein Zustand, kein Flackern
		_inScript.Add((t, () => Base()));
		t += 10;

		// ── d) Autopilot per adb-Tipp im selben Bild: an, hält 10 s mit Regler 0 (auch über weitere adb-Tipps in der Welt mit Maus-Nachbildung) ──
		_inScript.Add((t, () =>
		{
			if (_m.TestDocked) _m.TestUndock();
			if (_m.TestAutopilotOn) _m.TestStopAutomation();
			fl.SetSlider(0f);
			var u = _m.Session.U;
			var far = u.Stations.Values.Where(st => st.Sector != u.PlayerSector && u.FindRoute(u.PlayerSector, st.Sector) is { Count: > 1 }).OrderBy(st => st.Id.Value).First();
			_m.TestSelectStation(far.Id);   // anderer Sektor: in 11 s nicht erreicht
		}));
		_inScript.Add((t + 2, () => _m.TestKeySteering = false));   // wie am Handy ohne Tastatur
		t += 90;   // Abdock-Animation abwarten
		AdbTap(t + 4, ap, 0);
		_inScript.Add((t + 16, () => Check(_m.TestAutopilotOn, $"adb: Tipp auf „Autopilot“ im selben Bild schaltet ihn ein (Hinweis „{hud.TestToastText}“, Ziel {_m.TestHasTarget})")));
		_inScript.Add((t + 18, () => { _inHold = true; _inHoldFrames = 0; _inHoldBad = 0; _inHoldWhy = ""; }));
		for (int k = 0; k < 5; k++)
		{
			int kk = k;
			AdbTap(t + 60 + k * 90, () => new Vector2(900 + kk * 60, 800), kk % 2);   // Tipps in die Welt
		}
		AdbLostUpTap(t + 520, () => new Vector2(1200, 860));                       // verlorenes Loslassen in der Welt
		AdbTap(t + 560, () => new Vector2(1250, 880), 0);
		// vom Gerät gemeldete Tastaturereignisse (Pfeil hoch, W) — am Handy ohne Tastatur keine Steuerung (Verdacht aus dem Handytest: Regler 6 %/56 % in Tastenschritten)
		_inScript.Add((t + 600, () => Ev(new InputEventKey { Keycode = Key.Up, PhysicalKeycode = Key.Up, Pressed = true })));
		_inScript.Add((t + 630, () => Ev(new InputEventKey { Keycode = Key.Up, PhysicalKeycode = Key.Up, Pressed = false })));
		_inScript.Add((t + 640, () => Ev(new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = true })));
		_inScript.Add((t + 645, () => Ev(new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = false })));
		_inScript.Add((t + 680, () =>
		{
			_inHold = false;
			Check(_inHoldBad == 0 && _inHoldFrames >= 600, $"adb: Autopilot hält {_inHoldFrames / 60f:0.0} s an, Regler bleibt 0{(_inHoldBad > 0 ? $" — {_inHoldBad} schlechte Bilder, zuerst {_inHoldWhy}" : "")}");
			_m.TestStopAutomation();
			_m.TestKeySteering = null;
		}));
		t += 700;
		return t;
	}
}
