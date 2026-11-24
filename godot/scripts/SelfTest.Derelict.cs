using System.Linq;
using Godot;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 159 (herrenlose Schiffe): neues Spiel (Händler), angedockt an einem Sektor-HQ; im Dock-Bereich „Aufträge“ wird ein Pannenauftrag per echtem
/// Tipp angenommen (das liegengebliebene Schiff der Fraktion entsteht), die Person fliegt hin, ruft es per Funk und tippt „Übernehmen“ — es folgt ihr; sie dockt
/// am HQ, das Pannenschiff dockt mit, der Auftrag ist erfüllt (Belohnung). Dann an einer Werft „Schiff liefern“: Annahme per Tipp, ein Debug-Schiff des Modells
/// liegt im Hangar, „Abliefern“ per Tipp übergibt es (Belohnung). Zuletzt ein Notsignal (Zufallsereignis) — das herrenlose Schiff wird per Funk übernommen.
/// </summary>
public partial class SelfTest
{
	StationId _drHq, _drYard;
	int _drOffer, _drMission, _drShipOffer, _drShipMission;
	ShipId _drShip, _drSpawned, _drEvent;
	float _drWallet;
	bool _drPlayerRaids, _drFleetRaids;

	bool DerelictStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				_m.Session.RunTicks(2);
				var me = u.Ships[u.PlayerShip];
				foreach (var a in u.Actors.Values) if (a.Id != u.PlayerActor && a.Id != u.FreeActor) a.Treasury.Credits = System.Math.Max(a.Treasury.Credits, 300000f);
				_drPlayerRaids = u.Rules.PlayerRaids; _drFleetRaids = u.Rules.FleetRaids;
				u.Rules.PlayerRaids = false; u.Rules.FleetRaids = false;   // hier geht es nicht um Überfälle
				var hq = u.StationsById().Where(s => u.StationTypes[s.TypeKey].Government == GovernmentKind.SectorHq && u.FactionOf(s) is { } fa && u.IsNpcFaction(fa))
					.OrderBy(s => s.Sector == me.Sector ? 0 : 1).ThenBy(s => s.Id.Value).First();
				_drHq = hq.Id;
				_m.TestDockAt(hq.Id);
				u.MissionOffers.RemoveAll(o => o.At == hq.Id);
				var o = MakeOffer(u, BreakdownMission.KeyId, hq, x => x.Args["sector"] == hq.Sector.Value.ToString()) ?? MakeOffer(u, BreakdownMission.KeyId, hq, null);
				if (o is null) { Fail("159: kein Pannenangebot erzeugt"); return false; }
				_drOffer = o.Id;
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Missions);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			}
		}
		if (f >= 8 && f < 12) { FoTap(f, 8, () => hud.DockMissions.TestAcceptButton(_drOffer), "Annehmen Panne"); return false; }
		switch (f)
		{
			case 13:
			{
				var m = u.Missions.FirstOrDefault(x => x.Type == BreakdownMission.KeyId);
				Check(m is not null, $"159: Pannenauftrag per Tipp angenommen ({hud.DockMissions.TestMessage})");
				if (m is null) return false;
				_drMission = m.Id;
				var d = BreakdownMission.ShipOf(u, m)!;
				_drShip = d.Id;
				Check(d.IsDerelict && d.Owner == m.Client, "159: Pannenschiff der Fraktion liegt herrenlos da");
				Check(MissionMarkers.For(u).Any(x => x.Mission == m.Id && x.Kind == MissionMarkerKind.Ship), "159: Marker am Pannenschiff");
				_m.TestCloseCommand();
				_m.TestUndock();
				return false;
			}
			case 24:
			{
				var d = u.Ships[_drShip];
				if (d.Sector != u.PlayerSector) _m.TestEnterSector(d.Sector);
				_m.TestTeleportTo(d.Position + new Vec2(14f, 0f));
				Check(_m.TestSelectShip(_drShip), "159: Pannenschiff als Ziel gewählt");
				_m.TestOpenHail(HailTarget.OfShip(_drShip));
				return false;
			}
			case 28:
				Check(hud.Flight.HailOpen, "159: Funk zum Pannenschiff offen");
				Check(hud.Flight.Hail.TestTopic("Übernehmen")?.State != Ui.Kit.KitButtonState.Disabled, $"159: „Übernehmen“ bereit ({hud.Flight.Hail.TestWho})");
				TapKit(hud.Flight.Hail.TestTopic("Übernehmen"), true, "Übernehmen");
				return false;
			case 30: TapKit(hud.Flight.Hail.TestTopic("Übernehmen"), false, "Übernehmen"); return false;
			case 34:
			{
				var d = u.Ships[_drShip];
				Check(d.IsPlayer && !d.IsDerelict, $"159: Pannenschiff per Tipp übernommen ({hud.Flight.Hail.TestReply})");
				Check(d.Command is FollowCommand, "159: Pannenschiff folgt dir");
				Check(MissionMarkers.For(u).Any(x => x.Mission == _drMission && x.Kind == MissionMarkerKind.Station), "159: Marker wechselt zur Auftragsstation");
				_m.HandleBack();
				_drWallet = u.Player.Treasury.Credits;
				_m.TestDockAt(_drHq);
				return false;
			}
		}
		if (f >= 36 && f < 90)
		{
			if (u.Missions.Any(x => x.Id == _drMission)) _m.Session.RunTicks(100);
			return false;
		}
		switch (f)
		{
			case 90:
			{
				Check(!u.Missions.Any(x => x.Id == _drMission), $"159: Pannenschiff zur Station gebracht — Auftrag erfüllt ({string.Join(",", u.Missions.Select(x => x.Type))})");
				Check(!u.Ships.ContainsKey(_drShip), "159: Pannenschiff zurück an die Fraktion");
				Check(u.Player.Treasury.Credits > _drWallet + 100f, $"159: Belohnung bezahlt ({_drWallet:0} → {u.Player.Treasury.Credits:0} Cr)");
				// Schiff liefern an einer Werft
				var yard = u.StationsById().Where(s => s.TypeKey == "shipyard" && u.FactionOf(s) is { } fa && u.IsNpcFaction(fa)).OrderBy(s => s.Id.Value).First();
				_drYard = yard.Id;
				_m.TestCloseCommand();
				_m.TestDockAt(yard.Id);
				u.MissionOffers.RemoveAll(o => o.At == yard.Id);
				var o = MakeOffer(u, ShipDeliveryMission.KeyId, yard, null);
				if (o is null) { Fail("159: kein Angebot „Schiff liefern“"); return false; }
				_drShipOffer = o.Id;
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Missions);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			}
		}
		if (f >= 92 && f < 96) { FoTap(f, 92, () => hud.DockMissions.TestAcceptButton(_drShipOffer), "Annehmen Schiff liefern"); return false; }
		switch (f)
		{
			case 97:
			{
				var m = u.Missions.FirstOrDefault(x => x.Type == ShipDeliveryMission.KeyId);
				Check(m is not null, $"159: „Schiff liefern“ per Tipp angenommen ({hud.DockMissions.TestMessage})");
				if (m is null) return false;
				_drShipMission = m.Id;
				Check(hud.DockMissions.TestMissionButton(m.Id)?.State == Ui.Kit.KitButtonState.Disabled, "159: „Abliefern“ ohne passendes Schiff gesperrt");
				var ship = DebugSpawn.SpawnShip(u, m.Args["model"], out var msg);
				Check(ship is not null && ship.DockedAt == _drYard, $"159: Debug-Schiff liegt im Hangar ({msg})");
				_drSpawned = ship?.Id ?? default;
				_drWallet = u.Player.Treasury.Credits;
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			}
		}
		if (f == 98)
		{
			_m.Session.RunTicks(1);
			var dm = u.Missions.FirstOrDefault(x => x.Id == _drShipMission);
			GD.Print($"[selftest] 159: Abliefern möglich? {(dm is null ? "-" : MissionRules.Types.Get(dm.Type)!.DeliverIssue(u, dm, u.Ships[u.PlayerShip], u.Stations[_drYard]) ?? "ja")}");
			hud.Screens.Current?.Refresh();
			return false;
		}
		if (f >= 101 && f < 105) { FoTap(f, 101, () => hud.DockMissions.TestMissionButton(_drShipMission), "Abliefern"); return false; }
		switch (f)
		{
			case 107:
				Check(!u.Missions.Any(x => x.Id == _drShipMission), $"159: Schiff per Tipp abgeliefert ({hud.DockMissions.TestMessage})");
				Check(!u.Ships.ContainsKey(_drSpawned), "159: geliefertes Schiff an die Fraktion übergeben");
				Check(u.Player.Treasury.Credits > _drWallet + 100f, $"159: Belohnung für das Schiff ({_drWallet:0} → {u.Player.Treasury.Credits:0} Cr)");
				_m.TestCloseCommand();
				_m.TestUndock();
				return false;
			case 116:
			{
				// Zufallsereignis: Notsignal eines herrenlosen Schiffs (eigener RNG), dann zu dir geholt und per Funk übernommen
				Ship? d = null;
				for (long b = 0; b < 60 && d is null; b++) d = Derelicts.SpawnEvent(u, b);
				Check(d is not null, "159: Notsignal erzeugt ein herrenloses Schiff");
				if (d is null) return false;
				Check(_m.LastDerelictMessage.Contains("Notsignal"), $"159: Notsignal als Meldung ({_m.LastDerelictMessage})");
				_drEvent = d.Id;
				var me = u.Ships[u.PlayerShip];
				if (d.Sector != me.Sector) u.MoveShip(d, me.Sector);
				d.Position = me.Position + new Vec2(16f, 6f);
				Check(_m.TestSelectShip(d.Id), "159: herrenloses Schiff als Ziel gewählt");
				Check(SpaceSim.Core.Combat.PlayerCombat.TargetIssueShort(u, me, d) == "herrenlos", "159: herrenlos — nicht beschießbar");
				_m.TestOpenHail(HailTarget.OfShip(d.Id));
				return false;
			}
			case 120: TapKit(hud.Flight.Hail.TestTopic("Übernehmen"), true, "Übernehmen"); return false;
			case 122: TapKit(hud.Flight.Hail.TestTopic("Übernehmen"), false, "Übernehmen"); return false;
			case 126:
			{
				var d = u.Ships[_drEvent];
				Check(d.IsPlayer && d.Owner == u.PlayerActor, $"159: herrenloses Schiff per Tipp übernommen ({hud.Flight.Hail.TestReply})");
				Check(PlayerFleet.Owned(u).Any(s => s.Id == _drEvent), "159: gehört jetzt zur eigenen Flotte");
				_m.HandleBack();
				u.Rules.PlayerRaids = _drPlayerRaids; u.Rules.FleetRaids = _drFleetRaids;
				return true;
			}
		}
		return false;
	}
}
