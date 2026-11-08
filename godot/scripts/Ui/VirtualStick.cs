using Godot;

namespace SpaceAdventure.Ui;

/// <summary>Virtueller Stick (Richtungssteuerung): Value zeigt in Bildschirmrichtung, Länge = Schub 0..1.</summary>
public partial class VirtualStick : Control
{
	public Vector2 Value { get; private set; }

	int _finger = -1;
	Vector2 _knob;
	float Radius => Size.X * 0.5f;

	public VirtualStick()
	{
		CustomMinimumSize = new Vector2(240, 240);
		MouseFilter = MouseFilterEnum.Stop;
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (!Pointer.TryRead(@event, out int idx, out var pos, out bool pressed, out bool motion)) return;
		if (!motion && pressed && _finger < 0) { _finger = idx; Update(pos); AcceptEvent(); }
		else if (idx == _finger && pressed) { Update(pos); AcceptEvent(); }
		else if (idx == _finger && !pressed) { _finger = -1; Value = Vector2.Zero; _knob = Vector2.Zero; QueueRedraw(); AcceptEvent(); }
	}

	void Update(Vector2 local)
	{
		var v = (local - Size / 2f) / (Radius * 0.8f);
		Value = v.LimitLength(1f);
		if (Value.Length() < 0.12f) Value = Vector2.Zero;   // Totzone
		_knob = Value * Radius * 0.8f;
		QueueRedraw();
	}

	public override void _Draw()
	{
		var c = Size / 2f;
		DrawCircle(c, Radius, new Color(0.08f, 0.12f, 0.2f, 0.35f));
		DrawArc(c, Radius - 2, 0, Mathf.Tau, 48, new Color(0.5f, 0.7f, 1f, 0.55f), 3f);
		DrawCircle(c + _knob, Radius * 0.28f, new Color(0.6f, 0.8f, 1f, _finger >= 0 ? 0.85f : 0.5f));
	}
}
