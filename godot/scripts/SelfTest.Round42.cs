using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 118 (Runde 42): (1) Patrouille-Angebot suchen (im Comms-Angebotsliste sichtbar), an der Angebots-Station
/// andocken, im Dock-Bereich „Aufträge“ per echtem Tipp annehmen, Marker sichtbar (HUD-Zeile, Leuchtfeuer, Core-Marker),
/// Wegpunkte per Testhilfe (Sprung + Position, wie andere Stufen) nacheinander abfliegen → erfüllt, Belohnung in der Kasse,
/// Posteingang meldet „Auftrag erfüllt“; (2) Kopfgeldjagd-Angebot sichtbar (Comms-Vorschau) und nach Annahme mit Marker auf
/// das Ziel (Schiff/Sektor). Ausführung nur über Dienst/Registry bzw. dieselbe Core-Funktion wie die UI.
/// </summary>
public partial class SelfTest
{
	int _r42PatrolOfferId;
	StationId _r42St;
	int _r42BountyOfferId;
	float _r42Wallet0;
	float _r42Reserved;
	List<(SectorId Sector, Vec2 Pos)> _r42Wps = new();
	int _r42Retry;

	bool Round42Stage(int f)
	{
		var hud = _m.HudRef;
		var u = _m.Session.U;
		switch (f)
		{
			case 1:
			{
				if (_m.TestDocked) _m.TestUndock();
				// Patrouille-Angebot suchen (Board erneuert alle 2400 Ticks)
				MissionOffer? offer = null;
				for (int i = 0; i < 60 && offer is null; i++)
				{
					_m.Session.RunTicks(500);
					offer = u.MissionOffers.FirstOrDefault(o => o.Type == PatrolMission.KeyId
						&& SpaceSim.Core.Missions.MissionRules.AcceptIssue(u, o.Id, u.Ships[u.PlayerShip], remote: true) is null);
				}
				if (offer is null) { Fail("42: kein annehmbares Patrouille-Angebot erzeugt"); return false; }
				_r42PatrolOfferId = offer.Id;
				_r42St = offer.At;
				_r42Wallet0 = u.Player.Treasury.Credits;
				// im Comms-Reiter „Aufträge“ sichtbar (Angebotsliste der Registry — dieselbe, die der Bildschirm zeichnet)
				var comms = CommsActions.Default();
				var keys = comms.Offers(u).Where(o => o.Key.StartsWith("mission:accept:", StringComparison.Ordinal)).Select(o => o.Key).ToList();
				Check(keys.Contains($"mission:accept:{_r42PatrolOfferId}"), $"42: Patrouille-Angebot in der Comms-Angebotsliste ({keys.Count} Angebote)");
				// an der Angebots-Station andocken und den Dock-Bereich „Aufträge“ öffnen (wie Stufe 117)
				_m.TestDockAt(_r42St);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Missions);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 3:
			{
				Check(_m.TestScreenOpen("dock.missions"), "42: Dock-Bereich „Aufträge“ offen");
				Check(hud.DockMissions.TestOfferCount >= 1, $"42: Angebote der Station angezeigt ({hud.DockMissions.TestOfferCount})");
				// Bildschirm neu aufbauen, dann Knopf-Index aus dem Live-Zustand (Angebote der Station in Id-Reihenfolge)
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				var btn = hud.DockMissions.TestAcceptButton(_r42PatrolOfferId);
				bool okBtn = btn is not null && btn.IsVisibleInTree() && btn.State != Ui.Kit.KitButtonState.Disabled;
				if (!okBtn)
				{
					_r42Retry++;
					if (_r42Retry > 3) { Fail($"42: Annehmen-Knopf für die Patrouille fehlt/gesperrt ({hud.DockMissions.TestMessage})"); return false; }
					if (_m.TestDocked) _m.TestUndock();
					_stageStart = _frame;   // Stufe 118 von vorn (neu suchen)
					return false;
				}
				Check(true, "42: Knopf „Annehmen“ für die Patrouille vorhanden und aktiv");
				// Ausführung über dieselbe Dienst-Schnittstelle wie der Knopf (MissionsScreen.Execute → MissionService.Execute)
				var r = hud.DockMissions.Execute($"{SpaceSim.Core.Services.MissionService.Accept}:{_r42PatrolOfferId}");
				Check(r.Success, $"42: Annahme über den Dock-Dienst ({r.Message})");
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 7:
			{
				var m = u.Missions.FirstOrDefault(x => x.State == MissionState.Active && x.Type == PatrolMission.KeyId);
				if (m is null)
				{
					_r42Retry++;
					if (_r42Retry > 3) { Fail("42: Annahme fehlgeschlagen: " + hud.DockMissions.TestMessage); return false; }
					foreach (var x in u.Missions.Where(x => x.State == MissionState.Active && x.Type != PatrolMission.KeyId).ToList())
						SpaceSim.Core.Missions.MissionRules.Abandon(u, x.Id, u.Ships[u.PlayerShip]);   // falsch angenommenen Auftrag weg
					if (_m.TestDocked) _m.TestUndock();
					_stageStart = _frame;   // Stufe 118 von vorn: nochmal suchen und tippen
					return false;
				}
				Check(true, "42: Patrouille per Dock-Tipp angenommen");
				_r42Reserved = m.Reserved;
				_r42Wps = PatrolMission.WaypointsOf(m);
				Check(_r42Wps.Count is >= 2 and <= 3, $"42: 2–3 Wegpunkte ({_r42Wps.Count})");
				Check(SpaceSim.Core.Missions.MissionMarkers.For(u).Any(mk => mk.Mission == m.Id && mk.Label.Contains("Patrouille")), "42: Marker zur Patrouille (Core)");
				_m.TestUndock();
				_m.Session.RunTicks(2);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 9:
			{
				Check(hud.Flight.TestMission.Contains("Patrouille"), $"42: HUD-Zeile zeigt die Patrouille („{hud.Flight.TestMission}“)");
				FlyR42(u, _r42Wps[0]);   // erster Wegpunkt (Testhilfe: Sprung + Position)
				return false;
			}
			case 11:
			{
				var m = u.Missions.FirstOrDefault(x => x.Type == PatrolMission.KeyId);
				Check(m is not null && PatrolMission.CurrentOf(m) == 1, $"42: erster Wegpunkt angeflogen (wp={(m is null ? -1 : PatrolMission.CurrentOf(m))})");
				Check(_m.TestMissionBeacons >= 1 || SpaceSim.Core.Missions.MissionMarkers.For(u).Count >= 1, "42: Marker/Leuchtfeuer zur Patrouille sichtbar");
				foreach (var wp in _r42Wps.Skip(1)) FlyR42(u, wp);   // Rest in Reihenfolge
				_m.Session.RunTicks(30);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 13:
			{
				Check(!u.Missions.Any(x => x.Type == PatrolMission.KeyId), "42: Patrouille nach allen Wegpunkten erfüllt");
				Check(u.Player.Treasury.Credits >= _r42Wallet0 + _r42Reserved - 0.01f,
					$"42: Belohnung in der Spielerkasse ({_r42Wallet0:0} → {u.Player.Treasury.Credits:0} Cr, reserviert {_r42Reserved:0} Cr)");
				Check(u.Inbox.Any(x => x.Kind == "mission_completed"), "42: Posteingang meldet „Auftrag erfüllt“");
				// Kopfgeld-Angebot sichtbar mit Marker: Voraussetzungen setzen (Bedrohung im Gebiet + Fahndung auf einen Kaperschiff-Besitzer),
				// dann warten, bis das Board ein Kopfgeld-Angebot erzeugt (wie andere Stufen Weltzustand für ihren Test setzen)
				var fac = u.NpcFactionIds().First(f => !SpaceSim.Core.Simulation.Wars.AtWar(u, u.PlayerFaction!.Value, f));
				var sec = u.Sectors.Values.First(x => x.Owner == fac);
				MissionOffer? bo = null;
				for (int i = 0; i < 60 && bo is null; i++)
				{
					// Bedrohung verfällt pro Tick (ThreatMap.Update) und Fahndung verfällt ohne neues Verbrechen — vor jedem
					// Angebots-Bucket (alle MissionRefreshTicks) erneut setzen, sonst greift MissionBountyMinThreat nicht.
					sec.Threat = MathF.Max(sec.Threat, 50f);
					var raider = u.Ships.Values.FirstOrDefault(SpaceSim.Core.Simulation.ThreatMap.IsRaider);
					if (raider is not null)
						u.Actors[raider.Owner].Wanted[fac] = new WantedRecord { Heat = 4f, Fine = 800f, LastCrimeTick = u.Tick, LastCrime = "piracy" };
					_m.Session.RunTicks(500);
					bo = u.MissionOffers.FirstOrDefault(o => o.Type == BountyMission.KeyId
						&& SpaceSim.Core.Missions.MissionRules.AcceptIssue(u, o.Id, u.Ships[u.PlayerShip], remote: true) is null);
				}
				if (bo is null) { Fail("42: kein annehmbares Kopfgeld-Angebot erzeugt"); return false; }
				_r42BountyOfferId = bo.Id;
				var comms = CommsActions.Default();
				var p = comms.Preview(u, $"mission:accept:{bo.Id}");
				Check(p.Possible && p.Text.Contains("Kopfgeldjagd"), $"42: Kopfgeld-Angebot sichtbar mit Vorschau („{p.Text}“)");
				var r = comms.Execute(u, $"mission:accept:{bo.Id}");   // dieselbe Registry wie der Comms-Reiter
				Check(r.Success, $"42: Kopfgeldjagd angenommen ({r.Message})");
				return false;
			}
			case 15:
			{
				var m = u.Missions.FirstOrDefault(x => x.Type == BountyMission.KeyId);
				Check(m is not null, "42: Kopfgeldjagd aktiv");
				if (m is null) return false;
				var mk = SpaceSim.Core.Missions.MissionMarkers.For(u).FirstOrDefault(x => x.Mission == m.Id);
				Check(mk is not null && mk.Label.Contains("Kopfgeld"), $"42: Marker auf das Kopfgeld-Ziel (Sektor {mk?.Sector.Value}, Art {mk?.Kind})");
				Check(hud.Flight.TestMission.Contains("Kopfgeld") || SpaceSim.Core.Missions.MissionMarkers.First(u)?.Label.Contains("Kopfgeld") == true,
					$"42: HUD-Zeile/Marker zeigt die Kopfgeldjagd („{hud.Flight.TestMission}“)");
				_m.Session.RunTicks(2);
				hud.Screens.Current?.Refresh();
				return true;
			}
		}
		return false;
	}

	/// <summary>Runde 42 (Testhilfe): aktives Schiff in den Wegpunkt-Sektor setzen und auf die Wegpunkt-Position — wie ein Sprung per Autopilot.</summary>
	void FlyR42(Universe u, (SectorId Sector, Vec2 Pos) wp)
	{
		var me = u.Ships[u.PlayerShip];
		if (me.Transit is not null) u.FinishTransit(me);
		if (me.Sector != wp.Sector) u.MoveShip(me, wp.Sector);
		u.PlayerSector = wp.Sector;
		me.Position = wp.Pos;
		me.DockedAt = default;
		_m.Session.RunTicks(25);   // MissionSystem prüft alle 20 Ticks
	}
}
