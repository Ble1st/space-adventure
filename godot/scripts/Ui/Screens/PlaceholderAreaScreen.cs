using Godot;
using SpaceAdventure.Ui.Kit;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kurze Platzhalterseite für Dock-Bereiche, deren Dienst der Core noch nicht kennt (Reparatur & Tanken,
/// Ausrüster, Werft, Taxi, Aufträge): gedämpft mit „Bald“, Erklärtext aus <see cref="UiPlaceholders"/>.
/// Der Aufrufer setzt vor dem Öffnen <see cref="OpenWith"/>.
/// </summary>
public sealed partial class PlaceholderAreaScreen : DockAreaScreen
{
	public override string Id => "dock.placeholder";
	DockAreaDef? _def;

	/// <summary>Setzt den anzuzeigenden Platzhalter-Bereich und öffnet die Seite.</summary>
	public void OpenWith(DockAreaDef def)
	{
		_def = def;
		AreaId = def.Id;
		Open();
	}

	protected override void BuildContent(Control inner)
	{
		var def = _def;
		if (def == null) return;
		float w = inner.Size.X > 0 ? inner.Size.X : 1020f;

		At(inner, Text(def.Label, 26, new Color(Dim, 0.9f)), 0, 8, w, 36);
		var soon = At(inner, new PanelContainer(), 0, 52, 120, 36);
		soon.AddThemeStyleboxOverride("panel", Box(new Color(Dim, 0.15f), new Color(Dim, 0.5f), 8, 1, 6));
		soon.AddChild(Text(UiPlaceholders.Soon, 18, new Color(Dim, 0.9f)));

		WrappedText(inner, UiPlaceholders.DockAreaText(def.Id), 0, 108, w - 20, 18, White);
		WrappedText(inner, $"Dieser Bereich ist ein {UiPlaceholders.Tag}: er ist nicht klickbar und ändert keinen Spielzustand.", 0, 300, w - 20, 14, new Color(Dim, 0.75f));
	}
}
