using System.Linq;
using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>Runde 11: Bilder der Schiffsmodelle (97 Bergbau mit drehendem Werkzeug, 98 Gefecht mit Strahlen aus den Stellungen und Raketen aus den Schächten),
/// eigene Datei; Stufen 597_xxx, nur mit <c>--ui-shots-ships</c> oder im Anschluss an die Torbilder.</summary>
public partial class UiShots
{
	const int ShipShotsStart = 597_001;
	ShipId[] _ssFight = System.Array.Empty<ShipId>();

	void ShipShots()
	{
		var u = _m.Session.U;
		switch (_stage)
		{
			case 597_001:
				_m.TestMenu("new");   // Einheitsschürfer 100 mit Bohrkopf in Alpha
				_stage = 597_002; _wait = 240; break;
			case 597_002:
				_m.TestTouchNearestRock();
				_m.TestSetCam(35f, 30f, 28f);
				_stage = 597_003; _wait = 40; break;
			case 597_003: Capture("97-schiff-bohrt-am-fels.png"); _stage = 597_004; _wait = 5; break;
			case 597_004:
			{
				var owner = u.ShipsById().First(ThreatMap.IsRaider).Owner;
				var yard = u.StationsById().First(s => !s.IsBuildSite);
				var me = u.Ships[u.PlayerShip];
				Ship Make(string model, ShipRole role, ActorId who, Vec2 at)
				{
					var m = u.ShipModels[model];
					var s = ShipPurchase.Create(u, u.NewShipId(), model, u.ShipClasses[m.ClassKey].Slots, m.Fit, role, Controller.Ai, who, yard, 2000f);
					u.AddShip(s);
					if (s.Sector != u.PlayerSector) u.MoveShip(s, u.PlayerSector);
					s.DockedAt = default; s.Position = at;
					return s;
				}
				var p = me.Position + new Vec2(0f, -45f);
				var corvette = Make("corvette", ShipRole.Pirate, owner, p);
				var v1 = Make("hauler", ShipRole.Trader, u.FreeActor, p + new Vec2(28f, -8f));
				var bomber = Make("bomber", ShipRole.Pirate, owner, p + new Vec2(-30f, 5f));
				var v2 = Make("behemoth", ShipRole.Trader, u.FreeActor, p + new Vec2(-55f, -20f));
				foreach (var v in new[] { v1, v2 }) v.Cargo[u.FindGood("electronics")!.Id] = 20f;
				_ssFight = new[] { corvette.Id, v1.Id, bomber.Id, v2.Id };
				Engage.StartRaid(u, corvette, v1, u.Sectors[u.PlayerSector]);
				Engage.StartRaid(u, bomber, v2, u.Sectors[u.PlayerSector]);
				_stage = 597_010; _wait = 45; break;
			}
			case 597_010:
			{
				// Mitte der Gefechtsteilnehmer (räumlich, aus dem Core) — der Spieler steht knapp südlich, die Kamera blickt von hinten über ihn aufs Gefecht
				var pts = _ssFight.Select(id => _m.TestView.ShipPosition(id)).OfType<Vector3>().Select(v => new Vec2(v.X, v.Z)).ToList();
				var c = pts.Count == 0 ? u.Ships[u.PlayerShip].Position : pts.Aggregate(new Vec2(0f, 0f), (a, b) => a + b) * (1f / pts.Count);
				GD.Print($"[ui-shots] Gefecht: {pts.Count} Schiffe sichtbar um {c.X:0},{c.Y:0}");
				_m.TestTeleportTo(c + new Vec2(0f, 14f));
				_m.TestFace(c);
				_m.TestMenu("cam_reset");
				_stage = 597_005; _wait = 25; break;
			}
			case 597_005: Capture("98-gefecht-stellungen-und-raketen.png"); _stage = 597_006; _wait = 5; break;
			case 597_006:
				_m.TestMenu("cam_reset");
				_stage = 999_999; _wait = 5; break;
		}
	}
}
