using System;
using System.Collections.Generic;
using Godot;
using SpaceAdventure.Ui.Kit;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Platzierungsmodus im Flug (docs/STATIONEN.md „Eigene Stationen bauen“): ersetzt das Flug-HUD, solange der Geist einer eigenen Station gesetzt wird.
/// Oben eine Karte (Typ, Bausumme, gültig grün / ungültig rot mit Grund, Drehung), unten links das Steuerkreuz (▲ ▼ ◀ ▶ auf der Ebene, relativ zur Kamera),
/// unten rechts „↻ 15°“, „↻ 45°“, „Abbrechen“, „Platzieren“ — alle Tasten mindestens 64 px. Liegt in der Flug-Schicht (gleicher UI-Faktor wie das Flug-HUD)
/// in eigener Fläche; Regeln nur im Core (<see cref="SpaceSim.Core.Simulation.PlayerBuilding.PlaceIssue"/>), Main bewegt den Geist.
/// </summary>
public sealed partial class PlacementHud : Control
{
	public const float Btn = 64f, Gap = 8f, Margin = 16f;

	/// <summary>Bewegung des Geists (−1/0/1 je Achse: rechts, vor).</summary>
	public event Action<int, int>? Move;
	public event Action<float>? Rotate;
	public event Action? Place;
	public event Action? Cancel;

	readonly KitButton _up = new("▲", new Vector2(Btn, Btn)) { FontSize = 26 };
	readonly KitButton _down = new("▼", new Vector2(Btn, Btn)) { FontSize = 26 };
	readonly KitButton _left = new("◀", new Vector2(Btn, Btn)) { FontSize = 26 };
	readonly KitButton _right = new("▶", new Vector2(Btn, Btn)) { FontSize = 26 };
	readonly KitButton _rot15 = new("↻ 15°", new Vector2(150, Btn)) { FontSize = 18, Sub = "Drehen" };
	readonly KitButton _rot45 = new("↻ 45°", new Vector2(150, Btn)) { FontSize = 18, Sub = "Drehen" };
	readonly KitButton _cancel = new("Abbrechen", new Vector2(150, Btn)) { FontSize = 18 };
	readonly KitButton _place = new("Platzieren", new Vector2(150, Btn)) { FontSize = 18 };
	readonly Control _card = new() { MouseFilter = MouseFilterEnum.Stop };
	readonly Label _title = Text("", 18, White);
	readonly Label _status = Text("", 15, Green);
	readonly Label _hint = Text("", 13, Dim);

	/// <summary>Selbsttest/Prüfwerkzeug: Tasten nach Namen.</summary>
	public KitButton UpBtn => _up; public KitButton DownBtn => _down; public KitButton LeftBtn => _left; public KitButton RightBtn => _right;
	public KitButton Rot15Btn => _rot15; public KitButton Rot45Btn => _rot45; public KitButton CancelBtn => _cancel; public KitButton PlaceBtn => _place;
	public string StatusText => _status.Text;
	public bool Valid { get; private set; }

	public PlacementHud()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		Visible = false;
	}

	public override void _Ready()
	{
		var panel = new Panel { MouseFilter = MouseFilterEnum.Ignore };
		panel.AddThemeStyleboxOverride("panel", Box(PanelSolid, Edge, 12, 2, 0));
		_card.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		foreach (var l in new[] { _title, _status, _hint }) { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; _card.AddChild(l); }
		AddChild(_card);
		foreach (var b in new[] { _up, _down, _left, _right, _rot15, _rot45, _cancel, _place }) AddChild(b);
		_up.Pressed += () => Move?.Invoke(0, 1);
		_down.Pressed += () => Move?.Invoke(0, -1);
		_left.Pressed += () => Move?.Invoke(-1, 0);
		_right.Pressed += () => Move?.Invoke(1, 0);
		_rot15.Pressed += () => Rotate?.Invoke(15f);
		_rot45.Pressed += () => Rotate?.Invoke(45f);
		_cancel.Pressed += () => Cancel?.Invoke();
		_place.Pressed += () => { if (Valid) Place?.Invoke(); };
		Relayout();
	}

	/// <summary>Gleicher Faktor wie das Flug-HUD, ganze Fläche; Lagen aus der sicheren Fläche.</summary>
	public void ApplyScale(Vector2 viewport, float factor, SpaceSim.Core.Ui.UiInsets safe1)
	{
		AnchorLeft = 0f; AnchorTop = 0f; AnchorRight = 0f; AnchorBottom = 0f;
		Position = Vector2.Zero;
		Scale = new Vector2(factor, factor);
		var size = viewport / factor;
		var insets = UiScale.Div(safe1, factor);
		if ((Size - size).LengthSquared() < 0.01f && insets == _insets) return;
		Size = size;
		_insets = insets;
		Relayout();
	}
	SpaceSim.Core.Ui.UiInsets _insets;

	void Relayout()
	{
		if (Size.X < 10f) return;
		float l = _insets.Left + Margin, r = Size.X - _insets.Right - Margin, b = Size.Y - _insets.Bottom - Margin, t = _insets.Top + Margin;
		_up.Position = new Vector2(l + Btn + Gap, b - 3 * Btn - 2 * Gap);
		_left.Position = new Vector2(l, b - 2 * Btn - Gap);
		_right.Position = new Vector2(l + 2 * (Btn + Gap), b - 2 * Btn - Gap);
		_down.Position = new Vector2(l + Btn + Gap, b - Btn);
		_place.Position = new Vector2(r - 150f, b - Btn);
		_cancel.Position = new Vector2(r - 2 * 150f - Gap, b - Btn);
		_rot45.Position = new Vector2(r - 150f, b - 2 * Btn - Gap);
		_rot15.Position = new Vector2(r - 2 * 150f - Gap, b - 2 * Btn - Gap);
		float cw = MathF.Min(560f, r - l);
		_card.Position = new Vector2((l + r) / 2f - cw / 2f, t);
		_card.Size = _card.CustomMinimumSize = new Vector2(cw, 92f);
		_title.Position = new Vector2(14, 8); _title.Size = new Vector2(cw - 28, 26);
		_status.Position = new Vector2(14, 36); _status.Size = new Vector2(cw - 28, 22);
		_hint.Position = new Vector2(14, 62); _hint.Size = new Vector2(cw - 28, 20);
	}

	/// <summary>Anzeige je Bild: Titel, gültig (grün) oder Grund (rot), Hinweiszeile; „Platzieren“ nur bei gültigem Platz.</summary>
	public void SetState(string title, string? issue, string hint)
	{
		Valid = issue is null;
		_title.Text = title;
		_status.Text = issue is null ? "Platz frei – „Platzieren“ legt die Baustelle an" : $"Ungültig: {issue}";
		_status.AddThemeColorOverride("font_color", issue is null ? Green : Red);
		_hint.Text = hint;
		_place.State = Valid ? KitButtonState.Ready : KitButtonState.Disabled;
	}

	/// <summary>Rechtecke (Bildpunkte) der Tasten und der Karte — Meldung und Hinweis weichen ihnen aus.</summary>
	public List<Rect2> OccupiedGlobal(bool interactiveOnly = false)
	{
		var list = new List<Rect2>();
		if (!Visible) return list;
		foreach (var c in GetChildren())
			if (c is Control ctl && ctl.Visible && (!interactiveOnly || ctl is KitButton)) list.Add(ctl.GetGlobalRect().Grow(6f));
		return list;
	}
}
