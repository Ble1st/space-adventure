using System.Collections.Generic;
using System.Linq;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Weapons;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 99 (Runde 20): Zielwechsel Tor → Schiff → Tor → Station → Tor halten Zielkarte, Angriffstasten und Kontexttaste stimmig; die leere Zielkarte
/// passt; Auto-Mining übernimmt einen bestehenden Anker (kein „Anker fliegt“); nach „Lösen“ mit Bug am Fels wird nicht von selbst neu verankert — die Taste heißt
/// „Ankern“ und setzt die Harpunen wieder.
/// </summary>
public partial class SelfTest
{
	GateId _r20Gate; StationId _r20Station, _r20Near; ShipId _r20Ship;
	List<SimEvent>? _r20Ev;

	bool Round20Stage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef.Flight;
		switch (f)
		{
			case 1:
				_m.TestMenu("new:war");
				return false;
			case 5:
			{
				_m.SyncHudForTest();
				Check(hud.TestTargetLineOverflow.Length == 0, $"leere Zielkarte passt ({hud.TestTargetLineOverflow})");
				var me = u.Ships[u.PlayerShip];
				var sector = u.Sectors[u.PlayerSector];
				_r20Gate = sector.Gates.Select(id => u.Gates[id]).OrderBy(g => g.Id.Value).First(g => GateCombat.TypeOf(u, g) is not null).Id;
				_r20Station = u.Stations.Values.First(s => s.Name == "Sektor-HQ Beta").Id;
				var npc = u.ShipsById().FirstOrDefault(s => s.Sector == u.PlayerSector && !s.IsPlayer && s.Transit is null && s.DockedAt.Value == 0 && !u.Fights.IsEngaged(s.Id))
					?? u.ShipsById().First(s => s.Role == ShipRole.Trader && !s.IsPlayer && !u.Fights.IsEngaged(s.Id));
				if (npc.Transit is not null) u.FinishTransit(npc);
				if (npc.Sector != u.PlayerSector) u.MoveShip(npc, u.PlayerSector);
				npc.Position = me.Position + new Vec2(25f, 0f); npc.DockedAt = default; npc.Order = null; npc.NextThinkTick = u.Tick + 100000;
				_r20Ship = npc.Id;
				var gate = u.Gates[_r20Gate];
				string gname = GateCombat.NameOf(u, gate);
				// Tor → Schiff
				_m.TestSelectGate(_r20Gate); _m.SyncHudForTest();
				Check(hud.TestGateAttackVisible, "Tor gewählt: „Tor angreifen“ sichtbar");
				Check(_m.TestSelectShip(_r20Ship), "Schiff als Ziel gültig");
				_m.SyncHudForTest();
				Check(!hud.TestGateAttackVisible && !hud.TestStationAttackVisible && hud.TestTargetName.Contains($"#{_r20Ship.Value}"),
					$"Tor → Schiff: kein „Tor angreifen“, Zielkarte zeigt das Schiff ({hud.TestTargetName}, Tor {hud.TestGateAttackVisible})");
				// Schiff → Tor
				_m.TestSelectGate(_r20Gate); _m.SyncHudForTest();
				Check(_m.TestFight.Target.Value == 0 && hud.TestGateAttackVisible && hud.TestTargetName.Contains("Tor"),
					$"Schiff → Tor: Zielkarte zeigt das Tor, „Tor angreifen“ ({hud.TestTargetName}, {gname})");
				// Tor → Station
				_m.TestSelectStation(_r20Station); _m.SyncHudForTest();
				Check(!hud.TestGateAttackVisible && hud.TestStationAttackVisible && hud.TestTargetName.Contains("Sektor-HQ"),
					$"Tor → Station: „Station angreifen“, kein „Tor angreifen“ ({hud.TestTargetName})");
				// Station → Tor
				_m.TestSelectGate(_r20Gate); _m.SyncHudForTest();
				Check(hud.TestGateAttackVisible && !hud.TestStationAttackVisible && hud.TestTargetName.Contains("Tor"),
					$"Station → Tor: „Tor angreifen“, kein „Station angreifen“ ({hud.TestTargetName})");
				// Kontexttaste (Handy-Test: mit gewähltem Tor blieb „Andocken: <Station>“): am Tor neben einer Station — Station gewählt → Andocken, Tor gewählt → Springen
				_m.TestTeleportTo(gate.Position + new Vec2(0f, 4f));
				return false;
			}
			case 8:
			{
				_m.SyncHudForTest();
				var me = u.Ships[u.PlayerShip];
				var near = u.Stations.Values.Where(s => s.Sector == u.PlayerSector && s.Dockable).OrderBy(s => Vec2.Distance(s.DockPoint(me.Id), me.Position)).First();
				_r20Near = near.Id;
				_m.TestSelectGate(_r20Gate); _m.SyncHudForTest();
				Check(hud.TestContextLabel.Contains("Spring"), $"am Tor mit gewähltem Tor: Springen ({hud.TestContextLabel})");
				// Runde 21 (Prüferhinweis: Fall war zu lax): dieselbe Stelle, die Station neben dem Tor gewählt → „Andocken: <Station>“
				_m.TestSelectStation(near.Id); _m.SyncHudForTest();
				Check(hud.TestContextLabel.Contains("Andock") && hud.TestContextLabel.Contains(near.Name.Split(' ')[0]),
					$"am Tor mit gewählter Station daneben: Andocken ({hud.TestContextLabel}, {near.Name})");
				_m.TestTeleportTo(near.DockPoint(me.Id) + new Vec2(0f, 2f));
				return false;
			}
			case 11:
				_m.SyncHudForTest();
				Check(hud.TestContextLabel.Contains("Andock"), $"an der gewählten Station: Andocken ({hud.TestContextLabel})");
				_m.TestSelectStation(_r20Near); _m.SyncHudForTest();
				Check(!hud.TestGateAttackVisible, $"Station gewählt: kein „Tor angreifen“ ({hud.TestContextLabel})");
				_m.TestMenu("new:anchor");
				return false;
			case 200:
			{
				var me = u.Ships[u.PlayerShip];
				Check(MiningAnchor.State(u, me) == AnchorState.Held, "Ankertest verankert");
				_r20Ev = new(); u.Events.Raised += _r20Ev.Add;
				_m.TestStartMining();
				return false;
			}
			case 260:
			{
				var me = u.Ships[u.PlayerShip];
				Check(MiningAnchor.State(u, me) == AnchorState.Held && !_r20Ev!.Any(e => e is AnchorFired af && af.Ship == me.Id || e is AnchorReleased ar && ar.Ship == me.Id) && _m.TestContactState != ContactState.Anchoring,
					$"Auto-Mining übernimmt den bestehenden Anker ({string.Join(",", _r20Ev!.Where(e => e is AnchorFired or AnchorReleased).Select(e => e.ToString()))}, {_m.TestContactState}, {_m.TestMinerStatus})");
				// „Lösen“ (beendet auch Auto-Mining)
				int bay = _m.TestBayKeys.ToList().IndexOf(WeaponBays.Contact.Key);
				_m.TestPressBay(bay);
				return false;
			}
			case 320:   // 1 s später: Bug am Fels, aber kein selbsttätiger Neuanker
			{
				var me = u.Ships[u.PlayerShip];
				_m.SyncHudForTest();
				Check(me.AnchorRock == 0 && !_m.TestMinerEnabled && _m.TestContactState == ContactState.Released,
					$"nach „Lösen“ kein Neuanker ({me.AnchorRock}, {_m.TestContactState})");
				Check(_m.TestBayNames.Contains("Ankern") && _m.TestBayDetails.Contains("gelöst") && hud.TestBayOverflow.Length == 0,
					$"Taste „Ankern“ · „gelöst“ ({string.Join("|", _m.TestBayNames)} / {string.Join("|", _m.TestBayDetails)})");
				_m.TestPressBay(_m.TestBayKeys.ToList().IndexOf(WeaponBays.Contact.Key));
				return false;
			}
			case 560:
			{
				var me = u.Ships[u.PlayerShip];
				Check(MiningAnchor.State(u, me) == AnchorState.Held, $"„Ankern“ setzt die Harpunen neu ({_m.TestContactState})");
				if (_r20Ev is not null) u.Events.Raised -= _r20Ev.Add;
				return true;
			}
		}
		return false;
	}
}
