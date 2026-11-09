using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>Kit-Balken (z. B. Hülle): Fortschrittsbalken mit einstellbarer Füllfarbe, nicht klickbar.</summary>
public partial class KitBar : ProgressBar
{
	public KitBar(float w, float h, Color fill)
	{
		CustomMinimumSize = new Vector2(w, h);
		Size = new Vector2(w, h);
		MaxValue = 100;
		ShowPercentage = false;
		MouseFilter = Control.MouseFilterEnum.Ignore;
		SetFill(fill);
		AddThemeStyleboxOverride("background", UiKit.Box(new Color(0, 0, 0, 0.45f), null, (int)(h / 2), 0, 0));
	}

	/// <summary>Wert 0..max mit optional neuer Füllfarbe.</summary>
	public void Set(float value, Color? fill = null, float max = 100f)
	{
		MaxValue = max;
		Value = value;
		if (fill is { } f) SetFill(f);
	}

	void SetFill(Color fill) => AddThemeStyleboxOverride("fill", UiKit.Box(fill, null, 6, 0, 0));
}
