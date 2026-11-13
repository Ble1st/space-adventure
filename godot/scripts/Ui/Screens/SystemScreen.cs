using System;
using Godot;
using SpaceAdventure.Ui.Kit;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „System“ (UI.md Abschnitt 4): Speichern, Laden und Neues Spiel bleiben über die
/// linke Leiste erreichbar (Aktionen werden an Main durchgereicht, <see cref="CommandScreen.SystemAction"/>); „Neues Spiel“
/// öffnet die Auswahl des Startpakets (Phase 5c);
/// Einstellungen sind Platzhalter. Autosave läuft alle 30 s in Main.
/// </summary>
public sealed partial class SystemScreen : CommandScreen, ISessionScreen
{
	public override string Id => "cc.system";
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { _choosing = false; }

	protected override void BuildContent(Control inner)
	{
		At(inner, Text("Spielstand", 22, Cyan), 20, 16, 400, 30);
		var bSave = At(inner, new KitButton("Speichern", new Vector2(300, 56)) { FontSize = 19 }, 20, 56);
		bSave.Pressed += () => RaiseSystemAction("save");
		var bLoad = At(inner, new KitButton("Laden", new Vector2(300, 56)) { FontSize = 19 }, 20, 122);
		bLoad.Pressed += () => RaiseSystemAction("load");
		var bNew = At(inner, new KitButton("Neues Spiel", new Vector2(300, 56)) { FontSize = 19, State = _choosing ? KitButtonState.On : KitButtonState.Normal }, 20, 188);
		bNew.Pressed += () => { _choosing = !_choosing; Invalidate(); };
		if (_choosing) BuildStartChooser(inner);
		WrappedText(inner, "Autosave alle 30 s; Speichern/Laden legen den Spielstand wie bisher in Main ab.", 20, 256, 310, 14, Dim, 20);
		// Gerätetest Phase 27: sichtbare Rückmeldung im Bereich selbst (der Toast liegt unter der Kommandozentrale)
		if (_message.Length > 0) At(inner, Text(_message, 16, _messageOk ? Green : Red), 20, 290, 310, 24);

		At(inner, Text("Einstellungen", 22, Cyan), 20, 320, 400, 30);
		At(inner, new KitButton("Einstellungen", new Vector2(300, 56)) { FontSize = 19, State = KitButtonState.Disabled, Sub = UiPlaceholders.Soon }, 20, 360);
		At(inner, Text("Ton, Tasten, Sprache: " + UiPlaceholders.Tag + " (" + UiPlaceholders.Soon + ").", 14, new Color(Dim, 0.8f)), 20, 428, 320, 44);
	}

	bool _choosing;
	string _message = "";
	bool _messageOk = true;

	/// <summary>Gerätetest Phase 27: Ergebnis von Speichern/Laden im Bereich zeigen („Gespeichert 12:03“).</summary>
	public void ShowMessage(string text, bool ok) { _message = text; _messageOk = ok; Invalidate(); }

	/// <summary>Für den Selbsttest: zuletzt gezeigte Meldung.</summary>
	public string TestMessage => _message;

	/// <summary>
	/// Auswahl des Startpakets (Phase 5c, Content <c>player.startOptions</c>): je Paket Schiff, Startgeld und Idee, dazu „Standard“
	/// (Einheitsschiff wie bisher). „Starten“ meldet „new:&lt;Paket&gt;“ bzw. „new“ an Main.
	/// </summary>
	void BuildStartChooser(Control inner)
	{
		float x = 350f, w = 1022f - x - 10f;
		At(inner, Text("Startpaket wählen", 22, Cyan), x, 16, w, 30);
		// Gerätetest Phase 27: Liste rollt (KitScroll) — „Standard“ lief über den Rahmen
		var scroll = At(inner, new KitScroll(), x, 56, w, 520 - 56 - 8);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 8);
		scroll.AddChild(box);
		float cw = w - 14f;
		var def = SpaceSim.Core.Content.WorldBuilder.Parse(SpaceSim.Core.Content.WorldBuilder.DefaultWorldJson());
		string ModelName(string key) => def.ShipModels.Find(m => m.Key == key)?.Name ?? key;
		foreach (var o in def.Player.StartOptions)
		{
			// Gerätetest Phase 28: Kartenhöhe nach der Länge der Beschreibung (lange Testpaket-Texte liefen über den Kartenrand); ≈ 6,8 px je Zeichen bei 13 px
			int lines = Math.Max(2, (int)Math.Ceiling(o.Description.Length * 6.8f / Math.Max(100f, cw - 210f)) + 1);
			float ch = Math.Max(100f, 40f + lines * 18f);
			var holder = new Control { CustomMinimumSize = new Vector2(cw, ch), MouseFilter = MouseFilterEnum.Pass };
			box.AddChild(holder);
			var card = CardBg(holder, 0, 0, cw, ch, Card, new Color(Edge, 0.6f), 10, 1);
			At(card, Text($"{o.Name} · {ModelName(o.Model)} · {o.Credits:0} Cr", 18, White), 14, 6, cw - 200, 26);
			WrappedText(card, o.Description, 14, 34, cw - 210, 13, Dim, 18);
			var b = At(card, new KitButton("Starten", new Vector2(160, 56)) { FontSize = 17, State = KitButtonState.Ready }, cw - 172, 22);
			string key = o.Key;
			b.Pressed += () => { _choosing = false; RaiseSystemAction("new:" + key); };
		}
		var sh = new Control { CustomMinimumSize = new Vector2(cw, 64), MouseFilter = MouseFilterEnum.Pass };
		box.AddChild(sh);
		var std = CardBg(sh, 0, 0, cw, 64, Card, new Color(Edge, 0.4f), 10, 1);
		At(std, Text($"Standard · {ModelName(def.Player.Model ?? "unit_player")} · {def.Player.Credits:0} Cr (wie bisher)", 16, White), 14, 18, cw - 200, 26);
		var bs = At(std, new KitButton("Starten", new Vector2(160, 52)) { FontSize = 17 }, cw - 172, 6);
		bs.Pressed += () => { _choosing = false; RaiseSystemAction("new"); };
		_chooserScroll = scroll;
	}

	KitScroll? _chooserScroll;
	/// <summary>Für den Selbsttest: Rollbereich der Startpakete (null = zu).</summary>
	public KitScroll? TestChooserScroll => _choosing ? _chooserScroll : null;

	/// <summary>Für den Selbsttest: Startpaket-Auswahl offen?</summary>
	public bool TestChoosing { get => _choosing; set { _choosing = value; Invalidate(); } }
}
