using System;
using Godot;

namespace SpaceAdventure.Ui;

/// <summary>
/// Bedienoberfläche: Stick, Knöpfe, Statusanzeige, Hinweise, Zielmarker und die modalen Panels.
/// Enthält keine Spiellogik, sondern meldet Eingaben über Ereignisse an den Aufrufer.
/// </summary>
public partial class Hud : CanvasLayer
{
	public VirtualStick Stick { get; } = new();
	public MapOverlay Map { get; } = new();
	public EconomyPanel Economy { get; } = new();
	public DockPanel Dock { get; } = new();
	public MenuPanel Menu { get; } = new();
	public TouchButton AutopilotButton { get; } = new("Autopilot") { Toggle = true };
	public TouchButton FastButton { get; } = new("Zeitraffer") { Toggle = true };
	public TouchButton DockButton { get; } = new("Andocken");
	public TouchButton LaserButton { get; } = new("Feuer (F)");
	public TouchButton TractorButton { get; } = new("Traktor") { Toggle = true };
	public TouchButton AutoMineButton { get; } = new("Auto-Mining") { Toggle = true };

	public event Action? MapPressed, EconomyPressed, MenuPressed, DockPressed, AutopilotPressed, FastPressed, TractorPressed, AutoMinePressed;

	/// <summary>Liefert die Bildschirmposition des Ziels (oder null), damit der Marker gezeichnet werden kann.</summary>
	public Func<Vector2?>? TargetScreenPosition { get; set; }

	readonly Label _status = new();
	readonly Label _target = new();
	readonly Label _cargo = new();
	readonly Label _mining = new();
	readonly Label _toast = new();
	readonly ProgressBar _hull = new() { MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(220, 14) };
	readonly ColorRect _flash = new() { Color = Colors.White, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
	readonly TargetMarker _marker = new();
	Tween? _toastTween;

	public override void _Ready()
	{
		var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);

		_marker.Hud = this;
		root.AddChild(_marker);

		// Status oben links
		var top = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(16, 10) };
		_status.AddThemeFontSizeOverride("font_size", 20);
		_target.AddThemeFontSizeOverride("font_size", 18);
		_target.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.4f));
		_cargo.AddThemeFontSizeOverride("font_size", 18);
		_mining.AddThemeFontSizeOverride("font_size", 18);
		_mining.AddThemeColorOverride("font_color", new Color(0.5f, 1f, 0.7f));
		top.AddChild(_status); top.AddChild(_hull); top.AddChild(_target); top.AddChild(_cargo); top.AddChild(_mining);
		root.AddChild(top);

		// Hinweistext oben Mitte
		_toast.HorizontalAlignment = HorizontalAlignment.Center;
		_toast.AddThemeFontSizeOverride("font_size", 22);
		_toast.Modulate = new Color(1, 1, 1, 0);
		_toast.MouseFilter = Control.MouseFilterEnum.Ignore;
		_toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
		_toast.Position = new Vector2(-400, 70); _toast.Size = new Vector2(800, 40);
		root.AddChild(_toast);

		// Stick unten links
		Stick.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
		Stick.Position = new Vector2(24, -264); Stick.Size = new Vector2(240, 240);
		root.AddChild(Stick);

		// Knöpfe unten rechts
		var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		col.AddThemeConstantOverride("separation", 8);
		foreach (var b in new[] { DockButton, AutopilotButton, new TouchButton("Karte"), new TouchButton("Wirtschaft"), FastButton, new TouchButton("Menü") })
			col.AddChild(b);
		col.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
		col.Position = new Vector2(-180, -460); col.Size = new Vector2(160, 440);
		root.AddChild(col);
		DockButton.Visible = false;

		// Abbau-Knöpfe links neben der Hauptspalte (gut mit dem Daumen erreichbar)
		var mineCol = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		mineCol.AddThemeConstantOverride("separation", 8);
		foreach (var b in new[] { LaserButton, TractorButton, AutoMineButton }) mineCol.AddChild(b);
		mineCol.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
		mineCol.Position = new Vector2(-350, -230); mineCol.Size = new Vector2(160, 200);
		root.AddChild(mineCol);
		TractorButton.Pressed += () => TractorPressed?.Invoke();
		AutoMineButton.Pressed += () => AutoMinePressed?.Invoke();

		DockButton.Pressed += () => DockPressed?.Invoke();
		AutopilotButton.Pressed += () => AutopilotPressed?.Invoke();
		FastButton.Pressed += () => FastPressed?.Invoke();
		((TouchButton)col.GetChild(2)).Pressed += () => MapPressed?.Invoke();
		((TouchButton)col.GetChild(3)).Pressed += () => EconomyPressed?.Invoke();
		((TouchButton)col.GetChild(5)).Pressed += () => MenuPressed?.Invoke();

		// Wirtschaftspanel oben rechts, Dock/Menü zentriert, Karte vollflächig
		Economy.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
		Economy.Position = new Vector2(-470, 10);
		root.AddChild(Economy);
		foreach (var c in new Control[] { Dock, Menu })
		{
			c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
			c.GrowHorizontal = Control.GrowDirection.Both; c.GrowVertical = Control.GrowDirection.Both;
			root.AddChild(c);
		}
		root.AddChild(Map);

		_flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(_flash);
	}

	public bool AnyModalOpen => Map.Visible || Dock.Visible || Menu.Visible;

	public void SetStatus(string text, float hull, string? target, string? cargo = null, string? mining = null)
	{
		_status.Text = text;
		_hull.Value = hull;
		_target.Text = target ?? "";
		_cargo.Text = cargo ?? "";
		_mining.Text = mining ?? "";
	}

	public void Toast(string text, float seconds = 2.5f)
	{
		_toast.Text = text;
		_toastTween?.Kill();
		_toast.Modulate = Colors.White;
		_toastTween = CreateTween();
		_toastTween.TweenInterval(seconds);
		_toastTween.TweenProperty(_toast, "modulate:a", 0.0, 0.6);
	}

	public void Flash(Color color, float seconds = 0.5f)
	{
		_flash.Color = color;
		_flash.Modulate = new Color(1, 1, 1, 0.85f);
		var tw = CreateTween();
		tw.TweenProperty(_flash, "modulate:a", 0.0, seconds);
	}

	/// <summary>Zeichnet Ring am Ziel bzw. Pfeil am Bildschirmrand, wenn das Ziel außerhalb liegt.</summary>
	sealed partial class TargetMarker : Control
	{
		public Hud Hud = null!;
		public TargetMarker() { MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }
		public override void _Process(double delta) => QueueRedraw();

		public override void _Draw()
		{
			var p = Hud.TargetScreenPosition?.Invoke();
			if (p is not { } pos) return;
			var rect = new Rect2(Vector2.Zero, Size).Grow(-30f);
			var col = new Color(1f, 0.9f, 0.3f);
			if (rect.HasPoint(pos)) { DrawArc(pos, 22f, 0, Mathf.Tau, 32, col, 3f); return; }
			var c = Size / 2f;
			var dir = (pos - c).Normalized();
			float t = Mathf.Min(Mathf.Abs(rect.Size.X / 2f / (dir.X == 0 ? 0.0001f : dir.X)), Mathf.Abs(rect.Size.Y / 2f / (dir.Y == 0 ? 0.0001f : dir.Y)));
			var tip = c + dir * t;
			var side = new Vector2(-dir.Y, dir.X) * 12f;
			DrawColoredPolygon(new[] { tip, tip - dir * 24f + side, tip - dir * 24f - side }, col);
		}
	}
}
