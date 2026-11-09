using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Skriptierter Durchlauf ohne Anzeige: godot-mono --headless --path godot --fixed-fps 60 -- --selftest
/// Prüft Autopilot über mehrere Sektoren, Sprünge, Andocken, Oberflächen-Panels, Speichern und Laden sowie den Auto-Mining-Kreislauf (Feld anfliegen, abbauen, verkaufen).
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
			case 2: // Panels
				_m.HudRef.Economy.Visible = true; _m.HudRef.Economy.Refresh(_m.Session, force: true);
				_m.HudRef.Map.Open(u, u.PlayerSector, null);
				_m.HudRef.Menu.Visible = true;
				{ // Kaufen am Dock: erste Ware mit Bestand, dann zurückverkaufen
					var st = u.Stations[u.Stations.Values.First(x => x.Sector == u.PlayerSector).Id];
					var pl = _m.Session.Player; pl.Credits = 100000f;
					var gid = st.Market.First(k => k.Value.Amount >= 5f && k.Value.Price > 0f).Key;
					float before = pl.Cargo.GetValueOrDefault(gid);
					var r = SpaceSim.Core.Trading.TradeActions.Buy(u, pl, st, gid, 5f);
					Check(r.Units == 5f && pl.Cargo.GetValueOrDefault(gid) == before + 5f, "Kaufen im Dock-Zustand");
					_m.HudRef.Dock.Open(u, st, pl);
				}
				Next(); break;
			case 3:
				_m.HudRef.Map.Visible = false; _m.HudRef.Menu.Visible = false; _m.TestUndock();
				Check(!_m.Docked, "abgedockt");
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
			case 7: // Auto-Mining: aus Delta zum Feld fliegen, abbauen
				_m.TestStartMining();
				Check(_m.TestMinerEnabled, "Auto-Mining gestartet");
				Next(); break;
			case 8:
				if (_m.TestCargoUnits > 10f) { Log($"Fracht {_m.TestCargoUnits:0} nach {_frame - _stageStart} Frames in {_m.Session.U.Sectors[_m.Session.U.PlayerSector].Name} ({_m.TestMinerStatus})"); Next(); }
				else if (!_m.TestMinerEnabled) Fail("Auto-Mining beendete sich: " + _m.TestMinerStatus);
				else if (_frame - _stageStart > 30_000) Fail("Keine Fracht abgebaut, Status: " + _m.TestMinerStatus);
				break;
			case 9: // verkaufen
				if (_m.TestSales >= 1) { Log($"Verkauf nach {_frame - _stageStart} Frames, Credits {_m.Session.Player.Credits:0}"); Check(_m.Session.Player.Credits > 500f, "Credits gestiegen"); Check(_m.TestHits == 0, $"Auto-Mining ohne harte Kollision (Treffer: {_m.TestHits}, niedrigste Hülle {_m.TestLowestHull:0}) – {_m.TestLastHit}"); Next(); }
				else if (_frame - _stageStart > 40_000) Fail("Kein Verkauf, Status: " + _m.TestMinerStatus);
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
