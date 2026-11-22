using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.World;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 125 (Runde 44h, Startpaket „Bergbautest“): der eigene kleine Fels (25 Erz) ist ohne Eingabe in &lt; 15 s leer und zerbricht sichtbar,
/// „Auto-Mining“ verankert danach am zweiten Fels daneben; die zwei KI-Bohrschiffe (S und L) fliegen selbst ohne Sprung an ihren Fels, ankern mit kurzen
/// Seilen (≤ ¼ Schiffslänge) und bohren — der große Bohrkopf dreht sich.
/// </summary>
public partial class SelfTest
{
	int _r44hRock, _r44hFrame0, _r44hDepletedFrame = -1, _r44hAutoFrame = -1;
	List<ShipId> _r44hNpcs = new();
	Dictionary<ShipId, Vec2> _r44hLast = new(), _r44hStart = new();
	Dictionary<ShipId, float> _r44hMaxStep = new();
	HashSet<ShipId> _r44hContact = new();
	float _r44hRotor0;

	bool Round44hStage(int f)
	{
		var u = _m.Session.U;
		if (f == 1) { _m.TestMenu("new:mining"); return false; }
		if (f < 4) return false;
		var me = u.Ships[u.PlayerShip];
		var sector = u.Sectors[u.PlayerSector];
		if (f == 4)
		{
			var rock = sector.Asteroids.Where(a => a.Minable).OrderBy(a => Vec2.Distance(a.Position, me.Position) - a.Radius).First();
			_r44hRock = rock.Id;
			Check(rock.Ore <= 25.01f && rock.Radius <= 3.01f && !u.Rules.PlayerRaids, $"Bergbautest: kleiner Fels mit {rock.Ore:0} Erz (Radius {rock.Radius:0.0}), ruhig");
			_r44hNpcs = u.ShipsById().Where(s => s.Controller == Controller.Ai && s.Role == ShipRole.Miner && s.Sector == u.PlayerSector && s.MineAsteroid != 0
			                                      && Vec2.Distance(s.Position, me.Position) <= 61f).Select(s => s.Id).ToList();
			Check(_r44hNpcs.Count == 2, $"Bergbautest: zwei KI-Bohrschiffe 40–60 m entfernt ({string.Join(",", _r44hNpcs.Select(i => u.Ships[i].ModelKey))})");
			foreach (var id in _r44hNpcs) { _r44hStart[id] = _r44hLast[id] = u.Ships[id].Position; _r44hMaxStep[id] = 0f; }
			_r44hFrame0 = f;
			u.Events.Raised += e => { if (e is AsteroidDepleted d && d.Asteroid == _r44hRock && _r44hDepletedFrame < 0) _r44hDepletedFrame = _frame - _stageStart; };
			return false;
		}
		// KI-Anflug: Schrittweite je Tick ≤ Tempo bis zum Kontakt
		foreach (var id in _r44hNpcs)
		{
			if (!u.Ships.TryGetValue(id, out var n) || _r44hContact.Contains(id)) continue;
			_r44hMaxStep[id] = Mathf.Max(_r44hMaxStep[id], Vec2.Distance(_r44hLast[id], n.Position));
			_r44hLast[id] = n.Position;
			int rid = n.AnchorRock != 0 ? n.AnchorRock : n.MineAsteroid;
			if (sector.Asteroids.FirstOrDefault(a => a.Id == rid) is { } r && ContactMining.InContact(n.Position, (r.Position - n.Position).Normalized(), r, ContactMining.ToolReach(n, u)))
				_r44hContact.Add(id);
		}
		if (f == 600)
		{
			foreach (var id in _r44hNpcs)
			{
				var n = u.Ships[id];
				Check(_r44hContact.Contains(id) && Vec2.Distance(_r44hStart[id], n.Position) > 15f && _r44hMaxStep[id] <= Travel.SpeedOf(n) + 0.01f,
					$"Bergbautest: {n.ModelKey} fliegt selbst ohne Sprung an den Fels (Kontakt {_r44hContact.Contains(id)}, größter Schritt {_r44hMaxStep[id]:0.00} ≤ {Travel.SpeedOf(n):0.00})");
				Check(MiningAnchor.State(u, n) == AnchorState.Held, $"Bergbautest: {n.ModelKey} verankert ({MiningAnchor.State(u, n)})");
				var vis = _m.TestView.TestVisual(id);
				float shipLen = vis is null ? 1f : 2f * vis.Root.Scale.X;
				float rope = _m.TestAnchors.RopeLength(id);
				Check(vis is not null && _m.TestAnchors.MuzzlesOf(id).Count > 0 && rope > 0.05f && rope <= 0.6f * shipLen,
					$"Bergbautest: {n.ModelKey} kurze Seile ({rope:0.00} von {shipLen:0.00})");
				if (n.ModelKey == "strip_miner" && vis is not null) _r44hRotor0 = vis.RotorAngle;
			}
			return false;
		}
		if (f == 660)
		{
			var big = _r44hNpcs.Select(i => u.Ships[i]).FirstOrDefault(s => s.ModelKey == "strip_miner");
			var vis = big is null ? null : _m.TestView.TestVisual(big.Id);
			Check(vis is not null && vis.Rotors.Count > 0 && vis.RotorAngle > _r44hRotor0 + 1f, $"Bergbautest: große Schneidtrommel dreht sich ({_r44hRotor0:0.0} → {vis?.RotorAngle:0.0})");
		}
		if (_r44hDepletedFrame >= 0 && _r44hAutoFrame < 0 && f > _r44hDepletedFrame + 30)
		{
			Check((_r44hDepletedFrame - _r44hFrame0) / 60f < 15f, $"Bergbautest: eigener Fels nach {(_r44hDepletedFrame - _r44hFrame0) / 60f:0.0} s leer (< 15 s)");
			Check(_m.TestView.TestBreakupCount >= 1, "Bergbautest: Fels zerbricht sichtbar (Trümmer)");
			Check(me.UsedVolume(u) >= 24.5f, $"Bergbautest: 25 Erz im Laderaum ({me.UsedVolume(u):0.0})");
			_m.TestStartMining();
			_r44hAutoFrame = f;
			return false;
		}
		if (_r44hAutoFrame >= 0)
		{
			if (MiningAnchor.State(u, me) == AnchorState.Held && me.AnchorRock != _r44hRock && f >= 661)
			{
				Check(true, "Bergbautest: Auto-Mining verankert am zweiten Fels");
				_m.TestStopAutomation();
				return true;
			}
			if (f - _r44hAutoFrame > 60 * 20) { Check(false, $"Bergbautest: Auto-Mining verankerte nicht am zweiten Fels (Status {_m.TestMinerStatus}, Kontakt {_m.TestContactState})"); _m.TestStopAutomation(); return true; }
			return false;
		}
		if (f - _r44hFrame0 > 60 * 20) { Check(false, $"Bergbautest: eigener Fels nach 20 s nicht leer (Anker {MiningAnchor.State(u, me)}, Kontakt {_m.TestContactState}, Erz {sector.Asteroids.FirstOrDefault(a => a.Id == _r44hRock)?.Ore:0.0})"); return true; }
		return false;
	}
}
