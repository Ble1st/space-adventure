using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>Bilder der Jumpgate-Modelle (93–96: offen, eingeschränkt, geschlossen, zerstört), eigene Datei; Stufen 595_xxx.</summary>
public partial class UiShots
{
	const int GateShotsStart = 595_001;
	GateId _gsGate;

	static readonly (GateState State, string File)[] GateShotList =
	{
		(GateState.Open, "93-tor-offen.png"), (GateState.Restricted, "94-tor-eingeschraenkt.png"),
		(GateState.Closed, "95-tor-geschlossen.png"), (GateState.Destroyed, "96-tor-zerstoert.png"),
	};

	/// <summary>Eine Stufe der Torbilder; Stufen 595_001 (Aufbau) und je Bild 595_010 + 2·i (Zustand setzen) / + 1 (Aufnahme).</summary>
	void GateShots()
	{
		var u = _m.Session.U;
		if (_stage == GateShotsStart)
		{
			_m.TestMenu("new");
			u = _m.Session.U;
			var sector = u.Sectors[u.PlayerSector];
			// das Tor mit dem meisten Abstand zu anderen Toren und Stationen (übersichtliches Bild)
			var g = sector.Gates.Select(id => u.Gates[id]).OrderByDescending(x =>
				sector.Gates.Where(o => o != x.Id).Select(o => Vec2.Distance(u.Gates[o].Position, x.Position))
					.Concat(sector.Stations.Select(st => Vec2.Distance(u.Stations[st].Position, x.Position))).DefaultIfEmpty(999f).Min()).First();
			_gsGate = g.Id;
			// Schiff vor die Öffnung (von innen), Kamera schräg dahinter und seitlich, so dass Tunnel und Zollstation ganz im Bild sind
			float yaw = World.GateModels.YawOf(u, g);
			var outward = new Vec2(Mathf.Sin(yaw), Mathf.Cos(yaw));
			var side = new Vec2(outward.Y, -outward.X);                       // Modell +X (Zollstation)
			var ship = g.Position - outward * 26f - side * 6f;
			_m.TestTeleportTo(ship);
			_m.TestFace(g.Position);
			var cam = outward * -0.8f - side * 0.6f;                         // Blick von innen und von der stationsfernen Seite
			_m.TestSetCam(Mathf.RadToDeg(Mathf.Atan2(cam.X, cam.Y)), 32f, 95f);
			_stage = 595_010; _wait = 240;   // Meldung „Neues Spiel“ abwarten
			return;
		}
		int i = (_stage - 595_010) / 2;
		if (i >= GateShotList.Length)
		{
			GateAccess.SetState(u, u.Gates[_gsGate], GateState.Open);
			_m.TestMenu("cam_reset");
			_stage = 999_999; _wait = 5;
			return;
		}
		var (state, file) = GateShotList[i];
		if ((_stage - 595_010) % 2 == 0)
		{
			GateAccess.SetState(u, u.Gates[_gsGate], state);
			_m.TestView.SyncGates(u, force: true);
			_stage++; _wait = 12;
		}
		else
		{
			Capture(file);
			_stage++; _wait = 5;
		}
	}
}
