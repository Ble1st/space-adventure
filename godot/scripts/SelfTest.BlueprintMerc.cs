using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 162 (Lücken gegenüber Interstellar Pilot 2): neues Spiel (Händler), an der Werft angedockt. (1) Schiffsmenü → „Module“: Name
/// eingeben, echter Tipp „Als Bauplan speichern“; (2) Werft: echter Tipp „Kaufen“ in der Bauplan-Zeile — das neue Schiff trägt den Fit des Bauplans, die
/// Kasse zahlt; (3) Comms → „Aufträge“: echter Tipp „Anheuern“ bei einer Gilde — der Söldner begleitet das aktive Schiff, Sold gebucht; nach dem
/// Abdocken folgt er, nach einem Spieltag wird der zweite Tagessold gebucht.
/// </summary>
public partial class SelfTest
{
	StationId _bmYard;
	int _bmBp;
	float _bmCash, _bmTreasury;
	int _bmShips;
	ActorId _bmGuild;
	ShipId _bmMerc;
	float _bmDist;

	static void BmVisible(Control c)
	{
		for (Node? n = c.GetParent(); n is not null; n = n.GetParent())
			if (n is ScrollContainer sc)
			{
				var view = sc.GetGlobalRect(); var r = c.GetGlobalRect();
				if (!view.Encloses(r)) sc.ScrollVertical = System.Math.Max(0, sc.ScrollVertical + (int)(r.Position.Y - view.Position.Y - 20f));
				return;
			}
	}

	float BmDistance(Universe u)
	{
		if (!u.Ships.TryGetValue(_bmMerc, out var m) || !u.Ships.TryGetValue(u.PlayerShip, out var p)) return -1f;
		var a = ThreatMap.Where(u, m); var b = ThreatMap.Where(u, p);
		return a.Sector != b.Sector ? 99_999f : Vec2.Distance(a.Position, b.Position);
	}

	bool BlueprintMercStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 40:
			{
				var yard = u.StationsById().First(s => s.TypeKey == "shipyard");
				_bmYard = yard.Id;
				_m.TestDockAt(yard.Id);
				u.Player.Treasury.Credits = 200_000f;
				foreach (var g in u.Goods.Values.Where(g => u.Modules.ContainsKey(g.Key) || g.Key.StartsWith("ship_kit")))
				{
					var e = yard.Stock(g.Id);
					e.Amount = System.Math.Max(e.Amount, 10f);
					if (e.Price <= 0f) e.Price = g.BasePrice;
				}
				_m.TestOpenShipMenu(u.PlayerShip, Ui.Screens.ShipMenuScreen.Tab.Modules);
				return false;
			}
			case 60:
			{
				var edit = hud.ShipMenu.TestBlueprintEdit;
				Check(edit is not null && edit.IsVisibleInTree(), "BM: Namensfeld „Bauplan“ im Reiter „Module“ sichtbar");
				if (edit is null) { Fail("BM: kein Namensfeld"); return true; }
				BmVisible(edit);
				return false;
			}
			case 64: { var e = hud.ShipMenu.TestBlueprintEdit; if (e is not null) Tap(e.GetGlobalRect().GetCenter(), true); return false; }
			case 66:
			{
				var e = hud.ShipMenu.TestBlueprintEdit;
				if (e is not null) { Tap(e.GetGlobalRect().GetCenter(), false); e.Text = "Testkahn"; e.EmitSignal(LineEdit.SignalName.TextChanged, "Testkahn"); e.ReleaseFocus(); }
				return false;
			}
			case 70:
			{
				var b = hud.ShipMenu.TestSaveBlueprintButton;
				if (b is not null) BmVisible(b);
				return false;
			}
			case 74: TapKit(hud.ShipMenu.TestSaveBlueprintButton, true, "Als Bauplan speichern"); return false;
			case 76: TapKit(hud.ShipMenu.TestSaveBlueprintButton, false, "Als Bauplan speichern"); return false;
			case 82:
			{
				var bp = Blueprints.Of(u).FirstOrDefault(b => b.Name == "Testkahn");
				Check(bp is not null, $"BM: echter Tipp speichert den Bauplan „Testkahn“ ({hud.ShipMenu.TestBlueprintMessage})");
				if (bp is null) { Fail("BM: kein Bauplan"); return true; }
				_bmBp = bp.Id;
				Check(bp.ModelKey == u.Ships[u.PlayerShip].ModelKey, "BM: Bauplan trägt das Modell des Schiffs");
				PressButton(hud.ShipMenu, "✕ Zurück", true);
				return false;
			}
			case 84: PressButton(hud.ShipMenu, "✕ Zurück", false); return false;
			case 90: _m.TestOpenDockArea(Ui.Screens.DockAreas.Shipyard); return false;
			case 100:
			{
				var b = hud.DockShipyard.TestBuyButton($"blueprint:{_bmBp}");
				Check(b is not null && b.State != Ui.Kit.KitButtonState.Disabled, $"BM: Werft listet den Bauplan kaufbar ({hud.DockShipyard.TestRowCount} Zeilen)");
				if (b is not null) BmVisible(b);
				_bmCash = u.Player.Treasury.Credits;
				_bmShips = PlayerFleet.Owned(u).Count;
				return false;
			}
			case 106: TapKit(hud.DockShipyard.TestBuyButton($"blueprint:{_bmBp}"), true, "Kaufen (Bauplan)"); return false;
			case 108: TapKit(hud.DockShipyard.TestBuyButton($"blueprint:{_bmBp}"), false, "Kaufen (Bauplan)"); return false;
			case 114:
			{
				var bp = Blueprints.Find(u, _bmBp)!;
				var owned = PlayerFleet.Owned(u);
				var bought = owned.OrderBy(s => s.Id.Value).Last();
				Check(owned.Count == _bmShips + 1 && bought.Fit.SequenceEqual(bp.Fit), $"BM: echter Tipp kauft ein Schiff nach Bauplan ({hud.DockShipyard.TestMessage})");
				Check(u.Player.Treasury.Credits < _bmCash - 1f, $"BM: Kauf bucht von der Kasse ({_bmCash:0} → {u.Player.Treasury.Credits:0})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Comms);
				return false;
			}
			case 120: PressButton(hud.CcComms, "Aufträge", true); return false;
			case 122: PressButton(hud.CcComms, "Aufträge", false); return false;
			case 130:
			{
				var guild = Mercenaries.Providers(u).FirstOrDefault(a => a.Kind == ActorKind.Guild);
				if (guild is null) { Fail("BM: keine Gilde als Söldneranbieter"); return true; }
				_bmGuild = guild.Id;
				var b = hud.CcComms.TestMercButton($"merc:hire:{guild.Id.Value}:3");
				Check(b is not null && b.State != Ui.Kit.KitButtonState.Disabled, "BM: Comms „Aufträge“ bietet Söldner der Gilde an");
				if (b is not null) BmVisible(b);
				_bmCash = u.Player.Treasury.Credits;
				_bmTreasury = guild.Treasury.Credits;
				return false;
			}
			case 134: { var b = hud.CcComms.TestMercButton($"merc:hire:{_bmGuild.Value}:3"); if (b is not null) BmVisible(b); return false; }
			case 136:
			{
				var b = hud.CcComms.TestMercButton($"merc:hire:{_bmGuild.Value}:3");
				Log($"BM: Anheuern-Taste {b?.GetGlobalRect()} sichtbar {b?.IsVisibleInTree()}");
				TapKit(b, true, "Anheuern");
				return false;
			}
			case 138: TapKit(hud.CcComms.TestMercButton($"merc:hire:{_bmGuild.Value}:3"), false, "Anheuern"); return false;
			case 144:
			{
				var c = u.Mercenaries.FirstOrDefault();
				Check(c is not null, $"BM: echter Tipp heuert einen Söldner an ({hud.CcComms.TestMessage})");
				if (c is null) { Fail("BM: kein Söldnervertrag"); return true; }
				_bmMerc = c.Ship;
				var m = u.Ships[c.Ship];
				Check(m.Owner == _bmGuild && !m.IsPlayer, "BM: Söldner gehört der Gilde, nicht dem Spieler");
				Check(m.Command is EscortCommand ec && ec.Target == u.PlayerShip, "BM: Söldner begleitet das aktive Schiff");
				float fee = u.Rules.MercenaryFeePerDay;
				Check(System.MathF.Abs(_bmCash - fee - u.Player.Treasury.Credits) < 0.5f, $"BM: erster Tagessold von der Kasse ({_bmCash:0} → {u.Player.Treasury.Credits:0})");
				Check(System.MathF.Abs(_bmTreasury + fee - u.Actors[_bmGuild].Treasury.Credits) < 0.5f, "BM: Sold geht in die Gildenkasse");
				_m.TestUndock();
				return false;
			}
			case 260:
			{
				var yard = u.Stations[_bmYard];
				_m.TestTeleportTo(yard.Position + new Vec2(160f, 40f));
				_bmDist = BmDistance(u);
				_m.Session.RunTicks(400);
				float d = BmDistance(u);
				Check(d >= 0f && (d < _bmDist - 20f || d < 40f), $"BM: Söldner folgt dem Spieler ({_bmDist:0} → {d:0})");
				_bmCash = u.Player.Treasury.Credits;
				_m.Session.RunTicks(u.Rules.DayTicks);
				var c = u.Mercenaries.FirstOrDefault(x => x.Ship == _bmMerc);
				Check(c is not null && c.PaidDays >= 2, $"BM: zweiter Tagessold gebucht (bezahlte Tage {c?.PaidDays})");
				Check(u.Player.Treasury.Credits <= _bmCash - u.Rules.MercenaryFeePerDay + 0.5f, $"BM: Sold von der Kasse ({_bmCash:0} → {u.Player.Treasury.Credits:0})");
				return true;
			}
		}
		return false;
	}
}
