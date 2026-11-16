using System;
using System.Collections.Generic;
using System.Linq;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 119 (Runde 42b, Handytest-Fixes): (1) Transportauftrag annehmen und über den <b>echten Andockweg der
/// Ansicht</b> (<see cref="Main.TestDockAt"/> → <c>Dock()</c> setzt <c>DockedAt</c> und veröffentlicht ShipDocked) an der Zielstation
/// andocken → erfüllt sofort, Belohnung in der Kasse, Posteingang „Auftrag erfüllt“, Fracht frei; (2) Zwei-Tipp-Bestätigung
/// (KitConfirm) mit <b>2 s Abstand</b> und Neuaufbau dazwischen: erster Tipp „Wirklich?“, zweiter Tipp innerhalb des 3-s-Fensters
/// führt aus (Rückgängig nach einem Kauf), echter Debounce 250 ms; (3) Meldung im Dock-Bereich „Aufträge“ nach erneutem Öffnen
/// zurückgesetzt.
/// </summary>
public partial class SelfTest
{
	int _r42bOfferId;
	StationId _r42bAt, _r42bTarget;
	float _r42bWallet0, _r42bReserved;

	bool Round42bStage(int f)
	{
		var hud = _m.HudRef;
		var u = _m.Session.U;
		if (f == 1) { _m.TestMenu("new:trader"); return false; }   // frisches Händler-Spiel: Nomade mit 100 m³ Laderaum (Kampf-Start hat keinen)
		switch (f)
		{
			case 2:
			{
				if (_m.TestDocked) _m.TestUndock();
				_m.Session.RunTicks(2);
				// saubere Ausgangsbasis: keine laufenden Aufträge, keine Fracht
				foreach (var x in u.Missions.Where(m => m.State == MissionState.Active).ToList())
					SpaceSim.Core.Missions.MissionRules.Abandon(u, x.Id, u.Ships[u.PlayerShip]);
				var me = u.Ships[u.PlayerShip];
				me.Cargo.Clear();
				me.MissionCargo = 0f;
				if (me.CargoCapacity < 40f) me.CargoCapacity = me.Stats.Cargo > 0f ? me.Stats.Cargo : 100f;   // Start-Schiff hat 100 m³ (frühere Stufen können das Frachtmodul ausgebaut haben)
				// Weltzustand für den Test aufdrehen (wie andere Stufen): Kassen der Fraktionen, damit Budget/Spendable reichen, keine Sperren/Fahndung
				foreach (var fac in u.NpcFactionIds())
				{
					var a = u.Actors[new ActorId(fac.Value)];   // Fraktionsakteur: Id = Fraktions-Id
					a.Treasury.Credits = MathF.Max(a.Treasury.Credits, 50_000f);
				}
				u.MissionFactions.Clear();
				u.Actors[u.PlayerActor].Wanted.Clear();
				// Transportangebot suchen (Board erneuert alle 2400 Ticks)
				MissionOffer? offer = null;
				int seenTransport = 0, maxOffers = 0;
				var issues = new Dictionary<string, int>();
				for (int i = 0; i < 60 && offer is null; i++)
				{
					_m.Session.RunTicks(500);
					maxOffers = Math.Max(maxOffers, u.MissionOffers.Count);
					foreach (var o in u.MissionOffers.Where(o => o.Type == SpaceSim.Core.Missions.TransportMission.KeyId))
					{
						seenTransport++;
						var iss = SpaceSim.Core.Missions.MissionRules.AcceptIssue(u, o.Id, u.Ships[u.PlayerShip], remote: true) ?? "OK";
						if (iss == "OK" && u.Stations.TryGetValue(o.At, out var os) && SpaceSim.Core.Simulation.Docking.Issue(u, os, u.Ships[u.PlayerShip]) is not null)
							iss = "Angebots-Station nimmt dieses Schiff nicht (Docking)";
						if (iss == "OK" && u.Stations.TryGetValue(SpaceSim.Core.Missions.TransportMission.TargetOf(o), out var ts2) && SpaceSim.Core.Simulation.Docking.Issue(u, ts2, u.Ships[u.PlayerShip]) is not null)
							iss = "Ziel-Station nimmt dieses Schiff nicht (Docking)";
						issues[iss] = issues.GetValueOrDefault(iss) + 1;
						if (iss == "OK") { offer = o; break; }
					}
				}
				if (offer is null)
				{
					int taxiSt = u.StationsById().Count(s => u.StationTypes.TryGetValue(s.TypeKey, out var t) && (t.Services.Contains("taxi") || t.Services.Contains("hangar")));
					Fail($"42b: kein annehmbares Transport-Angebot erzeugt (transport gesehen {seenTransport}, Angebote max {maxOffers}, taxi/hangar-Stationen {taxiSt}, "
						+ $"Gründe: {string.Join("; ", issues.Select(kv => $"{kv.Key} x{kv.Value}"))}, Laderaum frei {me.CargoCapacity - me.UsedVolume(u) - me.MissionCargo:0.#}/{me.CargoCapacity}, Kasse {u.Player.Treasury.Credits:0})");
					return false;
				}
				_r42bOfferId = offer.Id;
				_r42bAt = offer.At;
				_r42bTarget = u.Stations[SpaceSim.Core.Missions.TransportMission.TargetOf(offer)].Id;
				_r42bWallet0 = u.Player.Treasury.Credits;
				// an der Angebots-Station andocken und im Dock-Bereich „Aufträge“ annehmen (Dienst wie der Knopf)
				_m.TestDockAt(_r42bAt);
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Missions);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 3:
			{
				Check(_m.TestScreenOpen("dock.missions"), "42b: Dock-Bereich „Aufträge“ offen");
				Check(hud.DockMissions.TestOfferCount >= 1, $"42b: Angebote der Station angezeigt ({hud.DockMissions.TestOfferCount})");
				var r = hud.DockMissions.Execute($"{MissionService.Accept}:{_r42bOfferId}");
				Check(r.Success, $"42b: Transport über den Dock-Dienst angenommen ({r.Message})");
				if (!r.Success) { Fail("42b: Annahme fehlgeschlagen: " + r.Message); return false; }
				var m = u.Missions.FirstOrDefault(x => x.State == MissionState.Active && x.Type == SpaceSim.Core.Missions.TransportMission.KeyId);
				Check(m is not null, "42b: Transportauftrag aktiv");
				if (m is null) return false;
				_r42bReserved = m.Reserved;
				Check(u.Ships[u.PlayerShip].MissionCargo >= m.CargoVolume - 0.01f, $"42b: versiegelte Fracht an Bord ({u.Ships[u.PlayerShip].MissionCargo:0.#} m³)");
				// Bereich schließen und wieder öffnen: alte grüne Meldung darf nicht wieder erscheinen
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
				hud.Screens.Current?.Refresh();
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Missions);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				Check(!hud.DockMissions.TestMessage.Contains("angenommen"), $"42b: Meldung nach erneutem Öffnen zurückgesetzt („{hud.DockMissions.TestMessage}“)");
				// abdocken und über den echten Andockweg (TestDockAt → Dock) an der Zielstation andocken
				_m.TestUndock();
				_m.Session.RunTicks(2);
				_m.TestDockAt(_r42bTarget);
				_m.Session.RunTicks(2);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 5:
			{
				Check(!u.Missions.Any(x => x.Type == SpaceSim.Core.Missions.TransportMission.KeyId), "42b: Transport durch Andocken an der Zielstation erfüllt (View-Pfad)");
				Check(u.Ships[u.PlayerShip].MissionCargo <= 0.01f, $"42b: versiegelte Fracht abgeliefert ({u.Ships[u.PlayerShip].MissionCargo:0.#} m³)");
				Check(u.Player.Treasury.Credits >= _r42bWallet0 + _r42bReserved - 0.01f,
					$"42b: Belohnung + Pfand in der Spielerkasse ({_r42bWallet0:0} → {u.Player.Treasury.Credits:0} Cr, reserviert {_r42bReserved:0} Cr)");
				Check(u.Inbox.Any(x => x.Kind == "mission_completed"), "42b: Posteingang meldet „Auftrag erfüllt“");
				// ── Zwei-Tipp-Bestätigung mit 2 s Abstand (KitConfirm) ──
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
				hud.Screens.Current?.Refresh();
				hud.Screens.Current?.Refresh();
				// kaufbare Ware an der Zielstation finden und 1 Einheit kaufen (Journal-Eintrag für „Rückgängig“)
				var st = u.Stations[_r42bTarget];
				var ship = u.Ships[u.PlayerShip];
				GoodId? good = null;
				foreach (var (g, e) in st.Market.OrderBy(kv => kv.Key.Value))
					if (e.Amount >= 1f && e.Price > 0f && !SpaceSim.Core.Simulation.ExportRules.Buys(u, st, g)
						&& SpaceSim.Core.Trading.TradeActions.MaxBuyable(u, ship, st, g) >= 1f) { good = g; break; }
				Check(good is not null, "42b: kaufbare Ware an der Zielstation vorhanden");
				if (good is null) { Fail("42b: keine kaufbare Ware"); return false; }
				float walletBefore = u.Player.Treasury.Credits;
				var buy = hud.DockTrade.Execute($"buy:{good.Value.Value}", 1f);
				Check(buy.Success, $"42b: 1 Einheit gekauft ({buy.Message})");
				if (!buy.Success) { Fail("42b: Kauf fehlgeschlagen: " + buy.Message); return false; }
				hud.DockTrade.Refresh();
				var btn = SpaceAdventure.Ui.Kit.KitConfirm.Find(hud.DockTrade, TradeService.Undo);
				Check(btn is not null, "42b: Rückgängig-Knopf verdrahtet (zwei Taps)");
				if (btn is null) { Fail("42b: Rückgängig-Knopf fehlt"); return false; }
				btn.TestPress();   // erster Tipp → scharf
				Check(btn.Text == "Wirklich?", $"42b: erster Tipp macht scharf („{btn.Text}“)");
				Check(u.Journal.Last is not null, "42b: Buchung im Journal");
				// 2 s vergehen (Uhr vorgeschoben) + Neuaufbau zwischen den Tipps — echter Debounce 250 ms
				var realNow = SpaceAdventure.Ui.Kit.KitConfirm.Now;
				var oldDeb = SpaceAdventure.Ui.Kit.KitConfirm.DebounceMs;
				SpaceAdventure.Ui.Kit.KitConfirm.DebounceMs = 250;
				SpaceAdventure.Ui.Kit.KitConfirm.Now = () => realNow() + 2000;
				_r42bNow = realNow; _r42bDeb = oldDeb;
				_m.Session.RunTicks(1);   // Tickwechsel → Neuaufbau des Bereichs zwischen den Tipps
				hud.DockTrade.Refresh();
				hud.DockTrade.Refresh();
				return false;
			}
			case 7:
			{
				var btn2 = SpaceAdventure.Ui.Kit.KitConfirm.Find(hud.DockTrade, TradeService.Undo);
				Check(btn2 is not null && btn2.Text == "Wirklich?", $"42b: Bestätigung überlebt den Neuaufbau („{btn2?.Text}“)");
				if (btn2 is null) { Fail("42b: Rückgängig-Knopf nach Neuaufbau weg"); RestoreConfirm(); return false; }
				float cargoBefore = u.Ships[u.PlayerShip].Cargo.Values.Sum();
				btn2.TestPress();   // zweiter Tipp nach 2 s (innerhalb des 3-s-Fensters) → ausführen
				Check(u.Journal.Last is null, "42b: zweiter Tipp nach 2 s führt Rückgängig aus (Journal leer)");
				Check(u.Ships[u.PlayerShip].Cargo.Values.Sum() < cargoBefore + 0.01f, "42b: gekaufte Ware nach Rückgängig wieder weg");
				Check(MathF.Abs(u.Player.Treasury.Credits - _r42bWallet0 - _r42bReserved) < 1.5f,
					$"42b: Kauf zurückgebucht ({_r42bWallet0 + _r42bReserved:0} erwartet, {u.Player.Treasury.Credits:0} jetzt)");
				RestoreConfirm();
				_m.TestUndock();
				_m.Session.RunTicks(2);
				return true;
			}
		}
		return false;
	}

	Func<ulong>? _r42bNow;
	ulong _r42bDeb;
	void RestoreConfirm()
	{
		if (_r42bNow is not null) SpaceAdventure.Ui.Kit.KitConfirm.Now = _r42bNow;
		SpaceAdventure.Ui.Kit.KitConfirm.DebounceMs = _r42bDeb;
		_r42bNow = null;
	}
}
