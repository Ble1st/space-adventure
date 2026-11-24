using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 157 (Lücken gegenüber Interstellar Pilot 2): zwei eigene Schiffe angedockt (Debug-Spawn); Hangar → „Fracht umladen“ öffnet das
/// Schiffsmenü im Inventar; echter Tipp auf „Umladen“ bewegt die Ware; Übersicht: Name eintippen + Tipp auf „Umbenennen“; Kommandozentrale
/// „Politik → Statistik“ zeigt den Handelserlös, der nach einem Verkauf steigt; „Deine Fraktion“: Name/Kürzel ändern per Tipp.
/// </summary>
public partial class SelfTest
{
	StationId _ipStation;
	ShipId _ipOther;
	GoodId _ipFood;
	string _ipBefore = "";

	bool Ip2Stage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				var me = u.Ships[u.PlayerShip];
				var st = u.StationsById().Where(s => s.Sector == me.Sector && s.Dockable && !s.IsBuildSite && s.Market.Count > 0)
					.OrderBy(s => Vec2.Distance(s.Position, me.Position)).First();
				_ipStation = st.Id;
				_m.TestDockAt(st.Id);
				return false;
			}
			case 10:
			{
				Check(hud.Screens.Current == hud.DockMenu, "IP: angedockt, Dock-Menü offen");
				var other = DebugSpawn.SpawnShip(u, "nomad", out var msg);
				Check(other is not null && other.DockedAt == _ipStation, $"IP: zweites eigenes Schiff angedockt ({msg})");
				_ipOther = other?.Id ?? default;
				var me = u.Ships[u.PlayerShip];
				_ipFood = u.FindGood("food")!.Id;
				me.Cargo.Clear();
				me.Cargo[_ipFood] = 20f;
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Hangar);
				return false;
			}
			case 14: TapKit(hud.DockHangar.TestTransferButton, true, "Fracht umladen"); return false;
			case 16: TapKit(hud.DockHangar.TestTransferButton, false, "Fracht umladen"); return false;
			case 20:
			{
				Check(hud.Screens.Current == hud.ShipMenu && hud.ShipMenu.Current == Ui.Screens.ShipMenuScreen.Tab.Cargo, $"IP: „Fracht umladen“ öffnet das Schiffsmenü im Inventar ({hud.Screens.Current?.Id})");
				Check(hud.ShipMenu.TestTransferTo == _ipOther, $"IP: Zielschiff ist das zweite Schiff (#{hud.ShipMenu.TestTransferTo.Value})");
				TapKit(hud.ShipMenu.TestTransferButton(_ipFood), true, "Umladen");
				return false;
			}
			case 22: TapKit(hud.ShipMenu.TestTransferButton(_ipFood), false, "Umladen"); return false;
			case 26:
			{
				var me = u.Ships[u.PlayerShip];
				var other = u.Ships[_ipOther];
				Check(!me.Cargo.ContainsKey(_ipFood) && System.Math.Abs(other.Cargo.GetValueOrDefault(_ipFood) - 20f) < 0.01f,
					$"IP: echter Tipp auf „Umladen“ bewegt 20 Nahrung ({me.Cargo.GetValueOrDefault(_ipFood)} → {other.Cargo.GetValueOrDefault(_ipFood)}; {hud.ShipMenu.TestTransferMessage})");
				_m.TestOpenShipMenu(u.PlayerShip, Ui.Screens.ShipMenuScreen.Tab.Overview);
				return false;
			}
			case 30:
			{
				var edit = hud.ShipMenu.TestRenameEdit;
				Check(edit is not null && edit.IsVisibleInTree(), "IP: Namensfeld im Schiffsmenü sichtbar");
				if (edit is not null) edit.Text = "Graue Gans";
				TapKit(hud.ShipMenu.TestRenameButton, true, "Umbenennen");
				return false;
			}
			case 32: TapKit(hud.ShipMenu.TestRenameButton, false, "Umbenennen"); return false;
			case 36:
			{
				var me = u.Ships[u.PlayerShip];
				Check(me.CustomName == "Graue Gans" && ShipNames.Display(u, me).StartsWith("Graue Gans"), $"IP: Schiff umbenannt ({me.CustomName}; {hud.ShipMenu.TestRenameMessage})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Stats);
				return false;
			}
			case 40:
			{
				Check(hud.Screens.Current == hud.CcStats, $"IP: Statistik-Bildschirm offen ({hud.Screens.Current?.Id})");
				_ipBefore = hud.CcStats.TestValue("Handel: Erlös aus Verkäufen");
				Check(_ipBefore.Length > 0, $"IP: Statistik zeigt den Handelserlös ({_ipBefore})");
				// Verkauf einer Ware, die die Station annimmt
				var me = u.Ships[u.PlayerShip];
				var st = u.Stations[_ipStation];
				var good = st.Market.Keys.Where(g => MarketInfo.Acceptance(u, st, g).Units >= 1f && u.Goods[g].Form == GoodForm.Solid).OrderBy(g => g.Value).FirstOrDefault();
				Check(good.Value != 0, "IP: Station nimmt eine Ware an");
				me.Cargo[good] = 5f;
				var r = TradeActions.Sell(u, me, st, good, 5f);
				Check(r.Revenue > 0f, $"IP: Verkauf gebucht ({r.Revenue:0.0} Cr)");
				return false;
			}
			case 70:
			{
				string after = hud.CcStats.TestValue("Handel: Erlös aus Verkäufen");
				Check(after != _ipBefore && after.Length > 0, $"IP: Statistik steigt nach dem Verkauf ({_ipBefore} → {after})");
				Check(hud.CcStats.TestValue("Handel: Verkäufe / Einkäufe").StartsWith("1 /"), $"IP: ein Verkauf gezählt ({hud.CcStats.TestValue("Handel: Verkäufe / Einkäufe")})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.MyFaction);
				return false;
			}
			case 74: TapKit(hud.CcMyFaction.TestRenameOpen, true, "Name/Kürzel ändern"); return false;
			case 76: TapKit(hud.CcMyFaction.TestRenameOpen, false, "Name/Kürzel ändern"); return false;
			case 80:
			{
				Check(hud.CcMyFaction.TestNameEdit is not null && hud.CcMyFaction.TestTagEdit is not null, "IP: Eingabe Name/Kürzel offen");
				if (hud.CcMyFaction.TestNameEdit is { } n) n.Text = "Sternenbund";
				if (hud.CcMyFaction.TestTagEdit is { } t) t.Text = "SB7";
				// TextChanged feuert bei gesetztem Text nicht — wie ein Tippen melden
				hud.CcMyFaction.TestNameEdit?.EmitSignal(LineEdit.SignalName.TextChanged, "Sternenbund");
				hud.CcMyFaction.TestTagEdit?.EmitSignal(LineEdit.SignalName.TextChanged, "SB7");
				TapKit(hud.CcMyFaction.TestRenameApply, true, "Übernehmen");
				return false;
			}
			case 82: TapKit(hud.CcMyFaction.TestRenameApply, false, "Übernehmen"); return false;
			case 86:
				Check(u.Player.Name == "Sternenbund" && u.Player.Tag == "SB7", $"IP: Fraktion umbenannt ({u.Player.Name} [{u.Player.Tag}]; {hud.CcMyFaction.TestRenameMessage})");
				return true;
		}
		return false;
	}
}
