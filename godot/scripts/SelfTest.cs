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

	public SelfTest(Main m)
	{
		_m = m; SpaceAdventure.Ui.Kit.KitConfirm.DebounceMs = 0;   // der Selbsttest tippt ohne Pause doppelt
		// Phase 27: „--selftest-from=67“ beginnt bei einer späteren, in sich geschlossenen Stufe (Entwicklungshilfe; der volle Lauf beginnt bei 0)
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--selftest-from=") && int.TryParse(a["--selftest-from=".Length..], out int from)) _stage = from;
	}

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
					// Phase 29: das ganze Raster (30 Modelle, auch Korsar, Fregatte und Schlachtkreuzer) steht in der Werft
					Check(yardOffers.Count == u.ShipModels.Count && yardOffers.Contains("buy:corsair") && yardOffers.Contains("buy:battlecruiser") && yardOffers.Contains("buy:strip_miner"),
						$"Werft bietet alle {u.ShipModels.Count} Modelle an ({yardOffers.Count})");
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
				_m.TestSetTool("mining_laser_1");   // Phase 29: das Startschiff trägt einen Bohrkopf — die Lasertaste prüfen wir mit dem Mk1-Laser
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
				if (_m.TestLaserReady) { Check(_m.TestBayState(0) == "Ready", "Waffentaste wieder bereit nach dem Nachladen"); _m.TestSetTool("drill_head_s"); Next(); }
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
				if (_m.TestSales >= 1) { Log($"Verkauf nach {_frame - _stageStart} Frames, Credits {_m.Session.U.PlayerWallet.Credits:0}"); Check(_m.Session.U.PlayerWallet.Credits > 500f, "Credits gestiegen"); Check(_m.TestHits == 0, $"Auto-Mining ohne harte Kollision (Treffer: {_m.TestHits}, niedrigste Hülle {_m.TestLowestHull:0}) – {_m.TestLastHit}");
					// Phase 29: der Einheitsschürfer baut mit dem Bohrkopf im Kontakt ab (Funken), Schacht „Bohrer“ statt „Laser“
					Check(_m.TestContactSparks > 0, $"Kontaktabbau hat Erz gelöst (Portionen {_m.TestContactSparks})");
					Check(_m.TestBayKeys.Contains("contact") && !_m.TestBayKeys.Contains("laser"), $"Schächte des Bohrschiffs ({string.Join(",", _m.TestBayKeys)})");
					Next(); }
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
				if (_frame - _stageStart == 1)
				{
					// Runde 18: verankert rückt die Kamera näher heran (Main.AnchorZoom) — für die Standardansicht weg vom Fels, Anker gelöst
					var cu = _m.Session.U; var cme = cu.Ships[cu.PlayerShip];
					if (cme.AnchorRock != 0 && cu.Sectors[cu.PlayerSector].Asteroids.FirstOrDefault(a => a.Id == cme.AnchorRock) is { } crk)
					{
						SpaceSim.Core.Mining.MiningAnchor.Release(cu, cme, cu.PlayerSector, "released");
						_m.TestTeleportTo(crk.Position + (cme.Position - crk.Position).Normalized() * (crk.Radius + 40f));
					}
				}
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
				// Runde 12: gleichmäßige Stufen, über das Menü bis zur Höchstdistanz
				for (int i = 0; i < SpaceSim.Core.Flight.OrbitCamera.ZoomSteps + 1; i++) _m.TestCameraAction("zoom_out");
				Check(System.Math.Abs(_m.TestCamAngles.Dist - SpaceSim.Core.Flight.OrbitCamera.MaxDistance) < 0.5f, $"Menü: Zoom − erreicht {SpaceSim.Core.Flight.OrbitCamera.MaxDistance} ({_m.TestCamAngles.Dist:0})");
				_m.TestResetCam();
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
						Check(_m.HudRef.Flight.TestWanted.Contains("auffällig") && _m.HudRef.Flight.TestWanted.Contains(uni.Factions[guild].Name), $"Flug-HUD zeigt die Fahndung („{_m.HudRef.Flight.TestWanted}“)");
						_m.TestOpenCommand();
						_m.TestOpenCcArea("myfaction");
						Check(_m.HudRef.CcMyFaction.TestWanted.Contains(uni.Factions[guild].Name) && _m.HudRef.CcMyFaction.TestWanted.Contains("auffällig"), $"Meine Fraktion zeigt die Fahndung je Fraktion („{_m.HudRef.CcMyFaction.TestWanted}“)");
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
				// Phase 27: Kaperer laufen über den Befehl „Lauern“ (Posten = hier, Frist weit)
				if (uni.Rules.FleetCombat)
				{
					if (SpaceSim.Core.Simulation.Squadrons.Get(uni, raider) is { } rsq) SpaceSim.Core.Simulation.Squadrons.Disband(uni, rsq);
					SpaceSim.Core.Simulation.Commands.Give(uni, raider, new LurkCommand(sector.Id, raider.Position, uni.Tick + 100_000), out _, ignoreCapacity: true);
				}
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
			case 51: // Phase 23: Schiffssysteme — Schild- und Leistungsbalken echt, drei Tasten der Leistungsverteilung, Schild fängt Schaden zuerst und lädt nach
			{
				if (_frame - _stageStart < 10) break;
				try
				{
					var uni = _m.Session.U;
					var ship = _m.Session.Player;
					var hud = _m.HudRef.Flight;
					Check(hud.TestPowerText.StartsWith($"{ship.Stats.PowerDraw:0}/{ship.Stats.PowerOutput:0}"), $"HUD-Leistung aus dem Core („{hud.TestPowerText}“)");
					Check(hud.TestShieldText == (ship.Stats.Shield > 0f ? $"{(int)ship.ShieldNow}" : "–"), $"HUD-Schild aus dem Core („{hud.TestShieldText}“)");
					// drei Tasten: Mindest-Trefferfläche 56 px, keine Überdeckung untereinander, mit der Statusleiste (bis y 214) und den Wendetasten (ab y 560)
					var rects = new[] { PowerMode.Weapons, PowerMode.Shield, PowerMode.Engine }.Select(hud.TestModeRect).ToList();
					Check(rects.All(r => r.Size.X >= 56f && r.Size.Y >= 56f), "Verteilungstasten mindestens 56 px");
					Check(rects.All(r => r.Position.Y >= 214f && r.End.Y <= 560f), "Verteilungstasten zwischen Statusleiste und Wendetasten");
					Check(!rects[0].Intersects(rects[1]) && !rects[1].Intersects(rects[2]) && !rects[0].Intersects(rects[2]), "Verteilungstasten überdecken sich nicht");
					hud.TestPressMode(PowerMode.Shield);
					Check(ship.PowerMode == PowerMode.Shield, "Taste „Schild“ stellt die Verteilung im Core um");
					hud.Refresh(new Ui.Screens.FlightHudData { PowerMode = ship.PowerMode });
					hud.TestPressMode(PowerMode.Shield);
					Check(ship.PowerMode == PowerMode.Balanced, "Zweites Tippen auf „Schild“ = ausgeglichen");
					hud.TestPressMode(PowerMode.Engine);
					Check(ship.PowerMode == PowerMode.Engine && SpaceSim.Core.Combat.ShipSystems.SpeedFactor(uni, ship) > 1f, "Stellung „Antrieb“ hebt das Höchsttempo");
					SpaceSim.Core.Combat.ShipSystems.SetMode(uni, ship, PowerMode.Balanced);
					// Schildmodul in einen zusätzlichen Slot (nur für den Test), Treffer, der Schild fängt zuerst ab
					ship.Slots.Add(new ClassSlot { Type = "shield", Size = SlotSize.S });
					ship.Fit.Add("shield_mk1");
					ship.RecomputeStats(uni);
					_p23Hull = ship.Hull;
					float rest = SpaceSim.Core.Combat.ShipSystems.Absorb(uni, ship, 30f);
					Check(rest == 0f && System.MathF.Abs(ship.ShieldNow - (ship.Stats.Shield - 30f)) < 0.01f && ship.Hull == _p23Hull, $"Schild fängt den Treffer ab (Schild {ship.ShieldNow:0}/{ship.Stats.Shield:0})");
					_p23Shield = ship.ShieldNow;
				}
				catch (System.Exception ex) { Fail("Schiffssysteme (Phase 23) warfen eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 52: // Phase 23: HUD zeigt den getroffenen Schild, nach der Pause lädt er aus dem Überschuss nach
			{
				if (_frame - _stageStart < 5) break;
				try
				{
					var uni = _m.Session.U;
					var ship = _m.Session.Player;
					var hud = _m.HudRef.Flight;
					Check(hud.TestShieldText == $"{(int)ship.ShieldNow}" && ship.ShieldNow < ship.Stats.Shield, $"HUD zeigt den getroffenen Schild („{hud.TestShieldText}“)");
					Check(uni.Recovering.Contains(ship.Id), "Schiff steht im Erholungsindex");
					_m.Session.RunTicks(200);
					Check(ship.ShieldNow > _p23Shield, $"Schild lädt nach ({_p23Shield:0} → {ship.ShieldNow:0})");
					// Testslot wieder entfernen
					ship.Slots.RemoveAt(ship.Slots.Count - 1);
					ship.Fit.RemoveAt(ship.Fit.Count - 1);
					ship.RecomputeStats(uni);
					Check(ship.Stats.Shield == 0f && ship.ShieldNow == 0f, "Ohne Schildmodul kein Schild");
				}
				catch (System.Exception ex) { Fail("Schildanzeige (Phase 23) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 53: // Phase 24: NPC-Gefecht im Spielersektor — Hinweis, Schüsse als Strahlen aus den Ereignissen, Sektorkarte zeigt das Gefecht, saubere Freigabe
			{
				if (_frame - _stageStart < 5) break;
				try
				{
					var uni = _m.Session.U;
					var sec = uni.Sectors[uni.PlayerSector];
					var raider = uni.ShipsById().FirstOrDefault(SpaceSim.Core.Simulation.ThreatMap.IsRaider);
					Check(raider is not null, "Kaperschiff in der Welt");
					if (raider is not null)
					{
						var model = uni.ShipModels["unit_trader_100"];
						var at = uni.StationsById().First(x => !x.IsBuildSite);
						var victim = SpaceSim.Core.Simulation.ShipPurchase.Create(uni, uni.NewShipId(), model.Key, uni.ShipClasses[model.ClassKey].Slots, model.Fit,
							ShipRole.Trader, Controller.Ai, uni.FreeActor, at, 1000f);
						uni.AddShip(victim);
						var pl = uni.Ships[uni.PlayerShip];
						void Put(Ship s, Vec2 p)
						{
							if (s.Transit is not null) uni.FinishTransit(s);
							s.Order = null;
							if (s.Sector != sec.Id) uni.MoveShip(s, sec.Id);
							s.Position = p; s.DockedAt = default;
						}
						Put(victim, pl.Position + new Vec2(40f, 20f));
						Put(raider, pl.Position + new Vec2(55f, 30f));
						victim.Cargo[uni.FindGood("electronics")!.Id] = 40f;
						int shots0 = _m.TestCombatShots;
						var enc = SpaceSim.Core.Combat.Engage.StartRaid(uni, raider, victim, sec);
						Check((_m.LastCombatMessage ?? "").StartsWith("Gefecht"), $"Hinweis beim Gefechtsbeginn („{_m.LastCombatMessage}“)");
						Check(SpaceSim.Core.Simulation.SecurityInfo.ForSector(uni, sec.Id, pl).Fights >= 1 && SpaceSim.Core.Combat.CombatInfo.InSector(uni, sec.Id).Any(c => c.Id == enc.Id),
							"Lagekarte zählt das Gefecht");
						_m.TestOpenCommand();
						_m.TestOpenCcArea("sector");
						_m.TestCcSelectSectorView(sec.Id);
						Check(_m.HudRef.CcSector.TestCombat.StartsWith("Gefecht: "), $"Sektorkarte zeigt das Gefecht („{_m.HudRef.CcSector.TestCombat}“)");
						_m.TestCloseCommand();
						_m.Session.RunTicks(40);
						Check(_m.TestCombatShots > shots0, $"Schüsse als Strahlen gezeichnet ({_m.TestCombatShots - shots0})");
						Check(enc.Shots > 0, "Gefecht feuert im Core");
						for (int i = 0; i < 10 && uni.Fights.IsEngaged(victim.Id); i++) _m.Session.RunTicks(40);
						Check(!uni.Fights.IsEngaged(victim.Id) && !uni.Fights.IsEngaged(raider.Id), "Gefecht endet, beide sind frei");
						Check(uni.Fights.Count == 0 || uni.Fights.Encounters.All(x => x.Id != enc.Id), "Gefecht ist ausgetragen");
					}
				}
				catch (System.Exception ex) { Fail("Gefecht (Phase 24) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 54: // Phase 25: Spielerkampf — in einen Jäger (Falke) wechseln, im freien Raum
			{
				if (_frame - _stageStart < 5) break;
				try
				{
					var uni = _m.Session.U;
					if (_m.Docked) { _m.TestUndock(); _stageStart = _frame; break; }
					// in Alpha (Union) — Fahndung und Kopfgeld brauchen einen Sektorbesitzer
					if (uni.PlayerSector.Value != 1) { _m.TestEnterSector(new SectorId(1)); _stageStart = _frame; break; }
					var pl = uni.Ships[uni.PlayerShip];
					var model = uni.ShipModels["falcon"];
					var at = uni.StationsById().First(x => !x.IsBuildSite && x.Sector == uni.PlayerSector);
					var falcon = SpaceSim.Core.Simulation.ShipPurchase.Create(uni, uni.NewShipId(), model.Key, uni.ShipClasses[model.ClassKey].Slots, model.Fit,
						ShipRole.Player, Controller.Player, uni.PlayerActor, at, 0f);
					uni.AddShip(falcon);
					falcon.DockedAt = default;
					falcon.Position = pl.Position + new Vec2(6f, 0f);
					_p25Falcon = falcon.Id;
					_m.TestSwitchRemote(falcon.Id);
				}
				catch (System.Exception ex) { Fail("Jäger (Phase 25) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 55: // Phase 25: Kaperer vor dem Bug, „Nächster Gegner“, Zielkarte mit Schild/Hülle, Schuss über die Schachttaste bucht Treffer/Munition/Bank
			{
				if (_frame - _stageStart < 8) break;
				try
				{
					var uni = _m.Session.U;
					var pl = uni.Ships[uni.PlayerShip];
					Check(pl.Id == _p25Falcon && !_m.Docked, "Spieler fliegt den Falken im freien Raum");
					// Runde 10: ein Kaperer, der gerade nicht kämpft, mit voller Hülle (der Pfad kann den ersten in ein anderes Gefecht verwickelt haben)
					var raider = uni.ShipsById().Where(SpaceSim.Core.Simulation.ThreatMap.IsRaider).OrderBy(r => uni.Fights.IsEngaged(r.Id) ? 1 : 0).First();
					if (uni.Fights.Of(raider.Id) is { } busy && busy.Of(raider.Id) is { } bp) SpaceSim.Core.Combat.Engage.Leave(uni, busy, bp, SpaceSim.Core.Model.CombatExit.Fled);
					if (raider.Transit is not null) uni.FinishTransit(raider);
					raider.Hull = raider.MaxHull;
					raider.Order = new PatrolOrder(uni.PlayerSector); raider.NextThinkTick = uni.Tick + 100000;
					if (raider.Sector != uni.PlayerSector) uni.MoveShip(raider, uni.PlayerSector);
					raider.Position = pl.Position + new Vec2(0f, -25f); raider.DockedAt = default;
					_p25Raider = raider.Id;
					var owner = uni.Sectors[uni.PlayerSector].Owner;
					SpaceSim.Core.Simulation.Wanted.Report(uni, raider.Owner, owner, "piracy");   // Kopfgeld ausgeschrieben
					_m.TestFace(raider.Position);
					_m.TestNextEnemy();
					Check(_m.TestFight.Target == raider.Id, "„Nächster Gegner“ wählt das Kaperschiff");
					var keys = _m.TestBayKeys.ToList();
					int wi = keys.FindIndex(k => k.StartsWith(SpaceSim.Core.Combat.PlayerGunnery.BayPrefix));
					Check(keys.Count(k => k.StartsWith(SpaceSim.Core.Combat.PlayerGunnery.BayPrefix)) == 2, $"zwei Waffenschächte ({string.Join(",", keys)})");
					var hud = _m.HudRef.Flight;
					Check(hud.TestBayRect(wi).Size.Y >= 56f && hud.TestAutoFireRect.Size.Y >= 56f && hud.TestNextEnemyRect.Size.Y >= 56f, "Kampftasten mindestens 56 px");
					Check(hud.TestNextEnemyVisible, "Taste „Nächster Gegner“ sichtbar");
					_p25Shots = uni.Fights.Of(pl.Id)?.Shots ?? 0;
					_p25Charge = pl.Charge;
					_p25Ammo = pl.AmmoLoaded;
					_p25Rng = uni.Rng.State;
				}
				catch (System.Exception ex) { Fail("Zielwahl (Phase 25) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 56:
			{
				if (_frame - _stageStart < 3) break;
				try
				{
					var uni = _m.Session.U;
					var pl = uni.Ships[uni.PlayerShip];
					var raider = uni.Ships[_p25Raider];
					Check(_m.HudRef.Flight.TestTargetCombat.Contains("Hülle"), $"Zielkarte zeigt Schild und Hülle („{_m.HudRef.Flight.TestTargetCombat}“)");
					// Runde 12 (Handy-Test): der Gegner vor dem Bug liegt nicht unter Meldung, Beschuss- oder Fluchtzeile
					if (_m.TestView.ShipPosition(raider.Id) is { } rp)
					{
						var sp = _m.TestScreenOf(rp);
						var rects = new[] { _m.HudRef.TestAlertRect, _m.HudRef.Flight.TestHitWarningRect, _m.HudRef.Flight.TestEscapeRect };
						Check(!rects.Any(r => r.Grow(8f).HasPoint(sp)), $"Gegner im Bild frei von Warnflächen ({sp})");
					}
					_m.TestFace(raider.Position);
					var keys = _m.TestBayKeys.ToList();
					int wi = keys.FindIndex(k => k == SpaceSim.Core.Combat.PlayerGunnery.BayPrefix + pl.Fit.IndexOf("weapon_laser_s"));
					int gi = keys.FindIndex(k => k == SpaceSim.Core.Combat.PlayerGunnery.BayPrefix + pl.Fit.IndexOf("weapon_massdriver_s"));
					_m.TestPressBay(wi);
					_m.TestPressBay(gi);
					var e = uni.Fights.Of(pl.Id);
					Check(e is not null && e.Shots >= _p25Shots + 2, $"Schachttasten feuern über die Core-Regel ({_m.TestFight.LastMessage})");
					Check(uni.Rng.State != _p25Rng, "Schuss würfelt über Universe.Rng");
					Check(pl.Charge < _p25Charge, "Laser zieht Spitzenlast aus der Speicherbank");
					Check(pl.AmmoLoaded < _p25Ammo, "Geschütz verbraucht Munition aus dem Magazin");
					Check(SpaceSim.Core.Combat.PlayerGunnery.ReloadLeft(uni, pl, pl.Fit.IndexOf("weapon_laser_s")) > 0f, "Laserschacht lädt nach");
					raider.Hull = 1f; raider.Systems.ShieldMissing = raider.Stats.Shield;
					_m.TestFight.AutoFire = true;
				}
				catch (System.Exception ex) { Fail("Spielerschuss (Phase 25) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 57: // Dauerfeuer bis zum Abschuss → Kopfgeld
			{
				var uni = _m.Session.U;
				var pl = uni.Ships[uni.PlayerShip];
				pl.Hull = pl.MaxHull;
				if (uni.Ships.TryGetValue(_p25Raider, out var raider))
				{
					// in Reichweite halten (flieht er, holt ihn der Test zurück — geprüft wird der Weg über die Schachttasten)
					if (Vec2.Distance(raider.Position, pl.Position) > 30f)
					{
						raider.Position = pl.Position + new Vec2(0f, -20f);
						if (uni.Fights.Of(raider.Id)?.Of(raider.Id) is { } rp) { rp.Pos = rp.PrevPos = raider.Position; }
					}
					if (!uni.Fights.IsEngaged(raider.Id)) _m.TestSetShipTarget(raider.Id);
					_m.TestFace(raider.Position);
					raider.Hull = System.MathF.Min(raider.Hull, 1f); raider.Systems.ShieldMissing = raider.Stats.Shield;
					if (_frame - _stageStart < 1500) break;
					Fail("Kaperschiff nach 1500 Frames Dauerfeuer nicht zerstört");
					Next(); break;
				}
				if (_frame - _stageStart < 3 + 0) { break; }
				_m.Session.RunTicks(2);
				Check(uni.Actors[uni.PlayerActor].BountyClaims.Values.Sum() > 0f, "Abschuss des Kaperers bringt Kopfgeld");
				Check(SpaceSim.Core.Simulation.Wanted.MaxLevel(uni, uni.PlayerActor) == 0, "Abschuss eines Gesetzlosen ist kein Verbrechen");
				_m.TestFight.AutoFire = false;
				Next(); break;
			}
			case 58: // Angriff auf einen Händler → Fahndung, Handel verweigert
			{
				if (_frame - _stageStart < 3) break;
				try
				{
					var uni = _m.Session.U;
					var pl = uni.Ships[uni.PlayerShip];
					var model = uni.ShipModels["unit_trader_100"];
					var at = uni.StationsById().First(x => !x.IsBuildSite);
					var trader = SpaceSim.Core.Simulation.ShipPurchase.Create(uni, uni.NewShipId(), model.Key, uni.ShipClasses[model.ClassKey].Slots, model.Fit,
						ShipRole.Trader, Controller.Ai, uni.FreeActor, at, 1000f);
					uni.AddShip(trader);
					if (trader.Sector != uni.PlayerSector) uni.MoveShip(trader, uni.PlayerSector);
					trader.Position = pl.Position + new Vec2(12f, -12f); trader.DockedAt = default;
					trader.NextThinkTick = uni.Tick + 100000;   // bleibt stehen (kein neuer Auftrag während des Tests)
					_m.TestFace(trader.Position);
					_m.TestSetShipTarget(trader.Id);
					_m.Session.RunTicks(9);   // Waffen nachgeladen
					_p25Trader = trader.Id;
				}
				catch (System.Exception ex) { Fail("Angriff (Phase 25) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 59:
			{
				if (_frame - _stageStart < 3) break;
				try
				{
					var uni = _m.Session.U;
					var pl = uni.Ships[uni.PlayerShip];
					if (uni.Ships.TryGetValue(_p25Trader, out var tr)) _m.TestFace(tr.Position);
					var keys = _m.TestBayKeys.ToList();
					_m.TestPressBay(keys.FindIndex(k => k == SpaceSim.Core.Combat.PlayerGunnery.BayPrefix + pl.Fit.IndexOf("weapon_laser_s")));
					var owner = uni.Sectors[uni.PlayerSector].Owner;
					int level = SpaceSim.Core.Simulation.Wanted.LevelOf(uni, uni.PlayerActor, owner);
					Check(level >= uni.Rules.TradeRefusalWantedLevel, $"Angriff auf Händler: Fahndung Stufe {level} ({_m.TestFight.LastMessage}, Sektor {uni.Sectors[uni.PlayerSector].Name})");
					var st = uni.StationsById().First(x => uni.FactionOf(x) == owner && x.Market.Count > 0 && !x.IsBuildSite);
					var offers = new SpaceSim.Core.Services.TradeService().Offers(uni, st, pl);
					Check(offers.Any(o => (o.Note ?? "").Contains("verweigert")), "Handel an einer Station der Fraktion verweigert (Grund im Angebot)");
					// Fahndung löschen, Gefecht beenden (für die nächsten Stufen)
					foreach (var a in uni.Actors.Values) a.Wanted.Clear();
					SpaceSim.Core.Combat.PlayerCombat.Withdraw(uni);
					_m.Session.RunTicks(2);
				}
				catch (System.Exception ex) { Fail("Angriff (Phase 25) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 60: // Verlust des Schiffs → Landung an einer Station im eigenen oder Leihschiff, Geld bleibt
			{
				var uni = _m.Session.U;
				if (_frame - _stageStart == 3)
				{
					var pl = uni.Ships[uni.PlayerShip];
					_p25Money = uni.WalletOf(pl).Credits;
					var raider = uni.ShipsById().FirstOrDefault(SpaceSim.Core.Simulation.ThreatMap.IsRaider);
					if (raider is null) { Fail("kein Kaperschiff für den Verlusttest"); Next(); break; }
					if (raider.Transit is not null) uni.FinishTransit(raider);
					if (raider.Sector != uni.PlayerSector) uni.MoveShip(raider, uni.PlayerSector);
					raider.Position = pl.Position + new Vec2(0f, -20f); raider.DockedAt = default;
					raider.Hull = raider.MaxHull; raider.Systems.ShieldMissing = 0f; raider.Systems.AmmoMissing = 0f;   // kampfbereit (flieht nicht)
					pl.Cargo[uni.FindGood("electronics")!.Id] = 5f;
					SpaceSim.Core.Combat.Engage.StartRaid(uni, raider, pl, uni.Sectors[uni.PlayerSector]);
				}
				if (_frame - _stageStart < 4) break;
				if (uni.Ships.TryGetValue(_p25Falcon, out var old))
				{
					old.Hull = System.MathF.Min(old.Hull, 0.5f); old.Systems.ShieldMissing = old.Stats.Shield;
					if (_frame - _stageStart < 2000) break;
					Fail("Spielerschiff nach 2000 Frames im Gefecht nicht verloren"); Next(); break;
				}
				if (_p25LostFrame < 0) _p25LostFrame = _frame;
				if (_frame - _p25LostFrame < 6) break;
				var now = uni.Ships[uni.PlayerShip];
				Check(_m.LastLossMessage is { Length: > 0 }, $"Verlustmeldung („{_m.LastLossMessage}“)");
				Check(now.IsPlayer && uni.Stations.ContainsKey(now.DockedAt), "Person sitzt an einer Station im eigenen oder Leihschiff");
				Check(_m.Docked, "Ansicht zeigt das Andock-Menü der Landestation");
				Check(System.MathF.Abs(uni.WalletOf(now).Credits - _p25Money) < 0.5f || now.IsRental, $"Geld bleibt ({uni.WalletOf(now).Credits:0} von {_p25Money:0})");
				Next(); break;
			}
			case 61: // rearm über den Bildschirm „Reparatur“: Falke an der Werft, Magazin leer
			{
				if (_frame - _stageStart < 5) break;
				try
				{
					var uni = _m.Session.U;
					var yard2 = uni.StationsById().First(x => x.TypeKey == "shipyard");
					var model = uni.ShipModels["falcon"];
					var f2 = SpaceSim.Core.Simulation.ShipPurchase.Create(uni, uni.NewShipId(), model.Key, uni.ShipClasses[model.ClassKey].Slots, model.Fit,
						ShipRole.Player, Controller.Player, uni.PlayerActor, yard2, 0f);
					uni.AddShip(f2);
					SpaceSim.Core.Simulation.PlayerFleet.Dock(uni, f2, yard2);
					_m.TestSwitchRemote(f2.Id);
					_p25Falcon = f2.Id;
					var ammo = SpaceSim.Core.Combat.Rearm.AmmoGood(uni)!;
					yard2.Market[ammo.Id].Amount = 20f;
					f2.Systems.AmmoMissing = f2.Stats.AmmoCapacity;
					uni.WalletOf(f2).Credits = System.MathF.Max(uni.WalletOf(f2).Credits, 2000f);
				}
				catch (System.Exception ex) { Fail("rearm-Vorbereitung (Phase 25) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 62:
			{
				if (_frame - _stageStart < 8) break;
				try
				{
					var uni = _m.Session.U;
					var f2 = uni.Ships[_p25Falcon];
					Check(_m.Docked && uni.PlayerShip == f2.Id, "im Falken an der Werft angedockt");
					Check(_m.TestDockAreas().Any(a => a.Id == Ui.Screens.DockAreas.Repair && a.Available), "Bereich „Reparatur“ verfügbar (mit Munition)");
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Repair);
					var scr = _m.HudRef.DockRepair;
					float before = f2.AmmoLoaded;
					var bad = scr.Execute(SpaceSim.Core.Services.RearmService.ServiceKey, "rearm:7");
					Check(!bad.Success && f2.AmmoLoaded == before, "rearm: unbekannter Schlüssel abgelehnt ohne Änderung");
					var r = scr.Execute(SpaceSim.Core.Services.RearmService.ServiceKey, "rearm:10");
					Check(r.Success && f2.AmmoLoaded >= before + 9.99f, $"rearm über den Bildschirm lädt Munition ({r.Message})");
					Check(scr.TestMessage == r.Message, "Meldung des Dienstes angezeigt");
				}
				catch (System.Exception ex) { Fail("rearm (Phase 25) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 63: // Phase 26: keine Gratisreparatur beim Andocken — abdocken, Schaden und Brand, HUD-Warnung, wieder andocken
			{
				var uni = _m.Session.U;
				if (_frame - _stageStart == 1) _m.TestUndock();
				if (_frame - _stageStart < 70) break;
				var pl = uni.Ships[uni.PlayerShip];
				if (_frame - _stageStart == 70)
				{
					pl.Hull = pl.MaxHull * 0.4f;
					_p26Laser = pl.Fit.IndexOf("weapon_laser_s");
					SpaceSim.Core.Combat.ModuleDamage.SetCondition(uni, pl, _p26Laser, 0.4f);
					SpaceSim.Core.Combat.ModuleDamage.Ignite(uni, pl, pl.Fit.FindIndex(k => k is not null && k.StartsWith("reactor")));
					uni.Rules.FireExtinguishChance = 0f;
					break;
				}
				if (_frame - _stageStart == 76)
				{
					Check(!_m.Docked && pl.Systems.Burning.Count > 0, "Schiff brennt im Flug");
					Check(_m.HudRef.Flight.TestFire.Contains("BRAND"), $"HUD-Warnung „Brand“ ({_m.HudRef.Flight.TestFire})");
					_p26Hull = pl.Hull;
					_m.TestDockNearest();
					break;
				}
				if (_frame - _stageStart < 90) break;
				Check(_m.Docked, "wieder angedockt");
				Check(pl.Hull < pl.MaxHull - 1f && pl.Hull <= _p26Hull + 0.5f, $"keine Gratisreparatur beim Andocken (Hülle {pl.Hull:0}/{pl.MaxHull:0})");
				Check(pl.Systems.Burning.Count == 0, "Andocken löscht den Brand");
				Check(_m.HudRef.Flight.TestFire.Length == 0 || !_m.HudRef.Flight.Visible, "keine Brandwarnung mehr");
				uni.Rules.FireExtinguishChance = new SpaceSim.Core.Model.EconomyRules().FireExtinguishChance;
				Next(); break;
			}
			case 64: // Reparatur über den Bildschirm: Schadensliste, Modulzeile, „alles“
			{
				if (_frame - _stageStart < 4) break;
				try
				{
					var uni = _m.Session.U;
					var pl = uni.Ships[uni.PlayerShip];
					var st = uni.Stations[_m.TestDockedStation!.Value];
					var plates = SpaceSim.Core.Services.RepairService.PlatesGood(uni)!.Value;
					var elec = SpaceSim.Core.Services.RepairService.ElectronicsGood(uni)!.Value;
					if (st.Market.ContainsKey(plates)) st.Market[plates].Amount = 50f;
					if (st.Market.ContainsKey(elec)) st.Market[elec].Amount = 50f;
					uni.WalletOf(pl).Credits = System.MathF.Max(uni.WalletOf(pl).Credits, 5000f);
					Check(_m.TestDockAreas().Any(a => a.Id == Ui.Screens.DockAreas.Repair && a.Available), "Bereich „Reparatur“ verfügbar");
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Repair);
					var scr = _m.HudRef.DockRepair;
					Check(scr.TestDamageRows >= 1, $"Schadensliste zeigt beschädigte Module ({scr.TestDamageRows})");
					var bad = scr.Execute(SpaceSim.Core.Services.RepairService.ServiceKey, "repair:module:x");
					Check(!bad.Success, "unbekannter Reparaturschlüssel abgelehnt");
					var r = scr.Execute(SpaceSim.Core.Services.RepairService.ServiceKey, "repair:all");
					Check(r.Success && pl.ConditionOf(_p26Laser) >= 0.999f && pl.Hull >= pl.MaxHull - 0.5f, $"„Alles reparieren“ über den Bildschirm ({r.Message})");
				}
				catch (System.Exception ex) { Fail("Reparatur (Phase 26) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			case 65: // Wrack und Bergung: Schiff mit Bergungsgreifer, Wrack vor dem Bug, Modul bergen, Traktor zieht Fracht
			{
				var uni = _m.Session.U;
				if (_frame - _stageStart == 1)
				{
					try
					{
						var yard3 = uni.Stations[_m.TestDockedStation!.Value];
						var model = uni.ShipModels["prospector"];
						var fit = model.Fit.ToList();
						int tool = uni.ShipClasses[model.ClassKey].Slots.FindIndex(sl => sl.Type == "tool");
						fit[tool] = "wreck_grapple_s";
						var sv = SpaceSim.Core.Simulation.ShipPurchase.Create(uni, uni.NewShipId(), model.Key, uni.ShipClasses[model.ClassKey].Slots, fit,
							ShipRole.Player, Controller.Player, uni.PlayerActor, yard3, 0f);
						uni.AddShip(sv);
						SpaceSim.Core.Simulation.PlayerFleet.Dock(uni, sv, yard3);
						_m.TestSwitchRemote(sv.Id);
						_p26Salvager = sv.Id;
					}
					catch (System.Exception ex) { Fail("Bergung-Vorbereitung (Phase 26) warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
					break;
				}
				if (_frame - _stageStart == 8) _m.TestUndock();
				if (_frame - _stageStart < 80) break;
				var pl = uni.Ships[uni.PlayerShip];
				if (_frame - _stageStart == 80)
				{
					Check(pl.Id == _p26Salvager && pl.Has(SpaceSim.Core.Model.Capability.Salvage), "Schiff mit Bergungsgreifer aktiv");
					Check(_m.TestBays.Any(b => b.Key == "salvage"), "Schacht „Bergen“ vorhanden");
					var food = uni.FindGood("food")!.Id;
					var w = SpaceSim.Core.Simulation.Wrecks.Drop(uni, uni.PlayerSector, pl.Position + new Vec2(0f, -8f),
						new System.Collections.Generic.Dictionary<SpaceSim.Core.Model.GoodId, float> { [food] = 6f }, default, "Wrack: Selbsttest")!;
					w.Kind = SpaceSim.Core.Model.WreckKind.Wreck;
					w.Modules.Add(new SpaceSim.Core.Model.WreckModule { Key = "reactor_mk1", Condition = 0.8f });
					_p26Wreck = w.Id;
					_p26Cargo = pl.Cargo.Values.Sum();
					break;
				}
				if (_frame - _stageStart == 84)
				{
					Check(_m.View.WreckCount >= 1, "Wrack in der Sektoransicht gezeichnet");
					bool ok = _m.TestGrabSalvage();
					Check(ok && pl.Cargo.GetValueOrDefault(uni.FindGood("reactor_mk1")!.Id) >= 1f, $"Modul geborgen ({_m.TestSalvage.LastMessage})");
					_m.TestTractor(true);
					break;
				}
				if (_frame - _stageStart < 200) break;
				Check(_m.TestSalvage.Pulled > 0f, $"Traktorstrahl zieht Fracht aus dem Wrack ({_m.TestSalvage.Pulled:0.#})");
				Check(SpaceSim.Core.Simulation.Wrecks.Get(uni, _p26Wreck) is null, "leeres Wrack verschwindet");
				_m.TestTractor(false);
				// Schulden sichtbar
				float keep = uni.PlayerWallet.Credits;
				uni.PlayerWallet.Credits = -250f;
				_p26Keep = keep;
				Next(); break;
			}
			case 66:
			{
				if (_frame - _stageStart < 3) break;
				var uni = _m.Session.U;
				Check(_m.HudRef.Flight.TestCredits.Contains("Schulden"), $"HUD zeigt Schulden ({_m.HudRef.Flight.TestCredits})");
				uni.PlayerWallet.Credits = _p26Keep;
				Next(); break;
			}
			case 67: // Gerätetest: „Testszenario“ starten — Lage vorbereitet; Zeitraffer schaltet bei Gefahr auf ×1 mit wichtiger Meldung
			{
				if (_frame - _stageStart == 1) { _m.TestMenu("new:test"); break; }
				if (_frame - _stageStart < 40) break;
				try
				{
					var uni = _m.Session.U;
					var pl = uni.Ships[uni.PlayerShip];
					Check(pl.ModelKey == "tinker" && pl.Has(SpaceSim.Core.Model.Capability.Salvage), "Testszenario: Schiff mit Bergungsgreifer und Waffe");
					Check(pl.Systems.Condition.Count > 0, "Testszenario: beschädigtes Modul");
					Check(_m.View.WreckCount >= 1, "Testszenario: Wrack in der Sektoransicht");
					Check(uni.Ships.Values.Any(s => SpaceSim.Core.Simulation.ThreatMap.IsRaider(s) && s.Sector == uni.PlayerSector), "Testszenario: Kaperschiff im Sektor");
					Check(_m.HudRef.Flight.TestNextEnemyVisible, "„Nächster Gegner“ erreichbar, wenn ein Gegner im Sektor ist");
					_m.TestSetFast(true);
					int slot = pl.Fit.FindIndex(k => k is not null && k.StartsWith("reactor"));
					SpaceSim.Core.Combat.ModuleDamage.EndFire(uni, pl, slot, "test");
					SpaceSim.Core.Combat.ModuleDamage.Ignite(uni, pl, slot);
					Check(!_m.TestFast, "Zeitraffer schaltet bei Brand auf ×1");
					Check(_m.HudRef.TestAlert.Contains("Zeitraffer aus"), $"wichtige Meldung sichtbar („{_m.HudRef.TestAlert}“)");
				}
				catch (System.Exception ex) { Fail("Testszenario warf eine Ausnahme: " + ex.GetType().Name + ": " + ex.Message); }
				Next(); break;
			}
			// ───────── Phase 27: Flottenkampf und Gerätetest-Befunde ─────────
			case 68: // Wisch-Rollen (Gerätetest): Startpaket-Liste in „System“, Finger- und Mauszug über eine Taste rollt und löst nichts aus
			{
				if (_frame - _stageStart == 1) { _m.TestOpenCommand(); _m.TestOpenCcArea("system"); _m.HudRef.CcSystem.TestChoosing = true; break; }
				if (_frame - _stageStart < 4) break;
				var scroll = _m.HudRef.CcSystem.TestChooserScroll;
				if (scroll is null) { Fail("Startpaket-Liste hat keinen Rollbereich"); break; }
				var rect = scroll.GetGlobalRect();
				if (_frame - _stageStart == 4) Log($"Rollbereich {rect}, rollbar {(scroll.GetVScrollBar().MaxValue - scroll.GetVScrollBar().Page):0}, mit Canvas {scroll.GetGlobalTransformWithCanvas().Origin}");
				if (_frame - _stageStart == 4)
				{
					// Runde 11: genau drei reguläre Startpakete (Jäger, Händler, Schürfer), kein „Standard“ in der Liste
					var regular = SpaceSim.Core.Content.WorldBuilder.DefaultStartOptions().Where(o => o.Scenario is null).Select(o => o.Key).ToList();
					Check(string.Join(",", regular) == "fighter,trader,miner", $"Startpakete regulär: {string.Join(",", regular)}");
					Check(!_m.HudRef.CcSystem.TestChooserTexts().Any(t => t.StartsWith("Standard")), "keine Karte „Standard“ in der Startauswahl");
					Check(_m.HudRef.CcSystem.TestChooserTexts().Any(t => t.StartsWith("Testszenarien")), "Testszenarien abgesetzt");
					_p27Universe = _m.Session.U;
					scroll.ScrollVertical = 0;
					var a = rect.Position + new Vector2(rect.Size.X * 0.4f, rect.Size.Y * 0.7f);
					Push(new InputEventScreenTouch { Index = 0, Position = a, Pressed = true });
					for (int i = 1; i <= 4; i++) Push(new InputEventScreenDrag { Index = 0, Position = a - new Vector2(0, 40 * i) });
					Push(new InputEventScreenTouch { Index = 0, Position = a - new Vector2(0, 160), Pressed = false });
					break;
				}
				if (_frame - _stageStart == 6)
				{
					Check(scroll.ScrollVertical > 60, $"Fingerzug rollt die Liste ({scroll.ScrollVertical} px)");
					scroll.ScrollVertical = 0;
					// Maus-Zug, Start auf einer „Starten“-Taste: rollt, die Taste löst beim Loslassen nicht aus
					var b = rect.Position + new Vector2(rect.Size.X - 90f, 50f);
					Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = b, GlobalPosition = b, Pressed = true, ButtonMask = MouseButtonMask.Left });
					for (int i = 1; i <= 4; i++) { var mp = b - new Vector2(0, 40 * i); Push(new InputEventMouseMotion { Position = mp, GlobalPosition = mp, ButtonMask = MouseButtonMask.Left, Relative = new Vector2(0, -40) }); }
					var e = b - new Vector2(0, 160);
					Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = e, GlobalPosition = e, Pressed = false });
					break;
				}
				if (_frame - _stageStart < 10) break;
				Check(scroll.ScrollVertical > 60, $"Mauszug über eine Taste rollt die Liste ({scroll.ScrollVertical} px)");
				Check(ReferenceEquals(_p27Universe, _m.Session.U), "Zug über „Starten“ startet kein neues Spiel");
				_p27Universe = null;   // Runde 17: Stufe 97 zählt die lebenden Universen
				// Speichern gibt sichtbare Rückmeldung im System-Bereich
				_m.TestMenu("save");
				Check(_m.HudRef.CcSystem.TestMessage.StartsWith("Gespeichert"), $"Speichern meldet sich im System-Bereich ({_m.HudRef.CcSystem.TestMessage})");
				_m.HudRef.CcSystem.TestChoosing = false;
				_m.TestCloseCommand();
				Next(); break;
			}
			case 69: // Polizeigefecht gegen den gesuchten Spieler: Vorwarnung, „Ergeben“ sichtbar, Gefecht, Ergeben zahlt die Strafe
			{
				var uni = _m.Session.U;
				var pl = uni.Ships[uni.PlayerShip];
				if (_frame - _stageStart == 1)
				{
					if (_m.Docked) _m.TestUndock();
					uni.Rules.PlayerRaids = true;
					uni.PendingEncounters.Clear();
					// Phase 30: Waffenstellungen im Sektor binden die Streife sonst an Abwehrgefechte gegen lauernde Kaperer (PoliceIn zählt Beteiligte nicht) — hier aus
					_p30Engage = uni.Rules.OutpostsEngage; uni.Rules.OutpostsEngage = false;
					foreach (var fe in uni.Fights.Encounters.Where(x => x.Sector == uni.PlayerSector).ToList())
						foreach (var part in fe.Participants.Where(x => x.Active).ToList()) SpaceSim.Core.Combat.Engage.Leave(uni, fe, part, SpaceSim.Core.Model.CombatExit.Fled);
					foreach (var r in uni.Ships.Values.Where(SpaceSim.Core.Simulation.ThreatMap.IsRaider).ToList())
						if (r.Sector == uni.PlayerSector && !uni.Fights.IsEngaged(r.Id)) { if (r.Transit is not null) uni.FinishTransit(r); uni.MoveShip(r, new SectorId(11)); }
					break;
				}
				if (_frame - _stageStart == 60)
				{
					var owner = uni.Sectors[uni.PlayerSector].Owner;
					_p27Faction = owner;
					SpaceSim.Core.Simulation.Wanted.Report(uni, uni.PlayerActor, owner, "assault");
					uni.PlayerWallet.Credits = 40_000f;
					var cop = SpaceSim.Core.Simulation.SecuritySystem.PoliceOf(uni, owner).First(c => !uni.Fights.IsEngaged(c.Id));
					if (cop.Command is not null) SpaceSim.Core.Simulation.Commands.Cancel(uni, cop);
					if (cop.Transit is not null) uni.FinishTransit(cop);
					if (cop.Sector != uni.PlayerSector) uni.MoveShip(cop, uni.PlayerSector);
					cop.Position = pl.Position + new Vec2(45f, 0f); cop.DockedAt = default;
					cop.DutySector = uni.PlayerSector; cop.Hull = cop.MaxHull;   // Phase 30: bleibt auf Streife hier (sonst teilt die Fraktion sie anderswo ein)
					_p27Police = cop.Id;
					_m.TestSetFast(true);
					break;
				}
				if (_frame - _stageStart < 60) break;
				if (uni.PendingEncounters.Any(x => x.Target == pl.Id) && !_p27Warned)
				{
					_p27Warned = true;
					Check(SpaceSim.Core.Combat.PlayerSurrender.Issue(uni) is null, "in der Vorwarnung ist Ergeben möglich");
				}
				var fight = uni.Fights.Of(pl.Id);
				if (fight is null) { if (_frame - _stageStart > 2400) Fail($"Polizeigefecht begann nicht (Sektor {uni.PlayerSector.Value}, angedockt {pl.DockedAt.Value}, Warnung {_p27Warned}, Meldung {_m.LastCombatMessage}, Lage {_m.LastSecurityMessage}, Streife {(uni.Ships.TryGetValue(_p27Police, out var pc) ? $"Sektor {pc.Sector.Value} Befehl {pc.Command?.GetType().Name} Gefecht {uni.Fights.Of(pc.Id)?.Cause} Transit {pc.Transit is not null} Hülle {pc.Hull:0}" : "weg")})"); break; }
				Check(fight.Cause == SpaceSim.Core.Model.EncounterCause.Arrest, $"Gefecht ist ein Polizeigefecht ({fight.Cause})");
				Check(_m.LastCombatMessage?.Contains("Polizei") == true, $"Meldung zum Polizeigefecht ({_m.LastCombatMessage})");
				_m.TestSetFast(false);
				_m.SyncHudForTest();
				Check(_m.HudRef.Flight.TestSurrenderVisible, "Taste „Ergeben“ im Gefecht sichtbar");
				var rr = _m.HudRef.Flight.TestSurrenderRect;
				Check(rr.Size.X >= 56f && rr.Size.Y >= 56f, "„Ergeben“ mindestens 56 px");
				_m.HudRef.Flight.TestPressSurrender();
				Check(!uni.Fights.IsEngaged(pl.Id), "nach dem Ergeben nicht mehr im Gefecht");
				Check(!uni.Player.Wanted.ContainsKey(_p27Faction), "Strafe bezahlt — Fahndung gelöscht");
				Check(uni.PlayerWallet.Credits < 40_000f, "Strafe aus der Kasse");
				Check(_m.HudRef.TestAlert.Contains("Ergeben"), $"Meldung „Ergeben“ ({_m.HudRef.TestAlert})");
				uni.Rules.OutpostsEngage = _p30Engage;
				Next(); break;
			}
			case 70: // Sektoransicht: Streifen-Verband als Linie, Flottenzeile in der Karte
			{
				var uni = _m.Session.U;
				if (_frame - _stageStart == 1)
				{
					var f = uni.Sectors[uni.PlayerSector].Owner;
					var cops = SpaceSim.Core.Simulation.SecuritySystem.PoliceOf(uni, f).Where(c => !uni.Fights.IsEngaged(c.Id)).Take(2).ToList();
					foreach (var c in cops)
					{
						if (c.Command is not null) SpaceSim.Core.Simulation.Commands.Cancel(uni, c);
						if (c.Transit is not null) uni.FinishTransit(c);
						if (c.Sector != uni.PlayerSector) uni.MoveShip(c, uni.PlayerSector);
						c.Position = uni.Ships[uni.PlayerShip].Position + new Vec2(60f, 20f * c.Id.Value % 40); c.DockedAt = default;
						c.DutySector = uni.PlayerSector;
						c.Hull = c.MaxHull;
					}
					SpaceSim.Core.Simulation.PoliceCommands.Organize(uni, f, SpaceSim.Core.Simulation.SecuritySystem.PoliceOf(uni, f));
					Check(cops.Count == 2 && cops[0].Squadron.Value != 0 && cops[0].Squadron == cops[1].Squadron, "zwei Streifen im selben Sektor bilden einen Verband");
					_m.TestOpenCommand(); _m.TestOpenCcArea("sector");
					break;
				}
				if (_frame - _stageStart < 5) break;
				Check(_m.HudRef.CcSector.TestFleet.Contains("Verb"), $"Sektorkarte zeigt den Verband ({_m.HudRef.CcSector.TestFleet})");
				Check(Ui.Screens.SectorScreen.TestFleetLinks > 0, "Verbandslinie gezeichnet");
				_m.TestCloseCommand();
				Next(); break;
			}
			case 71: // Gerätetest: Abdocken am Wasserwerk Alpha und sofort Vollschub — kein Schaden
			{
				var uni = _m.Session.U;
				if (_frame - _stageStart == 1)
				{
					var wp = uni.Stations.Values.First(s => s.Sector.Value == 1 && s.TypeKey == "water_plant");
					_m.TestDockAt(wp.Id);
					break;
				}
				if (_frame - _stageStart == 20) { Check(_m.Docked, "am Wasserwerk Alpha angedockt"); _p27Hull = uni.Ships[uni.PlayerShip].Hull; _p27Hits = _m.TestHits; _m.TestUndock(); break; }
				if (_frame - _stageStart == 70) { _m.TestSetSlider(1f); break; }
				if (_frame - _stageStart < 70 + 60 * 8) break;
				_m.TestSetSlider(0f);
				var hull = uni.Ships[uni.PlayerShip].Hull;
				Check(_m.TestHits == _p27Hits && hull >= _p27Hull - 0.01f, $"Abdocken mit Vollschub ohne Aufprall (Hülle {_p27Hull:0} → {hull:0}, Treffer {_m.TestHits - _p27Hits}; {_m.TestLastHit})");
				Next(); break;
			}
			case 72: // Station-Info: deutsche Dienstnamen; neues Spiel setzt die Sektoransicht zurück
			{
				var uni = _m.Session.U;
				if (_frame - _stageStart == 1)
				{
					var yardD = uni.Stations.Values.First(s => s.Name == "Werft Delta");
					_m.TestDockAt(yardD.Id);
					break;
				}
				if (_frame - _stageStart == 10) { _m.TestOpenDockArea(Ui.Screens.DockAreas.Info); break; }
				if (_frame - _stageStart == 14)
				{
					var info = (Ui.Screens.StationInfoScreen)_m.HudRef.Screens.Get("dock.info");
					Check(info.TestChips.Contains("Schiffshändler") && !info.TestChips.Contains("shipdealer") && !info.TestChips.Contains("rearm"), $"Dienst-Chips deutsch ({info.TestChips})");
					_m.TestUndock();
					break;
				}
				if (_frame - _stageStart == 40)
				{
					var other = uni.Sectors.Keys.First(k => k != uni.PlayerSector);
					_m.TestCcSelectSectorView(other);
					_m.TestMenu("new");
					break;
				}
				if (_frame - _stageStart < 50) break;
				Check(_m.HudRef.CcSector.SelectedSector is null, "neues Spiel: Sektoransicht zeigt wieder den eigenen Sektor");
				Next(); break;
			}
			case 73: // Phase 28: neues Spiel mit dem Jäger; Station-Info zeigt die Verteidigung einer Station mit Geschützen
			{
				if (_frame - _stageStart == 1) { _m.TestMenu("new:fighter"); break; }
				if (_frame - _stageStart < 40) break;
				var uni = _m.Session.U;
				if (_frame - _stageStart == 40)
				{
					Check(uni.Rules.Warfare, "Kriegsführung in der Standardwelt eingeschaltet");
					Check(uni.Wars.Count == 0, "Standardwelt: kein Krieg");
					_p28Events = new List<SpaceSim.Core.Events.SimEvent>();
					uni.Events.Raised += e => { if (e is SpaceSim.Core.Events.EncounterEnded or SpaceSim.Core.Events.CombatExited) Log("Ereignis " + e); if (e is SpaceSim.Core.Events.MissileLaunched or SpaceSim.Core.Events.MissileResolved or SpaceSim.Core.Events.StationShot or SpaceSim.Core.Events.ShotAtStation) _p28Events?.Add(e); };
					var st = uni.Stations.Values.Where(x => x.Sector == uni.PlayerSector && SpaceSim.Core.Combat.StationCombat.DefenseOf(uni, x) is { Guns.Count: > 0 }).OrderBy(x => x.Id.Value).First();
					_p28Station = st.Id;
					_m.TestDockAt(st.Id);
					break;
				}
				if (_frame - _stageStart == 50) { _m.TestOpenDockArea(Ui.Screens.DockAreas.Info); break; }
				if (_frame - _stageStart < 54) break;
				var info = (Ui.Screens.StationInfoScreen)_m.HudRef.Screens.Get("dock.info");
				Check(info.TestDefense.Contains("Hülle") && info.TestDefense.Contains("Geschütz"), $"Station-Info zeigt die Verteidigung ({info.TestDefense})");
				_m.TestUndock();
				Next(); break;
			}
			case 74: // Phase 28: Rakete des Spielers — Werfer statt Geschütz, Ziel Kaperschiff, Rakete fliegt sichtbar und wird aufgelöst
			{
				var uni = _m.Session.U;
				var pl = uni.Ships[uni.PlayerShip];
				if (_frame - _stageStart == 30)
				{
					int slot = pl.Fit.IndexOf("weapon_massdriver_s");
					pl.Fit[slot] = "missile_launcher_s";
					pl.RecomputeStats(uni);
					var raider = uni.Ships.Values.Where(SpaceSim.Core.Simulation.ThreatMap.IsRaider).OrderBy(r => r.Id.Value).First(r => !uni.Fights.IsEngaged(r.Id));
					if (raider.Command is not null) SpaceSim.Core.Simulation.Commands.Cancel(uni, raider);
					if (raider.Transit is not null) uni.FinishTransit(raider);
					if (raider.Sector != uni.PlayerSector) uni.MoveShip(raider, uni.PlayerSector);
					var away = uni.Stations[_p28Station].Position + new Vec2(0f, 160f);
					_m.TestTeleportTo(away);
					raider.Position = away + new Vec2(0f, -45f); raider.DockedAt = default;
					_p28Raider = raider.Id;
					_m.TestFace(raider.Position);
					_m.TestSetShipTarget(raider.Id);
					break;
				}
				if (_frame - _stageStart < 34) break;
				if (_frame - _stageStart == 34)
				{
					_m.TestFace(uni.Ships[_p28Raider].Position);
					Log($"vor dem Schuss: angedockt {pl.DockedAt.Value}, Reise {pl.Transit is not null}, Sektor {pl.Sector.Value}/{uni.PlayerSector.Value}, Abstand {Vec2.Distance(pl.Position, uni.Ships[_p28Raider].Position):0}, Ansicht angedockt {_m.Docked}");
					bool fired = _m.TestFireSlot(pl.Fit.IndexOf("missile_launcher_s"));
					Check(fired, $"Raketenwerfer feuert ({_m.TestFightMessage})");
					break;
				}
				if (_frame - _stageStart < 40) break;
				if (_frame - _stageStart == 40)
				{
					Check(_p28Events!.Any(e => e is SpaceSim.Core.Events.MissileLaunched ml && ml.Shooter == uni.PlayerShip), "Spieler hat eine Rakete abgefeuert");
					Check(pl.Systems.MissilesMissing >= 1f, $"Raketenmagazin verbraucht ({pl.Systems.MissilesMissing})");
					_p28SeenMissile = _m.TestMissilesShown;
					var fe = uni.Fights.Of(pl.Id);
					Log($"Gefecht {(fe is null ? "-" : $"{fe.Cause} räumlich {fe.Spatial} Raketen {fe.Missiles.Count}")}, gezeigt {_m.TestMissilesShown}");
					break;
				}
				if (_frame - _stageStart < 320) { _p28SeenMissile = System.Math.Max(_p28SeenMissile, _m.TestMissilesShown); break; }
				Check(_p28SeenMissile > 0, "Rakete im Flug gezeichnet");
				Check(_p28Events!.Any(e => e is SpaceSim.Core.Events.MissileResolved mr && mr.Shooter == uni.PlayerShip), "Rakete am Ziel aufgelöst");
				Next(); break;
			}
			case 75: // Phase 28: Station angreifen — Taste nur für eine gewählte Station, zwei Tipps, Belagerung, die Station schießt zurück, Fahndung
			{
				var uni = _m.Session.U;
				var pl = uni.Ships[uni.PlayerShip];
				var st = uni.Stations[_p28Station];
				if (_frame - _stageStart == 1)
				{
					if (uni.Fights.Of(pl.Id) is { } old && old.Of(pl.Id) is { Active: true } op) SpaceSim.Core.Combat.Engage.Leave(uni, old, op, SpaceSim.Core.Model.CombatExit.Fled);
					foreach (var r in uni.Ships.Values.Where(SpaceSim.Core.Simulation.ThreatMap.IsRaider).ToList())
						if (r.Sector == uni.PlayerSector && !uni.Fights.IsEngaged(r.Id)) { if (r.Transit is not null) uni.FinishTransit(r); uni.MoveShip(r, new SectorId(11)); }
					pl.Hull = pl.MaxHull;
					_m.TestTeleportTo(st.Position + new Vec2(0f, 40f));
					_m.TestFace(st.Position);
					_m.TestSetShipTarget(default);
					_m.TestSetStationTarget(st.Id);
					break;
				}
				if (_frame - _stageStart == 6)
				{
					_m.SyncHudForTest();
					Check(_m.HudRef.Flight.TestStationAttackVisible, "Taste „Station angreifen“ sichtbar bei gewählter Station");
					var r1 = _m.HudRef.Flight.TestNextEnemyRect;
					Check(r1.Size.Y >= 56f, "Kampftasten mindestens 56 px");
					_m.TestArmStation();
					Check(_m.TestStationTarget == st.Id, "Station als Angriffsziel scharf");
					_m.TestFace(st.Position);
					_m.SyncHudForTest();
					_m.TestAllFire();
					break;
				}
				if (_frame - _stageStart < 120) break;
				Check(_p28Events!.Any(e => e is SpaceSim.Core.Events.ShotAtStation sa && sa.Station == st.Id), "Schüsse auf die Station");
				Check(uni.Fights.Of(pl.Id) is { Cause: SpaceSim.Core.Model.EncounterCause.Siege } || _p28Events!.Any(e => e is SpaceSim.Core.Events.StationShot ss && ss.Target == pl.Id), "Belagerung läuft");
				Check(_p28Events!.Any(e => e is SpaceSim.Core.Events.StationShot ss && ss.Target == pl.Id), "die Station schießt zurück");
				var f = uni.FactionOf(st)!.Value;
				Check(SpaceSim.Core.Simulation.Wanted.LevelOf(uni, pl.Owner, f) >= 3, "Angriff auf die Station ist ein schweres Verbrechen");
				_m.SyncHudForTest();
				Check(!_m.HudRef.Flight.TestStationAttackVisible, "Taste verschwindet, solange der Angriff scharf ist");
				// Flucht: Sektor verlassen beendet die Belagerung
				var other = uni.Sectors.Keys.First(k => k != uni.PlayerSector);
				_m.TestEnterSector(other);
				Next(); break;
			}
			case 76: // Prüfung Phase 27: große Meldung verdeckt weder „Nächster Gegner“ noch die Zielkarte; Krieg in der Anzeige
			{
				var uni = _m.Session.U;
				if (_frame - _stageStart == 1)
				{
					_m.HudRef.Alert("Prüfmeldung: Polizei von Gilde greift an — „Ergeben“ zahlt die Strafe, sonst kämpfen oder fliehen.", 6f);
					break;
				}
				if (_frame - _stageStart < 4) break;
				if (_frame - _stageStart == 4)
				{
					var a = _m.HudRef.TestAlertRect;
					Check(!a.Intersects(_m.HudRef.Flight.TestNextEnemyRect), $"Meldung verdeckt „Nächster Gegner“ nicht ({a})");
					// zweiter Gerätetest Phase 28: die Meldung sitzt oben mittig unter Sektorchip/Lagezeile und verdeckt keine Taste des Flug-HUD
					var under = Buttons(_m.HudRef.Flight).Where(b => b.IsVisibleInTree() && b.GetGlobalRect().Intersects(a)).Select(b => b.Text).ToList();
					// Runde 12 (Handy-Test): unten mittig über dem Tempo-Regler — die Bildmitte vor dem Bug (dort steht der Gegner) bleibt frei
					bool dockMenuOpen = _m.HudRef.Screens.Current?.Id == "dock.menu";   // über dem Dock-Menü steht sie links neben dem Fenster (eigene Prüfung unten)
					Check(under.Count == 0 && (dockMenuOpen || (a.Position.Y > 400f && !a.Intersects(_m.HudRef.Flight.TestHitWarningRect))), $"Meldung unten mittig, verdeckt keine Flug-Taste ({a}; {string.Join(",", under)}; Bildschirm {_m.HudRef.Screens.Current?.Id ?? "Flug"})");
					_m.HudRef.HideAlert();
					var war = SpaceSim.Core.Simulation.Wars.Declare(uni, new FactionId(1), new FactionId(2));
					_p28WarAlert = _m.HudRef.TestAlert;
					Check(_m.LastWarMessage.Contains("Krieg"), $"Meldung zur Kriegserklärung ({_m.LastWarMessage})");
					_m.TestOpenCommand(); _m.TestOpenCcArea("factions"); _m.TestCcSelectFaction(new FactionId(1));
					break;
				}
				if (_frame - _stageStart < 8) break;
				Check(_m.HudRef.CcFactions.TestWar.StartsWith("Krieg mit"), $"Fraktionsdetail zeigt den Krieg ({_m.HudRef.CcFactions.TestWar})");
				_m.TestCloseCommand();
				SpaceSim.Core.Simulation.Wars.MakePeace(uni, uni.Wars[0], "manual");
				Check(uni.Wars.Count == 0, "Frieden geschlossen");
				Next(); break;
			}
			case 77: // Gerätetest Phase 27 (Punkt 4): Zeitraffer ×10 + Autopilot über mehrere Sprünge — Sektorchip folgt dem Sektor, die Ansicht hängt nicht
			{
				var uni = _m.Session.U;
				if (_frame - _stageStart == 1)
				{
					if (_m.Docked) _m.TestUndock();
					break;
				}
				if (_frame - _stageStart == 60)
				{
					var target = new SectorId(11);   // Abgrund (Sackgasse hinter Delta)
					_p28Hops = uni.FindRoute(uni.PlayerSector, target)?.Count ?? -1;
					_m.TestOpenCommand(); _m.TestOpenCcArea("universe"); _m.TestCcSelectSector(target);
					_m.TestCcPlanRoute();
					Check(_m.HudRef.Screens.Current == null && _m.HudRef.Flight.Visible, "„Route planen“ führt zurück in die Flugansicht (Rückmeldung sichtbar)");
					_m.TestSetFast(true);
					_p28LastSector = uni.PlayerSector; _p28SectorFrame = _frame; _p28Jumps = 0;
					break;
				}
				if (_frame - _stageStart < 60) break;
				_m.SyncHudForTest();
				if (uni.PlayerSector != _p28LastSector) { _p28LastSector = uni.PlayerSector; _p28SectorFrame = _frame; _p28Jumps++; }
				string want = uni.Sectors[uni.PlayerSector].Name;
				if (_m.HudRef.Flight.TestSectorChip != want && _frame - _p28SectorFrame > 90)
				{ Fail($"Sektorchip hängt („{_m.HudRef.Flight.TestSectorChip}“, Sektor {want}, seit {_frame - _p28SectorFrame} Bildern)"); break; }
				if (uni.PlayerSector.Value == 11 || _frame - _stageStart > 60 + 60 * 90)
				{
					_m.TestSetFast(false);
					Log($"Zeitraffer-Autopilot: {_p28Jumps} Sprünge (Route {_p28Hops}), jetzt {want}, Bilder {_frame - _stageStart - 60}");
					Check(_p28Jumps >= 1, "Autopilot ist im Zeitraffer mindestens einmal gesprungen");
					Next();
				}
				break;
			}
			case 78: // Gerätetest Phase 27 (Punkt 1): Werftliste — Rollposition bleibt über Neuaufbauten mit geänderten Daten; Finger auf der Liste hält den Neuaufbau an
			{
				var uni = _m.Session.U;
				if (_frame - _stageStart == 1)
				{
					var yardS = uni.Stations.Values.Where(x => x.TypeKey == "shipyard").OrderBy(x => x.Id.Value).First();
					_m.TestDockAt(yardS.Id);
					break;
				}
				if (_frame - _stageStart == 30) { _m.TestOpenDockArea("shipyard"); break; }
				if (_frame - _stageStart < 36) break;
				var screen = _m.HudRef.Screens.Get("dock.shipyard");
				var scroll = FindScroll(screen.Node, "ShipyardScreen.cs");
				if (scroll is null) { Fail("Werftliste hat keinen Rollbereich"); break; }
				if (_frame - _stageStart == 36) { scroll.ScrollVertical = 300; break; }
				if (_frame - _stageStart == 40)
				{
					_p28Scroll = scroll.ScrollVertical;
					Check(_p28Scroll > 100, $"Werftliste gerollt ({_p28Scroll})");
					// Daten ändern sich (Kasse, Lager), mehrere Ticks → Neuaufbauten
					uni.PlayerWallet.Credits += 1234f;
					_m.Session.RunTicks(3);
					screen.Refresh();
					break;
				}
				if (_frame - _stageStart == 44) { _m.Session.RunTicks(2); screen.Refresh(); break; }
				if (_frame - _stageStart == 50)
				{
					int now = SpaceAdventure.Ui.Kit.KitScroll.PositionOf(screen.Node, "ShipyardScreen.cs");
					Check(System.Math.Abs(now - _p28Scroll) <= 2, $"Rollposition bleibt über Neuaufbauten ({_p28Scroll} → {now})");
					// Finger aufgelegt (noch kein Zug): kein Neuaufbau, der Bereich bleibt derselbe
					var r = scroll.GetGlobalRect();
					var a = r.Position + new Vector2(r.Size.X * 0.5f, r.Size.Y * 0.5f);
					Push(new InputEventScreenTouch { Index = 0, Position = a, Pressed = true });
					_p28ScrollNode = FindScroll(screen.Node, "ShipyardScreen.cs");
					_m.Session.RunTicks(2); screen.Refresh();
					Check(ReferenceEquals(_p28ScrollNode, FindScroll(screen.Node, "ShipyardScreen.cs")), "kein Neuaufbau, solange ein Finger auf der Liste liegt");
					for (int i = 1; i <= 3; i++) Push(new InputEventScreenDrag { Index = 0, Position = a - new Vector2(0, 30 * i) });
					Push(new InputEventScreenTouch { Index = 0, Position = a - new Vector2(0, 90), Pressed = false });
					break;
				}
				if (_frame - _stageStart < 56) break;
				Check(SpaceAdventure.Ui.Kit.KitScroll.PositionOf(screen.Node, "ShipyardScreen.cs") > _p28Scroll + 40, "erster Wisch nach dem Aufsetzen rollt");
				_m.TestUndock();
				// Hamburger „Neues Spiel“: führt zur Startpaket-Auswahl, startet nicht sofort
				var before = _m.Session.U;
				_m.TestCommand("new");
				Check(ReferenceEquals(before, _m.Session.U) && _m.HudRef.CcSystem.TestChoosing, "„Neues Spiel“ im Menü öffnet die Startpaket-Auswahl");
				_m.HudRef.CcSystem.TestChoosing = false;
				_m.TestCloseCommand();
				Next(); break;
			}
			case 79: // Gerätetest Phase 28: Autopilot zum Hauptquartier der Union (Alpha) von mehreren Startpunkten kommt an und dockt (vorher kreiste er zwischen Asteroiden)
			{
				var uni = _m.Session.U;
				var hq = uni.Stations.Values.First(x => x.TypeKey == "headquarters" && x.Sector.Value == 1);   // Phase 29: Name mit gewürfeltem Fraktionsnamen
				int k = _p28Try;
				if (_frame - _stageStart == 1)
				{
					if (_m.Docked) _m.TestUndock();
					break;
				}
				if (_frame - _stageStart == 40 + k * 3000)
				{
					if (uni.PlayerSector != hq.Sector) _m.TestEnterSector(hq.Sector);
					var start = hq.Position + Vec2.FromAngle(k * 1.9f + 0.4f) * 230f;
					_m.TestTeleportTo(SpaceSim.Core.Combat.CombatSpace.Clear(uni, hq.Sector, start));
					_m.TestSetStationTarget(hq.Id);
					break;
				}
				if (_frame - _stageStart < 40 + k * 3000) break;
				if (_m.Docked)
				{
					Log($"Autopilot Start {k}: angedockt nach {(_frame - _stageStart - 40 - k * 3000) / 60f:0.0} s");
					_m.TestUndock();
					_p28Try++;
					if (_p28Try >= 4) { Check(true, "Autopilot zum HQ Alpha kommt von vier Startpunkten an"); Next(); }
					else _stageStart = _frame - 40 - _p28Try * 3000 + 60;   // nächster Start nach dem Abdocken
					break;
				}
				if (_frame - _stageStart > 40 + k * 3000 + 60 * 45)
				{
					var pl = uni.Ships[uni.PlayerShip];
					Fail($"Autopilot zum HQ Alpha kam von Start {k} nicht an (Abstand {Vec2.Distance(pl.Position, hq.Position):0})");
				}
				break;
			}
			case 80: // Gerätetest Phase 28: „Ergeben“ im laufenden Überfall-Gefecht sichtbar und wirksam (mit Fracht); ohne Fracht sichtbar, deaktiviert
			{
				if (_frame - _stageStart == 1) { _m.TestMenu("new:test"); break; }
				if (_frame - _stageStart < 40) break;
				var uni = _m.Session.U;
				var pl = uni.Ships[uni.PlayerShip];
				if (_frame - _stageStart == 40)
				{
					if (_m.Docked) _m.TestUndock();
					break;
				}
				if (_frame - _stageStart == 90)
				{
					var raider = uni.Ships.Values.Where(SpaceSim.Core.Simulation.ThreatMap.IsRaider).OrderBy(r => r.Id.Value).First(r => !uni.Fights.IsEngaged(r.Id));
					if (raider.Command is not null) SpaceSim.Core.Simulation.Commands.Cancel(uni, raider);
					if (raider.Transit is not null) uni.FinishTransit(raider);
					if (raider.Sector != uni.PlayerSector) uni.MoveShip(raider, uni.PlayerSector);
					var away = pl.Position + new Vec2(0f, -150f);
					_m.TestTeleportTo(SpaceSim.Core.Combat.CombatSpace.Clear(uni, uni.PlayerSector, away));
					raider.Position = pl.Position + new Vec2(0f, -30f); raider.DockedAt = default;
					pl.Cargo[uni.FindGood("food")!.Id] = 10f;
					SpaceSim.Core.Combat.Engage.StartRaid(uni, raider, pl, uni.Sectors[uni.PlayerSector]);
					_m.SyncHudForTest();
					Check(_m.HudRef.Flight.TestSurrenderVisible && _m.HudRef.Flight.TestSurrenderEnabled, "„Ergeben“ im Überfall-Gefecht sichtbar und aktiv");
					_m.HudRef.Flight.TestPressSurrender();
					Check(!uni.Fights.IsEngaged(pl.Id), "Ergeben gegenüber Kaperern beendet das Gefecht");
					// ohne Fracht: Taste sichtbar, deaktiviert mit Grund
					pl.Cargo.Clear();
					var raider2 = uni.Ships.Values.Where(SpaceSim.Core.Simulation.ThreatMap.IsRaider).OrderBy(r => r.Id.Value).First(r => !uni.Fights.IsEngaged(r.Id) && r.Id != raider.Id);
					if (raider2.Command is not null) SpaceSim.Core.Simulation.Commands.Cancel(uni, raider2);
					if (raider2.Transit is not null) uni.FinishTransit(raider2);
					if (raider2.Sector != uni.PlayerSector) uni.MoveShip(raider2, uni.PlayerSector);
					raider2.Position = pl.Position + new Vec2(0f, -30f); raider2.DockedAt = default;
					SpaceSim.Core.Combat.Engage.StartRaid(uni, raider2, pl, uni.Sectors[uni.PlayerSector]);
					_m.SyncHudForTest();
					Check(_m.HudRef.Flight.TestSurrenderVisible && !_m.HudRef.Flight.TestSurrenderEnabled, "ohne Fracht: „Ergeben“ sichtbar, deaktiviert");
					if (uni.Fights.Of(pl.Id) is { } e2 && e2.Of(pl.Id) is { Active: true } pp) SpaceSim.Core.Combat.Engage.Leave(uni, e2, pp, SpaceSim.Core.Model.CombatExit.Fled);
					Next();
				}
				break;
			}
			case 81: // Zweiter Gerätetest Phase 28: wichtige Meldung über dem Dock-Menü fängt den Tipp selbst ab; Station-Info-Karte im Rahmen; Nomad ohne Laserschacht
			{
				var uni = _m.Session.U;
				int f = _frame - _stageStart;
				if (f == 1) { _m.TestMenu("new:trader"); break; }
				if (f == 20)
				{
					Check(!_m.TestBayKeys.Contains("laser"), $"Nomad (ohne Bergbaulaser) zeigt keinen Laserschacht ({string.Join(",", _m.TestBayKeys)})");
					var hq = uni.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
					_m.TestDockAt(hq.Id);
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Info);
					break;
				}
				if (f == 26)
				{
					var (card, content, scrolls) = _m.HudRef.DockInfo.TestInfoCard;
					Check(card.Size.Y > 0f && card.Encloses(content) && !_m.HudRef.DockInfo.TestCardsScroll, $"Station-Info: Karten passen in ihren Rahmen, nichts angeschnitten ({card} / {content})");
					_m.TestCloseDockArea();
					break;
				}
				if (f == 30)
				{
					_m.HudRef.Alert("Prüfmeldung: Schiff zerstört (Test) — bleibt, bis „✕“ oder ein Tipp sie schließt.", 10f, sticky: true);
					break;
				}
				if (f == 32)
				{
					var hud = _m.HudRef;
					var a = hud.TestAlertRect;
					Check(hud.TestAlert.Length > 0 && hud.TestAlertSticky, "stehende Meldung sichtbar");
					Check(_m.TestScreenOpen("dock.menu"), "Dock-Menü offen unter der Meldung");
					// Gerätetest Phase 28: über dem Dock-Menü links neben dem Fenster (oben verdeckte sie den Titel)
					float winLeft = (_m.GetViewport().GetVisibleRect().Size.X - 580f) / 2f;
					Check(a.End.X <= winLeft + 1f && a.Size.Y <= 160f, $"Meldung links neben dem Dock-Menü, verdeckt den Titel nicht ({a})");
					// keine sichtbare Taste des Dock-Menüs unter der Meldung
					var hidden = Buttons(hud.DockMenu).Where(b => b.IsVisibleInTree() && b.GetGlobalRect().Intersects(a)).Select(b => b.Text).ToList();
					Check(hidden.Count == 0, $"Meldung verdeckt keine Taste des Dock-Menüs ({string.Join(",", hidden)})");
					// eine Taste genau unter der Meldung (gleiche Ebene wie die Dock-Bildschirme) darf beim Tipp nicht auslösen
					_p28Probe = new Ui.Kit.KitButton("Sonde", a.Size) { Position = a.Position - hud.DockMenu.GetGlobalRect().Position };
					_p28ProbeHits = 0;
					_p28Probe.Pressed += () => _p28ProbeHits++;
					hud.DockMenu.AddChild(_p28Probe);
					break;
				}
				if (f == 34) { Tap(_m.HudRef.TestAlertCenter, true); break; }
				if (f == 35) { Tap(_m.HudRef.TestAlertCenter, false); break; }
				if (f == 37)
				{
					Check(_m.HudRef.TestAlert.Length == 0, "Tipp auf die Meldung schließt sie");
					Check(_p28ProbeHits == 0, "der Tipp löst darunter nichts aus");
					Check(_m.TestScreenOpen("dock.menu"), "Dock-Menü bleibt offen");
					_p28Probe?.QueueFree(); _p28Probe = null;
					_m.HudRef.Alert("Prüfmeldung 2", 8f, sticky: true);
					break;
				}
				if (f == 38) { Tap(_m.HudRef.TestAlertCloseCenter, true); break; }
				if (f == 39) { Tap(_m.HudRef.TestAlertCloseCenter, false); break; }
				if (f == 41)
				{
					Check(_m.HudRef.TestAlert.Length == 0, "„✕“ schließt die Meldung");
					_m.HudRef.Alert("Prüfmeldung 3", 8f, sticky: true);
					_m.HudRef.Alert("gewöhnliche Meldung", 6f);
					Check(_m.HudRef.TestAlert.StartsWith("Prüfmeldung 3"), "eine gewöhnliche Meldung verdrängt die stehende nicht");
					_m.TestMenu("new");
					break;
				}
				if (f == 43) { Check(_m.HudRef.TestAlert.Length == 0, "nach „Neues Spiel“ keine Meldung"); Next(); }
				break;
			}
			case 82: // Zweiter Gerätetest Phase 28: Handel „Max“ und „+“ über simulierte Tipps, während Ticks laufen und die Liste neu aufbauen will
			{
				var uni = _m.Session.U;
				int f = _frame - _stageStart;
				var trade = _m.HudRef.DockTrade;
				var energy = uni.FindGood("energy")!.Id;
				if (f == 1)
				{
					var ww = uni.Stations.Values.First(x => x.Name == "Wasserwerk Alpha");
					_m.Session.RunTicks(2);   // neue Welt: Preise gibt es erst nach dem ersten Takt
					_m.TestDockAt(ww.Id);
					uni.WalletOf(uni.Ships[uni.PlayerShip]).Credits = 100000f;   // Menge begrenzt der Frachtraum (das Angebot bleibt über die Ticks gleich)
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
					break;
				}
				if (f == 3) { trade.TestExpand(energy); break; }
				// Max: drücken — Ticks laufen, die Liste will neu aufbauen — loslassen
				if (f == 6) { _p28Max = trade.TestBuyAmount(energy); PressButton(trade, "Max", true); break; }
				if (f == 7) { _m.Session.RunTicks(2); trade.Refresh(); break; }
				if (f == 8) { PressButton(trade, "Max", false); break; }
				if (f == 9) { _m.Session.RunTicks(2); trade.Refresh(); break; }
				if (f == 11)
				{
					float max = _p28Max;   // Angebot beim Drücken (zwischen Drücken und Loslassen baut die Liste nicht neu auf)
					Check(max > 1f, $"Energie kaufbar (Angebot {max})");
					Check(trade.TestQty == max && trade.TestEditorQtyText == $"{max:0.#}", $"„Max“ setzt die Menge auf das Angebot ({trade.TestQty} / „{trade.TestEditorQtyText}“ statt {max})");
					break;
				}
				// zweimal „−“, einmal „+“ — jeder Tipp mit Ticks und Neuaufbau-Versuch zwischen Drücken und Loslassen
				if (f >= 12 && f < 24)
				{
					int k = (f - 12) / 4, ph = (f - 12) % 4;
					string label = k < 2 ? "−" : "+";
					if (ph == 0) PressButton(trade, label, true);
					else if (ph == 1) { _m.Session.RunTicks(2); trade.Refresh(); }
					else if (ph == 2) PressButton(trade, label, false);
					else { _m.Session.RunTicks(1); trade.Refresh(); }
					break;
				}
				if (f == 25)
				{
					Check(trade.TestQty == _p28Max - 1f && trade.TestEditorQtyText == $"{_p28Max - 1f:0.#}", $"„−“, „−“, „+“ wirken je Tipp ({trade.TestQty} / „{trade.TestEditorQtyText}“, erwartet {_p28Max - 1f})");
					_p28Cargo = uni.Ships[uni.PlayerShip].Cargo.GetValueOrDefault(energy);
					_p28Buyable = SpaceSim.Core.Trading.TradeActions.MaxBuyable(uni, uni.Ships[uni.PlayerShip], uni.Stations[_m.HudRef.DockCtx.Station.Id], energy);
					PressButton(trade, "Kaufen", true);
					break;
				}
				if (f == 26) { trade.Refresh(); break; }
				if (f == 27) { PressButton(trade, "Kaufen", false); break; }
				if (f == 29)
				{
					float got = uni.Ships[uni.PlayerShip].Cargo.GetValueOrDefault(energy) - _p28Cargo;
					float want = System.MathF.Min(_p28Max - 1f, _p28Buyable);   // die Station kann inzwischen weniger haben (Ticks liefen)
					Check(System.MathF.Abs(got - want) < 0.01f, $"„Kaufen“ kauft die gewählte Menge ({got} statt {want})");
					// Liste: letzte Zeile aufklappen → ganz sichtbar gerollt; über Neuaufbauten bleibt die Position
					trade.TestOpenRow(trade.TestLastGood);
					break;
				}
				if (f == 32)
				{
					var sc = trade.TestScroll;
					var r = trade.TestExpandedRect;
					Check(sc != null && r.Size.Y > 0f && sc.GetGlobalRect().Grow(1f).Encloses(r), $"aufgeklappte letzte Zeile ganz sichtbar ({r} in {sc?.GetGlobalRect()})");
					_p28Scroll = sc?.ScrollVertical ?? -1;
					break;
				}
				if (f > 32 && f < 40) { _m.Session.RunTicks(1); trade.Refresh(); break; }
				if (f == 41)
				{
					int now = trade.TestScroll?.ScrollVertical ?? -2;
					Check(now == _p28Scroll && _p28Scroll > 0, $"Rollposition hält über Neuaufbauten mit offenem Editor ({_p28Scroll} → {now})");
					_m.TestCloseDockArea();
					_m.TestUndock();
					Next();
				}
				break;
			}
			case 83: // Zweiter Gerätetest Phase 28: Gefechtstest bestehbar — nur „Nächster Gegner“ + „Dauerfeuer“ (Nase aufs Ziel wie mit den Wendetasten)
			{
				var uni = _m.Session.U;
				int f = _frame - _stageStart;
				if (f == 120) { _m.TestMenu("new:combat"); break; }   // erst nach dem Abdock-Übergang der vorigen Stufe
				if (f < 125) break;
				if (f == 122)
				{
					// Prüfung Runde 2: „Nächster Gegner“ aktiv genau dann, wenn ein Gegner im Sektor ist (sonst gesperrt „kein Gegner“)
					_m.SyncHudForTest();
					var me0 = uni.Ships[uni.PlayerShip];
					bool any = SpaceSim.Core.Combat.PlayerCombat.Enemies(uni, me0).Count > 0;
					Check(!_m.HudRef.Flight.TestNextEnemyVisible || _m.HudRef.Flight.TestNextEnemyEnabled == any, $"„Nächster Gegner“ aktiv nur mit Gegner im Sektor (Gegner {any})");
					break;
				}
				if (f == 125)
				{
					var pend = uni.PendingEncounters.Where(e => e.Kind == EncounterKind.Raid && e.Target == uni.PlayerShip).OrderBy(e => e.DueTick).ToList();
					Check(pend.Count == 2, $"zwei angekündigte Überfälle ({pend.Count})");
					if (pend.Count == 0) { Next(); break; }
					_p28Raider = pend[0].Source;
					_p25Falcon = uni.PlayerShip;
					_p28SeenMissile = 0;
					var lead = uni.Ships[_p28Raider];
					var pl = uni.Ships[uni.PlayerShip];
					float rel = SpaceSim.Core.Weapons.FiringArc.Normalize180(SpaceSim.Core.Weapons.FiringArc.Bearing(lead.Position - pl.Position) - _m.TestHeadingDeg);
					Check(System.MathF.Abs(rel) < 3f, $"Nase zeigt auf den ersten Kaperer ({rel:0.0}°)");
					Check(_m.HudRef.TestAlert.Contains("Piraten"), $"Überfallwarnung sichtbar („{_m.HudRef.TestAlert}“)");
					_m.TestFight.AutoFire = true;
					break;
				}
				bool alive = uni.PlayerShip == _p25Falcon && uni.Ships.ContainsKey(_p25Falcon);
				if (!alive) { Fail("Gefechtstest: Falke verloren"); Next(); break; }
				var me = uni.Ships[uni.PlayerShip];
				if (uni.Fights.IsEngaged(me.Id))
				{
					if (_m.TestFight.Target.Value == 0 || !uni.Ships.ContainsKey(_m.TestFight.Target)) _m.TestNextEnemy();
					if (SpaceSim.Core.Combat.PlayerCombat.PositionOf(uni, _m.TestFight.Target) is { } tp) _m.TestFace(tp.Pos);
					if (_p28SeenMissile == 0) { _p28SeenMissile = 1; _m.SyncHudForTest(); Check(_m.HudRef.Flight.TestSurrenderVisible && _m.HudRef.Flight.TestSurrenderEnabled, "im Gefecht „Ergeben“ sichtbar und aktiv (Fracht an Bord)"); }
				}
				if (!uni.Ships.ContainsKey(_p28Raider))
				{
					_m.Session.RunTicks(2);   // das Gefecht wertet im nächsten Takt aus (Kopfgeld)
					Check(uni.Actors[uni.PlayerActor].BountyClaims.Values.Sum() > 0f, $"Kopfgeld für den ersten Kaperer ({_m.LastSecurityMessage})");
					Check(me.Hull > 0f, "Falke lebt");
					_m.TestFight.AutoFire = false;
					_p28SeenMissile = 0;
					Next(); break;
				}
				if (f > 60 * 60)
				{
					var lr = uni.Ships[_p28Raider];
					Fail($"Gefechtstest: erster Kaperer nach 60 s nicht abgeschossen (Kaperer {lr.Hull:0}, Abstand {Vec2.Distance(lr.Position, me.Position):0}, im Gefecht {uni.Fights.IsEngaged(me.Id)}/{uni.Fights.IsEngaged(lr.Id)}, Ziel {_m.TestFight.Target.Value}, Schüsse {uni.Fights.Of(me.Id)?.Shots}, Spieler {me.Hull:0}, {_m.TestFightMessage})");
					_m.TestFight.AutoFire = false; Next();
				}
				break;
			}
			case 84: // Gerätetest Phase 28: Gefechtstest nur mit „Dauerfeuer“ und der echten Zielhilfe (Drehrate des Schiffs, kein TestFace) — Ziel automatisch gewählt
			{
				var uni = _m.Session.U;
				int f = _frame - _stageStart;
				if (f == 1) { _m.TestMenu("new:combat"); break; }
				if (f < 6) break;
				if (f == 6)
				{
					_p28Raider = uni.PendingEncounters.Where(e => e.Kind == EncounterKind.Raid && e.Target == uni.PlayerShip).OrderBy(e => e.DueTick).First().Source;
					_p25Falcon = uni.PlayerShip;
					_m.TestAimAssist = true;
					_p28SeenMissile = 0;
					break;
				}
				if (uni.PlayerShip != _p25Falcon || !uni.Ships.ContainsKey(_p25Falcon)) { Fail("Gefechtstest mit Zielhilfe: Falke verloren"); Next(); break; }
				var me = uni.Ships[uni.PlayerShip];
				if (uni.Fights.IsEngaged(me.Id) && _p28SeenMissile == 0)
				{
					_p28SeenMissile = 1;
					_m.SyncHudForTest();
					Check(_m.TestFight.Target == _p28Raider, $"Überfall: das angreifende Schiff ist automatisch Ziel ({_m.TestFight.Target.Value})");
					Check((_m.LastCombatMessage ?? "").Contains("Ziel gewählt"), $"Meldung nennt das Ziel und Dauerfeuer ({_m.LastCombatMessage})");
					Check(_m.HudRef.Flight.TestAimAssistVisible && _m.HudRef.Flight.TestAimAssistOn, "Taste „Ziel folgen“ sichtbar und an");
					_m.HudRef.Flight.TestPressAutoFire();   // nur Dauerfeuer — gewendet wird über die Zielhilfe
				}
				if (_p28SeenMissile == 1 && SpaceSim.Core.Combat.PlayerCombat.PositionOf(uni, _m.TestFight.Target) is { } tp && f % 30 == 0)
				{
					float off = Mathf.RadToDeg(SpaceSim.Core.Flight.AimAssist.Delta(_m.TestHeadingRad, SpaceSim.Core.Flight.AimAssist.Bearing(me.Position, tp.Pos)));
					if (System.MathF.Abs(off) > _p28MaxOff && f > 200) _p28MaxOff = System.MathF.Abs(off);
				}
				if (!uni.Ships.ContainsKey(_p28Raider))
				{
					_m.Session.RunTicks(2);
					Check(uni.Actors[uni.PlayerActor].BountyClaims.Values.Sum() > 0f, "mit Zielhilfe: erster Kaperer abgeschossen, Kopfgeld");
					Check(me.Hull > 0f, "mit Zielhilfe: Falke lebt");
					Check(_p28MaxOff < 45f, $"Zielhilfe hält das Ziel im Bugwinkel (größte Abweichung {_p28MaxOff:0}°)");
					if (_m.TestFight.AutoFire) _m.HudRef.Flight.TestPressAutoFire();
					Next(); break;
				}
				if (f > 60 * 70) { Fail($"Gefechtstest mit Zielhilfe: Kaperer nach 70 s nicht abgeschossen (Kaperer {uni.Ships[_p28Raider].Hull:0}, Falke {me.Hull:0})"); Next(); }
				break;
			}
			case 85: // Gerätetest Phase 28: Kriegstest — Gegenwehr unter „Station angreifen“, Zielhilfe dreht zur Station, Raketenvorrat auf der Bucht, Rakete sichtbar
			{
				var uni = _m.Session.U;
				int f = _frame - _stageStart;
				if (f == 1) { _m.TestMenu("new:war"); break; }
				if (f == 6)
				{
					var hq = uni.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
					_p28Station = hq.Id;
					_m.TestSetStationTarget(hq.Id);
					_m.TestStopAutomation();
					// Nase weg von der Station (wie nach dem Abdocken)
					var pl = uni.Ships[uni.PlayerShip];
					_m.TestFace(pl.Position + (pl.Position - hq.Position));
					_m.SyncHudForTest();
					Check(_m.HudRef.Flight.TestStationAttackInfo.Contains("Geschütze"), $"„Station angreifen“ nennt die Gegenwehr ({_m.HudRef.Flight.TestStationAttackInfo})");
					Check(_m.HudRef.Flight.TestTargetArc.Contains("draußen · Rak. ✓"), $"Zielkarte: außerhalb der Geschütze, Raketen reichen ({_m.HudRef.Flight.TestTargetArc})");
					Check(_m.TestBayPlaces.Any(n => n.Contains(" Rak. · ") && n.Contains('/')), $"Raketenbucht zeigt den Vorrat ({string.Join(",", _m.TestBayPlaces)})");
					// Runde 5: Bollwerk mit eigenem Verband — die Zielkarte zeigt das Kräfteverhältnis
					Check(pl.ModelKey == "bastion" && SpaceSim.Core.Simulation.Squadrons.Get(uni, pl) is { Members.Count: 3 }, "Kriegstest: Bollwerk mit Verband aus zwei weiteren Bollwerken");
					Check(_m.HudRef.Flight.TestStationOdds.StartsWith("Überlegen"), $"Zielkarte: Kräfteverhältnis mit Verband ({_m.HudRef.Flight.TestStationOdds})");
					_m.TestArmStation();
					Check(_m.TestFight.AutoFire && _m.TestAimAssist, "Angriff schaltet Dauerfeuer und „Ziel folgen“ ein");
					// Runde 7: Layout mit dem Bollwerk — keine Kampftaste/-zeile überlappt eine andere, auch mit sechs Schächten
					var lay = _m.HudRef.Flight.TestCombatLayout(6);
					var clash = new List<string>();
					for (int a = 0; a < lay.Count; a++) for (int b = a + 1; b < lay.Count; b++) if (lay[a].Rect.Intersects(lay[b].Rect)) clash.Add($"{lay[a].Name}/{lay[b].Name}");
					Check(clash.Count == 0, $"Kampf-HUD ohne Überlappung ({_m.TestBayPlaces.Count()} Schächte; {string.Join(", ", clash)})");
					_m.HudRef.Toast("Verstärkung (Verband): ein langer Hinweis, der über drei Zeilen umbricht, damit die Beschusswarnung darunter rücken muss — wie am Handy beobachtet.", 5f);
					break;
				}
				if (f == 9)
				{
					var t = _m.HudRef.TestToastRect; var hw = _m.HudRef.Flight.TestHitWarningRect;
					Check(hw.Position.Y >= t.End.Y, $"Beschusswarnung unter dem Hinweis (Hinweis bis {t.End.Y:0}, Warnung ab {hw.Position.Y:0})");
					break;
				}
				if (f == 90)
				{
					var pl = uni.Ships[uni.PlayerShip];
					float off = Mathf.RadToDeg(SpaceSim.Core.Flight.AimAssist.Delta(_m.TestHeadingRad, SpaceSim.Core.Flight.AimAssist.Bearing(pl.Position, uni.Stations[_p28Station].Position)));
					Check(System.MathF.Abs(off) < 5f, $"Zielhilfe dreht die Nase zur Station ({off:0}°)");
					bool fired = _m.TestFireSlot(pl.Fit.IndexOf("missile_launcher_s")) || _m.TestFight.Shots > 0;   // Dauerfeuer kann schon geschossen haben
					Check(fired, $"Rakete gestartet ({_m.TestFightMessage})");
					// Runde 4: „Ziel folgen“ bewusst aus — Dauerfeuer aus/an schaltet es für dieses Ziel nicht wieder ein
					_m.HudRef.Flight.TestPressAimAssist();
					_m.HudRef.Flight.TestPressAutoFire(); _m.HudRef.Flight.TestPressAutoFire();
					Check(!_m.TestAimAssist && _m.TestFight.AutoFire, "bewusstes Aus von „Ziel folgen“ bleibt, bis das Ziel wechselt");
					_m.HudRef.Flight.TestPressAimAssist();
					break;
				}
				if (f > 90 && f < 200 && _m.TestMissilesShown > 0) { _p28SeenMissile = 2; }
				if (f == 200)
				{
					Check(_p28SeenMissile == 2, "Rakete im Flug sichtbar (CombatView)");
					_p28SeenMissile = 0;
					// Runde 5: im Gefecht nennt die Fluchtzeile, wer noch reicht (dieselbe Regel wie die Simulation)
					_m.SyncHudForTest();
					Check(!uni.Fights.IsEngaged(uni.PlayerShip) || _m.HudRef.Flight.TestEscapeStatus.StartsWith("Flucht:"), $"Fluchtzeile im Gefecht ({_m.HudRef.Flight.TestEscapeStatus})");
					// Runde 5: ohne Verband ist der Angriff aussichtslos — Zielkarte und Taste warnen
					var me5 = uni.Ships[uni.PlayerShip];
					if (SpaceSim.Core.Simulation.Squadrons.Get(uni, me5) is { } sq5)
						foreach (var id in sq5.Members.Where(id => id != me5.Id).ToList())
						{
							if (uni.Fights.Of(id) is { } fe && fe.Of(id) is { Active: true } fp) SpaceSim.Core.Combat.Engage.Leave(uni, fe, fp, CombatExit.Fled);
							SpaceSim.Core.Simulation.Departures.RemoveShip(uni, uni.Ships[id], SpaceSim.Core.Model.DepartureReason.Sold, "selftest");
						}
					_m.SyncHudForTest();
					Check(_m.HudRef.Flight.TestStationOdds == "Aussichtslos allein – Fregatte oder Flotte nötig", $"allein: Zielkarte warnt ({_m.HudRef.Flight.TestStationOdds})");
					Next();
				}
				break;
			}
			case 86: // Gerätetest Phase 28: Stationen außerhalb des Bildes — Randmarker, antippen wählt die Station
			{
				var uni = _m.Session.U;
				int f = _frame - _stageStart;
				if (f == 1)
				{
					if (uni.Fights.Of(uni.PlayerShip) is { } e && e.Of(uni.PlayerShip) is { Active: true } pp) SpaceSim.Core.Combat.Engage.Leave(uni, e, pp, CombatExit.Fled);
					_m.TestFight.ClearTarget();
					_m.TestClearTarget();
					break;
				}
				if (f == 5)
				{
					var marks = _m.HudRef.EdgeMarkers.ToList();
					Check(marks.Count > 0, $"Randmarker für Stationen außerhalb des Bildes ({marks.Count})");
					if (marks.Count > 0) { _p28MarkId = marks[0].Id; Tap(marks[0].Pos, true); }
					break;
				}
				if (f == 6) { if (_p28MarkId != 0) Tap(_m.HudRef.EdgeMarkers.FirstOrDefault(m => m.Id == _p28MarkId).Pos, false); break; }
				if (f == 9)
				{
					Check(_p28MarkId == 0 || _m.TestTargetStation == _p28MarkId, $"Tipp auf den Randmarker wählt die Station ({_m.TestTargetStation} statt {_p28MarkId})");
					// Runde 4: Kommandozentrale → Sektor → „Ziel wählen“ setzt das Ziel ohne Autopilot
					var hq = uni.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
					_m.TestOpenCommand(); _m.TestOpenCcArea("sector");
					// Runde 5: ein vorher gewähltes Schiff verschwindet sofort von der Zielkarte
					if (uni.ShipsById().FirstOrDefault(s => s.Sector == uni.PlayerSector && s.Id != uni.PlayerShip) is { } other) _m.TestFight.SetTarget(other.Id);
					_m.HudRef.CcSector.TestSelectStation(hq.Id);
					_m.HudRef.CcSector.TestTargetHere();
					Check(_m.TestFight.Target.Value == 0 && _m.HudRef.Flight.TestTargetName.EndsWith(hq.Name), $"Zielkarte zeigt sofort die neue Station ({_m.HudRef.Flight.TestTargetName})");
					Check(_m.TestTargetStation == hq.Id.Value && !_m.TestAutopilotOn && !_m.TestScreenOpen("cc.sector"), "„Ziel wählen“ in der Sektoransicht wählt die Station ohne Autopilot");
					Next();
				}
				break;
			}
			case 87: // Gerätetest Phase 28: Speicher — Knoten/Objekte über zwei Runden aus je 200 Neuaufbauten und 5 Sektorwechseln (wächst die zweite Runde, ist es ein Leck)
			{
				var uni = _m.Session.U;
				int f = _frame - _stageStart;
				int round = f / 400, rf = f % 400;
				if (f == 1) { _m.TestSetFast(true); _m.TestMenu("new"); _m.Session.RunTicks(2); Check(!_m.TestFast, "„Neues Spiel“ setzt den Zeitraffer auf ×1 zurück"); break; }
				if (round > 2) break;
				if (rf == 30)
				{
					System.GC.Collect(); System.GC.WaitForPendingFinalizers();
					long nodes = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
					long objects = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
					long orphans = (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
					GD.Print($"[selftest] Speicher Runde {round}: Knoten {nodes}, Objekte {objects}, verwaist {orphans}, Ressourcen {Performance.GetMonitor(Performance.Monitor.ObjectResourceCount)}, statisch {Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0:0.0} MB, verwaltet {System.GC.GetTotalMemory(false) / 1048576.0:0.0} MB");
					if (round == 1) { _p28Nodes = nodes; _p28Objects = objects; _p28Orphans = orphans; }
					if (round == 2)
					{
						Check(nodes - _p28Nodes < 100, $"Knotenzahl wächst nicht je Runde ({_p28Nodes} → {nodes})");
						Check(orphans - _p28Orphans < 20, $"keine verwaisten Knoten ({_p28Orphans} → {orphans})");
						Check(objects - _p28Objects < 1000, $"Objektzahl wächst nicht je Runde ({_p28Objects} → {objects})");
						Next(); break;
					}
					var ww = uni.Stations.Values.First(x => x.Name == "Wasserwerk Alpha");
					_m.TestDockAt(ww.Id);
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
					break;
				}
				if (rf > 30 && rf <= 230) { _m.HudRef.DockTrade.TestRebuild(); break; }   // 200 Neuaufbauten
				if (rf == 231) { _m.TestCloseDockArea(); _m.TestUndock(); break; }
				if (rf > 300 && rf <= 305)
				{
					var secs = uni.Sectors.Keys.OrderBy(k => k.Value).ToList();
					_m.TestEnterSector(secs[(rf - 300) % secs.Count]);
					break;
				}
				if (rf == 306) _m.TestEnterSector(new SectorId(1));
				// Runde 16 (Handy-Test: Speicher 679 → 849 MB in 25 min Gefecht/Torangriff): je Runde ein Gefechtstest mit Überfall, Schüssen, Raketen, Wracks
				// und ein Tor fällt aus und läuft wieder an — die Knoten/Objekte der Ansicht müssen danach wieder abgebaut sein
				if (rf == 310) { _m.TestMenu("new:combat"); _m.TestFight.AutoFire = true; break; }
				if (rf > 310 && rf < 380)
				{
					var cu = _m.Session.U;
					if (rf == 311) foreach (var pe in cu.PendingEncounters.Where(p => p.Target == cu.PlayerShip)) pe.DueTick = cu.Tick;
					if (rf == 330 && cu.Sectors[cu.PlayerSector].Gates.FirstOrDefault() is { } gid && cu.Gates.TryGetValue(gid, out var gg) && SpaceSim.Core.Combat.GateCombat.TypeOf(cu, gg) is not null)
						SpaceSim.Core.Combat.GateCombat.Apply(cu, gg, new SpaceSim.Core.Combat.HitResult(true, 1f, 0f, 0f, SpaceSim.Core.Combat.GateCombat.MaxHull(cu, gg) + 10f), new ActorId(9999));
					if (rf == 360 && cu.Sectors[cu.PlayerSector].Gates.FirstOrDefault() is { } gid2 && cu.Gates.TryGetValue(gid2, out var g2))
						SpaceSim.Core.Combat.GateCombat.Restore(cu, g2, "repaired");
					_m.Session.RunTicks(8);
					break;
				}
				if (rf == 385) { _m.TestFight.AutoFire = false; _m.TestMenu("new"); }
				break;
			}
			case 88: // Runde 6: Stationsangriff über den echten Ansichtspfad (Dauerfeuer, Ziel folgen, 50 % Schub, Echtzeit-Bilder) — Schaden je Sekunde wie im Core; dann Rückzug
			{
				var uni = _m.Session.U;
				int f = _frame - _stageStart;
				if (f == 1) { _m.TestMenu("new:war"); break; }
				if (f == 6)
				{
					var hq = uni.Stations.Values.First(x => x.Name == "Sektor-HQ Beta");
					_p28Station = hq.Id;
					_m.TestSetStationTarget(hq.Id);
					_m.TestStopAutomation();
					_m.TestFace(hq.Position);
					_m.TestArmStation();
					_m.TestSetSlider(0.5f);
					_p6Hp = SpaceSim.Core.Combat.StationCombat.Hull(uni, hq) + SpaceSim.Core.Combat.StationCombat.Shield(uni, hq);
					_p6Tick = uni.Tick;
					_p6Events = new List<SpaceSim.Core.Events.SimEvent>();
					uni.Events.Raised += _p6Events.Add;
					break;
				}
				if (f > 6 && f < 6 + 60 * 20)
				{
					// am Ziel nicht weiterfliegen (der Tester flog sogar vorbei — der Schaden hängt nicht davon ab, die Nachladeuhr schon)
					var pl = uni.Ships[uni.PlayerShip];
					if (Vec2.Distance(pl.Position, uni.Stations[_p28Station].Position) < 45f) _m.TestSetSlider(0f);
					break;
				}
				if (f == 6 + 60 * 20)
				{
					var hq = uni.Stations[_p28Station];
					float hp = SpaceSim.Core.Combat.StationCombat.Hull(uni, hq) + SpaceSim.Core.Combat.StationCombat.Shield(uni, hq);
					float secs = (uni.Tick - _p6Tick) / (float)SpaceSim.Core.Simulation.SimClock.TicksPerSecond;
					float dps = (_p6Hp - hp) / System.MathF.Max(1f, secs);
					GD.Print($"[selftest] Stationsangriff über die Ansicht: {_p6Hp:0} → {hp:0} in {secs:0} s Simulationszeit ({dps:0.0} je s), Spielerschüsse {_m.TestFight.Shots}");
					Check(secs > 15f && secs < 25f, $"Zeitraffer ×1: 20 s Echtzeit ≈ 20 s Simulation ({secs:0} s)");
					Check(dps > 0f && dps < 30f, $"Schaden je Sekunde wie im Core-Test, nicht je Bild ({dps:0.0} je s; vorher ≈ 80)");
					Check(!SpaceSim.Core.Combat.StationCombat.IsDown(uni, hq), "das HQ steht nach 20 s noch");
					// Rückzug: Dauerfeuer aus, wegdrehen, voller Schub
					_m.HudRef.Flight.TestPressAutoFire();
					var pl = uni.Ships[uni.PlayerShip];
					_m.TestAimAssist = false;
					_m.TestFace(pl.Position + (pl.Position - hq.Position));
					_m.TestSetSlider(1f);
					break;
				}
				if (f > 6 + 60 * 20 && f < 6 + 60 * 60 && !_p6Events!.OfType<SpaceSim.Core.Events.SiegeWithdrawn>().Any()) break;
				if (f >= 6 + 60 * 20)
				{
					var sw = _p6Events!.OfType<SpaceSim.Core.Events.SiegeWithdrawn>().FirstOrDefault();
					Check(sw is not null && sw.Wingmen == 2, $"Rückzug beendet die Belagerung, der Verband folgt ({sw?.Message ?? "kein Rückzug in 40 s"})");
					Check(SpaceSim.Core.Combat.Siege.Of(uni, _p28Station) is null, "Belagerung beendet");
					_m.TestSetSlider(0f);
					_m.SyncHudForTest();
					Check(!_m.HudRef.Flight.TestTargetName.StartsWith("Angriff:"), $"Zielkarte zeigt keinen Angriff mehr ({_m.HudRef.Flight.TestTargetName})");
					Check(_m.LastCombatMessage?.StartsWith("Rückzug") == true || _m.LastCombatMessage?.Contains("brechen die Belagerung") == true, $"Meldung zum Ende ({_m.LastCombatMessage})");
					Next();
				}
				break;
			}
			case 89: // Runde 8: Stationsmodelle — in den Sektoren mit vielen Stationstypen hat jede Station ihr Modell, Radius = Kollisionskreis, Fraktionsfarbe
			{
				var uni = _m.Session.U;
				int f = _frame - _stageStart;
				if (f == 1) { _m.TestMenu("new"); _p8Sectors = uni.Sectors.Values.OrderByDescending(s => s.Stations.Count).Take(3).Select(s => s.Id).ToList(); _p8Types.Clear(); _p8Over = SpaceAdventure.World.StationModels.OverrideCount; break; }
				int k = (f - 2) / 4;
				if (k < _p8Sectors.Count && (f - 2) % 4 == 0) { _m.TestEnterSector(_p8Sectors[k]); break; }
				if (k < _p8Sectors.Count && (f - 2) % 4 == 3)
				{
					uni = _m.Session.U;
					int before = SpaceAdventure.World.StationModels.OverrideCount;
					foreach (var (id, model, top) in _m.TestView.StationModelNodes())
					{
						var st = uni.Stations[id];
						string key = st.IsBuildSite ? "build_site" : st.TypeKey;
						if (model is null) { Check(!SpaceAdventure.World.StationModels.Has(key), $"Modell für {key} ({st.Name}) geladen, kein Grundkörper"); continue; }
						_p8Types.Add(key);
						float r = SpaceAdventure.World.StationModels.PlaneRadius(model);
						Check(System.MathF.Abs(r - st.Radius) <= 0.1f * st.Radius, $"{st.Name}: Modellradius {r:0.0} ≈ Kollisionskreis {st.Radius:0}");
						var acc = SpaceAdventure.World.StationModels.AccentOf(model);
						var want = SpaceAdventure.World.SectorView.AccentOf(uni, st);
						// Phase 29: gesättigte Besitzerfarbe (Farbton gleich; grau bleibt grau)
						Check(acc is { } a && (want.S <= 0.2f ? System.MathF.Abs(a.Shown.R - want.R) < 0.02f : System.MathF.Abs(Mathf.Wrap(a.Shown.H - want.H + 0.5f, 0f, 1f) - 0.5f) < 0.03f && a.Shown.S >= 0.74f),
							$"{st.Name}: Akzent in der Besitzerfarbe ({acc?.Shown} statt {want})");
						Check(top > 0.5f, $"{st.Name}: Beschriftung über dem Modell ({top:0.0})");
					}
					break;
				}
				if (k >= _p8Sectors.Count)
				{
					GD.Print($"[selftest] Stationsmodelle: {_p8Types.Count} Typen in {_p8Sectors.Count} Sektoren, {SpaceAdventure.World.StationModels.OverrideCount} geteilte Überschreibungsmaterialien");
					Check(_p8Types.Count >= 12, $"viele Stationstypen mit Modell ({_p8Types.Count})");
					// je Modelltyp ein Akzentmaterial je Besitzerfarbe (höchstens drei Farben je Typ), nicht je Station
					int stations = _p8Sectors.Sum(sid => uni.Sectors[sid].Stations.Count);
					int grown = SpaceAdventure.World.StationModels.OverrideCount - _p8Over;
					Check(grown <= _p8Types.Count * 3 && grown < stations * 2,
						$"Überschreibungen geteilt, nicht je Station ({grown} neu für {_p8Types.Count} Typen, {stations} Stationen)");
					// Zustand: ausgeschaltet → Einfärbung folgt ohne Neuaufbau
					var hq = uni.Stations.Values.First(x => x.Sector == uni.PlayerSector && SpaceSim.Core.Combat.StationCombat.DefenseOf(uni, x) is not null);
					hq.Condition = new StationCondition { Disabled = true };
					_m.TestView.SyncStations(uni);
					var node = _m.TestView.StationModelNodes().First(n => n.Id == hq.Id).Model!;
					Check(SpaceAdventure.World.StationModels.AccentOf(node) is { } d && d.Shown.V < SpaceAdventure.World.SectorView.AccentOf(uni, hq).V * d.Source.V, "ausgeschaltete Station abgedunkelt");
					hq.Condition = null;
					_m.TestView.SyncStations(uni);
					Next();
				}
				break;
			}
			case 90: if (GateModelStage(_frame - _stageStart)) Next(); break;   // Jumpgate-Modelle (SelfTest.Gates.cs)
			case 91: if (ShipModelStage(_frame - _stageStart)) Next(); break;   // Runde 11: Schiffsmodelle, Mündungen, Türme (SelfTest.Ships.cs)
			case 92: if (AudioStage(_frame - _stageStart)) Next(); break;   // Runde 12: Musikzustände, Signale, Ton-Busse und Einstellungen (SelfTest.Audio.cs)
			case 93: if (SmallStationStage(_frame - _stageStart)) Next(); break;
			case 94: if (GateCombatStage(_frame - _stageStart)) Next(); break;
			case 95: if (PhoneStage(_frame - _stageStart)) Next(); break;
			case 96: if (AnchorStage(_frame - _stageStart)) Next(); break;   // Phase 32: Anker mit Trossen beim Kontaktabbau (SelfTest.Anchors.cs)
			case 97: if (SessionLeakStage(_frame - _stageStart)) Next(); break;   // Runde 17: Sitzungswechsel gibt das alte Universum frei (SelfTest.Anchors.cs)   // Runde 15: Handyformat — Gefechtskamera, Zielkarte, Tor antippen, Annäherung (SelfTest.Phone.cs)   // Phase 31: Tor angreifen, außer Betrieb, Wiederanlauf (SelfTest.GateCombat.cs)   // Phase 30: Kleinstationen, Größenklassen, Gefechtskamera, Musik-Tipp (SelfTest.Small.cs)
			default:
				if (_fail.Length == 0) { GD.Print("SELFTEST OK"); GetTree().Quit(0); }
				else { GD.PrintErr("SELFTEST FAILED: " + _fail); GetTree().Quit(1); }
				SetProcess(false);
				break;
		}
	}

	void Next() { _stage++; _stageStart = _frame; }
	List<SectorId> _p8Sectors = new(); readonly HashSet<string> _p8Types = new(); int _p8Over;
	float _p6Hp; long _p6Tick; List<SpaceSim.Core.Events.SimEvent>? _p6Events;
	/// <summary>Phase 28: Stufen 73–76.</summary>
	List<SpaceSim.Core.Events.SimEvent>? _p28Events;
	SpaceSim.Core.Model.StationId _p28Station;
	ShipId _p28Raider;
	int _p28SeenMissile;
	string _p28WarAlert = "";
	SectorId _p28LastSector;
	int _p28SectorFrame, _p28Jumps, _p28Hops, _p28Scroll, _p28Try;
	SpaceAdventure.Ui.Kit.KitScroll? _p28ScrollNode;
	static SpaceAdventure.Ui.Kit.KitScroll? FindScroll(Node root, string key)
	{
		foreach (var c in root.GetChildren())
		{
			if (c is SpaceAdventure.Ui.Kit.KitScroll k && !k.IsQueuedForDeletion() && k.ScrollKey.StartsWith(key, System.StringComparison.Ordinal)) return k;
			if (FindScroll(c, key) is { } f) return f;
		}
		return null;
	}
	/// <summary>Phase 23: Hülle und Schild zwischen den Stufen 51 und 52.</summary>
	float _p23Hull, _p23Shield;
	/// <summary>Phase 25: Kampfstufen 54–61.</summary>
	ShipId _p25Falcon, _p25Raider;
	int _p25Shots;
	float _p25Charge, _p25Ammo, _p25Money;
	ulong _p25Rng;
	ShipId _p25Trader;
	int _p25LostFrame = -1;
	/// <summary>Phase 27: Stufen 68–72.</summary>
	SpaceSim.Core.Model.Universe? _p27Universe;
	SpaceSim.Core.Model.FactionId _p27Faction;
	ShipId _p27Police;
	bool _p27Warned;
	float _p27Hull;
	int _p27Hits;
	void Push(InputEvent e) => GetViewport().PushInput(e, true);
	/// <summary>Zweiter Gerätetest Phase 28: simulierter Tipp (Finger auf Touch-Geräten, sonst linke Maustaste) an einer Bildschirmposition.</summary>
	void Tap(Vector2 at, bool down)
	{
		if (Ui.Pointer.TouchMode) Push(new InputEventScreenTouch { Index = 0, Position = at, Pressed = down });
		else Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = at, GlobalPosition = at, Pressed = down, ButtonMask = down ? MouseButtonMask.Left : 0 });
	}
	/// <summary>Drückt bzw. lässt die erste sichtbare Kit-Taste mit dieser Beschriftung unter <paramref name="root"/> los (über einen simulierten Tipp).</summary>
	void PressButton(Node root, string label, bool down)
	{
		if (down) _p28Press = Buttons(root).FirstOrDefault(b => b.Text == label && b.IsVisibleInTree())?.GetGlobalRect().GetCenter();
		if (_p28Press is { } at) Tap(at, down);
		else Fail($"Taste „{label}“ nicht gefunden (Bildschirm {_m.HudRef.Screens.Current?.Id ?? "-"}; Tasten {string.Join(",", Buttons(root).Where(b => b.IsVisibleInTree()).Select(b => b.Text))}; alle {string.Join(",", Buttons(root).Select(b => b.Text))}; wischt {Ui.Kit.KitScroll.Dragging}, gedrückt {Ui.Kit.KitButton.AnyHeld}, Zeilen {_m.HudRef.DockTrade.TestRowCount}, Station {_m.HudRef.DockCtx.Station?.Name}, gleiche Welt {ReferenceEquals(_m.HudRef.DockCtx.U, _m.Session.U)}, Meldung {_m.HudRef.DockTrade.TestMessage})");
	}
	Vector2? _p28Press;
	bool _p30Engage = true;
	static IEnumerable<Ui.Kit.KitButton> Buttons(Node root)
	{
		foreach (var c in root.GetChildren())
		{
			if (c is Ui.Kit.KitButton b && !b.IsQueuedForDeletion()) yield return b;
			foreach (var x in Buttons(c)) yield return x;
		}
	}
	Ui.Kit.KitButton? _p28Probe;
	int _p28ProbeHits;
	float _p28Max, _p28Cargo, _p28Buyable, _p28MaxOff;
	int _p28MarkId;
	long _p28Nodes, _p28Objects, _p28Orphans;
	/// <summary>Phase 26: Schaden, Reparatur, Bergung (Stufen 63–66).</summary>
	int _p26Laser, _p26Wreck;
	float _p26Hull, _p26Cargo, _p26Keep;
	ShipId _p26Salvager;
	void Log(string s) => GD.Print("[selftest] " + s);
	/// <summary>Phase 18: Ware „Nahrung“ der Welt (für die Abwurf-Prüfungen).</summary>
	static SpaceSim.Core.Model.GoodId DemoFood(SpaceSim.Core.Model.Universe u) => u.FindGood("food")!.Id;

	void Check(bool ok, string what) { if (!ok) { _fail += what + "; "; GD.PrintErr("[selftest] FEHLER: " + what); } else Log("ok: " + what); }
	void Fail(string what) { _fail += what + "; "; GD.PrintErr("[selftest] FEHLER: " + what); _stage = 99; }
}
