using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Fensterrahmen der Kit-Bildschirme: Titelleiste plus Inhaltsbereich (<see cref="Content"/>).
/// Vollbild-Bildschirme (Dock, Kommandozentrale) hängen hier ihre Zeilen und Knöpfe ein.
/// </summary>
public partial class KitWindow : PanelContainer
{
	readonly Label _title;

	public string Title { get => _title.Text; set => _title.Text = value; }
	/// <summary>Inhaltsbereich unter der Titelleiste.</summary>
	public VBoxContainer Content { get; }

	public KitWindow(string title, Vector2? size = null)
	{
		AddThemeStyleboxOverride("panel", UiKit.Box(UiKit.PanelSolid, UiKit.Edge, 16, 2, 16));   // Gerätetest: Innenabstand 16 (Inhalte klebten am Rahmen)
		if (size is { } s) { CustomMinimumSize = s; Size = s; }
		var outer = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		outer.AddThemeConstantOverride("separation", 10);
		AddChild(outer);
		_title = UiKit.Text(title, 24, UiKit.Cyan);
		outer.AddChild(_title);
		Content = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		Content.AddThemeConstantOverride("separation", 8);
		Content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		outer.AddChild(Content);
	}
}
