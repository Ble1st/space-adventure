using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 173 (Welle B): Spende per echtem Tipp in Comms → Diplomatie (Kasse, Fraktionskasse, Ansehen); an der Werft die Kaufwarnung über der
/// Befehlskapazität; ein Großmodul per Tipp über das Regalziel hinaus an die Werft verkauft (Spieler liefert Module für Großkampfschiffe).
/// </summary>
public partial class SelfTest
{
	FactionId _wbF;
	float _wbWallet, _wbTreasury, _wbRel, _wbHeld, _wbShelf;
	GoodId _wbGood;
	StationId _wbYard;

	bool WelleBStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
				Check(u.Rules.Donations && u.Rules.ShipyardCapacityWarning && u.Rules.PlayerModuleShelfFactor > 0f, "WB: Spende, Kaufwarnung und Modulregal in der Standardwelt an");
				u.Player.Treasury.Credits = 50_000f;
				_wbF = u.NpcFactionIds()[0];
				Relations.Set(u, _wbF, u.PlayerFaction!.Value, 0f); Relations.Set(u, u.PlayerFaction.Value, _wbF, 0f);
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				return false;
			case 8:
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Comms);
				hud.Screens.Current?.Refresh();
				return false;
			case 10: PressButton(hud.CcComms, "Diplomatie", true); return false;
			case 12: PressButton(hud.CcComms, "Diplomatie", false); hud.Screens.Current?.Refresh(); return false;
			case 14:
				hud.CcComms.TestSelectFaction(_wbF); hud.Screens.Current?.Refresh();
				_wbWallet = u.Player.Treasury.Credits; _wbTreasury = u.Actors[new ActorId(_wbF.Value)].Treasury.Credits;
				_wbRel = Relations.Effective(u, _wbF, u.PlayerFaction!.Value);
				return false;
			case 16:
				Check(hud.CcComms.TestDonationKeys.Count >= 2, $"WB: Spendenknöpfe in der Diplomatie ({string.Join(",", hud.CcComms.TestDonationKeys)})");
				return false;
			case >= 18 and <= 24: return FdTap(hud, f - 18, "Spende 1000", "Spende");
			case 28:
			{
				hud.Screens.Current?.Refresh();
				float rel = Relations.Effective(u, _wbF, u.PlayerFaction!.Value);
				Check(Mathf.Abs(u.Player.Treasury.Credits - (_wbWallet - 1000f)) < 1f, $"WB: Spende von der Kasse ({_wbWallet:0} → {u.Player.Treasury.Credits:0}; {hud.CcComms.TestMessage})");
				Check(u.Actors[new ActorId(_wbF.Value)].Treasury.Credits >= _wbTreasury + 999f, "WB: Spende in der Fraktionskasse");
				Check(rel > _wbRel + 0.015f, $"WB: Ansehen gestiegen ({_wbRel:0.00} → {rel:0.00})");
				_m.TestCloseCommand();
				// an der Werft andocken, eigene Schiffe bis zur Befehlskapazität
				var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
				_wbYard = yard.Id;
				// so viele eigene Schiffe, bis ein weiterer Kauf über der Befehlskapazität läge (die Befehlsstufe steigt mit den Schiffen)
				for (int i = 0; i < 60 && SpaceSim.Core.Services.ShipyardWarning.For(u, u.Ships[u.PlayerShip]) is null; i++) FoOwn(u, "mule", yard);
				_m.TestDockAt(yard.Id);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Shipyard);
				return false;
			}
			case 32:
				hud.DockShipyard.Refresh();
				return false;
			case 34:
			{
				string w = hud.DockShipyard.TestWarning;
				Check(w.StartsWith(SpaceSim.Core.Services.ShipyardWarning.Text), $"WB: Werft zeigt die Kaufwarnung („{w}“)");
				// Großmodul an Bord, Regal steht schon auf dem Ziel (altes Limit erreicht)
				var yard = u.Stations[_wbYard];
				yard.Credits = 1_000_000f;
				_wbGood = u.FindGood("weapon_laser_m")!.Id;
				var e = yard.Market[_wbGood];
				e.Amount = e.Target;
				var me = u.Ships[u.PlayerShip];
				me.Cargo[_wbGood] = 3f;
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
				return false;
			}
			case 37:
			{
				hud.DockTrade.TestExpand(_wbGood);
				var intake = SpaceSim.Core.Trading.MarketInfo.Acceptance(u, u.Stations[_wbYard], _wbGood, u.Ships[u.PlayerShip]);
				Check(intake.Units >= 3f && hud.DockTrade.TestSellEnabled(_wbGood), $"WB: Werft nimmt das Großmodul über dem Regalziel an ({intake.Units:0.#}; {intake.Reason})");
				return false;
			}
			case 38:
			{
				// die geöffnete Zeile liegt in der langen Werftliste evtl. außerhalb der Ansicht: in die Mitte rollen (wie ein Wisch)
				var b = Buttons(hud.DockTrade).FirstOrDefault(x => x.Text == "Verkaufen" && x.IsVisibleInTree());
				if (b is not null && hud.DockTrade.TestScroll is { } sc)
					sc.ScrollVertical += (int)(b.GetGlobalRect().GetCenter().Y - sc.GetGlobalRect().GetCenter().Y);
				return false;
			}
			case 40:
			{
				var e = u.Stations[_wbYard].Market[_wbGood];
				_wbShelf = e.Amount; _wbHeld = u.Ships[u.PlayerShip].Cargo.GetValueOrDefault(_wbGood);
				PressButton(hud.DockTrade, "Verkaufen", true);
				return false;
			}
			case 41: PressButton(hud.DockTrade, "Verkaufen", false); return false;
			case 43:
			{
				var e = u.Stations[_wbYard].Market[_wbGood];
				float held = u.Ships[u.PlayerShip].Cargo.GetValueOrDefault(_wbGood);
				Check(held < _wbHeld && e.Amount > e.Target, $"WB: Großmodul über das Regalziel an die Werft verkauft (an Bord {_wbHeld:0} → {held:0}, Regal {_wbShelf:0} → {e.Amount:0}, Ziel {e.Target:0}; {hud.DockTrade.TestMessage})");
				_m.TestUndock();
				return true;
			}
		}
		return false;
	}
}
