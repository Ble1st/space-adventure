using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 172 (Welle A): Angriffsbefehl der Flotte über echte Tipps (Schiff → „Angreifen“ → Station) — nach 200 Ticks Belagerung genau ein
/// Verbrechen und eine Angriffsmeldung (vorher eine je Tick). Danach im Krieg erteilter Angriff: Frieden → der Befehl endet von selbst.
/// </summary>
public partial class SelfTest
{
	ShipId _waA, _waB;
	StationId _waSt;
	List<SpaceSim.Core.Events.SimEvent>? _waEvents;
	int _waWait;

	Ui.Kit.KitButton? WaStationTarget(Universe u)
	{
		// Stationsziel mit den wenigsten Geschützen (Wirtschaftsstation, kein Regierungssitz) — Reihenfolge wie die Zielliste
		var list = FleetTargets.Attack(u, u.Ships[_waA]);
		int best = -1, guns = int.MaxValue;
		for (int i = 0; i < list.Count; i++)
			if (list[i].Command is AttackCommand { Station.Value: > 0 } a && u.Stations.TryGetValue(a.Station, out var st))
			{
				int g = (SpaceSim.Core.Combat.StationCombat.DefenseOf(u, st)?.Guns.Count ?? 0) + (StationProtection.IsOfficial(u, st) ? 100 : 0);
				if (g < guns) { guns = g; best = i; }
			}
		return best >= 0 ? _m.HudRef.CcFleet.TestTargetButton(best) : null;
	}

	bool WelleAStage(int f)
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
				u.WalletOf(u.Ships[u.PlayerShip]).Credits = 500000f;
				var me = u.Ships[u.PlayerShip];
				var yard = u.StationsById().Where(s => s.Sector == me.Sector && !s.IsBuildSite && Docking.Allows(u, s, me)).OrderBy(s => Vec2.Distance(s.Position, me.Position)).First();
				_waA = FoOwn(u, "falcon", yard).Id;
				_waB = FoOwn(u, "falcon", yard).Id;
				_m.TestOpenCommand();
				return false;
			}
			case 6: PressButton(_m.HudRef.Command, "Kommando", true); return false;
			case 7: PressButton(_m.HudRef.Command, "Kommando", false); return false;
			case 9: PressButton(cc, "Flotte", true); return false;
			case 10: PressButton(cc, "Flotte", false); return false;
			case 12: Check(_m.TestScreenOpen("cc.fleet"), "Welle A: Kommando → Flotte offen"); return false;
		}
		int s0 = 14;
		if (f >= s0 && f < s0 + 4) { FoTap(f, s0, () => FoRowButton(cc, FoRow(u, _waA)), "Schiff A"); return false; }
		if (f >= s0 + 5 && f < s0 + 9) { FoTap(f, s0 + 5, () => fs.TestCommandButton(CommandKinds.Attack), "Angreifen"); return false; }
		if (f >= s0 + 10 && f < s0 + 14) { FoTap(f, s0 + 10, () => WaStationTarget(u), "Stationsziel"); return false; }
		if (f == s0 + 15)
		{
			bool ok = u.Ships[_waA].Command is AttackCommand ac && ac.Station.Value != 0;
			Check(ok, $"Welle A: Angreifen einer Station per drei Tipps ({fs.TestMessage})");
			if (!ok) return true;
			_waSt = ((AttackCommand)u.Ships[_waA].Command!).Station;
			_waEvents = new List<SpaceSim.Core.Events.SimEvent>();
			u.Events.Raised += _waEvents.Add;
			_m.TestCloseCommand();
			_waWait = 0;
			return false;
		}
		if (f == s0 + 17)
		{
			// bis zur Belagerung (Anflug), dann 200 Ticks Belagerung
			while (!u.Fights.IsEngaged(_waA) && _waWait < 4000 && u.Ships.ContainsKey(_waA)) { _m.Session.RunTicks(20); _waWait += 20; }
			Check(u.Fights.IsEngaged(_waA), $"Welle A: Belagerung beginnt (nach {_waWait} Ticks)");
			_m.Session.RunTicks(200);
			return false;
		}
		if (f == s0 + 19)
		{
			int crimes = _waEvents!.Count(e => e is SpaceSim.Core.Events.CrimeReported c && c.Actor == u.PlayerActor && c.Crime == "assault_station");
			int started = _waEvents!.Count(e => e is SpaceSim.Core.Combat.FleetAttackStarted fa && fa.Ship == _waA);
			Check(crimes == 1, $"Welle A: nach 200 Ticks Belagerung genau ein Verbrechen ({crimes})");
			Check(started == 1, $"Welle A: genau eine Angriffsmeldung ({started})");
			if (u.Ships.TryGetValue(_waA, out var a)) Commands.Cancel(u, a);
			// Krieg mit dem Besitzer der Station, Angriff von B im Krieg
			var owner = u.FactionOf(u.Stations[_waSt].Owner);
			Check(owner is not null, "Welle A: Stationsbesitzer hat eine Fraktion");
			if (owner is null) return true;
			if (Wars.PlayerWar(u, owner.Value) is null) Wars.PlayerDeclare(u, owner.Value);
			var b = u.Ships[_waB];
			Check(Commands.Give(u, b, new AttackCommand(default, _waSt, default, u.Stations[_waSt].Sector), out var msg), $"Welle A: Angriff im Krieg ({msg})");
			Check(b.Command is AttackCommand { War: true }, "Welle A: Befehl merkt sich den Krieg");
			_m.Session.RunTicks(60);
			Wars.MakePeace(u, Wars.PlayerWar(u, owner.Value)!, "negotiated");
			_waWait = 0;
			return false;
		}
		if (f == s0 + 21)
		{
			while (u.Ships.TryGetValue(_waB, out var b) && b.Command is AttackCommand && _waWait < 4000) { _m.Session.RunTicks(20); _waWait += 20; }
			Check((!u.Ships.TryGetValue(_waB, out var b2) || b2.Command is not AttackCommand) && _waWait <= 100, $"Welle A: Frieden beendet den Angriffsbefehl (nach {_waWait} Ticks)");
			if (_waEvents is not null) u.Events.Raised -= _waEvents.Add;
			return true;
		}
		return false;
	}
}
