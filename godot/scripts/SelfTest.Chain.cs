using System.Collections.Generic;
using System.Linq;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 102 (Auftrag „Test der gesamten Handelskette mit Selberhandel des Spielers“): eine kurze Kette über den Handelsbildschirm mit echten
/// Tipps — selbst abgebautes Erz an die Hütte verkaufen, Stahl beim Stahlwerk kaufen (dort „Abwerfen“ neben dem Grund, das Stahlwerk nimmt Stahl nicht),
/// beim besten Abnehmer verkaufen, Nahrung bei der Farm kaufen und an eine Planeten-Handelsstation verkaufen (Stationsinfo: „Export an …“).
/// </summary>
public partial class SelfTest
{
	GoodId _chOre, _chSteel, _chFood;
	float _chHeld, _chWallet;

	void ChainDock(SpaceSim.Core.Model.Station st)
	{
		_m.TestDockAt(st.Id);
		_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
	}

	bool ChainStage(int f)
	{
		var u = _m.Session.U;
		var trade = _m.HudRef.DockTrade;
		var me = u.Ships[u.PlayerShip];
		switch (f)
		{
			case 1: _m.TestMenu("new:anchor"); return false;
			case 400:   // ≈ 7 s verankerter Kontaktabbau
			{
				_chOre = me.Cargo.Where(kv => kv.Value >= 1f).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
				Check(_chOre.Value != 0, $"Ankertest: Erz abgebaut ({string.Join(",", me.Cargo.Select(kv => $"{kv.Key.Value}:{kv.Value:0.#}"))})");
				var hut = MarketInfo.BestBuyerForGood(u, _chOre);
				Check(hut is not null, "Erz hat einen annehmenden Abnehmer");
				if (hut is null) return true;
				ChainDock(hut);
				return false;
			}
			case 403: trade.TestExpand(_chOre); return false;
			case 405: _chHeld = me.Cargo.GetValueOrDefault(_chOre); _chWallet = u.PlayerWallet.Credits; PressButton(trade, "Verkaufen", true); return false;
			case 406: PressButton(trade, "Verkaufen", false); return false;
			case 408:
			{
				Check(me.Cargo.GetValueOrDefault(_chOre) < _chHeld && u.PlayerWallet.Credits > _chWallet, $"Erz an der Hütte verkauft ({_chHeld:0.#} → {me.Cargo.GetValueOrDefault(_chOre):0.#}, Kasse {_chWallet:0} → {u.PlayerWallet.Credits:0}; {trade.TestMessage})");
				// Rest abladen, dann Stahl beim Stahlwerk
				foreach (var g in me.Cargo.Keys.ToList()) me.Cargo.Remove(g);
				_chSteel = u.FindGood("steel")!.Id;
				u.PlayerWallet.Credits = 100000f;
				var works = u.StationsById().First(s => s.Produces(_chSteel) && s.Market.TryGetValue(_chSteel, out var e) && e.Amount >= 2f);
				ChainDock(works);
				return false;
			}
			case 411: trade.TestExpand(_chSteel); return false;
			case 413: _chHeld = me.Cargo.GetValueOrDefault(_chSteel); PressButton(trade, "Kaufen", true); return false;
			case 414: PressButton(trade, "Kaufen", false); return false;
			case 416:
				Check(me.Cargo.GetValueOrDefault(_chSteel) > _chHeld, $"Stahl beim Stahlwerk gekauft ({trade.TestMessage})");
				trade.Refresh();
				return false;
			case 418:
			{
				Check(trade.TestDropShown(_chSteel) && !trade.TestSellEnabled(_chSteel), $"Stahlwerk nimmt Stahl nicht: „Abwerfen“ neben dem Grund ({trade.TestSellNote(_chSteel)})");
				var buyer = MarketInfo.BestBuyerForGood(u, _chSteel);
				Check(buyer is not null, "Stahl hat einen Abnehmer");
				if (buyer is null) return true;
				ChainDock(buyer);
				return false;
			}
			case 421: trade.TestExpand(_chSteel); return false;
			case 423: _chHeld = me.Cargo.GetValueOrDefault(_chSteel); _chWallet = u.PlayerWallet.Credits; PressButton(trade, "Verkaufen", true); return false;
			case 424: PressButton(trade, "Verkaufen", false); return false;
			case 426:
			{
				Check(me.Cargo.GetValueOrDefault(_chSteel) < _chHeld && u.PlayerWallet.Credits > _chWallet, $"Stahl beim Abnehmer verkauft ({trade.TestMessage})");
				_chFood = u.FindGood("food")!.Id;
				var farm = u.StationsById().First(s => s.Produces(_chFood) && s.Market.TryGetValue(_chFood, out var e) && e.Amount >= 2f);
				ChainDock(farm);
				return false;
			}
			case 429: trade.TestExpand(_chFood); return false;
			case 431: _chHeld = me.Cargo.GetValueOrDefault(_chFood); PressButton(trade, "Kaufen", true); return false;
			case 432: PressButton(trade, "Kaufen", false); return false;
			case 434:
			{
				Check(me.Cargo.GetValueOrDefault(_chFood) > _chHeld, $"Nahrung bei der Farm gekauft ({trade.TestMessage})");
				var sink = u.StationsById().First(s => SpaceSim.Core.Simulation.ExportRules.Buys(u, s, _chFood) && MarketInfo.Acceptance(u, s, _chFood).Accepts);
				ChainDock(sink);
				return false;
			}
			case 437: trade.TestExpand(_chFood); return false;
			case 439: _chHeld = me.Cargo.GetValueOrDefault(_chFood); PressButton(trade, "Verkaufen", true); return false;
			case 440: PressButton(trade, "Verkaufen", false); return false;
			case 442:
				Check(me.Cargo.GetValueOrDefault(_chFood) < _chHeld, $"Nahrung an die Planeten-Handelsstation verkauft ({trade.TestMessage})");
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
				return false;
			case 445:
				Check(_m.HudRef.DockInfo.TestExport.StartsWith("Export an") && _m.HudRef.DockInfo.TestExport.Contains("unterwegs"),
					$"Stationsinfo zeigt die Exportcharge („{_m.HudRef.DockInfo.TestExport}“)");
				_m.TestUndock();
				return true;
		}
		return false;
	}
}
