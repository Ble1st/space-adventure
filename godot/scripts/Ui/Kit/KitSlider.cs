using System;
using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Waagerechter Tempo-Slider am unteren Bildrand: STOPP (0) bis MAX (1), nur vorwärts. Rückwärts ist eine eigene
/// Haltetaste (siehe SpaceSim.Core.Flight.Throttle). Der Slider bleibt stehen, bis er geändert wird
/// (Ziehen oder Taste W/Pfeil hoch, X = STOPP). Der Griff zeigt den Prozentwert, links daneben steht die Geschwindigkeit.
/// </summary>
public partial class KitSlider : Control
{
	public float Value { get; private set; }
	/// <summary>Gerätetest: optionale Rastung (Totzone) für Touch-Eingaben, z. B. <see cref="SpaceSim.Core.Flight.Throttle.Snap"/>.</summary>
	public Func<float, float>? Snap;
	string _caption = "TEMPO";
	public string Caption { get => _caption; set { _caption = value; QueueRedraw(); } }
	string _speedText = "";
	/// <summary>Geschwindigkeitsanzeige links neben der Spur (z. B. „24 m/s“).</summary>
	public string SpeedText { get => _speedText; set { _speedText = value; QueueRedraw(); } }
	public event Action<float>? ValueChanged;

	int _finger = -1;

	public KitSlider()
	{
		CustomMinimumSize = new Vector2(600, 68);
		Size = CustomMinimumSize;
		MouseFilter = MouseFilterEnum.Stop;
	}

	float Left => 100f;
	float Right => Size.X - 30f;
	float Cy => 30f;
	float StopX => Left;

	/// <summary>Setzt den Sliderwert (gekappt auf [0, 1]); Test-Hooks und Tastatur nutzen das.</summary>
	public void SetValue(float v, bool notify = true)
	{
		v = Mathf.Clamp(v, 0f, 1f);
		if (Mathf.Abs(v - Value) < 1e-5f) return;
		Value = v;
		QueueRedraw();
		if (notify) ValueChanged?.Invoke(v);
	}

	float Snapped(float v) => Snap is null ? v : Snap(v);

	float ValueAt(float x)
	{
		return Mathf.Clamp((x - Left) / (Right - Left), 0f, 1f);
	}

	float XOf(float v) => Left + v * (Right - Left);

	public override void _GuiInput(InputEvent @event)
	{
		if (!Pointer.TryRead(@event, out int idx, out var pos, out bool pressed, out bool motion)) return;
		if (!motion && pressed && _finger < 0) { _finger = idx; SetValue(Snapped(ValueAt(pos.X))); AcceptEvent(); }
		else if (idx == _finger && motion) { SetValue(Snapped(ValueAt(pos.X))); AcceptEvent(); }
		else if (idx == _finger && !pressed && !motion) { _finger = -1; AcceptEvent(); }
	}

	public override void _Draw()
	{
		var font = ThemeDB.FallbackFont;
		float left = Left, right = Right, cy = Cy, stop = StopX;
		DrawString(font, new Vector2(0, 26), Caption, HorizontalAlignment.Left, -1, 14, UiKit.Dim);
		DrawString(font, new Vector2(0, 50), SpeedText, HorizontalAlignment.Left, -1, 18, UiKit.White);
		DrawRect(new Rect2(stop, cy - 7, right - stop, 14), new Color(1, 1, 1, 0.16f));
		float tx = XOf(Value);
		if (Value > 0f) DrawRect(new Rect2(stop, cy - 7, tx - stop, 14), new Color(UiKit.Cyan, 0.8f));
		foreach (var (lab, f) in new[] { ("STOPP", 0f), ("25", 0.25f), ("50", 0.5f), ("75", 0.75f), ("MAX", 1f) })
		{
			float x = stop + f * (right - stop);
			DrawLine(new Vector2(x, cy + 11), new Vector2(x, cy + 20), new Color(1, 1, 1, 0.5f), 2);
			DrawString(font, f > 0 ? new Vector2(x - 30, cy + 36) : new Vector2(stop - 14, cy + 36), lab,
				f > 0 ? HorizontalAlignment.Center : HorizontalAlignment.Left, f > 0 ? 60 : -1, 12, f > 0 ? UiKit.Dim : UiKit.White);
		}
		var th = new Rect2(tx - 32, cy - 19, 64, 38);
		DrawRect(th, new Color(0.2f, 0.38f, 0.62f, 0.97f));
		DrawRect(th, UiKit.Cyan, false, 2);
		DrawString(font, new Vector2(tx - 32, cy + 6), (Value >= 0.999f ? "MAX" : $"{Mathf.RoundToInt(Value * 100)} %"), HorizontalAlignment.Center, 64, 18, UiKit.White);
	}
}
