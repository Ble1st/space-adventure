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
	/// <summary>Phase 13: Forschung (Dienst „research“ an Forschungsstationen).</summary>
	public ResearchScreen DockResearch { get; } = new();
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
		Screens.Register(DockResearch);
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
		DockResearch.Ctx = DockCtx;
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
		// Gerätetest: schmaler und zweizeilig (ein langer Hinweis überdeckte die Zielkarte), mit dunklem Hintergrund lesbar
		_toast.Position = new Vector2(-300, 112); _toast.Size = new Vector2(600, 60);
		_toast.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_toast.AddThemeStyleboxOverride("normal", UiKit.Box(new Color(0.02f, 0.04f, 0.09f, 0.78f), null, 10, 0, 6));
		root.AddChild(_toast);

		// Gerätetest: wichtige Meldungen (Überfall, Schiffsverlust, Zeitraffer aus) — kontrastreich, bleiben bis angetippt oder mindestens 6 s
		_alert.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
		// Prüfung Phase 27: kleiner und unter dem Schiff statt über der Zielkarte/„Nächster Gegner“ (verdeckte sie teilweise) — frei bis zu den Wendetasten
		_alert.Position = new Vector2(-250, 420); _alert.Size = new Vector2(500, 80);
		_alert.MouseFilter = Control.MouseFilterEnum.Stop;
		_alert.AddThemeStyleboxOverride("panel", UiKit.Box(new Color(0.22f, 0.04f, 0.04f, 0.96f), UiKit.Red, 12, 3, 12));
		_alertText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_alertText.HorizontalAlignment = HorizontalAlignment.Center;
		_alertText.VerticalAlignment = VerticalAlignment.Center;
		_alertText.AddThemeFontSizeOverride("font_size", 18);
		_alertText.AddThemeColorOverride("font_color", UiKit.White);
		_alertText.MouseFilter = Control.MouseFilterEnum.Ignore;
		_alert.AddChild(_alertText);
		_alert.Visible = false;
		_alert.GuiInput += e => { if (Pointer.TryRead(e, out _, out _, out bool pressed, out bool motion) && pressed && !motion) HideAlert(); };
		root.AddChild(_alert);
		_alert.ZIndex = 50;   // Gerätetest Phase 28: über Dock-Bildschirmen (die später in den Baum kommen)

		// Phase 28 (Prüfpunkt f): Fortschritt des Offline-Nachholens beim Start (statt langem Splash) — dunkle Fläche mit Text und Balken, blockiert Eingaben
		_loading.Color = new Color(0.01f, 0.02f, 0.05f, 0.97f);
		_loading.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_loading.MouseFilter = Control.MouseFilterEnum.Stop;
		_loadingText.HorizontalAlignment = HorizontalAlignment.Center;
		_loadingText.AddThemeFontSizeOverride("font_size", 24);
		_loadingText.AddThemeColorOverride("font_color", UiKit.Cyan);
		_loadingText.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		_loadingText.Position = new Vector2(-300, -50); _loadingText.Size = new Vector2(600, 40);
		_loading.AddChild(_loadingText);
		_loadingBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		_loadingBar.Position = new Vector2(-250, 10); _loadingBar.Size = new Vector2(500, 14);
		_loading.AddChild(_loadingBar);
		_loading.Visible = false;
		root.AddChild(_loading);

		// Panels: Wirtschaft oben rechts, Karte vollflächig; Dock-Bildschirme kommen ans Ende (liegen oben)
		Economy.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
		Economy.Position = new Vector2(-470, 10);
		root.AddChild(Economy);
		root.AddChild(Map);
		// Gerätetest (breite Displays): Vollbild-Bildschirme sind für 1280 × 720 gebaut — sie liegen mittig in einer 1280 breiten Fläche, die Ränder deckt
		// ein dunkler Hintergrund ab (statt schwarzer Balken über das ganze Spiel); Flug-HUD, Karte und Meldungen nutzen die volle Breite
		_backdrop.Color = new Color(0.01f, 0.02f, 0.05f, 1f);
		_backdrop.MouseFilter = Control.MouseFilterEnum.Ignore;
		_backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_backdrop.Visible = false;
		root.AddChild(_backdrop);
		_center.MouseFilter = Control.MouseFilterEnum.Ignore;
		_center.AnchorLeft = 0.5f; _center.AnchorRight = 0.5f; _center.AnchorTop = 0f; _center.AnchorBottom = 1f;
		_center.OffsetLeft = -640f; _center.OffsetRight = 640f; _center.OffsetTop = 0f; _center.OffsetBottom = 0f;
		root.AddChild(_center);
		_center.AddChild(Command);
		_center.AddChild(DockMenu);
		_center.AddChild(DockTrade);
		_center.AddChild(DockInfo);
		_center.AddChild(DockHangar);
		_center.AddChild(DockTaxi);
		_center.AddChild(DockRepair);
		_center.AddChild(DockOutfitter);
		_center.AddChild(DockShipyard);
		_center.AddChild(DockStorage);
		_center.AddChild(DockAuthority);
		_center.AddChild(DockResearch);
		_center.AddChild(DockPlaceholder);
		_center.AddChild(CcUniverse);
		_center.AddChild(CcSector);
		_center.AddChild(CcFactions);
		_center.AddChild(CcMyFaction);
		_center.AddChild(CcFleet);
		_center.AddChild(CcAssets);
		_center.AddChild(CcComms);
		_center.AddChild(CcSystem);

		_flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(_flash);
	}

	/// <summary>Ein Modal (Karte, Hamburger-Menü, Dock-Menü/-Bereich) ist offen – Weltzeiger und Tasten bleiben gesperrt.</summary>
	public bool AnyModalOpen => Map.Visible || Screens.Current != null;

	readonly Control _center = new();
	readonly ColorRect _backdrop = new();
	readonly PanelContainer _alert = new();
	readonly Label _alertText = new();
	double _alertUntil;
	/// <summary>Selbsttest: sichtbare wichtige Meldung (leer = keine).</summary>
	public string TestAlert => _alert.Visible ? _alertText.Text : "";
	/// <summary>Selbsttest Phase 28: Bildschirmrechteck der großen Meldung (Überdeckung mit Zielkarte/Kampftasten prüfen).</summary>
	public Rect2 TestAlertRect => _alert.GetGlobalRect();

	/// <summary>
	/// Gerätetest: wichtige Meldung — rot hinterlegt, groß, bleibt <paramref name="seconds"/> (mindestens 6 s) oder bis sie angetippt wird.
	/// </summary>
	public void Alert(string text, float seconds = 6f, bool sticky = false)
	{
		_alertSticky = sticky;   // Gerätetest Phase 28: Schiffsverlust bleibt stehen, bis angetippt
		_alertText.Text = text;   // ein neues Ereignis ersetzt das alte (es gibt nur ein Meldungsfeld)
		_alert.Visible = true;
		double now = Time.GetTicksMsec() / 1000.0;
		_alertShown = now;
		_alertUntil = now + System.Math.Max(6f, seconds);
		// Prüfung Phase 28: nie zwei Meldungsfelder zugleich — ein laufender Hinweis weicht der wichtigen Meldung
		_toastTween?.Kill();
		_toast.Modulate = new Color(1, 1, 1, 0);
	}
	double _alertShown;
	bool _alertSticky;

	public void HideAlert() => _alert.Visible = false;

	readonly ColorRect _loading = new();
	readonly Label _loadingText = new();
	readonly SpaceAdventure.Ui.Kit.KitBar _loadingBar = new(500, 14, UiKit.Cyan);

	/// <summary>Phase 28: Fortschrittsanzeige (Offline-Nachholen beim Start); <paramref name="progress"/> 0..1.</summary>
	public void ShowLoading(string text, float progress)
	{
		_loading.Visible = true;
		_loadingText.Text = text;
		_loadingBar.Set(progress, UiKit.Cyan, 1f);
	}

	public void HideLoading() => _loading.Visible = false;
	public bool LoadingVisible => _loading.Visible;

	public override void _Process(double delta)
	{
		if (_alert.Visible && !_alertSticky && Time.GetTicksMsec() / 1000.0 > _alertUntil) _alert.Visible = false;
		_backdrop.Visible = Screens.Current != null && Screens.Current != Flight;
	}

	public void Toast(string text, float seconds = 2.5f)
	{
		// Prüfung Phase 28: ein neuer Hinweis ersetzt eine ältere Meldung, die schon mindestens 3 s stand (keine zwei Felder übereinander)
		if (_alert.Visible && !_alertSticky && Time.GetTicksMsec() / 1000.0 - _alertShown >= 3.0) _alert.Visible = false;
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
