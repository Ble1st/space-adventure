using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 106 (Runde 28): Handelsliste über echte Tipps bei laufenden Ticks — in einer langen Liste fünf Zeilen nacheinander öffnen (jeweils die
/// getroffene Zeile), eine offene Zeile überlebt einen Neuaufbau mit gehaltenem Finger (Hangar-Änderung), die gewählte Menge bleibt beim Zeilenwechsel,
/// „n kaufen“ bei begrenzter Menge, und die Preis-Nachfrage erscheint beim echten Tipp, wenn der Preis seit der Anzeige um mehr als 2 % stieg.
/// </summary>
public partial class SelfTest
{
	System.Collections.Generic.List<GoodId> _r28Goods = new();
	int _r28I;
	Vector2 _r28At;

	bool Round28Stage(int f)
	{
		var trade = _m.HudRef.DockTrade;
		var u = _m.Session.U;
		switch (f)
		{
			case 1:
			{
				// lange Liste: die Station mit den meisten Waren im Startsektor oder Gamma
				var st = u.StationsById().Where(s => s.Dockable && !s.IsBuildSite).OrderByDescending(s => s.Market.Count).First();
				u.WalletOf(u.Ships[u.PlayerShip]).Credits = 100000f;
				u.Ships[u.PlayerShip].Cargo.Clear();   // Laderaum frei, damit die Menge nur am Geld hängt
				_m.TestDockAt(st.Id);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
				return false;
			}
			case 30:
			{
				_r28Goods = trade.TestGoods;
				if (_r28Goods.Count == 0) { _m.TestOpenDockArea(Ui.Screens.DockAreas.Trade); _r28Wait++; if (_r28Wait < 20) { _stageStart += 5; return false; } }
				Check(_r28Goods.Count >= 8, $"lange Liste ({_r28Goods.Count} Zeilen, {_m.HudRef.DockCtx.Station.Name})");
				_r28I = 0;
				return false;
			}
		}
		// fünf Zeilen nacheinander per echtem Tipp öffnen (Index 1, 5, 3, letzte, 0), je 40 Bilder Abstand; dazwischen laufen Ticks
		int[] order = { 1, 5, 3, _r28Goods.Count - 1, 0 };
		if (f >= 40 && f < 40 + order.Length * 40)
		{
			int k = (f - 40) / 40, ph = (f - 40) % 40;
			var g = _r28Goods[System.Math.Min(order[k], _r28Goods.Count - 1)];
			if (ph == 0 && trade.TestScroll is { } sc)
			{
				// in den sichtbaren Bereich rollen, falls nötig (wie der Spieler) — ohne zu tippen
				var head0 = trade.TestRowHead(g);
				var area = sc.GetGlobalRect();
				if (head0.Position.Y < area.Position.Y) sc.ScrollTo((int)System.MathF.Max(0f, sc.ScrollVertical - (area.Position.Y - head0.Position.Y) - 4f));
				else if (head0.End.Y > area.End.Y) sc.ScrollTo((int)(sc.ScrollVertical + head0.End.Y - area.End.Y + 4f));
			}
			if (ph == 7) _r28Head = trade.TestRowHead(g);
			if (ph == 8)
			{
				var head = trade.TestRowHead(g);
				if (head != _r28Head) { _r28Head = head; _stageStart += 1; return false; }   // Layout steht noch nicht (Neuaufbau in diesem Bild) — ein Bild warten
				if (head.Size.X <= 0f) { Fail($"Zeile {u.Goods[g].Name} nicht gefunden"); return true; }
				_r28At = head.GetCenter() - new Vector2(head.Size.X * 0.3f, 0f);
				Tap(_r28At, true);
			}
			if (ph == 10) Tap(_r28At, false);
			// Runde 29: passt die geöffnete Zeile samt Editor schon ganz in die Liste, rollt nichts (erster Tipp: Liste oben, zweite Zeile)
			if (ph == 30 && k == 0) Check(trade.TestScroll?.ScrollVertical == 0, $"Öffnen einer ganz sichtbaren Zeile rollt nicht (Rollposition {trade.TestScroll?.ScrollVertical})");
			if (ph == 30) Check(trade.TestExpanded is { } ex && ex == g, $"Tipp {k + 1} öffnet die getroffene Zeile ({u.Goods[g].Name}; offen: {(trade.TestExpanded is { } o ? u.Goods[o].Name : "keine")})");
			return false;
		}
		if (f < 40) return false;
		int t = f - (40 + order.Length * 40);
		switch (t)
		{
			case 0:
			{
				// gewählte Menge bleibt beim Zeilenwechsel
				Check(System.MathF.Abs(trade.TestQty - 10f) < 0.01f, $"gewählte Menge bleibt 10 ({trade.TestQty})");
				// Finger liegt auf einer anderen Zeile, ein Neuaufbau (Hangar-Änderung) passiert, Finger hebt ab: die offene Zeile bleibt offen
				var other = trade.TestRowHead(_r28Goods[2]);
				_r28At = other.GetCenter();
				Tap(_r28At, true);
				return false;
			}
			case 2: trade.TestInvalidate(); return false;
			case 6: Tap(_r28At + new Vector2(0, 40), false); return false;   // Finger wanderte (Wischen) — kein Tipp
			case 12:
			{
				Check(trade.TestExpanded is { } ex && ex == _r28Goods[0], $"offene Zeile überlebt Neuaufbau und fremdes Loslassen ({(trade.TestExpanded is { } o ? u.Goods[o].Name : "keine")})");
				// begrenzte Menge: nur 3 bezahlbar → Taste „3 kaufen“, Zeile nennt den Grund
				var stx = _m.HudRef.DockCtx.Station;
				var good = _r28Goods.Where(gg => trade.TestBuyAmount(gg) >= 5f && stx.Market.TryGetValue(gg, out var ee) && ee.Price > 0f).OrderByDescending(gg => stx.Market[gg].Amount).First();
				trade.TestOpenRow(good);
				var e = _m.HudRef.DockCtx.Station.Market[good];
				u.WalletOf(u.Ships[u.PlayerShip]).Credits = e.Price * 3.5f;
				_r28Goods.Insert(0, good);
				trade.TestInvalidate();
				return false;
			}
			case 20:
			{
				var (l1, _, _) = trade.TestEditorLines;
				Check(l1.Contains("nur 3 bezahlbar") && l1.StartsWith("Kauf 10 ×"), $"Kaufzeile nennt die gewählte und die kaufbare Menge ({l1}; Ware {u.Goods[_r28Goods[0]].Name}, offen {(trade.TestExpanded is { } o ? u.Goods[o].Name : "keine")}, kaufbar {trade.TestBuyAmount(_r28Goods[0])}, wischt {Ui.Kit.KitScroll.Dragging}, gedrückt {Ui.Kit.KitButton.AnyHeld})");
				Check(Buttons(trade).Any(b => b.Text == "3 kaufen" && b.IsVisibleInTree()), "Taste „3 kaufen“");
				u.WalletOf(u.Ships[u.PlayerShip]).Credits = 100000f;
				trade.TestInvalidate();
				return false;
			}
			case 26:
			{
				// Preis-Nachfrage über den echten Tipp: Finger auf „Kaufen“, der Preis steigt zwischen Anzeige und Loslassen um 50 %
				_r28Before = trade.TestPriceChanged;
				_r28Cash = u.WalletOf(u.Ships[u.PlayerShip]).Credits;
				PressButton(trade, BuyLabel(trade), true);
				return false;
			}
			case 27:
			{
				var e = _m.HudRef.DockCtx.Station.Market[_r28Goods[0]];
				_r28Price = e.Price;
				e.Price *= 1.5f;
				PressButton(trade, "Kaufen", false);
				return false;
			}
			case 30:
			{
				Check(trade.TestPriceChanged == _r28Before + 1 && trade.TestMessageText.StartsWith("Preis hat sich geändert") && u.WalletOf(u.Ships[u.PlayerShip]).Credits == _r28Cash,
					$"Preis-Nachfrage beim echten Tipp ({trade.TestMessageText})");
				return false;
			}
			case 40: _m.TestUndock(); return true;
		}
		return false;
	}
	Rect2 _r28Head;
	float _r28Price, _r28Cash; int _r28Before, _r28Wait;
}
