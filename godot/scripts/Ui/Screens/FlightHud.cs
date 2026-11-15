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

/// <summary>HUD-Neu: Anzeigedaten eines Gruppenknopfs (Waffentyp oder einzelner Bergbauschacht).</summary>
public sealed class GroupView
{
	public required string Label, Sub;
	public required BayState State;
	public float Reload;
	public bool Combat, On;
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
	/// <summary>HUD-Neu: Gruppen des Daumenbogens (Kampfwaffen nach Typ, Bergbauschächte einzeln) — Anzeige aus <see cref="SpaceSim.Core.Weapons.BayGroups"/>.</summary>
	public IReadOnlyList<GroupView> Groups = Array.Empty<GroupView>();
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
	public string TestSectorChip => _sector.Text.Replace(" · ×10", "");   // HUD-Neu: „×10“ zeigt den Zeitraffer
	public string Title => "Flug";
	public Control Node => this;
	public bool IsOpen => Visible;

	public event Action? HamburgerPressed, MapPressed, AutopilotPressed, AutoMinePressed, ContextPressed, AllFire;
	/// <summary>Finger auf einer Waffenschacht-Taste (Index in <see cref="FlightHudData.Bays"/>).</summary>
	public event Action<int>? BayDown;
	/// <summary>HUD-Neu: Zahl der Gruppen/Schächte, nach der gerade gelegt ist (im Gefecht fest).</summary>
	public int TestLayoutBays => _layoutBays;
	public int TestLayoutGroups => _layoutGroups;
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
	string FitEscape(string t)
	{
		if (t.Length == 0 || _escape.Size.X <= 0f) return t;
		var font = _escape.GetThemeFont("font");
		int fs = _escape.GetThemeFontSize("font_size");
		string noParen = System.Text.RegularExpressions.Regex.Replace(t, @"\s*\([^)]*\)", "");
		foreach (var v in new[] { t, noParen, noParen.Replace("Flucht: ", ""), noParen.Replace("Flucht: ", "").Replace(" in Reichweite", "") })
			if (font.GetStringSize(v, HorizontalAlignment.Left, -1, fs).X <= _escape.Size.X - 2f) return v;
		return noParen.Replace("Flucht: ", "").Replace(" in Reichweite", "");
	}

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
		for (int i = 0; i < bays && i < _bayButtons.Count; i++) if (_bayButtons[i].Visible) list.Add(($"Schacht {i + 1}", new Rect2(_bayButtons[i].Position, _bayButtons[i].Size)));
		for (int i = 0; i < _groupButtons.Count; i++) if (_groupButtons[i].Visible) list.Add(($"Gruppe {_groupButtons[i].Text}", new Rect2(_groupButtons[i].Position, _groupButtons[i].Size)));
		foreach (var (n, c) in new (string, Control)[] { ("FEUER", _allFire), ("Dauerfeuer", _autoFire), ("Nächster Gegner", _nextEnemy), ("Angriff", _stationAttack),
			("Ziel folgen", _aimAssist), ("Ergeben", _surrender), ("Leistung", _power), ("Einzeln", _expand), ("Beschusswarnung", _hitWarning), ("Fluchtzeile", _escape), ("Zielkarte", _tName.GetParent().GetParent<Control>()) })
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
			? GetChildren().OfType<Control>().Where(c => c.Visible && c is KitButton or KitSlider or KitVSlider && c.Size.X > 4f).Select(c => c.GetGlobalRect().Grow(6f)).ToList()
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
	public float LeftColumnX() => _slider is null ? 112f : _slider.Position.X + _slider.Size.X + 12f;   // HUD-Neu: rechts neben dem Regler, unter dem Status
	public float LeftColumnTop() => _status is null ? 160f : Layout is { ChipBelowStatus: true } cl ? cl[HudItem.Fire].Bottom + 12f : _status.Position.Y + _status.Size.Y + 12f;   // schmal: unter dem Chipblock
	public Rect2 TestModeRect(PowerMode m) => new(_power.Position, _power.Size);   // HUD-Neu: eine Taste ⚡ für alle Stellungen
	Control _fuelRow = null!;
	/// <summary>Selbsttest: ist die Tankzeile sichtbar (Phase 14: nur mit eingeschaltetem Treibstoff)?</summary>
	public bool TestFuelVisible => _fuelRow.Visible;
	Label _hullVal = null!;
	Label _powerInfo = null!;
	Label _credits = null!, _sector = null!, _factionChip = null!, _wanted = null!, _security = null!, _fire = null!;
	KitButton _mapBtn = null!;
	bool _fastOn;
	int _menuBadge;
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
	// Slider (HUD-Neu: senkrecht links)
	KitVSlider _slider = null!;
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
	public bool FastOn => _fastOn;

	/// <summary>Zähler ungelesener Comms-Nachrichten — HUD-Neu: am Menüknopf („Menü · 2“; die Kommandozentrale steht im Menü).</summary>
	public void SetCommandBadge(int unread) { _menuBadge = unread; _hamburger?.QueueRedraw(); }

	public void SetSlider(float v) => _slider.SetValue(v, notify: false);
	public void SetAutopilot(bool on) => _autopilotBtn.On = on;
	public void SetAutoMine(bool on) => _autoMineBtn.On = on;

	public void Open() => Visible = true;
	public void Close() => Visible = false;
	public void Refresh() => Apply(_d);

	public override void _Ready()
	{
		AddChild(_arcs);

		// ── Status oben links (HUD-Neu: schlank, Balken + Kasse; die Leistungsstellung zeigt ⚡ in der rechten Spalte) ──
		var status = _status = Win(this, 12, 8, 300, 132, new Color(0.03f, 0.05f, 0.1f, 0.78f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 10);
		var sv = At(status, new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, 10, 6, 280, 122);
		sv.AddThemeConstantOverride("separation", 2);
		_hullBar = BarRow(sv, "Hülle", UiKit.Green, out _hullVal!);
		_shieldBar = BarRow(sv, "Schild", UiKit.Cyan, out _shieldVal!);
		_fuelBar = BarRow(sv, "Sprit", UiKit.Orange, out _fuelVal!);
		_fuelRow = (Control)sv.GetChild(sv.GetChildCount() - 1);
		_powerBar = BarRow(sv, "Leistung", PowerColor, out _powerVal!);
		_powerInfo = Text("", 12, new Color(UiKit.Dim, 0.85f));
		_powerInfo.Visible = false;   // HUD-Neu: Stellung steht auf ⚡
		_credits = Text("", 18, UiKit.Orange);
		_credits.ClipText = true; _credits.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; _credits.CustomMinimumSize = new Vector2(1, 24);
		sv.AddChild(_credits);

		// ── Sektorchip oben Mitte mit Lage-, Fahndungs- und Brandzeile ──
		var chip = _chip = Win(this, 420, 8, 430, 42, new Color(0.03f, 0.05f, 0.1f, 0.68f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 20);
		var cr = _chipRow = At(chip, new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, 14, 0, 402, 42);
		cr.AddThemeConstantOverride("separation", 12);
		_sector = Text("", 18, UiKit.Cyan);
		_factionChip = Text("", 15, UiKit.Dim);
		cr.AddChild(_sector); cr.AddChild(_factionChip);
		_factionChip.ClipText = true; _factionChip.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_factionChip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_security = At(this, Text("", 13, UiKit.Dim, HorizontalAlignment.Center), 420, 54, 430, 18);
		_wanted = At(this, Text("", 15, UiKit.Red, HorizontalAlignment.Center), 420, 74, 430, 20);
		_fire = At(this, Text("", 15, UiKit.Red, HorizontalAlignment.Center), 420, 96, 430, 20);
		foreach (var l in new[] { _security, _wanted, _fire }) { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }

		// ── oben rechts: Karte und Menü (Symboltasten), Zielkarte links daneben ──
		_mapBtn = At(this, new KitButton("", new Vector2(64, 64)), 1140, 8);
		_mapBtn.Symbol = g => { var c = new Vector2(32, 24); g.DrawArc(c, 13, 0, Mathf.Tau, 24, UiKit.White, 2f); g.DrawLine(c + new Vector2(-13, 0), c + new Vector2(13, 0), UiKit.White, 1.5f); g.DrawLine(c + new Vector2(0, -13), c + new Vector2(0, 13), UiKit.White, 1.5f);
			UiKit.DrawText(g, 10, ThemeDB.FallbackFont, new Vector2(2, 56), "Karte", 12, UiKit.Dim, HorizontalAlignment.Center, 60); };
		_mapBtn.Pressed += () => MapPressed?.Invoke();
		_hamburger = At(this, new KitButton("", new Vector2(64, 64)), 1208, 8);
		_hamburger.Symbol = g => { foreach (var yy in new[] { 12f, 21f, 30f }) g.DrawRect(new Rect2(18, yy, 28, 4), UiKit.White);
			UiKit.DrawText(g, 10, ThemeDB.FallbackFont, new Vector2(2, 56), _menuBadge > 0 ? $"Menü · {_menuBadge}" : "Menü", 12, _menuBadge > 0 ? UiKit.Orange : UiKit.Dim, HorizontalAlignment.Center, 60); };
		_hamburger.Pressed += () => HamburgerPressed?.Invoke();

		var card = Win(this, 800, 8, 330, 118, new Color(0.03f, 0.05f, 0.1f, 0.72f), UiKit.Green, 12);
		_card = card;
		var tv = At(card, new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, 12, 6, 246, 108);
		tv.AddThemeConstantOverride("separation", 1);
		_tName = Text("", 17, UiKit.Green); _tInfo = Text("", 14, UiKit.Dim); _tArc = Text("", 14, UiKit.White);
		_tCombat = Text("", 14, UiKit.Cyan);
		_tOdds = Text("", 13, UiKit.Orange);
		foreach (var l in new[] { _tName, _tInfo, _tArc, _tCombat }) { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; l.CustomMinimumSize = new Vector2(246, 0); l.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; }
		_tName.AddThemeFontSizeOverride("font_size", 17);
		foreach (var l in new[] { _tInfo, _tArc, _tCombat }) l.AddThemeFontSizeOverride("font_size", 14);
		_tOdds.AddThemeFontSizeOverride("font_size", 13);
		_tOdds.ClipText = true; _tOdds.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; _tOdds.CustomMinimumSize = new Vector2(306, 0);
		tv.AddChild(_tName); tv.AddChild(_tInfo); tv.AddChild(_tArc); tv.AddChild(_tCombat); tv.AddChild(_tOdds);
		_tOdds.Visible = false;
		_radar = At(card, new Radar(), 258, 6, 64, 64);

		_autopilotBtn = At(this, new KitButton("Autopilot", new Vector2(161, 64)) { FontSize = 17, Toggle = true }, 800, 134);
		_autoMineBtn = At(this, new KitButton("Auto-Mining", new Vector2(161, 64)) { FontSize = 17, Toggle = true }, 969, 134);
		_autopilotBtn.Pressed += () => AutopilotPressed?.Invoke();
		_autoMineBtn.Pressed += () => AutoMinePressed?.Invoke();
		_contextBtn = At(this, new KitButton("", new Vector2(330, 64)) { FontSize = 20 }, 800, 206);
		_contextBtn.Pressed += () => ContextPressed?.Invoke();

		// ── rechte Spalte (Symboltasten 64 × 64): Leistung, Nächster Gegner, Ziel folgen, Dauerfeuer, Ergeben, Angriff ──
		_power = At(this, new KitButton("⚡", new Vector2(64, 64)) { FontSize = 22 }, 1140, 80);
		_power.Pressed += () => PowerModePressed?.Invoke(NextMode(_d.PowerMode));
		_nextEnemy = At(this, new KitButton("⊕", new Vector2(64, 64)) { FontSize = 24, Sub = "Gegner" }, 1208, 80);
		_nextEnemy.Pressed += () => NextEnemyPressed?.Invoke();
		_aimAssist = At(this, new KitButton("◎", new Vector2(64, 64)) { FontSize = 24, Sub = "folgen", Toggle = true }, 1140, 152);
		_aimAssist.Pressed += () => AimAssistPressed?.Invoke();
		_aimAssist.Visible = false;
		_autoFire = At(this, new KitButton("↻", new Vector2(64, 64)) { FontSize = 24, Sub = "Dauer", Toggle = true }, 1208, 152);
		_autoFire.Pressed += () => AutoFirePressed?.Invoke();
		_surrender = At(this, new KitButton("⚑", new Vector2(64, 64)) { FontSize = 24, Sub = "Ergeben", State = KitButtonState.Warn }, 1140, 224);
		_surrender.Pressed += () => SurrenderPressed?.Invoke();
		_surrender.Visible = false;
		_stationAttack = At(this, _attackConfirm.Wire(new KitButton("", new Vector2(64, 64)) { FontSize = 15 }, "station_attack", "Angriff",
			() => StationAttackPressed?.Invoke()), 1208, 224);
		_stationAttack.Visible = false;
		_gateAttack = At(this, _attackConfirm.Wire(new KitButton("", new Vector2(64, 64)) { FontSize = 15 }, "gate_attack", "Angriff",
			() => GateAttackPressed?.Invoke()), 1208, 224);
		_gateAttack.Visible = false;

		// ── links: senkrechter Schubregler, ◀ ▶ und RÜCK unten ──
		_slider = At(this, new KitVSlider { Snap = SpaceSim.Core.Flight.Throttle.Snap }, 12, 152, 88, 556);
		_turnL = At(this, new KitButton("◀", new Vector2(104, 100)) { FontSize = 44 }, 112, 608);
		_turnR = At(this, new KitButton("▶", new Vector2(104, 100)) { FontSize = 44 }, 224, 608);
		_reverseLabel = At(this, Text("RÜCK · max 20 %", 12, UiKit.Dim, HorizontalAlignment.Center), 332, 612, 112, 18);
		_reverse = At(this, new KitButton("RÜCK", new Vector2(96, 72)) { FontSize = 20 }, 336, 636);

		// ── unten rechts: Daumenbogen — FEUER (Tipp = alle bereiten, halten = Dauerfeuer), Gruppen nach Waffentyp, „Einzeln“ ──
		_allFire = At(this, new KitButton("FEUER", new Vector2(128, 128)) { FontSize = 26, State = KitButtonState.Warn }, 1140, 580);
		_allFire.Down += () => { _fireHeld = 0; _fireLong = false; };
		_allFire.Pressed += () => { if (!_fireLong) AllFire?.Invoke(); };
		for (int i = 0; i < MaxGroups; i++)
		{
			int idx = i;
			var g = At(this, new KitButton("", new Vector2(128, 64)) { FontSize = 17 }, 1000, 640 - i * 72);
			g.Down += () => GroupDown?.Invoke(idx);
			_groupButtons.Add(g);
		}
		_expand = At(this, new KitButton("Einzeln", new Vector2(128, 64)) { FontSize = 16, Toggle = true }, 860, 640);
		_expand.Pressed += () => { _expanded = _expand.On; LayoutForData(force: true); };
		for (int i = 0; i < MaxBays; i++)
		{
			int idx = i;
			var b = At(this, new KitButton("", new Vector2(176, 64)), 600, 134 + i * 72);
			b.Symbol = g => PaintArcSymbol(g, idx);
			b.Down += () => BayDown?.Invoke(idx);
			b.Visible = false;
			_bayButtons.Add(b);
		}
		_ready = Text("", 1, UiKit.Green);   // HUD-Neu: „n von m bereit“ steht auf FEUER und den Gruppen
		_ready.Visible = false;

		// Beschuss- und Fluchtzeile unten Mitte (zwischen RÜCK und Daumenbogen)
		_hitWarning = At(this, Text("", 17, UiKit.Red, HorizontalAlignment.Center), 452, 656, 400, 28);
		_escape = At(this, Text("", 14, UiKit.Orange, HorizontalAlignment.Center), 452, 688, 400, 22);
		_escape.ClipText = true; _escape.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_escape.Visible = false;
		foreach (var l in new[] { _security, _wanted, _hitWarning, _escape }) l.AddThemeStyleboxOverride("normal", UiKit.Box(new Color(0.02f, 0.04f, 0.09f, 0.82f), null, 8, 0, 2));
		_items = new()
		{
			[HudItem.Status] = _status, [HudItem.Chip] = _chip, [HudItem.Security] = _security, [HudItem.Wanted] = _wanted, [HudItem.Fire] = _fire,
			[HudItem.Map] = _mapBtn, [HudItem.Hamburger] = _hamburger, [HudItem.Card] = _card,
			[HudItem.Autopilot] = _autopilotBtn, [HudItem.AutoMine] = _autoMineBtn, [HudItem.Context] = _contextBtn,
			[HudItem.Power] = _power, [HudItem.NextEnemy] = _nextEnemy, [HudItem.AimAssist] = _aimAssist, [HudItem.AutoFire] = _autoFire, [HudItem.Surrender] = _surrender,
			[HudItem.Slider] = _slider, [HudItem.TurnLeft] = _turnL, [HudItem.TurnRight] = _turnR, [HudItem.Reverse] = _reverse, [HudItem.ReverseLabel] = _reverseLabel!,
			[HudItem.FireBtn] = _allFire, [HudItem.Expand] = _expand, [HudItem.HitWarning] = _hitWarning, [HudItem.Escape] = _escape,
		};
		for (int i = 0; i < _groupButtons.Count; i++) _items[HudItem.Group0 + i] = _groupButtons[i];
		for (int i = 0; i < _bayButtons.Count; i++) _items[HudItem.Bay0 + i] = _bayButtons[i];
		Relayout();
	}

	/// <summary>HUD-Neu: ⚡ schaltet die Leistungsverteilung reihum (ausgeglichen → Waffen → Schild → Antrieb → ausgeglichen).</summary>
	static PowerMode NextMode(PowerMode m) => m switch
	{
		PowerMode.Balanced => PowerMode.Weapons,
		PowerMode.Weapons => PowerMode.Shield,
		PowerMode.Shield => PowerMode.Engine,
		_ => PowerMode.Balanced,
	};

	/// <summary>HUD-Neu: FEUER halten (≥ <see cref="LongPressMs"/>) schaltet Dauerfeuer um — das Loslassen feuert dann nicht zusätzlich.</summary>
	public const ulong LongPressMs = 550;
	double _fireHeld;   // Haltezeit aus den Bildschritten (am Gerät = Echtzeit; im Kopflos-Lauf mit festem Takt dieselbe Bildzeit wie das Spiel)
	bool _fireLong;
	public override void _Process(double delta)
	{
		if (_allFire is not null && _allFire.Held) _fireHeld += delta;
		if (_allFire is not null && _allFire.Held && !_fireLong && _fireHeld * 1000.0 >= LongPressMs)
		{
			_fireLong = true;
			AutoFirePressed?.Invoke();
		}
	}

	Control _status = null!, _chip = null!, _chipRow = null!;
	Dictionary<HudItem, Control> _items = new();
	KitButton _power = null!, _expand = null!;
	readonly List<KitButton> _groupButtons = new();
	bool _expanded;
	int _layoutGroups = -1, _layoutBays = -1;
	bool _layoutFire = true;
	/// <summary>HUD-Neu: Gruppenknopf gedrückt (Index in <see cref="FlightHudData.Groups"/>).</summary>
	public event Action<int>? GroupDown;
	/// <summary>Selbsttest: Einzelschächte aufgeklappt, Gruppentexte, Lage einer Gruppe/des FEUER-Knopfs.</summary>
	public bool TestExpanded { get => _expanded; set { _expanded = value; _expand.On = value; LayoutForData(force: true); } }
	public List<string> TestGroupLabels => _groupButtons.Where(b => b.Visible).Select(b => b.Text).ToList();
	public Rect2 TestGroupRect(int i) => _groupButtons[i].GetGlobalRect();
	public bool TestGroupDisabled(int i) => _groupButtons[i].State == KitButtonState.Disabled;
	public string TestGroupSub(int i) => _groupButtons[i].Sub;
	public Rect2 TestFireRect => _allFire.GetGlobalRect();
	public Rect2 TestPowerRect => _power.GetGlobalRect();
	public bool TestExpandVisible => _expand.Visible;
	public int TestVisibleBays => _bayButtons.Count(b => b.Visible);
	/// <summary>Runde 33: zuletzt gerechnete Anordnung (Selbsttest, Prüfwerkzeug).</summary>
	public FlightHudLayout? Layout { get; private set; }
	public static UiInsets? TestInsets { get => UiScale.TestInsets; set => UiScale.TestInsets = value; }
	public void TestRelayout() { if (_viewport != Vector2.Zero) { Scale = Vector2.Zero; ApplyScale(_viewport, _wantFactor); } Relayout(); }
	public Rect2 TestSliderRect => new(_slider.Position, _slider.Size);
	public Rect2 TestReverseRect => new(_reverse.Position, _reverse.Size);
	public Rect2 TestTurnLeftRect => _turnL.GetGlobalRect();
	public Rect2 TestAutoFireGlobal => _autoFire.GetGlobalRect();
	/// <summary>Bildschirmpunkt auf dem (senkrechten) Schubregler beim Wert <paramref name="v"/>.</summary>
	public Vector2 TestSliderPoint(float v) => _slider.GetGlobalTransform() * new Vector2(_slider.Size.X * 0.5f, _slider.YOf(v));

	public override void _Notification(int what)
	{
		if (what == NotificationResized && _items.Count > 0) Relayout();
	}

	SpaceSim.Core.Ui.UiInsets _laidInsets;
	public void ApplyScale(Vector2 viewport, float factor)
	{
		_wantFactor = factor; _viewport = viewport;
		// nie zwei Tasten übereinander — der Faktor sinkt (Core: FitFactor), bis Gruppen, FEUER und die aufgeklappten Schächte ihren Platz haben
		factor = FlightHudLayout.FitFactor(viewport.X, viewport.Y, SafeInsets1(), Math.Max(0, _layoutGroups), Math.Max(0, _layoutBays), factor);
		var size = viewport / factor;
		var insets = SafeInsets1();
		if (Mathf.Abs(Scale.X - factor) < 1e-4f && (Size - size).LengthSquared() < 0.01f && Position == Vector2.Zero && insets == _laidInsets) return;
		_laidInsets = insets;
		AnchorLeft = 0f; AnchorTop = 0f; AnchorRight = 0f; AnchorBottom = 0f;
		Position = Vector2.Zero;
		Scale = new Vector2(factor, factor);
		Size = size;
		Relayout();
	}

	/// <summary>HUD-Neu: Lagen aus <see cref="FlightHudLayout"/> (Core) — Gruppen, FEUER und aufgeklappte Schächte nach den aktuellen Daten.</summary>
	void Relayout()
	{
		var layout = FlightHudLayout.Compute(Size.X, Size.Y, SafeInsets(), Math.Max(0, _layoutGroups), Math.Max(0, _layoutBays), _expanded, _layoutFire);
		Layout = layout;
		foreach (var (item, c) in _items)
		{
			var r = layout[item];
			c.Position = new Vector2(r.X, r.Y);
			if (item is HudItem.Chip or HudItem.Security or HudItem.Wanted or HudItem.Fire or HudItem.Slider or HudItem.HitWarning or HudItem.Escape or HudItem.ReverseLabel)
			{ c.CustomMinimumSize = new Vector2(r.W, r.H); c.Size = new Vector2(r.W, r.H); }
		}
		_chipRow.CustomMinimumSize = Vector2.Zero;
		_chipRow.Size = new Vector2(MathF.Max(0f, layout[HudItem.Chip].W - 28f), 42f);
		_gateAttack.Position = _stationAttack.Position = new Vector2(layout[HudItem.Attack].X, layout[HudItem.Attack].Y);
		ApplyHidden();
	}

	/// <summary>Ausblendungen der Anordnung (verdeckte Autopilot-Zeile bei aufgeklappter Leiste, leere Gruppen-/Schachtplätze).</summary>
	void ApplyHidden()
	{
		if (Layout is not { } l) return;
		foreach (var it in new[] { HudItem.Autopilot, HudItem.AutoMine }) _items[it].Visible = !l.Hidden(it);
		if (l.Hidden(HudItem.Context)) _contextBtn.Visible = false;
		_expand.Visible = !l.Hidden(HudItem.Expand);
		_allFire.Visible = !l.Hidden(HudItem.FireBtn) && _d.HasWeapons;
		for (int i = 0; i < _groupButtons.Count; i++) if (l.Hidden(HudItem.Group0 + i)) _groupButtons[i].Visible = false;
		for (int i = 0; i < _bayButtons.Count; i++) if (l.Hidden(HudItem.Bay0 + i)) _bayButtons[i].Visible = false;
	}

	/// <summary>Neu legen, wenn sich Zahl der Gruppen/Schächte, FEUER oder der Aufklappzustand ändert (im Gefecht bleibt die Zahl stehen).</summary>
	void LayoutForData(bool force = false)
	{
		// Einzelschächte nur, wenn eine Gruppe mehrere Schächte hat (sonst zeigt der Bogen schon jeden Schacht)
		int groups = Math.Min(_d.Groups.Count, MaxGroups), bays = _d.Bays.Count > _d.Groups.Count || _d.Groups.Count > MaxGroups ? Math.Min(_d.Bays.Count, MaxBays) : 0;
		if (_d.InCombat) { groups = Math.Max(groups, _layoutGroups); bays = Math.Max(bays, _layoutBays); }
		bool fire = _d.HasWeapons;
		if (!force && groups == _layoutGroups && bays == _layoutBays && fire == _layoutFire) return;
		_layoutGroups = groups; _layoutBays = bays; _layoutFire = fire;
		if (bays < 2 && _expanded) { _expanded = false; _expand.On = false; }
		if (_viewport != Vector2.Zero) { Scale = Vector2.Zero; ApplyScale(_viewport, _wantFactor); }
		Relayout();
	}

	float _wantFactor = 1f;
	Vector2 _viewport;
	/// <summary>angeforderter Faktor der Stufe (die Schicht kann darunter liegen, siehe <see cref="FlightHudLayout.FitFactor"/>).</summary>
	public float WantedFactor => _wantFactor;

	UiInsets SafeInsets()
	{
		var i = SafeInsets1();
		float k = Scale.X > 0f ? 1f / Scale.X : 1f;
		return new UiInsets(i.Left * k, i.Top * k, i.Right * k, i.Bottom * k);
	}

	static UiInsets SafeInsets1() => UiScale.SafeInsets1();

	const int MaxBays = FlightHudLayout.MaxBays, MaxGroups = FlightHudLayout.MaxGroups;

	static Control Win(Control parent, float x, float y, float w, float h, Color bg, Color? border, int radius)
	{
		var c = At(parent, new Control { MouseFilter = Control.MouseFilterEnum.Ignore }, x, y, w, h);
		var p = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
		p.AddThemeStyleboxOverride("panel", Box(bg, border, radius, 2, 0));
		c.AddChild(p);
		p.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		return c;
	}

	/// <summary>Phase 23: erneutes Tippen auf die aktive Stellung schaltet auf ausgeglichen zurück (Selbsttest; das HUD schaltet über ⚡ reihum).</summary>
	void PressMode(PowerMode m) => PowerModePressed?.Invoke(_d.PowerMode == m ? PowerMode.Balanced : m);

	KitBar BarRow(Control parent, string name, Color fill, out Label? valueLabel, float value = 0f, bool placeholder = false)
	{
		var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		row.AddThemeConstantOverride("separation", 8);
		var l = Text(name, 14, placeholder ? new Color(UiKit.Dim, 0.75f) : UiKit.Dim);
		l.CustomMinimumSize = new Vector2(66, 0);
		row.AddChild(l);
		var bar = new KitBar(140, 10, fill);
		if (placeholder) bar.Modulate = new Color(1, 1, 1, 0.45f);
		bar.Set(value, fill);
		row.AddChild(bar);
		var v = Text(placeholder ? $"{(int)value} %" : "", 13, new Color(UiKit.Dim, 0.7f));
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
		_fuelBar.Set(fuelPct, fuelPct < 20f ? UiKit.Red : UiKit.Orange);
		_fuelVal.Text = d.FuelCapacity > 0f ? $"{(int)d.Fuel}" : "–";
		_fuelRow.Visible = d.FuelActive;
		float shieldPct = d.ShieldMax > 0f ? 100f * d.Shield / d.ShieldMax : 0f;
		_shieldBar.Set(shieldPct, shieldPct < 30f ? UiKit.Orange : UiKit.Cyan);
		_shieldVal.Text = d.ShieldMax > 0f ? $"{(int)d.Shield}" : "–";
		float loadPct = d.PowerOutput > 0f ? 100f * d.PowerDraw / d.PowerOutput : 100f;
		_powerBar.Set(Math.Min(100f, loadPct), d.PowerDraw > d.PowerOutput ? UiKit.Red : PowerColor);
		_powerVal.Text = $"{d.PowerDraw:0}/{d.PowerOutput:0}";
		// HUD-Neu: Stellung auf ⚡ (Unterzeile), aktiv = nicht ausgeglichen
		_power.Sub = d.PowerMode switch { PowerMode.Weapons => "Waffen", PowerMode.Shield => "Schild", PowerMode.Engine => "Antrieb", _ => "ausgegl." };
		_power.On = d.PowerMode != PowerMode.Balanced;
		_powerInfo.Text = "Stellung " + (d.PowerMode == PowerMode.Balanced ? "ausgeglichen" : SpaceSim.Core.Combat.ShipSystems.ModeName(d.PowerMode))
			+ (d.ChargeMax > 0f ? $" · Bank {(int)d.Charge}/{(int)d.ChargeMax}" : "");
		_credits.Text = (d.Credits < 0f ? $"Schulden: {(int)-d.Credits} Cr" : $"{(int)d.Credits} Credits")
			+ (d.CostsPerMinute > 0.001f ? $" · −{d.CostsPerMinute.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))}/min" : "");
		_credits.AddThemeColorOverride("font_color", d.Credits < 0f ? UiKit.Red : UiKit.Orange);
		_fire.Text = d.Fire;
		_fire.Visible = d.Fire.Length > 0;
		// HUD-Neu: der Zeitraffer steht im Menü — läuft er, zeigt der Sektorchip „×10“
		_sector.Text = d.FastOn ? $"{d.SectorName} · ×10" : d.SectorName;
		{
			var font = _factionChip.GetThemeFont("font");
			float room = _chipRow.Size.X - 12f - _sector.GetThemeFont("font").GetStringSize(_sector.Text, HorizontalAlignment.Left, -1, _sector.GetThemeFontSize("font_size")).X;
			bool fits = font.GetStringSize(d.FactionName, HorizontalAlignment.Left, -1, _factionChip.GetThemeFontSize("font_size")).X <= room;
			_factionChip.Text = fits || d.FactionTag.Length == 0 ? d.FactionName : d.FactionTag;
		}
		_security.Text = d.Security;
		_wanted.Text = d.Wanted;
		_wanted.Visible = d.Wanted.Length > 0;
		_wanted.AddThemeColorOverride("font_color", d.WantedSevere ? UiKit.Red : UiKit.Orange);
		_fastOn = d.FastOn;
		_autopilotBtn.On = d.AutopilotOn;
		_autoMineBtn.On = d.AutoMineOn;

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

		// HUD-Neu: Anordnung nach Zahl der Gruppen/Schächte und FEUER (im Gefecht springt sie nicht)
		LayoutForData();

		// Gruppen im Daumenbogen
		for (int i = 0; i < _groupButtons.Count; i++)
		{
			var b = _groupButtons[i];
			if (i >= d.Groups.Count || Layout is { } lg && lg.Hidden(HudItem.Group0 + i)) { b.Visible = false; continue; }
			var g = d.Groups[i];
			b.Visible = true;
			b.Text = g.Label;
			b.Sub = g.Sub;
			b.State = g.State switch
			{
				BayState.Ready => KitButtonState.Ready,
				BayState.OutsideArc => KitButtonState.Warn,
				BayState.Reloading => KitButtonState.Busy,
				_ => g.On ? KitButtonState.On : KitButtonState.Disabled,
			};
			b.Progress = g.Reload;
			b.QueueRedraw();
		}
		// Einzelschächte (aufgeklappt)
		for (int i = 0; i < _bayButtons.Count; i++)
		{
			var b = _bayButtons[i];
			if (!_expanded || i >= d.Bays.Count || Layout is { } lb && lb.Hidden(HudItem.Bay0 + i)) { b.Visible = false; continue; }
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
		_expand.Sub = $"{d.Bays.Count} Schächte";
		// FEUER: alle bereiten; halten = Dauerfeuer (Zustand in der Unterzeile)
		_allFire.Visible = d.HasWeapons && !(Layout?.Hidden(HudItem.FireBtn) ?? false);
		_allFire.Sub = d.AutoFireOn ? "Dauerfeuer AN" : d.FireBays > 0 ? $"{d.ReadyBays}/{d.FireBays} bereit" : "";
		_allFire.Detail = d.AutoFireOn ? "" : "halten = Dauer";
		_allFire.State = d.ReadyBays > 0 || d.AutoFireOn ? KitButtonState.Warn : KitButtonState.Busy;
		_autoFire.Visible = d.HasWeapons;
		_autoFire.On = d.AutoFireOn;
		_autoFireHint = d.AutoFireHint && !d.AutoFireOn;
		_autoFire.State = _autoFireHint ? KitButtonState.Warn : KitButtonState.Normal;
		float pulse = _autoFireHint ? 0.85f + 0.45f * Mathf.Sin((float)Time.GetTicksMsec() / 1000f * 5f) : 1f;
		_autoFire.Modulate = new Color(pulse, pulse, pulse, 1f);
		_allFire.Modulate = _autoFireHint ? new Color(pulse, pulse, pulse, 1f) : Colors.White;   // auch FEUER pulsiert (Langdruck = Dauerfeuer)
		_nextEnemy.Visible = d.HasWeapons || d.EnemyNear;
		_nextEnemy.State = d.EnemyNear ? KitButtonState.Normal : KitButtonState.Disabled;
		_nextEnemy.Sub = d.EnemyNear ? "Gegner" : "keiner";
		_stationAttack.Visible = d.CanAttackStation && !d.StationAttackArmed;
		_gateAttack.Visible = (d.CanAttackGate || d.GateAttackBlocked.Length > 0) && !d.StationAttackArmed && !d.CanAttackStation;
		_gateAttack.State = d.GateAttackBlocked.Length > 0 ? KitButtonState.Disabled : KitButtonState.Warn;
		// HUD-Neu: Angriffstaste ist ein 64er-Symbol — Gegenwehr, Kräfte und Sperrgrund stehen auf der Zielkarte; die Taste nennt nur „Station“/„Tor“
		if (_gateAttack.Text != "Wirklich?") _gateAttack.Sub = d.GateAttackBlocked.Length > 0 ? "gesperrt" : "Tor";
		if (_stationAttack.Text != "Wirklich?") _stationAttack.Sub = d.StationHopeless ? "aussichtsl." : "Station";
		TestStationAttackInfo = d.StationAttackInfo;
		TestGateAttackInfo = d.GateAttackBlocked.Length > 0 ? d.GateAttackBlocked : d.GateAttackInfo;
		_aimAssist.Visible = d.AimAssistVisible;
		_aimAssist.On = d.AimAssistOn;
		_surrender.Visible = d.CanSurrender || d.SurrenderRelevant;
		_surrender.State = d.CanSurrender ? KitButtonState.Warn : KitButtonState.Disabled;
		_surrender.Sub = d.CanSurrender ? "Ergeben" : "Ergeben";
		TestSurrenderReason = d.CanSurrender ? "" : d.SurrenderReason;
		_tCombat.Text = d.TargetCombat;
		_tCombat.Visible = d.TargetCombat.Length > 0;
		if (d.TargetCombat.Length > 0) _tName.AddThemeColorOverride("font_color", d.TargetHostile ? UiKit.Red : UiKit.Orange);
		_hitWarning.Text = d.HitWarning;
		_hitWarning.Visible = d.HitWarning.Length > 0;
		// HUD-Neu: Gegenwehr der gewählten Station bzw. Sperrgrund des Tors stehen in der Kräftezeile der Zielkarte (die Taste ist dafür zu klein)
		string odds = d.StationOdds.Length > 0 ? d.StationOdds : (d.CanAttackStation && d.StationAttackInfo.Length > 0 ? d.StationAttackInfo : d.CanAttackGate || d.GateAttackBlocked.Length > 0 ? TestGateAttackInfo : "");
		_tOdds.Text = odds;
		_tOdds.Visible = odds.Length > 0;
		_tOdds.AddThemeColorOverride("font_color", d.StationHopeless || d.GateAttackBlocked.Length > 0 ? UiKit.Red : odds.StartsWith("Knapp") ? UiKit.Orange : UiKit.Green);
		_escape.Text = FitEscape(d.EscapeStatus);
		_escape.Visible = d.EscapeStatus.Length > 0;
		ApplyHidden();
		_arcs.Data = d;
		_arcs.QueueRedraw();
	}

	/// <summary>HUD-Neu: Begründungen, die nicht mehr auf der kleinen Taste stehen (Selbsttest).</summary>
	public string TestGateAttackInfo { get; private set; } = "";
	public string TestSurrenderReason { get; private set; } = "";

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
