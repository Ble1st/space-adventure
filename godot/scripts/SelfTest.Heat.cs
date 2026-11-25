using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 164 (Hitze und Wärmeabfuhr): Falke vor einem Korsaren; echte Tipps auf „FEUER“, bis das Schiff heiß ist → Hitzezeile im Status (im Feld,
/// nichts verdeckt), Warnzeile und Meldung sichtbar; ein weiterer Tipp feuert nicht (Waffen gesperrt); nach dem Abkühlen feuert ein Tipp wieder.
/// Die Hitze je Schuss ist für den Test erhöht (die Regel selbst prüfen die Core-Tests <c>HitzeTests</c>).
/// </summary>
public partial class SelfTest
{
	ShipId _htRaider;
	List<SimEvent>? _htEvents;
	Vector2 _htAt;
	int _htTapAt = -1, _htLockedFrame = -1, _htShotsAtLock = -1, _htCoolFrame = -1;
	bool _htCooled;

	int HtPlayerShots(Universe u) => _htEvents!.Count(e => e is ShotFired sf && sf.Shooter == u.PlayerShip || e is MissileLaunched ml && ml.Shooter == u.PlayerShip);

	bool HeatStage(int f)
	{
		var u = _m.Session.U;
		var fl = _m.HudRef.Flight;
		if (f == 1) { _m.TestMenu("new:fighter"); return false; }
		if (f == 8)
		{
			var me = u.Ships[u.PlayerShip];
			if (_m.TestDocked) _m.TestUndock();
			Check(u.Rules.ShipHeat, "Hitze: in der Standardwelt an");
			u.Rules.PlayerRaids = false;
			u.Rules.HeatPerDamageEnergy = 3f; u.Rules.HeatPerDamageKinetic = 3f;   // Test: wenige Schüsse bis heiß
			var pirate = u.Actors.Values.OrderBy(a => a.Id.Value).First(a => a.Kind == ActorKind.Outlaw);
			var m = u.ShipModels["corsair"];
			var yard = u.StationsById().First(s => !s.IsBuildSite);
			var raider = ShipPurchase.Create(u, u.NewShipId(), "corsair", u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Pirate, Controller.Ai, pirate.Id, yard, 0f);
			u.AddShip(raider);
			if (raider.Sector != me.Sector) u.MoveShip(raider, me.Sector);
			raider.DockedAt = default;
			float h = _m.TestHeadingRad;
			raider.Position = me.Position + new Vec2(-Mathf.Sin(h), -Mathf.Cos(h)) * 25f;
			raider.Hull = 1e6f;
			_htRaider = raider.Id;
			_m.TestSetShipTarget(raider.Id);
			_htEvents = new List<SimEvent>();
			u.Events.Raised += _htEvents.Add;
			return false;
		}
		if (f < 12) return false;
		var ship = u.Ships[u.PlayerShip];
		ship.Hull = ship.MaxHull;   // der Korsar schießt zurück — der Test prüft die Hitze, nicht das Überleben
		if (u.Ships.TryGetValue(_htRaider, out var rd)) rd.Hull = 1e6f;
		if (f > 3000) { Fail($"Hitze: Ablauf nicht beendet (Hitze {Heat.Share(u, ship):P0}, gesperrt {ship.Systems.HeatLocked}, Schüsse {HtPlayerShots(u)})"); return true; }
		// Phase 1: FEUER antippen, bis die Sperre greift
		if (_htLockedFrame < 0)
		{
			if (ship.Systems.HeatLocked)
			{
				_htLockedFrame = f; _htShotsAtLock = HtPlayerShots(u);
				return false;
			}
			if (_htTapAt < 0 && f % 20 == 0) { _htAt = fl.TestFireRect.GetCenter(); Tap(_htAt, true); _htTapAt = f; }
			else if (_htTapAt >= 0 && f >= _htTapAt + 2) { Tap(_htAt, false); _htTapAt = -1; }
			return false;
		}
		if (f == _htLockedFrame + 4)
		{
			if (_htTapAt >= 0) { Tap(_htAt, false); _htTapAt = -1; }
			Check(HtPlayerShots(u) > 0, $"Hitze: echte Tipps auf „FEUER“ haben geschossen ({HtPlayerShots(u)})");
			Check(fl.TestHeatVisible, "Hitze: Hitzezeile im Status sichtbar");
			Check(fl.TestHeatText.EndsWith("%") && int.TryParse(fl.TestHeatText.TrimEnd('%', ' '), out int pct) && pct >= 75, $"Hitze: Zeile zeigt die Hitze ({fl.TestHeatText})");
			Check(fl.TestHeatRowRect.Position.Y >= fl.TestStatusRect.Position.Y && fl.TestHeatRowRect.End.Y <= fl.TestStatusRect.End.Y - 2f, $"Hitze: Hitzezeile liegt im Statusfeld (Höhe) ({fl.TestHeatRowRect} in {fl.TestStatusRect})");
			Check(fl.TestCargoTextRect.End.Y <= fl.TestStatusRect.End.Y + 0.5f, $"Hitze: Frachtzeile bleibt im Statusfeld ({fl.TestCargoTextRect} in {fl.TestStatusRect})");
			Check(fl.Layout is { Overflow: 0 }, $"Hitze: Anordnung ohne Überlappung ({fl.Layout?.Overflow})");
			Check(fl.TestFire.Contains("gesperrt") || fl.TestFire.Contains("ÜBERHITZT"), $"Hitze: Warnzeile sichtbar ({fl.TestFire})");
			Check(_m.HudRef.TestAlert.Contains("Überhitzt") || _m.HudRef.TestAlert.Contains("HITZE"), $"Hitze: Meldung beim Sperren ({_m.HudRef.TestAlert})");
			Check(!Gunnery.HasPower(u, ship, WeaponProfile.Of(u.Modules["weapon_laser_s"])), "Hitze: Waffen gesperrt");
			_htShotsAtLock = HtPlayerShots(u);
			_htAt = fl.TestFireRect.GetCenter();
			Tap(_htAt, true);
			return false;
		}
		if (f == _htLockedFrame + 6) { Tap(_htAt, false); return false; }
		if (f == _htLockedFrame + 12)
		{
			Check(HtPlayerShots(u) == _htShotsAtLock, $"Hitze: Tipp auf „FEUER“ schießt gesperrt nicht ({_htShotsAtLock} → {HtPlayerShots(u)})");
			u.Rules.HeatDissipationBySize[0] = 25f;   // Test: schnell abkühlen
			return false;
		}
		if (f > _htLockedFrame + 12 && !_htCooled)
		{
			if (!ship.Systems.HeatLocked) { _htCooled = true; _htCoolFrame = f; }
			return false;
		}
		if (_htCooled && f == _htCoolFrame + 2)
		{
			Check(fl.TestFire.Length == 0 || !fl.TestFire.Contains("gesperrt"), $"Hitze: Warnzeile nach dem Abkühlen weg ({fl.TestFire})");
			_htShotsAtLock = HtPlayerShots(u);
			return false;
		}
		if (_htCooled && f > _htCoolFrame + 2)
		{
			if (HtPlayerShots(u) > _htShotsAtLock)
			{
				if (_htTapAt >= 0) Tap(_htAt, false);
				Log($"Hitze: gesperrt in Bild {_htLockedFrame}, abgekühlt in Bild {_htCoolFrame}, feuert wieder in Bild {f}");
				u.Rules.HeatDissipationBySize[0] = 3f; u.Rules.HeatPerDamageEnergy = 0.7f; u.Rules.HeatPerDamageKinetic = 0.3f;
				u.Events.Raised -= _htEvents!.Add;
				if (u.Ships.TryGetValue(_htRaider, out var r2)) Departures.RemoveShip(u, r2, DepartureReason.Sold, "test");
				return true;
			}
			if (_htTapAt < 0 && f % 20 == 0) { _htAt = fl.TestFireRect.GetCenter(); Tap(_htAt, true); _htTapAt = f; }
			else if (_htTapAt >= 0 && f >= _htTapAt + 2) { Tap(_htAt, false); _htTapAt = -1; }
		}
		return false;
	}
}
