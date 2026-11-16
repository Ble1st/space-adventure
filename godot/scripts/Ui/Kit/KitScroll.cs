using System;
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

	/// <summary>
	/// Läuft gerade ein Wisch in irgendeinem Rollbereich — oder liegt ein Finger in einem rollbaren Bereich auf (Gerätetest Phase 27: ein Neuaufbau zwischen
	/// Aufsetzen und Ziehen ersetzte den Bereich, der erste Wisch von einer Taste aus rollte nicht)? Die Bereiche bauen sich dann nicht neu auf.
	/// </summary>
	public static bool Dragging => _active > 0 || _held > 0;
	/// <summary>Runde 37: läuft gerade ein echter Wisch (über die Totzone hinaus) — oder endete er in diesem Bild (Loslassen nach einem Wisch ist kein Tipp)?</summary>
	public static bool Swiping => _active > 0 || Engine.GetProcessFrames() <= _swipeEndFrame + 1;
	static ulong _swipeEndFrame;
	static int _active, _held;
	bool _holding;

	/// <summary>
	/// Gerätetest Phase 27: stabiler Schlüssel des Rollbereichs (Datei und Zeile, an der er angelegt wird) — die Rollposition wird nach einem Neuaufbau über
	/// ihn wiederhergestellt statt über die Baumreihenfolge (die sich verschiebt, wenn Zeilen oder Bereiche dazukommen oder wegfallen).
	/// </summary>
	public string ScrollKey { get; set; }
	/// <summary>Runde 37b: Rollposition wird vom Bildschirm selbst geführt (Leiste der Zentrale) — Capture/Restore lassen sie aus.</summary>
	public bool OwnPosition { get; set; }

	int _finger = -1;
	int _pending = -1, _pendingFrames;
	Vector2 _start;
	int _startScroll;
	bool _dragging;

	public KitScroll([System.Runtime.CompilerServices.CallerFilePath] string file = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
	{
		ScrollKey = $"{System.IO.Path.GetFileName(file)}:{line}";
		HorizontalScrollMode = ScrollMode.Disabled;
		MouseFilter = MouseFilterEnum.Pass;
		// Zweiter Gerätetest Phase 28: die gemerkte Position gleich nach dem Layout setzen (im selben Bild) — vorher stand die Liste nach jedem Neuaufbau
		// ein oder mehrere Bilder oben, die Handelsliste sprang sichtbar zwischen oben und gerollt
		SortChildren += ApplyPending;
		// Runde 37 (Prüfung: Detailkarten mit halb abgeschnittener letzter Zeile): Hinweis „es geht weiter“ — Verlauf und Pfeil am unteren Rand, solange
		// unten noch Inhalt liegt. Innerer Kindknoten (der Rollbereich ordnet ihn nicht an, er rollt nicht mit), fängt keine Eingaben
		_hint = new MoreHint { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
		AddChild(_hint, false, InternalMode.Back);
	}

	/// <summary>
	/// Runde 44h (Handy-Test r44 s02: Tasten lagen direkt an der Scrollleiste): durchsichtiger Abstand links an der senkrechten Leiste — die Leiste wird um
	/// so viel breiter, ihre Bilder (Grund, Griff) werden mit negativem Außenrand links schmal gezeichnet; der Inhalt (um die ganze Leistenbreite schmaler)
	/// endet so mit Abstand vor der sichtbaren Leiste. 0 = aus (Gegenprobe der UI-Prüfung: <c>--ui-check-nobargap</c>).
	/// </summary>
	public static float BarGap = Array.IndexOf(OS.GetCmdlineUserArgs(), "--ui-check-nobargap") >= 0 ? 0f : 6f;
	/// <summary>Runde 44h: tatsächlich gesetzter durchsichtiger Abstand dieser Leiste (UI-Prüfung: sichtbarer Teil = Leistenfläche minus Abstand).</summary>
	public float AppliedBarGap { get; private set; }

	public override void _Ready()
	{
		if (BarGap <= 0f || VerticalScrollMode is ScrollMode.ShowNever or ScrollMode.Disabled || GetVScrollBar() is not { } bar) return;
		foreach (var name in new[] { "scroll", "scroll_focus", "grabber", "grabber_highlight", "grabber_pressed" })
		{
			if (!bar.HasThemeStylebox(name)) continue;
			var sb = (StyleBox)bar.GetThemeStylebox(name).Duplicate();
			// die Leiste wird über den Innenrand ihres Grundes breiter (ScrollContainer misst die Mindestgröße der Leiste, nicht ihre CustomMinimumSize)
			if (name.StartsWith("scroll")) sb.ContentMarginLeft = MathF.Max(0f, sb.ContentMarginLeft) + BarGap;
			if (sb is StyleBoxFlat f) f.ExpandMarginLeft -= BarGap;
			else if (sb is StyleBoxTexture t) t.ExpandMarginLeft -= BarGap;
			bar.AddThemeStyleboxOverride(name, sb);
		}
		AppliedBarGap = BarGap;
		QueueSort();   // der Inhalt wurde evtl. schon mit der schmalen Leiste ausgelegt
	}

	readonly MoreHint _hint;
	/// <summary>Runde 37b: gewollte Rollposition — eine noch nicht angewandte (nach dem Layout) oder die aktuelle.</summary>
	public int WantedScroll => _pending >= 0 ? _pending : ScrollVertical;
	/// <summary>Runde 37: zeigt der Bereich gerade den Hinweis „weiter unten“?</summary>
	public bool MoreBelow => _hint.Visible;

	void UpdateHint()
	{
		bool more = CanScroll() && GetVScrollBar() is { } bar && ScrollVertical < bar.MaxValue - bar.Page - 2;
		if (more) { _hint.Position = new Vector2(0, Size.Y - 22f); _hint.Size = new Vector2(Size.X, 22f); }
		if (_hint.Visible != more) _hint.Visible = more;
	}

	/// <summary>Runde 37: Verlauf zum Hintergrund und ein kleiner Pfeil — ohne Text (zählt nicht als Schrift über Inhalt).</summary>
	sealed partial class MoreHint : Control
	{
		public override void _Draw()
		{
			var bg = new Color(0.02f, 0.04f, 0.09f);
			var cols = new[] { new Color(bg, 0f), new Color(bg, 0f), new Color(bg, 0.85f), new Color(bg, 0.85f) };
			DrawPolygon(new[] { new Vector2(0, 0), new Vector2(Size.X, 0), new Vector2(Size.X, Size.Y), new Vector2(0, Size.Y) }, cols);
			float cx = Size.X - 22f, y = Size.Y - 15f;
			DrawColoredPolygon(new[] { new Vector2(cx - 7, y), new Vector2(cx + 7, y), new Vector2(cx, y + 8) }, new Color(UiKit.Cyan, 0.9f));
		}
	}

	void ApplyPending()
	{
		if (_pending < 0 || !CanScroll()) return;
		ScrollVertical = _pending;
		if (ScrollVertical == _pending) _pending = -1;
	}

	/// <summary>Zweiter Gerätetest Phase 28: rollt auf <paramref name="v"/>, sobald das Layout steht (z. B. aufgeklappte Zeile sichtbar machen).</summary>
	public void ScrollTo(int v)
	{
		if (v <= 0) { _pending = -1; ScrollVertical = 0; return; }
		SetPending(v);
	}

	/// <summary>Rollpositionen aller Rollbereiche unter <paramref name="root"/> nach stabilem Schlüssel (mehrfache Schlüssel durchnummeriert) — vor einem Neuaufbau.</summary>
	public static Dictionary<string, int> Capture(Node root)
	{
		var map = new Dictionary<string, int>();
		var seen = new Dictionary<string, int>();
		void Walk(Node n)
		{
			foreach (var c in n.GetChildren())
			{
				if (c is KitScroll k && !k.IsQueuedForDeletion() && !k.OwnPosition)
				{
					int nth = seen.GetValueOrDefault(k.ScrollKey); seen[k.ScrollKey] = nth + 1;
					map[$"{k.ScrollKey}#{nth}"] = k._pending >= 0 ? k._pending : k.ScrollVertical;
				}
				Walk(c);
			}
		}
		Walk(root);
		return map;
	}

	/// <summary>Setzt die Rollpositionen nach dem Neuaufbau über den Schlüssel wieder (sobald das Layout steht).</summary>
	public static void Restore(Node root, Dictionary<string, int> positions)
	{
		var seen = new Dictionary<string, int>();
		void Walk(Node n)
		{
			foreach (var c in n.GetChildren())
			{
				if (c is KitScroll k && !k.IsQueuedForDeletion() && !k.OwnPosition)
				{
					int nth = seen.GetValueOrDefault(k.ScrollKey); seen[k.ScrollKey] = nth + 1;
					// Runde 37b (Handytest r37 N2): auch „ganz oben“ (0) wiederherstellen — vorher blieb eine ältere, gemerkte Position stehen und die Liste sprang zurück
					if (positions.TryGetValue($"{k.ScrollKey}#{nth}", out int v)) { if (v > 0) k.SetPending(v); else { k._pending = -1; k.ScrollVertical = 0; } }
				}
				Walk(c);
			}
		}
		Walk(root);
	}

	/// <summary>Selbsttest: Rollposition des ersten Bereichs mit diesem Schlüsselanfang unter <paramref name="root"/> (−1 = keiner).</summary>
	public static int PositionOf(Node root, string keyPrefix)
	{
		foreach (var c in root.GetChildren())
		{
			if (c is KitScroll k && !k.IsQueuedForDeletion() && k.ScrollKey.StartsWith(keyPrefix, System.StringComparison.Ordinal)) return k._pending >= 0 ? k._pending : k.ScrollVertical;
			int r = PositionOf(c, keyPrefix);
			if (r >= 0) return r;
		}
		return -1;
	}

	void SetPending(int v)
	{
		if (v <= 0) return;
		_pending = v; _pendingFrames = 0;
		ScrollVertical = v;   // gilt sofort, wenn das Layout schon steht; sonst im nächsten Bild
	}

	public override void _Process(double delta)
	{
		UpdateHint();
		if (_pending < 0) return;
		// Gerätetest Phase 28 (Startpaket-Liste sprang an den Anfang): warten, bis das Layout den Bereich wirklich rollbar macht (am Gerät dauert das länger als
		// 4 Bilder) — erst dann setzen; nach 60 Bildern aufgeben
		++_pendingFrames;
		if (CanScroll()) { ScrollVertical = _pending; if (ScrollVertical == _pending || _pendingFrames > 60) _pending = -1; }
		else if (_pendingFrames > 60) _pending = -1;
	}

	public override void _ExitTree()
	{
		if (_dragging) { _dragging = false; _active = Mathf.Max(0, _active - 1); }
		if (_holding) { _holding = false; _held = Mathf.Max(0, _held - 1); }
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
		if (!IsVisibleInTree()) { Release(); return; }
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
				if (_finger < 0 && ScreenRect().HasPoint(pos) && CanScroll())
				{
					_finger = idx; _start = pos; _startScroll = _pending >= 0 ? _pending : ScrollVertical; _dragging = false;
					if (!_holding) { _holding = true; _held++; }
				}
			}
			else if (idx == _finger) Release();
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
		// Runde 34b (Prüfung B4): dy ist in Bildpunkten, die Schicht kann skaliert sein — durch ihren Maßstab teilen, damit der Inhalt dem Finger 1:1 folgt
		float k = GetGlobalTransform().Scale.Y;
		ScrollVertical = Mathf.RoundToInt(_startScroll - dy / (k > 0.01f ? k : 1f));
		GetViewport().SetInputAsHandled();
	}

	void Release()
	{
		_finger = -1;
		if (_dragging) { _dragging = false; _active = Mathf.Max(0, _active - 1); _swipeEndFrame = Engine.GetProcessFrames(); }
		if (_holding) { _holding = false; _held = Mathf.Max(0, _held - 1); }
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
