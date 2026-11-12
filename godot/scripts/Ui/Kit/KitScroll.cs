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

	/// <summary>Läuft gerade ein Wisch in irgendeinem Rollbereich? Die Bereiche bauen sich dann nicht neu auf.</summary>
	public static bool Dragging => _active > 0;
	static int _active;

	int _finger = -1;
	int _pending = -1, _pendingFrames;
	Vector2 _start;
	int _startScroll;
	bool _dragging;

	public KitScroll()
	{
		HorizontalScrollMode = ScrollMode.Disabled;
		MouseFilter = MouseFilterEnum.Pass;
	}

	/// <summary>Rollpositionen aller Rollbereiche unter <paramref name="root"/> (Baumreihenfolge) — vor einem Neuaufbau.</summary>
	public static List<int> Capture(Node root)
	{
		var list = new List<int>();
		void Walk(Node n) { foreach (var c in n.GetChildren()) { if (c is KitScroll k) list.Add(k._pending >= 0 ? k._pending : k.ScrollVertical); Walk(c); } }
		Walk(root);
		return list;
	}

	/// <summary>Setzt die Rollpositionen nach dem Neuaufbau wieder (sobald das Layout steht).</summary>
	public static void Restore(Node root, List<int> positions)
	{
		int i = 0;
		void Walk(Node n) { foreach (var c in n.GetChildren()) { if (c is KitScroll k && i < positions.Count) { k.SetPending(positions[i]); i++; } Walk(c); } }
		Walk(root);
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
		if (CanScroll() || ++_pendingFrames > 4) { ScrollVertical = _pending; if (ScrollVertical == _pending || _pendingFrames > 4) _pending = -1; }
	}

	public override void _ExitTree()
	{
		if (_dragging) { _dragging = false; _active = Mathf.Max(0, _active - 1); }
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
		if (!IsVisibleInTree()) { _finger = -1; _dragging = false; return; }
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
				if (_finger < 0 && ScreenRect().HasPoint(pos) && CanScroll()) { _finger = idx; _start = pos; _startScroll = ScrollVertical; _dragging = false; }
			}
			else if (idx == _finger) { _finger = -1; if (_dragging) { _dragging = false; _active = Mathf.Max(0, _active - 1); } }
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

	static void CancelPresses(Node n)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is KitButton b) b.CancelPress();
			CancelPresses(c);
		}
	}
}
