using System.Collections.Generic;
using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Gerätetest (Phase 27, Samsung A15): Listen ließen sich per Finger nicht wischen — die Kit-Tasten (<see cref="KitButton"/>, Mausfilter „Stop“) nehmen das
/// Aufsetzen an, der <see cref="ScrollContainer"/> sah den Zug nie. <see cref="KitScroll"/> ist der eine Rollbereich aller Bildschirme: Er liest Finger- und
/// Mausereignisse vorab (<see cref="_Input"/>), merkt sich den Aufsetzpunkt in seinem Rechteck und rollt, sobald der Finger mehr als
/// <see cref="Deadzone"/> Pixel senkrecht gezogen wurde; dann bricht er das Drücken der Tasten darunter ab (<see cref="KitButton.CancelPress"/>), damit das
/// Loslassen nichts auslöst. Verschachtelte Bereiche: Der innerste rollbare nimmt den Zug (er bekommt die Ereignisse zuerst und markiert sie als erledigt).
/// Mausrad und Rollbalken bleiben wie beim <see cref="ScrollContainer"/>.
/// </summary>
public partial class KitScroll : ScrollContainer
{
	/// <summary>Totzone in Pixeln, ab der ein Zug als Rollen gilt (darunter bleibt es ein Tipp).</summary>
	public const float Deadzone = 12f;

	/// <summary>
	/// Läuft gerade ein Wisch in irgendeinem Rollbereich — oder liegt ein Finger in einem rollbaren Bereich auf (Gerätetest Phase 27: ein Neuaufbau zwischen
	/// Aufsetzen und Ziehen ersetzte den Bereich, der erste Wisch von einer Taste aus rollte nicht)? Die Bereiche bauen sich dann nicht neu auf.
	/// </summary>
	public static bool Dragging => _active > 0 || _held > 0;
	static int _active, _held;
	bool _holding;

	/// <summary>
	/// Gerätetest Phase 27: stabiler Schlüssel des Rollbereichs (Datei und Zeile, an der er angelegt wird) — die Rollposition wird nach einem Neuaufbau über
	/// ihn wiederhergestellt statt über die Baumreihenfolge (die sich verschiebt, wenn Zeilen oder Bereiche dazukommen oder wegfallen).
	/// </summary>
	public string ScrollKey { get; set; }

	int _finger = -1;
	int _pending = -1, _pendingFrames;
	Vector2 _start;
	int _startScroll;
	bool _dragging;

	public KitScroll([System.Runtime.CompilerServices.CallerFilePath] string file = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
	{
		ScrollKey = $"{System.IO.Path.GetFileName(file)}:{line}";
		HorizontalScrollMode = ScrollMode.Disabled;
		MouseFilter = MouseFilterEnum.Pass;
		// Zweiter Gerätetest Phase 28: die gemerkte Position gleich nach dem Layout setzen (im selben Bild) — vorher stand die Liste nach jedem Neuaufbau
		// ein oder mehrere Bilder oben, die Handelsliste sprang sichtbar zwischen oben und gerollt
		SortChildren += ApplyPending;
	}

	void ApplyPending()
	{
		if (_pending < 0 || !CanScroll()) return;
		ScrollVertical = _pending;
		if (ScrollVertical == _pending) _pending = -1;
	}

	/// <summary>Zweiter Gerätetest Phase 28: rollt auf <paramref name="v"/>, sobald das Layout steht (z. B. aufgeklappte Zeile sichtbar machen).</summary>
	public void ScrollTo(int v)
	{
		if (v <= 0) { _pending = -1; ScrollVertical = 0; return; }
		SetPending(v);
	}

	/// <summary>Rollpositionen aller Rollbereiche unter <paramref name="root"/> nach stabilem Schlüssel (mehrfache Schlüssel durchnummeriert) — vor einem Neuaufbau.</summary>
	public static Dictionary<string, int> Capture(Node root)
	{
		var map = new Dictionary<string, int>();
		var seen = new Dictionary<string, int>();
		void Walk(Node n)
		{
			foreach (var c in n.GetChildren())
			{
				if (c is KitScroll k && !k.IsQueuedForDeletion())
				{
					int nth = seen.GetValueOrDefault(k.ScrollKey); seen[k.ScrollKey] = nth + 1;
					map[$"{k.ScrollKey}#{nth}"] = k._pending >= 0 ? k._pending : k.ScrollVertical;
				}
				Walk(c);
			}
		}
		Walk(root);
		return map;
	}

	/// <summary>Setzt die Rollpositionen nach dem Neuaufbau über den Schlüssel wieder (sobald das Layout steht).</summary>
	public static void Restore(Node root, Dictionary<string, int> positions)
	{
		var seen = new Dictionary<string, int>();
		void Walk(Node n)
		{
			foreach (var c in n.GetChildren())
			{
				if (c is KitScroll k && !k.IsQueuedForDeletion())
				{
					int nth = seen.GetValueOrDefault(k.ScrollKey); seen[k.ScrollKey] = nth + 1;
					if (positions.TryGetValue($"{k.ScrollKey}#{nth}", out int v)) k.SetPending(v);
				}
				Walk(c);
			}
		}
		Walk(root);
	}

	/// <summary>Selbsttest: Rollposition des ersten Bereichs mit diesem Schlüsselanfang unter <paramref name="root"/> (−1 = keiner).</summary>
	public static int PositionOf(Node root, string keyPrefix)
	{
		foreach (var c in root.GetChildren())
		{
			if (c is KitScroll k && !k.IsQueuedForDeletion() && k.ScrollKey.StartsWith(keyPrefix, System.StringComparison.Ordinal)) return k._pending >= 0 ? k._pending : k.ScrollVertical;
			int r = PositionOf(c, keyPrefix);
			if (r >= 0) return r;
		}
		return -1;
	}

	void SetPending(int v)
	{
		if (v <= 0) return;
		_pending = v; _pendingFrames = 0;
		ScrollVertical = v;   // gilt sofort, wenn das Layout schon steht; sonst im nächsten Bild
	}

	public override void _Process(double delta)
	{
		if (_pending < 0) return;
		// Gerätetest Phase 28 (Startpaket-Liste sprang an den Anfang): warten, bis das Layout den Bereich wirklich rollbar macht (am Gerät dauert das länger als
		// 4 Bilder) — erst dann setzen; nach 60 Bildern aufgeben
		++_pendingFrames;
		if (CanScroll()) { ScrollVertical = _pending; if (ScrollVertical == _pending || _pendingFrames > 60) _pending = -1; }
		else if (_pendingFrames > 60) _pending = -1;
	}

	public override void _ExitTree()
	{
		if (_dragging) { _dragging = false; _active = Mathf.Max(0, _active - 1); }
		if (_holding) { _holding = false; _held = Mathf.Max(0, _held - 1); }
		_finger = -1;
	}

	/// <summary>Bildschirmrechteck des Bereichs (mit CanvasLayer-Versatz und Skalierung).</summary>
	Rect2 ScreenRect()
	{
		var xf = GetGlobalTransformWithCanvas();
		return new Rect2(xf.Origin, Size * xf.Scale);
	}

	bool CanScroll() => GetVScrollBar() is { } bar && bar.MaxValue - bar.Page > 0.5;

	public override void _Input(InputEvent e)
	{
		if (!IsVisibleInTree()) { Release(); return; }
		int idx; Vector2 pos; bool pressed, motion;
		switch (e)
		{
			case InputEventScreenTouch t: idx = t.Index; pos = t.Position; pressed = t.Pressed; motion = false; break;
			case InputEventScreenDrag d: idx = d.Index; pos = d.Position; pressed = true; motion = true; break;
			case InputEventMouseButton mb when !Pointer.TouchMode && mb.ButtonIndex == MouseButton.Left: idx = 0; pos = mb.Position; pressed = mb.Pressed; motion = false; break;
			case InputEventMouseMotion mm when !Pointer.TouchMode && (mm.ButtonMask & MouseButtonMask.Left) != 0: idx = 0; pos = mm.Position; pressed = true; motion = true; break;
			default: return;
		}
		if (!motion)
		{
			if (pressed)
			{
				if (_finger < 0 && ScreenRect().HasPoint(pos) && CanScroll())
				{
					_finger = idx; _start = pos; _startScroll = _pending >= 0 ? _pending : ScrollVertical; _dragging = false;
					if (!_holding) { _holding = true; _held++; }
				}
			}
			else if (idx == _finger) Release();
			return;
		}
		if (idx != _finger) return;
		float dy = pos.Y - _start.Y;
		if (!_dragging && Mathf.Abs(dy) > Deadzone)
		{
			_dragging = true;
			_active++;
			_pending = -1;
			CancelPresses(this);
		}
		if (!_dragging) return;
		ScrollVertical = Mathf.RoundToInt(_startScroll - dy);
		GetViewport().SetInputAsHandled();
	}

	void Release()
	{
		_finger = -1;
		if (_dragging) { _dragging = false; _active = Mathf.Max(0, _active - 1); }
		if (_holding) { _holding = false; _held = Mathf.Max(0, _held - 1); }
	}

	static void CancelPresses(Node n)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is KitButton b) b.CancelPress();
			CancelPresses(c);
		}
	}
}
