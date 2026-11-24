using System;
using System.Linq;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 121 (Runde 42d, Handytest-Restmängel r42c): (1) Dock-Bereich „Aufträge“ — Annahmemeldung ohne doppeltes
/// „Auftrag:“, sichtbar und nach ~6 s verfallen (wie Comms); Posteingang: angenommene Meldung ohne „verfällt in …“ und ohne
/// weiterlaufende Restzeit im Detail. (2) Liefervertrag über Comms annehmen — Reiter „Aufträge“ zeigt „Festpreis … ·
/// Kasse reserviert“ statt „Belohnung 0 Cr (reserviert)“. (3) HUD-Auftragszeile gekürzt, aber Restzeit sichtbar, keine „~~“.
/// (4) Sternenkarte: Legende „◆/★ = Auftrag“ gezeichnet. (5) Zeitraffer: ×10 vor dem Andocken, danach ×1 mit gebündelter
/// Meldung „Zeitraffer aus (angedockt)“ (nicht von anderen Toasts verdrängt).
/// </summary>
public partial class SelfTest
{
	int _r42dOfferId;
	StationId _r42dAt;
	Func<ulong>? _r42dMsgNow;

	bool Round42dStage(int f)
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
					MissionRules.Abandon(u, x.Id, u.Ships[u.PlayerShip]);
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
				u.WalletOf(me).Credits = MathF.Max(u.WalletOf(me).Credits, 100_000f);
				// Transportangebot suchen (wie Stufe 120)
				MissionOffer? offer = null;
				for (int i = 0; i < 60 && offer is null; i++)
				{
					_m.Session.RunTicks(500);
					foreach (var o in u.MissionOffers.Where(o => o.Type == TransportMission.KeyId))
					{
						var iss = MissionRules.AcceptIssue(u, o.Id, me, remote: true) ?? "OK";
						if (iss == "OK" && u.Stations.TryGetValue(o.At, out var os) && SpaceSim.Core.Simulation.Docking.Issue(u, os, me) is not null)
							iss = "Angebots-Station nimmt das Schiff nicht";
						if (iss == "OK" && u.Stations.TryGetValue(TransportMission.TargetOf(o), out var ts2) && SpaceSim.Core.Simulation.Docking.Issue(u, ts2, me) is not null)
							iss = "Ziel-Station nimmt das Schiff nicht";
						if (iss == "OK") { offer = o; break; }
					}
				}
				if (offer is null) { Fail("42d: kein annehmbares Transport-Angebot erzeugt"); return false; }
				_r42dOfferId = offer.Id;
				_r42dAt = offer.At;
				_m.TestDockAt(_r42dAt);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Missions);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 3:
			{
				// Annehmen im Dock: Meldung ohne doppeltes „Auftrag:“, sichtbar, verfällt nach ~6 s
				Check(_m.TestScreenOpen("dock.missions"), "42d: Dock-Bereich „Aufträge“ offen");
				var r = hud.DockMissions.Execute($"{MissionService.Accept}:{_r42dOfferId}");
				Check(r.Success, $"42d: Transport angenommen ({r.Message})");
				if (!r.Success) { Fail("42d: Annahme fehlgeschlagen: " + r.Message); return false; }
				Check(r.Message.StartsWith("Auftrag angenommen: ", StringComparison.Ordinal) && !r.Message.Contains("Auftrag: Auftrag"),
					$"42d: Meldung ohne doppeltes „Auftrag:“ („{r.Message}“)");
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				Check(hud.DockMissions.TestMessageVisible, "42d: Annahmemeldung im Dock-Bereich sichtbar");
				_r42dMsgNow = Ui.Screens.MissionsScreen.MsgNow;
				var now = _r42dMsgNow();
				Ui.Screens.MissionsScreen.MsgNow = () => now + 10_000;
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				Check(!hud.DockMissions.TestMessageVisible, "42d: Annahmemeldung verfällt nach ~6 s (wie in Comms)");
				Ui.Screens.MissionsScreen.MsgNow = _r42dMsgNow;
				_r42dMsgNow = null;
				// Posteingang: die angenommene Meldung hat weder „verfällt in …“ noch eine weiterlaufende Restzeit
				var msg = u.Inbox.LastOrDefault(x => x.Kind == "mission_accepted");
				Check(msg is not null, "42d: Posteingang meldet „Auftrag angenommen“");
				if (msg is not null)
				{
					string meta = CommsText.RowMeta(u, msg);
					Check(!meta.Contains("verfällt"), $"42d: Zeile ohne „verfällt in …“ („{meta}“)");
					var (_, body) = CommsText.Describe(u, msg);
					Check(!System.Text.RegularExpressions.Regex.IsMatch(body, @"noch \d+:\d\d"),
						$"42d: Detailtext ohne weiterlaufende Restzeit („{body}“)");
				}
				return false;
			}
			case 4:
			{
				// Liefervertrag über Comms annehmen (remote) — Reiter „Aufträge“ zeigt „Festpreis … · Kasse reserviert“
				if (_m.TestDocked) _m.TestUndock();
				_m.Session.RunTicks(2);
				MissionOffer? co = null;
				for (int i = 0; i < 60 && co is null; i++)
				{
					_m.Session.RunTicks(500);
					co = u.MissionOffers.FirstOrDefault(o => o.Type == DeliveryMission.KeyId
						&& o.Args.TryGetValue("contract", out var c) && c == "1"
						&& MissionRules.AcceptIssue(u, o.Id, u.Ships[u.PlayerShip], remote: true) is null);
				}
				if (co is null) { Fail("42d: kein annehmbares Liefervertrag-Angebot erzeugt"); return false; }
				var comms = CommsActions.Default();
				var r = comms.Execute(u, $"mission:accept:{co.Id}");
				Check(r.Success, $"42d: Liefervertrag angenommen ({r.Message})");
				if (!r.Success) { Fail("42d: Annahme Vertrag fehlgeschlagen: " + r.Message); return false; }
				Check(!r.Message.Contains("Auftrag: Auftrag"), $"42d: Comms-Meldung ohne doppeltes „Auftrag:“ („{r.Message}“)");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Comms);
				var cs = hud.CcComms;
				cs.TestSetTab("missions");
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				var line = cs.TestMoneyLines.FirstOrDefault(x => x.StartsWith("Festpreis", StringComparison.Ordinal));
				Check(line is not null && line.Contains("Kasse reserviert"),
					$"42d: Vertrag zeigt „Festpreis … · Kasse reserviert“ statt „Belohnung 0 Cr (reserviert)“ („{string.Join(" | ", cs.TestMoneyLines)}“)");
				_m.TestCloseCommand();
				return false;
			}
			case 5:
			{
				if (_m.TestDocked) _m.TestUndock();
				_m.Session.RunTicks(2);
				return false;
			}
			case 6:
			{
				// HUD-Auftragszeile: gekürzt nach gemessener Breite, Restzeit aber immer sichtbar, keine „~~“
				var t = hud.Flight.TestMission;
				Check(t.Length > 0, "42d: HUD-Auftragszeile sichtbar");
				Check(t.Contains("noch"), $"42d: Restzeit in der HUD-Zeile sichtbar („{t}“)");
				Check(!t.Contains("~~"), $"42d: keine doppelte Tilde in der HUD-Zeile („{t}“)");
				return false;
			}
			case 7:
			{
				_m.HudRef.Map.Open(_m.Session.U, _m.Session.U.PlayerSector, null);   // UI-Umbau: „Karte“ öffnet das Kartenfenster — die Sternenkarte (Sprungzielwahl) direkt
				return false;
			}
			case 8:
			{
				Check(hud.Map.Visible, "42d: Sternenkarte offen");
				Check(hud.Map.TestLegendDrawn, $"42d: Legende „{Ui.MapOverlay.LegendText}“ auf der Sternenkarte gezeichnet");
				hud.Map.Visible = false;
				return false;
			}
			case 9:
			{
				// Zeitraffer: ×10 vor dem Andocken, danach automatisch ×1 + gebündelte Meldung „Zeitraffer aus (angedockt)“
				_m.TestSetFast(true);
				Check(_m.TestFast, "42d: Zeitraffer ×10 gesetzt");
				_m.TestDockAt(_r42dAt);
				_m.Session.RunTicks(2);
				hud.Screens.Current?.Refresh();
				Check(!_m.TestFast, "42d: Andocken schaltet den Zeitraffer automatisch auf ×1");
				Check(hud.TestToastText.Contains("Zeitraffer aus (angedockt)"),
					$"42d: Meldung „Zeitraffer aus (angedockt)“ sichtbar (nicht verdrängt) („{hud.TestToastText}“)");
				return true;
			}
		}
		return false;
	}
}
