using System.Collections.Generic;
using System.Linq;
using Godot;
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
					Check(areas.Any(a => a.Id == Ui.Screens.DockAreas.Info && a.Available) && areas.Any(a => a.Id == Ui.Screens.DockAreas.Hangar && a.Available),
						"Station-Info und Hangar sind immer aktiv");
					Check(areas.Where(a => a.Id is Ui.Screens.DockAreas.Repair or Ui.Screens.DockAreas.Outfitter or Ui.Screens.DockAreas.Shipyard or Ui.Screens.DockAreas.Taxi or Ui.Screens.DockAreas.Missions)
						.All(a => !a.Available), "Platzhalterbereiche (Reparatur, Ausrüster, Werft, Taxi, Aufträge) sind nicht aktiv");
				}
				{ // Handel öffnen und über die Oberfläche (Dienst Execute) kaufen/verkaufen
					var st = u.Stations[_m.TestDockedStation!.Value];
					var pl = _m.Session.Player; pl.Credits = 100000f;
					_m.TestOpenDockArea(Ui.Screens.DockAreas.Trade);
					Check(_m.TestScreenOpen("dock.trade"), "Handel-Bereich über das Menü geöffnet");
					Check(_m.HudRef.DockTrade.TestRowCount > 0, "Handel zeigt Zeilen aus IStationService.Offers");
					var gid = st.Market.First(k => k.Value.Amount >= 5f && k.Value.Price > 0f).Key;
					float before = pl.Cargo.GetValueOrDefault(gid);
					float creditsBefore = pl.Credits;
					float price = st.Market[gid].Price;
					var buy = _m.HudRef.DockTrade.TestExecute($"buy:{gid.Value}", 5f);
					Check(buy.Success && buy.Units == 5f, "Kaufen über Dienst+Oberfläche (Execute buy)");
					Check(pl.Cargo.GetValueOrDefault(gid) == before + 5f, "Fracht nach Kauf um 5 erhöht");
					Check(System.Math.Abs(pl.Credits - (creditsBefore - 5f * price)) < 0.01f, "Credits nach Kauf um 5 × Preis gesunken");
					Check(_m.HudRef.DockTrade.TestMessage.Length > 0, "Ergebnismeldung (ServiceResult.Message) wird angezeigt");
					var sell = _m.HudRef.DockTrade.TestExecute($"sell:{gid.Value}", 5f);
					Check(sell.Success && sell.Units == 5f, "Verkaufen über Dienst+Oberfläche (Execute sell)");
					Check(pl.Cargo.GetValueOrDefault(gid) == before, "Fracht nach Verkauf wieder auf Anfangsstand");
					Check(System.Math.Abs(pl.Credits - creditsBefore) < 0.01f, "Credits nach Verkauf wieder hergestellt");
					// Alles verkaufen über sell_all
					_m.HudRef.DockTrade.TestExecute($"buy:{gid.Value}", 3f);
					var all = _m.HudRef.DockTrade.TestExecute("sell_all", 0f);
					Check(all.Success && pl.Cargo.GetValueOrDefault(gid) == 0f, "„Alles verkaufen“ (sell_all) über die Oberfläche räumt die Ware ab");
				}
				Next(); break;
			case 3: // Abdocken aus einem offenen Bereich heraus
				_m.TestUndock();
				Check(!_m.Docked, "Abdocken aus dem Handelsbereich");
				Check(_m.HudRef.Screens.Current == null, "Dock-Bildschirme nach dem Abdocken geschlossen");
				Next(); break;
			case 4: // Speichern, Spiel verändern, Laden
				Check(_m.TestSave(), "Speichern");
				_m.Session.Player.Credits = 12345;
				_m.TestMenu("load");
				Next(); break;
			case 5:
				Check(_m.Session.U.PlayerSector.Value == 4, "Nach Laden im Sektor Delta");
				Check(_m.Session.Player.Credits != 12345, "Laden stellt gespeicherten Stand her");
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
					_m.TestSetSlider(-1f);
					Check(System.Math.Abs(_m.TestTargetSpeed + 9f) < 0.01f, "Rückwärtsgrenze: Slider -1 → -9 m/s (20 % von 45)");
					Next();
				}
				else if (_frame - _stageStart > 900) Fail("Schiff beschleunigt nicht auf Slider-Tempo");
				break;
			case 9:
				if (_m.TestForwardSpeed < -5f)
				{
					Check(_m.TestForwardSpeed >= -9.6f, $"Rückwärtsfahrt höchstens 20 % (ist {_m.TestForwardSpeed:0.0} m/s, Ziel -9 m/s)");
					Log($"Rückwärts {_m.TestForwardSpeed:0.0} m/s bei Slider -1");
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
				if (_m.TestSales >= 1) { Log($"Verkauf nach {_frame - _stageStart} Frames, Credits {_m.Session.Player.Credits:0}"); Check(_m.Session.Player.Credits > 500f, "Credits gestiegen"); Check(_m.TestHits == 0, $"Auto-Mining ohne harte Kollision (Treffer: {_m.TestHits}, niedrigste Hülle {_m.TestLowestHull:0}) – {_m.TestLastHit}"); Next(); }
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
