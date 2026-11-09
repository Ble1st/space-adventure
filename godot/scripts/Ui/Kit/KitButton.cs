using System;
using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>Anzeigestil einer Kit-Taste (Zustände laut UI.md: bereit, außerhalb, lädt nach, gedämpft …).</summary>
public enum KitButtonState { Normal, On, Ready, Warn, Busy, Disabled }

/// <summary>
/// Kit-Taste mit Zuständen und Mehrfinger-Unterstützung (über <see cref="Pointer"/> wie TouchButton).
/// Zustände steuern Rahmen/Farbe; <see cref="Symbol"/> malt ein eigenes Symbol links (z. B. den Feuerwinkel
/// einer Waffenschacht-Taste), <see cref="Progress"/> einen Nachladebalken bei <see cref="KitButtonState.Busy"/>.
/// </summary>
public partial class KitButton : Control
{
	string _text = "";
	public string Text { get => _text; set { _text = value; QueueRedraw(); } }
	string _sub = "";
	/// <summary>Kleine zweite Zeile unter dem Text (z. B. „Bug · ±40°“).</summary>
	public string Sub { get => _sub; set { _sub = value; QueueRedraw(); } }
	string _detail = "";
	/// <summary>Zustandszeile (z. B. „FEUER“, „◀ 41° drehen“, „lädt nach“), eigene Farbe über <see cref="DetailColor"/>.</summary>
	public string Detail { get => _detail; set { _detail = value; QueueRedraw(); } }
	public Color DetailColor { get; set; } = UiKit.Dim;
	public KitButtonState State { get => _state; set { _state = value; QueueRedraw(); } }
	/// <summary>Balkenfüllung 0..1, gezeichnet bei <see cref="KitButtonState.Busy"/>.</summary>
	public float Progress { get; set; }
	public bool Toggle;
	public bool On { get => _on; set { _on = value; QueueRedraw(); } }
	/// <summary>True, solange ein Finger die Taste hält (Haltetasten wie „Wenden“).</summary>
	public bool Held => _down;
	public int FontSize { get; set; } = 20;
	/// <summary>Text linksbündig statt zentriert (Listenreihen, Tasten mit Symbol).</summary>
	public bool AlignLeft;
	/// <summary>Eigenes Symbol links; zeichnet mit den Draw-Methoden dieser Taste.</summary>
	public Action<KitButton>? Symbol;

	/// <summary>Sofort beim Aufsetzen des Fingers (Einzelschuss), nicht erst beim Loslassen.</summary>
	public event Action? Down;
	/// <summary>Beim Loslassen innerhalb der Taste.</summary>
	public event Action? Pressed;

	KitButtonState _state;
	bool _on, _down;
	int _finger = -1;

	public KitButton(string text = "", Vector2? size = null)
	{
		Text = text;
		CustomMinimumSize = size ?? new Vector2(150, 56);
		Size = CustomMinimumSize;
		MouseFilter = MouseFilterEnum.Stop;
	}

	public void SetState(KitButtonState state, string? detail = null, Color? detailColor = null)
	{
		State = state;
		if (detail != null) Detail = detail;
		if (detailColor is { } c) DetailColor = c;
		QueueRedraw();
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (!Pointer.TryRead(@event, out int idx, out var pos, out bool pressed, out bool motion)) return;
		if (State == KitButtonState.Disabled) { if (!motion && pressed) AcceptEvent(); return; }
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

	StyleBoxFlat Style()
	{
		var (bg, border) = _state switch
		{
			KitButtonState.On => (new Color(0.15f, 0.55f, 0.35f, 0.9f), UiKit.Green),
			KitButtonState.Ready => (new Color(0.1f, 0.34f, 0.2f, 0.95f), UiKit.Green),
			KitButtonState.Warn => (new Color(0.3f, 0.2f, 0.06f, 0.92f), UiKit.Orange),
			KitButtonState.Busy => (new Color(0.08f, 0.12f, 0.2f, 0.92f), UiKit.Dim),
			KitButtonState.Disabled => (new Color(0.05f, 0.08f, 0.14f, 0.85f), new Color(UiKit.Dim, 0.4f)),
			_ => (new Color(0.1f, 0.18f, 0.32f, 0.92f), UiKit.Edge),
		};
		if (_down && _state != KitButtonState.Disabled) bg = bg.Lightened(0.18f);
		return UiKit.Box(bg, border, 12, 2, 0);
	}

	public override void _Draw()
	{
		DrawStyleBox(Style(), new Rect2(Vector2.Zero, Size));
		var font = ThemeDB.FallbackFont;
		Color fg = _state == KitButtonState.Disabled ? UiKit.Dim : UiKit.White;
		bool left = Symbol != null || AlignLeft;
		float tx = left ? 58f : 0f;
		float tw = left ? -1f : -1f;   // -1 = unbegrenzt
		if (left)
		{
			Symbol?.Invoke(this);
			DrawString(font, new Vector2(tx, Size.Y * 0.40f), Text, HorizontalAlignment.Left, -1, FontSize, fg);
			if (Sub.Length > 0) DrawString(font, new Vector2(tx, Size.Y * 0.64f), Sub, HorizontalAlignment.Left, -1, 12, UiKit.Dim);
			if (Detail.Length > 0) DrawString(font, new Vector2(tx, Size.Y * 0.88f), Detail, HorizontalAlignment.Left, -1, 13, DetailColor);
		}
		else
		{
			float cy = Size.Y / 2f + FontSize * 0.35f - (Sub.Length > 0 ? 8f : 0f);
			DrawString(font, new Vector2(0, cy), Text, HorizontalAlignment.Center, Size.X, FontSize, fg);
			if (Sub.Length > 0) DrawString(font, new Vector2(0, cy + 20f), Sub, HorizontalAlignment.Center, Size.X, 13, UiKit.Dim);
			if (Detail.Length > 0) DrawString(font, new Vector2(Size.X - 12, cy), Detail, HorizontalAlignment.Right, 400, 14, DetailColor);
		}
		if (_state == KitButtonState.Busy)
		{
			var r = new Rect2(8, Size.Y - 8, (Size.X - 16) * Mathf.Clamp(Progress, 0f, 1f), 4);
			DrawRect(new Rect2(8, Size.Y - 8, Size.X - 16, 4), new Color(0, 0, 0, 0.5f));
			DrawRect(r, UiKit.Cyan);
		}
	}
}
