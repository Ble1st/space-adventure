using System.Linq;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 149 (Ausbau Punkt 8, Stationsverschleiß): neues Spiel; eine Station im Spielersektor auf 55 % Zustand gesetzt → Stationsinfo zeigt
/// „Zustand 55 %“ und „Produktion 78 % …“ ohne Text über Text oder Beschnitt; Sektoransicht der Kommandozentrale zeigt die Zustandszeile; bei 20 % „Stillstand“.
/// </summary>
public partial class SelfTest
{
	StationId _wearSt;

	bool WearStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				Check(u.Rules.StationWear, "Verschleiß: Regel in der Standardwelt an");
				var st = u.StationsById().First(s => !s.IsBuildSite && s.Dockable && s.Production.Count > 0 && Maintenance.Applies(u, s));
				_wearSt = st.Id;
				st.Integrity = 0.55f;
				_m.TestDockAt(st.Id);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
				return false;
			}
			case 9:
			{
				var line = hud.DockInfo.TestWear;
				Check(line.StartsWith("Zustand 55 %", System.StringComparison.Ordinal) && line.Contains("Produktion 79 %"), $"Verschleiß: Stationsinfo zeigt Zustand und Wirkung ({line})");
				DrawnText.Enabled = true;
				if (hud.Screens.Current is Godot.Control sc) { sc.QueueRedraw(); foreach (var n in UiCheck.Visible(sc)) if (n is Godot.Control cc) cc.QueueRedraw(); }
				return false;
			}
			case 11:
			{
				if (hud.Screens.Current is Godot.Control sc)
				{
					var scope = UiCheck.Visible(sc).ToList();
					var inter = scope.Where(c => UiCheck.Interactive(c) && c.Size.X > 1f && c.Size.Y > 1f && c.MouseFilter != Godot.Control.MouseFilterEnum.Ignore).ToList();
					var notes = new System.Collections.Generic.List<string>();
					var tc = UiCheck.TextChecks(scope, inter, notes);
					Check(tc.TextOverlaps == 0 && tc.Clipped == 0, $"Verschleiß: Stationsinfo Text über Text/Taste {tc.TextOverlaps}, beschnitten {tc.Clipped} [{string.Join("; ", notes.Take(4))}]");
				}
				DrawnText.Enabled = false;
				_m.TestUndock();
				u.Stations[_wearSt].Integrity = 0.2f;
				_m.TestOpenCommand();
				_m.TestOpenCcArea("sector");
				_m.TestCcSelectSectorView(u.Stations[_wearSt].Sector);
				hud.CcSector.TestSelectStation(_wearSt);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 13:
			{
				var line = hud.CcSector.TestWearLine;
				Check(line.StartsWith("Zustand 20 %", System.StringComparison.Ordinal) && line.Contains("Stillstand"), $"Verschleiß: Sektoransicht zeigt den Zustand ({line})");
				Check(StationWear.ProductionFactor(u, u.Stations[_wearSt]) == 0f, "Verschleiß: unter 30 % steht die Produktion");
				u.Stations[_wearSt].Integrity = 1f;
				_m.TestCloseCommand();
				return true;
			}
		}
		return false;
	}
}
