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
				_m.SyncHudForTest();
				Check(_m.HudRef.Flight.TestAutoFireHint, "Warnphase: „Dauerfeuer“ pulsiert");
				pe.DueTick = u.Tick;   // Überfall jetzt
				return false;
			}
			case 260:
			{
				Check(u.Fights.Of(u.PlayerShip) is not null, "Kampf läuft");
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
				return false;
			case 430:
			{
				// Tor antippen wählt nur aus
				var gate = u.Sectors[u.PlayerSector].Gates.Select(id => u.Gates[id]).OrderBy(g => g.Id.Value).First();
				_m.TestSelectGate(gate.Id);
				Check(!_m.TestAutopilot, "Tor antippen startet keinen Autopiloten");
				_m.SyncHudForTest();
				Check(_m.HudRef.Flight.TestTargetName.Contains("Tor") || _m.HudRef.Flight.TestTargetName.Contains("Gate"), $"Zielkarte zeigt das Tor ({_m.HudRef.Flight.TestTargetName})");
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
				Check(OnScreen(hw), $"Stationsangriff: HQ im Bild ({_m.TestCam.UnprojectPosition(hw)}; beide {_m.TestFramingBoth}; {_m.TestFrameDebug}; HQ {hw})");
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
