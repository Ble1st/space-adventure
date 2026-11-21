using System;
using Godot;
using SpaceAdventure.Ui.Kit;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Hamburger-Menü (öffnet sich unten links): Kamera-Einstellungen (Zoom, freie Kamera oder Target-Lock, Schwenken
/// invertieren, Zurücksetzen) und die Systemaktionen (Karte, Wirtschaft, Speichern/Laden/Neues Spiel). Die Kommandozentrale
/// hat einen eigenen Knopf oben rechts im Flug-HUD. Die Kamera-Aktionen lassen das Menü offen, damit man das Ergebnis sieht.
/// </summary>
public partial class CommandMenu : Control, IScreen
{
	public string Id => "command";
	public string Title => "Menü";
	public Control Node => this;
	public bool IsOpen => Visible;

	/// <summary>
	/// Aktion eines Eintrags: "map", "economy", "save", "load", "new", "close" oder Kamera-Aktionen
	/// "zoom_in", "zoom_out", "cam_free", "cam_lock", "cam_invert", "cam_reset", sowie "emergency" (Notruf, Phase 4c).
	/// </summary>
	public event Action<string>? Action;

	// (Anzeige, Untertitel, Aktionsschlüssel)
	static readonly (string Label, string Sub, string Key)[] Entries =
	{
		("Kommando", "Kommandozentrale", "command"),   // HUD-Neu: vorher Taste oben rechts im Flug
		("Zeitraffer", "×1 · antippen = ×10", "fast"),  // HUD-Neu: vorher Taste „Zeit ×1“ im Flug
		("Karte", "Sternenkarte öffnen", "map"),
		("Wirtschaft", "Debug-Übersicht", "economy"),
		("Notruf", "Treibstoff zum Aufpreis bei leerem Tank", "emergency"),
		("Speichern", "", "save"),
		("Laden", "", "load"),
		("Neues Spiel", "", "new"),
		("Musik", "an/aus (Lautstärke: System)", "music_mute"),   // Runde 12
		("Effekte", "an/aus (Lautstärke: System)", "effects_mute"),
		("Kamera-Diagnose", "Zeile für den Gerätetest", "cam_debug"),   // Runde 16
		("UI-Größe", "klein / normal / groß", "ui_size"),   // Runde 31
		("Guthaben", "Debug: Spielerkasse auf 100.000.000 Cr", "money_cheat"),   // Runde 45: Nutzerwunsch, unter Einstellungen
	};

	readonly KitWindow _win;
	KitButton _free = null!, _lock = null!, _chase = null!, _invert = null!;
	/// <summary>Runde 44: Zeile „Höhe − / +“ der Rückansicht (nur in diesem Modus sichtbar).</summary>
	HBoxContainer _heightRow = null!;
	Label _heightText = null!;
	/// <summary>Selbsttest: Moduskasten „Rückansicht“ und die Höhe-Tasten.</summary>
	public KitButton TestChaseButton => _chase;
	public KitButton? TestHeightUp { get; private set; }
	public bool TestHeightRowVisible => _heightRow.Visible;
	KitButton? _emergency, _music, _effects, _uiSize, _fast, _command;
	/// <summary>HUD-Neu: Zeitraffer-Zeile zeigt den Zustand.</summary>
	public void SetFast(bool on) { if (_fast is null) return; _fast.Text = on ? "Zeitraffer ×10" : "Zeitraffer"; _fast.Sub = on ? "läuft · antippen = ×1" : "×1 · antippen = ×10"; _fast.State = on ? KitButtonState.On : KitButtonState.Normal; }
	/// <summary>HUD-Neu: ungelesene Comms an der Zeile „Kommando“.</summary>
	public void SetCommsBadge(int unread) { if (_command is null) return; _command.Sub = unread > 0 ? $"Kommandozentrale · Comms: {unread}" : "Kommandozentrale"; }

	/// <summary>Runde 12: Anzeige der Ton-Schalter (stumm = normal, an = grün).</summary>
	public void SetAudioState()
	{
		if (_music is not null) { _music.Text = SpaceAdventure.Audio.AudioSettings.MusicText; _music.State = SpaceAdventure.Audio.AudioSettings.MusicMuted ? KitButtonState.Normal : KitButtonState.On; }
		if (_uiSize is not null) { _uiSize.Text = UiScale.Text; _uiSize.Sub = UiScale.SubText; _uiSize.QueueRedraw(); }   // Runde 31; Runde 33: kurz, Faktor/mm in der Unterzeile
		if (_effects is not null) { _effects.Text = SpaceAdventure.Audio.AudioSettings.EffectsText; _effects.State = SpaceAdventure.Audio.AudioSettings.EffectsMuted ? KitButtonState.Normal : KitButtonState.On; }
	}

	/// <summary>Phase 14: Notruf nur mit eingeschaltetem Treibstoff anzeigen.</summary>
	public void SetFuelActive(bool on) { if (_emergency is not null && _emergency.Visible != on) { _emergency.Visible = on; LayoutSystemRows(); } }

	readonly System.Collections.Generic.List<KitButton> _systemButtons = new();
	KitScroll _scroll = null!;
	readonly System.Collections.Generic.Dictionary<string, KitButton> _byKey = new();
	KitButton? _holdRow;
	float _holdOffset;
	int _holdFrames;

	/// <summary>
	/// Runde 38a (Handy-Abschlusstest v4 → v7: beim Wechsel der UI-Größe sprang die Rollposition, der nächste Tipp traf ein anderes Element): die Zeile dieser
	/// Taste bleibt an derselben Stelle im sichtbaren Teil der Liste (Abstand zum oberen Rand der Liste in logischen Punkten), auch wenn die Schicht neu
	/// skaliert und das Fenster eine andere Höhe bekommt.
	/// </summary>
	public void HoldRow(string key)
	{
		if (!_byKey.TryGetValue(key, out var b) || !b.IsInsideTree()) return;
		_holdRow = b;
		_holdOffset = b.GetGlobalRect().Intersection(_scroll.GetGlobalRect()).GetCenter().Y;   // Bildschirmlage der sichtbaren Mitte (Bildpunkte): der nächste Tipp dort soll dieselbe Zeile treffen
		_holdFrames = 8;
	}
	/// <summary>Selbsttest: Abstand der Taste zum oberen Listenrand in logischen Punkten.</summary>
	public float TestRowOffset(string key) => _byKey.TryGetValue(key, out var b) && b.IsInsideTree()
		? (b.GetGlobalRect().Position.Y - _scroll.GetGlobalRect().Position.Y) / MathF.Max(0.01f, b.GetGlobalTransform().Scale.Y) : float.NaN;
	public KitScroll TestScroll => _scroll;

	public override void _Process(double delta)
	{
		if (_holdRow is null || _holdFrames <= 0) return;
		_holdFrames--;
		// Lage der Taste im Inhalt der Liste (logisch) minus gewünschter Abstand zum oberen Listenrand = Rollposition; der Abstand folgt aus der alten
		// Bildschirmlage, im sichtbaren Teil der Liste gehalten
		float k = MathF.Max(0.01f, _holdRow.GetGlobalTransform().Scale.Y);
		float inContent = (_holdRow.GetGlobalRect().Position.Y - _list.GetGlobalRect().Position.Y) / k;
		float view = _scroll.GetGlobalRect().Size.Y / k, rowH = _holdRow.GetGlobalRect().Size.Y / k;
		float offset = Mathf.Clamp((_holdOffset - _scroll.GetGlobalRect().Position.Y) / k - rowH / 2f, 0f, MathF.Max(0f, view - rowH));
		int want = Math.Max(0, (int)MathF.Round(inContent - offset));
		if (_scroll.ScrollVertical != want) _scroll.ScrollVertical = want;
		if (_holdFrames == 0) _holdRow = null;
	}
	readonly System.Collections.Generic.List<HBoxContainer> _systemRows = new();
	VBoxContainer _list = null!;

	/// <summary>Runde 37: Systemtasten zu zweit je Zeile — nur die sichtbaren (ausgeblendeter „Notruf“ hinterlässt keine halbe Zeile).</summary>
	void LayoutSystemRows()
	{
		foreach (var r in _systemRows) { foreach (var c in r.GetChildren()) r.RemoveChild(c); _list.RemoveChild(r); r.QueueFree(); }
		_systemRows.Clear();
		HBoxContainer? row = null;
		foreach (var b in _systemButtons)
		{
			if (b.GetParent() is { } old) old.RemoveChild(b);
			if (!b.Visible) { _hidden.AddChild(b); continue; }
			if (row is null || row.GetChildCount() >= 2)
			{
				row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 52), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };   // Runde 44h: Breite aus der Liste
				row.AddThemeConstantOverride("separation", 10);
				_list.AddChild(row);
				_systemRows.Add(row);
			}
			row.AddChild(b);
		}
	}
	/// <summary>Halter der ausgeblendeten Systemtasten (nicht im Baum der Liste).</summary>
	readonly Control _hidden = new() { Visible = false };
	/// <summary>Runde 44h: Abstand des Listeninhalts zur senkrechten Scrollleiste.</summary>
	public const int ScrollbarGap = 10;
	/// <summary>Selbsttest: Zahl der Tasten je Systemzeile.</summary>
	public System.Collections.Generic.List<int> TestSystemRows => _systemRows.ConvertAll(r => r.GetChildCount());
	/// <summary>Selbsttest: ist der Menüpunkt „Notruf“ sichtbar?</summary>
	public bool TestEmergencyVisible => _emergency?.Visible ?? false;
	/// <summary>Runde 33 (Handytest): Menüzeile „UI-Größe“ — Text, Unterzeile, Überlauf (leer = passt) und Bildschirmmitte für einen echten Tipp.</summary>
	public KitButton? TestUiSizeButton => _uiSize;
	/// <summary>Runde 34b: Fenster in Bildpunkten (der Hinweis steht links daneben).</summary>
	public Rect2? WindowGlobal => Visible ? _win.GetGlobalRect() : null;
	Label _zoomText = null!, _modeHint = null!;

	public CommandMenu()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		_win = new KitWindow("Menü", new Vector2(580, 688)) { Position = new Vector2(350, 16) };
		AddChild(_win);
		AddChild(_hidden);
		var scroll = new KitScroll { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 8);

		// ── Kamera ──
		list.AddChild(Text("KAMERA", 15, Cyan));
		var zoomRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 56) };
		zoomRow.AddThemeConstantOverride("separation", 10);
		// Runde 44 (Nutzer: „−“ heraus / „+“ heran war verwechselbar): eindeutige Beschriftung mit Unterzeile
		var zoomOut = new KitButton("Weiter weg", new Vector2(130, 56)) { FontSize = 17, Sub = "Zoom heraus" };
		_zoomText = Text("Zoom", 20, White, HorizontalAlignment.Center);
		_zoomText.CustomMinimumSize = new Vector2(150, 56);
		_zoomText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_zoomText.VerticalAlignment = VerticalAlignment.Center;
		var zoomIn = new KitButton("Näher", new Vector2(130, 56)) { FontSize = 17, Sub = "Zoom heran" };
		zoomOut.Pressed += () => Action?.Invoke("zoom_out");
		zoomIn.Pressed += () => Action?.Invoke("zoom_in");
		zoomRow.AddChild(zoomOut); zoomRow.AddChild(_zoomText); zoomRow.AddChild(zoomIn);
		list.AddChild(zoomRow);

		var modeRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 56) };
		modeRow.AddThemeConstantOverride("separation", 10);
		// Runde 44: drei Modi — frei / Zielfolge / Rückansicht
		_free = new KitButton("Frei", new Vector2(150, 56)) { FontSize = 18, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_lock = new KitButton("Zielfolge", new Vector2(150, 56)) { FontSize = 18, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_chase = new KitButton("Rückansicht", new Vector2(150, 56)) { FontSize = 18, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_free.Pressed += () => Action?.Invoke("cam_free");
		_lock.Pressed += () => Action?.Invoke("cam_lock");
		_chase.Pressed += () => Action?.Invoke("cam_chase");
		modeRow.AddChild(_free); modeRow.AddChild(_lock); modeRow.AddChild(_chase);
		list.AddChild(modeRow);
		_heightRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 56), Visible = false };
		_heightRow.AddThemeConstantOverride("separation", 10);
		var hDown = new KitButton("Tiefer", new Vector2(130, 56)) { FontSize = 17, Sub = "Höhe −" };
		_heightText = Text("Höhe", 20, White, HorizontalAlignment.Center);
		_heightText.CustomMinimumSize = new Vector2(150, 56);
		_heightText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_heightText.VerticalAlignment = VerticalAlignment.Center;
		var hUp = new KitButton("Höher", new Vector2(130, 56)) { FontSize = 17, Sub = "Höhe +" };
		hDown.Pressed += () => Action?.Invoke("chase_down");
		hUp.Pressed += () => Action?.Invoke("chase_up");
		TestHeightUp = hUp;
		_heightRow.AddChild(hDown); _heightRow.AddChild(_heightText); _heightRow.AddChild(hUp);
		list.AddChild(_heightRow);
		_modeHint = Text("", 13, new Color(Dim, 0.85f));
		_modeHint.CustomMinimumSize = new Vector2(0, 36);
		_modeHint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_modeHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		list.AddChild(_modeHint);

		var miscRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 56) };
		miscRow.AddThemeConstantOverride("separation", 10);
		_invert = new KitButton("Schwenken invertieren", new Vector2(290, 56)) { FontSize = 17, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.5f };
		var reset = new KitButton("Zurücksetzen", new Vector2(170, 56)) { FontSize = 17, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_invert.Pressed += () => Action?.Invoke("cam_invert");
		reset.Pressed += () => Action?.Invoke("cam_reset");
		miscRow.AddChild(_invert); miscRow.AddChild(reset);
		list.AddChild(miscRow);

		// ── System ──
		list.AddChild(Text("SYSTEM", 15, Cyan));
		// Gerätetest Phase 28: zwei Tasten je Zeile — „Neues Spiel“ ist ohne Rollen erreichbar
		// Runde 37 (Handytest r35: „Speichern“ stand allein in einer Zeile — der ausgeblendete „Notruf“ belegte ihren Nachbarplatz): die Zeilen werden aus den
		// sichtbaren Tasten gelegt (LayoutSystemRows), ausgeblendete zählen nicht
		foreach (var (label, sub, key) in Entries)
		{
			var b = new KitButton(label, new Vector2(240, 52)) { AlignLeft = true, FontSize = 19, Sub = sub, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			string k = key;
			b.Pressed += () => Action?.Invoke(k);
			if (k == "emergency") _emergency = b;
			if (k == "music_mute") _music = b;
			if (k == "effects_mute") _effects = b;
			if (k == "ui_size") _uiSize = b;
			if (k == "fast") _fast = b;
			if (k == "command") _command = b;
			_byKey[k] = b;
			_systemButtons.Add(b);
		}
		_list = list;
		LayoutSystemRows();
		// Runde 44h (Handy-Test r44 s02: „Rückansicht“/„Zurücksetzen“ lagen an der Scrollleiste): Inhalt mit Abstand zur Leiste, Tasten füllen die
		// verfügbare Breite statt fester 540 (die Leiste nimmt der Liste die Breite)
		var pad = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		pad.AddThemeConstantOverride("margin_right", ScrollbarGap);
		pad.AddChild(list);
		scroll.AddChild(pad);
		_scroll = scroll;
		_win.Content.AddChild(scroll);
		var close = new KitButton("Schließen", new Vector2(540, 56)) { FontSize = 20 };
		close.Pressed += () => Action?.Invoke("close");
		_win.Content.AddChild(close);
	}

	/// <summary>Zeigt den aktuellen Kamerazustand (Modus, Invertierung, Abstand); wird vor dem Öffnen und nach jeder Kamera-Aktion gesetzt.</summary>
	public void SetCameraState(SpaceSim.Core.Flight.CameraMode mode, bool invert, float distance, bool hasTarget, float chaseHeight, float chaseBack)
	{
		bool targetLock = mode == SpaceSim.Core.Flight.CameraMode.TargetLock, chase = mode == SpaceSim.Core.Flight.CameraMode.Chase;
		_free.State = mode == SpaceSim.Core.Flight.CameraMode.Free ? KitButtonState.On : KitButtonState.Normal;   // aktiver Modus grün hervorgehoben
		_lock.State = targetLock ? KitButtonState.On : KitButtonState.Normal;
		_chase.State = chase ? KitButtonState.On : KitButtonState.Normal;
		_invert.State = invert ? KitButtonState.On : KitButtonState.Normal;
		_heightRow.Visible = chase;
		_zoomText.Text = chase ? $"Abstand {chaseBack:0}" : $"Zoom · Abstand {distance:0}";
		_heightText.Text = $"Höhe {chaseHeight:0}";
		_modeHint.Text = chase
			? "Rückansicht: Die Kamera folgt dem Kurs hinter dem Bug. Senkrecht wischen ändert die Höhe, waagrecht wirkt nicht."
			: targetLock
			? (hasTarget ? "Target-Lock: Die Kamera dreht sich hinter das Schiff, sodass das gewählte Ziel vor dir liegt. Neigung und Zoom bleiben frei."
			             : "Target-Lock: Noch kein Ziel gewählt – tippe eine Station, ein Gate oder einen Asteroiden an. Bis dahin bleibt die Kamera stehen.")
			: "Freie Kamera: Mit einem Finger wischen schwenkt, mit zwei Fingern zoomt (Mausrad am Desktop).";
	}

	public void Open()
	{
		Visible = true; LayoutWindow(); QueueRedraw();
		_openFrame = Engine.GetProcessFrames(); _backdropFinger = -1;
		InputLog.Log($"menü AUF (Aufrufer {InputLog.Caller()})");
	}
	ulong _openFrame;
	int _backdropFinger = -1;

	/// <summary>
	/// Runde 37 (UI-ANPASSUNG.md E5): das Menü liegt in der Menü-Schicht mit dem UI-Faktor — Fenster mittig, höchstens 688 hoch und nie höher als die
	/// Fläche (die Liste rollt; „Schließen“ bleibt unten im Fenster). Vorher fest 580 × 688 bei (350, 16) in einer 1280 breiten Mitte.
	/// </summary>
	void LayoutWindow()
	{
		if (Size.X <= 0f || Size.Y <= 0f) return;
		// Runde 37c (Prüfung M3): innerhalb der sicheren Fläche
		var si = UiScale.Div(UiScale.SafeInsets1(), IsInsideTree() ? MathF.Max(1f, GetGlobalTransform().Scale.X) : 1f);
		float uw = Size.X - si.Left - si.Right, uh = Size.Y - si.Top - si.Bottom;
		float h = MathF.Min(688f, uh - 24f);
		_win.CustomMinimumSize = new Vector2(580, h);
		_win.Size = new Vector2(580, h);
		_win.Position = new Vector2(si.Left + MathF.Max(8f, (uw - 580f) / 2f), si.Top + MathF.Max(8f, (uh - h) / 2f));
	}

	public override void _Notification(int what)
	{
		if (what == NotificationResized) LayoutWindow();
	}
	public void Close()
	{
		if (Visible) InputLog.Log($"menü ZU (Aufrufer {InputLog.Caller()})");
		Visible = false; _backdropFinger = -1;
	}
	public void Refresh() { }

	public override void _Draw()
	{
		if (Visible) DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.02f, 0.05f, 0.45f));
	}

	/// <summary>
	/// Ein Tipp auf den abgedunkelten Hintergrund schließt das Menü — ein ganzer Tipp: Drücken und Loslassen desselben Fingers außerhalb des Fensters, das
	/// Drücken in einem späteren Bild als das Öffnen. Zweiter Handytest A15 („Menü“ öffnete in 0 von 15 adb-Tipps): vorher schloss schon das Drücken. Ging
	/// das Loslassen eines Tipps verloren („dropping inconsistent event“), gibt Godots Android-Eingabe beim nächsten Tipp erst das alte Loslassen (die Taste
	/// „Menü“ löst aus, das Menü öffnet) und im selben Bild das neue Drücken — das landete auf dem Hintergrund und schloss das Menü sofort wieder.
	/// </summary>
	public override void _GuiInput(InputEvent @event)
	{
		if (!Pointer.TryRead(@event, out int idx, out var pos, out bool pressed, out bool motion) || motion) return;
		bool outside = !new Rect2(_win.Position, _win.Size).HasPoint(pos);
		if (pressed)
		{
			if (outside && Engine.GetProcessFrames() > _openFrame) _backdropFinger = idx;
			else if (outside) InputLog.Log($"menü: Drücken idx{idx} auf dem Hintergrund im Öffnungsbild ignoriert");
			AcceptEvent();
			return;
		}
		if (idx == _backdropFinger)
		{
			_backdropFinger = -1;
			if (outside && !Pointer.Canceled(@event)) { InputLog.Log($"menü: Tipp auf den Hintergrund idx{idx} schließt"); Action?.Invoke("close"); }
		}
		AcceptEvent();
	}
}
