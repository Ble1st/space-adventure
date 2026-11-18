using System;
using System.Collections.Generic;
using Godot;

namespace SpaceAdventure.Ui;

/// <summary>
/// Eingabe-Protokoll für den Gerätetest (zweiter Handytest A15: Autopilot schaltete sich mit „manuelle Steuerung“ ab, „Menü“ öffnete nie): jede
/// Bildschirmberührung (Drücken/Loslassen/Abbruch mit Index, Gerätepunkt und dem Element darunter), jede Taste (auch Tastaturereignisse, die das Gerät
/// selbst schickt), jede Auslösung einer Kit-Taste, jede bewusste Regleränderung, jeder Start und Abbruch von Autopilot/Auto-Mining mit dem genauen
/// Eingabeweg und jedes Öffnen/Schließen von Menü und Karte mit dem Aufrufer. Zeilen beginnen mit „[input]“ (am Gerät: <c>adb logcat | grep '\[input\]'</c>).
/// An in Debug-Builds (Gerätetest-APK, Selbsttest), gedrosselt auf <see cref="MaxPerSecond"/> Zeilen je Sekunde — was darüber hinausgeht, wird gezählt
/// und als „… n Zeilen gedrosselt“ nachgereicht. Ziehbewegungen werden nicht einzeln protokolliert, sondern beim Loslassen zusammengefasst.
/// </summary>
public static class InputLog
{
	/// <summary>An/aus (Vorgabe: Debug-Build). Kommandozeile „--inputlog=off“ schaltet es ab, „--inputlog=on“ in Release-Builds an.</summary>
	public static bool Enabled = OS.IsDebugBuild();
	public const int MaxPerSecond = 40;

	static ulong _windowStart;
	static int _inWindow, _dropped;
	/// <summary>Selbsttest: alle bisher geschriebenen Zeilen dieser Sitzung zählen (auch gedrosselte).</summary>
	public static int Lines { get; private set; }
	/// <summary>Selbsttest: die letzten Zeilen (ohne Drosselung), um Ursachen zu prüfen.</summary>
	public static readonly List<string> Recent = new();

	public static void Init()
	{
		foreach (var a in OS.GetCmdlineUserArgs())
		{
			if (a == "--inputlog=off") Enabled = false;
			if (a == "--inputlog=on") Enabled = true;
		}
		if (Enabled) GD.Print($"[input] Protokoll an (Debug {OS.IsDebugBuild()}, Touch {DisplayServer.IsTouchscreenAvailable()}, Hardwaretastatur {DisplayServer.HasHardwareKeyboard()}, " +
			$"Maus aus Touch {ProjectSettings.GetSetting("input_devices/pointing/emulate_mouse_from_touch")}, Fenster {DisplayServer.WindowGetSize()})");
	}

	public static void Log(string line)
	{
		if (!Enabled) return;
		Lines++;
		string full = $"[input] {Engine.GetProcessFrames()} {line}";
		Recent.Add(full);
		if (Recent.Count > 400) Recent.RemoveRange(0, 100);
		ulong now = Time.GetTicksMsec();
		if (now - _windowStart >= 1000)
		{
			if (_dropped > 0) GD.Print($"[input] … {_dropped} Zeilen gedrosselt");
			_windowStart = now; _inWindow = 0; _dropped = 0;
		}
		if (_inWindow >= MaxPerSecond) { _dropped++; return; }
		_inWindow++;
		GD.Print(full);
	}

	/// <summary>Name eines Steuerelements fürs Protokoll: Text einer Kit-Taste, sonst Typ und Knotenname, mit dem Elternteil.</summary>
	public static string Name(Node? n)
	{
		if (n is null) return "-";
		string own = n switch
		{
			Kit.KitButton b => $"Taste „{(b.Text.Length > 0 ? b.Text : b.Sub.Length > 0 ? b.Sub : b.Name.ToString())}“",
			_ => $"{n.GetType().Name}({n.Name})",
		};
		return n.GetParent() is Node p && n is not Kit.KitButton ? $"{own} in {p.GetType().Name}" : own;
	}

	/// <summary>Kurzer Aufruferpfad (Methodennamen) für Öffnen/Schließen von Menüs.</summary>
	public static string Caller(int skip = 2, int depth = 4)
	{
		var st = new System.Diagnostics.StackTrace(skip, false);
		var parts = new List<string>();
		for (int i = 0; i < st.FrameCount && parts.Count < depth; i++)
		{
			var m = st.GetFrame(i)?.GetMethod();
			if (m is null) continue;
			string n = m.Name;
			if (n.StartsWith('<')) n = n.Trim('<').Split('>')[0] + "(λ)";
			parts.Add($"{m.DeclaringType?.Name}.{n}");
		}
		return string.Join(" ← ", parts);
	}

	public static string P(Vector2 v) => $"{v.X:0},{v.Y:0}";

	/// <summary>
	/// Oberstes sichtbares Steuerelement, das Berührungen annimmt (Maus-Filter nicht „Ignore“), an einem Punkt in logischen Wurzelkoordinaten — wie Godots
	/// Oberfläche wählt: höchste CanvasLayer-Ebene zuerst, innerhalb in umgekehrter Baumreihenfolge (zuletzt gezeichnet = oben). Nur fürs Protokoll (Drücken/Loslassen
	/// sind selten); Abschneiden durch Elternteile wird nicht berücksichtigt.
	/// </summary>
	public static Control? TopControlAt(Viewport vp, Vector2 pos)
	{
		Control? best = null; int bestLayer = int.MinValue;
		void Walk(Node n, int layer)
		{
			if (n is CanvasLayer cl) { if (!cl.Visible) return; layer = cl.Layer; }
			if (n is CanvasItem ci && !ci.Visible) return;
			if (n is Window && n != vp) return;
			for (int i = n.GetChildCount() - 1; i >= 0; i--)
			{
				Walk(n.GetChild(i), layer);
			}
			if (n is Control c && c.MouseFilter != Control.MouseFilterEnum.Ignore && layer > bestLayer && c.GetGlobalRect().HasPoint(pos))
			{ best = c; bestLayer = layer; }
		}
		// Kinder rückwärts zuerst; ein Treffer in einem später gezeichneten Zweig gewinnt bei gleicher Ebene, weil nur „layer > bestLayer“ ersetzt
		Walk(vp, 0);
		return best;
	}
}

/// <summary>
/// Sieht jedes Eingabeereignis zuerst (vor der Oberfläche) und protokolliert Berührungen, Maus-Nachbildung und Tasten (<see cref="InputLog"/>).
/// Hängt am Wurzelfenster; die Positionen sind im Fenster in Gerätepunkten (am Handy = adb-Koordinaten) und logisch angegeben.
/// </summary>
public partial class InputLogNode : Node
{
	readonly Dictionary<int, (int Count, Vector2 Start, Vector2 Last)> _drags = new();
	int _mouseMoves;

	public InputLogNode() { ProcessPriority = -2000; ProcessMode = ProcessModeEnum.Always; }

	float WinScale => GetViewport() is { } vp && vp.GetVisibleRect().Size.Y > 0 ? GetWindow().Size.Y / vp.GetVisibleRect().Size.Y : 1f;

	public override void _Input(InputEvent e)
	{
		if (!InputLog.Enabled) return;
		float k = WinScale;
		switch (e)
		{
			case InputEventScreenTouch t:
			{
				string what = t.Canceled ? "ABBRUCH" : t.Pressed ? "AB" : "AUF";
				string drag = "";
				if (!t.Pressed && _drags.TryGetValue(t.Index, out var d)) { if (d.Count > 0) drag = $" · gezogen {d.Count}× {d.Start.DistanceTo(d.Last) * k:0} px"; _drags.Remove(t.Index); }
				if (t.Pressed) _drags[t.Index] = (0, t.Position, t.Position);
				InputLog.Log($"touch {what} idx{t.Index} geraet {InputLog.P(t.Position * k)} log {InputLog.P(t.Position)}{(t.DoubleTap ? " doppel" : "")}{drag} · oben {InputLog.Name(InputLog.TopControlAt(GetViewport(), t.Position))}");
				break;
			}
			case InputEventScreenDrag sd:
				if (_drags.TryGetValue(sd.Index, out var dd)) _drags[sd.Index] = (dd.Count + 1, dd.Start, sd.Position);
				else { _drags[sd.Index] = (1, sd.Position, sd.Position); InputLog.Log($"drag ohne Drücken idx{sd.Index} geraet {InputLog.P(sd.Position * k)}"); }
				break;
			case InputEventMouseButton mb:
				InputLog.Log($"maus {mb.ButtonIndex} {(mb.Canceled ? "ABBRUCH" : mb.Pressed ? "AB" : "AUF")} geraet {InputLog.P(mb.Position * k)} geraetId {mb.Device}{(_mouseMoves > 0 ? $" ({_mouseMoves} Bewegungen davor)" : "")}");
				_mouseMoves = 0;
				break;
			case InputEventMouseMotion:
				_mouseMoves++;
				break;
			case InputEventKey key:
				if (!key.Echo) InputLog.Log($"taste {key.Keycode} (physisch {key.PhysicalKeycode}, unicode {key.Unicode}) {(key.Pressed ? "AB" : "AUF")} geraetId {key.Device}");
				break;
			case InputEventJoypadButton jb:
				InputLog.Log($"gamepad Taste {jb.ButtonIndex} {(jb.Pressed ? "AB" : "AUF")} geraetId {jb.Device}");
				break;
		}
	}

	public override void _Notification(int what)
	{
		if (!InputLog.Enabled) return;
		if (what == NotificationWMGoBackRequest) InputLog.Log("zurück-Benachrichtigung");
		else if (what == NotificationApplicationFocusOut) InputLog.Log("App verliert den Fokus (Godot löst gedrückte Eingaben)");
		else if (what == NotificationApplicationFocusIn) InputLog.Log("App hat den Fokus");
	}
}
