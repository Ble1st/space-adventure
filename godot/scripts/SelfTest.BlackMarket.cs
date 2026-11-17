using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 128 (Schwarzmarkt, docs/SCHWARZMARKT.md): neues Spiel (Händler), der Spieler ist bei allen Fraktionen gesucht (Stufe 3), dockt am
/// Piratenversteck an — Dock-Kachel „Schwarzmarkt“ vorhanden, Bereich öffnet, Nahrung über einen echten Tipp auf „Verkaufen“ an den Hehler verkauft (Geld aus der
/// Piratenkasse, Ware im Hehlerlager), Hehlerware über einen echten Tipp auf „Kaufen“ gekauft (Geld in die Piratenkasse); Station-Info zeigt die
/// Schwarzmarktzeile.
/// </summary>
public partial class SelfTest
{
	StationId _bmSt;
	GoodId _bmFood, _bmWater;
	float _bmWallet, _bmPurse, _bmSellPrice, _bmBuyPrice;
	Vector2 _bmAt;
	float _bmPaid, _bmTaken;

	bool BlackMarketStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var me = u.Ships[u.PlayerShip];
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				var hideout = u.StationsById().FirstOrDefault(s => BlackMarket.IsAt(u, s));
				if (hideout is null) { Fail("SM: kein Versteck mit Schwarzmarkt"); return false; }
				_bmSt = hideout.Id;
				_bmFood = u.FindGood("food")!.Id;
				_bmWater = u.FindGood("water")!.Id;
				me.Cargo.Clear();
				me.Cargo[_bmFood] = 10f;
				u.WalletOf(me).Credits = 5000f;
				BlackMarket.Fence(u, hideout)!.Treasury.Credits = 9000f;
				hideout.BlackStock[_bmWater] = 6f;
				foreach (var fac in u.NpcFactionIds()) Wanted.Report(u, u.PlayerActor, fac, "assault");
				u.Events.Raised += e =>
				{
					if (e is SpaceSim.Core.Events.TreasuryUpkeep { Purpose: "blackmarket" } up) _bmPaid += up.Amount;
					if (e is SpaceSim.Core.Events.TreasuryIncome { Purpose: "blackmarket" } inc) _bmTaken += inc.Amount;
				};
				_m.TestDockAt(_bmSt);
				return false;
			}
			case 10:
			{
				var area = _m.TestDockAreas().FirstOrDefault(d => d.Id == Ui.Screens.DockAreas.BlackMarket);
				Check(area is { Available: true } && area.Label == "Schwarzmarkt", $"SM: Dock-Kachel „Schwarzmarkt“ ({area?.Label ?? "fehlt"}, {area?.Sub})");
				_m.TestOpenDockArea(Ui.Screens.DockAreas.BlackMarket);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 14:
			{
				Check(_m.TestScreenOpen("dock.blackmarket"), "SM: Bereich „Schwarzmarkt“ offen");
				Check(hud.DockBlackMarket.TestRowCount >= 2, $"SM: Fracht- und Hehlerzeile ({hud.DockBlackMarket.TestRowCount})");
				_bmWallet = u.WalletOf(me).Credits;
				_bmPurse = BlackMarket.Fence(u, u.Stations[_bmSt])!.Treasury.Credits;
				_bmSellPrice = BlackMarket.BuyPrice(u, _bmFood);
				var b = hud.DockBlackMarket.TestButton($"sell:{_bmFood.Value}");
				if (b is null || b.State == Ui.Kit.KitButtonState.Disabled) { Fail("SM: „Verkaufen“ fehlt oder aus: " + hud.DockBlackMarket.TestMessage); return false; }
				_bmAt = b.GetGlobalRect().GetCenter();
				Tap(_bmAt, true);
				return false;
			}
			case 16: Tap(_bmAt, false); return false;
			case 20:
			{
				var st = u.Stations[_bmSt];
				Check(!me.Cargo.ContainsKey(_bmFood), $"SM: Nahrung trotz Fahndung verkauft ({hud.DockBlackMarket.TestMessage})");
				Check(Mathf.Abs(u.WalletOf(me).Credits - (_bmWallet + 10f * _bmSellPrice)) < 0.05f, $"SM: Erlös {u.WalletOf(me).Credits - _bmWallet:0.00} = 10 × {_bmSellPrice:0.00}");
				Check(Mathf.Abs(_bmPaid - 10f * _bmSellPrice) < 0.05f, $"SM: Piratenkasse zahlt ({_bmPaid:0.00} Cr, Ereignis „blackmarket“)");
				Check(st.BlackStock.GetValueOrDefault(_bmFood) >= 10f - 0.01f, "SM: Nahrung im Hehlerlager");
				_bmWallet = u.WalletOf(me).Credits;
				_bmBuyPrice = BlackMarket.SellPrice(u, _bmWater);
				var b = hud.DockBlackMarket.TestButton($"buy:{_bmWater.Value}");
				if (b is null || b.State == Ui.Kit.KitButtonState.Disabled) { Fail("SM: „Kaufen“ fehlt oder aus: " + hud.DockBlackMarket.TestMessage); return false; }
				_bmAt = b.GetGlobalRect().GetCenter();
				Tap(_bmAt, true);
				return false;
			}
			case 22: Tap(_bmAt, false); return false;
			case 26:
			{
				Check(me.Cargo.GetValueOrDefault(_bmWater) >= 6f - 0.01f, $"SM: Hehlerware gekauft ({hud.DockBlackMarket.TestMessage})");
				Check(Mathf.Abs(u.WalletOf(me).Credits - (_bmWallet - 6f * _bmBuyPrice)) < 0.05f, $"SM: Preis 6 × {_bmBuyPrice:0.00} gezahlt");
				Check(Mathf.Abs(_bmTaken - 6f * _bmBuyPrice) < 0.05f, $"SM: Erlös in die Piratenkasse ({_bmTaken:0.00} Cr)");
				Check(_bmBuyPrice >= 3f * BlackMarket.BuyPrice(u, _bmWater) - 0.02f, "SM: Verkaufspreis ≥ 3 × Ankaufspreis (kein Kreislauf)");
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 30:
				Check(hud.DockInfo.TestBlackMarket.StartsWith("Schwarzmarkt: kauft jede Ware"), $"SM: Station-Info-Zeile („{hud.DockInfo.TestBlackMarket}“)");
				_m.TestUndock();
				return false;
			case 34: return true;
		}
		return false;
	}
}
