using System.Collections.Generic;
using System.Linq;
using System;
using Godot;
using SpaceAdventure.Ui.Kit;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „System“ (UI.md Abschnitt 4): Speichern, Laden und Neues Spiel bleiben über die
/// linke Leiste erreichbar (Aktionen werden an Main durchgereicht, <see cref="CommandScreen.SystemAction"/>); „Neues Spiel“
/// öffnet die Auswahl des Startpakets (Phase 5c);
/// Runde 12: Ton (Musik und Effekte, Lautstärke und Stumm). Autosave läuft alle 30 s in Main.
/// </summary>
public sealed partial class SystemScreen : CommandScreen, ISessionScreen
{
	public override string Id => "cc.system";
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { _choosing = false; }
	/// <summary>Runde 45 (Nutzerwunsch: Start im Hauptmenü, kein Spiel im Hintergrund): das Spiel startet in diesem Bereich — ohne laufende Welt:
	/// nur Neues Spiel (Startpaket-Auswahl von Anfang an offen), Laden und die Einstellungen (Ton, Bedienung). Main lässt die Simulation und das
	/// Autospeichern ruhen und blockiert den Rückweg ins Flug-HUD, bis ein Spiel gewählt ist. Nach dem Start setzt Main das Flag zurück.</summary>
	public bool StartMenu { get; set; }

	protected override void BuildContent(Control inner)
	{
		At(inner, Text(StartMenu ? "Hauptmenü" : "Spielstand", 22, Cyan), 20, 16, 400, 30);
		if (!StartMenu)
		{
			var bSave = At(inner, new KitButton("Speichern", new Vector2(300, 56)) { FontSize = 19 }, 20, 56);
			bSave.Pressed += () => RaiseSystemAction("save");
		}
		var bLoad = At(inner, new KitButton("Laden", new Vector2(300, 56)) { FontSize = 19 }, 20, 122);
		bLoad.Pressed += () => RaiseSystemAction("load");
		var bNew = At(inner, new KitButton("Neues Spiel", new Vector2(300, 56)) { FontSize = 19, State = _choosing ? KitButtonState.On : KitButtonState.Normal }, 20, 188);
		bNew.Pressed += () => { _choosing = !_choosing; Invalidate(); };
		if (_choosing) BuildStartChooser(inner);
		float my = WrappedText(inner, StartMenu ? "Neues Spiel wählt ein Startpaket und startet die Welt; Laden setzt den letzten Spielstand fort. Die Einstellungen gelten sofort." : "Autosave alle 30 s; Speichern/Laden legen den Spielstand wie bisher in Main ab.", 20, 256, 310, 14, Dim, 20);
		// Gerätetest Phase 27: sichtbare Rückmeldung im Bereich selbst (der Toast liegt unter der Kommandozentrale)
		if (_message.Length > 0) At(inner, Text(_message, 16, _messageOk ? Green : Red), 20, my + 8f, 310, 24);   // Runde 4: Abstand zum Text darüber

		// Runde 12: Ton — Musik und Effekte getrennt (eigene Busse), Lautstärke in 10-%-Schritten, Tipp auf die Mitte schaltet stumm; gespeichert in user://settings.cfg
		At(inner, Text("Ton", 22, Cyan), 20, 350, 400, 30);
		AudioRow(inner, 390, SpaceAdventure.Audio.AudioSettings.MusicText, SpaceAdventure.Audio.AudioSettings.MusicMuted,
			() => SpaceAdventure.Audio.AudioSettings.ChangeMusic(-SpaceAdventure.Audio.AudioSettings.Step), () => SpaceAdventure.Audio.AudioSettings.ChangeMusic(SpaceAdventure.Audio.AudioSettings.Step),
			SpaceAdventure.Audio.AudioSettings.ToggleMusicMute, "music");
		AudioRow(inner, 452, SpaceAdventure.Audio.AudioSettings.EffectsText, SpaceAdventure.Audio.AudioSettings.EffectsMuted,
			() => SpaceAdventure.Audio.AudioSettings.ChangeEffects(-SpaceAdventure.Audio.AudioSettings.Step), () => SpaceAdventure.Audio.AudioSettings.ChangeEffects(SpaceAdventure.Audio.AudioSettings.Step),
			SpaceAdventure.Audio.AudioSettings.ToggleEffectsMute, "effects");

		// Runde 31 (UI-ANPASSUNG.md E1): UI-Größe klein/normal/groß — rechts neben „Ton“, solange keine Startpaket-Auswahl offen ist
		if (!_choosing)
		{
			At(inner, Text("Bedienung", 22, Cyan), 360, 350, 400, 30);
			float bx = 360;
			foreach (var size in new[] { SpaceSim.Core.Ui.UiSize.Small, SpaceSim.Core.Ui.UiSize.Normal, SpaceSim.Core.Ui.UiSize.Large })
			{
				var b = At(inner, new KitButton(UiScale.Name(size), new Vector2(120, 56)) { FontSize = 17, State = UiScale.Size == size ? KitButtonState.On : KitButtonState.Normal }, bx, 390);
				var sz = size;
				b.Pressed += () => { UiScale.Set(sz); Invalidate(); };
				if (size == SpaceSim.Core.Ui.UiSize.Large) _testUiLarge = b;
				bx += 128;
			}
			WrappedText(inner, UiScale.Describe(), 360, 452, 380, 13, Dim, 18);   // Runde 32: echte Tastengröße, ehrlicher Hinweis
			if (!StartMenu)
			{
				// Runde 45 (Nutzerwunsch: unter Einstellungen das Spielergeld setzen): Debug-Zeile — setzt die Spielerkasse auf 100.000.000 Cr
				var bMoney = At(inner, new KitButton("Guthaben", new Vector2(380, 56)) { FontSize = 17, Sub = "Debug: auf 100.000.000 Cr setzen" }, 360, 486);
				bMoney.Pressed += () => RaiseSystemAction("money_cheat");
				_testMoney = bMoney;
			}
		}
	}

	/// <summary>Runde 31, Selbsttest: Taste „groß“ der UI-Größe.</summary>
	public KitButton? TestUiLarge => _testUiLarge;
	KitButton? _testUiLarge;
	/// <summary>Runde 45, Selbsttest: Debug-Taste „Guthaben“ unter den Einstellungen.</summary>
	public KitButton? TestMoney => _testMoney;
	KitButton? _testMoney;

	/// <summary>Runde 12: eine Tonzeile „− | Musik 70 % | +“ (Mitte = stumm an/aus).</summary>
	void AudioRow(Control inner, float y, string text, bool muted, Action down, Action up, Action toggle, string key)
	{
		var minus = At(inner, new KitButton("−", new Vector2(56, 56)) { FontSize = 22 }, 20, y);
		minus.Pressed += () => { down(); Invalidate(); };
		var mid = At(inner, new KitButton(text, new Vector2(176, 56)) { FontSize = 16, State = muted ? KitButtonState.Normal : KitButtonState.On, Sub = muted ? "tippen: an" : "tippen: stumm" }, 82, y);
		mid.Pressed += () => { toggle(); Invalidate(); };
		var plus = At(inner, new KitButton("+", new Vector2(56, 56)) { FontSize = 22 }, 264, y);
		plus.Pressed += () => { up(); Invalidate(); };
		if (key == "music") { _testMusicMinus = minus; _testMusicMute = mid; } else _testEffectsMute = mid;
	}

	bool _choosing;
	string _message = "";
	bool _messageOk = true;

	/// <summary>Gerätetest Phase 27: Ergebnis von Speichern/Laden im Bereich zeigen („Gespeichert 12:03“).</summary>
	public void ShowMessage(string text, bool ok) { _message = text; _messageOk = ok; Invalidate(); }

	/// <summary>Für den Selbsttest: zuletzt gezeigte Meldung.</summary>
	public string TestMessage => _message;

	/// <summary>
	/// Auswahl des Startpakets (Phase 5c, Content <c>player.startOptions</c>): je Paket Schiff, Startgeld und Idee. Runde 11: nur die regulären Pakete
	/// (Jäger, Händler, Schürfer) und abgesetzt die Testszenarien; „Standard“ entfällt (der Code-Standard ohne Paket bleibt für Tests). „Starten“ meldet „new:&lt;Paket&gt;“.
	/// </summary>
	void BuildStartChooser(Control inner)
	{
		float x = 350f, w = InnerW(inner) - x - 10f;   // Runde 35: Breite aus der Fläche
		At(inner, Text("Startpaket wählen", 22, Cyan), x, 16, w, 30);
		// Gerätetest Phase 27: Liste rollt (KitScroll) — „Standard“ lief über den Rahmen
		var scroll = At(inner, new KitScroll(), x, 56, w, InnerH(inner) - 56 - 8);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 8);
		scroll.AddChild(box);
		float cw = w - 14f;
		// Zweiter Gerätetest Phase 28: die Standardwelt nur einmal lesen (vorher bei jedem Neuaufbau, viermal je Sekunde — lange Bilder am Gerät)
		var def = _defaultWorld ??= SpaceSim.Core.Content.WorldBuilder.Parse(SpaceSim.Core.Content.WorldBuilder.DefaultWorldJson());
		string ModelName(string key) => def.ShipModels.Find(m => m.Key == key)?.Name ?? key;
		// Runde 11 (Wunsch: „Standardschiff ein Jäger, Händler oder Miner“): genau die regulären Pakete oben, Testszenarien abgesetzt darunter, kein „Standard“ mehr
		bool testHeader = false;
		foreach (var o in def.Player.StartOptions.OrderBy(x => x.Scenario is null ? 0 : 1))
		{
			if (o.Scenario is not null && !testHeader)
			{
				testHeader = true;
				var gap = new Control { CustomMinimumSize = new Vector2(cw, 44), MouseFilter = MouseFilterEnum.Pass };
				box.AddChild(gap);
				At(gap, Text("Testszenarien (nur zum Prüfen)", 16, Dim), 4, 14, cw - 8, 26);
			}
			// Gerätetest Phase 28: Kartenhöhe nach der Länge der Beschreibung (lange Testpaket-Texte liefen über den Kartenrand); ≈ 6,8 px je Zeichen bei 13 px
			// zweiter Gerätetest Phase 28: Zeilen gemessen statt geschätzt (der Festnahmetest wurde in der dritten Zeile gekürzt)
			int lines = Math.Max(2, Wrap(o.Description, 13, cw - 210f).Count);
			// Runde 35b (Prüfwerkzeug: „Händler · Nomade · 1500 Cr“ bei 1,3 gekürzt): der Titel bricht um statt gekürzt zu werden
			string title = $"{o.Name} · {ModelName(o.Model)} · {o.Credits:0} Cr";
			int tl = Math.Max(1, Wrap(title, 18, cw - 200f).Count);
			float ch = Math.Max(100f, 40f + (tl - 1) * 24f + lines * 18f);
			var holder = new Control { CustomMinimumSize = new Vector2(cw, ch), MouseFilter = MouseFilterEnum.Pass };
			box.AddChild(holder);
			var card = CardBg(holder, 0, 0, cw, ch, Card, new Color(Edge, 0.6f), 10, 1);
			float ty = WrappedText(card, title, 14, 6, cw - 200, 18, White, 24);
			WrappedText(card, o.Description, 14, ty + 4, cw - 210, 13, Dim, 18);
			var b = At(card, new KitButton("Starten", new Vector2(160, 56)) { FontSize = 17, State = KitButtonState.Ready }, cw - 172, 22);
			string key = o.Key;
			_startButtons[key] = b;
			b.Pressed += () => { _choosing = false; RaiseSystemAction("new:" + key); };
		}
		_chooserScroll = scroll;
	}

	KitScroll? _chooserScroll;
	readonly Dictionary<string, KitButton> _startButtons = new();
	/// <summary>Für den Selbsttest: „Starten“-Taste des Startpakets (null = nicht in der offenen Auswahl).</summary>
	public KitButton? TestStartButton(string key) => _choosing && _startButtons.TryGetValue(key, out var b) && IsInstanceValid(b) && b.IsInsideTree() ? b : null;
	KitButton? _testMusicMinus, _testMusicMute, _testEffectsMute;
	/// <summary>Selbsttest: Tontasten (Musik leiser, Musik stumm, Effekte stumm).</summary>
	public (KitButton? MusicMinus, KitButton? MusicMute, KitButton? EffectsMute) TestAudioButtons => (_testMusicMinus, _testMusicMute, _testEffectsMute);
	static SpaceSim.Core.Content.WorldDef? _defaultWorld;
	/// <summary>Für den Selbsttest: Rollbereich der Startpakete (null = zu).</summary>
	public KitScroll? TestChooserScroll => _choosing ? _chooserScroll : null;

	/// <summary>Für den Selbsttest: alle Texte der Startauswahl (Karten-Titel, Zwischenüberschrift).</summary>
	public List<string> TestChooserTexts()
	{
		var list = new List<string>();
		void Walk(Node n) { foreach (var c in n.GetChildren()) { if (c is Label l) list.Add(l.Text); Walk(c); } }
		if (_chooserScroll is not null) Walk(_chooserScroll);
		return list;
	}

	/// <summary>Für den Selbsttest: Startpaket-Auswahl offen?</summary>
	public bool TestChoosing { get => _choosing; set { _choosing = value; Invalidate(); } }
}
