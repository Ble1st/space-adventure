using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Balken (Hülle, Schild, Leistung, Fortschritt). Gerätetest Phase 27: selbst gezeichnet statt <see cref="ProgressBar"/> — dessen Mindesthöhe kam aus dem
/// Thema (Schrift/Ränder) und ließ den Balken über die Textzeile darunter wachsen. Die Größe ist genau die angegebene.
/// </summary>
public partial class KitBar : Control
{
	float _value, _max = 100f;
	Color _fill;

	public KitBar(float w, float h, Color fill)
	{
		CustomMinimumSize = new Vector2(w, h);
		Size = new Vector2(w, h);
		MouseFilter = MouseFilterEnum.Ignore;
		_fill = fill;
	}

	/// <summary>Aktueller Wert (0..<see cref="MaxValue"/>).</summary>
	public double Value { get => _value; set { _value = (float)value; QueueRedraw(); } }
	public double MaxValue { get => _max; set { _max = (float)value; QueueRedraw(); } }

	/// <summary>Wert 0..max mit optional neuer Füllfarbe.</summary>
	public void Set(float value, Color? fill = null, float max = 100f)
	{
		_max = max; _value = value;
		if (fill is { } f) _fill = f;
		QueueRedraw();
	}

	public override void _Draw()
	{
		var size = Size;
		float r = Mathf.Min(size.Y / 2f, 6f);
		DrawStyleBox(Rounded(new Color(0, 0, 0, 0.45f), r), new Rect2(Vector2.Zero, size));
		float t = _max > 0f ? Mathf.Clamp(_value / _max, 0f, 1f) : 0f;
		if (t > 0f) DrawStyleBox(Rounded(_fill, r), new Rect2(Vector2.Zero, new Vector2(Mathf.Max(size.Y, size.X * t), size.Y)));
	}

	static StyleBoxFlat Rounded(Color c, float r)
	{
		var s = new StyleBoxFlat { BgColor = c };
		s.SetCornerRadiusAll((int)r);
		return s;
	}
}
