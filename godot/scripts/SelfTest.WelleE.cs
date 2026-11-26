using System.Linq;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 176 (Welle E): neues Spiel (Jäger), Krieg zwischen zwei KI-Fraktionen, volle Kasse → Großkampfschiff-Auftrag an einer Werft; angedockt
/// zeigt die Stationsinfo „Bauauftrag: Leviathan für … — Material n %“, danach „Montage“; nach der Montage meldet der Toast das fertige Schiff, und das
/// NPC-Schiff steht als Marke im Sektor (in Sicht).
/// </summary>
public partial class SelfTest
{
	StationId _ceYard;
	ShipId _ceShip;
	CapitalOrder? _ceOrder;

	bool WelleEStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:fighter"); return false;
			case 4:
			{
				_m.Session.RunTicks(2);
				if (_m.TestDocked) _m.TestUndock();
				Check(u.Rules.CapitalOrders && u.Rules.CapitalModels.Contains("battlecruiser"), "WE: Welle-E-Regeln in der Standardwelt an");
				var facs = u.NpcFactionIds().Where(x => Wars.CanWage(u, x)).ToList();
				var a = facs[0]; var b = facs[1];
				Wars.Declare(u, b, a);
				u.Actors[new ActorId(a.Value)].Treasury.Credits = 3_000_000f;
				_ceOrder = CapitalOrders.TryPlace(u, a);
				Check(_ceOrder is not null, "WE: Krieg + volle Kasse → Bauauftrag erteilt");
				if (_ceOrder is null) return true;
				_ceYard = _ceOrder.Yard;
				_m.TestDockAt(_ceYard);
				return false;
			}
			case 8:
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
				hud.Screens.Current?.Refresh();
				return false;
			case 12:
			{
				string line = hud.DockInfo.TestCapitalOrder;
				Check(line.StartsWith("Bauauftrag: Leviathan für") && line.Contains("Material"), $"WE: Stationsinfo-Zeile („{line}“)");
				// Material liefern (statt Händler abzuwarten), dann reserviert der Auftrag alles und beginnt die Montage
				var yard = u.Stations[_ceYard];
				foreach (var (g, need) in _ceOrder!.Need)
				{
					var gid = new GoodId(g);
					if (!yard.Market.TryGetValue(gid, out var e)) { e = new StockEntry { Target = 0f }; yard.Market[gid] = e; }
					e.Amount += need;
				}
				_m.Session.RunTicks(60);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 16:
			{
				string line = hud.DockInfo.TestCapitalOrder;
				Check(_ceOrder!.AssemblyStart >= 0 && line.Contains("Montage"), $"WE: Material vollständig, Montage läuft („{line}“)");
				_m.Session.RunTicks(u.Rules.CapitalAssemblyTicks + 60);
				return false;
			}
			case 20:
			{
				var ship = u.Ships.Values.FirstOrDefault(s => CapitalOrders.IsCapital(u, s));
				Check(ship is not null, "WE: Leviathan fertig (Kriegsschiff der Fraktion)");
				Check(_m.TestCapitalToast.Contains("Leviathan") && _m.TestCapitalToast.Contains("fertig"), $"WE: Toast im Spielersektor („{_m.TestCapitalToast}“)");
				if (ship is not null) _ceShip = ship.Id;
				_m.TestUndock();
				return false;
			}
			case 40:
			{
				bool seen = _m.TestView.ShipMarkers().Any(m => m.Id == _ceShip);
				Check(seen, "WE: fertiges NPC-Großkampfschiff steht als Marke im Sektor");
				Check(CapitalOrders.InfoLine(u, u.Stations[_ceYard]) is null, "WE: nach der Fertigstellung kein Auftrag mehr an der Werft");
				return true;
			}
		}
		return false;
	}
}
