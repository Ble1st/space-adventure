using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.World;
using SpaceAdventure.Ui.Screens;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// UI-Umbau (Nutzerwunsch nach Interstellar Pilot 2): Kopfleiste (Nachrichten, Befehle, Zeitraffer), „☰ Sektor“ mit der Scanliste (Antippen = Ziel,
/// Halten = Autopilot, 📡 Funk), Frachtzeile im Status (Tipp = Inventar im Schiffsmenü).
/// </summary>
public partial class Main
{
	List<ScanContact> _scan = new();
	double _scanAt = -1;

	void WireScanAndBar()
	{
		_hud.Flight.CommsPressed += () => { _sfx.Play(SfxId.Click); OpenCcArea(Ui.Screens.CommandAreas.Comms); };
		_hud.Flight.CommandPressed += () => { _sfx.Play(SfxId.Click); OpenCcArea(Ui.Screens.CommandAreas.Fleet); };   // Karte ist ein eigenes Fenster — die Zentrale beginnt bei „Besitz“
		_hud.Flight.FastPressed += () => OnCommandAction("fast");
		_hud.Flight.CargoPressed += () => { _sfx.Play(SfxId.Click); OpenInventory(); };
		_hud.Flight.ScanPressed += () => { _sfx.Play(SfxId.Click); _scanAt = -1; };
		// Nutzerwunsch: Kameraoptionen im Regal unten links (dieselben Aktionen wie im Pause-Menü)
		_hud.Flight.CameraAction += k => { OnCommandAction(k); _hud.Flight.SetCameraMode(_orbit.Mode); };
		var p = _hud.Flight.Scan;
		p.Select += c => { ScanSelect(c); _hud.Flight.SetScanOpen(false); };
		p.Autopilot += c =>
		{
			ScanSelect(c);
			_hud.Flight.SetScanOpen(false);
			if (_target is null) { _hud.Toast("Dorthin fliegt der Autopilot nicht"); return; }
			_hud.Flight.SetAutopilot(true);
			ToggleAutopilot();
		};
		p.Hail += c => { ScanSelect(c); _hud.Flight.SetScanOpen(false); OpenHail(c); };
	}

	/// <summary>Nutzerwunsch: „Karte“ öffnet die große Karte als eigenes Fenster (Universum/Sektor umschaltbar), nicht mehr Teil der Kommandozentrale.
	/// Vorgewählt ist der Sektor des Ziels, sonst der eigene.</summary>
	void OpenMapWindow()
	{
		var u = _session.U;
		_hud.Map.Visible = false;
		var sel = _target?.Sector ?? u.PlayerSector;
		if (u.Sectors.ContainsKey(sel)) _hud.CcUniverse.SelectedSector = sel;
		OpenCcArea(Ui.Screens.CommandAreas.Universe);
	}

	/// <summary>UI-Umbau: Inventar = Schiffsmenü des aktiven Schiffs, Reiter „Inventar“.</summary>
	void OpenInventory()
	{
		var u = _session.U;
		if (!u.Ships.ContainsKey(u.PlayerShip)) return;
		_hud.ShipMenu.Current = ShipMenuScreen.Tab.Cargo;
		OpenShipMenu(u.PlayerShip);
	}

	/// <summary>Zeile der Scanliste als Ziel übernehmen (dieselben Wege wie das Antippen in der Welt).</summary>
	void ScanSelect(ScanContact c)
	{
		var u = _session.U;
		switch (c.Kind)
		{
			case ScanKind.Station:
				var sid = new StationId(c.Id);
				if (u.Stations.ContainsKey(sid)) SelectTarget(Navigator.ForStation(u, sid, u.PlayerShip), sid);
				break;
			case ScanKind.Gate: SelectGate(new GateId(c.Id)); break;
			case ScanKind.Ship: SelectShip(new ShipId(c.Id)); break;
			case ScanKind.Rock:
				var rock = u.Sectors[u.PlayerSector].Asteroids.FirstOrDefault(a => a.Id == c.Id);
				if (rock is not null)
				{
					_mineTarget = rock;
					SelectTarget(new NavTarget(u.PlayerSector, rock.Position, c.Name, rock.Radius + 6f), null, quiet: true);
					_hud.Toast($"Ziel: {c.Name} ({rock.Ore:0} Erz) · „Autopilot“ fliegt hin");
				}
				break;
			case ScanKind.Wreck:
				var w = u.Wrecks.FirstOrDefault(x => x.Id == c.Id);
				if (w is not null) { SelectTarget(new NavTarget(u.PlayerSector, w.Position, c.Name, 12f), null, quiet: true); _hud.Toast($"Ziel: {c.Name} · {c.Info}"); }
				break;
		}
	}

	/// <summary>Funk mit dem Ziel (Stufe 7 verdrahtet das Gespräch).</summary>
	partial void OpenHailPartial(ScanContact c, ref bool handled);
	void OpenHail(ScanContact c)
	{
		bool handled = false;
		OpenHailPartial(c, ref handled);
		if (!handled) _hud.Toast("Funk: noch keine Verbindung möglich");
	}

	/// <summary>Frachtzeile und Zähler fürs Flug-HUD; Scanliste höchstens viermal je Sekunde neu.</summary>
	void FillCargoAndScan(FlightHudData d)
	{
		var u = _session.U;
		d.CommsUnread = SpaceSim.Core.Comms.Comms.Unread(u);
		if (u.Ships.TryGetValue(u.PlayerShip, out var me))
		{
			d.CargoCap = me.CargoCapacity;
			d.CargoUsed = me.UsedVolume(u) + me.MissionCargo;
			float cap = MathF.Max(1f, me.CargoCapacity);
			var parts = new List<(string Name, float Units, float Vol, Color Col)>();
			foreach (var (g, a) in me.Cargo.OrderBy(kv => kv.Key.Value))
			{
				if (a <= 0.001f || !u.Goods.TryGetValue(g, out var good)) continue;
				parts.Add((good.Name, a, a * good.VolumePerUnit, Color.FromHtml(good.Color)));
			}
			foreach (var p in parts.OrderByDescending(p => p.Vol)) d.CargoSegments.Add((p.Col, p.Vol / cap));
			if (me.MissionCargo > 0.01f) d.CargoSegments.Add((new Color(0.91f, 0.76f, 0.35f), me.MissionCargo / cap));
			var names = parts.OrderByDescending(p => p.Vol).Take(3).Select(p => $"{p.Name} {p.Units:0}").ToList();
			if (me.MissionCargo > 0.01f) names.Add("versiegelt");
			d.CargoText = string.Join(" · ", names);
		}
		double now = Time.GetTicksMsec() / 1000.0;
		if (_scanAt < 0 || now - _scanAt > 0.25)
		{
			_scanAt = now;
			_scan = SectorScan.For(u, u.PlayerShip, new Vec2(_player.Position.X, _player.Position.Z));
		}
		d.ScanCount = _scan.Count;
		d.ScanHostile = _scan.Count(c => c.Hostile);
		if (_hud.Flight.ScanOpen)
		{
			(ScanKind, int)? sel = _fight.Target.Value != 0 ? (ScanKind.Ship, _fight.Target.Value)
				: _targetStation is { } ts ? (ScanKind.Station, ts.Value) : _targetGate is { } tg ? (ScanKind.Gate, tg.Value) : _mineTarget is { } mr ? (ScanKind.Rock, mr.Id) : null;
			_hud.Flight.Scan.SetData(u.Sectors[u.PlayerSector].Name, _scan, sel);
		}
	}

	internal List<ScanContact> TestScan => _scan;
}
