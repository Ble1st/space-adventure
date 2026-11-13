using System.Linq;
using Godot;
using SpaceAdventure.World;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe Phase 31 (eigene Datei): Tor angreifen im Kriegstest (Bollwerk) — Tor wählen, „Tor angreifen“ (zwei Tipps), Schüsse treffen das Tor,
/// Verbrechen bei beiden Endfraktionen, Zielkarte; Tor außer Betrieb (geschlossenes Modell, Zustandsschild, Routengrund), Wiederanlauf nach der Frist.
/// </summary>
public partial class SelfTest
{
	GateId _gcGate;
	System.Collections.Generic.List<SimEvent>? _gcEv;

	/// <summary>Rückgabe true = Stufe fertig.</summary>
	bool GateCombatStage(int f)
	{
		var u = _m.Session.U;
		switch (f)
		{
			case 1: _m.TestMenu("new:war"); return false;
			case 6:
			{
				u.Rules.GateCombat = true;
				var me = u.Ships[u.PlayerShip];
				var gate = u.Sectors[u.PlayerSector].Gates.Select(id => u.Gates[id]).OrderBy(g => g.Id.Value).First(g => GateCombat.TypeOf(u, g) is not null);
				_gcGate = gate.Id;
				// heran ans Tor (30 m), Nase drauf
				var at = gate.Position + new Vec2(0f, 40f);
				me.Position = at;
				_m.Player.Position = new Vector3(at.X, 0f, at.Y);
				_m.TestFace(gate.Position);
				_gcEv = new();
				u.Events.Raised += _gcEv.Add;
				_m.TestArmGate(gate.Id);
				Check(_m.TestAtGate, "„Tor angreifen“ macht das Tor zum Angriffsziel");
				return false;
			}
			case 200:
			{
				var gate = u.Gates[_gcGate];
				Check(_gcEv!.OfType<ShotAtGate>().Any(s => s.Gate == _gcGate), $"Schüsse treffen das Tor ({_gcEv!.OfType<ShotAtGate>().Count()})");
				Check(_gcEv!.OfType<GateAttacked>().Any(), "Torangriff gemeldet");
				foreach (var fac in GateCombat.DefendersOf(u, gate))
					Check(Wanted.LevelOf(u, u.PlayerActor, fac) >= 3, $"Verbrechen bei {u.Factions[fac].Name}");
				_m.SyncHudForTest();
				Check(_m.HudRef.Flight.TestTargetName.Contains("Tor"), $"Zielkarte zeigt das Tor ({_m.HudRef.Flight.TestTargetName})");
				Check(_m.HudRef.Flight.TestTargetCombat.Contains("Hülle"), $"Zielkarte zeigt Hülle/Schild ({_m.HudRef.Flight.TestTargetCombat})");
				// außer Betrieb setzen (Rest der Hülle in einem Treffer — die Dauer prüfen die Core-Tests)
				_m.TestFight.AutoFire = false;
				GateCombat.Apply(u, gate, new HitResult(true, 1f, 0f, 0f, GateCombat.MaxHull(u, gate) + 1f), u.PlayerActor);
				Check(GateCombat.IsDown(u, gate) && gate.State == GateState.Closed && u.Gates[gate.Target].State == GateState.Closed, "Tor außer Betrieb: beide Hälften geschlossen");
				foreach (var fe in u.Fights.Encounters.ToList()) foreach (var part in fe.Participants.Where(x => x.Active).ToList()) Engage.Leave(u, fe, part, CombatExit.Fled);
				return false;
			}
			case 204:
			{
				var gate = u.Gates[_gcGate];
				var node = _m.TestView.GateModelNodes().FirstOrDefault(n => n.Id == _gcGate);
				if (node.Model is not null) Check(node.Look == GateModels.Look.Closed, $"Tormodell zeigt „geschlossen“ ({node.Look})");
				Check(_m.TestView.GateStatusText(_gcGate).Contains("AUSSER BETRIEB"), $"Zustandsschild am Tor ({_m.TestView.GateStatusText(_gcGate)})");
				Check(_m.LastWarMessage.Contains("außer Betrieb"), $"Meldung zum Ausfall ({_m.LastWarMessage})");
				var far = u.Gates[gate.Target].Sector;
				string? why = GateAccess.BlockReason(u, u.Ships[u.PlayerShip], u.PlayerSector, far);
				// Ringtor: es gibt einen Umweg — ohne Umweg nennt die Routenplanung den Ausfall
				Check(why is null || why.Contains("außer Betrieb"), $"Routengrund ({why})");
				Check(AccessInfo.ForGate(u, gate, u.Ships[u.PlayerShip]).State.Contains("außer Betrieb"), "Torkarte zeigt außer Betrieb");
				// Wiederanlauf nach der Frist (Notregel bzw. Reparatur)
				_m.Session.RunTicks(u.Rules.GateMaxOutageTicks + 100);
				return false;
			}
			case 210:
			{
				var gate = u.Gates[_gcGate];
				Check(!GateCombat.IsDown(u, gate) && MapParts.Passable(gate), "Tor läuft wieder");
				Check(_gcEv!.OfType<GateRestored>().Any(), "Wiederanlauf gemeldet");
				if (_gcEv is not null) u.Events.Raised -= _gcEv.Add;
				return true;
			}
		}
		return false;
	}
}
