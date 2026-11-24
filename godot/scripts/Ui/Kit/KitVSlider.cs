using System;
using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// HUD-Neu (Entwurf A): senkrechter Schubregler am linken Rand — unten STOPP (0), oben MAX (1), nur vorwärts (Rückwärts ist die eigene Haltetaste RÜCK).
/// Der Wert bleibt stehen, bis er geändert wird. Griff mit Prozentwert, darüber die Geschwindigkeit. Gleiche Schnittstelle wie <see cref="KitSlider"/>.
/// </summary>
public partial class KitVSlider : Control
{
	public float Value { get; private set; }
	/// <summary>Optionale Rastung (Totzone), z. B. <see cref="SpaceSim.Core.Flight.Throttle.Snap"/>.</summary>
	public Func<float, float>? Snap;
	string _speedText = "";
	public string SpeedText { get => _speedText; set { if (_speedText != value) { _speedText = value; QueueRedraw(); } } }
	public event Action<float>? ValueChanged;
	/// <summary>
	/// Geräte-Eingabe (Handytest A15): nur ein Finger, der auf dem Regler aufgesetzt hat, ändert ihn — dann kommt dieses Ereignis (bewusste Bedienung;
	/// der Autopilot bricht nur darauf ab, nie auf einen bloßen Wert ≠ 0).
	/// </summary>
	public event Action<float>? UserChanged;
	/// <summary>Zahl der bewussten Änderungen seit dem Start (Selbsttest).</summary>
	public int UserChanges { get; private set; }
	/// <summary>Eingabe-Protokoll: letzte bewusste Änderung (Finger, Wert vorher → nachher).</summary>
	public string LastUserInfo { get; private set; } = "";
	int _finger = -1;
	float _valueAtPress;

	public KitVSlider()
	{
		CustomMinimumSize = new Vector2(88, 300);
		Size = CustomMinimumSize;
		MouseFilter = MouseFilterEnum.Stop;
	}

	/// <summary>UI-Umbau (Nutzerwunsch: Regler waagerecht unten rechts): breiter als hoch = waagerecht, STOPP links, MAX rechts.</summary>
	public bool Horizontal => Size.X > Size.Y;
	/// <summary>Spur: oben unter der Geschwindigkeit (44), unten über „STOPP“ (24).</summary>
	public float TrackTop => 44f + 20f;
	public float TrackBottom => Size.Y - 24f - 20f;
	/// <summary>Waagerecht: Spur von links (STOPP) nach rechts (MAX).</summary>
	public float TrackLeft => 36f;
	public float TrackRight => Size.X - 36f;

	public void SetValue(float v, bool notify = true)
	{
		v = Mathf.Clamp(v, 0f, 1f);
		if (Mathf.Abs(v - Value) < 1e-5f) return;
		Value = v;
		QueueRedraw();
		if (notify) ValueChanged?.Invoke(v);
	}

	void UserSet(float v)
	{
		float before = Value;
		SetValue(v);
		if (Mathf.Abs(Value - before) >= 1e-5f) { UserChanges++; LastUserInfo = $"idx{_finger} {before:0.00}→{Value:0.00}"; UserChanged?.Invoke(Value); }
	}

	float Snapped(float v) => Snap is null ? v : Snap(v);
	float ValueAt(Vector2 p) => Horizontal
		? Mathf.Clamp((p.X - TrackLeft) / MathF.Max(1f, TrackRight - TrackLeft), 0f, 1f)
		: Mathf.Clamp((TrackBottom - p.Y) / MathF.Max(1f, TrackBottom - TrackTop), 0f, 1f);
	public float YOf(float v) => TrackBottom - v * (TrackBottom - TrackTop);
	public float XOf(float v) => TrackLeft + v * (TrackRight - TrackLeft);
	/// <summary>Lokaler Punkt auf der Spur beim Wert <paramref name="v"/> (beide Ausrichtungen).</summary>
	public Vector2 PointOf(float v) => Horizontal ? new Vector2(XOf(v), Size.Y * 0.62f) : new Vector2(Size.X * 0.5f, YOf(v));

	/// <summary>
	/// Geräte-Eingabe: sieht jedes Drücken/Loslassen vor der Oberfläche. Setzt ein Finger mit dem Index des Reglerfingers neu auf (das Loslassen
	/// davor ging verloren — Android verwirft „inkonsistente“ Ereignisse) oder wird er irgendwo losgelassen, ist die Geste des Reglers vorbei. Vorher
	/// blieb der Index hängen, und ein späterer Wisch in der Welt mit demselben Index, der über den Regler lief, schob ihn (Godot gibt einen Wisch
	/// ohne Fokus an das Element unter dem Finger) — der Regler stand dann ohne Berührung auf einigen Prozent und schaltete den Autopiloten ab.
	/// </summary>
	public override void _Input(InputEvent @event)
	{
		if (_finger < 0 || !Pointer.TryRead(@event, out int idx, out _, out bool pressed, out bool motion) || motion || idx != _finger) return;
		EndGesture(Pointer.Canceled(@event), pressed ? "derselbe Index drückt neu" : "losgelassen");
	}

	void EndGesture(bool canceled, string why)
	{
		if (_finger < 0) return;
		InputLog.Log($"regler Geste idx{_finger} endet ({why}{(canceled ? ", abgebrochen → zurück auf " + _valueAtPress.ToString("0.00") : "")}), Wert {Value:0.00}");
		_finger = -1;
		// das System hat die Geste übernommen: zurück auf den Wert vor dem Druck — ohne „bewusste Änderung“ (zweiter Handytest: ein Abbruch ist keine Bedienung)
		if (canceled) SetValue(_valueAtPress, notify: false);
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (!Pointer.TryRead(@event, out int idx, out var pos, out bool pressed, out bool motion)) return;
		bool inside = new Rect2(Vector2.Zero, Size).HasPoint(pos);
		if (!motion && pressed && inside && _finger < 0)
		{
			_finger = idx; _valueAtPress = Value; UserSet(Snapped(ValueAt(pos))); AcceptEvent();
			InputLog.Log($"regler gedrückt idx{idx} bei {pos} → {_valueAtPress:0.00} → {Value:0.00}");
		}
		else if (idx == _finger && motion) { float b = Value; UserSet(Snapped(ValueAt(pos))); AcceptEvent(); if (Mathf.Abs(Value - b) >= 0.05f) InputLog.Log($"regler gezogen idx{idx} → {Value:0.00}"); }
		else if (idx == _finger && !pressed && !motion) { EndGesture(Pointer.Canceled(@event), "losgelassen auf dem Regler"); AcceptEvent(); }
		else if (!motion && pressed && !inside) InputLog.Log($"regler: Drücken idx{idx} außerhalb (y {pos.Y:0}) ignoriert");
	}

	public override void _Draw()
	{
		var font = ThemeDB.FallbackFont;
		DrawnText.Clear(this);
		if (Horizontal) { DrawHorizontal(font); return; }
		float cx = Size.X / 2f, top = TrackTop, bottom = TrackBottom;
		DrawStyleBox(UiKit.Box(new Color(0.03f, 0.05f, 0.1f, 0.55f), new Color(0.3f, 0.5f, 0.8f, 0.35f), 12, 1, 0), new Rect2(Vector2.Zero, Size));
		UiKit.DrawFit(this, 0, font, new Vector2(4, 22), SpeedText, 16, Size.X - 8f, UiKit.White, HorizontalAlignment.Center);
		UiKit.DrawText(this, 1, font, new Vector2(4, 42), "MAX", 12, UiKit.Dim, HorizontalAlignment.Center, Size.X - 8f);
		UiKit.DrawText(this, 2, font, new Vector2(4, Size.Y - 8), "STOPP", 12, UiKit.White, HorizontalAlignment.Center, Size.X - 8f);
		DrawRect(new Rect2(cx - 7, top, 14, bottom - top), new Color(1, 1, 1, 0.16f));
		float ty = YOf(Value);
		if (Value > 0f) DrawRect(new Rect2(cx - 7, ty, 14, bottom - ty), new Color(UiKit.Cyan, 0.8f));
		foreach (float f in new[] { 0.25f, 0.5f, 0.75f })
		{
			float y = YOf(f);
			DrawLine(new Vector2(cx + 11, y), new Vector2(cx + 20, y), new Color(1, 1, 1, 0.5f), 2);
		}
		var th = new Rect2(4, ty - 20, Size.X - 8f, 40);
		DrawRect(th, new Color(0.2f, 0.38f, 0.62f, 0.97f));
		DrawRect(th, UiKit.Cyan, false, 2);
		UiKit.DrawText(this, 3, font, new Vector2(4, ty + 7), Value >= 0.999f ? "MAX" : $"{Mathf.RoundToInt(Value * 100)} %", 18, UiKit.White, HorizontalAlignment.Center, Size.X - 8f);
	}

	void DrawHorizontal(Font font)
	{
		float l = TrackLeft, r = TrackRight, cy = Size.Y * 0.62f;
		DrawStyleBox(UiKit.Box(new Color(0.03f, 0.05f, 0.1f, 0.6f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 12, 1, 0), new Rect2(Vector2.Zero, Size));
		UiKit.DrawText(this, 1, font, new Vector2(10, 20), "STOPP", 14, UiKit.Dim);
		UiKit.DrawText(this, 2, font, new Vector2(Size.X - 46, 20), "MAX", 14, UiKit.Dim);
		UiKit.DrawFit(this, 0, font, new Vector2(Size.X * 0.25f, 22), SpeedText, 17, Size.X * 0.5f, UiKit.White, HorizontalAlignment.Center);
		DrawRect(new Rect2(l, cy - 5, r - l, 10), new Color(1, 1, 1, 0.16f));
		float tx = XOf(Value);
		if (Value > 0f) DrawRect(new Rect2(l, cy - 5, tx - l, 10), new Color(UiKit.Cyan, 0.8f));
		foreach (float f in new[] { 0.25f, 0.5f, 0.75f }) { float x = XOf(f); DrawLine(new Vector2(x, cy + 9), new Vector2(x, cy + 16), new Color(1, 1, 1, 0.5f), 2); }
		var th = new Rect2(tx - 30, cy - 22, 60, 44);
		DrawRect(th, new Color(0.2f, 0.38f, 0.62f, 0.97f));
		DrawRect(th, UiKit.Cyan, false, 2);
		UiKit.DrawText(this, 3, font, new Vector2(tx - 30, cy + 7), Value >= 0.999f ? "MAX" : $"{Mathf.RoundToInt(Value * 100)} %", 16, UiKit.White, HorizontalAlignment.Center, 60);
	}
}
