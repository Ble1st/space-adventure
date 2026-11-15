using System.Linq;
using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe Runde 15 (eigene Datei) im Handyformat (Fenster 2340×1080): Gefechtskamera in der Warnphase (Schiff im freien Feld, Ziel im Bild oder
/// Randpfeil), im Kampf (beide im Feld, gut erkennbar), nach Kampfende (Schiff bleibt im Feld), beim Stationsangriff (Station mit im Bild); „Dauerfeuer“ pulsiert
/// beim Überfall; Tor antippen wählt nur aus; Zielkarte ohne abgeschnittene Pflichtzeilen; Annäherungsautomatik beim Stationsangriff.
/// </summary>
public partial class SelfTest
{
	ShipId _phLead;

	bool ShipInField(string what)
	{
		var size = GetViewport().GetVisibleRect().Size;
		var free = _m.FramingField();
		var sp = _m.TestCam.UnprojectPosition(_m.Player.Position);
		float nx = sp.X / size.X * 2f - 1f, ny = 1f - sp.Y / size.Y * 2f;
		bool ok = nx >= free.X0 - 0.06f && nx <= free.X1 + 0.06f && ny >= free.Y0 - 0.06f && ny <= free.Y1 + 0.06f;
		Check(ok, $"{what}: Schiff im freien Feld ({nx:0.00}, {ny:0.00}; Feld x {free.X0:0.00}…{free.X1:0.00}, y {free.Y0:0.00}…{free.Y1:0.00}; {size.X:0}×{size.Y:0})");
		return ok;
	}

	bool OnScreen(Vector3 w)
	{
		var size = GetViewport().GetVisibleRect().Size;
		if (_m.TestCam.IsPositionBehind(w)) return false;
		var sp = _m.TestCam.UnprojectPosition(w);
		return sp.X >= 0 && sp.X <= size.X && sp.Y >= 0 && sp.Y <= size.Y;
	}

	Vector2? _phPress;

	/// <summary>Runde 16: Taste wie am Gerät antippen (Berührung bzw. Maus über die Eingabe der Ansicht, nicht über die Ereignisse der Taste).</summary>
	bool PhoneTap(string label, bool down)
	{
		if (down) _phPress = FindKit(_m.HudRef.Flight, label)?.GetGlobalRect().GetCenter();
		if (_phPress is not { } at) return false;
		var vp = GetViewport();
		if (Ui.Pointer.TouchMode) vp.PushInput(new InputEventScreenTouch { Index = 0, Position = at, Pressed = down }, true);
		else vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = at, GlobalPosition = at, Pressed = down, ButtonMask = down ? MouseButtonMask.Left : 0 }, true);
		return true;
	}

	/// <summary>HUD-Neu: alte Beschriftungen der Flugtasten → neue Symboltasten (gleiche Funktion).</summary>
	internal static string HudAlias(string label) => label switch
	{
		"Nächster Gegner" => "⊕", "Dauerfeuer" => "↻", "Ziel folgen" => "◎", "Ergeben" => "⚑", _ => label,
	};

	static Ui.Kit.KitButton? FindKit(Node root, string label)
	{
		label = HudAlias(label);
		foreach (var c in root.GetChildren())
		{
			if (c is Ui.Kit.KitButton b && b.Text == label && b.IsVisibleInTree()) return b;
			if (FindKit(c, label) is { } x) return x;
		}
		return null;
	}

	/// <summary>Runde 16: Ziel sichtbar — im freien Feld auf dem Bild oder als Randpfeil am freien Feld.</summary>
	bool TargetVisible(Vector3 w)
	{
		var size = GetViewport().GetVisibleRect().Size;
		var free = _m.FramingField();
		if (!_m.TestCam.IsPositionBehind(w))
		{
			var sp = _m.TestCam.UnprojectPosition(w);
			float nx = sp.X / size.X * 2f - 1f, ny = 1f - sp.Y / size.Y * 2f;
			if (nx >= free.X0 - 0.06f && nx <= free.X1 + 0.06f && ny >= free.Y0 - 0.06f && ny <= free.Y1 + 0.06f) return true;
		}
		return _m.HudRef.TargetArrowShown;
	}

	/// <summary>Rückgabe true = Stufe fertig.</summary>
	bool PhoneStage(int f)
	{
		var u = _m.Session.U;
		switch (f)
		{
			case 1:
				GetWindow().Size = new Vector2I(2340, 1080);
				_m.TestMenu("new:combat");
				return false;
			case 90:
			{
				// Warnphase: Überfall angekündigt — Ziel gesetzt, Kamera rahmt, Dauerfeuer pulsiert
				var pe = u.PendingEncounters.FirstOrDefault(p => p.Target == u.PlayerShip && p.Kind == EncounterKind.Raid);
				Check(pe is not null, "Gefechtstest: Überfall angekündigt");
				if (pe is null) return true;
				_phLead = pe.Source;
				Check(_m.TestFight.Target == _phLead, "Warnphase: der Kaperer ist Ziel");
				Check(_m.TestFraming, $"Warnphase: Gefechtskamera aktiv ({_m.TestFramingDebug})");
				ShipInField("Warnphase");
				if (_m.TestFramingBoth && _m.TestView.ShipPosition(_phLead) is { } lp) Check(OnScreen(lp), "Warnphase: Kaperer im Bild");
				if (_m.TestView.ShipPosition(_phLead) is { } wp) Check(TargetVisible(wp), "Warnphase: Kaperer im freien Feld oder Randpfeil");
				// Runde 18: das eigene Schiff behält eine Mindestgröße (Kamera höchstens bis zur Kappung) und liegt nicht unter dem Warnkasten
				if (_m.HudRef.AlertRect is { } alr0)
					Check(!alr0.Intersects(_m.HudRef.Flight.ReverseLabelRect), $"Warnkasten überdeckt „RÜCK · max 20 %“ nicht (Handyformat: {alr0} / {_m.HudRef.Flight.ReverseLabelRect})");   // Runde 19
				Check(_m.TestCamDistance <= _m.TestWarningCap + 2f, $"Warnphase: Schiff groß genug (Abstand {_m.TestCamDistance:0} ≤ {_m.TestWarningCap:0})");
				if (_m.HudRef.AlertRect is { } alr && !_m.TestCam.IsPositionBehind(_m.Player.Position))
					Check(!alr.HasPoint(_m.TestCam.UnprojectPosition(_m.Player.Position)), "Warnphase: Schiff nicht unter dem Warnkasten");
				if (_m.HudRef.AlertRect is { } alr2 && _m.TestView.ShipPosition(_phLead) is { } lp2 && !_m.TestCam.IsPositionBehind(lp2))
					Check(!alr2.HasPoint(_m.TestCam.UnprojectPosition(lp2)), "Warnphase: Kaperer nicht unter dem Warnkasten");
				_m.SyncHudForTest();
				Check(_m.HudRef.Flight.TestAutoFireHint, "Warnphase: „Dauerfeuer“ pulsiert");
				pe.DueTick = u.Tick;   // Überfall jetzt
				return false;
			}
			// Runde 16: „Nächster Gegner“ und „Dauerfeuer“ wie am Gerät antippen — das Ziel bleibt der Angreifer (nicht der ferne Verstärkungskaperer)
			case 200: Check(PhoneTap("Nächster Gegner", true), "Taste „Nächster Gegner“ sichtbar"); return false;
			case 204: PhoneTap("", false); return false;
			case 210: Check(PhoneTap("Dauerfeuer", true), "Taste „Dauerfeuer“ sichtbar"); return false;
			case 214: PhoneTap("", false); return false;
			case 260:
			{
				Check(u.Fights.Of(u.PlayerShip) is not null, "Kampf läuft");
				Check(_m.TestFight.Target == _phLead, $"nach Tipp „Nächster Gegner“: Ziel bleibt der Angreifer ({_m.TestFight.Target.Value} statt {_phLead.Value})");
				Check(_m.TestFight.AutoFire, "nach Tipp „Dauerfeuer“: Dauerfeuer an");
				if (_m.TestView.ShipPosition(_phLead) is { } tp2) Check(TargetVisible(tp2), "Kampf: Ziel im freien Feld oder Randpfeil");
				Check(_m.TestFraming && _m.TestFramingBoth, $"Kampf: Schiff und Ziel gerahmt ({_m.TestFramingDebug})");
				ShipInField("Kampf");
				if (_m.TestView.ShipPosition(_phLead) is { } lp)
				{
					Check(OnScreen(lp), "Kampf: Kaperer im Bild");
					// gut erkennbar: der Abstand Schiff–Ziel auf dem Bild ist ein deutlicher Teil des freien Felds
					var size = GetViewport().GetVisibleRect().Size;
					var a = _m.TestCam.UnprojectPosition(_m.Player.Position); var b = _m.TestCam.UnprojectPosition(lp);
					var free = _m.FramingField();
					float span = Mathf.Max(Mathf.Abs(a.X - b.X) / (size.X * (free.X1 - free.X0) / 2f), Mathf.Abs(a.Y - b.Y) / (size.Y * (free.Y1 - free.Y0) / 2f));
					Check(span >= 0.3f, $"Kampf: beide gut erkennbar (Abstand füllt {span * 100f:0} % des Felds)");
				}
				_m.SyncHudForTest();
				Check(_m.HudRef.Flight.TestTargetLinesComplete, $"Zielkarte vollständig ({_m.HudRef.Flight.TestTargetCombat})");
				// Kampfende: alle Kaperer weg
				foreach (var fe in u.Fights.Encounters.ToList()) foreach (var p in fe.Participants.Where(x => x.Active).ToList()) Engage.Leave(u, fe, p, CombatExit.Fled);
				foreach (var r in u.ShipsById().Where(ThreatMap.IsRaider).Where(r => r.Sector == u.PlayerSector).ToList()) { if (r.Transit is not null) u.FinishTransit(r); u.MoveShip(r, new SectorId(11)); }
				u.PendingEncounters.Clear();
				_m.TestFight.SetTarget(default);
				return false;
			}
			case > 262 and < 420 when f % 15 == 0:
				ShipInField($"nach dem Kampf (Bild {f - 262})");
				return false;
			case 420:
				Check(!_m.TestFraming, "nach dem Kampf: Gefechtskamera aus");
				_m.TestMenu("new:war");
				_m.SyncHudForTest();
				Check(!_m.HudRef.Flight.TestGateAttackVisible, "nach „Neues Spiel“ keine Taste „Tor angreifen“");
				return false;
			case 430:
			{
				// Tor antippen wählt nur aus
				var gate = u.Sectors[u.PlayerSector].Gates.Select(id => u.Gates[id]).OrderBy(g => g.Id.Value).First();
				_m.TestSelectGate(gate.Id);
				Check(!_m.TestAutopilot, "Tor antippen startet keinen Autopiloten");
				_m.SyncHudForTest();
				Check(_m.HudRef.Flight.TestTargetName.Contains("Tor") || _m.HudRef.Flight.TestTargetName.Contains("Gate"), $"Zielkarte zeigt das Tor ({_m.HudRef.Flight.TestTargetName})");
				string tapped = _m.HudRef.Flight.TestTargetName, tappedCombat = _m.HudRef.Flight.TestTargetCombat;
				bool tappedAttack = _m.HudRef.Flight.TestGateAttackVisible;
				// Runde 16: dasselbe Tor über Kommandozentrale → Sektoransicht → „Ziel wählen“ ergibt dieselbe Zielkarte
				_m.TestSelectStation(u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta").Id);
				_m.TestOpenCommand(); _m.TestOpenCcArea("cc.sector");
				_m.HudRef.CcSector.TestSelectSector(u.PlayerSector);
				_m.HudRef.CcSector.TestSelectGate(gate.Id);
				_m.HudRef.CcSector.TestTargetHere();
				_m.SyncHudForTest();
				Check(!_m.TestScreenOpen("cc.sector") && _m.HudRef.Flight.TestTargetName == tapped && _m.HudRef.Flight.TestTargetCombat == tappedCombat
					&& _m.HudRef.Flight.TestGateAttackVisible == tappedAttack, $"Sektoransicht „Ziel wählen“ = Tor antippen ({_m.HudRef.Flight.TestTargetName} | {_m.HudRef.Flight.TestTargetCombat})");
				// außer Betrieb: Zielkarte vollständig (vorher „AUSSER BETRIEB · Wiederanlauf …“ abgeschnitten)
				SpaceSim.Core.Combat.GateCombat.Apply(u, gate, new SpaceSim.Core.Combat.HitResult(true, 1f, 0f, 0f, SpaceSim.Core.Combat.GateCombat.MaxHull(u, gate) + 10f), new ActorId(9999));
				_m.SyncHudForTest();
				Check(SpaceSim.Core.Combat.GateCombat.IsDown(u, gate) && _m.HudRef.Flight.TestTargetLinesComplete, $"Zielkarte Tor außer Betrieb vollständig ({_m.HudRef.Flight.TestTargetCombat}; {_m.HudRef.Flight.TestTargetLineOverflow})");
				SpaceSim.Core.Combat.GateCombat.Restore(u, gate, "repaired");
				// Stationsangriff: HQ wählen und angreifen — Station mit im Bild, Annäherungsautomatik, vollständige Zielkarte
				var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
				_m.TestSelectStation(hq.Id);
				_m.TestArmStation();
				Check(_m.TestFight.AtStation, "Stationsangriff scharf");
				Check(_m.TestAttackApproach is { } at && at > 0f, $"Annäherungsautomatik fliegt heran ({_m.TestAttackApproach})");
				return false;
			}
			case 560:
			{
				var hq = u.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
				Check(_m.TestFraming, $"Stationsangriff: Gefechtskamera aktiv ({_m.TestFramingDebug})");
				ShipInField("Stationsangriff");
				var hw = new Vector3(hq.Position.X, 0f, hq.Position.Y);
				Check(OnScreen(hw), $"Stationsangriff: HQ im Bild ({_m.TestCam.UnprojectPosition(hw)}; beide {_m.TestFramingBoth}; {_m.TestFrameDebug}; HQ {hw}; Feld {_m.FreeFieldPixels()}; Meldung {_m.HudRef.AlertRect} „{_m.HudRef.TestAlert}“; Bild {GetViewport().GetVisibleRect().Size}; belegt {string.Join(" ", _m.HudRef.Flight.OccupiedRects())})");
				_m.SyncHudForTest();
				Check(_m.HudRef.Flight.TestTargetLinesComplete, $"Zielkarte Stationsangriff vollständig ({_m.HudRef.Flight.TestTargetArc} | {_m.HudRef.Flight.TestTargetCombat})");
				_m.TestFight.AutoFire = false;
				_m.TestFight.ClearTarget();
				foreach (var fe in u.Fights.Encounters.ToList()) foreach (var p in fe.Participants.Where(x => x.Active).ToList()) Engage.Leave(u, fe, p, CombatExit.Fled);
				GetWindow().Size = new Vector2I(1280, 720);
				return true;
			}
		}
		return false;
	}
}
