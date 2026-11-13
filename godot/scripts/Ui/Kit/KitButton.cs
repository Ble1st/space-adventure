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

	/// <summary>Gerätetest Phase 26: ein Wisch in einer Liste (<see cref="KitScroll"/>) bricht das Drücken ab — beim Loslassen löst die Taste nicht aus.</summary>
	public void CancelPress()
	{
		if (_finger < 0 && !_down) return;
		Release();
		_down = false; QueueRedraw();
	}

	/// <summary>
	/// Zweiter Gerätetest Phase 28: Anzahl gedrückter Tasten. Solange eine Taste gedrückt ist, bauen die Bildschirme nicht neu auf
	/// (sonst landete das Loslassen auf einem neuen Knoten, der das Drücken nie gesehen hat, und die Taste löste nicht aus).
	/// </summary>
	public static int AnyHeld
	{
		get
		{
			// Sicherung: ein Drücken ohne Loslassen (Finger außerhalb des Fensters gelöst) zählt nach 8 s nicht mehr
			ulong now = Time.GetTicksMsec();
			_heldSet.RemoveWhere(b => !GodotObject.IsInstanceValid(b) || !b.IsInsideTree() || b._finger < 0 || now - b._pressedAt > 8000);
			return _heldSet.Count;
		}
	}
	static readonly System.Collections.Generic.HashSet<KitButton> _heldSet = new();
	ulong _pressedAt;
	void Press(int idx) { _finger = idx; _pressedAt = Time.GetTicksMsec(); _heldSet.Add(this); }
	void Release() { _finger = -1; _heldSet.Remove(this); }
	public override void _ExitTree() => Release();

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
		if (!motion && pressed && inside && _finger < 0) { Press(idx); _down = true; QueueRedraw(); AcceptEvent(); Down?.Invoke(); }
		else if (idx == _finger && !pressed && !motion)
		{
			bool fire = _down && inside;
			Release(); _down = false; QueueRedraw(); AcceptEvent();
			if (fire) { if (Toggle) _on = !_on; Pressed?.Invoke(); }
		}
		else if (idx == _finger && motion) { _down = inside; QueueRedraw(); AcceptEvent(); }
	}

	StyleBoxFlat Style()
	{
		// Gerätetest: ein eingeschalteter Umschalter (On) ist sichtbar aktiv — vorher zeichnete nur State, „On“ blieb unsichtbar
		var shown = _on && _state == KitButtonState.Normal ? KitButtonState.On : _state;
		var (bg, border) = shown switch
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

	/// <summary>Gerätetest: kürzt eine Zeile mit „…“, bis sie in <paramref name="maxWidth"/> passt (gemessen mit der Schrift).</summary>
	public static string Fit(Font font, string text, int size, float maxWidth)
	{
		if (maxWidth <= 0f || font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= maxWidth) return text;
		for (int n = text.Length - 1; n > 0; n--)
		{
			string t = text[..n].TrimEnd() + "…";
			if (font.GetStringSize(t, HorizontalAlignment.Left, -1, size).X <= maxWidth) return t;
		}
		return "…";
	}

	public override void _Draw()
	{
		DrawStyleBox(Style(), new Rect2(Vector2.Zero, Size));
		var font = ThemeDB.FallbackFont;
		Color fg = _state == KitButtonState.Disabled ? UiKit.Dim : UiKit.White;
		bool left = Symbol != null || AlignLeft;
		// Gerätetest Phase 27: der Platz für ein Symbol (58 px) nur, wenn es eines gibt — linksbündige Tasten ohne Symbol kürzten sonst „Befehle“ zu „Befe…“
		float tx = Symbol != null ? 58f : left ? 14f : 0f;
		float tw = left ? -1f : -1f;   // -1 = unbegrenzt
		if (left)
		{
			Symbol?.Invoke(this);
			float room = Size.X - tx - 8f;   // Gerätetest: Text endet vor dem Rand (Innenabstand), zu lange Zeilen mit „…“
			DrawString(font, new Vector2(tx, Size.Y * 0.40f), Fit(font, Text, FontSize, room), HorizontalAlignment.Left, -1, FontSize, fg);
			if (Sub.Length > 0) DrawString(font, new Vector2(tx, Size.Y * 0.64f), Fit(font, Sub, 12, room), HorizontalAlignment.Left, -1, 12, UiKit.Dim);
			if (Detail.Length > 0) DrawString(font, new Vector2(tx, Size.Y * 0.88f), Fit(font, Detail, 13, room), HorizontalAlignment.Left, -1, 13, DetailColor);
		}
		else
		{
			float cy = Size.Y / 2f + FontSize * 0.35f - (Sub.Length > 0 ? 8f : 0f);
			DrawString(font, new Vector2(6, cy), Fit(font, Text, FontSize, Size.X - 12f), HorizontalAlignment.Center, Size.X - 12f, FontSize, fg);
			if (Sub.Length > 0) DrawString(font, new Vector2(6, cy + 20f), Fit(font, Sub, 13, Size.X - 12f), HorizontalAlignment.Center, Size.X - 12f, 13, UiKit.Dim);
			// Gerätetest: Zustandszeile unten in der Taste (vorher rechts außerhalb gezeichnet: „Com…“ am Bildrand)
			if (Detail.Length > 0) DrawString(font, new Vector2(6, Size.Y - 6), Fit(font, Detail, 12, Size.X - 12f), HorizontalAlignment.Center, Size.X - 12f, 12, DetailColor);
		}
		if (_state == KitButtonState.Busy)
		{
			// Prüfung Phase 28: bei linksbündiger Taste mit Zustandszeile (Waffenschacht) oben, sonst berührte „lädt nach“ den Balken
			float by = left && Detail.Length > 0 ? 4f : Size.Y - 8f;
			var r = new Rect2(8, by, (Size.X - 16) * Mathf.Clamp(Progress, 0f, 1f), 4);
			DrawRect(new Rect2(8, by, Size.X - 16, 4), new Color(0, 0, 0, 0.5f));
			DrawRect(r, UiKit.Cyan);
		}
	}
}
