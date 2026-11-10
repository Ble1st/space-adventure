using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Skriptierter Durchlauf ohne Anzeige: godot-mono --headless --path godot --fixed-fps 60 -- --selftest
/// Prüft Autopilot über mehrere Sektoren, Sprünge, Andocken, Oberflächen-Panels, Speichern und Laden, den
/// Tempo-Slider (50 % beschleunigt, Rückwärts höchstens 20 %), die Waffenschacht-Taste (nach dem Schuss lädt
/// sie nach) sowie den Auto-Mining-Kreislauf (Feld anfliegen, abbauen, verkaufen). Am Dock: Bereichsliste
/// aus den Stationsdiensten (Handel nur mit „trade“, Platzhalter inaktiv), Kauf/Verkauf/„Alles verkaufen“
/// über den Dienst und die Handel-Oberfläche (Execute-Hook) mit Prüfung von Credits und Fracht, Abdocken
/// aus einem offenen Bereich. Kommandozentrale (UI.md Abschnitt 4): jeder Bereich lässt sich öffnen und
/// aktualisieren ohne Ausnahme, die Universum-Sektorwahl liefert Route/Sprungzahl passend zu
/// Universe.FindRoute, „Route planen/Autopilot“ setzt ein Autopilot-Ziel und „← Flug“ schließt alles.
/// Beendet mit Exit-Code 0 bei Erfolg, sonst 1.
/// Kamera (UI.md Abschnitt 2): (a) Standardansicht == früherer fester Offset (0, 38, 24), (b) Test-Hook Yaw 90°/Pitch 45° –
/// Kamera blickt weiter aufs Schiff und hält den Abstand, (c) Bildschirm→Welt-Roundtrip des Schiffsmittelpunkts mit
/// gedrehter Kamera, (d) „Kamera ↺“ setzt auf die Standardansicht zurück.
/// </summary>
public partial class SelfTest : Node
{
	readonly Main _m;
	int _frame, _stage;
	string _fail = "";
	SectorId _startSector;
	int _stageStart;

	public SelfTest(Main m) { _m = m; }

	public override void _Process(double delta)
	{
		_frame++;
		var u = _m.Session.U;
		switch (_stage)
		{
			case 0: // Autopilot zur Werft Delta (3 Sektoren weiter)
				_startSector = u.PlayerSector;
				var yard = u.Stations.Values.First(s => s.TypeKey == "shipyard");
				_m.TestSetStationTarget(yard.Id);
				Log($"Start in {u.Sectors[_startSector].Name}, Ziel {yard.Name}");
				Next(); break;
			case 1:
				if (_m.Docked) { Log($"Angedockt in {u.Sectors[u.PlayerSector].Name} nach {_frame} Frames, Tick {u.Tick}"); Check(u.PlayerSector.Value == 4, "Dock-Sektor ist Delta"); Check(_m.TestHits == 0, $"Autopilot-Flug ohne harte Kollision (Treffer: {_m.TestHits})"); Next(); }
				else if (_frame - _stageStart > 12_000) Fail("Autopilot erreichte das Ziel nicht");
				break;
			case 2: // Panels + Dock-Oberfläche: Bereichsliste aus den Daten, Kauf/Verkauf über den Dienst und die neue Oberfläche
				_m.HudRef.Economy.Visible = true; _m.HudRef.Economy.Refresh(_m.Session, force: true);
				_m.HudRef.Map.Open(u, u.PlayerSector, null);
				_m.HudRef.Screens.Show(_m.HudRef.Command);
				_m.HudRef.Map.Visible = false; _m.HudRef.Screens.Hide();
				{ // Bereichsliste: Handel an einer Handelsstation, Platzhalter nicht aktiv
					var areas = _m.TestDockAreas();
					var st = u.Stations[_m.TestDockedStation!.Value];
					bool tradeOffered = _m.Session.Services.ForStation(u, st).Any(s => s.Key == "trade");
					Check(areas.Any(a => a.Id == Ui.Screens.DockAreas.Trade && a.Available) == tradeOffered,
						"Bereichsliste enthält Handel genau dann, wenn ForStation den Dienst trade liefert");
					{ // Negativfall: Stationstyp ohne Dienste (services: []) zeigt keinen Handelsbereich, Info/Hangar bleiben
						var type = u.StationTypes[st.TypeKey];
						var saved = type.Services;
						type.Services = new List<string>();
						var bare = Ui.Screens.DockAreas.Build(u, st, _m.Session.Services);
						type.Services = saved;
						Check(!bare.Any(a => a.Id == Ui.Screens.DockAreas.Trade), "Stationstyp ohne Dienste: kein Handelsbereich");
						Check(bare.Any(a => a.Id == Ui.Screens.DockAreas.Info && a.Available) && bare.Any(a => a.Id == Ui.Screens.DockAreas.Hangar && a.Available),
							"Stationstyp ohne Dienste: Station-Info und Hangar bleiben aktiv");
						Check(Ui.Screens.DockAreas.Build(u, st, _m.Session.Services).Any(a => a.Id == Ui.Screens.DockAreas.Trade) == tradeOffered, "Dienste-Liste nach dem Negativtest wiederhergestellt");
					}
					Check(areas.Any(a => a.Id == Ui.Screens.DockAreas.Info && a.Available) && areas.Any(a => a.Id == Ui.Screens.DockAreas.Hangar && a.Available),
						"Station-Info und Hangar sind immer aktiv");
					Check(areas.Where(a => a.Id is Ui.Screens.DockAreas.Taxi or Ui.Screens.DockAreas.Missions)
						.All(a => !a.Available), "Platzhalterbereiche (Taxi, Aufträge) sind nicht aktiv");
					var offered = _m.Session.Services.ForStation(u, st).Select(x => x.Key).ToList();
					Check(areas.Any(a => a.Id == Ui.Screens.DockAreas.Outfitter && a.Available) == offered.Contains("outfitter")
						&& areas.Any(a => a.Id == Ui.Screens.DockAreas.Shipyard && a.Available) == offered.Contains("shipdealer"),
						"Ausrüster und Werft sind genau dann aktiv, wenn ForStation outfitter bzw. shipdealer liefert (Phase 5c)");
					Check(offered.Contains("outfitter") && offered.Contains("shipdealer"), "Werft Delta bietet Ausrüster und Schiffskauf");
					bool repairOrFuel = _m.Session.Services.ForStation(u, st).Any(s => s.Key is "refuel" or "repair");
					Check(areas.Any(a => a.Id == Ui.Screens.DockAreas.Repair && a.Available) == repairOrFuel,
						"Reparatur & Tanken ist genau dann aktiv, wenn ForStation refuel oder repair liefert");
				}
				{ // Phase 4c: Verbrauch im Flug, Reparatur an der Werft und Tanken über den Dock-Bereich (nur Offers/Execute)
					var st = u.Stations[_m.TestDockedStation!.Value];
					var pl = _m.Session.Player; _m.Session.U.PlayerWallet.Credits = 100000f;
					Check(pl.TotalFuel < pl.Stats.FuelCapacity && pl.TotalFuel > 0f, $"Spielerflug hat Treibstoff verbraucht (Tank {pl.TotalFuel:0.0}/{pl.Stats.FuelCapacity:0})");
					var plates = u.FindGood("hull_plates")!.Id;
					st.Market[plates].Amount = System.Math.Max(st.Market[plates].Amount, 20f);
					pl.Hull = pl.MaxHull - 30f;
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Repair);
					Check(_m.TestScreenOpen("dock.repair"), "Bereich Reparatur & Tanken an der Werft geöffnet");
					Check(_m.HudRef.DockRepair.TestRowCount >= 2, "Reparatur zeigt Zeilen aus IStationService.Offers");
					float platesBefore = st.Market[plates].Amount, crBefore = _m.Session.U.PlayerWallet.Credits;
					var rep = _m.HudRef.DockRepair.Execute("repair", "repair:full");
					Check(rep.Success && pl.Hull >= pl.MaxHull - 0.01f, "Reparatur über Dienst+Oberfläche stellt die Hülle her");
					Check(st.Market[plates].Amount < platesBefore && _m.Session.U.PlayerWallet.Credits < crBefore, "Reparatur verbraucht Hüllenplatten der Station und kostet Credits");
					Check(_m.HudRef.DockRepair.TestMessage.Length > 0, "Ergebnismeldung der Reparatur wird angezeigt");
					// Tanken: dieselbe Seite mit einer Raffinerie als Station (die Werft verkauft keinen Treibstoff)
					var refinery = u.Stations.Values.First(x => x.TypeKey == "fuel_refinery");
					var prop = u.FindGood("propellant")!.Id;
					refinery.Market[prop].Amount = System.Math.Max(refinery.Market[prop].Amount, 60f);
					_m.HudRef.DockCtx.Station = refinery;
					pl.Fuel[prop] = 100f;
					crBefore = _m.Session.U.PlayerWallet.Credits;
					var fuel = _m.HudRef.DockRepair.Execute("refuel", "refuel:25");
					Check(fuel.Success && System.Math.Abs(pl.TotalFuel - 125f) < 0.01f, "Tanken über Dienst+Oberfläche füllt den Tank um 25");
					Check(_m.Session.U.PlayerWallet.Credits < crBefore, "Tanken kostet Credits");
					_m.HudRef.DockCtx.Station = st;
				}
				{ // Handel öffnen und über die Oberfläche (Dienst Execute) kaufen/verkaufen
					var st = u.Stations[_m.TestDockedStation!.Value];
					var pl = _m.Session.Player; _m.Session.U.PlayerWallet.Credits = 100000f;
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
					Check(_m.TestScreenOpen("dock.trade"), "Handel-Bereich über das Menü geöffnet");
					Check(_m.HudRef.DockTrade.TestRowCount > 0, "Handel zeigt Zeilen aus IStationService.Offers");
					var gid = st.Market.First(k => k.Value.Amount >= 5f && k.Value.Price > 0f).Key;
					float before = pl.Cargo.GetValueOrDefault(gid);
					float creditsBefore = _m.Session.U.PlayerWallet.Credits;
					float price = st.Market[gid].Price;
					var buy = _m.HudRef.DockTrade.TestExecute($"buy:{gid.Value}", 5f);
					Check(buy.Success && buy.Units == 5f, "Kaufen über Dienst+Oberfläche (Execute buy)");
					Check(pl.Cargo.GetValueOrDefault(gid) == before + 5f, "Fracht nach Kauf um 5 erhöht");
					Check(System.Math.Abs(_m.Session.U.PlayerWallet.Credits - (creditsBefore - 5f * price)) < 0.01f, "Credits nach Kauf um 5 × Preis gesunken");
					Check(_m.HudRef.DockTrade.TestMessage.Length > 0, "Ergebnismeldung (ServiceResult.Message) wird angezeigt");
					var sell = _m.HudRef.DockTrade.TestExecute($"sell:{gid.Value}", 5f);
					Check(sell.Success && sell.Units == 5f, "Verkaufen über Dienst+Oberfläche (Execute sell)");
					Check(pl.Cargo.GetValueOrDefault(gid) == before, "Fracht nach Verkauf wieder auf Anfangsstand");
					Check(System.Math.Abs(_m.Session.U.PlayerWallet.Credits - creditsBefore) < 0.01f, "Credits nach Verkauf wieder hergestellt");
					Check(pl.Credits == 0f && u.WalletOf(pl) == u.PlayerWallet, "Handel bucht auf die Spielerkasse, das Spielerschiff selbst hat keine Credits (Phase 5b)");
					// Alles verkaufen über sell_all
					_m.HudRef.DockTrade.TestExecute($"buy:{gid.Value}", 3f);
					var all = _m.HudRef.DockTrade.TestExecute("sell_all", 0f);
					Check(all.Success && pl.Cargo.GetValueOrDefault(gid) == 0f, "„Alles verkaufen“ (sell_all) über die Oberfläche räumt die Ware ab");
				}
				{ // Phase 5a: Modul-Waren im Werftlager (stocks) erscheinen im Handel und lassen sich über die Oberfläche kaufen
					var st = u.Stations[_m.TestDockedStation!.Value];
					var pl = _m.Session.Player; _m.Session.U.PlayerWallet.Credits = 100000f;
					var reactor = u.FindGood("reactor_mk1")!.Id;
					Check(st.Market.ContainsKey(reactor) && st.IsStockOnly(reactor), "Werft führt den Reaktor als Lagerware ohne Rezept (stocks)");
					st.Market[reactor].Amount = System.Math.Max(st.Market[reactor].Amount, 3f);
					var offers = _m.Session.Services.ForStation(u, st).First(s => s.Key == "trade").Offers(u, st, pl);
					Check(offers.Any(o => o.Key == $"buy:{reactor.Value}"), "Handel bietet die Modul-Ware reactor_mk1 an (IStationService.Offers)");
					var b = _m.HudRef.DockTrade.TestExecute($"buy:{reactor.Value}", 1f);
					Check(b.Success && pl.Cargo.GetValueOrDefault(reactor) == 1f, "Modul-Ware über Dienst+Oberfläche gekauft (landet im Laderaum)");
					var s = _m.HudRef.DockTrade.TestExecute($"sell:{reactor.Value}", 1f);
					Check(s.Success && pl.Cargo.GetValueOrDefault(reactor) == 0f, "Modul-Ware zurückverkauft");
				}
				{ // Phase 5c: Ausrüster – Frachtmodul tauschen über die Oberfläche (nur Offers/Execute), ShipStats ändern sich
					var st = u.Stations[_m.TestDockedStation!.Value];
					var pl = _m.Session.Player; _m.Session.U.PlayerWallet.Credits = 100000f;
					int cargoSlot = pl.Slots.FindIndex(x => x.Type == "cargo");
					var g60 = u.FindGood("cargo_universal_60")!.Id;
					st.Market[g60].Amount = System.Math.Max(st.Market[g60].Amount, 2f);
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Outfitter);
					Check(_m.TestScreenOpen("dock.outfitter"), "Ausrüster-Bereich über das Menü geöffnet");
					_m.HudRef.DockOutfitter.TestSelectSlot(cargoSlot);
					Check(_m.HudRef.DockOutfitter.TestRowCount > 0, "Ausrüster zeigt Zeilen aus IStationService.Offers für den Frachtslot");
					Check(_m.HudRef.DockOutfitter.TestSlotOffers().Any(o => o.Key == $"fit:{cargoSlot}:cargo_universal_60" && o.Amount >= 1f), "Angebot: Universalfrachtraum 60 einbauen");
					float cr = _m.Session.U.PlayerWallet.Credits;
					var g100 = u.FindGood("cargo_universal_100")!.Id;
					float expect = cr - st.Market[g60].Price + st.Market[g100].Price;   // Einbau aus dem Lager, alter Frachtraum an die Station
					var fit = _m.HudRef.DockOutfitter.Execute($"fit:{cargoSlot}:cargo_universal_60");
					Check(System.Math.Abs(_m.Session.U.PlayerWallet.Credits - expect) < 0.05f, "Umrüsten bucht Einbaupreis und Ankauf des alten Moduls auf die Spielerkasse");
					Check(fit.Success && pl.Fit[cargoSlot] == "cargo_universal_60" && pl.Stats.Cargo == 60f && pl.CargoCapacity == 60f, "Modul getauscht: Laderaum 100 → 60 (ShipStats)");
					Check(_m.HudRef.DockOutfitter.TestMessage.Length > 0, "Ergebnismeldung des Ausrüsters wird angezeigt");
					var bad = _m.HudRef.DockOutfitter.Execute($"fit:{cargoSlot}:reactor_mk1");
					Check(!bad.Success && pl.Fit[cargoSlot] == "cargo_universal_60", "Ungültiger Einbau (falsche Slotart) wird abgelehnt");
					var back = _m.HudRef.DockOutfitter.Execute($"fit:{cargoSlot}:cargo_universal_100");
					Check(back.Success && pl.Stats.Cargo == 100f, "Zurückgetauscht auf Laderaum 100");
				}
				{ // Phase 5c: Werft – Schiff kaufen (Bausatz + Module aus dem Lager), neues Schiff aktiv, altes im Hangar; zurückwechseln
					var st = u.Stations[_m.TestDockedStation!.Value];
					var old = _m.Session.Player;
					_m.Session.U.PlayerWallet.Credits = 100000f;
					foreach (var n in SpaceSim.Core.Simulation.ShipPurchase.Materials(u, u.ShipModels["prospector"])!)
						st.Stock(n.Good).Amount = System.Math.Max(st.Stock(n.Good).Amount, 3f);
					var kit = u.FindGood("ship_kit_light")!.Id;
					float kits = st.Market[kit].Amount;
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Shipyard);
					Check(_m.TestScreenOpen("dock.shipyard"), "Werft-Bereich über das Menü geöffnet");
					Check(_m.HudRef.DockShipyard.TestRowCount >= 3, $"Werft zeigt Modelle aus IStationService.Offers ({_m.HudRef.DockShipyard.TestRowCount})");
					var buy = _m.HudRef.DockShipyard.Execute("buy:prospector");
					var now = _m.Session.Player;
					Check(buy.Success && now.Id != old.Id && now.ModelKey == "prospector", "Schiff über die Werft-Oberfläche gekauft, neues Schiff ist aktiv");
					Check(st.Market[kit].Amount == kits - 1f, "Kauf verbraucht einen leichten Bausatz aus dem Werftlager");
					Check(old.IsPlayer && SpaceSim.Core.Simulation.PlayerFleet.ParkedAt(u, st).Contains(old), "Altes Schiff bleibt als Spielerbesitz an der Werft");
					Check(_m.HudRef.DockCtx.Ship == now, "Dock-Kontext zeigt das neue Schiff");
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Hangar);
					Check(_m.HudRef.DockHangar.TestParkedRows >= 1, "Hangar zeigt das geparkte eigene Schiff");
					_m.TestSwitchShip(old.Id);
					Check(_m.Session.Player == old && _m.HudRef.DockCtx.Ship == old, "Hangar „Übernehmen“ wechselt zurück ins alte Schiff");
					Check(SpaceSim.Core.Simulation.PlayerFleet.ParkedAt(u, st).Contains(now), "Gekauftes Schiff liegt jetzt im Hangar der Werft");
				}
				Next(); break;
			case 3: // Abdocken aus einem offenen Bereich heraus
				_m.TestUndock();
				Check(!_m.Docked, "Abdocken aus dem Handelsbereich");
				Check(_m.HudRef.Screens.Current == null, "Dock-Bildschirme nach dem Abdocken geschlossen");
				{ // Phase 4c: leerer Tank → Notantrieb, Notruf aus dem Menü füllt bis zur nächsten Tankstelle
					var pl = _m.Session.Player;
					var prop = u.FindGood("propellant")!.Id;
					float saved = pl.TotalFuel;
					pl.Fuel[prop] = 0f;
					Check(SpaceSim.Core.Flight.PlayerFuel.ThrottleCap(pl) < 1f, "Leerer Tank deckelt das Tempo (Notantrieb)");
					float cr = _m.Session.U.PlayerWallet.Credits;
					_m.TestCommand("emergency");
					Check(pl.TotalFuel > 0f && _m.Session.U.PlayerWallet.Credits < cr, $"Notruf liefert Treibstoff gegen Credits (Tank {pl.TotalFuel:0.0})");
					Check(SpaceSim.Core.Flight.PlayerFuel.ThrottleCap(pl) >= 1f, "Nach dem Notruf wieder volles Tempo");
					pl.Fuel[prop] = saved;
				}
				Next(); break;
			case 4: // Speichern, Spiel verändern, Laden
				Check(_m.TestSave(), "Speichern");
				_m.Session.U.PlayerWallet.Credits = 12345;
				_m.TestMenu("load");
				Next(); break;
			case 5:
				Check(_m.Session.U.PlayerSector.Value == 4, "Nach Laden im Sektor Delta");
				Check(_m.Session.U.PlayerWallet.Credits != 12345, "Laden stellt gespeicherten Stand her");
				Next(); break;
			case 6: // Wirtschaft läuft weiter, NPC-Schiffe werden dargestellt
				if (_frame - _stageStart > 600)
				{
					Log($"Tick {_m.Session.U.Tick}, Stationen {_m.Session.U.Stations.Count}, Schiffe {_m.Session.U.Ships.Count}");
					Next();
				}
				break;
			case 7: // Tempo-Slider auf 50 %: Zielfunktion im Core und Beschleunigung des Schiffs
				_m.TestSetSlider(0.5f);
				Check(System.Math.Abs(_m.TestTargetSpeed - 22.5f) < 0.01f, "Slider 50 % → Zieltempo 22,5 m/s");
				Next(); break;
			case 8:
				if (_m.TestForwardSpeed > 15f)
				{
					Log($"Slider 50 %: Vorwärtsgeschwindigkeit {_m.TestForwardSpeed:0.0} m/s");
					Check(_m.TestForwardSpeed <= 22.6f, "Slider 50 % wird nicht schneller als das Zieltempo");
					Check(_m.TestRightTurnNoseX > 0.01f, "Rechts-Taste dreht die Nase nach rechts");
					_m.TestSetSlider(0f);
					_m.TestSetReverse(true);
					Check(System.Math.Abs(_m.TestTargetSpeed + 9f) < 0.01f, "Rückwärts-Haltetaste → -9 m/s (20 % von 45), unabhängig vom Slider");
					Next();
				}
				else if (_frame - _stageStart > 900) Fail("Schiff beschleunigt nicht auf Slider-Tempo");
				break;
			case 9:
				if (_m.TestForwardSpeed < -5f)
				{
					Check(_m.TestForwardSpeed >= -9.6f, $"Rückwärtsfahrt höchstens 20 % (ist {_m.TestForwardSpeed:0.0} m/s, Ziel -9 m/s)");
					Log($"Rückwärts {_m.TestForwardSpeed:0.0} m/s mit Rückwärts-Taste");
					_m.TestSetReverse(false);
					_m.TestSetSlider(0f);
					Next();
				}
				else if (_frame - _stageStart > 900) Fail($"Schiff fährt nicht rückwärts (v={_m.TestForwardSpeed:0.0})");
				break;
			case 10: // Waffenschacht-Taste: Schiff neben einen Asteroiden setzen (Ziel in Laserreichweite)
				_m.TestAimAtNearestRock();
				Next(); break;
			case 11:
				if (_frame - _stageStart > 3)
				{
					Check(_m.TestBayState(0) == "Ready", "Waffentaste Laser bereit (Ziel im 360°-Winkel, in Reichweite)");
					_m.TestFireWeapon();
					Next();
				}
				break;
			case 12:
				if (_frame - _stageStart > 2)
				{
					Check(!_m.TestLaserReady, "Laser lädt nach dem Schuss nach");
					Check(_m.TestBayState(0) == "Reloading", "Waffentaste zeigt Nachladen (grau mit Balken)");
					Next();
				}
				break;
			case 13: // Nachladezeit abwarten, dann ist die Waffe wieder bereit
				if (_m.TestLaserReady) { Check(_m.TestBayState(0) == "Ready", "Waffentaste wieder bereit nach dem Nachladen"); Next(); }
				else if (_frame - _stageStart > 120) Fail("Laser lädt nicht nach");
				break;
			case 14: // Auto-Mining: aus Delta zum Feld fliegen, abbauen
				_m.TestStartMining();
				Check(_m.TestMinerEnabled, "Auto-Mining gestartet");
				Next(); break;
			case 15:
				if (_m.TestCargoUnits > 10f) { Log($"Fracht {_m.TestCargoUnits:0} nach {_frame - _stageStart} Frames in {_m.Session.U.Sectors[_m.Session.U.PlayerSector].Name} ({_m.TestMinerStatus})"); Next(); }
				else if (!_m.TestMinerEnabled) Fail("Auto-Mining beendete sich: " + _m.TestMinerStatus);
				else if (_frame - _stageStart > 30_000) Fail("Keine Fracht abgebaut, Status: " + _m.TestMinerStatus);
				break;
			case 16: // verkaufen
				if (_m.TestSales >= 1) { Log($"Verkauf nach {_frame - _stageStart} Frames, Credits {_m.Session.U.PlayerWallet.Credits:0}"); Check(_m.Session.U.PlayerWallet.Credits > 500f, "Credits gestiegen"); Check(_m.TestHits == 0, $"Auto-Mining ohne harte Kollision (Treffer: {_m.TestHits}, niedrigste Hülle {_m.TestLowestHull:0}) – {_m.TestLastHit}"); Next(); }
				else if (_frame - _stageStart > 40_000) Fail("Kein Verkauf, Status: " + _m.TestMinerStatus);
				break;
			case 17: // Kommandozentrale (UI.md Abschnitt 4): alle Bereiche öffnen + aktualisieren ohne Ausnahme
				try
				{
					if (_m.Docked) _m.TestUndock();   // nach dem Auto-Mining-Verkauf kann noch angedockt sein
					foreach (var id in new[] { "universe", "sector", "factions", "myfaction", "fleet", "assets", "comms", "system" })
					{
						_m.TestOpenCcArea(id);
						_m.HudRef.Screens.Current?.Refresh();
						_m.HudRef.Screens.Current?.Refresh();
						Check(_m.TestScreenOpen("cc." + id), $"Kommandozentralen-Bereich „{id}“ öffnet und aktualisiert ohne Ausnahme");
					}
					// Universum: Sektorauswahl liefert Route/Sprungzahl passend zu Universe.FindRoute
					var target = u.Sectors.Keys.First(s => s != u.PlayerSector);
					_m.TestOpenCcArea("universe");
					_m.TestCcSelectSector(target);
					int expect = u.FindRoute(u.PlayerSector, target)?.Count ?? -1;
					Check(_m.TestCcRouteHops == expect, $"Sektorauswahl: Sprungzahl {_m.TestCcRouteHops} passt zu FindRoute ({expect})");
					// „Route planen/Autopilot“ setzt ein Ziel; „← Flug“ schließt alles
					_m.TestCcPlanRoute();
					Check(_m.TestAutopilotTarget is { } t && t.Sector == target, "„Route planen/Autopilot“ setzt das Autopilot-Ziel in Main");
					_m.TestCloseCommand();
					Check(_m.HudRef.Screens.Current == null && _m.HudRef.Flight.Visible, "„← Flug“ schließt die Kommandozentrale und zeigt den Flug-HUD");
				}
				catch (System.Exception ex) { Fail("Kommandozentrale warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			case 18: // Kamera (a): Standardansicht == früherer fester Offset (0, 38, 24), ohne dass eine Eingabe fiel
				if (_frame - _stageStart > 30)
				{
					var (yaw, pitch, dist) = _m.TestCamAngles;
					Check(System.Math.Abs(yaw - OrbitCamera.DefaultYawDeg) < 0.01f
						&& System.Math.Abs(pitch - OrbitCamera.DefaultPitchDeg) < 0.01f
						&& System.Math.Abs(dist - OrbitCamera.DefaultDistance) < 0.01f,
						"Kamera startet in der Standardansicht (Yaw 0, Standard-Neigung/-Abstand)");
					var off = _m.TestCamOffset;
					var legacy = new Vector3(0, OrbitCamera.LegacyOffsetY, OrbitCamera.LegacyOffsetZ);
					Check(off.DistanceTo(legacy) < 0.5f, $"Standard-Kameraoffset == früherer fester Offset (0,38,24); ist {off}");
					Next();
				}
				break;
			case 19: // Kamera (b): Test-Hook setzt Yaw 90°, Pitch 45°, Abstand 50
				_m.TestSetCam(90f, 45f, 50f);
				Next(); break;
			case 20:
				if (_frame - _stageStart > 5)
				{
					var cp = _m.TestCamPos;
					var pp = _m.Player.Position;
					float d = cp.DistanceTo(pp);
					Check(System.Math.Abs(d - 50f) < 0.5f, $"Gedrehte Kamera: Abstand zum Schiff == eingestellte Distanz 50 (ist {d:0.00})");
					Check((pp - cp).Normalized().Dot(_m.TestCamForward) > 0.999f, "Gedrehte Kamera blickt weiter direkt auf das Schiff (Blickrichtung zeigt aufs Schiff)");
					// (c): Bildschirm → Welt mit gedrehter Kamera: Schiffsmittelpunkt projizieren und zurückrechnen
					var screen = _m.TestProject(pp);
					var ground = _m.TestGroundPoint(screen);
					Check(ground.HasValue && new Vector2(ground.Value.X - pp.X, ground.Value.Z - pp.Z).Length() < 0.5f,
						$"Bildschirm→Welt-Roundtrip des Schiffsmittelpunkts mit gedrehter Kamera (Abweichung {(ground.HasValue ? new Vector2(ground.Value.X - pp.X, ground.Value.Z - pp.Z).Length() : -1):0.000})");
					Next();
				}
				break;
			case 21: // Kamera (d): „Kamera ↺“ setzt zurück
				_m.TestResetCam();
				Next(); break;
			case 22:
				if (_frame - _stageStart > 120)   // weiches Nachführen abwarten
				{
					var (yaw, pitch, dist) = _m.TestCamAngles;
					Check(System.Math.Abs(yaw - OrbitCamera.DefaultYawDeg) < 0.01f
						&& System.Math.Abs(pitch - OrbitCamera.DefaultPitchDeg) < 0.01f
						&& System.Math.Abs(dist - OrbitCamera.DefaultDistance) < 0.01f,
						"„Kamera ↺“ setzt Yaw/Pitch/Abstand auf die Standardansicht zurück");
					var off = _m.TestCamOffset;
					var legacy = new Vector3(0, OrbitCamera.LegacyOffsetY, OrbitCamera.LegacyOffsetZ);
					Check(off.DistanceTo(legacy) < 0.05f, $"„Kamera ↺“: Kamera-Offset == früherer fester Offset (0,38,24); ist {off}");
					Next();
				}
				break;
			case 23: // Touch/Maus steuert nur die Kamera: ein Wisch schwenkt, gibt dem Schiff aber keinen Befehl
			{
				_m.TestStopAutomation();   // Autopilot/Auto-Mining würden selbst Tempo geben
				float yawBefore = _m.TestCamAngles.Yaw;
				float throttle = _m.TestSwipe(new Vector2(600, 300), new Vector2(700, 300));
				float yawAfter = _m.TestCamAngles.Yaw;
				Check(System.Math.Abs(throttle) < 0.001f, $"Wisch gibt dem Schiff keine Flugrichtung/kein Tempo (Soll {throttle:0.00}, Auto-Mining {_m.TestMinerEnabled})");
				Check(System.Math.Abs(OrbitCamera.ShortestAngleDelta(yawBefore, yawAfter)) > 5f, $"Wisch schwenkt die Kamera (Yaw {yawBefore:0} → {yawAfter:0})");
				Check(OrbitCamera.ShortestAngleDelta(yawBefore, yawAfter) < 0f, "Wisch nach rechts dreht die Kamera nach links um das Schiff (Szene folgt dem Finger)");
				_m.TestCameraAction("cam_invert");
				Check(_m.TestCamInvert, "Menü: Schwenken invertieren schaltet um");
				float y2 = _m.TestCamAngles.Yaw;
				_m.TestSwipe(new Vector2(600, 300), new Vector2(700, 300));
				Check(OrbitCamera.ShortestAngleDelta(y2, _m.TestCamAngles.Yaw) > 0f, "Invertiert: Wisch nach rechts dreht die Kamera nach rechts");
				_m.TestCameraAction("cam_invert");
				Next(); break;
			}
			case 24: // Hamburger-Menü: Zoom und Modus
			{
				_m.TestResetCam();
				float d0 = _m.TestCamAngles.Dist;
				_m.TestCameraAction("zoom_in");
				Check(_m.TestCamAngles.Dist < d0 - 1f, "Menü: Zoom + geht näher heran");
				_m.TestCameraAction("zoom_out");
				Check(System.Math.Abs(_m.TestCamAngles.Dist - d0) < 0.5f, "Menü: Zoom − geht zurück");
				_m.TestCameraAction("cam_lock");
				Check(_m.TestCamMode == CameraMode.TargetLock, "Menü: Target-Lock aktiv");
				Next(); break;
			}
			case 25: // Target-Lock: Ziel (Station im Sektor) liegt vor dem Schiff, Kamera dahinter
			{
				var uni = _m.Session.U;
				var st = uni.Stations.Values.FirstOrDefault(x => x.Sector == uni.PlayerSector);
				if (st == null) { Log("Target-Lock-Test übersprungen: keine Station im Sektor"); _m.TestCameraAction("cam_free"); Next(); break; }
				_m.TestSetStationTarget(st.Id);
				Check(_m.TestHasLockTarget, "Target-Lock hat ein Ziel (Station)");
				Next(); break;
			}
			case 26:
				if (_frame - _stageStart > 240)   // weiches Nachführen abwarten
				{
					if (_m.TestLockGeometry() is { } g)
					{
						float dot = g.ToTarget.Dot(g.ToCamera);
						// Das Schiff driftet noch und die Kamera folgt weich (kleiner Nachlauf): Toleranz ≈ 30°
						Check(dot < -0.85f, $"Target-Lock: Kamera steht auf der dem Ziel abgewandten Seite (Skalarprodukt {dot:0.00})");
					}
					_m.TestCameraAction("cam_free");
					Check(_m.TestCamMode == CameraMode.Free, "Menü: Freie Kamera aktiv");
					Next();
				}
				break;
			default:
				if (_fail.Length == 0) { GD.Print("SELFTEST OK"); GetTree().Quit(0); }
				else { GD.PrintErr("SELFTEST FAILED: " + _fail); GetTree().Quit(1); }
				SetProcess(false);
				break;
		}
	}

	void Next() { _stage++; _stageStart = _frame; }
	void Log(string s) => GD.Print("[selftest] " + s);
	void Check(bool ok, string what) { if (!ok) { _fail += what + "; "; GD.PrintErr("[selftest] FEHLER: " + what); } else Log("ok: " + what); }
	void Fail(string what) { _fail += what + "; "; GD.PrintErr("[selftest] FEHLER: " + what); _stage = 99; }
}
