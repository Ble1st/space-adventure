using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Texteingabe im Kit-Stil (Lücken gegenüber IP2: Schiff und eigene Fraktion umbenennen). Ein Godot-<see cref="LineEdit"/> mit Kit-Rahmen, ≥ 56 px hoch;
/// auf Android öffnet ein Tipp die virtuelle Tastatur. Die Prüfregel liegt im Core (Issue-Funktionen); hier nur Eingabe.
/// </summary>
public static class KitLineEdit
{
	public static LineEdit Create(string text, string placeholder, Vector2 size, int maxLength, int fontSize = 18)
	{
		var e = new LineEdit
		{
			Text = text,
			PlaceholderText = placeholder,
			MaxLength = maxLength,
			CustomMinimumSize = new Vector2(size.X, System.MathF.Max(56f, size.Y)),
			Size = new Vector2(size.X, System.MathF.Max(56f, size.Y)),
			VirtualKeyboardEnabled = true,
			SelectAllOnFocus = true,
			ClearButtonEnabled = true,
		};
		e.AddThemeFontSizeOverride("font_size", fontSize);
		e.AddThemeColorOverride("font_color", UiKit.White);
		e.AddThemeColorOverride("font_placeholder_color", new Color(UiKit.Dim, 0.6f));
		e.AddThemeStyleboxOverride("normal", UiKit.Box(UiKit.Card, new Color(UiKit.Edge, 0.7f), 8, 1, 10));
		e.AddThemeStyleboxOverride("focus", UiKit.Box(UiKit.Card, UiKit.Cyan, 8, 2, 10));
		return e;
	}
}
