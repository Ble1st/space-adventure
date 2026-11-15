using System;
using System.Collections.Generic;
using System.Linq;
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
	/// <summary>
	/// Gerätetest Phase 28 (Station im Flugbild nicht auffindbar): Bildschirmpositionen der Stationen des Sektors (Projektion, auch außerhalb des Bildes) mit
	/// Namen und Id — außerhalb des Bildes zeichnet die Anzeige am Rand des freien Mittelfelds einen kleinen Pfeil mit Namen, antippbar.
	/// </summary>
	public Func<IReadOnlyList<(Vector2 Screen, string Name, int Id)>>? StationScreenPositions { get; set; }
	/// <summary>Zuletzt gezeichnete Randmarker (Bildschirmposition, Stations-Id) — für das Antippen.</summary>
	public List<(Vector2 Pos, int Id)> EdgeMarkers { get; } = new();
	/// <summary>Runde 16: freies Feld (Bildpunkte) für Randpfeile; Zielpfeil (Bildposition, Name, feindlich) — gesetzt von Main.</summary>
	public Func<Rect2>? FreeField { get; set; }
	public Func<(Vector2 Screen, string Label, bool Hostile)?>? TargetEdge { get; set; }
	public bool TargetArrowShown { get; set; }
	public Vector2 TargetArrowAt { get; set; }

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

		// Wichtige Meldungen (Überfall, Schiffsverlust, Zeitraffer aus). Zweiter Gerätetest Phase 28: eigene Ebene über allem (CanvasLayer 100) —
		// sie bekommt Tipps zuerst und fängt sie selbst ab (vorher lag sie im Baum vor den Dock-Bildschirmen, der Tipp ging an die Kachel darunter);
		// schmal oben mittig unter der Kopfleiste, höchstens drei Zeilen, eigener „✕“-Knopf, Höchstdauer 20 s
		_alertLayer.Layer = 100;
		AddChild(_alertLayer);
		var alertRoot = _alertRoot;
		alertRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
		_alertLayer.AddChild(alertRoot);
		// Runde 16: Diagnosezeile (Kamera) — oben links unter dem Statusfeld, klein, nur auf Wunsch
		_debugLine.AddThemeFontSizeOverride("font_size", 12);
		_debugLine.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.4f));
		_debugLine.AddThemeColorOverride("font_outline_color", Colors.Black);
		_debugLine.AddThemeConstantOverride("outline_size", 4);
		_debugLine.MouseFilter = Control.MouseFilterEnum.Ignore;
		_debugLine.Position = new Vector2(8, 300); _debugLine.Size = new Vector2(900, 18);
		_debugLine.Visible = false;
		alertRoot.AddChild(_debugLine);
		_alert.MouseFilter = Control.MouseFilterEnum.Stop;
		_alert.Size = new Vector2(AlertWidth, AlertHeight);
		_alert.AddThemeStyleboxOverride("panel", UiKit.Box(new Color(0.22f, 0.04f, 0.04f, 0.97f), UiKit.Red, 12, 3, 0));
		_alertText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_alertText.VerticalAlignment = VerticalAlignment.Center;
		_alertText.AddThemeFontSizeOverride("font_size", 15);
		_alertText.AddThemeColorOverride("font_color", UiKit.White);
		_alertText.MouseFilter = Control.MouseFilterEnum.Ignore;
		_alertText.MaxLinesVisible = 3;
		_alertText.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_alertText.ClipText = true;
		_alertText.Position = new Vector2(12, 6); _alertText.Size = new Vector2(AlertWidth - 12 - 64, AlertHeight - 12);
		_alert.AddChild(_alertText);
		_alertClose.Position = new Vector2(AlertWidth - 62, (AlertHeight - 56) / 2f);
		_alertClose.Pressed += HideAlert;
		_alert.AddChild(_alertClose);
		_alert.Visible = false;
		// Tipp irgendwo auf die Meldung schließt sie; das Ereignis endet hier (Stop + AcceptEvent), nichts darunter löst aus
		_alert.GuiInput += e =>
		{
			if (!Pointer.TryRead(e, out _, out _, out bool pressed, out bool motion)) return;
			_alert.AcceptEvent();
			if (!pressed && !motion) HideAlert();
		};
		alertRoot.AddChild(_alert);
		// Gerätetest Phase 28: Hinweise (z. B. „Gespeichert“) über allen Bildschirmen — vorher lagen sie unter dem Hintergrund offener Menüs
		alertRoot.AddChild(_toast);

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
		// Runde 34 (UI-ANPASSUNG.md E3): Dock-Menü und Dock-Bereiche liegen in einer eigenen Schicht über die ganze Fläche (statt mittig in 1280 Breite), mit
		// dem Faktor der Dock-Schicht (DockLayout.FitFactor) — gleiche Idee wie das Flug-HUD
		_dockLayer.MouseFilter = Control.MouseFilterEnum.Ignore;
		root.AddChild(_dockLayer);
		foreach (var sc in new Control[] { DockMenu, DockTrade, DockInfo, DockHangar, DockTaxi, DockRepair, DockOutfitter, DockShipyard, DockStorage, DockAuthority, DockResearch, DockPlaceholder })
			_dockLayer.AddChild(sc);
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
		ApplyLayerScale();   // Runde 33
	}

	/// <summary>Ein Modal (Karte, Hamburger-Menü, Dock-Menü/-Bereich) ist offen – Weltzeiger und Tasten bleiben gesperrt.</summary>
	public bool AnyModalOpen => Map.Visible || Screens.Current != null;

	readonly Control _center = new();
	/// <summary>Runde 34: Schicht der Dock-Bildschirme (ganze Fläche, Faktor <see cref="DockFactor"/>).</summary>
	readonly Control _dockLayer = new();
	/// <summary>Runde 34: wirksamer Faktor der Dock-Schicht (Stufe, gesenkt bis jeder Bereich seine Mindestfläche hat).</summary>
	public float DockFactor => _dockLayer.Scale.X;
	public Control DockLayerNode => _dockLayer;
	/// <summary>Runde 33: Ebene von Meldung, Hinweis und Diagnosezeile — im Flug mit dem Faktor der Flug-Schicht skaliert, über Vollbildern mit 1.</summary>
	readonly Control _alertRoot = new();
	/// <summary>Runde 33: aktueller Maßstab der Meldungsebene; <see cref="AlertSpace"/> = ihre logische Fläche.</summary>
	float _alertScale = 1f;
	Vector2 AlertSpace => GetViewport().GetVisibleRect().Size / _alertScale;

	/// <summary>
	/// Runde 33 (UI-ANPASSUNG.md E2): Faktor je Schicht. Flug-HUD (und im Flug Meldung/Hinweis) mit <see cref="UiScale.Factor"/>; Vollbild-Bildschirme,
	/// Menü, Karte und Zielmarker bleiben bei 1 (E3–E5). Jedes Bild geprüft — billig, nur bei Änderung wird umgebaut.
	/// </summary>
	/// <summary>Runde 34: Schichtfaktoren sofort neu rechnen (z. B. nach dem Umschalten der UI-Größe, damit der Hinweis die neuen Werte nennt).</summary>
	public void UpdateLayerScales() => ApplyLayerScale();

	void ApplyLayerScale()
	{
		var vp = GetViewport().GetVisibleRect().Size;
		float f = UiScale.Factor;
		Flight.ApplyScale(vp, f);
		// Runde 34: Dock-Schicht — ein Faktor für alle Dock-Bildschirme (Core: DockLayout.FitFactor), nur bei Änderung neu
		var dkey = (vp, f, UiScale.SafeInsets1());
		if (dkey != _dockKey)
		{
			_dockKey = dkey;
			float df = SpaceSim.Core.Ui.DockLayout.FitFactor(vp.X, vp.Y, dkey.Item3, f);
			_dockLayer.AnchorLeft = 0f; _dockLayer.AnchorTop = 0f; _dockLayer.AnchorRight = 0f; _dockLayer.AnchorBottom = 0f;
			_dockLayer.Position = Vector2.Zero;
			_dockLayer.Scale = new Vector2(df, df);
			_dockLayer.Size = vp / df;
			UiScale.DockFactor = df;
		}
		bool flight = Screens.Current == null || Screens.Current == Flight;
		float s = flight ? Flight.Scale.X : UiScale.ScreensFactor;   // Runde 33b: der wirksame Faktor der Flug-Schicht (kann unter der Stufe liegen)
		var size = vp / s;
		if (Mathf.Abs(_alertScale - s) > 1e-4f || (_alertRoot.Size - size).LengthSquared() > 0.01f)
		{
			_alertScale = s;
			_alertRoot.AnchorLeft = 0f; _alertRoot.AnchorTop = 0f; _alertRoot.AnchorRight = 0f; _alertRoot.AnchorBottom = 0f;
			_alertRoot.Position = Vector2.Zero;
			_alertRoot.Scale = new Vector2(s, s);
			_alertRoot.Size = size;
		}
	}
	/// <summary>Runde 33: Maßstab der Meldungsebene (Selbsttest).</summary>
	public float TestAlertScale => _alertScale;
	readonly ColorRect _backdrop = new();
	const float AlertWidth = 560f, AlertHeight = 62f;
	/// <summary>Zweiter Gerätetest Phase 28: Höchstdauer jeder Meldung (auch der stehenden Verlustmeldung).</summary>
	public const float AlertMaxSeconds = 20f;
	readonly CanvasLayer _alertLayer = new();
	readonly KitButton _alertClose = new("✕", new Vector2(56, 56));
	readonly Panel _alert = new();
	readonly Label _alertText = new();
	double _alertUntil;
	/// <summary>Selbsttest: sichtbare wichtige Meldung (leer = keine).</summary>
	public string TestAlert => _alert.Visible ? _alertText.Text : "";
	/// <summary>Selbsttest Phase 28: Bildschirmrechteck der großen Meldung (Überdeckung mit Zielkarte/Kampftasten prüfen).</summary>
	public Rect2 TestAlertRect => _alert.GetGlobalRect();

	/// <summary>
	/// Gerätetest: wichtige Meldung — rot hinterlegt, groß, bleibt <paramref name="seconds"/> (mindestens 6 s) oder bis sie angetippt wird.
	/// </summary>
	/// <summary>Runde 21: Text der sichtbaren Meldung ersetzen (Countdown der Vorwarnung), nur wenn sie noch <paramref name="expect"/> zeigt; Dauer bleibt.</summary>
	public bool ReplaceAlertText(string expect, string text)
	{
		if (!_alert.Visible || _alertText.Text != expect) return false;
		_alertText.Text = text;
		return true;
	}

	public void Alert(string text, float seconds = 6f, bool sticky = false)
	{
		double now = Time.GetTicksMsec() / 1000.0;
		// Zweiter Gerätetest Phase 28: eine stehende Meldung (Schiffsverlust) wird von einer gewöhnlichen nicht verdrängt — die kommt als Hinweis darunter
		if (_alert.Visible && _alertSticky && !sticky) { Toast(text, 4f); return; }
		_alertSticky = sticky;
		_alertText.Text = text;   // ein neues Ereignis ersetzt das alte (es gibt nur ein Meldungsfeld)
		_alert.Visible = true;
		_alertShown = now;
		_alertUntil = now + (sticky ? AlertMaxSeconds : System.Math.Clamp(seconds, 6f, AlertMaxSeconds));
		PlaceAlert();
		// Prüfung Phase 28: nie zwei Meldungsfelder zugleich — ein laufender Hinweis weicht der wichtigen Meldung
		_toastTween?.Kill();
		_toast.Modulate = new Color(1, 1, 1, 0);
	}

	/// <summary>
	/// Oben: im Flug mittig unter Sektorchip und Lagezeile (frei bis zur Zielkarte, höchstens drei Zeilen); über Vollbildern und dem Dock-Menü in der
	/// Kopfleiste zwischen Bereichstitel und Kasse, zwei Zeilen hoch über dem Fensterrand (Prüfung Runde 2: sie verdeckte Lagezeile bzw. Bereichstitel).
	/// </summary>
	void PlaceAlert()
	{
		var vp = AlertSpace;
		bool screen = Screens.Current != null && Screens.Current != Flight;
		// Gerätetest Phase 28: über dem Dock-Menü (schmales Fenster in der Mitte, Titel oben) links neben dem Fenster, mehrzeilig — oben verdeckte es den Titel
		bool dockMenu = Screens.Current == DockMenu;
		float side = DockMenuSide();
		// Runde 12: im Flug unten, links der Kampftasten (zweite Schachtspalte ab x 906) — 536 breit ab der Bildmitte − 280
		var size = dockMenu ? new Vector2(MathF.Max(300f, side), 150f) : screen ? new Vector2(ScreenAlertWidth, ScreenAlertHeight) : new Vector2(FlightAlertWidth, AlertHeight);
		_alert.Size = size;
		// Runde 33: im Flug unten verankert (720er-Entwurf: y 508 = Unterkante − 212)
		_alert.Position = dockMenu ? new Vector2(10f, 90f) : screen ? new Vector2((vp.X - 1280f) / 2f + ScreenAlertX, 0f) : new Vector2(vp.X / 2f - 280f, vp.Y - (720f - FlightAlertY));
		// Runde 34: Dock-Bereiche liegen über die ganze Fläche — die Meldung in die Lücke der Kopfleiste (Core: DockShell.AlertSlot, in Bildpunkten)
		if (screen && Screens.Current is DockAreaScreen da && da.AlertSlotGlobal is { } slot)
		{
			size = new Vector2(slot.Size.X, ScreenAlertHeight);
			_alert.Size = size;
			_alert.Position = slot.Position;
		}
		// Runde 19 (Handy-Test B_4): im Flug endet die Meldung über der Beschriftung „RÜCK · max 20 %“ (nach dem Rand-Layout der echten Auflösung)
		float realH = MathF.Max(size.Y, _alert.GetCombinedMinimumSize().Y);   // die Meldung kann mit ihrem Text wachsen
		if (!dockMenu && !screen && Flight.ReverseLabelLocal is { Size.Y: > 0f } rl && new Rect2(_alert.Position, new Vector2(size.X, realH)).Intersects(rl.Grow(6f)))
			_alert.Position = new Vector2(_alert.Position.X, rl.Position.Y - 6f - realH);
		// Runde 33 (E2): im Kompakt-HUD (UI-Faktor > 1) kann diese Lage auf Tasten fallen — dann die nächste freie Lage (FlightHudLayout.FindSpot, Core)
		if (!dockMenu && !screen && Flight.Layout is { Compact: true })
		{
			var occ = Flight.OccupiedLocal();
			if (Flight.ShipRectLocal() is { } shipR) occ.Add(shipR);   // Runde 33b: nie über dem eigenen Schiff
			var want = new Rect2(_alert.Position, new Vector2(size.X, realH));
			if (occ.Exists(r => r.Intersects(want)))
			{
				var key = (vp, realH, occ.Count, occ.Aggregate(0f, (a, r) => a + r.Position.X * 3f + r.Position.Y * 7f + r.Size.X));
				if (key != _spotKey)
				{
					_spotKey = key;
					var list = occ.Select(r => new SpaceSim.Core.Ui.UiRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y)).ToList();
					var area = new SpaceSim.Core.Ui.UiRect(0, 0, vp.X, vp.Y);
					_spot = SpaceSim.Core.Ui.FlightHudLayout.FindSpot(area, list, size.X, realH, 360f, vp.X / 2f, want.Position.Y);
					// kein Platz neben allem (Gefecht bei Faktor 1,3 auf 16:9): dann nur Tasten frei lassen — die Meldung darf kurz über Status/Sektorzeilen liegen
					var tapsAndShip = Flight.OccupiedLocal(interactiveOnly: true);
					if (Flight.ShipRectLocal() is { } shipR2) tapsAndShip.Add(shipR2);
					_spot ??= SpaceSim.Core.Ui.FlightHudLayout.FindSpot(area, tapsAndShip
						.Select(r => new SpaceSim.Core.Ui.UiRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y)).ToList(), size.X, realH, 300f, vp.X / 2f, want.Position.Y);
				}
				if (_spot is { } sp) { size = new Vector2(sp.W, size.Y); _alert.Size = size; _alert.Position = new Vector2(sp.X, sp.Y); }
			}
		}
		_alertText.MaxLinesVisible = dockMenu ? 6 : screen ? 2 : 3;
		_alertText.AddThemeFontSizeOverride("font_size", screen ? 14 : 15);
		_alertText.Position = new Vector2(12, 4); _alertText.Size = new Vector2(size.X - 12 - 64, size.Y - 8);
		_alertClose.Position = new Vector2(size.X - 60, dockMenu ? size.Y - 60 : (size.Y - 56) / 2f);
	}
	const float DockMenuWidth = 580f;
	/// <summary>Runde 34: Platz links neben der Dock-Menü-Karte (Bildpunkte; die Karte liegt in der skalierten Dock-Schicht).</summary>
	float DockMenuSide() => DockMenu.CardGlobal is { } c && c.Size.X > 0f ? c.Position.X - 20f : (AlertSpace.X - DockMenuWidth) / 2f - 20f;
	(Vector2, float, SpaceSim.Core.Ui.UiInsets) _dockKey;
	(Vector2, float, int, float) _spotKey;
	(Vector2, string, int, float) _toastKey;
	SpaceSim.Core.Ui.UiRect? _toastSpot;
	SpaceSim.Core.Ui.UiRect? _spot;
	// Runde 12 (Handy-Test: Meldung und Beschusszeile verdeckten den Gegner vor dem Bug): im Flug unten mittig über dem Tempo-Regler — hinter dem eigenen
	// Schiff, dort steht im Gefecht kein Gegner (die Kamera blickt über das Heck nach vorn); oben bleibt die Bildmitte frei
	const float FlightAlertWidth = 536f;
	const float FlightAlertY = 508f, ScreenAlertX = 420f, ScreenAlertWidth = 600f, ScreenAlertHeight = 56f;

	/// <summary>Runde 14: Fläche der Meldung im Flug (für das freie Feld der Gefechtskamera); null = keine sichtbar oder nicht im Flug.</summary>
	/// <summary>Runde 17: Fläche der sichtbaren Kurzmeldung (Randpfeil-Beschriftungen weichen ihr aus); null = keine.</summary>
	public Rect2? ToastRect => _toast.Modulate.A > 0.05f && _toast.Text.Length > 0 ? _toast.GetGlobalRect() : null;
	/// <summary>Runde 17: im Gefecht/in der Warnphase nur wenige Stations-Randpfeile (Main setzt das je Bild).</summary>
	public bool EdgeThin { get; set; }
	public Rect2? AlertRect => _alert.Visible && (Screens.Current == null || Screens.Current == Flight) ? _alert.GetGlobalRect() : null;

	/// <summary>Selbsttest: Mittelpunkt der Meldung und des „✕“ (für simulierte Tipps).</summary>
	public Vector2 TestAlertCenter => _alert.GetGlobalRect().GetCenter();
	public Vector2 TestAlertCloseCenter => _alertClose.GetGlobalRect().GetCenter();
	public bool TestAlertSticky => _alert.Visible && _alertSticky;

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
		ApplyLayerScale();
		if (_alert.Visible && Time.GetTicksMsec() / 1000.0 > _alertUntil) _alert.Visible = false;
		if (_alert.Visible) PlaceAlert();
		// der Hinweis rückt unter eine stehende Meldung (statt sie zu überdecken)
		// Prüfung Runde 2: unter einer Meldung im Flug schmaler und links der Kampftasten („Nächster Gegner“ beginnt bei x 764 im 1280er-Raster)
		bool onScreen = Screens.Current != null && Screens.Current != Flight;
		bool below = _alert.Visible && !onScreen;
		// Runde 4: über Vollbildern unten mittig (zwischen „← Menü“/„← Flug“ und „Abdocken“), im Flug mit Abstand zur Lage-/Brandzeile
		var vpSize = AlertSpace;   // Runde 33: in Einheiten der Meldungsebene (im Flug skaliert)
		if (Screens.Current == DockMenu)   // Dock-Menü: links neben dem Fenster, unter einer Meldung
		{
			_toast.Position = new Vector2(10f, _alert.Visible ? 250f : 90f);
			_toast.Size = new Vector2(MathF.Max(300f, DockMenuSide()), 0f);   // Runde 21: Höhe folgt dem Text
		}
		else if (!onScreen && EdgeThin)
		{
			// Runde 17 (Handy-Test: Warnphase — Zielpfeil, Stationsnamen und Start-Hinweis als Textknäuel oben): im Gefecht/in der Warnphase links unter den
			// Leistungstasten, das freie Feld oben bleibt für Ziel und Zielpfeil
			_toast.Position = new Vector2(Flight.LeftColumnX(), Flight.LeftColumnTop());
			_toast.Size = new Vector2(300f, 0f);   // Runde 21: Höhe folgt dem Text
		}
		else
		{
			_toast.Position = new Vector2(vpSize.X / 2f - 300f, onScreen ? vpSize.Y - 70f : 122f);
			float tw = below ? 416f : 600f;
			// Runde 17 (Handy-Test: Hintergrund des Hinweises überdeckte den Rand von „Nächster Gegner“): im Flug vor der ersten HUD-Fläche rechts enden
			if (!onScreen)
				foreach (var r in Flight.OccupiedLocal())
					if (r.Position.X > vpSize.X / 2f && r.Position.Y < _toast.Position.Y + 100f && r.End.Y > _toast.Position.Y)
						tw = MathF.Min(tw, MathF.Max(300f, r.Position.X - 6f - _toast.Position.X));
			_toast.Size = new Vector2(tw, 0f);   // Runde 21: Höhe folgt dem Text
			// Runde 34 (Handytest r33 c2/c4: über dem offenen Menü lief ein mehrzeiliger Hinweis unten aus dem Bild): über Vollbildern endet er 12 über dem Rand
			if (onScreen)
			{
				float th = MathF.Max(_toast.Size.Y, _toast.GetCombinedMinimumSize().Y);
				_toast.Position = new Vector2(_toast.Position.X, MathF.Min(_toast.Position.Y, vpSize.Y - 12f - th));
			}
		}
		// Runde 33 (E2): im Kompakt-HUD (UI-Faktor > 1) kann die Wunschlage auf Tasten oder Beschuss-/Fluchtzeile fallen — dann die nächste freie Lage
		if (!onScreen && Screens.Current != DockMenu && Flight.Layout is { Compact: true } && _toast.Modulate.A > 0.02f && _toast.Text.Length > 0)
		{
			var occ = Flight.OccupiedLocal();
			if (Flight.ShipRectLocal() is { } shipR) occ.Add(shipR);   // Runde 33b: nie über dem eigenen Schiff
			if (_alert.Visible) occ.Add(new Rect2(_alert.Position, _alert.Size).Grow(6f));
			float th = MathF.Max(_toast.Size.Y, _toast.GetCombinedMinimumSize().Y);
			var want = new Rect2(_toast.Position, new Vector2(_toast.Size.X, th));
			if (occ.Exists(r => r.Intersects(want)))
			{
				var key = (vpSize, _toast.Text, occ.Count, occ.Aggregate(0f, (a, r) => a + r.Position.X * 3f + r.Position.Y * 7f + r.Size.X));
				if (key != _toastKey)
				{
					_toastKey = key;
					var list = occ.Select(r => new SpaceSim.Core.Ui.UiRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y)).ToList();
					_toastSpot = SpaceSim.Core.Ui.FlightHudLayout.FindSpot(new SpaceSim.Core.Ui.UiRect(0, 0, vpSize.X, vpSize.Y), list, want.Size.X, th,
						MathF.Min(want.Size.X, 280f), want.GetCenter().X, want.Position.Y, 12f);
				}
				if (_toastSpot is { } sp) { _toast.Position = new Vector2(sp.X, sp.Y); _toast.Size = new Vector2(sp.W, 0f); }
			}
		}
		_backdrop.Visible = Screens.Current != null && Screens.Current != Flight;
		// Runde 7: Beschuss- und Fluchtzeile unter einen mehrzeiligen Hinweis im Flug
		bool toastShown = _toast.Modulate.A > 0.02f && !onScreen && Screens.Current != DockMenu;
		Flight.AvoidToast(toastShown ? _toast.Position.Y + MathF.Max(_toast.Size.Y, _toast.GetCombinedMinimumSize().Y) : 0f);
	}

	/// <summary>Selbsttest Runde 7: Lage des Hinweises (mit gewachsener Höhe).</summary>
	public string TestToastText => _toast.Modulate.A > 0.05f ? _toast.Text : "";
	public Rect2 TestToastRect => new(_toast.Position, new Vector2(_toast.Size.X, MathF.Max(_toast.Size.Y, _toast.GetCombinedMinimumSize().Y)));

	readonly Label _debugLine = new();
	/// <summary>Runde 16: Diagnosezeile setzen (null = aus).</summary>
	public void SetDebugLine(string? text) { _debugLine.Visible = text is not null; if (text is not null) _debugLine.Text = text; }
	public string TestDebugLine => _debugLine.Visible ? _debugLine.Text : "";

	public void Toast(string text, float seconds = 2.5f)
	{
		// Prüfung Phase 28: ein neuer Hinweis ersetzt eine ältere Meldung, die schon mindestens 3 s stand (keine zwei Felder übereinander)
		if (_alert.Visible && !_alertSticky && Time.GetTicksMsec() / 1000.0 - _alertShown >= 3.0) _alert.Visible = false;
		_toast.Text = text;
		// Runde 21 (Handy-Test b08: nach einem mehrzeiligen Hinweis blieb die dunkle Fläche dreizeilig hoch): Höhe auf den neuen Text zurücksetzen
		_toast.Size = new Vector2(_toast.Size.X, 0f);
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
		/// <summary>Freies Mittelfeld (zwischen Status-/Kampfspalten), an dessen Rand die Stations-Randmarker liegen.</summary>
		// Runde 16: das freie Feld der Gefechtskamera (größtes Rechteck ohne HUD-Fläche) — Randpfeile liegen strikt darin
		Rect2 Field() => (Hud.FreeField?.Invoke() is { } f && f.Size.X > 40f && f.Size.Y > 40f ? f : new Rect2(Size.X * 0.27f, 180f, Size.X * 0.46f, Size.Y - 180f - 130f)).Grow(-24f);

		readonly List<Rect2> _labelBoxes = new();
		/// <summary>Runde 17: HUD-Flächen samt Kurzmeldung und Meldung — Beschriftungen, Pfeile und Ringe weichen ihnen aus.</summary>
		List<Rect2> Occupied()
		{
			var occ = Hud.Flight.OccupiedRects();
			if (Hud.ToastRect is { } tr) occ.Add(tr.Grow(6f));
			if (Hud.AlertRect is { } ar) occ.Add(ar.Grow(6f));
			return occ;
		}
		Rect2? _arrowBox;

		/// <summary>Runde 16: roter Zielpfeil am Rand des freien Felds, wenn das gewählte (feindliche) Ziel außerhalb liegt — immer, nicht nur in der Warnphase.</summary>
		void DrawTargetArrow()
		{
			Hud.TargetArrowShown = false;
			if (Hud.TargetEdge?.Invoke() is not { } t || Hud.Screens.Current != null && Hud.Screens.Current != Hud.Flight) return;
			var field = Field();
			if (field.HasPoint(t.Screen)) return;
			var c = field.GetCenter();
			var dir = (t.Screen - c).Normalized();
			if (dir == Vector2.Zero) return;
			float k = Mathf.Min(Mathf.Abs(field.Size.X / 2f / (dir.X == 0 ? 0.0001f : dir.X)), Mathf.Abs(field.Size.Y / 2f / (dir.Y == 0 ? 0.0001f : dir.Y)));
			var tip = c + dir * k;
			var side = new Vector2(-dir.Y, dir.X) * 14f;
			var col = t.Hostile ? new Color(1f, 0.3f, 0.25f, 0.95f) : new Color(0.5f, 1f, 0.6f, 0.95f);
			DrawColoredPolygon(new[] { tip, tip - dir * 28f + side, tip - dir * 28f - side }, col);
			var font = ThemeDB.FallbackFont;
			string label = t.Label.Length > 22 ? t.Label[..21] + "…" : t.Label;
			float w = font.GetStringSize(label, HorizontalAlignment.Left, -1, 14).X;
			var lp = tip - dir * 44f - new Vector2(w / 2f, -5f);
			var box = new Rect2(lp - new Vector2(4f, 14f), new Vector2(w + 8f, 19f));
			// Runde 17: Beschriftung nicht über HUD/Kurzmeldung (Warnphase: Textknäuel oben) — sonst weiter innen, notfalls nur der Pfeil
			var occ = Occupied();
			for (int step = 0; step < 4 && occ.Exists(r => r.Intersects(box)); step++) { lp -= dir * 22f; box.Position -= dir * 22f; }
			_arrowBox = new Rect2(tip - new Vector2(30f, 30f), new Vector2(60f, 60f)).Merge(box);
			if (!occ.Exists(r => r.Intersects(box)))
			{
				DrawRect(box, new Color(0.1f, 0.02f, 0.02f, 0.8f));
				DrawString(font, lp, label, HorizontalAlignment.Left, -1, 14, col);
			}
			Hud.TargetArrowShown = true;
			Hud.TargetArrowAt = tip;
		}

		void DrawEdgeStations()
		{
			Hud.EdgeMarkers.Clear();
			_labelBoxes.Clear();
			if (Hud.StationScreenPositions?.Invoke() is not { } list || Hud.Screens.Current != null && Hud.Screens.Current != Hud.Flight) return;
			var font = ThemeDB.FallbackFont;
			var col = new Color(0.45f, 0.85f, 1f, 0.9f);
			var field = Field();
			var c = field.GetCenter();
			var occupied = Occupied();   // Runde 4: Namen und Pfeile nie über HUD-Tasten/-Beschriftungen (Runde 17: auch Kurzmeldung/Meldung)
			// Runde 17: dem Gefechtsziel und seinem Pfeil ausweichen; im Gefecht nur die drei wichtigsten Stationen (verteidigte zuerst)
			if (_arrowBox is { } ab) occupied.Add(ab.Grow(12f));
			if (Hud.TargetScreenPosition?.Invoke() is { } tsp) occupied.Add(new Rect2(tsp - new Vector2(60f, 40f), new Vector2(120f, 80f)));
			int maxMarkers = Hud.EdgeThin ? 3 : 10;
			foreach (var (screen, name, id) in list)
			{
				// Runde 16 (Handy-Test: Pfeile lagen unter „Karte“, Stationen unter HUD-Tasten waren nicht wählbar): nur Stationen im freien Feld sind selbst
				// antippbar — alles andere (außerhalb des Bildes oder unter einer HUD-Fläche) bekommt einen Pfeil am Rand des freien Feldes
				if (field.Grow(24f).HasPoint(screen) && !occupied.Exists(r => r.HasPoint(screen))) continue;
				var dir = (screen - c).Normalized();
				if (dir == Vector2.Zero) continue;
				float t = Mathf.Min(Mathf.Abs(field.Size.X / 2f / (dir.X == 0 ? 0.0001f : dir.X)), Mathf.Abs(field.Size.Y / 2f / (dir.Y == 0 ? 0.0001f : dir.Y)));
				var tip = c + dir * t;
				// dicht beieinander liegende Stationen: nur ein Marker (Namen liefen übereinander)
				if (occupied.Exists(r => r.HasPoint(tip))) continue;
				bool near = false;
				foreach (var (mp, _) in Hud.EdgeMarkers) if (mp.DistanceTo(tip - dir * 18f) < 70f) { near = true; break; }
				if (near) continue;
				if (Hud.EdgeMarkers.Count >= maxMarkers) break;
				var side = new Vector2(-dir.Y, dir.X) * 9f;
				DrawColoredPolygon(new[] { tip, tip - dir * 18f + side, tip - dir * 18f - side }, col);
				string label = name.Length > 20 ? name[..19] + "…" : name;
				float w = font.GetStringSize(label, HorizontalAlignment.Left, -1, 13).X;
				var lp = tip - dir * 30f - new Vector2(w / 2f, -4f);
				var box = new Rect2(lp - new Vector2(4f, 13f), new Vector2(w + 8f, 18f));
				// Namen nur, wo sie nicht über einem schon gezeichneten Namen liegen (der Pfeil bleibt)
				if (!_labelBoxes.Exists(b => b.Intersects(box)) && !occupied.Exists(r => r.Intersects(box)))
				{
					_labelBoxes.Add(box);
					DrawRect(box, new Color(0.02f, 0.04f, 0.09f, 0.75f));
					DrawString(font, lp, label, HorizontalAlignment.Left, -1, 13, col);
				}
				Hud.EdgeMarkers.Add((tip - dir * 18f, id));
			}
		}

		public Hud Hud = null!;
		public TargetMarker() { MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }
		public override void _Process(double delta) => QueueRedraw();

		public override void _Draw()
		{
			_arrowBox = null;
			DrawTargetArrow();   // Runde 16 (Runde 17: zuerst — die Stationspfeile weichen ihm aus)
			DrawEdgeStations();
			var p = Hud.TargetScreenPosition?.Invoke();
			if (p is not { } pos) return;
			var rect = new Rect2(Vector2.Zero, Size).Grow(-30f);
			var col = new Color(1f, 0.9f, 0.3f);
			// Runde 17: Zielring nie über HUD-Tasten (rechts über Laser/Geschütz gesehen)
			if (rect.HasPoint(pos)) { if (!Occupied().Exists(r => r.Intersects(new Rect2(pos - new Vector2(24f, 24f), new Vector2(48f, 48f))))) DrawArc(pos, 22f, 0, Mathf.Tau, 32, col, 3f); return; }
			var c = Size / 2f;
			var dir = (pos - c).Normalized();
			float t = Mathf.Min(Mathf.Abs(rect.Size.X / 2f / (dir.X == 0 ? 0.0001f : dir.X)), Mathf.Abs(rect.Size.Y / 2f / (dir.Y == 0 ? 0.0001f : dir.Y)));
			var tip = c + dir * t;
			var side = new Vector2(-dir.Y, dir.X) * 12f;
			DrawColoredPolygon(new[] { tip, tip - dir * 24f + side, tip - dir * 24f - side }, col);
		}
	}
}
