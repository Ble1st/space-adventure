using Godot;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Runde 45 (Befehlsstand): Die Person sitzt im aktiven Schiff, während es seinen Befehl NPC-artig ausführt — Steuerung gesperrt,
/// Kamera frei. Diese Leiste zeigt unten links den Hinweis (Befehl) und das <b>X</b>: X hebt den Befehlsstand auf — die Person bekommt
/// die Steuerung zurück, der Befehl pausiert (wie an Bord im Flug) und läuft weiter, sobald die Person woanders ist.
/// </summary>
public partial class SpectateBar : Control
{
	/// <summary>X gedrückt — Befehlsstand aufheben (Steuerung zurück, Befehl pausiert).</summary>
	public event System.Action? ExitPressed;

	KitButton _x = null!;
	Label _label = null!;
	readonly Label _command = new();

	public string Id => "spectate";

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;

		_x = new KitButton("X", new Vector2(96, 96)) { FontSize = 40, Sub = "steuern" };
		_x.Pressed += () => ExitPressed?.Invoke();
		AddChild(_x);
		_x.Position = Vector2.Zero;

		_label = new Label
		{
			Text = "BEFEHLSSTAND",
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_label.AddThemeFontSizeOverride("font_size", 22);
		_label.AddThemeColorOverride("font_color", new Color(Ui.Kit.UiKit.Cyan, 0.95f));
		AddChild(_label);
		_label.Position = new Vector2(112, 12);

		_command.MouseFilter = MouseFilterEnum.Ignore;
		_command.AddThemeFontSizeOverride("font_size", 17);
		_command.AddThemeColorOverride("font_color", new Color(Ui.Kit.UiKit.White, 0.85f));
		AddChild(_command);
		_command.Position = new Vector2(112, 44);
	}

	/// <summary>Befehlsstand zeigen (Beschriftung = laufender Befehl des Schiffs).</summary>
	public void ShowSpectate(string command)
	{
		if (string.IsNullOrEmpty(command)) command = "Befehl läuft";
		_command.Text = command;
		Visible = true;
	}

	/// <summary>Befehlsstand ausblenden.</summary>
	public void HideSpectate() => Visible = false;

	/// <summary>Lage an Fensterfläche, UI-Faktor und Safe Area (wie das Flug-HUD, Runde 33): unten links.</summary>
	public void ApplyScale(Vector2 vp, float f, SpaceSim.Core.Ui.UiInsets insets)
	{
		f = f > 0.05f ? f : 1f;
		Scale = new Vector2(f, f);
		Position = new Vector2((insets.Left + 16f) / f, (vp.Y - insets.Bottom - 16f - 96f * f) / f);
	}
}
