using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 177 (Welle F): neues Spiel (Händler), Sektorbehörde in Nebelmark → „Meine Fraktion“ zeigt „Reich je Sektor“ mit Steuer gegen Kosten;
/// „Ruhen lassen“ mit zwei echten Tipps → die Sektorzeile verliert die Behördenkosten (sichtbare Änderung). Dazu: Wartemeldung eines gestrandeten
/// Flottenschiffs erscheint als Meldung (höchstens einmal je Spieltag und Schiff) und die Regeln der Welle F sind in der Standardwelt an.
/// </summary>
public partial class SelfTest
{
	Vector2 _wfAt;
	StationId _wfSeat;
	string _wfLineBefore = "";

	string WfNebelmarkLine() => _m.HudRef.CcMyFaction.TestEmpireLines.FirstOrDefault(l => l.Contains("Nebelmark")) ?? "";

	bool WelleFStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var neb = u.Sectors.Values.First(s => s.Name.Contains("Nebelmark")).Id;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
				Check(u.Rules.PlayerFuelPriority && u.Rules.WarBrokeTicks > 0 && u.Rules.PlayerGovernmentMaintenanceShare > 0f, "WF: Welle-F-Regeln in der Standardwelt an");
				u.Player.Treasury.Credits = 2_000_000f;
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				_m.TestEnterSector(neb);
				return false;
			case 12:
			{
				foreach (var c in new[] { new Vec2(0, 0), new(200, 200), new(-200, 200), new(200, -200), new(-200, -200), new(400, 0), new(-400, 0), new(0, 400), new(0, -400) })
				{
					_m.TestTeleportTo(c);
					if (PlayerSeats.FoundIssue(u, neb) is null) break;
				}
				Check(PlayerSeats.FoundIssue(u, neb) is null, $"WF: Behörde in Nebelmark möglich ({PlayerSeats.FoundIssue(u, neb)})");
				_wfSeat = PlayerSeats.Found(u, neb).Id;
				return false;
			}
			case 20:
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.MyFaction);
				hud.Screens.Current?.Refresh();
				return false;
			case 26:
			{
				hud.Screens.Current?.Refresh();
				_wfLineBefore = WfNebelmarkLine();
				Check(_wfLineBefore.Contains("Steuer") && _wfLineBefore.Contains("Behörden") && !_wfLineBefore.Contains("Behörden 0,"),
					$"WF: „Reich je Sektor“ zeigt Nebelmark mit Behördenkosten ({_wfLineBefore})");
				Check(hud.CcMyFaction.TestEmpireLines.Any(l => l.StartsWith("Summe")), "WF: Summenzeile vorhanden");
				return false;
			}
			case >= 30 and <= 46: return TapTwice(f, 30, "Ruhen lassen", ref _wfAt);
			case 52:
			{
				hud.Screens.Current?.Refresh();
				string after = WfNebelmarkLine();
				Check(u.Stations[_wfSeat].Dormant, $"WF: zwei Tipps lassen die Behörde ruhen ({hud.CcMyFaction.TestPolicyMessage})");
				Check(after.Contains("Behörden 0,") && after != _wfLineBefore, $"WF: Sektorzeile ohne Behördenkosten nach dem Ruhen ({after})");
				// gestrandetes Flottenschiff: Hinweis einmal, Wiederholung im selben Spieltag still
				var ship = u.Ships[u.PlayerShip];
				u.Events.Publish(new FleetShipStranded(ship.Id, "fuel", 30f, 1920f));
				Check(_m.TestStrandedText.Contains("1920") && hud.TestToastText.Contains("1920"), $"WF: Wartemeldung als Meldung ({hud.TestToastText})");
				u.Events.Publish(new FleetShipStranded(ship.Id, "fuel", 31f, 1984f));
				Check(!hud.TestToastText.Contains("1984") && !_m.TestStrandedText.Contains("1984"), "WF: dieselbe Meldung höchstens einmal je Spieltag und Schiff");
				_m.TestCloseCommand();
				return true;
			}
		}
		return false;
	}
}
