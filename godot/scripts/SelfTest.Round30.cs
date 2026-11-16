using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 107 (Runde 30): Rückkaufdeckel über den Handelsbildschirm — an einem Werftregal kaufen, einen Tick laufen, zurückverkaufen: 0 Gewinn,
/// der Hinweis nennt den Kaufpreis; Spiegel an einer Verbraucherware: verkaufen, einen Tick, zurückkaufen: 0 Gewinn, der Hinweis nennt den Mindestpreis.
/// </summary>
public partial class SelfTest
{
	GoodId _r30Good; StationId _r30St; float _r30Cash;

	bool Round30Stage(int f)
	{
		var u = _m.Session.U;
		var trade = _m.HudRef.DockTrade;
		var me = u.Ships[u.PlayerShip];
		switch (f)
		{
			case 1:
			{
				var yard = u.StationsById().First(s => s.TypeKey == "shipyard" && s.Market.Any(kv => s.IsStockOnly(kv.Key) && kv.Value.Amount >= 2f && kv.Value.Price > 0f));
				_r30St = yard.Id;
				_r30Good = yard.Market.Where(kv => yard.IsStockOnly(kv.Key) && kv.Value.Amount >= 2f && kv.Value.Price > 0f).OrderBy(kv => kv.Key.Value).First().Key;
				me.Cargo.Clear();
				u.WalletOf(me).Credits = 100000f;
				_m.TestDockAt(yard.Id);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
				return false;
			}
			case 6:
			{
				_r30Cash = u.WalletOf(me).Credits;
				var r = trade.TestExecute($"buy:{_r30Good.Value}", 2f);
				Check(r.Success, $"Regal: Kauf ({r.Message})");
				_m.Session.RunTicks(1);
				var note = new SpaceSim.Core.Services.TradeService().Offers(u, u.Stations[_r30St], me).First(o => o.Key == $"sell:{_r30Good.Value}").Note ?? "";
				var e = u.Stations[_r30St].Market[_r30Good];
				var post = PlayerLedger.Post(u, u.Stations[_r30St], _r30Good);
				Check(post is null || e.Price <= post.BoughtPrice || note.StartsWith("Wegen deines Kaufs hier: höchstens"), $"Verkaufsangebot nennt den Deckel ({note})");
				var s = trade.TestExecute($"sell:{_r30Good.Value}", 0f);
				float gain = u.WalletOf(me).Credits - _r30Cash + me.Cargo.GetValueOrDefault(_r30Good) * (r.Credits == 0 ? 0 : -r.Credits / r.Units);
				Check(s.Success && gain <= 0.5f, $"Regal: Kauf → Tick → Rückverkauf ohne Gewinn ({gain:0.00} Cr; {s.Message})");
				me.Cargo.Clear();
				return false;
			}
			case 8:
			{
				// Spiegel: Verbraucherware mit Annahme — verkaufen, ein Tick, zurückkaufen
				var st = u.StationsById().First(s => s.Dockable && !s.IsBuildSite && s.Market.Any(kv => s.Consumes(kv.Key) && !s.Produces(kv.Key) && kv.Value.Price > 0f
					&& !ExportRules.Buys(u, s, kv.Key) && TradeActions.SaleCap(u, s, kv.Key) >= 5f));
				var g = st.Market.Where(kv => st.Consumes(kv.Key) && !st.Produces(kv.Key) && kv.Value.Price > 0f && !ExportRules.Buys(u, st, kv.Key) && TradeActions.SaleCap(u, st, kv.Key) >= 5f)
					.OrderBy(kv => kv.Key.Value).First().Key;
				_r30St = st.Id; _r30Good = g;
				me.Cargo.Clear(); me.Cargo[g] = 5f;
				_m.TestDockAt(st.Id);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
				return false;
			}
			case 14:
			{
				_r30Cash = u.WalletOf(me).Credits;
				var s = trade.TestExecute($"sell:{_r30Good.Value}", 5f);
				Check(s.Success, $"Verbraucher: Verkauf ({s.Message})");
				_m.Session.RunTicks(1);
				var st = u.Stations[_r30St];
				var note = new SpaceSim.Core.Services.TradeService().Offers(u, st, me).First(o => o.Key == $"buy:{_r30Good.Value}").Note ?? "";
				var b = trade.TestExecute($"buy:{_r30Good.Value}", s.Units);
				float missing = s.Units - b.Units;
				// Runde 43: nicht zurückgekaufte Einheiten zum wirksamen Kaufpreis bewerten (Boden des offenen Verkaufspostens, sonst Markt) — der Marktpreis allein
				// unterschätzt sie, weil der Boden für jede Menge gilt (Runde 32b); vorher fiel der Rückkauf hier nie unter die verkaufte Menge
				float missingCost = missing > 0f ? SpaceSim.Core.Trading.PlayerLedger.BuyUnitPrice(u, me, st, _r30Good, missing, st.Market[_r30Good].Price) * missing : 0f;
				float gain = u.WalletOf(me).Credits - _r30Cash - missingCost;
				Check(gain <= 0.5f, $"Verbraucher: Verkauf → Tick → Rückkauf ohne Gewinn ({gain:0.00} Cr; Hinweis „{note}“; {b.Message})");
				me.Cargo.Clear();
				return false;
			}
			case 16: _m.TestUndock(); return true;
		}
		return false;
	}
}
