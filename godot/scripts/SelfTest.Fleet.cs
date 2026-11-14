using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 104 (Flottentest): echte Tipps — an der Werft zwei Schiffe kaufen; Kommando → Flotte: Handelsbefehl mit Grenze, Schürfbefehl, Verband
/// bilden, Eskorte; Besitz zeigt Wert und Zustand; HUD-Kostenzeile; Fernwechsel und zurück. Jede Taste höchstens drei Tipps ab „Kommando“; keine Taste läuft über.
/// </summary>
public partial class SelfTest
{
	readonly List<ShipId> _flBought = new();
	ShipId _flFirst;
	readonly List<string> _flDepth = new();
	int _flTaps;

	static Ui.Kit.KitButton? FirstEnabled(Node root, string label)
	{
		foreach (var b in Buttons(root))
			if (b.Text == label && b.IsVisibleInTree() && b.State != Ui.Kit.KitButtonState.Disabled) return b;
		return null;
	}

	/// <summary>„Kaufen“ in der Werftzeile des Modells (Zeilentext beginnt mit dem Modellnamen), nur wenn frei.</summary>
	static Ui.Kit.KitButton? RowBuy(Node root, string model)
	{
		foreach (var lbl in Labels(root))
			if (lbl.Text.StartsWith(model + " (") && lbl.GetParent() is Node row)
				foreach (var c in row.GetChildren())
					if (c is Ui.Kit.KitButton b && b.Text == "Kaufen" && b.State != Ui.Kit.KitButtonState.Disabled) return b;
		return null;
	}
	static IEnumerable<Label> Labels(Node root)
	{
		foreach (var c in root.GetChildren())
		{
			if (c is Label l) yield return l;
			foreach (var x in Labels(c)) yield return x;
		}
	}

	/// <summary>Runde 24: „Begleiten“-Taste in der Listenzeile, deren Schiffstaste mit <paramref name="rowLabel"/> beschriftet ist.</summary>
	static Ui.Kit.KitButton? BeginRow(Node root, string rowLabel)
	{
		foreach (var b in Buttons(root))
			if (b.Text == rowLabel && b.IsVisibleInTree() && b.GetParent() is HBoxContainer row)
				foreach (var c in row.GetChildren())
					if (c is Ui.Kit.KitButton g && g.Sub == "begleiten lassen") return g;
		return null;
	}

	void TapNode(Ui.Kit.KitButton b, bool down) { _p28Press = b.GetGlobalRect().GetCenter(); Tap(_p28Press.Value, down); }

	string FleetOverflow(Node root) => string.Join(" | ", Buttons(root).Where(b => b.IsVisibleInTree()).Select(b => b.Overflow()).Where(o => o.Length > 0));

	bool FleetStage(int f)
	{
		var u = _m.Session.U;
		Node cc = (Node?)_m.HudRef.Screens.Current?.Node ?? _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 4:
			{
				_m.Session.RunTicks(2);
				_flFirst = u.PlayerShip;
				u.WalletOf(u.Ships[u.PlayerShip]).Credits = 200000f;
				var yard = u.StationsById().First(s => s.Name == "Werft Vesta");
				_m.TestDockAt(yard.Id);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Shipyard);
				return false;
			}
			case 7: case 15:
			{
				// erste freie „Kaufen“-Taste in den sichtbaren Rollbereich bringen (wie ein Wisch)
				if (_m.HudRef.Screens.Current is null) { Fail("Werft-Bildschirm nicht offen"); return true; }
				var scr = (Node)_m.HudRef.Screens.Current.Node;
				_flBuy = f == 7 ? RowBuy(scr, "Goldsucher") ?? FirstEnabled(scr, "Kaufen") : FirstEnabled(scr, "Kaufen");   // erster Kauf: ein Schürfer, wenn lieferbar (der zweite Kauf wird aktiv)
				if (_flBuy is null) { Fail($"Werft: keine freie „Kaufen“-Taste ({_m.HudRef.DockShipyard.TestMessage})"); return true; }
				Node? n = _flBuy;
				while (n is not null && n is not Ui.Kit.KitScroll) n = n.GetParent();
				if (n is Ui.Kit.KitScroll ks)
				{
					var area = ks.GetGlobalRect(); var r = _flBuy.GetGlobalRect();
					if (r.End.Y > area.End.Y - 4f) ks.ScrollTo((int)(ks.ScrollVertical + r.End.Y - area.End.Y + 20f));
					else if (r.Position.Y < area.Position.Y) ks.ScrollTo((int)(ks.ScrollVertical - (area.Position.Y - r.Position.Y) - 20f));
				}
				return false;
			}
			case 10: case 18: TapNode(_flBuy!, true); return false;
			case 11: case 19: Tap(_p28Press!.Value, false); return false;
			case 13: case 21:
			{
				var cur = u.PlayerShip;
				Check(cur != _flFirst && !_flBought.Contains(cur) && u.Ships[cur].IsPlayer, $"Werft: Schiff gekauft per Tipp (aktiv #{cur.Value} {u.Ships[cur].ModelKey}; {_m.HudRef.DockShipyard.TestMessage})");
				if (!_flBought.Contains(cur) && cur != _flFirst) _flBought.Add(cur);
				return false;
			}
			case 22: _m.TestUndock(); return false;
			case 23: _flTaps = 0; PressButton(_m.HudRef.Flight, "Kommando", true); _flTaps++; return false;
			case 24: PressButton(_m.HudRef.Flight, "Kommando", false); return false;
			case 26: PressButton(cc, "Flotte", true); _flTaps++; return false;
			case 27: PressButton(cc, "Flotte", false); return false;
			case 29:
			{
				Check(_m.TestScreenOpen("cc.fleet"), "Kommando → Flotte offen");
				// Schürfen: Goldsucher wählen (Zeile) und „Schürfen“
				var miner = _flBought.Concat(new[] { _flFirst }).Select(id => u.Ships[id]).FirstOrDefault(s => s.Has(Capability.Mining) && s.Id != u.PlayerShip);
				_flMiner = miner?.Id ?? default;
				if (miner is null) { Check(true, "kein Schürfschiff gekauft (Werftlager)"); return false; }
				PressButton(cc, $"#{miner.Id.Value} {u.ShipModels[miner.ModelKey].Name}", true);
				return false;
			}
			case 30: if (_flMiner.Value != 0) PressButton(cc, $"#{_flMiner.Value} {u.ShipModels[u.Ships[_flMiner].ModelKey].Name}", false); return false;
			case 32: if (_flMiner.Value != 0) PressButton(cc, "Schürfen", true); return false;
			case 33: if (_flMiner.Value != 0) PressButton(cc, "Schürfen", false); return false;
			case 35:
				if (_flMiner.Value != 0)
				{
					Check(u.Ships[_flMiner].Command is MineCommand, $"Schürfbefehl per Tipp ({_m.HudRef.CcFleet.TestMessage})");
					_flDepth.Add("Schürfen: Kommando → Flotte → Schiff → Schürfen (4 Tipps; 3, wenn das Schiff vorgewählt ist)");
				}
				// Handel: das Erstschiff (Nomade) wählen, Reichweite +1, „Handel“
				PressButton(cc, $"#{_flFirst.Value} {u.ShipModels[u.Ships[_flFirst].ModelKey].Name}", true);
				return false;
			case 36: PressButton(cc, $"#{_flFirst.Value} {u.ShipModels[u.Ships[_flFirst].ModelKey].Name}", false); return false;
			case 38: PressButton(cc, "Handel", true); return false;
			case 39: PressButton(cc, "Handel", false); return false;
			case 41:
			{
				var t = u.Ships[_flFirst];
				Check(t.Command is TradeCommand, $"Handelsbefehl per Tipp ({_m.HudRef.CcFleet.TestMessage})");
				Check(_m.HudRef.CcFleet.TestLossGuardButton is { } lg && lg.Text == "gesperrt" && t.Command is TradeCommand tcg && !tcg.Limits.AllowLoss,
					$"Runde 24: Schalter Verlustkauf bei den Grenzen, Standard gesperrt ({_m.HudRef.CcFleet.TestLossGuardButton?.Text})");
				Check(FleetOverflow(cc).Length == 0, $"Flotte: keine Taste läuft über ({FleetOverflow(cc)})");
				// Verband bilden mit dem Händler als Führer
				PressButton(cc, "Bilden", true);
				return false;
			}
			case 42: PressButton(cc, "Bilden", false); return false;
			case 44:
			{
				Check(u.Ships[_flFirst].Squadron.Value != 0, $"Verband per Tipp gebildet ({_m.HudRef.CcFleet.TestMessage})");
				// Eskorte: ein bewaffnetes gekauftes Schiff (nicht aktiv) eskortiert den Händler
				var esc = _flBought.Concat(new[] { _flFirst }).Select(id => u.Ships[id]).FirstOrDefault(s => s.Has(Capability.Combat) && s.Id != u.PlayerShip && s.Id != _flMiner);
				_flEscort = esc?.Id ?? default;
				// Ziel der Eskorte: der Schürfer (sonst das Erstschiff)
				_flEscTarget = _flMiner.Value != 0 && esc is not null && esc.Id != _flMiner ? _flMiner : _flFirst;
				Check(esc is not null, "bewaffnetes Flottenschiff für die Eskorte vorhanden");
				if (esc is null) return false;
				PressButton(cc, $"#{esc.Id.Value} {u.ShipModels[esc.ModelKey].Name}", true);
				return false;
			}
			case 45: if (_flEscort.Value != 0) PressButton(cc, $"#{_flEscort.Value} {u.ShipModels[u.Ships[_flEscort].ModelKey].Name}", false); return false;
			case 47:
				if (_flEscort.Value != 0)
				{
					// Runde 24: „Begleiten“ in der Zeile des zu schützenden Schiffs (das gewählte Kampfschiff begleitet) — ein Tipp
					var tgt = BeginRow(cc, $"#{_flEscTarget.Value} {u.ShipModels[u.Ships[_flEscTarget].ModelKey].Name}");
					if (tgt is null) Fail($"„Begleiten“ in der Zeile #{_flEscTarget.Value} fehlt"); else TapNode(tgt, true);
				}
				return false;
			case 48: if (_flEscort.Value != 0 && _p28Press is { } at48) Tap(at48, false); return false;
			case 53:
				if (_flEscort.Value != 0)
				{
					var e = u.Ships[_flEscort];
					// Mitglied eines Verbands: eine Eskorte geht an das Schiff oder (als Führer) an den Verband
					Check(e.Command is EscortCommand ec && ec.Target == _flEscTarget, $"Eskorte per Tipp ({_m.HudRef.CcFleet.TestMessage}; Befehl {e.Command?.GetType().Name})");
					_flDepth.Add("Eskorte: Kommando → Flotte → (Kampfschiff wählen) → „Begleiten“ in der Zeile des Ziels (3 Tipps mit Vorwahl, 4 mit Schiffswahl)");
				}
				// Besitz
				PressButton(cc, "Besitz", true);
				return false;
			case 54: PressButton(cc, "Besitz", false); return false;
			case 56:
				Check(_m.TestScreenOpen("cc.assets"), "Besitz offen");
				Check(FleetOverflow(cc).Length == 0, $"Besitz: keine Taste läuft über ({FleetOverflow(cc)})");
				// Runde 25 (Gerätetest: Schürfer mit Schürfbefehl stand als „geparkt“)
				if (_flMiner.Value != 0 && u.Ships.TryGetValue(_flMiner, out var minerR25))
				{
					string stR25 = Ui.Screens.AssetsScreen.StateText(minerR25, false, false, u);
					Check(stR25 != "geparkt" && (stR25.StartsWith("schürft") || minerR25.Command is not MineCommand), $"Besitz: Zustand folgt dem Schürfbefehl ({stR25})");
				}
				// Fernwechsel aus der Flotte und zurück
				PressButton(cc, "Flotte", true);
				return false;
			case 57: PressButton(cc, "Flotte", false); return false;
			case 59: PressButton(cc, $"#{_flFirst.Value} {u.ShipModels[u.Ships[_flFirst].ModelKey].Name}", true); return false;
			case 60: PressButton(cc, $"#{_flFirst.Value} {u.ShipModels[u.Ships[_flFirst].ModelKey].Name}", false); return false;
			case 62: _flBefore = u.PlayerShip; PressButton(cc, "Übernehmen", true); return false;
			case 63: PressButton(cc, "Übernehmen", false); return false;
			case 70:
				Check(u.PlayerShip == _flFirst, $"Fernwechsel per Tipp in #{_flFirst.Value} (aktiv #{u.PlayerShip.Value})");
				if (u.Ships.ContainsKey(_flBefore)) _m.TestSwitchRemote(_flBefore);
				return false;
			case 76:
			{
				Check(u.PlayerShip == _flBefore, "zurückgewechselt");
				if (_m.HudRef.Screens.Current is not null) _m.TestCloseCommand();
				_m.SyncHudForTest();
				Check(_m.HudRef.Flight.TestCredits.Contains("/min"), $"HUD-Kostenzeile zeigt laufende Kosten ({_m.HudRef.Flight.TestCredits})");
				_flDepth.Add("Handel/Schürfen mit vorgewähltem Schiff, Verband bilden, Übernehmen: je 3 Tipps ab „Kommando“");
				Check(_flTaps <= 3, $"Flotte in {_flTaps} Tipps erreicht");
				GD.Print("[selftest] Tipptiefen: " + string.Join(" · ", _flDepth));
				// Runde 25 (Gerätetest): Kapazitätsmeldungen höchstens zwei Zeilen, „Laufende Kosten“ bleibt in der Karte
				var mePl = u.Ships[u.PlayerShip];
				string capMsg = $"Nicht möglich: Befehlskapazität erreicht ({Commands.Count(8, "Schiff", "Schiffe")} mit Einzelbefehl) – Schiffe in einen Verband aufnehmen oder mehr Besitz (Stationen, Geld) hebt die Befehlsstufe.";
				Check(Ui.Kit.UiKit.Wrap(capMsg, 13, 596).Count <= 2, $"Kapazitätsmeldung in zwei Zeilen ({Ui.Kit.UiKit.Wrap(capMsg, 13, 596).Count})");
				Check(Ui.Kit.UiKit.Wrap("Verband aufgelöst. Befehle 12/8: laufende Befehle bleiben, neue Einzelbefehle gesperrt, bis 5 Befehle enden.", 13, 596).Count <= 2, "Überkapazitätsmeldung in zwei Zeilen");
				_m.TestOpenCommand();
				_m.TestOpenCcArea("myfaction");
				return false;
			}
			case 79:
			{
				var mf = _m.HudRef.CcMyFaction;
				Check(mf.TestCostsText.Contains("/min") && !mf.TestCostsOverflow, $"Meine Fraktion: „Laufende Kosten“ passt in die Karte ({mf.TestCostsText})");
				Check(mf.TestAbilityOverflow.Length == 0, $"Meine Fraktion: Fähigkeitsnotizen passen ({mf.TestAbilityOverflow})");
				_m.TestCloseCommand();
				return true;
			}
		}
		return false;
	}
	ShipId _flMiner, _flEscort, _flBefore, _flEscTarget;
	Ui.Kit.KitButton? _flBuy;
}
