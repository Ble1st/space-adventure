using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 105 (Runde 27): Tickrate der Ansicht bei ×1 (4 Ticks je Sekunde) nach „Neues Spiel“, nach Zeitraffer an/aus und nach Laden, in der
/// Diagnosezeile; im Handel ein Preis überall (Zeile, Editor, Buchung) und keine Buchung zu einem teureren Preis als angezeigt; der Abnehmer bleibt bei offener Zeile.
/// </summary>
public partial class SelfTest
{
	long _r27Tick; int _r27Frame; GoodId _r27Good; StationId _r27Buyer;

	bool Round27Stage(int f)
	{
		var trade = _m.HudRef.DockTrade;
		float Rate() => (_m.Session.U.Tick - _r27Tick) / ((f - _r27Frame) / 60f);
		void Mark() { _r27Tick = _m.Session.U.Tick; _r27Frame = f; }
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 30: Check(!_m.TestFast, "nach „Neues Spiel“ Zeit ×1"); Mark(); return false;
			case 1230:
			{
				float r = Rate();
				GD.Print($"[selftest] Takt nach „Neues Spiel“: {r:0.00} Ticks/s (Sitzung misst {_m.Session.TickRate:0.0})");
				Check(r > 3.9f && r < 4.1f && _m.Session.TickRate > 3.9f && _m.Session.TickRate < 4.1f, $"×1 läuft mit 4 Ticks/s ({r:0.00}, Sitzung {_m.Session.TickRate:0.0})");
				Check(_m.CameraDebugLine().StartsWith("Takt 4") && _m.CameraDebugLine().Contains("Ticks/s"), $"Diagnosezeile nennt den Takt ({_m.CameraDebugLine()[..24]})");
				_m.TestSetFast(true); Mark(); return false;
			}
			case 1470:
			{
				float r = Rate();
				Check(r > 38f && r < 42f, $"×10 läuft mit 40 Ticks/s ({r:0.0})");
				_m.TestSetFast(false); Mark(); return false;
			}
			case 2670:
			{
				float r = Rate();
				Check(r > 3.9f && r < 4.1f, $"nach Zeitraffer aus wieder 4 Ticks/s ({r:0.00})");
				Check(_m.TestSave(), "Speichern");
				_m.TestMenu("load");
				return false;
			}
			case 2700: Check(!_m.TestFast, "nach Laden Zeit ×1"); Mark(); return false;
			case 3900:
			{
				float r = Rate();
				GD.Print($"[selftest] Takt nach Laden: {r:0.00} Ticks/s");
				Check(r > 3.9f && r < 4.1f, $"nach Laden 4 Ticks/s ({r:0.00})");
				// Handel: eine Ware mit Lager an einer Station der Startsektors
				var u = _m.Session.U;
				var st = u.StationsById().First(s => s.Dockable && s.Sector == u.PlayerSector && s.Market.Any(kv => kv.Value.Amount >= 10f && kv.Value.Price > 0f && !ExportRules.Buys(u, s, kv.Key)));
				_r27Good = st.Market.First(kv => kv.Value.Amount >= 10f && kv.Value.Price > 0f && !ExportRules.Buys(u, st, kv.Key)).Key;
				u.WalletOf(u.Ships[u.PlayerShip]).Credits = 100000f;
				_m.TestDockAt(st.Id);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
				return false;
			}
			case 3903: trade.TestExpand(_r27Good); return false;
			case 3908:
			{
				var u = _m.Session.U;
				var e = _m.HudRef.DockCtx.Station.Market[_r27Good];
				Check(System.MathF.Abs(trade.TestShownPrice(_r27Good) - e.Price) < 0.001f, $"Zeile/Editor zeigen den Marktpreis dieses Ticks ({trade.TestShownPrice(_r27Good):0.00} / {e.Price:0.00})");
				var (_, l2, l3) = trade.TestEditorLines;
				Check(l2 == "Derzeit kein Abnehmer" || l2.Contains("zuletzt") && !l2.Contains("Ankunft") && !l2.Contains("≈"), $"Abnehmerzeile ohne Versprechen ({l2})");
				Check(l2 == "Derzeit kein Abnehmer" || l3.StartsWith("Lohnt sich") || l3.StartsWith("Riskant") || l3.StartsWith("Verlust") || l3.Contains("nimmt nur"), $"Ampelzeile ({l3})");
				_r27Buyer = trade.TestEditorBuyer;
				// Preis springt zwischen Anzeige und Tipp: nicht buchen, neuer Preis in der Meldung
				float shown = e.Price;
				e.Price = shown * 1.5f;
				float cash = u.WalletOf(u.Ships[u.PlayerShip]).Credits;
				int before = trade.TestPriceChanged;
				trade.TestBuyRow(_r27Good, 2f);
				Check(trade.TestPriceChanged == before + 1 && u.WalletOf(u.Ships[u.PlayerShip]).Credits == cash && trade.TestMessageText.StartsWith("Preis hat sich geändert"),
					$"Buchung zum teureren Preis verweigert ({trade.TestMessageText})");
				trade.TestBuyRow(_r27Good, 2f);   // zweiter Tipp: zum genannten Preis
				Check(trade.TestMessageText.StartsWith("Gekauft 2 × "), $"zweiter Tipp kauft zum genannten Preis ({trade.TestMessageText})");
				return false;
			}
			case 3990:
			{
				// Der Abnehmer bleibt bei offener Zeile (auch wenn die Preise laufen), ~20 s
				var b = trade.TestEditorBuyer;
				Check(_r27Buyer.Value == 0 || b == _r27Buyer, $"Abnehmer bleibt bei offener Zeile (#{_r27Buyer.Value} → #{b.Value})");
				return false;
			}
			case 4000: _m.TestUndock(); return true;
		}
		return false;
	}
}
