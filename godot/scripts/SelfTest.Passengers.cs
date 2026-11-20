using System.Globalization;
using System.Linq;
using Godot;
using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 151 (Ausbau Punkte 10/14): neues Spiel (Händler), Passagierkabine M eingebaut, angedockt an einem Taxistand; im Dock-Bereich „Aufträge“
/// werden ein Fahrgast- und ein Schmuggelauftrag mit echten Tipps auf „Annehmen“ angenommen (Plätze/versiegelte Fracht belegt), Andocken an den Zielen
/// über den echten Andock-Pfad liefert ab (Lohn in der Kasse); dann bekommt ein Mauersegler der Flotte im Flottenbildschirm per Tipp „Taxi“ und fährt
/// Fahrgäste (Fahrgeld kommt an).
/// </summary>
public partial class SelfTest
{
	int _pxPax, _pxSmug;
	StationId _pxStand, _pxPaxTo, _pxSmugTo;
	ShipId _pxSwift;
	float _pxWallet, _pxFares;
	int _pxTrips;

	bool PassengerStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var fs = hud.CcFleet;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				_m.Session.RunTicks(2);
				var ship = u.Ships[u.PlayerShip];
				u.Rules.SmugglerSearchChance = 0f;   // die Fahrt zwischen den Andockpunkten ist hier kein Thema
				// Kabine S in den freien Utility-Slot der Nomade (wie beim Ausrüster; 2 Plätze)
				int us = Enumerable.Range(0, ship.Slots.Count).FirstOrDefault(i => ship.Slots[i].Type == "utility" && ship.Fit[i] is null, -1);
				if (us < 0) { ship.Slots.Add(new ClassSlot { Type = "utility", Size = SlotSize.S }); ship.Fit.Add(null); us = ship.Fit.Count - 1; }
				ship.Fit[us] = "passenger_cabin_s";
				ship.RecomputeStats(u);
				foreach (var a in u.Actors.Values) if (a.Id != u.PlayerActor && a.Id != u.FreeActor) a.Treasury.Credits = System.Math.Max(a.Treasury.Credits, 200000f);
				var stand = u.StationsById().Where(s => TaxiExecutor.IsStand(u, s) && u.FactionOf(s) is { } fa && u.IsNpcFaction(fa)).OrderBy(s => s.Sector == ship.Sector ? 0 : 1).First();
				_pxStand = stand.Id;
				_m.TestDockAt(stand.Id);
				u.MissionOffers.RemoveAll(o => o.At == stand.Id);
				var pax = MakeOffer(u, PassengerMission.KeyId, stand, o => o.Args["kind"] == "normal" && int.Parse(o.Args["pax"], CultureInfo.InvariantCulture) <= 2);
				var fence = u.StationsById().First(s => SpaceSim.Core.Trading.BlackMarket.IsAt(u, s));
				var smug = MakeOffer(u, SmuggleMission.KeyId, fence, null);
				if (pax is null || smug is null) { Fail("151: keine Angebote erzeugt"); return false; }
				smug.At = stand.Id;   // Test: das Hehler-Angebot hier am Stand anzeigen (gleiche Annahme über den Dienst)
				_pxPax = pax.Id; _pxSmug = smug.Id;
				_pxPaxTo = new StationId(int.Parse(pax.Args["target"], CultureInfo.InvariantCulture));
				_pxSmugTo = new StationId(int.Parse(smug.Args["target"], CultureInfo.InvariantCulture));
				_pxWallet = u.Player.Treasury.Credits;
				u.Events.Raised += e => { if (e is PassengerFarePaid p && p.Ship == _pxSwift) { _pxFares += p.Fare; _pxTrips++; } };
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Missions);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 7:
				GD.Print($"[selftest] 151: Annahme Fahrgast: {MissionRules.AcceptIssue(u, _pxPax, u.Ships[u.PlayerShip], false) ?? "möglich"}, Schmuggel: {MissionRules.AcceptIssue(u, _pxSmug, u.Ships[u.PlayerShip], false) ?? "möglich"}, angedockt {u.Ships[u.PlayerShip].DockedAt.Value} (Stand {_pxStand.Value}), Plätze {u.Ships[u.PlayerShip].Stats.Passengers}");
				hud.Screens.Current?.Refresh();
				return false;
		}
		if (f >= 8 && f < 12) { FoTap(f, 8, () => hud.DockMissions.TestAcceptButton(_pxPax), "Annehmen Fahrgast"); return false; }
		if (f == 13)
		{
			hud.Screens.Current?.Refresh();
			var m = u.Missions.FirstOrDefault(x => x.Type == PassengerMission.KeyId);
			Check(m is not null, $"151: Fahrgastauftrag per Tipp angenommen ({hud.DockMissions.TestMessage})");
			if (m is not null) Check(PassengerMission.FreeSeats(u, u.Ships[u.PlayerShip]) == 2 - PassengerMission.PaxOf(m), "151: Plätze belegt");
			return false;
		}
		if (f >= 15 && f < 19) { FoTap(f, 15, () => hud.DockMissions.TestAcceptButton(_pxSmug), "Annehmen Schmuggel"); return false; }
		switch (f)
		{
			case 20:
			{
				var ship = u.Ships[u.PlayerShip];
				Check(u.Missions.Any(x => x.Type == SmuggleMission.KeyId), $"151: Schmuggelauftrag per Tipp angenommen ({hud.DockMissions.TestMessage})");
				Check(ship.MissionCargo >= u.Rules.MissionSmuggleUnits - 0.01f, "151: Schmuggelware belegt Laderaum");
				Check(MissionMarkers.For(u).Count >= 2, "151: Marker für beide Aufträge");
				_m.TestDockAt(_pxPaxTo);
				_m.Session.RunTicks(2);
				return false;
			}
			case 24:
				Check(!u.Missions.Any(x => x.Type == PassengerMission.KeyId), "151: Andocken am Ziel liefert die Fahrgäste ab");
				_m.TestDockAt(_pxSmugTo);
				_m.Session.RunTicks(2);
				return false;
			case 28:
			{
				Check(u.Missions.Count == 0, $"151: Schmuggelware am Ziel abgeliefert ({string.Join(",", u.Missions.Select(m => m.Type))})");
				Check(u.Player.Treasury.Credits > _pxWallet + 100f, $"151: Lohn in der Kasse ({_pxWallet:0} → {u.Player.Treasury.Credits:0} Cr)");
				var stand = u.Stations[_pxStand];
				_pxSwift = FoOwn(u, "swift", stand).Id;
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Fleet);
				fs.Select(_pxSwift);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			}
		}
		if (f >= 30 && f < 34) { FoTap(f, 30, () => fs.TestCommandButton(CommandKinds.Taxi), "Taxi"); return false; }
		switch (f)
		{
			case 35:
				Check(u.Ships[_pxSwift].Command is TaxiCommand, $"151: „Taxi“ per Tipp gegeben ({fs.TestMessage})");
				_m.Session.RunTicks(4000);
				return false;
			case 37:
				Check(_pxTrips >= 1 && _pxFares > 0f, $"151: Flottentaxi fährt Fahrgäste ({_pxTrips} Fahrten, {_pxFares:0} Cr Fahrgeld)");
				u.Rules.SmugglerSearchChance = new EconomyRules().SmugglerSearchChance;
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				return true;
		}
		return false;
	}

	/// <summary>Angebot einer Art an einer Station (erstes Bucket mit passendem Angebot), eingetragen wie das Brett es tut.</summary>
	static MissionOffer? MakeOffer(Universe u, string type, Station st, System.Func<MissionOffer, bool>? want)
	{
		var t = MissionRules.Types.Get(type)!;
		for (long b = 0; b < 200; b++)
		{
			var o = t.Generate(u, st, MissionRng.For(u, st.Id.Value, b), b);
			if (o is null || (want is not null && !want(o))) continue;
			o.Id = u.NextMissionId++; o.At = st.Id; o.Client = st.Owner; o.Created = u.Tick; o.Expires = u.Tick + 20000;
			u.MissionOffers.Add(o);
			return o;
		}
		return null;
	}
}
