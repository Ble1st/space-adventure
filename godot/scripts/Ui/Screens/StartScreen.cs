using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// UI-Umbau (Nutzerwunsch „eigener Startbildschirm, Neues Spiel hat im aktiven Spiel nichts zu suchen“): eigener Bildschirm beim App-Start und über
/// „Zum Hauptmenü“ — Titel, links Fortsetzen / Neues Spiel / Einstellungen / Beenden, rechts die Startpaket-Karten bzw. die Einstellungen. Halbdurchsichtig
/// über der (ruhenden) Welt. Aktionen gehen als Schlüssel an Main: "continue", "new:&lt;Paket&gt;", "quit".
/// </summary>
public sealed partial class StartScreen : Control
{
	public event Action<string>? Action;
	enum Panel { Packages, Settings }
	Panel _panel = Panel.Packages;
	bool _showTests;
	bool _dirty = true;
	readonly Dictionary<string, KitButton> _startButtons = new();
	KitScroll? _scroll;
	static SpaceSim.Core.Content.WorldDef? _defaultWorld;

	public StartScreen()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
	}

	public void Open() { Visible = true; _panel = Panel.Packages; _dirty = true; }
	public void Close() { Visible = false; }
	/// <summary>ui-check/Selbsttest: Einstellungen rechts zeigen.</summary>
	public void ShowSettings() { _panel = Panel.Settings; _dirty = true; }
	public void Invalidate() => _dirty = true;

	public override void _Notification(int what) { if (what == NotificationResized) _dirty = true; }

	public override void _Process(double delta)
	{
		if (!Visible || !_dirty || Size.X <= 0f) return;
		_dirty = false;
		Rebuild();
	}

	public override void _Draw()
	{
		// Verlauf: links dunkel (Text lesbar), rechts sieht man die Welt
		var w = Size.X; var h = Size.Y;
		DrawPolygon(new[] { Vector2.Zero, new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) },
			new[] { new Color(0.01f, 0.02f, 0.05f, 0.85f), new Color(0.01f, 0.02f, 0.05f, 0.25f), new Color(0.01f, 0.02f, 0.05f, 0.25f), new Color(0.01f, 0.02f, 0.05f, 0.85f) });
	}

	void Rebuild()
	{
		foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
		_startButtons.Clear();
		QueueRedraw();
		var si = UiScale.Div(UiScale.SafeInsets1(), IsInsideTree() ? MathF.Max(1f, GetGlobalTransform().Scale.X) : 1f);
		float x0 = si.Left + 40f, y0 = si.Top + 30f, w = Size.X - si.Left - si.Right, h = Size.Y - si.Top - si.Bottom;
		var title = At(this, Text("SPACE ADVENTURE", 50, White), x0, y0, 0, 0);
		title.AddThemeConstantOverride("outline_size", 0);
		At(this, Text("Elf Sektoren · eine Wirtschaft, die ohne dich weiterläuft", 17, Dim), x0 + 4, y0 + 64, 0, 0);

		float bx = x0, by = y0 + 120, bw = 360;
		// Mehrere Spielstände: „Fortsetzen“ lädt den neuesten Stand (Autosave oder benannt), „Laden“ öffnet die Liste
		var newest = Game.SaveService.Newest;
		if (newest is { } nm)
		{
			var cont = At(this, new KitButton("Fortsetzen", new Vector2(bw, 76)) { FontSize = 24, AlignLeft = true, State = KitButtonState.Ready, Sub = nm.Name + " · " + nm.SavedAt.ToLocalTime().ToString("dd.MM. HH:mm") }, bx, by);
			cont.Pressed += () => Action?.Invoke("continue");
			TestContinue = cont;
			by += 88;
			var load = At(this, new KitButton("Laden", new Vector2(bw, 64)) { FontSize = 21, AlignLeft = true, Sub = "Spielstand wählen" }, bx, by);
			load.Pressed += () => Action?.Invoke("loadlist");
			TestLoad = load;
			by += 76;
		}
		else { TestContinue = null; TestLoad = null; }
		var nw = At(this, new KitButton("Neues Spiel", new Vector2(bw, 64)) { FontSize = 21, AlignLeft = true, State = _panel == Panel.Packages ? KitButtonState.On : KitButtonState.Normal, Sub = "Startpaket wählen" }, bx, by);
		nw.Pressed += () => { _panel = Panel.Packages; _dirty = true; };
		by += 76;
		var st = At(this, new KitButton("Einstellungen", new Vector2(bw, 64)) { FontSize = 21, AlignLeft = true, State = _panel == Panel.Settings ? KitButtonState.On : KitButtonState.Normal, Sub = "Ton · Bedienung · Autosave" }, bx, by);
		st.Pressed += () => { _panel = Panel.Settings; _dirty = true; };
		TestSettings = st;
		by += 76;
		var quit = At(this, new KitButton("Beenden", new Vector2(bw, 56)) { FontSize = 19, AlignLeft = true }, bx, by);
		quit.Pressed += () => Action?.Invoke("quit");

		// rechte Fläche
		float px = bx + bw + 40f, py = y0 + 120, pw = MathF.Max(380f, w + si.Left - px - 30f), ph = MathF.Max(300f, h + si.Top - py - 24f);
		var panel = CardBg(this, px, py, pw, ph, new Color(0.03f, 0.06f, 0.12f, 0.72f), new Color(Cyan, 0.45f), 14, 2);
		if (_panel == Panel.Packages) BuildPackages(panel, pw, ph); else BuildSettings(panel, pw, ph);
	}

	void BuildPackages(Control panel, float pw, float ph)
	{
		At(panel, Text("Wie willst du anfangen?", 24, Cyan), 20, 14, pw - 40, 32);
		var def = _defaultWorld ??= SpaceSim.Core.Content.WorldBuilder.Parse(SpaceSim.Core.Content.WorldBuilder.DefaultWorldJson());
		string ModelName(string key) => def.ShipModels.Find(m => m.Key == key)?.Name ?? key;
		var scroll = At(panel, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 14, 56, pw - 28, ph - 64);
		_scroll = scroll;
		var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		box.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(box);
		float cw = pw - 44f;
		// Hilfe und Tutorial: das Tutorial (Szenario mit tutorial = true) steht vorn bei den regulären Paketen, nicht unter den Testszenarien
		var regular = def.Player.StartOptions.Where(o => o.Scenario is { Tutorial: true }).Concat(def.Player.StartOptions.Where(o => o.Scenario is null)).ToList();
		var tests = def.Player.StartOptions.Where(o => o.Scenario is { Tutorial: false }).ToList();
		void Card(SpaceSim.Core.Content.StartOptionDef o, bool test)
		{
			string titleText = $"{o.Name} · {ModelName(o.Model)} · {o.Credits:0} Cr";
			int lines = Math.Max(2, Wrap(o.Description, 15, cw - 210f).Count);
			int tl = Math.Max(1, Wrap(titleText, 20, cw - 200f).Count);
			float ch = Math.Max(96f, 44f + (tl - 1) * 26f + lines * 20f);
			var holder = new Control { CustomMinimumSize = new Vector2(cw, ch), MouseFilter = MouseFilterEnum.Pass };
			box.AddChild(holder);
			var card = CardBg(holder, 0, 0, cw, ch, new Color(0.04f, 0.08f, 0.16f, 0.9f), new Color(Edge, 0.6f), 10, 1);
			float ty = WrappedText(card, titleText, 16, 8, cw - 200, 20, test ? Dim : White, 26);
			WrappedText(card, o.Description, 16, ty + 4, cw - 210, 15, Dim, 20);
			var b = At(card, new KitButton("Starten", new Vector2(160, 60)) { FontSize = 19, State = test ? KitButtonState.Normal : KitButtonState.Ready }, cw - 174, (ch - 60f) / 2f);
			string key = o.Key;
			_startButtons[key] = b;
			b.Pressed += () => Action?.Invoke("new:" + key);
		}
		foreach (var o in regular) Card(o, false);
		if (tests.Count > 0)
		{
			var tog = new KitButton(_showTests ? $"▾ Testszenarien ({tests.Count})" : $"▸ Testszenarien ({tests.Count}) — nur zum Prüfen", new Vector2(cw, 52)) { FontSize = 16, AlignLeft = true };
			tog.Pressed += () => { _showTests = !_showTests; _dirty = true; };
			box.AddChild(tog);
			TestScenarioToggle = tog;
			if (_showTests) foreach (var o in tests) Card(o, true);
		}
	}

	void BuildSettings(Control panel, float pw, float ph)
	{
		At(panel, Text("Einstellungen", 24, Cyan), 20, 14, pw - 40, 32);
		// Mehrere Spielstände: mit dem Autosave-Block rollt der Inhalt (bei großer UI höher als die Fläche)
		var scroll = At(panel, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 0, 52, pw - 4, ph - 56);
		panel = new Control { MouseFilter = MouseFilterEnum.Pass };
		scroll.AddChild(panel);
		float y = 8;
		At(panel, Text("Ton", 19, White), 20, y, 200, 28); y += 34;
		SettingsRow(panel, ref y, Audio.AudioSettings.MusicText, Audio.AudioSettings.MusicMuted, () => Audio.AudioSettings.ChangeMusic(-Audio.AudioSettings.Step), () => Audio.AudioSettings.ChangeMusic(Audio.AudioSettings.Step), Audio.AudioSettings.ToggleMusicMute);
		SettingsRow(panel, ref y, Audio.AudioSettings.EffectsText, Audio.AudioSettings.EffectsMuted, () => Audio.AudioSettings.ChangeEffects(-Audio.AudioSettings.Step), () => Audio.AudioSettings.ChangeEffects(Audio.AudioSettings.Step), Audio.AudioSettings.ToggleEffectsMute);
		y += 10;
		At(panel, Text("Bedienung (Tastengröße)", 19, White), 20, y, 300, 28); y += 34;
		float bx = 20;
		foreach (var size in new[] { SpaceSim.Core.Ui.UiSize.Small, SpaceSim.Core.Ui.UiSize.Normal, SpaceSim.Core.Ui.UiSize.Large })
		{
			var b = At(panel, new KitButton(UiScale.Name(size), new Vector2(130, 60)) { FontSize = 18, State = UiScale.Size == size ? KitButtonState.On : KitButtonState.Normal }, bx, y);
			var sz = size;
			b.Pressed += () => { UiScale.Set(sz); _dirty = true; };
			bx += 138;
		}
		y += 70;
		y = WrappedText(panel, UiScale.Describe(), 20, y, pw - 60, 15, Dim, 20) + 12;
		// Mehrere Spielstände: Autosave-Intervall (gespeichert in den Einstellungen)
		At(panel, Text("Autosave", 19, White), 20, y, 300, 28); y += 34;
		bx = 20;
		_autosaveButtons.Clear();
		foreach (int sec in SpaceSim.Core.Persistence.SaveSlots.AutosaveOptions)
		{
			var b = At(panel, new KitButton(SpaceSim.Core.Persistence.SaveSlots.AutosaveText(sec), new Vector2(110, 60)) { FontSize = 18, State = Game.SaveSettings.AutosaveSeconds == sec ? KitButtonState.On : KitButtonState.Normal }, bx, y);
			int s2 = sec;
			b.Pressed += () => { Game.SaveSettings.SetAutosave(s2); _dirty = true; };
			_autosaveButtons[sec] = b;
			bx += 118;
		}
		y += 66;
		y = WrappedText(panel, "„aus“: kein regelmäßiges Speichern — beim Schließen, Pausieren und „Zum Hauptmenü“ wird trotzdem gespeichert.", 20, y, pw - 60, 14, Dim, 19);
		// Hilfe und Tutorial: Kontexthinweise an/aus und zurücksetzen (gespeichert in den Einstellungen)
		y += 14;
		At(panel, Text("Hinweise", 19, White), 20, y, 300, 28); y += 34;
		var hintsOn = At(panel, new KitButton(Game.HintSettings.Text, new Vector2(220, 60)) { FontSize = 18, State = Game.HintSettings.Enabled ? KitButtonState.On : KitButtonState.Normal, Sub = "Tipps beim ersten Mal" }, 20, y);
		hintsOn.Pressed += () => { Game.HintSettings.Toggle(); _dirty = true; };
		TestHintsToggle = hintsOn;
		var hintsReset = At(panel, new KitButton("Zurücksetzen", new Vector2(200, 60)) { FontSize = 18, Sub = $"{Game.HintSettings.Seen.Count} gesehen" }, 252, y);
		hintsReset.Pressed += () => { Game.HintSettings.Reset(); _dirty = true; };
		TestHintsReset = hintsReset;
		y += 70;
		y = WrappedText(panel, "Kurze Tipps erscheinen einmal, wenn eine Lage zum ersten Mal auftritt. Die Hilfe (☰ → Hilfe) erklärt alles ausführlich.", 20, y, pw - 60, 14, Dim, 19);
		panel.CustomMinimumSize = new Vector2(pw - 24, y + 12);
	}
	readonly Dictionary<int, KitButton> _autosaveButtons = new();
	/// <summary>Selbsttest: Taste des Autosave-Intervalls (Sekunden).</summary>
	public KitButton? TestAutosave(int seconds) => Visible && _autosaveButtons.TryGetValue(seconds, out var b) && IsInstanceValid(b) && b.IsInsideTree() ? b : null;
	public KitButton? TestLoad { get; private set; }

	void SettingsRow(Control panel, ref float y, string text, bool muted, System.Action down, System.Action up, System.Action toggle)
	{
		var minus = At(panel, new KitButton("−", new Vector2(64, 60)) { FontSize = 24 }, 20, y);
		minus.Pressed += () => { down(); _dirty = true; };
		var mid = At(panel, new KitButton(text, new Vector2(220, 60)) { FontSize = 18, State = muted ? KitButtonState.Normal : KitButtonState.On, Sub = muted ? "tippen: an" : "tippen: stumm" }, 92, y);
		mid.Pressed += () => { toggle(); _dirty = true; };
		var plus = At(panel, new KitButton("+", new Vector2(64, 60)) { FontSize = 24 }, 320, y);
		plus.Pressed += () => { up(); _dirty = true; };
		y += 70;
	}

	// ── Selbsttest ──
	public KitButton? TestContinue { get; private set; }
	public KitButton? TestSettings { get; private set; }
	public KitButton? TestScenarioToggle { get; private set; }
	public KitButton? TestHintsToggle { get; private set; }
	public KitButton? TestHintsReset { get; private set; }
	public KitScroll? TestChooserScroll => Visible ? _scroll : null;
	/// <summary>„Starten“-Taste eines Pakets (Testszenarien nur, wenn aufgeklappt — <see cref="ShowTests"/>).</summary>
	public KitButton? TestStartButton(string key) => Visible && _startButtons.TryGetValue(key, out var b) && IsInstanceValid(b) && b.IsInsideTree() ? b : null;
	public bool ShowTests { get => _showTests; set { _showTests = value; _dirty = true; } }
	public List<string> TestChooserTexts()
	{
		var list = new List<string>();
		void Walk(Node n) { foreach (var c in n.GetChildren()) { if (c is Label l) list.Add(l.Text); if (c is KitButton kb) list.Add(kb.Text); Walk(c); } }
		if (_scroll is not null) Walk(_scroll);
		return list;
	}
	/// <summary>Sofort neu aufbauen (Selbsttest).</summary>
	public void TestRebuild() { _dirty = false; Rebuild(); }
}
