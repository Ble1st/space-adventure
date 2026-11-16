using System;
using System.Collections.Generic;
using System.Linq;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 120 (Runde 42c, Handytest-Restmängel): (1) Transport annehmen → das Angebot ist aus der Dock-Liste
/// entfernt und wird nicht sofort neu erzeugt; versiegelte Fracht steht in der Schiffspalte („versiegelte Fracht 20 m³ → Ziel“);
/// Frist ≥ Mindestfrist; (2) Zeitraffer: vor dem Andocken ×10, nach dem echten Andockweg (TestDockAt → Dock) automatisch ×1;
/// (3) Comms-Reiter „Aufträge“: Meldung im eigenen Fach (sichtbar), Reiterwechsel und erneutes Öffnen setzen sie zurück,
/// nach 6 s verfällt sie (umschaltbare Test-Uhr).
/// </summary>
public partial class SelfTest
{
	int _r42cOfferId;
	StationId _r42cAt, _r42cTarget;
	Func<ulong>? _r42cMsgNow;

	bool Round42cStage(int f)
	{
		var hud = _m.HudRef;
		var u = _m.Session.U;
		if (f == 1) { _m.TestMenu("new:trader"); return false; }   // frisches Händler-Spiel
		switch (f)
		{
			case 2:
			{
				if (_m.TestDocked) _m.TestUndock();
				_m.Session.RunTicks(2);
				foreach (var x in u.Missions.Where(m => m.State == MissionState.Active).ToList())
					SpaceSim.Core.Missions.MissionRules.Abandon(u, x.Id, u.Ships[u.PlayerShip]);
				var me = u.Ships[u.PlayerShip];
				me.Cargo.Clear();
				me.MissionCargo = 0f;
				if (me.CargoCapacity < 40f) me.CargoCapacity = me.Stats.Cargo > 0f ? me.Stats.Cargo : 100f;
				foreach (var fac in u.NpcFactionIds())
				{
					var a = u.Actors[new ActorId(fac.Value)];
					a.Treasury.Credits = MathF.Max(a.Treasury.Credits, 50_000f);
				}
				u.MissionFactions.Clear();
				u.Actors[u.PlayerActor].Wanted.Clear();
				// Transportangebot suchen (wie Stufe 119)
				MissionOffer? offer = null;
				for (int i = 0; i < 60 && offer is null; i++)
				{
					_m.Session.RunTicks(500);
					foreach (var o in u.MissionOffers.Where(o => o.Type == SpaceSim.Core.Missions.TransportMission.KeyId))
					{
						var iss = SpaceSim.Core.Missions.MissionRules.AcceptIssue(u, o.Id, me, remote: true) ?? "OK";
						if (iss == "OK" && u.Stations.TryGetValue(o.At, out var os) && SpaceSim.Core.Simulation.Docking.Issue(u, os, me) is not null)
							iss = "Angebots-Station nimmt das Schiff nicht";
						if (iss == "OK" && u.Stations.TryGetValue(SpaceSim.Core.Missions.TransportMission.TargetOf(o), out var ts2) && SpaceSim.Core.Simulation.Docking.Issue(u, ts2, me) is not null)
							iss = "Ziel-Station nimmt das Schiff nicht";
						if (iss == "OK") { offer = o; break; }
					}
				}
				if (offer is null) { Fail("42c: kein annehmbares Transport-Angebot erzeugt"); return false; }
				_r42cOfferId = offer.Id;
				_r42cAt = offer.At;
				_r42cTarget = u.Stations[SpaceSim.Core.Missions.TransportMission.TargetOf(offer)].Id;
				_m.TestDockAt(_r42cAt);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Missions);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 3:
			{
				Check(_m.TestScreenOpen("dock.missions"), "42c: Dock-Bereich „Aufträge“ offen");
				int offersBefore = hud.DockMissions.TestOfferCount;
				Check(offersBefore >= 1, $"42c: Angebote der Station angezeigt ({offersBefore})");
				var r = hud.DockMissions.Execute($"{MissionService.Accept}:{_r42cOfferId}");
				Check(r.Success, $"42c: Transport angenommen ({r.Message})");
				if (!r.Success) { Fail("42c: Annahme fehlgeschlagen: " + r.Message); return false; }
				var m = u.Missions.FirstOrDefault(x => x.State == MissionState.Active && x.Type == SpaceSim.Core.Missions.TransportMission.KeyId);
				Check(m is not null, "42c: Transportauftrag aktiv");
				if (m is null) return false;
				Check(m.Deadline - u.Tick >= u.Rules.MissionMinDeadlineTicks, $"42c: Frist ≥ Mindestfrist ({m.Deadline - u.Tick} ≥ {u.Rules.MissionMinDeadlineTicks})");
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				// (1) angenommenes Angebot aus der Liste entfernt und nicht sofort neu erzeugt
				Check(hud.DockMissions.TestAcceptButton(_r42cOfferId) is null, "42c: angenommenes Angebot nicht mehr in der Dock-Liste");
				_m.Session.RunTicks(150);   // Andock-Station würde sofort neu erzeugen (Runde 42b) — das alte Angebot darf nicht wiederkommen
				hud.Screens.Current?.Refresh();
				bool again = u.MissionOffers.Any(o => o.At == _r42cAt && o.Type == SpaceSim.Core.Missions.TransportMission.KeyId
					&& o.Args.TryGetValue("target", out var ts) && int.TryParse(ts, out var tv) && tv == _r42cTarget.Value);
				Check(!again, "42c: kein neues Angebot für denselben Transport an der Andock-Station");
				// (2) versiegelte Fracht in der Schiffspalte sichtbar
				var texts = hud.DockMissions.TestShipPanelTexts();
				var sealedLine = texts.FirstOrDefault(t => t.StartsWith("versiegelte Fracht", StringComparison.Ordinal));
				Check(sealedLine is not null, $"42c: „versiegelte Fracht“ in der Schiffspalte ({string.Join(" | ", texts.Take(8))})");
				if (sealedLine is not null)
					Check(sealedLine.Contains(u.Stations[_r42cTarget].Name), $"42c: versiegelte Fracht mit Ziel „{u.Stations[_r42cTarget].Name}“ („{sealedLine}“)");
				return false;
			}
			case 5:
			{
				// Zeitraffer: vor dem Andocken ×10, nach dem echten Andockweg automatisch ×1
				_m.TestSetFast(true);
				Check(_m.TestFast, "42c: Zeitraffer ×10 gesetzt");
				_m.TestUndock();
				_m.Session.RunTicks(2);
				_m.TestDockAt(_r42cAt);
				_m.Session.RunTicks(2);
				hud.Screens.Current?.Refresh();
				Check(!_m.TestFast, "42c: Andocken schaltet den Zeitraffer automatisch auf ×1");
				return false;
			}
			case 7:
			{
				// Comms: Meldung im eigenen Fach, Rücksetzen bei Reiterwechsel/Öffnen, Verfall nach 6 s
				if (_m.TestDocked) _m.TestUndock();
				_m.Session.RunTicks(2);
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Comms);
				var cs = hud.CcComms;
				cs.TestSetTab("missions");
				cs.TestShowMessage("Auftrag angenommen: Transport — versiegelte Fracht 20 m³ → Ziel · noch 9:59 (zweite Zeile zum Messen)");
				hud.Screens.Current?.Refresh();
				Check(cs.TestTab == "missions" && cs.TestMessageVisible, "42c: Meldung im Comms-Reiter „Aufträge“ sichtbar (eigenes Fach)");
				cs.TestSetTab("inbox");
				Check(cs.TestMessage.Length == 0, "42c: Reiterwechsel setzt die Meldung zurück");
				cs.TestSetTab("missions");
				cs.TestShowMessage("Erneut gemeldet");
				hud.Screens.Current?.Refresh();
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Factions);
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Comms);
				Check(cs.TestMessage.Length == 0, "42c: erneutes Öffnen des Bereichs setzt die Meldung zurück");
				// Verfall nach 6 s (Test-Uhr vorgeschoben)
				cs.TestShowMessage("Alter Text");
				hud.Screens.Current?.Refresh();
				Check(cs.TestMessageVisible, "42c: Meldung frisch sichtbar");
				_r42cMsgNow = Ui.Screens.CommsScreen.MsgNow;
				var now = _r42cMsgNow();
				Ui.Screens.CommsScreen.MsgNow = () => now + 10_000;
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				Check(!cs.TestMessageVisible, "42c: Meldung verfällt nach 6 s (nicht minutenlang stehen)");
				Ui.Screens.CommsScreen.MsgNow = _r42cMsgNow;
				_r42cMsgNow = null;
				_m.TestCloseCommand();
				return true;
			}
		}
		return false;
	}
}
