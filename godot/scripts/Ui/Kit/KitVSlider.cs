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
	int _finger = -1;
	float _valueAtPress;

	public KitVSlider()
	{
		CustomMinimumSize = new Vector2(88, 300);
		Size = CustomMinimumSize;
		MouseFilter = MouseFilterEnum.Stop;
	}

	/// <summary>Spur: oben unter der Geschwindigkeit (44), unten über „STOPP“ (24).</summary>
	public float TrackTop => 44f + 20f;
	public float TrackBottom => Size.Y - 24f - 20f;

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
		if (Mathf.Abs(Value - before) >= 1e-5f) { UserChanges++; UserChanged?.Invoke(Value); }
	}

	float Snapped(float v) => Snap is null ? v : Snap(v);
	float ValueAt(float y) => Mathf.Clamp((TrackBottom - y) / MathF.Max(1f, TrackBottom - TrackTop), 0f, 1f);
	public float YOf(float v) => TrackBottom - v * (TrackBottom - TrackTop);

	/// <summary>
	/// Geräte-Eingabe: sieht jedes Drücken/Loslassen vor der Oberfläche. Setzt ein Finger mit dem Index des Reglerfingers neu auf (das Loslassen
	/// davor ging verloren — Android verwirft „inkonsistente“ Ereignisse) oder wird er irgendwo losgelassen, ist die Geste des Reglers vorbei. Vorher
	/// blieb der Index hängen, und ein späterer Wisch in der Welt mit demselben Index, der über den Regler lief, schob ihn (Godot gibt einen Wisch
	/// ohne Fokus an das Element unter dem Finger) — der Regler stand dann ohne Berührung auf einigen Prozent und schaltete den Autopiloten ab.
	/// </summary>
	public override void _Input(InputEvent @event)
	{
		if (_finger < 0 || !Pointer.TryRead(@event, out int idx, out _, out _, out bool motion) || motion || idx != _finger) return;
		EndGesture(Pointer.Canceled(@event));
	}

	void EndGesture(bool canceled)
	{
		if (_finger < 0) return;
		_finger = -1;
		if (canceled) UserSet(_valueAtPress);   // das System hat die Geste übernommen: zurück auf den Wert vor dem Druck
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (!Pointer.TryRead(@event, out int idx, out var pos, out bool pressed, out bool motion)) return;
		bool inside = new Rect2(Vector2.Zero, Size).HasPoint(pos);
		if (!motion && pressed && inside && _finger < 0) { _finger = idx; _valueAtPress = Value; UserSet(Snapped(ValueAt(pos.Y))); AcceptEvent(); }
		else if (idx == _finger && motion) { UserSet(Snapped(ValueAt(pos.Y))); AcceptEvent(); }
		else if (idx == _finger && !pressed && !motion) { EndGesture(Pointer.Canceled(@event)); AcceptEvent(); }
	}

	public override void _Draw()
	{
		var font = ThemeDB.FallbackFont;
		DrawnText.Clear(this);
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
}
