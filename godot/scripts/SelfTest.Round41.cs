using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 117 (Runde 41): Aufträge im echten UI-Fluss — (1) Dock → Bereich „Aufträge“ listet die Angebote der Station,
/// (2) „Annehmen“ nimmt einen Lieferauftrag über den Dienst an (Pfand gehalten, Belohnung reserviert, Angebot weg),
/// (3) Marker sichtbar: HUD-Zeile (Gold), Leuchtfeuer im Sektor, Symbole auf Sektor- und Universumskarte, Comms-Reiter
/// „Aufträge“ listet den aktiven Auftrag; (4) Ware an der Zielstation verkauft → Auftrag erfüllt, Belohnung in der Kasse,
/// Posteingang meldet „Auftrag erfüllt“. Ausführung überall nur über Dienst/Registry bzw. dieselbe Core-Funktion wie die UI.
/// </summary>
public partial class SelfTest
{
	StationId _r41St;
	GoodId _r41Good;
	float _r41Units, _r41Wallet0, _r41Client0, _r41Reserved;
	int _r41OfferId;
	Vector2 _r41At;

	bool Round41Stage(int f)
	{
		var hud = _m.HudRef;
		var u = _m.Session.U;
		switch (f)
		{
			case 1:
			{
				MissionOffer? offer = null;
				for (int i = 0; i < 40 && (offer is null || offer.Id == 0); i++)   // Board erneuert alle 2400 Ticks — weitersuchen, bis ein annehmbares Angebot da ist
				{
					_m.Session.RunTicks(500);
					offer = u.MissionOffers.FirstOrDefault(o => o.Type == "delivery" && u.Stations.TryGetValue(o.At, out var stq) && !stq.IsBuildSite
						&& SpaceSim.Core.Missions.MissionRules.AcceptIssue(u, o.Id, u.Ships[u.PlayerShip], remote: true) is null);
				}
				if (offer is null || offer.Id == 0) { Fail("41: kein annehmbares Lieferangebot erzeugt"); return false; }
				_r41OfferId = offer.Id;
				_r41St = offer.At;
				_r41Good = new GoodId(int.Parse(offer.Args["good"], CultureInfo.InvariantCulture));
				_r41Units = float.Parse(offer.Args["units"], CultureInfo.InvariantCulture);
				_r41Wallet0 = u.Player.Treasury.Credits;
				_r41Client0 = u.Actors[offer.Client].Treasury.Credits;
				_m.TestDockAt(_r41St);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Missions);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 3:
			{
				Check(_m.TestScreenOpen("dock.missions"), "41: Dock-Bereich „Aufträge“ öffnet");
				Check(hud.DockMissions.TestOfferCount >= 1, $"41: Angebote der Station werden angezeigt ({hud.DockMissions.TestOfferCount})");
				var btn = Buttons(hud.DockMissions).FirstOrDefault(b => b.Text == "Annehmen" && b.IsVisibleInTree() && b.State != Ui.Kit.KitButtonState.Disabled);
				Check(btn is not null, "41: Knopf „Annehmen“ vorhanden und aktiv");
				if (btn is null) { Fail("41: Annehmen nicht aktiv: " + hud.DockMissions.TestMessage); return false; }
				_r41At = btn.GetGlobalRect().GetCenter();
				PushMouse(_r41At, true, false);
				return false;
			}
			case 5:
				PushMouse(_r41At, false, false);
				hud.Screens.Current?.Refresh();
				return false;
			case 7:
			{
				var act = u.Missions.Where(m => m.State == MissionState.Active).ToList();
				Check(act.Count == 1, $"41: Auftrag über den Dienst angenommen ({act.Count} aktiv)");
				if (act.Count != 1) { Fail("41: Annahme fehlgeschlagen: " + hud.DockMissions.TestMessage); return false; }
				var m = act[0];
				_r41Reserved = m.Reserved;
				Check(u.MissionOffers.All(o => o.Id != _r41OfferId), "41: Angebot nach der Annahme weg");
				Check(u.Player.Treasury.Credits <= _r41Wallet0 - m.Deposit + 0.01f, $"41: Pfand gehalten ({_r41Wallet0:0} → {u.Player.Treasury.Credits:0} Cr, Pfand {m.Deposit:0} Cr)");
				Check(u.Actors[m.Client].Treasury.Credits <= _r41Client0 - m.Reserved + 0.01f, $"41: Belohnung aus Spendable reserviert ({_r41Client0:0} → {u.Actors[m.Client].Treasury.Credits:0} Cr)");
				Check(SpaceSim.Core.Missions.MissionMarkers.For(u).Count == 1, "41: Marker zum Auftrag (Core: MissionMarkers.For)");
				_m.TestUndock();
				_m.Session.RunTicks(2);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 9:
				Check(hud.Flight.TestMission.Contains("Auftrag"), $"41: HUD-Zeile zeigt den aktiven Auftrag („{hud.Flight.TestMission}“)");
				Check(_m.TestMissionBeacons >= 1, $"41: Leuchtfeuer im Sektor sichtbar ({_m.TestMissionBeacons} Beacon, „{_m.TestMissionBeaconLabels.FirstOrDefault()}“)");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Sector);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			case 11:
				Check(hud.CcSector.TestMissionSymbols >= 1, $"41: Gold-Symbol auf der Sektorkarte ({hud.CcSector.TestMissionSymbols})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Universe);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			case 13:
				Check(hud.CcUniverse.TestMissionSymbols >= 1, $"41: Gold-Symbol auf der Universumskarte ({hud.CcUniverse.TestMissionSymbols})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Comms);
				hud.CcComms.TestSetTab("missions");
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			case 15:
			{
				Check(hud.CcComms.TestTab == "missions" && hud.CcComms.TestMissionRows == 1, "41: Comms-Reiter „Aufträge“ listet den aktiven Auftrag");
				_m.TestCloseCommand();
				_m.TestDockAt(_r41St);
				// Ware an Bord bringen (Test) und an der Zielstation verkaufen — dieselbe Core-Funktion wie der Handelsbildschirm
				var ship = u.Ships[u.PlayerShip];
				ship.Cargo.TryGetValue(_r41Good, out var have);
				ship.Cargo[_r41Good] = have + _r41Units;
				var st = u.Stations[_r41St];
				var sr = SpaceSim.Core.Trading.TradeActions.Sell(u, ship, st, _r41Good, _r41Units);
				sr.Sold.TryGetValue(_r41Good, out var soldN);
				Check(soldN >= _r41Units - 0.01f, $"41: Ware an der Zielstation verkauft ({soldN:0.#}/{_r41Units:0.#} Einheiten)");
				_m.Session.RunTicks(30);   // MissionSystem prüft Fortschritt/Frist alle 20 Ticks
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 17:
			{
				Check(u.Missions.Count == 0, "41: Auftrag erfüllt und aus der Liste entfernt");
				Check(u.Player.Treasury.Credits >= _r41Wallet0 + _r41Reserved - 0.01f, $"41: Belohnung in der Spielerkasse ({_r41Wallet0:0} → {u.Player.Treasury.Credits:0} Cr, reserviert {_r41Reserved:0} Cr)");
				Check(u.Inbox.Any(m => m.Kind == "mission_completed"), "41: Posteingang meldet „Auftrag erfüllt“");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Comms);
				hud.CcComms.TestSetTab("missions");
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 19:
				Check(hud.CcComms.TestMissionRows == 0, "41: Comms-Reiter „Aufträge“ ist nach Erfüllung leer");
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				return true;
		}
		return false;
	}
}
