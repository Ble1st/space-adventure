using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 155 (Nutzerwünsche nach Runde 46): (1) „Schiff verlassen“ wirkt mit einem Tipp, auch wenn zwischen Drücken und Loslassen Ticks laufen und das
/// Dock-Menü aufgefrischt wird; (2) ein Befehl angedockt an Bord (nicht über die Flottenliste gegeben) startet den Befehlsstand und das Schiff legt wirklich ab
/// (die Ansicht schreibt keine Stationsposition mehr zurück); (3) Debug-Bereich: Tipp auf ein Modell spawnt ein eigenes Schiff; (4) Schiffsmenü aus der Flotte
/// per Tipp, Reiter „Inventar“, „✕ Zurück“ führt zur Flotte.
/// </summary>
public partial class SelfTest
{
	StationId _dfStation;
	int _dfShips;
	ShipId _dfSpawned;
	StationId _dfVisit;

	bool DockFlowStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:miner"); return false;
			case 6:
			{
				var me = u.Ships[u.PlayerShip];
				var st = u.StationsById().Where(s => s.Sector == me.Sector && s.Dockable).OrderBy(s => Vec2.Distance(s.Position, me.Position)).First();
				_dfStation = st.Id;
				_m.TestDockAt(st.Id);
				return false;
			}
			case 10: Check(hud.Screens.Current == hud.DockMenu, "DF: angedockt, Dock-Menü offen"); PressButton(hud.DockMenu, "Schiff verlassen", true); return false;
			case 14: _m.Session.RunTicks(8); hud.DockMenu.Refresh(); return false;   // Ticks und Auffrischung unter dem Finger
			case 18: PressButton(hud.DockMenu, "Schiff verlassen", false); return false;
			case 22: Check(u.PlayerOnFoot && u.PlayerAtStation == _dfStation, $"DF: „Schiff verlassen“ wirkt mit einem Tipp trotz Ticks und Auffrischung (zu Fuß {u.PlayerOnFoot})"); return false;
			case 26: _m.TestMenu("new:miner"); return false;
			case 32:
			{
				var me = u.Ships[u.PlayerShip];
				var st = u.StationsById().Where(s => s.Sector == me.Sector && s.Dockable).OrderBy(s => Vec2.Distance(s.Position, me.Position)).First();
				_dfStation = st.Id;
				_m.TestDockAt(st.Id);
				return false;
			}
			case 36:
			{
				var me = u.Ships[u.PlayerShip];
				bool ok = Commands.Give(u, me, new MineCommand(new AutoLimits(u.PlayerSector, 0, 2f, 0f)), out var msg);
				Check(ok, $"DF: Schürfbefehl angedockt an Bord gegeben ({msg})");
				return false;
			}
			case 140: Check(_m.TestSpectating, "DF: angedockt + Befehl an Bord → Befehlsstand (mitfliegen), auch ohne Flottenliste"); return false;
			case 300:
			{
				// Nutzer: „von Schiff mit Befehl zu Station wechseln (ohne andocken) — Schiff im Hintergrund statt Station, als angedockt angezeigt“
				var st = DebugSpawn.SpawnStation(u, "solar_plant", out var msg);
				Check(st is not null, $"DF: eigene Station gespawnt ({msg})");
				if (st is not null) { _dfVisit = st.Id; _m.TestVisitStation(st.Id); }
				return false;
			}
			case 330:
			{
				var st = u.Stations[_dfVisit];
				var me = u.Ships[u.PlayerShip];
				Check(u.PlayerOnFoot && !_m.TestSpectating && !u.PlayerSpectates, "DF: Wechsel in die eigene Station beendet den Befehlsstand (zu Fuß)");
				var p = _m.TestPlayerNodePos;
				Check(Vec2.Distance(new Vec2(p.X, p.Z), st.Position) < 1f, $"DF: Ansicht bleibt bei der Station, nicht beim Schiff (Abstand {Vec2.Distance(new Vec2(p.X, p.Z), st.Position):0})");
				Check(me.DockedAt != _dfVisit, "DF: das Schiff liegt nicht an der besuchten Station");
				string state = Ui.Screens.AssetsScreen.StateText(me, false, true, u);
				Check(!state.Contains("angedockt") || me.DockedAt.Value != 0, $"DF: Besitz zeigt das schürfende Schiff nicht als angedockt ({state})");
				return false;
			}
			case 400:
			{
				var me = u.Ships[u.PlayerShip];
				var st = u.Stations[_dfStation];
				Check(me.DockedAt != _dfStation || me.Transit is not null || Vec2.Distance(me.Position, st.Position) > st.Radius + 20f,
					$"DF: das Schiff legt ab und führt den Befehl auch allein (Person zu Fuß) aus (angedockt {me.DockedAt.Value}, Abstand {Vec2.Distance(me.Position, st.Position):0})");
				Commands.Cancel(u, me);
				return false;
			}
			case 406:
				_dfShips = u.Ships.Count;
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Debug);
				hud.Screens.Current?.Refresh();
				return false;
			case 410: Check(hud.Screens.Current == hud.CcDebug, "DF: Debug-Bereich offen"); PressButton(hud.CcDebug, DebugSpawn.ShipModels(u)[0].Name, true); return false;
			case 412: PressButton(hud.CcDebug, DebugSpawn.ShipModels(u)[0].Name, false); return false;
			case 416:
			{
				Check(u.Ships.Count == _dfShips + 1, $"DF: Tipp auf ein Modell spawnt ein eigenes Schiff ({hud.CcDebug.TestMessage})");
				var sp = u.ShipsById().Last(s => s.IsPlayer);
				_dfSpawned = sp.Id;
				Check(sp.Owner == u.PlayerActor, "DF: gespawntes Schiff gehört dir");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Fleet);
				hud.CcFleet.Select(_dfSpawned);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 420: PressButton(hud.CcFleet, "Menü", true); return false;
			case 422: PressButton(hud.CcFleet, "Menü", false); return false;
			case 426:
				Check(hud.Screens.Current == hud.ShipMenu && hud.ShipMenu.Ship == _dfSpawned, "DF: „Menü“ in der Flotte öffnet das Schiffsmenü des Schiffs");
				PressButton(hud.ShipMenu, "Inventar", true);
				return false;
			case 428: PressButton(hud.ShipMenu, "Inventar", false); return false;
			case 432: Check(hud.ShipMenu.Current == Ui.Screens.ShipMenuScreen.Tab.Cargo, "DF: Reiter „Inventar“"); PressButton(hud.ShipMenu, "✕ Zurück", true); return false;
			case 434: PressButton(hud.ShipMenu, "✕ Zurück", false); return false;
			case 438:
				Check(hud.Screens.Current == hud.CcFleet, "DF: „✕ Zurück“ führt zur Flotte zurück");
				_m.TestCloseCommand();
				return true;
		}
		return false;
	}
}
