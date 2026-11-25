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
		// UI-Umbau: „Laden“ und „Neues Spiel“ gibt es nur noch auf dem Startbildschirm (über „Zum Hauptmenü“)
		var bMain = At(inner, new KitButton("Zum Hauptmenü", new Vector2(300, 56)) { FontSize = 19, Sub = "speichert vorher" }, 20, 122);
		bMain.Pressed += () => RaiseSystemAction("mainmenu");
		float my = WrappedText(inner, Game.SaveSettings.Text + " · Neues Spiel, Laden und Einstellungen im Hauptmenü.", 20, 196, 310, 14, Dim, 20);
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
			float dy = WrappedText(inner, UiScale.Describe(), 360, 452, 380, 13, Dim, 18);   // Runde 32: echte Tastengröße, ehrlicher Hinweis
			if (!StartMenu)
			{
				// Runde 45 (Nutzerwunsch: unter Einstellungen das Spielergeld setzen): Debug-Zeile — setzt die Spielerkasse auf 100.000.000 Cr
				var bMoney = At(inner, new KitButton("Guthaben", new Vector2(380, 56)) { FontSize = 17, Sub = "Debug: auf 100.000.000 Cr setzen" }, 360, MathF.Max(486f, dy + 8f));   // unter dem (mehrzeiligen) Hinweis
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

	KitButton? _testMusicMinus, _testMusicMute, _testEffectsMute;
	/// <summary>Selbsttest: Tontasten (Musik leiser, Musik stumm, Effekte stumm).</summary>
	public (KitButton? MusicMinus, KitButton? MusicMute, KitButton? EffectsMute) TestAudioButtons => (_testMusicMinus, _testMusicMute, _testEffectsMute);
}
