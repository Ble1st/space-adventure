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
	/// <summary>Eingabe-Protokoll: welcher Finger hält seit wann (z. B. „idx0 seit 1200 ms“).</summary>
	public string HeldInfo => _finger >= 0 ? $"idx{_finger} seit {Time.GetTicksMsec() - _pressedAt} ms" : "kein Finger";
	public int FontSize { get; set; } = 20;
	/// <summary>Text linksbündig statt zentriert (Listenreihen, Tasten mit Symbol).</summary>
	public bool AlignLeft;
	/// <summary>UI-Umbau: unsichtbare Tippfläche über einer Karte (zeichnet nichts, nur ein leichter Rahmen beim Drücken).</summary>
	public bool Flat;
	/// <summary>Eigenes Symbol links; zeichnet mit den Draw-Methoden dieser Taste.</summary>
	public Action<KitButton>? Symbol;

	/// <summary>Sofort beim Aufsetzen des Fingers (Einzelschuss), nicht erst beim Loslassen.</summary>
	public event Action? Down;
	/// <summary>Beim Loslassen innerhalb der Taste.</summary>
	public event Action? Pressed;
	/// <summary>Selbsttest (Runde 12): Tipp auslösen wie ein echter Tipp (Umschalter schalten um).</summary>
	public void TestPress() { if (State == KitButtonState.Disabled) return; if (Toggle) _on = !_on; Pressed?.Invoke(); }

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
	void Press(int idx) { _finger = idx; _pressedAt = Time.GetTicksMsec(); _heldSet.Add(this); SetProcessInput(true); }
	void Release() { _finger = -1; _heldSet.Remove(this); if (IsInsideTree()) SetProcessInput(false); }
	public override void _ExitTree() => Release();
	public override void _Ready() => SetProcessInput(_finger >= 0);   // _Input nur, solange ein Finger hält (Hunderte Tasten, sonst je Ereignis ein Aufruf)

	/// <summary>
	/// Zweiter Handytest A15 (adb-Tipps, „dropping inconsistent event“): setzt der haltende Finger (Index) außerhalb der Taste neu auf, ging sein Loslassen
	/// verloren — die Taste lässt los (ohne auszulösen). Vorher blieb sie gedrückt: eine Haltetaste (◀ ▶ RÜCK) meldete weiter „gehalten“, und der Autopilot
	/// brach mit „manuelle Steuerung“ ab. Läuft vor der Oberfläche; ein neues Drücken in der Taste übernimmt _GuiInput.
	/// </summary>
	public override void _Input(InputEvent e)
	{
		if (_finger < 0 || !Pointer.TryRead(e, out int idx, out var pos, out bool pressed, out bool motion) || motion || !pressed || idx != _finger) return;
		if (GetGlobalRect().HasPoint(pos)) return;
		InputLog.Log($"knopf {InputLog.Name(this)} lässt los: Finger idx{idx} setzt woanders neu auf (Loslassen verloren)");
		Release(); _down = false; QueueRedraw();
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
		// Geräte-Eingabe: setzt derselbe Finger (Index) neu auf, ging sein Loslassen verloren — die Taste nimmt den neuen Druck an statt hängen zu bleiben
		if (State == KitButtonState.Disabled) { if (!motion && pressed) { AcceptEvent(); InputLog.Log($"knopf {InputLog.Name(this)} gesperrt, Drücken idx{idx} ignoriert"); } return; }
		bool inside = new Rect2(Vector2.Zero, Size).HasPoint(pos);
		if (!motion && pressed && inside && (_finger < 0 || idx == _finger))
		{
			if (InputLog.Enabled) InputLog.Log($"knopf {InputLog.Name(this)} gedrückt idx{idx}{(_finger == idx ? " (neu gedrückt: Loslassen davor verloren)" : "")}");
			Press(idx); _down = true; QueueRedraw(); AcceptEvent(); Down?.Invoke();
		}
		else if (idx == _finger && !pressed && !motion)
		{
			bool canceled = Pointer.Canceled(@event);
			bool fire = _down && inside && !canceled;   // Geräte-Eingabe: abgebrochene Geste (System übernimmt) ist kein Tipp
			if (InputLog.Enabled) InputLog.Log($"knopf {InputLog.Name(this)} losgelassen idx{idx} → {(fire ? "löst aus" : canceled ? "kein Tipp (abgebrochen)" : !inside ? "kein Tipp (außerhalb)" : "kein Tipp")}{(Toggle && fire ? $", Umschalter {(!_on ? "an" : "aus")}" : "")}");
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

	/// <summary>
	/// Runde 18 (Handy-Test: „verankert · Abb…“, „Vorrat 12/12 · 11…“): erste Zeile (Text/Unterzeile/Detail), die in dieser Taste mit „…“ gekürzt würde — gemessen
	/// mit der Schrift wie im Zeichnen; leer = alles passt. Nur für linksbündige Tasten mit Symbol/Ausrichtung (Waffenschächte), sonst die Mittelspalte.
	/// </summary>
	public string Overflow()
	{
		var font = ThemeDB.FallbackFont;
		bool left = Symbol != null || AlignLeft;
		float room = left ? Size.X - (Symbol != null ? 58f : 14f) - 8f : Size.X - 12f;
		foreach (var (t, sz) in new[] { (Text, MainSize(font, room, left)), (Sub, left ? 12 : 13), (Detail, left ? 13 : 12) })
			if (t.Length > 0 && Fit(font, t, sz, room) != t) return t;
		return "";
	}

	/// <summary>
	/// Runde 37 (Handytest r35 g21: „Station angreif…“ bei „groß“; g20: „Laser“ klebte am oberen Rand): Schriftgröße der Hauptzeile — erst kleiner bis zur
	/// Untergrenze der Ebene (nie unter „klein“), dann erst „…“; links ausgerichtete Tasten mit Unter- und Zustandszeile halten oben und unten 4 px Rand.
	/// </summary>
	int MainSize(Font font, float room, bool left)
	{
		int size = FontSize;
		if (left)
		{
			float rest = (Sub.Length > 0 ? 12 + 2 : 0) + (Detail.Length > 0 ? 13 + 2 : 0);
			size = Math.Max(11, Math.Min(size, (int)MathF.Floor(Size.Y - 8f - rest)));
		}
		if (Text.Length == 0 || room <= 0f) return size;
		int floor = Math.Min(size, SpaceSim.Core.Ui.UiLayout.FontFloor(FontSize, UiKit.LayerFactor(this)));
		return SpaceSim.Core.Ui.UiLayout.FitFontSize(fs => font.GetStringSize(Text, HorizontalAlignment.Left, -1, fs).X, room, size, floor);
	}

	public override void _Draw()
	{
		if (Flat) { DrawnText.Clear(this); if (Held) DrawRect(new Rect2(Vector2.Zero, Size), new Color(UiKit.Cyan, 0.15f)); return; }
		DrawStyleBox(Style(), new Rect2(Vector2.Zero, Size));
		var font = ThemeDB.FallbackFont;
		DrawnText.Clear(this);
		Color fg = _state == KitButtonState.Disabled ? UiKit.Dim : UiKit.White;
		bool left = Symbol != null || AlignLeft;
		// Gerätetest Phase 27: der Platz für ein Symbol (58 px) nur, wenn es eines gibt — linksbündige Tasten ohne Symbol kürzten sonst „Befehle“ zu „Befe…“
		float tx = Symbol != null ? 58f : left ? 14f : 0f;
		float tw = left ? -1f : -1f;   // -1 = unbegrenzt
		if (left)
		{
			Symbol?.Invoke(this);
			float room = Size.X - tx - 8f;   // Gerätetest: Text endet vor dem Rand (Innenabstand), zu lange Zeilen mit „…“
			// Runde 35c (Prüfwerkzeug, Hamburger-Menü: Unterlänge der Hauptzeile berührte die Unterzeile): die Zeilen als Block nach ihren Schriftgrößen
			// senkrecht mittig gesetzt (Abstand 3), statt an festen Anteilen der Höhe
			int ms = MainSize(font, room, true);
			float total = ms + (Sub.Length > 0 ? 12 + 2 : 0) + (Detail.Length > 0 ? 13 + 2 : 0);
			float y0 = MathF.Max(4f, (Size.Y - total) / 2f);
			UiKit.DrawText(this, 0, font, new Vector2(tx, y0 + ms * 0.8f), Fit(font, Text, ms, room), ms, fg, full: Text);
			y0 += ms + 2f;
			if (Sub.Length > 0) { UiKit.DrawText(this, 1, font, new Vector2(tx, y0 + 12 * 0.8f), Fit(font, Sub, 12, room), 12, UiKit.Dim, full: Sub); y0 += 12 + 2f; }
			if (Detail.Length > 0) UiKit.DrawText(this, 2, font, new Vector2(tx, y0 + 13 * 0.8f), Fit(font, Detail, 13, room), 13, DetailColor, full: Detail);
		}
		else
		{
			// Runde 35c (Prüfwerkzeug: Text und Zustandszeile berührten sich bei 46 px hohen Tasten): mit Zustandszeile rückt der Text etwas nach oben
			int ms = MainSize(font, Size.X - 12f, false);
			float cy = Size.Y / 2f + ms * 0.35f - (Sub.Length > 0 ? 8f : 0f) - (Detail.Length > 0 && Sub.Length == 0 ? 5f : 0f);
			UiKit.DrawText(this, 0, font, new Vector2(6, cy), Fit(font, Text, ms, Size.X - 12f), ms, fg, HorizontalAlignment.Center, Size.X - 12f, Text);
			if (Sub.Length > 0) UiKit.DrawText(this, 1, font, new Vector2(6, cy + 20f), Fit(font, Sub, 13, Size.X - 12f), 13, UiKit.Dim, HorizontalAlignment.Center, Size.X - 12f, Sub);
			// Gerätetest: Zustandszeile unten in der Taste (vorher rechts außerhalb gezeichnet: „Com…“ am Bildrand)
			if (Detail.Length > 0) UiKit.DrawText(this, 2, font, new Vector2(6, Size.Y - 6), Fit(font, Detail, 12, Size.X - 12f), 12, DetailColor, HorizontalAlignment.Center, Size.X - 12f, Detail);
		}
		if (_state == KitButtonState.Busy)
		{
			// Prüfung Phase 28: bei linksbündiger Taste mit Zustandszeile (Waffenschacht) oben, sonst berührte „lädt nach“ den Balken
			float by = left && Detail.Length > 0 ? 0.5f : Size.Y - 8f;   // Runde 35c: ganz oben (die Zeilen stehen jetzt als Block mittig)
			float bh = left && Detail.Length > 0 ? 3f : 4f;
			var r = new Rect2(8, by, (Size.X - 16) * Mathf.Clamp(Progress, 0f, 1f), bh);
			DrawRect(new Rect2(8, by, Size.X - 16, bh), new Color(0, 0, 0, 0.5f));
			DrawRect(r, UiKit.Cyan);
		}
	}
}
