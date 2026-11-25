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
	/// <summary>UI-Umbau: Laderaum (belegt/gesamt in m³), Farbstücke je Ware (Farbe, Anteil am Laderaum) und Kurztext der größten Posten; versiegelte Auftragsfracht in Gold.</summary>
	public float CargoUsed, CargoCap;
	public List<(Color Color, float Share)> CargoSegments = new();
	public string CargoText = "";
	/// <summary>UI-Umbau: ungelesene Nachrichten (Kopfleiste), Zahl der gescannten Objekte und der Feinde darunter (Taste „☰ Sektor“).</summary>
	public int CommsUnread, ScanCount, ScanHostile;
	/// <summary>Runde 16: laufende Kosten je Minute (Flottenunterhalt + Liegegebühr; 0 = keine) — erklärt, warum die Kasse ohne Kauf sinkt.</summary>
	public float CostsPerMinute;
	/// <summary>Tankinhalt und Tankgröße des Spielerschiffs (echt, Phase 4c); Kapazität 0 = kein Treibstoffsystem.</summary>
	public float Fuel, FuelCapacity;
	/// <summary>Phase 14: Treibstoff eingeschaltet (Regel fuelActive)? Aus = Tankzeile ausgeblendet.</summary>
	public bool FuelActive = true;
	/// <summary>Tankanzeige: Kurzname der verbrannten Treibstoffart (Zeilenbeschriftung), Infozeile „Inhalt/Tank · Reichweite · Sprünge“, knapp = rot.</summary>
	public string FuelLabel = "Sprit", FuelInfo = "";
	public bool FuelLow;
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
	/// <summary>Hitze (KAMPF.md „Hitze und Wärmeabfuhr“): Schalter an (Zeile sichtbar), Anteil 0..1, Sperre und Warnzeile (leer = keine).</summary>
	public bool HeatActive;
	public float HeatShare;
	public bool HeatLocked;
	public string HeatWarning = "";
	/// <summary>Runde 41: Auftragszeile des aktiven Auftrags (Gold; leer = ausgeblendet) — Text aus <see cref="SpaceSim.Core.Missions.MissionMarkers"/>.</summary>
	public string Mission = "";
	/// <summary>Mehrpunkt-Route: „Route 2/4 → …“ (leer = keine Route) — Text aus <see cref="SpaceSim.Core.Flight.PlayerRoutes.HudLine"/>.</summary>
	public string Route = "";
	// Zielkarte
	public string TargetName = "Kein Ziel";
	/// <summary>Runde 44i: kürzere Formen des Zielnamens (längste zuerst, z. B. aus <see cref="SpaceSim.Core.Model.StationNames.Candidates"/>);
	/// die Zielkarte nimmt die erste, die bis zur Schrift-Untergrenze passt. null = nur <see cref="TargetName"/>. Ein Vorsatz („Angriff: “) steht in <see cref="TargetPrefix"/>.</summary>
	public IReadOnlyList<string>? TargetNameOptions;
	public string TargetPrefix = "";
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
	/// <summary>Runde 44i: Kontexttaste aus Verb („Andocken“) und Namensformen — eine Zeile „Verb: Name“, wenn eine Form passt, sonst Verb oben und der
	/// Name als Unterzeile. Gesetzt, überschreibt <see cref="ContextLabel"/>.</summary>
	public string ContextVerb = "";
	public IReadOnlyList<string>? ContextNameOptions;
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
	/// <summary>Ausbau Punkt 1: „Verband greift an“ — Zahl der eigenen Schiffe, die das gewählte Ziel angreifen würden ("" = Taste aus).</summary>
	public string WingAttack = "";
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
	/// <summary>Runde 41: Gold der Missionsmarker (Randpfeile, Auftragszeile, Leuchtfeuer).</summary>
	public static readonly Color MissionGold = new(1f, 0.84f, 0.2f);

	public string Id => "flight";
	/// <summary>Selbsttest: angezeigte Fahndungs- und Lagezeile (Phase 11).</summary>
	public string TestWanted => _wanted.Visible ? _wanted.Text : "";
	/// <summary>Phase 26: sichtbare Brandwarnung (leer = keine) und Kassenanzeige (zeigt „Schulden“ bei negativer Kasse).</summary>
	public string TestFire => _fire.Visible ? _fire.Text : "";
	/// <summary>Runde 41: sichtbare Auftragszeile (leer = kein aktiver Auftrag).</summary>
	public string TestMission => _mission.Visible ? _mission.Text : "";
	/// <summary>Mehrpunkt-Route (Selbsttest): sichtbare Routenzeile.</summary>
	public string TestRoute => _route.Visible ? _route.Text : "";
	/// <summary>Mehrpunkt-Route (Selbsttest): passt die Routenzeile ohne Kürzung mit „…“?</summary>
	public bool TestRouteFits => !_route.Visible || _route.GetThemeFont("font").GetStringSize(_route.Text, HorizontalAlignment.Left, -1, _route.GetThemeFontSize("font_size")).X <= _route.Size.X - 2f;

	/// <summary>Mehrpunkt-Route: Zeile nach gemessener Breite — erst „· angehalten“ kürzen, dann den Haltnamen; „Route n/m“ bleibt immer.</summary>
	static string FitRouteLine(string full, Label label)
	{
		if (full.Length == 0) return full;
		var font = label.GetThemeFont("font");
		int size = label.GetThemeFontSize("font_size");
		float maxW = label.Size.X - 4f;
		float W(string x) => font.GetStringSize(x, HorizontalAlignment.Left, -1, size).X;
		if (maxW <= 10f || W(full) <= maxW) return full;
		string tail = full.EndsWith(" · angehalten", StringComparison.Ordinal) ? " · halt" : "";
		string body = tail.Length > 0 ? full[..^" · angehalten".Length] : full;
		if (W(body + tail) <= maxW) return body + tail;
		int arrow = body.IndexOf(" → ", StringComparison.Ordinal);
		if (arrow < 0) return body;
		string head = body[..(arrow + 3)], name = body[(arrow + 3)..];
		for (int c = name.Length - 1; c >= 3; c--)
		{
			string cand = head + name[..c].TrimEnd() + "…" + tail;
			if (W(cand) <= maxW) return cand;
		}
		return body[..arrow];
	}

	/// <summary>
	/// Runde 42d (Handytest): Auftragszeile nach gemessener Breite kürzen (vorher abgeschnitten „…· noch…“) — zuerst der
	/// Klammerzusatz, dann das Ziel stufenweise, zuletzt nur „Auftrag … · noch m:ss“. Die Restzeit bleibt immer sichtbar.
	/// </summary>
	static string FitMissionLine(string full, Label label)
	{
		if (full.Length == 0) return full;
		var font = label.GetThemeFont("font");
		int size = label.GetThemeFontSize("font_size");
		float maxW = label.Size.X - 4f;
		float W(string s) => font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
		if (maxW <= 10f || W(full) <= maxW) return full;
		int noch = full.IndexOf(" · noch ", StringComparison.Ordinal);
		if (noch < 0) return full;   // keine Restzeit im Text — nichts zu kürzen
		string head = full[..noch], rest = full[noch..];
		int paren = rest.IndexOf(" (", StringComparison.Ordinal);
		string restShort = paren > 0 ? rest[..paren] : rest;
		foreach (string cand in MissionLineCandidates(head, rest, restShort))
			if (W(cand) <= maxW) return cand;
		return "Auftrag …" + restShort;
	}

	/// <summary>Kandidaten der Auftragszeile, längste zuerst: Ziel (nach „→ “/„— Ziel in “) stufenweise gekürzt.</summary>
	static IEnumerable<string> MissionLineCandidates(string head, string rest, string restShort)
	{
		yield return head + restShort;   // Klammerzusatz „(Andocken …)“ zuerst weglassen
		int arrow = head.LastIndexOf("→ ", StringComparison.Ordinal);
		int dash = head.LastIndexOf("— Ziel in ", StringComparison.Ordinal);
		int sep = Math.Max(arrow, dash);
		string[] heads;
		if (sep >= 0)
		{
			int zs = sep + (arrow >= dash ? 2 : 10);
			string prefix = head[..zs], ziel = head[zs..];
			heads = new[] { 20, 14, 10, 6, 3 }.Where(c => ziel.Length > c).Select(c => prefix + ziel[..c].TrimEnd() + "…").ToArray();
		}
		else
		{
			heads = new[] { 28, 20, 12, 6 }.Where(c => head.Length > c).Select(c => head[..c].TrimEnd() + "…").ToArray();
		}
		foreach (var h in heads) yield return h + restShort;
		foreach (var h in heads) yield return h + rest;
	}
	public string TestCredits => _credits.Text;
	public string TestSecurity => _security.Text;
	/// <summary>Selbsttest (Gerätetest Phase 27): angezeigter Sektorname im Chip.</summary>
	public string TestSectorChip => _sector.Text.Replace(" · ×10", "");   // HUD-Neu: „×10“ zeigt den Zeitraffer
	public string Title => "Flug";
	public Control Node => this;
	public bool IsOpen => Visible;

	public event Action? HamburgerPressed, MapPressed, AutopilotPressed, AutoMinePressed, ContextPressed, AllFire;
	/// <summary>UI-Umbau: Kopfleiste (Nachrichten, Befehle = Kommandozentrale, Zeitraffer), „☰ Sektor“ unten links, Frachtzeile (Inventar).</summary>
	public event Action? CommsPressed, CommandPressed, FastPressed, ScanPressed, CargoPressed;
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
	/// <summary>Ausbau Punkt 1: „Verband greift an“ (zwei Tipps).</summary>
	public event Action? WingAttackPressed;
	KitButton _wingAttack = null!;
	/// <summary>Selbsttest: Taste „Verband greift an“.</summary>
	public KitButton TestWingAttack => _wingAttack;
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
	public string TestContextLabel => _contextBtn.Visible ? (_contextBtn.Sub.Length > 0 ? $"{_contextBtn.Text}: {_contextBtn.Sub}" : _contextBtn.Text) : "";
	/// <summary>Runde 44i: gezeichnete Zeilen der Kontexttaste ohne „…“ (leer = alles passt).</summary>
	public string TestContextOverflow => _contextBtn.Overflow();
	public bool TestTargetNameFits { get { var f = _tName.GetThemeFont("font"); int fs = _tName.GetThemeFontSize("font_size"); return f.GetStringSize(_tName.Text, HorizontalAlignment.Left, -1, fs).X <= _tName.CustomMinimumSize.X + 0.5f; } }

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
	KitBar _hullBar = null!, _fuelBar = null!, _shieldBar = null!, _powerBar = null!, _heatBar = null!;
	Label _heatVal = null!;
	Control _heatRow = null!;
	/// <summary>Selbsttest: Hitzezeile (sichtbar, Wert, Farbe rot = gesperrt/kritisch) und Status-/Zeilenrechteck.</summary>
	public bool TestHeatVisible => _heatRow.Visible;
	public string TestHeatText => _heatVal.Text;
	public Rect2 TestHeatRowRect => _heatRow.GetGlobalRect();
	public Rect2 TestStatusRect => _status.GetGlobalRect();
	public Rect2 TestCargoTextRect => _cargoText.GetGlobalRect();
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
		var list = Extra?.Invoke(false) ?? new List<Rect2>();   // eigene Stationen: Platzierungsmodus ersetzt das HUD — seine Tasten und Karte sind belegt
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
	/// <summary>Eigene Stationen: weitere belegte Flächen in Bildpunkten (Platzierungs-HUD); Argument = nur Tasten.</summary>
	public Func<bool, List<Rect2>>? Extra { get; set; }

	public List<Rect2> OccupiedLocal(bool interactiveOnly = false, bool importantOnly = false)
	{
		var list = interactiveOnly
			? (Extra?.Invoke(true) ?? new List<Rect2>()).Concat(GetChildren().OfType<Control>().Where(c => c.IsVisibleInTree() && c is KitButton or KitSlider or KitVSlider && c.Size.X > 4f).Select(c => c.GetGlobalRect().Grow(6f))).ToList()
			: importantOnly
				// Runde 35b: alles außer Sektorchip und Lagezeile — die darf eine Meldung im Notfall kurz überdecken (Status, Beschuss-/Fluchtzeile, Zielkarte nie)
				? GetChildren().OfType<Control>().Where(c => c.IsVisibleInTree() && c != _chip && c != _security && c.Size.X > 4f && c.Size.Y > 4f
					&& c.Size.X * c.Size.Y < 0.3f * GetViewportRect().Size.X * GetViewportRect().Size.Y / MathF.Max(1f, Scale.X * Scale.X)).Select(c => c.GetGlobalRect().Grow(6f)).Concat(Extra?.Invoke(false) ?? new List<Rect2>()).ToList()
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
	Label _fuelName = null!, _fuelInfo = null!;
	public string TestFuelInfo => _fuelInfo.Text;
	public string TestFuelName => _fuelName.Text;
	public bool TestFuelLow => _fuelLowShown;
	bool _fuelLowShown;
	/// <summary>Selbsttest: ist die Tankzeile sichtbar (Phase 14: nur mit eingeschaltetem Treibstoff)?</summary>
	public bool TestFuelVisible => _fuelRow.Visible;
	Label _hullVal = null!;
	Label _powerInfo = null!;
	Label _credits = null!, _sector = null!, _factionChip = null!, _wanted = null!, _security = null!, _fire = null!, _mission = null!, _route = null!;
	KitButton _mapBtn = null!;
	bool _fastOn;
	int _menuBadge;
	// Zielkarte
	Label _tName = null!, _tInfo = null!, _tArc = null!;

	/// <summary>Runde 44i: erste Namensform, die mit dem Vorsatz bis zur Schrift-Untergrenze in die Zielkarte passt (sonst die kürzeste).</summary>
	string FitTargetName(string prefix, IReadOnlyList<string> options)
	{
		var font = _tName.GetThemeFont("font");
		float room = _tName.CustomMinimumSize.X;
		int floor = SpaceSim.Core.Ui.UiLayout.FontFloor(17, UiKit.LayerFactor(_tName));
		string pick = prefix + options[^1];
		foreach (var o in options)
			if (font.GetStringSize(prefix + o, HorizontalAlignment.Left, -1, floor).X <= room + 0.5f) { pick = prefix + o; break; }
		return FitTargetName(pick);
	}

	/// <summary>Runde 44i: Kontexttaste — „Verb: Name“ in einer Zeile mit der ersten passenden Namensform, sonst Verb oben und Name als Unterzeile.</summary>
	void SetContext(string verb, IReadOnlyList<string> options)
	{
		var font = ThemeDB.FallbackFont;
		float room = _contextBtn.Size.X - 12f;
		int floor = SpaceSim.Core.Ui.UiLayout.FontFloor(_contextBtn.FontSize, UiKit.LayerFactor(_contextBtn));
		foreach (var o in options)
			if (font.GetStringSize($"{verb}: {o}", HorizontalAlignment.Left, -1, floor).X <= room) { _contextBtn.Text = $"{verb}: {o}"; _contextBtn.Sub = ""; return; }
		_contextBtn.Text = verb;
		string sub = options[^1];
		foreach (var o in options)
			if (font.GetStringSize(o, HorizontalAlignment.Left, -1, 13).X <= room) { sub = o; break; }
		_contextBtn.Sub = sub;
	}

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
	/// <summary>Runde 46 (Befehlsstand): Steuerelemente (Regler, ◀ ▶, RÜCK) ausblenden — die Steuerung ist gesperrt, dort steht die Befehlsstand-Leiste mit X.</summary>
	public void SetSteeringVisible(bool on)
	{
		if (_slider is null || _slider.Visible == on) return;
		_slider.Visible = on; _turnL.Visible = on; _turnR.Visible = on; _reverse.Visible = on;
		if (_reverseLabel is not null) _reverseLabel.Visible = on;
	}
	// Waffenschächte
	readonly List<KitButton> _bayButtons = new();
	KitButton _allFire = null!;
	Label _ready = null!;
	// Kegel um das Schiff

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
	/// <summary>Eingabe-Protokoll (zweiter Handytest A15): welcher Finger hält ◀/▶/RÜCK bzw. hat den Regler zuletzt bewegt.</summary>
	public string TurnLeftInfo => _turnL.HeldInfo;
	public string TurnRightInfo => _turnR.HeldInfo;
	public string ReverseInfo => _reverse.HeldInfo;
	public string SliderInfo => _slider.LastUserInfo;
	public float SliderValue => _slider.Value;
	public bool AutopilotOn => _autopilotBtn.On;
	public bool AutoMineOn => _autoMineBtn.On;
	public bool FastOn => _fastOn;

	/// <summary>Zähler ungelesener Comms-Nachrichten — HUD-Neu: am Menüknopf („Menü · 2“; die Kommandozentrale steht im Menü).</summary>
	public void SetCommandBadge(int unread) { _menuBadge = unread; _commsBtn?.QueueRedraw(); }

	public void SetSlider(float v) => _slider.SetValue(v, notify: false);
	bool _sliderTouched;
	/// <summary>
	/// Geräte-Eingabe (Handytest A15): true, wenn seit dem letzten Abruf ein Finger, der auf dem Regler aufgesetzt hat, seinen Wert geändert hat (bewusste
	/// Bedienung). Setzt die Marke zurück. Nur das (oder eine Taste) bricht Autopilot/Auto-Mining ab — nicht ein bloßer Reglerwert ≠ 0.
	/// </summary>
	public bool TakeSliderTouched() { bool t = _sliderTouched; _sliderTouched = false; return t; }
	/// <summary>Selbsttest: Zahl der bewussten Regleränderungen.</summary>
	public int TestSliderUserChanges => _slider.UserChanges;
	public void SetAutopilot(bool on) => _autopilotBtn.On = on;
	public void SetAutoMine(bool on) => _autoMineBtn.On = on;

	public void Open() => Visible = true;
	public void Close() => Visible = false;
	public void Refresh() => Apply(_d);

	public override void _Ready()
	{
		// UI-Umbau (Interstellar-Pilot-2-Aufbau): eine Kopfleiste über die ganze Breite — zuerst gelegt, damit sie hinter allem liegt
		_topBar = Win(this, 0, 0, 1280, FlightHudLayout.BarH, new Color(0.03f, 0.05f, 0.1f, 0.74f), new Color(0.3f, 0.5f, 0.8f, 0.35f), 0);

		// ── Status oben links (HUD-Neu: schlank, Balken + Kasse; die Leistungsstellung zeigt ⚡ in der rechten Spalte) ──
		var status = _status = Win(this, 10, 78, 260, 140, new Color(0.03f, 0.05f, 0.1f, 0.78f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 10);
		var sv = At(status, new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }, 10, 4, 280, 126);
		sv.AddThemeConstantOverride("separation", 0);   // Tankanzeige: Platz für die Tank-Infozeile (vorher 2)
		_hullBar = BarRow(sv, "Hülle", UiKit.Green, out _hullVal!);
		_shieldBar = BarRow(sv, "Schild", UiKit.Cyan, out _shieldVal!);
		_fuelBar = BarRow(sv, "Sprit", UiKit.Orange, out _fuelVal!);
		_fuelRow = (Control)sv.GetChild(sv.GetChildCount() - 1);
		_fuelName = (Label)_fuelRow.GetChild(0);
		// Tankanzeige: Inhalt/Tank, Restreichweite und grobe Sprünge der verbrannten Art (weitere Arten dahinter)
		_fuelInfo = Text("", 12, new Color(UiKit.Dim, 0.9f));
		_fuelInfo.ClipText = true; _fuelInfo.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; _fuelInfo.CustomMinimumSize = new Vector2(1, 13);
		sv.AddChild(_fuelInfo);
		_powerBar = BarRow(sv, "Leistung", PowerColor, out _powerVal!);
		// Hitze: eigene Zeile unter der Leistung (nur mit Schalter shipHeat; der Status wird dann um FlightHudLayout.HeatRowH höher)
		_heatBar = BarRow(sv, "Hitze", UiKit.Orange, out _heatVal!);
		_heatRow = (Control)sv.GetChild(sv.GetChildCount() - 1);
		_heatRow.Visible = false;
		_powerInfo = Text("", 12, new Color(UiKit.Dim, 0.85f));
		_powerInfo.Visible = false;   // HUD-Neu: Stellung steht auf ⚡
		// UI-Umbau: Fracht im Status (Farbleiste je Ware + größte Posten); Tipp öffnet das Inventar
		var cargoRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		cargoRow.AddThemeConstantOverride("separation", 8);
		var cl = Text("Fracht", 14, UiKit.Dim); cl.CustomMinimumSize = new Vector2(66, 0);
		cargoRow.AddChild(cl);
		_cargoBar = new CargoBar { CustomMinimumSize = new Vector2(110, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter };
		cargoRow.AddChild(_cargoBar);
		_cargoVal = Text("", 13, new Color(UiKit.Dim, 0.85f));
		_cargoVal.ClipText = true; _cargoVal.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; _cargoVal.CustomMinimumSize = new Vector2(52, 0);
		cargoRow.AddChild(_cargoVal);
		sv.AddChild(cargoRow);
		_cargoText = Text("", 13, new Color(UiKit.Dim, 0.9f));
		_cargoText.ClipText = true; _cargoText.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; _cargoText.CustomMinimumSize = new Vector2(1, 16);
		sv.AddChild(_cargoText);
		// ganze Statuskarte ist die Taste zum Inventar
		_cargoTap = At(status, new KitButton("", new Vector2(260, 140)) { Flat = true }, 0, 0);
		_cargoTap.Pressed += () => CargoPressed?.Invoke();
		// Kasse in der Kopfleiste
		_credits = Text("", 19, UiKit.Orange, HorizontalAlignment.Right);
		_credits.ClipText = true; _credits.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;

		// ── Sektorchip oben Mitte mit Lage-, Fahndungs- und Brandzeile ──
		var chip = _chip = Win(this, 12, 14, 430, 42, new Color(0, 0, 0, 0), null, 20);   // UI-Umbau: in der Kopfleiste ohne eigenen Kasten
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
		_mission = At(this, Text("", 13, MissionGold, HorizontalAlignment.Center), 420, 118, 430, 18);   // Runde 41: Auftragszeile
		_route = At(this, Text("", 13, UiKit.Cyan, HorizontalAlignment.Center), 420, 138, 430, 18);   // Mehrpunkt-Route
		foreach (var l in new[] { _security, _wanted, _fire, _mission, _route }) { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }

		// ── oben rechts: Karte und Menü (Symboltasten), Zielkarte links daneben ──
		_mapBtn = At(this, new KitButton("", new Vector2(72, 64)), 1140, 8);
		_mapBtn.Symbol = g => { var c = new Vector2(36, 24); g.DrawArc(c, 13, 0, Mathf.Tau, 24, UiKit.White, 2f); g.DrawLine(c + new Vector2(-13, 0), c + new Vector2(13, 0), UiKit.White, 1.5f); g.DrawLine(c + new Vector2(0, -13), c + new Vector2(0, 13), UiKit.White, 1.5f);
			UiKit.DrawText(g, 10, ThemeDB.FallbackFont, new Vector2(2, 56), "Karte", 13, UiKit.Dim, HorizontalAlignment.Center, 68); };
		_mapBtn.Pressed += () => MapPressed?.Invoke();
		_hamburger = At(this, new KitButton("", new Vector2(72, 64)), 1208, 8);
		_hamburger.Symbol = g => { g.DrawRect(new Rect2(26, 12, 7, 26), UiKit.White); g.DrawRect(new Rect2(39, 12, 7, 26), UiKit.White);
			UiKit.DrawText(g, 10, ThemeDB.FallbackFont, new Vector2(2, 56), "Pause", 13, UiKit.Dim, HorizontalAlignment.Center, 68); };   // UI-Umbau: Pause-Menü
		_hamburger.Pressed += () => HamburgerPressed?.Invoke();
		// UI-Umbau: Kopfleiste — Nachrichten (Zähler), Befehle (Kommandozentrale), Zeitraffer; Kasse links davon
		_commsBtn = At(this, new KitButton("", new Vector2(72, 64)), 900, 3);
		_commsBtn.Symbol = g => { var r = new Rect2(22, 12, 28, 20); g.DrawRect(r, UiKit.White, false, 2.5f); g.DrawLine(r.Position, r.Position + new Vector2(14, 11), UiKit.White, 2f); g.DrawLine(r.Position + new Vector2(28, 0), r.Position + new Vector2(14, 11), UiKit.White, 2f);
			if (_menuBadge > 0) { g.DrawCircle(new Vector2(54, 12), 10, UiKit.Orange); UiKit.DrawText(g, 11, ThemeDB.FallbackFont, new Vector2(44, 17), _menuBadge.ToString(), 13, new Color(0.1f, 0.05f, 0f), HorizontalAlignment.Center, 20); }
			UiKit.DrawText(g, 10, ThemeDB.FallbackFont, new Vector2(2, 56), "Nachr.", 13, UiKit.Dim, HorizontalAlignment.Center, 68); };
		_commsBtn.Pressed += () => CommsPressed?.Invoke();
		_commandBtn = At(this, new KitButton("", new Vector2(72, 64)), 980, 3);
		_commandBtn.Symbol = g => { g.DrawColoredPolygon(new[] { new Vector2(36, 8), new Vector2(52, 22), new Vector2(20, 22) }, UiKit.White); g.DrawRect(new Rect2(24, 22, 24, 14), UiKit.White, false, 2.5f);
			UiKit.DrawText(g, 10, ThemeDB.FallbackFont, new Vector2(2, 56), "Befehle", 13, UiKit.Dim, HorizontalAlignment.Center, 68); };
		_commandBtn.Pressed += () => CommandPressed?.Invoke();
		_fastBtn = At(this, new KitButton("", new Vector2(72, 64)), 1060, 3);
		_fastBtn.Symbol = g => { UiKit.DrawText(g, 12, ThemeDB.FallbackFont, new Vector2(2, 34), _fastOn ? "×10" : "×1", 22, _fastOn ? UiKit.Green : UiKit.White, HorizontalAlignment.Center, 68);
			UiKit.DrawText(g, 10, ThemeDB.FallbackFont, new Vector2(2, 56), "Zeit", 13, UiKit.Dim, HorizontalAlignment.Center, 68); };
		_fastBtn.Pressed += () => FastPressed?.Invoke();
		_credits = At(this, _credits, 700, 14, 170, 42);
		_credits.VerticalAlignment = VerticalAlignment.Center;
		// UI-Umbau: „☰ Sektor“ unten links — öffnet die Scanliste
		_scanBtn = At(this, new KitButton("", new Vector2(84, 68)), 10, 642);
		_scanBtn.Symbol = g => { foreach (var yy in new[] { 12f, 20f, 28f }) g.DrawRect(new Rect2(28, yy, 28, 4), UiKit.White);
			if (_scanHostile > 0) { g.DrawCircle(new Vector2(70, 12), 10, UiKit.Red); UiKit.DrawText(g, 11, ThemeDB.FallbackFont, new Vector2(60, 17), _scanHostile.ToString(), 13, UiKit.White, HorizontalAlignment.Center, 20); }
			UiKit.DrawText(g, 10, ThemeDB.FallbackFont, new Vector2(2, 58), _scanOpen || _shelfOpen || _hailOpen ? "Schließen" : "Menü", 14, UiKit.White, HorizontalAlignment.Center, 80); };
		// Nutzerwunsch: „☰“ unten links ist ein Regal — ein Tipp klappt darüber Scan, Nachrichten, Karte und Kamera auf
		_scanBtn.Pressed += () =>
		{
			if (_scanOpen || _camOpen || _hailOpen) { SetScanOpen(false); SetCamOpen(false); SetHailOpen(false); SetShelfOpen(false); return; }
			SetShelfOpen(!_shelfOpen);
		};
		foreach (var (key, label) in new[] { ("scan", "Scan"), ("comms", "Nachrichten"), ("map", "Karte"), ("cam", "Kamera") })
		{
			var b = new KitButton(label, new Vector2(170, 60)) { FontSize = 18, AlignLeft = true, Visible = false };
			string k = key;
			b.Pressed += () => ShelfPressed(k);
			AddChild(b);
			_shelf[key] = b;
		}
		_camPanel = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
		var cbg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
		cbg.AddThemeStyleboxOverride("panel", Box(new Color(0.03f, 0.06f, 0.12f, 0.85f), new Color(UiKit.Cyan, 0.6f), 12, 2, 0));
		_camPanel.AddChild(cbg); cbg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		At(_camPanel, Text("Kamera", 18, UiKit.Cyan), 14, 8, 200, 26);
		float cy = 40;
		foreach (var row in new[] { new[] { ("cam_free", "Frei"), ("cam_lock", "Zielfolge"), ("cam_chase", "Rückansicht") }, new[] { ("zoom_out", "Weiter weg"), ("zoom_in", "Näher"), ("cam_reset", "Zurücksetzen") } })
		{
			float cx = 12;
			foreach (var (key, label) in row)
			{
				var b = At(_camPanel, new KitButton(label, new Vector2(126, 60)) { FontSize = 15 }, cx, cy);
				string k = key;
				b.Pressed += () => CameraAction?.Invoke(k);
				_camButtons[key] = b;
				cx += 132;
			}
			cy += 66;
		}
		_camPanel.Size = new Vector2(12 + 3 * 132, cy + 6);
		AddChild(_camPanel);
		_scanPanel = new ScanPanel();
		AddChild(_scanPanel);
		_hailPanel = new HailPanel();   // Funk Stufe 1: an der Stelle der Scanliste
		AddChild(_hailPanel);

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
		// Ausbau Punkt 1: „Verband greift an“ rechts in der Zeile der Kontexttaste (die Kontexttaste rückt dann auf die linke Hälfte)
		_wingAttack = At(this, _attackConfirm.Wire(new KitButton("", new Vector2(161, 64)) { FontSize = 16, State = KitButtonState.Warn }, "wing_attack", "Verband greift an",
			() => WingAttackPressed?.Invoke()), 969, 206);
		_wingAttack.Visible = false;

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
		_slider.UserChanged += _ => _sliderTouched = true;
		_turnL = At(this, new KitButton("◀", new Vector2(104, 100)) { FontSize = 44 }, 112, 608);
		_turnR = At(this, new KitButton("▶", new Vector2(104, 100)) { FontSize = 44 }, 224, 608);
		_reverseLabel = At(this, Text("RÜCK · max 20 %", 12, UiKit.Dim, HorizontalAlignment.Center), 332, 612, 112, 18);
		_reverse = At(this, new KitButton("RÜCK", new Vector2(96, 72)) { FontSize = 18, Sub = "max 20 %" }, 336, 636);

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
		foreach (var l in new[] { _security, _wanted, _hitWarning, _escape, _mission, _route }) l.AddThemeStyleboxOverride("normal", UiKit.Box(new Color(0.02f, 0.04f, 0.09f, 0.82f), null, 8, 0, 2));
		_items = new()
		{
			[HudItem.Status] = _status, [HudItem.Chip] = _chip, [HudItem.Security] = _security, [HudItem.Wanted] = _wanted, [HudItem.Fire] = _fire, [HudItem.Mission] = _mission, [HudItem.Route] = _route,
			[HudItem.Map] = _mapBtn, [HudItem.Hamburger] = _hamburger, [HudItem.Card] = _card,
			[HudItem.Autopilot] = _autopilotBtn, [HudItem.AutoMine] = _autoMineBtn, [HudItem.Context] = _contextBtn,
			[HudItem.Power] = _power, [HudItem.NextEnemy] = _nextEnemy, [HudItem.AimAssist] = _aimAssist, [HudItem.AutoFire] = _autoFire, [HudItem.Surrender] = _surrender,
			[HudItem.Slider] = _slider, [HudItem.TurnLeft] = _turnL, [HudItem.TurnRight] = _turnR, [HudItem.Reverse] = _reverse, [HudItem.ReverseLabel] = _reverseLabel!,
			[HudItem.FireBtn] = _allFire, [HudItem.Expand] = _expand, [HudItem.HitWarning] = _hitWarning, [HudItem.Escape] = _escape,
			[HudItem.TopBar] = _topBar, [HudItem.Credits] = _credits, [HudItem.Comms] = _commsBtn, [HudItem.Command] = _commandBtn, [HudItem.Fast] = _fastBtn,
			[HudItem.Scan] = _scanBtn, [HudItem.ScanPanel] = _scanPanel,
		};
		for (int i = 0; i < _groupButtons.Count; i++) _items[HudItem.Group0 + i] = _groupButtons[i];
		for (int i = 0; i < _bayButtons.Count; i++) _items[HudItem.Bay0 + i] = _bayButtons[i];
		foreach (var (item, c) in _items) c.Name = item.ToString();   // Eingabe-Protokoll: lesbare Knotennamen („Hamburger“ statt „@Control@150“)
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

	Control _status = null!, _chip = null!, _chipRow = null!, _topBar = null!;
	KitButton _commsBtn = null!, _commandBtn = null!, _fastBtn = null!, _scanBtn = null!, _cargoTap = null!;
	CargoBar _cargoBar = null!;
	Label _cargoVal = null!, _cargoText = null!;
	ScanPanel _scanPanel = null!;
	bool _scanOpen;
	int _scanHostile;
	/// <summary>UI-Umbau: Scanliste („☰ Sektor“) offen? Solange offen, deckt sie Status und Lagezeilen.</summary>
	public bool ScanOpen => _scanOpen;
	readonly Dictionary<string, KitButton> _shelf = new();
	readonly Dictionary<string, KitButton> _camButtons = new();
	Control _camPanel = null!;
	bool _shelfOpen, _camOpen;
	/// <summary>Nutzerwunsch: Kameraoptionen aus dem Regal (Schlüssel wie im Pause-Menü: cam_free, cam_lock, cam_chase, zoom_in, zoom_out, cam_reset).</summary>
	public event Action<string>? CameraAction;
	public bool ShelfOpen => _shelfOpen;
	public bool CamOpen => _camOpen;
	/// <summary>Kamera-Modus fürs Regal (aktive Taste grün).</summary>
	public void SetCameraMode(SpaceSim.Core.Flight.CameraMode m)
	{
		if (_camButtons.Count == 0) return;
		_camButtons["cam_free"].State = m == SpaceSim.Core.Flight.CameraMode.Free ? KitButtonState.On : KitButtonState.Normal;
		_camButtons["cam_lock"].State = m == SpaceSim.Core.Flight.CameraMode.TargetLock ? KitButtonState.On : KitButtonState.Normal;
		_camButtons["cam_chase"].State = m == SpaceSim.Core.Flight.CameraMode.Chase ? KitButtonState.On : KitButtonState.Normal;
	}
	void ShelfPressed(string k)
	{
		switch (k)
		{
			case "scan": SetShelfOpen(false); SetScanOpen(true); ScanPressed?.Invoke(); break;
			case "comms": SetShelfOpen(false); CommsPressed?.Invoke(); break;
			case "map": SetShelfOpen(false); MapPressed?.Invoke(); break;
			case "cam": SetCamOpen(!_camOpen); break;
		}
	}
	public void SetShelfOpen(bool open)
	{
		_shelfOpen = open;
		if (!open) SetCamOpen(false);
		LayoutShelf();
		_scanBtn?.QueueRedraw();
		if (_scanBtn is not null) _scanBtn.State = open || _scanOpen ? KitButtonState.On : KitButtonState.Normal;
	}
	public void SetCamOpen(bool open) { _camOpen = open; if (_shelf.TryGetValue("cam", out var b)) b.State = open ? KitButtonState.On : KitButtonState.Normal; LayoutShelf(); }
	/// <summary>Regal über „☰“: Tasten von unten nach oben, Kamerafeld rechts neben dem Regal.</summary>
	void LayoutShelf()
	{
		if (_scanBtn is null || _shelf.Count == 0) return;
		var r = new Rect2(_scanBtn.Position, _scanBtn.Size);
		float y = r.Position.Y - 8f;
		foreach (var key in new[] { "scan", "comms", "map", "cam" })
		{
			var b = _shelf[key];
			y -= 60f;
			b.Position = new Vector2(r.Position.X, y);
			b.Visible = _shelfOpen;
			y -= 6f;
		}
		var cam = _shelf["cam"];
		_camPanel.Position = new Vector2(cam.Position.X + 178f, MathF.Max(80f, cam.Position.Y));
		_camPanel.Visible = _shelfOpen && _camOpen;
	}
	public KitButton? TestShelf(string key) => _shelf.TryGetValue(key, out var b) ? b : null;
	public KitButton? TestCamButton(string key) => _camButtons.TryGetValue(key, out var b) ? b : null;
	public ScanPanel Scan => _scanPanel;
	HailPanel _hailPanel = null!;
	bool _hailOpen;
	public HailPanel Hail => _hailPanel;
	public bool HailOpen => _hailOpen;
	/// <summary>Funk Stufe 1: Gesprächsfenster an der Stelle der Scanliste (schließt die Scanliste).</summary>
	public void SetHailOpen(bool open)
	{
		_hailOpen = open;
		if (_hailPanel is null) return;
		if (open) { _hailPanel.Invalidate(); if (_scanOpen) SetScanOpen(false); _shelfOpen = false; LayoutShelf(); }
		_hailPanel.Visible = open;
		_status.Visible = !open && !_scanOpen;
		ApplyHidden();
	}
	public void SetScanOpen(bool open)
	{
		_scanOpen = open;
		if (_scanPanel is null) return;
		if (open) { _scanPanel.Invalidate(); _shelfOpen = false; LayoutShelf(); }
		_scanPanel.Visible = open;
		_status.Visible = !open;
		_scanBtn.State = open ? KitButtonState.On : KitButtonState.Normal;
		_scanBtn.QueueRedraw();
		ApplyHidden();
	}
	/// <summary>Selbsttest/Prüfwerkzeug: Tasten der Kopfleiste und unten links.</summary>
	public KitButton TestCommsButton => _commsBtn;
	public KitButton TestCommandButton => _commandBtn;
	public KitButton TestFastButton => _fastBtn;
	public KitButton TestScanButton => _scanBtn;
	public KitButton TestCargoTap => _cargoTap;
	public string TestCargoText => _cargoText.Text;

	/// <summary>UI-Umbau: Frachtleiste — Farbstücke je Ware, Rest dunkel.</summary>
	public sealed partial class CargoBar : Control
	{
		public List<(Color Color, float Share)> Segments = new();
		public CargoBar() { MouseFilter = MouseFilterEnum.Ignore; }
		public override void _Draw()
		{
			DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.07f, 0.1f, 0.17f));
			float x = 0f;
			foreach (var (c, s) in Segments)
			{
				float w = Mathf.Clamp(s, 0f, 1f) * Size.X;
				if (x + w > Size.X) w = Size.X - x;
				if (w <= 0f) continue;
				DrawRect(new Rect2(x, 0, w, Size.Y), c);
				x += w;
			}
		}
	}
	Dictionary<HudItem, Control> _items = new();
	KitButton _power = null!, _expand = null!;
	readonly List<KitButton> _groupButtons = new();
	bool _expanded;
	int _layoutGroups = -1, _layoutBays = -1;
	bool _layoutFire = true;
	bool _layoutHeat;
	/// <summary>HUD-Neu: Gruppenknopf gedrückt (Index in <see cref="FlightHudData.Groups"/>).</summary>
	public event Action<int>? GroupDown;
	/// <summary>Selbsttest: Einzelschächte aufgeklappt, Gruppentexte, Lage einer Gruppe/des FEUER-Knopfs.</summary>
	public bool TestExpanded { get => _expanded; set { _expanded = value; _expand.On = value; LayoutForData(force: true); } }
	public List<string> TestGroupLabels => _groupButtons.Where(b => b.Visible).Select(b => b.Text).ToList();
	public Rect2 TestGroupRect(int i) => _groupButtons[i].GetGlobalRect();
	public bool TestGroupDisabled(int i) => _groupButtons[i].State == KitButtonState.Disabled;
	public string TestGroupSub(int i) => _groupButtons[i].Sub;
	/// <summary>Sprungantrieb/Tarnfeld (Selbsttest): erster abgeschnittene Text einer sichtbaren Gruppentaste ("" = alles passt).</summary>
	public string TestGroupOverflow => _groupButtons.Where(b => b.Visible).Select(b => b.Overflow()).FirstOrDefault(o => o.Length > 0) ?? "";
	public KitButtonState TestGroupState(int i) => _groupButtons[i].State;
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
	public Vector2 TestSliderPoint(float v) => _slider.GetGlobalTransform() * _slider.PointOf(v);   // UI-Umbau: waagerecht
	/// <summary>Geräte-Eingabe (Selbsttest 131): derselbe Punkt in Fensterpunkten (= Gerätepunkte am Handy).</summary>
	public Vector2 TestSliderScreenPoint(float v) => _slider.GetScreenTransform() * _slider.PointOf(v);
	/// <summary>Geräte-Eingabe (Selbsttest 131): Steuerelement einer HUD-Zone.</summary>
	public Control? TestItem(HudItem item) => _items.TryGetValue(item, out var c) ? c : null;

	public override void _Notification(int what)
	{
		if (what == NotificationResized && _items.Count > 0) Relayout();
	}

	SpaceSim.Core.Ui.UiInsets _laidInsets;
	public void ApplyScale(Vector2 viewport, float factor)
	{
		_wantFactor = factor; _viewport = viewport;
		// nie zwei Tasten übereinander — der Faktor sinkt (Core: FitFactor), bis Gruppen, FEUER und die aufgeklappten Schächte ihren Platz haben
		factor = FlightHudLayout.FitFactor(viewport.X, viewport.Y, SafeInsets1(), Math.Max(0, _layoutGroups), Math.Max(0, _layoutBays), factor, _layoutHeat);
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
		var layout = FlightHudLayout.Compute(Size.X, Size.Y, SafeInsets(), Math.Max(0, _layoutGroups), Math.Max(0, _layoutBays), _expanded, _layoutFire, _layoutHeat);
		Layout = layout;
		foreach (var (item, c) in _items)
		{
			var r = layout[item];
			c.Position = new Vector2(r.X, r.Y);
			// UI-Umbau: jede Zone bekommt ihre Größe aus der Anordnung (Randzellen 76 × 66, Kopfleiste über die ganze Breite)
			if (r.W > 0f && r.H > 0f && item is not (HudItem.Status or HudItem.Card)) { c.CustomMinimumSize = new Vector2(r.W, r.H); c.Size = new Vector2(r.W, r.H); }
		}
		// Hitze: Statusfeld und seine Taste (Inventar) folgen der Höhe aus der Anordnung
		var stR = layout[HudItem.Status];
		if (stR.H > 0f) { _status.Size = new Vector2(_status.Size.X, stR.H); _cargoTap.Size = new Vector2(_cargoTap.Size.X, stR.H); _cargoTap.CustomMinimumSize = new Vector2(_cargoTap.CustomMinimumSize.X, stR.H); }
		_heatRow.Visible = _layoutHeat;
		_chipRow.CustomMinimumSize = Vector2.Zero;
		_chipRow.Size = new Vector2(MathF.Max(0f, layout[HudItem.Chip].W - 28f), 42f);
		_gateAttack.Position = _stationAttack.Position = new Vector2(layout[HudItem.Attack].X, layout[HudItem.Attack].Y);
		var sp = layout[HudItem.ScanPanel];
		_hailPanel.Position = new Vector2(sp.X, sp.Y); _hailPanel.Size = new Vector2(sp.W, sp.H);
		ApplyHidden();
	}

	/// <summary>Ausblendungen der Anordnung (verdeckte Autopilot-Zeile bei aufgeklappter Leiste, leere Gruppen-/Schachtplätze).</summary>
	void ApplyHidden()
	{
		if (Layout is not { } l) return;
		foreach (var it in new[] { HudItem.Autopilot, HudItem.AutoMine }) _items[it].Visible = !l.Hidden(it);
		if (_reverseLabel is not null) _reverseLabel.Visible = !l.Hidden(HudItem.ReverseLabel);
		_mapBtn.Visible = !l.Hidden(HudItem.Map); _commsBtn.Visible = !l.Hidden(HudItem.Comms);   // Nutzerwunsch: im Regal unten links
		LayoutShelf();
		if (_scanOpen || _hailOpen) foreach (var lab in new Control[] { _status, _security, _wanted, _fire, _mission, _route }) lab.Visible = false;   // UI-Umbau: die Scanliste deckt die linke Spalte
		foreach (var it in new[] { HudItem.HitWarning, HudItem.Escape }) if (l.Hidden(it)) _items[it].Visible = false;
		if (l.Hidden(HudItem.Context)) { _contextBtn.Visible = false; _wingAttack.Visible = false; }
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
		bool heat = _d.HeatActive;
		if (!force && groups == _layoutGroups && bays == _layoutBays && fire == _layoutFire && heat == _layoutHeat) return;
		_layoutGroups = groups; _layoutBays = bays; _layoutFire = fire; _layoutHeat = heat;
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
		_fuelLowShown = d.FuelLow || fuelPct < 20f;
		_fuelBar.Set(fuelPct, _fuelLowShown ? UiKit.Red : UiKit.Orange);
		_fuelVal.Text = d.FuelCapacity > 0f ? $"{(int)d.Fuel}" : "–";
		_fuelRow.Visible = d.FuelActive;
		_fuelName.Text = d.FuelLabel;
		_fuelInfo.Text = d.FuelInfo;
		_fuelInfo.Modulate = _fuelLowShown ? new Color(1f, 0.45f, 0.4f) : Colors.White;
		_fuelInfo.Visible = d.FuelActive && d.FuelInfo.Length > 0;
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
		// UI-Umbau: Fracht im Status, Zähler in Kopfleiste und an „☰ Sektor“
		_cargoBar.Segments = d.CargoSegments; _cargoBar.QueueRedraw();
		_cargoVal.Text = d.CargoCap > 0f ? $"{d.CargoUsed:0}/{d.CargoCap:0}" : "–";
		_cargoText.Text = d.CargoText.Length > 0 ? d.CargoText + " ▸" : "leer ▸";
		if (_menuBadge != d.CommsUnread) { _menuBadge = d.CommsUnread; _commsBtn.QueueRedraw(); }
		if (_shelf.Count > 0)
		{
			_shelf["scan"].Sub = d.ScanHostile > 0 ? $"{d.ScanCount} Objekte · {d.ScanHostile} feindlich" : $"{d.ScanCount} Objekte";
			_shelf["comms"].Sub = d.CommsUnread > 0 ? $"{d.CommsUnread} ungelesen" : "Posteingang";
			_shelf["map"].Sub = "Sternenkarte";
			_shelf["cam"].Sub = "Ansicht · Zoom";
		}
		if (_scanHostile != d.ScanHostile) { _scanHostile = d.ScanHostile; _scanBtn.QueueRedraw(); }
		if (_fastOn != d.FastOn) _fastBtn.QueueRedraw();
		// Hitze: Balken (orange ab „langsamer“, rot gesperrt/kritisch); die Warnzeile teilt sich die rote Brandzeile (Brand geht vor)
		bool hot = d.HeatLocked || d.HeatShare >= 0.999f;
		_heatBar.Set(d.HeatShare * 100f, hot ? UiKit.Red : d.HeatShare >= 0.7f ? UiKit.Orange : new Color(0.95f, 0.75f, 0.35f));
		_heatVal.Text = d.HeatActive ? $"{d.HeatShare * 100f:0} %" : "";
		_heatVal.AddThemeColorOverride("font_color", hot ? UiKit.Red : new Color(UiKit.Dim, 0.7f));
		string fireLine = d.Fire.Length > 0 ? d.Fire : d.HeatWarning;
		_fire.Text = fireLine;
		_fire.Visible = fireLine.Length > 0;
		// Runde 41: Auftragszeile (Gold); Runde 42d (Handytest): nach gemessener Breite gekürzt — Ziel kürzen,
		// Restzeit („· noch m:ss“) bleibt immer sichtbar (sonst „…· noch…“ abgeschnitten)
		_mission.Text = FitMissionLine(d.Mission, _mission);
		_mission.Visible = d.Mission.Length > 0;
		_route.Text = FitRouteLine(d.Route, _route);
		_route.Visible = d.Route.Length > 0;
		// HUD-Neu: der Zeitraffer steht im Menü — läuft er, zeigt der Sektorchip „×10“
		_sector.Text = d.SectorName;   // UI-Umbau: Zeitraffer zeigt die Taste „Zeit“ in der Kopfleiste
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

		_tName.Text = d.TargetNameOptions is { Count: > 0 } tno ? FitTargetName(d.TargetPrefix, tno) : FitTargetName(d.TargetName);
		_tName.AddThemeColorOverride("font_color", d.TargetHasInfo ? UiKit.Green : UiKit.Dim);
		_tInfo.Text = d.TargetInfo;
		_tArc.Text = d.TargetArc;
		_radar.RelBearing = d.TargetRelBearing;
		_radar.HasTarget = d.TargetHasInfo && d.TargetDistance >= 0f;
		_radar.Distance = d.TargetDistance;

		_contextBtn.Visible = d.ContextLabel.Length > 0 || d.ContextVerb.Length > 0;
		if (d.ContextVerb.Length > 0 && d.ContextNameOptions is { Count: > 0 } cno) SetContext(d.ContextVerb, cno);
		else { _contextBtn.Text = d.ContextLabel; _contextBtn.Sub = ""; }
		// Ausbau Punkt 1: „Verband greift an“ neben der Kontexttaste
		bool wing = d.WingAttack.Length > 0;
		_wingAttack.Visible = wing;
		if (wing && _wingAttack.Text != "Wirklich?") _wingAttack.Sub = d.WingAttack;
		if (Layout is { } wl)
		{
			var cr = wl[HudItem.Context];
			float cw = wing ? MathF.Floor((cr.W - 8f) / 2f) : cr.W;
			if (MathF.Abs(_contextBtn.Size.X - cw) > 0.5f) { _contextBtn.CustomMinimumSize = new Vector2(cw, cr.H); _contextBtn.Size = new Vector2(cw, cr.H); }
			_wingAttack.Position = new Vector2(cr.X + cr.W - cw, cr.Y);
			_wingAttack.CustomMinimumSize = new Vector2(cw, cr.H); _wingAttack.Size = new Vector2(cw, cr.H);
		}

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
	}

	/// <summary>HUD-Neu: Begründungen, die nicht mehr auf der kleinen Taste stehen (Selbsttest).</summary>
	public string TestGateAttackInfo { get; private set; } = "";
	public string TestSurrenderReason { get; private set; } = "";

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
