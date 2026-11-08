using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Game;
using SpaceAdventure.Ui;
using SpaceAdventure.World;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceAdventure;

/// <summary>
/// Kompositionswurzel: verdrahtet Session, Sektoransicht, Spielerschiff und Oberfläche und übersetzt Eingaben in
/// Steuerwünsche. Neue Features hängen sich als eigene Komponenten an, statt diese Klasse aufzublähen.
/// Steuerung: Stick/WASD/Pfeile, Maus oder Finger gedrückt halten = zum Zeiger fliegen, Antippen eines Ziels = Autopilot-Ziel.
/// </summary>
public partial class Main : Node3D
{
	const float GateRadius = 10f;
	const float AutosaveSeconds = 30f;
	static readonly Vector3 CamOffset = new(0, 38, 24);

	GameSession _session = null!;
	SectorView _view = null!;
	PlayerShip _player = null!;
	Camera3D _cam = null!;
	Hud _hud = null!;
	Fx _fx = null!;
	Sfx _sfx = null!;
	MiningView _mining = null!;

	// Abbau: Spieler-Mikrosimulation (flüchtig), Automodus und manuelle Bedienung
	readonly OreChunks _chunks = new();
	readonly MiningAutopilot _miner = new();
	bool _tractorOn;
	bool _fireRequested;   // Einzelschuss angefordert (Tipp auf "Feuer" oder Taste F)
	Asteroid? _mineTarget;
	float _sinceHit = 99f;
	int _hitCount;
	string _lastHit = "";
	float _lowestHull = 100f;
	bool _transition;   // Ein-/Ausdocken läuft (Schiff animiert, keine Kollision)
	string _mineHint = "";
	float _hintCooldown;
	MiningStep? _miningStep;
	int _sales;

	NavTarget? _target;
	StationId? _targetStation;
	StationId? _docked;
	bool _autopilot, _fast;
	float _jumpCooldown, _autosave;
	float _hitFlash;

	// Zeiger in der Welt (nicht über Oberfläche): Halten = fliegen, Antippen = Ziel wählen
	int _pointerFinger = -1;
	Vector2 _pointerPos, _pointerStart;
	double _pointerDownAt;

	Ship PlayerData => _session.Player;

	public override void _Ready()
	{
		BuildEnvironment();
		BuildStars();

		_view = new SectorView(); AddChild(_view);
		_mining = new MiningView { GoodColor = id => GoodColor(id) }; AddChild(_mining);
		_player = new PlayerShip(); AddChild(_player);
		_fx = new Fx(); AddChild(_fx);
		_sfx = new Sfx(); AddChild(_sfx);
		_cam = new Camera3D { Fov = 50f, Far = 1000f }; AddChild(_cam);
		_hud = new Hud(); AddChild(_hud);

		_hud.TargetScreenPosition = ScreenPositionOfTarget;
		_hud.MapPressed += () => { _sfx.Play(SfxId.Click); _hud.Map.Open(_session.U, _session.U.PlayerSector, _target?.Sector); };
		_hud.Map.SectorSelected += sector => SetTarget(Navigator.ForSector(_session.U, sector, _session.U.PlayerShip), null);
		_hud.EconomyPressed += () => { _sfx.Play(SfxId.Click); _hud.Economy.Visible = !_hud.Economy.Visible; _hud.Economy.Refresh(_session, force: true); };
		_hud.MenuPressed += () => { _sfx.Play(SfxId.Click); _hud.Menu.Visible = true; };
		_hud.Menu.Action += OnMenu;
		_hud.AutopilotPressed += ToggleAutopilot;
		_hud.FastPressed += () => _fast = _hud.FastButton.On;
		_hud.DockPressed += DockAtNearest;
		_hud.Dock.Undock += Undock;
		_hud.Dock.SellAll += SellAllHere;
		_hud.LaserButton.Down += () => _fireRequested = true;
		_hud.TractorPressed += () => { _sfx.Play(SfxId.Click); _tractorOn = _hud.TractorButton.On; };
		_hud.AutoMinePressed += () => SetAutoMining(_hud.AutoMineButton.On);
		_player.Hit += OnPlayerHit;
		_view.AsteroidGone += (pos, r) => { _fx.Burst(pos, new Color(1f, 0.7f, 0.3f), 22, 10f); _sfx.Play(SfxId.Crack); };
		_chunks.Shot += OnShot;

		bool selftest = OS.GetCmdlineUserArgs().Contains("--selftest");
		if (selftest)
		{
			SaveService.UseFile("selftest.json");
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://selftest.json"));   // immer frisch beginnen
		}
		StartSession();
		if (selftest) AddChild(new SelfTest(this));
	}

	// ───────── Spielstand ─────────

	void StartSession()
	{
		if (SaveService.TryLoad(out var u, out var savedAt, out var error) && u != null)
		{
			_session = new GameSession();
			_session.Use(u);
			var result = OfflineCatchUp.Run(u, _session.Runner, DateTimeOffset.UtcNow - savedAt);
			AttachSession();
			if (result.TicksRun > 0)
				_hud.Toast($"Willkommen zurück – {result.TicksRun / SimClock.TicksPerSecond / 60} Min Spielzeit nachgeholt{(result.Capped ? " (gedeckelt)" : "")}", 4f);
		}
		else
		{
			if (SaveService.Exists) GD.PushWarning("Spielstand nicht ladbar, starte neu: " + error);
			_session = GameSession.New();
			AttachSession();
			if (SaveService.Exists) _hud.Toast("Spielstand beschädigt – neues Spiel gestartet", 4f);
		}
	}

	/// <summary>Hängt Ansicht und Ereignisse an das aktuelle Universum und setzt das Spielerschiff.</summary>
	void AttachSession()
	{
		var u = _session.U;
		u.Events.Raised += OnSimEvent;
		_target = null; _targetStation = null; _docked = null; _autopilot = false;
		_hud.AutopilotButton.On = false;
		SetAutoMining(false, announce: false);
		_chunks.Clear(); _mineTarget = null; _mining.ClearPlayer(); _mining.ResetNpc();
		_transition = false; _player.Visible = true; _player.Scale = Vector3.One; _player.GiveGrace(2f);
		_hud.Dock.Visible = false;
		_view.Rebuild(u);
		var p = PlayerData;
		_player.Position = new Vector3(p.Position.X, 0, p.Position.Y);
		_player.Velocity = Vector3.Zero; _player.Locked = false;
		_cam.Position = _player.Position + CamOffset;
	}

	void OnMenu(string action)
	{
		_sfx.Play(SfxId.Click);
		switch (action)
		{
			case "save": SaveNow(manual: true); break;
			case "load":
				if (SaveService.TryLoad(out var u, out _, out var error) && u != null) { _session.Use(u); AttachSession(); _hud.Toast("Spielstand geladen"); }
				else _hud.Toast("Laden fehlgeschlagen: " + error, 4f);
				break;
			case "new": _session.Use(SpaceSim.Core.Content.WorldBuilder.BuildDefault()); AttachSession(); _hud.Toast("Neues Spiel"); break;
		}
		if (action != "save") _hud.Menu.Visible = false;
	}

	void SaveNow(bool manual)
	{
		SyncPlayerData();
		bool ok = SaveService.Save(_session.U, out var error);
		if (manual) _hud.Toast(ok ? "Gespeichert" : "Speichern fehlgeschlagen: " + error, ok ? 2f : 4f);
		else if (!ok) GD.PushWarning("Autosave fehlgeschlagen: " + error);
	}

	void SyncPlayerData()
	{
		PlayerData.Position = new Vec2(_player.Position.X, _player.Position.Z);
	}

	public override void _Notification(int what)
	{
		if (_session != null && (what == NotificationWMCloseRequest || what == NotificationApplicationPaused)) SaveNow(manual: false);
	}

	void OnSimEvent(SimEvent e)
	{
		switch (e)
		{
			case StationBuilt b: _hud.Toast($"Neue Station: {_session.U.Stations[b.Station].Name}"); break;
			case PlayerSold: _sales++; break;
			case MinerShot shot when shot.Sector == _session.U.PlayerSector: OnMinerShot(shot); break;
			case StationRemoved r when r.Reason == "bankrupt": _hud.Toast("Station aufgegeben (Pleite)"); break;
		}
	}

	// ───────── Frame ─────────

	public override void _Process(double deltaD)
	{
		float delta = (float)deltaD;
		_jumpCooldown = Mathf.Max(0f, _jumpCooldown - delta);
		_hitFlash = Mathf.Max(0f, _hitFlash - delta);

		_session.Advance(delta, _fast ? 10f : 1f);
		if (_view.NeedsRebuild(_session.U)) { _view.Rebuild(_session.U); _mining.ResetNpc(); _chunks.Clear(); _mineTarget = null; }
		_view.SyncAsteroids(_session.U, delta);

		// Hülle regeneriert langsam, wenn eine Weile nichts getroffen hat
		_sinceHit += delta;
		if (_sinceHit > 5f && PlayerData.Hull < Ship.MaxHull) PlayerData.Hull = Mathf.Min(Ship.MaxHull, PlayerData.Hull + 3f * delta);

		bool frozen = _hud.AnyModalOpen || _docked != null;
		_miningStep = _miner.Enabled && !frozen
			? _miner.Step(_session.U, _chunks, PlayerData, new Vec2(_player.Position.X, _player.Position.Z))
			: null;
		if (!_miner.Enabled && _hud.AutoMineButton.On) { _hud.AutoMineButton.On = false; _hud.Toast(_miner.Status, 3.5f); }   // Automatik hat sich selbst beendet
		var (dir, throttle, speedScale) = frozen ? (Vector2.Zero, 0f, 1f) : ReadSteering();
		_player.Locked = _docked != null || _transition;
		bool hold = _miner.Enabled && _miningStep is { Nav: null, Waypoint: null };   // Automatik arbeitet an Ort und Stelle
		_player.Step(delta, dir, throttle, speedScale, _view, brake: hold);
		_sfx.SetThrust(_player.Thrusting ? Mathf.Clamp(throttle, 0.3f, 1f) : 0f);

		if (!frozen) { CheckGates(); UpdateDockButton(); }
		UpdateMining(delta, frozen);
		SyncPlayerData();

		_view.Animate(delta);
		_view.UpdateShips(_session.U, _session.FractionalTick, _docked != null ? null : _player.Position, delta);   // angedockt: niemand weicht dem unsichtbaren Schiff aus
		_cam.Position = _cam.Position.Lerp(_player.Position + CamOffset, 1f - Mathf.Exp(-6f * delta));
		_cam.LookAt(_cam.Position - CamOffset);

		UpdateHud();
		_hud.Economy.Refresh(_session);
		_autosave += delta;
		if (_autosave >= AutosaveSeconds) { _autosave = 0f; SaveNow(manual: false); }
	}

	/// <summary>Steuerwunsch aus Stick/Tastatur (Vorrang), Zeiger oder Autopilot.</summary>
	(Vector2 dir, float throttle, float speedScale) ReadSteering()
	{
		var manual = _hud.Stick.Value;
		var keys = new Vector2(
			(Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) ? 1 : 0) - (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) ? 1 : 0),
			(Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down) ? 1 : 0) - (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) ? 1 : 0));
		if (keys != Vector2.Zero) manual = keys.Normalized();

		if (manual.Length() > 0.1f) { CancelAutopilot(); if (_miner.Enabled) SetAutoMining(false, "Auto-Mining aus (manuelle Steuerung)"); return (manual, manual.Length(), 1f); }

		if (_pointerFinger >= 0 && Time.GetTicksMsec() / 1000.0 - _pointerDownAt > 0.2)
		{
			var ground = GroundPoint(_pointerPos);
			if (ground is { } g)
			{
				var d = g - _player.Position;
				if (d.Length() > 3f) { CancelAutopilot(); return (new Vector2(d.X, d.Z), 1f, 1f); }
			}
		}

		if (_miner.Enabled && _miningStep is { } ms)
		{
			if (ms.Nav is { } nav) { _targetStation = ms.DockAt; _target = nav; return Autopilot(nav); }
			if (ms.Waypoint is { } wp)
				return (new Vector2(wp.X - _player.Position.X, wp.Y - _player.Position.Z), ms.Throttle, ClearanceScale());
			return (Vector2.Zero, 0f, ClearanceScale());
		}
		if (_autopilot && _target is { } target) return Autopilot(target);
		return (Vector2.Zero, 0f, 1f);
	}

	(Vector2, float, float) Autopilot(NavTarget target)
	{
		var u = _session.U;
		var step = Navigator.Next(u, u.PlayerSector, new Vec2(_player.Position.X, _player.Position.Z), target);
		switch (step.Status)
		{
			case NavStatus.Unreachable: _hud.Toast("Kein Weg zum Ziel (Gates gesperrt)"); CancelAutopilot(); return (Vector2.Zero, 0f, 1f);
			case NavStatus.Arrived:
				_hud.Toast($"Ziel erreicht: {target.Label}");
				CancelAutopilot();
				if (_targetStation is { } sid && u.Stations.ContainsKey(sid)) Dock(sid);
				_target = null; _targetStation = null;
				return (Vector2.Zero, 0f, 1f);
		}
		var to = new Vector2(step.Waypoint.X - _player.Position.X, step.Waypoint.Y - _player.Position.Z);
		// Im Zielsektor sanft abbremsen, damit das Schiff nicht über den Parkpunkt hinausschießt
		float final = u.PlayerSector == target.Sector ? Vec2.Distance(new Vec2(_player.Position.X, _player.Position.Z), target.Position) : 999f;
		float scale = Mathf.Min(Mathf.Clamp(final / 40f, 0.25f, 1f), ClearanceScale());
		return (to, 1f, scale);
	}

	/// <summary>Drosselt den Autopiloten in der Nähe fester Hindernisse, damit er mit Schwung nicht in Asteroiden oder Stationen läuft.</summary>
	float ClearanceScale()
	{
		float edge = float.MaxValue;
		foreach (var o in _view.Obstacles)
			edge = Mathf.Min(edge, new Vector2(_player.Position.X - o.Position.X, _player.Position.Z - o.Position.Y).Length() - o.Radius - Collision.ShipRadius);
		return Mathf.Clamp(edge / 30f, 0.22f, 1f);   // bei Berührungsnähe ~10 u/s, unter der Schadensschwelle
	}

	void CheckGates()
	{
		if (_jumpCooldown > 0f) return;
		foreach (var (id, pos) in _view.Gates)
			if (_player.Position.DistanceTo(pos) < GateRadius) { Jump(id); return; }
	}

	/// <summary>Sprung: Core hängt das Schiff um, Szene des alten Sektors wird verworfen, der Zielsektor neu aufgebaut.</summary>
	void Jump(GateId gateId)
	{
		var u = _session.U;
		var far = u.Gates[u.Gates[gateId].Target];
		u.MoveShip(PlayerData, far.Sector);
		u.PlayerSector = far.Sector;
		_view.Rebuild(u);
		_mining.ResetNpc(); _mining.ClearPlayer(); _chunks.Clear(); _mineTarget = null;
		_player.GiveGrace(1.5f);

		var gpos = new Vector3(far.Position.X, 0, far.Position.Y);
		var dir = (-gpos).Normalized();
		_player.Position = gpos + dir * 22f;
		_player.Heading = Mathf.Atan2(-dir.X, -dir.Z);
		_player.Velocity = dir * 12f;
		_jumpCooldown = 2f;
		_cam.Position = _player.Position + CamOffset;
		_hud.Flash(new Color(0.6f, 0.85f, 1f));
		_sfx.Play(SfxId.Jump);
		_hud.Toast(u.Sectors[far.Sector].Name);
	}

	// ───────── Ziele, Autopilot, Andocken ─────────

	void SetTarget(NavTarget target, StationId? station)
	{
		_target = target; _targetStation = station;
		_autopilot = true; _hud.AutopilotButton.On = true;
		_sfx.Play(SfxId.Click);
		_hud.Toast($"Autopilot: {target.Label}");
	}

	void ToggleAutopilot()
	{
		_sfx.Play(SfxId.Click);
		if (_target == null) { _hud.AutopilotButton.On = false; _hud.Toast("Kein Ziel – Station oder Gate antippen, oder Karte öffnen"); return; }
		_autopilot = _hud.AutopilotButton.On;
	}

	void CancelAutopilot()
	{
		if (!_autopilot) return;
		_autopilot = false; _hud.AutopilotButton.On = false;
	}

	Vector2? ScreenPositionOfTarget()
	{
		if (_target is not { } t || _session == null) return null;
		var u = _session.U;
		Vector3 world;
		if (t.Sector == u.PlayerSector) world = new Vector3(t.Position.X, 0, t.Position.Y);
		else
		{
			var route = u.FindRoute(u.PlayerSector, t.Sector);
			if (route is not { Count: > 0 }) return null;
			var g = u.Gates[route[0]].Position;
			world = new Vector3(g.X, 0, g.Y);
		}
		if (_cam.IsPositionBehind(world)) return null;
		return _cam.UnprojectPosition(world);
	}

	void UpdateDockButton()
	{
		_hud.DockButton.Visible = NearestStation(out _) != null;
	}

	StationId? NearestStation(out float distance)
	{
		StationId? best = null; distance = 55f;
		var u = _session.U;
		foreach (var sid in u.Sectors[u.PlayerSector].Stations)
		{
			var st = u.Stations[sid];
			float d = new Vector2(_player.Position.X - st.Position.X, _player.Position.Z - st.Position.Y).Length();
			if (d < distance) { distance = d; best = sid; }
		}
		return best;
	}

	void DockAtNearest() { if (NearestStation(out _) is { } sid) Dock(sid); }

	/// <summary>Andocken: Das Schiff fliegt in die Station hinein und verschwindet (unsichtbar, keine Kollision); die Hülle wird repariert.</summary>
	void Dock(StationId id)
	{
		var u = _session.U;
		var st = u.Stations[id];
		_docked = id;
		_transition = true;
		_player.Velocity = Vector3.Zero;
		_miningStep = null;

		var center = new Vector3(st.Position.X, 0, st.Position.Y);
		var tw = CreateTween();
		tw.TweenProperty(_player, "position", center, 0.7);
		tw.Parallel().TweenProperty(_player, "scale", Vector3.One * 0.1f, 0.7);
		tw.TweenCallback(Callable.From(() => { _player.Visible = false; _transition = false; }));

		bool repaired = PlayerData.Hull < Ship.MaxHull - 0.5f;
		PlayerData.Hull = Ship.MaxHull;
		_sfx.Play(SfxId.Dock);
		_hud.Dock.Open(u, st, PlayerData);
		if (repaired) _hud.Toast("Hülle repariert");
		if (_miner.Enabled)
		{
			var sale = _miner.OnDocked(u, PlayerData, st);
			if (sale is { Revenue: > 0 }) { _sfx.Play(SfxId.Pickup); _hud.Toast($"Verkauft für {sale.Revenue:0} Credits"); }
			_hud.Dock.Open(u, st, PlayerData);
			// Nach kurzer Pause automatisch abdocken und weiterarbeiten
			var timer = GetTree().CreateTimer(1.8);
			timer.Timeout += () => { if (_docked == id) Undock(); };
			if (!_miner.Enabled) { _hud.AutoMineButton.On = false; _hud.Toast(_miner.Status, 3.5f); }
		}
	}

	/// <summary>Abdocken: Das Schiff taucht aus der Station auf und fliegt zum Parkpunkt; danach kurze Schonfrist ohne Schaden.</summary>
	void Undock()
	{
		if (_docked is not { } id || !_session.U.Stations.TryGetValue(id, out var st)) return;
		_sfx.Play(SfxId.Click);
		_docked = null;
		_transition = true;
		_hud.Dock.Visible = false;

		var spot = st.DockPoint(_session.U.PlayerShip);
		_player.Position = new Vector3(st.Position.X, 0, st.Position.Y);
		_player.Scale = Vector3.One * 0.1f;
		_player.Visible = true;
		var outward = new Vector3(spot.X - st.Position.X, 0, spot.Y - st.Position.Y).Normalized();
		_player.Heading = Mathf.Atan2(-outward.X, -outward.Z);

		var tw = CreateTween();
		tw.TweenProperty(_player, "position", new Vector3(spot.X, 0, spot.Y), 0.7);
		tw.Parallel().TweenProperty(_player, "scale", Vector3.One, 0.7);
		tw.TweenCallback(Callable.From(() => { _transition = false; _player.Velocity = outward * 8f; _player.GiveGrace(2.5f); }));
	}

	// ───────── Zeiger in der Welt ─────────

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_hud == null || _hud.AnyModalOpen) return;
		if (@event is InputEventKey { Pressed: true, Echo: false } key)
		{
			if (key.Keycode == Key.F) _fireRequested = true;
			else if (key.Keycode == Key.G) { _tractorOn = !_tractorOn; _hud.TractorButton.On = _tractorOn; _sfx.Play(SfxId.Click); }
			else if (key.Keycode == Key.M) SetAutoMining(!_miner.Enabled);
			return;
		}
		if (!Pointer.TryRead(@event, out int idx, out var pos, out bool pressed, out bool motion)) return;

		if (!motion && pressed && _pointerFinger < 0)
		{
			_pointerFinger = idx; _pointerPos = _pointerStart = pos;
			_pointerDownAt = Time.GetTicksMsec() / 1000.0;
		}
		else if (idx == _pointerFinger && motion) _pointerPos = pos;
		else if (idx == _pointerFinger && !pressed)
		{
			bool tap = Time.GetTicksMsec() / 1000.0 - _pointerDownAt < 0.25 && pos.DistanceTo(_pointerStart) < 20f;
			_pointerFinger = -1;
			if (tap) TryPickTarget(pos);
		}
	}

	Vector3? GroundPoint(Vector2 screen)
	{
		var from = _cam.ProjectRayOrigin(screen);
		var dir = _cam.ProjectRayNormal(screen);
		if (Mathf.Abs(dir.Y) < 0.001f) return null;
		return from + dir * (-from.Y / dir.Y);
	}

	void TryPickTarget(Vector2 screen)
	{
		if (GroundPoint(screen) is not { } ground) return;
		var sector = _session.U.Sectors[_session.U.PlayerSector];
		Asteroid? rock = null; float rockD = 3f;
		foreach (var a in sector.Asteroids)
		{
			float d = new Vector2(ground.X - a.Position.X, ground.Z - a.Position.Y).Length() - a.Radius;
			if (d < rockD) { rockD = d; rock = a; }
		}
		if (rock != null)
		{
			if (rock.Minable) { _mineTarget = rock; _sfx.Play(SfxId.Click); _hud.Toast($"Laserziel gesetzt (Erz {rock.Ore:0}) – jeder Tipp auf Feuer (Taste F) ist ein Schuss"); }
			else _hud.Toast("Kein Erz in diesem Asteroiden");
			return;
		}
		var best = _view.Targets.Where(t => t.Position.DistanceTo(ground) < 22f).OrderBy(t => t.Position.DistanceTo(ground)).FirstOrDefault();
		if (best.Label == null) return;
		var u = _session.U;
		if (best.Kind == "station")
		{
			var sid = new StationId(best.Id);
			SetTarget(Navigator.ForStation(u, sid, u.PlayerShip), sid);
		}
		else
		{
			var gate = u.Gates[new GateId(best.Id)];
			SetTarget(Navigator.ForSector(u, u.Gates[gate.Target].Sector, u.PlayerShip), null);
		}
	}

	// ───────── Rückmeldung ─────────

	/// <summary>Harter Aufprall auf Asteroid/Station (Schwelle <see cref="PlayerShip.HardImpact"/>); leichte Stöße prallen nur ab.</summary>
	void OnPlayerHit(float impact, Vector3 point)
	{
		_hitCount++;
		var uu = _session.U;
		var near = _view.Obstacles.OrderBy(o => new Vector2(point.X - o.Position.X, point.Z - o.Position.Y).Length() - o.Radius).First();
		_lastHit = $"Aufprall {impact:0.0} u/s bei ({point.X:0},{point.Z:0}) in {uu.Sectors[uu.PlayerSector].Name}, Tempo {_player.Velocity.Length():0}, " +
		           $"Auto-Mining={_miner.Enabled} ({_miner.Status}), Hindernis r={near.Radius:0.0} bei ({near.Position.X:0},{near.Position.Y:0}), Grace={_player.Grace:0.0}, Step={(_miningStep is { } ms ? $"nav={ms.Nav?.Label} wp={ms.Waypoint?.X:0},{ms.Waypoint?.Y:0} thr={ms.Throttle:0.0} fire={ms.Fire}" : "keiner")}, Autopilot={_autopilot}, Ziel={_target?.Label}, Transition={_transition}, Docked={_docked}, Clear={ClearanceScale():0.00}";
		_lowestHull = Mathf.Min(_lowestHull, PlayerData.Hull);
		PlayerData.Hull = Mathf.Max(0f, PlayerData.Hull - (impact - PlayerShip.HardImpact) * 0.5f - 2f);
		_sinceHit = 0f;
		_hitFlash = 0.4f;
		_sfx.Play(SfxId.Hit);
		_fx.Burst(point, new Color(1f, 0.7f, 0.3f), 14, 14f);
		_hud.Flash(new Color(1f, 0.2f, 0.1f), 0.35f);
		if (PlayerData.Hull <= 0f) Respawn();
	}

	/// <summary>Hülle zerstört: Explosion, Reparatur und Neustart an der Station des Sektors (mit Schonfrist).</summary>
	void Respawn()
	{
		var u = _session.U;
		_fx.Burst(_player.Position, new Color(1f, 0.5f, 0.2f), 60, 28f, 1.2f);
		_sfx.Play(SfxId.Explosion);
		var sector = u.Sectors[u.PlayerSector];
		var spot = sector.Stations.Count > 0 ? u.Stations[sector.Stations[0]].DockPoint(u.PlayerShip) : new Vec2(0, 0);
		_player.Position = new Vector3(spot.X, 0, spot.Y);
		_player.Velocity = Vector3.Zero;
		_player.GiveGrace(3f);
		PlayerData.Hull = Ship.MaxHull;
		_chunks.Clear();
		CancelAutopilot();
		if (_miner.Enabled) SetAutoMining(false, "Auto-Mining unterbrochen (Schiff zerstört)");
		_hud.Toast("Schiff zerstört – repariert an der Station", 3.5f);
	}

	/// <summary>
	/// Ein Spielerschuss ist gefallen: Ton, Laserblitz, bei Treffer Funken – und Rückstoß: Das Schiff wird
	/// vom Asteroiden weggestoßen (echte Geschwindigkeitsänderung, deshalb bremst die Automatik im Haltemodus aktiv).
	/// </summary>
	void OnShot(Asteroid rock, LaserShot.Result result)
	{
		float units = result.Units;
		var pos = new Vec2(_player.Position.X, _player.Position.Z);
		var toShip = (pos - rock.Position).Normalized();
		var surface = new Vector3(rock.Position.X + toShip.X * rock.Radius, 0, rock.Position.Y + toShip.Y * rock.Radius);

		_sfx.Play(SfxId.Zap);
		_mining.FirePlayer(surface, Time.GetTicksMsec() / 1000.0);
		_player.Velocity += new Vector3(toShip.X, 0, toShip.Y) * MiningRules.RecoilImpulse;
		if (units > 0f) _fx.Burst(surface, GoodColor(result.Good), 5 + (int)units, 7f, 0.5f);
	}

	Color GoodColor(GoodId good) => _session.U.Goods.TryGetValue(good, out var g) ? Color.FromHtml(g.Color) : new Color(1f, 0.75f, 0.3f);

	/// <summary>Ein NPC-Miner hat laut Simulation geschossen. Im Spielersektor zeigen wir Blitz, fliegenden Brocken und Rückstoß.</summary>
	void OnMinerShot(MinerShot shot)
	{
		var shipPos = _view.ShipPosition(shot.Ship);
		if (shipPos is not { } sp) return;
		var rockPos = new Vector3(shot.AsteroidPosition.X, 0, shot.AsteroidPosition.Y);
		var away = (sp - rockPos).Normalized();
		var surface = rockPos + away * shot.AsteroidRadius;

		_mining.NpcShot(shot.Ship, surface, shot.Units, shot.Good, Time.GetTicksMsec() / 1000.0);
		_view.Kick(shot.Ship, away * (MiningRules.RecoilImpulse * 0.4f));   // Rückstoß, verhältnismäßig zum Spielerschiff
		if (shot.Units > 0f) _fx.Burst(surface, GoodColor(shot.Good), 4 + (int)shot.Units, 6f, 0.4f);
	}

	void UpdateHud()
	{
		var u = _session.U;
		string status = $"{u.Sectors[u.PlayerSector].Name}   Tick {u.Tick}   Speed {(int)_player.Velocity.Length()}   Credits {(int)PlayerData.Credits}";
		string? target = null;
		if (_target is { } t)
		{
			float d = t.Sector == u.PlayerSector
				? new Vector2(_player.Position.X - t.Position.X, _player.Position.Z - t.Position.Y).Length()
				: float.NaN;
			target = float.IsNaN(d) ? $"Ziel: {t.Label} (anderer Sektor)" : $"Ziel: {t.Label}  {d:0} m";
		}
		float used = PlayerData.UsedVolume(u);
		string cargo = $"Fracht {used:0}/{PlayerData.CargoCapacity:0}" +
			(PlayerData.Cargo.Count > 0 ? "  (" + string.Join(", ", PlayerData.Cargo.Select(kv => $"{u.Goods[kv.Key].Name} {kv.Value:0}")) + ")" : "");
		string? mining = _miner.Enabled ? "Auto-Mining: " + _miner.Status : _tractorOn ? "Traktorstrahl an" : null;
		if (_mineTarget is { } rock) mining = (mining == null ? "" : mining + "   ") + $"Ziel-Asteroid: Erz {rock.Ore:0}/{rock.MaxOre:0}";
		_hud.SetStatus(status, PlayerData.Hull, target, cargo, mining);
	}

	// ───────── Statische Szene ─────────

	void BuildEnvironment()
	{
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.01f, 0.01f, 0.04f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.25f, 0.28f, 0.4f),
			GlowEnabled = true,
		};
		AddChild(new WorldEnvironment { Environment = env });
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0), LightEnergy = 1.3f });
	}

	void BuildStars()
	{
		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.5f) },
			InstanceCount = 500,
		};
		var rng = new RandomNumberGenerator { Seed = 42 };
		for (int i = 0; i < 500; i++)
		{
			float s = rng.RandfRange(0.4f, 1.6f);
			mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(s, s, s)),
				new Vector3(rng.RandfRange(-400, 400), rng.RandfRange(-40, -8), rng.RandfRange(-400, 400))));
		}
		AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = Gfx.Mat(new Color(0.9f, 0.95f, 1f), 2f) });
	}

	// ───────── Abbau ─────────

	void SetAutoMining(bool on, string? message = null, bool announce = true)
	{
		if (on)
		{
			CancelAutopilot(); _target = null; _targetStation = null;
			_miner.Start();
			_hud.AutoMineButton.On = true;
			if (announce) _hud.Toast("Auto-Mining an – Stick bewegen zum Abbrechen");
		}
		else
		{
			if (_miner.Enabled) _miner.Stop(message ?? "");
			_hud.AutoMineButton.On = false;
			if (announce && message != null) _hud.Toast(message);
		}
	}

	void Hint(string text)
	{
		if (_hintCooldown > 0f) return;
		_hintCooldown = 1.5f;
		_hud.Toast(text, 1.5f);
	}

	/// <summary>Laser und Traktorstrahl des Spielers (manuell oder durch den Automodus), Brocken, Strahldarstellung, NPC-Laser.</summary>
	void UpdateMining(float delta, bool frozen)
	{
		var u = _session.U;
		var ship = PlayerData;
		var sector = u.Sectors[u.PlayerSector];
		var pos = new Vec2(_player.Position.X, _player.Position.Z);
		_hintCooldown = Mathf.Max(0f, _hintCooldown - delta);
		if (_mineTarget != null && !_chunks.IsMinable(sector, _mineTarget)) _mineTarget = null;

		bool fire = false, tractor = _tractorOn;
		Asteroid? aim = null;
		if (!frozen)
		{
			if (_miningStep is { } ms) { fire = ms.Fire; tractor |= ms.Tractor; aim = ms.Aim; }   // Automatik: feuert, sobald nachgeladen
			else
			{
				fire = _fireRequested;   // Spieler: ein Tipp / Tastendruck = ein Schuss
				aim = _mineTarget ?? _chunks.Nearest(sector, pos, MiningRules.LaserRange);
			}
		}
		_fireRequested = false;

		if (fire)
		{
			if (aim == null) Hint("Kein abbaubarer Asteroid in Reichweite");
			else switch (_chunks.TryFire(u, ship, sector, aim, pos))
			{
				case MineResult.OutOfRange: Hint("Zu weit entfernt – näher heranfliegen"); break;
				case MineResult.CargoFull: Hint("Laderaum voll"); break;
				case MineResult.NotMinable: Hint("Kein Erz mehr in diesem Asteroiden"); break;
				// Fired: OnShot erledigt Ton, Blitz und Rückstoß; Cooldown: Waffe lädt noch – stillschweigend ignorieren
			}
		}

		tractor &= !frozen;
		if (_chunks.Update(u, ship, pos, tractor, delta) > 0f) _sfx.Play(SfxId.Pickup);
		_hud.LaserButton.On = _chunks.Ready;   // grün = nachgeladen

		var shown = fire && aim != null ? aim : _mineTarget;
		if (shown != null && sector.Asteroids.Contains(shown))
			_mining.SetTarget(new Vector3(shown.Position.X, 0, shown.Position.Y), shown.Radius);
		else _mining.SetTarget(null, 0f);

		double time = Time.GetTicksMsec() / 1000.0;
		_mining.UpdatePlayer(_player.Position, tractor, _chunks.Chunks, time);
		_mining.UpdateNpc(_view.ShipPosition, time);
	}

	void SellAllHere()
	{
		if (_docked is not { } id || !_session.U.Stations.TryGetValue(id, out var st)) return;
		var result = TradeActions.SellAll(_session.U, PlayerData, st);
		_sfx.Play(result.Revenue > 0 ? SfxId.Pickup : SfxId.Click);
		_hud.Toast(result.Revenue > 0 ? $"Verkauft für {result.Revenue:0} Credits" : "Hier gibt es nichts zu verkaufen");
		_hud.Dock.Open(_session.U, st, PlayerData);
	}

	// ───────── Zugriff für den Selbsttest ─────────

	internal GameSession Session => _session;
	internal Hud HudRef => _hud;
	internal PlayerShip Player => _player;
	internal SectorView View => _view;
	internal bool Docked => _docked != null;
	internal void TestSetStationTarget(StationId id) { var u = _session.U; SetTarget(Navigator.ForStation(u, id, u.PlayerShip), id); }
	internal void TestUndock() => Undock();
	internal bool TestSave() { SaveNow(manual: false); return SaveService.Exists; }
	internal void TestMenu(string a) => OnMenu(a);
	internal void TestStartMining() => SetAutoMining(true);
	internal float TestCargoUnits => PlayerData.Cargo.Values.Sum();
	internal int TestSales => _sales;
	internal int TestHits => _hitCount;
	internal string TestLastHit => _lastHit;
	internal float TestLowestHull => _lowestHull;
	internal bool TestMinerEnabled => _miner.Enabled;
	internal string TestMinerStatus => _miner.Status;
}
