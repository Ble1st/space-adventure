using System;
using Godot;
using SpaceAdventure.Ui.Kit;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „System“ (UI.md Abschnitt 4): Speichern, Laden und Neues Spiel bleiben über die
/// linke Leiste erreichbar (Aktionen werden an Main durchgereicht, <see cref="CommandScreen.SystemAction"/>);
/// Einstellungen sind Platzhalter. Autosave läuft alle 30 s in Main.
/// </summary>
public sealed partial class SystemScreen : CommandScreen
{
	public override string Id => "cc.system";

	protected override void BuildContent(Control inner)
	{
		At(inner, Text("Spielstand", 22, Cyan), 20, 16, 400, 30);
		var bSave = At(inner, new KitButton("Speichern", new Vector2(300, 56)) { FontSize = 19 }, 20, 56);
		bSave.Pressed += () => RaiseSystemAction("save");
		var bLoad = At(inner, new KitButton("Laden", new Vector2(300, 56)) { FontSize = 19 }, 20, 122);
		bLoad.Pressed += () => RaiseSystemAction("load");
		var bNew = At(inner, new KitButton("Neues Spiel", new Vector2(300, 56)) { FontSize = 19 }, 20, 188);
		bNew.Pressed += () => RaiseSystemAction("new");
		At(inner, Text("Autosave alle 30 s; Speichern/Laden legen den Spielstand wie bisher in Main ab.", 14, Dim), 20, 256, 600, 40);

		At(inner, Text("Einstellungen", 22, Cyan), 20, 320, 400, 30);
		At(inner, new KitButton("Einstellungen", new Vector2(300, 56)) { FontSize = 19, State = KitButtonState.Disabled, Detail = UiPlaceholders.Soon }, 20, 360);
		At(inner, Text("Ton, Tasten, Sprache: " + UiPlaceholders.Tag + " (" + UiPlaceholders.Soon + ").", 14, new Color(Dim, 0.8f)), 20, 428, 600, 22);
	}
}
