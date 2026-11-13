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
		float my = WrappedText(inner, "Autosave alle 30 s; Speichern/Laden legen den Spielstand wie bisher in Main ab.", 20, 256, 310, 14, Dim, 20);
		// Gerätetest Phase 27: sichtbare Rückmeldung im Bereich selbst (der Toast liegt unter der Kommandozentrale)
		if (_message.Length > 0) At(inner, Text(_message, 16, _messageOk ? Green : Red), 20, my + 8f, 310, 24);   // Runde 4: Abstand zum Text darüber

		At(inner, Text("Einstellungen", 22, Cyan), 20, 350, 400, 30);
		At(inner, new KitButton("Einstellungen", new Vector2(300, 56)) { FontSize = 19, State = KitButtonState.Disabled, Sub = UiPlaceholders.Soon }, 20, 390);
		At(inner, Text("Ton, Tasten, Sprache: " + UiPlaceholders.Tag + " (" + UiPlaceholders.Soon + ").", 14, new Color(Dim, 0.8f)), 20, 458, 320, 44);
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
		float x = 350f, w = 1022f - x - 10f;
		At(inner, Text("Startpaket wählen", 22, Cyan), x, 16, w, 30);
		// Gerätetest Phase 27: Liste rollt (KitScroll) — „Standard“ lief über den Rahmen
		var scroll = At(inner, new KitScroll(), x, 56, w, 520 - 56 - 8);
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
		_chooserScroll = scroll;
	}

	KitScroll? _chooserScroll;
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
