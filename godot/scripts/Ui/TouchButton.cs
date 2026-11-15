using System;
using Godot;

namespace SpaceAdventure.Ui;

/// <summary>Eigener Knopf mit Mehrfinger-Unterstützung (Godots Button reagiert bei Touch nur auf den ersten Finger).</summary>
public partial class TouchButton : Control
{
	public string Text { get; set; } = "";
	public bool Toggle { get; set; }
	public bool On { get => _on; set { _on = value; QueueRedraw(); } }
	public event Action? Pressed;

	/// <summary>Sofort beim Aufsetzen des Fingers (für Einzelschüsse), nicht erst beim Loslassen.</summary>
	public event Action? Down;

	/// <summary>True, solange ein Finger/die Maus gedrückt hält (für Dauerfeuer).</summary>
	public bool Held => _down;

	static readonly StyleBoxFlat Normal = Make(new Color(0.08f, 0.12f, 0.2f, 0.78f));
	static readonly StyleBoxFlat Pressed_ = Make(new Color(0.30f, 0.45f, 0.65f, 0.95f));
	static readonly StyleBoxFlat Active = Make(new Color(0.15f, 0.55f, 0.35f, 0.9f));

	static StyleBoxFlat Make(Color bg)
	{
		var b = new StyleBoxFlat { BgColor = bg, BorderColor = new Color(0.5f, 0.7f, 1f, 0.8f) };
		b.SetCornerRadiusAll(10); b.SetBorderWidthAll(2);
		return b;
	}

	bool _on, _down;
	int _finger = -1;

	public TouchButton(string text, Vector2? size = null)
	{
		Text = text;
		CustomMinimumSize = size ?? new Vector2(150, 56);
		MouseFilter = MouseFilterEnum.Stop;
	}

	public void SetText(string text) { Text = text; QueueRedraw(); }

	public override void _GuiInput(InputEvent @event)
	{
		if (!Pointer.TryRead(@event, out int idx, out var pos, out bool pressed, out bool motion)) return;
		bool inside = new Rect2(Vector2.Zero, Size).HasPoint(pos);
		if (!motion && pressed && inside && _finger < 0) { _finger = idx; _down = true; QueueRedraw(); AcceptEvent(); Down?.Invoke(); }
		else if (idx == _finger && !pressed && !motion)
		{
			bool fire = _down && inside;
			_finger = -1; _down = false; QueueRedraw(); AcceptEvent();
			if (fire) { if (Toggle) _on = !_on; Pressed?.Invoke(); }
		}
		else if (idx == _finger && motion) { _down = inside; QueueRedraw(); AcceptEvent(); }
	}

	public override void _Draw()
	{
		DrawStyleBox(_down ? Pressed_ : _on ? Active : Normal, new Rect2(Vector2.Zero, Size));
		var font = ThemeDB.FallbackFont;
		int fs = 20;
		var ts = font.GetStringSize(Text, HorizontalAlignment.Left, -1, fs);
		SpaceAdventure.Ui.Kit.UiKit.DrawText(this, 0, font, new Vector2((Size.X - ts.X) / 2f, (Size.Y + fs * 0.7f) / 2f), Text, fs, Colors.White);
	}
}
