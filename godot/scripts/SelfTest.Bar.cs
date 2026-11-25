using System.Linq;
using Godot;
using SpaceSim.Core.Bars;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 169 (Bars, docs/BARS.md): neues Spiel (Händler), an einer Werft mit Bar andocken — Dock-Kachel „Bar“, Bereich öffnet mit Gerüchten;
/// echte Tipps: „Los kaufen“ (Preis von der Spielerkasse an die Stationskasse), ein Feld aufdecken, „Alle aufdecken“ (Symbole = beim Kauf gezogene),
/// „Gewinn abholen“/„Los abgeben“ (Auszahlung = Gewinn, höchstens Stationskasse), danach kein offenes Los mehr.
/// </summary>
public partial class SelfTest
{
	StationId _barSt;
	float _barWallet;
	int _barPerformed;
	int[] _barSymbols = System.Array.Empty<int>();
	int _barPrize;
	Vector2 _barAt;

	bool TapBar(string key, string what)
	{
		var b = _m.HudRef.DockBar.TestButton(key);
		if (b is null || !b.IsVisibleInTree() || b.State == Ui.Kit.KitButtonState.Disabled) { Fail($"BAR: Taste {what} fehlt oder aus ({_m.HudRef.DockBar.TestMessage})"); return false; }
		_barAt = b.GetGlobalRect().GetCenter();
		Tap(_barAt, true);
		return true;
	}

	bool BarStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var me = u.Ships[u.PlayerShip];
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				var bar = u.StationsById().FirstOrDefault(s => s.TypeKey == "shipyard" && Bars.IsAt(u, s));
				if (bar is null) { Fail("BAR: keine Werft mit Bar"); return false; }
				_barSt = bar.Id;
				bar.Credits = 50_000f;
				u.WalletOf(me).Credits = 5000f;
				_m.TestDockAt(_barSt);
				return false;
			}
			case 10:
			{
				var area = _m.TestDockAreas().FirstOrDefault(d => d.Id == Ui.Screens.DockAreas.Bar);
				Check(area is { Available: true } && area.Label == "Bar", $"BAR: Dock-Kachel „Bar“ ({area?.Label ?? "fehlt"}, {area?.Sub})");
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Bar);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 14:
			{
				Check(_m.TestScreenOpen("dock.bar"), "BAR: Bereich „Bar“ offen");
				Check(hud.DockBar.TestRumourCount >= 1, $"BAR: Gerüchte gezeigt ({hud.DockBar.TestRumourCount})");
				_barWallet = u.WalletOf(me).Credits;
				u.Events.Raised += e => { if (e is SpaceSim.Core.Events.ServicePerformed { Service: "bar", Offer: "ticket", Credits: < 0f } sp && sp.Station == _barSt) _barPerformed++; };
				TapBar(BarService.Ticket, "„Los kaufen“");
				return false;
			}
			case 16: Tap(_barAt, false); return false;
			case 20:
			{
				var t = Bars.OpenTicket(u);
				if (t is null) { Fail("BAR: kein Los nach dem Tipp: " + hud.DockBar.TestMessage); return false; }
				Check(Mathf.Abs(u.WalletOf(me).Credits - (_barWallet - u.Rules.BarTicketPrice)) < 0.01f, $"BAR: Lospreis von der Kasse ({_barWallet - u.WalletOf(me).Credits:0.##} Cr)");
				Check(_barPerformed == 1, $"BAR: Dienst gebucht ({_barPerformed} × ServicePerformed „ticket“; die Stationskasse bewegt sich daneben mit dem Handel)");
				_barSymbols = t.Symbols.ToArray();
				_barPrize = t.Prize;
				TapBar("reveal:4", "Feld 5");
				return false;
			}
			case 22: Tap(_barAt, false); return false;
			case 26:
			{
				var t = Bars.OpenTicket(u)!;
				Check(t.IsRevealed(4) && t.RevealedCount == 1, $"BAR: Feld 5 aufgedeckt ({t.RevealedCount})");
				TapBar("reveal:all", "„Alle aufdecken“");
				return false;
			}
			case 28: Tap(_barAt, false); return false;
			case 32:
			{
				var t = Bars.OpenTicket(u)!;
				Check(t.AllRevealed, "BAR: alle Felder offen");
				Check(t.Symbols.SequenceEqual(_barSymbols) && t.Prize == _barPrize, "BAR: Ausgang stand beim Kauf fest");
				_barWallet = u.WalletOf(me).Credits;
				TapBar(BarService.Claim, "„Gewinn abholen/Los abgeben“");
				return false;
			}
			case 34: Tap(_barAt, false); return false;
			case 38:
			{
				Check(Bars.OpenTicket(u) is null, $"BAR: Los abgegeben ({hud.DockBar.TestMessage})");
				Check(Mathf.Abs(u.WalletOf(me).Credits - (_barWallet + _barPrize)) < 0.01f, $"BAR: Auszahlung {u.WalletOf(me).Credits - _barWallet:0} = Gewinn {_barPrize}");
				Check(hud.DockBar.TestButton(BarService.Ticket) is not null, "BAR: neues Los wieder kaufbar");
				_m.TestUndock();
				return false;
			}
			case 42: return true;
		}
		return false;
	}
}
