using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 146 (Ausbau Punkt 1/1b): Flottenbefehle über echte Tipps im Flottenbildschirm — Angreifen, Verfolgen, Bergen, Liefern (Zielwahl in drei
/// Tipps: Schiff → Befehl → Ziel), Sammelpunkt und Konvoi (Gruppe mit „mit“), Blockade ohne Krieg gesperrt; im Flug „Verband greift an“ auf der Zielkarte
/// (zwei Tipps) und das Flottenschiff stellt das Ziel (Verbrechen gemeldet). Keine Taste läuft über.
/// </summary>
public partial class SelfTest
{
	ShipId _foA, _foB, _foC, _foD, _foE, _foS, _foM, _foH, _foT;
	readonly Queue<(string What, System.Func<bool> Step)> _foSteps = new();

	Ship FoOwn(Universe u, string model, Station at, IReadOnlyList<string?>? fit = null)
	{
		var m = u.ShipModels[model];
		var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, fit ?? m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
		s.HomeBase = at.Id;
		u.AddShip(s);
		s.RecomputeStats(u);
		return s;
	}

	string FoRow(Universe u, ShipId id) => $"#{id.Value} {u.ShipModels[u.Ships[id].ModelKey].Name}";

	/// <summary>Bringt eine Taste in alle rollenden Vorfahren hinein (wie ein Wisch).</summary>
	static void FoScrollTo(Ui.Kit.KitButton b)
	{
		for (Node? n = b.GetParent(); n is not null; n = n.GetParent())
			if (n is Ui.Kit.KitScroll ks)
			{
				var area = ks.GetGlobalRect(); var r = b.GetGlobalRect();
				if (r.End.Y > area.End.Y - 4f) ks.ScrollTo((int)(ks.ScrollVertical + r.End.Y - area.End.Y + 12f));
				else if (r.Position.Y < area.Position.Y + 4f) ks.ScrollTo((int)(ks.ScrollVertical - (area.Position.Y - r.Position.Y) - 12f));
			}
	}

	/// <summary>Tippt eine Taste: Bild 0 hineinrollen, Bild 2 drücken, Bild 3 loslassen. Liefert true, wenn fertig.</summary>
	bool FoTap(int f, int at, System.Func<Ui.Kit.KitButton?> pick, string what)
	{
		if (f == at) { if (pick() is { } b) FoScrollTo(b); else Fail($"Flottenbefehle: Taste {what} fehlt ({_m.HudRef.CcFleet.TestMessage}; Ziele {string.Join(",", _m.HudRef.CcFleet.TestTargetLabels)}; T #{_foT.Value})"); }
		else if (f == at + 2) { if (pick() is { } b) TapNode(b, true); }
		else if (f == at + 3 && _p28Press is { } p) Tap(p, false);
		return f >= at + 3;
	}

	Ui.Kit.KitButton? FoRowButton(Node cc, string label) => Buttons(cc).FirstOrDefault(b => b.Text == label && b.IsVisibleInTree());
	Ui.Kit.KitButton? FoTarget(System.Func<string, bool> match)
	{
		var fs = _m.HudRef.CcFleet;
		for (int i = 0; fs.TestTargetButton(i) is { } b; i++) if (match(b.Text) && b.State != Ui.Kit.KitButtonState.Disabled) return b;
		return null;
	}

	bool FleetOpsStage(int f)
	{
		var u = _m.Session.U;
		Node cc = (Node?)_m.HudRef.Screens.Current?.Node ?? _m.HudRef;
		var fs = _m.HudRef.CcFleet;
		switch (f)
		{
			case 1: _m.TestMenu("new:fighter"); return false;
			case 4:
			{
				_m.Session.RunTicks(2);
				if (_m.TestDocked) _m.TestUndock();
				u.WalletOf(u.Ships[u.PlayerShip]).Credits = 200000f;
				var me = u.Ships[u.PlayerShip];
				var yard = u.StationsById().Where(s => s.Sector == me.Sector && !s.IsBuildSite && Docking.Allows(u, s, me)).OrderBy(s => Vec2.Distance(s.Position, me.Position)).First();
				_foA = FoOwn(u, "falcon", yard).Id; _foB = FoOwn(u, "falcon", yard).Id;
				_foC = FoOwn(u, "falcon", yard).Id; _foD = FoOwn(u, "falcon", yard).Id; _foE = FoOwn(u, "falcon", yard).Id;
				_foS = FoOwn(u, "tinker", yard, new[] { "reactor_mk2", "chem_engine_m", "wreck_grapple_s", "weapon_massdriver_s", "cargo_universal_60", "cargo_universal_60", "weapon_magazine_s" }).Id;
				_foM = FoOwn(u, "mule", yard).Id; _foH = FoOwn(u, "hauler", yard).Id;
				// fremdes Zielschiff (freier Händler) im Sektor, hält still
				var tm = u.ShipModels["mule"];
				var t = ShipPurchase.Create(u, u.NewShipId(), tm.Key, u.ShipClasses[tm.ClassKey].Slots, tm.Fit, ShipRole.Trader, Controller.Ai, u.FreeActor, yard, 500f);
				u.AddShip(t);
				// nah am Liegeplatz der Flotte (die Zielliste ordnet nach Abstand zum befehligten Schiff)
				t.Position = u.Ships[_foA].Position + new Vec2(6f, 4f); t.DockedAt = default; t.NextThinkTick = long.MaxValue / 4;
				_foT = t.Id;
				SpaceSim.Core.Simulation.Wrecks.Drop(u, yard.Sector, yard.Position + new Vec2(90f, 50f), new Dictionary<GoodId, float> { [u.FindGood("food")!.Id] = 8f }, default, "Wrack: Selbsttest");
				_m.TestOpenCommand();
				return false;
			}
			case 6: PressButton(_m.HudRef.Command, "Kommando", true); return false;
			case 7: PressButton(_m.HudRef.Command, "Kommando", false); return false;
			case 9: PressButton(cc, "Flotte", true); return false;
			case 10: PressButton(cc, "Flotte", false); return false;
			case 12: Check(_m.TestScreenOpen("cc.fleet"), "Flottenbefehle: Kommando → Flotte offen"); return false;
		}
		// ── Angreifen: Schiff A → „Angreifen“ → Ziel T (drei Tipps) ──
		int s0 = 14;
		if (f >= s0 && f < s0 + 4) { FoTap(f, s0, () => FoRowButton(cc, FoRow(u, _foA)), "Schiff A"); return false; }
		if (f >= s0 + 5 && f < s0 + 9) { FoTap(f, s0 + 5, () => fs.TestCommandButton(CommandKinds.Attack), "Angreifen"); return false; }
		if (f >= s0 + 10 && f < s0 + 14) { FoTap(f, s0 + 10, () => FoTarget(l => l.EndsWith($"#{_foT.Value}")), "Ziel T"); return false; }
		if (f == s0 + 15)
		{
			Check(u.Ships[_foA].Command is AttackCommand ac && ac.Ship == _foT, $"Angreifen per drei Tipps (Schiff → Befehl → Ziel) ({fs.TestMessage})");
			Check(fs.TestMessage.Contains("Greift"), $"Meldung nennt den Angriff ({fs.TestMessage})");
			Check(FleetOverflow(cc).Length == 0, $"Flotte: keine Taste läuft über ({FleetOverflow(cc)})");
			Commands.Cancel(u, u.Ships[_foA]);   // das Ziel bleibt für die folgenden Schritte erhalten
			return false;
		}
		// ── Verfolgen: Schiff B → „Verfolgen“ → Ziel T ──
		int s1 = s0 + 17;
		if (f >= s1 && f < s1 + 4) { FoTap(f, s1, () => FoRowButton(cc, FoRow(u, _foB)), "Schiff B"); return false; }
		if (f >= s1 + 5 && f < s1 + 9) { FoTap(f, s1 + 5, () => fs.TestCommandButton(CommandKinds.Pursue), "Verfolgen"); return false; }
		if (f >= s1 + 10 && f < s1 + 14) { FoTap(f, s1 + 10, () => FoTarget(l => l.EndsWith($"#{_foT.Value}")), "Verfolgungsziel T"); return false; }
		if (f == s1 + 15)
		{
			Check(u.Ships[_foB].Command is PursueCommand pc && pc.Target == _foT && pc.Until > u.Tick, $"Verfolgen per Tipp ({fs.TestMessage})");
			Commands.Cancel(u, u.Ships[_foB]);
			return false;
		}
		int s2 = s1 + 17;
		if (f >= s2 && f < s2 + 4) { FoTap(f, s2, () => fs.TestCommandButton(CommandKinds.Blockade), "Blockade"); return false; }
		if (f == s2 + 5)
		{
			Check(fs.TestTargetLabels.Count == 0 && Labels(cc).Any(l => l.Text.Contains("nur im Krieg")), "Blockade ohne Krieg: keine Ziele, Hinweis „nur im Krieg“");
			return false;
		}
		// ── Bergen: S → „Bergen“ → Sektor mit Wrack ──
		int s3 = s2 + 7;
		if (f >= s3 && f < s3 + 4) { FoTap(f, s3, () => FoRowButton(cc, FoRow(u, _foS)), "Bergeschiff"); return false; }
		if (f >= s3 + 5 && f < s3 + 9) { FoTap(f, s3 + 5, () => fs.TestCommandButton(CommandKinds.Salvage), "Bergen"); return false; }
		if (f >= s3 + 10 && f < s3 + 14) { FoTap(f, s3 + 10, () => FoTarget(_ => true), "Bergungssektor"); return false; }
		if (f == s3 + 15) { Check(u.Ships[_foS].Command is SalvageCommand, $"Bergen per Tipp ({fs.TestMessage})"); return false; }
		// ── Liefern: M → „Liefern“ → erste Route ──
		int s4 = s3 + 17;
		if (f >= s4 && f < s4 + 4) { FoTap(f, s4, () => FoRowButton(cc, FoRow(u, _foM)), "Händler"); return false; }
		if (f >= s4 + 5 && f < s4 + 9) { FoTap(f, s4 + 5, () => fs.TestCommandButton(CommandKinds.Deliver), "Liefern"); return false; }
		if (f >= s4 + 10 && f < s4 + 14) { FoTap(f, s4 + 10, () => FoTarget(_ => true), "Lieferroute"); return false; }
		if (f == s4 + 15) { Check(u.Ships[_foM].Command is DeliverCommand, $"Liefern per Tipp ({fs.TestMessage})"); return false; }
		// ── Sammelpunkt: C → „Sammeln“ → D „mit“ → Station ──
		int s5 = s4 + 17;
		if (f >= s5 && f < s5 + 4) { FoTap(f, s5, () => FoRowButton(cc, FoRow(u, _foC)), "Schiff C"); return false; }
		if (f >= s5 + 5 && f < s5 + 9) { FoTap(f, s5 + 5, () => fs.TestCommandButton(CommandKinds.Rally), "Sammeln"); return false; }
		if (f >= s5 + 10 && f < s5 + 14) { FoTap(f, s5 + 10, () => fs.TestGroupButton(_foD), "„mit“ D"); return false; }
		if (f >= s5 + 15 && f < s5 + 19) { FoTap(f, s5 + 15, () => FoTarget(_ => true), "Treffpunkt"); return false; }
		if (f == s5 + 20)
		{
			Check(u.Ships[_foC].Command is RallyCommand r1 && r1.Leader == _foC && u.Ships[_foD].Command is RallyCommand r2 && r2.Leader == _foC, $"Sammelpunkt per Tipp ({fs.TestMessage})");
			return false;
		}
		// ── Konvoi: H → „Konvoi“ → M „mit“, B „mit“ → „Konvoi bilden“ ──
		int s6 = s5 + 22;
		if (f >= s6 && f < s6 + 4) { FoTap(f, s6, () => FoRowButton(cc, FoRow(u, _foH)), "Frachter H"); return false; }
		if (f >= s6 + 5 && f < s6 + 9) { FoTap(f, s6 + 5, () => fs.TestCommandButton(CommandKinds.Convoy), "Konvoi"); return false; }
		if (f >= s6 + 10 && f < s6 + 14) { FoTap(f, s6 + 10, () => fs.TestGroupButton(_foM), "„mit“ M"); return false; }
		if (f >= s6 + 15 && f < s6 + 19) { FoTap(f, s6 + 15, () => fs.TestGroupButton(_foE), "„mit“ E"); return false; }
		if (f >= s6 + 20 && f < s6 + 24) { FoTap(f, s6 + 20, () => fs.TestConvoyButton, "Konvoi bilden"); return false; }
		if (f == s6 + 25)
		{
			var h = u.Ships[_foH]; var m = u.Ships[_foM];
			Check(h.Command is ConvoyCommand && m.Command is ConvoyCommand && h.Squadron.Value != 0 && h.Squadron == m.Squadron, $"Konvoi per Tipp: Händler mit Konvoibefehl im Verband ({fs.TestMessage})");
			Check(u.Squadrons.TryGetValue(h.Squadron, out var sq) && sq.Leader == FleetFormations.SlowestTrader(new[] { h, m }).Id, "Konvoi: der langsamste Händler führt");
			Check(u.Ships[_foE].Command is EscortCommand, "Konvoi: Kampfschiff begleitet");
			Check(FleetOverflow(cc).Length == 0, $"Flotte: keine Taste läuft über ({FleetOverflow(cc)})");
			_m.TestCloseCommand();
			return false;
		}
		// ── Flug: Ziel T wählen → „Verband greift an“ (zwei Tipps) ──
		int s7 = s6 + 28;
		if (f == s7)
		{
			Commands.Cancel(u, u.Ships[_foA]);   // A und D frei für den Verband (D wartet am Sammelpunkt)
			Commands.Cancel(u, u.Ships[_foD]);
			Commands.Cancel(u, u.Ships[_foC]);
			foreach (var id in new[] { _foA, _foC, _foD }) { var s = u.Ships[id]; s.DockedAt = default; s.Position = u.Ships[u.PlayerShip].Position + new Vec2(10f + id.Value % 5, 6f); }
			_m.TestSetShipTarget(_foT);
			_m.SyncHudForTest();
			return false;
		}
		if (f == s7 + 2)
		{
			var w = _m.HudRef.Flight.TestWingAttack;
			Check(w.IsVisibleInTree() && w.Sub.Contains("Schiff"), $"Zielkarte: „Verband greift an“ sichtbar ({w.Visible}, {w.Sub})");
			Check(w.Overflow().Length == 0, $"„Verband greift an“ passt ({w.Overflow()})");
			TapNode(w, true);
			return false;
		}
		if (f == s7 + 3) { if (_p28Press is { } p) Tap(p, false); return false; }
		if (f == s7 + 5) { TapNode(_m.HudRef.Flight.TestWingAttack, true); return false; }
		if (f == s7 + 6) { if (_p28Press is { } p) Tap(p, false); return false; }
		if (f == s7 + 8)
		{
			int n = new[] { _foA, _foC, _foD }.Count(id => u.Ships[id].Command is AttackCommand ac && ac.Ship == _foT);
			Check(n >= 2, $"„Verband greift an“ per zwei Tipps: {n} Schiffe greifen an");
			_p28Events = new List<SpaceSim.Core.Events.SimEvent>();
			u.Events.Raised += _p28Events.Add;
			_m.Session.RunTicks(120);
			return false;
		}
		if (f == s7 + 10)
		{
			Check(new[] { _foA, _foC, _foD }.Any(id => u.Ships.ContainsKey(id) && u.Fights.IsEngaged(id)) || !u.Ships.ContainsKey(_foT),
				"Flottenschiffe stellen das Ziel (Gefecht)");
			Check(_p28Events!.Any(e => e is SpaceSim.Core.Events.CrimeReported c && c.Actor == u.PlayerActor), "Angriff außerhalb eines Kriegs ist ein Verbrechen (Fahndung)");
			return true;
		}
		return false;
	}
}
