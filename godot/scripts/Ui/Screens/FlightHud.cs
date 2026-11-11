using System;
using System.Collections.Generic;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Weapons;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>Anzeigedaten einer Waffenschacht-Taste (Zustand kommt aus SpaceSim.Core.Weapons.FiringArc).</summary>
public sealed class BayView
{
	public required string Name, Place, Detail;
	public required BayState State;
	/// <summary>Nachlade-Fortschritt 0..1 (Balken bei <see cref="BayState.Reloading"/>).</summary>
	public float Reload;
	public Facing Facing;
	public float ArcDegrees;
}

/// <summary>Pro Frame vom Main gelieferte HUD-Daten – die Oberfläche folgt den Daten, sie kennt keine Spielregeln.</summary>
public sealed class FlightHudData
{
	public float Hull, Credits;
	/// <summary>Tankinhalt und Tankgröße des Spielerschiffs (echt, Phase 4c); Kapazität 0 = kein Treibstoffsystem.</summary>
	public float Fuel, FuelCapacity;
	public string SectorName = "", FactionName = "";
	/// <summary>Phase 11: Fahndungszeile des Spielers (leer = sauber, dann ausgeblendet) und Lage des Sektors (Sicherheit/Bedrohung).</summary>
	public string Wanted = "", Security = "";
	public bool WantedSevere;
	// Zielkarte
	public string TargetName = "Kein Ziel";
	public string TargetInfo = "Station, Gate oder Asteroid antippen";
	public string TargetArc = "";
	public bool TargetHasInfo;
	public float TargetRelBearing;      // Peilung des Ziels relativ zur Schiffsnase, Grad (für Radar)
	public float TargetDistance = -1f;
	// Schalter
	public bool AutopilotOn, AutoMineOn, TractorOn, FastOn;
	// Slider
	public float SliderValue;           // 0..1 (nur vorwärts)
	public float Speed;                 // m/s Ist
	// Kontexttaste
	public string ContextLabel = "";    // "Andocken …" / "Springen …" / "" = versteckt
	// Waffenschächte
	public IReadOnlyList<BayView> Bays = Array.Empty<BayView>();
	public int ReadyBays, FireBays;
	// Feuerwinkel-Kegel um das Schiff
	public Vector2? ShipScreen;
	/// <summary>Bildschirmwinkel der Schiffsnase in Grad (atan2, y nach unten) – hängt von der Kameradrehung ab, Basis der Kegel.</summary>
	public float ShipScreenNoseDeg;
	public Vector2? TargetScreen;
	public bool TargetInArc;
}

/// <summary>
/// Flug-HUD (UI.md Abschnitt 2): Statusleiste oben (Hülle und Credits echt, Schild/Sprit/Leistung Platzhalter),
/// Sektorchip Mitte, Karte, Zeitraffer und „Kamera ↺“ (Standardansicht) oben rechts, Zielkarte mit Autopilot/Auto-Mining
/// und Kontexttaste, unten links Hamburger-Menü und Wendetasten (RÜCK als eigene kleinere Haltetaste dazwischen),
/// unten rechts der Tempo-Slider, rechts die Waffenschächte.
/// </summary>
public partial class FlightHud : Control, IScreen
{
	public string Id => "flight";
	/// <summary>Selbsttest: angezeigte Fahndungs- und Lagezeile (Phase 11).</summary>
	public string TestWanted => _wanted.Visible ? _wanted.Text : "";
	public string TestSecurity => _security.Text;
	public string Title => "Flug";
	public Control Node => this;
	public bool IsOpen => Visible;

	public event Action? HamburgerPressed, MapPressed, FastPressed, AutopilotPressed, AutoMinePressed, ContextPressed, AllFire, CommandPressed;
	/// <summary>Finger auf einer Waffenschacht-Taste (Index in <see cref="FlightHudData.Bays"/>).</summary>
	public event Action<int>? BayDown;

	// oben
	KitBar _hullBar = null!, _fuelBar = null!;
	Label _fuelVal = null!;
	Label _hullVal = null!;
	Label _credits = null!, _sector = null!, _factionChip = null!, _wanted = null!, _security = null!;
	KitButton _mapBtn = null!, _fastBtn = null!, _cmdBtn = null!;
	// Zielkarte
	Label _tName = null!, _tInfo = null!, _tArc = null!;
	Radar _radar = null!;
	KitButton _autopilotBtn = null!, _autoMineBtn = null!, _contextBtn = null!;
	// unten links
	KitButton _turnL = null!, _turnR = null!, _reverse = null!, _hamburger = null!;
	// Slider
	KitSlider _slider = null!;
	// Waffenschächte
	readonly List<KitButton> _bayButtons = new();
	KitButton _allFire = null!;
	Label _ready = null!;
	// Kegel um das Schiff
	readonly ArcOverlay _arcs = new();

	FlightHudData _d = new();

	public FlightHud()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public bool TurnLeftHeld => _turnL.Held;
	public bool TurnRightHeld => _turnR.Held;
	public bool ReverseHeld => _reverse.Held;
	public float SliderValue => _slider.Value;
	public bool AutopilotOn => _autopilotBtn.On;
	public bool AutoMineOn => _autoMineBtn.On;
	public bool FastOn => _fastBtn.On;

	/// <summary>Zähler ungelesener Comms-Nachrichten am Kommando-Knopf (0 = keine Anzeige).</summary>
	public void SetCommandBadge(int unread)
	{
		_cmdBtn.Detail = unread > 0 ? $"Comms: {unread}" : "";
		_cmdBtn.DetailColor = UiKit.Orange;
	}

	public void SetSlider(float v) => _slider.SetValue(v, notify: false);
	public void SetAutopilot(bool on) => _autopilotBtn.On = on;
	public void SetAutoMine(bool on) => _autoMineBtn.On = on;

	public void Open() => Visible = true;
	public void Close() => Visible = false;
	public void Refresh() => Apply(_d);

	public override void _Ready()
	{
		AddChild(_arcs);

		// ── Status oben links: Hülle echt, Schild/Sprit/Leistung Platzhalter (ein Hinweis, nicht pro Zeile) ──
		var status = Win(this, 14, 12, 300, 168, new Color(0.03f, 0.05f, 0.1f, 0.68f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 10);
		var sv = At(status, new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, 10, 10, 280, 148);
		sv.AddThemeConstantOverride("separation", 7);
		_hullBar = BarRow(sv, "Hülle", UiKit.Green, out _hullVal!);
		BarRow(sv, "Schild", UiKit.Cyan, UiPlaceholders.ShieldPercent, placeholder: true);
		_fuelBar = BarRow(sv, "Sprit", UiKit.Orange, out _fuelVal!);
		var powRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		powRow.AddThemeConstantOverride("separation", 8);
		var pl = Text("Leistung", 16, UiKit.Dim); pl.CustomMinimumSize = new Vector2(66, 0);
		powRow.AddChild(pl);
		powRow.AddChild(Text(UiPlaceholders.PowerText, 16, new Color(UiKit.Dim, 0.7f)));
		sv.AddChild(powRow);
		sv.AddChild(Text("Schild · Leistung: " + UiPlaceholders.Tag, 12, new Color(UiKit.Dim, 0.6f)));
		_credits = At(this, Text("", 20, UiKit.Orange), 20, 188, 300, 26);

		// ── Sektorchip oben Mitte (echt: Sektorname und Besitzerfraktion) ──
		var chip = Win(this, 420, 12, 430, 42, new Color(0.03f, 0.05f, 0.1f, 0.68f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 20);
		var cr = At(chip, new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, 14, 0, 402, 42);
		cr.AddThemeConstantOverride("separation", 12);
		_sector = Text("", 18, UiKit.Cyan);
		_factionChip = Text("", 15, UiKit.Dim);
		cr.AddChild(_sector); cr.AddChild(_factionChip);
		// Phase 11: Lage des Sektors unter dem Chip, Fahndung (nur wenn gesucht) darunter
		_security = At(this, Text("", 13, UiKit.Dim, HorizontalAlignment.Center), 420, 56, 430, 18);
		_wanted = At(this, Text("", 15, UiKit.Red, HorizontalAlignment.Center), 420, 74, 430, 20);

		// ── Kommandozentrale, Karte und Zeitraffer oben rechts ──
		_mapBtn = At(this, new KitButton("Karte", new Vector2(110, 50)) { FontSize = 18 }, 1030, 12);
		_fastBtn = At(this, new KitButton("Zeit ×1", new Vector2(110, 50)) { FontSize = 17, Toggle = true }, 1150, 12);
		_cmdBtn = At(this, new KitButton("Kommando", new Vector2(160, 56)) { FontSize = 18 }, 862, 8);
		_mapBtn.Pressed += () => MapPressed?.Invoke();
		_fastBtn.Pressed += () => FastPressed?.Invoke();
		_cmdBtn.Pressed += () => CommandPressed?.Invoke();

		// ── Zielkarte rechts unter der oberen Leiste ──
		var card = Win(this, 930, 72, 340, 132, new Color(0.03f, 0.05f, 0.1f, 0.72f), UiKit.Green, 12);
		var tv = At(card, new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, 12, 8, 246, 116);
		tv.AddThemeConstantOverride("separation", 2);
		_tName = Text("", 18, UiKit.Green); _tInfo = Text("", 15, UiKit.Dim); _tArc = Text("", 15, UiKit.White);
		tv.AddChild(_tName); tv.AddChild(_tInfo); tv.AddChild(_tArc);
		_radar = At(card, new Radar(), 266, 8, 64, 64);

		_autopilotBtn = At(this, new KitButton("Autopilot", new Vector2(160, 42)) { FontSize = 16, Toggle = true }, 930, 212);
		_autoMineBtn = At(this, new KitButton("Auto-Mining", new Vector2(170, 42)) { FontSize = 16, Toggle = true }, 1100, 212);
		_autopilotBtn.Pressed += () => AutopilotPressed?.Invoke();
		_autoMineBtn.Pressed += () => AutoMinePressed?.Invoke();
		_contextBtn = At(this, new KitButton("", new Vector2(340, 52)) { FontSize = 20 }, 930, 262);
		_contextBtn.Pressed += () => ContextPressed?.Invoke();

		// ── Unten links: Wenden (Haltetasten) und Hamburger-Menü ──
		At(this, Text("WENDEN", 13, UiKit.Dim, HorizontalAlignment.Center), 100, 560, 260, 20);
		_turnL = At(this, new KitButton("◀", new Vector2(120, 100)) { FontSize = 44 }, 96, 584);
		_turnR = At(this, new KitButton("▶", new Vector2(120, 100)) { FontSize = 44 }, 230, 584);
		// RÜCK: 50 % der Kanten (80x56, Mindest-Trefferfläche), mittig zwischen Wendetasten (bis x=350) und Slider (ab x=660)
		At(this, Text("RÜCK · max 20 %", 13, UiKit.Dim, HorizontalAlignment.Center), 410, 566, 200, 20);
		_reverse = At(this, new KitButton("RÜCK", new Vector2(80, 56)) { FontSize = 20 }, 470, 590);
		_hamburger = At(this, new KitButton("", new Vector2(60, 60)), 16, 644);
		_hamburger.Symbol = g => { foreach (var yy in new[] { 20f, 30f, 40f }) g.DrawRect(new Rect2(16, yy, 28, 4), UiKit.White); };
		_hamburger.Pressed += () => HamburgerPressed?.Invoke();

		// ── Tempo-Slider unten rechts am Bildrand ──
		_slider = At(this, new KitSlider(), 660, 646, 600, 68);

		// ── Waffenschächte rechts am Rand, eine Taste je Schacht ──
		for (int i = 0; i < 4; i++)   // Reserve für spätere Schächte; sichtbare Anzahl kommt aus den Daten
		{
			int idx = i;
			var b = At(this, new KitButton("", new Vector2(176, 68)), 1090, 330 + i * 76);
			b.Symbol = g => PaintArcSymbol(g, idx);
			b.Down += () => BayDown?.Invoke(idx);
			_bayButtons.Add(b);
		}
		_allFire = At(this, new KitButton("Alle Feuer", new Vector2(176, 64)) { FontSize = 19 }, 1090, 490);
		_allFire.Pressed += () => AllFire?.Invoke();
		_ready = At(this, Text("", 13, UiKit.Green, HorizontalAlignment.Center), 1090, 560, 176, 20);
	}

	/// <summary>Fenster mit freier Platzierung der Kinder (ein PanelContainer würde alle Kinder strecken).</summary>
	static Control Win(Control parent, float x, float y, float w, float h, Color bg, Color? border, int radius)
	{
		var c = At(parent, new Control { MouseFilter = Control.MouseFilterEnum.Ignore }, x, y, w, h);
		var p = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
		p.AddThemeStyleboxOverride("panel", Box(bg, border, radius, 2, 0));
		c.AddChild(p);
		p.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		return c;
	}

	KitBar BarRow(Control parent, string name, Color fill, out Label? valueLabel, float value = 0f, bool placeholder = false)
	{
		var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		row.AddThemeConstantOverride("separation", 8);
		var l = Text(name, 16, placeholder ? new Color(UiKit.Dim, 0.75f) : UiKit.Dim);
		l.CustomMinimumSize = new Vector2(66, 0);
		row.AddChild(l);
		var bar = new KitBar(170, 12, fill);
		if (placeholder) bar.Modulate = new Color(1, 1, 1, 0.45f);
		bar.Set(value, fill);
		row.AddChild(bar);
		var v = Text(placeholder ? $"{(int)value} %" : "", 14, new Color(UiKit.Dim, 0.7f));
		row.AddChild(v);
		valueLabel = v;
		parent.AddChild(row);
		return bar;
	}

	KitBar BarRow(Control parent, string name, Color fill, float value = 0f, bool placeholder = false) =>
		BarRow(parent, name, fill, out _, value, placeholder);

	void PaintArcSymbol(KitButton g, int idx)
	{
		if (idx >= _d.Bays.Count) return;
		var bay = _d.Bays[idx];
		var c = new Vector2(30, g.Size.Y / 2f);
		g.DrawCircle(c, 22, new Color(0, 0, 0, 0.35f));
		Color edge = bay.State switch
		{
			BayState.Ready => UiKit.Green,
			BayState.OutsideArc => UiKit.Orange,
			BayState.Reloading => UiKit.Dim,
			_ => new Color(UiKit.Dim, 0.6f),
		};
		float center = FiringArc.FacingOffset(bay.Facing);
		float half = Math.Min(bay.ArcDegrees, 180f);
		if (bay.ArcDegrees >= 360f)
		{
			g.DrawArc(c, 20, 0, Mathf.Tau, 40, new Color(edge, 0.45f), 6f);
			g.DrawArc(c, 20, 0, Mathf.Tau, 40, edge, 1.5f);
		}
		else
		{
			var pts = new List<Vector2> { c };
			const int n = 20;
			for (int i = 0; i <= n; i++)
			{
				float a = Mathf.DegToRad(center - half + 2 * half * i / n - 90);
				pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 20f);
			}
			g.DrawColoredPolygon(pts.ToArray(), new Color(edge, 0.45f));
			pts.Add(c);
			g.DrawPolyline(pts.ToArray(), edge, 1.5f, true);
		}
		g.DrawCircle(c, 3.5f, UiKit.White);
	}

	/// <summary>Übernimmt die neuen Simulationsdaten und zeichnet um.</summary>
	public void Refresh(FlightHudData d)
	{
		_d = d;
		Apply(d);
	}

	void Apply(FlightHudData d)
	{
		_hullBar.Set(d.Hull, d.Hull > 60f ? UiKit.Green : UiKit.Orange);
		_hullVal.Text = $"{(int)d.Hull}";
		float fuelPct = d.FuelCapacity > 0f ? 100f * d.Fuel / d.FuelCapacity : 0f;
		_fuelBar.Set(fuelPct, fuelPct < 20f ? UiKit.Red : UiKit.Orange);   // Warnfarbe unter 20 %
		_fuelVal.Text = d.FuelCapacity > 0f ? $"{(int)d.Fuel}" : "–";
		_credits.Text = $"{(int)d.Credits} Credits";
		_sector.Text = d.SectorName;
		_factionChip.Text = d.FactionName;
		_security.Text = d.Security;
		_wanted.Text = d.Wanted;
		_wanted.Visible = d.Wanted.Length > 0;
		_wanted.AddThemeColorOverride("font_color", d.WantedSevere ? UiKit.Red : UiKit.Orange);
		_fastBtn.Text = d.FastOn ? "Zeit ×10" : "Zeit ×1";
		_fastBtn.On = d.FastOn;
		_autopilotBtn.On = d.AutopilotOn;
		_autoMineBtn.On = d.AutoMineOn;

		_tName.Text = d.TargetName;
		_tName.AddThemeColorOverride("font_color", d.TargetHasInfo ? UiKit.Green : UiKit.Dim);
		_tInfo.Text = d.TargetInfo;
		_tArc.Text = d.TargetArc;
		_radar.RelBearing = d.TargetRelBearing;
		_radar.HasTarget = d.TargetHasInfo && d.TargetDistance >= 0f;
		_radar.Distance = d.TargetDistance;

		_contextBtn.Visible = d.ContextLabel.Length > 0;
		_contextBtn.Text = d.ContextLabel;

		_slider.SetValue(d.SliderValue, notify: false);
		_slider.SpeedText = $"{(int)d.Speed} m/s";

		for (int i = 0; i < _bayButtons.Count; i++)
		{
			var b = _bayButtons[i];
			if (i >= d.Bays.Count) { b.Visible = false; continue; }
			var bay = d.Bays[i];
			b.Visible = true;
			b.Text = bay.Name;
			b.Sub = bay.Place;
			(b.State, b.Detail, b.DetailColor) = bay.State switch
			{
				BayState.Ready => (KitButtonState.Ready, bay.Detail, UiKit.Green),
				BayState.OutsideArc => (KitButtonState.Warn, bay.Detail, UiKit.Orange),
				BayState.Reloading => (KitButtonState.Busy, bay.Detail, UiKit.Dim),
				_ => (KitButtonState.Disabled, bay.Detail, new Color(UiKit.Dim, 0.7f)),
			};
			b.Progress = bay.Reload;
			b.QueueRedraw();
		}
		_ready.Text = d.FireBays > 0 ? $"{d.ReadyBays} von {d.FireBays} bereit" : "";
		// Phase 8: mit einem dritten Schacht (Spalter) rutschen „Alle Feuer“ und die Bereit-Zeile unter die Schächte
		float fireY = Math.Max(490f, 330f + Math.Min(d.Bays.Count, _bayButtons.Count) * 76f + 4f);
		_allFire.Position = new Vector2(1090, fireY);
		_ready.Position = new Vector2(1090, fireY + 66f);
		_ready.Size = new Vector2(176, 16);
		_arcs.Data = d;
		_arcs.QueueRedraw();
	}

	/// <summary>Zeichnet die Feuerwinkel als Kegel um das Schiff (Bildschirmposition des Schiffs, 290 px Radius).</summary>
	sealed partial class ArcOverlay : Control
	{
		public FlightHudData? Data;
		public ArcOverlay() { MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }
		public override void _Draw()
		{
			var d = Data;
			if (d == null || d.ShipScreen is not { } ship) return;
			const float r = 290f;
			for (int i = 0; i < d.Bays.Count; i++)
			{
				var bay = d.Bays[i];
				var (fill, edge) = bay.State switch
				{
					BayState.Ready => (new Color(UiKit.Green, 0.10f), new Color(UiKit.Green, 0.6f)),
					BayState.OutsideArc => (new Color(UiKit.Orange, 0.07f), new Color(UiKit.Orange, 0.45f)),
					BayState.Reloading => (new Color(UiKit.Dim, 0.05f), new Color(UiKit.Dim, 0.3f)),
					_ => (new Color(UiKit.Dim, 0.04f), new Color(UiKit.Dim, 0.25f)),
				};
				// Bildschirmwinkel: Nase ist direkt projiziert (Kamera kann gedreht sein); Backbord (+90°) liegt
				// auf dem Bildschirm 90° gegen den Uhrzeigersinn (y nach unten) von der Nase
				float center = d.ShipScreenNoseDeg - FiringArc.FacingOffset(bay.Facing);
				if (bay.ArcDegrees >= 360f)
				{
					DrawCircle(ship, r, fill);
					DrawArc(ship, r, 0, Mathf.Tau, 64, edge, 2f);
					continue;
				}
				float half = Math.Min(bay.ArcDegrees, 180f);
				var pts = new List<Vector2> { ship };
				const int n = 28;
				for (int k = 0; k <= n; k++)
				{
					float a = Mathf.DegToRad(center - half + 2 * half * k / n);
					pts.Add(ship + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
				}
				DrawColoredPolygon(pts.ToArray(), fill);
				pts.Add(ship);
				DrawPolyline(pts.ToArray(), edge, 1.5f, true);
			}
			if (d.TargetScreen is { } t && ship.DistanceTo(t) < r)
				DrawLine(ship + (t - ship).Normalized() * 40, t, new Color(d.TargetInArc ? UiKit.Green : UiKit.Orange, 0.8f), 2f, true);
		}
	}

	/// <summary>Mini-Radar in der Zielkarte: Schiff in der Mitte, Nase oben, Ziel als Punkt.</summary>
	sealed partial class Radar : Control
	{
		public float RelBearing, Distance;
		public bool HasTarget;
		public Radar() { MouseFilter = MouseFilterEnum.Ignore; }
		public override void _Process(double delta) => QueueRedraw();
		public override void _Draw()
		{
			var c = Size / 2f;
			DrawCircle(c, 30, new Color(0, 0.08f, 0.04f, 0.8f));
			DrawArc(c, 30, 0, Mathf.Tau, 48, UiKit.Green, 2);
			DrawArc(c, 15, 0, Mathf.Tau, 32, new Color(UiKit.Green, 0.4f), 1);
			DrawCircle(c, 3, UiKit.Cyan);
			if (HasTarget)
			{
				float a = Mathf.DegToRad(RelBearing - 90);
				float rr = Mathf.Clamp(Distance / 300f, 0.15f, 1f) * 24f;
				DrawCircle(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr, 3, UiKit.Green);
			}
		}
	}
}
