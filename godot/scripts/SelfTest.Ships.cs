using System.Linq;
using Godot;
using SpaceAdventure.World;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>Selbsttest-Stufe der Schiffsmodelle (Runde 11, eigene Datei).</summary>
public partial class SelfTest
{
	ShipId _smGunner, _smBomber;
	int _smShotSlot = -1;
	float _smRotor;
	bool _smGuns = true;
	float _smPolice;
	System.Collections.Generic.List<SimEvent>? _smEvents;

	/// <summary>
	/// Schiffsmodelle (ShipModels): Spielerschiff und NPC-Marker zeigen ihr Modell im Maßstab aus ships.json, Akzent in der Besitzerfarbe, geteilte
	/// Überschreibungen; das Kontaktwerkzeug dreht am Fels; ein Schuss startet an der Mündung des feuernden Slots (≈ Hardpoint in Weltkoordinaten), eine Rakete am
	/// Schacht; Stationstürme drehen zum Ziel. Rückgabe true = Stufe fertig.
	/// </summary>
	bool ShipModelStage(int f)
	{
		var u = _m.Session.U;
		switch (f)
		{
			case 1: _m.TestMenu("new"); return false;   // Code-Standard: Einheitsschürfer 100 (Bohrkopf) in Alpha
			case 6:
			{
				var me = u.Ships[u.PlayerShip];
				var pv = _m.Player.Visual;
				Check(pv is not null, $"Spielerschiff zeigt sein Modell ({me.ModelKey})");
				if (pv is not null)
				{
					Check(Mathf.Abs(pv.Root.Scale.X - ShipModels.ScaleOf(me.ModelKey)) < 1e-3f, $"Maßstab aus ships.json ({pv.Root.Scale.X:0.00})");
					Check(pv.Rotors.Count > 0, $"Bohrkopf als drehender Rotor ({pv.Rotors.Count})");
				}
				var (models, simple) = _m.TestView.TestBodies();
				Check(models > 0, $"NPC-Schiffe mit Modell ({models} Modelle, {simple} Grundkörper)");
				int over = ShipModels.OverrideCount;
				Check(over < 120, $"Überschreibungen geteilt, nicht je Schiff ({over})");
				// Akzent in der Besitzerfarbe
				var npc = u.Ships.Values.Where(s => s.Sector == u.PlayerSector && s.Id != u.PlayerShip && _m.TestView.TestVisual(s.Id) is not null).OrderBy(s => s.Id.Value).FirstOrDefault();
				if (npc is not null && _m.TestView.TestVisual(npc.Id) is { } nv)
				{
					var mi = nv.Accents.Select(a => a.Mesh.GetSurfaceOverrideMaterial(a.Surface)).OfType<BaseMaterial3D>().FirstOrDefault();
					var want = ShipModels.AccentOf(u, npc.Owner);
					Check(mi is not null && (want.S <= 0.2f ? mi.AlbedoColor.R == want.R : Mathf.Abs(mi.AlbedoColor.H - want.H) < 0.02f), $"Akzent in der Besitzerfarbe ({npc.ModelKey})");
				}
				// Kontaktabbau: Bug an den Fels, das Werkzeug dreht
				_m.TestTouchNearestRock();
				_smRotor = pv?.RotorAngle ?? 0f;
				return false;
			}
			case 30:
			{
				var pv = _m.Player.Visual;
				Check(_m.TestContactState == SpaceSim.Core.Mining.ContactState.Mining, $"Kontakt am Fels ({_m.TestContactState})");
				Check(pv is not null && pv.RotorAngle > _smRotor + 1f, $"Werkzeug dreht beim Abbau ({_smRotor:0.0} → {pv?.RotorAngle:0.0} rad)");
				Check(!_m.TestLabelsOverlap(), "sichtbare Namensschilder überlappen sich nicht (Runde 12: das fernere wird ausgeblendet)");
				// Gefecht im Spielersektor: Korsar (Bugrohr) und Donner (Raketenschächte) überfallen zwei beladene Händler
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
				var basePos = me.Position + new Vec2(60f, 60f);
				var gunner = Make("corsair", ShipRole.Pirate, owner, basePos);
				var v1 = Make("unit_trader_100", ShipRole.Trader, u.FreeActor, basePos + new Vec2(25f, 0f));
				var bomber = Make("bomber", ShipRole.Pirate, owner, basePos + new Vec2(0f, 40f));
				var v2 = Make("unit_trader_100", ShipRole.Trader, u.FreeActor, basePos + new Vec2(60f, 40f));
				foreach (var v in new[] { v1, v2 }) { v.Cargo[u.FindGood("electronics")!.Id] = 20f; for (int i = 0; i < v.Fit.Count; i++) if (v.Fit[i] is { } k && u.Modules[k].IsWeapon) v.Fit[i] = null; v.RecomputeStats(u); }
				_smGunner = gunner.Id; _smBomber = bomber.Id; _smShotSlot = -1;
				// Runde 12: Stationsgeschütze (Hauptquartier mit sechs Geschützen) und eingreifende Streifen beendeten das Gefecht im vollen Lauf, bevor beide schossen —
				// für diese Prüfung aus, die Kaperer fliehen nicht (Regel „Angriff“)
				_smGuns = u.Rules.StationGunsActive; _smPolice = u.Rules.PoliceResponseMax;
				u.Rules.StationGunsActive = false; u.Rules.PoliceResponseMax = 0f;
				gunner.Engagement = Engagement.Attack; bomber.Engagement = Engagement.Attack;
				_smEvents = new();
				u.Events.Raised += _smEvents.Add;
				Engage.StartRaid(u, gunner, v1, u.Sectors[u.PlayerSector]);
				Engage.StartRaid(u, bomber, v2, u.Sectors[u.PlayerSector]);
				return false;
			}
			default:
			{
				if (f < 31) return false;
				var cv = _m.TestCombatView;
				// Schuss des Korsaren: Strahl beginnt an der Mündung des feuernden Slots
				if (_smShotSlot < 0 && _smEvents!.OfType<ShotFired>().LastOrDefault(x => x.Shooter == _smGunner && x.Slot >= 0) is { } shot) _smShotSlot = shot.Slot;
				var e = u.Fights.Of(_smBomber);
				var flight = e?.Missiles.FirstOrDefault(x => x.Shooter == _smBomber && x.Slot >= 0);
				bool beamOk = _smShotSlot >= 0 && cv.LastBeamFrom is not null, missileOk = flight is not null && cv.MissileStarts.ContainsKey(_smBomber);
				if (beamOk && missileOk || f > 900)
				{
					Check(_smShotSlot >= 0, "Korsar feuert mit Slot im Ereignis");
					if (_smShotSlot >= 0 && _m.TestView.TestVisual(_smGunner) is { } gv && gv.Hardpoints.Def(_smShotSlot) is { } hp)
					{
						var expected = gv.Root.GlobalTransform * hp.Pos;   // starres Bugrohr: Hardpoint im Modellraum → Welt
						var muzzle = _m.TestView.Muzzle(_smGunner, _smShotSlot);
						Check(muzzle is { } mz && mz.DistanceTo(expected) < 0.05f, $"Mündung = Hardpoint in Weltkoordinaten ({muzzle} ≈ {expected})");
						Check(muzzle is { } m2 && m2.DistanceTo(gv.Root.GlobalPosition) > 0.3f, "Mündung liegt nicht in der Schiffsmitte");
					}
					else Fail("Korsar ohne Modell oder Stellung");
					Check(cv.LastBeamFrom is not null, "Strahl gezeichnet");
					Check(flight is not null, "Donner startet eine Rakete mit Slot");
					if (flight is not null)
					{
						var (mslot, start) = cv.MissileStarts.TryGetValue(_smBomber, out var st0) ? st0 : (-1, Vector3.Zero);
						var at = _m.TestView.Muzzle(_smBomber, mslot);
						var dir = _m.TestView.MuzzleDirection(_smBomber, mslot);
						Check(mslot >= 0 && at is { } a && a.DistanceTo(start) < 0.5f, $"Rakete des Donners startet am Schacht von Slot {mslot} ({start} ≈ {at})");
						Check(dir is { } d && d.Y > 0.5f, $"Schacht zeigt nach oben ({dir})");
					}
					// Stationsturm: dreht zum Ziel, die Mündung wandert mit
					var gunStation = u.Sectors[u.PlayerSector].Stations.Select(id => (id, set: _m.TestView.TestStationGuns(id))).FirstOrDefault(x => x.set is { TurretCount: > 0 });
					Check(gunStation.set is not null, "Station mit Geschützturm im Sektor");
					if (gunStation.set is { } gs)
					{
						int gi = gs.Indices.First();
						var before = gs.Muzzle(gi);
						var c = before ?? Vector3.Zero;
						gs.Aim(gi, c + new Vector3(0, 0, 200f), 0.1f, 0f);
						var a1 = gs.Muzzle(gi);
						gs.Aim(gi, c + new Vector3(200f, 0, 0f), 0.1f, 0f);
						var a2 = gs.Muzzle(gi);
						Check(a1 is { } p1 && a2 is { } p2 && p1.DistanceTo(p2) > 0.05f, "Stationsturm dreht zum Ziel (Mündung wandert)");
					}
					if (_smEvents is not null) u.Events.Raised -= _smEvents.Add;
					u.Rules.StationGunsActive = _smGuns; u.Rules.PoliceResponseMax = _smPolice;
					// Aufräumen: Gefechte beenden
					foreach (var id in new[] { _smGunner, _smBomber })
						if (u.Fights.Of(id) is { } fe && fe.Of(id) is { } fp) Engage.Leave(u, fe, fp, CombatExit.Fled);
					return true;
				}
				return false;
			}
		}
	}
}
