using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 103 (Runde 23): Handels-Editor — abgeblendetes „Kaufen“ nennt immer den Grund (Exportsenke, kein Geld, kein Laderaum, Lager leer,
/// Fahndung), Abnehmerzeile ehrlich („nimmt jetzt …“, „Erwartet“/„Verlust“), Zeilen passen; Dock-Menü: Kasse in der Unterzeile sichtbar, „≡ Menü“ öffnet das
/// Menü und „Schließen“ führt zurück; Taxi-Kachel ohne Ziel abgeblendet mit Grund; „Station angreifen“ mit Abstand zu Kontexttaste und Autopilot.
/// </summary>
public partial class SelfTest
{
	GoodId _r23Food;
	StationId _r23Farm;

	bool Round23Stage(int f)
	{
		var u = _m.Session.U;
		var trade = _m.HudRef.DockTrade;
		switch (f)
		{
			case 1: _m.TestMenu("new"); return false;
			case 4:
			{
				_m.Session.RunTicks(2);
				_r23Food = u.FindGood("food")!.Id;
				var sink = u.StationsById().First(s => ExportRules.Buys(u, s, _r23Food));
				_m.TestDockAt(sink.Id);
				u.WalletOf(u.Ships[u.PlayerShip]).Credits = 100000f;
				// Dock-Menü: Kasse sichtbar
				Check(_m.HudRef.DockMenu.TestSubtitle.Contains("Kasse") && !_m.HudRef.DockMenu.TestSubtitle.Contains('…'), $"Dock-Menü-Unterzeile mit Kasse ({_m.HudRef.DockMenu.TestSubtitle})");
				PressButton(_m.HudRef.DockMenu, "≡ Menü", true);
				return false;
			}
			case 5: PressButton(_m.HudRef.DockMenu, "≡ Menü", false); return false;
			case 7:
				Check(_m.HudRef.Screens.Current == _m.HudRef.Command, $"„≡ Menü“ öffnet angedockt das Menü ({_m.HudRef.Screens.Current?.Id})");
				PressButton(_m.HudRef.Command, "Schließen", true);
				return false;
			case 8: PressButton(_m.HudRef.Command, "Schließen", false); return false;
			case 10:
			{
				Check(_m.HudRef.Screens.Current == _m.HudRef.DockMenu, $"Menü schließen führt ins Dock-Menü zurück ({_m.HudRef.Screens.Current?.Id})");
				var st = _m.HudRef.DockCtx.Station;
				var taxi = Ui.Screens.DockAreas.Build(u, st, _m.Session.Services, u.Ships[u.PlayerShip]).FirstOrDefault(d => d.Id == Ui.Screens.DockAreas.Taxi);
				if (taxi is not null && taxi.Sub.Contains("Kein Ziel")) Check(!taxi.Available, "Taxi ohne Ziel abgeblendet");
				Check(taxi is null || taxi.Available || taxi.Sub.Length > 0, $"Taxi-Kachel nennt den Grund ({taxi?.Sub})");
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
				return false;
			}
			case 12: trade.TestExpand(_r23Food); return false;
			case 14:
			{
				var (l1, _, _) = trade.TestEditorLines;
				Check(l1.StartsWith("Kaufen: Nur Ankauf (Export)"), $"Exportsenke: „Kaufen“ nennt den Grund ({l1})");
				var farm = u.StationsById().First(s => s.Produces(_r23Food) && s.Market[_r23Food].Amount >= 5f);
				_r23Farm = farm.Id;
				_m.TestDockAt(farm.Id);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
				return false;
			}
			case 15: trade.TestExpand(_r23Food); return false;
			case 17:
			{
				var (l1, l2, l3) = trade.TestEditorLines;
				Check(l1.StartsWith("Kauf ") && l1.Contains(" Cr") && !l1.Contains('…'), $"Kaufzeile mit Summe ({l1})");
				Check(l2.StartsWith("Abnehmer: ") && l2.Contains("nimmt jetzt") || l2 == "Derzeit kein Abnehmer", $"Abnehmerzeile ehrlich ({l2})");
				Check(l2 == "Derzeit kein Abnehmer" || l3.StartsWith("Erwartet: +") || l3.StartsWith("Verlust −") || l3.StartsWith("Abnehmer nimmt nur"), $"Erwartungszeile ({l3})");
				// kein Geld
				u.WalletOf(u.Ships[u.PlayerShip]).Credits = 0f;
				trade.Refresh();
				return false;
			}
			case 18: trade.TestExpand(_r23Food); return false;
			case 20:
			{
				Check(trade.TestEditorLines.Item1.Contains("Nicht genug Credits"), $"kein Geld: Grund ({trade.TestEditorLines.Item1})");
				var me = u.Ships[u.PlayerShip];
				u.WalletOf(me).Credits = 100000f;
				me.Cargo[u.FindGood("iron_ore")!.Id] = me.CargoCapacity;   // Laderaum voll
				trade.Refresh();
				return false;
			}
			case 21: trade.TestExpand(_r23Food); return false;
			case 23:
			{
				Check(trade.TestEditorLines.Item1.Contains("Kein Platz"), $"Laderaum voll: Grund ({trade.TestEditorLines.Item1})");
				var me = u.Ships[u.PlayerShip];
				me.Cargo.Remove(u.FindGood("iron_ore")!.Id);
				_r23Stock = u.Stations[_r23Farm].Market[_r23Food].Amount;
				u.Stations[_r23Farm].Market[_r23Food].Amount = 0f;   // Lager leer
				trade.Refresh();
				return false;
			}
			case 24: trade.TestExpand(_r23Food); return false;
			case 26:
			{
				Check(trade.TestEditorLines.Item1.Contains("Lager leer"), $"Lager leer: Grund ({trade.TestEditorLines.Item1})");
				u.Stations[_r23Farm].Market[_r23Food].Amount = _r23Stock;
				// Fahndung: Handelsverweigerung
				var fac = u.FactionOf(u.Stations[_r23Farm]);
				if (fac is { } fv) for (int i = 0; i < 3; i++) Wanted.Report(u, u.PlayerActor, fv, "assault");
				trade.Refresh();
				return false;
			}
			case 27: trade.TestExpand(_r23Food); return false;
			case 29:
			{
				var fac = u.FactionOf(u.Stations[_r23Farm]);
				if (fac is not null) Check(trade.TestEditorLines.Item1.StartsWith("Kaufen: ") && trade.TestEditorLines.Item1.Length > 10, $"Fahndung: Grund ({trade.TestEditorLines.Item1})");
				if (fac is { } fv) u.Actors[u.PlayerActor].Wanted.Remove(fv);
				_m.TestUndock();
				return false;
			}
			case 32:
			{
				_m.SyncHudForTest();
				var hud = _m.HudRef.Flight;
				var a = hud.TestStationAttackRect; var c = hud.TestContextRect; var ap = hud.TestAutopilotRect;
				Check(!a.Grow(10f).Intersects(c) && !a.Grow(10f).Intersects(ap), $"„Station angreifen“ mit Abstand zu Kontexttaste/Autopilot ({a} / {c} / {ap})");
				return true;
			}
		}
		return false;
	}
	float _r23Stock;
}
