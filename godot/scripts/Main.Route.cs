using SpaceSim.Core.Comms;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Mehrpunkt-Route (Lücke gegenüber Interstellar Pilot 2): der Autopilot fliegt die Halte aus <see cref="PlayerRoutes"/> der Reihe nach ab — derselbe Autopilot
/// wie für ein einzelnes Ziel (Zeitraffer, Abbruch durch manuelle Steuerung wie dort). Ein erreichter Halt gibt den nächsten frei; bei „an jeder Station
/// andocken“ dockt der Autopilot am Stationshalt an, das Abdocken fliegt weiter. Manuelle Steuerung oder ein anderes Ziel halten die Route an (sie bleibt
/// erhalten; „Route starten“ oder der Autopilot auf das Routenziel setzen fort).
/// </summary>
public partial class Main
{
	/// <summary>Fliegt der Autopilot gerade die Route?</summary>
	bool _routeFlying;
	/// <summary>Am Stationshalt angedockt — Abdocken setzt die Route fort.</summary>
	bool _routeResume;

	void WireRoute()
	{
		_hud.CcUniverse.RouteStartRequested += () => { CloseCommand(); StartRoute(); };
		_hud.CcSector.RouteStartRequested += () => { CloseCommand(); StartRoute(); };
		_hud.CcUniverse.TargetSectorRequested += sec =>
		{
			var u = _session.U;
			SelectTarget(Navigator.ForSector(u, sec, u.PlayerShip), null);
			CloseCommand();
		};
		_hud.CcSector.HailRequested += sid => { CloseCommand(); OpenHailTo(HailTarget.OfStation(sid)); };
		_hud.CcSector.ShipMenuRequested += OpenShipMenu;
	}

	void ResetRoute() { _routeFlying = false; _routeResume = false; }

	/// <summary>„Route starten“: vom ersten offenen Halt (eine abgeflogene Route von vorn); angedockt erst abdocken.</summary>
	void StartRoute()
	{
		var u = _session.U;
		PlayerRoutes.Restart(u);
		if (PlayerRoutes.Current(u) is null) { _hud.Toast("Keine Route — auf der Karte lange drücken: „Zur Route hinzufügen“"); return; }
		if (_docked != null)
		{
			if (u.PlayerOnFoot) { _hud.Toast("Du bist zu Fuß — erst ein Schiff übernehmen."); return; }
			_routeResume = true;
			Undock();
			_hud.Toast("Route: Abdocken, dann fliegt der Autopilot los");
			return;
		}
		FlyRoute();
	}

	/// <summary>Nächsten offenen Halt als Autopilot-Ziel setzen (erreichte überspringen).</summary>
	void FlyRoute()
	{
		var u = _session.U;
		if (!PlayerRoutes.SkipReached(u, u.PlayerSector, PlayerPos2())) { FinishRoute(); return; }
		if (PlayerRoutes.Current(u) is not { } stop || PlayerRoutes.Leg(u, stop) is not { } leg) { FinishRoute(); return; }
		SetTarget(leg.Target, leg.Station);
		_routeFlying = true;
		_hud.Toast($"Route {PlayerRoutes.Of(u)!.Next + 1}/{PlayerRoutes.Of(u)!.Stops.Count}: {PlayerRoutes.Label(u, stop)}");
	}

	void FinishRoute()
	{
		CancelAutopilot("Route beendet");
		_routeFlying = false;
		_target = null; _targetStation = null;
		_hud.Toast("Route abgeflogen");
	}

	Vec2 PlayerPos2() => new(_player.Position.X, _player.Position.Z);

	/// <summary>
	/// Je Autopilot-Schritt: ist der aktuelle Halt erreicht, den nächsten freigeben (Tor: im Sektor dahinter). true = der Autopilot steht in diesem Bild
	/// (angedockt oder Route zu Ende).
	/// </summary>
	bool RouteTick()
	{
		if (!_routeFlying) return false;
		var u = _session.U;
		if (PlayerRoutes.Current(u) is not { } cur) { _routeFlying = false; return false; }
		if (!PlayerRoutes.Reached(u, cur, u.PlayerSector, PlayerPos2())) return false;
		var route = PlayerRoutes.Of(u)!;
		PlayerRoutes.Advance(u);
		if (cur.Kind == RouteStopKind.Station && route.DockAtStations && u.Stations.ContainsKey(new StationId(cur.Id)))
		{
			bool more = !route.Finished;
			CancelAutopilot("Route-Halt");
			_target = null; _targetStation = null;
			var sid = new StationId(cur.Id);
			Dock(sid);
			_routeResume = more && _docked == sid;
			_hud.Toast(more ? $"Route: angedockt an {u.Stations[sid].Name} — „Abdocken“ fliegt weiter" : $"Route abgeflogen: angedockt an {u.Stations[sid].Name}", 4f);
			return true;
		}
		if (!PlayerRoutes.SkipReached(u, u.PlayerSector, PlayerPos2())) { FinishRoute(); return true; }
		var next = PlayerRoutes.Current(u)!;
		if (PlayerRoutes.Leg(u, next) is not { } leg) { FinishRoute(); return true; }
		_target = leg.Target; _targetStation = leg.Station;
		_follower.Reset();
		_hud.Toast($"Route {route.Next + 1}/{route.Stops.Count}: {PlayerRoutes.Label(u, next)}");
		return false;
	}

	/// <summary>Nach dem Abdocken (Animation zu Ende): die Route weiterfliegen, wenn sie am Stationshalt stand.</summary>
	void ResumeRouteAfterUndock()
	{
		if (!_routeResume) return;
		_routeResume = false;
		if (PlayerRoutes.Current(_session.U) is null) return;
		FlyRoute();
	}

	/// <summary>Autopilot-Taste auf das aktuelle Routenziel: die Route läuft weiter.</summary>
	void ResumeRouteIfTarget()
	{
		var u = _session.U;
		if (_target is not { } t || PlayerRoutes.Current(u) is not { } cur || PlayerRoutes.Leg(u, cur) is not { } leg) return;
		if (leg.Target == t) _routeFlying = true;
	}

	string RouteHudLine() => PlayerRoutes.HudLine(_session.U, _routeFlying && _autopilot || _routeResume);

	internal bool TestRouteFlying => _routeFlying;
	internal bool TestRouteResume => _routeResume;
	internal void TestStartRoute() => StartRoute();
}
