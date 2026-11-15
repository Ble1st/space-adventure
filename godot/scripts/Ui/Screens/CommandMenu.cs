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
	};

	readonly KitWindow _win;
	KitButton _free = null!, _lock = null!, _invert = null!;
	KitButton? _emergency, _music, _effects, _uiSize;

	/// <summary>Runde 12: Anzeige der Ton-Schalter (stumm = normal, an = grün).</summary>
	public void SetAudioState()
	{
		if (_music is not null) { _music.Text = SpaceAdventure.Audio.AudioSettings.MusicText; _music.State = SpaceAdventure.Audio.AudioSettings.MusicMuted ? KitButtonState.Normal : KitButtonState.On; }
		if (_uiSize is not null) { _uiSize.Text = UiScale.Text; _uiSize.Sub = UiScale.SubText; _uiSize.QueueRedraw(); }   // Runde 31; Runde 33: kurz, Faktor/mm in der Unterzeile
		if (_effects is not null) { _effects.Text = SpaceAdventure.Audio.AudioSettings.EffectsText; _effects.State = SpaceAdventure.Audio.AudioSettings.EffectsMuted ? KitButtonState.Normal : KitButtonState.On; }
	}

	/// <summary>Phase 14: Notruf nur mit eingeschaltetem Treibstoff anzeigen.</summary>
	public void SetFuelActive(bool on) { if (_emergency is not null) _emergency.Visible = on; }
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
		var scroll = new KitScroll { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 8);

		// ── Kamera ──
		list.AddChild(Text("KAMERA", 15, Cyan));
		var zoomRow = new HBoxContainer { CustomMinimumSize = new Vector2(540, 56) };
		zoomRow.AddThemeConstantOverride("separation", 10);
		var zoomOut = new KitButton("−", new Vector2(110, 56)) { FontSize = 30 };
		_zoomText = Text("Zoom", 20, White, HorizontalAlignment.Center);
		_zoomText.CustomMinimumSize = new Vector2(290, 56);
		_zoomText.VerticalAlignment = VerticalAlignment.Center;
		var zoomIn = new KitButton("+", new Vector2(110, 56)) { FontSize = 30 };
		zoomOut.Pressed += () => Action?.Invoke("zoom_out");
		zoomIn.Pressed += () => Action?.Invoke("zoom_in");
		zoomRow.AddChild(zoomOut); zoomRow.AddChild(_zoomText); zoomRow.AddChild(zoomIn);
		list.AddChild(zoomRow);

		var modeRow = new HBoxContainer { CustomMinimumSize = new Vector2(540, 56) };
		modeRow.AddThemeConstantOverride("separation", 10);
		_free = new KitButton("Freie Kamera", new Vector2(265, 56)) { FontSize = 18 };
		_lock = new KitButton("Target-Lock", new Vector2(265, 56)) { FontSize = 18 };
		_free.Pressed += () => Action?.Invoke("cam_free");
		_lock.Pressed += () => Action?.Invoke("cam_lock");
		modeRow.AddChild(_free); modeRow.AddChild(_lock);
		list.AddChild(modeRow);
		_modeHint = Text("", 13, new Color(Dim, 0.85f));
		_modeHint.CustomMinimumSize = new Vector2(540, 36);
		_modeHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		list.AddChild(_modeHint);

		var miscRow = new HBoxContainer { CustomMinimumSize = new Vector2(540, 56) };
		miscRow.AddThemeConstantOverride("separation", 10);
		_invert = new KitButton("Schwenken invertieren", new Vector2(320, 56)) { FontSize = 17 };
		var reset = new KitButton("Zurücksetzen", new Vector2(210, 56)) { FontSize = 17 };
		_invert.Pressed += () => Action?.Invoke("cam_invert");
		reset.Pressed += () => Action?.Invoke("cam_reset");
		miscRow.AddChild(_invert); miscRow.AddChild(reset);
		list.AddChild(miscRow);

		// ── System ──
		list.AddChild(Text("SYSTEM", 15, Cyan));
		// Gerätetest Phase 28: zwei Tasten je Zeile — „Neues Spiel“ ist ohne Rollen erreichbar
		HBoxContainer? row = null;
		foreach (var (label, sub, key) in Entries)
		{
			if (row is null || row.GetChildCount() >= 2)
			{
				row = new HBoxContainer { CustomMinimumSize = new Vector2(540, 52) };
				row.AddThemeConstantOverride("separation", 10);
				list.AddChild(row);
			}
			var b = new KitButton(label, new Vector2(265, 52)) { AlignLeft = true, FontSize = 19, Sub = sub };
			string k = key;
			b.Pressed += () => Action?.Invoke(k);
			if (k == "emergency") _emergency = b;
			if (k == "music_mute") _music = b;
			if (k == "effects_mute") _effects = b;
			if (k == "ui_size") _uiSize = b;
			row.AddChild(b);
		}
		scroll.AddChild(list);
		_win.Content.AddChild(scroll);
		var close = new KitButton("Schließen", new Vector2(540, 56)) { FontSize = 20 };
		close.Pressed += () => Action?.Invoke("close");
		_win.Content.AddChild(close);
	}

	/// <summary>Zeigt den aktuellen Kamerazustand (Modus, Invertierung, Abstand); wird vor dem Öffnen und nach jeder Kamera-Aktion gesetzt.</summary>
	public void SetCameraState(bool targetLock, bool invert, float distance, bool hasTarget)
	{
		_free.State = targetLock ? KitButtonState.Normal : KitButtonState.On;   // aktiver Modus grün hervorgehoben
		_lock.State = targetLock ? KitButtonState.On : KitButtonState.Normal;
		_invert.State = invert ? KitButtonState.On : KitButtonState.Normal;
		_zoomText.Text = $"Zoom · Abstand {distance:0}";
		_modeHint.Text = targetLock
			? (hasTarget ? "Target-Lock: Die Kamera dreht sich hinter das Schiff, sodass das gewählte Ziel vor dir liegt. Neigung und Zoom bleiben frei."
			             : "Target-Lock: Noch kein Ziel gewählt – tippe eine Station, ein Gate oder einen Asteroiden an. Bis dahin bleibt die Kamera stehen.")
			: "Freie Kamera: Mit einem Finger wischen schwenkt, mit zwei Fingern zoomt (Mausrad am Desktop).";
	}

	public void Open() { Visible = true; QueueRedraw(); }
	public void Close() => Visible = false;
	public void Refresh() { }

	public override void _Draw()
	{
		if (Visible) DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.02f, 0.05f, 0.45f));
	}

	/// <summary>Klick auf den abgedunkelten Hintergrund schließt das Menü.</summary>
	public override void _GuiInput(InputEvent @event)
	{
		if (Pointer.TryRead(@event, out _, out var pos, out bool pressed, out bool motion) && !motion && pressed)
		{
			if (!new Rect2(_win.Position, _win.Size).HasPoint(pos)) Action?.Invoke("close");
			AcceptEvent();
		}
	}
}
