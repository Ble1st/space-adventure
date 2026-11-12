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
	StationId _siteId;     // Phase 8: Test-Baustelle
	int _splitRock;        // Phase 8: gespaltener Asteroid
	string? _savedTool;    // Phase 8: Werkzeug-Modul vor dem Spaltlaser-Test

	ShipId _firstShip, _secondShip;   // Phase 12: erstes Schiff und Zweitschiff der Spielerflotte
	int _trades;
	void CountTrades(SpaceSim.Core.Events.SimEvent e) { if (e is SpaceSim.Core.Events.TradeCompleted t && t.Ship == _secondShip) _trades++; }

	public SelfTest(Main m) { _m = m; SpaceAdventure.Ui.Kit.KitConfirm.DebounceMs = 0; } // der Selbsttest tippt ohne Pause doppelt

	public override void _Process(double delta)
	{
		_frame++;
		var u = _m.Session.U;
		switch (_stage)
		{
			case 0: // Autopilot zur Werft Delta (3 Sektoren weiter)
				_startSector = u.PlayerSector;
				// Phase 11: Piraten überfallen den Spieler erst im eigenen Überfall-Test (sonst verändern sie Fracht und Kasse der Handelsprüfungen)
				u.Rules.PlayerRaids = false;
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
					var offered = _m.Session.Services.ForStation(u, st).Select(x => x.Key).ToList();
					Check(areas.Where(a => a.Id is Ui.Screens.DockAreas.Missions).All(a => !a.Available), "Platzhalterbereich Aufträge ist nicht aktiv");
					Check(areas.Any(a => a.Id == Ui.Screens.DockAreas.Taxi && a.Available) == offered.Contains("taxi"), "Phase 12: Taxi echt, wenn die Station den Dienst „taxi“ liefert");
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
					// Phase 16: Stahl am Stahlwerk — der Hersteller nimmt sein reines Produkt nicht an; die Zeile nennt den Grund, Verkaufen ist aus
					var works = u.Stations.Values.First(x => x.TypeKey == "steelworks" && !x.IsBuildSite);
					var steelG = u.FindGood("steel")!.Id;
					pl.Cargo[steelG] = 5f;
					_m.HudRef.DockCtx.Station = works;
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
					_m.HudRef.DockTrade.TestRebuild();   // der Bereich ist schon offen: Zeilen der neuen Station laden
					var note = _m.HudRef.DockTrade.TestSellNote(steelG);
					Check(note == "Stellt die Ware selbst her" && !_m.HudRef.DockTrade.TestSellEnabled(steelG), $"Stahl am Stahlwerk: Grund sichtbar ({note}), Verkaufen deaktiviert");
					var refused = _m.HudRef.DockTrade.TestExecute($"sell:{steelG.Value}", 5f);
					Check(!refused.Success && refused.Message == "Stellt die Ware selbst her" && pl.Cargo.GetValueOrDefault(steelG) == 5f, "Verkauf abgelehnt mit demselben Grund, Fracht unverändert");
					var bestSteel = SpaceSim.Core.Trading.MarketInfo.BestBuyerForGood(u, steelG);
					Check(bestSteel is null || SpaceSim.Core.Trading.MarketInfo.Acceptance(u, bestSteel, steelG).Accepts, "Beste bekannte Verkaufsstelle für Stahl nimmt Stahl an");
					pl.Cargo.Remove(steelG);
					// Phase 17: Bruchteil-Regel — ein Erzrest unter einer Einheit, den das Stahlwerk ganz nimmt, ist verkäuflich (Taste aktiv ⇔ Verkauf gelingt);
					// nimmt das Werk weniger als eine Einheit an, ist Verkaufen (und „Alles verkaufen“) aus und der Verkauf wird mit demselben Grund abgelehnt
					var ironOre = u.FindGood("iron_ore")!.Id;
					var oreEntry = works.Stock(ironOre);
					float oreKeep = oreEntry.Amount, worksCredits = works.Credits;
					works.Credits = 100000f;
					pl.Cargo[ironOre] = 0.4f;
					_m.HudRef.DockTrade.TestRebuild();
					bool fracOn = _m.HudRef.DockTrade.TestSellEnabled(ironOre) && _m.HudRef.DockTrade.TestSellAllEnabled;
					var frac = _m.HudRef.DockTrade.TestExecute($"sell:{ironOre.Value}", 1f);
					Check(fracOn && frac.Success && pl.Cargo.GetValueOrDefault(ironOre) == 0f, "Erzrest 0,4: Verkaufen aktiv und der Rest wird ganz verkauft (Phase 17)");
					oreEntry.Amount += works.AcceptableUnits(ironOre, u.Rules) - 0.5f;   // Werk nimmt nur noch 0,5 an
					pl.Cargo[ironOre] = 3f;
					_m.HudRef.DockTrade.TestRebuild();
					bool fracOff = !_m.HudRef.DockTrade.TestSellEnabled(ironOre) && !_m.HudRef.DockTrade.TestSellAllEnabled;
					var frRefused = _m.HudRef.DockTrade.TestExecute($"sell:{ironOre.Value}", 3f);
					var frAll = _m.HudRef.DockTrade.TestExecute("sell_all", 0f);
					Check(fracOff && !frRefused.Success && !frAll.Success && pl.Cargo.GetValueOrDefault(ironOre) == 3f && frRefused.Message == _m.HudRef.DockTrade.TestSellNote(ironOre),
						$"Annahme 0,5 < 1: Verkaufen und „Alles verkaufen“ aus, beide abgelehnt, Fracht unverändert ({frRefused.Message})");
					// Phase 18: Resttoleranz — ein Rest 0,4 an einem Werk, das nur 0,2 annimmt, geht ganz weg (Überhang unter einer Einheit)
					oreEntry.Amount = oreKeep;
					oreEntry.Amount += works.AcceptableUnits(ironOre, u.Rules) - 0.2f;
					pl.Cargo[ironOre] = 0.4f;
					_m.HudRef.DockTrade.TestRebuild();
					bool restOn = _m.HudRef.DockTrade.TestSellEnabled(ironOre);
					var rest = _m.HudRef.DockTrade.TestExecute($"sell:{ironOre.Value}", 1f);
					Check(restOn && rest.Success && pl.Cargo.GetValueOrDefault(ironOre) == 0f, "Erzrest 0,4 bei Annahme 0,2: Verkaufen aktiv, Rest ganz verkauft (Phase 18, Resttoleranz)");
					// Phase 18: Abwerfen — Stahl nimmt das Stahlwerk nicht (eigenes Produkt): „Abwerfen“ neben dem Grund, erst der zweite Tipp wirft ab
					pl.Cargo[steelG] = 2.5f;
					_m.HudRef.DockTrade.TestRebuild();
					Check(_m.HudRef.DockTrade.TestDropShown(steelG), "Stahl am Stahlwerk: Taste „Abwerfen“ neben dem Grund");
					Check(!_m.HudRef.DockTrade.TestDropShown(DemoFood(u)) || pl.Cargo.ContainsKey(DemoFood(u)), "Abwerfen nur für Ware an Bord");
					bool firstTap = _m.HudRef.DockTrade.TestTapDrop(steelG, 9999f);
					Check(!firstTap && pl.Cargo.GetValueOrDefault(steelG) == 2.5f, "Erster Tipp auf „Abwerfen“ macht nur scharf, Fracht unverändert");
					float walletDrop = u.PlayerWallet.Credits;
					bool secondTap = _m.HudRef.DockTrade.TestTapDrop(steelG, 9999f);
					Check(secondTap && !pl.Cargo.ContainsKey(steelG) && u.PlayerWallet.Credits == walletDrop && _m.HudRef.DockTrade.TestMessage.Contains("abgeworfen"),
						$"Zweiter Tipp wirft den Stahl ab, ohne Erlös ({_m.HudRef.DockTrade.TestMessage})");
					pl.Cargo.Remove(ironOre);
					oreEntry.Amount = oreKeep; works.Credits = worksCredits;
					_m.HudRef.DockCtx.Station = st;
					_m.HudRef.DockTrade.TestRebuild();
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
				{ // Phase 7: Upgrade im Ausrüster (Dienst „upgrade“, nur Offers/Execute): Triebwerk Mk1 → Mk2, Material und Credits verbraucht
					var st = u.Stations[_m.TestDockedStation!.Value];
					var pl = _m.Session.Player; _m.Session.U.PlayerWallet.Credits = 100000f;
					foreach (var k in new[] { "electronics", "titanium" }) st.Stock(u.FindGood(k)!.Id).Amount = System.Math.Max(st.Stock(u.FindGood(k)!.Id).Amount, 5f);
					int engineSlot = pl.Slots.FindIndex(x => x.Type == "engine");
					var saved = u.Rules.UpgradeChances;
					u.Rules.UpgradeChances = new List<float> { 1f };   // sicherer Erfolg für den Test (der Wurf selbst ist in den Core-Tests geprüft)
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Outfitter);
					_m.HudRef.DockOutfitter.TestSelectSlot(engineSlot);
					Check(_m.HudRef.DockOutfitter.TestUpgradeRows > 0, $"Ausrüster zeigt Upgrade-Zeilen aus dem Dienst „upgrade“ ({_m.HudRef.DockOutfitter.TestUpgradeRows})");
					float speed = pl.Stats.Speed, cr = _m.Session.U.PlayerWallet.Credits;
					float elec = st.Market[u.FindGood("electronics")!.Id].Amount;
					var up = _m.HudRef.DockOutfitter.ExecuteUpgrade($"module:{engineSlot}");
					Check(up.Success && up.Units == 1f && pl.Fit[engineSlot] == "chem_engine_s_mk2" && pl.Stats.Speed > speed,
						$"Upgrade über die Oberfläche: Triebwerk Mk2, Tempo {speed:0.0} → {pl.Stats.Speed:0.0}");
					Check(_m.Session.U.PlayerWallet.Credits < cr && st.Market[u.FindGood("electronics")!.Id].Amount == elec - 1f, "Upgrade kostet Credits und Material aus dem Stationslager");
					Check(!_m.HudRef.DockOutfitter.ExecuteUpgrade("module:99").Success, "Unbekanntes Upgrade-Angebot wird abgelehnt");
					u.Rules.UpgradeChances = saved;
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
					// Phase 6: die Werft verkauft alle Klassen, auch das erste Multirole-Modell (Tinker) und die schweren Klassen
					var yardOffers = _m.Session.Services.ForStation(u, st).First(s => s.Key == "shipdealer").Offers(u, st, old).Select(o => o.Key).ToList();
					Check(yardOffers.Contains("buy:tinker") && yardOffers.Contains("buy:behemoth") && yardOffers.Contains("buy:rockbreaker"),
						$"Werft bietet Tinker, Behemoth und Rockbreaker an ({yardOffers.Count} Modelle)");
					Check(_m.HudRef.DockShipyard.TestRowCount == yardOffers.Count, "Werft zeigt jedes Angebot als Zeile");
					var buy = _m.HudRef.DockShipyard.Execute("buy:prospector");
					var now = _m.Session.Player;
					Check(buy.Success && now.Id != old.Id && now.ModelKey == "prospector", "Schiff über die Werft-Oberfläche gekauft, neues Schiff ist aktiv");
					Check(System.Enum.IsDefined(now.Quality) && (now.Quality == SpaceSim.Core.Model.ShipQuality.Standard || buy.Message.Contains(SpaceSim.Core.Model.ShipQualityNames.Name(now.Quality))),
						$"Gekauftes Schiff hat eine Güteklasse ({SpaceSim.Core.Model.ShipQualityNames.Name(now.Quality)}), die Meldung nennt sie");
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
					Check(SpaceSim.Core.Flight.PlayerFuel.ThrottleCap(_m.Session.U, pl) < 1f, "Leerer Tank deckelt das Tempo (Notantrieb)");
					float cr = _m.Session.U.PlayerWallet.Credits;
					_m.TestCommand("emergency");
					Check(pl.TotalFuel > 0f && _m.Session.U.PlayerWallet.Credits < cr, $"Notruf liefert Treibstoff gegen Credits (Tank {pl.TotalFuel:0.0})");
					Check(SpaceSim.Core.Flight.PlayerFuel.ThrottleCap(_m.Session.U, pl) >= 1f, "Nach dem Notruf wieder volles Tempo");
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
					// Phase 9: Fraktionen und Meine Fraktion zeigen die echten Akteure (Fraktionen, Sammelgruppe; der Spieler ist Einzelakteur)
					var rows = _m.HudRef.CcFactions.TestRows;
					Check(u.Factions.Keys.All(f => rows.Any(r => r.Id == f)) && rows.Any(r => r.Id == u.FreeActor) && rows.All(r => r.Id != u.PlayerActor),
						$"Fraktionen-Liste aus den Akteuren: {rows.Count} Zeilen (Fraktionen, Einzelakteure, Freie; ohne Spieler)");
					_m.TestOpenCcArea("factions");
					_m.TestCcSelectFaction(new FactionId(2));
					_m.HudRef.Screens.Current?.Refresh();
					Check(_m.HudRef.CcFactions.Selected == new ActorId(2) && _m.TestScreenOpen("cc.factions"), "Fraktionsdetail der Gilde öffnet ohne Ausnahme");
					var plShip = u.Ships[u.PlayerShip];
					Check(plShip.Owner == u.PlayerActor && ReferenceEquals(u.WalletOf(plShip), u.Player.Treasury) && u.Player.Kind == ActorKind.Player,
						"Spielerschiff gehört dem Spielerakteur und bucht auf seine Kasse (Phase 9)");
					Check(SpaceSim.Core.Simulation.Relations.Effective(u, new ActorId(2), u.PlayerActor) is >= -1f and <= 1f, "Beziehung der Gilde zum Spieler ist auswertbar");
					// Universum: Sektorauswahl liefert Route/Sprungzahl passend zu Universe.FindRoute
					var target = u.Sectors.Keys.First(s => s != u.PlayerSector);
					_m.TestOpenCcArea("universe");
					_m.TestCcSelectSector(target);
					// Phase 10: Route nach dem Mobilitätsprofil des Spielerschiffs
					int expect = u.FindRoute(u.PlayerSector, target, SpaceSim.Core.Simulation.GateAccess.ProfileOf(u, u.Ships[u.PlayerShip]))?.Count ?? -1;
					Check(_m.TestCcRouteHops == expect, $"Sektorauswahl: Sprungzahl {_m.TestCcRouteHops} passt zu FindRoute mit Spielerprofil ({expect})");
					_m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Security;
					_m.HudRef.Screens.Current?.Refresh();
					Check(_m.TestScreenOpen("cc.universe"), "Universumskarte: Ebene Sicherheit (echte Sperrstufen) zeichnet ohne Ausnahme");
					_m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Faction;
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
			case 27: // Phase 8: Baustelle im Spielersektor anlegen, andocken, Material über die Handel-Oberfläche liefern
			{
				try
				{
					if (_m.Docked) _m.TestUndock();
					var uni = _m.Session.U;
					var sec = uni.Sectors[uni.PlayerSector];
					var spot = SpaceSim.Core.Simulation.StationLifecycleSystem.FindFreeSpot(uni, sec);
					Check(spot != null, "Bauplatz für eine Test-Baustelle gefunden");
					var site = SpaceSim.Core.Simulation.ConstructionRules.StartSite(uni, uni.StationTypes["farm"], sec.Id, spot!.Value, sec.Owner, 3500f, null, null);
					_siteId = site.Id;
					_m.TestDockAt(site.Id);
					Check(_m.Docked && _m.TestDockedStation == site.Id, "An der Baustelle angedockt");
					var areas = _m.TestDockAreas();
					Check(areas.Any(a => a.Id == Ui.Screens.DockAreas.Trade && a.Available), "Baustelle bietet den Handel (Material liefern)");
				}
				catch (System.Exception ex) { Fail("Baustelle: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 28:
				if (_frame - _stageStart > 10)   // ein Simulationstick setzt das Gebot der Baustelle
				{
					var uni = _m.Session.U;
					var site = uni.Stations[_siteId];
					var pl = _m.Session.Player;
					var steel = uni.FindGood("steel")!.Id;
					pl.Cargo[steel] = 5f;
					float before = site.Market[steel].Amount, cr = uni.PlayerWallet.Credits;
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
					var r = _m.HudRef.DockTrade.TestExecute($"sell:{steel.Value}", 5f);
					Check(r.Success && System.Math.Abs(site.Market[steel].Amount - before - 5f) < 0.01f, "Stahl über die Handel-Oberfläche an die Baustelle geliefert");
					Check(uni.PlayerWallet.Credits > cr, "Spieler bekommt den Preis der Baustelle (freier Handel)");
					Check(!_m.HudRef.DockTrade.TestExecute($"buy:{steel.Value}", 1f).Success, "Laufende Baustelle verkauft kein Material zurück");
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
					Check(_m.TestScreenOpen("dock.info"), "Station-Info an der Baustelle geöffnet");
					var status = SpaceSim.Core.Simulation.ConstructionInfo.Of(uni, site);
					Check(status != null && status.Progress > 0f, $"Baustellen-Status aus dem Core ({status?.State}, {100f * (status?.Progress ?? 0f):0} %)");
					_m.TestUndock();
					_m.TestOpenCcArea("sector");
					_m.HudRef.Screens.Current?.Refresh();
					Check(_m.TestScreenOpen("cc.sector"), "Sektoransicht zeigt die Baustelle ohne Ausnahme");
					_m.TestCloseCommand();
					Next();
				}
				break;
			case 29: // Phase 8: Spalter — Spaltlaser einbauen, Schacht erscheint, Spaltschuss zerlegt den Asteroiden
			{
				if (_frame - _stageStart < 180) break;   // Abdock-Animation abwarten (sie setzt die Schiffsposition)
				var uni = _m.Session.U;
				var pl = _m.Session.Player;
				int tool = pl.Slots.FindIndex(s => s.Type == "tool");
				_savedTool = tool >= 0 ? pl.Fit[tool] : null;
				if (tool >= 0) { pl.Fit[tool] = "split_laser_s"; pl.RecomputeStats(uni); }
				var rock = _m.TestAimAtBiggestRock();
				_splitRock = rock?.Id ?? 0;
				Check(pl.Stats.SplitPower > 0f && rock != null, "Spaltlaser eingebaut und Ziel gewählt");
				Next(); break;
			}
			case 30:
				if (_frame - _stageStart > 3)
				{
					var uni = _m.Session.U;
					var pl = _m.Session.Player;
					int idx = _m.TestBayKeys.ToList().IndexOf("split");
					Check(idx >= 0, "Flug-HUD zeigt den Schacht „Spalter“ (WeaponBays.For)");
					int before = uni.Sectors[uni.PlayerSector].Asteroids.Count;
					if (idx >= 0) _m.TestPressBay(idx);
					Check(!uni.Sectors[uni.PlayerSector].Asteroids.Any(a => a.Id == _splitRock) && uni.Sectors[uni.PlayerSector].Asteroids.Count > before,
						$"Spaltschuss zerlegt den Asteroiden in Fragmente ({_m.TestLastSplitIssue})");
					int tool = pl.Slots.FindIndex(s => s.Type == "tool");
					if (tool >= 0) { pl.Fit[tool] = _savedTool; pl.RecomputeStats(uni); }
					Next();
				}
				break;
			case 31: // Phase 8: Lagerhaus in Gamma — einlagern und abholen über den Bereich „Lager“
				_m.TestEnterSector(new SectorId(3));
				Next(); break;
			case 32:
				if (_frame - _stageStart > 3)
				{
					var uni = _m.Session.U;
					var wh = uni.Stations.Values.First(s => s.TypeKey == "warehouse");
					_m.TestDockAt(wh.Id);
					Check(_m.Docked, "Am Lagerhaus angedockt");
					Check(_m.TestDockAreas().Any(a => a.Id == Ui.Screens.DockAreas.Storage && a.Available), "Andock-Menü zeigt den Bereich „Lager“");
					// Phase 12: Lager verdrängt das Taxi nicht mehr — neun Kacheln im rollbaren Raster
					Check(_m.TestDockAreas().Any(a => a.Id == Ui.Screens.DockAreas.Taxi && a.Available) && _m.TestDockAreas().Count == 9 && _m.HudRef.DockMenu.TestTiles == 9,
						$"Lagerhaus: Lager und Taxi nebeneinander, {_m.HudRef.DockMenu.TestTiles} Kacheln im Andock-Menü");
					var pl = _m.Session.Player;
					var ice = uni.FindGood("ice")!.Id;
					pl.Cargo[ice] = 7f;
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Storage);
					Check(_m.TestScreenOpen("dock.storage"), "Lager-Bereich geöffnet");
					Check(_m.HudRef.DockStorage.TestRowCount >= 1, "Lager zeigt Zeilen aus IStationService.Offers");
					var st = _m.HudRef.DockStorage.Execute($"store:{ice.Value}");
					Check(st.Success && wh.PlayerStorage.GetValueOrDefault(ice) == 7f && pl.Cargo.GetValueOrDefault(ice) == 0f, "Einlagern über Dienst+Oberfläche");
					var fe = _m.HudRef.DockStorage.Execute($"fetch:{ice.Value}");
					Check(fe.Success && pl.Cargo.GetValueOrDefault(ice) == 7f && !wh.PlayerStorage.ContainsKey(ice), "Abholen über Dienst+Oberfläche");
					pl.Cargo.Remove(ice);
					_m.TestUndock();
					Next();
				}
				break;
			case 33: // Phase 10: Gate-Zugang — Sperre gegen die Union in Gamma, Sprung wird mit Grund abgewiesen, Karte/Station-Info zeigen es
				_m.TestEnterSector(new SectorId(2));
				Next(); break;
			case 34:
				if (_frame - _stageStart > 3)
				{
					try
					{
						var uni = _m.Session.U;
						var gamma = uni.Sectors[new SectorId(3)];
						var guild = new FactionId(2);
						var union = new FactionId(1);
						var pl = _m.Session.Player;
						foreach (var kv in pl.Cargo.ToList()) pl.Cargo.Remove(kv.Key);
						SpaceSim.Core.Simulation.Relations.Set(uni, guild, union, -0.9f);
						int lvl = SpaceSim.Core.Simulation.FactionPolicySystem.SetLevel(uni, gamma, 3);
						Check(lvl == 3 && gamma.Access.EntryLevel == 3, "Gamma (Kontrolle der Gilde) sperrt auf Stufe 3 gegen die Union");
						var gate = uni.Gates.Values.First(g => g.Sector == uni.PlayerSector && uni.Gates[g.Target].Sector == gamma.Id);
						Check(gate.State == GateState.Open && uni.Gates[gate.Target].State == GateState.Restricted, "Gate nach Gamma ist auf der Gamma-Seite eingeschränkt");
						bool jumped = _m.TestJumpThrough(gate.Id);
						Check(!jumped && uni.PlayerSector.Value == 2, "Sprung nach Gamma wird abgewiesen, das Schiff bleibt in Beta");
						Check(_m.LastJumpMessage.StartsWith("Sprung verweigert") && _m.LastJumpMessage.Contains("Verbündete"), $"Meldung nennt den Grund: „{_m.LastJumpMessage}“");
						// Universumskarte: Gamma für dich nicht erreichbar (Profil), Ebene Sicherheit ohne Ausnahme
						_m.TestOpenCommand();
						_m.TestOpenCcArea("universe");
						_m.TestCcSelectSector(gamma.Id);
						_m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Security;
						_m.HudRef.Screens.Current?.Refresh();
						Check(_m.TestCcRouteHops == -1, "Universumskarte: Route nach Gamma für dich gesperrt (Mobilitätsprofil)");
						_m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Faction;
						_m.TestCloseCommand();
						// Station-Info an einer Station in Gamma zeigt Sperrstufe und Zoll echt (Kontext auf eine Gamma-Station)
						var beta = uni.Stations.Values.First(x => x.Sector.Value == 2 && x.Market.Count > 0);
						_m.TestDockAt(beta.Id);
						Check(_m.Docked, "In Beta angedockt");
						_m.HudRef.DockCtx.Station = uni.Stations.Values.First(x => x.Sector == gamma.Id && x.Market.Count > 0);
						_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
						Check(_m.TestScreenOpen("dock.info"), "Station-Info geöffnet");
						Check(_m.HudRef.DockInfo.TestLock.StartsWith("Stufe 3") && _m.HudRef.DockInfo.TestCustoms.StartsWith("gesperrt"),
							$"Station-Info zeigt Sperrstufe „{_m.HudRef.DockInfo.TestLock}“ und Zoll „{_m.HudRef.DockInfo.TestCustoms}“ echt");
						_m.HudRef.DockCtx.Station = beta;
						_m.TestUndock();
						// Zoll: neutral statt feindlich auf Stufe 1 — Sprung erlaubt, Zoll aus der Spielerkasse
						SpaceSim.Core.Simulation.Relations.Set(uni, guild, union, 0f);
						SpaceSim.Core.Simulation.FactionPolicySystem.SetLevel(uni, gamma, 1);
						float before = uni.PlayerWallet.Credits;
						bool jumped2 = _m.TestJumpThrough(gate.Id);
						Check(jumped2 && uni.PlayerSector == gamma.Id, "Sprung nach Gamma auf Stufe 1 erlaubt");
						Check(System.Math.Abs(before - uni.PlayerWallet.Credits - uni.Rules.GateTollPerLevel) < 0.01f && _m.LastJumpMessage.Contains("Zoll"),
							$"Zoll {uni.Rules.GateTollPerLevel:0} Cr aus der Spielerkasse an die Gilde („{_m.LastJumpMessage}“)");
						SpaceSim.Core.Simulation.FactionPolicySystem.SetLevel(uni, gamma, 0);
						SpaceSim.Core.Simulation.Relations.Set(uni, guild, union, 0.25f);
						Check(gamma.Access.IsOpen && !uni.AccessRestricted, "Sperre aufgehoben, alle Gates wieder offen");
					}
					catch (System.Exception ex) { Fail("Gate-Zugang warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
					Next();
				}
				break;
			case 35: // Phase 11: Lagekarte, Fahndung, Behörde (Gericht, Kopfgeldbüro), Überfall mit Vorwarnung
				_m.TestEnterSector(new SectorId(3));
				SpaceSim.Core.Simulation.Wanted.Report(u, u.PlayerActor, new FactionId(2), "toll_evasion");
				Next(); break;
			case 36:
				if (_frame - _stageStart > 3)
				{
					try
					{
						var uni = _m.Session.U;
						var guild = new FactionId(2);
						var pl = _m.Session.Player;
						Check(_m.HudRef.Flight.TestSecurity.StartsWith("Sicherheit"), $"Flug-HUD zeigt die Lage echt („{_m.HudRef.Flight.TestSecurity}“)");
						Check(_m.HudRef.Flight.TestWanted.Contains("auffällig") && _m.HudRef.Flight.TestWanted.Contains("Freie Händler"), $"Flug-HUD zeigt die Fahndung („{_m.HudRef.Flight.TestWanted}“)");
						_m.TestOpenCommand();
						_m.TestOpenCcArea("myfaction");
						Check(_m.HudRef.CcMyFaction.TestWanted.Contains("Freie Händler") && _m.HudRef.CcMyFaction.TestWanted.Contains("auffällig"), $"Meine Fraktion zeigt die Fahndung je Fraktion („{_m.HudRef.CcMyFaction.TestWanted}“)");
						_m.TestOpenCcArea("universe");
						_m.TestCcSelectSector(new SectorId(4));
						_m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Security;
						_m.HudRef.Screens.Current?.Refresh();
						Check(_m.TestScreenOpen("cc.universe"), "Universumskarte: Ebene Sicherheit aus der Lagekarte zeichnet ohne Ausnahme");
						_m.HudRef.CcUniverse.CurrentLayer = Ui.Screens.UniverseScreen.Layer.Faction;
						_m.TestCloseCommand();
						// Behörde am Gildenhaus Gamma: Strafe bezahlen (Fahndung gelöscht), Kopfgeld abholen
						var hq = uni.Stations.Values.First(s => s.Sector.Value == 3 && s.TypeKey == "headquarters");
						_m.TestDockAt(hq.Id);
						Check(_m.Docked, "Am Gildenhaus angedockt");
						Check(_m.TestDockAreas().Any(a => a.Id == Ui.Screens.DockAreas.Authority && a.Available), "Andock-Menü zeigt den Bereich „Behörde“ (Dienste court/bounty)");
						_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
						Check(_m.HudRef.DockInfo.TestSecurity.StartsWith("Sicherheit"), $"Station-Info zeigt die Lage echt („{_m.HudRef.DockInfo.TestSecurity}“)");
						_m.TestOpenDockArea(Ui.Screens.DockAreas.Authority);
						Check(_m.TestScreenOpen("dock.authority"), "Behörde geöffnet");
						Check(_m.HudRef.DockAuthority.TestRowCount >= 1, "Behörde zeigt die offene Strafe aus IStationService.Offers");
						float fine = SpaceSim.Core.Simulation.Wanted.FineOf(uni, uni.Player.Wanted[guild]);
						uni.PlayerWallet.Credits += fine + 10f;
						float before = uni.PlayerWallet.Credits;
						var paid = _m.HudRef.DockAuthority.Execute("court", $"pay:{guild.Value}");
						Check(paid.Success && !uni.Player.Wanted.ContainsKey(guild) && System.Math.Abs(before - fine - uni.PlayerWallet.Credits) < 0.5f,
							$"Strafe {fine:0} Cr bezahlt, Fahndung gelöscht („{paid.Message}“)");
						Check(!_m.HudRef.DockAuthority.Execute("court", "pay:x").Success, "Unbekannter Gerichts-Schlüssel wird abgewiesen");
						uni.Player.BountyClaims[guild] = 250f;
						uni.Actors[guild].Treasury.Credits += 1000f;
						before = uni.PlayerWallet.Credits;
						var got = _m.HudRef.DockAuthority.Execute("bounty", $"claim:{guild.Value}");
						Check(got.Success && System.Math.Abs(uni.PlayerWallet.Credits - before - 250f) < 0.5f && !uni.Player.BountyClaims.ContainsKey(guild), $"Kopfgeld abgeholt („{got.Message}“)");
						_m.TestUndock();
					}
					catch (System.Exception ex) { Fail("Behörde/Fahndung warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
					Next();
				}
				break;
			case 37: // Überfall auf den Spieler: Vorwarnung, dann andocken = entkommen (nichts verloren)
			{
				if (_frame - _stageStart < 90) break;   // Abdock-Bewegung abwarten (sie setzt die Position sonst zurück)
				var uni = _m.Session.U;
				var pl = _m.Session.Player;
				var sector = uni.Sectors[uni.PlayerSector];
				var gatePos = uni.Gates[sector.Gates[0]].Position;
				Vec2 spot = gatePos;
				for (int k = 0; k < 24; k++)
				{
					var c = gatePos + Vec2.FromAngle(k * 0.5236f) * (25f + 10f * (k / 12));
					if (sector.Stations.All(id => Vec2.Distance(uni.Stations[id].Position, c) > 60f)) { spot = c; break; }
				}
				_m.TestTeleportTo(spot);
				foreach (var kv in pl.Cargo.ToList()) pl.Cargo.Remove(kv.Key);
				pl.Cargo[uni.FindGood("food")!.Id] = 40f;
				var raider = uni.Ships.Values.Where(SpaceSim.Core.Simulation.ThreatMap.IsRaider).OrderBy(s => s.Id.Value).First();
				if (raider.Transit != null) uni.FinishTransit(raider);
				if (raider.Sector != sector.Id) uni.MoveShip(raider, sector.Id);
				raider.Position = spot + new Vec2(12f, 0f);
				raider.Order = new PatrolOrder(sector.Id);
				raider.NextThinkTick = uni.Tick + 100_000;
				uni.PendingEncounters.Clear();
				uni.Rules.PlayerRaids = true;
				uni.Rules.RaidRatePerTick = 1f;
				uni.Rules.PlayerRaidWeight = 1e6f;
				uni.Rules.InterceptRatePerTick = 0f;
				uni.Rules.PlayerWarningTicks = 12;
				Next(); break;
			}
			case 38:
				if (_m.LastSecurityMessage.StartsWith("Piraten"))
				{
					var uni = _m.Session.U;
					Check(uni.PendingEncounters.Any(e => e.Target == uni.PlayerShip), $"Vorwarnung vor dem Überfall angezeigt („{_m.LastSecurityMessage}“)");
					var near = uni.Stations[uni.Sectors[uni.PlayerSector].Stations.OrderBy(id => id.Value).First()];
					_m.TestDockAt(near.Id);
					Check(_m.Docked, "Angedockt, um dem Überfall zu entgehen");
					Next();
				}
				else if (_frame - _stageStart > 1500) Fail("Keine Überfall-Vorwarnung");
				break;
			case 39:
				if (_m.LastSecurityMessage.Contains("entkommen"))
				{
					var uni = _m.Session.U;
					var pl = _m.Session.Player;
					Check(pl.Cargo.GetValueOrDefault(uni.FindGood("food")!.Id) == 40f, "Angedockt entkommen: keine Fracht verloren");
					uni.Rules.PlayerRaids = false;
					uni.Rules.RaidRatePerTick = new EconomyRules().RaidRatePerTick;
					_m.TestUndock();
					Next();
				}
				else if (_frame - _stageStart > 3000) Fail($"Überfall nicht aufgelöst („{_m.LastSecurityMessage}“)");
				break;
			case 40: // Phase 12: Befehl an das eigene Zweitschiff (an der Werft gekaufter Prospector) über die Flotte der Kommandozentrale
			{
				var uni = _m.Session.U;
				var yardSt = uni.Stations.Values.First(s => s.TypeKey == "shipyard");
				var tradeSt = uni.Stations.Values.First(s => s.TypeKey == "trade_station");
				var second = SpaceSim.Core.Simulation.PlayerFleet.Owned(uni).FirstOrDefault(s => s.Id != uni.PlayerShip && s.ModelKey == "prospector");
				if (second is null) { Fail("Kein Zweitschiff aus dem Werftkauf"); break; }
				_secondShip = second.Id;
				_m.TestFleetSelect(second.Id);
				Check(_m.TestScreenOpen("cc.fleet"), "Flotte der Kommandozentrale geöffnet");
				Check(_m.HudRef.CcFleet.TestEnabledCommands >= 5, $"Befehlsraster: {_m.HudRef.CcFleet.TestEnabledCommands} Befehle für das Zweitschiff freigegeben");
				bool ok = _m.TestFleetGive(second.Id, new GoToCommand(default, default, tradeSt.Id));
				Check(ok && second.Command is GoToCommand, $"Befehl „Fliegen“ zur Handelsstation gegeben ({_m.HudRef.CcFleet.TestMessage})");
				Check(!_m.TestFleetGive(second.Id, new PatrolCommand(new[] { second.Sector })) && second.Command is GoToCommand, "Patrouille ohne Waffen wird abgelehnt, der Befehl bleibt");
				_m.Session.RunTicks(600);
				Check(second.Command is null && second.DockedAt == tradeSt.Id && second.Sector == tradeSt.Sector, $"Zweitschiff ist nach dem Befehl an der Handelsstation angedockt ({second.DockedAt.Value})");
				_m.TestCloseCommand();
				Next(); break;
			}
			case 41: // Phase 12: Taxi von der Werft zur Handelsstation, wo das Zweitschiff wartet; das alte Schiff bleibt an der Werft
			{
				var uni = _m.Session.U;
				var yardSt = uni.Stations.Values.First(s => s.TypeKey == "shipyard");
				var tradeSt = uni.Stations.Values.First(s => s.TypeKey == "trade_station");
				_firstShip = uni.PlayerShip;
				_m.TestDockAt(yardSt.Id);
				Check(_m.Docked, "An der Werft angedockt (Taxistand)");
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Taxi);
				Check(_m.TestScreenOpen("dock.taxi") && _m.HudRef.DockTaxi.TestRows >= 1, $"Taxi-Bereich zeigt Ziele aus den Angeboten ({_m.HudRef.DockTaxi.TestRows})");
				float cr = uni.PlayerWallet.Credits;
				long tick = uni.Tick;
				var r = _m.HudRef.DockTaxi.Book($"taxi:{tradeSt.Id.Value}");
				Check(r.Success, $"Taxi gebucht: {r.Message}");
				Check(uni.PlayerShip == _secondShip && _m.TestDockedStation == tradeSt.Id, "Am Ziel im wartenden eigenen Schiff, angedockt an der Handelsstation");
				Check(uni.Ships[_firstShip].DockedAt == yardSt.Id, "Das alte Schiff bleibt an der Werft angedockt");
				Check(uni.PlayerWallet.Credits < cr && uni.Tick >= tick + _m.LastTaxiTicks && _m.LastTaxiTicks > 0, $"Fahrpreis bezahlt, Reisezeit {_m.LastTaxiTicks} Ticks im Zeitraffer vergangen");
				Next(); break;
			}
			case 42: // Phase 12: Hangar an der Handelsstation — fremde Schiffe, Leihschiff mieten und zurückgeben
			{
				var uni = _m.Session.U;
				_m.TestOpenDockArea(Ui.Screens.DockAreas.Hangar);
				Check(_m.TestScreenOpen("dock.hangar"), "Hangar-Bereich geöffnet");
				float cr = uni.PlayerWallet.Credits;
				var rent = _m.HudRef.DockHangar.Execute(SpaceSim.Core.Services.RentalService.ServiceKey, "rent:1");
				var rented = _m.Session.Player;
				Check(rent.Success && rented.IsRental && rented.ModelKey == uni.Rules.RentalModel, $"Leihschiff gemietet ({rent.Message})");
				Check(System.Math.Abs(uni.PlayerWallet.Credits - (cr - uni.Rules.RentalFee - uni.Rules.RentalDeposit)) < 0.05f, "Gebühr und Pfand aus der Kasse");
				Check(_m.HudRef.DockHangar.TestParkedRows >= 1, "Hangar zeigt das eigene Zweitschiff als geparkt");
				var back = _m.HudRef.DockHangar.Execute(SpaceSim.Core.Services.RentalService.ServiceKey, $"return:{rented.Id.Value}");
				Check(back.Success && !uni.Ships.ContainsKey(rented.Id) && uni.PlayerShip == _secondShip, $"Leihschiff zurückgegeben, wieder im eigenen Schiff ({back.Message})");
				Check(uni.PlayerWallet.Credits > cr - uni.Rules.RentalFee - uni.Rules.RentalDeposit, "Pfand (abzüglich Miete) erstattet");
				Check(!_m.HudRef.DockHangar.Execute(SpaceSim.Core.Services.HangarService.ServiceKey, "switch:x").Success, "Hangar lehnt einen ungültigen Schlüssel ab");
				Next(); break;
			}
			case 43: // Phase 12: Fernwechsel zurück ins erste Schiff (liegt an der Werft) und Automatikauftrag „Handel“ für das Zweitschiff
			{
				var uni = _m.Session.U;
				var yardSt = uni.Stations.Values.First(s => s.TypeKey == "shipyard");
				_m.TestSwitchRemote(_firstShip);
				Check(uni.PlayerShip == _firstShip && uni.PlayerSector == yardSt.Sector && _m.TestDockedStation == yardSt.Id, "Fernwechsel: im ersten Schiff, angedockt an der Werft");
				var second = uni.Ships[_secondShip];
				Check(second.DockedAt.Value != 0 && second.Command is null, "Das Zweitschiff bleibt angedockt und hält ohne Befehl die Position");
				_trades = 0;
				uni.Events.Raised += CountTrades;
				bool ok = _m.TestFleetGive(_secondShip, new TradeCommand(new AutoLimits(second.Sector, 2, 1.5f, 500f)));
				Check(ok && second.Role == ShipRole.Trader, "Automatikauftrag „Handel“ mit Grenzen gegeben");
				float cr = uni.PlayerWallet.Credits;
				_m.Session.RunTicks(3000);
				uni.Events.Raised -= CountTrades;
				Check(_trades > 0 && second.Command is TradeCommand, $"Zweitschiff handelt automatisch ({_trades} Handelsfahrten) und bucht auf die Spielerkasse ({uni.PlayerWallet.Credits - cr:+0;-0} Cr)");
				Check(second.Credits == 0f, "Das Flottenschiff selbst hat keine Credits (Geld bei der Person)");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Assets);
				_m.HudRef.CcAssets.TestOpenShipDetail(_secondShip);
				_m.HudRef.CcAssets.Refresh();
				Check(_m.TestScreenOpen("cc.assets"), "Besitz zeigt das Zweitschiff im Detailfenster");
				{ // Phase 18: Abwerfen im Schiffsdetail (aus der Ferne, Core-Regel CargoJettison), zweiter Tipp bestätigt
					var food = DemoFood(uni);
					second.Cargo[food] = 0.3f;
					_m.HudRef.CcAssets.Refresh();
					bool t1 = _m.HudRef.CcAssets.TestTapJettison(_secondShip, food);
					bool t2 = _m.HudRef.CcAssets.TestTapJettison(_secondShip, food);
					Check(!t1 && t2 && !second.Cargo.ContainsKey(food) && _m.HudRef.CcAssets.TestMessage.Contains("abgeworfen"),
						$"Schiffsdetail: Rest 0,3 Nahrung nach dem zweiten Tipp abgeworfen ({_m.HudRef.CcAssets.TestMessage})");
				}
				{ // Phase 19: Frist der Bestätigung abgelaufen → Beschriftung springt von selbst zurück, ein einzelner Tipp wirft nicht ab
					var food = DemoFood(uni);
					second.Cargo[food] = 0.3f;
					_m.HudRef.CcAssets.TestOpenShipDetail(_secondShip);   // Neuaufbau mit der Frachtzeile
					string key = $"drop:{_secondShip.Value}:{food.Value}";
					var btn = SpaceAdventure.Ui.Kit.KitConfirm.Find(_m.HudRef.CcAssets, key);
					var ticker = btn?.GetChildren().OfType<SpaceAdventure.Ui.Kit.KitConfirmTicker>().FirstOrDefault();
					bool armedTap = _m.HudRef.CcAssets.TestTapJettison(_secondShip, food);
					ticker?.Poll();
					string armedText = btn?.Text ?? "";
					var realNow = SpaceAdventure.Ui.Kit.KitConfirm.Now;
					ulong shift = SpaceAdventure.Ui.Kit.KitConfirm.WindowMs + 500;
					SpaceAdventure.Ui.Kit.KitConfirm.Now = () => realNow() + shift;   // Frist verstreicht
					ticker?.Poll();
					string expiredText = btn?.Text ?? "";
					bool single = _m.HudRef.CcAssets.TestTapJettison(_secondShip, food);
					SpaceAdventure.Ui.Kit.KitConfirm.Now = realNow;
					Check(btn is not null && ticker is not null && !armedTap && armedText == "Wirklich?" && expiredText == "Abwerfen" && !single
						&& second.Cargo.GetValueOrDefault(food) == 0.3f,
						$"Bestätigung: nach Ablauf der Frist steht wieder „{expiredText}“ (vorher „{armedText}“), ein einzelner Tipp wirft nicht ab");
					second.Cargo.Remove(food);
					_m.HudRef.CcAssets.TestOpenShipDetail(_secondShip);
				}
				_m.TestCloseCommand();
				_m.TestUndock();
				Next(); break;
			}
			case 44: // Phase 13: Forschung — Forschungsinstitut der Union: Bereich „Forschung“, beauftragen, mitfinanzieren, strenge Schlüssel; Kommandozentrale
			{
				if (_frame - _stageStart < 90) break;   // Abdock-Bewegung abwarten
				try
				{
					var uni = _m.Session.U;
					var union = uni.Factions[new FactionId(1)];
					var lab = uni.Stations.Values.First(st => st.TypeKey == "research_station" && SpaceSim.Core.Simulation.Research.TechOf(uni, st) == union.Id);
					uni.PlayerWallet.Credits += 20_000f;
					_m.TestDockAt(lab.Id);
					Check(_m.Docked, "Am Forschungsinstitut angedockt");
					Check(_m.TestDockAreas().Any(a => a.Id == Ui.Screens.DockAreas.Research && a.Available), "Andock-Menü zeigt den Bereich „Forschung“ (Dienst research)");
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Research);
					Check(_m.TestScreenOpen("dock.research"), "Forschung geöffnet");
					Check(_m.HudRef.DockResearch.TestRowCount == uni.Research.Count && _m.HudRef.DockResearch.TestFundButtons == uni.Rules.ResearchFundAmounts.Count,
						$"Forschung zeigt {_m.HudRef.DockResearch.TestRowCount} Projekte und {_m.HudRef.DockResearch.TestFundButtons} Beträge aus ResearchInfo/Offers");
					string? running = SpaceSim.Core.Simulation.Research.Peek(uni, union.Research)?.Key;
					string target = SpaceSim.Core.Simulation.Research.AvailableProjects(uni, union.Research).Select(p => p.Key).FirstOrDefault(k => k != running) ?? running!;
					float before = uni.PlayerWallet.Credits;
					if (target == running)
						Check(!_m.HudRef.DockResearch.Execute($"commission:{target}").Success, "Das laufende Projekt lässt sich nicht noch einmal beauftragen");
					else
					{
						var c = _m.HudRef.DockResearch.Execute($"commission:{target}");
						Check(c.Success && union.Research.Current == target && System.Math.Abs(before - uni.Rules.ResearchCommissionFee - uni.PlayerWallet.Credits) < 0.5f,
							$"Projekt beauftragt, Gebühr gebucht („{c.Message}“)");
					}
					float pts = union.Research.Progress.GetValueOrDefault(target);
					var f = _m.HudRef.DockResearch.Execute("fund:2000");
					Check(f.Success && union.Research.Progress.GetValueOrDefault(target) > pts, $"Mitfinanziert: +FP („{f.Message}“)");
					Check(!_m.HudRef.DockResearch.Execute("fund:7").Success && !_m.HudRef.DockResearch.Execute("commission:x").Success, "Unbekannte Forschungs-Schlüssel werden abgewiesen");
					_m.TestOpenCommand();
					_m.TestOpenCcArea("factions");
					_m.HudRef.CcFactions.TestSelect(union.Id);
					Check(_m.TestScreenOpen("cc.factions"), "Fraktionen zeigen den Forschungsstand ohne Ausnahme");
					_m.TestOpenCcArea("myfaction");
					Check(_m.TestScreenOpen("cc.myfaction"), "Meine Fraktion zeigt die Forschung der eigenen Fraktion");
					_m.TestCloseCommand();
					_m.TestUndock();
				}
				catch (System.Exception ex) { Fail("Forschung warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 45: // Phase 13: Werft und Ausrüster zeigen nicht Erforschtes; nach der Forschung der Werftfraktion kaufbar
			{
				if (_frame - _stageStart < 90) break;
				try
				{
					var uni = _m.Session.U;
					var yardSt = uni.Stations.Values.First(s => s.TypeKey == "shipyard");
					var guild = uni.Factions[SpaceSim.Core.Simulation.Research.TechOf(uni, yardSt)!.Value];
					_m.TestDockAt(yardSt.Id);
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Shipyard);
					int locked = _m.HudRef.DockShipyard.TestLockedRows;
					Check(locked >= 1 && !guild.Research.IsCompleted("deep_mining"), $"Werft zeigt {locked} nicht erforschte(s) Modell(e) (Excavator) sichtbar, aber gesperrt");
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Outfitter);
					_m.HudRef.DockOutfitter.TestSelectSlot(_m.Session.Player.Slots.FindIndex(sl => sl.Type == "reactor"));
					var mk3 = _m.HudRef.DockOutfitter.TestSlotOffers().FirstOrDefault(o => o.Key.EndsWith(":reactor_mk3"));
					Check(mk3.Key is not null && mk3.Amount == 0f && mk3.Text.Contains("nicht erforscht"), $"Ausrüster zeigt Reaktor Mk3 als „nicht erforscht“ („{mk3.Text}“)");
					foreach (var p in uni.Research.Projects) SpaceSim.Core.Simulation.Research.AddPoints(uni, guild, p, p.Points);
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Shipyard);
					Check(_m.HudRef.DockShipyard.TestLockedRows == 0, "Nach der Forschung der Gilde ist an ihrer Werft nichts mehr gesperrt");
					_m.TestUndock();
				}
				catch (System.Exception ex) { Fail("Forschung an Werft/Ausrüster warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 46: // Phase 14: neues Spiel aus der Standardwelt — Treibstoff und Forschung sind dort aus (bis hier lief der Selbsttest mit beiden an)
			{
				if (_frame - _stageStart < 30) break;
				_m.TestMenu("new");
				Next(); break;
			}
			case 47: // Phase 14: Standardwelt — keine Tankanzeige, kein Notruf, keine Forschung; Planeten und Exportsenke
			{
				if (_frame - _stageStart < 20) break;
				try
				{
					var uni = _m.Session.U;
					uni.Rules.PlayerRaids = false;
					Check(!uni.Rules.FuelActive && !uni.Rules.ResearchActive, "Standardwelt: Treibstoff und Forschung abgeschaltet");
					Check(!uni.Stations.Values.Any(s => s.TypeKey is "fuel_refinery" or "research_station"), "Standardwelt ohne Treibstoffraffinerien und Forschungsstationen");
					Check(!_m.HudRef.Flight.TestFuelVisible, "Flug-HUD zeigt ohne Treibstoff keine Tankanzeige");
					_m.TestOpenCommand();
					Check(!_m.HudRef.Command.TestEmergencyVisible, "Menü ohne „Notruf“, wenn der Treibstoff aus ist");
					_m.TestCloseCommand();
					// Handelsstation Gamma (Planet Thalassa): Exportsenke, keine Forschung, „Reparatur“ ohne „& Tanken“
					var post = uni.Stations[new StationId(39)];
					_m.TestDockAt(post.Id);
					var areas = _m.TestDockAreas();
					Check(!areas.Any(a => a.Id == Ui.Screens.DockAreas.Research), "Kein Dock-Bereich „Forschung“ ohne Forschung");
					Check(areas.Any(a => a.Id == Ui.Screens.DockAreas.Repair && a.Label == "Reparatur"), "Dock-Bereich heißt ohne Treibstoff nur „Reparatur“");
					var lux = uni.FindGood("luxury_goods")!.Id;
					var pl = _m.Session.Player;
					pl.Cargo[lux] = 2f;
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
					var sold = _m.HudRef.DockTrade.TestExecute($"sell:{lux.Value}", 2f);
					Check(sold.Success && post.Exports.Count >= 1, $"Luxusgüter an die Exportsenke verkauft, Charge wartet ({post.Exports.Count})");
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
					Check(_m.HudRef.DockInfo.TestExport.StartsWith("Export an Thalassa:") && _m.HudRef.DockInfo.TestExport.Contains("unterwegs"),
						$"Stationsinfo zeigt die Exportzeile („{_m.HudRef.DockInfo.TestExport}“)");
					// Phase 15: Instandhaltung der Station (Material über den Handel, Kasse erstattet) als Statuszeile
					string maint = _m.HudRef.DockInfo.TestMaintenance;
					Check(maint == "gedeckt" || maint.StartsWith("offen: "), $"Stationsinfo zeigt die Instandhaltung („{maint}“)");
					Check(_m.View.PlanetNode is not null, "Sektoransicht zeigt den Planeten als Kulisse");
					_m.TestUndock();
					// Werft: Reparatur ohne Tank-Abschnitt
					var yardSt = uni.Stations.Values.First(s => s.TypeKey == "shipyard");
					_m.TestDockAt(yardSt.Id);
					pl.Hull = pl.MaxHull - 20f;
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Repair);
					Check(_m.TestScreenOpen("dock.repair") && !_m.HudRef.DockRepair.TestFuelSection && _m.HudRef.DockRepair.TestRowCount >= 1,
						"Reparatur-Bereich ohne Tanken, Reparatur-Zeilen da");
					_m.TestUndock();
					// Kommandozentrale: Planet im Sektor- und Universums-Detail, Fraktion ohne Forschungsblock
					_m.TestOpenCommand();
					_m.TestOpenCcArea("universe");
					_m.TestCcSelectSector(new SectorId(3));
					Check(_m.HudRef.CcUniverse.TestPlanet == "Thalassa", "Universum-Detail nennt den Planeten");
					_m.TestOpenCcArea("sector");
					_m.TestCcSelectSectorView(new SectorId(2));
					Check(_m.HudRef.CcSector.TestPlanet == "Korvan", "Sektor-Detail nennt den Planeten");
					_m.TestOpenCcArea("factions");
					_m.HudRef.CcFactions.TestSelect(new FactionId(1));
					Check(!_m.HudRef.CcFactions.TestResearchShown, "Fraktionsdetail ohne Forschungsblock, wenn die Forschung aus ist");
					_m.TestCloseCommand();
				}
				catch (System.Exception ex) { Fail("Standardwelt (Phase 14) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 48: // Phase 20: Versorgungspanik — Alpha abgeschnitten (Phase 21: alle drei Tore Alphas, die Karte hat Ringe), Alpha fehlt Wasserstoff fürs
			         // Kraftwerk: Hinweis in der Ansicht, Ersatz-Baustelle
			{
				if (_frame - _stageStart < 10) break;
				try
				{
					var uni = _m.Session.U;
					uni.Rules.PlayerRaids = false;
					var gates = uni.Gates.Values.Where(g => g.Sector.Value == 1).ToList();
					foreach (var gate in gates) SpaceSim.Core.Simulation.GateAccess.SetState(uni, gate, GateState.Closed);
					Check(SpaceSim.Core.Simulation.MapParts.Of(uni).IsSplit, "Alpha ohne Tore: Karte zerfallen");
					_m.Session.RunTicks(uni.Rules.PanicDelayTicks + 100);
					var msg = _m.LastPanicMessage ?? "";
					Check(msg.StartsWith("Versorgungspanik"), $"Hinweis zur Versorgungspanik erscheint ({msg})");
					Check(uni.Stations.Values.Any(s => s.Site is { Panic: true }), "Panik legt eine Ersatz-Baustelle an");
					foreach (var gate in gates) SpaceSim.Core.Simulation.GateAccess.SetState(uni, gate, GateState.Open);
					_m.Session.RunTicks(40);
					Check(uni.PanicSince.Count == 0, "Tore wieder offen: keine Panik mehr");
				}
				catch (System.Exception ex) { Fail("Versorgungspanik (Phase 20) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 49: // Phase 21: 11-Sektoren-Karte — Universum (Route je Sektor = FindRoute, herrenlose Sektoren), Sternenkarte, Taxiliste mit allen Zielen
			{
				if (_frame - _stageStart < 10) break;
				try
				{
					var uni = _m.Session.U;
					Check(uni.Sectors.Count == 11, $"Standardwelt hat 11 Sektoren ({uni.Sectors.Count})");
					_m.TestOpenCommand();
					_m.TestOpenCcArea("universe");
					foreach (var sec in uni.Sectors.Values.OrderBy(x => x.Id.Value))
					{
						_m.TestCcSelectSector(sec.Id);
						int want = uni.FindRoute(uni.PlayerSector, sec.Id, SpaceSim.Core.Simulation.GateAccess.ProfileOf(uni, _m.Session.Player))?.Count ?? -1;
						Check(_m.HudRef.CcUniverse.TestRouteHops == want, $"Universum: Route nach {sec.Name} {_m.HudRef.CcUniverse.TestRouteHops} Sprünge (FindRoute {want})");
					}
					Check(uni.Sectors.Values.Count(x => !uni.Factions.ContainsKey(x.Owner)) == 2, "Zwei herrenlose Sektoren");
					_m.TestCloseCommand();
					_m.HudRef.Map.Open(uni, uni.PlayerSector, null);
					Check(_m.HudRef.Map.Visible, "Sternenkarte öffnet");
					_m.HudRef.Map.Visible = false;
					_m.TestDockAt(new StationId(30));   // Handelsstation Beta: Taxi
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Taxi);
					_m.HudRef.DockTaxi.TestRebuild();
					int rentalStands = uni.Stations.Values.Count(s => s.Id.Value != 30 && !s.IsBuildSite && uni.StationTypes[s.TypeKey].Services.Contains("rental"));
					Check(_m.HudRef.DockTaxi.TestRows == rentalStands && rentalStands >= 3, $"Taxi listet jedes Ziel mit Leihschiff ({_m.HudRef.DockTaxi.TestRows}/{rentalStands})");
					Check(_m.HudRef.DockTaxi.TestHidden > 20, $"Taxi fasst Ziele ohne Schiff und Leihdienst zusammen ({_m.HudRef.DockTaxi.TestHidden})");
					_m.TestUndock();
				}
				catch (System.Exception ex) { Fail("11-Sektoren-Karte (Phase 21) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 50: // Phase 22: Abgänge nur aus drei Gründen — Rentabilität in der Stationsinfo, Handlungsfähigkeit der Fraktion, Hinweis bei Pleite im Sektor
			{
				if (_frame - _stageStart < 10) break;
				try
				{
					var uni = _m.Session.U;
					_m.Session.RunTicks(120);   // mindestens zwei Messtakte der Rentabilität
					_m.TestOpenCommand();
					_m.TestOpenCcArea("factions");
					_m.HudRef.CcFactions.TestSelect(new FactionId(1));
					string via = SpaceSim.Core.Simulation.FactionViability.Text(uni, new ActorId(1)) ?? "";
					Check(via.Length > 0 && _m.HudRef.CcFactions.TestViability == via, $"Fraktionsdetail zeigt die Handlungsfähigkeit („{_m.HudRef.CcFactions.TestViability}“)");
					_m.TestCloseCommand();
					var src = uni.Stations.Values.Where(x => x.Sector == uni.PlayerSector && x.Production.Count > 0 && !x.IsBuildSite).OrderBy(x => x.Id.Value).FirstOrDefault();
					Check(src is not null, "Produktionsstation im Spielersektor");
					if (src is not null)
					{
						_m.TestDockAt(src.Id);
						_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
						Check(_m.HudRef.DockInfo.TestProfit.Contains("Cr/Tick"), $"Stationsinfo zeigt die Rentabilität („{_m.HudRef.DockInfo.TestProfit}“)");
						_m.TestUndock();
						// zweite, ungeschützte Station desselben Typs geht pleite → Hinweis mit Grund
						var copy = SpaceSim.Core.Content.StationFactory.Create(uni, uni.NewStationId(), uni.StationTypes[src.TypeKey], "Teststation " + src.TypeKey,
							src.Sector, src.Position + new SpaceSim.Core.Model.Vec2(0, 80), src.Owner);
						uni.AddStation(copy);
						copy.Credits = uni.Rules.BankruptcyLimit - 100f;
						copy.BelowLimitSince = uni.Tick - uni.Rules.BankruptcyTicks - 1;
						_m.Session.RunTicks(40);
						Check(!uni.Stations.ContainsKey(copy.Id), "Pleite-Station ist aufgegeben");
						Check((_m.LastDepartureMessage ?? "").Contains("pleite"), $"Hinweis nennt den Grund („{_m.LastDepartureMessage}“)");
					}
				}
				catch (System.Exception ex) { Fail("Abgänge (Phase 22) warfen eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			default:
				if (_fail.Length == 0) { GD.Print("SELFTEST OK"); GetTree().Quit(0); }
				else { GD.PrintErr("SELFTEST FAILED: " + _fail); GetTree().Quit(1); }
				SetProcess(false);
				break;
		}
	}

	void Next() { _stage++; _stageStart = _frame; }
	void Log(string s) => GD.Print("[selftest] " + s);
	/// <summary>Phase 18: Ware „Nahrung“ der Welt (für die Abwurf-Prüfungen).</summary>
	static SpaceSim.Core.Model.GoodId DemoFood(SpaceSim.Core.Model.Universe u) => u.FindGood("food")!.Id;

	void Check(bool ok, string what) { if (!ok) { _fail += what + "; "; GD.PrintErr("[selftest] FEHLER: " + what); } else Log("ok: " + what); }
	void Fail(string what) { _fail += what + "; "; GD.PrintErr("[selftest] FEHLER: " + what); _stage = 99; }
}
