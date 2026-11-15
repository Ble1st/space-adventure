using System.Linq;
using System;
using System.Collections.Generic;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Ui;
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
	/// <summary>Runde 16: laufende Kosten je Minute (Flottenunterhalt + Liegegebühr; 0 = keine) — erklärt, warum die Kasse ohne Kauf sinkt.</summary>
	public float CostsPerMinute;
	/// <summary>Tankinhalt und Tankgröße des Spielerschiffs (echt, Phase 4c); Kapazität 0 = kein Treibstoffsystem.</summary>
	public float Fuel, FuelCapacity;
	/// <summary>Phase 14: Treibstoff eingeschaltet (Regel fuelActive)? Aus = Tankzeile ausgeblendet.</summary>
	public bool FuelActive = true;
	/// <summary>Phase 23: Schild (aktuell/Höchstwert; Höchstwert 0 = kein Schildmodul), Leistungsbilanz (Leistung, Dauerlast), Speicherbank und Leistungsverteilung — echt aus dem Core.</summary>
	public float Shield, ShieldMax, PowerOutput, PowerDraw, Charge, ChargeMax;
	public PowerMode PowerMode;
	public string SectorName = "", FactionName = "";
	/// <summary>Runde 33: Kürzel der Besitzerfraktion („[UNI]“) — passt der Name nicht in den Sektorchip, steht das Kürzel.</summary>
	public string FactionTag = "";
	/// <summary>Phase 11: Fahndungszeile des Spielers (leer = sauber, dann ausgeblendet) und Lage des Sektors (Sicherheit/Bedrohung).</summary>
	public string Wanted = "", Security = "";
	public bool WantedSevere;
	/// <summary>Phase 26: Brandwarnung (leer = kein Brand) — rot unter der Fahndungszeile.</summary>
	public string Fire = "";
	// Zielkarte
	public string TargetName = "Kein Ziel";
	public string TargetInfo = "Station, Tor, Fels antippen";   // Runde 20: passt in die Zielkarte (vorher „… Asteroid antipp…“)
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
	/// <summary>Runde 34b (Prüfung B6): Spieler im Gefecht — die Zahl der Schachtzellen (und damit der Faktor der Schicht) bleibt dann stehen, auch wenn ein
	/// Waffenmodul ausfällt und sein Schacht verschwindet (Gunnery.WeaponAt liefert für ausgefallene Module null).</summary>
	public bool InCombat;
	public int ReadyBays, FireBays;
	// Feuerwinkel-Kegel um das Schiff
	public Vector2? ShipScreen;
	/// <summary>Bildschirmwinkel der Schiffsnase in Grad (atan2, y nach unten) – hängt von der Kameradrehung ab, Basis der Kegel.</summary>
	public float ShipScreenNoseDeg;
	public Vector2? TargetScreen;
	public bool TargetInArc;
	/// <summary>Phase 25: Kampf — Schild/Hülle des gewählten Schiffs („“ = kein Schiffsziel), feindlich, Dauerfeuer, Beschusswarnung mit Richtung, hat Kampfwaffen.</summary>
	public string TargetCombat = "";
	public bool TargetHostile, AutoFireOn, HasWeapons;
	/// <summary>Runde 15: Überfall läuft (oder angekündigt) und Dauerfeuer ist aus — die Taste pulsiert.</summary>
	public bool AutoFireHint;
	/// <summary>Gerätetest: ein Gegner (Gefecht oder Gesetzloser) im Sektor — „Nächster Gegner“ bleibt dann auch ohne Waffe erreichbar.</summary>
	public bool EnemyNear;
	/// <summary>Phase 27: Der Spieler kann sich ergeben (Polizei: Strafe zahlen; Kaperer: Fracht abwerfen) — Taste „Ergeben“ sichtbar.</summary>
	public bool CanSurrender;
	/// <summary>Gerätetest Phase 28: Ergeben ist ein Thema (Gefecht/Warnung) — Taste sichtbar; ohne <see cref="CanSurrender"/> deaktiviert mit diesem Grund.</summary>
	public bool SurrenderRelevant;
	public string SurrenderReason = "";
	/// <summary>Phase 28: Die gewählte Station kann angegriffen werden (Taste „Station angreifen“, zwei Tipps); scharf = die Waffen schießen auf sie.</summary>
	public bool CanAttackStation, StationAttackArmed;
	/// <summary>Phase 31: Das gewählte Tor kann angegriffen werden (Taste „Tor angreifen“, zwei Tipps, Warnung zu den Folgen).</summary>
	public bool CanAttackGate;
	public string GateAttackInfo = "";
	/// <summary>Runde 14b: Tor gewählt, aber gerade nicht angreifbar (Schonfrist nach dem Wiederanlauf) — Taste gesperrt mit diesem Grund.</summary>
	public string GateAttackBlocked = "";
	public string HitWarning = "";
	/// <summary>Gerätetest Phase 28: Gegenwehr der Station (Geschütze, Reichweite, eigene Raketenreichweite) unter „Station angreifen“.</summary>
	public string StationAttackInfo = "";
	/// <summary>Gerätetest Phase 28 (Runde 5): Kräfteverhältnis eines Stationsangriffs („Aussichtslos allein – Fregatte oder Flotte nötig“), aussichtslos = rot.</summary>
	public string StationOdds = "";
	public bool StationHopeless;
	/// <summary>Runde 5: Fluchtlage im Gefecht („Flucht: Polizei in Reichweite (52 von 60 m)“), dieselbe Regel wie die Simulation; leer ohne Gefecht.</summary>
	public string EscapeStatus = "";
	/// <summary>Gerätetest Phase 28: Zielhilfe „Ziel folgen“ — sichtbar mit feindlichem Ziel (Schiff im Gefecht oder scharfe Station), an/aus.</summary>
	public bool AimAssistVisible, AimAssistOn;
}

/// <summary>
/// Flug-HUD (UI.md Abschnitt 2): Statusleiste oben (Hülle, Schild, Sprit, Leistung und Credits echt; Phase 23), darunter die drei Tasten der
/// Leistungsverteilung (Waffen, Schild, Antrieb; nochmals tippen = ausgeglichen),
/// Sektorchip Mitte, Karte, Zeitraffer und „Kamera ↺“ (Standardansicht) oben rechts, Zielkarte mit Autopilot/Auto-Mining
/// und Kontexttaste, unten links Hamburger-Menü und Wendetasten (RÜCK als eigene kleinere Haltetaste dazwischen),
/// unten rechts der Tempo-Slider, rechts die Waffenschächte.
/// </summary>
public partial class FlightHud : Control, IScreen
{
	public string Id => "flight";
	/// <summary>Selbsttest: angezeigte Fahndungs- und Lagezeile (Phase 11).</summary>
	public string TestWanted => _wanted.Visible ? _wanted.Text : "";
	/// <summary>Phase 26: sichtbare Brandwarnung (leer = keine) und Kassenanzeige (zeigt „Schulden“ bei negativer Kasse).</summary>
	public string TestFire => _fire.Visible ? _fire.Text : "";
	public string TestCredits => _credits.Text;
	public string TestSecurity => _security.Text;
	/// <summary>Selbsttest (Gerätetest Phase 27): angezeigter Sektorname im Chip.</summary>
	public string TestSectorChip => _sector.Text;
	public string Title => "Flug";
	public Control Node => this;
	public bool IsOpen => Visible;

	public event Action? HamburgerPressed, MapPressed, FastPressed, AutopilotPressed, AutoMinePressed, ContextPressed, AllFire, CommandPressed;
	/// <summary>Finger auf einer Waffenschacht-Taste (Index in <see cref="FlightHudData.Bays"/>).</summary>
	public event Action<int>? BayDown;
	/// <summary>Runde 33b: Sammeltaste „+n weitere“ (Schächte ab Index) gedrückt.</summary>
	public event Action<int>? MoreBaysDown;
	int _bayMore;
	/// <summary>Selbsttest/Prüfung: Text der sechsten Schachttaste (Sammeltaste) und ob gerade zusammengefasst wird.</summary>
	public int TestLayoutBays => _layoutBays;
	public string TestBay6Text => _bayButtons.Count >= MaxBays ? _bayButtons[MaxBays - 1].Text : "";
	/// <summary>
	/// Runde 33b: Bildschirmlage des eigenen Schiffs (Bildpunkte, null = hinter der Kamera) — Meldung/Hinweis meiden sie, das Prüfwerkzeug prüft, dass keine
	/// HUD-Taste sie verdeckt.
	/// </summary>
	public Vector2? ShipScreen => _d.ShipScreen;
	/// <summary>Runde 33b: Fläche um das eigene Schiff (Ring) in Einheiten der Schicht.</summary>
	public Rect2? ShipRectLocal(float radiusPx = 70f)
	{
		if (_d.ShipScreen is not { } p) return null;
		float k = Scale.X > 0f ? 1f / Scale.X : 1f;
		return new Rect2((p - new Vector2(radiusPx, radiusPx)) * k, new Vector2(2f * radiusPx, 2f * radiusPx) * k);
	}
	/// <summary>Phase 23: Taste der Leistungsverteilung gedrückt — gewünschte Stellung (erneutes Tippen auf die aktive Stellung = ausgeglichen).</summary>
	public event Action<PowerMode>? PowerModePressed;
	/// <summary>Phase 25: Dauerfeuer umschalten, „Nächster Gegner“ wählen.</summary>
	public event Action? AutoFirePressed, NextEnemyPressed, SurrenderPressed, StationAttackPressed, AimAssistPressed, GateAttackPressed;
	/// <summary>Phase 28: „Station angreifen“ (zwei Tipps über <see cref="KitConfirm"/>).</summary>
	KitButton _stationAttack = null!, _gateAttack = null!;
	public bool TestGateAttackVisible => _gateAttack.Visible;
	public bool TestGateAttackDisabled => _gateAttack.State == KitButtonState.Disabled;
	public string TestGateAttackSub => _gateAttack.Sub;
	public void TestPressGateAttack() => GateAttackPressed?.Invoke();
	readonly KitConfirm _attackConfirm = new();
	public bool TestStationAttackVisible => _stationAttack.Visible;
	/// <summary>Runde 23: Rechtecke von „Station angreifen“, Kontexttaste und Autopilot (Abstand gegen Fehltipps).</summary>
	public Rect2 TestStationAttackRect => _stationAttack.GetGlobalRect();
	public Rect2 TestContextRect => _contextBtn.GetGlobalRect();
	public Rect2 TestAutopilotRect => _autopilotBtn.GetGlobalRect();
	public void TestPressStationAttack() => StationAttackPressed?.Invoke();
	KitButton _autoFire = null!, _nextEnemy = null!, _surrender = null!, _aimAssist = null!;
	bool _autoFireHint;
	/// <summary>Runde 15: pulsiert „Dauerfeuer“ gerade (Selbsttest)?</summary>
	public bool TestAutoFireHint => _autoFireHint;
	public bool TestAimAssistVisible => _aimAssist.Visible;
	public string TestTargetArc => _tArc.Text;
	/// <summary>Runde 21: Zeile „Entfernung …“ der Zielkarte.</summary>
	public string TestTargetInfo => _tInfo.Text;
	public void TestPressAimAssist() => AimAssistPressed?.Invoke();
	public string TestStationAttackInfo { get; private set; } = "";
	public bool TestAimAssistOn => _aimAssist.On;
	public Rect2 TestAimAssistRect => new(_aimAssist.Position, _aimAssist.Size);
	/// <summary>Phase 27: Selbsttest — Taste „Ergeben“.</summary>
	public bool TestSurrenderVisible => _surrender.Visible;
	public bool TestSurrenderEnabled => _surrender.Visible && _surrender.State != KitButtonState.Disabled;
	public Rect2 TestSurrenderRect => new(_surrender.Position, _surrender.Size);
	public void TestPressSurrender() => SurrenderPressed?.Invoke();
	Label _tCombat = null!, _hitWarning = null!, _tOdds = null!, _escape = null!;
	/// <summary>Selbsttest Runde 5: Kräftezeile der Zielkarte, Fluchtzeile.</summary>
	public string TestStationOdds => _tOdds.Visible ? _tOdds.Text : "";
	public string TestEscapeStatus => _escape.Visible ? _escape.Text : "";
	public string TestStationAttackSub => _stationAttack.Sub;
	public string TestTargetName => _tName.Text;
	/// <summary>Runde 26: rechter Rand der Namenszeile und linker Rand des Radars (Kartenkoordinaten).</summary>
	public (float NameRight, float RadarLeft) TestTargetNameEdge => (_tName.GetParent<Control>().Position.X + _tName.Position.X + _tName.Size.X, _radar.Position.X);
	/// <summary>Runde 20: Text der Kontexttaste („“ = verborgen).</summary>
	public string TestContextLabel => _contextBtn.Visible ? _contextBtn.Text : "";

	/// <summary>
	/// Runde 7: der Hinweis (Toast) ist mehrzeilig gewachsen — Beschuss- und Fluchtzeile rücken unter ihn (vorher überdeckte „BESCHUSS (Station)“ den Hinweis
	/// „Verstärkung (Verband) …“). <paramref name="bottom"/> = Unterkante des sichtbaren Hinweises im Flug, 0 = keiner.
	/// </summary>
	public Rect2 TestHitWarningRect => new(_hitWarning.Position, _hitWarning.Size);
	public void AvoidToast(float bottom) { }   // Runde 12: die Zeilen stehen unten, der Hinweis oben — kein Ausweichen mehr nötig
	public Rect2 TestEscapeRect => new(_escape.Position, _escape.Size);
	const float HitWarningY = 534f;

	/// <summary>Selbsttest Runde 7: Lage aller Kampftasten und -zeilen, als wären sie sichtbar (Name → Rechteck); Sichtbarkeit bleibt unverändert.</summary>
	public List<(string Name, Rect2 Rect)> TestCombatLayout(int bays)
	{
		var list = new List<(string, Rect2)>();
		for (int i = 0; i < bays && i < _bayButtons.Count; i++) list.Add(($"Schacht {i + 1}", new Rect2(_bayButtons[i].Position, _bayButtons[i].Size)));
		foreach (var (n, c) in new (string, Control)[] { ("Alle Feuer", _allFire), ("Dauerfeuer", _autoFire), ("Nächster Gegner", _nextEnemy), ("Station angreifen", _stationAttack),
			("Ziel folgen", _aimAssist), ("Ergeben", _surrender), ("Beschusswarnung", _hitWarning), ("Fluchtzeile", _escape), ("Zielkarte", _tName.GetParent().GetParent<Control>()) })
			list.Add((n, new Rect2(c.Position, c.Size)));
		return list;
	}
	/// <summary>Selbsttest: Zielkartenzeile Schild/Hülle, Beschusswarnung, Lage und Größe der Kampftasten.</summary>
	public string TestTargetCombat => _tCombat.Text;
	/// <summary>Runde 15: Pflichtzeilen der Zielkarte vollständig sichtbar (kein Abschneiden)?</summary>
	public bool TestTargetLinesComplete => TestTargetLineOverflow.Length == 0;
	/// <summary>Runde 16: erste Zielkartenzeile, die nicht in ihre Breite passt („…“), sonst leer; auch ob die Kräftezeile unter den Kartenrand ragt.</summary>
	public string TestTargetLineOverflow
	{
		get
		{
			foreach (var l in new[] { _tInfo, _tArc, _tCombat, _tOdds })
			{
				if (!l.Visible || l.Text.Length == 0) continue;
				var font = l.GetThemeFont("font"); int size = l.GetThemeFontSize("font_size");
				float w = font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, size).X;
				float room = l == _tOdds ? 316f : 246f;
				if (w > room + 0.5f) return $"„{l.Text}“ {w:0} > {room:0} px";
			}
			if (_tOdds.Visible && _tOdds.GetGlobalRect().End.Y > _card.GetGlobalRect().End.Y + 1f) return $"Kräftezeile unter dem Kartenrand ({_tOdds.GetGlobalRect().End.Y:0} > {_card.GetGlobalRect().End.Y:0})";
			return "";
		}
	}
	public string TestHitWarning => _hitWarning.Visible ? _hitWarning.Text : "";
	Control? _reverseLabel;
	/// <summary>Runde 19 (Handy-Test B_4: der Warnkasten verdeckte „RÜCK · max 20 %“): Bildschirmfläche der Beschriftung über der Rücktaste.</summary>
	public Rect2 ReverseLabelRect => _reverseLabel is { } l ? l.GetGlobalRect() : new Rect2();
	/// <summary>Runde 33: dieselbe Fläche in Einheiten der Schicht.</summary>
	public Rect2 ReverseLabelLocal => _reverseLabel is { } l ? new Rect2(l.Position, l.Size) : new Rect2();
	public Rect2 TestBayRect(int i) => new(_bayButtons[i].Position, _bayButtons[i].Size);
	/// <summary>Runde 18: erste sichtbare Schachtzeile, die gekürzt würde (leer = alles passt).</summary>
	public string TestBayOverflow => _bayButtons.Where(b => b.Visible).Select(b => b.Overflow()).FirstOrDefault(o => o.Length > 0) ?? "";
	/// <summary>Runde 18: passt <paramref name="text"/> als Unterzeile/Detail in eine Schachttaste? (Probe über alle Bay-Zustände im Selbsttest)</summary>
	public bool TestBayLineFits(string sub, string detail, string name = "Bohrer")
	{
		if (_bayButtons.Count == 0) return true;
		var b = _bayButtons[0];
		var (t, s, dd) = (b.Text, b.Sub, b.Detail);
		b.Text = name; b.Sub = sub; b.Detail = detail;
		bool ok = b.Overflow().Length == 0;
		b.Text = t; b.Sub = s; b.Detail = dd;
		return ok;
	}
	public Rect2 TestAutoFireRect => new(_autoFire.Position, _autoFire.Size);
	public Rect2 TestNextEnemyRect => new(_nextEnemy.Position, _nextEnemy.Size);
	public bool TestNextEnemyVisible => _nextEnemy.Visible;
	public bool TestNextEnemyEnabled => _nextEnemy.Visible && _nextEnemy.State != KitButtonState.Disabled;
	public void TestPressNextEnemy() => NextEnemyPressed?.Invoke();
	public void TestPressAutoFire() => AutoFirePressed?.Invoke();

	// oben
	KitBar _hullBar = null!, _fuelBar = null!, _shieldBar = null!, _powerBar = null!;
	Label _fuelVal = null!, _shieldVal = null!, _powerVal = null!;
	// Phase 23: Leistungsverteilung
	readonly Dictionary<PowerMode, KitButton> _modeButtons = new();
	/// <summary>Selbsttest: angezeigte Schild- und Leistungswerte, aktive Stellung.</summary>
	public string TestShieldText => _shieldVal.Text;
	public string TestPowerText => _powerVal.Text;

	/// <summary>
	/// Gerätetest Phase 27: Bildschirmrechtecke der HUD-Elemente (Statusleiste, Tasten, Regler, Zielkarte …) — 3D-Beschriftungen (Stationsnamen), die
	/// dahinter liegen, blendet Main aus (sie liefen hinter den Drehknöpfen durch). Nur sichtbare direkte Kinder, keine flächigen Ebenen.
	/// </summary>
	public List<Rect2> OccupiedRects()
	{
		var list = new List<Rect2>();
		if (!Visible) return list;
		var screen = GetViewportRect().Size;
		foreach (var c in GetChildren())
		{
			if (c is not Control ctl || !ctl.Visible || ctl.Size.X <= 4f || ctl.Size.Y <= 4f) continue;
			var g = ctl.GetGlobalRect();   // Runde 33: enthält die Skalierung der Schicht → Bildpunkte
			if (g.Size.X * g.Size.Y < 0.3f * screen.X * screen.Y) list.Add(g.Grow(6f));
		}
		return list;
	}

	/// <summary>Runde 33: <see cref="OccupiedRects"/> in Einheiten der Schicht (für Meldung/Hinweis, die im Flug mit derselben Skalierung liegen).</summary>
	public List<Rect2> OccupiedLocal(bool interactiveOnly = false, bool importantOnly = false)
	{
		var list = interactiveOnly
			? GetChildren().OfType<Control>().Where(c => c.Visible && c is KitButton or KitSlider && c.Size.X > 4f).Select(c => c.GetGlobalRect().Grow(6f)).ToList()
			: importantOnly
				// Runde 35b: alles außer Sektorchip und Lagezeile — die darf eine Meldung im Notfall kurz überdecken (Status, Beschuss-/Fluchtzeile, Zielkarte nie)
				? GetChildren().OfType<Control>().Where(c => c.Visible && c != _chip && c != _security && c.Size.X > 4f && c.Size.Y > 4f
					&& c.Size.X * c.Size.Y < 0.3f * GetViewportRect().Size.X * GetViewportRect().Size.Y / MathF.Max(1f, Scale.X * Scale.X)).Select(c => c.GetGlobalRect().Grow(6f)).ToList()
				: OccupiedRects();
		float k = Scale.X > 0f ? 1f / Scale.X : 1f;
		for (int i = 0; i < list.Count; i++) list[i] = new Rect2(list[i].Position * k, list[i].Size * k);
		return list;
	}
	public PowerMode TestMode => _d.PowerMode;
	/// <summary>Selbsttest: Taste der Leistungsverteilung auslösen wie ein Fingertipp.</summary>
	public void TestPressMode(PowerMode m) => PressMode(m);
	/// <summary>Selbsttest: Größe und Lage einer Verteilungstaste (Mindest-Trefferfläche, keine Überdeckung).</summary>
	/// <summary>Runde 17: linke Spalte unter den Leistungstasten (Platz für Hinweise im Gefecht) — Bildschirmkoordinaten.</summary>
	/// <summary>Runde 33: in Einheiten der Schicht (wie Meldung/Hinweis im Flug).</summary>
	public float LeftColumnX() => _modeButtons.TryGetValue(PowerMode.Weapons, out var b) ? b.Position.X : 14f;
	public float LeftColumnTop() => _modeButtons.TryGetValue(PowerMode.Weapons, out var b) ? b.Position.Y + b.Size.Y + 40f : 300f;
	public Rect2 TestModeRect(PowerMode m) => new(_modeButtons[m].Position, _modeButtons[m].Size);
	Control _fuelRow = null!;
	/// <summary>Selbsttest: ist die Tankzeile sichtbar (Phase 14: nur mit eingeschaltetem Treibstoff)?</summary>
	public bool TestFuelVisible => _fuelRow.Visible;
	Label _hullVal = null!;
	Label _powerInfo = null!;
	Label _credits = null!, _sector = null!, _factionChip = null!, _wanted = null!, _security = null!, _fire = null!;
	KitButton _mapBtn = null!, _fastBtn = null!, _cmdBtn = null!;
	// Zielkarte
	Label _tName = null!, _tInfo = null!, _tArc = null!;

	string FitTargetName(string full)
	{
		var font = _tName.GetThemeFont("font");
		float room = _tName.CustomMinimumSize.X;
		int floor = SpaceSim.Core.Ui.UiLayout.FontFloor(17, UiKit.LayerFactor(_tName));
		string text = full;
		int fs = SpaceSim.Core.Ui.UiLayout.FitFontSize(s => font.GetStringSize(text, HorizontalAlignment.Left, -1, s).X, room, 17, floor);
		if (font.GetStringSize(text, HorizontalAlignment.Left, -1, fs).X > room + 0.5f)
		{
			// „Korsar #22 ([RSC] Rostige Schakale)“ → „Korsar #22 [RSC]“
			var m = System.Text.RegularExpressions.Regex.Match(full, @"^(.*?)\s*\((\[[A-Z]{2,4}\])[^)]*\)\s*$");
			if (m.Success) text = $"{m.Groups[1].Value} {m.Groups[2].Value}";
			fs = SpaceSim.Core.Ui.UiLayout.FitFontSize(s => font.GetStringSize(text, HorizontalAlignment.Left, -1, s).X, room, 17, floor);
		}
		_tName.AddThemeFontSizeOverride("font_size", fs);
		return text;
	}
	Control _card = null!;
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
	/// <summary>Phase 23: Farbe des Leistungsbalkens.</summary>
	static readonly Color PowerColor = new(1f, 0.86f, 0.4f);

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

		// ── Status oben links: Hülle, Schild, Sprit und Leistung echt (Phase 23) ──
		// Gerätetest Phase 27: Credits und Leistungsstellung stehen im Rahmen der Statusleiste (vorher frei über der Szene: orange auf orangem Planeten unlesbar)
		var status = _status = Win(this, 14, 12, 300, 200, new Color(0.03f, 0.05f, 0.1f, 0.78f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 10);
		var sv = At(status, new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, 10, 8, 280, 184);
		sv.AddThemeConstantOverride("separation", 5);
		_hullBar = BarRow(sv, "Hülle", UiKit.Green, out _hullVal!);
		_shieldBar = BarRow(sv, "Schild", UiKit.Cyan, out _shieldVal!);
		_fuelBar = BarRow(sv, "Sprit", UiKit.Orange, out _fuelVal!);
		_fuelRow = (Control)sv.GetChild(sv.GetChildCount() - 1);
		// Leistung: Balken = Dauerlast ÷ Reaktorleistung, Wert „Last/Leistung“ (+ Bank, wenn vorhanden)
		_powerBar = BarRow(sv, "Leistung", PowerColor, out _powerVal!);
		_powerInfo = Text("", 13, new Color(UiKit.Dim, 0.85f));
		_powerInfo.ClipText = true; _powerInfo.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; _powerInfo.CustomMinimumSize = new Vector2(1, 18);
		sv.AddChild(_powerInfo);
		_credits = Text("", 20, UiKit.Orange);
		_credits.ClipText = true; _credits.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; _credits.CustomMinimumSize = new Vector2(1, 26);
		sv.AddChild(_credits);

		// ── Phase 23: Leistungsverteilung unter den Credits (drei Tasten 96×56, zwischen Statusleiste und Wendetasten nichts überdeckt) ──
		var modes = new[] { (PowerMode.Weapons, "Waffen"), (PowerMode.Shield, "Schild"), (PowerMode.Engine, "Antrieb") };
		for (int i = 0; i < modes.Length; i++)
		{
			var (mode, label) = modes[i];
			var b = At(this, new KitButton(label, new Vector2(96, 56)) { FontSize = 16, Toggle = true }, 14 + i * 102, 222);
			b.Pressed += () => PressMode(mode);
			_modeButtons[mode] = b;
		}

		// ── Sektorchip oben Mitte (echt: Sektorname und Besitzerfraktion) ──
		var chip = _chip = Win(this, 420, 12, 430, 42, new Color(0.03f, 0.05f, 0.1f, 0.68f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 20);
		var cr = _chipRow = At(chip, new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, 14, 0, 402, 42);
		cr.AddThemeConstantOverride("separation", 12);
		_sector = Text("", 18, UiKit.Cyan);
		_factionChip = Text("", 15, UiKit.Dim);
		cr.AddChild(_sector); cr.AddChild(_factionChip);
		// Runde 33: im schmalen Chip schrumpft der Fraktionsname (mit „…“), statt den Chip über die Kommandotaste zu schieben
		_factionChip.ClipText = true; _factionChip.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_factionChip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		// Phase 11: Lage des Sektors unter dem Chip, Fahndung (nur wenn gesucht) darunter
		_security = At(this, Text("", 13, UiKit.Dim, HorizontalAlignment.Center), 420, 56, 430, 18);
		_wanted = At(this, Text("", 15, UiKit.Red, HorizontalAlignment.Center), 420, 74, 430, 20);
		// Phase 26: Brandwarnung (rot, nur bei Brand)
		_fire = At(this, Text("", 15, UiKit.Red, HorizontalAlignment.Center), 300, 94, 670, 20);
		// Runde 33: im Kompakt-HUD sind die Zeilen so breit wie der Chip — lange Texte enden mit „…“ statt über den Rand zu laufen
		foreach (var l in new[] { _security, _wanted, _fire }) { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }

		// ── Kommandozentrale, Karte und Zeitraffer oben rechts ──
		_mapBtn = At(this, new KitButton("Karte", new Vector2(110, 50)) { FontSize = 18 }, 1030, 12);
		_fastBtn = At(this, new KitButton("Zeit ×1", new Vector2(110, 50)) { FontSize = 17, Toggle = true }, 1150, 12);
		_cmdBtn = At(this, new KitButton("Kommando", new Vector2(160, 56)) { FontSize = 18 }, 862, 8);
		_mapBtn.Pressed += () => MapPressed?.Invoke();
		_fastBtn.Pressed += () => FastPressed?.Invoke();
		_cmdBtn.Pressed += () => CommandPressed?.Invoke();

		// ── Zielkarte rechts unter der oberen Leiste ──
		var card = Win(this, 930, 72, 340, 132, new Color(0.03f, 0.05f, 0.1f, 0.72f), UiKit.Green, 12);
		_card = card;
		var tv = At(card, new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, 12, 8, 246, 116);
		tv.AddThemeConstantOverride("separation", 2);
		_tName = Text("", 18, UiKit.Green); _tInfo = Text("", 15, UiKit.Dim); _tArc = Text("", 15, UiKit.White);
		_tCombat = Text("", 15, UiKit.Cyan);
		_tOdds = Text("", 14, UiKit.Orange);   // Runde 5: Kräfteverhältnis eines Stationsangriffs
		// Gerätetest: Zeilen der Zielkarte enden vor dem Radarkreis (der Hinweistext lief hinein) — gekürzt mit „…“
		// Runde 26 (Gerätetest b14_1: langer Stationsname lief unter den Radarring): die Zeilen füllen nicht die Kartenbreite (die Kräftezeile ist breiter),
		// sondern bleiben 246 px breit
		foreach (var l in new[] { _tName, _tInfo, _tArc, _tCombat }) { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; l.CustomMinimumSize = new Vector2(246, 0); l.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; }
		// Runde 16 (Handy-Test: Zeilen abgeschnitten, Kräftezeile über dem Kartenrand): kurze Texte (Core-Kurzformen) und etwas kleinere Schrift — fünf Zeilen passen
		// in die 132 px hohe Karte (Umbruch half am Gerät nicht: die Zeilen bekamen keine zweite Höhe)
		_tName.AddThemeFontSizeOverride("font_size", 17);
		foreach (var l in new[] { _tInfo, _tArc, _tCombat }) l.AddThemeFontSizeOverride("font_size", 14);
		_tOdds.AddThemeFontSizeOverride("font_size", 13);
		_tOdds.ClipText = true; _tOdds.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; _tOdds.CustomMinimumSize = new Vector2(316, 0);   // unter dem Radar: ganze Kartenbreite
		tv.AddChild(_tName); tv.AddChild(_tInfo); tv.AddChild(_tArc); tv.AddChild(_tCombat); tv.AddChild(_tOdds);
		_tOdds.Visible = false;
		_radar = At(card, new Radar(), 266, 8, 64, 64);

		_autopilotBtn = At(this, new KitButton("Autopilot", new Vector2(160, 42)) { FontSize = 16, Toggle = true }, 930, 212);
		_autoMineBtn = At(this, new KitButton("Auto-Mining", new Vector2(170, 42)) { FontSize = 16, Toggle = true }, 1100, 212);
		_autopilotBtn.Pressed += () => AutopilotPressed?.Invoke();
		_autoMineBtn.Pressed += () => AutoMinePressed?.Invoke();
		_contextBtn = At(this, new KitButton("", new Vector2(340, 52)) { FontSize = 20 }, 930, 262);
		_contextBtn.Pressed += () => ContextPressed?.Invoke();

		// ── Unten links: Wenden (Haltetasten) und Hamburger-Menü ──
		_turnLabel = At(this, Text("WENDEN", 13, UiKit.Dim, HorizontalAlignment.Center), 100, 560, 260, 20);
		_turnL = At(this, new KitButton("◀", new Vector2(120, 100)) { FontSize = 44 }, 96, 584);
		_turnR = At(this, new KitButton("▶", new Vector2(120, 100)) { FontSize = 44 }, 230, 584);
		// RÜCK: 50 % der Kanten (80x56, Mindest-Trefferfläche), mittig zwischen Wendetasten (bis x=350) und Slider (ab x=660)
		_reverseLabel = At(this, Text("RÜCK · max 20 %", 13, UiKit.Dim, HorizontalAlignment.Center), 410, 566, 200, 20);
		_reverse = At(this, new KitButton("RÜCK", new Vector2(80, 56)) { FontSize = 20 }, 470, 590);
		_hamburger = At(this, new KitButton("", new Vector2(60, 60)), 16, 644);
		_hamburger.Symbol = g => { foreach (var yy in new[] { 20f, 30f, 40f }) g.DrawRect(new Rect2(16, yy, 28, 4), UiKit.White); };
		_hamburger.Pressed += () => HamburgerPressed?.Invoke();

		// ── Tempo-Slider unten rechts am Bildrand ──
		_slider = At(this, new KitSlider { Snap = SpaceSim.Core.Flight.Throttle.Snap }, 660, 646, 600, 68);   // Gerätetest: Totzone 5 %/95 %

		// ── Waffenschächte rechts am Rand, eine Taste je Schacht (Phase 25: bis zu 6, ab dem fünften in einer zweiten Spalte links daneben) ──
		for (int i = 0; i < MaxBays; i++)
		{
			int idx = i;
			var b = At(this, new KitButton("", new Vector2(176, BayH)), 1090, 330 + i * BayStep);
			b.Symbol = g => PaintArcSymbol(g, idx);
			b.Down += () => { if (_bayMore > 0 && idx == MaxBays - 1) MoreBaysDown?.Invoke(idx); else BayDown?.Invoke(idx); };
			_bayButtons.Add(b);
		}
		_allFire = At(this, new KitButton("Alle Feuer", new Vector2(176, 56)) { FontSize = 19 }, 1090, 490);
		_allFire.Pressed += () => AllFire?.Invoke();
		_ready = At(this, Text("", 13, UiKit.Green, HorizontalAlignment.Center), 1090, 314, 176, 16);
		// Phase 25: Dauerfeuer (Schalter) neben „Alle Feuer“, „Nächster Gegner“ links der Zielkarte, Beschusswarnung unter dem Sektorchip
		_autoFire = At(this, new KitButton("Dauerfeuer", new Vector2(176, 56)) { FontSize = 17, Toggle = true }, 906, 490);
		_autoFire.Pressed += () => AutoFirePressed?.Invoke();
		_nextEnemy = At(this, new KitButton("Nächster Gegner", new Vector2(160, 56)) { FontSize = 15 }, 764, 206);
		_nextEnemy.Pressed += () => NextEnemyPressed?.Invoke();
		// Phase 28: Angriff auf die gewählte Station — unter „Nächster Gegner“, nur wenn erlaubt; zwei Tipps (folgenschwer: Fahndung, Gegenwehr)
		// Runde 23 (Handy-Test 15: lag 6 px neben „Andocken“/„Autopilot“): eine Zeile tiefer und schmaler — frei von der Kontexttaste
		_stationAttack = At(this, _attackConfirm.Wire(new KitButton("", new Vector2(136, 56)) { FontSize = 15 }, "station_attack", "Station angreifen",
			() => StationAttackPressed?.Invoke()), 764, 330);
		_stationAttack.Visible = false;
		// Phase 31: Angriff auf das gewählte Tor — gleicher Platz, zwei Tipps (schweres Verbrechen bei beiden Endfraktionen, das Tor fällt aus)
		_gateAttack = At(this, _attackConfirm.Wire(new KitButton("", new Vector2(136, 56)) { FontSize = 15 }, "gate_attack", "Tor angreifen",
			() => GateAttackPressed?.Invoke()), 764, 330);
		_gateAttack.Visible = false;
		// Gerätetest Phase 28: Zielhilfe „Ziel folgen“ — unter „Station angreifen“ (Umschalter; die Nase folgt dem gewählten Feind mit der Drehrate des Schiffs)
		_aimAssist = At(this, new KitButton("Ziel folgen", new Vector2(136, 56)) { FontSize = 15, Toggle = true }, 764, 392);
		_aimAssist.Pressed += () => AimAssistPressed?.Invoke();
		_aimAssist.Visible = false;
		// Phase 27: „Ergeben“ — rechts über „Dauerfeuer“, nur wenn die Polizei oder Kaperer eine Aufgabe annehmen; Bestätigung mit zweitem Tippen (KitConfirm)
		_surrender = At(this, new KitButton("Ergeben", new Vector2(176, 56)) { FontSize = 16, State = KitButtonState.Warn }, 906, 398);
		_surrender.Pressed += () => SurrenderPressed?.Invoke();
		_surrender.Visible = false;
		// Runde 12 (Handy-Test): Beschuss- und Fluchtzeile unten rechts der Rückwärtstaste, unter der Meldungsfläche — die Bildmitte vor dem Bug bleibt frei
		_hitWarning = At(this, Text("", 17, UiKit.Red, HorizontalAlignment.Center), 590, HitWarningY, 310, 28);   // Runde 13: mehr Abstand zum „RÜCK“-Label   // rechts von „RÜCK“ (bis 550), links der Schachtspalte (ab 906)
		// Runde 5: Fluchtlage unter der Beschusswarnung — wer noch reicht (dieselbe Regel wie die Simulation)
		_escape = At(this, Text("", 14, UiKit.Orange, HorizontalAlignment.Center), 590, HitWarningY + 32f, 310, 22);
		_escape.ClipText = true; _escape.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_escape.Visible = false;
		// Prüfung Phase 28: dunkler Grund unter Lage-, Fahndungs- und Beschusszeile (3D-Beschriftungen und Ringe schienen durch) und Beschusszeile links von „Nächster Gegner“
		foreach (var l in new[] { _security, _wanted, _hitWarning, _escape }) l.AddThemeStyleboxOverride("normal", UiKit.Box(new Color(0.02f, 0.04f, 0.09f, 0.82f), null, 8, 0, 2));   // Gerätetest: unter der Meldungsfläche (y 112–172), sonst verdeckt eine Meldung die Warnung
		// Runde 33 (UI-ANPASSUNG.md E2): jedes Element gehört zu einer Zone; die Lagen rechnet FlightHudLayout im Core
		_items = new()
		{
			[HudItem.Status] = _status, [HudItem.Mode0] = _modeButtons[PowerMode.Weapons], [HudItem.Mode1] = _modeButtons[PowerMode.Shield], [HudItem.Mode2] = _modeButtons[PowerMode.Engine],
			[HudItem.Chip] = _chip, [HudItem.Security] = _security, [HudItem.Wanted] = _wanted, [HudItem.Fire] = _fire,
			[HudItem.Command] = _cmdBtn, [HudItem.Map] = _mapBtn, [HudItem.Fast] = _fastBtn,
			[HudItem.Card] = _card, [HudItem.Autopilot] = _autopilotBtn, [HudItem.AutoMine] = _autoMineBtn, [HudItem.Context] = _contextBtn,
			[HudItem.TurnLabel] = _turnLabel, [HudItem.TurnLeft] = _turnL, [HudItem.TurnRight] = _turnR, [HudItem.ReverseLabel] = _reverseLabel!, [HudItem.Reverse] = _reverse,
			[HudItem.Hamburger] = _hamburger, [HudItem.Slider] = _slider,
			[HudItem.AllFire] = _allFire, [HudItem.AutoFire] = _autoFire, [HudItem.Ready] = _ready, [HudItem.NextEnemy] = _nextEnemy,
			[HudItem.AimAssist] = _aimAssist, [HudItem.Surrender] = _surrender, [HudItem.HitWarning] = _hitWarning, [HudItem.Escape] = _escape,
		};
		for (int i = 0; i < _bayButtons.Count; i++) _items[HudItem.Bay0 + i] = _bayButtons[i];
		Relayout();
	}

	Control _status = null!, _chip = null!, _chipRow = null!, _turnLabel = null!;
	Dictionary<HudItem, Control> _items = new();
	int _layoutBays;
	/// <summary>Runde 33: zuletzt gerechnete Anordnung (Selbsttest, Prüfwerkzeug).</summary>
	public FlightHudLayout? Layout { get; private set; }
	/// <summary>Runde 33, Selbsttest: Safe-Area-Ränder vorgeben (Bildpunkte bei Faktor 1, null = Gerät) — gilt seit Runde 34 für alle Schichten.</summary>
	public static UiInsets? TestInsets { get => UiScale.TestInsets; set => UiScale.TestInsets = value; }
	public void TestRelayout() { if (_viewport != Vector2.Zero) { Scale = Vector2.Zero; ApplyScale(_viewport, _wantFactor); } Relayout(); }
	public Rect2 TestSliderRect => new(_slider.Position, _slider.Size);
	public Rect2 TestReverseRect => new(_reverse.Position, _reverse.Size);
	/// <summary>Runde 33b: globale Rechtecke/Punkte (mit Skalierung der Schicht) für echte Tipps im Selbsttest.</summary>
	public Rect2 TestTurnLeftRect => _turnL.GetGlobalRect();
	public Rect2 TestAutoFireGlobal => _autoFire.GetGlobalRect();
	/// <summary>Bildschirmpunkt auf dem Schubregler beim Wert <paramref name="v"/> (Spur von x 100 bis Breite − 30, Mitte der Höhe).</summary>
	public Vector2 TestSliderPoint(float v) => _slider.GetGlobalTransform() * new Vector2(100f + v * (_slider.Size.X - 130f), _slider.Size.Y * 0.5f);

	public override void _Notification(int what)
	{
		if (what == NotificationResized && _items.Count > 0) Relayout();
	}

	/// <summary>
	/// Runde 33 (E2): Faktor der Flug-Schicht anwenden — die Schicht wird um <paramref name="factor"/> vergrößert und ihre logische Fläche entsprechend
	/// kleiner (<paramref name="viewport"/> ÷ Faktor); die Zonen ordnen sich darin neu an. Alles, was Bildschirm ↔ Welt rechnet, liest globale Rechtecke
	/// (<see cref="Control.GetGlobalRect"/> enthält die Skalierung) und bleibt in Bildpunkten.
	/// </summary>
	public void ApplyScale(Vector2 viewport, float factor)
	{
		_wantFactor = factor; _viewport = viewport;
		// Runde 33b: nie zwei Tasten in einer Zelle — der Faktor sinkt (Core: FitFactor), bis Schächte und alle Kampftasten eigene Zellen haben
		factor = FlightHudLayout.FitFactor(viewport.X, viewport.Y, SafeInsets1(), _layoutBays, factor);
		var size = viewport / factor;
		if (Mathf.Abs(Scale.X - factor) < 1e-4f && (Size - size).LengthSquared() < 0.01f && Position == Vector2.Zero) return;
		AnchorLeft = 0f; AnchorTop = 0f; AnchorRight = 0f; AnchorBottom = 0f;
		Position = Vector2.Zero;
		Scale = new Vector2(factor, factor);
		Size = size;
		Relayout();
	}

	/// <summary>
	/// Runde 33 (UI-ANPASSUNG.md E2): Zonen aus der logischen Fläche der Schicht (<see cref="FlightHudLayout"/>, reine Rechnung im Core) statt fester
	/// 1280 × 720-Lagen: waagerecht und senkrecht verankert, Safe Area an allen vier Seiten; unter 1280 × 720 (UI-Faktor &gt; 1) kompakt mit fließender
	/// Kampfspalte. Bei 1280 × 720 ohne Ränder bleibt jede Lage wie bisher.
	/// </summary>
	void Relayout()
	{
		var layout = FlightHudLayout.Compute(Size.X, Size.Y, SafeInsets(), _layoutBays);
		Layout = layout;
		foreach (var (item, c) in _items)
		{
			var r = layout[item];
			c.Position = new Vector2(r.X, r.Y);
			// nur Elemente mit veränderlicher Breite bekommen eine neue Größe (Tasten behalten ihr Maß)
			if (item is HudItem.Chip or HudItem.Security or HudItem.Wanted or HudItem.Fire or HudItem.Slider or HudItem.HitWarning or HudItem.Escape or HudItem.TurnLabel or HudItem.ReverseLabel)
			{ c.CustomMinimumSize = new Vector2(r.W, r.H); c.Size = new Vector2(r.W, r.H); }   // UiKit.At setzt das Mindestmaß — sonst bliebe die alte Breite
		}
		_chipRow.CustomMinimumSize = Vector2.Zero;
		_chipRow.Size = new Vector2(MathF.Max(0f, layout[HudItem.Chip].W - 28f), 42f);
		_turnLabel.Visible = !layout.Hidden(HudItem.TurnLabel);
		_ready.Visible = !layout.Hidden(HudItem.Ready);
		_gateAttack.Position = _stationAttack.Position = new Vector2(layout[HudItem.Attack].X, layout[HudItem.Attack].Y);
	}

	/// <summary>Sichere Ränder (Kamera-Aussparung, Gestenleiste) an allen vier Seiten in Einheiten der Schicht; nur auf Mobilgeräten (am Desktop meldet die Safe Area den Arbeitsbereich des Bildschirms, nicht das Fenster).</summary>
	float _wantFactor = 1f;
	Vector2 _viewport;
	/// <summary>Runde 33b: angeforderter Faktor der Stufe (die Schicht kann darunter liegen, siehe <see cref="FlightHudLayout.FitFactor"/>).</summary>
	public float WantedFactor => _wantFactor;

	/// <summary>Safe-Area-Ränder in Einheiten der aktuellen Schicht.</summary>
	UiInsets SafeInsets()
	{
		var i = SafeInsets1();
		float k = Scale.X > 0f ? 1f / Scale.X : 1f;
		return new UiInsets(i.Left * k, i.Top * k, i.Right * k, i.Bottom * k);
	}

	/// <summary>Runde 33b: Safe-Area-Ränder in Bildpunkten der Wurzel (Faktor 1) — Grundlage für <see cref="FlightHudLayout.FitFactor"/>.</summary>
	static UiInsets SafeInsets1() => UiScale.SafeInsets1();

	const int MaxBays = 6;
	const float BayH = 56f, BayStep = 60f;
	// Runde 33: Lagen der Schächte rechnet FlightHudLayout (klassisch wie bis Runde 32: vier rechts, weitere links daneben ab y 456)

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

	/// <summary>Phase 23: erneutes Tippen auf die aktive Stellung schaltet auf ausgeglichen zurück.</summary>
	void PressMode(PowerMode m) => PowerModePressed?.Invoke(_d.PowerMode == m ? PowerMode.Balanced : m);

	KitBar BarRow(Control parent, string name, Color fill, out Label? valueLabel, float value = 0f, bool placeholder = false)
	{
		var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		row.AddThemeConstantOverride("separation", 8);
		var l = Text(name, 16, placeholder ? new Color(UiKit.Dim, 0.75f) : UiKit.Dim);
		l.CustomMinimumSize = new Vector2(66, 0);
		row.AddChild(l);
		var bar = new KitBar(140, 12, fill);
		if (placeholder) bar.Modulate = new Color(1, 1, 1, 0.45f);
		bar.Set(value, fill);
		row.AddChild(bar);
		var v = Text(placeholder ? $"{(int)value} %" : "", 14, new Color(UiKit.Dim, 0.7f));
		v.ClipText = true; v.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; v.CustomMinimumSize = new Vector2(52, 0);
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
			BayState.NoPower or BayState.NoAmmo => UiKit.Red,
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
		_fuelRow.Visible = d.FuelActive;   // Phase 14: Treibstoff abgeschaltet → keine Tankanzeige
		// Phase 23: Schild und Leistung echt
		float shieldPct = d.ShieldMax > 0f ? 100f * d.Shield / d.ShieldMax : 0f;
		_shieldBar.Set(shieldPct, shieldPct < 30f ? UiKit.Orange : UiKit.Cyan);
		_shieldVal.Text = d.ShieldMax > 0f ? $"{(int)d.Shield}" : "–";
		float loadPct = d.PowerOutput > 0f ? 100f * d.PowerDraw / d.PowerOutput : 100f;
		_powerBar.Set(Math.Min(100f, loadPct), d.PowerDraw > d.PowerOutput ? UiKit.Red : PowerColor);
		_powerVal.Text = $"{d.PowerDraw:0}/{d.PowerOutput:0}";
		// Gerätetest: Stellung sichtbar — eigene Zeile, damit nichts über den Rahmen ragt
		_powerInfo.Text = "Stellung " + (d.PowerMode == PowerMode.Balanced ? "ausgeglichen" : SpaceSim.Core.Combat.ShipSystems.ModeName(d.PowerMode))
			+ (d.ChargeMax > 0f ? $" · Bank {(int)d.Charge}/{(int)d.ChargeMax}" : "");
		foreach (var (mode, b) in _modeButtons) b.On = d.PowerMode == mode;
		// Phase 26: negative Kasse = Schulden (z. B. laufende Leihmiete)
		_credits.Text = (d.Credits < 0f ? $"Schulden: {(int)-d.Credits} Cr" : $"{(int)d.Credits} Credits")
			+ (d.CostsPerMinute > 0.001f ? $" · −{d.CostsPerMinute.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))}/min" : "");   // Runde 16: Verbandsunterhalt sichtbar
		_credits.AddThemeColorOverride("font_color", d.Credits < 0f ? UiKit.Red : UiKit.Orange);
		_fire.Text = d.Fire;
		_fire.Visible = d.Fire.Length > 0;
		_sector.Text = d.SectorName;
		// Runde 33: im schmalen Chip (Kompakt-HUD) das Kürzel statt des langen Fraktionsnamens — mit der Schrift gemessen
		{
			var font = _factionChip.GetThemeFont("font");
			float room = _chipRow.Size.X - 12f - _sector.GetThemeFont("font").GetStringSize(d.SectorName, HorizontalAlignment.Left, -1, _sector.GetThemeFontSize("font_size")).X;
			bool fits = font.GetStringSize(d.FactionName, HorizontalAlignment.Left, -1, _factionChip.GetThemeFontSize("font_size")).X <= room;
			_factionChip.Text = fits || d.FactionTag.Length == 0 ? d.FactionName : d.FactionTag;
		}
		_security.Text = d.Security;
		_wanted.Text = d.Wanted;
		_wanted.Visible = d.Wanted.Length > 0;
		_wanted.AddThemeColorOverride("font_color", d.WantedSevere ? UiKit.Red : UiKit.Orange);
		_fastBtn.Text = d.FastOn ? "Zeit ×10" : "Zeit ×1";
		_fastBtn.On = d.FastOn;
		_autopilotBtn.On = d.AutopilotOn;
		_autoMineBtn.On = d.AutoMineOn;

		// Runde 35b (Prüfwerkzeug: „Korsar #22 ([RSC] Rostige Schakale)“ gekürzt): erst kleiner bis zur Untergrenze der Ebene, dann ohne Besitzernamen (Kürzel bleibt)
		_tName.Text = FitTargetName(d.TargetName);
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

		// Runde 33b (Prüfung: der Leviathan hat 10 Waffenschächte, das HUD 6 Tasten): mehr als sechs → die sechste Taste fasst den Rest zusammen
		// („+5 weitere“, feuert alle bereiten davon; MoreBaysDown) — vorher feuerten Schacht 7–10 nur über „Alle Feuer“/„Dauerfeuer“, ohne Anzeige
		_bayMore = d.Bays.Count > MaxBays ? d.Bays.Count - (MaxBays - 1) : 0;
		for (int i = 0; i < _bayButtons.Count; i++)
		{
			var b = _bayButtons[i];
			if (i >= d.Bays.Count) { b.Visible = false; continue; }
			if (_bayMore > 0 && i == MaxBays - 1)
			{
				int rdy = 0;
				for (int k = i; k < d.Bays.Count; k++) if (d.Bays[k].State == BayState.Ready) rdy++;
				b.Visible = true;
				b.Text = $"+{_bayMore} weitere";
				b.Sub = $"Schacht {i + 1}–{d.Bays.Count}";
				(b.State, b.Detail, b.DetailColor) = rdy > 0 ? (KitButtonState.Ready, $"{rdy} bereit · feuern", UiKit.Green) : (KitButtonState.Busy, "keiner bereit", UiKit.Dim);
				b.Progress = 0f;
				b.QueueRedraw();
				continue;
			}
			var bay = d.Bays[i];
			b.Visible = true;
			b.Text = bay.Name;
			b.Sub = bay.Place;
			(b.State, b.Detail, b.DetailColor) = bay.State switch
			{
				BayState.Ready => (KitButtonState.Ready, bay.Detail, UiKit.Green),
				BayState.OutsideArc => (KitButtonState.Warn, bay.Detail, UiKit.Orange),
				BayState.Reloading => (KitButtonState.Busy, bay.Detail, UiKit.Dim),
				BayState.NoPower or BayState.NoAmmo => (KitButtonState.Disabled, bay.Detail, UiKit.Red),
				_ => (KitButtonState.Disabled, bay.Detail, new Color(UiKit.Dim, 0.7f)),
			};
			b.Progress = bay.Reload;
			b.QueueRedraw();
		}
		_ready.Text = d.FireBays > 0 ? $"{d.ReadyBays} von {d.FireBays} bereit" : "";
		// Phase 8/25: „Alle Feuer“ (und Dauerfeuer daneben) rutschen unter die rechte Schachtspalte; die Bereit-Zeile steht über den Schächten
		// Runde 33: die Zahl der Schächte bestimmt die Lage von „Alle Feuer“/„Dauerfeuer“ (klassisch) bzw. die Zellen der Kampfspalte (kompakt)
		int shown = Math.Min(d.Bays.Count, MaxBays);
		if (d.InCombat) shown = Math.Max(shown, _layoutBays);   // Runde 34b: im Gefecht springt die Schicht nicht
		if (shown != _layoutBays) { _layoutBays = shown; if (_viewport != Vector2.Zero) ApplyScale(_viewport, _wantFactor); Relayout(); }
		_allFire.Sub = Layout is { Compact: true } && d.FireBays > 0 ? $"{d.ReadyBays} von {d.FireBays} bereit" : "";
		_autoFire.Visible = d.HasWeapons;
		_autoFire.On = d.AutoFireOn;
		// Runde 15 (Handy-Test): solange ein Überfall läuft und Dauerfeuer aus ist, pulsiert die Taste (Warnrahmen, Helligkeit)
		_autoFireHint = d.AutoFireHint && !d.AutoFireOn;
		_autoFire.State = _autoFireHint ? KitButtonState.Warn : KitButtonState.Normal;
		float pulse = _autoFireHint ? 0.85f + 0.45f * Mathf.Sin((float)Time.GetTicksMsec() / 1000f * 5f) : 1f;   // Runde 16: deutlicher (0,4 … 1,3)
		_autoFire.Modulate = new Color(1f, 1f, 1f, 1f) * new Color(pulse, pulse, pulse, 1f);
		_nextEnemy.Visible = d.HasWeapons || d.EnemyNear;
		// Prüfung Runde 2: bewaffnet, aber kein Gegner im Sektor — Taste bleibt an ihrem Platz, gesperrt mit Grund (vorher sah sie aktiv aus)
		_nextEnemy.State = d.EnemyNear ? KitButtonState.Normal : KitButtonState.Disabled;
		_nextEnemy.Sub = d.EnemyNear ? "" : "kein Gegner";
		_stationAttack.Visible = d.CanAttackStation && !d.StationAttackArmed;
		_gateAttack.Visible = (d.CanAttackGate || d.GateAttackBlocked.Length > 0) && !d.StationAttackArmed && !d.CanAttackStation;
		_gateAttack.State = d.GateAttackBlocked.Length > 0 ? KitButtonState.Disabled : KitButtonState.Warn;
		if (_gateAttack.Text != "Wirklich?") _gateAttack.Sub = d.GateAttackBlocked.Length > 0 ? d.GateAttackBlocked : d.GateAttackInfo;
		// Runde 5: aussichtslos → Warnfarbe und die Warnung statt der Geschützzeile (Hinweis, kein Verbot)
		if (_stationAttack.Text != "Wirklich?") _stationAttack.Sub = d.StationHopeless ? "Aussichtslos!" : d.StationAttackInfo;
		TestStationAttackInfo = d.StationAttackInfo;   // scharf: „nochmal tippen“ bleibt
		_aimAssist.Visible = d.AimAssistVisible;
		_aimAssist.On = d.AimAssistOn;
		_surrender.Visible = d.CanSurrender || d.SurrenderRelevant;
		_surrender.State = d.CanSurrender ? KitButtonState.Warn : KitButtonState.Disabled;
		_surrender.Sub = d.CanSurrender ? "" : d.SurrenderReason;
		_tCombat.Text = d.TargetCombat;
		_tCombat.Visible = d.TargetCombat.Length > 0;
		if (d.TargetCombat.Length > 0) _tName.AddThemeColorOverride("font_color", d.TargetHostile ? UiKit.Red : UiKit.Orange);
		_hitWarning.Text = d.HitWarning;
		_hitWarning.Visible = d.HitWarning.Length > 0;
		_tOdds.Text = d.StationOdds;
		_tOdds.Visible = d.StationOdds.Length > 0;
		_tOdds.AddThemeColorOverride("font_color", d.StationHopeless ? UiKit.Red : d.StationOdds.StartsWith("Knapp") ? UiKit.Orange : UiKit.Green);
		_escape.Text = d.EscapeStatus;
		_escape.Visible = d.EscapeStatus.Length > 0;
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
			DrawSetTransformMatrix(GetGlobalTransform().AffineInverse());   // Runde 33: Schiffs-/Zielposition kommen in Bildpunkten, die Schicht ist skaliert
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
