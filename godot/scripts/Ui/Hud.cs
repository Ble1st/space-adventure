using System;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceAdventure.Ui.Screens;

namespace SpaceAdventure.Ui;

/// <summary>
/// Komposition der Bedienoberfläche: Flug-HUD, Hamburger-Menü und die Dock-Bildschirme (Andock-Menü plus
/// Bereiche) als registrierte Bildschirme (Ui/Kit, Ui/Screens), dazu die bestehenden Panels (Karte,
/// Wirtschaft). Hinweise, Zielmarker und Vollbild-Blitz liegen hier. Enthält keine Spiellogik, sondern meldet
/// Eingaben über Ereignisse.
/// </summary>
public partial class Hud : CanvasLayer
{
	public FlightHud Flight { get; } = new();
	public CommandMenu Command { get; } = new();
	public MapOverlay Map { get; } = new();
	public EconomyPanel Economy { get; } = new();
	// Dock (UI.md Abschnitt 3): Auswahlmenü plus Vollbild-Bereiche; der Kontext hält Main aktuell
	public DockContext DockCtx { get; } = new();
	public DockMenu DockMenu { get; } = new();
	public TradeScreen DockTrade { get; } = new();
	public StationInfoScreen DockInfo { get; } = new();
	public HangarScreen DockHangar { get; } = new();
	/// <summary>Phase 12: Taxi (Dienst „taxi“).</summary>
	public TaxiScreen DockTaxi { get; } = new();
	public RepairFuelScreen DockRepair { get; } = new();
	public OutfitterScreen DockOutfitter { get; } = new();
	public ShipyardScreen DockShipyard { get; } = new();
	public StorageScreen DockStorage { get; } = new();
	public AuthorityScreen DockAuthority { get; } = new();
	public PlaceholderAreaScreen DockPlaceholder { get; } = new();
	// Kommandozentrale (UI.md Abschnitt 4): Vollbild-Bereiche mit linker Leiste; Kontext hält Main aktuell
	public CommandContext CcCtx { get; } = new();
	public UniverseScreen CcUniverse { get; } = new();
	public SectorScreen CcSector { get; } = new();
	public FactionsScreen CcFactions { get; } = new();
	public MyFactionScreen CcMyFaction { get; } = new();
	public FleetScreen CcFleet { get; } = new();
	public AssetsScreen CcAssets { get; } = new();
	public CommsScreen CcComms { get; } = new();
	public SystemScreen CcSystem { get; } = new();
	/// <summary>Registry aller Bildschirme; ein neuer Bildschirm ist eine Klasse plus ein Register-Aufruf.</summary>
	public ScreenRegistry Screens { get; } = new();

	/// <summary>Liefert die Bildschirmposition des Autopilot-Ziels (oder null), damit der Marker gezeichnet werden kann.</summary>
	public Func<Vector2?>? TargetScreenPosition { get; set; }

	readonly Label _toast = new();
	readonly ColorRect _flash = new() { Color = Colors.White, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
	readonly TargetMarker _marker = new();
	Tween? _toastTween;

	public override void _Ready()
	{
		var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);

		Screens.Register(Flight);
		Screens.Register(Command);
		Screens.Register(DockMenu);
		Screens.Register(DockTrade);
		Screens.Register(DockInfo);
		Screens.Register(DockHangar);
		Screens.Register(DockTaxi);
		Screens.Register(DockRepair);
		Screens.Register(DockOutfitter);
		Screens.Register(DockShipyard);
		Screens.Register(DockStorage);
		Screens.Register(DockAuthority);
		Screens.Register(DockPlaceholder);
		DockMenu.Ctx = DockCtx;
		DockTrade.Ctx = DockCtx;
		DockInfo.Ctx = DockCtx;
		DockHangar.Ctx = DockCtx;
		DockTaxi.Ctx = DockCtx;
		DockRepair.Ctx = DockCtx;
		DockOutfitter.Ctx = DockCtx;
		DockShipyard.Ctx = DockCtx;
		DockStorage.Ctx = DockCtx;
		DockAuthority.Ctx = DockCtx;
		DockPlaceholder.Ctx = DockCtx;
		Screens.Register(CcUniverse);
		Screens.Register(CcSector);
		Screens.Register(CcFactions);
		Screens.Register(CcMyFaction);
		Screens.Register(CcFleet);
		Screens.Register(CcAssets);
		Screens.Register(CcComms);
		Screens.Register(CcSystem);
		CcUniverse.Ctx = CcCtx;
		CcSector.Ctx = CcCtx;
		CcFactions.Ctx = CcCtx;
		CcMyFaction.Ctx = CcCtx;
		CcFleet.Ctx = CcCtx;
		CcAssets.Ctx = CcCtx;
		CcComms.Ctx = CcCtx;
		CcSystem.Ctx = CcCtx;

		_marker.Hud = this;
		root.AddChild(_marker);
		root.AddChild(Flight);

		// Hinweistext oben Mitte
		_toast.HorizontalAlignment = HorizontalAlignment.Center;
		_toast.AddThemeFontSizeOverride("font_size", 22);
		_toast.Modulate = new Color(1, 1, 1, 0);
		_toast.MouseFilter = Control.MouseFilterEnum.Ignore;
		_toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
		_toast.Position = new Vector2(-400, 100); _toast.Size = new Vector2(800, 40);   // Phase 11: unter Lage- und Fahndungszeile des Sektorchips
		root.AddChild(_toast);

		// Panels: Wirtschaft oben rechts, Karte vollflächig; Dock-Bildschirme kommen ans Ende (liegen oben)
		Economy.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
		Economy.Position = new Vector2(-470, 10);
		root.AddChild(Economy);
		root.AddChild(Map);
		root.AddChild(Command);
		root.AddChild(DockMenu);
		root.AddChild(DockTrade);
		root.AddChild(DockInfo);
		root.AddChild(DockHangar);
		root.AddChild(DockTaxi);
		root.AddChild(DockRepair);
		root.AddChild(DockOutfitter);
		root.AddChild(DockShipyard);
		root.AddChild(DockStorage);
		root.AddChild(DockAuthority);
		root.AddChild(DockPlaceholder);
		root.AddChild(CcUniverse);
		root.AddChild(CcSector);
		root.AddChild(CcFactions);
		root.AddChild(CcMyFaction);
		root.AddChild(CcFleet);
		root.AddChild(CcAssets);
		root.AddChild(CcComms);
		root.AddChild(CcSystem);

		_flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(_flash);
	}

	/// <summary>Ein Modal (Karte, Hamburger-Menü, Dock-Menü/-Bereich) ist offen – Weltzeiger und Tasten bleiben gesperrt.</summary>
	public bool AnyModalOpen => Map.Visible || Screens.Current != null;

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
